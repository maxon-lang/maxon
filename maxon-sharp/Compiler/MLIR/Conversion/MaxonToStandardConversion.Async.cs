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

    // Allocate arg buffer: (argCount + 1) * 8 bytes
    // Layout: [count | arg0 | arg1 | ...]
    var bufSizeConst = new StdConstI64Op((argCount + 1) * 8);
    block.AddOp(bufSizeConst);
    var argBuf = EmitRawAlloc(block, bufSizeConst.Result, label: "async.args", scopeName: _currentFuncName);

    // Store arg count at [buf + 0]
    var countConst = new StdConstI64Op(argCount);
    block.AddOp(countConst);
    var storeCount = new StdStoreIndirectOp(countConst.Result, argBuf, 0, IrType.I64);
    block.AddOp(storeCount);

    // Store each arg at [buf + 8 + i*8]
    for (int i = 0; i < argCount; i++) {
      var argVal = valueMap[asyncOp.Args[i]];
      // StdHeapPtr is a deferred reference (variable name, not a loaded value).
      // We must emit a load from the variable's stack home to get the actual
      // heap pointer before storing it into the arg buffer.
      if (argVal is StdHeapPtr hp && hp.VarName != null) {
        argVal = EmitLoad(block, hp.VarName, varTypes);
      }
      var storeArg = new StdStoreIndirectOp(argVal, argBuf, 8 + i * 8, IrType.I64);
      block.AddOp(storeArg);
    }

    // Get function pointer
    var funcRef = new StdFuncRefOp(asyncOp.Callee);
    block.AddOp(funcRef);

    // Call __gt_spawn(func_ptr, arg_count, arg_buf) -> promise (green thread ptr)
    var promiseResult = new StdI64(IrContext.Current.NextId());
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
    MaxonValueKind.Struct => new StdI64(IrContext.Current.NextId()),
    MaxonValueKind.Enum => new StdI64(IrContext.Current.NextId()),
    MaxonValueKind.Function => new StdI64(IrContext.Current.NextId()),
    _ => kind.CreateStdValue(),
  };

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
