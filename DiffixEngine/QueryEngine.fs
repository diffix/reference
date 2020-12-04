namespace DiffixEngine

module QueryEngine =
    open FsToolkit.ErrorHandling
    open SqlParser
    open Types
    
    let private executeQuery reqParams queryAst =
        asyncResult {
            let! connection = DiffixSqlite.dbConnection reqParams.DatabasePath
            do! connection.OpenAsync() |> Async.AwaitTask
            let! result =
                match queryAst with
                | Query.ShowTables -> DiffixSqlite.getTables connection
                | Query.ShowColumnsFromTable table -> DiffixSqlite.getColumnsFromTable connection table
                | Query.SelectQuery query -> DiffixSqlite.executeSelect connection reqParams query
            do! connection.CloseAsync() |> Async.AwaitTask
            return result
        }
        
    let parseSql sqlQuery =
        match Parser.parseSql sqlQuery with
        | Ok ast -> Ok ast
        | Error (Parser.CouldNotParse error) -> Error (ParseError error)
        
    let runQuery reqParams =
        asyncResult {
            let! queryAst = parseSql reqParams.Query
            return! executeQuery reqParams queryAst
        }