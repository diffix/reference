module OpenDiffix.CLI.Program

open System
open System.IO
open System.Globalization
open Argu
open OpenDiffix.CLI
open OpenDiffix.Core
open OpenDiffix.Core.QueryEngine

type CliArguments =
  | [<AltCommandLine("-v")>] Version
  | [<Unique; AltCommandLine("-f")>] File_Path of string
  | Aid_Columns of string list
  | [<AltCommandLine("-q")>] Query of string
  | Queries_Path of string
  | Query_Stdin
  | [<Unique; AltCommandLine("-s")>] Salt of string
  | Access_Level of string
  | Strict of bool
  | Json

  // Threshold values
  | [<Unique>] Outlier_Count of int * int
  | [<Unique>] Top_Count of int * int
  | [<Unique>] Low_Threshold of int
  | [<Unique>] Low_Layer_SD of float
  | [<Unique>] Low_Mean_Gap of float

  // General anonymization parameters
  | [<Unique>] Layer_Noise_SD of float

  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Version -> "Prints the version number of the program."
      | File_Path _ -> "Specifies the path on disk to the SQLite file containing the data to be anonymized."
      | Aid_Columns _ -> "Specifies the AID column(s). Each AID should follow the format tableName.columnName."
      | Query _ -> "The SQL query to execute."
      | Queries_Path _ ->
        "Path to a file containing a list of query specifications. All queries will be executed in batch mode. "
        + "The db_path inside should be relative to this file's path."
      | Query_Stdin -> "Reads the query from standard in."
      | Salt _ -> "The salt value to use when anonymizing the data. Changing the salt will change the result."
      | Access_Level _ ->
        "Controls the access level to the data: 'publish_trusted' - protects against accidental re-identification; "
        + "'publish_untrusted' - protects against intentional re-identification; 'direct' - no anonymization."
      | Strict _ ->
        "Controls whether the anonymization parameters must be checked strictly, i.e. to ensure safe minimum level of "
        + "anonymization. Defaults to `true`."
      | Json -> "Outputs the query result as JSON. By default, output is in CSV format."
      | Outlier_Count _ ->
        "Interval used in the count aggregate to determine how many of the entities with the most extreme values "
        + "should be excluded. A number is picked from a uniform distribution between the upper and lower limit."
      | Top_Count _ ->
        "Interval used in the count aggregate together with the outlier count interval. It determines how many "
        + "of the next most contributing users' values should be used to calculate the replacement value for the "
        + "excluded users. A number is picked from a uniform distribution between the upper and lower limit."
      | Low_Threshold _ ->
        "Sets the lower bound for the number of distinct AID values that must be present in a bucket for it to pass the low count filter."
      | Low_Layer_SD _ ->
        "Specifies the standard deviation for each noise layer used when calculating the low count filter noisy threshold."
      | Low_Mean_Gap _ ->
        "Specifies the number of (total) standard deviations between the lower bound and the mean of the low count filter threshold."
        + "Total standard deviation is the combined standard deviation of two noise layers with `--low-layer-sd` standard deviation each."
      | Layer_Noise_SD _ ->
        "Specifies the standard deviation for each noise layer used when calculating aggregation noise."

let executableName = "OpenDiffix.CLI"

let parser = ArgumentParser.Create<CliArguments>(programName = executableName)

let failWithUsageInfo errorMsg =
  failwith $"%s{errorMsg}\n\nPlease run '%s{executableName} -h' for help."

let toInterval =
  function
  | Some (lower, upper) -> { Lower = lower; Upper = upper }
  | _ -> Interval.Default

let toNoise =
  function
  | Some stdDev -> stdDev
  | _ -> AnonymizationParams.Default.LayerNoiseSD

let private toTableSettings (aidColumns: string list option) =
  aidColumns
  |> Option.defaultValue List.empty<string>
  |> List.map (fun aidColumn ->
    match aidColumn.Split '.' with
    | [| tableName; columnName |] -> (tableName, columnName)
    | _ -> failWithUsageInfo "Invalid request: AID doesn't have the format `table_name.column_name`"
  )
  |> List.groupBy fst
  |> List.map (fun (tableName, fullAidColumnList) -> (tableName, { AidColumns = fullAidColumnList |> List.map snd }))
  |> Map.ofList

let toSalt =
  function
  | Some (salt: string) -> Text.Encoding.UTF8.GetBytes(salt)
  | _ -> [||]

let toAccessLevel =
  function
  | Some "publish_untrusted" -> PublishUntrusted
  | None
  | Some "publish_trusted" -> PublishTrusted
  | Some "direct" -> Direct
  | Some _ -> failWithUsageInfo "--access-level must be one of: 'publish_trusted', 'publish_untrusted', 'direct'."

let constructAnonParameters (parsedArgs: ParseResults<CliArguments>) : AnonymizationParams =
  let suppression =
    {
      LowThreshold =
        parsedArgs.TryGetResult Low_Threshold
        |> Option.defaultValue SuppressionParams.Default.LowThreshold
      LayerSD =
        parsedArgs.TryGetResult Low_Layer_SD
        |> Option.defaultValue SuppressionParams.Default.LayerSD
      LowMeanGap =
        parsedArgs.TryGetResult Low_Mean_Gap
        |> Option.defaultValue SuppressionParams.Default.LowMeanGap
    }

  {
    TableSettings = parsedArgs.TryGetResult Aid_Columns |> toTableSettings
    Salt = parsedArgs.TryGetResult Salt |> toSalt
    AccessLevel = parsedArgs.TryGetResult Access_Level |> toAccessLevel
    Strict = parsedArgs.TryGetResult Strict |> Option.defaultValue true
    Suppression = suppression
    OutlierCount = parsedArgs.TryGetResult Outlier_Count |> toInterval
    TopCount = parsedArgs.TryGetResult Top_Count |> toInterval
    LayerNoiseSD = parsedArgs.TryGetResult Layer_Noise_SD |> toNoise
  }

let getQuery (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult Query, parsedArgs.Contains Query_Stdin with
  | Some query, false -> query
  | None, true -> Console.In.ReadLine()
  | _, _ -> failWithUsageInfo "Please specify one (and only one) of the query input methods."

let getFilePath (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult CliArguments.File_Path with
  | Some filePath ->
    if File.Exists(filePath) then
      filePath
    else
      failWithUsageInfo $"Could not find a file at %s{filePath}"
  | None -> failWithUsageInfo "Please specify the file path."

let runQuery query filePath anonParams =
  use dataProvider = new SQLite.DataProvider(filePath) :> IDataProvider
  let queryContext = QueryContext.make anonParams dataProvider
  QueryEngine.run queryContext query

let csvFormat value =
  match value with
  | String s -> String.quote s
  | List _ -> value |> Value.toString |> String.quote
  | _ -> Value.toString value

let csvFormatter result =
  let header =
    result.Columns
    |> List.map (fun column -> String.quote column.Name)
    |> String.join ","

  let rows =
    result.Rows
    |> List.map (fun row -> row |> Array.map csvFormat |> String.join ",")

  header :: rows |> String.join "\n"

let jsonFormatter = JsonEncodersDecoders.encodeQueryResult

let private deriveDbPath (queriesPath: string) (queryRequest: JsonEncodersDecoders.QueryRequest) =
  let queriesDir = System.IO.Path.GetDirectoryName(queriesPath)
  System.IO.Path.Combine(queriesDir, queryRequest.DbPath)

let private runSingleQueryRequest queriesPath queryRequest =
  try
    let fullDbPath = deriveDbPath queriesPath queryRequest

    runQuery queryRequest.Query fullDbPath queryRequest.AnonymizationParameters
    |> (fun result -> (result, queryRequest))
    |> Ok
  with
  | (exn: Exception) -> Error exn.Message

let batchExecuteQueries (queriesPath: string) =
  if not <| File.Exists queriesPath then
    failWithUsageInfo $"Could not find a queries file at %s{queriesPath}"

  let queryFileContent = File.ReadAllText queriesPath

  let querySpecs =
    match JsonEncodersDecoders.decodeRequestParams queryFileContent with
    | Ok queries -> queries
    | Error error -> failWithUsageInfo $"Could not parse queries: %s{error}"

  let time = DateTime.Now

  let results = querySpecs |> List.map (runSingleQueryRequest queriesPath)

  JsonEncodersDecoders.encodeBatchRunResult time AssemblyInfo.version results

let mainCore argv =
  // Default to invariant culture regardless of system default.
  CultureInfo.DefaultThreadCurrentCulture <- CultureInfo.InvariantCulture

  let parsedArguments =
    parser.ParseCommandLine(inputs = argv, raiseOnUsage = true, ignoreMissing = false, ignoreUnrecognized = false)

  if parsedArguments.Contains(Version) then
    JsonEncodersDecoders.encodeVersionResult AssemblyInfo.version
  else if parsedArguments.Contains(Queries_Path) then
    batchExecuteQueries (parsedArguments.GetResult Queries_Path)
  else
    let query = getQuery parsedArguments
    let filePath = getFilePath parsedArguments
    let anonParams = constructAnonParameters parsedArguments
    let outputFormatter = if parsedArguments.Contains Json then jsonFormatter else csvFormatter
    runQuery query filePath anonParams |> outputFormatter

[<EntryPoint>]
let main argv =
  try
    argv |> mainCore |> printfn "%s"
    0
  with
  | e ->
    eprintfn $"ERROR: %s{e.Message}"
    1
