module OpenDiffix.Parser.ParserTests

open OpenDiffix.Core
open Xunit
open FsUnit.Xunit

open ParserTypes
open Parser
open Parser.QueryParser

let defaultSelect =
  {
    SelectDistinct = false
    Expressions = []
    From = Table("table", None)
    Where = None
    GroupBy = []
    Having = None
    Limit = None
    OrderBy = []
  }

let expectFail query =
  try
    let result = parse query
    failwith $"Expected query to fail parsing. Got successfully parsed: %A{result}"
  with
  | _e -> ()

let expectFailWithParser parser fragment =
  try
    let result = FParsec.CharParsers.run parser fragment
    failwith $"Expected query fragment parser to fail. Got successfully parsed: %A{result}"
  with
  | _e -> ()

let parseFragment parser fragment =
  match FParsec.CharParsers.run parser fragment with
  | FParsec.CharParsers.Success (result, _, _) -> result
  | _ -> failwith "Expected a successfully parsed query fragment"

[<Fact>]
let ``Parses simple identifiers`` () =
  parseFragment expr "hello" |> should equal (Identifier(None, "hello"))
  parseFragment expr "hello12" |> should equal (Identifier(None, "hello12"))
  parseFragment expr "hello_12" |> should equal (Identifier(None, "hello_12")) // Allows underscores

  parseFragment expr "hello.bar12"
  |> should equal (Identifier(Some "hello", "bar12")) // Allows qualified names

[<Fact>]
let ``Parses expressions`` () =
  [
    "1", Integer 1L
    "1.1", Float 1.1
    "1.01", Float 1.01
    "1.001", Float 1.001
    "1.0", Float 1.0
    "'hello'", String "hello"
    "true", Boolean true
    "false", Boolean false
  ]
  |> List.iter (fun (value, expected) -> parseFragment expr value |> should equal expected)

  [ "+"; "-"; "*"; "/"; "%" ]
  |> List.iter (fun op ->
    parseFragment expr $"1 %s{op} 1"
    |> should equal (Function(op, [ Integer 1L; Integer 1L ]))
  )

  [ "and", And; "or", Or; "<", Lt; "<=", LtE; ">", Gt; ">=", GtE; "=", Equals; "<>", Not << Equals ]
  |> List.iter (fun (op, expected) ->
    parseFragment expr $"1 %s{op} 1"
    |> should equal (expected (Integer 1L, Integer 1L))
  )

  parseFragment expr "not 1" |> should equal (Not(Integer 1L))

  parseFragment expr "value is null"
  |> should equal (IsNull(Identifier(None, "value")))

  parseFragment expr "value is not null"
  |> should equal (Not(IsNull(Identifier(None, "value"))))

  parseFragment expr $"'ab' || 'bc'"
  |> should equal (Function("||", [ String "ab"; String "bc" ]))

[<Fact>]
let ``Parses columns`` () =
  parseFragment commaSepExpressions "hello"
  |> should equal [ As(Identifier(None, "hello"), None) ]

  parseFragment commaSepExpressions "hello as alias"
  |> should equal [ As(Identifier(None, "hello"), Some "alias") ]

  parseFragment commaSepExpressions "hello, world"
  |> should equal [ As(Identifier(None, "hello"), None); As(Identifier(None, "world"), None) ]

  parseFragment commaSepExpressions "hello,world"
  |> should equal [ As(Identifier(None, "hello"), None); As(Identifier(None, "world"), None) ]

  parseFragment commaSepExpressions "hello ,world"
  |> should equal [ As(Identifier(None, "hello"), None); As(Identifier(None, "world"), None) ]

[<Fact>]
let ``Parses functions`` () =
  parseFragment expr "hello(world)"
  |> should equal (Function("hello", [ Identifier(None, "world") ]))

  parseFragment expr "hello ( world )"
  |> should equal (Function("hello", [ Identifier(None, "world") ]))

  parseFragment commaSepExpressions "hello(world), hello(moon)"
  |> should
       equal
       [
         As(Function("hello", [ Identifier(None, "world") ]), None)
         As(Function("hello", [ Identifier(None, "moon") ]), None)
       ]

  parseFragment expr "hello('world', 1, 2.5)"
  |> should equal (Function("hello", [ String("world"); Integer(1L); Float(2.5) ]))

[<Fact>]
let ``Parses casts`` () =
  parseFragment expr "cast(1 as boolean)"
  |> should equal (Function("cast", [ Integer 1L; String "boolean" ]))

  parseFragment expr "cast('0' as real)"
  |> should equal (Function("cast", [ String "0"; String "real" ]))

[<Fact>]
let ``Precedence is as expected`` () =
  parseFragment expr "1 + 2 * 3 % 2 < 1 AND a or not b IS NULL"
  |> should
       equal
       (And(
         Lt(
           Function("+", [ Integer 1L; Function("*", [ Integer 2L; Function("%", [ Integer 3L; Integer 2L ]) ]) ]),
           Integer 1L
         ),
         Or(Identifier(None, "a"), Not(IsNull(Identifier(None, "b"))))
       ))

[<Fact>]
let ``Parses count(*)`` () =
  let expected = Function("count", [ Star ])
  parseFragment expr "count(*)" |> should equal expected
  parseFragment expr "count( *     )" |> should equal expected

[<Fact>]
let ``Parses count(distinct col)`` () =
  let expected = Function("count", [ Distinct(Identifier(None, "col")) ])
  parseFragment expr "count(distinct col)" |> should equal expected
  parseFragment expr "count ( distinct     col )" |> should equal expected

[<Fact>]
let ``Parses complex functions`` () =
  parseFragment expr "length(sum(rain + sun))"
  |> should
       equal
       (Function(
         "length",
         [ Function("sum", [ Function("+", [ Identifier(None, "rain"); Identifier(None, "sun") ]) ]) ]
       ))

[<Fact>]
let ``Parses WHERE clause conditions`` () =
  parseFragment whereClause "WHERE a = 1"
  |> should equal (Equals(Identifier(None, "a"), Integer 1L))

  parseFragment whereClause "WHERE a = '1'"
  |> should equal (Equals(Identifier(None, "a"), String "1"))

[<Fact>]
let ``Parses GROUP BY statement`` () =
  parseFragment groupBy "GROUP BY a, b, c"
  |> should equal [ Identifier(None, "a"); Identifier(None, "b"); Identifier(None, "c") ]

  parseFragment groupBy "GROUP BY a" |> should equal [ Identifier(None, "a") ]

  expectFailWithParser groupBy "GROUP BY"

[<Fact>]
let ``Parses ORDER BY statement`` () =
  parseFragment orderBy "ORDER BY a, b, c"
  |> should equal [ Identifier(None, "a"); Identifier(None, "b"); Identifier(None, "c") ]

  parseFragment orderBy "ORDER BY a" |> should equal [ Identifier(None, "a") ]

  expectFailWithParser orderBy "ORDER BY"

[<Fact>]
let ``Parses SELECT by itself`` () =
  parseFragment selectQuery "SELECT col FROM table"
  |> should equal (SelectQuery { defaultSelect with Expressions = [ As(Identifier(None, "col"), None) ] })

[<Fact>]
let ``Parses SELECT DISTINCT`` () =
  parseFragment selectQuery "SELECT DISTINCT col FROM table"
  |> should
       equal
       (SelectQuery
         { defaultSelect with
             SelectDistinct = true
             Expressions = [ As(Identifier(None, "col"), None) ]
         })

[<Fact>]
let ``Fails on unexpected input`` () = expectFail "Foo"

[<Fact>]
let ``Parse SELECT query with columns and table`` () =
  parse "SELECT col1, col2 FROM table"
  |> should
       equal
       { defaultSelect with
           Expressions = [ As(Identifier(None, "col1"), None); As(Identifier(None, "col2"), None) ]
       }

  parse "SELECT col1, col2 FROM table ;"
  |> should
       equal
       { defaultSelect with
           Expressions = [ As(Identifier(None, "col1"), None); As(Identifier(None, "col2"), None) ]
       }

[<Fact>]
let ``Multiline select`` () =
  parse
    """
       SELECT
         col1
       FROM
         table
       """
  |> should equal { defaultSelect with Expressions = [ As(Identifier(None, "col1"), None) ] }

[<Fact>]
let ``Parse aggregate query`` () =
  parse
    """
       SELECT col1, count(distinct aid)
       FROM table
       GROUP BY col1
       """
  |> should
       equal
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
  parse
    """
       SELECT col1 as colAlias, count(distinct aid)
       FROM table
       WHERE col1 = 1 AND col2 = 2 or col2 = 3
       GROUP BY col1
       HAVING count(distinct aid) > 1
       ORDER BY col2
       """
  |> should
       equal
       { defaultSelect with
           Expressions =
             [
               As(Identifier(None, "col1"), Some "colAlias")
               As(Function("count", [ Distinct(Identifier(None, "aid")) ]), None)
             ]
           Where =
             Some(
               And(
                 Equals(Identifier(None, "col1"), Integer 1L),
                 (Or(Equals(Identifier(None, "col2"), Integer 2L), Equals(Identifier(None, "col2"), Integer 3L)))
               )
             )
           GroupBy = [ Identifier(None, "col1") ]
           Having = Some <| Gt(Function("count", [ Distinct(Identifier(None, "aid")) ]), Integer 1L)
           OrderBy = [ Identifier(None, "col2") ]
       }

[<Fact>]
let ``Parses simple JOINs`` () =
  parseFragment from "from t1, t2"
  |> should equal (Join(InnerJoin, Table("t1", None), Table("t2", None), Boolean true))

  parseFragment from "from t1 inner join t2 on true"
  |> should equal (Join(InnerJoin, Table("t1", None), Table("t2", None), Boolean true))

  parseFragment from "from t1 left join t2 on true"
  |> should equal (Join(LeftJoin, Table("t1", None), Table("t2", None), Boolean true))

  parseFragment from "from t1 right join t2 on true"
  |> should equal (Join(RightJoin, Table("t1", None), Table("t2", None), Boolean true))

  parseFragment from "from t1 full join t2 on true"
  |> should equal (Join(FullJoin, Table("t1", None), Table("t2", None), Boolean true))

  parseFragment from "from t1 JOIN t2 ON t1.a = t2.b"
  |> should
       equal
       (Join(
         InnerJoin,
         Table("t1", None),
         Table("t2", None),
         Equals(Identifier(Some "t1", "a"), Identifier(Some "t2", "b"))
       ))

[<Fact>]
let ``Parses multiple JOINs`` () =
  parseFragment from "from t1, t2, t3"
  |> should
       equal
       (Join(
         InnerJoin,
         Join(InnerJoin, Table("t1", None), Table("t2", None), Boolean true),
         Table("t3", None),
         Boolean true
       ))

  parseFragment from "from t1 join t2 on true join t3 on true"
  |> should
       equal
       (Join(
         InnerJoin,
         Join(InnerJoin, Table("t1", None), Table("t2", None), Boolean true),
         Table("t3", None),
         Boolean true
       ))

  parseFragment from "from t1 left join t2 on a right join t3 on b"
  |> should
       equal
       (Join(
         RightJoin,
         Join(LeftJoin, Table("t1", None), Table("t2", None), Identifier(None, "a")),
         Table("t3", None),
         Identifier(None, "b")
       ))

[<Fact>]
let ``Rejects invalid JOINs`` () =
  expectFailWithParser from "from t1 join t2"
  expectFailWithParser from "from t1 join t2 where a = b"

[<Fact>]
let ``Failed Paul attack query 1`` () =
  parse
    """
      select count(distinct aid1)
      from tab where t1='y' and t2 = 'm'
       """
  |> should
       equal
       { defaultSelect with
           Expressions = [ As(Function("count", [ Distinct(Identifier(None, "aid1")) ]), None) ]
           From = Table("tab", None)
           Where = Some(And(Equals(Identifier(None, "t1"), String "y"), Equals(Identifier(None, "t2"), String "m")))
       }

[<Fact>]
let ``Failed Paul attack query 2`` () =
  parse
    """
      select count(distinct aid1)
      from tab where t1 = 'y' and not (i1 = 100 and t2 = 'x')
       """
  |> should
       equal
       { defaultSelect with
           Expressions = [ As(Function("count", [ Distinct(Identifier(None, "aid1")) ]), None) ]
           From = Table("tab", None)
           Where =
             Some(
               And(
                 Equals(Identifier(None, "t1"), String "y"),
                 Not(And(Equals(Identifier(None, "i1"), Integer 100L), Equals(Identifier(None, "t2"), String "x")))
               )
             )
       }

[<Fact>]
let ``Parse sub-query`` () =
  let query = { defaultSelect with Expressions = [ As(Identifier(None, "col"), None) ] }

  parseFragment from "FROM (SELECT col FROM table) t"
  |> should equal (SubQuery(query, "t"))

  parseFragment from "FROM (SELECT col FROM table) t INNER JOIN (SELECT col FROM table) s ON t.col = s.col"
  |> should
       equal
       (Join(
         InnerJoin,
         SubQuery(query, "t"),
         SubQuery(query, "s"),
         Equals(Identifier(Some "t", "col"), Identifier(Some "s", "col"))
       ))

[<Fact>]
let ``Parses star select`` () =
  parseFragment selectQuery "SELECT * FROM table"
  |> should equal (SelectQuery { defaultSelect with Expressions = [ Star ] })

[<Fact>]
let ``Parses limit`` () =
  parseFragment selectQuery "SELECT * FROM table LIMIT 10"
  |> should equal (SelectQuery { defaultSelect with Expressions = [ Star ]; Limit = Some(10u) })

[<Fact>]
let ``Parses quoted identifier 1`` () =
  parseFragment selectQuery "SELECT \"*-\\(\" FROM table"
  |> should equal (SelectQuery { defaultSelect with Expressions = [ As(Identifier(None, "*-\\("), None) ] })

[<Fact>]
let ``Parses quoted identifier 2`` () =
  parseFragment selectQuery "SELECT \"(a)\".\"(b)\" FROM table"
  |> should equal (SelectQuery { defaultSelect with Expressions = [ As(Identifier(Some "(a)", "(b)"), None) ] })

[<Fact>]
let ``Parses quoted identifier 3`` () =
  parseFragment selectQuery "SELECT * FROM \"table\""
  |> should equal (SelectQuery { defaultSelect with Expressions = [ Star ] })

[<Fact>]
let ``Parses quoted identifier 4`` () =
  parseFragment selectQuery "SELECT a FROM table GROUP BY \"a\" LIMIT 1"
  |> should
       equal
       (SelectQuery
         { defaultSelect with
             Expressions = [ As(Identifier(None, "a"), None) ]
             GroupBy = [ Identifier(None, "a") ]
             Limit = Some 1u
         })

[<Fact>]
let ``Parses quoted alias`` () =
  parseFragment selectQuery "SELECT a AS \"a b\" FROM table AS \"a b\""
  |> should
       equal
       (SelectQuery
         { defaultSelect with
             Expressions = [ As(Identifier(None, "a"), Some "a b") ]
             From = Table("table", Some "a b")
         })
