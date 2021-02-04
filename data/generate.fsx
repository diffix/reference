#r "nuget: System.Data.SQLite.Core, Version=1.0.113.6"

open System
open System.IO
open System.Data.SQLite

type Type =
  | Text
  | Integer
  | Real

type Field =
  | Null
  | Text of string
  | Integer of int64
  | Real of float

let fieldToString field =
  match field with
  | Null -> "NULL"
  | Text value -> $"'{value}'"
  | Integer value -> string value
  | Real value -> string value

type Column = { Name: string; Type: Type }

type Table =
  {
    Name: string
    Columns: Column list
    GeneratedRowsCount: int
    Generators: seq<Field> list
    StaticRows: Field list list
  }

let gRNG = System.Random(123) // Fixed seed because we want constant values

let sequentialGenerator () =
  seq {
    while true do
      for i in 1 .. Int32.MaxValue -> Field.Integer(int64 i)
  }

let listGenerator values =
  seq {
    while true do
      for value in values -> value
  }

let randomGenerator min max =
  seq {
    while true do
      yield Field.Integer(int64 (gRNG.Next(min, max)))
  }

let statefulGenerator (generator: seq<Field>) =
  let enumerator = generator.GetEnumerator()

  fun () ->
    enumerator.MoveNext() |> ignore
    enumerator.Current

let cities =
  [ "Berlin"; "Berlin"; "Berlin"; "Rome"; "Rome"; "Paris"; "Madrid"; "London" ]
  |> List.map Field.Text

let firstNames =
  [
    "James"
    "James"
    "James"
    "Mary"
    "Mary"
    "Robert"
    "Jennifer"
    "David"
    "William"
    "Elizabeth"
    "David"
    "Susan"
  ]
  |> List.map Field.Text

let lastNames =
  [
    "Smith"
    "Smith"
    "Smith"
    "Jones"
    "Jones"
    "Williams"
    "Brown"
    "Wilson"
    "Taylor"
    "Thomas"
    "Thomas"
    "Thomas"
    "Thomas"
  ]
  |> List.map Field.Text

let customersSmall =
  {
    Name = "customers_small"
    Columns =
      [
        { Name = "id"; Type = Type.Integer }
        { Name = "first_name"; Type = Type.Text }
        { Name = "last_name"; Type = Type.Text }
        { Name = "age"; Type = Type.Integer }
        { Name = "city"; Type = Type.Text }
      ]

    GeneratedRowsCount = 20
    Generators =
      [
        sequentialGenerator ()
        listGenerator [ Text "Alice"; Text "Bob" ]
        listGenerator [ Text "Regular" ]
        listGenerator [ Integer 25L; Integer 30L; Integer 35L ]
        listGenerator [ Text "Berlin"; Text "Rome" ]
      ]

    StaticRows =
      [
        [ Integer 1000L; Null; Null; Null; Null ]
        [ Integer 1001L; Text "Alice"; Text "Outlier"; Integer 18L; Text "Bucharest" ]
        [ Integer 1001L; Text "Alice"; Text "Outlier"; Integer 18L; Text "Bucharest" ]
        [ Integer 1001L; Text "Alice"; Text "Outlier"; Integer 18L; Text "Bucharest" ]
        [ Integer 1001L; Text "Alice"; Text "Outlier"; Integer 18L; Text "Bucharest" ]
        [ Integer 1001L; Text "Alice"; Text "Outlier"; Integer 18L; Text "Bucharest" ]
        [ Integer 1002L; Text "Bob"; Text "Outlier"; Integer 100L; Text "Pristina" ]
        [ Integer 1002L; Text "Bob"; Text "Outlier"; Integer 100L; Text "Pristina" ]
        [ Integer 1002L; Text "Bob"; Text "Outlier"; Integer 100L; Text "Pristina" ]
        [ Integer 1002L; Text "Bob"; Text "Outlier"; Integer 100L; Text "Pristina" ]
        [ Integer 1002L; Text "Bob"; Text "Outlier"; Integer 100L; Text "Pristina" ]
        [ Integer 1002L; Text "Bob"; Text "Outlier"; Integer 100L; Text "Pristina" ]
        [ Integer 1002L; Text "Bob"; Text "Outlier"; Integer 100L; Text "Pristina" ]
      ]
  }

let customers =
  {
    Name = "customers"
    Columns =
      [
        { Name = "id"; Type = Type.Integer }
        { Name = "first_name"; Type = Type.Text }
        { Name = "last_name"; Type = Type.Text }
        { Name = "age"; Type = Type.Integer }
        { Name = "city"; Type = Type.Text }
      ]

    GeneratedRowsCount = 200
    Generators =
      [
        sequentialGenerator ()
        listGenerator firstNames
        listGenerator lastNames
        randomGenerator 18 80
        listGenerator cities
      ]

    StaticRows =
      [
        [ Integer 1000L; Null; Null; Null; Null ]
        [ Integer 1001L; Text "1"; Text "outlier"; Integer 17L; Text "Oslo" ]
        [ Integer 1002L; Text "2"; Text "outlier"; Integer 90L; Text "Paris" ]
        [ Integer 1003L; Text "3"; Text "outlier"; Null; Text "Berlin" ]
        [ Integer 1004L; Text "4"; Text "outlier"; Integer 10L; Text "Berlin" ]
      ]
  }

let products =
  {
    Name = "products"
    Columns =
      [
        { Name = "id"; Type = Type.Integer }
        { Name = "name"; Type = Type.Text }
        { Name = "price"; Type = Type.Real }
      ]

    GeneratedRowsCount = 0
    Generators = []

    StaticRows =
      [
        [ Integer 1L; Text "Water"; Real 1.3 ]
        [ Integer 2L; Text "Pasta"; Real 7.5 ]
        [ Integer 3L; Text "Chicken"; Real 12.81 ]
        [ Integer 4L; Text "Wine"; Real 9.25 ]
        [ Integer 5L; Text "Cheese"; Real 4.93 ]
        [ Integer 6L; Text "Milk"; Real 3.74 ]
        [ Integer 8L; Text "Coffee"; Real 6.14 ]
        [ Integer 9L; Text "Bread"; Real 1.4 ]
        [ Integer 10L; Text "Banana"; Real 4.78 ]
        [ Integer 1000L; Null; Null ]
        [ Integer 1001L; Text "Drugs"; Real 30.7 ]
      ]
  }

let purchaseAmounts =
  [ 0.25; 0.25; 0.5; 0.5; 0.5; 0.75; 1.0; 1.0; 1.0; 1.0; 1.5; 2.0; 2.0; 2.5; 4.0 ]
  |> List.map Field.Real

let purchases =
  {
    Name = "purchases"
    Columns =
      [
        { Name = "cid"; Type = Type.Integer }
        { Name = "pid"; Type = Type.Integer }
        { Name = "amount"; Type = Type.Real }
      ]

    GeneratedRowsCount = 500
    Generators =
      [
        randomGenerator 1 customers.GeneratedRowsCount
        randomGenerator 1 (products.StaticRows.Length - 2)
        listGenerator purchaseAmounts
      ]

    StaticRows =
      [
        [ Null; Null; Null ]
        [ Integer 1000L; Integer 1000L; Real 0.0 ]
        [ Integer 1001L; Integer 1001L; Real 1.0 ]
        [ Integer 1002L; Integer 1001L; Real 5.0 ]
        [ Integer 1003L; Integer 1001L; Real 3.5 ]
        [ Integer 1003L; Integer 1001L; Real 4.5 ]
        [ Integer 1004L; Integer 1L; Real 20.0 ]
        [ Integer 1L; Integer 1L; Real 7.0 ]
        [ Integer 2L; Integer 1L; Real 0.1 ]
      ]
  }

let generate conn table =
  let columns =
    table.Columns
    |> List.map (fun column -> $"{column.Name} {column.Type}")
    |> String.concat ", "

  printfn "Creating table %A with columns %A" table.Name columns

  use command = new SQLiteCommand($"CREATE TABLE %s{table.Name} (%s{columns})", conn)

  command.ExecuteNonQuery() |> ignore

  let columns = table.Columns |> List.map (fun column -> column.Name) |> String.concat ", "

  let generators = List.map statefulGenerator table.Generators

  let rowGenerator = fun _ -> List.map (fun generator -> generator ()) generators

  let genericRows = Seq.init table.GeneratedRowsCount rowGenerator

  let rows = Seq.append table.StaticRows genericRows

  for row in rows do
    let values = row |> List.map fieldToString |> String.concat ", "

    use command = new SQLiteCommand($"INSERT INTO {table.Name} (%s{columns}) VALUES (%s{values})", conn)

    command.ExecuteNonQuery() |> ignore

// Main Body

let filePath = Path.Combine(__SOURCE_DIRECTORY__, "data.sqlite")

File.Delete(filePath)

let conn = new SQLiteConnection("Data Source=" + filePath)

conn.Open()

generate conn customers
generate conn products
generate conn purchases
generate conn customersSmall

conn.Close()

printfn "Done!"

0
