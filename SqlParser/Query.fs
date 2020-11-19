namespace SqlParser

module Query =
  type Constant =
    | Integer of int
    | String of string
    
  type ColumnName = string
  type TableName = string

  type ColumnType =
    | PlainColumn of ColumnName 
    | AliasedColumn of ColumnName * string
    
  type Expression =
    | Constant of Constant
    | Column of ColumnType
    | Function of (string * Expression)
    
  type From =
    | Table of TableName
    
  type SelectQuery = {
    Expressions: Expression list
    From: From
  }

  type Query =
    // Exploration queries
    | ShowTables 
    | ShowColumnsFromTable of TableName
    // Data extraction
    | SelectQuery of SelectQuery
