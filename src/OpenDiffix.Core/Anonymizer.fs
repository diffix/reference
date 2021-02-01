module OpenDiffix.Core.Anonymizer

open System
open OpenDiffix.Core.AnonymizerTypes

let private randomUniform (rnd: Random) (threshold: Threshold) = rnd.Next(threshold.Lower, threshold.Upper + 1)

let private randomNormal (rnd: Random) stdDev =
  let u1 = 1.0 - rnd.NextDouble()
  let u2 = 1.0 - rnd.NextDouble()

  let randStdNormal = Math.Sqrt(-2.0 * log (u1)) * Math.Sin(2.0 * Math.PI * u2)

  stdDev * randStdNormal

let private newRandom (aidSet: Set<Value>) (anonymizationParams: AnonymizationParams) =
  Random(aidSet.GetHashCode() ^^^ anonymizationParams.Seed)

let private addNoise rnd (anonymizationParams: AnonymizationParams) value =
  let noiseParams = anonymizationParams.Noise

  let noise =
    noiseParams.StandardDev
    |> randomNormal rnd
    |> max -noiseParams.Cutoff
    |> min noiseParams.Cutoff
    |> round
    |> int

  max (value + noise) 0

let anonymousCount (anonymizationParams: AnonymizationParams) (perUserContribution: Map<Value, int>) =
  let aids = perUserContribution |> Map.toList |> List.map fst |> Set.ofList
  let rnd = newRandom aids anonymizationParams

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
    let outlierReplacement = topValueAverage * (float outlierCount) |> int

    let sumExcludingOutliers = sortedUserContributions |> List.skip (outlierCount) |> List.sum

    let totalCount = sumExcludingOutliers + outlierReplacement
    addNoise rnd anonymizationParams totalCount |> int64 |> Value.Integer
