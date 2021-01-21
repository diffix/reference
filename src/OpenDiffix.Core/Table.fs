namespace OpenDiffix.Core

type ColumnType =
  | BooleanType
  | IntegerType
  | FloatType
  | StringType
  | UnknownType of string

type Column = { Name: string; Type: ColumnType; Index: int }

type Table = { Name: string; Columns: Column list }

module Table =
  open FsToolkit.ErrorHandling
  open OpenDiffix.Core.Utils

  let private columnTypeFromString =
    function
    | "integer" -> IntegerType
    | "text" -> StringType
    | other -> UnknownType other

  let columnTypeToString =
    function
    | IntegerType -> "integer"
    | StringType -> "string"
    | BooleanType -> "boolean"
    | FloatType -> "float"
    | UnknownType typeName -> $"unknown ({typeName})"

  let getAll (connection: SQLite.DbConnection) =
    asyncResult {
      let! schema = SQLite.dbSchema connection

      return
        schema
        |> List.map (fun table ->
             let columns =
               table.Columns
               |> List.zip [ 0 .. (List.length (table.Columns) - 1) ]
               |> List.map (fun (index, column) ->
                    { Name = column.Name; Type = columnTypeFromString (column.Type); Index = index })

             { Name = table.Name; Columns = columns })
    }

  let getI connection tableName =
    asyncResult {
      let! tables = getAll connection

      return!
        tables
        |> List.tryFind (fun table -> equalsI table.Name tableName)
        |> Result.requireSome "Execution error: Table not found"
    }

  let tryFindColumn table columnName = table.Columns |> List.tryFind (fun column -> equalsI column.Name columnName)

  let getColumn table columnName =
    tryFindColumn table columnName
    |> Result.requireSome $"Unknown column %s{columnName} in table %s{table.Name}"
