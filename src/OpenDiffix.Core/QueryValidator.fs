module rec OpenDiffix.Core.QueryValidator

open AnalyzerTypes
open NodeUtils

let private validateOnlyCount query =
  query
  |> visitAggregates (
    function
    | FunctionExpr (AggregateFunction (Count, _), _) -> ()
    | FunctionExpr (AggregateFunction (_otherAggregate, _), _) -> failwith "Only count aggregates are supported"
    | _ -> ()
  )

let private allowedCountUsage query =
  query
  |> visitAggregates (
    function
    | FunctionExpr (AggregateFunction (Count, _), args) ->
      match args with
      | []
      | [ ColumnReference _ ] -> ()
      | _ -> failwith "Only count(*) and count(distinct column) are supported"
    | _ -> ()
  )

let private validateSubQuery query =
  let selectQuery = Query.assertSelectQuery query

  selectQuery
  |> visitAggregates (fun _ -> failwith "Aggregates in subqueries are not currently supported")

  if selectQuery.GroupingSets <> [ GroupingSet [] ] then
    failwith "Grouping in subqueries is not currently supported"

  validateSelectTarget selectQuery
  validateLimitUsage selectQuery

let validateSelectTarget selectQuery =
  let rec rangeVisitor range =
    match range with
    | SubQuery (subQuery, _alias) -> validateSubQuery subQuery
    | Join join -> join |> visit rangeVisitor
    | RangeTable _ -> ()

  selectQuery |> visit rangeVisitor

let private validateLimitUsage selectQuery =
  if selectQuery.Limit <> None then
    failwith "Limit is not allowed in anonymizing subqueries"

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

/// Validates a top-level anonymizing query.
let validateQuery (selectQuery: SelectQuery) =
  validateOnlyCount selectQuery
  allowedCountUsage selectQuery
  validateSelectTarget selectQuery
