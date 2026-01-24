using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.Func;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Eliminates functions that are never called.
/// Keeps 'main' and any functions reachable from it.
/// </summary>
public sealed class DeadFunctionEliminationPass : PassBase {
	public override string Name => "dead-function-elimination";
	public override string Description => "Eliminates unreachable functions";

	public override bool Run(MlirModule module) {
		var initialCount = module.Functions.Count;
		Logger.Debug(LogCategory.Optimizer, $"dead-function-elimination: analyzing {initialCount} functions");

		// Find all called functions starting from main
		var calledFunctions = new HashSet<string> { "main" };
		var worklist = new Queue<string>();
		worklist.Enqueue("main");

		while (worklist.Count > 0) {
			var funcName = worklist.Dequeue();
			var func = module.Functions.FirstOrDefault(f => f.Name == funcName);
			if (func is null) continue;

			// Find all call operations in this function
			foreach (var block in func.Body.Blocks) {
				foreach (var op in block.Operations) {
					if (op is CallOp call) {
						if (!calledFunctions.Contains(call.Callee)) {
							calledFunctions.Add(call.Callee);
							worklist.Enqueue(call.Callee);
						}
					}
				}
			}
		}

		// Remove functions not in the called set
		var toRemove = module.Functions
			.Where(f => !calledFunctions.Contains(f.Name))
			.ToList();

		foreach (var func in toRemove) {
			Logger.Trace(LogCategory.Optimizer, $"  eliminating dead function: {func.Name}");
			module.Functions.Remove(func);
		}

		if (toRemove.Count > 0) {
			Logger.Debug(LogCategory.Optimizer, $"dead-function-elimination: removed {toRemove.Count} unreachable functions ({initialCount} -> {module.Functions.Count})");
		}

		return toRemove.Count > 0;
	}
}
