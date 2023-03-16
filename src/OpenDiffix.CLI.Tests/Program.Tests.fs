module OpenDiffix.CLI.ProgramTests

open Xunit
open FsUnit.Xunit

open OpenDiffix.CLI.Program

let private dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

[<Fact>]
let ``Prints version`` () =
  [| "--version" |] |> mainCore |> should not' (be Empty)

[<Fact>]
let ``Counts all rows`` () =
  [| "-f"; dataDirectory; "--aid-columns"; "customers.id"; "-q"; "SELECT count(*) FROM customers" |]
  |> mainCore
  |> should not' (be Empty)

[<Fact>]
let ``Counts in non-anonymized tables`` () =
  [| "-f"; dataDirectory; "-q"; "SELECT count(*) FROM purchases" |]
  |> mainCore
  |> should not' (be Empty)

[<Fact>]
let ``Rejects invalid SQL`` () =
  shouldFail (fun () ->
    [|
      "-f"
      dataDirectory
      "--aid-columns"
      "customers.id"
      "-q"
      "SELECT no_such_column, count(*) FROM customers GROUP BY no_such_column"
    |]
    |> mainCore
    |> ignore
  )

[<Fact>]
let ``Rejects invalid intervals`` () =
  shouldFail (fun () ->
    [| "-f"; dataDirectory; "--outlier-count"; "2"; "1"; "-q"; "SELECT count(*) FROM customers" |]
    |> mainCore
    |> ignore
  )

  shouldFail (fun () ->
    [| "-f"; dataDirectory; "--top-count"; "0"; "1"; "-q"; "SELECT count(*) FROM customers" |]
    |> mainCore
    |> ignore
  )

[<Fact>]
let ``Guards against unknown params`` () =
  shouldFail (fun () ->
    [| "-f"; dataDirectory; "--foo"; "customers.id"; "-q"; "SELECT count(*) FROM customers" |]
    |> mainCore
    |> ignore
  )

[<Fact>]
let ``Accepts supported CLI parameters`` () =
  [|
    "-f"
    dataDirectory
    "--salt"
    "1"
    "--json"
    "--outlier-count"
    "1"
    "2"
    "--top-count"
    "12"
    "14"
    "--low-threshold"
    "3"
    "--low-layer-sd"
    "1.2"
    "--low-mean-gap"
    "2.0"
    "--layer-noise-sd"
    "2.4"
    "--aid-columns"
    "customers.id"
    "-q"
    "SELECT count(*) FROM customers"
    "--use-adaptive-buckets"
    "false"
    "--range-low-threshold"
    "10"
    "--singularity-low-threshold"
    "5"
  |]
  |> mainCore
  |> should not' (be Empty)

[<Fact>]
let ``Rejects unsafe CLI parameters`` () =
  shouldFail (fun () ->
    [|
      "-f"
      dataDirectory
      "--low-layer-sd"
      "0.2"
      "--aid-columns"
      "customers.id"
      "-q"
      "SELECT count(*) FROM customers"
    |]
    |> mainCore
    |> ignore
  )

[<Fact>]
let ``Allows unsafe CLI parameters in unsafe mode`` () =
  [|
    "-f"
    dataDirectory
    "--low-layer-sd"
    "0.2"
    "--strict"
    "false"
    "--aid-columns"
    "customers.id"
    "-q"
    "SELECT count(*) FROM customers"
  |]
  |> mainCore
  |> should not' (be Empty)

[<Fact>]
let ``Executes example batch query`` () =
  [| "--queries-path"; __SOURCE_DIRECTORY__ + "/../../queries-sample.json" |]
  |> mainCore
  |> should not' (be Empty)
