module rec OpenDiffix.Core.Planner

open OpenDiffix.Core.PlannerTypes
open OpenDiffix.Core.AnalyzerTypes

let private planJoin join = Plan.Join(planFrom join.Left, planFrom join.Right, join.Type, join.Condition)

let private planFrom =
  function
  | Table table -> Plan.Scan table
  | Join join -> planJoin join
  | Query query -> plan query

let private planProject expressions plan = Plan.Project(plan, expressions)

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
  if groupingLabels.IsEmpty && aggregators.IsEmpty then plan else Plan.Aggregate(plan, groupingLabels, aggregators)

let private getType expression = expression |> Expression.GetType |> Utils.unwrap

let rec private projectExpression expression columns =
  if List.isEmpty columns then
    expression
  else
    match columns |> List.tryFindIndex (fun column -> column = expression) with
    | None ->
        match expression with
        | FunctionExpr (fn, args) ->
            let args = args |> List.map (fun arg -> projectExpression arg columns)
            FunctionExpr(fn, args)
        | Constant _ -> expression
        | ColumnReference _ -> failwith "Expression projection failed"
    | Some i -> ColumnReference(i, getType expression)

let private planSelect query =
  let selectedExpressions = query.Columns |> List.map (fun column -> column.Expression)
  let orderByExpressions = query.OrderBy |> List.map (fun (OrderBy (expression, _, _)) -> expression)
  let expressions = query.Having :: selectedExpressions @ orderByExpressions

  let aggregators = expressions |> List.collect extractAggregators |> List.distinct

  let groupingLabels = query.GroupingSets |> List.collect GroupingSet.Unwrap |> List.distinct

  let aggregatedColumns = groupingLabels @ aggregators

  let selectedExpressions =
    selectedExpressions
    |> List.map (fun expression -> projectExpression expression aggregatedColumns)

  let orderByExpressions =
    OrderByExpression.Map(
      query.OrderBy,
      (fun (expression: Expression) -> projectExpression expression aggregatedColumns)
    )

  let havingExpression = projectExpression query.Having aggregatedColumns

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
