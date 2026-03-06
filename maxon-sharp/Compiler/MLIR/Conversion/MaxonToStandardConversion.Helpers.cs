using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Describes a type destructor function to be generated: for each managed field at a known
/// offset, the destructor loads the field pointer and calls mm_decref (null-guarded).
/// </summary>
internal record DestructorRequest(
    string TypeName,
    List<(int Offset, string FieldTypeName, bool IsRawBuffer)> ManagedFields,
    string? ChainClearFunc = null,
    bool NeedsManagedElementCleanup = false);

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

  /// Converts a tag name to its symdata label form (e.g. "foo.bar" -> "__tag_foo_bar").
  internal static string SanitizeTagLabel(string tag) =>
    $"__tag_{tag.Replace('.', '_').Replace(' ', '_')}";

  /// Registers a symdata C string for the given tag if not already cached. Returns the label.
  [ThreadStatic] private static Dictionary<string, string>? _symdataTagCache;
  [ThreadStatic] private static Dictionary<string, int>? _tagIndexMap;
  [ThreadStatic] private static int _nextTagIndex;
  private static string EnsureSymdataTag(string tag) {
    _symdataTagCache ??= [];
    if (_symdataTagCache.TryGetValue(tag, out var existingLabel))
      return existingLabel;
    var symdataLabel = SanitizeTagLabel(tag);
    var nullTerminated = new byte[System.Text.Encoding.UTF8.GetByteCount(tag) + 1];
    System.Text.Encoding.UTF8.GetBytes(tag, nullTerminated);
    _resultModule!.SymdataEntries.Add((symdataLabel, nullTerminated, 1));
    _symdataTagCache[tag] = symdataLabel;
    return symdataLabel;
  }

  /// Returns the tag index for a type name string, assigning a new index if needed.
  /// Index 0 is reserved for "no tag". Indices start at 1.
  private static int EnsureTagIndex(string tag) {
    _tagIndexMap ??= [];
    if (_tagIndexMap.TryGetValue(tag, out var idx))
      return idx;
    if (_nextTagIndex == 0) _nextTagIndex = 1; // 0 = no tag
    idx = _nextTagIndex++;
    _tagIndexMap[tag] = idx;
    EnsureSymdataTag(tag); // ensure the string exists in symdata
    return idx;
  }

  /// Populates result.TagTable with the ordered symdata labels for each tag index.
  /// Must be called after all lowering is complete.
  private static void EmitTagTable(MlirModule<StandardOp> result) {
    if (_tagIndexMap == null || _tagIndexMap.Count == 0) return;
    var maxIndex = _nextTagIndex;
    var orderedLabels = new string?[maxIndex];
    foreach (var (tag, idx) in _tagIndexMap) {
      orderedLabels[idx] = SanitizeTagLabel(tag);
    }
    result.TagTable = [.. orderedLabels];
  }

  /// Returns a tag pointer for memory manager calls. When --mm-trace is enabled,
  /// emits a symdata C string and returns its address; otherwise returns NULL (0).
  private static StdI64 EmitTagPtr(MlirBlock<StandardOp> block, string tag) {
    if (!Compiler.MmTrace) {
      var nullOp = new StdConstI64Op(0);
      block.AddOp(nullOp);
      return nullOp.Result;
    }
    var symdataLabel = EnsureSymdataTag(tag);
    var leaOp = new StdLeaSymdataOp(symdataLabel);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);
    return ptrOp.Result;
  }

  /// <summary>
  /// Returns the destructor function label for a type, or null if the type has no managed fields.
  /// The destructor label convention is "__destruct_{TypeName}".
  /// </summary>
  private static string? GetDestructorLabelForType(string? typeName) {
    if (typeName == null) return null;
    var typeDefs = _resultModule!.TypeDefs;
    if (!typeDefs.TryGetValue(typeName, out var typeDef)) return null;

    // Union types with associated values that have heap-allocated payloads need destructors
    if (typeDef is MlirUnionType unionType && unionType.HasAssociatedValues) {
      foreach (var c in unionType.Cases) {
        if (c.AssociatedValues == null) continue;
        foreach (var (_, avType) in c.AssociatedValues) {
          if (avType.IsHeapAllocated) return $"__destruct_{typeName}";
        }
      }
      return null;
    }

    if (typeDef is not MlirStructType structType) return null;

    // __ManagedMemory types need a destructor to free their raw buffer
    if (_resultModule?.TypeAliasSources is { } aliasSources
        && TypeAliasInfo.IsManagedMemoryType(typeName, aliasSources))
      return $"__destruct_{typeName}";

    // __Chain types need a destructor to clear nodes (and decref values if managed)
    if (_resultModule?.TypeAliasSources is { } chainAliasSources
        && TypeAliasInfo.IsChainType(typeName, chainAliasSources))
      return $"__destruct_{typeName}";

    var resolved = ResolveStructType(structType, typeDefs);
    foreach (var f in resolved.Fields) {
      if (f.Type.IsHeapAllocated) return $"__destruct_{typeName}";
    }
    return null;
  }

  /// <summary>
  /// Emits the destructor function pointer as an I64 value: either the address of
  /// __destruct_{TypeName} for types with managed fields, or 0 for types without.
  /// </summary>
  private static StdI64 EmitDestructorPtr(MlirBlock<StandardOp> block, string? typeName) {
    var label = GetDestructorLabelForType(typeName);
    if (label == null) {
      var zeroOp = new StdConstI64Op(0);
      block.AddOp(zeroOp);
      return zeroOp.Result;
    }
    var funcRefOp = new StdFuncRefOp(label);
    block.AddOp(funcRefOp);
    var ptrToI64 = new StdPtrToI64Op(funcRefOp.Result);
    block.AddOp(ptrToI64);
    return ptrToI64.Result;
  }

  private static StdI64 EmitAlloc(MlirBlock<StandardOp> block, StdI64 size, string? typeName, string? tag = null, string? scopeName = null) {
    if (typeName != null) RegisterTypeForDestructor(typeName);
    var destructorPtr = EmitDestructorPtr(block, typeName);
    var effectiveTag = tag ?? typeName;
    int tagIndex = effectiveTag != null ? EnsureTagIndex(effectiveTag) : 0;
    var tagIndexOp = new StdConstI64Op(tagIndex);
    block.AddOp(tagIndexOp);
    var result = new StdI64(MlirContext.Current.NextId());
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_alloc", [size, destructorPtr, tagIndexOp.Result, scopePtr], result));
    } else {
      block.AddOp(new StdCallRuntimeOp("mm_alloc", [size, destructorPtr, tagIndexOp.Result], result));
    }
    return result;
  }

  private static StdI64 EmitAlloc(MlirBlock<StandardOp> block, long constSize, string? typeName, string? tag = null, string? scopeName = null) {
    var sizeOp = new StdConstI64Op(constSize);
    block.AddOp(sizeOp);
    return EmitAlloc(block, sizeOp.Result, typeName, tag, scopeName);
  }

  /// <summary>
  /// Raw buffer allocation via HeapAlloc (no refcount header).
  /// Used for buffer allocations inside __ManagedMemory.
  /// </summary>
  private static StdI64 EmitRawAlloc(MlirBlock<StandardOp> block, StdI64 size) {
    var result = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("mm_raw_alloc", [size], result));
    return result;
  }

  /// <summary>Emit mm_incref(heap_ptr) — increments reference count for a scope-owned allocation. Trace is built into mm_incref.</summary>
  private static void EmitIncref(MlirBlock<StandardOp> block, string varName, Dictionary<string, string> varTypes, string? scopeName = null) {
    var heapPtr = EmitLoad(block, varName, varTypes);
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr, scopePtr], null));
    } else {
      block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr], null));
    }
  }

  /// <summary>Emit mm_trace_transfer — records ownership transfer to caller (trace-only, no runtime effect).</summary>
  private static void EmitTransfer(MlirBlock<StandardOp> block, string varName, Dictionary<string, string> varTypes, string scopeName) {
    if (Compiler.MmTrace) {
      var transferPtr = EmitLoad(block, varName, varTypes);
      var scopePtr = EmitTagPtr(block, scopeName);
      block.AddOp(new StdCallRuntimeOp("mm_trace_transfer", [transferPtr, scopePtr], null));
    }
  }

  /// <summary>Emit mm_incref on a raw heap pointer (StdI64). Used when the pointer is already loaded. Trace is built into mm_incref.</summary>
  private static void EmitIncrefValue(MlirBlock<StandardOp> block, StdI64 heapPtr, string? scopeName = null) {
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr, scopePtr], null));
    } else {
      block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr], null));
    }
  }

  /// <summary>Emit null-guarded mm_decref: skips if pointer is null. Trace is built into mm_decref.</summary>
  private static void EmitDecrefValueIfNonnull(MlirBlock<StandardOp> block, StdI64 heapPtr, string? scopeName = null) {
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_decref", [heapPtr, scopePtr], null));
    } else {
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_decref", [heapPtr], null));
    }
  }

  /// <summary>Emit null-guarded mm_incref: skips if pointer is null. Trace is built into mm_incref.</summary>
  private static void EmitIncrefValueIfNonnull(MlirBlock<StandardOp> block, StdI64 heapPtr, string? scopeName = null) {
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_incref", [heapPtr, scopePtr], null));
    } else {
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_incref", [heapPtr], null));
    }
  }

  private static StdI64 EmitNullPtr(MlirBlock<StandardOp> block) {
    var op = new StdConstI64Op(0);
    block.AddOp(op);
    return op.Result;
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
  /// Calculates the max number of payload slots needed across all enum cases.
  /// Each associated value occupies exactly one slot: scalars store their value
  /// directly, structs and associated-value enums store a heap pointer.
  /// </summary>
  private static int GetMaxFlatPayloadSlots(MlirUnionType enumType) {
    int max = 0;
    foreach (var c in enumType.Cases) {
      if (c.AssociatedValues == null) continue;
      if (c.AssociatedValues.Count > max) max = c.AssociatedValues.Count;
    }
    return max;
  }

}
