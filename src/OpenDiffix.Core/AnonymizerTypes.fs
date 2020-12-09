namespace OpenDiffix.Core.AnonymizerTypes

type LowCountSettings =
  { Threshold: float
    StdDev: float }

  static member Defaults = { Threshold = 5.; StdDev = 2. }

type AnonymizationParams =
  { AidColumnOption: string option
    Seed: int
    LowCountSettings: LowCountSettings option }

type RequestParams =
  { AnonymizationParams: AnonymizationParams
    Query: string
    DatabasePath: string }

type ColumnName = string

type ColumnValue =
  | IntegerValue of int
  | StringValue of string

type NonPersonalColumnCell =
  { ColumnName: string
    ColumnValue: ColumnValue }

type ColumnCell =
  { ColumnName: string
    ColumnValue: ColumnValue }

type AnonymizableColumnCell = { AidValue: ColumnValue Set }

type AnonymizableRow =
  { AidValues: ColumnValue Set
    Columns: ColumnCell list }

type NonPersonalRow = { Columns: ColumnCell list }

type Row =
  | AnonymizableRow of AnonymizableRow
  | NonPersonalRow of NonPersonalRow

type QueryResult = ResultTable of Row list

type QueryError =
  | ParseError of string
  | DbNotFound
  | InvalidRequest of string
  | ExecutionError of string
  | UnexpectedError of string
