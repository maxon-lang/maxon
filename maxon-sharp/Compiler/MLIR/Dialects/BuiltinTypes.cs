using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

// ============================================================================
// Integer Types
// ============================================================================

/// <summary>
/// Fixed-width integer type (i1, i8, i16, i32, i64, etc.).
/// </summary>
public sealed record IntegerType(int BitWidth, bool IsSigned = true) : MlirType, IFunctionResultType, IMemRefElementType {
	public override string? Dialect => null;
	public override string Mnemonic => IsSigned ? $"i{BitWidth}" : $"ui{BitWidth}";
	public override int SizeInBytes => (BitWidth + 7) / 8;
	public override bool IsCopyType => true;

	public override string ToString() => Mnemonic;

	// Common types
	public static readonly IntegerType I1 = new(1);
	public static readonly IntegerType I8 = new(8);
	public static readonly IntegerType I16 = new(16);
	public static readonly IntegerType I32 = new(32);
	public static readonly IntegerType I64 = new(64);
	public static readonly IntegerType UI8 = new(8, IsSigned: false);
	public static readonly IntegerType UI16 = new(16, IsSigned: false);
	public static readonly IntegerType UI32 = new(32, IsSigned: false);
	public static readonly IntegerType UI64 = new(64, IsSigned: false);
}

// ============================================================================
// Floating Point Types
// ============================================================================

/// <summary>
/// Floating-point type (f16, f32, f64, etc.).
/// </summary>
public sealed record FloatType(int BitWidth) : MlirType, IFunctionResultType, IMemRefElementType {
	public override string? Dialect => null;
	public override string Mnemonic => $"f{BitWidth}";
	public override int SizeInBytes => BitWidth / 8;
	public override bool IsCopyType => true;

	public override string ToString() => Mnemonic;

	// Common types
	public static readonly FloatType F16 = new(16);
	public static readonly FloatType F32 = new(32);
	public static readonly FloatType F64 = new(64);
}

// ============================================================================
// Index Type
// ============================================================================

/// <summary>
/// Index type - target-specific integer for array indexing.
/// </summary>
public sealed record IndexType : MlirType, IFunctionResultType {
	public override string? Dialect => null;
	public override string Mnemonic => "index";
	public override int SizeInBytes => 8; // Assume 64-bit target
	public override bool IsCopyType => true;

	public static readonly IndexType Instance = new();
	private IndexType() { }

	public override string ToString() => Mnemonic;
}

// ============================================================================
// None Type (void)
// ============================================================================

/// <summary>
/// None type - represents no value (void in C-like languages).
/// </summary>
public sealed record NoneType : MlirType, IFunctionResultType {
	public override string? Dialect => null;
	public override string Mnemonic => "none";
	public override int SizeInBytes => 0;

	public static readonly NoneType Instance = new();
	private NoneType() { }

	public override string ToString() => Mnemonic;
}

// ============================================================================
// Function Type
// ============================================================================

/// <summary>
/// Function type - represents a function signature.
/// </summary>
public sealed record FunctionType(IReadOnlyList<MlirType> Inputs, IReadOnlyList<MlirType> Results) : MlirType {
	public override string? Dialect => null;
	public override string Mnemonic => "function";
	public override int SizeInBytes => 8; // Function pointer

	public override string ToString() {
		var inputStr = Inputs.Count switch {
			0 => "()",
			1 => $"({Inputs[0]})",
			_ => $"({string.Join(", ", Inputs)})"
		};
		var resultStr = Results.Count switch {
			0 => "()",
			1 => Results[0].ToString()!,
			_ => $"({string.Join(", ", Results)})"
		};
		return $"{inputStr} -> {resultStr}";
	}

	public static FunctionType Create(IEnumerable<MlirType> inputs, params MlirType[] results) =>
		new([.. inputs], results);
}

// ============================================================================
// Function Pointer Type
// ============================================================================

/// <summary>
/// Function pointer type - represents a pointer to a function.
/// Used for first-class functions and callbacks.
/// </summary>
public sealed record FunctionPtrType(IReadOnlyList<MlirType> ParamTypes, MlirType ReturnType) : MlirType, IFunctionResultType {
	public override string? Dialect => null;
	public override string Mnemonic => "fn_ptr";
	public override int SizeInBytes => 8; // 64-bit function pointer
	public override bool IsCopyType => true; // Function pointers are copied, not moved

	public override string ToString() {
		var paramsStr = ParamTypes.Count switch {
			0 => "()",
			1 => $"({ParamTypes[0]})",
			_ => $"({string.Join(", ", ParamTypes)})"
		};
		return $"fn{paramsStr} -> {ReturnType}";
	}
}

// ============================================================================
// Tuple Type
// ============================================================================

/// <summary>
/// Tuple type - fixed-size collection of potentially different types.
/// </summary>
public sealed record TupleType(IReadOnlyList<MlirType> Elements) : MlirType, IFunctionResultType {
	public override string? Dialect => null;
	public override string Mnemonic => "tuple";
	public override int SizeInBytes => Elements.Sum(e => e.SizeInBytes);

	public override string ToString() => $"tuple<{string.Join(", ", Elements)}>";
}

// ============================================================================
// MemRef Types
// ============================================================================

/// <summary>
/// Unranked memory reference type - pointer to memory of unknown shape.
/// </summary>
public sealed record UnrankedMemRefType(MlirType ElementType) : MlirType {
	public override string? Dialect => null;
	public override string Mnemonic => "memref";
	public override int SizeInBytes => 16; // Pointer + size

	public override string ToString() => $"memref<*x{ElementType}>";
}

/// <summary>
/// Ranked memory reference type - pointer to memory with known shape.
/// </summary>
public sealed record MemRefType(MlirType ElementType, IReadOnlyList<int>? Shape = null) : MlirType, IFunctionResultType {
	public override string? Dialect => null;
	public override string Mnemonic => "memref";
	public override int SizeInBytes => 8; // Just a pointer for now

	public bool IsScalar => Shape is null || Shape.Count == 0;
	public int Rank => Shape?.Count ?? 0;

	public override string ToString() {
		if (Shape is null || Shape.Count == 0)
			return $"memref<{ElementType}>";
		var shapeStr = string.Join("x", Shape.Select(d => d < 0 ? "?" : d.ToString()));
		return $"memref<{shapeStr}x{ElementType}>";
	}

	public static MemRefType Scalar(MlirType elementType) => new(elementType);
	public static MemRefType Vector(MlirType elementType, int size) => new(elementType, [size]);
}

// ============================================================================
// Pointer Type
// ============================================================================

/// <summary>
/// Low-level pointer type (for machine-level IR).
/// </summary>
public sealed record PtrType(MlirType? ElementType = null) : MlirType, IFunctionResultType, IMemRefElementType {
	public override string? Dialect => null;
	public override string Mnemonic => "ptr";
	public override int SizeInBytes => 8;

	public override string ToString() =>
		ElementType is null ? "ptr" : $"ptr<{ElementType}>";

	public static readonly PtrType Untyped = new();
}
