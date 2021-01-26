namespace OpenDiffix.Core.AnalyzerTypes

open OpenDiffix.Core

type JoinType =
  | InnerJoin
  | LeftJoin
  | RightJoin
  | FullJoin

type SelectExpression = { Expression: Expression; Alias: string }

type GroupingSet = Expression list

type Query =
  | UnionQuery of distinct: bool * Query * Query
  | SelectQuery of SelectQuery

and SelectQuery =
  {
    Columns: SelectExpression list
    From: SelectFrom
    Where: Expression
    GroupingSets: GroupingSet list
    OrderBy: OrderByExpression list
    Having: Expression
  }

and SelectFrom =
  | Query of query: Query
  | Join of Join
  | Table of table: Table

and Join =
  {
    //
    Type: JoinType
    Condition: Expression
    Left: SelectFrom
    Right: SelectFrom
  }
