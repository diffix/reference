module OpenDiffix.Core.AggregatorTests

open Xunit
open FsUnit.Xunit

open CommonTypes

let diffixCount = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = false })
let distinctDiffixCount = AggregateFunction(DiffixCount, { AggregateOptions.Default with Distinct = true })
let diffixLowCount = AggregateFunction(DiffixLowCount, AggregateOptions.Default)

let randomNullableInteger (random: System.Random) =
  // 10% chance to get a NULL
  if random.Next(10) = 0 then Null else Integer(random.Next(100) |> int64)

let buildAidInstancesSequence numAids (random: System.Random) =
  Seq.initInfinite (fun _ -> List.init numAids (fun _ -> randomNullableInteger random) |> Value.List)

let buildIntegerSequence (random: System.Random) =
  Seq.initInfinite (fun _ -> randomNullableInteger random)

let makeArgs hasValueArg random numAids length =
  let argsSeq =
    if hasValueArg then
      (buildAidInstancesSequence numAids random, buildIntegerSequence random)
      ||> Seq.map2 (fun aidInstances argValue -> [ aidInstances; argValue ])
    else
      buildAidInstancesSequence numAids random
      |> Seq.map (fun aidInstances -> [ aidInstances ])

  argsSeq |> Seq.truncate length |> Seq.toList

/// Verifies that merging 2 separately aggregated sequences is equivalent
/// to a single aggregation of those 2 sequences concatenated.
let ensureConsistentMerging ctx fn sourceArgs destinationArgs =
  let sourceAggregator = Aggregator.create ctx true fn
  sourceArgs |> List.iter sourceAggregator.Transition

  let destinationAggregator = Aggregator.create ctx true fn
  destinationArgs |> List.iter destinationAggregator.Transition

  destinationAggregator.Merge sourceAggregator
  let mergedFinal = destinationAggregator.Final ctx

  let replayedAggregator = Aggregator.create ctx true fn
  (destinationArgs @ sourceArgs) |> List.iter replayedAggregator.Transition
  let replayedFinal = replayedAggregator.Final ctx

  mergedFinal |> should equal replayedFinal

let testAggregatorMerging fn hasValueArg =
  let random = System.Random(Hash.string $"{fn}:{hasValueArg}" |> int32)
  let ctx = ExecutionContext.makeDefault ()

  let testPair numAids (length1, length2) =
    let makeArgs = makeArgs hasValueArg random numAids
    let args1 = makeArgs length1
    let args2 = makeArgs length2
    ensureConsistentMerging ctx fn args1 args2
    ensureConsistentMerging ctx fn args2 args1

  // Empty args can't indicate number of AID instances
  testPair 1 (0, 0)

  for numAids in 1 .. 5 do
    [ //
      200, 300
      250, 250
      250, 1
      250, 0
      1, 1
      1, 0
    ]
    |> List.iter (testPair numAids)

let WITH_VALUE_ARG = true
let WITHOUT_VALUE_ARG = false

[<Fact>]
let ``Merging DiffixCount`` () =
  testAggregatorMerging diffixCount WITH_VALUE_ARG
  testAggregatorMerging diffixCount WITHOUT_VALUE_ARG

[<Fact>]
let ``Merging distinct DiffixCount`` () =
  testAggregatorMerging distinctDiffixCount WITH_VALUE_ARG

[<Fact>]
let ``Merging DiffixLowCount`` () =
  testAggregatorMerging diffixLowCount WITHOUT_VALUE_ARG
