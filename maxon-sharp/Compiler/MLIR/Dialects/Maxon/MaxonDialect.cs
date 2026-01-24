using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Dialects.Maxon;

/// <summary>
/// Maxon dialect - language-level semantics including ownership.
/// </summary>
public sealed class MaxonDialect : DialectBase {
	public override string Name => "maxon";

	public override IEnumerable<Type> Types => [
		typeof(MaxonStructType),
		typeof(MaxonEnumType),
		typeof(MaxonArrayType),
		typeof(OwnedType),
		typeof(BorrowedType),
	];

	public override IEnumerable<Type> Operations => [
        // Ownership operations
        typeof(MoveOp),
		typeof(BorrowOp),
		typeof(DropOp),
        // Struct operations
        typeof(StructInitOp),
		typeof(FieldGetOp),
		typeof(FieldSetOp),
		typeof(FieldPtrOp),
        // Enum operations
        typeof(EnumInitOp),
		typeof(MatchOp),
		typeof(GetTagOp),
		typeof(GetPayloadOp),
        // Array operations
        typeof(ArrayNewOp),
		typeof(ArrayGetOp),
		typeof(ArraySetOp),
		typeof(ArrayLenOp),
		typeof(ArrayPtrOp),
        // Function call with ownership
        typeof(CallOp),
	];
}
