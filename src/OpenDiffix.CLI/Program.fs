open System
open System.IO
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
  | Json

  // Threshold values
  | [<Unique>] Outlier_Count of int * int
  | [<Unique>] Top_Count of int * int
  | [<Unique>] Low_Threshold of int
  | [<Unique>] Low_SD of float
  | [<Unique>] Low_Mean_Gap of float

  // General anonymization parameters
  | [<Unique>] Noise_SD of float

  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Version -> "Prints the version number of the program."
      | File_Path _ -> "Specifies the path on disk to the SQLite file containing the data to be anonymized."
      | Aid_Columns _ -> "Specifies the AID column(s). Each AID should follow the format tableName.columnName."
      | Query _ -> "The SQL query to execute."
      | Queries_Path _ ->
        "Path to a file containing a list of query specifications. All queries will be executed in batch mode."
      | Query_Stdin -> "Reads the query from standard in."
      | Salt _ -> "The salt value to use when anonymizing the data. Changing the salt will change the result."
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
      | Low_SD _ -> "Sets the standard deviation for the low count filter threshold."
      | Low_Mean_Gap _ ->
        "Sets the number of standard deviations between the lower bound and the mean of the low count filter threshold."
      | Noise_SD _ -> "Specifies the standard deviation used when calculating the noise throughout the system."

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
  | _ -> AnonymizationParams.Default.NoiseSD

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

let constructAnonParameters (parsedArgs: ParseResults<CliArguments>) : AnonymizationParams =
  let suppression =
    {
      LowThreshold =
        parsedArgs.TryGetResult Low_Threshold
        |> Option.defaultValue SuppressionParams.Default.LowThreshold
      SD =
        parsedArgs.TryGetResult Low_SD
        |> Option.defaultValue SuppressionParams.Default.SD
      LowMeanGap =
        parsedArgs.TryGetResult Low_Mean_Gap
        |> Option.defaultValue SuppressionParams.Default.LowMeanGap
    }

  {
    TableSettings = parsedArgs.TryGetResult Aid_Columns |> toTableSettings
    Salt = parsedArgs.TryGetResult Salt |> toSalt
    Suppression = suppression
    OutlierCount = parsedArgs.TryGetResult Outlier_Count |> toInterval
    TopCount = parsedArgs.TryGetResult Top_Count |> toInterval
    NoiseSD = parsedArgs.TryGetResult Noise_SD |> toNoise
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

let dryRun query filePath anonParams =
  let encodedRequest = JsonEncodersDecoders.encodeRequestParams query filePath anonParams
  Thoth.Json.Net.Encode.toString 2 encodedRequest, 0

let runQuery query filePath anonParams =
  use dataProvider = new SQLite.DataProvider(filePath) :> IDataProvider
  let context = QueryContext.make anonParams dataProvider
  QueryEngine.run context query

let quoteString (string: string) =
  "\"" + string.Replace("\"", "\"\"") + "\""

let csvFormat value =
  match value with
  | String string -> quoteString string
  | _ -> Value.toString value

let csvFormatter result =
  let header =
    result.Columns
    |> List.map (fun column -> quoteString column.Name)
    |> String.join ","

  let rows =
    result.Rows
    |> List.map (fun row -> row |> Array.map csvFormat |> String.join ",")

  header :: rows |> String.join "\n"

let jsonFormatter = JsonEncodersDecoders.encodeQueryResult >> Thoth.Json.Net.Encode.toString 2

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
      with
      | (exn: Exception) -> JsonEncodersDecoders.encodeErrorMsg exn.Message
    )

  let jsonValue = JsonEncodersDecoders.encodeBatchRunResult time AssemblyInfo.versionJsonValue results
  let resultJsonEncoded = Thoth.Json.Net.Encode.toString 2 jsonValue
  printfn $"%s{resultJsonEncoded}"

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
      let filePath = getFilePath parsedArguments
      let anonParams = constructAnonParameters parsedArguments
      let outputFormatter = if parsedArguments.Contains Json then jsonFormatter else csvFormatter
      let output = runQuery query filePath anonParams |> outputFormatter

      printfn $"%s{output}"
      0

  with
  | e ->
    eprintfn $"ERROR: %s{e.Message}"
    1
