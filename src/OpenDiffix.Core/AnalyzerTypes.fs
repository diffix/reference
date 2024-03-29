module rec OpenDiffix.Core.AnalyzerTypes

// ----------------------------------------------------------------
// Types
// ----------------------------------------------------------------

type Query = SelectQuery

type SelectQuery =
  {
    TargetList: TargetEntry list
    From: QueryRange
    Where: Expression
    GroupBy: Expression list
    OrderBy: OrderBy list
    Having: Expression
    Limit: uint option
    AnonymizationContext: AnonymizationContext option
  }

type TargetEntry = { Expression: Expression; Alias: string; Tag: TargetEntryTag }

type TargetEntryTag =
  | RegularTargetEntry
  | JunkTargetEntry
  | AidTargetEntry

type QueryRange =
  | SubQuery of query: Query * alias: string
  | Join of Join
  | RangeTable of RangeTable

type Join = { Type: JoinType; Left: QueryRange; Right: QueryRange; On: Expression }

type RangeTable = Table * string

type RangeTables = RangeTable list

type RangeColumn = { RangeName: string; ColumnName: string; Type: ExpressionType; IsAid: bool }

// ----------------------------------------------------------------
// Functions
// ----------------------------------------------------------------

module TargetEntry =
  let isRegular targetEntry = targetEntry.Tag = RegularTargetEntry

module QueryRange =
  let rec columnsCount (queryRange: QueryRange) =
    match queryRange with
    | SubQuery(query, _) -> query.TargetList.Length
    | Join join -> (columnsCount join.Left) + (columnsCount join.Right)
    | RangeTable(table, _) -> table.Columns.Length
