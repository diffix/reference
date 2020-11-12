namespace DiffixEngine

module QueryEngine =
    open FsToolkit.ErrorHandling
    open SqlParser
    open Types
    
    let private executeQuery databasePath queryAst =
        asyncResult {
            let! connection = DiffixSqlite.dbConnection databasePath
            match queryAst with
            | Query.ShowTables -> return! DiffixSqlite.getTables connection
            | Query.ShowColumnsFromTable table -> return! DiffixSqlite.getColumnsFromTable connection table
            | Query.SelectQuery _ -> return (ExecutionError "SELECT queries are not yet supported")
        }
        
    let parseSql sqlQuery =
        match Parser.parseSql sqlQuery with
        | Ok ast -> Ok ast
        | Error (Parser.CouldNotParse error) -> Error (ParseError error)
        
    let runQuery databasePath sqlQuery =
        asyncResult {
            let! queryAst = parseSql sqlQuery
            return! executeQuery databasePath queryAst
        }
        |> Async.map(
            function
            | Ok result -> result
            | Error error -> error
        )