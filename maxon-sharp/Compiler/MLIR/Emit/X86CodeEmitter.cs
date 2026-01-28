using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Emit;

/// <summary>
/// Represents an imported function.
/// </summary>
public record ImportEntry(string DllName, string FunctionName);

/// <summary>
/// Emits machine code from X86 dialect operations.
/// </summary>
public sealed class X86CodeEmitter {
	private readonly List<byte> _code = [];
	private readonly Dictionary<string, int> _labelOffsets = [];
	private readonly List<(int offset, string label, int instrSize)> _labelFixups = [];
	private string _currentFunction = "";

	// Global data support
	private readonly List<byte> _data = [];
	private readonly Dictionary<string, int> _globalOffsets = [];
	private readonly List<(int codeOffset, string globalName, int instrSize)> _globalFixups = [];

	// Import table support
	private readonly List<ImportEntry> _imports = [];
	private readonly Dictionary<string, int> _importIndices = [];  // function name -> index in _imports
	private readonly List<(int codeOffset, int importIndex, int instrSize)> _importFixups = [];

	// Rdata section (read-only constants like array literals)
	private readonly List<byte> _rdata = [];
	private readonly Dictionary<string, int> _rdataLabels = [];  // label -> offset
	private readonly List<(int codeOffset, string label, int instrSize)> _rdataFixups = [];

	/// <summary>
	/// Gets the emitted machine code.
	/// </summary>
	public byte[] GetCode() => [.. _code];

	/// <summary>
	/// Gets the emitted global data.
	/// </summary>
	public byte[] GetData() => [.. _data];

	/// <summary>
	/// Gets the emitted read-only data (rdata) section.
	/// </summary>
	public byte[] GetRdata() => [.. _rdata];

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
	/// Defines constant data in the read-only data (rdata) section.
	/// Returns the offset, deduplicating identical data by label.
	/// </summary>
	public int DefineRdata(string label, byte[] data) {
		// Deduplication: if we already have this label, return existing offset
		if (_rdataLabels.TryGetValue(label, out var existing))
			return existing;

		var offset = _rdata.Count;
		_rdataLabels[label] = offset;
		_rdata.AddRange(data);

		// Align to 8 bytes for optimal access
		while (_rdata.Count % 8 != 0)
			_rdata.Add(0);

		Logger.Trace(LogCategory.Codegen, $"DefineRdata: {label} at offset {offset}, size {data.Length}");

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
			case JzOp jz:
				EmitJz(jz);
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
				DefineLabel(ScopedLabel(label.Name));
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
			case LeaRdataOp leaRdata:
				EmitLeaRdata(leaRdata);
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
			case XorpdOp xorpd:
				EmitXorpd(xorpd);
				break;
			case MovzxOp movzx:
				EmitMovzx(movzx);
				break;
			case MovsxOp movsx:
				EmitMovsx(movsx);
				break;
			case MovsxdOp movsxd:
				EmitMovsxd(movsxd);
				break;
			case PrologueOp prologue:
				EmitPrologue(prologue);
				break;
			case EpilogueOp epilogue:
				EmitEpilogue(epilogue);
				break;
			case CldOp:
				EmitCld();
				break;
			case StdOp:
				EmitStd();
				break;
			case RepMovsbOp:
				EmitRepMovsb();
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
	/// Emits function prologue: push rbp; mov rbp, rsp; [push callee-saved]; sub rsp, N
	/// Callee-saved register pushes happen BEFORE sub rsp to ensure shadow space
	/// for outgoing calls doesn't overlap with saved registers.
	/// For stack allocations >= 4096 bytes, calls __chkstk to probe stack pages.
	/// </summary>
	private void EmitPrologue(PrologueOp op) {
		// push rbp
		EmitByte(0x55);

		// mov rbp, rsp (REX.W + MOV r/m64, r64)
		EmitRexW(X86Register.RSP, X86Register.RBP);
		EmitByte(0x89);
		EmitByte(ModRM(0b11, GetRegCode(X86Register.RSP), GetRegCode(X86Register.RBP)));

		// Push callee-saved GPRs (before sub rsp)
		foreach (var reg in op.CalleeSavedGprs) {
			EmitPushReg(reg);
		}

		// sub rsp, N (with __chkstk for large allocations)
		if (op.StackSize > 0) {
			if (op.StackSize >= 4096) {
				// Large stack allocation: call __chkstk first
				// mov eax, stackSize (32-bit immediate is sufficient for stack sizes)
				EmitByte(0xB8);
				EmitImm32(op.StackSize);

				// call __chkstk (relative call, will be patched later)
				EmitByte(0xE8);
				RecordChkstkCall();
				EmitImm32(0);  // Placeholder for displacement
			}

			// sub rsp, N
			EmitRexW(rm: X86Register.RSP);
			if (op.StackSize <= 127) {
				// sub rsp, imm8 (0x83 /5)
				EmitByte(0x83);
				EmitByte(ModRM(0b11, 5, GetRegCode(X86Register.RSP)));
				EmitByte((byte)op.StackSize);
			} else {
				// sub rsp, imm32 (0x81 /5)
				EmitByte(0x81);
				EmitByte(ModRM(0b11, 5, GetRegCode(X86Register.RSP)));
				EmitImm32(op.StackSize);
			}
		}
	}

	/// <summary>
	/// Emits function epilogue to match the prologue layout:
	/// Prologue: push rbp; mov rbp,rsp; push callee-saved; sub rsp,N
	/// Epilogue: lea rsp,[rbp-K]; pop callee-saved; pop rbp; ret
	/// where K = number of callee-saved GPRs * 8
	/// </summary>
	private void EmitEpilogue(EpilogueOp op) {
		if (op.CalleeSavedGprs.Count > 0) {
			// lea rsp, [rbp - K] where K = callee-saved count * 8
			// This restores RSP to point to the topmost callee-saved register
			int offset = op.CalleeSavedGprs.Count * 8;
			EmitRexW(X86Register.RBP, X86Register.RSP);
			EmitByte(0x8D);  // LEA
			if (offset <= 127) {
				EmitByte(ModRM(0b01, GetRegCode(X86Register.RSP), GetRegCode(X86Register.RBP)));  // [rbp + disp8]
				EmitByte((byte)(-offset));  // Negative displacement
			} else {
				EmitByte(ModRM(0b10, GetRegCode(X86Register.RSP), GetRegCode(X86Register.RBP)));  // [rbp + disp32]
				EmitImm32(-offset);  // Negative displacement
			}

			// Pop callee-saved GPRs in reverse order
			for (int i = op.CalleeSavedGprs.Count - 1; i >= 0; i--) {
				EmitPopReg(op.CalleeSavedGprs[i]);
			}

			// pop rbp (RSP now points to saved rbp after all callee-saved pops)
			EmitByte(0x5D);
		} else {
			// No callee-saved registers, use simple epilogue
			// mov rsp, rbp (REX.W + MOV r/m64, r64)
			EmitRexW(X86Register.RBP, X86Register.RSP);
			EmitByte(0x89);
			EmitByte(ModRM(0b11, GetRegCode(X86Register.RBP), GetRegCode(X86Register.RSP)));

			// pop rbp
			EmitByte(0x5D);
		}
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
	/// Resolves all rdata references.
	/// Must be called after code emission is complete but before writing to PE.
	/// </summary>
	/// <param name="rdataRvaOffset">The RVA offset of the rdata section relative to code section end</param>
	public void ResolveRdata(int rdataRvaOffset) {
		Logger.Debug(LogCategory.Codegen, $"Resolving {_rdataFixups.Count} rdata references");

		foreach (var (codeOffset, label, instrSize) in _rdataFixups) {
			if (!_rdataLabels.TryGetValue(label, out var rdataOffset)) {
				throw new InvalidOperationException($"Undefined rdata label: {label}");
			}

			// RIP-relative addressing: displacement is from end of instruction to target
			// Target = code_end + rdataRvaOffset + rdataOffset
			// RIP at instruction end = codeOffset + instrSize
			// So displacement = code_end + rdataRvaOffset + rdataOffset - (codeOffset + instrSize)
			var codeEnd = _code.Count;
			var targetAddr = codeEnd + rdataRvaOffset + rdataOffset;
			var ripAtEnd = codeOffset + instrSize;
			var relOffset = targetAddr - ripAtEnd;

			Logger.Trace(LogCategory.Codegen, $"  {label}: code offset 0x{codeOffset:X} -> rdata offset 0x{rdataOffset:X}");

			// Patch the displacement
			_code[codeOffset] = (byte)relOffset;
			_code[codeOffset + 1] = (byte)(relOffset >> 8);
			_code[codeOffset + 2] = (byte)(relOffset >> 16);
			_code[codeOffset + 3] = (byte)(relOffset >> 24);
		}
	}

	/// <summary>
	/// Resolves all import fixups.
	/// Must be called after code emission is complete and import table addresses are known.
	/// </summary>
	/// <param name="iatRvaOffset">The RVA offset of the IAT relative to code section end</param>
	public void ResolveImports(int iatRvaOffset) {
		Logger.Debug(LogCategory.Codegen, $"Resolving {_importFixups.Count} import references");

		foreach (var (codeOffset, importIndex, instrSize) in _importFixups) {
			// Each IAT entry is 8 bytes (64-bit pointer)
			var iatEntryOffset = importIndex * 8;

			// RIP-relative addressing: displacement is from end of instruction to target
			// Target = code_end + iatRvaOffset + iatEntryOffset
			// RIP at instruction end = codeOffset + instrSize
			var codeEnd = _code.Count;
			var targetAddr = codeEnd + iatRvaOffset + iatEntryOffset;
			var ripAtEnd = codeOffset + instrSize;
			var relOffset = targetAddr - ripAtEnd;

			Logger.Trace(LogCategory.Codegen, $"  import[{importIndex}]: code offset 0x{codeOffset:X} -> IAT offset 0x{iatEntryOffset:X}");

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
	/// Gets whether there is any rdata defined.
	/// </summary>
	public bool HasRdata => _rdata.Count > 0;

	/// <summary>
	/// Gets whether there are any imports.
	/// </summary>
	public bool HasImports => _imports.Count > 0;

	/// <summary>
	/// Gets the list of imported functions.
	/// </summary>
	public IReadOnlyList<ImportEntry> Imports => _imports;

	/// <summary>
	/// Defines a label at the current position.
	/// </summary>
	public void DefineLabel(string name) {
		_labelOffsets[name] = CurrentOffset;
		Logger.Trace(LogCategory.Codegen, $"Label: {name} at offset 0x{CurrentOffset:X}");
	}

	/// <summary>
	/// Sets the current function name for scoping block labels.
	/// </summary>
	public void SetCurrentFunction(string name) {
		_currentFunction = name;
	}

	/// <summary>
	/// Returns a function-scoped label name.
	/// </summary>
	public string ScopedLabel(string blockName) {
		return $"{_currentFunction}.{blockName}";
	}

	// ========================================================================
	// Runtime functions (emitted as raw machine code)
	// ========================================================================

	// Track __chkstk call sites for patching
	private readonly List<int> _chkstkCallFixups = [];
	private int _chkstkOffset = -1;

	/// <summary>
	/// Emits the _start wrapper that calls main and ExitProcess.
	/// This must be emitted first at offset 0 (the entry point).
	/// </summary>
	public void EmitStartWrapper() {
		DefineLabel("_start");
		Logger.Debug(LogCategory.Codegen, "Emitting _start wrapper");

		// push rbp
		EmitByte(0x55);

		// mov rbp, rsp
		EmitBytes(0x48, 0x89, 0xE5);

		// sub rsp, 32 (shadow space)
		EmitBytes(0x48, 0x83, 0xEC, 0x20);

		// call main (relative call, will be patched)
		EmitByte(0xE8);
		_labelFixups.Add((CurrentOffset, "main", 4));
		EmitImm32(0);

		// mov rcx, rax (pass main's return value to ExitProcess)
		EmitBytes(0x48, 0x89, 0xC1);

		// call ExitProcess (external call via IAT)
		EmitByte(0xFF);
		EmitByte(0x15);
		if (!_importIndices.TryGetValue("ExitProcess", out var importIndex)) {
			importIndex = _imports.Count;
			_imports.Add(new ImportEntry("kernel32.dll", "ExitProcess"));
			_importIndices["ExitProcess"] = importIndex;
		}
		_importFixups.Add((CurrentOffset, importIndex, 4));
		EmitImm32(0);

		// Note: ExitProcess never returns, so no epilogue needed
	}

	/// <summary>
	/// Emits the __chkstk function for Windows x64 stack probing.
	/// Input: RAX = number of bytes to allocate.
	/// Probes each page and preserves all registers.
	/// </summary>
	public void EmitChkstk() {
		_chkstkOffset = CurrentOffset;
		DefineLabel("__chkstk");
		Logger.Debug(LogCategory.Codegen, $"Emitting __chkstk at offset 0x{_chkstkOffset:X}");

		// Algorithm:
		//   push rcx
		//   push r10
		//   mov rcx, rax          ; rcx = bytes to probe
		//   mov r10, rsp
		//   add r10, 24           ; adjust for return addr + 2 pushes
		// loop:
		//   cmp rcx, 0x1000
		//   jb done
		//   sub r10, 0x1000       ; move to next page
		//   test [r10], r10       ; touch the page
		//   sub rcx, 0x1000
		//   jmp loop
		// done:
		//   pop r10
		//   pop rcx
		//   ret

		// push rcx
		EmitByte(0x51);

		// push r10
#pragma warning disable IDE0230 // Use UTF-8 string literal
		EmitBytes(0x41, 0x52);
#pragma warning restore IDE0230 // Use UTF-8 string literal

		// mov rcx, rax
		EmitBytes(0x48, 0x89, 0xC1);

		// mov r10, rsp
		EmitBytes(0x4C, 0x89, 0xD4);  // This should be mov r10, rsp
																	// Actually: 49 89 E2 = mov r10, rsp (wrong direction)
																	// Correct: 4C 8B D4 = mov r10, rsp
																	// Let me fix this
		_code.RemoveRange(_code.Count - 3, 3);
		EmitBytes(0x4C, 0x8B, 0xD4);  // mov r10, rsp

		// add r10, 24 (adjust for return address + 2 pushes = 8 + 8 + 8 = 24)
		EmitBytes(0x49, 0x83, 0xC2, 0x18);

		// loop: (record offset for jmp back)
		var loopOffset = CurrentOffset;

		// cmp rcx, 0x1000
		EmitBytes(0x48, 0x81, 0xF9, 0x00, 0x10, 0x00, 0x00);

		// jb done (short jump, will patch)
		EmitByte(0x72);
		var jbPatchOffset = CurrentOffset;
		EmitByte(0x00);  // placeholder

		// sub r10, 0x1000
		EmitBytes(0x49, 0x81, 0xEA, 0x00, 0x10, 0x00, 0x00);

		// Touch the page to trigger guard page exception if needed
		// mov dword ptr [r10], 0  (actually read is sufficient: mov eax, [r10])
		// Using: or dword ptr [r10], 0 - reads and writes the memory location
		// 41 83 0A 00 = or dword ptr [r10], 0
		EmitBytes(0x41, 0x83, 0x0A, 0x00);

		// sub rcx, 0x1000
		EmitBytes(0x48, 0x81, 0xE9, 0x00, 0x10, 0x00, 0x00);

		// jmp loop (short jump back)
		EmitByte(0xEB);
		var jmpBackDisp = loopOffset - (CurrentOffset + 1);
		EmitByte((byte)jmpBackDisp);

		// done:
		var doneOffset = CurrentOffset;
		_code[jbPatchOffset] = (byte)(doneOffset - jbPatchOffset - 1);

		// pop r10
#pragma warning disable IDE0230 // Use UTF-8 string literal
		EmitBytes(0x41, 0x5A);
#pragma warning restore IDE0230 // Use UTF-8 string literal

		// pop rcx
		EmitByte(0x59);

		// ret
		EmitByte(0xC3);
	}

	/// <summary>
	/// Patches all __chkstk call sites with the actual offset.
	/// Must be called after EmitChkstk().
	/// </summary>
	public void PatchChkstkCalls() {
		if (_chkstkOffset < 0) return;

		foreach (var callOffset in _chkstkCallFixups) {
			// callOffset points to the 4-byte displacement after E8
			// Target = callOffset + 4 + displacement, so displacement = target - callOffset - 4
			var displacement = _chkstkOffset - callOffset - 4;
			_code[callOffset] = (byte)displacement;
			_code[callOffset + 1] = (byte)(displacement >> 8);
			_code[callOffset + 2] = (byte)(displacement >> 16);
			_code[callOffset + 3] = (byte)(displacement >> 24);
		}

		Logger.Debug(LogCategory.Codegen, $"Patched {_chkstkCallFixups.Count} __chkstk call sites");
	}

	/// <summary>
	/// Records a __chkstk call fixup for the current offset.
	/// Used by EmitPrologue when stack size >= 4096.
	/// </summary>
	public void RecordChkstkCall() {
		_chkstkCallFixups.Add(CurrentOffset);
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
		X86Register.XMM0 or X86Register.XMM8 => 0,
		X86Register.XMM1 or X86Register.XMM9 => 1,
		X86Register.XMM2 or X86Register.XMM10 => 2,
		X86Register.XMM3 or X86Register.XMM11 => 3,
		X86Register.XMM4 or X86Register.XMM12 => 4,
		X86Register.XMM5 or X86Register.XMM13 => 5,
		X86Register.XMM6 or X86Register.XMM14 => 6,
		X86Register.XMM7 or X86Register.XMM15 => 7,
		_ => throw new NotSupportedException($"Unsupported register: {reg}")
	};

	private static bool NeedsRex(X86Register reg) =>
		reg is >= X86Register.R8 and <= X86Register.R15 or
				 >= X86Register.R8D and <= X86Register.R15D or
				 >= X86Register.R8W and <= X86Register.R15W or
				 >= X86Register.R8B and <= X86Register.R15B or
				 >= X86Register.XMM8 and <= X86Register.XMM15;

	private void EmitRexW(X86Register? r = null, X86Register? rm = null) {
		byte rex = 0x48; // REX.W
		if (r is not null && NeedsRex(r.Value)) rex |= 0x04; // REX.R
		if (rm is not null && NeedsRex(rm.Value)) rex |= 0x01; // REX.B
		EmitByte(rex);
	}

	private void EmitRexW(X86Register r, X86Register baseReg, X86Register indexReg) {
		byte rex = 0x48; // REX.W
		if (NeedsRex(r)) rex |= 0x04; // REX.R
		if (NeedsRex(indexReg)) rex |= 0x02; // REX.X (for SIB index)
		if (NeedsRex(baseReg)) rex |= 0x01; // REX.B (for SIB base)
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

	private void EmitMovzx(MovzxOp op) {
		// MOVZX r64, r/m8 or r/m16
		// For i8 to i64: 0F B6 /r (zero-extend byte to qword)
		// For i16 to i64: 0F B7 /r (zero-extend word to qword)
		if (op.Dst is RegOperand dst && op.Src is RegOperand src) {
			EmitRexW(dst.Register, src.Register);
			EmitByte(0x0F);
			EmitByte(op.IsByte ? (byte)0xB6 : (byte)0xB7);
			EmitByte(ModRM(0b11, GetRegCode(dst.Register), GetRegCode(src.Register)));
		} else if (op.Dst is RegOperand dstReg && op.Src is MemOperand srcMem && srcMem.Base is RegOperand baseReg) {
			EmitRexW(dstReg.Register, baseReg.Register);
			EmitByte(0x0F);
			EmitByte(op.IsByte ? (byte)0xB6 : (byte)0xB7);
			EmitByte(ModRM(0b10, GetRegCode(dstReg.Register), GetRegCode(baseReg.Register)));
			if (GetRegCode(baseReg.Register) == 4) EmitByte(0x24);
			EmitImm32(srcMem.Displacement);
		} else {
			throw new NotSupportedException($"Unsupported MOVZX operands: {op.Dst}, {op.Src}");
		}
	}

	private void EmitMovsx(MovsxOp op) {
		// MOVSX r64, r/m8 or r/m16
		// For i8 to i64: REX.W + 0F BE /r (sign-extend byte to qword)
		// For i16 to i64: REX.W + 0F BF /r (sign-extend word to qword)
		if (op.Dst is RegOperand dst && op.Src is RegOperand src) {
			EmitRexW(dst.Register, src.Register);
			EmitByte(0x0F);
			EmitByte(op.IsByte ? (byte)0xBE : (byte)0xBF);
			EmitByte(ModRM(0b11, GetRegCode(dst.Register), GetRegCode(src.Register)));
		} else if (op.Dst is RegOperand dstReg && op.Src is MemOperand srcMem && srcMem.Base is RegOperand baseReg) {
			EmitRexW(dstReg.Register, baseReg.Register);
			EmitByte(0x0F);
			EmitByte(op.IsByte ? (byte)0xBE : (byte)0xBF);
			EmitByte(ModRM(0b10, GetRegCode(dstReg.Register), GetRegCode(baseReg.Register)));
			if (GetRegCode(baseReg.Register) == 4) EmitByte(0x24);
			EmitImm32(srcMem.Displacement);
		} else {
			throw new NotSupportedException($"Unsupported MOVSX operands: {op.Dst}, {op.Src}");
		}
	}

	private void EmitMovsxd(MovsxdOp op) {
		// MOVSXD r64, r/m32
		// REX.W + 63 /r (sign-extend dword to qword)
		if (op.Dst is RegOperand dst && op.Src is RegOperand src) {
			EmitRexW(dst.Register, src.Register);
			EmitByte(0x63);
			EmitByte(ModRM(0b11, GetRegCode(dst.Register), GetRegCode(src.Register)));
		} else if (op.Dst is RegOperand dstReg && op.Src is MemOperand srcMem && srcMem.Base is RegOperand baseReg) {
			EmitRexW(dstReg.Register, baseReg.Register);
			EmitByte(0x63);
			EmitByte(ModRM(0b10, GetRegCode(dstReg.Register), GetRegCode(baseReg.Register)));
			if (GetRegCode(baseReg.Register) == 4) EmitByte(0x24);
			EmitImm32(srcMem.Displacement);
		} else {
			throw new NotSupportedException($"Unsupported MOVSXD operands: {op.Dst}, {op.Src}");
		}
	}

	private void EmitMovRegMem(X86Register dst, MemOperand src) {
		// Determine opcode based on operand size
		// For byte loads: 0x8A (MOV r8, r/m8)
		// For word/dword/qword loads: 0x8B (MOV r16/32/64, r/m16/32/64)
		// REX.W only for 64-bit, 0x66 prefix for 16-bit
		byte opcode = src.Size == 1 ? (byte)0x8A : (byte)0x8B;

		if (src.Base is RegOperand baseReg && src.Index is RegOperand indexReg) {
			// [base + index*scale + disp] - requires SIB byte
			EmitMovRegMemPrefix(src.Size, dst, baseReg.Register, indexReg.Register);
			EmitByte(opcode);
			EmitByte(ModRM(0b10, GetRegCode(dst), 0b100)); // rm=4 means SIB follows
			EmitByte(SIB(src.Scale, GetRegCode(indexReg.Register), GetRegCode(baseReg.Register)));
			EmitImm32(src.Displacement);
		} else if (src.Base is RegOperand baseOnly) {
			// [base + disp32]
			EmitMovRegMemPrefix(src.Size, dst, baseOnly.Register, null);
			EmitByte(opcode);
			EmitByte(ModRM(0b10, GetRegCode(dst), GetRegCode(baseOnly.Register)));
			if (GetRegCode(baseOnly.Register) == 4) EmitByte(0x24); // SIB for RSP
			EmitImm32(src.Displacement);
		}
	}

	private void EmitMovRegMemPrefix(int size, X86Register dst, X86Register baseReg, X86Register? indexReg) {
		// Emit appropriate prefixes based on operand size:
		// - Size 8 (qword): REX.W prefix
		// - Size 4 (dword): REX prefix only if extended registers used (no W bit)
		// - Size 2 (word): 0x66 prefix + REX if extended registers
		// - Size 1 (byte): REX prefix only if extended registers used (no W bit)
		switch (size) {
			case 8:
				// 64-bit: always emit REX.W
				if (indexReg != null) {
					EmitRexW(dst, baseReg, indexReg.Value);
				} else {
					EmitRexW(dst, baseReg);
				}
				break;
			case 4:
				// 32-bit: only emit REX if extended registers are used (no W bit)
				if (NeedsRex(dst) || NeedsRex(baseReg) || (indexReg != null && NeedsRex(indexReg.Value))) {
					EmitByte(Rex(false, dst, baseReg, indexReg));
				}
				break;
			case 2:
				// 16-bit: 0x66 operand size prefix + REX if extended registers
				EmitByte(0x66);
				if (NeedsRex(dst) || NeedsRex(baseReg) || (indexReg != null && NeedsRex(indexReg.Value))) {
					EmitByte(Rex(false, dst, baseReg, indexReg));
				}
				break;
			case 1:
				// 8-bit: only emit REX if extended registers are used (no W bit)
				if (NeedsRex(dst) || NeedsRex(baseReg) || (indexReg != null && NeedsRex(indexReg.Value))) {
					EmitByte(Rex(false, dst, baseReg, indexReg));
				}
				break;
			default:
				throw new NotSupportedException($"Unsupported operand size: {size}");
		}
	}

	private void EmitMovMemReg(MemOperand dst, X86Register src) {
		// Determine opcode based on operand size
		// For byte stores: 0x88 (MOV r/m8, r8)
		// For word/dword/qword stores: 0x89 (MOV r/m16/32/64, r16/32/64)
		// REX.W only for 64-bit, 0x66 prefix for 16-bit
		byte opcode = dst.Size == 1 ? (byte)0x88 : (byte)0x89;

		if (dst.Base is RegOperand baseReg && dst.Index is RegOperand indexReg) {
			// [base + index*scale + disp] - requires SIB byte
			EmitMovMemRegPrefix(dst.Size, src, baseReg.Register, indexReg.Register);
			EmitByte(opcode);
			EmitByte(ModRM(0b10, GetRegCode(src), 0b100)); // rm=4 means SIB follows
			EmitByte(SIB(dst.Scale, GetRegCode(indexReg.Register), GetRegCode(baseReg.Register)));
			EmitImm32(dst.Displacement);
		} else if (dst.Base is RegOperand baseOnly) {
			// [base + disp32]
			EmitMovMemRegPrefix(dst.Size, src, baseOnly.Register, null);
			EmitByte(opcode);
			EmitByte(ModRM(0b10, GetRegCode(src), GetRegCode(baseOnly.Register)));
			if (GetRegCode(baseOnly.Register) == 4) EmitByte(0x24);
			EmitImm32(dst.Displacement);
		}
	}

	private void EmitMovMemRegPrefix(int size, X86Register src, X86Register baseReg, X86Register? indexReg) {
		// Emit appropriate prefixes based on operand size:
		// - Size 8 (qword): REX.W prefix
		// - Size 4 (dword): REX prefix only if extended registers used (no W bit)
		// - Size 2 (word): 0x66 prefix + REX if extended registers
		// - Size 1 (byte): REX prefix only if extended registers used (no W bit)
		switch (size) {
			case 8:
				// 64-bit: always emit REX.W
				if (indexReg != null) {
					EmitRexW(src, baseReg, indexReg.Value);
				} else {
					EmitRexW(src, baseReg);
				}
				break;
			case 4:
				// 32-bit: only emit REX if extended registers are used (no W bit)
				if (NeedsRex(src) || NeedsRex(baseReg) || (indexReg != null && NeedsRex(indexReg.Value))) {
					EmitByte(Rex(false, src, baseReg, indexReg));
				}
				break;
			case 2:
				// 16-bit: 0x66 operand size prefix + REX if extended registers
				EmitByte(0x66);
				if (NeedsRex(src) || NeedsRex(baseReg) || (indexReg != null && NeedsRex(indexReg.Value))) {
					EmitByte(Rex(false, src, baseReg, indexReg));
				}
				break;
			case 1:
				// 8-bit: only emit REX if extended registers are used (no W bit)
				if (NeedsRex(src) || NeedsRex(baseReg) || (indexReg != null && NeedsRex(indexReg.Value))) {
					EmitByte(Rex(false, src, baseReg, indexReg));
				}
				break;
			default:
				throw new NotSupportedException($"Unsupported operand size: {size}");
		}
	}

	private static byte Rex(bool w, X86Register reg, X86Register rm, X86Register? index = null) {
		int rex = 0x40;
		if (w) rex |= 0x08;
		if (NeedsRex(reg)) rex |= 0x04; // REX.R
		if (index != null && NeedsRex(index.Value)) rex |= 0x02; // REX.X
		if (NeedsRex(rm)) rex |= 0x01; // REX.B
		return (byte)rex;
	}

	private static byte SIB(int scale, byte index, byte baseReg) {
		byte scaleBits = scale switch {
			1 => 0b00,
			2 => 0b01,
			4 => 0b10,
			8 => 0b11,
			_ => 0b00
		};
		return (byte)((scaleBits << 6) | (index << 3) | baseReg);
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

	private void EmitTest(TestOp op) {
		if (op.Left is RegOperand leftReg && op.Right is RegOperand rightReg) {
			// TEST reg, reg: opcode 85
			EmitRexW(rightReg.Register, leftReg.Register);
			EmitByte(0x85);
			EmitByte(ModRM(0b11, GetRegCode(rightReg.Register), GetRegCode(leftReg.Register)));
		} else if (op.Left is RegOperand reg && op.Right is ImmOperand imm) {
			// TEST reg, imm32
			// For EAX/RAX, use short form: A9 id (TEST EAX, imm32) or REX.W A9 id (TEST RAX, imm32)
			// For other regs: F7 /0 (TEST r/m32, imm32) or REX.W F7 /0 (TEST r/m64, imm64)
			bool is32Bit = reg.Register == X86Register.EAX;
			bool is64Bit = reg.Register >= X86Register.RAX && reg.Register <= X86Register.R15;

			if (is32Bit) {
				// TEST EAX, imm32: A9 id
				EmitByte(0xA9);
				EmitImm32((int)imm.Value);
			} else if (is64Bit && (reg.Register == X86Register.RAX)) {
				// TEST RAX, imm32 (sign-extended): REX.W A9 id
				EmitByte(0x48); // REX.W
				EmitByte(0xA9);
				EmitImm32((int)imm.Value);
			} else {
				// TEST r/m, imm32: F7 /0
				if (NeedsRex(reg.Register)) {
					EmitByte((byte)(0x40 | (NeedsRex(reg.Register) ? 0x01 : 0)));
				}
				EmitByte(0xF7);
				EmitByte(ModRM(0b11, 0, GetRegCode(reg.Register)));
				EmitImm32((int)imm.Value);
			}
		}
	}

	private void EmitRegRegOp(X86Operand left, X86Operand right, byte opcode) {
		if (left is RegOperand leftReg && right is RegOperand rightReg) {
			EmitRexW(rightReg.Register, leftReg.Register);
			EmitByte(opcode);
			EmitByte(ModRM(0b11, GetRegCode(rightReg.Register), GetRegCode(leftReg.Register)));
		}
	}

	private void EmitSetcc(SetccOp op) {
		if (op.Dst is not RegOperand dst) return;
		// Set the low byte based on condition
		if (NeedsRex(dst.Register)) EmitByte(0x41); // REX.B for r8-r15
		EmitBytes(0x0F, GetSetccOpcode(op.Condition));
		EmitByte(ModRM(0b11, 0, GetRegCode(dst.Register)));
		// Zero-extend byte to 64-bit with MOVZX r64, r8
		EmitRexW(dst.Register, dst.Register);
		EmitBytes(0x0F, 0xB6); // MOVZX r64, r/m8
		EmitByte(ModRM(0b11, GetRegCode(dst.Register), GetRegCode(dst.Register)));
	}

	private void EmitJmp(JmpOp op) {
		EmitByte(0xE9);
		_labelFixups.Add((CurrentOffset, ScopedLabel(op.Target), 4));
		EmitImm32(0); // Placeholder
	}

	private void EmitJz(JzOp op) {
		// JZ rel32 (jump if zero/equal): 0F 84 rel32
		EmitBytes(0x0F, 0x84);
		_labelFixups.Add((CurrentOffset, ScopedLabel(op.Target), 4));
		EmitImm32(0); // Placeholder
	}

	private void EmitJcc(JccOp op) {
		EmitBytes(0x0F, GetJccOpcode(op.Condition));
		_labelFixups.Add((CurrentOffset, ScopedLabel(op.TrueTarget), 4));
		EmitImm32(0);

		// Emit fall-through jump
		EmitByte(0xE9);
		_labelFixups.Add((CurrentOffset, ScopedLabel(op.FalseTarget), 4));
		EmitImm32(0);
	}

	private void EmitCall(X86CallOp op) {
		if (op.IsExternal) {
			// External call: use indirect call through IAT
			// CALL [RIP + disp32] = FF 15 disp32
			EmitByte(0xFF);
			EmitByte(0x15);

			// Track this import
			if (!_importIndices.TryGetValue(op.Target, out var importIndex)) {
				importIndex = _imports.Count;
				_imports.Add(new ImportEntry(op.DllName ?? "kernel32.dll", op.Target));
				_importIndices[op.Target] = importIndex;
			}

			// Record fixup for the displacement (will be resolved when IAT address is known)
			_importFixups.Add((CurrentOffset, importIndex, 4));
			EmitImm32(0);  // Placeholder
		} else {
			// Internal call: direct relative call
			EmitByte(0xE8);
			_labelFixups.Add((CurrentOffset, op.Target, 4));
			EmitImm32(0);
		}
	}

	private void EmitRet() {
		EmitByte(0xC3);
	}

	private void EmitCld() {
		// cld - clear direction flag (opcode: FC)
		EmitByte(0xFC);
	}

	private void EmitStd() {
		// std - set direction flag (opcode: FD)
		EmitByte(0xFD);
	}

	private void EmitRepMovsb() {
		// rep movsb - copy RCX bytes from [RSI] to [RDI] (opcode: F3 A4)
		EmitByte(0xF3);
		EmitByte(0xA4);
	}

	private void EmitPush(PushOp op) {
		if (op.Src is RegOperand reg) {
			EmitPushReg(reg.Register);
		}
	}

	private void EmitPushReg(X86Register reg) {
		if (NeedsRex(reg)) EmitByte(0x41);
		EmitByte((byte)(0x50 + GetRegCode(reg)));
	}

	private void EmitPop(PopOp op) {
		if (op.Dst is RegOperand reg) {
			EmitPopReg(reg.Register);
		}
	}

	private void EmitPopReg(X86Register reg) {
		if (NeedsRex(reg)) EmitByte(0x41);
		EmitByte((byte)(0x58 + GetRegCode(reg)));
	}

	private void EmitCdq() {
		EmitByte(0x99);
	}

	private void EmitLea(LeaOp op) {
		if (op.Dst is RegOperand dst && op.Src is MemOperand src && src.Base is RegOperand baseReg) {
			if (src.Index is RegOperand indexReg) {
				// LEA with SIB: [base + index*scale + disp]
				EmitRexW(dst.Register, baseReg.Register, indexReg.Register);
				EmitByte(0x8D);
				EmitByte(ModRM(0b10, GetRegCode(dst.Register), 0b100)); // rm=4 means SIB follows
				EmitByte(SIB(src.Scale, GetRegCode(indexReg.Register), GetRegCode(baseReg.Register)));
				EmitImm32(src.Displacement);
			} else {
				// LEA without index: [base + disp]
				EmitRexW(dst.Register, baseReg.Register);
				EmitByte(0x8D);
				EmitByte(ModRM(0b10, GetRegCode(dst.Register), GetRegCode(baseReg.Register)));
				if (GetRegCode(baseReg.Register) == 4) EmitByte(0x24); // SIB for RSP
				EmitImm32(src.Displacement);
			}
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

	private void EmitLeaRdata(LeaRdataOp op) {
		if (op.Dst is RegOperand dst) {
			// LEA with RIP-relative addressing: LEA reg, [RIP + disp32]
			// REX.W + 8D /r with ModR/M byte indicating RIP-relative (mod=00, rm=101)
			EmitRexW(dst.Register);
			EmitByte(0x8D);
			EmitByte(ModRM(0b00, GetRegCode(dst.Register), 0b101)); // RIP-relative

			// Record fixup for the displacement (will be resolved when we know rdata section offset)
			_rdataFixups.Add((CurrentOffset, op.Label, 4)); // 4 bytes for disp32
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
		// F2 [REX] 0F 10 /r - MOVSD xmm1, xmm2/m64 (load)
		// F2 [REX] 0F 11 /r - MOVSD xmm1/m64, xmm2 (store)
		// REX.R extends the reg field (XMM8-XMM15)
		// REX.B extends the r/m field (base register R8-R15 or XMM8-XMM15)
		if (op.Dst is RegOperand dst && op.Src is RegOperand src) {
			// XMM to XMM move
			EmitByte(0xF2);
			EmitRexIfNeeded(dst.Register, src.Register);
			EmitBytes(0x0F, 0x10);
			EmitByte(ModRM(0b11, GetRegCode(dst.Register), GetRegCode(src.Register)));
		} else if (op.Dst is RegOperand dstReg && op.Src is MemOperand srcMem) {
			// Memory to XMM (load)
			var baseReg = (srcMem.Base as RegOperand)?.Register;
			EmitByte(0xF2);
			EmitRexIfNeeded(dstReg.Register, baseReg);
			EmitBytes(0x0F, 0x10);
			EmitMemOperand(dstReg.Register, srcMem);
		} else if (op.Dst is MemOperand dstMem && op.Src is RegOperand srcReg) {
			// XMM to memory (store)
			var baseReg = (dstMem.Base as RegOperand)?.Register;
			EmitByte(0xF2);
			EmitRexIfNeeded(srcReg.Register, baseReg);
			EmitBytes(0x0F, 0x11);
			EmitMemOperand(srcReg.Register, dstMem);
		} else {
			throw new NotSupportedException($"Unsupported MOVSD operand combination");
		}
	}

	/// <summary>
	/// Emit REX prefix only if needed (when using extended registers).
	/// For SSE instructions, REX.R extends the reg field and REX.B extends the r/m field.
	/// </summary>
	private void EmitRexIfNeeded(X86Register? reg = null, X86Register? rm = null) {
		bool needsRex = (reg.HasValue && NeedsRex(reg.Value)) ||
						(rm.HasValue && NeedsRex(rm.Value));
		if (needsRex) {
			byte rex = 0x40;
			if (reg.HasValue && NeedsRex(reg.Value)) rex |= 0x04; // REX.R
			if (rm.HasValue && NeedsRex(rm.Value)) rex |= 0x01; // REX.B
			EmitByte(rex);
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
		// reg field = dst, rm field = src
		if (dst is RegOperand dstReg && src is RegOperand srcReg) {
			EmitByte(0xF2);
			EmitRexW(dstReg.Register, srcReg.Register);
			EmitBytes(0x0F, opcode);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
		} else {
			throw new NotSupportedException($"Unsupported {name} operand combination");
		}
	}

	private void EmitMovq(MovqOp op) {
		// 66 REX.W 0F 6E /r - MOVQ xmm, r/m64
		// Move 64-bit value from GPR to XMM register
		// reg field = dst (XMM), rm field = src (GPR)
		if (op.Dst is RegOperand dstReg && op.Src is RegOperand srcReg) {
			EmitByte(0x66);
			EmitRexW(dstReg.Register, srcReg.Register);
			EmitBytes(0x0F, 0x6E);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
		} else {
			throw new NotSupportedException($"Unsupported MOVQ operand combination: {op.Dst?.GetType().Name} <- {op.Src?.GetType().Name}");
		}
	}

	private void EmitComisd(ComiOp op) {
		// 66 [REX] 0F 2F /r - COMISD xmm1, xmm2/m64
		// Compares two doubles and sets EFLAGS (ZF, PF, CF)
		if (op.Left is RegOperand left && op.Right is RegOperand right) {
			EmitByte(0x66);
			EmitRexIfNeeded(left.Register, right.Register);
			EmitBytes(0x0F, 0x2F);
			EmitByte(ModRM(0b11, GetRegCode(left.Register), GetRegCode(right.Register)));
		} else if (op.Left is RegOperand leftReg && op.Right is MemOperand rightMem) {
			var baseReg = (rightMem.Base as RegOperand)?.Register;
			EmitByte(0x66);
			EmitRexIfNeeded(leftReg.Register, baseReg);
			EmitBytes(0x0F, 0x2F);
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
		// 66 [REX] 0F 3A 0B /r imm8 - ROUNDSD xmm1, xmm2/m64, imm8
		// imm8: 0x08 = nearest, 0x09 = floor, 0x0A = ceil, 0x0B = truncate
		if (op.Dst is RegOperand dstReg && op.Src is RegOperand srcReg) {
			EmitByte(0x66);
			EmitRexIfNeeded(dstReg.Register, srcReg.Register);
			EmitBytes(0x0F, 0x3A, 0x0B);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
			EmitByte((byte)op.Mode);
		} else if (op.Dst is RegOperand dstRegOp && op.Src is MemOperand srcMem) {
			var baseReg = (srcMem.Base as RegOperand)?.Register;
			EmitByte(0x66);
			EmitRexIfNeeded(dstRegOp.Register, baseReg);
			EmitBytes(0x0F, 0x3A, 0x0B);
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
		// 66 [REX] 0F 54 /r - ANDPD xmm1, xmm2/m128
		// Bitwise AND of packed doubles
		if (op.Dst is RegOperand dstReg && op.Src is RegOperand srcReg) {
			EmitByte(0x66);
			EmitRexIfNeeded(dstReg.Register, srcReg.Register);
			EmitBytes(0x0F, 0x54);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
		} else if (op.Dst is RegOperand dstRegOp && op.Src is MemOperand srcMem) {
			var baseReg = (srcMem.Base as RegOperand)?.Register;
			EmitByte(0x66);
			EmitRexIfNeeded(dstRegOp.Register, baseReg);
			EmitBytes(0x0F, 0x54);
			EmitMemOperand(dstRegOp.Register, srcMem);
		} else {
			throw new NotSupportedException($"Unsupported ANDPD operand combination");
		}
	}

	private void EmitXorpd(XorpdOp op) {
		// 66 [REX] 0F 57 /r - XORPD xmm1, xmm2/m128
		// Bitwise XOR of packed doubles
		if (op.Dst is RegOperand dstReg && op.Src is RegOperand srcReg) {
			EmitByte(0x66);
			EmitRexIfNeeded(dstReg.Register, srcReg.Register);
			EmitBytes(0x0F, 0x57);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
		} else if (op.Dst is RegOperand dstRegOp && op.Src is MemOperand srcMem) {
			var baseReg = (srcMem.Base as RegOperand)?.Register;
			EmitByte(0x66);
			EmitRexIfNeeded(dstRegOp.Register, baseReg);
			EmitBytes(0x0F, 0x57);
			EmitMemOperand(dstRegOp.Register, srcMem);
		} else {
			throw new NotSupportedException($"Unsupported XORPD operand combination");
		}
	}

	private void EmitSseArith(byte opcode, X86Operand dst, X86Operand src) {
		// F2 [REX] 0F opcode /r
		if (dst is RegOperand dstReg && src is RegOperand srcReg) {
			EmitByte(0xF2);
			EmitRexIfNeeded(dstReg.Register, srcReg.Register);
			EmitBytes(0x0F, opcode);
			EmitByte(ModRM(0b11, GetRegCode(dstReg.Register), GetRegCode(srcReg.Register)));
		} else if (dst is RegOperand dstRegOp && src is MemOperand srcMem) {
			var baseReg = (srcMem.Base as RegOperand)?.Register;
			EmitByte(0xF2);
			EmitRexIfNeeded(dstRegOp.Register, baseReg);
			EmitBytes(0x0F, opcode);
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
