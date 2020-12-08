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
    assertOkEqual (parse anyWord "hello") "hello"
    assertError (parse anyWord ",hello") // Rejects something not starting with a character
    assertOkEqual (parse anyWord "hello, world") "hello" // Parses until special token
    assertOkEqual (parse anyWord "hello12") "hello12" // Parses digits too
    assertError (parse anyWord "12hello") // Rejects things starting with digits
    assertOkEqual (parse anyWord "hello_12") "hello_12" // Allows underscores

[<Fact>]
let ``Parses columns`` () =
    assertOkEqual (parse SelectQueries.expressions "hello") [Column (PlainColumn "hello")]
    assertOkEqual (parse SelectQueries.expressions "hello, world") [Column (PlainColumn "hello"); Column (PlainColumn "world")]
    assertOkEqual (parse SelectQueries.expressions "hello,world") [(Column (PlainColumn "hello")); Column (PlainColumn "world")]
    assertOkEqual (parse SelectQueries.expressions "hello ,world") [(Column (PlainColumn "hello")); Column (PlainColumn "world")]
    
[<Fact>]
let ``Parses functions`` () =
    assertOkEqual (parse SelectQueries.expressions "hello(world)") [Function ("hello", (Column (PlainColumn "world")))]
    assertOkEqual (parse SelectQueries.expressions "hello ( world )") [Function ("hello", (Column (PlainColumn "world")))]
    assertOkEqual
        (parse SelectQueries.expressions "hello(world), hello(moon)")
        [
          Function ("hello", (Column (PlainColumn "world")))
          Function ("hello", (Column (PlainColumn "moon")))
        ]
        
[<Fact>]
let ``Parses count(distinct col)`` () =
    let expected = AggregateFunction (AnonymizedCount (Distinct (PlainColumn "col")))
    assertOkEqual (parse SelectQueries.expressions "count(distinct col)") [expected]
    assertOkEqual (parse SelectQueries.expressions "count ( distinct     col )") [expected]
    
[<Fact>]
let ``Parses optional semicolon`` () =
    assertOk (parse skipSemiColon ";")
    assertOk (parse skipSemiColon "")
    
[<Fact>]
let ``Parses GROUP BY statement`` () =
    assertOkEqual (parse SelectQueries.groupBy "GROUP BY a, b, c") ["a"; "b"; "c"]
    assertOkEqual (parse SelectQueries.groupBy "GROUP BY a") ["a"]
    assertError (parse SelectQueries.groupBy "GROUP BY")
    
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
    assertOkEqual
        (parseSql "SELECT col1, col2 FROM table ;")
        (SelectQuery {Expressions = [Column (PlainColumn "col1"); Column (PlainColumn "col2")]; From = Table "table"})
        
[<Fact>]
let ``Parse aggregate query`` () =
    assertOkEqual
        (parseSql """
         SELECT col1, count(distinct aid)
         FROM table
         GROUP BY col1
         """)
        (AggregateQuery {
            Expressions = [
                Column (PlainColumn "col1")
                Distinct (PlainColumn "aid") |> AnonymizedCount |> AggregateFunction
            ]
            From = Table "table"
            GroupBy = ["col1"]
        })
