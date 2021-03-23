module Website.Client.Main

open Elmish
open Bolero
open Bolero.Html
open Bolero.Templating.Client
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

type MixedCell = { AnonymizedValue: Value; RawValue: Value }

type ComparativeCell =
  | IdenticalCell of Value
  | MixedCell of MixedCell

type ComparativeRow =
  | AnonymizedAway of Row
  | Identical of Row
  | Mixed of ComparativeCell array

type ComparativeResult = { Columns: string list; Rows: ComparativeRow list }

type Page = | [<EndPoint "/">] Home

type Message =
  | SetPage of Page
  | SetQuery of string
  | SetQueryResult of ComparativeResult
  | SetError of string
  | AdjustAnonParam of (AnonymizationParams -> AnonymizationParams)
  | RunQuery

let runQuery query anonParams =
  async {
    let dataProvider = DataProvider.PlaygroundDataProvider()

    try
      match! QueryEngine.run dataProvider query anonParams with
      | Ok result -> return Ok result
      | Error parseError -> return Error(parseError.ToString())
    with exn -> return Error exn.Message
  }

let anyMatchingCells anon raw = Array.zip anon raw |> Array.tryFind (fun (a, r) -> a = r) |> Option.isSome

let matchUpCells anon raw =
  Array.zip anon raw
  |> Array.map
       (function
       | (a, r) when a = r -> IdenticalCell a
       | (a, r) -> MixedCell { AnonymizedValue = a; RawValue = r })

let compareResults (anon: QueryResult) (raw: QueryResult): ComparativeResult =
  let rec compareRowsRecursively a r =
    match a, r with
    | [], [] -> []
    | [], rows -> rows |> List.map AnonymizedAway
    | _, [] -> failwith "Anonymized rows should be a subset of the raw rows. This should never happen."
    | an :: ans, r :: rs when an = r -> Identical an :: compareRowsRecursively ans rs
    | an :: ans, r :: rs ->
        if anyMatchingCells an r then
          (Mixed <| matchUpCells an r) :: compareRowsRecursively ans rs
        else
          (AnonymizedAway r) :: compareRowsRecursively (an :: ans) rs

  let compareRows (a: Row list) (r: Row list) =
    match a, r with
    | [ a ], [ r ] -> [ Mixed <| matchUpCells a r ]
    | _, _ -> compareRowsRecursively a r

  { Columns = anon.Columns; Rows = compareRows anon.Rows raw.Rows }

let executeComparativeQuery query anonParams =
  async {
    match! runQuery query anonParams with
    | Ok anonymizedResult ->
        match! runQuery query { anonParams with TableSettings = Map.empty } with
        | Ok rawResult ->
            let comparativeResult = compareResults anonymizedResult rawResult
            return SetQueryResult comparativeResult
        | Error error -> return SetError error
    | Error error -> return SetError error
  }

type Model =
  {
    Page: Page
    AnonParams: AnonymizationParams
    Query: string
    Result: ComparativeResult option
    Error: string option
  }

let initModel =
  let tableSettings =
    [ "customers", { AidColumns = [ { Name = "aid"; MinimumAllowed = 2 } ] } ]
    |> Map.ofList

  {
    Page = Home
    AnonParams = { AnonymizationParams.Default with TableSettings = tableSettings }
    Query = "SELECT age, count(*)\n" + "FROM customers\n" + "GROUP BY age"
    Result = None
    Error = None
  }

let update message model =
  match message with
  | SetPage page -> { model with Page = page }, Cmd.none
  | RunQuery ->
      let cmd = Cmd.OfAsync.result (executeComparativeQuery model.Query model.AnonParams)
      model, cmd
  | SetQuery query -> { model with Query = query }, Cmd.ofMsg RunQuery
  | SetQueryResult result -> { model with Result = Some result; Error = None }, Cmd.none
  | SetError error -> { model with Error = Some error }, Cmd.none
  | AdjustAnonParam adjuster -> { model with AnonParams = adjuster model.AnonParams }, Cmd.ofMsg RunQuery

let router = Router.infer SetPage (fun model -> model.Page)

type Main = Template<"wwwroot/main.html">

let cellClasses =
  function
  | Null
  | Boolean _
  | Integer _
  | Real _ -> [ "text-right px-1 py-0.5" ]
  | String _ -> [ "text-left px-1 py-0.5" ]

let valueToNode value = text <| Value.ToString value

let renderRow rowCss row =
  tr
    [ Classes rowCss ]
    (row
     |> Array.map (fun value -> td [ Classes(cellClasses value) ] [ valueToNode value ])
     |> Array.toList)

let renderComparativeRow row =
  tr
    []
    (row
     |> Array.map
          (function
          | IdenticalCell value -> td [ Classes(cellClasses value) ] [ valueToNode value ]
          | MixedCell mixed ->
              td [ Classes(cellClasses mixed.RawValue) ] [
                div [ Classes [ "inline-flex items-center inline p-px text-base bg-green-500 border rounded-full" ] ] [
                  span [ Classes [ "bg-white text-green-500 rounded-full pl-1 pr-1.5" ] ] [
                    valueToNode mixed.AnonymizedValue
                  ]
                  span [ Classes [ "ml-0.5 text-xs pr-1 h-full text-white" ] ] [ text "A" ]
                ]
                span [ Classes [ "text-gray-200 mx-1" ] ] [ text "|" ]
                div [ Classes [ "inline-flex items-center inline p-px text-base bg-yellow-500 border rounded-full" ] ] [
                  span [ Classes [ "bg-white text-yellow-500 rounded-full pl-1 pr-1.5" ] ] [
                    valueToNode mixed.RawValue
                  ]
                  span [ Classes [ "ml-0.5 text-xs pr-1 h-full text-white" ] ] [ text "R" ]
                ]
              ])
     |> Array.toList)

let resultTable (result: ComparativeResult) =
  table [ Classes [ "w-full" ] ] [
    thead [] [ tr [] (result.Columns |> List.map (fun columnName -> th [] [ text columnName ])) ]

    tbody
      []
      (result.Rows
       |> List.map
            (function
            | AnonymizedAway row -> renderRow [ "bg-red-50" ] row
            | Identical row -> renderRow [ "bg-green-50" ] row
            | Mixed row -> renderComparativeRow row))
  ]

let errorTemplate (description: string) = Main.Error().ErrorDescription(description).Elt()

let getMinimumAllowedAids table column (anonParams: AnonymizationParams) =
  let customersSettings = Map.find table anonParams.TableSettings

  customersSettings.AidColumns
  |> List.find (fun aidSetting -> aidSetting.Name = column)
  |> fun aidSetting -> aidSetting.MinimumAllowed

let setMinimumAllowedAids table column newMinimum (anonParam: AnonymizationParams) =
  Map.find table anonParam.TableSettings
  |> fun tableSettings -> tableSettings.AidColumns
  |> List.map (fun columnSettings ->
    if columnSettings.Name = column then { columnSettings with MinimumAllowed = newMinimum } else columnSettings
  )
  |> fun columnsSettings ->
       { anonParam with
           TableSettings = Map.add table { AidColumns = columnsSettings } anonParam.TableSettings
       }

let homePage model dispatch =
  Main
    .Home()
    .Query(model.Query, (fun query -> dispatch (SetQuery query)))
    .MinimumNumberAIDs(getMinimumAllowedAids "customers" "aid" model.AnonParams,
                       fun v -> dispatch <| AdjustAnonParam(setMinimumAllowedAids "customers" "aid" v))
    .OutlierMin(model.AnonParams.OutlierCount.Lower,
                fun v ->
                  dispatch
                  <| AdjustAnonParam(fun a -> { a with OutlierCount = { a.OutlierCount with Lower = v } }))
    .OutlierMax(model.AnonParams.OutlierCount.Upper,
                fun v ->
                  dispatch
                  <| AdjustAnonParam(fun a -> { a with OutlierCount = { a.OutlierCount with Upper = v } }))
    .TopMin(model.AnonParams.TopCount.Lower,
            fun v ->
              dispatch
              <| AdjustAnonParam(fun a -> { a with TopCount = { a.TopCount with Lower = v } }))
    .TopMax(model.AnonParams.TopCount.Upper,
            fun v ->
              dispatch
              <| AdjustAnonParam(fun a -> { a with TopCount = { a.TopCount with Upper = v } }))
    .NoiseStdDev(model.AnonParams.Noise.StandardDev,
                 fun v ->
                   dispatch
                   <| AdjustAnonParam(fun a -> { a with Noise = { a.Noise with StandardDev = v } }))
    .NoiseCutoff(model.AnonParams.Noise.Cutoff,
                 fun v ->
                   dispatch
                   <| AdjustAnonParam(fun a -> { a with Noise = { a.Noise with Cutoff = v } }))
    .Error(model.Error |> Option.map errorTemplate |> Option.defaultValue empty)
    .Result(model.Result |> Option.map resultTable |> Option.defaultValue empty)
    .Elt()

let view model dispatch = Main().Body(homePage model dispatch).Elt()

type MyApp() =
  inherit ProgramComponent<Model, Message>()

  override this.Program =
    Program.mkProgram (fun _ -> initModel, Cmd.ofMsg RunQuery) update view
    |> Program.withRouter router
#if DEBUG
    |> Program.withHotReload
#endif
