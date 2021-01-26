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
          LowCountThreshold = {Threshold.Default with Lower = 5; Upper = 7}
          OutlierCount = Threshold.Default
          TopCount = Threshold.Default
          CountNoise = NoiseParam.Default
        }
      DatabasePath = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"
      Query = query
    }

  QueryEngine.runQuery requestParams |> Async.RunSynchronously

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
