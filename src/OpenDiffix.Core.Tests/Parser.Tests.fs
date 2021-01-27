module OpenDiffix.Core.ParserTests

open Xunit
open FParsec.CharParsers
open OpenDiffix.Core
open OpenDiffix.Core.Parser.QueryParser
open OpenDiffix.Core.ParserTypes

let defaultSelect =
  {
    SelectDistinct = false
    Expressions = []
    From = Identifier "table"
    Where = None
    GroupBy = []
    Having = None
  }

let parse p string =
  match run p string with
  | ParserResult.Success (value, _, _) -> Ok value
  | ParserResult.Failure (error, _, _) -> Error error

[<Fact>]
let ``Parses simple identifiers`` () =
  assertOkEqual (parse commaSepExpressions "hello") [ Identifier "hello" ]
  assertOkEqual (parse commaSepExpressions "hello, world") [ Identifier "hello"; Identifier "world" ]
  assertOkEqual (parse commaSepExpressions "hello12") [ Identifier "hello12" ]
  assertOkEqual (parse commaSepExpressions "hello_12") [ Identifier "hello_12" ] // Allows underscores
  assertOkEqual (parse commaSepExpressions "hello.bar12") [ Identifier "hello.bar12" ] // Allows punctuated names

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
  |> List.iter (fun (value, expected) -> assertOkEqual (parse expr value) expected)

  [ "+"; "-"; "*"; "/"; "^"; "%" ]
  |> List.iter (fun op -> assertOkEqual (parse expr $"1 %s{op} 1") (Expression.Function(op, [ Integer 1; Integer 1 ])))

  [ "and", And; "or", Or; "<", Lt; "<=", LtE; ">", Gt; ">=", GtE; "=", Equals; "<>", Not << Equals ]
  |> List.iter (fun (op, expected) -> assertOkEqual (parse expr $"1 %s{op} 1") (expected (Integer 1, Integer 1)))

  assertOkEqual (parse expr "not 1") (Expression.Not(Integer 1))
  assertOkEqual (parse expr "value is null") (Equals(Identifier "value", Null))
  assertOkEqual (parse expr "value is not null") (Not(Equals(Identifier "value", Null)))
  assertOkEqual (parse expr "value as alias") (As(Identifier "value", Identifier "alias"))


[<Fact>]
let ``Parses columns`` () =
  assertOkEqual (parse commaSepExpressions "hello") [ Identifier "hello" ]
  assertOkEqual (parse commaSepExpressions "hello, world") [ Identifier "hello"; Identifier "world" ]
  assertOkEqual (parse commaSepExpressions "hello,world") [ Identifier "hello"; Identifier "world" ]
  assertOkEqual (parse commaSepExpressions "hello ,world") [ Identifier "hello"; Identifier "world" ]

[<Fact>]
let ``Parses functions`` () =
  assertOkEqual (parse commaSepExpressions "hello(world)") [ Function("hello", [ Identifier "world" ]) ]
  assertOkEqual (parse commaSepExpressions "hello ( world )") [ Function("hello", [ Identifier "world" ]) ]

  assertOkEqual
    (parse commaSepExpressions "hello(world), hello(moon)")
    [ Function("hello", [ Identifier "world" ]); Function("hello", [ Identifier "moon" ]) ]

[<Fact>]
let ``Precedence is as expected`` () =
  assertOkEqual
    (parse expr "1 + 2 * 3^2 < 1 AND a or not b IS NULL")
    (And(
      Lt(Function("+", [ Integer 1; Function("*", [ Integer 2; Function("^", [ Integer 3; Integer 2 ]) ]) ]), Integer 1),
      Or(Identifier "a", Not(Equals(Identifier "b", Null)))
    ))

[<Fact>]
let ``Parses count(*)`` () =
  let expected = Function("count", [ Star ])
  assertOkEqual (parse commaSepExpressions "count(*)") [ expected ]
  assertOkEqual (parse commaSepExpressions "count( *     )") [ expected ]

[<Fact>]
let ``Parses count(distinct col)`` () =
  let expected = Function("count", [ Distinct(Identifier "col") ])
  assertOkEqual (parse commaSepExpressions "count(distinct col)") [ expected ]
  assertOkEqual (parse commaSepExpressions "count ( distinct     col )") [ expected ]

[<Fact>]
let ``Parses complex functions`` () =
  let expected = Function("length", [ Function("sum", [ Function("+", [ Identifier "rain"; Identifier "sun" ]) ]) ])
  assertOkEqual (parse expr "length(sum(rain + sun))") expected

[<Fact>]
let ``Parses WHERE clause conditions`` () =
  assertOkEqual (parse whereClause "WHERE a = 1") (Equals(Identifier "a", Integer 1))
  assertOkEqual (parse whereClause "WHERE a = '1'") (Equals(Identifier "a", String "1"))

[<Fact>]
let ``Parses GROUP BY statement`` () =
  assertOkEqual (parse groupBy "GROUP BY a, b, c") [ Identifier "a"; Identifier "b"; Identifier "c" ]
  assertOkEqual (parse groupBy "GROUP BY a") [ Identifier "a" ]
  assertError (parse groupBy "GROUP BY")

[<Fact>]
let ``Parses SELECT by itself`` () =
  assertOkEqual
    (parse selectQuery "SELECT col FROM table")
    (SelectQuery { defaultSelect with Expressions = [ Identifier "col" ] })

[<Fact>]
let ``Parses SELECT DISTINCT`` () =
  assertOkEqual
    (parse selectQuery "SELECT DISTINCT col FROM table")
    (SelectQuery { defaultSelect with SelectDistinct = true; Expressions = [ Identifier "col" ] })

[<Fact>]
let ``Fails on unexpected input`` () = assertError (Parser.parse "Foo")

[<Fact>]
let ``Parse SELECT query with columns and table`` () =
  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table")
    { defaultSelect with Expressions = [ Identifier "col1"; Identifier "col2" ] }

  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table ;")
    { defaultSelect with Expressions = [ Identifier "col1"; Identifier "col2" ] }

[<Fact>]
let ``Multiline select`` () =
  assertOkEqual
    (Parser.parse
      """
         SELECT
           col1
         FROM
           table
         """)
    { defaultSelect with Expressions = [ Identifier "col1" ] }

[<Fact>]
let ``Parse aggregate query`` () =
  assertOkEqual
    (Parser.parse
      """
         SELECT col1, count(distinct aid)
         FROM table
         GROUP BY col1
         """)
    { defaultSelect with
        Expressions = [ Identifier "col1"; Function("count", [ Distinct(Identifier "aid") ]) ]
        GroupBy = [ Identifier "col1" ]
    }

[<Fact>]
let ``Parse complex aggregate query`` () =
  assertOkEqual
    (Parser.parse
      """
         SELECT col1 as colAlias, count(distinct aid)
         FROM table
         WHERE col1 = 1 AND col2 = 2 or col2 = 3
         GROUP BY col1
         HAVING count(distinct aid) > 1
         """)
    { defaultSelect with
        Expressions =
          [ As(Identifier "col1", Identifier "colAlias"); Function("count", [ Distinct(Identifier "aid") ]) ]
        Where =
          Some(
            And(
              Equals(Identifier "col1", Integer 1),
              (Or(Equals(Identifier "col2", Integer 2), Equals(Identifier "col2", Integer 3)))
            )
          )
        GroupBy = [ Identifier "col1" ]
        Having = Some <| Gt(Function("count", [ Distinct(Identifier "aid") ]), Integer 1)
    }
