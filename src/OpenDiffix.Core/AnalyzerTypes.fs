module rec OpenDiffix.Core.AnalyzerTypes

// ----------------------------------------------------------------
// Types
// ----------------------------------------------------------------

type Query =
  | UnionQuery of distinct: bool * Query * Query
  | SelectQuery of SelectQuery

type SelectQuery =
  {
    TargetList: TargetEntry list
    From: QueryRange
    Where: Expression
    GroupingSets: GroupingSet list
    OrderBy: OrderBy list
    Having: Expression
    Limit: uint option
  }

type TargetEntry = { Expression: Expression; Alias: string; Tag: TargetEntryTag }

type TargetEntryTag =
  | RegularTargetEntry
  | JunkTargetEntry
  | AidTargetEntry

type GroupingSet = GroupingSet of Expression list

type QueryRange =
  | SubQuery of query: Query * alias: string
  | Join of Join
  | RangeTable of RangeTable

type JoinType = ParserTypes.JoinType

type Join = { Type: JoinType; Left: QueryRange; Right: QueryRange; On: Expression }

type RangeTable = Table * string

type RangeTables = RangeTable list

// ----------------------------------------------------------------
// Functions
// ----------------------------------------------------------------

module Query =
  let assertSelectQuery query =
    match query with
    | SelectQuery selectQuery -> selectQuery
    | UnionQuery _ -> failwith "Union queries are not yet supported"

module TargetEntry =
  let isRegular targetEntry = targetEntry.Tag = RegularTargetEntry

module QueryRange =
  let rec columnsCount (queryRange: QueryRange) =
    match queryRange with
    | SubQuery (query, _) -> (Query.assertSelectQuery query).TargetList.Length
    | Join join -> (columnsCount join.Left) + (columnsCount join.Right)
    | RangeTable (table, _) -> table.Columns.Length
