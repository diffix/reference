module OpenDiffix.Core.AnonymizerTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let companies i =
  let names = [ "Alpha"; "Beta"; "Gamma" ]
  names |> List.item (i % names.Length)

let rows =
  [ 1, 5; 2, 4; 3, 2; 4, 1; 5, 5; 6, 4; 7, 3; 8, 6 ]
  |> List.collect (fun (id, count) -> List.replicate count id)
  |> List.map (fun id -> [| id |> int64 |> Integer; String "value"; companies id |> String |])
  |> List.append [
       [| Null; String "value"; String "Alpha" |]
       [| Integer 8L; Null; String "Alpha" |]
       [| Integer 9L; String "value"; Null |]
     ]

let aidColumn = ColumnReference(0, IntegerType)
let aidColumnArray = Expression.Array [| aidColumn |]
let strColumn = ColumnReference(1, StringType)
let companyColumn = ColumnReference(2, IntegerType)
let allAidColumns = Expression.Array [| aidColumn; companyColumn |]

let context =
  { EvaluationContext.Default with
      AnonymizationParams =
        {
          TableSettings = Map.empty
          Seed = 0
          MinimumAllowedAids = 2
          OutlierCount = { Lower = 1; Upper = 1 }
          TopCount = { Lower = 1; Upper = 1 }
          Noise = { StandardDev = 1.; Cutoff = 0. }
        }
  }

let evaluateAggregator = evaluateAggregator context

let distinctDiffixCount = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = true })
let diffixCount = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = false })

[<Fact>]
let ``anon count distinct aid`` () =
  rows
  |> evaluateAggregator distinctDiffixCount [ allAidColumns; aidColumn ]
  |> should equal (Integer 9L)

[<Fact>]
let ``anon count()`` () =
  // - replacing outlier 6, with top 5
  // - 0 noise
  rows
  |> evaluateAggregator diffixCount [ aidColumnArray ]
  |> should equal (Integer 30L)

[<Fact>]
let ``anon count(col)`` () =
  // - 1 user with Null string is ignored
  // - replacing outlier 6 with top 5
  // - 0 noise
  rows
  |> evaluateAggregator diffixCount [ aidColumnArray; strColumn ]
  |> should equal (Integer 30L)

[<Fact>]
let ``anon count returns Null if insufficient users`` () =
  let firstRow = rows |> List.take 1

  firstRow
  |> evaluateAggregator diffixCount [ allAidColumns; strColumn ]
  |> should equal Null

  firstRow
  |> evaluateAggregator diffixCount [ allAidColumns; aidColumn ]
  |> should equal Null

[<Fact>]
let ``anon count returns 0 for Null inputs`` () =
  let rows = [ 1L .. 10L ] |> List.map (fun i -> [| Integer i; Null |])

  rows
  |> evaluateAggregator diffixCount [ aidColumnArray; strColumn ]
  |> should equal (Integer 0L)

[<Fact>]
let ``anon count returns Null when all AIDs null`` () =
  let rows = [ 1L .. 10L ] |> List.map (fun _ -> [| Null; String "value"; Null |])

  rows
  |> evaluateAggregator diffixCount [ allAidColumns; strColumn ]
  |> should equal Null
