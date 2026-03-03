using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Passes;

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

  /// Returns a tag pointer for memory manager calls. When --mm-trace is enabled,
  /// emits a symdata C string and returns its address; otherwise returns NULL (0).
  [ThreadStatic] private static Dictionary<string, string>? _symdataTagCache;
  private static StdI64 EmitTagPtr(MlirBlock<StandardOp> block, string tag) {
    if (!Compiler.MmTrace) {
      var nullOp = new StdConstI64Op(0);
      block.AddOp(nullOp);
      return nullOp.Result;
    }
    _symdataTagCache ??= [];
    var symdataLabel = $"__tag_{tag.Replace('.', '_').Replace(' ', '_')}";
    if (!_symdataTagCache.TryGetValue(tag, out var existingLabel)) {
      var nullTerminated = new byte[System.Text.Encoding.UTF8.GetByteCount(tag) + 1];
      System.Text.Encoding.UTF8.GetBytes(tag, nullTerminated);
      _resultModule!.SymdataEntries.Add((symdataLabel, nullTerminated, 1));
      _symdataTagCache[tag] = symdataLabel;
    } else {
      symdataLabel = existingLabel;
    }
    var leaOp = new StdLeaSymdataOp(symdataLabel);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);
    return ptrOp.Result;
  }

  private static StdI64 EmitAlloc(MlirBlock<StandardOp> block, StdI64 size, string? tag = null, string? scopeName = null) {
    StdI64 tagPtr;
    if (tag != null) {
      tagPtr = EmitTagPtr(block, tag);
    } else {
      var tagOp = new StdConstI64Op(0);
      block.AddOp(tagOp);
      tagPtr = tagOp.Result;
    }
    var result = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("mm_alloc", [size, tagPtr], result));
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_trace_alloc", [result, scopePtr], null));
    }
    return result;
  }

  private static StdI64 EmitAlloc(MlirBlock<StandardOp> block, long constSize, string? tag = null, string? scopeName = null) {
    var sizeOp = new StdConstI64Op(constSize);
    block.AddOp(sizeOp);
    return EmitAlloc(block, sizeOp.Result, tag, scopeName);
  }

  /// <summary>
  /// Allocates memory as a child of a parent allocation. When the parent is freed
  /// via mm_free, children are freed recursively.
  /// </summary>
  private static StdI64 EmitAllocIn(MlirBlock<StandardOp> block, StdI64 size, StdI64 parentPtr, string? tag = null) {
    StdI64 tagPtr;
    if (tag != null) {
      tagPtr = EmitTagPtr(block, tag);
    } else {
      var tagOp = new StdConstI64Op(0);
      block.AddOp(tagOp);
      tagPtr = tagOp.Result;
    }
    var result = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("mm_alloc_in", [size, parentPtr, tagPtr], result));
    return result;
  }

  private static StdI64 EmitAllocIn(MlirBlock<StandardOp> block, long constSize, StdI64 parentPtr, string? tag = null) {
    var sizeOp = new StdConstI64Op(constSize);
    block.AddOp(sizeOp);
    return EmitAllocIn(block, sizeOp.Result, parentPtr, tag);
  }

  /// <summary>Emit mm_incref(heap_ptr) — increments reference count for a scope-owned allocation.</summary>
  private static void EmitIncref(MlirBlock<StandardOp> block, string varName, Dictionary<string, string> varTypes, string? scopeName = null) {
    var heapPtr = EmitLoad(block, varName, varTypes);
    block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr], null));
    if (Compiler.MmTrace) {
      var tracePtr = EmitLoad(block, varName, varTypes);
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_trace_incref", [tracePtr, scopePtr], null));
    }
  }

  /// <summary>Emit mm_decref(heap_ptr) — decrements reference count, reclaims ownership when zero.</summary>
  private static void EmitDecref(MlirBlock<StandardOp> block, string varName, Dictionary<string, string> varTypes, string? scopeName = null) {
    var heapPtr = EmitLoad(block, varName, varTypes);
    // Trace BEFORE decref: mm_decref may free the object, invalidating the pointer
    if (Compiler.MmTrace) {
      var tracePtr = EmitLoad(block, varName, varTypes);
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_trace_decref", [tracePtr, scopePtr], null));
    }
    block.AddOp(new StdCallRuntimeOp("mm_decref", [heapPtr], null));
  }

  /// <summary>
  /// Builds recursive FieldDestructorInfo for a list of managed struct fields.
  /// Fields whose types themselves have managed fields get nested destructors.
  /// The visited set breaks cycles for self-referential types (e.g. linked-list nodes):
  /// a cyclic field gets an empty nested list so the runtime mm_decref handles cleanup
  /// via the type's own destructor chain rather than infinitely inlining it.
  /// outerType and outerTypeName are the containing struct — used to detect when a
  /// child-owned __ManagedMemory holds struct element pointers that need per-element
  /// decref before the buffer is freed.
  /// </summary>
  private static List<FieldDestructorInfo> BuildFieldDestructors(
    List<MlirStructField> fields, Dictionary<string, MlirType> typeDefs,
    HashSet<string>? visited = null, MlirStructType? outerType = null,
    string? outerTypeName = null, Dictionary<string, TypeAliasInfo>? typeAliasSources = null) {
    var result = new List<FieldDestructorInfo>();
    foreach (var field in fields) {
      var fieldTypeName = (field.Type as MlirStructType)?.Name;
      bool isCycle = fieldTypeName != null && (visited?.Contains(fieldTypeName) ?? false);
      List<FieldDestructorInfo> nested;
      if (isCycle) {
        nested = [];
      } else {
        var nestedFields = GetManagedFieldsForType(field.Type, typeDefs);
        var nextVisited = visited != null ? new HashSet<string>(visited) : [];
        if (fieldTypeName != null) nextVisited.Add(fieldTypeName);
        nested = BuildFieldDestructors(nestedFields, typeDefs, nextVisited);
      }
      bool isChildOwned = (field.Type as MlirStructType)?.IsChildOwned ?? false;
      // A __ManagedMemory field (or its concrete alias, e.g. "ElementMemory") whose outer type
      // has a struct Element type parameter stores heap pointers in its buffer. Each pointer
      // must be decrefd before mm_decref frees the ElementMemory struct (and its buffer child),
      // because the buffer free alone does not touch element refcounts.
      bool isManagedMemory = fieldTypeName != null
        && typeAliasSources != null
        && TypeAliasInfo.IsManagedMemoryType(fieldTypeName, typeAliasSources);
      bool hasManagedElements = isManagedMemory
        && HasStructElementType(outerType, outerTypeName, typeAliasSources);
      result.Add(new FieldDestructorInfo(field.Offset, field.Type, nested, isChildOwned, hasManagedElements));
    }
    return result;
  }

  /// <summary>
  /// Returns true if the outer type is parameterized with a struct Element type —
  /// meaning its __ManagedMemory buffer holds heap pointers that need per-element decref.
  /// Checks both the concrete struct's TypeParams and the TypeAliasSources table.
  /// </summary>
  private static bool HasStructElementType(
    MlirStructType? outerType, string? outerTypeName,
    Dictionary<string, TypeAliasInfo>? typeAliasSources) {
    // First check the concrete struct's TypeParams (populated during monomorphization)
    if (outerType?.TypeParams.TryGetValue("Element", out var elemType) == true && elemType is MlirStructType)
      return true;
    // Fall back to TypeAliasSources which tracks alias -> source type + type params
    if (outerTypeName != null && typeAliasSources?.TryGetValue(outerTypeName, out var aliasInfo) == true
        && aliasInfo.TypeParams?.TryGetValue("Element", out var aliasElemType) == true
        && aliasElemType is MlirStructType)
      return true;
    return false;
  }

  /// <summary>Returns the heap-allocated fields of the named struct type, in field order.</summary>
  private static List<MlirStructField> GetManagedFields(string typeName, Dictionary<string, MlirType> typeDefs) {
    if (!typeDefs.TryGetValue(typeName, out var typeDef) || typeDef is not MlirStructType structType)
      return [];
    var resolved = ResolveStructType(structType, typeDefs);
    return [.. resolved.Fields.Where(f => f.Type.IsHeapAllocated)];
  }

  private static List<MlirStructField> GetManagedFieldsForType(MlirType type, Dictionary<string, MlirType> typeDefs) {
    if (type is not MlirStructType structType) return [];
    var resolved = ResolveStructType(structType, typeDefs);
    return [.. resolved.Fields.Where(f => f.Type.IsHeapAllocated)];
  }

  /// <summary>Emit mm_incref on a raw heap pointer (StdI64). Used when the pointer is already loaded.</summary>
  private static void EmitIncrefValue(MlirBlock<StandardOp> block, StdI64 heapPtr, string? scopeName = null) {
    block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr], null));
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_trace_incref", [heapPtr, scopePtr], null));
    }
  }

  /// <summary>Emit null-guarded mm_decref: skips if pointer is null.</summary>
  private static void EmitDecrefValueIfNonnull(MlirBlock<StandardOp> block, StdI64 heapPtr, string? scopeName = null) {
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_trace_decref", [heapPtr, scopePtr], null));
    }
    block.AddOp(new StdCallRuntimeIfNonnullOp("mm_decref", [heapPtr], null));
  }

  /// <summary>Emit null-guarded mm_incref: skips if pointer is null.</summary>
  private static void EmitIncrefValueIfNonnull(MlirBlock<StandardOp> block, StdI64 heapPtr, string? scopeName = null) {
    block.AddOp(new StdCallRuntimeIfNonnullOp("mm_incref", [heapPtr], null));
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_trace_incref", [heapPtr, scopePtr], null));
    }
  }

  /// <summary>Emit null-guarded mm_decref on a variable.</summary>
  private static void EmitDecrefIfNonnull(MlirBlock<StandardOp> block, string varName, Dictionary<string, string> varTypes, string? scopeName = null) {
    var heapPtr = EmitLoad(block, varName, varTypes);
    if (Compiler.MmTrace) {
      var tracePtr = EmitLoad(block, varName, varTypes);
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_trace_decref", [tracePtr, scopePtr], null));
    }
    block.AddOp(new StdCallRuntimeIfNonnullOp("mm_decref", [heapPtr], null));
  }

  /// <summary>
  /// Null-guarded version of EmitDecrefWithFieldCleanup. Skips entire destruction if pointer is null.
  /// Used for scope-end cleanup where variables may be zero-initialized on untaken code paths.
  /// </summary>
  private static void EmitDecrefWithFieldCleanupIfNonnull(
    MlirBlock<StandardOp> block, string varName,
    Dictionary<string, string> varTypes, Dictionary<string, string> varNameToStructType,
    Dictionary<string, MlirType> typeDefs, Dictionary<string, TypeAliasInfo>? typeAliasSources = null,
    string? scopeName = null) {
    var typeName = varNameToStructType[varName];
    var managedFields = GetManagedFields(typeName, typeDefs);

    if (managedFields.Count == 0) {
      EmitDecrefIfNonnull(block, varName, varTypes, scopeName);
      return;
    }

    var heapPtr = (StdI64)EmitLoad(block, varName, varTypes);
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_trace_decref", [heapPtr, scopePtr], null));
    }

    var outerType = typeDefs.TryGetValue(typeName, out var t) ? t as MlirStructType : null;
    var fieldInfos = BuildFieldDestructors(managedFields, typeDefs, [typeName], outerType, typeName, typeAliasSources);
    block.AddOp(new StdDestructStructOp(heapPtr, fieldInfos, nullGuarded: true));
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
