module WebFrontend.Types

open Thoth.Json.Net

[<CLIMutable>]
type QueryRequest =
  {
    Query: string
    Database: string
    AidColumns: string list
    Seed: int option
  }
  
  static member Decoder: Decoder<QueryRequest> =
    Decode.object (
      fun get ->
        {
          Query = get.Required.Field "query" Decode.string
          Database = get.Required.Field "database" Decode.string
          AidColumns = get.Optional.Field "aid_columns" (Decode.list Decode.string) |> Option.defaultValue []
          Seed = get.Optional.Field "seed" Decode.int
        }
    )

  static member withQuery query db =
    {
      Query = query
      Database = db
      AidColumns = []
      Seed = None
    }

