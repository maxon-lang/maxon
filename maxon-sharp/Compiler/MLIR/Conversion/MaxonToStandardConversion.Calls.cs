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
    Dictionary<int, string>? fnEnvVarNames = null) {
    LowerCallCore(callOp.Callee, callOp.Args, callOp.Result, callOp.ResultKind,
      isTryCall: false, funcLookup, block, valueMap, varTypes, structVarNames,
      structValueTypes, typeDefs, fnEnvVarNames: fnEnvVarNames,
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
    MaxonValue? errorFlagValue = null,
    Dictionary<int, string>? fnEnvVarNames = null,
    List<bool>? argMutabilities = null,
    List<string?>? argVarNames = null) {

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

    var enumPackagingBlocks = new List<(StdI64 ptr, string typeName)>();
    FlattenCallArgs(args, calleeFunc, block, valueMap, varTypes, structVarNames, newArgs, callee, typeDefs, fnEnvVarNames, argVarNames, enumPackagingBlocks);

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
        // Struct return: store the heap pointer in a named variable
        var retVarName = $"__callret_{result.Id}";
        EmitStore(block, callResult, retVarName, varTypes);
        structVarNames[result.Id] = retVarName;
        structValueTypes[result.Id] = calleeRetStructType.Name;
      } else if (calleeRetAssocEnum && callResult != null) {
        // Associated-value enum return: unpack heap pointer into flat vars
        var retEnumType = (MlirUnionType)calleeFunc.ReturnType!;
        var retVarName = $"__callret_{result.Id}";

        if (isTryCall) {
          // try_call returns null (0) on error — guard against null dereference
          // by substituting a dummy allocation when the pointer is null
          int maxPayloadForSize = GetMaxFlatPayloadSlots(retEnumType, typeDefs);
          int heapSize = 8 + maxPayloadForSize * 8;
          var dummyPtr = EmitAlloc(block, heapSize, "EnumDummy");
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

        UnpackEnumHeapToFlatVars(block, retVarName, retEnumType, varTypes, typeDefs);
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
    Dictionary<string, MlirType> typeDefs) {

    // Error propagation: forward the error flag to the caller
    if (retOp.IsErrorPropagation) {
      var mappedErrFlag = valueMap[retOp.Value!];
      block.AddOp(new StdErrorReturnOp(mappedErrFlag));
      return;
    }

    // Associated-value enum return: caller expects a heap pointer, not flat vars
    if (retOp.Value != null
        && structVarNames.TryGetValue(retOp.Value.Id, out var enumRetPrefix)
        && structValueTypes.TryGetValue(retOp.Value.Id, out var enumRetTypeName)
        && typeDefs.TryGetValue(enumRetTypeName, out var enumRetTypeDef)
        && enumRetTypeDef is MlirUnionType enumRetType && enumRetType.HasAssociatedValues) {
      var heapPtr = PackEnumFlatVarsToHeap(block, enumRetPrefix, enumRetType, varTypes, typeDefs);
      block.AddOp(new StdReturnOp(heapPtr));
      return;
    }

    if (retStructType != null && retOp.Value != null) {
      // Struct return: return the heap pointer as i64
      // mm_move is handled by MaxonMoveOp emitted before scope exits in the parser
      StdValue retHeapPtr;
      if (structVarNames.TryGetValue(retOp.Value.Id, out var srcName)) {
        retHeapPtr = EmitLoad(block, srcName, varTypes);
      } else {
        retHeapPtr = valueMap[retOp.Value];
      }
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
    // No scope cleanup needed — the parser emits MaxonScopeExitOp before throws,
    // and the memory manager handles all cleanup via mm_scope_exit.

    // Check if this is an associated-value error enum
    if (structVarNames.TryGetValue(throwOp.ErrorValue.Id, out var enumPrefix)
        && typeDefs.TryGetValue(throwOp.ErrorTypeName, out var errorTypeDef)
        && errorTypeDef is MlirUnionType errorEnumType && errorEnumType.HasAssociatedValues) {
      // Error return expects a heap pointer in RDX, not flat vars
      var heapPtr = PackEnumFlatVarsToHeap(block, enumPrefix, errorEnumType, varTypes, typeDefs);
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
    Dictionary<string, MlirType> typeDefs) {
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
      LowerUnionFromName(tryCallOp, enumType, block, valueMap, varTypes, structVarNames, structValueTypes, typeDefs);
      // No temp release needed — scope handles cleanup
      return;
    }
    LowerCallCore(tryCallOp.Callee, tryCallOp.Args, tryCallOp.Result,
      tryCallOp.ResultKind, isTryCall: true, funcLookup, block, valueMap, varTypes,
      structVarNames, structValueTypes, typeDefs,
      tryCallOp.ErrorFlag,
      argMutabilities: tryCallOp.ArgMutabilities, argVarNames: tryCallOp.ArgVarNames);
  }
}
