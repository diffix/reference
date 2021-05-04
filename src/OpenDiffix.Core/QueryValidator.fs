module OpenDiffix.Core.QueryValidator

open AnalyzerTypes

let onAggregates (f: Expression -> unit) query =
  query
  |> NodeUtils.map
       (function
       | FunctionExpr (AggregateFunction (_fn, _opts), _args) as aggregateExpression ->
           f aggregateExpression
           aggregateExpression
       | other -> other)
  |> ignore

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
  query
  |> NodeUtils.map
       (function
       | SubQuery _ -> failwith "Subqueries are not supported at present"
       | Join _ as j -> j
       | RangeTable _ as t -> t)
  |> ignore

let private allowedAggregate (query: Query) =
  validateOnlyCount query
  allowedCountUsage query
  validateSelectTarget query

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let validateQuery (query: Query) = allowedAggregate query
