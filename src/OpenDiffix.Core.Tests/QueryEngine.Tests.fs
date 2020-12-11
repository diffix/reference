module OpenDiffix.Core.QueryEngineTests

open Xunit
open OpenDiffix.Core
open OpenDiffix.Core.AnonymizerTypes

let runQuery query =
  let requestParams =
    {
      AnonymizationParams =
        {
          AidColumnOption = Some "id"
          Seed = 1
          LowCountSettings = Some LowCountSettings.Defaults
        }
      DatabasePath = __SOURCE_DIRECTORY__ + "/../../dbs/test-db.sqlite"
      Query = query
    }

  QueryEngine.runQuery requestParams |> Async.RunSynchronously

[<Fact>]
let ``SHOW TABLES`` () =
  let expected =
    [ "customers"; "line_items"; "products"; "purchases" ]
    |> List.map (fun v ->
         NonPersonalRow
           {
             Columns =
               [
                 {
                   ColumnName = "name"
                   ColumnValue = StringValue v
                 }
               ]
           })
    |> ResultTable
    |> Ok

  Assert.Equal(expected, runQuery "SHOW TABLES")

[<Fact>]
let ``SHOW columns FROM customers`` () =
  let expected =
    [ "id", "integer"; "name", "string" ]
    |> List.map (fun (name, dataType) ->
         NonPersonalRow
           {
             Columns =
               [
                 {
                   ColumnName = "name"
                   ColumnValue = StringValue name
                 }
                 {
                   ColumnName = "type"
                   ColumnValue = StringValue dataType
                 }
               ]
           })
    |> ResultTable
    |> Ok

  let queryResult = runQuery "SHOW columns FROM customers"
  Assert.Equal(expected, queryResult)

[<Fact>]
let ``SELECT product_id FROM line_items`` () =
  let expected =
    [ 1, 10; 2, 16; 3, 16; 4, 16; 5, 16 ]
    |> List.map (fun (productId, occurrences) ->
         let row =
           NonPersonalRow
             {
               Columns =
                 [
                   {
                     ColumnName = "product_id"
                     ColumnValue = IntegerValue productId
                   }
                 ]
             }

         List.replicate occurrences row)
    |> List.concat
    |> ResultTable
    |> Ok

  let queryResult = runQuery "SELECT product_id FROM line_items"

  Assert.Equal(expected, queryResult)