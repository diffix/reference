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
          "customers_small", { AidColumns = [ "id" ] } //
          "purchases", { AidColumns = [ "cid" ] } //
        ]
      Seed = 1
      LowCountThreshold = { Threshold.Default with Lower = 4; Upper = 5 }
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      Noise = { StandardDev = 1.; Cutoff = 0. }
    }

  let runQuery query = QueryEngine.run db.Connection query anonParams |> Async.RunSynchronously

  let runQueryGetColumns query = runQuery query |> Result.map (fun result -> result.Columns)

  let runQueryGetRows query = runQuery query |> Result.map (fun result -> List.map Row.GetValues result.Rows)

  [<Fact>]
  let ``query 1`` () =
    let query = "SELECT name AS n FROM products WHERE id = 1"
    assertOkEqual (runQueryGetColumns query) [ "n" ]
    assertOkEqual (runQueryGetRows query) [ [| String "Water" |] ]

  [<Fact>]
  let ``query 2`` () =
    let query = "SELECT COUNT(*) AS c1, COUNT(DISTINCT length(name)) AS c2 FROM products"
    assertOkEqual (runQueryGetColumns query) [ "c1"; "c2" ]
    assertOkEqual (runQueryGetRows query) [ [| Integer 11L; Integer 4L |] ]

  [<Fact>]
  let ``query 3`` () =
    let query = "SELECT name, SUM(price) FROM products GROUP BY 1 HAVING length(name) = 7"
    assertOkEqual (runQueryGetColumns query) [ "name"; "sum" ]
    assertOkEqual (runQueryGetRows query) [ [| String "Chicken"; Real 12.81 |] ]

  [<Fact>]
  let ``query 4`` () =
    let query = "SELECT count(DISTINCT id) FROM customers_small"
    assertOkEqual (runQueryGetColumns query) [ "count" ]
    assertOkEqual (runQueryGetRows query) [ [| Integer 13L |] ]

  [<Fact>]
  let ``query 5`` () =
    let query = "SELECT count(id) FROM customers_small"
    assertOkEqual (runQueryGetColumns query) [ "count" ]
    assertOkEqual (runQueryGetRows query) [ [| Integer 39L |] ]

  [<Fact>]
  let ``query 6`` () =
    let query = "SELECT city FROM customers_small"

    let expected =
      [
        [| String "Berlin" |]
        [| String "Berlin" |]
        [| String "Berlin" |]
        [| String "Berlin" |]
        [| String "Berlin" |]
      ]

    assertOkEqual (runQueryGetColumns query) [ "city" ]
    assertOkEqual (runQueryGetRows query) expected

  interface IClassFixture<DBFixture>
