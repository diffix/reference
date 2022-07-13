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

type private RequestedOutput =
  | Value
  | Noise

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

type private DiffixCount(aidsCount) =
  let mutable state: Anonymizer.AidCountState array =
    Array.init aidsCount (fun _ -> { AidContributions = Dictionary<AidHash, float>(); UnaccountedFor = 0.0 })

  /// Increases contribution of all AID instances.
  let increaseContributions valueIncrease (aidInstances: Value list) =
    aidInstances
    |> List.iteri (fun i aidValue ->
      let aidState = state.[i]
      let aidContributions = aidState.AidContributions

      match aidValue with
      // No AIDs, add to unaccounted value
      | Null -> aidState.UnaccountedFor <- aidState.UnaccountedFor + valueIncrease
      | Value.List _ -> failwith "Lists of AIDs and distributing of contributions are not supported."
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
        if Array.isEmpty aggContext.GroupingLabels && List.isEmpty anonContext.BaseLabels then
          0L
        else
          int64 aggContext.AnonymizationParams.Suppression.LowThreshold

      match Anonymizer.count aggContext.AnonymizationParams anonContext state with
      | Anonymizer.AnonymizedResult.NotEnoughAIDVs -> Integer minCount
      | Anonymizer.AnonymizedResult.Ok { AnonymizedSum = value } -> Integer(max value minCount)

type private DiffixCountNoise(aidsCount) =
  inherit DiffixCount(aidsCount)

  interface IAggregator with
    override this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext

      match Anonymizer.count aggContext.AnonymizationParams anonContext this.State with
      | Anonymizer.AnonymizedResult.NotEnoughAIDVs -> Null
      | Anonymizer.AnonymizedResult.Ok { NoiseSD = noiseSD } -> Real noiseSD

type private DiffixCountDistinct(aidsCount) =
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
            let aidSets = emptySets aidsCount
            aidsPerValue.[value] <- aidSets
            aidSets

        aidInstances
        |> List.iteri (fun i aidValue ->
          match aidValue with
          | Null -> ()
          | Value.List _ -> failwith "Lists of AIDs and distributing of contributions are not supported."
          | aidValue -> aidSets.[i].Add(hashAid aidValue) |> ignore
        )
      | _ -> invalidArgs args

    member this.Merge aggregator =
      let other = (castAggregator<DiffixCountDistinct> aggregator)
      let otherAidsPerValue = other.State

      otherAidsPerValue
      |> Seq.iter (fun pair ->
        match aidsPerValue.TryGetValue(pair.Key) with
        | true, aidSets -> pair.Value |> mergeHashSetsInto aidSets
        | false, _ -> aidsPerValue.[pair.Key] <- pair.Value |> Array.map cloneHashSet
      )

    member this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext

      let minCount =
        if Array.isEmpty aggContext.GroupingLabels && List.isEmpty anonContext.BaseLabels then
          0L
        else
          int64 aggContext.AnonymizationParams.Suppression.LowThreshold

      match Anonymizer.countDistinct aggContext.AnonymizationParams anonContext aidsCount aidsPerValue with
      | Anonymizer.AnonymizedResult.NotEnoughAIDVs -> Integer minCount
      | Anonymizer.AnonymizedResult.Ok { AnonymizedSum = value } -> Integer(max value minCount)

type private DiffixCountDistinctNoise(aidsCount) =
  inherit DiffixCountDistinct(aidsCount)

  interface IAggregator with
    override this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext

      match Anonymizer.countDistinct aggContext.AnonymizationParams anonContext this.AidsCount this.State with
      | Anonymizer.AnonymizedResult.NotEnoughAIDVs -> Null
      | Anonymizer.AnonymizedResult.Ok { NoiseSD = noiseSD } -> Real noiseSD

type private DiffixLowCount(aidsCount) =
  let mutable state: HashSet<AidHash> [] = emptySets aidsCount

  member this.State = state

  interface IAggregator with
    member this.Transition args =
      match args with
      | [ Null ] -> ()
      | [ Value.List aidInstances ] when not aidInstances.IsEmpty ->
        aidInstances
        |> List.iteri (fun i aidValue ->
          match aidValue with
          | Null -> ()
          | Value.List _ -> failwith "Lists of AIDs and distributing of contributions are not supported."
          | aidValue -> state.[i].Add(hashAid aidValue) |> ignore
        )

      | _ -> invalidArgs args

    member this.Merge aggregator =
      let otherState = (castAggregator<DiffixLowCount> aggregator).State
      otherState |> mergeHashSetsInto state

    member this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext
      Boolean(Anonymizer.isLowCount aggContext.AnonymizationParams state)

type private DiffixSum(summandType, aidsCount) =
  let state: Anonymizer.SumState =
    {
      Positive =
        Array.init aidsCount (fun _ -> { AidContributions = Dictionary<AidHash, float>(); UnaccountedFor = 0.0 })
      Negative =
        Array.init aidsCount (fun _ -> { AidContributions = Dictionary<AidHash, float>(); UnaccountedFor = 0.0 })
    }

  let increaseUnaccountedFor valueIncrease i =
    let absValueIncrease = abs (valueIncrease)

    if valueIncrease > 0.0 then
      state.Positive.[i].UnaccountedFor <- state.Positive.[i].UnaccountedFor + absValueIncrease

    if valueIncrease < 0.0 then
      state.Negative.[i].UnaccountedFor <- state.Negative.[i].UnaccountedFor + absValueIncrease

  let increaseSumContribution valueIncrease aidValue i =
    let absValueIncrease = abs (valueIncrease)

    if valueIncrease >= 0.0 then
      increaseContribution absValueIncrease aidValue state.Positive.[i].AidContributions

    if valueIncrease <= 0.0 then
      increaseContribution absValueIncrease aidValue state.Negative.[i].AidContributions


  /// Increases contribution of all AID instances.
  let increaseContributions valueIncrease (aidInstances: Value list) =
    aidInstances
    |> List.iteri (fun i aidValue ->
      match aidValue with
      // No AIDs, add to unaccounted value
      | Null -> increaseUnaccountedFor valueIncrease i
      | Value.List _ -> failwith "Lists of AIDs and distributing of contributions are not supported."
      // Single AID, add to its contribution
      | aidValue -> increaseSumContribution valueIncrease aidValue i
    )

  let updateAidMaps aidInstances valueIncrease =
    match aidInstances with
    | Value.List aidInstances ->
      if not <| List.forall missingAid aidInstances then
        increaseContributions valueIncrease aidInstances
    | _ -> failwith "Expecting a list as input"

  member this.State = state
  member this.SummandType = summandType

  interface IAggregator with
    member this.Transition args =
      match args with
      | Value.List [] :: _ -> invalidArgs args
      // Note that we're completely ignoring `Null`, contrary to `count(col)` where it contributes 0.
      | [ _; Null ] -> ()
      | [ aidInstances; Integer value ] -> updateAidMaps aidInstances (float value)
      | [ aidInstances; Real value ] -> updateAidMaps aidInstances value
      | _ -> invalidArgs args

    member this.Merge aggregator =
      let otherState = (castAggregator<DiffixSum> aggregator).State

      if summandType <> (castAggregator<DiffixSum> aggregator).SummandType then
        failwith "Cannot merge incompatible aggregators."

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

    member this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext

      let isReal = summandType = RealType

      match Anonymizer.sum aggContext.AnonymizationParams anonContext state isReal with
      | Anonymizer.AnonymizedResult.NotEnoughAIDVs -> Null
      | Anonymizer.AnonymizedResult.Ok { AnonymizedSum = value } -> value


type private DiffixSumNoise(summandType, aidsCount) =
  inherit DiffixSum(summandType, aidsCount)

  interface IAggregator with
    override this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext

      let isReal = this.SummandType = RealType

      match Anonymizer.sum aggContext.AnonymizationParams anonContext this.State isReal with
      | Anonymizer.AnonymizedResult.NotEnoughAIDVs -> Null
      | Anonymizer.AnonymizedResult.Ok { NoiseSD = noiseSD } -> Real noiseSD


let private floorBy binSize count =
  match binSize with
  | None
  | Some 1L -> count
  | Some binSize -> (count / binSize) * binSize

type private CountHistogram(binSize: int64 option) =
  let state = Dictionary<Value, int64>()

  interface IAggregator with
    member this.Transition args =
      match args with
      | Null :: _ -> () // Ignore NULLs
      | countedAid :: _ -> state |> Dictionary.increment countedAid
      | _ -> invalidArgs args

    member this.Merge aggregator =
      let otherState = (castAggregator<CountHistogram> aggregator).State

      for other in otherState do
        let current = state |> Dictionary.getOrDefault other.Key 0L
        state.[other.Key] <- current + other.Value

    member this.Final(aggContext, anonContext) =
      let bins = Dictionary<int64, int64>()

      for pair in state do
        let binLabel = floorBy binSize pair.Value
        bins |> Dictionary.increment binLabel

      bins
      |> Seq.map (fun pair -> (pair.Key, pair.Value))
      |> Seq.sortBy fst
      |> Seq.map (fun (binLabel, aidCount) -> Value.List [ Integer binLabel; Integer aidCount ])
      |> Seq.toList
      |> Value.List

  member this.State = state


type private CountHistogramAidTracker = { mutable RowCount: int64; LowCount: DiffixLowCount }

type private CountHistogramBin = { LowCount: DiffixLowCount }

type private DiffixCountHistogram(aidsCount, countedAidIndex, binSize: int64 option) =
  let state = Dictionary<AidHash, CountHistogramAidTracker>()

  let makeAidTracker () =
    { RowCount = 0; LowCount = DiffixLowCount(aidsCount) }

  interface IAggregator with
    member this.Transition args =
      match args with
      | Value.List [] :: _ -> invalidArgs args
      | Value.List aidInstances :: _ ->
        let countedAid = aidInstances |> List.item countedAidIndex

        if countedAid <> Null then
          let aidState = state |> Dictionary.getOrInit (hashAid countedAid) makeAidTracker
          aidState.RowCount <- aidState.RowCount + 1L
          (aidState.LowCount :> IAggregator).Transition([ Value.List aidInstances ])
      | _ -> invalidArgs args

    member this.Merge aggregator =
      let otherState = (castAggregator<DiffixCountHistogram> aggregator).State

      for other in otherState do
        let currentState = state |> Dictionary.getOrInit other.Key makeAidTracker
        currentState.RowCount <- currentState.RowCount + other.Value.RowCount
        (currentState.LowCount :> IAggregator).Merge(other.Value.LowCount)

    member this.Final(aggContext, anonContext) =
      let anonContext = unwrapAnonContext anonContext
      let bins = Dictionary<int64, CountHistogramBin>()

      let anonBinCount (bin: CountHistogramBin) =
        Anonymizer.histogramBinCount aggContext.AnonymizationParams anonContext bin.LowCount.State.[countedAidIndex]

      for pair in state do
        let aidEntry = pair.Value
        let binLabel = floorBy binSize aidEntry.RowCount

        match bins.TryGetValue(binLabel) with
        | true, bin ->
          // Bin already exists, merge to it.
          (bin.LowCount :> IAggregator).Merge(aidEntry.LowCount)
        | false, _ ->
          // New bin, create and merge.
          let bin = { LowCount = DiffixLowCount(aidsCount) }
          (bin.LowCount :> IAggregator).Merge(aidEntry.LowCount)
          bins.[binLabel] <- bin

      let suppressBin = { LowCount = DiffixLowCount(aidsCount) }

      let highCountBins =
        bins
        |> Seq.choose (fun pair ->
          let binLabel = pair.Key
          let bin = pair.Value

          if Anonymizer.isLowCount aggContext.AnonymizationParams bin.LowCount.State then
            // Merge low count bin to suppress bin.
            (suppressBin.LowCount :> IAggregator).Merge(bin.LowCount)
            None
          else
            Some(binLabel, bin)
        )
        |> Seq.sortBy fst
        |> Seq.map (fun (binLabel, bin) -> Value.List [ Integer binLabel; anonBinCount bin ])
        |> Seq.toList

      if Anonymizer.isLowCount aggContext.AnonymizationParams suppressBin.LowCount.State then
        Value.List highCountBins
      else
        Value.List((Value.List [ Null; anonBinCount suppressBin ]) :: highCountBins)

  member this.State = state

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type T = IAggregator

let isAnonymizing ((fn, _args): AggregatorSpec) =
  match fn with
  | DiffixCount
  | DiffixCountNoise
  | DiffixLowCount
  | DiffixSum
  | DiffixSumNoise
  | DiffixAvg
  | DiffixAvgNoise
  | DiffixCountHistogram -> true
  | _ -> false

let create (aggSpec: AggregatorSpec, aggArgs: AggregatorArgs) : T =
  let aidsCount =
    if isAnonymizing aggSpec then
      aggArgs
      |> List.head
      |> function
        | ListExpr v -> v.Length
        | _ -> failwith "Expected the AID argument to be a ListExpr."
    else
      0

  let unpackAidIndexArgAt index =
    aggArgs
    |> List.tryItem index
    |> function
      | Some (Constant (Integer x)) when x >= 0L && x < aidsCount -> int32 x
      | _ -> failwith "Expected a valid AID index"

  let unpackFloorWidthArgAt index =
    aggArgs
    |> List.tryItem index
    |> function
      | None -> None
      | Some (Constant (Integer x)) when x >= 1L -> Some x
      | _ -> failwith $"Expected positive integer argument in index {index} of aggregate {fst aggSpec}."

  match aggSpec with
  | Count, { Distinct = false } -> Count() :> T
  | Count, { Distinct = true } -> CountDistinct() :> T
  | Sum, { Distinct = false } -> Sum() :> T
  | DiffixCount, { Distinct = false } -> DiffixCount(aidsCount) :> T
  | DiffixCountNoise, { Distinct = false } -> DiffixCountNoise(aidsCount) :> T
  | DiffixCount, { Distinct = true } -> DiffixCountDistinct(aidsCount) :> T
  | DiffixCountNoise, { Distinct = true } -> DiffixCountDistinctNoise(aidsCount) :> T
  | DiffixLowCount, _ -> DiffixLowCount(aidsCount) :> T
  | DiffixSum, { Distinct = false } ->
    let aggType = Expression.typeOfAggregate (fst aggSpec) aggArgs
    DiffixSum(aggType, aidsCount) :> T
  | DiffixSumNoise, { Distinct = false } ->
    let aggType = Expression.typeOfAggregate (fst aggSpec) aggArgs
    DiffixSumNoise(aggType, aidsCount) :> T
  | CountHistogram, { Distinct = false } -> CountHistogram(unpackFloorWidthArgAt 1) :> T
  | DiffixCountHistogram, { Distinct = false } ->
    DiffixCountHistogram(aidsCount, unpackAidIndexArgAt 1, unpackFloorWidthArgAt 2) :> T
  | _ -> failwith "Invalid aggregator"
