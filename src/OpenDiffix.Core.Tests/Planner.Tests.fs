module OpenDiffix.Core.PlannerTests

open Xunit
open FsUnit.Xunit

open AnalyzerTypes

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
    TargetList = []
    Where = constTrue
    From = RangeTable(table, table.Name)
    GroupBy = []
    Having = constTrue
    OrderBy = []
    Limit = None
  }

let column index =
  let column = table.Columns |> List.item index
  ColumnReference(index, column.Type)

let selectColumn index =
  let column = table.Columns |> List.item index

  {
    Expression = ColumnReference(index, column.Type)
    Alias = column.Name
    Tag = RegularTargetEntry
  }

let countStar = FunctionExpr(AggregateFunction(Count, { Distinct = false; OrderBy = [] }), [])

let plus1 expression =
  FunctionExpr(ScalarFunction Add, [ expression; Constant(Integer 1L) ])

[<Fact>]
let ``plan select`` () =
  let select = { emptySelect with TargetList = [ selectColumn 0; selectColumn 1 ] }

  let expected = Plan.Project(Plan.Scan(table, [ 0; 1 ]), [ column 0; column 1 ])

  select |> Planner.plan |> should equal expected

[<Fact>]
let ``plan where`` () =
  let condition = FunctionExpr(ScalarFunction Equals, [ column 1; Constant(String "abc") ])

  let select = { emptySelect with Where = condition }

  let expected = Plan.Project(Plan.Filter(Plan.Scan(table, [ 1 ]), condition), [])

  select |> Planner.plan |> should equal expected

[<Fact>]
let ``plan order by`` () =
  let orderBy = [ OrderBy(column 1, Ascending, NullsFirst) ]
  let select = { emptySelect with OrderBy = orderBy }

  let expected = Plan.Project(Plan.Sort(Plan.Scan(table, [ 1 ]), orderBy), [])

  select |> Planner.plan |> should equal expected

[<Fact>]
let ``plan aggregation`` () =
  let groupBy = [ column 1 ]
  let selectedColumns = [ selectColumn 1; { Expression = countStar; Alias = ""; Tag = RegularTargetEntry } ]

  let select = { emptySelect with TargetList = selectedColumns; GroupBy = groupBy }

  let expected =
    Plan.Project(
      Plan.Aggregate(Plan.Scan(table, [ 1 ]), groupBy, [ countStar ]),
      [ ColumnReference(0, StringType); ColumnReference(1, IntegerType) ]
    )

  select |> Planner.plan |> should equal expected

[<Fact>]
let ``plan all`` () =
  let groupBy = [ column 0 ]

  let selectedColumns =
    [
      { Expression = plus1 (column 0); Alias = ""; Tag = RegularTargetEntry }
      { Expression = countStar; Alias = ""; Tag = RegularTargetEntry }
    ]

  let whereCondition = FunctionExpr(ScalarFunction Equals, [ column 1; Constant(String "abc") ])
  let havingCondition = FunctionExpr(ScalarFunction Equals, [ countStar; Constant(Integer 0L) ])
  let orderBy = [ OrderBy(plus1 countStar, Ascending, NullsFirst) ]

  let select =
    { emptySelect with
        TargetList = selectedColumns
        GroupBy = groupBy
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
              Plan.Scan(table, [ 0; 1 ]), //
              whereCondition
            ),
            groupBy,
            [ countStar ]
          ),
          FunctionExpr(ScalarFunction Equals, [ ColumnReference(1, IntegerType); Constant(Integer 0L) ])
        ),
        [ OrderBy(plus1 (ColumnReference(1, IntegerType)), Ascending, NullsFirst) ]
      ),
      [ plus1 (ColumnReference(0, IntegerType)); ColumnReference(1, IntegerType) ]
    )

  select |> Planner.plan |> should equal expected

[<Fact>]
let ``sub-query plan`` () =
  let selectedColumns = [ selectColumn 1 ]
  let subQuery = { emptySelect with TargetList = selectedColumns }

  let query =
    { subQuery with
        TargetList = [ selectColumn 0 ]
        From = SubQuery(subQuery, "subQuery")
    }

  let expected = Plan.Project(Plan.Project(Plan.Scan(table, [ 1 ]), [ column 1 ]), [ column 0 ])

  query |> Planner.plan |> should equal expected

[<Fact>]
let ``plan join`` () =
  let join =
    {
      Type = JoinType.InnerJoin
      Left = RangeTable(table, table.Name)
      Right = RangeTable(table, table.Name)
      On = constTrue
    }

  let select = { emptySelect with From = Join join }

  let expected =
    Plan.Project(Plan.Join(Plan.Scan(table, []), Plan.Scan(table, []), JoinType.InnerJoin, on = constTrue), [])

  select |> Planner.plan |> should equal expected

[<Fact>]
let ``plan set select`` () =
  let setExpression = FunctionExpr((SetFunction GenerateSeries), [ column 0 ])
  let setSelect = { Expression = setExpression; Alias = "set"; Tag = RegularTargetEntry }

  let select = { emptySelect with TargetList = [ selectColumn 1; setSelect ] }

  let expected =
    Plan.Project(
      Plan.ProjectSet(Plan.Scan(table, [ 0; 1 ]), GenerateSeries, [ column 0 ]),
      [ column 1; ColumnReference(2, IntegerType) ]
    )

  select |> Planner.plan |> should equal expected

[<Fact>]
let ``junk filter`` () =
  let junkCol = { selectColumn 0 with Tag = JunkTargetEntry }

  let select = { emptySelect with TargetList = [ junkCol; selectColumn 1 ] }

  let expected = Plan.Project(Plan.Project(Plan.Scan(table, [ 0; 1 ]), [ column 0; column 1 ]), [ column 1 ])

  select |> Planner.plan |> should equal expected
