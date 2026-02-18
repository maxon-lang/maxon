using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Peephole optimization on x86 ops.
/// - add rX, rY; mov rZ, rX → lea rZ, [rX + rY] (when rX is dead after)
/// - mov rX, [rbp+off]; mov rY, rX → mov rY, [rbp+off] (when rX is dead after)
/// </summary>
public static class PeepholePass {
  public static void Run(MlirModule<X86Op> module) {
    foreach (var func in module.Functions) {
      Optimize(func);
    }
  }

  private static void Optimize(MlirFunction<X86Op> func) {
    foreach (var block in func.Body.Blocks) {
      var ops = block.Operations;
      for (int i = 0; i < ops.Count - 1; i++) {
        if (ops[i + 1] is X86MovRegRegOp mov) {
          // add rX, rY; mov rZ, rX → lea rZ, [rX + rY]
          if (ops[i] is X86AddRegRegOp add
              && mov.Src == add.Dest && mov.Dest != add.Dest
              && !IsRegReadAfter(ops, i + 2, add.Dest)) {
            ops[i] = new X86LeaRegRegRegOp(mov.Dest, add.Dest, add.Src);
            ops.RemoveAt(i + 1);
          }
          // mov rX, [rbp+off]; mov rY, rX → mov rY, [rbp+off]
          else if (ops[i] is X86MovRegMemOp load
              && To64Bit(mov.Src) == To64Bit(load.Dest) && mov.Dest != load.Dest
              && !IsRegReadAfter(ops, i + 2, load.Dest)) {
            ops[i] = new X86MovRegMemOp(mov.Dest, load.Displacement, load.SizeInBytes);
            ops.RemoveAt(i + 1);
          }
        }
      }
    }
  }

  private static bool IsRegReadAfter(List<X86Op> ops, int startIndex, X86Register reg) {
    var reg64 = To64Bit(reg);
    for (int i = startIndex; i < ops.Count; i++) {
      foreach (var r in GetReadRegisters(ops[i])) {
        if (To64Bit(r) == reg64) return true;
      }
    }
    return false;
  }

  private static X86Register To64Bit(X86Register reg) => reg switch {
    X86Register.Eax => X86Register.Rax,
    X86Register.Ecx => X86Register.Rcx,
    X86Register.Edx => X86Register.Rdx,
    X86Register.Ebx => X86Register.Rbx,
    X86Register.Esp => X86Register.Rsp,
    X86Register.Ebp => X86Register.Rbp,
    X86Register.Esi => X86Register.Rsi,
    X86Register.Edi => X86Register.Rdi,
    _ => reg
  };

  /// <summary>
  /// Return the GPR registers read (used as sources) by an X86 op.
  /// Unrecognized ops conservatively return all GPRs to prevent unsafe optimizations.
  /// </summary>
  private static IEnumerable<X86Register> GetReadRegisters(X86Op op) {
    switch (op) {
      // Pure writes (no GPR reads)
      case X86PrologueOp:
      case X86EpilogueOp:
      case X86MovRegImmOp:
      case X86MovRegMemOp:
      case X86LeaRegMemOp:
      case X86LeaRipRelOp:
      case X86LeaFuncAddrOp:
      case X86SetccOp:
      case X86JccOp:
      case X86JmpOp:
      case X86RetOp:
      case X86GlobalLoadOp:
      case X86CvttFloat2SiOp:
        break;
      // Single GPR read
      case X86MovRegRegOp mov: yield return mov.Src; break;
      case X86PushRegOp push: yield return push.Register; break;
      case X86PopRegOp: break;
      case X86AddRegImmOp addImm: yield return addImm.Dest; break;
      case X86SubRegImmOp subImm: yield return subImm.Dest; break;
      case X86MovzxRegOp movzx: yield return movzx.Dest; break;
      case X86MovMemRegOp store: yield return store.Src; break;
      case X86MovMemRspRegOp storeRsp: yield return storeRsp.Src; break;
      case X86GlobalStoreOp gs: yield return gs.Src; break;
      case X86CallIndirectOp callInd: yield return callInd.Target; break;
      case X86CvtSi2FloatOp cvt: yield return cvt.Src; break;
      // Two GPR reads
      case X86AddRegRegOp add: yield return add.Dest; yield return add.Src; break;
      case X86SubRegRegOp sub: yield return sub.Dest; yield return sub.Src; break;
      case X86AndRegRegOp and: yield return and.Dest; yield return and.Src; break;
      case X86OrRegRegOp or: yield return or.Dest; yield return or.Src; break;
      case X86XorRegRegOp xor: yield return xor.Dest; yield return xor.Src; break;
      case X86ImulRegRegOp imul: yield return imul.Dest; yield return imul.Src; break;
      case X86XchgRegRegOp xchg: yield return xchg.A; yield return xchg.B; break;
      case X86CmpRegRegOp cmp: yield return cmp.Lhs; yield return cmp.Rhs; break;
      case X86TestRegRegOp test: yield return test.Lhs; yield return test.Rhs; break;
      case X86LeaRegRegRegOp lea: yield return lea.BaseReg; yield return lea.Index; break;
      case X86MovIndirectMemRegOp storeInd: yield return storeInd.BaseReg; yield return storeInd.Src; break;
      case X86MovRegIndirectMemOp loadInd: yield return loadInd.BaseReg; break;
      case X86MovzxRegByteIndirectOp movzxInd: yield return movzxInd.BaseReg; break;
      case X86MovByteIndirectRegOp storeByteInd: yield return storeByteInd.BaseReg; yield return storeByteInd.Src; break;
      case X86MovzxRegWordIndirectOp movzxWordInd: yield return movzxWordInd.BaseReg; break;
      case X86MovWordIndirectRegOp storeWordInd: yield return storeWordInd.BaseReg; yield return storeWordInd.Src; break;
      // Shift reads dest + implicit ECX
      case X86ShlRegClOp shl: yield return shl.Dest; yield return X86Register.Ecx; break;
      case X86SarRegClOp sar: yield return sar.Dest; yield return X86Register.Ecx; break;
      case X86ShrRegClOp shr: yield return shr.Dest; yield return X86Register.Ecx; break;
      // IDIV reads RAX, RDX, and divisor
      case X86CqoOp: yield return X86Register.Rax; break;
      case X86IdivRegOp idiv: yield return X86Register.Rax; yield return X86Register.Rdx; yield return idiv.Divisor; break;
      // REP MOVSB reads RSI, RDI, RCX
      case X86RepMovsbOp: yield return X86Register.Rsi; yield return X86Register.Rdi; yield return X86Register.Rcx; break;
      // REP STOSQ reads RAX, RDI, RCX
      case X86RepStosqOp: yield return X86Register.Rax; yield return X86Register.Rdi; yield return X86Register.Rcx; break;
      // XMM ops that read GPR base registers
      case X86MovIndirectMemXmmOp sdStoreInd: yield return sdStoreInd.BaseReg; break;
      case X86MovXmmIndirectMemOp sdLoadInd: yield return sdLoadInd.BaseReg; break;
      // Calls/imports: conservatively assume all caller-saved registers are read
      case X86CallDirectOp:
      case X86CallImportOp:
        yield return X86Register.Rcx; yield return X86Register.Rdx;
        yield return X86Register.R8; yield return X86Register.R9;
        break;
      // Conservative default: assume all GPRs are read
      default:
        yield return X86Register.Rax; yield return X86Register.Rcx;
        yield return X86Register.Rdx; yield return X86Register.Rbx;
        yield return X86Register.Rsi; yield return X86Register.Rdi;
        yield return X86Register.R8; yield return X86Register.R9;
        yield return X86Register.R10; yield return X86Register.R11;
        break;
    }
  }
}
