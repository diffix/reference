module OpenDiffix.Core.QueryValidatorTests

open Xunit

let testTables =
  [
    {
      Name = "anon_table"
      Columns =
        [
          { Name = "id"; Type = IntegerType }
          { Name = "str_col"; Type = StringType }
          { Name = "int_col"; Type = IntegerType }
          { Name = "float_col"; Type = RealType }
          { Name = "bool_col"; Type = BooleanType }
        ]
    }
    {
      Name = "direct_table"
      Columns =
        [
          { Name = "str_col"; Type = StringType }
          { Name = "int_col"; Type = IntegerType }
          { Name = "float_col"; Type = RealType }
          { Name = "bool_col"; Type = BooleanType }
        ]
    }
  ]

let anonParams =
  { AnonymizationParams.Default with
      TableSettings = Map [ "anon_table", { AidColumns = [ "id" ] } ]
  }

let dataProvider = dummyDataProvider testTables
let queryContext = QueryContext.make anonParams dataProvider

let analyzeQuery queryString =
  queryString
  |> Parser.parse
  |> Analyzer.analyze queryContext
  |> Normalizer.normalize
  |> Analyzer.anonymize queryContext
  |> ignore

let assertAnalyzeError queryString (errorFragment: string) =
  try
    analyzeQuery queryString
    failwith "Was expecting query analysis to fail"
  with
  | ex ->
    let str = ex.Message.ToLower()

    if str.Contains(errorFragment.ToLower()) then
      ()
    else
      failwith $"Expecting error to contain '%s{errorFragment}'. Got '%s{str}' instead."

let ensureAnalyzeFails queryString errorFragment =
  assertAnalyzeError queryString errorFragment

[<Fact>]
let ``Fail on sum aggregate`` () =
  assertAnalyzeError "SELECT sum(int_col) FROM anon_table" "only count"

[<Fact>]
let ``Allow count(*) and count(distinct column)`` () =
  analyzeQuery "SELECT count(*) FROM anon_table"
  analyzeQuery "SELECT count(distinct int_col) FROM anon_table"

[<Fact>]
let ``Disallow multiple low count aggregators`` () =
  assertAnalyzeError
    "SELECT count(*), diffix_low_count(int_col), diffix_low_count(str_col) FROM direct_table"
    "single low count aggregator is allowed"

[<Fact>]
let ``Disallow anonymizing queries with JOINs`` () =
  assertAnalyzeError
    "SELECT count(*) FROM anon_table JOIN anon_table AS t ON true"
    "JOIN in anonymizing queries is not currently supported"

[<Fact>]
let ``Disallow anonymizing queries with subqueries`` () =
  assertAnalyzeError
    "SELECT count(*) FROM (SELECT 1 FROM anon_table) x"
    "Subqueries in anonymizing queries are not currently supported"

[<Fact>]
let ``Allow limiting top query`` () =
  analyzeQuery "SELECT count(*) FROM anon_table LIMIT 1"

[<Fact>]
let ``Disallow anonymizing queries with WHERE`` () =
  assertAnalyzeError
    "SELECT count(*) FROM anon_table WHERE str_col=''"
    "WHERE in anonymizing queries is not currently supported"

[<Fact>]
let ``Don't validate not anonymizing queries for unsupported anonymization features`` () =
  // Subqueries, JOINs, WHEREs, other aggregators etc.
  analyzeQuery
    "SELECT sum(z.int_col) FROM (SELECT t.int_col FROM direct_table JOIN direct_table AS t ON true) z WHERE z.int_col=0"
