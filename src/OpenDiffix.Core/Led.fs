module OpenDiffix.Core.Led

open System
open CommonTypes

let private startStopwatch () = Diagnostics.Stopwatch.StartNew()

let private logDebug msg = Logger.debug $"[LED] {msg}"

let inline private uncheckedAdd x y = Operators.op_Addition x y
let inline private uncheckedMul x y = Operators.op_Multiply x y

let private fastRowHash (row: Row) =
  let mutable hash = 0

  for value in row do
    hash <- uncheckedAdd (uncheckedMul hash 9176) (value.GetHashCode())

  Math.Abs(hash)

/// Converts the sequence to an array, casting if it's already one.
let private toArray (seq: 'a seq) : 'a array =
  match seq with
  | :? ('a array) as arr -> arr
  | _ -> Seq.toArray seq

// ----------------------------------------------------------------
// Bucket merging
// ----------------------------------------------------------------

/// Returns an array of indexes pointing to anonymizing aggregators (diffix count, low count).
let private anonymizingAggregatorIndexes (aggregationContext: AggregationContext) =
  aggregationContext.Aggregators
  |> Seq.map fst
  |> Seq.indexed
  |> Seq.choose (fun (i, aggSpec) -> if Aggregator.isAnonymizing aggSpec then Some i else None)
  |> Seq.toArray

/// Merges only given indexes from source bucket to destination bucket.
let private mergeGivenAggregatorsInto (targetBucket: Bucket) aggIndexes (sourceBucket: Bucket) =
  targetBucket.RowCount <- targetBucket.RowCount + 1

  let sourceAggregators = sourceBucket.Aggregators
  let targetAggregators = targetBucket.Aggregators

  for i in aggIndexes do
    targetAggregators.[i].Merge(sourceAggregators.[i])

// ----------------------------------------------------------------
// Main LED
// ----------------------------------------------------------------

type private Interlocked = Threading.Interlocked

type private LedDiagnostics() =
  // Bucket size vs. how many merges for that size occurred
  let mergeHistogram = Dictionary<int, int>()

  // How many buckets with isolating columns are found
  let mutable bucketsIsolatingColumn = 0

  // How many buckets with unknown columns are found
  let mutable bucketsUnknownColumn = 0

  // How many buckets are merged (at least once)
  let mutable bucketsMerged = 0

  member this.IncrementBucketsIsolatingColumn() =
    Interlocked.Increment(&bucketsIsolatingColumn) |> ignore

  member this.IncrementBucketsUnknownColumn() =
    Interlocked.Increment(&bucketsUnknownColumn) |> ignore

  member this.IncrementBucketsMerged() =
    Interlocked.Increment(&bucketsMerged) |> ignore

  member this.IncrementMerge(bucketCount: int) =
    // Not thread safe!
    mergeHistogram |> Dictionary.increment bucketCount

  member this.Print(totalBuckets: int, suppressedBuckets: int) =
    let mergeHistogramStr =
      mergeHistogram
      |> Seq.sortBy (fun pair -> pair.Key)
      |> Seq.map (fun pair -> $"({pair.Key}, {pair.Value})")
      |> Seq.append [ $"(*, {mergeHistogram |> Seq.sumBy (fun pair -> pair.Value)})" ]
      |> String.join " "

    logDebug $"Total buckets: {totalBuckets}"
    logDebug $"Suppressed buckets: {suppressedBuckets}"
    logDebug $"Buckets with isolating column(s): {bucketsIsolatingColumn}"
    logDebug $"Buckets with unknown column(s): {bucketsUnknownColumn}"
    logDebug $"Merged buckets: {bucketsMerged}"
    logDebug $"Merge distribution: {mergeHistogramStr}"

/// Reference wrapper for a System.Threading.SpinLock struct.
type private SpinLock() =
  let mutable lock = Threading.SpinLock()

  member this.Enter() =
    let mutable entered = false
    lock.Enter(&entered)

    if not entered then
      failwith "Failed to enter lock."

  member this.Exit() = lock.Exit()

type private SiblingsPerColumn = MutableList<Bucket * bool>

let private initSiblings () = SiblingsPerColumn(3)

/// Copies source array but skips the given index.
let private sliceArray skipAt (source: Row) =
  let targetLength = source.Length - 1
  let target = Array.zeroCreate targetLength
  Array.blit source 0 target 0 skipAt
  Array.blit source (skipAt + 1) target skipAt (targetLength - skipAt)
  target

/// Returns victim buckets and their siblings per column.
let private prepareBuckets (aggregationContext: AggregationContext) (buckets: Bucket array) =
  let stopwatch = startStopwatch ()
  let lowCountIndex = AggregationContext.lowCountIndex aggregationContext
  let groupingLabelsLength = aggregationContext.GroupingLabels.Length

  let CACHE_DISTRIBUTION = 53 // Prime number for better distribution, determined empirically.

  // For each column, we build a cache where we group buckets by their labels EXCLUDING that column.
  // This means that every cache will associate siblings where the respective column is different.
  // Dictionaries are spread in CACHE_DISTRIBUTION parts to reduce lock contention.
  let siblingCaches =
    Array.init
      groupingLabelsLength
      (fun _col ->
        Array.init
          CACHE_DISTRIBUTION
          (fun _cacheNo -> Dictionary<Row, SiblingsPerColumn>(Row.equalityComparer), SpinLock())
      )

  // Filters low count buckets and builds caches in the same pass.
  let victimBuckets =
    buckets
    |> Array.Parallel.choose (fun bucket ->
      let lowCount = Bucket.isLowCount lowCountIndex bucket aggregationContext
      let bucketTuple = bucket, lowCount
      let columnValues = bucket.Group
      let siblingsPerColumn = Array.zeroCreate<SiblingsPerColumn> groupingLabelsLength

      // Put entries in caches and store a reference to the (mutable) list of siblings.
      for colIndex in 0 .. groupingLabelsLength - 1 do
        let key = sliceArray colIndex columnValues
        let keyHash = fastRowHash key
        let cache, lock = siblingCaches.[colIndex].[keyHash % CACHE_DISTRIBUTION]

        // Get exclusive access to this cache.
        lock.Enter()
        let siblings = cache |> Dictionary.getOrInit key initSiblings
        siblingsPerColumn.[colIndex] <- siblings
        // We don't actually need more than 3 values.
        // 1 = no siblings; 2 = single sibling; 3 = multiple siblings
        if siblings.Count < 3 then
          siblings.Add(bucketTuple)

        lock.Exit()

      if lowCount then Some(bucket, siblingsPerColumn) else None
    )

  logDebug $"Cache building took {stopwatch.Elapsed}"

  victimBuckets

/// Main LED logic.
let private led (aggregationContext: AggregationContext) (buckets: Bucket array) =
  let diagnostics = LedDiagnostics()
  let victimBuckets = prepareBuckets aggregationContext buckets

  let stopwatch = startStopwatch ()

  let anonAggregators = anonymizingAggregatorIndexes aggregationContext
  let groupingLabelsLength = aggregationContext.GroupingLabels.Length

  /// Returns `Some(victimBucket, mergeTargets)` if victimBucket has an unknown column
  /// and has merge targets (siblings which match isolating column criteria).
  let chooseMergeTargets (victimBucket: Bucket, siblingsPerColumn: SiblingsPerColumn array) =
    let mutable hasUnknownColumn = false
    let mergeTargets = MutableList()

    for colIndex in 0 .. groupingLabelsLength - 1 do
      let siblings = siblingsPerColumn.[colIndex]

      match siblings.Count with
      | 1 ->
        // No siblings for this column (Count=1 means itself).
        // A column without siblings is an unknown column.
        hasUnknownColumn <- true
      | 2 ->
        // Single sibling (self+other). Find it and check if it's high count.
        // A column with a single high-count sibling is an isolating column.
        for siblingBucket, siblingLowCount in siblings do
          if not (obj.ReferenceEquals(victimBucket, siblingBucket)) && not siblingLowCount then
            mergeTargets.Add(siblingBucket)
      | _ ->
        // Count=3 means there are multiple siblings and has no special meaning.
        ()

    if hasUnknownColumn then
      diagnostics.IncrementBucketsUnknownColumn()

    if mergeTargets.Count > 0 then
      diagnostics.IncrementBucketsIsolatingColumn()

    if hasUnknownColumn && mergeTargets.Count > 0 then
      Some(victimBucket, mergeTargets)
    else
      None

  victimBuckets
  |> Array.Parallel.choose chooseMergeTargets
  |> Array.iter (fun (victimBucket, mergeTargets) ->
    diagnostics.IncrementBucketsMerged()
    Bucket.putAttribute BucketAttributes.IS_LED_MERGED (Boolean true) victimBucket

    for siblingBucket in mergeTargets do
      victimBucket |> mergeGivenAggregatorsInto siblingBucket anonAggregators
      diagnostics.IncrementMerge(victimBucket.RowCount)
  )

  diagnostics.Print(buckets.Length, victimBuckets.Length)
  logDebug $"Led merging took {stopwatch.Elapsed}"

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let hook (aggregationContext: AggregationContext) (_anonymizationContext: AnonymizationContext) (buckets: Bucket seq) =
  let stopwatch = startStopwatch ()

  try
    // LED requires at least 2 columns - an isolating column and an unknown column.
    // With exactly 2 columns attacks are not useful because
    // they would have to isolate victims against the whole dataset.
    if aggregationContext.GroupingLabels.Length <= 2 then
      buckets
    else
      let buckets = toArray buckets
      led aggregationContext buckets
      buckets :> Bucket seq
  finally
    logDebug $"Hook took {stopwatch.Elapsed}"
