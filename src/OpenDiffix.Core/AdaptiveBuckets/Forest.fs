module OpenDiffix.Core.AdaptiveBuckets.Forest

open System

open OpenDiffix.Core
open OpenDiffix.Core.AdaptiveBuckets.Combination
open OpenDiffix.Core.AdaptiveBuckets.Range

module rec Tree =
  type Subnodes = Node option array

  type Context =
    {
      Combination: Combination
      AnonymizationContext: AnonymizationContext
      mutable BuildTime: TimeSpan
    }

  type NodeData =
    {
      Id: int
      SnappedRanges: Ranges
      ActualRanges: Ranges
      Subnodes: Subnodes
      IsStub: bool
      mutable Contributions: Anonymizer.ContributionsState array
      Context: Context
    }

  type Row = { Values: float array; Aids: Value list }

  type Leaf = { Data: NodeData; Rows: Row list }

  type Branch = { Data: NodeData; Children: Map<int, Node> }

  type Node =
    | Leaf of Leaf
    | Branch of Branch

  let nodeData =
    function
    | Leaf leaf -> leaf.Data
    | Branch branch -> branch.Data

  let private getNextNodeId =
    let mutable nodeId = 0

    fun () ->
      nodeId <- nodeId + 1
      nodeId

  let createBucketRanges (nodeData: NodeData) =
    (nodeData.SnappedRanges, nodeData.ActualRanges)
    ||> Array.map2 (fun snapped actual -> if actual.IsSingularity() then actual else snapped)

  let noisyRowCount (nodeData: NodeData) =
    let anonContext = nodeData.Context.AnonymizationContext

    // Uses range midpoints as the "bucket labels" for seeding.
    let bucketSeed =
      nodeData
      |> createBucketRanges
      |> Array.map (fun range -> Real(range.Middle()))
      |> Array.toList
      |> List.append anonContext.BaseLabels // Always empty at the moment.
      |> Value.addToSeed anonContext.BucketSeed

    let anonContext = { anonContext with BucketSeed = bucketSeed }

    match Anonymizer.count anonContext nodeData.Contributions with
    | Some result -> result.AnonymizedCount
    | None -> anonContext.AnonymizationParams.Suppression.LowThreshold

  let dataCrossesLowThreshold lowThreshold (nodeData: NodeData) =
    let anonParams = nodeData.Context.AnonymizationContext.AnonymizationParams
    let lowCountParams = { anonParams.Suppression with LowThreshold = lowThreshold }

    // This recomputes HashSets every call. We may consider optimizing by storing and updating
    // the per-node AID hash-sets separately from `nodeData.Contributions`.
    nodeData.Contributions
    |> Seq.map (fun contribution -> HashSet(contribution.AidContributions.Keys))
    |> Anonymizer.isLowCount anonParams.Salt lowCountParams
    |> not

  let isSingularityNode (ranges: Ranges) =
    ranges |> Array.forall (fun range -> range.IsSingularity())

  let private isStubSubnode =
    function
    | Some node ->
      let nodeData = nodeData node
      let anonParams = nodeData.Context.AnonymizationContext.AnonymizationParams

      let stubLowTreshold =
        if isSingularityNode nodeData.ActualRanges then
          anonParams.AdaptiveBuckets.SingularityLowThreshold
        else
          anonParams.AdaptiveBuckets.RangeLowThreshold

      nodeData.IsStub || nodeData |> dataCrossesLowThreshold stubLowTreshold |> not
    | None -> true

  let private allSubnodesAreStubs (subnodes: Subnodes) =
    // 0-dim subnodes of 1-dim nodes are not considered stubs.
    subnodes.Length > 0 && subnodes |> Array.forall isStubSubnode

  let private initContributions aidDimensions : Anonymizer.ContributionsState array =
    Array.init aidDimensions (fun _ -> { AidContributions = Dictionary<AidHash, float>(); UnaccountedFor = 0.0 })

  let private hashAid (aidValue: Value) =
    match aidValue with
    | Integer i -> i |> System.BitConverter.GetBytes |> Hash.bytes
    | String s -> Hash.string s
    | _ -> failwith "Unsupported AID type."

  let private updateContributions (row: Row) (nodeData: NodeData) =
    (row.Aids, nodeData.Contributions)
    ||> Seq.iter2 (fun aidValue contribution ->
      match aidValue with
      // No AIDs, add to unaccounted value
      | Null -> contribution.UnaccountedFor <- contribution.UnaccountedFor + 1.0
      | aidValue ->
        let aidHash = hashAid aidValue

        let updatedContribution =
          match contribution.AidContributions.TryGetValue(aidHash) with
          | true, aidContribution -> aidContribution + 1.0
          | false, _ -> 1.0

        contribution.AidContributions.[aidHash] <- updatedContribution
    )

    (row.Values, nodeData.ActualRanges)
    ||> Array.iteri2 (fun index value actualRange -> nodeData.ActualRanges.[index] <- expandRange value actualRange)


  // Removes the low-count half, if one exists, from the specified range.
  let private getHalvedRange (leaf: Leaf) (dimension: int) (range: Range) =
    let aidsDimensions = leaf.Rows.Head.Aids.Length
    // HashSet is mutable, so we need to create a new object for each slot.
    let perHalfAidSets = Array.init 2 (fun _ -> Array.init aidsDimensions (fun _ -> HashSet<AidHash>()))

    leaf.Rows
    |> List.iter (fun row ->
      let halfIndex = range.HalfIndex(row.Values[dimension])
      let rowAidSets = perHalfAidSets.[halfIndex]

      row.Aids
      |> Seq.iteri (fun aidDimension aid ->
        if aid <> Null then
          aid |> hashAid |> rowAidSets.[aidDimension].Add |> ignore
      )
    )

    let anonParams = leaf.Data.Context.AnonymizationContext.AnonymizationParams

    match
      perHalfAidSets
      |> Array.map (Anonymizer.isLowCount anonParams.Salt anonParams.Suppression)
    with
    | [| false; true |] -> range.Half(0)
    | [| true; false |] -> range.Half(1)
    | _ -> range

  let getHalvedRanges (node: Node) =
    match node with
    | Leaf leaf ->
      if leaf.Data.IsStub then
        leaf.Data.SnappedRanges |> Array.mapi (getHalvedRange leaf)
      else
        leaf.Data.SnappedRanges
    | Branch branch -> branch.Data.SnappedRanges

  let createLeaf context snappedRanges subnodes initialRow =
    let nodeData =
      {
        Id = getNextNodeId ()
        SnappedRanges = snappedRanges
        Subnodes = subnodes
        IsStub = allSubnodesAreStubs (subnodes)
        ActualRanges = (initialRow.Values, initialRow.Values) ||> Array.map2 createRange
        Contributions = initContributions initialRow.Aids.Length
        Context = context
      }

    updateContributions initialRow nodeData

    Leaf { Data = nodeData; Rows = [ initialRow ] }

  // Each dimension corresponds to a bit in index, where 0 means the lower range half, and 1 means the upper range half.
  let private findChildIndex (values: float array) (snappedRanges: Ranges) =
    (0, values, snappedRanges)
    |||> Array.fold2 (fun index value range -> (index <<< 1) ||| range.HalfIndex(value))

  let private removeDimensionFromIndex position index =
    let lowerMask = (1 <<< position) - 1
    let upperMask = ~~~((1 <<< (position + 1)) - 1)
    // Remove missing position bit from index.
    ((index &&& upperMask) >>> 1) ||| (index &&& lowerMask)

  let private createChildLeaf (childIndex: int) (parent: Branch) (initialRow: Row) =
    // Create child's ranges by halfing parent's ranges, using the corresponding bit in the index to select the correct half.
    let snappedRanges, _ =
      Array.mapFoldBack
        (fun (range: Range) (index: int) ->
          let halfRange = range.Half(index &&& 1)
          halfRange, index >>> 1
        )
        parent.Data.SnappedRanges
        childIndex

    // Set child's subnodes to the matching-range children of the parent's subnodes.
    let subnodes, _ =
      parent.Data.Subnodes
      |> Array.mapFold
        (fun position subnode ->
          let subnode =
            match subnode with
            | Some(Branch subnodeBranch) ->
              childIndex
              |> removeDimensionFromIndex position
              |> subnodeBranch.Children.TryFind
            | _ -> None

          subnode, position + 1
        )
        0

    createLeaf parent.Data.Context snappedRanges subnodes initialRow

  let private leafShouldBeSplit (leaf: Leaf) =
    let anonParams = leaf.Data.Context.AnonymizationContext.AnonymizationParams

    (not leaf.Data.IsStub)
    && leaf.Data.ActualRanges |> isSingularityNode |> not
    && dataCrossesLowThreshold anonParams.Suppression.LowThreshold leaf.Data

  let rec addRow (node: Node) (row: Row) =
    match node with
    | Leaf leaf ->
      updateContributions row leaf.Data

      if leafShouldBeSplit leaf then
        // Convert current leaf node into a new branch node and insert previously gathered rows down the tree.
        let branch =
          Branch
            {
              Data =
                { leaf.Data with
                    Contributions = initContributions leaf.Data.Contributions.Length
                }
              Children = Map.empty
            }

        List.fold addRow branch (row :: leaf.Rows)
      else
        Leaf { leaf with Rows = row :: leaf.Rows }
    | Branch branch ->
      let childIndex = findChildIndex row.Values branch.Data.SnappedRanges

      let newChild =
        match Map.tryFind childIndex branch.Children with
        | Some child -> addRow child row
        | None -> createChildLeaf childIndex branch row

      updateContributions row branch.Data

      Branch { branch with Children = Map.add childIndex newChild branch.Children }

  let private getLowCountRowsInChild childIndex (branch: Branch) =
    match branch.Children.TryFind childIndex with
    | Some(Leaf leaf) ->
      let lowThreshold = leaf.Data.Context.AnonymizationContext.AnonymizationParams.Suppression.LowThreshold
      if dataCrossesLowThreshold lowThreshold leaf.Data then None else Some leaf.Rows
    | Some _ -> None
    | None -> Some []

  let rec pushDown1DimRoot root =
    match root with
    | Leaf _ -> root
    | Branch branch ->
      match getLowCountRowsInChild 0 branch, getLowCountRowsInChild 1 branch with
      | None, Some rows -> rows |> List.fold addRow branch.Children.[0] |> pushDown1DimRoot
      | Some rows, None -> rows |> List.fold addRow branch.Children.[1] |> pushDown1DimRoot
      | _ -> root

  // Casts a `Value` to a `float` in order to match it against a `Range`.
  let castValueToFloat value =
    match value with
    | Null -> None
    | Boolean true -> Some 1.0
    | Boolean false -> Some 0.0
    | Integer i -> Some(float i)
    | Real r -> Some r // TODO: handle non-finite reals.
    | String s -> Some(hashStringToFloat s)
    | Timestamp t -> Some (t - TIMESTAMP_REFERENCE).TotalSeconds
    | List _ -> failwith "Unsupported list type for casted value."

  let mapRow (nullMappings: float array) (row: Value array) =
    let values =
      row
      |> Array.tail
      |> Array.map castValueToFloat
      |> Array.map2 Option.defaultValue nullMappings

    let aids =
      match Array.head row with
      | List aids -> aids
      | _ -> failwith "Expecting a list of AIDs as first value in row."

    { Values = values; Aids = aids }


type Tree = Tree.Node

let private getSubnodes
  (lowerLevel: int)
  (dimensions: int)
  (upperCombination: Combination)
  (getTree: Combination -> Tree)
  =
  generateCombinations lowerLevel dimensions
  |> Seq.filter (fun lowerCombination -> isSubsetCombination lowerCombination upperCombination)
  |> Seq.map (Some << getTree)
  |> Seq.toArray

let private getActualRanges dimensions (rows: Row array) =
  rows
  |> Array.fold
    (fun boundaries row ->
      row
      |> Array.tail
      |> Array.map Tree.castValueToFloat
      |> Array.map2
        (fun boundary currentValue ->
          currentValue
          |> Option.map (fun currentValue ->
            boundary
            |> Option.map (fun (minValue, maxValue) -> min minValue currentValue, max maxValue currentValue)
            |> Option.orElse (Some(currentValue, currentValue))
          )
          |> Option.defaultValue boundary
        )
        boundaries
    )
    (Array.create dimensions None)
  |> Array.map (fun boundary -> boundary |> Option.defaultValue (0.0, 0.0) ||> createRange)

let mapNulls range =
  if range.Max > 0 then 2.0 * range.Max
  elif range.Min < 0 then 2.0 * range.Min
  else 1.0

type Forest(rows: Row seq, dimensions: int, anonContext: AnonymizationContext) =
  let rows = Seq.toArray rows

  let nullMappings, snappedRanges =
    rows
    |> getActualRanges dimensions
    |> Array.map (fun range ->
      let nullMapping = mapNulls range
      nullMapping, range |> expandRange nullMapping |> snapRange
    )
    |> Array.unzip

  let mappedRows = rows |> Array.map (Tree.mapRow nullMappings)

  let treeCache = Dictionary<Combination, Tree>(LanguagePrimitives.FastGenericEqualityComparer)

  let rec getTree (combination: Combination) =
    treeCache |> Dictionary.getOrInit combination (fun () -> buildTree combination)

  and buildTree (combination: Combination) =
    let level = combination.Length

    // Build lower levels first because 1-Dim trees might mutate `snappedRanges`.
    let subnodes = getSubnodes (level - 1) dimensions combination getTree

    let stopwatch = Diagnostics.Stopwatch.StartNew()

    let treeContext: Tree.Context =
      {
        Combination = combination
        AnonymizationContext = anonContext
        BuildTime = TimeSpan.Zero
      }

    let combinationRow =
      fun (row: Tree.Row) -> { row with Values = getItemsCombination combination row.Values }

    // Warning: Do not access `snappedRanges` before 1-Dim trees are built.
    let rootRanges = getItemsCombination combination snappedRanges

    let root =
      mappedRows
      |> Array.head
      |> combinationRow
      |> Tree.createLeaf treeContext rootRanges subnodes

    let mutable tree = mappedRows |> Seq.tail |> Seq.map combinationRow |> Seq.fold Tree.addRow root

    if level = 1 then
      // We need to flatten uni-dimensional trees, by pushing the root down as long as one of its halves
      // fails LCF, and update the corresponding dimension's range, in order to anonymize its boundaries.
      tree <- Tree.pushDown1DimRoot tree
      snappedRanges.[combination.[0]] <- (Tree.nodeData tree).SnappedRanges.[0]

    treeContext.BuildTime <- stopwatch.Elapsed
    tree

  // ----------------------------------------------------------------
  // Public interface
  // ----------------------------------------------------------------

  member this.AnonymizationContext = anonContext
  member this.Rows = rows
  member this.Dimensions = dimensions
  member this.NullMappings = nullMappings

  member this.GetTree(combination: Combination) = getTree combination


let buildForest (anonContext: AnonymizationContext) (dimensions: int) (rows: Row seq) =
  let forest = Forest(rows, dimensions, anonContext)
  forest.GetTree([| 0 .. forest.Dimensions - 1 |]), forest.NullMappings
