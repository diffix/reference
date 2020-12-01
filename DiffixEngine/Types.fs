module DiffixEngine.Types

open Thoth.Json.Net

type ColumnName = string

type ColumnValue =
  | IntegerValue of int
  | StringValue of string
  
  static member Encoder (columnValue: ColumnValue) =
    match columnValue with
    | IntegerValue intValue -> Encode.int intValue
    | StringValue strValue -> Encode.string strValue
  
type ColumnCell =
  | ColumnCell of (ColumnName * ColumnValue) 
  
type Row = ColumnCell list
  
type QueryResult =
  | ParseError of string
  | DbNotFound
  | ExecutionError of string
  | UnexpectedError of string
  | ResultTable of Row list
  
  static member Encoder (queryResult: QueryResult) =
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
    | ResultTable rows ->
      let columnNames =
        match List.tryHead rows with
        | Some columnCells ->
          columnCells
          |> List.map (fun (ColumnCell (columnName, _)) -> columnName)
          |> List.map Encode.string
        | None -> []
      
      let values =
        rows
        |> List.map(fun row ->
          List.map(fun (ColumnCell (_name, value)) -> ColumnValue.Encoder value) row
          |> Encode.list
        )
        |> Encode.list
      
      Encode.object [
        "success", Encode.bool true
        "column_names", Encode.list columnNames
        "values", values
      ]