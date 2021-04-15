module OpenDiffix.Core.Anonymizer

open System
open OpenDiffix.Core.AnonymizerTypes

let private randomUniform (rnd: Random) (threshold: Threshold) = rnd.Next(threshold.Lower, threshold.Upper + 1)

let private randomNormal (rnd: Random) stdDev =
  let u1 = 1.0 - rnd.NextDouble()
  let u2 = 1.0 - rnd.NextDouble()

  let randStdNormal = Math.Sqrt(-2.0 * log u1) * Math.Sin(2.0 * Math.PI * u2)

  stdDev * randStdNormal

let private newRandom (anonymizationParams: AnonymizationParams) (aidSet: Set<AidHash>) =
  let combinedAids = aidSet |> Set.toList |> List.reduce (^^^)
  let seed = combinedAids ^^^ anonymizationParams.Seed
  Random(seed)

let private noiseValue rnd (noiseParam: NoiseParam) =
  noiseParam.StandardDev
  |> randomNormal rnd
  |> max -noiseParam.Cutoff
  |> min noiseParam.Cutoff

let private noiseValueInt rnd (noiseParam: NoiseParam) = noiseValue rnd noiseParam |> round |> int32

let isLowCount (aidSets: Set<AidHash> array) (anonymizationParams: AnonymizationParams) =
  aidSets
  |> Array.map (fun aidSet ->
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
  |> Array.reduce (||)

type private AidCount = { FlattenedSum: float; Flattening: float; NoiseParam: NoiseParam; Rnd: Random }

let private aidFlattening
  (anonymizationParams: AnonymizationParams)
  (aidContributions: Map<AidHash, int64>)
  : AidCount option =
  match Map.toList aidContributions with
  | [] -> None
  | perAidContributions ->
      let rnd =
        perAidContributions
        |> List.map fst
        |> Set.ofList
        |> newRandom anonymizationParams

      let outlierCount = randomUniform rnd anonymizationParams.OutlierCount
      let topCount = randomUniform rnd anonymizationParams.TopCount

      let sortedUserContributions = perAidContributions |> List.map snd |> List.sortDescending

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

        let totalCount = sortedUserContributions |> List.sum
        let flattening = float outliersSummed - outlierReplacement
        let noisyCount = float totalCount - flattening

        let noiseParam =
          {
            StandardDev = topGroupAverage |> max anonymizationParams.Noise.StandardDev
            Cutoff = 3. * topGroupAverage |> max anonymizationParams.Noise.Cutoff
          }

        Some
          {
            FlattenedSum = noisyCount
            Flattening = flattening
            NoiseParam = noiseParam
            Rnd = rnd
          }

let transposePerAidMapsToPerValue (valuesPerAid: Map<AidHash, Set<Value>>) : Map<Value, Set<AidHash>> =
  valuesPerAid
  |> Map.toList
  |> List.collect (fun (aidHash, valuesSet) -> valuesSet |> Set.toList |> List.map (fun value -> value, aidHash))
  |> List.fold
    (fun acc (value, aidHash) ->
      Map.change
        value
        (function
        | None -> Some <| Set.singleton aidHash
        | Some aidSet -> Some <| Set.add aidHash aidSet)
        acc
    )
    Map.empty

let transposeToPerValue (perAidTypeValueMap: Map<AidHash, Set<Value>> array) : Map<Value, Set<AidHash> array> =
  perAidTypeValueMap
  |> Array.map transposePerAidMapsToPerValue
  |> Array.fold
    (fun (acc: Map<Value, Set<AidHash> array>) (valueHashMap: Map<Value, Set<AidHash>>) ->
      valueHashMap
      |> Map.toList
      |> List.fold
        (fun (valueAcc: Map<Value, Set<AidHash> array>) (value, aidHashSet) ->
          let a = Array.singleton aidHashSet

          Map.change
            value
            (function
            | None -> Some a
            | Some existingAidSets -> Some <| Array.append existingAidSets a)
            valueAcc
        )
        acc
    )
    Map.empty

let rec distributeUntilEmpty takenValues queue itemsByAID =
  match itemsByAID, queue with
  | [], [] -> [] // Done :D
  | [], _ -> distributeUntilEmpty takenValues [] (List.rev queue)

  | (aid, values) :: rest, _ ->
      match values |> List.tryFind (fun value -> not <| Set.contains value takenValues) with
      | Some value ->
          let updatedTaken = Set.add value takenValues

          match values |> List.filter ((<>) value) with
          | [] -> (aid, value) :: distributeUntilEmpty updatedTaken queue rest
          | remaining ->
              let queue = (aid, remaining) :: queue
              (aid, value) :: distributeUntilEmpty updatedTaken queue rest
      | None ->
          // No more value to take for user...
          distributeUntilEmpty takenValues queue rest

let private countDistinctFlatteningByAid
  anonParams
  valuesPassingLowCount
  (perAidContributions: Map<AidHash, Set<Value>>)
  =
  let distributableValues =
    perAidContributions
    |> Map.map (fun _aidHash valuesSet -> Set.difference valuesSet valuesPassingLowCount)
    |> Map.toList
    |> List.sortBy (fun (aid, valuesSet) -> Set.count valuesSet, aid)
    |> List.map (fun (aid, valuesSet) -> aid, Set.toList valuesSet |> List.sort)

  distributableValues
  |> distributeUntilEmpty Set.empty []
  |> List.groupBy fst
  |> List.map (fun (aid, values) -> aid, List.length values |> int64)
  |> Map.ofList
  |> aidFlattening anonParams

let private flattenedAndNoisySum (byAidSum: AidCount option []) =
  let values = byAidSum |> Array.choose id

  let aidForFlattening =
    values
    |> Array.sortByDescending (fun aggregate -> aggregate.Flattening)
    |> Array.head

  let noise =
    values
    |> Array.sortByDescending (fun aggregate -> aggregate.NoiseParam.StandardDev)
    |> Array.head
    |> fun flatteningResult -> noiseValue flatteningResult.Rnd flatteningResult.NoiseParam

  aidForFlattening.FlattenedSum + noise

let countDistinct (perAidValuesByAidType: Map<AidHash, Set<Value>> array) (anonymizationParams: AnonymizationParams) =
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
    |> Array.map (countDistinctFlatteningByAid anonymizationParams valuesPassingLowCount)

  if byAid |> Array.exists ((=) None) then
    if safeCount > 0 then safeCount |> int64 |> Integer else Null
  else
    let flattenedCount = flattenedAndNoisySum byAid
    float safeCount + flattenedCount |> round |> max 0. |> int64 |> Integer

let count (anonymizationParams: AnonymizationParams) (perAidContributions: Map<AidHash, int64> array option) =
  match perAidContributions with
  | None -> Null
  | Some perAidContributions ->
      let byAid = perAidContributions |> Array.map (aidFlattening anonymizationParams)

      // If any of the AIDs had insufficient data to produce a sensible flattening
      // we have to abort anonymization.
      if byAid |> Array.exists ((=) None) then
        Null
      else
        flattenedAndNoisySum byAid |> round |> int64 |> Integer
