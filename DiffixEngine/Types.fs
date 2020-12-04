module DiffixEngine.Types

open Thoth.Json.Net

type LowCountSettings =
  {
    Threshold: float
    StdDev: float
  }
  
  static member Defaults =
    {
      Threshold = 5.
      StdDev = 2.
    }
    
  static member Decoder: Decoder<LowCountSettings> =
    Decode.object (
      fun get ->
        {
          Threshold =
            get.Optional.Field "threshold" Decode.float
            |> Option.defaultValue LowCountSettings.Defaults.Threshold
          StdDev =
            get.Optional.Field "std_dev" Decode.float
            |> Option.defaultValue LowCountSettings.Defaults.StdDev
        }
    )
    
  static member Encoder (settings: LowCountSettings) =
    Encode.object [
      "threshold", Encode.float settings.Threshold
      "std_dev", Encode.float settings.StdDev
    ]

type AnonymizationParams =
  {
    AidColumnOption: string option
    Seed: int
    LowCountSettings: LowCountSettings option
  }
  
  static member Encoder (anonymizationParams: AnonymizationParams) =
    Encode.object [
      "anonymization_parameters", Encode.object [
        "aid_columns", Encode.list (
          anonymizationParams.AidColumnOption
          |> Option.map (fun aid -> [Encode.string aid])
          |> Option.defaultValue []
        )
        "seed", Encode.int anonymizationParams.Seed
        "low_count",
          anonymizationParams.LowCountSettings
          |> Option.map LowCountSettings.Encoder
          |> Option.defaultValue (Encode.bool false)
      ]
    ]

type RequestParams =
  {
    AnonymizationParams: AnonymizationParams
    Query: string
    DatabasePath: string
  }
  
  static member Encoder (requestParams: RequestParams) =
    Encode.object [
      "anonymization_parameters", AnonymizationParams.Encoder requestParams.AnonymizationParams
    ]

type ColumnName = string

type ColumnValue =
  | IntegerValue of int
  | StringValue of string
  
  static member Encoder (columnValue: ColumnValue) =
    match columnValue with
    | IntegerValue intValue -> Encode.int intValue
    | StringValue strValue -> Encode.string strValue
  
type NonPersonalColumnCell = {
  ColumnName: string
  ColumnValue: ColumnValue
}

type ColumnCell = {
  ColumnName: string
  ColumnValue: ColumnValue
}

type AnonymizableColumnCell = {
  AidValue: ColumnValue Set
}
  
type AnonymizableRow = {
  AidValues: ColumnValue Set
  Columns: ColumnCell list
}

type NonPersonalRow = {
  Columns: ColumnCell list
}

type Row =
  | AnonymizableRow of AnonymizableRow
  | NonPersonalRow of NonPersonalRow
  
type QueryResult =
  | ResultTable of Row list
  
  static member Encoder (requestParams: RequestParams) (queryResult: QueryResult) =
    match queryResult with
    | ResultTable rows ->
      let encodeColumnNames columns = 
        columns
        |> List.map(fun column -> Encode.string column.ColumnName) 
        |> Encode.list
        
      let columnNames =
        match List.tryHead rows with
        | Some (AnonymizableRow anonymizableRow) -> encodeColumnNames anonymizableRow.Columns
        | Some (NonPersonalRow nonPersonalRow) -> encodeColumnNames nonPersonalRow.Columns
        | None -> Encode.list []
      
      let encodeColumns columns = 
        columns
        |> List.map(fun column -> ColumnValue.Encoder column.ColumnValue) 
        |> Encode.list
        
      let values =
        rows
        |> List.map(
          function
          | AnonymizableRow anonymizableRow -> encodeColumns anonymizableRow.Columns
          | NonPersonalRow nonPersonalRow -> encodeColumns nonPersonalRow.Columns
        )
        |> Encode.list
      
      Encode.object [
        "success", Encode.bool true
        "column_names", columnNames
        "values", values
        "anonymization", RequestParams.Encoder requestParams
      ]
  
type QueryError =
  | ParseError of string
  | DbNotFound
  | InvalidRequest of string
  | ExecutionError of string
  | UnexpectedError of string
  
  static member Encoder (queryResult: QueryError) =
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