module WebFrontend.Page

open DiffixEngine.Types
open Giraffe
open GiraffeViewEngine

let layout content =
  html [_lang "en"] [
    head [] [
      meta [_charset "UTF-8"]
      title [] [str "Diffix query prototype"]
      link [_rel "stylesheet"; _type "text/css"; _href "styles.css"]
    ]
    body [] [
      div [_class "min-h-screen bg-gray-100 flex flex-col justify-center sm:py-12"] [
        div [_class "relative sm:max-w-3xl w-full sm:mx-auto shadow-lg rounded-xl border-gray-200 border overflow-hidden text-gray-800"] [
          div [_class "bg-white w-full px-6 py-5 relative"] [
            h1 [_class "font-medium text-4xl"] [str "Diffix prototype"]
            p [_class "mt-2"] [
              str "Test db-diffix to your hearts contents. May it produce exceptionally well anonymized results."
            ]
          ]
          content
        ]
      ]
    ]
  ]

let queryContainer query =
  form [_method "POST"; _action "/query"] [
    textarea [
      _class "pt-6 w-full block bg-gray-100 font-mono px-6 focus:bg-gray-700 focus:text-gray-100 focus:outline-none"
      _name "query"
    ] [
      str query
    ]
    div [_class "bg-white w-full px-6 py-5"] [
      button [_class "bg-green-400 px-3 py-2 rounded-lg text-white hover:bg-green-500"] [str "Run query"]
    ]
  ]

let valueToStrNode =
  function
  | ColumnValue.IntegerValue v -> str (sprintf "%i" v)
  | ColumnValue.StringValue v -> str v
  
let renderResults: Row list -> XmlNode =
  function
  | [] -> 
    div [_class "bg-gray-400 text-white p-4 w-full"] [
      h2 [_class "text-lg font-bold"] [str "No rows returned"]
    ]
  | rows ->
    let header = List.head rows
    
    div [_class "w-full bg-white py-3 px-6"] [
      table [_class "w-full"] [
        thead [] [
          tr [_class "text-left border-b-2 border-gr"] [
            for ColumnCell (columnName, _value) in header do
              yield th [] [str columnName]
          ]
        ]
        tbody [] [
          for row in rows do
            yield
              tr [_class "pt-2 odd:bg-gray-200"] [
                for ColumnCell (_, columnValue) in row do
                  yield td [] [valueToStrNode columnValue]
              ]
        ]
      ]
    ]

let errorFragment title description =
  div [_class "bg-red-400 text-white p-4 w-full"] [
    h2 [_class "text-lg font-bold"] [str title]
    pre [_class "mt-2 p-4 bg-red-500 rounded-b-md"] [str description]
  ]
  
let queryPage query queryResult =
  let renderedResultSection =
    match queryResult with
    | QueryResult.ResultTable rows -> renderResults rows
    | ParseError error -> errorFragment "Parse error" error
    | DbNotFound -> errorFragment "Error" "Could not locate the database"
    | ExecutionError error -> errorFragment "Execution error" error
    | UnexpectedError error -> errorFragment "An unexpected error occurred" error
  div [] [
    queryContainer query
    renderedResultSection
  ]
  |> layout
  
let index =
  queryContainer "SHOW tables"
  |> layout
