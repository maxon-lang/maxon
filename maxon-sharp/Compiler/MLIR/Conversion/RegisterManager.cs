using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Manages GPR and XMM register allocation during StandardToX86 conversion.
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

	// XMM register pool and state
	private static readonly X86XmmRegister[] XmmPool = [
		X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, X86XmmRegister.Xmm2, X86XmmRegister.Xmm3,
		X86XmmRegister.Xmm4, X86XmmRegister.Xmm5, X86XmmRegister.Xmm6, X86XmmRegister.Xmm7,
		X86XmmRegister.Xmm8, X86XmmRegister.Xmm9, X86XmmRegister.Xmm10, X86XmmRegister.Xmm11,
		X86XmmRegister.Xmm12, X86XmmRegister.Xmm13, X86XmmRegister.Xmm14, X86XmmRegister.Xmm15
	];

	private int _nextFreshXmmIndex;
	private readonly Dictionary<X86XmmRegister, StdValue> _xmmContents = [];
	private readonly Dictionary<StdValue, X86XmmRegister> _valueToXmm = [];
	private readonly Dictionary<StdValue, int> _valueXmmStackHome = [];
	private readonly Dictionary<X86XmmRegister, int> _xmmLastUsed = [];

	/// <summary>
	/// Allocate a physical register for a new value.
	/// May evict an existing value if all registers are occupied.
	/// </summary>
	private X86Register AllocateRegister(StdValue value, MlirBlock<X86Op> block) {
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
	private X86Register EnsureInRegister(StdValue value, MlirBlock<X86Op> block) {
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
	/// Allocate a register and load an immediate value into it.
	/// </summary>
	public void EmitLoadImmediate(StdValue result, int immediate, MlirBlock<X86Op> block) {
		var gpr = AllocateRegister(result, block);
		block.AddOp(new X86MovRegImmOp(gpr, immediate));
	}

	/// <summary>
	/// Emit a two-operand register-register instruction (e.g. add, sub)
	/// where the result overwrites the lhs register.
	/// </summary>
	public void EmitBinaryRegReg(StdValue lhs, StdValue rhs, StdValue result,
		MlirBlock<X86Op> block, Func<X86Register, X86Register, X86Op> makeOp) {
		var lhsReg = EnsureInRegister(lhs, block);
		var rhsReg = EnsureInRegister(rhs, block);
		block.AddOp(makeOp(lhsReg, rhsReg));
		TransferValue(lhs, lhsReg, result);
	}

	/// <summary>
	/// Emit IDIV and capture the remainder in EDX.
	/// Handles register constraints: divisor must not be in EAX/EDX.
	/// </summary>
	public void EmitRemainder(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
		var lhsReg = EnsureInRegister(lhs, block);
		var rhsReg = EnsureInRegister(rhs, block);

		// Divisor must not be in RAX or RDX (IDIV clobbers both).
		if (rhsReg == X86Register.Eax || rhsReg == X86Register.Edx) {
			var safeReg = lhsReg != X86Register.Ecx ? X86Register.Ecx : X86Register.Ebx;
			block.AddOp(new X86MovRegRegOp(safeReg, rhsReg));
			rhsReg = safeReg;
		}

		if (lhsReg != X86Register.Eax) {
			block.AddOp(new X86MovRegRegOp(X86Register.Eax, lhsReg));
		}

		// Sign-extend RAX into RDX:RAX
		block.AddOp(new X86CqoOp());
		block.AddOp(new X86IdivRegOp(rhsReg));

		NoteValueInRegister(result, X86Register.Edx);
	}

	/// <summary>
	/// Store a GPR value to a stack offset and record the stack home.
	/// </summary>
	public void EmitStoreToStack(StdValue value, int offset, MlirBlock<X86Op> block) {
		var srcReg = EnsureInRegister(value, block);
		block.AddOp(new X86MovMemRegOp(offset, srcReg));
		NoteStoreToStack(value, offset);
	}

	/// <summary>
	/// Load a GPR value from a stack offset.
	/// </summary>
	public void EmitLoadFromStack(StdValue result, int offset, MlirBlock<X86Op> block) {
		var gpr = AllocateRegister(result, block);
		block.AddOp(new X86MovRegMemOp(gpr, offset));
	}

	/// <summary>
	/// Store an XMM value to a stack offset and record the stack home.
	/// </summary>
	public void EmitXmmStoreToStack(StdValue value, int offset, MlirBlock<X86Op> block) {
		var srcXmm = EnsureInXmmRegister(value, block);
		block.AddOp(new X86MovSdMemXmmOp(offset, srcXmm));
		NoteXmmStoreToStack(value, offset);
	}

	/// <summary>
	/// Load an XMM value from a stack offset.
	/// </summary>
	public void EmitXmmLoadFromStack(StdValue result, int offset, MlirBlock<X86Op> block) {
		var xmmReg = AllocateXmmRegister(result, block);
		block.AddOp(new X86MovSdXmmMemOp(xmmReg, offset));
	}

	/// <summary>
	/// Allocate an XMM register and load a float constant from rdata.
	/// </summary>
	public void EmitXmmLoadFromRipRelative(StdValue result, string rdataLabel, MlirBlock<X86Op> block) {
		var xmmReg = AllocateXmmRegister(result, block);
		block.AddOp(new X86MovSdXmmRipRelOp(xmmReg, rdataLabel));
	}

	/// <summary>
	/// Ensure both XMM operands are in registers and emit ucomisd.
	/// </summary>
	public void EmitXmmCompare(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
		var lhsReg = EnsureInXmmRegister(lhs, block);
		var rhsReg = EnsureInXmmRegister(rhs, block);
		block.AddOp(new X86UcomisdOp(lhsReg, rhsReg));
	}

	/// <summary>
	/// Ensure a value is in a specific physical register, emitting a mov if needed.
	/// </summary>
	public void EnsureInSpecificRegister(StdValue value, X86Register target, MlirBlock<X86Op> block) {
		var reg = EnsureInRegister(value, block);
		if (reg != target) {
			block.AddOp(new X86MovRegRegOp(target, reg));
		}
	}

	/// <summary>
	/// Emit a function call, invalidate caller-saved registers, and
	/// record the result (if any) in Eax.
	/// </summary>
	public void EmitCall(string callee, StdValue? result, MlirBlock<X86Op> block) {
		block.AddOp(new X86CallDirectOp(callee));

		// Caller-saved registers are clobbered by the call.
		// Remove register mappings but preserve stack homes so values can be reloaded.
		InvalidateCallerSavedRegisters();

		if (result != null) {
			Assign(X86Register.Eax, result);
		}
	}

	/// <summary>
	/// After a call, EAX/ECX/EDX are clobbered. Remove any value mappings
	/// for those registers so stale values are never used.
	/// </summary>
	private void InvalidateCallerSavedRegisters() {
		X86Register[] callerSaved = [X86Register.Eax, X86Register.Ecx, X86Register.Edx];
		foreach (var reg in callerSaved) {
			if (_registerContents.TryGetValue(reg, out var value)) {
				_valueToRegister.Remove(value);
				_registerContents.Remove(reg);
				_lastUsed.Remove(reg);
			}
		}
	}

	/// <summary>
	/// Record that a value is already in a specific physical register
	/// (e.g. function call result in Eax).
	/// Evicts any existing occupant of that register.
	/// </summary>
	private void NoteValueInRegister(StdValue value, X86Register reg) {
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
	private void NoteStoreToStack(StdValue value, int displacement) {
		_valueStackHome[value] = displacement;
	}

	/// <summary>
	/// Allocate an XMM register for a new float value.
	/// </summary>
	private X86XmmRegister AllocateXmmRegister(StdValue value, MlirBlock<X86Op> block) {
		if (_nextFreshXmmIndex < XmmPool.Length) {
			var candidate = XmmPool[_nextFreshXmmIndex];
			if (!_xmmContents.ContainsKey(candidate)) {
				_nextFreshXmmIndex++;
				AssignXmm(candidate, value);
				return candidate;
			}
		}

		foreach (var reg in XmmPool) {
			if (!_xmmContents.ContainsKey(reg)) {
				AssignXmm(reg, value);
				return reg;
			}
		}

		throw new InvalidOperationException("RegisterManager: all XMM registers occupied");
	}

	/// <summary>
	/// Ensure a float value is in an XMM register, reloading from stack if needed.
	/// </summary>
	private X86XmmRegister EnsureInXmmRegister(StdValue value, MlirBlock<X86Op> block) {
		if (_valueToXmm.TryGetValue(value, out var reg)) {
			_xmmLastUsed[reg] = _currentOpIndex;
			return reg;
		}

		if (_valueXmmStackHome.TryGetValue(value, out var displacement)) {
			var reloadReg = AllocateXmmRegister(value, block);
			block.AddOp(new X86MovSdXmmMemOp(reloadReg, displacement));
			return reloadReg;
		}

		throw new InvalidOperationException($"RegisterManager: float value %{value.Id} has no XMM register and no stack home");
	}

	/// <summary>
	/// Record that a float value is already in a specific XMM register.
	/// </summary>
	private void NoteValueInXmmRegister(StdValue value, X86XmmRegister reg) {
		if (_xmmContents.TryGetValue(reg, out var existing)) {
			_valueToXmm.Remove(existing);
			_xmmContents.Remove(reg);
			_xmmLastUsed.Remove(reg);
		}
		AssignXmm(reg, value);
	}

	/// <summary>
	/// Record that a float value has been stored to stack at the given displacement.
	/// </summary>
	private void NoteXmmStoreToStack(StdValue value, int displacement) {
		_valueXmmStackHome[value] = displacement;
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

		if (_valueToXmm.TryGetValue(value, out var xmmReg)) {
			_valueToXmm.Remove(value);
			_xmmContents.Remove(xmmReg);
			_xmmLastUsed.Remove(xmmReg);
		}
	}

	/// <summary>
	/// After an add/sub, the destination register now holds a new result value.
	/// The old value is replaced.
	/// </summary>
	private void TransferValue(StdValue oldValue, X86Register reg, StdValue newValue) {
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

		_nextFreshXmmIndex = 0;
		_xmmContents.Clear();
		_valueToXmm.Clear();
		_valueXmmStackHome.Clear();
		_xmmLastUsed.Clear();
	}

	private void Assign(X86Register reg, StdValue value) {
		_registerContents[reg] = value;
		_valueToRegister[value] = reg;
		_lastUsed[reg] = _currentOpIndex;
	}

	private void AssignXmm(X86XmmRegister reg, StdValue value) {
		_xmmContents[reg] = value;
		_valueToXmm[value] = reg;
		_xmmLastUsed[reg] = _currentOpIndex;
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
