module OpenDiffix.Core.HookTestHelpers

open System

let private noiselessAnonParams: AnonymizationParams =
  {
    TableSettings = Map []
    Salt = [||]
    AccessLevel = Direct
    StrictCheck = false
    Suppression = { LowThreshold = 3; LowMeanGap = 0.; LayerSD = 0. }
    OutlierCount = { Lower = 1; Upper = 1 }
    TopCount = { Lower = 1; Upper = 1 }
    LayerNoiseSD = 0.
  }

let private csvReader (csv: string) =
  let rows =
    csv.Split("\n", StringSplitOptions.TrimEntries ||| StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun row -> row.Split(",", StringSplitOptions.TrimEntries))

  { new IDataProvider with
      member _.OpenTable(_table, _columnIndices) =
        rows
        |> Seq.skip 1
        |> Seq.mapi (fun i row -> Array.append [| Integer(int64 i) |] (Array.map Value.String row))

      member _.GetSchema() =
        let columns =
          rows.[0]
          |> Array.map (fun name -> { Name = name; Type = StringType })
          |> Array.toList

        [
          {
            Name = "table"
            Columns = { Name = "RowIndex"; Type = IntegerType } :: columns
          }
        ]

      member _.Dispose() = ()
  }

let private withHooks hooks context =
  { context with PostAggregationHooks = hooks }

let run hooks csv query =
  let queryContext = QueryContext.make noiselessAnonParams (csvReader csv) |> withHooks hooks
  QueryEngine.run queryContext query
