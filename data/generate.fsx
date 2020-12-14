#r "nuget: System.Data.SQLite.Core, Version=1.0.113.6"

open System
open System.IO
open System.Data.SQLite

type Type =
  | Text = 0
  | Integer = 1
  | Real = 2

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

type IGenerator =
  abstract Create: unit -> Field

type Column =
  {
    Name: string
    Type: Type
    Generator: IGenerator
  }

type Table = { Name: string; Columns: Column list }

type SequentialGenerator(?max: uint) =
  let mutable current = 0u
  let max = defaultArg max UInt32.MaxValue

  member this.Next() =
    current <- (current + 1u) % max
    current

  interface IGenerator with
    member this.Create() =
      let value = this.Next()
      Field.Integer(int64 value)

type ListGenerator(values: Field list) =
  let values = values
  let sequence = SequentialGenerator(uint values.Length)

  interface IGenerator with
    member this.Create() =
      let index = sequence.Next()
      List.item (int index) values

type RandomGenerator(min, max) =
  let min = min
  let max = max
  let rng = System.Random(123) // Fixed seed because we want constant values

  interface IGenerator with
    member this.Create() =
      let value = rng.Next(min, max)
      Field.Integer(int64 value)

let cities =
  [ "Berlin"; "Rome"; "Paris"; "Madrid"; "London" ]
  |> List.map Field.Text

let first_names =
  [
    "James"
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
    "Jones"
    "Williams"
    "Brown"
    "Wilson"
    "Taylor"
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
          Generator = SequentialGenerator()
        }
        {
          Name = "first_name"
          Type = Type.Text
          Generator = ListGenerator(first_names)
        }
        {
          Name = "last_name"
          Type = Type.Text
          Generator = ListGenerator(last_names)
        }
        {
          Name = "age"
          Type = Type.Integer
          Generator = RandomGenerator(18, 80)
        }
        {
          Name = "city"
          Type = Type.Text
          Generator = ListGenerator(cities)
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
    new SQLiteCommand($"CREATE TABLE {table.Name} ({columns})", conn)

  command.ExecuteNonQuery() |> ignore

  let columns =
    table.Columns
    |> List.map (fun column -> column.Name)
    |> String.concat ", "

  for _i = 1 to rowsCount do
    let row =
      table.Columns
      |> List.map (fun column -> column.Generator.Create())
      |> List.map fieldToString
      |> String.concat ", "

    use command =
      new SQLiteCommand($"INSERT INTO {table.Name} ({columns}) VALUES ({row})", conn)

    command.ExecuteNonQuery() |> ignore

// Main Body

let file_path = Path.Combine(__SOURCE_DIRECTORY__, "data.sqlite")

File.Delete(file_path)

let conn = new SQLiteConnection("Data Source=" + file_path)

conn.Open()

generate conn customers 100

conn.Close()

printfn "Done!"

0
