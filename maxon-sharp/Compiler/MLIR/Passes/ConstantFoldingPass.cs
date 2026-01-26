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

	protected override bool RunOnFunction(MlirFunction func) {
		bool anyChanged = false;
		bool changed;
		int iterations = 0;
		_foldCount = 0;
		_strengthReductionCount = 0;
		_branchEliminationCount = 0;

		Logger.Debug(LogCategory.Optimizer, $"constant-folding: processing {func.Name}");

		// Iterate until no more changes (fixed-point)
		do {
			changed = false;
			iterations++;
			foreach (var block in func.Body.Blocks) {
				var opsToProcess = block.Operations.ToList();

				foreach (var op in opsToProcess) {
					if (TryFold(op, out var folded, out var foldType)) {
						// Replace the operation with the constant
						var idx = block.Operations.IndexOf(op);
						if (idx >= 0) {
							block.Operations[idx] = folded;
							changed = true;
							if (foldType == FoldType.Constant) {
								_foldCount++;
								Logger.Trace(LogCategory.Optimizer, $"  folded: {op.Mnemonic} -> {folded.Mnemonic}");
							} else if (foldType == FoldType.StrengthReduction) {
								_strengthReductionCount++;
								Logger.Trace(LogCategory.Optimizer, $"  strength-reduction: {op.Mnemonic} -> {folded.Mnemonic}");
							}
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

		if (_foldCount > 0 || _strengthReductionCount > 0 || _branchEliminationCount > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: {_foldCount} folded, {_strengthReductionCount} strength-reduced, {_branchEliminationCount} branches eliminated in {iterations} iteration(s)");
		}

		return anyChanged;
	}

	private enum FoldType { None, Constant, StrengthReduction }

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

	private static bool TryFold(MlirOperation op, out MlirOperation folded, out FoldType foldType) {
		folded = op;
		foldType = FoldType.None;

		// Special handling for MulIOp with load propagation
		// Check multiply by zero using pass's GetConstantValue (which propagates through loads)
		if (op is MulIOp mul) {
			var lConst = GetConstantValue(mul.Lhs);
			var rConst = GetConstantValue(mul.Rhs);

			// x * 0 = 0 (with load propagation)
			if (lConst is 0 || rConst is 0) {
				folded = CreateConstantReplacement(new IntegerAttr(0), mul.Result);
				foldType = FoldType.Constant;
				return true;
			}

			// Full constant folding (with load propagation)
			if (lConst is long l && rConst is long r) {
				folded = CreateConstantReplacement(new IntegerAttr(l * r), mul.Result);
				foldType = FoldType.Constant;
				return true;
			}

			// Strength reduction
			if (TryStrengthReduceMul(mul, out var shifted)) {
				folded = shifted;
				foldType = FoldType.StrengthReduction;
				return true;
			}
		}

		// Dispatch to Math dialect operations' fold methods
		if (op is MathOp mathOp) {
			var result = mathOp.TryFold();
			if (result != null) {
				folded = result;
				foldType = FoldType.Constant;
				return true;
			}
		}

		// Dispatch to Arith dialect operations' fold methods
		if (op is ArithOp arithOp) {
			var result = arithOp.TryFold();
			if (result != null) {
				folded = result;
				foldType = FoldType.Constant;
				return true;
			}
		}

		return false;
	}

	private static bool TryStrengthReduceMul(MulIOp mul, out MlirOperation shifted) {
		shifted = mul;
		var lConst = GetConstantValue(mul.Lhs);
		var rConst = GetConstantValue(mul.Rhs);

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

	private static long? GetConstantValue(MlirValue value) {
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

	private static bool MemRefsMatch(MlirValue a, MlirValue b) {
		// Same value identity
		if (a == b) return true;

		// Both are GetGlobalOp referring to the same global
		if (a.DefiningOp is GetGlobalOp globalA && b.DefiningOp is GetGlobalOp globalB) {
			return globalA.Name == globalB.Name;
		}

		return false;
	}

	private static double? GetConstantDoubleValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is FloatAttr floatAttr) {
			return floatAttr.Value;
		}
		return null;
	}

	private static bool IsPowerOfTwo(long value) {
		return value > 0 && (value & (value - 1)) == 0;
	}

	private static ConstantOp CreateConstantReplacement(MlirAttribute value, MlirValue originalResult) {
		var constOp = new ConstantOp(value, originalResult.Type) {
			Results = { [0] = originalResult }
		};
		// Update DefiningOp so subsequent folding iterations can find this constant
		originalResult.DefiningOp = constOp;
		return constOp;
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
