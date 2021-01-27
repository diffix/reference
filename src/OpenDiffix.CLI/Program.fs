open System.IO
open Argu
open OpenDiffix.CLI
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

type CliArguments =
  | [<AltCommandLine("-v")>] Version
  | DryRun
  | [<Unique; AltCommandLine("-d")>] Database of db_path: string
  | Aid_Columns of column_name: string list
  | Query_Path of path: string
  | [<AltCommandLine("-q")>] Query of sql: string
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
      | DryRun -> "Outputs the anonymization parameters used, but without running a query or anonymizing data."
      | Database _ -> "Specifies the path on disk to the SQLite database containing the data to be anonymized."
      | Aid_Columns _ -> "Specifies the AID column(s). Each AID should follow the format tableName.columnName."
      | Query_Path _ -> "Path to a file containing the SQL to be executed."
      | Query _ -> "The SQL query to execute."
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

let parser = ArgumentParser.Create<CliArguments>(programName = "OpenDiffix.CLI")

let failWithUsageInfo errorMsg = failwith $"ERROR: %s{errorMsg}\n\n%s{parser.PrintUsage()}"

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
       | _ -> failWithUsageInfo "Invalid request: AID doesn't have the format `table_name.column_name`")
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
  match parsedArgs.TryGetResult Query, parsedArgs.TryGetResult Query_Path with
  | Some query, _ -> query
  | None, Some path ->
      if File.Exists(path) then File.ReadAllText(path) else failWithUsageInfo $"Could not find a query at %s{path}"
  | _, _ -> failWithUsageInfo "Please specify a query to run!"

let getDbPath (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult CliArguments.Database with
  | Some dbPath -> if File.Exists(dbPath) then dbPath else failWithUsageInfo $"Could not find a database at %s{dbPath}"
  | None -> failWithUsageInfo $"Please specify the database path!"

let dryRun queryRequest =
  let encodedRequest = RequestParams.Encoder queryRequest
  $"%s{Thoth.Json.Net.Encode.toString 2 encodedRequest}", 0

let anonymize request =
  let queryResult = QueryEngine.runQuery request |> Async.RunSynchronously

  match queryResult with
  | Ok result ->
      let resultSet =
        result.Rows
        |> List.map (fun columnValues -> columnValues |> List.map ColumnValue.ToString |> List.reduce (sprintf "%s;%s"))
        |> List.reduce (sprintf "%s\n%s")

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
      let request =
        {
          Query = getQuery parsedArguments
          DatabasePath = getDbPath parsedArguments
          AnonymizationParams = constructAnonParameters parsedArguments
        }

      (if parsedArguments.Contains DryRun then dryRun request else anonymize request)
      |> fun (output, exitCode) ->
           printfn "%s" output
           exitCode

  with e ->
    printfn "%s" e.Message
    1
