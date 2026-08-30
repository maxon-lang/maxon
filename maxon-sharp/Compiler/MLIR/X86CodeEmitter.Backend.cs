using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Runtime;
using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;

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

    public void LeaFuncAddr(VReg dest, string codeLabel) =>
      _e.EmitLeaFuncAddr(R(dest), codeLabel);

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

    // ---- ModR/M displacement encoding (shared) ----

    // The hand-encoded [base + offset] memory forms below (byte MOV / MOVZX, and the
    // LOCK FF /0, FF /1, and 0F C1 group forms) select their displacement width here,
    // in ONE place, rather than each writing a single displacement byte. A lone disp8
    // silently truncates any offset outside a signed byte (200 becomes -56), so those
    // sites read/wrote the wrong address for a field more than 127 bytes from its base.
    // These forms have always emitted an EXPLICIT displacement (mod=01 even at offset 0),
    // so keeping mod=01 for the in-range case leaves every offset in [Disp8Min, Disp8Max]
    // byte-identical to prior output; only a larger offset now widens to mod=10 + disp32
    // instead of truncating. This mirrors EmitModRmWithBase's disp8/disp32/SIB selection;
    // it stays separate only because these forms never elide the displacement at offset 0.
    private const int Disp8Min = -128;
    private const int Disp8Max = 127;
    private const byte ModRmModDisp8 = 0x40;   // mod=01: [base + disp8]
    private const byte ModRmModDisp32 = 0x80;  // mod=10: [base + disp32]
    private const int ModRmRmSibEscape = 4;    // r/m=100 escapes to a SIB byte (RSP/R12 base)
    private const byte SibBaseNoIndex = 0x24;  // scale=0, index=none(100), base=100
    private const int FfExtInc = 0;            // FF /0 = INC
    private const int FfExtDec = 1;            // FF /1 = DEC

    /// Emit the ModR/M byte (plus a SIB byte for an RSP/R12 base) and the base-relative
    /// displacement for a [base + offset] operand. `regField` is a real register's low
    /// 3 bits (MOV / XADD) or an opcode-group extension (INC /0, DEC /1). disp8 when the
    /// offset fits a signed byte, else disp32 — never the truncating single byte.
    private void EmitByteMemModRm(int regField, X86Register baseReg, int offset) {
      int baseCode = (int)baseReg & 7;
      bool needsSib = baseCode == ModRmRmSibEscape;
      byte mod = offset >= Disp8Min && offset <= Disp8Max ? ModRmModDisp8 : ModRmModDisp32;
      _e.EmitByte((byte)(mod | ((regField & 7) << 3) | baseCode));
      if (needsSib)
        _e.EmitByte(SibBaseNoIndex);
      if (mod == ModRmModDisp8)
        _e.EmitByte((byte)(offset & 0xFF));
      else
        _e.EmitDword(offset);
    }

    // ---- Atomics ----

    // LOCK-prefixed unary group form: F0 + REX.W(.B) + FF /ext + [base + disp].
    // Shared by INC (/0) and DEC (/1); the displacement widens to disp32 as needed.
    private void EmitLockFfUnary(int ext, VReg baseAddr, int offset) {
      var reg = R(baseAddr);
      _e.EmitByte(0xF0); // LOCK prefix
      _e.EmitBytes((byte)(reg >= X86Register.R8 ? 0x49 : 0x48), 0xFF);
      EmitByteMemModRm(ext, reg, offset);
    }

    public void AtomicInc(VReg baseAddr, int offset) => EmitLockFfUnary(FfExtInc, baseAddr, offset);

    public void AtomicDec(VReg baseAddr, int offset) => EmitLockFfUnary(FfExtDec, baseAddr, offset);

    public void AtomicXadd(VReg baseAddr, int offset, VReg val) {
      // LOCK XADD [base + offset], val: F0 + REX.W(.R,.B) + 0F C1 /r
      var baseReg = R(baseAddr);
      var valReg = R(val);
      _e.EmitByte(0xF0); // LOCK
      byte rex = 0x48;
      if (baseReg >= X86Register.R8) rex |= 0x01; // REX.B
      if (valReg >= X86Register.R8) rex |= 0x04; // REX.R
      _e.EmitByte(rex);
      _e.EmitBytes(0x0F, 0xC1);
      EmitByteMemModRm((int)valReg & 7, baseReg, offset);
    }

    public void AtomicCAS(VReg destBase, int offset, VReg expected, VReg desired) {
      // x86 CMPXCHG forces the "expected" operand to be in RAX (= VReg.Scratch0),
      // so we move expected -> RAX first. After the instruction, RAX holds either
      // the just-written value (on success) or the actually-observed memory value
      // (on failure). Either way Scratch0 is clobbered; this is documented in the
      // IEmitterBackend contract.
      var destBaseReg = R(destBase);
      var expectedReg = R(expected);
      var desiredReg = R(desired);
      if (destBaseReg == X86Register.Rax || destBaseReg == X86Register.Rbx)
        throw new ArgumentException("AtomicCAS: destBase must not be Scratch0/RAX or Scratch3/RBX");
      if (expectedReg == X86Register.Rbx || desiredReg == X86Register.Rax || desiredReg == X86Register.Rbx)
        throw new ArgumentException("AtomicCAS: expected/desired must not collide with Scratch0/Scratch3");

      // MOV RAX, expected
      if (expectedReg != X86Register.Rax)
        _e.EmitMovRegReg(X86Register.Rax, expectedReg);

      // LOCK CMPXCHG [destBase + offset], desired
      // ZF=1 on success (memory unchanged, RAX==[mem]) — semantically RAX still equals expected.
      // ZF=0 on failure (RAX = [mem]).
      _e.EmitLockCmpxchgMemReg(destBaseReg, offset, desiredReg);

      // SETZ BL — write low byte of Scratch3 with the ZF flag (1 on success).
      _e.EmitSetcc("z", X86Register.Rbx);

      // MOVZX RBX, BL — zero-extend to 64 bits so the result reads back cleanly as 0/1.
      _e.EmitMovzxReg8To64(X86Register.Rbx);
    }

    public void SpinHint() => _e.EmitBytes(0xF3, 0x90); // PAUSE

    public void FullBarrier() => _e.EmitMfence();

    // x86-64 is TSO: every load already has acquire and every store release
    // semantics, so acquire/release are plain indirect load/store.
    public void LoadAcquire(VReg dest, VReg baseReg, int offset) => LoadIndirect(dest, baseReg, offset);
    public void StoreRelease(VReg baseReg, int offset, VReg src) => StoreIndirect(baseReg, offset, src);

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
      // Load P* from TEB via precomputed GS-segment offset, using ONLY `dest` as
      // scratch — this primitive must clobber nothing but its destination, the
      // same contract every other backend op honors (and the one arm64's X28-move
      // LoadCurrentP already meets). The previous version used R11 unconditionally,
      // which is VReg.Scratch2: any caller holding a live value in Scratch2 across
      // LoadCurrentP (e.g. the slab alloc ownership gate, which loads span->owning_p
      // into Scratch2 then calls LoadCurrentP) had that value silently destroyed,
      // making the gate compare P* against P.id and reject every span — an infinite
      // refill spin that exhausted memory on x86-windows. EmitMovRegIndirectMemRaw
      // encodes the GS dereference for any register, so `dest` works directly.
      var reg = R(dest);
      _e.EmitGlobalLoadReg(reg, "__sched_tls_teb_offset"); // dest = teb_offset
      _e.EmitByte(0x65); // GS segment override prefix
      _e.EmitMovRegIndirectMemRaw(reg, reg, 0); // dest = GS:[dest] = P*
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

    public void OsYield() =>
      _e.EmitCallImportOnSystemStack("kernel32.dll", "SwitchToThread");

    // Sleep takes milliseconds, which IS the portable unit, so no scaling.
    public void OsSleepMillis(VReg millis) {
      if (R(millis) != X86Register.Rcx) _e.EmitMovRegReg(X86Register.Rcx, R(millis));
      _e.EmitCallImportOnSystemStack("kernel32.dll", "Sleep");
    }

    // GetEnvironmentVariableA copies into caller memory, so unlike POSIX's getenv this needs the
    // caller's scratch slots. Four slots = 32 bytes, which is ample for the unsigned decimals this
    // reads and matches the buffer __gt_init's own env reads use. The buffer starts AT
    // scratchSlot's address and grows toward RBP, so it occupies slots scratchSlot..scratchSlot-3
    // (see LeaLocal, and IEmitterBackend's contract for why the direction is written down).
    public void ReadEnvUnsigned(VReg dest, string nameSymdata, int scratchSlot) {
      const int envBufSlots = 4;
      var zeroLabel = BackendLabel("env_unsigned_zero");
      var doneLabel = BackendLabel("env_unsigned_done");

      _e.EmitLeaRegSymdataRel(X86Register.Rcx, nameSymdata);   // lpName
      LeaLocal(VReg.Arg1, scratchSlot);                        // lpBuffer
      _e.EmitMovRegImm(X86Register.R8, envBufSlots * 8);        // nSize
      _e.EmitCallImport("kernel32.dll", "GetEnvironmentVariableA");

      // ⚠ TEST THE RETURNED COUNT BEFORE ZEROING `dest`, AND ZERO IT AT A SEPARATE LABEL — the
      // order is load-bearing, not style. `dest` MAY ALIAS RAX (VReg.Scratch0 maps to it, and both
      // of this helper's callers pass Scratch0), so zeroing first destroys the very count that
      // distinguishes "unset or empty" from "read N chars" and leaves ZF=1 unconditionally: the
      // branch is then always taken and every read answers 0. It did exactly that — both netpoll
      // injection knobs were silently dead on Windows. This is the shape the pre-existing twin
      // EmitReadMaxProcsEnvOverride has always used, for the same reason.
      _e.EmitTestRegReg(X86Register.Rax, X86Register.Rax);
      _e.EmitJcc("z", zeroLabel);                              // unset or empty -> 0

      LeaLocal(VReg.Arg0, scratchSlot);
      _e.EmitParseUnsignedCstrIntoRax(X86Register.Rcx);
      if (R(dest) != X86Register.Rax) _e.EmitMovRegReg(R(dest), X86Register.Rax);
      _e.EmitJmp(doneLabel);

      _e.DefineLabel(zeroLabel);
      _e.EmitXorRegReg(R(dest), R(dest));

      _e.DefineLabel(doneLabel);
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

    /// <summary>Nanoseconds in a second: the numerator of the performance-counter tick scale.</summary>
    private const long NanosPerSecond = 1_000_000_000L;

    public void GetCurrentTimeMs(VReg dest, int scratchSlot) {
      // GetTickCount64() returns milliseconds since boot
      _e.EmitCallImportOnSystemStack("kernel32.dll", "GetTickCount64");
      // Result in RAX; move to dest if needed
      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    public void GetCurrentTimeNanos(VReg dest, int scratchSlot) {
      // QueryPerformanceFrequency(&freq) / QueryPerformanceCounter(&ticks): both take a
      // LARGE_INTEGER out-param in RCX, so the two scratch slots are the out-buffers.
      // Slot N lives at [rbp-(N+1)*8] (see LoadLocal).
      int ticksDisp = -(scratchSlot + 1) * 0x08;
      int freqDisp = -(scratchSlot + 2) * 0x08;

      // QPF is fixed for the lifetime of the boot, but it is re-read per call rather than
      // memoized in a global: on every supported Windows it is a user-mode read of
      // KUSER_SHARED_DATA (no syscall), and caching it would buy a few nanoseconds at the
      // cost of a lazily-initialized global with a cross-thread init race. The counter's
      // own period (100 ns in practice) dominates the error budget either way.
      _e.EmitLeaRegMem(X86Register.Rcx, freqDisp);
      _e.EmitCallImportOnSystemStack("kernel32.dll", "QueryPerformanceFrequency");

      _e.EmitLeaRegMem(X86Register.Rcx, ticksDisp);
      _e.EmitCallImportOnSystemStack("kernel32.dll", "QueryPerformanceCounter");

      // nanos = ticks * 1e9 / freq, via the FULL 128-bit MUL/DIV pair. A 64-bit IMUL would
      // silently wrap: at the usual 10 MHz QPF, `ticks * 1e9` exceeds 2^64 after ~15 minutes
      // of uptime. MUL leaves the 128-bit product in RDX:RAX, which is exactly the dividend
      // DIV consumes, so the scale is exact; the quotient is nanoseconds-since-boot and
      // cannot overflow 64 bits for ~584 years, so DIV cannot raise #DE.
      _e.EmitMovRegMem(X86Register.Rax, ticksDisp, 8);
      _e.EmitMovRegImm(X86Register.R8, NanosPerSecond);
      _e.EmitBytes(0x49, 0xF7, 0xE0);                  // MUL R8   => RDX:RAX = ticks * 1e9
      _e.EmitMovRegMem(X86Register.Rcx, freqDisp, 8);  // RCX = ticks per second
      _e.EmitBytes(0x48, 0xF7, 0xF1);                  // DIV RCX  => RAX = nanoseconds

      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    /// <summary>100 ns ticks between the FILETIME epoch (1601-01-01) and the Unix epoch (1970-01-01).</summary>
    private const long FileTimeTicksToUnixEpoch = 116_444_736_000_000_000L;

    /// <summary>A FILETIME tick is 100 ns, so this many of them make a second.</summary>
    private const long FileTimeTicksPerSecond = 10_000_000L;

    public void GetCurrentUnixTimeSeconds(VReg dest, int scratchSlot) {
      // GetSystemTimeAsFileTime(&ft) writes a FILETIME — a 64-bit count of 100 ns ticks since
      // 1601-01-01 UTC — through an out-param in RCX. Slot N lives at [rbp-(N+1)*8].
      //
      // It is the cheap read: a user-mode load out of KUSER_SHARED_DATA, no syscall. Its
      // resolution is the same ~15.6 ms scheduler tick as GetTickCount64, which is irrelevant
      // here — a caller asking for whole SECONDS cannot observe the difference.
      int fileTimeDisp = -(scratchSlot + 1) * 0x08;
      _e.EmitLeaRegMem(X86Register.Rcx, fileTimeDisp);
      _e.EmitCallImportOnSystemStack("kernel32.dll", "GetSystemTimeAsFileTime");

      // seconds = (filetime - ticksToUnixEpoch) / ticksPerSecond.
      //
      // DIV (not IDIV) is correct and cannot trap: the subtraction cannot go negative unless the
      // system clock is set before 1970, and the quotient — seconds since the epoch — does not
      // approach 2^64. RDX must be zeroed first because DIV divides the full 128-bit RDX:RAX.
      _e.EmitMovRegMem(X86Register.Rax, fileTimeDisp, 8);
      _e.EmitMovRegImm(X86Register.Rcx, FileTimeTicksToUnixEpoch);
      _e.EmitBytes(0x48, 0x29, 0xC8);                        // SUB RAX, RCX
      _e.EmitBytes(0x48, 0x31, 0xD2);                        // XOR RDX, RDX
      _e.EmitMovRegImm(X86Register.Rcx, FileTimeTicksPerSecond);
      _e.EmitBytes(0x48, 0xF7, 0xF1);                        // DIV RCX => RAX = unix seconds

      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    /// <summary>The GetCurrentThread() pseudo-handle, (HANDLE)-2. It is a constant, not a
    /// handle to open or close, so materializing it inline saves an import and a call.</summary>
    private const long CurrentThreadPseudoHandle = -2L;

    public void GetThreadCpuTicks(VReg dest, int scratchSlot) {
      // QueryThreadCycleTime(HANDLE, PULONG64) writes its result through an out-param in RDX,
      // so slot N — at [rbp-(N+1)*8], see LoadLocal — is that buffer.
      //
      // The value is a TSC tick count, NOT a retired-cycle count: on every machine this
      // targets the TSC is invariant, so a "cycle" here is a fixed-rate tick and the reading
      // is really CPU TIME at TSC resolution. That is precisely what is wanted — it excludes
      // preemption and every other process — but it is why the unit is `ticks` and why this
      // cannot be compared against the POSIX backend's nanoseconds.
      int cyclesDisp = -(scratchSlot + 1) * 0x08;

      _e.EmitMovRegImm(X86Register.Rcx, CurrentThreadPseudoHandle);
      _e.EmitLeaRegMem(X86Register.Rdx, cyclesDisp);
      _e.EmitCallImportOnSystemStack("kernel32.dll", "QueryThreadCycleTime");

      // The BOOL return is deliberately ignored. The only documented failure is an invalid
      // handle, and the handle is a compile-time constant naming the running thread — there is
      // no runtime condition under which it can fail, and a caller measuring its own cost has
      // nothing useful to do about it if it somehow did.
      _e.EmitMovRegMem(X86Register.Rax, cyclesDisp, 8);

      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    /// <summary>The GetCurrentProcess() pseudo-handle, (HANDLE)-1. Like the thread one above it is a
    /// constant rather than a handle to open or close, so materializing it inline costs no import.</summary>
    private const long CurrentProcessPseudoHandle = -1L;

    /// <summary>BELOW_NORMAL_PRIORITY_CLASS. Deliberately not IDLE_PRIORITY_CLASS: idle work can be
    /// starved for as long as anything else wants the CPU, and the harnesses that call this bound how
    /// long they will wait, so a long starve would be reported as a HARNESS failure rather than as the
    /// busy machine it actually is.</summary>
    private const long BelowNormalPriorityClass = 0x4000L;

    public void EnterBackgroundPriority(VReg dest) {
      // SetPriorityClass sets a PROCESS class, which is what discharges the inheritance contract on
      // this lane: it applies to threads created before AND after this call, so the IOCP completion
      // thread that __io_init started from _start — before main ever ran — is covered too, and no
      // worker M needs to do anything at its own birth. See IEmitterBackend.EnterBackgroundPriority.
      _e.EmitMovRegImm(X86Register.Rcx, CurrentProcessPseudoHandle);
      _e.EmitMovRegImm(X86Register.Rdx, BelowNormalPriorityClass);
      _e.EmitCallImportOnSystemStack("kernel32.dll", "SetPriorityClass");

      // The BOOL return is ignored, and the ANSWER is a fresh GetPriorityClass instead. That is not
      // belt-and-braces: returning the constant we just tried to write would report success from a
      // lowering that never called anything, which is exactly the failure a spec must be able to see.
      _e.EmitMovRegImm(X86Register.Rcx, CurrentProcessPseudoHandle);
      _e.EmitCallImportOnSystemStack("kernel32.dll", "GetPriorityClass");

      // GetPriorityClass answers a DWORD; the 32-bit write zero-extends into RAX. It answers 0 only
      // on failure, which this call site cannot provoke — the handle is a compile-time constant.
      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    public void GetCurrentProcessId(VReg dest) {
      // GetCurrentProcessId() returns a DWORD (process ID). Zero-extends
      // into RAX naturally for the caller's i64 result.
      _e.EmitCallImportOnSystemStack("kernel32.dll", "GetCurrentProcessId");
      var destReg = R(dest);
      if (destReg != X86Register.Rax)
        _e.EmitMovRegReg(destReg, X86Register.Rax);
    }

    public void DriveSchedulerAndIo() => _e.EmitDriveSchedulerAndIo();

    public void SwitchToMainThread() => _e.EmitSwitchToMainThread();

    public void WakeWorker(VReg p) {
      // SetEvent(p->wakeEvent); POffWakeEvent = 0x38
      _e.EmitMovRegIndirectMem(X86Register.Rcx, R(p), 0x38); // rcx = p->wakeEvent
      _e.EmitCallImportOnSystemStack("kernel32.dll", "SetEvent");
    }

    public void SpawnWorker(VReg p) {
      // CreateThread(NULL, 0, __sched_worker_loop, P[i], 0, NULL)
      //
      // ⚠⚠ P[i] IS CARRIED ACROSS THE STACK SWITCH IN MEMORY, NEVER IN A REGISTER, and that is the
      // whole reason [rbp-0x30] exists here. `EmitSystemStackEnter` is a CONDITIONAL: its green-
      // thread arm does `PUSH R10 / MOV R10, RSP` to shuttle the outgoing RSP across the switch,
      // and its main-thread arm is a bare `SUB RSP`. So R10 holds the caller's value on one arm and
      // the OUTGOING GT RSP on the other — anything staged there before the Enter is a STACK
      // ADDRESS by the time the argument setup reads it.
      //
      // ⚠ THIS WAS A LIVE ACCESS VIOLATION, NOT A THEORETICAL ONE. `__gt_enqueue` calls this from
      // whatever thread made a GT runnable, and a green thread making another green thread runnable
      // is the ordinary case; when the scan finds no idle P it lands here ON THE GT STACK.
      // `MOV R9, R10` then handed `__sched_worker_loop` a stack pointer as its P*, and the new
      // worker dereferenced it: measured 2026-08-01, a nested `async` (8 spawns from inside an
      // `async` body) crashed 12/12 with 0xC0000005 while the identical 8 spawns from `main` — the
      // straight-through arm — passed 5/5. Same shape as B3's RAX defect, one register over.
      //
      // RBP is callee-saved and still addresses the caller's frame inside the switched region, so
      // the slot is readable from both arms. The caller must have a frame of at least 0x30
      // (`__gt_enqueue` declares 0x60 and uses slots 0-4, i.e. down to [rbp-0x28]).
      const int PSlotDisp = -0x30;

      var pReg = R(p);
      _e.EmitMovMemReg(PSlotDisp, pReg, 8); // save P[i] to [rbp-0x30]

      // Switch to system stack and set up 6 args for CreateThread
      _e.EmitSystemStackEnter(0x30); // shadow(0x20) + 2 stack args(0x10) = 0x30
      _e.EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);     // lpThreadAttributes = NULL
      _e.EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);     // dwStackSize = 0
      // LEA R8, [rip + __sched_worker_loop]
      _e.EmitByte(0x4C); _e.EmitByte(0x8D); _e.EmitByte(0x05);
      _e._jumpFixups.Add((_e._code.Count, "__sched_worker_loop"));
      _e.EmitDword(0);
      _e.EmitMovRegMem(X86Register.R9, PSlotDisp, 8);          // lpParameter = P[i], reloaded via RBP
      // Args 5 and 6 on stack: [rsp+0x20] = dwCreationFlags=0, [rsp+0x28] = lpThreadId=NULL
      _e.EmitXorRegReg(X86Register.Rax, X86Register.Rax);
      _e.EmitMovMemRspReg(0x20, X86Register.Rax); // dwCreationFlags = 0
      _e.EmitMovMemRspReg(0x28, X86Register.Rax); // lpThreadId = NULL
      _e.EmitCallImport("kernel32.dll", "CreateThread");
      _e.EmitSystemStackLeave(0x30);

      // Reload P[i] from the frame — CreateThread destroyed R10 by ABI on both arms.
      _e.EmitMovRegMem(X86Register.R10, PSlotDisp, 8);
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
    public string SymbolTableLabel => "__symtable";

    // ---- Local address / byte memory ----

    public void LeaLocal(VReg dest, int slotIndex) {
      // LEA R(dest), [RBP + -(slotIndex+1)*8]
      _e.EmitLeaRegMem(R(dest), -(slotIndex + 1) * 8);
    }

    public void StoreIndirectByte(VReg baseReg, int offset, VReg src) {
      // MOV BYTE [R(base) + offset], R(src) low byte: [REX] 88 /r
      var baseReg_ = R(baseReg);
      var srcReg_ = R(src);
      byte rex = 0x40;
      if (baseReg_ >= X86Register.R8) rex |= 0x01; // REX.B
      if (srcReg_ >= X86Register.R8) rex |= 0x04;  // REX.R
      // Without REX, mod/rm encodings 4..7 select AH/CH/DH/BH instead of SPL/BPL/SIL/DIL.
      // REX must be emitted whenever src is RSP/RBP/RSI/RDI to reach their low bytes.
      bool needRex = rex != 0x40 || (srcReg_ >= X86Register.Rsp && srcReg_ <= X86Register.Rdi);
      if (needRex) _e.EmitByte(rex);
      _e.EmitByte(0x88);
      EmitByteMemModRm((int)srcReg_ & 7, baseReg_, offset);
    }

    public void LoadIndirectByte(VReg dest, VReg baseReg, int offset) {
      // MOVZX R(dest), BYTE [R(base) + offset]: REX.W + 0F B6 /r
      var destReg = R(dest);
      var baseReg_ = R(baseReg);
      byte rex = 0x48;
      if (destReg >= X86Register.R8) rex |= 0x04; // REX.R
      if (baseReg_ >= X86Register.R8) rex |= 0x01; // REX.B
      _e.EmitByte(rex);
      _e.EmitBytes(0x0F, 0xB6);
      EmitByteMemModRm((int)destReg & 7, baseReg_, offset);
    }

    // ---- Platform info ----

    public string SchedLockLabel => "__sched_global_queue_cs";
    public string TimerLockLabel => "__gt_timer_cs";

    // The same CRITICAL_SECTION, taken the same way, that __gt_spawn and __gt_trampoline take.
    public void AllThreadsLockAcquire() => LockAcquire("__sched_all_cs");
    public void AllThreadsLockRelease() => LockRelease("__sched_all_cs");

    // ---- Fault handler (real impls land in Phase 2) ----

    public void InstallFaultHandler(string thunkLabel) {
      _e.EmitInstallFaultHandler(thunkLabel);
    }

    public void OsInstallTrapHandler(string thunkLabel) {
      _e.EmitInstallTrapHandler(thunkLabel);
    }

    public void EmitFaultHandlerProlog(string thunkLabel, string sharedHandlerLabel) {
      _e.EmitFaultHandlerProlog(thunkLabel, sharedHandlerLabel);
    }

    public void EmitFaultHandlerEpilog() {
      _e.EmitFaultHandlerEpilog();
    }

    public void EmitFaultBacktrace() {
      _e.EmitFaultBacktrace();
    }

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
