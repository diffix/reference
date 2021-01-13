module OpenDiffix.Core.SQLite

open System
open System.Data.SQLite
open Dapper
open FsToolkit.ErrorHandling

type DbConnection = SQLiteConnection
type DbColumn = { Name: string; Type: string }
type DbTable = { Name: string; Columns: DbColumn list }

let dbConnection path =
  try
    Ok(new SQLiteConnection(sprintf "Data Source=%s; Version=3; Read Only=true;" path))
  with ex -> Error("Connect error: " + ex.Message)

[<CLIMutable>]
type private DbSchemaQueryRow = { TableName: string; ColumnName: string; ColumnType: string }

let dbSchema (connection: SQLiteConnection) =
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
    WHERE tableName NOT LIKE 'sqlite%'
    ORDER by tableName, columnName
    """

    try
      let! resultRows = connection.QueryAsync<DbSchemaQueryRow>(sql) |> Async.AwaitTask

      return
        resultRows
        |> Seq.toList
        |> List.groupBy (fun row -> row.TableName)
        |> List.map (fun (tableName, rows) ->
          {
            Name = tableName
            Columns =
              rows
              |> List.map (fun row -> { Name = row.ColumnName; Type = row.ColumnType.ToLower() })
          }
        )
        |> List.sortBy (fun table -> table.Name)
    with ex ->
      printfn "Exception: %A" ex
      return! Error("Execution error: " + ex.Message)
  }
