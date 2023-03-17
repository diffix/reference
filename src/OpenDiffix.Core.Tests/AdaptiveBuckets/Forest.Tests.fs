module OpenDiffix.Core.AdaptiveBuckets.ForestTests

open Xunit
open FsUnit.Xunit

open OpenDiffix.Core
open OpenDiffix.Core.AdaptiveBuckets.Range
open OpenDiffix.Core.AdaptiveBuckets.Forest

[<Fact>]
let ``Root ranges are anonymized`` () =
  let root, _ =
    [
      [| List [ Integer 1 ]; Integer 0; Integer 1 |]
      [| List [ Integer 2 ]; Integer 0; Integer 5 |]
      [| List [ Integer 3 ]; Integer 0; Integer 2 |]
      [| List [ Integer 4 ]; Integer 0; Integer 7 |]
      [| List [ Integer 5 ]; Integer 0; Integer 21 |]
      [| List [ Integer 6 ]; Integer 0; Integer 4 |]
      [| List [ Integer 7 ]; Integer 0; Integer 21 |]
      [| List [ Integer 8 ]; Integer 0; Integer 28 |]
      [| List [ Integer 9 ]; Integer 0; Integer 19 |]
      [| List [ Integer 10 ]; Integer 0; Integer 2 |]
      [| List [ Integer 11 ]; Integer 1; Integer 1 |]
      [| List [ Integer 12 ]; Integer 1; Integer 13 |]
      [| List [ Integer 13 ]; Integer 1; Integer 25 |]
      [| List [ Integer 14 ]; Integer 1; Integer 30 |]
      [| List [ Integer 15 ]; Integer 1; Integer 6 |]
      [| List [ Integer 16 ]; Integer 1; Integer 2 |]
      [| List [ Integer 17 ]; Integer 1; Integer 15 |]
      [| List [ Integer 18 ]; Integer 1; Integer 24 |]
      [| List [ Integer 19 ]; Integer 1; Integer 9 |]
      [| List [ Integer 20 ]; Integer 0; Integer 100 |]
      [| List [ Integer 21 ]; Integer -5; Integer 0 |]
    ]
    |> buildForest defaultAnonContext 2

  (Tree.nodeData root).SnappedRanges
  |> should equal [| { Min = 0.0; Max = 2.0 }; { Min = 0.0; Max = 32.0 } |]

[<Fact>]
let ``Multiple rows per AID`` () =
  let rows =
    [
      [| List [ Integer 1 ]; Integer 1 |]
      [| List [ Integer 2 ]; Integer 1 |]
      [| List [ Integer 3 ]; Integer 1 |]
      [| List [ Integer 4 ]; Integer 1 |]
      [| List [ Integer 5 ]; Integer 1 |]
      [| List [ Integer 6 ]; Integer 1 |]
      [| List [ Integer 7 ]; Integer 1 |]
      [| List [ Integer 8 ]; Integer 1 |]
      [| List [ Integer 9 ]; Integer 1 |]
      [| List [ Integer 10 ]; Integer 0 |]
      [| List [ Integer 11 ]; Integer 0 |]
      [| List [ Integer 12 ]; Integer 0 |]
      [| List [ Integer 13 ]; Integer 0 |]
      [| List [ Integer 14 ]; Integer 0 |]
      [| List [ Integer 15 ]; Integer 0 |]
      [| List [ Integer 16 ]; Integer 0 |]
      [| List [ Integer 17 ]; Integer 0 |]
    ]

  let root, _ = rows |> buildForest defaultAnonContext 1

  // Sanity check, there's enough AIDs to branch at least once.
  match root with
  | Tree.Branch _ -> ()
  | _ -> failwith "Expected a branch root"

  let rowsAid =
    rows
    |> List.map (fun row -> row |> Array.tail |> Array.insertAt 0 (List [ Integer 1L ]))

  let rootAid, _ = rowsAid |> buildForest defaultAnonContext 1

  match rootAid with
  | Tree.Leaf l ->
    let aidContributions = l.Data.Contributions.[0].AidContributions.Values |> Seq.toList

    // There's a single AID contributing all rows.
    aidContributions.Length |> should equal 1
    aidContributions.Head |> should equal (float rows.Length)
  | _ -> failwith "Expected a leaf root"

[<Fact>]
let ``Outliers are not dropped from 1-dim trees`` () =
  let root, _ =
    [
      [| List [ Integer 1 ]; Integer 1 |]
      [| List [ Integer 2 ]; Integer 5 |]
      [| List [ Integer 3 ]; Integer 2 |]
      [| List [ Integer 4 ]; Integer 7 |]
      [| List [ Integer 5 ]; Integer 21 |]
      [| List [ Integer 6 ]; Integer 4 |]
      [| List [ Integer 7 ]; Integer 21 |]
      [| List [ Integer 8 ]; Integer 28 |]
      [| List [ Integer 9 ]; Integer 19 |]
      [| List [ Integer 10 ]; Integer 2 |]
      [| List [ Integer 11 ]; Integer 1 |]
      [| List [ Integer 12 ]; Integer 13 |]
      [| List [ Integer 13 ]; Integer 25 |]
      [| List [ Integer 14 ]; Integer 30 |]
      [| List [ Integer 15 ]; Integer 6 |]
      [| List [ Integer 16 ]; Integer 2 |]
      [| List [ Integer 17 ]; Integer 15 |]
      [| List [ Integer 18 ]; Integer 24 |]
      [| List [ Integer 19 ]; Integer 9 |]
      [| List [ Integer 20 ]; Integer 100 |]
      [| List [ Integer 21 ]; Integer 0 |]
    ]
    |> buildForest noiselessAnonContext 1

  root |> Tree.nodeData |> Tree.noisyRowCount |> should equal 21L
