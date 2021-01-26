module OpenDiffix.Core.Tests.Analysis.QueryValidity_Tests

open Xunit
open OpenDiffix.Core

let testTable: Table =
  {
    Name = "table"
    Columns =
      [
        { Name = "str_col"; Type = StringType }
        { Name = "int_col"; Type = IntegerType }
        { Name = "float_col"; Type = FloatType }
        { Name = "bool_col"; Type = BooleanType }
      ]
  }

let ensureFailParsedQuery queryString callback (errorFragment: string) =
  let testResult =
    Parser.parse queryString
    |> Result.mapError (fun e -> $"Failed to parse: %A{e}")
    |> Result.bind (Analyzer.transformQuery testTable)
    |> Result.bind callback

  match testResult with
  | Error str ->
      if str.ToLower().Contains(errorFragment.ToLower()) then
        ()
      else
        failwith $"Expecting error to contain '%s{errorFragment}'. Got '%s{str}' instead."
  | Ok _ -> failwith "Was excepting query analysis to fail"


[<Fact>]
let ``Fail on sum aggregate`` () =
  ensureFailParsedQuery "SELECT sum(int_col) FROM table" (Analysis.QueryValidity.validateQuery) "only count"
