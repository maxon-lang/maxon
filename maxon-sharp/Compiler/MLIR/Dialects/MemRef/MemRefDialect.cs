using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Dialects.MemRef;

/// <summary>
/// MemRef dialect - memory reference operations.
/// </summary>
public sealed class MemRefDialect : DialectBase {
	public override string Name => "memref";

	public override IEnumerable<Type> Operations => [
		typeof(AllocaOp),
		typeof(AllocOp),
		typeof(DeallocOp),
		typeof(LoadOp),
		typeof(StoreOp),
		typeof(CopyOp),
		typeof(SubViewOp),
		typeof(CastOp),
		typeof(GetGlobalOp),
		typeof(GlobalOp),
	];
}
