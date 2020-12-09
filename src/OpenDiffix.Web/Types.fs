module OpenDiffix.Web.Types

open Thoth.Json.Net
open OpenDiffix.Core.AnonymizerTypes

type LowCountSettingsJson =
  static member Decoder: Decoder<LowCountSettings> =
    Decode.object (fun get ->
      { Threshold =
          get.Optional.Field "threshold" Decode.float
          |> Option.defaultValue LowCountSettings.Defaults.Threshold
        StdDev =
          get.Optional.Field "std_dev" Decode.float
          |> Option.defaultValue LowCountSettings.Defaults.StdDev })

  static member Encoder(settings: LowCountSettings) =
    Encode.object [ "threshold", Encode.float settings.Threshold; "std_dev", Encode.float settings.StdDev ]

type AnonymizationParamsJson =
  static member Encoder(anonymizationParams: AnonymizationParams) =
    Encode.object [
      "anonymization_parameters",
      Encode.object [
        "aid_columns",
        Encode.list
          (anonymizationParams.AidColumnOption
           |> Option.map (fun aid -> [ Encode.string aid ])
           |> Option.defaultValue [])
        "seed", Encode.int anonymizationParams.Seed
        "low_count",
        anonymizationParams.LowCountSettings
        |> Option.map LowCountSettingsJson.Encoder
        |> Option.defaultValue (Encode.bool false)
      ]
    ]

type RequestParamsJson =
  static member Encoder(requestParams: RequestParams) =
    Encode.object [ "anonymization_parameters", AnonymizationParamsJson.Encoder requestParams.AnonymizationParams ]

type ColumnValueJson =
  static member Encoder(columnValue: ColumnValue) =
    match columnValue with
    | IntegerValue intValue -> Encode.int intValue
    | StringValue strValue -> Encode.string strValue

type QueryResultJson =
  static member Encoder (requestParams: RequestParams) (queryResult: QueryResult) =
    match queryResult with
    | ResultTable rows ->
        let encodeColumnNames columns =
          columns
          |> List.map (fun column -> Encode.string column.ColumnName)
          |> Encode.list

        let columnNames =
          match List.tryHead rows with
          | Some (AnonymizableRow anonymizableRow) -> encodeColumnNames anonymizableRow.Columns
          | Some (NonPersonalRow nonPersonalRow) -> encodeColumnNames nonPersonalRow.Columns
          | None -> Encode.list []

        let encodeColumns columns =
          columns
          |> List.map (fun column -> ColumnValueJson.Encoder column.ColumnValue)
          |> Encode.list

        let values =
          rows
          |> List.map (function
               | AnonymizableRow anonymizableRow -> encodeColumns anonymizableRow.Columns
               | NonPersonalRow nonPersonalRow -> encodeColumns nonPersonalRow.Columns)
          |> Encode.list

        Encode.object [
          "success", Encode.bool true
          "column_names", columnNames
          "values", values
          "anonymization", RequestParamsJson.Encoder requestParams
        ]

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
  { AidColumns: string list
    LowCountFiltering: LowCountSettings option
    Seed: int }

  static member Default =
    { AidColumns = []
      LowCountFiltering = Some LowCountSettings.Defaults
      Seed = 1 }

  static member Decoder: Decoder<AnonymizationParameters> =
    Decode.object (fun get ->
      { AidColumns =
          get.Optional.Field "aid_columns" (Decode.list Decode.string)
          |> Option.defaultValue AnonymizationParameters.Default.AidColumns
        LowCountFiltering = get.Optional.Field "low_count_filter" LowCountSettingsJson.Decoder
        Seed =
          get.Optional.Field "seed" Decode.int
          |> Option.defaultValue AnonymizationParameters.Default.Seed })

type QueryRequest =
  { Query: string
    Database: string
    Anonymization: AnonymizationParameters }

  static member Decoder: Decoder<QueryRequest> =
    Decode.object (fun get ->
      { Query = get.Required.Field "query" Decode.string
        Database = get.Required.Field "database" Decode.string
        Anonymization =
          get.Optional.Field "anonymization_parameters" AnonymizationParameters.Decoder
          |> Option.defaultValue AnonymizationParameters.Default })

  static member withQuery query db =
    { Query = query
      Database = db
      Anonymization = AnonymizationParameters.Default }
