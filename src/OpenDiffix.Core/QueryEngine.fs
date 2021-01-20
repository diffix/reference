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

  let private getColumnsFromTable (connection: DbConnection) tableName =
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
    | Star -> "*"
    | Null -> "null"
    | Integer value -> $"%i{value}"
    | Float value -> $"%f{value}"
    | String value -> value
    | Boolean value -> $"%b{value}"
    | Distinct expression -> $"DISTINCT %s{expressionToSql expression}"
    | Not expression -> $"NOT %s{expressionToSql expression}"
    | And (left, right) -> $"%s{expressionToSql left} AND %s{expressionToSql right}"
    | Or (left, right) -> $"%s{expressionToSql left} OR %s{expressionToSql right}"
    | Lt (left, right) -> $"%s{expressionToSql left} < %s{expressionToSql right}"
    | LtE (left, right) -> $"%s{expressionToSql left} <= %s{expressionToSql right}"
    | Gt (left, right) -> $"%s{expressionToSql left} > %s{expressionToSql right}"
    | GtE (left, right) -> $"%s{expressionToSql left} >= %s{expressionToSql right}"
    | Equal (left, right) -> $"%s{expressionToSql left} = %s{expressionToSql right}"
    | As (expr, alias) -> $"%s{expressionToSql expr} AS %s{expressionToSql alias}"
    | Identifier name -> name
    | Function (functionName, subExpressions) ->
      let functionArgs =
        subExpressions
        |> List.map expressionToSql
        |> List.reduceBack (sprintf "%s %s")
      sprintf "%s(%s)" functionName  functionArgs
    | ShowQuery _ -> failwith "SHOW-queries are not supported"
    | SelectQuery queryExpr ->
      let distinct = if queryExpr.SelectDistinct then "DISTINCT" else ""
      let columnExpr =
        queryExpr.Expressions
        |> List.map expressionToSql
        |> List.reduceBack (sprintf "%s, %s")
      let where =
        queryExpr.Where
        |> Option.map (fun expr -> $"FROM %s{expressionToSql expr}")
        |> Option.defaultValue ""
      let groupBy =
        queryExpr.GroupBy
        |> List.map expressionToSql
        |> function
          | [] -> ""
          | groupings ->
            let groupByTerms = groupings |> List.reduceBack (sprintf "%s, %s")
            $"GROUP BY %s{groupByTerms}"
      $"
      SELECT %s{distinct} %s{columnExpr}
      FROM %s{expressionToSql queryExpr.From}
      %s{where}
      %s{groupBy}
      "

  let getIdentifier =
    function
    | Identifier identifier -> Ok identifier
    | _ -> Error "Expected an identifier, got something else"

  let rec columnToSql index column = sprintf "%s as col%i" (expressionToSql column) index

  let private generateSqlQuery tableName aidColumnName (query: SelectQuery) =
    let columns =
      (Identifier aidColumnName) :: query.Expressions
      |> List.mapi columnToSql
      |> List.reduce (sprintf "%s, %s")

    $"""
    SELECT
      {columns}
    FROM %s{tableName}
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
      let! tableName = getIdentifier query.From
      let! table = Table.getI connection tableName

      let sqlQuery = generateSqlQuery table.Name aidColumnName query
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

  let private extractAidColumn anonymizationParams ({ From = from }: SelectQuery) =
    result {
      let! tableName = getIdentifier from
      match anonymizationParams.TableSettings.TryFind(tableName) with
      | None
      | Some { AidColumns = [] } -> return! (Error "Execution error: An AID column name is required")
      | Some { AidColumns = [ column ] } -> return column
      | Some _ -> return! (Error "Execution error: Multiple AID column names aren't supported yet")
    }

  let rec private columnName =
    function
    | Star -> "*"
    | Null -> "null"
    | Distinct expr -> columnName expr
    | Integer value -> $"%i{value}"
    | Float value -> $"%f{value}"
    | String value -> value
    | Boolean value -> $"%b{value}"
    | And _ -> "and"
    | Or _ -> "or"
    | Not expr -> columnName expr
    | Lt _ -> "<"
    | LtE _ -> "<="
    | Gt _ -> ">"
    | GtE _ -> ">="
    | Equal _ -> "="
    | Identifier expr -> expr
    | As (_term, name) -> columnName name
    | Function (functionName, _expression) -> functionName
    | ShowQuery _
    | SelectQuery _ -> failwith "Not a valid term for selection"

  let executeShow (connection: SQLiteConnection) =
    function
    | ShowQueryKinds.Tables -> getTables connection
    | ShowQueryKinds.Columns tableName -> getColumnsFromTable connection tableName

  let private executeSelect (connection: DbConnection) anonymizationParams query =
    asyncResult {
      let! aidColumn = extractAidColumn anonymizationParams query

      let! rawRows =
        readQueryResults connection aidColumn query
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
        | ShowQuery query -> executeShow connection query
        | SelectQuery query -> executeSelect connection reqParams.AnonymizationParams query
        | _ -> AsyncResult.returnError "Expecting an SQL query to run"

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
