namespace OpenDiffix.Core.AnonymizerTypes

open Thoth.Json.Net

type Threshold =
  {
    Lower: int
    Upper: int
  }

  static member Default = { Lower = 2; Upper = 5 }

  static member Encoder(t: Threshold) = Encode.object [ "lower", Encode.int t.Lower; "upper", Encode.int t.Upper ]

type NoiseParam =
  {
    StandardDev: float
    Cutoff: float
  }

  static member Default = { StandardDev = 2.; Cutoff = 5. }

  static member Encoder(np: NoiseParam) =
    Encode.object [ "standard_dev", Encode.float np.StandardDev; "cutoff", Encode.float np.Cutoff ]

type TableSettings =
  {
    AidColumns: string list
  }

  static member Encoder(ts: TableSettings) =
    Encode.object [ "aid_columns", Encode.list (ts.AidColumns |> List.map Encode.string) ]

type AnonymizationParams =
  {
    TableSettings: Map<string, TableSettings>
    Seed: int
    LowCountThreshold: Threshold

    // Count params
    OutlierCount: Threshold
    TopCount: Threshold
    Noise: NoiseParam
  }

  static member Encoder(ap: AnonymizationParams) =
    Encode.object [
      "table_settings",
      Encode.list
        (ap.TableSettings
         |> Map.toList
         |> List.map (fun (table, settings) ->
              Encode.object [ "table", Encode.string table; "settings", TableSettings.Encoder settings ]))
      "low_count_threshold", Threshold.Encoder ap.LowCountThreshold
      "outlier_count", Threshold.Encoder ap.OutlierCount
      "top_count", Threshold.Encoder ap.TopCount
      "noise", NoiseParam.Encoder ap.Noise
    ]

type RequestParams =
  {
    AnonymizationParams: AnonymizationParams
    Query: string
    DatabasePath: string
  }

  static member Encoder(rp: RequestParams) =
    Encode.object [
      "anonymization_parameters", AnonymizationParams.Encoder rp.AnonymizationParams
      "query", Encode.string rp.Query
      "database_path", Encode.string rp.DatabasePath
    ]

type ColumnName = string
type Columns = ColumnName list

type ColumnValue =
  | IntegerValue of int
  | StringValue of string
  | NullValue

  static member ToString =
    function
    | IntegerValue value -> $"%i{value}"
    | StringValue value -> value
    | NullValue -> "<null>"

type NonPersonalRow = ColumnValue list
type NonPersonalRows = NonPersonalRow list
type PersonalRow = { AidValues: ColumnValue Set; RowValues: NonPersonalRow }
type PersonalRows = PersonalRow list

type QueryResult = { Columns: Columns; Rows: NonPersonalRows }

type QueryError = string
