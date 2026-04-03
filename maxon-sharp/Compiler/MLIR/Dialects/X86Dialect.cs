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

public enum FloatPrecision { F64, F32 }

public abstract class X86Op : IPrintableOp {
  public abstract string Mnemonic { get; }
  public IReadOnlyList<string> PrintableResults => [];
  public IReadOnlyList<string> PrintableOperands => [];
  public IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes => new Dictionary<string, MlirAttribute>();
}

public class X86PrologueOp(int stackSize) : X86Op {
  public int StackSize { get; } = stackSize;
  public override string Mnemonic => $"x64.prologue stack_size={StackSize}";
}

public class X86EpilogueOp : X86Op {
  public override string Mnemonic => "x64.epilogue";
}

public class X86PushRegOp(X86Register register) : X86Op {
  public X86Register Register { get; } = register;
  public override string Mnemonic => $"x64.push {Register.ToString().ToLower()}";
}

public class X86PopRegOp(X86Register register) : X86Op {
  public X86Register Register { get; } = register;
  public override string Mnemonic => $"x64.pop {Register.ToString().ToLower()}";
}

public class X86MovRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// MOVSXD r64, r/m32: sign-extend 32-bit to 64-bit
public class X86MovsxdOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.movsxd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86XchgRegRegOp(X86Register a, X86Register b) : X86Op {
  public X86Register A { get; } = a;
  public X86Register B { get; } = b;
  public override string Mnemonic => $"x64.xchg {A.ToString().ToLower()}, {B.ToString().ToLower()}";
}

public class X86MovRegImmOp(X86Register dest, long immediate) : X86Op {
  public X86Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"x64.mov {Dest.ToString().ToLower()}, {Immediate}";
}

public class X86RetOp : X86Op {
  public override string Mnemonic => "x64.ret";
}

public class X86SubRegImmOp(X86Register dest, long immediate) : X86Op {
  public X86Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"x64.sub {Dest.ToString().ToLower()}, {Immediate}";
}

public class X86AddRegImmOp(X86Register dest, long immediate) : X86Op {
  public X86Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"x64.add {Dest.ToString().ToLower()}, {Immediate}";
}

public class X86AddRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.add {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86SubRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.sub {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86AndRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.and {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86OrRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.or {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86XorRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.xor {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86ShlRegClOp(X86Register dest) : X86Op {
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.shl {Dest.ToString().ToLower()}, cl";
}

public class X86SarRegClOp(X86Register dest) : X86Op {
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.sar {Dest.ToString().ToLower()}, cl";
}

public class X86ShrRegClOp(X86Register dest) : X86Op {
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.shr {Dest.ToString().ToLower()}, cl";
}

public class X86ImulRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.imul {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CallDirectOp(string target) : X86Op {
  public string Target { get; } = target;
  public override string Mnemonic => $"x64.call {Target}";
}

public class X86CallIndirectOp(X86Register target) : X86Op {
  public X86Register Target { get; } = target;
  public override string Mnemonic => $"x64.call {Target.ToString().ToLower()}";
}

public class X86MovMemRegOp(int displacement, X86Register src, int sizeInBytes) : X86Op {
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public int SizeInBytes { get; } = sizeInBytes;
  public override string Mnemonic => $"x64.mov [rbp{(Displacement >= 0 ? "+" : "")}{Displacement}], {Src.ToString().ToLower()}";
}

public class X86MovRegMemOp(X86Register dest, int displacement, int sizeInBytes) : X86Op {
  public X86Register Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public int SizeInBytes { get; } = sizeInBytes;
  public override string Mnemonic => $"x64.mov {Dest.ToString().ToLower()}, [rbp{(Displacement >= 0 ? "+" : "")}{Displacement}]";
}

public class X86MovMemRspRegOp(int offset, X86Register src) : X86Op {
  public int Offset { get; } = offset;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov [rsp+{Offset}], {Src.ToString().ToLower()}";
}

public class X86MovXmmRipRelOp(X86XmmRegister dest, string rdataLabel, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, [rip+{RdataLabel}]";
}

public class X86MovMemXmmOp(int displacement, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public int Displacement { get; } = displacement;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} [rbp{Displacement}], {Src.ToString().ToLower()}";
}

public class X86MovXmmXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86MovXmmMemOp(X86XmmRegister dest, int displacement, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, [rbp{Displacement}]";
}

public class X86AddXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.add{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86SubXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.sub{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86MulXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mul{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86DivXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.div{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CvttFloat2SiOp(X86Register dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.cvtt{(Precision == FloatPrecision.F64 ? "sd" : "ss")}2si {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86MovqXmmToGprOp(X86Register dest, X86XmmRegister src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x64.movq {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CvtSi2FloatOp(X86XmmRegister dest, X86Register src, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.cvtsi2{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CvtSd2SsOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x64.cvtsd2ss {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CvtSs2SdOp(X86XmmRegister dest, X86XmmRegister src) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public override string Mnemonic => $"x64.cvtss2sd {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86AndMaskRipRelOp(X86XmmRegister dest, string rdataLabel, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.{(Precision == FloatPrecision.F64 ? "andpd" : "andps")} {Dest.ToString().ToLower()}, [rip+{RdataLabel}]";
}

public class X86SqrtXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.sqrt{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86RoundXmmOp(X86XmmRegister dest, X86XmmRegister src, byte mode, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public byte Mode { get; } = mode;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.round{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}, {Mode}";
}

public class X86MinXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.min{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86MaxXmmOp(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.max{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CmpRegRegOp(X86Register lhs, X86Register rhs) : X86Op {
  public X86Register Lhs { get; } = lhs;
  public X86Register Rhs { get; } = rhs;
  public override string Mnemonic => $"x64.cmp {Lhs.ToString().ToLower()}, {Rhs.ToString().ToLower()}";
}

public class X86TestRegRegOp(X86Register lhs, X86Register rhs) : X86Op {
  public X86Register Lhs { get; } = lhs;
  public X86Register Rhs { get; } = rhs;
  public override string Mnemonic => $"x64.test {Lhs.ToString().ToLower()}, {Rhs.ToString().ToLower()}";
}

public class X86UcomisXmmOp(X86XmmRegister src1, X86XmmRegister src2, FloatPrecision precision) : X86Op {
  public X86XmmRegister Src1 { get; } = src1;
  public X86XmmRegister Src2 { get; } = src2;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.ucomis{(Precision == FloatPrecision.F64 ? "d" : "s")} {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class X86SetccOp(string condition, X86Register dest) : X86Op {
  public string Condition { get; } = condition;
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.set{Condition} {Dest.ToString().ToLower()}";
}

public class X86MovzxRegOp(X86Register dest) : X86Op {
  public X86Register Dest { get; } = dest;
  public override string Mnemonic => $"x64.movzx {Dest.ToString().ToLower()}, {Dest.ToString().ToLower()}b";
}

/// Conditional move: moves src to dest if the zero flag is not set (condition != 0).
/// Must be preceded by a TEST instruction on the condition register.
public class X86CmovneRegRegOp(X86Register dest, X86Register src) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.cmovne {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86JccOp(string condition, string target) : X86Op {
  public string Condition { get; } = condition;
  public string Target { get; } = target;
  public override string Mnemonic => $"x64.j{Condition} {Target}";
}

public class X86JmpOp(string target) : X86Op {
  public string Target { get; } = target;
  public override string Mnemonic => $"x64.jmp {Target}";
}

public class X86JumpTableOp(X86Register indexReg, int caseCount,
    string rdataLabel, string defaultTarget, string[] caseTargets) : X86Op {
  public X86Register IndexReg { get; } = indexReg;
  public int CaseCount { get; } = caseCount;
  public string RdataLabel { get; } = rdataLabel;
  public string DefaultTarget { get; } = defaultTarget;
  public string[] CaseTargets { get; } = caseTargets;
  public override string Mnemonic => $"x64.jump_table {IndexReg.ToString().ToLower()}, {CaseCount} cases, default={DefaultTarget}";
}

public class X86LabelDefOp(string name) : X86Op {
  public string Name { get; } = name;
  public override string Mnemonic => $"x64.label {Name}";
}

public class X86CqoOp : X86Op {
  public override string Mnemonic => "x64.cqo";
}

// CDQ: sign-extend EAX into EDX:EAX (32-bit, for 32-bit IDIV)
public class X86CdqOp : X86Op {
  public override string Mnemonic => "x64.cdq";
}

public class X86IdivRegOp(X86Register divisor) : X86Op {
  public X86Register Divisor { get; } = divisor;
  public override string Mnemonic => $"x64.idiv {Divisor.ToString().ToLower()}";
}

// 32-bit signed division (no REX.W)
public class X86IdivReg32Op(X86Register divisor) : X86Op {
  public X86Register Divisor { get; } = divisor;
  public override string Mnemonic => $"x64.idiv32 {Divisor.ToString().ToLower()}";
}

public class X86DivRegOp(X86Register divisor) : X86Op {
  public X86Register Divisor { get; } = divisor;
  public override string Mnemonic => $"x64.div {Divisor.ToString().ToLower()}";
}

// 32-bit unsigned division (no REX.W)
public class X86DivReg32Op(X86Register divisor) : X86Op {
  public X86Register Divisor { get; } = divisor;
  public override string Mnemonic => $"x64.div32 {Divisor.ToString().ToLower()}";
}

// LEA dest, [rbp+disp] - load effective address of a stack variable
public class X86LeaRegMemOp(X86Register dest, int displacement) : X86Op {
  public X86Register Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.lea {Dest.ToString().ToLower()}, [rbp{(Displacement >= 0 ? "+" : "")}{Displacement}]";
}

// LEA dest, [rip+disp] - load effective address of an rdata label via RIP-relative
public class X86LeaRipRelOp(X86Register dest, string rdataLabel) : X86Op {
  public X86Register Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public override string Mnemonic => $"x64.lea_rdata {Dest.ToString().ToLower()}, [{RdataLabel}]";
}

// LEA dest, [rip+disp] - load effective address of a symdata label via RIP-relative
public class X86LeaSymdataRelOp(X86Register dest, string symdataLabel) : X86Op {
  public X86Register Dest { get; } = dest;
  public string SymdataLabel { get; } = symdataLabel;
  public override string Mnemonic => $"x64.lea_symdata {Dest.ToString().ToLower()}, [{SymdataLabel}]";
}

// LEA dest, [rip+disp] - load effective address of a ucddata label via RIP-relative
public class X86LeaUcddataRelOp(X86Register dest, string ucddataLabel) : X86Op {
  public X86Register Dest { get; } = dest;
  public string UcddataLabel { get; } = ucddataLabel;
  public override string Mnemonic => $"x64.lea_ucddata {Dest.ToString().ToLower()}, [{UcddataLabel}]";
}

// LEA dest, [rip+disp] - load effective address of a function
public class X86LeaFuncAddrOp(X86Register dest, string functionName) : X86Op {
  public X86Register Dest { get; } = dest;
  public string FunctionName { get; } = functionName;
  public override string Mnemonic => $"x64.lea_func {Dest.ToString().ToLower()}, [{FunctionName}]";
}

// LEA dest, [base + index] - fused add+mov (non-destructive three-register add)
public class X86LeaRegRegRegOp(X86Register dest, X86Register baseReg, X86Register index) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public X86Register Index { get; } = index;
  public override string Mnemonic => $"x64.lea {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()} + {Index.ToString().ToLower()}]";
}

// MOV [baseReg+disp], srcReg - store through register-indirect addressing
public class X86MovIndirectMemRegOp(X86Register baseReg, int displacement, X86Register src) : X86Op {
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}";
}

// MOV destReg, [baseReg+disp] - load through register-indirect addressing
public class X86MovRegIndirectMemOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.mov {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOV[SD/SS] [baseReg+disp], xmm - store float through register-indirect addressing
public class X86MovIndirectMemXmmOp(X86Register baseReg, int displacement, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}";
}

// MOV[SD/SS] xmm, [baseReg+disp] - load float through register-indirect addressing
public class X86MovXmmIndirectMemOp(X86XmmRegister dest, X86Register baseReg, int displacement, FloatPrecision precision) : X86Op {
  public X86XmmRegister Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOVZX dest64, byte ptr [baseReg+disp] - load byte and zero-extend to 64-bit
public class X86MovzxRegByteIndirectOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.movzx {Dest.ToString().ToLower()}, byte ptr [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOV byte ptr [baseReg+disp], src8 - store low byte of register to memory
public class X86MovByteIndirectRegOp(X86Register baseReg, int displacement, X86Register src) : X86Op {
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov byte ptr [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}b";
}

// MOVZX dest32, word ptr [baseReg+disp] - load 16-bit and zero-extend to 32/64-bit
public class X86MovzxRegWordIndirectOp(X86Register dest, X86Register baseReg, int displacement) : X86Op {
  public X86Register Dest { get; } = dest;
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"x64.movzx {Dest.ToString().ToLower()}, word ptr [{BaseReg.ToString().ToLower()}+{Displacement}]";
}

// MOV word ptr [baseReg+disp], src16 - store low 16 bits of register to memory
public class X86MovWordIndirectRegOp(X86Register baseReg, int displacement, X86Register src) : X86Op {
  public X86Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public X86Register Src { get; } = src;
  public override string Mnemonic => $"x64.mov word ptr [{BaseReg.ToString().ToLower()}+{Displacement}], {Src.ToString().ToLower()}w";
}

// ============================================================================
// Global variable operations (RIP-relative addressing)
// ============================================================================

// MOV dest, [rip + globalName] - load integer/bool global (size: 1, 2, 4, or 8 bytes)
public class X86GlobalLoadOp(string globalName, X86Register dest, int size = 8) : X86Op {
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
public class X86GlobalStoreOp(string globalName, X86Register src, int size = 8) : X86Op {
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
public class X86GlobalLoadXmmOp(string globalName, X86XmmRegister dest, FloatPrecision precision) : X86Op {
  public string GlobalName { get; } = globalName;
  public X86XmmRegister Dest { get; } = dest;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} {Dest.ToString().ToLower()}, [rip+{GlobalName}]";
}

// MOV[SD/SS] [rip + globalName], xmm - store float global
public class X86GlobalStoreXmmOp(string globalName, X86XmmRegister src, FloatPrecision precision) : X86Op {
  public string GlobalName { get; } = globalName;
  public X86XmmRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"x64.mov{(Precision == FloatPrecision.F64 ? "sd" : "ss")} [rip+{GlobalName}], {Src.ToString().ToLower()}";
}

// REP MOVSB - block copy RSI -> RDI, RCX bytes
public class X86RepMovsbOp : X86Op {
  public override string Mnemonic => "x64.rep_movsb";
}

// STD - set direction flag (for backward REP MOVSB)
public class X86StdOp : X86Op {
  public override string Mnemonic => "x64.std";
}

// CLD - clear direction flag (restore forward direction)
public class X86CldOp : X86Op {
  public override string Mnemonic => "x64.cld";
}

// REP STOSQ - fill RCX qwords at RDI with RAX
public class X86RepStosqOp : X86Op {
  public override string Mnemonic => "x64.rep_stosq";
}

// CALL [rip+IAT] for imported DLL function
public class X86CallImportOp(string dllName, string functionName) : X86Op {
  public string DllName { get; } = dllName;
  public string FunctionName { get; } = functionName;
  public override string Mnemonic => $"x64.call_import {DllName}!{FunctionName}";
}
