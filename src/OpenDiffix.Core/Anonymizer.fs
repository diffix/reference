module OpenDiffix.Core.Anonymizer

open System
open OpenDiffix.Core.AnonymizerTypes

let private randomNum (rnd: Random) mean stdDev =
  let u1 = 1.0 - rnd.NextDouble()
  let u2 = 1.0 - rnd.NextDouble()

  let randStdNormal = Math.Sqrt(-2.0 * log (u1)) * Math.Sin(2.0 * Math.PI * u2)

  mean + stdDev * randStdNormal

let private newRandom (anonymizationParams: AnonymizationParams) = Random(anonymizationParams.Seed)

let private lowCountFilter (anonymizationParams: AnonymizationParams) rnd (rows: AnonymizableRow list) =
  match anonymizationParams.LowCountSettings with
  | None -> rows
  | Some lowCountParams ->
      rows
      |> List.groupBy (fun row -> row.Columns)
      |> List.filter (fun (_columns, instancesOfRow) ->
        let distinctUsersCount =
          instancesOfRow
          |> List.map (fun row -> row.AidValues)
          |> Set.unionMany
          |> Set.count

        let lowCountThreshold = randomNum rnd lowCountParams.Threshold lowCountParams.StdDev

        (float distinctUsersCount) >= lowCountThreshold
      )
      |> List.collect (fun (columns, instancesOfRow) -> instancesOfRow)

let anonymize (anonymizationParams: AnonymizationParams) (rows: AnonymizableRow list) =
  let rnd = newRandom anonymizationParams

  rows
  |> lowCountFilter anonymizationParams rnd
  |> List.map (fun anonymizedRow -> NonPersonalRow { Columns = anonymizedRow.Columns })
