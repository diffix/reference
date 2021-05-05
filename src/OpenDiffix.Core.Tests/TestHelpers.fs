[<AutoOpen>]
module OpenDiffix.Core.TestHelpers

open Xunit

type DBFixture() =
  member this.DataProvider =
    new OpenDiffix.CLI.SQLite.DataProvider(__SOURCE_DIRECTORY__ + "/../../data/data.sqlite") :> IDataProvider

let assertOk (result: Result<'a, 'b>) =
  Assert.True(
    match result with
    | Ok _ -> true
    | Error _ -> false
  )

let assertError (result: Result<'a, 'b>) =
  Assert.True(
    match result with
    | Ok _ -> false
    | Error _ -> true
  )

let assertOkEqual<'a, 'b> (result: Result<'a, 'b>) (expected: 'a) =
  match result with
  | Ok value -> Assert.Equal(expected, value)
  | Error error -> failwith $"Did not expect error: %A{error}"

let assertErrorEqual (result: Result<'a, 'b>) (expected: 'b) =
  assertError result

  Assert.True(
    match result with
    | Error value when value = expected -> true
    | _ -> false
  )

let evaluateAggregator ctx fn args rows =
  let processor = fun (agg: Aggregator.T) row -> args |> List.map (Expression.evaluate ctx row) |> agg.Transition

  let aggregator = List.fold processor (Aggregator.create ctx fn) rows
  aggregator.Final ctx
