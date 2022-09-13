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
    AnonymizationContext = None
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
let ``Analyze avg(int_col) produces save division with nullif`` () =
  testParsedQuery
    "SELECT avg(int_col) from table"
    { defaultQuery with
        TargetList =
          [
            {
              Expression =
                FunctionExpr(
                  ScalarFunction Divide,
                  [
                    FunctionExpr(
                      ScalarFunction Cast,
                      [
                        FunctionExpr(
                          AggregateFunction(Sum, { Distinct = false; OrderBy = [] }),
                          [ ColumnReference(1, IntegerType) ]
                        )
                        Constant(String "real")
                      ]
                    )
                    FunctionExpr(
                      ScalarFunction NullIf,
                      [
                        FunctionExpr(
                          AggregateFunction(Count, { Distinct = false; OrderBy = [] }),
                          [ ColumnReference(1, IntegerType) ]
                        )
                        Constant(Integer 0L)
                      ]
                    )
                  ]
                )
              Alias = "avg"
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
let ``Selecting cast columns from a table`` () =
  testParsedQuery
    "SELECT cast(bool_col, 'text') FROM table"
    { defaultQuery with
        TargetList =
          [
            {
              Expression =
                FunctionExpr(ScalarFunction Cast, [ ColumnReference(3, BooleanType); Constant(String "text") ])
              // Should not be `cast`.
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
      AnonymizationContext = None
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
      Strict = false
      Suppression = { LowThreshold = 2; LowMeanGap = 0.0; LayerSD = 0. }
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      LayerNoiseSD = 0.
      RecoverOutliers = true
    }

  let queryContext accessLevel =
    QueryContext.make { anonParams with AccessLevel = accessLevel } db.DataProvider

  let idColumn = ColumnReference(4, IntegerType)
  let companyColumn = ColumnReference(2, StringType)
  let aidColumns = [ companyColumn; idColumn ] |> ListExpr

  let analyzeQuery accessLevel query =
    query
    |> Parser.parse
    |> Analyzer.analyze (queryContext accessLevel)
    |> Normalizer.normalize
    |> Analyzer.compile (queryContext accessLevel)

  let analyzeTrustedQuery = analyzeQuery PublishTrusted
  let analyzeDirectQuery = analyzeQuery Direct
  let analyzeUntrustedQuery = analyzeQuery PublishUntrusted

  let assertQueryFails accessLevel query error =
    try
      query |> (analyzeQuery accessLevel) |> ignore
      failwith "Expected query to fail"
    with
    | ex -> ex.Message |> should equal error

  let assertTrustedQueryFails = assertQueryFails PublishTrusted
  let assertDirectQueryFails = assertQueryFails Direct
  let assertUntrustedQueryFails = assertQueryFails PublishUntrusted

  let assertSqlSeedWithFilter query (seedMaterials: string seq) baseLabels =
    let expectedSeed = Hash.strings 0UL seedMaterials

    (analyzeTrustedQuery query).AnonymizationContext
    |> should equal (Some { BucketSeed = expectedSeed; BaseLabels = baseLabels })

  let assertSqlSeed query (seedMaterials: string seq) =
    assertSqlSeedWithFilter query seedMaterials []

  let assertEqualAnonContexts query1 query2 =
    (analyzeTrustedQuery query1).AnonymizationContext
    |> should equal (analyzeTrustedQuery query2).AnonymizationContext

  let assertNoAnonContext query =
    (analyzeTrustedQuery query).AnonymizationContext |> should equal None

  [<Fact>]
  let ``Analyze count transforms`` () =
    let result = analyzeTrustedQuery "SELECT count(*), count(distinct id) FROM customers_small HAVING count(*) > 1"

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
  let ``Fail on unsupported aggregate in non-direct access level`` () =
    assertTrustedQueryFails
      "SELECT diffix_count(*, age) FROM customers"
      "Aggregate not supported in anonymizing queries."

    assertTrustedQueryFails
      "SELECT diffix_count_noise(*, age) FROM customers"
      "Aggregate not supported in anonymizing queries."

  [<Fact>]
  let ``Allow count(*), count(column) and count(distinct column) (with noise versions)`` () =
    analyzeTrustedQuery "SELECT count(*) FROM customers" |> ignore
    analyzeTrustedQuery "SELECT count(age) FROM customers" |> ignore
    analyzeTrustedQuery "SELECT count_noise(*) FROM customers" |> ignore
    analyzeTrustedQuery "SELECT count_noise(age) FROM customers" |> ignore
    analyzeTrustedQuery "SELECT count(distinct age) FROM customers" |> ignore
    analyzeTrustedQuery "SELECT count_noise(distinct age) FROM customers" |> ignore

  [<Fact>]
  let ``Allow sum(column) and sum_noise(column)`` () =
    analyzeTrustedQuery "SELECT sum(age) FROM customers" |> ignore
    analyzeTrustedQuery "SELECT sum_noise(age) FROM customers" |> ignore

  [<Fact>]
  let ``Fail on disallowed count`` () =
    assertTrustedQueryFails
      "SELECT count(age + id) FROM customers"
      "Only count(column) is supported in anonymizing queries."

  [<Fact>]
  let ``Fail on disallowed sum`` () =
    assertTrustedQueryFails
      "SELECT sum(distinct age) FROM customers"
      "Only sum(column) is supported in anonymizing queries."

    assertTrustedQueryFails
      "SELECT sum(age + id) FROM customers"
      "Only sum(column) is supported in anonymizing queries."

    assertTrustedQueryFails
      "SELECT sum_noise(distinct age) FROM customers"
      "Only sum(column) is supported in anonymizing queries."

    assertTrustedQueryFails
      "SELECT sum_noise(age + id) FROM customers"
      "Only sum(column) is supported in anonymizing queries."

  [<Fact>]
  let ``Disallow multiple low count aggregators`` () =
    assertDirectQueryFails
      "SELECT count(*), diffix_low_count(age), diffix_low_count(first_name) FROM customers"
      "A single low count aggregator is allowed in a query."

  [<Fact>]
  let ``Allow WHERE clause in untrusted mode`` () =
    analyzeUntrustedQuery "SELECT count(*) FROM customers WHERE city = ''"

  [<Fact>]
  let ``Reject WHERE clause over AID column`` () =
    assertUntrustedQueryFails
      "SELECT count(*) FROM customers WHERE round(id, 10) = 0"
      "AID columns can't be referenced by pre-anonymization filters."

  [<Fact>]
  let ``Allow anonymizing queries with JOINs`` () =
    analyzeTrustedQuery "SELECT count(*) FROM customers AS c1 JOIN customers AS c2 ON c1.id = c2.id"

  [<Fact>]
  let ``Reject anonymizing queries with CROSS JOINs`` () =
    assertTrustedQueryFails
      "SELECT count(*) FROM customers AS c1, customers AS c2"
      "`CROSS JOIN` in anonymizing queries is not supported."

  [<Fact>]
  let ``Reject anonymizing queries with subqueries`` () =
    assertTrustedQueryFails
      "SELECT count(*) FROM customers AS c JOIN (SELECT id FROM customers) t ON c.id = t.id"
      "Subqueries in anonymizing queries are not supported."

  [<Fact>]
  let ``Reject anonymizing queries with invalid JOIN filters`` () =
    assertTrustedQueryFails
      "SELECT count(*) FROM customers AS c1 JOIN customers AS c2 ON c1.id > c2.id"
      "Only equalities between simple column references are supported as `JOIN` filters in anonymizing queries."

    assertTrustedQueryFails
      "SELECT count(*) FROM customers AS c1 JOIN customers AS c2 ON c1.id = 0"
      "Only equalities between simple column references are supported as `JOIN` filters in anonymizing queries."

    assertTrustedQueryFails
      "SELECT count(*) FROM customers AS c1 JOIN customers AS c2 ON round_by(c1.id, 10) = c2.id"
      "Only equalities between simple column references are supported as `JOIN` filters in anonymizing queries."

  [<Fact>]
  let ``Reject anonymizing queries with OR filters`` () =
    assertTrustedQueryFails
      "SELECT count(*) FROM customers AS c1 JOIN customers AS c2 ON c1.id = c2.id OR c1.id = c2.id"
      "Combining `JOIN` filters using `OR` in anonymizing queries is not supported."

  [<Fact>]
  let ``Allow anonymizing subquery`` () =
    analyzeTrustedQuery "SELECT count(city) FROM (SELECT city FROM customers) x"

  [<Fact>]
  let ``Allow LIMIT clause in anonymizing subqueries`` () =
    analyzeTrustedQuery "SELECT count(*) FROM customers LIMIT 1"

  [<Fact>]
  let ``Allow HAVING clause in anonymizing subqueries`` () =
    analyzeTrustedQuery "SELECT count(*) FROM customers GROUP BY city HAVING length(city) > 3"

  [<Fact>]
  let ``Reject unsupported WHERE clause in anonymizing subqueries`` () =
    assertTrustedQueryFails
      "SELECT count(*) FROM customers WHERE first_name <> ''"
      "Only equalities between a generalization and a constant are allowed as filters in anonymizing queries."

    assertTrustedQueryFails
      "SELECT count(*) FROM customers WHERE age = 20 OR city = 'London'"
      "Only equalities between a generalization and a constant are allowed as filters in anonymizing queries."

  [<Fact>]
  let ``Allow supported WHERE clause in anonymizing subqueries`` () =
    analyzeTrustedQuery "SELECT count(*) FROM customers WHERE floor_by(age, 10) = 20 AND city = 'London'"

  [<Fact>]
  let ``Don't validate not anonymizing queries for unsupported anonymization features`` () =
    // Subqueries, JOINs, WHEREs, other aggregators etc.
    analyzeDirectQuery
      "SELECT sum(z.age) FROM (SELECT t.age FROM customers JOIN customers AS t ON true) z WHERE z.age=0"

  [<Fact>]
  let ``Require AID argument for count_histogram`` () =
    analyzeTrustedQuery "SELECT count_histogram(id) FROM customers" |> ignore
    analyzeTrustedQuery "SELECT count_histogram(id, 3) FROM customers" |> ignore
    assertTrustedQueryFails "SELECT count_histogram(age, 3) from customers" "count_histogram requires an AID argument."

  [<Fact>]
  let ``Require constant integer for count_histogram bin size`` () =
    let errorMsg = "count_histogram bin size must be a constant positive integer."
    assertTrustedQueryFails "SELECT count_histogram(age, -3) from customers" errorMsg
    assertTrustedQueryFails "SELECT count_histogram(age, age) from customers" errorMsg
    assertTrustedQueryFails "SELECT count_histogram(age, 'text') from customers" errorMsg

  [<Fact>]
  let ``Detect queries with disallowed generalizations in untrusted access level`` () =
    assertUntrustedQueryFails
      "SELECT substring(city, 2, 2) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    assertUntrustedQueryFails
      "SELECT floor_by(age, 3) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    assertUntrustedQueryFails
      "SELECT floor_by(age, 3.0) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    assertUntrustedQueryFails
      "SELECT floor_by(age, 5000000000.1) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    assertUntrustedQueryFails
      "SELECT ceil_by(age, 2) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    assertUntrustedQueryFails
      "SELECT width_bucket(age, 2, 200, 5) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    assertUntrustedQueryFails
      "SELECT width_bucket(age, 2, 200, 5) from customers"
      "Generalization used in the query is not allowed in untrusted access level."

    assertUntrustedQueryFails
      "SELECT count_histogram(id, 3) from customers"
      "count_histogram bin size must be a money-aligned value (1, 2, 5, 10, ...)."

  [<Fact>]
  let ``Analyze queries with allowed generalizations in untrusted access level`` () =
    analyzeUntrustedQuery "SELECT substring(city, 1, 2) from customers" |> ignore
    analyzeUntrustedQuery "SELECT floor_by(age, 2) from customers" |> ignore
    analyzeUntrustedQuery "SELECT floor_by(age, 20) from customers" |> ignore
    analyzeUntrustedQuery "SELECT floor_by(age, 2.0) from customers" |> ignore
    analyzeUntrustedQuery "SELECT floor_by(age, 0.2) from customers" |> ignore
    analyzeUntrustedQuery "SELECT floor_by(age, 20.0) from customers" |> ignore
    analyzeUntrustedQuery "SELECT floor_by(age, 50.0) from customers" |> ignore
    analyzeUntrustedQuery "SELECT round_by(age, 50.0) from customers" |> ignore
    analyzeUntrustedQuery "SELECT count_histogram(id, 5) from customers" |> ignore

    analyzeUntrustedQuery "SELECT date_trunc('month', last_seen) from customers"
    |> ignore

    // No generalization, either implicitly or explicitly
    analyzeUntrustedQuery "SELECT floor(age) from customers" |> ignore
    analyzeUntrustedQuery "SELECT age from customers" |> ignore

  [<Fact>]
  let ``Detect queries with disallowed bucket functions calls`` () =
    assertTrustedQueryFails
      "SELECT round(2, age) from customers"
      "Primary argument for a generalization expression has to be a simple column reference."

    assertTrustedQueryFails
      "SELECT round(age, age) from customers"
      "Secondary arguments for a generalization expression have to be constants."

  [<Fact>]
  let ``Default SQL seed from non-anonymizing queries`` () =
    assertNoAnonContext "SELECT * FROM products"
    assertNoAnonContext "SELECT count(*) FROM products"

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
    // assertEqualAnonContexts
    //  "SELECT floor(age) FROM customers_small GROUP BY 1"
    //  "SELECT floor_by(cast(age AS real), 1.0) FROM customers_small GROUP BY 1"
    // assertEqualAnonContexts
    //  "SELECT round(cast(age AS real)) FROM customers_small GROUP BY 1"
    //  "SELECT round_by(age, 1.0) FROM customers_small GROUP BY 1"

    assertEqualAnonContexts
      "SELECT ceil_by(age, 1.0) FROM customers_small GROUP BY 1"
      "SELECT ceil_by(age, 1) FROM customers_small GROUP BY 1"

  [<Fact>]
  let ``SQL seed from rounding cast`` () =
    assertSqlSeed "SELECT cast(amount AS integer) FROM purchases" [ "round,purchases.amount,1" ]

  [<Fact>]
  let ``SQL seed from non-rounding cast`` () =
    assertSqlSeed "SELECT cast(city AS integer) FROM customers" [ "customers.city" ]

  [<Fact>]
  let ``Default SQL seed from non-anonymizing rounding cast`` () =
    assertNoAnonContext "SELECT cast(price AS integer) FROM products"

  [<Fact>]
  let ``SQL seed from single filter`` () =
    assertSqlSeedWithFilter
      "SELECT COUNT(*) FROM customers WHERE substring(city, 1, 2) = 'Lo'"
      [ "substring,customers.city,1,2" ]
      [ String "Lo" ]

  let ``SQL seed from multiple filters`` () =
    assertSqlSeedWithFilter
      "SELECT COUNT(*) FROM customers WHERE age = 20 AND city = 'London'"
      [ "customers.age"; "customers.city" ]
      [ Integer 20L; String "London" ]

  [<Fact>]
  let ``Constant bucket labels are ignored`` () =
    assertEqualAnonContexts
      "SELECT age, round(1) FROM customers_small GROUP BY 1, 2"
      "SELECT age, round(1) FROM customers_small GROUP BY 1"

    assertEqualAnonContexts
      "SELECT 1, COUNT(*) FROM customers_small GROUP BY 1"
      "SELECT 1, COUNT(*) FROM customers_small"

  [<Fact>]
  let ``Constants targets aren't used for implicit bucket grouping and don't impact the seed`` () =
    assertEqualAnonContexts "SELECT round(1) FROM customers_small" "SELECT round(2) FROM customers_small"
    assertEqualAnonContexts "SELECT age, round(1) FROM customers_small" "SELECT age, round(2) FROM customers_small"

  [<Fact>]
  let ``No low-count filtering for non-grouping, non-aggregate queries with only constants`` () =
    (analyzeTrustedQuery "SELECT round(1) FROM customers_small").Having
    |> should equal (Boolean true |> Constant)

  interface IClassFixture<DBFixture>
