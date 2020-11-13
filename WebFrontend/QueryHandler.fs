module WebFrontend.QueryHandler

open System.Globalization
open DiffixEngine.Types
open FSharp.Control.Tasks
open Microsoft.AspNetCore.Http
open Giraffe

[<CLIMutable>]
type Query = {
  Query: string
}

let dbPath =
    #if DEBUG
    __SOURCE_DIRECTORY__ + "/../test-db.sqlite"
    #else
    "/data/test-db.sqlite"
    #endif 

let handleQuery: HttpHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      let usAmerican = CultureInfo.CreateSpecificCulture("en-US")
      let! userRequest = ctx.BindFormAsync<Query>(usAmerican)
      let query = userRequest.Query.Trim()
      let! queryResult = DiffixEngine.QueryEngine.runQuery dbPath query |> Async.StartAsTask
      return! htmlView (Page.queryPage userRequest.Query queryResult) next ctx
    }