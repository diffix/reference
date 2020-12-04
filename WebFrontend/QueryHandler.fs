module WebFrontend.QueryHandler

open System.Globalization
open DiffixEngine.Types
open Thoth.Json.Net
open FSharp.Control.Tasks
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Giraffe
open System.IO
open Types

let availableDbs path =
  Directory.GetFiles path
  |> Array.map (fun dbPathName -> Path.GetFileName dbPathName, dbPathName)
  |> Array.sortBy fst
  |> Array.toList

let private getAidColumnOption (userRequest: QueryRequest) =
  result {
    match userRequest.AidColumns with
    | [aidColumn] -> return Some aidColumn
    | [] -> return None
    | _ -> return! Error (InvalidRequest "A maximum of one AID column is supported at present")
  }

let findDatabase pathToDbs database =
  availableDbs pathToDbs
  |> List.tryFind (fun (name, _) -> name = database)
  |> Option.map snd
  |> Result.requireSome DbNotFound 
  
let deriveRequestParams pathToDbs (requestBody: string) =
  result {
    let! userRequest = Decode.fromString QueryRequest.Decoder requestBody |> Result.mapError (InvalidRequest)
    let! aidColumnOption = getAidColumnOption userRequest
    let! dbPath = findDatabase pathToDbs userRequest.Database
    return {
      AidColumnOption = aidColumnOption
      Seed = userRequest.Seed 
      LowCountSettings = userRequest.Anonymization.LowCountFiltering
      Query = userRequest.Query.Trim()
      DatabasePath = dbPath
    }
  }
  
let apiHandleQuery pathToDbs: HttpHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      let! body = ctx.ReadBodyFromRequestAsync()
      match deriveRequestParams pathToDbs body with
      | Ok requestParams ->
        let! result = DiffixEngine.QueryEngine.runQuery requestParams
        let response =
          match result with
          | Ok result -> Encode.toString 2 (QueryResult.Encoder requestParams result)
          | Error error -> Encode.toString 2 (QueryError.Encoder error)
        return! (
          text response
          >=> setHttpHeader "Content-Type" "application/json; charset=utf-8"
          >=> setStatusCode 200
        ) next ctx
      | Error queryError ->
        let error = Encode.toString 2 (QueryError.Encoder queryError)
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
      return! htmlView (Page.queryPage pathToDbs userRequest result) next ctx
    }