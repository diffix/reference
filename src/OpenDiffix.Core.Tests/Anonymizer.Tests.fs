module OpenDiffix.Core.AnonymizerTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let ctx = EvaluationContext.Default

let ids =
  [ 1, 5; 2, 4; 3, 2; 4, 1; 5, 5; 6, 4; 7, 3 ]
  |> List.collect (fun (id, count) -> List.replicate count id)
  |> List.map (int64 >> Integer >> Array.singleton)

let aidColumn = ColumnReference(0, IntegerType)

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
let ``anon count distinct 1`` () =
  let diffixCountDistinct = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = true })
  // select count(distinct aid_column)
  ids |> evalAggr diffixCountDistinct [ aidColumn ] |> should equal (Integer 7L)
