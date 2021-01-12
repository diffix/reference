module OpenDiffix.Core.ParserTests

open Xunit
open FParsec.CharParsers
open OpenDiffix.Core
open OpenDiffix.Core.ParserTypes
open OpenDiffix.Core.Parser.Definitions

let parse p string =
  match run p string with
  | ParserResult.Success (value, _, _) -> Ok value
  | ParserResult.Failure (error, _, _) -> Error error

let distinct = Term "distinct"
let star = Operator Star

[<Fact>]
let ``Parses words`` () =
  assertOkEqual (parse anyWord "hello") "hello"
  assertError (parse anyWord ",hello") // Rejects something not starting with a character
  assertOkEqual (parse anyWord "hello, world") "hello" // Parses until special token
  assertOkEqual (parse anyWord "hello12") "hello12" // Parses digits too
  assertError (parse anyWord "12hello") // Rejects things starting with digits
  assertOkEqual (parse anyWord "hello_12") "hello_12" // Allows underscores

[<Fact>]
let ``Parses terms`` () =
  assertOkEqual (parse SelectQueries.term "+") (Expression.Operator Operator.Plus)
  assertOkEqual (parse SelectQueries.term "-") (Expression.Operator Operator.Minus)
  assertOkEqual (parse SelectQueries.term "*") (Expression.Operator Operator.Star)
  assertOkEqual (parse SelectQueries.term "/") (Expression.Operator Operator.Slash)
  assertOkEqual (parse SelectQueries.term "^") (Expression.Operator Operator.Hat)
  assertOkEqual (parse SelectQueries.term "not") (Expression.Operator Operator.Not)
  assertOkEqual (parse SelectQueries.term "false") (Expression.Constant (Boolean false))
  assertOkEqual (parse SelectQueries.term "true") (Expression.Constant (Boolean true))
  assertOkEqual (parse SelectQueries.term "1") (Expression.Constant (Integer 1))
  assertOkEqual (parse SelectQueries.term "hello") (Expression.Term "hello")

[<Fact>]
let ``Parses columns`` () =
  assertOkEqual (parse SelectQueries.commaSepExpressions "hello") [ Term "hello" ]
  assertOkEqual (parse SelectQueries.commaSepExpressions "hello, world") [ Term "hello"; Term "world" ]
  assertOkEqual (parse SelectQueries.commaSepExpressions "hello,world") [ Term "hello"; Term "world" ]
  assertOkEqual (parse SelectQueries.commaSepExpressions "hello ,world") [ Term "hello"; Term "world" ]

[<Fact>]
let ``Parses functions`` () =
  assertOkEqual (parse SelectQueries.commaSepExpressions "hello(world)") [ Function("hello", [Term "world"]) ]
  assertOkEqual (parse SelectQueries.commaSepExpressions "hello ( world )") [ Function("hello", [Term "world"]) ]
  assertOkEqual
    (parse SelectQueries.commaSepExpressions "hello(world), hello(moon)")
    [ Function("hello", [Term "world"]); Function("hello", [Term "moon"]) ]

[<Fact>]
let ``Parses function args`` () =
  assertOkEqual (parse SelectQueries.spaceSepUnaliasedExpressions "* distinct foo")
    [star; distinct; Term "foo"]

[<Fact>]
let ``Parses count(*)`` () =
  let expected = Function("count", [star])
  assertOkEqual (parse SelectQueries.commaSepExpressions "count(*)") [ expected ]
  assertOkEqual (parse SelectQueries.commaSepExpressions "count( *     )") [ expected ]

[<Fact>]
let ``Parses count(distinct col)`` () =
  let expected = Function("count", [distinct; Term "col"])
  assertOkEqual (parse SelectQueries.commaSepExpressions "count(distinct col)") [ expected ]
  assertOkEqual (parse SelectQueries.commaSepExpressions "count ( distinct     col )") [ expected ]

[<Fact>]
let ``Parses complex functions`` () =
  let expected = Function("length", [Function("sum", [Term "rain"; (Operator Plus); Term "sun"])])
  assertOkEqual (parse SelectQueries.commaSepExpressions "length(sum(rain + sun))") [ expected ]

[<Fact>]
let ``Parses optional semicolon`` () =
  assertOk (parse skipSemiColon ";")
  assertOk (parse skipSemiColon "")

[<Fact>]
let ``Parses GROUP BY statement`` () =
  assertOkEqual (parse SelectQueries.groupBy "GROUP BY a, b, c") [ Term "a"; Term "b"; Term "c" ]
  assertOkEqual (parse SelectQueries.groupBy "GROUP BY a") [ Term "a" ]
  assertError (parse SelectQueries.groupBy "GROUP BY")

[<Fact>]
let ``Parses SELECT by itself`` () =
  assertOkEqual
    (parse SelectQueries.parse "SELECT col FROM table")
    (SelectQuery { Expressions = [ Term "col" ]; From = Table "table"; GroupBy = []})

[<Fact>]
let ``Fails on unexpected input`` () = assertError (Parser.parse "Foo")

[<Fact>]
let ``Parses "SHOW tables"`` () = assertOkEqual (Parser.parse "show tables") (Show ShowQuery.Tables)

[<Fact>]
let ``Parses "SHOW columns FROM bar"`` () =
  assertOkEqual (Parser.parse "show columns FROM bar") (Show (ShowQuery.Columns("bar")))

[<Fact>]
let ``Not sensitive to whitespace`` () =
  assertOkEqual<Query, _>
    (Parser.parse
      "   show
                   tables   ")
    (Show ShowQuery.Tables)

[<Fact>]
let ``Parse SELECT query with columns and table`` () =
  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table")
    (SelectQuery
      {
        Expressions = [ Term "col1"; Term "col2" ]
        From = Table "table"
        GroupBy = []
      })

  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table ;")
    (SelectQuery
      {
        Expressions = [ Term "col1"; Term "col2" ]
        From = Table "table"
        GroupBy = []
      })

[<Fact>]
let ``Parse aggregate query`` () =
  assertOkEqual
    (Parser.parse
      """
         SELECT col1, count(distinct aid)
         FROM table
         GROUP BY col1
         """)
    (SelectQuery
      {
        Expressions = [ Term "col1"; Function("count", [distinct; Term "aid"]) ]
        From = Table "table"
        GroupBy = [ Term "col1" ]
      })
