module OpenDiffix.Core.Anonymizer

// ----------------------------------------------------------------
// Random & noise
// ----------------------------------------------------------------

// A 64-bit RNG built from 2 32-bit RNGs.
type private Random(seed: uint64) =
  let mutable state = (System.Random(int seed), System.Random(int (seed >>> 32)))

  member this.Uniform(threshold: Threshold) =
    let (state1, state2) = state
    state <- (state2, state1) // Rotate the 32-bit RNGs.
    state1.Next(threshold.Lower, threshold.Upper + 1)

  member this.Normal(stdDev) =
    let (state1, state2) = state

    let u1 = 1.0 - state1.NextDouble()
    let u2 = 1.0 - state2.NextDouble()

    let randStdNormal = System.Math.Sqrt(-2.0 * log u1) * System.Math.Sin(2.0 * System.Math.PI * u2)

    stdDev * randStdNormal

let private newRandom (anonymizationParams: AnonymizationParams) (aidSet: AidHash seq) =
  let combinedAids = Seq.fold (^^^) 0UL aidSet
  let seed = combinedAids ^^^ anonymizationParams.Salt
  Random(seed)

// ----------------------------------------------------------------
// AID processing
// ----------------------------------------------------------------

/// Returns whether any of the AID value sets has a low count.
let isLowCount (aidSets: Set<AidHash> list) (anonymizationParams: AnonymizationParams) =
  aidSets
  |> List.map (fun aidSet ->
    if aidSet.Count < anonymizationParams.MinimumAllowedAids then
      true
    else
      let rnd = newRandom anonymizationParams aidSet

      let threshold =
        rnd.Uniform(
          {
            Lower = anonymizationParams.MinimumAllowedAids
            Upper = anonymizationParams.MinimumAllowedAids + 2
          }
        )

      aidSet.Count < threshold
  )
  |> List.reduce (||)

type private AidCount = { FlattenedSum: float; Flattening: float; NoiseSD: float; Noise: float }

let inline private aidFlattening
  (anonymizationParams: AnonymizationParams)
  (unaccountedFor: int64)
  (aidContributions: (AidHash * ^Contribution) list)
  : AidCount option =
  let rnd = aidContributions |> List.map fst |> newRandom anonymizationParams

  let outlierCount = rnd.Uniform(anonymizationParams.OutlierCount)
  let topCount = rnd.Uniform(anonymizationParams.TopCount)

  let sortedUserContributions = aidContributions |> List.map snd |> List.sortDescending

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

let mapValueSet value =
  Option.map (Set.add value) >> Option.orElse (Some(Set.singleton value))

let transposeToPerAid (aidsPerValue: Map<Value, Set<AidHash> list>) aidIndex =
  aidsPerValue
  |> Map.fold
    (fun acc value aids ->
      aids
      |> List.item aidIndex
      |> Set.fold (fun acc aidHash -> acc |> Map.change aidHash (mapValueSet value)) acc
    )
    Map.empty

let rec distributeValues valuesByAID =
  match valuesByAID with
  | [] -> [] // Done :D
  | (_aid, []) :: restValuesByAID -> distributeValues restValuesByAID
  | (aid, value :: restValues) :: restValuesByAID ->
      let restValuesByAID = // Drop current value from the remaining items.
        List.map (fun (aid, values) -> aid, values |> List.filter ((<>) value)) restValuesByAID

      (aid, value) :: distributeValues (restValuesByAID @ [ aid, restValues ])

let private countDistinctFlatteningByAid anonParams (perAidContributions: Map<AidHash, Set<Value>>) =
  perAidContributions
  |> Map.map (fun _aidHash valuesSet ->
    // keep low count values in sorted order to ensure the algorithm is deterministic
    Set.toList valuesSet
  )
  |> Map.toList
  |> List.sortBy (fun (aid, values) -> values.Length, aid)
  |> distributeValues
  |> List.countBy fst
  |> List.map (fun (aid, count) -> aid, int64 count)
  |> aidFlattening anonParams 0L

let private anonymizedSum (byAidSum: AidCount list) =
  let aidForFlattening =
    byAidSum
    |> List.sortByDescending (fun aggregate -> aggregate.Flattening)
    |> List.groupBy (fun aggregate -> aggregate.Flattening)
    |> List.tryHead
    // We might end up with multiple different flattened sums that have the same amount of flattening.
    // This could be the result of some AID values being null for one of the AIDs, while there were still
    // overall enough AIDs to produce a flattened sum.
    // In these case we want to use the largest flattened sum to minimize unnecessary flattening.
    |> Option.map (fun (_, values) -> values |> List.maxBy (fun aggregate -> aggregate.FlattenedSum))

  let noise =
    byAidSum
    |> List.sortByDescending (fun aggregate -> aggregate.NoiseSD)
    |> List.tryHead
    |> Option.map (fun aggregate -> aggregate.Noise)

  match aidForFlattening, noise with
  | Some flattening, Some noise -> Some <| flattening.FlattenedSum + noise
  | _ -> None

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let countDistinct aidsCount (aidsPerValue: Map<Value, Set<AidHash> list>) (anonymizationParams: AnonymizationParams) =
  // These values are safe, and can be counted as they are
  // without any additional noise.
  let lowCountValues, highCountValues =
    aidsPerValue
    |> Map.partition (fun _value aidSets -> isLowCount aidSets anonymizationParams)

  let byAid =
    [ 0 .. aidsCount - 1 ]
    |> List.map (
      transposeToPerAid lowCountValues
      >> countDistinctFlatteningByAid anonymizationParams
    )

  let safeCount = int64 highCountValues.Count

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

let count (anonymizationParams: AnonymizationParams) (perAidContributions: (Map<AidHash, float> * int64) list option) =
  match perAidContributions with
  | None -> Null
  | Some perAidContributions ->
      let byAid =
        perAidContributions
        |> List.map (fun (aidMap, unaccountedFor) ->
          aidMap |> Map.toList |> aidFlattening anonymizationParams unaccountedFor
        )

      // If any of the AIDs had insufficient data to produce a sensible flattening
      // we have to abort anonymization.
      if byAid |> List.exists ((=) None) then
        Null
      else
        byAid
        |> List.choose id
        |> anonymizedSum
        |> Option.map (round >> int64 >> Integer)
        |> Option.defaultValue Null
