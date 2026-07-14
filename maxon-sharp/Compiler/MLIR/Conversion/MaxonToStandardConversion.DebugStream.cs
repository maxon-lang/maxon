using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Runtime;

namespace MaxonSharp.Compiler.Ir.Conversion;

/// <summary>
/// Lowering for the `__DebugStream` builtin — the one thing that lets USER MAXON SOURCE put a
/// structured event into the shared-memory ring the runtime already carries (Workstream O).
///
/// The two gates live HERE, because this is the only place that knows both what the source
/// asked for and whether the ring exists:
///
///   * COMPILE-TIME. `--debugstream` off ⇒ the emitting ops lower to NOTHING. Not a branch that
///     is never taken: no instructions at all. The one exception is `enabled()`, which must
///     still produce a value — it folds to the constant `false`, which every later fold and
///     dead-branch elimination then sees.
///   * RUNTIME. `__ds_base == 0` ⇒ bail INLINE, before any CALL. Each emitting op lowers to a
///     load of `__ds_base` and a StdCallRuntimeIfNonzeroOp, so the detached case costs a load,
///     a test and a not-taken branch — never a call. (The MM events pay two real CALLs before
///     their runtime-off check. That wart is not reproduced.)
/// </summary>
public static partial class MaxonToStandardConversion {

  // Compile-time intern table for the names Log events carry. A name becomes a u16 here and a
  // real string only in the MXDS_STRS blob the monitor reads out of the PE — which is what
  // keeps the structured tier ZERO-ALLOC: a pass can emit an event from inside the register
  // allocator without allocating into the very `mm` stream the trace exists to read.
  //
  // Index 0 is reserved for "no name", exactly as the tag table reserves it, so a zeroed field
  // never resolves to a real name.
  [ThreadStatic] private static Dictionary<string, int>? _debugStreamNameIndexMap;
  [ThreadStatic] private static int _nextDebugStreamNameIndex;

  private const int DebugStreamFirstNameIndex = 1;

  /// Reset per compile, from Run(), alongside the tag table's state.
  private static void ResetDebugStreamNames() {
    _debugStreamNameIndexMap = [];
    _nextDebugStreamNameIndex = DebugStreamFirstNameIndex;
  }

  /// Returns the interned index for a Log name, assigning a new one if this is its first use.
  private static int EnsureDebugStreamNameIndex(string name) {
    _debugStreamNameIndexMap ??= [];
    if (_debugStreamNameIndexMap.TryGetValue(name, out var existing))
      return existing;
    if (_nextDebugStreamNameIndex == 0) _nextDebugStreamNameIndex = DebugStreamFirstNameIndex;
    if (_nextDebugStreamNameIndex > RuntimeEmitter.DsLogU16FieldMask)
      throw new InvalidOperationException(
        $"DebugStream name table overflow: a Log event carries a u16 name id, so a program may " +
        $"intern at most {RuntimeEmitter.DsLogU16FieldMask} distinct names (adding '{name}').");
    var index = _nextDebugStreamNameIndex++;
    _debugStreamNameIndexMap[name] = index;
    return index;
  }

  /// Publishes the interned names onto the module, indexed by the u16 a Log event carries.
  /// Must run after all lowering is complete — mirrors EmitTagTable.
  private static void EmitDebugStreamNameTable(IrModule<StandardOp> result) {
    if (_debugStreamNameIndexMap == null || _debugStreamNameIndexMap.Count == 0) return;
    var ordered = new string?[_nextDebugStreamNameIndex];
    foreach (var (name, index) in _debugStreamNameIndexMap)
      ordered[index] = name;
    result.DebugStreamNames = [.. ordered];
  }

  /// The global the DebugStream init writes the mapped ring's base pointer into; 0 = detached.
  private const string DebugStreamBaseGlobal = "__ds_base";

  /// Load `__ds_base` — the runtime gate every emitting op branches on.
  private static StdI64 EmitDebugStreamBase(IrBlock<StandardOp> block) {
    var loadBase = new StdGlobalLoadI64Op(DebugStreamBaseGlobal);
    block.AddOp(loadBase);
    return loadBase.Result;
  }

  private static void LowerDebugStreamEnabled(
      MaxonDebugStreamEnabledOp op,
      IrBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap) {
    if (!Compiler.DebugStream) {
      // The compile-time gate. `enabled()` has a result, so it cannot lower to nothing — it
      // lowers to the constant that makes every guarded body below it dead.
      var constFalse = new StdConstI1Op(false);
      block.AddOp(constFalse);
      valueMap[op.Result] = constFalse.Result;
      return;
    }

    var cmp = new StdCmpI64Op("ne", EmitDebugStreamBase(block), EmitConstI64(0, block));
    block.AddOp(cmp);
    valueMap[op.Result] = cmp.Result;
  }

  private static void LowerDebugStreamNameId(
      MaxonDebugStreamNameIdOp op,
      IrBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap) {
    // Interned unconditionally: the index must be stable whether or not the blob is emitted,
    // so that a `--debugstream` build and a plain one differ ONLY in the emitted events.
    valueMap[op.Result] = EmitConstI64(EnsureDebugStreamNameIndex(op.Name), block);
  }

  private static void LowerDebugStreamPhase(
      MaxonDebugStreamPhaseOp op,
      IrBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap) {
    if (!Compiler.DebugStream) return; // the compile-time gate: zero instructions

    var eventType = EmitConstI64(
      op.IsBegin ? RuntimeEmitter.DsEvLogPhaseBegin : RuntimeEmitter.DsEvLogPhaseEnd, block);
    block.AddOp(new StdCallRuntimeIfNonzeroOp(EmitDebugStreamBase(block), "__ds_emit_log_phase",
      [eventType, ResolveDebugStreamArg(op.NameId, valueMap), ResolveDebugStreamArg(op.UnitId, valueMap)]));
  }

  private static void LowerDebugStreamEvent(
      MaxonDebugStreamEventOp op,
      IrBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap) {
    if (!Compiler.DebugStream) return; // the compile-time gate: zero instructions

    block.AddOp(new StdCallRuntimeIfNonzeroOp(EmitDebugStreamBase(block), "__ds_emit_log_event", [
      ResolveDebugStreamArg(op.NameId, valueMap),
      ResolveDebugStreamArg(op.Category, valueMap),
      ResolveDebugStreamArg(op.Level, valueMap),
      ResolveDebugStreamArg(op.UnitId, valueMap),
      ResolveDebugStreamArg(op.Arg0, valueMap),
      ResolveDebugStreamArg(op.Arg1, valueMap),
    ]));
  }

  private static void LowerDebugStreamText(
      MaxonDebugStreamTextOp op,
      IrBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap,
      Dictionary<string, string> varTypes) {
    if (!Compiler.DebugStream) return; // the compile-time gate: zero instructions

    // The message is a __ManagedMemory: the runtime wants the raw (ptr, len) pair, which is
    // exactly what the stdout/stderr writers take out of it.
    var managedVarName = ResolveManagedVarName(op.Managed, valueMap);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, IrType.I64, varTypes);

    block.AddOp(new StdCallRuntimeIfNonzeroOp(EmitDebugStreamBase(block), "__ds_emit_log_text", [
      ResolveDebugStreamArg(op.Category, valueMap),
      ResolveDebugStreamArg(op.Level, valueMap),
      ResolveDebugStreamArg(op.UnitId, valueMap),
      buffer,
      length,
    ]));
  }

  /// Every `__DebugStream` numeric argument is an i64 by the time it reaches here — the builtin's
  /// registered signature admits nothing else, so a miss is a lowering bug, not a user error.
  private static StdValue ResolveDebugStreamArg(MaxonValue value, Dictionary<MaxonValue, StdValue> valueMap) {
    if (!valueMap.TryGetValue(value, out var mapped))
      throw new InvalidOperationException($"__DebugStream arg {value} not found in valueMap");
    return mapped;
  }
}
