namespace OpenDiffix.Core

type Value =
  | Null
  | Boolean of bool
  | Integer of int64
  | Real of float
  | String of string
  static member ToString =
    function
    | Null -> "NULL"
    | Boolean b -> b.ToString()
    | Integer i -> i.ToString()
    | Real r -> r.ToString()
    | String s -> s

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

    { new System.Collections.Generic.IComparer<Value> with
        member __.Compare(x, y) =
          match x, y with
          | Null, Null -> 0
          | Null, _ -> nullsValue
          | _, Null -> -nullsValue
          | x, y -> directionCoefficient * Operators.compare x y
    }

  let isTruthy =
    function
    | Null -> false
    | Boolean value -> value
    | Integer n -> n <> 0L
    | Real n -> n <> 0.
    | String "" -> false
    | String value -> value.ToLower() = "true"
