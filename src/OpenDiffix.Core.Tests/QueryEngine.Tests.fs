module OpenDiffix.Core.QueryEngineTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

type Tests(db: DBFixture) =
  let anonParams =
    {
      TableSettings =
        Map [
          "customers", { AidColumns = [ "id" ] } //
          "purchases", { AidColumns = [ "cid" ] } //
          "customers_small", { AidColumns = [ "id" ] } //
        ]
      Seed = 1
      MinimumAllowedAids = 2
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      Noise = { StandardDev = 1.; Cutoff = 0. }
    }

  let runQueryWithCustomAnonParams anonymizationParams query =
    QueryEngine.run db.DataProvider query anonymizationParams
    |> Async.RunSynchronously

  let runQuery = runQueryWithCustomAnonParams anonParams

  [<Fact>]
  let ``query 1`` () =
    let expected = { Columns = [ "n" ]; Rows = [ [| String "Water" |] ] }
    let queryResult = runQuery "SELECT name AS n FROM products WHERE id = 1"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 2`` () =
    let expected = { Columns = [ "c1"; "c2" ]; Rows = [ [| Integer 11L; Integer 4L |] ] }
    let queryResult = runQuery "SELECT COUNT(*) AS c1, COUNT(DISTINCT length(name)) AS c2 FROM products"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 3`` () =
    let expected = { Columns = [ "name"; "sum" ]; Rows = [ [| String "Chicken"; Real 12.81 |] ] }
    let queryResult = runQuery "SELECT name, SUM(price) FROM products GROUP BY 1 HAVING length(name) = 7"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 4`` () =
    let expected =
      {
        Columns = [ "city"; "count" ]
        // 10 + noise
        Rows = [ [| String "Berlin"; Integer 11L |]; [| String "Rome"; Integer 11L |] ]
      }

    let queryResult = runQuery "SELECT city, count(distinct id) FROM customers_small GROUP BY city"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 5 - bucket expansion`` () =
    let queryResult = runQuery "SELECT city FROM customers_small"

    let expectedRows = List.collect (fun name -> [ for _i in 1 .. 11 -> [| String name |] ]) [ "Berlin"; "Rome" ]

    let expected = { Columns = [ "city" ]; Rows = expectedRows }

    assertOkEqual queryResult expected

  /// Returns the aggregate result of a query such as `SELECT count(*) FROM ...`
  let runQueryToSingleNumericAggregate query =
    runQuery query
    |> Utils.unwrap
    |> fun result ->
         result.Rows
         |> List.head
         |> Array.head
         |> function
         | Integer i -> float i
         | Real r -> r
         | other -> failwith $"Unexpected return '%A{other}'"

  [<Fact>]
  let ``query 6 - cross join`` () =
    "SELECT count(*) FROM customers_small, purchases WHERE id = cid"
    |> runQueryToSingleNumericAggregate
    |> should (equalWithin 21) 72

  [<Fact>]
  let ``query 7 - inner join`` () =
    "SELECT count(*) FROM purchases join customers_small ON id = cid"
    |> runQueryToSingleNumericAggregate
    |> should (equalWithin 21) 72

  [<Fact>]
  let ``query 8 - left join`` () =
    "SELECT count(*) FROM customers_small LEFT JOIN purchases ON id = cid"
    |> runQueryToSingleNumericAggregate
    |> should (equalWithin 21) 72

  [<Fact>]
  let ``query 9 - right join`` () =
    // The underlying data looks like this:
    //
    // ID = null, CID = null, COUNT
    //     false,      false,    73
    //      true,      false,   445
    //      true,       true,     1
    //
    // The query should yield the 73 + 445 values,
    // but only the 73 where neither column is null
    // should be considered by the anonymizer.
    // There is a flattening of 1 and noise proportional to
    // the top group average of 7
    "SELECT count(*) FROM customers_small RIGHT JOIN purchases ON id = cid"
    |> runQueryToSingleNumericAggregate
    |> should (equalWithin 21) 72

  [<Fact>]
  let ``query 10`` () =
    let expected = { Columns = [ "n" ]; Rows = [ [| String "Water" |] ] }
    let queryResult = runQuery "SELECT p.name AS n FROM products AS p WHERE id = 1"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 11`` () =
    let outerJoinCount =
      // Note that this is an entirely bogus join forcing the joined table to return Null values only
      "SELECT count(*) FROM customers_small as c LEFT JOIN products as p ON c.city = p.name"
      |> runQueryToSingleNumericAggregate

    let plainCount =
      "SELECT count(*) FROM customers_small"
      |> runQueryToSingleNumericAggregate

    outerJoinCount |> should equal plainCount

    // Flattening by 2 and noise with std proportional to top group average of 5
    plainCount |> should (equalWithin 15) 31

  interface IClassFixture<DBFixture>
