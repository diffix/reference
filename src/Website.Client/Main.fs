module Website.Client.Main

open System
open Elmish
open Bolero
open Bolero.Html
open Bolero.Templating.Client
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

type Page =
    | [<EndPoint "/">] Home

type Model =
    {
        Page: Page
        AnonParams: AnonymizationParams
        Query: string
        Result: QueryResult option
        ParserError: Parser.SqlParserError option
    }

let initModel =
    {
        Page = Home
        AnonParams = AnonymizationParams.Default
        Query = ""
        Result = None
        ParserError = None
    }

type Message =
    | SetPage of Page
    | SetQuery of string
    | SetQueryResult of Result<QueryResult, Parser.SqlParserError>

let update message model =
    match message with
    | SetPage page ->
        { model with Page = page }, Cmd.none
    | SetQuery query ->
        let cmd =
          Cmd.OfAsync.result (async {
            let dataProvider = DataProvider.PlaygroundDataProvider()
            let! result = QueryEngine.run dataProvider query model.AnonParams
            return SetQueryResult result
          })
        { model with Query = query }, cmd
    | SetQueryResult result ->
        let updatedModel =
          match result with
          | Ok result ->
            { model with Result = Some result; ParserError = None }
          | Error parseError ->
            { model with ParserError = Some parseError }
        updatedModel, Cmd.none

let router = Router.infer SetPage (fun model -> model.Page)

type Main = Template<"wwwroot/main.html">

let homePage model dispatch =
    Main
      .Home()
      .Query(model.Query, fun query -> dispatch (SetQuery query))
      .Result(
        match model.Result, model.ParserError with
        | Some result, _ -> $"has result %A{result}"
        | _, Some parseError -> $"has parse error %A{parseError}"
        | _, _ -> "Does not yet have meaningful results or state"
      )
      .Elt()

let view model dispatch =
    Main()
        .Body(homePage model dispatch)
        .Elt()

type MyApp() =
    inherit ProgramComponent<Model, Message>()

    override this.Program =
        Program.mkProgram (fun _ -> initModel, Cmd.none) update view
        |> Program.withRouter router
#if DEBUG
        |> Program.withHotReload
#endif
