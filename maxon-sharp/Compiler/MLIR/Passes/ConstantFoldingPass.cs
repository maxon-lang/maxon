using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Constant folding pass - evaluates constant expressions at compile time.
/// </summary>
public sealed class ConstantFoldingPass : FunctionPass {
	public override string Name => "constant-folding";
	public override string Description => "Folds constant arithmetic expressions";

	private int _foldCount;
	private int _strengthReductionCount;
	private int _branchEliminationCount;
	private int _identityFoldCount;

	private readonly FoldingContext _context = new();

	protected override bool RunOnFunction(MlirFunction func) {
		bool anyChanged = false;
		bool changed;
		int iterations = 0;
		_foldCount = 0;
		_strengthReductionCount = 0;
		_branchEliminationCount = 0;
		_identityFoldCount = 0;

		Logger.Debug(LogCategory.Optimizer, $"constant-folding: processing {func.Name}");

		// Iterate until no more changes (fixed-point)
		do {
			changed = false;
			iterations++;
			foreach (var block in func.Body.Blocks) {
				var opsToProcess = block.Operations.ToList();

				foreach (var op in opsToProcess) {
					if (TryFold(op, out var folded, out var replacementValue, out var foldType)) {
						var idx = block.Operations.IndexOf(op);
						if (idx >= 0) {
							if (foldType == FoldType.ValueReplacement && replacementValue != null) {
								// Identity fold: replace all uses of the result with the replacement value
								ReplaceAllUses(func.Body, op.Results[0], replacementValue);
								block.Operations.RemoveAt(idx);
								_identityFoldCount++;
								Logger.Trace(LogCategory.Optimizer, $"  identity-fold: {op.Mnemonic} -> {replacementValue}");
							} else if (folded != null) {
								// Operation fold: replace the operation
								block.Operations[idx] = folded;
								if (foldType == FoldType.Constant) {
									_foldCount++;
									Logger.Trace(LogCategory.Optimizer, $"  folded: {op.Mnemonic} -> {folded.Mnemonic}");
								} else if (foldType == FoldType.StrengthReduction) {
									_strengthReductionCount++;
									Logger.Trace(LogCategory.Optimizer, $"  strength-reduction: {op.Mnemonic} -> {folded.Mnemonic}");
								}
							}
							changed = true;
						}
					}
				}

				// Handle branch elimination for the terminator
				if (block.Terminator is CondBranchOp condBr && TryEliminateBranch(condBr, block)) {
					changed = true;
					_branchEliminationCount++;
				}
			}
			anyChanged |= changed;
		} while (changed);

		if (_foldCount > 0 || _strengthReductionCount > 0 || _branchEliminationCount > 0 || _identityFoldCount > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: {_foldCount} folded, {_identityFoldCount} identity-folded, {_strengthReductionCount} strength-reduced, {_branchEliminationCount} branches eliminated in {iterations} iteration(s)");
		}

		return anyChanged;
	}

	private enum FoldType { None, Constant, StrengthReduction, ValueReplacement }

	/// <summary>
	/// Replaces all uses of oldValue with newValue in the region.
	/// </summary>
	private static void ReplaceAllUses(MlirRegion region, MlirValue oldValue, MlirValue newValue) {
		foreach (var block in region.Blocks) {
			foreach (var op in block.Operations) {
				for (int i = 0; i < op.Operands.Count; i++) {
					if (op.Operands[i] == oldValue)
						op.Operands[i] = newValue;
				}
			}
		}
	}

	/// <summary>
	/// Tries to eliminate a conditional branch with a constant condition.
	/// Replaces cf.cond_br with cf.br to the appropriate target.
	/// </summary>
	private static bool TryEliminateBranch(CondBranchOp condBr, MlirBlock block) {
		var condValue = GetConstantBoolValue(condBr.Condition);
		if (condValue is null) return false;

		Logger.Trace(LogCategory.Optimizer, $"  branch elimination: cond_br with constant {condValue.Value} -> br");

		// Replace with unconditional branch to the appropriate target
		MlirBlock targetBlock;
		IReadOnlyList<MlirValue> targetArgs;

		if (condValue.Value) {
			targetBlock = condBr.TrueBlock;
			targetArgs = condBr.TrueArguments;
		} else {
			targetBlock = condBr.FalseBlock;
			targetArgs = condBr.FalseArguments;
		}

		var branchOp = new BranchOp(targetBlock, [.. targetArgs]);

		// Replace the terminator
		var idx = block.Operations.IndexOf(condBr);
		if (idx >= 0) {
			block.Operations[idx] = branchOp;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Gets the constant boolean value (0 or non-0) from a value.
	/// </summary>
	private static bool? GetConstantBoolValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is IntegerAttr intAttr) {
			return intAttr.Value != 0;
		}
		return null;
	}

	private bool TryFold(MlirOperation op, out MlirOperation? folded, out MlirValue? replacementValue, out FoldType foldType) {
		folded = null;
		replacementValue = null;
		foldType = FoldType.None;

		// Dispatch to Arith dialect operations' fold methods
		if (op is ArithOp arithOp) {
			var result = arithOp.TryFold(_context);
			if (result.IsSuccess) {
				if (result.IsValueReplacement) {
					replacementValue = result.Value;
					foldType = FoldType.ValueReplacement;
				} else {
					folded = result.Operation;
					foldType = FoldType.Constant;
				}
				return true;
			}
		}

		// Dispatch to Math dialect operations' fold methods
		if (op is MathOp mathOp) {
			var result = mathOp.TryFold(_context);
			if (result.IsSuccess) {
				if (result.IsValueReplacement) {
					replacementValue = result.Value;
					foldType = FoldType.ValueReplacement;
				} else {
					folded = result.Operation;
					foldType = FoldType.Constant;
				}
				return true;
			}
		}

		// Strength reduction for MulIOp (stays in the pass - peephole optimization, not folding)
		if (op is MulIOp mul && TryStrengthReduceMul(mul, out var shifted)) {
			folded = shifted;
			foldType = FoldType.StrengthReduction;
			return true;
		}

		return false;
	}

	private static bool TryStrengthReduceMul(MulIOp mul, out MlirOperation shifted) {
		shifted = mul;
		var lConst = FoldingContext.GetConstantValue(mul.Lhs);
		var rConst = FoldingContext.GetConstantValue(mul.Rhs);

		// x * 2^n -> x << n (only when x is not constant)
		if (lConst is null && rConst is long r && r > 0 && IsPowerOfTwo(r)) {
			shifted = CreateShiftReplacement(mul.Lhs, mul, r, mul.Result);
			return true;
		}
		if (rConst is null && lConst is long l && l > 0 && IsPowerOfTwo(l)) {
			shifted = CreateShiftReplacement(mul.Rhs, mul, l, mul.Result);
			return true;
		}
		return false;
	}

	private static bool IsPowerOfTwo(long value) {
		return value > 0 && (value & (value - 1)) == 0;
	}

	private static ShLIOp CreateShiftReplacement(MlirValue valueToShift, MulIOp mul, long powerOf2Value, MlirValue originalResult) {
		int shiftAmount = System.Numerics.BitOperations.Log2((ulong)powerOf2Value);
		var shiftConst = new ConstantOp(new IntegerAttr(shiftAmount), valueToShift.Type);
		// Insert the constant into the same block, just before the mul operation
		var block = mul.ParentBlock;
		if (block != null) {
			var idx = block.Operations.IndexOf(mul);
			if (idx >= 0) {
				block.Operations.Insert(idx, shiftConst);
			}
		}
		var shlOp = new ShLIOp(valueToShift, shiftConst.Result) {
			Results = { [0] = originalResult }
		};
		// Update DefiningOp
		originalResult.DefiningOp = shlOp;
		return shlOp;
	}
}

/// <summary>
/// Context provided to operations during constant folding.
/// Provides load propagation for getting constant values through loads.
/// </summary>
public sealed class FoldingContext {
	/// <summary>
	/// Gets the constant integer value of a value, with load propagation.
	/// </summary>
	public static long? GetConstantValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is IntegerAttr intAttr) {
			return intAttr.Value;
		}

		// Try to propagate constants through loads
		if (value.DefiningOp is LoadOp load) {
			var storedValue = GetStoredConstant(load);
			if (storedValue.HasValue) {
				return storedValue.Value;
			}
		}

		return null;
	}

	/// <summary>
	/// Gets the constant double value of a value, with load propagation.
	/// </summary>
	public static double? GetConstantDoubleValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is FloatAttr floatAttr) {
			return floatAttr.Value;
		}

		// Try to propagate constants through loads
		if (value.DefiningOp is LoadOp load) {
			var storedValue = GetStoredDoubleConstant(load);
			if (storedValue.HasValue) {
				return storedValue.Value;
			}
		}

		return null;
	}

	/// <summary>
	/// Creates a constant operation that replaces an existing operation's result.
	/// Preserves the result identity so that all uses continue to work.
	/// </summary>
	public static ConstantOp CreateConstantReplacement(MlirAttribute value, MlirValue originalResult) {
		var constOp = new ConstantOp(value, originalResult.Type) {
			Results = { [0] = originalResult }
		};
		// Update DefiningOp so subsequent folding iterations can find this constant
		originalResult.DefiningOp = constOp;
		return constOp;
	}

	private static long? GetStoredConstant(LoadOp load) {
		// Find the store that wrote to this memory location
		// Walk backwards through the block to find the most recent store to the same memref
		var block = load.ParentBlock;
		if (block == null) return null;

		var loadIdx = block.Operations.IndexOf(load);
		var memref = load.MemRef;

		// Search backwards for the most recent store to this memref
		for (int i = loadIdx - 1; i >= 0; i--) {
			var op = block.Operations[i];
			if (op is StoreOp store && MemRefsMatch(store.MemRef, memref)) {
				// Found a store to the same memref - check if the value is a constant
				if (store.Value.DefiningOp is ConstantOp constOp && constOp.Value is IntegerAttr intAttr) {
					return intAttr.Value;
				}
				// Not a constant store, stop searching
				return null;
			}
			// Check if this operation might modify the memory (call, other store to unknown location, etc.)
			if (op.HasSideEffects && op is not StoreOp) {
				// Conservative: assume memory might be modified
				return null;
			}
		}

		return null;
	}

	private static double? GetStoredDoubleConstant(LoadOp load) {
		// Find the store that wrote to this memory location
		// Walk backwards through the block to find the most recent store to the same memref
		var block = load.ParentBlock;
		if (block == null) return null;

		var loadIdx = block.Operations.IndexOf(load);
		var memref = load.MemRef;

		// Search backwards for the most recent store to this memref
		for (int i = loadIdx - 1; i >= 0; i--) {
			var op = block.Operations[i];
			if (op is StoreOp store && MemRefsMatch(store.MemRef, memref)) {
				// Found a store to the same memref - check if the value is a constant
				if (store.Value.DefiningOp is ConstantOp constOp && constOp.Value is FloatAttr floatAttr) {
					return floatAttr.Value;
				}
				// Not a constant store, stop searching
				return null;
			}
			// Check if this operation might modify the memory (call, other store to unknown location, etc.)
			if (op.HasSideEffects && op is not StoreOp) {
				// Conservative: assume memory might be modified
				return null;
			}
		}

		return null;
	}

	private static bool MemRefsMatch(MlirValue a, MlirValue b) {
		// Same value identity
		if (a == b) return true;

		// Both are GetGlobalOp referring to the same global
		if (a.DefiningOp is GetGlobalOp globalA && b.DefiningOp is GetGlobalOp globalB) {
			return globalA.Name == globalB.Name;
		}

		return false;
	}
}

/// <summary>
/// Result of a fold operation on an MLIR operation.
/// Supports three states: no fold possible, replace with operation, replace uses with existing value.
/// </summary>
public readonly struct FoldResult {
	/// <summary>
	/// The operation to replace with (constant folding).
	/// </summary>
	public MlirOperation? Operation { get; }

	/// <summary>
	/// The value to replace all uses with (identity folding like x + 0 = x).
	/// </summary>
	public MlirValue? Value { get; }

	/// <summary>
	/// Whether the fold was successful (either operation or value replacement).
	/// </summary>
	public bool IsSuccess => Operation is not null || Value is not null;

	/// <summary>
	/// Whether this is a value replacement (identity folding) rather than operation replacement.
	/// </summary>
	public bool IsValueReplacement => Value is not null;

	private FoldResult(MlirOperation? op, MlirValue? value) {
		Operation = op;
		Value = value;
	}

	/// <summary>
	/// No fold possible.
	/// </summary>
	public static FoldResult None => new(null, null);

	/// <summary>
	/// Replace the operation with a new operation (e.g., constant).
	/// </summary>
	public static FoldResult WithOperation(MlirOperation op) => new(op, null);

	/// <summary>
	/// Replace all uses of the operation's result with an existing value (identity folding).
	/// </summary>
	public static FoldResult WithValue(MlirValue value) => new(null, value);
}
