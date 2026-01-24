using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Dialects.Cf;

/// <summary>
/// Cf dialect - control flow operations.
/// </summary>
public sealed class CfDialect : DialectBase {
	public override string Name => "cf";

	public override IEnumerable<Type> Operations => [
		typeof(BranchOp),
		typeof(CondBranchOp),
		typeof(SwitchOp),
	];
}
