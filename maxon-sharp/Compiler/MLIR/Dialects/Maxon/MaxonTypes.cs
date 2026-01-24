using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects.Maxon;

// ============================================================================
// Maxon Struct Type
// ============================================================================

/// <summary>
/// Maxon struct type - represents a user-defined struct.
/// </summary>
public sealed record MaxonStructType(string Name, IReadOnlyList<MaxonFieldInfo> Fields) : MlirType, IFunctionResultType {
	public override string? Dialect => "maxon";
	public override string Mnemonic => $"struct<{Name}>";
	public override int SizeInBytes => Fields.Sum(f => f.Type.SizeInBytes);

	public override string ToString() => $"!maxon.struct<{Name}>";

	/// <summary>
	/// Gets a field by name.
	/// </summary>
	public MaxonFieldInfo? GetField(string name) => Fields.FirstOrDefault(f => f.Name == name);

	/// <summary>
	/// Gets the byte offset of a field.
	/// </summary>
	public int GetFieldOffset(string name) {
		int offset = 0;
		foreach (var field in Fields) {
			if (field.Name == name) return offset;
			offset += field.Type.SizeInBytes;
		}
		throw new ArgumentException($"Field '{name}' not found in struct '{Name}'");
	}
}

/// <summary>
/// Information about a struct field.
/// </summary>
public sealed record MaxonFieldInfo(string Name, MlirType Type, int Offset);

// ============================================================================
// Maxon Enum Type
// ============================================================================

/// <summary>
/// Maxon enum type - represents a tagged union.
/// </summary>
public sealed record MaxonEnumType(string Name, IReadOnlyList<MaxonVariantInfo> Variants, int TagSize = 1) : MlirType, IFunctionResultType {
	public override string? Dialect => "maxon";
	public override string Mnemonic => $"enum<{Name}>";

	public int MaxPayloadSize => Variants.Count > 0 ? Variants.Max(v => v.PayloadSize) : 0;
	public override int SizeInBytes => TagSize + MaxPayloadSize;

	public override string ToString() => $"!maxon.enum<{Name}>";

	/// <summary>
	/// Gets a variant by name.
	/// </summary>
	public MaxonVariantInfo? GetVariant(string name) => Variants.FirstOrDefault(v => v.Name == name);
}

/// <summary>
/// Information about an enum variant.
/// </summary>
public sealed record MaxonVariantInfo(string Name, int Tag, IReadOnlyList<MaxonFieldInfo> PayloadFields) {
	public int PayloadSize => PayloadFields.Sum(f => f.Type.SizeInBytes);
}

// ============================================================================
// Maxon Array Type
// ============================================================================

/// <summary>
/// Maxon managed array type.
/// </summary>
public sealed record MaxonArrayType(MlirType ElementType) : MlirType, IFunctionResultType {
	public override string? Dialect => "maxon";
	public override string Mnemonic => $"array<{ElementType}>";
	public override int SizeInBytes => 8; // Managed array is a pointer

	public override string ToString() => $"!maxon.array<{ElementType}>";
}

// ============================================================================
// Ownership Wrapper Types
// ============================================================================

/// <summary>
/// Wrapper indicating an owned value (has ownership, will be dropped).
/// </summary>
public sealed record OwnedType(MlirType Inner) : MlirType, IFunctionResultType {
	public override string? Dialect => "maxon";
	public override string Mnemonic => $"owned<{Inner}>";
	public override int SizeInBytes => Inner.SizeInBytes;

	public override string ToString() => $"!maxon.owned<{Inner}>";
}

/// <summary>
/// Wrapper indicating a borrowed reference (no ownership transfer).
/// </summary>
public sealed record BorrowedType(MlirType Inner, bool IsMutable = false) : MlirType, IFunctionResultType {
	public override string? Dialect => "maxon";
	public override string Mnemonic => IsMutable ? $"borrowed_mut<{Inner}>" : $"borrowed<{Inner}>";
	public override int SizeInBytes => 8; // Reference is a pointer

	public override string ToString() => IsMutable ? $"!maxon.borrowed_mut<{Inner}>" : $"!maxon.borrowed<{Inner}>";
}
