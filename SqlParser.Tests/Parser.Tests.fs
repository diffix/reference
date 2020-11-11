module Tests

open SqlParser.Tests
open SqlParser.Parser
open SqlParser.Query
open Xunit
open TestHelpers

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
    assertOkEqual (parseSql "   show
                   tables   ") ShowTables 