using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Runtime;
using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;

namespace MaxonSharp.Compiler.Ir;

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
    VReg.Scratch0 => ARM64Register.X9,   // Also VReg.Ret — X9 is scratch; Call/CallImport move X0→X9 after each call
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
      _e._runtimeFunctionLabels.Add(name);
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
      // On ARM64, VReg.Ret/Scratch0 = X9 but calling convention returns in X0.
      // Always move X9→X0 so the return value is correct (harmless for void functions).
      _e.EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
      // MOV sp, x29
      _e.EmitWord(0x91000000 | (29u << 5) | 31u);
      // LDP x29, x30, [sp], #stackSize
      var imm7 = (uint)((_e._currentRuntimeStackSize / 8) & 0x7F);
      _e.EmitWord(0xA8C00000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
      // RET
      _e.EmitWord(0xD65F03C0);
    }

    public void ReturnValue(VReg src) {
      // Move return value to X9 (Scratch0/Ret) first if needed, then FunctionEnd handles X9→X0
      if (R(src) != ARM64Register.X9)
        _e.EmitMovRegReg(ARM64Register.X9, R(src));
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

    public void LeaFuncAddr(VReg dest, string codeLabel) =>
      _e.EmitAdrpAddFixup(R(dest), _e._funcAddrAdrpFixups, codeLabel);

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

    public void ShrRegReg(VReg dest, VReg count) {
      // LSRV Xd, Xn, Xm: 0x9AC02400 | (Rm << 16) | (Rn << 5) | Rd
      _e.EmitWord(0x9AC02400 | (Reg(R(count)) << 16) | (Reg(R(dest)) << 5) | Reg(R(dest)));
    }

    public void ShlRegReg(VReg dest, VReg count) {
      // LSLV Xd, Xn, Xm: 0x9AC02000 | (Rm << 16) | (Rn << 5) | Rd
      _e.EmitWord(0x9AC02000 | (Reg(R(count)) << 16) | (Reg(R(dest)) << 5) | Reg(R(dest)));
    }

    public void AndRegReg(VReg dest, VReg src) =>
      _e.EmitAluRegReg(0x8A000000, R(dest), R(dest), R(src)); // AND X

    public void OrRegReg(VReg dest, VReg src) =>
      _e.EmitAluRegReg(0xAA000000, R(dest), R(dest), R(src)); // ORR X

    public void XorRegReg(VReg dest, VReg src) =>
      _e.EmitAluRegReg(0xCA000000, R(dest), R(dest), R(src)); // EOR X

    // ---- Bit manipulation ----

    public void BitScanForward(VReg dest, VReg src) {
      // CTZ(x) = CLZ(RBIT(x)). Result is 64 if src==0.
      // RBIT Xd, Xn: 0xDAC00000 | (Rn << 5) | Rd
      _e.EmitWord(0xDAC00000 | (Reg(R(src)) << 5) | Reg(R(dest)));
      // CLZ Xd, Xn: 0xDAC01000 | (Rn << 5) | Rd
      _e.EmitWord(0xDAC01000 | (Reg(R(dest)) << 5) | Reg(R(dest)));
    }

    public void BitTestAndReset(VReg baseReg, int offset, VReg bitIndex) =>
      _e.EmitBitTestAndModify(R(baseReg), offset, R(bitIndex), clear: true);

    public void BitTestAndSet(VReg baseReg, int offset, VReg bitIndex) =>
      _e.EmitBitTestAndModify(R(baseReg), offset, R(bitIndex), clear: false);

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
    // ARM64 returns in X0 but VReg.Ret/Scratch0 maps to X9.
    // Move X0→X9 after every call so VReg.Scratch0 sees the return value.

    public void Call(string label) {
      _e.EmitBranchLink(label);
      _e.EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    }

    public void CallImport(string function) {
      var resolved = ResolveImport(function);
      _e.EmitCallImport(resolved);
      _e.EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    }

    public void CallImportOnSystemStack(string function) {
      // ARM64 macOS doesn't need system stack switching (no TIB, no green thread stack issues
      // with macOS syscalls in the same way). Just call the import directly.
      var resolved = ResolveImport(function);
      _e.EmitCallImport(resolved);
      _e.EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    }

    public void CallIndirect(VReg target) {
      _e.EmitWord(0xD63F0000 | (Reg(R(target)) << 5)); // BLR Xn
      _e.EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    }

    // ---- Atomics ----
    // Non-atomic load/add/store — safe because each P runs one GT at a time.
    // Cross-P atomics (LDAXR/STLXR) are only needed for shared scheduler state,
    // which uses the LockAcquire/LockRelease path instead.

    // ARM64 has no plain-RMW atomicity (unlike x86's LOCK INC/DEC/XADD): a
    // LDR/ADD/STR sequence is NOT atomic, so under the multi-OS-thread GMP
    // scheduler concurrent refcount inc/dec lose updates -> premature free /
    // leak -> heap corruption. These use an LDAXR/STLXR exclusive loop (the same
    // acquire/release primitive AtomicCAS uses). Internal scratch: X16=addr,
    // X17=value, X14=tmp, W15=store-exclusive status — none are VReg-mapped
    // (VRegs are X0-X5/X9-X12), so callers see only X16/X17 clobbered.

    /// X16 = baseAddr + offset (the exclusive-monitor address, kept across retries).
    private void EmitAtomicAddr(ARM64Register baseReg, int offset) {
      if (offset != 0)
        _e.EmitAddSubImm(ARM64Register.X16, baseReg, offset, isAdd: true);
      else if (baseReg != ARM64Register.X16)
        _e.EmitMovRegReg(ARM64Register.X16, baseReg);
    }

    public void AtomicInc(VReg baseAddr, int offset) {
      EmitAtomicAddr(R(baseAddr), offset);
      var retry = $"__ainc_retry_{_e._uniqueLabelCounter++}";
      _e.DefineLabel(retry);
      _e.EmitWord(0xC85FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17)); // LDAXR X17, [X16]
      _e.EmitAddSubImm(ARM64Register.X17, ARM64Register.X17, 1, isAdd: true);            // ADD X17, X17, #1
      _e.EmitWord(0xC800FC00 | (15u << 16) | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17)); // STLXR W15, X17, [X16]
      _e._condBranchFixups.Add((_e._code.Count, retry));
      _e.EmitWord(0x35000000 | 15u);                                                     // CBNZ W15, retry
    }

    public void AtomicDec(VReg baseAddr, int offset) {
      EmitAtomicAddr(R(baseAddr), offset);
      var retry = $"__adec_retry_{_e._uniqueLabelCounter++}";
      _e.DefineLabel(retry);
      _e.EmitWord(0xC85FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17)); // LDAXR X17, [X16]
      // SUBS X17, X17, #1 — sets NZCV so callers branch on (new refcount == 0).
      // STLXR/CBNZ below leave NZCV untouched, so the flags survive to the caller.
      _e.EmitWord(0xF1000000 | (1u << 10) | (Reg(ARM64Register.X17) << 5) | Reg(ARM64Register.X17));
      _e.EmitWord(0xC800FC00 | (15u << 16) | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17)); // STLXR W15, X17, [X16]
      _e._condBranchFixups.Add((_e._code.Count, retry));
      _e.EmitWord(0x35000000 | 15u);                                                     // CBNZ W15, retry
    }

    public void AtomicXadd(VReg baseAddr, int offset, VReg val) {
      // old = [base+offset]; [base+offset] = old + val; val = old
      var vr = R(val);
      EmitAtomicAddr(R(baseAddr), offset);
      var retry = $"__axadd_retry_{_e._uniqueLabelCounter++}";
      _e.DefineLabel(retry);
      _e.EmitWord(0xC85FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17)); // LDAXR X17, [X16] (old)
      _e.EmitAluRegReg(0x8B000000, ARM64Register.X14, ARM64Register.X17, vr);            // ADD X14, X17, val
      _e.EmitWord(0xC800FC00 | (15u << 16) | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X14)); // STLXR W15, X14, [X16]
      _e._condBranchFixups.Add((_e._code.Count, retry));
      _e.EmitWord(0x35000000 | 15u);                                                     // CBNZ W15, retry
      _e.EmitMovRegReg(vr, ARM64Register.X17);                                            // val = old
    }

    public void AtomicCAS(VReg destBase, int offset, VReg expected, VReg desired) {
      // ARM64 LDAXR/STLXR require the address in a register (no offset operand).
      // Effective address is precomputed once into X16 and kept across retries.
      // X17 is reused as both the loaded value and the STLXR status code — once
      // we've finished the CMP we no longer need the loaded value, so the same
      // register doubles as the status output.
      //
      // CLREX on the fail path explicitly clears the local-monitor reservation
      // taken by LDAXR — without it a subsequent unrelated LDAXR/STLXR pair on
      // this core could observe a stale reservation. Per CLAUDE.md, "no silent
      // fallthrough": the failure branch must do real work, not just B.NE-skip.
      var baseReg = R(destBase);
      var expectedReg = R(expected);
      var desiredReg = R(desired);
      var resultReg = R(VReg.Scratch3);

      if (expectedReg == ARM64Register.X16 || expectedReg == ARM64Register.X17 ||
          desiredReg == ARM64Register.X16 || desiredReg == ARM64Register.X17)
        throw new ArgumentException("AtomicCAS: expected/desired must not collide with X16/X17 scratch pair");

      // X16 = destBase + offset
      if (offset != 0) {
        _e.EmitAddSubImm(ARM64Register.X16, baseReg, offset, isAdd: true);
      } else if (baseReg != ARM64Register.X16) {
        _e.EmitMovRegReg(ARM64Register.X16, baseReg);
      }

      var retry = $"__cas_retry_{_e._uniqueLabelCounter}";
      var fail = $"__cas_fail_{_e._uniqueLabelCounter}";
      var done = $"__cas_done_{_e._uniqueLabelCounter++}";

      _e.DefineLabel(retry);
      // LDAXR X17, [X16]
      _e.EmitWord(0xC85FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17));
      // CMP X17, expected  (SUBS XZR, X17, expected)
      _e.EmitWord(0xEB00001F | (Reg(expectedReg) << 16) | (Reg(ARM64Register.X17) << 5));
      // B.NE fail
      _e._condBranchFixups.Add((_e._code.Count, fail));
      _e.EmitWord(0x54000001); // B.NE (cond=0001)

      // STLXR W17, desired, [X16] — status code goes back into X17.
      _e.EmitWord(0xC800FC00 | (Reg(ARM64Register.X16) << 5) | (17u << 16) | Reg(desiredReg));
      // CBNZ W17, retry (STLXR failed: lost exclusive monitor)
      _e._condBranchFixups.Add((_e._code.Count, retry));
      _e.EmitWord(0x35000000 | 17u);

      // Success path: result = 1
      _e.EmitMovRegImm(resultReg, 1);
      _e.EmitBranch(done);

      // Failure path: clear monitor, result = 0
      _e.DefineLabel(fail);
      // CLREX (clear local-monitor reservation): 0xD5033F5F
      _e.EmitWord(0xD5033F5F);
      _e.EmitMovRegImm(resultReg, 0);

      _e.DefineLabel(done);
    }

    public void SpinHint() => _e.EmitWord(0xD503203F); // YIELD

    public void FullBarrier() => _e.EmitDmbIsh();

    // LDAR/STLR take a bare [Xn] address (no offset form), so fold a non-zero
    // offset into X16 (a scratch not used by the VReg map: X0-X5 / X9-X12).
    public void LoadAcquire(VReg dest, VReg baseReg, int offset) {
      var addr = R(baseReg);
      if (offset != 0) {
        _e.EmitAddSubImm(ARM64Register.X16, R(baseReg), offset, isAdd: true);
        addr = ARM64Register.X16;
      }
      _e.EmitWord(0xC8DFFC00u | (Reg(addr) << 5) | Reg(R(dest))); // LDAR Xt, [Xn]
    }

    public void StoreRelease(VReg baseReg, int offset, VReg src) {
      var addr = R(baseReg);
      if (offset != 0) {
        _e.EmitAddSubImm(ARM64Register.X16, R(baseReg), offset, isAdd: true);
        addr = ARM64Register.X16;
      }
      _e.EmitWord(0xC89FFC00u | (Reg(addr) << 5) | Reg(R(src))); // STLR Xt, [Xn]
    }

    // ---- Labels & data ----

    public void DefineLabel(string label) => _e.DefineLabel(label);
    public void DefineGlobal(string label, int size, long initValue) =>
      _e.DefineGlobal(label, size, initValue);
    public void DefineSymdata(string label, byte[] data) => _e.DefineSymdata(label, data);

    // ---- Locking ----
    // Recursive spinlock: layout [lock(8) @ +0, owner(8) @ +8, count(8) @ +16]
    // Owner is the P* pointer (X28). Lock=0 means free, lock=1 means held.

    public void LockAcquire(string lockGlobal) {
      // X16 = &lock_global (layout: [lock(8), owner(8), count(8)])
      _e.EmitGlobalLeaReg(ARM64Register.X16, lockGlobal);

      var owned = $"__lock_owned_{_e._uniqueLabelCounter}";
      var spin = $"__lock_spin_{_e._uniqueLabelCounter}";
      var acquired = $"__lock_acquired_{_e._uniqueLabelCounter++}";

      // Check if we already own it: if [X16+8] == X28, go to owned
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X17, ARM64Register.X16, 8, 8); // X17 = owner
      // CMP X17, X28 (SUBS XZR, X17, X28)
      _e.EmitWord(0xEB1C023F);
      // B.EQ owned
      _e._condBranchFixups.Add((_e._code.Count, owned));
      _e.EmitWord(0x54000000); // B.EQ (cond=0000)

      // Spin to acquire lock
      _e.DefineLabel(spin);
      // LDAXR X17, [X16]
      _e.EmitWord(0xC85FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17));
      // CBNZ X17, spin (lock held by someone else)
      _e.EmitCbnz(ARM64Register.X17, spin);
      // STLXR W17, #1, [X16] — try to set lock to 1
      _e.EmitMovRegImm(ARM64Register.X17, 1);
      _e.EmitWord(0xC800FC00 | (Reg(ARM64Register.X16) << 5) | (17u << 16) | Reg(ARM64Register.X17));
      // CBNZ W17, spin (CAS failed)
      _e._condBranchFixups.Add((_e._code.Count, spin));
      _e.EmitWord(0x35000000 | 17u);
      // We got the lock — set owner = X28, count = 1
      _e.EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X28, ARM64Register.X16, 8, 8); // owner = P*
      _e.EmitMovRegImm(ARM64Register.X17, 1);
      _e.EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X17, ARM64Register.X16, 16, 8); // count = 1
      _e.EmitBranch(acquired);

      // Already owned: increment count
      _e.DefineLabel(owned);
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X17, ARM64Register.X16, 16, 8); // X17 = count
      _e.EmitAddSubImm(ARM64Register.X17, ARM64Register.X17, 1, isAdd: true);
      _e.EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X17, ARM64Register.X16, 16, 8); // count++

      _e.DefineLabel(acquired);
    }

    public void LockRelease(string lockGlobal) {
      // X16 = &lock_global
      _e.EmitGlobalLeaReg(ARM64Register.X16, lockGlobal);

      // Decrement count
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X17, ARM64Register.X16, 16, 8); // X17 = count
      _e.EmitAddSubImm(ARM64Register.X17, ARM64Register.X17, 1, isAdd: false); // count--
      _e.EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X17, ARM64Register.X16, 16, 8); // store count

      // If count > 0, still held — just return
      var done = $"__lock_release_done_{_e._uniqueLabelCounter++}";
      _e.EmitCbnz(ARM64Register.X17, done);

      // count == 0: clear owner and release lock
      _e.EmitMovRegImm(ARM64Register.X17, 0);
      _e.EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X17, ARM64Register.X16, 8, 8); // owner = 0
      // STLR XZR, [X16] — store-release of 0 to lock field
      _e.EmitWord(0xC89FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.Xzr));

      _e.DefineLabel(done);
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

    // ---- Shared memory (debugstream) ----

    public void OsOpenAndMapSharedMemory(VReg dest, VReg name_ptr, VReg size) {
      // Save size in callee-saved register X19 across calls
      _e.EmitMovRegReg(ARM64Register.X19, R(size));

      // open(path, O_RDWR) -> fd. MAXON_DEBUGSTREAM carries a PATH here, not a POSIX shm name:
      // the monitor backs the segment with a temp file because .NET cannot create a named map off
      // Windows (see DebugStreamMonitor.SharedMapping). `shm_open` is deliberately NOT used —
      // it is variadic, and on Apple arm64 variadic arguments do not travel in X2, so the `mode`
      // this call site puts there is never read: the object would be created with mode 0 and every
      // later open would fail EACCES. `open` reads `mode` only under O_CREAT, which is absent, so
      // the X2=0 below is unread by either function and the call is ABI-correct as written.
      _e.EmitMovRegReg(ARM64Register.X0, R(name_ptr));
      _e.EmitMovRegImm(ARM64Register.X1, 2); // O_RDWR
      _e.EmitMovRegImm(ARM64Register.X2, 0);
      var resolvedOpen = ResolveImport("open");
      _e.EmitCallImport(resolvedOpen);
      // X0 = fd (-1 on failure)
      var failLabel = $"__ds_shm_fail_{_e._uniqueLabelCounter++}";
      var doneLabel = $"__ds_shm_done_{_e._uniqueLabelCounter++}";
      _e.EmitAddSubImm(ARM64Register.X9, ARM64Register.X0, 1, isAdd: true); // X9 = fd + 1
      _e.EmitCbz(ARM64Register.X9, failLabel); // if fd == -1, fail

      // mmap(NULL, size, PROT_READ|PROT_WRITE, MAP_SHARED, fd, 0)
      _e.EmitMovRegReg(ARM64Register.X4, ARM64Register.X0); // fd
      _e.EmitMovRegReg(ARM64Register.X1, ARM64Register.X19); // size
      _e.EmitMovRegImm(ARM64Register.X0, 0);     // addr = NULL
      _e.EmitMovRegImm(ARM64Register.X2, 3);     // PROT_READ | PROT_WRITE
      _e.EmitMovRegImm(ARM64Register.X3, 1);     // MAP_SHARED
      _e.EmitMovRegImm(ARM64Register.X5, 0);     // offset = 0
      var resolvedMmap = ResolveImport("mmap");
      _e.EmitCallImport(resolvedMmap);
      _e.EmitBranch(doneLabel);

      _e.DefineLabel(failLabel);
      _e.EmitMovRegImm(ARM64Register.X0, 0); // return NULL

      _e.DefineLabel(doneLabel);
      var destReg = R(dest);
      if (destReg != ARM64Register.X0)
        _e.EmitMovRegReg(destReg, ARM64Register.X0);
    }

    public void OsUnmapSharedMemory(VReg base_ptr, VReg size) {
      // munmap(base, size)
      _e.EmitMovRegReg(ARM64Register.X0, R(base_ptr));
      _e.EmitMovRegReg(ARM64Register.X1, R(size));
      var resolvedMunmap = ResolveImport("munmap");
      _e.EmitCallImport(resolvedMunmap);
    }

    public void OsYield() => _e.EmitCallImport(ResolveImport("sched_yield"));

    // usleep takes microseconds; the portable unit is milliseconds (see IEmitterBackend), so scale.
    public void OsSleepMillis(VReg millis) {
      _e.EmitMovRegReg(ARM64Register.X0, R(millis));
      _e.EmitMovRegImm(ARM64Register.X1, MicrosPerMilli);
      // MUL X0, X0, X1 = MADD X0, X0, X1, XZR
      _e.EmitWord(0x9B007C00 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)
        | Reg(ARM64Register.X0));
      _e.EmitCallImport(ResolveImport("usleep"));
    }

    // getenv returns a pointer straight into the environment block, so POSIX needs no buffer and
    // ignores scratchSlot — the parameter exists for Windows, which copies.
    public void ReadEnvUnsigned(VReg dest, string nameSymdata, int scratchSlot) {
      _e.EmitAdrpAddFixup(ARM64Register.X0, _e._symdataAdrpFixups, nameSymdata);
      _e.EmitCallImport(ResolveImport("getenv"));
      _e.EmitParseUnsignedCstrIntoX9(ARM64Register.X0);
      _e.EmitMovRegReg(R(dest), ARM64Register.X9);
    }

    // ---- Bulk memory ----

    public void FillMemoryQwords(VReg destAddr, VReg value, VReg count) {
      // Tight loop: STR value, [dest], #8 / SUB count, 1 / CBNZ count, loop
      var loopLabel = $"__fill_qwords_{_e._uniqueLabelCounter++}";
      _e.DefineLabel(loopLabel);
      // STR Xvalue, [Xdest], #8 (post-index)
      _e.EmitWord(0xF8008400 | (Reg(R(destAddr)) << 5) | Reg(R(value)));
      // SUB Xcount, Xcount, #1
      _e.EmitWord(0xD1000400u | (Reg(R(count)) << 5) | Reg(R(count)));
      // CBNZ Xcount, loopLabel
      _e.EmitCbnz(R(count), loopLabel);
    }

    // ---- Scheduler platform helpers ----

    private const int CLOCK_UPTIME_RAW = 0x08; // macOS monotonic clock
    private const int CLOCK_MONOTONIC = 0x06;  // macOS _CLOCK_MONOTONIC (POSIX-standard monotonic)
    private const int CLOCK_REALTIME = 0x00;   // macOS _CLOCK_REALTIME (wall clock, counts from the Unix epoch)
    private const int CLOCK_THREAD_CPUTIME_ID = 0x10; // macOS _CLOCK_THREAD_CPUTIME_ID (this thread's CPU time)

    /// <summary>Nanoseconds in a second: the tv_sec -> nanosecond scale of a `struct timespec`.</summary>
    private const long NanosPerSecond = 1_000_000_000L;

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

    public void GetCurrentTimeNanos(VReg dest, int scratchSlot) {
      // clock_gettime(CLOCK_MONOTONIC, &timespec). Same shape as GetCurrentTimeMs, but it
      // reads the POSIX-standard monotonic clock rather than CLOCK_UPTIME_RAW, and it keeps
      // the timespec's native nanosecond precision instead of dividing it away.
      int tsOff = 16 + scratchSlot * 8; // timespec occupies slots scratchSlot, scratchSlot+1
      _e.EmitMovRegImm(ARM64Register.X0, CLOCK_MONOTONIC);
      _e.EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, tsOff, isAdd: true);
      _e.EmitCallImport("clock_gettime");

      // nanos = tv_sec * 1e9 + tv_nsec. tv_nsec is < 1e9 by the timespec contract, so the
      // sum is exact in 64 bits (it only overflows after ~584 years of uptime).
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, tsOff, 8); // tv_sec
      _e.EmitMovRegImm(ARM64Register.X3, NanosPerSecond);
      _e.EmitWord(0x9B037C42); // MUL X2, X2, X3
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, tsOff + 8, 8); // tv_nsec
      _e.EmitWord(0x8B040042); // ADD X2, X2, X4

      var destReg = R(dest);
      if (destReg != ARM64Register.X2)
        _e.EmitMovRegReg(destReg, ARM64Register.X2);
    }

    public void GetCurrentUnixTimeSeconds(VReg dest, int scratchSlot) {
      // clock_gettime(CLOCK_REALTIME, &timespec), occupying slots scratchSlot and scratchSlot+1.
      int tsOff = 16 + scratchSlot * 8; // [x29 + 16 + slot*8]
      _e.EmitMovRegImm(ARM64Register.X0, CLOCK_REALTIME);
      _e.EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, tsOff, isAdd: true);
      _e.EmitCallImport("clock_gettime");

      // tv_sec IS the answer. CLOCK_REALTIME already counts from the Unix epoch, so unlike the
      // two monotonic clocks above there is nothing to rebase and nothing to scale — tv_nsec is
      // simply dropped, which is exactly the truncation to whole seconds the caller asked for.
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, tsOff, 8); // tv_sec

      var destReg = R(dest);
      if (destReg != ARM64Register.X2)
        _e.EmitMovRegReg(destReg, ARM64Register.X2);
    }

    public void GetThreadCpuTicks(VReg dest, int scratchSlot) {
      // clock_gettime(CLOCK_THREAD_CPUTIME_ID, &timespec) — structurally identical to
      // GetCurrentTimeNanos, reading a different clock id. That clock advances only while
      // this thread is scheduled, so unlike the three above it cannot see preemption.
      //
      // POSIX hands back a timespec, so this backend's unit is NANOSECONDS where the Windows
      // one is TSC ticks. The two are not comparable and nothing here pretends to convert.
      int tsOff = 16 + scratchSlot * 8; // timespec occupies slots scratchSlot, scratchSlot+1
      _e.EmitMovRegImm(ARM64Register.X0, CLOCK_THREAD_CPUTIME_ID);
      _e.EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, tsOff, isAdd: true);
      _e.EmitCallImport("clock_gettime");

      // nanos = tv_sec * 1e9 + tv_nsec, exact in 64 bits — tv_nsec is < 1e9 by the timespec
      // contract, and a thread would have to accumulate ~584 years of CPU time to overflow.
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, tsOff, 8); // tv_sec
      _e.EmitMovRegImm(ARM64Register.X3, NanosPerSecond);
      _e.EmitWord(0x9B037C42); // MUL X2, X2, X3
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, tsOff + 8, 8); // tv_nsec
      _e.EmitWord(0x8B040042); // ADD X2, X2, X4

      var destReg = R(dest);
      if (destReg != ARM64Register.X2)
        _e.EmitMovRegReg(destReg, ARM64Register.X2);
    }

    /// <summary>PRIO_PROCESS — setpriority/getpriority's "who is a process id" selector.</summary>
    private const long PrioProcess = 0L;

    /// <summary>The nice value background work runs at. POSIX nice runs the OPPOSITE direction to a
    /// Windows priority class — larger is lower — which is why nothing converts between the two. Not
    /// 19 (the maximum) for the same reason Windows does not use IDLE: a starve long enough to trip a
    /// caller's timeout gets reported as a harness bug rather than as a busy machine.</summary>
    private const long BackgroundNiceValue = 10L;

    public void EnterBackgroundPriority(VReg dest) {
      // setpriority(PRIO_PROCESS, 0, 10). `who = 0` means THIS process, and on Darwin nice is scoped
      // to the process — which is what discharges the inheritance contract on this lane, covering
      // threads created before and after. ⚠ That is a DARWIN property, not a POSIX one: on Linux nice
      // is per-thread, so a Linux lane owes real work here. See IEmitterBackend.EnterBackgroundPriority.
      _e.EmitMovRegImm(ARM64Register.X0, PrioProcess);
      _e.EmitMovRegImm(ARM64Register.X1, 0);
      _e.EmitMovRegImm(ARM64Register.X2, BackgroundNiceValue);
      _e.EmitCallImport("setpriority");

      // Read back rather than reporting the value we just wrote — a lowering that called nothing must
      // not be able to answer like one that worked. Only RAISING nice is unprivileged, and raising is
      // all this does, so the write cannot be silently refused.
      _e.EmitMovRegImm(ARM64Register.X0, PrioProcess);
      _e.EmitMovRegImm(ARM64Register.X1, 0);
      _e.EmitCallImport("getpriority");

      // getpriority answers a signed int in W0 and nice is legitimately negative (-20..19), so the
      // upper half of X0 must be SIGN-extended, not left as the ABI happens to leave it.
      _e.EmitWord(0x93407C00); // SXTW X0, W0

      var destReg = R(dest);
      if (destReg != ARM64Register.X0)
        _e.EmitMovRegReg(destReg, ARM64Register.X0);
    }

    public void GetCurrentProcessId(VReg dest) {
      // POSIX getpid() returns a pid_t (32-bit). Zero-extends naturally
      // into X0 for the caller's i64 result.
      _e.EmitCallImport("getpid");
      var destReg = R(dest);
      if (destReg != ARM64Register.X0)
        _e.EmitMovRegReg(destReg, ARM64Register.X0);
    }

    public void DriveSchedulerAndIo() => _e.EmitDriveSchedulerAndIo();

    public void SwitchToMainThread() => _e.EmitSwitchToMainThread();

    public void WakeWorker(VReg p) {
      // Go semawakeup on p->wakeSemaphore's lock block (mutex+cond+count).
      // POffWakeSemaphore = 0x38. Loads the block pointer into X9, then signals.
      _e.EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, R(p), 0x38, 8);
      _e.EmitSemaWakeup(ARM64Register.X9);
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
      _e.EmitWord(0x9AC00800 | (Reg(ARM64Register.X16) << 16) | (Reg(d) << 5) | Reg(ARM64Register.X17));
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
      _e.EmitWord(0x9AC00800 | (Reg(v) << 16) | (Reg(d) << 5) | Reg(ARM64Register.X16));
      // MSUB dest, X16, v, d → dest = d - X16*v = d % v
      var dr = R(dest);
      _e.EmitWord(0x9B008000 | (Reg(v) << 16) | (Reg(d) << 10) | (Reg(ARM64Register.X16) << 5) | Reg(dr));
    }

    // ---- Platform-specific labels ----

    public string WriteStderrLabel => "rt_write_cstr_stderr";
    public string SymbolTableLabel => "__symtab";

    // ---- Local address / byte memory ----

    public void LeaLocal(VReg dest, int slotIndex) {
      // ADD R(dest), X29, #(16 + slotIndex*8) — ARM64 args start at [x29+16]
      int offset = 16 + slotIndex * 8;
      _e.EmitAddSubImm(R(dest), ARM64Register.X29, offset, isAdd: true);
    }

    public void StoreIndirectByte(VReg baseReg, int offset, VReg src) {
      // STRB W(src), [R(base), #offset]. Routed through the shared narrow load/store
      // encoder (size 1): it keeps the scaled unsigned-offset form — byte-identical to
      // the previous hand-rolled STRB — for 0..4095, drops to the unscaled signed form
      // for small negatives, and materializes the address into X16 for anything wider.
      // The old `imm12 = offset & 0xFFF` masked any offset past 4095 (and every negative
      // offset), silently storing to the wrong byte.
      _e.EmitStoreIndirect(R(baseReg), offset, R(src), 1);
    }

    public void LoadIndirectByte(VReg dest, VReg baseReg, int offset) {
      // LDRB W(dest), [R(base), #offset]. See StoreIndirectByte: the shared encoder
      // covers the full offset range instead of masking to a 12-bit immediate.
      _e.EmitLoadIndirect(R(dest), R(baseReg), offset, 1);
    }

    // ---- Platform info ----

    public string SchedLockLabel => "__sched_global_lock";
    public string TimerLockLabel => "__sched_timer_lock";

    // os_unfair_lock, NOT the recursive LockAcquire above — this is the primitive __gt_spawn and
    // __gt_trampoline already use on this same word. See IEmitterBackend.AllThreadsLockAcquire.
    public void AllThreadsLockAcquire() => _e.EmitLockAcquire("__sched_all_lock");
    public void AllThreadsLockRelease() => _e.EmitLockRelease("__sched_all_lock");

    // ---- Fault handler (real impls land in Phase 3) ----

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
      _e.EmitBranchLink("mrt_fault_backtrace");
    }

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
