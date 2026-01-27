using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Converts Standard dialect operations to X86 dialect.
/// </summary>
public static class StandardToX86Patterns {
	public static void PopulatePatterns(ConversionPatternSet patterns) {
		// Arith patterns
		patterns.Add<LowerConstantOp>();
		patterns.Add<LowerAddIOp>();
		patterns.Add<LowerSubIOp>();
		patterns.Add<LowerMulIOp>();
		patterns.Add<LowerDivSIOp>();
		patterns.Add<LowerRemSIOp>();
		patterns.Add<LowerCmpIOp>();
		patterns.Add<LowerShLIOp>();
		patterns.Add<LowerShRSIOp>();
		patterns.Add<LowerShRUIOp>();
		patterns.Add<LowerAndIOp>();
		patterns.Add<LowerOrIOp>();
		patterns.Add<LowerXOrIOp>();
		patterns.Add<LowerAddFOp>();
		patterns.Add<LowerSubFOp>();
		patterns.Add<LowerMulFOp>();
		patterns.Add<LowerDivFOp>();
		patterns.Add<LowerSIToFPOp>();
		patterns.Add<LowerCmpFOp>();
		patterns.Add<LowerNegFOp>();

		// Integer extension patterns
		patterns.Add<LowerExtUIOp>();
		patterns.Add<LowerExtSIOp>();

		// Math patterns
		patterns.Add<LowerTruncOp>();
		patterns.Add<LowerSqrtOp>();
		patterns.Add<LowerFloorOp>();
		patterns.Add<LowerCeilOp>();
		patterns.Add<LowerRoundOp>();
		patterns.Add<LowerAbsFOp>();
		patterns.Add<LowerMinFOp>();
		patterns.Add<LowerMaxFOp>();

		// Func patterns
		patterns.Add<LowerFuncCallOp>();
		patterns.Add<LowerReturnOp>();

		// Cf patterns
		patterns.Add<LowerBranchOp>();
		patterns.Add<LowerCondBranchOp>();

		// MemRef patterns
		patterns.Add<LowerLoadOp>();
		patterns.Add<LowerStoreOp>();
		patterns.Add<LowerAllocaOp>();
		patterns.Add<LowerGetGlobalOp>();

		// Heap operations (for managed memory)
		patterns.Add<LowerHeapAllocOp>();
		patterns.Add<LowerHeapReallocOp>();
		patterns.Add<LowerHeapFreeOp>();
		patterns.Add<LowerPtrAddOp>();
		patterns.Add<LowerMemMoveOp>();
		patterns.Add<LowerMemCpyOp>();

		// Maxon patterns that pass through to x86
		patterns.Add<LowerFieldPtrOp>();
	}

	/// <summary>
	/// Emits MOV instructions to pass values to block arguments.
	/// </summary>
	internal static void EmitBlockArgumentMoves(IReadOnlyList<MlirValue> args, MlirBlock destBlock, ConversionPatternRewriter rewriter) {
		for (int i = 0; i < args.Count; i++) {
			var srcValue = args[i];
			var destArg = destBlock.Arguments[i];
			bool isFloat = srcValue.Type is FloatType;

			var src = new VRegOperand(srcValue.Id, IsFloat: isFloat);
			var dst = new VRegOperand(destArg.Value.Id, IsFloat: isFloat);

			if (isFloat) rewriter.Insert(new MovsdOp(dst, src));
			else rewriter.Insert(new MovOp(dst, src));
		}
	}
}

// ============================================================================
// Base Patterns
// ============================================================================

/// <summary>
/// Base pattern for integer binary ops: mov dst, lhs; op dst, rhs
/// </summary>
public abstract class IntBinaryPattern<TArith, TX86> : ConversionPattern<TArith>
	where TArith : MlirOperation
	where TX86 : MlirOperation {

	protected override bool MatchAndRewrite(TArith op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Results[0].Id);
		var lhs = new VRegOperand(op.Operands[0].Id);
		var rhs = new VRegOperand(op.Operands[1].Id);

		rewriter.Insert(new MovOp(dst, lhs));
		rewriter.Insert((TX86)Activator.CreateInstance(typeof(TX86), dst, rhs)!);
		return true;
	}
}

/// <summary>
/// Base pattern for float binary ops: movsd dst, lhs; xxxsd dst, rhs
/// </summary>
public abstract class FloatBinaryPattern<TArith, TX86> : ConversionPattern<TArith>
	where TArith : MlirOperation
	where TX86 : MlirOperation {

	protected override bool MatchAndRewrite(TArith op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Results[0].Id, IsFloat: true);
		var lhs = new VRegOperand(op.Operands[0].Id, IsFloat: true);
		var rhs = new VRegOperand(op.Operands[1].Id, IsFloat: true);

		rewriter.Insert(new MovsdOp(dst, lhs));
		rewriter.Insert((TX86)Activator.CreateInstance(typeof(TX86), dst, rhs)!);
		return true;
	}
}

/// <summary>
/// Base pattern for idiv-based operations (division and remainder).
/// Uses a fresh virtual register for the divisor to avoid clobbering live values.
/// </summary>
public abstract class DivisionPattern<T>(X86Register resultReg) : ConversionPattern<T>
	where T : MlirOperation {

	protected override bool MatchAndRewrite(T op, ConversionPatternRewriter rewriter) {
		var rax = new RegOperand(X86Register.RAX);
		var lhs = new VRegOperand(op.Operands[0].Id);
		var rhs = new VRegOperand(op.Operands[1].Id);
		var dst = new VRegOperand(op.Results[0].Id);

		// Use a fresh virtual register for the divisor instead of hardcoding R11
		// This avoids clobbering loop variables that might be allocated to R11
		var divisorVreg = ConversionPatternRewriter.CreateVReg();

		rewriter.Insert(new MovOp(divisorVreg, rhs));
		rewriter.Insert(new MovOp(rax, lhs));
		rewriter.Insert(new CdqOp());
		rewriter.Insert(new IdivOp(divisorVreg));
		rewriter.Insert(new MovOp(dst, new RegOperand(resultReg)));
		return true;
	}
}

/// <summary>
/// Base pattern for float unary ops: op dst, src
/// </summary>
public abstract class FloatUnaryPattern<TArith, TX86> : ConversionPattern<TArith>
	where TArith : MlirOperation
	where TX86 : MlirOperation {

	protected override bool MatchAndRewrite(TArith op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Results[0].Id, IsFloat: true);
		var src = new VRegOperand(op.Operands[0].Id, IsFloat: true);

		rewriter.Insert((TX86)Activator.CreateInstance(typeof(TX86), dst, src)!);
		return true;
	}
}

/// <summary>
/// Base pattern for float rounding ops with mode parameter.
/// </summary>
public abstract class FloatRoundingPattern<T>(RoundingMode mode) : ConversionPattern<T>
	where T : MlirOperation {

	protected override bool MatchAndRewrite(T op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Results[0].Id, IsFloat: true);
		var src = new VRegOperand(op.Operands[0].Id, IsFloat: true);

		rewriter.Insert(new RoundsdOp(dst, src, mode));
		return true;
	}
}

/// <summary>
/// Base pattern for float bit manipulation (abs, neg) using mask and XMM bitwise op.
/// </summary>
public abstract class FloatBitMaskPattern<TArith, TX86>(long mask, int tempOffset) : ConversionPattern<TArith>
	where TArith : MlirOperation
	where TX86 : MlirOperation {

	protected override bool MatchAndRewrite(TArith op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Results[0].Id, IsFloat: true);
		var src = new VRegOperand(op.Operands[0].Id, IsFloat: true);
		var tempGpr = new VRegOperand(-op.Results[0].Id - tempOffset, IsFloat: false);
		var maskXmm = new VRegOperand(-op.Results[0].Id - tempOffset - 1000, IsFloat: true);

		rewriter.Insert(new MovOp(tempGpr, new ImmOperand(mask)));
		rewriter.Insert(new MovqOp(maskXmm, tempGpr));
		rewriter.Insert(new MovsdOp(dst, src));
		rewriter.Insert((TX86)Activator.CreateInstance(typeof(TX86), dst, maskXmm)!);
		return true;
	}
}

// ============================================================================
// Arith to X86 Patterns
// ============================================================================

public sealed class LowerConstantOp : ConversionPattern<ConstantOp> {
	protected override bool MatchAndRewrite(ConstantOp op, ConversionPatternRewriter rewriter) {
		if (op.Value is IntegerAttr ia) {
			rewriter.Insert(new MovOp(new VRegOperand(op.Result.Id), new ImmOperand(ia.Value)));
		} else if (op.Value is FloatAttr fa) {
			var tempGpr = new VRegOperand(-op.Result.Id - 1000, IsFloat: false);
			var floatVreg = new VRegOperand(op.Result.Id, IsFloat: true);
			rewriter.Insert(new MovOp(tempGpr, new ImmOperand(BitConverter.DoubleToInt64Bits(fa.Value))));
			rewriter.Insert(new MovqOp(floatVreg, tempGpr));
		} else {
			throw new NotSupportedException($"Unsupported constant type: {op.Value.GetType()}");
		}
		return true;
	}
}

public sealed class LowerAddIOp : IntBinaryPattern<AddIOp, AddOp>;
public sealed class LowerSubIOp : IntBinaryPattern<SubIOp, SubOp>;
public sealed class LowerShLIOp : IntBinaryPattern<ShLIOp, ShlOp>;
public sealed class LowerShRSIOp : IntBinaryPattern<ShRSIOp, SarOp>;
public sealed class LowerShRUIOp : IntBinaryPattern<ShRUIOp, ShrOp>;
public sealed class LowerAndIOp : IntBinaryPattern<AndIOp, AndOp>;
public sealed class LowerOrIOp : IntBinaryPattern<OrIOp, OrOp>;
public sealed class LowerXOrIOp : IntBinaryPattern<XOrIOp, XorOp>;

public sealed class LowerMulIOp : ConversionPattern<MulIOp> {
	protected override bool MatchAndRewrite(MulIOp op, ConversionPatternRewriter rewriter) {
		rewriter.Insert(new ImulOp(
			new VRegOperand(op.Result.Id),
			new VRegOperand(op.Lhs.Id),
			new VRegOperand(op.Rhs.Id)));
		return true;
	}
}

public sealed class LowerDivSIOp() : DivisionPattern<DivSIOp>(X86Register.RAX);
public sealed class LowerRemSIOp() : DivisionPattern<RemSIOp>(X86Register.RDX);

public sealed class LowerCmpIOp : ConversionPattern<CmpIOp> {
	protected override bool MatchAndRewrite(CmpIOp op, ConversionPatternRewriter rewriter) {
		rewriter.Insert(new CmpOp(new VRegOperand(op.Lhs.Id), new VRegOperand(op.Rhs.Id)));
		rewriter.Insert(new SetccOp(MapPredicate(op.Predicate), new VRegOperand(op.Result.Id)));
		return true;
	}

	private static X86CondCode MapPredicate(CmpIPredicate pred) => pred switch {
		CmpIPredicate.Eq => X86CondCode.E,
		CmpIPredicate.Ne => X86CondCode.NE,
		CmpIPredicate.Slt => X86CondCode.L,
		CmpIPredicate.Sle => X86CondCode.LE,
		CmpIPredicate.Sgt => X86CondCode.G,
		CmpIPredicate.Sge => X86CondCode.GE,
		CmpIPredicate.Ult => X86CondCode.B,
		CmpIPredicate.Ule => X86CondCode.BE,
		CmpIPredicate.Ugt => X86CondCode.A,
		CmpIPredicate.Uge => X86CondCode.AE,
		_ => throw new NotSupportedException($"Unsupported comparison: {pred}")
	};
}

public sealed class LowerAddFOp : FloatBinaryPattern<AddFOp, AddsdOp>;
public sealed class LowerSubFOp : FloatBinaryPattern<SubFOp, SubsdOp>;
public sealed class LowerMulFOp : FloatBinaryPattern<MulFOp, MulsdOp>;
public sealed class LowerDivFOp : FloatBinaryPattern<DivFOp, DivsdOp>;

public sealed class LowerCmpFOp : ConversionPattern<CmpFOp> {
	protected override bool MatchAndRewrite(CmpFOp op, ConversionPatternRewriter rewriter) {
		rewriter.Insert(new ComiOp(
			new VRegOperand(op.Lhs.Id, IsFloat: true),
			new VRegOperand(op.Rhs.Id, IsFloat: true)));
		rewriter.Insert(new SetccOp(MapPredicate(op.Predicate), new VRegOperand(op.Result.Id)));
		return true;
	}

	private static X86CondCode MapPredicate(CmpFPredicate pred) => pred switch {
		CmpFPredicate.Oeq => X86CondCode.E,
		CmpFPredicate.One => X86CondCode.NE,
		CmpFPredicate.Olt => X86CondCode.B,
		CmpFPredicate.Ole => X86CondCode.BE,
		CmpFPredicate.Ogt => X86CondCode.A,
		CmpFPredicate.Oge => X86CondCode.AE,
		_ => throw new NotSupportedException($"Unsupported float comparison: {pred}")
	};
}

public sealed class LowerTruncOp : ConversionPattern<TruncOp> {
	protected override bool MatchAndRewrite(TruncOp op, ConversionPatternRewriter rewriter) {
		rewriter.Insert(new CvttsdOp(
			new VRegOperand(op.Result.Id, IsFloat: false),
			new VRegOperand(op.Operand.Id, IsFloat: true)));
		return true;
	}
}

public sealed class LowerSIToFPOp : ConversionPattern<SIToFPOp> {
	protected override bool MatchAndRewrite(SIToFPOp op, ConversionPatternRewriter rewriter) {
		rewriter.Insert(new CvtsiOp(
			new VRegOperand(op.Result.Id, IsFloat: true),
			new VRegOperand(op.Operand.Id, IsFloat: false)));
		return true;
	}
}

public sealed class LowerExtUIOp : ConversionPattern<ExtUIOp> {
	protected override bool MatchAndRewrite(ExtUIOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: false);
		var src = new VRegOperand(op.Operand.Id, IsFloat: false);

		// On x86-64, sub-word loads (LoadOp) already use movzx, so the value is already zero-extended
		// in the full 64-bit register. We just need to copy the value.
		// Note: movzx with register-to-register requires the source to be a byte/word register,
		// but on x86-64 all GPRs are 64-bit and LoadOp already extended the value.
		rewriter.Insert(new MovOp(dst, src));
		return true;
	}
}

public sealed class LowerExtSIOp : ConversionPattern<ExtSIOp> {
	protected override bool MatchAndRewrite(ExtSIOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: false);
		var src = new VRegOperand(op.Operand.Id, IsFloat: false);

		// On x86-64, sub-word loads need movsx for sign-extension.
		// However, LowerLoadOp uses movzx (zero-extend) for all sub-word loads.
		// For sign extension, we'd need LowerLoadOp to use movsx instead.
		// For now, just copy (assumes source already contains the correct value).
		// TODO: Handle actual sign extension if LoadOp doesn't use movsx
		rewriter.Insert(new MovOp(dst, src));
		return true;
	}
}

// ============================================================================
// Func to X86 Patterns
// ============================================================================

public sealed class LowerFuncCallOp : ConversionPattern<FuncCallOp> {
	private static readonly X86Register[] ScratchRegs = [X86Register.R10, X86Register.R11];

	protected override bool MatchAndRewrite(FuncCallOp op, ConversionPatternRewriter rewriter) {
		// Handle stack arguments FIRST (5th parameter onward)
		// This must happen before we set up register arguments, because the register
		// setup may clobber vregs that hold stack argument values (e.g., when we pop
		// arg3/arg4 into R8/R9, we might clobber a vreg that holds arg5/arg6)
		int stackArgsCount = Math.Max(0, op.Operands.Count - WindowsX64Abi.IntArgRegs.Length);
		// Push in reverse order so they appear in memory in parameter order
		for (int i = op.Operands.Count - 1; i >= WindowsX64Abi.IntArgRegs.Length; i--) {
			var operand = op.Operands[i];
			rewriter.Insert(new PushOp(new VRegOperand(operand.Id, IsFloat: operand.Type is FloatType)));
		}

		var intArgs = new List<(int index, VRegOperand arg)>();
		var floatArgs = new List<(int index, VRegOperand arg)>();

		// Categorize arguments (only first 4)
		for (int i = 0; i < op.Operands.Count && i < WindowsX64Abi.IntArgRegs.Length; i++) {
			var operand = op.Operands[i];
			bool isFloat = operand.Type is FloatType;
			var arg = new VRegOperand(operand.Id, IsFloat: isFloat);

			if (isFloat) floatArgs.Add((i, arg));
			else intArgs.Add((i, arg));
		}

		// Handle integer arguments
		if (intArgs.Count >= 2) {
			var temps = new List<(int index, X86Operand? temp)>();
			for (int j = 0; j < intArgs.Count; j++) {
				var (i, arg) = intArgs[j];
				if (j < ScratchRegs.Length) {
					var scratch = new RegOperand(ScratchRegs[j]);
					rewriter.Insert(new MovOp(scratch, arg));
					temps.Add((i, scratch));
				} else {
					rewriter.Insert(new PushOp(arg));
					temps.Add((i, null));
				}
			}

			for (int j = temps.Count - 1; j >= 0; j--) {
				var (i, temp) = temps[j];
				var argReg = new RegOperand(WindowsX64Abi.IntArgRegs[i]);
				if (temp is null) rewriter.Insert(new PopOp(argReg));
				else rewriter.Insert(new MovOp(argReg, temp));
			}
		} else {
			foreach (var (i, arg) in intArgs) {
				rewriter.Insert(new MovOp(new RegOperand(WindowsX64Abi.IntArgRegs[i]), arg));
			}
		}

		// Handle float arguments
		foreach (var (i, arg) in floatArgs) {
			rewriter.Insert(new MovsdOp(new RegOperand(WindowsX64Abi.FloatArgRegs[i]), arg));
		}

		rewriter.Insert(new X86CallOp(op.Callee));

		// Clean up stack arguments after call (caller cleanup in x64)
		if (stackArgsCount > 0) {
			int cleanupBytes = stackArgsCount * 8;
			rewriter.Insert(new AddOp(
				new RegOperand(X86Register.RSP),
				new ImmOperand(cleanupBytes)));
		}

		// Handle result
		if (op.Result is not null) {
			bool isFloat = op.Result.Type is FloatType;
			var dst = new VRegOperand(op.Result.Id, IsFloat: isFloat);
			if (isFloat) rewriter.Insert(new MovsdOp(dst, new RegOperand(X86Register.XMM0)));
			else rewriter.Insert(new MovOp(dst, new RegOperand(X86Register.RAX)));
		}

		return true;
	}
}

public sealed class LowerReturnOp : ConversionPattern<ReturnOp> {
	protected override bool MatchAndRewrite(ReturnOp op, ConversionPatternRewriter rewriter) {
		if (op.ReturnValues.Count > 0) {
			var retVal = op.ReturnValues[0];
			bool isFloat = retVal.Type is FloatType;
			var val = new VRegOperand(retVal.Id, IsFloat: isFloat);

			if (isFloat) rewriter.Insert(new MovsdOp(new RegOperand(X86Register.XMM0), val));
			else rewriter.Insert(new MovOp(new RegOperand(X86Register.RAX), val));
		}
		rewriter.Insert(new EpilogueOp());
		rewriter.Insert(new RetOp());
		return true;
	}
}

// ============================================================================
// Cf to X86 Patterns
// ============================================================================

public sealed class LowerBranchOp : ConversionPattern<BranchOp> {
	protected override bool MatchAndRewrite(BranchOp op, ConversionPatternRewriter rewriter) {
		StandardToX86Patterns.EmitBlockArgumentMoves(op.BlockArguments, op.Destination, rewriter);
		rewriter.Insert(new JmpOp(op.Destination.Name));
		return true;
	}
}

public sealed class LowerCondBranchOp : ConversionPattern<CondBranchOp> {
	protected override bool MatchAndRewrite(CondBranchOp op, ConversionPatternRewriter rewriter) {
		var cond = new VRegOperand(op.Condition.Id);
		bool hasTrueArgs = op.TrueArguments.Count > 0;
		bool hasFalseArgs = op.FalseArguments.Count > 0;

		// Use source block name to create unique .args labels when multiple branches target same block
		string sourceBlockName = op.ParentBlock?.Name ?? "unknown";
		string trueDest = hasTrueArgs ? $"{op.TrueBlock.Name}.args.from.{sourceBlockName}" : op.TrueBlock.Name;
		string falseDest = hasFalseArgs ? $"{op.FalseBlock.Name}.args.from.{sourceBlockName}" : op.FalseBlock.Name;

		rewriter.Insert(new TestOp(cond, cond));
		rewriter.Insert(new JccOp(X86CondCode.NE, trueDest, falseDest));

		// Emit intermediate blocks for argument passing (critical edge splitting)
		if (hasFalseArgs) {
			rewriter.Insert(new LabelOp($"{op.FalseBlock.Name}.args.from.{sourceBlockName}"));
			StandardToX86Patterns.EmitBlockArgumentMoves(op.FalseArguments, op.FalseBlock, rewriter);
			rewriter.Insert(new JmpOp(op.FalseBlock.Name));
		}

		if (hasTrueArgs) {
			rewriter.Insert(new LabelOp($"{op.TrueBlock.Name}.args.from.{sourceBlockName}"));
			StandardToX86Patterns.EmitBlockArgumentMoves(op.TrueArguments, op.TrueBlock, rewriter);
			rewriter.Insert(new JmpOp(op.TrueBlock.Name));
		}

		return true;
	}
}

// ============================================================================
// MemRef to X86 Patterns
// ============================================================================

public sealed class LowerLoadOp : ConversionPattern<LoadOp> {
	protected override bool MatchAndRewrite(LoadOp op, ConversionPatternRewriter rewriter) {
		var mem = MemOperandHelper.Build(op.MemRef, op.Indices, op.Result.Type.SizeInBytes, rewriter);
		var dst = new VRegOperand(op.Result.Id, IsFloat: op.Result.Type is FloatType);

		if (op.Result.Type is FloatType) {
			rewriter.Insert(new MovsdOp(dst, mem));
		} else if (op.Result.Type is IntegerType it && it.BitWidth == 8) {
			// For byte loads, use movzx to zero-extend into 64-bit register
			rewriter.Insert(new MovzxOp(dst, mem, isByte: true));
		} else if (op.Result.Type is IntegerType it16 && it16.BitWidth == 16) {
			// For word loads, use movzx to zero-extend into 64-bit register
			rewriter.Insert(new MovzxOp(dst, mem, isByte: false));
		} else {
			rewriter.Insert(new MovOp(dst, mem));
		}
		return true;
	}
}

public sealed class LowerStoreOp : ConversionPattern<StoreOp> {
	protected override bool MatchAndRewrite(StoreOp op, ConversionPatternRewriter rewriter) {
		var mem = MemOperandHelper.Build(op.MemRef, op.Indices, op.Value.Type.SizeInBytes, rewriter);
		var src = new VRegOperand(op.Value.Id, IsFloat: op.Value.Type is FloatType);

		if (op.Value.Type is FloatType) rewriter.Insert(new MovsdOp(mem, src));
		else rewriter.Insert(new MovOp(mem, src));
		return true;
	}
}

internal static class MemOperandHelper {
	public static MemOperand Build(MlirValue memref, IReadOnlyList<MlirValue> indices,
		int size, ConversionPatternRewriter rewriter) {

		var ptr = new VRegOperand(memref.Id);

		if (indices.Count == 0) {
			return new MemOperand(Base: ptr, Size: size);
		}

		if (indices.Count == 1) {
			var idx = new VRegOperand(indices[0].Id);
			var tmp = VRegOperand.CreateTemp();
			rewriter.Insert(new LeaOp(tmp, new MemOperand(Base: ptr, Index: idx, Size: 8)));
			return new MemOperand(Base: tmp, Size: size);
		}

		throw new NotSupportedException($"Load/Store with {indices.Count} indices not supported");
	}
}

public sealed class LowerAllocaOp : ConversionPattern<AllocaOp> {
	protected override bool MatchAndRewrite(AllocaOp op, ConversionPatternRewriter rewriter) {
		var allocaInfos = rewriter.CurrentFunction?.GetMetadata<List<StackAllocaInfo>>("alloca_offsets");
		var info = (allocaInfos?.FirstOrDefault(i => i.ResultId == op.Result.Id)) ?? throw new InvalidOperationException("AllocaOp without frame offset");
		rewriter.Insert(new LeaOp(
				new VRegOperand(op.Result.Id),
				new MemOperand(Base: new RegOperand(X86Register.RBP), Displacement: info.FrameOffset)));
		return true;
	}
}

public sealed class LowerGetGlobalOp : ConversionPattern<GetGlobalOp> {
	protected override bool MatchAndRewrite(GetGlobalOp op, ConversionPatternRewriter rewriter) {
		rewriter.Insert(new LeaGlobalOp(new VRegOperand(op.Result.Id), op.Name));
		return true;
	}
}

public sealed class LowerFieldPtrOp : ConversionPattern<FieldPtrOp> {
	protected override bool MatchAndRewrite(FieldPtrOp op, ConversionPatternRewriter rewriter) {
		rewriter.Insert(new LeaOp(
			new VRegOperand(op.Result.Id),
			new MemOperand(Base: new VRegOperand(op.Struct.Id), Displacement: op.Offset)));
		return true;
	}
}

// ============================================================================
// Math to X86 Patterns
// ============================================================================

public sealed class LowerSqrtOp : FloatUnaryPattern<SqrtOp, SqrtsdOp>;
public sealed class LowerFloorOp() : FloatRoundingPattern<FloorOp>(RoundingMode.Floor);
public sealed class LowerCeilOp() : FloatRoundingPattern<CeilOp>(RoundingMode.Ceil);
public sealed class LowerRoundOp() : FloatRoundingPattern<RoundOp>(RoundingMode.Nearest);

public sealed class LowerAbsFOp() : FloatBitMaskPattern<AbsFOp, AndpdOp>(0x7FFFFFFFFFFFFFFF, 2000);
public sealed class LowerNegFOp() : FloatBitMaskPattern<NegFOp, XorpdOp>(unchecked((long)0x8000000000000000), 4000);

public sealed class LowerMinFOp : FloatBinaryPattern<MinFOp, MinsdOp>;
public sealed class LowerMaxFOp : FloatBinaryPattern<MaxFOp, MaxsdOp>;

// ============================================================================
// Heap Operation Patterns (using kernel32.dll)
// ============================================================================

/// <summary>
/// Lowers heap_alloc to HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, size).
/// </summary>
public sealed class LowerHeapAllocOp : ConversionPattern<HeapAllocOp> {
	private const int HEAP_ZERO_MEMORY = 0x08;

	protected override bool MatchAndRewrite(HeapAllocOp op, ConversionPatternRewriter rewriter) {
		// Save size in R11 (caller-saved scratch register)
		rewriter.Insert(new MovOp(new RegOperand(X86Register.R11), new VRegOperand(op.Size.Id)));

		// Call GetProcessHeap() -> RAX = hHeap
		rewriter.Insert(new X86CallOp("GetProcessHeap", isExternal: true, dllName: "kernel32.dll"));

		// Set up HeapAlloc arguments:
		// RCX = hHeap (from RAX)
		// RDX = HEAP_ZERO_MEMORY (0x08)
		// R8 = dwBytes (size)
		rewriter.Insert(new MovOp(new RegOperand(X86Register.RCX), new RegOperand(X86Register.RAX)));
		rewriter.Insert(new MovOp(new RegOperand(X86Register.RDX), new ImmOperand(HEAP_ZERO_MEMORY)));
		rewriter.Insert(new MovOp(new RegOperand(X86Register.R8), new RegOperand(X86Register.R11)));

		// Call HeapAlloc
		rewriter.Insert(new X86CallOp("HeapAlloc", isExternal: true, dllName: "kernel32.dll"));

		// Move result from RAX to destination
		rewriter.Insert(new MovOp(new VRegOperand(op.Result.Id), new RegOperand(X86Register.RAX)));

		return true;
	}
}

/// <summary>
/// Lowers heap_realloc to HeapReAlloc(GetProcessHeap(), 0, ptr, newSize).
/// </summary>
public sealed class LowerHeapReallocOp : ConversionPattern<HeapReallocOp> {
	protected override bool MatchAndRewrite(HeapReallocOp op, ConversionPatternRewriter rewriter) {
		// Save ptr in R10 and newSize in R11 (caller-saved scratch registers)
		rewriter.Insert(new MovOp(new RegOperand(X86Register.R10), new VRegOperand(op.Ptr.Id)));
		rewriter.Insert(new MovOp(new RegOperand(X86Register.R11), new VRegOperand(op.NewSize.Id)));

		// Call GetProcessHeap() -> RAX = hHeap
		rewriter.Insert(new X86CallOp("GetProcessHeap", isExternal: true, dllName: "kernel32.dll"));

		// Set up HeapReAlloc arguments:
		// RCX = hHeap (from RAX)
		// RDX = dwFlags (0)
		// R8 = lpMem (ptr)
		// R9 = dwBytes (newSize)
		rewriter.Insert(new MovOp(new RegOperand(X86Register.RCX), new RegOperand(X86Register.RAX)));
		rewriter.Insert(new MovOp(new RegOperand(X86Register.RDX), new ImmOperand(0)));
		rewriter.Insert(new MovOp(new RegOperand(X86Register.R8), new RegOperand(X86Register.R10)));
		rewriter.Insert(new MovOp(new RegOperand(X86Register.R9), new RegOperand(X86Register.R11)));

		// Call HeapReAlloc
		rewriter.Insert(new X86CallOp("HeapReAlloc", isExternal: true, dllName: "kernel32.dll"));

		// Move result from RAX to destination
		rewriter.Insert(new MovOp(new VRegOperand(op.Result.Id), new RegOperand(X86Register.RAX)));

		return true;
	}
}

/// <summary>
/// Lowers heap_free to HeapFree(GetProcessHeap(), 0, ptr).
/// </summary>
public sealed class LowerHeapFreeOp : ConversionPattern<HeapFreeOp> {
	protected override bool MatchAndRewrite(HeapFreeOp op, ConversionPatternRewriter rewriter) {
		// Save ptr in R11 (caller-saved scratch register)
		rewriter.Insert(new MovOp(new RegOperand(X86Register.R11), new VRegOperand(op.Ptr.Id)));

		// Call GetProcessHeap() -> RAX = hHeap
		rewriter.Insert(new X86CallOp("GetProcessHeap", isExternal: true, dllName: "kernel32.dll"));

		// Set up HeapFree arguments:
		// RCX = hHeap (from RAX)
		// RDX = dwFlags (0)
		// R8 = lpMem (ptr)
		rewriter.Insert(new MovOp(new RegOperand(X86Register.RCX), new RegOperand(X86Register.RAX)));
		rewriter.Insert(new MovOp(new RegOperand(X86Register.RDX), new ImmOperand(0)));
		rewriter.Insert(new MovOp(new RegOperand(X86Register.R8), new RegOperand(X86Register.R11)));

		// Call HeapFree
		rewriter.Insert(new X86CallOp("HeapFree", isExternal: true, dllName: "kernel32.dll"));

		return true;
	}
}

/// <summary>
/// Lowers ptr_add to LEA instruction.
/// </summary>
public sealed class LowerPtrAddOp : ConversionPattern<PtrAddOp> {
	protected override bool MatchAndRewrite(PtrAddOp op, ConversionPatternRewriter rewriter) {
		// Use LEA to add offset to base pointer: dst = [base + offset]
		rewriter.Insert(new LeaOp(
			new VRegOperand(op.Result.Id),
			new MemOperand(
				Base: new VRegOperand(op.Ptr.Id),
				Index: new VRegOperand(op.Offset.Id),
				Scale: 1)));

		return true;
	}
}

/// <summary>
/// Lowers memmove to inline rep movsb instruction.
/// Note: This uses forward copy only. Overlapping regions where dest > src
/// are not handled correctly. The compiler should ensure non-overlapping copies.
/// Uses: RDI=dest, RSI=src, RCX=count, direction flag must be clear.
/// </summary>
public sealed class LowerMemMoveOp : ConversionPattern<MemMoveOp> {
	protected override bool MatchAndRewrite(MemMoveOp op, ConversionPatternRewriter rewriter) {
		// Save RSI and RDI (callee-saved on Windows x64)
		rewriter.Insert(new PushOp(new RegOperand(X86Register.RSI)));
		rewriter.Insert(new PushOp(new RegOperand(X86Register.RDI)));

		// Load destination to RDI
		rewriter.Insert(new MovOp(new RegOperand(X86Register.RDI), new VRegOperand(op.Destination.Id)));

		// Load source to RSI
		rewriter.Insert(new MovOp(new RegOperand(X86Register.RSI), new VRegOperand(op.Source.Id)));

		// Load length to RCX
		rewriter.Insert(new MovOp(new RegOperand(X86Register.RCX), new VRegOperand(op.Length.Id)));

		// Clear direction flag (forward copy)
		rewriter.Insert(new CldOp());

		// rep movsb - copy RCX bytes from [RSI] to [RDI]
		rewriter.Insert(new RepMovsbOp());

		// Restore RDI and RSI (reverse order)
		rewriter.Insert(new PopOp(new RegOperand(X86Register.RDI)));
		rewriter.Insert(new PopOp(new RegOperand(X86Register.RSI)));

		return true;
	}
}

/// <summary>
/// Lowers memcpy to inline rep movsb instruction.
/// Uses: RDI=dest, RSI=src, RCX=count, direction flag must be clear.
/// </summary>
public sealed class LowerMemCpyOp : ConversionPattern<MemCpyOp> {
	protected override bool MatchAndRewrite(MemCpyOp op, ConversionPatternRewriter rewriter) {
		Logger.Debug(LogCategory.Mlir, $"LowerMemCpyOp: dest=v{op.Destination.Id}, src=v{op.Source.Id}, len=v{op.Length.Id}");

		// Save RSI and RDI (callee-saved on Windows x64)
		var push1 = rewriter.Insert(new PushOp(new RegOperand(X86Register.RSI)));
		Logger.Trace(LogCategory.Mlir, $"  Inserted: {push1.GetType().Name}");
		var push2 = rewriter.Insert(new PushOp(new RegOperand(X86Register.RDI)));
		Logger.Trace(LogCategory.Mlir, $"  Inserted: {push2.GetType().Name}");

		// Load destination to RDI
		_ = rewriter.Insert(new MovOp(new RegOperand(X86Register.RDI), new VRegOperand(op.Destination.Id)));
		Logger.Trace(LogCategory.Mlir, $"  Inserted: mov rdi, v{op.Destination.Id}");

		// Load source to RSI
		_ = rewriter.Insert(new MovOp(new RegOperand(X86Register.RSI), new VRegOperand(op.Source.Id)));
		Logger.Trace(LogCategory.Mlir, $"  Inserted: mov rsi, v{op.Source.Id}");

		// Load length to RCX
		var lenMov = rewriter.Insert(new MovOp(new RegOperand(X86Register.RCX), new VRegOperand(op.Length.Id)));
		Logger.Trace(LogCategory.Mlir, $"  Inserted: mov rcx, v{op.Length.Id} - op={lenMov.GetType().Name}, src={((MovOp)lenMov).Src}");

		// Clear direction flag (forward copy)
		var cld = rewriter.Insert(new CldOp());
		Logger.Trace(LogCategory.Mlir, $"  Inserted: {cld.GetType().Name}");

		// rep movsb - copy RCX bytes from [RSI] to [RDI]
		var rep = rewriter.Insert(new RepMovsbOp());
		Logger.Trace(LogCategory.Mlir, $"  Inserted: {rep.GetType().Name}");

		// Restore RDI and RSI (reverse order)
		rewriter.Insert(new PopOp(new RegOperand(X86Register.RDI)));
		rewriter.Insert(new PopOp(new RegOperand(X86Register.RSI)));

		return true;
	}
}
