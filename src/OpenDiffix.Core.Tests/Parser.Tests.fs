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

let plainColumn = ColumnName >> PlainColumn >> Column

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
  assertOkEqual (parse SelectQueries.expressions "hello") [ plainColumn "hello" ]

  assertOkEqual (parse SelectQueries.expressions "hello, world") [ plainColumn "hello"; plainColumn "world" ]

  assertOkEqual (parse SelectQueries.expressions "hello,world") [ plainColumn "hello"; plainColumn "world" ]

  assertOkEqual (parse SelectQueries.expressions "hello ,world") [ plainColumn "hello"; plainColumn "world" ]

[<Fact>]
let ``Parses functions`` () =
  assertOkEqual (parse SelectQueries.expressions "hello(world)") [ Function("hello", plainColumn "world") ]
  assertOkEqual (parse SelectQueries.expressions "hello ( world )") [ Function("hello", plainColumn "world") ]

  assertOkEqual
    (parse SelectQueries.expressions "hello(world), hello(moon)")
    [
      Function("hello", plainColumn "world")
      Function("hello", plainColumn "moon")
    ]

[<Fact>]
let ``Parses count(distinct col)`` () =
  let expected =
    AggregateFunction(AnonymizedCount(Distinct(ColumnName "col")))

  assertOkEqual (parse SelectQueries.expressions "count(distinct col)") [ expected ]
  assertOkEqual (parse SelectQueries.expressions "count ( distinct     col )") [ expected ]

[<Fact>]
let ``Parses optional semicolon`` () =
  assertOk (parse skipSemiColon ";")
  assertOk (parse skipSemiColon "")

[<Fact>]
let ``Parses GROUP BY statement`` () =
  assertOkEqual (parse SelectQueries.groupBy "GROUP BY a, b, c") [ ColumnName "a"; ColumnName "b"; ColumnName "c" ]

  assertOkEqual (parse SelectQueries.groupBy "GROUP BY a") [ ColumnName "a" ]
  assertError (parse SelectQueries.groupBy "GROUP BY")

[<Fact>]
let ``Parses SELECT by itself`` () =
  assertOkEqual
    (parse SelectQueries.parse "SELECT col FROM table")
    (SelectQuery
      {
        Expressions = [ plainColumn "col" ]
        From = Table(TableName "table")
      })

[<Fact>]
let ``Fails on unexpected input`` () = assertError (Parser.parse "Foo")

[<Fact>]
let ``Parses "SHOW tables"`` () =
  assertOkEqual (Parser.parse "show tables") ShowTables

[<Fact>]
let ``Parses "SHOW columns FROM bar"`` () =
  assertOkEqual (Parser.parse "show columns FROM bar") (ShowColumnsFromTable(TableName "bar"))

[<Fact>]
let ``Not sensitive to whitespace`` () =
  assertOkEqual<Query, _>
    (Parser.parse "   show
                   tables   ")
    ShowTables

[<Fact>]
let ``Parse SELECT query with columns and table`` () =
  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table")
    (SelectQuery
      {
        Expressions = [ plainColumn "col1"; plainColumn "col2" ]
        From = Table(TableName "table")
      })

  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table ;")
    (SelectQuery
      {
        Expressions = [ plainColumn "col1"; plainColumn "col2" ]
        From = Table(TableName "table")
      })

[<Fact>]
let ``Parse aggregate query`` () =
  assertOkEqual
    (Parser.parse """
         SELECT col1, count(distinct aid)
         FROM table
         GROUP BY col1
         """)
    (AggregateQuery
      {
        Expressions =
          [
            plainColumn "col1"
            Distinct(ColumnName "aid")
            |> AnonymizedCount
            |> AggregateFunction
          ]
        From = Table(TableName "table")
        GroupBy = [ ColumnName "col1" ]
      })