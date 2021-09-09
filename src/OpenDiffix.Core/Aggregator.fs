module OpenDiffix.Core.Aggregator

open System.Collections.Generic

type IAggregator =
  abstract Transition : Value list -> unit
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

let private emptySets length =
  Array.init length (fun _ -> HashSet<AidHash>())

// ----------------------------------------------------------------
// Aggregators
// ----------------------------------------------------------------

type private Count() =
  let mutable state = 0L

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> ()
      | _ -> state <- state + 1L

    member this.Final _ctx = Integer state

type private CountDistinct() =
  let state = HashSet<Value>()

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> ()
      | [ value ] -> state.Add(value) |> ignore
      | _ -> invalidArgs args

    member this.Final _ctx = state.Count |> int64 |> Integer

type private Sum() =
  let mutable state = Null

  interface IAggregator with
    member this.Transition args =
      state <-
        match state, args with
        | _, [ Null ] -> state
        | Null, [ value ] -> value
        | Integer oldValue, [ Integer value ] -> Integer(oldValue + value)
        | Real oldValue, [ Real value ] -> Real(oldValue + value)
        | _ -> invalidArgs args

    member this.Final _ctx = state

type private DiffixCount(minCount) =
  let mutable state : Anonymizer.AidCountState array = null

  /// Increases contribution of a single AID value.
  let increaseContribution valueIncrease aidValue (aidMap: Dictionary<AidHash, float>) =
    let aidHash = hashAid aidValue

    let updatedContribution =
      match aidMap.TryGetValue(aidHash) with
      | true, aidContribution -> aidContribution + valueIncrease
      | false, _ -> valueIncrease

    aidMap.[aidHash] <- updatedContribution

  /// Increases contribution of all AID instances.
  let increaseContributions valueIncrease (aidInstances: Value list) =
    if state = null then
      state <-
        Array.init
          aidInstances.Length
          (fun _ -> { AidContributions = Dictionary<AidHash, float>(); UnaccountedFor = 0L }
          )

    aidInstances
    |> List.iteri (fun i aidValue ->
      let aidState = state.[i]
      let aidContributions = aidState.AidContributions

      match aidValue with
      // No AIDs, add to unaccounted value
      | Null
      | Value.List [] -> aidState.UnaccountedFor <- aidState.UnaccountedFor + valueIncrease
      // List of AIDs, distribute contribution evenly
      | Value.List aidValues ->
          let partialIncrease = (float valueIncrease) / (aidValues |> List.length |> float)

          aidValues
          |> List.iter (fun aidValue -> increaseContribution partialIncrease aidValue aidContributions)
      // Single AID, add to its contribution
      | aidValue -> increaseContribution (float valueIncrease) aidValue aidContributions
    )

  let updateAidMaps aidInstances valueIncrease =
    match aidInstances with
    | Value.List aidInstances when List.forall missingAid aidInstances -> ()
    | Value.List aidInstances -> increaseContributions valueIncrease aidInstances
    | _ -> failwith "Expecting a list as input"

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ aidInstances; Null ] -> updateAidMaps aidInstances 0L
      | [ aidInstances ]
      | [ aidInstances; _ ] -> updateAidMaps aidInstances 1L
      | _ -> invalidArgs args

    member this.Final ctx =
      if state = null then
        Integer minCount
      else
        match Anonymizer.count ctx.AnonymizationParams state with
        | Null -> Integer minCount
        | Integer value -> Integer(max value minCount)
        | value -> value

type private DiffixCountDistinct(minCount) =
  let mutable aidsCount = 0
  let aidsPerValue = Dictionary<Value, HashSet<AidHash> array>()

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ _aidInstances; Null ] -> ()
      | [ Value.List aidInstances; value ] ->
          let aidSets =
            match aidsPerValue.TryGetValue(value) with
            | true, aidSets -> aidSets
            | false, _ ->
                if aidsCount = 0 then aidsCount <- aidInstances.Length
                let aidSets = emptySets aidsCount
                aidsPerValue.[value] <- aidSets
                aidSets

          aidInstances
          |> List.iteri (fun i aidValue ->
            match aidValue with
            | Null
            | Value.List [] -> ()
            | Value.List aidValues -> aidSets.[i].UnionWith(hashAidList aidValues)
            | aidValue -> aidSets.[i].Add(hashAid aidValue) |> ignore
          )
      | _ -> invalidArgs args

    member this.Final ctx =
      match Anonymizer.countDistinct aidsCount aidsPerValue ctx.AnonymizationParams with
      | Null -> Integer minCount
      | Integer value -> Integer(max value minCount)
      | value -> value

type private DiffixLowCount() =
  let mutable state : HashSet<AidHash> [] = null

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> ()
      | [ Value.List aidInstances ] ->
          if state = null then state <- emptySets aidInstances.Length

          aidInstances
          |> List.iteri (fun i aidValue ->
            match aidValue with
            | Null
            | Value.List [] -> ()
            | Value.List aidValues -> state.[i].UnionWith(hashAidList aidValues)
            | aidValue -> state.[i].Add(hashAid aidValue) |> ignore
          )

      | _ -> invalidArgs args

    member this.Final ctx =
      if state = null then
        Boolean true
      else
        Anonymizer.isLowCount state ctx.AnonymizationParams |> Boolean

type private MergeAids() =
  let state = HashSet<Value>()

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> ()
      | [ Value.List aidValues ] -> state.UnionWith(aidValues)
      | [ aidValue ] -> state.Add(aidValue) |> ignore
      | _ -> invalidArgs args

    member this.Final _ctx = state |> Seq.toList |> Value.List

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type T = IAggregator

let create ctx globalBucket fn : T =
  let minDiffixCount =
    if globalBucket then
      0L
    else
      int64 ctx.AnonymizationParams.Suppression.LowThreshold

  match fn with
  | AggregateFunction (Count, { Distinct = false }) -> Count() :> T
  | AggregateFunction (Count, { Distinct = true }) -> CountDistinct() :> T
  | AggregateFunction (Sum, { Distinct = false }) -> Sum() :> T
  | AggregateFunction (DiffixCount, { Distinct = false }) -> DiffixCount(minDiffixCount) :> T
  | AggregateFunction (DiffixCount, { Distinct = true }) -> DiffixCountDistinct(minDiffixCount) :> T
  | AggregateFunction (DiffixLowCount, _) -> DiffixLowCount() :> T
  | AggregateFunction (MergeAids, _) -> MergeAids() :> T
  | _ -> failwith "Invalid aggregator"
