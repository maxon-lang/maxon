using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

/// <summary>
/// Base class for control flow operations.
/// </summary>
public abstract class CfOp : MlirOperation {
	public override string Dialect => "cf";
	public override bool IsTerminator => true;
}

// ============================================================================
// Unconditional Branch
// ============================================================================

/// <summary>
/// Unconditional branch: cf.br ^block(%args...)
/// </summary>
public sealed class BranchOp : CfOp {
	public override string Mnemonic => "br";
	public override bool HasSideEffects => true;

	public MlirBlock Destination => Successors[0];
	public IReadOnlyList<MlirValue> BlockArguments => Operands;

	public BranchOp(MlirBlock destination, params MlirValue[] args) {
		Successors.Add(destination);
		foreach (var arg in args)
			Operands.Add(arg);
	}

	public override void Print(MlirPrinter printer) {
		if (Operands.Count > 0) {
			var argsStr = string.Join(", ", Operands);
			var typesStr = string.Join(", ", Operands.Select(o => o.Type));
			printer.PrintLine($"cf.br ^{Destination.Name}({argsStr} : {typesStr})");
		} else {
			printer.PrintLine($"cf.br ^{Destination.Name}");
		}
	}
}

// ============================================================================
// Conditional Branch
// ============================================================================

/// <summary>
/// Conditional branch: cf.cond_br %cond, ^true(%args...), ^false(%args...)
/// </summary>
public sealed class CondBranchOp : CfOp {
	public override string Mnemonic => "cond_br";
	public override bool HasSideEffects => true;

	public MlirValue Condition => Operands[0];
	public MlirBlock TrueBlock => Successors[0];
	public MlirBlock FalseBlock => Successors[1];

	private readonly int _trueArgsCount;

	public IReadOnlyList<MlirValue> TrueArguments => [.. Operands.Skip(1).Take(_trueArgsCount)];
	public IReadOnlyList<MlirValue> FalseArguments => [.. Operands.Skip(1 + _trueArgsCount)];

	public CondBranchOp(MlirValue condition, MlirBlock trueBlock, MlirBlock falseBlock,
						IEnumerable<MlirValue>? trueArgs = null, IEnumerable<MlirValue>? falseArgs = null) {
		Operands.Add(condition);
		Successors.Add(trueBlock);
		Successors.Add(falseBlock);

		var trueArgsList = trueArgs?.ToList() ?? [];
		var falseArgsList = falseArgs?.ToList() ?? [];

		_trueArgsCount = trueArgsList.Count;

		foreach (var arg in trueArgsList)
			Operands.Add(arg);
		foreach (var arg in falseArgsList)
			Operands.Add(arg);
	}

	public override void Print(MlirPrinter printer) {
		var trueArgsStr = FormatBlockArgs(TrueArguments);
		var falseArgsStr = FormatBlockArgs(FalseArguments);
		printer.PrintLine($"cf.cond_br {Condition}, ^{TrueBlock.Name}{trueArgsStr}, ^{FalseBlock.Name}{falseArgsStr}");
	}

	private static string FormatBlockArgs(IReadOnlyList<MlirValue> args) {
		if (args.Count == 0) return "";
		var argsStr = string.Join(", ", args);
		var typesStr = string.Join(", ", args.Select(a => a.Type));
		return $"({argsStr} : {typesStr})";
	}
}

// ============================================================================
// Switch
// ============================================================================

/// <summary>
/// A case for a switch operation.
/// </summary>
public sealed record SwitchCase(long Value, MlirBlock Block, IReadOnlyList<MlirValue> Arguments);

/// <summary>
/// Multi-way branch: cf.switch %index, [case values -> ^blocks]
/// </summary>
public sealed class SwitchOp : CfOp {
	public override string Mnemonic => "switch";
	public override bool HasSideEffects => true;

	public MlirValue Index => Operands[0];
	public MlirBlock DefaultBlock { get; }
	public IReadOnlyList<SwitchCase> Cases { get; }

	public SwitchOp(MlirValue index, MlirBlock defaultBlock, IEnumerable<MlirValue>? defaultArgs,
					List<SwitchCase> cases) {
		Cases = cases;
		DefaultBlock = defaultBlock;

		Operands.Add(index);

		// Default block is first successor
		Successors.Add(defaultBlock);
		foreach (var arg in defaultArgs ?? [])
			Operands.Add(arg);

		// Case blocks follow
		foreach (var c in cases) {
			Successors.Add(c.Block);
		}
	}

	public override void Print(MlirPrinter printer) {
		printer.PrintLine($"cf.switch {Index} : {Index.Type}, [");
		printer.Indent();
		printer.PrintLine($"default: ^{DefaultBlock.Name}");
		foreach (var c in Cases) {
			var argsStr = c.Arguments.Count > 0 ? $"({string.Join(", ", c.Arguments)})" : "";
			printer.PrintLine($"{c.Value}: ^{c.Block.Name}{argsStr}");
		}
		printer.Dedent();
		printer.PrintLine("]");
	}
}
