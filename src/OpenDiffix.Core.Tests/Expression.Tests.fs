module OpenDiffix.Core.ExpressionTests

open Xunit
open FsUnit.Xunit

open CommonTypes

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
  let Multiply () =
    runsBinary
      Multiply
      [
        Integer 5L, Integer 3L, Integer 15L
        Real 2.5, Integer 3L, Real 7.5
        Integer 4L, Real 2.5, Real 10.0
        Integer 3L, Null, Null
        Null, Integer 3L, Null
      ]

    fails
      Multiply
      [ //
        [ Integer 5L; String "a" ]
        [ Boolean true; Integer 1L ]
        [ String "a"; Real 2.5 ]
      ]

  [<Fact>]
  let Divide () =
    runsBinary
      Divide
      [
        Integer 5L, Integer 3L, Integer 1L
        Real 6.0, Integer 3L, Real 2.0
        Integer 3L, Real 2.0, Real 1.5
        Integer 3L, Null, Null
        Null, Integer 3L, Null
      ]

    fails
      Divide
      [ //
        [ Integer 5L; String "a" ]
        [ Boolean true; Integer 1L ]
        [ String "a"; Real 2.5 ]
      ]

  [<Fact>]
  let Modulo () =
    runsBinary
      Modulo
      [ //
        Integer 5L, Integer 3L, Integer 2L
        Integer 5L, Integer -3L, Integer 2L
        Integer 3L, Null, Null
      ]

    fails
      Modulo
      [ //
        [ Integer 2L; Real 1.0 ]
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

  [<Fact>]
  let Round () =
    runsUnary
      Round
      [
        Integer 2L, Integer 2L
        Real 1.5, Integer 2L
        Real 2.5, Integer 3L
        Real 2.2, Integer 2L
        Real 0., Integer 0L
        Real -0.2, Integer 0L
        Null, Null
      ]

    fails
      Round
      [ //
        [ Boolean true ]
        [ String "a" ]
      ]

  [<Fact>]
  let RoundBy () =
    runsBinary
      RoundBy
      [
        Integer 3L, Integer 2L, Integer 4L
        Integer 3L, Real 2.5, Real 2.5
        Integer 3L, Integer -1L, Null
        Real 2.5, Integer 2L, Integer 2L
        Real 3.5, Real 2.0, Real 4.0
        Integer 1L, Real 0.5, Real 1.0
        Real 1.3245, Real 0.5, Real 1.5
      ]

    fails
      RoundBy
      [ //
        [ Integer 5L; String "a" ]
        [ String "a"; Real 1.0 ]
      ]

  [<Fact>]
  let Ceil () =
    runsUnary
      Ceil
      [
        Integer 2L, Integer 2L
        Real 1.5, Integer 2L
        Real 2.2, Integer 3L
        Real 0., Integer 0L
        Real -0.2, Integer 0L
        Null, Null
      ]

    fails
      Ceil
      [ //
        [ Boolean true ]
        [ String "a" ]
      ]

  [<Fact>]
  let CeilBy () =
    runsBinary
      CeilBy
      [
        Integer 3L, Integer 2L, Integer 4L
        Integer 3L, Real 2.5, Real 5.0
        Integer 3L, Integer -1L, Null
        Real 2.5, Integer 2L, Integer 4L
        Real 3.5, Real 2.0, Real 4.0
        Integer 1L, Real 0.5, Real 1.0
        Real 1.233, Real 0.5, Real 1.5
      ]

    fails
      CeilBy
      [ //
        [ Integer 5L; String "a" ]
        [ String "a"; Real 1.0 ]
      ]

  [<Fact>]
  let Floor () =
    runsUnary
      Floor
      [
        Integer 2L, Integer 2L
        Real 1.5, Integer 1L
        Real 2.2, Integer 2L
        Real 0., Integer 0L
        Real -0.2, Integer -1L
        Null, Null
      ]

    fails
      Floor
      [ //
        [ Boolean true ]
        [ String "a" ]
      ]

  [<Fact>]
  let FloorBy () =
    runsBinary
      FloorBy
      [
        Integer 3L, Integer 2L, Integer 2L
        Integer 3L, Real 2.5, Real 2.5
        Integer 3L, Integer -1L, Null
        Real 2.5, Integer 2L, Integer 2L
        Real 3.5, Real 2.0, Real 2.0
        Integer 1L, Real 0.5, Real 1.0
        Real 1.3245, Real 0.5, Real 1.0
      ]

    fails
      FloorBy
      [ //
        [ Integer 5L; String "a" ]
        [ String "a"; Real 1.0 ]
      ]

  [<Fact>]
  let Abs () =
    runsUnary
      Abs
      [ //
        Real 1.5, Real 1.5
        Real 0., Real 0.
        Real -0.2, Real 0.2
        Integer -3L, Integer 3L
        Null, Null
      ]

    fails
      Abs
      [ //
        [ Boolean true ]
        [ String "a" ]
      ]

  [<Fact>]
  let Lower () =
    runsUnary
      Lower
      [ //
        String "_aBC_", String "_abc_"
        Null, Null
      ]

    fails Lower [ [ Integer 3L ] ]

  [<Fact>]
  let Upper () =
    runsUnary
      Upper
      [ //
        String "_aBc_", String "_ABC_"
        Null, Null
      ]

    fails Upper [ [ Integer 3L ] ]

  [<Fact>]
  let Substring () =
    [
      "abcd", 1, 2, String "ab"
      "abcd", -1, 2, Null
      "abcd", 1, -2, Null
      "abcd", 2, 3, String "bcd"
      "abcd", 4, 0, String ""
      "abcd", 4, 1, String "d"
      "abcd", 4, 3, String "d"
      "abcd", 1, 7, String "abcd"
    ]
    |> List.iter (fun (v, s, l, result) ->
      Expression.evaluateScalarFunction Substring [ String v; Integer(int64 s); Integer(int64 l) ]
      |> should equal result
    )

    fails Substring [ [ String "abc"; Integer 3L; Real 3.0 ] ]

  [<Fact>]
  let Concat () =
    runsBinary
      Concat
      [ //
        String "ab", String "AB", String "abAB"
        Null, String "ab", Null
      ]

    fails Concat [ [ Integer 3L; String "abc" ] ]

  [<Fact>]
  let WidthBucket () =
    [ // value, bottom, top, count, result
      Integer 1L, Integer 1L, Integer 10L, Integer 5L, Integer 1L
      Integer 2L, Integer 1L, Integer 10L, Integer 5L, Integer 1L
      Integer 3L, Integer 1L, Integer 10L, Integer 5L, Integer 2L
      Integer 9L, Integer 1L, Integer 10L, Integer 5L, Integer 5L
      Integer 10L, Integer 1L, Integer 10L, Integer 5L, Integer 6L
      Integer 1000L, Integer 1L, Integer 10L, Integer 5L, Integer 6L
      Integer 0L, Integer 1L, Integer 10L, Integer 5L, Integer 0L
      Integer -10L, Integer 1L, Integer 10L, Integer 5L, Integer 0L

      Real 1., Real 1., Real 10., Integer 5L, Integer 1L
      Real 1.5, Real 1., Real 10., Integer 5L, Integer 1L
      Real 3., Real 1., Real 10., Integer 5L, Integer 2L
      Real 9.5, Real 1., Real 10., Integer 5L, Integer 5L
      Real 10., Real 1., Real 10., Integer 5L, Integer 6L
      Real 1000., Real 1., Real 10., Integer 5L, Integer 6L
      Real 0., Real 1., Real 10., Integer 5L, Integer 0L
      Real -10., Real 1., Real 10., Integer 5L, Integer 0L

      Null, Integer 1L, Integer 10L, Integer 5L, Null
      Integer 0L, Integer 1L, Integer 10L, Null, Null
    ]
    |> List.iter (fun (v, b, t, c, result) ->
      Expression.evaluateScalarFunction WidthBucket [ v; b; t; c ]
      |> should equal result
    )

    fails WidthBucket [ [ Real 3.0; Real 1.0; Real 10.0; Real 10.0 ] ]

  [<Fact>]
  let Cast () =
    runsBinary
      Cast
      [ //
        String "  1 ", String "integer", Integer 1L
        String "+0", String "integer", Integer 0L
        String "+0.0", String "integer", Null
        String "-2", String "integer", Integer -2L
        String "0.5", String "real", Real 0.5
        String "-12.334", String "real", Real -12.334
        String "-12.334.8", String "real", Null
        String "true", String "boolean", Boolean true
        String "TRUE", String "boolean", Boolean true
        String "1", String "boolean", Boolean true
        String "", String "boolean", Null
        String "not a bool", String "boolean", Null
        String "False", String "boolean", Boolean false
        String "  False", String "boolean", Boolean false
        String "False  ", String "boolean", Boolean false
        Integer 100L, String "boolean", Boolean true
        Integer 0L, String "boolean", Boolean false
        Integer -1L, String "real", Real -1.
        Real 0.7, String "integer", Integer 1L
        Real 0.7, String "text", String "0.7"
        Integer -2L, String "text", String "-2"
        Boolean false, String "text", String "false"
        String "2013-01-08 13:56:41", String "timestamp", Timestamp(System.DateTime(2013, 1, 8, 13, 56, 41))
        Null, String "integer", Null
      ]

  [<Fact>]
  let NullIf () =
    runsBinary
      NullIf
      [ //
        String "a", String "b", String "a"
        Integer 1, Integer 2, Integer 1
        Real 1.1, Real 1.2, Real 1.1
        Boolean false, Boolean true, Boolean false
        List [], List [ Integer 1 ], List []
        String "a", String "a", Null
        Integer 1, Integer 1, Null
        Real 1.1, Real 1.1, Null
        Boolean false, Boolean false, Null
        List [], List [], Null
        Null, Null, Null
        // cases leveraging the mixed integer/real binary coercion
        Integer 1, Real 1.0, Null
        Real 1.0, Integer 1, Null
        Integer 1, Real 2.0, Real 1.0
        Real 1.0, Integer 2, Real 1.0
        // special cases, differing from PostgreSQL
        String "a", Integer 1, String "a"
      ]

  [<Fact>]
  let DateTrunc () =
    runsBinary
      DateTrunc
      [ //
        String "second",
        Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41, 123)),
        Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41))
        String "minute",
        Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)),
        Timestamp(System.DateTime(2113, 2, 8, 13, 56, 0))
        String "hour",
        Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)),
        Timestamp(System.DateTime(2113, 2, 8, 13, 0, 0))
        String "day",
        Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)),
        Timestamp(System.DateTime(2113, 2, 8, 0, 0, 0))
        String "week", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Timestamp(System.DateTime(2113, 2, 6))
        String "month", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Timestamp(System.DateTime(2113, 2, 1))
        String "quarter", Timestamp(System.DateTime(2113, 5, 8)), Timestamp(System.DateTime(2113, 4, 1))
        String "year", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Timestamp(System.DateTime(2113, 1, 1))
        String "decade", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Timestamp(System.DateTime(2110, 1, 1))
        String "century", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Timestamp(System.DateTime(2101, 1, 1))
        String "millennium", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Timestamp(System.DateTime(2001, 1, 1))
        String "century", Timestamp(System.DateTime(2000, 2, 8, 13, 56, 41)), Timestamp(System.DateTime(1901, 1, 1))
        String "millennium", Timestamp(System.DateTime(2000, 2, 8, 13, 56, 41)), Timestamp(System.DateTime(1001, 1, 1))
      ]

    fails
      DateTrunc
      [ //
        [ String "secondz"; Timestamp(System.DateTime(2113, 2, 6)) ]
      ]

  [<Fact>]
  let Extract () =
    runsBinary
      Extract
      [ //
        String "second", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41, 123)), Real 41.0
        String "epoch", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41, 123)), Real 4516005401.123
        String "minute", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 56.0
        String "hour", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 13.0
        String "day", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 8.0
        String "dow", Timestamp(System.DateTime(2113, 2, 5, 13, 56, 41)), Real 0.0
        String "doy", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 39.0
        String "isodow", Timestamp(System.DateTime(2113, 2, 5, 13, 56, 41)), Real 7.0
        String "week", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 6.0
        String "month", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 2.0
        String "quarter", Timestamp(System.DateTime(2113, 5, 8)), Real 2.0
        String "year", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 2113.0
        String "isoyear", Timestamp(System.DateTime(2113, 1, 1, 13, 56, 41)), Real 2112.0
        String "decade", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 211.0
        String "century", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 22.0
        String "millennium", Timestamp(System.DateTime(2113, 2, 8, 13, 56, 41)), Real 3.0
        String "century", Timestamp(System.DateTime(2000, 2, 8, 13, 56, 41)), Real 20.0
        String "millennium", Timestamp(System.DateTime(2000, 2, 8, 13, 56, 41)), Real 2.0
      ]

    fails
      Extract
      [ //
        [ String "secondz"; Timestamp(System.DateTime(2113, 2, 6)) ]
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

let evaluate expr = Expression.evaluate testRow expr

let aggContext = { GroupingLabels = [||]; Aggregators = [||] }

let anonContext =
  {
    BucketSeed = 0UL
    BaseLabels = []
    AnonymizationParams = AnonymizationParams.Default
  }

let evaluateAggregator aggSpec args =
  evaluateAggregator (aggContext, Some anonContext, None) aggSpec args testRows

[<Fact>]
let ``evaluate scalar expressions`` () =
  // select val_int + 3
  evaluate (FunctionExpr(ScalarFunction Add, [ colRef1; Constant(Integer 3L) ]))
  |> should equal (Integer 10L)

  // select val_str
  evaluate colRef0 |> should equal (String "Some text")

[<Fact>]
let ``evaluate standard aggregators`` () =
  let count = Count, AggregateOptions.Default
  let sum = Sum, AggregateOptions.Default
  let countDistinct = Count, { AggregateOptions.Default with Distinct = true }

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
