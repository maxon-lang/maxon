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

	// Spill slot allocation: offsets grow downward (more negative) from the variable area
	private int _spillBaseOffset;
	private int _nextSpillOffset;

	/// <summary>
	/// Total stack frame size including variables and spill slots, aligned to 16 bytes.
	/// Returns 0 if no stack space is needed.
	/// </summary>
	public int TotalStackSize {
		get {
			int raw = -_nextSpillOffset;
			return raw > 0 ? (raw + 15) & ~15 : 0;
		}
	}

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
	/// Protected registers will not be evicted.
	/// </summary>
	private X86Register AllocateRegister(StdValue value, MlirBlock<X86Op> block,
		X86Register? protect1 = null, X86Register? protect2 = null) {
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
		Evict(block, protect1, protect2);

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
	/// Protected registers will not be evicted during allocation.
	/// </summary>
	private X86Register EnsureInRegister(StdValue value, MlirBlock<X86Op> block,
		X86Register? protect1 = null, X86Register? protect2 = null) {
		// Already in a register
		if (_valueToRegister.TryGetValue(value, out var reg)) {
			_lastUsed[reg] = _currentOpIndex;
			return reg;
		}

		// Not in a register — must reload from stack
		if (_valueStackHome.TryGetValue(value, out var displacement)) {
			var reloadReg = AllocateRegister(value, block, protect1, protect2);
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
		if (immediate == 0)
			block.AddOp(new X86XorRegRegOp(gpr, gpr));
		else
			block.AddOp(new X86MovRegImmOp(gpr, immediate));
	}

	/// <summary>
	/// Emit a two-operand register-register instruction (e.g. add, sub).
	/// When lhsConsumed is true, reuses the LHS register directly (no extra mov).
	/// When false, allocates a separate result register and copies LHS into it first.
	/// </summary>
	public void EmitBinaryRegReg(StdValue lhs, StdValue rhs, StdValue result,
		MlirBlock<X86Op> block, Func<X86Register, X86Register, X86Op> makeOp,
		bool lhsConsumed = false) {
		var rhsReg = EnsureInRegister(rhs, block);
		var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);
		if (lhsConsumed) {
			TransferValue(lhs, lhsReg, result);
			block.AddOp(makeOp(lhsReg, rhsReg));
		} else {
			var resultReg = AllocateRegister(result, block, protect1: lhsReg, protect2: rhsReg);
			if (resultReg != lhsReg) {
				block.AddOp(new X86MovRegRegOp(resultReg, lhsReg));
			}
			block.AddOp(makeOp(resultReg, rhsReg));
		}
	}

	/// <summary>
	/// Emit IMUL (integer multiplication).
	/// Result = lhs * rhs.
	/// </summary>
	public void EmitMultiply(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block,
		bool lhsConsumed = false) {
		EmitBinaryRegReg(lhs, rhs, result, block, (l, r) => new X86ImulRegRegOp(l, r), lhsConsumed);
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
		var rhsReg = EnsureInRegister(rhs, block);
		var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);

		// Divisor must not be in RAX or RDX (IDIV clobbers both).
		if (rhsReg == X86Register.Eax || rhsReg == X86Register.Edx) {
			var safeReg = FindSafeRegisterForIdiv(lhsReg, rhsReg);
			SpillRegisterIfOccupied(safeReg, block);
			block.AddOp(new X86MovRegRegOp(safeReg, rhsReg));
			rhsReg = safeReg;
		}

		// IDIV clobbers both EAX and EDX. Spill any live values in those registers
		// before we overwrite them.
		SpillRegisterIfOccupied(X86Register.Eax, block);
		SpillRegisterIfOccupied(X86Register.Edx, block);

		if (lhsReg != X86Register.Eax) {
			block.AddOp(new X86MovRegRegOp(X86Register.Eax, lhsReg));
		}

		// Sign-extend RAX into RDX:RAX
		block.AddOp(new X86CqoOp());
		block.AddOp(new X86IdivRegOp(rhsReg));

		Assign(resultRegister, result);
	}

	/// <summary>
	/// Find a register that is safe to use as a temporary for IDIV divisor relocation.
	/// Must not be EAX, EDX, the LHS register, or the current divisor register.
	/// Prefers a free register, but will return an occupied one (caller must spill it).
	/// </summary>
	private X86Register FindSafeRegisterForIdiv(X86Register lhsReg, X86Register divisorReg) {
		X86Register? fallback = null;
		foreach (var reg in GprPool) {
			if (reg == X86Register.Eax || reg == X86Register.Edx) continue;
			if (reg == lhsReg || reg == divisorReg) continue;
			if (!_registerContents.ContainsKey(reg))
				return reg;
			fallback ??= reg;
		}
		if (fallback != null)
			return fallback.Value;
		throw new InvalidOperationException("RegisterManager: no safe register for IDIV divisor relocation");
	}

	/// <summary>
	/// If a register contains a live value without a stack home, spill it.
	/// Then remove the register mapping so the register can be reused.
	/// </summary>
	private void SpillRegisterIfOccupied(X86Register reg, MlirBlock<X86Op> block) {
		if (!_registerContents.TryGetValue(reg, out var existing)) return;
		if (!_valueStackHome.ContainsKey(existing)) {
			_nextSpillOffset -= 8;
			block.AddOp(new X86MovMemRegOp(_nextSpillOffset, reg));
			_valueStackHome[existing] = _nextSpillOffset;
		}
		_valueToRegister.Remove(existing);
		_registerContents.Remove(reg);
		_lastUsed.Remove(reg);
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
		var xmmReg = AllocateXmmRegister(result);
		block.AddOp(new X86MovSdXmmMemOp(xmmReg, offset));
	}

	/// <summary>
	/// Allocate an XMM register and load a float constant from rdata.
	/// </summary>
	public void EmitXmmLoadFromRipRelative(StdValue result, string rdataLabel, MlirBlock<X86Op> block) {
		var xmmReg = AllocateXmmRegister(result);
		block.AddOp(new X86MovSdXmmRipRelOp(xmmReg, rdataLabel));
	}

	/// <summary>
	/// Ensure both GPR operands are in registers and emit cmp.
	/// </summary>
	public void EmitIntegerCompare(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
		var rhsReg = EnsureInRegister(rhs, block);
		var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);
		block.AddOp(new X86CmpRegRegOp(lhsReg, rhsReg));
	}

	/// <summary>
	/// Ensure a bool value is in a register and emit test reg, reg.
	/// Sets ZF=1 when value is 0 (false).
	/// </summary>
	public void EmitBoolTest(StdValue value, MlirBlock<X86Op> block) {
		var reg = EnsureInRegister(value, block);
		block.AddOp(new X86TestRegRegOp(reg, reg));
	}

	/// <summary>
	/// Materialize a comparison result (from CPU flags) into a GPR via setcc + movzx.
	/// </summary>
	public void EmitSetcc(StdValue result, string condition, MlirBlock<X86Op> block) {
		var reg = AllocateRegister(result, block);
		block.AddOp(new X86SetccOp(condition, reg));
		block.AddOp(new X86MovzxRegOp(reg));
	}

	/// <summary>
	/// Emit an XMM binary operation (e.g. addsd).
	/// </summary>
	public void EmitXmmBinaryRegReg(StdValue lhs, StdValue rhs, StdValue result,
		MlirBlock<X86Op> block, Func<X86XmmRegister, X86XmmRegister, X86Op> makeOp) {
		var rhsXmm = EnsureInXmmRegister(rhs, block);
		var lhsXmm = EnsureInXmmRegister(lhs, block);
		var resultXmm = AllocateXmmRegister(result);
		if (resultXmm != lhsXmm) {
			block.AddOp(new X86MovSdXmmXmmOp(resultXmm, lhsXmm));
		}
		block.AddOp(makeOp(resultXmm, rhsXmm));
	}

	/// <summary>
	/// Emit cvttsd2si: convert XMM float to GPR integer with truncation.
	/// </summary>
	public void EmitCvttSd2Si(StdValue input, StdValue result, MlirBlock<X86Op> block) {
		var srcXmm = EnsureInXmmRegister(input, block);
		var destGpr = AllocateRegister(result, block);
		block.AddOp(new X86CvttSd2SiOp(destGpr, srcXmm));
	}

	/// <summary>
	/// Emit a unary XMM operation where the instruction reads src and writes dest
	/// independently (e.g. sqrtsd, roundsd). No preliminary copy needed.
	/// </summary>
	public void EmitXmmUnaryRegReg(StdValue input, StdValue result, MlirBlock<X86Op> block,
		Func<X86XmmRegister, X86XmmRegister, X86Op> makeOp) {
		var srcXmm = EnsureInXmmRegister(input, block);
		var resultXmm = AllocateXmmRegister(result);
		block.AddOp(makeOp(resultXmm, srcXmm));
	}

	/// <summary>
	/// Emit absf64: clear sign bit via ANDPD with rdata mask.
	/// </summary>
	public void EmitAbsF64(StdValue input, StdValue result, string maskLabel, MlirBlock<X86Op> block) {
		var srcXmm = EnsureInXmmRegister(input, block);
		var resultXmm = AllocateXmmRegister(result);
		if (resultXmm != srcXmm) {
			block.AddOp(new X86MovSdXmmXmmOp(resultXmm, srcXmm));
		}
		block.AddOp(new X86AndpdRipRelOp(resultXmm, maskLabel));
	}

	/// <summary>
	/// Ensure both XMM operands are in registers and emit ucomisd.
	/// </summary>
	public void EmitXmmCompare(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
		var rhsReg = EnsureInXmmRegister(rhs, block);
		var lhsReg = EnsureInXmmRegister(lhs, block);
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

		// Spill caller-saved registers that hold live values without a stack home.
		// This must happen before argument placement overwrites those registers.
		var argSet = new HashSet<StdValue>(args);
		SpillCallerSavedRegisters(block, argSet);

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

		// Snapshot where each register arg currently lives (register or stack).
		// This avoids calling EnsureInRegister in a loop where each call
		// can evict the previous arg under high register pressure.
		var argSources = new X86Register?[regArgCount];
		int?[] argStackHomes = new int?[regArgCount];
		for (int i = 0; i < regArgCount; i++) {
			if (_valueToRegister.TryGetValue(args[i], out var reg))
				argSources[i] = reg;
			else if (_valueStackHome.TryGetValue(args[i], out var disp))
				argStackHomes[i] = disp;
			else
				throw new InvalidOperationException($"RegisterManager: call arg %{args[i].Id} has no register and no stack home");
		}

		// Move args to their target calling convention registers.
		// We track the actual current physical state of registers throughout
		// the placement process.
		var placed = new bool[regArgCount];

		// Pass 1: mark args already in their target register
		for (int i = 0; i < regArgCount; i++) {
			if (argSources[i] == CallConvRegs[i])
				placed[i] = true;
		}

		// Pass 2: repeatedly place register args whose target is free
		bool progress = true;
		while (progress) {
			progress = false;
			for (int i = 0; i < regArgCount; i++) {
				if (placed[i]) continue;
				bool targetBlocked = false;
				for (int j = 0; j < regArgCount; j++) {
					if (j != i && !placed[j] && argSources[j] == CallConvRegs[i]) {
						targetBlocked = true;
						break;
					}
				}
				if (!targetBlocked) {
					if (argSources[i] is { } srcReg)
						block.AddOp(new X86MovRegRegOp(CallConvRegs[i], srcReg));
					else
						block.AddOp(new X86MovRegMemOp(CallConvRegs[i], argStackHomes[i]!.Value));
					placed[i] = true;
					progress = true;
				}
			}
		}

		// Pass 3: resolve remaining conflicts with xchg (only register-register cycles)
		for (int i = 0; i < regArgCount; i++) {
			if (placed[i]) continue;
			block.AddOp(new X86XchgRegRegOp(argSources[i]!.Value, CallConvRegs[i]));
			// Update the other arg that was in the target register
			for (int j = 0; j < regArgCount; j++) {
				if (j != i && argSources[j] == CallConvRegs[i]) {
					argSources[j] = argSources[i];
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

	private static readonly X86Register[] CallerSavedRegisters = [
		X86Register.Eax, X86Register.Ecx, X86Register.Edx,
		X86Register.R8, X86Register.R9, X86Register.R10, X86Register.R11
	];

	/// <summary>
	/// Before a call, spill any caller-saved register values that don't have
	/// a stack home yet, skipping values that are call arguments (they will
	/// be consumed by the call and don't need preserving).
	/// </summary>
	private void SpillCallerSavedRegisters(MlirBlock<X86Op> block, HashSet<StdValue> callArgs) {
		foreach (var reg in CallerSavedRegisters) {
			if (_registerContents.TryGetValue(reg, out var value)
				&& !_valueStackHome.ContainsKey(value)
				&& !callArgs.Contains(value)) {
				_nextSpillOffset -= 8;
				block.AddOp(new X86MovMemRegOp(_nextSpillOffset, reg));
				_valueStackHome[value] = _nextSpillOffset;
			}
		}
	}

	/// <summary>
	/// After a call, caller-saved registers are clobbered. Remove any value mappings
	/// for those registers so stale values are never used.
	/// Windows x64 caller-saved: rax, rcx, rdx, r8, r9, r10, r11.
	/// </summary>
	private void InvalidateCallerSavedRegisters() {
		foreach (var reg in CallerSavedRegisters) {
			if (_registerContents.TryGetValue(reg, out var value)) {
				_valueToRegister.Remove(value);
				_registerContents.Remove(reg);
				_lastUsed.Remove(reg);
			}
		}
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
	private X86XmmRegister AllocateXmmRegister(StdValue value) {
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
			var reloadReg = AllocateXmmRegister(value);
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

	/// <summary>
	/// Set the base offset for spill slot allocation.
	/// This should be the negative offset just past the last variable slot
	/// (e.g. -16 if variables occupy offsets -8 and -16).
	/// </summary>
	public void SetSpillBaseOffset(int offset) {
		_spillBaseOffset = offset;
		_nextSpillOffset = offset;
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

		_nextSpillOffset = _spillBaseOffset;
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
	/// Find the least-recently-used register, optionally filtering to values with a stack home.
	/// Returns null if no eligible register is found.
	/// </summary>
	private X86Register? FindLruRegister(X86Register? protect1, X86Register? protect2, bool requireStackHome) {
		X86Register? bestReg = null;
		int bestLastUsed = int.MaxValue;

		foreach (var (reg, value) in _registerContents) {
			if (reg == protect1 || reg == protect2) continue;
			if (requireStackHome && !_valueStackHome.ContainsKey(value)) continue;
			var lastUsed = _lastUsed.GetValueOrDefault(reg, 0);
			if (lastUsed < bestLastUsed) {
				bestLastUsed = lastUsed;
				bestReg = reg;
			}
		}

		return bestReg;
	}

	/// <summary>
	/// Evict the least-recently-used register value.
	/// Prefers values that already have a stack home (no spill store needed).
	/// If no value has a stack home, allocates a spill slot and stores the value.
	/// Protected registers will not be evicted.
	/// </summary>
	private void Evict(MlirBlock<X86Op> block, X86Register? protect1 = null, X86Register? protect2 = null) {
		// Prefer evicting values that have a stack home (no spill store needed)
		var bestReg = FindLruRegister(protect1, protect2, requireStackHome: true)
			?? FindLruRegister(protect1, protect2, requireStackHome: false);

		if (bestReg != null) {
			SpillRegisterIfOccupied(bestReg.Value, block);
			return;
		}

		throw new InvalidOperationException("RegisterManager: no registers to evict");
	}
}
