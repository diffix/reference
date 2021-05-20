module OpenDiffix.Core.Aggregator

type IAggregator =
  abstract Transition : Value list -> IAggregator
  abstract Final : EvaluationContext -> Value

// ----------------------------------------------------------------
// Helpers
// ----------------------------------------------------------------

let private invalidArgs (values: Value list) =
  failwith $"Invalid arguments for aggregator: {values}"

let private hashAid (aidValue: Value) = aidValue.GetHashCode()

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

type private DiffixCount(perAidCounts: (Map<AidHash, int64> * int64) list option) =
  let mapAidStructure valueIncrease transition (aidValues: Value list) =
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

  let updateAidMaps aidsArray valueIncrease transition =
    match aidsArray with
    | Value.List aidValues when List.forall ((=) Null) aidValues -> perAidCounts
    | Value.List aidValues -> mapAidStructure valueIncrease transition aidValues
    | _ -> failwith "Expecting an AID list as input"

  new() = DiffixCount(None)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ aidInstances; Null ] -> updateAidMaps aidInstances 0L id |> DiffixCount
      | [ aidInstances ]
      | [ aidInstances; _ ] -> updateAidMaps aidInstances 1L ((+) 1L) |> DiffixCount
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final ctx =
      Anonymizer.count ctx.AnonymizationParams perAidCounts

type private DiffixCountDistinct(aidsCount, aidsPerValue: Map<Value, Set<AidHash> list>) =
  new() = DiffixCountDistinct(0, Map.empty)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ _aidInstances; Null ] -> this
      | [ Value.List aidInstances; value ] ->
          let initialEntry =
            fun () ->
              aidInstances
              |> List.map (fun aidValue ->
                if aidValue = Null then Set.empty else aidValue.GetHashCode() |> Set.singleton
              )
              |> Some

          let transitionEntry =
            aidInstances
            |> List.map2 (fun aidValue hashSet ->
              if aidValue = Null then hashSet else Set.add (aidValue.GetHashCode()) hashSet
            )

          DiffixCountDistinct(
            aidInstances.Length,
            Map.change value (Option.map transitionEntry >> Option.orElseWith initialEntry) aidsPerValue
          )
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final ctx =
      Anonymizer.countDistinct aidsCount aidsPerValue ctx.AnonymizationParams

type private DiffixLowCount(aidSets: Set<AidHash> list option) =
  new() = DiffixLowCount(None)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> this
      | [ Value.List aidInstances ] ->
          aidSets
          |> Option.defaultWith (fun () -> List.replicate aidInstances.Length Set.empty)
          |> List.zip aidInstances
          |> List.map (fun (aidValue: Value, aidSet) ->
            match aidValue with
            | Null -> aidSet
            | Value.List aidValues ->
                let aidHashes = aidValues |> List.map hashAid
                Set.addRange aidHashes aidSet
            | aidValue -> Set.add (aidValue.GetHashCode()) aidSet
          )
          |> Some
          |> DiffixLowCount
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final ctx =
      match aidSets with
      | None -> true |> Boolean
      | Some aidSets -> Anonymizer.isLowCount aidSets ctx.AnonymizationParams |> Boolean

type private MergeAids(aidSet: Set<Value>) =
  new() = MergeAids(Set.empty)

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> this
      | [ Value.List aidValues ] -> aidSet |> Set.addRange aidValues |> MergeAids
      | [ aidValue ] -> aidSet |> Set.add aidValue |> MergeAids
      | _ -> invalidArgs args
      :> IAggregator

    member this.Final _ctx = aidSet |> Set.toList |> Value.List

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
  | AggregateFunction (MergeAids, _) -> MergeAids() :> T
  | _ -> failwith "Invalid aggregator"
