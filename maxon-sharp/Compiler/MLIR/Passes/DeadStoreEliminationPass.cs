using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.MemRef;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Dead store elimination pass - removes stores to memory locations that are never loaded.
/// This applies to both local allocations and global variables.
///
/// A store is dead if:
/// 1. The memory location (alloca or global) is never loaded from, OR
/// 2. The store is overwritten before any load occurs
/// </summary>
public sealed class DeadStoreEliminationPass : IPass {
	public string Name => "dead-store-elimination";
	public string Description => "Removes stores to memory locations that are never loaded";

	public bool Run(MlirModule module) {
		bool changed = false;
		int removedStores = 0;
		int removedAllocas = 0;
		int removedGlobals = 0;

		Logger.Debug(LogCategory.Optimizer, "dead-store-elimination: analyzing module");

		// Track which memory locations are loaded from
		var loadedMemRefs = new HashSet<MlirValue>();
		var loadedGlobals = new HashSet<string>();

		// Track stores and allocas for potential removal
		var allStores = new List<(MlirBlock block, StoreOp op, MlirValue memref, string? globalName)>();
		var allAllocas = new List<(MlirBlock block, AllocaOp op)>();
		var allGetGlobals = new List<(MlirBlock block, GetGlobalOp op)>();

		// First pass: collect all loads, stores, allocas, and get_globals
		foreach (var func in module.Functions) {
			foreach (var block in func.Body.Blocks) {
				AnalyzeBlock(block, loadedMemRefs, loadedGlobals, allStores, allAllocas, allGetGlobals);
			}
		}

		// Remove stores to memory that is never loaded
		foreach (var (block, store, memref, globalName) in allStores) {
			bool isDead = false;

			if (globalName != null) {
				// Store to global - dead if global is never loaded
				isDead = !loadedGlobals.Contains(globalName);
			} else {
				// Store to local - dead if memref is never loaded
				isDead = !loadedMemRefs.Contains(memref);
			}

			if (isDead) {
				Logger.Trace(LogCategory.Optimizer, $"  removing dead store to {(globalName != null ? $"@{globalName}" : memref.ToString())}");
				block.Operations.Remove(store);
				removedStores++;
				changed = true;
			}
		}

		// Remove allocas that are never loaded (and now have no stores)
		foreach (var (block, alloca) in allAllocas) {
			if (!loadedMemRefs.Contains(alloca.Result)) {
				// Check if there are any remaining stores to this alloca
				bool hasStores = block.Operations.OfType<StoreOp>().Any(s => s.MemRef == alloca.Result);
				if (!hasStores) {
					Logger.Trace(LogCategory.Optimizer, $"  removing dead alloca {alloca.Result}");
					block.Operations.Remove(alloca);
					removedAllocas++;
					changed = true;
				}
			}
		}

		// Remove get_global ops for globals that are never loaded (and now have no stores)
		foreach (var (block, getGlobal) in allGetGlobals) {
			if (!loadedGlobals.Contains(getGlobal.Name)) {
				// Check if any stores to this get_global remain
				bool hasStores = block.Operations.OfType<StoreOp>().Any(s => s.MemRef == getGlobal.Result);
				if (!hasStores) {
					Logger.Trace(LogCategory.Optimizer, $"  removing dead get_global @{getGlobal.Name}");
					block.Operations.Remove(getGlobal);
					changed = true;
				}
			}
		}

		// Remove global declarations that are never loaded
		var deadGlobals = module.Globals.Where(g => !loadedGlobals.Contains(g.Name)).ToList();
		foreach (var global in deadGlobals) {
			Logger.Trace(LogCategory.Optimizer, $"  removing dead global declaration @{global.Name}");
			module.Globals.Remove(global);
			removedGlobals++;
			changed = true;
		}

		if (removedStores > 0 || removedAllocas > 0 || removedGlobals > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  removed {removedStores} dead stores, {removedAllocas} dead allocas, {removedGlobals} dead globals");
		}

		return changed;
	}

	private static void AnalyzeBlock(
		MlirBlock block,
		HashSet<MlirValue> loadedMemRefs,
		HashSet<string> loadedGlobals,
		List<(MlirBlock, StoreOp, MlirValue, string?)> allStores,
		List<(MlirBlock, AllocaOp)> allAllocas,
		List<(MlirBlock, GetGlobalOp)> allGetGlobals) {

		// Build a map of values to their defining get_global ops
		var valueToGlobal = new Dictionary<MlirValue, string>();
		foreach (var op in block.Operations) {
			if (op is GetGlobalOp getGlobal) {
				valueToGlobal[getGlobal.Result] = getGlobal.Name;
			}
		}

		foreach (var op in block.Operations.ToList()) {
			switch (op) {
				case LoadOp load:
					loadedMemRefs.Add(load.MemRef);
					if (valueToGlobal.TryGetValue(load.MemRef, out var loadGlobalName)) {
						loadedGlobals.Add(loadGlobalName);
					}
					break;

				case StoreOp store:
					string? storeGlobalName = null;
					if (valueToGlobal.TryGetValue(store.MemRef, out var gn)) {
						storeGlobalName = gn;
					}
					allStores.Add((block, store, store.MemRef, storeGlobalName));
					break;

				case AllocaOp alloca:
					allAllocas.Add((block, alloca));
					break;

				case GetGlobalOp getGlobal:
					allGetGlobals.Add((block, getGlobal));
					break;
			}

			// Handle nested regions
			foreach (var region in op.Regions) {
				foreach (var nestedBlock in region.Blocks) {
					AnalyzeBlock(nestedBlock, loadedMemRefs, loadedGlobals, allStores, allAllocas, allGetGlobals);
				}
			}
		}
	}
}
