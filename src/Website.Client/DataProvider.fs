module Website.Client.DataProvider

open OpenDiffix.Core

type PlaygroundDataProvider () =
  interface IDataProvider with
    member this.GetSchema() =
      let tables = [
        {
          Name = "customers"
          Columns = [
            {Name = "aid"; Type = IntegerType}
            {Name = "age"; Type = IntegerType}
            {Name = "name"; Type = StringType}
          ]
        }
      ]
      async {return Ok tables}

    member this.LoadData(table) =
      if table.Name = "customers"
      then
        let data =
          [
            [| Integer 1L; Integer 30L; String "Alice" |]
            [| Integer 2L; Integer 30L; String "Bob" |]
            [| Integer 3L; Integer 30L; String "Cynthia" |]
            [| Integer 4L; Integer 31L; String "Denis" |]
            [| Integer 5L; Integer 31L; String "Elizabeth" |]
            [| Integer 6L; Integer 32L; String "Ferdinand" |]
            [| Integer 7L; Integer 33L; String "Genevieve" |]
            [| Integer 8L; Integer 34L; String "Hannah" |]
            [| Integer 9L; Integer 34L; String "Isolde" |]
          ]
          |> Seq.ofList
        async {return Ok data}
      else
        async {return Error $"Table '%s{table.Name}' was not found"}
