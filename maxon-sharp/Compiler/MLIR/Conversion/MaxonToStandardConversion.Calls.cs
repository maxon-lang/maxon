using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  private static readonly HashSet<string> ThrowingManagedMemBuiltins = [
    "__managed_mem_get", "__managed_mem_set", "__managed_mem_remove",
    "__managed_mem_byte_at", "__managed_mem_set_byte",
    "__managed_mem_grow", "__managed_mem_set_length", "__managed_mem_fill",
    "__managed_mem_shift_right", "__managed_mem_shift_left",
    "__managed_mem_swap",
    "__managed_mem_create", "__managed_mem_slice"
  ];

  private static readonly HashSet<string> ThrowingManagedSocketBuiltins = [
    "__managed_socket_send", "__managed_socket_recv", "__managed_socket_tcp_connect"
  ];

  private static readonly HashSet<string> ThrowingManagedFileBuiltins = [
    "__managed_file_size", "__managed_file_read", "__managed_file_write",
    "__managed_file_open_read", "__managed_file_open_write",
    "__managed_file_open_write_executable",
    "__managed_file_delete", "__managed_file_rename", "__managed_file_stat"
  ];

  private static readonly HashSet<string> ThrowingManagedDirectoryBuiltins = [
    "__managed_directory_open_search", "__managed_directory_create",
    "__managed_directory_current_path", "__managed_directory_next"
  ];

  private static bool IsThrowingManagedMemBuiltin(string callee) =>
    ThrowingManagedMemBuiltins.Contains(callee);

  private static bool IsThrowingManagedSocketBuiltin(string callee) =>
    ThrowingManagedSocketBuiltins.Contains(callee);

  private static bool IsThrowingManagedFileBuiltin(string callee) =>
    ThrowingManagedFileBuiltins.Contains(callee);

  private static bool IsThrowingManagedDirectoryBuiltin(string callee) =>
    ThrowingManagedDirectoryBuiltins.Contains(callee);

  private static IrType ResolveEnumBackingIrType(IrEnumType enumType) {
    if (enumType.BackingType == IrType.F64) return IrType.F64;
    if (enumType.BackingType is IrStringBackingType or IrCharBackingType) return IrType.I64;
    if (enumType.BackingType is IrStructBackingType) return IrType.I64;
    // Function-backed enums store an ordinal at runtime (the function-pointer
    // table is reconstructed by MaxonEnumFunctionRawValueOp's select chain).
    if (enumType.BackingType is IrFunctionBackingType) return IrType.I64;
    if (enumType.BackingType == IrType.I64 || enumType.BackingType == null) return IrType.I64;
    throw new InvalidOperationException($"Unsupported enum backing type: {enumType.BackingType}");
  }

  /// <summary>
  /// Handles method calls on primitive types as intrinsics (e.g. i64.hash, i8.hash).
  /// Returns true if the call was handled, false to fall through to normal LowerCall.
  /// </summary>
  private static bool TryLowerPrimitiveMethod(
    MaxonCallOp callOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {
    switch (callOp.Callee) {
      case "i64.hash" or "i8.hash" or "i1.hash": {
        // Integer/byte/bool hash is the identity function
        var selfVal = valueMap[callOp.Args[0]];
        if (callOp.Result != null) valueMap[callOp.Result] = selfVal;
        return true;
      }
      case "f64.hash": {
        // Float hash: truncate to integer
        var selfVal = valueMap[callOp.Args[0]];
        var truncOp = new StdFpToSiOp((StdF64)selfVal);
        block.AddOp(truncOp);
        if (callOp.Result != null) valueMap[callOp.Result] = truncOp.Result;
        return true;
      }
    }
    return false;
  }

  private static void LowerCall(
    MaxonCallOp callOp,
    Dictionary<string, IrFunction<MaxonOp>> funcLookup,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps,
    Dictionary<int, string>? fnEnvVarNames = null,
    Dictionary<int, StdI64>? fnEnvDirectValues = null) {
    LowerCallCore(callOp.Callee, callOp.Args, callOp.Result, callOp.ResultKind,
      isTryCall: false, funcLookup, func, ref block, valueMap, varTypes,
      typeDefs, temps, sourceCallOp: callOp, fnEnvVarNames: fnEnvVarNames,
      fnEnvDirectValues: fnEnvDirectValues,
      argMutabilities: callOp.ArgMutabilities, argVarNames: callOp.ArgVarNames,
      callLine: callOp.CallLine, callColumn: callOp.CallColumn);
  }

  /// <summary>
  /// Shared implementation for lowering both MaxonCallOp and MaxonTryCallOp.
  /// For try calls, pass errorFlagValue to map the error flag into valueMap.
  /// sourceCallOp carries the original call op so builtins can inspect subtype metadata (e.g. MaxonManagedMemCreateTryCallOp).
  /// </summary>
  private static void LowerCallCore(
    string callee,
    List<MaxonValue> args,
    MaxonValue? result,
    MaxonValueKind? resultKind,
    bool isTryCall,
    Dictionary<string, IrFunction<MaxonOp>> funcLookup,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps,
    MaxonCallOp? sourceCallOp = null,
    MaxonValue? errorFlagValue = null,
    Dictionary<int, string>? fnEnvVarNames = null,
    Dictionary<int, StdI64>? fnEnvDirectValues = null,
    List<bool>? argMutabilities = null,
    List<string?>? argVarNames = null,
    int? callLine = null,
    int? callColumn = null) {

    // Intercept synthetic managed list navigation calls before resolving the callee
    // (these are not real functions in the module)
    if (TryLowerManagedListNavigation(callee, args, result, isTryCall, block, valueMap,
        varTypes, errorFlagValue, temps))
      return;

    // Intercept synthetic cursor calls before resolving the callee
    if (TryLowerCursorCall(callee, args, result, resultKind, block, valueMap,
        varTypes, typeDefs, errorFlagValue, temps))
      return;

    // Intercept synthetic __ManagedMemory builtin calls (throwing variants of get/set/slice/etc.)
    if (TryLowerManagedMemBuiltin(callee, args, result, func, ref block,
        valueMap, varTypes, typeDefs, errorFlagValue, temps, sourceCallOp))
      return;

    // Intercept the desugared checked-division builtins (__checked_div / __checked_mod) — a
    // possibly-zero `a / b` / `a mod b` that throws __DivisionByZeroError on a zero divisor.
    if (TryLowerCheckedDivMod(callee, args, result, ref block, func, valueMap, varTypes,
        errorFlagValue, sourceCallOp))
      return;

    // Intercept synthetic __ManagedList reinsert_* builtins (non-throwing moves).
    if (TryLowerManagedListBuiltin(callee, args, block, valueMap, varTypes))
      return;

    // Intercept synthetic __ManagedSocket builtins (send/recv/tcpConnect throw;
    // close is non-throwing but routed here for the struct-pointer lowering).
    if (TryLowerManagedSocketBuiltin(callee, args, result, func, ref block,
        valueMap, varTypes, errorFlagValue, temps))
      return;

    // Intercept synthetic __ManagedFile builtins (open/read/write/stat/delete throw;
    // exists/statField/statFree/close are non-throwing but still routed here for
    // lowering-side invariant checks and field extraction).
    if (TryLowerManagedFileBuiltin(callee, args, result, func, ref block,
        valueMap, varTypes, errorFlagValue, temps))
      return;

    // Intercept synthetic __ManagedDirectory builtins (openSearch/create/currentPath/next throw;
    // exists/filename/close are non-throwing but still routed here for lowering-side
    // invariant checks).
    if (TryLowerManagedDirectoryBuiltin(callee, args, result, func, ref block,
        valueMap, varTypes, errorFlagValue, temps))
      return;

    // Throwing builtins must always be called via try (the parser enforces this via
    // ValidateThrowingBuiltinCallContext). A non-try call reaching here is a compiler bug.
    if (!isTryCall && (IsThrowingManagedMemBuiltin(callee) || IsThrowingManagedSocketBuiltin(callee)
        || IsThrowingManagedFileBuiltin(callee) || IsThrowingManagedDirectoryBuiltin(callee)))
      throw new InvalidOperationException($"throwing builtin '{callee}' called without try — parser should have rewritten to MaxonTryCallOp");

    var calleeFunc = ResolveCallee(callee, funcLookup);
    var resolvedCallee = calleeFunc.Name;
    var resultTypeName = (result as MaxonStruct)?.TypeName;
    var calleeRetStructType = ResolveStructReturnType(calleeFunc.ReturnType, typeDefs, resultTypeName: resultTypeName);

    var newArgs = new List<StdValue>();

    // Mutability enforcement: immutable args cannot be passed to functions that mutate
    // the corresponding parameter (E3063). Uses MutatedParams on the callee function.
    if (calleeFunc.MutatedParams != null && argMutabilities != null) {
      for (int i = 0; i < calleeFunc.ParamNames.Count && i < argMutabilities.Count; i++) {
        // Skip self-derived arguments: struct self is always passed by reference,
        // so fields of self are inherently mutable even though self is declared as let.
        var argName = argVarNames != null && i < argVarNames.Count ? argVarNames[i] : null;
        if (argName == "self") continue;
        if (calleeFunc.MutatedParams.Contains(calleeFunc.ParamNames[i]) && !argMutabilities[i]) {
          var argDesc = argVarNames != null && i < argVarNames.Count && argVarNames[i] != null
            ? $"'{argVarNames[i]}'" : "immutable 'let' variable";
          var inFunc = _currentFuncName != null ? $" (in {_currentFuncName})" : "";
          var errorLine = callLine ?? _currentFuncSourceLine;
          var errorColumn = callLine != null ? callColumn : null;
          throw new CompileError(
            ErrorCode.SemanticImmutableRefToMutatingParam,
            $"cannot pass {argDesc} to function that mutates parameter '{calleeFunc.ParamNames[i]}'{inFunc}",
            errorLine, errorColumn) { FilePath = _currentFuncSourceFile };
        }
      }
    }

    FlattenCallArgs(args, calleeFunc, block, valueMap, varTypes, newArgs, callee,
      fnEnvVarNames: fnEnvVarNames, fnEnvDirectValues: fnEnvDirectValues, argVarNames: argVarNames);

    // Check if callee returns an associated-value enum (passed as heap pointer)
    bool calleeRetAssocEnum = calleeFunc.ReturnType is IrEnumType cret && cret.HasAssociatedValues;

    // Two-register value tuple: the pair arrives in registers, so there is no pointer to
    // receive. This reads ValueTupleAbiPass's module-wide verdict — the very set the callee's
    // own return was lowered against — which is what makes the two ends agree.
    var calleeValueTupleType = _valueTupleReturnFunctions?.Contains(calleeFunc.Name) == true
      ? IrStructType.AsTwoRegisterValueTuple(calleeFunc.ReturnType, typeDefs)
      : null;

    // Emit call or try_call
    StdValue? callResult = calleeRetStructType != null || calleeRetAssocEnum
      ? new StdI64(IrContext.Current.NextStdId())
      : ResolveCallResultType(resultKind, calleeFunc.ReturnType);
    StdValue? callResultHigh = calleeValueTupleType != null
      ? new StdI64(IrContext.Current.NextStdId())
      : null;
    if (isTryCall) {
      var tryCall = new StdTryCallOp(resolvedCallee, newArgs, callResult);
      block.AddOp(tryCall);
      if (errorFlagValue != null) {
        valueMap[errorFlagValue] = tryCall.ErrorFlag;
        EmitStore(block, tryCall.ErrorFlag, "__error_flag", varTypes);
      }
    } else {
      block.AddOp(new StdCallOp(resolvedCallee, newArgs, callResult, callResultHigh));
    }

    if (calleeValueTupleType != null && result != null && callResult != null) {
      MaterializeValueTupleResult(block, result, callResult, callResultHigh!, calleeValueTupleType,
        valueMap, varTypes, temps, func.Name);
      return;
    }

    // Map results
    if (result != null) {
      if (calleeRetStructType != null && callResult != null) {
        // ReturnsSelf: the returned pointer is a borrowed reference (not a new allocation).
        // Use a non-callret prefix so the caller increfs it like any other alias assignment.
        var retVarName = calleeFunc.ReturnsSelf
            ? temps.CreateTemp("selfret", result.Id, calleeRetStructType.Name, OwnershipFlags.SelfReturn)
            : temps.CreateTemp("callret", result.Id, calleeRetStructType.Name, OwnershipFlags.Orphan | OwnershipFlags.CallReturn);
        EmitStore(block, callResult, retVarName, varTypes);
        // If ReturnsSelf and self arg was a stack pointer, propagate stack-ness
        bool selfIsStack = calleeFunc.ReturnsSelf && args.Count > 0
            && valueMap.TryGetValue(args[0], out var selfSv) && selfSv is StdStackPtr;
        valueMap[result] = selfIsStack
            ? new StdStackPtr(callResult!.Id, calleeRetStructType.Name, retVarName)
            : new StdHeapPtr(callResult!.Id, calleeRetStructType.Name, retVarName);
      } else if (calleeRetAssocEnum && callResult != null) {
        // Associated-value enum return: the value IS a heap pointer, so store it as-is.
        //
        // A try_call returns null on the error path, and null is stored here unchanged —
        // ABSENT is exactly what null means. This used to substitute a heap-allocated
        // "EnumDummy" whenever the pointer was null, selected between the two, and decreffed
        // whichever lost, on the belief that scope cleanup needed a real rc=1 allocation to
        // decref. It does not: scope-end cleanup emits a null-GUARDED decref and every managed
        // slot is zeroed on function entry, so a null slot is already the well-defined
        // "nothing to release" case. The dummy was allocated, increffed, decreffed and freed
        // on every SUCCESSFUL call without ever being read — the single largest source of
        // allocations in the compiler (~20% of them).
        //
        // What makes the bare null safe is that no path reads this value without first
        // testing the error flag: every `try` form branches on it immediately (see
        // EmitErrorFlagCheck, and RouteEmittedTryCallToTryBlock for the try-block form),
        // so the loads and increfs that would fault on null are all dominated by the
        // success edge. The error edge only ever null-guard-decrefs the slot.
        var retEnumType = (IrEnumType)calleeFunc.ReturnType!;
        var retVarName = temps.CreateTemp("callret", result.Id, retEnumType.Name, OwnershipFlags.Orphan | OwnershipFlags.CallReturn);
        EmitStore(block, callResult, retVarName, varTypes);
        valueMap[result] = new StdHeapPtr(callResult!.Id, retEnumType.Name, retVarName);
      } else if (callResult != null) {
        // Widen 32-bit call results to 64-bit — StdU32 extends StdI32 so this catches both;
        // unsigned values get zero-extended, signed values get sign-extended
        if (callResult is StdI32) {
          bool isUnsigned = callResult is StdU32;
          callResult = EnsureI64(callResult, block, signExtend: !isUnsigned);
        }
        valueMap[result] = callResult;
      }
    }

    // No post-call temp releases needed — scope-based cleanup handles all allocations
  }

  /// <summary>
  /// Give the two halves of a value-tuple call result somewhere to live.
  ///
  /// When the escape analysis cleared the result (StackPromotionAnalysisPass marked it
  /// stack-eligible), the halves go into BulkZero stack slots: no allocation, no refcounting,
  /// and field access reads the slots directly. That is the whole point of the ABI.
  ///
  /// Otherwise the result escapes — it is aliased, stored, captured, put in an array — and
  /// callers downstream need a real record with reference identity, so the halves are written
  /// into a heap record here. That costs exactly the allocation the old ABI made in the
  /// CALLEE, so an escaping tuple is no worse than before, just no better.
  /// </summary>
  private static void MaterializeValueTupleResult(
    IrBlock<StandardOp> block,
    MaxonValue result,
    StdValue low,
    StdValue high,
    IrStructType tupleType,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps,
    string funcName) {

    if (_stackEligibleStructs != null && _stackEligibleStructs.Contains(result.Id)) {
      var stackVarName = temps.CreateTemp("stack", result.Id, tupleType.Name, OwnershipFlags.None);
      var stackTag = $"__stk_{stackVarName}";

      // Slots are immediately overwritten by both halves, so skip the zero-init.
      // QWORDS, not fields (see StackSlotCount) — equal for a 2-scalar tuple, but the slot names
      // below are indexed by offset, so the two must be derived from the same quantity.
      block.AddOp(new StdBulkZeroOp(stackTag, StackSlotCount(tupleType), zeroInit: false));
      EmitStore(block, low, StackSlotName(stackTag, tupleType, tupleType.Fields[0].Offset), varTypes);
      EmitStore(block, high, StackSlotName(stackTag, tupleType, tupleType.Fields[1].Offset), varTypes);

      valueMap[result] = new StdStackPtr(result.Id, tupleType.Name, stackVarName);
      _stackAllocatedVars?.Add(stackVarName);
      _stackVarTags?.Add(stackVarName, stackTag);
      return;
    }

    var heapVarName = temps.CreateTemp("tupleret", result.Id, tupleType.Name,
      OwnershipFlags.Orphan | OwnershipFlags.CallReturn);
    var recordPtr = EmitAlloc(block, tupleType.SizeInBytes, tupleType.Name, scopeName: funcName);
    EmitStore(block, recordPtr, heapVarName, varTypes);
    EmitStructFieldStore(block, low, heapVarName, tupleType.Fields[0].Offset,
      IrType.Resolve(tupleType.Fields[0].Type), varTypes);
    EmitStructFieldStore(block, high, heapVarName, tupleType.Fields[1].Offset,
      IrType.Resolve(tupleType.Fields[1].Type), varTypes);

    // Establishes the scope reference that scope_end's mm_decref releases, exactly as an
    // orphan struct literal does — this record is built here, so this frame owns it.
    EmitIncrefValue(block, recordPtr, scopeName: funcName);
    _varNameToStructType?.TryAdd(heapVarName, tupleType.Name);
    valueMap[result] = new StdHeapPtr(recordPtr.Id, tupleType.Name, heapVarName);
  }

  private static void LowerReturn(
    MaxonReturnOp retOp,
    IrStructType? retStructType,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    string funcName,
    VarRegistry temps,
    bool functionReturnsSelf = false,
    bool usesValueTupleReturn = false) {

    // Error propagation: forward the error flag to the caller
    if (retOp.IsErrorPropagation) {
      var mappedErrFlag = valueMap[retOp.Value!];
      block.AddOp(new StdErrorReturnOp(mappedErrFlag));
      return;
    }

    // Associated-value enum return: the enum is already a heap pointer.
    // Incref before return so the caller receives an owned reference (rc>=1),
    // matching the struct return convention.
    if (retOp.Value != null
        && valueMap.TryGetValue(retOp.Value, out var retSv) && retSv is StdHeapPtr retHp
        && typeDefs.TryGetValue(retHp.TypeName, out var enumRetTypeDef)
        && enumRetTypeDef is IrEnumType enumRetType && enumRetType.HasAssociatedValues) {
      bool isEnumParam = _structParamNames != null && _structParamNames.Contains(retHp.VarName!)
            && retHp.VarName != "self";
      bool isEnumManagedTemp = temps.IsTempManaged(retHp.VarName!)
            && !temps.TempHasFlag(retHp.VarName!, OwnershipFlags.SelfReturn)
            && !temps.TempHasFlag(retHp.VarName!, OwnershipFlags.Orphan);
      if (isEnumParam || isEnumManagedTemp) {
        EmitIncref(block, retHp.VarName!, varTypes, scopeName: funcName);
        EmitTransfer(block, retHp.VarName!, varTypes, funcName);
      }
      var retHeapPtr = EmitLoad(block, retHp.VarName!, varTypes);
      block.AddOp(new StdReturnOp(retHeapPtr));
      return;
    }

    // Two-register value tuple: copy both halves into the return registers.
    //
    // No incref and no transfer, unlike every other struct return: the caller receives VALUES,
    // not a reference, so there is no new owner to account for.
    //
    // A heap record's halves were already read at scope_end, before the cleanup that may have
    // released it; take them from there. A stack-promoted record — the common case — has no
    // refcount and outlives cleanup untouched, so its slots are read here.
    if (usesValueTupleReturn && retStructType != null && retOp.Value != null) {
      var (low, high) = _valueTupleReturnStash != null
          && _valueTupleReturnStash.TryGetValue(retOp.Value.Id, out var stashed)
        ? stashed
        : (EmitValueTupleHalfLoad(block, valueMap[retOp.Value], retStructType, 0, varTypes),
           EmitValueTupleHalfLoad(block, valueMap[retOp.Value], retStructType, 1, varTypes));

      block.AddOp(new StdReturnOp(low, high));
      return;
    }

    if (retStructType != null && retOp.Value != null) {
      // Struct return: return the heap pointer as i64
      StdValue retHeapPtr;
      if (valueMap.TryGetValue(retOp.Value, out var retStructSv) && retStructSv is StdHeapPtr retStructHp) {
        // Incref before return so the caller receives an owned reference.
        // - Temps: scope-end decrefs them, so incref balances that.
        // - Struct params: scope-end skips them (borrowed), so incref creates
        //   a new owned reference for the caller.
        // Skip SelfReturn (alias, not owned).
        // Skip Orphan temps: their scope-end cleanup is already skipped for returned values,
        // so the single reference from creation transfers directly to the caller.
        bool isStructParam = _structParamNames != null && _structParamNames.Contains(retStructHp.VarName!)
              && (retStructHp.VarName != "self" || !functionReturnsSelf);
        bool isManagedTemp = temps.IsTempManaged(retStructHp.VarName!)
              && !temps.TempHasFlag(retStructHp.VarName!, OwnershipFlags.SelfReturn)
              && !temps.TempHasFlag(retStructHp.VarName!, OwnershipFlags.Orphan);
        if (isStructParam || isManagedTemp) {
          EmitIncref(block, retStructHp.VarName!, varTypes, scopeName: funcName);
          EmitTransfer(block, retStructHp.VarName!, varTypes, funcName);
        }
        retHeapPtr = EmitLoad(block, retStructHp.VarName!, varTypes);
      } else {
        retHeapPtr = valueMap[retOp.Value];
      }
      block.AddOp(new StdReturnOp(retHeapPtr));
    } else if (retOp.Value != null && valueMap.TryGetValue(retOp.Value, out var fbSv) && fbSv is StdHeapPtr fbHp) {
      // Value is a heap pointer (registered by chain/managed-memory ops) but the
      // function's return type is unresolved (e.g., type parameter "Element" in a
      // generic template function). Return the heap pointer as i64.
      var retHeapPtr = EmitLoad(block, fbHp.VarName!, varTypes);
      block.AddOp(new StdReturnOp(retHeapPtr));
    } else {
      StdValue? newRetVal = retOp.Value != null ? valueMap[retOp.Value] : null;
      block.AddOp(new StdReturnOp(newRetVal));
    }
  }


  private static void LowerThrow(
    MaxonThrowOp throwOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps) {
    // Scope cleanup is handled by MaxonScopeEndOp lowering before throw ops.

    // Check if this is an associated-value error enum
    if (valueMap.TryGetValue(throwOp.ErrorValue, out var throwSv) && throwSv is StdHeapPtr throwHp
        && typeDefs.TryGetValue(throwOp.ErrorTypeName, out var errorTypeDef)
        && errorTypeDef is IrEnumType errorEnumType && errorEnumType.HasAssociatedValues) {
      // Error return expects a heap pointer in RDX — already a heap pointer.
      // Convention: throws transfer an owned reference (rc>=1) to the caller,
      // which the receiving end (MaxonErrorFlagToEnumOp + assign of the binding)
      // consumes with a single decref instead of incref'ing again.
      //
      // Whether this site must MINT that reference depends on the thrown value's
      // provenance:
      //  - A FRESH construct (`throw PErr.x(...)`) arrives rc=0 from mm_alloc, and
      //    a BORROWED value (`throw self.field`) is owned by another local whose
      //    destructor would otherwise reclaim it during the caller's scope
      //    cleanup. Both need this incref to reach owned-on-delivery.
      //  - A CALL RESULT (`throw buildErr()`) already arrives rc=1: the callee
      //    transferred a reference through its own return-incref. Incref'ing here
      //    would deliver rc=2 against the receiver's single decref, leaking the
      //    error (OPEN #47 / #16). Skip it.
      //  - A KEPT OWNED LOCAL (`throw e`, or a re-thrown caught error) is a parser-level
      //    binding, NOT a lowering temp: scope-end now TRANSFERS it (keepVars, OPEN #63)
      //    instead of reclaiming it, so it already arrives rc=1. Incref'ing it too would
      //    deliver rc=2 against the receiver's single decref — a leak. So the incref fires
      //    only for a managed TEMP (a fresh construct or a borrowed field read) or a PARAM
      //    (`throw errArg`, owned by the caller), never a plain owned local. This mirrors
      //    LowerReturn, whose transfer-incref is likewise `isEnumParam || managed-temp`.
      // A KEPT OWNED LOCAL already owns its reference at rc=1; scope-end TRANSFERS it (keepVars),
      // so incref'ing it too delivers rc=2 against the receiver's single decref — a leak (OPEN #63).
      // Everything else reaching here still needs the incref to become owned-on-delivery: a fresh
      // CONSTRUCT (Orphan temp, rc=0), a BORROWED self-field (owned by the receiver's box, whose
      // destructor reclaims it during the caller's cleanup), and a PARAM (owned by the caller). So skip
      // the incref ONLY for a plain owned LOCAL binding — one that is NOT a lowering temp and NOT
      // borrowed / a self-field / a param — the same value scope-end's keepVars just protected.
      if (!temps.IsCallReturnTransfer(throwHp.VarName!) && !throwOp.IsOwnedLocalTransfer) {
        EmitIncref(block, throwHp.VarName!, varTypes, scopeName: throwOp.ErrorTypeName);
      }
      var heapPtr = EmitLoad(block, throwHp.VarName!, varTypes);
      block.AddOp(new StdErrorReturnOp(heapPtr));
    } else {
      // Simple error enum: the error value is the ordinal. Add 1 to make non-zero (0 = success).
      var errorVal = (StdI64)valueMap[throwOp.ErrorValue];
      var oneOp = new StdConstI64Op(1);
      block.AddOp(oneOp);
      var addOp = new StdAddI64Op(errorVal, oneOp.Result);
      block.AddOp(addOp);
      block.AddOp(new StdErrorReturnOp(addOp.Result));
    }
  }

  private static void LowerTryCall(
    MaxonTryCallOp tryCallOp,
    Dictionary<string, IrFunction<MaxonOp>> funcLookup,
    IrFunction<StandardOp> func,
    ref IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    VarRegistry temps,
    Dictionary<int, string>? fnEnvVarNames = null,
    Dictionary<int, StdI64>? fnEnvDirectValues = null) {
    // Intercept synthetic enum static method calls
    if (tryCallOp.Callee.StartsWith("__enum_fromRawValue:")) {
      var enumTypeName = tryCallOp.Callee["__enum_fromRawValue:".Length..];
      var enumType = (IrEnumType)typeDefs[enumTypeName];
      LowerEnumFromRawValue(tryCallOp, enumType, block, valueMap, varTypes);
      // No temp release needed — scope handles cleanup
      return;
    }
    if (tryCallOp.Callee.StartsWith("__enum_fromName:")) {
      var enumTypeName = tryCallOp.Callee["__enum_fromName:".Length..];
      var enumType = (IrEnumType)typeDefs[enumTypeName];
      LowerEnumFromName(tryCallOp, enumType, block, valueMap, varTypes, temps: temps);
      // No temp release needed — scope handles cleanup
      return;
    }
    LowerCallCore(tryCallOp.Callee, tryCallOp.Args, tryCallOp.Result,
      tryCallOp.ResultKind, isTryCall: true, funcLookup, func, ref block, valueMap, varTypes,
      typeDefs,
      temps,
      sourceCallOp: tryCallOp,
      errorFlagValue: tryCallOp.ErrorFlag,
      // A try-call carries a function argument's environment exactly as a plain call does. It used
      // to pass neither map, so FlattenCallArgs could only answer 0: `try apply(f, ...)` with a
      // capturing `f` nil-dereffed, in ANY block, including the one that bound it.
      fnEnvVarNames: fnEnvVarNames,
      fnEnvDirectValues: fnEnvDirectValues,
      argMutabilities: tryCallOp.ArgMutabilities, argVarNames: tryCallOp.ArgVarNames,
      callLine: tryCallOp.CallLine, callColumn: tryCallOp.CallColumn);
  }
}
