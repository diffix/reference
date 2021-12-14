module OpenDiffix.Core.Bucket

let private addValuesToSeed seed (values: Value seq) =
  values |> Seq.map Value.toString |> Hash.strings seed

let make group aggregators executionContext =
  let bucketExecutionContext =
    { executionContext with
        NoiseLayers =
          { executionContext.NoiseLayers with
              BucketSeed = addValuesToSeed executionContext.NoiseLayers.BucketSeed group
          }
    }

  {
    Group = group
    RowCount = 0
    Aggregators = aggregators
    ExecutionContext = bucketExecutionContext
    Attributes = Dictionary<string, Value>()
  }

let getAttribute attr bucket =
  bucket.Attributes |> Dictionary.getOrDefault attr Null

let putAttribute attr value bucket = bucket.Attributes.[attr] <- value
