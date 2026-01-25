using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Borrow checker pass that verifies ownership rules for Maxon dialect operations.
/// </summary>
public sealed class MaxonBorrowChecker : FunctionPass {
	public override string Name => "maxon-borrow-check";
	public override string Description => "Verifies ownership and borrowing rules";

	private readonly List<CompileError> _errors = [];

	/// <summary>
	/// Gets the borrow check errors from the last run.
	/// </summary>
	public IReadOnlyList<CompileError> Errors => _errors;

	/// <summary>
	/// Throws the first error if any errors were collected.
	/// </summary>
	public void ThrowIfErrors() {
		if (_errors.Count > 0) {
			throw _errors[0];
		}
	}

	private void AddError(ErrorCode code, string message, Mlir.Core.SourceLocation? loc = null) {
		_errors.Add(new CompileError(code, message, loc?.Line, loc?.Column));
	}

	protected override bool RunOnFunction(MlirFunction func) {
		_errors.Clear();

		var state = new BorrowCheckState();

		// Process function parameters
		foreach (var param in func.Parameters) {
			if (param.Type is OwnedType) {
				state.MarkOwned(param);
			} else if (param.Type is BorrowedType bt) {
				state.MarkBorrowed(param, bt.IsMutable);
			}
		}

		// Process each block
		foreach (var block in func.Body.Blocks) {
			// Process block arguments
			foreach (var arg in block.Arguments) {
				if (arg.Type is OwnedType) {
					state.MarkOwned(arg.Value);
				} else if (arg.Type is BorrowedType bt) {
					state.MarkBorrowed(arg.Value, bt.IsMutable);
				}
			}

			// Process operations
			foreach (var op in block.Operations) {
				CheckOperation(op, state);
			}
		}

		// Check for values that weren't dropped (but don't error for now - many values are implicitly dropped)
		// foreach (var (value, info) in state.OwnedValues) {
		// 	if (!info.Dropped && !info.Moved) {
		// 		AddError(ErrorCode.MlirUnsupportedExpression, $"Owned value {value.Id} was not dropped or moved");
		// 	}
		// }

		// Check for dangling borrows
		foreach (var (_, info) in state.BorrowedValues) {
			if (info.Active) {
				// This might be okay at function end if returned
			}
		}

		// Throw the first error if any were collected
		ThrowIfErrors();

		return false; // Borrow checker doesn't modify the IR
	}

	private void CheckOperation(MlirOperation op, BorrowCheckState state) {
		switch (op) {
			case MoveOp move:
				CheckMove(move, state);
				break;

			case BorrowOp borrow:
				CheckBorrow(borrow, state);
				break;

			case DropOp drop:
				CheckDrop(drop, state);
				break;

			case MaxonCallOp call:
				CheckCall(call, state);
				break;

			case AllocaOp alloca:
				// Track the alloca as a variable location
				state.TrackAlloca(alloca.Result, alloca.Result.Name);
				break;

			case LoadOp load:
				// Track that the loaded value comes from this memref
				state.TrackLoadSource(load.Result, load.MemRef);
				// Check if the memref (alloca) has been moved
				if (state.WasAllocaMoved(load.MemRef)) {
					var varName = state.GetAllocaName(load.MemRef) ?? $"%{load.MemRef.Id}";
					AddError(ErrorCode.OwnershipUseAfterMove, $"use after move: '{varName}'", load.Location);
				}
				break;

			case StoreOp store:
				// Storing to an alloca restores ownership (reassignment)
				state.RestoreAlloca(store.MemRef);
				break;
		}
	}

	private void CheckMove(MoveOp move, BorrowCheckState state) {
		var source = move.Source;

		// Check source wasn't already moved
		if (state.WasMoved(source)) {
			var varName = GetValueName(source, state);
			AddError(ErrorCode.OwnershipUseAfterMove, $"use after move: '{varName}'", move.Location);
			return;
		}

		// Check source isn't borrowed
		if (state.IsBorrowed(source)) {
			var varName = GetValueName(source, state);
			AddError(ErrorCode.OwnershipUseAfterMove, $"cannot move value '{varName}' while it is borrowed", move.Location);
			return;
		}

		// Mark as moved
		state.MarkMoved(source);

		// Mark result as owned
		state.MarkOwned(move.Result, source.Name);
	}

	private static string GetValueName(MlirValue value, BorrowCheckState state) {
		// Try state first, then value name, then fall back to ID
		return state.GetVariableName(value) ?? value.Name ?? $"%{value.Id}";
	}

	private void CheckBorrow(BorrowOp borrow, BorrowCheckState state) {
		var source = borrow.Source;

		// Check source wasn't moved
		if (state.WasMoved(source)) {
			var varName = GetValueName(source, state);
			AddError(ErrorCode.OwnershipUseAfterMove, $"use after move: '{varName}'", borrow.Location);
			return;
		}

		// Check borrow rules
		if (borrow.IsMutable) {
			// Mutable borrow: no other borrows allowed
			if (state.HasActiveBorrows(source)) {
				var varName = GetValueName(source, state);
				AddError(ErrorCode.OwnershipBranchConflict, $"cannot mutably borrow '{varName}': already borrowed", borrow.Location);
				return;
			}
		} else {
			// Immutable borrow: no mutable borrows allowed
			if (state.HasMutableBorrow(source)) {
				var varName = GetValueName(source, state);
				AddError(ErrorCode.OwnershipBranchConflict, $"cannot borrow '{varName}': already mutably borrowed", borrow.Location);
				return;
			}
		}

		// Record the borrow
		state.AddBorrow(source, borrow.Result, borrow.IsMutable);
		state.MarkBorrowed(borrow.Result, borrow.IsMutable);
	}

	private void CheckDrop(DropOp drop, BorrowCheckState state) {
		var value = drop.Value;

		// Check value wasn't moved
		if (state.WasMoved(value)) {
			// Double drop after move - this is okay, the move handles cleanup
			return;
		}

		// Check no active borrows
		if (state.HasActiveBorrows(value)) {
			var varName = GetValueName(value, state);
			AddError(ErrorCode.OwnershipBranchConflict, $"cannot drop '{varName}': still has active borrows", drop.Location);
			return;
		}

		// Mark as dropped
		state.MarkDropped(value);
	}

	private void CheckCall(MaxonCallOp call, BorrowCheckState state) {
		// Check argument ownership matches parameter expectations
		for (int i = 0; i < call.Operands.Count; i++) {
			var arg = call.Operands[i];
			var ownership = i < call.ArgOwnership.Count ? call.ArgOwnership[i] : ArgumentOwnership.Borrow;

			switch (ownership) {
				case ArgumentOwnership.Move:
					if (state.WasMoved(arg)) {
						var varName = GetValueName(arg, state);
						AddError(ErrorCode.OwnershipUseAfterMove, $"use after move: '{varName}'", call.Location);
					} else {
						state.MarkMovedWithAlloca(arg); // Ownership transferred to callee, mark source alloca too
					}
					break;

				case ArgumentOwnership.Borrow:
				case ArgumentOwnership.BorrowMut:
					if (state.WasMoved(arg)) {
						var varName = GetValueName(arg, state);
						AddError(ErrorCode.OwnershipUseAfterMove, $"use after move: '{varName}'", call.Location);
					}
					// Temporary borrow for call
					break;

				case ArgumentOwnership.Copy:
					// Copy is always safe
					break;
			}
		}
	}
}

/// <summary>
/// Tracks ownership state during borrow checking.
/// </summary>
internal sealed class BorrowCheckState {
	private readonly Dictionary<MlirValue, OwnershipInfo> _owned = [];
	private readonly Dictionary<MlirValue, BorrowInfo> _borrowed = [];
	private readonly Dictionary<MlirValue, List<BorrowRecord>> _activeBorrows = [];

	// Track allocas (memory locations) and their moved state
	private readonly Dictionary<MlirValue, AllocaInfo> _allocas = [];
	// Track which values were loaded from which allocas
	private readonly Dictionary<MlirValue, MlirValue> _loadSources = [];

	public IEnumerable<(MlirValue, OwnershipInfo)> OwnedValues => _owned.Select(kv => (kv.Key, kv.Value));
	public IEnumerable<(MlirValue, BorrowInfo)> BorrowedValues => _borrowed.Select(kv => (kv.Key, kv.Value));

	public void MarkOwned(MlirValue value, string? variableName = null) {
		_owned[value] = new OwnershipInfo { VariableName = variableName };
	}

	public string? GetVariableName(MlirValue value) {
		if (_owned.TryGetValue(value, out var info)) {
			return info.VariableName;
		}
		return null;
	}

	public void MarkBorrowed(MlirValue value, bool mutable) {
		_borrowed[value] = new BorrowInfo { IsMutable = mutable, Active = true };
	}

	public void MarkMoved(MlirValue value) {
		if (_owned.TryGetValue(value, out var info)) {
			info.Moved = true;
		}
	}

	public void MarkDropped(MlirValue value) {
		if (_owned.TryGetValue(value, out var info)) {
			info.Dropped = true;
		}
	}

	public bool IsOwned(MlirValue value) => _owned.ContainsKey(value);

	public bool WasMoved(MlirValue value) =>
		_owned.TryGetValue(value, out var info) && info.Moved;

	public bool IsBorrowed(MlirValue value) =>
		_activeBorrows.TryGetValue(value, out var borrows) && borrows.Count > 0;

	public bool HasActiveBorrows(MlirValue owner) =>
		_activeBorrows.TryGetValue(owner, out var borrows) && borrows.Count > 0;

	public bool HasMutableBorrow(MlirValue owner) =>
		_activeBorrows.TryGetValue(owner, out var borrows) && borrows.Any(b => b.IsMutable);

	public void AddBorrow(MlirValue owner, MlirValue borrowValue, bool mutable) {
		if (!_activeBorrows.TryGetValue(owner, out var borrows)) {
			borrows = [];
			_activeBorrows[owner] = borrows;
		}
		borrows.Add(new BorrowRecord { BorrowValue = borrowValue, IsMutable = mutable });
	}

	public void EndBorrow(MlirValue owner, MlirValue borrowValue) {
		if (_activeBorrows.TryGetValue(owner, out var borrows)) {
			borrows.RemoveAll(b => b.BorrowValue == borrowValue);
		}
		if (_borrowed.TryGetValue(borrowValue, out var info)) {
			info.Active = false;
		}
	}

	// ========================================================================
	// Alloca (memory location) tracking
	// ========================================================================

	public void TrackAlloca(MlirValue alloca, string? variableName) {
		_allocas[alloca] = new AllocaInfo { VariableName = variableName };
	}

	public void TrackLoadSource(MlirValue loadResult, MlirValue sourceMemRef) {
		_loadSources[loadResult] = sourceMemRef;
	}

	public bool WasAllocaMoved(MlirValue alloca) =>
		_allocas.TryGetValue(alloca, out var info) && info.Moved;

	public void MarkAllocaMoved(MlirValue alloca) {
		if (_allocas.TryGetValue(alloca, out var info)) {
			info.Moved = true;
		}
	}

	public void RestoreAlloca(MlirValue alloca) {
		if (_allocas.TryGetValue(alloca, out var info)) {
			info.Moved = false;
		}
	}

	public string? GetAllocaName(MlirValue alloca) =>
		_allocas.TryGetValue(alloca, out var info) ? info.VariableName : null;

	/// <summary>
	/// Gets the source alloca for a value (if it was loaded from one).
	/// </summary>
	public MlirValue? GetSourceAlloca(MlirValue value) =>
		_loadSources.TryGetValue(value, out var source) ? source : null;

	/// <summary>
	/// Marks a value as moved, and also marks its source alloca if applicable.
	/// </summary>
	public void MarkMovedWithAlloca(MlirValue value) {
		MarkMoved(value);
		if (_loadSources.TryGetValue(value, out var sourceAlloca)) {
			MarkAllocaMoved(sourceAlloca);
		}
	}
}

internal sealed class AllocaInfo {
	public bool Moved { get; set; }
	public string? VariableName { get; set; }
}

internal sealed class OwnershipInfo {
	public bool Moved { get; set; }
	public bool Dropped { get; set; }
	public string? VariableName { get; set; }
}

internal sealed class BorrowInfo {
	public bool IsMutable { get; set; }
	public bool Active { get; set; }
}

internal sealed class BorrowRecord {
	public required MlirValue BorrowValue { get; init; }
	public bool IsMutable { get; init; }
}
