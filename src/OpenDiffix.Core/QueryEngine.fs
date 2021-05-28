module OpenDiffix.Core.QueryEngine

open AnalyzerTypes

type ColumnName = string

type Columns = ColumnName list

let rec private extractColumnNames query =
  match query with
  | UnionQuery (_, query1, _query2) -> extractColumnNames query1
  | SelectQuery query ->
      query.TargetList
      |> List.filter TargetEntry.isRegular
      |> List.map (fun column -> column.Alias)

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type QueryResult = { Columns: Columns; Rows: Row list }

type QueryError = string

let run context parsedQuery : Result<QueryResult, QueryError> =
  try
    let query =
      parsedQuery
      |> Analyzer.analyze context
      |> Normalizer.normalize
      |> Analyzer.rewrite context

    let rows = query |> Planner.plan |> Executor.execute context |> Seq.toList
    let columns = extractColumnNames query
    Ok { Columns = columns; Rows = rows }
  with ex -> Error ex.Message
