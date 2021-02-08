namespace OpenDiffix.Core

type ColumnName = string
type Columns = ColumnName list

type QueryResult = { Columns: Columns; Rows: Row list }

type QueryError = string

module QueryEngine =
  open FsToolkit.ErrorHandling
  open OpenDiffix.Core
  open OpenDiffix.Core.AnalyzerTypes

  let rec private extractColumns =
    function
    | UnionQuery (_, query1, _query2) -> extractColumns query1
    | SelectQuery query -> query.Columns

  let rec private extractColumnNames columns =
    columns
    |> List.filter (fun column -> not column.Junk)
    |> List.map (fun column -> column.Alias)

  let removeJunkColumns columns (row: Row): Row =
    row
    |> Array.zip columns
    |> Array.filter (fun (selectExpression, _value) -> not selectExpression.Junk)
    |> Array.map snd

  let run connection statement anonParams =
    asyncResult {
      let! parsedQuery = Parser.parse statement

      let! query = Analyzer.analyze connection anonParams parsedQuery
      let columns = extractColumns query

      let plan = Planner.plan query

      let context = { AnonymizationParams = anonParams }

      let rows =
        plan
        |> Executor.execute connection context
        |> Seq.map (removeJunkColumns (Array.ofList columns))
        |> Seq.toList

      let columns = extractColumnNames columns

      return { Columns = columns; Rows = rows }
    }
