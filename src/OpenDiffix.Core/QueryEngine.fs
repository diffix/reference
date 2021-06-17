﻿module OpenDiffix.Core.QueryEngine

open AnalyzerTypes

let rec private extractColumns query =
  match query with
  | UnionQuery (_, query1, _query2) -> extractColumns query1
  | SelectQuery query ->
      query.TargetList
      |> List.filter TargetEntry.isRegular
      |> List.map (fun column -> //
        { Name = column.Alias; Type = Expression.typeOf (column.Expression) }
      )

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type QueryResult = { Columns: Column list; Rows: Row list }

type QueryError = string

let run context statement : Result<QueryResult, QueryError> =
  try
    let query =
      statement
      |> Parser.parse
      |> Analyzer.analyze context
      |> Normalizer.normalize
      |> Analyzer.rewrite context

    let rows = query |> Planner.plan |> Executor.execute context |> Seq.toList
    let columns = extractColumns query
    Ok { Columns = columns; Rows = rows }
  with ex -> Error ex.Message
