module OpenDiffix.Core.QueryEngineTests

open Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

let runQuery query =
  let requestParams =
    {
      AnonymizationParams =
        {
          TableSettings =
            Map [
              "customers", { AidColumns = [ "id" ] } //
              "purchases", { AidColumns = [ "cid" ] } //
            ]
          Seed = 1
          LowCountSettings = Some LowCountSettings.Defaults
        }
      DatabasePath = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"
      Query = query
    }

  QueryEngine.runQuery requestParams |> Async.RunSynchronously

[<Fact>]
let ``SHOW TABLES`` () =
  let rows =
    [ "customers"; "products"; "purchases" ]
    |> List.map (fun v -> [ StringValue v ])

  let expected = { Columns = [ "name" ]; Rows = rows }

  let queryResult = runQuery "SHOW TABLES"

  assertOkEqual queryResult expected

[<Fact>]
let ``SHOW columns FROM customers`` () =
  let rows =
    [
      "id", "integer"
      "first_name", "string"
      "last_name", "string"
      "age", "integer"
      "city", "string"
    ]
    |> List.sortBy fst
    |> List.map (fun (name, dataType) -> [ StringValue name; StringValue dataType ])

  let expected = { Columns = [ "name"; "type" ]; Rows = rows }

  let queryResult = runQuery "SHOW columns FROM Customers"

  assertOkEqual queryResult expected

[<Fact>]
let ``SELECT city FROM customers`` () =
  let rows =
    [ "Berlin", 77; "London", 25; "Madrid", 25; "Paris", 26; "Rome", 50 ]
    |> List.collect (fun (city, occurrences) -> List.replicate occurrences [ StringValue city ])

  let expected = { Columns = [ "city" ]; Rows = rows }

  let queryResult =
    match runQuery "SELECT city FROM customers" with
    | Ok result ->
        let sortedRows = result.Rows |> List.sort
        { result with Rows = sortedRows } |> Ok
    | other -> other

  assertOkEqual queryResult expected

[<Fact>]
let ``SELECT pid FROM purchases`` () =
  let rows =
    [ 1, 67; 2, 58; 3, 64; 4, 63; 5, 70; 6, 59; 7, 58; 8, 64 ]
    |> List.collect (fun (pid, occurrences) -> List.replicate occurrences [ IntegerValue pid ])

  let expected = { Columns = [ "pid" ]; Rows = rows }

  let queryResult =
    match runQuery "SELECT pid FROM purchases" with
    | Ok result ->
        let sortedRows = result.Rows |> List.sort
        { result with Rows = sortedRows } |> Ok
    | other -> other

  assertOkEqual queryResult expected
