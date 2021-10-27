module OpenDiffix.CLI.JsonEncodersDecoders

open OpenDiffix.Core
open Thoth.Json.Net

type QueryRequest = { Query: string; DbPath: string; AnonymizationParameters: AnonymizationParams }

module private rec Encoders =
  // each successful query result is decorated with data provided in the request
  type QueryResponseRequest = Result<QueryEngine.QueryResult * QueryRequest, string>

  let private noDecoder<'T> () : Decoder<'T> =
    (fun _str _obj -> failwith "Decoder not implemented")

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

  let private extraCoders () =
    Extra.empty
    |> Extra.withCustom encodeType (noDecoder ())
    |> Extra.withCustom encodeValue (noDecoder ())
    |> Extra.withCustom encodeAnonParams (noDecoder ())
    |> Extra.withCustom encodeResponse (noDecoder ())

  let autoEncode<'T> = Encode.Auto.generateEncoder<'T> (caseStrategy = SnakeCase, extra = extraCoders ())

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

    autoEncode anonParams

  let private encodeResponse (response: QueryResponseRequest) =
    match response with
    | Ok (queryResult, queryRequest) ->
      let response =
        {|
          Success = true
          AnonymizationParameters = queryRequest.AnonymizationParameters
          Result = queryResult
        |}

      autoEncode response
    | Error errorMsg ->
      let response = {| Success = false; error = errorMsg |}
      autoEncode response

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

open Encoders

let encodeVersionResult version = (version |> autoEncode).ToString()

let encodeQueryResult (queryResult: QueryEngine.QueryResult) = (queryResult |> autoEncode).ToString()

let encodeBatchRunResult (time: System.DateTime) version (queryResults: QueryResponseRequest list) =
  ({|
     Version = version
     Time = time.ToLongDateString()
     QueryResults = queryResults
   |}
   |> autoEncode)
    .ToString()

let decodeRequestParams content =
  Decode.Auto.fromString<QueryRequest list> (content, caseStrategy = SnakeCase)
