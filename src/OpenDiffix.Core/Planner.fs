module rec OpenDiffix.Core.Planner

open AnalyzerTypes
open NodeUtils

// ----------------------------------------------------------------
// Utils
// ----------------------------------------------------------------

/// Prepares an expression for use in a plan.
/// If an expression is already computed in a child plan, the outer
/// expression is mapped to a reference to its index in the inner plan.
let rec private projectExpression innerExpressions outerExpression =
  if List.isEmpty innerExpressions then
    outerExpression
  else
    match innerExpressions |> List.tryFindIndex ((=) outerExpression) with
    | None ->
      match outerExpression with
      | FunctionExpr (fn, args) ->
        let args = args |> List.map (projectExpression innerExpressions)
        FunctionExpr(fn, args)
      | Constant _ -> outerExpression
      | ColumnReference _ -> failwith "Expression projection failed"
      | ListExpr values -> values |> List.map (projectExpression innerExpressions) |> ListExpr
    | Some i -> ColumnReference(i, Expression.typeOf outerExpression)

/// Swaps set function expressions with a reference to their evaluated value in the child plan.
let private projectSetFunctions evaluatedSetFunction expression =
  let rec exprMapper expr =
    match expr with
    | FunctionExpr (SetFunction _, _) -> evaluatedSetFunction
    | other -> other |> map exprMapper

  exprMapper expression

/// Returns all set functions in an expression.
let rec private collectSetFunctions expression =
  match expression with
  | FunctionExpr (SetFunction fn, args) -> [ (fn, args) ]
  | expr -> expr |> collect collectSetFunctions

// ----------------------------------------------------------------
// Node planners
// ----------------------------------------------------------------

let private planJoin join columnIndices =
  let leftColumnCount = QueryRange.columnsCount join.Left
  let leftIndices, rightIndices = List.partition ((>=) leftColumnCount) columnIndices
  let rightIndices = List.map (fun i -> i - leftColumnCount) rightIndices
  Plan.Join(planFrom join.Left leftIndices, planFrom join.Right rightIndices, join.Type, join.On)

let private planProject expressions plan =
  match expressions |> List.collect collectSetFunctions |> List.distinct with
  | [] -> Plan.Project(plan, expressions)
  | [ setFn, args ] ->
    let setColumn = ColumnReference(Plan.columnsCount plan, Expression.typeOfSetFunction setFn args)
    let expressions = expressions |> List.map (projectSetFunctions setColumn)
    Plan.Project(Plan.ProjectSet(plan, setFn, args), expressions)
  | _ -> failwith "Using multiple set functions in the same query is not supported"

let private planFilter condition plan =
  match condition with
  | Constant (Boolean true) -> plan
  | _ -> Plan.Filter(plan, condition)

let private planSort sortExpressions plan =
  match sortExpressions with
  | [] -> plan
  | _ -> Plan.Sort(plan, sortExpressions)

let private planAggregate (groupingLabels: Expression list) (aggregators: Expression list) plan =
  if groupingLabels.IsEmpty && aggregators.IsEmpty then
    plan
  else
    Plan.Aggregate(plan, groupingLabels, aggregators)

let private planFrom queryRange columnIndices =
  match queryRange with
  | RangeTable (table, _alias) -> Plan.Scan(table, columnIndices)
  | Join join -> planJoin join columnIndices
  | SubQuery (query, _alias) -> planQuery query

let private planLimit amount plan =
  match amount with
  | None -> plan
  | Some amount -> Plan.Limit(plan, amount)

let private collectColumnIndices node =
  let rec exprIndices expr =
    match expr with
    | ColumnReference (index, _) -> [ index ]
    | expr -> expr |> collect exprIndices

  node |> collect exprIndices

let private planQuery query =
  let selectedExpressions = query.TargetList |> List.map (fun column -> column.Expression)
  let orderByExpressions = query.OrderBy |> List.map (fun (OrderBy (expression, _, _)) -> expression)
  let expressions = query.Having :: selectedExpressions @ orderByExpressions
  let aggregators = expressions |> collectAggregates |> List.distinct
  let groupingLabels = query.GroupBy |> List.distinct
  let aggregatedColumns = groupingLabels @ aggregators
  let selectedExpressions = selectedExpressions |> List.map (projectExpression aggregatedColumns)
  let orderByExpressions = query.OrderBy |> map (projectExpression aggregatedColumns)
  let havingExpression = projectExpression aggregatedColumns query.Having

  let columnIndices = query.Where :: expressions |> collectColumnIndices |> List.distinct |> List.sort

  planFrom query.From columnIndices
  |> planFilter query.Where
  |> planAggregate groupingLabels aggregators
  |> planFilter havingExpression
  |> planSort orderByExpressions
  |> planProject selectedExpressions
  |> planLimit query.Limit

let private filterJunk targetList plan =
  let columns =
    targetList
    |> List.indexed
    |> List.filter (fun (_i, col) ->
      match col.Tag with
      | RegularTargetEntry -> true
      | JunkTargetEntry -> false
      | AidTargetEntry -> failwith "AID target entries should never be exposed from a top-level query"
    )
    |> List.map fst

  if List.length columns = List.length targetList then
    // No junk, do nothing
    plan
  else
    Plan.Project(
      plan,
      columns
      |> List.map (fun i -> ColumnReference(i, Expression.typeOf targetList.[i].Expression))
    )

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let plan query =
  query |> planQuery |> filterJunk query.TargetList
