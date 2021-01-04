namespace OpenDiffix.Core.AnalyzerTypes

open OpenDiffix.Core

type JoinType =
  | InnerJoin
  | LeftJoin
  | RightJoin
  | OuterJoin
  | CrossJoin

type SelectExpression =
  {
    Type: ExpressionType
    Expression: Expression
    Alias: string
  }

type Query =
  {
    Select: SelectExpression list
    Where: Expression option
    From: QueryFrom
    GroupBy: Expression list option
    Having: Expression option
    OrderBy: Expression list
    Offset: uint64
    Limit: uint64 option
  }

and QueryFrom =
  | Subquery of query: Query * alias: string
  | Join of Join
  | Table of string

and Join =
  {
    Type: JoinType
    Condition: Expression
    Left: QueryFrom
    Right: QueryFrom
  }
