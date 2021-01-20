namespace OpenDiffix.Core

open FsToolkit.ErrorHandling

type ExpressionType =
  | NullType
  | StringType
  | IntegerType
  | FloatType
  | BooleanType

type Row = Value array

type Function =
  | Plus
  | Minus
  | Equality
  | Not
  | And
  | Or
  | Lt
  | LtE
  | Gt
  | GtE
  | Count
  | Sum

  static member FromString =
    function
    | "+" -> Ok Plus
    | "-" -> Ok Minus
    | "=" -> Ok Equality
    | "count" -> Ok Count
    | "sum" -> Ok Sum
    | other -> Error $"Unknown function %A{other}"

  static member TypeInfo =
    function
    | Plus
    | Minus
    | Equality
    | Not
    | And
    | Or
    | Lt
    | LtE
    | Gt
    | GtE -> Scalar
    | Count
    | Sum -> Aggregate AggregateOptions.Default

  static member ReturnType fn (args: Expression list) =
    match fn with
    | Plus
    | Minus ->
      args
      |> List.tryFind(fun arg ->
        match (Expression.GetType arg) with
        | Ok FloatType -> true
        | _ -> false
      )
      |> Option.map(fun _ -> FloatType)
      |> Option.defaultValue IntegerType
      |> Ok
    | Not
    | And
    | Equality
    | Or
    | Lt
    | LtE
    | Gt
    | GtE -> Ok BooleanType
    | Count -> Ok IntegerType
    | Sum ->
      List.tryHead args
      |> Result.requireSome "Sum requires an argument"
      |> Result.bind Expression.GetType

and Expression =
  | FunctionExpr of fn: Function * args: Expression list * functionType: FunctionType
  | ColumnReference of index: int * exprType: ExpressionType
  | Constant of value: Value

  static member GetType =
    function
    | FunctionExpr (fn, args, _fnType) -> Function.ReturnType fn args
    | ColumnReference (_, exprType) -> Ok exprType
    | Constant (String _) -> Ok StringType
    | Constant (Integer _) -> Ok IntegerType
    | Constant (Boolean _) -> Ok BooleanType
    | Constant (Float _) -> Ok FloatType
    | Constant Null -> Ok NullType

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
      | Float a, Float b -> Float(a + b)
      | Float a, Integer b -> Float(a + float b)
      | Integer a, Float b -> Float(float a + b)
      | _ -> invalidOverload "+")

  let sub =
    nullableBinaryFunction
      (function
      | Integer a, Integer b -> Integer(a - b)
      | Float a, Float b -> Float(a - b)
      | Float a, Integer b -> Float(a - float b)
      | Integer a, Float b -> Float(float a - b)
      | _ -> invalidOverload "-")

  let equals =
    nullableBinaryFunction
      (function
      | a, b when a = b -> Boolean true
      | Integer a, Float b -> Boolean(float a = b)
      | Float a, Integer b -> Boolean(a = float b)
      | _ -> Boolean false)

  let not _ctx args =
    match args with
    | [ Boolean b ] -> Boolean(not b)
    | [ Null ] -> Null
    | _ -> invalidOverload "not"

  let binaryBooleanCheck check _ctx =
    function
    | [_a; _b] as values ->
      values
      |> List.map Value.IsTruthy
      |> List.reduce check
      |> Value.Boolean
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
         | [ Null ] -> 0
         | []
         | [ _ ] -> 1
         | _ -> invalidOverload "count")
    |> Integer

module Expression =
  let invokeFunction ctx fn args =
    match fn with
    | Plus -> DefaultFunctions.add ctx args
    | Minus -> DefaultFunctions.sub ctx args
    | Equality -> DefaultFunctions.equals ctx args
    | Not -> DefaultFunctions.not ctx args
    | And -> DefaultFunctions.binaryBooleanCheck (&&) ctx args
    | Or -> DefaultFunctions.binaryBooleanCheck (||) ctx args
    | Lt -> DefaultFunctions.binaryBooleanCheck (<) ctx args
    | LtE -> DefaultFunctions.binaryBooleanCheck (<=) ctx args
    | Gt -> DefaultFunctions.binaryBooleanCheck (>) ctx args
    | GtE -> DefaultFunctions.binaryBooleanCheck (>=) ctx args
    | Count | Sum -> failwith "Aggregate functions are invoked using invokeAggregate"

  let invokeAggregate ctx fn mappedArgs =
    match fn with
    | Count -> DefaultAggregates.count ctx mappedArgs
    | Sum -> DefaultAggregates.sum ctx mappedArgs
    | Plus | Minus | Equality | Not | And | Or | Lt | LtE | Gt | GtE ->
      failwith "Aggregate functions are invoked using invokeAggregate"

  let rec evaluate (ctx: EvaluationContext) (row: Row) (expr: Expression) =
    match expr with
    | FunctionExpr (fn, args, Scalar) -> invokeFunction ctx fn (args |> List.map (evaluate ctx row))
    | FunctionExpr (name, _, _) -> failwith $"Invalid usage of aggregate '{name}'."
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
        | FunctionExpr (name, args, Scalar) ->
            let evaluatedArgs = args |> List.map (evaluateAggregated ctx groupings rows)
            invokeFunction ctx name evaluatedArgs
        | FunctionExpr (name, args, Aggregate opts) ->
            let aggregateArgs = prepareAggregateArgs ctx args opts rows
            invokeAggregate ctx name aggregateArgs
        | ColumnReference _ -> failwith $"Incorrect column reference in aggregated context."
        | Constant value -> value

  let defaultAggregate = Aggregate AggregateOptions.Default

  let distinctAggregate = Aggregate { AggregateOptions.Default with Distinct = true }
