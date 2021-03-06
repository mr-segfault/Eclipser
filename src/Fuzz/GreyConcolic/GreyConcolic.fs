module Eclipser.GreyConcolic

open Config
open Utils
open Options

// Mutable variables for statistics management.
let mutable private recentExecNums: Queue<int> = Queue.empty
let mutable private recentNewPathNums: Queue<int> = Queue.empty

let updateStatus opt execN newPathN =
  let recentExecNums' = if Queue.getSize recentExecNums > RecentRoundN
                        then Queue.drop recentExecNums
                        else recentExecNums
  recentExecNums <- Queue.enqueue recentExecNums' execN
  let recentNewPathNums' = if Queue.getSize recentNewPathNums > RecentRoundN
                           then Queue.drop recentNewPathNums
                           else recentNewPathNums
  recentNewPathNums <- Queue.enqueue recentNewPathNums' newPathN

let evaluateEfficiency () =
  let execNum = List.sum (Queue.elements recentExecNums)
  let newPathNum = List.sum (Queue.elements recentNewPathNums)
  if execNum = 0 then 1.0 else float newPathNum / float execNum

let printFoundSeed seed newNodeN =
  let seedStr = Seed.toString seed
  let nodeStr = if newNodeN > 0 then sprintf "(%d new nodes) " newNodeN else ""
  log "[*] Found by grey-box concolic %s: %s" nodeStr seedStr

let evalSeedsAux opt accSeeds seed =
  let newNodeN, pathHash, nodeHash, exitSig = Executor.getCoverage opt seed
  let isNewPath = Manager.storeSeed opt seed newNodeN pathHash nodeHash exitSig
  if newNodeN > 0 && opt.Verbosity >= 0 then printFoundSeed seed newNodeN
  if isNewPath && not (Signal.isTimeout exitSig) && not (Signal.isCrash exitSig)
  then let priority = if newNodeN > 0 then Favored else Normal
       (priority, seed) :: accSeeds
  else accSeeds

let evalSeeds opt items =
  List.fold (evalSeedsAux opt) [] items |> List.rev // To preserve order

let checkByProducts opt spawnedSeeds =
  evalSeeds opt spawnedSeeds

let run seed opt =
  let curByteVal = Seed.getCurByteVal seed
  let minByte, maxByte = ByteVal.getMinMax curByteVal seed.SourceCursor
  if minByte = maxByte then
    let seedStr = Seed.toString seed
    failwithf "Cursor pointing to Fixed ByteVal %s" seedStr
  let minVal, maxVal = bigint (int minByte), bigint (int maxByte)
  let branchTraces, spawnSeeds = BranchTrace.collect seed opt minVal maxVal
  let byteDir = Seed.getByteCursorDir seed
  let bytes = Seed.queryNeighborBytes seed byteDir
  let ctx = { Bytes = bytes; ByteDir = byteDir }
  let branchTree = BranchTree.make opt ctx branchTraces
  let branchTree = BranchTree.selectAndRepair opt branchTree
  GreySolver.clearSolutionCache ()
  let solutions = GreySolver.solve seed opt byteDir branchTree
  evalSeeds opt solutions @ checkByProducts opt spawnSeeds
