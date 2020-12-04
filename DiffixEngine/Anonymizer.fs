module DiffixEngine.Anonymizer

open DiffixEngine.Types
open System

let private randomNum (rnd: Random) mean stdDev = 
  let u1 = 1.0-rnd.NextDouble()
  let u2 = 1.0-rnd.NextDouble()
  let randStdNormal = Math.Sqrt(-2.0 * log(u1)) * Math.Sin(2.0 * Math.PI * u2)
  mean + stdDev * randStdNormal; 
  
let private newRandom (reqParams: RequestParams) =
  Random(reqParams.Seed)
  
let private lowCountFilter reqParams rnd (rows: AnonymizableRow list) =
  let rowsToReject =
    rows
    |> List.groupBy(fun row -> row.Columns)
    |> List.filter(fun (_columns, instancesOfRow) ->
      let distinctUsersCount =
        instancesOfRow
        |> List.map(fun row -> row.AidValues)
        |> Set.unionMany
        |> Set.count
      let lowCountThreshold = randomNum rnd reqParams.LowCountThreshold reqParams.LowCountThresholdStdDev
      (float distinctUsersCount) < lowCountThreshold
    )
    |> List.map(fst)
    |> Set.ofSeq
  rows
  |> List.filter(fun row ->
    not (Set.contains row.Columns rowsToReject)
  )
  
let anonymize reqParams (rows: AnonymizableRow list) =
  let rnd = newRandom reqParams
  rows
  |> lowCountFilter reqParams rnd
  |> List.map (fun anonymizedRow -> NonPersonalRow {Columns = anonymizedRow.Columns})