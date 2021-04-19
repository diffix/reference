namespace OpenDiffix.Core

open System

type AidHash = int

type Value =
  | Null
  | Boolean of bool
  | Integer of int64
  | Real of float
  | String of string
  | List of Value list

  static member ToString =
    function
    | Null -> "NULL"
    | Boolean b -> b.ToString()
    | Integer i -> i.ToString()
    | Real r -> r.ToString()
    | String s -> s
    | List values -> values |> List.map Value.ToString |> fun values -> String.Join(",", values)

type Row = Value array

type ValueType =
  | BooleanType
  | IntegerType
  | RealType
  | StringType
  | ListType of ValueType
  | UnknownType of string

type OrderByDirection =
  | Ascending
  | Descending

type OrderByNullsBehavior =
  | NullsFirst
  | NullsLast

module Value =
  let comparer direction nulls =
    let directionCoefficient =
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
      | x, y -> directionCoefficient * Operators.compare x y

  let unwrapBoolean =
    function
    | Null -> false
    | Boolean value -> value
    | _ -> failwith "Expecting boolean value or null."
