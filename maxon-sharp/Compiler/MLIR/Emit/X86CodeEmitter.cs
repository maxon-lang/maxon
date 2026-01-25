using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Emit;

/// <summary>
/// Emits machine code from X86 dialect operations.
/// </summary>
public sealed class X86CodeEmitter {
	private readonly List<byte> _code = [];
	private readonly Dictionary<string, int> _labelOffsets = [];
	private readonly List<(int offset, string label, int instrSize)> _labelFixups = [];

	// Global data support
	private readonly List<byte> _data = [];
	private readonly Dictionary<string, int> _globalOffsets = [];
	private readonly List<(int codeOffset, string globalName, int instrSize)> _globalFixups = [];

	/// <summary>
	/// Gets the emitted machine code.
	/// </summary>
	public byte[] GetCode() => [.. _code];

	/// <summary>
	/// Gets the emitted global data.
	/// </summary>
	public byte[] GetData() => [.. _data];

	/// <summary>
	/// Gets the current code offset.
	/// </summary>
	public int CurrentOffset => _code.Count;

	/// <summary>
	/// Defines a global variable and returns its offset in the data section.
	/// </summary>
	public int DefineGlobal(string name, int size, long initialValue = 0) {
		var offset = _data.Count;
		_globalOffsets[name] = offset;

		Logger.Trace(LogCategory.Codegen, $"DefineGlobal: {name} at offset {offset}, size {size}");

		// Write initial value as bytes (little-endian)
		for (int i = 0; i < size; i++) {
			_data.Add((byte)(initialValue >> (i * 8)));
		}

		return offset;
	}

	/// <summary>
	/// Emits machine code for an X86 operation.
	/// </summary>
	public void Emit(X86Op op) {
		var startOffset = CurrentOffset;
		switch (op) {
			case MovOp mov:
				EmitMov(mov);
				break;
			case AddOp add:
				EmitAdd(add);
				break;
			case SubOp sub:
				EmitSub(sub);
				break;
			case ImulOp imul:
				EmitImul(imul);
				break;
			case IdivOp idiv:
				EmitIdiv(idiv);
				break;
			case CmpOp cmp:
				EmitCmp(cmp);
				break;
			case TestOp test:
				EmitTest(test);
				break;
			case SetccOp setcc:
				EmitSetcc(setcc);
				break;
			case JmpOp jmp:
				EmitJmp(jmp);
				break;
			case JccOp jcc:
				EmitJcc(jcc);
				break;
			case X86CallOp call:
				EmitCall(call);
				break;
			case RetOp:
				EmitRet();
				break;
			case LabelOp label:
				DefineLabel(label.Name);
				break;
			case PushOp push:
				EmitPush(push);
				break;
			case PopOp pop:
				EmitPop(pop);
				break;
			case CdqOp:
				EmitCdq();
				break;
			case LeaOp lea:
				EmitLea(lea);
				break;
			case LeaGlobalOp leaGlobal:
				EmitLeaGlobal(leaGlobal);
				break;
			case ShlOp shl:
				EmitShl(shl);
				break;
			case ShrOp shr:
				EmitShr(shr);
				break;
			case SarOp sar:
				EmitSar(sar);
				break;
			case AndOp and:
				EmitAnd(and);
				break;
			case OrOp or:
				EmitOr(or);
				break;
			case XorOp xor:
				EmitXor(xor);
				break;
			case NotOp not:
				EmitNot(not);
				break;
			// Floating point
			case MovsdOp movsd:
				EmitMovsd(movsd);
				break;
			case AddsdOp addsd:
				EmitAddsd(addsd);
				break;
			case SubsdOp subsd:
				EmitSubsd(subsd);
				break;
			case MulsdOp mulsd:
				EmitMulsd(mulsd);
				break;
			case DivsdOp divsd:
				EmitDivsd(divsd);
				break;
			case MovqOp movq:
				EmitMovq(movq);
				break;
			case CvttsdOp cvttsd:
				EmitCvttsd2si(cvttsd);
				break;
			case CvtsiOp cvtsi:
				EmitCvtsi2sd(cvtsi);
				break;
			case ComiOp comi:
				EmitComisd(comi);
				break;
			// SSE Math operations
			case SqrtsdOp sqrtsd:
				EmitSqrtsd(sqrtsd);
				break;
			case RoundsdOp roundsd:
				EmitRoundsd(roundsd);
				break;
			case MinsdOp minsd:
				EmitMinsd(minsd);
				break;
			case MaxsdOp maxsd:
				EmitMaxsd(maxsd);
				break;
			case AndpdOp andpd:
				EmitAndpd(andpd);
				break;
			case PrologueOp prologue:
				EmitPrologue(prologue);
				break;
			case EpilogueOp:
				EmitEpilogue();
				break;
			default:
				throw new NotSupportedException($"Unsupported X86 operation: {op.GetType().Name}");
		}

		var bytesEmitted = CurrentOffset - startOffset;
		if (bytesEmitted > 0) {
			Logger.Trace(LogCategory.Codegen, $"  {op.GetType().Name}: {bytesEmitted} bytes at offset 0x{startOffset:X}");
		}
	}

	/// <summary>
	/// Emits function prologue: push rbp; mov rbp, rsp; sub rsp, N
	/// </summary>
	private void EmitPrologue(PrologueOp op) {
		// push rbp
		EmitByte(0x55);

		// mov rbp, rsp (REX.W + MOV r/m64, r64)
		EmitRexW(X86Register.RSP, X86Register.RBP);
		EmitByte(0x89);
		EmitByte(ModRM(0b11, GetRegCode(X86Register.RSP), GetRegCode(X86Register.RBP)));

		// sub rsp, N
		if (op.StackSize > 0) {
			EmitRexW(rm: X86Register.RSP);
			EmitByte(0x83);
			EmitByte(ModRM(0b11, 5, GetRegCode(X86Register.RSP)));
			EmitByte((byte)op.StackSize);
		}
	}

	/// <summary>
	/// Emits function epilogue: mov rsp, rbp; pop rbp
	/// </summary>
	private void EmitEpilogue() {
		// mov rsp, rbp (REX.W + MOV r/m64, r64)
		EmitRexW(X86Register.RBP, X86Register.RSP);
		EmitByte(0x89);
		EmitByte(ModRM(0b11, GetRegCode(X86Register.RBP), GetRegCode(X86Register.RSP)));

		// pop rbp
		EmitByte(0x5D);
	}

	/// <summary>
	/// Resolves all label references.
	/// </summary>
	public void ResolveLabels() {
		Logger.Debug(LogCategory.Codegen, $"Resolving {_labelFixups.Count} label references");

		foreach (var (offset, label, instrSize) in _labelFixups) {
			if (!_labelOffsets.TryGetValue(label, out var targetOffset)) {
				throw new InvalidOperationException($"Undefined label: {label}");
			}

			// Calculate relative offset (from end of instruction)
			var relOffset = targetOffset - (offset + instrSize);

			Logger.Trace(LogCategory.Codegen, $"  {label}: offset 0x{offset:X} -> target 0x{targetOffset:X} (rel {relOffset})");

			// Patch the displacement (assuming 32-bit displacement at offset)
			_code[offset] = (byte)relOffset;
			_code[offset + 1] = (byte)(relOffset >> 8);
			_code[offset + 2] = (byte)(relOffset >> 16);
			_code[offset + 3] = (byte)(relOffset >> 24);
		}
	}

	/// <summary>
	/// Resolves all global variable references.
	/// Must be called after code emission is complete but before writing to PE.
	/// </summary>
	/// <param name="dataRvaOffset">The RVA offset of the data section relative to code section end</param>
	public void ResolveGlobals(int dataRvaOffset) {
		Logger.Debug(LogCategory.Codegen, $"Resolving {_globalFixups.Count} global references");

		foreach (var (codeOffset, globalName, instrSize) in _globalFixups) {
			if (!_globalOffsets.TryGetValue(globalName, out var globalOffset)) {
				throw new InvalidOperationException($"Undefined global: {globalName}");
			}

			// RIP-relative addressing: displacement is from end of instruction to target
			// Target = code_end + dataRvaOffset + globalOffset
			// RIP at instruction end = codeOffset + instrSize
			// So displacement = code_end + dataRvaOffset + globalOffset - (codeOffset + instrSize)
			var codeEnd = _code.Count;
			var targetAddr = codeEnd + dataRvaOffset + globalOffset;
			var ripAtEnd = codeOffset + instrSize;
			var relOffset = targetAddr - ripAtEnd;

			Logger.Trace(LogCategory.Codegen, $"  {globalName}: code offset 0x{codeOffset:X} -> data offset 0x{globalOffset:X}");

			// Patch the displacement
			_code[codeOffset] = (byte)relOffset;
			_code[codeOffset + 1] = (byte)(relOffset >> 8);
			_code[codeOffset + 2] = (byte)(relOffset >> 16);
			_code[codeOffset + 3] = (byte)(relOffset >> 24);
		}
	}

	/// <summary>
	/// Gets whether there are any globals defined.
	/// </summary>
	public bool HasGlobals => _data.Count > 0;

	/// <summary>
	/// Defines a label at the current position.
	/// </summary>
	public void DefineLabel(string name) {
		_labelOffsets[name] = CurrentOffset;
		Logger.Trace(LogCategory.Codegen, $"Label: {name} at offset 0x{CurrentOffset:X}");
	}

	// ========================================================================
	// Encoding helpers
	// ========================================================================

	private void EmitByte(byte b) => _code.Add(b);

	private void EmitBytes(params byte[] bytes) => _code.AddRange(bytes);

	private void EmitImm32(int value) {
		EmitByte((byte)value);
		EmitByte((byte)(value >> 8));
		EmitByte((byte)(value >> 16));
		EmitByte((byte)(value >> 24));
	}

	private void EmitImm64(long value) {
		EmitImm32((int)value);
		EmitImm32((int)(value >> 32));
	}

	private static byte GetRegCode(X86Register reg) => reg switch {
		X86Register.RAX or X86Register.EAX or X86Register.AX or X86Register.AL => 0,
		X86Register.RCX or X86Register.ECX or X86Register.CX or X86Register.CL => 1,
		X86Register.RDX or X86Register.EDX or X86Register.DX or X86Register.DL => 2,
		X86Register.RBX or X86Register.EBX or X86Register.BX or X86Register.BL => 3,
		X86Register.RSP or X86Register.ESP or X86Register.SP or X86Register.SPL => 4,
		X86Register.RBP or X86Register.EBP or X86Register.BP or X86Register.BPL => 5,
		X86Register.RSI or X86Register.ESI or X86Register.SI or X86Register.SIL => 6,
		X86Register.RDI or X86Register.EDI or X86Register.DI or X86Register.DIL => 7,
		X86Register.R8 or X86Register.R8D or X86Register.R8W or X86Register.R8B => 0,
		X86Register.R9 or X86Register.R9D or X86Register.R9W or X86Register.R9B => 1,
		X86Register.R10 or X86Register.R10D or X86Register.R10W or X86Register.R10B => 2,
		X86Register.R11 or X86Register.R11D or X86Register.R11W or X86Register.R11B => 3,
		X86Register.R12 or X86Register.R12D or X86Register.R12W or X86Register.R12B => 4,
		X86Register.R13 or X86Register.R13D or X86Register.R13W or X86Register.R13B => 5,
		X86Register.R14 or X86Register.R14D or X86Register.R14W or X86Register.R14B => 6,
		X86Register.R15 or X86Register.R15D or X86Register.R15W or X86Register.R15B => 7,
		X86Register.XMM0 => 0,
		X86Register.XMM1 => 1,
		X86Register.XMM2 => 2,
		X86Register.XMM3 => 3,
		X86Register.XMM4 => 4,
		X86Register.XMM5 => 5,
		X86Register.XMM6 => 6,
		X86Register.XMM7 => 7,
		_ => throw new NotSupportedException($"Unsupported register: {reg}")
	};

	private static bool NeedsRex(X86Register reg) =>
		reg is >= X86Register.R8 and <= X86Register.R15 or
				 >= X86Register.R8D and <= X86Register.R15D or
				 >= X86Register.R8W and <= X86Register.R15W or
				 >= X86Register.R8B and <= X86Register.R15B;

	private void EmitRexW(X86Register? r = null, X86Register? rm = null) {
		byte rex = 0x48; // REX.W
		if (r is not null && NeedsRex(r.Value)) rex |= 0x04; // REX.R
		if (rm is not null && NeedsRex(rm.Value)) rex |= 0x01; // REX.B
		EmitByte(rex);
	}

	private static byte ModRM(byte mod, byte reg, byte rm) =>
		(byte)((mod << 6) | (reg << 3) | rm);

	private static byte GetSetccOpcode(X86CondCode cc) => cc switch {
		X86CondCode.E => 0x94,
		X86CondCode.NE => 0x95,
		X86CondCode.L => 0x9C,
		X86CondCode.LE => 0x9E,
		X86CondCode.G => 0x9F,
		X86CondCode.GE => 0x9D,
		X86CondCode.B => 0x92,
		X86CondCode.BE => 0x96,
		X86CondCode.A => 0x97,
		X86CondCode.AE => 0x93,
		_ => throw new NotSupportedException($"Unsupported condition: {cc}")
	};

	private static byte GetJccOpcode(X86CondCode cc) => cc switch {
		X86CondCode.E => 0x84,
		X86CondCode.NE => 0x85,
		X86CondCode.L => 0x8C,
		X86CondCode.LE => 0x8E,
		X86CondCode.G => 0x8F,
		X86CondCode.GE => 0x8D,
		X86CondCode.B => 0x82,
		X86CondCode.BE => 0x86,
		X86CondCode.A => 0x87,
		X86CondCode.AE => 0x83,
		_ => throw new NotSupportedException($"Unsupported condition: {cc}")
	};

	// ========================================================================
	// Instruction encodings
	// ========================================================================

	private void EmitMov(MovOp op) {
		switch (op.Dst, op.Src) {
			case (RegOperand dst, ImmOperand src):
				// MOV r64, imm64
				EmitRexW(rm: dst.Register);
				EmitByte((byte)(0xB8 + GetRegCode(dst.Register)));
				EmitImm64(src.Value);
				break;

			case (RegOperand dst, RegOperand src):
				// MOV r64, r64
				EmitRexW(src.Register, dst.Register);
				EmitByte(0x89);
				EmitByte(ModRM(0b11, GetRegCode(src.Register), GetRegCode(dst.Register)));
				break;

			case (RegOperand dst, MemOperand src):
				// MOV r64, [mem]
				EmitMovRegMem(dst.Register, src);
				break;

			case (MemOperand dst, RegOperand src):
				// MOV [mem], r64
				EmitMovMemReg(dst, src.Register);
				break;

			default:
				throw new NotSupportedException($"Unsupported MOV operands: {op.Dst}, {op.Src}");
		}
	}

	private void EmitMovRegMem(X86Register dst, MemOperand src) {
		// Simplified: assume [base + disp32]
		if (src.Base is RegOperand baseReg) {
			EmitRexW(dst, baseReg.Register);
			EmitByte(0x8B);
			EmitByte(ModRM(0b10, GetRegCode(dst), GetRegCode(baseReg.Register)));
			if (GetRegCode(baseReg.Register) == 4) EmitByte(0x24); // SIB for RSP
			EmitImm32(src.Displacement);
		}
	}

	private void EmitMovMemReg(MemOperand dst, X86Register src) {
		// Simplified: assume [base + disp32]
		if (dst.Base is RegOperand baseReg) {
			EmitRexW(src, baseReg.Register);
			EmitByte(0x89);
			EmitByte(ModRM(0b10, GetRegCode(src), GetRegCode(baseReg.Register)));
			if (GetRegCode(baseReg.Register) == 4) EmitByte(0x24);
			EmitImm32(dst.Displacement);
		}
	}

	private void EmitAdd(AddOp op) => EmitAluOp(op.Dst, op.Src, regOpcode: 0x01, immModRmReg: 0);

	private void EmitSub(SubOp op) => EmitAluOp(op.Dst, op.Src, regOpcode: 0x29, immModRmReg: 5);

	private void EmitAluOp(X86Operand dst, X86Operand src, byte regOpcode, byte immModRmReg) {
		if (dst is RegOperand dstReg && src is RegOperand srcReg) {
			EmitRexW(srcReg.Register, dstReg.Register);
			EmitByte(regOpcode);
			EmitByte(ModRM(0b11, GetRegCode(srcReg.Register), GetRegCode(dstReg.Register)));
		} else if (dst is RegOperand dstRegOp && src is ImmOperand imm) {
			EmitRexW(rm: dstRegOp.Register);
			EmitByte(0x81);
			EmitByte(ModRM(0b11, immModRmReg, GetRegCode(dstRegOp.Register)));
			EmitImm32((int)imm.Value);
		}
	}

	private void EmitImul(ImulOp op) {
		if (op.Src2 is not null) {
			// 3-operand form: IMUL dst, src1, src2
			// Implemented as: mov dst, src1; imul dst, src2
			if (op.Dst is RegOperand dst && op.Src1 is RegOperand src1 && op.Src2 is RegOperand src2) {
				// mov dst, src1
				EmitRexW(src1.Register, dst.Register);
				EmitByte(0x89);
				EmitByte(ModRM(0b11, GetRegCode(src1.Register), GetRegCode(dst.Register)));

				// imul dst, src2
				EmitRexW(dst.Register, src2.Register);
				EmitBytes(0x0F, 0xAF);
				EmitByte(ModRM(0b11, GetRegCode(dst.Register), GetRegCode(src2.Register)));
			} else if (op.Dst is RegOperand dst2 && op.Src1 is RegOperand src1b && op.Src2 is ImmOperand imm) {
				// IMUL r64, r/m64, imm32
				EmitRexW(dst2.Register, src1b.Register);
				EmitByte(0x69);
				EmitByte(ModRM(0b11, GetRegCode(dst2.Register), GetRegCode(src1b.Register)));
				EmitImm32((int)imm.Value);
			}
		} else {
			// 2-operand form: IMUL r64, r/m64
			if (op.Dst is RegOperand dst && op.Src1 is RegOperand src) {
				EmitRexW(dst.Register, src.Register);
				EmitBytes(0x0F, 0xAF);
				EmitByte(ModRM(0b11, GetRegCode(dst.Register), GetRegCode(src.Register)));
			}
		}
	}

	private void EmitIdiv(IdivOp op) {
		// IDIV r/m64
		if (op.Divisor is RegOperand src) {
			EmitRexW(rm: src.Register);
			EmitByte(0xF7);
			EmitByte(ModRM(0b11, 7, GetRegCode(src.Register)));
		}
	}

	private void EmitCmp(CmpOp op) => EmitRegRegOp(op.Left, op.Right, 0x39);

	private void EmitTest(TestOp op) => EmitRegRegOp(op.Left, op.Right, 0x85);

	private void EmitRegRegOp(X86Operand left, X86Operand right, byte opcode) {
		if (left is RegOperand leftReg && right is RegOperand rightReg) {
			EmitRexW(rightReg.Register, leftReg.Register);
			EmitByte(opcode);
			EmitByte(ModRM(0b11, GetRegCode(rightReg.Register), GetRegCode(leftReg.Register)));
		}
	}

	private void EmitSetcc(SetccOp op) {
		if (op.Dst is not RegOperand dst) return;
		EmitBytes(0x0F, GetSetccOpcode(op.Condition));
		EmitByte(ModRM(0b11, 0, GetRegCode(dst.Register)));
	}

	private void EmitJmp(JmpOp op) {
		EmitByte(0xE9);
		_labelFixups.Add((CurrentOffset, op.Target, 4));
		EmitImm32(0); // Placeholder
	}

	private void EmitJcc(JccOp op) {
		EmitBytes(0x0F, GetJccOpcode(op.Condition));
		_labelFixups.Add((CurrentOffset, op.TrueTarget, 4));
		EmitImm32(0);

		// Emit fall-through jump
		EmitByte(0xE9);
		_labelFixups.Add((CurrentOffset, op.FalseTarget, 4));
		EmitImm32(0);
	}

	private void EmitCall(X86CallOp op) {
		EmitByte(0xE8);
		_labelFixups.Add((CurrentOffset, op.Target, 4));
		EmitImm32(0);
	}

	private void EmitRet() {
		EmitByte(0xC3);
	}

	private void EmitPush(PushOp op) {
		if (op.Src is RegOperand reg) {
			if (NeedsRex(reg.Register)) EmitByte(0x41);
			EmitByte((byte)(0x50 + GetRegCode(reg.Register)));
		}
	}

	private void EmitPop(PopOp op) {
		if (op.Dst is RegOperand reg) {
			if (NeedsRex(reg.Register)) EmitByte(0x41);
			EmitByte((byte)(0x58 + GetRegCode(reg.Register)));
		}
	}

	private void EmitCdq() {
		EmitByte(0x99);
	}

	private void EmitLea(LeaOp op) {
		if (op.Dst is RegOperand dst && op.Src is MemOperand src && src.Base is RegOperand baseReg) {
			EmitRexW(dst.Register, baseReg.Register);
			EmitByte(0x8D);
			EmitByte(ModRM(0b10, GetRegCode(dst.Register), GetRegCode(baseReg.Register)));
			if (GetRegCode(baseReg.Register) == 4) EmitByte(0x24);
			EmitImm32(src.Displacement);
		}
	}

	private void EmitLeaGlobal(LeaGlobalOp op) {
		if (op.Dst is RegOperand dst) {
			// LEA with RIP-relative addressing: LEA reg, [RIP + disp32]
			// REX.W + 8D /r with ModR/M byte indicating RIP-relative (mod=00, rm=101)
			EmitRexW(dst.Register);
			EmitByte(0x8D);
			EmitByte(ModRM(0b00, GetRegCode(dst.Register), 0b101)); // RIP-relative

			// Record fixup for the displacement (will be resolved when we know data section offset)
			_globalFixups.Add((CurrentOffset, op.GlobalName, 4)); // 4 bytes for disp32
			EmitImm32(0); // Placeholder, will be fixed up later
		}
	}

	private void EmitShl(ShlOp op) {
		// SHL r/m64, CL (when count is in CL register)
		// SHL r/m64, imm8 (when count is immediate)
		if (op.Dst is RegOperand dst && op.Count is RegOperand count) {
			// Shift by CL: D3 /4 (SHL r/m64, CL)
			// First move count to CL if it's not already there
			if (count.Register != X86Register.RCX && count.Register != X86Register.CL) {
				// mov rcx, count
				EmitRexW(count.Register, X86Register.RCX);
				EmitByte(0x89);
				EmitByte(ModRM(0b11, GetRegCode(count.Register), GetRegCode(X86Register.RCX)));
			}
			// shl dst, cl
			EmitRexW(rm: dst.Register);
			EmitByte(0xD3);
			EmitByte(ModRM(0b11, 4, GetRegCode(dst.Register)));
		} else if (op.Dst is RegOperand dstReg && op.Count is ImmOperand imm) {
			// Shift by immediate: C1 /4 ib (SHL r/m64, imm8)
			EmitRexW(rm: dstReg.Register);
			EmitByte(0xC1);
			EmitByte(ModRM(0b11, 4, GetRegCode(dstReg.Register)));
			EmitByte((byte)imm.Value);
		} else {
			throw new NotSupportedException($"Unsupported SHL operands: {op.Dst}, {op.Count}");
		}
	}

	private void EmitShr(ShrOp op) {
		// SHR r/m64, CL (when count is in CL register)
		// SHR r/m64, imm8 (when count is immediate)
		if (op.Dst is RegOperand dst && op.Count is RegOperand count) {
			// Shift by CL: D3 /5 (SHR r/m64, CL)
			if (count.Register != X86Register.RCX && count.Register != X86Register.CL) {
				// mov rcx, count
				EmitRexW(count.Register, X86Register.RCX);
				EmitByte(0x89);
				EmitByte(ModRM(0b11, GetRegCode(count.Register), GetRegCode(X86Register.RCX)));
			}
			// shr dst, cl
			EmitRexW(rm: dst.Register);
			EmitByte(0xD3);
			EmitByte(ModRM(0b11, 5, GetRegCode(dst.Register)));
		} else if (op.Dst is RegOperand dstReg && op.Count is ImmOperand imm) {
			// Shift by immediate: C1 /5 ib (SHR r/m64, imm8)
			EmitRexW(rm: dstReg.Register);
			EmitByte(0xC1);
			EmitByte(ModRM(0b11, 5, GetRegCode(dstReg.Register)));
			EmitByte((byte)imm.Value);
		} else {
			throw new NotSupportedException($"Unsupported SHR operands: {op.Dst}, {op.Count}");
		}
	}

	private void EmitSar(SarOp op) {
		// SAR r/m64, CL (when count is in CL register)
		// SAR r/m64, imm8 (when count is immediate)
		if (op.Dst is RegOperand dst && op.Count is RegOperand count) {
			// Shift by CL: D3 /7 (SAR r/m64, CL)
			if (count.Register != X86Register.RCX && count.Register != X86Register.CL) {
				// mov rcx, count
				EmitRexW(count.Register, X86Register.RCX);
				EmitByte(0x89);
				EmitByte(ModRM(0b11, GetRegCode(count.Register), GetRegCode(X86Register.RCX)));
			}
			// sar dst, cl
			EmitRexW(rm: dst.Register);
			EmitByte(0xD3);
			EmitByte(ModRM(0b11, 7, GetRegCode(dst.Register)));
		} else if (op.Dst is RegOperand dstReg && op.Count is ImmOperand imm) {
			// Shift by immediate: C1 /7 ib (SAR r/m64, imm8)
			EmitRexW(rm: dstReg.Register);
			EmitByte(0xC1);
			EmitByte(ModRM(0b11, 7, GetRegCode(dstReg.Register)));
			EmitByte((byte)imm.Value);
		} else {
			throw new NotSupportedException($"Unsupported SAR operands: {op.Dst}, {op.Count}");
		}
	}

	private void EmitAnd(AndOp op) => EmitAluOp(op.Dst, op.Src, regOpcode: 0x21, immModRmReg: 4);

	private void EmitOr(OrOp op) => EmitAluOp(op.Dst, op.Src, regOpcode: 0x09, immModRmReg: 1);

	private void EmitXor(XorOp op) => EmitAluOp(op.Dst, op.Src, regOpcode: 0x31, immModRmReg: 6);

	private void EmitNot(NotOp op) {
		// NOT r/m64: REX.W F7 /2
		if (op.Dst is RegOperand dst) {
			EmitRexW(rm: dst.Register);
			EmitByte(0xF7);
			EmitByte(ModRM(0b11, 2, GetRegCode(dst.Register)));
		} else {
			throw new NotSupportedException($"Unsupported NOT operand: {op.Dst}");
		}
	}

	// SSE floating point
	private void EmitMovsd(MovsdOp op) {
		// F2 0F 10 /r - MOVSD xmm1, xmm2/m64 (load)
		// F2 0F 11 /r - MOVSD xmm1/m64, xmm2 (store)
		if (op.Dst is RegOperand dst && op.Src is RegOperand src) {
			// XMM to XMM move
			EmitBytes(0xF2, 0x0F, 0x10);
			EmitByte(ModRM(0b11, GetRegCode(dst.Register), GetRegCode(src.Register)));
		} else if (op.Dst is RegOperand dstReg && op.Src is MemOperand srcMem) {
			// Memory to XMM (load)
			EmitBytes(0xF2, 0x0F, 0x10);
			EmitMemOperand(dstReg.Register, srcMem);
		} else if (op.Dst is MemOperand dstMem && op.Src is RegOperand srcReg) {
			// XMM to memory (store)
			EmitBytes(0xF2, 0x0F, 0x11);
			EmitMemOperand(srcReg.Register, dstMem);
		} else {
			throw new NotSupportedException($"Unsupported MOVSD operand combination");
		}
	}

	private void EmitAddsd(AddsdOp op) {
		// F2 0F 58 /r - ADDSD xmm1, xmm2/m64
		EmitSseArith(0x58, op.Dst, op.Src);
	}

	private void EmitSubsd(SubsdOp op) {
		// F2 0F 5C /r - SUBSD xmm1, xmm2/m64
		EmitSseArith(0x5C, op.Dst, op.Src);
	}

	private void EmitMulsd(MulsdOp op) {
		// F2 0F 59 /r - MULSD xmm1, xmm2/m64
		EmitSseArith(0x59, op.Dst, op.Src);
	}

	private void EmitDivsd(DivsdOp op) {
		// F2 0F 5E /r - DIVSD xmm1, xmm2/m64
		EmitSseArith(0x5E, op.Dst, op.Src);
	}

	private void EmitCvttsd2si(CvttsdOp op) => EmitCvtOp(op.Dst, op.Src, 0x2C, "CVTTSD2SI");

	private void EmitCvtsi2sd(CvtsiOp op) => EmitCvtOp(op.Dst, op.Src, 0x2A, "CVTSI2SD");

	private void EmitCvtOp(X86Operand dst, X86Operand src, byte opcode, string name) {
		// F2 REX.W 0F xx /r - CVT* instructions
		if (dst is RegOperand dstReg && src is RegOperand srcReg) {
			EmitByte(0xF2);
			EmitRexW(srcReg.Register, dstReg.Register);
			EmitBytes(0x0F, opcode);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
		} else {
			throw new NotSupportedException($"Unsupported {name} operand combination");
		}
	}

	private void EmitMovq(MovqOp op) {
		// 66 REX.W 0F 6E /r - MOVQ xmm, r/m64
		// Move 64-bit value from GPR to XMM register
		if (op.Dst is RegOperand dstReg && op.Src is RegOperand srcReg) {
			EmitByte(0x66);
			EmitRexW(srcReg.Register, dstReg.Register);
			EmitBytes(0x0F, 0x6E);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
		} else {
			throw new NotSupportedException($"Unsupported MOVQ operand combination: {op.Dst?.GetType().Name} <- {op.Src?.GetType().Name}");
		}
	}

	private void EmitComisd(ComiOp op) {
		// 66 0F 2F /r - COMISD xmm1, xmm2/m64
		// Compares two doubles and sets EFLAGS (ZF, PF, CF)
		if (op.Left is RegOperand left && op.Right is RegOperand right) {
			EmitBytes(0x66, 0x0F, 0x2F);
			EmitByte(ModRM(0b11, GetRegCode(left.Register), GetRegCode(right.Register)));
		} else if (op.Left is RegOperand leftReg && op.Right is MemOperand rightMem) {
			EmitBytes(0x66, 0x0F, 0x2F);
			EmitMemOperand(leftReg.Register, rightMem);
		} else {
			throw new NotSupportedException($"Unsupported COMISD operand combination: {op.Left}, {op.Right}");
		}
	}

	private void EmitSqrtsd(SqrtsdOp op) {
		// F2 0F 51 /r - SQRTSD xmm1, xmm2/m64
		EmitSseArith(0x51, op.Dst, op.Src);
	}

	private void EmitRoundsd(RoundsdOp op) {
		// 66 0F 3A 0B /r imm8 - ROUNDSD xmm1, xmm2/m64, imm8
		// imm8: 0x08 = nearest, 0x09 = floor, 0x0A = ceil, 0x0B = truncate
		if (op.Dst is RegOperand dstReg && op.Src is RegOperand srcReg) {
			EmitBytes(0x66, 0x0F, 0x3A, 0x0B);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
			EmitByte((byte)op.Mode);
		} else if (op.Dst is RegOperand dstRegOp && op.Src is MemOperand srcMem) {
			EmitBytes(0x66, 0x0F, 0x3A, 0x0B);
			EmitMemOperand(dstRegOp.Register, srcMem);
			EmitByte((byte)op.Mode);
		} else {
			throw new NotSupportedException($"Unsupported ROUNDSD operand combination");
		}
	}

	private void EmitMinsd(MinsdOp op) {
		// F2 0F 5D /r - MINSD xmm1, xmm2/m64
		EmitSseArith(0x5D, op.Dst, op.Src);
	}

	private void EmitMaxsd(MaxsdOp op) {
		// F2 0F 5F /r - MAXSD xmm1, xmm2/m64
		EmitSseArith(0x5F, op.Dst, op.Src);
	}

	private void EmitAndpd(AndpdOp op) {
		// 66 0F 54 /r - ANDPD xmm1, xmm2/m128
		// Bitwise AND of packed doubles
		if (op.Dst is RegOperand dstReg && op.Src is RegOperand srcReg) {
			EmitBytes(0x66, 0x0F, 0x54);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
		} else if (op.Dst is RegOperand dstRegOp && op.Src is MemOperand srcMem) {
			EmitBytes(0x66, 0x0F, 0x54);
			EmitMemOperand(dstRegOp.Register, srcMem);
		} else {
			throw new NotSupportedException($"Unsupported ANDPD operand combination");
		}
	}

	private void EmitSseArith(byte opcode, X86Operand dst, X86Operand src) {
		EmitBytes(0xF2, 0x0F, opcode);
		if (dst is RegOperand dstReg && src is RegOperand srcReg) {
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
		} else if (dst is RegOperand dstRegOp && src is MemOperand srcMem) {
			EmitMemOperand(dstRegOp.Register, srcMem);
		} else {
			throw new NotSupportedException($"Unsupported SSE operand combination");
		}
	}

	private void EmitMemOperand(X86Register reg, MemOperand mem) {
		if (mem.Base is RegOperand baseReg) {
			var baseCode = GetRegCode(baseReg.Register);
			if (mem.Displacement == 0 && baseCode != 5) {
				// [base]
				EmitByte(ModRM(0b00, GetRegCode(reg), baseCode));
				if (baseCode == 4) EmitByte(0x24); // SIB for RSP
			} else if (mem.Displacement >= -128 && mem.Displacement <= 127) {
				// [base + disp8]
				EmitByte(ModRM(0b01, GetRegCode(reg), baseCode));
				if (baseCode == 4) EmitByte(0x24);
				EmitByte((byte)mem.Displacement);
			} else {
				// [base + disp32]
				EmitByte(ModRM(0b10, GetRegCode(reg), baseCode));
				if (baseCode == 4) EmitByte(0x24);
				EmitImm32(mem.Displacement);
			}
		} else {
			throw new NotSupportedException("Memory operand must have a base register");
		}
	}
}
