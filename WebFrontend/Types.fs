module WebFrontend.Types

open Thoth.Json.Net
open DiffixEngine.Types

type AnonymizationParameters =
  {
    LowCountFiltering: LowCountSettings option
    Seed: int
  }
  
  static member Decoder: Decoder<AnonymizationParameters> =
    Decode.object (
      fun get ->
        {
          LowCountFiltering = get.Optional.Field "low_count_filter" LowCountSettings.Decoder
          Seed = get.Optional.Field "seed" Decode.int |> Option.defaultValue 1
        }
    )

type QueryRequest =
  {
    Query: string
    Database: string
    AidColumns: string list
    Anonymization: AnonymizationParameters
  }
  
  static member Decoder: Decoder<QueryRequest> =
    Decode.object (
      fun get ->
        {
          Query = get.Required.Field "query" Decode.string
          Database = get.Required.Field "database" Decode.string
          AidColumns = get.Optional.Field "aid_columns" (Decode.list Decode.string) |> Option.defaultValue []
          Anonymization =
            get.Optional.Field "anonymization_parameters" AnonymizationParameters.Decoder
            |> Option.defaultValue {LowCountFiltering = Some LowCountSettings.Defaults; Seed = 1}
        }
    )

  static member withQuery query db =
    {
      Query = query
      Database = db
      AidColumns = []
      Anonymization = {
        LowCountFiltering = Some LowCountSettings.Defaults
        Seed = 1
      }
    }

