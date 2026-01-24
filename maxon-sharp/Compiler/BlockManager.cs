namespace MaxonSharp.Compiler;

/// <summary>
/// Types of blocks for control flow tracking.
/// </summary>
public enum BlockKind {
	Function,
	IfThen,
	IfElse,
	WhileLoop,
	ForLoop,
	MatchArm,
	Plain
}

/// <summary>
/// Tracks the state of a block for ownership/move tracking.
/// </summary>
public record BlockState {
	public BlockKind Kind { get; init; }
	public OwnershipSnapshot EntrySnapshot { get; init; }
	public bool Terminates { get; set; }
	public Dictionary<string, SourceLocation> Moves { get; } = [];
	public HashSet<string> Reassigns { get; } = [];

	public BlockState(BlockKind kind, OwnershipSnapshot entrySnapshot) {
		Kind = kind;
		EntrySnapshot = entrySnapshot;
		Terminates = false;
	}
}

/// <summary>
/// Represents a conflict when merging ownership from different branches.
/// </summary>
public record MoveConflict(string Name, BlockKind MovedInBranch, SourceLocation MovedAt);

/// <summary>
/// Result of merging ownership states from different branches.
/// </summary>
public record MergeResult(
	List<string> DefinitelyMoved,
	List<MoveConflict> Conflicts
);

/// <summary>
/// Manages control flow complexity for ownership tracking.
/// Tracks which variables are moved/reassigned in different branches and
/// validates that ownership states can be merged correctly.
/// </summary>
public class BlockManager {
	private readonly Stack<BlockState> _blocks = new();

	/// <summary>
	/// Enter a new block with the given kind and current ownership state.
	/// </summary>
	public void EnterBlock(BlockKind kind, OwnershipSnapshot current) {
		_blocks.Push(new BlockState(kind, current));
		Logger.Debug(LogCategory.Hir, $"Entered {kind} block, depth={_blocks.Count}");
	}

	/// <summary>
	/// Exit the current block and return its state.
	/// </summary>
	public BlockState ExitBlock() {
		if (_blocks.Count == 0) {
			throw new InvalidOperationException("No block to exit");
		}
		var state = _blocks.Pop();
		Logger.Debug(LogCategory.Hir, $"Exited {state.Kind} block, depth={_blocks.Count}");
		return state;
	}

	/// <summary>
	/// Get the current block state, or null if at top level.
	/// </summary>
	public BlockState? CurrentBlock() {
		return _blocks.Count > 0 ? _blocks.Peek() : null;
	}

	/// <summary>
	/// Mark the current block as terminating (return/break/continue).
	/// </summary>
	public void MarkTerminates() {
		if (_blocks.Count > 0) {
			_blocks.Peek().Terminates = true;
			Logger.Debug(LogCategory.Hir, "Block marked as terminating");
		}
	}

	/// <summary>
	/// Record that a variable was moved in the current block.
	/// </summary>
	public void RecordMove(string name, SourceLocation loc) {
		if (_blocks.Count > 0) {
			_blocks.Peek().Moves[name] = loc;
			Logger.Debug(LogCategory.Hir, $"Recorded move of {name}");
		}
	}

	/// <summary>
	/// Record that a variable was reassigned in the current block.
	/// </summary>
	public void RecordReassign(string name) {
		if (_blocks.Count > 0) {
			_blocks.Peek().Reassigns.Add(name);
			Logger.Debug(LogCategory.Hir, $"Recorded reassign of {name}");
		}
	}

	/// <summary>
	/// Merge ownership states from then/else branches.
	///
	/// Rules:
	/// 1. No else, then terminates: Variables moved in then are NOT moved afterward
	/// 2. No else, then falls through: Moves in then apply to continuation
	/// 3. Both terminate: No merge needed (unreachable after)
	/// 4. Only then terminates: Else state applies
	/// 5. Only else terminates: Then state applies
	/// 6. Neither terminates: Must agree on moves (conflict = compile error)
	/// </summary>
	public static MergeResult MergeBranches(BlockState thenState, BlockState? elseState) {
		var definitelyMoved = new List<string>();
		var conflicts = new List<MoveConflict>();

		if (elseState == null) {
			// No else branch
			if (thenState.Terminates) {
				// Rule 1: then terminates, moves don't apply to continuation
				Logger.Debug(LogCategory.Hir, "Then terminates, no moves propagate");
			} else {
				// Rule 2: then falls through, moves apply
				definitelyMoved.AddRange(thenState.Moves.Keys);
				Logger.Debug(LogCategory.Hir, $"Then falls through, {definitelyMoved.Count} moves propagate");
			}
		} else {
			// Has else branch
			if (thenState.Terminates && elseState.Terminates) {
				// Rule 3: both terminate, nothing reachable after
				Logger.Debug(LogCategory.Hir, "Both branches terminate, no continuation");
			} else if (thenState.Terminates) {
				// Rule 4: only then terminates, else state applies
				definitelyMoved.AddRange(elseState.Moves.Keys);
				Logger.Debug(LogCategory.Hir, $"Then terminates, {definitelyMoved.Count} else moves propagate");
			} else if (elseState.Terminates) {
				// Rule 5: only else terminates, then state applies
				definitelyMoved.AddRange(thenState.Moves.Keys);
				Logger.Debug(LogCategory.Hir, $"Else terminates, {definitelyMoved.Count} then moves propagate");
			} else {
				// Rule 6: neither terminates, must agree
				var thenMoves = new HashSet<string>(thenState.Moves.Keys);
				var elseMoves = new HashSet<string>(elseState.Moves.Keys);

				// Variables moved in both are definitely moved
				var movedInBoth = new HashSet<string>(thenMoves);
				movedInBoth.IntersectWith(elseMoves);
				definitelyMoved.AddRange(movedInBoth);

				// Variables moved in only one branch are conflicts
				foreach (var name in thenMoves.Except(movedInBoth)) {
					conflicts.Add(new MoveConflict(name, BlockKind.IfThen, thenState.Moves[name]));
				}
				foreach (var name in elseMoves.Except(movedInBoth)) {
					conflicts.Add(new MoveConflict(name, BlockKind.IfElse, elseState.Moves[name]));
				}

				if (conflicts.Count > 0) {
					Logger.Debug(LogCategory.Hir, $"Branch merge has {conflicts.Count} conflicts");
				}
			}
		}

		return new MergeResult(definitelyMoved, conflicts);
	}

	/// <summary>
	/// Validate loop body for move safety.
	/// Move in loop body without reassignment in same iteration = error.
	/// </summary>
	public static void ValidateLoopBody(BlockState block) {
		foreach (var (name, loc) in block.Moves) {
			if (!block.Reassigns.Contains(name)) {
				// Moved but not reassigned - this is an error in a loop
				Logger.Debug(LogCategory.Hir, $"Loop move without reassign: {name}");
				throw new CompileError(
					ErrorCode.OwnershipMoveInLoop,
					$"Variable '{name}' is moved in loop body without being reassigned",
					loc.Line,
					loc.Column
				);
			}
		}
	}

	/// <summary>
	/// Check if we're currently inside a loop.
	/// </summary>
	public bool IsInLoop() {
		foreach (var block in _blocks) {
			if (block.Kind is BlockKind.WhileLoop or BlockKind.ForLoop) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Get the current block depth.
	/// </summary>
	public int Depth => _blocks.Count;
}
