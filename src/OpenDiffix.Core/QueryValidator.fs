module rec OpenDiffix.Core.QueryValidator

open AnalyzerTypes
open NodeUtils

let private validateSingleLowCount query =
  let lowCountAggregators =
    query
    |> collectAggregates
    |> List.filter (
      function
      | FunctionExpr (AggregateFunction (DiffixLowCount, _), _) -> true
      | _ -> false
    )
    |> List.distinct

  if List.length lowCountAggregators > 1 then
    failwith "A single low count aggregator is allowed in a query"

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
      | _ -> failwith "Only count(*), count(column) and count(distinct column) are supported"
    | _ -> ()
  )

let private validateSubQuery selectQuery =
  selectQuery
  |> visitAggregates (fun _ -> failwith "Aggregates in subqueries are not currently supported")

  if not (List.isEmpty selectQuery.GroupBy) then
    failwith "Grouping in subqueries is not currently supported"

  validateSelectTarget selectQuery
  validateLimitUsage selectQuery

let private validateSelectTarget selectQuery = selectQuery |> visit validateSubQuery

let private validateLimitUsage selectQuery =
  if selectQuery.Limit <> None then
    failwith "Limit is not allowed in anonymizing subqueries"

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

/// Validates a top-level query.
let validateQuery isAnonymizing (selectQuery: SelectQuery) =
  validateSingleLowCount selectQuery

  if isAnonymizing then
    validateOnlyCount selectQuery
    allowedCountUsage selectQuery
    validateSelectTarget selectQuery
