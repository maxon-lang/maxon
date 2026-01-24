using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Dead code elimination pass - removes unused values and unreachable code.
/// </summary>
public sealed class DeadCodeEliminationPass : FunctionPass {
	public override string Name => "dead-code-elimination";
	public override string Description => "Removes unused values and unreachable blocks";

	protected override bool RunOnFunction(MlirFunction func) {
		bool anyChanged = false;
		int totalRemovedOps = 0;
		int totalRemovedBlocks = 0;

		Logger.Debug(LogCategory.Optimizer, $"dead-code-elimination: processing {func.Name}");

		// Iterate until no more changes (removing one dead op may make others dead)
		bool changed;
		do {
			changed = false;

			// Collect all used values
			var usedValues = new HashSet<MlirValue>();

			// First pass: mark all values that are used
			foreach (var block in func.Body.Blocks) {
				foreach (var op in block.Operations) {
					foreach (var operand in op.Operands) {
						usedValues.Add(operand);
					}

					// Check nested regions
					foreach (var region in op.Regions) {
						MarkUsedValuesInRegion(region, usedValues);
					}
				}
			}

			// Also mark function return values as used
			foreach (var block in func.Body.Blocks) {
				if (block.Terminator is { } term) {
					foreach (var operand in term.Operands) {
						usedValues.Add(operand);
					}
				}
			}

			// Second pass: remove operations with unused results
			foreach (var block in func.Body.Blocks) {
				var opsToRemove = new List<MlirOperation>();

				foreach (var op in block.Operations) {
					// Don't remove terminators or side-effectful ops
					if (op.IsTerminator || op.HasSideEffects) continue;

					// Check if all results are unused
					bool allResultsUnused = op.Results.Count > 0 && op.Results.All(r => !usedValues.Contains(r));

					if (allResultsUnused) {
						opsToRemove.Add(op);
					}
				}

				foreach (var op in opsToRemove) {
					Logger.Trace(LogCategory.Optimizer, $"  removing dead op: {op.Mnemonic}");
					block.Operations.Remove(op);
					totalRemovedOps++;
					changed = true;
				}
			}

			// Third pass: remove unreachable blocks
			var unreachableRemoved = RemoveUnreachableBlocks(func, out int removedBlocks);
			changed |= unreachableRemoved;
			totalRemovedBlocks += removedBlocks;

			anyChanged |= changed;
		} while (changed);

		if (totalRemovedOps > 0 || totalRemovedBlocks > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: removed {totalRemovedOps} dead ops, {totalRemovedBlocks} unreachable blocks");
		}

		return anyChanged;
	}

	private static void MarkUsedValuesInRegion(MlirRegion region, HashSet<MlirValue> usedValues) {
		foreach (var block in region.Blocks) {
			foreach (var op in block.Operations) {
				foreach (var operand in op.Operands) {
					usedValues.Add(operand);
				}

				foreach (var nestedRegion in op.Regions) {
					MarkUsedValuesInRegion(nestedRegion, usedValues);
				}
			}
		}
	}

	private static bool RemoveUnreachableBlocks(MlirFunction func, out int removedCount) {
		removedCount = 0;
		if (func.Body.Blocks.Count == 0) return false;

		var reachable = new HashSet<MlirBlock>();
		var worklist = new Queue<MlirBlock>();

		// Entry block is always reachable
		var entryBlock = func.Body.Blocks[0];
		reachable.Add(entryBlock);
		worklist.Enqueue(entryBlock);

		// BFS to find all reachable blocks
		while (worklist.Count > 0) {
			var block = worklist.Dequeue();

			if (block.Terminator is null) continue;

			foreach (var successor in block.Terminator.Successors) {
				if (reachable.Add(successor)) {
					worklist.Enqueue(successor);
				}
			}
		}

		// Remove unreachable blocks
		var unreachable = func.Body.Blocks.Where(b => !reachable.Contains(b)).ToList();
		foreach (var block in unreachable) {
			Logger.Trace(LogCategory.Optimizer, $"  removing unreachable block: {block.Name}");
			func.Body.Blocks.Remove(block);
		}

		removedCount = unreachable.Count;
		return unreachable.Count > 0;
	}
}
