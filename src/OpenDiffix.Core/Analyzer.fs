module rec OpenDiffix.Core.Analyzer

open System.Text.RegularExpressions
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
   | AggregateFunction (DiffixLowCount, aggregateArgs), parsedAids ->
     let aids = parsedAids |> List.map (mapExpression rangeColumns)
     AggregateFunction(DiffixLowCount, aggregateArgs), [ ListExpr aids ]
   | AggregateFunction (DiffixCount, aggregateArgs), parsedArg :: parsedAids ->
     let aggregateArgs, args =
       match parsedArg with
       | ParserTypes.Star -> aggregateArgs, []
       | ParserTypes.Distinct parsedExpr ->
         { aggregateArgs with Distinct = true }, [ mapExpression rangeColumns parsedExpr ]
       | parsedExpr -> aggregateArgs, [ mapExpression rangeColumns parsedExpr ]

     let aids = parsedAids |> List.map (mapExpression rangeColumns) |> ListExpr
     AggregateFunction(DiffixCount, aggregateArgs), aids :: args
   | AggregateFunction (aggregate, aggregateArgs), [ ParserTypes.Distinct expr ] ->
     let arg = mapExpression rangeColumns expr
     AggregateFunction(aggregate, { aggregateArgs with Distinct = true }), [ arg ]
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
// Order by
// ----------------------------------------------------------------

let private mapOrderByIndex (expressions: Expression list) orderByExpression =
  match orderByExpression with
  | Constant (Integer index), direction, nullsBehavior ->
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
  | ParserTypes.OrderSpec (expression, direction, nullsBehavior) ->
    (mapExpression rangeColumns expression), (interpretDirection direction), (interpretNullsBehavior nullsBehavior)
  | _ -> failwith "Invalid `ORDER BY` clause"

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

  let simpleOrderBy =
    selectQuery.OrderBy
    |> List.map (interpretOrderByExpression rangeColumns)
    |> mapOrderByIndices targetList
    |> List.map (fun (expression, direction, nullsBehavior) -> OrderBy(expression, direction, nullsBehavior))

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
    OrderBy = simpleOrderBy
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

let private bucketExpand aidColumnsExpression =
  let bucketCount = Expression.makeAggregate DiffixCount [ aidColumnsExpression ]
  Expression.makeSetFunction GenerateSeries [ bucketCount ]

let private addLowCountFilter aidColumnsExpression selectQuery =
  let lowCountFilter =
    Expression.makeAggregate DiffixLowCount [ aidColumnsExpression ]
    |> Expression.makeNot

  let selectedExpressions =
    selectQuery.TargetList
    |> List.map (fun selectedColumn -> selectedColumn.Expression)

  let nonConstantExpressions =
    selectedExpressions
    |> List.filter (
      function
      | Constant _ -> false
      | _ -> true
    )

  let doesGrouping = List.isEmpty selectQuery.GroupBy |> not
  let doesAggregation = selectedExpressions |> List.forall Expression.isScalar |> not
  let onlyConstantsSelected = List.isEmpty nonConstantExpressions

  if not doesGrouping && not doesAggregation then
    // Non-grouping, non-aggregate query; group implicitly and expand
    let having =
      if not onlyConstantsSelected then
        Expression.makeAnd lowCountFilter selectQuery.Having
      else
        // All selected expressions are constants; no low-count filter in line with global anonymized count.
        selectQuery.Having

    { selectQuery with
        TargetList =
          {
            Expression = bucketExpand aidColumnsExpression
            Alias = ""
            Tag = JunkTargetEntry
          }
          :: selectQuery.TargetList
        // No need to group implicitly by constants, which are invalid bucket definitions anyway.
        GroupBy = nonConstantExpressions
        Having = having
    }
  else if not doesGrouping && doesAggregation then
    // Non-grouping aggregate query; do nothing.
    selectQuery
  else
    // Grouping query; add LCF to HAVING
    { selectQuery with
        Having = Expression.makeAnd lowCountFilter selectQuery.Having
    }

let private rewriteQuery anonParams (selectQuery: SelectQuery) =
  let rangeColumns = collectRangeColumns anonParams selectQuery.From
  let aidColumnsExpression = makeAidColumnsExpression rangeColumns

  let isAnonymizing =
    anonParams.AccessLevel <> Direct
    && aidColumnsExpression |> Expression.unwrapListExpr |> List.isEmpty |> not

  QueryValidator.validateQuery isAnonymizing anonParams.AccessLevel selectQuery

  if isAnonymizing then
    selectQuery
    |> rewriteToDiffixAggregate aidColumnsExpression
    |> addLowCountFilter aidColumnsExpression
  else
    selectQuery

// ----------------------------------------------------------------
// Noise layers
// ----------------------------------------------------------------

let private basicSeedMaterial rangeColumns expression =
  match expression with
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
  | CeilBy -> "ceil"
  | FloorBy -> "floor"
  | RoundBy -> "round"
  | WidthBucket -> "width_bucket"
  | _ -> failwith "Unsupported function used for defining buckets."

let private collectSeedMaterials rangeColumns expression =
  match expression with
  | FunctionExpr (ScalarFunction fn, args) -> functionSeedMaterial fn :: List.map (basicSeedMaterial rangeColumns) args
  | Constant _ -> failwith "Constant expressions can not be used for defining buckets."
  | _ -> [ basicSeedMaterial rangeColumns expression ]

let rec private normalizeBucketLabelExpression expression =
  match expression with
  | FunctionExpr (ScalarFunction Cast, [ expression; Constant (String "integer") ]) ->
    FunctionExpr(ScalarFunction RoundBy, [ normalizeBucketLabelExpression expression; 1.0 |> Real |> Constant ])
  | FunctionExpr (ScalarFunction Cast, [ expression; _type ]) -> normalizeBucketLabelExpression expression
  | FunctionExpr (ScalarFunction fn, args) ->
    let fn, extraArgs =
      match fn with
      | Ceil -> CeilBy, [ 1.0 |> Real |> Constant ]
      | Floor -> FloorBy, [ 1.0 |> Real |> Constant ]
      | Round -> RoundBy, [ 1.0 |> Real |> Constant ]
      | _ -> fn, []

    FunctionExpr(ScalarFunction fn, List.map normalizeBucketLabelExpression args @ extraArgs)
  | _ -> expression

let private isMoneyStyle arg =
  match arg with
  // "money-style" numbers, i.e. 1, 2, or 5 preceeded by or followed by zeros: ⟨... 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, ...⟩
  | Constant (Real c) -> Regex.IsMatch($"%.15e{c}", "^[125]\.0+e[-+]\d+$")
  | Constant (Integer c) -> Regex.IsMatch($"%i{c}", "^[125]0*$")
  | _ -> false

let private validateBucketLabelExpression accessLevel expression =
  if accessLevel = PublishUntrusted then
    match expression with
    | FunctionExpr (ScalarFunction FloorBy, [ _; arg ]) when isMoneyStyle arg -> ()
    | FunctionExpr (ScalarFunction Substring, [ _; fromArg; _ ]) when fromArg = (1L |> Integer |> Constant) -> ()
    | _ -> failwith "Generalization used in the query is not allowed in untrusted access level"

  expression

let private computeNoiseLayers anonParams query =
  let rangeColumns = collectRangeColumns anonParams query.From

  let sqlSeed =
    query.GroupBy
    |> Seq.map (
      normalizeBucketLabelExpression
      >> validateBucketLabelExpression anonParams.AccessLevel
      >> collectSeedMaterials rangeColumns
      >> String.join ","
    )
    |> Hash.strings 0UL

  { BucketSeed = sqlSeed }

// NOTE: We do not check subqueries, as they aren't supported in conjunction with anonymization.
let private hasAnonymizingAggregates query =
  query
  |> collectAggregates
  |> Seq.exists (
    function
    | FunctionExpr (AggregateFunction (fn, opts), _) -> Aggregator.isAnonymizing (fn, opts)
    | _ -> false
  )

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let analyze queryContext (parseTree: ParserTypes.SelectQuery) : Query =
  let schema = queryContext.DataProvider.GetSchema()
  let anonParams = queryContext.AnonymizationParams
  let query = mapQuery schema anonParams false parseTree
  query

let anonymize queryContext (query: Query) =
  let query = rewriteQuery queryContext.AnonymizationParams query

  // Noise is needed only when we anonymize. If we don't, we also don't need to do the validations which are done deep
  // in `computeNoiseLayers`. This includes anonymization injected in `rewriteQuery` and explicit use of anonymizing
  // aggregates like `diffix_low_count`.
  let noiseLayers =
    if hasAnonymizingAggregates query then
      computeNoiseLayers queryContext.AnonymizationParams query
    else
      NoiseLayers.Default

  query, noiseLayers
