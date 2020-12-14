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

type Column =
  {
    Name: string
    Type: Type
    Generator: seq<Field>
  }

type Table = { Name: string; Columns: Column list }

let g_rng = System.Random(123) // Fixed seed because we want constant values

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
      yield Field.Integer(int64 (g_rng.Next(min, max)))
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
        {
          Name = "id"
          Type = Type.Integer
          Generator = sequentialGenerator ()
        }
        {
          Name = "first_name"
          Type = Type.Text
          Generator = listGenerator first_names
        }
        {
          Name = "last_name"
          Type = Type.Text
          Generator = listGenerator last_names
        }
        {
          Name = "age"
          Type = Type.Integer
          Generator = randomGenerator 18 80
        }
        {
          Name = "city"
          Type = Type.Text
          Generator = listGenerator cities
        }
      ]
  }

let generate conn table rowsCount =
  let columns =
    table.Columns
    |> List.map (fun column -> $"{column.Name} {column.Type}")
    |> String.concat ", "

  printfn "Creating table %A with %i rows and columns %A" table.Name rowsCount columns

  use command =
    new SQLiteCommand($"CREATE TABLE %s{table.Name} (%s{columns})", conn)

  command.ExecuteNonQuery() |> ignore

  let columns =
    table.Columns
    |> List.map (fun column -> column.Name)
    |> String.concat ", "

  let generators =
    table.Columns
    |> List.map (fun column -> statefulGenerator column.Generator)

  for _i = 1 to rowsCount do
    let row =
      generators
      |> List.map (fun generator -> generator ())
      |> List.map fieldToString
      |> String.concat ", "

    use command =
      new SQLiteCommand($"INSERT INTO {table.Name} (%s{columns}) VALUES (%s{row})", conn)

    command.ExecuteNonQuery() |> ignore

// Main Body

let file_path = Path.Combine(__SOURCE_DIRECTORY__, "data.sqlite")

File.Delete(file_path)

let conn = new SQLiteConnection("Data Source=" + file_path)

conn.Open()

generate conn customers 200

conn.Close()

printfn "Done!"

0
