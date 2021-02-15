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
      LowCountParams = { Lower = 5.; Mean = 6.; StandardDev = 1. }
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      Noise = { StandardDev = 1.; Cutoff = 0. }
    }

  let runQueryWithCustomAnonParams anonymizationParams query =
    QueryEngine.run db.Connection query anonymizationParams
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
    let expected = { Columns = [ "diffix_count" ]; Rows = [ [| Integer 11L |] ] }
    let queryResult = runQuery "SELECT DIFFIX_COUNT(DISTINCT id) FROM products"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 5`` () =
    let expected = { Columns = [ "diffix_count" ]; Rows = [ [| Integer 11L |] ] }
    let queryResult = runQuery "SELECT DIFFIX_COUNT(id) FROM products"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 6`` () =
    let expected =
      {
        Columns = [ "city"; "count" ]
        Rows = [ [| String "Berlin"; Integer 10L |]; [| String "Rome"; Integer 10L |] ]
      }

    let queryResult = runQuery "SELECT city, count(distinct id) FROM customers_small GROUP BY city"
    assertOkEqual queryResult expected

  interface IClassFixture<DBFixture>
