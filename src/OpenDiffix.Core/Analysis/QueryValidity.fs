module OpenDiffix.Core.Analysis.QueryValidity

open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

let onAggregates (f: Expression -> unit) query =
  Query.Map(
    query,
    (function
    | FunctionExpr (AggregateFunction (_fn, _opts), _args) as aggregateExpression ->
        f aggregateExpression
        aggregateExpression
    | other -> other)
  )
  |> ignore

let private assertEmpty query errorMsg seq = if Seq.isEmpty seq then Ok query else Error errorMsg

let private validateOnlyCount query =
  query
  |> onAggregates
       (function
       | FunctionExpr (AggregateFunction (Count, _), _) -> ()
       | FunctionExpr (AggregateFunction (_otherAggregate, _), _) -> failwith "Only count aggregates are supported"
       | _ -> ())

let private allowedCountUsage query =
  query
  |> onAggregates
       (function
       | FunctionExpr (AggregateFunction (Count, _), args) ->
           match args with
           | []
           | [ ColumnReference _ ] -> ()
           | _ -> failwith "Only count(*) and count(distinct column) are supported"
       | _ -> ())

let rec private validateSelectTarget query =
  Query.Map(
    query,
    function
    | Query _ -> failwith "Subqueries are not supported at present"
    | Join _ as j -> j
    | Table _ as t -> t
  )
  |> ignore

let private allowedAggregate (query: AnalyzerTypes.Query) =
  validateOnlyCount query
  allowedCountUsage query
  validateSelectTarget query

let validateQuery (query: AnalyzerTypes.Query) : Result<unit, string> =
  try
    allowedAggregate query
    Ok()
  with exn -> Error exn.Message
