module OpenDiffix.Core.Analyzer

open FsToolkit.ErrorHandling
open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes
open OpenDiffix.Core.AnonymizerTypes

let rec private mapUnqualifiedColumn tables indexOffset name =
  match tables with
  | [] -> Error $"Column `{name}` not found in the list of target tables"
  | firstTable :: nextTables ->
      match Table.tryGetColumnI firstTable name with
      | None -> mapUnqualifiedColumn nextTables (indexOffset + firstTable.Columns.Length) name
      | Some (index, column) -> ColumnReference(index + indexOffset, column.Type) |> Ok

let rec private mapQualifiedColumn (tables: Table list) indexOffset tableName columnName =
  match tables with
  | [] -> Error $"Table `{tableName}` not found in the list of target tables"
  | firstTable :: _ when firstTable.Name = tableName ->
      columnName
      |> Table.getColumnI firstTable
      |> Result.bind (fun (index, column) -> ColumnReference(index + indexOffset, column.Type) |> Ok)
  | firstTable :: nextTables ->
      mapQualifiedColumn nextTables (indexOffset + firstTable.Columns.Length) tableName columnName

let private mapColumn tables tableName columnName =
  match tableName with
  | None -> mapUnqualifiedColumn tables 0 columnName
  | Some tableName -> mapQualifiedColumn tables 0 tableName columnName

let rec functionExpression tables fn children =
  children
  |> List.map (mapExpression tables)
  |> List.sequenceResultM
  |> Result.map (fun children -> FunctionExpr(fn, children))

and mapExpression tables parsedExpression =
  match parsedExpression with
  | ParserTypes.Identifier (tableName, columnName) -> mapColumn tables tableName columnName
  | ParserTypes.Expression.Integer value -> Value.Integer(int64 value) |> Constant |> Ok
  | ParserTypes.Expression.Float value -> Value.Real value |> Constant |> Ok
  | ParserTypes.Expression.String value -> Value.String value |> Constant |> Ok
  | ParserTypes.Expression.Boolean value -> Value.Boolean value |> Constant |> Ok
  | ParserTypes.Not expr -> functionExpression tables (ScalarFunction Not) [ expr ]
  | ParserTypes.Lt (left, right) -> functionExpression tables (ScalarFunction Lt) [ left; right ]
  | ParserTypes.LtE (left, right) -> functionExpression tables (ScalarFunction LtE) [ left; right ]
  | ParserTypes.Gt (left, right) -> functionExpression tables (ScalarFunction Gt) [ left; right ]
  | ParserTypes.GtE (left, right) -> functionExpression tables (ScalarFunction GtE) [ left; right ]
  | ParserTypes.And (left, right) -> functionExpression tables (ScalarFunction And) [ left; right ]
  | ParserTypes.Or (left, right) -> functionExpression tables (ScalarFunction Or) [ left; right ]
  | ParserTypes.Equals (left, right) -> functionExpression tables (ScalarFunction Equals) [ left; right ]
  | ParserTypes.Function (name, args) ->
      result {
        let! fn = Function.FromString name
        let! fn, childExpressions = mapFunctionExpression tables fn args
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

let expressionName =
  function
  | ParserTypes.Identifier (_, columnName) -> columnName
  | ParserTypes.Function (name, _args) -> name
  | _ -> ""

let rec mapSelectedExpression tables selectedExpression: Result<SelectExpression, string> =
  match selectedExpression with
  | ParserTypes.As (parsedExpression, parsedAlias) ->
      let alias = parsedAlias |> Option.defaultWith (fun () -> expressionName parsedExpression)

      parsedExpression
      |> mapExpression tables
      |> Result.bind (fun expression -> { Expression = expression; Alias = alias } |> Ok)
  | other -> Error $"Unexpected expression selected '%A{other}'"

let transformSelectedExpressions tables selectedExpressions =
  selectedExpressions
  |> List.map (mapSelectedExpression tables)
  |> List.sequenceResultM

let selectedTableName =
  function
  | ParserTypes.Expression.Table name -> Ok name
  | _ -> Error "Only selecting from a single table is supported"

let booleanTrueExpression = Boolean true |> Constant

let transformExpressionOptionWithDefaultTrue targetTables optionalExpression =
  optionalExpression
  |> Option.map (mapExpression targetTables)
  |> Option.defaultValue (Ok booleanTrueExpression)

let transformGroupByIndex (expressions: Expression list) groupByExpression =
  match groupByExpression with
  | Constant (Integer index) ->
      if index < 1L || index > int64 expressions.Length then
        Error $"Invalid `GROUP BY` index: {index}"
      else
        expressions |> List.item (int index - 1) |> Ok
  | _ -> Ok groupByExpression

let transformGroupByIndices (selectedExpressions: SelectExpression list) groupByExpressions =
  let selectedExpressions =
    selectedExpressions
    |> List.map (fun selectedExpression -> selectedExpression.Expression)

  groupByExpressions |> List.map (transformGroupByIndex selectedExpressions)

let rec private collectTargetTables =
  function
  | Table table -> [ table ]
  | Join join -> (collectTargetTables join.Left) @ (collectTargetTables join.Right)
  | Query _ -> failwith "Unexpected subquery encountered while collecting tables"

let rec private transformFrom schema from =
  match from with
  | ParserTypes.Table name -> name |> Table.getI schema |> Result.map Table
  | ParserTypes.Join (joinType, left, right, on) ->
      result {
        let! left = transformFrom schema left
        let! right = transformFrom schema right

        let targetTables = (collectTargetTables left) @ (collectTargetTables right)
        let! condition = transformExpressionOptionWithDefaultTrue targetTables (Some on)

        return Join { Type = joinType; Left = left; Right = right; On = condition }
      }
  | _ -> Error "Invalid `FROM` clause"

let transformQuery schema (selectQuery: ParserTypes.SelectQuery) =
  result {
    let! from = transformFrom schema selectQuery.From
    let targetTables = collectTargetTables from
    let! selectedExpressions = transformSelectedExpressions targetTables selectQuery.Expressions
    let! whereClause = transformExpressionOptionWithDefaultTrue targetTables selectQuery.Where
    let! havingClause = transformExpressionOptionWithDefaultTrue targetTables selectQuery.Having

    let! groupBy =
      selectQuery.GroupBy
      |> List.map (mapExpression targetTables)
      |> List.sequenceResultM

    let! groupBy = groupBy |> transformGroupByIndices selectedExpressions |> List.sequenceResultM

    return
      {
        Columns = selectedExpressions
        Where = whereClause
        From = from
        TargetTables = targetTables
        GroupingSets = [ GroupingSet groupBy ]
        Having = havingClause
        OrderBy = []
      }
  }

let rewriteToDiffixAggregate aidColumnExpression query =
  Query.Map(
    query,
    (function
    | FunctionExpr (AggregateFunction (Count, opts), args) ->
        let args =
          match opts.Distinct, args with
          | true, [ colExpr ] when colExpr = aidColumnExpression -> args
          | true, _ -> failwith "Should have failed validation. Only count(distinct aid) is allowed"
          | false, _ -> aidColumnExpression :: args

        FunctionExpr(AggregateFunction(DiffixCount, opts), args)
    | expression -> expression)
  )

let rec scalarExpression =
  function
  | FunctionExpr (AggregateFunction _, _) -> false
  | FunctionExpr (_, args) -> List.forall scalarExpression args
  | _ -> true

let selectExpressionToColumn selectExpression =
  {
    Name = selectExpression.Alias
    Type = selectExpression.Expression |> Expression.GetType |> Utils.unwrap
  }

let selectColumnsFromQuery columnIndices innerQuery =
  let selectedColumns =
    columnIndices
    |> List.map (fun index ->
      let column = innerQuery.Columns |> List.item index
      let columnType = column.Expression |> Expression.GetType |> Utils.unwrap
      { column with Expression = ColumnReference(index, columnType) }
    )

  let queryTable =
    {
      Name = "dfx_virtual_query"
      Columns = List.map selectExpressionToColumn innerQuery.Columns
    }

  {
    Columns = selectedColumns
    From = Query(SelectQuery innerQuery)
    TargetTables = [ queryTable ]
    Where = booleanTrueExpression
    GroupingSets = []
    OrderBy = []
    Having = booleanTrueExpression
  }

let addLowCountFilter aidColumnExpression query =
  Query.Map(
    query,
    fun selectQuery ->
      let lowCountAggregate =
        FunctionExpr(AggregateFunction(DiffixLowCount, AggregateOptions.Default), [ aidColumnExpression ])

      let lowCountFilter = FunctionExpr(ScalarFunction Not, [ lowCountAggregate ])

      if selectQuery.GroupingSets = [ GroupingSet [] ] then
        let selectedExpressions =
          selectQuery.Columns
          |> List.map (fun selectedColumn -> selectedColumn.Expression)

        if selectedExpressions |> List.forall scalarExpression then
          let bucketCount =
            FunctionExpr(AggregateFunction(DiffixCount, AggregateOptions.Default), [ aidColumnExpression ])

          let bucketExpand = FunctionExpr(SetFunction GenerateSeries, [ bucketCount ])

          { selectQuery with
              Columns = { Expression = bucketExpand; Alias = "" } :: selectQuery.Columns
              GroupingSets = [ GroupingSet selectedExpressions ]
              Having = FunctionExpr(ScalarFunction And, [ lowCountFilter; selectQuery.Having ])
          }
          |> selectColumnsFromQuery [ 1 .. selectedExpressions.Length ]
        else
          selectQuery
      else
        { selectQuery with
            Having = FunctionExpr(ScalarFunction And, [ lowCountFilter; selectQuery.Having ])
        }
  )

let rec private tryfindAid (anonParams: AnonymizationParams) tables =
  match tables with
  | [] -> None
  | firstTable :: nextTables ->
      match anonParams.TableSettings.TryFind(firstTable.Name) with
      | None
      | Some { AidColumns = [] } -> tryfindAid anonParams nextTables
      | Some { AidColumns = column :: _ } -> Some(firstTable, column)

let analyze (dataProvider: IDataProvider)
            (anonParams: AnonymizationParams)
            (parseTree: ParserTypes.SelectQuery)
            : Async<Result<AnalyzerTypes.Query, string>> =
  asyncResult {
    let! schema = dataProvider.GetSchema()
    let! query = transformQuery schema parseTree

    return!
      match tryfindAid anonParams query.TargetTables with
      | None -> query |> SelectQuery |> Ok
      | Some (table, aidColumn) ->
          result {
            let! (aidColumnIndex, aidColumn) = Table.getColumnI table aidColumn
            do! query |> SelectQuery |> Analysis.QueryValidity.validateQuery aidColumnIndex

            let aidColumnExpression = ColumnReference(aidColumnIndex, aidColumn.Type)

            return
              query
              |> SelectQuery
              |> rewriteToDiffixAggregate aidColumnExpression
              |> addLowCountFilter aidColumnExpression
          }
  }
