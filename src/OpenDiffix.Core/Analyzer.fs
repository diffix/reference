module rec OpenDiffix.Core.Analyzer

open AnalyzerTypes
open NodeUtils

type private RangeColumn = { RangeName: string; ColumnName: string; Type: ExpressionType; IsAid: bool }

/// Attempts to find a unique match for a given column name (and optional range name) in the query range.
/// Returns a `RangeColumn` and its index in the range.
let rec private resolveColumn rangeColumns tableName columnName =
  let label, condition =
    match tableName with
    | Some tableName ->
      $"%s{tableName}.%s{columnName}",
      fun (_, col) ->
        String.equalsI col.RangeName tableName
        && String.equalsI col.ColumnName columnName
    | None -> columnName, (fun (_, col) -> String.equalsI col.ColumnName columnName)

  let candidates = rangeColumns |> List.indexed |> List.filter condition

  match candidates with
  | [ rangeColumn ] -> rangeColumn
  | [] -> failwith $"Column %s{label} not found in query range"
  | _ -> failwith $"Ambiguous reference to column %s{label} in query range"

let private expressionName parsedExpr =
  match parsedExpr with
  | ParserTypes.Identifier (_, columnName) -> columnName
  | ParserTypes.Function (name, _args) -> name
  | _ -> ""

let private constTrue = Constant(Boolean true)

// ----------------------------------------------------------------
// Expression building
// ----------------------------------------------------------------

let private mapColumnReference rangeColumns tableName columnName =
  let index, column = resolveColumn rangeColumns tableName columnName
  ColumnReference(index, column.Type)

let private mapFunctionExpression rangeColumns fn parsedArgs =
  (match fn, parsedArgs with
   | AggregateFunction (Count, aggregateArgs), [ ParserTypes.Star ] -> //
     AggregateFunction(Count, aggregateArgs), []
   | AggregateFunction (aggregate, aggregateArgs), [ ParserTypes.Distinct expr ] ->
     let arg = mapExpression rangeColumns expr
     AggregateFunction(aggregate, { aggregateArgs with Distinct = true }), [ arg ]
   | AggregateFunction (fn, aggregateArgs), parsedArgs when List.contains fn [ DiffixCount; DiffixLowCount ] -> //
     let args = parsedArgs |> List.map (mapExpression rangeColumns)
     AggregateFunction(fn, aggregateArgs), [ ListExpr args ]
   | _ ->
     let args = parsedArgs |> List.map (mapExpression rangeColumns)
     fn, args)
  |> FunctionExpr

let private mapExpression rangeColumns parsedExpr =
  match parsedExpr with
  | ParserTypes.Identifier (tableName, columnName) -> mapColumnReference rangeColumns tableName columnName
  | ParserTypes.Expression.Integer value -> Value.Integer(int64 value) |> Constant
  | ParserTypes.Expression.Float value -> Value.Real value |> Constant
  | ParserTypes.Expression.String value -> Value.String value |> Constant
  | ParserTypes.Expression.Boolean value -> Value.Boolean value |> Constant
  | ParserTypes.Not expr -> mapFunctionExpression rangeColumns (ScalarFunction Not) [ expr ]
  | ParserTypes.Lt (left, right) -> mapFunctionExpression rangeColumns (ScalarFunction Lt) [ left; right ]
  | ParserTypes.LtE (left, right) -> mapFunctionExpression rangeColumns (ScalarFunction LtE) [ left; right ]
  | ParserTypes.Gt (left, right) -> mapFunctionExpression rangeColumns (ScalarFunction Gt) [ left; right ]
  | ParserTypes.GtE (left, right) -> mapFunctionExpression rangeColumns (ScalarFunction GtE) [ left; right ]
  | ParserTypes.And (left, right) -> mapFunctionExpression rangeColumns (ScalarFunction And) [ left; right ]
  | ParserTypes.Or (left, right) -> mapFunctionExpression rangeColumns (ScalarFunction Or) [ left; right ]
  | ParserTypes.Equals (left, right) -> mapFunctionExpression rangeColumns (ScalarFunction Equals) [ left; right ]
  | ParserTypes.IsNull expr -> mapFunctionExpression rangeColumns (ScalarFunction IsNull) [ expr ]
  | ParserTypes.Function (name, args) ->
    let fn = Function.fromString name
    mapFunctionExpression rangeColumns fn args
  | other -> failwith $"The expression is not permitted in this context: %A{other}"

let private mapFilterExpression rangeColumns optionalExpr =
  optionalExpr
  |> Option.map (mapExpression rangeColumns)
  |> Option.defaultValue constTrue

let private mapTargetEntry rangeColumns parsedExpr =
  match parsedExpr with
  | ParserTypes.As (parsedExpression, parsedAlias) ->
    let alias = parsedAlias |> Option.defaultValue (expressionName parsedExpression)
    let expression = parsedExpression |> mapExpression rangeColumns
    [ { Expression = expression; Alias = alias; Tag = RegularTargetEntry } ]
  | ParserTypes.Star ->
    rangeColumns
    |> List.mapi (fun index rangeColumn ->
      {
        Expression = ColumnReference(index, rangeColumn.Type)
        Alias = rangeColumn.ColumnName
        Tag = RegularTargetEntry
      }
    )
  | other -> failwith $"Unexpected expression selected '%A{other}'"

// ----------------------------------------------------------------
// Group by
// ----------------------------------------------------------------

let private mapGroupByIndex (expressions: Expression list) groupByExpression =
  match groupByExpression with
  | Constant (Integer index) ->
    if index < 1L || index > int64 expressions.Length then
      failwith $"Invalid `GROUP BY` index: {index}"
    else
      expressions |> List.item (int index - 1)
  | _ -> groupByExpression

let private mapGroupByIndices (targetList: TargetEntry list) groupByExpressions =
  let selectedExpressions = targetList |> List.map (fun targetEntry -> targetEntry.Expression)
  groupByExpressions |> List.map (mapGroupByIndex selectedExpressions)

// ----------------------------------------------------------------
// Query range
// ----------------------------------------------------------------

/// Returns the `RangeColumn`s in scope of a query.
let rec private collectRangeColumns anonParams range =
  match range with
  | SubQuery (query, queryAlias) ->
    query.TargetList
    |> List.map (fun targetEntry ->
      {
        RangeName = queryAlias
        ColumnName = targetEntry.Alias
        Type = Expression.typeOf targetEntry.Expression
        IsAid = (targetEntry.Tag = AidTargetEntry)
      }
    )
  | Join { Left = left; Right = right } -> //
    collectRangeColumns anonParams left @ collectRangeColumns anonParams right
  | RangeTable (table, alias) ->
    table.Columns
    |> List.map (fun col ->
      {
        RangeName = alias
        ColumnName = col.Name
        Type = col.Type
        IsAid = AnonymizationParams.isAidColumn anonParams table.Name col.Name
      }
    )

let rec private mapQueryRange schema anonParams parsedRange =
  match parsedRange with
  | ParserTypes.Table (name, alias) ->
    let alias = alias |> Option.defaultValue name
    let table = name |> Schema.findTable schema
    RangeTable(table, alias)
  | ParserTypes.Join (joinType, left, right, on) ->
    let left = mapQueryRange schema anonParams left
    let right = mapQueryRange schema anonParams right

    let rangeColumns = collectRangeColumns anonParams left @ collectRangeColumns anonParams right
    let condition = mapFilterExpression rangeColumns (Some on)

    Join { Type = joinType; Left = left; Right = right; On = condition }
  | ParserTypes.SubQuery (subQuery, alias) -> SubQuery(mapQuery schema anonParams true subQuery, alias)
  | _ -> failwith "Invalid `FROM` clause"

// ----------------------------------------------------------------
// Query
// ----------------------------------------------------------------

/// Returns the list of all aliases in range.
let private rangeEntries range =
  match range with
  | SubQuery (_query, alias) -> [ alias ]
  | RangeTable (_table, alias) -> [ alias ]
  | Join { Left = left; Right = right } -> rangeEntries left @ rangeEntries right

/// Verifies that there are no duplicate names in range.
let private validateRange range =
  let aliases = range |> rangeEntries |> List.map String.toLower

  if aliases.Length <> (List.distinct aliases).Length then
    failwith "Ambiguous target names in `FROM` clause."

  range

let private mapQuery schema anonParams isSubQuery (selectQuery: ParserTypes.SelectQuery) =
  let range = mapQueryRange schema anonParams selectQuery.From |> validateRange
  let rangeColumns = collectRangeColumns anonParams range
  let targetList = selectQuery.Expressions |> List.collect (mapTargetEntry rangeColumns)
  let whereClause = mapFilterExpression rangeColumns selectQuery.Where
  let havingClause = mapFilterExpression rangeColumns selectQuery.Having

  let groupBy =
    selectQuery.GroupBy
    |> List.map (mapExpression rangeColumns)
    |> mapGroupByIndices targetList

  let isAggregating = not (List.isEmpty groupBy && List.isEmpty (collectAggregates targetList))

  let aidTargets =
    if isSubQuery then
      // Subqueries will export their AIDs to outer queries.
      rangeColumns
      |> List.indexed
      |> List.filter (fun (_i, col) -> col.IsAid)
      |> List.mapi (fun aidIndex (colIndex, col) ->
        {
          Expression =
            if isAggregating then
              FunctionExpr(
                AggregateFunction(MergeAids, AggregateOptions.Default),
                [ ColumnReference(colIndex, col.Type) ]
              )
            else
              ColumnReference(colIndex, col.Type)
          Alias = $"__aid_{aidIndex}"
          Tag = AidTargetEntry
        }
      )
    else
      []

  {
    TargetList = targetList @ aidTargets
    Where = whereClause
    From = range
    GroupBy = groupBy
    Having = havingClause
    OrderBy = []
    Limit = selectQuery.Limit
  }

// ----------------------------------------------------------------
// Rewriting
// ----------------------------------------------------------------

/// Builds a list expression which is used for accessing all AIDs in scope of a query.
let private makeAidColumnsExpression rangeColumns =
  rangeColumns
  |> List.indexed
  |> List.filter (fun (_i, col) -> col.IsAid)
  |> List.map (fun (i, col) -> ColumnReference(i, col.Type))
  |> ListExpr

let private rewriteToDiffixAggregate aidColumnsExpression query =
  let rec exprMapper expr =
    match expr with
    | FunctionExpr (AggregateFunction (Count, opts), args) ->
      FunctionExpr(AggregateFunction(DiffixCount, opts), aidColumnsExpression :: args)
    | other -> other |> map exprMapper

  query |> map exprMapper

let private addLowCountFilter aidColumnsExpression selectQuery =
  let lowCountFilter =
    Expression.makeAggregate DiffixLowCount [ aidColumnsExpression ]
    |> Expression.makeNot

  if List.isEmpty selectQuery.GroupBy then
    let selectedExpressions =
      selectQuery.TargetList
      |> List.map (fun selectedColumn -> selectedColumn.Expression)

    if selectedExpressions |> List.forall Expression.isScalar then
      // Non-grouping & non-aggregating query; group implicitly and expand
      let bucketCount = Expression.makeAggregate DiffixCount [ aidColumnsExpression ]
      let bucketExpand = Expression.makeSetFunction GenerateSeries [ bucketCount ]

      { selectQuery with
          TargetList =
            { Expression = bucketExpand; Alias = ""; Tag = JunkTargetEntry }
            :: selectQuery.TargetList
          GroupBy = selectedExpressions
          Having = Expression.makeAnd lowCountFilter selectQuery.Having
      }
    else
      // Non-grouping aggregate query; do nothing
      selectQuery
  else
    // Grouping query; add LCF to HAVING
    { selectQuery with
        Having = Expression.makeAnd lowCountFilter selectQuery.Having
    }

let private rewriteQuery anonParams (selectQuery: SelectQuery) =
  let rangeColumns = collectRangeColumns anonParams selectQuery.From
  let aidColumnsExpression = makeAidColumnsExpression rangeColumns
  let isAnonymizing = aidColumnsExpression |> Expression.unwrapListExpr |> List.isEmpty |> not

  if isAnonymizing then
    QueryValidator.validateQuery selectQuery

    selectQuery
    |> rewriteToDiffixAggregate aidColumnsExpression
    |> addLowCountFilter aidColumnsExpression
  else
    selectQuery

// ----------------------------------------------------------------
// Noise layers
// ----------------------------------------------------------------

let private collectGroupingExpressions selectQuery : Expression list =
  if List.isEmpty selectQuery.GroupBy then
    selectQuery.TargetList
    |> List.map (fun selectedColumn -> selectedColumn.Expression)
    |> List.filter Expression.isScalar
  else
    selectQuery.GroupBy

let rec private basicSeedMaterial rangeColumns expression =
  match expression with
  | FunctionExpr (ScalarFunction Cast, [ expression; _ ]) -> basicSeedMaterial rangeColumns expression
  | ColumnReference (index, _type) ->
    let rangeColumn = List.item index rangeColumns
    $"%s{rangeColumn.RangeName}.%s{rangeColumn.ColumnName}"
  | Constant (String value) -> value
  | Constant (Integer value) -> value.ToString()
  | Constant (Real value) -> value.ToString()
  | Constant (Boolean value) -> value.ToString()
  | _ -> failwith "Unsupported expression used for defining buckets."

let private functionSeedMaterial =
  function
  | Substring -> "substring"
  | Ceil -> "ceil"
  | Floor -> "floor"
  | Round -> "round"
  | WidthBucket -> "width_bucket"
  | Concat -> "concat"
  | _ -> failwith "Unsupported function used for defining buckets."

let rec private collectSeedMaterials rangeColumns expression =
  match expression with
  | FunctionExpr (ScalarFunction Cast, [ expression; _type ]) -> collectSeedMaterials rangeColumns expression
  | FunctionExpr (ScalarFunction fn, args) ->
    (functionSeedMaterial fn) :: (List.map (basicSeedMaterial rangeColumns) args)
  | _ -> [ basicSeedMaterial rangeColumns expression ]

let private computeNoiseLayers anonParams query =
  let rangeColumns = collectRangeColumns anonParams query.From

  let sqlSeed =
    query
    |> collectGroupingExpressions
    |> Seq.collect (collectSeedMaterials rangeColumns)
    |> String.join ","
    |> System.Text.Encoding.UTF8.GetBytes
    |> Hash.bytes

  { BucketSeed = sqlSeed }

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let analyze context (parseTree: ParserTypes.SelectQuery) : Query =
  let schema = context.DataProvider.GetSchema()
  let anonParams = context.AnonymizationParams
  let query = mapQuery schema anonParams false parseTree
  query

let anonymize evaluationContext (query: Query) =
  let executionContext =
    {
      EvaluationContext = evaluationContext
      NoiseLayers = computeNoiseLayers evaluationContext.AnonymizationParams query
    }

  let query = map (rewriteQuery evaluationContext.AnonymizationParams) query
  query, executionContext
