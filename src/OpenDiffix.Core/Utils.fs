[<AutoOpen>]
module OpenDiffix.Core.Utils

let NULL_TYPE = UnknownType "null_type"
let MISSING_TYPE = UnknownType "missing_type"
let MIXED_TYPE = UnknownType "mixed_type"

module ExpressionType =
  /// Resolves the common type from a list of types.
  let commonType types =
    types
    |> List.distinct
    |> function
    | [] -> MISSING_TYPE
    | [ t ] -> t
    | _ -> MIXED_TYPE

module EvaluationContext =
  let make anonParams dataProvider =
    { AnonymizationParams = anonParams; DataProvider = dataProvider }

module String =
  open System

  let join (sep: string) (values: seq<'T>) = String.Join<'T>(sep, values)

  let equalsI s1 s2 =
    String.Equals(s1, s2, StringComparison.InvariantCultureIgnoreCase)

  let toLower (s: string) = s.ToLower()

module Set =
  let addSeq items set =
    items |> Seq.fold (fun acc item -> Set.add item acc) set

module Result =
  let value (result: Result<'T, string>) : 'T =
    match result with
    | Ok result -> result
    | Error err -> failwith err

module Function =
  let fromString name =
    match name with
    | "count" -> AggregateFunction(Count, AggregateOptions.Default)
    | "sum" -> AggregateFunction(Sum, AggregateOptions.Default)
    | "diffix_count" -> AggregateFunction(DiffixCount, AggregateOptions.Default)
    | "+" -> ScalarFunction Add
    | "-" -> ScalarFunction Subtract
    | "*" -> ScalarFunction Multiply
    | "/" -> ScalarFunction Divide
    | "%" -> ScalarFunction Modulo
    | "round" -> ScalarFunction Round
    | "ceil" -> ScalarFunction Ceil
    | "floor" -> ScalarFunction Floor
    | "abs" -> ScalarFunction Abs
    | "length" -> ScalarFunction Length
    | "lower" -> ScalarFunction Lower
    | "upper" -> ScalarFunction Upper
    | "substring" -> ScalarFunction Substring
    | "||" -> ScalarFunction Concat
    | "width_bucket" -> ScalarFunction WidthBucket
    | "cast" -> ScalarFunction Cast
    | other -> failwith $"Unknown function `{other}`"

module Schema =
  /// Finds a table by name in the schema.
  let tryFindTable schema tableName =
    schema |> List.tryFind (fun table -> String.equalsI table.Name tableName)

  /// Finds a table by name in the schema. Fails if table not found.
  let findTable schema tableName =
    tableName
    |> tryFindTable schema
    |> function
    | Some table -> table
    | None -> failwith $"Could not find table `{tableName}`."

module Table =
  /// Finds a column along with its index. The index is zero-based.
  let tryFindColumn table columnName =
    table.Columns
    |> List.indexed
    |> List.tryFind (fun (_index, column) -> String.equalsI column.Name columnName)

  /// Finds a column along with its index. The index is zero-based. Fails if column not found.
  let findColumn table columnName =
    columnName
    |> tryFindColumn table
    |> function
    | Some column -> column
    | None -> failwith $"Could not find column `{columnName}` in table `{table.Name}`"

module AnonymizationParams =
  /// Returns whether the given column in the table is an AID column.
  let isAidColumn anonParams tableName columnName =
    anonParams.TableSettings
    |> Map.tryFind tableName
    |> function
    | Some tableSettings -> tableSettings.AidColumns |> List.exists (String.equalsI columnName)
    | None -> false

module Hash =
  let bytes (data: byte []) =
    // Implementation of FNV-1a hash algorithm: http://www.isthe.com/chongo/tech/comp/fnv/index.html
    let fnvPrime = 16777619u
    let offsetBasis = 2166136261u

    let mutable hash = offsetBasis

    for octet in data do
      hash <- hash ^^^ uint32 octet
      hash <- hash * fnvPrime

    int32 hash
