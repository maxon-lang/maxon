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
    Dictionary<int, string>? fnEnvVarNames = null,
    Dictionary<string, (string ElementTypeName, int ElementCount, string ArrayTag)>? initBufferElementInfo = null) {
    LowerCallCore(callOp.Callee, callOp.Args, callOp.Result, callOp.ResultKind,
      isTryCall: false, funcLookup, block, valueMap, varTypes, structVarNames,
      structValueTypes, typeDefs, fnEnvVarNames: fnEnvVarNames,
      argMutabilities: callOp.ArgMutabilities, argVarNames: callOp.ArgVarNames,
      initBufferElementInfo: initBufferElementInfo);
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
    List<string?>? argVarNames = null,
    Dictionary<string, (string ElementTypeName, int ElementCount, string ArrayTag)>? initBufferElementInfo = null) {

    var calleeFunc = ResolveCallee(callee, funcLookup);
    // Use the resolved fully-qualified name for call emission (e.g., "String.hash" → "stdlib.String.hash")
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
    // Struct returns and associated-value enum returns are i64 heap pointers
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
        // In loops, the same slot is reused — release old value before overwriting.
        // The slot is zero-initialized in the entry block, so first iteration is a no-op.
        if (varTypes.ContainsKey(retVarName)) {
          var oldVal = (StdI64)EmitLoad(block, retVarName, varTypes);
          EmitTypeAwareRelease(block, oldVal, calleeRetStructType.Name, typeDefs);
        }
        EmitStore(block, callResult, retVarName, varTypes);
        structVarNames[result.Id] = retVarName;
        structValueTypes[result.Id] = calleeRetStructType.Name;
      } else if (calleeRetAssocEnum && callResult != null) {
        // Associated-value enum return: unpack heap pointer into flat vars
        var retEnumType = (MlirUnionType)calleeFunc.ReturnType!;
        var retVarName = $"__callret_{result.Id}";
        // In loops, release old enum heap block before overwriting
        if (varTypes.ContainsKey(retVarName)) {
          var oldEnumVal = (StdI64)EmitLoad(block, retVarName, varTypes);
          block.AddOp(new StdCallRuntimeOp("maxon_release", [oldEnumVal], null));
        }

        if (isTryCall) {
          // try_call returns null (0) on error — guard against null dereference
          // by substituting a dummy allocation when the pointer is null
          int maxPayloadForSize = GetMaxFlatPayloadSlots(retEnumType, typeDefs);
          int heapSize = 8 + maxPayloadForSize * 8;
          var dummyPtr = EmitAlloc(block, heapSize);
          var zeroConst = new StdConstI64Op(0);
          block.AddOp(zeroConst);
          var isNull = new StdCmpI64Op("eq", (StdI64)callResult, zeroConst.Result);
          block.AddOp(isNull);
          var safePtr = new StdSelectI64Op(isNull.Result, dummyPtr, (StdI64)callResult);
          block.AddOp(safePtr);
          EmitStore(block, safePtr.Result, retVarName, varTypes);
          // Free the unused pointer: on success the dummy is unused, on error callResult is 0.
          // Select: isNull=true (error) → 0 (no-op), isNull=false (success) → dummyPtr
          var dummyToFree = new StdSelectI64Op(isNull.Result, zeroConst.Result, dummyPtr);
          block.AddOp(dummyToFree);
          block.AddOp(new StdCallRuntimeOp("maxon_release", [dummyToFree.Result], null));
        } else {
          EmitStore(block, callResult, retVarName, varTypes);
        }

        UnpackEnumHeapToFlatVars(block, retVarName, retEnumType, varTypes, typeDefs);
        structVarNames[result.Id] = retVarName;
        structValueTypes[result.Id] = retEnumType.Name;
      } else if (callResult != null) {
        // Widen I32/U32 call results to I64 to avoid width mismatches
        // (e.g., try...otherwise where default is I64 but call result is U32)
        if (callResult is StdI32) {
          callResult = EnsureI64(callResult is StdU32 u32cr ? new StdI32(u32cr.Id) : callResult, block, signExtend: callResult is not StdU32);
        }
        valueMap[result] = callResult;
      }
    }

    // Release literal temporary arguments after the call.
    // String/character literals create heap-allocated temps that the callee doesn't own.
    // Also release __struct_* temps (array/managed literal temps passed to init functions).
    for (int i = 0; i < args.Count; i++) {
      if (!structVarNames.TryGetValue(args[i].Id, out var argVarName)) continue;
      // Release known literal temps and struct literal temps
      bool isLiteralTemp = argVarName.StartsWith("__strtmp_") || argVarName.StartsWith("__chrtmp_")
          || argVarName.StartsWith("__interptmp_") || argVarName.StartsWith("__bstrtmp_");
      bool isStructLiteralTemp = argVarName.StartsWith("__struct_");
      if (!isLiteralTemp && !isStructLiteralTemp) continue;
      // Determine type from structValueTypes or callee param type
      string? argTypeName = null;
      if (structValueTypes.TryGetValue(args[i].Id, out var svt)) {
        argTypeName = svt;
      } else if (i < calleeFunc.ParamTypes.Count && calleeFunc.ParamTypes[i] is MlirStructType paramStructType) {
        argTypeName = paramStructType.Name;
      }
      if (argTypeName == null) continue;
      if (!varTypes.ContainsKey(argVarName)) continue;
      // For __ManagedMemory init buffers with struct elements (e.g., Map literal key buffers),
      // release each element before destroying the buffer. __destroy___ManagedMemory only frees
      // memory structures; it doesn't iterate/release struct elements stored in the buffer.
      if (initBufferElementInfo != null
          && initBufferElementInfo.TryGetValue(argVarName, out var elemInfo)) {
        for (int ei = 0; ei < elemInfo.ElementCount; ei++) {
          var elemVarName = $"{elemInfo.ArrayTag}.{ei}";
          if (varTypes.ContainsKey(elemVarName)) {
            var elemHeapPtr = (StdI64)EmitLoad(block, elemVarName, varTypes);
            EmitTypeAwareRelease(block, elemHeapPtr, elemInfo.ElementTypeName, typeDefs);
          }
        }
      }
      var argHeapPtr = (StdI64)EmitLoad(block, argVarName, varTypes);
      EmitTypeAwareRelease(block, argHeapPtr, argTypeName, typeDefs);
    }

    // Release enum packaging blocks created by FlattenCallArgs.
    // The callee increfs if it stores the pointer (e.g., Array.push), so decref is safe.
    foreach (var (ptr, typeName) in enumPackagingBlocks) {
      EmitTypeAwareRelease(block, ptr, typeName, typeDefs);
    }
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
    Dictionary<string, string> ownedStructVars,
    Dictionary<int, string> fnEnvVarNames) {

    // Error propagation: forward the error flag to the caller
    if (retOp.IsErrorPropagation) {
      var mappedErrFlag = valueMap[retOp.Value!];
      block.AddOp(new StdErrorReturnOp(mappedErrFlag));
      return;
    }

    // No self write-back needed: with heap refs, all field mutations go through
    // the heap pointer directly, so the caller sees changes automatically.

    // Determine which variable is being returned (skip cleanup for it — ownership transfers to caller)
    string? returnedVarName = null;
    if (retOp.Value != null && structVarNames.TryGetValue(retOp.Value.Id, out var retVarName)) {
      returnedVarName = retVarName;
    }

    // Emit decref for all owned struct vars that are NOT the returned value
    EmitScopeCleanup(block, ownedStructVars, returnedVarName, varTypes, typeDefs);

    // Free closure environment allocations
    foreach (var envVarName in fnEnvVarNames.Values.Distinct()) {
      var envPtr = (StdI64)EmitLoad(block, envVarName, varTypes);
      block.AddOp(new StdCallRuntimeOp("maxon_release", [envPtr], null));
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
      StdValue retHeapPtr;
      if (structVarNames.TryGetValue(retOp.Value.Id, out var srcName)) {
        retHeapPtr = EmitLoad(block, srcName, varTypes);
      } else {
        // Value is already an i64 (e.g. struct element pointer from managed memory)
        retHeapPtr = valueMap[retOp.Value];
      }
      block.AddOp(new StdReturnOp(retHeapPtr));
    } else {
      StdValue? newRetVal = retOp.Value != null ? valueMap[retOp.Value] : null;
      block.AddOp(new StdReturnOp(newRetVal));
    }
  }

  /// <summary>
  /// Emit release for all owned struct variables at scope exit.
  /// The returned variable is skipped since its ownership transfers to the caller.
  /// Each variable is released via its type-specific destructor (__destroy_TypeName).
  /// </summary>
  private static void EmitScopeCleanup(
    MlirBlock<StandardOp> block,
    Dictionary<string, string> ownedStructVars,
    string? returnedVarName,
    Dictionary<string, string> varTypes,
    Dictionary<string, MlirType> typeDefs) {
    foreach (var (varName, typeName) in ownedStructVars) {
      if (varName == returnedVarName) continue;
      Logger.Debug(LogCategory.Mlir, $"Scope cleanup: releasing '{varName}' type={typeName} (returned={returnedVarName})");
      var heapPtr = (StdI64)EmitLoad(block, varName, varTypes);
      EmitTypeAwareRelease(block, heapPtr, typeName, typeDefs);
    }
  }

  /// <summary>
  /// Emit the appropriate release call for a heap pointer based on its type.
  /// Recursively resolves inner struct fields to find __ManagedMemory and call
  /// the right runtime release function.
  /// </summary>
  /// <summary>
  /// Emits a call to the type-specific destructor for the given heap pointer.
  /// Each type has a generated __destroy_TypeName function that handles null-check,
  /// decref, recursive field cleanup, and freeing.
  /// </summary>
  private static void EmitTypeAwareRelease(
    MlirBlock<StandardOp> block,
    StdI64 heapPtr,
    string typeName,
    Dictionary<string, MlirType> typeDefs) {
    var destroyName = $"__destroy_{SanitizeDestructorName(typeName)}";
    block.AddOp(new StdCallRuntimeOp(destroyName, [heapPtr], null));
  }

  private static string SanitizeDestructorName(string typeName) {
    return typeName.Replace(" ", "_").Replace("<", "_").Replace(">", "_").Replace(",", "_");
  }

  /// <summary>
  /// Releases the String/Character temporary argument used by inline enum fromName/fromRawValue lowering.
  /// These methods bypass LowerCallCore's post-call cleanup, so temps must be freed here.
  /// </summary>
  private static void ReleaseInlineEnumStringArg(
    MaxonTryCallOp tryCallOp,
    MlirUnionType enumType,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<string, MlirType> typeDefs) {
    var firstArg = tryCallOp.Args[0];
    if (!structVarNames.TryGetValue(firstArg.Id, out var argVarName)) return;
    bool isTemp = argVarName.StartsWith("__strtmp_") || argVarName.StartsWith("__chrtmp_")
        || argVarName.StartsWith("__interptmp_") || argVarName.StartsWith("__bstrtmp_");
    if (!isTemp) return;
    if (!varTypes.ContainsKey(argVarName)) return;
    var argHeapPtr = (StdI64)EmitLoad(block, argVarName, varTypes);
    // String and Character are both struct types
    var argTypeName = argVarName.StartsWith("__chrtmp_") ? "Character" : "String";
    EmitTypeAwareRelease(block, argHeapPtr, argTypeName, typeDefs);
  }

  private static void LowerThrow(
    MaxonThrowOp throwOp,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<int, string> structVarNames,
    Dictionary<string, string> varTypes,
    Dictionary<string, MlirType> typeDefs,
    Dictionary<string, string> ownedStructVars,
    Dictionary<int, string> fnEnvVarNames) {
    // Clean up owned struct vars before throwing (no struct is being "returned")
    EmitScopeCleanup(block, ownedStructVars, returnedVarName: null, varTypes, typeDefs);

    // Free closure environment allocations
    foreach (var envVarName in fnEnvVarNames.Values.Distinct()) {
      var envPtr = (StdI64)EmitLoad(block, envVarName, varTypes);
      block.AddOp(new StdCallRuntimeOp("maxon_release", [envPtr], null));
    }

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
    Dictionary<string, MlirType> typeDefs,
    Dictionary<string, (string ElementTypeName, int ElementCount, string ArrayTag)>? initBufferElementInfo = null) {
    // Intercept synthetic enum static method calls
    if (tryCallOp.Callee.StartsWith("__enum_fromRawValue:")) {
      var enumTypeName = tryCallOp.Callee["__enum_fromRawValue:".Length..];
      var enumType = (MlirUnionType)typeDefs[enumTypeName];
      LowerUnionFromRawValue(tryCallOp, enumType, block, valueMap, varTypes, structVarNames);
      ReleaseInlineEnumStringArg(tryCallOp, enumType, block, varTypes, structVarNames, typeDefs);
      return;
    }
    if (tryCallOp.Callee.StartsWith("__enum_fromName:")) {
      var enumTypeName = tryCallOp.Callee["__enum_fromName:".Length..];
      var enumType = (MlirUnionType)typeDefs[enumTypeName];
      LowerUnionFromName(tryCallOp, enumType, block, valueMap, varTypes, structVarNames, structValueTypes);
      ReleaseInlineEnumStringArg(tryCallOp, enumType, block, varTypes, structVarNames, typeDefs);
      return;
    }
    LowerCallCore(tryCallOp.Callee, tryCallOp.Args, tryCallOp.Result,
      tryCallOp.ResultKind, isTryCall: true, funcLookup, block, valueMap, varTypes,
      structVarNames, structValueTypes, typeDefs,
      tryCallOp.ErrorFlag,
      argMutabilities: tryCallOp.ArgMutabilities, argVarNames: tryCallOp.ArgVarNames,
      initBufferElementInfo: initBufferElementInfo);
  }
}
