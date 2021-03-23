module OpenDiffix.CLI.JsonEncodersDecoders

open Thoth.Json.Net
open OpenDiffix.Core.AnonymizerTypes

type QueryRequest = {
  Query: string
  DbPath: string
  AnonymizationParameters: AnonymizationParams
}

let encodeValue =
  function
  | OpenDiffix.Core.Value.Null -> Encode.nil
  | OpenDiffix.Core.Value.Boolean bool -> Encode.bool bool
  | OpenDiffix.Core.Value.Integer int64 -> Encode.int64 int64
  | OpenDiffix.Core.Value.Real float -> Encode.float float
  | OpenDiffix.Core.Value.String string -> Encode.string string

let encodeRow values =
  Encode.list (
    values
    |> Array.toList
    |> List.map encodeValue
  )

let encodeQueryResult (queryResult: OpenDiffix.Core.QueryResult) =
  Encode.object [
    "columns", Encode.list (queryResult.Columns |> List.map Encode.string)
    "rows", Encode.list (queryResult.Rows |> List.map encodeRow)
  ]

let encodeThreshold (t: Threshold) = Encode.object [ "lower", Encode.int t.Lower; "upper", Encode.int t.Upper ]

let encodeNoiseParam (np: NoiseParam) =
  Encode.object [ "standard_dev", Encode.float np.StandardDev; "cutoff", Encode.float np.Cutoff ]

let encodeAidColumnSetting (aidSetting: AIDSetting) =
  Encode.object [
    "name", Encode.string aidSetting.Name
    "minimum_allowed", Encode.int aidSetting.MinimumAllowed
  ]

let encodeTableSettings (ts: TableSettings) =
  Encode.object [ "aid_columns", Encode.list (ts.AidColumns |> List.map encodeAidColumnSetting) ]

let encodeAnonParams (ap: AnonymizationParams) =
  Encode.object [
    "table_settings",
    Encode.list (
      ap.TableSettings
      |> Map.toList
      |> List.map (fun (table, settings) ->
        Encode.object [ "table", Encode.string table; "settings", encodeTableSettings settings ]
      )
    )
    "outlier_count", encodeThreshold ap.OutlierCount
    "top_count", encodeThreshold ap.TopCount
    "noise", encodeNoiseParam ap.Noise
  ]

let encodeRequestParams query dbPath anonParams =
  Encode.object [
    "anonymization_parameters", encodeAnonParams anonParams
    "query", Encode.string query
    "database_path", Encode.string dbPath
  ]

let encodeErrorMsg errorMsg =
  Encode.object [
    "success", Encode.bool false
    "error", Encode.string errorMsg
  ]

let encodeIndividualQueryResponse (queryRequest: QueryRequest) =
  function
  | Ok queryResult ->
    Encode.object [
      "success", Encode.bool true
      "anonymization_parameters", encodeAnonParams queryRequest.AnonymizationParameters
      "result", encodeQueryResult queryResult
    ]
  | Error parseError ->
    encodeErrorMsg (parseError.ToString())

let encodeBatchRunResult (time: System.DateTime) version queryResults =
  Encode.object [
    "version", version
    "time", Encode.string (time.ToLongDateString())
    "query_results", Encode.list queryResults
  ]

let decodeRequestParams content =
  Decode.Auto.fromString<QueryRequest list>(content, caseStrategy=SnakeCase)
