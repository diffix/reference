module OpenDiffix.Core.Anonymizer

open System
open System.Security.Cryptography

// ----------------------------------------------------------------
// Random & noise
// ----------------------------------------------------------------

// The noise seeds are hash values.
// From each seed we generate a single random value, with either a uniform or a normal distribution.
// Any decent hash function should produce values that are uniformly distributed over the output space.
// Hence, we only need to limit the seed to the requested interval to get a uniform random integer.
// To get a normal random float, we use the Box-Muller method on two uniformly distributed integers.

// While using modulo to bound values produces biased output, we are using very small ranges
// (typically less than 10), for which the bias is insignificant.
let private boundRandomUniform random range = random % range

let private randomUniform (interval: Interval) (seed: Hash) =
  let randomUniform = abs (int ((seed >>> 32) ^^^ seed))
  let boundedRandomUniform = boundRandomUniform randomUniform (interval.Upper - interval.Lower + 1)
  interval.Lower + boundedRandomUniform

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

let private mixSeed (text: string) (seed: Hash) =
  let seedBytes = BitConverter.GetBytes(seed)
  let textBytes = System.Text.Encoding.UTF8.GetBytes text
  Array.append seedBytes textBytes |> Hash.bytes

let private generateNoise salt stepName stdDev noiseLayers =
  noiseLayers
  |> Seq.map (cryptoHashSaltedSeed salt >> mixSeed stepName >> randomNormal stdDev)
  |> Seq.reduce (+)

// ----------------------------------------------------------------
// AID processing
// ----------------------------------------------------------------

/// Returns whether any of the AID value sets has a low count.
let isLowCount (executionContext: ExecutionContext) (aidSets: HashSet<AidHash> seq) =
  aidSets
  |> Seq.map (fun aidSet ->
    let anonParams = executionContext.AnonymizationParams

    if aidSet.Count < anonParams.Suppression.LowThreshold then
      true
    else
      let thresholdNoise =
        [ executionContext.NoiseLayers.BucketSeed; seedFromAidSet aidSet ]
        |> generateNoise anonParams.Salt "suppress" anonParams.Suppression.LayerSD

      let thresholdMean = anonParams.Suppression.LowMeanGap + float anonParams.Suppression.LowThreshold
      let threshold = thresholdNoise + thresholdMean

      float aidSet.Count < threshold
  )
  |> Seq.reduce (||)

// Compacts flattening intervals to fit into the total count of contributors.
// Both intervals are reduced proportionally, with `topCount` taking priority.
let private compactFlatteningIntervals outlierCount topCount totalCount =
  let totalAdjustment = outlierCount.Upper + topCount.Upper - totalCount

  if totalAdjustment <= 0 then
    outlierCount, topCount // no adjustment needed
  else
    let outlierRange = outlierCount.Upper - outlierCount.Lower

    if outlierRange > topCount.Upper - topCount.Lower then
      failwith "Invalid config: OutlierCount interval is larger than TopCount interval."

    let outlierAdjustment, topAdjustment =
      if outlierRange < totalAdjustment / 2 then
        outlierRange, totalAdjustment - outlierRange
      else
        totalAdjustment / 2, totalAdjustment - totalAdjustment / 2

    { outlierCount with Upper = outlierCount.Upper - outlierAdjustment },
    { topCount with Upper = topCount.Upper - topAdjustment }

type private AidCount = { FlattenedSum: float; Flattening: float; NoiseSD: float; Noise: float }

let inline private aidFlattening
  (executionContext: ExecutionContext)
  (unaccountedFor: int64)
  (aidContributions: (AidHash * ^Contribution) array)
  : AidCount option =
  let anonParams = executionContext.AnonymizationParams

  if aidContributions.Length < anonParams.OutlierCount.Lower + anonParams.TopCount.Lower then
    None
  else
    let outlierInterval, topInterval =
      compactFlatteningIntervals anonParams.OutlierCount anonParams.TopCount aidContributions.Length

    let sortedAidContributions = aidContributions |> Array.sortByDescending snd

    let flatSeed =
      sortedAidContributions
      |> Seq.take (outlierInterval.Upper + topInterval.Upper)
      |> Seq.map fst
      |> seedFromAidSet
      |> cryptoHashSaltedSeed anonParams.Salt

    let outlierCount = flatSeed |> mixSeed "outlier" |> randomUniform outlierInterval
    let topCount = flatSeed |> mixSeed "top" |> randomUniform topInterval

    let outliersSummed = sortedAidContributions |> Seq.take outlierCount |> Seq.sumBy snd

    let topGroupValuesSummed =
      sortedAidContributions
      |> Seq.skip outlierCount
      |> Seq.take topCount
      |> Seq.sumBy snd

    let topGroupAverage = (float topGroupValuesSummed) / (float topCount)
    let outlierReplacement = topGroupAverage * (float outlierCount)

    let summedContributions = aidContributions |> Array.sumBy snd
    let flattening = float outliersSummed - outlierReplacement
    let flattenedUnaccountedFor = float unaccountedFor - flattening |> max 0.
    let flattenedSum = float summedContributions - flattening
    let flattenedAvg = flattenedSum / float aidContributions.Length

    let noiseScale = max flattenedAvg (0.5 * topGroupAverage)
    let noiseSD = anonParams.LayerNoiseSD * noiseScale

    let noise =
      [ executionContext.NoiseLayers.BucketSeed; aidContributions |> Seq.map fst |> seedFromAidSet ]
      |> generateNoise anonParams.Salt "noise" noiseSD

    Some
      {
        FlattenedSum = flattenedSum + flattenedUnaccountedFor
        Flattening = flattening
        NoiseSD = noiseSD
        Noise = noise
      }

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
  (executionContext: ExecutionContext)
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
  |> aidFlattening executionContext 0L

let private anonymizedSum (byAidSum: AidCount seq) =
  let aidForFlattening =
    byAidSum
    |> Seq.sortByDescending (fun aggregate -> aggregate.Flattening)
    |> Seq.groupBy (fun aggregate -> aggregate.Flattening)
    |> Seq.tryHead
    // We might end up with multiple different flattened sums that have the same amount of flattening.
    // This could be the result of some AID values being null for one of the AIDs, while there were still
    // overall enough AIDs to produce a flattened sum.
    // In these case we want to use the largest flattened sum to minimize unnecessary flattening.
    |> Option.map (fun (_, values) -> values |> Seq.maxBy (fun aggregate -> aggregate.FlattenedSum))

  let noise =
    byAidSum
    |> Seq.sortByDescending (fun aggregate -> aggregate.NoiseSD)
    |> Seq.tryHead
    |> Option.map (fun aggregate -> aggregate.Noise)

  match aidForFlattening, noise with
  | Some flattening, Some noise -> Some <| flattening.FlattenedSum + noise
  | _ -> None

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let countDistinct
  (executionContext: ExecutionContext)
  aidsCount
  (aidsPerValue: Dictionary<Value, HashSet<AidHash> array>)
  =
  // These values are safe, and can be counted as they are
  // without any additional noise.
  let lowCountValues, highCountValues =
    aidsPerValue
    |> Seq.toArray
    |> Array.partition (fun pair -> isLowCount executionContext pair.Value)

  let sortedLowCountValues = sortByValue lowCountValues

  let byAid =
    [ 0 .. aidsCount - 1 ]
    |> List.map (
      transposeToPerAid sortedLowCountValues
      >> countDistinctFlatteningByAid executionContext
    )

  let safeCount = int64 highCountValues.Length

  // If any of the AIDs had insufficient data to produce a sensible flattening
  // we can only report the count of values we already know to be safe as they
  // individually passed low count filtering.
  if byAid |> List.exists ((=) None) then
    if safeCount > 0L then Integer safeCount else Null
  else
    byAid
    |> List.choose id
    |> anonymizedSum
    |> Option.defaultValue 0.
    |> (Math.roundAwayFromZero >> int64 >> (+) safeCount >> max 0L >> Integer)

type AidCountState = { AidContributions: Dictionary<AidHash, float>; mutable UnaccountedFor: int64 }

let count (executionContext: ExecutionContext) (perAidContributions: AidCountState array) =
  let byAid =
    perAidContributions
    |> Array.map (fun aidState ->
      aidState.AidContributions
      |> Seq.map (fun pair -> pair.Key, pair.Value)
      |> Seq.toArray
      |> aidFlattening executionContext aidState.UnaccountedFor
    )

  // If any of the AIDs had insufficient data to produce a sensible flattening
  // we have to abort anonymization.
  if byAid |> Array.exists ((=) None) then
    Null
  else
    byAid
    |> Array.choose id
    |> anonymizedSum
    |> Option.map (Math.roundAwayFromZero >> int64 >> Integer)
    |> Option.defaultValue Null
