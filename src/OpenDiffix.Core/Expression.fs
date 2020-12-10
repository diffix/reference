namespace OpenDiffix.Core

open System
open System.Collections.Generic

type Value =
  | StringValue of string
  | IntegerValue of int
  | FloatValue of float
  | BooleanValue of bool
  | NullValue

type Tuple = Map<string, Value>

type Expression =
  | FunctionCall of name: string * args: Expression list
  | DistinctFunctionCall of name: string * arg: Expression list
  | ColumnReference of name: string
  | Constant of value: Value

type EvaluationContext = EmptyContext

module DefaultFunctions =
  let private invalidOverload name =
    failwith $"Invalid overload called for function '{name}'."

  let add _ctx args =
    match args with
    | [ IntegerValue a; IntegerValue b ] -> IntegerValue(a + b)
    | [ FloatValue a; FloatValue b ] -> FloatValue(a + b)
    | [ FloatValue a; IntegerValue b ] -> FloatValue(a + float b)
    | [ IntegerValue a; FloatValue b ] -> FloatValue(float a + b)
    | _ -> invalidOverload "+"

  let sub _ctx args =
    match args with
    | [ IntegerValue a; IntegerValue b ] -> IntegerValue(a - b)
    | [ FloatValue a; FloatValue b ] -> FloatValue(a - b)
    | [ FloatValue a; IntegerValue b ] -> FloatValue(a - float b)
    | [ IntegerValue a; FloatValue b ] -> FloatValue(float a - b)
    | _ -> invalidOverload "-"

module DefaultAggregators =
  open System.Linq

  let sum ctx values =
    if Seq.isEmpty values then
      NullValue
    else
      values
      |> Seq.reduce (fun a b -> DefaultFunctions.add ctx [ a; b ])

  let count _ctx (values: seq<Value>) =
    values.Count(fun x ->
      match x with
      | NullValue -> false
      | _ -> true)
    |> IntegerValue

module Expression =
  let functions =
    Dictionary<string, EvaluationContext -> Value list -> Value>(StringComparer.OrdinalIgnoreCase)

  functions.Add("+", DefaultFunctions.add)
  functions.Add("-", DefaultFunctions.sub)

  let aggregators =
    Dictionary<string, EvaluationContext -> seq<Value> -> Value>(StringComparer.OrdinalIgnoreCase)

  aggregators.Add("sum", DefaultAggregators.sum)
  aggregators.Add("count", DefaultAggregators.count)

  let invokeFunction ctx name args =
    match functions.TryGetValue name with
    | true, fn -> fn ctx args
    | _ -> failwith $"Unknown function {name}."

  let invokeAggregator ctx name values =
    match aggregators.TryGetValue name with
    | true, fn -> fn ctx values
    | _ -> failwith $"Unknown aggregator {name}."

  let rec evaluate (ctx: EvaluationContext) (expr: Expression) (tuple: Tuple) =
    match expr with
    | FunctionCall (name, args) -> invokeFunction ctx name (args |> List.map (fun arg -> evaluate ctx arg tuple))
    | DistinctFunctionCall (name, _) -> failwith $"Invalid usage of distinct aggregator '{name}'."
    | ColumnReference name -> tuple.[name]
    | Constant value -> value

  let rec evaluateAggregated (ctx: EvaluationContext)
                             (expr: Expression)
                             (groupings: Map<Expression, Value>)
                             (tuples: seq<Tuple>)
                             =
    match Map.tryFind expr groupings with
    | Some value -> value
    | None ->
        match expr with
        // Regular function
        | FunctionCall (name, args) when functions.ContainsKey(name) ->
            let evaluatedArgs =
              args
              |> List.map (fun arg -> evaluateAggregated ctx arg groupings tuples)

            invokeFunction ctx name evaluatedArgs
        // Non-distinct aggregate
        | FunctionCall (name, [ arg ]) ->
            let mappedTuples = tuples |> Seq.map (evaluate ctx arg)
            invokeAggregator ctx name mappedTuples
        | FunctionCall _ -> failwith "Aggregators accept only one argument."
        // Distinct aggregate
        | DistinctFunctionCall (name, [ arg ]) ->
            let mappedTuples =
              tuples |> Seq.map (evaluate ctx arg) |> Seq.distinct

            invokeAggregator ctx name mappedTuples
        | DistinctFunctionCall _ -> failwith "Aggregators accept only one argument."
        | ColumnReference name -> failwith $"Value '{name}' is not found in aggregated context."
        | Constant value -> value

  let makeTuple (data: list<string * Value>): Tuple = Map.ofList data
