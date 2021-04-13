module OpenDiffix.Core.Tests.AnalyzerTypesTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnalyzerTypes

let testTable : Table =
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
    TargetTables = [ testTable, testTable.Name ]
    Where = expression
    From = Table(testTable, testTable.Name)
    GroupingSets = [ GroupingSet [ expression ] ]
    Having = expression
    OrderBy = [ OrderBy(expression, Ascending, NullsFirst) ]
  }

let selectQueryNegative =
  {
    Columns = [ { Expression = negativeExpression; Alias = "col" } ]
    TargetTables = [ testTable, testTable.Name ]
    Where = negativeExpression
    From = Table(testTable, testTable.Name)
    GroupingSets = [ GroupingSet [ negativeExpression ] ]
    Having = negativeExpression
    OrderBy = [ OrderBy(negativeExpression, Ascending, NullsFirst) ]
  }

[<Fact>]
let ``Map expressions`` () =
  let data =
    SelectQuery.Map(
      selectQuery,
      (function
      | Constant (Boolean true) -> Constant(Boolean false)
      | other -> other)
    )

  should equal selectQueryNegative data
