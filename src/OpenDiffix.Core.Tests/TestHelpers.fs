[<AutoOpen>]
module OpenDiffix.Core.TestHelpers

open Xunit

type DBFixture() =
  member this.DataProvider =
    new OpenDiffix.CLI.SQLite.DataProvider(__SOURCE_DIRECTORY__ + "/../../data/data.sqlite") :> IDataProvider

let evaluateAggregator ctx fn args rows =
  let aggregator = Aggregator.create ctx true fn
  let processor = fun row -> args |> List.map (Expression.evaluate ctx row) |> aggregator.Transition
  List.iter processor rows
  aggregator.Final ctx

let dummyDataProvider schema =
  { new IDataProvider with
      member _.OpenTable _table = failwith "Opening tables not supported"
      member _.GetSchema() = schema
      member _.Dispose() = ()
  }
