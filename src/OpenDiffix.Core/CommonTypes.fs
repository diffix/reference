/// Types used across multiple query phases.
[<AutoOpen>]
module rec OpenDiffix.Core.CommonTypes

open System
open System.Diagnostics
open System.Runtime.CompilerServices

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
    Metadata: QueryMetadata
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
  member this.Metadata = this.QueryContext.Metadata

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
      Metadata = QueryMetadata(fun _msg -> ())
    }

  let makeDefault () =
    make AnonymizationParams.Default defaultDataProvider

  let makeWithAnonParams anonParams = make anonParams defaultDataProvider

  let makeWithDataProvider dataProvider =
    make AnonymizationParams.Default dataProvider

  let withLogger logger queryContext =
    { queryContext with Metadata = QueryMetadata(logger) }

module ExecutionContext =
  let fromQueryContext queryContext =
    { QueryContext = queryContext; NoiseLayers = NoiseLayers.Default }

  let makeDefault () =
    fromQueryContext (QueryContext.makeDefault ())

// ----------------------------------------------------------------
// Logging & Instrumentation
// ----------------------------------------------------------------

// Events are relative to init time, expressed in ticks (unit of 100ns).
type Ticks = int64

type LogLevel =
  | DebugLevel
  | InfoLevel
  | WarningLevel
  | ErrorLevel

type LogMessage = { Timestamp: Ticks; Level: LogLevel; Message: string }

module Ticks =
  let private ticksPerMillisecond = float TimeSpan.TicksPerMillisecond

  let toTimestamp (t: Ticks) = TimeSpan.FromTicks(t).ToString("c")

  let toDuration (t: Ticks) =
    let ms = (float t / ticksPerMillisecond)

    if ms >= 1000.0 then
      (ms / 1000.0).ToString("N3") + "s"
    else
      ms.ToString("N3") + "ms"

module LogMessage =
  let private levelToString =
    function
    | DebugLevel -> "[DBG]"
    | InfoLevel -> "[INF]"
    | WarningLevel -> "[WRN]"
    | ErrorLevel -> "[ERR]"

  let toString (message: LogMessage) : string =
    $"{Ticks.toTimestamp message.Timestamp} {levelToString message.Level} {message.Message}"

type LoggerCallback = LogMessage -> unit

type QueryMetadata(logger: LoggerCallback) =
  let globalTimer = Stopwatch.StartNew()
  let measurements = Collections.Generic.Dictionary<string, Ticks>()
  let counters = Collections.Generic.Dictionary<string, int>()

  let makeMessage level message =
    { Timestamp = globalTimer.Elapsed.Ticks; Level = level; Message = message }

  let required opt =
    opt |> Option.defaultWith (fun () -> failwith "Event name required.")

  [<Conditional("DEBUG")>]
  member this.LogDebug(message: string) : unit = logger (makeMessage DebugLevel message)

  member this.Log(message: string) : unit = logger (makeMessage InfoLevel message)

  member this.LogWarning(message: string) : unit =
    logger (makeMessage WarningLevel message)

  member this.LogError(message: string) : unit = logger (makeMessage ErrorLevel message)

  member this.MeasureScope([<CallerMemberName>] ?event: string) : IDisposable =
    let event = required event
    let stopwatch = Stopwatch.StartNew()

    { new IDisposable with
        member _.Dispose() =
          let total = stopwatch.Elapsed.Ticks + (Dictionary.getOrDefault event 0L measurements)
          stopwatch.Reset()
          measurements.[event] <- total
    }

  [<Conditional("DEBUG")>]
  member this.CountDebug([<CallerMemberName>] ?event: string) : unit = this.Count(required event)

  member this.Count([<CallerMemberName>] ?event: string) : unit =
    let event = required event
    let currentCount = Dictionary.getOrDefault event 0 counters
    counters.[event] <- currentCount + 1

  override this.ToString() =
    let builder = Text.StringBuilder()

    if measurements.Count > 0 || counters.Count > 0 then
      builder.AppendLine("<Metadata>") |> ignore

      measurements
      |> Seq.iter (fun pair -> builder.AppendLine($"{pair.Key}: {Ticks.toDuration pair.Value}") |> ignore)

      counters
      |> Seq.iter (fun pair -> builder.AppendLine($"{pair.Key}: {pair.Value}") |> ignore)

    builder.ToString()
