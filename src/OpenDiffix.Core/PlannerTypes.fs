namespace OpenDiffix.Core.PlannerTypes

open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

type Plan =
  | Scan of tableName: string
  | Select of Plan * columns: Expression list
  | Filter of Plan * condition: Expression
  | Sort of Plan * OrderByExpression list
  | Aggregate of Plan * labels: Expression list * aggregators: Expression list
  | Unique of Plan
  | Join of left: Plan * right: Plan * JoinType * condition: Expression
  | Append of first: Plan * second: Plan
