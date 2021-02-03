module OpenDiffix.Core.Analysis.QueryValidity

open Aether
open Aether.Operators
open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

let onAggregates (f: Expression -> unit) query =
  query
  |> Query.mapQuery (fun selectQuery ->
    selectQuery
    |> SelectQuery.mapExpressions
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

let rec private validateSingleTableSelect query =
  query
  |> Query.mapQuery (fun query ->
    query
    |> Optic.get (SelectQuery._From >-> SelectFrom._table)
    |> function
    | None -> failwith "JOIN queries and sub queries are not supported at present"
    | Some _ -> ()
    query
  )
  |> ignore

let private allowedAggregate aidColIdx (query: AnalyzerTypes.Query) =
  validateOnlyCount query
  allowedCountUsage aidColIdx query
  validateSingleTableSelect query

let validateQuery aidColIdx (query: AnalyzerTypes.Query): Result<unit, string> =
  try
    allowedAggregate aidColIdx query
    Ok()
  with exn -> Error exn.Message
