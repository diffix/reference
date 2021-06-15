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
  abstract OpenTable : table: Table -> Row seq
  abstract GetSchema : unit -> Schema

// ----------------------------------------------------------------
// Anonymizer types
// ----------------------------------------------------------------

type AidHash = int32

type Threshold =
  {
    Lower: int
    Upper: int
  }
  static member Default = { Lower = 2; Upper = 5 }

type NoiseParam =
  {
    StandardDev: float
    Cutoff: float
  }
  static member Default = { StandardDev = 2.; Cutoff = 3. }

type TableSettings = { AidColumns: string list }

type AnonymizationParams =
  {
    TableSettings: Map<string, TableSettings>
    Seed: int
    MinimumAllowedAids: int

    // Count params
    OutlierCount: Threshold
    TopCount: Threshold
    Noise: NoiseParam
  }
  static member Default =
    {
      TableSettings = Map.empty
      Seed = 0
      MinimumAllowedAids = 2
      OutlierCount = Threshold.Default
      TopCount = Threshold.Default
      Noise = NoiseParam.Default
    }

type EvaluationContext =
  {
    AnonymizationParams: AnonymizationParams
    DataProvider: IDataProvider
  }
  static member Default =
    {
      AnonymizationParams = AnonymizationParams.Default
      DataProvider =
        { new IDataProvider with
            member _.OpenTable _table = failwith "No tables in data provider"
            member _.GetSchema() = []
            member _.Dispose() = ()
        }
    }
