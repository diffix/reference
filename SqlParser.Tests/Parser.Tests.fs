module Tests

open FParsec.CharParsers
open SqlParser.ParserDefinition
open TestHelpers
open SqlParser.Parser
open SqlParser.Query
open Xunit

let parse p string =
    match run p string with
    | ParserResult.Success (value, _, _) -> Ok value
    | ParserResult.Failure (error, _, _) -> Error error

[<Fact>]
let ``Parses words`` () =
    assertOkEqual (parse pAnyWord "hello") "hello"
    assertError (parse pAnyWord ",hello") // Rejects something not starting with a character
    assertOkEqual (parse pAnyWord "hello, world") "hello" // Parses until special token
    assertOkEqual (parse pAnyWord "hello12") "hello12" // Parses digits too
    assertError (parse pAnyWord "12hello") // Rejects things starting with digits
    assertOkEqual (parse pAnyWord "hello_12") "hello_12" // Allows underscores

[<Fact>]
let ``Parses columns`` () =
    assertOkEqual (parse SelectQueries.pExpressions "hello") [Column (PlainColumn "hello")]
    assertOkEqual (parse SelectQueries.pExpressions "hello, world") [Column (PlainColumn "hello"); Column (PlainColumn "world")]
    assertOkEqual (parse SelectQueries.pExpressions "hello,world") [(Column (PlainColumn "hello")); Column (PlainColumn "world")]
    assertOkEqual (parse SelectQueries.pExpressions "hello ,world") [(Column (PlainColumn "hello")); Column (PlainColumn "world")]
    
[<Fact>]
let ``Parses functions`` () =
    assertOkEqual (parse SelectQueries.pExpressions "hello(world)") [Function ("hello", (Column (PlainColumn "world")))]
    assertOkEqual (parse SelectQueries.pExpressions "hello ( world )") [Function ("hello", (Column (PlainColumn "world")))]
    assertOkEqual
        (parse SelectQueries.pExpressions "hello(world), hello(moon)")
        [
          Function ("hello", (Column (PlainColumn "world")))
          Function ("hello", (Column (PlainColumn "moon")))
        ]
    
[<Fact>]
let ``Parses SELECT by itself`` () =
    assertOkEqual
        (parse SelectQueries.parse "SELECT col FROM table")
        (SelectQuery {Expressions = [Column (PlainColumn "col")]; From = Table "table"})

[<Fact>]
let ``Fails on unexpected input`` () =
    assertError (parseSql "Foo")

[<Fact>]
let ``Parses "SHOW tables"`` () =
    assertOkEqual (parseSql "show tables") ShowTables

[<Fact>]
let ``Parses "SHOW columns FROM bar"`` () =
    assertOkEqual (parseSql "show columns FROM bar") (ShowColumnsFromTable "bar")

[<Fact>]
let ``Not sensitive to whitespace`` () =
    assertOkEqual<Query, _> (parseSql "   show
                   tables   ") ShowTables

[<Fact>]
let ``Parse SELECT query with columns and table`` () =
    assertOkEqual
        (parseSql "SELECT col1, col2 FROM table")
        (SelectQuery {Expressions = [Column (PlainColumn "col1"); Column (PlainColumn "col2")]; From = Table "table"})
