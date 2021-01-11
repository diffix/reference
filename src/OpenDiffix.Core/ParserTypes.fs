namespace OpenDiffix.Core.ParserTypes

type Constant =
  | Integer of int
  | String of string

type ColumnName = ColumnName of string
type TableName = TableName of string

type ColumnType =
  | PlainColumn of ColumnName
  | AliasedColumn of ColumnName * string

type AggregateFunctionArgs = Distinct of ColumnName

type AggregateFunction = AnonymizedCount of AggregateFunctionArgs

type Expression =
  | Constant of Constant
  | Column of ColumnType
  | Function of (string * Expression)
  | AggregateFunction of AggregateFunction

type From = Table of TableName

type SelectQuery = { Expressions: Expression list; From: From }

type AggregateQuery = { Expressions: Expression list; From: From; GroupBy: ColumnName list }

[<RequireQualifiedAccess>]
type ShowQuery =
  | Tables
  | Columns of TableName

type Query =
  | Show of ShowQuery
  // Data extraction
  | SelectQuery of SelectQuery
  | AggregateQuery of AggregateQuery
