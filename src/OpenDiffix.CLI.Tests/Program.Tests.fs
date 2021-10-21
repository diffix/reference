module OpenDiffix.CLI.ProgramTests

open Xunit
open FsUnit.Xunit

open OpenDiffix.CLI.Program

[<Fact>]
let ``Prints version`` () =
  let argv = [| "--version" |]

  main argv |> should equal 0

[<Fact>]
let ``Counts all rows`` () =
  let dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

  let argv = [| "-f"; dataDirectory; "--aid-columns"; "customers.id"; "-q"; "SELECT count(*) FROM customers" |]

  main argv |> should equal 0

[<Fact>]
let ``Counts all protected entities`` () =
  let dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

  let argv =
    [|
      "-f"
      dataDirectory
      "--aid-columns"
      "customers.id"
      "-q"
      "SELECT count(DISTINCT customers.id) FROM customers"
    |]

  main argv |> should equal 0

[<Fact>]
let ``Counts in non-anonymized tables`` () =
  let dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

  let argv =
    [|
      "-f"
      dataDirectory
      "--aid-columns"
      "customers.id"
      "-q"
      // note here that the aid column is on a different table
      "SELECT count(*) FROM purchases"
    |]

  main argv |> should equal 0

[<Fact>]
let ``Counts some buckets`` () =
  let dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

  let argv =
    [|
      "-f"
      dataDirectory
      "--aid-columns"
      "customers.id"
      "-q"
      "SELECT age, city, company_name, count(*) FROM customers GROUP BY age, city, company_name"
    |]

  main argv |> should equal 0

[<Fact>]
let ``Rejects invalid SQL`` () =
  let dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

  let argv =
    [|
      "-f"
      dataDirectory
      "--aid-columns"
      "customers.id"
      "-q"
      "SELECT no_such_column, count(*) FROM customers GROUP BY no_such_column"
    |]

  main argv |> should equal 1

[<Fact>]
let ``Rejects malformed SQL`` () =
  let dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

  let argv = [| "-f"; dataDirectory; "--aid-columns"; "customers.id"; "-q"; "foo" |]

  main argv |> should equal 1

[<Fact>]
let ``Guards against unknown params`` () =
  let dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

  let argv = [| "-f"; dataDirectory; "--foo"; "customers.id"; "-q"; "SELECT count(*) FROM customers" |]

  main argv |> should equal 1

[<Fact>]
let ``Accepts supported CLI parameters`` () =
  let dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

  let argv =
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
      "--low-sd"
      "1.2"
      "--low-mean-gap"
      "1"
      "--noise-sd"
      "2.4"
      "--aid-columns"
      "customers.id"
      "-q"
      "SELECT count(*) FROM customers"
    |]

  main argv |> should equal 0

[<Fact>]
let ``Executes example batch query`` () =
  let dataDirectory = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"
  let batchDirectory = __SOURCE_DIRECTORY__ + "/../../queries-sample.json"

  let argv = [| "-f"; dataDirectory; "--aid-columns"; "customers.id"; "--queries-path"; batchDirectory |]

  main argv |> should equal 0
