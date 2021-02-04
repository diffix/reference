module OpenDiffix.Core.Executor

open OpenDiffix.Core.PlannerTypes

let private executeScan connection table = table |> Table.load connection |> Async.RunSynchronously |> Utils.unwrap

let private executeProject context expressions rowsStream =
  let expressions = Array.ofList expressions

  rowsStream
  |> Seq.map (fun row -> expressions |> Array.map (Expression.evaluate context row) |> Row.OfValues)

let private executeFilter context condition rowsStream =
  rowsStream
  |> Seq.filter (fun row -> condition |> Expression.evaluate context row |> Value.isTruthy)

let private executeSort context orderings rowsStream = rowsStream |> Expression.sortRows context orderings

let private unpackAggregator =
  function
  | FunctionExpr (AggregateFunction _ as fn, args) -> fn, args
  | _ -> failwith "Expression is not an aggregator"

let private unpackAggregators aggregators = aggregators |> Array.map (unpackAggregator) |> Array.unzip

let private executeAggregate context groupingLabels aggregators rowsStream =
  let groupingLabels = Array.ofList groupingLabels
  let aggFns, aggArgs = aggregators |> Array.ofList |> unpackAggregators
  let defaultAccumulators = aggFns |> Array.map (Expression.createAccumulator context)

  let initialState: Map<Row, Expression.Accumulator array> =
    if groupingLabels.Length = 0 then Map [ Row.OfValues [||], defaultAccumulators ] else Map []

  rowsStream
  |> Seq.fold
    (fun state row ->
      let group = groupingLabels |> Array.map (Expression.evaluate context row) |> Row.OfValues

      let accumulator =
        match Map.tryFind group state with
        | Some accumulator -> accumulator
        | None -> defaultAccumulators
        |> Array.zip aggArgs
        |> Array.map (fun (args, accumulator) -> accumulator.Process context args row)

      Map.add group accumulator state
    )
    initialState
  |> Map.toSeq
  |> Seq.map (fun (group, accumulators) ->
    let values = accumulators |> Array.map (fun acc -> acc.Evaluate context) |> Row.OfValues
    Row.Append group values
  )

let rec execute connection context plan =
  match plan with
  | Plan.Scan table -> executeScan connection table
  | Plan.Project (plan, expressions) -> plan |> execute connection context |> executeProject context expressions
  | Plan.Filter (plan, condition) -> plan |> execute connection context |> executeFilter context condition
  | Plan.Sort (plan, expressions) -> plan |> execute connection context |> executeSort context expressions
  | Plan.Aggregate (plan, labels, aggregators) ->
      plan
      |> execute connection context
      |> executeAggregate context labels aggregators
  | _ -> failwith "Plan execution not implemented"
