namespace OpenDiffix.Core

type Column = { Name: string; Type: ValueType }

type Table = { Name: string; Columns: Column list }

type IDataProvider =
  abstract LoadData: table:Table -> Async<Result<Row seq, string>>
  abstract GetSchema: unit -> Async<Result<Table list, string>>

module Table =
  open FsToolkit.ErrorHandling
  open OpenDiffix.Core.Utils

  let getI (dataProvider: IDataProvider) tableName =
    asyncResult {
      let! tables = dataProvider.GetSchema()

      return!
        tables
        |> List.tryFind (fun table -> equalsI table.Name tableName)
        |> Result.requireSome "Execution error: Table not found"
    }

  let getColumn table columnName =
    table.Columns
    |> List.indexed
    |> List.tryFind (fun (_index, column) -> equalsI column.Name columnName)
    |> Result.requireSome $"Unknown column %s{columnName} in table %s{table.Name}"

  let load (dataProvider: IDataProvider) table = dataProvider.LoadData table
