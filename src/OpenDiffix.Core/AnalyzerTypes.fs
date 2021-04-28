namespace OpenDiffix.Core.AnalyzerTypes

open OpenDiffix.Core

type JoinType = ParserTypes.JoinType

type TargetEntry =
  {
    Expression: Expression
    Alias: string
  }
  static member Map(te: TargetEntry, f: Expression -> Expression) =
    { te with Expression = Expression.Map(te.Expression, f) }

type GroupingSet =
  | GroupingSet of Expression list

  static member Map(groupingSet: GroupingSet, f: Expression -> Expression) =
    match groupingSet with
    | GroupingSet expressions -> GroupingSet(expressions |> List.map (fun expression -> Expression.Map(expression, f)))

  static member Unwrap =
    function
    | GroupingSet expressions -> expressions

type RangeTable = Table * string
type TargetTables = RangeTable list

type Query =
  | UnionQuery of distinct: bool * Query * Query
  | SelectQuery of SelectQuery

  static member Map(query: Query, f: SelectQuery -> SelectQuery) =
    match query with
    | UnionQuery (distinct, q1, q2) -> UnionQuery(distinct, Query.Map(q1, f), Query.Map(q2, f))
    | SelectQuery selectQuery -> SelectQuery(f selectQuery)

  static member Map(query: Query, f: QueryRange -> QueryRange) : Query =
    Query.Map(query, (fun (selectQuery: SelectQuery) -> SelectQuery.Map(selectQuery, f)))

  static member Map(query: Query, f: Expression -> Expression) : Query =
    Query.Map(query, (fun (selectQuery: SelectQuery) -> SelectQuery.Map(selectQuery, f)))

and SelectQuery =
  {
    TargetList: TargetEntry list
    From: QueryRange
    TargetTables: TargetTables
    Where: Expression
    GroupingSets: GroupingSet list
    OrderBy: OrderByExpression list
    Having: Expression
  }

  static member Map(query: SelectQuery, f: QueryRange -> QueryRange) =
    { query with From = QueryRange.Map(query.From, f) }

  static member Map(query: SelectQuery, f: Expression -> Expression) =
    { query with
        TargetList = List.map (fun column -> TargetEntry.Map(column, f)) query.TargetList
        From = QueryRange.Map(query.From, f)
        Where = Expression.Map(query.Where, f)
        GroupingSets = List.map (fun groupingSet -> GroupingSet.Map(groupingSet, f)) query.GroupingSets
        OrderBy = OrderByExpression.Map(query.OrderBy, f)
        Having = Expression.Map(query.Having, f)
    }

and QueryRange =
  | SubQuery of query: Query
  | Join of Join
  | RangeTable of RangeTable

  static member Map(range: QueryRange, f: Query -> Query) =
    match range with
    | SubQuery q -> SubQuery(f q)
    | other -> other

  static member Map(range: QueryRange, f: Table -> Table) =
    match range with
    | RangeTable (table, alias) -> RangeTable(f table, alias)
    | other -> other

  static member Map(range: QueryRange, f: QueryRange -> QueryRange) = f range

  static member Map(range: QueryRange, f: Expression -> Expression) =
    match range with
    | SubQuery q -> SubQuery(Query.Map(q, f))
    | other -> other

and Join =
  {
    Type: JoinType
    Left: QueryRange
    Right: QueryRange
    On: Expression
  }
