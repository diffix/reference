namespace OpenDiffix.Core.AnonymizerTypes

open Thoth.Json.Net

type Threshold =
  {
    Lower: int
    Upper: int
  }

  static member Default = { Lower = 2; Upper = 5 }

type NoiseParam =
  {
    StandardDev: float
    Cutoff: float
  }

  static member Default = { StandardDev = 2.; Cutoff = 5. }

type TableSettings =
  {
    AidColumns: string list
  }

type AnonymizationParams =
  {
    TableSettings: Map<string, TableSettings>
    Seed: int
    LowCountThreshold: Threshold

    // Count params
    OutlierCount: Threshold
    TopCount: Threshold
    Noise: NoiseParam
  }

type ColumnValue =
  | IntegerValue of int
  | StringValue of string
  | NullValue

  static member ToString =
    function
    | IntegerValue value -> $"%i{value}"
    | StringValue value -> value
    | NullValue -> "<null>"

type NonPersonalRow = ColumnValue list
type NonPersonalRows = NonPersonalRow list
type PersonalRow = { AidValues: ColumnValue Set; RowValues: NonPersonalRow }
type PersonalRows = PersonalRow list
