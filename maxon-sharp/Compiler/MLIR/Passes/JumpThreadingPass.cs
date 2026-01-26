using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Jump threading pass - optimizes control flow by threading jumps through blocks
/// when branch conditions are known or can be determined from the control flow path.
///
/// This pass applies several patterns:
/// 1. Constant condition branches - Replace conditional branches with unconditional when condition is constant
/// 2. Redundant condition checks - Thread through blocks that re-check a condition we already know
/// 3. Empty block elimination - Skip blocks that only contain an unconditional branch
/// 4. Trivial phi elimination - Replace block arguments with incoming values when single predecessor
/// </summary>
public sealed class JumpThreadingPass : FunctionPass {
	public override string Name => "jump-threading";
	public override string Description => "Threads jumps through blocks with known conditions";

	private int _constantBranchCount;
	private int _redundantCheckCount;
	private int _emptyBlockCount;
	private int _trivialPhiCount;

	protected override bool RunOnFunction(MlirFunction func) {
		bool anyChanged = false;
		bool changed;
		int iterations = 0;

		_constantBranchCount = 0;

		// Trace: print CFG before optimization
		if (Logger.GetLevel(LogCategory.Optimizer) <= LogLevel.Trace) {
			Logger.Trace(LogCategory.Optimizer, $"  {func.Name} CFG before jump-threading:");
			foreach (var block in func.Body.Blocks) {
				var term = block.Terminator;
				if (term is BranchOp br) {
					string args = br.BlockArguments.Count > 0 ? $"({string.Join(", ", br.BlockArguments.Select(a => $"%{a.Id}"))})" : "";
					Logger.Trace(LogCategory.Optimizer, $"    ^{block.Name} -> ^{br.Destination.Name}{args}");
				} else if (term is CondBranchOp condBr) {
					string trueArgs = condBr.TrueArguments.Count > 0 ? $"({string.Join(", ", condBr.TrueArguments.Select(a => $"%{a.Id}"))})" : "";
					string falseArgs = condBr.FalseArguments.Count > 0 ? $"({string.Join(", ", condBr.FalseArguments.Select(a => $"%{a.Id}"))})" : "";
					Logger.Trace(LogCategory.Optimizer, $"    ^{block.Name} -> true:^{condBr.TrueBlock.Name}{trueArgs}, false:^{condBr.FalseBlock.Name}{falseArgs}");
				} else if (term != null) {
					Logger.Trace(LogCategory.Optimizer, $"    ^{block.Name} -> {term.Mnemonic}");
				}
			}
		}
		_redundantCheckCount = 0;
		_emptyBlockCount = 0;
		_trivialPhiCount = 0;

		Logger.Debug(LogCategory.Optimizer, $"jump-threading: processing {func.Name}");

		// Iterate until fixed-point (with safety limit to prevent infinite loops)
		const int MaxIterations = 100;
		do {
			changed = false;
			iterations++;

			// Pattern 1: Constant condition branches
			changed |= EliminateConstantBranches(func);

			// Pattern 2: Redundant condition checks
			changed |= ThreadRedundantConditionChecks(func);

			// Pattern 3: Empty block elimination
			changed |= EliminateEmptyBlocks(func);

			// Pattern 4: Trivial phi elimination (single predecessor)
			changed |= EliminateTrivialPhis(func);

			anyChanged |= changed;

			if (iterations >= MaxIterations) {
				Logger.Error(LogCategory.Optimizer, $"  {func.Name}: jump-threading hit iteration limit ({MaxIterations}), possible infinite loop");
				break;
			}
		} while (changed);

		if (_constantBranchCount > 0 || _redundantCheckCount > 0 || _emptyBlockCount > 0 || _trivialPhiCount > 0) {
			Logger.Debug(LogCategory.Optimizer,
				$"  {func.Name}: {_constantBranchCount} constant branches, {_redundantCheckCount} redundant checks, " +
				$"{_emptyBlockCount} empty blocks, {_trivialPhiCount} trivial phis eliminated in {iterations} iteration(s)");
		}

		// Trace: print CFG after optimization
		if (Logger.GetLevel(LogCategory.Optimizer) <= LogLevel.Trace) {
			Logger.Trace(LogCategory.Optimizer, $"  {func.Name} CFG after jump-threading:");
			foreach (var block in func.Body.Blocks) {
				var term = block.Terminator;
				if (term is BranchOp br) {
					string args = br.BlockArguments.Count > 0 ? $"({string.Join(", ", br.BlockArguments.Select(a => $"%{a.Id}"))})" : "";
					Logger.Trace(LogCategory.Optimizer, $"    ^{block.Name} -> ^{br.Destination.Name}{args}");
				} else if (term is CondBranchOp condBr) {
					string trueArgs = condBr.TrueArguments.Count > 0 ? $"({string.Join(", ", condBr.TrueArguments.Select(a => $"%{a.Id}"))})" : "";
					string falseArgs = condBr.FalseArguments.Count > 0 ? $"({string.Join(", ", condBr.FalseArguments.Select(a => $"%{a.Id}"))})" : "";
					Logger.Trace(LogCategory.Optimizer, $"    ^{block.Name} -> true:^{condBr.TrueBlock.Name}{trueArgs}, false:^{condBr.FalseBlock.Name}{falseArgs}");
				} else if (term != null) {
					Logger.Trace(LogCategory.Optimizer, $"    ^{block.Name} -> {term.Mnemonic}");
				}
			}
		}

		return anyChanged;
	}

	// ============================================================================
	// Pattern 1: Constant Condition Branches
	// ============================================================================

	/// <summary>
	/// Finds conditional branches where the condition is a constant and replaces
	/// them with unconditional branches to the appropriate target.
	/// </summary>
	private bool EliminateConstantBranches(MlirFunction func) {
		bool changed = false;

		foreach (var block in func.Body.Blocks) {
			if (block.Terminator is not CondBranchOp condBr) continue;

			var condValue = GetConstantBoolValue(condBr.Condition);
			if (condValue is null) continue;

			// Determine target block and arguments based on constant condition
			MlirBlock targetBlock;
			IReadOnlyList<MlirValue> targetArgs;

			if (condValue.Value) {
				targetBlock = condBr.TrueBlock;
				targetArgs = condBr.TrueArguments;
				Logger.Trace(LogCategory.Optimizer, $"  constant branch elimination: ^{block.Name} cond_br (true) -> br ^{targetBlock.Name}");
			} else {
				targetBlock = condBr.FalseBlock;
				targetArgs = condBr.FalseArguments;
				Logger.Trace(LogCategory.Optimizer, $"  constant branch elimination: ^{block.Name} cond_br (false) -> br ^{targetBlock.Name}");
			}

			// Replace with unconditional branch
			var branchOp = new BranchOp(targetBlock, [.. targetArgs]);
			ReplaceBranchOp(block, condBr, branchOp);

			_constantBranchCount++;
			changed = true;
		}

		return changed;
	}

	// ============================================================================
	// Pattern 2: Redundant Condition Checks
	// ============================================================================

	/// <summary>
	/// If a block ends with CondBranchOp on condition X, and the true target also
	/// checks condition X, we know X is true on that path, so we can thread through.
	/// </summary>
	private bool ThreadRedundantConditionChecks(MlirFunction func) {
		bool changed = false;

		foreach (var block in func.Body.Blocks) {
			if (block.Terminator is not CondBranchOp condBr) continue;

			// Check if true block re-checks the same condition
			changed |= TryThreadThroughBlock(block, condBr, condBr.TrueBlock, isTrue: true);

			// Check if false block re-checks the same condition
			changed |= TryThreadThroughBlock(block, condBr, condBr.FalseBlock, isTrue: false);
		}

		return changed;
	}

	/// <summary>
	/// Attempts to thread a branch through a target block when the target re-checks
	/// the same condition that was just tested.
	/// </summary>
	private bool TryThreadThroughBlock(MlirBlock sourceBlock, CondBranchOp sourceBranch,
		MlirBlock targetBlock, bool isTrue) {

		if (targetBlock.Terminator is not CondBranchOp targetCondBr) return false;

		// Check if the target block is testing the same condition
		if (!IsSameCondition(sourceBranch.Condition, targetCondBr.Condition, targetBlock)) return false;

		// The target block re-checks the same condition
		// On the true path, we know condition is true, so target will take its true branch
		// On the false path, we know condition is false, so target will take its false branch

		MlirBlock threadTarget;
		IReadOnlyList<MlirValue> sourceArgs;
		IReadOnlyList<MlirValue> threadArgs;

		if (isTrue) {
			// Coming from true path: condition is true, so thread to target's true block
			threadTarget = targetCondBr.TrueBlock;
			sourceArgs = sourceBranch.TrueArguments;
			threadArgs = MapBlockArguments(targetBlock, sourceArgs, targetCondBr.TrueArguments);
			Logger.Trace(LogCategory.Optimizer,
				$"  redundant check: ^{sourceBlock.Name} -> ^{targetBlock.Name} (cond=true) -> ^{threadTarget.Name}");
		} else {
			// Coming from false path: condition is false, so thread to target's false block
			threadTarget = targetCondBr.FalseBlock;
			sourceArgs = sourceBranch.FalseArguments;
			threadArgs = MapBlockArguments(targetBlock, sourceArgs, targetCondBr.FalseArguments);
			Logger.Trace(LogCategory.Optimizer,
				$"  redundant check: ^{sourceBlock.Name} -> ^{targetBlock.Name} (cond=false) -> ^{threadTarget.Name}");
		}

		// Only thread if the target block has no other operations besides the terminator
		// and the block arguments can be properly forwarded
		if (targetBlock.Operations.Count > 1) {
			// Target block has operations before the terminator - can't simply thread
			// We would need to duplicate those operations, which is more complex
			return false;
		}

		// Update the source branch to go directly to the threaded target
		CondBranchOp newBranch;
		if (isTrue) {
			newBranch = new CondBranchOp(
				sourceBranch.Condition,
				threadTarget,  // New true target
				sourceBranch.FalseBlock,
				threadArgs,
				sourceBranch.FalseArguments);
		} else {
			newBranch = new CondBranchOp(
				sourceBranch.Condition,
				sourceBranch.TrueBlock,
				threadTarget,  // New false target
				sourceBranch.TrueArguments,
				threadArgs);
		}

		ReplaceBranchOp(sourceBlock, sourceBranch, newBranch);
		_redundantCheckCount++;
		return true;
	}

	/// <summary>
	/// Checks if two conditions are effectively the same value.
	/// </summary>
	private static bool IsSameCondition(MlirValue cond1, MlirValue cond2, MlirBlock targetBlock) {
		// Direct value equality
		if (cond1 == cond2) return true;

		// If cond2 is a block argument of targetBlock, check if it receives cond1
		if (cond2.DefiningBlockArg is { } blockArg && blockArg.Block == targetBlock) {
			// This would require checking what values flow into this block argument
			// For now, only handle the simple case
			return false;
		}

		// If both are produced by the same comparison operation type with same operands
		if (cond1.DefiningOp is CmpIOp cmp1 && cond2.DefiningOp is CmpIOp cmp2) {
			return cmp1.Predicate == cmp2.Predicate &&
					 cmp1.Lhs == cmp2.Lhs &&
					 cmp1.Rhs == cmp2.Rhs;
		}

		return false;
	}

	/// <summary>
	/// Maps block arguments through a block that we're threading through.
	/// </summary>
	private static List<MlirValue> MapBlockArguments(MlirBlock throughBlock,
		IReadOnlyList<MlirValue> incomingArgs, IReadOnlyList<MlirValue> outgoingArgs) {

		var result = new List<MlirValue>();

		foreach (var outArg in outgoingArgs) {
			// Check if this outgoing arg is a block argument of throughBlock
			if (outArg.DefiningBlockArg is { } blockArg && blockArg.Block == throughBlock) {
				// Map it to the corresponding incoming argument
				if (blockArg.Index < incomingArgs.Count) {
					result.Add(incomingArgs[blockArg.Index]);
				} else {
					result.Add(outArg);
				}
			} else {
				// Not a block argument, keep as-is
				result.Add(outArg);
			}
		}

		return result;
	}

	// ============================================================================
	// Pattern 3: Empty Block Elimination
	// ============================================================================

	/// <summary>
	/// If a block only contains an unconditional branch (no other operations),
	/// redirect all predecessors to skip the empty block.
	/// </summary>
	private bool EliminateEmptyBlocks(MlirFunction func) {
		bool changed = false;

		// Find empty blocks (only contain an unconditional branch)
		var emptyBlocks = func.Body.Blocks
			.Where(IsEmptyBlock)
			.Where(b => b != func.Body.EntryBlock) // Don't eliminate entry block
			.ToList();

		foreach (var emptyBlock in emptyBlocks) {
			var br = (BranchOp)emptyBlock.Terminator!;
			var target = br.Destination;
			var blockArgs = br.BlockArguments;

			// Don't eliminate if the block branches to itself (infinite loop)
			if (target == emptyBlock) continue;

			// Redirect all predecessors
			var predecessors = emptyBlock.Predecessors.ToList();
			if (predecessors.Count == 0) continue; // Unreachable block, will be cleaned by DCE

			int redirectedCount = 0;
			foreach (var pred in predecessors) {
				if (RedirectBranchThroughEmptyBlock(pred, emptyBlock, target, blockArgs)) {
					redirectedCount++;
				}
			}

			// Only consider this block eliminated if at least one predecessor was redirected
			if (redirectedCount == 0) continue;

			// Check if all predecessors have been redirected by recalculating predecessors
			// (the Predecessors property dynamically computes based on current branch targets)
			var remainingPreds = emptyBlock.Predecessors.ToList();

			if (remainingPreds.Count == 0) {
				// Safe to remove the block - no remaining references
				func.Body.Blocks.Remove(emptyBlock);
				Logger.Debug(LogCategory.Optimizer,
					$"  empty block removed: ^{emptyBlock.Name} -> ^{target.Name} ({redirectedCount} predecessors redirected)");
			} else {
				// Some predecessors could not be redirected - keep the block
				Logger.Trace(LogCategory.Optimizer,
					$"  empty block elimination: ^{emptyBlock.Name} -> ^{target.Name} ({redirectedCount}/{predecessors.Count} predecessors redirected, {remainingPreds.Count} remaining)");
			}

			_emptyBlockCount++;
			changed = true;
		}

		return changed;
	}

	/// <summary>
	/// Checks if a block is "empty" (only contains an unconditional branch).
	/// A block with arguments is NOT empty because it carries values that need
	/// to be properly threaded through - removing it would lose SSA phi semantics.
	/// </summary>
	private static bool IsEmptyBlock(MlirBlock block) {
		// Block must only have an unconditional branch
		if (block.Operations.Count != 1 || block.Terminator is not BranchOp)
			return false;

		// Block with arguments is not "empty" - it has phi semantics
		// The arguments receive values from different predecessors
		if (block.Arguments.Count > 0)
			return false;

		return true;
	}

	/// <summary>
	/// Redirects a branch from pred through emptyBlock to target.
	/// </summary>
	private static bool RedirectBranchThroughEmptyBlock(MlirBlock pred, MlirBlock emptyBlock,
		MlirBlock target, IReadOnlyList<MlirValue> emptyBlockArgs) {

		var terminator = pred.Terminator;
		if (terminator is null) return false;

		if (terminator is BranchOp br && br.Destination == emptyBlock) {
			// Map the arguments: emptyBlock's arguments become the values passed to it,
			// and we use those to substitute into the target arguments
			var newArgs = MapArgumentsThroughEmptyBlock(emptyBlock, br.BlockArguments, emptyBlockArgs);
			var newBranch = new BranchOp(target, [.. newArgs]);
			ReplaceBranchOp(pred, br, newBranch);
			return true;
		}

		if (terminator is CondBranchOp condBr) {
			bool needsUpdate = false;
			var newTrueBlock = condBr.TrueBlock;
			var newTrueArgs = condBr.TrueArguments.ToList();
			var newFalseBlock = condBr.FalseBlock;
			var newFalseArgs = condBr.FalseArguments.ToList();

			if (condBr.TrueBlock == emptyBlock) {
				newTrueBlock = target;
				newTrueArgs = [.. MapArgumentsThroughEmptyBlock(emptyBlock, condBr.TrueArguments, emptyBlockArgs)];
				needsUpdate = true;
			}

			if (condBr.FalseBlock == emptyBlock) {
				newFalseBlock = target;
				newFalseArgs = [.. MapArgumentsThroughEmptyBlock(emptyBlock, condBr.FalseArguments, emptyBlockArgs)];
				needsUpdate = true;
			}

			if (needsUpdate) {
				var newCondBr = new CondBranchOp(condBr.Condition, newTrueBlock, newFalseBlock, newTrueArgs, newFalseArgs);
				ReplaceBranchOp(pred, condBr, newCondBr);
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Maps arguments through an empty block by substituting block arguments
	/// with incoming values.
	/// </summary>
	private static List<MlirValue> MapArgumentsThroughEmptyBlock(MlirBlock emptyBlock,
		IReadOnlyList<MlirValue> incomingArgs, IReadOnlyList<MlirValue> outgoingArgs) {

		var result = new List<MlirValue>();

		foreach (var outArg in outgoingArgs) {
			// Check if this is a block argument of the empty block
			if (outArg.DefiningBlockArg is { } blockArg && blockArg.Block == emptyBlock) {
				// Substitute with the incoming argument
				if (blockArg.Index < incomingArgs.Count) {
					result.Add(incomingArgs[blockArg.Index]);
				} else {
					// Index out of range, keep original (shouldn't happen in valid IR)
					result.Add(outArg);
				}
			} else {
				// Not a block argument, keep as-is
				result.Add(outArg);
			}
		}

		return result;
	}

	// ============================================================================
	// Pattern 4: Trivial Phi Elimination
	// ============================================================================

	/// <summary>
	/// If a block has a single predecessor, its block arguments can be replaced
	/// with the incoming values directly (trivial phi elimination).
	/// </summary>
	private bool EliminateTrivialPhis(MlirFunction func) {
		bool changed = false;

		foreach (var block in func.Body.Blocks) {
			// Skip entry block and blocks with no arguments
			if (block == func.Body.EntryBlock) continue;
			if (block.Arguments.Count == 0) continue;

			// Check for single predecessor
			var predecessors = block.Predecessors.ToList();
			if (predecessors.Count != 1) continue;

			var pred = predecessors[0];
			var terminator = pred.Terminator;
			if (terminator is null) continue;

			// Get the arguments passed from the predecessor
			IReadOnlyList<MlirValue>? incomingArgs = terminator switch {
				BranchOp brOp when brOp.Destination == block => brOp.BlockArguments,
				CondBranchOp condBrOp when condBrOp.TrueBlock == block => condBrOp.TrueArguments,
				CondBranchOp condBrOp when condBrOp.FalseBlock == block => condBrOp.FalseArguments,
				_ => null
			};

			if (incomingArgs is null || incomingArgs.Count != block.Arguments.Count) continue;

			// Replace uses of block arguments with incoming values
			for (int i = 0; i < block.Arguments.Count; i++) {
				var blockArgValue = block.Arguments[i].Value;
				var incomingValue = incomingArgs[i];

				if (blockArgValue != incomingValue) {
					ReplaceAllUses(blockArgValue, incomingValue, func);
					Logger.Trace(LogCategory.Optimizer,
						$"  trivial phi: ^{block.Name}[{i}] (%{blockArgValue.Id}) -> %{incomingValue.Id}");
				}
			}

			// Remove block arguments
			block.Arguments.Clear();

			// Update the terminator to not pass arguments
			if (terminator is BranchOp br) {
				var newBranch = new BranchOp(block);
				ReplaceBranchOp(pred, br, newBranch);
			} else if (terminator is CondBranchOp condBr) {
				IReadOnlyList<MlirValue> newTrueArgs = condBr.TrueBlock == block ? [] : condBr.TrueArguments;
				IReadOnlyList<MlirValue> newFalseArgs = condBr.FalseBlock == block ? [] : condBr.FalseArguments;
				var newCondBr = new CondBranchOp(condBr.Condition, condBr.TrueBlock, condBr.FalseBlock,
					newTrueArgs, newFalseArgs);
				ReplaceBranchOp(pred, condBr, newCondBr);
			}

			_trivialPhiCount++;
			changed = true;
		}

		return changed;
	}

	// ============================================================================
	// Utilities
	// ============================================================================

	/// <summary>
	/// Gets the constant boolean value from a value if it's a constant.
	/// </summary>
	private static bool? GetConstantBoolValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is IntegerAttr intAttr) {
			return intAttr.Value != 0;
		}
		return null;
	}

	/// <summary>
	/// Replaces a branch operation in a block.
	/// </summary>
	private static void ReplaceBranchOp(MlirBlock block, MlirOperation oldOp, MlirOperation newOp) {
		var idx = block.Operations.IndexOf(oldOp);
		if (idx >= 0) {
			block.Operations[idx] = newOp;
			newOp.ParentBlock = block;
			oldOp.ParentBlock = null;
		}
	}

	/// <summary>
	/// Replaces all uses of a value with another value throughout the function.
	/// </summary>
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
}
