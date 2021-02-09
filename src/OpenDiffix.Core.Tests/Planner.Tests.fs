module OpenDiffix.Core.PlannerTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core
open OpenDiffix.Core.PlannerTypes
open OpenDiffix.Core.AnalyzerTypes

let constTrue = Constant(Boolean true)

let table =
  {
    Name = "table"
    Columns =
      [ //
        { Name = "id"; Type = IntegerType }
        { Name = "name"; Type = StringType }
      ]
  }

let emptySelect =
  {
    Columns = []
    Where = constTrue
    From = Table table
    GroupingSets = []
    Having = constTrue
    OrderBy = []
  }

let column index =
  let column = table.Columns |> List.item index
  ColumnReference(index, column.Type)

let selectColumn index =
  let column = table.Columns |> List.item index
  { Expression = ColumnReference(index, column.Type); Alias = column.Name }

let countStar = FunctionExpr(AggregateFunction(Count, { Distinct = false; OrderBy = [] }), [])

let plus1 expression = FunctionExpr(ScalarFunction Plus, [ expression; Constant(Integer 1L) ])

[<Fact>]
let ``plan select`` () =
  let select = { emptySelect with Columns = [ selectColumn 0; selectColumn 1 ] }

  let expected = Plan.Project(Plan.Scan(table), [ column 0; column 1 ])

  SelectQuery select |> Planner.plan |> should equal expected

[<Fact>]
let ``plan where`` () =
  let condition = FunctionExpr(ScalarFunction Equals, [ column 1; Constant(String "abc") ])

  let select = { emptySelect with Where = condition }

  let expected = Plan.Project(Plan.Filter(Plan.Scan(table), condition), [])

  SelectQuery select |> Planner.plan |> should equal expected

[<Fact>]
let ``plan order by`` () =
  let orderBy = [ OrderBy (column 1, Ascending, NullsFirst) ]
  let select = { emptySelect with OrderBy = orderBy }

  let expected = Plan.Project(Plan.Sort(Plan.Scan(table), orderBy), [])

  SelectQuery select |> Planner.plan |> should equal expected

[<Fact>]
let ``plan aggregation`` () =
  let groupingSet = [ column 1 ]
  let selectedColumns = [ selectColumn 1; { Expression = countStar; Alias = "" } ]
  let select = { emptySelect with Columns = selectedColumns; GroupingSets = [ GroupingSet groupingSet ] }

  let expected =
    Plan.Project(
      Plan.Aggregate(Plan.Scan(table), groupingSet, [ countStar ]),
      [ ColumnReference(0, StringType); ColumnReference(1, IntegerType) ]
    )

  SelectQuery select |> Planner.plan |> should equal expected

[<Fact>]
let ``plan all`` () =
  let groupingSet = [ column 0 ]
  let selectedColumns = [ { Expression = plus1 (column 0); Alias = "" }; { Expression = countStar; Alias = "" } ]
  let whereCondition = FunctionExpr(ScalarFunction Equals, [ column 1; Constant(String "abc") ])
  let havingCondition = FunctionExpr(ScalarFunction Equals, [ countStar; Constant(Integer 0L) ])
  let orderBy = [ OrderBy(plus1 countStar, Ascending, NullsFirst) ]

  let select =
    { emptySelect with
        Columns = selectedColumns
        GroupingSets = [ GroupingSet groupingSet ]
        Where = whereCondition
        OrderBy = orderBy
        Having = havingCondition
    }

  let expected =
    Plan.Project(
      Plan.Sort(
        Plan.Filter(
          Plan.Aggregate(
            Plan.Filter(
              Plan.Scan(table),  //
              whereCondition
            ),
            groupingSet,
            [ countStar ]
          ),
          FunctionExpr(ScalarFunction Equals, [ ColumnReference(1, IntegerType); Constant(Integer 0L) ])
        ),
        [ OrderBy(plus1 (ColumnReference(1, IntegerType)), Ascending, NullsFirst) ]
      ),
      [ plus1 (ColumnReference(0, IntegerType)); ColumnReference(1, IntegerType) ]
    )

  SelectQuery select |> Planner.plan |> should equal expected

[<Fact>]
let ``sub-query plan`` () =
  let selectedColumns = [ selectColumn 1 ]
  let subQuery = { emptySelect with Columns = selectedColumns}
  let query =
    { subQuery with Columns = [ selectColumn 0 ]; From = Query <| SelectQuery subQuery }

  let expected = Plan.Project(Plan.Project(Plan.Scan(table), [ column 1 ]), [ column 0 ])

  SelectQuery query |> Planner.plan |> should equal expected
