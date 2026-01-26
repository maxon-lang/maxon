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
				// Create index for the offset
				var offsetConst = ConstantOp.Index(offset);
				rewriter.Insert(offsetConst);

				// Store the value
				var store = new StoreOp(value, alloca.Result, offsetConst.Result);
				rewriter.Insert(store);
			}
			offset += field.Type.SizeInBytes;
		}

		rewriter.ReplaceOpWithValue(op, alloca.Result);
		return true;
	}
}

/// <summary>
/// Lowers maxon.field_get to field_ptr + load.
/// </summary>
public sealed class LowerFieldGetOp : ConversionPattern<FieldGetOp> {
	protected override bool MatchAndRewrite(FieldGetOp op, ConversionPatternRewriter rewriter) {
		var fieldPtr = EmitFieldPtr(op.Struct, op.FieldName, rewriter);

		var load = new LoadOp(fieldPtr.Result);
		rewriter.Insert(load);

		rewriter.ReplaceOpWithValue(op, load.Result);
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

		var store = new StoreOp(op.Value, fieldPtr.Result);
		rewriter.Insert(store);

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
/// Lowers maxon.array_get to memref.load.
/// </summary>
public sealed class LowerArrayGetOp : ConversionPattern<ArrayGetOp> {
	protected override bool MatchAndRewrite(ArrayGetOp op, ConversionPatternRewriter rewriter) {
		var load = new LoadOp(op.Array, op.Index);
		rewriter.Insert(load);
		rewriter.ReplaceOpWithValue(op, load.Result);
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

		// Non-struct return: standard call
		var standardCall = new FuncCallOp(op.Callee, op.Operands, op.Result?.Type);
		rewriter.Insert(standardCall);
		if (op.Result is not null && standardCall.Result is not null) {
			rewriter.ReplaceOpWithValue(op, standardCall.Result);
		}
		return true;
	}
}

