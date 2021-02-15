namespace OpenDiffix.Core

type Column = { Name: string; Type: ValueType }

type Table = { Name: string; Columns: Column list }

module Table =
  open FsToolkit.ErrorHandling
  open OpenDiffix.Core.Utils

  let private columnTypeFromString =
    function
    | "integer" -> IntegerType
    | "text" -> StringType
    | "boolean" -> BooleanType
    | "real" -> RealType
    | other -> UnknownType other

  let columnTypeToString =
    function
    | IntegerType -> "integer"
    | StringType -> "string"
    | BooleanType -> "boolean"
    | RealType -> "real"
    | UnknownType typeName -> $"unknown ({typeName})"

  let getAll (connection: SQLite.DbConnection) =
    asyncResult {
      let! schema = SQLite.dbSchema connection

      return
        schema
        |> List.map (fun table ->
          let columns =
            table.Columns
            |> List.map (fun column -> { Name = column.Name; Type = columnTypeFromString (column.Type) })

          { Name = table.Name; Columns = columns }
        )
    }

  let getI connection tableName =
    asyncResult {
      let! tables = getAll connection

      return!
        tables
        |> List.tryFind (fun table -> equalsI table.Name tableName)
        |> Result.requireSome "Execution error: Table not found"
    }

  let getColumn table columnName =
    table.Columns
    |> List.indexed
    |> List.tryFind (fun (_index, column) -> equalsI column.Name columnName)
    |> Result.requireSome $"Unknown column %s{columnName} in table %s{table.Name}"

  let load connection table =
    let columns =
      table.Columns
      |> List.map (fun column -> $"\"%s{column.Name}\"")
      |> List.reduce (sprintf "%s, %s")

    let loadQuery = $"SELECT {columns} FROM {table.Name}"

    SQLite.executeQuery connection loadQuery
