module OpenDiffix.Core.ParserTypes

type Constant =
  | Integer of int
  | String of string
  | Boolean of bool

type JoinType =
  | InnerJoin
  | LeftJoin
  | RightJoin
  | FullJoin

type SelectQuery =
  {
    SelectDistinct: bool
    Expressions: Expression list
    From: Expression
    Where: Expression option
    GroupBy: Expression list
    Having: Expression option
  }

and Expression =
  | Star
  | Null
  | Integer of int32
  | Float of float
  | String of string
  | Boolean of bool
  | Distinct of expression: Expression
  | And of left: Expression * right: Expression
  | Or of left: Expression * right: Expression
  | Not of expr: Expression
  | Lt of left: Expression * right: Expression
  | LtE of left: Expression * right: Expression
  | Gt of left: Expression * right: Expression
  | GtE of left: Expression * right: Expression
  | Equals of left: Expression * right: Expression
  | As of expr: Expression * alias: string option
  | Identifier of tableName: string option * columnName: string
  | Table of name: string * alias: string option
  | Join of joinType: JoinType * left: Expression * right: Expression * on: Expression
  | SubQuery of subQuery: SelectQuery * alias: string
  | Function of functionName: string * Expression list
  | SelectQuery of SelectQuery
// Please notice the lack of the BETWEEN WHERE-clause construct. I couldn't get it to work!!! :/
// If added as a Ternary parser with "BETWEEN" and "AND" being the phrases to look for, then
// the operator parser rejects the definition since it clashes with the regular AND operator parser.
// If instead it is defined as the infix operator BETWEEN requiring later validation that the
// right hand expression was a conjunction, then I couldn't get the precedence to work correctly.
// Regular AND binds loosely, whereas AND as the second term in BETWEEN binds very tightly.
// I couldn't get the parse tree to be anything but nonsensical using this approach.
