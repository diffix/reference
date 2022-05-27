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

let private validateAllowedAggregates query =
  query
  |> visitAggregates (
    function
    | FunctionExpr (AggregateFunction (Count, _), _) -> ()
    | FunctionExpr (AggregateFunction (CountNoise, _), _) -> ()
    | FunctionExpr (AggregateFunction (Sum, _), _) -> ()
    | FunctionExpr (AggregateFunction (_otherAggregate, _), _) ->
      failwith "Only count, count_noise and sum aggregates are supported in anonymizing queries."
    | _ -> ()
  )

let private allowedCountUsage query =
  query
  |> visitAggregates (
    function
    | FunctionExpr (AggregateFunction (Count, _), args)
    | FunctionExpr (AggregateFunction (CountNoise, { Distinct = false }), args) ->
      match args with
      | []
      | [ ColumnReference _ ] -> ()
      | _ -> failwith "Only count(column) is supported in anonymizing queries."
    | FunctionExpr (AggregateFunction (CountNoise, { Distinct = true }), _) ->
      failwith "count_noise(distinct column) is not currently supported."
    | _ -> ()
  )

let private allowedSumUsage query =
  query
  |> visitAggregates (
    function
    | FunctionExpr (AggregateFunction (Sum, { Distinct = distinct }), args) ->
      match distinct, args with
      | false, [ ColumnReference _ ] -> ()
      | _ -> failwith "Only sum(column) is supported in anonymizing queries."
    | _ -> ()
  )

let private validateSelectTarget (selectQuery: SelectQuery) =
  match selectQuery.From with
  | Join _ -> failwith "JOIN in anonymizing queries is not currently supported."
  | SubQuery _ -> failwith "Subqueries in anonymizing queries are not currently supported."
  | _ -> ()

let private validateGeneralization accessLevel expression =
  if accessLevel <> Direct then
    match expression with
    | FunctionExpr (ScalarFunction _, primaryArg :: _) when not (Expression.isColumnReference primaryArg) ->
      failwith "Primary argument for a generalization expression has to be a simple column reference."
    | FunctionExpr (ScalarFunction _, _ :: secondaryArgs) when List.exists (Expression.isConstant >> not) secondaryArgs ->
      failwith "Secondary arguments for a generalization expression have to be constants."
    | _ -> ()

  if accessLevel = PublishUntrusted then
    match expression with
    | FunctionExpr (ScalarFunction fn, [ _ ]) when List.contains fn [ Floor; Ceil; Round ] -> ()
    | FunctionExpr (ScalarFunction fn, [ _; arg ]) when List.contains fn [ FloorBy; RoundBy ] && isMoneyStyle arg -> ()
    | FunctionExpr (ScalarFunction Substring, [ _; fromArg; _ ]) when fromArg = (1L |> Integer |> Constant) -> ()
    | ColumnReference _ -> ()
    | _ -> failwith "Generalization used in the query is not allowed in untrusted access level."

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let validateDirectQuery (selectQuery: SelectQuery) = validateSingleLowCount selectQuery

let validateAnonymizingQuery (selectQuery: SelectQuery) =
  validateAllowedAggregates selectQuery
  allowedCountUsage selectQuery
  allowedSumUsage selectQuery
  validateSelectTarget selectQuery

let validateGeneralizations accessLevel expressions =
  Seq.iter (validateGeneralization accessLevel) expressions
