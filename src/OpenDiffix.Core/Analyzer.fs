module OpenDiffix.Core.Analyzer

open FsToolkit.ErrorHandling
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

let rec functionExpression table fn children =
  children
  |> List.map (mapExpression table)
  |> List.sequenceResultM
  |> Result.map (fun children -> FunctionExpr(fn, children))

and mapExpression table parsedExpression =
  match parsedExpression with
  | ParserTypes.Identifier identifierName ->
      result {
        let! (index, column) = Table.getColumn table identifierName
        return Expression.ColumnReference(index, column.Type)
      }
  | ParserTypes.Expression.Integer value -> Value.Integer(int64 value) |> Constant |> Ok
  | ParserTypes.Expression.Float value -> Value.Real value |> Constant |> Ok
  | ParserTypes.Expression.String value -> Value.String value |> Constant |> Ok
  | ParserTypes.Expression.Boolean value -> Value.Boolean value |> Constant |> Ok
  | ParserTypes.Not expr -> functionExpression table (ScalarFunction Not) [ expr ]
  | ParserTypes.Lt (left, right) -> functionExpression table (ScalarFunction Lt) [ left; right ]
  | ParserTypes.LtE (left, right) -> functionExpression table (ScalarFunction LtE) [ left; right ]
  | ParserTypes.Gt (left, right) -> functionExpression table (ScalarFunction Gt) [ left; right ]
  | ParserTypes.GtE (left, right) -> functionExpression table (ScalarFunction GtE) [ left; right ]
  | ParserTypes.And (left, right) -> functionExpression table (ScalarFunction And) [ left; right ]
  | ParserTypes.Or (left, right) -> functionExpression table (ScalarFunction Or) [ left; right ]
  | ParserTypes.Equals (left, right) -> functionExpression table (ScalarFunction Equals) [ left; right ]
  | ParserTypes.Function (name, args) ->
      result {
        let! fn = Function.FromString name
        let! fn, childExpressions = mapFunctionExpression table fn args
        return FunctionExpr(fn, childExpressions)
      }
  | other -> Error $"The expression is not permitted in this context: %A{other}"

and mapFunctionExpression table fn args =
  match fn, args with
  | AggregateFunction (Count, aggregateArgs), [ ParserTypes.Star ] -> Ok(AggregateFunction(Count, aggregateArgs), [])
  | AggregateFunction (aggregate, aggregateArgs), [ ParserTypes.Distinct expr ] ->
      mapExpression table expr
      |> Result.map (fun childArg -> AggregateFunction(aggregate, { aggregateArgs with Distinct = true }), [ childArg ])
  | _, _ ->
      args
      |> List.map (mapExpression table)
      |> List.sequenceResultM
      |> Result.map (fun childArgs -> fn, childArgs)

let extractAlias =
  function
  | ParserTypes.Expression.Identifier aliasName -> Ok aliasName
  | other -> Error $"Expected an alias, but got an expression: %A{other}"

let expressionName =
  function
  | ParserTypes.Identifier identifierName -> identifierName
  | ParserTypes.Function (name, _args) -> name
  | _ -> ""

let wrapExpressionAsSelected table parserExpr =
  result {
    let! expr = mapExpression table parserExpr
    let name = expressionName parserExpr

    return { AnalyzerTypes.Expression = expr; AnalyzerTypes.Alias = name}
  }

let rec mapSelectedExpression table selectedExpression: Result<AnalyzerTypes.SelectExpression, string> =
  match selectedExpression with
  | ParserTypes.As (expr, exprAlias) ->
      result {
        let! childExpr = mapExpression table expr
        let! alias = extractAlias exprAlias
        return { Expression = childExpr; Alias = alias}
      }
  | ParserTypes.Identifier identifierName ->
      result {
        let! (index, column) = Table.getColumn table identifierName

        return
          {
            Expression = Expression.ColumnReference(index, column.Type)
            Alias = identifierName
          }
      }
  | ParserTypes.Expression.Function (fn, args) ->
      wrapExpressionAsSelected table (ParserTypes.Expression.Function(fn, args))
  | ParserTypes.Expression.Integer value -> wrapExpressionAsSelected table (ParserTypes.Expression.Integer value)
  | ParserTypes.Expression.Float value -> wrapExpressionAsSelected table (ParserTypes.Expression.Float value)
  | ParserTypes.Expression.String value -> wrapExpressionAsSelected table (ParserTypes.Expression.String value)
  | ParserTypes.Expression.Boolean value -> wrapExpressionAsSelected table (ParserTypes.Expression.Boolean value)
  | other -> Error $"Unexpected expression selected '%A{other}'"

let transformSelectedExpressions (table: Table) selectedExpressions =
  selectedExpressions
  |> List.map (mapSelectedExpression table)
  |> List.sequenceResultM

let selectedTableName =
  function
  | ParserTypes.Expression.Identifier tableName -> Ok tableName
  | _ -> Error "Only selecting from a single table is supported"

let transformExpressionOptionWithDefaultTrue table optionalExpression =
  optionalExpression
  |> Option.map (mapExpression table)
  |> Option.defaultValue (Value.Boolean true |> Expression.Constant |> Ok)

let transformGroupByIndex (expressions: Expression list) groupByExpression =
  match groupByExpression with
  | Constant (Integer index) ->
      if index < 1L || index > int64 expressions.Length then
        Error $"Invalid `GROUP BY` index: {index}"
      else
        expressions |> List.item (int index - 1) |> Ok
  | _ -> Ok groupByExpression

let transformGroupByIndices (selectedExpressions: AnalyzerTypes.SelectExpression list) groupByExpressions =
  let selectedExpressions =
    selectedExpressions
    |> List.map (fun selectedExpression -> selectedExpression.Expression)

  groupByExpressions |> List.map (transformGroupByIndex selectedExpressions)

let transformQuery table (selectQuery: ParserTypes.SelectQuery) =
  result {
    let! selectedExpressions = transformSelectedExpressions table selectQuery.Expressions
    let! whereClause = transformExpressionOptionWithDefaultTrue table selectQuery.Where
    let! havingClause = transformExpressionOptionWithDefaultTrue table selectQuery.Having

    let! groupBy = selectQuery.GroupBy |> List.map (mapExpression table) |> List.sequenceResultM
    let! groupBy = groupBy |> transformGroupByIndices selectedExpressions |> List.sequenceResultM

    return
      AnalyzerTypes.SelectQuery
        {
          Columns = selectedExpressions
          Where = whereClause
          From = AnalyzerTypes.SelectFrom.Table table
          GroupingSets = [ groupBy ]
          Having = havingClause
          OrderBy = []
          QueryPostProcessing = AnalyzerTypes.QueryPostProcessing.Default
        }
  }

let rec notAggregate =
  function
  | FunctionExpr (ScalarFunction _, args) -> List.forall notAggregate args
  | FunctionExpr (AggregateFunction _, _) -> false
  | _ -> true

let private addLowCountFilterToSelect aidColumnIndex (select: AnalyzerTypes.SelectQuery) =
  let userCountAggregateSelection =
    {
      AnalyzerTypes.SelectExpression.Expression =
        FunctionExpr(AggregateFunction(JunkUserCount, AggregateOptions.Default), [ aidColumnIndex ])
      AnalyzerTypes.SelectExpression.Alias = "low_count_aggregate"
    }

  let lowCountFilter = FunctionExpr(ScalarFunction Not, [ JunkReference LowCount ])

  if select.GroupingSets = [ [] ] then
    let nonAggregateSelectedExpressions =
      select.Columns
      |> List.map (fun selectedColumn -> selectedColumn.Expression)
      |> List.filter notAggregate

    let needsExpanding = List.length nonAggregateSelectedExpressions > 0

    { select with
        Columns = select.Columns @ [ userCountAggregateSelection ]
        GroupingSets = [ nonAggregateSelectedExpressions ]
        QueryPostProcessing = { select.QueryPostProcessing with ExpandRowsByUserCount = needsExpanding }
        Having = lowCountFilter
    }
  else
    { select with
        Columns = select.Columns @ [ userCountAggregateSelection ]
        Having = FunctionExpr(ScalarFunction And, [ lowCountFilter; select.Having ])
    }

let rec private addLowCountFilter aidColumnIndex =
  function
  | AnalyzerTypes.UnionQuery (distinct, q1, q2) ->
      AnalyzerTypes.UnionQuery(distinct, addLowCountFilter aidColumnIndex q1, addLowCountFilter aidColumnIndex q2)
  | AnalyzerTypes.SelectQuery select -> addLowCountFilterToSelect aidColumnIndex select |> AnalyzerTypes.SelectQuery

let private aidColumn (anonParams: AnonymizationParams) (tableName: string) =
  match anonParams.TableSettings.TryFind(tableName) with
  | None
  | Some { AidColumns = [] } -> Ok(None)
  | Some { AidColumns = [ column ] } -> Ok(Some column)
  | Some _ -> Error("Analyze error: Multiple AID columns aren't supported yet")

let analyze connection
            (anonParams: AnonymizationParams)
            (parseTree: ParserTypes.SelectQuery)
            : Async<Result<AnalyzerTypes.Query, string>> =
  asyncResult {
    let! tableName = selectedTableName parseTree.From
    let! aidColumn = aidColumn anonParams tableName
    let! table = Table.getI connection tableName
    let! analyzerQuery = transformQuery table parseTree

    return!
      match aidColumn with
      | None -> Ok analyzerQuery
      | Some aidColumn ->
          result {
            let! (aidColumnIndex, aidColumn) = Table.getColumn table aidColumn
            let aidColumnReference = ColumnReference(aidColumnIndex, aidColumn.Type)
            do! Analysis.QueryValidity.validateQuery aidColumnIndex analyzerQuery
            return addLowCountFilter aidColumnReference analyzerQuery
          }
  }
