using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

// ============================================================================
// Base classes for Arith operations
// ============================================================================

/// <summary>
/// Base class for arith operations.
/// </summary>
public abstract class ArithOp : MlirOperation {
	public override string Dialect => "arith";
	public override bool HasSideEffects => false;

	public abstract MlirOperation Clone(Func<MlirValue, MlirValue> remapValue);
	public abstract MlirOperation? TryFold();
}

/// <summary>
/// Base class for binary integer operations.
/// </summary>
public abstract class IntBinaryOp : ArithOp {
	public MlirValue Lhs => Operands[0];
	public MlirValue Rhs => Operands[1];
	public MlirValue Result => Results[0];

	protected IntBinaryOp(MlirValue lhs, MlirValue rhs) {
		Operands.Add(lhs);
		Operands.Add(rhs);
		CreateResult(lhs.Type);
	}

	public override string? Verify() {
		if (Operands.Count != 2)
			return $"{FullName} requires exactly 2 operands";
		if (Lhs.Type != Rhs.Type)
			return $"{FullName} operands must have same type, got {Lhs.Type} and {Rhs.Type}";
		if (Lhs.Type is not IntegerType and not IndexType)
			return $"{FullName} requires integer operands, got {Lhs.Type}";
		return null;
	}

	protected static long? GetConstantValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is IntegerAttr intAttr)
			return intAttr.Value;
		return null;
	}

	protected ConstantOp CreateConstantReplacement(IntegerAttr value) {
		var constOp = new ConstantOp(value, Result.Type) { Results = { [0] = Result } };
		Result.DefiningOp = constOp;
		return constOp;
	}
}

/// <summary>
/// Base class for binary floating-point operations.
/// </summary>
public abstract class FloatBinaryOp : ArithOp {
	public MlirValue Lhs => Operands[0];
	public MlirValue Rhs => Operands[1];
	public MlirValue Result => Results[0];

	protected FloatBinaryOp(MlirValue lhs, MlirValue rhs) {
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
}

// ============================================================================
// Constants
// ============================================================================

/// <summary>
/// Constant value: %result = arith.constant value : type
/// </summary>
public sealed class ConstantOp : ArithOp {
	public override string Mnemonic => "constant";
	public override bool IsConstant => true;

	public MlirAttribute Value => Attributes["value"];
	public MlirValue Result => Results[0];

	public ConstantOp(MlirAttribute value, MlirType type) {
		Attributes["value"] = value;
		CreateResult(type);
	}

	public static ConstantOp Int(long value, IntegerType? type = null) {
		type ??= IntegerType.I64;
		return new ConstantOp(new IntegerAttr(value, type.BitWidth), type);
	}

	public static ConstantOp Float(double value, FloatType? type = null) {
		type ??= FloatType.F64;
		return new ConstantOp(new FloatAttr(value, type.BitWidth), type);
	}

	public static ConstantOp Bool(bool value) =>
		new(new IntegerAttr(value ? 1 : 0, 1), IntegerType.I1);

	public static ConstantOp Index(long value) =>
		new(new IntegerAttr(value, 64), IndexType.Instance);

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = arith.constant {Value}");
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new ConstantOp(Value, Result.Type);

	public override MlirOperation? TryFold() => null;
}

// ============================================================================
// Integer Arithmetic Operations
// ============================================================================

/// <summary>
/// Integer addition: %result = arith.addi %lhs, %rhs : type
/// </summary>
public sealed class AddIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "addi";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new AddIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r)
			return CreateConstantReplacement(new IntegerAttr(l + r));
		return null;
	}
}

/// <summary>
/// Integer subtraction: %result = arith.subi %lhs, %rhs : type
/// </summary>
public sealed class SubIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "subi";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new SubIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r)
			return CreateConstantReplacement(new IntegerAttr(l - r));
		return null;
	}
}

/// <summary>
/// Integer multiplication: %result = arith.muli %lhs, %rhs : type
/// </summary>
public sealed class MulIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "muli";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new MulIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		var lConst = GetConstantValue(Lhs);
		var rConst = GetConstantValue(Rhs);

		// x * 0 = 0
		if (lConst is 0 || rConst is 0)
			return CreateConstantReplacement(new IntegerAttr(0));

		// Full constant folding
		if (lConst is long l && rConst is long r)
			return CreateConstantReplacement(new IntegerAttr(l * r));

		return null;
	}
}

/// <summary>
/// Signed integer division: %result = arith.divsi %lhs, %rhs : type
/// </summary>
public sealed class DivSIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "divsi";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new DivSIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r && r != 0)
			return CreateConstantReplacement(new IntegerAttr(l / r));
		return null;
	}
}

/// <summary>
/// Unsigned integer division: %result = arith.divui %lhs, %rhs : type
/// </summary>
public sealed class DivUIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "divui";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new DivUIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r && r != 0)
			return CreateConstantReplacement(new IntegerAttr((long)((ulong)l / (ulong)r)));
		return null;
	}
}

/// <summary>
/// Signed integer remainder: %result = arith.remsi %lhs, %rhs : type
/// </summary>
public sealed class RemSIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "remsi";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new RemSIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r && r != 0)
			return CreateConstantReplacement(new IntegerAttr(l % r));
		return null;
	}
}

/// <summary>
/// Unsigned integer remainder: %result = arith.remui %lhs, %rhs : type
/// </summary>
public sealed class RemUIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "remui";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new RemUIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r && r != 0)
			return CreateConstantReplacement(new IntegerAttr((long)((ulong)l % (ulong)r)));
		return null;
	}
}

// ============================================================================
// Bitwise Operations
// ============================================================================

/// <summary>
/// Bitwise AND: %result = arith.andi %lhs, %rhs : type
/// </summary>
public sealed class AndIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "andi";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new AndIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r)
			return CreateConstantReplacement(new IntegerAttr(l & r));
		return null;
	}
}

/// <summary>
/// Bitwise OR: %result = arith.ori %lhs, %rhs : type
/// </summary>
public sealed class OrIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "ori";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new OrIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r)
			return CreateConstantReplacement(new IntegerAttr(l | r));
		return null;
	}
}

/// <summary>
/// Bitwise XOR: %result = arith.xori %lhs, %rhs : type
/// </summary>
public sealed class XOrIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "xori";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new XOrIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r)
			return CreateConstantReplacement(new IntegerAttr(l ^ r));
		return null;
	}
}

/// <summary>
/// Shift left: %result = arith.shli %lhs, %rhs : type
/// </summary>
public sealed class ShLIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "shli";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new ShLIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r)
			return CreateConstantReplacement(new IntegerAttr(l << (int)r));
		return null;
	}
}

/// <summary>
/// Arithmetic (signed) shift right: %result = arith.shrsi %lhs, %rhs : type
/// </summary>
public sealed class ShRSIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "shrsi";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new ShRSIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r)
			return CreateConstantReplacement(new IntegerAttr(l >> (int)r));
		return null;
	}
}

/// <summary>
/// Logical (unsigned) shift right: %result = arith.shrui %lhs, %rhs : type
/// </summary>
public sealed class ShRUIOp(MlirValue lhs, MlirValue rhs) : IntBinaryOp(lhs, rhs) {
	public override string Mnemonic => "shrui";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new ShRUIOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long l && GetConstantValue(Rhs) is long r)
			return CreateConstantReplacement(new IntegerAttr((long)((ulong)l >> (int)r)));
		return null;
	}
}

// ============================================================================
// Integer Comparisons
// ============================================================================

/// <summary>
/// Integer comparison predicates.
/// </summary>
public enum CmpIPredicate {
	Eq,   // Equal
	Ne,   // Not equal
	Slt,  // Signed less than
	Sle,  // Signed less or equal
	Sgt,  // Signed greater than
	Sge,  // Signed greater or equal
	Ult,  // Unsigned less than
	Ule,  // Unsigned less or equal
	Ugt,  // Unsigned greater than
	Uge   // Unsigned greater or equal
}

/// <summary>
/// Integer comparison: %result = arith.cmpi predicate, %lhs, %rhs : type
/// </summary>
public sealed class CmpIOp : ArithOp {
	public override string Mnemonic => "cmpi";

	public CmpIPredicate Predicate { get; }
	public MlirValue Lhs => Operands[0];
	public MlirValue Rhs => Operands[1];
	public MlirValue Result => Results[0];

	public CmpIOp(CmpIPredicate predicate, MlirValue lhs, MlirValue rhs) {
		Predicate = predicate;
		Operands.Add(lhs);
		Operands.Add(rhs);
		Attributes["predicate"] = new StringAttr(predicate.ToString().ToLowerInvariant());
		CreateResult(IntegerType.I1);
	}

	public override void Print(MlirPrinter printer) {
		var pred = Predicate.ToString().ToLowerInvariant();
		printer.PrintLine($"{Result} = arith.cmpi {pred}, {Lhs}, {Rhs} : {Lhs.Type}");
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new CmpIOp(Predicate, remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantValue(Lhs) is long lhsVal && GetConstantValue(Rhs) is long rhsVal) {
			bool result = EvaluateComparison(Predicate, lhsVal, rhsVal);
			var constOp = new ConstantOp(new IntegerAttr(result ? 1 : 0, 1), Result.Type) {
				Results = { [0] = Result }
			};
			Result.DefiningOp = constOp;
			return constOp;
		}
		return null;
	}

	private static long? GetConstantValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is IntegerAttr intAttr)
			return intAttr.Value;
		return null;
	}

	private static bool EvaluateComparison(CmpIPredicate predicate, long lhs, long rhs) {
		return predicate switch {
			CmpIPredicate.Eq => lhs == rhs,
			CmpIPredicate.Ne => lhs != rhs,
			CmpIPredicate.Slt => lhs < rhs,
			CmpIPredicate.Sle => lhs <= rhs,
			CmpIPredicate.Sgt => lhs > rhs,
			CmpIPredicate.Sge => lhs >= rhs,
			CmpIPredicate.Ult => (ulong)lhs < (ulong)rhs,
			CmpIPredicate.Ule => (ulong)lhs <= (ulong)rhs,
			CmpIPredicate.Ugt => (ulong)lhs > (ulong)rhs,
			CmpIPredicate.Uge => (ulong)lhs >= (ulong)rhs,
			_ => throw new NotSupportedException($"Unsupported comparison predicate: {predicate}")
		};
	}
}

// ============================================================================
// Floating-Point Operations
// ============================================================================

/// <summary>
/// Float addition: %result = arith.addf %lhs, %rhs : type
/// </summary>
public sealed class AddFOp(MlirValue lhs, MlirValue rhs) : FloatBinaryOp(lhs, rhs) {
	public override string Mnemonic => "addf";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new AddFOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Lhs) is double l && GetConstantDoubleValue(Rhs) is double r)
			return CreateConstantReplacement(new FloatAttr(l + r));
		return null;
	}
}

/// <summary>
/// Float subtraction: %result = arith.subf %lhs, %rhs : type
/// </summary>
public sealed class SubFOp(MlirValue lhs, MlirValue rhs) : FloatBinaryOp(lhs, rhs) {
	public override string Mnemonic => "subf";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new SubFOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Lhs) is double l && GetConstantDoubleValue(Rhs) is double r)
			return CreateConstantReplacement(new FloatAttr(l - r));
		return null;
	}
}

/// <summary>
/// Float multiplication: %result = arith.mulf %lhs, %rhs : type
/// </summary>
public sealed class MulFOp(MlirValue lhs, MlirValue rhs) : FloatBinaryOp(lhs, rhs) {
	public override string Mnemonic => "mulf";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new MulFOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Lhs) is double l && GetConstantDoubleValue(Rhs) is double r)
			return CreateConstantReplacement(new FloatAttr(l * r));
		return null;
	}
}

/// <summary>
/// Float division: %result = arith.divf %lhs, %rhs : type
/// </summary>
public sealed class DivFOp(MlirValue lhs, MlirValue rhs) : FloatBinaryOp(lhs, rhs) {
	public override string Mnemonic => "divf";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new DivFOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Lhs) is double l && GetConstantDoubleValue(Rhs) is double r && r != 0)
			return CreateConstantReplacement(new FloatAttr(l / r));
		return null;
	}
}

/// <summary>
/// Float remainder: %result = arith.remf %lhs, %rhs : type
/// </summary>
public sealed class RemFOp(MlirValue lhs, MlirValue rhs) : FloatBinaryOp(lhs, rhs) {
	public override string Mnemonic => "remf";

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new RemFOp(remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Lhs) is double l && GetConstantDoubleValue(Rhs) is double r && r != 0)
			return CreateConstantReplacement(new FloatAttr(l % r));
		return null;
	}
}

/// <summary>
/// Float negation: %result = arith.negf %operand : type
/// </summary>
public sealed class NegFOp : ArithOp {
	public override string Mnemonic => "negf";

	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	public NegFOp(MlirValue operand) {
		Operands.Add(operand);
		CreateResult(operand.Type);
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new NegFOp(remapValue(Operand));

	public override MlirOperation? TryFold() {
		if (Operand.DefiningOp is ConstantOp constOp && constOp.Value is FloatAttr floatAttr) {
			var constResult = new ConstantOp(new FloatAttr(-floatAttr.Value), Result.Type) {
				Results = { [0] = Result }
			};
			Result.DefiningOp = constResult;
			return constResult;
		}
		return null;
	}
}

// ============================================================================
// Floating-Point Comparisons
// ============================================================================

/// <summary>
/// Floating-point comparison predicates.
/// </summary>
public enum CmpFPredicate {
	False,  // Always false
	Oeq,    // Ordered equal
	Ogt,    // Ordered greater than
	Oge,    // Ordered greater or equal
	Olt,    // Ordered less than
	Ole,    // Ordered less or equal
	One,    // Ordered not equal
	Ord,    // Ordered (neither is NaN)
	Ueq,    // Unordered or equal
	Ugt,    // Unordered or greater than
	Uge,    // Unordered or greater or equal
	Ult,    // Unordered or less than
	Ule,    // Unordered or less or equal
	Une,    // Unordered or not equal
	Uno,    // Unordered (either is NaN)
	True    // Always true
}

/// <summary>
/// Floating-point comparison: %result = arith.cmpf predicate, %lhs, %rhs : type
/// </summary>
public sealed class CmpFOp : ArithOp {
	public override string Mnemonic => "cmpf";

	public CmpFPredicate Predicate { get; }
	public MlirValue Lhs => Operands[0];
	public MlirValue Rhs => Operands[1];
	public MlirValue Result => Results[0];

	public CmpFOp(CmpFPredicate predicate, MlirValue lhs, MlirValue rhs) {
		Predicate = predicate;
		Operands.Add(lhs);
		Operands.Add(rhs);
		Attributes["predicate"] = new StringAttr(predicate.ToString().ToLowerInvariant());
		CreateResult(IntegerType.I1);
	}

	public override void Print(MlirPrinter printer) {
		var pred = Predicate.ToString().ToLowerInvariant();
		printer.PrintLine($"{Result} = arith.cmpf {pred}, {Lhs}, {Rhs} : {Lhs.Type}");
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new CmpFOp(Predicate, remapValue(Lhs), remapValue(Rhs));

	public override MlirOperation? TryFold() {
		if (GetConstantDoubleValue(Lhs) is double lhsVal && GetConstantDoubleValue(Rhs) is double rhsVal) {
			bool result = EvaluateComparison(Predicate, lhsVal, rhsVal);
			var constOp = new ConstantOp(new IntegerAttr(result ? 1 : 0, 1), Result.Type) {
				Results = { [0] = Result }
			};
			Result.DefiningOp = constOp;
			return constOp;
		}
		return null;
	}

	private static double? GetConstantDoubleValue(MlirValue value) {
		if (value.DefiningOp is ConstantOp constOp && constOp.Value is FloatAttr floatAttr)
			return floatAttr.Value;
		return null;
	}

	private static bool EvaluateComparison(CmpFPredicate predicate, double lhs, double rhs) {
		return predicate switch {
			CmpFPredicate.False => false,
			CmpFPredicate.Oeq => !double.IsNaN(lhs) && !double.IsNaN(rhs) && lhs == rhs,
			CmpFPredicate.Ogt => !double.IsNaN(lhs) && !double.IsNaN(rhs) && lhs > rhs,
			CmpFPredicate.Oge => !double.IsNaN(lhs) && !double.IsNaN(rhs) && lhs >= rhs,
			CmpFPredicate.Olt => !double.IsNaN(lhs) && !double.IsNaN(rhs) && lhs < rhs,
			CmpFPredicate.Ole => !double.IsNaN(lhs) && !double.IsNaN(rhs) && lhs <= rhs,
			CmpFPredicate.One => !double.IsNaN(lhs) && !double.IsNaN(rhs) && lhs != rhs,
			CmpFPredicate.Ord => !double.IsNaN(lhs) && !double.IsNaN(rhs),
			CmpFPredicate.Ueq => double.IsNaN(lhs) || double.IsNaN(rhs) || lhs == rhs,
			CmpFPredicate.Ugt => double.IsNaN(lhs) || double.IsNaN(rhs) || lhs > rhs,
			CmpFPredicate.Uge => double.IsNaN(lhs) || double.IsNaN(rhs) || lhs >= rhs,
			CmpFPredicate.Ult => double.IsNaN(lhs) || double.IsNaN(rhs) || lhs < rhs,
			CmpFPredicate.Ule => double.IsNaN(lhs) || double.IsNaN(rhs) || lhs <= rhs,
			CmpFPredicate.Une => double.IsNaN(lhs) || double.IsNaN(rhs) || lhs != rhs,
			CmpFPredicate.Uno => double.IsNaN(lhs) || double.IsNaN(rhs),
			CmpFPredicate.True => true,
			_ => throw new NotSupportedException($"Unsupported float comparison predicate: {predicate}")
		};
	}
}

// ============================================================================
// Casts
// ============================================================================

/// <summary>
/// Sign-extend integer: %result = arith.extsi %operand : from to to
/// </summary>
public sealed class ExtSIOp : ArithOp {
	public override string Mnemonic => "extsi";

	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	public ExtSIOp(MlirValue operand, IntegerType targetType) {
		Operands.Add(operand);
		CreateResult(targetType);
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new ExtSIOp(remapValue(Operand), (IntegerType)Result.Type);

	public override MlirOperation? TryFold() => null;
}

/// <summary>
/// Zero-extend integer: %result = arith.extui %operand : from to to
/// </summary>
public sealed class ExtUIOp : ArithOp {
	public override string Mnemonic => "extui";

	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	public ExtUIOp(MlirValue operand, IntegerType targetType) {
		Operands.Add(operand);
		CreateResult(targetType);
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new ExtUIOp(remapValue(Operand), (IntegerType)Result.Type);

	public override MlirOperation? TryFold() => null;
}

/// <summary>
/// Truncate integer: %result = arith.trunci %operand : from to to
/// </summary>
public sealed class TruncIOp : ArithOp {
	public override string Mnemonic => "trunci";

	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	public TruncIOp(MlirValue operand, IntegerType targetType) {
		Operands.Add(operand);
		CreateResult(targetType);
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new TruncIOp(remapValue(Operand), (IntegerType)Result.Type);

	public override MlirOperation? TryFold() => null;
}

/// <summary>
/// Signed int to float: %result = arith.sitofp %operand : from to to
/// </summary>
public sealed class SIToFPOp : ArithOp {
	public override string Mnemonic => "sitofp";

	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	public SIToFPOp(MlirValue operand, FloatType targetType) {
		Operands.Add(operand);
		CreateResult(targetType);
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new SIToFPOp(remapValue(Operand), (FloatType)Result.Type);

	public override MlirOperation? TryFold() => null;
}

/// <summary>
/// Extend float: %result = arith.extf %operand : from to to
/// </summary>
public sealed class ExtFOp : ArithOp {
	public override string Mnemonic => "extf";

	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	public ExtFOp(MlirValue operand, FloatType targetType) {
		Operands.Add(operand);
		CreateResult(targetType);
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new ExtFOp(remapValue(Operand), (FloatType)Result.Type);

	public override MlirOperation? TryFold() => null;
}

/// <summary>
/// Truncate float: %result = arith.truncf %operand : from to to
/// </summary>
public sealed class TruncFOp : ArithOp {
	public override string Mnemonic => "truncf";

	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	public TruncFOp(MlirValue operand, FloatType targetType) {
		Operands.Add(operand);
		CreateResult(targetType);
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new TruncFOp(remapValue(Operand), (FloatType)Result.Type);

	public override MlirOperation? TryFold() => null;
}

/// <summary>
/// Cast between index and integer: %result = arith.index_cast %operand : from to to
/// </summary>
public sealed class IndexCastOp : ArithOp {
	public override string Mnemonic => "index_cast";

	public MlirValue Operand => Operands[0];
	public MlirValue Result => Results[0];

	public IndexCastOp(MlirValue operand, MlirType targetType) {
		Operands.Add(operand);
		CreateResult(targetType);
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new IndexCastOp(remapValue(Operand), Result.Type);

	public override MlirOperation? TryFold() => null;
}

// ============================================================================
// Select
// ============================================================================

/// <summary>
/// Conditional select: %result = arith.select %cond, %true, %false : type
/// </summary>
public sealed class SelectOp : ArithOp {
	public override string Mnemonic => "select";

	public MlirValue Condition => Operands[0];
	public MlirValue TrueValue => Operands[1];
	public MlirValue FalseValue => Operands[2];
	public MlirValue Result => Results[0];

	public SelectOp(MlirValue condition, MlirValue trueValue, MlirValue falseValue) {
		Operands.Add(condition);
		Operands.Add(trueValue);
		Operands.Add(falseValue);
		CreateResult(trueValue.Type);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = arith.select {Condition}, {TrueValue}, {FalseValue} : {TrueValue.Type}");
	}

	public override MlirOperation Clone(Func<MlirValue, MlirValue> remapValue)
		=> new SelectOp(remapValue(Condition), remapValue(TrueValue), remapValue(FalseValue));

	public override MlirOperation? TryFold() {
		// Fold if condition is constant
		if (Condition.DefiningOp is ConstantOp constOp && constOp.Value is IntegerAttr intAttr) {
			bool condValue = intAttr.Value != 0;
			MlirValue selected = condValue ? TrueValue : FalseValue;

			// If the selected value is a constant, create a new constant
			// Otherwise, we can't fold further - the pass will handle value replacement
			if (selected.DefiningOp is ConstantOp selectedConst) {
				var resultConst = new ConstantOp(selectedConst.Value, Result.Type) {
					Results = { [0] = Result }
				};
				Result.DefiningOp = resultConst;
				return resultConst;
			}
		}
		return null;
	}
}
