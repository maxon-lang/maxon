using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

// ============================================================================
// Base class for Maxon operations
// ============================================================================

/// <summary>
/// Base class for Maxon dialect operations.
/// </summary>
public abstract class MaxonOp : MlirOperation {
	public override string Dialect => "maxon";
}

// ============================================================================
// Ownership Operations
// ============================================================================

/// <summary>
/// Transfer ownership: %result = maxon.move %value : type
/// Marks the source as moved (invalid for further use).
/// </summary>
public sealed class MoveOp : MaxonOp {
	public override string Mnemonic => "move";
	public override bool HasSideEffects => true;

	public MlirValue Source => Operands[0];
	public MlirValue Result => Results[0];

	public MoveOp(MlirValue source) {
		Operands.Add(source);
		CreateResult(new OwnedType(source.Type));
	}
}

/// <summary>
/// Borrow a value: %result = maxon.borrow %value : type
/// Creates a reference without transferring ownership.
/// </summary>
public sealed class BorrowOp : MaxonOp {
	public override string Mnemonic => "borrow";
	public override bool HasSideEffects => false;

	public MlirValue Source => Operands[0];
	public MlirValue Result => Results[0];
	public bool IsMutable { get; }

	public BorrowOp(MlirValue source, bool isMutable = false) {
		IsMutable = isMutable;
		Operands.Add(source);
		var innerType = source.Type is OwnedType owned ? owned.Inner : source.Type;
		CreateResult(new BorrowedType(innerType, isMutable));
	}
}

/// <summary>
/// Drop an owned value: maxon.drop %value : type
/// Explicitly releases ownership and runs destructor.
/// </summary>
public sealed class DropOp : MaxonOp {
	public override string Mnemonic => "drop";
	public override bool HasSideEffects => true;

	public MlirValue Value => Operands[0];

	public DropOp(MlirValue value) {
		Operands.Add(value);
	}
}

// ============================================================================
// Struct Operations
// ============================================================================

/// <summary>
/// Initialize a struct: %result = maxon.struct_init @TypeName { field1 = %v1, field2 = %v2 }
/// </summary>
public sealed class StructInitOp : MaxonOp {
	public override string Mnemonic => "struct_init";
	public override bool HasSideEffects => false;

	public MaxonStructType StructType { get; }
	public IReadOnlyDictionary<string, MlirValue> FieldValues { get; }
	public MlirValue Result => Results[0];

	public StructInitOp(MaxonStructType structType, Dictionary<string, MlirValue> fieldValues) {
		StructType = structType;
		FieldValues = fieldValues;
		foreach (var field in structType.Fields) {
			if (fieldValues.TryGetValue(field.Name, out var value))
				Operands.Add(value);
		}
		Attributes["type"] = new SymbolRefAttr(structType.Name);
		CreateResult(structType);
	}

	public override void Print(MlirPrinter printer) {
		var fields = FieldValues.Select(kv => $"{kv.Key} = {kv.Value}");
		printer.PrintLine($"{Result} = maxon.struct_init @{StructType.Name} {{ {string.Join(", ", fields)} }} : {StructType}");
	}
}

/// <summary>
/// Get a struct field value: %result = maxon.field_get %struct["fieldName"] : type
/// </summary>
public sealed class FieldGetOp : MaxonOp {
	public override string Mnemonic => "field_get";
	public override bool HasSideEffects => false;

	public MlirValue Struct => Operands[0];
	public string FieldName { get; }
	public MlirValue Result => Results[0];

	public FieldGetOp(MlirValue structVal, string fieldName, MlirType fieldType) {
		FieldName = fieldName;
		Operands.Add(structVal);
		Attributes["field"] = new StringAttr(fieldName);
		CreateResult(fieldType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.field_get {Struct}[\"{FieldName}\"] : {Result.Type}");
	}
}

/// <summary>
/// Set a struct field value: maxon.field_set %struct["fieldName"], %value : type
/// </summary>
public sealed class FieldSetOp : MaxonOp {
	public override string Mnemonic => "field_set";
	public override bool HasSideEffects => true;

	public MlirValue Struct => Operands[0];
	public MlirValue Value => Operands[1];
	public string FieldName { get; }

	public FieldSetOp(MlirValue structVal, string fieldName, MlirValue value) {
		FieldName = fieldName;
		Operands.Add(structVal);
		Operands.Add(value);
		Attributes["field"] = new StringAttr(fieldName);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.field_set {Struct}[\"{FieldName}\"], {Value} : {Value.Type}");
	}
}

/// <summary>
/// Get a pointer to a struct field: %result = maxon.field_ptr %struct["fieldName"] : ptr
/// </summary>
public sealed class FieldPtrOp : MaxonOp {
	public override string Mnemonic => "field_ptr";
	public override bool HasSideEffects => false;

	public MlirValue Struct => Operands[0];
	public string FieldName { get; }
	public int Offset { get; }
	public MlirValue Result => Results[0];

	public FieldPtrOp(MlirValue structVal, string fieldName, int offset, MlirType fieldType) {
		FieldName = fieldName;
		Offset = offset;
		Operands.Add(structVal);
		Attributes["field"] = new StringAttr(fieldName);
		Attributes["offset"] = new IntegerAttr(offset);
		CreateResult(new PtrType(fieldType));
	}
}

// ============================================================================
// Enum Operations
// ============================================================================

/// <summary>
/// Initialize an enum variant: %result = maxon.enum_init @TypeName::VariantName { %payload }
/// </summary>
public sealed class EnumInitOp : MaxonOp {
	public override string Mnemonic => "enum_init";
	public override bool HasSideEffects => false;

	public MaxonEnumType EnumType { get; }
	public string VariantName { get; }
	public IReadOnlyList<MlirValue> PayloadValues { get; }
	public MlirValue Result => Results[0];

	public EnumInitOp(MaxonEnumType enumType, string variantName, List<MlirValue>? payloadValues = null) {
		EnumType = enumType;
		VariantName = variantName;
		PayloadValues = payloadValues ?? [];
		foreach (var v in PayloadValues)
			Operands.Add(v);
		Attributes["type"] = new SymbolRefAttr(enumType.Name);
		Attributes["variant"] = new StringAttr(variantName);
		CreateResult(enumType);
	}

	public override void Print(MlirPrinter printer) {
		var payloadStr = PayloadValues.Count > 0 ? $" {{ {string.Join(", ", PayloadValues)} }}" : "";
		printer.PrintLine($"{Result} = maxon.enum_init @{EnumType.Name}::{VariantName}{payloadStr} : {EnumType}");
	}
}

/// <summary>
/// Match on an enum: maxon.match %enum { Variant1(%v) -> ^block1, Variant2 -> ^block2 }
/// </summary>
public sealed class MatchOp : MaxonOp {
	public override string Mnemonic => "match";
	public override bool IsTerminator => true;

	public MlirValue EnumValue => Operands[0];
	public IReadOnlyList<MatchArm> Arms { get; }

	public MatchOp(MlirValue enumValue, List<MatchArm> arms) {
		Arms = arms;
		Operands.Add(enumValue);
		foreach (var arm in arms) {
			Successors.Add(arm.Block);
		}
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.match {EnumValue} {{");
		printer.Indent();
		foreach (var arm in Arms) {
			var argStr = arm.PayloadArgs.Count > 0 ? $"({string.Join(", ", arm.PayloadArgs)})" : "";
			printer.PrintLine($"{arm.VariantName}{argStr} -> ^{arm.Block.Name}");
		}
		printer.Dedent();
		printer.PrintLine("}");
	}
}

/// <summary>
/// A single arm of a match expression.
/// </summary>
public sealed record MatchArm(string VariantName, MlirBlock Block, IReadOnlyList<MlirValue> PayloadArgs);

/// <summary>
/// Get the tag of an enum: %result = maxon.get_tag %enum : i8
/// </summary>
public sealed class GetTagOp : MaxonOp {
	public override string Mnemonic => "get_tag";
	public override bool HasSideEffects => false;

	public MlirValue EnumValue => Operands[0];
	public MlirValue Result => Results[0];

	public GetTagOp(MlirValue enumValue, int tagBitWidth = 8) {
		Operands.Add(enumValue);
		CreateResult(new IntegerType(tagBitWidth));
	}
}

/// <summary>
/// Get the payload of an enum variant: %result = maxon.get_payload %enum : type
/// </summary>
public sealed class GetPayloadOp : MaxonOp {
	public override string Mnemonic => "get_payload";
	public override bool HasSideEffects => false;

	public MlirValue EnumValue => Operands[0];
	public string VariantName { get; }
	public int PayloadIndex { get; }
	public MlirValue Result => Results[0];

	public GetPayloadOp(MlirValue enumValue, string variantName, int payloadIndex, MlirType payloadType) {
		VariantName = variantName;
		PayloadIndex = payloadIndex;
		Operands.Add(enumValue);
		Attributes["variant"] = new StringAttr(variantName);
		Attributes["index"] = new IntegerAttr(payloadIndex);
		CreateResult(payloadType);
	}
}

// ============================================================================
// Array Operations
// ============================================================================

/// <summary>
/// Create a new managed array: %result = maxon.array_new %size : !maxon.array<type>
/// </summary>
public sealed class ArrayNewOp : MaxonOp {
	public override string Mnemonic => "array_new";
	public override bool HasSideEffects => true;

	public MlirValue Size => Operands[0];
	public MaxonArrayType ArrayType { get; }
	public MlirValue Result => Results[0];

	public ArrayNewOp(MlirValue size, MlirType elementType) {
		ArrayType = new MaxonArrayType(elementType);
		Operands.Add(size);
		CreateResult(ArrayType);
	}
}

/// <summary>
/// Get array element: %result = maxon.array_get %array[%index] : type
/// </summary>
public sealed class ArrayGetOp : MaxonOp {
	public override string Mnemonic => "array_get";
	public override bool HasSideEffects => false;

	public MlirValue Array => Operands[0];
	public MlirValue Index => Operands[1];
	public MlirValue Result => Results[0];

	public ArrayGetOp(MlirValue array, MlirValue index, MlirType elementType) {
		Operands.Add(array);
		Operands.Add(index);
		CreateResult(elementType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.array_get {Array}[{Index}] : {Result.Type}");
	}
}

/// <summary>
/// Set array element: maxon.array_set %array[%index], %value : type
/// </summary>
public sealed class ArraySetOp : MaxonOp {
	public override string Mnemonic => "array_set";
	public override bool HasSideEffects => true;

	public MlirValue Array => Operands[0];
	public MlirValue Index => Operands[1];
	public MlirValue Value => Operands[2];

	public ArraySetOp(MlirValue array, MlirValue index, MlirValue value) {
		Operands.Add(array);
		Operands.Add(index);
		Operands.Add(value);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.array_set {Array}[{Index}], {Value} : {Value.Type}");
	}
}

/// <summary>
/// Get array length: %result = maxon.array_len %array : i64
/// </summary>
public sealed class ArrayLenOp : MaxonOp {
	public override string Mnemonic => "array_len";
	public override bool HasSideEffects => false;

	public MlirValue Array => Operands[0];
	public MlirValue Result => Results[0];

	public ArrayLenOp(MlirValue array) {
		Operands.Add(array);
		CreateResult(IntegerType.I64);
	}
}

/// <summary>
/// Get pointer to array element: %result = maxon.array_ptr %array[%index] : ptr
/// </summary>
public sealed class ArrayPtrOp : MaxonOp {
	public override string Mnemonic => "array_ptr";
	public override bool HasSideEffects => false;

	public MlirValue Array => Operands[0];
	public MlirValue Index => Operands[1];
	public MlirValue Result => Results[0];

	public ArrayPtrOp(MlirValue array, MlirValue index, MlirType elementType) {
		Operands.Add(array);
		Operands.Add(index);
		CreateResult(new PtrType(elementType));
	}
}

// ============================================================================
// Function Call with Ownership
// ============================================================================

/// <summary>
/// Ownership mode for function arguments.
/// </summary>
public enum ArgumentOwnership {
	Copy,     // Pass by value (copy)
	Move,     // Transfer ownership
	Borrow,   // Immutable borrow
	BorrowMut // Mutable borrow
}

/// <summary>
/// Function call with explicit ownership: %result = maxon.call @func(%a move, %b borrow) : type
/// </summary>
public sealed class MaxonCallOp : MaxonOp {
	public override string Mnemonic => "call";
	public override bool HasSideEffects => true;

	public string Callee { get; }
	public IReadOnlyList<ArgumentOwnership> ArgOwnership { get; }
	public MlirValue? Result => Results.Count > 0 ? Results[0] : null;

	public MaxonCallOp(string callee, List<MlirValue> args, List<ArgumentOwnership> ownership, MlirType? returnType = null) {
		Callee = callee;
		ArgOwnership = ownership;
		foreach (var arg in args)
			Operands.Add(arg);
		Attributes["callee"] = new SymbolRefAttr(callee);
		if (returnType is not null)
			CreateResult(returnType);
	}

	public override void Print(MlirPrinter printer) {
		var argsStr = new List<string>();
		for (int i = 0; i < Operands.Count; i++) {
			var ownerStr = ArgOwnership.Count > i ? $" {ArgOwnership[i].ToString().ToLowerInvariant()}" : "";
			argsStr.Add($"{Operands[i]}{ownerStr}");
		}
		var resultStr = Result is not null ? $"{Result} = " : "";
		var typeStr = Result is not null ? $" : {Result.Type}" : "";
		printer.PrintLine($"{resultStr}maxon.call @{Callee}({string.Join(", ", argsStr)}){typeStr}");
	}
}

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
/// <param name="Name">Field name</param>
/// <param name="Type">Field type</param>
/// <param name="Offset">Byte offset within struct</param>
/// <param name="IsMutable">True if declared with 'var', false if 'let'</param>
/// <param name="DefaultValue">Optional default value expression</param>
public sealed record MaxonFieldInfo(string Name, MlirType Type, int Offset, bool IsMutable, Expr? DefaultValue = null);

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

// ============================================================================
// Error Union Type
// ============================================================================

/// <summary>
/// Error union type for functions that can throw.
/// Layout: { tag: i8 (0=success, 1+=error), padding: [7]i8, value: T | ErrorType }
/// </summary>
public sealed record MaxonErrorUnionType(MlirType SuccessType, MaxonEnumType ErrorType) : MlirType, IFunctionResultType {
	public override string? Dialect => "maxon";
	public override string Mnemonic => $"error_union<{SuccessType}, {ErrorType.Name}>";
	// Size = 8 (tag + padding) + max(success size, error size)
	public override int SizeInBytes => 8 + Math.Max(SuccessType.SizeInBytes, ErrorType.SizeInBytes);

	public override string ToString() => $"!maxon.error_union<{SuccessType}, {ErrorType.Name}>";
}

// ============================================================================
// Error Union Operations
// ============================================================================

/// <summary>
/// Create a success error union: %result = maxon.error_union_success %value : error_union_type
/// </summary>
public sealed class ErrorUnionSuccessOp : MaxonOp {
	public override string Mnemonic => "error_union_success";
	public override bool HasSideEffects => false;

	public MlirValue Value => Operands[0];
	public MlirValue Result => Results[0];

	public ErrorUnionSuccessOp(MlirValue value, MaxonErrorUnionType errorUnionType) {
		Operands.Add(value);
		CreateResult(errorUnionType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.error_union_success {Value} : {Result.Type}");
	}
}

/// <summary>
/// Create an error error union: %result = maxon.error_union_error %error : error_union_type
/// </summary>
public sealed class ErrorUnionErrorOp : MaxonOp {
	public override string Mnemonic => "error_union_error";
	public override bool HasSideEffects => false;

	public MlirValue Error => Operands[0];
	public MlirValue Result => Results[0];

	public ErrorUnionErrorOp(MlirValue error, MaxonErrorUnionType errorUnionType) {
		Operands.Add(error);
		CreateResult(errorUnionType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.error_union_error {Error} : {Result.Type}");
	}
}

/// <summary>
/// Check if error union holds an error: %result = maxon.error_union_is_error %union : i1
/// </summary>
public sealed class ErrorUnionIsErrorOp : MaxonOp {
	public override string Mnemonic => "error_union_is_error";
	public override bool HasSideEffects => false;

	public MlirValue Union => Operands[0];
	public MlirValue Result => Results[0];

	public ErrorUnionIsErrorOp(MlirValue union) {
		Operands.Add(union);
		CreateResult(IntegerType.I1);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.error_union_is_error {Union} : i1");
	}
}

/// <summary>
/// Extract success value from error union: %result = maxon.error_union_get_value %union : success_type
/// </summary>
public sealed class ErrorUnionGetValueOp : MaxonOp {
	public override string Mnemonic => "error_union_get_value";
	public override bool HasSideEffects => false;

	public MlirValue Union => Operands[0];
	public MlirValue Result => Results[0];

	public ErrorUnionGetValueOp(MlirValue union, MlirType successType) {
		Operands.Add(union);
		CreateResult(successType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.error_union_get_value {Union} : {Result.Type}");
	}
}

/// <summary>
/// Extract error value from error union: %result = maxon.error_union_get_error %union : error_type
/// </summary>
public sealed class ErrorUnionGetErrorOp : MaxonOp {
	public override string Mnemonic => "error_union_get_error";
	public override bool HasSideEffects => false;

	public MlirValue Union => Operands[0];
	public MlirValue Result => Results[0];

	public ErrorUnionGetErrorOp(MlirValue union, MlirType errorType) {
		Operands.Add(union);
		CreateResult(errorType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.error_union_get_error {Union} : {Result.Type}");
	}
}

// ============================================================================
// Managed Memory Operations (for Array implementation)
// ============================================================================

/// <summary>
/// Create managed memory: %result = maxon.managed_memory_create %count, %elemSize : !maxon.managed_memory
/// Allocates memory for count elements of elemSize bytes each.
/// </summary>
public sealed class ManagedMemoryCreateOp : MaxonOp {
	public override string Mnemonic => "managed_memory_create";
	public override bool HasSideEffects => true;

	public MlirValue Count => Operands[0];
	public MlirValue ElemSize => Operands[1];
	public MlirValue Result => Results[0];
	public MaxonStructType ManagedMemoryType { get; }

	public ManagedMemoryCreateOp(MlirValue count, MlirValue elemSize, MaxonStructType managedMemoryType) {
		Operands.Add(count);
		Operands.Add(elemSize);
		ManagedMemoryType = managedMemoryType;
		CreateResult(managedMemoryType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.managed_memory_create {Count}, {ElemSize}");
	}
}

/// <summary>
/// Constants for managed memory flags.
/// </summary>
public static class ManagedMemoryFlags {
	/// <summary>
	/// Flag indicating managed memory buffer points to read-only data (rdata section).
	/// When this flag is set, mutations must first copy the data to the heap.
	/// </summary>
	public const int RDATA = 1;
}

/// <summary>
/// Store constant data in rdata section: %ptr = maxon.const_data @label, data
/// Returns a pointer to the constant data in the read-only section.
/// </summary>
public sealed class ConstDataOp : MaxonOp {
	public override string Mnemonic => "const_data";
	public override bool HasSideEffects => false;

	public byte[] Data { get; }
	public string Label { get; }
	public MlirValue Result => Results[0];

	public ConstDataOp(byte[] data, string label) {
		Data = data;
		Label = label;
		CreateResult(new PtrType(IntegerType.I8));
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.const_data @{Label}, [{Data.Length} bytes]");
	}
}

/// <summary>
/// Create managed memory pointing to rdata: %result = maxon.managed_memory_create_from_rdata %ptr, %count, %elemSize
/// Creates a __ManagedMemory struct with _buffer pointing to rdata and _flags = RDATA.
/// </summary>
public sealed class ManagedMemoryCreateFromRdataOp : MaxonOp {
	public override string Mnemonic => "managed_memory_create_from_rdata";
	public override bool HasSideEffects => true;

	public MlirValue RdataPtr => Operands[0];
	public MlirValue Count => Operands[1];
	public MlirValue ElemSize => Operands[2];
	public MlirValue Result => Results[0];
	public MaxonStructType ManagedMemoryType { get; }

	public ManagedMemoryCreateFromRdataOp(MlirValue rdataPtr, MlirValue count, MlirValue elemSize, MaxonStructType managedMemoryType) {
		Operands.Add(rdataPtr);
		Operands.Add(count);
		Operands.Add(elemSize);
		ManagedMemoryType = managedMemoryType;
		CreateResult(managedMemoryType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.managed_memory_create_from_rdata {RdataPtr}, {Count}, {ElemSize}");
	}
}

/// <summary>
/// Copy-on-write: %result = maxon.managed_memory_make_unique %mem, %elemSize
/// If the memory has the RDATA flag set, copies data to heap and clears the flag.
/// Otherwise returns the same memory unchanged.
/// </summary>
public sealed class ManagedMemoryMakeUniqueOp : MaxonOp {
	public override string Mnemonic => "managed_memory_make_unique";
	public override bool HasSideEffects => true;

	public MlirValue Memory => Operands[0];
	public MlirValue ElemSize => Operands[1];
	public MlirValue Result => Results[0];

	public ManagedMemoryMakeUniqueOp(MlirValue memory, MlirValue elemSize, MaxonStructType managedMemoryType) {
		Operands.Add(memory);
		Operands.Add(elemSize);
		CreateResult(managedMemoryType);
	}

	/// <summary>
	/// Constructor that derives the result type from the memory operand.
	/// Used when inserting COW checks in lowering patterns.
	/// </summary>
	public ManagedMemoryMakeUniqueOp(MlirValue memory, MlirValue elemSize) {
		Operands.Add(memory);
		Operands.Add(elemSize);
		// The result type matches the memory operand type (pointer to struct)
		CreateResult(memory.Type);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.managed_memory_make_unique {Memory}, {ElemSize}");
	}
}

/// <summary>
/// Free managed memory: maxon.managed_memory_free %mem
/// Checks the RDATA flag before freeing. If RDATA is set, skips the HeapFree.
/// </summary>
public sealed class ManagedMemoryFreeOp : MaxonOp {
	public override string Mnemonic => "managed_memory_free";
	public override bool HasSideEffects => true;

	public MlirValue Memory => Operands[0];

	public ManagedMemoryFreeOp(MlirValue memory) {
		Operands.Add(memory);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.managed_memory_free {Memory}");
	}
}

/// <summary>
/// Get managed memory length: %result = maxon.managed_memory_len %mem : i64
/// </summary>
public sealed class ManagedMemoryLenOp : MaxonOp {
	public override string Mnemonic => "managed_memory_len";
	public override bool HasSideEffects => false;

	public MlirValue Memory => Operands[0];
	public MlirValue Result => Results[0];

	public ManagedMemoryLenOp(MlirValue memory) {
		Operands.Add(memory);
		CreateResult(IntegerType.I64);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.managed_memory_len {Memory}");
	}
}

/// <summary>
/// Get managed memory capacity: %result = maxon.managed_memory_capacity %mem : i64
/// </summary>
public sealed class ManagedMemoryCapacityOp : MaxonOp {
	public override string Mnemonic => "managed_memory_capacity";
	public override bool HasSideEffects => false;

	public MlirValue Memory => Operands[0];
	public MlirValue Result => Results[0];

	public ManagedMemoryCapacityOp(MlirValue memory) {
		Operands.Add(memory);
		CreateResult(IntegerType.I64);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.managed_memory_capacity {Memory}");
	}
}

/// <summary>
/// Get element from managed memory (unchecked): %result = maxon.managed_memory_get_unchecked %mem, %index : element_type
/// </summary>
public sealed class ManagedMemoryGetUncheckedOp : MaxonOp {
	public override string Mnemonic => "managed_memory_get_unchecked";
	public override bool HasSideEffects => false;

	public MlirValue Memory => Operands[0];
	public MlirValue Index => Operands[1];
	public MlirType ElementType { get; }
	public MlirValue Result => Results[0];

	public ManagedMemoryGetUncheckedOp(MlirValue memory, MlirValue index, MlirType elementType) {
		Operands.Add(memory);
		Operands.Add(index);
		ElementType = elementType;
		CreateResult(elementType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.managed_memory_get_unchecked {Memory}, {Index} : {ElementType}");
	}
}

/// <summary>
/// Set element in managed memory: maxon.managed_memory_set_at %mem, %index, %value
/// </summary>
public sealed class ManagedMemorySetAtOp : MaxonOp {
	public override string Mnemonic => "managed_memory_set_at";
	public override bool HasSideEffects => true;

	public MlirValue Memory => Operands[0];
	public MlirValue Index => Operands[1];
	public MlirValue Value => Operands[2];

	public ManagedMemorySetAtOp(MlirValue memory, MlirValue index, MlirValue value) {
		Operands.Add(memory);
		Operands.Add(index);
		Operands.Add(value);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.managed_memory_set_at {Memory}, {Index}, {Value}");
	}
}

/// <summary>
/// Grow managed memory capacity: maxon.managed_memory_grow %mem, %newCapacity
/// </summary>
public sealed class ManagedMemoryGrowOp : MaxonOp {
	public override string Mnemonic => "managed_memory_grow";
	public override bool HasSideEffects => true;

	public MlirValue Memory => Operands[0];
	public MlirValue NewCapacity => Operands[1];
	public MlirType ElementType { get; }

	public ManagedMemoryGrowOp(MlirValue memory, MlirValue newCapacity, MlirType elementType) {
		Operands.Add(memory);
		Operands.Add(newCapacity);
		ElementType = elementType;
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.managed_memory_grow {Memory}, {NewCapacity}");
	}
}

/// <summary>
/// Set managed memory length: maxon.managed_memory_set_length %mem, %newLen
/// </summary>
public sealed class ManagedMemorySetLengthOp : MaxonOp {
	public override string Mnemonic => "managed_memory_set_length";
	public override bool HasSideEffects => true;

	public MlirValue Memory => Operands[0];
	public MlirValue NewLength => Operands[1];

	public ManagedMemorySetLengthOp(MlirValue memory, MlirValue newLength) {
		Operands.Add(memory);
		Operands.Add(newLength);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.managed_memory_set_length {Memory}, {NewLength}");
	}
}

/// <summary>
/// Grow managed memory (ByRef version - takes a pointer): maxon.managed_memory_grow_byref %memPtr, %newCap
/// The pointer allows modifications to persist to the original struct.
/// </summary>
public sealed class ManagedMemoryGrowByRefOp : MaxonOp {
	public override string Mnemonic => "managed_memory_grow_byref";
	public override bool HasSideEffects => true;

	public MlirValue MemoryPtr => Operands[0];  // Pointer to __ManagedMemory
	public MlirValue NewCapacity => Operands[1];
	public MlirType ElementType { get; }

	public ManagedMemoryGrowByRefOp(MlirValue memoryPtr, MlirValue newCapacity, MlirType elementType) {
		Operands.Add(memoryPtr);
		Operands.Add(newCapacity);
		ElementType = elementType;
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.managed_memory_grow_byref {MemoryPtr}, {NewCapacity}");
	}
}

/// <summary>
/// Set managed memory length (ByRef version - takes a pointer): maxon.managed_memory_set_length_byref %memPtr, %newLen
/// </summary>
public sealed class ManagedMemorySetLengthByRefOp : MaxonOp {
	public override string Mnemonic => "managed_memory_set_length_byref";
	public override bool HasSideEffects => true;

	public MlirValue MemoryPtr => Operands[0];  // Pointer to __ManagedMemory
	public MlirValue NewLength => Operands[1];

	public ManagedMemorySetLengthByRefOp(MlirValue memoryPtr, MlirValue newLength) {
		Operands.Add(memoryPtr);
		Operands.Add(newLength);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.managed_memory_set_length_byref {MemoryPtr}, {NewLength}");
	}
}

/// <summary>
/// Shift elements right: maxon.managed_memory_shift_right %mem, %startIndex, %count
/// </summary>
public sealed class ManagedMemoryShiftRightOp : MaxonOp {
	public override string Mnemonic => "managed_memory_shift_right";
	public override bool HasSideEffects => true;

	public MlirValue Memory => Operands[0];
	public MlirValue StartIndex => Operands[1];
	public MlirValue Count => Operands[2];
	public MlirType ElementType { get; }

	public ManagedMemoryShiftRightOp(MlirValue memory, MlirValue startIndex, MlirValue count, MlirType elementType) {
		Operands.Add(memory);
		Operands.Add(startIndex);
		Operands.Add(count);
		ElementType = elementType;
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.managed_memory_shift_right {Memory}, {StartIndex}, {Count}");
	}
}

/// <summary>
/// Shift elements left: maxon.managed_memory_shift_left %mem, %startIndex, %count
/// </summary>
public sealed class ManagedMemoryShiftLeftOp : MaxonOp {
	public override string Mnemonic => "managed_memory_shift_left";
	public override bool HasSideEffects => true;

	public MlirValue Memory => Operands[0];
	public MlirValue StartIndex => Operands[1];
	public MlirValue Count => Operands[2];
	public MlirType ElementType { get; }

	public ManagedMemoryShiftLeftOp(MlirValue memory, MlirValue startIndex, MlirValue count, MlirType elementType) {
		Operands.Add(memory);
		Operands.Add(startIndex);
		Operands.Add(count);
		ElementType = elementType;
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"maxon.managed_memory_shift_left {Memory}, {StartIndex}, {Count}");
	}
}

/// <summary>
/// Get element size for a type: %result = maxon.element_size : i64
/// Returns the size in bytes of the element type.
/// </summary>
public sealed class ElementSizeOp : MaxonOp {
	public override string Mnemonic => "element_size";
	public override bool HasSideEffects => false;

	public MlirType ElementType { get; }
	public MlirValue Result => Results[0];

	public ElementSizeOp(MlirType elementType) {
		ElementType = elementType;
		CreateResult(IntegerType.I64);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = maxon.element_size<{ElementType}>");
	}
}
