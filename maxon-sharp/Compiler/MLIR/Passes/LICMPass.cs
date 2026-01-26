using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Loop Invariant Code Motion (LICM) pass.
/// Moves loop-invariant computations from loop bodies to loop preheaders,
/// reducing redundant computation when the loop executes multiple iterations.
///
/// An operation is loop-invariant if:
/// - It has no side effects
/// - All its operands are either constants or defined outside the loop body
/// - It is not a terminator
///
/// This pass requires that loops have a preheader block to hoist operations into.
/// </summary>
public sealed class LICMPass : FunctionPass {
	public override string Name => "licm";
	public override string Description => "Loop Invariant Code Motion";

	protected override bool RunOnFunction(MlirFunction func) {
		Logger.Debug(LogCategory.Optimizer, $"licm: processing {func.Name}");

		var loops = LoopAnalysis.DetectLoops(func);
		if (loops.Count == 0) {
			Logger.Trace(LogCategory.Optimizer, $"  {func.Name}: no loops detected");
			return false;
		}

		// Build nesting information
		var nestInfo = LoopAnalysis.BuildLoopNest(loops);

		// Process from innermost to outermost (standard LICM order)
		var orderedLoops = LoopAnalysis.GetLoopsInnerToOuter(loops, nestInfo);

		Logger.Trace(LogCategory.Optimizer, $"  {func.Name}: detected {loops.Count} loop(s), processing inner-to-outer");

		bool anyChanged = false;
		int totalHoisted = 0;

		foreach (var loop in orderedLoops) {
			int hoistedInLoop = ProcessLoop(loop, func, nestInfo);
			if (hoistedInLoop > 0) {
				anyChanged = true;
				totalHoisted += hoistedInLoop;
			}
		}

		if (totalHoisted > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: hoisted {totalHoisted} loop-invariant operation(s)");
		}

		return anyChanged;
	}

	// Limit how many values we hoist to avoid register pressure issues
	// The register allocator has ~10 GPRs available (excluding RAX/RDX for special uses)
	// We need to leave room for loop-local values, so limit hoisting
	private const int MaxHoistedValuesPerLoop = 4;

	// Maximum nesting depth for LICM - only hoist from outermost loops to avoid register pressure issues
	// Depth 0 = outermost loop, depth 1 = one level nested, etc.
	private const int MaxNestingDepth = 0;

	/// <summary>
	/// Processes a single loop, hoisting invariant operations to the preheader.
	/// </summary>
	private static int ProcessLoop(LoopInfo loop, MlirFunction func, Dictionary<LoopInfo, LoopNestInfo> nestInfo) {
		if (loop.Preheader == null) {
			Logger.Trace(LogCategory.Optimizer, $"    loop ^{loop.Header.Name}: no preheader, skipping");
			return 0;
		}

		var nestingDepth = nestInfo[loop].Depth;

		// Skip deeply nested loops - hoisting from inner loops to outer loop bodies
		// doesn't help much and increases register pressure across the outer loops
		if (nestingDepth > MaxNestingDepth) {
			Logger.Trace(LogCategory.Optimizer, $"    loop ^{loop.Header.Name}: too deeply nested (depth={nestingDepth}), skipping LICM");
			return 0;
		}

		Logger.Trace(LogCategory.Optimizer, $"    loop ^{loop.Header.Name}: analyzing for invariant operations (depth={nestingDepth})");

		// Collect all values defined outside the loop, excluding outer loop header block args
		var outsideDefinitions = CollectOutsideDefinitions(loop, func, nestInfo);

		// Count existing loop-live values (values defined outside but used inside)
		int existingPressure = CountLoopLiveValues(loop, outsideDefinitions);

		// Compute budget - don't hoist if we're already at high pressure
		int budget = MaxHoistedValuesPerLoop - existingPressure;
		if (budget <= 0) {
			Logger.Trace(LogCategory.Optimizer, $"    loop ^{loop.Header.Name}: register pressure too high ({existingPressure} loop-live values), skipping LICM");
			return 0;
		}

		// Find loop-invariant operations
		var outerHeaders = LoopAnalysis.GetOuterLoopHeaders(loop, nestInfo);
		var invariantOps = FindInvariantOperations(loop, outsideDefinitions, outerHeaders);

		if (invariantOps.Count == 0) {
			Logger.Trace(LogCategory.Optimizer, $"    loop ^{loop.Header.Name}: no invariant operations found");
			return 0;
		}

		// Sort operations to maintain dependencies between hoisted ops
		var sortedOps = TopologicalSort(invariantOps);

		// Only hoist up to the budget (prefer ops that produce results used inside the loop)
		var opsToHoist = sortedOps.Take(budget).ToList();
		if (opsToHoist.Count < sortedOps.Count) {
			Logger.Trace(LogCategory.Optimizer, $"    loop ^{loop.Header.Name}: limiting hoist to {opsToHoist.Count}/{sortedOps.Count} ops (budget={budget})");
		}

		// Hoist operations to preheader
		int hoisted = HoistOperations(opsToHoist, loop.Preheader);

		Logger.Trace(LogCategory.Optimizer, $"    loop ^{loop.Header.Name}: hoisted {hoisted} operation(s) to ^{loop.Preheader.Name}");

		return hoisted;
	}

	/// <summary>
	/// Counts values that are defined outside the loop but used inside it.
	/// These contribute to register pressure within the loop.
	/// </summary>
	private static int CountLoopLiveValues(LoopInfo loop, HashSet<MlirValue> outsideDefinitions) {
		var usedOutsideValues = new HashSet<MlirValue>();

		foreach (var block in loop.Body) {
			foreach (var op in block.Operations) {
				foreach (var operand in op.Operands) {
					if (outsideDefinitions.Contains(operand)) {
						usedOutsideValues.Add(operand);
					}
				}
			}
		}

		return usedOutsideValues.Count;
	}

	/// <summary>
	/// Collects all values that are truly loop-invariant for this loop.
	/// This EXCLUDES:
	/// - Values defined inside the loop body
	/// - Block arguments from this loop's header (they are loop-carried values)
	/// - Block arguments from outer loop headers (they change per outer iteration)
	/// </summary>
	private static HashSet<MlirValue> CollectOutsideDefinitions(
		LoopInfo loop,
		MlirFunction func,
		Dictionary<LoopInfo, LoopNestInfo> nestInfo) {

		var outsideValues = new HashSet<MlirValue>();
		var outerHeaders = LoopAnalysis.GetOuterLoopHeaders(loop, nestInfo);

		foreach (var block in func.Body.Blocks) {
			// Skip blocks inside this loop's body
			if (loop.Body.Contains(block)) continue;

			// For outer loop headers: include operation results but NOT block arguments
			// Block arguments in outer loop headers are loop-carried values that change per iteration
			if (outerHeaders.Contains(block)) {
				foreach (var op in block.Operations) {
					// Skip terminators
					if (op.IsTerminator) continue;
					foreach (var result in op.Results) {
						outsideValues.Add(result);
					}
				}
				// DO NOT add block arguments from outer loop headers
				Logger.Trace(LogCategory.Optimizer, $"      excluding block args from outer header ^{block.Name}");
				continue;
			}

			// For non-outer-header blocks outside the loop, everything is invariant
			foreach (var arg in block.Arguments) {
				outsideValues.Add(arg.Value);
			}
			foreach (var op in block.Operations) {
				foreach (var result in op.Results) {
					outsideValues.Add(result);
				}
			}
		}

		// NEVER include this loop's header arguments as invariant
		// They are loop-carried values that change each iteration
		Logger.Trace(LogCategory.Optimizer, $"      excluding header args from ^{loop.Header.Name} (loop-carried values)");

		return outsideValues;
	}

	/// <summary>
	/// Finds all operations in the loop that are loop-invariant.
	/// An operation is invariant if it has no side effects, is not a terminator,
	/// and all operands are defined outside the loop or by other invariant operations.
	/// </summary>
	private static List<MlirOperation> FindInvariantOperations(
		LoopInfo loop,
		HashSet<MlirValue> outsideDefinitions,
		HashSet<MlirBlock> outerHeaders) {

		var invariantOps = new List<MlirOperation>();
		var invariantValues = new HashSet<MlirValue>(outsideDefinitions);

		// Collect all memory locations that have stores in the loop body
		// Loads from these locations are NOT loop-invariant
		var storedMemRefs = CollectStoredMemRefs(loop);

		// Iterate until fixed point - as we find invariant ops, their results
		// become available for other ops to be considered invariant
		bool changed;
		do {
			changed = false;

			foreach (var block in loop.Body) {
				foreach (var op in block.Operations) {
					// Skip if already identified as invariant
					if (invariantOps.Contains(op)) continue;

					if (IsLoopInvariant(op, invariantValues, loop, outerHeaders, storedMemRefs)) {
						invariantOps.Add(op);

						// Mark results as invariant so dependent ops can be hoisted too
						foreach (var result in op.Results) {
							invariantValues.Add(result);
						}

						Logger.Trace(LogCategory.Optimizer, $"      found invariant: {op.Mnemonic} %{(op.Results.Count > 0 ? op.Results[0].Id.ToString() : "void")}");
						changed = true;
					}
				}
			}
		} while (changed);

		return invariantOps;
	}

	/// <summary>
	/// Collects all memory references that have stores in the loop body.
	/// Loads from these memory locations are NOT loop-invariant.
	/// </summary>
	private static HashSet<MlirValue> CollectStoredMemRefs(LoopInfo loop) {
		var storedMemRefs = new HashSet<MlirValue>();

		foreach (var block in loop.Body) {
			foreach (var op in block.Operations) {
				if (op is StoreOp store) {
					storedMemRefs.Add(store.MemRef);
				}
			}
		}

		return storedMemRefs;
	}

	/// <summary>
	/// Checks if an operation is loop-invariant given the current set of invariant values.
	/// </summary>
	private static bool IsLoopInvariant(
		MlirOperation op,
		HashSet<MlirValue> invariantValues,
		LoopInfo loop,
		HashSet<MlirBlock> outerHeaders,
		HashSet<MlirValue> storedMemRefs) {

		// Terminators cannot be hoisted
		if (op.IsTerminator) {
			return false;
		}

		// Operations with side effects cannot be hoisted
		if (op.HasSideEffects) {
			Logger.Trace(LogCategory.Optimizer, $"      {op.Mnemonic}: has side effects, cannot hoist");
			return false;
		}

		// Loads from memory locations that have stores in the loop are NOT invariant
		if (op is LoadOp load && storedMemRefs.Contains(load.MemRef)) {
			Logger.Trace(LogCategory.Optimizer, $"      {op.Mnemonic}: loads from memory with stores in loop, cannot hoist");
			return false;
		}

		// All operands must be defined outside the loop or be invariant
		foreach (var operand in op.Operands) {
			if (!invariantValues.Contains(operand)) {
				// Check if operand is an outer loop header block argument
				// These change per outer loop iteration and are NOT invariant
				if (operand.DefiningBlockArg is MlirBlockArgument blockArg) {
					if (outerHeaders.Contains(blockArg.Block)) {
						Logger.Trace(LogCategory.Optimizer, $"      {op.Mnemonic}: operand %{operand.Id} is outer loop header arg, cannot hoist");
						return false;
					}

					// Check if it's an inner block argument within this loop
					if (loop.Body.Contains(blockArg.Block) && blockArg.Block != loop.Header) {
						Logger.Trace(LogCategory.Optimizer, $"      {op.Mnemonic}: operand %{operand.Id} is inner block arg, cannot hoist");
						return false;
					}
				}

				// Check if operand is defined by an op inside the loop
				if (operand.DefiningOp is MlirOperation defOp && defOp.ParentBlock != null) {
					if (loop.Body.Contains(defOp.ParentBlock)) {
						Logger.Trace(LogCategory.Optimizer, $"      {op.Mnemonic}: operand %{operand.Id} defined inside loop, cannot hoist");
						return false;
					}
				}

				// Operand not in invariant set and we couldn't determine it's safe
				Logger.Trace(LogCategory.Optimizer, $"      {op.Mnemonic}: operand %{operand.Id} not invariant");
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Topologically sorts invariant operations to maintain dependencies.
	/// Operations that produce values used by other operations come first.
	/// </summary>
	private static List<MlirOperation> TopologicalSort(List<MlirOperation> ops) {
		if (ops.Count <= 1) return ops;

		var result = new List<MlirOperation>();
		var remaining = new HashSet<MlirOperation>(ops);
		var opResults = new Dictionary<MlirValue, MlirOperation>();

		// Build map from results to their defining operations
		foreach (var op in ops) {
			foreach (var res in op.Results) {
				opResults[res] = op;
			}
		}

		// Repeatedly find ops with all dependencies satisfied
		while (remaining.Count > 0) {
			MlirOperation? ready = null;

			foreach (var op in remaining) {
				bool dependenciesSatisfied = true;

				foreach (var operand in op.Operands) {
					if (opResults.TryGetValue(operand, out var defOp) && remaining.Contains(defOp)) {
						dependenciesSatisfied = false;
						break;
					}
				}

				if (dependenciesSatisfied) {
					ready = op;
					break;
				}
			}

			if (ready == null) {
				// Cycle detected - should not happen with valid SSA
				Logger.Error(LogCategory.Optimizer, $"      cycle detected in invariant operations");
				break;
			}

			result.Add(ready);
			remaining.Remove(ready);
		}

		return result;
	}

	/// <summary>
	/// Hoists operations to the preheader block, inserting them before the terminator.
	/// </summary>
	private static int HoistOperations(List<MlirOperation> ops, MlirBlock preheader) {
		int hoisted = 0;

		// Find insertion point - before the terminator
		int insertionIndex = preheader.Operations.Count - 1;
		if (insertionIndex < 0) {
			Logger.Error(LogCategory.Optimizer, "      preheader has no terminator");
			return 0;
		}

		foreach (var op in ops) {
			// Remove from original block
			var originalBlock = op.ParentBlock;
			if (originalBlock == null) continue;

			originalBlock.Operations.Remove(op);

			// Insert in preheader before terminator
			preheader.InsertOperation(insertionIndex, op);
			insertionIndex++;

			Logger.Trace(LogCategory.Optimizer, $"      hoisted {op.Mnemonic} from ^{originalBlock.Name} to ^{preheader.Name}");
			hoisted++;
		}

		return hoisted;
	}
}
