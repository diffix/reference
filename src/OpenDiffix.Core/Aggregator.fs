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

let private hashAid (aidValue: Value) =
  match aidValue with
  | Integer i -> i |> System.BitConverter.GetBytes |> Hash.bytes
  | String s -> Hash.string s
  | _ -> failwith "Unsupported AID type."

let private hashAidList (aidValues: Value list) = List.map hashAid aidValues

let private missingAid (aidValue: Value) =
  match aidValue with
  | Null -> true
  | Value.List [] -> true
  | _ -> false

let private emptySets length =
  Array.init length (fun _ -> HashSet<AidHash>())

let private unwrapAnonContext anonymizationContext =
  match anonymizationContext with
  | Some anonymizationContext -> anonymizationContext
  | None -> failwith "Anonymizing aggregator called with empty anonymization context."

/// Increases contribution of a single AID value.
let private increaseContribution valueIncrease aidValue (aidMap: Dictionary<AidHash, float>) =
  let aidHash = hashAid aidValue

  let updatedContribution =
    match aidMap.TryGetValue(aidHash) with
    | true, aidContribution -> aidContribution + valueIncrease
    | false, _ -> valueIncrease

  aidMap.[aidHash] <- updatedContribution

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

    member this.Final(_aggContext, _anonContext) = Integer state

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

    member this.Final(_aggContext, _anonContext) = state.Count |> int64 |> Integer

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

    member this.Final(_aggContext, _anonContext) = state

type private DiffixCount() =
  let mutable state: Anonymizer.AidCountState array = null

  let initialState length : Anonymizer.AidCountState array =
    Array.init length (fun _ -> { AidContributions = Dictionary<AidHash, float>(); UnaccountedFor = 0.0 })

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
      | [ aidInstances; Null ] -> updateAidMaps aidInstances 0.0
      | [ aidInstances ]
      | [ aidInstances; _ ] -> updateAidMaps aidInstances 1.0
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

    member this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext

      let minCount =
        if Array.isEmpty aggContext.GroupingLabels then
          0L
        else
          int64 aggContext.AnonymizationParams.Suppression.LowThreshold

      if isNull state then
        Integer minCount
      else
        match Anonymizer.count aggContext.AnonymizationParams anonContext state with
        | Anonymizer.AnonymizedResult.NotEnoughAIDVs -> Integer minCount
        | Anonymizer.AnonymizedResult.Ok value -> Integer(max value minCount)

type private DiffixCountDistinct() =
  let mutable aidsCount = Option<int>.None
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
            if Option.isNone aidsCount then aidsCount <- Some aidInstances.Length
            let aidSets = emptySets aidsCount.Value
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

      if Option.isNone aidsCount then aidsCount <- other.AidsCount

      otherAidsPerValue
      |> Seq.iter (fun pair ->
        match aidsPerValue.TryGetValue(pair.Key) with
        | true, aidSets -> pair.Value |> mergeHashSetsInto aidSets
        | false, _ -> aidsPerValue.[pair.Key] <- pair.Value |> Array.map cloneHashSet
      )

    member this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext

      let minCount =
        if Array.isEmpty aggContext.GroupingLabels then
          0L
        else
          int64 aggContext.AnonymizationParams.Suppression.LowThreshold

      if Option.isNone aidsCount then
        Integer minCount
      else
        match Anonymizer.countDistinct aggContext.AnonymizationParams anonContext aidsCount.Value aidsPerValue with
        | Anonymizer.AnonymizedResult.NotEnoughAIDVs -> Integer minCount
        | Anonymizer.AnonymizedResult.Ok value -> Integer(max value minCount)

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

    member this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext

      if isNull state then
        Boolean true
      else
        Boolean(Anonymizer.isLowCount aggContext.AnonymizationParams anonContext state)

type private DiffixSum() =
  let nullState: Anonymizer.SumState = { Positive = null; Negative = null; IsReal = false }
  let mutable state = nullState

  let initialState length : Anonymizer.SumState =
    { nullState with
        Positive =
          Array.init length (fun _ -> { AidContributions = Dictionary<AidHash, float>(); UnaccountedFor = 0.0 })
        Negative =
          Array.init length (fun _ -> { AidContributions = Dictionary<AidHash, float>(); UnaccountedFor = 0.0 })
    }

  let increaseUnaccountedFor valueIncrease i =
    let absValueIncrease = abs (valueIncrease)

    if valueIncrease > 0.0 then
      state.Positive.[i].UnaccountedFor <- state.Positive.[i].UnaccountedFor + absValueIncrease
    else if valueIncrease < 0.0 then
      state.Negative.[i].UnaccountedFor <- state.Negative.[i].UnaccountedFor + absValueIncrease
    else
      state.Positive.[i].UnaccountedFor <- state.Positive.[i].UnaccountedFor + absValueIncrease
      state.Negative.[i].UnaccountedFor <- state.Negative.[i].UnaccountedFor + absValueIncrease

  let increaseSumContribution valueIncrease aidValue i =
    let absValueIncrease = abs (valueIncrease)

    if valueIncrease > 0.0 then
      increaseContribution absValueIncrease aidValue state.Positive.[i].AidContributions
    else if valueIncrease < 0.0 then
      increaseContribution absValueIncrease aidValue state.Negative.[i].AidContributions
    else
      increaseContribution absValueIncrease aidValue state.Positive.[i].AidContributions
      increaseContribution absValueIncrease aidValue state.Negative.[i].AidContributions


  /// Increases contribution of all AID instances.
  let increaseContributions valueIncrease (aidInstances: Value list) =
    aidInstances
    |> List.iteri (fun i aidValue ->
      match aidValue with
      // No AIDs, add to unaccounted value
      | Null
      | Value.List [] -> increaseUnaccountedFor valueIncrease i
      // List of AIDs, distribute contribution evenly
      | Value.List aidValues ->
        let partialIncrease = valueIncrease / (aidValues |> List.length |> float)

        aidValues
        |> List.iter (fun aidValue -> increaseSumContribution partialIncrease aidValue i)
      // Single AID, add to its contribution
      | aidValue -> increaseSumContribution valueIncrease aidValue i
    )

  let updateAidMaps aidInstances valueIncrease =
    match aidInstances with
    | Value.List aidInstances ->
      if state = nullState then state <- initialState aidInstances.Length

      if not <| List.forall missingAid aidInstances then
        increaseContributions valueIncrease aidInstances
    | _ -> failwith "Expecting a list as input"

  member this.State = state

  interface IAggregator with
    member this.Transition args =
      match args with
      | Value.List [] :: _ -> invalidArgs args
      // Note that we're completely ignoring `Null`, contrary to `count(col)` where it contributed 0.
      | [ aidInstances; Null ] -> ()
      | [ aidInstances; Integer value ] -> updateAidMaps aidInstances (float value)
      | [ aidInstances; Real value ] ->
        updateAidMaps aidInstances value
        state.IsReal <- true
      | _ -> invalidArgs args

    member this.Merge aggregator =
      let otherState = (castAggregator<DiffixSum> aggregator).State

      if otherState <> nullState then
        if state = nullState then state <- initialState otherState.Positive.Length

        let mergeStateLeg (leg: Anonymizer.AidCountState array) (otherLeg: Anonymizer.AidCountState array) =
          otherLeg
          |> Array.iteri (fun i otherAidCountState ->
            let aidCountState = leg.[i]
            let aidContributions = aidCountState.AidContributions
            aidCountState.UnaccountedFor <- aidCountState.UnaccountedFor + otherAidCountState.UnaccountedFor

            otherAidCountState.AidContributions
            |> Seq.iter (fun pair ->
              aidContributions.[pair.Key] <- (aidContributions |> Dictionary.getOrDefault pair.Key 0.0) + pair.Value
            )
          )

        mergeStateLeg state.Positive otherState.Positive
        mergeStateLeg state.Negative otherState.Negative
        state.IsReal <- state.IsReal || otherState.IsReal

    member this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext

      if state = nullState then
        Null
      else
        match Anonymizer.sum aggContext.AnonymizationParams anonContext state with
        | Anonymizer.AnonymizedResult.NotEnoughAIDVs -> Null
        | Anonymizer.AnonymizedResult.Ok value -> value

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type T = IAggregator

let isAnonymizing ((fn, _args): AggregatorSpec) =
  match fn with
  | DiffixCount
  | DiffixLowCount
  | DiffixSum -> true
  | _ -> false

let create (aggSpec: AggregatorSpec) : T =
  match aggSpec with
  | Count, { Distinct = false } -> Count() :> T
  | Count, { Distinct = true } -> CountDistinct() :> T
  | Sum, { Distinct = false } -> Sum() :> T
  | DiffixCount, { Distinct = false } -> DiffixCount() :> T
  | DiffixCount, { Distinct = true } -> DiffixCountDistinct() :> T
  | DiffixLowCount, _ -> DiffixLowCount() :> T
  | DiffixSum, { Distinct = false } -> DiffixSum() :> T
  | _ -> failwith "Invalid aggregator"
