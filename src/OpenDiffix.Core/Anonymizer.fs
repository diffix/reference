module OpenDiffix.Core.Anonymizer

open System
open OpenDiffix.Core.AnonymizerTypes

let private randomUniform (rnd: Random) (threshold: Threshold) = rnd.Next(threshold.Lower, threshold.Upper + 1)

let private randomNormal (rnd: Random) stdDev =
  let u1 = 1.0 - rnd.NextDouble()
  let u2 = 1.0 - rnd.NextDouble()

  let randStdNormal = Math.Sqrt(-2.0 * log u1) * Math.Sin(2.0 * Math.PI * u2)

  stdDev * randStdNormal

let private newRandom (aidSet: Set<AidHash>) (anonymizationParams: AnonymizationParams) =
  let combinedAids = aidSet |> Set.toList |> List.reduce (^^^)
  let seed = combinedAids ^^^ anonymizationParams.Seed
  Random(seed)

let private noiseValue rnd (noiseParam: NoiseParam) =
  noiseParam.StandardDev
  |> randomNormal rnd
  |> max -noiseParam.Cutoff
  |> min noiseParam.Cutoff

let private noiseValueInt rnd (noiseParam: NoiseParam) = noiseValue rnd noiseParam |> round |> int32

let countAids (aidSets: Set<AidHash> array option) (anonymizationParams: AnonymizationParams) =
  match aidSets with
  | None -> 0 // The result set was entirely empty, no aggregation state was created
  | Some aidSets when Set.isEmpty (Array.head aidSets) -> 0 // Is this right? Should it be Null instead?
  | Some aidSets ->
      let aidSet = aidSets |> Array.head
      let rnd = newRandom aidSet anonymizationParams
      let noise = noiseValueInt rnd anonymizationParams.Noise
      max (aidSet.Count + noise) 0

let isLowCount (aidSets: Set<AidHash> array option) (anonymizationParams: AnonymizationParams) =
  match aidSets with
  | None -> true
  | Some aidSets ->
      aidSets
      |> Array.map (fun aidSet ->
        if aidSet.Count = 0 then
          true
        else
          let rnd = newRandom aidSet anonymizationParams

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

type private AidCount = { NoisyCount: float; Flattening: float }

let private aidFlattening
  (anonymizationParams: AnonymizationParams)
  (aidContributions: Map<AidHash, int64>)
  : AidCount option =
  match Map.toList aidContributions with
  | [] -> None
  | perAidContributions ->
      let aids = perAidContributions |> List.map fst |> Set.ofList
      let rnd = newRandom aids anonymizationParams

      // The noise value must be generated first to make sure the random number generator is fresh.
      // This ensures count(distinct aid) which uses addNoise directly produces the same results.
      let noise = noiseValue rnd anonymizationParams.Noise

      let sortedUserContributions = perAidContributions |> List.map snd |> List.sortDescending

      let outlierCount = randomUniform rnd anonymizationParams.OutlierCount
      let topCount = randomUniform rnd anonymizationParams.TopCount

      if sortedUserContributions.Length < outlierCount + topCount then
        None
      else
        let outliersSummed = sortedUserContributions |> List.take outlierCount |> List.sum

        let topValueSummed =
          sortedUserContributions
          |> List.skip outlierCount
          |> List.take topCount
          |> List.sum

        let topValueAverage = (float topValueSummed) / (float topCount)
        let outlierReplacement = topValueAverage * (float outlierCount)

        let totalCount = sortedUserContributions |> List.sum
        let flattening = float outliersSummed - outlierReplacement
        let noisyCount = float totalCount - flattening + noise |> max 0.

        Some { NoisyCount = noisyCount; Flattening = flattening }


let count (anonymizationParams: AnonymizationParams) (perUserContributions: Map<AidHash, int64> array option) =
  match perUserContributions with
  | None -> Null
  | Some perUserContributions ->
      let results =
        perUserContributions
        |> Array.toList
        |> List.map (aidFlattening anonymizationParams)
        |> List.choose id
        |> List.sortByDescending (fun aggregate -> aggregate.Flattening)

      match results with
      | [] -> Null
      | flattenedCount :: _ -> flattenedCount.NoisyCount |> round |> int64 |> Integer
