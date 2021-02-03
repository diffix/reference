namespace OpenDiffix.Core.AnalyzerTypes

open Aether
open OpenDiffix.Core

type JoinType =
  | InnerJoin
  | LeftJoin
  | RightJoin
  | FullJoin

type SelectExpression =
  {
    Expression: Expression
    Alias: string
  }
  static member _expression: Lens<SelectExpression, Expression> =
    (fun s -> s.Expression), (fun a s -> { s with Expression = a })

type GroupingSet = Expression list

module GroupingSet =
  let mapExpressions f expressionList = List.map (List.map (Expression.mapExpression f)) expressionList

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
  static member _columns = (fun s -> s.Columns), (fun a s -> { s with SelectQuery.Columns = a })
  static member _where = (fun s -> s.Where), (fun a s -> { s with SelectQuery.Where = a })
  static member _groupingSets = (fun s -> s.GroupingSets), (fun a s -> { s with GroupingSets = a })
  static member _orderBy = (fun s -> s.OrderBy), (fun a s -> { s with SelectQuery.OrderBy = a })
  static member _having = (fun s -> s.Having), (fun a s -> { s with SelectQuery.Having = a })

  static member mapExpressions f selectQuery =
    let expressionMapper = Expression.mapExpression f

    selectQuery
    |> Optic.map SelectQuery._columns (List.map (Optic.map SelectExpression._expression expressionMapper))
    |> Optic.map SelectQuery._where expressionMapper
    |> Optic.map SelectQuery._groupingSets (GroupingSet.mapExpressions f)
    |> Optic.map SelectQuery._orderBy (List.map (Optic.map OrderByExpression._expression expressionMapper))
    |> Optic.map SelectQuery._having expressionMapper

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
