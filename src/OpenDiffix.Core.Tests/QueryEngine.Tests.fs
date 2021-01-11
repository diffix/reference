module OpenDiffix.Core.QueryEngineTests

open Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

let runQuery query =
  let requestParams =
    {
      AnonymizationParams =
        {
          AidColumnOption = Some "id"
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

  let expected = { Columns = [ "name" ]; Rows = rows } |> Ok

  Assert.Equal(expected, runQuery "SHOW TABLES")

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

  let expected = { Columns = [ "name"; "type" ]; Rows = rows } |> Ok

  let queryResult = runQuery "SHOW columns FROM customers"
  Assert.Equal(expected, queryResult)

[<Fact>]
let ``SELECT city FROM customers`` () =
  let rows =
    [ "Berlin", 77; "London", 25; "Madrid", 25; "Paris", 26; "Rome", 50 ]
    |> List.collect (fun (city, occurrences) -> List.replicate occurrences [ StringValue city ])

  let expected = { Columns = [ "city" ]; Rows = rows } |> Ok

  let queryResult =
    match runQuery "SELECT city FROM customers" with
    | Ok result ->
        let sortedRows = result.Rows |> List.sort
        { result with Rows = sortedRows } |> Ok
    | other -> other

  Assert.Equal(expected, queryResult)
