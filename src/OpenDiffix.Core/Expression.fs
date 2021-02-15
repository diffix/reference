namespace OpenDiffix.Core

open FsToolkit.ErrorHandling
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

type AggregateFunction =
  | Count
  | DiffixCount
  | DiffixLowCount
  | Sum

  static member ReturnType fn (args: Expression list) =
    match fn with
    | Count
    | DiffixCount -> Ok IntegerType
    | DiffixLowCount -> Ok BooleanType
    | Sum ->
        List.tryHead args
        |> Result.requireSome "Sum requires an argument"
        |> Result.bind Expression.GetType

and ScalarFunction =
  | Add
  | Subtract
  | Equals
  | Not
  | And
  | Or
  | Lt
  | LtE
  | Gt
  | GtE
  | Length

  static member ReturnType fn (args: Expression list) =
    match fn with
    | Add
    | Subtract ->
        args
        |> List.tryFind (fun arg ->
          match (Expression.GetType arg) with
          | Ok RealType -> true
          | _ -> false
        )
        |> Option.map (fun _ -> RealType)
        |> Option.defaultValue IntegerType
        |> Ok
    | Not
    | And
    | Equals
    | Or
    | Lt
    | LtE
    | Gt
    | GtE -> Ok BooleanType
    | Length -> Ok IntegerType

and Function =
  | ScalarFunction of fn: ScalarFunction
  | AggregateFunction of fn: AggregateFunction * options: AggregateOptions

  static member FromString =
    function
    | "count" -> Ok(AggregateFunction(Count, AggregateOptions.Default))
    | "sum" -> Ok(AggregateFunction(Sum, AggregateOptions.Default))
    | "diffix_count" -> Ok(AggregateFunction(DiffixCount, AggregateOptions.Default))
    | "+" -> Ok(ScalarFunction Add)
    | "-" -> Ok(ScalarFunction Subtract)
    | "=" -> Ok(ScalarFunction Equals)
    | "length" -> Ok(ScalarFunction Length)
    | other -> Error $"Unknown function %A{other}"

and Expression =
  | FunctionExpr of fn: Function * args: Expression list
  | ColumnReference of index: int * exprType: ValueType
  | Constant of value: Value

  static member GetType =
    function
    | FunctionExpr (ScalarFunction fn, args) -> ScalarFunction.ReturnType fn args
    | FunctionExpr (AggregateFunction (fn, _options), args) -> AggregateFunction.ReturnType fn args
    | ColumnReference (_, exprType) -> Ok exprType
    | Constant (String _) -> Ok StringType
    | Constant (Integer _) -> Ok IntegerType
    | Constant (Boolean _) -> Ok BooleanType
    | Constant (Real _) -> Ok RealType
    | Constant Null -> Ok(UnknownType null)

  static member Map(expression, f: Expression -> Expression) =
    match expression with
    | FunctionExpr (fn, args) -> f (FunctionExpr(fn, List.map (fun (arg: Expression) -> Expression.Map(arg, f)) args))
    | expr -> f expr

and FunctionType =
  | Scalar
  | Aggregate of options: AggregateOptions

and OrderByExpression =
  | OrderBy of Expression * OrderByDirection * OrderByNullsBehavior

  static member Map(orderBy: OrderByExpression list, f: Expression -> Expression) =
    List.map (fun (orderBy: OrderByExpression) -> OrderByExpression.Map(orderBy, f)) orderBy

  static member Map(orderBy: OrderByExpression, f: Expression -> Expression) =
    match orderBy with
    | OrderBy (exp, direction, nullBehavior) -> OrderBy(f exp, direction, nullBehavior)

and AggregateOptions =
  {
    Distinct: bool
    OrderBy: OrderByExpression list
  }
  static member Default = { Distinct = false; OrderBy = [] }

type EvaluationContext =
  {
    AnonymizationParams: AnonymizationParams
  }
  static member Default = { AnonymizationParams = AnonymizationParams.Default }

type ScalarArgs = Value list
type AggregateArgs = seq<Value list>

module Expression =
  let rec evaluateScalarFunction fn args =
    match fn, args with
    | And, [ Boolean false; _ ] -> Boolean false
    | And, [ _; Boolean false ] -> Boolean false
    | Or, [ Boolean true; _ ] -> Boolean true
    | Or, [ _; Boolean true ] -> Boolean true

    | _, [ Null ] -> Null
    | _, [ Null; _ ] -> Null
    | _, [ _; Null ] -> Null

    | Not, [ Boolean b ] -> Boolean(not b)
    | And, [ Boolean b1; Boolean b2 ] -> Boolean(b1 && b2)
    | Or, [ Boolean b1; Boolean b2 ] -> Boolean(b1 || b2)

    | _, [ Integer i; Real r ] -> evaluateScalarFunction fn [ Real(double i); Real r ]
    | _, [ Real r; Integer i ] -> evaluateScalarFunction fn [ Real r; Real(double i) ]

    | Add, [ Integer i1; Integer i2 ] -> Integer(i1 + i2)
    | Add, [ Real r1; Real r2 ] -> Real(r1 + r2)
    | Subtract, [ Integer i1; Integer i2 ] -> Integer(i1 - i2)
    | Subtract, [ Real r1; Real r2 ] -> Real(r1 - r2)

    | Equals, [ v1; v2 ] -> Boolean(v1 = v2)

    | Lt, [ v1; v2 ] -> Boolean(v1 < v2)
    | LtE, [ v1; v2 ] -> Boolean(v1 <= v2)
    | Gt, [ v1; v2 ] -> Boolean(v1 > v2)
    | GtE, [ v1; v2 ] -> Boolean(v1 >= v2)

    | Length, [ String s ] -> Integer(int64 s.Length)

    | _ -> failwith $"Invalid usage of scalar function '%A{fn}'."

  let rec evaluate (ctx: EvaluationContext) (row: Row) (expr: Expression) =
    match expr with
    | FunctionExpr (ScalarFunction fn, args) -> evaluateScalarFunction fn (args |> List.map (evaluate ctx row))
    | FunctionExpr (AggregateFunction (fn, _options), _) -> failwith $"Invalid usage of aggregate '%A{fn}'."
    | ColumnReference (index, _) -> if index >= row.Length then Null else row.[index]
    | Constant value -> value

  open System.Linq

  let rec private thenSort ctx orderings (rows: IOrderedEnumerable<Row>) =
    match orderings with
    | [] -> rows
    | (OrderBy (expr, direction, nulls)) :: tail ->
        let sorted = rows.ThenBy((fun row -> evaluate ctx row expr), Value.comparer direction nulls)
        thenSort ctx tail sorted

  let sortRows ctx orderings (rows: seq<Row>) =
    match orderings with
    | [] -> rows
    | (OrderBy (expr, direction, nulls)) :: tail ->
        let firstSort = rows.OrderBy((fun row -> evaluate ctx row expr), Value.comparer direction nulls)
        thenSort ctx tail firstSort :> seq<Row>
