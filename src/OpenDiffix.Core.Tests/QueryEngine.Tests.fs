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
  let expected =
    [ "customers"; "products"; "purchases" ]
    |> List.map (fun v -> NonPersonalRow { Columns = [ { ColumnName = "name"; ColumnValue = StringValue v } ] })
    |> ResultTable
    |> Ok

  Assert.Equal(expected, runQuery "SHOW TABLES")

[<Fact>]
let ``SHOW columns FROM customers`` () =
  let expected =
    [
      "id", "integer"
      "first_name", "string"
      "last_name", "string"
      "age", "integer"
      "city", "string"
    ]
    |> List.sortBy (fun (name, _type) -> name)
    |> List.map (fun (name, dataType) ->
      NonPersonalRow
        {
          Columns =
            [
              { ColumnName = "name"; ColumnValue = StringValue name }
              { ColumnName = "type"; ColumnValue = StringValue dataType }
            ]
        }
    )
    |> ResultTable
    |> Ok

  let queryResult = runQuery "SHOW columns FROM customers"
  Assert.Equal(expected, queryResult)

[<Fact>]
let ``SELECT city FROM customers`` () =
  let expected =
    [ "Berlin", 77; "London", 25; "Madrid", 25; "Paris", 26; "Rome", 50 ]
    |> List.collect (fun (city, occurrences) ->
      let row = NonPersonalRow { Columns = [ { ColumnName = "city"; ColumnValue = StringValue city } ] }

      List.replicate occurrences row
    )
    |> ResultTable
    |> Ok

  let queryResult =
    match runQuery "SELECT city FROM customers" with
    | Ok (ResultTable rows) -> rows |> List.sort |> ResultTable |> Ok
    | other -> other

  Assert.Equal(expected, queryResult)
