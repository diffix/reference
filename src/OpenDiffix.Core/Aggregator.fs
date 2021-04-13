namespace OpenDiffix.Core

type IAggregator =
  abstract Transition : Value list -> IAggregator
  abstract Final : EvaluationContext -> Value

module Aggregator =

  let private invalidArgs (values: Value list) = failwith $"Invalid arguments for aggregator: {values}"

  let private mapAidStructure callback aidMaps defaultValue (aidValues: Value array) =
    aidMaps
    |> Option.defaultValue (Array.create aidValues.Length defaultValue)
    |> Array.zip aidValues
    |> Array.map (fun (aidValue: Value, aidStructure) ->
      if aidValue = Null then aidStructure else callback aidValue aidStructure
    )
    |> Some

  let private updateAidMaps<'T> (aidsArray: Value) initial transition (aidMaps: Map<AidHash, 'T> array option) =
    match aidsArray with
    | Value.Array aidValues ->
        let fn =
          fun aidValue aidMap ->
            let aidHash = aidValue.GetHashCode()

            let newValue =
              match aidMap |> Map.tryFind aidHash with
              | Some value -> transition value
              | None -> initial

            Map.add aidHash newValue aidMap

        mapAidStructure fn aidMaps Map.empty aidValues
    | _ -> failwith "Expecting an AID array as input"

  let private addToPotentiallyMissingAidsSetsArray aidSets valueFn (aidValues: Value array) =
    mapAidStructure (fun aidValue -> aidValue.GetHashCode() |> valueFn |> Set.add) aidSets Set.empty aidValues

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

  type private DiffixCount(perAidCounts: Map<AidHash, int64> array option) =
    new() = DiffixCount(None)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ aidValues; Null ] -> perAidCounts |> updateAidMaps aidValues 0L id |> DiffixCount
        | [ aidValues ]
        | [ aidValues; _ ] ->
            perAidCounts
            |> updateAidMaps aidValues 1L (fun count -> count + 1L)
            |> DiffixCount
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final ctx = Anonymizer.count ctx.AnonymizationParams perAidCounts

  type private DiffixCountDistinct(aidSets: Set<AidHash> array option) =
    new() = DiffixCountDistinct(None)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ _aidValues; Null ] -> this
        | [ Value.Array aidValues; aidValue ] ->
            aidValues
            |> addToPotentiallyMissingAidsSetsArray aidSets (fun _ -> aidValue.GetHashCode())
            |> DiffixCountDistinct
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final ctx = Anonymizer.countAids aidSets ctx.AnonymizationParams |> int64 |> Integer

  type private DiffixLowCount(aidSets: Set<AidHash> array option) =
    new() = DiffixLowCount(None)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ Null ] -> this
        | [ Value.Array aidValues ] -> aidValues |> addToPotentiallyMissingAidsSetsArray aidSets id |> DiffixLowCount
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final ctx = Anonymizer.isLowCount aidSets ctx.AnonymizationParams |> Boolean

  let create _ctx fn : IAggregator =
    match fn with
    | AggregateFunction (Count, { Distinct = false }) -> Count() :> IAggregator
    | AggregateFunction (Count, { Distinct = true }) -> CountDistinct() :> IAggregator
    | AggregateFunction (Sum, { Distinct = false }) -> Sum() :> IAggregator
    | AggregateFunction (DiffixCount, { Distinct = false }) -> DiffixCount() :> IAggregator
    | AggregateFunction (DiffixCount, { Distinct = true }) -> DiffixCountDistinct() :> IAggregator
    | AggregateFunction (DiffixLowCount, _) -> DiffixLowCount() :> IAggregator
    | _ -> failwith "Invalid aggregator"
