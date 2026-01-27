using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

// ============================================================================
// Base class for X86 operations
// ============================================================================

/// <summary>
/// Base class for X86 dialect operations.
/// </summary>
public abstract class X86Op : MlirOperation {
	public override string Dialect => "x86";
	public override bool HasSideEffects => true;

	/// <summary>
	/// Get the operands as X86Operand types.
	/// </summary>
	public List<X86Operand> X86Operands { get; } = [];

	/// <summary>
	/// Returns registers that are clobbered (destroyed) by this operation.
	/// Used by register allocation to save/restore live values.
	/// Every X86 operation must explicitly declare this, even if empty.
	/// </summary>
	public abstract IReadOnlyList<X86Register> ClobberedRegisters { get; }
}

// ============================================================================
// Data Movement Operations
// ============================================================================

/// <summary>
/// Move: x86.mov dst, src
/// </summary>
public sealed class MovOp : X86Op {
	public override string Mnemonic => "mov";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public MovOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.mov {Dst}, {Src}");
	}
}

/// <summary>
/// Load effective address: x86.lea dst, mem
/// </summary>
public sealed class LeaOp : X86Op {
	public override string Mnemonic => "lea";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public LeaOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.lea {Dst}, {Src}");
	}
}

/// <summary>
/// Load effective address of global: x86.lea_global dst, @name
/// Uses RIP-relative addressing to load the address of a global variable.
/// </summary>
public sealed class LeaGlobalOp : X86Op {
	public override string Mnemonic => "lea_global";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public string GlobalName { get; }

	public LeaGlobalOp(X86Operand dst, string globalName) {
		X86Operands.Add(dst);
		GlobalName = globalName;
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.lea_global {Dst}, @{GlobalName}");
	}
}

/// <summary>
/// Push onto stack: x86.push src
/// </summary>
public sealed class PushOp : X86Op {
	public override string Mnemonic => "push";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Src => X86Operands[0];

	public PushOp(X86Operand src) {
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.push {Src}");
	}
}

/// <summary>
/// Pop from stack: x86.pop dst
/// </summary>
public sealed class PopOp : X86Op {
	public override string Mnemonic => "pop";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];

	public PopOp(X86Operand dst) {
		X86Operands.Add(dst);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.pop {Dst}");
	}
}

// ============================================================================
// Integer Arithmetic Operations
// ============================================================================

/// <summary>
/// Add: x86.add dst, src (dst += src)
/// </summary>
public sealed class AddOp : X86Op {
	public override string Mnemonic => "add";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public AddOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.add {Dst}, {Src}");
	}
}

/// <summary>
/// Subtract: x86.sub dst, src (dst -= src)
/// </summary>
public sealed class SubOp : X86Op {
	public override string Mnemonic => "sub";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public SubOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.sub {Dst}, {Src}");
	}
}

/// <summary>
/// Signed multiply: x86.imul dst, src (dst *= src) or x86.imul dst, src1, src2 (dst = src1 * src2)
/// </summary>
public sealed class ImulOp : X86Op {
	public override string Mnemonic => "imul";

	// 2-operand imul clobbers RDX (high bits), 3-operand form does not
	public override IReadOnlyList<X86Register> ClobberedRegisters =>
		Src2 is null ? [X86Register.RDX] : [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src1 => X86Operands[1];
	public X86Operand? Src2 => X86Operands.Count > 2 ? X86Operands[2] : null;

	public ImulOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public ImulOp(X86Operand dst, X86Operand src1, X86Operand src2) {
		X86Operands.Add(dst);
		X86Operands.Add(src1);
		X86Operands.Add(src2);
	}

	public override void Print(MlirPrinter printer) {
		if (Src2 is not null) {
			printer.PrintLine($"x86.imul {Dst}, {Src1}, {Src2}");
		} else {
			printer.PrintLine($"x86.imul {Dst}, {Src1}");
		}
	}
}

/// <summary>
/// Signed divide: x86.idiv src (divides rdx:rax by src)
/// Note: The DivisionPattern sets up RAX/RDX before idiv and reads the results after.
/// The register allocator must avoid allocating vregs to RAX/RDX when they're live across division.
/// </summary>
public sealed class IdivOp : X86Op {
	public override string Mnemonic => "idiv";

	// idiv uses RAX/RDX but constraint analysis handles this - we can't push/pop
	// because idiv needs those values set up by the preceding cdq instruction
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Divisor => X86Operands[0];

	public IdivOp(X86Operand divisor) {
		X86Operands.Add(divisor);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.idiv {Divisor}");
	}
}

/// <summary>
/// Negate: x86.neg dst (dst = -dst)
/// </summary>
public sealed class NegOp : X86Op {
	public override string Mnemonic => "neg";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];

	public NegOp(X86Operand dst) {
		X86Operands.Add(dst);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.neg {Dst}");
	}
}

/// <summary>
/// Increment: x86.inc dst (dst++)
/// </summary>
public sealed class IncOp : X86Op {
	public override string Mnemonic => "inc";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];

	public IncOp(X86Operand dst) {
		X86Operands.Add(dst);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.inc {Dst}");
	}
}

/// <summary>
/// Decrement: x86.dec dst (dst--)
/// </summary>
public sealed class DecOp : X86Op {
	public override string Mnemonic => "dec";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];

	public DecOp(X86Operand dst) {
		X86Operands.Add(dst);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.dec {Dst}");
	}
}

// ============================================================================
// Bitwise Operations
// ============================================================================

/// <summary>
/// Bitwise AND: x86.and dst, src
/// </summary>
public sealed class AndOp : X86Op {
	public override string Mnemonic => "and";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public AndOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.and {Dst}, {Src}");
	}
}

/// <summary>
/// Bitwise OR: x86.or dst, src
/// </summary>
public sealed class OrOp : X86Op {
	public override string Mnemonic => "or";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public OrOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.or {Dst}, {Src}");
	}
}

/// <summary>
/// Bitwise XOR: x86.xor dst, src
/// </summary>
public sealed class XorOp : X86Op {
	public override string Mnemonic => "xor";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public XorOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.xor {Dst}, {Src}");
	}
}

/// <summary>
/// Bitwise NOT: x86.not dst
/// </summary>
public sealed class NotOp : X86Op {
	public override string Mnemonic => "not";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];

	public NotOp(X86Operand dst) {
		X86Operands.Add(dst);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.not {Dst}");
	}
}

/// <summary>
/// Shift left: x86.shl dst, count
/// </summary>
public sealed class ShlOp : X86Op {
	public override string Mnemonic => "shl";

	public X86Operand Dst => X86Operands[0];
	public X86Operand Count => X86Operands[1];

	// x86 SHL uses CL (RCX) for variable shift counts
	public override IReadOnlyList<X86Register> ClobberedRegisters =>
		Count is VRegOperand ? [X86Register.RCX] : [];

	public ShlOp(X86Operand dst, X86Operand count) {
		X86Operands.Add(dst);
		X86Operands.Add(count);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.shl {Dst}, {Count}");
	}
}

/// <summary>
/// Logical shift right: x86.shr dst, count
/// </summary>
public sealed class ShrOp : X86Op {
	public override string Mnemonic => "shr";

	public X86Operand Dst => X86Operands[0];
	public X86Operand Count => X86Operands[1];

	// x86 SHR uses CL (RCX) for variable shift counts
	public override IReadOnlyList<X86Register> ClobberedRegisters =>
		Count is VRegOperand ? [X86Register.RCX] : [];

	public ShrOp(X86Operand dst, X86Operand count) {
		X86Operands.Add(dst);
		X86Operands.Add(count);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.shr {Dst}, {Count}");
	}
}

/// <summary>
/// Arithmetic shift right: x86.sar dst, count
/// </summary>
public sealed class SarOp : X86Op {
	public override string Mnemonic => "sar";

	public X86Operand Dst => X86Operands[0];
	public X86Operand Count => X86Operands[1];

	// x86 SAR uses CL (RCX) for variable shift counts
	public override IReadOnlyList<X86Register> ClobberedRegisters =>
		Count is VRegOperand ? [X86Register.RCX] : [];

	public SarOp(X86Operand dst, X86Operand count) {
		X86Operands.Add(dst);
		X86Operands.Add(count);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.sar {Dst}, {Count}");
	}
}

// ============================================================================
// Floating Point Operations
// ============================================================================

/// <summary>
/// Move scalar double: x86.movsd dst, src
/// </summary>
public sealed class MovsdOp : X86Op {
	public override string Mnemonic => "movsd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public MovsdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.movsd {Dst}, {Src}");
	}
}

/// <summary>
/// Move quadword between GPR and XMM: x86.movq dst, src
/// Used to transfer 64-bit values between general purpose and XMM registers.
/// - movq xmm, r/m64: 66 REX.W 0F 6E /r
/// - movq r/m64, xmm: 66 REX.W 0F 7E /r
/// </summary>
public sealed class MovqOp : X86Op {
	public override string Mnemonic => "movq";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public MovqOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.movq {Dst}, {Src}");
	}
}

/// <summary>
/// Add scalar double: x86.addsd dst, src
/// </summary>
public sealed class AddsdOp : X86Op {
	public override string Mnemonic => "addsd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public AddsdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.addsd {Dst}, {Src}");
	}
}

/// <summary>
/// Subtract scalar double: x86.subsd dst, src
/// </summary>
public sealed class SubsdOp : X86Op {
	public override string Mnemonic => "subsd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public SubsdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.subsd {Dst}, {Src}");
	}
}

/// <summary>
/// Multiply scalar double: x86.mulsd dst, src
/// </summary>
public sealed class MulsdOp : X86Op {
	public override string Mnemonic => "mulsd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public MulsdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.mulsd {Dst}, {Src}");
	}
}

/// <summary>
/// Divide scalar double: x86.divsd dst, src
/// </summary>
public sealed class DivsdOp : X86Op {
	public override string Mnemonic => "divsd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public DivsdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.divsd {Dst}, {Src}");
	}
}

// ============================================================================
// Comparison and Flags
// ============================================================================

/// <summary>
/// Compare: x86.cmp left, right (sets flags)
/// </summary>
public sealed class CmpOp : X86Op {
	public override string Mnemonic => "cmp";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Left => X86Operands[0];
	public X86Operand Right => X86Operands[1];

	public CmpOp(X86Operand left, X86Operand right) {
		X86Operands.Add(left);
		X86Operands.Add(right);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.cmp {Left}, {Right}");
	}
}

/// <summary>
/// Test: x86.test left, right (sets flags from AND)
/// </summary>
public sealed class TestOp : X86Op {
	public override string Mnemonic => "test";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Left => X86Operands[0];
	public X86Operand Right => X86Operands[1];

	public TestOp(X86Operand left, X86Operand right) {
		X86Operands.Add(left);
		X86Operands.Add(right);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.test {Left}, {Right}");
	}
}

/// <summary>
/// Set byte on condition: x86.setcc cond, dst
/// </summary>
public sealed class SetccOp : X86Op {
	public override string Mnemonic => $"set{Condition.ToString().ToLowerInvariant()}";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86CondCode Condition { get; }
	public X86Operand Dst => X86Operands[0];

	public SetccOp(X86CondCode condition, X86Operand dst) {
		Condition = condition;
		X86Operands.Add(dst);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.set{Condition.ToString().ToLowerInvariant()} {Dst}");
	}
}

/// <summary>
/// Compare floating point: x86.comisd left, right (sets flags)
/// </summary>
public sealed class ComiOp : X86Op {
	public override string Mnemonic => "comisd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Left => X86Operands[0];
	public X86Operand Right => X86Operands[1];

	public ComiOp(X86Operand left, X86Operand right) {
		X86Operands.Add(left);
		X86Operands.Add(right);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.comisd {Left}, {Right}");
	}
}

// ============================================================================
// Control Flow
// ============================================================================

/// <summary>
/// Unconditional jump: x86.jmp target
/// </summary>
public sealed class JmpOp(string target) : X86Op {
	public override string Mnemonic => "jmp";
	public override bool IsTerminator => true;
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public string Target { get; } = target;

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.jmp {Target}");
	}
}

/// <summary>
/// Conditional jump: x86.jcc cond, target
/// </summary>
public sealed class JccOp(X86CondCode condition, string trueTarget, string falseTarget) : X86Op {
	public override string Mnemonic => $"j{Condition.ToString().ToLowerInvariant()}";
	public override bool IsTerminator => true;
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86CondCode Condition { get; } = condition;
	public string TrueTarget { get; } = trueTarget;
	public string FalseTarget { get; } = falseTarget;

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.j{Condition.ToString().ToLowerInvariant()} {TrueTarget}");
		printer.PrintLine($"x86.jmp {FalseTarget}");
	}
}

/// <summary>
/// Call: x86.call target
/// </summary>
public sealed class X86CallOp(string target) : X86Op {
	public override string Mnemonic => "call";

	// All volatile registers are clobbered by a call (Windows x64 ABI)
	public override IReadOnlyList<X86Register> ClobberedRegisters => [
		X86Register.RAX, X86Register.RCX, X86Register.RDX,
		X86Register.R8, X86Register.R9, X86Register.R10, X86Register.R11,
		X86Register.XMM0, X86Register.XMM1, X86Register.XMM2, X86Register.XMM3,
		X86Register.XMM4, X86Register.XMM5
	];

	public string Target { get; } = target;

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.call {Target}");
	}
}

/// <summary>
/// Return: x86.ret
/// </summary>
public sealed class RetOp : X86Op {
	public override string Mnemonic => "ret";
	public override bool IsTerminator => true;
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public RetOp() { }

	public override void Print(MlirPrinter printer) {
		printer.PrintLine("x86.ret");
	}
}

/// <summary>
/// Label definition: label:
/// </summary>
public sealed class LabelOp(string name) : X86Op {
	public override string Mnemonic => "label";
	public override bool HasSideEffects => false;
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public string Name { get; } = name;

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Name}:");
	}
}

// ============================================================================
// Conversions
// ============================================================================

/// <summary>
/// Convert signed integer to double: x86.cvtsi2sd dst, src
/// </summary>
public sealed class CvtsiOp : X86Op {
	public override string Mnemonic => "cvtsi2sd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public CvtsiOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.cvtsi2sd {Dst}, {Src}");
	}
}

/// <summary>
/// Convert double to signed integer (truncate): x86.cvttsd2si dst, src
/// </summary>
public sealed class CvttsdOp : X86Op {
	public override string Mnemonic => "cvttsd2si";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public CvttsdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.cvttsd2si {Dst}, {Src}");
	}
}

/// <summary>
/// Sign extend EAX to EDX:EAX: x86.cdq
/// Note: cdq clobbers RDX but we return [] because cdq is always
/// immediately followed by idiv which needs RDX. Constraint analysis handles this.
/// </summary>
public sealed class CdqOp : X86Op {
	public override string Mnemonic => "cdq";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public CdqOp() { }

	public override void Print(MlirPrinter printer) {
		printer.PrintLine("x86.cdq");
	}
}

// ============================================================================
// SSE Math Operations
// ============================================================================

/// <summary>
/// Square root of scalar double: x86.sqrtsd dst, src
/// Encoding: F2 0F 51 /r
/// </summary>
public sealed class SqrtsdOp : X86Op {
	public override string Mnemonic => "sqrtsd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public SqrtsdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.sqrtsd {Dst}, {Src}");
	}
}

/// <summary>
/// Rounding modes for roundsd instruction.
/// </summary>
public enum RoundingMode : byte {
	/// <summary>Round to nearest even (ties to even).</summary>
	Nearest = 0x08,
	/// <summary>Round toward negative infinity (floor).</summary>
	Floor = 0x09,
	/// <summary>Round toward positive infinity (ceil).</summary>
	Ceil = 0x0A,
	/// <summary>Round toward zero (truncate).</summary>
	Truncate = 0x0B,
}

/// <summary>
/// Round scalar double: x86.roundsd dst, src, mode
/// Encoding: 66 0F 3A 0B /r imm8
/// </summary>
public sealed class RoundsdOp : X86Op {
	public override string Mnemonic => "roundsd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];
	public RoundingMode Mode { get; }

	public RoundsdOp(X86Operand dst, X86Operand src, RoundingMode mode) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
		Mode = mode;
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.roundsd {Dst}, {Src}, {Mode}");
	}
}

/// <summary>
/// Minimum of scalar doubles: x86.minsd dst, src
/// Encoding: F2 0F 5D /r
/// </summary>
public sealed class MinsdOp : X86Op {
	public override string Mnemonic => "minsd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public MinsdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.minsd {Dst}, {Src}");
	}
}

/// <summary>
/// Maximum of scalar doubles: x86.maxsd dst, src
/// Encoding: F2 0F 5F /r
/// </summary>
public sealed class MaxsdOp : X86Op {
	public override string Mnemonic => "maxsd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public MaxsdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.maxsd {Dst}, {Src}");
	}
}

/// <summary>
/// Bitwise AND of packed doubles: x86.andpd dst, src
/// Encoding: 66 0F 54 /r
/// Used for abs() by ANDing with a mask that clears the sign bit.
/// </summary>
public sealed class AndpdOp : X86Op {
	public override string Mnemonic => "andpd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public AndpdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.andpd {Dst}, {Src}");
	}
}

/// <summary>
/// Bitwise XOR of packed doubles: x86.xorpd dst, src
/// Encoding: 66 0F 57 /r
/// Used for negation by XORing with a mask that flips the sign bit.
/// </summary>
public sealed class XorpdOp : X86Op {
	public override string Mnemonic => "xorpd";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public X86Operand Dst => X86Operands[0];
	public X86Operand Src => X86Operands[1];

	public XorpdOp(X86Operand dst, X86Operand src) {
		X86Operands.Add(dst);
		X86Operands.Add(src);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.xorpd {Dst}, {Src}");
	}
}

// ============================================================================
// Function Prologue/Epilogue
// ============================================================================

/// <summary>
/// Function prologue: push rbp; mov rbp, rsp; sub rsp, N
/// </summary>
public sealed class PrologueOp(int stackSize = 32) : X86Op {
	public override string Mnemonic => "prologue";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];
	public int StackSize { get; } = stackSize;

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"x86.prologue stack_size={StackSize}");
	}
}

/// <summary>
/// Function epilogue: mov rsp, rbp; pop rbp
/// </summary>
public sealed class EpilogueOp : X86Op {
	public override string Mnemonic => "epilogue";
	public override IReadOnlyList<X86Register> ClobberedRegisters => [];

	public EpilogueOp() { }

	public override void Print(MlirPrinter printer) {
		printer.PrintLine("x86.epilogue");
	}
}

// ============================================================================
// X86 Registers
// ============================================================================

/// <summary>
/// X86-64 general purpose registers.
/// </summary>
public enum X86Register {
	// 64-bit general purpose
	RAX, RBX, RCX, RDX, RSI, RDI, RSP, RBP,
	R8, R9, R10, R11, R12, R13, R14, R15,

	// 32-bit (lower half of 64-bit)
	EAX, EBX, ECX, EDX, ESI, EDI, ESP, EBP,
	R8D, R9D, R10D, R11D, R12D, R13D, R14D, R15D,

	// 16-bit
	AX, BX, CX, DX, SI, DI, SP, BP,
	R8W, R9W, R10W, R11W, R12W, R13W, R14W, R15W,

	// 8-bit
	AL, BL, CL, DL, SIL, DIL, SPL, BPL,
	R8B, R9B, R10B, R11B, R12B, R13B, R14B, R15B,
	AH, BH, CH, DH,

	// SSE/XMM (for floating point)
	XMM0, XMM1, XMM2, XMM3, XMM4, XMM5, XMM6, XMM7,
	XMM8, XMM9, XMM10, XMM11, XMM12, XMM13, XMM14, XMM15,
}

/// <summary>
/// X86 condition codes for conditional jumps and setcc.
/// </summary>
public enum X86CondCode {
	E,   // Equal (ZF=1)
	NE,  // Not equal (ZF=0)
	L,   // Less (signed) (SF≠OF)
	LE,  // Less or equal (signed) (ZF=1 or SF≠OF)
	G,   // Greater (signed) (ZF=0 and SF=OF)
	GE,  // Greater or equal (signed) (SF=OF)
	B,   // Below (unsigned) (CF=1)
	BE,  // Below or equal (unsigned) (CF=1 or ZF=1)
	A,   // Above (unsigned) (CF=0 and ZF=0)
	AE,  // Above or equal (unsigned) (CF=0)
	S,   // Sign (SF=1)
	NS,  // Not sign (SF=0)
	O,   // Overflow (OF=1)
	NO,  // Not overflow (OF=0)
	P,   // Parity (PF=1)
	NP,  // Not parity (PF=0)
}

// ============================================================================
// X86 Types
// ============================================================================

/// <summary>
/// Type representing a physical X86 register.
/// </summary>
public sealed record X86RegisterType(X86Register Register) : MlirType {
	public override string? Dialect => "x86";
	public override string Mnemonic => Register.ToString().ToLowerInvariant();
	public override int SizeInBytes => GetSize(Register);

	private static int GetSize(X86Register reg) => reg switch {
		>= X86Register.RAX and <= X86Register.R15 => 8,
		>= X86Register.EAX and <= X86Register.R15D => 4,
		>= X86Register.AX and <= X86Register.R15W => 2,
		>= X86Register.AL and <= X86Register.DH => 1,
		>= X86Register.XMM0 and <= X86Register.XMM15 => 16,
		_ => 8
	};

	public override string ToString() => Mnemonic;
}

/// <summary>
/// Type representing a stack slot.
/// </summary>
public sealed record X86StackSlotType(int Offset, int Size) : MlirType {
	public override string? Dialect => "x86";
	public override string Mnemonic => $"slot[{Offset}]";
	public override int SizeInBytes => Size;

	public override string ToString() => Offset >= 0 ? $"[rbp+{Offset}]" : $"[rbp{Offset}]";
}

// ============================================================================
// X86 Operands
// ============================================================================

/// <summary>
/// Represents an X86 operand (register, memory, immediate).
/// </summary>
public abstract record X86Operand;

/// <summary>
/// Physical register operand.
/// </summary>
public sealed record RegOperand(X86Register Register) : X86Operand {
	public override string ToString() => Register.ToString().ToLowerInvariant();
}

/// <summary>
/// Virtual register operand (pre-register allocation).
/// </summary>
/// <param name="Id">Unique identifier for the virtual register.</param>
/// <param name="Size">Size in bytes (default 8).</param>
/// <param name="IsFloat">True if this vreg holds a floating-point value (uses XMM register).</param>
public sealed record VRegOperand(int Id, int Size = 8, bool IsFloat = false) : X86Operand {
	public override string ToString() => IsFloat ? $"vf{Id}" : $"v{Id}";
}

/// <summary>
/// Immediate value operand.
/// </summary>
public sealed record ImmOperand(long Value) : X86Operand {
	public override string ToString() => Value.ToString();
}

/// <summary>
/// Memory operand [base + index*scale + disp].
/// </summary>
public sealed record MemOperand(
	X86Operand? Base = null,
	X86Operand? Index = null,
	int Scale = 1,
	int Displacement = 0,
	int Size = 8
) : X86Operand {
	public override string ToString() {
		var parts = new List<string>();
		if (Base is not null) parts.Add(Base.ToString()!);
		if (Index is not null) {
			var idxStr = Scale != 1 ? $"{Index}*{Scale}" : Index.ToString()!;
			parts.Add(idxStr);
		}
		var addr = string.Join("+", parts);
		if (Displacement != 0) {
			if (Displacement > 0)
				addr = parts.Count > 0 ? $"{addr}+{Displacement}" : Displacement.ToString();
			else
				addr = parts.Count > 0 ? $"{addr}{Displacement}" : Displacement.ToString();
		} else if (parts.Count == 0) {
			addr = "0";
		}
		var sizePrefix = Size switch {
			1 => "byte ptr ",
			2 => "word ptr ",
			4 => "dword ptr ",
			8 => "qword ptr ",
			_ => ""
		};
		return $"{sizePrefix}[{addr}]";
	}
}

/// <summary>
/// Label reference operand.
/// </summary>
public sealed record LabelOperand(string Name) : X86Operand {
	public override string ToString() => Name;
}

/// <summary>
/// RIP-relative address operand.
/// </summary>
public sealed record RipRelOperand(string Symbol) : X86Operand {
	public override string ToString() => $"[rip + {Symbol}]";
}
