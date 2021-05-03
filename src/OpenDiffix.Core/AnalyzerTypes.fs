module rec OpenDiffix.Core.AnalyzerTypes

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
  }

type TargetEntry = { Expression: Expression; Alias: string }

type GroupingSet = GroupingSet of Expression list

type QueryRange =
  | SubQuery of query: Query
  | Join of Join
  | RangeTable of RangeTable

type JoinType = ParserTypes.JoinType

type Join = { Type: JoinType; Left: QueryRange; Right: QueryRange; On: Expression }

type RangeTable = Table * string

type RangeTables = RangeTable list
