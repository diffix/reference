module OpenDiffix.Core.Executor

open OpenDiffix.Core.PlannerTypes

let private executeScan connection table = table |> Table.load connection |> Async.RunSynchronously |> Utils.unwrap

let private executeProject expressions rowsStream =
  let expressions = Array.ofList expressions

  rowsStream
  |> Seq.map (fun row -> expressions |> Array.map (Expression.evaluate EmptyContext row))

let private executeFilter condition rowsStream =
  rowsStream
  |> Seq.filter (fun row -> condition |> Expression.evaluate EmptyContext row |> Value.isTruthy)

let private executeSort orderings rowsStream = rowsStream |> Expression.sortRows EmptyContext orderings

let private unpackAggregator =
  function
  | FunctionExpr (AggregateFunction _ as fn, args) -> fn, args
  | _ -> failwith "Expression is not an aggregator"

let private unpackAggregators aggregators = aggregators |> Array.map (unpackAggregator) |> Array.unzip

let private executeAggregate groupingLabels aggregators rowsStream =
  let groupingLabels = Array.ofList groupingLabels
  let aggFns, aggArgs = aggregators |> Array.ofList |> unpackAggregators
  let defaultAccumulators = aggFns |> Array.map (Expression.createAccumulator EmptyContext)

  let initialState: Map<Row, Expression.Accumulator array> =
    if groupingLabels.Length = 0 then Map [ [||], defaultAccumulators ] else Map []

  rowsStream
  |> Seq.fold
    (fun state row ->
      let group = groupingLabels |> Array.map (Expression.evaluate EmptyContext row)

      let accumulator =
        match Map.tryFind group state with
        | Some accumulator -> accumulator
        | None -> defaultAccumulators
        |> Array.zip aggArgs
        |> Array.map (fun (args, accumulator) -> accumulator.Process EmptyContext args row)

      Map.add group accumulator state
    )
    initialState
  |> Map.toSeq
  |> Seq.map (fun (group, accumulators) ->
    let values = accumulators |> Array.map (fun acc -> acc.Evaluate)
    Array.append group values
  )

let rec execute connection plan =
  match plan with
  | Plan.Scan table -> executeScan connection table
  | Plan.Project (plan, expressions) -> plan |> execute connection |> executeProject expressions
  | Plan.Filter (plan, condition) -> plan |> execute connection |> executeFilter condition
  | Plan.Sort (plan, expressions) -> plan |> execute connection |> executeSort expressions
  | Plan.Aggregate (plan, labels, aggregators) -> plan |> execute connection |> executeAggregate labels aggregators
  | _ -> failwith "Plan execution not implemented"
