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
		foreach (var func in module.Functions) {
			var result = ConvertFunction(func);
			if (result == ConversionResult.Failure)
				return result;
		}
		return ConversionResult.Success;
	}

	/// <summary>
	/// Runs the conversion on a function.
	/// </summary>
	public ConversionResult ConvertFunction(MlirFunction func) {
		foreach (var block in func.Body.Blocks) {
			var result = ConvertBlock(block);
			if (result == ConversionResult.Failure)
				return result;
		}

		// After all blocks are converted, update value references
		UpdateValueReferences(func);

		return ConversionResult.Success;
	}

	private ConversionResult ConvertBlock(MlirBlock block) {
		// Process operations in order, keeping track of which ones to remove
		var toRemove = new List<MlirOperation>();
		var allMappings = new Dictionary<MlirValue, MlirValue>();

		for (int i = 0; i < block.Operations.Count; i++) {
			var op = block.Operations[i];

			if (IsLegal(op))
				continue;

			// Find matching patterns
			var patterns = _patterns.GetMatching(op.GetType());
			bool converted = false;

			foreach (var pattern in patterns) {
				var rewriter = new ConversionPatternRewriter(block, i + 1);
				if (pattern.MatchAndRewrite(op, rewriter)) {
					// Collect mappings
					foreach (var kv in rewriter.ValueMappings)
						allMappings[kv.Key] = kv.Value;

					toRemove.Add(op);
					converted = true;

					// Adjust index based on how many operations were inserted
					i = block.Operations.IndexOf(op);
					break;
				}
			}

			if (!converted && !IsLegal(op)) {
				// Operation couldn't be converted and isn't legal
				// For now, we'll just skip it (partial conversion)
			}
		}

		// Remove converted operations
		foreach (var op in toRemove) {
			block.RemoveOperation(op);
		}

		return ConversionResult.Success;
	}

	private static void UpdateValueReferences(MlirFunction func) {
		// This is a simplified implementation
		// A full implementation would walk all operands and update mapped values
	}
}
