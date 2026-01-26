using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Tail call optimization pass - detects tail calls and marks them for special lowering.
///
/// A tail call is a function call that is immediately followed by a return, where the
/// return value is exactly the call result (or no return value). When lowered to x86,
/// tail calls can use a jump instead of a call instruction, avoiding stack growth.
///
/// Self-recursive tail calls are particularly important as they enable efficient
/// iteration-like behavior for recursive algorithms without stack overflow.
/// </summary>
public sealed class TailCallOptimizationPass : FunctionPass {
	public override string Name => "tco";
	public override string Description => "Tail Call Optimization";

	private int _tailCallCount;
	private int _selfRecursiveTailCallCount;

	protected override bool RunOnFunction(MlirFunction func) {
		_tailCallCount = 0;
		_selfRecursiveTailCallCount = 0;

		Logger.Debug(LogCategory.Optimizer, $"tco: processing {func.Name}");

		bool changed = false;

		foreach (var block in func.Body.Blocks) {
			changed |= ProcessBlock(block, func.Name);
		}

		if (_tailCallCount > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: marked {_tailCallCount} tail call(s) ({_selfRecursiveTailCallCount} self-recursive)");
		}

		return changed;
	}

	/// <summary>
	/// Processes a single block looking for tail call patterns.
	/// </summary>
	private bool ProcessBlock(MlirBlock block, string currentFunctionName) {
		bool changed = false;

		// Look for the pattern: FuncCallOp followed immediately by ReturnOp
		for (int i = 0; i < block.Operations.Count - 1; i++) {
			if (block.Operations[i] is not FuncCallOp call) continue;

			// Already marked as tail call? Skip
			if (call.Attributes.ContainsKey("tail_call")) continue;

			// Check if this call can be a tail call
			var (isTailCall, reason) = AnalyzeTailCall(block, i, call);

			if (isTailCall) {
				MarkAsTailCall(call, currentFunctionName);
				changed = true;
			} else {
				Logger.Trace(LogCategory.Optimizer, $"  {call.Callee}: not a tail call - {reason}");
			}
		}

		return changed;
	}

	/// <summary>
	/// Analyzes whether a call at the given index is a valid tail call.
	/// Returns (true, null) if it is, or (false, reason) if not.
	/// </summary>
	private static (bool IsTailCall, string? Reason) AnalyzeTailCall(MlirBlock block, int callIndex, FuncCallOp call) {
		// The call must be immediately followed by a return
		if (callIndex + 1 >= block.Operations.Count) {
			return (false, "call is not followed by any operation");
		}

		var nextOp = block.Operations[callIndex + 1];
		if (nextOp is not ReturnOp returnOp) {
			return (false, $"call is followed by {nextOp.Mnemonic}, not return");
		}

		// Check that there are no operations between call and return that could have side effects
		// Since we're checking callIndex + 1, we've already verified the return is immediately after

		// Validate the return value matches the call result
		if (!ValidateReturnValue(call, returnOp, out var reason)) {
			return (false, reason);
		}

		// Ensure the call result is not used anywhere else in the function
		if (call.Result is not null && !IsResultOnlyUsedByReturn(call.Result, returnOp, block)) {
			return (false, "call result is used elsewhere, not just by return");
		}

		return (true, null);
	}

	/// <summary>
	/// Validates that the return value matches what's expected for a tail call.
	/// </summary>
	private static bool ValidateReturnValue(FuncCallOp call, ReturnOp returnOp, out string? reason) {
		reason = null;

		// Case 1: void function - both call and return have no value
		if (call.Result is null && returnOp.ReturnValues.Count == 0) {
			return true;
		}

		// Case 2: non-void function - return must use exactly the call result
		if (call.Result is not null) {
			if (returnOp.ReturnValues.Count == 0) {
				reason = "call has a result but return has no value";
				return false;
			}

			if (returnOp.ReturnValues.Count != 1) {
				reason = "return has multiple values, not supported for tail call";
				return false;
			}

			if (returnOp.ReturnValues[0] != call.Result) {
				reason = "return value is not the call result";
				return false;
			}

			return true;
		}

		// Case 3: void call but non-void return - not a tail call pattern
		reason = "void call but non-void return";
		return false;
	}

	/// <summary>
	/// Checks if the call result is only used by the given return operation.
	/// This ensures we can safely optimize the call without breaking other uses.
	/// </summary>
	private static bool IsResultOnlyUsedByReturn(MlirValue result, ReturnOp returnOp, MlirBlock block) {
		// Walk through all operations in the block to find uses of the result
		foreach (var op in block.Operations) {
			if (op == result.DefiningOp) continue; // Skip the defining operation itself

			// Check if this operation uses the result
			foreach (var operand in op.Operands) {
				if (operand == result) {
					// This operation uses the result - it must be the return op
					if (op != returnOp) {
						return false;
					}
				}
			}

			// Also check nested regions for uses
			foreach (var region in op.Regions) {
				if (IsValueUsedInRegion(result, region)) {
					return false;
				}
			}
		}

		return true;
	}

	/// <summary>
	/// Checks if a value is used anywhere in a region.
	/// </summary>
	private static bool IsValueUsedInRegion(MlirValue value, MlirRegion region) {
		foreach (var block in region.Blocks) {
			foreach (var op in block.Operations) {
				foreach (var operand in op.Operands) {
					if (operand == value) {
						return true;
					}
				}

				foreach (var nestedRegion in op.Regions) {
					if (IsValueUsedInRegion(value, nestedRegion)) {
						return true;
					}
				}
			}
		}
		return false;
	}

	/// <summary>
	/// Marks a function call as a tail call by adding the tail_call attribute.
	/// </summary>
	private void MarkAsTailCall(FuncCallOp call, string currentFunctionName) {
		call.Attributes["tail_call"] = UnitAttr.Instance;
		_tailCallCount++;

		bool isSelfRecursive = call.Callee == currentFunctionName;
		if (isSelfRecursive) {
			_selfRecursiveTailCallCount++;
			Logger.Trace(LogCategory.Optimizer, $"  marked self-recursive tail call to {call.Callee}");
		} else {
			Logger.Trace(LogCategory.Optimizer, $"  marked tail call to {call.Callee}");
		}
	}
}
