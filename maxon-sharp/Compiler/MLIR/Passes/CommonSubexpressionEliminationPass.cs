using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Common Subexpression Elimination (CSE) pass - removes redundant computations.
/// </summary>
public sealed class CommonSubexpressionEliminationPass : FunctionPass {
	public override string Name => "cse";
	public override string Description => "Eliminates common subexpressions";

	protected override bool RunOnFunction(MlirFunction func) {
		bool changed = false;
		int eliminatedCount = 0;

		Logger.Debug(LogCategory.Optimizer, $"cse: processing {func.Name}");

		foreach (var block in func.Body.Blocks) {
			var (blockChanged, count) = ProcessBlock(block);
			changed |= blockChanged;
			eliminatedCount += count;
		}

		if (eliminatedCount > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: eliminated {eliminatedCount} common subexpressions");
		}

		return changed;
	}

	private static (bool changed, int eliminatedCount) ProcessBlock(MlirBlock block) {
		bool changed = false;
		var expressionMap = new Dictionary<ExpressionKey, MlirValue>();
		var opsToRemove = new List<MlirOperation>();
		var valueReplacements = new Dictionary<MlirValue, MlirValue>();

		foreach (var op in block.Operations) {
			// Skip operations that can't be CSE'd
			if (op.IsTerminator || op.HasSideEffects || op.Results.Count != 1) {
				continue;
			}

			var key = ComputeExpressionKey(op);
			if (key is null) continue;

			if (expressionMap.TryGetValue(key, out var existingValue)) {
				// Found a common subexpression - mark for removal
				Logger.Trace(LogCategory.Optimizer, $"  cse: {op.Mnemonic} result %{op.Results[0].Id} -> %{existingValue.Id}");
				valueReplacements[op.Results[0]] = existingValue;
				opsToRemove.Add(op);
				changed = true;
			} else {
				// First occurrence - add to map
				expressionMap[key] = op.Results[0];
			}
		}

		// Replace uses of removed values
		foreach (var op in block.Operations) {
			for (int i = 0; i < op.Operands.Count; i++) {
				if (valueReplacements.TryGetValue(op.Operands[i], out var replacement)) {
					op.Operands[i] = replacement;
				}
			}
		}

		// Remove redundant operations
		foreach (var op in opsToRemove) {
			block.Operations.Remove(op);
		}

		return (changed, opsToRemove.Count);
	}

	private static ExpressionKey? ComputeExpressionKey(MlirOperation op) {
		// Only CSE pure operations
		if (op.HasSideEffects) return null;

		// Build a key from: opcode + operand IDs + attributes
		return new ExpressionKey(
			op.GetType().FullName ?? op.Mnemonic,
			[.. op.Operands.Select(o => o.Id)],
			[.. op.Attributes.Select(a => a.ToString()!)]
		);
	}

	private sealed record ExpressionKey(string Opcode, int[] OperandIds, string[] Attributes) {
		public bool Equals(ExpressionKey? other) {
			if (other is null) return false;
			if (Opcode != other.Opcode) return false;
			if (OperandIds.Length != other.OperandIds.Length) return false;
			if (Attributes.Length != other.Attributes.Length) return false;

			for (int i = 0; i < OperandIds.Length; i++) {
				if (OperandIds[i] != other.OperandIds[i]) return false;
			}

			for (int i = 0; i < Attributes.Length; i++) {
				if (Attributes[i] != other.Attributes[i]) return false;
			}

			return true;
		}

		public override int GetHashCode() {
			var hash = new HashCode();
			hash.Add(Opcode);
			foreach (var id in OperandIds) hash.Add(id);
			foreach (var attr in Attributes) hash.Add(attr);
			return hash.ToHashCode();
		}
	}
}
