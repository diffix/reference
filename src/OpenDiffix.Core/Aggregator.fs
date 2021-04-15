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
          fun aidValue ->
            Map.change
              (aidValue.GetHashCode())
              (function
              | Some value -> Some <| transition value
              | None -> Some initial)

        mapAidStructure fn aidMaps Map.empty aidValues
    | _ -> failwith "Expecting an AID array as input"

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

  type private DiffixCountDistinct(perAidValuesByAidType: Map<AidHash, Set<Value>> array option) =
    new() = DiffixCountDistinct(None)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ _aidValues; Null ] -> this
        | [ aidValues; value ] ->
            perAidValuesByAidType
            |> updateAidMaps aidValues (Set.singleton value) (fun set -> Set.add value set)
            |> DiffixCountDistinct
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final ctx =
        match perAidValuesByAidType with
        | None -> 0L |> Integer
        | Some perAidValuesByAidType -> Anonymizer.countDistinct perAidValuesByAidType ctx.AnonymizationParams

  type private DiffixLowCount(aidSets: Set<AidHash> array option) =
    new() = DiffixLowCount(None)

    interface IAggregator with
      member this.Transition values =
        match values with
        | [ Null ] -> this
        | [ Value.Array aidValues ] ->
            aidValues
            |> mapAidStructure (fun aidValue -> aidValue.GetHashCode() |> Set.add) aidSets Set.empty
            |> DiffixLowCount
        | _ -> invalidArgs values
        :> IAggregator

      member this.Final ctx =
        match aidSets with
        | None -> true |> Boolean
        | Some aidSets -> Anonymizer.isLowCount aidSets ctx.AnonymizationParams |> Boolean

  let create _ctx fn : IAggregator =
    match fn with
    | AggregateFunction (Count, { Distinct = false }) -> Count() :> IAggregator
    | AggregateFunction (Count, { Distinct = true }) -> CountDistinct() :> IAggregator
    | AggregateFunction (Sum, { Distinct = false }) -> Sum() :> IAggregator
    | AggregateFunction (DiffixCount, { Distinct = false }) -> DiffixCount() :> IAggregator
    | AggregateFunction (DiffixCount, { Distinct = true }) -> DiffixCountDistinct() :> IAggregator
    | AggregateFunction (DiffixLowCount, _) -> DiffixLowCount() :> IAggregator
    | _ -> failwith "Invalid aggregator"
