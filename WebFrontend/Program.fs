open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Http
open Giraffe
open WebFrontend
open FSharp.Control.Tasks.V2
open System.IO

let uploadPassword =
  match Environment.GetEnvironmentVariable("UPLOAD_PASSWORD") with
  | null -> "db-diffix"
  | password -> password
    
let dbPath =
  #if DEBUG
  __SOURCE_DIRECTORY__ + "/../dbs"
  #else
  "/data/"
  #endif
  
let validatePassword: HttpHandler =
  fun (next: HttpFunc) (ctx: HttpContext) ->
    let passwordValues = ctx.Request.Form.Item "password"
    if passwordValues.Count = 0
    then RequestErrors.FORBIDDEN "Please authenticate with a password" next ctx
    else
      match passwordValues.Item 0 with
      | null -> RequestErrors.FORBIDDEN "Please authenticate with a password" next ctx
      | password when password <> uploadPassword -> RequestErrors.FORBIDDEN "Password is incorrect" next ctx
      | _ -> next ctx
    
let dbUploadHandler =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      return!
        (match ctx.Request.HasFormContentType with
        | false -> RequestErrors.BAD_REQUEST "Bad request" next ctx
        | true  ->
          if ctx.Request.Form.Files.Count = 0
          then text "Please upload at least one database file" next ctx
          else
            ctx.Request.Form.Files
            |> Seq.iter(fun file ->
              let fileNamePath = Path.Join [| dbPath; file.FileName |]
              let fileStream = File.Create fileNamePath
              file.CopyTo fileStream
              fileStream.Flush()
              fileStream.Close()
            )
            redirectTo false "/" next ctx)
    }
    
let webApp =
  warbler (fun _ ->
    choose [
      POST >=>
        choose [
          route "/query" >=> QueryHandler.handleQuery dbPath 
          route "/upload-db" >=> validatePassword >=> dbUploadHandler
        ]
      route  "/" >=> htmlView (Page.index dbPath)
      route  "/query" >=> htmlView (Page.index dbPath)
    ]
  )

let configureApp (app : IApplicationBuilder) =
  app
    .UseStaticFiles()
    .UseGiraffe webApp

let configureServices (services : IServiceCollection) =
  services.AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
  Host.CreateDefaultBuilder()
    .ConfigureWebHostDefaults(
      fun webHostBuilder ->
        webHostBuilder
          .Configure(configureApp)
          .ConfigureServices(configureServices)
          #if DEBUG
          .UseWebRoot(__SOURCE_DIRECTORY__ + "/wwwroot")
          #else
          .UseWebRoot("/wwwroot")
          #endif 
          |> ignore)
    .Build()
    .Run()
  0