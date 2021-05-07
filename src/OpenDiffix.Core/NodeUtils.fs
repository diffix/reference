module OpenDiffix.Core.NodeUtils

open AnalyzerTypes

type NodeFunctions =
  // ----------------------------------------------------------------
  // Map
  // ----------------------------------------------------------------

  // Expression
  static member Map(expression: Expression, f: Expression -> Expression) =
    match expression with
    | FunctionExpr (fn, args) -> FunctionExpr(fn, List.map f args)
    | expr -> expr

  static member Map(expressions: Expression list, f: Expression -> Expression) = //
    List.map f expressions

  // Query
  static member Map(query: Query, f: SelectQuery -> SelectQuery) =
    match query with
    | UnionQuery (distinct, q1, q2) -> UnionQuery(distinct, NodeFunctions.Map(q1, f), NodeFunctions.Map(q2, f))
    | SelectQuery selectQuery -> SelectQuery(f selectQuery)

  static member Map(query: Query, f: Expression -> Expression) =
    NodeFunctions.Map(query, (fun (selectQuery: SelectQuery) -> NodeFunctions.Map(selectQuery, f)))

  static member Map(query: SelectQuery, f: Expression -> Expression) =
    {
      TargetList = NodeFunctions.Map(query.TargetList, f)
      From = query.From
      Where = f query.Where
      GroupingSets = NodeFunctions.Map(query.GroupingSets, f)
      OrderBy = NodeFunctions.Map(query.OrderBy, f)
      Having = f query.Having
    }

  // TargetEntry
  static member Map(targetList: TargetEntry list, f: Expression -> Expression) =
    targetList |> List.map (fun targetEntry -> NodeFunctions.Map(targetEntry, f))

  static member Map(targetEntry: TargetEntry, f: Expression -> Expression) =
    { targetEntry with Expression = f targetEntry.Expression }

  // GroupingSet
  static member Map(groupingSets: GroupingSet list, f: Expression -> Expression) =
    groupingSets |> List.map (fun groupingSet -> NodeFunctions.Map(groupingSet, f))

  static member Map(groupingSet: GroupingSet, f: Expression -> Expression) =
    groupingSet |> NodeFunctions.Unwrap |> List.map f |> GroupingSet

  // OrderBy
  static member Map(orderByList: OrderBy list, f: Expression -> Expression) =
    orderByList |> List.map (fun orderBy -> NodeFunctions.Map(orderBy, f))

  static member Map(orderBy: OrderBy, f: Expression -> Expression) =
    let exp, direction, nullBehavior = NodeFunctions.Unwrap(orderBy)
    OrderBy(f exp, direction, nullBehavior)

  // QueryRange
  static member Map(query: Query, f: QueryRange -> QueryRange) =
    NodeFunctions.Map(query, (fun (selectQuery: SelectQuery) -> { selectQuery with From = f selectQuery.From }))

  static member Map(join: Join, f: QueryRange -> QueryRange) =
    { join with Left = f join.Left; Right = f join.Right }

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

let inline private callUnwrap (_: ^M, node: ^T) =
  ((^M or ^T): (static member Unwrap : ^T -> ^U) node)

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

/// Maps immediate children of a node, preserving structure.
/// Recursion can be achieved by calling `map` again inside `func`.
let inline map func node =
  callMap (Unchecked.defaultof<NodeFunctions>, node, func)

/// Visits immediate children of a node.
/// Recursion can be achieved by calling `visit` again inside `func`.
let inline visit func node =
  node
  |> map (fun x ->
    func x
    x
  )
  |> ignore

/// Maps immediate children of a node to a list and flattens the result.
/// Recursion can be achieved by calling `collect` again inside `func`.
let inline collect func node =
  let mutable result = []
  node |> visit (fun x -> result <- result @ func x)
  result

/// Gets the inner expression of a node.
let inline unwrap node =
  callUnwrap (Unchecked.defaultof<NodeFunctions>, node)
