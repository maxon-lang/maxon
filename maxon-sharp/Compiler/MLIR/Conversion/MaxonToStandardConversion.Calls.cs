using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static partial class MaxonToStandardConversion {
  private static MlirType ResolveEnumBackingMlirType(MlirUnionType enumType) {
    if (enumType.BackingType == MlirType.F64) return MlirType.F64;
    if (enumType.BackingType is MlirStringBackingType or MlirCharBackingType) return MlirType.I64;
    if (enumType.BackingType == MlirType.I64 || enumType.BackingType == null) return MlirType.I64;
    throw new InvalidOperationException($"Unsupported enum backing type: {enumType.BackingType}");
  }

  /// <summary>
  /// Handles method calls on primitive types as intrinsics (e.g. i64.hash, i8.hash).
  /// Returns true if the call was handled, false to fall through to normal LowerCall.
  /// </summary>
  private static bool TryLowerPrimitiveMethod(
    MaxonCallOp callOp,
    MlirBlock<StandardOp> block,
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
    Dictionary<string, MlirFunction<MaxonOp>> funcLookup,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirType> typeDefs,
    VarRegistry temps,
    Dictionary<int, string>? fnEnvVarNames = null) {
    LowerCallCore(callOp.Callee, callOp.Args, callOp.Result, callOp.ResultKind,
      isTryCall: false, funcLookup, block, valueMap, varTypes, structVarNames,
      structValueTypes, typeDefs, temps, fnEnvVarNames: fnEnvVarNames,
      argMutabilities: callOp.ArgMutabilities, argVarNames: callOp.ArgVarNames);
  }

  /// <summary>
  /// Shared implementation for lowering both MaxonCallOp and MaxonTryCallOp.
  /// For try calls, pass errorFlagValue to map the error flag into valueMap.
  /// </summary>
  private static void LowerCallCore(
    string callee,
    List<MaxonValue> args,
    MaxonValue? result,
    MaxonValueKind? resultKind,
    bool isTryCall,
    Dictionary<string, MlirFunction<MaxonOp>> funcLookup,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirType> typeDefs,
    VarRegistry temps,
    MaxonValue? errorFlagValue = null,
    Dictionary<int, string>? fnEnvVarNames = null,
    List<bool>? argMutabilities = null,
    List<string?>? argVarNames = null) {

    // Intercept synthetic chain navigation calls before resolving the callee
    // (these are not real functions in the module)
    if (TryLowerChainNavigation(callee, args, result, isTryCall, block, valueMap,
        varTypes, structVarNames, structValueTypes, errorFlagValue, temps))
      return;

    var calleeFunc = ResolveCallee(callee, funcLookup);
    var resolvedCallee = calleeFunc.Name;
    var calleeRetStructType = ResolveStructReturnType(calleeFunc.ReturnType, typeDefs);

    var newArgs = new List<StdValue>();

    // Mutability enforcement: immutable args cannot be passed to mutating params
    if (_mutatingParams != null && _mutatingParams.TryGetValue(calleeFunc.Name, out var calleeMutParams)
        && argMutabilities != null) {
      for (int i = 0; i < calleeFunc.ParamNames.Count && i < argMutabilities.Count; i++) {
        if (calleeMutParams.Contains(calleeFunc.ParamNames[i]) && !argMutabilities[i]) {
          throw new CompileError(
            ErrorCode.SemanticImmutableRefToMutatingParam,
            $"cannot pass immutable 'let' variable to function that mutates parameter '{calleeFunc.ParamNames[i]}'");
        }
      }
    }

    FlattenCallArgs(args, calleeFunc, block, valueMap, varTypes, structVarNames, newArgs, callee, fnEnvVarNames, argVarNames);

    // Check if callee returns an associated-value enum (passed as heap pointer)
    bool calleeRetAssocEnum = calleeFunc.ReturnType is MlirUnionType cret && cret.HasAssociatedValues;

    // Emit call or try_call
    StdValue? callResult = calleeRetStructType != null || calleeRetAssocEnum
      ? new StdI64(MlirContext.Current.NextId())
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
        structVarNames[result.Id] = retVarName;
        structValueTypes[result.Id] = calleeRetStructType.Name;
      } else if (calleeRetAssocEnum && callResult != null) {
        // Associated-value enum return: store heap pointer (no unpacking needed)
        var retEnumType = (MlirUnionType)calleeFunc.ReturnType!;
        var retVarName = temps.CreateTemp("callret", result.Id, retEnumType.Name, OwnershipFlags.Orphan | OwnershipFlags.CallReturn);

        if (isTryCall) {
          // try_call returns null (0) on error — guard against null dereference
          // by substituting a dummy allocation when the pointer is null
          int maxPayloadForSize = GetMaxFlatPayloadSlots(retEnumType);
          int heapSize = 8 + maxPayloadForSize * 8;
          var dummyPtr = EmitAlloc(block, heapSize, "EnumDummy", scopeName: _currentFuncName);
          var zeroConst = new StdConstI64Op(0);
          block.AddOp(zeroConst);
          var isNull = new StdCmpI64Op("eq", (StdI64)callResult, zeroConst.Result);
          block.AddOp(isNull);
          var safePtr = new StdSelectI64Op(isNull.Result, dummyPtr, (StdI64)callResult);
          block.AddOp(safePtr);
          EmitStore(block, safePtr.Result, retVarName, varTypes);
        } else {
          EmitStore(block, callResult, retVarName, varTypes);
        }

        structVarNames[result.Id] = retVarName;
        structValueTypes[result.Id] = retEnumType.Name;
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
    MlirStructType? retStructType,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirType> typeDefs,
    string funcName,
    VarRegistry temps) {

    // Error propagation: forward the error flag to the caller
    if (retOp.IsErrorPropagation) {
      var mappedErrFlag = valueMap[retOp.Value!];
      block.AddOp(new StdErrorReturnOp(mappedErrFlag));
      return;
    }

    // Associated-value enum return: the enum is already a heap pointer.
    if (retOp.Value != null
        && structVarNames.TryGetValue(retOp.Value.Id, out var enumRetPrefix)
        && structValueTypes.TryGetValue(retOp.Value.Id, out var enumRetTypeName)
        && typeDefs.TryGetValue(enumRetTypeName, out var enumRetTypeDef)
        && enumRetTypeDef is MlirUnionType enumRetType && enumRetType.HasAssociatedValues) {
      var retHeapPtr = EmitLoad(block, enumRetPrefix, varTypes);
      block.AddOp(new StdReturnOp(retHeapPtr));
      return;
    }

    if (retStructType != null && retOp.Value != null) {
      // Struct return: return the heap pointer as i64
      StdValue retHeapPtr;
      if (structVarNames.TryGetValue(retOp.Value.Id, out var srcName)) {
        // Incref before return: the function's scope-end will decref all locals,
        // so the returned value needs an extra reference for the caller.
        // Skip SelfReturn (alias, not owned).
        // Skip Orphan temps: their scope-end cleanup is already skipped for returned values,
        // so the single reference from creation transfers directly to the caller.
        if (temps.IsTempManaged(srcName)
              && !temps.TempHasFlag(srcName, OwnershipFlags.SelfReturn)
              && !temps.TempHasFlag(srcName, OwnershipFlags.Orphan)) {
          EmitIncref(block, srcName, varTypes, scopeName: funcName);
          EmitTransfer(block, srcName, varTypes, funcName);
        }
        retHeapPtr = EmitLoad(block, srcName, varTypes);
      } else {
        retHeapPtr = valueMap[retOp.Value];
      }
      block.AddOp(new StdReturnOp(retHeapPtr));
    } else if (retOp.Value != null && structVarNames.TryGetValue(retOp.Value.Id, out var fallbackSrcName)) {
      // Value is a heap pointer (registered by chain/managed-memory ops) but the
      // function's return type is unresolved (e.g., type parameter "Element" in a
      // generic template function). Return the heap pointer as i64.
      var retHeapPtr = EmitLoad(block, fallbackSrcName, varTypes);
      block.AddOp(new StdReturnOp(retHeapPtr));
    } else {
      StdValue? newRetVal = retOp.Value != null ? valueMap[retOp.Value] : null;
      block.AddOp(new StdReturnOp(newRetVal));
    }
  }


  private static void LowerThrow(
    MaxonThrowOp throwOp,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<int, string> structVarNames,
    Dictionary<string, string> varTypes,
    Dictionary<string, MlirType> typeDefs) {
    // Scope cleanup is handled by MaxonScopeEndOp lowering before throw ops.

    // Check if this is an associated-value error enum
    if (structVarNames.TryGetValue(throwOp.ErrorValue.Id, out var enumPrefix)
        && typeDefs.TryGetValue(throwOp.ErrorTypeName, out var errorTypeDef)
        && errorTypeDef is MlirUnionType errorEnumType && errorEnumType.HasAssociatedValues) {
      // Error return expects a heap pointer in RDX — already a heap pointer
      var heapPtr = EmitLoad(block, enumPrefix, varTypes);
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
    Dictionary<string, MlirFunction<MaxonOp>> funcLookup,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirType> typeDefs,
    VarRegistry temps) {
    // Intercept synthetic enum static method calls
    if (tryCallOp.Callee.StartsWith("__enum_fromRawValue:")) {
      var enumTypeName = tryCallOp.Callee["__enum_fromRawValue:".Length..];
      var enumType = (MlirUnionType)typeDefs[enumTypeName];
      LowerUnionFromRawValue(tryCallOp, enumType, block, valueMap, varTypes, structVarNames);
      // No temp release needed — scope handles cleanup
      return;
    }
    if (tryCallOp.Callee.StartsWith("__enum_fromName:")) {
      var enumTypeName = tryCallOp.Callee["__enum_fromName:".Length..];
      var enumType = (MlirUnionType)typeDefs[enumTypeName];
      LowerUnionFromName(tryCallOp, enumType, block, valueMap, varTypes, structVarNames, structValueTypes, temps: temps);
      // No temp release needed — scope handles cleanup
      return;
    }
    LowerCallCore(tryCallOp.Callee, tryCallOp.Args, tryCallOp.Result,
      tryCallOp.ResultKind, isTryCall: true, funcLookup, block, valueMap, varTypes,
      structVarNames, structValueTypes, typeDefs,
      temps,
      errorFlagValue: tryCallOp.ErrorFlag,
      argMutabilities: tryCallOp.ArgMutabilities, argVarNames: tryCallOp.ArgVarNames);
  }
}
