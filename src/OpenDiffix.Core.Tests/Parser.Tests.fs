module OpenDiffix.Core.ParserTests

open Xunit
open FParsec.CharParsers
open OpenDiffix.Core
open OpenDiffix.Core.Parser.Definitions
open OpenDiffix.Core.ParserTypes

let parse p string =
  match run p string with
  | ParserResult.Success (value, _, _) -> Ok value
  | ParserResult.Failure (error, _, _) -> Error error

[<Fact>]
let ``Parses simple identifiers`` () =
  assertOkEqual (parse commaSepExpressions "hello") [Identifier "hello"]
  assertOkEqual (parse commaSepExpressions "hello, world") [Identifier "hello"; Identifier "world"]
  assertOkEqual (parse commaSepExpressions "hello12") [Identifier "hello12"]
  assertOkEqual (parse commaSepExpressions "hello_12") [Identifier "hello_12"] // Allows underscores
  assertOkEqual (parse commaSepExpressions "hello.bar12") [Identifier "hello.bar12"] // Allows punctuated names

[<Fact>]
let ``Parses expressions`` () =
  [
    "1", Integer 1
    "1.1", Float 1.1
    "1.01", Float 1.01
    "1.001", Float 1.001
    "'hello'", String "hello"
    "true", Boolean true
    "false", Boolean false
  ]
  |> List.iter(fun (value, expected) -> assertOkEqual (parse expr value) expected)

  [
    "+"
    "-"
    "*"
    "/"
    "^"
    "%"
  ]
  |> List.iter(fun op ->
    assertOkEqual (parse expr $"1 %s{op} 1") (Expression.Function (op, [Integer 1; Integer 1]))
  )

  [
    "and", And
    "or", Or
    "<", Lt
    "<=", LtE
    ">", Gt
    ">=", GtE
    "=", Equal
    "<>", Not << Equal
  ]
  |> List.iter(fun (op, expected) ->
    assertOkEqual (parse expr $"1 %s{op} 1") (expected (Integer 1, Integer 1))
  )

  assertOkEqual (parse expr "not 1") (Expression.Not (Integer 1))
  assertOkEqual (parse expr "value is null") (Equal (Identifier "value", Null))
  assertOkEqual (parse expr "value is not null") (Not (Equal (Identifier "value", Null)))
  assertOkEqual (parse expr "value as alias") (As (Identifier "value", Identifier "alias"))


[<Fact>]
let ``Parses columns`` () =
  assertOkEqual (parse commaSepExpressions "hello") [ Identifier "hello" ]
  assertOkEqual (parse commaSepExpressions "hello, world") [ Identifier "hello"; Identifier "world" ]
  assertOkEqual (parse commaSepExpressions "hello,world") [ Identifier "hello"; Identifier "world" ]
  assertOkEqual (parse commaSepExpressions "hello ,world") [ Identifier "hello"; Identifier "world" ]

[<Fact>]
let ``Parses functions`` () =
  assertOkEqual (parse commaSepExpressions "hello(world)") [ Function("hello", [Identifier "world"]) ]
  assertOkEqual (parse commaSepExpressions "hello ( world )") [ Function("hello", [Identifier "world"]) ]
  assertOkEqual
    (parse commaSepExpressions "hello(world), hello(moon)")
    [ Function("hello", [Identifier "world"]); Function("hello", [Identifier "moon"]) ]

[<Fact>]
let ``Precedence is as expected`` () =
  assertOkEqual (parse expr "1 + 2 * 3^2 < 1 AND a or not b IS NULL") (
    And(
      Lt(
        Function("+", [
          Integer 1
          Function("*", [
            Integer 2
            Function("^", [Integer 3; Integer 2])
          ])
        ]),
        Integer 1
      ),
      Or(
        Identifier "a",
        Not(
          Equal(
            Identifier "b",
            Null
          )
        )
      )
    )
  )

[<Fact>]
let ``Parses count(*)`` () =
  let expected = Function("count", [Star])
  assertOkEqual (parse commaSepExpressions "count(*)") [ expected ]
  assertOkEqual (parse commaSepExpressions "count( *     )") [ expected ]

[<Fact>]
let ``Parses count(distinct col)`` () =
  let expected = Function("count", [Distinct (Identifier "col")])
  assertOkEqual (parse commaSepExpressions "count(distinct col)") [ expected ]
  assertOkEqual (parse commaSepExpressions "count ( distinct     col )") [ expected ]

[<Fact>]
let ``Parses complex functions`` () =
  let expected = Function("length", [Function("sum", [Function("+", [Identifier "rain"; Identifier "sun"])])])
  assertOkEqual (parse expr "length(sum(rain + sun))") expected

[<Fact>]
let ``Parses WHERE clause conditions`` () =
  assertOkEqual (parse whereClause "WHERE a = 1") (Equal (Identifier "a", Integer 1))
  assertOkEqual (parse whereClause "WHERE a = '1'") (Equal (Identifier "a", String "1"))

[<Fact>]
let ``Parses GROUP BY statement`` () =
  assertOkEqual (parse groupBy "GROUP BY a, b, c") [ Identifier "a"; Identifier "b"; Identifier "c" ]
  assertOkEqual (parse groupBy "GROUP BY a") [ Identifier "a" ]
  assertError (parse groupBy "GROUP BY")

[<Fact>]
let ``Parses SELECT by itself`` () =
  assertOkEqual
    (parse parseSelectQuery "SELECT col FROM table")
    (SelectQuery { SelectDistinct = false; Expressions = [ Identifier "col" ]; From = Identifier "table"; Where = None; GroupBy = []})

[<Fact>]
let ``Parses SELECT DISTINCT`` () =
  assertOkEqual
    (parse parseSelectQuery "SELECT DISTINCT col FROM table")
    (SelectQuery { SelectDistinct = true; Expressions = [ Identifier "col" ]; From = Identifier "table"; Where = None; GroupBy = []})

[<Fact>]
let ``Fails on unexpected input`` () = assertError (Parser.parse "Foo")

[<Fact>]
let ``Parses "SHOW tables"`` () = assertOkEqual (Parser.parse "show tables") (ShowQuery ShowQuery.Tables)

[<Fact>]
let ``Parses "SHOW columns FROM bar"`` () =
  assertOkEqual (Parser.parse "show columns FROM bar") (ShowQuery (ShowQuery.Columns("bar")))

[<Fact>]
let ``Not sensitive to whitespace`` () =
  assertOkEqual<Expression, _>
    (Parser.parse
      "   show
                   tables   ")
    (ShowQuery ShowQuery.Tables)

[<Fact>]
let ``Parse SELECT query with columns and table`` () =
  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table")
    (SelectQuery
      {
        SelectDistinct = false
        Expressions = [ Identifier "col1"; Identifier "col2" ]
        From = Identifier "table"
        Where = None
        GroupBy = []
      })

  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table ;")
    (SelectQuery
      {
        SelectDistinct = false
        Expressions = [ Identifier "col1"; Identifier "col2" ]
        From = Identifier "table"
        Where = None
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
        SelectDistinct = false
        Expressions = [ Identifier "col1"; Function("count", [Distinct(Identifier "aid")]) ]
        From = Identifier "table"
        Where = None
        GroupBy = [ Identifier "col1" ]
      })

[<Fact>]
let ``Parse aggregate query with where clause`` () =
  assertOkEqual
    (Parser.parse
      """
         SELECT col1, count(distinct aid)
         FROM table
         WHERE col1 = 1 AND col2 = 2 or col2 = 3
         GROUP BY col1
         """)
    (SelectQuery
      {
        SelectDistinct = false
        Expressions = [ Identifier "col1"; Function("count", [Distinct(Identifier "aid")]) ]
        From = Identifier "table"
        Where =
          Some
            (And (
              Equal (Identifier "col1", Integer 1),
              (Or (
                Equal (Identifier "col2", Integer 2),
                Equal (Identifier "col2", Integer 3)
              ))
            ))
        GroupBy = [ Identifier "col1" ]
      })
