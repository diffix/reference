module OpenDiffix.Core.ExecutorTests

open Xunit
open FsUnit.Xunit

open CommonTypes
open PlannerTypes

type Tests(db: DBFixture) =
  let schema = db.DataProvider.GetSchema()

  let getTable name = name |> Schema.findTable schema

  let products = getTable "products"

  let column table index =
    let column = table.Columns |> List.item index
    ColumnReference(index, column.Type)

  let plus1 expression =
    FunctionExpr(ScalarFunction Add, [ expression; Constant(Integer 1L) ])

  let nameLength = FunctionExpr(ScalarFunction Length, [ column products 1 ])
  let countStar = FunctionExpr(AggregateFunction(Count, { Distinct = false; OrderBy = [] }), [])

  let countDistinct expression =
    FunctionExpr(AggregateFunction(Count, { Distinct = true; OrderBy = [] }), [ expression ])

  let context = { EvaluationContext.Default with DataProvider = db.DataProvider }

  let execute plan =
    plan |> Executor.execute context |> Seq.map rowToArray |> Seq.toList

  [<Fact>]
  let ``execute scan`` () =
    let plan = Plan.Scan(products)
    let expected = [ [| Integer 1L; String "Water"; Real 1.3 |]; [| Integer 2L; String "Pasta"; Real 7.5 |] ]
    plan |> execute |> List.take 2 |> should equal expected

  [<Fact>]
  let ``execute project`` () =
    let plan = Plan.Project(Plan.Scan(products), [ plus1 (column products 0) ])
    let expected = [ [| Integer 2L |]; [| Integer 3L |]; [| Integer 4L |] ]
    plan |> execute |> List.take 3 |> should equal expected

  [<Fact>]
  let ``execute filter`` () =
    let condition = FunctionExpr(ScalarFunction Equals, [ column products 1; Constant(String "Milk") ])
    let plan = Plan.Filter(Plan.Scan(products), condition)
    let expected = [ [| Integer 6L; String "Milk"; Real 3.74 |] ]
    plan |> execute |> should equal expected

  [<Fact>]
  let ``execute sort`` () =
    let idColumn = column products 0
    let orderById = OrderBy(idColumn, Descending, NullsFirst)
    let plan = Plan.Project(Plan.Sort(Plan.Scan(products), [ orderById ]), [ idColumn ])
    let expected = [ [| Integer 1001L |]; [| Integer 1000L |]; [| Integer 10L |] ]
    plan |> execute |> List.take 3 |> should equal expected

  [<Fact>]
  let ``execute grouping aggregate`` () =
    let plan = Plan.Aggregate(Plan.Scan(products), [ nameLength ], [ countStar ])

    let expected =
      [
        [| Null; Integer 1L |]
        [| Integer 4L; Integer 2L |]
        [| Integer 5L; Integer 4L |]
        [| Integer 6L; Integer 3L |]
        [| Integer 7L; Integer 1L |]
      ]

    plan |> execute |> should equal expected

  [<Fact>]
  let ``execute global aggregate`` () =
    let plan = Plan.Aggregate(Plan.Scan(products), [], [ countStar; countDistinct nameLength ])

    let expected = [ [| Integer 11L; Integer 4L |] ]
    plan |> execute |> should equal expected

  [<Fact>]
  let ``execute grouping aggregate over nothing`` () =
    let condition = FunctionExpr(ScalarFunction Equals, [ column products 1; Constant(String "xxx") ])
    let plan = Plan.Aggregate(Plan.Filter(Plan.Scan(products), condition), [ nameLength ], [ countStar ])

    let expected : Value array list = []
    plan |> execute |> should equal expected

  [<Fact>]
  let ``execute global aggregate over nothing`` () =
    let condition = FunctionExpr(ScalarFunction Equals, [ column products 1; Constant(String "xxx") ])
    let plan = Plan.Aggregate(Plan.Filter(Plan.Scan(products), condition), [], [ countStar ])

    let expected = [ [| Integer 0L |] ]
    plan |> execute |> should equal expected

  [<Fact>]
  let ``execute inner join`` () =
    let idColumn1 = column products 0
    let idColumn2 = ColumnReference(products.Columns.Length, IntegerType)
    let condition = FunctionExpr(ScalarFunction Equals, [ plus1 idColumn1; idColumn2 ])
    let joinPlan = Plan.Join(Plan.Scan(products), Plan.Scan(products), ParserTypes.InnerJoin, condition)
    let plan = Plan.Project(joinPlan, [ idColumn1; idColumn2 ])

    let expected = [ [| Integer 1000L; Integer 1001L |]; [| Integer 9L; Integer 10L |]; [| Integer 8L; Integer 9L |] ]
    plan |> execute |> List.rev |> List.take 3 |> should equal expected

  [<Fact>]
  let ``execute outer join`` () =
    let idColumn1 = column products 0
    let idColumn2 = ColumnReference(products.Columns.Length, IntegerType)
    let condition = FunctionExpr(ScalarFunction Equals, [ plus1 idColumn1; idColumn2 ])
    let joinPlan = Plan.Join(Plan.Scan(products), Plan.Scan(products), ParserTypes.LeftJoin, condition)
    let plan = Plan.Project(joinPlan, [ idColumn1; idColumn2 ])

    let expected = [ [| Integer 1001L; Null |]; [| Integer 1000L; Integer 1001L |]; [| Integer 10L; Null |] ]
    plan |> execute |> List.rev |> List.take 3 |> should equal expected

  [<Fact>]
  let ``execute project set`` () =
    let fromPlan = Plan.Project(Plan.Scan(products), [ column products 0 ])
    let plan = Plan.ProjectSet(fromPlan, GenerateSeries, [ Constant(Integer 2L) ])

    let expected =
      [
        [| Integer 1L; Integer 1L |]
        [| Integer 1L; Integer 2L |]
        [| Integer 2L; Integer 1L |]
        [| Integer 2L; Integer 2L |]
      ]

    plan |> execute |> List.take 4 |> should equal expected

  interface IClassFixture<DBFixture>
