module OpenDiffix.Core.ExpressionTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let ctx = EmptyContext

module DefaultFunctionsTests =
  [<Fact>]
  let add () =
    DefaultFunctions.add ctx [ Integer 5; Integer 3 ]
    |> should equal (Integer 8)

    (fun () -> DefaultFunctions.add ctx [ Integer 5; String "a" ] |> ignore)
    |> shouldFail

  [<Fact>]
  let sub () =
    DefaultFunctions.sub ctx [ Integer 5; Integer 3 ]
    |> should equal (Integer 2)

    (fun () -> DefaultFunctions.sub ctx [ Integer 5; String "a" ] |> ignore)
    |> shouldFail

module DefaultAggregatorsTests =
  [<Fact>]
  let sum () =
    DefaultAggregates.sum ctx [ [ Integer 5 ]; [ Integer 3 ]; [ Integer -2 ] ]
    |> should equal (Integer 6)

    DefaultAggregates.sum ctx [] |> should equal Null

  [<Fact>]
  let count () =
    DefaultAggregates.count ctx [ [ Integer 7 ]; [ Integer 15 ]; [ Integer -3 ] ]
    |> should equal (Integer 3)

    DefaultAggregates.count ctx [] |> should equal (Integer 0)

    DefaultAggregates.count ctx [ [ String "str1" ]; [ String "" ]; [ Null ] ]
    |> should equal (Integer 2)

module ExpressionTests =
  let makeRow strValue intValue floatValue =
    [| String strValue; Integer intValue; Float floatValue |]

  let testRow = makeRow "Some text" 7 0.25

  let testRows =
    [
      makeRow "row1" 1 1.5
      makeRow "row1" 2 2.5
      makeRow "row2" 3 3.5
      makeRow "row2" 4 4.5
    ]

  let distinctAggregate =
    Aggregate
      {
        Distinct = true
        OrderBy = []
        OrderByDirection = Ascending
      }

  let eval expr = Expression.evaluate ctx expr testRow

  let evalAggr expr =
    Expression.evaluateAggregated ctx expr Map.empty testRows

  [<Fact>]
  let evaluate () =
    // select val_int + 3
    eval (Function("+", [ ColumnReference 1; Constant(Integer 3) ], Scalar))
    |> should equal (Integer 10)

    // select val_str
    eval (ColumnReference 0)
    |> should equal (String "Some text")

  [<Fact>]
  let evaluateAggregated () =
    // select sum(val_float - val_int)
    evalAggr
      (Function
        ("sum",
         [
           Function("-", [ ColumnReference 2; ColumnReference 1 ], Scalar)
         ],
         Expression.defaultAggregate))
    |> should equal (Float 2.0)

    // select count(1)
    evalAggr (Function("count", [ Constant(Integer 1) ], Expression.defaultAggregate))
    |> should equal (Integer 4)

    // select count(distinct val_str)
    evalAggr (Function("count", [ ColumnReference 0 ], distinctAggregate))
    |> should equal (Integer 2)

    // select val_str
    (fun () -> evalAggr (ColumnReference 0) |> ignore)
    |> shouldFail
