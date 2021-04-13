namespace Website.Server

open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Bolero.Remoting.Server
open Bolero.Server.RazorHost
open Bolero.Templating.Server

type Startup() =

  // This method gets called by the runtime. Use this method to add services to the container.
  // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
  member this.ConfigureServices(services: IServiceCollection) =
    services.AddMvc().AddRazorRuntimeCompilation() |> ignore
    services.AddServerSideBlazor() |> ignore

    services.AddBoleroHost()
#if DEBUG
      .AddHotReload(
        templateDir = __SOURCE_DIRECTORY__ + "/../Website.Client"
      )
#endif
    |> ignore

  // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
  member this.Configure(app: IApplicationBuilder, env: IWebHostEnvironment) =
    app
      .UseAuthentication()
      .UseRemoting()
      .UseStaticFiles()
      .UseRouting()
      .UseBlazorFrameworkFiles()
      .UseEndpoints(fun endpoints ->
#if DEBUG
        endpoints.UseHotReload()
#endif
        endpoints.MapBlazorHub() |> ignore
        endpoints.MapFallbackToPage("/_Host") |> ignore)
    |> ignore

module Program =

  [<EntryPoint>]
  let main args =
    WebHost.CreateDefaultBuilder(args).UseStaticWebAssets().UseStartup<Startup>().Build().Run()
    0
