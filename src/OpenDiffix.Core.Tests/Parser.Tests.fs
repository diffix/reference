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
    From = Table("table", None)
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
  assertOkEqual (parse expr "hello") (Identifier(None, "hello"))
  assertOkEqual (parse expr "hello12") (Identifier(None, "hello12"))
  assertOkEqual (parse expr "hello_12") (Identifier(None, "hello_12")) // Allows underscores
  assertOkEqual (parse expr "hello.bar12") (Identifier(Some "hello", "bar12")) // Allows qualified names

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
  assertOkEqual (parse expr "value is null") (Equals(Identifier(None, "value"), Null))
  assertOkEqual (parse expr "value is not null") (Not(Equals(Identifier(None, "value"), Null)))


[<Fact>]
let ``Parses columns`` () =
  assertOkEqual (parse commaSepExpressions "hello") [ As(Identifier(None, "hello"), None) ]
  assertOkEqual (parse commaSepExpressions "hello as alias") [ As(Identifier(None, "hello"), Some "alias") ]

  assertOkEqual
    (parse commaSepExpressions "hello, world")
    [ As(Identifier(None, "hello"), None); As(Identifier(None, "world"), None) ]

  assertOkEqual
    (parse commaSepExpressions "hello,world")
    [ As(Identifier(None, "hello"), None); As(Identifier(None, "world"), None) ]

  assertOkEqual
    (parse commaSepExpressions "hello ,world")
    [ As(Identifier(None, "hello"), None); As(Identifier(None, "world"), None) ]

[<Fact>]
let ``Parses functions`` () =
  assertOkEqual (parse expr "hello(world)") (Function("hello", [ Identifier(None, "world") ]))
  assertOkEqual (parse expr "hello ( world )") (Function("hello", [ Identifier(None, "world") ]))

  assertOkEqual
    (parse commaSepExpressions "hello(world), hello(moon)")
    [
      As(Function("hello", [ Identifier(None, "world") ]), None)
      As(Function("hello", [ Identifier(None, "moon") ]), None)
    ]

[<Fact>]
let ``Precedence is as expected`` () =
  assertOkEqual
    (parse expr "1 + 2 * 3^2 < 1 AND a or not b IS NULL")
    (And(
      Lt(Function("+", [ Integer 1; Function("*", [ Integer 2; Function("^", [ Integer 3; Integer 2 ]) ]) ]), Integer 1),
      Or(Identifier(None, "a"), Not(Equals(Identifier(None, "b"), Null)))
    ))

[<Fact>]
let ``Parses count(*)`` () =
  let expected = Function("count", [ Star ])
  assertOkEqual (parse expr "count(*)") expected
  assertOkEqual (parse expr "count( *     )") expected

[<Fact>]
let ``Parses count(distinct col)`` () =
  let expected = Function("count", [ Distinct(Identifier(None, "col")) ])
  assertOkEqual (parse expr "count(distinct col)") expected
  assertOkEqual (parse expr "count ( distinct     col )") expected

[<Fact>]
let ``Parses complex functions`` () =
  let expected =
    Function("length", [ Function("sum", [ Function("+", [ Identifier(None, "rain"); Identifier(None, "sun") ]) ]) ])

  assertOkEqual (parse expr "length(sum(rain + sun))") expected

[<Fact>]
let ``Parses WHERE clause conditions`` () =
  assertOkEqual (parse whereClause "WHERE a = 1") (Equals(Identifier(None, "a"), Integer 1))
  assertOkEqual (parse whereClause "WHERE a = '1'") (Equals(Identifier(None, "a"), String "1"))

[<Fact>]
let ``Parses GROUP BY statement`` () =
  assertOkEqual
    (parse groupBy "GROUP BY a, b, c")
    [ Identifier(None, "a"); Identifier(None, "b"); Identifier(None, "c") ]

  assertOkEqual (parse groupBy "GROUP BY a") [ Identifier(None, "a") ]
  assertError (parse groupBy "GROUP BY")

[<Fact>]
let ``Parses SELECT by itself`` () =
  assertOkEqual
    (parse selectQuery "SELECT col FROM table")
    (SelectQuery { defaultSelect with Expressions = [ As(Identifier(None, "col"), None) ] })

[<Fact>]
let ``Parses SELECT DISTINCT`` () =
  assertOkEqual
    (parse selectQuery "SELECT DISTINCT col FROM table")
    (SelectQuery
      { defaultSelect with
          SelectDistinct = true
          Expressions = [ As(Identifier(None, "col"), None) ]
      })

[<Fact>]
let ``Fails on unexpected input`` () = assertError (Parser.parse "Foo")

[<Fact>]
let ``Parse SELECT query with columns and table`` () =
  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table")
    { defaultSelect with
        Expressions = [ As(Identifier(None, "col1"), None); As(Identifier(None, "col2"), None) ]
    }

  assertOkEqual
    (Parser.parse "SELECT col1, col2 FROM table ;")
    { defaultSelect with
        Expressions = [ As(Identifier(None, "col1"), None); As(Identifier(None, "col2"), None) ]
    }

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
    { defaultSelect with Expressions = [ As(Identifier(None, "col1"), None) ] }

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
        Expressions =
          [
            As(Identifier(None, "col1"), None)
            As(Function("count", [ Distinct(Identifier(None, "aid")) ]), None)
          ]
        GroupBy = [ Identifier(None, "col1") ]
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
          [
            As(Identifier(None, "col1"), Some "colAlias")
            As(Function("count", [ Distinct(Identifier(None, "aid")) ]), None)
          ]
        Where =
          Some(
            And(
              Equals(Identifier(None, "col1"), Integer 1),
              (Or(Equals(Identifier(None, "col2"), Integer 2), Equals(Identifier(None, "col2"), Integer 3)))
            )
          )
        GroupBy = [ Identifier(None, "col1") ]
        Having = Some <| Gt(Function("count", [ Distinct(Identifier(None, "aid")) ]), Integer 1)
    }

[<Fact>]
let ``Parses simple JOINs`` () =
  assertOkEqual (parse from "from t1, t2") (Join(InnerJoin, Table("t1", None), Table("t2", None), Boolean true))

  assertOkEqual
    (parse from "from t1 inner join t2 on true")
    (Join(InnerJoin, Table("t1", None), Table("t2", None), Boolean true))

  assertOkEqual
    (parse from "from t1 left join t2 on true")
    (Join(LeftJoin, Table("t1", None), Table("t2", None), Boolean true))

  assertOkEqual
    (parse from "from t1 right join t2 on true")
    (Join(RightJoin, Table("t1", None), Table("t2", None), Boolean true))

  assertOkEqual
    (parse from "from t1 full join t2 on true")
    (Join(FullJoin, Table("t1", None), Table("t2", None), Boolean true))

  assertOkEqual
    (parse from "from t1 JOIN t2 ON t1.a = t2.b")
    (Join(
      InnerJoin,
      Table("t1", None),
      Table("t2", None),
      Equals(Identifier(Some "t1", "a"), Identifier(Some "t2", "b"))
    ))

[<Fact>]
let ``Parses multiple JOINs`` () =
  assertOkEqual
    (parse from "from t1, t2, t3")
    (Join(
      InnerJoin,
      Join(InnerJoin, Table("t1", None), Table("t2", None), Boolean true),
      Table("t3", None),
      Boolean true
    ))

  assertOkEqual
    (parse from "from t1 join t2 on true join t3 on true")
    (Join(
      InnerJoin,
      Join(InnerJoin, Table("t1", None), Table("t2", None), Boolean true),
      Table("t3", None),
      Boolean true
    ))

  assertOkEqual
    (parse from "from t1 left join t2 on a right join t3 on b")
    (Join(
      RightJoin,
      Join(LeftJoin, Table("t1", None), Table("t2", None), Identifier(None, "a")),
      Table("t3", None),
      Identifier(None, "b")
    ))

[<Fact>]
let ``Rejects invalid JOINs`` () =
  assertError (parse from "from t1 join t2")
  assertError (parse from "from t1 join t2 where a = b")

[<Fact>]
let ``Failed Paul attack query 1`` () =
  assertOkEqual
    (Parser.parse
      """
        select count(distinct aid1)
        from tab where t1='y' and t2 = 'm'
         """)
    { defaultSelect with
        Expressions = [ As(Function("count", [ Distinct(Identifier(None, "aid1")) ]), None) ]
        From = Table("tab", None)
        Where = Some(And(Equals(Identifier(None, "t1"), String "y"), Equals(Identifier(None, "t2"), String "m")))
    }

[<Fact>]
let ``Failed Paul attack query 2`` () =
  assertOkEqual
    (Parser.parse
      """
        select count(distinct aid1)
        from tab where t1 = 'y' and not (i1 = 100 and t2 = 'x')
         """)
    { defaultSelect with
        Expressions = [ As(Function("count", [ Distinct(Identifier(None, "aid1")) ]), None) ]
        From = Table("tab", None)
        Where =
          Some(
            And(
              Equals(Identifier(None, "t1"), String "y"),
              Not(And(Equals(Identifier(None, "i1"), Integer 100), Equals(Identifier(None, "t2"), String "x")))
            )
          )
    }
