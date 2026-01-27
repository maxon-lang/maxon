using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Passes;

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

	public abstract MlirOperation Clone(Func<MlirValue, MlirValue> remapValue);
	public abstract FoldResult TryFold(FoldingContext context);
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

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new SqrtOp(remapValue(Operand));

	public override FoldResult TryFold(FoldingContext context) {
		var constVal = FoldingContext.GetConstantDoubleValue(Operand);
		if (constVal is double v && v >= 0)
			return FoldResult.WithOperation(FoldingContext.CreateConstantReplacement(new FloatAttr(Math.Sqrt(v)), Result));
		return FoldResult.None;
	}
}

/// <summary>
/// Floor: %result = math.floor %operand : f64
/// Rounds toward negative infinity.
/// </summary>
public sealed class FloorOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "floor";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new FloorOp(remapValue(Operand));

	public override FoldResult TryFold(FoldingContext context) {
		var constVal = FoldingContext.GetConstantDoubleValue(Operand);
		if (constVal is double v)
			return FoldResult.WithOperation(FoldingContext.CreateConstantReplacement(new FloatAttr(Math.Floor(v)), Result));
		return FoldResult.None;
	}
}

/// <summary>
/// Ceiling: %result = math.ceil %operand : f64
/// Rounds toward positive infinity.
/// </summary>
public sealed class CeilOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "ceil";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new CeilOp(remapValue(Operand));

	public override FoldResult TryFold(FoldingContext context) {
		var constVal = FoldingContext.GetConstantDoubleValue(Operand);
		if (constVal is double v)
			return FoldResult.WithOperation(FoldingContext.CreateConstantReplacement(new FloatAttr(Math.Ceiling(v)), Result));
		return FoldResult.None;
	}
}

/// <summary>
/// Round: %result = math.round %operand : f64
/// Rounds to nearest integer, ties to even.
/// </summary>
public sealed class RoundOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "round";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new RoundOp(remapValue(Operand));

	public override FoldResult TryFold(FoldingContext context) {
		var constVal = FoldingContext.GetConstantDoubleValue(Operand);
		if (constVal is double v)
			return FoldResult.WithOperation(FoldingContext.CreateConstantReplacement(new FloatAttr(Math.Round(v, MidpointRounding.ToEven)), Result));
		return FoldResult.None;
	}
}

/// <summary>
/// Absolute value (float): %result = math.absf %operand : f64
/// </summary>
public sealed class AbsFOp(MlirValue operand) : FloatUnaryOp(operand) {
	public override string Mnemonic => "absf";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new AbsFOp(remapValue(Operand));

	public override FoldResult TryFold(FoldingContext context) {
		var constVal = FoldingContext.GetConstantDoubleValue(Operand);
		if (constVal is double v)
			return FoldResult.WithOperation(FoldingContext.CreateConstantReplacement(new FloatAttr(Math.Abs(v)), Result));
		return FoldResult.None;
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

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new MinFOp(remapValue(Lhs), remapValue(Rhs));

	public override FoldResult TryFold(FoldingContext context) {
		var lConst = FoldingContext.GetConstantDoubleValue(Lhs);
		var rConst = FoldingContext.GetConstantDoubleValue(Rhs);
		if (lConst is double l && rConst is double r)
			return FoldResult.WithOperation(FoldingContext.CreateConstantReplacement(new FloatAttr(Math.Min(l, r)), Result));
		return FoldResult.None;
	}
}

/// <summary>
/// Maximum: %result = math.maxf %lhs, %rhs : f64
/// Returns the larger of two floating-point values.
/// </summary>
public sealed class MaxFOp(MlirValue lhs, MlirValue rhs) : FloatBinaryMathOp(lhs, rhs) {
	public override string Mnemonic => "maxf";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new MaxFOp(remapValue(Lhs), remapValue(Rhs));

	public override FoldResult TryFold(FoldingContext context) {
		var lConst = FoldingContext.GetConstantDoubleValue(Lhs);
		var rConst = FoldingContext.GetConstantDoubleValue(Rhs);
		if (lConst is double l && rConst is double r)
			return FoldResult.WithOperation(FoldingContext.CreateConstantReplacement(new FloatAttr(Math.Max(l, r)), Result));
		return FoldResult.None;
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

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new TruncOp(remapValue(Operand), (IntegerType)Result.Type);

	public override FoldResult TryFold(FoldingContext context) {
		var constVal = FoldingContext.GetConstantDoubleValue(Operand);
		if (constVal is double v)
			return FoldResult.WithOperation(FoldingContext.CreateConstantReplacement(new IntegerAttr((long)v), Result));
		return FoldResult.None;
	}
}
