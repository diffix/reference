module OpenDiffix.Core.AggregatorTests

open Xunit
open FsUnit.Xunit

open CommonTypes

let makeAgg distinct fn =
  fn, { AggregateOptions.Default with Distinct = distinct }

let randomNullableInteger (random: System.Random) =
  // 10% chance to get a NULL
  if random.Next(10) = 0 then Null else Integer(random.Next(100) |> int64)

let randomNullableReal (random: System.Random) =
  // 10% chance to get a NULL
  if random.Next(10) = 0 then Null else Real(random.Next(100) |> float)

let buildAidInstancesSequence numAids (random: System.Random) =
  // Infinite sequence of (Value.List [aid1, aid2, ...])
  Seq.initInfinite (fun _ -> List.init numAids (fun _ -> randomNullableInteger random) |> Value.List)

let buildRealSequence (random: System.Random) =
  // Infinite sequence of (Value.Integer int | Null)
  Seq.initInfinite (fun _ -> randomNullableReal random)

/// Builds a list of given length with aggregator transitions.
/// Each transition contains AID instances as the first argument
/// and an optional random integer (as `Real`) as the second argument.
let makeAnonArgs hasValueArg random numAids length =
  (if hasValueArg then
     // Generates a sequence of [ Value.List [aid1, aid2, ...]; Value.Integer int ]
     (buildAidInstancesSequence numAids random, buildRealSequence random)
     ||> Seq.map2 (fun aidInstances argValue -> [ aidInstances; argValue ])
   else
     // Generates a sequence of [ Value.List [aid1, aid2, ...] ]
     buildAidInstancesSequence numAids random
     |> Seq.map (fun aidInstances -> [ aidInstances ]))
  |> Seq.truncate length
  |> Seq.toList

/// Like `makeAnonArgs` but without the AID instances.
let makeStandardArgs hasValueArg random length =
  (if hasValueArg then
     // Generates a sequence of [ Value.Integer int ]
     buildRealSequence random |> Seq.map (fun argValue -> [ argValue ])
   else
     // Generates a sequence of [ ]
     Seq.initInfinite (fun _ -> []))
  |> Seq.truncate length
  |> Seq.toList

/// Verifies that merging 2 separately aggregated sequences is equivalent
/// to a single aggregation of those 2 sequences concatenated.
let ensureConsistentMerging ctx fn sourceArgs destinationArgs aggArgs =
  let sourceAggregator = Aggregator.create (fn, aggArgs)
  sourceArgs |> List.iter sourceAggregator.Transition

  let destinationAggregator = Aggregator.create (fn, aggArgs)
  destinationArgs |> List.iter destinationAggregator.Transition

  destinationAggregator.Merge sourceAggregator
  let mergedFinal = destinationAggregator.Final ctx

  let replayedAggregator = Aggregator.create (fn, aggArgs)
  (destinationArgs @ sourceArgs) |> List.iter replayedAggregator.Transition
  let replayedFinal = replayedAggregator.Final ctx

  // agg2(args2) -> merge_to -> agg1(args1) == agg(args1 ++ args2)
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

let aggContext =
  {
    AnonymizationParams = AnonymizationParams.Default
    GroupingLabels = [||]
    Aggregators = [||]
  }

let testAnonAggregatorMerging fn hasValueArg =
  let random = makeRandom fn hasValueArg
  let ctx = aggContext, Some { BucketSeed = 0UL }

  let testPair numAids (length1, length2) =
    let makeArgs = makeAnonArgs hasValueArg random numAids
    let args1 = makeArgs length1
    let args2 = makeArgs length2

    let DUMMY_ARGS_EXPRS =
      [
        (List.replicate numAids (ColumnReference(0, IntegerType))) |> ListExpr
        ColumnReference(1, RealType)
      ]

    ensureConsistentMerging ctx fn args1 args2 DUMMY_ARGS_EXPRS
    ensureConsistentMerging ctx fn args2 args1 DUMMY_ARGS_EXPRS

  // Empty args can't indicate number of AID instances
  testPair 1 (0, 0)

  for numAids in 1 .. 5 do
    argLengthPairs |> List.iter (testPair numAids)

let testStandardAggregatorMerging fn hasValueArg =
  let random = makeRandom fn hasValueArg
  let ctx = aggContext, None

  let testPair (length1, length2) =
    let makeArgs = makeStandardArgs hasValueArg random
    let args1 = makeArgs length1
    let args2 = makeArgs length2
    let DUMMY_ARGS_EXPRS = [ ListExpr []; ColumnReference(1, RealType) ]
    ensureConsistentMerging ctx fn args1 args2 DUMMY_ARGS_EXPRS
    ensureConsistentMerging ctx fn args2 args1 DUMMY_ARGS_EXPRS

  argLengthPairs |> List.iter testPair

let WITH_VALUE_ARG, WITHOUT_VALUE_ARG = true, false
let DISTINCT, NON_DISTINCT = true, false

/// Verifies correct merging of an anonymizing aggregator (where first argument is the aid instances).
let testAnon distinct hasArg fn =
  testAnonAggregatorMerging (makeAgg distinct fn) hasArg

/// Verifies correct merging of a standard aggregator.
let testStandard distinct hasArg fn =
  testStandardAggregatorMerging (makeAgg distinct fn) hasArg

[<Fact>]
let ``Merging DiffixCount`` () =
  DiffixCount |> testAnon NON_DISTINCT WITH_VALUE_ARG
  DiffixCount |> testAnon NON_DISTINCT WITHOUT_VALUE_ARG

[<Fact>]
let ``Merging DiffixSum`` () =
  DiffixSum |> testAnon NON_DISTINCT WITH_VALUE_ARG

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
