module OpenDiffix.CLI.JsonEncodersDecoders

open OpenDiffix.Core
open Thoth.Json.Net

type QueryRequest = { Query: string; DbPath: string; AnonymizationParameters: AnonymizationParams }

let rec encodeValue =
  function
  | Null -> Encode.nil
  | Boolean bool -> Encode.bool bool
  | Integer int64 -> Encode.float (float int64)
  | Real float -> Encode.float float
  | String string -> Encode.string string
  | List values -> Encode.list (values |> List.map encodeValue)

let rec typeName =
  function
  | BooleanType -> "boolean"
  | IntegerType -> "integer"
  | RealType -> "real"
  | StringType -> "text"
  | ListType itemType -> typeName itemType + "[]"
  | UnknownType _ -> "unknown"

let encodeRow values =
  Encode.list (values |> Array.toList |> List.map encodeValue)

let encodeColumn (column: Column) =
  Encode.object [ "name", Encode.string column.Name; "type", column.Type |> typeName |> Encode.string ]

let encodeQueryResult (queryResult: QueryEngine.QueryResult) =
  Encode.object [
    "columns", Encode.list (queryResult.Columns |> List.map encodeColumn)
    "rows", Encode.list (queryResult.Rows |> List.map encodeRow)
  ]

let encodeInterval (i: Interval) =
  Encode.object [ "lower", Encode.int i.Lower; "upper", Encode.int i.Upper ]

let encodeTableSettings (ts: TableSettings) =
  Encode.object [ "aid_columns", Encode.list (ts.AidColumns |> List.map Encode.string) ]

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
    "low_threshold", Encode.int ap.Suppression.LowThreshold
    "low_mean_gap", Encode.float ap.Suppression.LowMeanGap
    "low_sd", Encode.float ap.Suppression.SD
    "outlier_count", encodeInterval ap.OutlierCount
    "top_count", encodeInterval ap.TopCount
    "noise_sd", Encode.float ap.NoiseSD
  ]

let encodeRequestParams query dbPath anonParams =
  Encode.object [
    "anonymization_parameters", encodeAnonParams anonParams
    "query", Encode.string query
    "database_path", Encode.string dbPath
  ]

let encodeErrorMsg errorMsg =
  Encode.object [ "success", Encode.bool false; "error", Encode.string errorMsg ]

let encodeIndividualQueryResponse queryRequest queryResult =
  Encode.object [
    "success", Encode.bool true
    "anonymization_parameters", encodeAnonParams queryRequest.AnonymizationParameters
    "result", encodeQueryResult queryResult
  ]

let encodeBatchRunResult (time: System.DateTime) version queryResults =
  Encode.object [
    "version", version
    "time", Encode.string (time.ToLongDateString())
    "query_results", Encode.list queryResults
  ]

let decodeRequestParams content =
  Decode.Auto.fromString<QueryRequest list> (content, caseStrategy = SnakeCase)
