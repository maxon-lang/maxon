using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects.X86;

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
}

// ============================================================================
// Data Movement Operations
// ============================================================================

/// <summary>
/// Move: x86.mov dst, src
/// </summary>
public sealed class MovOp : X86Op {
	public override string Mnemonic => "mov";

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
/// </summary>
public sealed class IdivOp : X86Op {
	public override string Mnemonic => "idiv";

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
public sealed class CallOp(string target) : X86Op {
	public override string Mnemonic => "call";

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
/// </summary>
public sealed class CdqOp : X86Op {
	public override string Mnemonic => "cdq";

	public override void Print(MlirPrinter printer) {
		printer.PrintLine("x86.cdq");
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

	public override void Print(MlirPrinter printer) {
		printer.PrintLine("x86.epilogue");
	}
}
