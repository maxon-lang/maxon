using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Passes;

namespace MaxonSharp.Compiler.Ir.Conversion;

/// <summary>
/// Describes a type destructor function to be generated: for each managed field at a known
/// offset, the destructor loads the field pointer and calls mm_decref (null-guarded).
/// </summary>
internal record DestructorRequest(
    string TypeName,
    List<(int Offset, string FieldTypeName, bool IsRawBuffer)> ManagedFields,
    string? ManagedListClearFunc = null,
    bool NeedsManagedElementCleanup = false);

public static partial class MaxonToStandardConversion {
  /// <summary>
  /// Re-resolves a struct type from typeDefs if the captured instance has no fields
  /// (forward-referenced types captured before their fields were parsed).
  /// </summary>
  private static IrStructType ResolveStructType(IrStructType structType, Dictionary<string, IrType> typeDefs) {
    if (structType.Fields.Count > 0) return structType;
    if (typeDefs.TryGetValue(structType.Name, out var resolved) && resolved is IrStructType resolvedStruct && resolvedStruct.Fields.Count > 0)
      return resolvedStruct;
    return structType;
  }

  private static IrFunction<MaxonOp> ResolveCallee(string calleeName, Dictionary<string, IrFunction<MaxonOp>> funcLookup) {
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
  ///
  /// When the declared return type is an interface, the parser has already
  /// inferred the concrete implementing struct and stored it on the call's
  /// result MaxonStruct (resultTypeName). In that case we resolve the
  /// concrete type from typeDefs so downstream lowering treats the call like
  /// any other struct-returning call (heap pointer carried as I64).
  /// </summary>
  private static IrStructType? ResolveStructReturnType(IrType? returnType, Dictionary<string, IrType> typeDefs, string? resultTypeName = null) {
    if (returnType is IrStructType retStruct) {
      if (typeDefs.TryGetValue(retStruct.Name, out var canonical) && canonical is IrStructType canonicalStruct) {
        return canonicalStruct;
      }
      return retStruct;
    }
    if (returnType is IrInterfaceType && resultTypeName != null
        && typeDefs.TryGetValue(resultTypeName, out var resolved) && resolved is IrStructType resolvedStruct) {
      return resolvedStruct;
    }
    return null;
  }

  /// <summary>
  /// Resolve the standard-level result value type for a call or try_call.
  /// </summary>
  private static StdValue? ResolveCallResultType(MaxonValueKind? resultKind, IrType? calleeReturnType) {
    if (resultKind == MaxonValueKind.Enum && calleeReturnType is IrEnumType retEnumType) {
      var backingType = ResolveEnumBackingIrType(retEnumType);
      if (backingType == IrType.F64) return new StdF64(IrContext.Current.NextId());
      if (backingType == IrType.F32) return new StdF32(IrContext.Current.NextId());
      return new StdI64(IrContext.Current.NextId());
    }
    // Match the callee's actual return width so narrow returns skip the I64 round-trip
    if (resultKind == MaxonValueKind.Integer && calleeReturnType != null
        && calleeReturnType is not IrTypeParameterType)
      return StdValueFactory.CreateStdValueForType(calleeReturnType);
    return resultKind?.CreateStdValue();
  }

  private static StdF64 PromoteToF64(StdValue value, IrBlock<StandardOp> block) {
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

  private static IrType ResolveSizeofType(string typeName, IrModule<MaxonOp> module) {
    if (module.TypeDefs.TryGetValue(typeName, out var t)) return t;
    return typeName switch {
      "i1" => IrType.I1,
      "i8" => IrType.I8,
      "i16" => IrType.I16,
      "i32" => IrType.I32,
      "i64" => IrType.I64,
      "u8" => IrType.U8,
      "u16" => IrType.U16,
      "u32" => IrType.U32,
      "u64" => IrType.U64,
      "f32" => IrType.F32,
      "f64" => IrType.F64,
      _ => throw new InvalidOperationException($"sizeof: unknown type '{typeName}'"),
    };
  }

  private static void LowerUnaryFloat(
    Dictionary<MaxonValue, StdValue> valueMap,
    IrBlock<StandardOp> block,
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
    IrBlock<StandardOp> block,
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

  private static void EmitStore(IrBlock<StandardOp> block, StdValue value, string varName, Dictionary<string, string> varTypes) {
    switch (value) {
      case StdHeapPtr hp:
        block.AddOp(new StdStoreI64Op(hp, varName));
        varTypes[varName] = "i64";
        _varNameToStructType![varName] = hp.TypeName;
        break;
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
  [ThreadStatic] private static Dictionary<string, string>? _varNameToStructType;
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
  private static void EmitTagTable(IrModule<StandardOp> result) {
    if (_tagIndexMap == null || _tagIndexMap.Count == 0) return;
    var maxIndex = _nextTagIndex;
    var orderedLabels = new string?[maxIndex];
    var orderedNames = new string?[maxIndex];
    foreach (var (tag, idx) in _tagIndexMap) {
      orderedLabels[idx] = SanitizeTagLabel(tag);
      orderedNames[idx] = tag;
    }
    result.TagTable = [.. orderedLabels];
    result.TagNames = [.. orderedNames];
  }

  /// Returns a tag pointer for memory manager calls. When --mm-trace is enabled,
  /// emits a symdata C string and returns its address; otherwise returns NULL (0).
  private static StdI64 EmitTagPtr(IrBlock<StandardOp> block, string tag) {
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

    // Enum types with associated values that have heap-allocated payloads need destructors
    if (typeDef is IrEnumType enumType && enumType.HasAssociatedValues) {
      foreach (var c in enumType.Cases) {
        if (c.AssociatedValues == null) continue;
        foreach (var (_, avType) in c.AssociatedValues) {
          if (avType.IsHeapAllocated) return $"__destruct_{typeName}";
        }
      }
      return null;
    }

    if (typeDef is not IrStructType structType) return null;

    // __ManagedSocket has a hand-written runtime destructor that calls closesocket
    if (typeName == "__ManagedSocket") return "__destruct___ManagedSocket";

    // __ManagedFile has a hand-written runtime destructor that calls CloseHandle
    if (typeName == "__ManagedFile") return "__destruct___ManagedFile";

    // __ManagedDirectory has a hand-written runtime destructor that calls FindClose and frees the block
    if (typeName == "__ManagedDirectory") return "__destruct___ManagedDirectory";

    // __ManagedMemoryCursor types need a destructor to decref their source_ptr
    if (_resultModule?.TypeAliasSources is { } cursorAliasSources
        && TypeAliasInfo.IsManagedCursorType(typeName, cursorAliasSources))
      return $"__destruct_{typeName}";

    // __ManagedMemory types need a destructor to free their raw buffer
    if (_resultModule?.TypeAliasSources is { } aliasSources
        && TypeAliasInfo.IsManagedMemoryType(typeName, aliasSources))
      return $"__destruct_{typeName}";

    // __ManagedList types need a destructor to clear nodes (and decref values if managed)
    if (_resultModule?.TypeAliasSources is { } managedListAliasSources
        && TypeAliasInfo.IsManagedListType(typeName, managedListAliasSources))
      return $"__destruct_{typeName}";

    var resolved = ResolveStructType(structType, typeDefs);
    foreach (var f in resolved.Fields) {
      if (IsFieldHeapAllocated(f, typeDefs)) return $"__destruct_{typeName}";
    }
    return null;
  }

  /// <summary>
  /// Checks if a struct field holds a heap-allocated type, resolving through typeDefs
  /// when the field's own type object is a stale copy (e.g. tuple fields created before
  /// the enum type's associated value cases were populated).
  /// </summary>
  private static bool IsFieldHeapAllocated(IrStructField field, Dictionary<string, IrType> typeDefs) {
    if (field.Type.IsHeapAllocated) return true;
    return typeDefs.TryGetValue(field.Type.Name, out var resolved) && resolved.IsHeapAllocated;
  }

  /// <summary>
  /// Emits the destructor function pointer as an I64 value: either the address of
  /// __destruct_{TypeName} for types with managed fields, or 0 for types without.
  /// </summary>
  private static StdI64 EmitDestructorPtr(IrBlock<StandardOp> block, string? typeName) {
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

  private static StdI64 EmitAlloc(IrBlock<StandardOp> block, StdI64 size, string? typeName, string? tag = null, string? scopeName = null) {
    if (typeName != null) RegisterTypeForDestructor(typeName);
    var destructorPtr = EmitDestructorPtr(block, typeName);
    var effectiveTag = tag ?? typeName;
    int tagIndex = effectiveTag != null ? EnsureTagIndex(effectiveTag) : 0;
    var tagIndexOp = new StdConstI64Op(tagIndex);
    block.AddOp(tagIndexOp);
    var result = typeName != null
        ? (StdI64)new StdHeapPtr(IrContext.Current.NextId(), typeName)
        : new StdI64(IrContext.Current.NextId());
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_alloc", [size, destructorPtr, tagIndexOp.Result, scopePtr], result));
    } else {
      block.AddOp(new StdCallRuntimeOp("mm_alloc", [size, destructorPtr, tagIndexOp.Result], result));
    }
    return result;
  }

  private static StdI64 EmitAlloc(IrBlock<StandardOp> block, long constSize, string? typeName, string? tag = null, string? scopeName = null) {
    var sizeOp = new StdConstI64Op(constSize);
    block.AddOp(sizeOp);
    return EmitAlloc(block, sizeOp.Result, typeName, tag, scopeName);
  }

  /// <summary>
  /// Raw buffer allocation via mm_raw_alloc (no refcount header).
  /// <paramref name="label"/> names what is being allocated (e.g. "ManagedMemory.buf").
  /// <paramref name="scopeName"/> is the enclosing function name.
  /// Trace output: "raw_alloc label size=N [scope]"
  /// </summary>
  private static StdI64 EmitRawAlloc(IrBlock<StandardOp> block, StdI64 size, string? label = null, string? scopeName = null) {
    var result = new StdI64(IrContext.Current.NextId());
    if (Compiler.MmTrace) {
      var traceLabel = label != null
        ? (scopeName != null ? $"{label} [{scopeName}]" : label)
        : scopeName;
      var scopePtr = traceLabel != null ? EmitTagPtr(block, traceLabel) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_raw_alloc", [size, scopePtr], result));
    } else {
      block.AddOp(new StdCallRuntimeOp("mm_raw_alloc", [size], result));
    }
    return result;
  }

  /// <summary>Emit mm_incref(heap_ptr) — increments reference count for a scope-owned allocation. Trace is built into mm_incref.</summary>
  private static void EmitIncref(IrBlock<StandardOp> block, string varName, Dictionary<string, string> varTypes, string? scopeName = null) {
    var heapPtr = EmitLoad(block, varName, varTypes);
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr, scopePtr], null));
    } else {
      block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr], null));
    }
  }

  /// <summary>Emit mm_trace_transfer — records ownership transfer to caller (trace-only, no runtime effect).</summary>
  private static void EmitTransfer(IrBlock<StandardOp> block, string varName, Dictionary<string, string> varTypes, string scopeName) {
    if (Compiler.MmTrace) {
      var transferPtr = EmitLoad(block, varName, varTypes);
      var scopePtr = EmitTagPtr(block, scopeName);
      block.AddOp(new StdCallRuntimeOp("mm_trace_transfer", [transferPtr, scopePtr], null));
    }
  }

  /// <summary>Emit mm_incref on a raw heap pointer (StdI64). Used when the pointer is already loaded. Trace is built into mm_incref.</summary>
  private static void EmitIncrefValue(IrBlock<StandardOp> block, StdI64 heapPtr, string? scopeName = null) {
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr, scopePtr], null));
    } else {
      block.AddOp(new StdCallRuntimeOp("mm_incref", [heapPtr], null));
    }
  }

  /// <summary>Emit null-guarded mm_decref: skips if pointer is null. Trace is built into mm_decref.</summary>
  private static void EmitDecrefValueIfNonnull(IrBlock<StandardOp> block, StdI64 heapPtr, string? scopeName = null) {
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_decref", [heapPtr, scopePtr], null));
    } else {
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_decref", [heapPtr], null));
    }
  }

  /// <summary>Emit null-guarded mm_incref: skips if pointer is null. Trace is built into mm_incref.</summary>
  private static void EmitIncrefValueIfNonnull(IrBlock<StandardOp> block, StdI64 heapPtr, string? scopeName = null) {
    if (Compiler.MmTrace) {
      var scopePtr = scopeName != null ? EmitTagPtr(block, scopeName) : EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_incref", [heapPtr, scopePtr], null));
    } else {
      block.AddOp(new StdCallRuntimeIfNonnullOp("mm_incref", [heapPtr], null));
    }
  }

  private static StdI64 EmitNullPtr(IrBlock<StandardOp> block) {
    var op = new StdConstI64Op(0);
    block.AddOp(op);
    return op.Result;
  }

  // __ManagedMemory field offsets (all fields are 8 bytes)
  private const int ManagedFieldBuffer = 0;
  private const int ManagedFieldLength = 8;
  private const int ManagedFieldCapacity = 16;
  private const int ManagedFieldElementSize = 24;
  private const int ManagedFieldParentPtr = 32;
  private const int ManagedMemoryStructSize = 40;

  // __ManagedMemoryCursor struct field offsets (all fields are 8 bytes)
  private const int CursorFieldBuffer = 0;
  private const int CursorFieldPosition = 8;
  private const int CursorFieldLength = 16;
  private const int CursorFieldElementSize = 24;
  private const int CursorFieldSourcePtr = 32;
  private const int CursorStructSize = 40;

  // String struct field offsets (all fields are 8 bytes)
  private const int StringFieldManaged = 0;
  private const int StringFieldGraphemeCount = 8;
  private const int StringFieldIsAscii = 16;
  private const int StringStructSize = 24;
  private const int CharacterStructSize = 8;

  /// Store all five fields of a __ManagedMemory struct.
  private static void EmitInitManagedMemory(
    IrBlock<StandardOp> block, string managedVarName,
    StdI64 buffer, StdI64 length, StdI64 capacity, StdI64 elementSize, StdI64 parentPtr,
    Dictionary<string, string> varTypes) {
    EmitStructFieldStore(block, buffer, managedVarName, ManagedFieldBuffer, IrType.I64, varTypes);
    EmitStructFieldStore(block, length, managedVarName, ManagedFieldLength, IrType.I64, varTypes);
    EmitStructFieldStore(block, capacity, managedVarName, ManagedFieldCapacity, IrType.I64, varTypes);
    EmitStructFieldStore(block, elementSize, managedVarName, ManagedFieldElementSize, IrType.I64, varTypes);
    EmitStructFieldStore(block, parentPtr, managedVarName, ManagedFieldParentPtr, IrType.I64, varTypes);
  }

  /// Load a field from a heap-allocated struct. Loads the struct's heap pointer from
  /// its variable, then reads the field at the given offset.
  private static StdValue EmitStructFieldLoad(
    IrBlock<StandardOp> block, string structVarName, int fieldOffset,
    IrType fieldType, Dictionary<string, string> varTypes) {
    var heapPtr = EmitLoad(block, structVarName, varTypes);
    var loadOp = new StdLoadIndirectOp(heapPtr, fieldOffset, fieldType);
    block.AddOp(loadOp);
    return loadOp.Result;
  }

  /// Store a value into a field of a heap-allocated struct.
  private static void EmitStructFieldStore(
    IrBlock<StandardOp> block, StdValue value, string structVarName,
    int fieldOffset, IrType fieldType, Dictionary<string, string> varTypes) {
    var heapPtr = EmitLoad(block, structVarName, varTypes);
    block.AddOp(new StdStoreIndirectOp(value, heapPtr, fieldOffset, fieldType));
  }

  /// Emit select chains for struct-backed enum raw value fields, recursing into nested struct fields.
  private static void EmitStructRawValueFields(
      IrBlock<StandardOp> block, IrStructType structType, IrEnumType enumType,
      StdI64 ordinalValue, string parentVarName, string fieldPrefix,
      VarRegistry temps, Dictionary<string, string> varTypes, string scopeName,
      Dictionary<string, IrType> typeDefs) {
    foreach (var field in structType.Fields) {
      var qualifiedName = fieldPrefix.Length > 0 ? $"{fieldPrefix}.{field.Name}" : field.Name;

      if (field.Type is IrStructType nestedStructType) {
        // Resolve the struct type in case it was forward-declared with no fields
        var resolved = typeDefs.TryGetValue(nestedStructType.Name, out var td) && td is IrStructType rst
          ? rst : nestedStructType;

        // Allocate nested struct, populate its fields recursively, then store pointer in parent
        var nestedTempName = temps.CreateTemp("nested_rawval", IrContext.Current.NextId(), resolved.Name, OwnershipFlags.Borrowed);
        var nestedPtr = EmitAlloc(block, resolved.SizeInBytes, resolved.Name, scopeName: scopeName);
        EmitStore(block, nestedPtr, nestedTempName, varTypes);

        EmitStructRawValueFields(block, resolved, enumType, ordinalValue,
          nestedTempName, qualifiedName, temps, varTypes, scopeName, typeDefs);

        // Store nested struct pointer into parent and incref (parent holds a reference)
        var nestedHeapPtr = (StdI64)EmitLoad(block, nestedTempName, varTypes);
        EmitStructFieldStore(block, nestedHeapPtr, parentVarName, field.Offset, IrType.I64, varTypes);
        EmitIncrefValue(block, nestedHeapPtr, scopeName: scopeName);
      } else {
        // Leaf field: emit select chain mapping ordinal -> field value
        var defaultVal = new StdConstI64Op(0);
        block.AddOp(defaultVal);
        StdI64 currentFieldVal = defaultVal.Result;

        foreach (var enumCase in enumType.Cases) {
          if (enumCase.RawValue is not StructRawValue srv) continue;
          long fieldValue = srv.Fields.First(f => f.FieldName == qualifiedName).Value;

          var ordConst = new StdConstI64Op(enumCase.Ordinal);
          block.AddOp(ordConst);
          var cmpOp = new StdCmpI64Op("eq", ordinalValue, ordConst.Result);
          block.AddOp(cmpOp);

          var fieldConst = new StdConstI64Op(fieldValue);
          block.AddOp(fieldConst);

          var selectOp = new StdSelectI64Op(cmpOp.Result, fieldConst.Result, currentFieldVal);
          block.AddOp(selectOp);
          currentFieldVal = selectOp.Result;
        }

        EmitStructFieldStore(block, currentFieldVal, parentVarName, field.Offset, field.Type, varTypes);
      }
    }
  }

  private static StdValue EmitLoad(IrBlock<StandardOp> block, string varName, Dictionary<string, string> varTypes) {
    var varTypeName = varTypes[varName];
    switch (varTypeName) {
      case "i64": {
        var loadOp = new StdLoadI64Op(varName);
        block.AddOp(loadOp);
        if (_varNameToStructType != null && _varNameToStructType.TryGetValue(varName, out var structType))
          return new StdHeapPtr(loadOp.Result.Id, structType, varName);
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

  /// Converts a varTypes string key (e.g. "i64", "f64") to an IrType for StdStoreIndirectOp.
  private static IrType VarTypeToIrType(string varType) => varType switch {
    "i64" => IrType.I64,
    "f64" => IrType.F64,
    "f32" => IrType.F32,
    "i1" => IrType.I1,
    "i32" => IrType.I32,
    "ptr" => IrType.I64,
    _ => throw new InvalidOperationException($"Unsupported var type for IrType conversion: {varType}"),
  };

  /// <summary>
  /// Returns true if a function still has unresolved type parameters — either in its
  /// signature or in its owning type. Such functions are generic templates that were
  /// monomorphized into concrete specializations and should be skipped during lowering.
  /// </summary>
  private static bool HasUnresolvedTypeParameters(IrFunction<MaxonOp> func, IrModule<MaxonOp> module) {
    static bool hasUnresolved(IrType? t) => t is IrTypeParameterType;
    if (func.ParamTypes.Any(hasUnresolved) || hasUnresolved(func.ReturnType)) {
      return true;
    }
    // Check if the function body contains any unresolvable ops
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonIteratorNextOp) return true;
      }
    }
    // Check if the owning type is a generic source type that has been specialized.
    // Extract type name from function name and check if it's used as a source for type aliases.
    var parts = func.Name.Split('.');
    for (int i = parts.Length - 1; i >= 1; i--) {
      var candidateTypeName = parts[i - 1];
      if (module.TypeDefs.TryGetValue(candidateTypeName, out var ownerType)) {
        bool hasAssocTypes = (ownerType is IrStructType st && st.AssociatedTypeNames.Count > 0)
                          || (ownerType is IrEnumType ut && ut.AssociatedTypeNames.Count > 0);
        if (hasAssocTypes) {
          // Check if this type is used as a source for at least one concrete alias
          bool hasConcreteAlias = module.TypeAliasSources.Values
            .Any(a => a.SourceTypeName == candidateTypeName
                 && a.TypeParams != null
                 && !a.TypeParams.Values.Any(t => t is IrTypeParameterType));
          if (hasConcreteAlias) {
            // Only skip if the function's non-self parameters or return type reference
            // associated types. Functions like Array.resize(self: Array, newLength: i64)
            // don't use the Element type parameter, so they aren't monomorphized and
            // the original generic version must be kept. The self parameter always
            // uses the generic type name and is handled by call-site rewriting.
            var assocNames = ownerType is IrStructType st2 ? st2.AssociatedTypeNames
              : ownerType is IrEnumType ut2 ? ut2.AssociatedTypeNames : [];
            bool nonSelfParamUsesAssocType = false;
            for (int pi = 1; pi < func.ParamTypes.Count; pi++) {
              var pt = func.ParamTypes[pi];
              if (pt is IrTypeParameterType) { nonSelfParamUsesAssocType = true; break; }
              if (pt is IrStructType pst && assocNames.Any(n => pst.Name == n || pst.Name.Contains(n))) { nonSelfParamUsesAssocType = true; break; }
              if (pt is IrEnumType pet && assocNames.Any(n => pet.Name == n || pet.Name.Contains(n))) { nonSelfParamUsesAssocType = true; break; }
            }
            if (!nonSelfParamUsesAssocType) {
              if (func.ReturnType is IrTypeParameterType) nonSelfParamUsesAssocType = true;
              else if (func.ReturnType is IrStructType rst && (rst.Name == "Self" || assocNames.Any(n => rst.Name == n || rst.Name.Contains(n)))) nonSelfParamUsesAssocType = true;
              else if (func.ReturnType is IrEnumType ret && assocNames.Any(n => ret.Name == n || ret.Name.Contains(n))) nonSelfParamUsesAssocType = true;
            }
            if (nonSelfParamUsesAssocType) return true;
          }
        }
      }
    }
    return false;
  }

  private static bool IsStructInstanceMethod<T>(IrFunction<T> func) where T : IPrintableOp =>
    func.ParamNames.Count > 0
    && func.ParamNames[0] == "self"
    && func.ParamTypes[0] is IrStructType;

  private static bool IsSelfField(bool isStructInstanceMethod, IrStructType? selfStructType, string name) =>
    isStructInstanceMethod && selfStructType != null && selfStructType.GetField(name) != null;

  /// Reload struct-typed self-field local variables from the self pointer.
  /// Called after method calls that may mutate self-fields (e.g. grow() reallocating arrays).
  private static void ReloadSelfFieldLocals(IrStructType selfStructType, IrBlock<StandardOp> block, Dictionary<string, string> varTypes, Dictionary<string, string>? selfFieldTempVars = null) {
    foreach (var field in selfStructType.Fields) {
      if (field.Type is not IrStructType) continue;
      if (!varTypes.ContainsKey(field.Name)) continue;
      var reloaded = EmitStructFieldLoad(block, "self", field.Offset, IrType.I64, varTypes);
      EmitStore(block, reloaded, field.Name, varTypes);
      // Also update the entry-block temp var (e.g. __field_1234) that aliases this self-field,
      // so subsequent code using the SSA-derived temp sees the fresh value.
      if (selfFieldTempVars != null && selfFieldTempVars.TryGetValue(field.Name, out var tempName)
          && varTypes.ContainsKey(tempName)) {
        EmitStore(block, reloaded, tempName, varTypes);
      }
    }
  }

  private static bool IsEnumInstanceMethod<T>(IrFunction<T> func) where T : IPrintableOp =>
    func.ParamNames.Count > 0
    && func.ParamNames[0] == "self"
    && func.ParamTypes[0] is IrEnumType;

  /// <summary>
  /// Calculates the max number of payload slots needed across all enum cases.
  /// Each associated value occupies exactly one slot: scalars store their value
  /// directly, structs and associated-value enums store a heap pointer.
  /// </summary>
  private static int GetMaxFlatPayloadSlots(IrEnumType enumType) {
    int max = 0;
    foreach (var c in enumType.Cases) {
      if (c.AssociatedValues == null) continue;
      if (c.AssociatedValues.Count > max) max = c.AssociatedValues.Count;
    }
    return max;
  }

}
