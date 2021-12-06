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

type BloomFilter() =
  let bytes: byte array = Array.zeroCreate 8192

  let putHash hash16 =
    let byteIndex = hash16 / 8
    let bitIndex = hash16 % 8
    bytes.[byteIndex] <- bytes.[byteIndex] ||| (1uy <<< bitIndex)

  let checkHash hash16 =
    let byteIndex = hash16 / 8
    let bitIndex = hash16 % 8
    (bytes.[byteIndex] &&& (1uy <<< bitIndex)) <> 0uy

  let sliceAt offset (hash: Hash) = int ((hash >>> offset) &&& 0xFFUL)

  member this.PutHash(hash: Hash) =
    hash |> sliceAt 0 |> putHash
    hash |> sliceAt 16 |> putHash
    hash |> sliceAt 32 |> putHash
    hash |> sliceAt 48 |> putHash

  member this.CheckHash(hash: Hash) =
    (hash |> sliceAt 0 |> checkHash)
    && (hash |> sliceAt 16 |> checkHash)
    && (hash |> sliceAt 32 |> checkHash)
    && (hash |> sliceAt 48 |> checkHash)
