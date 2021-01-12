namespace OpenDiffix.Core.ParserTypes

type Constant =
  | Integer of int
  | String of string
  | Boolean of bool

type Operator =
  | Not   // not
  | Plus  // +
  | Minus // -
  | Star  // *
  | Slash // /
  | Hat   // ^

type Expression =
  | Operator of Operator
  | Constant of Constant
  | Term of termName: string
  | AliasedTerm of term: Expression * aliasName: string
  | Function of functionName: string * Expression list

type From = Table of tableName: string

type SelectQuery = {
  Expressions: Expression list
  From: From
  GroupBy: Expression list
}

[<RequireQualifiedAccess>]
type ShowQuery =
  | Tables
  | Columns of tableName: string

type Query =
  | Show of ShowQuery
  | SelectQuery of SelectQuery
