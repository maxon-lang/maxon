namespace MaxonSharp.Compiler;

public class X86Encoder {
	private readonly List<byte> _code = [];
	private readonly Dictionary<string, int> _labelOffsets = [];
	private readonly List<(int Offset, string Label, bool IsRelative)> _labelReferences = [];

	public byte[] GetCode() {
		// Resolve label references
		var code = _code.ToArray();
		foreach (var (offset, label, isRelative) in _labelReferences) {
			if (!_labelOffsets.TryGetValue(label, out var targetOffset)) {
				throw new Exception($"Undefined label: {label}");
			}

			if (isRelative) {
				// Relative offset from end of instruction (assumes 4-byte displacement)
				var rel = targetOffset - (offset + 4);
				code[offset] = (byte)(rel & 0xFF);
				code[offset + 1] = (byte)((rel >> 8) & 0xFF);
				code[offset + 2] = (byte)((rel >> 16) & 0xFF);
				code[offset + 3] = (byte)((rel >> 24) & 0xFF);
			} else {
				// Absolute offset
				code[offset] = (byte)(targetOffset & 0xFF);
				code[offset + 1] = (byte)((targetOffset >> 8) & 0xFF);
				code[offset + 2] = (byte)((targetOffset >> 16) & 0xFF);
				code[offset + 3] = (byte)((targetOffset >> 24) & 0xFF);
			}
		}
		return code;
	}

	public int CurrentOffset => _code.Count;

	// ==========================================================================
	// Labels
	// ==========================================================================

	public void DefineLabel(string name) {
		_labelOffsets[name] = _code.Count;
	}

	// ==========================================================================
	// Stack operations
	// ==========================================================================

	// push reg64
	public void Push(Reg reg) {
		if ((int)reg >= 8) {
			_code.Add(0x41); // REX.B
			_code.Add((byte)(0x50 + ((int)reg - 8)));
		} else {
			_code.Add((byte)(0x50 + (int)reg));
		}
	}

	// pop reg64
	public void Pop(Reg reg) {
		if ((int)reg >= 8) {
			_code.Add(0x41); // REX.B
			_code.Add((byte)(0x58 + ((int)reg - 8)));
		} else {
			_code.Add((byte)(0x58 + (int)reg));
		}
	}

	// sub rsp, imm8
	public void SubRspImm8(byte value) {
		_code.Add(0x48); // REX.W
		_code.Add(0x83); // SUB r/m64, imm8
		_code.Add(ModRM(3, 5, 4)); // /5 = SUB, RSP
		_code.Add(value);
	}

	// add rsp, imm8
	public void AddRspImm8(byte value) {
		_code.Add(0x48); // REX.W
		_code.Add(0x83); // ADD r/m64, imm8
		_code.Add(ModRM(3, 0, 4)); // /0 = ADD, RSP
		_code.Add(value);
	}

	// sub rsp, imm32
	public void SubRspImm32(int value) {
		_code.Add(0x48); // REX.W
		_code.Add(0x81); // SUB r/m64, imm32
		_code.Add(ModRM(3, 5, 4)); // /5 = SUB, RSP
		EmitInt32(value);
	}

	// add rsp, imm32
	public void AddRspImm32(int value) {
		_code.Add(0x48); // REX.W
		_code.Add(0x81); // ADD r/m64, imm32
		_code.Add(ModRM(3, 0, 4)); // /0 = ADD, RSP
		EmitInt32(value);
	}

	// ==========================================================================
	// Data movement
	// ==========================================================================

	// mov reg64, reg64
	public void MovRegReg(Reg dest, Reg src) {
		byte rex = 0x48; // REX.W
		if ((int)src >= 8) rex |= 0x04; // REX.R
		if ((int)dest >= 8) rex |= 0x01; // REX.B

		_code.Add(rex);
		_code.Add(0x89); // MOV r/m64, r64
		_code.Add(ModRM(3, (int)src & 7, (int)dest & 7));
	}

	// mov reg64, imm64 (or smaller if fits)
	public void MovRegImm(Reg dest, long value) {
		// For small values, use mov eax, imm32 (zero-extended)
		if (value >= 0 && value <= uint.MaxValue) {
			if ((int)dest >= 8) {
				_code.Add(0x41); // REX.B
			}
			_code.Add((byte)(0xB8 + ((int)dest & 7))); // MOV r32, imm32
			EmitInt32((int)value);
		} else {
			// Full 64-bit move
			byte rex = 0x48; // REX.W
			if ((int)dest >= 8) rex |= 0x01; // REX.B

			_code.Add(rex);
			_code.Add((byte)(0xB8 + ((int)dest & 7))); // MOV r64, imm64
			EmitInt64(value);
		}
	}

	// mov [reg+offset], reg64
	public void MovMemReg(Reg baseReg, int offset, Reg src) {
		byte rex = 0x48; // REX.W
		if ((int)src >= 8) rex |= 0x04; // REX.R
		if ((int)baseReg >= 8) rex |= 0x01; // REX.B

		_code.Add(rex);
		_code.Add(0x89); // MOV r/m64, r64
		EmitMemOperand((int)src & 7, baseReg, offset);
	}

	// mov [reg+offset], reg8 (byte store)
	public void MovMemReg8(Reg baseReg, int offset, Reg src) {
		// REX prefix needed for extended registers or to access SPL, BPL, SIL, DIL
		byte rex = 0x40;
		if ((int)src >= 8) rex |= 0x04; // REX.R
		if ((int)baseReg >= 8) rex |= 0x01; // REX.B
																				// Always emit REX for consistent encoding with 64-bit mode
		_code.Add(rex);
		_code.Add(0x88); // MOV r/m8, r8
		EmitMemOperand((int)src & 7, baseReg, offset);
	}

	// Sized memory store: writes 1, 2, 4, or 8 bytes depending on size
	public void MovMemRegSized(Reg baseReg, int offset, Reg src, int size) {
		switch (size) {
			case 1:
				MovMemReg8(baseReg, offset, src);
				break;
			case 2:
				// 16-bit store: operand size prefix
				_code.Add(0x66);
				goto case 4; // Fall through to 32-bit encoding without REX.W
			case 4:
				// 32-bit store (no REX.W)
				if ((int)src >= 8 || (int)baseReg >= 8) {
					byte rex = 0x40;
					if ((int)src >= 8) rex |= 0x04;
					if ((int)baseReg >= 8) rex |= 0x01;
					_code.Add(rex);
				}
				_code.Add(0x89); // MOV r/m32, r32
				EmitMemOperand((int)src & 7, baseReg, offset);
				break;
			default: // 8 bytes
				MovMemReg(baseReg, offset, src);
				break;
		}
	}

	// mov reg64, [reg+offset]
	public void MovRegMem(Reg dest, Reg baseReg, int offset) {
		byte rex = 0x48; // REX.W
		if ((int)dest >= 8) rex |= 0x04; // REX.R
		if ((int)baseReg >= 8) rex |= 0x01; // REX.B

		_code.Add(rex);
		_code.Add(0x8B); // MOV r64, r/m64
		EmitMemOperand((int)dest & 7, baseReg, offset);
	}

	// movzx reg64, byte [reg+offset] - zero-extend 1 byte to 64 bits
	public void MovzxReg64Mem8(Reg dest, Reg baseReg, int offset) {
		byte rex = 0x48; // REX.W for 64-bit destination
		if ((int)dest >= 8) rex |= 0x04; // REX.R
		if ((int)baseReg >= 8) rex |= 0x01; // REX.B

		_code.Add(rex);
		_code.Add(0x0F);
		_code.Add(0xB6); // MOVZX r64, r/m8
		EmitMemOperand((int)dest & 7, baseReg, offset);
	}

	// movzx reg64, word [reg+offset] - zero-extend 2 bytes to 64 bits
	public void MovzxReg64Mem16(Reg dest, Reg baseReg, int offset) {
		byte rex = 0x48; // REX.W for 64-bit destination
		if ((int)dest >= 8) rex |= 0x04; // REX.R
		if ((int)baseReg >= 8) rex |= 0x01; // REX.B

		_code.Add(rex);
		_code.Add(0x0F);
		_code.Add(0xB7); // MOVZX r64, r/m16
		EmitMemOperand((int)dest & 7, baseReg, offset);
	}

	// mov reg32, [reg+offset] - load 32 bits (implicitly zero-extends to 64)
	public void MovReg32Mem32(Reg dest, Reg baseReg, int offset) {
		// No REX.W for 32-bit operand, but may need REX for extended registers
		if ((int)dest >= 8 || (int)baseReg >= 8) {
			byte rex = 0x40;
			if ((int)dest >= 8) rex |= 0x04; // REX.R
			if ((int)baseReg >= 8) rex |= 0x01; // REX.B
			_code.Add(rex);
		}
		_code.Add(0x8B); // MOV r32, r/m32
		EmitMemOperand((int)dest & 7, baseReg, offset);
	}

	// lea reg64, [reg+offset]
	public void LeaRegMem(Reg dest, Reg baseReg, int offset) {
		byte rex = 0x48; // REX.W
		if ((int)dest >= 8) rex |= 0x04; // REX.R
		if ((int)baseReg >= 8) rex |= 0x01; // REX.B

		_code.Add(rex);
		_code.Add(0x8D); // LEA r64, m
		EmitMemOperand((int)dest & 7, baseReg, offset);
	}

	// ==========================================================================
	// Integer arithmetic
	// ==========================================================================

	// add reg64, reg64
	public void AddRegReg(Reg dest, Reg src) {
		byte rex = 0x48;
		if ((int)src >= 8) rex |= 0x04;
		if ((int)dest >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0x01); // ADD r/m64, r64
		_code.Add(ModRM(3, (int)src & 7, (int)dest & 7));
	}

	// add reg64, imm32
	public void AddRegImm(Reg dest, int value) {
		byte rex = 0x48;
		if ((int)dest >= 8) rex |= 0x01;

		_code.Add(rex);
		if (value >= -128 && value <= 127) {
			_code.Add(0x83); // ADD r/m64, imm8
			_code.Add(ModRM(3, 0, (int)dest & 7));
			_code.Add((byte)value);
		} else {
			_code.Add(0x81); // ADD r/m64, imm32
			_code.Add(ModRM(3, 0, (int)dest & 7));
			EmitInt32(value);
		}
	}

	// sub reg64, reg64
	public void SubRegReg(Reg dest, Reg src) {
		byte rex = 0x48;
		if ((int)src >= 8) rex |= 0x04;
		if ((int)dest >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0x29); // SUB r/m64, r64
		_code.Add(ModRM(3, (int)src & 7, (int)dest & 7));
	}

	// sub reg64, imm32
	public void SubRegImm(Reg dest, int value) {
		byte rex = 0x48;
		if ((int)dest >= 8) rex |= 0x01;

		_code.Add(rex);
		if (value >= -128 && value <= 127) {
			_code.Add(0x83); // SUB r/m64, imm8
			_code.Add(ModRM(3, 5, (int)dest & 7));
			_code.Add((byte)value);
		} else {
			_code.Add(0x81); // SUB r/m64, imm32
			_code.Add(ModRM(3, 5, (int)dest & 7));
			EmitInt32(value);
		}
	}

	// imul reg64, reg64
	public void IMulRegReg(Reg dest, Reg src) {
		byte rex = 0x48;
		if ((int)dest >= 8) rex |= 0x04;
		if ((int)src >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0x0F);
		_code.Add(0xAF); // IMUL r64, r/m64
		_code.Add(ModRM(3, (int)dest & 7, (int)src & 7));
	}

	// imul reg64, reg64, imm32
	public void IMulRegRegImm(Reg dest, Reg src, int value) {
		byte rex = 0x48;
		if ((int)dest >= 8) rex |= 0x04;
		if ((int)src >= 8) rex |= 0x01;

		_code.Add(rex);
		if (value >= -128 && value <= 127) {
			_code.Add(0x6B); // IMUL r64, r/m64, imm8
			_code.Add(ModRM(3, (int)dest & 7, (int)src & 7));
			_code.Add((byte)value);
		} else {
			_code.Add(0x69); // IMUL r64, r/m64, imm32
			_code.Add(ModRM(3, (int)dest & 7, (int)src & 7));
			EmitInt32(value);
		}
	}

	// neg reg64
	public void NegReg(Reg reg) {
		byte rex = 0x48;
		if ((int)reg >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0xF7); // NEG r/m64
		_code.Add(ModRM(3, 3, (int)reg & 7));
	}

	// cqo (sign-extend RAX into RDX:RAX for division)
	public void Cqo() {
		_code.Add(0x48); // REX.W
		_code.Add(0x99); // CQO
	}

	// idiv reg64 (RDX:RAX / reg -> RAX, remainder in RDX)
	public void IDivReg(Reg reg) {
		byte rex = 0x48;
		if ((int)reg >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0xF7); // IDIV r/m64
		_code.Add(ModRM(3, 7, (int)reg & 7));
	}

	// ==========================================================================
	// Bitwise operations
	// ==========================================================================

	// and reg64, reg64
	public void AndRegReg(Reg dest, Reg src) {
		byte rex = 0x48;
		if ((int)src >= 8) rex |= 0x04;
		if ((int)dest >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0x21); // AND r/m64, r64
		_code.Add(ModRM(3, (int)src & 7, (int)dest & 7));
	}

	// or reg64, reg64
	public void OrRegReg(Reg dest, Reg src) {
		byte rex = 0x48;
		if ((int)src >= 8) rex |= 0x04;
		if ((int)dest >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0x09); // OR r/m64, r64
		_code.Add(ModRM(3, (int)src & 7, (int)dest & 7));
	}

	// xor reg64, reg64
	public void XorRegReg(Reg dest, Reg src) {
		byte rex = 0x48;
		if ((int)src >= 8) rex |= 0x04;
		if ((int)dest >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0x31); // XOR r/m64, r64
		_code.Add(ModRM(3, (int)src & 7, (int)dest & 7));
	}

	// not reg64
	public void NotReg(Reg reg) {
		byte rex = 0x48;
		if ((int)reg >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0xF7); // NOT r/m64
		_code.Add(ModRM(3, 2, (int)reg & 7));
	}

	// shl reg64, cl
	public void ShlRegCl(Reg reg) {
		byte rex = 0x48;
		if ((int)reg >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0xD3); // SHL r/m64, CL
		_code.Add(ModRM(3, 4, (int)reg & 7));
	}

	// shl reg64, imm8
	public void ShlRegImm(Reg reg, byte count) {
		byte rex = 0x48;
		if ((int)reg >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0xC1); // SHL r/m64, imm8
		_code.Add(ModRM(3, 4, (int)reg & 7));
		_code.Add(count);
	}

	// sar reg64, cl (arithmetic shift right)
	public void SarRegCl(Reg reg) {
		byte rex = 0x48;
		if ((int)reg >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0xD3); // SAR r/m64, CL
		_code.Add(ModRM(3, 7, (int)reg & 7));
	}

	// sar reg64, imm8
	public void SarRegImm(Reg reg, byte count) {
		byte rex = 0x48;
		if ((int)reg >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0xC1); // SAR r/m64, imm8
		_code.Add(ModRM(3, 7, (int)reg & 7));
		_code.Add(count);
	}

	// ==========================================================================
	// Comparisons
	// ==========================================================================

	// cmp reg64, reg64
	public void CmpRegReg(Reg left, Reg right) {
		byte rex = 0x48;
		if ((int)right >= 8) rex |= 0x04;
		if ((int)left >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0x39); // CMP r/m64, r64
		_code.Add(ModRM(3, (int)right & 7, (int)left & 7));
	}

	// test reg64, reg64 (AND without storing result, sets flags)
	public void TestRegReg(Reg left, Reg right) {
		byte rex = 0x48;
		if ((int)right >= 8) rex |= 0x04;
		if ((int)left >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0x85); // TEST r/m64, r64
		_code.Add(ModRM(3, (int)right & 7, (int)left & 7));
	}

	// ud2 - undefined instruction, causes #UD exception (for controlled crash)
	public void Ud2() {
		_code.Add(0x0F);
		_code.Add(0x0B);
	}

	// cmp reg64, imm32
	public void CmpRegImm(Reg reg, int value) {
		byte rex = 0x48;
		if ((int)reg >= 8) rex |= 0x01;

		_code.Add(rex);
		if (value >= -128 && value <= 127) {
			_code.Add(0x83); // CMP r/m64, imm8
			_code.Add(ModRM(3, 7, (int)reg & 7));
			_code.Add((byte)value);
		} else {
			_code.Add(0x81); // CMP r/m64, imm32
			_code.Add(ModRM(3, 7, (int)reg & 7));
			EmitInt32(value);
		}
	}

	// setcc reg8 (set byte based on condition)
	public void SetCC(CondCode cond, Reg dest) {
		if ((int)dest >= 8) {
			_code.Add(0x41); // REX.B
		}
		_code.Add(0x0F);
		_code.Add((byte)(0x90 + (int)cond)); // SETcc r/m8
		_code.Add(ModRM(3, 0, (int)dest & 7));
	}

	// movzx reg64, reg8 (zero-extend byte to qword)
	public void MovzxRegReg8(Reg dest, Reg src) {
		byte rex = 0x48;
		if ((int)dest >= 8) rex |= 0x04;
		if ((int)src >= 8) rex |= 0x01;

		_code.Add(rex);
		_code.Add(0x0F);
		_code.Add(0xB6); // MOVZX r64, r/m8
		_code.Add(ModRM(3, (int)dest & 7, (int)src & 7));
	}

	// ==========================================================================
	// Control flow
	// ==========================================================================

	// ret
	public void Ret() {
		_code.Add(0xC3);
	}

	// jmp rel32
	public void JmpRel32(string label) {
		_code.Add(0xE9); // JMP rel32
		_labelReferences.Add((_code.Count, label, true));
		EmitInt32(0); // Placeholder
	}

	// jmp short rel8
	public void JmpRel8(sbyte offset) {
		_code.Add(0xEB);
		_code.Add((byte)offset);
	}

	// jcc rel32 (conditional jump)
	public void JccRel32(CondCode cond, string label) {
		_code.Add(0x0F);
		_code.Add((byte)(0x80 + (int)cond)); // Jcc rel32
		_labelReferences.Add((_code.Count, label, true));
		EmitInt32(0); // Placeholder
	}

	// call rel32
	public void CallRel32(string label) {
		_code.Add(0xE8); // CALL rel32
		_labelReferences.Add((_code.Count, label, true));
		EmitInt32(0); // Placeholder
	}

	// call reg64
	public void CallReg(Reg reg) {
		if ((int)reg >= 8) {
			_code.Add(0x41); // REX.B
		}
		_code.Add(0xFF);
		_code.Add(ModRM(3, 2, (int)reg & 7));
	}

	// ==========================================================================
	// Floating point (SSE/SSE2)
	// ==========================================================================

	// movsd xmm, xmm
	public void MovsdXmmXmm(int destXmm, int srcXmm) {
		_code.Add(0xF2);
		EmitXmmPrefix(destXmm, srcXmm);
		_code.Add(0x0F);
		_code.Add(0x10); // MOVSD xmm, xmm/m64
		_code.Add(ModRM(3, destXmm & 7, srcXmm & 7));
	}

	// movsd xmm, [reg+offset]
	public void MovsdXmmMem(int destXmm, Reg baseReg, int offset) {
		_code.Add(0xF2);
		byte rex = 0x40; // REX prefix base
		if (destXmm >= 8) rex |= 0x04; // REX.R - extends reg field
		if ((int)baseReg >= 8) rex |= 0x01; // REX.B - extends r/m field
		if (rex != 0x40) _code.Add(rex); // Only emit if needed
		_code.Add(0x0F);
		_code.Add(0x10); // MOVSD xmm, m64
		EmitMemOperand(destXmm & 7, baseReg, offset);
	}

	// movsd [reg+offset], xmm
	public void MovsdMemXmm(Reg baseReg, int offset, int srcXmm) {
		_code.Add(0xF2);
		byte rex = 0x40; // REX prefix base
		if (srcXmm >= 8) rex |= 0x04; // REX.R - extends reg field
		if ((int)baseReg >= 8) rex |= 0x01; // REX.B - extends r/m field
		if (rex != 0x40) _code.Add(rex); // Only emit if needed
		_code.Add(0x0F);
		_code.Add(0x11); // MOVSD m64, xmm
		EmitMemOperand(srcXmm & 7, baseReg, offset);
	}

	// addsd xmm, xmm
	public void AddsdXmmXmm(int destXmm, int srcXmm) {
		_code.Add(0xF2);
		EmitXmmPrefix(destXmm, srcXmm);
		_code.Add(0x0F);
		_code.Add(0x58); // ADDSD
		_code.Add(ModRM(3, destXmm & 7, srcXmm & 7));
	}

	// subsd xmm, xmm
	public void SubsdXmmXmm(int destXmm, int srcXmm) {
		_code.Add(0xF2);
		EmitXmmPrefix(destXmm, srcXmm);
		_code.Add(0x0F);
		_code.Add(0x5C); // SUBSD
		_code.Add(ModRM(3, destXmm & 7, srcXmm & 7));
	}

	// mulsd xmm, xmm
	public void MulsdXmmXmm(int destXmm, int srcXmm) {
		_code.Add(0xF2);
		EmitXmmPrefix(destXmm, srcXmm);
		_code.Add(0x0F);
		_code.Add(0x59); // MULSD
		_code.Add(ModRM(3, destXmm & 7, srcXmm & 7));
	}

	// divsd xmm, xmm
	public void DivsdXmmXmm(int destXmm, int srcXmm) {
		_code.Add(0xF2);
		EmitXmmPrefix(destXmm, srcXmm);
		_code.Add(0x0F);
		_code.Add(0x5E); // DIVSD
		_code.Add(ModRM(3, destXmm & 7, srcXmm & 7));
	}

	// cvtsi2sd xmm, reg64 (int to double)
	public void Cvtsi2sdXmmReg(int destXmm, Reg src) {
		_code.Add(0xF2);
		byte rex = 0x48;
		if (destXmm >= 8) rex |= 0x04;
		if ((int)src >= 8) rex |= 0x01;
		_code.Add(rex);
		_code.Add(0x0F);
		_code.Add(0x2A); // CVTSI2SD
		_code.Add(ModRM(3, destXmm & 7, (int)src & 7));
	}

	// cvttsd2si reg64, xmm (double to int, truncated)
	public void Cvttsd2siRegXmm(Reg dest, int srcXmm) {
		_code.Add(0xF2);
		byte rex = 0x48;
		if ((int)dest >= 8) rex |= 0x04;
		if (srcXmm >= 8) rex |= 0x01;
		_code.Add(rex);
		_code.Add(0x0F);
		_code.Add(0x2C); // CVTTSD2SI
		_code.Add(ModRM(3, (int)dest & 7, srcXmm & 7));
	}

	// ucomisd xmm, xmm (compare floats)
	public void UcomisdXmmXmm(int leftXmm, int rightXmm) {
		_code.Add(0x66);
		EmitXmmPrefix(leftXmm, rightXmm);
		_code.Add(0x0F);
		_code.Add(0x2E); // UCOMISD
		_code.Add(ModRM(3, leftXmm & 7, rightXmm & 7));
	}

	// ==========================================================================
	// Helpers
	// ==========================================================================

	private static byte ModRM(int mod, int reg, int rm) {
		return (byte)((mod << 6) | ((reg & 7) << 3) | (rm & 7));
	}

	private void EmitMemOperand(int reg, Reg baseReg, int offset) {
		var b = (int)baseReg & 7;

		if (baseReg == Reg.Rsp || baseReg == Reg.R12) {
			// Need SIB byte for RSP-based addressing
			if (offset == 0) {
				_code.Add(ModRM(0, reg, 4)); // mod=0, rm=4 (SIB)
				_code.Add(0x24); // SIB: scale=0, index=4 (none), base=4 (RSP)
			} else if (offset >= -128 && offset <= 127) {
				_code.Add(ModRM(1, reg, 4)); // mod=1, rm=4 (SIB)
				_code.Add(0x24); // SIB
				_code.Add((byte)offset);
			} else {
				_code.Add(ModRM(2, reg, 4)); // mod=2, rm=4 (SIB)
				_code.Add(0x24); // SIB
				EmitInt32(offset);
			}
		} else if (baseReg == Reg.Rbp || baseReg == Reg.R13) {
			// RBP/R13 always need displacement
			if (offset >= -128 && offset <= 127) {
				_code.Add(ModRM(1, reg, b));
				_code.Add((byte)offset);
			} else {
				_code.Add(ModRM(2, reg, b));
				EmitInt32(offset);
			}
		} else {
			if (offset == 0) {
				_code.Add(ModRM(0, reg, b));
			} else if (offset >= -128 && offset <= 127) {
				_code.Add(ModRM(1, reg, b));
				_code.Add((byte)offset);
			} else {
				_code.Add(ModRM(2, reg, b));
				EmitInt32(offset);
			}
		}
	}

	private void EmitXmmPrefix(int xmm1, int xmm2) {
		byte rex = 0;
		if (xmm1 >= 8) rex |= 0x04;
		if (xmm2 >= 8) rex |= 0x01;
		if (rex != 0) _code.Add((byte)(0x40 | rex));
	}

	private void EmitInt32(int value) {
		_code.Add((byte)(value & 0xFF));
		_code.Add((byte)((value >> 8) & 0xFF));
		_code.Add((byte)((value >> 16) & 0xFF));
		_code.Add((byte)((value >> 24) & 0xFF));
	}

	private void EmitInt64(long value) {
		EmitInt32((int)(value & 0xFFFFFFFF));
		EmitInt32((int)((value >> 32) & 0xFFFFFFFF));
	}
}

// Condition codes for setcc and jcc instructions
public enum CondCode {
	O = 0,    // Overflow
	No = 1,   // Not overflow
	B = 2,    // Below (unsigned <)
	Ae = 3,   // Above or equal (unsigned >=)
	E = 4,    // Equal
	Ne = 5,   // Not equal
	Be = 6,   // Below or equal (unsigned <=)
	A = 7,    // Above (unsigned >)
	S = 8,    // Sign (negative)
	Ns = 9,   // Not sign (positive)
	P = 10,   // Parity even
	Np = 11,  // Parity odd
	L = 12,   // Less than (signed <)
	Ge = 13,  // Greater or equal (signed >=)
	Le = 14,  // Less or equal (signed <=)
	G = 15    // Greater than (signed >)
}

