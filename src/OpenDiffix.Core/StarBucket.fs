module OpenDiffix.Core.StarBucket

/// Merges all source aggregators into the target bucket.
let private mergeAllAggregatorsInto (targetBucket: Bucket) (sourceBucket: Bucket) =
  let targetAggregators = targetBucket.Aggregators

  sourceBucket.Aggregators |> Array.iteri (fun i -> targetAggregators.[i].Merge)

let private makeStarBucket aggregationContext anonymizationContext =
  // Group labels: '*'s for text and NULL for other, so that this can potentially be delivered
  // as a properly typed row in a SQL query result.
  let group =
    aggregationContext.GroupingLabels
    |> Array.map (fun expr -> if Expression.typeOf expr = StringType then String "*" else Null)

  let aggregators =
    aggregationContext.Aggregators
    |> Seq.map (fst >> Aggregator.create)
    |> Seq.toArray

  let starBucket = Bucket.make group aggregators (Some anonymizationContext)
  // Not currently used, but may be in the future.
  starBucket |> Bucket.putAttribute BucketAttributes.IS_STAR_BUCKET (Boolean true)
  starBucket

let private getBucketAggregate index aggregationContext bucket =
  bucket.Aggregators.[index].Final(aggregationContext, bucket.AnonymizationContext)

let hook
  callback
  (aggregationContext: AggregationContext)
  (anonymizationContext: AnonymizationContext)
  (buckets: Bucket seq)
  =
  let starBucket = makeStarBucket aggregationContext anonymizationContext
  let lowCountIndex = AggregationContext.lowCountIndex aggregationContext
  let diffixCountIndex = AggregationContext.diffixCountIndex aggregationContext

  let isInStarBucket bucket =
    let isAlreadyMerged =
      bucket
      |> Bucket.getAttribute BucketAttributes.IS_LED_MERGED
      |> Value.unwrapBoolean

    not isAlreadyMerged && Bucket.isLowCount lowCountIndex bucket aggregationContext

  let bucketsInStarBucket =
    buckets
    |> Seq.filter isInStarBucket
    |> Seq.map (mergeAllAggregatorsInto starBucket)
    |> Seq.length

  let isStarBucketLowCount =
    starBucket
    |> getBucketAggregate lowCountIndex aggregationContext
    |> Value.unwrapBoolean

  let suppressedAnonCount =
    // NOTE: we can have a star bucket consisting of a single suppressed bucket,
    // which won't be suppressed by itself (different noise seed). In such case,
    // we must enforce the suppression manually.
    if isStarBucketLowCount || bucketsInStarBucket < 2 then
      Null
    else
      starBucket |> getBucketAggregate diffixCountIndex aggregationContext

  callback suppressedAnonCount
  buckets
