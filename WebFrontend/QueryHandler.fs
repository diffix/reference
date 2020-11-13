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

let handleQuery: HttpHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      let usAmerican = CultureInfo.CreateSpecificCulture("en-US")
      let! userRequest = ctx.BindFormAsync<Query>(usAmerican)
      let! queryResult = DiffixEngine.QueryEngine.runQuery "/Users/sebastian/work-projects/DiffixPrototype/test-db.sqlite" userRequest.Query |> Async.StartAsTask
      return! htmlView (Page.queryPage userRequest.Query queryResult) next ctx
    }