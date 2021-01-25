namespace OpenDiffix.Core

module Utils =
  let equalsI s1 s2 = System.String.Equals(s1, s2, System.StringComparison.CurrentCultureIgnoreCase)

  let unwrap =
    function
    | Ok value -> value
    | Error message -> failwith message
