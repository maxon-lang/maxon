using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects.X86;

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
		if (Displacement != 0 || parts.Count == 0) {
			if (Displacement >= 0 && parts.Count > 0)
				parts.Add($"+{Displacement}");
			else
				parts.Add(Displacement.ToString());
		}
		var sizePrefix = Size switch {
			1 => "byte ptr ",
			2 => "word ptr ",
			4 => "dword ptr ",
			8 => "qword ptr ",
			_ => ""
		};
		return $"{sizePrefix}[{string.Join("", parts)}]";
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
