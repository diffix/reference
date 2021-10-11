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

type Hash = uint64
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

type QueryContext =
  {
    AnonymizationParams: AnonymizationParams
    DataProvider: IDataProvider
  }
  static member Default =
    {
      AnonymizationParams = AnonymizationParams.Default
      DataProvider =
        { new IDataProvider with
            member _.OpenTable(_table, _columnIndices) = failwith "No tables in data provider"
            member _.GetSchema() = []
            member _.Dispose() = ()
        }
    }

type NoiseLayers = { BucketSeed: Hash }

type ExecutionContext =
  {
    QueryContext: QueryContext
    NoiseLayers: NoiseLayers
  }
  static member Default = { QueryContext = QueryContext.Default; NoiseLayers = { BucketSeed = 0UL } }
