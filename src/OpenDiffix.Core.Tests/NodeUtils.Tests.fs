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

let anonContext =
  {
    BucketSeed = 0UL
    BaseLabels = []
    AnonymizationParams = AnonymizationParams.Default
  }

let selectQuery =
  {
    TargetList = [ { Expression = expression; Alias = "col"; Tag = RegularTargetEntry } ]
    Where = expression
    From = RangeTable(testTable, testTable.Name)
    GroupBy = [ expression ]
    Having = expression
    OrderBy = [ OrderBy(expression, Ascending, NullsFirst) ]
    Limit = None
    AnonymizationContext = Some anonContext
  }

let selectQueryNegative =
  {
    TargetList = [ { Expression = negativeExpression; Alias = "col"; Tag = RegularTargetEntry } ]
    Where = negativeExpression
    From = RangeTable(testTable, testTable.Name)
    GroupBy = [ negativeExpression ]
    Having = negativeExpression
    OrderBy = [ OrderBy(negativeExpression, Ascending, NullsFirst) ]
    Limit = None
    AnonymizationContext = Some anonContext
  }

[<Fact>]
let ``Map expressions`` () =
  let data =
    selectQuery
    |> NodeUtils.map (
      function
      | Constant(Boolean true) -> Constant(Boolean false)
      | other -> other
    )

  should equal selectQueryNegative data
