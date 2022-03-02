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
      AccessLevel = PublishTrusted
      Suppression = { LowThreshold = 2; LowMeanGap = 0.0; LayerSD = 0. }
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      LayerNoiseSD = 0.
    }

  let queryContext = QueryContext.make anonParams db.DataProvider
  let queryContextUntrusted = QueryContext.make { anonParams with AccessLevel = PublishUntrusted } db.DataProvider

  let idColumn = ColumnReference(4, IntegerType)
  let companyColumn = ColumnReference(2, StringType)
  let aidColumns = [ companyColumn; idColumn ] |> ListExpr

  let analyzeQuery query =
    let query, _ =
      query
      |> Parser.parse
      |> Analyzer.analyze queryContext
      |> Normalizer.normalize
      |> Analyzer.anonymize queryContext

    query

  let analyzeQueryUntrusted query =
    let query, _ =
      query
      |> Parser.parse
      |> Analyzer.analyze queryContextUntrusted
      |> Normalizer.normalize
      |> Analyzer.anonymize queryContextUntrusted

    query

  let ensureQueryFails query error =
    try
      query |> analyzeQuery |> ignore
      failwith "Expected query to fail"
    with
    | ex -> ex.Message |> should equal error

  let ensureQueryFailsUntrusted query error =
    try
      query |> analyzeQueryUntrusted |> ignore
      failwith "Expected query to fail"
    with
    | ex -> ex.Message |> should equal error

  let sqlNoiseLayers query =
    query
    |> Parser.parse
    |> Analyzer.analyze queryContext
    |> Normalizer.normalize
    |> Analyzer.anonymize queryContext
    |> snd

  let assertSqlSeed query (seedMaterials: string seq) =
    let expectedSeed = Hash.strings 0UL seedMaterials
    (sqlNoiseLayers query).BucketSeed |> should equal expectedSeed

  let assertDefaultSqlSeed query =
    (sqlNoiseLayers query) |> should equal NoiseLayers.Default

  let assertNoLCF query =
    (analyzeQuery query).Having |> should equal (Boolean true |> Constant)

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

  // NOTE: We do QueryValidator testing in its respective test module. Here we just check, if it's invoked at all based
  // on whether the query is anonymizing.
  [<Fact>]
  let ``Detect subqueries touching tables with AID columns`` () =
    ensureQueryFails
      "SELECT count(*) FROM (SELECT 1 FROM customers_small) x"
      "Subqueries in anonymizing queries are not currently supported"

  [<Fact>]
  let ``Detect queries joining tables with AID columns`` () =
    ensureQueryFails
      "SELECT price FROM products JOIN customers_small ON true"
      "JOIN in anonymizing queries is not currently supported"

  [<Fact>]
  let ``Detect queries with disallowed generalizations in untrusted access level`` () =
    ensureQueryFailsUntrusted
      "SELECT substring(city, 2, 2) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    ensureQueryFailsUntrusted
      "SELECT floor_by(age, 3) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    ensureQueryFailsUntrusted
      "SELECT floor_by(age, 3.0) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    ensureQueryFailsUntrusted
      "SELECT floor_by(age, 5000000000.1) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    ensureQueryFailsUntrusted
      "SELECT width_bucket(age, 2, 200, 5) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

  [<Fact>]
  let ``Analyze queries with allowed generalizations in untrusted access level`` () =
    analyzeQueryUntrusted "SELECT substring(city, 1, 2) from customers" |> ignore
    analyzeQueryUntrusted "SELECT floor_by(age, 2) from customers" |> ignore
    analyzeQueryUntrusted "SELECT floor_by(age, 20) from customers" |> ignore
    analyzeQueryUntrusted "SELECT floor_by(age, 2.0) from customers" |> ignore
    analyzeQueryUntrusted "SELECT floor_by(age, 0.2) from customers" |> ignore
    analyzeQueryUntrusted "SELECT floor_by(age, 20.0) from customers" |> ignore
    analyzeQueryUntrusted "SELECT floor_by(age, 50.0) from customers" |> ignore
    analyzeQueryUntrusted "SELECT ceil_by(age, 50.0) from customers" |> ignore
    analyzeQueryUntrusted "SELECT round_by(age, 50.0) from customers" |> ignore
    // No generalization, either implicitly or explicitly
    analyzeQueryUntrusted "SELECT floor(age) from customers" |> ignore
    analyzeQueryUntrusted "SELECT age from customers" |> ignore

  [<Fact>]
  let ``Detect queries with disallowed bucket functions calls`` () =
    ensureQueryFailsUntrusted
      "SELECT round(2, age) from customers"
      "Primary argument for a bucket function has to be a simple column reference."

    ensureQueryFailsUntrusted
      "SELECT round(age, age) from customers"
      "Secondary arguments for a bucket function have to be constants."

  [<Fact>]
  let ``Default SQL seed from non-anonymizing queries`` () =
    assertDefaultSqlSeed "SELECT * FROM products"
    assertDefaultSqlSeed "SELECT count(*) FROM products"

  [<Fact>]
  let ``SQL seed from non-anonymizing queries using anonymizing aggregates`` () =
    assertSqlSeed "SELECT diffix_low_count(products.id), name FROM products GROUP BY name" [ "products.name" ]

  [<Fact>]
  let ``SQL seed from column selection`` () =
    assertSqlSeed "SELECT city FROM customers_small" [ "customers_small.city" ]

  [<Fact>]
  let ``SQL seed from column generalization`` () =
    assertSqlSeed "SELECT substring(city, 1, 2) FROM customers_small" [ "substring,customers_small.city,1,2" ]

  [<Fact>]
  let ``SQL seeds from numeric ranges are consistent`` () =
    // TODO: temporarily broken, because `floor(age)` seeds as `age` and `floor_by(cast(...), 1.0)` seeds as `floor,age,1.0`
    // (sqlNoiseLayers "SELECT floor(age) FROM customers_small GROUP BY 1")
    // |> should equal (sqlNoiseLayers "SELECT floor_by(cast(age AS real), 1.0) FROM customers_small GROUP BY 1")

    // (sqlNoiseLayers "SELECT round(cast(age AS real)) FROM customers_small GROUP BY 1")
    // |> should equal (sqlNoiseLayers "SELECT round_by(age, 1.0) FROM customers_small GROUP BY 1")

    (sqlNoiseLayers "SELECT ceil_by(age, 1.0) FROM customers_small GROUP BY 1")
    |> should equal (sqlNoiseLayers "SELECT ceil_by(age, 1) FROM customers_small GROUP BY 1")

  [<Fact>]
  let ``SQL seed from rounding cast`` () =
    assertSqlSeed "SELECT cast(amount AS integer) FROM purchases" [ "round,purchases.amount,1" ]

  [<Fact>]
  let ``Default SQL seed from non-anonymizing rounding cast`` () =
    assertDefaultSqlSeed "SELECT cast(price AS integer) FROM products"

  [<Fact>]
  let ``Constant bucket labels are rejected`` () =
    ensureQueryFails
      "SELECT age, round(1) FROM customers_small GROUP BY 2"
      "Constant expressions can not be used for defining buckets."

  [<Fact>]
  let ``Constants targets aren't used for implicit bucket grouping and don't impact the seed`` () =
    (sqlNoiseLayers "SELECT round(1) FROM customers_small")
    |> should equal (sqlNoiseLayers "SELECT round(2) FROM customers_small")

    (sqlNoiseLayers "SELECT age, round(1) FROM customers_small")
    |> should equal (sqlNoiseLayers "SELECT age, round(2) FROM customers_small")

  [<Fact>]
  let ``No low-count filtering for non-grouping, non-aggregate queries with only constants`` () =
    assertNoLCF "SELECT round(1) FROM customers_small"

  interface IClassFixture<DBFixture>
