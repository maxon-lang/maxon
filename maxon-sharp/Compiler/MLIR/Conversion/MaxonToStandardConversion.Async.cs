using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {

  /// <summary>
  /// Lowers MaxonAsyncCallOp to:
  /// 1. Allocate arg buffer via mm_raw_alloc((argCount + 1) * 8)
  /// 2. Store arg count at [buf+0], each arg at [buf + 8 + i*8]
  /// 3. Get function pointer via StdFuncRefOp
  /// 4. Call __gt_spawn(func_ptr, arg_count, arg_buf) -> green thread ptr (promise)
  /// </summary>
  private static void LowerAsyncCall(
    MaxonAsyncCallOp asyncOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {

    int argCount = asyncOp.Args.Count;

    // Allocate arg buffer: (argCount + 2) * 8 bytes
    // Layout: [count | managed_mask | arg0 | arg1 | ...]
    //
    // managed_mask is a bitmap (LSB = arg0) of which args are refcounted heap
    // pointers (Struct / Enum / Function kinds). For each set bit we incref
    // here at the spawn site and the trampoline decrefs the matching arg
    // after the spawned function returns. Without this, a managed arg could
    // be freed by the caller's scope-end decref before the GT actually runs.
    var bufSizeConst = new StdConstI64Op((argCount + 2) * 8);
    block.AddOp(bufSizeConst);
    var argBuf = EmitRawAlloc(block, bufSizeConst.Result, label: "async.args", scopeName: _currentFuncName);

    // Store arg count at [buf + 0]
    var countConst = new StdConstI64Op(argCount);
    block.AddOp(countConst);
    var storeCount = new StdStoreIndirectOp(countConst.Result, argBuf, 0, IrType.I64);
    block.AddOp(storeCount);

    // Compute the managed-mask first so it can be stored before the arg loop
    // emits any side-effecting ops. The trampoline reads [buf+8] for the mask;
    // keeping the mask store close to the count store (and ahead of the arg
    // stores) preserves the original buffer-initialisation order.
    //
    // Invariant: every set bit in managedMask corresponds to an arg that the
    // arg loop will (a) be a heap-pointer-shaped StdI64 and (b) incref. The
    // arg loop below asserts (a) so any asymmetry between mask-driven trampoline
    // decrefs and spawn-site increfs is caught at compile time, not by a
    // refcount underflow at runtime.
    long managedMask = 0;
    for (int i = 0; i < argCount; i++) {
      if (IsManagedAsyncArg(asyncOp.Args[i]))
        managedMask |= 1L << i;
    }
    var maskConst = new StdConstI64Op(managedMask);
    block.AddOp(maskConst);
    var storeMask = new StdStoreIndirectOp(maskConst.Result, argBuf, 8, IrType.I64);
    block.AddOp(storeMask);

    // Store each arg at [buf + 16 + i*8]. For managed (refcounted) args we also
    // incref so the value survives across any scope-end decrefs the caller
    // emits between the `async X(...)` site and the moment the spawned GT
    // actually runs. The GT's body owns its own ref and decrefs at scope-end
    // (cleanup unchanged); the trampoline drops the spawn-time incref after
    // the function returns by walking the managed_mask.
    for (int i = 0; i < argCount; i++) {
      var maxonArg = asyncOp.Args[i];
      var argVal = valueMap[maxonArg];
      // StdHeapPtr is a deferred reference (variable name, not a loaded value).
      // We must emit a load from the variable's stack home to get the actual
      // heap pointer before storing it into the arg buffer.
      if (argVal is StdHeapPtr hp && hp.VarName != null) {
        argVal = EmitLoad(block, hp.VarName, varTypes);
      }
      var storeArg = new StdStoreIndirectOp(argVal, argBuf, 16 + i * 8, IrType.I64);
      block.AddOp(storeArg);

      if (IsManagedAsyncArg(maxonArg)) {
        // The mask bit for this arg was set above; the trampoline will emit a
        // paired decref. If the lowered value isn't a StdI64 heap pointer the
        // pairing is broken — throw rather than silently skip the incref and
        // leave the trampoline to over-decref a buffer slot of unknown shape.
        if (argVal is not StdI64 heapPtr)
          throw new InvalidOperationException(
            $"Async managed arg {i} ({maxonArg.GetType().Name}) lowered to {argVal.GetType().Name}, expected StdI64. " +
            "Mask/incref asymmetry would cause trampoline-side over-decref.");
        EmitIncrefValueIfNonnull(block, heapPtr, scopeName: $"async.arg.{i}");
      }
    }

    // Get function pointer
    var funcRef = new StdFuncRefOp(asyncOp.Callee);
    block.AddOp(funcRef);

    // Call __gt_spawn(func_ptr, arg_count, arg_buf) -> promise (green thread ptr)
    var promiseResult = new StdI64(IrContext.Current.NextStdId());
    block.AddOp(new StdCallRuntimeOp("__gt_spawn", [funcRef.Result, countConst.Result, argBuf], promiseResult));

    valueMap[asyncOp.Result] = promiseResult;
  }

  /// <summary>
  /// Lowers MaxonAwaitOp to:
  /// 1. Call __gt_await(promise) -> result in RAX
  /// 2. Map result value per ResultKind
  /// </summary>
  private static void LowerAwait(
    MaxonAwaitOp awaitOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {

    var promiseVal = valueMap[awaitOp.Promise];

    if (awaitOp.ResultKind == null) {
      // Void await: just call __gt_await, discard result
      block.AddOp(new StdCallRuntimeOp("__gt_await", [promiseVal]));
    } else {
      // The await returns a raw i64 value. For struct types, this is a heap pointer
      // but must be StdI64 (not StdPtr) to match the standard dialect's struct convention.
      var resultVal = CreateAwaitResultValue(awaitOp.ResultKind.Value);
      block.AddOp(new StdCallRuntimeOp("__gt_await", [promiseVal], resultVal));
      if (awaitOp.Result != null)
        valueMap[awaitOp.Result] = resultVal;
    }
  }

  /// <summary>
  /// Creates an appropriate StdValue for await result types.
  /// For struct/enum/function types, returns StdI64 (heap pointers are i64 in the standard dialect).
  /// For primitive types, returns the standard value type.
  /// </summary>
  private static StdValue CreateAwaitResultValue(MaxonValueKind kind) => kind switch {
    MaxonValueKind.Struct => new StdI64(IrContext.Current.NextStdId()),
    MaxonValueKind.Enum => new StdI64(IrContext.Current.NextStdId()),
    MaxonValueKind.Function => new StdI64(IrContext.Current.NextStdId()),
    _ => kind.CreateStdValue(),
  };

  /// <summary>
  /// True if a MaxonValue is a refcounted heap pointer. Async args of these
  /// kinds need an extra mm_incref at spawn time so the producer scope's
  /// decref doesn't free the object before the consumer GT runs.
  /// MaxonStruct, MaxonEnum, MaxonFunctionPtr are all heap-allocated.
  /// </summary>
  private static bool IsManagedAsyncArg(MaxonValue value) =>
    value is MaxonStruct or MaxonEnum or MaxonFunctionPtr;

  /// <summary>
  /// Lowers MaxonCancelPromiseOp to a __gt_cancel(gt_ptr) call.
  /// The promise value IS the green thread pointer.
  /// </summary>
  private static void LowerCancelPromise(
    MaxonCancelPromiseOp cancelOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {

    var promiseVal = valueMap[cancelOp.Promise];
    block.AddOp(new StdCallRuntimeOp("__gt_cancel", [promiseVal]));
  }

  /// <summary>
  /// Lowers MaxonTryAwaitOp to:
  /// 1. Call __gt_try_await(promise) -> result in RAX, threw flag in RDX
  /// 2. Map result and error flag per ResultKind
  /// </summary>
  private static void LowerTryAwait(
    MaxonTryAwaitOp tryAwaitOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {

    var promiseVal = valueMap[tryAwaitOp.Promise];

    if (tryAwaitOp.ResultKind == null) {
      // Void-returning throwing async: still need the error flag
      var tryCall = new StdTryCallRuntimeOp("__gt_try_await", [promiseVal]);
      block.AddOp(tryCall);
      valueMap[tryAwaitOp.ErrorFlag] = tryCall.ErrorFlag;
    } else {
      // The await returns a raw i64 value. For struct types, this is a heap pointer
      // but must be StdI64 (not StdPtr) to match the standard dialect's struct convention.
      var resultVal = CreateAwaitResultValue(tryAwaitOp.ResultKind.Value);
      var tryCall = new StdTryCallRuntimeOp("__gt_try_await", [promiseVal], resultVal);
      block.AddOp(tryCall);
      if (tryAwaitOp.Result != null)
        valueMap[tryAwaitOp.Result] = resultVal;
      valueMap[tryAwaitOp.ErrorFlag] = tryCall.ErrorFlag;
    }
  }
}
