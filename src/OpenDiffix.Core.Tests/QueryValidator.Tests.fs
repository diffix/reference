module OpenDiffix.Core.QueryValidatorTests

open Xunit

let testTable: Table =
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


let dataProvider = dummyDataProvider [ testTable ]
let queryContext = QueryContext.make AnonymizationParams.Default dataProvider

let aidColIndex = Table.findColumn testTable "int_col" |> fst

let analyzeQuery isAnonymizing queryString =
  queryString
  |> Parser.parse
  |> Analyzer.analyze queryContext
  |> QueryValidator.validateQuery isAnonymizing

let ensureFailParsedQuery isAnonymizing queryString (errorFragment: string) =
  try
    analyzeQuery isAnonymizing queryString
    failwith "Was expecting query analysis to fail"
  with
  | ex ->
    let str = ex.Message.ToLower()

    if str.Contains(errorFragment.ToLower()) then
      ()
    else
      failwith $"Expecting error to contain '%s{errorFragment}'. Got '%s{str}' instead."

let ANONYMIZING = true
let NOT_ANONYMIZING = false

let ensureAnalyzeValid queryString = analyzeQuery ANONYMIZING queryString

let ensureAnalyzeFails queryString errorFragment =
  ensureFailParsedQuery ANONYMIZING queryString errorFragment

let ensureAnalyzeNotAnonFails queryString errorFragment =
  ensureFailParsedQuery NOT_ANONYMIZING queryString errorFragment

[<Fact>]
let ``Fail on sum aggregate`` () =
  ensureAnalyzeFails "SELECT sum(int_col) FROM table" "only count"

[<Fact>]
let ``Only allow count(*) and count(distinct column)`` () =
  ensureAnalyzeValid "SELECT count(*) FROM table"
  ensureAnalyzeValid "SELECT count(distinct int_col) FROM table"

[<Fact>]
let ``Disallow multiple low count aggregators`` () =
  let errorFragment = "single low count aggregator is allowed"

  ensureAnalyzeNotAnonFails
    "SELECT count(*), diffix_low_count(int_col), diffix_low_count(str_col) FROM table"
    errorFragment
