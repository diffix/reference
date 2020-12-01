module WebFrontend.QueryHandler

open System
open System.Globalization
open DiffixEngine.Types
open Thoth.Json.Net
open FSharp.Control.Tasks
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Giraffe
open System.IO

[<CLIMutable>]
type QueryRequest =
  {
    Query: string
    Database: string
    AidColumns: string list
    Seed: int option
  }
  
  static member Decoder: Decoder<QueryRequest> =
    Decode.object (
      fun get ->
        {
          Query = get.Required.Field "query" Decode.string
          Database = get.Required.Field "database" Decode.string
          AidColumns = get.Optional.Field "aid_columns" (Decode.list Decode.string) |> Option.defaultValue []
          Seed = get.Optional.Field "seed" Decode.int
        }
    )

let seed =
  match Environment.GetEnvironmentVariable("SEED") with
  | null -> 0
  | seed -> int seed

let availableDbs path =
  Directory.GetFiles path
  |> Array.map (fun dbPathName -> Path.GetFileName dbPathName, dbPathName)
  |> Array.sortBy fst
  |> Array.toList

let runQuery pathToDbs (request: QueryRequest) =
  asyncResult {
    let! dbPath =
      availableDbs pathToDbs
      |> List.tryFind (fun (name, _) -> name = request.Database)
      |> Option.map snd
      |> Result.requireSome (ExecutionError $"database %s{request.Database} not found")
    let! aidColumnOption = result {
      match request.AidColumns with
      | [aidColumn] -> return Some aidColumn
      | [] -> return None
      | _ -> return! Error (InvalidRequest "A maximum of one AID column is supported at present")
    }
    let reqParams = {
      AidColumnOption = aidColumnOption
      Seed = request.Seed |> Option.map int |> Option.defaultValue seed
      LowCountThreshold = 5.
      LowCountThresholdStdDev = 0.5
    }
    let query = request.Query.Trim()
    return! DiffixEngine.QueryEngine.runQuery dbPath reqParams query
  }
  
let apiHandleQuery pathToDbs: HttpHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      let! body = ctx.ReadBodyFromRequestAsync()
      match Decode.fromString QueryRequest.Decoder body with
      | Ok userRequest ->
        let! result = runQuery pathToDbs userRequest
        let response =
          match result with
          | Ok result -> Encode.toString 2 (QueryResult.Encoder result)
          | Error error -> Encode.toString 2 (QueryError.Encoder error)
        return! (
          text response
          >=> setHttpHeader "Content-Type" "application/json; charset=utf-8"
          >=> setStatusCode 200
        ) next ctx
      | Error errorMessage ->
        let error = Encode.toString 2 (QueryError.Encoder (InvalidRequest errorMessage))
        return! (
          text error
          >=> setHttpHeader "Content-Type" "application/json; charset=utf-8"
          >=> setStatusCode 400
        ) next ctx
    }
  
let handleQuery pathToDbs: HttpHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      let usAmerican = CultureInfo.CreateSpecificCulture("en-US")
      let! userRequest = ctx.BindFormAsync<QueryRequest>(usAmerican)
      let! result = runQuery pathToDbs userRequest 
      return! htmlView (Page.queryPage pathToDbs userRequest.Database userRequest.Query result) next ctx
    }