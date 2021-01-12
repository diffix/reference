namespace OpenDiffix.Core

module QueryEngine =
  open FsToolkit.ErrorHandling
  open OpenDiffix.Core
  open OpenDiffix.Core.AnonymizerTypes

  let private executeQuery reqParams queryAst =
    asyncResult {
      let! connection = DiffixSqlite.dbConnection reqParams.DatabasePath
      do! connection.OpenAsync() |> Async.AwaitTask

      let! result =
        match queryAst with
        | ParserTypes.Show query -> DiffixSqlite.executeShow connection query
        | ParserTypes.SelectQuery query -> DiffixSqlite.executeSelect connection reqParams.AnonymizationParams query

      do! connection.CloseAsync() |> Async.AwaitTask
      return result
    }

  let parseSql sqlQuery =
    match Parser.parse sqlQuery with
    | Ok ast -> Ok ast
    | Error (Parser.CouldNotParse error) -> Error(ParseError error)

  let runQuery reqParams =
    asyncResult {
      let! queryAst = parseSql reqParams.Query
      return! executeQuery reqParams queryAst
    }
