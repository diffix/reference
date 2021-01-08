module OpenDiffix.Core.DiffixSqlite

open System
open System.Data.SQLite
open Dapper
open FsToolkit.ErrorHandling
open OpenDiffix.Core.ParserTypes
open OpenDiffix.Core.AnonymizerTypes

type DbColumnType =
  | DbInteger
  | DbString
  | DbUnknownType of string

type DbColumn = { Name: string; ColumnType: DbColumnType }

type DbTable = { Name: string; Columns: DbColumn list }

let dbConnection path =
  try
    Ok(new SQLiteConnection(sprintf "Data Source=%s; Version=3; Read Only=true;" path))
  with exn -> Error DbNotFound

let columnTypeFromString =
  function
  | "integer" -> DbInteger
  | "text" -> DbString
  | other -> DbUnknownType other

let columnTypeToString =
  function
  | DbInteger -> "integer"
  | DbString -> "string"
  | DbUnknownType typeName -> typeName + " (not yet supported)"

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
              |> List.map (fun row ->
                {
                  Name = row.ColumnName
                  ColumnType = columnTypeFromString (row.ColumnType.ToLower())
                }
              )
          }
        )
        |> List.sortBy (fun table -> table.Name)
    with exn ->
      printfn "Exception: %A" exn
      return! (Error(ExecutionError exn.Message))
  }

let getTables (connection: SQLiteConnection) =
  asyncResult {
    let! schema = dbSchema connection

    let rows = schema |> List.map (fun table -> [ StringValue table.Name ])

    let columns = [ "name" ]

    return { Columns = columns; Rows = rows }
  }

let getColumnsFromTable (connection: SQLiteConnection) (TableName tableName) =
  asyncResult {
    let! schema = dbSchema connection
    let columns = [ "name"; "type" ]

    let rows =
      schema
      |> List.tryFind (fun table -> table.Name = tableName)
      |> function
      | None -> []
      | Some table ->
          table.Columns
          |> List.map (fun column -> //
            [ StringValue column.Name; StringValue(columnTypeToString column.ColumnType) ]
          )

    return { Columns = columns; Rows = rows }
  }

let rec expressionToSql =
  function
  | Constant (Integer value) -> string value
  | Constant (String value) -> sprintf "'%s'" value
  | Column (PlainColumn (ColumnName name)) -> name
  | Column (AliasedColumn (ColumnName columnName, _aliasName)) -> columnName
  | Function (functionName, subExpression) -> sprintf "%s(%s)" functionName (expressionToSql subExpression)
  | AggregateFunction (AnonymizedCount (Distinct (ColumnName name))) -> sprintf "count(distinct %s)" name

let rec columnToSql index column = sprintf "%s as col%i" (expressionToSql column) index

let tableName =
  function
  | Table (TableName tableName) -> tableName

let generateSqlQuery aidColumnName (query: SelectQuery) =
  let aidColumn = Column(PlainColumn aidColumnName)

  let columns =
    aidColumn :: query.Expressions
    |> List.mapi columnToSql
    |> List.reduce (sprintf "%s, %s")

  let from = tableName query.From

  sprintf
    """
  SELECT
    %s
  FROM %s
  """
    columns
    from

let columnName =
  function
  | Constant (Integer value) -> string value
  | Constant (String value) -> value
  | Column (PlainColumn (ColumnName columnName))
  | Column (AliasedColumn (ColumnName columnName, _)) -> columnName
  | Function (functionName, _expression) -> functionName
  | AggregateFunction (AnonymizedCount (Distinct (ColumnName name))) -> "count"

let readValue index (reader: SQLiteDataReader) =
  if reader.IsDBNull index then
    NullValue
  else
    match reader.GetFieldType(index) with
    | fieldType when fieldType = typeof<Int32> -> IntegerValue(reader.GetInt32 index)
    | fieldType when fieldType = typeof<Int64> -> IntegerValue(int (reader.GetInt64 index))
    | fieldType when fieldType = typeof<String> -> StringValue(reader.GetString index)
    | unknownType -> StringValue(sprintf "Unknown type: %A" unknownType)


let readQueryResults connection aidColumnName (query: SelectQuery) =
  asyncResult {
    let! schema = dbSchema connection
    let desiredTableName = tableName query.From

    let (tableOption: DbTable option) = schema |> List.tryFind (fun table -> table.Name = desiredTableName)

    if tableOption.IsNone then
      return! Error(ExecutionError(sprintf "Unknown table %s" desiredTableName))
    else
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
      with exn -> return! Error(ExecutionError exn.Message)
  }

let extractAidColumns anonymizationParams ({ From = Table (TableName tableName) }: SelectQuery) =
  match anonymizationParams.TableSettings.TryFind(tableName) with
  | None -> []
  | Some tableSettings -> tableSettings.AidColumns

let executeSelect (connection: SQLiteConnection) anonymizationParams query =
  asyncResult {
    match extractAidColumns anonymizationParams query with
    | [] -> return! Error(ExecutionError "An AID column name is required")
    | [ aidColumn ] ->
        let! rawRows =
          readQueryResults connection (OpenDiffix.Core.ParserTypes.ColumnName aidColumn) query
          |> AsyncResult.map Seq.toList

        let rows = Anonymizer.anonymize anonymizationParams rawRows
        let columns = query.Expressions |> List.map columnName

        return { Columns = columns; Rows = rows }
    | _ -> return! Error(ExecutionError "Multiple AID column names aren't supported yet")
  }
