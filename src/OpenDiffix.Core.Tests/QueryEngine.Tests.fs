module OpenDiffix.Core.QueryEngineTests

open Xunit
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
        Rows = [ [| String "Berlin"; Integer 10L |]; [| String "Rome"; Integer 10L |] ]
      }

    let queryResult = runQuery "SELECT city, count(distinct id) FROM customers_small GROUP BY city"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 5 - bucket expansion`` () =
    let queryResult = runQuery "SELECT city FROM customers_small"

    let expectedRows = List.collect (fun name -> [ for _i in 1 .. 10 -> [| String name |] ]) [ "Berlin"; "Rome" ]

    let expected = { Columns = [ "city" ]; Rows = expectedRows }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 6 - cross join`` () =
    let queryResult = runQuery "SELECT count(*) FROM customers_small, purchases WHERE id = cid"

    let expected = { Columns = [ "count" ]; Rows = [ [| Integer 72L |] ] }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 7 - inner join`` () =
    let queryResult = runQuery "SELECT count(*) FROM purchases join customers_small ON id = cid"

    let expected = { Columns = [ "count" ]; Rows = [ [| Integer 72L |] ] }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 8 - left join`` () =
    let queryResult = runQuery "SELECT count(*) FROM customers_small LEFT JOIN purchases ON id = cid"

    let expected = { Columns = [ "count" ]; Rows = [ [| Integer 72L |] ] }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 9 - right join`` () =
    let queryResult = runQuery "SELECT count(*) FROM customers_small RIGHT JOIN purchases ON id = cid"

    let expected = { Columns = [ "count" ]; Rows = [ [| Integer 72L |] ] }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 10`` () =
    let expected = { Columns = [ "n" ]; Rows = [ [| String "Water" |] ] }
    let queryResult = runQuery "SELECT p.name AS n FROM products AS p WHERE id = 1"
    assertOkEqual queryResult expected

  interface IClassFixture<DBFixture>
