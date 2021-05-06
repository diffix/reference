module OpenDiffix.Core.Analyzer

open AnalyzerTypes
open NodeUtils

let rec private mapUnqualifiedColumn (tables: RangeTables) indexOffset name =
  match tables with
  | [] -> failwith $"Column `{name}` not found in the list of target tables"
  | (firstTable, _alias) :: nextTables ->
      match Table.tryFindColumn firstTable name with
      | None -> mapUnqualifiedColumn nextTables (indexOffset + firstTable.Columns.Length) name
      | Some (index, column) -> ColumnReference(index + indexOffset, column.Type)

let rec private mapQualifiedColumn (tables: RangeTables) indexOffset tableName columnName =
  match tables with
  | [] -> failwith $"Table `{tableName}` not found in the list of target tables"
  | (firstTable, alias) :: _ when String.equalsI alias tableName ->
      columnName
      |> Table.findColumn firstTable
      |> fun (index, column) -> ColumnReference(index + indexOffset, column.Type)
  | (firstTable, _alias) :: nextTables ->
      mapQualifiedColumn nextTables (indexOffset + firstTable.Columns.Length) tableName columnName

let private mapColumn tables tableName columnName =
  match tableName with
  | Some tableName -> mapQualifiedColumn tables 0 tableName columnName
  | None -> mapUnqualifiedColumn tables 0 columnName

let rec functionExpression tables fn children =
  let args = children |> List.map (mapExpression tables)
  FunctionExpr(fn, args)

and mapExpression tables parsedExpression =
  match parsedExpression with
  | ParserTypes.Identifier (tableName, columnName) -> mapColumn tables tableName columnName
  | ParserTypes.Expression.Integer value -> Value.Integer(int64 value) |> Constant
  | ParserTypes.Expression.Float value -> Value.Real value |> Constant
  | ParserTypes.Expression.String value -> Value.String value |> Constant
  | ParserTypes.Expression.Boolean value -> Value.Boolean value |> Constant
  | ParserTypes.Not expr -> functionExpression tables (ScalarFunction Not) [ expr ]
  | ParserTypes.Lt (left, right) -> functionExpression tables (ScalarFunction Lt) [ left; right ]
  | ParserTypes.LtE (left, right) -> functionExpression tables (ScalarFunction LtE) [ left; right ]
  | ParserTypes.Gt (left, right) -> functionExpression tables (ScalarFunction Gt) [ left; right ]
  | ParserTypes.GtE (left, right) -> functionExpression tables (ScalarFunction GtE) [ left; right ]
  | ParserTypes.And (left, right) -> functionExpression tables (ScalarFunction And) [ left; right ]
  | ParserTypes.Or (left, right) -> functionExpression tables (ScalarFunction Or) [ left; right ]
  | ParserTypes.Equals (left, right) -> functionExpression tables (ScalarFunction Equals) [ left; right ]
  | ParserTypes.Function (name, args) ->
      let fn = Function.fromString name
      let fn, childExpressions = mapFunctionExpression tables fn args
      FunctionExpr(fn, childExpressions)
  | other -> failwith $"The expression is not permitted in this context: %A{other}"

and mapFunctionExpression table fn args =
  match fn, args with
  | AggregateFunction (Count, aggregateArgs), [ ParserTypes.Star ] -> AggregateFunction(Count, aggregateArgs), []
  | AggregateFunction (aggregate, aggregateArgs), [ ParserTypes.Distinct expr ] ->
      let childArg = mapExpression table expr
      AggregateFunction(aggregate, { aggregateArgs with Distinct = true }), [ childArg ]
  | _, _ ->
      let childArgs = args |> List.map (mapExpression table)
      fn, childArgs

let expressionName expr =
  match expr with
  | ParserTypes.Identifier (_, columnName) -> columnName
  | ParserTypes.Function (name, _args) -> name
  | _ -> ""

let rec mapSelectedExpression tables selectedExpression =
  match selectedExpression with
  | ParserTypes.As (parsedExpression, parsedAlias) ->
      let alias = parsedAlias |> Option.defaultWith (fun () -> expressionName parsedExpression)
      let expression = parsedExpression |> mapExpression tables
      { Expression = expression; Alias = alias }
  | other -> failwith $"Unexpected expression selected '%A{other}'"

let transformSelectedExpressions tables selectedExpressions =
  selectedExpressions |> List.map (mapSelectedExpression tables)

let booleanTrueExpression = Boolean true |> Constant

let transformExpressionOptionWithDefaultTrue rangeTables optionalExpression =
  optionalExpression
  |> Option.map (mapExpression rangeTables)
  |> Option.defaultValue booleanTrueExpression

let transformGroupByIndex (expressions: Expression list) groupByExpression =
  match groupByExpression with
  | Constant (Integer index) ->
      if index < 1L || index > int64 expressions.Length then
        failwith $"Invalid `GROUP BY` index: {index}"
      else
        expressions |> List.item (int index - 1)
  | _ -> groupByExpression

let transformGroupByIndices (selectedExpressions: TargetEntry list) groupByExpressions =
  let selectedExpressions =
    selectedExpressions
    |> List.map (fun selectedExpression -> selectedExpression.Expression)

  groupByExpressions |> List.map (transformGroupByIndex selectedExpressions)

let rec private collectRangeTables range : RangeTables =
  match range with
  | RangeTable (table, alias) -> [ table, alias ]
  | Join join -> (collectRangeTables join.Left) @ (collectRangeTables join.Right)
  | SubQuery _ -> failwith "Unexpected subquery encountered while collecting tables"

let rec private transformFrom schema from =
  match from with
  | ParserTypes.Table (name, alias) ->
      let alias = alias |> Option.defaultWith (fun () -> name)
      let table = name |> Schema.findTable schema
      RangeTable(table, alias)
  | ParserTypes.Join (joinType, left, right, on) ->
      let left = transformFrom schema left
      let right = transformFrom schema right

      let rangeTables = (collectRangeTables left) @ (collectRangeTables right)
      let condition = transformExpressionOptionWithDefaultTrue rangeTables (Some on)

      Join
        {
          Type = joinType
          Left = left
          Right = right
          On = condition
        }
  | _ -> failwith "Invalid `FROM` clause"

let private validateRangeTables (tables: RangeTables) =
  let aliases = tables |> List.map (fun (_table, alias) -> alias.ToLower())

  if aliases.Length <> (List.distinct aliases).Length then
    failwith "Ambiguous target names in `FROM` clause."

  tables

let transformQuery schema (selectQuery: ParserTypes.SelectQuery) =
  let from = transformFrom schema selectQuery.From
  let rangeTables = from |> collectRangeTables |> validateRangeTables
  let selectedExpressions = transformSelectedExpressions rangeTables selectQuery.Expressions
  let whereClause = transformExpressionOptionWithDefaultTrue rangeTables selectQuery.Where
  let havingClause = transformExpressionOptionWithDefaultTrue rangeTables selectQuery.Having

  let groupBy =
    selectQuery.GroupBy
    |> List.map (mapExpression rangeTables)
    |> transformGroupByIndices selectedExpressions

  SelectQuery
    {
      TargetList = selectedExpressions
      Where = whereClause
      From = from
      GroupingSets = [ GroupingSet groupBy ]
      Having = havingClause
      OrderBy = []
    },
  rangeTables

let rewriteToDiffixAggregate aidColumnsExpression query =
  query
  |> map
       (function
       | FunctionExpr (AggregateFunction (Count, opts), args) ->
           FunctionExpr(AggregateFunction(DiffixCount, opts), aidColumnsExpression :: args)
       | expression -> expression)

let rec scalarExpression =
  function
  | FunctionExpr (AggregateFunction _, _) -> false
  | FunctionExpr (_, args) -> List.forall scalarExpression args
  | _ -> true

let selectExpressionToColumn selectExpression =
  {
    Name = selectExpression.Alias
    Type = Expression.typeOf selectExpression.Expression
  }

let selectColumnsFromQuery columnIndices innerQuery =
  let selectedColumns =
    columnIndices
    |> List.map (fun index ->
      let column = innerQuery.TargetList |> List.item index
      let columnType = Expression.typeOf column.Expression

      { column with
          Expression = ColumnReference(index, columnType)
      }
    )

  {
    TargetList = selectedColumns
    From = SubQuery(SelectQuery innerQuery)
    Where = booleanTrueExpression
    GroupingSets = []
    OrderBy = []
    Having = booleanTrueExpression
  }

let addLowCountFilter aidColumnsExpression query =
  query
  |> map (fun selectQuery ->
    let lowCountAggregate =
      FunctionExpr(AggregateFunction(DiffixLowCount, AggregateOptions.Default), [ aidColumnsExpression ])

    let lowCountFilter = FunctionExpr(ScalarFunction Not, [ lowCountAggregate ])

    if selectQuery.GroupingSets = [ GroupingSet [] ] then
      let selectedExpressions =
        selectQuery.TargetList
        |> List.map (fun selectedColumn -> selectedColumn.Expression)

      if selectedExpressions |> List.forall scalarExpression then
        let bucketCount =
          FunctionExpr(AggregateFunction(DiffixCount, AggregateOptions.Default), [ aidColumnsExpression ])

        let bucketExpand = FunctionExpr(SetFunction GenerateSeries, [ bucketCount ])

        { selectQuery with
            TargetList = { Expression = bucketExpand; Alias = "" } :: selectQuery.TargetList
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

let rec private collectAids (anonParams: AnonymizationParams) (tables: RangeTables) indexOffset =
  match tables with
  | [] -> []
  | (firstTable, _alias) :: nextTables ->
      let remainingTablesAidColumns = collectAids anonParams nextTables (indexOffset + firstTable.Columns.Length)

      match anonParams.TableSettings.TryFind(firstTable.Name) with
      | None -> remainingTablesAidColumns
      | Some { AidColumns = aidColumns } ->
          let currentTableAidColumns =
            aidColumns
            |> List.map (Table.findColumn firstTable)
            |> List.map (fun (index, column) -> (index + indexOffset, column))

          let otherColumns = remainingTablesAidColumns
          currentTableAidColumns @ otherColumns

let rec private findAids (anonParams: AnonymizationParams) (tables: RangeTables) = collectAids anonParams tables 0

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let analyze context (parseTree: ParserTypes.SelectQuery) : Query =
  let schema = context.DataProvider.GetSchema()
  let query, rangeTables = transformQuery schema parseTree
  let aidColumns = findAids context.AnonymizationParams rangeTables

  if List.isEmpty aidColumns then
    query
  else
    QueryValidator.validateQuery query

    let aidColumnsExpression =
      aidColumns
      |> List.map (fun (index, column) -> ColumnReference(index, column.Type))
      |> ListExpr

    query
    |> rewriteToDiffixAggregate aidColumnsExpression
    |> addLowCountFilter aidColumnsExpression
