open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Giraffe
open WebFrontend

let webApp =
    choose [
        POST >=> route "/query" >=> QueryHandler.handleQuery
        route  "/" >=> htmlView Page.index
        route  "/query" >=> htmlView Page.index
    ]

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
                    .UseWebRoot(__SOURCE_DIRECTORY__ + "/wwwroot")
                    |> ignore)
        .Build()
        .Run()
    0