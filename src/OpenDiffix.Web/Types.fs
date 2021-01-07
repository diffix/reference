module OpenDiffix.Web.Types

open Thoth.Json.Net
open OpenDiffix.Core.AnonymizerTypes

type LowCountSettingsJson =
  static member Decoder: Decoder<LowCountSettings> =
    Decode.object (fun get ->
      {
        Threshold =
          get.Optional.Field "threshold" Decode.float
          |> Option.defaultValue LowCountSettings.Defaults.Threshold
        StdDev =
          get.Optional.Field "std_dev" Decode.float
          |> Option.defaultValue LowCountSettings.Defaults.StdDev
      }
    )

type ColumnValueJson =
  static member Encoder(columnValue: ColumnValue) =
    match columnValue with
    | IntegerValue intValue -> Encode.int intValue
    | StringValue strValue -> Encode.string strValue

type QueryResultJson =
  static member Encoder(queryResult: QueryResult) =
    match queryResult with
    | { Rows = rows; Columns = columns } ->
        let columnNames = columns |> List.map Encode.string |> Encode.list

        let values =
          rows
          |> List.map (fun row ->
            row
            |> List.map
                 (function
                 | StringValue value -> Encode.string value
                 | IntegerValue value -> Encode.int value)
            |> Encode.list
          )
          |> Encode.list

        Encode.object [ "success", Encode.bool true; "column_names", columnNames; "values", values ]

type QueryErrorJson =
  static member Encoder(queryResult: QueryError) =
    match queryResult with
    | ParseError error ->
        Encode.object [
          "success", Encode.bool false
          "type", Encode.string "Parse error"
          "error_message", Encode.string error
        ]
    | DbNotFound ->
        Encode.object [
          "success", Encode.bool false
          "type", Encode.string "Database not found"
          "error_message", Encode.string "Could not find the database"
        ]
    | InvalidRequest error ->
        Encode.object [
          "success", Encode.bool false
          "type", Encode.string "Invalid request"
          "error_message", Encode.string error
        ]
    | ExecutionError error ->
        Encode.object [
          "success", Encode.bool false
          "type", Encode.string "Execution error"
          "error_message", Encode.string error
        ]
    | UnexpectedError error ->
        Encode.object [
          "success", Encode.bool false
          "type", Encode.string "Unexpected error"
          "error_message", Encode.string error
        ]

type AnonymizationParameters =
  {
    AidColumns: string list
    LowCountFiltering: LowCountSettings option
    Seed: int
  }

  static member Default =
    {
      AidColumns = []
      LowCountFiltering = Some LowCountSettings.Defaults
      Seed = 1
    }

  static member Decoder: Decoder<AnonymizationParameters> =
    Decode.object (fun get ->
      {
        AidColumns =
          get.Optional.Field "aid_columns" (Decode.list Decode.string)
          |> Option.defaultValue AnonymizationParameters.Default.AidColumns
        LowCountFiltering = get.Optional.Field "low_count_filter" LowCountSettingsJson.Decoder
        Seed =
          get.Optional.Field "seed" Decode.int
          |> Option.defaultValue AnonymizationParameters.Default.Seed
      }
    )

type QueryRequest =
  {
    Query: string
    Database: string
    Anonymization: AnonymizationParameters
  }

  static member Decoder: Decoder<QueryRequest> =
    Decode.object (fun get ->
      {
        Query = get.Required.Field "query" Decode.string
        Database = get.Required.Field "database" Decode.string
        Anonymization =
          get.Optional.Field "anonymization_parameters" AnonymizationParameters.Decoder
          |> Option.defaultValue AnonymizationParameters.Default
      }
    )

  static member WithQuery query db =
    {
      Query = query
      Database = db
      Anonymization = AnonymizationParameters.Default
    }
