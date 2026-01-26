using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Mem2Reg pass - promotes memory allocations to SSA values.
/// This pass converts local scalar variables from alloca/load/store form
/// to direct SSA value form, enabling further optimizations like constant folding.
///
/// Supports both single-block and cross-block promotion:
/// - Single-block: simple reaching definition analysis within one block
/// - Cross-block: inserts block arguments at merge points to maintain SSA form
/// </summary>
public sealed class Mem2RegPass : FunctionPass {
	public override string Name => "mem2reg";
	public override string Description => "Promotes memory allocations to SSA values";

	// Limit cross-block promotions to avoid register pressure and value clobbering
	// (until proper parallel copy handling is implemented)
	private const int MaxCrossBlockPromotions = 2;

	protected override bool RunOnFunction(MlirFunction func) {
		bool anyChanged = false;
		bool changed;
		int promoted = 0;
		int crossBlockPromoted = 0;
		int iterations = 0;

		Logger.Debug(LogCategory.Optimizer, $"mem2reg: processing {func.Name}");

		do {
			changed = false;
			iterations++;

			foreach (var block in func.Body.Blocks) {
				var allocas = block.Operations.OfType<AllocaOp>().ToList();

				foreach (var alloca in allocas) {
					if (TryPromote(alloca, block, func, crossBlockPromoted >= MaxCrossBlockPromotions, out bool isCrossBlock)) {
						Logger.Trace(LogCategory.Optimizer, $"  promoted alloca %{alloca.Result.Id} to SSA{(isCrossBlock ? " (cross-block)" : "")}");
						promoted++;
						if (isCrossBlock) crossBlockPromoted++;
						changed = true;
					}
				}
			}

			anyChanged |= changed;
		} while (changed);

		if (promoted > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: promoted {promoted} allocas ({crossBlockPromoted} cross-block) in {iterations} iteration(s)");
		}

		return anyChanged;
	}

	// ============================================================================
	// Alloca Use Collection
	// ============================================================================

	/// <summary>
	/// Holds collected uses of an alloca across a function.
	/// </summary>
	private sealed class AllocaUses {
		public List<(LoadOp Op, MlirBlock Block)> Loads { get; } = [];
		public List<(StoreOp Op, MlirBlock Block)> Stores { get; } = [];
		public List<MlirOperation> OtherUses { get; } = [];

		public HashSet<MlirBlock> BlocksWithStores => [.. Stores.Select(s => s.Block).Distinct()];
		public HashSet<MlirBlock> BlocksWithLoads => [.. Loads.Select(l => l.Block).Distinct()];
		public HashSet<MlirBlock> AllUsedBlocks => [.. BlocksWithStores.Union(BlocksWithLoads)];
	}

	/// <summary>
	/// Collects all uses of an alloca across the function.
	/// </summary>
	private static AllocaUses CollectAllocaUses(AllocaOp alloca, MlirFunction func) {
		var uses = new AllocaUses();

		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				if (op is LoadOp load && IsAllocaAccess(load.MemRef, load.Indices, alloca)) {
					uses.Loads.Add((load, block));
				} else if (op is StoreOp store && IsAllocaAccess(store.MemRef, store.Indices, alloca)) {
					uses.Stores.Add((store, block));
				} else if (op.Operands.Contains(alloca.Result) && op != alloca) {
					uses.OtherUses.Add(op);
				}
			}
		}

		return uses;
	}

	/// <summary>
	/// Checks if a memory access is a simple scalar access to the given alloca.
	/// </summary>
	private static bool IsAllocaAccess(MlirValue memRef, IReadOnlyList<MlirValue> indices, AllocaOp alloca) {
		return memRef == alloca.Result && indices.Count == 0;
	}

	// ============================================================================
	// Promotion
	// ============================================================================

	/// <summary>
	/// Tries to promote an alloca to SSA form.
	/// Handles both single-block and cross-block cases.
	/// </summary>
	private static bool TryPromote(AllocaOp alloca, MlirBlock allocaBlock, MlirFunction func,
		bool skipCrossBlock, out bool isCrossBlock) {

		isCrossBlock = false;

		var uses = CollectAllocaUses(alloca, func);

		// Can't promote if address escapes
		if (uses.OtherUses.Count > 0) return false;

		// Can't promote if there are no stores (uninitialized variable)
		if (uses.Stores.Count == 0 && uses.Loads.Count > 0) return false;

		// Can't promote pointer/memref types
		foreach (var (store, _) in uses.Stores) {
			if (store.Value.Type is MemRefType or PtrType) return false;
		}

		// Single-block case: all uses in the alloca's block
		bool isSingleBlock = uses.AllUsedBlocks.All(b => b == allocaBlock);

		if (isSingleBlock) {
			return TryPromoteSingleBlock(uses, allocaBlock, alloca, func);
		}

		// Cross-block case
		if (skipCrossBlock) return false;

		isCrossBlock = true;
		return TryPromoteCrossBlock(uses, allocaBlock, alloca, func);
	}

	/// <summary>
	/// Promotes an alloca where all uses are in a single block.
	/// </summary>
	private static bool TryPromoteSingleBlock(AllocaUses uses, MlirBlock allocaBlock,
		AllocaOp alloca, MlirFunction func) {

		var loadReplacements = new Dictionary<LoadOp, MlirValue>();

		foreach (var (load, _) in uses.Loads) {
			var lastStoredValue = FindLastStoreBefore(allocaBlock, load, alloca);
			if (lastStoredValue == null) return false;
			loadReplacements[load] = lastStoredValue;
		}

		ApplyPromotion(uses, loadReplacements, allocaBlock, alloca, func);
		return true;
	}

	/// <summary>
	/// Promotes an alloca that spans multiple blocks using block arguments.
	/// </summary>
	private static bool TryPromoteCrossBlock(AllocaUses uses, MlirBlock allocaBlock,
		AllocaOp alloca, MlirFunction func) {

		if (alloca.Result.Type is not MemRefType allocaType) return false;
		var elementType = allocaType.ElementType;

		// Find merge points needing block arguments
		var mergeBlocks = FindMergeBlocks(func, uses.BlocksWithStores, uses.BlocksWithLoads);

		// Skip if involves loops (back-edges cause value clobbering issues)
		if (HasBackEdges(mergeBlocks)) return false;

		// Verify all loads can be resolved before modifying IR
		foreach (var (load, loadBlock) in uses.Loads) {
			if (!CanResolveLoad(load, loadBlock, alloca, mergeBlocks, func)) {
				Logger.Debug(LogCategory.Optimizer, $"    cannot promote: no reaching definition for load in ^{loadBlock.Name}");
				return false;
			}
		}

		// Insert block arguments at merge blocks
		var blockArguments = new Dictionary<MlirBlock, MlirValue>();
		foreach (var mergeBlock in mergeBlocks) {
			var arg = mergeBlock.AddArgument(elementType);
			blockArguments[mergeBlock] = arg;
			Logger.Trace(LogCategory.Optimizer, $"    inserted block argument %{arg.Id} at ^{mergeBlock.Name}");
		}

		var reachingDefs = ComputeReachingDefinitions(func, alloca, blockArguments);
		UpdateBranchOperands(func, alloca, mergeBlocks, reachingDefs);

		// Build load replacements
		var loadReplacements = new Dictionary<LoadOp, MlirValue>();
		foreach (var (load, loadBlock) in uses.Loads) {
			var reachingValue = GetReachingValueAtLoad(load, loadBlock, alloca, reachingDefs);
			if (reachingValue != null) {
				loadReplacements[load] = reachingValue;
			}
		}

		ApplyPromotion(uses, loadReplacements, allocaBlock, alloca, func);
		return true;
	}

	/// <summary>
	/// Finds the last store to an alloca before a given load in the same block.
	/// </summary>
	private static MlirValue? FindLastStoreBefore(MlirBlock block, LoadOp load, AllocaOp alloca) {
		var loadIdx = block.Operations.IndexOf(load);
		for (int i = loadIdx - 1; i >= 0; i--) {
			if (block.Operations[i] is StoreOp store && IsAllocaAccess(store.MemRef, store.Indices, alloca)) {
				return store.Value;
			}
		}
		return null;
	}

	/// <summary>
	/// Applies the promotion by replacing loads, removing stores, and removing the alloca.
	/// </summary>
	private static void ApplyPromotion(AllocaUses uses, Dictionary<LoadOp, MlirValue> loadReplacements,
		MlirBlock allocaBlock, AllocaOp alloca, MlirFunction func) {

		foreach (var (load, replacement) in loadReplacements) {
			ReplaceAllUses(load.Result, replacement, func);
		}

		foreach (var (load, loadBlock) in uses.Loads) {
			loadBlock.Operations.Remove(load);
		}

		foreach (var (store, storeBlock) in uses.Stores) {
			storeBlock.Operations.Remove(store);
		}

		allocaBlock.Operations.Remove(alloca);
	}

	private static void ReplaceAllUses(MlirValue oldValue, MlirValue newValue, MlirFunction func) {
		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				for (int i = 0; i < op.Operands.Count; i++) {
					if (op.Operands[i] == oldValue) {
						op.Operands[i] = newValue;
					}
				}
			}
		}
	}

	// ============================================================================
	// Cross-Block Analysis
	// ============================================================================

	private static HashSet<MlirBlock> FindMergeBlocks(MlirFunction func,
		HashSet<MlirBlock> blocksWithStores, HashSet<MlirBlock> blocksWithLoads) {

		var mergeBlocks = new HashSet<MlirBlock>();
		var blocksReachingLoads = ComputeBlocksReachingLoads(blocksWithLoads);

		foreach (var block in func.Body.Blocks) {
			if (block.Predecessors.Count() < 2) continue;

			// Block needs argument if: multiple predecessors, variable is live, reachable from store
			if (blocksReachingLoads.Contains(block) && CanReachFromBlocks(block, blocksWithStores)) {
				mergeBlocks.Add(block);
			}
		}

		return mergeBlocks;
	}

	private static HashSet<MlirBlock> ComputeBlocksReachingLoads(HashSet<MlirBlock> blocksWithLoads) {
		var result = new HashSet<MlirBlock>(blocksWithLoads);
		var worklist = new Queue<MlirBlock>(blocksWithLoads);

		while (worklist.Count > 0) {
			var block = worklist.Dequeue();
			foreach (var pred in block.Predecessors) {
				if (result.Add(pred)) {
					worklist.Enqueue(pred);
				}
			}
		}

		return result;
	}

	private static bool HasBackEdges(HashSet<MlirBlock> mergeBlocks) {
		foreach (var block in mergeBlocks) {
			if (CanReachBlock(block, block)) return true;
		}
		return false;
	}

	private static bool CanReachBlock(MlirBlock source, MlirBlock target) {
		var visited = new HashSet<MlirBlock>();
		var queue = new Queue<MlirBlock>(source.Successors);

		while (queue.Count > 0) {
			var current = queue.Dequeue();
			if (current == target) return true;
			if (!visited.Add(current)) continue;

			foreach (var succ in current.Successors) {
				queue.Enqueue(succ);
			}
		}

		return false;
	}

	private static bool CanReachFromBlocks(MlirBlock target, HashSet<MlirBlock> sourceBlocks) {
		var visited = new HashSet<MlirBlock>();
		var queue = new Queue<MlirBlock>(sourceBlocks);

		while (queue.Count > 0) {
			var current = queue.Dequeue();
			if (current == target) return true;
			if (!visited.Add(current)) continue;

			foreach (var succ in current.Successors) {
				queue.Enqueue(succ);
			}
		}

		return false;
	}

	private static Dictionary<MlirBlock, MlirValue> ComputeReachingDefinitions(MlirFunction func,
		AllocaOp alloca, Dictionary<MlirBlock, MlirValue> blockArguments) {

		var reachingDefs = new Dictionary<MlirBlock, MlirValue>(blockArguments);

		// Find initial value (first store in entry block)
		var entryBlock = func.Body.Blocks[0];
		foreach (var op in entryBlock.Operations) {
			if (op is StoreOp store && IsAllocaAccess(store.MemRef, store.Indices, alloca)) {
				reachingDefs[entryBlock] = store.Value;
				break;
			}
		}

		return reachingDefs;
	}

	private static void UpdateBranchOperands(MlirFunction func, AllocaOp alloca,
		HashSet<MlirBlock> mergeBlocks, Dictionary<MlirBlock, MlirValue> reachingDefs) {

		foreach (var block in func.Body.Blocks) {
			var terminator = block.Terminator;
			if (terminator == null) continue;

			var exitValue = GetBlockExitValue(block, alloca, reachingDefs);
			if (exitValue == null) continue;

			if (terminator is BranchOp br && mergeBlocks.Contains(br.Destination)) {
				var newBr = new BranchOp(br.Destination, [.. br.BlockArguments, exitValue]);
				ReplaceBranchOp(block, br, newBr);
			} else if (terminator is CondBranchOp condBr) {
				var newTrueArgs = condBr.TrueArguments.ToList();
				var newFalseArgs = condBr.FalseArguments.ToList();
				bool needsUpdate = false;

				if (mergeBlocks.Contains(condBr.TrueBlock)) {
					newTrueArgs.Add(exitValue);
					needsUpdate = true;
				}
				if (mergeBlocks.Contains(condBr.FalseBlock)) {
					newFalseArgs.Add(exitValue);
					needsUpdate = true;
				}

				if (needsUpdate) {
					var newCondBr = new CondBranchOp(condBr.Condition, condBr.TrueBlock, condBr.FalseBlock, newTrueArgs, newFalseArgs);
					ReplaceBranchOp(block, condBr, newCondBr);
				}
			}
		}
	}

	private static MlirValue? GetBlockExitValue(MlirBlock block, AllocaOp alloca,
		Dictionary<MlirBlock, MlirValue> reachingDefs) {

		MlirValue? value = reachingDefs.GetValueOrDefault(block);

		foreach (var op in block.Operations) {
			if (op is StoreOp store && IsAllocaAccess(store.MemRef, store.Indices, alloca)) {
				value = store.Value;
			}
		}

		if (value == null) {
			var preds = block.Predecessors.ToList();
			if (preds.Count == 1) {
				value = GetBlockExitValue(preds[0], alloca, reachingDefs);
			}
		}

		return value;
	}

	private static MlirValue? GetReachingValueAtLoad(LoadOp load, MlirBlock loadBlock,
		AllocaOp alloca, Dictionary<MlirBlock, MlirValue> reachingDefs) {

		var lastStore = FindLastStoreBefore(loadBlock, load, alloca);
		if (lastStore != null) return lastStore;

		if (reachingDefs.TryGetValue(loadBlock, out var reachingValue)) {
			return reachingValue;
		}

		var preds = loadBlock.Predecessors.ToList();
		if (preds.Count == 1) {
			return GetBlockExitValue(preds[0], alloca, reachingDefs);
		}

		return null;
	}

	private static void ReplaceBranchOp(MlirBlock block, MlirOperation oldOp, MlirOperation newOp) {
		var idx = block.Operations.IndexOf(oldOp);
		if (idx >= 0) {
			block.Operations[idx] = newOp;
			newOp.ParentBlock = block;
			oldOp.ParentBlock = null;
		}
	}

	// ============================================================================
	// Load Resolution Validation
	// ============================================================================

	private static bool CanResolveLoad(LoadOp load, MlirBlock loadBlock, AllocaOp alloca,
		HashSet<MlirBlock> mergeBlocks, MlirFunction func) {

		if (FindLastStoreBefore(loadBlock, load, alloca) != null) return true;
		if (mergeBlocks.Contains(loadBlock)) return true;

		return CanResolveFromPredecessors(loadBlock, alloca, mergeBlocks, [], func);
	}

	private static bool CanResolveFromPredecessors(MlirBlock block, AllocaOp alloca,
		HashSet<MlirBlock> mergeBlocks, HashSet<MlirBlock> visited, MlirFunction func) {

		if (!visited.Add(block)) return false;
		if (mergeBlocks.Contains(block)) return true;

		var preds = block.Predecessors.ToList();

		if (preds.Count == 1) {
			var pred = preds[0];
			if (BlockHasStoreToAlloca(pred, alloca)) return true;
			return CanResolveFromPredecessors(pred, alloca, mergeBlocks, visited, func);
		}

		if (preds.Count > 1) {
			foreach (var pred in preds) {
				if (BlockHasStoreToAlloca(pred, alloca)) continue;
				if (!CanResolveFromPredecessors(pred, alloca, mergeBlocks, visited, func)) {
					return false;
				}
			}
			return true;
		}

		// No predecessors (entry block)
		return BlockHasStoreToAlloca(func.Body.Blocks[0], alloca);
	}

	private static bool BlockHasStoreToAlloca(MlirBlock block, AllocaOp alloca) {
		return block.Operations.Any(op =>
			op is StoreOp store && IsAllocaAccess(store.MemRef, store.Indices, alloca));
	}
}
