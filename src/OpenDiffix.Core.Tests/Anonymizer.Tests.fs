module OpenDiffix.Core.AnonymizerTests

open Xunit
open FsUnit.Xunit

open CommonTypes

let companies i =
  let names = [ "Alpha"; "Beta"; "Gamma"; "Delta" ]
  names |> List.item (i % names.Length) |> String

let reals i =
  let reals = [ 0.1; 0.0; 1.0; -0.01 ]
  reals |> List.item (i % reals.Length) |> Real

let rows =
  [ 1, 5; 2, 4; 3, 2; 4, 1; 5, 5; 6, 4; 7, 3; 8, 6 ]
  |> List.collect (fun (id, count) -> List.replicate count id)
  |> List.map (fun id -> [| id |> int64 |> Integer; String "value"; companies id; reals id |])
  |> List.append [
       [| Null; String "value"; String "Alpha"; Real -100.0 |]
       [| Integer 8L; Null; String "Alpha"; Null |]
       [| Integer 9L; String "value"; Null; Real 10.0 |]
     ]

let aidColumn = ColumnReference(0, IntegerType)
let aidColumnList = ListExpr [ aidColumn ]
let strColumn = ColumnReference(1, StringType)
let companyColumn = ColumnReference(2, StringType)
let realColumn = ColumnReference(3, RealType)
let allAidColumns = ListExpr [ aidColumn; companyColumn ]

let anonParams =
  {
    TableSettings = Map.empty
    Salt = [||]
    AccessLevel = Direct
    Strict = false
    Suppression = { LowThreshold = 2; LowMeanGap = 0.0; LayerSD = 0. }
    OutlierCount = { Lower = 1; Upper = 1 }
    TopCount = { Lower = 1; Upper = 1 }
    LayerNoiseSD = 0.
    RecoverOutliers = true
  }

let aggContext = { AnonymizationParams = anonParams; GroupingLabels = [||]; Aggregators = [||] }

let evaluateAggregator fn args =
  TestHelpers.evaluateAggregator (aggContext, Some { BucketSeed = 0UL; BaseLabels = [] }, None) fn args

let evaluateAggregatorNoisy fn args =
  let aggContextNoisy = { aggContext with AnonymizationParams = { anonParams with LayerNoiseSD = 1. } }
  TestHelpers.evaluateAggregator (aggContextNoisy, Some { BucketSeed = 0UL; BaseLabels = [] }, None) fn args

let distinctDiffixCount = DiffixCount, { AggregateOptions.Default with Distinct = true }
let diffixCount = DiffixCount, AggregateOptions.Default
let diffixCountNoise = DiffixCountNoise, AggregateOptions.Default
let diffixLowCount = DiffixLowCount, AggregateOptions.Default
let diffixSum = DiffixSum, AggregateOptions.Default
let countHistogram = CountHistogram, AggregateOptions.Default
let diffixCountHistogram = DiffixCountHistogram, AggregateOptions.Default

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
  // replacing outlier 8, with top 5 --> flattened by 2
  rows
  |> evaluateAggregator diffixCount [ aidColumnList ]
  |> should equal (Integer 30L)

[<Fact>]
let ``anon count_noise()`` () =
  // noise proportional to flattened avg of 30/9 = 3.333..., money rounded to 3.4
  rows
  |> evaluateAggregatorNoisy diffixCountNoise [ aidColumnList ]
  |> function
    | Real value -> value
    | _ -> failwith "Unexpected aggregator result"
  |> should (equalWithin 1e-3) 3.4

[<Fact>]
let ``anon count(col)`` () =
  // replacing outlier 8, with top 5 --> flattened by 2
  rows
  |> evaluateAggregator diffixCount [ aidColumnList; strColumn ]
  |> should equal (Integer 30L)

[<Fact>]
let ``anon count_noise(col)`` () =
  // noise proportional to flattened avg of 30/9 = 3.333..., money rounded to 3.4
  rows
  |> evaluateAggregatorNoisy diffixCountNoise [ aidColumnList; strColumn ]
  |> function
    | Real value -> value
    | _ -> failwith "Unexpected aggregator result"
  |> should (equalWithin 1e-3) 3.4

[<Fact>]
let ``anon sum(real col)`` () =
  // 1 user with Null real is ignored
  // replacing positive outlier 10.0 with 4.0
  // replacing negative outlier 3x -0.01 with 2x -0.01
  // end up with 4.0 + 4.0 + 4.0 + 0.7 - 0.02 - 0.02
  rows
  |> evaluateAggregator diffixSum [ aidColumnList; realColumn ]
  |> function
    | Real value -> value
    | _ -> failwith "Unexpected aggregator result"
  |> should (equalWithin 1e-10) 12.66

[<Fact>]
let ``anon sum(int col)`` () =
  rows
  |> evaluateAggregator diffixSum [ aidColumnList; aidColumn ]
  |> should equal (Integer 127L)

let countHistogramRows =
  [
    List.replicate 1 [| Integer 1L; String "value" |]
    List.replicate 1 [| Integer 2L; String "value" |]
    List.replicate 1 [| Integer 3L; String "value" |]
    List.replicate 1 [| Integer 4L; String "value" |] // 4 users contribute 1 row
    List.replicate 2 [| Integer 5L; String "value" |]
    List.replicate 2 [| Integer 6L; String "value" |]
    List.replicate 2 [| Integer 7L; String "value" |] // 3 users contribute 2 rows
    List.replicate 6 [| Integer 8L; String "value" |] // 1 user contributes 6 rows
    List.replicate 7 [| Integer 9L; String "value" |] // 1 user contributes 7 rows
    List.replicate 13 [| Integer 10L; String "value" |] // 1 user contributes 13 rows
  ]
  |> List.concat

[<Fact>]
let ``direct count_histogram`` () =
  countHistogramRows
  |> evaluateAggregator countHistogram [ aidColumn ]
  |> should
       equal
       (List [
         List [ Integer 1L; Integer 4L ]
         List [ Integer 2L; Integer 3L ]
         List [ Integer 6L; Integer 1L ]
         List [ Integer 7L; Integer 1L ]
         List [ Integer 13L; Integer 1L ]
        ])

[<Fact>]
let ``direct count_histogram with generalization`` () =
  countHistogramRows
  |> evaluateAggregator countHistogram [ aidColumn; Constant(Integer 5L) ]
  |> should
       equal
       (List [ //
         List [ Integer 0L; Integer 7L ]
         List [ Integer 5L; Integer 2L ]
         List [ Integer 10L; Integer 1L ]
        ])

[<Fact>]
let ``anon count_histogram`` () =
  countHistogramRows
  |> evaluateAggregator diffixCountHistogram [ aidColumnList; Constant(Integer 0L) ]
  |> should
       equal
       (List [ //
         List [ Null; Integer 3L ]
         List [ Integer 1L; Integer 4L ]
         List [ Integer 2L; Integer 3L ]
        ])

[<Fact>]
let ``anon count_histogram with generalization`` () =
  countHistogramRows
  |> evaluateAggregator diffixCountHistogram [ aidColumnList; Constant(Integer 0L); Constant(Integer 5L) ]
  |> should
       equal
       (List [ //
         List [ Integer 0L; Integer 7L ]
         List [ Integer 5L; Integer 2L ]
        ])

[<Fact>]
let ``anon count returns 0 if insufficient users`` () =
  let firstRow = rows |> List.take 1

  firstRow
  |> evaluateAggregator diffixCount [ allAidColumns; strColumn ]
  |> should equal (Integer 0L)

  firstRow
  |> evaluateAggregator diffixCount [ allAidColumns; aidColumn ]
  |> should equal (Integer 0L)

[<Fact>]
let ``anon count returns 0 for Null inputs`` () =
  let rows = [ 1L .. 10L ] |> List.map (fun i -> [| Integer i; Null |])

  rows
  |> evaluateAggregator diffixCount [ aidColumnList; strColumn ]
  |> should equal (Integer 0L)

[<Fact>]
let ``anon sum returns Null for Null inputs`` () =
  let rows = [ 1L .. 10L ] |> List.map (fun i -> [| Integer i; Null; Null; Null |])

  rows
  |> evaluateAggregator diffixSum [ aidColumnList; realColumn ]
  |> should equal Null

[<Fact>]
let ``anon count returns 0 when all AIDs null`` () =
  let rows = [ 1L .. 10L ] |> List.map (fun _ -> [| Null; String "value"; Null |])

  rows
  |> evaluateAggregator diffixCount [ allAidColumns; strColumn ]
  |> should equal (Integer 0L)

[<Fact>]
let ``anon sum returns null when all AIDs null`` () =
  let rows = [ 1L .. 10L ] |> List.map (fun _ -> [| Null; Null; Null; Real 10.0 |])

  rows
  |> evaluateAggregator diffixSum [ aidColumnList; realColumn ]
  |> should equal Null

[<Fact>]
let ``anon sum accepts 0.0 as contributions for both positive and negative`` () =
  let rows =
    [ 1L .. 10L ]
    |> List.map (fun i -> [| Integer i; Null; Null; Real 0.0 |])
    |> List.append ([ [| Integer 11L; Null; Null; Real -10.0 |]; [| Integer 12L; Null; Null; Real 10.0 |] ])

  rows
  |> evaluateAggregator diffixSum [ aidColumnList; realColumn ]
  |> should equal (Real 0.0)

[<Fact>]
let ``anon sum ignores nulls completely, flattening included`` () =
  let rows =
    [ 1L .. 10L ]
    |> List.map (fun i -> [| Integer i; Null; Null; Null |])
    |> List.append ([ [| Integer 11L; Null; Null; Real -10.0 |]; [| Integer 12L; Null; Null; Real 10.0 |] ])

  rows
  |> evaluateAggregator diffixSum [ aidColumnList; realColumn ]
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
  // Noise is proportional to top group average of 1

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
  let allAidColumns = ListExpr [ aid1; aid2 ]

  rows
  |> evaluateAggregator distinctDiffixCount [ allAidColumns; fruit ]
  |> should equal (Integer 5L)

[<Fact>]
let ``count distinct with flattening - re-worked example 2 from doc`` () =
  // This example differs from the one in the docs by it altering the
  // AIDs to ensure that the number of distinct AIDs of each kind are
  // truly always above the minimum allowed AIDs, even in the case where
  // we operate with `minimum_allowed_aid_values + 2`. The original example assumed
  // the noisy `minimum_allowed_aid_values` is equal to 2.
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
  let allAidColumns = ListExpr [ aid1; aid2 ]

  rows
  |> evaluateAggregator distinctDiffixCount [ allAidColumns; fruit ]
  |> should equal (Integer 2L)

[<Fact>]
let ``counts with insufficient values for one AID return 0`` () =
  let rows =
    [
      // AID1; AID2; Fruit
      [| String "Paul"; String "Paul"; Integer 1L |]
      [| String "Paul"; String "Felix"; Integer 2L |]
      [| String "Paul"; String "Edon"; Integer 3L |]
      [| String "Paul"; String "Cristian"; Integer 4L |]
      [| String "Paul"; String "Sebastian"; Integer 5L |]
    ]

  let aid1 = ColumnReference(0, StringType)
  let aid2 = ColumnReference(1, StringType)
  let value = ColumnReference(2, IntegerType)
  let allAidColumns = ListExpr [ aid1; aid2 ]

  rows
  |> evaluateAggregator diffixCount [ allAidColumns; value ]
  |> should equal (Integer 0L)

  rows
  |> evaluateAggregator distinctDiffixCount [ allAidColumns; value ]
  |> should equal (Integer 0L)

[<Fact>]
let ``allows null-values for some of the AID rows`` () =
  let rows =
    [
      // AID1; AID2; Fruit
      [| String "Paul"; String "Paul"; Integer 1L |]
      [| String "Felix"; String "Felix"; Integer 2L |]
      [| String "Edon"; String "Sebastian"; Integer 3L |]
      [| String "Cristian"; Null; Integer 4L |]
      [| String "Sebastian"; Null; Integer 5L |]
    ]

  let aid1 = ColumnReference(0, StringType)
  let aid2 = ColumnReference(1, StringType)
  let value = ColumnReference(2, IntegerType)
  let allAidColumns = ListExpr [ aid1; aid2 ]

  rows
  |> evaluateAggregator diffixCount [ allAidColumns; value ]
  |> should equal (Integer 5L)

  rows
  |> evaluateAggregator distinctDiffixCount [ allAidColumns; value ]
  |> should equal (Integer 5L)

  // The aggregate result should not be affected by the order of the AIDs
  let allAidsFlipped = ListExpr [ aid2; aid1 ]

  rows
  |> evaluateAggregator diffixCount [ allAidsFlipped; value ]
  |> should equal (Integer 5L)

  rows
  |> evaluateAggregator distinctDiffixCount [ allAidsFlipped; value ]
  |> should equal (Integer 5L)

[<Fact>]
let ``account for values where AID-value is null`` () =
  let rows =
    [
      // AID1; AID2; Fruit
      [| String "Paul"; Null; Integer 1L |]
      [| String "Felix"; Null; Integer 2L |]
      [| String "Edon"; Null; Integer 3L |]
      [| String "Cristian"; Null; Integer 4L |]
      [| Null; String "Paul"; Integer 1L |]
      [| Null; String "Felix"; Integer 2L |]
      [| Null; String "Edon"; Integer 3L |]
      [| Null; String "Cristian"; Integer 4L |]
      [| Null; Null; Integer 5L |]
    ]

  let aid1 = ColumnReference(0, StringType)
  let aid2 = ColumnReference(1, StringType)
  let value = ColumnReference(2, IntegerType)
  let allAidColumns = ListExpr [ aid1; aid2 ]

  rows
  |> evaluateAggregator diffixCount [ allAidColumns; value ]
  |> should equal (Integer 8L)

[<Fact>]
let ``compacting top/outlier interval respects rules`` () =
  let totalCount = 5

  let assertCorrectCompaction (originalOutlier, originalTop) =
    let (compactOutlier, compactTop) =
      (Anonymizer.compactFlatteningIntervals originalOutlier originalTop totalCount).Value
    // only upper bounds are compacted and only change downwards
    compactOutlier.Lower |> should equal originalOutlier.Lower
    compactTop.Lower |> should equal originalTop.Lower
    compactOutlier.Upper |> should be (lessThanOrEqualTo originalOutlier.Upper)
    compactTop.Upper |> should be (lessThanOrEqualTo originalTop.Upper)

    // still valid intervals
    compactOutlier.Lower |> should be (lessThanOrEqualTo compactOutlier.Upper)
    compactTop.Lower |> should be (lessThanOrEqualTo compactTop.Upper)

    // compaction succeeded
    totalCount
    |> should be (greaterThanOrEqualTo (compactTop.Upper + compactOutlier.Upper))

    // same rate, if possible; `topCount` takes priority and might compact more by 1
    ((compactTop.Upper = compactTop.Lower)
     || (compactOutlier.Upper = compactOutlier.Lower)
     || (originalTop.Upper - compactTop.Upper = originalOutlier.Upper - compactOutlier.Upper)
     || (originalTop.Upper - compactTop.Upper = originalOutlier.Upper - compactOutlier.Upper + 1))
    |> should equal true

  let rec cartesian LL =
    match LL with
    | [] -> Seq.singleton []
    | L :: Ls ->
      seq {
        for x in L do
          for xs in cartesian Ls -> x :: xs
      }

  cartesian [ seq { 1 .. 5 }; seq { 1 .. 5 }; seq { 1 .. 5 }; seq { 1 .. 5 } ]
  |> Seq.map (fun l -> ({ Lower = l.[0]; Upper = l.[1] }, { Lower = l.[2]; Upper = l.[3] }))
  // Pick only valid intervals which have a flattening for `totalCount`
  |> Seq.filter (fun (outlier, top) ->
    outlier.Lower <= outlier.Upper
    && top.Lower <= top.Upper
    && outlier.Lower + top.Lower <= totalCount
  )
  |> Seq.iter assertCorrectCompaction

[<Fact>]
let ``compacting top/outlier interval finds cases of not enough AIDVs`` () =
  let assertNotEnoughAIDVs (originalOutlier, originalTop) =
    (Anonymizer.compactFlatteningIntervals originalOutlier originalTop 4)
    |> should equal None

  ({ Lower = 4; Upper = 4 }, { Lower = 1; Upper = 1 }) |> assertNotEnoughAIDVs
  ({ Lower = 1; Upper = 1 }, { Lower = 4; Upper = 4 }) |> assertNotEnoughAIDVs
  ({ Lower = 2; Upper = 3 }, { Lower = 3; Upper = 4 }) |> assertNotEnoughAIDVs
