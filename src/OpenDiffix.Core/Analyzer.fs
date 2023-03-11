module rec OpenDiffix.Core.Analyzer

open AnalyzerTypes
open NodeUtils

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

let rec private expressionName parsedExpr =
  match parsedExpr with
  | ParserTypes.Identifier(_, columnName) -> columnName
  | ParserTypes.Function("cast", arg :: _) -> expressionName arg
  | ParserTypes.Function(name, _args) -> name
  | _ -> ""

let private constTrue = Constant(Boolean true)

// ----------------------------------------------------------------
// Expression building
// ----------------------------------------------------------------

let private mapColumnReference rangeColumns tableName columnName =
  let index, column = resolveColumn rangeColumns tableName columnName
  ColumnReference(index, column.Type)

let private mapFunctionExpression rangeColumns fn parsedArgs =
  let mapAids parsedAids =
    parsedAids |> List.map (mapExpression rangeColumns) |> ListExpr

  let mapAvg options parsedArgs sumFunction countFunction =
    let sum = mapFunctionExpression rangeColumns (AggregateFunction(sumFunction, options)) parsedArgs
    let count = mapFunctionExpression rangeColumns (AggregateFunction(countFunction, options)) parsedArgs
    // `nullif` is necessary to handle cases where large negative noise brings `count`
    // down to 0 during global aggregation.
    let countDenominator =
      FunctionExpr(ScalarFunction ScalarFunction.NullIf, [ count; Constant(Integer 0L) ])

    let castSum = FunctionExpr(ScalarFunction ScalarFunction.Cast, [ sum; Constant(String "real") ])
    ScalarFunction ScalarFunction.Divide, [ castSum; countDenominator ]

  (match fn, parsedArgs with
   | AggregateFunction(Count, options), [ ParserTypes.Star ] -> //
     AggregateFunction(Count, options), []

   | AggregateFunction(CountNoise, options), [ ParserTypes.Star ] -> //
     AggregateFunction(CountNoise, options), []

   | AggregateFunction(Avg, options), parsedArgs -> //
     mapAvg options parsedArgs Sum Count

   | AggregateFunction(AvgNoise, options), parsedArgs -> //
     mapAvg options parsedArgs SumNoise Count

   | AggregateFunction(DiffixLowCount, options), parsedAids ->
     AggregateFunction(DiffixLowCount, options), [ mapAids parsedAids ]

   | AggregateFunction(DiffixCount, options), parsedArg :: parsedAids ->
     let options, args =
       match parsedArg with
       | ParserTypes.Star -> options, []
       | ParserTypes.Distinct parsedExpr -> { options with Distinct = true }, [ mapExpression rangeColumns parsedExpr ]
       | parsedExpr -> options, [ mapExpression rangeColumns parsedExpr ]

     AggregateFunction(DiffixCount, options), mapAids parsedAids :: args

   | AggregateFunction(DiffixCountNoise, options), parsedArg :: parsedAids ->
     let options, args =
       match parsedArg with
       | ParserTypes.Star -> options, []
       | ParserTypes.Distinct parsedExpr -> { options with Distinct = true }, [ mapExpression rangeColumns parsedExpr ]
       | parsedExpr -> options, [ mapExpression rangeColumns parsedExpr ]

     AggregateFunction(DiffixCountNoise, options), mapAids parsedAids :: args

   | AggregateFunction(DiffixSum, options), parsedArg :: parsedAids ->
     let options, args =
       match parsedArg with
       | ParserTypes.Distinct parsedExpr -> { options with Distinct = true }, [ mapExpression rangeColumns parsedExpr ]
       | parsedExpr -> options, [ mapExpression rangeColumns parsedExpr ]

     AggregateFunction(DiffixSum, options), mapAids parsedAids :: args

   | AggregateFunction(DiffixSumNoise, options), parsedArg :: parsedAids ->
     let options, args =
       match parsedArg with
       | ParserTypes.Distinct parsedExpr -> { options with Distinct = true }, [ mapExpression rangeColumns parsedExpr ]
       | parsedExpr -> options, [ mapExpression rangeColumns parsedExpr ]

     AggregateFunction(DiffixSumNoise, options), mapAids parsedAids :: args

   | AggregateFunction(DiffixAvg, options), parsedArgs -> //
     mapAvg options parsedArgs DiffixSum DiffixCount

   | AggregateFunction(DiffixAvgNoise, options), parsedArgs -> //
     mapAvg options parsedArgs DiffixSumNoise DiffixCount

   | AggregateFunction(DiffixCountHistogram, options), parsedAidIndex :: parsedBinSize :: parsedAids ->
     AggregateFunction(DiffixCountHistogram, options),
     [
       mapAids parsedAids
       mapExpression rangeColumns parsedAidIndex
       mapExpression rangeColumns parsedBinSize
     ]

   | AggregateFunction(aggregate, options), [ ParserTypes.Distinct expr ] ->
     let arg = mapExpression rangeColumns expr
     AggregateFunction(aggregate, { options with Distinct = true }), [ arg ]

   | _ ->
     let args = parsedArgs |> List.map (mapExpression rangeColumns)
     fn, args)
  |> FunctionExpr

let private mapExpression rangeColumns parsedExpr =
  match parsedExpr with
  | ParserTypes.Identifier(tableName, columnName) -> mapColumnReference rangeColumns tableName columnName
  | ParserTypes.Expression.Integer value -> Value.Integer(int64 value) |> Constant
  | ParserTypes.Expression.Float value -> Value.Real value |> Constant
  | ParserTypes.Expression.String value -> Value.String value |> Constant
  | ParserTypes.Expression.Boolean value -> Value.Boolean value |> Constant
  | ParserTypes.Not expr -> mapFunctionExpression rangeColumns (ScalarFunction Not) [ expr ]
  | ParserTypes.Lt(left, right) -> mapFunctionExpression rangeColumns (ScalarFunction Lt) [ left; right ]
  | ParserTypes.LtE(left, right) -> mapFunctionExpression rangeColumns (ScalarFunction LtE) [ left; right ]
  | ParserTypes.Gt(left, right) -> mapFunctionExpression rangeColumns (ScalarFunction Gt) [ left; right ]
  | ParserTypes.GtE(left, right) -> mapFunctionExpression rangeColumns (ScalarFunction GtE) [ left; right ]
  | ParserTypes.And(left, right) -> mapFunctionExpression rangeColumns (ScalarFunction And) [ left; right ]
  | ParserTypes.Or(left, right) -> mapFunctionExpression rangeColumns (ScalarFunction Or) [ left; right ]
  | ParserTypes.Equals(left, right) -> mapFunctionExpression rangeColumns (ScalarFunction Equals) [ left; right ]
  | ParserTypes.IsNull expr -> mapFunctionExpression rangeColumns (ScalarFunction IsNull) [ expr ]
  | ParserTypes.Function(name, args) ->
    let fn = Function.fromString name
    mapFunctionExpression rangeColumns fn args
  | other -> failwith $"The expression is not permitted in this context: %A{other}"

let private mapFilterExpression rangeColumns optionalExpr =
  optionalExpr
  |> Option.map (mapExpression rangeColumns)
  |> Option.defaultValue constTrue

let private mapTargetEntry rangeColumns parsedExpr =
  match parsedExpr with
  | ParserTypes.As(parsedExpression, parsedAlias) ->
    let alias = parsedAlias |> Option.defaultValue (expressionName parsedExpression)
    let expression = parsedExpression |> mapExpression rangeColumns
    [ { Expression = expression; Alias = alias; Tag = RegularTargetEntry } ]
  | ParserTypes.Star ->
    rangeColumns
    |> List.indexed
    // Ignore AIDs because they are collected in a separate pass.
    |> List.filter (fun (_index, rangeColumn) -> not rangeColumn.IsAid)
    |> List.map (fun (index, rangeColumn) ->
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
  | Constant(Integer index) ->
    if index < 1L || index > int64 expressions.Length then
      failwith $"Invalid `GROUP BY` index: {index}"
    else
      expressions |> List.item (int index - 1)
  | _ -> groupByExpression

let private mapGroupByIndices (targetList: TargetEntry list) groupByExpressions =
  let selectedExpressions = targetList |> List.map (fun targetEntry -> targetEntry.Expression)
  groupByExpressions |> List.map (mapGroupByIndex selectedExpressions)

// ----------------------------------------------------------------
// Order by
// ----------------------------------------------------------------

let private mapOrderByIndex (expressions: Expression list) orderByExpression =
  match orderByExpression with
  | Constant(Integer index), direction, nullsBehavior ->
    if index < 1L || index > int64 expressions.Length then
      failwith $"Invalid `ORDER BY` index: {index}"
    else
      expressions |> List.item (int index - 1), direction, nullsBehavior
  | _ -> orderByExpression

let private mapOrderByIndices (targetList: TargetEntry list) orderByExpressions =
  let selectedExpressions = targetList |> List.map (fun targetEntry -> targetEntry.Expression)
  orderByExpressions |> List.map (mapOrderByIndex selectedExpressions)

let private interpretDirection direction =
  match direction with
  | ParserTypes.Asc -> Ascending
  | ParserTypes.Desc -> Descending
  | _ -> failwith "Invalid `ORDER BY` clause"

let private interpretNullsBehavior nullsBehavior =
  match nullsBehavior with
  | ParserTypes.NullsFirst -> NullsFirst
  | ParserTypes.NullsLast -> NullsLast
  | _ -> failwith "Invalid `ORDER BY` clause"

let private interpretOrderByExpression rangeColumns expression =
  match expression with
  | ParserTypes.OrderSpec(expression, direction, nullsBehavior) ->
    (mapExpression rangeColumns expression), (interpretDirection direction), (interpretNullsBehavior nullsBehavior)
  | _ -> failwith "Invalid `ORDER BY` clause"

// ----------------------------------------------------------------
// Query range
// ----------------------------------------------------------------

/// Returns the `RangeColumn`s in scope of a query.
let rec private collectRangeColumns anonParams range =
  match range with
  | SubQuery(query, queryAlias) ->
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
  | RangeTable(table, alias) ->
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
  | ParserTypes.Table(name, alias) ->
    let alias = alias |> Option.defaultValue name
    let table = name |> Schema.findTable schema
    RangeTable(table, alias)
  | ParserTypes.Join(joinType, left, right, on) ->
    let left = mapQueryRange schema anonParams left
    let right = mapQueryRange schema anonParams right

    let rangeColumns = collectRangeColumns anonParams left @ collectRangeColumns anonParams right
    let condition = mapFilterExpression rangeColumns (Some on)

    Join { Type = joinType; Left = left; Right = right; On = condition }
  | ParserTypes.SubQuery(subQuery, alias) -> SubQuery(mapQuery schema anonParams true subQuery, alias)
  | _ -> failwith "Invalid `FROM` clause"

// ----------------------------------------------------------------
// Query
// ----------------------------------------------------------------

/// Returns the list of all aliases in range.
let private rangeEntries range =
  match range with
  | SubQuery(_query, alias) -> [ alias ]
  | RangeTable(_table, alias) -> [ alias ]
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

  let simpleOrderBy =
    selectQuery.OrderBy
    |> List.map (interpretOrderByExpression rangeColumns)
    |> mapOrderByIndices targetList
    |> List.map (fun (expression, direction, nullsBehavior) -> OrderBy(expression, direction, nullsBehavior))

  {
    TargetList = targetList
    Where = whereClause
    From = range
    GroupBy = groupBy
    Having = havingClause
    OrderBy = simpleOrderBy
    Limit = selectQuery.Limit
    AnonymizationContext = None
  }

// NOTE: We do not check subqueries, as they aren't supported in conjunction with anonymization.
let private hasAnonymizingAggregators query =
  query
  |> collectAggregates
  |> Seq.exists (
    function
    | FunctionExpr(AggregateFunction(fn, opts), _) -> Aggregator.isAnonymizing (fn, opts)
    | _ -> false
  )

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

let private compileAnonymizingAggregators aidColumnsExpression query =
  let anonymizing =
    function
    | Count -> DiffixCount
    | CountNoise -> DiffixCountNoise
    | Sum -> DiffixSum
    | SumNoise -> DiffixSumNoise
    | Avg -> DiffixAvg
    | AvgNoise -> DiffixAvgNoise
    | other -> other

  let rec exprMapper expr =
    match expr with
    | FunctionExpr(AggregateFunction(CountHistogram, opts), args) ->
      let countedAidExpr = List.head args

      let countedAidIndex =
        aidColumnsExpression
        |> Expression.unwrapListExpr
        |> List.tryFindIndex ((=) countedAidExpr)
        |> function
          | Some index -> Constant(Integer(int64 index))
          | None -> failwith "count_histogram requires an AID argument."

      FunctionExpr(
        AggregateFunction(anonymizing DiffixCountHistogram, opts),
        aidColumnsExpression :: countedAidIndex :: (List.tail args)
      )
    | FunctionExpr(AggregateFunction(agg, opts), args) when
      List.contains agg [ Count; CountNoise; Sum; SumNoise; Avg; AvgNoise ]
      ->
      FunctionExpr(AggregateFunction(anonymizing agg, opts), aidColumnsExpression :: args)
    | other -> other |> map exprMapper

  query |> map exprMapper

let private bucketExpand aidColumnsExpression =
  let bucketCount = Expression.makeAggregate DiffixCount [ aidColumnsExpression ]
  Expression.makeSetFunction GenerateSeries [ bucketCount ]

let private compileAnonymizingQuery aidColumnsExpression selectQuery =
  let selectQuery = compileAnonymizingAggregators aidColumnsExpression selectQuery

  let selectedExpressions =
    selectQuery.TargetList
    |> List.map (fun selectedColumn -> selectedColumn.Expression)

  let noGrouping = List.isEmpty selectQuery.GroupBy
  let noAggregation = selectedExpressions |> List.forall Expression.isScalar

  let selectQuery =
    if noGrouping && noAggregation then
      // Non-aggregating query; group implicitly and expand.
      { selectQuery with
          TargetList =
            {
              Expression = bucketExpand aidColumnsExpression
              Alias = ""
              Tag = JunkTargetEntry
            }
            :: selectQuery.TargetList
          GroupBy = selectedExpressions
      }
    else
      selectQuery

  // Ignore constant expressions during grouped aggregation.
  let groupBy = List.filter (Expression.isConstant >> not) selectQuery.GroupBy

  let having =
    if List.isEmpty groupBy then
      // No need to apply the LCF to the global bucket.
      selectQuery.Having
    else
      // Apply the LCF to the output buckets.
      [ aidColumnsExpression ]
      |> Expression.makeAggregate DiffixLowCount
      |> Expression.makeNot
      |> Expression.makeAnd selectQuery.Having

  { selectQuery with GroupBy = groupBy; Having = having }

let private compileQuery anonParams (query: SelectQuery) =
  let rangeColumns = collectRangeColumns anonParams query.From
  let aidColumnsExpression = makeAidColumnsExpression rangeColumns

  let isAnonymizing =
    anonParams.AccessLevel <> Direct
    && aidColumnsExpression |> Expression.unwrapListExpr |> List.isEmpty |> not

  let query =
    if isAnonymizing then
      QueryValidator.validateAnonymizingQuery anonParams.AccessLevel rangeColumns query
      compileAnonymizingQuery aidColumnsExpression query
    else
      QueryValidator.validateDirectQuery query
      query

  // Noise seeding is needed only for anonymizing aggregators. This includes the aggregators injected
  // in `compileAnonymizingQuery` and explicit use of noisy aggregators like `diffix_low_count`.
  // If there aren't any, we also don't need to do the validations which are done deep in `computeSQLSeed`.
  if hasAnonymizingAggregators query then
    let normalizedBucketExpressions =
      (query.GroupBy @ gatherBucketExpressionsFromFilter query.Where)
      |> Seq.map normalizeBucketExpression

    QueryValidator.validateGeneralizations anonParams.AccessLevel normalizedBucketExpressions

    let sqlSeed = NoiseLayers.computeSQLSeed rangeColumns normalizedBucketExpressions
    let baseLabels = gatherBucketLabelsFromFilter query.Where
    let anonContext = Some { BucketSeed = sqlSeed; BaseLabels = baseLabels }
    { query with AnonymizationContext = anonContext }
  else
    query

let rec private gatherBucketExpressionsFromFilter filter =
  match filter with
  | Constant(Boolean true) -> []
  | FunctionExpr(ScalarFunction And, args) -> args |> List.collect gatherBucketExpressionsFromFilter
  | FunctionExpr(ScalarFunction Eq, [ bucketExpression; Constant _ ]) -> [ bucketExpression ]
  | _ ->
    failwith "Only equalities between a generalization and a constant are allowed as filters in anonymizing queries."

let rec private gatherBucketLabelsFromFilter filter =
  match filter with
  | Constant(Boolean true) -> []
  | FunctionExpr(ScalarFunction And, args) -> args |> List.collect gatherBucketLabelsFromFilter
  | FunctionExpr(ScalarFunction Eq, [ _; Constant bucketLabel ]) -> [ bucketLabel ]
  | _ ->
    failwith "Only equalities between a generalization and a constant are allowed as filters in anonymizing queries."

let rec private normalizeBucketExpression expression =
  match expression with
  | FunctionExpr(ScalarFunction Cast, [ expression; Constant(String "integer") ]) when
    Expression.typeOf expression = RealType
    ->
    FunctionExpr(ScalarFunction RoundBy, [ normalizeBucketExpression expression; 1.0 |> Real |> Constant ])
  | FunctionExpr(ScalarFunction Cast, [ expression; _type ]) -> normalizeBucketExpression expression
  | FunctionExpr(ScalarFunction fn, args) ->
    let fn, extraArgs =
      match fn with
      | Ceil -> CeilBy, [ 1.0 |> Real |> Constant ]
      | Floor -> FloorBy, [ 1.0 |> Real |> Constant ]
      | Round -> RoundBy, [ 1.0 |> Real |> Constant ]
      | _ -> fn, []

    FunctionExpr(ScalarFunction fn, List.map normalizeBucketExpression args @ extraArgs)
  | _ -> expression

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let analyze queryContext (parseTree: ParserTypes.SelectQuery) : Query =
  let schema = queryContext.DataProvider.GetSchema()
  let anonParams = queryContext.AnonymizationParams
  let query = mapQuery schema anonParams false parseTree
  query

let rec compile (queryContext: QueryContext) (query: Query) =
  query
  |> map (compile queryContext)
  |> compileQuery queryContext.AnonymizationParams
