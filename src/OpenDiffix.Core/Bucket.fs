module OpenDiffix.Core.Bucket

let private addValuesToSeed seed (values: Value seq) =
  values |> Seq.map Value.toString |> Hash.strings seed

let make group aggregators anonymizationContext =
  let anonContextUpdater = fun context -> { context with BucketSeed = addValuesToSeed context.BucketSeed group }

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
  bucket.Aggregators.[lowCountIndex].Final(aggregationContext, bucket.AnonymizationContext)
  |> Value.unwrapBoolean
