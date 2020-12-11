namespace OpenDiffix.Core

type Value =
  | String of string
  | Integer of int
  | Float of float
  | Boolean of bool
  | Unit
  | Null

type Row = Value array

type OrderByDirection =
  | Ascending
  | Descending

type Expression =
  | Function of name: string * args: Expression list * functionType: FunctionType
  | ColumnReference of index: int
  | Constant of value: Value

and FunctionType =
  | Scalar
  | Aggregate of options: AggregateOptions

and AggregateOptions =
  {
    Distinct: bool
    OrderBy: Expression list
    OrderByDirection: OrderByDirection
  }
  static member Default =
    {
      Distinct = false
      OrderBy = []
      OrderByDirection = Ascending
    }

type EvaluationContext = EmptyContext

type ScalarArgs = Value list
type AggregateArgs = seq<Value list>

module ExpressionUtils =
  open System.Linq

  let invalidOverload name =
    failwith $"Invalid overload called for function '{name}'."

  let countSeqBy fn (seq: seq<'a>) = seq.Count(fun x -> fn x)

  let mapSingleArg name (args: AggregateArgs) =
    args
    |> Seq.map (function
         | [ arg ] -> arg
         | _ -> invalidOverload name)

  let filterNulls args = args |> Seq.filter (fun v -> not (v = Null))

  let binaryFunction fn =
    fun _ctx args ->
      match args with
      | [ a; b ] -> fn (a, b)
      | _ -> failwith "Expected 2 arguments in function."

  let nullableBinaryFunction fn =
    binaryFunction (function
      | Null, _ -> Null
      | _, Null -> Null
      | a, b -> fn (a, b))

module DefaultFunctions =
  open ExpressionUtils

  let add =
    nullableBinaryFunction (function
      | Integer a, Integer b -> Integer(a + b)
      | Float a, Float b -> Float(a + b)
      | Float a, Integer b -> Float(a + float b)
      | Integer a, Float b -> Float(float a + b)
      | _ -> invalidOverload "+")

  let sub =
    nullableBinaryFunction (function
      | Integer a, Integer b -> Integer(a - b)
      | Float a, Float b -> Float(a - b)
      | Float a, Integer b -> Float(a - float b)
      | Integer a, Float b -> Float(float a - b)
      | _ -> invalidOverload "-")

  let equals =
    nullableBinaryFunction (function
      | a, b when a = b -> Boolean true
      | Integer a, Float b -> Boolean(float a = b)
      | Float a, Integer b -> Boolean(a = float b)
      | _ -> Boolean false)

  let not _ctx args =
    match args with
    | [ Boolean b ] -> Boolean(not b)
    | [ Null ] -> Null
    | _ -> invalidOverload "not"

module DefaultAggregates =
  open ExpressionUtils

  let sum ctx (values: AggregateArgs) =
    let values = values |> mapSingleArg "sum" |> filterNulls

    if Seq.isEmpty values then
      Null
    else
      values
      |> Seq.reduce (fun a b -> DefaultFunctions.add ctx [ a; b ])

  let count _ctx (values: AggregateArgs) =
    values
    |> countSeqBy (function
         | [ Null ] -> false
         | [ _ ] -> true
         | _ -> invalidOverload "count")
    |> Integer

module Expression =
  let functions =
    Map.ofList [
      "+", DefaultFunctions.add
      "-", DefaultFunctions.sub
      "=", DefaultFunctions.equals
      "not", DefaultFunctions.not
    ]

  let aggregates =
    Map.ofList [ "sum", DefaultAggregates.sum; "count", DefaultAggregates.count ]

  let invokeFunction ctx name args =
    match Map.tryFind name functions with
    | Some fn -> fn ctx args
    | None -> failwith $"Unknown function '{name}'."

  let invokeAggregate ctx name mappedArgs =
    match Map.tryFind name aggregates with
    | Some fn -> fn ctx mappedArgs
    | None -> failwith $"Unknown aggregate '{name}'."

  let rec evaluate (ctx: EvaluationContext) (row: Row) (expr: Expression) =
    match expr with
    | Function (name, args, Scalar) -> invokeFunction ctx name (args |> List.map (evaluate ctx row))
    | Function (name, _, _) -> failwith $"Invalid usage of aggregate '{name}'."
    | ColumnReference index -> row.[index]
    | Constant value -> value

  let private prepareAggregateArgs ctx args opts rows =
    let sortedRows =
      match opts.OrderBy, opts.OrderByDirection with
      | [], _ -> rows
      | [ orderByExpr ], Ascending -> rows |> Seq.sortBy (fun row -> evaluate ctx row orderByExpr)
      | [ orderByExpr ], Descending ->
          rows
          |> Seq.sortByDescending (fun row -> evaluate ctx row orderByExpr)
      | _ -> failwith "Multiple order by expressions are not supported yet."

    let projectedArgs =
      sortedRows
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
        | Function (name, args, Scalar) ->
            let evaluatedArgs =
              args |> List.map (evaluateAggregated ctx groupings rows)

            invokeFunction ctx name evaluatedArgs
        | Function (name, args, Aggregate opts) ->
            let aggregateArgs = prepareAggregateArgs ctx args opts rows
            invokeAggregate ctx name aggregateArgs
        | ColumnReference _ -> failwith $"Incorrect column reference in aggregated context."
        | Constant value -> value

  let defaultAggregate = Aggregate AggregateOptions.Default

  let distinctAggregate =
    Aggregate
      { AggregateOptions.Default with
          Distinct = true
      }
