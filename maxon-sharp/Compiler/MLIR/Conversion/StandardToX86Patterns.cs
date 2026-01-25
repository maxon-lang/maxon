using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Converts Standard dialect operations to X86 dialect.
/// </summary>
public static class StandardToX86Patterns {
	/// <summary>
	/// Populates the pattern set with all Standard→X86 patterns.
	/// </summary>
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
		patterns.Add<LowerFPToSIOp>();
		patterns.Add<LowerSIToFPOp>();
		patterns.Add<LowerCmpFOp>();
		patterns.Add<LowerNegFOp>();

		// Math patterns
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

		// Maxon patterns that pass through to x86
		patterns.Add<LowerFieldPtrOp>();
	}
}

// ============================================================================
// Arith to X86 Patterns
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
/// </summary>
public abstract class DivisionPattern<T>(X86Register resultReg) : ConversionPattern<T>
	where T : MlirOperation {
	protected override bool MatchAndRewrite(T op, ConversionPatternRewriter rewriter) {
		var rax = new RegOperand(X86Register.RAX);
		var lhs = new VRegOperand(op.Operands[0].Id);
		var rhs = new VRegOperand(op.Operands[1].Id);
		var dst = new VRegOperand(op.Results[0].Id);

		rewriter.Insert(new MovOp(rax, lhs));
		rewriter.Insert(new CdqOp());
		rewriter.Insert(new IdivOp(rhs));
		rewriter.Insert(new MovOp(dst, new RegOperand(resultReg)));
		return true;
	}
}

/// <summary>
/// Lowers arith.constant to x86.mov (for integers) or mov + movq (for floats).
/// Float constants require loading bits into a GPR first, then transferring to XMM.
/// </summary>
public sealed class LowerConstantOp : ConversionPattern<ConstantOp> {
	protected override bool MatchAndRewrite(ConstantOp op, ConversionPatternRewriter rewriter) {
		if (op.Value is IntegerAttr ia) {
			// Integer: simple mov to GPR
			var vreg = new VRegOperand(op.Result.Id);
			rewriter.Insert(new MovOp(vreg, new ImmOperand(ia.Value)));
		} else if (op.Value is FloatAttr fa) {
			// Float: load bits into temp GPR, then transfer to XMM via movq
			var tempGpr = new VRegOperand(-op.Result.Id - 1000, IsFloat: false);
			var floatVreg = new VRegOperand(op.Result.Id, IsFloat: true);
			var bits = BitConverter.DoubleToInt64Bits(fa.Value);
			rewriter.Insert(new MovOp(tempGpr, new ImmOperand(bits)));
			rewriter.Insert(new MovqOp(floatVreg, tempGpr));
		} else {
			throw new NotSupportedException($"Unsupported constant type: {op.Value.GetType()}");
		}

		return true;
	}
}

/// <summary>
/// Lowers arith.addi to x86.add.
/// </summary>
public sealed class LowerAddIOp : IntBinaryPattern<AddIOp, AddOp>;

/// <summary>
/// Lowers arith.subi to x86.sub.
/// </summary>
public sealed class LowerSubIOp : IntBinaryPattern<SubIOp, SubOp>;

/// <summary>
/// Lowers arith.muli to x86.imul.
/// </summary>
public sealed class LowerMulIOp : ConversionPattern<MulIOp> {
	protected override bool MatchAndRewrite(MulIOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);

		rewriter.Insert(new ImulOp(dst, lhs, rhs));
		return true;
	}
}

/// <summary>
/// Lowers arith.divsi to x86.idiv (quotient in RAX).
/// </summary>
public sealed class LowerDivSIOp() : DivisionPattern<DivSIOp>(X86Register.RAX);

/// <summary>
/// Lowers arith.remsi (modulo) to x86.idiv (remainder in RDX).
/// </summary>
public sealed class LowerRemSIOp() : DivisionPattern<RemSIOp>(X86Register.RDX);

/// <summary>
/// Lowers arith.shli to x86.shl.
/// </summary>
public sealed class LowerShLIOp : IntBinaryPattern<ShLIOp, ShlOp>;

/// <summary>
/// Lowers arith.shrsi (arithmetic shift right) to x86.sar.
/// </summary>
public sealed class LowerShRSIOp : IntBinaryPattern<ShRSIOp, SarOp>;

/// <summary>
/// Lowers arith.shrui (logical shift right) to x86.shr.
/// </summary>
public sealed class LowerShRUIOp : IntBinaryPattern<ShRUIOp, ShrOp>;

/// <summary>
/// Lowers arith.andi to x86.and.
/// </summary>
public sealed class LowerAndIOp : IntBinaryPattern<AndIOp, AndOp>;

/// <summary>
/// Lowers arith.ori to x86.or.
/// </summary>
public sealed class LowerOrIOp : IntBinaryPattern<OrIOp, OrOp>;

/// <summary>
/// Lowers arith.xori to x86.xor.
/// </summary>
public sealed class LowerXOrIOp : IntBinaryPattern<XOrIOp, XorOp>;

public sealed class LowerCmpIOp : ConversionPattern<CmpIOp> {
	protected override bool MatchAndRewrite(CmpIOp op, ConversionPatternRewriter rewriter) {
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);
		var dst = new VRegOperand(op.Result.Id);

		rewriter.Insert(new CmpOp(lhs, rhs));

		var cc = op.Predicate switch {
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
			_ => throw new NotSupportedException($"Unsupported comparison: {op.Predicate}")
		};

		rewriter.Insert(new SetccOp(cc, dst));
		return true;
	}
}

/// <summary>
/// Lowers arith.cmpf to x86.comisd + x86.setcc.
/// COMISD sets EFLAGS based on float comparison:
/// - ZF=1, PF=1, CF=1 for unordered (NaN)
/// - ZF=0, PF=0, CF=0 for greater than
/// - ZF=1, PF=0, CF=0 for equal
/// - ZF=0, PF=0, CF=1 for less than
/// </summary>
public sealed class LowerCmpFOp : ConversionPattern<CmpFOp> {
	protected override bool MatchAndRewrite(CmpFOp op, ConversionPatternRewriter rewriter) {
		var lhs = new VRegOperand(op.Lhs.Id, IsFloat: true);
		var rhs = new VRegOperand(op.Rhs.Id, IsFloat: true);
		var dst = new VRegOperand(op.Result.Id);

		// Compare floats
		rewriter.Insert(new ComiOp(lhs, rhs));

		// Map predicate to condition code
		// Note: COMISD uses unsigned comparison flags for ordered comparisons
		var cc = op.Predicate switch {
			CmpFPredicate.Oeq => X86CondCode.E,    // ZF=1
			CmpFPredicate.One => X86CondCode.NE,   // ZF=0
			CmpFPredicate.Olt => X86CondCode.B,    // CF=1
			CmpFPredicate.Ole => X86CondCode.BE,   // CF=1 or ZF=1
			CmpFPredicate.Ogt => X86CondCode.A,    // CF=0 and ZF=0
			CmpFPredicate.Oge => X86CondCode.AE,   // CF=0
			_ => throw new NotSupportedException($"Unsupported float comparison: {op.Predicate}")
		};

		rewriter.Insert(new SetccOp(cc, dst));
		return true;
	}
}

/// <summary>
/// Lowers arith.addf to x86.addsd.
/// </summary>
public sealed class LowerAddFOp : FloatBinaryPattern<AddFOp, AddsdOp>;

/// <summary>
/// Lowers arith.subf to x86.subsd.
/// </summary>
public sealed class LowerSubFOp : FloatBinaryPattern<SubFOp, SubsdOp>;

/// <summary>
/// Lowers arith.mulf to x86.mulsd.
/// </summary>
public sealed class LowerMulFOp : FloatBinaryPattern<MulFOp, MulsdOp>;

/// <summary>
/// Lowers arith.divf to x86.divsd.
/// </summary>
public sealed class LowerDivFOp : FloatBinaryPattern<DivFOp, DivsdOp>;

/// <summary>
/// Lowers arith.fptosi to x86.cvttsd2si.
/// </summary>
public sealed class LowerFPToSIOp : ConversionPattern<FPToSIOp> {
	protected override bool MatchAndRewrite(FPToSIOp op, ConversionPatternRewriter rewriter) {
		// dst is integer (GPR), src is float (XMM)
		var dst = new VRegOperand(op.Result.Id, IsFloat: false);
		var src = new VRegOperand(op.Operand.Id, IsFloat: true);

		rewriter.Insert(new CvttsdOp(dst, src));
		return true;
	}
}

/// <summary>
/// Lowers arith.sitofp to x86.cvtsi2sd.
/// </summary>
public sealed class LowerSIToFPOp : ConversionPattern<SIToFPOp> {
	protected override bool MatchAndRewrite(SIToFPOp op, ConversionPatternRewriter rewriter) {
		// dst is float (XMM), src is integer (GPR)
		var dst = new VRegOperand(op.Result.Id, IsFloat: true);
		var src = new VRegOperand(op.Operand.Id, IsFloat: false);

		rewriter.Insert(new CvtsiOp(dst, src));
		return true;
	}
}

// ============================================================================
// Func to X86 Patterns
// ============================================================================

/// <summary>
/// Lowers func.call to x86.call with ABI handling.
/// </summary>
public sealed class LowerFuncCallOp : ConversionPattern<FuncCallOp> {
	protected override bool MatchAndRewrite(FuncCallOp op, ConversionPatternRewriter rewriter) {
		for (int i = 0; i < op.Operands.Count; i++) {
			var operand = op.Operands[i];
			bool isFloat = operand.Type is FloatType;
			var arg = new VRegOperand(operand.Id, IsFloat: isFloat);

			if (i < WindowsX64Abi.IntArgRegs.Length) {
				if (isFloat) {
					// Float arg goes in XMM register
					rewriter.Insert(new MovsdOp(new RegOperand(WindowsX64Abi.FloatArgRegs[i]), arg));
				} else {
					// Integer arg goes in GPR
					rewriter.Insert(new MovOp(new RegOperand(WindowsX64Abi.IntArgRegs[i]), arg));
				}
			} else {
				// Push remaining args on stack (right to left)
				rewriter.Insert(new PushOp(arg));
			}
		}

		rewriter.Insert(new X86CallOp(op.Callee));

		// Result handling: floats return in XMM0, integers in RAX
		if (op.Result is not null) {
			bool resultIsFloat = op.Result.Type is FloatType;
			var dst = new VRegOperand(op.Result.Id, IsFloat: resultIsFloat);
			if (resultIsFloat) {
				rewriter.Insert(new MovsdOp(dst, new RegOperand(X86Register.XMM0)));
			} else {
				rewriter.Insert(new MovOp(dst, new RegOperand(X86Register.RAX)));
			}
		}

		return true;
	}
}

/// <summary>
/// Lowers func.return to x86.epilogue + x86.ret.
/// </summary>
public sealed class LowerReturnOp : ConversionPattern<ReturnOp> {
	protected override bool MatchAndRewrite(ReturnOp op, ConversionPatternRewriter rewriter) {
		if (op.ReturnValues.Count > 0) {
			var retVal = op.ReturnValues[0];
			bool isFloat = retVal.Type is FloatType;
			var val = new VRegOperand(retVal.Id, IsFloat: isFloat);
			if (isFloat) {
				// Float return value goes in XMM0
				rewriter.Insert(new MovsdOp(new RegOperand(X86Register.XMM0), val));
			} else {
				// Integer return value goes in RAX
				rewriter.Insert(new MovOp(new RegOperand(X86Register.RAX), val));
			}
		}
		rewriter.Insert(new EpilogueOp());
		rewriter.Insert(new RetOp());
		return true;
	}
}

// ============================================================================
// Cf to X86 Patterns
// ============================================================================

/// <summary>
/// Lowers cf.br to x86.jmp.
/// </summary>
public sealed class LowerBranchOp : ConversionPattern<BranchOp> {
	protected override bool MatchAndRewrite(BranchOp op, ConversionPatternRewriter rewriter) {
		rewriter.Insert(new JmpOp(op.Destination.Name));
		return true;
	}
}

/// <summary>
/// Lowers cf.cond_br to x86.test + x86.jcc.
/// </summary>
public sealed class LowerCondBranchOp : ConversionPattern<CondBranchOp> {
	protected override bool MatchAndRewrite(CondBranchOp op, ConversionPatternRewriter rewriter) {
		var cond = new VRegOperand(op.Condition.Id);

		// test cond, cond (sets ZF if cond == 0)
		rewriter.Insert(new TestOp(cond, cond));
		// jne true_label (jump if not zero)
		rewriter.Insert(new JccOp(X86CondCode.NE, op.TrueBlock.Name, op.FalseBlock.Name));

		return true;
	}
}

// ============================================================================
// MemRef to X86 Patterns
// ============================================================================

/// <summary>
/// Lowers memref.load to x86.mov.
/// </summary>
public sealed class LowerLoadOp : ConversionPattern<LoadOp> {
	protected override bool MatchAndRewrite(LoadOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);
		var ptr = new VRegOperand(op.MemRef.Id);
		var mem = new MemOperand(Base: ptr, Size: op.Result.Type.SizeInBytes);

		rewriter.Insert(new MovOp(dst, mem));
		return true;
	}
}

/// <summary>
/// Lowers memref.store to x86.mov.
/// </summary>
public sealed class LowerStoreOp : ConversionPattern<StoreOp> {
	protected override bool MatchAndRewrite(StoreOp op, ConversionPatternRewriter rewriter) {
		var val = new VRegOperand(op.Value.Id);
		var ptr = new VRegOperand(op.MemRef.Id);
		var mem = new MemOperand(Base: ptr, Size: op.Value.Type.SizeInBytes);

		rewriter.Insert(new MovOp(mem, val));
		return true;
	}
}

/// <summary>
/// Lowers memref.alloca to x86.sub rsp + x86.lea.
/// </summary>
/// <summary>
/// Lowers memref.alloca to LEA with RBP-relative offset.
/// The actual stack allocation happens in the prologue (FunctionFramePass).
/// </summary>
public sealed class LowerAllocaOp : ConversionPattern<AllocaOp> {
	protected override bool MatchAndRewrite(AllocaOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);

		// Try to get the frame offset from function metadata (set by FunctionFramePass)
		var allocaInfos = rewriter.CurrentFunction?.GetMetadata<List<StackAllocaInfo>>("alloca_offsets");
		if (allocaInfos is not null) {
			var info = allocaInfos.FirstOrDefault(i => i.ResultId == op.Result.Id);
			if (info is not null) {
				// LEA dst, [rbp + offset] (offset is negative)
				var rbp = new RegOperand(X86Register.RBP);
				var mem = new MemOperand(Base: rbp, Displacement: info.FrameOffset);
				rewriter.Insert(new LeaOp(dst, mem));
				return true;
			}
		}

		// Fallback: immediate allocation (legacy behavior for backwards compatibility)
		// This should only happen if FunctionFramePass hasn't run yet
		var size = op.MemRefType.ElementType.SizeInBytes;
		var rsp = new RegOperand(X86Register.RSP);

		Logger.Debug(LogCategory.Codegen, $"Alloca v{op.Result.Id} without frame offset - using legacy stack allocation");

		// Allocate on stack
		rewriter.Insert(new SubOp(rsp, new ImmOperand(size)));
		// Get pointer to allocated space
		rewriter.Insert(new LeaOp(dst, new MemOperand(Base: rsp)));

		return true;
	}
}

/// <summary>
/// Lowers memref.get_global to x86.lea with RIP-relative addressing.
/// </summary>
public sealed class LowerGetGlobalOp : ConversionPattern<GetGlobalOp> {
	protected override bool MatchAndRewrite(GetGlobalOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);

		// Use lea to load the address of the global using RIP-relative addressing
		rewriter.Insert(new LeaGlobalOp(dst, op.Name));

		return true;
	}
}

/// <summary>
/// Lowers maxon.field_ptr to x86.lea with offset.
/// </summary>
public sealed class LowerFieldPtrOp : ConversionPattern<FieldPtrOp> {
	protected override bool MatchAndRewrite(FieldPtrOp op, ConversionPatternRewriter rewriter) {
		var basePtr = new VRegOperand(op.Struct.Id);
		var dst = new VRegOperand(op.Result.Id);

		// LEA dst, [base + offset]
		var mem = new MemOperand(Base: basePtr, Displacement: op.Offset);
		rewriter.Insert(new LeaOp(dst, mem));

		return true;
	}
}

// ============================================================================
// Math to X86 Patterns
// ============================================================================

/// <summary>
/// Lowers math.sqrt to x86.sqrtsd.
/// </summary>
public sealed class LowerSqrtOp : ConversionPattern<SqrtOp> {
	protected override bool MatchAndRewrite(SqrtOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: true);
		var src = new VRegOperand(op.Operand.Id, IsFloat: true);

		rewriter.Insert(new SqrtsdOp(dst, src));
		return true;
	}
}

/// <summary>
/// Lowers math.floor to x86.roundsd with floor mode.
/// </summary>
public sealed class LowerFloorOp : ConversionPattern<FloorOp> {
	protected override bool MatchAndRewrite(FloorOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: true);
		var src = new VRegOperand(op.Operand.Id, IsFloat: true);

		rewriter.Insert(new RoundsdOp(dst, src, RoundingMode.Floor));
		return true;
	}
}

/// <summary>
/// Lowers math.ceil to x86.roundsd with ceil mode.
/// </summary>
public sealed class LowerCeilOp : ConversionPattern<CeilOp> {
	protected override bool MatchAndRewrite(CeilOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: true);
		var src = new VRegOperand(op.Operand.Id, IsFloat: true);

		rewriter.Insert(new RoundsdOp(dst, src, RoundingMode.Ceil));
		return true;
	}
}

/// <summary>
/// Lowers math.round to x86.roundsd with nearest mode.
/// </summary>
public sealed class LowerRoundOp : ConversionPattern<RoundOp> {
	protected override bool MatchAndRewrite(RoundOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: true);
		var src = new VRegOperand(op.Operand.Id, IsFloat: true);

		rewriter.Insert(new RoundsdOp(dst, src, RoundingMode.Nearest));
		return true;
	}
}

/// <summary>
/// Lowers math.absf to x86.andpd with a constant mask.
/// The absolute value of a float is computed by clearing the sign bit.
/// For f64: AND with 0x7FFFFFFFFFFFFFFF to clear the sign bit.
/// We load the mask constant into an XMM register via GPR, then use ANDPD.
/// </summary>
public sealed class LowerAbsFOp : ConversionPattern<AbsFOp> {
	protected override bool MatchAndRewrite(AbsFOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: true);
		var src = new VRegOperand(op.Operand.Id, IsFloat: true);

		// Load the mask 0x7FFFFFFFFFFFFFFF into a temp GPR, then move to XMM
		// Use a unique negative ID for the temp vreg (based on result ID)
		var tempGpr = new VRegOperand(-op.Result.Id - 2000, IsFloat: false);
		var maskXmm = new VRegOperand(-op.Result.Id - 3000, IsFloat: true);

		// Load mask constant into GPR
		rewriter.Insert(new MovOp(tempGpr, new ImmOperand(0x7FFFFFFFFFFFFFFF)));
		// Transfer to XMM register
		rewriter.Insert(new MovqOp(maskXmm, tempGpr));
		// Copy source to destination first
		rewriter.Insert(new MovsdOp(dst, src));
		// AND with mask to clear sign bit
		rewriter.Insert(new AndpdOp(dst, maskXmm));

		return true;
	}
}

/// <summary>
/// Lowers math.minf to x86.minsd.
/// </summary>
public sealed class LowerMinFOp : ConversionPattern<MinFOp> {
	protected override bool MatchAndRewrite(MinFOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: true);
		var lhs = new VRegOperand(op.Lhs.Id, IsFloat: true);
		var rhs = new VRegOperand(op.Rhs.Id, IsFloat: true);

		// MINSD: dst = min(dst, src), so copy lhs to dst first
		rewriter.Insert(new MovsdOp(dst, lhs));
		rewriter.Insert(new MinsdOp(dst, rhs));
		return true;
	}
}

/// <summary>
/// Lowers math.maxf to x86.maxsd.
/// </summary>
public sealed class LowerMaxFOp : ConversionPattern<MaxFOp> {
	protected override bool MatchAndRewrite(MaxFOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: true);
		var lhs = new VRegOperand(op.Lhs.Id, IsFloat: true);
		var rhs = new VRegOperand(op.Rhs.Id, IsFloat: true);

		// MAXSD: dst = max(dst, src), so copy lhs to dst first
		rewriter.Insert(new MovsdOp(dst, lhs));
		rewriter.Insert(new MaxsdOp(dst, rhs));
		return true;
	}
}
/// <summary>
/// Lowers arith.negf to x86.xorpd with a constant mask.
/// The negation of a float is computed by flipping the sign bit.
/// For f64: XOR with 0x8000000000000000 to flip the sign bit.
/// We load the mask constant into an XMM register via GPR, then use XORPD.
/// </summary>
public sealed class LowerNegFOp : ConversionPattern<NegFOp> {
	protected override bool MatchAndRewrite(NegFOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id, IsFloat: true);
		var src = new VRegOperand(op.Operand.Id, IsFloat: true);

		// Load the mask 0x8000000000000000 into a temp GPR, then move to XMM
		// Use unique negative IDs for temp vregs (based on result ID)
		var tempGpr = new VRegOperand(-op.Result.Id - 4000, IsFloat: false);
		var maskXmm = new VRegOperand(-op.Result.Id - 5000, IsFloat: true);

		// Load mask constant into GPR (unchecked to handle sign bit constant)
		rewriter.Insert(new MovOp(tempGpr, new ImmOperand(unchecked((long)0x8000000000000000))));
		// Transfer to XMM register
		rewriter.Insert(new MovqOp(maskXmm, tempGpr));
		// Copy source to destination first
		rewriter.Insert(new MovsdOp(dst, src));
		// XOR with mask to flip sign bit
		rewriter.Insert(new XorpdOp(dst, maskXmm));

		return true;
	}
}
