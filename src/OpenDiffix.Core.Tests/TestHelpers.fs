[<AutoOpen>]
module OpenDiffix.Core.TestHelpers

type DBFixture() =
  member this.DataProvider =
    new OpenDiffix.CLI.SQLite.DataProvider(__SOURCE_DIRECTORY__ + "/../../data/data.sqlite") :> IDataProvider

let evaluateAggregator ctx aggSpec args rows =
  let aggregator = Aggregator.create (aggSpec, args)
  let processor = fun row -> args |> List.map (Expression.evaluate row) |> aggregator.Transition
  List.iter processor rows
  aggregator.Final ctx

let dummyDataProvider schema =
  { new IDataProvider with
      member _.OpenTable(_table, _columnIndices) = failwith "Opening tables not supported"
      member _.GetSchema() = schema
      member _.Dispose() = ()
  }
