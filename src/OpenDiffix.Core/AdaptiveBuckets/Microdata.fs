module OpenDiffix.Core.AdaptiveBuckets.Microdata

open System.Collections.Generic

open OpenDiffix.Core
open OpenDiffix.Core.AdaptiveBuckets.Range
open OpenDiffix.Core.AdaptiveBuckets.Bucket

type ValueMap = IDictionary<float, Value>

// Contains data required to generate microdata for a given data column.
type MicrodataColumn =
  // Requires only the floating point ranges to generate microdata.
  | Simple of ExpressionType
  // Requires an additional reverse lookup table to generate microdata values.
  | Mapped of ValueMap

type MicrodataColumns = MicrodataColumn seq

// This source of randomness isn't sticky, so can only be applied to already anonymized data.
let private nonStickyRng = System.Random(0)

let private generateFloat (range: Range) =
  range.Min + nonStickyRng.NextDouble() * range.Size()

let private castFloatToType (columnType: ExpressionType) (value: float) =
  match columnType with
  | BooleanType -> Boolean(value >= 0.5)
  | IntegerType -> Integer(int64 value)
  | RealType -> Real value
  | TimestampType -> Timestamp(TIMESTAMP_REFERENCE + System.TimeSpan.FromSeconds(value))
  | _ -> failwith "Cannot cast float to type"

let private generateField (abColumn: MicrodataColumn) (nullMapping: float) (range: Range) =
  if nullMapping = range.Min then
    assert range.IsSingularity()
    Null
  else
    match abColumn with
    | Simple expressionType -> range |> generateFloat |> castFloatToType expressionType
    | Mapped valueMap -> if range.IsSingularity() then valueMap[range.Min] else String "*"

let private generateRow microdataColumns nullMappings ranges _index =
  Seq.map3 generateField microdataColumns nullMappings ranges
  |> Seq.append (Seq.singleton Null) // Set the AID instances field of a synthetic row to `Null`.
  |> Seq.toArray

// Extracts distinct String values from the data and produces a mapping from their float-representations
// back to String, which will be used in the microdata generation step
// Consider optimizing by only taking the values which become singularity ranges (i.e. Min=Max).
let extractValueMaps (columnTypes: ExpressionType seq) (rows: Row seq) =
  columnTypes
  |> Seq.mapi (fun index columnType ->
    if columnType = StringType then
      let valueMap =
        rows
        |> Seq.map (fun row -> row.[index + 1])
        |> Seq.distinct
        |> Seq.collect (fun v ->
          match v with
          | String s -> [ hashStringToFloat s, v ]
          | Null -> []
          | _ -> failwith "Unexpected Value converted to hash-float"
        )
        |> dict

      Mapped valueMap
    else
      Simple columnType
  )

let generateMicrodata (microdataColumns: MicrodataColumns) (nullMappings: float array) (buckets: Buckets) =
  buckets
  |> Seq.collect (fun bucket -> Seq.init (int bucket.Count) (generateRow microdataColumns nullMappings bucket.Ranges))
