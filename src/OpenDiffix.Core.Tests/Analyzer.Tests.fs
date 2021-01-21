module OpenDiffix.Core.AnalyzerTests

open Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

let testParsedQuery queryString callback (expected: Query) =
  let testResult =
    Parser.parse queryString
    |> Result.mapError (fun e -> $"Failed to parse: %A{e}")
    |> Result.bind callback

  assertOkEqual testResult expected

module AnalyzeSelect =
  let testTable: Table =
    {
      Name = "table"
      Columns =
        [
          { Name = "str_col"; Type = ColumnType.StringType; Index = 0 }
          { Name = "int_col"; Type = ColumnType.IntegerType; Index = 1 }
          { Name = "float_col"; Type = ColumnType.FloatType; Index = 2 }
          { Name = "bool_col"; Type = ColumnType.BooleanType; Index = 3 }
        ]
    }

  [<Fact>]
  let ``Selecting columns from a table`` () =
    testParsedQuery
      "SELECT str_col, bool_col FROM table"
      (Analyzer.transformQuery testTable)
      (SelectQuery
        {
          Columns =
            [
              {
                Type = ExpressionType.StringType
                Expression = ColumnReference(0, ExpressionType.StringType)
                Alias = ""
              }
              {
                Type = ExpressionType.BooleanType
                Expression = ColumnReference(3, ExpressionType.BooleanType)
                Alias = ""
              }
            ]
          Where = Boolean true |> Constant
          From = Table(testTable, "")
          GroupBy = []
          GroupingSets = [ [] ]
          Having = Boolean true |> Constant
          OrderBy = []
        })

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

    testParsedQuery
      query
      (Analyzer.transformQuery testTable)
      (SelectQuery
        {
          Columns =
            [
              {
                Type = ExpressionType.IntegerType
                Expression = ColumnReference(1, ExpressionType.IntegerType)
                Alias = "colAlias"
              }
              {
                Type = ExpressionType.FloatType
                Expression =
                  FunctionExpr
                    (Function.Plus,
                     [ ColumnReference(2, ExpressionType.FloatType); ColumnReference(1, ExpressionType.IntegerType) ],
                     FunctionType.Scalar)
                Alias = ""
              }
              {
                Type = ExpressionType.IntegerType
                Expression =
                  FunctionExpr
                    (Function.Count,
                     [ ColumnReference(1, ExpressionType.IntegerType) ],
                     FunctionType.Aggregate AggregateOptions.Default)
                Alias = ""
              }
            ]
          Where =
            FunctionExpr
              (Function.And,
               [
                 FunctionExpr
                   (Function.Gt,
                    [ ColumnReference(1, ExpressionType.IntegerType); Constant(Value.Integer 0) ],
                    FunctionType.Scalar)
                 FunctionExpr
                   (Function.Lt,
                    [ ColumnReference(1, ExpressionType.IntegerType); Constant(Value.Integer 10) ],
                    FunctionType.Scalar)
               ],
               FunctionType.Scalar)
          From = Table(testTable, "")
          GroupBy =
            [
              ColumnReference(1, ExpressionType.IntegerType)
              FunctionExpr
                (Function.Plus,
                 [ ColumnReference(2, ExpressionType.FloatType); ColumnReference(1, ExpressionType.IntegerType) ],
                 FunctionType.Scalar)
              FunctionExpr
                (Function.Count,
                 [ ColumnReference(1, ExpressionType.IntegerType) ],
                 FunctionType.Aggregate AggregateOptions.Default)
            ]
          GroupingSets = [ [ 0; 1; 2 ] ]
          Having = Boolean true |> Constant
          OrderBy = []
        })
