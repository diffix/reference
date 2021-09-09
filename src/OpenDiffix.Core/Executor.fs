module rec OpenDiffix.Core.Executor

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

let private executeProject context (childPlan, expressions) : Rows =
  let expressions = Array.ofList expressions

  childPlan
  |> execute context
  |> Seq.map (fun row -> expressions |> Array.map (Expression.evaluate context row) |> arrayToRow)

let private executeProjectSet context (childPlan, fn, args) : Rows =
  childPlan
  |> execute context
  |> Seq.collect (fun row ->
    let args = args |> List.map (Expression.evaluate context row)

    Expression.evaluateSetFunction fn args
    |> Seq.map (fun value -> Array.append (rowToArray row) [| value |] |> arrayToRow)
  )

let private executeFilter context (childPlan, condition) : Rows =
  childPlan |> execute context |> filter context condition

let private executeSort context (childPlan, orderings) : Rows =
  childPlan
  |> execute context
  // Sorting does multiple passes through the sequence, so we need to make sure the rows are collapsed.
  |> Seq.map (rowToArray >> arrayToRow)
  |> Expression.sortRows context orderings

let private executeLimit context (childPlan, amount) : Rows =
  if amount > uint System.Int32.MaxValue then
    failwith "`LIMIT` amount is greater than supported range"

  childPlan |> execute context |> Seq.truncate (int amount)

let private executeAggregate context (childPlan, groupingLabels, aggregators) : Rows =
  let groupingLabels = Array.ofList groupingLabels
  let aggFns, aggArgs = aggregators |> Array.ofList |> unpackAggregators
  let defaultAggregators = aggFns |> Array.map (Aggregator.create context (Array.isEmpty groupingLabels))

  let initialState : Map<Value array, Aggregator.T array> =
    if groupingLabels.Length = 0 then Map [ [||], defaultAggregators ] else Map []

  childPlan
  |> execute context
  |> Seq.fold
    (fun state row ->
      let group = groupingLabels |> Array.map (Expression.evaluate context row)
      let argEvaluator = Expression.evaluate context row

      let aggregators =
        match Map.tryFind group state with
        | Some aggregators -> aggregators
        | None -> defaultAggregators
        |> Array.zip aggArgs
        |> Array.map (fun (args, aggregator) -> args |> List.map argEvaluator |> aggregator.Transition)

      Map.add group aggregators state
    )
    initialState
  |> Map.toSeq
  |> Seq.map (fun (group, aggregators) ->
    let values = aggregators |> Array.map (fun acc -> acc.Final context)
    Array.append group values |> arrayToRow
  )

let private executeJoin context (leftPlan, rightPlan, joinType, on) : Rows =
  let isOuterJoin, outerPlan, innerPlan, arrayJoiner =
    match joinType with
    | ParserTypes.InnerJoin -> false, leftPlan, rightPlan, Array.append
    | ParserTypes.LeftJoin -> true, leftPlan, rightPlan, Array.append
    | ParserTypes.RightJoin -> true, rightPlan, leftPlan, (fun a b -> Array.append b a)
    | ParserTypes.FullJoin -> failwith "`FULL JOIN` execution not implemented"

  let innerRows = innerPlan |> execute context |> Seq.map rowToArray |> Seq.toList
  let innerColumnsCount = Plan.columnsCount innerPlan

  outerPlan
  |> execute context
  |> Seq.collect (fun outerRow ->
    let outerRow = rowToArray outerRow

    let joinedRows =
      innerRows
      |> List.map (fun innerRow -> arrayJoiner outerRow innerRow |> arrayToRow)
      |> filter context on

    if isOuterJoin && Seq.isEmpty joinedRows then
      let nullInnerRow = Array.create innerColumnsCount Null
      seq { arrayJoiner outerRow nullInnerRow |> arrayToRow }
    else
      joinedRows
  )

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let rec execute context plan : Rows =
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
