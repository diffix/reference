module OpenDiffix.Core.Anonymizer

open System
open System.Security.Cryptography

type ContributionsState = { AidContributions: Dictionary<AidHash, float>; mutable UnaccountedFor: float }

type CountResult =
  {
    AnonymizedCount: int64
    NoiseSD: float
    Outliers: (AidHash * float) array array
  }

type SumResult =
  {
    AnonymizedSum: float
    NoiseSD: float
    PositiveOutliers: (AidHash * float) array array
    NegativeOutliers: (AidHash * float) array array
  }

type SumState =
  {
    // Both `Positive` and `Negative` include 0.0 contributions by design, but for simplicity we call it like this.
    Positive: ContributionsState array
    Negative: ContributionsState array
  }

// ----------------------------------------------------------------
// Random & noise
// ----------------------------------------------------------------

// The noise seeds are hash values.
// From each seed we generate a single random value, with either a uniform or a normal distribution.
// Any decent hash function should produce values that are uniformly distributed over the output space.
// Hence, we only need to limit the seed to the requested interval to get a uniform random integer.
// To get a normal random float, we use the Box-Muller method on two uniformly distributed integers.

let private randomUniform (interval: Interval) (seed: Hash) =
  let randomUniform = uint32 ((seed >>> 32) ^^^ seed)
  // While using modulo to bound values produces biased output, we are using very small ranges
  // (typically less than 10), for which the bias is insignificant.
  let boundedRandomUniform = randomUniform % uint32 (interval.Upper - interval.Lower + 1)
  interval.Lower + int boundedRandomUniform

let private randomNormal stdDev (seed: Hash) =
  let u1 = float (uint32 seed) / float UInt32.MaxValue
  let u2 = float (uint32 (seed >>> 32)) / float UInt32.MaxValue
  let randomNormal = Math.Sqrt(-2.0 * log u1) * Math.Sin(2.0 * Math.PI * u2)
  stdDev * randomNormal

let private cryptoHashSaltedSeed salt (seed: Hash) : Hash =
  use sha256 = SHA256.Create()
  let seedBytes = BitConverter.GetBytes(seed)
  let hash = sha256.ComputeHash(Array.append salt seedBytes)
  BitConverter.ToUInt64(hash, 0)

let private seedFromAidSet (aidSet: AidHash seq) = Seq.fold (^^^) 0UL aidSet

let private mixSeed (text: string) (seed: Hash) = text |> Hash.string |> ((^^^) seed)

let private generateNoise salt stepName stdDev noiseLayers =
  noiseLayers
  |> Seq.map (cryptoHashSaltedSeed salt >> mixSeed stepName >> randomNormal stdDev)
  |> Seq.reduce (+)

// ----------------------------------------------------------------
// AID processing
// ----------------------------------------------------------------

// Compacts flattening intervals to fit into the total count of contributors.
// Both intervals are reduced proportionally, with `topCount` taking priority.
// `None` is returned in case there's not enough AIDVs for a sensible flattening.
// `public` just to test the low-level algorithm
let compactFlatteningIntervals outlierCount topCount totalCount =
  if totalCount < outlierCount.Lower + topCount.Lower then
    None
  else
    let totalAdjustment = outlierCount.Upper + topCount.Upper - totalCount

    let compactIntervals =
      if totalAdjustment <= 0 then
        outlierCount, topCount // no adjustment needed
      else
        // NOTE: at this point we know `0 < totalAdjustment <= outlierRange + topRange` (*)
        //       because `totalAdjustment = outlierCount.Upper + topCount.Upper - totalCount
        //                               <= outlierCount.Upper + topCount.Upper - outlierCount.Lower - topCount.Lower`
        let outlierRange = outlierCount.Upper - outlierCount.Lower
        let topRange = topCount.Upper - topCount.Lower
        // `topAdjustment` will be half of `totalAdjustment` rounded up, so it takes priority as it should
        let outlierAdjustment = totalAdjustment / 2
        let topAdjustment = totalAdjustment - outlierAdjustment

        // adjust, depending on how the adjustments "fit" in the ranges
        match outlierRange >= outlierAdjustment, topRange >= topAdjustment with
        | true, true ->
          // both ranges are compacted at same rate
          { outlierCount with Upper = outlierCount.Upper - outlierAdjustment },
          { topCount with Upper = topCount.Upper - topAdjustment }
        | false, true ->
          // `outlierCount` is compacted as much as possible by `outlierRange`, `topCount` takes the surplus adjustment
          { outlierCount with Upper = outlierCount.Lower },
          { topCount with Upper = topCount.Upper - totalAdjustment + outlierRange }
        | true, false ->
          // vice versa
          { outlierCount with Upper = outlierCount.Upper - totalAdjustment + topRange },
          { topCount with Upper = topCount.Lower }
        | false, false ->
          // Not possible. Otherwise `outlierRange + topRange < outlierAdjustment + topAdjustment = totalAdjustment` but we
          // knew the opposite was true in (*) above
          failwith "Internal error - impossible interval compacting"

    Some compactIntervals

type private AidCount =
  {
    FlattenedSum: float
    Flattening: float
    NoiseSD: float
    Noise: float
    Outliers: (AidHash * float) array
  }

let inline private aidFlattening
  (anonContext: AnonymizationContext)
  (unaccountedFor: float)
  (aidContributions: (AidHash * ^Contribution) array)
  : AidCount option =
  let totalCount = aidContributions.Length
  let anonParams = anonContext.AnonymizationParams

  match compactFlatteningIntervals anonParams.OutlierCount anonParams.TopCount totalCount with
  | None -> None // not enough AIDVs for a sensible flattening
  | Some(outlierInterval, topInterval) ->
    let sortedAidContributions =
      aidContributions
      |> Array.sortByDescending (fun (aid, contribution) -> contribution, aid)

    let flatSeed =
      sortedAidContributions
      |> Seq.take (outlierInterval.Upper + topInterval.Upper)
      |> Seq.map fst
      |> seedFromAidSet
      |> cryptoHashSaltedSeed anonParams.Salt

    let outlierCount = flatSeed |> mixSeed "outlier" |> randomUniform outlierInterval
    let topCount = flatSeed |> mixSeed "top" |> randomUniform topInterval

    let topGroupSum =
      sortedAidContributions
      |> Seq.skip outlierCount
      |> Seq.take topCount
      |> Seq.sumBy snd

    let topGroupAverage = (float topGroupSum) / (float topCount)

    let droppedContributions =
      sortedAidContributions
      |> Seq.take outlierCount
      |> Seq.map (fun (aid, contribution) -> aid, (float contribution) - topGroupAverage)
      |> Seq.filter (fun (_aid, contribution) -> contribution > 0)
      |> Seq.toArray

    let realSum = aidContributions |> Array.sumBy snd
    let flattening = droppedContributions |> Seq.sumBy snd
    let flattenedUnaccountedFor = unaccountedFor - flattening |> max 0.
    let flattenedSum = float realSum - flattening
    let flattenedAvg = flattenedSum / float totalCount

    let noiseScale = max flattenedAvg (0.5 * topGroupAverage)
    let noiseSD = anonParams.LayerNoiseSD * noiseScale

    let noise =
      [ anonContext.BucketSeed; aidContributions |> Seq.map fst |> seedFromAidSet ]
      |> generateNoise anonParams.Salt "noise" noiseSD

    Some
      {
        FlattenedSum = flattenedSum + flattenedUnaccountedFor
        Flattening = flattening
        NoiseSD = noiseSD
        Noise = noise
        Outliers = droppedContributions
      }

let private arrayFromDict (d: Dictionary<'a, 'b>) =
  d |> Seq.map (fun pair -> pair.Key, pair.Value) |> Seq.toArray

let private mapAidFlattening (anonContext: AnonymizationContext) perAidContributions =
  perAidContributions
  |> Array.map (fun aidState ->
    aidState.AidContributions
    |> arrayFromDict
    |> aidFlattening anonContext aidState.UnaccountedFor
  )

let private sortByValue (aidsPerValue: KeyValuePair<Value, HashSet<AidHash> array> seq) =
  let comparer = Value.comparer Ascending NullsFirst
  aidsPerValue |> (Seq.sortWith (fun kvA kvB -> comparer kvA.Key kvB.Key))

let private transposeToPerAid (aidsPerValue: KeyValuePair<Value, HashSet<AidHash> array> seq) aidIndex =
  let result = Dictionary<AidHash, HashSet<Value>>()

  let addToResult value aid =
    match result.TryGetValue(aid) with
    | true, valueSet -> valueSet.Add(value) |> ignore
    | false, _ ->
      let valueSet = HashSet<Value>()
      result.[aid] <- valueSet
      valueSet.Add(value) |> ignore

  for pair in aidsPerValue do
    let value = pair.Key
    let aids = pair.Value.[aidIndex]
    aids |> Seq.iter (addToResult value)

  result

let private distributeValues (valuesByAID: seq<AidHash * array<Value>>) : seq<AidHash * Value> =
  let usedValues = HashSet<Value>()

  let rec pickUnusedValue (values: Stack<Value>) =
    match values.TryPop() with
    | true, value -> if usedValues.Contains(value) then pickUnusedValue values else ValueSome value
    | false, _ -> ValueNone

  let result = MutableList<AidHash * Value>()

  let mutable remainingItems =
    valuesByAID
    |> Seq.filter (fun (_aid, values) -> values.Length > 0)
    |> Seq.map (fun (aid, values) -> aid, Stack<Value>(values))
    |> Seq.toArray

  while remainingItems.Length > 0 do
    remainingItems <-
      remainingItems
      |> Array.filter (fun (aid, values) ->
        match pickUnusedValue values with
        | ValueSome value ->
          result.Add((aid, value))
          usedValues.Add(value) |> ignore
          values.Count > 0
        | ValueNone -> false
      )

  result :> seq<AidHash * Value>

let private countDistinctFlatteningByAid
  (anonContext: AnonymizationContext)
  (perAidContributions: Dictionary<AidHash, HashSet<Value>>)
  =
  perAidContributions
  // keep low count values in sorted order to ensure the algorithm is deterministic
  |> Seq.map (fun pair -> pair.Key, pair.Value |> Seq.toArray)
  |> Seq.sortBy (fun (aid, values) -> values.Length, aid)
  |> distributeValues
  |> Seq.countBy fst
  |> Seq.map (fun (aid, count) -> aid, int64 count)
  |> Seq.toArray
  |> aidFlattening anonContext 0.0

// Assumes that `byAidSum` is non-empty, meaning that there is at least one AID instance involved
let private anonymizedSum (byAidSum: AidCount seq) =
  let flattening =
    byAidSum
    // We might end up with multiple different flattened sums that have the same amount of flattening.
    // This could be the result of some AID values being null for one of the AIDs, while there were still
    // overall enough AIDs to produce a flattened sum.
    // In these case we want to use the largest flattened sum to minimize unnecessary flattening.
    |> Seq.maxBy (fun aggregate -> aggregate.Flattening, aggregate.FlattenedSum)

  let noise =
    byAidSum
    // For determinism, resolve draws using maximum absolute noise value.
    |> Seq.maxBy (fun aggregate -> aggregate.NoiseSD, Math.Abs(aggregate.Noise))

  (flattening.FlattenedSum + noise.Noise, noise.NoiseSD)

let private moneyRoundNoise noiseSD =
  if noiseSD = 0.0 then
    0.0
  else
    let roundingResolution = Value.moneyRound (0.05 * noiseSD)

    (noiseSD / roundingResolution) |> ceil |> (*) roundingResolution

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

/// Returns whether any of the AID value sets has a low count.
let isLowCount salt (lowCountParams: LowCountParams) (aidSets: HashSet<AidHash> seq) =
  aidSets
  |> Seq.map (fun aidSet ->
    if aidSet.Count < lowCountParams.LowThreshold then
      true
    else
      let thresholdNoise =
        [ seedFromAidSet aidSet ]
        |> generateNoise salt "suppress" lowCountParams.LayerSD

      // `LowMeanGap` is the number of (total!) standard deviations between `LowThreshold` and desired mean
      let thresholdMean =
        lowCountParams.LowMeanGap * lowCountParams.LayerSD * sqrt (2.0)
        + float lowCountParams.LowThreshold

      let threshold = thresholdNoise + thresholdMean

      float aidSet.Count < threshold
  )
  |> Seq.reduce (||)

let countDistinct
  (anonContext: AnonymizationContext)
  aidsCount
  (aidsPerValue: Dictionary<Value, HashSet<AidHash> array>)
  =
  let anonParams = anonContext.AnonymizationParams
  // These values are safe, and can be counted as they are
  // without any additional noise.
  let lowCountValues, highCountValues =
    aidsPerValue
    |> Seq.toArray
    |> Array.partition (fun pair -> isLowCount anonParams.Salt anonParams.Suppression pair.Value)

  let sortedLowCountValues = sortByValue lowCountValues

  let byAid =
    [ 0 .. aidsCount - 1 ]
    |> List.map (
      transposeToPerAid sortedLowCountValues
      >> countDistinctFlatteningByAid anonContext
    )

  let safeCount = int64 highCountValues.Length

  // If any of the AIDs had insufficient data to produce a sensible flattening
  // we can only report the count of values we already know to be safe as they
  // individually passed low count filtering.
  if byAid |> List.exists ((=) None) then
    if safeCount > 0 then
      Some { AnonymizedCount = safeCount; NoiseSD = 0.0; Outliers = [||] }
    else
      None
  else
    let (value, noiseSD) = byAid |> List.choose id |> anonymizedSum

    Some
      {
        AnonymizedCount = value |> Math.roundAwayFromZero |> int64 |> (+) safeCount
        NoiseSD = moneyRoundNoise noiseSD
        Outliers = [||]
      }

let private gatherOutliers (byAid: option<AidCount> array) =
  byAid
  |> Array.map (
    function
    | None -> [||]
    | Some state -> state.Outliers
  )

let count (anonContext: AnonymizationContext) (contributions: ContributionsState array) =
  let byAid = mapAidFlattening anonContext contributions

  // If any of the AIDs had insufficient data to produce a sensible flattening
  // we have to abort anonymization.
  if byAid |> Array.exists ((=) None) then
    None
  else
    let (value, noiseSD) = byAid |> Array.choose id |> anonymizedSum

    Some
      {
        AnonymizedCount = value |> Math.roundAwayFromZero |> int64
        NoiseSD = moneyRoundNoise noiseSD
        Outliers = byAid |> gatherOutliers
      }

let histogramBinCount (anonContext: AnonymizationContext) (aidSet: HashSet<AidHash>) =
  let numAids = aidSet.Count
  let anonParams = anonContext.AnonymizationParams

  let noise =
    [ anonContext.BucketSeed; seedFromAidSet aidSet ]
    |> generateNoise anonParams.Salt "count_histogram" anonParams.LayerNoiseSD

  (float numAids + noise)
  |> Math.roundAwayFromZero
  |> int64
  |> max anonParams.Suppression.LowThreshold
  |> Integer

let sum (anonContext: AnonymizationContext) (contributions: SumState) =
  let byAidPositive = mapAidFlattening anonContext contributions.Positive
  let byAidNegative = mapAidFlattening anonContext contributions.Negative

  // If any of the AIDs had insufficient data to produce a sensible flattening
  // for both positive and negative values, we have to abort anonymization.
  if (Array.zip byAidPositive byAidNegative) |> Array.exists ((=) (None, None)) then
    None
  else
    let anonymizedSumOnNonEmpty =
      Array.choose id
      >> function
        | [||] -> (0.0, 0.0)
        | nonEmpty -> anonymizedSum nonEmpty

    // Using `anonymizedSum` separately for positive and negative, we ensure that we pick the appropriate
    // amount of flattening and noise for each leg, and only later combine the results.
    let (positive, positiveNoiseSD) = anonymizedSumOnNonEmpty byAidPositive
    let (negative, negativeNoiseSD) = anonymizedSumOnNonEmpty byAidNegative

    let noiseSD = Math.Sqrt(positiveNoiseSD ** 2.0 + negativeNoiseSD ** 2.0)

    Some
      {
        AnonymizedSum = positive - negative
        NoiseSD = moneyRoundNoise noiseSD
        PositiveOutliers = gatherOutliers byAidPositive
        NegativeOutliers = gatherOutliers byAidNegative
      }
