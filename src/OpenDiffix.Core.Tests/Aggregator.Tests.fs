module OpenDiffix.Core.AggregatorTests

open Xunit
open FsUnit.Xunit

open CommonTypes

type ArgType =
  | WithoutArg
  | WithIntegerArg
  | WithRealArg
  | WithConstArg of Value

let mapArgType =
  function
  | WithoutArg -> MISSING_TYPE
  | WithIntegerArg -> IntegerType
  | WithRealArg -> RealType
  | WithConstArg value -> Value.typeOf value

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

let buildIntegerSequence (random: System.Random) =
  // Infinite sequence of (Value.Integer x | Null)
  Seq.initInfinite (fun _ -> randomNullableInteger random)

let buildRealSequence (random: System.Random) =
  // Infinite sequence of (Value.Real x | Null)
  Seq.initInfinite (fun _ -> randomNullableReal random)

/// Builds a list of given length with aggregator transitions.
/// Each transition contains AID instances as the first argument
/// and an optional random integer (as `Real`) as the second argument.
let makeAnonArgs argType random numAids length =
  (match argType with
   | WithoutArg ->
     buildAidInstancesSequence numAids random
     |> Seq.map (fun aidInstances -> [ aidInstances ])
   | WithIntegerArg ->
     (buildAidInstancesSequence numAids random, buildIntegerSequence random)
     ||> Seq.map2 (fun aidInstances argValue -> [ aidInstances; argValue ])
   | WithRealArg ->
     (buildAidInstancesSequence numAids random, buildRealSequence random)
     ||> Seq.map2 (fun aidInstances argValue -> [ aidInstances; argValue ])
   | WithConstArg arg ->
     buildAidInstancesSequence numAids random
     |> Seq.map (fun aidInstances -> [ aidInstances; arg ]))
  |> Seq.truncate length
  |> Seq.toList

/// Like `makeAnonArgs` but without the AID instances.
let makeStandardArgs argType random length =
  (match argType with
   | WithoutArg -> Seq.initInfinite (fun _ -> [])
   | WithIntegerArg -> buildIntegerSequence random |> Seq.map (fun argValue -> [ argValue ])
   | WithRealArg -> buildRealSequence random |> Seq.map (fun argValue -> [ argValue ])
   | WithConstArg arg -> Seq.initInfinite (fun _ -> [ arg ]))
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

let makeRandom fn argType =
  System.Random(Hash.string $"{fn}:{argType}" |> int32)

let argLengthPairs =
  [ //
    200, 300
    250, 250
    250, 1
    250, 0
    1, 1
    1, 0
  ]

let aggContext = { GroupingLabels = [||]; Aggregators = [||] }

let anonContext =
  {
    BucketSeed = 0UL
    BaseLabels = []
    AnonymizationParams = AnonymizationParams.Default
  }


let testAnonAggregatorMerging fn argType =
  let random = makeRandom fn argType
  let ctx = aggContext, Some anonContext, None

  let testPair numAids (length1, length2) =
    let makeArgs = makeAnonArgs argType random numAids
    let args1 = makeArgs length1
    let args2 = makeArgs length2

    let DUMMY_ARGS_EXPRS =
      [
        (List.replicate numAids (ColumnReference(0, IntegerType))) |> ListExpr
        match argType with
        | WithConstArg arg -> Constant arg
        | _ -> ColumnReference(1, mapArgType argType)
      ]

    ensureConsistentMerging ctx fn args1 args2 DUMMY_ARGS_EXPRS
    ensureConsistentMerging ctx fn args2 args1 DUMMY_ARGS_EXPRS

  // Empty args can't indicate number of AID instances
  testPair 1 (0, 0)

  for numAids in 1..5 do
    argLengthPairs |> List.iter (testPair numAids)

let testStandardAggregatorMerging fn argType =
  let random = makeRandom fn argType
  let ctx = aggContext, None, None

  let testPair (length1, length2) =
    let makeArgs = makeStandardArgs argType random
    let args1 = makeArgs length1
    let args2 = makeArgs length2
    let DUMMY_ARGS_EXPRS = [ ColumnReference(1, mapArgType argType) ]
    ensureConsistentMerging ctx fn args1 args2 DUMMY_ARGS_EXPRS
    ensureConsistentMerging ctx fn args2 args1 DUMMY_ARGS_EXPRS

  argLengthPairs |> List.iter testPair

let DISTINCT, NON_DISTINCT = true, false

/// Verifies correct merging of an anonymizing aggregator (where first argument is the aid instances).
let testAnon distinct argType fn =
  testAnonAggregatorMerging (makeAgg distinct fn) argType

/// Verifies correct merging of a standard aggregator.
let testStandard distinct argType fn =
  testStandardAggregatorMerging (makeAgg distinct fn) argType

[<Fact>]
let ``Merging DiffixCount`` () =
  DiffixCount |> testAnon NON_DISTINCT WithIntegerArg
  DiffixCount |> testAnon NON_DISTINCT WithoutArg

[<Fact>]
let ``Merging DiffixCountNoise`` () =
  DiffixCountNoise |> testAnon NON_DISTINCT WithIntegerArg
  DiffixCountNoise |> testAnon NON_DISTINCT WithoutArg

[<Fact>]
let ``Merging DiffixSum`` () =
  DiffixSum |> testAnon NON_DISTINCT WithRealArg

[<Fact>]
let ``Merging distinct DiffixCount`` () =
  DiffixCount |> testAnon DISTINCT WithIntegerArg

[<Fact>]
let ``Merging DiffixLowCount`` () =
  DiffixLowCount |> testAnon NON_DISTINCT WithoutArg

[<Fact>]
let ``Merging CountHistogram`` () =
  CountHistogram |> testStandard NON_DISTINCT WithIntegerArg

[<Fact>]
let ``Merging DiffixCountHistogram`` () =
  DiffixCountHistogram |> testAnon NON_DISTINCT (WithConstArg(Integer 0L))

[<Fact>]
let ``Merging default aggregators`` () =
  Count |> testStandard NON_DISTINCT WithoutArg
  Count |> testStandard NON_DISTINCT WithIntegerArg
  Count |> testStandard DISTINCT WithIntegerArg
  Sum |> testStandard NON_DISTINCT WithRealArg
