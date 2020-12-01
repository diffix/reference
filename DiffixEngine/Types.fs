module DiffixEngine.Types

open Thoth.Json.Net

type RequestParams = {
  AidColumnOption: string option
  Seed: int
  LowCountThreshold: float
  LowCountThresholdStdDev: float
}

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
  
  static member Encoder (queryResult: QueryResult) =
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
      ]
  
type QueryError =
  | ParseError of string
  | DbNotFound
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