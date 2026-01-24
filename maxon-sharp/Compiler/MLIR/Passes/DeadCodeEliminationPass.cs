using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Dead code elimination pass - removes unused values and unreachable code.
/// </summary>
public sealed class DeadCodeEliminationPass : FunctionPass {
	public override string Name => "dead-code-elimination";
	public override string Description => "Removes unused values and unreachable blocks";

	protected override bool RunOnFunction(MlirFunction func) {
		bool changed = false;

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
				block.Operations.Remove(op);
				changed = true;
			}
		}

		// Third pass: remove unreachable blocks
		changed |= RemoveUnreachableBlocks(func);

		return changed;
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

	private static bool RemoveUnreachableBlocks(MlirFunction func) {
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
			func.Body.Blocks.Remove(block);
		}

		return unreachable.Count > 0;
	}
}
