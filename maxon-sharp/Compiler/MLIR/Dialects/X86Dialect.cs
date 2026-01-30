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

public abstract class X86Op : MlirOperation;

public class X86PushReg(X86Register register) : X86Op {
	public X86Register Register { get; } = register;
	public override string Mnemonic => $"x86.push {Register.ToString().ToLower()}";
}

public class X86PopReg(X86Register register) : X86Op {
	public X86Register Register { get; } = register;
	public override string Mnemonic => $"x86.pop {Register.ToString().ToLower()}";
}

public class X86MovRegReg(X86Register dest, X86Register src) : X86Op {
	public X86Register Dest { get; } = dest;
	public X86Register Src { get; } = src;
	public override string Mnemonic => $"x86.mov {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86MovRegImm(X86Register dest, long immediate) : X86Op {
	public X86Register Dest { get; } = dest;
	public long Immediate { get; } = immediate;
	public override string Mnemonic => $"x86.mov {Dest.ToString().ToLower()}, {Immediate}";
}

public class X86Ret : X86Op {
	public override string Mnemonic => "x86.ret";
}

public class X86SubRegImm(X86Register dest, long immediate) : X86Op {
	public X86Register Dest { get; } = dest;
	public long Immediate { get; } = immediate;
	public override string Mnemonic => $"x86.sub {Dest.ToString().ToLower()}, {Immediate}";
}

public class X86AddRegImm(X86Register dest, long immediate) : X86Op {
	public X86Register Dest { get; } = dest;
	public long Immediate { get; } = immediate;
	public override string Mnemonic => $"x86.add {Dest.ToString().ToLower()}, {Immediate}";
}

public class X86AddRegReg(X86Register dest, X86Register src) : X86Op {
	public X86Register Dest { get; } = dest;
	public X86Register Src { get; } = src;
	public override string Mnemonic => $"x86.add {Dest.ToString().ToLower()}, {Src.ToString().ToLower()}";
}

public class X86CallDirect(string target) : X86Op {
	public string Target { get; } = target;
	public override string Mnemonic => $"x86.call {Target}";
}

public class X86MovSdXmmRipRel(X86XmmRegister dest, string rdataLabel) : X86Op {
	public X86XmmRegister Dest { get; } = dest;
	public string RdataLabel { get; } = rdataLabel;
	public override string Mnemonic => $"x86.movsd {Dest.ToString().ToLower()}, [rip+{RdataLabel}]";
}

public class X86MovSdMemXmm(int displacement, X86XmmRegister src) : X86Op {
	public int Displacement { get; } = displacement;
	public X86XmmRegister Src { get; } = src;
	public override string Mnemonic => $"x86.movsd [rbp{Displacement}], {Src.ToString().ToLower()}";
}

public class X86MovSdXmmMem(X86XmmRegister dest, int displacement) : X86Op {
	public X86XmmRegister Dest { get; } = dest;
	public int Displacement { get; } = displacement;
	public override string Mnemonic => $"x86.movsd {Dest.ToString().ToLower()}, [rbp{Displacement}]";
}

public class X86Ucomisd(X86XmmRegister src1, X86XmmRegister src2) : X86Op {
	public X86XmmRegister Src1 { get; } = src1;
	public X86XmmRegister Src2 { get; } = src2;
	public override string Mnemonic => $"x86.ucomisd {Src1.ToString().ToLower()}, {Src2.ToString().ToLower()}";
}

public class X86Jcc(string condition, string target) : X86Op {
	public string Condition { get; } = condition;
	public string Target { get; } = target;
	public override string Mnemonic => $"x86.j{Condition} {Target}";
}

public class X86Jmp(string target) : X86Op {
	public string Target { get; } = target;
	public override string Mnemonic => $"x86.jmp {Target}";
}
