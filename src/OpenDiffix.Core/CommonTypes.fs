/// Types used across multiple query phases.
[<AutoOpen>]
module rec OpenDiffix.Core.CommonTypes

open System

// ----------------------------------------------------------------
// Values
// ----------------------------------------------------------------

type Value =
  | Null
  | Boolean of bool
  | Integer of int64
  | Real of float
  | String of string
  | Timestamp of DateTime
  | List of Value list

type Row = Value array

// ----------------------------------------------------------------
// Expressions
// ----------------------------------------------------------------

type ExpressionType =
  | BooleanType
  | IntegerType
  | RealType
  | StringType
  | TimestampType
  | ListType of ExpressionType
  | UnknownType of string

type Expression =
  | FunctionExpr of fn: Function * args: Expression list
  | ColumnReference of index: int * exprType: ExpressionType
  | Constant of value: Value
  | ListExpr of Expression list
  override this.ToString() = Expression.toString this

type Function =
  | ScalarFunction of fn: ScalarFunction
  | SetFunction of fn: SetFunction
  | AggregateFunction of fn: AggregateFunction * options: AggregateOptions

type ScalarFunction =
  | Add
  | Subtract
  | Multiply
  | Divide
  | Modulo
  | Equals
  | IsNull
  | Not
  | And
  | Or
  | Lt
  | LtE
  | Gt
  | GtE
  | Round
  | Floor
  | Ceil
  | RoundBy
  | FloorBy
  | CeilBy
  | Abs
  | Length
  | Lower
  | Upper
  | Substring
  | Concat
  | WidthBucket
  | Cast
  | NullIf
  | DateTrunc
  | Extract

type ScalarArgs = Value list

type SetFunction = | GenerateSeries

type AggregateFunction =
  | Count
  | CountNoise
  | DiffixCount
  | DiffixCountNoise
  | DiffixLowCount
  | Sum
  | SumNoise
  | DiffixSum
  | DiffixSumNoise
  | Avg
  | AvgNoise
  | DiffixAvg
  | DiffixAvgNoise
  | CountHistogram
  | DiffixCountHistogram

type AggregateOptions =
  {
    Distinct: bool
    OrderBy: OrderBy list
  }
  static member Default = { Distinct = false; OrderBy = [] }

type OrderBy =
  | OrderBy of Expression * OrderByDirection * OrderByNullsBehavior
  override this.ToString() = OrderBy.toString this

type OrderByDirection =
  | Ascending
  | Descending

type OrderByNullsBehavior =
  | NullsFirst
  | NullsLast

// ----------------------------------------------------------------
// Tables and schemas
// ----------------------------------------------------------------

type Column = { Name: string; Type: ExpressionType }

type Columns = Column list

type Table = { Name: string; Columns: Columns }

type Schema = Table list

type IDataProvider =
  inherit IDisposable
  abstract OpenTable: table: Table * columnIndices: int list -> Row seq
  abstract GetSchema: unit -> Schema

// ----------------------------------------------------------------
// Anonymizer types
// ----------------------------------------------------------------

type AidHash = Hash

type Interval =
  {
    Lower: int
    Upper: int
  }
  static member Default = { Lower = 2; Upper = 5 }

type TableSettings = { AidColumns: string list }

type LowCountParams = { LowThreshold: int; LayerSD: float; LowMeanGap: float }

type SuppressionParams =
  static member Default = { LowThreshold = 2; LayerSD = 1.; LowMeanGap = 2. }

type AccessLevel =
  | PublishTrusted
  | PublishUntrusted
  | Direct

type AdaptiveBucketsParams =
  {
    SingularityLowThreshold: int
    RangeLowThreshold: int
  }
  static member Default = { SingularityLowThreshold = 4; RangeLowThreshold = 8 }

type AnonymizationParams =
  {
    TableSettings: Map<string, TableSettings>
    Salt: byte[]
    Suppression: LowCountParams
    AdaptiveBuckets: AdaptiveBucketsParams
    AccessLevel: AccessLevel
    Strict: bool

    // Count params
    OutlierCount: Interval
    TopCount: Interval
    LayerNoiseSD: float

    RecoverOutliers: bool
    UseAdaptiveBuckets: bool
  }
  static member Default =
    {
      TableSettings = Map.empty
      Salt = [||]
      Suppression = SuppressionParams.Default
      AdaptiveBuckets = AdaptiveBucketsParams.Default
      AccessLevel = PublishTrusted
      Strict = true
      OutlierCount = Interval.Default
      TopCount = Interval.Default
      LayerNoiseSD = 1.0
      RecoverOutliers = true
      UseAdaptiveBuckets = false
    }

// ----------------------------------------------------------------
// Query & Planner
// ----------------------------------------------------------------

type PostAggregationHook = AggregationContext -> AnonymizationContext -> seq<Bucket> -> seq<Bucket>

type QueryContext =
  {
    AnonymizationParams: AnonymizationParams
    DataProvider: IDataProvider
    PostAggregationHooks: PostAggregationHook list
  }

type JoinType =
  | InnerJoin
  | LeftJoin
  | RightJoin
  | FullJoin

[<RequireQualifiedAccess>]
type Plan =
  | Scan of Table * int list
  | Project of Plan * expressions: Expression list
  | ProjectSet of Plan * setGenerator: SetFunction * args: Expression list
  | Filter of Plan * condition: Expression
  | Sort of Plan * OrderBy list
  | Aggregate of Plan * groupingLabels: Expression list * aggregators: Expression list * AnonymizationContext option
  | Join of left: Plan * right: Plan * JoinType * on: Expression
  | Limit of Plan * amount: uint
  | AdaptiveBuckets of Plan * expressions: Expression list * AnonymizationContext
  override this.ToString() = Plan.explain this

// ----------------------------------------------------------------
// Executor
// ----------------------------------------------------------------

type AnonymizationContext =
  {
    BucketSeed: Hash
    BaseLabels: Value list
    AnonymizationParams: AnonymizationParams
  }

type AggregationContext =
  {
    GroupingLabels: Expression array
    Aggregators: (AggregatorSpec * AggregatorArgs) array
  }

// Responsible for accumulating the state of the aggregation function while
// scanning the rows, and delivering the final result as a query result `Value`
type IAggregator =
  // Process an instance of the aggregation function's arguments and transition
  // the aggregator state
  abstract Transition: Value list -> unit
  // Merge state with that of a compatible aggregator
  abstract Merge: IAggregator -> unit
  // Extract the final value of the aggregation function from the state
  // Also merges unused data from outliers into the target aggregator, if any
  abstract Final: AggregationContext * AnonymizationContext option * IAggregator option -> Value

type AggregatorSpec = AggregateFunction * AggregateOptions
type AggregatorArgs = Expression list

type Bucket =
  {
    Group: Row
    mutable RowCount: int
    Aggregators: IAggregator array
    AnonymizationContext: AnonymizationContext option
    Attributes: Dictionary<string, Value>
  }

// ----------------------------------------------------------------
// Constants
// ----------------------------------------------------------------

let NULL_TYPE = UnknownType "null_type"
let MISSING_TYPE = UnknownType "missing_type"
let MIXED_TYPE = UnknownType "mixed_type"

module BucketAttributes =
  let IS_LED_MERGED = "is_led_merged"
  let IS_STAR_BUCKET = "is_star_bucket"

// ----------------------------------------------------------------
// Functions
// ----------------------------------------------------------------

module Row =
  let equalityComparer = LanguagePrimitives.FastGenericEqualityComparer<Row>

module ExpressionType =
  /// Resolves the common type from a list of types.
  let commonType types =
    types
    |> List.distinct
    |> function
      | [] -> MISSING_TYPE
      | [ t ] -> t
      | _ -> MIXED_TYPE

module OrderBy =
  let toString (OrderBy(expr, direction, nullsBehavior)) =
    let directionString =
      match direction with
      | Ascending -> "ASC"
      | Descending -> "DESC"

    let nullsBehaviorString =
      match nullsBehavior with
      | NullsFirst -> "NULLS FIRST"
      | NullsLast -> "NULLS LAST"

    $"{expr} {directionString} {nullsBehaviorString}"

module Expression =
  // Slightly different from Value.toString.
  let rec private valueToString value =
    match value with
    | Null -> "NULL"
    | Boolean b -> b.ToString()
    | Integer i -> i.ToString()
    | Real r -> r.ToString()
    | String s -> String.quoteSingle s
    | Timestamp ts -> ts.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
    | List values -> "[" + (values |> List.map valueToString |> String.joinWithComma) + "]"

  let toString expr =
    match expr with
    | FunctionExpr(AggregateFunction(fn, opts), args) ->
      let argsStr = if List.isEmpty args then "*" else args |> String.join ", "
      let distinct = if opts.Distinct then "DISTINCT " else ""

      let orderBy =
        if List.isEmpty opts.OrderBy then
          ""
        else
          $" WITHIN GROUP (ORDER BY {String.joinWithComma opts.OrderBy})"

      $"{fn}({distinct}{argsStr}){orderBy}"
    | FunctionExpr(ScalarFunction fn, args) -> $"{fn}({String.joinWithComma args})"
    | FunctionExpr(SetFunction fn, args) -> $"{fn}({String.joinWithComma args})"
    | ColumnReference(index, _) ->
      // Without some context we can't know column names.
      $"${index}"
    | Constant value -> valueToString value
    | ListExpr exprs -> $"[{String.joinWithComma exprs}]"

module Function =
  let fromString name =
    match name with
    | "count" -> AggregateFunction(Count, AggregateOptions.Default)
    | "count_noise" -> AggregateFunction(CountNoise, AggregateOptions.Default)
    | "sum_noise" -> AggregateFunction(SumNoise, AggregateOptions.Default)
    | "sum" -> AggregateFunction(Sum, AggregateOptions.Default)
    | "avg" -> AggregateFunction(Avg, AggregateOptions.Default)
    | "avg_noise" -> AggregateFunction(AvgNoise, AggregateOptions.Default)
    | "count_histogram" -> AggregateFunction(CountHistogram, AggregateOptions.Default)
    | "diffix_count" -> AggregateFunction(DiffixCount, AggregateOptions.Default)
    | "diffix_count_noise" -> AggregateFunction(DiffixCountNoise, AggregateOptions.Default)
    | "diffix_low_count" -> AggregateFunction(DiffixLowCount, AggregateOptions.Default)
    | "diffix_sum" -> AggregateFunction(DiffixSum, AggregateOptions.Default)
    | "diffix_sum_noise" -> AggregateFunction(DiffixSumNoise, AggregateOptions.Default)
    | "diffix_avg" -> AggregateFunction(DiffixAvg, AggregateOptions.Default)
    | "diffix_avg_noise" -> AggregateFunction(DiffixAvgNoise, AggregateOptions.Default)
    | "diffix_count_histogram" -> AggregateFunction(DiffixCountHistogram, AggregateOptions.Default)
    | "+" -> ScalarFunction Add
    | "-" -> ScalarFunction Subtract
    | "*" -> ScalarFunction Multiply
    | "/" -> ScalarFunction Divide
    | "%" -> ScalarFunction Modulo
    | "round" -> ScalarFunction Round
    | "ceil" -> ScalarFunction Ceil
    | "floor" -> ScalarFunction Floor
    | "round_by" -> ScalarFunction RoundBy
    | "ceil_by" -> ScalarFunction CeilBy
    | "floor_by" -> ScalarFunction FloorBy
    | "abs" -> ScalarFunction Abs
    | "length" -> ScalarFunction Length
    | "lower" -> ScalarFunction Lower
    | "upper" -> ScalarFunction Upper
    | "substring" -> ScalarFunction Substring
    | "||" -> ScalarFunction Concat
    | "width_bucket" -> ScalarFunction WidthBucket
    | "cast" -> ScalarFunction Cast
    | "nullif" -> ScalarFunction NullIf
    | "date_trunc" -> ScalarFunction DateTrunc
    | "extract"
    | "date_part" -> ScalarFunction Extract
    | other -> failwith $"Unknown function `{other}`"

module Table =
  /// Finds a column along with its index. The index is zero-based.
  let tryFindColumn table columnName =
    table.Columns
    |> List.indexed
    |> List.tryFind (fun (_index, column) -> String.equalsI column.Name columnName)

  /// Finds a column along with its index. The index is zero-based. Fails if column not found.
  let findColumn table columnName =
    columnName
    |> tryFindColumn table
    |> function
      | Some column -> column
      | None -> failwith $"Could not find column `{columnName}` in table `{table.Name}`"

module Schema =
  /// Finds a table by name in the schema.
  let tryFindTable schema tableName =
    schema |> List.tryFind (fun table -> String.equalsI table.Name tableName)

  /// Finds a table by name in the schema. Fails if table not found.
  let findTable schema tableName =
    tableName
    |> tryFindTable schema
    |> function
      | Some table -> table
      | None -> failwith $"Could not find table `{tableName}`."

module AnonymizationParams =
  let private validateInterval interval =
    if interval.Upper < interval.Lower then
      failwith "Invalid interval bounds: (%i{interval.Lower}, %i{interval.Upper})"

  /// Returns whether the given column in the table is an AID column.
  let isAidColumn anonParams tableName columnName =
    anonParams.TableSettings
    |> Map.tryFind tableName
    |> function
      | Some tableSettings -> tableSettings.AidColumns |> List.exists (String.equalsI columnName)
      | None -> false

  /// Fails if any of the anon params does not meet the requirements. Set `strict` to `true` to enforce
  /// checking if the parameters ensure safe minimum level of anonymization, `false` only for basic checks.
  let validate anonParams =
    if anonParams.Strict then
      if anonParams.Suppression.LowThreshold < 2 then
        failwith "Suppression.LowThreshold must be greater than or equal to 2"

      if anonParams.Suppression.LayerSD < 1.0 then
        failwith "Suppression.LayerSD must be greater than or equal to 1.0"

      if anonParams.Suppression.LowMeanGap < 2.0 then
        failwith "Suppression.LowMeanGap must be greater than or equal to 2.0"

      if anonParams.OutlierCount.Lower < 1 then
        failwith "OutlierCount lower bound must be greater than or equal to 1"

      if anonParams.OutlierCount.Upper < 2 then
        failwith "OutlierCount upper bound must be greater than or equal to 2"

      if anonParams.TopCount.Lower < 2 then
        failwith "TopCount lower bound must be greater than or equal to 2"

      if anonParams.TopCount.Upper < 3 then
        failwith "TopCount upper bound must be greater than or equal to 3"

      if anonParams.LayerNoiseSD < 1.0 then
        failwith "LayerNoiseSD must be greater than or equal to 1.0"

      if anonParams.OutlierCount.Upper - anonParams.OutlierCount.Lower < 1 then
        failwith "OutlierCount bounds must differ by at least 1"

      if anonParams.TopCount.Upper - anonParams.TopCount.Lower < 1 then
        failwith "TopCount bounds must differ by at least 1"
    else
      if anonParams.Suppression.LowThreshold < 1 then
        failwith "Suppression.LowThreshold must be greater than or equal to 1"

      if anonParams.Suppression.LayerSD < 0.0 then
        failwith "Suppression.LayerSD must be non-negative"

      if anonParams.Suppression.LowMeanGap < 0.0 then
        failwith "Suppression.LowMeanGap must be non-negative"

      if anonParams.OutlierCount.Lower < 0 then
        failwith "OutlierCount bounds must be non-negative"

      if anonParams.TopCount.Lower <= 0 then
        failwith "TopCount bounds must be positive"

      if anonParams.LayerNoiseSD < 0.0 then
        failwith "LayerNoiseSD must be non-negative"

    validateInterval anonParams.OutlierCount
    validateInterval anonParams.TopCount

module QueryContext =
  let private defaultDataProvider =
    { new IDataProvider with
        member _.OpenTable(_table, _columnIndices) = failwith "No tables in data provider"
        member _.GetSchema() = []
        member _.Dispose() = ()
    }

  let make anonParams dataProvider =
    AnonymizationParams.validate anonParams

    {
      AnonymizationParams = anonParams
      DataProvider = dataProvider
      PostAggregationHooks = []
    }

  let makeDefault () =
    make AnonymizationParams.Default defaultDataProvider

  let makeWithAnonParams anonParams = make anonParams defaultDataProvider

  let makeWithDataProvider dataProvider =
    make AnonymizationParams.Default dataProvider

module Plan =
  let rec columnsCount (plan: Plan) =
    match plan with
    | Plan.Scan(table, _) -> table.Columns.Length
    | Plan.Project(_, expressions) -> expressions.Length
    | Plan.ProjectSet(plan, _, _) -> (columnsCount plan) + 1
    | Plan.Filter(plan, _) -> columnsCount plan
    | Plan.Sort(plan, _) -> columnsCount plan
    | Plan.Aggregate(_, groupingLabels, aggregators, _) -> groupingLabels.Length + aggregators.Length
    | Plan.Join(left, right, _, _) -> columnsCount left + columnsCount right
    | Plan.Limit(plan, _) -> columnsCount plan
    | Plan.AdaptiveBuckets(_, expressions, _) -> expressions.Length

  let private NEWLINE = Environment.NewLine

  let private indent depth =
    if depth > 0 then String.replicate (4 * (depth - 1)) " " else ""

  let private nodeLine depth =
    if depth > 0 then indent depth + " -> " else ""

  let private propLine depth = NEWLINE + indent (depth + 1) + " "

  let rec private toString depth plan =
    let childToString childPlan =
      NEWLINE + (toString (depth + 1) childPlan)

    (nodeLine depth)
    + (
      match plan with
      | Plan.Scan(table, _) -> $"Seq Scan on {table.Name}"
      | Plan.Project(childPlan, expressions) -> $"Project {String.joinWithComma expressions}" + childToString childPlan
      | Plan.ProjectSet(childPlan, fn, args) ->
        $"ProjectSet {fn}({String.joinWithComma args})" + childToString childPlan
      | Plan.Filter(childPlan, condition) -> $"Filter {condition})" + childToString childPlan
      | Plan.Sort(childPlan, orderings) -> $"Sort {String.joinWithComma orderings}" + childToString childPlan
      | Plan.Aggregate(childPlan, groupingLabels, aggregators, anonymizationContext) ->
        "Aggregate"
        + $"{propLine depth}Group Keys: {String.joinWithComma groupingLabels}"
        + $"{propLine depth}Aggregates: {String.joinWithComma aggregators}"
        + $"{propLine depth}AnonymizationContext: {anonymizationContext}"
        + childToString childPlan
      | Plan.Join(leftPlan, rightPlan, joinType, condition) ->
        $"{joinType} on {condition}" + childToString leftPlan + childToString rightPlan
      | Plan.Limit(childPlan, amount) -> $"Limit {amount}" + childToString childPlan
      | Plan.AdaptiveBuckets(childPlan, expressions, anonymizationContext) ->
        "AdaptiveBuckets"
        + $"{propLine depth}Expressions: {String.joinWithComma expressions}"
        + $"{propLine depth}AnonymizationContext: {anonymizationContext}"
        + childToString childPlan
    )

  let explain (plan: Plan) = toString 0 plan

module AggregationContext =
  let private findSingleIndex cond arr =
    arr
    |> Array.indexed
    |> Array.filter (snd >> cond)
    |> function
      | [| index, _item |] -> Some index
      | _ -> None

  let lowCountIndex (aggregationContext: AggregationContext) =
    match findSingleIndex (fun ((fn, _), _) -> fn = DiffixLowCount) aggregationContext.Aggregators with
    | Some index -> index
    | None -> failwith "Cannot find required DiffixLowCount aggregator"
