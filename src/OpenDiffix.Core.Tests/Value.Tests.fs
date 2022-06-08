module OpenDiffix.Core.ValueTests

open Xunit
open FsUnit.Xunit

open CommonTypes

module ComparerTests =
  let sort direction nulls (values: seq<Value>) =
    values |> Seq.sortWith (Value.comparer direction nulls) |> Seq.toList

  [<Fact>]
  let ``Sorts in ascending direction`` () =
    [ Integer 5L; Integer 9L; Integer 3L; Integer -4L; Integer 12L ]
    |> sort Ascending NullsFirst
    |> should equal [ Integer -4L; Integer 3L; Integer 5L; Integer 9L; Integer 12L ]

  [<Fact>]
  let ``Sorts in descending direction`` () =
    [ Integer 5L; Integer 9L; Integer 3L; Integer -4L; Integer 12L ]
    |> sort Descending NullsFirst
    |> should equal [ Integer 12L; Integer 9L; Integer 5L; Integer 3L; Integer -4L ]

  [<Fact>]
  let ``Nulls first in ascending order`` () =
    [ Integer 10L; Null; Integer 1L; Null; Integer 5L ]
    |> sort Ascending NullsFirst
    |> should equal [ Null; Null; Integer 1L; Integer 5L; Integer 10L ]

  [<Fact>]
  let ``Nulls last in ascending order`` () =
    [ Integer 10L; Null; Integer 1L; Null; Integer 5L ]
    |> sort Ascending NullsLast
    |> should equal [ Integer 1L; Integer 5L; Integer 10L; Null; Null ]

  [<Fact>]
  let ``Nulls first in descending order`` () =
    [ Integer 10L; Null; Integer 1L; Null; Integer 5L ]
    |> sort Descending NullsFirst
    |> should equal [ Null; Null; Integer 10L; Integer 5L; Integer 1L ]

  [<Fact>]
  let ``Nulls last in descending order`` () =
    [ Integer 10L; Null; Integer 1L; Null; Integer 5L ]
    |> sort Descending NullsLast
    |> should equal [ Integer 10L; Integer 5L; Integer 1L; Null; Null ]

  [<Fact>]
  let ``String sort groups by letter case`` () =
    [ String "a"; String "C"; String "A"; String "c" ]
    |> sort Ascending NullsLast
    |> should equal [ String "a"; String "A"; String "c"; String "C" ]

  [<Fact>]
  let ``String sort ignores whitespace and symbols`` () =
    [ String " a"; String "  C"; String "+  A"; String "   c" ]
    |> sort Ascending NullsLast
    |> should equal [ String " a"; String "+  A"; String "   c"; String "  C" ]

  [<Fact>]
  let ``String sort uses whitespace and symbols comparison on conflict`` () =
    [ String "+a"; String "a"; String " a"; String "b" ]
    |> sort Ascending NullsLast
    |> should equal [ String " a"; String "+a"; String "a"; String "b" ]

  [<Fact>]
  let ``Money rounding`` () =
    Value.moneyRound 5.0 |> should (equalWithin 1e-10) 5.0
    Value.moneyRound 5.1 |> should (equalWithin 1e-10) 5.0
    Value.moneyRound 4.9 |> should (equalWithin 1e-10) 5.0
    Value.moneyRound 2.9 |> should (equalWithin 1e-10) 2.0
    Value.moneyRound 0.00009 |> should (equalWithin 1e-10) 0.0001
    Value.moneyRound 0.02009 |> should (equalWithin 1e-10) 0.02
    Value.moneyRound 0.00509 |> should (equalWithin 1e-10) 0.005
    Value.moneyRound 0.00019 |> should (equalWithin 1e-10) 0.0002
    Value.moneyRound 0.000000000000009 |> should (equalWithin 1e-10) 0.
    Value.moneyRound 100000.0000000009 |> should (equalWithin 1e-10) 100000.

  [<Fact>]
  let ``Money rounded checking`` () =
    Value.isMoneyRounded (Real 5.0) |> should equal true
    Value.isMoneyRounded (Real 5.1) |> should equal false
    Value.isMoneyRounded (Real 0.00009) |> should equal false
    Value.isMoneyRounded (Real 0.000000000000009) |> should equal true
    Value.isMoneyRounded (Real 100000.0000000009) |> should equal false
    Value.isMoneyRounded (Real 100000.000000000000009) |> should equal true
