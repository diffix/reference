module OpenDiffix.Core.Tests.AnalyzerTypes_Tests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

let testTable: Table =
  {
    Name = "table"
    Columns =
      [
        { Name = "str_col"; Type = StringType }
        { Name = "int_col"; Type = IntegerType }
        { Name = "float_col"; Type = RealType }
        { Name = "bool_col"; Type = BooleanType }
      ]
  }

let expression = Boolean true |> Constant
let negativeExpression = Boolean false |> Constant

let selectQuery =
  {
    Columns = [ { Expression = expression; Alias = "col" } ]
    Where = expression
    From = Table testTable
    GroupingSets = [ [ expression ] ]
    Having = expression
    OrderBy = [ expression, Ascending, NullsFirst ]
  }

let selectQueryNegative =
  {
    Columns = [ { Expression = negativeExpression; Alias = "col" } ]
    Where = negativeExpression
    From = Table testTable
    GroupingSets = [ [ negativeExpression ] ]
    Having = negativeExpression
    OrderBy = [ negativeExpression, Ascending, NullsFirst ]
  }

[<Fact>]
let ``Map expressions`` () =
  let data =
    SelectQuery.mapExpressions
      (function
      | Constant (Boolean true) -> Constant(Boolean false)
      | other -> other) selectQuery

  should equal selectQueryNegative data
