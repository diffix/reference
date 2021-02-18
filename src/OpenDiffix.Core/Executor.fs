module OpenDiffix.Core.Executor

open OpenDiffix.Core.PlannerTypes

let private executeScan connection table = table |> Table.load connection |> Async.RunSynchronously |> Utils.unwrap

let private executeProject context expressions rowsStream =
  let expressions = Array.ofList expressions

  rowsStream
  |> Seq.map (fun row -> expressions |> Array.map (Expression.evaluate context row))

let private executeProjectSet context setFn args rowsStream =
  rowsStream
  |> Seq.collect (fun row ->
    let args = args |> List.map (Expression.evaluate context row)

    Expression.evaluateSetFunction setFn args
    |> Seq.map (fun value -> Array.append row [| value |])
  )

let private executeFilter context condition rowsStream =
  rowsStream
  |> Seq.filter (fun row -> condition |> Expression.evaluate context row |> Value.unwrapBoolean)

let private executeSort context orderings rowsStream = rowsStream |> Expression.sortRows context orderings

let private unpackAggregator =
  function
  | FunctionExpr (AggregateFunction _ as fn, args) -> fn, args
  | _ -> failwith "Expression is not an aggregator"

let private unpackAggregators aggregators = aggregators |> Array.map (unpackAggregator) |> Array.unzip

let private executeAggregate context groupingLabels aggregators rowsStream =
  let groupingLabels = Array.ofList groupingLabels
  let aggFns, aggArgs = aggregators |> Array.ofList |> unpackAggregators
  let defaultAggregators = aggFns |> Array.map (Aggregator.create context)

  let initialState: Map<Row, IAggregator array> =
    if groupingLabels.Length = 0 then Map [ [||], defaultAggregators ] else Map []

  rowsStream
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
    Array.append group values
  )

let private executeJoin isOuterJoin leftStream rightStream rightColumnsCount context condition =
  let rightRows = Seq.toList rightStream

  leftStream
  |> Seq.collect (fun leftRow ->
    let joinedRows = rightRows |> List.map (Array.append leftRow) |> executeFilter context condition

    if isOuterJoin && Seq.isEmpty joinedRows then
      let nullRightRow = Array.create rightColumnsCount Null
      seq { Array.append leftRow nullRightRow }
    else
      joinedRows
  )

let rec execute connection context plan =
  match plan with
  | Plan.Scan table -> executeScan connection table
  | Plan.Project (plan, expressions) -> plan |> execute connection context |> executeProject context expressions
  | Plan.ProjectSet (plan, fn, args) -> plan |> execute connection context |> executeProjectSet context fn args
  | Plan.Filter (plan, condition) -> plan |> execute connection context |> executeFilter context condition
  | Plan.Sort (plan, expressions) -> plan |> execute connection context |> executeSort context expressions

  | Plan.Aggregate (plan, labels, aggregators) ->
      plan
      |> execute connection context
      |> executeAggregate context labels aggregators

  | Plan.Join (leftPlan, rightPlan, joinType, condition) ->
      let leftStream = execute connection context leftPlan
      let rightStream = execute connection context rightPlan

      let joinExecutor =
        match joinType with
        | AnalyzerTypes.InnerJoin -> executeJoin false leftStream rightStream 0
        | AnalyzerTypes.LeftJoin -> executeJoin true leftStream rightStream (rightPlan.ColumnsCount())
        | AnalyzerTypes.RightJoin -> executeJoin true rightStream leftStream (leftPlan.ColumnsCount())
        | AnalyzerTypes.FullJoin -> failwith "`FULL JOIN` execution not implemented"

      joinExecutor context condition

  | _ -> failwith "Plan execution not implemented"
