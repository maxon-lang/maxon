namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// Information about a loop's position in the loop nesting hierarchy.
/// </summary>
public sealed class LoopNestInfo {
	/// <summary>
	/// The parent (enclosing) loop, or null if this is an outermost loop.
	/// </summary>
	public LoopInfo? Parent { get; init; }

	/// <summary>
	/// Immediate child (nested) loops.
	/// </summary>
	public List<LoopInfo> Children { get; } = [];

	/// <summary>
	/// The loop depth (0 = outermost, 1 = one level nested, etc.)
	/// </summary>
	public int Depth { get; init; }
}

/// <summary>
/// Represents a detected natural loop in the CFG.
/// A natural loop consists of a header block (the single entry point),
/// one or more latch blocks (blocks with back-edges to the header),
/// the loop body (all blocks dominated by the header that can reach a latch),
/// and optionally a preheader and exit block.
/// </summary>
public sealed class LoopInfo {
	/// <summary>
	/// The loop header - the single entry point to the loop.
	/// All back-edges target this block.
	/// </summary>
	public required MlirBlock Header { get; init; }

	/// <summary>
	/// The latch block - the block containing the back-edge to the header.
	/// For loops with multiple back-edges, this is the last one encountered.
	/// </summary>
	public required MlirBlock Latch { get; init; }

	/// <summary>
	/// The exit block - the first block outside the loop that is a successor
	/// of a block inside the loop. May be null for infinite loops.
	/// </summary>
	public required MlirBlock? Exit { get; init; }

	/// <summary>
	/// The set of all blocks in the loop body, including the header and latch.
	/// </summary>
	public required HashSet<MlirBlock> Body { get; init; }

	/// <summary>
	/// The preheader block - the single predecessor of the header that is
	/// outside the loop. May be null if the header has multiple non-loop predecessors.
	/// </summary>
	public required MlirBlock? Preheader { get; init; }

	/// <summary>
	/// Checks whether a block is contained within this loop.
	/// </summary>
	public bool Contains(MlirBlock block) => Body.Contains(block);
}

/// <summary>
/// Static utility class for detecting and analyzing natural loops in a function's CFG.
/// Uses back-edge detection to identify loop structures.
/// </summary>
public static class LoopAnalysis {
	/// <summary>
	/// Detects all natural loops in a function.
	/// A natural loop is identified by finding back-edges (edges from a block to
	/// a block that appears earlier in the CFG order).
	/// </summary>
	/// <param name="func">The function to analyze.</param>
	/// <returns>A list of detected loops, ordered by discovery.</returns>
	public static List<LoopInfo> DetectLoops(MlirFunction func) {
		var loops = new List<LoopInfo>();
		var blockIndices = BuildBlockIndexMap(func);
		var backEdgesByHeader = FindBackEdges(func, blockIndices);

		foreach (var (header, latches) in backEdgesByHeader) {
			// Skip exit and merge blocks masquerading as loop headers
			// These can appear as loop headers when block ordering puts them before their predecessors
			if (header.Name.Contains("exit") || header.Name.StartsWith("merge")) {
				Logger.Trace(LogCategory.Optimizer, $"    loop-analysis: skipping spurious loop with exit/merge block as header: ^{header.Name}");
				continue;
			}

			var loop = BuildLoopInfo(header, latches);
			if (loop != null) {
				loops.Add(loop);
				Logger.Trace(LogCategory.Optimizer, $"    loop-analysis: detected loop: header=^{loop.Header.Name}, latches={latches.Count}, body={loop.Body.Count} blocks");
			}
		}

		return loops;
	}

	/// <summary>
	/// Builds a mapping from blocks to their index in the function's block list.
	/// Used for determining back-edges (edges to earlier blocks).
	/// </summary>
	public static Dictionary<MlirBlock, int> BuildBlockIndexMap(MlirFunction func) {
		var indices = new Dictionary<MlirBlock, int>();
		for (int i = 0; i < func.Body.Blocks.Count; i++) {
			indices[func.Body.Blocks[i]] = i;
		}
		return indices;
	}

	/// <summary>
	/// Finds all back-edges in the CFG, grouped by their target (header) block.
	/// A back-edge is an edge from a block to a block with equal or lower index.
	/// </summary>
	public static Dictionary<MlirBlock, List<MlirBlock>> FindBackEdges(
		MlirFunction func,
		Dictionary<MlirBlock, int> blockIndices) {

		var backEdgesByHeader = new Dictionary<MlirBlock, List<MlirBlock>>();

		foreach (var block in func.Body.Blocks) {
			foreach (var succ in block.Successors) {
				if (blockIndices[succ] <= blockIndices[block]) {
					if (!backEdgesByHeader.TryGetValue(succ, out var latches)) {
						latches = [];
						backEdgesByHeader[succ] = latches;
					}
					latches.Add(block);
				}
			}
		}

		return backEdgesByHeader;
	}

	/// <summary>
	/// Constructs a LoopInfo from a header and its latch blocks.
	/// </summary>
	public static LoopInfo? BuildLoopInfo(MlirBlock header, List<MlirBlock> latches) {
		var body = CollectLoopBody(header, latches);
		var exit = FindLoopExit(header, body);
		var preheader = FindLoopPreheader(header, body);

		return new LoopInfo {
			Header = header,
			Latch = latches[^1],
			Exit = exit,
			Body = body,
			Preheader = preheader
		};
	}

	/// <summary>
	/// Collects all blocks in the loop body by walking backwards from latches to header.
	/// The loop body contains all blocks that can reach a latch without leaving the header-dominated region.
	/// </summary>
	public static HashSet<MlirBlock> CollectLoopBody(MlirBlock header, List<MlirBlock> latches) {
		var body = new HashSet<MlirBlock> { header };
		var worklist = new Stack<MlirBlock>();

		foreach (var latch in latches) {
			worklist.Push(latch);
		}

		while (worklist.Count > 0) {
			var block = worklist.Pop();
			if (body.Add(block)) {
				foreach (var pred in block.Predecessors) {
					if (!body.Contains(pred)) {
						worklist.Push(pred);
					}
				}
			}
		}

		return body;
	}

	/// <summary>
	/// Finds the loop exit block - the first successor of the header that is outside the loop body.
	/// </summary>
	public static MlirBlock? FindLoopExit(MlirBlock header, HashSet<MlirBlock> body) {
		foreach (var succ in header.Successors) {
			if (!body.Contains(succ)) return succ;
		}
		return null;
	}

	/// <summary>
	/// Finds the loop preheader - a predecessor of the header that is outside the loop body.
	/// Returns null if there are multiple non-loop predecessors (no unique preheader).
	/// </summary>
	public static MlirBlock? FindLoopPreheader(MlirBlock header, HashSet<MlirBlock> body) {
		foreach (var pred in header.Predecessors) {
			if (!body.Contains(pred)) return pred;
		}
		return null;
	}

	/// <summary>
	/// Finds the innermost loop containing all the specified blocks.
	/// Returns null if no single loop contains all blocks.
	/// </summary>
	public static LoopInfo? FindContainingLoop(List<LoopInfo> loops, HashSet<MlirBlock> blocks) {
		LoopInfo? best = null;
		int bestSize = int.MaxValue;

		foreach (var loop in loops) {
			var validBlocks = loop.Body.ToHashSet();
			if (loop.Preheader != null) validBlocks.Add(loop.Preheader);
			if (loop.Exit != null) validBlocks.Add(loop.Exit);

			if (blocks.All(b => validBlocks.Contains(b)) && loop.Body.Count < bestSize) {
				best = loop;
				bestSize = loop.Body.Count;
			}
		}

		return best;
	}

	/// <summary>
	/// Checks if a block is inside any of the provided loops.
	/// </summary>
	public static bool IsInAnyLoop(MlirBlock block, List<LoopInfo> loops) {
		return loops.Any(loop => loop.Body.Contains(block));
	}

	/// <summary>
	/// Gets all loops that contain a specific block.
	/// </summary>
	public static IEnumerable<LoopInfo> GetLoopsContaining(MlirBlock block, List<LoopInfo> loops) {
		return loops.Where(loop => loop.Body.Contains(block));
	}

	/// <summary>
	/// Builds loop nesting information from a flat list of loops.
	/// A loop A is nested inside loop B if A's header is contained in B's body.
	/// </summary>
	public static Dictionary<LoopInfo, LoopNestInfo> BuildLoopNest(List<LoopInfo> loops) {
		var nestInfo = new Dictionary<LoopInfo, LoopNestInfo>();

		// Initialize all loops with no parent
		foreach (var loop in loops) {
			nestInfo[loop] = new LoopNestInfo { Parent = null, Depth = 0 };
		}

		// Find parent for each loop - the smallest enclosing loop
		foreach (var loop in loops) {
			LoopInfo? bestParent = null;
			int bestParentSize = int.MaxValue;

			foreach (var potentialParent in loops) {
				if (potentialParent == loop) continue;

				// Check if potentialParent contains this loop's header
				if (potentialParent.Body.Contains(loop.Header)) {
					// This is an enclosing loop - pick the smallest one (immediate parent)
					if (potentialParent.Body.Count < bestParentSize) {
						bestParent = potentialParent;
						bestParentSize = potentialParent.Body.Count;
					}
				}
			}

			if (bestParent != null) {
				nestInfo[loop] = new LoopNestInfo { Parent = bestParent, Depth = 0 };
				nestInfo[bestParent].Children.Add(loop);
			}
		}

		// Compute depths by walking up the parent chain
		foreach (var loop in loops) {
			int depth = 0;
			var current = nestInfo[loop].Parent;
			while (current != null) {
				depth++;
				current = nestInfo[current].Parent;
			}
			var info = nestInfo[loop];
			nestInfo[loop] = new LoopNestInfo { Parent = info.Parent, Depth = depth };
			// Preserve children list
			foreach (var child in info.Children) {
				nestInfo[loop].Children.Add(child);
			}
		}

		return nestInfo;
	}

	/// <summary>
	/// Returns loops sorted from innermost to outermost (highest depth first).
	/// This is the standard order for LICM processing.
	/// </summary>
	public static List<LoopInfo> GetLoopsInnerToOuter(
		List<LoopInfo> loops,
		Dictionary<LoopInfo, LoopNestInfo> nestInfo) {

		return [.. loops.OrderByDescending(l => nestInfo[l].Depth)];
	}

	/// <summary>
	/// Gets all header blocks of loops that enclose the given loop.
	/// These blocks define values that change per outer-loop iteration.
	/// </summary>
	public static HashSet<MlirBlock> GetOuterLoopHeaders(
		LoopInfo loop,
		Dictionary<LoopInfo, LoopNestInfo> nestInfo) {

		var headers = new HashSet<MlirBlock>();
		var current = nestInfo[loop].Parent;

		while (current != null) {
			headers.Add(current.Header);
			current = nestInfo[current].Parent;
		}

		return headers;
	}
}
