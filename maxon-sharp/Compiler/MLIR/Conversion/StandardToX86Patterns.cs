using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.Arith;
using MaxonSharp.Compiler.Mlir.Dialects.Builtin;
using MaxonSharp.Compiler.Mlir.Dialects.Cf;
using MaxonSharp.Compiler.Mlir.Dialects.MemRef;
using MaxonSharp.Compiler.Mlir.Dialects.X86;

using FuncDialectOps = MaxonSharp.Compiler.Mlir.Dialects.Func;

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
		patterns.Add<LowerAddFOp>();
		patterns.Add<LowerSubFOp>();
		patterns.Add<LowerMulFOp>();
		patterns.Add<LowerDivFOp>();

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
	}
}

// ============================================================================
// Arith to X86 Patterns
// ============================================================================

/// <summary>
/// Lowers arith.constant to x86.mov.
/// </summary>
public sealed class LowerConstantOp : ConversionPattern<ConstantOp> {
	protected override bool MatchAndRewrite(ConstantOp op, ConversionPatternRewriter rewriter) {
		var vreg = new VRegOperand(op.Result.Id);
		var value = op.Value switch {
			IntegerAttr ia => new ImmOperand(ia.Value),
			FloatAttr fa => new ImmOperand(BitConverter.DoubleToInt64Bits(fa.Value)),
			_ => throw new NotSupportedException($"Unsupported constant type: {op.Value.GetType()}")
		};

		var mov = new MovOp(vreg, value);
		rewriter.Insert(mov);
		return true;
	}
}

/// <summary>
/// Lowers arith.addi to x86.add.
/// </summary>
public sealed class LowerAddIOp : ConversionPattern<AddIOp> {
	protected override bool MatchAndRewrite(AddIOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);

		// mov dst, lhs
		rewriter.Insert(new MovOp(dst, lhs));
		// add dst, rhs
		rewriter.Insert(new Dialects.X86.AddOp(dst, rhs));
		return true;
	}
}

/// <summary>
/// Lowers arith.subi to x86.sub.
/// </summary>
public sealed class LowerSubIOp : ConversionPattern<SubIOp> {
	protected override bool MatchAndRewrite(SubIOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);

		rewriter.Insert(new MovOp(dst, lhs));
		rewriter.Insert(new Dialects.X86.SubOp(dst, rhs));
		return true;
	}
}

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
/// Lowers arith.divsi to x86.idiv.
/// </summary>
public sealed class LowerDivSIOp : ConversionPattern<DivSIOp> {
	protected override bool MatchAndRewrite(DivSIOp op, ConversionPatternRewriter rewriter) {
		var rax = new RegOperand(X86Register.RAX);
		var rdx = new RegOperand(X86Register.RDX);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);
		var dst = new VRegOperand(op.Result.Id);

		// mov rax, lhs
		rewriter.Insert(new MovOp(rax, lhs));
		// cdq (sign extend rax to rdx:rax)
		rewriter.Insert(new CdqOp());
		// idiv rhs
		rewriter.Insert(new IdivOp(rhs));
		// mov dst, rax (quotient in rax)
		rewriter.Insert(new MovOp(dst, rax));
		return true;
	}
}

/// <summary>
/// Lowers arith.remsi (modulo) to x86.idiv.
/// </summary>
public sealed class LowerRemSIOp : ConversionPattern<RemSIOp> {
	protected override bool MatchAndRewrite(RemSIOp op, ConversionPatternRewriter rewriter) {
		var rax = new RegOperand(X86Register.RAX);
		var rdx = new RegOperand(X86Register.RDX);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);
		var dst = new VRegOperand(op.Result.Id);

		// mov rax, lhs
		rewriter.Insert(new MovOp(rax, lhs));
		// cdq (sign extend rax to rdx:rax)
		rewriter.Insert(new CdqOp());
		// idiv rhs
		rewriter.Insert(new IdivOp(rhs));
		// mov dst, rdx (remainder in rdx)
		rewriter.Insert(new MovOp(dst, rdx));
		return true;
	}
}

/// <summary>
/// Lowers arith.cmpi to x86.cmp + x86.setcc.
/// </summary>
/// <summary>
/// Lowers arith.shli to x86.shl.
/// </summary>
public sealed class LowerShLIOp : ConversionPattern<ShLIOp> {
	protected override bool MatchAndRewrite(ShLIOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);

		rewriter.Insert(new MovOp(dst, lhs));
		rewriter.Insert(new Dialects.X86.ShlOp(dst, rhs));
		return true;
	}
}

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
/// Lowers arith.addf to x86.addsd.
/// </summary>
public sealed class LowerAddFOp : ConversionPattern<AddFOp> {
	protected override bool MatchAndRewrite(AddFOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);

		rewriter.Insert(new MovsdOp(dst, lhs));
		rewriter.Insert(new AddsdOp(dst, rhs));
		return true;
	}
}

/// <summary>
/// Lowers arith.subf to x86.subsd.
/// </summary>
public sealed class LowerSubFOp : ConversionPattern<SubFOp> {
	protected override bool MatchAndRewrite(SubFOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);

		rewriter.Insert(new MovsdOp(dst, lhs));
		rewriter.Insert(new SubsdOp(dst, rhs));
		return true;
	}
}

/// <summary>
/// Lowers arith.mulf to x86.mulsd.
/// </summary>
public sealed class LowerMulFOp : ConversionPattern<MulFOp> {
	protected override bool MatchAndRewrite(MulFOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);

		rewriter.Insert(new MovsdOp(dst, lhs));
		rewriter.Insert(new MulsdOp(dst, rhs));
		return true;
	}
}

/// <summary>
/// Lowers arith.divf to x86.divsd.
/// </summary>
public sealed class LowerDivFOp : ConversionPattern<DivFOp> {
	protected override bool MatchAndRewrite(DivFOp op, ConversionPatternRewriter rewriter) {
		var dst = new VRegOperand(op.Result.Id);
		var lhs = new VRegOperand(op.Lhs.Id);
		var rhs = new VRegOperand(op.Rhs.Id);

		rewriter.Insert(new MovsdOp(dst, lhs));
		rewriter.Insert(new DivsdOp(dst, rhs));
		return true;
	}
}

// ============================================================================
// Func to X86 Patterns
// ============================================================================

/// <summary>
/// Lowers func.call to x86.call with ABI handling.
/// </summary>
public sealed class LowerFuncCallOp : ConversionPattern<FuncDialectOps.CallOp> {
	protected override bool MatchAndRewrite(FuncDialectOps.CallOp op, ConversionPatternRewriter rewriter) {
		// Windows x64 ABI: first 4 args in rcx, rdx, r8, r9
		var argRegs = new[] { X86Register.RCX, X86Register.RDX, X86Register.R8, X86Register.R9 };

		for (int i = 0; i < op.Operands.Count; i++) {
			var arg = new VRegOperand(op.Operands[i].Id);
			if (i < 4) {
				rewriter.Insert(new MovOp(new RegOperand(argRegs[i]), arg));
			} else {
				// Push remaining args on stack (right to left)
				rewriter.Insert(new PushOp(arg));
			}
		}

		rewriter.Insert(new Dialects.X86.CallOp(op.Callee));

		// Result is in rax
		if (op.Result is not null) {
			var dst = new VRegOperand(op.Result.Id);
			rewriter.Insert(new MovOp(dst, new RegOperand(X86Register.RAX)));
		}

		return true;
	}
}

/// <summary>
/// Lowers func.return to x86.epilogue + x86.ret.
/// </summary>
public sealed class LowerReturnOp : ConversionPattern<FuncDialectOps.ReturnOp> {
	protected override bool MatchAndRewrite(FuncDialectOps.ReturnOp op, ConversionPatternRewriter rewriter) {
		if (op.ReturnValues.Count > 0) {
			var val = new VRegOperand(op.ReturnValues[0].Id);
			rewriter.Insert(new MovOp(new RegOperand(X86Register.RAX), val));
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
public sealed class LowerAllocaOp : ConversionPattern<AllocaOp> {
	protected override bool MatchAndRewrite(AllocaOp op, ConversionPatternRewriter rewriter) {
		var size = op.MemRefType.ElementType.SizeInBytes;
		var rsp = new RegOperand(X86Register.RSP);
		var dst = new VRegOperand(op.Result.Id);

		// Allocate on stack
		rewriter.Insert(new Dialects.X86.SubOp(rsp, new ImmOperand(size)));
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
