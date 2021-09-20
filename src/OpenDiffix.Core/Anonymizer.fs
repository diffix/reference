module OpenDiffix.Core.Anonymizer

open System
open System.Collections.Generic
open System.Security.Cryptography

// ----------------------------------------------------------------
// Random & noise
// ----------------------------------------------------------------

// A 64-bit RNG built from 2 32-bit RNGs.
type private Random64(seed: uint64) =
  let mutable state = (Random(int seed), Random(int (seed >>> 32)))

  member this.Uniform(interval: Interval) =
    let state1, state2 = state
    state <- (state2, state1) // Rotate the 32-bit RNGs.
    state1.Next(interval.Lower, interval.Upper + 1)

  member this.Normal(stdDev) =
    let state1, state2 = state

    let u1 = 1.0 - state1.NextDouble()
    let u2 = 1.0 - state2.NextDouble()

    let randStdNormal = Math.Sqrt(-2.0 * log u1) * Math.Sin(2.0 * Math.PI * u2)

    stdDev * randStdNormal

let private sha256 = SHA256.Create()

let private cryptoHashSaltedAid salt (aid: AidHash) =
  let aidBytes = BitConverter.GetBytes(aid)
  sha256.ComputeHash(Array.append salt aidBytes)

let private newRandom anonymizationParams (aidSet: AidHash seq) =
  let setAid = Seq.fold (^^^) 0UL aidSet
  let hash = cryptoHashSaltedAid anonymizationParams.Salt setAid
  Random64(BitConverter.ToUInt64(hash, 0))

// ----------------------------------------------------------------
// AID processing
// ----------------------------------------------------------------

/// Returns whether any of the AID value sets has a low count.
let isLowCount (aidSets: HashSet<AidHash> seq) (anonymizationParams: AnonymizationParams) =
  aidSets
  |> Seq.map (fun aidSet ->
    let suppression = anonymizationParams.Suppression

    if aidSet.Count < suppression.LowThreshold then
      true
    else
      let rnd = newRandom anonymizationParams aidSet
      let thresholdMean = suppression.LowMeanGap + float suppression.LowThreshold
      let threshold = rnd.Normal(suppression.SD) + thresholdMean

      float aidSet.Count < threshold
  )
  |> Seq.reduce (||)

type private AidCount = { FlattenedSum: float; Flattening: float; NoiseSD: float; Noise: float }

let inline private aidFlattening
  (anonymizationParams: AnonymizationParams)
  (unaccountedFor: int64)
  (aidContributions: (AidHash * ^Contribution) list)
  : AidCount option =
  let rnd = aidContributions |> Seq.map fst |> newRandom anonymizationParams

  let outlierCount = rnd.Uniform(anonymizationParams.OutlierCount)
  let topCount = rnd.Uniform(anonymizationParams.TopCount)

  let sortedUserContributions = aidContributions |> Seq.map snd |> Seq.sortDescending |> Seq.toList

  if sortedUserContributions.Length < outlierCount + topCount then
    None
  else
    let outliersSummed = sortedUserContributions |> List.take outlierCount |> List.sum

    let topGroupValuesSummed =
      sortedUserContributions
      |> List.skip outlierCount
      |> List.take topCount
      |> List.sum

    let topGroupAverage = (float topGroupValuesSummed) / (float topCount)
    let outlierReplacement = topGroupAverage * (float outlierCount)

    let summedContributions = sortedUserContributions |> List.sum
    let flattening = float outliersSummed - outlierReplacement
    let flattenedUnaccountedFor = float unaccountedFor - flattening |> max 0.
    let flattenedSum = float summedContributions - flattening
    let flattenedAvg = flattenedSum / float sortedUserContributions.Length

    let noiseScale = max flattenedAvg (0.5 * topGroupAverage)
    let noiseSD = anonymizationParams.NoiseSD * noiseScale

    Some
      {
        FlattenedSum = flattenedSum + flattenedUnaccountedFor
        Flattening = flattening
        NoiseSD = noiseSD
        Noise = rnd.Normal(noiseSD)
      }

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

let rec private distributeValues valuesByAID =
  match valuesByAID with
  | [] -> [] // Done :D
  | (_aid, []) :: restValuesByAID -> distributeValues restValuesByAID
  | (aid, value :: restValues) :: restValuesByAID ->
    let restValuesByAID = // Drop current value from the remaining items.
      List.map (fun (aid, values) -> aid, values |> List.filter ((<>) value)) restValuesByAID

    (aid, value) :: distributeValues (restValuesByAID @ [ aid, restValues ])

let private countDistinctFlatteningByAid anonParams (perAidContributions: Dictionary<AidHash, HashSet<Value>>) =
  perAidContributions
  // keep low count values in sorted order to ensure the algorithm is deterministic
  |> Seq.map (fun pair -> pair.Key, pair.Value |> Seq.toList)
  |> Seq.sortBy (fun (aid, values) -> values.Length, aid)
  |> Seq.toList
  |> distributeValues
  |> List.countBy fst
  |> List.map (fun (aid, count) -> aid, int64 count)
  |> aidFlattening anonParams 0L

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
  aidsCount
  (aidsPerValue: Dictionary<Value, HashSet<AidHash> array>)
  (anonymizationParams: AnonymizationParams)
  =
  // These values are safe, and can be counted as they are
  // without any additional noise.
  let lowCountValues, highCountValues =
    aidsPerValue
    |> Seq.toList
    |> List.partition (fun pair -> isLowCount pair.Value anonymizationParams)

  let byAid =
    [ 0 .. aidsCount - 1 ]
    |> List.map (
      transposeToPerAid lowCountValues
      >> countDistinctFlatteningByAid anonymizationParams
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
    |> (round >> int64 >> (+) safeCount >> max 0L >> Integer)

type AidCountState = { AidContributions: Dictionary<AidHash, float>; mutable UnaccountedFor: int64 }

let count (anonymizationParams: AnonymizationParams) (perAidContributions: AidCountState array) =
  let byAid =
    perAidContributions
    |> Array.map (fun aidState ->
      aidState.AidContributions
      |> Seq.map (fun pair -> pair.Key, pair.Value)
      |> Seq.toList
      |> aidFlattening anonymizationParams aidState.UnaccountedFor
    )

  // If any of the AIDs had insufficient data to produce a sensible flattening
  // we have to abort anonymization.
  if byAid |> Array.exists ((=) None) then
    Null
  else
    byAid
    |> Array.choose id
    |> anonymizedSum
    |> Option.map (round >> int64 >> Integer)
    |> Option.defaultValue Null
