module OpenDiffix.Core.Aggregator

type IAggregator =
  abstract Transition : Value list -> IAggregator
  abstract Final : EvaluationContext -> Value

// ----------------------------------------------------------------
// Helpers
// ----------------------------------------------------------------

let private invalidArgs (values: Value list) =
  failwith $"Invalid arguments for aggregator: {values}"

let private hashAid (aidValue: Value) = Value.hash aidValue

let private hashAidList (aidValues: Value list) = List.map hashAid aidValues

let private missingAid (aidValue: Value) =
  match aidValue with
  | Null -> true
  | Value.List [] -> true
  | _ -> false

// ----------------------------------------------------------------
// Aggregators
// ----------------------------------------------------------------

type private Count(counter) =
  new() = Count(0L)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> this
      | _ -> Count(counter + 1L)
      :> IAggregator

    member this.Final _ctx = Integer counter

type private CountDistinct(set: Set<Value>) =
  new() = CountDistinct(Set.empty)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> this
      | [ value ] -> set |> Set.add value |> CountDistinct
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final _ctx = set.Count |> int64 |> Integer

type private Sum(sum: Value) =
  new() = Sum(Null)

  interface IAggregator with
    member this.Transition args =
      match sum, args with
      | _, [ Null ] -> this
      | Null, [ value ] -> Sum(value)
      | Integer oldValue, [ Integer value ] -> (oldValue + value) |> Integer |> Sum
      | Real oldValue, [ Real value ] -> (oldValue + value) |> Real |> Sum
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final _ctx = sum

type private DiffixCount(minCount, perAidCounts: (Map<AidHash, float> * int64) list option) =
  /// Initializes (if not already initialized) per aid counts with empty maps.
  let initializeCounts aidInstances counts =
    match counts with
    | Some counts -> counts
    | None -> List.replicate (List.length aidInstances) (Map.empty, 0L)

  /// Increases contribution of a single AID value.
  let increaseContribution valueIncrease aidValue aidMap =
    Map.change
      (hashAid aidValue)
      (function
      | Some count -> Some(count + valueIncrease)
      | None -> Some(valueIncrease))
      aidMap

  /// Increases contribution of all AID instances.
  let increaseContributions valueIncrease (aidInstances: Value list) =
    perAidCounts
    |> initializeCounts aidInstances
    |> List.zip aidInstances
    |> List.map (fun (aidValue: Value, (aidMap, unaccountedFor)) ->
      match aidValue with
      // No AIDs, add to unaccounted value
      | Null
      | Value.List [] -> aidMap, unaccountedFor + valueIncrease
      // List of AIDs, distribute contribution evenly
      | Value.List aidValues ->
          let partialIncrease = (float valueIncrease) / (aidValues |> List.length |> float)

          aidValues
          |> List.fold (fun acc aidValue -> increaseContribution partialIncrease aidValue acc) aidMap,
          unaccountedFor
      // Single AID, add to its contribution
      | aidValue -> increaseContribution (float valueIncrease) aidValue aidMap, unaccountedFor
    )
    |> Some

  let updateAidMaps aidInstances valueIncrease =
    match aidInstances with
    | Value.List aidInstances when List.forall missingAid aidInstances -> perAidCounts
    | Value.List aidInstances -> increaseContributions valueIncrease aidInstances
    | _ -> failwith "Expecting a list as input"

  new(minCount) = DiffixCount(minCount, None)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ aidInstances; Null ] -> DiffixCount(minCount, updateAidMaps aidInstances 0L)
      | [ aidInstances ]
      | [ aidInstances; _ ] -> DiffixCount(minCount, updateAidMaps aidInstances 1L)
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final ctx =
      match Anonymizer.count ctx.AnonymizationParams perAidCounts with
      | Null -> Integer minCount
      | value -> value

type private DiffixCountDistinct(minCount, aidsCount, aidsPerValue: Map<Value, Set<AidHash> list>) =
  new(minCount) = DiffixCountDistinct(minCount, 0, Map.empty)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ _aidInstances; Null ] -> this
      | [ Value.List aidInstances; value ] ->
          let initialEntry =
            fun () ->
              aidInstances
              |> List.map
                   (function
                   | Null
                   | Value.List [] -> Set.empty
                   | Value.List aidValues -> aidValues |> hashAidList |> Set.ofList
                   | aidValue -> aidValue |> hashAid |> Set.singleton)
              |> Some

          let transitionEntry =
            aidInstances
            |> List.map2 (fun aidValue hashSet ->
              match aidValue with
              | Null
              | Value.List [] -> hashSet
              | Value.List aidValues -> Set.addSeq (hashAidList aidValues) hashSet
              | aidValue -> Set.add (hashAid aidValue) hashSet
            )

          DiffixCountDistinct(
            minCount,
            aidInstances.Length,
            Map.change value (Option.map transitionEntry >> Option.orElseWith initialEntry) aidsPerValue
          )
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final ctx =
      match Anonymizer.countDistinct aidsCount aidsPerValue ctx.AnonymizationParams with
      | Null -> Integer minCount
      | value -> value

type private DiffixLowCount(aidValueSets: Set<AidHash> list option) =
  new() = DiffixLowCount(None)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> this
      | [ Value.List aidInstances ] ->
          aidValueSets
          |> Option.defaultWith (fun () -> List.replicate aidInstances.Length Set.empty)
          |> List.zip aidInstances
          |> List.map (fun (aidValue: Value, aidValueSet) ->
            match aidValue with
            | Null
            | Value.List [] -> aidValueSet
            | Value.List aidValues -> Set.addSeq (hashAidList aidValues) aidValueSet
            | aidValue -> Set.add (hashAid aidValue) aidValueSet
          )
          |> Some
          |> DiffixLowCount
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final ctx =
      match aidValueSets with
      | None -> true |> Boolean
      | Some aidValueSets -> Anonymizer.isLowCount aidValueSets ctx.AnonymizationParams |> Boolean

type private MergeAids(aidValueSet: Set<Value>) =
  new() = MergeAids(Set.empty)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> this
      | [ Value.List aidValues ] -> aidValueSet |> Set.addSeq aidValues |> MergeAids
      | [ aidValue ] -> aidValueSet |> Set.add aidValue |> MergeAids
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final _ctx = aidValueSet |> Set.toList |> Value.List

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type T = IAggregator

let create ctx globalBucket fn : T =
  let minDiffixCount = if globalBucket then 0L else int64 ctx.AnonymizationParams.MinimumAllowedAids

  match fn with
  | AggregateFunction (Count, { Distinct = false }) -> Count() :> T
  | AggregateFunction (Count, { Distinct = true }) -> CountDistinct() :> T
  | AggregateFunction (Sum, { Distinct = false }) -> Sum() :> T
  | AggregateFunction (DiffixCount, { Distinct = false }) -> DiffixCount(minDiffixCount) :> T
  | AggregateFunction (DiffixCount, { Distinct = true }) -> DiffixCountDistinct(minDiffixCount) :> T
  | AggregateFunction (DiffixLowCount, _) -> DiffixLowCount() :> T
  | AggregateFunction (MergeAids, _) -> MergeAids() :> T
  | _ -> failwith "Invalid aggregator"
