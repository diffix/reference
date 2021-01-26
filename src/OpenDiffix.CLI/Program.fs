﻿open System
open System.IO
open System.Security.Cryptography
open Argu
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes


let version = "0.0.1"

type CliArguments =
  | [<AltCommandLine("-v")>] Version
  | DryRun
  | [<Mandatory; Unique; AltCommandLine("-d")>] Database of db_path: string
  | [<Mandatory; Unique; AltCommandLine("-aid")>] Aid_Column of column_name: string
  | [<AltCommandLine("-q")>] Query_Path of path: string
  | [<Unique; AltCommandLine("-s")>] Seed of seed_value: int

  // Threshold values
  | [<Unique>] Threshold_Outlier_Count of lower: int * upper: int
  | [<Unique>] Threshold_Top_Count of lower: int * upper: int
  | [<Unique>] Threshold_Low_Count of lower: int * upper: int

  // General anonymization parameters
  | [<Unique>] Noise_Distinct_count of std_dev: float * limit: int

  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Version -> "Outputs the version of the OpenDiffix reference implementation"
      | DryRun -> "Outputs the anonymization parameters used, but without running a query or anonymizing data"
      | Database _ -> "Specifies the path on disk to the SQLite database containing the data to be anonymized"
      | Aid_Column _ -> "Specifies the AID column. Should follow the format tableName.columnName"
      | Query_Path _ ->
          "Path to a file containing the SQL to be executed. If not present the query will be read from standard in"
      | Seed _ -> "The seed value to use when anonymizing the data. Changing the seed will change the result"
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
      | Noise_Distinct_count _ ->
          "Specifies the standard deviation used when calculating the noise applied to the count aggregate. "
          + "Additionally a limit should be specified which is used to truncate the normal distributed value generated."

let parser = ArgumentParser.Create<CliArguments>(programName = "opendiffix")

let toThreshold =
  function
  | Some (lower, upper) -> { Lower = lower; Upper = upper }
  | _ -> { Lower = 2; Upper = 5 }

let toNoise =
  function
  | Some (stdDev, cutoffLimit) -> { StandardDev = stdDev; Cutoff = cutoffLimit }
  | _ -> { StandardDev = 2.; Cutoff = 5 }

let private toTableSettings (aidColumn: string) =
  match aidColumn.Split '.' with
  | [| tableName; columnName |] -> Map [ tableName, { AidColumns = [ columnName ] } ]
  | _ -> failwith "Invalid request: AID doesn't have the format `table_name.column_name`"

let constructAnonParameters (parsedArgs: ParseResults<CliArguments>): AnonymizationParams =
  {
    TableSettings = parsedArgs.GetResult Aid_Column |> toTableSettings
    Seed = parsedArgs.GetResult(Seed, defaultValue = 1)
    LowCountThreshold = parsedArgs.TryGetResult Threshold_Low_Count |> toThreshold
    OutlierCount = parsedArgs.TryGetResult Threshold_Outlier_Count |> toThreshold
    TopCount = parsedArgs.TryGetResult Threshold_Top_Count |> toThreshold
    CountNoise = parsedArgs.TryGetResult Noise_Distinct_count |> toNoise
  }

let getQuery (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult Query_Path with
  | Some path ->
      if File.Exists(path) then File.ReadAllText(path) else failwith $"ERROR: Could not find a query at %s{path}"
  | None -> Console.In.ReadLine()

let getDbPath (parsedArgs: ParseResults<CliArguments>) =
  let dbPath = parsedArgs.GetResult CliArguments.Database
  if File.Exists(dbPath) then dbPath else failwith $"ERROR: Could not find a database at %s{dbPath}"

let dryRun query dbPath (anonParams: AnonymizationParams) =
  let formatThreshold threshold = $"[%i{threshold.Lower}, %i{threshold.Upper}]"
  let formatNoise np = $"0 +- %.2f{np.StandardDev}std limited to [-%i{np.Cutoff}, %i{np.Cutoff}]"

  $"
OpenDiffix dry run:

Database: %s{dbPath}
Query:
%s{query}

Anonymization parameters:
------------------------
Low count threshold: %s{formatThreshold anonParams.LowCountThreshold}

Outlier count threshold: %s{formatThreshold anonParams.OutlierCount}
Top count threshold: %s{formatThreshold anonParams.TopCount}
Count noise: %s{formatNoise anonParams.CountNoise}
  "

let anonymize query dbPath anonParams =
  let request = {
    Query = query
    DatabasePath = dbPath
    AnonymizationParams = anonParams
  }
  let queryResult = QueryEngine.runQuery request |> Async.RunSynchronously
  match queryResult with
  | Ok result ->
    let resultSet =
      result.Rows
      |> List.map(fun columnValues ->
        columnValues
        |> List.map ColumnValue.ToString
        |> List.reduce (sprintf "%s;%s")
      )
      |> List.reduce (sprintf "%s\n%s")
    resultSet, 0
  | Error err ->
    $"ERROR: %s{err}", 1

[<EntryPoint>]
let main argv =
  try
    let parsedArguments =
      parser.ParseCommandLine(inputs = argv, raiseOnUsage = true, ignoreMissing = false, ignoreUnrecognized = false)

    let output, exitCode =
      match parsedArguments.GetAllResults() with
      | [ Version ] -> $"OpenDiffix - %s{version}", 0
      | _ ->
          let query = getQuery parsedArguments
          let dbPath = getDbPath parsedArguments
          let anonParams = constructAnonParameters parsedArguments

          if parsedArguments.Contains DryRun
          then dryRun query dbPath anonParams, 0
          else anonymize query dbPath anonParams

    printfn "%s" output
    exitCode

  with e ->
    printfn "%s" e.Message
    1