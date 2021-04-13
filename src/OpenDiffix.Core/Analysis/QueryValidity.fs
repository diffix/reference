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

let private allowedCountUsage aidColIdx query =
  query
  |> onAggregates
       (function
       | FunctionExpr (AggregateFunction (Count, _), args) ->
           match args with
           | [] -> ()
           | [ ColumnReference (index, _) ] when index = aidColIdx -> ()
           | _ -> failwith "Only count(*) and count(distinct aid-column) are supported"
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

let private allowedAggregate aidColIdx (query: AnalyzerTypes.Query) =
  validateOnlyCount query
  allowedCountUsage aidColIdx query
  validateSelectTarget query

let validateQuery aidColIdx (query: AnalyzerTypes.Query) : Result<unit, string> =
  try
    allowedAggregate aidColIdx query
    Ok()
  with exn -> Error exn.Message
