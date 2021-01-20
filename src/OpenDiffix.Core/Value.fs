namespace OpenDiffix.Core

type Value =
  | Null
  | Boolean of bool
  | Integer of int
  | Float of float
  | String of string

  static member IsTruthy =
    function
    | Null -> false
    | Boolean value -> value
    | Integer n -> n <> 0
    | Float n -> n <> 0.
    | String "" -> false
    | String value -> value.ToLower() = "true"

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
