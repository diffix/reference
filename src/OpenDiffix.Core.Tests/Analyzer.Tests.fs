module OpenDiffix.Core.AnalyzerTests

open System.Linq.Expressions
open Xunit
open FsUnit.Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

let testParsedQuery queryString queryTypeFilter callback (expected: Query) =
  let testResult =
    Parser.parse queryString
    |> Result.mapError(fun e -> $"Failed to parse: %A{e}")
    |> Result.bind queryTypeFilter
    |> Result.bind callback
  assertOkEqual testResult expected

module AnalyzeShow =
  let showQueries =
    function
    | ParserTypes.Expression.ShowQuery q -> Ok q
    | _ -> Error "Expecting SHOW query"

  [<Fact>]
  let ``SHOW TABLES`` () =
    testParsedQuery "SHOW TABLES" showQueries Analyzer.transformShowQuery (ShowQuery ShowQueryKinds.Tables)

  [<Fact>]
  let ``SHOW columns FROM table`` () =
    testParsedQuery "SHOW columns FROM table" showQueries Analyzer.transformShowQuery
      (ShowQuery (ShowQueryKinds.ColumnsInTable "table"))

module AnalyzeSelect =
  let selectQueries =
    function
    | ParserTypes.Expression.SelectQuery q -> Ok q
    | _ -> Error "Expecting SELECT query"

  let testTable: Table =
    {
      Name = "table"
      Columns = [
        {Name = "str_col"; Type = ColumnType.StringType; Index = 0}
        {Name = "int_col"; Type = ColumnType.IntegerType; Index = 1}
        {Name = "float_col"; Type = ColumnType.FloatType; Index = 2}
        {Name = "bool_col"; Type = ColumnType.BooleanType; Index = 3}
      ]
    }

  [<Fact>]
  let ``Selecting columns from a table`` () =
    testParsedQuery "SELECT str_col, bool_col FROM table" selectQueries (Analyzer.transformSelectQueryWithTable testTable) (
      SelectQuery {
        Select = [
          {
            Type = ExpressionType.StringType
            Expression = ColumnReference (0, ExpressionType.StringType)
            Alias = ""
          }
          {
            Type = ExpressionType.BooleanType
            Expression = ColumnReference (3, ExpressionType.BooleanType)
            Alias = ""
          }
        ]
        Where = None
        From = Table ("table", None)
        GroupBy = []
        GroupingSets = []
        Having = None
        OrderBy = []
      }
    )

  [<Fact>]
  let ``SELECT with alias, function, aggregate, GROUP BY, and WHERE-clause`` () =
    let query = @"
    SELECT
      int_col as colAlias,
      float_col + int_col,
      count(int_col)
    FROM table
    WHERE
      int_col > 0 and int_col < 10
    GROUP BY int_col, float_col + int_col, count(int_col)
    "
    testParsedQuery query selectQueries (Analyzer.transformSelectQueryWithTable testTable) (
      SelectQuery {
        Select = [
          {
            Type = ExpressionType.IntegerType
            Expression = ColumnReference (1, ExpressionType.IntegerType)
            Alias = "colAlias"
          }
          {
            Type = ExpressionType.FloatType
            Expression = FunctionExpr (Function.Plus, [
              ColumnReference (2, ExpressionType.FloatType)
              ColumnReference (1, ExpressionType.IntegerType)
            ], FunctionType.Scalar)
            Alias = ""
          }
          {
            Type = ExpressionType.IntegerType
            Expression = FunctionExpr (Function.Count, [
              ColumnReference (1, ExpressionType.IntegerType)
            ], FunctionType.Aggregate AggregateOptions.Default)
            Alias = ""
          }
        ]
        Where =
          Some (
            FunctionExpr (Function.And, [
              FunctionExpr (Function.Gt, [ColumnReference (1, ExpressionType.IntegerType); Constant (Value.Integer 0)], FunctionType.Scalar)
              FunctionExpr (Function.Lt, [ColumnReference (1, ExpressionType.IntegerType); Constant (Value.Integer 10)], FunctionType.Scalar)
            ], FunctionType.Scalar)
          )
        From = Table ("table", None)
        GroupBy = [
          ColumnReference (1, ExpressionType.IntegerType)
          FunctionExpr (Function.Plus, [
            ColumnReference (2, ExpressionType.FloatType)
            ColumnReference (1, ExpressionType.IntegerType)
          ], FunctionType.Scalar)
          FunctionExpr (Function.Count, [
            ColumnReference (1, ExpressionType.IntegerType)
          ], FunctionType.Aggregate AggregateOptions.Default)
        ]
        GroupingSets = []
        Having = None
        OrderBy = []
      }
    )

