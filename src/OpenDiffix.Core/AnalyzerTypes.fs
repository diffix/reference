namespace OpenDiffix.Core.AnalyzerTypes

open OpenDiffix.Core

type JoinType =
  | InnerJoin
  | LeftJoin
  | RightJoin
  | FullJoin

type SelectExpression =
  {
    //
    Type: ExpressionType
    Expression: Expression
    Alias: string
  }

type GroupingSet = int list

[<RequireQualifiedAccess>]
type Query =
  | Union of distinct: bool * Query * Query
  | Select of SelectQuery

and SelectQuery =
  {
    Columns: SelectExpression list
    Where: Expression
    From: SelectFrom
    GroupBy: Expression list
    GroupingSets: GroupingSet list
    Having: Expression
    OrderBy: OrderByExpression list
  }

and SelectFrom =
  | Query of query: Query * alias: string
  | Join of Join
  | Table of table: Table * alias: string

and Join =
  {
    //
    Type: JoinType
    Condition: Expression
    Left: SelectFrom
    Right: SelectFrom
  }
