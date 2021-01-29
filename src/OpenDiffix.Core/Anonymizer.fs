module OpenDiffix.Core.Anonymizer

open System
open OpenDiffix.Core.AnonymizerTypes

let private randomUniform (rnd: Random) lower upper = rnd.Next(lower, upper + 1)

let private randomNormal (rnd: Random) stdDev =
  let u1 = 1.0 - rnd.NextDouble()
  let u2 = 1.0 - rnd.NextDouble()

  let randStdNormal = Math.Sqrt(-2.0 * log (u1)) * Math.Sin(2.0 * Math.PI * u2)

  stdDev * randStdNormal

let private newRandom (anonymizationParams: AnonymizationParams) = Random(anonymizationParams.Seed)

let noisyCount (anonymizationParams: AnonymizationParams) count =
  let rnd = newRandom anonymizationParams
  let noiseParams = anonymizationParams.Noise

  let noise =
    noiseParams.StandardDev
    |> randomNormal rnd
    |> max -noiseParams.Cutoff
    |> min noiseParams.Cutoff
    |> round
    |> int

  max (count + noise) 0
