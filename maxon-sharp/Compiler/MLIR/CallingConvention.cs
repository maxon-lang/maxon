using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

/// <summary>
/// Windows x64 calling convention definitions.
/// Centralizes ABI knowledge to avoid duplication across passes.
/// </summary>
public static class WindowsX64Abi {
	/// <summary>
	/// Integer argument registers (first 4 args): RCX, RDX, R8, R9
	/// </summary>
	public static readonly X86Register[] IntArgRegs = [
		X86Register.RCX,
		X86Register.RDX,
		X86Register.R8,
		X86Register.R9
	];

	/// <summary>
	/// Float argument registers (first 4 args, same positions as int): XMM0-XMM3
	/// </summary>
	public static readonly X86Register[] FloatArgRegs = [
		X86Register.XMM0,
		X86Register.XMM1,
		X86Register.XMM2,
		X86Register.XMM3
	];

	/// <summary>
	/// Caller-saved (volatile) GPRs - clobbered by calls
	/// </summary>
	public static readonly X86Register[] VolatileGprs = [
		X86Register.RAX,
		X86Register.RCX,
		X86Register.RDX,
		X86Register.R8,
		X86Register.R9,
		X86Register.R10,
		X86Register.R11
	];

	/// <summary>
	/// Callee-saved (non-volatile) GPRs - preserved across calls
	/// </summary>
	public static readonly X86Register[] NonVolatileGprs = [
		X86Register.RBX,
		X86Register.R12,
		X86Register.R13,
		X86Register.R14,
		X86Register.R15
	];

	/// <summary>
	/// Volatile XMM registers (caller-saved): XMM0-XMM5
	/// </summary>
	public static readonly X86Register[] VolatileXmm = [
		X86Register.XMM0,
		X86Register.XMM1,
		X86Register.XMM2,
		X86Register.XMM3,
		X86Register.XMM4,
		X86Register.XMM5
	];

	/// <summary>
	/// Non-volatile XMM registers (callee-saved): XMM6-XMM15
	/// </summary>
	public static readonly X86Register[] NonVolatileXmm = [
		X86Register.XMM6,
		X86Register.XMM7,
		X86Register.XMM8,
		X86Register.XMM9,
		X86Register.XMM10,
		X86Register.XMM11,
		X86Register.XMM12,
		X86Register.XMM13,
		X86Register.XMM14,
		X86Register.XMM15
	];

	/// <summary>
	/// Shadow space size required by Windows x64 (32 bytes for 4 register args)
	/// </summary>
	public const int ShadowSpaceSize = 32;

	/// <summary>
	/// Stack alignment requirement (16 bytes)
	/// </summary>
	public const int StackAlignment = 16;

	/// <summary>
	/// Integer return register
	/// </summary>
	public static readonly X86Register IntReturnReg = X86Register.RAX;

	/// <summary>
	/// Float return register
	/// </summary>
	public static readonly X86Register FloatReturnReg = X86Register.XMM0;

	/// <summary>
	/// Aligns a value up to the specified alignment.
	/// </summary>
	public static int AlignTo(int value, int alignment) =>
		(value + alignment - 1) & ~(alignment - 1);
}
