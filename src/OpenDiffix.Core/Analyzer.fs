module OpenDiffix.Core.Analyzer

open FsToolkit.ErrorHandling
open OpenDiffix.Core

let rec functionExpression table fn children =
  children
  |> List.map (mapExpression table)
  |> List.sequenceResultM
  |> Result.map (fun children -> FunctionExpr(fn, children))

and mapExpression table parsedExpression =
  match parsedExpression with
  | ParserTypes.Identifier identifierName ->
      result {
        let! (index, column) = Table.getColumn table identifierName
        return Expression.ColumnReference(index, column.Type)
      }
  | ParserTypes.Expression.Integer value -> Value.Integer value |> Constant |> Ok
  | ParserTypes.Expression.Float value -> Value.Float value |> Constant |> Ok
  | ParserTypes.Expression.String value -> Value.String value |> Constant |> Ok
  | ParserTypes.Expression.Boolean value -> Value.Boolean value |> Constant |> Ok
  | ParserTypes.Not expr -> functionExpression table (ScalarFunction Not) [ expr ]
  | ParserTypes.Lt (left, right) -> functionExpression table (ScalarFunction Lt) [ left; right ]
  | ParserTypes.LtE (left, right) -> functionExpression table (ScalarFunction LtE) [ left; right ]
  | ParserTypes.Gt (left, right) -> functionExpression table (ScalarFunction Gt) [ left; right ]
  | ParserTypes.GtE (left, right) -> functionExpression table (ScalarFunction GtE) [ left; right ]
  | ParserTypes.And (left, right) -> functionExpression table (ScalarFunction And) [ left; right ]
  | ParserTypes.Or (left, right) -> functionExpression table (ScalarFunction Or) [ left; right ]
  | ParserTypes.Function (name, args) ->
      result {
        let! fn = Function.FromString name
        let! fn, childExpressions = mapFunctionExpression table fn args
        return FunctionExpr(fn, childExpressions)
      }
  | other -> Error $"The expression is not permitted in this context: %A{other}"

and mapFunctionExpression table fn args =
  match fn, args with
  | AggregateFunction (Count, aggregateArgs), [ParserTypes.Distinct expr] ->
    mapExpression table expr
    |> Result.map(fun childArg ->
      AggregateFunction(Count, {aggregateArgs with Distinct = true}), [childArg]
    )
  | _, _ ->
    args
    |> List.map (mapExpression table)
    |> List.sequenceResultM
    |> Result.map(fun childArgs -> fn, childArgs)

let extractAlias =
  function
  | ParserTypes.Expression.Identifier aliasName -> Ok aliasName
  | other -> Error $"Expected an alias, but got an expression: %A{other}"

let wrapExpressionAsSelected table parserExpr =
  result {
    let! expr = mapExpression table parserExpr

    return { AnalyzerTypes.Expression = expr; AnalyzerTypes.Alias = "" }
  }

let rec mapSelectedExpression table selectedExpression: Result<AnalyzerTypes.SelectExpression, string> =
  match selectedExpression with
  | ParserTypes.As (expr, exprAlias) ->
      result {
        let! childExpr = mapExpression table expr
        let! alias = extractAlias exprAlias
        return { Expression = childExpr; Alias = alias }
      }
  | ParserTypes.Identifier identifierName ->
      result {
        let! (index, column) = Table.getColumn table identifierName

        return { Expression = Expression.ColumnReference(index, column.Type); Alias = "" }
      }
  | ParserTypes.Expression.Function (fn, args) ->
      wrapExpressionAsSelected table (ParserTypes.Expression.Function(fn, args))
  | ParserTypes.Expression.Integer value -> wrapExpressionAsSelected table (ParserTypes.Expression.Integer value)
  | ParserTypes.Expression.Float value -> wrapExpressionAsSelected table (ParserTypes.Expression.Float value)
  | ParserTypes.Expression.String value -> wrapExpressionAsSelected table (ParserTypes.Expression.String value)
  | ParserTypes.Expression.Boolean value -> wrapExpressionAsSelected table (ParserTypes.Expression.Boolean value)
  | other -> Error $"Unexpected expression selected '%A{other}'"

let transformSelectedExpressions (table: Table) selectedExpressions =
  selectedExpressions
  |> List.map (mapSelectedExpression table)
  |> List.sequenceResultM

let selectedTableName =
  function
  | ParserTypes.Expression.Identifier tableName -> Ok tableName
  | _ -> Error "Only selecting from a single table is supported"

let transformExpressionOptionWithDefaultTrue table optionalExpression =
  optionalExpression
  |> Option.map (mapExpression table)
  |> Option.defaultValue (Value.Boolean true |> Expression.Constant |> Ok)

let transformQuery table (selectQuery: ParserTypes.SelectQuery) =
  result {
    let! selectedExpressions = transformSelectedExpressions table selectQuery.Expressions
    let! whereClause = transformExpressionOptionWithDefaultTrue table selectQuery.Where
    let! havingClause = transformExpressionOptionWithDefaultTrue table selectQuery.Having
    let! groupBy = selectQuery.GroupBy |> List.map (mapExpression table) |> List.sequenceResultM

    return
      AnalyzerTypes.SelectQuery
        {
          Columns = selectedExpressions
          Where = whereClause
          From = AnalyzerTypes.SelectFrom.Table table
          GroupingSets = [ groupBy ]
          Having = havingClause
          OrderBy = []
        }
  }

let analyze connection (parseTree: ParserTypes.SelectQuery): Async<Result<AnalyzerTypes.Query, string>> =
  asyncResult {
    let! tableName = selectedTableName parseTree.From
    let! table = Table.getI connection tableName
    return! transformQuery table parseTree
  }
