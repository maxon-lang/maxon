namespace MaxonSharp.Compiler;

/// <summary>
/// Ownership states for variables.
/// </summary>
public enum OwnershipState {
	Owned,     // Variable owns the value
	Moved,     // Ownership has been transferred
	Borrowed   // Value is borrowed, ownership remains with original owner
}

/// <summary>
/// Information about a variable for ownership tracking.
/// </summary>
public record VariableInfo {
	public required string Name { get; init; }
	public required HirValue Ptr { get; init; }        // IR pointer to storage
	public required HirType ValueType { get; init; }   // Type info (determines cleanup strategy)
	public required bool IsMutable { get; init; }      // let vs var
	public OwnershipState State { get; set; }
	public SourceLocation? MovedAt { get; set; }       // For error messages
}

/// <summary>
/// Snapshot of ownership states for branch handling.
/// </summary>
public class OwnershipSnapshot {
	private readonly Dictionary<string, OwnershipState> _states = [];
	private readonly Dictionary<string, SourceLocation?> _movedAt = [];

	public OwnershipSnapshot() { }

	private OwnershipSnapshot(Dictionary<string, OwnershipState> states, Dictionary<string, SourceLocation?> movedAt) {
		foreach (var (k, v) in states) {
			_states[k] = v;
		}
		foreach (var (k, v) in movedAt) {
			_movedAt[k] = v;
		}
	}

	public void Set(string name, OwnershipState state, SourceLocation? movedAt = null) {
		_states[name] = state;
		_movedAt[name] = movedAt;
	}

	public (OwnershipState State, SourceLocation? MovedAt)? Get(string name) {
		if (_states.TryGetValue(name, out var state)) {
			return (state, _movedAt.GetValueOrDefault(name));
		}
		return null;
	}

	public OwnershipSnapshot Clone() {
		return new OwnershipSnapshot(_states, _movedAt);
	}

	public IEnumerable<string> GetMovedVariables() {
		return _states.Where(kv => kv.Value == OwnershipState.Moved).Select(kv => kv.Key);
	}
}

/// <summary>
/// Unified ownership and memory management.
/// Tracks variable lifecycle and coordinates cleanup code generation.
/// </summary>
public class ResourceManager(MutationAnalyzer mutationAnalyzer, BlockManager blockManager, bool trackMemory = false) {
	private readonly Stack<Dictionary<string, VariableInfo>> _scopes = new();
	private readonly MutationAnalyzer _mutationAnalyzer = mutationAnalyzer;
	private readonly BlockManager _blockManager = blockManager;
	private readonly bool _trackMemory = trackMemory;  // --track-memory flag
	private List<HirInstr> _currentBlock = [];

	// For deferred struct literal moves
	private readonly Stack<List<(string Name, SourceLocation Loc)>> _pendingMoves = new();

	/// <summary>
	/// Set the current instruction list for emitting cleanup code.
	/// </summary>
	public void SetCurrentBlock(List<HirInstr> block) {
		_currentBlock = block;
	}

	// ========================================================================
	// Scope Lifecycle
	// ========================================================================

	/// <summary>
	/// Begin a new scope for variable declarations.
	/// </summary>
	public void BeginScope() {
		_scopes.Push([]);
		Logger.Debug(LogCategory.Hir, $"Begin scope, depth={_scopes.Count}");
	}

	/// <summary>
	/// End the current scope, cleaning up owned variables.
	/// </summary>
	public void EndScope() {
		if (_scopes.Count == 0) {
			throw new InvalidOperationException("No scope to end");
		}

		var scope = _scopes.Pop();

		// Clean up owned variables in reverse order of declaration
		var toCleanup = scope.Values
			.Where(v => v.State == OwnershipState.Owned && NeedsCleanup(v.ValueType))
			.Reverse()
			.ToList();

		foreach (var variable in toCleanup) {
			EmitCleanup(variable);
		}

		Logger.Debug(LogCategory.Hir, $"End scope, cleaned up {toCleanup.Count} vars, depth={_scopes.Count}");
	}

	// ========================================================================
	// Variable Declaration
	// ========================================================================

	/// <summary>
	/// Declare a new variable, allocating storage and registering with owned state.
	/// </summary>
	public HirValue DeclareVariable(string name, HirType valueType, bool isMutable, HirValue ptr) {
		if (_scopes.Count == 0) {
			throw new InvalidOperationException("No scope for variable declaration");
		}

		var info = new VariableInfo {
			Name = name,
			Ptr = ptr,
			ValueType = valueType,
			IsMutable = isMutable,
			State = OwnershipState.Owned,
			MovedAt = null
		};

		_scopes.Peek()[name] = info;
		Logger.Debug(LogCategory.Hir, $"Declared variable {name}: {valueType.Name}, mutable={isMutable}");

		return ptr;
	}

	// ========================================================================
	// Variable Access
	// ========================================================================

	/// <summary>
	/// Use a variable, checking ownership state. Throws E008 if moved.
	/// </summary>
	public HirValue UseVariable(string name, SourceLocation loc) {
		var info = FindVariable(name) ?? throw new CompileError(ErrorCode.HirUndefinedVariable, $"Unknown variable: {name}");
		if (info.State == OwnershipState.Moved) {
			throw new CompileError(
				ErrorCode.OwnershipUseAfterMove,
				$"Cannot use '{name}' after it has been moved",
				loc.Line,
				loc.Column
			);
		}

		return info.Ptr;
	}

	/// <summary>
	/// Get a variable's pointer without ownership check.
	/// Used for getting the address for cleanup, etc.
	/// </summary>
	public HirValue? GetVariablePtr(string name) {
		return FindVariable(name)?.Ptr;
	}

	/// <summary>
	/// Get full variable info for a name.
	/// </summary>
	public VariableInfo? GetVariableInfo(string name) {
		return FindVariable(name);
	}

	/// <summary>
	/// Check if a variable exists in any scope.
	/// </summary>
	public bool HasVariable(string name) {
		return FindVariable(name) != null;
	}

	// ========================================================================
	// Ownership Transfer
	// ========================================================================

	/// <summary>
	/// Record that a variable is being passed to a function.
	/// Determines move vs borrow based on mutation analysis.
	/// </summary>
	public void PassToFunction(string name, string funcName, int paramIdx, SourceLocation loc) {
		var info = FindVariable(name);
		if (info == null) return;

		// Check if function mutates this parameter
		var isMutated = _mutationAnalyzer.IsMutated(funcName, paramIdx);

		if (isMutated) {
			// Move semantics - ownership transfers to callee
			MarkMoved(name, loc);
		} else {
			// Borrow semantics - ownership stays with us
			Logger.Debug(LogCategory.Hir, $"Borrowing {name} to {funcName}");
		}
	}

	/// <summary>
	/// Mark a variable as moved.
	/// </summary>
	public void MarkMoved(string name, SourceLocation loc) {
		var info = FindVariable(name);
		if (info == null) return;

		// Can't move from immutable binding (let)
		if (!info.IsMutable && info.State == OwnershipState.Owned) {
			// Actually, moving from let is OK - it just can't be reassigned after
			// The error is trying to use it after move, which UseVariable catches
		}

		info.State = OwnershipState.Moved;
		info.MovedAt = loc;

		// Record move in block manager for branch tracking
		_blockManager.RecordMove(name, loc);

		Logger.Debug(LogCategory.Hir, $"Moved {name} at {loc.Line}:{loc.Column}");
	}

	/// <summary>
	/// Reassign a variable, restoring ownership.
	/// </summary>
	public void Reassign(string name) {
		var info = FindVariable(name);
		if (info == null) return;

		info.State = OwnershipState.Owned;
		info.MovedAt = null;

		// Record reassign in block manager for loop validation
		_blockManager.RecordReassign(name);

		Logger.Debug(LogCategory.Hir, $"Reassigned {name}, ownership restored");
	}

	// ========================================================================
	// Control Flow
	// ========================================================================

	/// <summary>
	/// Take a snapshot of current ownership states for branch handling.
	/// </summary>
	public OwnershipSnapshot Snapshot() {
		var snapshot = new OwnershipSnapshot();
		foreach (var scope in _scopes) {
			foreach (var (name, info) in scope) {
				snapshot.Set(name, info.State, info.MovedAt);
			}
		}
		return snapshot;
	}

	/// <summary>
	/// Restore ownership states from a snapshot.
	/// </summary>
	public void Restore(OwnershipSnapshot snapshot) {
		foreach (var scope in _scopes) {
			foreach (var (name, info) in scope) {
				var saved = snapshot.Get(name);
				if (saved.HasValue) {
					info.State = saved.Value.State;
					info.MovedAt = saved.Value.MovedAt;
				}
			}
		}
		Logger.Debug(LogCategory.Hir, "Restored ownership snapshot");
	}

	/// <summary>
	/// Apply merge result from block manager to current ownership state.
	/// </summary>
	public void ApplyMerge(MergeResult merge) {
		// Mark definitely moved variables
		foreach (var name in merge.DefinitelyMoved) {
			var info = FindVariable(name);
			if (info != null) {
				info.State = OwnershipState.Moved;
			}
		}

		// Report conflicts as errors
		foreach (var conflict in merge.Conflicts) {
			throw new CompileError(
				ErrorCode.OwnershipBranchConflict,
				$"Variable '{conflict.Name}' is moved in only one branch",
				conflict.MovedAt.Line,
				conflict.MovedAt.Column
			);
		}
	}

	// ========================================================================
	// Struct Literal Deferred Moves
	// ========================================================================

	/// <summary>
	/// Begin struct literal construction - defer moves until complete.
	/// </summary>
	public void BeginStructLiteral() {
		_pendingMoves.Push([]);
	}

	/// <summary>
	/// Record a pending move during struct literal construction.
	/// </summary>
	public void RecordPendingMove(string name, SourceLocation loc) {
		if (_pendingMoves.Count > 0) {
			_pendingMoves.Peek().Add((name, loc));
		} else {
			// Not in struct literal, apply immediately
			MarkMoved(name, loc);
		}
	}

	/// <summary>
	/// End struct literal construction - apply pending moves.
	/// </summary>
	public void EndStructLiteral() {
		if (_pendingMoves.Count > 0) {
			var pending = _pendingMoves.Pop();
			foreach (var (name, loc) in pending) {
				MarkMoved(name, loc);
			}
		}
	}

	// ========================================================================
	// Cleanup Code Generation
	// ========================================================================

	/// <summary>
	/// Check if a type needs cleanup (has managed resources).
	/// </summary>
	public static bool NeedsCleanup(HirType type) {
		return type switch {
			HirArrayType => true,                  // Arrays need heap deallocation
			HirStructType st => st.Fields.Any(f => NeedsCleanup(f.Type)),  // Struct with managed fields
			HirPtrType => false,                   // Raw pointers don't own memory
			_ => false                             // Primitives don't need cleanup
		};
	}

	/// <summary>
	/// Emit cleanup code for a variable.
	/// </summary>
	private void EmitCleanup(VariableInfo variable) {
		if (!NeedsCleanup(variable.ValueType)) return;

		Logger.Debug(LogCategory.Hir, $"Emitting cleanup for {variable.Name}");

		// For structs with managed fields, cleanup fields first (depth-first)
		if (variable.ValueType is HirStructType structType) {
			foreach (var field in structType.Fields.AsEnumerable().Reverse()) {
				if (NeedsCleanup(field.Type)) {
					EmitFieldCleanup(variable.Ptr, field);
				}
			}
		}

		// For arrays, free the buffer
		if (variable.ValueType is HirArrayType) {
			_currentBlock.Add(new HirHeapFree(variable.Ptr));
		}

		if (_trackMemory) {
			EmitTrackFree(variable);
		}
	}

	/// <summary>
	/// Emit cleanup for a struct field.
	/// </summary>
	private static void EmitFieldCleanup(HirValue basePtr, HirStructField field) {
		// Get field pointer and emit cleanup
		// This would need a way to generate new values - for now we just note it
		Logger.Debug(LogCategory.Hir, $"Field cleanup needed for {field.FieldName}");

		// In a full implementation, we'd emit:
		// var fieldPtr = NewValue();
		// _currentBlock.Add(new HirGetFieldPtr(fieldPtr, basePtr, field.FieldName, field.Offset));
		// if (field.Type is HirArrayType) {
		//     _currentBlock.Add(new HirHeapFree(fieldPtr));
		// }
	}

	/// <summary>
	/// Emit tracking for memory free (when --track-memory is enabled).
	/// </summary>
	private void EmitTrackFree(VariableInfo variable) {
		// Would emit a call to a tracking function
		Logger.Debug(LogCategory.Hir, $"Track free for {variable.Name}");
	}

	// ========================================================================
	// Helpers
	// ========================================================================

	private VariableInfo? FindVariable(string name) {
		foreach (var scope in _scopes) {
			if (scope.TryGetValue(name, out var info)) {
				return info;
			}
		}
		return null;
	}
}
