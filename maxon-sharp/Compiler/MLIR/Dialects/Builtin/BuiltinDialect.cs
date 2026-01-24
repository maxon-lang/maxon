using MaxonSharp.Compiler.Mlir.Core;

using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Dialects.Builtin;

/// <summary>
/// Builtin dialect - provides fundamental types used by all dialects.
/// </summary>
public sealed class BuiltinDialect : DialectBase {
	public override string Name => "builtin";

	public override IEnumerable<Type> Types => [
		typeof(IntegerType),
		typeof(FloatType),
		typeof(IndexType),
		typeof(NoneType),
		typeof(FunctionType),
		typeof(TupleType),
		typeof(UnrankedMemRefType),
	];
}
