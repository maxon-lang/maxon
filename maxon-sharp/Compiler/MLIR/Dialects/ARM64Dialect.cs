using MaxonSharp.Compiler.Ir.Core;

namespace MaxonSharp.Compiler.Ir.Dialects;

public enum ARM64Register {
  X0, X1, X2, X3, X4, X5, X6, X7,
  X8, X9, X10, X11, X12, X13, X14, X15,
  X16, X17, X18, X19, X20, X21, X22, X23,
  X24, X25, X26, X27, X28, X29, X30, Sp, Xzr,
  // 32-bit aliases
  W0, W1, W2, W3, W4, W5, W6, W7,
  W8, W9, W10, W11, W12, W13, W14, W15,
  W16, W17, W18, W19, W20, W21, W22, W23,
  W24, W25, W26, W27, W28, W29, W30, Wzr,
}

public enum ARM64FloatRegister {
  D0, D1, D2, D3, D4, D5, D6, D7,
  D8, D9, D10, D11, D12, D13, D14, D15,
  D16, D17, D18, D19, D20, D21, D22, D23,
  D24, D25, D26, D27, D28, D29, D30, D31,
  // 32-bit float aliases
  S0, S1, S2, S3, S4, S5, S6, S7,
  S8, S9, S10, S11, S12, S13, S14, S15,
  S16, S17, S18, S19, S20, S21, S22, S23,
  S24, S25, S26, S27, S28, S29, S30, S31,
}

public enum ARM64ConditionCode {
  Eq,  // Equal (Z=1)
  Ne,  // Not equal (Z=0)
  Lt,  // Signed less than (N!=V)
  Le,  // Signed less than or equal (Z=1 || N!=V)
  Gt,  // Signed greater than (Z=0 && N==V)
  Ge,  // Signed greater than or equal (N==V)
  Lo,  // Unsigned lower (C=0)
  Ls,  // Unsigned lower or same (C=0 || Z=1)
  Hi,  // Unsigned higher (C=1 && Z=0)
  Hs,  // Unsigned higher or same (C=1)
}

// --- Base class ---

public abstract class ARM64Op : IPrintableOp {
  public abstract string Mnemonic { get; }
  public IReadOnlyList<string> PrintableResults => [];
  public IReadOnlyList<string> PrintableOperands => [];
  public IReadOnlyDictionary<string, IrAttribute> PrintableAttributes => new Dictionary<string, IrAttribute>();
}

// --- Prologue / Epilogue ---

public class ARM64PrologueOp(int stackSize) : ARM64Op {
  public int StackSize { get; } = stackSize;
  public override string Mnemonic => $"arm64.prologue stack_size={StackSize}";
}

public class ARM64EpilogueOp(int stackSize = 0) : ARM64Op {
  public int StackSize { get; set; } = stackSize;
  public override string Mnemonic => $"arm64.epilogue stack_size={StackSize}";
}

// --- Register moves ---

public class ARM64MovRegRegOp(ARM64Register dest, ARM64Register src) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src { get; } = src;
  public override string Mnemonic => $"arm64.mov {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class ARM64MovRegImmOp(ARM64Register dest, long immediate) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"arm64.mov {Dest.ToString().ToLower()}, #{Immediate}";
}

public class ARM64MovRegImm32Op(ARM64Register dest, long immediate) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public long Immediate { get; } = immediate;
  private static string WReg(ARM64Register r) => r switch {
    >= ARM64Register.X0 and <= ARM64Register.X28 => $"w{r - ARM64Register.X0}",
    ARM64Register.X29 => "w29",
    ARM64Register.X30 => "w30",
    _ => r.ToString().ToLower()
  };
  public override string Mnemonic => $"arm64.mov {WReg(Dest)}, #{Immediate}";
}

// --- Stack load/store (frame-relative) ---

// STR Xt, [X29, #offset] — store 64-bit register to stack
public class ARM64StoreToStackOp(int displacement, ARM64Register src, int sizeInBytes) : ARM64Op {
  public int Displacement { get; } = displacement;
  public ARM64Register Src { get; } = src;
  public int SizeInBytes { get; } = sizeInBytes;
  public override string Mnemonic => $"arm64.str {Src.ToString().ToLower()}, [x29, #{Displacement}]";
}

// LDR Xt, [X29, #offset] — load 64-bit register from stack
public class ARM64LoadFromStackOp(ARM64Register dest, int displacement, int sizeInBytes) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public int SizeInBytes { get; } = sizeInBytes;
  public override string Mnemonic => $"arm64.ldr {Dest.ToString().ToLower()}, [x29, #{Displacement}]";
}

// --- Indirect load/store (through register) ---

// STR Xt, [Xn, #offset]
public class ARM64StoreIndirectOp(ARM64Register baseReg, int displacement, ARM64Register src) : ARM64Op {
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public ARM64Register Src { get; } = src;
  public override string Mnemonic => $"arm64.str {Src.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// LDR Xt, [Xn, #offset]
public class ARM64LoadIndirectOp(ARM64Register dest, ARM64Register baseReg, int displacement) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"arm64.ldr {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// STRB Wt, [Xn, #offset] — store byte
public class ARM64StoreByteIndirectOp(ARM64Register baseReg, int displacement, ARM64Register src) : ARM64Op {
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public ARM64Register Src { get; } = src;
  public override string Mnemonic => $"arm64.strb {Src.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// LDRB Wt, [Xn, #offset] — load byte (zero-extend)
public class ARM64LoadByteIndirectOp(ARM64Register dest, ARM64Register baseReg, int displacement) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"arm64.ldrb {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// STRH Wt, [Xn, #offset] — store halfword (16-bit)
public class ARM64StoreHalfIndirectOp(ARM64Register baseReg, int displacement, ARM64Register src) : ARM64Op {
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public ARM64Register Src { get; } = src;
  public override string Mnemonic => $"arm64.strh {Src.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// LDRH Wt, [Xn, #offset] — load halfword (16-bit, zero-extend)
public class ARM64LoadHalfIndirectOp(ARM64Register dest, ARM64Register baseReg, int displacement) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"arm64.ldrh {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// STR Wt, [Xn, #offset] — store 32-bit register
public class ARM64Store32IndirectOp(ARM64Register baseReg, int displacement, ARM64Register src) : ARM64Op {
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public ARM64Register Src { get; } = src;
  public override string Mnemonic => $"arm64.str32 {Src.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// LDR Wt, [Xn, #offset] — load 32-bit register (zero-extend)
public class ARM64Load32IndirectOp(ARM64Register dest, ARM64Register baseReg, int displacement) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"arm64.ldr32 {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// --- Arithmetic (integer, 64-bit) ---

public class ARM64AddRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.add {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64SubRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.sub {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64MulRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.mul {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64SdivRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.sdiv {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64UdivRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.udiv {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

// MSUB Xd, Xn, Xm, Xa — Xd = Xa - Xn * Xm (for remainder: Xd = dividend - quotient * divisor)
public class ARM64MsubRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2, ARM64Register accumulator) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public ARM64Register Accumulator { get; } = accumulator;
  public override string Mnemonic => $"arm64.msub {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}, {Accumulator.ToString().ToLower()}";
}

// NEG Xd, Xn (alias for SUB Xd, XZR, Xn)
public class ARM64NegRegOp(ARM64Register dest, ARM64Register src) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src { get; } = src;
  public override string Mnemonic => $"arm64.neg {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// --- Arithmetic (immediate) ---

public class ARM64AddRegImmOp(ARM64Register dest, ARM64Register src, long immediate) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src { get; } = src;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"arm64.add {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}, #{Immediate}";
}

public class ARM64SubRegImmOp(ARM64Register dest, ARM64Register src, long immediate) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src { get; } = src;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"arm64.sub {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}, #{Immediate}";
}

// --- Logical ---

public class ARM64AndRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.and {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64OrrRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.orr {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64EorRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.eor {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

// MVN Xd, Xn (bitwise NOT, alias for ORN Xd, XZR, Xn)
public class ARM64MvnRegOp(ARM64Register dest, ARM64Register src) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src { get; } = src;
  public override string Mnemonic => $"arm64.mvn {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// --- Shifts ---

public class ARM64LslRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.lslv {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64AsrRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.asrv {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64LsrRegRegOp(ARM64Register dest, ARM64Register src1, ARM64Register src2) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public override string Mnemonic => $"arm64.lsrv {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

// --- Compare ---

public class ARM64CmpRegRegOp(ARM64Register lhs, ARM64Register rhs) : ARM64Op {
  public ARM64Register Lhs { get; } = lhs;
  public ARM64Register Rhs { get; } = rhs;
  public override string Mnemonic => $"arm64.cmp {Lhs.ToString().ToLower()}, {Rhs.ToString().ToLower()}";
}

public class ARM64CmpRegImmOp(ARM64Register lhs, long immediate) : ARM64Op {
  public ARM64Register Lhs { get; } = lhs;
  public long Immediate { get; } = immediate;
  public override string Mnemonic => $"arm64.cmp {Lhs.ToString().ToLower()}, #{Immediate}";
}

// TST Xn, Xm (alias for ANDS XZR, Xn, Xm)
public class ARM64TestRegRegOp(ARM64Register lhs, ARM64Register rhs) : ARM64Op {
  public ARM64Register Lhs { get; } = lhs;
  public ARM64Register Rhs { get; } = rhs;
  public override string Mnemonic => $"arm64.tst {Lhs.ToString().ToLower()}, {Rhs.ToString().ToLower()}";
}

// CSET Xd, cond — set register to 1 if condition holds, else 0
public class ARM64CsetOp(ARM64Register dest, ARM64ConditionCode condition) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64ConditionCode Condition { get; } = condition;
  public override string Mnemonic => $"arm64.cset {Dest.ToString().ToLower()}, {Condition.ToString().ToLower()}";
}

// CSEL Xd, Xn, Xm, cond — conditional select
public class ARM64CselOp(ARM64Register dest, ARM64Register src1, ARM64Register src2, ARM64ConditionCode condition) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src1 { get; } = src1;
  public ARM64Register Src2 { get; } = src2;
  public ARM64ConditionCode Condition { get; } = condition;
  public override string Mnemonic => $"arm64.csel {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}, {Condition.ToString().ToLower()}";
}

// --- SXTW: sign-extend 32-bit to 64-bit ---
public class ARM64SxtwOp(ARM64Register dest, ARM64Register src) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register Src { get; } = src;
  public override string Mnemonic => $"arm64.sxtw {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// --- Control flow ---

public class ARM64BranchOp(string target) : ARM64Op {
  public string Target { get; } = target;
  public override string Mnemonic => $"arm64.b {Target}";
}

public class ARM64BranchCondOp(ARM64ConditionCode condition, string target) : ARM64Op {
  public ARM64ConditionCode Condition { get; } = condition;
  public string Target { get; } = target;
  public override string Mnemonic => $"arm64.b.{Condition.ToString().ToLower()} {Target}";
}

public class ARM64BranchLinkOp(string target) : ARM64Op {
  public string Target { get; } = target;
  public override string Mnemonic => $"arm64.bl {Target}";
}

// BLR Xn — branch with link to register (indirect call)
public class ARM64BranchLinkRegOp(ARM64Register target) : ARM64Op {
  public ARM64Register Target { get; } = target;
  public override string Mnemonic => $"arm64.blr {Target.ToString().ToLower()}";
}

public class ARM64RetOp : ARM64Op {
  public override string Mnemonic => "arm64.ret";
}

// --- Label ---

public class ARM64LabelDefOp(string name) : ARM64Op {
  public string Name { get; } = name;
  public override string Mnemonic => $"arm64.label {Name}";
}

// --- Floating point ---

// FMOV Dd, Xn — move general register to float register
public class ARM64FmovToFloatOp(ARM64FloatRegister dest, ARM64Register src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64Register Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fmov {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// FMOV Xn, Dd — move float register to general register
public class ARM64FmovToGprOp(ARM64Register dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fmov {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// FMOV Dd, Ds — move between float registers
public class ARM64FmovRegRegOp(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fmov {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// Float load from stack: LDR Dt, [X29, #offset]
public class ARM64FloatLoadFromStackOp(ARM64FloatRegister dest, int displacement, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fldr {Dest.ToString().ToLower()}, [x29, #{Displacement}]";
}

// Float store to stack: STR Dt, [X29, #offset]
public class ARM64FloatStoreToStackOp(int displacement, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public int Displacement { get; } = displacement;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fstr {Src.ToString().ToLower()}, [x29, #{Displacement}]";
}

// Float load indirect: LDR Dt, [Xn, #offset]
public class ARM64FloatLoadIndirectOp(ARM64FloatRegister dest, ARM64Register baseReg, int displacement, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fldr {Dest.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// Float store indirect: STR Dt, [Xn, #offset]
public class ARM64FloatStoreIndirectOp(ARM64Register baseReg, int displacement, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64Register BaseReg { get; } = baseReg;
  public int Displacement { get; } = displacement;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fstr {Src.ToString().ToLower()}, [{BaseReg.ToString().ToLower()}, #{Displacement}]";
}

// Float load from rdata (PC-relative): LDR Dt, [PC, label]
public class ARM64FloatLoadRdataOp(ARM64FloatRegister dest, string rdataLabel, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fldr {Dest.ToString().ToLower()}, [{RdataLabel}]";
}

// Float arithmetic
public class ARM64FaddOp(ARM64FloatRegister dest, ARM64FloatRegister src1, ARM64FloatRegister src2, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src1 { get; } = src1;
  public ARM64FloatRegister Src2 { get; } = src2;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fadd {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64FsubOp(ARM64FloatRegister dest, ARM64FloatRegister src1, ARM64FloatRegister src2, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src1 { get; } = src1;
  public ARM64FloatRegister Src2 { get; } = src2;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fsub {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64FmulOp(ARM64FloatRegister dest, ARM64FloatRegister src1, ARM64FloatRegister src2, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src1 { get; } = src1;
  public ARM64FloatRegister Src2 { get; } = src2;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fmul {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64FdivOp(ARM64FloatRegister dest, ARM64FloatRegister src1, ARM64FloatRegister src2, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src1 { get; } = src1;
  public ARM64FloatRegister Src2 { get; } = src2;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fdiv {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64FsqrtOp(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fsqrt {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class ARM64FnegOp(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fneg {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class ARM64FabsOp(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fabs {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class ARM64FminOp(ARM64FloatRegister dest, ARM64FloatRegister src1, ARM64FloatRegister src2, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src1 { get; } = src1;
  public ARM64FloatRegister Src2 { get; } = src2;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fmin {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class ARM64FmaxOp(ARM64FloatRegister dest, ARM64FloatRegister src1, ARM64FloatRegister src2, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src1 { get; } = src1;
  public ARM64FloatRegister Src2 { get; } = src2;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fmax {Dest.ToString().ToLower()}, {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

// FRINTP/FRINTM/FRINTZ/FRINTX (round to integer in float)
public class ARM64FrintzOp(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.frintz {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class ARM64FrintpOp(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.frintp {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class ARM64FrintmOp(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.frintm {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class ARM64FrintnOp(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.frintn {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// Float compare: FCMP Dn, Dm
public class ARM64FcmpOp(ARM64FloatRegister lhs, ARM64FloatRegister rhs, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Lhs { get; } = lhs;
  public ARM64FloatRegister Rhs { get; } = rhs;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fcmp {Lhs.ToString().ToLower()}, {Rhs.ToString().ToLower()}";
}

// Float conversions
// SCVTF Dd, Xn — signed integer to float
public class ARM64ScvtfOp(ARM64FloatRegister dest, ARM64Register src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64Register Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.scvtf {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// FCVTZS Xn, Dd — float to signed integer (truncate toward zero)
public class ARM64FcvtzsOp(ARM64Register dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fcvtzs {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// UCVTF Dd, Xn — unsigned integer to float
public class ARM64UcvtfOp(ARM64FloatRegister dest, ARM64Register src, FloatPrecision precision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64Register Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.ucvtf {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// FCVTZU Xn, Dd — float to unsigned integer (truncate toward zero)
public class ARM64FcvtzuOp(ARM64Register dest, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.fcvtzu {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// FCVT Sd, Dn or FCVT Dd, Sn — convert between float precisions
public class ARM64FcvtOp(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision destPrecision) : ARM64Op {
  public ARM64FloatRegister Dest { get; } = dest;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision DestPrecision { get; } = destPrecision;
  public override string Mnemonic => $"arm64.fcvt {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

// --- Address computation ---

// LEA equivalent: ADD Xd, X29, #offset (address of stack variable)
public class ARM64LeaStackOp(ARM64Register dest, int displacement) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public int Displacement { get; } = displacement;
  public override string Mnemonic => $"arm64.add {Dest.ToString().ToLower()}, x29, #{Displacement}";
}

// ADRP + ADD for PC-relative data access (rdata, globals, symdata, ucddata, func addr)
public class ARM64AdrpAddRdataOp(ARM64Register dest, string rdataLabel) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public string RdataLabel { get; } = rdataLabel;
  public override string Mnemonic => $"arm64.adrp_add_rdata {Dest.ToString().ToLower()}, {RdataLabel}";
}

public class ARM64AdrpAddGlobalOp(ARM64Register dest, string globalName) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public string GlobalName { get; } = globalName;
  public override string Mnemonic => $"arm64.adrp_add_global {Dest.ToString().ToLower()}, {GlobalName}";
}

public class ARM64AdrpAddSymdataOp(ARM64Register dest, string symdataLabel) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public string SymdataLabel { get; } = symdataLabel;
  public override string Mnemonic => $"arm64.adrp_add_symdata {Dest.ToString().ToLower()}, {SymdataLabel}";
}

public class ARM64AdrpAddUcddataOp(ARM64Register dest, string ucddataLabel) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public string UcddataLabel { get; } = ucddataLabel;
  public override string Mnemonic => $"arm64.adrp_add_ucddata {Dest.ToString().ToLower()}, {UcddataLabel}";
}

public class ARM64AdrpAddFuncOp(ARM64Register dest, string functionName) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public string FunctionName { get; } = functionName;
  public override string Mnemonic => $"arm64.adrp_add_func {Dest.ToString().ToLower()}, {FunctionName}";
}

// LEA [base + index] — non-destructive add (same as ADD Xd, Xn, Xm)
public class ARM64LeaRegRegOp(ARM64Register dest, ARM64Register baseReg, ARM64Register index) : ARM64Op {
  public ARM64Register Dest { get; } = dest;
  public ARM64Register BaseReg { get; } = baseReg;
  public ARM64Register Index { get; } = index;
  public override string Mnemonic => $"arm64.add {Dest.ToString().ToLower()}, {BaseReg.ToString().ToLower()}, {Index.ToString().ToLower()}";
}

// --- Global variable load/store (PC-relative via ADRP+LDR/STR) ---

public class ARM64GlobalLoadOp(string globalName, ARM64Register dest, int size = 8) : ARM64Op {
  public string GlobalName { get; } = globalName;
  public ARM64Register Dest { get; } = dest;
  public int Size { get; } = size;
  public override string Mnemonic => $"arm64.global_load {Dest.ToString().ToLower()}, [{GlobalName}]";
}

public class ARM64GlobalStoreOp(string globalName, ARM64Register src, int size = 8) : ARM64Op {
  public string GlobalName { get; } = globalName;
  public ARM64Register Src { get; } = src;
  public int Size { get; } = size;
  public override string Mnemonic => $"arm64.global_store [{GlobalName}], {Src.ToString().ToLower()}";
}

public class ARM64GlobalLoadFloatOp(string globalName, ARM64FloatRegister dest, FloatPrecision precision) : ARM64Op {
  public string GlobalName { get; } = globalName;
  public ARM64FloatRegister Dest { get; } = dest;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.global_fload {Dest.ToString().ToLower()}, [{GlobalName}]";
}

public class ARM64GlobalStoreFloatOp(string globalName, ARM64FloatRegister src, FloatPrecision precision) : ARM64Op {
  public string GlobalName { get; } = globalName;
  public ARM64FloatRegister Src { get; } = src;
  public FloatPrecision Precision { get; } = precision;
  public override string Mnemonic => $"arm64.global_fstore [{GlobalName}], {Src.ToString().ToLower()}";
}

// --- Memory copy (loop-based) ---
// Uses X0=dst, X1=src, X2=count (caller provides), clobbers X3
public class ARM64MemcpyOp : ARM64Op {
  public override string Mnemonic => "arm64.memcpy";
}

// --- Backward memcpy for overlapping shift-right ---
// Uses X0=dst, X1=src, X2=count (in bytes), copies from end to start
public class ARM64MemcpyReverseOp : ARM64Op {
  public override string Mnemonic => "arm64.memcpy_reverse";
}

// --- Bulk zero (loop-based) ---
// Uses X0=dst, X1=count (in qwords), fills with zero
public class ARM64BulkZeroOp : ARM64Op {
  public override string Mnemonic => "arm64.bulk_zero";
}

// --- Stack operations for passing arguments beyond x0-x7 ---

// STR Xt, [SP, #offset] — store to stack for outgoing call args
public class ARM64StoreToSpOp(int offset, ARM64Register src) : ARM64Op {
  public int Offset { get; } = offset;
  public ARM64Register Src { get; } = src;
  public override string Mnemonic => $"arm64.str {Src.ToString().ToLower()}, [sp, #{Offset}]";
}

// --- Jump table dispatch ---

public class ARM64JumpTableOp(ARM64Register indexReg, int caseCount,
    string rdataLabel, string defaultTarget, string[] caseTargets) : ARM64Op {
  public ARM64Register IndexReg { get; } = indexReg;
  public int CaseCount { get; } = caseCount;
  public string RdataLabel { get; } = rdataLabel;
  public string DefaultTarget { get; } = defaultTarget;
  public string[] CaseTargets { get; } = caseTargets;
  public override string Mnemonic => $"arm64.jump_table {IndexReg.ToString().ToLower()}, {CaseCount} cases, default={DefaultTarget}";
}
