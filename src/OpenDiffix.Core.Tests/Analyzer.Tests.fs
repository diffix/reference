module OpenDiffix.Core.AnalyzerTests

open Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

let testTable: Table =
  {
    Name = "table"
    Columns =
      [
        { Name = "str_col"; Type = StringType }
        { Name = "int_col"; Type = IntegerType }
        { Name = "float_col"; Type = RealType }
        { Name = "bool_col"; Type = BooleanType }
      ]
  }

let defaultQuery =
  {
    Columns = []
    Where = Boolean true |> Constant
    From = Table testTable
    GroupingSets = [ [] ]
    Having = Boolean true |> Constant
    OrderBy = []
  }

let testParsedQuery queryString callback (expected: Query) =
  let testResult =
    Parser.parse queryString
    |> Result.mapError (fun e -> $"Failed to parse: %A{e}")
    |> Result.bind callback

  assertOkEqual testResult expected

[<Fact>]
let ``Analyze count(*)`` () =
  testParsedQuery
    "SELECT count(*) from table"
    (Analyzer.transformQuery testTable)
    (SelectQuery
      { defaultQuery with
          Columns =
            [
              {
                Expression =
                  FunctionExpr(AggregateFunction(Count, { AggregateOptions.Default with Distinct = false }), [])
                Alias = ""
              }
            ]
      })

[<Fact>]
let ``Analyze count(distinct col)`` () =
  testParsedQuery
    "SELECT count(distinct int_col) from table"
    (Analyzer.transformQuery testTable)
    (SelectQuery
      { defaultQuery with
          Columns =
            [
              {
                Expression =
                  FunctionExpr(
                    AggregateFunction(Count, { AggregateOptions.Default with Distinct = true }),
                    [ ColumnReference(1, IntegerType) ]
                  )
                Alias = ""
              }
            ]
      })

[<Fact>]
let ``Selecting columns from a table`` () =
  testParsedQuery
    "SELECT str_col, bool_col FROM table"
    (Analyzer.transformQuery testTable)
    (SelectQuery
      { defaultQuery with
          Columns =
            [
              { Expression = ColumnReference(0, StringType); Alias = "" }
              { Expression = ColumnReference(3, BooleanType); Alias = "" }
            ]
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
  HAVING count(int_col) > 1
  "

  testParsedQuery
    query
    (Analyzer.transformQuery testTable)
    (SelectQuery
      {
        Columns =
          [
            { Expression = ColumnReference(1, IntegerType); Alias = "colAlias" }
            {
              Expression =
                FunctionExpr(ScalarFunction Plus, [ ColumnReference(2, RealType); ColumnReference(1, IntegerType) ])
              Alias = ""
            }
            {
              Expression =
                FunctionExpr(AggregateFunction(Count, AggregateOptions.Default), [ ColumnReference(1, IntegerType) ])
              Alias = ""
            }
          ]
        Where =
          FunctionExpr(
            ScalarFunction And,
            [
              FunctionExpr(ScalarFunction Gt, [ ColumnReference(1, IntegerType); Constant(Value.Integer 0L) ])
              FunctionExpr(ScalarFunction Lt, [ ColumnReference(1, IntegerType); Constant(Value.Integer 10L) ])
            ]
          )
        From = Table testTable
        GroupingSets =
          [
            [
              ColumnReference(1, IntegerType)
              FunctionExpr(ScalarFunction Plus, [ ColumnReference(2, RealType); ColumnReference(1, IntegerType) ])
              FunctionExpr(AggregateFunction(Count, AggregateOptions.Default), [ ColumnReference(1, IntegerType) ])
            ]
          ]
        Having =
          FunctionExpr(
            ScalarFunction Gt,
            [
              FunctionExpr(AggregateFunction(Count, AggregateOptions.Default), [ ColumnReference(1, IntegerType) ])
              Constant(Value.Integer 1L)
            ]
          )
        OrderBy = []
      })
