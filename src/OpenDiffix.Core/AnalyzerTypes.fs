namespace OpenDiffix.Core.AnalyzerTypes

open OpenDiffix.Core

type JoinType = ParserTypes.JoinType

type SelectExpression =
  {
    Expression: Expression
    Alias: string
  }
  static member Map(se: SelectExpression, f: Expression -> Expression) =
    { se with Expression = Expression.Map(se.Expression, f) }

type GroupingSet =
  | GroupingSet of Expression list

  static member Map(groupingSet: GroupingSet, f: Expression -> Expression) =
    match groupingSet with
    | GroupingSet expressions -> GroupingSet(expressions |> List.map (fun expression -> Expression.Map(expression, f)))

  static member Unwrap =
    function
    | GroupingSet expressions -> expressions

type Query =
  | UnionQuery of distinct: bool * Query * Query
  | SelectQuery of SelectQuery

  static member Map(query: Query, f: SelectQuery -> SelectQuery) =
    match query with
    | UnionQuery (distinct, q1, q2) -> UnionQuery(distinct, Query.Map(q1, f), Query.Map(q2, f))
    | SelectQuery selectQuery -> SelectQuery(f selectQuery)

  static member Map(query: Query, f: SelectFrom -> SelectFrom): Query =
    Query.Map(query, (fun (selectQuery: SelectQuery) -> SelectQuery.Map(selectQuery, f)))

  static member Map(query: Query, f: Expression -> Expression): Query =
    Query.Map(query, (fun (selectQuery: SelectQuery) -> SelectQuery.Map(selectQuery, f)))

and SelectQuery =
  {
    Columns: SelectExpression list
    From: SelectFrom
    TargetTables: Table list
    Where: Expression
    GroupingSets: GroupingSet list
    OrderBy: OrderByExpression list
    Having: Expression
  }

  static member Map(query: SelectQuery, f: SelectFrom -> SelectFrom) =
    { query with From = SelectFrom.Map(query.From, f) }

  static member Map(query: SelectQuery, f: Expression -> Expression) =
    { query with
        Columns = List.map (fun column -> SelectExpression.Map(column, f)) query.Columns
        From = SelectFrom.Map(query.From, f)
        Where = Expression.Map(query.Where, f)
        GroupingSets = List.map (fun groupingSet -> GroupingSet.Map(groupingSet, f)) query.GroupingSets
        OrderBy = OrderByExpression.Map(query.OrderBy, f)
        Having = Expression.Map(query.Having, f)
    }

and SelectFrom =
  | Query of query: Query
  | Join of Join
  | Table of table: Table

  static member Map(selectFrom: SelectFrom, f: Query -> Query) =
    match selectFrom with
    | Query q -> Query(f q)
    | other -> other

  static member Map(selectFrom: SelectFrom, f: Table -> Table) =
    match selectFrom with
    | Table t -> Table(f t)
    | other -> other

  static member Map(selectFrom: SelectFrom, f: SelectFrom -> SelectFrom) = f selectFrom

  static member Map(selectFrom: SelectFrom, f: Expression -> Expression) =
    match selectFrom with
    | Query q -> Query(Query.Map(q, f))
    | other -> other

and Join =
  {
    //
    Type: JoinType
    Condition: Expression
    Left: SelectFrom
    Right: SelectFrom
  }
