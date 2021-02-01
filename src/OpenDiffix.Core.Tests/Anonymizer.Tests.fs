module OpenDiffix.Core.AnonymizerTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let ctx = EvaluationContext.Default

let ids =
  [ 1, 5; 2, 4; 3, 2; 4, 1; 5, 5; 6, 4; 7, 3 ]
  |> List.collect (fun (id, count) -> List.replicate count id)
  |> List.map (int64 >> Integer >> Array.singleton)

let rows =
  let defaultUserRows = ids |> List.map (fun idArray -> Array.append idArray [| String "value" |])
  let extraUserRow = [ [| Integer 8L; Null |] ]
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

[<Fact>]
let ``anon count distinct aid 1`` () =
  let diffixCountDistinct = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = true })
  // count(distinct aid_column) --> count_diffix(aid_column) with distinct = true
  ids |> evalAggr diffixCountDistinct [ aidColumn ] |> should equal (Integer 7L)

[<Fact>]
let ``anon count(*) 1`` () =
  let diffixCount = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = false })
  // - count(*) --> diffix_count(aid) with distinct = false
  // - outlier is 5, top is 5, so no substitution.
  // - 0 noise
  ids |> evalAggr diffixCount [ aidColumn ] |> should equal (Integer 24L)

[<Fact>]
let ``anon count(col) 1`` () =
  let diffixCount = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = false })
  // - count(col) --> diffix_count(aid, col) with distinct = false
  // - 1 user with Null string is ignored
  // - outlier is 5, top is 5, so no substitution.
  // - 0 noise
  rows
  |> evalAggr diffixCount [ aidColumn; strColumn ]
  |> should equal (Integer 24L)

[<Fact>]
let ``anon count returns Null if insufficient data`` () =
  let diffixCount = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = false })
  let diffixCountDistinct = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = true })
  let firstRow = rows |> List.take 1
  firstRow |> evalAggr diffixCount [ aidColumn; strColumn ] |> should equal Null
  firstRow |> evalAggr diffixCount [ aidColumn ] |> should equal Null
  firstRow |> evalAggr diffixCountDistinct [ aidColumn ] |> should equal Null
