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

and SetFunction =
  | GenerateSeries

  static member ReturnType fn (_args: Expression list) =
    match fn with
    | GenerateSeries -> Ok IntegerType

and Function =
  | ScalarFunction of fn: ScalarFunction
  | AggregateFunction of fn: AggregateFunction * options: AggregateOptions
  | SetFunction of fn: SetFunction

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
  | List of Expression list

  static member GetType expression =
    let listTypes values =
      result {
        let! valueTypes = values |> List.distinct |> List.sequenceResultM

        match valueTypes with
        | [] -> return! Error "Unknown type"
        | [ valueType ] -> return ListType valueType
        | _ -> return UnknownType "mixed type" |> ListType
      }

    match expression with
    | FunctionExpr (ScalarFunction fn, args) -> ScalarFunction.ReturnType fn args
    | FunctionExpr (AggregateFunction (fn, _options), args) -> AggregateFunction.ReturnType fn args
    | FunctionExpr (SetFunction fn, args) -> SetFunction.ReturnType fn args
    | List values -> values |> List.map Expression.GetType |> listTypes
    | ColumnReference (_, exprType) -> Ok exprType
    | Constant (String _) -> Ok StringType
    | Constant (Integer _) -> Ok IntegerType
    | Constant (Boolean _) -> Ok BooleanType
    | Constant (Real _) -> Ok RealType
    | Constant (Value.List values) -> values |> List.map (Constant >> Expression.GetType) |> listTypes
    | Constant Null -> Ok(UnknownType null)

  static member Map(expression, f: Expression -> Expression) =
    match expression with
    | FunctionExpr (fn, args) -> f (FunctionExpr(fn, List.map (fun (arg: Expression) -> Expression.Map(arg, f)) args))
    | expr -> f expr

  static member Collect<'T> expression (f: Expression -> 'T option) =
    let innerItems =
      match expression with
      | FunctionExpr (_, args) -> List.collect (fun arg -> Expression.Collect arg f) args
      | _ -> []

    match f expression with
    | Some t -> t :: innerItems
    | None -> innerItems

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

    // From now on, if the unary or binary function gets a `Null` argument, we return `Null` directly.
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

  let evaluateSetFunction fn args =
    match fn, args with
    | GenerateSeries, [ Integer count ] -> seq { for i in 1L .. count -> Integer i }
    | _ -> failwith $"Invalid usage of set function '%A{fn}'."

  let rec evaluate (ctx: EvaluationContext) (row: Row) (expr: Expression) =
    match expr with
    | FunctionExpr (ScalarFunction fn, args) -> evaluateScalarFunction fn (args |> List.map (evaluate ctx row))
    | FunctionExpr (AggregateFunction (fn, _options), _) -> failwith $"Invalid usage of aggregate function '%A{fn}'."
    | FunctionExpr (SetFunction fn, _) -> failwith $"Invalid usage of set function '%A{fn}'."
    | List expressions -> expressions |> List.map (evaluate ctx row) |> Value.List
    | ColumnReference (index, _) -> row.[index]
    | Constant value -> value

  let sortRows ctx orderings (rows: Row seq) =
    let rec performSort orderings rows =
      match orderings with
      | [] -> rows
      | (OrderBy (expr, direction, nulls)) :: tail ->
          rows
          |> Seq.sortWith (fun rowA rowB ->
            let expressionA = evaluate ctx rowA expr
            let expressionB = evaluate ctx rowB expr
            Value.comparer direction nulls expressionA expressionB
          )
          |> performSort tail

    performSort (List.rev orderings) rows
