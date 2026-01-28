using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

/// <summary>
/// Base class for memref operations.
/// </summary>
public abstract class MemRefOp : MlirOperation {
	public override string Dialect => "memref";
}

// ============================================================================
// Allocation Operations
// ============================================================================

/// <summary>
/// Stack allocation: %result = memref.alloca : memref<type>
/// </summary>
public sealed class AllocaOp : MemRefOp {
	public override string Mnemonic => "alloca";
	public override bool HasSideEffects => true;

	public MemRefType MemRefType { get; }
	public MlirValue Result => Results[0];

	public AllocaOp(MlirType elementType) {
		MemRefType = new MemRefType(elementType);
		CreateResult(MemRefType);
	}

	public AllocaOp(MemRefType memRefType) {
		MemRefType = memRefType;
		CreateResult(memRefType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = memref.alloca : {MemRefType}");
	}
}

/// <summary>
/// Heap allocation: %result = memref.alloc : memref<type>
/// </summary>
public sealed class AllocOp : MemRefOp {
	public override string Mnemonic => "alloc";
	public override bool HasSideEffects => true;

	public MemRefType MemRefType { get; }
	public MlirValue Result => Results[0];

	public AllocOp(MlirType elementType) {
		MemRefType = new MemRefType(elementType);
		CreateResult(MemRefType);
	}

	public AllocOp(MemRefType memRefType) {
		MemRefType = memRefType;
		CreateResult(memRefType);
	}

	/// <summary>
	/// Creates an allocation with a dynamic size.
	/// </summary>
	public AllocOp(MlirType elementType, MlirValue size) {
		MemRefType = new MemRefType(elementType, [-1]); // Dynamic
		Operands.Add(size);
		CreateResult(MemRefType);
	}

	public override void Print(MlirPrinter printer) {
		if (Operands.Count > 0) {
			printer.PrintLine($"{Result} = memref.alloc({Operands[0]}) : {MemRefType}");
		} else {
			printer.PrintLine($"{Result} = memref.alloc : {MemRefType}");
		}
	}
}

/// <summary>
/// Deallocation: memref.dealloc %ptr : memref<type>
/// </summary>
public sealed class DeallocOp : MemRefOp {
	public override string Mnemonic => "dealloc";
	public override bool HasSideEffects => true;

	public MlirValue MemRef => Operands[0];

	public DeallocOp(MlirValue memref) {
		Operands.Add(memref);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"memref.dealloc {MemRef} : {MemRef.Type}");
	}
}

// ============================================================================
// Memory Access Operations
// ============================================================================

/// <summary>
/// Load from memory: %result = memref.load %memref[%indices...] : type
/// Supports both MemRefType and PtrType operands.
/// </summary>
public sealed class LoadOp : MemRefOp {
	public override string Mnemonic => "load";
	public override bool HasSideEffects => false;

	public MlirValue MemRef => Operands[0];
	public IReadOnlyList<MlirValue> Indices => [.. Operands.Skip(1)];
	public MlirValue Result => Results[0];

	public LoadOp(MlirValue memref, params MlirValue[] indices) {
		MlirType elementType = memref.Type switch {
			MemRefType mrt => mrt.ElementType,
			PtrType pt => pt.ElementType ?? throw new ArgumentException("Cannot load from untyped pointer"),
			_ => throw new ArgumentException($"Expected memref or ptr type, got {memref.Type}")
		};

		Operands.Add(memref);
		foreach (var idx in indices)
			Operands.Add(idx);
		CreateResult(elementType);
	}

	public override void Print(MlirPrinter printer) {
		if (Indices.Count > 0) {
			var idxStr = string.Join(", ", Indices);
			printer.PrintLine($"{Result} = memref.load {MemRef}[{idxStr}] : {MemRef.Type}");
		} else {
			printer.PrintLine($"{Result} = memref.load {MemRef}[] : {MemRef.Type}");
		}
	}
}

/// <summary>
/// Store to memory: memref.store %value, %memref[%indices...] : type
/// </summary>
public sealed class StoreOp : MemRefOp {
	public override string Mnemonic => "store";
	public override bool HasSideEffects => true;

	public MlirValue Value => Operands[0];
	public MlirValue MemRef => Operands[1];
	public IReadOnlyList<MlirValue> Indices => [.. Operands.Skip(2)];

	public StoreOp(MlirValue value, MlirValue memref, params MlirValue[] indices) {
		Operands.Add(value);
		Operands.Add(memref);
		foreach (var idx in indices)
			Operands.Add(idx);
	}

	public override void Print(MlirPrinter printer) {
		if (Indices.Count > 0) {
			var idxStr = string.Join(", ", Indices);
			printer.PrintLine($"memref.store {Value}, {MemRef}[{idxStr}] : {MemRef.Type}");
		} else {
			printer.PrintLine($"memref.store {Value}, {MemRef}[] : {MemRef.Type}");
		}
	}
}

/// <summary>
/// Copy memory: memref.copy %src, %dst : memref<type>
/// </summary>
public sealed class CopyOp : MemRefOp {
	public override string Mnemonic => "copy";
	public override bool HasSideEffects => true;

	public MlirValue Source => Operands[0];
	public MlirValue Destination => Operands[1];

	public CopyOp(MlirValue source, MlirValue destination) {
		Operands.Add(source);
		Operands.Add(destination);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"memref.copy {Source}, {Destination} : {Source.Type}");
	}
}

// ============================================================================
// View Operations
// ============================================================================

/// <summary>
/// Create a subview: %result = memref.subview %src[%offset][%size][%stride] : memref
/// </summary>
public sealed class SubViewOp : MemRefOp {
	public override string Mnemonic => "subview";
	public override bool HasSideEffects => false;

	public MlirValue Source => Operands[0];
	public MlirValue Offset => Operands[1];
	public MlirValue Size => Operands[2];
	public MlirValue Stride => Operands[3];
	public MlirValue Result => Results[0];

	public SubViewOp(MlirValue source, MlirValue offset, MlirValue size, MlirValue stride, MemRefType resultType) {
		Operands.Add(source);
		Operands.Add(offset);
		Operands.Add(size);
		Operands.Add(stride);
		CreateResult(resultType);
	}
}

/// <summary>
/// Cast between memref types: %result = memref.cast %src : memref<T> to memref<U>
/// </summary>
public sealed class CastOp : MemRefOp {
	public override string Mnemonic => "cast";
	public override bool HasSideEffects => false;

	public MlirValue Source => Operands[0];
	public MlirValue Result => Results[0];

	public CastOp(MlirValue source, MlirType targetType) {
		Operands.Add(source);
		CreateResult(targetType);
	}
}

// ============================================================================
// Global Operations
// ============================================================================

/// <summary>
/// Get address of a global: %result = memref.get_global @name : memref<type>
/// </summary>
public sealed class GetGlobalOp : MemRefOp {
	public override string Mnemonic => "get_global";
	public override bool HasSideEffects => false;

	public string Name { get; }
	public MlirValue Result => Results[0];

	public GetGlobalOp(string name, MemRefType type) {
		Name = name;
		Attributes["name"] = new SymbolRefAttr(name);
		CreateResult(type);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = memref.get_global @{Name} : {Result.Type}");
	}
}

/// <summary>
/// Declare a global variable: memref.global @name : memref<type> = value
/// </summary>
public sealed class GlobalOp : MemRefOp {
	public override string Mnemonic => "global";
	public override bool HasSideEffects => true;
	public override bool IsConstant => _isConstant;

	public string Name { get; }
	public MemRefType Type { get; }
	private readonly bool _isConstant;
	public MlirAttribute? InitialValue { get; }

	public GlobalOp(string name, MemRefType type, bool isConstant = false, MlirAttribute? initialValue = null) {
		Name = name;
		Type = type;
		_isConstant = isConstant;
		InitialValue = initialValue;
		Attributes["sym_name"] = new StringAttr(name);
		Attributes["type"] = new TypeAttr(type);
		if (isConstant)
			Attributes["constant"] = UnitAttr.Instance;
	}

	public override void Print(MlirPrinter printer) {
		var constStr = IsConstant ? "constant " : "";
		var initStr = InitialValue is not null ? $" = {InitialValue}" : "";
		printer.PrintLine($"memref.global {constStr}@{Name} : {Type}{initStr}");
	}
}

// ============================================================================
// Heap Operations (for managed memory)
// ============================================================================

/// <summary>
/// Heap allocation: %result = memref.heap_alloc %size : !ptr<i8>
/// Allocates size bytes on the heap.
/// </summary>
public sealed class HeapAllocOp : MemRefOp {
	public override string Mnemonic => "heap_alloc";
	public override bool HasSideEffects => true;

	public MlirValue Size => Operands[0];
	public MlirValue Result => Results[0];

	public HeapAllocOp(MlirValue size) {
		Operands.Add(size);
		CreateResult(new PtrType(IntegerType.I8));
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = memref.heap_alloc {Size} : !ptr<i8>");
	}
}

/// <summary>
/// Heap reallocation: %result = memref.heap_realloc %ptr, %newSize : !ptr<i8>
/// Reallocates the buffer to a new size.
/// NOTE: This does NOT handle NULL pointers on Windows. Use HeapReallocOrAllocOp for safe realloc.
/// </summary>
public sealed class HeapReallocOp : MemRefOp {
	public override string Mnemonic => "heap_realloc";
	public override bool HasSideEffects => true;

	public MlirValue Ptr => Operands[0];
	public MlirValue NewSize => Operands[1];
	public MlirValue Result => Results[0];

	public HeapReallocOp(MlirValue ptr, MlirValue newSize) {
		Operands.Add(ptr);
		Operands.Add(newSize);
		CreateResult(new PtrType(IntegerType.I8));
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = memref.heap_realloc {Ptr}, {NewSize} : !ptr<i8>");
	}
}

/// <summary>
/// Safe heap reallocation that handles NULL pointers: %result = memref.heap_realloc_or_alloc %ptr, %newSize : !ptr<i8>
/// If ptr is NULL, allocates new memory. Otherwise, reallocates.
/// This is needed because Windows HeapReAlloc doesn't handle NULL like Unix realloc.
/// </summary>
public sealed class HeapReallocOrAllocOp : MemRefOp {
	public override string Mnemonic => "heap_realloc_or_alloc";
	public override bool HasSideEffects => true;

	public MlirValue Ptr => Operands[0];
	public MlirValue NewSize => Operands[1];
	public MlirValue Result => Results[0];

	public HeapReallocOrAllocOp(MlirValue ptr, MlirValue newSize) {
		Operands.Add(ptr);
		Operands.Add(newSize);
		CreateResult(new PtrType(IntegerType.I8));
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = memref.heap_realloc_or_alloc {Ptr}, {NewSize} : !ptr<i8>");
	}
}

/// <summary>
/// Heap free: memref.heap_free %ptr
/// Frees memory allocated on the heap.
/// </summary>
public sealed class HeapFreeOp : MemRefOp {
	public override string Mnemonic => "heap_free";
	public override bool HasSideEffects => true;

	public MlirValue Ptr => Operands[0];

	public HeapFreeOp(MlirValue ptr) {
		Operands.Add(ptr);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"memref.heap_free {Ptr}");
	}
}

/// <summary>
/// Pointer add: %result = memref.ptr_add %ptr, %offset : !ptr<T>
/// Adds offset bytes to a pointer.
/// </summary>
public sealed class PtrAddOp : MemRefOp {
	public override string Mnemonic => "ptr_add";
	public override bool HasSideEffects => false;

	public MlirValue Ptr => Operands[0];
	public MlirValue Offset => Operands[1];
	public MlirValue Result => Results[0];
	public PtrType ResultPtrType { get; }

	public PtrAddOp(MlirValue ptr, MlirValue offset, PtrType resultType) {
		Operands.Add(ptr);
		Operands.Add(offset);
		ResultPtrType = resultType;
		CreateResult(resultType);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"{Result} = memref.ptr_add {Ptr}, {Offset} : {ResultPtrType}");
	}
}

/// <summary>
/// Memory copy (non-overlapping): memref.memcpy %dst, %src, %len
/// </summary>
public sealed class MemCpyOp : MemRefOp {
	public override string Mnemonic => "memcpy";
	public override bool HasSideEffects => true;

	public MlirValue Destination => Operands[0];
	public MlirValue Source => Operands[1];
	public MlirValue Length => Operands[2];

	public MemCpyOp(MlirValue dst, MlirValue src, MlirValue length) {
		Operands.Add(dst);
		Operands.Add(src);
		Operands.Add(length);
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"memref.memcpy {Destination}, {Source}, {Length}");
	}
}
