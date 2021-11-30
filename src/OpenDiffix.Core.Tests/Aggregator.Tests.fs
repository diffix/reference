module OpenDiffix.Core.AggregatorTests

open Xunit
open FsUnit.Xunit

open CommonTypes

let makeAgg distinct fn =
  AggregateFunction(fn, { AggregateOptions.Default with Distinct = distinct })

let randomNullableInteger (random: System.Random) =
  // 10% chance to get a NULL
  if random.Next(10) = 0 then Null else Integer(random.Next(100) |> int64)

let buildAidInstancesSequence numAids (random: System.Random) =
  Seq.initInfinite (fun _ -> List.init numAids (fun _ -> randomNullableInteger random) |> Value.List)

let buildIntegerSequence (random: System.Random) =
  Seq.initInfinite (fun _ -> randomNullableInteger random)

let makeAnonArgs hasValueArg random numAids length =
  (if hasValueArg then
     (buildAidInstancesSequence numAids random, buildIntegerSequence random)
     ||> Seq.map2 (fun aidInstances argValue -> [ aidInstances; argValue ])
   else
     buildAidInstancesSequence numAids random
     |> Seq.map (fun aidInstances -> [ aidInstances ]))
  |> Seq.truncate length
  |> Seq.toList

let makeStandardArgs hasValueArg random length =
  (if hasValueArg then
     buildIntegerSequence random |> Seq.map (fun argValue -> [ argValue ])
   else
     Seq.initInfinite (fun _ -> []))
  |> Seq.truncate length
  |> Seq.toList

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

let makeRandom fn hasValueArg =
  System.Random(Hash.string $"{fn}:{hasValueArg}" |> int32)

let argLengthPairs =
  [ //
    200, 300
    250, 250
    250, 1
    250, 0
    1, 1
    1, 0
  ]

let testAnonAggregatorMerging fn hasValueArg =
  let random = makeRandom fn hasValueArg
  let ctx = ExecutionContext.makeDefault ()

  let testPair numAids (length1, length2) =
    let makeArgs = makeAnonArgs hasValueArg random numAids
    let args1 = makeArgs length1
    let args2 = makeArgs length2
    ensureConsistentMerging ctx fn args1 args2
    ensureConsistentMerging ctx fn args2 args1

  // Empty args can't indicate number of AID instances
  testPair 1 (0, 0)

  for numAids in 1 .. 5 do
    argLengthPairs |> List.iter (testPair numAids)

let testStandardAggregatorMerging fn hasValueArg =
  let random = makeRandom fn hasValueArg
  let ctx = ExecutionContext.makeDefault ()

  let testPair (length1, length2) =
    let makeArgs = makeStandardArgs hasValueArg random
    let args1 = makeArgs length1
    let args2 = makeArgs length2
    ensureConsistentMerging ctx fn args1 args2
    ensureConsistentMerging ctx fn args2 args1

  argLengthPairs |> List.iter testPair

let WITH_VALUE_ARG, WITHOUT_VALUE_ARG = true, false
let DISTINCT, NON_DISTINCT = true, false

let testAnon distinct hasArg fn =
  testAnonAggregatorMerging (makeAgg distinct fn) hasArg

let testStandard distinct hasArg fn =
  testStandardAggregatorMerging (makeAgg distinct fn) hasArg

[<Fact>]
let ``Merging DiffixCount`` () =
  DiffixCount |> testAnon NON_DISTINCT WITH_VALUE_ARG
  DiffixCount |> testAnon NON_DISTINCT WITHOUT_VALUE_ARG

[<Fact>]
let ``Merging distinct DiffixCount`` () =
  DiffixCount |> testAnon DISTINCT WITH_VALUE_ARG

[<Fact>]
let ``Merging DiffixLowCount`` () =
  DiffixLowCount |> testAnon NON_DISTINCT WITHOUT_VALUE_ARG

[<Fact>]
let ``Merging default aggregators`` () =
  Count |> testStandard NON_DISTINCT WITHOUT_VALUE_ARG
  Count |> testStandard NON_DISTINCT WITH_VALUE_ARG
  Count |> testStandard DISTINCT WITH_VALUE_ARG
  Sum |> testStandard NON_DISTINCT WITH_VALUE_ARG
