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
		X86Register.Ebx, X86Register.Esi, X86Register.Edi,
		X86Register.R8, X86Register.R9
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
	/// Emit IMUL (integer multiplication).
	/// Result = lhs * rhs.
	/// </summary>
	public void EmitMultiply(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
		var lhsReg = EnsureInRegister(lhs, block);
		var rhsReg = EnsureInRegister(rhs, block);

		// IMUL reg, reg - multiply and store result in first operand
		var resultReg = AllocateRegister(result, block);
		if (resultReg != lhsReg) {
			block.AddOp(new X86MovRegRegOp(resultReg, lhsReg));
		}
		block.AddOp(new X86ImulRegRegOp(resultReg, rhsReg));
	}

	/// <summary>
	/// Emit IDIV and capture the quotient in EAX.
	/// Handles register constraints: divisor must not be in EAX/EDX.
	/// </summary>
	public void EmitDivision(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
		EmitIdivOperation(lhs, rhs, result, X86Register.Eax, block);
	}

	/// <summary>
	/// Emit IDIV and capture the remainder in EDX.
	/// Handles register constraints: divisor must not be in EAX/EDX.
	/// </summary>
	public void EmitRemainder(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
		EmitIdivOperation(lhs, rhs, result, X86Register.Edx, block);
	}

	/// <summary>
	/// Emit IDIV instruction with proper register allocation.
	/// IDIV clobbers both EAX (quotient) and EDX (remainder), so caller specifies which to capture.
	/// </summary>
	private void EmitIdivOperation(StdValue lhs, StdValue rhs, StdValue result, X86Register resultRegister, MlirBlock<X86Op> block) {
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

		NoteValueInRegister(result, resultRegister);
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
	/// Ensure both GPR operands are in registers and emit cmp.
	/// </summary>
	public void EmitIntegerCompare(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
		var lhsReg = EnsureInRegister(lhs, block);
		var rhsReg = EnsureInRegister(rhs, block);
		block.AddOp(new X86CmpRegRegOp(lhsReg, rhsReg));
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

	// Windows x64 integer parameter registers (rcx, rdx, r8, r9)
	private static readonly X86Register[] CallConvRegs = [X86Register.Ecx, X86Register.Edx, X86Register.R8, X86Register.R9];

	/// <summary>
	/// Emit a function call, placing arguments in calling convention registers
	/// (and on the stack for 5th+ args), invalidating caller-saved registers,
	/// and recording the result (if any) in Eax.
	/// </summary>
	public void EmitCall(string callee, List<StdValue> args, StdValue? result, MlirBlock<X86Op> block) {
		int regArgCount = Math.Min(args.Count, CallConvRegs.Length);
		int stackArgCount = args.Count - regArgCount;

		// Allocate stack space for overflow args (aligned to 16 bytes)
		int stackArgBytes = stackArgCount > 0 ? ((stackArgCount * 8 + 15) & ~15) : 0;
		if (stackArgBytes > 0)
			block.AddOp(new X86SubRegImmOp(X86Register.Rsp, stackArgBytes));

		// Place stack args first (5th+ parameters) using rsp-relative stores
		for (int i = CallConvRegs.Length; i < args.Count; i++) {
			var argReg = EnsureInRegister(args[i], block);
			int offset = (i - CallConvRegs.Length) * 8;
			block.AddOp(new X86MovMemRspRegOp(offset, argReg));
		}

		// Ensure all register arg values are in registers before moving to targets.
		var argRegs = new X86Register[regArgCount];
		for (int i = 0; i < regArgCount; i++)
			argRegs[i] = EnsureInRegister(args[i], block);

		// Move args to their target calling convention registers.
		// Process args that are already in the right place first, then
		// handle moves that don't conflict, and use xchg for cycles.
		var placed = new bool[regArgCount];
		// Pass 1: mark args already in place
		for (int i = 0; i < regArgCount; i++) {
			if (argRegs[i] == CallConvRegs[i])
				placed[i] = true;
		}
		// Pass 2: repeatedly place args whose target is free
		bool progress = true;
		while (progress) {
			progress = false;
			for (int i = 0; i < regArgCount; i++) {
				if (placed[i]) continue;
				bool targetBlocked = false;
				for (int j = 0; j < regArgCount; j++) {
					if (j != i && !placed[j] && argRegs[j] == CallConvRegs[i]) {
						targetBlocked = true;
						break;
					}
				}
				if (!targetBlocked) {
					block.AddOp(new X86MovRegRegOp(CallConvRegs[i], argRegs[i]));
					placed[i] = true;
					progress = true;
				}
			}
		}
		// Pass 3: resolve remaining conflicts with xchg
		for (int i = 0; i < regArgCount; i++) {
			if (placed[i]) continue;
			block.AddOp(new X86XchgRegRegOp(argRegs[i], CallConvRegs[i]));
			// Update the other arg that was in the target register
			for (int j = 0; j < regArgCount; j++) {
				if (j != i && argRegs[j] == CallConvRegs[i]) {
					argRegs[j] = argRegs[i];
					break;
				}
			}
			placed[i] = true;
		}

		block.AddOp(new X86CallDirectOp(callee));

		// Clean up stack args
		if (stackArgBytes > 0)
			block.AddOp(new X86AddRegImmOp(X86Register.Rsp, stackArgBytes));

		// Caller-saved registers are clobbered by the call.
		InvalidateCallerSavedRegisters();

		if (result != null) {
			Assign(X86Register.Eax, result);
		}
	}

	/// <summary>
	/// Record that a function parameter has arrived in a calling convention register,
	/// or load it from the stack for 5th+ parameters.
	/// After push rbp / mov rbp, rsp: [rbp+16] = 5th param, [rbp+24] = 6th, etc.
	/// </summary>
	public void NoteParam(StdValue paramValue, int paramIndex, MlirBlock<X86Op> block) {
		if (paramIndex < CallConvRegs.Length) {
			Assign(CallConvRegs[paramIndex], paramValue);
		} else {
			int stackOffset = 16 + (paramIndex - CallConvRegs.Length) * 8;
			var gpr = AllocateRegister(paramValue, block);
			block.AddOp(new X86MovRegMemOp(gpr, stackOffset));
		}
	}

	/// <summary>
	/// After a call, caller-saved registers are clobbered. Remove any value mappings
	/// for those registers so stale values are never used.
	/// Windows x64 caller-saved: rax, rcx, rdx, r8, r9, r10, r11.
	/// </summary>
	private void InvalidateCallerSavedRegisters() {
		X86Register[] callerSaved = [
			X86Register.Eax, X86Register.Ecx, X86Register.Edx,
			X86Register.R8, X86Register.R9, X86Register.R10, X86Register.R11
		];
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
