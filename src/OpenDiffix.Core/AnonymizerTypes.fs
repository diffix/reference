namespace OpenDiffix.Core.AnonymizerTypes

type LowCountSettings =
  {
    Threshold: float
    StdDev: float
  }

  static member Defaults = { Threshold = 5.; StdDev = 2. }

type TableSettings = { AidColumns: string list }

type AnonymizationParams =
  {
    TableSettings: Map<string, TableSettings>
    Seed: int
    LowCountSettings: LowCountSettings option
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

type NonPersonalRow = ColumnValue list
type NonPersonalRows = NonPersonalRow list
type PersonalRow = { AidValues: ColumnValue Set; RowValues: NonPersonalRow }
type PersonalRows = PersonalRow list

type QueryResult = { Columns: Columns; Rows: NonPersonalRows }

type QueryError =
  | ParseError of string
  | DbNotFound
  | InvalidRequest of string
  | ExecutionError of string
  | UnexpectedError of string
