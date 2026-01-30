using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Manages GPR allocation during StandardToX86 conversion.
/// Tracks which physical registers hold which values, handles eviction
/// when registers are exhausted, and reloads from stack when needed.
/// </summary>
public class RegisterManager {
	private static readonly X86Register[] GprPool = [
		X86Register.Eax, X86Register.Ecx, X86Register.Edx,
		X86Register.Ebx, X86Register.Esi, X86Register.Edi
	];

	// Sequential allocation pointer — assigns registers in pool order
	// to match the naive allocator's behavior when registers aren't exhausted
	private int _nextFreshIndex;

	private readonly Dictionary<X86Register, StdValue> _registerContents = [];
	private readonly Dictionary<StdValue, X86Register> _valueToRegister = [];
	private readonly Dictionary<StdValue, int> _valueStackHome = [];
	private readonly Dictionary<X86Register, int> _lastUsed = [];
	private int _currentOpIndex;

	/// <summary>
	/// Allocate a physical register for a new value.
	/// May evict an existing value if all registers are occupied.
	/// </summary>
	public X86Register AllocateRegister(StdValue value, MlirBlock<X86Op> block) {
		// 1. Try the next sequential slot (preserves existing test output)
		if (_nextFreshIndex < GprPool.Length) {
			var candidate = GprPool[_nextFreshIndex];
			if (!_registerContents.ContainsKey(candidate)) {
				_nextFreshIndex++;
				Assign(candidate, value);
				return candidate;
			}
		}

		// 2. Find any free register in pool order
		foreach (var reg in GprPool) {
			if (!_registerContents.ContainsKey(reg)) {
				Assign(reg, value);
				return reg;
			}
		}

		// 3. All registers occupied — evict one
		Evict(block);

		// Now find the freed register
		foreach (var reg in GprPool) {
			if (!_registerContents.ContainsKey(reg)) {
				Assign(reg, value);
				return reg;
			}
		}

		throw new InvalidOperationException("RegisterManager: failed to allocate register after eviction");
	}

	/// <summary>
	/// Ensure a value is in a physical register, reloading from stack if needed.
	/// </summary>
	public X86Register EnsureInRegister(StdValue value, MlirBlock<X86Op> block) {
		// Already in a register
		if (_valueToRegister.TryGetValue(value, out var reg)) {
			_lastUsed[reg] = _currentOpIndex;
			return reg;
		}

		// Not in a register — must reload from stack
		if (_valueStackHome.TryGetValue(value, out var displacement)) {
			var reloadReg = AllocateRegister(value, block);
			block.AddOp(new X86MovRegMemOp(reloadReg, displacement));
			return reloadReg;
		}

		throw new InvalidOperationException($"RegisterManager: value %{value.Id} has no register and no stack home");
	}

	/// <summary>
	/// Record that a value is already in a specific physical register
	/// (e.g. function call result in Eax).
	/// Evicts any existing occupant of that register.
	/// </summary>
	public void NoteValueInRegister(StdValue value, X86Register reg) {
		// Evict existing occupant if any
		if (_registerContents.TryGetValue(reg, out var existing)) {
			_valueToRegister.Remove(existing);
			_registerContents.Remove(reg);
			_lastUsed.Remove(reg);
		}
		Assign(reg, value);
	}

	/// <summary>
	/// Record that a value has been stored to stack at the given displacement.
	/// </summary>
	public void NoteStoreToStack(StdValue value, int displacement) {
		_valueStackHome[value] = displacement;
	}

	/// <summary>
	/// Mark a value as dead and free its register.
	/// </summary>
	public void NoteValueDead(StdValue value) {
		if (_valueToRegister.TryGetValue(value, out var reg)) {
			_valueToRegister.Remove(value);
			_registerContents.Remove(reg);
			_lastUsed.Remove(reg);
		}
	}

	/// <summary>
	/// After an add/sub, the destination register now holds a new result value.
	/// The old value is replaced.
	/// </summary>
	public void TransferValue(StdValue oldValue, X86Register reg, StdValue newValue) {
		// Remove old value mapping if it points to this register
		if (_valueToRegister.TryGetValue(oldValue, out var oldReg) && oldReg == reg) {
			_valueToRegister.Remove(oldValue);
		}

		_registerContents[reg] = newValue;
		_valueToRegister[newValue] = reg;
		_lastUsed[reg] = _currentOpIndex;
	}

	public void AdvanceOp() {
		_currentOpIndex++;
	}

	public void Reset() {
		_nextFreshIndex = 0;
		_registerContents.Clear();
		_valueToRegister.Clear();
		_valueStackHome.Clear();
		_lastUsed.Clear();
		_currentOpIndex = 0;
	}

	private void Assign(X86Register reg, StdValue value) {
		_registerContents[reg] = value;
		_valueToRegister[value] = reg;
		_lastUsed[reg] = _currentOpIndex;
	}

	/// <summary>
	/// Evict the least-recently-used register whose value has a stack home.
	/// No spill store is needed since the value already exists on the stack.
	/// </summary>
	private void Evict(MlirBlock<X86Op> block) {
		X86Register? bestReg = null;
		int bestLastUsed = int.MaxValue;

		// Prefer evicting values that have a stack home (no spill store needed)
		foreach (var (reg, value) in _registerContents) {
			if (_valueStackHome.ContainsKey(value)) {
				var lastUsed = _lastUsed.GetValueOrDefault(reg, 0);
				if (lastUsed < bestLastUsed) {
					bestLastUsed = lastUsed;
					bestReg = reg;
				}
			}
		}

		if (bestReg == null) {
			throw new InvalidOperationException(
				"RegisterManager: all registers occupied and no value has a stack home. " +
				"Spill slot allocation not yet implemented.");
		}

		var evictedValue = _registerContents[bestReg.Value];
		_registerContents.Remove(bestReg.Value);
		_valueToRegister.Remove(evictedValue);
		_lastUsed.Remove(bestReg.Value);
	}
}
