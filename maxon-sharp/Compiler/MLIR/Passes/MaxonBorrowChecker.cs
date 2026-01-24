using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.Maxon;

using MaxonDialectOps = MaxonSharp.Compiler.Mlir.Dialects.Maxon;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Borrow checker pass that verifies ownership rules for Maxon dialect operations.
/// </summary>
public sealed class MaxonBorrowChecker : FunctionPass {
	public override string Name => "maxon-borrow-check";
	public override string Description => "Verifies ownership and borrowing rules";

	private readonly List<string> _errors = [];

	/// <summary>
	/// Gets the borrow check errors from the last run.
	/// </summary>
	public IReadOnlyList<string> Errors => _errors;

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

		// Check for values that weren't dropped
		foreach (var (value, info) in state.OwnedValues) {
			if (!info.Dropped && !info.Moved) {
				_errors.Add($"Owned value {value.Id} was not dropped or moved");
			}
		}

		// Check for dangling borrows
		foreach (var (_, info) in state.BorrowedValues) {
			if (info.Active) {
				// This might be okay at function end if returned
			}
		}

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

			case MaxonDialectOps.CallOp call:
				CheckCall(call, state);
				break;
		}
	}

	private void CheckMove(MoveOp move, BorrowCheckState state) {
		var source = move.Source;

		// Check source is owned
		if (!state.IsOwned(source)) {
			_errors.Add($"Cannot move from non-owned value {source.Id}");
			return;
		}

		// Check source wasn't already moved
		if (state.WasMoved(source)) {
			_errors.Add($"Use of moved value {source.Id}");
			return;
		}

		// Check source isn't borrowed
		if (state.IsBorrowed(source)) {
			_errors.Add($"Cannot move value {source.Id} while it is borrowed");
			return;
		}

		// Mark as moved
		state.MarkMoved(source);

		// Mark result as owned
		state.MarkOwned(move.Result);
	}

	private void CheckBorrow(BorrowOp borrow, BorrowCheckState state) {
		var source = borrow.Source;

		// Check source is owned
		if (!state.IsOwned(source)) {
			_errors.Add($"Cannot borrow from non-owned value {source.Id}");
			return;
		}

		// Check source wasn't moved
		if (state.WasMoved(source)) {
			_errors.Add($"Cannot borrow moved value {source.Id}");
			return;
		}

		// Check borrow rules
		if (borrow.IsMutable) {
			// Mutable borrow: no other borrows allowed
			if (state.HasActiveBorrows(source)) {
				_errors.Add($"Cannot mutably borrow {source.Id}: already borrowed");
				return;
			}
		} else {
			// Immutable borrow: no mutable borrows allowed
			if (state.HasMutableBorrow(source)) {
				_errors.Add($"Cannot borrow {source.Id}: already mutably borrowed");
				return;
			}
		}

		// Record the borrow
		state.AddBorrow(source, borrow.Result, borrow.IsMutable);
		state.MarkBorrowed(borrow.Result, borrow.IsMutable);
	}

	private void CheckDrop(DropOp drop, BorrowCheckState state) {
		var value = drop.Value;

		// Check value is owned
		if (!state.IsOwned(value)) {
			_errors.Add($"Cannot drop non-owned value {value.Id}");
			return;
		}

		// Check value wasn't moved
		if (state.WasMoved(value)) {
			_errors.Add($"Cannot drop moved value {value.Id}");
			return;
		}

		// Check no active borrows
		if (state.HasActiveBorrows(value)) {
			_errors.Add($"Cannot drop {value.Id}: still has active borrows");
			return;
		}

		// Mark as dropped
		state.MarkDropped(value);
	}

	private void CheckCall(MaxonDialectOps.CallOp call, BorrowCheckState state) {
		// Check argument ownership matches parameter expectations
		for (int i = 0; i < call.Operands.Count; i++) {
			var arg = call.Operands[i];
			var ownership = i < call.ArgOwnership.Count ? call.ArgOwnership[i] : ArgumentOwnership.Borrow;

			switch (ownership) {
				case ArgumentOwnership.Move:
					if (!state.IsOwned(arg)) {
						_errors.Add($"Argument {i} to {call.Callee} must be owned");
					} else if (state.WasMoved(arg)) {
						_errors.Add($"Use of moved value {arg.Id} in call to {call.Callee}");
					} else {
						state.MarkMoved(arg); // Ownership transferred to callee
					}
					break;

				case ArgumentOwnership.Borrow:
				case ArgumentOwnership.BorrowMut:
					if (state.WasMoved(arg)) {
						_errors.Add($"Use of moved value {arg.Id} in call to {call.Callee}");
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

	public IEnumerable<(MlirValue, OwnershipInfo)> OwnedValues => _owned.Select(kv => (kv.Key, kv.Value));
	public IEnumerable<(MlirValue, BorrowInfo)> BorrowedValues => _borrowed.Select(kv => (kv.Key, kv.Value));

	public void MarkOwned(MlirValue value) {
		_owned[value] = new OwnershipInfo();
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
}

internal sealed class OwnershipInfo {
	public bool Moved { get; set; }
	public bool Dropped { get; set; }
}

internal sealed class BorrowInfo {
	public bool IsMutable { get; set; }
	public bool Active { get; set; }
}

internal sealed class BorrowRecord {
	public required MlirValue BorrowValue { get; init; }
	public bool IsMutable { get; init; }
}
