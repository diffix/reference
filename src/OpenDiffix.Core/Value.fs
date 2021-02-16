namespace OpenDiffix.Core

type AidHash = int

type Value =
  | Null
  | Boolean of bool
  | Integer of int64
  | Real of float
  | String of string

type Row = Value array

type ValueType =
  | BooleanType
  | IntegerType
  | RealType
  | StringType
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
