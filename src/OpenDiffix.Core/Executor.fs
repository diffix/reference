module rec OpenDiffix.Core.Executor

open System
open PlannerTypes

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

let private executeScan context table columnIndices =
  context.QueryContext.DataProvider.OpenTable(table, columnIndices)

let private executeProject context (childPlan, expressions) : seq<Row> =
  let expressions = Array.ofList expressions

  childPlan
  |> execute context
  |> Seq.map (fun row -> expressions |> Array.map (Expression.evaluate row))

let private executeProjectSet context (childPlan, fn, args) : seq<Row> =
  childPlan
  |> execute context
  |> Seq.collect (fun row ->
    let args = args |> List.map (Expression.evaluate row)

    Expression.evaluateSetFunction fn args
    |> Seq.map (fun value -> Array.append row [| value |])
  )

let private executeFilter context (childPlan, condition) : seq<Row> =
  childPlan |> execute context |> filter condition

let private executeSort context (childPlan, orderings) : seq<Row> =
  childPlan |> execute context |> Expression.sortRows orderings

let private executeLimit context (childPlan, amount) : seq<Row> =
  if amount > uint Int32.MaxValue then
    failwith "`LIMIT` amount is greater than supported range"

  childPlan |> execute context |> Seq.truncate (int amount)

let private addValuesToSeed seed values =
  values |> Seq.map Value.hash |> Seq.fold (^^^) seed

let private executeAggregate context (childPlan, groupingLabels, aggregators) : seq<Row> =
  let groupingLabels = Array.ofList groupingLabels
  let isGlobal = Array.isEmpty groupingLabels
  let aggFns, aggArgs = aggregators |> Array.ofList |> unpackAggregators

  let makeAggregators () =
    aggFns |> Array.map (Aggregator.create context isGlobal)

  let state = Collections.Generic.Dictionary<Row, Aggregator.T array>(Row.equalityComparer)
  if isGlobal then state.Add([||], makeAggregators ())

  for row in execute context childPlan do
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
    let bucketSeed = addValuesToSeed context.NoiseLayers.BucketSeed pair.Key

    let context =
      { context with
          NoiseLayers = { context.NoiseLayers with BucketSeed = bucketSeed }
      }

    let values = pair.Value |> Array.map (fun acc -> acc.Final context)
    Array.append pair.Key values
  )

let private executeJoin context (leftPlan, rightPlan, joinType, on) =
  let isOuterJoin, outerPlan, innerPlan, rowJoiner =
    match joinType with
    | ParserTypes.InnerJoin -> false, leftPlan, rightPlan, Array.append
    | ParserTypes.LeftJoin -> true, leftPlan, rightPlan, Array.append
    | ParserTypes.RightJoin -> true, rightPlan, leftPlan, (fun a b -> Array.append b a)
    | ParserTypes.FullJoin -> failwith "`FULL JOIN` execution not implemented"

  let innerRows = innerPlan |> execute context |> Seq.toList
  let innerColumnsCount = Plan.columnsCount innerPlan

  outerPlan
  |> execute context
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

let rec execute context plan : seq<Row> =
  match plan with
  | Plan.Scan (table, columnIndices) -> executeScan context table columnIndices
  | Plan.Project (plan, expressions) -> executeProject context (plan, expressions)
  | Plan.ProjectSet (plan, fn, args) -> executeProjectSet context (plan, fn, args)
  | Plan.Filter (plan, condition) -> executeFilter context (plan, condition)
  | Plan.Sort (plan, orderings) -> executeSort context (plan, orderings)
  | Plan.Aggregate (plan, labels, aggregators) -> executeAggregate context (plan, labels, aggregators)
  | Plan.Join (leftPlan, rightPlan, joinType, on) -> executeJoin context (leftPlan, rightPlan, joinType, on)
  | Plan.Limit (plan, amount) -> executeLimit context (plan, amount)
  | _ -> failwith "Plan execution not implemented"
