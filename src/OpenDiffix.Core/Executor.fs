module rec OpenDiffix.Core.Executor

open System
open PlannerTypes

let private filter context condition rows =
  rows
  |> Seq.filter (fun row -> condition |> Expression.evaluate context row |> Value.unwrapBoolean)

let private unpackAggregator =
  function
  | FunctionExpr (AggregateFunction _ as fn, args) -> fn, args
  | _ -> failwith "Expression is not an aggregator"

let private unpackAggregators aggregators =
  aggregators |> Array.map unpackAggregator |> Array.unzip

// ----------------------------------------------------------------
// Node execution
// ----------------------------------------------------------------

let private executeScan context table = context.DataProvider.OpenTable(table)

let private executeProject context (childPlan, expressions) : seq<Row> =
  let expressions = Array.ofList expressions

  childPlan
  |> execute context
  |> Seq.map (fun row -> expressions |> Array.map (Expression.evaluate context row))

let private executeProjectSet context (childPlan, fn, args) : seq<Row> =
  childPlan
  |> execute context
  |> Seq.collect
       (fun row ->
         let args = args |> List.map (Expression.evaluate context row)

         Expression.evaluateSetFunction fn args
         |> Seq.map (fun value -> Array.append row [| value |]))

let private executeFilter context (childPlan, condition) : seq<Row> =
  childPlan |> execute context |> filter context condition

let private executeSort context (childPlan, orderings) : seq<Row> =
  childPlan |> execute context |> Expression.sortRows context orderings

let private executeLimit context (childPlan, amount) : seq<Row> =
  if amount > uint System.Int32.MaxValue then
    failwith "`LIMIT` amount is greater than supported range"

  childPlan |> execute context |> Seq.truncate (int amount)

let private executeAggregate context (childPlan, groupingLabels, aggregators) : seq<Row> =
  let groupingLabels = Array.ofList groupingLabels
  let aggFns, aggArgs = aggregators |> Array.ofList |> unpackAggregators
  let defaultAggregators = aggFns |> Array.map (Aggregator.create context (Array.isEmpty groupingLabels))

  let state = Collections.Generic.Dictionary<Row, Aggregator.T array>(Row.equalityComparer)

  if groupingLabels.Length = 0 then state.Add([||], defaultAggregators)

  for row in execute context childPlan do
    let group = groupingLabels |> Array.map (Expression.evaluate context row)
    let argEvaluator = Expression.evaluate context row

    let aggregators =
      match state.TryGetValue(group) with
      | true, aggregators -> aggregators
      | false, _ -> defaultAggregators
      |> Array.zip aggArgs
      |> Array.map (fun (args, aggregator) -> args |> List.map argEvaluator |> aggregator.Transition)

    state.[group] <- aggregators

  state
  |> Seq.map
       (fun pair ->
         let values = pair.Value |> Array.map (fun acc -> acc.Final context)
         Array.append pair.Key values)

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
  |> Seq.collect
       (fun outerRow ->
         let joinedRows = innerRows |> List.map (rowJoiner outerRow) |> filter context on

         if isOuterJoin && Seq.isEmpty joinedRows then
           let nullInnerRow = Array.create innerColumnsCount Null
           seq { rowJoiner outerRow nullInnerRow }
         else
           joinedRows)

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let rec execute context plan : seq<Row> =
  match plan with
  | Plan.Scan table -> executeScan context table
  | Plan.Project (plan, expressions) -> executeProject context (plan, expressions)
  | Plan.ProjectSet (plan, fn, args) -> executeProjectSet context (plan, fn, args)
  | Plan.Filter (plan, condition) -> executeFilter context (plan, condition)
  | Plan.Sort (plan, orderings) -> executeSort context (plan, orderings)
  | Plan.Aggregate (plan, labels, aggregators) -> executeAggregate context (plan, labels, aggregators)
  | Plan.Join (leftPlan, rightPlan, joinType, on) -> executeJoin context (leftPlan, rightPlan, joinType, on)
  | Plan.Limit (plan, amount) -> executeLimit context (plan, amount)
  | _ -> failwith "Plan execution not implemented"
