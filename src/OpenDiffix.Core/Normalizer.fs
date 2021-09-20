module OpenDiffix.Core.Normalizer

open AnalyzerTypes
open NodeUtils

let rec private mapSequence mapper expr =
  let newExpr = mapper expr
  if newExpr = expr then expr else mapSequence mapper newExpr

let rec private mapBottomUp mapper expr =
  match expr with
  | FunctionExpr (fn, args) -> FunctionExpr(fn, args |> List.map (mapBottomUp mapper))
  | _ -> expr
  |> mapSequence mapper

let private normalizeConstant expr =
  match expr with
  | FunctionExpr (ScalarFunction fn, [ Constant value ]) ->
    [ value ] |> Expression.evaluateScalarFunction fn |> Constant
  | FunctionExpr (ScalarFunction fn, [ Constant value1; Constant value2 ]) ->
    [ value1; value2 ] |> Expression.evaluateScalarFunction fn |> Constant
  | _ -> expr

let private isInequality fn =
  List.exists ((=) fn) [ Lt; Gt; LtE; GtE ]

let private isComparison fn = fn = Equals || isInequality fn

let private invertComparison =
  function
  | Equals -> Equals
  | Lt -> GtE
  | Gt -> LtE
  | LtE -> Gt
  | GtE -> Lt
  | _ -> failwith "Unexpected comparison operator."

let private normalizeComparison expr =
  match expr with
  | FunctionExpr (ScalarFunction fn, [ arg1; arg2 ]) when isComparison fn ->
    match arg1 with
    | Constant _ -> (fn |> invertComparison |> ScalarFunction, [ arg2; arg1 ]) |> FunctionExpr
    | _ -> expr
  | _ -> expr

let private normalizeBooleanExpression expr =
  match expr with
  | FunctionExpr (ScalarFunction Equals,
                  [ FunctionExpr (ScalarFunction Not, [ arg1 ]); FunctionExpr (ScalarFunction Not, [ arg2 ]) ]) ->
    FunctionExpr(ScalarFunction Equals, [ arg1; arg2 ])
  | FunctionExpr (ScalarFunction Equals, [ FunctionExpr (ScalarFunction Not, [ arg1 ]); arg2 ]) ->
    FunctionExpr(ScalarFunction Not, [ FunctionExpr(ScalarFunction Equals, [ arg1; arg2 ]) ])
  | FunctionExpr (ScalarFunction Equals, [ arg1; FunctionExpr (ScalarFunction Not, [ arg2 ]) ]) ->
    FunctionExpr(ScalarFunction Not, [ FunctionExpr(ScalarFunction Equals, [ arg1; arg2 ]) ])
  | FunctionExpr (ScalarFunction Equals, [ arg; Constant (Boolean true) ]) -> arg
  | FunctionExpr (ScalarFunction Equals, [ arg; Constant (Boolean false) ]) -> FunctionExpr(ScalarFunction Not, [ arg ])
  | FunctionExpr (ScalarFunction Not, [ FunctionExpr (ScalarFunction Not, [ expr ]) ]) -> expr
  | FunctionExpr (ScalarFunction Not, [ FunctionExpr (ScalarFunction fn, args) ]) when isInequality fn ->
    (fn |> invertComparison |> ScalarFunction, args) |> FunctionExpr
  | _ -> expr

let rec normalize (query: Query) : Query =
  match query with
  | SelectQuery ({ From = SubQuery (subquery, alias) } as query) ->
    SelectQuery { query with From = SubQuery(normalize subquery, alias) }
  | _ -> query
  |> map (mapBottomUp normalizeConstant)
  |> map (mapBottomUp normalizeComparison)
  |> map (mapBottomUp normalizeBooleanExpression)
