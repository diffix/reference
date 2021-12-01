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

  let addValuesToSeed seed values =
    values |> Seq.map Value.toString |> Hash.strings seed

// ----------------------------------------------------------------
// Node execution
// ----------------------------------------------------------------

let private executeScan (executionContext: ExecutionContext) table columnIndices =
  executionContext.DataProvider.OpenTable(table, columnIndices)

let private executeProject executionContext (childPlan, expressions) : seq<Row> =
  let expressions = Array.ofList expressions

  childPlan
  |> execute executionContext
  |> Seq.map (fun row -> expressions |> Array.map (Expression.evaluate row))

let private executeProjectSet executionContext (childPlan, fn, args) : seq<Row> =
  childPlan
  |> execute executionContext
  |> Seq.collect (fun row ->
    let args = args |> List.map (Expression.evaluate row)

    Expression.evaluateSetFunction fn args
    |> Seq.map (fun value -> Array.append row [| value |])
  )

let private executeFilter executionContext (childPlan, condition) : seq<Row> =
  childPlan |> execute executionContext |> Utils.filter condition

let private executeSort executionContext (childPlan, orderings) : seq<Row> =
  childPlan |> execute executionContext |> Expression.sortRows orderings

let private executeLimit executionContext (childPlan, amount) : seq<Row> =
  if amount > uint Int32.MaxValue then
    failwith "`LIMIT` amount is greater than supported range"

  childPlan |> execute executionContext |> Seq.truncate (int amount)

let private executeAggregate executionContext (childPlan, groupingLabels, aggregators) : seq<Row> =
  let groupingLabels = Array.ofList groupingLabels
  let isGlobal = Array.isEmpty groupingLabels
  let aggregators = Utils.unpackAggregators aggregators
  let aggSpecs, aggArgs = Array.unzip aggregators

  let makeBucket group executionContext =
    Bucket.make group (aggSpecs |> Array.map (Aggregator.create executionContext isGlobal)) executionContext

  let state = Dictionary<Row, Bucket>(Row.equalityComparer)

  if isGlobal then
    let emptyGroup = [||]
    state.Add(emptyGroup, makeBucket emptyGroup executionContext)

  for row in execute executionContext childPlan do
    let argEvaluator = Expression.evaluate row
    let group = groupingLabels |> Array.map argEvaluator

    let bucket =
      match state.TryGetValue(group) with
      | true, aggregators -> aggregators
      | false, _ ->
        let bucketExecutionContext =
          { executionContext with
              NoiseLayers =
                { executionContext.NoiseLayers with
                    BucketSeed = Utils.addValuesToSeed executionContext.NoiseLayers.BucketSeed group
                }
          }

        let bucket = makeBucket group bucketExecutionContext
        state.[group] <- bucket
        bucket

    bucket.Aggregators
    |> Array.iteri (fun i aggregator -> aggArgs.[i] |> List.map argEvaluator |> aggregator.Transition)

  let aggregationContext =
    {
      ExecutionContext = executionContext
      GroupingLabels = groupingLabels
      Aggregators = aggregators
    }

  let buckets = state |> Seq.map (fun pair -> pair.Value)

  executionContext.QueryContext.PostAggregationHooks
  |> List.fold (fun buckets hook -> hook aggregationContext buckets) buckets
  |> Seq.map (fun bucket ->
    Array.append bucket.Group (bucket.Aggregators |> Array.map (fun agg -> agg.Final bucket.ExecutionContext))
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

let execute executionContext plan : seq<Row> =
  match plan with
  | Plan.Scan (table, columnIndices) -> executeScan executionContext table columnIndices
  | Plan.Project (plan, expressions) -> executeProject executionContext (plan, expressions)
  | Plan.ProjectSet (plan, fn, args) -> executeProjectSet executionContext (plan, fn, args)
  | Plan.Filter (plan, condition) -> executeFilter executionContext (plan, condition)
  | Plan.Sort (plan, orderings) -> executeSort executionContext (plan, orderings)
  | Plan.Aggregate (plan, labels, aggregators) -> executeAggregate executionContext (plan, labels, aggregators)
  | Plan.Join (leftPlan, rightPlan, joinType, on) -> executeJoin executionContext (leftPlan, rightPlan, joinType, on)
  | Plan.Limit (plan, amount) -> executeLimit executionContext (plan, amount)
  | _ -> failwith "Plan execution not implemented"
