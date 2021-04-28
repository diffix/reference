module rec OpenDiffix.Core.Planner

open OpenDiffix.Core.PlannerTypes
open OpenDiffix.Core.AnalyzerTypes

let private planJoin join = Plan.Join(planFrom join.Left, planFrom join.Right, join.Type, join.On)

let private planFrom =
  function
  | RangeTable (table, _alias) -> Plan.Scan table
  | Join join -> planJoin join
  | SubQuery query -> plan query

let rec private extractSetFunctions expression =
  function
  | FunctionExpr (SetFunction fn, args) -> Some(fn, args)
  | _ -> None
  |> Expression.Collect expression

let private projectSetFunctions setColumn expression =
  Expression.Map(
    expression,
    function
    | FunctionExpr (SetFunction _, _) -> setColumn
    | expression -> expression
  )

let private planProject expressions plan =
  match expressions |> List.collect extractSetFunctions |> List.distinct with
  | [] -> Plan.Project(plan, expressions)
  | [ setFn, args ] ->
      let setColumn = ColumnReference(plan.ColumnsCount(), SetFunction.ReturnType setFn args |> Utils.unwrap)
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

let rec private extractAggregators expression =
  function
  | FunctionExpr (AggregateFunction _, _) as aggregator -> Some aggregator
  | _ -> None
  |> Expression.Collect expression

let private planAggregate (groupingLabels: Expression list) (aggregators: Expression list) plan =
  if groupingLabels.IsEmpty && aggregators.IsEmpty then
    plan
  else
    Plan.Aggregate(plan, groupingLabels, aggregators)

let private getType expression = expression |> Expression.GetType |> Utils.unwrap

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
        | List values -> values |> List.map (projectExpression columns) |> List
    | Some i -> ColumnReference(i, getType expression)

let private planSelect query =
  let selectedExpressions = query.TargetList |> List.map (fun column -> column.Expression)
  let orderByExpressions = query.OrderBy |> List.map (fun (OrderBy (expression, _, _)) -> expression)
  let expressions = query.Having :: selectedExpressions @ orderByExpressions

  let aggregators = expressions |> List.collect extractAggregators |> List.distinct

  let groupingLabels = query.GroupingSets |> List.collect GroupingSet.Unwrap |> List.distinct

  let aggregatedColumns = groupingLabels @ aggregators

  let selectedExpressions = selectedExpressions |> List.map (projectExpression aggregatedColumns)

  let orderByExpressions = OrderByExpression.Map(query.OrderBy, (projectExpression aggregatedColumns))

  let havingExpression = projectExpression aggregatedColumns query.Having

  planFrom query.From
  |> planFilter query.Where
  |> planAggregate groupingLabels aggregators
  |> planFilter havingExpression
  |> planSort orderByExpressions
  |> planProject selectedExpressions

let plan =
  function
  | SelectQuery query -> planSelect query
  | UnionQuery _ -> failwith "Union planning not yet supported"
