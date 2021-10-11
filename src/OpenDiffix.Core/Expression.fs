module rec OpenDiffix.Core.Expression

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
  | Ceil ->
    match args with
    | [ _ ] -> IntegerType
    | [ _; amount ] -> typeOf amount
    | _ -> failwith $"Invalid arguments supplied to function `#{fn}`."
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
      | _ -> failwith "Unsupported cast destination type name."

/// Resolves the type of a set function expression.
let typeOfSetFunction fn _args =
  match fn with
  | GenerateSeries -> IntegerType

/// Resolves the type of an aggregate function expression.
let typeOfAggregate fn args =
  match fn with
  | Count
  | DiffixCount -> IntegerType
  | DiffixLowCount -> BooleanType
  | Sum ->
    match typeOfList args with
    | ListType IntegerType -> IntegerType
    | _ -> RealType
  | MergeAids -> ListType MIXED_TYPE

/// Resolves the type of an expression.
let rec typeOf expression =
  match expression with
  | FunctionExpr (ScalarFunction fn, args) -> typeOfScalarFunction fn args
  | FunctionExpr (SetFunction fn, args) -> typeOfSetFunction fn args
  | FunctionExpr (AggregateFunction (fn, _options), args) -> typeOfAggregate fn args
  | ColumnReference (_, exprType) -> exprType
  | Constant c -> Value.typeOf c
  | ListExpr expressions -> typeOfList expressions

// ----------------------------------------------------------------
// Evaluation
// ----------------------------------------------------------------

let widthBucket v b t c =
  let step = (t - b) / float c

  ((v - b) / step) |> floor |> int64 |> max -1L |> min c |> (+) 1L

let private doubleStyle = System.Globalization.NumberFormatInfo.InvariantInfo

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

  | Round, [ Real r ] -> r |> round |> int64 |> Integer
  | Round, [ _; Integer amount ] when amount <= 0L -> Null
  | Round, [ _; Real amount ] when amount <= 0.0 -> Null
  | Round, [ Integer value; Integer amount ] -> (float value / float amount) |> round |> int64 |> (*) amount |> Integer
  | Round, [ Integer value; Real amount ] -> (float value / amount) |> round |> (*) amount |> Real
  | Round, [ Real value; Integer amount ] -> (value / float amount) |> round |> int64 |> (*) amount |> Integer
  | Round, [ Real value; Real amount ] -> (value / amount) |> round |> (*) amount |> Real

  | Ceil, [ Real r ] -> r |> ceil |> int64 |> Integer
  | Ceil, [ _; Integer amount ] when amount <= 0L -> Null
  | Ceil, [ _; Real amount ] when amount <= 0.0 -> Null
  | Ceil, [ Integer value; Integer amount ] -> (float value / float amount) |> ceil |> int64 |> (*) amount |> Integer
  | Ceil, [ Integer value; Real amount ] -> (float value / amount) |> ceil |> (*) amount |> Real
  | Ceil, [ Real value; Integer amount ] -> (value / float amount) |> ceil |> int64 |> (*) amount |> Integer
  | Ceil, [ Real value; Real amount ] -> (value / amount) |> ceil |> (*) amount |> Real

  | Floor, [ Real r ] -> r |> floor |> int64 |> Integer
  | Floor, [ _; Integer amount ] when amount <= 0L -> Null
  | Floor, [ _; Real amount ] when amount <= 0.0 -> Null
  | Floor, [ Integer value; Integer amount ] -> (float value / float amount) |> floor |> int64 |> (*) amount |> Integer
  | Floor, [ Integer value; Real amount ] -> (float value / amount) |> floor |> (*) amount |> Real
  | Floor, [ Real value; Integer amount ] -> (value / float amount) |> floor |> int64 |> (*) amount |> Integer
  | Floor, [ Real value; Real amount ] -> (value / amount) |> floor |> (*) amount |> Real

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

  | Cast, [ String s; String "integer" ] -> if s = "" then Null else s |> System.Int64.Parse |> Integer
  | Cast, [ String s; String "real" ] -> if s = "" then Null else System.Double.Parse(s, doubleStyle) |> Real
  | Cast, [ String s; String "boolean" ] ->
    match s.ToLower() with
    | "true"
    | "1" -> Boolean true
    | "false"
    | "0" -> Boolean false
    | "" -> Null
    | _ -> failwith "Input value is not a valid boolean string."
  | Cast, [ Integer i; String "real" ] -> i |> float |> Real
  | Cast, [ Real r; String "integer" ] -> r |> round |> int64 |> Integer
  | Cast, [ Integer 0L; String "boolean" ] -> Boolean false
  | Cast, [ Integer _; String "boolean" ] -> Boolean true
  | Cast, [ Integer i; String "text" ] -> i.ToString() |> String
  | Cast, [ Real r; String "text" ] -> r.ToString(doubleStyle) |> String
  | Cast, [ Boolean b; String "text" ] -> b.ToString().ToLower() |> String

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
  let rec performSort orderings rows =
    match orderings with
    | [] -> rows
    | OrderBy (expr, direction, nulls) :: tail ->
      let compare = Value.comparer direction nulls

      rows
      |> Seq.sortWith (fun rowA rowB ->
        let valueA = evaluate rowA expr
        let valueB = evaluate rowB expr
        compare valueA valueB
      )
      |> performSort tail

  performSort (List.rev orderings) rows

// ----------------------------------------------------------------
// Factory functions
// ----------------------------------------------------------------

let makeSetFunction setFunctionType args =
  FunctionExpr(SetFunction(setFunctionType), args)

let makeAggregate aggType args =
  FunctionExpr(AggregateFunction(aggType, AggregateOptions.Default), args)

let makeAnd left right =
  FunctionExpr(ScalarFunction And, [ left; right ])

let makeNot expr =
  FunctionExpr(ScalarFunction Not, [ expr ])

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
