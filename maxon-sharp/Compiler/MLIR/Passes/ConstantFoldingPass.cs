using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.Arith;
using MaxonSharp.Compiler.Mlir.Dialects.MemRef;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Constant folding pass - evaluates constant expressions at compile time.
/// </summary>
public sealed class ConstantFoldingPass : FunctionPass {
	public override string Name => "constant-folding";
	public override string Description => "Folds constant arithmetic expressions";

	private int _foldCount;
	private int _strengthReductionCount;

	protected override bool RunOnFunction(MlirFunction func) {
		bool anyChanged = false;
		bool changed;
		int iterations = 0;
		_foldCount = 0;
		_strengthReductionCount = 0;

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
			}
			anyChanged |= changed;
		} while (changed);

		if (_foldCount > 0 || _strengthReductionCount > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: {_foldCount} folded, {_strengthReductionCount} strength-reduced in {iterations} iteration(s)");
		}

		return anyChanged;
	}

	private enum FoldType { None, Constant, StrengthReduction }

	private static bool TryFold(MlirOperation op, out MlirOperation folded, out FoldType foldType) {
		folded = op;
		foldType = FoldType.None;

		switch (op) {
			case AddIOp add when GetConstantValue(add.Lhs) is long lhs
							 && GetConstantValue(add.Rhs) is long rhs:
				folded = CreateConstantReplacement(new IntegerAttr(lhs + rhs), add.Result);
				foldType = FoldType.Constant;
				return true;

			case SubIOp sub when GetConstantValue(sub.Lhs) is long lhs
							 && GetConstantValue(sub.Rhs) is long rhs:
				folded = CreateConstantReplacement(new IntegerAttr(lhs - rhs), sub.Result);
				foldType = FoldType.Constant;
				return true;

			// Multiply by zero: x * 0 = 0, 0 * x = 0
			case MulIOp mul when GetConstantValue(mul.Lhs) is 0 || GetConstantValue(mul.Rhs) is 0:
				folded = CreateConstantReplacement(new IntegerAttr(0), mul.Result);
				foldType = FoldType.Constant;
				return true;

			// Full constant folding for multiplication (must come before strength reduction)
			case MulIOp mul when GetConstantValue(mul.Lhs) is long lhs
							 && GetConstantValue(mul.Rhs) is long rhs:
				folded = CreateConstantReplacement(new IntegerAttr(lhs * rhs), mul.Result);
				foldType = FoldType.Constant;
				return true;

			// Strength reduction: x * 2^n -> x << n (when only rhs is constant power of 2)
			case MulIOp mul when GetConstantValue(mul.Lhs) is null
							  && GetConstantValue(mul.Rhs) is long rhs
							  && rhs > 0 && IsPowerOfTwo(rhs):
				folded = CreateShiftReplacement(mul.Lhs, mul, rhs, mul.Result);
				foldType = FoldType.StrengthReduction;
				return true;

			// Strength reduction: 2^n * x -> x << n (when only lhs is constant power of 2)
			case MulIOp mul when GetConstantValue(mul.Rhs) is null
							  && GetConstantValue(mul.Lhs) is long lhs
							  && lhs > 0 && IsPowerOfTwo(lhs):
				folded = CreateShiftReplacement(mul.Rhs, mul, lhs, mul.Result);
				foldType = FoldType.StrengthReduction;
				return true;

			case DivSIOp div when GetConstantValue(div.Lhs) is long lhs
								&& GetConstantValue(div.Rhs) is long rhs && rhs != 0:
				folded = CreateConstantReplacement(new IntegerAttr(lhs / rhs), div.Result);
				foldType = FoldType.Constant;
				return true;

			case RemSIOp rem when GetConstantValue(rem.Lhs) is long lhs
								&& GetConstantValue(rem.Rhs) is long rhs && rhs != 0:
				folded = CreateConstantReplacement(new IntegerAttr(lhs % rhs), rem.Result);
				foldType = FoldType.Constant;
				return true;

			case AddFOp add when GetConstantDoubleValue(add.Lhs) is double lhs
							 && GetConstantDoubleValue(add.Rhs) is double rhs:
				folded = CreateConstantReplacement(new FloatAttr(lhs + rhs), add.Result);
				foldType = FoldType.Constant;
				return true;

			case SubFOp sub when GetConstantDoubleValue(sub.Lhs) is double lhs
							 && GetConstantDoubleValue(sub.Rhs) is double rhs:
				folded = CreateConstantReplacement(new FloatAttr(lhs - rhs), sub.Result);
				foldType = FoldType.Constant;
				return true;

			case MulFOp mul when GetConstantDoubleValue(mul.Lhs) is double lhs
							 && GetConstantDoubleValue(mul.Rhs) is double rhs:
				folded = CreateConstantReplacement(new FloatAttr(lhs * rhs), mul.Result);
				foldType = FoldType.Constant;
				return true;

			case DivFOp div when GetConstantDoubleValue(div.Lhs) is double lhs
							 && GetConstantDoubleValue(div.Rhs) is double rhs:
				folded = CreateConstantReplacement(new FloatAttr(lhs / rhs), div.Result);
				foldType = FoldType.Constant;
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
