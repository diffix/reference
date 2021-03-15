namespace OpenDiffix.Core

type Column = { Name: string; Type: ValueType }

type Table = { Name: string; Columns: Column list }

type Schema = Table list

type IDataProvider =
  abstract LoadData: table:Table -> Async<Result<Row seq, string>>
  abstract GetSchema: unit -> Async<Result<Schema, string>>

module Table =
  open FsToolkit.ErrorHandling
  open OpenDiffix.Core.Utils

  let getI schema tableName =
    schema
    |> List.tryFind (fun table -> equalsI table.Name tableName)
    |> Result.requireSome "Execution error: Table not found"

  let tryGetColumnI table columnName =
    table.Columns
    |> List.indexed
    |> List.tryFind (fun (_index, column) -> equalsI column.Name columnName)

  let getColumnI table columnName =
    columnName
    |> tryGetColumnI table
    |> Result.requireSome $"Unknown column %s{columnName} in table %s{table.Name}"
