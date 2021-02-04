namespace OpenDiffix.Core

type ColumnName = string
type Columns = ColumnName list

type QueryResult = { Columns: Columns; Rows: Row list }

type QueryError = string

module QueryEngine =
  open FsToolkit.ErrorHandling
  open OpenDiffix.Core
  open OpenDiffix.Core.AnalyzerTypes

  let rec private extractColumnNames =
    function
    | UnionQuery (_, query1, _query2) -> extractColumnNames query1
    | SelectQuery query ->
      query.Columns
      |> List.filter (fun column -> not <| Expression.IsJunk column.Expression)
      |> List.map (fun column -> column.Alias)

  let run connection statement anonParams =
    asyncResult {
      let! parsedQuery = Parser.parse statement

      let! query = Analyzer.analyze connection anonParams parsedQuery

      let plan = Planner.plan query

      let context = { AnonymizationParams = anonParams }
      let rows =
        plan
        |> Executor.execute connection context
        |> Seq.map Row.DropJunkValues
        |> Seq.toList

      let columns = extractColumnNames query

      return { Columns = columns; Rows = rows }
    }
