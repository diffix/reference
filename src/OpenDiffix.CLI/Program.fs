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
  | Query_Path of path: string
  | Query_Stdin
  | [<Unique; AltCommandLine("-s")>] Seed of seed_value: int

  // Threshold values
  | [<Unique>] Threshold_Outlier_Count of lower: int * upper: int
  | [<Unique>] Threshold_Top_Count of lower: int * upper: int
  | [<Unique>] Threshold_Low_Count of lower: int * upper: int

  // General anonymization parameters
  | [<Unique>] Noise of std_dev: float * limit: float

  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Version -> "Prints the version number of the program."
      | Dry_Run -> "Outputs the anonymization parameters used, but without running a query or anonymizing data."
      | Database _ -> "Specifies the path on disk to the SQLite database containing the data to be anonymized."
      | Aid_Columns _ -> "Specifies the AID column(s). Each AID should follow the format tableName.columnName."
      | Query _ -> "The SQL query to execute."
      | Query_Path _ -> "Path to a file containing the SQL to be executed."
      | Query_Stdin -> "Reads the query from standard in."
      | Seed _ -> "The seed value to use when anonymizing the data. Changing the seed will change the result."
      | Threshold_Outlier_Count _ ->
          "Threshold used in the count aggregate to determine how many of the entities with the most extreme values "
          + "should be excluded. A number is picked from a uniform distribution between the upper and lower limit."
      | Threshold_Top_Count _ ->
          "Threshold used in the count aggregate together with the outlier count threshold. It determines how many "
          + "of the next most contributing users' values should be used to calculate the replacement value for the "
          + "excluded users. A number is picked from a uniform distribution between the upper and lower limit."
      | Threshold_Low_Count _ ->
          "Threshold used to determine whether a bucket is low count or not. A number is picked from a uniform "
          + "distribution between the upper and lower limit."
      | Noise _ ->
          "Specifies the standard deviation used when calculating the noise throughout the system. "
          + "Additionally a limit must be specified which is used to truncate the normal distributed value generated."

let executableName = "OpenDiffix.CLI"

let parser = ArgumentParser.Create<CliArguments>(programName = executableName)

let failWithUsageInfo errorMsg = failwith $"ERROR: %s{errorMsg}\n\nPlease run '%s{executableName} -h' for help."

let toThreshold =
  function
  | Some (lower, upper) -> { Lower = lower; Upper = upper }
  | _ -> Threshold.Default

let toNoise =
  function
  | Some (stdDev, cutoffLimit) -> { StandardDev = stdDev; Cutoff = cutoffLimit }
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

let constructAnonParameters (parsedArgs: ParseResults<CliArguments>): AnonymizationParams =
  {
    TableSettings = parsedArgs.GetResult Aid_Columns |> toTableSettings
    Seed = parsedArgs.GetResult(Seed, defaultValue = 1)
    LowCountThreshold = parsedArgs.TryGetResult Threshold_Low_Count |> toThreshold
    OutlierCount = parsedArgs.TryGetResult Threshold_Outlier_Count |> toThreshold
    TopCount = parsedArgs.TryGetResult Threshold_Top_Count |> toThreshold
    Noise = parsedArgs.TryGetResult Noise |> toNoise
  }

let getQuery (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult Query, parsedArgs.TryGetResult Query_Path, parsedArgs.Contains Query_Stdin with
  | Some query, None, false -> query
  | None, Some path, false ->
      if File.Exists(path) then File.ReadAllText(path) else failWithUsageInfo $"Could not find a query at %s{path}"
  | None, None, true -> System.Console.In.ReadLine()
  | _, _, _ -> failWithUsageInfo "Please specify one (and only one) of the query input methods."

let getDbPath (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult CliArguments.Database with
  | Some dbPath -> if File.Exists(dbPath) then dbPath else failWithUsageInfo $"Could not find a database at %s{dbPath}"
  | None -> failWithUsageInfo $"Please specify the database path!"

let dryRun query dbPath anonParams =
  let encodedRequest = JsonEncoders.encodeRequestParams query dbPath anonParams
  Thoth.Json.Net.Encode.toString 2 encodedRequest, 0

let anonymize query dbPath anonParams =
  let connection = dbPath |> SQLite.dbConnection |> Utils.unwrap

  connection.Open()
  let queryResult = QueryEngine.run connection query anonParams |> Async.RunSynchronously
  connection.Close()

  match queryResult with
  | Ok result ->
      let resultSet =
        result.Rows
        |> List.map (fun row -> row.Values |> Array.map Value.ToString |> Array.reduce (sprintf "%s;%s"))
        |> List.fold (sprintf "%s\n%s") ""

      resultSet, 0
  | Error err -> $"ERROR: %s{err}", 1

[<EntryPoint>]
let main argv =
  try
    let parsedArguments =
      parser.ParseCommandLine(inputs = argv, raiseOnUsage = true, ignoreMissing = false, ignoreUnrecognized = false)

    if parsedArguments.Contains(Version) then
      (printfn "%s" AssemblyInfo.versionJson)
      0
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
