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

let private getTables (connection: SQLiteConnection) =
  asyncResult {
    let! schema = dbSchema connection

    let rows = schema |> List.map (fun table -> [ StringValue table.Name ])

    let columns = [ "name" ]

    return { Columns = columns; Rows = rows }
  }

let private getColumnsFromTable (connection: SQLiteConnection) tableName =
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

let rec columnToSql index column = sprintf "%s as col%i" (expressionToSql column) index

let tableName (Table name) = name

let generateSqlQuery aidColumnName (query: SelectQuery) =
  let aidColumn = Identifier aidColumnName
  { query with Expressions = aidColumn :: query.Expressions }
  |> Expression.SelectQuery
  |> expressionToSql

let rec columnName =
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

    let (tableOption: DbTable option) = schema |> List.tryFind (fun table -> table.Name = columnName query.From)

    if tableOption.IsNone then
      return! Error(ExecutionError(sprintf "Unknown table %s" <| columnName query.From))
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

let executeShow (connection: SQLiteConnection) =
  function
  | ShowQuery.Tables -> getTables connection
  | ShowQuery.Columns tableName -> getColumnsFromTable connection tableName

let extractAidColumn anonymizationParams ({ From = tableName }: SelectQuery) =
  match anonymizationParams.TableSettings.TryFind(columnName tableName) with
  | None
  | Some { AidColumns = [] } -> Error(ExecutionError "An AID column name is required")
  | Some { AidColumns = [ column ] } -> Ok column
  | Some _ -> Error(ExecutionError "Multiple AID column names aren't supported yet")

let executeSelect (connection: SQLiteConnection) anonymizationParams query =
  asyncResult {
    let! aidColumn = extractAidColumn anonymizationParams query

    let! rawRows =
      readQueryResults connection aidColumn query
      |> AsyncResult.map Seq.toList

    let rows = Anonymizer.anonymize anonymizationParams rawRows
    let columns = query.Expressions |> List.map columnName

    return { Columns = columns; Rows = rows }
  }
