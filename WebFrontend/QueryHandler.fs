module WebFrontend.QueryHandler

open System.Globalization
open FSharp.Control.Tasks
open Microsoft.AspNetCore.Http
open Giraffe
open System.IO

[<CLIMutable>]
type QueryRequest = {
  Query: string
  Anonymize: bool
  Database: string 
}

let availableDbs path =
  Directory.GetFiles path
  |> Array.map (fun dbPathName -> Path.GetFileName dbPathName, dbPathName)
  |> Array.sortBy fst
  |> Array.toList
  
let handleQuery pathToDbs: HttpHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      let usAmerican = CultureInfo.CreateSpecificCulture("en-US")
      let! userRequest = ctx.BindFormAsync<QueryRequest>(usAmerican)
      match List.tryFind (fun (name, _) -> name = userRequest.Database) (availableDbs pathToDbs) with
      | None -> return! RequestErrors.notFound (text <| sprintf "Could not find database %s" userRequest.Database) next ctx
      | Some (dbName, dbPath) ->
        let query = userRequest.Query.Trim()
        let! queryResult = DiffixEngine.QueryEngine.runQuery dbPath query |> Async.StartAsTask
        return! htmlView (Page.queryPage pathToDbs dbName userRequest.Query queryResult) next ctx
    }