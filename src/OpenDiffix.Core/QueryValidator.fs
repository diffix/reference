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

let private validateSelectTarget (selectQuery: SelectQuery) =
  match selectQuery.From with
  | Join _ -> failwith "JOIN in anonymizing queries is not currently supported"
  | SubQuery _ -> failwith "Subqueries in anonymizing queries are not currently supported"
  | _ -> ()

let private validateNoWhere (selectQuery: SelectQuery) =
  if selectQuery.Where <> Constant(Boolean true) then
    failwith "WHERE in anonymizing queries is not currently supported"

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let validateDirectQuery (selectQuery: SelectQuery) = validateSingleLowCount selectQuery

let validateAnonymizingQuery (selectQuery: SelectQuery) =
  validateOnlyCount selectQuery
  allowedCountUsage selectQuery
  validateNoWhere selectQuery
  validateSelectTarget selectQuery
