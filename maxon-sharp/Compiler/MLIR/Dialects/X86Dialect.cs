using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public enum X86Register {
  Rax, Rcx, Rdx, Rbx, Rsp, Rbp, Rsi, Rdi,
  R8, R9, R10, R11, R12, R13, R14, R15,
  Eax, Ecx, Edx, Ebx, Esp, Ebp, Esi, Edi
}

public enum X86XmmRegister {
  Xmm0, Xmm1, Xmm2, Xmm3, Xmm4, Xmm5, Xmm6, Xmm7,
  Xmm8, Xmm9, Xmm10, Xmm11, Xmm12, Xmm13, Xmm14, Xmm15
}

public abstract class X86Op : IPrintableOp {
  public abstract string Mnemonic { get; }
  public IReadOnlyList<string> PrintableResults => [];
  public IReadOnlyList<string> PrintableOperands => [];
  public IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes => new Dictionary<string, MlirAttribute>();
}

public class X86PrologueOp(int stackSize) : X86Op {
  public int StackSize { get; } = stackSize;
  public override string Mnemonic => $"x86.prologue stack_size={StackSize}";
}

public class X86EpilogueOp : X86Op {
  public override string Mnemonic => "x86.epilogue";
}

public class X86PushRegOp(X86Register register) : X86Op {
  public X86Register Register { get; } = register;
  public override string Mnemonic => $"x86.push {Register.ToString().ToLower()}";
}

public class X86PopRegOp(X86Register register) : X86Op {
  public X86Register Register { get; } = register;
  public override string Mnemonic => $"x86.pop {Register.ToString().ToLower()}";
}

public class X86MovRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x86.mov {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86XchgRegRegOp(X86Register a, X86Register b) : X86Op {
  public X86Register A { get; } = a;
  public X86Register B { get; } = b;
  public override string Mnemonic => $"x86.xchg {A.ToString().ToLower()}, {B.ToString().ToLower()}";
}

public class X86MovRegImmOp(X86Register dest, long immediate) : X86Op {
  public X86Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"x86.mov {Dest.ToString().ToLower()}, {Immediate}";
}

public class X86RetOp : X86Op {
  public override string Mnemonic => "x86.ret";
}

public class X86SubRegImmOp(X86Register dest, long immediate) : X86Op {
  public X86Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"x86.sub {Dest.ToString().ToLower()}, {Immediate}";
}

public class X86AddRegImmOp(X86Register dest, long immediate) : X86Op {
  public X86Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"x86.add {Dest.ToString().ToLower()}, {Immediate}";
}

public class X86AddRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x86.add {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86SubRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x86.sub {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86XorRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x86.xor {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86ImulRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x86.imul {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CallDirectOp(string target) : X86Op {
  public string Target { get; } = target;
  public override string Mnemonic => $"x86.call {Target}";
}

public class X86MovMemRegOp(int displacement, X86Register src) : X86Op {
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x86.mov [rbp{(Displacement >= 0 ? "+" : "")}{Displacement}], {Src.ToString().ToLower()}";
}

public class X86MovRegMemOp(X86Register dest, int displacement) : X86Op {
  public X86Register Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x86.mov {Dest.ToString().ToLower()}, [rbp{(Displacement >= 0 ? "+" : "")}{Displacement}]";
}

public class X86MovMemRspRegOp(int offset, X86Register src) : X86Op {
  public int Offset { get; } = offset;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x86.mov [rsp+{Offset}], {Src.ToString().ToLower()}";
}

public class X86MovSdXmmRipRelOp(X86XmmRegister dest, string rdataLabel) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public override string Mnemonic => $"x86.movsd {Dest.ToString().ToLower()}, [rip+{RdataLabel}]";
}

public class X86MovSdMemXmmOp(int displacement, X86XmmRegister src) : X86Op {
  public int Displacement { get; } = displacement;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.movsd [rbp{Displacement}], {Src.ToString().ToLower()}";
}

public class X86MovSdXmmXmmOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.movsd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86MovSdXmmMemOp(X86XmmRegister dest, int displacement) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x86.movsd {Dest.ToString().ToLower()}, [rbp{Displacement}]";
}

public class X86AddSdOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.addsd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86SubSdOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.subsd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86MulSdOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.mulsd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86DivSdOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.divsd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CvttSd2SiOp(X86Register dest, X86XmmRegister src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.cvttsd2si {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86AndpdRipRelOp(X86XmmRegister dest, string rdataLabel) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public override string Mnemonic => $"x86.andpd {Dest.ToString().ToLower()}, [rip+{RdataLabel}]";
}

public class X86SqrtSdOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.sqrtsd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86RoundSdOp(X86XmmRegister dest, X86XmmRegister src, byte mode) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public byte Mode { get; } = mode;
  public override string Mnemonic => $"x86.roundsd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}, {Mode}";
}

public class X86MinSdOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.minsd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86MaxSdOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.maxsd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CmpRegRegOp(X86Register lhs, X86Register rhs) : X86Op {
  public X86Register Lhs { get; } = lhs;
  public X86Register Rhs { get; } = rhs;
  public override string Mnemonic => $"x86.cmp {Lhs.ToString().ToLower()}, {Rhs.ToString().ToLower()}";
}

public class X86TestRegRegOp(X86Register lhs, X86Register rhs) : X86Op {
  public X86Register Lhs { get; } = lhs;
  public X86Register Rhs { get; } = rhs;
  public override string Mnemonic => $"x86.test {Lhs.ToString().ToLower()}, {Rhs.ToString().ToLower()}";
}

public class X86UcomisdOp(X86XmmRegister src1, X86XmmRegister src2) : X86Op {
  public X86XmmRegister Src1 { get; } = src1;
  public X86XmmRegister Src2 { get; } = src2;
  public override string Mnemonic => $"x86.ucomisd {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class X86SetccOp(string condition, X86Register dest) : X86Op {
  public string Condition { get; } = condition;
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x86.set{Condition} {Dest.ToString().ToLower()}";
}

public class X86MovzxRegOp(X86Register dest) : X86Op {
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x86.movzx {Dest.ToString().ToLower()}, {Dest.ToString().ToLower()}b";
}

public class X86JccOp(string condition, string target) : X86Op {
  public string Condition { get; } = condition;
  public string Target { get; } = target;
  public override string Mnemonic => $"x86.j{Condition} {Target}";
}

public class X86JmpOp(string target) : X86Op {
  public string Target { get; } = target;
  public override string Mnemonic => $"x86.jmp {Target}";
}

public class X86CqoOp : X86Op {
  public override string Mnemonic => "x86.cqo";
}

public class X86IdivRegOp(X86Register divisor) : X86Op {
  public X86Register Divisor { get; } = divisor;
  public override string Mnemonic => $"x86.idiv {Divisor.ToString().ToLower()}";
}

// LEA dest, [rbp+disp] - load effective address of a stack variable
public class X86LeaRegMemOp(X86Register dest, int displacement) : X86Op {
  public X86Register Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x86.lea {Dest.ToString().ToLower()}, [rbp{(Displacement >= 0 ? "+" : "")}{Displacement}]";
}

// MOV [baseReg+disp], srcReg - store through register-indirect addressing
public class X86MovIndirectMemRegOp(X86Register baseReg, int displacement, X86Register src) : X86Op {
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x86.mov [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}";
}

// MOV destReg, [baseReg+disp] - load through register-indirect addressing
public class X86MovRegIndirectMemOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x86.mov {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOVSD [baseReg+disp], xmm - store float through register-indirect addressing
public class X86MovSdIndirectMemXmmOp(X86Register baseReg, int displacement, X86XmmRegister src) : X86Op {
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x86.movsd [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}";
}

// MOVSD xmm, [baseReg+disp] - load float through register-indirect addressing
public class X86MovSdXmmIndirectMemOp(X86XmmRegister dest, X86Register baseReg, int displacement) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x86.movsd {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}+{Displacement}]";
}
