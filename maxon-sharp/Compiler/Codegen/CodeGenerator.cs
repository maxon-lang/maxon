using MaxonSharp.Lir;

namespace MaxonSharp.Codegen;

public class CodeGenerator {
	private readonly X86Encoder _encoder = new();
	private RegisterAllocator.Allocation _alloc = null!;
	private readonly List<LirStringData> _strings = [];
	private int _divCheckCounter;

	// Scratch registers
	private static readonly Reg R0 = Reg.Rax;
	private static readonly Reg R1 = Reg.R10;


	public byte[] Generate(LirModule module) {
		_strings.AddRange(module.Strings);

		// Generate code for ALL functions
		foreach (var func in module.Functions) {
			_alloc = RegisterAllocator.Allocate(func);
			GenerateFunction(func);
		}

		// Verify main exists
		if (!module.Functions.Exists(f => f.Name == "main")) {
			throw new Exception("No main function found");
		}

		return _encoder.GetCode();
	}

	private void GenerateFunction(LirFunction func) {
		// Define function label
		_encoder.DefineLabel(func.Name);

		// Function prologue
		_encoder.Push(Reg.Rbp);
		_encoder.MovRegReg(Reg.Rbp, Reg.Rsp);

		// Allocate stack space (align to 16 bytes)
		var stackSize = _alloc.StackSize;
		if (stackSize > 0) {
			if (stackSize <= 127) {
				_encoder.SubRspImm8((byte)stackSize);
			} else {
				_encoder.SubRspImm32(stackSize);
			}
		}

		// Save callee-saved registers if used
		// For spill-everything, we mainly use caller-saved regs, so minimal saving needed

		// Store incoming parameters to stack slots
		for (var i = 0; i < Math.Min(func.Params.Count, 4); i++) {
			// Windows x64: first 4 params in RCX, RDX, R8, R9
			var paramReg = RegisterAllocator.ParamRegs[i];
			// Parameters map to first vregs (0, 1, 2, 3)
			if (_alloc.VRegToStackOffset.TryGetValue(i, out var offset)) {
				_encoder.MovMemReg(Reg.Rbp, offset, paramReg);
			}
		}

		// Generate code for each block
		foreach (var block in func.Blocks) {
			_encoder.DefineLabel(block.Label);

			foreach (var instr in block.Instructions) {
				GenerateInstruction(instr);
			}
		}
	}

	private void GenerateInstruction(LirInstr instr) {
		switch (instr) {
			case LirMov mov:
				LoadValue(R0, mov.Src);
				StoreToStack(mov.Dest.Id, R0);
				break;

			case LirLoad load:
				LoadValue(R0, load.Ptr); // R0 = ptr
				_encoder.MovRegMem(R0, R0, 0); // R0 = [R0]
				StoreToStack(load.Dest.Id, R0);
				break;

			case LirStore store:
				LoadValue(R0, store.Ptr); // R0 = ptr
				LoadValue(R1, store.Value); // R1 = value
				_encoder.MovMemRegSized(R0, 0, R1, store.Size); // [R0] = R1 with proper size
				break;

			case LirLea lea:
				// For string refs, we'd need proper relocation
				// For now, just handle stack slots
				if (lea.Addr is LirStackSlot slot) {
					_encoder.LeaRegMem(R0, Reg.Rbp, slot.Offset);
					StoreToStack(lea.Dest.Id, R0);
				} else if (lea.Addr is LirStringRef strRef) {
					// TODO: proper string handling
					_encoder.MovRegImm(R0, 0);
					StoreToStack(lea.Dest.Id, R0);
				}
				break;

			case LirAdd add:
				LoadValue(R0, add.Left);
				LoadValue(R1, add.Right);
				_encoder.AddRegReg(R0, R1);
				StoreToStack(add.Dest.Id, R0);
				break;

			case LirSub sub:
				LoadValue(R0, sub.Left);
				LoadValue(R1, sub.Right);
				_encoder.SubRegReg(R0, R1);
				StoreToStack(sub.Dest.Id, R0);
				break;

			case LirIMul mul:
				LoadValue(R0, mul.Left);
				if (mul.Right is LirImmediate imm && imm.Value >= int.MinValue && imm.Value <= int.MaxValue) {
					_encoder.IMulRegRegImm(R0, R0, (int)imm.Value);
				} else {
					LoadValue(R1, mul.Right);
					_encoder.IMulRegReg(R0, R1);
				}
				StoreToStack(mul.Dest.Id, R0);
				break;

			case LirIDiv div:
				// idiv uses RDX:RAX / operand
				LoadValue(Reg.Rax, div.Left);
				LoadValue(R1, div.Right);
				// Check for division by zero
				_encoder.TestRegReg(R1, R1);
				_encoder.JccRel32(CondCode.Ne, "_div_ok_" + _divCheckCounter);
				// If zero, trigger a controlled crash (ud2 = undefined instruction)
				_encoder.Ud2();
				_encoder.DefineLabel("_div_ok_" + _divCheckCounter++);
				_encoder.Cqo(); // Sign-extend RAX into RDX:RAX
				_encoder.IDivReg(R1); // RAX = quotient, RDX = remainder
				StoreToStack(div.Dest.Id, Reg.Rax);
				break;

			case LirMod mod:
				// Same as div but take remainder from RDX
				LoadValue(Reg.Rax, mod.Left);
				LoadValue(R1, mod.Right);
				// Check for division by zero
				_encoder.TestRegReg(R1, R1);
				_encoder.JccRel32(CondCode.Ne, "_mod_ok_" + _divCheckCounter);
				// If zero, trigger a controlled crash (ud2 = undefined instruction)
				_encoder.Ud2();
				_encoder.DefineLabel("_mod_ok_" + _divCheckCounter++);
				_encoder.Cqo();
				_encoder.IDivReg(R1);
				StoreToStack(mod.Dest.Id, Reg.Rdx);
				break;

			case LirNeg neg:
				LoadValue(R0, neg.Src);
				_encoder.NegReg(R0);
				StoreToStack(neg.Dest.Id, R0);
				break;

			case LirAnd and:
				LoadValue(R0, and.Left);
				LoadValue(R1, and.Right);
				_encoder.AndRegReg(R0, R1);
				StoreToStack(and.Dest.Id, R0);
				break;

			case LirOr or:
				LoadValue(R0, or.Left);
				LoadValue(R1, or.Right);
				_encoder.OrRegReg(R0, R1);
				StoreToStack(or.Dest.Id, R0);
				break;

			case LirXor xor:
				LoadValue(R0, xor.Left);
				LoadValue(R1, xor.Right);
				_encoder.XorRegReg(R0, R1);
				StoreToStack(xor.Dest.Id, R0);
				break;

			case LirNot not:
				LoadValue(R0, not.Src);
				_encoder.NotReg(R0);
				StoreToStack(not.Dest.Id, R0);
				break;

			case LirShl shl:
				LoadValue(R0, shl.Left);
				if (shl.Right is LirImmediate shlImm && shlImm.Value is >= 0 and <= 63) {
					_encoder.ShlRegImm(R0, (byte)shlImm.Value);
				} else {
					LoadValue(Reg.Rcx, shl.Right); // Shift count must be in CL
					_encoder.ShlRegCl(R0);
				}
				StoreToStack(shl.Dest.Id, R0);
				break;

			case LirShr shr:
				LoadValue(R0, shr.Left);
				if (shr.Right is LirImmediate shrImm && shrImm.Value is >= 0 and <= 63) {
					_encoder.SarRegImm(R0, (byte)shrImm.Value);
				} else {
					LoadValue(Reg.Rcx, shr.Right);
					_encoder.SarRegCl(R0);
				}
				StoreToStack(shr.Dest.Id, R0);
				break;

			case LirCmp cmp:
				LoadValue(R0, cmp.Left);
				LoadValue(R1, cmp.Right);
				_encoder.CmpRegReg(R0, R1);
				break;

			case LirSetCC setcc:
				var cc = LirCondToX86Cond(setcc.Cond);
				_encoder.SetCC(cc, R0); // Sets low byte
				_encoder.MovzxRegReg8(R0, R0); // Zero-extend to 64-bit
				StoreToStack(setcc.Dest.Id, R0);
				break;

			case LirRet ret:
				if (ret.Value != null) {
					LoadValue(Reg.Rax, ret.Value);
				}
				// Epilogue
				if (_alloc.StackSize > 0) {
					if (_alloc.StackSize <= 127) {
						_encoder.AddRspImm8((byte)_alloc.StackSize);
					} else {
						_encoder.AddRspImm32(_alloc.StackSize);
					}
				}
				_encoder.Pop(Reg.Rbp);
				_encoder.Ret();
				break;

			case LirJmp jmp:
				_encoder.JmpRel32(jmp.Label);
				break;

			case LirJmpCC jmpCC:
				var jcc = LirCondToX86Cond(jmpCC.Cond);
				_encoder.JccRel32(jcc, jmpCC.TrueLabel);
				_encoder.JmpRel32(jmpCC.FalseLabel);
				break;

			case LirLabelDef labelDef:
				_encoder.DefineLabel(labelDef.Name);
				break;

			case LirCall call:
				// Windows x64 calling convention
				// First 4 args in RCX, RDX, R8, R9
				// Must allocate 32-byte shadow space
				// Stack must be 16-byte aligned before CALL

				// Load args into parameter registers
				for (var i = 0; i < Math.Min(call.Args.Count, 4); i++) {
					LoadValue(RegisterAllocator.ParamRegs[i], call.Args[i]);
				}

				// Stack args (if > 4)
				var stackArgs = Math.Max(0, call.Args.Count - 4);
				for (var i = call.Args.Count - 1; i >= 4; i--) {
					LoadValue(R0, call.Args[i]);
					_encoder.Push(R0);
				}

				// Allocate 32-byte shadow space (Windows x64 ABI requirement)
				_encoder.SubRspImm8(32);

				// Call function
				_encoder.CallRel32(call.FuncName);

				// Clean up shadow space + stack args
				var stackCleanup = 32 + (stackArgs * 8);
				if (stackCleanup <= 127) {
					_encoder.AddRspImm8((byte)stackCleanup);
				} else {
					_encoder.AddRspImm32(stackCleanup);
				}

				// Store return value
				if (call.Dest != null) {
					StoreToStack(call.Dest.Id, Reg.Rax);
				}
				break;

			case LirPush push:
				LoadValue(R0, push.Value);
				_encoder.Push(R0);
				break;

			case LirPop pop:
				_encoder.Pop(R0);
				StoreToStack(pop.Dest.Id, R0);
				break;

			case LirAddressOf addr:
				_encoder.LeaRegMem(R0, Reg.Rbp, addr.Slot.Offset);
				StoreToStack(addr.Dest.Id, R0);
				break;

			// Float operations (basic support)
			case LirFAdd fadd:
				// Load to XMM registers
				LoadValueToXmm(0, fadd.Left);
				LoadValueToXmm(1, fadd.Right);
				_encoder.AddsdXmmXmm(0, 1);
				StoreFromXmm(fadd.Dest.Id, 0);
				break;

			case LirFSub fsub:
				LoadValueToXmm(0, fsub.Left);
				LoadValueToXmm(1, fsub.Right);
				_encoder.SubsdXmmXmm(0, 1);
				StoreFromXmm(fsub.Dest.Id, 0);
				break;

			case LirFMul fmul:
				LoadValueToXmm(0, fmul.Left);
				LoadValueToXmm(1, fmul.Right);
				_encoder.MulsdXmmXmm(0, 1);
				StoreFromXmm(fmul.Dest.Id, 0);
				break;

			case LirFDiv fdiv:
				LoadValueToXmm(0, fdiv.Left);
				LoadValueToXmm(1, fdiv.Right);
				_encoder.DivsdXmmXmm(0, 1);
				StoreFromXmm(fdiv.Dest.Id, 0);
				break;

			case LirIntToFloat itof:
				LoadValue(R0, itof.Src);
				_encoder.Cvtsi2sdXmmReg(0, R0);
				StoreFromXmm(itof.Dest.Id, 0);
				break;

			case LirFloatToInt ftoi:
				LoadValueToXmm(0, ftoi.Src);
				_encoder.Cvttsd2siRegXmm(R0, 0);
				StoreToStack(ftoi.Dest.Id, R0);
				break;

			case LirFCmp fcmp:
				LoadValueToXmm(0, fcmp.Left);
				LoadValueToXmm(1, fcmp.Right);
				_encoder.UcomisdXmmXmm(0, 1);
				break;

			case LirSignExtend signExt:
				LoadValue(R0, signExt.Src);
				// For now, assume already sign-extended
				StoreToStack(signExt.Dest.Id, R0);
				break;

			case LirZeroExtend zeroExt:
				LoadValue(R0, zeroExt.Src);
				// Zero-extend handled by MovzxRegReg8 if needed
				StoreToStack(zeroExt.Dest.Id, R0);
				break;

			default:
				throw new Exception($"Unsupported LIR instruction: {instr.GetType().Name}");
		}
	}

	private void LoadValue(Reg dest, LirValue value) {
		switch (value) {
			case LirVReg vreg:
				if (_alloc.VRegToStackOffset.TryGetValue(vreg.Id, out var offset)) {
					_encoder.MovRegMem(dest, Reg.Rbp, offset);
				} else {
					throw new Exception($"VReg v{vreg.Id} not allocated");
				}
				break;

			case LirImmediate imm:
				_encoder.MovRegImm(dest, imm.Value);
				break;

			case LirStackSlot slot:
				_encoder.LeaRegMem(dest, Reg.Rbp, slot.Offset);
				break;

			default:
				throw new Exception($"Unsupported LIR value: {value.GetType().Name}");
		}
	}

	private void StoreToStack(int vregId, Reg src) {
		if (_alloc.VRegToStackOffset.TryGetValue(vregId, out var offset)) {
			_encoder.MovMemReg(Reg.Rbp, offset, src);
		} else {
			throw new Exception($"VReg v{vregId} not allocated");
		}
	}

	private void LoadValueToXmm(int xmm, LirValue value) {
		switch (value) {
			case LirVReg vreg:
				if (_alloc.VRegToStackOffset.TryGetValue(vreg.Id, out var offset)) {
					_encoder.MovsdXmmMem(xmm, Reg.Rbp, offset);
				}
				break;

			case LirFloatImmediate fimm:
				// Load float constant via dedicated temp slot (avoids corrupting user variables)
				var bits = BitConverter.DoubleToInt64Bits(fimm.Value);
				_encoder.MovRegImm(R0, bits);
				_encoder.MovMemReg(Reg.Rbp, _alloc.FloatTempOffset, R0);
				_encoder.MovsdXmmMem(xmm, Reg.Rbp, _alloc.FloatTempOffset);
				break;
		}
	}

	private void StoreFromXmm(int vregId, int xmm) {
		if (_alloc.VRegToStackOffset.TryGetValue(vregId, out var offset)) {
			_encoder.MovsdMemXmm(Reg.Rbp, offset, xmm);
		}
	}

	private static CondCode LirCondToX86Cond(Lir.CondCode cond) {
		return cond switch {
			Lir.CondCode.Eq => CondCode.E,
			Lir.CondCode.Ne => CondCode.Ne,
			Lir.CondCode.Lt => CondCode.L,
			Lir.CondCode.Le => CondCode.Le,
			Lir.CondCode.Gt => CondCode.G,
			Lir.CondCode.Ge => CondCode.Ge,
			Lir.CondCode.LtU => CondCode.B,
			Lir.CondCode.LeU => CondCode.Be,
			Lir.CondCode.GtU => CondCode.A,
			Lir.CondCode.GeU => CondCode.Ae,
			_ => throw new Exception($"Unknown condition code: {cond}")
		};
	}
}

