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

    FlattenCallArgs(args, calleeFunc, block, valueMap, varTypes, structVarNames, newArgs, callee, typeDefs, fnEnvVarNames, argVarNames);

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
      // Skip self argument for instance methods — callee doesn't own it
      if (i == 0 && calleeFunc.ParamNames.Count > 0 && calleeFunc.ParamNames[0] == "self") continue;
      // Determine type from structValueTypes or callee param type
      string? argTypeName = null;
      if (structValueTypes.TryGetValue(args[i].Id, out var svt)) {
        argTypeName = svt;
      } else if (i < calleeFunc.ParamTypes.Count && calleeFunc.ParamTypes[i] is MlirStructType paramStructType) {
        argTypeName = paramStructType.Name;
      }
      if (argTypeName == null) continue;
      if (!varTypes.ContainsKey(argVarName)) continue;
      var argHeapPtr = (StdI64)EmitLoad(block, argVarName, varTypes);
      EmitTypeAwareRelease(block, argHeapPtr, argTypeName, typeDefs);
    }

    // NOTE: Enum packaging blocks (from FlattenCallArgs) are NOT released here.
    // The callee may store the heap pointer (e.g., Array.push stores it in the buffer).
    // Releasing here would create dangling pointers in the array.
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
  /// Emit release (decref + free-if-zero) for all owned struct variables at scope exit.
  /// The returned variable is skipped since its ownership transfers to the caller.
  /// Uses type-aware release: structs with __ManagedMemory fields use maxon_release_with_managed
  /// to deep-free inner allocations when the outer refcount reaches 0.
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
  private static void EmitTypeAwareRelease(
    MlirBlock<StandardOp> block,
    StdI64 heapPtr,
    string typeName,
    Dictionary<string, MlirType> typeDefs) {
    // __ManagedMemory itself needs maxon_release_managed (frees buffer if capacity>0)
    if (typeName == "__ManagedMemory") {
      block.AddOp(new StdCallRuntimeOp("maxon_release_managed", [heapPtr], null));
      return;
    }

    // Check for array-of-managed-elements: dispatch to element-aware release.
    // Only applies to Array-like types where __ManagedMemory is at offset 8 (not String where it's at 0).
    if (typeDefs.TryGetValue(typeName, out var arrayTypeDef) && arrayTypeDef is MlirStructType arrayStructType
        && arrayStructType.TypeParams.TryGetValue("Element", out var elemType)
        && arrayStructType.Fields.Any(f => f.Type is MlirStructType st && st.Name == "__ManagedMemory" && f.Offset == 8)) {
      // Union-typed elements (associated-value enums): each element is a heap pointer
      if (elemType is MlirUnionType unionElemType && unionElemType.HasAssociatedValues) {
        block.AddOp(new StdCallRuntimeOp("maxon_release_array_of_simple", [heapPtr], null));
        return;
      }
      if (elemType is MlirStructType elemStructType) {
        var elemInnerFields = GetInnerHeapFields(elemStructType.Name, typeDefs);
        if (elemInnerFields.Count == 1 && elemInnerFields[0].isDirect) {
          // Element has direct __ManagedMemory (e.g., String)
          var elemOff = new StdConstI64Op(elemInnerFields[0].offset);
          block.AddOp(elemOff);
          block.AddOp(new StdCallRuntimeOp("maxon_release_array_of_with_managed",
            [heapPtr, elemOff.Result], null));
          return;
        } else if (elemInnerFields.Count == 0) {
          // Element is a simple struct (no managed fields)
          block.AddOp(new StdCallRuntimeOp("maxon_release_array_of_simple", [heapPtr], null));
          return;
        } else if (elemInnerFields.Count == 1 && !elemInnerFields[0].isDirect) {
          // Element has an indirect managed field (e.g., struct with Array field)
          var fieldOff = new StdConstI64Op(elemInnerFields[0].offset);
          var innerOff = new StdConstI64Op(elemInnerFields[0].innerManagedOffset);
          block.AddOp(fieldOff);
          block.AddOp(innerOff);
          block.AddOp(new StdCallRuntimeOp("maxon_release_array_of_nested",
            [heapPtr, fieldOff.Result, innerOff.Result], null));
          return;
        }
      }
      // For complex element types, fall through to the existing logic
    }

    // Look up struct type definition
    if (!typeDefs.TryGetValue(typeName, out var typeDef) || typeDef is not MlirStructType structType) {
      block.AddOp(new StdCallRuntimeOp("maxon_release", [heapPtr], null));
      return;
    }

    // Classify fields into __ManagedMemory vs other struct types
    var managedFields = new List<MlirStructField>();
    var structFields = new List<MlirStructField>();
    foreach (var field in structType.Fields) {
      if (field.Type is MlirStructType fieldSt) {
        if (fieldSt.Name == "__ManagedMemory")
          managedFields.Add(field);
        else
          structFields.Add(field);
      }
    }
    if (managedFields.Count == 0 && structFields.Count == 0) {
      block.AddOp(new StdCallRuntimeOp("maxon_release", [heapPtr], null));
      return;
    }

    // Managed-only fast path: use efficient runtime functions
    if (structFields.Count == 0 && managedFields.Count <= 3) {
      if (managedFields.Count == 1) {
        var off = new StdConstI64Op(managedFields[0].Offset);
        block.AddOp(off);
        block.AddOp(new StdCallRuntimeOp("maxon_release_with_managed", [heapPtr, off.Result], null));
      } else if (managedFields.Count == 2) {
        var off1 = new StdConstI64Op(managedFields[0].Offset);
        var off2 = new StdConstI64Op(managedFields[1].Offset);
        block.AddOp(off1);
        block.AddOp(off2);
        block.AddOp(new StdCallRuntimeOp("maxon_release_with_managed_2", [heapPtr, off1.Result, off2.Result], null));
      } else {
        var off1 = new StdConstI64Op(managedFields[0].Offset);
        var off2 = new StdConstI64Op(managedFields[1].Offset);
        var off3 = new StdConstI64Op(managedFields[2].Offset);
        block.AddOp(off1);
        block.AddOp(off2);
        block.AddOp(off3);
        block.AddOp(new StdCallRuntimeOp("maxon_release_with_managed_3", [heapPtr, off1.Result, off2.Result, off3.Result], null));
      }
      return;
    }

    // Has struct-typed fields: inline deep release with recursive field cleanup.
    // Uses StdNullSafeLoadI64Op so recursive calls are safe when heapPtr is null.
    var decrefResult = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_decref", [heapPtr], decrefResult));
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    // Combine null check with refcount check: only release when ptr is non-null AND refcount reached 0.
    // maxon_decref(null) returns 0, which would falsely trigger release without the null check.
    var isNotNull = new StdCmpI64Op("ne", heapPtr, zeroConst.Result);
    block.AddOp(isNotNull);
    var isRefZero = new StdCmpI64Op("eq", decrefResult, zeroConst.Result);
    block.AddOp(isRefZero);
    var shouldRelease = new StdAndI1Op(isNotNull.Result, isRefZero.Result);
    block.AddOp(shouldRelease);

    // Release struct-typed fields recursively
    foreach (var field in structFields) {
      var fieldSt = (MlirStructType)field.Type;
      var loadOp = new StdNullSafeLoadI64Op(heapPtr, field.Offset);
      block.AddOp(loadOp);
      var fieldOrNull = new StdSelectI64Op(shouldRelease.Result, loadOp.Result, zeroConst.Result);
      block.AddOp(fieldOrNull);
      EmitTypeAwareRelease(block, fieldOrNull.Result, fieldSt.Name, typeDefs);
    }

    // Release __ManagedMemory fields
    foreach (var field in managedFields) {
      var loadOp = new StdNullSafeLoadI64Op(heapPtr, field.Offset);
      block.AddOp(loadOp);
      var managedOrNull = new StdSelectI64Op(shouldRelease.Result, loadOp.Result, zeroConst.Result);
      block.AddOp(managedOrNull);
      block.AddOp(new StdCallRuntimeOp("maxon_release_managed", [managedOrNull.Result], null));
    }

    // Free the outer struct if refcount reached 0
    var outerOrNull = new StdSelectI64Op(shouldRelease.Result, heapPtr, zeroConst.Result);
    block.AddOp(outerOrNull);
    block.AddOp(new StdCallRuntimeOp("maxon_free", [outerOrNull.Result], null));
  }

  /// <summary>Check if a struct type directly contains a __ManagedMemory field.</summary>
  private static bool HasManagedMemoryField(MlirStructType structType) {
    return structType.Fields.Any(f => f.Type is MlirStructType st && st.Name == "__ManagedMemory");
  }

  /// <summary>
  /// Get information about inner heap fields that need cleanup (used for array-of-elements detection).
  /// Returns (offset, isDirect, innerManagedOffset) tuples where:
  /// - isDirect=true: field IS a __ManagedMemory (use maxon_release_managed)
  /// - isDirect=false: field is a struct CONTAINING __ManagedMemory at innerManagedOffset
  /// </summary>
  private static List<(int offset, bool isDirect, int innerManagedOffset)> GetInnerHeapFields(
    string typeName, Dictionary<string, MlirType> typeDefs) {
    var fields = new List<(int offset, bool isDirect, int innerManagedOffset)>();
    if (!typeDefs.TryGetValue(typeName, out var typeDef) || typeDef is not MlirStructType structType)
      return fields;

    foreach (var field in structType.Fields) {
      if (field.Type is MlirStructType fieldStructType) {
        if (fieldStructType.Name == "__ManagedMemory") {
          fields.Add((field.Offset, isDirect: true, innerManagedOffset: 0));
        } else {
          var managedField = fieldStructType.Fields.FirstOrDefault(
            f => f.Type is MlirStructType st && st.Name == "__ManagedMemory");
          if (managedField != null) {
            fields.Add((field.Offset, isDirect: false, innerManagedOffset: managedField.Offset));
          }
        }
      }
    }
    return fields;
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
    Dictionary<string, MlirType> typeDefs) {
    // Intercept synthetic enum static method calls
    if (tryCallOp.Callee.StartsWith("__enum_fromRawValue:")) {
      var enumTypeName = tryCallOp.Callee["__enum_fromRawValue:".Length..];
      var enumType = (MlirUnionType)typeDefs[enumTypeName];
      LowerUnionFromRawValue(tryCallOp, enumType, block, valueMap, varTypes, structVarNames);
      return;
    }
    if (tryCallOp.Callee.StartsWith("__enum_fromName:")) {
      var enumTypeName = tryCallOp.Callee["__enum_fromName:".Length..];
      var enumType = (MlirUnionType)typeDefs[enumTypeName];
      LowerUnionFromName(tryCallOp, enumType, block, valueMap, varTypes, structVarNames, structValueTypes);
      return;
    }
    LowerCallCore(tryCallOp.Callee, tryCallOp.Args, tryCallOp.Result,
      tryCallOp.ResultKind, isTryCall: true, funcLookup, block, valueMap, varTypes,
      structVarNames, structValueTypes, typeDefs,
      tryCallOp.ErrorFlag,
      argMutabilities: tryCallOp.ArgMutabilities, argVarNames: tryCallOp.ArgVarNames);
  }
}
