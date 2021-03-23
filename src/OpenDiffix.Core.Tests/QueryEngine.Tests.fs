module OpenDiffix.Core.QueryEngineTests

open Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

type Tests(db: DBFixture) =
  let aidSetting name = {Name = name; MinimumAllowed = 2}

  let anonParams =
    {
      TableSettings =
        Map [
          "customers", { AidColumns = [ aidSetting "id"; aidSetting "company_name" ] } //
          "purchases", { AidColumns = [ aidSetting "cid" ] } //
          "customers_small", { AidColumns = [ aidSetting "id" ] } //
        ]
      Seed = 1
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

  [<Fact>]
  let ``query 7 - bucket expansion`` () =
    let queryResult = runQuery "SELECT city FROM customers_small"

    let expectedRows = List.collect (fun name -> [ for _i in 1 .. 10 -> [| String name |] ]) [ "Berlin"; "Rome" ]

    let expected = { Columns = [ "city" ]; Rows = expectedRows }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 8 - cross join`` () =
    let queryResult = runQuery "SELECT count(*) FROM customers_small, purchases WHERE id = cid"

    let expected = { Columns = [ "count" ]; Rows = [ [| Integer 72L |] ] }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 9 - inner join`` () =
    let queryResult = runQuery "SELECT count(*) FROM purchases join customers_small ON id = cid"

    let expected = { Columns = [ "count" ]; Rows = [ [| Integer 72L |] ] }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 10 - left join`` () =
    let queryResult = runQuery "SELECT count(*) FROM customers_small LEFT JOIN purchases ON id = cid"

    let expected = { Columns = [ "count" ]; Rows = [ [| Integer 72L |] ] }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 11 - right join`` () =
    let queryResult = runQuery "SELECT count(*) FROM customers_small RIGHT JOIN purchases ON id = cid"

    let expected = { Columns = [ "count" ]; Rows = [ [| Integer 72L |] ] }

    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 12`` () =
    let expected = { Columns = [ "n" ]; Rows = [ [| String "Water" |] ] }
    let queryResult = runQuery "SELECT p.name AS n FROM products AS p WHERE id = 1"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 13 - low count id`` () =
    let anonParams =
      { anonParams with
          TableSettings =
            Map ["customers_small", { AidColumns = [
              {Name = "id"; MinimumAllowed = 20}
              {Name = "company_name"; MinimumAllowed = 2}
            ] }]
      }
    let expected = { Columns = [ "first_name"; "count" ]; Rows = [ ] }
    let queryResult =
      runQueryWithCustomAnonParams anonParams "SELECT first_name, count(*) FROM customers_small WHERE first_name = 'Alice' GROUP BY 1"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 14 - low count company_name`` () =
    let anonParams =
      { anonParams with
          TableSettings =
            Map ["customers_small", { AidColumns = [
              {Name = "id"; MinimumAllowed = 2}
              {Name = "company_name"; MinimumAllowed = 20}
            ] }]
      }
    let expected = { Columns = [ "first_name"; "count" ]; Rows = [ ] }
    let queryResult =
      runQueryWithCustomAnonParams anonParams "SELECT first_name, count(*) FROM customers_small WHERE first_name = 'Alice' GROUP BY 1"
    assertOkEqual queryResult expected

  [<Fact>]
  let ``query 15 - low count passes for multiple AIDs`` () =
    let anonParams =
      { anonParams with
          TableSettings =
            Map ["customers_small", { AidColumns = [
              {Name = "id"; MinimumAllowed = 2}
              {Name = "company_name"; MinimumAllowed = 2}
            ] }]
      }
    let expected = { Columns = [ "first_name"; "count" ]; Rows = [ [| String "Alice"; Integer 11L |] ] }
    let queryResult =
      runQueryWithCustomAnonParams anonParams "SELECT first_name, count(*) FROM customers_small WHERE first_name = 'Alice' GROUP BY 1"
    assertOkEqual queryResult expected

  interface IClassFixture<DBFixture>
