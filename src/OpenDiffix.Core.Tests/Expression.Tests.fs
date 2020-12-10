module OpenDiffix.Core.ExpressionTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let ctx = EmptyContext

module DefaultFunctionsTests =
  [<Fact>]
  let add () =
    DefaultFunctions.add ctx [ IntegerValue 5; IntegerValue 3 ]
    |> should equal (IntegerValue 8)

    (fun () ->
      DefaultFunctions.add ctx [ IntegerValue 5; StringValue "a" ]
      |> ignore)
    |> shouldFail

  [<Fact>]
  let sub () =
    DefaultFunctions.sub ctx [ IntegerValue 5; IntegerValue 3 ]
    |> should equal (IntegerValue 2)

    (fun () ->
      DefaultFunctions.sub ctx [ IntegerValue 5; StringValue "a" ]
      |> ignore)
    |> shouldFail

module DefaultAggregatorsTests =
  [<Fact>]
  let sum () =
    DefaultAggregators.sum ctx [ IntegerValue 5; IntegerValue 3; IntegerValue -2 ]
    |> should equal (IntegerValue 6)

    DefaultAggregators.sum ctx [] |> should equal NullValue

  [<Fact>]
  let count () =
    DefaultAggregators.count ctx [ IntegerValue 7; IntegerValue 15; IntegerValue -3 ]
    |> should equal (IntegerValue 3)

    DefaultAggregators.count ctx []
    |> should equal (IntegerValue 0)

    DefaultAggregators.count ctx [ StringValue "str1"; StringValue ""; NullValue ]
    |> should equal (IntegerValue 2)

module ExpressionTests =
  let row strValue intValue floatValue =
    Expression.makeTuple [
      "val_str", StringValue strValue
      "val_int", IntegerValue intValue
      "val_float", FloatValue floatValue
    ]

  let tuple = row "Some text" 7 0.25

  let tuples =
    [ row "row1" 1 1.5
      row "row2" 2 2.5
      row "row3" 3 3.5
      row "row4" 4 4.5 ]

  let eval expr = Expression.evaluate ctx expr tuple

  let evalAggr expr =
    Expression.evaluateAggregated ctx expr Map.empty tuples

  [<Fact>]
  let evaluate () =
    // select val_int + 3
    eval (FunctionCall("+", [ ColumnReference "val_int"; Constant(IntegerValue 3) ]))
    |> should equal (IntegerValue 10)

    // select val_str
    eval (ColumnReference "val_str")
    |> should equal (StringValue "Some text")

  [<Fact>]
  let evaluateAggregated () =
    // select sum(val_float - val_int)
    evalAggr (FunctionCall("sum", [ FunctionCall("-", [ ColumnReference "val_float"; ColumnReference "val_int" ]) ]))
    |> should equal (FloatValue 2.0)

    // select count(1)
    evalAggr (FunctionCall("count", [ Constant(IntegerValue 1) ]))
    |> should equal (IntegerValue 4)

    // select val_str
    (fun () -> evalAggr (ColumnReference "val_str") |> ignore)
    |> shouldFail
