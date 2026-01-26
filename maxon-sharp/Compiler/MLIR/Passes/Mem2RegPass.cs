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

	// Limit cross-block promotions to avoid register pressure
	private const int MaxCrossBlockPromotions = 2;

	// ============================================================================
	// Types
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
	/// Information about an alloca that can be promoted within a loop.
	/// </summary>
	private sealed class LoopAllocaInfo {
		public required AllocaOp Alloca { get; init; }
		public required MlirValue InitialValue { get; init; }
		public required LoopInfo Loop { get; init; }
		public required bool EscapesLoop { get; init; }
	}

	// ============================================================================
	// Entry Point
	// ============================================================================

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
	// Alloca Use Analysis
	// ============================================================================

	private static AllocaUses CollectAllocaUses(AllocaOp alloca, MlirFunction func) {
		var uses = new AllocaUses();

		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				if (op is LoadOp load && IsSimpleAllocaAccess(load.MemRef, load.Indices, alloca)) {
					uses.Loads.Add((load, block));
				} else if (op is StoreOp store && IsSimpleAllocaAccess(store.MemRef, store.Indices, alloca)) {
					uses.Stores.Add((store, block));
				} else if (op.Operands.Contains(alloca.Result) && op != alloca) {
					uses.OtherUses.Add(op);
				}
			}
		}

		return uses;
	}

	private static bool IsSimpleAllocaAccess(MlirValue memRef, IReadOnlyList<MlirValue> indices, AllocaOp alloca) {
		return memRef == alloca.Result && indices.Count == 0;
	}

	private static bool CanPromoteType(AllocaUses uses) {
		foreach (var (store, _) in uses.Stores) {
			if (store.Value.Type is MemRefType or PtrType) return false;
		}
		return true;
	}

	// ============================================================================
	// Promotion Entry Point
	// ============================================================================

	private static bool TryPromote(AllocaOp alloca, MlirBlock allocaBlock, MlirFunction func,
		bool skipCrossBlock, out bool isCrossBlock) {

		isCrossBlock = false;
		var uses = CollectAllocaUses(alloca, func);

		// Reject if address escapes or uninitialized
		if (uses.OtherUses.Count > 0) return false;
		if (uses.Stores.Count == 0 && uses.Loads.Count > 0) return false;
		if (!CanPromoteType(uses)) return false;

		// Single-block: all uses in the alloca's block
		if (uses.AllUsedBlocks.All(b => b == allocaBlock)) {
			return TryPromoteSingleBlock(uses, allocaBlock, alloca, func);
		}

		// Cross-block
		if (skipCrossBlock) return false;
		isCrossBlock = true;
		return TryPromoteCrossBlock(uses, allocaBlock, alloca, func);
	}

	// ============================================================================
	// Single-Block Promotion
	// ============================================================================

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

	// ============================================================================
	// Cross-Block Promotion
	// ============================================================================

	private static bool TryPromoteCrossBlock(AllocaUses uses, MlirBlock allocaBlock,
		AllocaOp alloca, MlirFunction func) {

		if (alloca.Result.Type is not MemRefType allocaType) return false;
		var elementType = allocaType.ElementType;

		// Try loop-aware promotion first
		var loops = LoopAnalysis.DetectLoops(func);
		var containingLoop = LoopAnalysis.FindContainingLoop(loops, uses.AllUsedBlocks);

		if (containingLoop != null) {
			var loopAlloca = AnalyzeLoopAlloca(alloca, containingLoop, uses, func);
			if (loopAlloca != null) {
				return TryPromoteLoopVariable(alloca, allocaBlock, uses, loopAlloca, func);
			}
		}

		// Fall back to non-loop cross-block promotion
		return TryPromoteNonLoopCrossBlock(uses, allocaBlock, alloca, elementType, func);
	}

	private static bool TryPromoteNonLoopCrossBlock(AllocaUses uses, MlirBlock allocaBlock,
		AllocaOp alloca, MlirType elementType, MlirFunction func) {

		var mergeBlocks = FindMergeBlocks(func, uses.BlocksWithStores, uses.BlocksWithLoads);

		// Reject loops (back-edges cause issues without loop-aware handling)
		if (HasBackEdges(mergeBlocks)) return false;

		// Verify all loads can be resolved
		foreach (var (load, loadBlock) in uses.Loads) {
			if (!CanResolveLoad(load, loadBlock, alloca, mergeBlocks, func)) {
				Logger.Debug(LogCategory.Optimizer, $"    cannot promote: no reaching definition for load in ^{loadBlock.Name}");
				return false;
			}
		}

		// Insert block arguments at merge points
		var blockArguments = new Dictionary<MlirBlock, MlirValue>();
		foreach (var mergeBlock in mergeBlocks) {
			var arg = mergeBlock.AddArgument(elementType);
			blockArguments[mergeBlock] = arg;
			Logger.Trace(LogCategory.Optimizer, $"    inserted block argument %{arg.Id} at ^{mergeBlock.Name}");
		}

		var reachingDefs = ComputeReachingDefinitions(func, alloca, blockArguments);
		UpdateBranchOperands(func, alloca, mergeBlocks, reachingDefs);

		// Build and apply load replacements
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

	// ============================================================================
	// Loop Alloca Analysis
	// ============================================================================

	private static LoopAllocaInfo? AnalyzeLoopAlloca(AllocaOp alloca, LoopInfo loop, AllocaUses uses, MlirFunction func) {
		bool hasLoopStore = uses.Stores.Any(s => loop.Body.Contains(s.Block));
		if (!hasLoopStore) return null;

		bool escapesLoop = uses.Loads.Any(l => !loop.Body.Contains(l.Block));
		var initialValue = FindInitialValue(alloca, loop, func);

		if (initialValue == null) {
			Logger.Trace(LogCategory.Optimizer, $"    cannot promote loop var: no initial value found");
			return null;
		}

		return new LoopAllocaInfo {
			Alloca = alloca,
			InitialValue = initialValue,
			Loop = loop,
			EscapesLoop = escapesLoop
		};
	}

	private static MlirValue? FindInitialValue(AllocaOp alloca, LoopInfo loop, MlirFunction func) {
		// Look in preheader first
		if (loop.Preheader != null) {
			foreach (var op in loop.Preheader.Operations) {
				if (op is StoreOp store && IsSimpleAllocaAccess(store.MemRef, store.Indices, alloca)) {
					return store.Value;
				}
			}
		}

		// Fall back to entry block
		var entryBlock = func.Body.Blocks[0];
		foreach (var op in entryBlock.Operations) {
			if (op is StoreOp store && IsSimpleAllocaAccess(store.MemRef, store.Indices, alloca)) {
				return store.Value;
			}
		}

		return null;
	}

	// ============================================================================
	// Loop Variable Promotion
	// ============================================================================

	private static bool TryPromoteLoopVariable(AllocaOp alloca, MlirBlock allocaBlock,
		AllocaUses uses, LoopAllocaInfo loopAlloca, MlirFunction func) {

		var loop = loopAlloca.Loop;
		if (alloca.Result.Type is not MemRefType allocaType) return false;
		var elementType = allocaType.ElementType;

		// Check for stores in nested inner loops (can't promote these yet)
		if (HasStoresInNestedLoop(uses, loop, func)) {
			return false;
		}

		Logger.Trace(LogCategory.Optimizer, $"    promoting loop variable %{alloca.Result.Id} (escapes={loopAlloca.EscapesLoop})");

		// Verify exit values can be computed before modifying IR
		if (!VerifyLoopExitValues(alloca, loop, elementType, func)) {
			return false;
		}

		// Insert block arguments
		var (headerArg, exitArg, blockArguments) = InsertLoopBlockArguments(loop, loopAlloca, elementType, alloca);

		// Update branch operands
		if (!UpdateLoopBranchOperands(func, alloca, loop, loopAlloca.InitialValue, headerArg, exitArg, blockArguments)) {
			Logger.Debug(LogCategory.Optimizer, $"    cannot promote: failed to update branch operands");
			return false;
		}

		// Build and apply load replacements
		var loadReplacements = BuildLoopLoadReplacements(uses, alloca, loop, headerArg, exitArg, blockArguments);
		if (loadReplacements == null) return false;

		ApplyPromotion(uses, loadReplacements, allocaBlock, alloca, func);
		return true;
	}

	private static bool HasStoresInNestedLoop(AllocaUses uses, LoopInfo loop, MlirFunction func) {
		var allLoops = LoopAnalysis.DetectLoops(func);

		foreach (var innerLoop in allLoops) {
			if (innerLoop.Header == loop.Header) continue;
			if (!loop.Body.Contains(innerLoop.Header)) continue;

			bool hasInnerStore = uses.Stores.Any(s => innerLoop.Body.Contains(s.Block));
			if (hasInnerStore) {
				bool flowsThroughInnerHeader = uses.Loads.Any(l =>
					innerLoop.Body.Contains(l.Block) && l.Block != innerLoop.Header);

				if (flowsThroughInnerHeader) {
					Logger.Trace(LogCategory.Optimizer, $"    cannot promote: variable has stores in nested inner loop ^{innerLoop.Header.Name}");
					return true;
				}
			}
		}

		return false;
	}

	private static bool VerifyLoopExitValues(AllocaOp alloca, LoopInfo loop, MlirType elementType, MlirFunction func) {
		var tempBlockArgs = new Dictionary<MlirBlock, MlirValue>();
		var placeholderArg = new MlirValue(elementType);
		tempBlockArgs[loop.Header] = placeholderArg;

		foreach (var block in func.Body.Blocks) {
			if (!loop.Body.Contains(block) && block != loop.Preheader) continue;

			var terminator = block.Terminator;
			if (terminator == null) continue;

			bool branchesToHeader = terminator switch {
				BranchOp br => br.Destination == loop.Header,
				CondBranchOp condBr => condBr.TrueBlock == loop.Header || condBr.FalseBlock == loop.Header,
				_ => false
			};

			if (branchesToHeader && loop.Body.Contains(block)) {
				var exitValue = GetLoopBlockExitValue(block, alloca, loop, placeholderArg, tempBlockArgs);
				if (exitValue == null) {
					Logger.Debug(LogCategory.Optimizer, $"    cannot promote: no exit value for branch ^{block.Name} -> ^{loop.Header.Name}");
					return false;
				}
			}
		}

		return true;
	}

	private static (MlirValue headerArg, MlirValue? exitArg, Dictionary<MlirBlock, MlirValue> blockArguments)
		InsertLoopBlockArguments(LoopInfo loop, LoopAllocaInfo loopAlloca, MlirType elementType, AllocaOp alloca) {

		var headerArg = loop.Header.AddArgument(elementType);
		Logger.Trace(LogCategory.Optimizer, $"    inserted header arg %{headerArg.Id} at ^{loop.Header.Name}");

		MlirValue? exitArg = null;
		if (loopAlloca.EscapesLoop && loop.Exit != null) {
			exitArg = loop.Exit.AddArgument(elementType);
			Logger.Trace(LogCategory.Optimizer, $"    inserted exit arg %{exitArg.Id} at ^{loop.Exit.Name}");
		}

		var blockArguments = new Dictionary<MlirBlock, MlirValue> { [loop.Header] = headerArg };
		if (exitArg != null && loop.Exit != null) {
			blockArguments[loop.Exit] = exitArg;
		}

		// Find and insert internal merge block arguments
		var internalMergeBlocks = FindInternalMergeBlocks(alloca, loop, headerArg, blockArguments);
		foreach (var mergeBlock in internalMergeBlocks) {
			var mergeArg = mergeBlock.AddArgument(elementType);
			blockArguments[mergeBlock] = mergeArg;
			Logger.Trace(LogCategory.Optimizer, $"    inserted merge arg %{mergeArg.Id} at ^{mergeBlock.Name}");
		}

		return (headerArg, exitArg, blockArguments);
	}

	private static HashSet<MlirBlock> FindInternalMergeBlocks(AllocaOp alloca, LoopInfo loop,
		MlirValue headerArg, Dictionary<MlirBlock, MlirValue> blockArguments) {

		var mergeBlocks = new HashSet<MlirBlock>();

		foreach (var block in loop.Body) {
			if (block == loop.Header) continue;

			var preds = block.Predecessors.Where(p => loop.Body.Contains(p)).ToList();
			if (preds.Count < 2) continue;

			var predValues = new HashSet<MlirValue>();
			foreach (var pred in preds) {
				var exitVal = GetLoopBlockExitValue(pred, alloca, loop, headerArg, blockArguments);
				if (exitVal != null) predValues.Add(exitVal);
			}

			if (predValues.Count > 1) {
				mergeBlocks.Add(block);
			}
		}

		return mergeBlocks;
	}

	private static Dictionary<LoadOp, MlirValue>? BuildLoopLoadReplacements(AllocaUses uses, AllocaOp alloca,
		LoopInfo loop, MlirValue headerArg, MlirValue? exitArg, Dictionary<MlirBlock, MlirValue> blockArguments) {

		var loadReplacements = new Dictionary<LoadOp, MlirValue>();

		foreach (var (load, loadBlock) in uses.Loads) {
			var reachingValue = GetLoopReachingValue(load, loadBlock, alloca, loop, headerArg, exitArg, blockArguments);
			if (reachingValue != null) {
				loadReplacements[load] = reachingValue;
			} else {
				Logger.Error(LogCategory.Optimizer, $"    failed to find reaching value for load in ^{loadBlock.Name}");
				return null;
			}
		}

		return loadReplacements;
	}

	// ============================================================================
	// Loop Branch Operand Updates
	// ============================================================================

	private static bool UpdateLoopBranchOperands(MlirFunction func, AllocaOp alloca,
		LoopInfo loop, MlirValue initialValue, MlirValue headerArg, MlirValue? exitArg,
		Dictionary<MlirBlock, MlirValue> blockArguments) {

		var internalMergeBlocks = blockArguments.Keys
			.Where(b => b != loop.Header && b != loop.Exit)
			.ToHashSet();

		foreach (var block in func.Body.Blocks) {
			var terminator = block.Terminator;
			if (terminator == null) continue;

			var exitValue = GetLoopBlockExitValue(block, alloca, loop, headerArg, blockArguments);

			if (terminator is BranchOp br) {
				if (!TryUpdateBranchOp(block, br, loop, initialValue, exitValue, exitArg, internalMergeBlocks)) {
					return false;
				}
			} else if (terminator is CondBranchOp condBr) {
				TryUpdateCondBranchOp(block, condBr, loop, initialValue, exitValue, exitArg, internalMergeBlocks);
			}
		}

		return true;
	}

	private static bool TryUpdateBranchOp(MlirBlock block, BranchOp br, LoopInfo loop,
		MlirValue initialValue, MlirValue? exitValue, MlirValue? exitArg, HashSet<MlirBlock> internalMergeBlocks) {

		var newArgs = br.BlockArguments.ToList();
		bool needsUpdate = false;

		if (br.Destination == loop.Header) {
			if (!loop.Body.Contains(block)) {
				newArgs.Add(initialValue);
				Logger.Trace(LogCategory.Optimizer, $"    branch ^{block.Name} -> ^{loop.Header.Name}: added initial value");
			} else {
				if (exitValue != null) {
					newArgs.Add(exitValue);
					Logger.Trace(LogCategory.Optimizer, $"    branch ^{block.Name} -> ^{loop.Header.Name}: added exit value %{exitValue.Id}");
				} else {
					Logger.Error(LogCategory.Optimizer, $"    branch ^{block.Name} -> ^{loop.Header.Name}: no exit value for loop variable");
					return false;
				}
			}
			needsUpdate = true;
		} else if (exitArg != null && br.Destination == loop.Exit && exitValue != null) {
			newArgs.Add(exitValue);
			needsUpdate = true;
		} else if (internalMergeBlocks.Contains(br.Destination) && exitValue != null) {
			newArgs.Add(exitValue);
			Logger.Trace(LogCategory.Optimizer, $"    branch ^{block.Name} -> ^{br.Destination.Name}: added merge value %{exitValue.Id}");
			needsUpdate = true;
		}

		if (needsUpdate) {
			ReplaceBranchOp(block, br, new BranchOp(br.Destination, [.. newArgs]));
		}

		return true;
	}

	private static void TryUpdateCondBranchOp(MlirBlock block, CondBranchOp condBr, LoopInfo loop,
		MlirValue initialValue, MlirValue? exitValue, MlirValue? exitArg, HashSet<MlirBlock> internalMergeBlocks) {

		var newTrueArgs = condBr.TrueArguments.ToList();
		var newFalseArgs = condBr.FalseArguments.ToList();
		bool needsUpdate = false;

		// True branch updates
		if (condBr.TrueBlock == loop.Header) {
			if (!loop.Body.Contains(block)) {
				newTrueArgs.Add(initialValue);
			} else if (exitValue != null) {
				newTrueArgs.Add(exitValue);
			}
			needsUpdate = true;
		}
		if (exitArg != null && condBr.TrueBlock == loop.Exit && exitValue != null) {
			newTrueArgs.Add(exitValue);
			needsUpdate = true;
		}
		if (internalMergeBlocks.Contains(condBr.TrueBlock) && exitValue != null) {
			newTrueArgs.Add(exitValue);
			Logger.Trace(LogCategory.Optimizer, $"    condbr ^{block.Name} true-> ^{condBr.TrueBlock.Name}: added merge value %{exitValue.Id}");
			needsUpdate = true;
		}

		// False branch updates
		if (condBr.FalseBlock == loop.Header) {
			if (!loop.Body.Contains(block)) {
				newFalseArgs.Add(initialValue);
			} else if (exitValue != null) {
				newFalseArgs.Add(exitValue);
			}
			needsUpdate = true;
		}
		if (exitArg != null && condBr.FalseBlock == loop.Exit && exitValue != null) {
			newFalseArgs.Add(exitValue);
			needsUpdate = true;
		}
		if (internalMergeBlocks.Contains(condBr.FalseBlock) && exitValue != null) {
			newFalseArgs.Add(exitValue);
			Logger.Trace(LogCategory.Optimizer, $"    condbr ^{block.Name} false-> ^{condBr.FalseBlock.Name}: added merge value %{exitValue.Id}");
			needsUpdate = true;
		}

		if (needsUpdate) {
			ReplaceBranchOp(block, condBr, new CondBranchOp(condBr.Condition, condBr.TrueBlock, condBr.FalseBlock, newTrueArgs, newFalseArgs));
		}
	}

	// ============================================================================
	// Loop Value Tracking
	// ============================================================================

	private static MlirValue? GetLoopBlockExitValue(MlirBlock block, AllocaOp alloca,
		LoopInfo loop, MlirValue headerArg, Dictionary<MlirBlock, MlirValue> blockArguments,
		HashSet<MlirBlock>? visited = null) {

		visited ??= [];
		if (!visited.Add(block)) return null;

		// Start with reaching definition at block entry
		MlirValue? value = null;

		if (block == loop.Header) {
			value = headerArg;
		} else if (blockArguments.TryGetValue(block, out var blockArg)) {
			value = blockArg;
		}

		// Scan for stores
		foreach (var op in block.Operations) {
			if (op is StoreOp store && IsSimpleAllocaAccess(store.MemRef, store.Indices, alloca)) {
				value = store.Value;
			}
		}

		// If no value, check predecessors
		if (value == null) {
			var preds = block.Predecessors.Where(p => loop.Body.Contains(p) || p == loop.Preheader).ToList();

			if (preds.Count == 1) {
				value = GetLoopBlockExitValue(preds[0], alloca, loop, headerArg, blockArguments, visited);
			} else if (preds.Count > 1) {
				foreach (var pred in preds) {
					var predValue = GetLoopBlockExitValue(pred, alloca, loop, headerArg, blockArguments, visited);
					if (predValue != null) {
						value = predValue;
						break;
					}
				}
			}
		}

		// Fallback for loop body blocks
		if (value == null && loop.Body.Contains(block)) {
			value = headerArg;
		}

		return value;
	}

	private static MlirValue? GetLoopReachingValue(LoadOp load, MlirBlock loadBlock,
		AllocaOp alloca, LoopInfo loop, MlirValue headerArg, MlirValue? exitArg,
		Dictionary<MlirBlock, MlirValue> blockArguments) {

		var lastStore = FindLastStoreBefore(loadBlock, load, alloca);
		if (lastStore != null) return lastStore;

		if (loadBlock == loop.Header) return headerArg;

		if (loop.Body.Contains(loadBlock)) {
			if (blockArguments.TryGetValue(loadBlock, out var blockArg)) {
				return blockArg;
			}

			var preds = loadBlock.Predecessors.Where(p => loop.Body.Contains(p)).ToList();
			if (preds.Count == 1) {
				return GetLoopBlockExitValue(preds[0], alloca, loop, headerArg, blockArguments);
			}

			return headerArg;
		}

		if (loadBlock == loop.Exit && exitArg != null) {
			return exitArg;
		}

		return null;
	}

	// ============================================================================
	// Non-Loop Cross-Block Analysis
	// ============================================================================

	private static HashSet<MlirBlock> FindMergeBlocks(MlirFunction func,
		HashSet<MlirBlock> blocksWithStores, HashSet<MlirBlock> blocksWithLoads) {

		var mergeBlocks = new HashSet<MlirBlock>();
		var blocksReachingLoads = ComputeBlocksReachingLoads(blocksWithLoads);

		foreach (var block in func.Body.Blocks) {
			if (block.Predecessors.Count() < 2) continue;

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

		var entryBlock = func.Body.Blocks[0];
		foreach (var op in entryBlock.Operations) {
			if (op is StoreOp store && IsSimpleAllocaAccess(store.MemRef, store.Indices, alloca)) {
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
				ReplaceBranchOp(block, br, new BranchOp(br.Destination, [.. br.BlockArguments, exitValue]));
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
					ReplaceBranchOp(block, condBr, new CondBranchOp(condBr.Condition, condBr.TrueBlock, condBr.FalseBlock, newTrueArgs, newFalseArgs));
				}
			}
		}
	}

	private static MlirValue? GetBlockExitValue(MlirBlock block, AllocaOp alloca,
		Dictionary<MlirBlock, MlirValue> reachingDefs) {

		MlirValue? value = reachingDefs.GetValueOrDefault(block);

		foreach (var op in block.Operations) {
			if (op is StoreOp store && IsSimpleAllocaAccess(store.MemRef, store.Indices, alloca)) {
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

		return BlockHasStoreToAlloca(func.Body.Blocks[0], alloca);
	}

	private static bool BlockHasStoreToAlloca(MlirBlock block, AllocaOp alloca) {
		return block.Operations.Any(op =>
			op is StoreOp store && IsSimpleAllocaAccess(store.MemRef, store.Indices, alloca));
	}

	// ============================================================================
	// Utilities
	// ============================================================================

	private static MlirValue? FindLastStoreBefore(MlirBlock block, LoadOp load, AllocaOp alloca) {
		var loadIdx = block.Operations.IndexOf(load);
		for (int i = loadIdx - 1; i >= 0; i--) {
			if (block.Operations[i] is StoreOp store && IsSimpleAllocaAccess(store.MemRef, store.Indices, alloca)) {
				return store.Value;
			}
		}
		return null;
	}

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

	private static void ReplaceBranchOp(MlirBlock block, MlirOperation oldOp, MlirOperation newOp) {
		var idx = block.Operations.IndexOf(oldOp);
		if (idx >= 0) {
			block.Operations[idx] = newOp;
			newOp.ParentBlock = block;
			oldOp.ParentBlock = null;
		}
	}
}
