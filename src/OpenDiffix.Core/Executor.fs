module rec OpenDiffix.Core.Executor

open System

module Utils =
  let filter condition rows =
    rows
    |> Seq.filter (fun row -> condition |> Expression.evaluate row |> Value.unwrapBoolean)

  let unpackAggregator =
    function
    | FunctionExpr (AggregateFunction (fn, opts), args) -> ((fn, opts), args)
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

let private invokeHooks aggregationContext anonymizationContext hooks buckets =
  match anonymizationContext with
  | None -> buckets
  | Some anonymizationContext ->
    if aggregationContext.GroupingLabels.Length > 0 then
      List.fold (fun buckets hook -> hook aggregationContext anonymizationContext buckets) buckets hooks
    else
      buckets // don't run hooks for global bucket

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
  |> Seq.map (fun bucket ->
    Array.append
      bucket.Group
      (bucket.Aggregators
       |> Array.map (fun agg -> agg.Final(aggregationContext, bucket.AnonymizationContext)))
  )

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
  | Plan.Scan (table, columnIndices) -> executeScan queryContext table columnIndices
  | Plan.Project (plan, expressions) -> executeProject queryContext (plan, expressions)
  | Plan.ProjectSet (plan, fn, args) -> executeProjectSet queryContext (plan, fn, args)
  | Plan.Filter (plan, condition) -> executeFilter queryContext (plan, condition)
  | Plan.Sort (plan, orderings) -> executeSort queryContext (plan, orderings)
  | Plan.Aggregate (plan, labels, aggregators, anonymizationContext) ->
    executeAggregate queryContext (plan, labels, aggregators, anonymizationContext)
  | Plan.Join (leftPlan, rightPlan, joinType, on) -> executeJoin queryContext (leftPlan, rightPlan, joinType, on)
  | Plan.Limit (plan, amount) -> executeLimit queryContext (plan, amount)
  | _ -> failwith "Plan execution not implemented"
