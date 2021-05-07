module OpenDiffix.Core.QueryValidator

open AnalyzerTypes
open NodeUtils

let private visitAggregates f query =
  let rec exprVisitor f expr =
    match expr with
    | FunctionExpr (AggregateFunction (_fn, _opts), _args) as aggregateExpression -> f aggregateExpression
    | other -> other |> visit (exprVisitor f)

  query |> visit (exprVisitor f)

let private validateOnlyCount query =
  query
  |> visitAggregates
       (function
       | FunctionExpr (AggregateFunction (Count, _), _) -> ()
       | FunctionExpr (AggregateFunction (_otherAggregate, _), _) -> failwith "Only count aggregates are supported"
       | _ -> ())

let private allowedCountUsage query =
  query
  |> visitAggregates
       (function
       | FunctionExpr (AggregateFunction (Count, _), args) ->
           match args with
           | []
           | [ ColumnReference _ ] -> ()
           | _ -> failwith "Only count(*) and count(distinct column) are supported"
       | _ -> ())

let private validateSelectTarget query =
  let rec rangeVisitor range =
    match range with
    | SubQuery _ -> failwith "Subqueries are not supported at present"
    | Join join -> join |> visit rangeVisitor
    | RangeTable _ -> ()

  query |> visit rangeVisitor

let private allowedAggregate (query: Query) =
  validateOnlyCount query
  allowedCountUsage query
  validateSelectTarget query

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let validateQuery (query: Query) = allowedAggregate query
