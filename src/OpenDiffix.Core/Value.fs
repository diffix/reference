module OpenDiffix.Core.Value

open System
open System.Globalization

/// Sort-related constants
let private stringCompareInfo = CompareInfo.GetCompareInfo("en-US")
let private ignoreSymbolsFlag = CompareOptions.IgnoreSymbols + CompareOptions.StringSort
let private stringSortFlag = CompareOptions.StringSort

/// Converts a value to its string representation.
let rec toString value =
  match value with
  | Null -> "NULL"
  | Boolean true -> "t"
  | Boolean false -> "f"
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
    | String x, String y ->
      // Using PostgreSQL string comparison as a template.
      // https://wiki.postgresql.org/wiki/FAQ#Why_do_my_strings_sort_incorrectly.3F

      // We want whitespace & punctuation comparison ("symbols" in .NET) to have smaller priority,
      // so we ignore them first.
      let comparisonIgnoreSymbols = directionValue * stringCompareInfo.Compare(x, y, ignoreSymbolsFlag)
      // If the former gives a tie, we include symbols.
      if (comparisonIgnoreSymbols <> 0) then
        comparisonIgnoreSymbols
      else
        // `StringSort` means symbols come last and we group letter cases together.
        directionValue * stringCompareInfo.Compare(x, y, stringSortFlag)
    | x, y -> directionValue * Operators.compare x y
