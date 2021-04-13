module OpenDiffix.Core.Tests.Analysis.QueryValidity_Tests

open Xunit
open OpenDiffix.Core

let testTable : Table =
  {
    Name = "table"
    Columns =
      [
        { Name = "str_col"; Type = StringType }
        { Name = "int_col"; Type = IntegerType } // AID column
        { Name = "float_col"; Type = RealType }
        { Name = "bool_col"; Type = BooleanType }
      ]
  }

let schema = [ testTable ]

let aidColIndex = Table.getColumnI testTable "int_col" |> Utils.unwrap |> fst

let analyzeQuery queryString =
  Parser.parse queryString
  |> Result.mapError (fun e -> $"Failed to parse: %A{e}")
  |> Result.bind (Analyzer.transformQuery schema)
  |> Result.bind (fun query ->
    AnalyzerTypes.SelectQuery query
    |> Analysis.QueryValidity.validateQuery
  )

let ensureFailParsedQuery queryString (errorFragment: string) =
  match analyzeQuery queryString with
  | Error str ->
      if str.ToLower().Contains(errorFragment.ToLower()) then
        ()
      else
        failwith $"Expecting error to contain '%s{errorFragment}'. Got '%s{str}' instead."
  | Ok _ -> failwith "Was excepting query analysis to fail"

let ensureAnalyzeValid queryString = assertOkEqual (analyzeQuery queryString) ()

[<Fact>]
let ``Fail on sum aggregate`` () = ensureFailParsedQuery "SELECT sum(int_col) FROM table" "only count"

[<Fact>]
let ``Only allow count(*) and count(distinct column)`` () =
  ensureAnalyzeValid "SELECT count(*) FROM table"
  ensureAnalyzeValid "SELECT count(distinct int_col) FROM table"
