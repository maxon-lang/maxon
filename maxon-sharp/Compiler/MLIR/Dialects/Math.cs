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

	protected static double? GetConstantDoubleValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is FloatAttr floatAttr)
			return floatAttr.Value;
		return null;
	}

	protected ConstantOp CreateConstantReplacement(FloatAttr value) {
		var constOp = new ConstantOp(value, Result.Type) { Results = { [0] = Result } };
		Result.DefiningOp = constOp;
		return constOp;
	}

	/// <summary>
	/// Attempts to fold this operation to a constant. Must be implemented by all subclasses.
	/// Return null if folding is not possible.
	/// </summary>
	public abstract MlirOperation? TryFold();
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

	protected static double? GetConstantDoubleValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is FloatAttr floatAttr)
			return floatAttr.Value;
		return null;
	}

	protected ConstantOp CreateConstantReplacement(FloatAttr value) {
		var constOp = new ConstantOp(value, Result.Type) { Results = { [0] = Result } };
		Result.DefiningOp = constOp;
		return constOp;
	}

	/// <summary>
	/// Attempts to fold this operation to a constant. Must be implemented by all subclasses.
	/// Return null if folding is not possible.
	/// </summary>
	public abstract MlirOperation? TryFold();
}

// ============================================================================
// Unary Operations
// ============================================================================

/// <summary>
/// Square root: %result = math.sqrt %operand : f64
/// </summary>
public sealed class SqrtOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "sqrt";

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Operand) is double v && v >= 0)
			return CreateConstantReplacement(new FloatAttr(Math.Sqrt(v)));
		return null;
	}
}

/// <summary>
/// Floor: %result = math.floor %operand : f64
/// Rounds toward negative infinity.
/// </summary>
public sealed class FloorOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "floor";

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Operand) is double v)
			return CreateConstantReplacement(new FloatAttr(Math.Floor(v)));
		return null;
	}
}

/// <summary>
/// Ceiling: %result = math.ceil %operand : f64
/// Rounds toward positive infinity.
/// </summary>
public sealed class CeilOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "ceil";

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Operand) is double v)
			return CreateConstantReplacement(new FloatAttr(Math.Ceiling(v)));
		return null;
	}
}

/// <summary>
/// Round: %result = math.round %operand : f64
/// Rounds to nearest integer, ties to even.
/// </summary>
public sealed class RoundOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "round";

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Operand) is double v)
			return CreateConstantReplacement(new FloatAttr(Math.Round(v, MidpointRounding.ToEven)));
		return null;
	}
}

/// <summary>
/// Absolute value (float): %result = math.absf %operand : f64
/// </summary>
public sealed class AbsFOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "absf";

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Operand) is double v)
			return CreateConstantReplacement(new FloatAttr(Math.Abs(v)));
		return null;
	}
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

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Lhs) is double l && GetConstantDoubleValue(Rhs) is double r)
			return CreateConstantReplacement(new FloatAttr(Math.Min(l, r)));
		return null;
	}
}

/// <summary>
/// Maximum: %result = math.maxf %lhs, %rhs : f64
/// Returns the larger of two floating-point values.
/// </summary>
public sealed class MaxFOp(MlirValue lhs, MlirValue rhs) : FloatBinaryMathOp(lhs, rhs) {
	public override string Mnemonic => "maxf";

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Lhs) is double l && GetConstantDoubleValue(Rhs) is double r)
			return CreateConstantReplacement(new FloatAttr(Math.Max(l, r)));
		return null;
	}
}

// ============================================================================
// Conversion Operations
// ============================================================================

/// <summary>
/// Truncate float to integer: %result = math.trunc %operand : f64 -> i64
/// Converts a floating-point value to a signed integer by truncating toward zero.
/// </summary>
public sealed class TruncOp : MathOp {
	public override string Mnemonic => "trunc";

	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	public TruncOp(MlirValue operand, IntegerType targetType) {
		Operands.Add(operand);
		CreateResult(targetType);
	}

	public override string? Verify() {
		if (Operands.Count != 1)
			return $"{FullName} requires exactly 1 operand";
		if (Operand.Type is not FloatType)
			return $"{FullName} requires float operand, got {Operand.Type}";
		if (Result.Type is not IntegerType)
			return $"{FullName} requires integer result, got {Result.Type}";
		return null;
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = {FullName} {Operand} : {Operand.Type} -> {Result.Type}");
	}

	public MlirOperation? TryFold() {
		if (Operand.DefiningOp is ConstantOp constOp && constOp.Value is FloatAttr floatAttr) {
			var constResult = new ConstantOp(new IntegerAttr((long)floatAttr.Value), Result.Type) {
				Results = { [0] = Result }
			};
			Result.DefiningOp = constResult;
			return constResult;
		}
		return null;
	}
}
