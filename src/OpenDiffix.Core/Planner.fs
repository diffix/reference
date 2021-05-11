module rec OpenDiffix.Core.Planner

open AnalyzerTypes
open PlannerTypes
open NodeUtils

let private planJoin join =
  Plan.Join(planFrom join.Left, planFrom join.Right, join.Type, join.On)

let private planFrom =
  function
  | RangeTable (table, _alias) -> Plan.Scan table
  | Join join -> planJoin join
  | SubQuery (query, _alias) -> plan query

let rec private extractSetFunctions expression =
  match expression with
  | FunctionExpr (SetFunction fn, args) -> [ (fn, args) ]
  | expr -> expr |> collect extractSetFunctions

let private projectSetFunctions setColumn expression =
  let rec exprMapper expr =
    match expr with
    | FunctionExpr (SetFunction _, _) -> setColumn
    | other -> other |> map exprMapper

  exprMapper expression

let private planProject expressions plan =
  match expressions |> List.collect extractSetFunctions |> List.distinct with
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

let rec private projectExpression columns expression =
  if List.isEmpty columns then
    expression
  else
    match columns |> List.tryFindIndex (fun column -> column = expression) with
    | None ->
        match expression with
        | FunctionExpr (fn, args) ->
            let args = args |> List.map (projectExpression columns)
            FunctionExpr(fn, args)
        | Constant _ -> expression
        | ColumnReference _ -> failwith "Expression projection failed"
        | ListExpr values -> values |> List.map (projectExpression columns) |> ListExpr
    | Some i -> ColumnReference(i, Expression.typeOf expression)

let private planSelect query =
  let selectedExpressions = query.TargetList |> List.map (fun column -> column.Expression)

  let orderByExpressions = query.OrderBy |> List.map (fun (OrderBy (expression, _, _)) -> expression)

  let expressions = query.Having :: selectedExpressions @ orderByExpressions

  let aggregators = expressions |> collectAggregators |> List.distinct

  let groupingLabels = query.GroupingSets |> List.collect unwrap |> List.distinct

  let aggregatedColumns = groupingLabels @ aggregators

  let selectedExpressions = selectedExpressions |> List.map (projectExpression aggregatedColumns)

  let orderByExpressions = query.OrderBy |> map (projectExpression aggregatedColumns)

  let havingExpression = projectExpression aggregatedColumns query.Having

  planFrom query.From
  |> planFilter query.Where
  |> planAggregate groupingLabels aggregators
  |> planFilter havingExpression
  |> planSort orderByExpressions
  |> planProject selectedExpressions

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let plan query =
  match query with
  | SelectQuery query -> planSelect query
  | UnionQuery _ -> failwith "Union planning not yet supported"
