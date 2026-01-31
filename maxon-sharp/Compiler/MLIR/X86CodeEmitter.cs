using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public record ImportEntry(string DllName, string FunctionName);

public class X86CodeEmitter {
	private readonly List<byte> _code = [];
	private readonly List<byte> _rdata = [];
	private readonly List<byte> _data = [];
	private readonly List<ImportEntry> _imports = [];

	// Label name -> code offset
	private readonly Dictionary<string, int> _labels = [];

	// Fixups: code offset -> target label name (for relative call/jmp)
	private readonly List<(int offset, string target)> _relCallFixups = [];

	// Jump fixups: code offset -> target label name (for Jcc and Jmp)
	private readonly List<(int offset, string target)> _jumpFixups = [];

	// Import fixups: code offset -> import index (for indirect calls through IAT)
	private readonly List<(int offset, int importIndex)> _importCallFixups = [];

	// Rdata fixups: code offset -> rdata label
	private readonly List<(int offset, string label)> _rdataFixups = [];

	// Rdata labels: label -> offset within rdata section
	private readonly Dictionary<string, int> _rdataLabels = [];

	// Global fixups: code offset -> global name
	private readonly List<(int offset, string name)> _globalFixups = [];

	// Global names: name -> offset within data section
	private readonly Dictionary<string, int> _globalLabels = [];

	// Chkstk call sites for patching
	private readonly List<int> _chkstkCallSites = [];

	private string _currentFunction = "";

	public IReadOnlyList<ImportEntry> Imports => _imports;
	public bool HasRdata => _rdata.Count > 0;
	public bool HasGlobals => _data.Count > 0;
	public bool HasImports => _imports.Count > 0;

	// --- Label management ---

	public void DefineLabel(string name) {
		_labels[name] = _code.Count;
	}

	public void SetCurrentFunction(string name) {
		_currentFunction = name;
	}

	public string ScopedLabel(string blockName) {
		return $"{_currentFunction}.{blockName}";
	}

	// --- Data section management ---

	public void DefineRdata(string label, byte[] bytes, int alignment = 1) {
		if (alignment > 1) {
			var padding = (alignment - (_rdata.Count % alignment)) % alignment;
			for (var i = 0; i < padding; i++) _rdata.Add(0);
		}
		_rdataLabels[label] = _rdata.Count;
		_rdata.AddRange(bytes);
	}

	public void DefineGlobal(string name, int size, long initValue) {
		_globalLabels[name] = _data.Count;
		var bytes = new byte[size];
		if (initValue != 0) {
			var valueBytes = BitConverter.GetBytes(initValue);
			Array.Copy(valueBytes, bytes, Math.Min(valueBytes.Length, size));
		}
		_data.AddRange(bytes);
	}

	// --- Emit X86 operations ---

	public void Emit(X86Op op) {
		switch (op) {
			case X86PrologueOp prologue:
				EmitPushReg(X86Register.Rbp);
				EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
				if (prologue.StackSize > 0)
					EmitSubRegImm(X86Register.Rsp, prologue.StackSize);
				break;
			case X86EpilogueOp:
				EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
				EmitPopReg(X86Register.Rbp);
				break;
			case X86PushRegOp push:
				EmitPushReg(push.Register);
				break;
			case X86PopRegOp pop:
				EmitPopReg(pop.Register);
				break;
			case X86MovRegRegOp mov:
				EmitMovRegReg(mov.Dest, mov.Src);
				break;
			case X86XchgRegRegOp xchg:
				EmitXchgRegReg(xchg.A, xchg.B);
				break;
			case X86MovRegImmOp mov:
				EmitMovRegImm(mov.Dest, mov.Immediate);
				break;
			case X86RetOp:
				EmitByte(0xC3); // ret
				break;
			case X86SubRegImmOp sub:
				EmitSubRegImm(sub.Dest, sub.Immediate);
				break;
			case X86AddRegImmOp add:
				EmitAddRegImm(add.Dest, add.Immediate);
				break;
			case X86AddRegRegOp addReg:
				EmitAddRegReg(addReg.Dest, addReg.Src);
				break;
			case X86SubRegRegOp subReg:
				EmitSubRegReg(subReg.Dest, subReg.Src);
				break;
			case X86XorRegRegOp xor:
				EmitXorRegReg(xor.Dest, xor.Src);
				break;
			case X86ImulRegRegOp imul:
				EmitImulRegReg(imul.Dest, imul.Src);
				break;
			case X86CallDirectOp call:
				EmitByte(0xE8); // call rel32
				_relCallFixups.Add((_code.Count, call.Target));
				EmitDword(0); // placeholder, patched by ResolveLabels
				break;
			case X86MovMemRegOp movMem:
				EmitMovMemReg(movMem.Displacement, movMem.Src);
				break;
			case X86MovMemRspRegOp movMemRsp:
				EmitMovMemRspReg(movMemRsp.Offset, movMemRsp.Src);
				break;
			case X86MovRegMemOp movReg:
				EmitMovRegMem(movReg.Dest, movReg.Displacement);
				break;
			case X86MovSdXmmRipRelOp movsd:
				EmitMovSdXmmRipRel(movsd.Dest, movsd.RdataLabel);
				break;
			case X86MovSdMemXmmOp movsd:
				EmitMovSdMemXmm(movsd.Displacement, movsd.Src);
				break;
			case X86MovSdXmmMemOp movsd:
				EmitMovSdXmmMem(movsd.Dest, movsd.Displacement);
				break;
			case X86CmpRegRegOp cmp:
				EmitCmpRegReg(cmp.Lhs, cmp.Rhs);
				break;
			case X86AddSdOp addsd:
				EmitAddSd(addsd.Dest, addsd.Src);
				break;
			case X86SubSdOp subsd:
				EmitSubSd(subsd.Dest, subsd.Src);
				break;
			case X86MulSdOp mulsd:
				EmitMulSd(mulsd.Dest, mulsd.Src);
				break;
			case X86DivSdOp divsd:
				EmitDivSd(divsd.Dest, divsd.Src);
				break;
			case X86CvttSd2SiOp cvttsd2si:
				EmitCvttSd2Si(cvttsd2si.Dest, cvttsd2si.Src);
				break;
			case X86AndpdRipRelOp andpd:
				EmitAndpdRipRel(andpd.Dest, andpd.RdataLabel);
				break;
			case X86SqrtSdOp sqrtsd:
				EmitSqrtSd(sqrtsd.Dest, sqrtsd.Src);
				break;
			case X86RoundSdOp roundsd:
				EmitRoundSd(roundsd.Dest, roundsd.Src, roundsd.Mode);
				break;
			case X86MinSdOp minsd:
				EmitMinSd(minsd.Dest, minsd.Src);
				break;
			case X86MaxSdOp maxsd:
				EmitMaxSd(maxsd.Dest, maxsd.Src);
				break;
			case X86MovSdXmmXmmOp movsdReg:
				EmitMovSdXmmXmm(movsdReg.Dest, movsdReg.Src);
				break;
			case X86UcomisdOp ucomisd:
				EmitUcomisd(ucomisd.Src1, ucomisd.Src2);
				break;
			case X86JccOp jcc:
				EmitJcc(jcc.Condition, jcc.Target);
				break;
			case X86JmpOp jmp:
				EmitJmp(jmp.Target);
				break;
			case X86CqoOp:
				EmitBytes(0x48, 0x99); // CQO: REX.W + 99
				break;
			case X86IdivRegOp idiv:
				EmitIdivReg(idiv.Divisor);
				break;
			default:
				throw new InvalidOperationException($"No X86 emission for: {op.GetType().Name} ({op.Mnemonic})");
		}
	}

	// --- Start wrapper (_start entry point) ---

	public void EmitStartWrapper() {
		DefineLabel("_start");

		// sub rsp, 0x28 (shadow space + alignment for Windows x64 ABI)
		EmitBytes(0x48, 0x83, 0xEC, 0x28);

		// call main (relative call, patched later)
		EmitByte(0xE8);
		_relCallFixups.Add((_code.Count, "main"));
		EmitDword(0); // placeholder

		// mov ecx, eax (move return value to first arg for ExitProcess)
		EmitBytes(0x89, 0xC1);

		// call [rip+disp32] ExitProcess (indirect through IAT)
		EmitBytes(0xFF, 0x15);
		var exitProcessIndex = AddImport("kernel32.dll", "ExitProcess");
		_importCallFixups.Add((_code.Count, exitProcessIndex));
		EmitDword(0); // placeholder

		// int3 (should never reach here)
		EmitByte(0xCC);
	}

	// --- Runtime stubs ---

	public void EmitChkstk() {
		DefineLabel("__chkstk");
		// Minimal chkstk: just return. For large stack frames Windows requires
		// probing each page, but for basic.maxon this is sufficient.
		EmitByte(0xC3); // ret
	}

	public static void EmitRuntimeFunctions() {
		// No runtime functions needed for basic.maxon
	}

	public void PatchChkstkCalls() {
		if (!_labels.TryGetValue("__chkstk", out var chkstkOffset)) return;
		foreach (var site in _chkstkCallSites) {
			var rel = chkstkOffset - (site + 4);
			PatchDword(site, rel);
		}
	}

	// --- Resolution ---

	public void ResolveLabels() {
		foreach (var (offset, target) in _relCallFixups) {
			if (!_labels.TryGetValue(target, out var targetOffset)) {
				throw new InvalidOperationException($"Unresolved label: {target}");
			}
			var rel = targetOffset - (offset + 4);
			PatchDword(offset, rel);
		}
		foreach (var (offset, target) in _jumpFixups) {
			if (!_labels.TryGetValue(target, out var targetOffset)) {
				throw new InvalidOperationException($"Unresolved jump target: {target}");
			}
			var rel = targetOffset - (offset + 4);
			PatchDword(offset, rel);
		}
	}

	public void ResolveRdata(int rvaOffset) {
		// rvaOffset = distance from end of code bytes to start of rdata in virtual memory
		// Target position relative to code start = _code.Count + rvaOffset + rdataOffset
		// RIP-relative: target - (fixupOffset + 4)
		var codeSize = _code.Count;
		foreach (var (offset, label) in _rdataFixups) {
			if (!_rdataLabels.TryGetValue(label, out var rdataOffset)) {
				throw new InvalidOperationException($"Unresolved rdata label: {label}");
			}
			var target = codeSize + rvaOffset + rdataOffset;
			var rel = target - (offset + 4);
			PatchDword(offset, rel);
		}
	}

	public void ResolveGlobals(int rvaOffset) {
		var codeSize = _code.Count;
		foreach (var (offset, name) in _globalFixups) {
			if (!_globalLabels.TryGetValue(name, out var globalOffset)) {
				throw new InvalidOperationException($"Unresolved global: {name}");
			}
			var target = codeSize + rvaOffset + globalOffset;
			var rel = target - (offset + 4);
			PatchDword(offset, rel);
		}
	}

	public void ResolveImports(int rvaOffset) {
		// IAT entries are 8 bytes each. Import index i is at IAT offset i*8.
		var codeSize = _code.Count;
		foreach (var (offset, importIndex) in _importCallFixups) {
			var iatEntryOffset = importIndex * 8;
			var target = codeSize + rvaOffset + iatEntryOffset;
			var rel = target - (offset + 4);
			PatchDword(offset, rel);
		}
	}

	// --- Output ---

	public byte[] GetCode() => [.. _code];
	public byte[] GetRdata() => [.. _rdata];
	public byte[] GetData() => [.. _data];

	// --- Private helpers ---

	private int AddImport(string dllName, string functionName) {
		var index = _imports.FindIndex(i => i.DllName == dllName && i.FunctionName == functionName);
		if (index >= 0) return index;
		_imports.Add(new ImportEntry(dllName, functionName));
		return _imports.Count - 1;
	}

	private void EmitByte(byte b) {
		_code.Add(b);
	}

	private void EmitBytes(params byte[] bytes) {
		_code.AddRange(bytes);
	}

	private void EmitDword(int value) {
		_code.AddRange(BitConverter.GetBytes(value));
	}

	private void PatchDword(int offset, int value) {
		var bytes = BitConverter.GetBytes(value);
		_code[offset] = bytes[0];
		_code[offset + 1] = bytes[1];
		_code[offset + 2] = bytes[2];
		_code[offset + 3] = bytes[3];
	}

	// --- X86 encoding helpers ---

	private static bool Is64BitReg(X86Register reg) {
		return reg <= X86Register.R15;
	}

	private static int RegCode(X86Register reg) {
		return reg switch {
			X86Register.Rax or X86Register.Eax => 0,
			X86Register.Rcx or X86Register.Ecx => 1,
			X86Register.Rdx or X86Register.Edx => 2,
			X86Register.Rbx or X86Register.Ebx => 3,
			X86Register.Rsp or X86Register.Esp => 4,
			X86Register.Rbp or X86Register.Ebp => 5,
			X86Register.Rsi or X86Register.Esi => 6,
			X86Register.Rdi or X86Register.Edi => 7,
			X86Register.R8 => 0,
			X86Register.R9 => 1,
			X86Register.R10 => 2,
			X86Register.R11 => 3,
			X86Register.R12 => 4,
			X86Register.R13 => 5,
			X86Register.R14 => 6,
			X86Register.R15 => 7,
			_ => throw new ArgumentException($"Unknown register: {reg}")
		};
	}

	private static bool NeedsRex(X86Register reg) {
		return reg >= X86Register.R8 && reg <= X86Register.R15;
	}

	private void EmitPushReg(X86Register reg) {
		if (NeedsRex(reg)) {
			EmitByte(0x41); // REX.B
		}
		EmitByte((byte)(0x50 + RegCode(reg)));
	}

	private void EmitPopReg(X86Register reg) {
		if (NeedsRex(reg)) {
			EmitByte(0x41); // REX.B
		}
		EmitByte((byte)(0x58 + RegCode(reg)));
	}

	private void EmitXchgRegReg(X86Register a, X86Register b) {
		// XCHG r32, r32: 87 /r
		EmitByte(0x87);
		EmitByte((byte)(0xC0 | (RegCode(a) << 3) | RegCode(b)));
	}

	private void EmitMovRegReg(X86Register dest, X86Register src) {
		// MOV r64, r64: REX.W + 89 /r
		if (Is64BitReg(dest) || Is64BitReg(src)) {
			byte rex = 0x48; // REX.W
			if (NeedsRex(src)) rex |= 0x04; // REX.R
			if (NeedsRex(dest)) rex |= 0x01; // REX.B
			EmitByte(rex);
			EmitByte(0x89);
			EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
		} else {
			// MOV r32, r32: 89 /r
			EmitByte(0x89);
			EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
		}
	}

	private void EmitMovRegImm(X86Register dest, long immediate) {
		if (Is64BitReg(dest)) {
			if (immediate >= int.MinValue && immediate <= int.MaxValue) {
				// MOV r64, imm32 (sign-extended): REX.W + C7 /0 id
				byte rex = 0x48;
				if (NeedsRex(dest)) rex |= 0x01;
				EmitByte(rex);
				EmitByte(0xC7);
				EmitByte((byte)(0xC0 | RegCode(dest)));
				EmitDword((int)immediate);
			} else {
				// MOV r64, imm64: REX.W + B8+rd io
				byte rex = 0x48;
				if (NeedsRex(dest)) rex |= 0x01;
				EmitByte(rex);
				EmitByte((byte)(0xB8 + RegCode(dest)));
				_code.AddRange(BitConverter.GetBytes(immediate));
			}
		} else {
			// MOV r32, imm32: B8+rd id
			EmitByte((byte)(0xB8 + RegCode(dest)));
			EmitDword((int)immediate);
		}
	}

	private void EmitSubRegImm(X86Register dest, long immediate) {
		if (immediate >= -128 && immediate <= 127) {
			// SUB r64, imm8: REX.W + 83 /5 ib
			byte rex = 0x48;
			if (NeedsRex(dest)) rex |= 0x01;
			EmitByte(rex);
			EmitByte(0x83);
			EmitByte((byte)(0xE8 | RegCode(dest)));
			EmitByte((byte)immediate);
		} else {
			// SUB r64, imm32: REX.W + 81 /5 id
			byte rex = 0x48;
			if (NeedsRex(dest)) rex |= 0x01;
			EmitByte(rex);
			EmitByte(0x81);
			EmitByte((byte)(0xE8 | RegCode(dest)));
			EmitDword((int)immediate);
		}
	}

	private void EmitAddRegImm(X86Register dest, long immediate) {
		if (immediate >= -128 && immediate <= 127) {
			// ADD r64, imm8: REX.W + 83 /0 ib
			byte rex = 0x48;
			if (NeedsRex(dest)) rex |= 0x01;
			EmitByte(rex);
			EmitByte(0x83);
			EmitByte((byte)(0xC0 | RegCode(dest)));
			EmitByte((byte)immediate);
		} else {
			// ADD r64, imm32: REX.W + 81 /0 id
			byte rex = 0x48;
			if (NeedsRex(dest)) rex |= 0x01;
			EmitByte(rex);
			EmitByte(0x81);
			EmitByte((byte)(0xC0 | RegCode(dest)));
			EmitDword((int)immediate);
		}
	}

	private void EmitAddRegReg(X86Register dest, X86Register src) {
		// ADD r64, r64: REX.W + 01 /r
		byte rex = 0x48;
		if (NeedsRex(src)) rex |= 0x04; // REX.R
		if (NeedsRex(dest)) rex |= 0x01; // REX.B
		EmitByte(rex);
		EmitByte(0x01);
		EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
	}

	private void EmitSubRegReg(X86Register dest, X86Register src) {
		// SUB r64, r64: REX.W + 29 /r
		byte rex = 0x48;
		if (NeedsRex(src)) rex |= 0x04; // REX.R
		if (NeedsRex(dest)) rex |= 0x01; // REX.B
		EmitByte(rex);
		EmitByte(0x29);
		EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
	}

	private void EmitXorRegReg(X86Register dest, X86Register src) {
		// XOR r32, r32: 31 /r  (implicitly zero-extends to 64 bits)
		if (NeedsRex(src) || NeedsRex(dest)) {
			byte rex = 0x40;
			if (NeedsRex(src)) rex |= 0x04; // REX.R
			if (NeedsRex(dest)) rex |= 0x01; // REX.B
			EmitByte(rex);
		}
		EmitByte(0x31);
		EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
	}

	private void EmitCmpRegReg(X86Register lhs, X86Register rhs) {
		// CMP r64, r64: REX.W + 39 /r
		byte rex = 0x48;
		if (NeedsRex(rhs)) rex |= 0x04; // REX.R
		if (NeedsRex(lhs)) rex |= 0x01; // REX.B
		EmitByte(rex);
		EmitByte(0x39);
		EmitByte((byte)(0xC0 | (RegCode(rhs) << 3) | RegCode(lhs)));
	}

	private void EmitImulRegReg(X86Register dest, X86Register src) {
		// IMUL r64, r64: REX.W + 0F AF /r
		byte rex = 0x48;
		if (NeedsRex(dest)) rex |= 0x04; // REX.R
		if (NeedsRex(src)) rex |= 0x01; // REX.B
		EmitByte(rex);
		EmitByte(0x0F);
		EmitByte(0xAF);
		EmitByte((byte)(0xC0 | (RegCode(dest) << 3) | RegCode(src)));
	}

	private void EmitIdivReg(X86Register divisor) {
		// IDIV r64: REX.W + F7 /7
		byte rex = 0x48;
		if (NeedsRex(divisor)) rex |= 0x01; // REX.B
		EmitByte(rex);
		EmitByte(0xF7);
		EmitByte((byte)(0xF8 | RegCode(divisor))); // /7 = 111 in reg field
	}

	private void EmitMovMemReg(int displacement, X86Register src) {
		// MOV [rbp+disp], r64: REX.W + 89 /r (mod=01 or 10, r/m=rbp)
		byte rex = 0x48; // REX.W
		if (NeedsRex(src)) rex |= 0x04; // REX.R
		EmitByte(rex);
		EmitByte(0x89);
		if (displacement >= -128 && displacement <= 127) {
			EmitByte((byte)(0x45 | (RegCode(src) << 3))); // mod=01, r/m=rbp(5)
			EmitByte((byte)(displacement & 0xFF));
		} else {
			EmitByte((byte)(0x85 | (RegCode(src) << 3))); // mod=10, r/m=rbp(5)
			EmitDword(displacement);
		}
	}

	private void EmitMovRegMem(X86Register dest, int displacement) {
		// MOV r64, [rbp+disp]: REX.W + 8B /r (mod=01 or 10, r/m=rbp)
		byte rex = 0x48; // REX.W
		if (NeedsRex(dest)) rex |= 0x04; // REX.R
		EmitByte(rex);
		EmitByte(0x8B);
		if (displacement >= -128 && displacement <= 127) {
			EmitByte((byte)(0x45 | (RegCode(dest) << 3))); // mod=01, r/m=rbp(5)
			EmitByte((byte)(displacement & 0xFF));
		} else {
			EmitByte((byte)(0x85 | (RegCode(dest) << 3))); // mod=10, r/m=rbp(5)
			EmitDword(displacement);
		}
	}

	private void EmitMovMemRspReg(int offset, X86Register src) {
		// MOV [rsp+offset], r64: REX.W + 89 /r with SIB byte (rsp addressing requires SIB)
		byte rex = 0x48; // REX.W
		if (NeedsRex(src)) rex |= 0x04; // REX.R
		EmitByte(rex);
		EmitByte(0x89);
		if (offset == 0) {
			EmitByte((byte)(0x04 | (RegCode(src) << 3))); // mod=00, r/m=100 (SIB follows)
			EmitByte(0x24); // SIB: scale=00, index=rsp(100), base=rsp(100)
		} else if (offset >= -128 && offset <= 127) {
			EmitByte((byte)(0x44 | (RegCode(src) << 3))); // mod=01, r/m=100 (SIB follows)
			EmitByte(0x24); // SIB: scale=00, index=rsp(100), base=rsp(100)
			EmitByte((byte)(offset & 0xFF));
		} else {
			EmitByte((byte)(0x84 | (RegCode(src) << 3))); // mod=10, r/m=100 (SIB follows)
			EmitByte(0x24); // SIB: scale=00, index=rsp(100), base=rsp(100)
			EmitDword(offset);
		}
	}

	// --- SSE2 encoding helpers ---

	private static int XmmRegCode(X86XmmRegister reg) {
		return (int)reg;
	}

	/// <summary>
	/// Emit an SSE/SSE2 XMM-to-XMM register instruction: prefix [REX] opcode ModRM
	/// </summary>
	private void EmitXmmRegRegOp(byte prefix, byte opcode1, byte opcode2, int destCode, int srcCode) {
		EmitByte(prefix);
		if (destCode >= 8 || srcCode >= 8) {
			byte rex = 0x40;
			if (destCode >= 8) rex |= 0x04; // REX.R
			if (srcCode >= 8) rex |= 0x01; // REX.B
			EmitByte(rex);
		}
		EmitBytes(opcode1, opcode2);
		EmitByte((byte)(0xC0 | ((destCode & 7) << 3) | (srcCode & 7)));
	}

	private void EmitMovSdXmmRipRel(X86XmmRegister dest, string rdataLabel) {
		// MOVSD xmm, [rip+disp32]: F2 0F 10 /r (ModRM: mod=00, r/m=101 for RIP-relative)
		var reg = XmmRegCode(dest);
		EmitByte(0xF2);
		if (reg >= 8) {
			EmitByte(0x44); // REX.R
		}
		EmitBytes(0x0F, 0x10);
		EmitByte((byte)(0x05 | ((reg & 7) << 3)));
		_rdataFixups.Add((_code.Count, rdataLabel));
		EmitDword(0); // placeholder for RIP-relative displacement
	}

	private void EmitMovSdMemXmm(int displacement, X86XmmRegister src) {
		// MOVSD [rbp+disp], xmm: F2 0F 11 /r
		var reg = XmmRegCode(src);
		EmitByte(0xF2);
		if (reg >= 8) {
			EmitByte(0x44); // REX.R
		}
		EmitBytes(0x0F, 0x11);
		if (displacement >= -128 && displacement <= 127) {
			EmitByte((byte)(0x45 | ((reg & 7) << 3))); // mod=01, r/m=rbp(5)
			EmitByte((byte)(displacement & 0xFF));
		} else {
			EmitByte((byte)(0x85 | ((reg & 7) << 3))); // mod=10, r/m=rbp(5)
			EmitDword(displacement);
		}
	}

	private void EmitMovSdXmmMem(X86XmmRegister dest, int displacement) {
		// MOVSD xmm, [rbp+disp]: F2 0F 10 /r
		var reg = XmmRegCode(dest);
		EmitByte(0xF2);
		if (reg >= 8) {
			EmitByte(0x44); // REX.R
		}
		EmitBytes(0x0F, 0x10);
		if (displacement >= -128 && displacement <= 127) {
			EmitByte((byte)(0x45 | ((reg & 7) << 3)));
			EmitByte((byte)(displacement & 0xFF));
		} else {
			EmitByte((byte)(0x85 | ((reg & 7) << 3)));
			EmitDword(displacement);
		}
	}

	private void EmitAddSd(X86XmmRegister dest, X86XmmRegister src) {
		// ADDSD xmm, xmm: F2 0F 58 /r
		EmitXmmRegRegOp(0xF2, 0x0F, 0x58, XmmRegCode(dest), XmmRegCode(src));
	}

	private void EmitSubSd(X86XmmRegister dest, X86XmmRegister src) {
		// SUBSD xmm, xmm: F2 0F 5C /r
		EmitXmmRegRegOp(0xF2, 0x0F, 0x5C, XmmRegCode(dest), XmmRegCode(src));
	}

	private void EmitMulSd(X86XmmRegister dest, X86XmmRegister src) {
		// MULSD xmm, xmm: F2 0F 59 /r
		EmitXmmRegRegOp(0xF2, 0x0F, 0x59, XmmRegCode(dest), XmmRegCode(src));
	}

	private void EmitDivSd(X86XmmRegister dest, X86XmmRegister src) {
		// DIVSD xmm, xmm: F2 0F 5E /r
		EmitXmmRegRegOp(0xF2, 0x0F, 0x5E, XmmRegCode(dest), XmmRegCode(src));
	}

	private void EmitCvttSd2Si(X86Register dest, X86XmmRegister src) {
		// CVTTSD2SI r64, xmm: F2 REX.W 0F 2C /r
		var d = RegCode(dest);
		var s = XmmRegCode(src);
		EmitByte(0xF2);
		byte rex = 0x48; // REX.W
		if (NeedsRex(dest)) rex |= 0x04; // REX.R
		if (s >= 8) rex |= 0x01; // REX.B
		EmitByte(rex);
		EmitBytes(0x0F, 0x2C);
		EmitByte((byte)(0xC0 | (d << 3) | (s & 7)));
	}

	private void EmitAndpdRipRel(X86XmmRegister dest, string rdataLabel) {
		// ANDPD xmm, [rip+disp32]: 66 0F 54 /r (ModRM: mod=00, r/m=101 for RIP-relative)
		var reg = XmmRegCode(dest);
		EmitByte(0x66);
		if (reg >= 8) {
			EmitByte(0x44); // REX.R
		}
		EmitBytes(0x0F, 0x54);
		EmitByte((byte)(0x05 | ((reg & 7) << 3)));
		_rdataFixups.Add((_code.Count, rdataLabel));
		EmitDword(0); // placeholder for RIP-relative displacement
	}

	private void EmitSqrtSd(X86XmmRegister dest, X86XmmRegister src) {
		// SQRTSD xmm, xmm: F2 0F 51 /r
		EmitXmmRegRegOp(0xF2, 0x0F, 0x51, XmmRegCode(dest), XmmRegCode(src));
	}

	private void EmitRoundSd(X86XmmRegister dest, X86XmmRegister src, byte mode) {
		// ROUNDSD xmm, xmm, imm8: 66 0F 3A 0B /r ib
		var d = XmmRegCode(dest);
		var s = XmmRegCode(src);
		EmitByte(0x66);
		if (d >= 8 || s >= 8) {
			byte rex = 0x40;
			if (d >= 8) rex |= 0x04; // REX.R
			if (s >= 8) rex |= 0x01; // REX.B
			EmitByte(rex);
		}
		EmitBytes(0x0F, 0x3A, 0x0B);
		EmitByte((byte)(0xC0 | ((d & 7) << 3) | (s & 7)));
		EmitByte(mode);
	}

	private void EmitMinSd(X86XmmRegister dest, X86XmmRegister src) {
		// MINSD xmm, xmm: F2 0F 5D /r
		EmitXmmRegRegOp(0xF2, 0x0F, 0x5D, XmmRegCode(dest), XmmRegCode(src));
	}

	private void EmitMaxSd(X86XmmRegister dest, X86XmmRegister src) {
		// MAXSD xmm, xmm: F2 0F 5F /r
		EmitXmmRegRegOp(0xF2, 0x0F, 0x5F, XmmRegCode(dest), XmmRegCode(src));
	}

	private void EmitMovSdXmmXmm(X86XmmRegister dest, X86XmmRegister src) {
		// MOVSD xmm, xmm: F2 0F 10 /r
		EmitXmmRegRegOp(0xF2, 0x0F, 0x10, XmmRegCode(dest), XmmRegCode(src));
	}

	private void EmitUcomisd(X86XmmRegister src1, X86XmmRegister src2) {
		// UCOMISD xmm, xmm: 66 0F 2E /r
		EmitXmmRegRegOp(0x66, 0x0F, 0x2E, XmmRegCode(src1), XmmRegCode(src2));
	}

	private void EmitJcc(string condition, string target) {
		// Jcc rel32: 0F 8x rel32
		byte opcode = condition switch {
			"e" or "z" => 0x84,
			"ne" or "nz" => 0x85,
			"b" or "c" => 0x82,
			"ae" or "nc" => 0x83,
			"p" or "pe" => 0x8A,
			"np" or "po" => 0x8B,
			"l" => 0x8C,
			"ge" => 0x8D,
			"le" => 0x8E,
			"g" => 0x8F,
			_ => throw new InvalidOperationException($"Unsupported Jcc condition: {condition}")
		};
		EmitByte(0x0F);
		EmitByte(opcode);
		_jumpFixups.Add((_code.Count, target));
		EmitDword(0); // placeholder for rel32
	}

	private void EmitJmp(string target) {
		// JMP rel32: E9 rel32
		EmitByte(0xE9);
		_jumpFixups.Add((_code.Count, target));
		EmitDword(0); // placeholder
	}
}
