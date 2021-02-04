module OpenDiffix.Core.Analysis.QueryValidity

open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

module private ExpressionExtractor =
  let rec flattenExpression (exp: Expression) =
    match exp with
    | Constant _ as constant -> Seq.singleton constant
    | JunkReference _ as ref -> Seq.singleton ref
    | ColumnReference _ as ref -> Seq.singleton ref
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
         | FunctionExpr (AggregateFunction (aggregateFn, aggregateOptions), args) ->
             Some {| Function = aggregateFn; Options = aggregateOptions; Args = args |}
         | _ -> None)
    |> Seq.choose id

let private assertEmpty query errorMsg seq = if Seq.isEmpty seq then Ok query else Error errorMsg

let private validateOnlyCount query =
  query
  |> ExpressionExtractor.aggregates
  |> Seq.filter (fun aggregate -> aggregate.Function <> AggregateFunction.Count)
  |> assertEmpty query "Only count aggregates are supported"

let private allowedCountUsage aidColIdx query =
  query
  |> ExpressionExtractor.aggregates
  |> Seq.filter (fun aggregate -> aggregate.Function = AggregateFunction.Count)
  |> Seq.map (fun aggregate -> aggregate.Args)
  |> Seq.filter (function
       | [] -> false
       | [ ColumnReference (index, _) ] when index = aidColIdx -> false
       | _ -> true)
  |> assertEmpty query "Only count(*) and count(distinct aid-column) are supported"

open FsToolkit.ErrorHandling
open FsToolkit.ErrorHandling.Operator.Result

let rec private validateSingleTableSelect (query: AnalyzerTypes.Query) =
  match query with
  | UnionQuery (_distinct, left, right) ->
      result {
        let! _ = validateSingleTableSelect left
        let! _ = validateSingleTableSelect right
        return query
      }
  | SelectQuery select ->
      match select.From with
      | SelectFrom.Table _ -> Ok query
      | _ -> Error "JOIN queries are not supported at present"

let private allowedAggregate aidColIdx (query: AnalyzerTypes.Query): Result<AnalyzerTypes.Query, string> =
  query
  |> validateOnlyCount
  >>= allowedCountUsage aidColIdx
  >>= validateSingleTableSelect

let validateQuery aidColIdx (query: AnalyzerTypes.Query): Result<unit, string> =
  query |> allowedAggregate aidColIdx |> Result.map ignore
