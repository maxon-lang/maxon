using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Runtime;

namespace MaxonSharp.Compiler.Ir;

public partial class X86CodeEmitter {

  /// <summary>
  /// Maps VRegs to x86-64 physical registers (Windows x64 calling convention).
  /// Scratch3 uses RBX (callee-saved); FunctionStart/End in X86EmitterBackend save and restore it.
  /// </summary>
  private static X86Register MapVReg(VReg v) => v switch {
    VReg.Arg0 => X86Register.Rcx,
    VReg.Arg1 => X86Register.Rdx,
    VReg.Arg2 => X86Register.R8,
    VReg.Arg3 => X86Register.R9,
    VReg.Arg4 => X86Register.Rsi,
    VReg.Arg5 => X86Register.Rdi,
    VReg.Scratch0 => X86Register.Rax,  // Also VReg.Ret
    VReg.Scratch1 => X86Register.R10,
    VReg.Scratch2 => X86Register.R11,
    VReg.Scratch3 => X86Register.Rbx,  // Callee-saved; saved/restored in FunctionStart/End
    _ => throw new ArgumentException($"Unmapped VReg: {v}")
  };

  /// <summary>
  /// IEmitterBackend implementation that delegates to X86CodeEmitter's private emit methods.
  /// Nested class so it can access all private members.
  /// </summary>
  public class X86EmitterBackend(X86CodeEmitter emitter) : IEmitterBackend {
    private readonly X86CodeEmitter _e = emitter;
    private int _backendLabelCounter;

    public bool IsWindows => true;
    public bool IsMacOS => false;

    private string BackendLabel(string prefix) => $"__be_{prefix}_{_backendLabelCounter++}";
    private static X86Register R(VReg v) => MapVReg(v);

    // ---- Function structure ----

    public void FunctionStart(string name, int argCount, int frameSize) {
      _e.DefineLabel(name);
      _e._runtimeFunctionLabels.Add(name);
      _e.EmitPushReg(X86Register.Rbp);
      _e.EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
      _e.EmitSubRegImm(X86Register.Rsp, frameSize);
      // Save callee-saved registers below the frame. PUSH modifies RSP only —
      // the named slots at [rbp-(i+1)*8] are unaffected since they are RBP-relative.
      // On Windows x64, RBX/RSI/RDI are callee-saved. RSI and RDI are used as
      // Arg4/Arg5 and also clobbered by REP MOVSB in string conversion helpers.
      _e.EmitPushReg(X86Register.Rbx);
      _e.EmitPushReg(X86Register.Rsi);
      _e.EmitPushReg(X86Register.Rdi);
      for (int i = 0; i < argCount; i++)
        _e.EmitMovMemReg(-(i + 1) * 0x08, _abiArgRegs[i], 8);
    }

    public void FunctionEnd() {
      // Restore callee-saved registers in reverse order, then tear down the frame.
      // The POPs advance RSP back to [RBP - frameSize], and MOV RSP,RBP resets it
      // to the saved RBP position so POP RBP and RET work correctly.
      _e.EmitPopReg(X86Register.Rdi);
      _e.EmitPopReg(X86Register.Rsi);
      _e.EmitPopReg(X86Register.Rbx);
      _e.EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
      _e.EmitPopReg(X86Register.Rbp);
      _e.EmitByte(0xC3); // ret
    }

    public void ReturnValue(VReg src) {
      // On x86, return value goes in RAX
      if (R(src) != X86Register.Rax)
        _e.EmitMovRegReg(X86Register.Rax, R(src));
      FunctionEnd();
    }

    // ---- Register operations ----

    public void MovRegReg(VReg dest, VReg src) => _e.EmitMovRegReg(R(dest), R(src));
    public void MovRegImm(VReg dest, long imm) => _e.EmitMovRegImm(R(dest), imm);
    public void ZeroReg(VReg reg) => _e.EmitXorRegReg(R(reg), R(reg));

    // ---- Memory: local stack frame ----
    // Slot 0 = [rbp-0x08] (first arg), slot 1 = [rbp-0x10], etc.
    // Negative slots for scratch: slot -1 = [rbp-0x00]... not useful.
    // We use the convention: slot N maps to displacement -(N+1)*8.

    public void LoadLocal(VReg dest, int slotIndex) =>
      _e.EmitMovRegMem(R(dest), -(slotIndex + 1) * 0x08, 8);

    public void StoreLocal(int slotIndex, VReg src) =>
      _e.EmitMovMemReg(-(slotIndex + 1) * 0x08, R(src), 8);

    // ---- Memory: indirect ----

    public void LoadIndirect(VReg dest, VReg baseReg, int offset) =>
      _e.EmitMovRegIndirectMem(R(dest), R(baseReg), offset);

    public void StoreIndirect(VReg baseReg, int offset, VReg src) =>
      _e.EmitMovIndirectMemReg(R(baseReg), offset, R(src));

    // ---- Globals ----

    public void LoadGlobal(VReg dest, string globalLabel) =>
      _e.EmitGlobalLoadReg(R(dest), globalLabel);

    public void StoreGlobal(string globalLabel, VReg src) =>
      _e.EmitGlobalStoreReg(R(src), globalLabel);

    public void LeaGlobal(VReg dest, string globalLabel) =>
      _e.EmitGlobalLeaReg(R(dest), globalLabel);

    public void LeaSymdata(VReg dest, string symdataLabel) =>
      _e.EmitLeaRegSymdataRel(R(dest), symdataLabel);

    // ---- Arithmetic ----

    public void AddRegImm(VReg dest, long imm) => _e.EmitAddRegImm(R(dest), imm);
    public void SubRegImm(VReg dest, long imm) => _e.EmitSubRegImm(R(dest), imm);
    public void AddRegReg(VReg dest, VReg src) => _e.EmitAddRegReg(R(dest), R(src));
    public void SubRegReg(VReg dest, VReg src) => _e.EmitSubRegReg(R(dest), R(src));
    public void MulRegReg(VReg dest, VReg src) => _e.EmitImulRegReg(R(dest), R(src));
    public void ShlRegImm(VReg dest, int shift) => _e.EmitShlRegImm(R(dest), (byte)shift);
    public void ShrRegImm(VReg dest, int shift) => _e.EmitShrRegImm(R(dest), (byte)shift);
    public void ShrRegReg(VReg dest, VReg count) {
      if (R(count) != X86Register.Rcx) _e.EmitMovRegReg(X86Register.Rcx, R(count));
      _e.EmitShrRegCl(R(dest));
    }
    public void ShlRegReg(VReg dest, VReg count) {
      if (R(count) != X86Register.Rcx) _e.EmitMovRegReg(X86Register.Rcx, R(count));
      _e.EmitShlRegCl(R(dest));
    }
    public void AndRegReg(VReg dest, VReg src) => _e.EmitAndRegReg(R(dest), R(src));
    public void OrRegReg(VReg dest, VReg src) => _e.EmitOrRegReg(R(dest), R(src));
    public void XorRegReg(VReg dest, VReg src) => _e.EmitXorRegReg(R(dest), R(src));

    // ---- Bit manipulation ----

    public void BitScanForward(VReg dest, VReg src) => _e.EmitBsfRegReg(R(dest), R(src));
    public void BitTestAndReset(VReg baseReg, int offset, VReg bitIndex) =>
      _e.EmitBtrMemReg(R(baseReg), offset, R(bitIndex));
    public void BitTestAndSet(VReg baseReg, int offset, VReg bitIndex) =>
      _e.EmitBtsMemReg(R(baseReg), offset, R(bitIndex));

    // ---- Comparison & branching ----

    public void CmpRegReg(VReg left, VReg right) => _e.EmitCmpRegReg(R(left), R(right));
    public void CmpRegImm(VReg reg, long imm) => _e.EmitCmpRegImm(R(reg), imm);
    public void TestRegReg(VReg left, VReg right) => _e.EmitTestRegReg(R(left), R(right));
    public void Jump(string label) => _e.EmitJmp(label);

    public void JumpIf(Condition cond, string label) {
      var cc = cond switch {
        Condition.Equal => "z",
        Condition.NotEqual => "nz",
        Condition.Less => "l",
        Condition.LessEqual => "le",
        Condition.Greater => "g",
        Condition.GreaterEqual => "ge",
        Condition.Above => "a",
        Condition.Below => "b",
        Condition.AboveEqual => "ae",
        Condition.BelowEqual => "be",
        _ => throw new ArgumentException($"Unknown condition: {cond}")
      };
      _e.EmitJcc(cc, label);
    }

    public void JumpIfZero(VReg reg, string label) {
      _e.EmitTestRegReg(R(reg), R(reg));
      _e.EmitJcc("z", label);
    }

    public void JumpIfNonZero(VReg reg, string label) {
      _e.EmitTestRegReg(R(reg), R(reg));
      _e.EmitJcc("nz", label);
    }

    // ---- Calls ----

    public void Call(string label) {
      _e.EmitByte(0xE8);
      _e._relCallFixups.Add((_e._code.Count, label));
      _e.EmitDword(0);
    }

    public void CallImport(string function) {
      // Platform-neutral function names → Windows API mapping
      var (dll, func) = ResolveImport(function);
      _e.EmitCallImportOnSystemStack(dll, func);
    }

    public void CallImportOnSystemStack(string function) {
      var (dll, func) = ResolveImport(function);
      _e.EmitCallImportOnSystemStack(dll, func);
    }

    public void CallIndirect(VReg target) {
      // CALL reg: FF /2 (opcode extension 2 in ModRM.reg)
      var reg = R(target);
      // REX prefix if needed + FF D0+reg
      if (reg >= X86Register.R8)
        _e.EmitByte(0x41); // REX.B
      _e.EmitBytes(0xFF, (byte)(0xD0 + ((int)reg & 7)));
    }

    // ---- Atomics ----

    public void AtomicInc(VReg baseAddr, int offset) {
      // LOCK INC qword [reg + offset]
      var reg = R(baseAddr);
      _e.EmitByte(0xF0); // LOCK prefix
      if (offset >= -128 && offset <= 127) {
        // REX.W + FF /0 [reg+disp8]
        if (reg >= X86Register.R8)
          _e.EmitBytes(0x49, 0xFF, (byte)(0x40 + ((int)reg & 7)), (byte)offset);
        else
          _e.EmitBytes(0x48, 0xFF, (byte)(0x40 + ((int)reg & 7)), (byte)offset);
      } else {
        throw new NotImplementedException("AtomicInc with large offset not yet implemented");
      }
    }

    public void AtomicDec(VReg baseAddr, int offset) {
      // LOCK DEC qword [reg + offset]
      var reg = R(baseAddr);
      _e.EmitByte(0xF0); // LOCK prefix
      if (offset >= -128 && offset <= 127) {
        // REX.W + FF /1 [reg+disp8]
        if (reg >= X86Register.R8)
          _e.EmitBytes(0x49, 0xFF, (byte)(0x48 + ((int)reg & 7)), (byte)offset);
        else
          _e.EmitBytes(0x48, 0xFF, (byte)(0x48 + ((int)reg & 7)), (byte)offset);
      } else {
        throw new NotImplementedException("AtomicDec with large offset not yet implemented");
      }
    }

    public void AtomicXadd(VReg baseAddr, int offset, VReg val) {
      // LOCK XADD [baseAddr + offset], val
      // For now, delegate to existing code pattern
      var baseReg = R(baseAddr);
      var valReg = R(val);
      _e.EmitByte(0xF0); // LOCK
      // REX.W + 0F C1 /r [base+disp8]
      byte rex = 0x48;
      if (baseReg >= X86Register.R8) rex |= 0x01; // REX.B
      if (valReg >= X86Register.R8) rex |= 0x04; // REX.R
      _e.EmitByte(rex);
      _e.EmitBytes(0x0F, 0xC1);
      _e.EmitByte((byte)(0x40 | (((int)valReg & 7) << 3) | ((int)baseReg & 7)));
      _e.EmitByte((byte)offset);
    }

    public void FullBarrier() => _e.EmitMfence();

    // ---- Labels & data ----

    public void DefineLabel(string label) => _e.DefineLabel(label);
    public void DefineGlobal(string label, int size, long initValue) =>
      _e.DefineGlobal(label, size, initValue);
    public void DefineSymdata(string label, byte[] data) => _e.DefineSymdata(label, data);

    // ---- Locking ----

    public void LockAcquire(string lockGlobal) {
      _e.EmitGlobalLeaReg(X86Register.Rcx, lockGlobal);
      _e.EmitCallImportOnSystemStack("kernel32.dll", "EnterCriticalSection");
    }

    public void LockRelease(string lockGlobal) {
      _e.EmitGlobalLeaReg(X86Register.Rcx, lockGlobal);
      _e.EmitCallImportOnSystemStack("kernel32.dll", "LeaveCriticalSection");
    }

    // ---- TLS ----

    public void LoadCurrentP(VReg dest) {
      // Load P* from TEB via precomputed GS-segment offset.
      // Load the offset into R11, then dereference GS:[R11] to get P*.
      // Uses R11 as scratch regardless of dest so the GS dereference is always
      // encoded as MOV R11, GS:[R11] (REX.W+REX.R+REX.B = 0x4F, 0x8B, 0x1B).
      _e.EmitGlobalLoadReg(X86Register.R11, "__sched_tls_teb_offset");
      _e.EmitByte(0x65); // GS segment override prefix
      _e.EmitMovRegIndirectMemRaw(X86Register.R11, X86Register.R11, 0); // R11 = P*
      var reg = R(dest);
      if (reg != X86Register.R11)
        _e.EmitMovRegReg(reg, X86Register.R11);
    }

    // ---- OS memory allocation ----

    public void OsAllocPages(VReg dest, VReg size) {
      // VirtualAlloc(NULL, size, MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE)
      // Move size out of the way first (VirtualAlloc takes it as arg1=RDX)
      var sizeReg = R(size);
      _e.EmitMovRegReg(X86Register.Rdx, sizeReg);
      _e.EmitXorRegReg(X86Register.Rcx, X86Register.Rcx); // lpAddress = NULL
      // RDX already = size
      _e.EmitMovRegImm(X86Register.R8, 0x3000); // MEM_COMMIT | MEM_RESERVE
      _e.EmitMovRegImm(X86Register.R9, 0x04);   // PAGE_READWRITE
      _e.EmitCallImportOnSystemStack("kernel32.dll", "VirtualAlloc");
      // Result in RAX; move to dest
      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    public void OsAllocLargePages(VReg dest, VReg size) {
      // VirtualAlloc(NULL, size, MEM_COMMIT|MEM_RESERVE|MEM_LARGE_PAGES, PAGE_READWRITE)
      // MEM_LARGE_PAGES = 0x20000000; combined flags = 0x3000 | 0x20000000 = 0x20003000
      // Returns NULL if SeLockMemoryPrivilege is not held — caller must check and fall back.
      var sizeReg = R(size);
      _e.EmitMovRegReg(X86Register.Rdx, sizeReg);
      _e.EmitXorRegReg(X86Register.Rcx, X86Register.Rcx); // lpAddress = NULL
      _e.EmitMovRegImm(X86Register.R8, 0x20003000);        // MEM_COMMIT|MEM_RESERVE|MEM_LARGE_PAGES
      _e.EmitMovRegImm(X86Register.R9, 0x04);              // PAGE_READWRITE
      _e.EmitCallImportOnSystemStack("kernel32.dll", "VirtualAlloc");
      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    public void OsFreePages(VReg ptr, VReg size) {
      // VirtualFree(ptr, 0, MEM_RELEASE) — size is ignored
      _e.EmitMovRegReg(X86Register.Rcx, R(ptr));
      _e.EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // dwSize = 0
      _e.EmitMovRegImm(X86Register.R8, 0x8000);            // MEM_RELEASE
      _e.EmitCallImportOnSystemStack("kernel32.dll", "VirtualFree");
    }

    // ---- Shared memory (debugstream) ----

    public void OsOpenAndMapSharedMemory(VReg dest, VReg name_ptr, VReg size) {
      // Save size in RBX (callee-saved) across API calls
      _e.EmitMovRegReg(X86Register.Rbx, R(size));

      // Step 1: OpenFileMappingA(FILE_MAP_ALL_ACCESS, FALSE, name)
      // Move name_ptr to R8 first since name_ptr might be RCX (Arg0)
      _e.EmitMovRegReg(X86Register.R8, R(name_ptr));            // lpName
      _e.EmitMovRegImm(X86Register.Rcx, 0xF001F);              // FILE_MAP_ALL_ACCESS
      _e.EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);      // bInheritHandle = FALSE
      _e.EmitCallImportOnSystemStack("kernel32.dll", "OpenFileMappingA");

      var failLabel = BackendLabel("ds_shm_fail");
      var doneLabel = BackendLabel("ds_shm_done");
      _e.EmitTestRegReg(X86Register.Rax, X86Register.Rax);
      _e.EmitJcc("z", failLabel);

      // Step 2: MapViewOfFile(handle, FILE_MAP_ALL_ACCESS, 0, 0, size)
      _e.EmitMovRegReg(X86Register.Rcx, X86Register.Rax);      // hFileMappingObject
      _e.EmitMovRegImm(X86Register.Rdx, 0xF001F);              // FILE_MAP_ALL_ACCESS
      _e.EmitXorRegReg(X86Register.R8, X86Register.R8);        // dwFileOffsetHigh = 0
      _e.EmitXorRegReg(X86Register.R9, X86Register.R9);        // dwFileOffsetLow = 0
      _e.EmitMovIndirectMemReg(X86Register.Rsp, 0x20, X86Register.Rbx); // [rsp+0x20] = size
      _e.EmitCallImportOnSystemStack("kernel32.dll", "MapViewOfFile");
      _e.EmitJmp(doneLabel);

      _e.DefineLabel(failLabel);
      _e.EmitXorRegReg(X86Register.Rax, X86Register.Rax);

      _e.DefineLabel(doneLabel);
      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    public void OsUnmapSharedMemory(VReg base_ptr, VReg size) {
      // UnmapViewOfFile(lpBaseAddress)
      _e.EmitMovRegReg(X86Register.Rcx, R(base_ptr));
      _e.EmitCallImportOnSystemStack("kernel32.dll", "UnmapViewOfFile");
    }

    // ---- Bulk memory ----

    public void FillMemoryQwords(VReg destAddr, VReg value, VReg count) {
      // REP STOSQ: RAX=value, RCX=count, RDI=dest
      if (R(value) != X86Register.Rax) _e.EmitMovRegReg(X86Register.Rax, R(value));
      if (R(count) != X86Register.Rcx) _e.EmitMovRegReg(X86Register.Rcx, R(count));
      if (R(destAddr) != X86Register.Rdi) _e.EmitMovRegReg(X86Register.Rdi, R(destAddr));
      _e.EmitBytes(0xF3, 0x48, 0xAB); // REP STOSQ
    }

    // ---- Scheduler platform helpers ----

    public void GetCurrentTimeMs(VReg dest, int scratchSlot) {
      // GetTickCount64() returns milliseconds since boot
      _e.EmitCallImportOnSystemStack("kernel32.dll", "GetTickCount64");
      // Result in RAX; move to dest if needed
      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    public void WakeWorker(VReg p) {
      // SetEvent(p->wakeEvent); POffWakeEvent = 0x38
      _e.EmitMovRegIndirectMem(X86Register.Rcx, R(p), 0x38); // rcx = p->wakeEvent
      _e.EmitCallImportOnSystemStack("kernel32.dll", "SetEvent");
    }

    public void SpawnWorker(VReg p) {
      // CreateThread(NULL, 0, __sched_worker_loop, P[i], 0, NULL)
      // R10 is volatile on Windows x64 and clobbered by CreateThread on the
      // main-thread path of SystemStackEnter/Leave. Save P[i] to the caller's
      // stack frame via RBP, which is callee-saved and guaranteed valid.
      // We use [rbp-0x30] as a scratch slot (caller must ensure frame >= 0x30).
      var pReg = R(p);
      _e.EmitMovMemReg(-0x30, pReg, 8); // save P[i] to [rbp-0x30]
      _e.EmitMovRegReg(X86Register.R10, pReg); // R10 = P[i] for lpParameter
      // Switch to system stack and set up 6 args for CreateThread
      _e.EmitSystemStackEnter(0x30); // shadow(0x20) + 2 stack args(0x10) = 0x30
      _e.EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);     // lpThreadAttributes = NULL
      _e.EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);     // dwStackSize = 0
      // LEA R8, [rip + __sched_worker_loop]
      _e.EmitByte(0x4C); _e.EmitByte(0x8D); _e.EmitByte(0x05);
      _e._jumpFixups.Add((_e._code.Count, "__sched_worker_loop"));
      _e.EmitDword(0);
      _e.EmitMovRegReg(X86Register.R9, X86Register.R10);       // lpParameter = P[i]
      // Args 5 and 6 on stack: [rsp+0x20] = dwCreationFlags=0, [rsp+0x28] = lpThreadId=NULL
      _e.EmitXorRegReg(X86Register.Rax, X86Register.Rax);
      _e.EmitMovMemRspReg(0x20, X86Register.Rax); // dwCreationFlags = 0
      _e.EmitMovMemRspReg(0x28, X86Register.Rax); // lpThreadId = NULL
      _e.EmitCallImport("kernel32.dll", "CreateThread");
      _e.EmitSystemStackLeave(0x30);
      // Reload P[i] from stack frame (R10 may have been clobbered on main-thread path)
      _e.EmitMovRegMem(X86Register.R10, -0x30, 8);
      // Store thread handle in P[i]->osThreadHandle (RAX has the handle)
      _e.EmitMovIndirectMemReg(X86Register.R10, 0x40, X86Register.Rax); // POffOsThreadHandle = 0x40
    }

    public void UDivRemainder(VReg dest, VReg dividend, long divisor) {
      // dest = dividend % divisor (unsigned)
      // DIV instruction: RDX:RAX / RCX → RAX=quotient, RDX=remainder
      var divReg = R(dividend);
      if (divReg != X86Register.Rax)
        _e.EmitMovRegReg(X86Register.Rax, divReg);
      _e.EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
      _e.EmitMovRegImm(X86Register.Rcx, divisor);
      _e.EmitBytes(0x48, 0xF7, 0xF1); // DIV RCX
      var destReg = R(dest);
      if (destReg != X86Register.Rdx)
        _e.EmitMovRegReg(destReg, X86Register.Rdx);
    }

    public void UDivRemainderReg(VReg dest, VReg dividend, VReg divisor) {
      // dest = dividend % divisor (unsigned, register divisor)
      // DIV instruction: RDX:RAX / src → RAX=quotient, RDX=remainder
      var divReg = R(dividend);
      if (divReg != X86Register.Rax)
        _e.EmitMovRegReg(X86Register.Rax, divReg);
      _e.EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
      // DIV r/m64: REX.W F7 /6
      var srcReg = R(divisor);
      byte rex = 0x48;
      if (srcReg >= X86Register.R8) rex |= 0x01; // REX.B
      _e.EmitByte(rex);
      _e.EmitBytes(0xF7, (byte)(0xF0 + ((int)srcReg & 7))); // DIV srcReg
      var destReg = R(dest);
      if (destReg != X86Register.Rdx)
        _e.EmitMovRegReg(destReg, X86Register.Rdx);
    }

    // ---- Platform-specific labels ----

    public string WriteStderrLabel => "maxon_write_stderr";

    // ---- Local address / byte memory ----

    public void LeaLocal(VReg dest, int slotIndex) {
      // LEA R(dest), [RBP + -(slotIndex+1)*8]
      _e.EmitLeaRegMem(R(dest), -(slotIndex + 1) * 8);
    }

    public void StoreIndirectByte(VReg baseReg, int offset, VReg src) {
      // MOV BYTE [R(base) + offset], R(src) low byte
      // REX + 88 /r [base + disp8]
      var baseReg_ = R(baseReg);
      var srcReg_ = R(src);
      byte rex = 0x40;
      if (baseReg_ >= X86Register.R8) rex |= 0x01; // REX.B
      if (srcReg_ >= X86Register.R8) rex |= 0x04;  // REX.R
      // Without REX, mod/rm encodings 4..7 select AH/CH/DH/BH instead of SPL/BPL/SIL/DIL.
      // REX must be emitted whenever src is RSP/RBP/RSI/RDI to reach their low bytes.
      bool needRex = rex != 0x40 || (srcReg_ >= X86Register.Rsp && srcReg_ <= X86Register.Rdi);
      if (needRex) _e.EmitByte(rex);
      // 88 /r ModRM(01, reg, base) + disp8
      _e.EmitByte(0x88);
      _e.EmitByte((byte)(0x40 | (((int)srcReg_ & 7) << 3) | ((int)baseReg_ & 7)));
      _e.EmitByte((byte)offset);
    }

    public void LoadIndirectByte(VReg dest, VReg baseReg, int offset) {
      // MOVZX R(dest), BYTE [R(base) + offset]
      // REX.W + 0F B6 /r ModRM(01, dest, base) + disp8
      var destReg = R(dest);
      var baseReg_ = R(baseReg);
      byte rex = 0x48;
      if (destReg >= X86Register.R8) rex |= 0x04; // REX.R
      if (baseReg_ >= X86Register.R8) rex |= 0x01; // REX.B
      _e.EmitByte(rex);
      _e.EmitBytes(0x0F, 0xB6);
      _e.EmitByte((byte)(0x40 | (((int)destReg & 7) << 3) | ((int)baseReg_ & 7)));
      _e.EmitByte((byte)offset);
    }

    // ---- Platform info ----

    public string SchedLockLabel => "__sched_global_queue_cs";
    public string TimerLockLabel => "__gt_timer_cs";

    // ---- Import resolution ----

    private static (string dll, string func) ResolveImport(string function) => function switch {
      "os_alloc_pages" => ("kernel32.dll", "VirtualAlloc"),
      "os_free_pages" => ("kernel32.dll", "VirtualFree"),
      "os_write_stdout" => ("kernel32.dll", "WriteFile"),
      "os_write_stderr" => ("kernel32.dll", "WriteFile"),
      "os_exit" => ("kernel32.dll", "ExitProcess"),
      _ => ("kernel32.dll", function) // fallback: assume kernel32
    };
  }

  /// <summary>Create the IEmitterBackend for this X86CodeEmitter.</summary>
  public IEmitterBackend CreateBackend() => new X86EmitterBackend(this);
}
