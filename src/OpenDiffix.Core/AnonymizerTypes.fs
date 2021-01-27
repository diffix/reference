namespace OpenDiffix.Core.AnonymizerTypes

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

type TableSettings = { AidColumns: string list }

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

type RequestParams =
  {
    AnonymizationParams: AnonymizationParams
    Query: string
    DatabasePath: string
  }

type ColumnName = string
type Columns = ColumnName list

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

type QueryResult = { Columns: Columns; Rows: NonPersonalRows }

type QueryError = string
