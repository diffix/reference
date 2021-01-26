[<AutoOpen>]
module OpenDiffix.Core.TestHelpers

open Xunit
open System
open OpenDiffix.Core

type DBFixture() =
  [<Literal>]
  static let DatabasePath = __SOURCE_DIRECTORY__ + "/../../data/data.sqlite"

  let connection = DatabasePath |> SQLite.dbConnection |> Utils.unwrap

  do connection.Open()

  member this.Connection = connection

  interface IDisposable with
    member this.Dispose() = connection.Close()

let assertOk (result: Result<'a, 'b>) =
  Assert.True(
    match result with
    | Ok _ -> true
    | Error _ -> false
  )

let assertError (result: Result<'a, 'b>) =
  Assert.True(
    match result with
    | Ok _ -> false
    | Error _ -> true
  )

let assertOkEqual<'a, 'b> (result: Result<'a, 'b>) (expected: 'a) =
  match result with
  | Ok value -> Assert.Equal(expected, value)
  | Error error -> failwith (sprintf "Did not expect error: %A" error)

let assertErrorEqual (result: Result<'a, 'b>) (expected: 'b) =
  assertError result

  Assert.True(
    match result with
    | Error value when value = expected -> true
    | _ -> false
  )
