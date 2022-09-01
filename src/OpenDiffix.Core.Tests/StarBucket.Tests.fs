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

let query =
  """
  SELECT letter, diffix_count(*, RowIndex), diffix_low_count(RowIndex)
  FROM table
  GROUP BY 1
  """

[<Fact>]
let ``Counts all suppressed buckets`` () =
  let mutable suppressedAnonCount = Null

  let pullHookResultsCallback aggCtx bucket buckets =
    suppressedAnonCount <- Bucket.finalizeAggregate 0 aggCtx bucket
    buckets

  HookTestHelpers.run [ StarBucket.hook pullHookResultsCallback ] csv query
  |> ignore

  suppressedAnonCount |> should equal (Integer 3L)

[<Fact>]
let ``Counts all suppressed buckets, but suppresses the star bucket`` () =
  let mutable suppressedAnonCount = Null

  let pullHookResultsCallback aggCtx bucket buckets =
    suppressedAnonCount <- Bucket.finalizeAggregate 0 aggCtx bucket
    buckets

  // now only C is below the default lowThresh, and so is the *-bucket
  let csvSuppressedStarBucket = csv.Replace("D" + System.Environment.NewLine, "")

  HookTestHelpers.run [ StarBucket.hook pullHookResultsCallback ] csvSuppressedStarBucket query
  |> ignore

  suppressedAnonCount |> should equal Null
