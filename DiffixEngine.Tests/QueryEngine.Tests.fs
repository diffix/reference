module Tests

open DiffixEngine.Types
open Xunit
open DiffixEngine

let runQuery query =
  QueryEngine.runQuery (__SOURCE_DIRECTORY__ + "/../test-db.sqlite") query
  |> Async.RunSynchronously
  
[<Fact>]
let ``SHOW TABLES`` () =
    let expected =
      [
        "customers"
        "line_items"
        "products"
        "purchases"
      ]
      |> List.map(fun v -> [ColumnCell ("name", StringValue v)])
      |> ResultTable
    Assert.Equal (expected, runQuery "SHOW TABLES")
    
[<Fact>]
let ``SHOW columns FROM customers`` () =
    let expected =
      [
        "id", "INTEGER"
        "name", "TEXT"
      ]
      |> List.map(fun (name, dataType) -> [ColumnCell ("name", StringValue name); ColumnCell ("type", StringValue dataType)])
      |> ResultTable
    Assert.Equal (expected, runQuery "SHOW columns FROM customers")
