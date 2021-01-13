namespace OpenDiffix.Core

module QueryEngine =
  open System.Data.SQLite
  open FsToolkit.ErrorHandling
  open OpenDiffix.Core
  open OpenDiffix.Core.AnonymizerTypes
  open OpenDiffix.Core.ParserTypes

  type DbConnection = SQLite.DbConnection

  let private getTables (connection: DbConnection) =
    asyncResult {
      let! tables = Table.getAll connection

      return
        {
          Columns = [ "name" ]
          Rows = tables |> List.map (fun table -> [ StringValue table.Name ])
        }
    }

  let private getColumnsFromTable (connection: DbConnection) (TableName tableName) =
    asyncResult {
      let! table = Table.getI connection tableName

      let rows =
        table.Columns
        |> List.map (fun column -> //
          [ StringValue column.Name; StringValue(Table.columnTypeToString column.Type) ]
        )

      return { Columns = [ "name"; "type" ]; Rows = rows }
    }


  let rec private expressionToSql =
    function
    | Constant (Integer value) -> string value
    | Constant (String value) -> sprintf "'%s'" value
    | Column (PlainColumn (ColumnName name)) -> name
    | Column (AliasedColumn (ColumnName columnName, _aliasName)) -> columnName
    | Function (functionName, subExpression) -> sprintf "%s(%s)" functionName (expressionToSql subExpression)
    | AggregateFunction (AnonymizedCount (Distinct (ColumnName name))) -> sprintf "count(distinct %s)" name

  let rec columnToSql index column = sprintf "%s as col%i" (expressionToSql column) index

  let private tableName =
    function
    | Table (TableName tableName) -> tableName

  let private generateSqlQuery aidColumnName (query: SelectQuery) =
    let aidColumn = Column(PlainColumn aidColumnName)

    let columns =
      aidColumn :: query.Expressions
      |> List.mapi columnToSql
      |> List.reduce (sprintf "%s, %s")

    $"""
    SELECT
      {columns}
    FROM {tableName query.From}
    """

  let private readValue index (reader: SQLiteDataReader) =
    if reader.IsDBNull index then
      NullValue
    else
      match reader.GetFieldType(index) with
      | fieldType when fieldType = typeof<int32> -> IntegerValue(reader.GetInt32 index)
      | fieldType when fieldType = typeof<int64> -> IntegerValue(int (reader.GetInt64 index))
      | fieldType when fieldType = typeof<string> -> StringValue(reader.GetString index)
      | unknownType -> StringValue(sprintf "Unknown type: %A" unknownType)

  let private readQueryResults (connection: DbConnection) aidColumnName (query: SelectQuery) =
    asyncResult {
      let tableName = tableName query.From
      let! table = Table.getI connection tableName

      let sqlQuery = generateSqlQuery aidColumnName query
      printfn "Using query:\n%s" sqlQuery
      use command = new SQLiteCommand(sqlQuery, connection)

      try
        let reader = command.ExecuteReader()

        return
          seq {
            while reader.Read() do
              let aidValue = readValue 0 reader
              let rowValues = [ 1 .. reader.FieldCount - 1 ] |> List.map (fun index -> readValue index reader)

              let row = { AidValues = Set.ofList [ aidValue ]; RowValues = rowValues }
              yield row
          }
      with ex -> return! Error("Execution error: " + ex.Message)
    }

  let private extractAidColumn anonymizationParams ({ From = Table (TableName tableName) }: SelectQuery) =
    match anonymizationParams.TableSettings.TryFind(tableName) with
    | None
    | Some { AidColumns = [] } -> Error "Execution error: An AID column name is required"
    | Some { AidColumns = [ column ] } -> Ok column
    | Some _ -> Error "Execution error: Multiple AID column names aren't supported yet"

  let private columnName =
    function
    | Constant (Integer value) -> string value
    | Constant (String value) -> value
    | Column (PlainColumn (ColumnName columnName))
    | Column (AliasedColumn (ColumnName columnName, _)) -> columnName
    | Function (functionName, _expression) -> functionName
    | AggregateFunction (AnonymizedCount (Distinct (ColumnName name))) -> "count"

  let private executeSelect (connection: DbConnection) anonymizationParams query =
    asyncResult {
      let! aidColumn = extractAidColumn anonymizationParams query

      let! rawRows =
        readQueryResults connection (OpenDiffix.Core.ParserTypes.ColumnName aidColumn) query
        |> AsyncResult.map Seq.toList

      let rows = Anonymizer.anonymize anonymizationParams rawRows
      let columns = query.Expressions |> List.map columnName

      return { Columns = columns; Rows = rows }
    }

  let private executeQuery reqParams queryAst =
    asyncResult {
      let! connection = SQLite.dbConnection reqParams.DatabasePath
      do! connection.OpenAsync() |> Async.AwaitTask

      let! result =
        match queryAst with
        | ParserTypes.ShowTables -> getTables connection
        | ParserTypes.ShowColumnsFromTable table -> getColumnsFromTable connection table
        | ParserTypes.SelectQuery query -> executeSelect connection reqParams.AnonymizationParams query
        | ParserTypes.AggregateQuery _query ->
            asyncResult { return! Error "Request error: Aggregate queries aren't supported yet" }

      do! connection.CloseAsync() |> Async.AwaitTask
      return result
    }

  let parseSql sqlQuery =
    match Parser.parse sqlQuery with
    | Ok ast -> Ok ast
    | Error (Parser.CouldNotParse error) -> Error("Parse error: " + error)

  let runQuery reqParams =
    asyncResult {
      let! queryAst = parseSql reqParams.Query
      return! executeQuery reqParams queryAst
    }
