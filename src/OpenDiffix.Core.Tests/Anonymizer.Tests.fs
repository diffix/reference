module OpenDiffix.Core.AnonymizerTests

open Xunit
open FsUnit.Xunit

open CommonTypes

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
let aidColumnList = ListExpr [ aidColumn ]
let strColumn = ColumnReference(1, StringType)
let companyColumn = ColumnReference(2, StringType)
let allAidColumns = ListExpr [ aidColumn; companyColumn ]

let executionContext =
  (QueryContext.makeWithAnonParams
    {
      TableSettings = Map.empty
      Salt = [||]
      Suppression = { LowThreshold = 2; LowMeanGap = 0.0; SD = 0. }
      OutlierCount = { Lower = 1; Upper = 1 }
      TopCount = { Lower = 1; Upper = 1 }
      LayerNoiseSD = 0.
    })
  |> ExecutionContext.fromQueryContext

let anonymizedAggregationContext =
  let threshold = { Lower = 2; Upper = 2 }

  let anonParams =
    { executionContext.AnonymizationParams with
        OutlierCount = threshold
        TopCount = threshold
    }

  QueryContext.makeWithAnonParams anonParams |> ExecutionContext.fromQueryContext

let evaluateAggregator fn args =
  evaluateAggregator executionContext fn args

let mergeAids = MergeAids, AggregateOptions.Default
let distinctDiffixCount = DiffixCount, { AggregateOptions.Default with Distinct = true }
let diffixCount = DiffixCount, { AggregateOptions.Default with Distinct = false }
let diffixLowCount = DiffixLowCount, AggregateOptions.Default

[<Fact>]
let ``merge bucket aids`` () =
  rows
  |> evaluateAggregator mergeAids [ aidColumn ]
  |> (function
  | Value.List values -> Value.List(List.sort values)
  | x -> x)
  |> should
       equal
       (Value.List [
         Integer 1L
         Integer 2L
         Integer 3L
         Integer 4L
         Integer 5L
         Integer 6L
         Integer 7L
         Integer 8L
         Integer 9L
        ])

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
  // - replacing outlier 6, with top 5 --> flattened by 1
  // - noise proportional to top group average of 5
  rows
  |> evaluateAggregator diffixCount [ aidColumnList ]
  |> should equal (Integer 30L)

[<Fact>]
let ``anon count(col)`` () =
  // - 1 user with Null string is ignored
  // - replacing outlier 6 with top 5
  // - noise proportional to top group average of 5
  rows
  |> evaluateAggregator diffixCount [ aidColumnList; strColumn ]
  |> should equal (Integer 30L)

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
let ``anon count returns 0 when all AIDs null`` () =
  let rows = [ 1L .. 10L ] |> List.map (fun _ -> [| Null; String "value"; Null |])

  rows
  |> evaluateAggregator diffixCount [ allAidColumns; strColumn ]
  |> should equal (Integer 0L)

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
  |> TestHelpers.evaluateAggregator anonymizedAggregationContext distinctDiffixCount [ allAidColumns; fruit ]
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
  |> TestHelpers.evaluateAggregator anonymizedAggregationContext distinctDiffixCount [ allAidColumns; fruit ]
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
  |> TestHelpers.evaluateAggregator anonymizedAggregationContext diffixCount [ allAidColumns; value ]
  |> should equal (Integer 0L)

  rows
  |> TestHelpers.evaluateAggregator anonymizedAggregationContext distinctDiffixCount [ allAidColumns; value ]
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
  |> TestHelpers.evaluateAggregator executionContext diffixCount [ allAidColumns; value ]
  |> should equal (Integer 5L)

  rows
  |> TestHelpers.evaluateAggregator executionContext distinctDiffixCount [ allAidColumns; value ]
  |> should equal (Integer 5L)

  // The aggregate result should not be affected by the order of the AIDs
  let allAidsFlipped = ListExpr [ aid2; aid1 ]

  rows
  |> TestHelpers.evaluateAggregator executionContext diffixCount [ allAidsFlipped; value ]
  |> should equal (Integer 5L)

  rows
  |> TestHelpers.evaluateAggregator executionContext distinctDiffixCount [ allAidsFlipped; value ]
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
  |> TestHelpers.evaluateAggregator executionContext diffixCount [ allAidColumns; value ]
  |> should equal (Integer 8L)

[<Fact>]
let ``low count accepts rows with shared contribution`` () =
  let aidList names = names |> List.map String |> Value.List
  let aidsExpression = ListExpr [ ColumnReference(0, ListType StringType) ]

  let lowUserRows = [ [| aidList [ "Sebastian" ] |] ]
  let highUserRows = [ [| aidList [ "Paul"; "Cristian"; "Felix"; "Edon" ] |] ]

  lowUserRows
  |> TestHelpers.evaluateAggregator executionContext diffixLowCount [ aidsExpression ]
  |> should equal (Boolean true)

  highUserRows
  |> TestHelpers.evaluateAggregator executionContext diffixLowCount [ aidsExpression ]
  |> should equal (Boolean false)

[<Fact>]
let ``count accepts rows with shared contribution`` () =
  let aidList names = names |> List.map String |> Value.List
  let aidsExpression = ListExpr [ ColumnReference(0, ListType StringType) ]

  let rows =
    [
      // AIDs
      [| aidList [ "Paul"; "Felix"; "Edon" ] |]
      [| aidList [ "Paul"; "Felix"; "Edon" ] |]
      [| aidList [ "Sebastian"; "Felix"; "Edon" ] |]
      [| aidList [ "Paul"; "Cristian" ] |]
      [| aidList [ "Cristian" ] |]
      [| aidList [ "Cristian" ] |]
      [| aidList [ "Cristian" ] |]
    ]

  // Cristian:  1/2 + 1 + 1 + 1 = 3.5  (outlier)
  // Paul:      1/3 + 1/3 + 1/2 = 1.16 (top)
  // Felix:     1/3 + 1/3 + 1/3 = 1.0
  // Edon:      1/3 + 1/3 + 1/3 = 1.0
  // Sebastian: 1/3             = 0.33
  // Total:                     = 7.0

  rows
  |> TestHelpers.evaluateAggregator executionContext diffixCount [ aidsExpression ]
  |> should equal (Integer 5L)

[<Fact>]
let ``count distinct accepts rows with shared contribution`` () =
  let email values = values |> List.map String |> Value.List
  let firstName = email

  let aidsExpression = ListExpr [ ColumnReference(0, ListType StringType); ColumnReference(1, ListType StringType) ]
  let dataColumn = ColumnReference(2, StringType)

  // See `docs/distinct pre-processing.md` for explanation.
  let rows =
    [
      [| email [ "Paul"; "Sebastian" ]; firstName [ "Sebastian" ]; String "Apple" |]
      [| email [ "Paul"; "Edon" ]; firstName [ "Sebastian" ]; String "Apple" |]
      [| email [ "Sebastian" ]; firstName [ "Sebastian" ]; String "Apple" |]
      [| email [ "Cristian" ]; firstName [ "Paul" ]; String "Apple" |]
      [| email [ "Edon" ]; firstName [ "Paul" ]; String "Apple" |]
      [| email [ "Edon" ]; firstName [ "Paul" ]; String "Pear" |]
      [| email [ "Paul" ]; firstName [ "Paul" ]; String "Pineapple" |]
      [| email [ "Cristian" ]; firstName [ "Paul" ]; String "Lemon" |]
      [| email [ "Cristian" ]; firstName [ "Felix" ]; String "Orange" |]
      [| email [ "Felix" ]; firstName [ "Edon" ]; String "Banana" |]
      [| email [ "Edon" ]; firstName [ "Cristian" ]; String "Grapefruit" |]
    ]

  rows
  |> TestHelpers.evaluateAggregator executionContext distinctDiffixCount [ aidsExpression; dataColumn ]
  |> should equal (Integer 5L)
