using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Generic base class for register allocation during lowering conversions.
/// Parameterized by GPR type, FP register type, and op type.
/// </summary>
public abstract class RegisterManagerBase<TGpr, TFp, TOp>
    where TGpr : struct
    where TFp : struct
    where TOp : IPrintableOp {

  // Subclass provides the register pools and caller-saved sets
  protected abstract TGpr[] GetGprPool();
  protected abstract TFp[] GetFpPool();
  protected abstract TGpr[] GetCallerSavedGprs();
  protected abstract TFp[] GetCallerSavedFpRegs();
  protected abstract bool SamePhysicalRegister(TGpr a, TGpr? b);

  // Instruction emission primitives — subclass provides the actual instructions
  protected abstract void EmitImmediateToRegister(TGpr reg, long immediate, MlirBlock<TOp> block);
  protected abstract void EmitMovGpr(TGpr dest, TGpr src, MlirBlock<TOp> block);
  protected abstract void EmitSpillGprToStack(int offset, TGpr reg, MlirBlock<TOp> block);
  protected abstract void EmitReloadGprFromStack(TGpr reg, int offset, MlirBlock<TOp> block);
  protected abstract void EmitMovFp(TFp dest, TFp src, MlirBlock<TOp> block);
  protected abstract void EmitSpillFpToStack(int offset, TFp reg, MlirBlock<TOp> block);
  protected abstract void EmitReloadFpFromStack(TFp reg, int offset, MlirBlock<TOp> block);

  // Cached pool arrays (initialized lazily from the abstract methods)
  private TGpr[]? _gprPool;
  private TFp[]? _fpPool;
  private TGpr[]? _callerSavedGprs;
  private TFp[]? _callerSavedFpRegs;

  protected TGpr[] GprPool => _gprPool ??= GetGprPool();
  protected TFp[] FpPool => _fpPool ??= GetFpPool();
  protected TGpr[] CallerSavedGprs => _callerSavedGprs ??= GetCallerSavedGprs();
  protected TFp[] CallerSavedFpRegs => _callerSavedFpRegs ??= GetCallerSavedFpRegs();

  // --- GPR state ---
  protected int _nextFreshIndex;
  protected readonly Dictionary<TGpr, StdValue> _registerContents = [];
  protected readonly Dictionary<StdValue, TGpr> _valueToRegister = [];
  protected readonly Dictionary<StdValue, int> _valueStackHome = [];
  protected readonly Dictionary<TGpr, int> _lastUsed = [];
  protected readonly Dictionary<StdValue, long> _constantValues = [];
  protected readonly Dictionary<StdValue, TGpr> _registerHints = [];
  protected HashSet<StdValue>? _deferredValues;
  protected readonly Stack<HashSet<StdValue>> _syntheticScopes = new();
  protected readonly HashSet<StdValue> _allSyntheticValues = [];
  protected int _currentOpIndex;
  protected int _scratchIdCounter = -1000;

  // Spill slot allocation
  protected int _spillBaseOffset;
  protected int _nextSpillOffset;
  protected int _minSpillOffset;

  /// <summary>
  /// Total stack frame size including variables and spill slots.
  /// Aligned to 16 bytes. Returns 0 if no stack space is needed.
  /// </summary>
  public int TotalStackSize {
    get {
      int raw = -Math.Min(_nextSpillOffset, _minSpillOffset);
      return raw > 0 ? (raw + 15) & ~15 : 0;
    }
  }

  // --- FP register state ---
  protected int _nextFreshFpIndex;
  protected readonly Dictionary<TFp, StdValue> _fpContents = [];
  protected readonly Dictionary<StdValue, TFp> _valueToFp = [];
  protected readonly Dictionary<StdValue, int> _valueFpStackHome = [];
  protected readonly Dictionary<TFp, int> _fpLastUsed = [];
  protected readonly Dictionary<StdValue, TFp> _valuePreviousFp = [];

  /// <summary>
  /// Values only consumed by sink ops (return/call) skip eager register materialization.
  /// </summary>
  public HashSet<StdValue>? DeferredValues { set => _deferredValues = value; }

  /// <summary>
  /// Record a preferred register for a value. AllocateRegister will try to
  /// honor this hint when the preferred register is available.
  /// </summary>
  public void SetRegisterHint(StdValue value, TGpr preferredReg) {
    _registerHints[value] = preferredReg;
  }

  /// <summary>
  /// Allocate a physical register for a new value.
  /// May evict an existing value if all registers are occupied.
  /// Protected registers will not be evicted.
  /// </summary>
  protected TGpr AllocateRegister(StdValue value, MlirBlock<TOp> block,
    TGpr? protect1 = null, TGpr? protect2 = null) {
    // 0. Try the hinted register first
    if (_registerHints.TryGetValue(value, out var hinted)) {
      if (!SamePhysicalRegister(hinted, protect1) && !SamePhysicalRegister(hinted, protect2)) {
        if (!_registerContents.ContainsKey(hinted)) {
          Assign(hinted, value);
          return hinted;
        }
        // If the hinted register holds a rematerializable constant, evict it
        if (_registerContents.TryGetValue(hinted, out var occupant) && _constantValues.ContainsKey(occupant)) {
          _valueToRegister.Remove(occupant);
          _registerContents.Remove(hinted);
          _lastUsed.Remove(hinted);
          Assign(hinted, value);
          return hinted;
        }
      }
    }

    // 1. Try the next sequential slot
    if (_nextFreshIndex < GprPool.Length) {
      var candidate = GprPool[_nextFreshIndex];
      if (!_registerContents.ContainsKey(candidate)
          && !SamePhysicalRegister(candidate, protect1) && !SamePhysicalRegister(candidate, protect2)) {
        _nextFreshIndex++;
        Assign(candidate, value);
        return candidate;
      }
    }

    // 2. Find any free register in pool order
    foreach (var reg in GprPool) {
      if (!_registerContents.ContainsKey(reg)
          && !SamePhysicalRegister(reg, protect1) && !SamePhysicalRegister(reg, protect2)) {
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
  protected TGpr EnsureInRegister(StdValue value, MlirBlock<TOp> block,
    TGpr? protect1 = null, TGpr? protect2 = null) {
    // Already in a register
    if (_valueToRegister.TryGetValue(value, out var reg)) {
      _lastUsed[reg] = _currentOpIndex;
      return reg;
    }

    // Constant rematerialization
    if (_constantValues.TryGetValue(value, out var immediate)) {
      var reloadReg = AllocateRegister(value, block, protect1, protect2);
      EmitImmediateToRegister(reloadReg, immediate, block);
      return reloadReg;
    }

    // Not in a register — must reload from stack
    if (_valueStackHome.TryGetValue(value, out var displacement)) {
      var reloadReg = AllocateRegister(value, block, protect1, protect2);
      EmitReloadGprFromStack(reloadReg, displacement, block);
      return reloadReg;
    }

    throw new InvalidOperationException($"RegisterManager: value %{value.Id} has no register and no stack home");
  }

  /// <summary>
  /// Allocate a register and load an immediate value into it.
  /// If the value is in the deferred set, only records it for later rematerialization.
  /// </summary>
  public void EmitLoadImmediate(StdValue result, long immediate, MlirBlock<TOp> block) {
    _constantValues[result] = immediate;
    if (_deferredValues?.Contains(result) == true) return;
    var gpr = AllocateRegister(result, block);
    EmitImmediateToRegister(gpr, immediate, block);
  }

  /// <summary>
  /// Allocate an FP register for a new float value.
  /// Prefers reusing the previous register if it's now free.
  /// </summary>
  protected TFp AllocateFpRegister(StdValue value) {
    // If this value had a previous register and it's now free, reuse it
    if (_valuePreviousFp.TryGetValue(value, out var previousReg) && !_fpContents.ContainsKey(previousReg)) {
      _valuePreviousFp.Remove(value);
      AssignFp(previousReg, value);
      return previousReg;
    }

    if (_nextFreshFpIndex < FpPool.Length) {
      var candidate = FpPool[_nextFreshFpIndex];
      if (!_fpContents.ContainsKey(candidate)) {
        _nextFreshFpIndex++;
        AssignFp(candidate, value);
        return candidate;
      }
    }

    foreach (var reg in FpPool) {
      if (!_fpContents.ContainsKey(reg)) {
        AssignFp(reg, value);
        return reg;
      }
    }

    throw new InvalidOperationException("RegisterManager: all FP registers occupied");
  }

  /// <summary>
  /// Ensure a float value is in an FP register, reloading from stack if needed.
  /// </summary>
  protected TFp EnsureInFpRegister(StdValue value, MlirBlock<TOp> block) {
    if (_valueToFp.TryGetValue(value, out var reg)) {
      _fpLastUsed[reg] = _currentOpIndex;
      return reg;
    }

    if (_valueFpStackHome.TryGetValue(value, out var displacement)) {
      var reloadReg = AllocateFpRegister(value);
      EmitReloadFpFromStack(reloadReg, displacement, block);
      return reloadReg;
    }

    throw new InvalidOperationException($"RegisterManager: float value %{value.Id} has no FP register and no stack home");
  }

  protected void Assign(TGpr reg, StdValue value) {
    // Detect value ID collisions
    if (_valueToRegister.TryGetValue(value, out var existingReg)
        && _registerContents.TryGetValue(existingReg, out var existingValue)
        && !ReferenceEquals(existingValue, value))
      throw new InvalidOperationException(
        $"RegisterManager: value ID collision — two different StdValue objects share ID {value.Id}");
    _registerContents[reg] = value;
    _valueToRegister[value] = reg;
    _lastUsed[reg] = _currentOpIndex;
  }

  protected void AssignFp(TFp reg, StdValue value) {
    _fpContents[reg] = value;
    _valueToFp[value] = reg;
    _fpLastUsed[reg] = _currentOpIndex;
  }

  protected void TransferValue(StdValue oldValue, TGpr reg, StdValue newValue) {
    if (_valueToRegister.TryGetValue(oldValue, out var oldReg) && EqualityComparer<TGpr>.Default.Equals(oldReg, reg)) {
      _valueToRegister.Remove(oldValue);
    }
    _registerContents[reg] = newValue;
    _valueToRegister[newValue] = reg;
    _lastUsed[reg] = _currentOpIndex;
  }

  protected void Evict(MlirBlock<TOp> block, TGpr? protect1 = null, TGpr? protect2 = null) {
    var bestReg = FindLruRegister(protect1, protect2, requireConstant: true)
      ?? FindLruRegister(protect1, protect2, requireStackHome: true)
      ?? FindLruRegister(protect1, protect2, requireStackHome: false);

    if (bestReg != null) {
      SpillRegisterIfOccupied(bestReg.Value, block);
      return;
    }

    throw new InvalidOperationException("RegisterManager: no registers to evict");
  }

  protected TGpr? FindLruRegister(TGpr? protect1, TGpr? protect2,
      bool requireStackHome = false, bool requireConstant = false) {
    TGpr? bestReg = null;
    int bestLastUsed = int.MaxValue;

    foreach (var (reg, value) in _registerContents) {
      if (SamePhysicalRegister(reg, protect1) || SamePhysicalRegister(reg, protect2)) continue;
      if (requireConstant && !_constantValues.ContainsKey(value)) continue;
      if (requireStackHome && !_valueStackHome.ContainsKey(value)) continue;
      var lastUsed = _lastUsed.GetValueOrDefault(reg, 0);
      if (lastUsed < bestLastUsed) {
        bestLastUsed = lastUsed;
        bestReg = reg;
      }
    }

    return bestReg;
  }

  protected void SpillRegisterIfOccupied(TGpr reg, MlirBlock<TOp> block) {
    if (!_registerContents.TryGetValue(reg, out var existing)) return;
    if (!_valueStackHome.ContainsKey(existing) && !_constantValues.ContainsKey(existing)) {
      _nextSpillOffset -= 8;
      EmitSpillGprToStack(_nextSpillOffset, reg, block);
      _valueStackHome[existing] = _nextSpillOffset;
    }
    _valueToRegister.Remove(existing);
    _registerContents.Remove(reg);
    _lastUsed.Remove(reg);
  }

  protected void SpillFpIfOccupied(TFp reg, MlirBlock<TOp> block) {
    if (!_fpContents.TryGetValue(reg, out var existing)) return;
    if (!_valueFpStackHome.ContainsKey(existing)) {
      _nextSpillOffset -= 8;
      EmitSpillFpToStack(_nextSpillOffset, reg, block);
      _valueFpStackHome[existing] = _nextSpillOffset;
    }
    _valueToFp.Remove(existing);
    _fpContents.Remove(reg);
    _fpLastUsed.Remove(reg);
  }

  public void SpillAllLiveRegisters(MlirBlock<TOp> block) {
    foreach (var reg in CallerSavedGprs) {
      if (_registerContents.TryGetValue(reg, out var value)
        && !_valueStackHome.ContainsKey(value)
        && !_constantValues.ContainsKey(value)) {
        _nextSpillOffset -= 8;
        EmitSpillGprToStack(_nextSpillOffset, reg, block);
        _valueStackHome[value] = _nextSpillOffset;
      }
    }
    foreach (var fp in CallerSavedFpRegs) {
      if (_fpContents.TryGetValue(fp, out var value)
        && !_valueFpStackHome.ContainsKey(value)) {
        _nextSpillOffset -= 8;
        EmitSpillFpToStack(_nextSpillOffset, fp, block);
        _valueFpStackHome[value] = _nextSpillOffset;
      }
    }
  }

  protected void InvalidateGpr(TGpr reg) {
    if (_registerContents.TryGetValue(reg, out var value)) {
      _valueToRegister.Remove(value);
      _registerContents.Remove(reg);
      _lastUsed.Remove(reg);
    }
  }

  protected void InvalidateCallerSavedRegisters() {
    foreach (var reg in CallerSavedGprs) {
      if (_registerContents.TryGetValue(reg, out var value)) {
        _valueToRegister.Remove(value);
        _registerContents.Remove(reg);
        _lastUsed.Remove(reg);
      }
    }
    foreach (var fp in CallerSavedFpRegs) {
      if (_fpContents.TryGetValue(fp, out var value)) {
        _valuePreviousFp[value] = fp;
        _valueToFp.Remove(value);
        _fpContents.Remove(fp);
        _fpLastUsed.Remove(fp);
      }
    }
  }

  public void NoteValueDead(StdValue value) {
    if (_valueToRegister.TryGetValue(value, out var reg)) {
      _valueToRegister.Remove(value);
      _registerContents.Remove(reg);
      _lastUsed.Remove(reg);
    }

    if (_valueToFp.TryGetValue(value, out var fpReg)) {
      _valueToFp.Remove(value);
      _fpContents.Remove(fpReg);
      _fpLastUsed.Remove(fpReg);
    }
  }

  protected void NoteStoreToStack(StdValue value, int displacement) {
    _valueStackHome[value] = displacement;
  }

  protected void NoteFpStoreToStack(StdValue value, int displacement) {
    _valueFpStackHome[value] = displacement;
  }

  public void AdvanceOp() {
    _currentOpIndex++;
  }

  public void SetSpillBaseOffset(int offset) {
    _spillBaseOffset = offset;
    _nextSpillOffset = offset;
    _minSpillOffset = offset;
  }

  public void Reset() {
    _nextFreshIndex = 0;
    _registerContents.Clear();
    _valueToRegister.Clear();
    _valueStackHome.Clear();
    _lastUsed.Clear();
    _currentOpIndex = 0;

    _nextFreshFpIndex = 0;
    _fpContents.Clear();
    _valueToFp.Clear();
    _valueFpStackHome.Clear();
    _fpLastUsed.Clear();
    _registerHints.Clear();
    _syntheticScopes.Clear();
    _allSyntheticValues.Clear();

    if (_nextSpillOffset < _minSpillOffset)
      _minSpillOffset = _nextSpillOffset;
    _nextSpillOffset = _spillBaseOffset;
  }

  public void ResetForBlockTransition(MlirBlock<TOp> block) {
    var spillOps = new List<TOp>();

    foreach (var reg in GprPool) {
      if (_registerContents.TryGetValue(reg, out var value)
        && !_valueStackHome.ContainsKey(value)
        && !_constantValues.ContainsKey(value)) {
        _nextSpillOffset -= 8;
        EmitSpillGprToStackCollect(spillOps, _nextSpillOffset, reg);
        _valueStackHome[value] = _nextSpillOffset;
      }
    }
    foreach (var fp in FpPool) {
      if (_fpContents.TryGetValue(fp, out var value)
        && !_valueFpStackHome.ContainsKey(value)) {
        _nextSpillOffset -= 8;
        EmitSpillFpToStackCollect(spillOps, _nextSpillOffset, fp);
        _valueFpStackHome[value] = _nextSpillOffset;
      }
    }

    if (spillOps.Count > 0) {
      int insertIdx = block.Operations.Count;
      while (insertIdx > 0 && IsTerminator(block.Operations[insertIdx - 1])) {
        insertIdx--;
      }
      block.Operations.InsertRange(insertIdx, spillOps);
    }

    _nextFreshIndex = 0;
    _registerContents.Clear();
    _valueToRegister.Clear();
    _lastUsed.Clear();
    _currentOpIndex = 0;

    _nextFreshFpIndex = 0;
    _fpContents.Clear();
    _valueToFp.Clear();
    _fpLastUsed.Clear();
    _registerHints.Clear();
    _syntheticScopes.Clear();
    _allSyntheticValues.Clear();
  }

  /// <summary>
  /// Snapshot of register allocator state for carrying assignments across diverging blocks.
  /// </summary>
  public sealed class RegisterSnapshot {
    internal Dictionary<TGpr, StdValue> RegisterContents = [];
    internal Dictionary<StdValue, TGpr> ValueToRegister = [];
    internal Dictionary<StdValue, int> ValueStackHome = [];
    internal Dictionary<StdValue, long> ConstantValues = [];
    internal Dictionary<TFp, StdValue> FpContents = [];
    internal Dictionary<StdValue, TFp> ValueToFp = [];
    internal Dictionary<StdValue, int> ValueFpStackHome = [];
    internal Dictionary<StdValue, TFp> ValuePreviousFp = [];
    internal int NextSpillOffset;
    internal int MinSpillOffset;
  }

  /// <summary>
  /// Capture the current register allocator state so it can be restored later.
  /// Used to carry register assignments across diverging (noreturn) blocks.
  /// </summary>
  public RegisterSnapshot SaveState() {
    return new RegisterSnapshot {
      RegisterContents = new(_registerContents),
      ValueToRegister = new(_valueToRegister),
      ValueStackHome = new(_valueStackHome),
      ConstantValues = new(_constantValues),
      FpContents = new(_fpContents),
      ValueToFp = new(_valueToFp),
      ValueFpStackHome = new(_valueFpStackHome),
      ValuePreviousFp = new(_valuePreviousFp),
      NextSpillOffset = _nextSpillOffset,
      MinSpillOffset = _minSpillOffset,
    };
  }

  /// <summary>
  /// Restore a previously saved register allocator state.
  /// Resets per-block counters while preserving value-to-register mappings.
  /// </summary>
  public void RestoreState(RegisterSnapshot state) {
    _registerContents.Clear();
    foreach (var kv in state.RegisterContents) _registerContents[kv.Key] = kv.Value;
    _valueToRegister.Clear();
    foreach (var kv in state.ValueToRegister) _valueToRegister[kv.Key] = kv.Value;
    _valueStackHome.Clear();
    foreach (var kv in state.ValueStackHome) _valueStackHome[kv.Key] = kv.Value;
    _constantValues.Clear();
    foreach (var kv in state.ConstantValues) _constantValues[kv.Key] = kv.Value;
    _fpContents.Clear();
    foreach (var kv in state.FpContents) _fpContents[kv.Key] = kv.Value;
    _valueToFp.Clear();
    foreach (var kv in state.ValueToFp) _valueToFp[kv.Key] = kv.Value;
    _valueFpStackHome.Clear();
    foreach (var kv in state.ValueFpStackHome) _valueFpStackHome[kv.Key] = kv.Value;
    _valuePreviousFp.Clear();
    foreach (var kv in state.ValuePreviousFp) _valuePreviousFp[kv.Key] = kv.Value;
    // Preserve spill offset watermarks: the diverging block's ResetForBlockTransition
    // may have allocated spill slots that must not be reused. Only restore if the saved
    // offsets are further from the base (more negative = more allocated).
    if (state.NextSpillOffset < _nextSpillOffset)
      _nextSpillOffset = state.NextSpillOffset;
    if (state.MinSpillOffset < _minSpillOffset)
      _minSpillOffset = state.MinSpillOffset;

    _nextFreshIndex = 0;
    _nextFreshFpIndex = 0;
    _lastUsed.Clear();
    _fpLastUsed.Clear();
    _currentOpIndex = 0;
    _registerHints.Clear();
    _syntheticScopes.Clear();
    _allSyntheticValues.Clear();
  }

  /// <summary>
  /// Create a spill op for collecting into a list (for block transition insertion).
  /// Subclass returns the appropriate instruction.
  /// </summary>
  protected abstract TOp MakeSpillGprOp(int offset, TGpr reg);
  protected abstract TOp MakeSpillFpOp(int offset, TFp reg);
  protected abstract bool IsTerminator(TOp op);

  private void EmitSpillGprToStackCollect(List<TOp> ops, int offset, TGpr reg) {
    ops.Add(MakeSpillGprOp(offset, reg));
  }

  private void EmitSpillFpToStackCollect(List<TOp> ops, int offset, TFp reg) {
    ops.Add(MakeSpillFpOp(offset, reg));
  }

  /// <summary>
  /// Tracks synthetic values created within destructor helpers.
  /// </summary>
  public sealed class SyntheticScope : IDisposable {
    private readonly RegisterManagerBase<TGpr, TFp, TOp> _mgr;

    public SyntheticScope(RegisterManagerBase<TGpr, TFp, TOp> mgr) {
      _mgr = mgr;
      _mgr._syntheticScopes.Push([]);
    }

    public StdI64 CreateValue() {
      var v = new StdI64(MlirContext.Current.NextId());
      _mgr._syntheticScopes.Peek().Add(v);
      _mgr._allSyntheticValues.Add(v);
      return v;
    }

    public StdPtr CreatePtr() {
      var v = new StdPtr(MlirContext.Current.NextId());
      _mgr._syntheticScopes.Peek().Add(v);
      _mgr._allSyntheticValues.Add(v);
      return v;
    }

    /// <summary>
    /// Promote a synthetic value to a normal value that survives calls.
    /// </summary>
    public void KeepAlive(StdValue v, MlirBlock<TOp> block) {
      _mgr._syntheticScopes.Peek().Remove(v);
      _mgr._allSyntheticValues.Remove(v);
      _mgr.EnsureSpilled(v, block);
    }

    public void Dispose() {
      var set = _mgr._syntheticScopes.Pop();
      foreach (var v in set) {
        _mgr._allSyntheticValues.Remove(v);
        _mgr.NoteValueDead(v);
      }
    }
  }

  public SyntheticScope BeginSyntheticScope() => new(this);

  /// <summary>
  /// Force a value to have a stack home by spilling it immediately.
  /// </summary>
  public void EnsureSpilled(StdValue value, MlirBlock<TOp> block) {
    if (_valueStackHome.ContainsKey(value)) return;
    var reg = EnsureInRegister(value, block);
    _nextSpillOffset -= 8;
    EmitSpillGprToStack(_nextSpillOffset, reg, block);
    _valueStackHome[value] = _nextSpillOffset;
  }
}

/// <summary>
/// Shared analysis utilities for backend conversions.
/// </summary>
public static class BlockAnalysis {
  /// <summary>
  /// Detect diverging blocks — blocks containing a noreturn call (maxon_panic, maxon_panic_dynamic)
  /// with no branch or return terminator. These blocks never transfer control to a successor,
  /// so register state can be carried across them to avoid unnecessary spills.
  /// </summary>
  public static HashSet<int> FindDivergingBlocks(List<MlirBlock<StandardOp>> sourceBlocks) {
    var result = new HashSet<int>();
    for (int bi = 0; bi < sourceBlocks.Count; bi++) {
      bool hasNoreturnCall = sourceBlocks[bi].Operations
        .OfType<StdCallRuntimeOp>()
        .Any(op => op.Callee is "maxon_panic" or "maxon_panic_dynamic");
      bool hasTerminator = sourceBlocks[bi].Operations
        .Any(op => op is StdCondBrOp or StdBrOp or StdReturnOp);
      if (hasNoreturnCall && !hasTerminator)
        result.Add(bi);
    }
    return result;
  }
}
