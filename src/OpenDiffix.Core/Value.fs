namespace OpenDiffix.Core

type AidHash = int

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

type Row =
  {
    Values: Value array
    Junk: Map<string, Value>
  }
  member this.Item
    with get (index) = this.Values.[index]
    and set index value = this.Values.[index] <- value

  static member OfValues values = { Values = values; Junk = Map.empty }
  static member GetValues row = row.Values

  /// Junk from row2 overwrites the junk in row1 if there is a clash
  static member Append row1 row2 =
    let values = Array.append row1.Values row2.Values
    let junk = row2.Junk |> Map.fold (fun map key value -> Map.add key value map) row1.Junk
    { Values = values; Junk = junk }

  member this.SetJunk key junk = { this with Junk = Map.add key junk this.Junk }
  member this.TryGetJunk key = Map.tryFind key this.Junk


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
