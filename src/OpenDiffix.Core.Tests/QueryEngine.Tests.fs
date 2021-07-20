module OpenDiffix.Core.QueryEngineTests

open Xunit
open FsUnit.Xunit

open QueryEngine

type Tests(db: DBFixture) =
  let anonParams =
    {
      TableSettings =
        Map [
          "customers", { AidColumns = [ "id" ] } //
          "purchases", { AidColumns = [ "cid" ] } //
          "customers_small", { AidColumns = [ "id" ] } //
        ]
      Salt = 1UL
      MinimumAllowedAids = 2
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      Noise = { StandardDev = 1.; Cutoff = 0. }
    }

  let runQueryWithCustomAnonParams anonymizationParams query =
    let context = EvaluationContext.make anonymizationParams db.DataProvider
    run context query

  let runQuery = runQueryWithCustomAnonParams anonParams

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
        Columns = [ { Name = "city"; Type = StringType }; { Name = "count"; Type = IntegerType } ]
        Rows = [ [| String "Berlin"; Integer 10L |]; [| String "Rome"; Integer 10L |] ]
      }

    let queryResult = runQuery "SELECT city, count(distinct id) FROM customers_small GROUP BY city"
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

  [<Fact>]
  let ``query 6 - cross join`` () =
    "SELECT count(*) FROM customers_small, purchases WHERE id = cid"
    |> runQueryToInteger
    |> should equal (Integer 72L)

  [<Fact>]
  let ``query 7 - inner join`` () =
    "SELECT count(*) FROM purchases join customers_small ON id = cid"
    |> runQueryToInteger
    |> should equal (Integer 72L)

  [<Fact>]
  let ``query 8 - left join`` () =
    "SELECT count(*) FROM customers_small LEFT JOIN purchases ON id = cid"
    |> runQueryToInteger
    |> should equal (Integer 72L)

  [<Fact>]
  let ``query 9 - right join`` () =
    // The underlying data looks like this:
    //
    // customer.ID = null, purchases.CID = null, COUNT
    // false,              false,                73
    // true,               false,                445
    // true,               true,                 1
    //
    // The query should yield the 73 + 445 values,
    // There are no flattening needed for purchases,
    // and a flattening of 1 for the customers table.
    // When anonymizing the customers table we have 445
    // unaccounted for values
    // This should give us: 73 - 1 + 445 - 1 = 516
    "SELECT count(*) FROM customers_small RIGHT JOIN purchases ON id = cid"
    |> runQueryToInteger
    |> should equal (Integer 516L)

  [<Fact>]
  let ``query 10`` () =
    let expected =
      {
        Columns = [ { Name = "n"; Type = StringType } ]
        Rows = [ [| String "Water" |] ]
      }

    runQuery "SELECT p.name AS n FROM products AS p WHERE id = 1"

  let equivalentQueries expectedQuery testQuery =
    let testResult = runQuery testQuery
    let expected = runQuery expectedQuery
    testResult |> should equal expected

  [<Fact>]
  let ``query 11`` () =
    let expected =
      {
        Columns = [ { Name = "n"; Type = StringType } ]
        Rows = [ [| String "1Water" |] ]
      }

    let queryResult = runQuery "SELECT CAST(id AS text) || name AS n FROM products WHERE id = 1"
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
      "SELECT count(*) FROM customers_small LEFT JOIN purchases ON id = cid"
      "SELECT count(*) FROM (SELECT id, cid FROM customers_small LEFT JOIN purchases ON id = cid) x"

  interface IClassFixture<DBFixture>
