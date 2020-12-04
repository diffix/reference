module WebFrontend.Types

open Thoth.Json.Net
open DiffixEngine.Types

type AnonymizationParameters =
  {
    LowCountFiltering: LowCountSettings option
  }
  
  static member Decoder: Decoder<AnonymizationParameters> =
    Decode.object (
      fun get ->
        {
          LowCountFiltering = get.Optional.Field "low_count_filter" LowCountSettings.Decoder
        }
    )

[<CLIMutable>]
type QueryRequest =
  {
    Query: string
    Database: string
    AidColumns: string list
    Seed: int 
    Anonymization: AnonymizationParameters
  }
  
  static member Decoder: Decoder<QueryRequest> =
    Decode.object (
      fun get ->
        {
          Query = get.Required.Field "query" Decode.string
          Database = get.Required.Field "database" Decode.string
          AidColumns = get.Optional.Field "aid_columns" (Decode.list Decode.string) |> Option.defaultValue []
          Seed = get.Optional.Field "seed" Decode.int |> Option.defaultValue 1
          Anonymization =
            get.Optional.Field "anonymization_parameters" AnonymizationParameters.Decoder
            |> Option.defaultValue {LowCountFiltering = Some LowCountSettings.Defaults}
        }
    )

  static member withQuery query db =
    {
      Query = query
      Database = db
      AidColumns = []
      Seed = None
      Anonymization = {
        LowCountFiltering = Some LowCountSettings.Defaults
      }
    }

