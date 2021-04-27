open System
open System.IO
open Argu
open OpenDiffix.CLI
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

type CliArguments =
  | [<AltCommandLine("-v")>] Version
  | Dry_Run
  | [<Unique; AltCommandLine("-d")>] Database of db_path: string
  | Aid_Columns of column_name: string list
  | [<AltCommandLine("-q")>] Query of sql: string
  | Queries_Path of path: string
  | Query_Stdin
  | [<Unique; AltCommandLine("-s")>] Seed of seed_value: int

  // Threshold values
  | [<Unique>] Threshold_Outlier_Count of lower: int * upper: int
  | [<Unique>] Threshold_Top_Count of lower: int * upper: int
  | [<Unique>] Minimum_Allowed_Aid_Values of threshold: int

  // General anonymization parameters
  | [<Unique>] Noise of std_dev: float * factor: float

  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Version -> "Prints the version number of the program."
      | Dry_Run -> "Outputs the anonymization parameters used, but without running a query or anonymizing data."
      | Database _ -> "Specifies the path on disk to the SQLite database containing the data to be anonymized."
      | Aid_Columns _ -> "Specifies the AID column(s). Each AID should follow the format tableName.columnName."
      | Query _ -> "The SQL query to execute."
      | Queries_Path _ ->
          "Path to a file containing a list of query specifications. All queries will be executed in "
          + "batch mode, and the results will be written to standard out. Please consult the README for the query "
          + "file specification."
      | Query_Stdin -> "Reads the query from standard in."
      | Seed _ -> "The seed value to use when anonymizing the data. Changing the seed will change the result."
      | Threshold_Outlier_Count _ ->
          "Threshold used in the count aggregate to determine how many of the entities with the most extreme values "
          + "should be excluded. A number is picked from a uniform distribution between the upper and lower limit."
      | Threshold_Top_Count _ ->
          "Threshold used in the count aggregate together with the outlier count threshold. It determines how many "
          + "of the next most contributing users' values should be used to calculate the replacement value for the "
          + "excluded users. A number is picked from a uniform distribution between the upper and lower limit."
      | Minimum_Allowed_Aid_Values _ ->
          "Sets the bound for the minimum number of AID values must be present in a bucket for it to pass the low count filter."
      | Noise _ ->
          "Specifies the standard deviation used when calculating the noise throughout the system. "
          + "Additionally, a factor for the SD must be specified which is used to truncate the normal "
          + "distributed value generated."

let executableName = "OpenDiffix.CLI"

let parser = ArgumentParser.Create<CliArguments>(programName = executableName)

let failWithUsageInfo errorMsg = failwith $"ERROR: %s{errorMsg}\n\nPlease run '%s{executableName} -h' for help."

let toThreshold =
  function
  | Some (lower, upper) -> { Lower = lower; Upper = upper }
  | _ -> Threshold.Default

let toNoise =
  function
  | Some (stdDev, cutoffFactor) -> { StandardDev = stdDev; Cutoff = cutoffFactor }
  | _ -> NoiseParam.Default

let private toTableSettings (aidColumns: string list) =
  aidColumns
  |> List.map (fun aidColumn ->
    match aidColumn.Split '.' with
    | [| tableName; columnName |] -> (tableName, columnName)
    | _ -> failWithUsageInfo "Invalid request: AID doesn't have the format `table_name.column_name`"
  )
  |> List.groupBy (fst)
  |> List.map (fun (tableName, fullAidColumnList) -> (tableName, { AidColumns = fullAidColumnList |> List.map (snd) }))
  |> Map.ofList

let constructAnonParameters (parsedArgs: ParseResults<CliArguments>) : AnonymizationParams =
  {
    TableSettings = parsedArgs.GetResult Aid_Columns |> toTableSettings
    Seed = parsedArgs.GetResult(Seed, defaultValue = 1)
    MinimumAllowedAids = parsedArgs.TryGetResult Minimum_Allowed_Aid_Values |> Option.defaultValue 2
    OutlierCount = parsedArgs.TryGetResult Threshold_Outlier_Count |> toThreshold
    TopCount = parsedArgs.TryGetResult Threshold_Top_Count |> toThreshold
    Noise = parsedArgs.TryGetResult Noise |> toNoise
  }

let getQuery (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult Query, parsedArgs.Contains Query_Stdin with
  | Some query, false -> query
  | None, true -> Console.In.ReadLine()
  | _, _ -> failWithUsageInfo "Please specify one (and only one) of the query input methods."

let getDbPath (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult CliArguments.Database with
  | Some dbPath ->
      if File.Exists(dbPath) then
        dbPath
      else
        failWithUsageInfo $"Could not find a database at %s{dbPath}"
  | None -> failWithUsageInfo $"Please specify the database path!"

let dryRun query dbPath anonParams =
  let encodedRequest = JsonEncodersDecoders.encodeRequestParams query dbPath anonParams
  Thoth.Json.Net.Encode.toString 2 encodedRequest, 0

let runQuery query dbPath anonParams =
  use dataProvider = new SQLite.DataProvider(dbPath)
  QueryEngine.run dataProvider query anonParams |> Async.RunSynchronously

let anonymize query dbPath anonParams =
  match runQuery query dbPath anonParams with
  | Ok result ->
      let resultSet =
        result.Rows
        |> List.map (fun row -> row |> Array.map Value.ToString |> Array.reduce (sprintf "%s;%s"))
        |> List.fold (sprintf "%s\n%s") ""

      resultSet, 0
  | Error err -> $"ERROR: %s{err}", 1

let batchExecuteQueries (queriesPath: string) =
  if not <| File.Exists queriesPath then
    failWithUsageInfo $"Could not find a queries file at %s{queriesPath}"

  let queryFileContent = File.ReadAllText queriesPath

  let querySpecs =
    match JsonEncodersDecoders.decodeRequestParams queryFileContent with
    | Ok queries -> queries
    | Error error -> failWithUsageInfo $"Could not parse queries: %s{error}"

  let time = DateTime.Now

  let results =
    querySpecs
    |> List.map (fun queryRequest ->
      try
        runQuery queryRequest.Query queryRequest.DbPath queryRequest.AnonymizationParameters
        |> JsonEncodersDecoders.encodeIndividualQueryResponse queryRequest
      with (exn: Exception) -> JsonEncodersDecoders.encodeErrorMsg (exn.Message)
    )

  let jsonValue = JsonEncodersDecoders.encodeBatchRunResult time AssemblyInfo.versionJsonValue results
  let resultJsonEncoded = Thoth.Json.Net.Encode.toString 2 jsonValue
  printf "%s" resultJsonEncoded

  0

[<EntryPoint>]
let main argv =
  try
    let parsedArguments =
      parser.ParseCommandLine(inputs = argv, raiseOnUsage = true, ignoreMissing = false, ignoreUnrecognized = false)

    if parsedArguments.Contains(Version) then
      let version = Thoth.Json.Net.Encode.toString 2 AssemblyInfo.versionJsonValue
      printfn $"%s{version}"
      0
    else if parsedArguments.Contains(Queries_Path) then
      batchExecuteQueries (parsedArguments.GetResult Queries_Path)

    else
      let query = getQuery parsedArguments
      let dbPath = getDbPath parsedArguments
      let anonParams = constructAnonParameters parsedArguments

      let processor = if parsedArguments.Contains Dry_Run then dryRun else anonymize

      (processor query dbPath anonParams)
      |> fun (output, exitCode) ->
           printfn "%s" output
           exitCode

  with e ->
    printfn "%s" e.Message
    1
