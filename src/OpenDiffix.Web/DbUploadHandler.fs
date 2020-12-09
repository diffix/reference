module OpenDiffix.Web.DbUploadHandler

open Microsoft.AspNetCore.Http
open Giraffe
open FSharp.Control.Tasks.V2
open System.IO

let fromFormHandler dbPath =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      return!
        (match ctx.Request.HasFormContentType with
        | false -> RequestErrors.BAD_REQUEST "Bad request" next ctx
        | true  ->
          if ctx.Request.Form.Files.Count = 0
          then text "Please upload at least one database file" next ctx
          else
            ctx.Request.Form.Files
            |> Seq.iter(fun file ->
              let fileNamePath = Path.Join [| dbPath; file.FileName |]
              let fileStream = File.Create fileNamePath
              file.CopyTo fileStream
              fileStream.Flush()
              fileStream.Close()
            )
            redirectTo false "/" next ctx)
    }

let fromBodyHandler dbPath =
  fun (next : HttpFunc) (ctx : HttpContext) ->
    task {
      match ctx.TryGetRequestHeader "db-name" with
      | None ->
        return! RequestErrors.unprocessableEntity
                  (text "Please specify the db name with an HTTP header called 'db-name'") next ctx
      | Some dbName ->
        let fileNamePath = Path.Join [| dbPath; dbName |]
        let fileStream = File.Create fileNamePath
        do! ctx.Request.Body.CopyToAsync fileStream
        fileStream.Flush()
        fileStream.Close()
        return! (
          text "{\"ok\": true}"
          >=> setHttpHeader "Content-Type" "application/json; charset=utf-8"
          >=> setStatusCode 200
        ) next ctx
    }
