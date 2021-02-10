namespace OpenDiffix.Core

type IAggregator =
  abstract Transition: Value list -> IAggregator
  abstract Final: EvaluationContext -> Value

module Aggregator =

  let private invalidArgs (values: Value list) = failwith $"Invalid arguments for aggregator: {values}"

  let private updateAidMap<'T> (aid: Value) initial transition (aidMap: Map<AidHash, 'T>) =
    let aidHash = aid.GetHashCode()

    let newValue =
      match aidMap |> Map.tryFind aidHash with
      | Some value -> transition value
      | None -> initial

    Map.add aidHash newValue aidMap

  type private Count(counter) =
    new() = Count(0L)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ Null ] -> this
        | _ -> Count(counter + 1L)
        :> IAggregator

      member this.Final _ctx = Integer counter

  type private CountDistinct(set: Set<Value>) =
    new() = CountDistinct(Set.empty)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ Null ] -> this
        | [ value ] -> set |> Set.add value |> CountDistinct
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final _ctx = set.Count |> int64 |> Integer

  type private Sum(sum: Value) =
    new() = Sum(Null)

    interface IAggregator with
      member this.Transition values =
        match sum, values with
        | _, [ Null ] -> this
        | Null, [ value ] -> Sum(value)
        | Integer oldValue, [ Integer value ] -> (oldValue + value) |> Integer |> Sum
        | Real oldValue, [ Real value ] -> (oldValue + value) |> Real |> Sum
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final _ctx = sum

  type private DiffixCount(perAidCounts: Map<AidHash, int64>) =
    new() = DiffixCount(Map.empty)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ _aid; Null ] -> this
        | [ aid ]
        | [ aid; _ ] -> perAidCounts |> updateAidMap aid 1L (fun count -> count + 1L) |> DiffixCount
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final ctx = Anonymizer.count ctx.AnonymizationParams perAidCounts

  type private DiffixCountDistinct(aids: Set<AidHash>) =
    new() = DiffixCountDistinct(Set.empty)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ Null ] -> this
        | [ aid ] -> aids |> Set.add (aid.GetHashCode()) |> DiffixCountDistinct
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final ctx = Anonymizer.countAids aids ctx.AnonymizationParams |> Integer

  type private DiffixLowCount(aids: Set<AidHash>) =
    new() = DiffixLowCount(Set.empty)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ Null ] -> this
        | [ aid ] -> aids |> Set.add (aid.GetHashCode()) |> DiffixLowCount
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final ctx =
        let count = aids.Count
        Anonymizer.isLowCount aids ctx.AnonymizationParams count |> Boolean

  let create _ctx fn: IAggregator =
    match fn with
    | AggregateFunction (Count, { Distinct = false }) -> Count() :> IAggregator
    | AggregateFunction (Count, { Distinct = true }) -> CountDistinct() :> IAggregator
    | AggregateFunction (Sum, { Distinct = false }) -> Sum() :> IAggregator
    | AggregateFunction (DiffixCount, { Distinct = false }) -> DiffixCount() :> IAggregator
    | AggregateFunction (DiffixCount, { Distinct = true }) -> DiffixCountDistinct() :> IAggregator
    | AggregateFunction (DiffixLowCount, _) -> DiffixLowCount() :> IAggregator
    | _ -> failwith "Invalid aggregator"
