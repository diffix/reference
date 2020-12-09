open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Http
open Giraffe
open OpenDiffix.Web

let uploadPassword =
  match Environment.GetEnvironmentVariable("UPLOAD_PASSWORD") with
  | null -> "db-diffix"
  | password -> password

let dbPath =
#if DEBUG
  __SOURCE_DIRECTORY__ + "/../../dbs"
#else
  "/data/"
#endif

let validatePasswordHeader: HttpHandler =
  fun (next: HttpFunc) (ctx: HttpContext) ->
    match ctx.TryGetRequestHeader "password" with
    | Some password when password = uploadPassword -> next ctx
    | _ -> RequestErrors.FORBIDDEN "The password is missing or wrong" next ctx

let validatePassword: HttpHandler =
  fun (next: HttpFunc) (ctx: HttpContext) ->
    let passwordValues = ctx.Request.Form.Item "password"

    if passwordValues.Count = 0 then
      RequestErrors.FORBIDDEN "Please authenticate with a password" next ctx
    else
      match passwordValues.Item 0 with
      | null -> RequestErrors.FORBIDDEN "Please authenticate with a password" next ctx
      | password when password <> uploadPassword -> RequestErrors.FORBIDDEN "Password is incorrect" next ctx
      | _ -> next ctx

let webApp =
  warbler (fun _ ->
    choose [ POST
             >=> choose [ route "/api" >=> QueryHandler.apiHandleQuery dbPath
                          route "/query" >=> QueryHandler.handleQuery dbPath
                          route "/upload-db"
                          >=> validatePassword
                          >=> DbUploadHandler.fromFormHandler dbPath
                          route "/api/upload-db"
                          >=> validatePasswordHeader
                          >=> DbUploadHandler.fromBodyHandler dbPath ]
             route "/" >=> htmlView (Page.index dbPath)
             route "/query" >=> htmlView (Page.index dbPath) ])

let configureApp (app: IApplicationBuilder) = app.UseStaticFiles().UseGiraffe webApp

let configureServices (services: IServiceCollection) = services.AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
  Host
    .CreateDefaultBuilder()
    .ConfigureWebHostDefaults(fun webHostBuilder ->
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
