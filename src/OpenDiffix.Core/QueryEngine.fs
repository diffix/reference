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

type QueryResult = { Columns: Column list; Rows: Row list }

// The main entrypoint to the `OpenDiffix.Core` library.
//
// Takes in a SQL `statement` as main parameter along with a set of parameters in `queryContext`. Executes the query
// against the `IDataProvider` provided in the `queryContext` and returns a structure representing the result table. The
// data in the result table will be anonymized (or will contain facilities necessary to anonymize), according to the
// modes of operation listed below.
//
// Supports two main modes of operation:
// 1. (anonymizing query) Standard SQL statement, `AnonymizationParams.AccessLevel = PublishTrusted / PublishUntrusted`
//
// Rewrites the statement, so that the query result is anonymized according to the `aidColumns` provided. For example,
// it appends `AND NOT diffix_low_count(aidColumns)` to the `HAVING` SQL clause and rewrites regular SQL aggregators to
// Diffix aggregators (e.g. `count` -> `diffix_count`, see below).
//
// 2. (direct query) SQL statement with Diffix aggregators, `AnonymizationParams.AccessLevel = Direct`
//
// (**NOTE**) AID columns may (and should) be used within the Diffix aggregator functions.
//
// Does not rewrite the statement. The caller is expected to use Diffix aggregator functions manually, in order to
// anonymize the query result. **NOTE** that the Diffix aggregators are optional, if they're not used, the result is the
// same as for a regular SQL query.
//
// Diffix aggregator functions available:
//   - `diffix_low_count(aid_columns_expression)` - `true` if the bucket has low count and should be suppressed
//   - `diffix_count(aid_columns_expression)` - anonymized count of the bucket
//
// `aid_columns_expression` should be a list of `aid_column` or `DISTINCT aid_column`.
let run queryContext statement : QueryResult =
  let query =
    statement
    |> Parser.parse
    |> Analyzer.analyze queryContext
    |> Normalizer.normalize
    |> Analyzer.compile queryContext

  let rows = query |> Planner.plan |> Executor.execute queryContext |> Seq.toList
  let columns = extractColumns query
  { Columns = columns; Rows = rows }
