using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public enum X86Register {
	Rax, Rcx, Rdx, Rbx, Rsp, Rbp, Rsi, Rdi,
	R8, R9, R10, R11, R12, R13, R14, R15,
	Eax, Ecx, Edx, Ebx, Esp, Ebp, Esi, Edi
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
