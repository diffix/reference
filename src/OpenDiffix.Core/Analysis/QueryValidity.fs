module OpenDiffix.Core.Analysis.QueryValidity

open OpenDiffix.Core

module private ExpressionExtractor =
  open OpenDiffix.Core.AnalyzerTypes

  let toSeq item = Seq.ofList [ item ]

  let rec flattenExpression (exp: Expression) =
    match exp with
    | Constant _ as constant -> toSeq constant
    | ColumnReference _ as columnRef -> toSeq columnRef
    | FunctionExpr (_, args) as fnExp ->
        seq {
          yield fnExp

          for arg in args do
            yield! flattenExpression arg
        }

  let selectExpression (column: SelectExpression) = flattenExpression column.Expression

  let rec from (fromClause: SelectFrom) =
    match fromClause with
    | Query query -> allExpressions query
    | Join join ->
        seq {
          yield! from join.Left
          yield! from join.Right
        }
    | Table _ -> Seq.empty

  and allExpressions (query: AnalyzerTypes.Query): Expression seq =
    match query with
    | UnionQuery (_, q1, q2) ->
        seq {
          yield! allExpressions q1
          yield! allExpressions q2
        }
    | SelectQuery select ->
        seq {
          for column in select.Columns do
            yield! selectExpression column

          yield! from select.From
          yield! flattenExpression select.Where

          for expression in List.concat select.GroupingSets do
            yield! flattenExpression expression

          for (expression, _, _) in select.OrderBy do
            yield! flattenExpression expression

          yield! flattenExpression select.Having
        }

  let aggregates query =
    allExpressions query
    |> Seq.map (function
         | FunctionExpr (AggregateFunction (aggregateFn, aggregateOptions), _) ->
             Some {| Function = aggregateFn; Options = aggregateOptions |}
         | _ -> None)
    |> Seq.choose id

let private allowedAggregate (query: AnalyzerTypes.Query): Result<AnalyzerTypes.Query, string> =
  query
  |> ExpressionExtractor.aggregates
  |> Seq.filter (fun aggregate -> aggregate.Function <> AggregateFunction.Count)
  |> Seq.isEmpty
  |> function
  | true -> Ok query
  | _ -> Error "Only count aggregates are supported"

let validateQuery (query: AnalyzerTypes.Query): Result<unit, string> =
  query
  |> allowedAggregate
  |> Result.map ignore
