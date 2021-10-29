module OpenDiffix.Core.AnalyzerTests

open Xunit
open FsUnit.Xunit

open AnalyzerTypes

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

let dataProvider = dummyDataProvider [ testTable ]
let queryContext = QueryContext.make AnonymizationParams.Default dataProvider

let defaultQuery =
  {
    TargetList = []
    Where = Boolean true |> Constant
    From = RangeTable(testTable, testTable.Name)
    GroupBy = []
    Having = Boolean true |> Constant
    OrderBy = []
    Limit = None
  }

let testParsedQuery queryString expected =
  queryString
  |> Parser.parse
  |> Analyzer.analyze queryContext
  |> should equal expected

let testQueryError queryString =
  (fun () -> queryString |> Parser.parse |> Analyzer.analyze queryContext |> ignore)
  |> shouldFail

[<Fact>]
let ``Analyze count(*)`` () =
  testParsedQuery
    "SELECT count(*) from table"
    { defaultQuery with
        TargetList =
          [
            {
              Expression =
                FunctionExpr(AggregateFunction(Count, { AggregateOptions.Default with Distinct = false }), [])
              Alias = "count"
              Tag = RegularTargetEntry
            }
          ]
    }

[<Fact>]
let ``Analyze count(distinct col)`` () =
  testParsedQuery
    "SELECT count(distinct int_col) from table"
    { defaultQuery with
        TargetList =
          [
            {
              Expression =
                FunctionExpr(
                  AggregateFunction(Count, { AggregateOptions.Default with Distinct = true }),
                  [ ColumnReference(1, IntegerType) ]
                )
              Alias = "count"
              Tag = RegularTargetEntry
            }
          ]
    }

[<Fact>]
let ``Selecting columns from a table`` () =
  testParsedQuery
    "SELECT str_col, bool_col FROM table"
    { defaultQuery with
        TargetList =
          [
            {
              Expression = ColumnReference(0, StringType)
              Alias = "str_col"
              Tag = RegularTargetEntry
            }
            {
              Expression = ColumnReference(3, BooleanType)
              Alias = "bool_col"
              Tag = RegularTargetEntry
            }
          ]
    }

[<Fact>]
let ``SELECT with alias, function, aggregate, GROUP BY, and WHERE-clause`` () =
  let query =
    @"
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
    {
      TargetList =
        [
          {
            Expression = ColumnReference(1, IntegerType)
            Alias = "colAlias"
            Tag = RegularTargetEntry
          }
          {
            Expression =
              FunctionExpr(ScalarFunction Add, [ ColumnReference(2, RealType); ColumnReference(1, IntegerType) ])
            Alias = "+"
            Tag = RegularTargetEntry
          }
          {
            Expression =
              FunctionExpr(AggregateFunction(Count, AggregateOptions.Default), [ ColumnReference(1, IntegerType) ])
            Alias = "count"
            Tag = RegularTargetEntry
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
      From = RangeTable(testTable, testTable.Name)
      GroupBy =
        [
          ColumnReference(1, IntegerType)
          FunctionExpr(ScalarFunction Add, [ ColumnReference(2, RealType); ColumnReference(1, IntegerType) ])
          FunctionExpr(AggregateFunction(Count, AggregateOptions.Default), [ ColumnReference(1, IntegerType) ])
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
      Limit = None
    }

[<Fact>]
let ``Selecting columns from an aliased table`` () =
  testParsedQuery
    "SELECT t.str_col, T.bool_col FROM table AS t"
    { defaultQuery with
        TargetList =
          [
            {
              Expression = ColumnReference(0, StringType)
              Alias = "str_col"
              Tag = RegularTargetEntry
            }
            {
              Expression = ColumnReference(3, BooleanType)
              Alias = "bool_col"
              Tag = RegularTargetEntry
            }
          ]
        From = RangeTable(testTable, "t")
    }

[<Fact>]
let ``Selecting columns from subquery`` () =
  testParsedQuery
    "SELECT int_col, x.aliased FROM (SELECT str_col AS aliased, int_col FROM table) x"
    { defaultQuery with
        TargetList =
          [
            {
              Expression = ColumnReference(1, IntegerType)
              Alias = "int_col"
              Tag = RegularTargetEntry
            }
            {
              Expression = ColumnReference(0, StringType)
              Alias = "aliased"
              Tag = RegularTargetEntry
            }
          ]
        From =
          SubQuery(
            { defaultQuery with
                TargetList =
                  [
                    {
                      Expression = ColumnReference(0, StringType)
                      Alias = "aliased"
                      Tag = RegularTargetEntry
                    }
                    {
                      Expression = ColumnReference(1, IntegerType)
                      Alias = "int_col"
                      Tag = RegularTargetEntry
                    }
                  ]
            },
            "x"
          )
    }

[<Fact>]
let ``Selecting columns from invalid table`` () =
  testQueryError "SELECT t.str_col FROM table"

[<Fact>]
let ``Selecting ambiguous table names`` () =
  testQueryError "SELECT count(*) FROM table, table AS Table"

[<Fact>]
let ``Star selecting columns from a table`` () =
  testParsedQuery
    "SELECT * FROM table"
    { defaultQuery with
        TargetList =
          [
            {
              Expression = ColumnReference(0, StringType)
              Alias = "str_col"
              Tag = RegularTargetEntry
            }
            {
              Expression = ColumnReference(1, IntegerType)
              Alias = "int_col"
              Tag = RegularTargetEntry
            }
            {
              Expression = ColumnReference(2, RealType)
              Alias = "float_col"
              Tag = RegularTargetEntry
            }
            {
              Expression = ColumnReference(3, BooleanType)
              Alias = "bool_col"
              Tag = RegularTargetEntry
            }
          ]
    }


type Tests(db: DBFixture) =
  let schema = db.DataProvider.GetSchema()

  let getTable name = Schema.findTable schema name

  let anonParams =
    {
      TableSettings =
        Map [
          "customers", { AidColumns = [ "id"; "company_name" ] }
          "customers_small", { AidColumns = [ "id"; "company_name" ] }
          "purchases", { AidColumns = [ "cid" ] }
        ]
      Salt = [||]
      Suppression = { LowThreshold = 2; LowMeanGap = 0.0; SD = 0. }
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      NoiseSD = 0.
    }

  let queryContext = QueryContext.make anonParams db.DataProvider

  let idColumn = ColumnReference(4, IntegerType)
  let companyColumn = ColumnReference(2, StringType)
  let aidColumns = [ companyColumn; idColumn ] |> ListExpr

  let analyzeQuery query =
    let query, _ =
      query
      |> Parser.parse
      |> Analyzer.analyze queryContext
      |> Analyzer.anonymize queryContext

    query

  let ensureQueryFails query error =
    try
      query |> analyzeQuery |> ignore
      failwith "Expected query to fail"
    with
    | ex -> ex.Message |> should equal error

  let sqlNoiseLayers query =
    query
    |> Parser.parse
    |> Analyzer.analyze queryContext
    |> Analyzer.anonymize queryContext
    |> snd

  let assertSqlSeed query (seedMaterial: string) =
    let expectedSeed = seedMaterial |> System.Text.Encoding.UTF8.GetBytes |> Hash.bytes
    (sqlNoiseLayers query).BucketSeed |> should equal expectedSeed

  [<Fact>]
  let ``Analyze count transforms`` () =
    let result = analyzeQuery "SELECT count(*), count(distinct id) FROM customers_small HAVING count(*) > 1"

    let countStar = FunctionExpr(AggregateFunction(DiffixCount, AggregateOptions.Default), [ aidColumns ])

    let countDistinct =
      FunctionExpr(
        AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = true }),
        [ aidColumns; idColumn ]
      )

    let expectedInTopQuery =
      [
        { Expression = countStar; Alias = "count"; Tag = RegularTargetEntry }
        { Expression = countDistinct; Alias = "count"; Tag = RegularTargetEntry }
      ]

    result.TargetList |> should equal expectedInTopQuery

    let expected = FunctionExpr(ScalarFunction Gt, [ countStar; 1L |> Integer |> Constant ])

    result.Having |> should equal expected

  [<Fact>]
  let ``Analyze JOINs`` () =
    let result = analyzeQuery "SELECT count(*) FROM customers_small JOIN purchases ON id = purchases.cid"

    let condition =
      FunctionExpr(ScalarFunction Equals, [ ColumnReference(4, IntegerType); ColumnReference(7, IntegerType) ])

    let customers = getTable "customers_small"
    let purchases = getTable "purchases"

    let expectedFrom =
      Join
        {
          Type = JoinType.InnerJoin
          Left = RangeTable(customers, customers.Name)
          Right = RangeTable(purchases, purchases.Name)
          On = condition
        }

    result.From |> should equal expectedFrom

  [<Fact>]
  let ``Analyze subqueries`` () =
    analyzeQuery "SELECT count(*) FROM (SELECT 1 FROM customers_small JOIN purchases ON id = purchases.cid) x"
    |> should
         equal
         { defaultQuery with
             TargetList =
               [
                 {
                   Expression =
                     FunctionExpr(
                       AggregateFunction(DiffixCount, AggregateOptions.Default),
                       [
                         ListExpr [
                           ColumnReference(1, StringType)
                           ColumnReference(2, IntegerType)
                           ColumnReference(3, IntegerType)
                         ]
                       ]
                     )
                   Alias = "count"
                   Tag = RegularTargetEntry
                 }
               ]
             From =
               SubQuery(
                 { defaultQuery with
                     TargetList =
                       [
                         { Expression = Constant(Integer 1L); Alias = ""; Tag = RegularTargetEntry }
                         {
                           Expression = ColumnReference(2, StringType)
                           Alias = "__aid_0"
                           Tag = AidTargetEntry
                         }
                         {
                           Expression = ColumnReference(4, IntegerType)
                           Alias = "__aid_1"
                           Tag = AidTargetEntry
                         }
                         {
                           Expression = ColumnReference(7, IntegerType)
                           Alias = "__aid_2"
                           Tag = AidTargetEntry
                         }
                       ]
                     From =
                       Join
                         {
                           Type = JoinType.InnerJoin
                           Left = RangeTable(getTable "customers_small", "customers_small")
                           Right = RangeTable(getTable "purchases", "purchases")
                           On =
                             FunctionExpr(
                               ScalarFunction Equals,
                               [ ColumnReference(4, IntegerType); ColumnReference(7, IntegerType) ]
                             )
                         }
                 },
                 "x"
               )
         }

  [<Fact>]
  let ``Reject limiting anonymizing subquery`` () =
    ensureQueryFails
      "SELECT count(*) FROM (SELECT city FROM customers_small LIMIT 1) t"
      "Limit is not allowed in anonymizing subqueries"

  [<Fact>]
  let ``Allow limiting top query`` () =
    analyzeQuery "SELECT count(*) FROM customers_small LIMIT 1" |> ignore

  [<Fact>]
  let ``SQL seed from column selection`` () =
    assertSqlSeed "SELECT city FROM customers_small" "customers_small.city"

  [<Fact>]
  let ``SQL seed from column generalization`` () =
    assertSqlSeed "SELECT substring(city, 1, 2) FROM customers_small" "substring,customers_small.city,1,2"

  [<Fact>]
  let ``SQL seed from multiple groupings from multiple tables`` () =
    assertSqlSeed
      "SELECT count(*) FROM customers_small JOIN purchases ON id = cid GROUP BY city, round(amount)"
      "customers_small.city,round,purchases.amount,1"

  [<Fact>]
  let ``SQL seeds from numeric ranges are consistent`` () =
    (sqlNoiseLayers "SELECT floor(age) FROM customers_small GROUP BY 1")
    |> should equal (sqlNoiseLayers "SELECT floor_by(cast(age AS real), 1.0) FROM customers_small GROUP BY 1")

    (sqlNoiseLayers "SELECT round(cast(age AS real)) FROM customers_small GROUP BY 1")
    |> should equal (sqlNoiseLayers "SELECT round_by(age, 1.0) FROM customers_small GROUP BY 1")

    (sqlNoiseLayers "SELECT ceil_by(age, 1.0) FROM customers_small GROUP BY 1")
    |> should equal (sqlNoiseLayers "SELECT ceil_by(age, 1) FROM customers_small GROUP BY 1")

  interface IClassFixture<DBFixture>
