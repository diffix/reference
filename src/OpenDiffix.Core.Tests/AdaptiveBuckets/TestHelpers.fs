[<AutoOpen>]
module OpenDiffix.Core.AdaptiveBuckets.TestHelpers

open System

open OpenDiffix.Core
open OpenDiffix.Core.AdaptiveBuckets.Forest
open OpenDiffix.Core.AdaptiveBuckets.Bucket
open OpenDiffix.Core.AdaptiveBuckets.Microdata

let defaultAnonContext =
  {
    AnonymizationParams = AnonymizationParams.Default
    BucketSeed = 0UL
    BaseLabels = []
  }

let noiselessAnonContext =
  { defaultAnonContext with
      AnonymizationParams =
        { AnonymizationParams.Default with
            LayerNoiseSD = 0.
            Suppression = { LowThreshold = 3; LayerSD = 0.; LowMeanGap = 0. }
            OutlierCount = { Lower = 1; Upper = 1 }
            TopCount = { Lower = 1; Upper = 1 }
        }
  }

let makeTimestamp (year, month, day) (hour, minute, second) =
  Timestamp(DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc))

let processDataWithParams anonContext columns rows =
  let columnTypes = columns |> List.map (fun column -> column.Type)
  let microdataColumns = extractValueMaps columnTypes rows
  let forest, nullMappings = rows |> buildForest anonContext columns.Length

  forest
  |> harvestBuckets
  |> generateMicrodata microdataColumns nullMappings
  |> Seq.map Array.tail // Drop the dummy AID instances field.
  |> Seq.toList

let processData columns rows =
  processDataWithParams defaultAnonContext columns rows
