using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Interface for MLIR passes that transform modules.
/// </summary>
public interface IPass {
	/// <summary>
	/// The unique name of the pass.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// A description of what the pass does.
	/// </summary>
	string Description { get; }

	/// <summary>
	/// Runs the pass on the given module.
	/// Returns true if the module was modified.
	/// </summary>
	bool Run(MlirModule module);
}

/// <summary>
/// Base class for passes with common functionality.
/// </summary>
public abstract class PassBase : IPass {
	public abstract string Name { get; }
	public abstract string Description { get; }

	public abstract bool Run(MlirModule module);

	/// <summary>
	/// Iterates over all operations in a module.
	/// </summary>
	protected static IEnumerable<MlirOperation> GetAllOperations(MlirModule module) {
		foreach (var func in module.Functions) {
			foreach (var op in GetAllOperations(func.Body)) {
				yield return op;
			}
		}
	}

	/// <summary>
	/// Iterates over all operations in a region.
	/// </summary>
	protected static IEnumerable<MlirOperation> GetAllOperations(MlirRegion region) {
		foreach (var block in region.Blocks) {
			foreach (var op in block.Operations.ToList()) {
				yield return op;
				foreach (var nestedRegion in op.Regions) {
					foreach (var nestedOp in GetAllOperations(nestedRegion)) {
						yield return nestedOp;
					}
				}
			}
		}
	}

	/// <summary>
	/// Replaces an operation with new operations.
	/// </summary>
	protected static void ReplaceOp(MlirBlock block, MlirOperation oldOp, IEnumerable<MlirOperation> newOps) {
		var idx = block.Operations.IndexOf(oldOp);
		block.Operations.RemoveAt(idx);
		foreach (var newOp in newOps.Reverse()) {
			block.Operations.Insert(idx, newOp);
		}
	}

	/// <summary>
	/// Removes an operation from its parent block.
	/// </summary>
	protected static void EraseOp(MlirBlock block, MlirOperation op) {
		block.Operations.Remove(op);
	}
}

/// <summary>
/// Pass that operates on individual functions.
/// </summary>
public abstract class FunctionPass : PassBase {
	public override bool Run(MlirModule module) {
		bool changed = false;
		foreach (var func in module.Functions) {
			changed |= RunOnFunction(func);
		}
		return changed;
	}

	/// <summary>
	/// Runs the pass on a single function.
	/// </summary>
	protected abstract bool RunOnFunction(MlirFunction func);
}

/// <summary>
/// Pass that operates on individual operations.
/// </summary>
public abstract class OperationPass<TOp> : PassBase where TOp : MlirOperation {
	public override bool Run(MlirModule module) {
		bool changed = false;
		foreach (var op in GetAllOperations(module)) {
			if (op is TOp typedOp) {
				changed |= RunOnOperation(typedOp);
			}
		}
		return changed;
	}

	/// <summary>
	/// Runs the pass on a matching operation.
	/// </summary>
	protected abstract bool RunOnOperation(TOp op);
}
