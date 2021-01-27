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
    | SelectQuery query -> query.Columns |> List.map (fun column -> column.Alias)

  let run connection statement anonParams =
    asyncResult {
      let! parsedQuery = Parser.parse statement

      let! query = Analyzer.analyze connection anonParams parsedQuery

      let plan = Planner.plan query

      let rows = plan |> Executor.execute connection |> Seq.toList

      let columns = extractColumns query

      return { Columns = columns; Rows = rows }
    }
