module OpenDiffix.Core.Anonymizer

open System
open OpenDiffix.Core.AnonymizerTypes

let private randomUniform (rnd: Random) (threshold: Threshold) = rnd.Next(threshold.Lower, threshold.Upper + 1)

let private randomNormal (rnd: Random) stdDev =
  let u1 = 1.0 - rnd.NextDouble()
  let u2 = 1.0 - rnd.NextDouble()

  let randStdNormal = Math.Sqrt(-2.0 * log (u1)) * Math.Sin(2.0 * Math.PI * u2)

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
  |> round
  |> int32

let countAids (aidSet: Set<AidHash>) (anonymizationParams: AnonymizationParams) =
  let rnd = newRandom aidSet anonymizationParams
  let noise = noiseValue rnd anonymizationParams.Noise
  max (aidSet.Count + noise) 0

let isLowCount (aidSet: Set<AidHash>) (anonymizationParams: AnonymizationParams) =
  let rnd = newRandom aidSet anonymizationParams
  let noise = noiseValue rnd anonymizationParams.Noise
  let threshold = randomUniform rnd anonymizationParams.LowCountThreshold

  aidSet.Count < anonymizationParams.LowCountAbsoluteLowerBound
  || aidSet.Count + noise <= threshold

let count (anonymizationParams: AnonymizationParams) (perUserContribution: Map<AidHash, int64>) =
  let aids = perUserContribution |> Map.toList |> List.map fst |> Set.ofList
  let rnd = newRandom aids anonymizationParams
  // The noise value must be generated first to make sure the random number generator is fresh.
  // This ensures count(distinct aid) which uses addNoise directly produces the same results.
  let noise = noiseValue rnd anonymizationParams.Noise

  let sortedUserContributions = perUserContribution |> Map.toList |> List.map snd |> List.sortDescending

  let outlierCount = randomUniform rnd anonymizationParams.OutlierCount
  let topCount = randomUniform rnd anonymizationParams.TopCount

  if sortedUserContributions.Length < outlierCount + topCount then
    Null
  else
    let topValueSummed =
      sortedUserContributions
      |> List.skip outlierCount
      |> List.take topCount
      |> List.sum

    let topValueAverage = (float topValueSummed) / (float topCount)
    let outlierReplacement = topValueAverage * (float outlierCount) |> int64

    let sumExcludingOutliers = sortedUserContributions |> List.skip (outlierCount) |> List.sum

    let totalCount = sumExcludingOutliers + outlierReplacement
    (max (totalCount + int64 noise) 0L) |> Integer
