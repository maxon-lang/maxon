namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// Computes and queries dominance relationships between basic blocks.
/// A block D dominates a block B if every path from the entry block to B
/// must pass through D.
/// </summary>
public sealed class DominatorTree {
	private readonly MlirFunction _function;
	private readonly MlirBlock _entryBlock;
	private readonly Dictionary<MlirBlock, MlirBlock?> _idom = [];
	private readonly Dictionary<MlirBlock, HashSet<MlirBlock>> _dominatedBy = [];
	private readonly Dictionary<MlirBlock, HashSet<MlirBlock>> _dominanceFrontier = [];
	private readonly Dictionary<MlirBlock, int> _postorderIndex = [];

	private DominatorTree(MlirFunction function, MlirBlock entryBlock) {
		_function = function;
		_entryBlock = entryBlock;
	}

	/// <summary>
	/// Builds a dominator tree for the given function.
	/// </summary>
	/// <param name="func">The function to analyze.</param>
	/// <returns>The computed dominator tree, or null if the function has no entry block.</returns>
	public static DominatorTree? Build(MlirFunction func) {
		if (func.EntryBlock is null) {
			Logger.Debug(LogCategory.Optimizer, $"DominatorTree: function {func.Name} has no entry block");
			return null;
		}

		Logger.Debug(LogCategory.Optimizer, $"DominatorTree: building for {func.Name} ({func.Body.Blocks.Count} blocks)");

		var tree = new DominatorTree(func, func.EntryBlock);
		tree.ComputeDominators();
		tree.ComputeDominanceFrontiers();

		Logger.Trace(LogCategory.Optimizer, $"DominatorTree: computed dominators for {tree._idom.Count} blocks");

		return tree;
	}

	/// <summary>
	/// Gets the immediate dominator of a block, or null if block is the entry block.
	/// </summary>
	/// <param name="block">The block to query.</param>
	/// <returns>The immediate dominator, or null for the entry block.</returns>
	public MlirBlock? GetImmediateDominator(MlirBlock block) {
		return _idom.GetValueOrDefault(block);
	}

	/// <summary>
	/// Returns true if 'dominator' dominates 'block'.
	/// A block always dominates itself.
	/// </summary>
	/// <param name="dominator">The potential dominator block.</param>
	/// <param name="block">The block to check.</param>
	/// <returns>True if dominator dominates block.</returns>
	public bool Dominates(MlirBlock dominator, MlirBlock block) {
		if (dominator == block) return true;

		// Walk up the dominator tree from block to see if we reach dominator
		var current = block;
		while (current is not null) {
			if (current == dominator) return true;
			current = GetImmediateDominator(current);
		}

		return false;
	}

	/// <summary>
	/// Returns true if 'dominator' strictly dominates 'block'.
	/// Strict dominance excludes self-dominance.
	/// </summary>
	/// <param name="dominator">The potential dominator block.</param>
	/// <param name="block">The block to check.</param>
	/// <returns>True if dominator strictly dominates block.</returns>
	public bool StrictlyDominates(MlirBlock dominator, MlirBlock block) {
		return dominator != block && Dominates(dominator, block);
	}

	/// <summary>
	/// Gets the dominance frontier of a block.
	/// The dominance frontier of a block B is the set of all blocks Y where
	/// B dominates a predecessor of Y but does not strictly dominate Y.
	/// </summary>
	/// <param name="block">The block to query.</param>
	/// <returns>The set of blocks in the dominance frontier.</returns>
	public IReadOnlySet<MlirBlock> GetDominanceFrontier(MlirBlock block) {
		return _dominanceFrontier.GetValueOrDefault(block) ?? (IReadOnlySet<MlirBlock>)new HashSet<MlirBlock>();
	}

	/// <summary>
	/// Gets all blocks dominated by the given block (including itself).
	/// </summary>
	/// <param name="block">The dominator block.</param>
	/// <returns>All blocks that are dominated by this block.</returns>
	public IReadOnlySet<MlirBlock> GetDominatedBlocks(MlirBlock block) {
		if (_dominatedBy.TryGetValue(block, out var dominated)) {
			return dominated;
		}

		// Compute lazily by walking through all blocks
		var result = new HashSet<MlirBlock>();
		foreach (var b in _function.Body.Blocks) {
			if (Dominates(block, b)) {
				result.Add(b);
			}
		}

		_dominatedBy[block] = result;
		return result;
	}

	/// <summary>
	/// Gets the entry block of the function.
	/// </summary>
	public MlirBlock EntryBlock => _entryBlock;

	/// <summary>
	/// Computes dominators using the iterative dataflow algorithm.
	/// Based on Cooper, Harvey, and Kennedy's "A Simple, Fast Dominance Algorithm".
	/// </summary>
	private void ComputeDominators() {
		var blocks = _function.Body.Blocks;

		// Compute reverse postorder traversal for efficient iteration
		var reversePostorder = ComputeReversePostorder();

		Logger.Trace(LogCategory.Optimizer, $"DominatorTree: reverse postorder: {string.Join(", ", reversePostorder.Select(b => b.Name))}");

		// Initialize: entry block has no idom, all others are undefined
		_idom[_entryBlock] = null;

		// Iterate until fixed point
		bool changed = true;
		int iterations = 0;

		while (changed) {
			changed = false;
			iterations++;

			Logger.Trace(LogCategory.Optimizer, $"DominatorTree: iteration {iterations}");

			// Process blocks in reverse postorder (skip entry block)
			foreach (var block in reversePostorder) {
				if (block == _entryBlock) continue;

				var predecessors = block.Predecessors.ToList();
				if (predecessors.Count == 0) {
					// Unreachable block - skip
					continue;
				}

				// Find the first processed predecessor
				MlirBlock? newIdom = null;
				foreach (var pred in predecessors) {
					if (_idom.ContainsKey(pred)) {
						newIdom = pred;
						break;
					}
				}

				if (newIdom is null) {
					// No processed predecessors yet
					continue;
				}

				// Intersect with other processed predecessors
				foreach (var pred in predecessors) {
					if (pred == newIdom) continue;
					if (_idom.ContainsKey(pred)) {
						newIdom = Intersect(pred, newIdom);
					}
				}

				// Check if idom changed
				if (!_idom.TryGetValue(block, out var oldIdom) || oldIdom != newIdom) {
					_idom[block] = newIdom;
					changed = true;
					Logger.Trace(LogCategory.Optimizer, $"DominatorTree: idom({block.Name}) = {newIdom?.Name ?? "null"}");
				}
			}
		}

		Logger.Debug(LogCategory.Optimizer, $"DominatorTree: converged after {iterations} iterations");
	}

	/// <summary>
	/// Computes the intersection of two dominators in the dominator tree.
	/// Returns the nearest common dominator of the two blocks.
	/// </summary>
	private MlirBlock Intersect(MlirBlock b1, MlirBlock b2) {
		var finger1 = b1;
		var finger2 = b2;

		while (finger1 != finger2) {
			while (_postorderIndex.GetValueOrDefault(finger1, -1) < _postorderIndex.GetValueOrDefault(finger2, -1)) {
				var idom = _idom.GetValueOrDefault(finger1);
				if (idom is null) break;
				finger1 = idom;
			}

			while (_postorderIndex.GetValueOrDefault(finger2, -1) < _postorderIndex.GetValueOrDefault(finger1, -1)) {
				var idom = _idom.GetValueOrDefault(finger2);
				if (idom is null) break;
				finger2 = idom;
			}
		}

		return finger1;
	}

	/// <summary>
	/// Computes reverse postorder traversal of the CFG.
	/// </summary>
	private List<MlirBlock> ComputeReversePostorder() {
		var postorder = new List<MlirBlock>();
		var visited = new HashSet<MlirBlock>();

		void Visit(MlirBlock block) {
			if (!visited.Add(block)) return;

			foreach (var succ in block.Successors) {
				Visit(succ);
			}

			_postorderIndex[block] = postorder.Count;
			postorder.Add(block);
		}

		Visit(_entryBlock);

		// Reverse to get reverse postorder
		postorder.Reverse();
		return postorder;
	}

	/// <summary>
	/// Computes dominance frontiers for all blocks.
	/// Uses the algorithm from Cytron et al.
	/// </summary>
	private void ComputeDominanceFrontiers() {
		// Initialize empty frontiers for all blocks
		foreach (var block in _function.Body.Blocks) {
			_dominanceFrontier[block] = [];
		}

		foreach (var block in _function.Body.Blocks) {
			var predecessors = block.Predecessors.ToList();

			// Blocks with multiple predecessors contribute to dominance frontiers
			if (predecessors.Count >= 2) {
				foreach (var pred in predecessors) {
					var runner = pred;

					// Walk up the dominator tree from pred to idom(block)
					while (runner is not null && runner != GetImmediateDominator(block)) {
						_dominanceFrontier[runner].Add(block);
						Logger.Trace(LogCategory.Optimizer, $"DominatorTree: DF({runner.Name}) += {block.Name}");
						runner = GetImmediateDominator(runner);
					}
				}
			}
		}
	}

	/// <summary>
	/// Returns a string representation of the dominator tree for debugging.
	/// </summary>
	public override string ToString() {
		var lines = new List<string> { $"DominatorTree for {_function.Name}:" };

		foreach (var block in _function.Body.Blocks) {
			var idom = GetImmediateDominator(block);
			var idomName = idom?.Name ?? "(none)";
			var df = GetDominanceFrontier(block);
			var dfNames = df.Count > 0 ? string.Join(", ", df.Select(b => b.Name)) : "(empty)";

			lines.Add($"  {block.Name}: idom={idomName}, DF={{{dfNames}}}");
		}

		return string.Join("\n", lines);
	}
}
