module OpenDiffix.Web.QueryHandler

open System.Globalization
open OpenDiffix.Core.Types
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
    match userRequest.Anonymization.AidColumns with
    | [aidColumn] -> return Some aidColumn
    | [] -> return None
    | _ -> return! Error (InvalidRequest "A maximum of one AID column is supported at present")
  }

let findDatabase pathToDbs database =
  availableDbs pathToDbs
  |> List.tryFind (fun (name, _) -> name = database)
  |> Option.map snd
  |> Result.requireSome DbNotFound

let deriveRequestParams pathToDbs (userRequest: QueryRequest) =
  result {
    let! aidColumnOption = getAidColumnOption userRequest
    let! dbPath = findDatabase pathToDbs userRequest.Database
    return {
      AnonymizationParams = {
        AidColumnOption = aidColumnOption
        Seed = userRequest.Anonymization.Seed
        LowCountSettings = userRequest.Anonymization.LowCountFiltering
      }
      Query = userRequest.Query.Trim()
      DatabasePath = dbPath
    }
  }

let deriveRequestParamsFromBody pathToDbs (requestBody: string) =
  result {
    let! userRequest = Decode.fromString QueryRequest.Decoder requestBody |> Result.mapError (InvalidRequest)
    return! deriveRequestParams pathToDbs userRequest
  }

let apiHandleQuery pathToDbs: HttpHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      let! body = ctx.ReadBodyFromRequestAsync()
      match deriveRequestParamsFromBody pathToDbs body with
      | Ok requestParams ->
        let! result = OpenDiffix.Core.QueryEngine.runQuery requestParams
        let response =
          match result with
          | Ok result -> Encode.toString 2 (QueryResultJson.Encoder requestParams result)
          | Error error -> Encode.toString 2 (QueryErrorJson.Encoder error)
        return! (
          text response
          >=> setHttpHeader "Content-Type" "application/json; charset=utf-8"
          >=> setStatusCode 200
        ) next ctx
      | Error queryError ->
        let error = Encode.toString 2 (QueryErrorJson.Encoder queryError)
        return! (
          text error
          >=> setHttpHeader "Content-Type" "application/json; charset=utf-8"
          >=> setStatusCode 400
        ) next ctx
    }

[<CLIMutable>]
type FormQueryRequest = {
  Query: string
  Database: string
  AidColumn: string
}

let handleQuery pathToDbs: HttpHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      let usAmerican = CultureInfo.CreateSpecificCulture("en-US")
      let! formUserRequest = ctx.BindFormAsync<FormQueryRequest>(usAmerican)
      let userRequest = {
        Query = formUserRequest.Query
        Database = formUserRequest.Database
        Anonymization = {
          AnonymizationParameters.Default
            with
              AidColumns =
                match formUserRequest.AidColumn with
                | "" -> []
                | columnName -> [columnName]
        }
      }
      match deriveRequestParams pathToDbs userRequest with
      | Ok requestParameters ->
        let! result = OpenDiffix.Core.QueryEngine.runQuery requestParameters |> Async.StartAsTask
        return! htmlView (Page.queryPage pathToDbs userRequest result) next ctx
      | Error error ->
        return! htmlView (Page.queryPage pathToDbs userRequest (Error error)) next ctx
    }
