module OpenDiffix.Core.Tests.Analysis.QueryValidity_Tests

open Xunit
open OpenDiffix.Core

let testTable: Table =
  {
    Name = "table"
    Columns =
      [
        { Name = "str_col"; Type = StringType }
        { Name = "int_col"; Type = IntegerType } // AID column
        { Name = "float_col"; Type = FloatType }
        { Name = "bool_col"; Type = BooleanType }
      ]
  }

let aidColIndex = Table.getColumn testTable "int_col" |> Utils.unwrap |> fst

let analyzeQuery queryString =
  Parser.parse queryString
  |> Result.mapError (fun e -> $"Failed to parse: %A{e}")
  |> Result.bind (Analyzer.transformQuery testTable)
  |> Result.bind (Analysis.QueryValidity.validateQuery aidColIndex)

let ensureFailParsedQuery queryString (errorFragment: string) =
  match analyzeQuery queryString with
  | Error str ->
      if str.ToLower().Contains(errorFragment.ToLower()) then
        ()
      else
        failwith $"Expecting error to contain '%s{errorFragment}'. Got '%s{str}' instead."
  | Ok _ -> failwith "Was excepting query analysis to fail"

let ensureFailWithUnspecifiedError queryString =
  match analyzeQuery queryString with
  | Error _ -> ()
  | Ok _ -> failwith "Was excepting query analysis to fail"

let ensureAnalyzeValid queryString =
  assertOkEqual (analyzeQuery queryString) ()

[<Fact>]
let ``Fail on sum aggregate`` () =
  ensureFailParsedQuery "SELECT sum(int_col) FROM table" "only count"

[<Fact>]
let ``Only allow count(*) and count(distinct aid)`` () =
  ensureAnalyzeValid "SELECT count(*) FROM table"
  ensureAnalyzeValid "SELECT count(distinct int_col) FROM table"
  ensureFailParsedQuery "SELECT count(distinct str_col) FROM table" "distinct aid-column"

[<Fact>]
let ``Only allow single table FROM`` () =
  ensureAnalyzeValid "SELECT int_col FROM table"
  // We don't yet parse anything else... but add some fallback tests that should always fail
  ensureFailWithUnspecifiedError "SELECT int_col FROM table, table"
  ensureFailWithUnspecifiedError "SELECT int_col FROM table t1 INNER JOIN table t2 ON t1.int_col = t2.int_col"
