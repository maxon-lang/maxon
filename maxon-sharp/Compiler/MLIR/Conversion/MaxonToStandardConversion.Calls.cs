using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  private static readonly HashSet<string> ThrowingManagedMemBuiltins = [
    "__managed_mem_get", "__managed_mem_set", "__managed_mem_remove",
    "__managed_mem_byte_at", "__managed_mem_set_byte",
    "__managed_mem_grow", "__managed_mem_set_length",
    "__managed_mem_shift_right", "__managed_mem_shift_left",
    "__managed_mem_create", "__managed_mem_slice"
  ];

  private static bool IsThrowingManagedMemBuiltin(string callee) =>
    ThrowingManagedMemBuiltins.Contains(callee);

  private static IrType ResolveEnumBackingIrType(IrEnumType enumType) {
    if (enumType.BackingType == IrType.F64) return IrType.F64;
    if (enumType.BackingType is IrStringBackingType or IrCharBackingType) return IrType.I64;
    if (enumType.BackingType is IrStructBackingType) return IrType.I64;
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
    Dictionary<int, string>? fnEnvVarNames = null) {
    LowerCallCore(callOp.Callee, callOp.Args, callOp.Result, callOp.ResultKind,
      isTryCall: false, funcLookup, func, ref block, valueMap, varTypes,
      typeDefs, temps, sourceCallOp: callOp, fnEnvVarNames: fnEnvVarNames,
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
        varTypes, errorFlagValue, temps))
      return;

    // Intercept synthetic __ManagedMemory builtin calls (throwing variants of get/set/slice/etc.)
    if (TryLowerManagedMemBuiltin(callee, args, result, func, ref block,
        valueMap, varTypes, typeDefs, errorFlagValue, temps, sourceCallOp))
      return;

    // Throwing builtins must always be called via try (the parser enforces this via
    // ValidateThrowingBuiltinCallContext). A non-try call reaching here is a compiler bug.
    if (!isTryCall && IsThrowingManagedMemBuiltin(callee))
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

    FlattenCallArgs(args, calleeFunc, block, valueMap, varTypes, newArgs, callee, fnEnvVarNames, argVarNames);

    // Check if callee returns an associated-value enum (passed as heap pointer)
    bool calleeRetAssocEnum = calleeFunc.ReturnType is IrEnumType cret && cret.HasAssociatedValues;

    // Emit call or try_call
    StdValue? callResult = calleeRetStructType != null || calleeRetAssocEnum
      ? new StdI64(IrContext.Current.NextId())
      : ResolveCallResultType(resultKind, calleeFunc.ReturnType);
    if (isTryCall) {
      var tryCall = new StdTryCallOp(resolvedCallee, newArgs, callResult);
      block.AddOp(tryCall);
      if (errorFlagValue != null) {
        valueMap[errorFlagValue] = tryCall.ErrorFlag;
        EmitStore(block, tryCall.ErrorFlag, "__error_flag", varTypes);
      }
    } else {
      block.AddOp(new StdCallOp(resolvedCallee, newArgs, callResult));
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
        // Associated-value enum return: store heap pointer (no unpacking needed)
        var retEnumType = (IrEnumType)calleeFunc.ReturnType!;
        var retVarName = temps.CreateTemp("callret", result.Id, retEnumType.Name, OwnershipFlags.Orphan | OwnershipFlags.CallReturn);

        if (isTryCall) {
          // try_call returns null (0) on error — guard against null dereference
          // by substituting a dummy allocation when the pointer is null.
          // The dummy is increffed to rc=1 so it matches the ownership semantics
          // of a CallReturn transfer — scope cleanup will decref whichever value
          // was selected (real result or dummy) and both will end up properly freed.
          int maxPayloadForSize = GetMaxFlatPayloadSlots(retEnumType);
          int heapSize = 8 + maxPayloadForSize * 8;
          var dummyPtr = EmitAlloc(block, heapSize, "EnumDummy", scopeName: _currentFuncName);
          EmitIncrefValue(block, dummyPtr, scopeName: _currentFuncName);
          var zeroConst = new StdConstI64Op(0);
          block.AddOp(zeroConst);
          var isNull = new StdCmpI64Op("eq", (StdI64)callResult, zeroConst.Result);
          block.AddOp(isNull);
          var safePtr = new StdSelectI64Op(isNull.Result, dummyPtr, (StdI64)callResult);
          block.AddOp(safePtr);
          // Decref the non-selected value: if call succeeded, free the dummy;
          // if call errored, the real result is null (no decref needed).
          var notSelected = new StdSelectI64Op(isNull.Result, (StdI64)callResult, dummyPtr);
          block.AddOp(notSelected);
          EmitDecrefValueIfNonnull(block, notSelected.Result, scopeName: _currentFuncName);
          EmitStore(block, safePtr.Result, retVarName, varTypes);
        } else {
          EmitStore(block, callResult, retVarName, varTypes);
        }

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

  private static void LowerReturn(
    MaxonReturnOp retOp,
    IrStructType? retStructType,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    string funcName,
    VarRegistry temps,
    bool functionReturnsSelf = false) {

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
    Dictionary<string, IrType> typeDefs) {
    // Scope cleanup is handled by MaxonScopeEndOp lowering before throw ops.

    // Check if this is an associated-value error enum
    if (valueMap.TryGetValue(throwOp.ErrorValue, out var throwSv) && throwSv is StdHeapPtr throwHp
        && typeDefs.TryGetValue(throwOp.ErrorTypeName, out var errorTypeDef)
        && errorTypeDef is IrEnumType errorEnumType && errorEnumType.HasAssociatedValues) {
      // Error return expects a heap pointer in RDX — already a heap pointer
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
    VarRegistry temps) {
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
      argMutabilities: tryCallOp.ArgMutabilities, argVarNames: tryCallOp.ArgVarNames,
      callLine: tryCallOp.CallLine, callColumn: tryCallOp.CallColumn);
  }
}
