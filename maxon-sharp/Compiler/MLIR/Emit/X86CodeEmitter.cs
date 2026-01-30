using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Emit;

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

	public void DefineRdata(string label, byte[] bytes) {
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
			case X86PushReg push:
				EmitPushReg(push.Register);
				break;
			case X86PopReg pop:
				EmitPopReg(pop.Register);
				break;
			case X86MovRegReg mov:
				EmitMovRegReg(mov.Dest, mov.Src);
				break;
			case X86MovRegImm mov:
				EmitMovRegImm(mov.Dest, mov.Immediate);
				break;
			case X86Ret:
				EmitByte(0xC3); // ret
				break;
			case X86SubRegImm sub:
				EmitSubRegImm(sub.Dest, sub.Immediate);
				break;
			case X86AddRegImm add:
				EmitAddRegImm(add.Dest, add.Immediate);
				break;
			default:
				throw new CompileError(ErrorCode.CodeEmitterUnsupportedInstruction, $"Unsupported X86 operation: {op.GetType().Name}");
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
				throw new CompileError(ErrorCode.CodeEmitterUnsupportedInstruction, $"Unresolved label: {target}");
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
				throw new CompileError(ErrorCode.CodeEmitterUnsupportedInstruction, $"Unresolved rdata label: {label}");
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
				throw new CompileError(ErrorCode.CodeEmitterUnsupportedInstruction, $"Unresolved global: {name}");
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
}
