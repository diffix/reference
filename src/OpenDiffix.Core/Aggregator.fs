module OpenDiffix.Core.Aggregator

// ----------------------------------------------------------------
// Helpers
// ----------------------------------------------------------------

let private castAggregator<'TAgg when 'TAgg :> IAggregator> (agg: IAggregator) =
  match agg with
  | :? 'TAgg as tAgg -> tAgg
  | _ -> failwith "Cannot merge incompatible aggregators."

let mergeHashSetsInto (destination: HashSet<_> array) (source: HashSet<_> array) =
  source |> Array.iteri (fun i -> destination.[i].UnionWith)

let cloneHashSet (hashSet: HashSet<'T>) = HashSet<'T>(hashSet, hashSet.Comparer)

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

  member this.State = state

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> ()
      | _ -> state <- state + 1L

    member this.Merge aggregator =
      state <- state + (castAggregator<Count> aggregator).State

    member this.Final _ctx = Integer state

type private CountDistinct() =
  let state = HashSet<Value>()

  member this.State = state

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> ()
      | [ value ] -> state.Add(value) |> ignore
      | _ -> invalidArgs args

    member this.Merge aggregator =
      state.UnionWith((castAggregator<CountDistinct> aggregator).State)

    member this.Final _ctx = state.Count |> int64 |> Integer

type private Sum() =
  let mutable state = Null

  member this.State = state

  interface IAggregator with
    member this.Transition args =
      state <-
        match state, args with
        | _, [ Null ] -> state
        | Null, [ value ] -> value
        | Integer oldValue, [ Integer value ] -> Integer(oldValue + value)
        | Real oldValue, [ Real value ] -> Real(oldValue + value)
        | _ -> invalidArgs args

    member this.Merge aggregator =
      (this :> IAggregator).Transition [ (castAggregator<Sum> aggregator).State ]

    member this.Final _ctx = state

type private DiffixCount(minCount) =
  let mutable state: Anonymizer.AidCountState array = null

  let initialState length : Anonymizer.AidCountState array =
    Array.init length (fun _ -> { AidContributions = Dictionary<AidHash, float>(); UnaccountedFor = 0L })

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
    if isNull state then state <- initialState aidInstances.Length

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

  member this.State = state

  interface IAggregator with
    member this.Transition args =
      match args with
      | Value.List [] :: _ -> invalidArgs args
      | [ aidInstances; Null ] -> updateAidMaps aidInstances 0L
      | [ aidInstances ]
      | [ aidInstances; _ ] -> updateAidMaps aidInstances 1L
      | _ -> invalidArgs args

    member this.Merge aggregator =
      let otherState = (castAggregator<DiffixCount> aggregator).State

      if otherState <> null then
        if isNull state then state <- initialState otherState.Length

        otherState
        |> Array.iteri (fun i otherAidCountState ->
          let aidCountState = state.[i]
          let aidContributions = aidCountState.AidContributions
          aidCountState.UnaccountedFor <- aidCountState.UnaccountedFor + otherAidCountState.UnaccountedFor

          otherAidCountState.AidContributions
          |> Seq.iter (fun pair ->
            aidContributions.[pair.Key] <- (aidContributions |> Dictionary.getOrDefault pair.Key 0.0) + pair.Value
          )
        )

    member this.Final executionContext =
      if isNull state then
        Integer minCount
      else
        match Anonymizer.count executionContext state with
        | Null -> Integer minCount
        | Integer value -> Integer(max value minCount)
        | value -> value

type private DiffixCountDistinct(minCount) =
  let mutable aidsCount = 0
  let aidsPerValue = Dictionary<Value, HashSet<AidHash> array>()

  member this.AidsCount = aidsCount

  member this.State = aidsPerValue

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ _aidInstances; Null ] -> ()
      | [ Value.List aidInstances; value ] when not aidInstances.IsEmpty ->
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

    member this.Merge aggregator =
      let other = (castAggregator<DiffixCountDistinct> aggregator)
      let otherAidsPerValue = other.State

      if aidsCount = 0 then aidsCount <- other.AidsCount

      otherAidsPerValue
      |> Seq.iter (fun pair ->
        match aidsPerValue.TryGetValue(pair.Key) with
        | true, aidSets -> pair.Value |> mergeHashSetsInto aidSets
        | false, _ -> aidsPerValue.[pair.Key] <- pair.Value |> Array.map cloneHashSet
      )

    member this.Final executionContext =
      match Anonymizer.countDistinct executionContext aidsCount aidsPerValue with
      | Null -> Integer minCount
      | Integer value -> Integer(max value minCount)
      | value -> value

type private DiffixLowCount() =
  let mutable state: HashSet<AidHash> [] = null

  member this.State = state

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> ()
      | [ Value.List aidInstances ] when not aidInstances.IsEmpty ->
        if isNull state then state <- emptySets aidInstances.Length

        aidInstances
        |> List.iteri (fun i aidValue ->
          match aidValue with
          | Null
          | Value.List [] -> ()
          | Value.List aidValues -> state.[i].UnionWith(hashAidList aidValues)
          | aidValue -> state.[i].Add(hashAid aidValue) |> ignore
        )

      | _ -> invalidArgs args

    member this.Merge aggregator =
      let otherState = (castAggregator<DiffixLowCount> aggregator).State

      if otherState <> null then
        if state = null then
          state <- otherState |> Array.map cloneHashSet
        else
          otherState |> mergeHashSetsInto state

    member this.Final executionContext =
      if isNull state then
        Boolean true
      else
        Anonymizer.isLowCount executionContext state |> Boolean

type private MergeAids() =
  let state = HashSet<Value>()

  member this.State = state

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> ()
      | [ Value.List aidValues ] -> state.UnionWith(aidValues)
      | [ aidValue ] -> state.Add(aidValue) |> ignore
      | _ -> invalidArgs args

    member this.Merge aggregator =
      state.UnionWith((castAggregator<MergeAids> aggregator).State)

    member this.Final _ctx = state |> Seq.toList |> Value.List

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type T = IAggregator

let create (executionContext: ExecutionContext) globalBucket fn : T =
  let minDiffixCount =
    if globalBucket then
      0L
    else
      int64 executionContext.AnonymizationParams.Suppression.LowThreshold

  match fn with
  | AggregateFunction (Count, { Distinct = false }) -> Count() :> T
  | AggregateFunction (Count, { Distinct = true }) -> CountDistinct() :> T
  | AggregateFunction (Sum, { Distinct = false }) -> Sum() :> T
  | AggregateFunction (DiffixCount, { Distinct = false }) -> DiffixCount(minDiffixCount) :> T
  | AggregateFunction (DiffixCount, { Distinct = true }) -> DiffixCountDistinct(minDiffixCount) :> T
  | AggregateFunction (DiffixLowCount, _) -> DiffixLowCount() :> T
  | AggregateFunction (MergeAids, _) -> MergeAids() :> T
  | _ -> failwith "Invalid aggregator"
