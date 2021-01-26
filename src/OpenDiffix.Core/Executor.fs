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

let rec execute connection plan =
  match plan with
  | Plan.Scan table -> executeScan connection table
  | Plan.Project (plan, expressions) -> plan |> execute connection |> executeProject expressions
  | Plan.Filter (plan, condition) -> plan |> execute connection |> executeFilter condition
  | Plan.Sort (plan, expressions) -> plan |> execute connection |> executeSort expressions
  | _ -> failwith "Plan execution not implemented"
