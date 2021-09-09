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

type IRow =
  abstract Item : int -> Value with get
  abstract Length : int

type Rows = IRow seq

type ArrayRow(values: Value array) =
  member this.Values = values

  interface IRow with
    member this.Item
      with get (index) = values.[index]

    member this.Length = values.Length

let arrayToRow values = ArrayRow(values) :> IRow

let rowToArray (row: IRow) =
  match row with
  | :? ArrayRow as arrayRow -> arrayRow.Values
  | _ -> Array.init row.Length (fun i -> row.[i])

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
  abstract OpenTable : table: Table -> Rows
  abstract GetSchema : unit -> Schema

// ----------------------------------------------------------------
// Anonymizer types
// ----------------------------------------------------------------

type AidHash = uint64

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
    Supression: SuppressionParams

    // Count params
    OutlierCount: Interval
    TopCount: Interval
    NoiseSD: float
  }
  static member Default =
    {
      TableSettings = Map.empty
      Salt = [||]
      Supression = SuppressionParams.Default
      OutlierCount = Interval.Default
      TopCount = Interval.Default
      NoiseSD = 1.0
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
