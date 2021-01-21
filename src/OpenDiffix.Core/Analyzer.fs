module OpenDiffix.Core.Analyzer

open FsToolkit.ErrorHandling
open OpenDiffix.Core

let columnToExpressionType =
  function
  | ColumnType.BooleanType -> Ok ExpressionType.BooleanType
  | ColumnType.IntegerType -> Ok ExpressionType.IntegerType
  | ColumnType.FloatType -> Ok ExpressionType.FloatType
  | ColumnType.StringType -> Ok ExpressionType.StringType
  | ColumnType.UnknownType other -> Error $"Unknown type %s{other} cannot be selected"

let rec functionExpression table fn children =
  children
  |> List.map (mapExpression table)
  |> List.sequenceResultM
  |> Result.map (fun children -> FunctionExpr(fn, children))

and mapExpression table parsedExpression =
  match parsedExpression with
  | ParserTypes.Identifier identifierName ->
      result {
        let! column = Table.getColumn table identifierName
        let! columnType = columnToExpressionType column.Type
        let! columnIndex = Table.getColumnIndex table column
        return Expression.ColumnReference(columnIndex, columnType)
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
        let! childExpressions = args |> List.map (mapExpression table) |> List.sequenceResultM
        return FunctionExpr(fn, childExpressions)
      }
  | other -> Error $"The expression is not permitted in this context: %A{other}"

let extractAlias =
  function
  | ParserTypes.Expression.Identifier aliasName -> Ok aliasName
  | other -> Error $"Expected an alias, but got an expression: %A{other}"

let wrapExpressionAsSelected table parserExpr =
  result {
    let! expr = mapExpression table parserExpr
    let! exprType = Expression.GetType expr

    return
      {
        AnalyzerTypes.Expression = expr
        AnalyzerTypes.Type = exprType
        AnalyzerTypes.Alias = ""
      }
  }

let rec mapSelectedExpression table selectedExpression: Result<AnalyzerTypes.SelectExpression, string> =
  match selectedExpression with
  | ParserTypes.As (expr, exprAlias) ->
      result {
        let! childExpr = mapExpression table expr
        let! childExprType = Expression.GetType childExpr
        let! alias = extractAlias exprAlias
        return { Expression = childExpr; Type = childExprType; Alias = alias }
      }
  | ParserTypes.Identifier identifierName ->
      result {
        let! column = Table.getColumn table identifierName
        let! columnType = columnToExpressionType column.Type
        let! columnIndex = Table.getColumnIndex table column

        return
          {
            Expression = Expression.ColumnReference(columnIndex, columnType)
            Type = columnType
            Alias = ""
          }
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

let optionalExprToAnalyzerExpressionWithDefaultTrue table optionalExpression =
  optionalExpression
  |> Option.map (mapExpression table)
  |> Option.defaultValue (Value.Boolean true |> Expression.Constant |> Ok)

let transformQuery table (selectQuery: ParserTypes.SelectQuery) =
  result {
    let! selectedExpressions = transformSelectedExpressions table selectQuery.Expressions
    let! whereClause = optionalExprToAnalyzerExpressionWithDefaultTrue table selectQuery.Where
    let! havingClause = optionalExprToAnalyzerExpressionWithDefaultTrue table None
    let! groupBy = selectQuery.GroupBy |> List.map (mapExpression table) |> List.sequenceResultM

    return
      AnalyzerTypes.SelectQuery
        {
          Columns = selectedExpressions
          Where = whereClause
          From = AnalyzerTypes.SelectFrom.Table table
          GroupBy = groupBy
          GroupingSets = [[0..groupBy.Length-1]]
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
