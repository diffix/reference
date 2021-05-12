module OpenDiffix.Core.Aggregator

type IAggregator =
  abstract Transition : Value list -> IAggregator
  abstract Final : EvaluationContext -> Value

// ----------------------------------------------------------------
// Helpers
// ----------------------------------------------------------------

let private invalidArgs (values: Value list) =
  failwith $"Invalid arguments for aggregator: {values}"

// ----------------------------------------------------------------
// Aggregators
// ----------------------------------------------------------------

type private Count(counter) =
  new() = Count(0L)

  interface IAggregator with
    member this.Transition values =
      match values with
      | [ Null ] -> this
      | _ -> Count(counter + 1L)
      :> IAggregator

    member this.Final _ctx = Integer counter

type private CountDistinct(set: Set<Value>) =
  new() = CountDistinct(Set.empty)

  interface IAggregator with
    member this.Transition values =
      match values with
      | [ Null ] -> this
      | [ value ] -> set |> Set.add value |> CountDistinct
      | _ -> invalidArgs values
      :> IAggregator

    member this.Final _ctx = set.Count |> int64 |> Integer

type private Sum(sum: Value) =
  new() = Sum(Null)

  interface IAggregator with
    member this.Transition values =
      match sum, values with
      | _, [ Null ] -> this
      | Null, [ value ] -> Sum(value)
      | Integer oldValue, [ Integer value ] -> (oldValue + value) |> Integer |> Sum
      | Real oldValue, [ Real value ] -> (oldValue + value) |> Real |> Sum
      | _ -> invalidArgs values
      :> IAggregator

    member this.Final _ctx = sum

type private DiffixCount(perAidCounts: (Map<AidHash, int64> * int64) list option) =
  new() = DiffixCount(None)

  member private this.MapAidStructure valueIncrease transition (aidValues: Value list) =
    perAidCounts
    |> Option.defaultValue (List.replicate aidValues.Length (Map.empty, 0L))
    |> List.zip aidValues
    |> List.map (fun (aidValue: Value, (aidMap, unaccountedFor)) ->
      if aidValue = Null then
        aidMap, unaccountedFor + valueIncrease
      else
        Map.change (aidValue.GetHashCode()) (Option.map transition >> Option.orElse (Some valueIncrease)) aidMap,
        unaccountedFor
    )
    |> Some

  member private this.UpdateAidMaps<'T> (aidsArray: Value) valueIncrease transition =
    match aidsArray with
    | Value.List aidValues when List.forall ((=) Null) aidValues -> perAidCounts
    | Value.List aidValues -> this.MapAidStructure valueIncrease transition aidValues
    | _ -> failwith "Expecting an AID list as input"

  interface IAggregator with
    member this.Transition values =
      match values with
      | [ aidValues; Null ] -> this.UpdateAidMaps aidValues 0L id |> DiffixCount
      | [ aidValues ]
      | [ aidValues; _ ] -> this.UpdateAidMaps aidValues 1L ((+) 1L) |> DiffixCount
      | _ -> invalidArgs values
      :> IAggregator

    member this.Final ctx =
      Anonymizer.count ctx.AnonymizationParams perAidCounts

type private DiffixCountDistinct(aidsCount, aidsPerValue: Map<Value, Set<AidHash> list>) =
  new() = DiffixCountDistinct(0, Map.empty)

  interface IAggregator with
    member this.Transition values =
      match values with
      | [ _aidValues; Null ] -> this
      | [ Value.List aidValues; value ] ->
          let initialEntry =
            fun () ->
              aidValues
              |> List.map (fun aidValue ->
                if aidValue = Null then Set.empty else aidValue.GetHashCode() |> Set.singleton
              )
              |> Some

          let transitionEntry =
            aidValues
            |> List.map2 (fun aidValue hashSet ->
              if aidValue = Null then hashSet else Set.add (aidValue.GetHashCode()) hashSet
            )

          DiffixCountDistinct(
            aidValues.Length,
            Map.change value (Option.map transitionEntry >> Option.orElseWith initialEntry) aidsPerValue
          )
      | _ -> invalidArgs values
      :> IAggregator

    member this.Final ctx =
      Anonymizer.countDistinct aidsCount aidsPerValue ctx.AnonymizationParams

type private DiffixLowCount(aidSets: Set<AidHash> list option) =
  new() = DiffixLowCount(None)

  interface IAggregator with
    member this.Transition values =
      match values with
      | [ Null ] -> this
      | [ Value.List aidValues ] ->
          aidSets
          |> Option.defaultWith (fun () -> List.replicate aidValues.Length Set.empty)
          |> List.zip aidValues
          |> List.map (fun (aidValue: Value, aidSet) ->
            if aidValue = Null then aidSet else Set.add (aidValue.GetHashCode()) aidSet
          )
          |> Some
          |> DiffixLowCount
      | _ -> invalidArgs values
      :> IAggregator

    member this.Final ctx =
      match aidSets with
      | None -> true |> Boolean
      | Some aidSets -> Anonymizer.isLowCount aidSets ctx.AnonymizationParams |> Boolean

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type T = IAggregator

let create _ctx fn : T =
  match fn with
  | AggregateFunction (Count, { Distinct = false }) -> Count() :> T
  | AggregateFunction (Count, { Distinct = true }) -> CountDistinct() :> T
  | AggregateFunction (Sum, { Distinct = false }) -> Sum() :> T
  | AggregateFunction (DiffixCount, { Distinct = false }) -> DiffixCount() :> T
  | AggregateFunction (DiffixCount, { Distinct = true }) -> DiffixCountDistinct() :> T
  | AggregateFunction (DiffixLowCount, _) -> DiffixLowCount() :> T
  | _ -> failwith "Invalid aggregator"
