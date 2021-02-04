module OpenDiffix.Core.Planner

open OpenDiffix.Core.PlannerTypes
open OpenDiffix.Core.AnalyzerTypes

let private planFrom =
  function
  | Table table -> Plan.Scan table
  | Join _ -> failwith "join planning not yet supported"
  | Query _ -> failwith "subquery planning not yet supported"

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
  match expression with
  | FunctionExpr (ScalarFunction _, args) -> args |> List.collect extractAggregators
  | FunctionExpr (AggregateFunction _, _) as aggregator -> [ aggregator ]
  | _ -> []

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
        | ColumnReference _
        | JunkReference _ -> failwith "Expression projection failed"
    | Some i -> ColumnReference(i, getType expression)

let private planSelect query =
  let selectedExpressions = query.Columns |> List.map (fun column -> column.Expression)
  let orderByExpressions = query.OrderBy |> List.map (fun (expression, _, _) -> expression)
  let expressions = selectedExpressions @ orderByExpressions

  let aggregators = expressions |> List.collect extractAggregators |> List.distinct
  let groupingLabels = query.GroupingSets |> List.concat |> List.distinct
  let aggregatedColumns = groupingLabels @ aggregators

  let selectedExpressions =
    selectedExpressions
    |> List.map (fun expression -> projectExpression expression aggregatedColumns)

  let orderByExpressions =
    query.OrderBy
    |> List.map (fun (expression, direction, nulls) ->
      let expression = projectExpression expression aggregatedColumns
      expression, direction, nulls
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
  | UnionQuery _ -> failwith "union planning not yet supported"
