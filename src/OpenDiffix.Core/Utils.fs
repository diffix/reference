[<AutoOpen>]
module OpenDiffix.Core.Utils

open System

// Aliases for common mutable collections
type MutableList<'T> = Collections.Generic.List<'T>
type Dictionary<'K, 'V> = Collections.Generic.Dictionary<'K, 'V>
type KeyValuePair<'K, 'V> = Collections.Generic.KeyValuePair<'K, 'V>
type HashSet<'T> = Collections.Generic.HashSet<'T>
type Stack<'T> = Collections.Generic.Stack<'T>

module String =
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

module Dictionary =
  let getOrDefault key defaultValue (dict: Dictionary<'K, 'V>) =
    match dict.TryGetValue(key) with
    | true, value -> value
    | false, _ -> defaultValue

  let getOrInit key initFn (dict: Dictionary<'K, 'V>) =
    match dict.TryGetValue(key) with
    | true, value -> value
    | false, _ ->
      let value = initFn ()
      dict.[key] <- value
      value

  let increment key (dict: Dictionary<'K, int>) =
    dict.[key] <-
      match dict.TryGetValue(key) with
      | true, count -> count + 1
      | false, _ -> 1

type Hash = uint64

module Hash =
  let bytes (data: byte []) : Hash =
    // Implementation of FNV-1a hash algorithm: http://www.isthe.com/chongo/tech/comp/fnv/index.html
    let fnvPrime = 1099511628211UL
    let offsetBasis = 14695981039346656037UL

    let mutable hash = offsetBasis

    for octet in data do
      hash <- hash ^^^ uint64 octet
      hash <- hash * fnvPrime

    hash

  let string (data: string) =
    data |> Text.Encoding.UTF8.GetBytes |> bytes

  let strings start data =
    data |> Seq.distinct |> Seq.map string |> Seq.fold (^^^) start
