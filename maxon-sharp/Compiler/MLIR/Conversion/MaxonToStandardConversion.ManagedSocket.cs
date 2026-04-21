using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  // __ManagedSocketError ordinals (see stdlib/Interfaces.maxon). Flag values are 1-indexed
  // (0 = success), so flag = ordinal + 1.
  // connectionClosed (5) and closed (6) are reserved for Phase B (runtime errno capture).
  private const int MsErrBufferOutOfBounds = 0;
  private const int MsErrResolveFailed = 1;
  private const int MsErrConnectFailed = 2;
  private const int MsErrSendFailed = 3;
  private const int MsErrRecvFailed = 4;

  /// <summary>
  /// Dispatches synthetic __managed_socket_* callees routed through LowerCallCore.
  ///
  /// Error mapping: each throwing method translates its runtime return value into
  /// a __ManagedSocketError ordinal. connectionClosed (5) and closed (6) require
  /// runtime-side detection and are deferred to Phase B.
  /// </summary>
  public static bool TryLowerManagedSocketBuiltin(
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
      case "__managed_socket_send":
        LowerManagedSocketSend(args, result, func, ref block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__managed_socket_recv":
        LowerManagedSocketRecv(args, result, block, valueMap, varTypes, errorFlagValue);
        return true;
      case "__managed_socket_close":
        LowerManagedSocketClose(args, block, valueMap, varTypes);
        return true;
      case "__managed_socket_tcp_connect":
        LowerManagedSocketTcpConnect(args, result, func, ref block, valueMap, varTypes, errorFlagValue, temps);
        return true;
    }
    return false;
  }

  private static void LowerManagedSocketSend(
    List<MaxonValue> args, MaxonValue? result,
    IrFunction<StandardOp> func, ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    // args: [handle, buf+offset (adjusted ptr), length, capacity]
    // The parser already computed buf+offset so the lowering receives a raw pointer and remaining capacity.
    var handle = (StdI64)valueMap[args[0]];
    var ptr = (StdI64)valueMap[args[1]];
    var length = (StdI64)valueMap[args[2]];
    var capacity = (StdI64)valueMap[args[3]];

    // Pre-check: length must not exceed capacity (the remaining capacity after offset was applied by parser).
    var overflow = new StdCmpU64Op("ugt", length, capacity);
    block.AddOp(overflow);
    EmitBoundsCheckErrorFlag(block, overflow.Result, MsErrBufferOutOfBounds + 1, valueMap, varTypes, errorFlagValue);

    var uid = IrContext.Current.NextId();
    var errLabel = $"__mssend_err_{uid}";
    var okLabel = $"__mssend_ok_{uid}";
    var mergeLabel = $"__mssend_merge_{uid}";
    block.AddOp(new StdCondBrOp(overflow.Result, errLabel, okLabel));

    var errBlock = func.Body.AddBlock(errLabel);
    var zeroBytesErr = new StdConstI64Op(0);
    errBlock.AddOp(zeroBytesErr);
    if (result != null) {
      EmitStore(errBlock, zeroBytesErr.Result, $"__mssend_bytes_{uid}", varTypes);
    }
    errBlock.AddOp(new StdBrOp(mergeLabel));

    block = func.Body.AddBlock(okLabel);
    var callRt = new StdCallRuntimeOp("maxon_net_send", [handle, ptr, length],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    var sent = (StdI64)callRt.Result!;
    EmitSentinelErrorFlag(block, sent, -1, MsErrSendFailed, valueMap, varTypes, errorFlagValue);
    if (result != null) {
      EmitStore(block, sent, $"__mssend_bytes_{uid}", varTypes);
    }
    block.AddOp(new StdBrOp(mergeLabel));

    block = func.Body.AddBlock(mergeLabel);
    if (errorFlagValue != null) {
      var mergedFlag = (StdI64)EmitLoad(block, "__error_flag", varTypes);
      valueMap[errorFlagValue] = mergedFlag;
    }
    if (result != null) {
      var mergedBytes = (StdI64)EmitLoad(block, $"__mssend_bytes_{uid}", varTypes);
      valueMap[result] = mergedBytes;
    }
  }

  private static void LowerManagedSocketRecv(
    List<MaxonValue> args, MaxonValue? result,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    // args: [handle, buffer_ptr, capacity]
    // Returns bytes received, 0 (peer closed gracefully — passed through), or -1 (error → recvFailed).
    // 0 is intentionally NOT treated as an error here; stdlib/TcpClient.maxon decides whether
    // "peer closed" is an error based on protocol context.
    var handle = (StdI64)valueMap[args[0]];
    var buf = (StdI64)valueMap[args[1]];
    var capacity = (StdI64)valueMap[args[2]];
    var callRt = new StdCallRuntimeOp("maxon_net_recv", [handle, buf, capacity],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    var received = (StdI64)callRt.Result!;
    EmitSentinelErrorFlag(block, received, -1, MsErrRecvFailed, valueMap, varTypes, errorFlagValue);
    if (result != null) valueMap[result] = received;
  }

  private static void LowerManagedSocketClose(
    List<MaxonValue> args, IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes) {
    // close() is idempotent and never throws. Pass the __ManagedSocket struct pointer so the
    // runtime reads _handle, zeros the field, and routes closesocket through the sync worker.
    // After an explicit close(), _handle is zero, so the destructor's zero-check skips the second close.
    var selfPtr = valueMap[args[0]];
    StdI64 structPtr;
    if (selfPtr is StdHeapPtr hp && hp.VarName != null)
      structPtr = (StdI64)EmitLoad(block, hp.VarName, varTypes);
    else
      structPtr = (StdI64)selfPtr;
    block.AddOp(new StdCallRuntimeOp("maxon_net_close", [structPtr], null));
  }

  private static void LowerManagedSocketTcpConnect(
    List<MaxonValue> args, MaxonValue? result,
    IrFunction<StandardOp> func, ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue,
    VarRegistry temps) {
    // args: [cstring_host, port]
    //
    // The runtime returns a managed __ManagedSocket ptr on success, -1 (DNS fail), or -2 (connect fail).
    // We distinguish the two failure codes and map them to different __ManagedSocketError ordinals.
    //
    // On the error path we still allocate a valid heap-resident __ManagedSocket{_handle:0} so that
    // scope-end decref calls the destructor on a real refcount header — matching the __ManagedFile open pattern.
    var cstr = (StdI64)valueMap[args[0]];
    var port = (StdI64)valueMap[args[1]];
    var callRt = new StdCallRuntimeOp("maxon_net_tcp_connect", [cstr, port],
      new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(callRt);
    var handlePtr = (StdI64)callRt.Result!;

    // Distinguish -1 (DNS) from -2 (connect) — emit a nested select chain.
    // The flag is stored to __error_flag and written into valueMap[errorFlagValue] before any branch.
    EmitMultiSentinelErrorFlag(block, handlePtr,
      [(-1L, MsErrResolveFailed), (-2L, MsErrConnectFailed)],
      valueMap, varTypes, errorFlagValue);

    // isError = handlePtr < 0 (covers both -1 and -2)
    var zeroForBranch = new StdConstI64Op(0);
    block.AddOp(zeroForBranch);
    var isError = new StdCmpI64Op("lt", handlePtr, zeroForBranch.Result);
    block.AddOp(isError);

    if (result != null) {
      var tempName = temps.CreateTemp("ms_connect", result.Id, "__ManagedSocket", OwnershipFlags.None);
      var uid = IrContext.Current.NextId();
      var errAllocLabel = $"__ms_conn_err_alloc_{uid}";
      var okLabel = $"__ms_conn_ok_{uid}";
      var doneLabel = $"__ms_conn_done_{uid}";
      block.AddOp(new StdCondBrOp(isError.Result, errAllocLabel, okLabel));

      // Error path: allocate a fresh __ManagedSocket with _handle=0 so scope-end decref
      // reaches a real refcount header and __destruct___ManagedSocket's zero-check is a no-op.
      var errBlock = func.Body.AddBlock(errAllocLabel);
      var emptyStruct = EmitAlloc(errBlock, 8, "__ManagedSocket", tag: "ms_err_slot", scopeName: _currentFuncName);
      var initZero = new StdConstI64Op(0);
      errBlock.AddOp(initZero);
      errBlock.AddOp(new StdStoreIndirectOp(initZero.Result, emptyStruct, 0, IrType.I64));
      EmitStore(errBlock, emptyStruct, tempName, varTypes);
      errBlock.AddOp(new StdBrOp(doneLabel));

      // Ok path: the runtime already allocated a __ManagedSocket struct; store it.
      var okBlock = func.Body.AddBlock(okLabel);
      EmitStore(okBlock, handlePtr, tempName, varTypes);
      okBlock.AddOp(new StdBrOp(doneLabel));

      block = func.Body.AddBlock(doneLabel);
      valueMap[result] = new StdHeapPtr(handlePtr.Id, "__ManagedSocket", tempName);
    }
  }

  /// <summary>
  /// Translates multiple distinct sentinel return values into distinct error-flag ordinals.
  /// Emits a nested select chain: flag = (rc == s0) ? f0 : (rc == s1) ? f1 : ... : 0.
  /// Stores the result to __error_flag and updates valueMap[errorFlagValue].
  /// </summary>
  internal static void EmitMultiSentinelErrorFlag(
    IrBlock<StandardOp> block,
    StdI64 callResult,
    (long Sentinel, int Ordinal)[] cases,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    MaxonValue? errorFlagValue) {
    // Build from innermost case outward: start with "0" (success) and wrap in selects.
    var zeroOp = new StdConstI64Op(0);
    block.AddOp(zeroOp);
    StdI64 flag = zeroOp.Result;
    for (int i = cases.Length - 1; i >= 0; i--) {
      var sentinelConst = new StdConstI64Op(cases[i].Sentinel);
      block.AddOp(sentinelConst);
      var isMatch = new StdCmpI64Op("eq", callResult, sentinelConst.Result);
      block.AddOp(isMatch);
      var flagConst = new StdConstI64Op(cases[i].Ordinal + 1);
      block.AddOp(flagConst);
      var selectOp = new StdSelectI64Op(isMatch.Result, flagConst.Result, flag);
      block.AddOp(selectOp);
      flag = selectOp.Result;
    }
    EmitStore(block, flag, "__error_flag", varTypes);
    if (errorFlagValue != null) {
      valueMap[errorFlagValue] = flag;
    }
  }
}
