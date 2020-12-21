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
  [
    "Berlin"
    "Berlin"
    "Berlin"
    "Rome"
    "Rome"
    "Paris"
    "Madrid"
    "London"
  ]
  |> List.map Field.Text

let first_names =
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

let last_names =
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

let customers =
  {
    Name = "customers"
    Columns =
      [
        { Name = "id"; Type = Type.Integer }
        {
          Name = "first_name"
          Type = Type.Text
        }
        { Name = "last_name"; Type = Type.Text }
        { Name = "age"; Type = Type.Integer }
        { Name = "city"; Type = Type.Text }
      ]

    GeneratedRowsCount = 200
    Generators =
      [
        sequentialGenerator ()
        listGenerator first_names
        listGenerator last_names
        randomGenerator 18 80
        listGenerator cities
      ]

    StaticRows =
      [
        [ Integer(0L); Null; Null; Null; Null ]
        [
          Integer(-1L)
          Text("1")
          Text("outlier")
          Integer(17L)
          Text("Oslo")
        ]
        [
          Integer(-2L)
          Text("2")
          Text("outlier")
          Integer(90L)
          Text("Paris")
        ]
        [
          Integer(-3L)
          Text("3")
          Text("outlier")
          Null
          Text("Berlin")
        ]
        [
          Integer(-4L)
          Text("4")
          Text("outlier")
          Integer(10L)
          Text("Berlin")
        ]
      ]
  }

let generate conn table =
  let columns =
    table.Columns
    |> List.map (fun column -> $"{column.Name} {column.Type}")
    |> String.concat ", "

  printfn "Creating table %A with columns %A" table.Name columns

  use command =
    new SQLiteCommand($"CREATE TABLE %s{table.Name} (%s{columns})", conn)

  command.ExecuteNonQuery() |> ignore

  let columns =
    table.Columns
    |> List.map (fun column -> column.Name)
    |> String.concat ", "

  let generators = List.map statefulGenerator table.Generators

  let rowGenerator =
    fun _ -> List.map (fun generator -> generator ()) generators

  let genericRows = Seq.init table.GeneratedRowsCount rowGenerator

  let rows = Seq.append table.StaticRows genericRows

  for row in rows do
    let values =
      row |> List.map fieldToString |> String.concat ", "

    use command =
      new SQLiteCommand($"INSERT INTO {table.Name} (%s{columns}) VALUES (%s{values})", conn)

    command.ExecuteNonQuery() |> ignore

// Main Body

let file_path = Path.Combine(__SOURCE_DIRECTORY__, "data.sqlite")

File.Delete(file_path)

let conn = new SQLiteConnection("Data Source=" + file_path)

conn.Open()

generate conn customers

conn.Close()

printfn "Done!"

0
