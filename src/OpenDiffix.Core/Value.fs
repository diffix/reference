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

type JunkType = | UserCount

type FunctionReturnValue =
  | OnlyValue of Value
  | ValueAndJunk of value: Value * junkType: JunkType * junkValue: Value

  static member Value =
    function
    | OnlyValue value
    | ValueAndJunk (value, _, _) -> value

type Row =
  {
    Values: Value array
    Junk: Map<JunkType, Value>
  }

  static member private ExtractValues(values: Value array) = values

  static member private ExtractValues(values: FunctionReturnValue array) =
    values
    |> Array.map
         (function
         | OnlyValue value
         | ValueAndJunk (value, _, _) -> value)

  static member private UpdateJunk(_values: Value array, existingJunk) = existingJunk

  static member private UpdateJunk(values: FunctionReturnValue array, existingJunk) =
    values
    |> Array.choose
         (function
         | OnlyValue _ -> None
         | ValueAndJunk (_value, junkName, junkValue) -> Some(junkName, junkValue))
    |> Array.fold (fun junkMap (name, value) -> Map.add name value junkMap) existingJunk

  static member Empty = { Values = [||]; Junk = Map.empty }
  static member OfValues(values: Value array) = { Values = values; Junk = Map.empty }

  static member OfValues(values: FunctionReturnValue array) =
    { Values = Row.ExtractValues values; Junk = Row.UpdateJunk(values, Map.empty) }

  static member OfValuesRetainingJunk previousRow values = { previousRow with Values = values }
  static member GetValues row = row.Values

  /// Junk from row2 overwrites the junk in row1 if there is a clash
  static member Append row1 row2 =
    let values = Array.append row1.Values row2.Values
    let junk = row2.Junk |> Map.fold (fun map key value -> Map.add key value map) row1.Junk
    { Values = values; Junk = junk }

  member this.SetJunk key junk = { this with Junk = Map.add key junk this.Junk }
  member this.GetJunk key = Map.find key this.Junk

  member this.UpdateValues(values: FunctionReturnValue array) =
    { this with
        Values = Row.ExtractValues(values)
        Junk = Row.UpdateJunk(values, this.Junk)
    }

  member this.UpdateValues(values: Value array) =
    { this with
        Values = Row.ExtractValues(values)
        Junk = Row.UpdateJunk(values, this.Junk)
    }

  member this.Item
    with get (index) = this.Values.[index]
    and set index value = this.Values.[index] <- value


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
