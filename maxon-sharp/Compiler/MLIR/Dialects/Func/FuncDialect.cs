using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Dialects.Func;

/// <summary>
/// Func dialect - function definitions and calls.
/// </summary>
public sealed class FuncDialect : DialectBase {
	public override string Name => "func";

	public override IEnumerable<Type> Operations => [
		typeof(FuncOp),
		typeof(CallOp),
		typeof(ReturnOp),
		typeof(CallIndirectOp),
	];
}
