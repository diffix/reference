module OpenDiffix.Core.QueryEngine

open AnalyzerTypes

let rec private extractColumns query =
  query.TargetList
  |> List.filter TargetEntry.isRegular
  |> List.map (fun column -> //
    { Name = column.Alias; Type = Expression.typeOf (column.Expression) }
  )

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type QueryResult = { Columns: Column list; Rows: Row list }

let run context statement : QueryResult =
  let query =
    statement
    |> Parser.parse
    |> Analyzer.analyze context
    |> Normalizer.normalize
    |> Analyzer.rewrite context

  let rows = query |> Planner.plan |> Executor.execute context |> Seq.toList
  let columns = extractColumns query
  { Columns = columns; Rows = rows }
