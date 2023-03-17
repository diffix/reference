module OpenDiffix.Core.AdaptiveBuckets.CommonTests

open Xunit
open FsUnit.Xunit

open System
open System.Globalization

open OpenDiffix.Core

let private parseTimestamp (str: string) =
  match
    DateTime.TryParse(
      str,
      CultureInfo.InvariantCulture,
      DateTimeStyles.AdjustToUniversal ||| DateTimeStyles.AssumeUniversal
    )
  with
  | true, value -> Timestamp value
  | _ -> Null


[<Fact>]
let ``Parse timestamp from ISO 8601`` () =
  parseTimestamp "2023-01-24T14:25:47.000Z"
  |> should equal (makeTimestamp (2023, 1, 24) (14, 25, 47))

  parseTimestamp "2023-01-24T14:25:47-05:00"
  |> should equal (makeTimestamp (2023, 1, 24) (19, 25, 47))

  parseTimestamp "2023-01-24T14:25:47+01:00"
  |> should equal (makeTimestamp (2023, 1, 24) (13, 25, 47))

[<Fact>]
let ``Parse timestamp from loose string`` () =
  parseTimestamp "2023-01-24"
  |> should equal (makeTimestamp (2023, 1, 24) (0, 0, 0))

  parseTimestamp "2023-01-24 14:25"
  |> should equal (makeTimestamp (2023, 1, 24) (14, 25, 0))

  parseTimestamp "2023/01/24 14:25:47"
  |> should equal (makeTimestamp (2023, 1, 24) (14, 25, 47))
