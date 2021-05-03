module OpenDiffix.Core.QueryValidatorTests

open Xunit

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

let aidColIndex = Table.findColumn testTable "int_col" |> fst

let analyzeQuery queryString =
  queryString
  |> Parser.parse
  |> Analyzer.transformQuery schema
  |> fst
  |> QueryValidator.validateQuery

let ensureFailParsedQuery queryString (errorFragment: string) =
  try
    analyzeQuery queryString
    failwith "Was expecting query analysis to fail"
  with ex ->
    let str = ex.Message.ToLower()

    if str.Contains(errorFragment.ToLower()) then
      ()
    else
      failwith $"Expecting error to contain '%s{errorFragment}'. Got '%s{str}' instead."

let ensureAnalyzeValid queryString = analyzeQuery queryString

[<Fact>]
let ``Fail on sum aggregate`` () =
  ensureFailParsedQuery "SELECT sum(int_col) FROM table" "only count"

[<Fact>]
let ``Only allow count(*) and count(distinct column)`` () =
  ensureAnalyzeValid "SELECT count(*) FROM table"
  ensureAnalyzeValid "SELECT count(distinct int_col) FROM table"
