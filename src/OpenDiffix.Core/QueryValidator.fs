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
      | FunctionExpr(AggregateFunction(DiffixLowCount, _), _) -> true
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

let private validateCountHistogramUsage accessLevel query =
  // Verifying that the first argument is an AID is done in the analyzer.
  query
  |> visitAggregates (
    function
    | CountHistogram, { Distinct = true }, _ ->
      failwith "count_histogram(distinct) is not supported in anonymizing queries."
    | CountHistogram, _, [ _aidExpr; Constant(Integer binSize) ] when binSize >= 1L ->
      if accessLevel = PublishUntrusted && not (Value.isMoneyRounded (Integer binSize)) then
        failwith "count_histogram bin size must be a money-aligned value (1, 2, 5, 10, ...)."
    | CountHistogram, _, [ _aidExpr; _invalidBinSize ] ->
      failwith "count_histogram bin size must be a constant positive integer."
    | CountHistogram, _, [] -> failwith "count_histogram must specify an AID argument."
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

let rec private validateJoinFilter filter =
  match filter with
  | FunctionExpr(ScalarFunction Or, _) ->
    failwith "Combining `JOIN` filters using `OR` in anonymizing queries is not supported."
  | FunctionExpr(ScalarFunction And, [ leftFilter; rightFilter ]) ->
    validateJoinFilter leftFilter
    validateJoinFilter rightFilter
  | FunctionExpr(ScalarFunction Equals, [ ColumnReference _; ColumnReference _ ]) -> ()
  | _ ->
    failwith "Only equalities between simple column references are supported as `JOIN` filters in anonymizing queries."

let rec private validateSelectTarget (target: QueryRange) =
  match target with
  | Join { On = Constant(Boolean true) } -> failwith "`CROSS JOIN` in anonymizing queries is not supported."
  | Join { Left = leftTarget; Right = rightTarget; On = matchFilter } ->
    validateSelectTarget leftTarget
    validateSelectTarget rightTarget
    validateJoinFilter matchFilter
  | SubQuery _ -> failwith "Subqueries in anonymizing queries are not supported."
  | _ -> ()

let private validateGeneralization accessLevel expression =
  if accessLevel <> Direct then
    match expression with
    | FunctionExpr(ScalarFunction DateTrunc, [ _; primaryArg ])
    | FunctionExpr(ScalarFunction Extract, [ _; primaryArg ])
    | FunctionExpr(ScalarFunction _, primaryArg :: _) ->
      if not (Expression.isColumnReference primaryArg) then
        failwith "Primary argument for a generalization expression has to be a simple column reference."
    | _ -> ()

    match expression with
    | FunctionExpr(ScalarFunction DateTrunc, [ secondaryArg; _ ])
    | FunctionExpr(ScalarFunction Extract, [ secondaryArg; _ ]) ->
      if secondaryArg |> Expression.isConstant |> not then
        failwith "Secondary arguments for a generalization expression have to be constants."
    | FunctionExpr(ScalarFunction _, _ :: secondaryArgs) ->
      if (List.exists (Expression.isConstant >> not) secondaryArgs) then
        failwith "Secondary arguments for a generalization expression have to be constants."
    | _ -> ()

  if accessLevel = PublishUntrusted then
    match expression with
    | FunctionExpr(ScalarFunction fn, _) when List.contains fn [ Floor; Ceil; Round; DateTrunc; Extract ] -> ()
    | FunctionExpr(ScalarFunction fn, [ _; Constant c ]) when
      List.contains fn [ FloorBy; RoundBy ] && Value.isMoneyRounded c
      ->
      ()
    | FunctionExpr(ScalarFunction Substring, [ _; fromArg; _ ]) when fromArg = (1L |> Integer |> Constant) -> ()
    | ColumnReference _ -> ()
    | _ -> failwith "Generalization used in the query is not allowed in untrusted access level."

let private validateWhere rangeColumns selectQuery =
  let rec filterVisitor =
    function
    | ColumnReference(index, _) ->
      let rangeColumn = List.item index rangeColumns

      if rangeColumn.IsAid then
        failwith "AID columns can't be referenced by pre-anonymization filters."
    | other -> visit filterVisitor other

  visit filterVisitor selectQuery.Where

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let validateDirectQuery (selectQuery: SelectQuery) = validateSingleLowCount selectQuery

let validateAnonymizingQuery accessLevel rangeColumns (selectQuery: SelectQuery) =
  validateAllowedAggregates selectQuery
  validateCountUsage selectQuery
  validateSumUsage selectQuery
  validateCountHistogramUsage accessLevel selectQuery
  validateSelectTarget selectQuery.From
  validateWhere rangeColumns selectQuery

let validateGeneralizations accessLevel expressions =
  Seq.iter (validateGeneralization accessLevel) expressions
