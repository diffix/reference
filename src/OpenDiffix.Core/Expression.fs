module rec OpenDiffix.Core.Expression

open System.Globalization
open OpenDiffix.Core.Utils.Math

// ----------------------------------------------------------------
// Type resolution
// ----------------------------------------------------------------

/// Resolves the common type from a list expression.
let typeOfList expressions =
  expressions |> List.map typeOf |> ExpressionType.commonType

/// Resolves the type of a scalar function expression.
let typeOfScalarFunction fn args =
  match fn with
  | Add
  | Subtract
  | Multiply
  | Divide ->
    args
    |> List.map typeOf
    |> function
      | [ IntegerType; IntegerType ] -> IntegerType
      | _ -> RealType
  | Modulo -> IntegerType
  | Not
  | And
  | Equals
  | IsNull
  | Or
  | Lt
  | LtE
  | Gt
  | GtE -> BooleanType
  | Length -> IntegerType
  | Round
  | Floor
  | Ceil -> IntegerType
  | RoundBy
  | FloorBy
  | CeilBy -> args |> List.item 1 |> typeOf
  | Abs -> args |> List.exactlyOne |> typeOf
  | Lower
  | Upper
  | Substring
  | Concat -> StringType
  | WidthBucket -> args |> List.head |> typeOf
  | Cast ->
    args
    |> List.item 1
    |> function
      | Constant (String "integer") -> IntegerType
      | Constant (String "real") -> RealType
      | Constant (String "boolean") -> BooleanType
      | Constant (String "timestamp") -> TimestampType
      | _ -> failwith "Unsupported cast destination type name."
  | NullIf ->
    args
    |> List.map typeOf
    |> function
      | [ IntegerType; RealType ] -> RealType
      | [ RealType; IntegerType ] -> RealType
      | argsTypes -> List.head argsTypes
  | DateTrunc -> TimestampType

/// Resolves the type of a set function expression.
let typeOfSetFunction fn _args =
  match fn with
  | GenerateSeries -> IntegerType

/// Resolves the type of an aggregate function expression.
let typeOfAggregate fn args =
  match fn with
  | Count -> IntegerType
  | CountNoise -> RealType
  | DiffixCount -> IntegerType
  | DiffixCountNoise -> RealType
  | DiffixLowCount -> BooleanType
  | Sum -> args |> List.last |> typeOf
  | SumNoise -> RealType
  | Avg -> RealType
  | AvgNoise -> RealType
  | DiffixSum -> args |> List.last |> typeOf
  | DiffixSumNoise -> RealType
  | DiffixAvg -> RealType
  | DiffixAvgNoise -> RealType
  | CountHistogram
  | DiffixCountHistogram -> ListType(ListType(IntegerType))

/// Resolves the type of an expression.
let rec typeOf expression =
  match expression with
  | FunctionExpr (ScalarFunction fn, args) -> typeOfScalarFunction fn args
  | FunctionExpr (SetFunction fn, args) -> typeOfSetFunction fn args
  | FunctionExpr (AggregateFunction (fn, _options), args) -> typeOfAggregate fn args
  | ColumnReference (_, exprType) -> exprType
  | Constant c -> Value.typeOf c
  | ListExpr expressions -> ListType(typeOfList expressions)

// ----------------------------------------------------------------
// Evaluation
// ----------------------------------------------------------------

let widthBucket v b t c =
  let step = (t - b) / float c

  ((v - b) / step) |> floor |> int64 |> max -1L |> min c |> (+) 1L

let private doubleStyle = NumberFormatInfo.InvariantInfo

/// Evaluates the result of a scalar function invocation.
let rec evaluateScalarFunction fn args =
  match fn, args with
  | And, [ Boolean false; _ ] -> Boolean false
  | And, [ _; Boolean false ] -> Boolean false
  | Or, [ Boolean true; _ ] -> Boolean true
  | Or, [ _; Boolean true ] -> Boolean true

  | IsNull, [ v1 ] -> Boolean(v1 = Null)

  // From now on, if the function gets a `Null` argument, we return `Null` directly.
  | _, args when List.contains Null args -> Null

  | Not, [ Boolean b ] -> Boolean(not b)
  | And, [ Boolean b1; Boolean b2 ] -> Boolean(b1 && b2)
  | Or, [ Boolean b1; Boolean b2 ] -> Boolean(b1 || b2)

  | Round, [ Integer i ] -> Integer i
  | Round, [ Real r ] -> r |> roundAwayFromZero |> int64 |> Integer
  | RoundBy, [ _; Integer amount ] when amount <= 0L -> Null
  | RoundBy, [ _; Real amount ] when amount <= 0.0 -> Null
  | RoundBy, [ Integer value; Integer amount ] ->
    (float value / float amount)
    |> roundAwayFromZero
    |> int64
    |> (*) amount
    |> Integer
  | RoundBy, [ Integer value; Real amount ] -> (float value / amount) |> roundAwayFromZero |> (*) amount |> Real
  | RoundBy, [ Real value; Integer amount ] ->
    (value / float amount) |> roundAwayFromZero |> int64 |> (*) amount |> Integer
  | RoundBy, [ Real value; Real amount ] -> (value / amount) |> roundAwayFromZero |> (*) amount |> Real

  | Ceil, [ Integer i ] -> Integer i
  | Ceil, [ Real r ] -> r |> ceil |> int64 |> Integer
  | CeilBy, [ _; Integer amount ] when amount <= 0L -> Null
  | CeilBy, [ _; Real amount ] when amount <= 0.0 -> Null
  | CeilBy, [ Integer value; Integer amount ] -> (float value / float amount) |> ceil |> int64 |> (*) amount |> Integer
  | CeilBy, [ Integer value; Real amount ] -> (float value / amount) |> ceil |> (*) amount |> Real
  | CeilBy, [ Real value; Integer amount ] -> (value / float amount) |> ceil |> int64 |> (*) amount |> Integer
  | CeilBy, [ Real value; Real amount ] -> (value / amount) |> ceil |> (*) amount |> Real

  | Floor, [ Integer i ] -> Integer i
  | Floor, [ Real r ] -> r |> floor |> int64 |> Integer
  | FloorBy, [ _; Integer amount ] when amount <= 0L -> Null
  | FloorBy, [ _; Real amount ] when amount <= 0.0 -> Null
  | FloorBy, [ Integer value; Integer amount ] ->
    (float value / float amount) |> floor |> int64 |> (*) amount |> Integer
  | FloorBy, [ Integer value; Real amount ] -> (float value / amount) |> floor |> (*) amount |> Real
  | FloorBy, [ Real value; Integer amount ] -> (value / float amount) |> floor |> int64 |> (*) amount |> Integer
  | FloorBy, [ Real value; Real amount ] -> (value / amount) |> floor |> (*) amount |> Real

  | Abs, [ Real r ] -> r |> abs |> Real
  | Abs, [ Integer i ] -> i |> abs |> Integer

  | WidthBucket, [ Integer v; Integer b; Integer t; Integer c ] ->
    widthBucket (float v) (float b) (float t) c |> Integer
  | WidthBucket, [ Real v; Real b; Real t; Integer c ] -> widthBucket v b t c |> Integer

  // Treat mixed integer/real binary operations as real/real operations.
  | _, [ Integer i; Real r ] -> evaluateScalarFunction fn [ Real(double i); Real r ]
  | _, [ Real r; Integer i ] -> evaluateScalarFunction fn [ Real r; Real(double i) ]

  | Add, [ Integer i1; Integer i2 ] -> Integer(i1 + i2)
  | Add, [ Real r1; Real r2 ] -> Real(r1 + r2)
  | Subtract, [ Integer i1; Integer i2 ] -> Integer(i1 - i2)
  | Subtract, [ Real r1; Real r2 ] -> Real(r1 - r2)
  | Multiply, [ Integer i1; Integer i2 ] -> Integer(i1 * i2)
  | Multiply, [ Real r1; Real r2 ] -> Real(r1 * r2)
  | Divide, [ Integer i1; Integer i2 ] -> Integer(i1 / i2)
  | Divide, [ Real r1; Real r2 ] -> Real(r1 / r2)
  | Modulo, [ Integer i1; Integer i2 ] -> Integer(i1 % i2)

  | Equals, [ v1; v2 ] -> Boolean(v1 = v2)

  | Lt, [ v1; v2 ] -> Boolean(v1 < v2)
  | LtE, [ v1; v2 ] -> Boolean(v1 <= v2)
  | Gt, [ v1; v2 ] -> Boolean(v1 > v2)
  | GtE, [ v1; v2 ] -> Boolean(v1 >= v2)

  | Length, [ String s ] -> Integer(int64 s.Length)

  | Lower, [ String s ] -> String(s.ToLower())
  | Upper, [ String s ] -> String(s.ToUpper())
  | Substring, [ String s; Integer start; Integer length ] ->
    let start = int start
    let length = int length

    if start <= 0 || length < 0 then Null
    else if start > s.Length then String ""
    else s.Substring(start - 1, min (s.Length - start + 1) length) |> String
  | Concat, [ String s1; String s2 ] -> String(s1 + s2)

  | Cast, [ String s; String "integer" ] ->
    match System.Int64.TryParse(s) with
    | true, i -> Integer i
    | false, _ -> Null
  | Cast, [ String s; String "real" ] ->
    match System.Double.TryParse(s, NumberStyles.Float ||| NumberStyles.AllowThousands, doubleStyle) with
    | true, r -> Real r
    | false, _ -> Null
  | Cast, [ String s; String "boolean" ] ->
    match s.Trim().ToLower() with
    | "true"
    | "1" -> Boolean true
    | "false"
    | "0" -> Boolean false
    | _ -> Null
  | Cast, [ Integer i; String "real" ] -> i |> float |> Real
  | Cast, [ Real r; String "integer" ] -> r |> roundAwayFromZero |> int64 |> Integer
  | Cast, [ Integer 0L; String "boolean" ] -> Boolean false
  | Cast, [ Integer _; String "boolean" ] -> Boolean true
  | Cast, [ Integer i; String "text" ] -> i.ToString() |> String
  | Cast, [ Real r; String "text" ] -> r.ToString(doubleStyle) |> String
  | Cast, [ Boolean b; String "text" ] -> b.ToString().ToLower() |> String
  | Cast, [ Timestamp ts; String "text" ] ->
    ts.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
    |> String
  | Cast, [ String s; String "timestamp" ] ->
    System.DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture)
    |> Timestamp

  | NullIf, [ x; y ] when x = y -> Null
  | NullIf, [ x; y ] -> x

  | DateTrunc, [ String "second"; Timestamp ts ]
  | DateTrunc, [ String "seconds"; Timestamp ts ] ->
    System.DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, ts.Minute, ts.Second, ts.Kind)
    |> Timestamp
  | DateTrunc, [ String "minute"; Timestamp ts ]
  | DateTrunc, [ String "minutes"; Timestamp ts ] ->
    System.DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, ts.Minute, 0, ts.Kind)
    |> Timestamp
  | DateTrunc, [ String "hour"; Timestamp ts ]
  | DateTrunc, [ String "hours"; Timestamp ts ] ->
    System.DateTime(ts.Year, ts.Month, ts.Day, ts.Hour, 0, 0, ts.Kind) |> Timestamp
  | DateTrunc, [ String "day"; Timestamp ts ]
  | DateTrunc, [ String "days"; Timestamp ts ] -> System.DateTime(ts.Year, ts.Month, ts.Day) |> Timestamp
  | DateTrunc, [ String "week"; Timestamp ts ]
  | DateTrunc, [ String "weeks"; Timestamp ts ] ->
    match ts.DayOfWeek with
    // .NET has Sunday as day 0, while PostgreSQL as day 7.
    | System.DayOfWeek.Sunday -> System.DateTime(ts.Year, ts.Month, ts.Day).AddDays(-6.0)
    | _ -> System.DateTime(ts.Year, ts.Month, ts.Day).AddDays(-(float ts.DayOfWeek) + 1.0)
    |> Timestamp
  | DateTrunc, [ String "month"; Timestamp ts ]
  | DateTrunc, [ String "months"; Timestamp ts ] -> System.DateTime(ts.Year, ts.Month, 1) |> Timestamp
  | DateTrunc, [ String "quarter"; Timestamp ts ] ->
    System.DateTime(ts.Year, ts.Month - (ts.Month - 1) % 3, 1) |> Timestamp
  | DateTrunc, [ String "year"; Timestamp ts ]
  | DateTrunc, [ String "years"; Timestamp ts ] -> System.DateTime(ts.Year, 1, 1) |> Timestamp
  | DateTrunc, [ String "decade"; Timestamp ts ]
  | DateTrunc, [ String "decades"; Timestamp ts ] -> System.DateTime(ts.Year - ts.Year % 10, 1, 1) |> Timestamp
  | DateTrunc, [ String "century"; Timestamp ts ]
  | DateTrunc, [ String "centuries"; Timestamp ts ] -> System.DateTime(ts.Year - (ts.Year - 1) % 100, 1, 1) |> Timestamp
  | DateTrunc, [ String "millennium"; Timestamp ts ]
  | DateTrunc, [ String "millenniums"; Timestamp ts ]
  | DateTrunc, [ String "millennia"; Timestamp ts ] ->
    System.DateTime(ts.Year - (ts.Year - 1) % 1000, 1, 1) |> Timestamp

  | _ -> failwith $"Invalid usage of scalar function '%A{fn}'."

/// Evaluates the result sequence of a set function invocation.
let evaluateSetFunction fn args =
  match fn, args with
  | GenerateSeries, [ Integer count ] -> seq { for i in 1L .. count -> Integer i }
  | _ -> failwith $"Invalid usage of set function '%A{fn}'."

/// Evaluates the expression for a given row.
let rec evaluate (row: Row) (expr: Expression) =
  match expr with
  | FunctionExpr (ScalarFunction fn, args) -> evaluateScalarFunction fn (args |> List.map (evaluate row))
  | FunctionExpr (AggregateFunction (fn, _options), _) -> failwith $"Invalid usage of aggregate function '%A{fn}'."
  | FunctionExpr (SetFunction fn, _) -> failwith $"Invalid usage of set function '%A{fn}'."
  | ListExpr expressions -> expressions |> List.map (evaluate row) |> Value.List
  | ColumnReference (index, _) -> row.[index]
  | Constant value -> value

/// Sorts a row sequence based on the given orderings.
let sortRows orderings (rows: Row seq) =
  let exprsComparers =
    orderings
    |> List.map (fun (OrderBy (expr, direction, nulls)) -> expr, Value.comparer direction nulls)

  let rec compare exprsComparers rowA rowB =
    match exprsComparers with
    | [] -> 0
    | (expr, comparer) :: tail ->
      let valueA = evaluate rowA expr
      let valueB = evaluate rowB expr

      match comparer valueA valueB with
      | 0 -> compare tail rowA rowB
      | order -> order

  rows |> Seq.sortWith (compare exprsComparers)


// ----------------------------------------------------------------
// Factory functions
// ----------------------------------------------------------------

let makeFunction functionType args =
  FunctionExpr(ScalarFunction(functionType), args)

let makeSetFunction setFunctionType args =
  FunctionExpr(SetFunction(setFunctionType), args)

let makeAggregate aggType args =
  FunctionExpr(AggregateFunction(aggType, AggregateOptions.Default), args)

let makeAnd left right =
  FunctionExpr(ScalarFunction And, [ left; right ])

let makeNot expr =
  FunctionExpr(ScalarFunction Not, [ expr ])

let makeEquals left right =
  FunctionExpr(ScalarFunction Equals, [ left; right ])

// ----------------------------------------------------------------
// Misc
// ----------------------------------------------------------------

let rec isScalar expr =
  match expr with
  | FunctionExpr (AggregateFunction _, _) -> false
  | FunctionExpr (_, args) -> List.forall isScalar args
  | _ -> true

let unwrapListExpr expr =
  match expr with
  | ListExpr list -> list
  | _ -> failwith "Expected a list expression"

let isColumnReference arg =
  match arg with
  | ColumnReference _ -> true
  | _ -> false

let isConstant arg =
  match arg with
  | Constant _ -> true
  | _ -> false
