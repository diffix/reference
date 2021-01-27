open System
open System.IO
open Argu
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

type CliArguments =
  | DryRun
  | [<Mandatory; Unique; AltCommandLine("-d")>] Database of db_path: string
  | [<Mandatory; AltCommandLine("-aid")>] Aid_Column of column_name: string
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
      | DryRun -> "Outputs the anonymization parameters used, but without running a query or anonymizing data."
      | Database _ -> "Specifies the path on disk to the SQLite database containing the data to be anonymized."
      | Aid_Column _ -> "Specifies the AID column. Should follow the format tableName.columnName."
      | Query_Path _ ->
          "Path to a file containing the SQL to be executed. If not present the query will be read from standard in"
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

let parser = ArgumentParser.Create<CliArguments>(programName = "opendiffix")

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
    | _ -> failwith "Invalid request: AID doesn't have the format `table_name.column_name`"
  )
  |> List.groupBy (fst)
  |> List.map (fun (tableName, fullAidColumnList) -> (tableName, { AidColumns = fullAidColumnList |> List.map (snd) }))
  |> Map.ofList

let constructAnonParameters (parsedArgs: ParseResults<CliArguments>): AnonymizationParams =
  {
    TableSettings = parsedArgs.GetResults Aid_Column |> toTableSettings
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
      if File.Exists(path) then File.ReadAllText(path) else failwith $"ERROR: Could not find a query at %s{path}"
  | _, _ -> Console.In.ReadLine()

let getDbPath (parsedArgs: ParseResults<CliArguments>) =
  let dbPath = parsedArgs.GetResult CliArguments.Database
  if File.Exists(dbPath) then dbPath else failwith $"ERROR: Could not find a database at %s{dbPath}"

let dryRun query dbPath (anonParams: AnonymizationParams) =
  let formatThreshold threshold = $"[%i{threshold.Lower}, %i{threshold.Upper}]"
  let formatNoise np = $"0 +- %.2f{np.StandardDev}std limited to [-%.2f{np.Cutoff}, %.2f{np.Cutoff}]"

  $"
OpenDiffix dry run:

Database: %s{dbPath}
Query:
%s{query}

Anonymization parameters:
------------------------
Low count threshold: %s{formatThreshold anonParams.LowCountThreshold}
Noise: %s{formatNoise anonParams.Noise}

Count specific:
Outlier count threshold: %s{formatThreshold anonParams.OutlierCount}
Top count threshold: %s{formatThreshold anonParams.TopCount}
  ",
  0

let anonymize query dbPath anonParams =
  let connection = dbPath |> SQLite.dbConnection |> Utils.unwrap

  connection.Open()
  let queryResult = QueryEngine.run connection query anonParams |> Async.RunSynchronously
  connection.Close()

  match queryResult with
  | Ok result ->
      let resultSet =
        result.Rows
        |> List.map (fun row -> row |> Array.map Value.ToString |> Array.reduce (sprintf "%s;%s"))
        |> List.reduce (sprintf "%s\n%s")

      resultSet, 0
  | Error err -> $"ERROR: %s{err}", 1

[<EntryPoint>]
let main argv =
  try
    let parsedArguments =
      parser.ParseCommandLine(inputs = argv, raiseOnUsage = true, ignoreMissing = false, ignoreUnrecognized = false)

    let query = getQuery parsedArguments
    let dbPath = getDbPath parsedArguments
    let anonParams = constructAnonParameters parsedArguments

    (if parsedArguments.Contains DryRun then dryRun else anonymize) query dbPath anonParams
    |> fun (output, exitCode) ->
         printfn "%s" output
         exitCode

  with e ->
    printfn "%s" e.Message
    1
