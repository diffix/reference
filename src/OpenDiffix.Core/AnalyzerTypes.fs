namespace OpenDiffix.Core.AnalyzerTypes

open OpenDiffix.Core

type JoinType =
  | InnerJoin
  | LeftJoin
  | RightJoin
  | FullJoin

type SelectExpression =
  {
    Type: ExpressionType
    Expression: Expression
    Alias: string
  }

type GroupingSet = int list

[<RequireQualifiedAccess>]
type ShowQueryKinds =
  | Tables
  | ColumnsInTable of tableName: string

type Query =
  | Union of distinct: bool * Query * Query
  | SelectQuery of SelectQuery
  | ShowQuery of ShowQueryKinds

and SelectQuery =
  {
    Select: SelectExpression list
    Where: Expression option
    From: SelectFrom
    GroupBy: Expression list
    GroupingSets: GroupingSet list
    Having: Expression option
    OrderBy: OrderByExpression list
  }

and SelectFrom =
  | SubQuery of query: Query * alias: string
  | Join of Join
  | Table of name: string * alias: string option

and Join =
  {
    Type: JoinType
    Condition: Expression
    Left: SelectFrom
    Right: SelectFrom
  }
