module OpenDiffix.Core.AnonymizerTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let ids =
  [ 1, 5; 2, 4; 3, 2; 4, 1; 5, 5; 6, 4; 7, 3; 8, 6 ]
  |> List.collect (fun (id, count) -> List.replicate count id)
  |> List.map (int64 >> Integer >> Array.singleton)

let rows =
  let defaultUserRows = ids |> List.map (fun idArray -> Array.append idArray [| String "value" |])
  let extraUserRows = [ [| Integer 8L; Null |]; [| Null; String "" |] ]
  List.append defaultUserRows extraUserRows

let aidColumn = ColumnReference(0, IntegerType)
let strColumn = ColumnReference(1, StringType)

let context =
  { EvaluationContext.Default with
      AnonymizationParams =
        {
          TableSettings = Map.empty
          Seed = 0
          LowCountAbsoluteLowerBound = 2
          LowCountThreshold = { Lower = 1; Upper = 1 }
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
  ids
  |> evaluateAggregator distinctDiffixCount [ aidColumn ]
  |> should equal (Integer 8L)

[<Fact>]
let ``anon count()`` () =
  // - replacing outlier 6, with top 5
  // - 0 noise
  ids
  |> evaluateAggregator diffixCount [ aidColumn ]
  |> should equal (Integer 29L)

[<Fact>]
let ``anon count(col)`` () =
  // - 1 user with Null string is ignored
  // - replacing outlier 6 with top 5
  // - 0 noise
  rows
  |> evaluateAggregator diffixCount [ aidColumn; strColumn ]
  |> should equal (Integer 29L)

[<Fact>]
let ``anon count returns Null if insufficient users`` () =
  let firstRow = rows |> List.take 1

  firstRow
  |> evaluateAggregator diffixCount [ aidColumn; strColumn ]
  |> should equal Null

  firstRow |> evaluateAggregator diffixCount [ aidColumn ] |> should equal Null

[<Fact>]
let ``anon count returns 0 for Null inputs`` () =
  let rows = [ 1L .. 10L ] |> List.map (fun i -> [| Integer i; Null |])

  rows
  |> evaluateAggregator diffixCount [ aidColumn; strColumn ]
  |> should equal (Integer 0L)
