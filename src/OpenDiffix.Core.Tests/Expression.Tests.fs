module OpenDiffix.Core.ExpressionTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core

let ctx = EmptyContext

module DefaultFunctionsTests =
  let runs fn expectations =
    expectations
    |> List.iter (fun (a, b, result) -> fn ctx [ a; b ] |> should equal result)

  let runsWithArg fn arg expectations =
    expectations
    |> List.iter (fun (a, result) -> fn ctx arg [ a ] |> should equal result)

  let runs1 fn expectations =
    expectations
    |> List.iter (fun (a, result) -> fn ctx [ a ] |> should equal result)

  let fails fn cases = cases |> List.iter (fun args -> (fun () -> fn ctx args |> ignore) |> shouldFail)

  [<Fact>]
  let add () =
    runs
      DefaultFunctions.add
      [
        Integer 5L, Integer 3L, Integer 8L
        Real 2.5, Integer 3L, Real 5.5
        Integer 4L, Real 2.5, Real 6.5
        Integer 3L, Null, Null
        Null, Integer 3L, Null
      ]

    fails
      DefaultFunctions.add
      [ //
        [ Integer 5L; String "a" ]
        [ Boolean true; Integer 1L ]
        [ String "a"; Real 2.5 ]
      ]

  [<Fact>]
  let sub () =
    runs
      DefaultFunctions.sub
      [
        Integer 5L, Integer 3L, Integer 2L
        Real 2.5, Integer 3L, Real -0.5
        Integer 3L, Real 2.5, Real 0.5
        Integer 3L, Null, Null
        Null, Integer 3L, Null
      ]

    fails
      DefaultFunctions.sub
      [ //
        [ Integer 5L; String "a" ]
        [ Boolean true; Integer 1L ]
        [ String "a"; Real 2.5 ]
      ]

  [<Fact>]
  let equals () =
    runs
      DefaultFunctions.equals
      [
        Integer 3L, Integer 3L, Boolean true
        Real 3., Integer 3L, Boolean true
        Null, Null, Null
        Null, Integer 3L, Null
        Integer 3L, Null, Null
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

  [<Fact>]
  let binaryChecks () =
    runs
      (DefaultFunctions.binaryBooleanCheck (&&))
      [ //
        Boolean true, Boolean true, Boolean true
        Boolean true, Boolean false, Boolean false
        Boolean false, Boolean false, Boolean false
        Integer 0L, Boolean true, Boolean false
        Real 0., Boolean true, Boolean false
        String "", Boolean true, Boolean false
        String "bar", Boolean true, Boolean false
        String "true", Boolean true, Boolean true
        Null, Boolean true, Boolean false
      ]

module DefaultAggregatorsTests =
  [<Fact>]
  let sum () =
    DefaultAggregates.sum ctx [ [ Integer 5L ]; [ Integer 3L ]; [ Integer -2L ] ]
    |> should equal (Integer 6L)

    DefaultAggregates.sum ctx [] |> should equal Null

  [<Fact>]
  let count () =
    DefaultAggregates.count ctx [ [ Integer 7L ]; [ Integer 15L ]; [ Integer -3L ] ]
    |> should equal (Integer 3L)

    DefaultAggregates.count ctx [] |> should equal (Integer 0L)

    DefaultAggregates.count ctx [ [ String "str1" ]; [ String "" ]; [ Null ] ]
    |> should equal (Integer 2L)

let makeRows (ctor1, ctor2, ctor3) (rows: ('a * 'b * 'c) list): Row list =
  rows |> List.map (fun (a, b, c) -> [| ctor1 a; ctor2 b; ctor3 c |])

let makeRow strValue intValue floatValue = [| String strValue; Integer intValue; Real floatValue |]

let testRow = makeRow "Some text" 7L 0.25

let testRows =
  makeRows
    (String, Integer, Real)
    [ //
      "row1", 1L, 1.5
      "row1", 2L, 2.5
      "row2", 3L, 3.5
      "row2", 4L, 4.5
    ]

let colRef0 = ColumnReference(0, StringType)
let colRef1 = ColumnReference(1, IntegerType)
let colRef2 = ColumnReference(2, RealType)

let eval expr = Expression.evaluate ctx testRow expr

let evalAggr expr = Expression.evaluateAggregated ctx Map.empty testRows expr


[<Fact>]
let evaluate () =
  // select val_int + 3
  eval (FunctionExpr(ScalarFunction Plus, [ colRef1; Constant(Integer 3L) ]))
  |> should equal (Integer 10L)

  // select val_str
  eval colRef0 |> should equal (String "Some text")

[<Fact>]
let evaluateAggregated () =
  // select sum(val_float - val_int)
  evalAggr (
    FunctionExpr(
      AggregateFunction(Sum, AggregateOptions.Default),
      [ FunctionExpr(ScalarFunction Minus, [ colRef2; colRef1 ]) ]
    )
  )
  |> should equal (Real 2.0)

  // select count(*)
  evalAggr (FunctionExpr(AggregateFunction(Count, AggregateOptions.Default), []))
  |> should equal (Integer 4L)

  // select count(1)
  evalAggr (FunctionExpr(AggregateFunction(Count, AggregateOptions.Default), [ Constant(Integer 1L) ]))
  |> should equal (Integer 4L)

  // select count(distinct val_str)
  evalAggr (FunctionExpr(AggregateFunction(Count, { AggregateOptions.Default with Distinct = true }), [ colRef0 ]))
  |> should equal (Integer 2L)

  // select val_str
  (fun () -> evalAggr colRef0 |> ignore) |> shouldFail

[<Fact>]
let sortRows () =
  [
    [| String "b"; Integer 1L |]
    [| String "b"; Integer 2L |]
    [| String "b"; Null |]
    [| String "a"; Integer 1L |]
    [| String "a"; Integer 2L |]
    [| String "a"; Null |]
    [| Null; Integer 1L |]
    [| Null; Integer 2L |]
    [| Null; Null |]
  ]
  |> Expression.sortRows
       ctx
       [ //
         ColumnReference(0, StringType), Ascending, NullsLast
         ColumnReference(1, IntegerType), Descending, NullsFirst
       ]
  |> List.ofSeq
  |> should
       equal
       [
         [| String "a"; Null |]
         [| String "a"; Integer 2L |]
         [| String "a"; Integer 1L |]
         [| String "b"; Null |]
         [| String "b"; Integer 2L |]
         [| String "b"; Integer 1L |]
         [| Null; Null |]
         [| Null; Integer 2L |]
         [| Null; Integer 1L |]
       ]
