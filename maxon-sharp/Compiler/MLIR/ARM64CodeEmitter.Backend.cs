using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Runtime;

namespace MaxonSharp.Compiler.Mlir;

public partial class ARM64CodeEmitter {

  /// <summary>
  /// Maps VRegs to ARM64 physical registers (AAPCS64 calling convention).
  /// </summary>
  private static ARM64Register MapVReg(VReg v) => v switch {
    VReg.Arg0 => ARM64Register.X0,
    VReg.Arg1 => ARM64Register.X1,
    VReg.Arg2 => ARM64Register.X2,
    VReg.Arg3 => ARM64Register.X3,
    VReg.Arg4 => ARM64Register.X4,
    VReg.Arg5 => ARM64Register.X5,
    VReg.Scratch0 => ARM64Register.X9,   // Also VReg.Ret (need to move to X0 on return)
    VReg.Scratch1 => ARM64Register.X10,
    VReg.Scratch2 => ARM64Register.X11,
    VReg.Scratch3 => ARM64Register.X12,
    _ => throw new ArgumentException($"Unmapped VReg: {v}")
  };

  /// <summary>
  /// IEmitterBackend implementation for ARM64 macOS.
  /// Nested class so it can access all private members.
  /// </summary>
  public class ARM64EmitterBackend(ARM64CodeEmitter emitter) : IEmitterBackend {
    private readonly ARM64CodeEmitter _e = emitter;

    public bool IsWindows => false;
    public bool IsMacOS => true;

    private static ARM64Register R(VReg v) => MapVReg(v);

    // ---- Function structure ----

    public void FunctionStart(string name, int argCount, int frameSize) {
      _e.DefineLabel(name);
      _e._currentRuntimeStackSize = frameSize;
      // STP x29, x30, [sp, #-stackSize]!
      var imm7 = (uint)((-frameSize / 8) & 0x7F);
      _e.EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
      // MOV x29, sp
      _e.EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);
      // Save arguments: [x29+16], [x29+24], ...
      for (int i = 0; i < argCount && i < 8; i++)
        _e.EmitLoadStoreUnsignedImm(0xF9000000, AbiArgRegs[i], ARM64Register.X29, 16 + i * 8, 8);
    }

    public void FunctionEnd() {
      // MOV sp, x29
      _e.EmitWord(0x91000000 | (29u << 5) | 31u);
      // LDP x29, x30, [sp], #stackSize
      var imm7 = (uint)((_e._currentRuntimeStackSize / 8) & 0x7F);
      _e.EmitWord(0xA8C00000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
      // RET
      _e.EmitWord(0xD65F03C0);
    }

    public void ReturnValue(VReg src) {
      // On ARM64, return value goes in X0
      if (R(src) != ARM64Register.X0)
        _e.EmitMovRegReg(ARM64Register.X0, R(src));
      FunctionEnd();
    }

    // ---- Register operations ----

    public void MovRegReg(VReg dest, VReg src) {
      // Special case: if dest is Ret (Scratch0=X9) and we need to return in X0,
      // the RuntimeEmitter handles this explicitly. Just mov between mapped regs.
      _e.EmitMovRegReg(R(dest), R(src));
    }

    public void MovRegImm(VReg dest, long imm) => _e.EmitMovRegImm(R(dest), imm);

    public void ZeroReg(VReg reg) => _e.EmitMovRegImm(R(reg), 0);

    // ---- Memory: local stack frame ----
    // ARM64 runtime functions save args at [x29+16], [x29+24], etc.
    // Slot 0 = [x29+16] (arg0), slot 1 = [x29+24] (arg1), etc.

    public void LoadLocal(VReg dest, int slotIndex) =>
      _e.EmitLoadStoreUnsignedImm(0xF9400000, R(dest), ARM64Register.X29, 16 + slotIndex * 8, 8);

    public void StoreLocal(int slotIndex, VReg src) =>
      _e.EmitLoadStoreUnsignedImm(0xF9000000, R(src), ARM64Register.X29, 16 + slotIndex * 8, 8);

    // ---- Memory: indirect ----

    public void LoadIndirect(VReg dest, VReg baseReg, int offset) {
      if (offset >= 0 && offset % 8 == 0 && offset < 32768) {
        _e.EmitLoadStoreUnsignedImm(0xF9400000, R(dest), R(baseReg), offset, 8);
      } else {
        // Use LDUR for unscaled/negative offsets
        // LDUR Xt, [Xn, #simm9]
        var imm9 = (uint)(offset & 0x1FF);
        _e.EmitWord(0xF8400000 | (imm9 << 12) | (Reg(R(baseReg)) << 5) | Reg(R(dest)));
      }
    }

    public void StoreIndirect(VReg baseReg, int offset, VReg src) {
      if (offset >= 0 && offset % 8 == 0 && offset < 32768) {
        _e.EmitLoadStoreUnsignedImm(0xF9000000, R(src), R(baseReg), offset, 8);
      } else {
        // Use STUR for unscaled/negative offsets
        var imm9 = (uint)(offset & 0x1FF);
        _e.EmitWord(0xF8000000 | (imm9 << 12) | (Reg(R(baseReg)) << 5) | Reg(R(src)));
      }
    }

    // ---- Globals ----

    public void LoadGlobal(VReg dest, string globalLabel) =>
      _e.EmitGlobalLoadReg(R(dest), globalLabel);

    public void StoreGlobal(string globalLabel, VReg src) =>
      _e.EmitGlobalStoreReg(R(src), globalLabel);

    public void LeaGlobal(VReg dest, string globalLabel) =>
      _e.EmitGlobalLeaReg(R(dest), globalLabel);

    public void LeaSymdata(VReg dest, string symdataLabel) =>
      _e.EmitAdrpAddFixup(R(dest), _e._symdataAdrpFixups, symdataLabel);

    // ---- Arithmetic ----

    public void AddRegImm(VReg dest, long imm) =>
      _e.EmitAddSubImm(R(dest), R(dest), imm, isAdd: true);

    public void SubRegImm(VReg dest, long imm) =>
      _e.EmitAddSubImm(R(dest), R(dest), imm, isAdd: false);

    public void AddRegReg(VReg dest, VReg src) =>
      _e.EmitAluRegReg(0x8B000000, R(dest), R(dest), R(src)); // ADD X

    public void SubRegReg(VReg dest, VReg src) =>
      _e.EmitAluRegReg(0xCB000000, R(dest), R(dest), R(src)); // SUB X

    public void MulRegReg(VReg dest, VReg src) {
      // MUL Xd, Xn, Xm = MADD Xd, Xn, Xm, XZR
      _e.EmitWord(0x9B007C00 | (Reg(R(src)) << 16) | (Reg(R(dest)) << 5) | Reg(R(dest)));
    }

    public void ShlRegImm(VReg dest, int shift) {
      // LSL Xd, Xn, #shift = UBFM Xd, Xn, #(64-shift), #(63-shift)
      var immr = (uint)((64 - shift) & 63);
      var imms = (uint)((63 - shift) & 63);
      _e.EmitWord(0xD3400000 | (immr << 16) | (imms << 10) | (Reg(R(dest)) << 5) | Reg(R(dest)));
    }

    public void ShrRegImm(VReg dest, int shift) {
      // LSR Xd, Xn, #shift = UBFM Xd, Xn, #shift, #63
      _e.EmitWord(0xD340FC00 | ((uint)shift << 16) | (Reg(R(dest)) << 5) | Reg(R(dest)));
    }

    public void AndRegReg(VReg dest, VReg src) =>
      _e.EmitAluRegReg(0x8A000000, R(dest), R(dest), R(src)); // AND X

    public void OrRegReg(VReg dest, VReg src) =>
      _e.EmitAluRegReg(0xAA000000, R(dest), R(dest), R(src)); // ORR X

    public void XorRegReg(VReg dest, VReg src) =>
      _e.EmitAluRegReg(0xCA000000, R(dest), R(dest), R(src)); // EOR X

    // ---- Comparison & branching ----

    public void CmpRegReg(VReg left, VReg right) =>
      _e.EmitCmpRegReg(R(left), R(right));

    public void CmpRegImm(VReg reg, long imm) =>
      _e.EmitCmpImm(R(reg), imm);

    public void TestRegReg(VReg left, VReg right) {
      // TST = ANDS XZR, Xn, Xm
      _e.EmitAluRegReg(0xEA000000, ARM64Register.Xzr, R(left), R(right));
    }

    public void Jump(string label) => _e.EmitBranch(label);

    public void JumpIf(Condition cond, string label) {
      var cc = cond switch {
        Condition.Equal => ARM64ConditionCode.Eq,
        Condition.NotEqual => ARM64ConditionCode.Ne,
        Condition.Less => ARM64ConditionCode.Lt,
        Condition.LessEqual => ARM64ConditionCode.Le,
        Condition.Greater => ARM64ConditionCode.Gt,
        Condition.GreaterEqual => ARM64ConditionCode.Ge,
        Condition.Above => ARM64ConditionCode.Hi,
        Condition.Below => ARM64ConditionCode.Lo,
        Condition.AboveEqual => ARM64ConditionCode.Hs,
        Condition.BelowEqual => ARM64ConditionCode.Ls,
        _ => throw new ArgumentException($"Unknown condition: {cond}")
      };
      _e.EmitBranchCond(cc, label);
    }

    public void JumpIfZero(VReg reg, string label) {
      // CBZ Xn, label
      _e._condBranchFixups.Add((_e._code.Count, label));
      _e.EmitWord(0xB4000000 | Reg(R(reg)));
    }

    public void JumpIfNonZero(VReg reg, string label) {
      // CBNZ Xn, label
      _e._condBranchFixups.Add((_e._code.Count, label));
      _e.EmitWord(0xB5000000 | Reg(R(reg)));
    }

    // ---- Calls ----

    public void Call(string label) => _e.EmitBranchLink(label);

    public void CallImport(string function) {
      var resolved = ResolveImport(function);
      _e.EmitCallImport(resolved);
    }

    public void CallImportOnSystemStack(string function) {
      // ARM64 macOS doesn't need system stack switching (no TIB, no green thread stack issues
      // with macOS syscalls in the same way). Just call the import directly.
      var resolved = ResolveImport(function);
      _e.EmitCallImport(resolved);
    }

    public void CallIndirect(VReg target) {
      // BLR Xn
      _e.EmitWord(0xD63F0000 | (Reg(R(target)) << 5));
    }

    // ---- Atomics ----

    public void AtomicInc(VReg baseAddr, int offset) {
      // Load value, add 1, store. ARM64 atomic: LDAXR/ADD/STLXR loop
      // For simplicity and matching existing code, use non-atomic load/add/store
      // (single-threaded per-P context — only one GT runs on a P at a time).
      // TODO: Use proper atomics when needed for cross-P operations.
      var reg = R(baseAddr);
      // Load [reg + offset] into X16
      if (offset >= 0 && offset % 8 == 0) {
        _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X16, reg, offset, 8);
      } else {
        var imm9 = (uint)(offset & 0x1FF);
        _e.EmitWord(0xF8400000 | (imm9 << 12) | (Reg(reg) << 5) | Reg(ARM64Register.X16));
      }
      // ADD X16, X16, #1
      _e.EmitAddSubImm(ARM64Register.X16, ARM64Register.X16, 1, isAdd: true);
      // Store back
      if (offset >= 0 && offset % 8 == 0) {
        _e.EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X16, reg, offset, 8);
      } else {
        var imm9 = (uint)(offset & 0x1FF);
        _e.EmitWord(0xF8000000 | (imm9 << 12) | (Reg(reg) << 5) | Reg(ARM64Register.X16));
      }
    }

    public void AtomicDec(VReg baseAddr, int offset) {
      // Load, sub 1, store, set flags (SUBS for zero check)
      var reg = R(baseAddr);
      if (offset >= 0 && offset % 8 == 0) {
        _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X16, reg, offset, 8);
      } else {
        var imm9 = (uint)(offset & 0x1FF);
        _e.EmitWord(0xF8400000 | (imm9 << 12) | (Reg(reg) << 5) | Reg(ARM64Register.X16));
      }
      // SUBS X16, X16, #1 (sets zero flag)
      // SUBS Xd, Xn, #imm12: sf=1, op=1, S=1, sh=0 → 0xF1000000 | (imm12 << 10) | (Rn << 5) | Rd
      _e.EmitWord(0xF1000000 | (1u << 10) | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X16));
      // Store back
      if (offset >= 0 && offset % 8 == 0) {
        _e.EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X16, reg, offset, 8);
      } else {
        var imm9 = (uint)(offset & 0x1FF);
        _e.EmitWord(0xF8000000 | (imm9 << 12) | (Reg(reg) << 5) | Reg(ARM64Register.X16));
      }
    }

    public void AtomicXadd(VReg baseAddr, int offset, VReg val) {
      // old = [base+offset]; [base+offset] = old + val; val = old
      var reg = R(baseAddr);
      var vr = R(val);
      // Load old into X16
      if (offset >= 0 && offset % 8 == 0) {
        _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X16, reg, offset, 8);
      } else {
        var imm9 = (uint)(offset & 0x1FF);
        _e.EmitWord(0xF8400000 | (imm9 << 12) | (Reg(reg) << 5) | Reg(ARM64Register.X16));
      }
      // X17 = X16 + val
      _e.EmitAluRegReg(0x8B000000, ARM64Register.X17, ARM64Register.X16, vr);
      // Store X17 back
      if (offset >= 0 && offset % 8 == 0) {
        _e.EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X17, reg, offset, 8);
      } else {
        var imm9 = (uint)(offset & 0x1FF);
        _e.EmitWord(0xF8000000 | (imm9 << 12) | (Reg(reg) << 5) | Reg(ARM64Register.X17));
      }
      // val = old (X16)
      _e.EmitMovRegReg(vr, ARM64Register.X16);
    }

    // ---- Labels & data ----

    public void DefineLabel(string label) => _e.DefineLabel(label);
    public void DefineGlobal(string label, int size, long initValue) =>
      _e.DefineGlobal(label, size, initValue);
    public void DefineSymdata(string label, byte[] data) => _e.DefineSymdata(label, data);

    // ---- Locking ----

    public void LockAcquire(string lockGlobal) {
      _e.EmitGlobalLeaReg(ARM64Register.X0, lockGlobal);
      _e.EmitCallImport("os_unfair_lock_lock");
    }

    public void LockRelease(string lockGlobal) {
      _e.EmitGlobalLeaReg(ARM64Register.X0, lockGlobal);
      _e.EmitCallImport("os_unfair_lock_unlock");
    }

    // ---- TLS ----

    public void LoadCurrentP(VReg dest) {
      // ARM64: X28 is the dedicated P* register
      _e.EmitMovRegReg(R(dest), ARM64Register.X28);
    }

    // ---- OS memory allocation ----

    public void OsAllocPages(VReg dest, VReg size) {
      // mmap(NULL, size, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0)
      // size is currently in R(size); we need to move it to X1 (arg1)
      _e.EmitMovRegReg(ARM64Register.X1, R(size)); // X1 = size
      _e.EmitMovRegImm(ARM64Register.X0, 0);        // addr = NULL
      // X1 already = size
      _e.EmitMovRegImm(ARM64Register.X2, 3);        // PROT_READ | PROT_WRITE
      _e.EmitMovRegImm(ARM64Register.X3, 0x1002);   // MAP_ANON | MAP_PRIVATE
      _e.EmitMovRegImm(ARM64Register.X4, -1);       // fd = -1
      _e.EmitMovRegImm(ARM64Register.X5, 0);        // offset = 0
      _e.EmitCallImport("mmap");
      // Result in X0; move to dest
      var destReg = R(dest);
      if (destReg != ARM64Register.X0)
        _e.EmitMovRegReg(destReg, ARM64Register.X0);
    }

    public void OsAllocLargePages(VReg dest, VReg size) {
      // macOS doesn't expose huge pages via standard mmap flags. Return NULL so the
      // caller falls back to regular OsAllocPages. Large-page support on macOS requires
      // the VM_FLAGS_SUPERPAGE_SIZE_2MB private Mach VM interface which is not ABI-stable.
      _e.EmitMovRegImm(R(dest), 0);
    }

    public void OsFreePages(VReg ptr, VReg size) {
      // munmap(ptr, size)
      _e.EmitMovRegReg(ARM64Register.X0, R(ptr));
      _e.EmitMovRegReg(ARM64Register.X1, R(size));
      _e.EmitCallImport("munmap");
    }

    // ---- Scheduler platform helpers ----

    private const int CLOCK_UPTIME_RAW = 0x08; // macOS monotonic clock

    public void GetCurrentTimeMs(VReg dest, int scratchSlot) {
      // clock_gettime(CLOCK_UPTIME_RAW, &timespec) using stack slots scratchSlot and scratchSlot+1
      _e.EmitMovRegImm(ARM64Register.X0, CLOCK_UPTIME_RAW);
      int tsOff = 16 + scratchSlot * 8; // [x29 + 16 + slot*8]
      _e.EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, tsOff, isAdd: true);
      _e.EmitCallImport("clock_gettime");
      // Convert timespec to ms: tv_sec * 1000 + tv_nsec / 1000000
      int tsOff2 = 16 + scratchSlot * 8;
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, tsOff2, 8); // tv_sec
      _e.EmitMovRegImm(ARM64Register.X3, 1000);
      _e.EmitWord(0x9B037C42); // MUL X2, X2, X3
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, tsOff2 + 8, 8); // tv_nsec
      _e.EmitMovRegImm(ARM64Register.X3, 1000000);
      _e.EmitWord(0x9AC30884); // UDIV X4, X4, X3
      _e.EmitWord(0x8B040042); // ADD X2, X2, X4 → now_ms in X2
      // Move result to dest
      var destReg = R(dest);
      if (destReg != ARM64Register.X2)
        _e.EmitMovRegReg(destReg, ARM64Register.X2);
    }

    public void WakeWorker(VReg p) {
      // dispatch_semaphore_signal(p->wakeSemaphore); POffWakeSemaphore = 0x38
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, R(p), 0x38, 8);
      _e.EmitCallImport("dispatch_semaphore_signal");
    }

    public void SpawnWorker(VReg p) {
      // pthread_create(&p->osThreadHandle, NULL, __sched_worker_loop, p)
      // POffOsThreadHandle = 0x40
      var pReg = R(p);
      _e.EmitAddSubImm(ARM64Register.X0, pReg, 0x40, isAdd: true); // &p->osThreadHandle
      _e.EmitMovRegImm(ARM64Register.X1, 0); // attr = NULL
      _e.EmitAdrpAddFixup(ARM64Register.X2, _e._funcAddrAdrpFixups, "__sched_worker_loop");
      _e.EmitMovRegReg(ARM64Register.X3, pReg); // arg = p
      _e.EmitCallImport("pthread_create");
    }

    public void UDivRemainder(VReg dest, VReg dividend, long divisor) {
      // dest = dividend % divisor (unsigned)
      // UDIV X16, dividend, X17; MSUB dest, X16, X17, dividend
      var d = R(dividend);
      _e.EmitMovRegImm(ARM64Register.X16, divisor);
      // UDIV X17, d, X16
      _e.EmitWord(0x9AD00000 | (Reg(ARM64Register.X16) << 16) | (Reg(d) << 5) | Reg(ARM64Register.X17));
      // MSUB dest, X17, X16, d → dest = d - X17*X16 = d % divisor
      var dr = R(dest);
      _e.EmitWord(0x9B108000 | (Reg(ARM64Register.X16) << 16) | (Reg(d) << 10) | (Reg(ARM64Register.X17) << 5) | Reg(dr));
    }

    public void UDivRemainderReg(VReg dest, VReg dividend, VReg divisor) {
      // dest = dividend % divisor (unsigned, register divisor)
      // UDIV X16, dividend, divisor; MSUB dest, X16, divisor, dividend
      var d = R(dividend);
      var v = R(divisor);
      // UDIV X16, d, v
      _e.EmitWord(0x9AC00000 | (Reg(v) << 16) | (Reg(d) << 5) | Reg(ARM64Register.X16));
      // MSUB dest, X16, v, d → dest = d - X16*v = d % v
      var dr = R(dest);
      _e.EmitWord(0x9B008000 | (Reg(v) << 16) | (Reg(d) << 10) | (Reg(ARM64Register.X16) << 5) | Reg(dr));
    }

    // ---- Platform-specific labels ----

    public string WriteStderrLabel => "rt_write_cstr_stderr";

    // ---- Local address / byte memory ----

    public void LeaLocal(VReg dest, int slotIndex) {
      // ADD R(dest), X29, #(16 + slotIndex*8) — ARM64 args start at [x29+16]
      int offset = 16 + slotIndex * 8;
      _e.EmitAddSubImm(R(dest), ARM64Register.X29, offset, isAdd: true);
    }

    public void StoreIndirectByte(VReg baseReg, int offset, VReg src) {
      // STRB W(src), [R(base), #offset] — unsigned offset form
      // Encoding: size=00, V=0, opc=00, imm12=offset, Rn=base, Rt=src
      // 0011 1001 000 | imm12 | Rn | Rt
      uint imm12 = (uint)offset & 0xFFF;
      uint instr = 0x39000000u | (imm12 << 10) | (Reg(R(baseReg)) << 5) | Reg(R(src));
      _e.EmitWord(instr);
    }

    public void LoadIndirectByte(VReg dest, VReg baseReg, int offset) {
      // LDRB W(dest), [R(base), #offset] — unsigned offset form
      // 0011 1001 010 | imm12 | Rn | Rt
      uint imm12 = (uint)offset & 0xFFF;
      uint instr = 0x39400000u | (imm12 << 10) | (Reg(R(baseReg)) << 5) | Reg(R(dest));
      _e.EmitWord(instr);
    }

    // ---- Platform info ----

    public string SchedLockLabel => "__sched_global_lock";
    public string TimerLockLabel => "__sched_timer_lock";

    // ---- Import resolution ----

    private static string ResolveImport(string function) => function switch {
      "os_alloc_pages" => "mmap",
      "os_free_pages" => "munmap",
      "os_write_stdout" => "write",
      "os_write_stderr" => "write",
      "os_exit" => "exit",
      _ => function // pass through for platform-native names
    };
  }

  /// <summary>Create the IEmitterBackend for this ARM64CodeEmitter.</summary>
  public IEmitterBackend CreateBackend() => new ARM64EmitterBackend(this);
}
