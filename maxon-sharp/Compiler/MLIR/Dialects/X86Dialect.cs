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

public abstract class X86Op : IMlirOp {
	public abstract string Mnemonic { get; }
	public List<MlirValue> Operands { get; } = [];
	public List<MlirValue> Results { get; } = [];
	public Dictionary<string, MlirAttribute> Attributes { get; } = [];
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

public class X86CallDirectOp(string target) : X86Op {
	public string Target { get; } = target;
	public override string Mnemonic => $"x86.call {Target}";
}

public class X86MovMemRegOp(int displacement, X86Register src) : X86Op {
	public int Displacement { get; } = displacement;
	public X86Register Src { get; } = src;
	public override string Mnemonic => $"x86.mov [rbp{Displacement}], {Src.ToString().ToLower()}";
}

public class X86MovRegMemOp(X86Register dest, int displacement) : X86Op {
	public X86Register Dest { get; } = dest;
	public int Displacement { get; } = displacement;
	public override string Mnemonic => $"x86.mov {Dest.ToString().ToLower()}, [rbp{Displacement}]";
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

public class X86MovSdXmmMemOp(X86XmmRegister dest, int displacement) : X86Op {
	public X86XmmRegister Dest { get; } = dest;
	public int Displacement { get; } = displacement;
	public override string Mnemonic => $"x86.movsd {Dest.ToString().ToLower()}, [rbp{Displacement}]";
}

public class X86UcomisdOp(X86XmmRegister src1, X86XmmRegister src2) : X86Op {
	public X86XmmRegister Src1 { get; } = src1;
	public X86XmmRegister Src2 { get; } = src2;
	public override string Mnemonic => $"x86.ucomisd {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
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
