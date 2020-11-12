module DiffixEngine.DiffixSqlite

open Types
open System.Data.SQLite
open FsToolkit.ErrorHandling
open Dapper

type DbColumn = {
    Name: string
    ColumnType: string
}

type DbTable = {
    Name: string
    Columns: DbColumn list
}

let dbConnection path =
    try Ok (new SQLiteConnection(sprintf "Data Source=%s; Version=3; Read Only=true;" path))
    with exn -> Error DbNotFound
    
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
            let! resultRows = connection.QueryAsync<{| TableName: string; ColumnName: string; ColumnType: string |}>(sql) |> Async.AwaitTask
            return
                resultRows
                |> Seq.toList
                |> List.groupBy(fun row -> row.TableName)
                |> List.map(fun (tableName, rows) ->
                   {
                       Name = tableName
                       Columns =
                           rows
                           |> List.map(fun row -> {Name = row.ColumnName; ColumnType = row.ColumnType})
                   } 
                )
                |> List.sortBy(fun table -> table.Name)
        with
        | exn ->
            return! (Error (ExecutionError exn.Message))
    }

let getTables (connection: SQLiteConnection) =
    asyncResult {
        let! schema = dbSchema connection
        return
            schema
            |> List.map(fun table -> [ColumnCell ("name", StringValue table.Name)])
            |> ResultTable
    }
    
let getColumnsFromTable (connection: SQLiteConnection) tableName =
    asyncResult {
        let! schema = dbSchema connection
        return
            schema
            |> List.tryFind (fun table -> table.Name = tableName)
            |> function
                | None -> []
                | Some table ->
                    table.Columns
                    |> List.map(fun column ->
                        [
                            ColumnCell ("name", StringValue column.Name)
                            ColumnCell ("type", StringValue column.ColumnType)
                        ]
                    )
            |> ResultTable
    }
