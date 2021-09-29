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

let run evaluationContext statement : QueryResult =
  let query, executionContext =
    statement
    |> Parser.parse
    |> Analyzer.analyze evaluationContext
    |> Normalizer.normalize
    |> Analyzer.anonymize evaluationContext

  let rows = query |> Planner.plan |> Executor.execute executionContext |> Seq.toList
  let columns = extractColumns query
  { Columns = columns; Rows = rows }
