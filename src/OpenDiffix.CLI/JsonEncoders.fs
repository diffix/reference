module OpenDiffix.CLI.JsonEncoders

open Thoth.Json.Net
open OpenDiffix.Core.AnonymizerTypes

let encodeThreshold (t: Threshold) = Encode.object [ "lower", Encode.int t.Lower; "upper", Encode.int t.Upper ]

let encodeNoiseParam (np: NoiseParam) =
  Encode.object [ "standard_dev", Encode.float np.StandardDev; "cutoff", Encode.float np.Cutoff ]

let encodeTableSettings (ts: TableSettings) =
  Encode.object [ "aid_columns", Encode.list (ts.AidColumns |> List.map Encode.string) ]

let encodeAnonParams (ap: AnonymizationParams) =
  Encode.object [
    "table_settings",
    Encode.list
      (ap.TableSettings
       |> Map.toList
       |> List.map (fun (table, settings) ->
            Encode.object [ "table", Encode.string table; "settings", encodeTableSettings settings ]))
    "low_count_threshold", encodeThreshold ap.LowCountThreshold
    "outlier_count", encodeThreshold ap.OutlierCount
    "top_count", encodeThreshold ap.TopCount
    "noise", encodeNoiseParam ap.Noise
  ]

let encodeRequestParams (rp: RequestParams) =
  Encode.object [
    "anonymization_parameters", encodeAnonParams rp.AnonymizationParams
    "query", Encode.string rp.Query
    "database_path", Encode.string rp.DatabasePath
  ]
