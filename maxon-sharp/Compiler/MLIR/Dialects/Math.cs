using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

// ============================================================================
// Base classes for Math operations
// ============================================================================

/// <summary>
/// Base class for math dialect operations.
/// </summary>
public abstract class MathOp : MlirOperation {
	public override string Dialect => "math";
	public override bool HasSideEffects => false;
}

/// <summary>
/// Base class for unary floating-point math operations.
/// </summary>
public abstract class FloatUnaryOp : MathOp {
	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	protected FloatUnaryOp(MlirValue operand) {
		Operands.Add(operand);
		CreateResult(operand.Type);
	}

	public override string? Verify() {
		if (Operands.Count != 1)
			return $"{FullName} requires exactly 1 operand";
		if (Operand.Type is not FloatType)
			return $"{FullName} requires float operand, got {Operand.Type}";
		return null;
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = {FullName} {Operand} : {Operand.Type}");
	}
}

/// <summary>
/// Base class for binary floating-point math operations.
/// </summary>
public abstract class FloatBinaryMathOp : MathOp {
	public MlirValue Lhs => Operands[0];
	public MlirValue Rhs => Operands[1];
	public MlirValue Result => Results[0];

	protected FloatBinaryMathOp(MlirValue lhs, MlirValue rhs) {
		Operands.Add(lhs);
		Operands.Add(rhs);
		CreateResult(lhs.Type);
	}

	public override string? Verify() {
		if (Operands.Count != 2)
			return $"{FullName} requires exactly 2 operands";
		if (Lhs.Type != Rhs.Type)
			return $"{FullName} operands must have same type, got {Lhs.Type} and {Rhs.Type}";
		if (Lhs.Type is not FloatType)
			return $"{FullName} requires float operands, got {Lhs.Type}";
		return null;
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = {FullName} {Lhs}, {Rhs} : {Lhs.Type}");
	}
}

// ============================================================================
// Unary Operations
// ============================================================================

/// <summary>
/// Square root: %result = math.sqrt %operand : f64
/// </summary>
public sealed class SqrtOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "sqrt";
}

/// <summary>
/// Floor: %result = math.floor %operand : f64
/// Rounds toward negative infinity.
/// </summary>
public sealed class FloorOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "floor";
}

/// <summary>
/// Ceiling: %result = math.ceil %operand : f64
/// Rounds toward positive infinity.
/// </summary>
public sealed class CeilOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "ceil";
}

/// <summary>
/// Round: %result = math.round %operand : f64
/// Rounds to nearest integer, ties to even.
/// </summary>
public sealed class RoundOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "round";
}

/// <summary>
/// Absolute value (float): %result = math.absf %operand : f64
/// </summary>
public sealed class AbsFOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "absf";
}

// ============================================================================
// Binary Operations
// ============================================================================

/// <summary>
/// Minimum: %result = math.minf %lhs, %rhs : f64
/// Returns the smaller of two floating-point values.
/// </summary>
public sealed class MinFOp(MlirValue lhs, MlirValue rhs) : FloatBinaryMathOp(lhs, rhs) {
	public override string Mnemonic => "minf";
}

/// <summary>
/// Maximum: %result = math.maxf %lhs, %rhs : f64
/// Returns the larger of two floating-point values.
/// </summary>
public sealed class MaxFOp(MlirValue lhs, MlirValue rhs) : FloatBinaryMathOp(lhs, rhs) {
	public override string Mnemonic => "maxf";
}
