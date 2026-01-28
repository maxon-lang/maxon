using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Converts Maxon dialect operations to standard dialects (Arith, MemRef, Func, Cf).
/// </summary>
public static class MaxonToStandardPatterns {
	/// <summary>
	/// Populates the pattern set with all Maxon→Standard patterns.
	/// </summary>
	public static void PopulatePatterns(ConversionPatternSet patterns) {
		patterns.Add<LowerMoveOp>();
		patterns.Add<LowerBorrowOp>();
		patterns.Add<LowerDropOp>();
		patterns.Add<LowerStructInitOp>();
		patterns.Add<LowerFieldGetOp>();
		patterns.Add<LowerFieldSetOp>();
		patterns.Add<LowerArrayNewOp>();
		patterns.Add<LowerArrayGetOp>();
		patterns.Add<LowerArraySetOp>();
		patterns.Add<LowerArrayLenOp>();
		patterns.Add<LowerMaxonCallOp>();
		// Enum patterns
		patterns.Add<LowerEnumInitOp>();
		// Error union patterns
		patterns.Add<LowerErrorUnionSuccessOp>();
		patterns.Add<LowerErrorUnionErrorOp>();
		patterns.Add<LowerErrorUnionIsErrorOp>();
		patterns.Add<LowerErrorUnionGetValueOp>();
		patterns.Add<LowerErrorUnionGetErrorOp>();
		// Managed memory patterns (for Array implementation)
		patterns.Add<LowerManagedMemoryCreateOp>();
		patterns.Add<PassThroughConstDataOp>();
		patterns.Add<LowerManagedMemoryCreateFromRdataOp>();
		patterns.Add<PassThroughManagedMemoryMakeUniqueOp>();
		patterns.Add<PassThroughManagedMemoryFreeOp>();
		patterns.Add<LowerManagedMemoryLenOp>();
		patterns.Add<LowerManagedMemoryCapacityOp>();
		patterns.Add<LowerManagedMemoryGetUncheckedOp>();
		patterns.Add<LowerManagedMemorySetAtOp>();
		patterns.Add<LowerManagedMemoryGrowOp>();
		patterns.Add<LowerManagedMemorySetLengthOp>();
		patterns.Add<LowerManagedMemoryShiftLeftOp>();
		patterns.Add<LowerManagedMemoryGrowByRefOp>();
		patterns.Add<LowerManagedMemorySetLengthByRefOp>();
		patterns.Add<LowerElementSizeOp>();
		// Note: FieldPtrOp is lowered by StandardToX86Patterns, not here
	}
}

/// <summary>
/// Lowers maxon.move to a no-op (value is passed through).
/// After borrow checking, move semantics are already verified.
/// </summary>
public sealed class LowerMoveOp : ConversionPattern<MoveOp> {
	protected override bool MatchAndRewrite(MoveOp op, ConversionPatternRewriter rewriter) {
		// Move is erased after borrow checking - the value is used directly
		rewriter.ReplaceOpWithValue(op, op.Source);
		return true;
	}
}

/// <summary>
/// Lowers maxon.borrow to a no-op (reference is passed through).
/// </summary>
public sealed class LowerBorrowOp : ConversionPattern<BorrowOp> {
	protected override bool MatchAndRewrite(BorrowOp op, ConversionPatternRewriter rewriter) {
		// Borrow is erased - the value/reference is used directly
		rewriter.ReplaceOpWithValue(op, op.Source);
		return true;
	}
}

/// <summary>
/// Lowers maxon.drop to memref.dealloc for managed types.
/// </summary>
public sealed class LowerDropOp : ConversionPattern<DropOp> {
	protected override bool MatchAndRewrite(DropOp op, ConversionPatternRewriter rewriter) {
		var valueType = op.Value.Type;

		// Unwrap owned type if present
		if (valueType is OwnedType owned)
			valueType = owned.Inner;

		// For managed array types, emit dealloc
		if (valueType is MaxonArrayType) {
			var dealloc = new DeallocOp(op.Value);
			rewriter.Insert(dealloc);
		}
		// For structs with managed fields, would recursively drop
		// For primitives, drop is a no-op

		return true;
	}
}

/// <summary>
/// Lowers maxon.struct_init to alloca + stores.
/// Uses StoreSemantics to ensure correct store/memcpy choice for struct vs primitive fields.
/// </summary>
public sealed class LowerStructInitOp : ConversionPattern<StructInitOp> {
	protected override bool MatchAndRewrite(StructInitOp op, ConversionPatternRewriter rewriter) {
		// Allocate space for the struct
		var memRefType = new MemRefType(IntegerType.I8, [op.StructType.SizeInBytes]);
		var alloca = new AllocaOp(memRefType);
		rewriter.Insert(alloca);

		// Store each field
		int offset = 0;
		foreach (var field in op.StructType.Fields) {
			if (op.FieldValues.TryGetValue(field.Name, out var value)) {
				// Calculate destination address for this field
				var fieldPtr = new FieldPtrOp(alloca.Result, field.Name, offset, field.Type);
				rewriter.Insert(fieldPtr);

				// Use StoreSemantics to determine store vs memcpy
				var storeAction = StoreSemantics.DetermineStoreAction(value, field.Type);
				StoreSemantics.EmitStore(storeAction, fieldPtr.Result, rewriter);
			}
			offset += field.Type.SizeInBytes;
		}

		rewriter.ReplaceOpWithValue(op, alloca.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.field_get to field_ptr + load (for primitives) or just field_ptr (for structs).
/// Uses ValueSemantics.Load to ensure correct handling of struct vs primitive fields.
/// </summary>
public sealed class LowerFieldGetOp : ConversionPattern<FieldGetOp> {
	protected override bool MatchAndRewrite(FieldGetOp op, ConversionPatternRewriter rewriter) {
		var fieldPtr = EmitFieldPtr(op.Struct, op.FieldName, rewriter);
		var result = ValueSemantics.Load(fieldPtr.Result, op.Result.Type, rewriter);
		rewriter.ReplaceOpWithValue(op, result.Unwrap());
		return true;
	}

	internal static FieldPtrOp EmitFieldPtr(MlirValue structValue, string fieldName, ConversionPatternRewriter rewriter) {
		var structType = GetStructType(structValue.Type)
			?? throw new InvalidOperationException($"Expected struct type, got {structValue.Type}");
		var offset = structType.GetFieldOffset(fieldName);
		var field = structType.GetField(fieldName)
			?? throw new InvalidOperationException($"Field '{fieldName}' not found in struct '{structType.Name}'");

		var fieldPtr = new FieldPtrOp(structValue, fieldName, offset, field.Type);
		rewriter.Insert(fieldPtr);
		return fieldPtr;
	}

	private static MaxonStructType? GetStructType(MlirType type) => type switch {
		MaxonStructType st => st,
		MemRefType mrt when mrt.ElementType is MaxonStructType st => st,
		PtrType pt when pt.ElementType is MaxonStructType st => st,
		_ => null
	};
}

/// <summary>
/// Lowers maxon.field_set to field_ptr + store.
/// </summary>
public sealed class LowerFieldSetOp : ConversionPattern<FieldSetOp> {
	protected override bool MatchAndRewrite(FieldSetOp op, ConversionPatternRewriter rewriter) {
		var fieldPtr = LowerFieldGetOp.EmitFieldPtr(op.Struct, op.FieldName, rewriter);
		if (fieldPtr.Result.Type is not PtrType ptrType || ptrType.ElementType is null) {
			throw new InvalidOperationException("Field pointer must have an element type");
		}
		var storeAction = StoreSemantics.DetermineStoreAction(op.Value, ptrType.ElementType);
		StoreSemantics.EmitStore(storeAction, fieldPtr.Result, rewriter);
		return true;
	}
}

/// <summary>
/// Lowers maxon.array_new to heap allocation.
/// </summary>
public sealed class LowerArrayNewOp : ConversionPattern<ArrayNewOp> {
	protected override bool MatchAndRewrite(ArrayNewOp op, ConversionPatternRewriter rewriter) {
		// Allocate array on heap
		var alloc = new AllocOp(op.ArrayType.ElementType, op.Size);
		rewriter.Insert(alloc);
		rewriter.ReplaceOpWithValue(op, alloc.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.array_get to load (for primitives) or element pointer (for structs).
/// Uses ValueSemantics.Load to ensure correct handling of struct vs primitive elements.
/// </summary>
public sealed class LowerArrayGetOp : ConversionPattern<ArrayGetOp> {
	protected override bool MatchAndRewrite(ArrayGetOp op, ConversionPatternRewriter rewriter) {
		// Get pointer to the array element
		var elemPtr = new ArrayPtrOp(op.Array, op.Index, op.Result.Type);
		rewriter.Insert(elemPtr);

		// Use ValueSemantics to decide: return pointer for structs, load for primitives
		var result = ValueSemantics.Load(elemPtr.Result, op.Result.Type, rewriter);
		rewriter.ReplaceOpWithValue(op, result.Unwrap());
		return true;
	}
}

/// <summary>
/// Lowers maxon.array_set to memref.store.
/// </summary>
public sealed class LowerArraySetOp : ConversionPattern<ArraySetOp> {
	protected override bool MatchAndRewrite(ArraySetOp op, ConversionPatternRewriter rewriter) {
		var store = new StoreOp(op.Value, op.Array, op.Index);
		rewriter.Insert(store);
		return true;
	}
}

/// <summary>
/// Lowers maxon.array_len to a load from array header.
/// </summary>
public sealed class LowerArrayLenOp : ConversionPattern<ArrayLenOp> {
	protected override bool MatchAndRewrite(ArrayLenOp op, ConversionPatternRewriter rewriter) {
		// Array length is stored at offset -8 from data pointer
		// This is a simplified implementation
		var load = new LoadOp(op.Array);
		rewriter.Insert(load);
		rewriter.ReplaceOpWithValue(op, load.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.call to func.call (strips ownership annotations).
/// </summary>
public sealed class LowerMaxonCallOp : ConversionPattern<MaxonCallOp> {
	protected override bool MatchAndRewrite(MaxonCallOp op, ConversionPatternRewriter rewriter) {
		// Check if return type is a struct (needs caller-allocated return space)
		if (op.Result?.Type is MaxonStructType structType) {
			// Allocate space for the return struct on caller's stack
			var memRefType = new MemRefType(IntegerType.I8, [structType.SizeInBytes]);
			var alloca = new AllocaOp(memRefType);
			rewriter.Insert(alloca);

			// Pass the return pointer as a hidden first argument
			var args = new List<MlirValue> { alloca.Result };
			args.AddRange(op.Operands);

			// Call with void return (struct is written to the pointer)
			var call = new FuncCallOp(op.Callee, args, null);
			rewriter.Insert(call);

			// Result is the alloca pointer (now points to the filled struct)
			rewriter.ReplaceOpWithValue(op, alloca.Result);
			return true;
		}

		// Check if return type is an error union (also needs caller-allocated return space)
		if (op.Result?.Type is MaxonErrorUnionType errorUnionType) {
			// Allocate space for the error union on caller's stack
			var memRefType = new MemRefType(IntegerType.I8, [errorUnionType.SizeInBytes]);
			var alloca = new AllocaOp(memRefType);
			rewriter.Insert(alloca);

			// Pass the return pointer as a hidden first argument
			var args = new List<MlirValue> { alloca.Result };
			args.AddRange(op.Operands);

			// Call with void return (error union is written to the pointer)
			var call = new FuncCallOp(op.Callee, args, null);
			rewriter.Insert(call);

			// Result is the alloca pointer (now points to the filled error union)
			rewriter.ReplaceOpWithValue(op, alloca.Result);
			return true;
		}

		// Non-struct return: standard call
		var standardCall = new FuncCallOp(op.Callee, op.Operands, op.Result?.Type);
		rewriter.Insert(standardCall);
		if (op.Result is not null && standardCall.Result is not null) {
			rewriter.ReplaceOpWithValue(op, standardCall.Result);
		}
		return true;
	}
}

// ============================================================================
// Error Union Lowering Patterns
// ============================================================================

/// <summary>
/// Lowers maxon.error_union_success to alloca + stores (tag=0, value).
/// </summary>
public sealed class LowerErrorUnionSuccessOp : ConversionPattern<ErrorUnionSuccessOp> {
	protected override bool MatchAndRewrite(ErrorUnionSuccessOp op, ConversionPatternRewriter rewriter) {
		var errorUnionType = (MaxonErrorUnionType)op.Result.Type;

		// Allocate space for the error union
		var memRefType = new MemRefType(IntegerType.I8, [errorUnionType.SizeInBytes]);
		var alloca = new AllocaOp(memRefType);
		rewriter.Insert(alloca);

		// Store tag = 0 (success) at offset 0
		var tagConst = ConstantOp.Int(0, IntegerType.I8);
		rewriter.Insert(tagConst);
		var tagStore = new StoreOp(tagConst.Result, alloca.Result);
		rewriter.Insert(tagStore);

		// Store value at offset 8 (after tag + padding)
		var valueOffsetConst = ConstantOp.Index(8);
		rewriter.Insert(valueOffsetConst);
		var valueStore = new StoreOp(op.Value, alloca.Result, valueOffsetConst.Result);
		rewriter.Insert(valueStore);

		rewriter.ReplaceOpWithValue(op, alloca.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.error_union_error to alloca + stores (tag=1, error value).
/// </summary>
public sealed class LowerErrorUnionErrorOp : ConversionPattern<ErrorUnionErrorOp> {
	protected override bool MatchAndRewrite(ErrorUnionErrorOp op, ConversionPatternRewriter rewriter) {
		var errorUnionType = (MaxonErrorUnionType)op.Result.Type;

		// Allocate space for the error union
		var memRefType = new MemRefType(IntegerType.I8, [errorUnionType.SizeInBytes]);
		var alloca = new AllocaOp(memRefType);
		rewriter.Insert(alloca);

		// Store tag = 1 (error) at offset 0
		var tagConst = ConstantOp.Int(1, IntegerType.I8);
		rewriter.Insert(tagConst);
		var tagStore = new StoreOp(tagConst.Result, alloca.Result);
		rewriter.Insert(tagStore);

		// Store error value at offset 8 (after tag + padding)
		var errorOffsetConst = ConstantOp.Index(8);
		rewriter.Insert(errorOffsetConst);
		var errorStore = new StoreOp(op.Error, alloca.Result, errorOffsetConst.Result);
		rewriter.Insert(errorStore);

		rewriter.ReplaceOpWithValue(op, alloca.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.error_union_is_error to load tag and compare != 0.
/// </summary>
public sealed class LowerErrorUnionIsErrorOp : ConversionPattern<ErrorUnionIsErrorOp> {
	protected override bool MatchAndRewrite(ErrorUnionIsErrorOp op, ConversionPatternRewriter rewriter) {
		// The union is a pointer (MemRefType or PtrType) to the error union data
		// After LowerMaxonCallOp runs, it will be a MemRefType<i8 x size>
		// The tag is at offset 0 (first byte)

		// Get the remapped value (in case it was replaced by LowerMaxonCallOp)
		var unionPtr = rewriter.GetRemapped(op.Union);

		// Create a ptr to load the tag byte
		var tagPtr = new FieldPtrOp(unionPtr, "_tag", 0, IntegerType.I8);
		rewriter.Insert(tagPtr);

		var load = new LoadOp(tagPtr.Result);
		rewriter.Insert(load);

		// Extend to i64 for comparison
		var extended = new ExtUIOp(load.Result, IntegerType.I64);
		rewriter.Insert(extended);

		// Compare tag != 0 (non-zero means error)
		var zeroConst = ConstantOp.Int(0, IntegerType.I64);
		rewriter.Insert(zeroConst);
		var cmp = new CmpIOp(CmpIPredicate.Ne, extended.Result, zeroConst.Result);
		rewriter.Insert(cmp);

		rewriter.ReplaceOpWithValue(op, cmp.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.error_union_get_value to access the value at offset 8.
/// Uses ValueSemantics.Load to ensure correct handling of struct vs primitive values.
/// </summary>
public sealed class LowerErrorUnionGetValueOp : ConversionPattern<ErrorUnionGetValueOp> {
	protected override bool MatchAndRewrite(ErrorUnionGetValueOp op, ConversionPatternRewriter rewriter) {
		// Get the remapped value (in case it was replaced by LowerMaxonCallOp)
		var unionPtr = rewriter.GetRemapped(op.Union);

		// Get pointer to value from the error union at offset 8
		var valueOffset = 8;
		var fieldPtr = new FieldPtrOp(unionPtr, "_value", valueOffset, op.Result.Type);
		rewriter.Insert(fieldPtr);

		// Use ValueSemantics to decide: return pointer for structs, load for primitives
		var result = ValueSemantics.Load(fieldPtr.Result, op.Result.Type, rewriter);
		rewriter.ReplaceOpWithValue(op, result.Unwrap());
		return true;
	}
}

/// <summary>
/// Lowers maxon.error_union_get_error to access the error at offset 8.
/// Uses ValueSemantics.Load to ensure correct handling of struct vs primitive errors.
/// </summary>
public sealed class LowerErrorUnionGetErrorOp : ConversionPattern<ErrorUnionGetErrorOp> {
	protected override bool MatchAndRewrite(ErrorUnionGetErrorOp op, ConversionPatternRewriter rewriter) {
		// Get the remapped value (in case it was replaced by LowerMaxonCallOp)
		var unionPtr = rewriter.GetRemapped(op.Union);

		// Get pointer to error from the error union at offset 8 (same offset as value)
		var errorOffset = 8;
		var fieldPtr = new FieldPtrOp(unionPtr, "_error", errorOffset, op.Result.Type);
		rewriter.Insert(fieldPtr);

		// Use ValueSemantics to decide: return pointer for structs, load for primitives
		var result = ValueSemantics.Load(fieldPtr.Result, op.Result.Type, rewriter);
		rewriter.ReplaceOpWithValue(op, result.Unwrap());
		return true;
	}
}

// ============================================================================
// Enum Lowering Patterns
// ============================================================================

/// <summary>
/// Lowers maxon.enum_init to a constant integer (the tag value).
/// For enums without payload, the enum value is just the discriminant.
/// For enums with payload, would need to allocate space for tag + payload.
/// </summary>
public sealed class LowerEnumInitOp : ConversionPattern<EnumInitOp> {
	protected override bool MatchAndRewrite(EnumInitOp op, ConversionPatternRewriter rewriter) {
		var variant = op.EnumType.GetVariant(op.VariantName) ?? throw new InvalidOperationException($"Unknown enum variant: {op.EnumType.Name}::{op.VariantName}");

		// For simple enums without payload, the value is just the tag
		if (variant.PayloadFields.Count == 0) {
			var tagConst = ConstantOp.Int(variant.Tag, IntegerType.I8);
			rewriter.Insert(tagConst);
			rewriter.ReplaceOpWithValue(op, tagConst.Result);
			return true;
		}

		// TODO: For enums with payload, allocate space for tag + payload
		throw new NotImplementedException($"Enum variants with payload not yet supported: {op.EnumType.Name}::{op.VariantName}");
	}
}

// ============================================================================
// Managed Memory Lowering Patterns (for Array implementation)
// ============================================================================

/// <summary>
/// Lowers maxon.managed_memory_create to heap allocation.
/// Creates the __ManagedMemory struct with allocated buffer.
/// </summary>
public sealed class LowerManagedMemoryCreateOp : ConversionPattern<ManagedMemoryCreateOp> {
	protected override bool MatchAndRewrite(ManagedMemoryCreateOp op, ConversionPatternRewriter rewriter) {
		// __ManagedMemory layout:
		//   _buffer: ptr (offset 0, 8 bytes)
		//   _len: i64 (offset 8, 8 bytes)
		//   _capacity: i64 (offset 16, 8 bytes)
		//   _flags: i32 (offset 24, 4 bytes)
		//   _parent_off: i32 (offset 28, 4 bytes)

		// Calculate total bytes needed: count * elemSize
		var totalBytes = new MulIOp(op.Count, op.ElemSize);
		rewriter.Insert(totalBytes);

		// Allocate buffer on heap (will be lowered to malloc call later)
		var heapAlloc = new HeapAllocOp(totalBytes.Result);
		rewriter.Insert(heapAlloc);

		// Allocate space for the struct (32 bytes)
		var structSize = ConstantOp.Int(32, IntegerType.I64);
		rewriter.Insert(structSize);
		var memRefType = new MemRefType(IntegerType.I8, [32]);
		var structAlloca = new AllocaOp(memRefType);
		rewriter.Insert(structAlloca);

		// Store _buffer at offset 0
		var offset0 = ConstantOp.Index(0);
		rewriter.Insert(offset0);
		var bufferPtr = new FieldPtrOp(structAlloca.Result, "_buffer", 0, new PtrType(IntegerType.I8));
		rewriter.Insert(bufferPtr);
		var storeBuffer = new StoreOp(heapAlloc.Result, bufferPtr.Result);
		rewriter.Insert(storeBuffer);

		// Store _len at offset 8 (initially 0)
		var zero = ConstantOp.Int(0, IntegerType.I64);
		rewriter.Insert(zero);
		var lenPtr = new FieldPtrOp(structAlloca.Result, "_len", 8, IntegerType.I64);
		rewriter.Insert(lenPtr);
		var storeLen = new StoreOp(zero.Result, lenPtr.Result);
		rewriter.Insert(storeLen);

		// Store _capacity at offset 16
		var capPtr = new FieldPtrOp(structAlloca.Result, "_capacity", 16, IntegerType.I64);
		rewriter.Insert(capPtr);
		var storeCap = new StoreOp(op.Count, capPtr.Result);
		rewriter.Insert(storeCap);

		// Store _flags at offset 24 (initially 0)
		var zeroFlags = ConstantOp.Int(0, IntegerType.I32);
		rewriter.Insert(zeroFlags);
		var flagsPtr = new FieldPtrOp(structAlloca.Result, "_flags", 24, IntegerType.I32);
		rewriter.Insert(flagsPtr);
		var storeFlags = new StoreOp(zeroFlags.Result, flagsPtr.Result);
		rewriter.Insert(storeFlags);

		// Store _parent_off at offset 28 (initially 0)
		var zeroParent = ConstantOp.Int(0, IntegerType.I32);
		rewriter.Insert(zeroParent);
		var parentOffPtr = new FieldPtrOp(structAlloca.Result, "_parent_off", 28, IntegerType.I32);
		rewriter.Insert(parentOffPtr);
		var storeParent = new StoreOp(zeroParent.Result, parentOffPtr.Result);
		rewriter.Insert(storeParent);

		rewriter.ReplaceOpWithValue(op, structAlloca.Result);
		return true;
	}
}

/// <summary>
/// Passes maxon.const_data through to X86 lowering (handled by StandardToX86).
/// Returns false to indicate no transformation at this stage.
/// </summary>
public sealed class PassThroughConstDataOp : ConversionPattern<ConstDataOp> {
	protected override bool MatchAndRewrite(ConstDataOp op, ConversionPatternRewriter rewriter) {
		// This operation passes through to StandardToX86 where it becomes LeaRdataOp
		// We don't transform it here, just keep it as-is
		return false;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_create_from_rdata to create __ManagedMemory pointing to rdata.
/// Sets _flags = ManagedMemoryFlags.RDATA to indicate copy-on-write needed for mutation.
/// </summary>
public sealed class LowerManagedMemoryCreateFromRdataOp : ConversionPattern<ManagedMemoryCreateFromRdataOp> {
	protected override bool MatchAndRewrite(ManagedMemoryCreateFromRdataOp op, ConversionPatternRewriter rewriter) {
		// __ManagedMemory layout:
		//   _buffer: ptr (offset 0, 8 bytes)
		//   _len: i64 (offset 8, 8 bytes)
		//   _capacity: i64 (offset 16, 8 bytes)
		//   _flags: i32 (offset 24, 4 bytes)
		//   _parent_off: i32 (offset 28, 4 bytes)

		// Allocate space for the struct (32 bytes)
		var memRefType = new MemRefType(IntegerType.I8, [32]);
		var structAlloca = new AllocaOp(memRefType);
		rewriter.Insert(structAlloca);

		// Store _buffer at offset 0 (points to rdata)
		var bufferPtr = new FieldPtrOp(structAlloca.Result, "_buffer", 0, new PtrType(IntegerType.I8));
		rewriter.Insert(bufferPtr);
		var storeBuffer = new StoreOp(op.RdataPtr, bufferPtr.Result);
		rewriter.Insert(storeBuffer);

		// Store _len at offset 8
		var lenPtr = new FieldPtrOp(structAlloca.Result, "_len", 8, IntegerType.I64);
		rewriter.Insert(lenPtr);
		var storeLen = new StoreOp(op.Count, lenPtr.Result);
		rewriter.Insert(storeLen);

		// Store _capacity at offset 16 (same as count for rdata)
		var capPtr = new FieldPtrOp(structAlloca.Result, "_capacity", 16, IntegerType.I64);
		rewriter.Insert(capPtr);
		var storeCap = new StoreOp(op.Count, capPtr.Result);
		rewriter.Insert(storeCap);

		// Store _flags at offset 24 (set RDATA flag)
		var rdataFlag = ConstantOp.Int(ManagedMemoryFlags.RDATA, IntegerType.I32);
		rewriter.Insert(rdataFlag);
		var flagsPtr = new FieldPtrOp(structAlloca.Result, "_flags", 24, IntegerType.I32);
		rewriter.Insert(flagsPtr);
		var storeFlags = new StoreOp(rdataFlag.Result, flagsPtr.Result);
		rewriter.Insert(storeFlags);

		// Store _parent_off at offset 28 (initially 0)
		var zeroParent = ConstantOp.Int(0, IntegerType.I32);
		rewriter.Insert(zeroParent);
		var parentOffPtr = new FieldPtrOp(structAlloca.Result, "_parent_off", 28, IntegerType.I32);
		rewriter.Insert(parentOffPtr);
		var storeParent = new StoreOp(zeroParent.Result, parentOffPtr.Result);
		rewriter.Insert(storeParent);

		rewriter.ReplaceOpWithValue(op, structAlloca.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_make_unique to a no-op (pass-through).
/// The actual COW logic is implemented in StandardToX86Patterns with inline jumps.
/// </summary>
public sealed class PassThroughManagedMemoryMakeUniqueOp : ConversionPattern<ManagedMemoryMakeUniqueOp> {
	protected override bool MatchAndRewrite(ManagedMemoryMakeUniqueOp op, ConversionPatternRewriter rewriter) {
		// This operation passes through to StandardToX86 where COW is implemented
		return false;
	}
}

/// <summary>
/// Passes through ManagedMemoryFreeOp to StandardToX86Patterns.
/// The actual rdata check is implemented there with inline jumps.
/// </summary>
public sealed class PassThroughManagedMemoryFreeOp : ConversionPattern<ManagedMemoryFreeOp> {
	protected override bool MatchAndRewrite(ManagedMemoryFreeOp op, ConversionPatternRewriter rewriter) {
		// This operation passes through to StandardToX86 where rdata check is implemented
		return false;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_len to field load from offset 8.
/// </summary>
public sealed class LowerManagedMemoryLenOp : ConversionPattern<ManagedMemoryLenOp> {
	protected override bool MatchAndRewrite(ManagedMemoryLenOp op, ConversionPatternRewriter rewriter) {
		var lenPtr = new FieldPtrOp(op.Memory, "_len", 8, IntegerType.I64);
		rewriter.Insert(lenPtr);
		var load = new LoadOp(lenPtr.Result);
		rewriter.Insert(load);
		rewriter.ReplaceOpWithValue(op, load.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_capacity to field load from offset 16.
/// </summary>
public sealed class LowerManagedMemoryCapacityOp : ConversionPattern<ManagedMemoryCapacityOp> {
	protected override bool MatchAndRewrite(ManagedMemoryCapacityOp op, ConversionPatternRewriter rewriter) {
		var capPtr = new FieldPtrOp(op.Memory, "_capacity", 16, IntegerType.I64);
		rewriter.Insert(capPtr);
		var load = new LoadOp(capPtr.Result);
		rewriter.Insert(load);
		rewriter.ReplaceOpWithValue(op, load.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_get_unchecked to buffer[index * elemSize] access.
/// Uses ValueSemantics.Load to ensure correct handling of struct vs primitive elements.
/// </summary>
public sealed class LowerManagedMemoryGetUncheckedOp : ConversionPattern<ManagedMemoryGetUncheckedOp> {
	protected override bool MatchAndRewrite(ManagedMemoryGetUncheckedOp op, ConversionPatternRewriter rewriter) {
		// Load buffer pointer
		var bufferPtr = new FieldPtrOp(op.Memory, "_buffer", 0, new PtrType(IntegerType.I8));
		rewriter.Insert(bufferPtr);
		var bufferLoad = new LoadOp(bufferPtr.Result);
		rewriter.Insert(bufferLoad);

		// Calculate offset: index * elementSize
		var elemSize = ConstantOp.Int(op.ElementType.SizeInBytes, IntegerType.I64);
		rewriter.Insert(elemSize);
		var offset = new MulIOp(op.Index, elemSize.Result);
		rewriter.Insert(offset);

		// Get element pointer: buffer + offset
		var elemPtr = new PtrAddOp(bufferLoad.Result, offset.Result, new PtrType(op.ElementType));
		rewriter.Insert(elemPtr);

		// Use ValueSemantics to decide: return pointer for structs, load for primitives
		var result = ValueSemantics.Load(elemPtr.Result, op.ElementType, rewriter);
		rewriter.ReplaceOpWithValue(op, result.Unwrap());
		return true;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_set_at to buffer[index * elemSize] store.
/// First ensures COW by inserting make_unique.
/// </summary>
public sealed class LowerManagedMemorySetAtOp : ConversionPattern<ManagedMemorySetAtOp> {
	protected override bool MatchAndRewrite(ManagedMemorySetAtOp op, ConversionPatternRewriter rewriter) {
		// First, ensure the memory is unique (COW check)
		var elemSizeForCow = ConstantOp.Int(op.Value.Type.SizeInBytes, IntegerType.I64);
		rewriter.Insert(elemSizeForCow);
		var makeUnique = new ManagedMemoryMakeUniqueOp(op.Memory, elemSizeForCow.Result);
		rewriter.Insert(makeUnique);
		var memory = makeUnique.Result;

		// Load buffer pointer
		var bufferPtr = new FieldPtrOp(memory, "_buffer", 0, new PtrType(IntegerType.I8));
		rewriter.Insert(bufferPtr);
		var bufferLoad = new LoadOp(bufferPtr.Result);
		rewriter.Insert(bufferLoad);

		// Calculate offset: index * elementSize
		var elemSize = ConstantOp.Int(op.Value.Type.SizeInBytes, IntegerType.I64);
		rewriter.Insert(elemSize);
		var offset = new MulIOp(op.Index, elemSize.Result);
		rewriter.Insert(offset);

		// Get element pointer: buffer + offset
		var elemPtr = new PtrAddOp(bufferLoad.Result, offset.Result, new PtrType(op.Value.Type));
		rewriter.Insert(elemPtr);

		// Store element
		var store = new StoreOp(op.Value, elemPtr.Result);
		rewriter.Insert(store);
		return true;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_grow to heap realloc.
/// First ensures COW by inserting make_unique.
/// </summary>
public sealed class LowerManagedMemoryGrowOp : ConversionPattern<ManagedMemoryGrowOp> {
	protected override bool MatchAndRewrite(ManagedMemoryGrowOp op, ConversionPatternRewriter rewriter) {
		// First, ensure the memory is unique (COW check)
		var elemSizeForCow = ConstantOp.Int(op.ElementType.SizeInBytes, IntegerType.I64);
		rewriter.Insert(elemSizeForCow);
		var makeUnique = new ManagedMemoryMakeUniqueOp(op.Memory, elemSizeForCow.Result);
		rewriter.Insert(makeUnique);
		var memory = makeUnique.Result;

		// Load current buffer and capacity
		var bufferPtr = new FieldPtrOp(memory, "_buffer", 0, new PtrType(IntegerType.I8));
		rewriter.Insert(bufferPtr);
		var bufferLoad = new LoadOp(bufferPtr.Result);
		rewriter.Insert(bufferLoad);

		var capPtr = new FieldPtrOp(memory, "_capacity", 16, IntegerType.I64);
		rewriter.Insert(capPtr);
		var capLoad = new LoadOp(capPtr.Result);
		rewriter.Insert(capLoad);

		// Calculate new size: newCapacity * elementSize
		var elemSize = ConstantOp.Int(op.ElementType.SizeInBytes, IntegerType.I64);
		rewriter.Insert(elemSize);
		var newSize = new MulIOp(op.NewCapacity, elemSize.Result);
		rewriter.Insert(newSize);

		// Calculate old size: capacity * elementSize (for memcpy)
		var oldSize = new MulIOp(capLoad.Result, elemSize.Result);
		rewriter.Insert(oldSize);

		// Reallocate (use safe version that handles NULL)
		var realloc = new HeapReallocOrAllocOp(bufferLoad.Result, newSize.Result);
		rewriter.Insert(realloc);

		// Store new buffer pointer
		var storeBuffer = new StoreOp(realloc.Result, bufferPtr.Result);
		rewriter.Insert(storeBuffer);

		// Update capacity
		var storeCap = new StoreOp(op.NewCapacity, capPtr.Result);
		rewriter.Insert(storeCap);

		return true;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_set_length to field store at offset 8.
/// First ensures COW by inserting make_unique.
/// </summary>
public sealed class LowerManagedMemorySetLengthOp : ConversionPattern<ManagedMemorySetLengthOp> {
	protected override bool MatchAndRewrite(ManagedMemorySetLengthOp op, ConversionPatternRewriter rewriter) {
		// First, ensure the memory is unique (COW check)
		// Use 8 as a default element size (for length operations the actual size doesn't matter
		// since we're not copying data, just potentially changing the buffer)
		var elemSizeForCow = ConstantOp.Int(8, IntegerType.I64);
		rewriter.Insert(elemSizeForCow);
		var makeUnique = new ManagedMemoryMakeUniqueOp(op.Memory, elemSizeForCow.Result);
		rewriter.Insert(makeUnique);
		var memory = makeUnique.Result;

		var lenPtr = new FieldPtrOp(memory, "_len", 8, IntegerType.I64);
		rewriter.Insert(lenPtr);
		var store = new StoreOp(op.NewLength, lenPtr.Result);
		rewriter.Insert(store);
		return true;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_grow_byref - takes a pointer to __ManagedMemory.
/// This version allows modifications to persist to the original struct.
/// First ensures COW by inserting make_unique.
/// </summary>
public sealed class LowerManagedMemoryGrowByRefOp : ConversionPattern<ManagedMemoryGrowByRefOp> {
	protected override bool MatchAndRewrite(ManagedMemoryGrowByRefOp op, ConversionPatternRewriter rewriter) {
		// First, ensure the memory is unique (COW check)
		var elemSizeForCow = ConstantOp.Int(op.ElementType.SizeInBytes, IntegerType.I64);
		rewriter.Insert(elemSizeForCow);
		var makeUnique = new ManagedMemoryMakeUniqueOp(op.MemoryPtr, elemSizeForCow.Result);
		rewriter.Insert(makeUnique);
		var memoryPtr = makeUnique.Result;

		// MemoryPtr is a pointer to the __ManagedMemory struct
		// Calculate field addresses from the pointer base

		// Buffer pointer is at offset 0 from the struct (it's a ptr to a ptr)
		var bufferOffset = ConstantOp.Int(0, IntegerType.I64);
		rewriter.Insert(bufferOffset);
		var bufferPtrType = new PtrType(new PtrType(IntegerType.I8));
		var bufferFieldPtr = new PtrAddOp(memoryPtr, bufferOffset.Result, bufferPtrType);
		rewriter.Insert(bufferFieldPtr);
		var bufferPtrPtr = bufferFieldPtr.Result;

		// Load current buffer pointer
		var bufferLoad = new LoadOp(bufferPtrPtr);
		rewriter.Insert(bufferLoad);

		// Capacity is at offset 16
		var capOffset = ConstantOp.Int(16, IntegerType.I64);
		rewriter.Insert(capOffset);
		var capPtrType = new PtrType(IntegerType.I64);
		var capFieldPtr = new PtrAddOp(memoryPtr, capOffset.Result, capPtrType);
		rewriter.Insert(capFieldPtr);

		// Calculate new size: newCapacity * elementSize
		var elemSize = ConstantOp.Int(op.ElementType.SizeInBytes, IntegerType.I64);
		rewriter.Insert(elemSize);
		var newSize = new MulIOp(op.NewCapacity, elemSize.Result);
		rewriter.Insert(newSize);

		// Reallocate (use safe version that handles NULL)
		var realloc = new HeapReallocOrAllocOp(bufferLoad.Result, newSize.Result);
		rewriter.Insert(realloc);

		// Store new buffer pointer back
		var storeBuffer = new StoreOp(realloc.Result, bufferPtrPtr);
		rewriter.Insert(storeBuffer);

		// Update capacity
		var storeCap = new StoreOp(op.NewCapacity, capFieldPtr.Result);
		rewriter.Insert(storeCap);

		return true;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_set_length_byref - takes a pointer to __ManagedMemory.
/// First ensures COW by inserting make_unique.
/// </summary>
public sealed class LowerManagedMemorySetLengthByRefOp : ConversionPattern<ManagedMemorySetLengthByRefOp> {
	protected override bool MatchAndRewrite(ManagedMemorySetLengthByRefOp op, ConversionPatternRewriter rewriter) {
		// First, ensure the memory is unique (COW check)
		// Use 8 as default element size (for length ops the actual size doesn't matter)
		var elemSizeForCow = ConstantOp.Int(8, IntegerType.I64);
		rewriter.Insert(elemSizeForCow);
		var makeUnique = new ManagedMemoryMakeUniqueOp(op.MemoryPtr, elemSizeForCow.Result);
		rewriter.Insert(makeUnique);
		var memoryPtr = makeUnique.Result;

		// Length is at offset 8 from the struct pointer
		var lenOffset = ConstantOp.Int(8, IntegerType.I64);
		rewriter.Insert(lenOffset);
		var lenPtrType = new PtrType(IntegerType.I64);
		var lenFieldPtr = new PtrAddOp(memoryPtr, lenOffset.Result, lenPtrType);
		rewriter.Insert(lenFieldPtr);

		var store = new StoreOp(op.NewLength, lenFieldPtr.Result);
		rewriter.Insert(store);
		return true;
	}
}

/// <summary>
/// Lowers maxon.managed_memory_shift_left to memcpy.
/// Shifts elements left (to lower addresses), which is safe for forward copy.
/// First ensures COW by inserting make_unique.
/// </summary>
public sealed class LowerManagedMemoryShiftLeftOp : ConversionPattern<ManagedMemoryShiftLeftOp> {
	protected override bool MatchAndRewrite(ManagedMemoryShiftLeftOp op, ConversionPatternRewriter rewriter) {
		// First, ensure the memory is unique (COW check)
		var elemSizeForCow = ConstantOp.Int(op.ElementType.SizeInBytes, IntegerType.I64);
		rewriter.Insert(elemSizeForCow);
		var makeUnique = new ManagedMemoryMakeUniqueOp(op.Memory, elemSizeForCow.Result);
		rewriter.Insert(makeUnique);
		var memory = makeUnique.Result;

		// Load buffer pointer
		var bufferPtr = new FieldPtrOp(memory, "_buffer", 0, new PtrType(IntegerType.I8));
		rewriter.Insert(bufferPtr);
		var bufferLoad = new LoadOp(bufferPtr.Result);
		rewriter.Insert(bufferLoad);

		var elemSize = ConstantOp.Int(op.ElementType.SizeInBytes, IntegerType.I64);
		rewriter.Insert(elemSize);

		// Source: buffer + (startIndex + count) * elemSize
		var srcIndex = new AddIOp(op.StartIndex, op.Count);
		rewriter.Insert(srcIndex);
		var srcOffset = new MulIOp(srcIndex.Result, elemSize.Result);
		rewriter.Insert(srcOffset);
		var srcPtr = new PtrAddOp(bufferLoad.Result, srcOffset.Result, new PtrType(IntegerType.I8));
		rewriter.Insert(srcPtr);

		// Destination: buffer + startIndex * elemSize
		var destOffset = new MulIOp(op.StartIndex, elemSize.Result);
		rewriter.Insert(destOffset);
		var destPtr = new PtrAddOp(bufferLoad.Result, destOffset.Result, new PtrType(IntegerType.I8));
		rewriter.Insert(destPtr);

		// Length to copy: (len - startIndex - count) * elemSize
		var lenPtr = new FieldPtrOp(memory, "_len", 8, IntegerType.I64);
		rewriter.Insert(lenPtr);
		var lenLoad = new LoadOp(lenPtr.Result);
		rewriter.Insert(lenLoad);
		var temp = new SubIOp(lenLoad.Result, op.StartIndex);
		rewriter.Insert(temp);
		var copyCount = new SubIOp(temp.Result, op.Count);
		rewriter.Insert(copyCount);
		var copyBytes = new MulIOp(copyCount.Result, elemSize.Result);
		rewriter.Insert(copyBytes);

		var memcpy = new MemCpyOp(destPtr.Result, srcPtr.Result, copyBytes.Result);
		rewriter.Insert(memcpy);

		return true;
	}
}

/// <summary>
/// Lowers maxon.element_size to a constant integer.
/// </summary>
public sealed class LowerElementSizeOp : ConversionPattern<ElementSizeOp> {
	protected override bool MatchAndRewrite(ElementSizeOp op, ConversionPatternRewriter rewriter) {
		var sizeConst = ConstantOp.Int(op.ElementType.SizeInBytes, IntegerType.I64);
		rewriter.Insert(sizeConst);
		rewriter.ReplaceOpWithValue(op, sizeConst.Result);
		return true;
	}
}

