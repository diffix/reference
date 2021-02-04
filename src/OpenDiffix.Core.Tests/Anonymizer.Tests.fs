module OpenDiffix.Core.AnonymizerTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let ctx = EvaluationContext.Default

let ids =
  [ 1, 5; 2, 4; 3, 2; 4, 1; 5, 5; 6, 4; 7, 3; 8, 6 ]
  |> List.collect (fun (id, count) -> List.replicate count id)
  |> List.map (int64 >> Integer >> Array.singleton >> Row.OfValues)

let rows =
  let defaultUserRows = ids |> List.map (fun idArray -> Row.Append idArray (Row.OfValues [| String "value" |]))
  let extraUserRow = [ Row.OfValues [| Integer 8L; Null |] ]
  List.append defaultUserRows extraUserRow

let aidColumn = ColumnReference(0, IntegerType)
let strColumn = ColumnReference(1, StringType)

let context =
  { EvaluationContext.Default with
      AnonymizationParams =
        {
          TableSettings = Map.empty
          Seed = 0
          LowCountThreshold = { Lower = 1; Upper = 1 }
          OutlierCount = { Lower = 1; Upper = 1 }
          TopCount = { Lower = 1; Upper = 1 }
          Noise = { StandardDev = 1.; Cutoff = 0. }
        }
  }

let evalAggr fn args rows =
  let processor = fun (acc: Expression.Accumulator) row -> acc.Process ctx args row
  let accumulator = List.fold processor (Expression.createAccumulator ctx fn) rows
  accumulator.Evaluate context

let distinctDiffixCount = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = true })
let diffixCount = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = false })

[<Fact>]
let ``anon count distinct aid 1`` () = ids |> evalAggr distinctDiffixCount [ aidColumn ] |> should equal (Integer 8L)

[<Fact>]
let ``anon count(*)`` () =
  // - replacing outlier 6, with top 5
  // - 0 noise
  ids |> evalAggr diffixCount [ aidColumn ] |> should equal (Integer 29L)

[<Fact>]
let ``anon count(col)`` () =
  // - 1 user with Null string is ignored
  // - replacing outlier 6 with top 5
  // - 0 noise
  rows
  |> evalAggr diffixCount [ aidColumn; strColumn ]
  |> should equal (Integer 29L)

[<Fact>]
let ``anon count returns Null if insufficient data`` () =
  let firstRow = rows |> List.take 1
  firstRow |> evalAggr diffixCount [ aidColumn; strColumn ] |> should equal Null
  firstRow |> evalAggr diffixCount [ aidColumn ] |> should equal Null
