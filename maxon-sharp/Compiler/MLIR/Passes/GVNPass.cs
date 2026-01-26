using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Global Value Numbering (GVN) pass for cross-block redundancy elimination.
///
/// Unlike local CSE which only eliminates redundant expressions within a single block,
/// GVN uses dominance information to eliminate redundant computations across basic blocks.
/// An expression computed in block B is available in all blocks dominated by B.
/// </summary>
public sealed class GVNPass : FunctionPass {
	public override string Name => "gvn";
	public override string Description => "Global Value Numbering for redundancy elimination";

	protected override bool RunOnFunction(MlirFunction func) {
		Logger.Debug(LogCategory.Optimizer, $"gvn: processing {func.Name}");

		var domTree = DominatorTree.Build(func);
		if (domTree is null) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: no entry block, skipping");
			return false;
		}

		var gvn = new GVNContext(func, domTree);
		int eliminated = gvn.Run();

		if (eliminated > 0) {
			Logger.Debug(LogCategory.Optimizer, $"  {func.Name}: eliminated {eliminated} redundant expression(s)");
		}

		return eliminated > 0;
	}

	/// <summary>
	/// Context for GVN analysis and transformation within a single function.
	/// </summary>
	private sealed class GVNContext(MlirFunction function, DominatorTree domTree) {
		private readonly MlirFunction _function = function;
		private readonly DominatorTree _domTree = domTree;

		// Maps expression keys to their value numbers
		private readonly Dictionary<ExpressionKey, int> _exprToVN = [];

		// Maps value numbers back to the canonical value (first computed result)
		private readonly Dictionary<int, MlirValue> _vnToValue = [];

		// Maps value numbers to the block where the canonical value is defined
		private readonly Dictionary<int, MlirBlock> _vnToBlock = [];

		// Maps each MlirValue to its value number
		private readonly Dictionary<MlirValue, int> _valueToVN = [];

		// Next available value number
		private int _nextVN;

		// Operations to remove after processing
		private readonly List<(MlirBlock block, MlirOperation op)> _opsToRemove = [];

		// Value replacements to apply
		private readonly Dictionary<MlirValue, MlirValue> _replacements = [];

		/// <summary>
		/// Runs the GVN algorithm and returns the number of eliminated expressions.
		/// </summary>
		public int Run() {
			// Assign value numbers to block arguments first
			// (they are inputs with unique value numbers)
			foreach (var block in _function.Body.Blocks) {
				foreach (var arg in block.Arguments) {
					AssignFreshValueNumber(arg.Value, block);
				}
			}

			// Process blocks in dominator tree preorder
			// This ensures we see definitions before uses in dominated blocks
			var preorder = ComputeDomTreePreorder();

			foreach (var block in preorder) {
				ProcessBlock(block);
			}

			// Apply replacements and remove redundant operations
			ApplyReplacements();
			RemoveDeadOperations();

			return _opsToRemove.Count;
		}

		/// <summary>
		/// Computes a preorder traversal of the dominator tree.
		/// </summary>
		private List<MlirBlock> ComputeDomTreePreorder() {
			var result = new List<MlirBlock>();
			var visited = new HashSet<MlirBlock>();

			void Visit(MlirBlock block) {
				if (!visited.Add(block)) return;
				result.Add(block);

				// Visit children in dominator tree (blocks where this block is the immediate dominator)
				foreach (var b in _function.Body.Blocks) {
					if (_domTree.GetImmediateDominator(b) == block) {
						Visit(b);
					}
				}
			}

			Visit(_domTree.EntryBlock);
			return result;
		}

		/// <summary>
		/// Processes all operations in a block for GVN.
		/// </summary>
		private void ProcessBlock(MlirBlock block) {
			foreach (var op in block.Operations) {
				// Skip operations that cannot be value-numbered
				if (!CanValueNumber(op)) {
					// Still assign fresh value numbers to results for use as operands
					foreach (var result in op.Results) {
						AssignFreshValueNumber(result, block);
					}
					continue;
				}

				// Compute the expression key using value numbers of operands
				var key = ComputeExpressionKey(op);
				if (key is null) {
					foreach (var result in op.Results) {
						AssignFreshValueNumber(result, block);
					}
					continue;
				}

				if (_exprToVN.TryGetValue(key, out int existingVN)) {
					// Found an expression with the same value number
					var canonicalValue = _vnToValue[existingVN];
					var canonicalBlock = _vnToBlock[existingVN];
					var redundantValue = op.Results[0];

					// For cross-block elimination, we need the canonical block to dominate this block.
					// However, our register allocator doesn't handle cross-block live ranges properly -
					// it processes blocks independently and doesn't maintain values across blocks except
					// through explicit block arguments. So we can only safely eliminate within the same block.
					if (canonicalBlock == block) {
						// Same block - safe to eliminate
						Logger.Trace(LogCategory.Optimizer,
							$"  gvn: redundant {op.Mnemonic} %{redundantValue.Id} -> %{canonicalValue.Id} (VN={existingVN})");

						// Map the redundant result to the same value number
						_valueToVN[redundantValue] = existingVN;

						// Record replacement
						_replacements[redundantValue] = canonicalValue;

						// Mark operation for removal
						_opsToRemove.Add((block, op));
					} else if (_domTree.Dominates(canonicalBlock, block)) {
						// Cross-block dominance holds, but we can't eliminate due to register allocator limitations
						Logger.Trace(LogCategory.Optimizer,
							$"  gvn: skipping cross-block elimination of {op.Mnemonic} %{redundantValue.Id} (VN={existingVN}): " +
							$"canonical block {canonicalBlock.Name} != current block {block.Name}");

						// Assign the same VN but don't replace or remove
						_valueToVN[redundantValue] = existingVN;
					} else {
						// Dominance does not hold - same expression but cannot eliminate
						Logger.Trace(LogCategory.Optimizer,
							$"  gvn: skipping elimination of {op.Mnemonic} %{redundantValue.Id} (VN={existingVN}): " +
							$"canonical block {canonicalBlock.Name} does not dominate {block.Name}");

						// Assign the same VN but don't replace or remove
						_valueToVN[redundantValue] = existingVN;
					}
				} else {
					// First occurrence of this expression - assign new value number
					int vn = _nextVN++;
					_exprToVN[key] = vn;
					_valueToVN[op.Results[0]] = vn;
					_vnToValue[vn] = op.Results[0];
					_vnToBlock[vn] = block;

					Logger.Trace(LogCategory.Optimizer,
						$"  gvn: new expression {op.Mnemonic} %{op.Results[0].Id} -> VN={vn}");
				}
			}
		}

		/// <summary>
		/// Determines if an operation can be value-numbered.
		/// </summary>
		private static bool CanValueNumber(MlirOperation op) {
			// Must be pure (no side effects)
			if (op.HasSideEffects) return false;

			// Must not be a terminator
			if (op.IsTerminator) return false;

			// Must have exactly one result
			if (op.Results.Count != 1) return false;

			// Skip memory operations (loads could be aliased)
			// Memory operations typically have HasSideEffects=true, but be explicit
			if (IsMemoryOperation(op)) return false;

			return true;
		}

		/// <summary>
		/// Checks if an operation is a memory operation that cannot be safely eliminated.
		/// </summary>
		private static bool IsMemoryOperation(MlirOperation op) {
			// Check by mnemonic for common memory operations
			return op.Mnemonic is "load" or "store" or "alloca" or "get_global";
		}

		/// <summary>
		/// Computes an expression key for GVN using value numbers of operands.
		/// </summary>
		private ExpressionKey? ComputeExpressionKey(MlirOperation op) {
			// Build operand value numbers
			var operandVNs = new List<int>(op.Operands.Count);

			foreach (var operand in op.Operands) {
				if (!_valueToVN.TryGetValue(operand, out int vn)) {
					// Operand has no value number - this can happen for values
					// from unreachable blocks or external references
					return null;
				}
				operandVNs.Add(vn);
			}

			// Build attribute strings for operations with attributes that affect the result
			var attrStrs = op.Attributes
				.Select(kv => $"{kv.Key}={kv.Value}")
				.ToList();

			return new ExpressionKey(
				op.GetType().FullName ?? op.Mnemonic,
				operandVNs,
				attrStrs
			);
		}

		/// <summary>
		/// Assigns a fresh value number to a value.
		/// </summary>
		private void AssignFreshValueNumber(MlirValue value, MlirBlock block) {
			int vn = _nextVN++;
			_valueToVN[value] = vn;
			_vnToValue[vn] = value;
			_vnToBlock[vn] = block;
		}

		/// <summary>
		/// Applies value replacements to all operands in the function.
		/// </summary>
		private void ApplyReplacements() {
			if (_replacements.Count == 0) return;

			foreach (var block in _function.Body.Blocks) {
				foreach (var op in block.Operations) {
					for (int i = 0; i < op.Operands.Count; i++) {
						if (_replacements.TryGetValue(op.Operands[i], out var replacement)) {
							op.Operands[i] = replacement;
						}
					}

					}
			}
		}

		/// <summary>
		/// Removes operations that were marked as redundant.
		/// </summary>
		private void RemoveDeadOperations() {
			foreach (var (block, op) in _opsToRemove) {
				block.Operations.Remove(op);
			}
		}
	}

	/// <summary>
	/// Key for identifying equivalent expressions in GVN.
	/// Two expressions are equivalent if they have the same opcode, operand value numbers, and attributes.
	/// </summary>
	private sealed class ExpressionKey(string opcode, List<int> operandVNs, List<string> attributes) : IEquatable<ExpressionKey> {
		public string Opcode { get; } = opcode;
		public List<int> OperandVNs { get; } = operandVNs;
		public List<string> Attributes { get; } = attributes;

		public bool Equals(ExpressionKey? other) {
			if (other is null) return false;
			if (Opcode != other.Opcode) return false;
			if (OperandVNs.Count != other.OperandVNs.Count) return false;
			if (Attributes.Count != other.Attributes.Count) return false;

			for (int i = 0; i < OperandVNs.Count; i++) {
				if (OperandVNs[i] != other.OperandVNs[i]) return false;
			}

			for (int i = 0; i < Attributes.Count; i++) {
				if (Attributes[i] != other.Attributes[i]) return false;
			}

			return true;
		}

		public override bool Equals(object? obj) => Equals(obj as ExpressionKey);

		public override int GetHashCode() {
			var hash = new HashCode();
			hash.Add(Opcode);
			foreach (var vn in OperandVNs) hash.Add(vn);
			foreach (var attr in Attributes) hash.Add(attr);
			return hash.ToHashCode();
		}
	}
}
