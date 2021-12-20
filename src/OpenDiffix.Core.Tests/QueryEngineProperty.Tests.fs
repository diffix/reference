module OpenDiffix.Core.QueryEnginePropertyTests

open Npgsql.FSharp

open Xunit
open FsCheck

open QueryEngine
open Npgsql

type private OperationMode =
  | AnonymizingQuery
  | StandardQuery

let private operationMode = AnonymizingQuery

// ----------------------------------------------------------------
// Functions
// ----------------------------------------------------------------

// TODO: introduce numeric functions - is not trivial because of lack of floor_by in PostgreSQL
type FunctionSpec =
  | NoFunction
  | Substring of PositiveInt * PositiveInt

let substringSQL name substringFrom substringTo =
  $"substring({name}, %i{substringFrom}, %i{substringTo}) as {name}"

// ----------------------------------------------------------------
// Column specifications
// ----------------------------------------------------------------

type BucketColumnSpec = { Name: string; Function: FunctionSpec }

type ColumnSpec = BucketColumn of BucketColumnSpec

let wrap (nameList, fList) =
  List.zip nameList fList
  |> List.map (fun (name, f) -> BucketColumn { Name = name; Function = f })

let availableColumns = [ "first_name"; "last_name"; "age"; "city"; "company_name" ]
let stringColumns = [ "first_name"; "last_name"; "city"; "company_name" ]

let isListOfDistinct l = (l = List.distinct l)
let areListsSameSize (list1, list2) = List.length list1 = List.length list2

let doListsMatch (list1, list2) =
  List.zip list1 list2
  |> List.forall (fun e ->
    match e with
    | (_, NoFunction) -> true
    | (columnName, Substring (_, _)) -> List.contains columnName stringColumns
  )

let removeAt list index =
  list |> List.indexed |> List.filter (fun (i, _) -> i <> index) |> List.map snd

// ----------------------------------------------------------------
// Other clauses' specifications
// ----------------------------------------------------------------

type LimitClauseSpec = NonNegativeInt option

// ----------------------------------------------------------------
// Final `FsCheck` generators
// ----------------------------------------------------------------

type DiffixGenerators =
  static member UniqueColumnSpecList() =
    { new Arbitrary<ColumnSpec list>() with
        member x.Generator =
          let columnListGenerator =
            Gen.elements availableColumns
            |> Gen.nonEmptyListOf
            |> Gen.filter isListOfDistinct

          let functionListGenerator = Arb.from<FunctionSpec>.Generator |> Gen.nonEmptyListOf

          Gen.zip columnListGenerator functionListGenerator
          |> Gen.filter areListsSameSize
          |> Gen.filter doListsMatch
          |> Gen.map wrap

        member x.Shrinker columnSpecList =
          // NOTE: `Arb.Default.FsList<ColumnSpec>().Shrinker` is too generic, it chops column names
          match columnSpecList with
          | [] -> Seq.empty
          | [ _ ] -> Seq.empty
          | _ -> [ 0 .. columnSpecList.Length - 1 ] |> Seq.map (removeAt columnSpecList)
    }

// This represents a property-based-testable SQL query. FsCheck will traverse this type
// and create generators and shrinkers (`Arbitrary` objects) later used to generate test cases
[<StructuredFormatDisplay("{SQL}")>]
type Query =
  {
    LimitClause: LimitClauseSpec
    ColumnSpecs: ColumnSpec list
  }
  member x.SQL =
    let selectColumns =
      x.ColumnSpecs
      |> List.map (fun columnSpec ->
        match columnSpec with
        | BucketColumn { Name = name; Function = f } ->
          match f with
          | NoFunction -> name
          | Substring (substringFrom, substringTo) -> substringSQL name substringFrom.Get substringTo.Get
      )

    let aggregatorColumns = [ "count(*)"; "count(distinct id) as count_distinct" ]

    let selectClause = selectColumns @ aggregatorColumns |> String.join ", "
    let groupByClause = $"""GROUP BY %s{String.join ", " [ 1 .. x.ColumnSpecs.Length ]}"""
    let orderByClause = $"""ORDER BY %s{String.join ", " [ 1 .. x.ColumnSpecs.Length ]}"""
    let limitClause = if (x.LimitClause.IsNone) then "" else $"LIMIT %i{x.LimitClause.Value.Get}"

    $"SELECT {selectClause} FROM customers {groupByClause} {orderByClause} {limitClause};"

// ----------------------------------------------------------------
// Anonymization parameters utilities
// ----------------------------------------------------------------

let tableSettings =
  Map(
    if operationMode = AnonymizingQuery then
      [ "customers", { AidColumns = [ "id" ] } ]
    else
      []
  )

let anonParams =
  {
    TableSettings = tableSettings
    Salt = [||]
    // NOTE: params must match pg_diffix, see `setPgDiffixSetting` below
    // TODO: LowMeanGap of 0.0 matches no lcf randomization, which in turn matches lcf_range parameter equal to 0 in
    //       `pg_diffix`, but these two parameters are different!
    Suppression = { LowThreshold = 2; LowMeanGap = 0.0; LayerSD = 0. }
    OutlierCount = { Lower = 0; Upper = 0 }
    TopCount = { Lower = 1; Upper = 1 }
    LayerNoiseSD = 0.
  }

// NOTE: this will last only until the session it is invoked on
// NOTE2: connected user must be SUPERUSER;
let setPgDiffixSetting setting value connection =
  connection
  |> Sql.existingConnection
  |> Sql.query ($"SET pg_diffix.%s{setting} = %s{value};")
  |> Sql.executeNonQuery
  |> ignore

  connection
  |> Sql.existingConnection
  |> Sql.query $"SELECT * FROM pg_settings WHERE name = 'pg_diffix.%s{setting}';"
  |> Sql.execute (fun read -> read.text "setting")
  |> function
    | [ currentSettingValue ] when currentSettingValue = value -> ()
    | _ -> failwith "pg_diffix failed to set %s{setting} to %s{value}"

  connection

let ensureNoiselessPgDiffix connection =
  connection
  |> setPgDiffixSetting "noise_sigma" (string anonParams.LayerNoiseSD)
  |> ignore

let ensureNoFlatteningPgDiffix connection =
  connection
  |> setPgDiffixSetting "lcf_range" (string 0)
  |> setPgDiffixSetting "outlier_count_min" (string anonParams.OutlierCount.Lower)
  |> setPgDiffixSetting "outlier_count_max" (string anonParams.OutlierCount.Upper)
  |> setPgDiffixSetting "top_count_min" (string anonParams.TopCount.Lower)
  |> setPgDiffixSetting "top_count_max" (string anonParams.TopCount.Upper)
  |> ignore

// ----------------------------------------------------------------
// Data transformation utilities
// ----------------------------------------------------------------

let toValueBoolean (option: bool option) =
  match option with
  | Some x -> Boolean x
  | None -> Value.Null

let toValueInteger (option: int64 option) =
  match option with
  | Some x -> Integer x
  | None -> Value.Null

let toValueReal (option: float option) =
  match option with
  | Some x -> Real x
  | None -> Value.Null

let toValueString (option: string option) =
  match option with
  | Some x -> String x
  | None -> Value.Null

// ----------------------------------------------------------------
// PostgreSQL utilities
// ----------------------------------------------------------------

// for a single Npgsql row from `read` will iterate `referenceColumns` and build a `reference`-compatible `Row` instance
let interpretPostgreSQLRow referenceColumns (read: RowReader) =
  let interpretPostgreSQLColumn { Name = columnName; Type = columnType } =
    match columnType with
    | StringType -> toValueString (read.textOrNone columnName)
    | IntegerType -> toValueInteger (read.int64OrNone columnName)
    | RealType -> toValueReal (read.doubleOrNone columnName)
    | BooleanType -> toValueBoolean (read.boolOrNone columnName)
    | _ -> failwith "Unexpected reference column type"

  referenceColumns |> List.map interpretPostgreSQLColumn |> Array.ofList

let connectionString pgDiffixUser =
  Sql.host "localhost"
  |> Sql.database "prop_test"
  |> Sql.username pgDiffixUser
  |> Sql.password "prop_test"
  |> Sql.port 10432
  |> Sql.formatConnectionString

let openConnection (connection: NpgsqlConnection) =
  try
    connection.Open()
  with
  | :? NpgsqlException ->
    failwith "Unable to open connection to pg_diffix, is the container running and accepting on 10432?"

// ----------------------------------------------------------------
// Property based tests
// ----------------------------------------------------------------

type Tests(db: DBFixture) =
  let queryGivesSameResult (query: Query) =
    let queryContext = QueryContext.make anonParams db.DataProvider
    let { Rows = referenceResult; Columns = referenceColumns } = run queryContext query.SQL

    let pgDiffixUser = if operationMode = AnonymizingQuery then "prop_test_publish" else "prop_test"
    let connection = new NpgsqlConnection(connectionString pgDiffixUser)

    try
      openConnection connection

      ensureNoiselessPgDiffix connection
      // TODO introduce flattening, you may find more discrepancies
      ensureNoFlatteningPgDiffix connection

      let pgDiffixResult =
        connection
        |> Sql.existingConnection
        |> Sql.query query.SQL
        // NOTE: if pg_diffix returns more columns than reference, the test will not notice that.
        |> Sql.execute (interpretPostgreSQLRow referenceColumns)

      if (referenceResult <> pgDiffixResult) then
        printfn "reference: %A\npg_diffix: %A" referenceResult pgDiffixResult

      referenceResult = pgDiffixResult
    finally
      connection.Dispose()

  let checkSkip =
    let shouldRunEnvVar = System.Environment.GetEnvironmentVariable("OPEN_DIFFIX_RUN_PROPERTY_BASED_TESTS")
    Skip.IfNot(isNull (shouldRunEnvVar) |> not && shouldRunEnvVar.ToLower() = "true")

  // run with `OPEN_DIFFIX_RUN_PROPERTY_BASED_TESTS=true` to run these tests
  [<SkippableFact>]
  let ``Check pg_diffix to give same query results`` () =
    checkSkip

    // this makes our generators and shrinkers available for custom types we've prepared
    Arb.register<DiffixGenerators> () |> ignore

    Check.VerboseThrowOnFailure(Prop.forAll Arb.from<Query> queryGivesSameResult)

  interface IClassFixture<DBFixture>
