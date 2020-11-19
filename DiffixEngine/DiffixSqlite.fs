module DiffixEngine.DiffixSqlite

open SqlParser.Query
open Types
open System.Data.SQLite
open FsToolkit.ErrorHandling
open Dapper

type DbColumnType =
    | DbInteger
    | DbString
    | DbUnknownType of string
    
type DbColumn = {
    Name: string
    ColumnType: DbColumnType
}

type DbTable = {
    Name: string
    Columns: DbColumn list
}

let dbConnection path =
    try Ok (new SQLiteConnection(sprintf "Data Source=%s; Version=3; Read Only=true;" path))
    with exn -> Error DbNotFound
    
let columnTypeFromString =
    function
    | "INTEGER" -> DbInteger
    | "TEXT" -> DbString
    | other -> DbUnknownType other

let columnTypeToString =
    function
    | DbInteger -> "integer"
    | DbString -> "string"
    | DbUnknownType typeName -> typeName + " (not yet supported)"
    
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
                           |> List.map(fun row -> {Name = row.ColumnName; ColumnType = columnTypeFromString row.ColumnType})
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
                            ColumnCell ("type", StringValue (columnTypeToString column.ColumnType))
                        ]
                    )
            |> ResultTable
    }

let columnToSql index column =
    let columnSql = 
        match column with
        | Constant (Integer value)  -> string value
        | Constant (String value)  -> sprintf "'%s'" value
        | Column (PlainColumn name) -> name
        | Column (AliasedColumn (columnName, _aliasName)) -> columnName
    sprintf "%s as col%i" columnSql index

let tableName =
    function
    | Table tableName -> tableName
    
let generateSqlQuery (query: SelectQuery) =
    let columns =
        query.Expressions
        |> List.mapi columnToSql
        |> List.reduce(fun a b -> sprintf "%s, %s" a b)
    let from = tableName query.From
    sprintf """
    SELECT
        %s
    FROM %s
    """ columns from

let readQueryResults connection (query: SelectQuery) =
    asyncResult {
        let! schema = dbSchema connection
        let desiredTableName = tableName query.From
        match schema |> List.tryFind (fun table -> table.Name = desiredTableName) with
        | None -> return! Error (ExecutionError (sprintf "Unknown table %s" desiredTableName))
        | Some table ->
            let typeByColumnName =
                table.Columns
                |> List.map(fun column -> column.Name, column.ColumnType)
                |> Map.ofList
                
            use command = new SQLiteCommand(generateSqlQuery query, connection)
            
            let columnConverter =
                query.Expressions
                |> List.mapi(fun index expression (reader: SQLiteDataReader) ->
                    match expression with
                    | Constant (Integer value) ->
                        ColumnCell (string value, ColumnValue.IntegerValue value)
                    | Constant (String value) ->
                        ColumnCell (value, ColumnValue.StringValue value)
                    | Column (PlainColumn columnName) 
                    | Column (AliasedColumn (columnName, _)) ->
                        let value =
                            match Map.find columnName typeByColumnName with
                            | DbInteger -> ColumnValue.IntegerValue(reader.GetInt32 index)
                            | DbString -> ColumnValue.StringValue(reader.GetString index)
                            | DbUnknownType _ -> ColumnValue.StringValue "Unknown type"
                        ColumnCell (columnName, value)
                )
            let reader = command.ExecuteReader()
            return
                seq {
                    while reader.Read() do
                        let row: ColumnCell list =
                            columnConverter
                            |> List.map(fun c -> c reader)
                        yield row
                }
    }
            
let executeSelect (connection: SQLiteConnection) query =
    asyncResult {
        let! rowSequence = readQueryResults connection query
        return ResultTable (Seq.toList rowSequence)
    }