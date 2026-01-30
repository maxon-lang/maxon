using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public class CfCondBrOp(MlirValue condition, string thenBlock, string elseBlock) : MlirOperation {
	public override string Mnemonic => $"cf.cond_br %{Condition.Id} [then: {ThenBlock}, else: {ElseBlock}]";
	public MlirValue Condition { get; } = condition;
	public string ThenBlock { get; } = thenBlock;
	public string ElseBlock { get; } = elseBlock;
}

public class CfBrOp(string target) : MlirOperation {
	public override string Mnemonic => $"cf.br {Target}";
	public string Target { get; } = target;
}
