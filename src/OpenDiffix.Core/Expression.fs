namespace OpenDiffix.Core

open FsToolkit.ErrorHandling

type AggregateFunction =
  | Count
  | Sum

  static member ReturnType fn (args: Expression list) =
    match fn with
    | Count -> Ok IntegerType
    | Sum ->
        List.tryHead args
        |> Result.requireSome "Sum requires an argument"
        |> Result.bind Expression.GetType

and ScalarFunction =
  | Plus
  | Minus
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
    | Plus
    | Minus ->
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
    | "+" -> Ok(ScalarFunction Plus)
    | "-" -> Ok(ScalarFunction Minus)
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

and FunctionType =
  | Scalar
  | Aggregate of options: AggregateOptions

and OrderByExpression = Expression * OrderByDirection * OrderByNullsBehavior

and AggregateOptions =
  {
    Distinct: bool
    OrderBy: OrderByExpression list
  }
  static member Default = { Distinct = false; OrderBy = [] }

type EvaluationContext = EmptyContext

type ScalarArgs = Value list
type AggregateArgs = seq<Value list>

module ExpressionUtils =
  let invalidOverload name = failwith $"Invalid overload called for function '{name}'."

  let mapSingleArg name (args: AggregateArgs) =
    args
    |> Seq.map
         (function
         | [ arg ] -> arg
         | _ -> invalidOverload name)

  let filterNulls args = args |> Seq.filter (fun v -> v <> Null)

  let binaryFunction fn =
    fun _ctx args ->
      match args with
      | [ a; b ] -> fn (a, b)
      | _ -> failwith "Expected 2 arguments in function."

  let nullableBinaryFunction fn =
    binaryFunction
      (function
      | Null, _ -> Null
      | _, Null -> Null
      | a, b -> fn (a, b))

module DefaultFunctions =
  open ExpressionUtils

  let add =
    nullableBinaryFunction
      (function
      | Integer a, Integer b -> Integer(a + b)
      | Real a, Real b -> Real(a + b)
      | Real a, Integer b -> Real(a + double b)
      | Integer a, Real b -> Real(double a + b)
      | _ -> invalidOverload "+")

  let sub =
    nullableBinaryFunction
      (function
      | Integer a, Integer b -> Integer(a - b)
      | Real a, Real b -> Real(a - b)
      | Real a, Integer b -> Real(a - double b)
      | Integer a, Real b -> Real(double a - b)
      | _ -> invalidOverload "-")

  let equals =
    nullableBinaryFunction
      (function
      | a, b when a = b -> Boolean true
      | Integer a, Real b -> Boolean(float a = b)
      | Real a, Integer b -> Boolean(a = float b)
      | _ -> Boolean false)

  let not _ctx args =
    match args with
    | [ Boolean b ] -> Boolean(not b)
    | [ Null ] -> Null
    | _ -> invalidOverload "not"

  let length _ctx args =
    match args with
    | [ String s ] -> Integer(int64 s.Length)
    | [ Null ] -> Null
    | _ -> invalidOverload "length"

  let binaryBooleanCheck check _ctx =
    function
    | [ _a; _b ] as values -> values |> List.map Value.isTruthy |> List.reduce check |> Value.Boolean
    | _ -> failwith "Expected two arguments for binary condition"

module DefaultAggregates =
  open ExpressionUtils

  let sum ctx (values: AggregateArgs) =
    let values = values |> mapSingleArg "sum" |> filterNulls

    if Seq.isEmpty values then Null else values |> Seq.reduce (fun a b -> DefaultFunctions.add ctx [ a; b ])

  let count _ctx (values: AggregateArgs) =
    values
    |> Seq.sumBy
         (function
         | [ Null ] -> 0L
         | []
         | [ _ ] -> 1L
         | _ -> invalidOverload "count")
    |> Integer

module Expression =
  let invokeScalarFunction ctx fn args =
    match fn with
    | Plus -> DefaultFunctions.add ctx args
    | Minus -> DefaultFunctions.sub ctx args
    | Equals -> DefaultFunctions.equals ctx args
    | Not -> DefaultFunctions.not ctx args
    | And -> DefaultFunctions.binaryBooleanCheck (&&) ctx args
    | Or -> DefaultFunctions.binaryBooleanCheck (||) ctx args
    | Lt -> DefaultFunctions.binaryBooleanCheck (<) ctx args
    | LtE -> DefaultFunctions.binaryBooleanCheck (<=) ctx args
    | Gt -> DefaultFunctions.binaryBooleanCheck (>) ctx args
    | GtE -> DefaultFunctions.binaryBooleanCheck (>=) ctx args
    | Length -> DefaultFunctions.length ctx args

  let invokeAggregateFunction ctx fn mappedArgs =
    match fn with
    | Count -> DefaultAggregates.count ctx mappedArgs
    | Sum -> DefaultAggregates.sum ctx mappedArgs

  let rec evaluate (ctx: EvaluationContext) (row: Row) (expr: Expression) =
    match expr with
    | FunctionExpr (ScalarFunction fn, args) -> invokeScalarFunction ctx fn (args |> List.map (evaluate ctx row))
    | FunctionExpr (AggregateFunction (fn, _options), _) -> failwith $"Invalid usage of aggregate '%A{fn}'."
    | ColumnReference (index, _) -> row.[index]
    | Constant value -> value

  open System.Linq

  let rec private thenSort ctx orderings (rows: IOrderedEnumerable<Row>) =
    match orderings with
    | [] -> rows
    | (expr, direction, nulls) :: tail ->
        let sorted = rows.ThenBy((fun row -> evaluate ctx row expr), Value.comparer direction nulls)
        thenSort ctx tail sorted

  let sortRows ctx orderings (rows: seq<Row>) =
    match orderings with
    | [] -> rows
    | (expr, direction, nulls) :: tail ->
        let firstSort = rows.OrderBy((fun row -> evaluate ctx row expr), Value.comparer direction nulls)
        thenSort ctx tail firstSort :> seq<Row>

  let private prepareAggregateArgs ctx args opts rows =
    let projectedArgs =
      rows
      |> sortRows ctx opts.OrderBy
      |> Seq.map (fun row -> args |> List.map (evaluate ctx row))

    if opts.Distinct then Seq.distinct projectedArgs else projectedArgs

  let rec evaluateAggregated (ctx: EvaluationContext)
                             (groupings: Map<Expression, Value>)
                             (rows: seq<Row>)
                             (expr: Expression)
                             =
    match Map.tryFind expr groupings with
    | Some value -> value
    | None ->
        match expr with
        | FunctionExpr (ScalarFunction fn, args) ->
            let evaluatedArgs = args |> List.map (evaluateAggregated ctx groupings rows)
            invokeScalarFunction ctx fn evaluatedArgs
        | FunctionExpr (AggregateFunction (fn, opts), args) ->
            let aggregateArgs = prepareAggregateArgs ctx args opts rows
            invokeAggregateFunction ctx fn aggregateArgs
        | ColumnReference _ -> failwith $"Incorrect column reference in aggregated context."
        | Constant value -> value

  let defaultAggregate = Aggregate AggregateOptions.Default

  let distinctAggregate = Aggregate { AggregateOptions.Default with Distinct = true }
