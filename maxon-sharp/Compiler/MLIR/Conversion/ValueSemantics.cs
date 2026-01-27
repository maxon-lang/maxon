using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Result of accessing memory (field, array element, error union payload, etc).
/// Discriminated union that forces callers to handle struct-pointer vs loaded-value cases.
/// This prevents bugs where struct values are accidentally loaded instead of kept as pointers.
/// </summary>
public abstract record MemoryAccessResult
{
	/// <summary>
	/// Pointer to a struct - do NOT load, use directly.
	/// Structs are always passed by reference in Maxon.
	/// </summary>
	public sealed record StructRef(MlirValue Pointer) : MemoryAccessResult;

	/// <summary>
	/// Loaded primitive value - ready to use.
	/// </summary>
	public sealed record Value(MlirValue Val) : MemoryAccessResult;

	/// <summary>
	/// Get the underlying MlirValue after handling is complete.
	/// </summary>
	public MlirValue Unwrap() => this switch
	{
		StructRef s => s.Pointer,
		Value v => v.Val,
		_ => throw new InvalidOperationException("Unknown MemoryAccessResult variant")
	};
}

/// <summary>
/// Centralized logic for value vs pointer semantics in the MLIR pipeline.
/// Use these helpers to avoid accidentally loading struct values.
/// </summary>
public static class ValueSemantics
{
	/// <summary>
	/// Returns true if the type should be kept as a pointer (not loaded).
	/// Structs and error unions are reference types in Maxon.
	/// </summary>
	public static bool IsReferenceType(MlirType type) => type switch
	{
		MaxonStructType => true,
		MaxonErrorUnionType => true,
		OwnedType { Inner: MaxonStructType } => true,
		OwnedType { Inner: MaxonErrorUnionType } => true,
		BorrowedType { Inner: MaxonStructType } => true,
		BorrowedType { Inner: MaxonErrorUnionType } => true,
		_ => false
	};

	/// <summary>
	/// Access memory at ptr, returning a discriminated union that forces correct handling.
	/// Call this instead of manually emitting LoadOp.
	///
	/// For reference types (structs, error unions): returns StructRef (pointer, no load)
	/// For value types (primitives): returns Value (emits load, returns loaded value)
	/// </summary>
	public static MemoryAccessResult Load(
		MlirValue ptr,
		MlirType elementType,
		ConversionPatternRewriter rewriter)
	{
		if (IsReferenceType(elementType))
			return new MemoryAccessResult.StructRef(ptr);

		var load = new LoadOp(ptr);
		rewriter.Insert(load);
		return new MemoryAccessResult.Value(load.Result);
	}
}

// ============================================================================
// VariableBinding - How a variable is stored
// ============================================================================

/// <summary>
/// How a variable is stored. Forces correct access pattern.
/// Variables can be either stack slots (need load) or direct values (use as-is).
/// </summary>
public abstract record VariableBinding
{
	/// <summary>
	/// Stack slot - requires load to get value.
	/// Used for local variables that have an address on the stack.
	/// </summary>
	public sealed record StackSlot(MlirValue Address) : VariableBinding;

	/// <summary>
	/// Direct value - use directly without loading.
	/// Used for struct parameters passed by reference.
	/// </summary>
	public sealed record Direct(MlirValue Value) : VariableBinding;

	/// <summary>
	/// Get the address for storing to this binding.
	/// </summary>
	public MlirValue GetAddress() => this switch
	{
		StackSlot s => s.Address,
		Direct d => d.Value, // For structs, the value IS the address
		_ => throw new InvalidOperationException("Unknown VariableBinding variant")
	};
}

// ============================================================================
// PassedArgument - Argument with ownership
// ============================================================================

/// <summary>
/// An argument with its determined ownership. Forces consistent handling.
/// Ownership is determined once when preparing arguments, not scattered across code.
/// </summary>
public abstract record PassedArgument
{
	/// <summary>Argument passed by copy (primitives).</summary>
	public sealed record Copied(MlirValue Value) : PassedArgument;

	/// <summary>Argument passed by move (ownership transferred).</summary>
	public sealed record Moved(MlirValue Value) : PassedArgument;

	/// <summary>Argument passed by borrow (temporary reference).</summary>
	public sealed record Borrowed(MlirValue Value) : PassedArgument;

	/// <summary>Get the underlying value.</summary>
	public MlirValue Unwrap() => this switch
	{
		Copied c => c.Value,
		Moved m => m.Value,
		Borrowed b => b.Value,
		_ => throw new InvalidOperationException("Unknown PassedArgument variant")
	};

	/// <summary>Get the ownership mode for this argument.</summary>
	public ArgumentOwnership Ownership => this switch
	{
		Copied => ArgumentOwnership.Copy,
		Moved => ArgumentOwnership.Move,
		Borrowed => ArgumentOwnership.Borrow,
		_ => throw new InvalidOperationException("Unknown PassedArgument variant")
	};
}

/// <summary>
/// Centralized logic for determining argument ownership.
/// </summary>
public static class ArgumentSemantics
{
	/// <summary>
	/// Determine the ownership for an argument based on type and mutation analysis.
	/// </summary>
	public static PassedArgument DetermineOwnership(
		MlirValue arg,
		string funcName,
		int paramIndex,
		MaxonSharp.Compiler.MutationAnalyzer analyzer)
	{
		if (arg.Type.IsCopyType)
			return new PassedArgument.Copied(arg);
		if (analyzer.IsMutated(funcName, paramIndex))
			return new PassedArgument.Moved(arg);
		return new PassedArgument.Borrowed(arg);
	}
}

// ============================================================================
// StoreAction - How to store a value
// ============================================================================

/// <summary>
/// How to store a value to memory. Forces correct store/memcpy choice.
/// Primitives use StoreOp, aggregates (structs) use MemCpyOp.
/// </summary>
public abstract record StoreAction
{
	/// <summary>Primitive value - use StoreOp.</summary>
	public sealed record Primitive(MlirValue Value) : StoreAction;

	/// <summary>Aggregate (struct) - use MemCpyOp from source pointer.</summary>
	public sealed record Aggregate(MlirValue SourcePtr, int Size) : StoreAction;
}

/// <summary>
/// Centralized logic for storing values to memory.
/// </summary>
public static class StoreSemantics
{
	/// <summary>
	/// Determine how to store a value based on target type.
	/// </summary>
	public static StoreAction DetermineStoreAction(MlirValue value, MlirType targetType)
	{
		if (targetType is MaxonStructType st)
			return new StoreAction.Aggregate(value, st.SizeInBytes);
		if (targetType is MaxonErrorUnionType eu)
			return new StoreAction.Aggregate(value, eu.SizeInBytes);
		return new StoreAction.Primitive(value);
	}

	/// <summary>
	/// Emit the appropriate store operation.
	/// </summary>
	public static void EmitStore(StoreAction action, MlirValue destPtr, ConversionPatternRewriter rewriter)
	{
		switch (action)
		{
			case StoreAction.Primitive p:
				rewriter.Insert(new StoreOp(p.Value, destPtr));
				break;
			case StoreAction.Aggregate a:
				var size = ConstantOp.Int(a.Size, IntegerType.I64);
				rewriter.Insert(size);
				rewriter.Insert(new MemCpyOp(destPtr, a.SourcePtr, size.Result));
				break;
		}
	}
}

// ============================================================================
// FunctionParameter - How a parameter is passed to a function
// ============================================================================

/// <summary>
/// How a parameter is passed to a function. Forces correct caller/callee semantics.
/// This discriminated union ensures type-safe parameter passing and prevents
/// mismatches between caller (passing pointer) and callee (expecting value).
/// </summary>
public abstract record FunctionParameter
{
	/// <summary>Name of the parameter.</summary>
	public abstract string Name { get; }

	/// <summary>The MLIR type of the parameter as it appears in the function signature.</summary>
	public abstract MlirType Type { get; }

	/// <summary>
	/// Scalar parameter - passed by value, needs alloca for SSA promotion.
	/// Used for primitives (int, float, bool).
	/// </summary>
	public sealed record Scalar(string ParamName, MlirType ParamType) : FunctionParameter
	{
		public override string Name => ParamName;
		public override MlirType Type => ParamType;
	}

	/// <summary>
	/// Struct self receiver - passed by reference (pointer), used directly.
	/// The caller passes the address; callee uses it as a pointer to mutate.
	/// </summary>
	public sealed record SelfRef(string ParamName, MaxonStructType StructType) : FunctionParameter
	{
		public override string Name => ParamName;
		public override MlirType Type => new MemRefType(StructType);
	}

	/// <summary>
	/// Struct parameter (not self) - passed by reference, used directly.
	/// For struct parameters other than self.
	/// </summary>
	public sealed record StructRef(string ParamName, MaxonStructType StructType) : FunctionParameter
	{
		public override string Name => ParamName;
		public override MlirType Type => new MemRefType(StructType);
	}

	/// <summary>
	/// Returns true if this parameter needs an alloca for SSA promotion.
	/// </summary>
	public bool NeedsAlloca => this is Scalar;

	/// <summary>
	/// Returns the underlying struct type for self/struct ref parameters.
	/// </summary>
	public MaxonStructType? GetStructType() => this switch
	{
		SelfRef s => s.StructType,
		StructRef s => s.StructType,
		_ => null
	};
}
