module OpenDiffix.Core.QueryEngine

open AnalyzerTypes

let rec private extractColumns query =
  query.TargetList
  |> List.filter TargetEntry.isRegular
  |> List.map (fun column -> //
    { Name = column.Alias; Type = Expression.typeOf column.Expression }
  )

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type QueryHooks =
  {
    ExecutorHook: ExecutorHook option
  }
  static member Default = { ExecutorHook = None }

type QueryResult = { Columns: Column list; Rows: Row list }

let run hooks queryContext statement : QueryResult =
  let query, noiseLayers =
    statement
    |> Parser.parse
    |> Analyzer.analyze queryContext
    |> Normalizer.normalize
    |> Analyzer.anonymize queryContext

  let executionContext =
    {
      QueryContext = queryContext
      NoiseLayers = noiseLayers
      ExecutorHook = hooks.ExecutorHook
    }

  let rows = query |> Planner.plan |> Executor.execute executionContext |> Seq.toList
  let columns = extractColumns query
  { Columns = columns; Rows = rows }
