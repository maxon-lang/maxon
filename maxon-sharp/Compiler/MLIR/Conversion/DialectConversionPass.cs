using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Result of a dialect conversion pass.
/// </summary>
public enum ConversionResult {
	Success,
	Failure
}

/// <summary>
/// Drives dialect conversion by applying patterns to operations.
/// </summary>
public sealed class DialectConversionPass(ConversionPatternSet patterns) {
	private readonly ConversionPatternSet _patterns = patterns;
	private readonly HashSet<string> _legalDialects = [];
	private readonly HashSet<Type> _legalOps = [];

	/// <summary>
	/// Marks a dialect as legal (operations from this dialect don't need conversion).
	/// </summary>
	public void AddLegalDialect(string dialect) => _legalDialects.Add(dialect);

	/// <summary>
	/// Marks a specific operation type as legal.
	/// </summary>
	public void AddLegalOp<T>() where T : MlirOperation => _legalOps.Add(typeof(T));

	/// <summary>
	/// Returns true if an operation is legal (doesn't need conversion).
	/// </summary>
	public bool IsLegal(MlirOperation op) =>
		_legalDialects.Contains(op.Dialect) || _legalOps.Contains(op.GetType());

	/// <summary>
	/// Runs the conversion on a module.
	/// </summary>
	public ConversionResult Run(MlirModule module) {
		Logger.Debug(LogCategory.Mlir, $"Dialect conversion: processing {module.Functions.Count} functions");
		Logger.Trace(LogCategory.Mlir, $"Legal dialects: {string.Join(", ", _legalDialects)}");

		foreach (var func in module.Functions) {
			var result = ConvertFunction(func);
			if (result == ConversionResult.Failure) {
				Logger.Error(LogCategory.Mlir, $"Conversion failed for function: {func.Name}");
				return result;
			}
		}

		Logger.Debug(LogCategory.Mlir, "Dialect conversion: completed successfully");
		return ConversionResult.Success;
	}

	/// <summary>
	/// Runs the conversion on a function.
	/// </summary>
	public ConversionResult ConvertFunction(MlirFunction func) {
		Logger.Trace(LogCategory.Mlir, $"Converting function: {func.Name} ({func.Body.Blocks.Count} blocks)");
		int convertedCount = 0;
		var valueMappings = new Dictionary<MlirValue, MlirValue>();

		foreach (var block in func.Body.Blocks) {
			var (result, count) = ConvertBlock(block, valueMappings, func);
			if (result == ConversionResult.Failure)
				return result;
			convertedCount += count;
		}

		// After all blocks are converted, update value references
		UpdateValueReferences(func, valueMappings);

		if (convertedCount > 0) {
			Logger.Debug(LogCategory.Mlir, $"  {func.Name}: converted {convertedCount} operations");
		}

		return ConversionResult.Success;
	}

	private (ConversionResult result, int convertedCount) ConvertBlock(MlirBlock block, Dictionary<MlirValue, MlirValue> globalMappings, MlirFunction? func = null) {
		// Process operations in order, keeping track of which ones to remove
		var toRemove = new List<MlirOperation>();

		for (int i = 0; i < block.Operations.Count; i++) {
			var op = block.Operations[i];

			if (IsLegal(op))
				continue;

			// Find matching patterns
			var patterns = _patterns.GetMatching(op.GetType());
			bool converted = false;

			foreach (var pattern in patterns) {
				var rewriter = new ConversionPatternRewriter(block, i + 1, func);
				if (pattern.MatchAndRewrite(op, rewriter)) {
					Logger.Trace(LogCategory.Mlir, $"    {op.Mnemonic} -> {pattern.GetType().Name}");

					// Collect mappings
					foreach (var kv in rewriter.ValueMappings)
						globalMappings[kv.Key] = kv.Value;

					toRemove.Add(op);
					converted = true;

					// Adjust index based on how many operations were inserted
					i = block.Operations.IndexOf(op);
					break;
				}
			}

			if (!converted && !IsLegal(op)) {
				// Operation couldn't be converted and isn't legal
				Logger.Trace(LogCategory.Mlir, $"    {op.Mnemonic}: no matching pattern (skipped)");
			}
		}

		// Remove converted operations
		foreach (var op in toRemove) {
			block.RemoveOperation(op);
		}

		return (ConversionResult.Success, toRemove.Count);
	}

	private static void UpdateValueReferences(MlirFunction func, Dictionary<MlirValue, MlirValue> mappings) {
		if (mappings.Count == 0) return;

		// Walk all blocks and update operands
		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				for (int i = 0; i < op.Operands.Count; i++) {
					// Follow the mapping chain to get the final value
					var operand = op.Operands[i];
					while (mappings.TryGetValue(operand, out var mapped)) {
						operand = mapped;
					}
					if (operand != op.Operands[i]) {
						op.Operands[i] = operand;
					}
				}
			}
		}
	}
}
