module OpenDiffix.Core.Bucket

let make group aggregators anonymizationContext =
  let anonContextUpdater =
    fun context ->
      { context with
          BucketSeed =
            group
            |> Array.toList
            |> List.append context.BaseLabels
            |> Value.addToSeed context.BucketSeed
      }

  {
    Group = group
    RowCount = 0
    Aggregators = aggregators
    AnonymizationContext = Option.map anonContextUpdater anonymizationContext
    Attributes = Dictionary<string, Value>()
  }

let getAttribute attr bucket =
  bucket.Attributes |> Dictionary.getOrDefault attr Null

let putAttribute attr value bucket = bucket.Attributes.[attr] <- value

let isLowCount lowCountIndex bucket aggregationContext =
  bucket.Aggregators.[lowCountIndex].Final(aggregationContext, bucket.AnonymizationContext, None)
  |> Value.unwrapBoolean

let finalizeAggregate index aggregationContext bucket =
  bucket.Aggregators.[index].Final(aggregationContext, bucket.AnonymizationContext, None)
