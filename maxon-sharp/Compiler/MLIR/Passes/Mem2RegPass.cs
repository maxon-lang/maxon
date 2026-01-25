using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Mem2Reg pass - promotes memory allocations to SSA values.
/// This pass converts local scalar variables from alloca/load/store form
/// to direct SSA value form, enabling further optimizations like constant folding.
/// </summary>
public sealed class Mem2RegPass : FunctionPass {
	public override string Name => "mem2reg";
	public override string Description => "Promotes memory allocations to SSA values";

	protected override bool RunOnFunction(MlirFunction func) {
		bool anyChanged = false;
		bool changed;
		int promotedCount = 0;
		int iterations = 0;

		Logger.Debug(LogCategory.Optimizer, $"mem2reg: processing {func.Name}");

		// Iterate until no more changes (fixed-point)
		do {
			changed = false;
			iterations++;
			foreach (var block in func.Body.Blocks) {
				var allocas = block.Operations.OfType<AllocaOp>().ToList();

				foreach (var alloca in allocas) {
					if (TryPromote(alloca, block, func)) {
						Logger.Trace(LogCategory.Optimizer, $"  promoted alloca %{alloca.Result.Id} to SSA");
						promotedCount++;
						changed = true;
					}
				}
			}
			anyChanged |= changed;
		} while (changed);

		if (promotedCount > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: promoted {promotedCount} allocas in {iterations} iteration(s)");
		}

		return anyChanged;
	}

	/// <summary>
	/// Tries to promote an alloca to SSA form.
	/// Returns true if the alloca was promoted and removed.
	/// Only promotes allocas where all uses are in the same block (simple case).
	/// Cross-block allocas require phi insertion which is not yet implemented.
	/// </summary>
	private static bool TryPromote(AllocaOp alloca, MlirBlock allocaBlock, MlirFunction func) {
		// Collect all uses of this alloca across ALL blocks
		var loads = new List<(LoadOp op, MlirBlock block)>();
		var stores = new List<(StoreOp op, MlirBlock block)>();
		var otherUses = new List<MlirOperation>();

		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				if (op is LoadOp load && load.MemRef == alloca.Result && load.Indices.Count == 0) {
					loads.Add((load, block));
				} else if (op is StoreOp store && store.MemRef == alloca.Result && store.Indices.Count == 0) {
					stores.Add((store, block));
				} else if (UsesValue(op, alloca.Result) && op != alloca) {
					// Some other use (e.g., passing address to a function)
					otherUses.Add(op);
				}
			}
		}

		// Can't promote if there are other uses (address escapes)
		if (otherUses.Count > 0) {
			return false;
		}

		// Can't promote if there are no stores (uninitialized variable)
		if (stores.Count == 0 && loads.Count > 0) {
			return false;
		}

		// For now, only promote if ALL uses are in the same block as the alloca
		// Cross-block promotion requires phi insertion which is complex
		bool allUsesInSameBlock = stores.All(s => s.block == allocaBlock)
									&& loads.All(l => l.block == allocaBlock);
		if (!allUsesInSameBlock) {
			return false;
		}

		// Build a mapping from each load to the value that should replace it
		// For simple straight-line code, this is just the most recent store's value
		var loadReplacements = new Dictionary<LoadOp, MlirValue>();

		foreach (var (load, _) in loads) {
			var loadIdx = allocaBlock.Operations.IndexOf(load);
			MlirValue? lastStoredValue = null;

			// Walk backwards from the load to find the most recent store
			for (int i = loadIdx - 1; i >= 0; i--) {
				var op = allocaBlock.Operations[i];
				if (op is StoreOp store && store.MemRef == alloca.Result && store.Indices.Count == 0) {
					lastStoredValue = store.Value;
					break;
				}
			}

			if (lastStoredValue != null) {
				loadReplacements[load] = lastStoredValue;
			} else {
				// No store found before this load - can't promote
				return false;
			}
		}

		// Now perform the promotion:
		// 1. Replace all load results with the stored values
		foreach (var (load, replacement) in loadReplacements) {
			ReplaceAllUses(load.Result, replacement, func);
		}

		// 2. Remove all loads
		foreach (var (load, _) in loads) {
			allocaBlock.Operations.Remove(load);
		}

		// 3. Remove all stores
		foreach (var (store, _) in stores) {
			allocaBlock.Operations.Remove(store);
		}

		// 4. Remove the alloca
		allocaBlock.Operations.Remove(alloca);

		return true;
	}

	/// <summary>
	/// Checks if an operation uses a particular value.
	/// </summary>
	private static bool UsesValue(MlirOperation op, MlirValue value) {
		return op.Operands.Contains(value);
	}

	/// <summary>
	/// Replaces all uses of oldValue with newValue in the function.
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
