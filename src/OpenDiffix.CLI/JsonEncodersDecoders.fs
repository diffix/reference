module OpenDiffix.CLI.JsonEncodersDecoders

open OpenDiffix.Core
open Thoth.Json.Net

type QueryRequest = { Query: string; DbPath: string; AnonymizationParameters: AnonymizationParams }

type QuerySuccessResponse =
  {
    Success: bool
    AnonymizationParameters: AnonymizationParams
    Result: QueryEngine.QueryResult
  }

type QueryErrorResponse = { Success: bool; Error: string }

type QueryResponse =
  | Success of QuerySuccessResponse
  | Error of QueryErrorResponse

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

let private encodeType = typeName >> Encode.string

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

let private generateDecoder<'T> = Decode.Auto.generateDecoder<'T> SnakeCase

let private generateEncoder<'T> =
  Encode.Auto.generateEncoder<'T> (
    caseStrategy = SnakeCase,
    extra =
      (Extra.empty
       |> Extra.withCustom encodeType generateDecoder<ExpressionType>
       |> Extra.withCustom encodeValue generateDecoder<Value>
       |> Extra.withCustom encodeAnonParams generateDecoder<AnonymizationParams>)
  )

let private encodeResponse response =
  match response with
  | Success response -> generateEncoder<QuerySuccessResponse> response
  | Error response -> generateEncoder<QueryErrorResponse> response

let private extraCoders =
  Extra.empty
  |> Extra.withCustom encodeType generateDecoder<ExpressionType>
  |> Extra.withCustom encodeValue generateDecoder<Value>
  |> Extra.withCustom encodeResponse generateDecoder<QueryResponse>

let encodeQueryResult (queryResult: QueryEngine.QueryResult) =
  Encode.Auto.toString (2, queryResult, caseStrategy = SnakeCase, extra = extraCoders)

let buildQueryErrorResponse (errorMsg: string) =
  Error { Success = false; Error = errorMsg }

let buildQuerySuccessResponse (queryRequest: QueryRequest) queryResult =
  Success
    {
      Success = true
      AnonymizationParameters = queryRequest.AnonymizationParameters
      Result = queryResult
    }

let encodeVersionResult (version: AssemblyInfo.Version) =
  Encode.Auto.toString (2, version, caseStrategy = SnakeCase)

let encodeBatchRunResult (time: System.DateTime) (version: AssemblyInfo.Version) (queryResults: QueryResponse list) =
  let batchRunResult =
    {|
      Version = version
      Time = time.ToLongDateString()
      QueryResults = queryResults
    |}

  Encode.Auto.toString (2, batchRunResult, caseStrategy = SnakeCase, extra = extraCoders)

let decodeRequestParams content =
  Decode.Auto.fromString<QueryRequest list> (content, caseStrategy = SnakeCase)
