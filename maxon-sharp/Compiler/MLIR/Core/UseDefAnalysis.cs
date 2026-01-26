namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// Provides efficient use-def chain analysis for MLIR values within a function.
/// This analysis builds a mapping from values to their uses, enabling quick queries
/// without scanning all operations.
/// </summary>
public sealed class UseDefAnalysis {
	private readonly Dictionary<MlirValue, List<MlirOperation>> _uses = [];
	private readonly Dictionary<MlirValue, MlirOperation?> _defs = [];

	private UseDefAnalysis() { }

	/// <summary>
	/// Builds a use-def analysis for the given function.
	/// Analyzes all blocks and operations to construct the use-def chains.
	/// </summary>
	/// <param name="func">The function to analyze.</param>
	/// <returns>A new UseDefAnalysis instance containing the analysis results.</returns>
	public static UseDefAnalysis Build(MlirFunction func) {
		var analysis = new UseDefAnalysis();

		Logger.Debug(LogCategory.Optimizer, $"UseDefAnalysis: building for function {func.Name}");

		int totalValues = 0;
		int totalUses = 0;

		// Process all blocks in the function
		foreach (var block in func.Body.Blocks) {
			// Register block arguments as definitions (with no defining operation)
			foreach (var arg in block.Arguments) {
				analysis._defs[arg.Value] = null;
				analysis._uses[arg.Value] = [];
				totalValues++;
				Logger.Trace(LogCategory.Optimizer, $"  registered block arg %{arg.Value.Id} from ^{block.Name}");
			}

			// Process all operations in the block
			foreach (var op in block.Operations) {
				// Register operation results as definitions
				foreach (var result in op.Results) {
					analysis._defs[result] = op;
					analysis._uses[result] = [];
					totalValues++;
					Logger.Trace(LogCategory.Optimizer, $"  registered result %{result.Id} from {op.Mnemonic}");
				}

				// Register operand uses
				foreach (var operand in op.Operands) {
					if (!analysis._uses.TryGetValue(operand, out var useList)) {
						// Value not yet seen (may be from outer scope or external)
						useList = [];
						analysis._uses[operand] = useList;
					}
					useList.Add(op);
					totalUses++;
					Logger.Trace(LogCategory.Optimizer, $"  registered use of %{operand.Id} in {op.Mnemonic}");
				}

				// Process nested regions recursively
				foreach (var region in op.Regions) {
					analysis.AnalyzeRegion(region, ref totalValues, ref totalUses);
				}
			}
		}

		Logger.Debug(LogCategory.Optimizer, $"  completed: {totalValues} values, {totalUses} uses");

		return analysis;
	}

	/// <summary>
	/// Analyzes a nested region and adds its values and uses to this analysis.
	/// </summary>
	private void AnalyzeRegion(MlirRegion region, ref int totalValues, ref int totalUses) {
		foreach (var block in region.Blocks) {
			// Register block arguments
			foreach (var arg in block.Arguments) {
				_defs[arg.Value] = null;
				_uses[arg.Value] = [];
				totalValues++;
				Logger.Trace(LogCategory.Optimizer, $"  registered nested block arg %{arg.Value.Id} from ^{block.Name}");
			}

			// Process operations
			foreach (var op in block.Operations) {
				// Register results
				foreach (var result in op.Results) {
					_defs[result] = op;
					_uses[result] = [];
					totalValues++;
				}

				// Register uses
				foreach (var operand in op.Operands) {
					if (!_uses.TryGetValue(operand, out var useList)) {
						useList = [];
						_uses[operand] = useList;
					}
					useList.Add(op);
					totalUses++;
				}

				// Recurse into nested regions
				foreach (var nestedRegion in op.Regions) {
					AnalyzeRegion(nestedRegion, ref totalValues, ref totalUses);
				}
			}
		}
	}

	/// <summary>
	/// Gets all operations that use the given value.
	/// </summary>
	/// <param name="value">The value to query.</param>
	/// <returns>A read-only list of operations using this value, or an empty list if the value has no uses.</returns>
	public IReadOnlyList<MlirOperation> GetUses(MlirValue value) {
		if (_uses.TryGetValue(value, out var uses)) {
			return uses;
		}
		return [];
	}

	/// <summary>
	/// Gets the operation that defines the given value.
	/// </summary>
	/// <param name="value">The value to query.</param>
	/// <returns>The defining operation, or null if the value is a block argument or not found.</returns>
	public MlirOperation? GetDef(MlirValue value) {
		// First check our cached analysis
		if (_defs.TryGetValue(value, out var def)) {
			return def;
		}
		// Fall back to the value's own DefiningOp property
		return value.DefiningOp;
	}

	/// <summary>
	/// Checks whether the given value has any uses.
	/// </summary>
	/// <param name="value">The value to query.</param>
	/// <returns>True if the value has at least one use, false otherwise.</returns>
	public bool HasUses(MlirValue value) {
		if (_uses.TryGetValue(value, out var uses)) {
			return uses.Count > 0;
		}
		return false;
	}

	/// <summary>
	/// Gets the number of operations using the given value.
	/// </summary>
	/// <param name="value">The value to query.</param>
	/// <returns>The number of uses, or 0 if the value has no uses.</returns>
	public int GetUseCount(MlirValue value) {
		if (_uses.TryGetValue(value, out var uses)) {
			return uses.Count;
		}
		return 0;
	}

	/// <summary>
	/// Checks whether the given value is used exactly once.
	/// This is useful for optimization passes that can inline single-use values.
	/// </summary>
	/// <param name="value">The value to query.</param>
	/// <returns>True if the value has exactly one use, false otherwise.</returns>
	public bool IsOnlyUsedOnce(MlirValue value) {
		return GetUseCount(value) == 1;
	}

	/// <summary>
	/// Checks whether the given value is dead (has no uses).
	/// </summary>
	/// <param name="value">The value to query.</param>
	/// <returns>True if the value has no uses, false otherwise.</returns>
	public bool IsDead(MlirValue value) {
		return !HasUses(value);
	}

	/// <summary>
	/// Gets all values tracked by this analysis.
	/// </summary>
	/// <returns>An enumerable of all tracked values.</returns>
	public IEnumerable<MlirValue> GetAllValues() {
		return _uses.Keys;
	}

	/// <summary>
	/// Gets all values that have no uses (dead values).
	/// </summary>
	/// <returns>An enumerable of dead values.</returns>
	public IEnumerable<MlirValue> GetDeadValues() {
		foreach (var (value, uses) in _uses) {
			if (uses.Count == 0) {
				yield return value;
			}
		}
	}

	/// <summary>
	/// Checks whether a value is defined by a block argument.
	/// </summary>
	/// <param name="value">The value to query.</param>
	/// <returns>True if the value is a block argument, false otherwise.</returns>
	public static bool IsBlockArgument(MlirValue value) {
		return value.DefiningBlockArg is not null;
	}

	/// <summary>
	/// Gets all uses of a value within a specific block.
	/// </summary>
	/// <param name="value">The value to query.</param>
	/// <param name="block">The block to search within.</param>
	/// <returns>An enumerable of operations in the block that use the value.</returns>
	public IEnumerable<MlirOperation> GetUsesInBlock(MlirValue value, MlirBlock block) {
		if (_uses.TryGetValue(value, out var uses)) {
			foreach (var use in uses) {
				if (use.ParentBlock == block) {
					yield return use;
				}
			}
		}
	}
}
