module OpenDiffix.Core.QueryValidator

open AnalyzerTypes
open NodeUtils

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

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

/// Validates a top-level anonymizing query.
let validateQuery (query: SelectQuery) =
  validateOnlyCount query
  allowedCountUsage query
  validateSelectTarget query
