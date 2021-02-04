module OpenDiffix.Core.ExecutorTests

open Xunit
open FsUnit.Xunit
open OpenDiffix.Core
open OpenDiffix.Core.PlannerTypes

type Tests(db: DBFixture) =

  let getTable name = name |> Table.getI db.Connection |> Async.RunSynchronously |> Utils.unwrap

  let products = getTable "products"

  let column table index =
    let column = table.Columns |> List.item index
    ColumnReference(index, column.Type)

  let idColumn = column products 0

  let plus1 expression = FunctionExpr(ScalarFunction Plus, [ expression; Constant(Integer 1L) ])

  let nameLength = FunctionExpr(ScalarFunction Length, [ column products 1 ])

  let countStar = FunctionExpr(AggregateFunction(Count, { Distinct = false; OrderBy = [] }), [])

  let junkCount expression =
    FunctionExpr(AggregateFunction(JunkUserCount, { Distinct = false; OrderBy = [] }), [ expression ])

  let countDistinct expression =
    FunctionExpr(AggregateFunction(Count, { Distinct = true; OrderBy = [] }), [ expression ])

  let context = EvaluationContext.Default

  let executeToRows plan = plan |> Executor.execute db.Connection context |> Seq.toList

  let execute plan = plan |> executeToRows |> List.map Row.GetValues

  [<Fact>]
  let ``execute scan`` () =
    let plan = Plan.Scan(products)
    let expected = [ [| Integer 0L; Null; Null |]; [| Integer 1L; String "Water"; Real 1.3 |] ]
    plan |> execute |> List.take 2 |> should equal expected

  [<Fact>]
  let ``execute project`` () =
    let plan = Plan.Project(Plan.Scan(products), [ plus1 (column products 0) ])
    let expected = [ [| Integer 1L |]; [| Integer 2L |]; [| Integer 3L |] ]
    plan |> execute |> List.take 3 |> should equal expected

  [<Fact>]
  let ``execute filter`` () =
    let condition = FunctionExpr(ScalarFunction Equals, [ column products 1; Constant(String "Milk") ])
    let plan = Plan.Filter(Plan.Scan(products), condition)
    let expected = [ [| Integer 6L; String "Milk"; Real 3.74 |] ]
    plan |> execute |> should equal expected

  [<Fact>]
  let ``execute sort`` () =
    let orderById = idColumn, Descending, NullsFirst
    let plan = Plan.Project(Plan.Sort(Plan.Scan(products), [ orderById ]), [ idColumn ])
    let expected = [ [| Integer 1000L |]; [| Integer 10L |]; [| Integer 9L |] ]
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

    let expected: Value array list = []
    plan |> execute |> should equal expected

  [<Fact>]
  let ``execute global aggregate over nothing`` () =
    let condition = FunctionExpr(ScalarFunction Equals, [ column products 1; Constant(String "xxx") ])
    let plan = Plan.Aggregate(Plan.Filter(Plan.Scan(products), condition), [], [ countStar ])

    let expected = [ [| Integer 0L |] ]
    plan |> execute |> should equal expected

  [<Fact>]
  let ``execute aggregate with junk`` () =
    let plan = Plan.Aggregate(Plan.Scan(products), [], [ junkCount idColumn ])

    let row = plan |> executeToRows |> List.head

    should equal [||] (Row.DropJunkValues(row).Values)
    should equal [| UselessJunk |] row.Values
    should equal (Integer 9L) (row.GetJunk NoisyUserCount)
    should equal (Boolean false) (row.GetJunk LowCount)

  interface IClassFixture<DBFixture>
