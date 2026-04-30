using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  // __ManagedFileError ordinals (see stdlib/Interfaces.maxon). Flag values are 1-indexed
  // (0 = success), so flag = ordinal + 1. notFound (0) / accessDenied (1) are dispatched at
  // runtime by SelectIoErrorOrdinal (errno→variant); the remaining variants (invalidStatBuffer
  // / invalidStatIndex / closed) are stdlib-internal invariant panics, not user-catchable.
  private const int MfErrOpenFailed = 2;
  private const int MfErrReadFailed = 3;
  private const int MfErrWriteFailed = 4;
  private const int MfErrSizeFailed = 5;
  private const int MfErrDeleteFailed = 6;
  private const int MfErrStatFailed = 7;

  /// <summary>
  /// Dispatches synthetic __managed_file_* callees routed through LowerCallCore.
  /// Instance methods read _handle before calling; static methods pass a cstring
  /// directly from MaxonManagedToCStringOp; invariant panics (statField/statFree)
  /// emit bounds checks before calling.
  ///
  /// Error mapping: each throwing method translates its sentinel return (-1 / 0 / negative)
  /// into a 1-indexed __ManagedFileError flag. SelectIoErrorOrdinal then routes specific
  /// OS error codes (ENOENT / EACCES on POSIX; ERROR_FILE_NOT_FOUND / ERROR_ACCESS_DENIED
  /// on Win32) to notFound / accessDenied; everything else falls through to the method-
  /// specific catch-all variant.
  /// </summary>
  public static bool TryLowerManagedFileBuiltin(
    string callee,
    List<MaxonValue> args,
    MaxonValue? result,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    VarRegistry temps) {

    switch (callee) {
      case "__managed_file_size":
        LowerManagedFileSize(args, result, block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__managed_file_read":
        LowerManagedFileRead(args, result, func, ref block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__managed_file_write":
        LowerManagedFileWrite(args, result, block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__managed_file_close":
        LowerManagedFileClose(args, block, valueMap, varTypes);
        return true;
      case "__managed_file_open_read":
      case "__managed_file_open_write":
      case "__managed_file_open_write_executable":
        LowerManagedFileOpen(callee, args, result, func, ref block, valueMap, varTypes, errorFlagValue, temps);
        return true;
      case "__managed_file_exists":
        LowerManagedFileExists(args, result, block, valueMap);
        return true;
      case "__managed_file_delete":
        LowerManagedFileDelete(args, block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__managed_file_stat":
        LowerManagedFileStat(args, result, block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__managed_file_stat_field":
        LowerManagedFileStatField(args, result, block, valueMap);
        return true;
      case "__managed_file_stat_free":
        LowerManagedFileStatFree(args, block, valueMap);
        return true;
    }
    return false;
  }

  /// Helper: after a runtime call that returns a sentinel (e.g. -1) on failure,
  /// translate the sentinel into the error flag. Emits:
  ///   isError = (result == sentinel)
  ///   flag = isError ? errorOrdinal+1 : 0
  ///   __error_flag = flag; valueMap[errorFlagValue] = flag
  ///
  /// When ioErrnoOrdinalSelector is non-null, the truthy arm becomes a runtime
  /// ordinal computed from gt->io_error_code; the catch-all (errorOrdinal+1) is
  /// passed as the fallback to the selector. Used by throwing __ManagedFile /
  /// __ManagedDirectory builtins to route ENOENT/EACCES to notFound/accessDenied.
  internal static void EmitSentinelErrorFlag(
    IrBlock<StandardOp> block,
    StdI64 callResult,
    long sentinel,
    int errorOrdinal,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    Func<IrBlock<StandardOp>, int, StdI64>? ioErrnoOrdinalSelector = null) {
    var sentinelConst = new StdConstI64Op(sentinel);
    block.AddOp(sentinelConst);
    var isError = new StdCmpI64Op("eq", callResult, sentinelConst.Result);
    block.AddOp(isError);
    EmitBoundsCheckErrorFlag(block, isError.Result, errorOrdinal + 1, valueMap, varTypes, errorFlagValue, ioErrnoOrdinalSelector);
  }

  private static void LowerManagedFileSize(
    List<MaxonValue> args, MaxonValue? result,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    var handle = (StdI64)valueMap[args[0]];
    var callRt = new StdCallRuntimeOp("maxon_file_size", [handle],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    var size = (StdI64)callRt.Result!;
    EmitSentinelErrorFlag(block, size, -1, MfErrSizeFailed, valueMap, varTypes, errorFlagValue,
      SelectIoErrorOrdinal);
    if (result != null) valueMap[result] = size;
  }

  private static void LowerManagedFileRead(
    List<MaxonValue> args, MaxonValue? result,
    IrFunction<StandardOp> func, ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    // args: [handle, buffer, size, capacity]
    var handle = (StdI64)valueMap[args[0]];
    var buffer = (StdI64)valueMap[args[1]];
    var size = (StdI64)valueMap[args[2]];
    var capacity = (StdI64)valueMap[args[3]];

    // Pre-check: size must not exceed capacity. A silent clamp in the runtime would
    // hide a user contract violation; throw readFailed instead.
    var overflow = new StdCmpU64Op("ugt", size, capacity);
    block.AddOp(overflow);
    EmitBoundsCheckErrorFlag(block, overflow.Result, MfErrReadFailed + 1, valueMap, varTypes, errorFlagValue);

    // Branch around the read when the pre-check fails.
    var uid = IrContext.Current.NextId();
    var errLabel = $"__mfread_err_{uid}";
    var okLabel = $"__mfread_ok_{uid}";
    var mergeLabel = $"__mfread_merge_{uid}";
    block.AddOp(new StdCondBrOp(overflow.Result, errLabel, okLabel));
    var errBlock = func.Body.AddBlock(errLabel);
    var zeroBytes = new StdConstI64Op(0);
    errBlock.AddOp(zeroBytes);
    if (result != null) {
      EmitStore(errBlock, zeroBytes.Result, $"__mfread_bytes_{uid}", varTypes);
    }
    errBlock.AddOp(new StdBrOp(mergeLabel));

    block = func.Body.AddBlock(okLabel);
    var callRt = new StdCallRuntimeOp("maxon_file_read", [handle, buffer, size, capacity],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    var bytes = (StdI64)callRt.Result!;
    // A negative return from the runtime indicates an I/O error.
    var negOne = new StdConstI64Op(-1);
    block.AddOp(negOne);
    var isIoError = new StdCmpI64Op("eq", bytes, negOne.Result);
    block.AddOp(isIoError);
    EmitBoundsCheckErrorFlag(block, isIoError.Result, MfErrReadFailed + 1, valueMap, varTypes, errorFlagValue,
      SelectIoErrorOrdinal);
    if (result != null) {
      EmitStore(block, bytes, $"__mfread_bytes_{uid}", varTypes);
    }
    block.AddOp(new StdBrOp(mergeLabel));

    block = func.Body.AddBlock(mergeLabel);
    if (errorFlagValue != null) {
      var mergedFlag = (StdI64)EmitLoad(block, "__error_flag", varTypes);
      valueMap[errorFlagValue] = mergedFlag;
    }
    if (result != null) {
      var mergedBytes = (StdI64)EmitLoad(block, $"__mfread_bytes_{uid}", varTypes);
      valueMap[result] = mergedBytes;
    }
  }

  private static void LowerManagedFileWrite(
    List<MaxonValue> args, MaxonValue? result,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    // args: [handle, buffer, length]
    var handle = (StdI64)valueMap[args[0]];
    var buffer = (StdI64)valueMap[args[1]];
    var length = (StdI64)valueMap[args[2]];
    var callRt = new StdCallRuntimeOp("maxon_managed_file_write", [handle, buffer, length],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    var written = (StdI64)callRt.Result!;
    EmitSentinelErrorFlag(block, written, -1, MfErrWriteFailed, valueMap, varTypes, errorFlagValue,
      SelectIoErrorOrdinal);
    if (result != null) valueMap[result] = written;
  }

  private static void LowerManagedFileClose(
    List<MaxonValue> args, IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {
    // close() is idempotent: pass the __ManagedFile struct pointer so the runtime
    // reads _handle, submits CloseHandle, and zeros _handle.
    var selfPtr = valueMap[args[0]];
    StdI64 structPtr;
    if (selfPtr is StdHeapPtr hp && hp.VarName != null)
      structPtr = (StdI64)EmitLoad(block, hp.VarName, varTypes);
    else
      structPtr = (StdI64)selfPtr;
    block.AddOp(new StdCallRuntimeOp("maxon_file_close", [structPtr], null));
  }

  private static void LowerManagedFileOpen(
    string callee, List<MaxonValue> args, MaxonValue? result,
    IrFunction<StandardOp> func, ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    VarRegistry temps) {
    // args: [cstring path]
    //
    // The runtime returns a refcounted `__ManagedFile` struct on success, or -1 on
    // failure. The caller's error-handler scope-end decrefs the result variable, so
    // on the error path we must still produce a VALID heap pointer (never -1) that
    // the destructor can safely clean up. Wrap the runtime call: if it returns -1,
    // replace with a fresh empty __ManagedFile allocation (handle zeroed).
    var path = (StdI64)valueMap[args[0]];
    var runtimeName = callee switch {
      "__managed_file_open_read" => "maxon_managed_file_open_read",
      "__managed_file_open_write" => "maxon_managed_file_open_write",
      "__managed_file_open_write_executable" => "maxon_managed_file_open_write_executable",
      _ => throw new InvalidOperationException($"unknown open callee '{callee}'")
    };
    var callRt = new StdCallRuntimeOp(runtimeName, [path],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    var handlePtr = (StdI64)callRt.Result!;
    var sentinelConst = new StdConstI64Op(-1);
    block.AddOp(sentinelConst);
    var isError = new StdCmpI64Op("eq", handlePtr, sentinelConst.Result);
    block.AddOp(isError);
    EmitBoundsCheckErrorFlag(block, isError.Result, MfErrOpenFailed + 1, valueMap, varTypes, errorFlagValue,
      SelectIoErrorOrdinal);

    if (result != null) {
      var tempName = temps.CreateTemp("mf_open", result.Id, "__ManagedFile", OwnershipFlags.None);
      var uid = IrContext.Current.NextId();
      var errAllocLabel = $"__mf_open_err_alloc_{uid}";
      var okLabel = $"__mf_open_ok_{uid}";
      var doneLabel = $"__mf_open_done_{uid}";
      block.AddOp(new StdCondBrOp(isError.Result, errAllocLabel, okLabel));

      // Error path: allocate a fresh __ManagedFile with handle=0 so scope-end decref
      // reaches a real refcount header and __destruct___ManagedFile's close-of-zero
      // is a no-op. Same type/destructor as the runtime allocation, so the caller's
      // scope cleanup runs uniformly across both paths.
      var errBlock = func.Body.AddBlock(errAllocLabel);
      var emptyStruct = EmitAlloc(errBlock, 8, "__ManagedFile", tag: "mf_err_slot", scopeName: _currentFuncName);
      var initZero = new StdConstI64Op(0);
      errBlock.AddOp(initZero);
      errBlock.AddOp(new StdStoreIndirectOp(initZero.Result, emptyStruct, 0, IrType.I64));
      EmitStore(errBlock, emptyStruct, tempName, varTypes);
      errBlock.AddOp(new StdBrOp(doneLabel));

      // Ok path: the runtime already allocated a __ManagedFile struct; store it.
      var okBlock = func.Body.AddBlock(okLabel);
      EmitStore(okBlock, handlePtr, tempName, varTypes);
      okBlock.AddOp(new StdBrOp(doneLabel));

      block = func.Body.AddBlock(doneLabel);
      valueMap[result] = new StdHeapPtr(handlePtr.Id, "__ManagedFile", tempName);
    }
  }

  private static void LowerManagedFileExists(
    List<MaxonValue> args, MaxonValue? result,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {
    var path = (StdI64)valueMap[args[0]];
    var callRt = new StdCallRuntimeOp("maxon_file_exists", [path],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    if (result != null) valueMap[result] = callRt.Result!;
  }

  private static void LowerManagedFileDelete(
    List<MaxonValue> args,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    var path = (StdI64)valueMap[args[0]];
    var callRt = new StdCallRuntimeOp("maxon_file_delete", [path],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    // delete returns 0 on success, non-zero on any failure.
    var rc = (StdI64)callRt.Result!;
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    var isError = new StdCmpI64Op("ne", rc, zero.Result);
    block.AddOp(isError);
    EmitBoundsCheckErrorFlag(block, isError.Result, MfErrDeleteFailed + 1, valueMap, varTypes, errorFlagValue,
      SelectIoErrorOrdinal);
  }

  private static void LowerManagedFileStat(
    List<MaxonValue> args, MaxonValue? result,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    var path = (StdI64)valueMap[args[0]];
    var callRt = new StdCallRuntimeOp("maxon_file_stat", [path],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    var statBuf = (StdI64)callRt.Result!;
    EmitSentinelErrorFlag(block, statBuf, -1, MfErrStatFailed, valueMap, varTypes, errorFlagValue,
      SelectIoErrorOrdinal);
    if (result != null) valueMap[result] = statBuf;
  }

  // ManagedFile stat buffer has 6 fields (size, mtime, ctime, atime, isDir, isReadOnly).
  private const int ManagedFileStatFieldCount = 6;

  private static void LowerManagedFileStatField(
    List<MaxonValue> args, MaxonValue? result,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {
    // Invariant: non-null buffer, index < 6. Stdlib-only; user code never gets here.
    var buffer = (StdI64)valueMap[args[0]];
    var index = (StdI64)valueMap[args[1]];

    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    var isNull = new StdCmpI64Op("eq", buffer, zero.Result);
    block.AddOp(isNull);
    EmitPanicIf(block, isNull.Result, "__mm_panic_file_stat_null_buffer");

    var limit = new StdConstI64Op(ManagedFileStatFieldCount);
    block.AddOp(limit);
    EmitBoundsCheck(block, index, limit.Result, "__mm_panic_file_stat_index_oob");

    var callRt = new StdCallRuntimeOp("maxon_file_stat_field", [buffer, index],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    if (result != null) valueMap[result] = callRt.Result!;
  }

  private static void LowerManagedFileStatFree(
    List<MaxonValue> args, IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {
    var buffer = (StdI64)valueMap[args[0]];
    // mm_raw_free is not null-safe.
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    var isNull = new StdCmpI64Op("eq", buffer, zero.Result);
    block.AddOp(isNull);
    EmitPanicIf(block, isNull.Result, "__mm_panic_file_stat_null_buffer");
    EmitRawFree(block, buffer);
  }
}
