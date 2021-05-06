module OpenDiffix.Core.NodeUtils

open AnalyzerTypes

type NodeFunctions =
  // ----------------------------------------------------------------
  // Map
  // ----------------------------------------------------------------

  static member Map(expression, f: Expression -> Expression) =
    match expression with
    | FunctionExpr (fn, args) ->
        f (FunctionExpr(fn, List.map (fun (arg: Expression) -> NodeFunctions.Map(arg, f)) args))
    | expr -> f expr

  static member Map(orderBy: OrderBy list, f: Expression -> Expression) =
    List.map (fun (orderBy: OrderBy) -> NodeFunctions.Map(orderBy, f)) orderBy

  static member Map(orderBy: OrderBy, f: Expression -> Expression) =
    let exp, direction, nullBehavior = NodeFunctions.Unwrap(orderBy)
    OrderBy(f exp, direction, nullBehavior)

  static member Map(query: Query, f: SelectQuery -> SelectQuery) =
    match query with
    | UnionQuery (distinct, q1, q2) -> UnionQuery(distinct, NodeFunctions.Map(q1, f), NodeFunctions.Map(q2, f))
    | SelectQuery selectQuery -> SelectQuery(f selectQuery)

  static member Map(query: Query, f: QueryRange -> QueryRange) =
    NodeFunctions.Map(query, (fun (selectQuery: SelectQuery) -> NodeFunctions.Map(selectQuery, f)))

  static member Map(query: Query, f: Expression -> Expression) =
    NodeFunctions.Map(query, (fun (selectQuery: SelectQuery) -> NodeFunctions.Map(selectQuery, f)))

  static member Map(query: SelectQuery, f: QueryRange -> QueryRange) =
    { query with
        From = NodeFunctions.Map(query.From, f)
    }

  static member Map(groupingSet: GroupingSet, f: Expression -> Expression) =
    groupingSet
    |> NodeFunctions.Unwrap
    |> List.map (fun expression -> NodeFunctions.Map(expression, f))
    |> GroupingSet

  static member Map(te: TargetEntry, f: Expression -> Expression) =
    { te with
        Expression = NodeFunctions.Map(te.Expression, f)
    }

  static member Map(query: SelectQuery, f: Expression -> Expression) =
    { query with
        TargetList = List.map (fun (column: TargetEntry) -> NodeFunctions.Map(column, f)) query.TargetList
        From = NodeFunctions.Map(query.From, f)
        Where = NodeFunctions.Map(query.Where, f)
        GroupingSets = List.map (fun (groupingSet: GroupingSet) -> NodeFunctions.Map(groupingSet, f)) query.GroupingSets
        OrderBy = NodeFunctions.Map(query.OrderBy, f)
        Having = NodeFunctions.Map(query.Having, f)
    }

  static member Map(range: QueryRange, f: Table -> Table) =
    match range with
    | RangeTable (table, alias) -> RangeTable(f table, alias)
    | other -> other

  static member Map(range: QueryRange, f: Query -> Query) =
    match range with
    | SubQuery q -> SubQuery(f q)
    | other -> other

  static member Map(range: QueryRange, f: QueryRange -> QueryRange) = f range

  static member Map(range: QueryRange, f: Expression -> Expression) =
    match range with
    | SubQuery q -> SubQuery(NodeFunctions.Map(q, f))
    | other -> other

  // ----------------------------------------------------------------
  // Collect
  // ----------------------------------------------------------------

  static member Collect<'T>(expression: Expression, f: Expression -> 'T list) =
    match expression with
    | FunctionExpr (_, args) -> List.collect f args
    | _ -> []

  static member Collect<'T>(expressions: Expression list, f: Expression -> 'T list) =
    expressions |> List.collect (fun expr -> NodeFunctions.Collect(expr, f))

  // ----------------------------------------------------------------
  // Unwrap
  // ----------------------------------------------------------------

  static member Unwrap(orderByExpression: OrderBy) =
    match orderByExpression with
    | OrderBy (expr, direction, nulls) -> (expr, direction, nulls)

  static member Unwrap(groupingSet: GroupingSet) : Expression list =
    match groupingSet with
    | GroupingSet expressions -> expressions

let inline private callMap (_: ^M, node: ^T, func: ^F) =
  ((^M or ^T): (static member Map : ^T * ^F -> ^T) (node, func))

let inline private callCollect (_: ^M, node: ^T, func: ^F) =
  ((^M or ^T): (static member Collect : ^T * ^F -> ^U list) (node, func))

let inline private callUnwrap (_: ^M, node: ^T) =
  ((^M or ^T): (static member Unwrap : ^T -> ^U) node)

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

/// Recursively maps elements of a node, preserving structure.
let inline map func node =
  callMap (Unchecked.defaultof<NodeFunctions>, node, func)

/// Maps immediate children of a node to a list and flattens the result.
/// Recursion can be achieved by calling `collect` again inside `func`.
let inline collect func node =
  callCollect (Unchecked.defaultof<NodeFunctions>, node, func)

/// Gets the inner expression of a node.
let inline unwrap node =
  callUnwrap (Unchecked.defaultof<NodeFunctions>, node)
