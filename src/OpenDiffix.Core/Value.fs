module OpenDiffix.Core.Value

open System

/// Converts a value to its string representation.
let rec toString value =
  match value with
  | Null -> "NULL"
  | Boolean b -> b.ToString()
  | Integer i -> i.ToString()
  | Real r -> r.ToString()
  | String s -> s
  | List values -> values |> List.map toString |> String.join ","

/// Resolves the type of a value.
let rec typeOf value =
  match value with
  | Null -> NULL_TYPE
  | Boolean _ -> BooleanType
  | Integer _ -> IntegerType
  | Real _ -> RealType
  | String _ -> StringType
  | List values -> values |> List.map typeOf |> ExpressionType.commonType

/// Attempts to convert a value to a boolean.
let unwrapBoolean value =
  match value with
  | Null -> false
  | Boolean value -> value
  | _ -> failwith "Expecting boolean value or null."

/// Returns a value comparer with given direction and nulls behavior.
let comparer direction nulls =
  let directionValue =
    match direction with
    | Ascending -> 1
    | Descending -> -1

  let nullsValue =
    match nulls with
    | NullsFirst -> -1
    | NullsLast -> 1

  fun a b ->
    match a, b with
    | Null, Null -> 0
    | Null, _ -> nullsValue
    | _, Null -> -nullsValue
    | x, y -> directionValue * Operators.compare x y

/// Computes a 32 bit hash of the given value.
let hash value =
  match value with
  | Null -> 0
  | Boolean false -> 0
  | Boolean true -> 1
  | Integer i -> i |> BitConverter.GetBytes |> Hash.bytes
  | Real r -> r |> BitConverter.GetBytes |> Hash.bytes
  | String s -> s |> Text.Encoding.UTF8.GetBytes |> Hash.bytes
  | List l -> l |> List.map hash |> List.fold (^^^) 0
