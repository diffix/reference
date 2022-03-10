module rec OpenDiffix.Core.QueryValidator

open System.Text.RegularExpressions
open AnalyzerTypes
open NodeUtils

let private isMoneyStyle arg =
  match arg with
  // "money-style" numbers, i.e. 1, 2, or 5 preceeded by or followed by zeros: ⟨... 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, ...⟩
  | Constant (Real c) -> Regex.IsMatch($"%.15e{c}", "^[125]\.0+e[-+]\d+$")
  | Constant (Integer c) -> Regex.IsMatch($"%i{c}", "^[125]0*$")
  | _ -> false

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
    failwith "A single low count aggregator is allowed in a query."

let private validateOnlyCount query =
  query
  |> visitAggregates (
    function
    | FunctionExpr (AggregateFunction (Count, _), _) -> ()
    | FunctionExpr (AggregateFunction (_otherAggregate, _), _) ->
      failwith "Only count aggregates are supported in anonymizing queries."
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
      | _ -> failwith "Only count(*), count(column) and count(distinct column) are supported in anonymizing queries."
    | _ -> ()
  )

let private validateSelectTarget (selectQuery: SelectQuery) =
  match selectQuery.From with
  | Join _ -> failwith "JOIN in anonymizing queries is not currently supported."
  | SubQuery _ -> failwith "Subqueries in anonymizing queries are not currently supported."
  | _ -> ()

let private validateNoWhere (selectQuery: SelectQuery) =
  if selectQuery.Where <> Constant(Boolean true) then
    failwith "WHERE in anonymizing queries is not currently supported."

let private validateGeneralization accessLevel expression =
  if accessLevel <> Direct then
    match expression with
    | FunctionExpr (ScalarFunction _, primaryArg :: _) when not (Expression.isColumnReference primaryArg) ->
      failwith "Primary argument for a bucket function has to be a simple column reference."
    | FunctionExpr (ScalarFunction _, _ :: secondaryArgs) when List.exists (Expression.isConstant >> not) secondaryArgs ->
      failwith "Secondary arguments for a bucket function have to be constants."
    | _ -> ()

  if accessLevel = PublishUntrusted then
    match expression with
    | FunctionExpr (ScalarFunction fn, [ _ ]) when List.contains fn [ Floor; Ceil; Round ] -> ()
    | FunctionExpr (ScalarFunction fn, [ _; arg ]) when
      List.contains fn [ FloorBy; CeilBy; RoundBy ] && isMoneyStyle arg
      ->
      ()
    | FunctionExpr (ScalarFunction Substring, [ _; fromArg; _ ]) when fromArg = (1L |> Integer |> Constant) -> ()
    | ColumnReference _ -> ()
    | _ -> failwith "Generalization used in the query is not allowed in untrusted access level."

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let validateDirectQuery (selectQuery: SelectQuery) = validateSingleLowCount selectQuery

let validateAnonymizingQuery (selectQuery: SelectQuery) =
  validateOnlyCount selectQuery
  allowedCountUsage selectQuery
  validateNoWhere selectQuery
  validateSelectTarget selectQuery

let validateGeneralizations accessLevel expressions =
  Seq.iter (validateGeneralization accessLevel) expressions
