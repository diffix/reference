module OpenDiffix.Core.NodeUtilsTests

open Xunit
open FsUnit.Xunit

open AnalyzerTypes

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
    TargetList = [ { Expression = expression; Alias = "col"; Tag = RegularTargetEntry } ]
    Where = expression
    From = RangeTable(testTable, testTable.Name)
    GroupingSets = [ GroupingSet [ expression ] ]
    Having = expression
    OrderBy = [ OrderBy(expression, Ascending, NullsFirst) ]
    Limit = None
  }

let selectQueryNegative =
  {
    TargetList = [ { Expression = negativeExpression; Alias = "col"; Tag = RegularTargetEntry } ]
    Where = negativeExpression
    From = RangeTable(testTable, testTable.Name)
    GroupingSets = [ GroupingSet [ negativeExpression ] ]
    Having = negativeExpression
    OrderBy = [ OrderBy(negativeExpression, Ascending, NullsFirst) ]
    Limit = None
  }

[<Fact>]
let ``Map expressions`` () =
  let data =
    selectQuery
    |> NodeUtils.map (
      function
      | Constant (Boolean true) -> Constant(Boolean false)
      | other -> other
    )

  should equal selectQueryNegative data
