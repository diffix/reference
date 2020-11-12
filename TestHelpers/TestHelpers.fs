module TestHelpers

open Xunit
open System

let assertOk (result: Result<'a, 'b>) =
  Assert.True (match result with Ok _ -> true | Error _ -> false)
  
let assertError (result: Result<'a, 'b>) =
  Assert.True (match result with Ok _ -> false | Error _ -> true)
  
let assertOkEqual (result: Result<'a, 'b>) (expected: 'a) =
  match result with
  | Ok value -> Assert.Equal (sprintf "%A" expected, sprintf "%A" value)
  | Error error -> Assert.Equal (sprintf "%A" expected, sprintf "%A" error)
    
let assertErrorEqual (result: Result<'a, 'b>) (expected: 'b) =
  assertError result
  Assert.True
    (match result with
    | Error value when value = expected -> true
    | _ -> false)