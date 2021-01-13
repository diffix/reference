namespace OpenDiffix.Core.ParserTypes

type Constant =
  | Integer of int
  | String of string
  | Boolean of bool

type Operator =
  | Not      // not
  | Plus     // +
  | Minus    // -
  | Star     // *
  | Slash    // /
  | Hat      // ^
  | Equal    // =
  | NotEqual // <>
  | LT       // <
  | GT       // >
  | And      // and
  | Or       // or

type Expression =
  | Operator of Operator
  | Constant of Constant
  | Term of termName: string
  | AliasedTerm of term: Expression * aliasName: string
  | Function of functionName: string * Expression list

type From = Table of tableName: string

[<RequireQualifiedAccess>]
type Condition =
  | Not of Expression
  | IsTrue of Expression
  | Equal of Expression * Expression
  | NotEqual of Expression * Expression
  | GT of Expression * Expression
  | LT of Expression * Expression
  | Between of Expression * Expression * Expression
  | And of Condition * Condition
  | Or of Condition * Condition

type SelectQuery = {
  Expressions: Expression list
  From: From
  Where: Condition option
  GroupBy: Expression list
}

[<RequireQualifiedAccess>]
type ShowQuery =
  | Tables
  | Columns of tableName: string

type Query =
  | Show of ShowQuery
  | SelectQuery of SelectQuery
