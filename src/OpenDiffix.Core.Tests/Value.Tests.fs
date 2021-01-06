module OpenDiffix.Core.ValueTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

module ComparerTests =
  open System.Linq

  let sort direction nulls (values: seq<Value>) =
    values.OrderBy((fun x -> x), Value.comparer direction nulls) |> List.ofSeq

  [<Fact>]
  let ``Sorts in ascending direction`` () =
    [ Integer 5; Integer 9; Integer 3; Integer -4; Integer 12 ]
    |> sort Ascending NullsFirst
    |> should equal [ Integer -4; Integer 3; Integer 5; Integer 9; Integer 12 ]

  [<Fact>]
  let ``Sorts in descending direction`` () =
    [ Integer 5; Integer 9; Integer 3; Integer -4; Integer 12 ]
    |> sort Descending NullsFirst
    |> should equal [ Integer 12; Integer 9; Integer 5; Integer 3; Integer -4 ]

  [<Fact>]
  let ``Compares floats and integers`` () =
    [ Integer 4; Float 3.5; Integer 3; Float 3.0; Integer 2 ]
    |> sort Ascending NullsFirst
    |> should equal [ Integer 2; Integer 3; Float 3.0; Float 3.5; Integer 4 ]

  [<Fact>]
  let ``Nulls first in ascending order`` () =
    [ Integer 10; Null; Integer 1; Null; Integer 5 ]
    |> sort Ascending NullsFirst
    |> should equal [ Null; Null; Integer 1; Integer 5; Integer 10 ]

  [<Fact>]
  let ``Nulls last in ascending order`` () =
    [ Integer 10; Null; Integer 1; Null; Integer 5 ]
    |> sort Ascending NullsLast
    |> should equal [ Integer 1; Integer 5; Integer 10; Null; Null ]

  [<Fact>]
  let ``Nulls first in descending order`` () =
    [ Integer 10; Null; Integer 1; Null; Integer 5 ]
    |> sort Descending NullsFirst
    |> should equal [ Null; Null; Integer 10; Integer 5; Integer 1 ]

  [<Fact>]
  let ``Nulls last in descending order`` () =
    [ Integer 10; Null; Integer 1; Null; Integer 5 ]
    |> sort Descending NullsLast
    |> should equal [ Integer 10; Integer 5; Integer 1; Null; Null ]
