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

let rec private encodeValue =
  function
  | Null -> Encode.nil
  | Boolean bool -> Encode.bool bool
  | Integer int64 -> Encode.float (float int64)
  | Real float -> Encode.float float
  | String string -> Encode.string string
  | List values -> Encode.list (values |> List.map encodeValue)

let rec private typeName =
  function
  | BooleanType -> "boolean"
  | IntegerType -> "integer"
  | RealType -> "real"
  | StringType -> "text"
  | ListType itemType -> typeName itemType + "[]"
  | UnknownType _ -> "unknown"

let private encodeType = typeName >> Encode.string

let private generateDecoder<'T> = Decode.Auto.generateDecoder<'T> SnakeCase

let private extraCoders =
  Extra.empty
  |> Extra.withCustom encodeType generateDecoder<ExpressionType>
  |> Extra.withCustom encodeValue generateDecoder<Value>

let private generateEncoder<'T> = Encode.Auto.generateEncoder<'T> (caseStrategy = SnakeCase, extra = extraCoders)

let private encodeAnonParams (ap: AnonymizationParams) =
  let anonParams =
    {|
      TableSettings =
        (ap.TableSettings
         |> Map.toList
         |> List.map (fun (table, settings) -> {| Table = table; Settings = settings |}))
      LowThreshold = ap.Suppression.LowThreshold
      LowMeanGap = ap.Suppression.LowMeanGap
      LowSd = ap.Suppression.SD
      OutlierCount = ap.OutlierCount
      TopCount = ap.TopCount
      NoiseSd = ap.NoiseSD
    |}

  generateEncoder anonParams

let private encodeResponse response =
  match response with
  | Success response -> generateEncoder<QuerySuccessResponse> response
  | Error response -> generateEncoder<QueryErrorResponse> response

let encodeQueryResult (queryResult: QueryEngine.QueryResult) =
  Encode.Auto.toString (
    2,
    queryResult,
    caseStrategy = SnakeCase,
    extra = (extraCoders |> Extra.withCustom encodeResponse generateDecoder<QueryResponse>)
  )

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

  Encode.Auto.toString (
    2,
    batchRunResult,
    caseStrategy = SnakeCase,
    extra =
      (extraCoders
       |> Extra.withCustom encodeAnonParams generateDecoder<AnonymizationParams>)
  )

let decodeRequestParams content =
  Decode.Auto.fromString<QueryRequest list> (content, caseStrategy = SnakeCase)
