module OpenDiffix.Core.QueryEngineTests

open Xunit
open FsUnit.Xunit

open QueryEngine

type Tests(db: DBFixture) =
  let tableSettings =
    Map [ //
      "customers", { AidColumns = [ "id" ] }
      "purchases", { AidColumns = [ "cid" ] }
      "customers_small", { AidColumns = [ "id" ] }
    ]

  let noiselessAnonParams =
    {
      TableSettings = tableSettings
      Salt = [||]
      AccessLevel = PublishTrusted
      Strict = false
      Suppression = { LowThreshold = 2; LowMeanGap = 0.0; LayerSD = 0. }
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      LayerNoiseSD = 0.
    }

  let noisyAnonParams = { AnonymizationParams.Default with TableSettings = tableSettings }

  let runQueryWithCustomAnonParams anonymizationParams query =
    let queryContext = QueryContext.make anonymizationParams db.DataProvider
    run queryContext query

  let runQuery = runQueryWithCustomAnonParams noiselessAnonParams

  [<Fact>]
  let ``query 1`` () =
    let expected =
      {
        Columns = [ { Name = "n"; Type = StringType } ]
        Rows = [ [| String "Water" |] ]
      }

    let queryResult = runQuery "SELECT name AS n FROM products WHERE id = 1"
    queryResult |> should equal expected

  [<Fact>]
  let ``query 2`` () =
    let expected =
      {
        Columns = [ { Name = "c1"; Type = IntegerType }; { Name = "c2"; Type = IntegerType } ]
        Rows = [ [| Integer 11L; Integer 4L |] ]
      }

    let queryResult = runQuery "SELECT count(*) AS c1, count(DISTINCT length(name)) AS c2 FROM products"
    queryResult |> should equal expected

  [<Fact>]
  let ``query 3`` () =
    let expected =
      {
        Columns = [ { Name = "name"; Type = StringType }; { Name = "sum"; Type = RealType } ]
        Rows = [ [| String "Chicken"; Real 12.81 |] ]
      }

    let queryResult = runQuery "SELECT name, SUM(price) FROM products GROUP BY 1 HAVING length(name) = 7"
    queryResult |> should equal expected

  [<Fact>]
  let ``query 4`` () =
    let expected =
      {
        Columns =
          [
            { Name = "city"; Type = StringType }
            { Name = "count"; Type = IntegerType }
            { Name = "sum"; Type = IntegerType }
            { Name = "count_noise"; Type = RealType }
            { Name = "count_noise"; Type = RealType }
            { Name = "count_noise"; Type = RealType }
            { Name = "sum_noise"; Type = RealType }
          ]
        Rows =
          [
            [| String "Berlin"; Integer 10L; Integer 295L; Real 0.0; Real 0.0; Real 0.0; Real 0.0 |]
            [| String "Rome"; Integer 10L; Integer 300L; Real 0.0; Real 0.0; Real 0.0; Real 0.0 |]
          ]
      }

    let queryResult =
      runQuery
        "SELECT city, count(distinct id), sum(age), count_noise(*), count_noise(id), count_noise(distinct id), sum_noise(age) FROM customers_small GROUP BY city"

    queryResult |> should equal expected

  [<Fact>]
  let ``query 5 - bucket expansion`` () =
    let queryResult = runQuery "SELECT city FROM customers_small"

    let expectedRows = List.collect (fun name -> [ for _i in 1 .. 10 -> [| String name |] ]) [ "Berlin"; "Rome" ]

    let expected = { Columns = [ { Name = "city"; Type = StringType } ]; Rows = expectedRows }

    queryResult |> should equal expected

  /// Returns the aggregate result of a query such as `SELECT count(*) FROM ...`
  let runQueryToInteger query =
    runQuery query
    |> fun result ->
         result.Rows
         |> List.head
         |> Array.head
         |> function
           | Integer i -> Integer i
           | other -> failwith $"Unexpected return '%A{other}'"

  // query 6 - query 9 removed (anonymizing JOINs, which are not supported now)
  [<Fact>]
  let ``query 10`` () =
    let expected =
      {
        Columns = [ { Name = "n"; Type = StringType } ]
        Rows = [ [| String "Water" |] ]
      }

    runQuery "SELECT p.name AS n FROM products AS p WHERE id = 1"
    |> should equal expected

  let equivalentQueries expectedQuery testQuery =
    let testResult = runQueryWithCustomAnonParams noisyAnonParams testQuery
    let expected = runQueryWithCustomAnonParams noisyAnonParams expectedQuery
    testResult |> should equal expected

  [<Fact>]
  let ``query 11`` () =
    let expected =
      {
        Columns = [ { Name = "n"; Type = StringType } ]
        Rows = [ [| String "1Water" |] ]
      }

    let queryResult = runQuery "SELECT CAST(id AS text) || name AS n FROM products WHERE id = 1 GROUP BY id, name"
    queryResult |> should equal expected

  [<Fact>]
  let ``query 12 - limit`` () =
    let expected =
      {
        Columns = [ { Name = "id"; Type = IntegerType } ]
        Rows = [ [| Integer 1L |]; [| Integer 2L |]; [| Integer 3L |] ]
      }

    let queryResult = runQuery "SELECT id FROM products LIMIT 3"
    queryResult |> should equal expected

  [<Fact>]
  let ``query 12 - group with rounding`` () =
    let queryResult = runQuery "SELECT round_by(age, 5), count(*) FROM customers_small GROUP BY 1"

    let expected =
      {
        Columns = [ { Name = "round_by"; Type = IntegerType }; { Name = "count"; Type = IntegerType } ]
        Rows = [ [| Integer 25L; Integer 7L |]; [| Integer 30L; Integer 7L |]; [| Integer 35L; Integer 6L |] ]
      }

    queryResult |> should equal expected

  [<Fact>]
  let ``query 13 - order by`` () =
    let queryResult = runQuery "SELECT age, count(*) FROM customers_small GROUP BY 1 ORDER BY 1"

    let expected =
      {
        Columns = [ { Name = "age"; Type = IntegerType }; { Name = "count"; Type = IntegerType } ]
        Rows = [ [| Integer 25L; Integer 7L |]; [| Integer 30L; Integer 7L |]; [| Integer 35L; Integer 6L |] ]
      }

    queryResult |> should equal expected

  [<Fact>]
  let ``query 14 - avg parity`` () =
    let expected = runQuery "SELECT sum(age) / cast(count(age), 'real') as avg FROM customers_small GROUP BY city"
    let queryResult = runQuery "SELECT avg(age) FROM customers_small GROUP BY city"

    queryResult |> should equal expected

  [<Fact>]
  let ``query 14 - avg_noise parity`` () =
    let expected =
      runQuery "SELECT sum_noise(age) / cast(count(age), 'real') as avg_noise FROM customers_small GROUP BY city"

    let queryResult = runQuery "SELECT avg_noise(age) FROM customers_small GROUP BY city"

    queryResult |> should equal expected

  [<Fact>]
  let ``Subquery wrappers produce consistent results`` () =
    equivalentQueries
      "SELECT p.name AS n FROM products AS p WHERE id = 1"
      "SELECT n FROM (SELECT p.name AS n FROM products AS p WHERE id = 1) x"

    equivalentQueries
      "SELECT count(*) AS c1, count(DISTINCT length(name)) AS c2 FROM products"
      "SELECT count(*) AS c1, count(DISTINCT length(name)) AS c2 FROM (SELECT name FROM products) x"

    equivalentQueries
      "SELECT count(*) FROM products as a LEFT JOIN products as b ON a.id = b.id"
      "SELECT count(*) FROM (SELECT a.id, b.id FROM products as a LEFT JOIN products as b ON a.id = b.id) x"

  [<Fact>]
  let ``Direct query can use diffix functions`` () =
    let expected =
      {
        Columns = [ { Name = "dc"; Type = IntegerType }; { Name = "lc"; Type = BooleanType } ]
        Rows = [ [| Integer 11L; Boolean false |] ]
      }

    let queryResult = runQuery "SELECT diffix_count(*, id) AS dc, diffix_low_count(id) AS lc FROM products"
    queryResult |> should equal expected

  [<Fact>]
  let ``Grouping order doesn't change results`` () =
    equivalentQueries
      "SELECT count(*) FROM customers_small GROUP BY round_by(age, 10), city ORDER BY 1"
      "SELECT count(*) FROM customers_small GROUP BY city, round_by(age, 10) ORDER BY 1"

  [<Fact>]
  let ``Duplicated grouping doesn't change results`` () =
    equivalentQueries
      "SELECT count(*) FROM customers_small GROUP BY city, city ORDER BY 1"
      "SELECT count(*) FROM customers_small GROUP BY city ORDER BY 1"

  [<Fact>]
  let ``Filtering and grouping doesn't change results`` () =
    equivalentQueries
      "SELECT count(*) FROM customers GROUP BY city HAVING city = 'Berlin'"
      "SELECT count(*) FROM customers WHERE city = 'Berlin'"

    equivalentQueries
      "SELECT count(*) FROM customers GROUP BY city, round_by(age, 10) HAVING city = 'Berlin' AND round_by(age, 10) = 20"
      "SELECT count(*) FROM customers WHERE city = 'Berlin' AND round_by(age, 10) = 20"

    equivalentQueries
      "SELECT count(*) FROM customers WHERE round_by(age, 10) = 20 GROUP BY city HAVING city = 'Berlin'"
      "SELECT count(*) FROM customers WHERE city = 'Berlin' AND round_by(age, 10) = 20"

  [<Fact>]
  let ``Anonymizing subquery`` () =
    let queryResult =
      runQuery
        """
          SELECT count(city), sum(count) FROM
            (SELECT city, count(*) FROM customers_small GROUP BY 1) t
          WHERE length(city) > 3
        """

    queryResult.Rows |> should equal [ [| Integer 2L; Integer 20L |] ]

  [<Fact>]
  let ``Joining anonymizing subqueries`` () =
    let queryResult =
      runQuery
        """
          SELECT t1.city, t1.count, t2.count FROM
            (SELECT city, count(*) FROM customers GROUP BY 1) t1
          LEFT JOIN
            (SELECT city, count(*) FROM customers_small GROUP BY 1) t2
          ON t1.city = t2.city
        """

    let expectedRows =
      [
        [| String "Paris"; Integer 26L; Value.Null |]
        [| String "Berlin"; Integer 77L; Integer 10L |]
        [| String "Rome"; Integer 50L; Integer 10L |]
        [| String "Madrid"; Integer 25L; Value.Null |]
        [| String "London"; Integer 25L; Value.Null |]
      ]

    queryResult.Rows |> should equal expectedRows

  interface IClassFixture<DBFixture>
