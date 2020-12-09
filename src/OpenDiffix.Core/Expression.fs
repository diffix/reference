namespace OpenDiffix.Core

open System.Collections.Generic

type Value =
  | StringValue of string
  | IntegerValue of int
  | FloatValue of float
  | BooleanValue of bool

type Tuple = Map<string, Value>

type Expression =
  | FunctionCall of name: string * args: Expression list
  | DistinctFunctionCall of name: string * arg: Expression list
  | TupleElement of name: string
  | Constant of value: Value

module Expression =
  let functions = Dictionary<string, Value list -> Value>()
  let aggregators = Dictionary<string, seq<Value> -> Value>()

  let invokeFunction name args =
    match functions.TryGetValue name with
    | true, fn -> fn args
    | _ -> failwith $"Unknown function {name}."

  let invokeAggregator name values =
    match aggregators.TryGetValue name with
    | true, fn -> fn values
    | _ -> failwith $"Unknown aggregator {name}."

  let rec evaluate (expr: Expression) (tuple: Tuple) =
    match expr with
    | FunctionCall (name, args) -> invokeFunction name (args |> List.map (fun arg -> evaluate arg tuple))
    | DistinctFunctionCall (name, _) -> failwith $"Invalid usage of distinct aggregator '{name}'."
    | TupleElement name -> tuple.[name]
    | Constant value -> value

  let rec evaluateAggregated (expr: Expression) (groupings: Map<Expression, Value>) (tuples: seq<Tuple>) =
    match Map.tryFind expr groupings with
    | Some value -> value
    | None ->
        match expr with
        // Regular function
        | FunctionCall (name, args) when functions.ContainsKey(name) ->
            let evaluatedArgs =
              args
              |> List.map (fun arg -> evaluateAggregated arg groupings tuples)

            invokeFunction name evaluatedArgs
        // Non-distinct aggregate
        | FunctionCall (name, [ arg ]) ->
            let mappedTuples = tuples |> Seq.map (evaluate arg)
            invokeAggregator name mappedTuples
        | FunctionCall _ -> failwith "Aggregators accept only one argument."
        // Distinct aggregate
        | DistinctFunctionCall (name, [ arg ]) ->
            let mappedTuples = tuples |> Seq.map (evaluate arg) |> Seq.distinct
            invokeAggregator name mappedTuples
        | DistinctFunctionCall _ -> failwith "Aggregators accept only one argument."
        | TupleElement name -> failwith $"Value '{name}' is not found in aggregated context."
        | Constant value -> value
