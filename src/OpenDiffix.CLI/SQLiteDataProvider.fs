module OpenDiffix.CLI.SQLite

open System
open FsToolkit.ErrorHandling
open OpenDiffix.Core
open System.Data.SQLite
open Dapper

type DbConnection = SQLiteConnection

let private dbConnection path =
  try
    Ok(new SQLiteConnection(sprintf "Data Source=%s; Version=3; Read Only=true;" path))
  with ex -> Error("Connect error: " + ex.Message)

[<CLIMutable>]
type private DbSchemaQueryRow = { TableName: string; ColumnName: string; ColumnType: string }

let private dbSchema (connection: SQLiteConnection) =
  asyncResult {
    // Note: somewhat counterintuitively the order in which the columns are selected matter here.
    // The reason is that we are using an anonymous record to deserialize the rows from the database.
    // Anonymous records have a constructor where the parameters are sorted alphabetically by name.
    // The order in which the columns are returned from the DB need to match that.
    let sql = """
    SELECT p.name as ColumnName,
           p.type as ColumnType,
           m.name as TableName
    FROM sqlite_master m
         left outer join pragma_table_info((m.name)) p
             on m.name <> p.name
    WHERE tableName NOT LIKE 'sqlite%' and m.type = 'table'
    ORDER by tableName, columnName
    """

    try
      let! resultRows = connection.QueryAsync<DbSchemaQueryRow>(sql) |> Async.AwaitTask

      return
        resultRows
        |> Seq.toList
        |> List.groupBy (fun row -> row.TableName)
        |> List.map (fun (tableName, rows) ->
          {|
            Name = tableName
            Columns =
              rows
              |> List.map (fun row -> {| Name = row.ColumnName; Type = row.ColumnType.ToLower() |})
          |}
        )
        |> List.sortBy (fun table -> table.Name)
    with ex ->
      printfn $"Exception: %A{ex}"
      return! Error("Execution error: " + ex.Message)
  }

let private readValue (reader: SQLiteDataReader) index =
  if reader.IsDBNull index then
    Null
  else
    match reader.GetFieldType(index) with
    | fieldType when fieldType = typeof<bool> -> Boolean(reader.GetBoolean index)
    | fieldType when fieldType = typeof<int32> || fieldType = typeof<int64> ->
        Integer(reader.GetFieldValue<int64> index)
    | fieldType when fieldType = typeof<single> || fieldType = typeof<double> ->
        Real(reader.GetFieldValue<double> index)
    | fieldType when fieldType = typeof<string> -> String(reader.GetString index)
    | _unknownType -> Null

let private executeQuery (connection: SQLiteConnection) (query: string) =
  asyncResult {
    use command = new SQLiteCommand(query, connection)

    try
      let reader = command.ExecuteReader()

      return
        seq<Row> {
          while reader.Read() do
            yield [| 0 .. reader.FieldCount - 1 |] |> Array.map (readValue reader)
        }
    with ex -> return! Error("Execution error: " + ex.Message)
  }

let private columnTypeFromString =
  function
  | "integer" -> IntegerType
  | "text" -> StringType
  | "boolean" -> BooleanType
  | "real" -> RealType
  | other -> UnknownType other

let rec columnTypeToString =
  function
  | IntegerType -> "integer"
  | StringType -> "string"
  | BooleanType -> "boolean"
  | RealType -> "real"
  | ArrayType elementType -> $"array of %s{columnTypeToString elementType}"
  | UnknownType typeName -> $"unknown ({typeName})"

type DataProvider(dbPath: string) =
  let connection =
    let connection = dbPath |> dbConnection |> Utils.unwrap
    connection.Open()
    connection

  interface IDisposable with
    member this.Dispose() = connection.Close()

  interface IDataProvider with
    member this.GetSchema() =
      asyncResult {
        let! schema = dbSchema connection

        return
          schema
          |> List.map (fun table ->
            let columns =
              table.Columns
              |> List.map (fun column -> { Name = column.Name; Type = columnTypeFromString (column.Type) })

            { Name = table.Name; Columns = columns }
          )
      }

    member this.LoadData(table) =
      let columns =
        table.Columns
        |> List.map (fun column -> $"\"%s{column.Name}\"")
        |> List.reduce (sprintf "%s, %s")

      let loadQuery = $"SELECT {columns} FROM {table.Name}"

      executeQuery connection loadQuery
