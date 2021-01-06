module OpenDiffix.Core.ExpressionTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let ctx = EmptyContext

module DefaultFunctionsTests =
  let runs fn expectations =
    expectations
    |> List.iter (fun (a, b, result) -> fn ctx [ a; b ] |> should equal result)

  let runs1 fn expectations =
    expectations
    |> List.iter (fun (a, result) -> fn ctx [ a ] |> should equal result)

  let fails fn cases = cases |> List.iter (fun args -> (fun () -> fn ctx args |> ignore) |> shouldFail)

  [<Fact>]
  let add () =
    runs
      DefaultFunctions.add
      [
        Integer 5, Integer 3, Integer 8
        Float 2.5, Integer 3, Float 5.5
        Integer 4, Float 2.5, Float 6.5
        Integer 3, Null, Null
        Null, Integer 3, Null
      ]

    fails
      DefaultFunctions.add
      [ //
        [ Integer 5; String "a" ]
        [ Boolean true; Integer 1 ]
        [ String "a"; Float 2.5 ]
      ]

  [<Fact>]
  let sub () =
    runs
      DefaultFunctions.sub
      [
        Integer 5, Integer 3, Integer 2
        Float 2.5, Integer 3, Float -0.5
        Integer 3, Float 2.5, Float 0.5
        Integer 3, Null, Null
        Null, Integer 3, Null
      ]

    fails
      DefaultFunctions.sub
      [ //
        [ Integer 5; String "a" ]
        [ Boolean true; Integer 1 ]
        [ String "a"; Float 2.5 ]
      ]

  [<Fact>]
  let equals () =
    runs
      DefaultFunctions.equals
      [
        Integer 3, Integer 3, Boolean true
        Float 3., Integer 3, Boolean true
        Null, Null, Null
        Null, Integer 3, Null
        Integer 3, Null, Null
        String "a", String "a", Boolean true
      ]

  [<Fact>]
  let not () =
    runs1
      DefaultFunctions.not
      [ //
        Boolean true, Boolean false
        Boolean false, Boolean true
        Null, Null
      ]

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

let makeRows (ctor1, ctor2, ctor3) (rows: ('a * 'b * 'c) list): Row list =
  rows |> List.map (fun (a, b, c) -> [| ctor1 a; ctor2 b; ctor3 c |])

let makeRow strValue intValue floatValue = [| String strValue; Integer intValue; Float floatValue |]

let testRow = makeRow "Some text" 7 0.25

let testRows =
  makeRows
    (String, Integer, Float)
    [ //
      "row1", 1, 1.5
      "row1", 2, 2.5
      "row2", 3, 3.5
      "row2", 4, 4.5
    ]

let eval expr = Expression.evaluate ctx testRow expr

let evalAggr expr = Expression.evaluateAggregated ctx Map.empty testRows expr

[<Fact>]
let evaluate () =
  // select val_int + 3
  eval (Function("+", [ ColumnReference 1; Constant(Integer 3) ], Scalar))
  |> should equal (Integer 10)

  // select val_str
  eval (ColumnReference 0) |> should equal (String "Some text")

[<Fact>]
let evaluateAggregated () =
  // select sum(val_float - val_int)
  evalAggr
    (Function("sum", [ Function("-", [ ColumnReference 2; ColumnReference 1 ], Scalar) ], Expression.defaultAggregate))
  |> should equal (Float 2.0)

  // select count(*)
  evalAggr (Function("count", [ Constant(Unit) ], Expression.defaultAggregate))
  |> should equal (Integer 4)

  // select count(1)
  evalAggr (Function("count", [ Constant(Integer 1) ], Expression.defaultAggregate))
  |> should equal (Integer 4)

  // select count(distinct val_str)
  evalAggr (Function("count", [ ColumnReference 0 ], Expression.distinctAggregate))
  |> should equal (Integer 2)

  // select val_str
  (fun () -> evalAggr (ColumnReference 0) |> ignore) |> shouldFail

[<Fact>]
let sortRows () =
  [
    [| String "b"; Integer 1 |]
    [| String "b"; Integer 2 |]
    [| String "b"; Null |]
    [| String "a"; Integer 1 |]
    [| String "a"; Integer 2 |]
    [| String "a"; Null |]
    [| Null; Integer 1 |]
    [| Null; Integer 2 |]
    [| Null; Null |]
  ]
  |> Expression.sortRows
       ctx
       [ //
         ColumnReference 0, Ascending, NullsLast
         ColumnReference 1, Descending, NullsFirst
       ]
  |> List.ofSeq
  |> should
       equal
       [
         [| String "a"; Null |]
         [| String "a"; Integer 2 |]
         [| String "a"; Integer 1 |]
         [| String "b"; Null |]
         [| String "b"; Integer 2 |]
         [| String "b"; Integer 1 |]
         [| Null; Null |]
         [| Null; Integer 2 |]
         [| Null; Integer 1 |]
       ]
