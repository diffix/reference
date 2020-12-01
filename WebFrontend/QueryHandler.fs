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
    AidColumn: string option
    Seed: int option
  }

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
    let reqParams = {
      AidColumnOption = request.AidColumn
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
      let! userRequest = ctx.BindJsonAsync<QueryRequest>()
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
    }
  
let handleQuery pathToDbs: HttpHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      let usAmerican = CultureInfo.CreateSpecificCulture("en-US")
      let! userRequest = ctx.BindFormAsync<QueryRequest>(usAmerican)
      let! result = runQuery pathToDbs userRequest 
      return! htmlView (Page.queryPage pathToDbs userRequest.Database userRequest.Query result) next ctx
    }