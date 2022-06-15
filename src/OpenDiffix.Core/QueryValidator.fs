module rec OpenDiffix.Core.QueryValidator

open System.Text.RegularExpressions
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
    failwith "A single low count aggregator is allowed in a query."

let private validateAllowedAggregates query =
  query
  |> visitAggregates (
    fst3
    >> function
      | Count
      | CountNoise
      | Sum
      | SumNoise
      | CountHistogram -> ()
      | _ -> failwith "Aggregate not supported in anonymizing queries."
  )

let private validateCountUsage query =
  query
  |> visitAggregates (
    function
    | Count, _, args
    | CountNoise, _, args ->
      match args with
      | []
      | [ ColumnReference _ ] -> ()
      | _ -> failwith "Only count(column) is supported in anonymizing queries."
    | _ -> ()
  )

let private validateSumUsage query =
  query
  |> visitAggregates (
    function
    | Sum, { Distinct = distinct }, args
    | SumNoise, { Distinct = distinct }, args ->
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
    | FunctionExpr (ScalarFunction fn, [ _; Constant c ]) when
      List.contains fn [ FloorBy; RoundBy ] && Value.isMoneyRounded c
      ->
      ()
    | FunctionExpr (ScalarFunction Substring, [ _; fromArg; _ ]) when fromArg = (1L |> Integer |> Constant) -> ()
    | ColumnReference _ -> ()
    | _ -> failwith "Generalization used in the query is not allowed in untrusted access level."

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let validateDirectQuery (selectQuery: SelectQuery) = validateSingleLowCount selectQuery

let validateAnonymizingQuery accessLevel (selectQuery: SelectQuery) =
  validateAllowedAggregates selectQuery
  validateCountUsage selectQuery
  validateSumUsage selectQuery
  validateSelectTarget selectQuery

let validateGeneralizations accessLevel expressions =
  Seq.iter (validateGeneralization accessLevel) expressions
