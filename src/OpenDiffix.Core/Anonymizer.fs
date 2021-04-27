module OpenDiffix.Core.Anonymizer

open System
open OpenDiffix.Core.AnonymizerTypes

let private randomUniform (rnd: Random) (threshold: Threshold) = rnd.Next(threshold.Lower, threshold.Upper + 1)

let private randomNormal (rnd: Random) stdDev =
  let u1 = 1.0 - rnd.NextDouble()
  let u2 = 1.0 - rnd.NextDouble()

  let randStdNormal = Math.Sqrt(-2.0 * log u1) * Math.Sin(2.0 * Math.PI * u2)

  stdDev * randStdNormal

let private newRandom (anonymizationParams: AnonymizationParams) (aidSet: AidHash seq) =
  let combinedAids = Seq.fold (^^^) 0 aidSet
  let seed = combinedAids ^^^ anonymizationParams.Seed
  Random(seed)

let private noiseValue rnd (noiseParam: NoiseParam) =
  let absoluteCutoff = noiseParam.Cutoff * noiseParam.StandardDev

  noiseParam.StandardDev
  |> randomNormal rnd
  |> max -absoluteCutoff
  |> min absoluteCutoff

let private noiseValueInt rnd (noiseParam: NoiseParam) = noiseValue rnd noiseParam |> round |> int32

let isLowCount (aidSets: Set<AidHash> list) (anonymizationParams: AnonymizationParams) =
  aidSets
  |> List.map (fun aidSet ->
    if aidSet.Count = 0 then
      true
    else
      let rnd = newRandom anonymizationParams aidSet

      let threshold =
        randomUniform
          rnd
          {
            Lower = anonymizationParams.MinimumAllowedAids
            Upper = anonymizationParams.MinimumAllowedAids + 2
          }

      aidSet.Count < threshold
  )
  |> List.reduce (||)

type private AidCount = { FlattenedSum: float; Flattening: float; noise: NoiseParam; Rnd: Random }

let private aidFlattening
  (anonymizationParams: AnonymizationParams)
  (aidContributions: (AidHash * int64) list)
  : AidCount option =
  let rnd = aidContributions |> List.map fst |> newRandom anonymizationParams

  let outlierCount = randomUniform rnd anonymizationParams.OutlierCount
  let topCount = randomUniform rnd anonymizationParams.TopCount

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
    let flattenedSum = float summedContributions - flattening
    let flattenedAvg = flattenedSum / float sortedUserContributions.Length

    let noiseScale = max flattenedAvg (0.5 * topGroupAverage)
    let noiseSD = anonymizationParams.Noise.StandardDev * noiseScale

    Some
      {
        FlattenedSum = flattenedSum
        Flattening = flattening
        noise = { anonymizationParams.Noise with StandardDev = noiseSD }
        Rnd = rnd
      }

let transposePerAidMapsToPerValue (valuesPerAid: Map<AidHash, Set<Value>>) : Map<Value, Set<AidHash>> =
  valuesPerAid
  |> Map.toList
  |> List.collect (fun (aidHash, valuesSet) -> valuesSet |> Set.toList |> List.map (fun value -> value, aidHash))
  |> List.fold
    (fun acc (value, aidHash) ->
      Map.change value (Option.map (Set.add aidHash) >> Option.orElse (Some(Set.singleton aidHash))) acc
    )
    Map.empty

let transposeToPerValue (perAidTypeValueMap: Map<AidHash, Set<Value>> list) : Map<Value, Set<AidHash> list> =
  perAidTypeValueMap
  |> List.map transposePerAidMapsToPerValue
  |> List.fold
    (fun acc valueHashMap ->
      valueHashMap
      |> Map.fold
        (fun valueAcc value aidHashSet ->
          Map.change
            value
            (Option.map (fun existingAidSets -> existingAidSets @ [ aidHashSet ])
             >> Option.orElse (Some [ aidHashSet ]))
            valueAcc
        )
        acc
    )
    Map.empty

let rec distributeValues =
  function
  | [] -> [] // Done :D
  | (_aid, []) :: restValuesByAID -> distributeValues restValuesByAID
  | (aid, value :: restValues) :: restValuesByAID ->
      let restValuesByAID = // Drop current value from the remaining items.
        List.map (fun (aid, values) -> aid, values |> List.filter ((<>) value)) restValuesByAID

      (aid, value) :: distributeValues (restValuesByAID @ [ aid, restValues ])

let private countDistinctFlatteningByAid
  anonParams
  valuesPassingLowCount
  (perAidContributions: Map<AidHash, Set<Value>>)
  =
  perAidContributions
  |> Map.map (fun _aidHash valuesSet -> // keep low count values in sorted order
    Set.toList (valuesSet - valuesPassingLowCount)
  )
  |> Map.filter (fun _aidHash values -> values.Length > 0)
  |> Map.toList
  |> List.sortBy (fun (aid, values) -> values.Length, aid)
  |> distributeValues
  |> List.countBy fst
  |> List.map (fun (aid, count) -> aid, int64 count)
  |> aidFlattening anonParams

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
    |> List.sortByDescending (fun aggregate -> aggregate.noise.StandardDev)
    |> List.tryHead
    |> Option.map (fun aggregate -> noiseValue aggregate.Rnd aggregate.noise)

  match aidForFlattening, noise with
  | Some flattening, Some noise -> Some <| flattening.FlattenedSum + noise
  | _ -> None

let countDistinct (perAidValuesByAidType: Map<AidHash, Set<Value>> list) (anonymizationParams: AnonymizationParams) =
  // These values are safe, and can be counted as they are
  // without any additional noise.
  let valuesPassingLowCount =
    perAidValuesByAidType
    |> transposeToPerValue
    |> Map.toList
    |> List.filter (fun (_value, aidSets) -> not <| isLowCount aidSets anonymizationParams)
    |> List.map fst
    |> Set.ofList

  let safeCount = Set.count valuesPassingLowCount

  let byAid =
    perAidValuesByAidType
    |> List.map (countDistinctFlatteningByAid anonymizationParams valuesPassingLowCount)

  // If any of the AIDs had insufficient data to produce a sensible flattening
  // we can only report the count of values we already know to be safe as they
  // individually passed low count filtering.
  if byAid |> List.exists ((=) None) then
    if safeCount > 0 then safeCount |> int64 |> Integer else Null
  else
    byAid
    |> List.choose id
    |> anonymizedSum
    |> Option.defaultValue 0.
    |> fun flattenedCount -> float safeCount + flattenedCount |> round |> max 0. |> int64 |> Integer

let count (anonymizationParams: AnonymizationParams) (perAidContributions: Map<AidHash, int64> list option) =
  match perAidContributions with
  | None -> Null
  | Some perAidContributions ->
      let byAid =
        perAidContributions
        |> List.map (Map.toList >> aidFlattening anonymizationParams)

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
