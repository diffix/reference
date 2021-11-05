module OpenDiffix.Core.NormalizerTests

open Xunit
open FsUnit.Xunit

let testTable =
  {
    Name = "table"
    Columns =
      [ //
        { Name = "id"; Type = IntegerType }
        { Name = "name"; Type = StringType }
        { Name = "age"; Type = IntegerType }
        { Name = "valid"; Type = BooleanType }
      ]
  }

let dataProvider = dummyDataProvider [ testTable ]
let queryContext = QueryContext.make AnonymizationParams.Default dataProvider

let queryPlan statement =
  statement
  |> Parser.parse
  |> Analyzer.analyze queryContext
  |> Normalizer.normalize

let equivalentQueries expectedQuery testQuery =
  let testPlan = queryPlan testQuery
  let expectedPlan = queryPlan expectedQuery
  testPlan |> should equal expectedPlan

[<Fact>]
let ``normalize constants (1)`` () =
  equivalentQueries //
    "SELECT 1 + 2 AS c FROM table"
    "SELECT 3 AS c FROM table"

[<Fact>]
let ``normalize constants (2)`` () =
  equivalentQueries //
    "SELECT name FROM table WHERE 1 < 2 AND 2 = 1 + 1"
    "SELECT name FROM table"

[<Fact>]
let ``normalize constants (3)`` () =
  equivalentQueries //
    "SELECT c FROM (SELECT 1 + 2 AS c FROM table) t"
    "SELECT c FROM (SELECT 3 AS c FROM table) t"

[<Fact>]
let ``normalize equality`` () =
  equivalentQueries //
    "SELECT COUNT(*) FROM table WHERE age = 3"
    "SELECT COUNT(*) FROM table WHERE 3 = age"

[<Fact>]
let ``normalize inequalities`` () =
  equivalentQueries //
    "SELECT COUNT(*) FROM table WHERE 4 < age AND 10 >= age"
    "SELECT COUNT(*) FROM table WHERE age >= 4 AND age < 10"

[<Fact>]
let ``normalize not (1)`` () =
  equivalentQueries //
    "SELECT NOT (NOT age = 3) FROM table"
    "SELECT age = 3 FROM table"

[<Fact>]
let ``normalize not (2)`` () =
  equivalentQueries //
    "SELECT NOT age > 3 FROM table"
    "SELECT age <= 3 FROM table"

[<Fact>]
let ``normalize boolean comparisons (1)`` () =
  equivalentQueries //
    "SELECT (NOT valid) = (NOT valid) FROM table"
    "SELECT valid = valid FROM table"

[<Fact>]
let ``normalize boolean comparisons (2)`` () =
  equivalentQueries //
    "SELECT NOT ((NOT valid) = TRUE) AS b FROM table"
    "SELECT valid = TRUE AS b FROM table"

[<Fact>]
let ``normalize boolean comparisons (3)`` () =
  equivalentQueries //
    "SELECT valid = TRUE AS b FROM table"
    "SELECT valid AS b FROM table"

[<Fact>]
let ``normalize boolean comparisons (4)`` () =
  equivalentQueries //
    "SELECT valid = FALSE AS b FROM table"
    "SELECT NOT valid AS b FROM table"

[<Fact>]
let ``normalize casts (1)`` () =
  equivalentQueries //
    "SELECT cast(valid AS boolean) AS c FROM table"
    "SELECT valid as c FROM table"

[<Fact>]
let ``normalize casts (2)`` () =
  equivalentQueries //
    "SELECT cast(cast(age AS integer) as real) AS c FROM table"
    "SELECT cast(age as real) as c FROM table"
