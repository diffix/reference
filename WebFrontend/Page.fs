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
        
        div [_class "mt-8 py-5 px-6 bg-gray-200 sm:max-w-3xl w-full sm:mx-auto shadow-lg rounded-xl border-gray-200 border overflow-hidden text-gray-800"] [
          h1 [_class "font-medium text-4xl"] [str "Upload databases"]
          form [
            _action "/upload-db"
            _method "POST"
            _enctype "multipart/form-data"
            _class "flex flex-col space-y-2 mt-4"] [
            div [_class "flex"] [
              label [_for "password"; _class "w-1/3 text-right pr-4"] [str "Password:"]
              input [_id "password"; _name "password"; _type "password"; _class "flex-grow px-2 py-1 rounded-lg"]
            ]

            div [_class "flex"] [
              label [_for "files"; _class "w-1/3 text-right pr-4"] [str "Sqlite files:"]
              input [_id "files"; _name "files"; _type "file"; _accept ".db,.sqlite,.sqlite3,application/vnd.sqlite3, application/x-sqlite3"; _multiple; _class "flex-grow px-2 py-1"]
            ]

            div [_class "flex"] [
              div [_class "w-1/3"] [str ""]
              button [_class "bg-green-500 text-gray-100 px-2 py-1 rounded-lg hover:bg-green-400 transition-colors"; _type "submit"] [str "Upload"]
            ]
          ]
        ]
      ]
    ]
  ]

let queryContainer databases (selectedDbOption: string option) query =
  form [_method "POST"; _action "/query"] [
    textarea [
      _class "pt-6 w-full block bg-gray-100 font-mono px-6 focus:bg-gray-700 focus:text-gray-100 focus:outline-none"
      _oninput "this.style.height = '';this.style.height = this.scrollHeight + 'px'"
      _name "query"
    ] [
      str query
    ]
    div [_class "flex items-center bg-white px-6 py-5"] [
      div [_class "flex-grow"] [
        label [] [
          str "Data source"
          select [_name "database"; _class "rounded-md border ml-2 px-2 py-1"] (
            databases
            |> List.map (fun dbName ->
              option [
                _value dbName
                match selectedDbOption with
                | Some selectedDbName when selectedDbName = dbName -> _selected
                | _ -> ()
              ] [str dbName]
            )
          )
        ]
        label [_class "ml-4 border-dotted border-l-2 pl-4 border-gray-400"] [
          input [_type "text"; _name "AidColumns[]"; _class "rounded-md border px-2 py-1"; _placeholder "Name of the AID column"]
        ]
      ]
      div [] [
        button [_class "bg-green-400 px-3 py-2 rounded-lg text-white hover:bg-green-500"] [str "Run query"]
      ]
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
    let columnsFromRow =
      function
      | AnonymizableRow row -> row.Columns
      | NonPersonalRow row -> row.Columns
      
    let header = columnsFromRow (List.head rows)
    
    div [_class "w-full bg-white py-3 px-6"] [
      table [_class "w-full"] [
        thead [] [
          tr [_class "text-left border-b-2 border-gr"] [
            for columnCell in header do
              yield th [] [str columnCell.ColumnName]
          ]
        ]
        tbody [] [
          for row in rows |> List.map columnsFromRow do
            yield
              tr [_class "pt-2 odd:bg-gray-200"] [
                for columnCell in row do
                  yield td [] [valueToStrNode columnCell.ColumnValue]
              ]
        ]
      ]
    ]

let errorFragment title description =
  div [_class "bg-red-400 text-white p-4 w-full"] [
    h2 [_class "text-lg font-bold"] [str title]
    pre [_class "mt-2 p-4 bg-red-500 rounded-b-md"] [str description]
  ]
  
open System.IO

let availableDbs path =
  Directory.GetFiles path
  |> Array.map Path.GetFileName
  |> Array.sortBy id
  |> Array.toList
  
let queryPage dbPath selectedDb query result =
  let renderedResultSection =
    match result with
    | Ok (QueryResult.ResultTable rows) -> renderResults rows
    | Error (ParseError error) -> errorFragment "Parse error" error
    | Error (DbNotFound) -> errorFragment "Error" "Could not locate the database"
    | Error (ExecutionError error) -> errorFragment "Execution error" error
    | Error (UnexpectedError error) -> errorFragment "An unexpected error occurred" error
  div [] [
    queryContainer (availableDbs dbPath) (Some selectedDb) query
    renderedResultSection
  ]
  |> layout
    
let index dbPath =
  queryContainer (availableDbs dbPath) None "SHOW tables"
  |> layout
