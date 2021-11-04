module rec OpenDiffix.Core.Executor

open System

let private filter condition rows =
  rows
  |> Seq.filter (fun row -> condition |> Expression.evaluate row |> Value.unwrapBoolean)

let private unpackAggregator =
  function
  | FunctionExpr (AggregateFunction _ as fn, args) -> fn, args
  | _ -> failwith "Expression is not an aggregator"

let private unpackAggregators aggregators =
  aggregators |> Array.map unpackAggregator |> Array.unzip

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
  childPlan |> execute executionContext |> filter condition

let private executeSort executionContext (childPlan, orderings) : seq<Row> =
  childPlan |> execute executionContext |> Expression.sortRows orderings

let private executeLimit executionContext (childPlan, amount) : seq<Row> =
  if amount > uint Int32.MaxValue then
    failwith "`LIMIT` amount is greater than supported range"

  childPlan |> execute executionContext |> Seq.truncate (int amount)

let private addValuesToSeed seed values =
  values |> Seq.map (Value.toString) |> Hash.strings seed

let private executeAggregate executionContext (childPlan, groupingLabels, aggregators) : seq<Row> =
  let groupingLabels = Array.ofList groupingLabels
  let isGlobal = Array.isEmpty groupingLabels
  let aggFns, aggArgs = aggregators |> Array.ofList |> unpackAggregators

  let makeAggregators () =
    aggFns |> Array.map (Aggregator.create executionContext isGlobal)

  let state = Collections.Generic.Dictionary<Row, Aggregator.T array>(Row.equalityComparer)
  if isGlobal then state.Add([||], makeAggregators ())

  for row in execute executionContext childPlan do
    let group = groupingLabels |> Array.map (Expression.evaluate row)
    let argEvaluator = Expression.evaluate row

    let aggregators =
      match state.TryGetValue(group) with
      | true, aggregators -> aggregators
      | false, _ ->
        let aggregators = makeAggregators ()
        state.[group] <- aggregators
        aggregators

    aggregators
    |> Array.iteri (fun i aggregator -> aggArgs.[i] |> List.map argEvaluator |> aggregator.Transition)

  state
  |> Seq.map (fun pair ->
    let bucketSeed = addValuesToSeed executionContext.NoiseLayers.BucketSeed pair.Key

    let childExecutionContext =
      { executionContext with
          NoiseLayers = { executionContext.NoiseLayers with BucketSeed = bucketSeed }
      }

    let values = pair.Value |> Array.map (fun acc -> acc.Final childExecutionContext)
    Array.append pair.Key values
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
    let joinedRows = innerRows |> List.map (rowJoiner outerRow) |> filter on

    if isOuterJoin && Seq.isEmpty joinedRows then
      let nullInnerRow = Array.create innerColumnsCount Null
      seq { rowJoiner outerRow nullInnerRow }
    else
      joinedRows
  )

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

/// Runs the default execution logic for a plan node.
let executePlanNode executionContext plan : seq<Row> =
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

let execute executionContext plan : seq<Row> =
  match executionContext.QueryContext.ExecutorHook with
  | Some executorHook -> executorHook executionContext plan
  | None -> executePlanNode executionContext plan
