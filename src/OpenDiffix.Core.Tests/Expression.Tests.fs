module OpenDiffix.Core.ExpressionTests

open Xunit
open FsUnit.Xunit

open CommonTypes

let ctx = EvaluationContext.Default

module DefaultFunctionsTests =
  let runsBinary fn expectations =
    expectations
    |> List.iter (fun (a, b, result) -> Expression.evaluateScalarFunction fn [ a; b ] |> should equal result)

  let runsUnary fn expectations =
    expectations
    |> List.iter (fun (a, result) -> Expression.evaluateScalarFunction fn [ a ] |> should equal result)

  let fails fn cases =
    cases
    |> List.iter (fun args -> (fun () -> Expression.evaluateScalarFunction fn args |> ignore) |> shouldFail)

  [<Fact>]
  let Add () =
    runsBinary
      Add
      [
        Integer 5L, Integer 3L, Integer 8L
        Real 2.5, Integer 3L, Real 5.5
        Integer 4L, Real 2.5, Real 6.5
        Integer 3L, Null, Null
        Null, Integer 3L, Null
      ]

    fails
      Add
      [ //
        [ Integer 5L; String "a" ]
        [ Boolean true; Integer 1L ]
        [ String "a"; Real 2.5 ]
      ]

  [<Fact>]
  let Subtract () =
    runsBinary
      Subtract
      [
        Integer 5L, Integer 3L, Integer 2L
        Real 2.5, Integer 3L, Real -0.5
        Integer 3L, Real 2.5, Real 0.5
        Integer 3L, Null, Null
        Null, Integer 3L, Null
      ]

    fails
      Subtract
      [ //
        [ Integer 5L; String "a" ]
        [ Boolean true; Integer 1L ]
        [ String "a"; Real 2.5 ]
      ]

  [<Fact>]
  let Equals () =
    runsBinary
      Equals
      [
        Integer 3L, Integer 3L, Boolean true
        Integer 3L, Integer 4L, Boolean false
        Real 3., Integer 3L, Boolean true
        Real 3., Integer 2L, Boolean false
        Null, Null, Null
        Null, Integer 3L, Null
        Real 3., Null, Null
        String "a", String "a", Boolean true
        String "a", String "abc", Boolean false
        Null, String "abc", Null
        Boolean false, Boolean false, Boolean true
      ]

  [<Fact>]
  let Not () =
    runsUnary
      Not
      [ //
        Boolean true, Boolean false
        Boolean false, Boolean true
        Null, Null
      ]

  [<Fact>]
  let IsNull () =
    runsUnary
      IsNull
      [ //
        String "abc", Boolean false
        Null, Boolean true
      ]

  [<Fact>]
  let Length () =
    runsUnary
      Length
      [ //
        String "abc", Integer 3L
        Null, Null
      ]

  [<Fact>]
  let And () =
    runsBinary
      And
      [ //
        Boolean true, Boolean true, Boolean true
        Boolean true, Boolean false, Boolean false
        Boolean false, Boolean false, Boolean false
        Null, Boolean true, Null
        Null, Boolean false, Boolean false
        Null, Null, Null
      ]

  [<Fact>]
  let Or () =
    runsBinary
      Or
      [ //
        Boolean true, Boolean true, Boolean true
        Boolean true, Boolean false, Boolean true
        Boolean false, Boolean false, Boolean false
        Boolean true, Null, Boolean true
        Boolean false, Null, Null
        Null, Null, Null
      ]

  [<Fact>]
  let Lt () =
    runsBinary
      Lt
      [
        Integer 3L, Integer 3L, Boolean false
        Integer 3L, Integer 4L, Boolean true
        Real 3., Integer 3L, Boolean false
        Real 3., Integer 2L, Boolean false
        Null, Null, Null
        Null, Integer 3L, Null
        Real 3., Null, Null
        String "a", String "a", Boolean false
        String "a", String "abc", Boolean true
        Null, String "abc", Null
        Boolean false, Boolean false, Boolean false
      ]

  [<Fact>]
  let LtE () =
    runsBinary
      LtE
      [
        Integer 3L, Integer 3L, Boolean true
        Integer 3L, Integer 4L, Boolean true
        Real 3., Integer 3L, Boolean true
        Real 3., Integer 2L, Boolean false
        Null, Null, Null
        Null, Integer 3L, Null
        Real 3., Null, Null
        String "a", String "a", Boolean true
        String "a", String "abc", Boolean true
        Null, String "abc", Null
        Boolean false, Boolean false, Boolean true
      ]

  [<Fact>]
  let Gt () =
    runsBinary
      Gt
      [
        Integer 3L, Integer 3L, Boolean false
        Integer 3L, Integer 4L, Boolean false
        Real 3., Integer 3L, Boolean false
        Real 3., Integer 2L, Boolean true
        Null, Null, Null
        Null, Integer 3L, Null
        Real 3., Null, Null
        String "a", String "a", Boolean false
        String "a", String "abc", Boolean false
        Null, String "abc", Null
        Boolean false, Boolean false, Boolean false
      ]

  [<Fact>]
  let GtE () =
    runsBinary
      GtE
      [
        Integer 3L, Integer 3L, Boolean true
        Integer 3L, Integer 4L, Boolean false
        Real 3., Integer 3L, Boolean true
        Real 3., Integer 2L, Boolean true
        Null, Null, Null
        Null, Integer 3L, Null
        Real 3., Null, Null
        String "a", String "a", Boolean true
        String "a", String "abc", Boolean false
        Null, String "abc", Null
        Boolean false, Boolean false, Boolean true
      ]

let makeRows (ctor1, ctor2, ctor3) (rows: ('a * 'b * 'c) list) : Row list =
  rows |> List.map (fun (a, b, c) -> [| ctor1 a; ctor2 b; ctor3 c |])

let makeRow strValue intValue floatValue =
  [| String strValue; Integer intValue; Real floatValue |]

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

let evaluate expr = Expression.evaluate ctx testRow expr

let evaluateAggregator fn args = evaluateAggregator ctx fn args testRows

[<Fact>]
let ``evaluate scalar expressions`` () =
  // select val_int + 3
  evaluate (FunctionExpr(ScalarFunction Add, [ colRef1; Constant(Integer 3L) ]))
  |> should equal (Integer 10L)

  // select val_str
  evaluate colRef0 |> should equal (String "Some text")

[<Fact>]
let ``evaluate standard aggregators`` () =
  let count = AggregateFunction(Count, AggregateOptions.Default)
  let sum = AggregateFunction(Sum, AggregateOptions.Default)
  let countDistinct = AggregateFunction(Count, { AggregateOptions.Default with Distinct = true })

  // select sum(val_float - val_int)
  evaluateAggregator sum [ FunctionExpr(ScalarFunction Subtract, [ colRef2; colRef1 ]) ]
  |> should equal (Real 2.0)

  // select count(*)
  evaluateAggregator count [] |> should equal (Integer 4L)

  // select count(1)
  evaluateAggregator count [ Constant(Integer 1L) ] |> should equal (Integer 4L)

  // select count(distinct val_str)
  evaluateAggregator countDistinct [ colRef0 ] |> should equal (Integer 2L)

  // select sum()
  (fun () -> evaluateAggregator sum [] |> ignore) |> shouldFail

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
         OrderBy(ColumnReference(0, StringType), Ascending, NullsLast)
         OrderBy(ColumnReference(1, IntegerType), Descending, NullsFirst)
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
