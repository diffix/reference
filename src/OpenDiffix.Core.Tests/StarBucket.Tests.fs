module OpenDiffix.Core.StarBucketTests

open Xunit
open FsUnit.Xunit
open CommonTypes

// C and D are below the default lowThresh of 3 and will be suppressed
let csv =
  """
  letter
  A
  A
  A
  B
  B
  B
  C
  C
  D
  """

// now only C is below the default lowThresh, and so is the *-bucket
let csvSuppressedStarBucket = csv.Replace("D\n", "")

let query =
  """
  SELECT letter, diffix_count(*, RowIndex), diffix_low_count(RowIndex)
  FROM table
  GROUP BY 1
  """

[<Fact>]
let ``Counts all suppressed buckets`` () =
  let mutable suppressedAnonCount = Null
  let pullHookResultsCallback results = suppressedAnonCount <- results

  HookTestHelpers.run [ StarBucket.hook pullHookResultsCallback ] csv query
  |> ignore

  suppressedAnonCount |> should equal (Integer 3L)

[<Fact>]
let ``Counts all suppressed buckets, but suppresses the star bucket`` () =
  let mutable suppressedAnonCount = Null
  let pullHookResultsCallback results = suppressedAnonCount <- results

  HookTestHelpers.run [ StarBucket.hook pullHookResultsCallback ] csvSuppressedStarBucket query
  |> ignore

  suppressedAnonCount |> should equal Null

[<Fact>]
let ``Works together with count(value) aggregators`` () =
  let query =
    """
    SELECT letter, diffix_count(*, RowIndex), diffix_low_count(RowIndex), diffix_count(letter, RowIndex)
    FROM table
    GROUP BY 1
    """

  let mutable suppressedAnonCount = Null
  let pullHookResultsCallback results = suppressedAnonCount <- results

  HookTestHelpers.run [ StarBucket.hook pullHookResultsCallback ] csv query
  |> ignore

  suppressedAnonCount |> should equal (Integer 3L)
