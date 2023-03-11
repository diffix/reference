module rec OpenDiffix.Core.Executor

open System

module Utils =
  let filter condition rows =
    rows
    |> Seq.filter (fun row -> condition |> Expression.evaluate row |> Value.unwrapBoolean)

  let unpackAggregator =
    function
    | FunctionExpr(AggregateFunction(fn, opts), args) -> ((fn, opts), args)
    | _ -> failwith "Expression is not an aggregator"

  let unpackAggregators aggregators =
    aggregators |> Seq.map unpackAggregator |> Seq.toArray

// ----------------------------------------------------------------
// Node execution
// ----------------------------------------------------------------

let private executeScan (queryContext: QueryContext) table columnIndices =
  queryContext.DataProvider.OpenTable(table, columnIndices)

let private executeProject queryContext (childPlan, expressions) : seq<Row> =
  let expressions = Array.ofList expressions

  childPlan
  |> execute queryContext
  |> Seq.map (fun row -> expressions |> Array.map (Expression.evaluate row))

let private executeProjectSet queryContext (childPlan, fn, args) : seq<Row> =
  childPlan
  |> execute queryContext
  |> Seq.collect (fun row ->
    let args = args |> List.map (Expression.evaluate row)

    Expression.evaluateSetFunction fn args
    |> Seq.map (fun value -> Array.append row [| value |])
  )

let private executeFilter queryContext (childPlan, condition) : seq<Row> =
  childPlan |> execute queryContext |> Utils.filter condition

let private executeSort queryContext (childPlan, orderings) : seq<Row> =
  childPlan |> execute queryContext |> Expression.sortRows orderings

let private executeLimit queryContext (childPlan, amount) : seq<Row> =
  if amount > uint Int32.MaxValue then
    failwith "`LIMIT` amount is greater than supported range"

  childPlan |> execute queryContext |> Seq.truncate (int amount)

let private accumulateAbs accumulator value =
  match value with
  | Integer value -> accumulator + abs (float value)
  | Real value -> accumulator + abs value
  | _ -> accumulator

// Returns a function that proportionally redistributes the aggregated outliers data over the individual values in a column.
let private makeOutliersRedistributor total outliersAggregate =
  match total > 0.0, outliersAggregate with
  | true, Integer outliersAggregate ->
    function
    | Integer i ->
      let tweakFactor = 1.0 + float outliersAggregate / total
      Integer(int64 (Math.roundAwayFromZero (float i * tweakFactor)))
    | Null -> Null
    | _ -> failwith "Unexpected value type in `integer` column."
  | true, Real outliersAggregate ->
    function
    | Real r ->
      let tweakFactor = 1.0 + float outliersAggregate / total
      Real(r * tweakFactor)
    | Null -> Null
    | _ -> failwith "Unexpected value type in `real` column."
  | _ -> id

// Recovers dropped outliers data while finalizing aggregated values and proportionally redistributes it over the
// non-suppressed anonymized values, in order to minimize the total distortion per column.
let private finalizeAggregatesAndRedistributeOutliers
  (aggregationContext: AggregationContext)
  anonymizationContext
  buckets
  : Row seq =
  let outliersAggregators = aggregationContext.Aggregators |> Array.map Aggregator.create
  let totals = Array.create aggregationContext.Aggregators.Length 0.0
  let lowCountIndex = AggregationContext.lowCountIndex aggregationContext

  // Finalize aggregated values, gather dropped outliers data and compute column totals.
  let rows =
    buckets
    |> Seq.map (fun bucket ->
      let isLowCount =
        bucket
        |> Bucket.finalizeAggregate lowCountIndex aggregationContext
        |> Value.unwrapBoolean

      let finalizer =
        if isLowCount then
          fun _i (agg: IAggregator) -> agg.Final(aggregationContext, bucket.AnonymizationContext, None)
        else
          fun i (agg: IAggregator) ->
            let value = agg.Final(aggregationContext, bucket.AnonymizationContext, Some outliersAggregators.[i])
            totals.[i] <- accumulateAbs totals.[i] value
            value

      Array.append bucket.Group (Array.mapi finalizer bucket.Aggregators)
    )
    |> Seq.toArray

  // Force global aggregation and anonymization contexts for aggregating outliers.
  let outliersAggregationContext = { aggregationContext with GroupingLabels = [||] }
  let outliersAnonymizationContext = { anonymizationContext with BaseLabels = [] }

  // Create a value tweaker for each column that minimizes the total distortion of that column.
  let outliersRedistributors =
    outliersAggregators
    |> Array.mapi (fun i agg ->
      makeOutliersRedistributor
        totals.[i]
        (agg.Final(outliersAggregationContext, Some outliersAnonymizationContext, None))
    )

  // Apply column tweakers to non-suppressed rows.
  rows
  |> Seq.filter (fun row -> not (Value.unwrapBoolean row.[lowCountIndex + aggregationContext.GroupingLabels.Length]))
  |> Seq.iter (fun row ->
    outliersRedistributors
    |> Array.iteri (fun aggregateIndex outliersRedistributor ->
      let valueIndex = aggregateIndex + aggregationContext.GroupingLabels.Length
      row.[valueIndex] <- outliersRedistributor row.[valueIndex]
    )
  )

  rows

let private finalizeBuckets aggregationContext anonymizationContext buckets =
  // Redistributing outliers require that a `DiffixLowCount` aggregator is present, which only happens during non-global anonymized aggregation.
  match aggregationContext, anonymizationContext with
  | {
      GroupingLabels = groupingLabels
      AnonymizationParams = { RecoverOutliers = true }
    },
    Some anonymizationContext when groupingLabels.Length > 0 ->
    finalizeAggregatesAndRedistributeOutliers aggregationContext anonymizationContext buckets
  | _ -> // finalize aggregates without redistributing outliers
    buckets
    |> Seq.map (fun bucket ->
      Array.append
        bucket.Group
        (bucket.Aggregators
         |> Array.map (fun agg -> agg.Final(aggregationContext, bucket.AnonymizationContext, None)))
    )

let private invokeHooks aggregationContext anonymizationContext hooks buckets =
  // Invoking hooks requires that a `DiffixLowCount` aggregator is present, which only happens during non-global anonymized aggregation.
  match aggregationContext, anonymizationContext with
  | { GroupingLabels = groupingLabels }, Some anonymizationContext when groupingLabels.Length > 0 ->
    List.fold (fun buckets hook -> hook aggregationContext anonymizationContext buckets) buckets hooks
  | _ -> buckets

let private executeAggregate queryContext (childPlan, groupingLabels, aggregators, anonymizationContext) : seq<Row> =
  let groupingLabels = Array.ofList groupingLabels
  let aggregators = Utils.unpackAggregators aggregators
  let _aggSpecs, aggArgs = Array.unzip aggregators

  let makeBucket group anonymizationContext =
    Bucket.make group (aggregators |> Array.map Aggregator.create) anonymizationContext

  let state = Dictionary<Row, Bucket>(Row.equalityComparer)

  if Array.isEmpty groupingLabels then
    let globalGroup = [||]
    state.Add(globalGroup, makeBucket globalGroup anonymizationContext)

  for row in execute queryContext childPlan do
    let argEvaluator = Expression.evaluate row
    let group = groupingLabels |> Array.map argEvaluator

    let bucket =
      match state.TryGetValue(group) with
      | true, aggregators -> aggregators
      | false, _ ->
        let bucket = makeBucket group anonymizationContext
        state.[group] <- bucket
        bucket

    bucket.RowCount <- bucket.RowCount + 1

    bucket.Aggregators
    |> Array.iteri (fun i aggregator -> aggArgs.[i] |> List.map argEvaluator |> aggregator.Transition)

  let aggregationContext =
    {
      AnonymizationParams = queryContext.AnonymizationParams
      GroupingLabels = groupingLabels
      Aggregators = aggregators
    }

  state
  |> Seq.map (fun pair -> pair.Value)
  |> invokeHooks aggregationContext anonymizationContext queryContext.PostAggregationHooks
  |> finalizeBuckets aggregationContext anonymizationContext

let private executeJoin executionContext (leftPlan, rightPlan, joinType, on) =
  let isOuterJoin, outerPlan, innerPlan, rowJoiner =
    match joinType with
    | InnerJoin -> false, leftPlan, rightPlan, Array.append
    | LeftJoin -> true, leftPlan, rightPlan, Array.append
    | RightJoin -> true, rightPlan, leftPlan, (fun a b -> Array.append b a)
    | FullJoin -> failwith "`FULL JOIN` execution not implemented"

  let innerRows = innerPlan |> execute executionContext |> Seq.toList
  let innerColumnsCount = Plan.columnsCount innerPlan

  outerPlan
  |> execute executionContext
  |> Seq.collect (fun outerRow ->
    let joinedRows = innerRows |> List.map (rowJoiner outerRow) |> Utils.filter on

    if isOuterJoin && Seq.isEmpty joinedRows then
      let nullInnerRow = Array.create innerColumnsCount Null
      seq { rowJoiner outerRow nullInnerRow }
    else
      joinedRows
  )

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let execute (queryContext: QueryContext) plan : seq<Row> =
  match plan with
  | Plan.Scan(table, columnIndices) -> executeScan queryContext table columnIndices
  | Plan.Project(plan, expressions) -> executeProject queryContext (plan, expressions)
  | Plan.ProjectSet(plan, fn, args) -> executeProjectSet queryContext (plan, fn, args)
  | Plan.Filter(plan, condition) -> executeFilter queryContext (plan, condition)
  | Plan.Sort(plan, orderings) -> executeSort queryContext (plan, orderings)
  | Plan.Aggregate(plan, labels, aggregators, anonymizationContext) ->
    executeAggregate queryContext (plan, labels, aggregators, anonymizationContext)
  | Plan.Join(leftPlan, rightPlan, joinType, on) -> executeJoin queryContext (leftPlan, rightPlan, joinType, on)
  | Plan.Limit(plan, amount) -> executeLimit queryContext (plan, amount)
  | _ -> failwith "Plan execution not implemented"
