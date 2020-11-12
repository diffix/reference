module DiffixEngine.Types

type ColumnName = string

type ColumnValue =
  | IntegerValue of int
  | StringValue of string
  
type ColumnCell =
  | ColumnCell of (ColumnName * ColumnValue) 
  
type Row = ColumnCell list
  
type QueryResult =
  | ParseError of string
  | DbNotFound
  | ExecutionError of string
  | ResultTable of Row list
  | UnexpectedError of string