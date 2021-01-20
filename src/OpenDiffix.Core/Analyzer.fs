module OpenDiffix.Core.Analyzer

open FsToolkit.ErrorHandling
open OpenDiffix.Core

let transformShowQuery showQuery: Result<AnalyzerTypes.Query, string> =
  match showQuery with
  | OpenDiffix.Core.ParserTypes.ShowQueryKinds.Tables -> AnalyzerTypes.ShowQueryKind.Tables
  | ParserTypes.ShowQueryKinds.Columns tableName -> AnalyzerTypes.ShowQueryKind.ColumnsInTable tableName
  |> AnalyzerTypes.ShowQuery
  |> Ok

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
  |> Result.map (fun children -> FunctionExpr(fn, children, Function.TypeInfo fn))

and mapExpression table parsedExpression =
  match parsedExpression with
  | ParserTypes.Identifier identifierName ->
      result {
        let! column = Table.getColumn table identifierName
        let! columnType = columnToExpressionType column.Type
        return Expression.ColumnReference(column.Index, columnType)
      }
  | ParserTypes.Expression.Integer value -> Value.Integer value |> Constant |> Ok
  | ParserTypes.Expression.Float value -> Value.Float value |> Constant |> Ok
  | ParserTypes.Expression.String value -> Value.String value |> Constant |> Ok
  | ParserTypes.Expression.Boolean value -> Value.Boolean value |> Constant |> Ok
  | ParserTypes.Not expr -> functionExpression table Not [ expr ]
  | ParserTypes.Lt (left, right) -> functionExpression table Lt [ left; right ]
  | ParserTypes.LtE (left, right) -> functionExpression table LtE [ left; right ]
  | ParserTypes.Gt (left, right) -> functionExpression table Gt [ left; right ]
  | ParserTypes.GtE (left, right) -> functionExpression table GtE [ left; right ]
  | ParserTypes.And (left, right) -> functionExpression table And [ left; right ]
  | ParserTypes.Or (left, right) -> functionExpression table Or [ left; right ]
  | ParserTypes.Function (name, args) ->
      result {
        let! fn = Function.FromString name
        let! childExpressions = args |> List.map (mapExpression table) |> List.sequenceResultM
        let fnType = Function.TypeInfo fn
        return FunctionExpr(fn, childExpressions, fnType)
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

        return
          {
            Expression = Expression.ColumnReference(column.Index, columnType)
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

let optionalExprToAnalyzerExpression table optionalExpression =
  optionalExpression
  |> Option.map (fun expr -> mapExpression table expr |> Result.map Some)
  |> Option.defaultValue (Ok None)

let transformSelectQueryWithTable table (selectQuery: ParserTypes.SelectQuery) =
  result {
    let! selectedExpressions = transformSelectedExpressions table selectQuery.Expressions
    let! whereClauseOption = optionalExprToAnalyzerExpression table selectQuery.Where
    let! havingClauseOption = optionalExprToAnalyzerExpression table None
    let! groupBy = selectQuery.GroupBy |> List.map (mapExpression table) |> List.sequenceResultM

    return
      AnalyzerTypes.SelectQuery
        {
          Columns = selectedExpressions
          Where = whereClauseOption
          From = AnalyzerTypes.SelectFrom.Table(table, "")
          GroupBy = groupBy
          GroupingSets = []
          Having = havingClauseOption
          OrderBy = []
        }
  }

let transformSelectQuery connection (selectQuery: ParserTypes.SelectQuery) =
  asyncResult {
    let! tableName = selectedTableName selectQuery.From
    let! table = Table.getI connection tableName
    return! transformSelectQueryWithTable table selectQuery
  }

let analyze connection parseTree: Async<Result<AnalyzerTypes.Query, string>> =
  match parseTree with
  | ParserTypes.Expression.ShowQuery showQuery -> async { return transformShowQuery showQuery }
  | ParserTypes.Expression.SelectQuery selectQuery -> transformSelectQuery connection selectQuery
  | _ -> async { return Error "Expected a SHOW or SELECT query" }
