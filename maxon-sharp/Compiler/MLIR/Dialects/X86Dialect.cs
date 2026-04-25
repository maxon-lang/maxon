using MaxonSharp.Compiler.Ir.Core;

namespace MaxonSharp.Compiler.Ir.Dialects;

public enum X86Register {
  Rax, Rcx, Rdx, Rbx, Rsp, Rbp, Rsi, Rdi,
  R8, R9, R10, R11, R12, R13, R14, R15,
  Eax, Ecx, Edx, Ebx, Esp, Ebp, Esi, Edi
}

public enum X86XmmRegister {
  Xmm0, Xmm1, Xmm2, Xmm3, Xmm4, Xmm5, Xmm6, Xmm7,
  Xmm8, Xmm9, Xmm10, Xmm11, Xmm12, Xmm13, Xmm14, Xmm15
}

public enum FloatPrecision { F64, F32 }

public enum X86OpKind {
  Prologue,
  Epilogue,
  PushReg,
  PopReg,
  MovRegReg,
  Movsxd,
  XchgRegReg,
  MovRegImm,
  Ret,
  SubRegImm,
  AddRegImm,
  AddRegReg,
  SubRegReg,
  AndRegReg,
  OrRegReg,
  XorRegReg,
  ShlRegCl,
  SarRegCl,
  ShrRegCl,
  ImulRegReg,
  CallDirect,
  CallIndirect,
  MovMemReg,
  MovRegMem,
  MovMemRspReg,
  MovXmmRipRel,
  MovMemXmm,
  MovXmmXmm,
  MovXmmMem,
  AddXmm,
  SubXmm,
  MulXmm,
  DivXmm,
  CvttFloat2Si,
  MovqXmmToGpr,
  CvtSi2Float,
  CvtSd2Ss,
  CvtSs2Sd,
  AndMaskRipRel,
  SqrtXmm,
  RoundXmm,
  MinXmm,
  MaxXmm,
  CmpRegReg,
  TestRegReg,
  UcomisXmm,
  Setcc,
  MovzxReg,
  CmovneRegReg,
  Jcc,
  Jmp,
  JumpTable,
  LabelDef,
  Cqo,
  Cdq,
  IdivReg,
  IdivReg32,
  DivReg,
  DivReg32,
  LeaRegMem,
  LeaRipRel,
  LeaSymdataRel,
  LeaUcddataRel,
  LeaFuncAddr,
  LeaRegRegReg,
  MovIndirectMemReg,
  MovRegIndirectMem,
  MovIndirectMemXmm,
  MovXmmIndirectMem,
  MovzxRegByteIndirect,
  MovsxRegByteIndirect,
  MovByteIndirectReg,
  MovzxRegWordIndirect,
  MovsxRegWordIndirect,
  MovRegDwordIndirect,
  MovsxdRegDwordIndirect,
  MovDwordIndirectReg,
  MovWordIndirectReg,
  GlobalLoad,
  GlobalStore,
  GlobalLoadXmm,
  GlobalStoreXmm,
  RepMovsb,
  Std,
  Cld,
  RepStosq,
  CallImport,
}

public abstract class X86Op : IPrintableOp {
  public abstract X86OpKind Kind { get; }
  public abstract string Mnemonic { get; }
  public IReadOnlyList<string> PrintableResults => [];
  public IReadOnlyList<string> PrintableOperands => [];
  public IReadOnlyDictionary<string, IrAttribute> PrintableAttributes => new Dictionary<string, IrAttribute>();
}

public sealed class X86PrologueOp(int stackSize) : X86Op {
  public override X86OpKind Kind => X86OpKind.Prologue;
  public int StackSize { get; } = stackSize;
  public override string Mnemonic => $"x64.prologue stack_size={StackSize}";
}

public sealed class X86EpilogueOp : X86Op {
  public override X86OpKind Kind => X86OpKind.Epilogue;
  public override string Mnemonic => "x64.epilogue";
}

public sealed class X86PushRegOp(X86Register register) : X86Op {
  public override X86OpKind Kind => X86OpKind.PushReg;
  public X86Register Register { get; } = register;
  public override string Mnemonic => $"x64.push {Register.ToString().ToLower()}";
}

public sealed class X86PopRegOp(X86Register register) : X86Op {
  public override X86OpKind Kind => X86OpKind.PopReg;
  public X86Register Register { get; } = register;
  public override string Mnemonic => $"x64.pop {Register.ToString().ToLower()}";
}

public sealed class X86MovRegRegOp(X86Register dest, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovRegReg;
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// MOVSXD r64, r/m32: sign-extend 32-bit to 64-bit
public sealed class X86MovsxdOp(X86Register dest, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.Movsxd;
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.movsxd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86XchgRegRegOp(X86Register a, X86Register b) : X86Op {
  public override X86OpKind Kind => X86OpKind.XchgRegReg;
  public X86Register A { get; } = a;
  public X86Register B { get; } = b;
  public override string Mnemonic => $"x64.xchg {A.ToString().ToLower()}, {B.ToString().ToLower()}";
}

public sealed class X86MovRegImmOp(X86Register dest, long immediate) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovRegImm;
  public X86Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"x64.mov {Dest.ToString().ToLower()}, {Immediate}";
}

public sealed class X86RetOp : X86Op {
  public override X86OpKind Kind => X86OpKind.Ret;
  public override string Mnemonic => "x64.ret";
}

public sealed class X86SubRegImmOp(X86Register dest, long immediate) : X86Op {
  public override X86OpKind Kind => X86OpKind.SubRegImm;
  public X86Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"x64.sub {Dest.ToString().ToLower()}, {Immediate}";
}

public sealed class X86AddRegImmOp(X86Register dest, long immediate) : X86Op {
  public override X86OpKind Kind => X86OpKind.AddRegImm;
  public X86Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"x64.add {Dest.ToString().ToLower()}, {Immediate}";
}

public sealed class X86AddRegRegOp(X86Register dest, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.AddRegReg;
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.add {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86SubRegRegOp(X86Register dest, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.SubRegReg;
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.sub {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86AndRegRegOp(X86Register dest, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.AndRegReg;
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.and {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86OrRegRegOp(X86Register dest, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.OrRegReg;
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.or {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86XorRegRegOp(X86Register dest, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.XorRegReg;
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => Dest == Src
    ? $"x64.xor {RegName32(Dest)}, {RegName32(Src)}"
    : $"x64.xor {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";

  private static string RegName32(X86Register reg) => reg switch {
    X86Register.Rax => "eax",
    X86Register.Rcx => "ecx",
    X86Register.Rdx => "edx",
    X86Register.Rbx => "ebx",
    X86Register.Rsp => "esp",
    X86Register.Rbp => "ebp",
    X86Register.Rsi => "esi",
    X86Register.Rdi => "edi",
    X86Register.R8 => "r8d",
    X86Register.R9 => "r9d",
    X86Register.R10 => "r10d",
    X86Register.R11 => "r11d",
    X86Register.R12 => "r12d",
    X86Register.R13 => "r13d",
    X86Register.R14 => "r14d",
    X86Register.R15 => "r15d",
    _ => reg.ToString().ToLower()
  };
}

public sealed class X86ShlRegClOp(X86Register dest) : X86Op {
  public override X86OpKind Kind => X86OpKind.ShlRegCl;
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.shl {Dest.ToString().ToLower()}, cl";
}

public sealed class X86SarRegClOp(X86Register dest) : X86Op {
  public override X86OpKind Kind => X86OpKind.SarRegCl;
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.sar {Dest.ToString().ToLower()}, cl";
}

public sealed class X86ShrRegClOp(X86Register dest) : X86Op {
  public override X86OpKind Kind => X86OpKind.ShrRegCl;
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.shr {Dest.ToString().ToLower()}, cl";
}

public sealed class X86ImulRegRegOp(X86Register dest, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.ImulRegReg;
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.imul {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86CallDirectOp(string target) : X86Op {
  public override X86OpKind Kind => X86OpKind.CallDirect;
  public string Target { get; } = target;
  public override string Mnemonic => $"x64.call {Target}";
}

public sealed class X86CallIndirectOp(X86Register target) : X86Op {
  public override X86OpKind Kind => X86OpKind.CallIndirect;
  public X86Register Target { get; } = target;
  public override string Mnemonic => $"x64.call {Target.ToString().ToLower()}";
}

public sealed class X86MovMemRegOp(int displacement, X86Register src, int sizeInBytes) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovMemReg;
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public int SizeInBytes { get; } = sizeInBytes;
  public override string Mnemonic => $"x64.mov [rbp{(Displacement >= 0 ? "+" : "")}{Displacement}], {Src.ToString().ToLower()}";
}

public sealed class X86MovRegMemOp(X86Register dest, int displacement, int sizeInBytes) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovRegMem;
  public X86Register Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public int SizeInBytes { get; } = sizeInBytes;
  public override string Mnemonic => $"x64.mov {Dest.ToString().ToLower()}, [rbp{(Displacement >= 0 ? "+" : "")}{Displacement}]";
}

public sealed class X86MovMemRspRegOp(int offset, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovMemRspReg;
  public int Offset { get; } = offset;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov [rsp+{Offset}], {Src.ToString().ToLower()}";
}

public sealed class X86MovXmmRipRelOp(X86XmmRegister dest, string rdataLabel, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovXmmRipRel;
  public X86XmmRegister Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, [rip+{RdataLabel}]";
}

public sealed class X86MovMemXmmOp(int displacement, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovMemXmm;
  public int Displacement { get; } = displacement;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} [rbp{Displacement}], {Src.ToString().ToLower()}";
}

public sealed class X86MovXmmXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovXmmXmm;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86MovXmmMemOp(X86XmmRegister dest, int displacement, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovXmmMem;
  public X86XmmRegister Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, [rbp{Displacement}]";
}

public sealed class X86AddXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.AddXmm;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.add{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86SubXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.SubXmm;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.sub{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86MulXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.MulXmm;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mul{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86DivXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.DivXmm;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.div{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86CvttFloat2SiOp(X86Register dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.CvttFloat2Si;
  public X86Register Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.cvtt{(Precision == FloatPrecision.F64 ? "sd" : "ss")}2si {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86MovqXmmToGprOp(X86Register dest, X86XmmRegister src) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovqXmmToGpr;
  public X86Register Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x64.movq {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86CvtSi2FloatOp(X86XmmRegister dest, X86Register src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.CvtSi2Float;
  public X86XmmRegister Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.cvtsi2{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86CvtSd2SsOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public override X86OpKind Kind => X86OpKind.CvtSd2Ss;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x64.cvtsd2ss {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86CvtSs2SdOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public override X86OpKind Kind => X86OpKind.CvtSs2Sd;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x64.cvtss2sd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86AndMaskRipRelOp(X86XmmRegister dest, string rdataLabel, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.AndMaskRipRel;
  public X86XmmRegister Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.{(Precision == FloatPrecision.F64 ? "andpd" : "andps")} {Dest.ToString().ToLower()}, [rip+{RdataLabel}]";
}

public sealed class X86SqrtXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.SqrtXmm;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.sqrt{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86RoundXmmOp(X86XmmRegister dest, X86XmmRegister src, byte mode, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.RoundXmm;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public byte Mode { get; } = mode;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.round{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}, {Mode}";
}

public sealed class X86MinXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.MinXmm;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.min{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86MaxXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.MaxXmm;
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.max{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86CmpRegRegOp(X86Register lhs, X86Register rhs) : X86Op {
  public override X86OpKind Kind => X86OpKind.CmpRegReg;
  public X86Register Lhs { get; } = lhs;
  public X86Register Rhs { get; } = rhs;
  public override string Mnemonic => $"x64.cmp {Lhs.ToString().ToLower()}, {Rhs.ToString().ToLower()}";
}

public sealed class X86TestRegRegOp(X86Register lhs, X86Register rhs) : X86Op {
  public override X86OpKind Kind => X86OpKind.TestRegReg;
  public X86Register Lhs { get; } = lhs;
  public X86Register Rhs { get; } = rhs;
  public override string Mnemonic => $"x64.test {Lhs.ToString().ToLower()}, {Rhs.ToString().ToLower()}";
}

public sealed class X86UcomisXmmOp(X86XmmRegister src1, X86XmmRegister src2, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.UcomisXmm;
  public X86XmmRegister Src1 { get; } = src1;
  public X86XmmRegister Src2 { get; } = src2;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.ucomis{(Precision == FloatPrecision.F64 ? "d" : "s")} {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public sealed class X86SetccOp(string condition, X86Register dest) : X86Op {
  public override X86OpKind Kind => X86OpKind.Setcc;
  public string Condition { get; } = condition;
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.set{Condition} {Dest.ToString().ToLower()}";
}

public sealed class X86MovzxRegOp(X86Register dest) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovzxReg;
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.movzx {Dest.ToString().ToLower()}, {Dest.ToString().ToLower()}b";
}

/// Conditional move: moves src to dest if the zero flag is not set (condition != 0).
/// Must be preceded by a TEST instruction on the condition register.
public sealed class X86CmovneRegRegOp(X86Register dest, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.CmovneRegReg;
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.cmovne {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public sealed class X86JccOp(string condition, string target) : X86Op {
  public override X86OpKind Kind => X86OpKind.Jcc;
  public string Condition { get; } = condition;
  public string Target { get; } = target;
  public override string Mnemonic => $"x64.j{Condition} {Target}";
}

public sealed class X86JmpOp(string target) : X86Op {
  public override X86OpKind Kind => X86OpKind.Jmp;
  public string Target { get; } = target;
  public override string Mnemonic => $"x64.jmp {Target}";
}

public sealed class X86JumpTableOp(X86Register indexReg, int caseCount,
    string rdataLabel, string defaultTarget, string[] caseTargets) : X86Op {
  public override X86OpKind Kind => X86OpKind.JumpTable;
  public X86Register IndexReg { get; } = indexReg;
  public int CaseCount { get; } = caseCount;
  public string RdataLabel { get; } = rdataLabel;
  public string DefaultTarget { get; } = defaultTarget;
  public string[] CaseTargets { get; } = caseTargets;
  public override string Mnemonic => $"x64.jump_table {IndexReg.ToString().ToLower()}, {CaseCount} cases, default={DefaultTarget}";
}

public sealed class X86LabelDefOp(string name) : X86Op {
  public override X86OpKind Kind => X86OpKind.LabelDef;
  public string Name { get; } = name;
  public override string Mnemonic => $"x64.label {Name}";
}

public sealed class X86CqoOp : X86Op {
  public override X86OpKind Kind => X86OpKind.Cqo;
  public override string Mnemonic => "x64.cqo";
}

// CDQ: sign-extend EAX into EDX:EAX (32-bit, for 32-bit IDIV)
public sealed class X86CdqOp : X86Op {
  public override X86OpKind Kind => X86OpKind.Cdq;
  public override string Mnemonic => "x64.cdq";
}

public sealed class X86IdivRegOp(X86Register divisor) : X86Op {
  public override X86OpKind Kind => X86OpKind.IdivReg;
  public X86Register Divisor { get; } = divisor;
  public override string Mnemonic => $"x64.idiv {Divisor.ToString().ToLower()}";
}

// 32-bit signed division (no REX.W)
public sealed class X86IdivReg32Op(X86Register divisor) : X86Op {
  public override X86OpKind Kind => X86OpKind.IdivReg32;
  public X86Register Divisor { get; } = divisor;
  public override string Mnemonic => $"x64.idiv32 {Divisor.ToString().ToLower()}";
}

public sealed class X86DivRegOp(X86Register divisor) : X86Op {
  public override X86OpKind Kind => X86OpKind.DivReg;
  public X86Register Divisor { get; } = divisor;
  public override string Mnemonic => $"x64.div {Divisor.ToString().ToLower()}";
}

// 32-bit unsigned division (no REX.W)
public sealed class X86DivReg32Op(X86Register divisor) : X86Op {
  public override X86OpKind Kind => X86OpKind.DivReg32;
  public X86Register Divisor { get; } = divisor;
  public override string Mnemonic => $"x64.div32 {Divisor.ToString().ToLower()}";
}

// LEA dest, [rbp+disp] - load effective address of a stack variable
public sealed class X86LeaRegMemOp(X86Register dest, int displacement) : X86Op {
  public override X86OpKind Kind => X86OpKind.LeaRegMem;
  public X86Register Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.lea {Dest.ToString().ToLower()}, [rbp{(Displacement >= 0 ? "+" : "")}{Displacement}]";
}

// LEA dest, [rip+disp] - load effective address of an rdata label via RIP-relative
public sealed class X86LeaRipRelOp(X86Register dest, string rdataLabel) : X86Op {
  public override X86OpKind Kind => X86OpKind.LeaRipRel;
  public X86Register Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public override string Mnemonic => $"x64.lea_rdata {Dest.ToString().ToLower()}, [{RdataLabel}]";
}

// LEA dest, [rip+disp] - load effective address of a symdata label via RIP-relative
public sealed class X86LeaSymdataRelOp(X86Register dest, string symdataLabel) : X86Op {
  public override X86OpKind Kind => X86OpKind.LeaSymdataRel;
  public X86Register Dest { get; } = dest;
  public string SymdataLabel { get; } = symdataLabel;
  public override string Mnemonic => $"x64.lea_symdata {Dest.ToString().ToLower()}, [{SymdataLabel}]";
}

// LEA dest, [rip+disp] - load effective address of a ucddata label via RIP-relative
public sealed class X86LeaUcddataRelOp(X86Register dest, string ucddataLabel) : X86Op {
  public override X86OpKind Kind => X86OpKind.LeaUcddataRel;
  public X86Register Dest { get; } = dest;
  public string UcddataLabel { get; } = ucddataLabel;
  public override string Mnemonic => $"x64.lea_ucddata {Dest.ToString().ToLower()}, [{UcddataLabel}]";
}

// LEA dest, [rip+disp] - load effective address of a function
public sealed class X86LeaFuncAddrOp(X86Register dest, string functionName) : X86Op {
  public override X86OpKind Kind => X86OpKind.LeaFuncAddr;
  public X86Register Dest { get; } = dest;
  public string FunctionName { get; } = functionName;
  public override string Mnemonic => $"x64.lea_func {Dest.ToString().ToLower()}, [{FunctionName}]";
}

// LEA dest, [base + index] - fused add+mov (non-destructive three-register add)
public sealed class X86LeaRegRegRegOp(X86Register dest, X86Register baseReg, X86Register index) : X86Op {
  public override X86OpKind Kind => X86OpKind.LeaRegRegReg;
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public X86Register Index { get; } = index;
  public override string Mnemonic => $"x64.lea {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()} + {Index.ToString().ToLower()}]";
}

// MOV [baseReg+disp], srcReg - store through register-indirect addressing
public sealed class X86MovIndirectMemRegOp(X86Register baseReg, int displacement, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovIndirectMemReg;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}";
}

// MOV destReg, [baseReg+disp] - load through register-indirect addressing
public sealed class X86MovRegIndirectMemOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovRegIndirectMem;
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.mov {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOV[SD/SS] [baseReg+disp], xmm - store float through register-indirect addressing
public sealed class X86MovIndirectMemXmmOp(X86Register baseReg, int displacement, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovIndirectMemXmm;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}";
}

// MOV[SD/SS] xmm, [baseReg+disp] - load float through register-indirect addressing
public sealed class X86MovXmmIndirectMemOp(X86XmmRegister dest, X86Register baseReg, int displacement, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovXmmIndirectMem;
  public X86XmmRegister Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOVZX dest64, byte ptr [baseReg+disp] - load byte and zero-extend to 64-bit
public sealed class X86MovzxRegByteIndirectOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovzxRegByteIndirect;
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.movzx {Dest.ToString().ToLower()}, byte ptr [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOVSX dest64, byte ptr [baseReg+disp] - load 8-bit and sign-extend to 64-bit
public sealed class X86MovsxRegByteIndirectOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovsxRegByteIndirect;
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.movsx {Dest.ToString().ToLower()}, byte ptr [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOV byte ptr [baseReg+disp], src8 - store low byte of register to memory
public sealed class X86MovByteIndirectRegOp(X86Register baseReg, int displacement, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovByteIndirectReg;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov byte ptr [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}b";
}

// MOVZX dest32, word ptr [baseReg+disp] - load 16-bit and zero-extend to 32/64-bit
public sealed class X86MovzxRegWordIndirectOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovzxRegWordIndirect;
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.movzx {Dest.ToString().ToLower()}, word ptr [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOVSX dest64, word ptr [baseReg+disp] - load 16-bit and sign-extend to 64-bit
public sealed class X86MovsxRegWordIndirectOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovsxRegWordIndirect;
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.movsx {Dest.ToString().ToLower()}, word ptr [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOV dest32, dword ptr [baseReg+disp] - load 32-bit; in x86-64, writes to a 32-bit
// register implicitly zero-extend the upper 32 bits, so this also serves U32 loads.
public sealed class X86MovRegDwordIndirectOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovRegDwordIndirect;
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.mov {Dest.ToString().ToLower()}d, dword ptr [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOVSXD dest64, dword ptr [baseReg+disp] - load 32-bit and sign-extend to 64-bit
public sealed class X86MovsxdRegDwordIndirectOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovsxdRegDwordIndirect;
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.movsxd {Dest.ToString().ToLower()}, dword ptr [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOV dword ptr [baseReg+disp], src32 - store low 32 bits of register to memory
public sealed class X86MovDwordIndirectRegOp(X86Register baseReg, int displacement, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovDwordIndirectReg;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov dword ptr [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}d";
}

// MOV word ptr [baseReg+disp], src16 - store low 16 bits of register to memory
public sealed class X86MovWordIndirectRegOp(X86Register baseReg, int displacement, X86Register src) : X86Op {
  public override X86OpKind Kind => X86OpKind.MovWordIndirectReg;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov word ptr [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}w";
}

// ============================================================================
// Global variable operations (RIP-relative addressing)
// ============================================================================

// MOV dest, [rip + globalName] - load integer/bool global (size: 1, 2, 4, or 8 bytes)
public sealed class X86GlobalLoadOp(string globalName, X86Register dest, int size = 8) : X86Op {
  public override X86OpKind Kind => X86OpKind.GlobalLoad;
  public string GlobalName { get; } = globalName;
  public X86Register Dest { get; } = dest;
  public int Size { get; } = size;
  public override string Mnemonic => Size switch {
    1 => $"x64.movzx {Dest.ToString().ToLower()}, byte [rip+{GlobalName}]",
    2 => $"x64.movzx {Dest.ToString().ToLower()}, word [rip+{GlobalName}]",
    4 => $"x64.mov {Dest.ToString().ToLower()}, dword [rip+{GlobalName}]",
    _ => $"x64.mov {Dest.ToString().ToLower()}, qword [rip+{GlobalName}]"
  };
}

// MOV [rip + globalName], src - store integer/bool global (size: 1, 2, 4, or 8 bytes)
public sealed class X86GlobalStoreOp(string globalName, X86Register src, int size = 8) : X86Op {
  public override X86OpKind Kind => X86OpKind.GlobalStore;
  public string GlobalName { get; } = globalName;
  public X86Register Src { get; } = src;
  public int Size { get; } = size;
  public override string Mnemonic => Size switch {
    1 => $"x64.mov byte [rip+{GlobalName}], {Src.ToString().ToLower()}",
    2 => $"x64.mov word [rip+{GlobalName}], {Src.ToString().ToLower()}",
    4 => $"x64.mov dword [rip+{GlobalName}], {Src.ToString().ToLower()}",
    _ => $"x64.mov qword [rip+{GlobalName}], {Src.ToString().ToLower()}"
  };
}

// MOV[SD/SS] xmm, [rip + globalName] - load float global
public sealed class X86GlobalLoadXmmOp(string globalName, X86XmmRegister dest, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.GlobalLoadXmm;
  public string GlobalName { get; } = globalName;
  public X86XmmRegister Dest { get; } = dest;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, [rip+{GlobalName}]";
}

// MOV[SD/SS] [rip + globalName], xmm - store float global
public sealed class X86GlobalStoreXmmOp(string globalName, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public override X86OpKind Kind => X86OpKind.GlobalStoreXmm;
  public string GlobalName { get; } = globalName;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} [rip+{GlobalName}], {Src.ToString().ToLower()}";
}

// REP MOVSB - block copy RSI -> RDI, RCX bytes
public sealed class X86RepMovsbOp : X86Op {
  public override X86OpKind Kind => X86OpKind.RepMovsb;
  public override string Mnemonic => "x64.rep_movsb";
}

// STD - set direction flag (for backward REP MOVSB)
public sealed class X86StdOp : X86Op {
  public override X86OpKind Kind => X86OpKind.Std;
  public override string Mnemonic => "x64.std";
}

// CLD - clear direction flag (restore forward direction)
public sealed class X86CldOp : X86Op {
  public override X86OpKind Kind => X86OpKind.Cld;
  public override string Mnemonic => "x64.cld";
}

// REP STOSQ - fill RCX qwords at RDI with RAX
public sealed class X86RepStosqOp : X86Op {
  public override X86OpKind Kind => X86OpKind.RepStosq;
  public override string Mnemonic => "x64.rep_stosq";
}

// CALL [rip+IAT] for imported DLL function
public sealed class X86CallImportOp(string dllName, string functionName) : X86Op {
  public override X86OpKind Kind => X86OpKind.CallImport;
  public string DllName { get; } = dllName;
  public string FunctionName { get; } = functionName;
  public override string Mnemonic => $"x64.call_import {DllName}!{FunctionName}";
}
