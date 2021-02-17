namespace OpenDiffix.Core.PlannerTypes

open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

[<RequireQualifiedAccess>]
type Plan =
  | Scan of Table
  | Project of Plan * expressions: Expression list
  | Filter of Plan * condition: Expression
  | Sort of Plan * OrderByExpression list
  | Aggregate of Plan * groupingLabels: Expression list * aggregators: Expression list
  | Unique of Plan
  | Join of left: Plan * right: Plan * JoinType * condition: Expression
  | Append of first: Plan * second: Plan

  member this.ColumnsCount() =
    match this with
    | Scan table -> table.Columns.Length
    | Project (_, expressions) -> expressions.Length
    | Filter (plan, _) -> plan.ColumnsCount()
    | Sort (plan, _) -> plan.ColumnsCount()
    | Aggregate (_, groupingLabels, aggregators) -> groupingLabels.Length + aggregators.Length
    | Unique plan -> plan.ColumnsCount()
    | Join (left, right, _, _) -> left.ColumnsCount() + right.ColumnsCount()
    | Append (first, _) -> first.ColumnsCount()
