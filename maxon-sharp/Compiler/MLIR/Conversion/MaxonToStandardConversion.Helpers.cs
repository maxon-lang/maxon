using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static partial class MaxonToStandardConversion {
  /// <summary>
  /// Re-resolves a struct type from typeDefs if the captured instance has no fields
  /// (forward-referenced types captured before their fields were parsed).
  /// </summary>
  private static MlirStructType ResolveStructType(MlirStructType structType, Dictionary<string, MlirType> typeDefs) {
    if (structType.Fields.Count > 0) return structType;
    if (typeDefs.TryGetValue(structType.Name, out var resolved) && resolved is MlirStructType resolvedStruct && resolvedStruct.Fields.Count > 0)
      return resolvedStruct;
    return structType;
  }

  private static MlirFunction<MaxonOp> ResolveCallee(string calleeName, Dictionary<string, MlirFunction<MaxonOp>> funcLookup) {
    if (funcLookup.TryGetValue(calleeName, out var calleeFunc))
      return calleeFunc;
    var suffixPattern = $".{calleeName}";
    var found = funcLookup.Values.FirstOrDefault(f => f.Name.EndsWith(suffixPattern));
    if (found != null) return found;
    throw new InvalidOperationException($"Function '{calleeName}' not found in module");
  }

  /// <summary>
  /// Resolve the canonical struct type for a function return type.
  /// Function return types may reference stale stub types from pre-scanning;
  /// this resolves to the full type definition from module.TypeDefs.
  /// </summary>
  private static MlirStructType? ResolveStructReturnType(MlirType? returnType, Dictionary<string, MlirType> typeDefs) {
    if (returnType is not MlirStructType retStruct) return null;
    if (typeDefs.TryGetValue(retStruct.Name, out var canonical) && canonical is MlirStructType canonicalStruct) {
      return canonicalStruct;
    }
    return retStruct;
  }

  /// <summary>
  /// Finds array literal struct ops whose buffer must be heap-allocated because
  /// the value escapes the function (via return or call argument).
  /// </summary>
  private static HashSet<int> FindEscapingArrayLiterals(MlirFunction<MaxonOp> func) {
    var escaping = new HashSet<int>();

    // Map: value ID -> struct literal op (for array literals only)
    var arrayLiterals = new Dictionary<int, MaxonStructLiteralOp>();
    // Map: variable name -> original array literal ID
    var varToArrayLiteral = new Dictionary<string, int>();

    // Array literal buffers escape when returned from the function, since the
    // stack frame is destroyed. Call arguments are safe because the callee
    // runs while the caller's stack is still alive.
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonStructLiteralOp structLit && structLit.ArrayLiteralTag != null) {
          arrayLiterals[structLit.Result.Id] = structLit;
        }
        if (op is MaxonAssignOp assign && arrayLiterals.ContainsKey(assign.Value.Id)) {
          varToArrayLiteral[assign.VarName] = assign.Value.Id;
        }
        if (op is MaxonReturnOp ret && ret.Value != null && arrayLiterals.ContainsKey(ret.Value.Id)) {
          escaping.Add(ret.Value.Id);
        }
        // Global stores escape: the function's stack frame is destroyed but the global persists
        if (op is MaxonGlobalStoreOp globalStore && arrayLiterals.ContainsKey(globalStore.Value.Id)) {
          escaping.Add(globalStore.Value.Id);
        }
        // Track struct_var_ref so indirect returns (var x = [...]; return x) are caught
        if (op is MaxonStructVarRefOp varRef && varToArrayLiteral.TryGetValue(varRef.VarName, out var litId)) {
          arrayLiterals.TryAdd(varRef.Result.Id, arrayLiterals[litId]);
        }
      }
    }

    // Second pass: catch returns and global stores that reference var refs of array literals
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonReturnOp ret && ret.Value != null && arrayLiterals.TryGetValue(ret.Value.Id, out MaxonStructLiteralOp? retLit)) {
          escaping.Add(retLit.Result.Id);
        }
        if (op is MaxonGlobalStoreOp gs && arrayLiterals.TryGetValue(gs.Value.Id, out MaxonStructLiteralOp? gsLit)) {
          escaping.Add(gsLit.Result.Id);
        }
      }
    }

    return escaping;
  }

  /// <summary>
  /// Resolve the standard-level result value type for a call or try_call.
  /// </summary>
  private static StdValue? ResolveCallResultType(MaxonValueKind? resultKind, MlirType? calleeReturnType) {
    if (resultKind == MaxonValueKind.Enum && calleeReturnType is MlirUnionType retEnumType) {
      var backingType = ResolveEnumBackingMlirType(retEnumType);
      if (backingType == MlirType.F64) return new StdF64(MlirContext.Current.NextId());
      if (backingType == MlirType.F32) return new StdF32(MlirContext.Current.NextId());
      return new StdI64(MlirContext.Current.NextId());
    }
    // Match the callee's actual return width so narrow returns skip the I64 round-trip
    if (resultKind == MaxonValueKind.Integer && calleeReturnType != null)
      return StdValueFactory.CreateStdValueForType(calleeReturnType);
    return resultKind?.CreateStdValue();
  }

  private static StdF64 PromoteToF64(StdValue value, MlirBlock<StandardOp> block) {
    if (value is StdF64 f64) {
      return f64;
    } else if (value is StdF32 f32) {
      var conv = new StdF32ToF64Op(f32);
      block.AddOp(conv);
      return conv.Result;
    } else if (value is StdI64 i64) {
      var conv = new StdSiToFpOp(i64);
      block.AddOp(conv);
      return conv.Result;
    } else {
      throw new InvalidOperationException($"Cannot promote {value.GetType().Name} to F64");
    }
  }

  private static void LowerUnaryFloat(
    Dictionary<MaxonValue, StdValue> valueMap,
    MlirBlock<StandardOp> block,
    MaxonValue maxonInput, MaxonValue maxonResult,
    Func<StdF32, StdUnaryF32Op> f32Factory,
    Func<StdF64, StdUnaryF64Op> f64Factory) {
    var input = valueMap[maxonInput];
    if (input is StdF32 f32Input) {
      var op = f32Factory(f32Input);
      block.AddOp(op);
      valueMap[maxonResult] = op.Result;
    } else if (input is StdF64 or StdI64) {
      var op = f64Factory(PromoteToF64(input, block));
      block.AddOp(op);
      valueMap[maxonResult] = op.Result;
    } else {
      throw new InvalidOperationException($"LowerUnaryFloat: unexpected input type {input.GetType().Name}");
    }
  }

  private static void LowerBinaryFloat(
    Dictionary<MaxonValue, StdValue> valueMap,
    MlirBlock<StandardOp> block,
    MaxonValue maxonLhs, MaxonValue maxonRhs, MaxonValue maxonResult,
    Func<StdF32, StdF32, StdBinaryF32Op> f32Factory,
    Func<StdF64, StdF64, StdBinaryF64Op> f64Factory) {
    var lhs = valueMap[maxonLhs];
    var rhs = valueMap[maxonRhs];
    if (lhs is StdF32 f32Lhs && rhs is StdF32 f32Rhs) {
      var op = f32Factory(f32Lhs, f32Rhs);
      block.AddOp(op);
      valueMap[maxonResult] = op.Result;
    } else if (lhs is StdF64 or StdI64 || rhs is StdF64 or StdI64) {
      var op = f64Factory(PromoteToF64(lhs, block), PromoteToF64(rhs, block));
      block.AddOp(op);
      valueMap[maxonResult] = op.Result;
    } else {
      throw new InvalidOperationException($"LowerBinaryFloat: unexpected input types {lhs.GetType().Name}, {rhs.GetType().Name}");
    }
  }

  private static void EmitStore(MlirBlock<StandardOp> block, StdValue value, string varName, Dictionary<string, string> varTypes) {
    switch (value) {
      case StdI64 i64:
        block.AddOp(new StdStoreI64Op(i64, varName));
        varTypes[varName] = "i64";
        break;
      case StdI32 i32:
        block.AddOp(new StdStoreI32Op(i32, varName));
        varTypes[varName] = "i32";
        break;
      case StdF64 f64:
        block.AddOp(new StdStoreF64Op(f64, varName));
        varTypes[varName] = "f64";
        break;
      case StdF32 f32:
        block.AddOp(new StdStoreF32Op(f32, varName));
        varTypes[varName] = "f32";
        break;
      case StdBool b:
        block.AddOp(new StdStoreI1Op(b, varName));
        varTypes[varName] = "i1";
        break;
      case StdPtr ptr:
        // Function pointers are stored as 64-bit values
        block.AddOp(new StdStorePtrOp(ptr, varName));
        varTypes[varName] = "ptr";
        break;
      default:
        throw new InvalidOperationException($"Unsupported StdValue type for store: {value.GetType().Name}");
    }
  }

  private static StdI64 EmitAlloc(MlirBlock<StandardOp> block, StdI64 size) {
    var tagOp = new StdConstI64Op(0); // NULL tag
    block.AddOp(tagOp);
    var result = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("mm_alloc", [size, tagOp.Result], result));
    return result;
  }

  private static StdI64 EmitAlloc(MlirBlock<StandardOp> block, long constSize) {
    var sizeOp = new StdConstI64Op(constSize);
    block.AddOp(sizeOp);
    return EmitAlloc(block, sizeOp.Result);
  }

  /// <summary>
  /// Allocates memory as a child of a parent allocation. When the parent is freed
  /// via mm_free or mm_scope_exit, children are freed recursively.
  /// </summary>
  private static StdI64 EmitAllocIn(MlirBlock<StandardOp> block, StdI64 size, StdI64 parentPtr) {
    var tagOp = new StdConstI64Op(0); // NULL tag
    block.AddOp(tagOp);
    var result = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("mm_alloc_in", [size, parentPtr, tagOp.Result], result));
    return result;
  }

  private static StdI64 EmitAllocIn(MlirBlock<StandardOp> block, long constSize, StdI64 parentPtr) {
    var sizeOp = new StdConstI64Op(constSize);
    block.AddOp(sizeOp);
    return EmitAllocIn(block, sizeOp.Result, parentPtr);
  }

  /// <summary>
  /// Moves a heap allocation to be a child of newParent (mm_move with mode=1).
  /// Used when storing a struct into an array element or struct field.
  /// </summary>
  private static void EmitReparent(MlirBlock<StandardOp> block, StdI64 childPtr, string parentVarName, Dictionary<string, string> varTypes) {
    var parentPtr = (StdI64)EmitLoad(block, parentVarName, varTypes);
    var modeOp = new StdConstI64Op(1);
    block.AddOp(modeOp);
    block.AddOp(new StdCallRuntimeOp("mm_move", [childPtr, parentPtr, modeOp.Result], null));
  }

  /// <summary>
  /// Allocates a struct on the stack by creating contiguous field variables and returning
  /// a LEA address to the base. Uses the same convention as array literal elements.
  /// </summary>
  private static StdI64 EmitStackAlloc(MlirBlock<StandardOp> block, int sizeInBytes) {
    var tag = $"__stk_{MlirContext.Current.NextId()}";
    int fieldCount = sizeInBytes / 8;
    block.AddOp(new StdBulkZeroOp(tag, fieldCount));
    var leaOp = new StdLeaOp(tag);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);
    return ptrOp.Result;
  }

  // __ManagedMemory field offsets (all fields are 8 bytes)
  private const int ManagedFieldBuffer = 0;
  private const int ManagedFieldLength = 8;
  private const int ManagedFieldCapacity = 16;
  private const int ManagedFieldElementSize = 24;

  /// Load a field from a heap-allocated struct. Loads the struct's heap pointer from
  /// its variable, then reads the field at the given offset.
  private static StdValue EmitStructFieldLoad(
    MlirBlock<StandardOp> block, string structVarName, int fieldOffset,
    MlirType fieldType, Dictionary<string, string> varTypes) {
    var heapPtr = EmitLoad(block, structVarName, varTypes);
    var loadOp = new StdLoadIndirectOp(heapPtr, fieldOffset, fieldType);
    block.AddOp(loadOp);
    return loadOp.Result;
  }

  /// Store a value into a field of a heap-allocated struct.
  private static void EmitStructFieldStore(
    MlirBlock<StandardOp> block, StdValue value, string structVarName,
    int fieldOffset, MlirType fieldType, Dictionary<string, string> varTypes) {
    var heapPtr = EmitLoad(block, structVarName, varTypes);
    block.AddOp(new StdStoreIndirectOp(value, heapPtr, fieldOffset, fieldType));
  }

  private static StdValue EmitLoad(MlirBlock<StandardOp> block, string varName, Dictionary<string, string> varTypes) {
    var varTypeName = varTypes[varName];
    switch (varTypeName) {
      case "i64": {
        var loadOp = new StdLoadI64Op(varName);
        block.AddOp(loadOp);
        return loadOp.Result;
      }
      case "f64": {
        var loadOp = new StdLoadF64Op(varName);
        block.AddOp(loadOp);
        return loadOp.Result;
      }
      case "f32": {
        var loadOp = new StdLoadF32Op(varName);
        block.AddOp(loadOp);
        return loadOp.Result;
      }
      case "i1": {
        var loadOp = new StdLoadI1Op(varName);
        block.AddOp(loadOp);
        return loadOp.Result;
      }
      case "i32": {
        var loadOp = new StdLoadI32Op(varName);
        block.AddOp(loadOp);
        return loadOp.Result;
      }
      case "ptr": {
        var loadOp = new StdLoadPtrOp(varName);
        block.AddOp(loadOp);
        return loadOp.Result;
      }
      default:
        throw new InvalidOperationException($"Unsupported var type for load: {varTypeName}");
    }
  }

  /// Converts a varTypes string key (e.g. "i64", "f64") to an MlirType for StdStoreIndirectOp.
  private static MlirType VarTypeToMlirType(string varType) => varType switch {
    "i64" => MlirType.I64,
    "f64" => MlirType.F64,
    "f32" => MlirType.F32,
    "i1" => MlirType.I1,
    "i32" => MlirType.I32,
    "ptr" => MlirType.I64,
    _ => throw new InvalidOperationException($"Unsupported var type for MlirType conversion: {varType}"),
  };

  private static bool IsStructInstanceMethod<T>(MlirFunction<T> func) where T : IPrintableOp =>
    func.ParamNames.Count > 0
    && func.ParamNames[0] == "self"
    && func.ParamTypes[0] is MlirStructType;

  private static bool IsSelfField(bool isStructInstanceMethod, MlirStructType? selfStructType, string name) =>
    isStructInstanceMethod && selfStructType != null && selfStructType.GetField(name) != null;

  private static bool IsEnumInstanceMethod<T>(MlirFunction<T> func) where T : IPrintableOp =>
    func.ParamNames.Count > 0
    && func.ParamNames[0] == "self"
    && func.ParamTypes[0] is MlirUnionType;

  /// <summary>
  /// Counts the flat (leaf) fields in a type — 1 for scalars, recursive sum for structs.
  /// </summary>
  private static int CountFlatFields(MlirType type, Dictionary<string, MlirType> typeDefs) {
    if (type is MlirStructType structType) {
      return structType.Fields.Sum(f => CountFlatFields(f.Type, typeDefs));
    }
    return 1;
  }

  /// <summary>
  /// Calculates the max number of flat payload slots needed across all enum cases.
  /// Struct-typed associated values expand to their flat field count.
  /// </summary>
  private static int GetMaxFlatPayloadSlots(MlirUnionType enumType, Dictionary<string, MlirType> typeDefs) {
    int max = 0;
    foreach (var c in enumType.Cases) {
      if (c.AssociatedValues == null) continue;
      int caseSlots = c.AssociatedValues.Sum(av => CountFlatFields(av.Type, typeDefs));
      if (caseSlots > max) max = caseSlots;
    }
    return max;
  }

  /// <summary>
  /// Calculates the flat slot offset for a given payload index within an enum case.
  /// For example, if payload_0 is a struct with 2 fields, then payload_1 starts at slot 2.
  /// Uses the first case that has enough associated values to determine the types.
  /// </summary>
  private static int GetFlatSlotOffset(MlirUnionType enumType, int payloadIndex, Dictionary<string, MlirType> typeDefs) {
    // Find a case that has this payload index to determine preceding types
    foreach (var c in enumType.Cases) {
      if (c.AssociatedValues == null || payloadIndex >= c.AssociatedValues.Count) continue;
      int offset = 0;
      for (int i = 0; i < payloadIndex; i++) {
        offset += CountFlatFields(c.AssociatedValues[i].Type, typeDefs);
      }
      return offset;
    }
    return payloadIndex; // fallback
  }

  /// Unpack an associated-value enum's heap pointer into flat __tag/__payload_N variables.
  /// Downstream code accesses enum values through these flat variables rather than indirection.
  private static void UnpackEnumHeapToFlatVars(
    MlirBlock<StandardOp> block, string varName, MlirUnionType enumType,
    Dictionary<string, string> varTypes, Dictionary<string, MlirType> typeDefs) {
    var tagLoaded = EmitStructFieldLoad(block, varName, 0, MlirType.I64, varTypes);
    EmitStore(block, tagLoaded, $"{varName}.__tag", varTypes);
    int maxPayload = GetMaxFlatPayloadSlots(enumType, typeDefs);
    for (int pi = 0; pi < maxPayload; pi++) {
      var payloadLoaded = EmitStructFieldLoad(block, varName, 8 + pi * 8, MlirType.I64, varTypes);
      EmitStore(block, payloadLoaded, $"{varName}.__payload_{pi}", varTypes);
    }
  }

  /// Pack flat __tag/__payload_N variables into a new heap-allocated block for storage in struct fields.
  private static StdI64 PackEnumFlatVarsToHeap(
    MlirBlock<StandardOp> block, string varName, MlirUnionType enumType,
    Dictionary<string, string> varTypes, Dictionary<string, MlirType> typeDefs) {
    int maxPayload = GetMaxFlatPayloadSlots(enumType, typeDefs);
    int heapSize = 8 + maxPayload * 8;
    var heapPtr = EmitAlloc(block, heapSize);
    var tagVal = EmitLoad(block, $"{varName}.__tag", varTypes);
    block.AddOp(new StdStoreIndirectOp(tagVal, heapPtr, 0, MlirType.I64));
    for (int pi = 0; pi < maxPayload; pi++) {
      var payloadVal = EmitLoad(block, $"{varName}.__payload_{pi}", varTypes);
      block.AddOp(new StdStoreIndirectOp(payloadVal, heapPtr, 8 + pi * 8, MlirType.I64));
    }
    return heapPtr;
  }
}
