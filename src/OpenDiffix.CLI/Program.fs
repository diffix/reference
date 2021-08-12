﻿open System
open System.IO
open Argu
open OpenDiffix.CLI
open OpenDiffix.Core
open OpenDiffix.Core.QueryEngine

type CliArguments =
  | [<AltCommandLine("-v")>] Version
  | [<Unique; AltCommandLine("-f")>] In_File_Path of file_path: string
  | [<Unique; AltCommandLine("-o")>] Out_File_Path of file_path: string
  | Aid_Columns of column_name: string list
  | [<AltCommandLine("-q")>] Query of sql: string
  | Queries_Path of path: string
  | Query_Stdin
  | [<Unique; AltCommandLine("-s")>] Salt of salt_value: uint64
  | Json

  // Threshold values
  | [<Unique>] Threshold_Outlier_Count of lower: int * upper: int
  | [<Unique>] Threshold_Top_Count of lower: int * upper: int
  | [<Unique>] Minimum_Allowed_Aid_Values of threshold: int

  // General anonymization parameters
  | [<Unique>] Noise_SD of std_dev: float

  interface IArgParserTemplate with
    member this.Usage =
      match this with
      | Version -> "Prints the version number of the program."
      | In_File_Path _ -> "Specifies the path on disk to the SQLite or CSV file containing the data to be anonymized."
      | Out_File_Path _ ->
          "Specifies the path on disk where the output is to be written. By default, standard out is used."
      | Aid_Columns _ -> "Specifies the AID column(s). Each AID should follow the format tableName.columnName."
      | Query _ -> "The SQL query to execute."
      | Queries_Path _ ->
          "Path to a file containing a list of query specifications. All queries will be executed in batch mode."
      | Query_Stdin -> "Reads the query from standard in."
      | Salt _ -> "The salt value to use when anonymizing the data. Changing the salt will change the result."
      | Json -> "Outputs the query result as JSON. By default, output is in CSV format."
      | Threshold_Outlier_Count _ ->
          "Threshold used in the count aggregate to determine how many of the entities with the most extreme values "
          + "should be excluded. A number is picked from a uniform distribution between the upper and lower limit."
      | Threshold_Top_Count _ ->
          "Threshold used in the count aggregate together with the outlier count threshold. It determines how many "
          + "of the next most contributing users' values should be used to calculate the replacement value for the "
          + "excluded users. A number is picked from a uniform distribution between the upper and lower limit."
      | Minimum_Allowed_Aid_Values _ ->
          "Sets the bound for the minimum number of AID values must be present in a bucket for it to pass the low count filter."
      | Noise_SD _ -> "Specifies the standard deviation used when calculating the noise throughout the system."

let executableName = "OpenDiffix.CLI"

let parser = ArgumentParser.Create<CliArguments>(programName = executableName)

let failWithUsageInfo errorMsg =
  failwith $"%s{errorMsg}\n\nPlease run '%s{executableName} -h' for help."

let toThreshold =
  function
  | Some (lower, upper) -> { Lower = lower; Upper = upper }
  | _ -> Threshold.Default

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

let constructAnonParameters (parsedArgs: ParseResults<CliArguments>) : AnonymizationParams =
  {
    TableSettings = parsedArgs.TryGetResult Aid_Columns |> toTableSettings
    Salt = parsedArgs.GetResult(Salt, defaultValue = 1UL)
    MinimumAllowedAids = parsedArgs.TryGetResult Minimum_Allowed_Aid_Values |> Option.defaultValue 2
    OutlierCount = parsedArgs.TryGetResult Threshold_Outlier_Count |> toThreshold
    TopCount = parsedArgs.TryGetResult Threshold_Top_Count |> toThreshold
    NoiseSD = parsedArgs.TryGetResult Noise_SD |> toNoise
  }

let getQuery (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult Query, parsedArgs.Contains Query_Stdin with
  | Some query, false -> query
  | None, true -> Console.In.ReadLine()
  | _, _ -> failWithUsageInfo "Please specify one (and only one) of the query input methods."

let getInFilePath (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult CliArguments.In_File_Path with
  | Some filePath ->
      if File.Exists(filePath) then
        filePath
      else
        failWithUsageInfo $"Could not find a file at %s{filePath}"
  | None -> failWithUsageInfo "Please specify the file path."

let getOutputStream (parsedArgs: ParseResults<CliArguments>) =
  match parsedArgs.TryGetResult CliArguments.Out_File_Path with
  | Some filePath -> new StreamWriter(filePath)
  | None -> new StreamWriter(Console.OpenStandardOutput())

let dryRun query filePath anonParams =
  let encodedRequest = JsonEncodersDecoders.encodeRequestParams query filePath anonParams
  Thoth.Json.Net.Encode.toString 2 encodedRequest, 0

let getDataProvider (filePath: string) =
  match filePath |> Path.GetExtension |> String.toLower with
  | ".csv" -> new CSV.DataProvider(filePath) :> IDataProvider
  | ".sqlite" -> new SQLite.DataProvider(filePath) :> IDataProvider
  | _ -> failWithUsageInfo "Please specify a file path with a .csv or .sqlite extension."

let runQuery query filePath anonParams =
  use dataProvider = getDataProvider filePath
  let context = EvaluationContext.make anonParams dataProvider
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
      with (exn: Exception) -> JsonEncodersDecoders.encodeErrorMsg exn.Message
    )

  let jsonValue = JsonEncodersDecoders.encodeBatchRunResult time AssemblyInfo.versionJsonValue results
  let resultJsonEncoded = Thoth.Json.Net.Encode.toString 2 jsonValue
  printf $"%s{resultJsonEncoded}"

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
      let inFilePath = getInFilePath parsedArguments
      let anonParams = constructAnonParameters parsedArguments
      let outputFormatter = if parsedArguments.Contains Json then jsonFormatter else csvFormatter
      let output = runQuery query inFilePath anonParams |> outputFormatter

      use writer = getOutputStream parsedArguments
      fprintfn writer $"%s{output}"
      0

  with e ->
    eprintfn $"ERROR: %s{e.Message}"
    1
