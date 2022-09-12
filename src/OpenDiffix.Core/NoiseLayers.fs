module OpenDiffix.Core.NoiseLayers

open AnalyzerTypes

let private basicSeedMaterial rangeColumns expression =
  match expression with
  | ColumnReference (index, _type) ->
    let rangeColumn = List.item index rangeColumns
    $"%s{rangeColumn.RangeName}.%s{rangeColumn.ColumnName}"
  | Constant (String value) -> value
  | Constant (Integer value) -> value.ToString()
  | Constant (Real value) -> value.ToString()
  | Constant (Timestamp value) -> (Timestamp value) |> Value.toString
  | Constant (Boolean value) -> value.ToString()
  | _ -> failwith "Unsupported expression used for defining buckets."

let private functionSeedMaterial =
  function
  | Substring -> "substring"
  | CeilBy -> "ceil"
  | FloorBy -> "floor"
  | RoundBy -> "round"
  | WidthBucket -> "width_bucket"
  | DateTrunc -> "date_trunc"
  | Extract -> "extract"
  | _ -> failwith "Unsupported function used for defining buckets."

let private collectSeedMaterials rangeColumns expression =
  match expression with
  | FunctionExpr (ScalarFunction fn, args) -> functionSeedMaterial fn :: List.map (basicSeedMaterial rangeColumns) args
  | Constant _ -> failwith "Constant expressions can not be used for defining buckets."
  | _ -> [ basicSeedMaterial rangeColumns expression ]
  |> String.join ","

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let computeSQLSeed rangeColumns normalizedBucketExpressions =
  normalizedBucketExpressions
  |> Seq.map (collectSeedMaterials rangeColumns)
  |> Hash.strings 0UL
