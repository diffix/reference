module OpenDiffix.Core.PlannerTypes

open AnalyzerTypes

[<RequireQualifiedAccess>]
type Plan =
  | Scan of Table
  | Project of Plan * expressions: Expression list
  | ProjectSet of Plan * setGenerator: SetFunction * args: Expression list
  | Filter of Plan * condition: Expression
  | Sort of Plan * OrderBy list
  | Aggregate of Plan * groupingLabels: Expression list * aggregators: Expression list
  | Unique of Plan
  | Join of left: Plan * right: Plan * JoinType * on: Expression
  | Append of first: Plan * second: Plan
  | Limit of Plan * amount: uint

module Plan =
  let rec columnsCount (plan: Plan) =
    match plan with
    | Plan.Scan table -> table.Columns.Length
    | Plan.Project (_, expressions) -> expressions.Length
    | Plan.ProjectSet (plan, _, _) -> (columnsCount plan) + 1
    | Plan.Filter (plan, _) -> columnsCount plan
    | Plan.Sort (plan, _) -> columnsCount plan
    | Plan.Aggregate (_, groupingLabels, aggregators) -> groupingLabels.Length + aggregators.Length
    | Plan.Unique plan -> columnsCount plan
    | Plan.Join (left, right, _, _) -> columnsCount left + columnsCount right
    | Plan.Append (first, _) -> columnsCount first
    | Plan.Limit (plan, _) -> columnsCount plan
