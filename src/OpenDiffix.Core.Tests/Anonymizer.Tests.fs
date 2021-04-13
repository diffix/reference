module OpenDiffix.Core.AnonymizerTests

open OpenDiffix.Core.AnonymizerTypes
open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let companies i =
  let names = [ "Alpha"; "Beta"; "Gamma"; "Delta" ]
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
let companyColumn = ColumnReference(2, StringType)
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
let ``anon count distinct column`` () =
  rows
  |> evaluateAggregator distinctDiffixCount [ allAidColumns; aidColumn ]
  |> should equal (Integer 9L)

  rows
  |> evaluateAggregator distinctDiffixCount [ allAidColumns; companyColumn ]
  |> should equal (Integer 4L)

  rows
  |> evaluateAggregator distinctDiffixCount [ allAidColumns; strColumn ]
  |> should equal (Integer 1L)

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

[<Fact>]
let ``multi-AID count`` () =
  let rows =
    [
      // AID1 ; String column ; AID 2
      [| Integer 1L; String "value"; String "Alpha" |]
      [| Integer 2L; String "value"; String "Alpha" |]
      [| Integer 3L; String "value"; String "Alpha" |]
      [| Integer 4L; String "value"; String "Alpha" |]
      [| Integer 5L; String "value"; String "Alpha" |]
      [| Integer 6L; String "value"; String "Alpha" |]
      [| Integer 7L; String "value"; String "Alpha" |]
      [| Integer 8L; String "value"; String "Alpha" |]
      [| Integer 9L; String "value"; String "Alpha" |]
      [| Integer 10L; String "value"; String "Alpha" |]
      [| Integer 11L; String "value"; String "Alpha" |]
      [| Integer 12L; String "value"; String "Beta" |]
      [| Integer 13L; String "value"; String "Gamma" |]
      [| Integer 14L; String "value"; String "Delta" |]
      [| Integer 15L; String "value"; String "Epsilon" |]
    ]

  // Alpha is outlier with 11 entries. Should be flattened by 10.

  rows
  |> evaluateAggregator diffixCount [ allAidColumns; strColumn ]
  |> should equal (Integer 5L)

[<Fact>]
let ``count distinct with flattening - worked example 1 from doc`` () =
  let rows =
    [
      // AID1; AID2; Fruit
      [| String "Paul"; String "Sebastian"; String "Apple" |]
      [| String "Sebastian"; String "Sebastian"; String "Apple" |]
      [| String "Paul"; String "Sebastian"; String "Apple" |]
      [| String "Edon"; String "Sebastian"; String "Apple" |]
      [| String "Sebastian"; String "Sebastian"; String "Apple" |]
      [| String "Cristian"; String "Paul"; String "Apple" |]
      [| String "Edon"; String "Paul"; String "Apple" |]
      [| String "Edon"; String "Paul"; String "Pear" |]
      [| String "Paul"; String "Paul"; String "Pineapple" |]
      [| String "Cristian"; String "Paul"; String "Lemon" |]
      [| String "Cristian"; String "Felix"; String "Orange" |]
      [| String "Felix"; String "Edon"; String "Banana" |]
      [| String "Edon"; String "Cristian"; String "Grapefruit" |]
    ]

  let aid1 = ColumnReference(0, StringType)
  let aid2 = ColumnReference(1, StringType)
  let fruit = ColumnReference(2, StringType)
  let allAidColumns = Expression.Array [| aid1; aid2 |]

  let threshold = { Lower = 2; Upper = 2 }

  let anonParams =
    { context.AnonymizationParams with
        OutlierCount = threshold
        TopCount = threshold
    }

  let context = { context with AnonymizationParams = anonParams }

  rows
  |> TestHelpers.evaluateAggregator context distinctDiffixCount [ allAidColumns; fruit ]
  |> should equal (Integer 5L)

[<Fact>]
let ``count distinct with flattening - re-worked example 2 from doc`` () =
  // This example differs from the one in the docs by it altering the
  // AIDs to ensure that the number of distinct AIDs of each kind are
  // truly always above the minimum allowed AIDs, even in the case where
  // we operate with `minimum_allowed_aids + 2`. The original example assumed
  // the noisy `minimum_allowed_aids` is equal to 2.
  let rows =
    [
      // AID1; AID2; Fruit
      [| String "Paul"; String "Paul"; String "Apple" |]
      [| String "Edon"; String "Edon"; String "Apple" |]
      [| String "Felix"; String "Felix"; String "Apple" |]
      [| String "Sebastian"; String "Sebastian"; String "Apple" |]
      [| String "Cristian"; String "Cristian"; String "Apple" |]
      [| String "Paul"; String "Paul"; String "Orange" |]
      [| String "Edon"; String "Edon"; String "Orange" |]
      [| String "Felix"; String "Felix"; String "Orange" |]
      [| String "Sebastian"; String "Sebastian"; String "Orange" |]
      [| String "Cristian"; String "Cristian"; String "Orange" |]
    ]

  let aid1 = ColumnReference(0, StringType)
  let aid2 = ColumnReference(1, StringType)
  let fruit = ColumnReference(2, StringType)
  let allAidColumns = Expression.Array [| aid1; aid2 |]

  let threshold = { Lower = 2; Upper = 2 }

  let anonParams =
    { context.AnonymizationParams with
        OutlierCount = threshold
        TopCount = threshold
    }

  let context = { context with AnonymizationParams = anonParams }

  rows
  |> TestHelpers.evaluateAggregator context distinctDiffixCount [ allAidColumns; fruit ]
  |> should equal (Integer 2L)
