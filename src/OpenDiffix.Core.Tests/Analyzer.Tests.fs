module OpenDiffix.Core.AnalyzerTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes
open OpenDiffix.Core.AnonymizerTypes

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
    GroupingSets = [ GroupingSet [] ]
    Having = Boolean true |> Constant
    OrderBy = []
  }

let testParsedQuery queryString callback (expected: Query) =
  let testResult = queryString |> Parser.parse |> Result.bind callback
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
                Alias = "count"
                Junk = false
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
                Alias = "count"
                Junk = false
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
              { Expression = ColumnReference(0, StringType); Alias = "str_col"; Junk = false }
              { Expression = ColumnReference(3, BooleanType); Alias = "bool_col"; Junk = false }
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
            { Expression = ColumnReference(1, IntegerType); Alias = "colAlias"; Junk = false }
            {
              Expression =
                FunctionExpr(ScalarFunction Plus, [ ColumnReference(2, RealType); ColumnReference(1, IntegerType) ])
              Alias = "+"
              Junk = false
            }
            {
              Expression =
                FunctionExpr(AggregateFunction(Count, AggregateOptions.Default), [ ColumnReference(1, IntegerType) ])
              Alias = "count"
              Junk = false
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
            GroupingSet [
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

type Tests(db: DBFixture) =
  let anonParams =
    {
      TableSettings =
        Map [
          "customers", { AidColumns = [ "id" ] }
          "customers_small", { AidColumns = [ "id" ] }
          "purchases", { AidColumns = [ "cid" ] }
        ]
      Seed = 1
      LowCountThreshold = { Threshold.Default with Lower = 5; Upper = 7 }
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      Noise = { StandardDev = 1.; Cutoff = 0. }
    }

  let idColumn = ColumnReference(3, IntegerType)

  let analyzeQuery query =
    query
    |> Parser.parse
    |> Result.bind (fun parseTree -> Analyzer.analyze db.Connection anonParams parseTree |> Async.RunSynchronously)
    |> Utils.unwrap
    |> function
    | SelectQuery s -> s
    | _other -> failwith "Expected a top-level SELECT query"


  [<Fact>]
  let ``Analyze count transforms`` () =
    let result =
      analyzeQuery
        "
    SELECT count(*), count(distinct id)
    FROM customers_small
    HAVING count(*) > 1
    "

    let countStar = FunctionExpr(AggregateFunction(DiffixCount, AggregateOptions.Default), [ idColumn ])

    let countDistinct =
      FunctionExpr(AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = true }), [ idColumn ])

    let expected = [ { Expression = countStar; Alias = "count"; Junk = false }; { Expression = countDistinct; Alias = "count"; Junk = false } ]
    result.Columns |> should equal expected

    let expected = FunctionExpr(ScalarFunction Gt, [ countStar; 1L |> Integer |> Constant ])
    result.Having |> should equal expected

  interface IClassFixture<DBFixture>
