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
  | ListType of ExpressionType
  | UnknownType of string

type Expression =
  | FunctionExpr of fn: Function * args: Expression list
  | ColumnReference of index: int * exprType: ExpressionType
  | Constant of value: Value
  | ListExpr of Expression list

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

type ScalarArgs = Value list

type SetFunction = | GenerateSeries

type AggregateFunction =
  | Count
  | DiffixCount
  | DiffixLowCount
  | Sum
  | MergeAids

type AggregateOptions =
  {
    Distinct: bool
    OrderBy: OrderBy list
  }
  static member Default = { Distinct = false; OrderBy = [] }

type AggregateArgs = seq<Value list>

type OrderBy = OrderBy of Expression * OrderByDirection * OrderByNullsBehavior

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

type Table = { Name: string; Columns: Column list }

type Schema = Table list

type IDataProvider =
  inherit IDisposable
  abstract OpenTable : table: Table * columnIndices: int list -> Row seq
  abstract GetSchema : unit -> Schema

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

type SuppressionParams =
  {
    LowThreshold: int
    SD: float
    LowMeanGap: float
  }
  static member Default = { LowThreshold = 2; SD = 1.; LowMeanGap = 2. }

type AnonymizationParams =
  {
    TableSettings: Map<string, TableSettings>
    Salt: byte []
    Suppression: SuppressionParams

    // Count params
    OutlierCount: Interval
    TopCount: Interval
    NoiseSD: float
  }
  static member Default =
    {
      TableSettings = Map.empty
      Salt = [||]
      Suppression = SuppressionParams.Default
      OutlierCount = Interval.Default
      TopCount = Interval.Default
      NoiseSD = 1.0
    }

// ----------------------------------------------------------------
// Query & Planner
// ----------------------------------------------------------------

type ExecutorHook = ExecutionContext -> Plan -> seq<Row>

type PostAggregationHook = AggregationContext -> seq<AggregationBucket> -> seq<AggregationBucket>

type QueryContext =
  {
    AnonymizationParams: AnonymizationParams
    DataProvider: IDataProvider
    PostAggregationHook: PostAggregationHook
    ExecutorHook: ExecutorHook option
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
  | Aggregate of Plan * groupingLabels: Expression list * aggregators: Expression list
  | Unique of Plan
  | Join of left: Plan * right: Plan * JoinType * on: Expression
  | Append of first: Plan * second: Plan
  | Limit of Plan * amount: uint

// ----------------------------------------------------------------
// Executor
// ----------------------------------------------------------------

type IAggregator =
  abstract Transition : Value list -> unit
  abstract Merge : IAggregator -> unit
  abstract Final : ExecutionContext -> Value

type AggregationContext =
  {
    ExecutionContext: ExecutionContext
    GroupingLabels: Expression array
    Aggregators: (Function * Expression list) array
    LowCountIndex: int option
  }

type AggregationBucket =
  {
    Group: Row
    Aggregators: IAggregator array
    ExecutionContext: ExecutionContext
  }

type NoiseLayers =
  {
    BucketSeed: Hash
  }
  static member Default = { BucketSeed = 0UL }

type ExecutionContext =
  {
    QueryContext: QueryContext
    NoiseLayers: NoiseLayers
  }
  member this.AnonymizationParams = this.QueryContext.AnonymizationParams
  member this.DataProvider = this.QueryContext.DataProvider

// ----------------------------------------------------------------
// Constants
// ----------------------------------------------------------------

let NULL_TYPE = UnknownType "null_type"
let MISSING_TYPE = UnknownType "missing_type"
let MIXED_TYPE = UnknownType "mixed_type"

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

module Function =
  let fromString name =
    match name with
    | "count" -> AggregateFunction(Count, AggregateOptions.Default)
    | "sum" -> AggregateFunction(Sum, AggregateOptions.Default)
    | "diffix_count" -> AggregateFunction(DiffixCount, AggregateOptions.Default)
    | "diffix_low_count" -> AggregateFunction(DiffixLowCount, AggregateOptions.Default)
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
  /// Returns whether the given column in the table is an AID column.
  let isAidColumn anonParams tableName columnName =
    anonParams.TableSettings
    |> Map.tryFind tableName
    |> function
      | Some tableSettings -> tableSettings.AidColumns |> List.exists (String.equalsI columnName)
      | None -> false

module QueryContext =
  let private defaultDataProvider =
    { new IDataProvider with
        member _.OpenTable(_table, _columnIndices) = failwith "No tables in data provider"
        member _.GetSchema() = []
        member _.Dispose() = ()
    }

  let make anonParams dataProvider =
    {
      AnonymizationParams = anonParams
      DataProvider = dataProvider
      PostAggregationHook = fun _aggregationContext -> id
      ExecutorHook = None
    }

  let makeDefault () =
    make AnonymizationParams.Default defaultDataProvider

  let makeWithAnonParams anonParams = make anonParams defaultDataProvider

  let makeWithDataProvider dataProvider =
    make AnonymizationParams.Default dataProvider

module ExecutionContext =
  let fromQueryContext queryContext =
    { QueryContext = queryContext; NoiseLayers = NoiseLayers.Default }

  let makeDefault () =
    fromQueryContext (QueryContext.makeDefault ())

module Plan =
  let rec columnsCount (plan: Plan) =
    match plan with
    | Plan.Scan (table, _) -> table.Columns.Length
    | Plan.Project (_, expressions) -> expressions.Length
    | Plan.ProjectSet (plan, _, _) -> (columnsCount plan) + 1
    | Plan.Filter (plan, _) -> columnsCount plan
    | Plan.Sort (plan, _) -> columnsCount plan
    | Plan.Aggregate (_, groupingLabels, aggregators) -> groupingLabels.Length + aggregators.Length
    | Plan.Unique plan -> columnsCount plan
    | Plan.Join (left, right, _, _) -> columnsCount left + columnsCount right
    | Plan.Append (first, _) -> columnsCount first
    | Plan.Limit (plan, _) -> columnsCount plan
