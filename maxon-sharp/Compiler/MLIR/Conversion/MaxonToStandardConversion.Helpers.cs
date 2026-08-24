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
    // Under directory-as-module, a call to `String.clone` from any namespace
    // should resolve to `stdlib.String.clone`. Walk the function list for a
    // suffix match — `.{calleeName}` covers cross-namespace calls without
    // accidentally matching a same-namespace function whose name HAPPENS to
    // end with `calleeName` (the leading dot anchors the prefix at a segment
    // boundary).
    var suffixPattern = $".{calleeName}";
    var found = funcLookup.Values.FirstOrDefault(f => f.Name.EndsWith(suffixPattern));
    if (found != null) return found;
    throw new InvalidOperationException($"Function '{calleeName}' not found in module");
  }

  /// <summary>
  /// How many 8-byte slots a stack-allocated <paramref name="structType"/> occupies — the number
  /// `StdBulkZeroOp` reserves, and the number `StackSlotName` indexes backwards from.
  ///
  /// It is a QWORD count, and it is NOT `Fields.Count`. Those agree only while every field occupies
  /// exactly one qword, which is true of every type that reaches here TODAY and is not a property of
  /// the layout: an inline `managed` field is 40 bytes (see `IrStructType.FieldSlotSize`), so a
  /// `String` is 2 fields and 6 qwords. Asking `Fields.Count` gave `StackSlotName` a NEGATIVE index
  /// for String's `singleByteGraphemesFlag` (`2 - 1 - 40/8` = -4) — invalid, and reachable the moment such a type
  /// becomes stack-allocatable. Size is the question; ask it.
  /// </summary>
  private static int StackSlotCount(IrStructType structType) => structType.SizeInBytes / 8;

  /// <summary>
  /// Name of the BulkZero stack slot holding the field at <paramref name="fieldOffset"/> of a
  /// stack-allocated struct tagged <paramref name="stackTag"/>.
  ///
  /// Slots run in REVERSE offset order because the LEA that materialises a pointer to the
  /// record yields the lowest stack address: numbering them backwards is what puts offset 0
  /// at [ptr+0].
  /// </summary>
  private static string StackSlotName(string stackTag, IrStructType structType, int fieldOffset) =>
    $"{stackTag}.{StackSlotCount(structType) - 1 - (fieldOffset / 8)}";

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
      if (backingType == IrType.F64) return new StdF64(IrContext.Current.NextStdId());
      if (backingType == IrType.F32) return new StdF32(IrContext.Current.NextStdId());
      return new StdI64(IrContext.Current.NextStdId());
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

  /// <summary>
  /// The element count a deferred <c>countof</c> resolves to. By the time this runs,
  /// monomorphization has rewritten the op's operand from the enclosing DECLARATION to the
  /// INSTANCE this copy of the body was cloned for, and an instance either states a count or does
  /// not.
  ///
  /// ⭐ THE REFUSAL IS A USER-FACING DIAGNOSTIC, NOT AN INTERNAL ERROR, and it is why the op
  /// carries a position. What has gone wrong is an INSTANTIATION — the same declaration written
  /// <c>Box with 3 Int</c> answers and written <c>Box with Int</c> does not — so it must name the
  /// user's line and the instance, rather than surfacing as an E9001 with a C# stack trace the way
  /// its <c>sizeof</c> twin's unresolved operand still does.
  /// </summary>
  private static long ResolveCountofElementCount(MaxonCountofOp op, IrModule<MaxonOp> module) {
    if (module.TypeDefs.TryGetValue(op.TypeName, out var t)
        && t is IrStructType instance
        && instance.ConstParams.TryGetValue(IrStructType.CapacityConstParamName, out var count))
      return count;

    // No spelling is claimed: monomorphization has already rewritten the operand, so which of
    // `Self` and the declaration's own name was written here is no longer knowable — and both
    // denoted the instance now being named anyway.
    throw new CompileError(ErrorCode.CountofTypeStatesNoElementCount,
      $"countof of the enclosing generic, in a body compiled for '{op.TypeName}' — that instance "
      + "states no element count. A count is a coordinate of the INSTANCE, and this one was applied "
      + "to type arguments only: instantiate it with a count (`Box with 3 Int`) or read a runtime "
      + "length instead",
      op.Line, op.Column) { FilePath = _currentFuncSourceFile };
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

    // Debug-info capture (--debug-info only; map null otherwise). Record this local's SOURCE type — a
    // heap pointer names its struct/enum type (set above in the StdHeapPtr case), a scalar names its
    // storage width. A PARAM's type is authoritative from its seed (its i64-pointer store here would
    // otherwise look like a conflict), so sealed names are skipped. For a non-param, a second store of
    // the same name with a DIFFERENT type poisons the entry, so the reused slot is OMITTED rather than
    // labeled with only one of its two types (see DebugLocalTypes) — the one stable slot cannot name
    // both honestly.
    if (_debugLocalTypes != null && !_debugSealedLocalNames!.Contains(varName))
      DebugLocalTypes.Record(_debugLocalTypes, varName,
        _varNameToStructType!.GetValueOrDefault(varName, varTypes[varName]));
  }

  /// Converts a tag name to its symdata label form (e.g. "foo.bar" -> "__tag_foo_bar").
  internal static string SanitizeTagLabel(string tag) =>
    $"__tag_{tag.Replace('.', '_').Replace(' ', '_')}";

  /// Registers a symdata C string for the given tag if not already cached. Returns the label.
  [ThreadStatic] private static Dictionary<string, string>? _symdataTagCache;
  [ThreadStatic] private static Dictionary<string, int>? _tagIndexMap;
  [ThreadStatic] private static int _nextTagIndex;
  [ThreadStatic] private static Dictionary<string, string>? _varNameToStructType;
  // Debug-info (--debug-info): the current function's local NAME -> SOURCE type name, captured by
  // EmitStore. Points at the per-function map the Run loop creates; null when debug info is off (or
  // the function is stdlib) and during the post-loop synthetic-function generation (so those helpers
  // do not pollute a function's already-attached map). MaxonToStandard is single-threaded, so a
  // ThreadStatic cursor is safe here exactly as _varNameToStructType is.
  [ThreadStatic] private static Dictionary<string, string>? _debugLocalTypes;
  // The PARAMETER names of the current function. Their type is seeded authoritatively from the source
  // signature (before the ABI erases a struct/enum param to an i64 pointer), so EmitStore must NOT let
  // that pointer store look like a conflicting redefinition — it skips capture for a sealed name.
  [ThreadStatic] private static HashSet<string>? _debugSealedLocalNames;
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

  // Memo for GetDestructorLabelForType. Each call resolves the type, walks
  // enum cases or struct fields, and runs four IsManaged*Type predicates over
  // the type-alias chain — work that's fully determined by typeName given the
  // module state at lowering start. _resultModule.TypeDefs is populated once
  // in Run() and read-only thereafter, so the result for a given typeName is
  // stable for the duration of a single Run(). Reset at the top of Run().
  [ThreadStatic] private static Dictionary<string, string?>? _destructorLabelCache;

  /// <summary>
  /// Returns the destructor function label for a type, or null if the type has no managed fields.
  /// The destructor label convention is "__destruct_{TypeName}".
  /// </summary>
  private static string? GetDestructorLabelForType(string? typeName) {
    if (typeName == null) return null;
    var cache = _destructorLabelCache!;
    if (cache.TryGetValue(typeName, out var cached)) return cached;
    var label = ComputeDestructorLabelForType(typeName);
    cache[typeName] = label;
    return label;
  }

  /// Computes the destructor label, or null when the type genuinely HAS no destructor — a
  /// legitimate and common answer. A name TypeDefs cannot resolve is the other thing entirely
  /// (the decision could not be taken), and RequireDeclaredAllocationType refuses it rather than
  /// letting it share this null.
  private static string? ComputeDestructorLabelForType(string typeName) {
    var typeDefs = _resultModule!.TypeDefs;
    var typeDef = RequireDeclaredAllocationType(typeName);

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
        ? (StdI64)new StdHeapPtr(IrContext.Current.NextStdId(), typeName)
        : new StdI64(IrContext.Current.NextStdId());
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
    var result = new StdI64(IrContext.Current.NextStdId());
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

  /// Raw buffer free via mm_raw_free (companion to EmitRawAlloc).
  /// Under --mm-trace, mm_raw_free reads a scope-cstring from Arg1; callers
  /// that emit only the ptr would leave Arg1 uninitialized and print garbage
  /// (or crash) in the trace. This helper adds a null-scope second arg when
  /// tracing so the runtime skips the [scope] section uniformly.
  private static void EmitRawFree(IrBlock<StandardOp> block, StdI64 ptr) {
    if (Compiler.MmTrace) {
      var nullScope = EmitNullPtr(block);
      block.AddOp(new StdCallRuntimeOp("mm_raw_free", [ptr, nullScope], null));
    } else {
      block.AddOp(new StdCallRuntimeOp("mm_raw_free", [ptr], null));
    }
  }

  /// <summary>Emit mm_incref(heap_ptr) — increments reference count for a scope-owned allocation. Trace is built into mm_incref.
  /// Not null-guarded: every caller is expected to only run this on live heap pointers.
  /// A null reaching mm_incref is a compiler bug — the panic helps surface it.</summary>
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

  /// A MANAGED element occupies exactly one machine pointer in the buffer, whatever the element
  /// type's own size is — the slot holds the record's address. Every walk over a managed buffer
  /// strides by this, and `mm_incref_managed_elements` panics when a record's element_size says
  /// otherwise.
  private const int ManagedElementPointerSize = 8;
  private const int ManagedMemoryStructSize = 40;

  // Byte-fusion (inline storage). An OWNED record whose element/byte buffer lives INLINE in the
  // SAME allocation — right after the record fields, so buffer@0 == self + recordSize — is marked
  // by parent_ptr == MmParentInline. Capacity stays >= 0 (a non-pointer parent sentinel like
  // ROOT/RDATA, not a capacity sentinel), so bounds checks and the COW no-copy fast path keep
  // working. The inline bytes are reclaimed with the record's own slot; there is no separate
  // buffer to free. The first grow past inline capacity DETACHES to a normal external buffer.
  // Arrays fuse only when their element bytes fit MmInlineCapBytes (they grow geometrically, so a
  // larger array keeps an external buffer); strings have no cap. Ports the self-hosted runtime's
  // MM_PARENT_INLINE / MM_INLINE_CAP_BYTES (stdlib/Internals.maxon).
  private const int MmParentInline = -3;
  private const int MmInlineCapBytes = 64;

  // The capacity a record carries when its buffer is NOT owned heap memory it may grow or free:
  // interned .rdata bytes, an immortal static-literal record, or stack scratch. It is a capacity
  // SENTINEL rather than a count, so the COW check detaches on any write and the destructor frees
  // nothing. Its sibling — capacity == -1, "borrowed from parent_ptr", the view sentinel — is still
  // spelled as a bare -1 at the slice sites; naming that one belongs with a sweep of the emitters,
  // which read both.
  private const int MmCapacityRdata = -2;

  // __ManagedMemoryCursor struct field offsets (all fields are 8 bytes)
  private const int CursorFieldBuffer = 0;
  private const int CursorFieldPosition = 8;
  private const int CursorFieldLength = 16;
  private const int CursorFieldElementSize = 24;
  private const int CursorFieldSourcePtr = 32;
  private const int CursorStructSize = 40;

  // Fused String/Character layout (envelope collapse): the record IS a __ManagedMemory
  // (buffer@0, length@8, capacity@16, element_size@24, parent_ptr@32) plus, for String, a
  // trailing singleByteGraphemesFlag@40. So `self.managed == self`, and the managed field offsets above
  // apply to `self` directly. Character is exactly a __ManagedMemory (40 bytes, no flag).
  private const int StringFieldSingleByteGraphemes = 40;
  private const int StringStructSize = 48;
  private const int CharacterStructSize = 40;

  // Associated-value union record layout. A union with ANY payload-carrying case is heap-boxed as a
  // discriminant followed by flat 8-byte payload slots: [tag@0, payload_0@8, payload_1@16, ...].
  // The value stored in the tag slot is IrEnumCase.TagValue — that property is the ONE source of the
  // discriminant, so nothing here re-derives it from RawValue/Ordinal.
  private const int UnionFieldTag = 0;
  private const int UnionPayloadSlotSize = 8;
  private const int UnionFirstPayloadOffset = 8;

  /// Byte offset of a union payload slot within the heap record. Passing the slot COUNT yields the
  /// offset one past the last slot, which is exactly the record's size — the two facts are the same
  /// arithmetic and are deliberately not written down separately.
  private static int UnionPayloadOffset(int slotIndex) =>
    UnionFirstPayloadOffset + slotIndex * UnionPayloadSlotSize;

  /// <summary>
  /// The type a union payload slot is written and read AS.
  ///
  /// ⭐ THE SLOT IS EIGHT BYTES WHATEVER IT HOLDS, SO THIS IS NOT A QUESTION ABOUT WIDTH — IT IS A
  /// QUESTION ABOUT WHICH REGISTER FILE THE VALUE LIVES IN. Every non-float payload names `i64`: an
  /// int, a heap pointer, and a narrowed scalar all occupy the whole slot, and a `bool` is stored
  /// widened and converted back with `!= 0` at the read. A float is the exception, and the only one.
  ///
  /// ⚠ THREE SITES — CONSTRUCT, EXTRACT AND WRITE-BACK — NAMED IT `i64` UNCONDITIONALLY, AND
  /// THE FRONT END ACCEPTS A FLOAT PAYLOAD, so the refusal arrived from the far end of the pipeline
  /// or not at all: the construct asked the register allocator for a general-purpose home for a
  /// value that only ever had an xmm one (`E9001: RegisterManager: value %N has no register and no
  /// stack home`) and the extract threw an unhandled `InvalidCastException` reported as `E9001 ...
  /// Unable to cast StdI64 to StdF64` — both with a .NET stack trace, at the user.
  ///
  /// `maxon-selfhosted` reaches the same eight bytes by BITCASTING through an integer
  /// (`Compiler/IR/Maxon/LowerMaxonToStd.maxon:12530`, which guards the identical three sites). It
  /// is not needed here: <c>StdStoreIndirectOp</c>/<c>StdLoadIndirectOp</c> already carry a field
  /// type and both targets already implement `f64` for it, so naming the type IS the lowering.
  ///
  /// ⚠ THIS RULE COVERS THE THREE SITES THAT STORE AT THE VALUE'S OWN TYPE. THERE IS A FOURTH,
  /// and it is not one of them: <c>LowerEnumFromNameAssociated</c> selects between the slot's current
  /// contents and the new value at RUNTIME, which is an i64 operation, so it widens through
  /// <see cref="EmitPayloadAsSlotBits"/> instead of naming a slot type. Counting three was how the
  /// fourth stayed broken after the other three were fixed.
  ///
  /// ⚠ A FLOAT PAYLOAD IS `f64` WHATEVER ITS DECLARED RANGE, INCLUDING AN <c>f32</c>-RANGED ONE.
  /// The slot is eight bytes, and `float(f32.min to f32.max)` — the only way to spell a 32-bit
  /// float, since bare `float32` is not a type — is MEASURED to arrive here as an <c>StdF64</c>.
  /// <c>Float32</c> therefore THROWS rather than answering: `f32` would be a store x64 cannot encode
  /// (<c>RegisterManager.EmitStoreIndirect</c> throws on it where arm64's dispatch handles it) and
  /// `f64` would be an eight-byte move of a four-byte value, silently wrong. Neither is a default
  /// worth having for an arm nothing can reach; whoever makes it reachable decides the width.
  ///
  /// ⚠ THE EXTRACT AND THE STORES ASK DIFFERENT SOURCES AND THEY DISAGREE ON PURPOSE, HARMLESSLY.
  /// The extract reads the front end's <c>MaxonValueKind</c>; the stores read the lowered value. A
  /// SYNTHESIZED clone takes neither route — <see cref="Passes.CloneBodySynthesis"/> deliberately
  /// labels every scalar payload <c>Integer</c> — so a cloned float payload is moved as `i64` bits
  /// and read back as `f64` by user code. Both are eight-byte moves of the same eight bytes, so the
  /// value round-trips; see that file's own note for why the clone side does it that way.
  /// </summary>
  private static IrType UnionPayloadSlotType(MaxonValueKind payloadKind) => payloadKind switch {
    MaxonValueKind.Float => IrType.F64,
    MaxonValueKind.Float32 => throw new InvalidOperationException(
      "union payload slot: a 32-bit float reached a payload slot, which no program can currently "
      + "produce - `float(f32.min to f32.max)` is lowered as an f64 and arrives as an StdF64. "
      + "Answering f64 here would move 8 bytes of a 4-byte value and answering f32 would emit a "
      + "store x64 cannot encode, so the slot's width has to be DECIDED before this arm is filled "
      + "in rather than guessed here."),
    _ => IrType.I64
  };

  /// <summary>
  /// The same rule asked of an ALREADY-LOWERED payload, which is what the two STORE sites hold. A
  /// <c>MaxonValue</c> cannot answer it — one <c>MaxonFloat</c> class serves both float widths — and
  /// the union's DECLARED case payload is out of reach at a write-back, which carries a flat slot
  /// index and no case name to read a type off. This only CLASSIFIES; which <c>IrType</c> follows
  /// from that is the overload above's, once.
  /// </summary>
  private static IrType UnionPayloadSlotType(StdValue payload) =>
    UnionPayloadSlotType(payload switch {
      StdF64 => MaxonValueKind.Float,
      StdF32 => MaxonValueKind.Float32,
      _ => MaxonValueKind.Integer
    });

  /// <summary>
  /// A scalar payload value in the slot's own <c>i64</c> REPRESENTATION, for the one construct site
  /// that cannot store the value at its own type.
  ///
  /// ⭐ <c>U.fromName("case", args…)</c> picks the matching case at RUNTIME, so it writes every
  /// slot branchlessly: load what is there, <c>arith.select</c> between that and the new value, store
  /// the result. A select is an <c>i64</c> operation, so unlike the direct construct — which stores
  /// the argument at whatever type it already has — this site has to bring the value INTO the slot's
  /// representation first.
  ///
  /// ⚠ IT USED TO DO THAT WITH A HARD <c>(StdI64)</c> CAST, so every payload whose lowered value is
  /// not already an <c>StdI64</c> died in the conversion with an unhandled .NET cast reported as
  /// `E9001 ... Unable to cast object of type 'StdF64' to type 'StdI64'` — and `'StdBool'` for a
  /// `bool`, which is the arm nobody reported. It is a WIDENING question, not a float question.
  ///
  /// A float is reinterpreted rather than converted, so the eight bytes the extract reads back as
  /// `f64` are the ones the caller passed. A `bool` becomes 1 or 0, which is what the extract's
  /// `!= 0` expects. A heap pointer is already an <c>StdI64</c> (<c>StdHeapPtr</c> derives from it)
  /// and needs nothing.
  /// </summary>
  private static StdI64 EmitPayloadAsSlotBits(IrBlock<StandardOp> block, StdValue payload) {
    switch (payload) {
      case StdI64 alreadySlotWidth:
        return alreadySlotWidth;

      case StdF64 f: {
        var bitcast = new StdBitcastF64ToI64Op(f);
        block.AddOp(bitcast);
        return bitcast.Result;
      }

      case StdBool b: {
        var one = new StdConstI64Op(1);
        block.AddOp(one);
        var zero = new StdConstI64Op(0);
        block.AddOp(zero);
        var widened = new StdSelectI64Op(b, one.Result, zero.Result);
        block.AddOp(widened);
        return widened.Result;
      }

      case StdI32 i32: {
        var ext = new StdExtI32ToI64Op(i32);
        block.AddOp(ext);
        return ext.Result;
      }

      default:
        throw new InvalidOperationException(
          $"union payload slot: no i64 representation for a payload lowered as "
          + $"{payload.GetType().Name}. The slot is eight bytes and every scalar payload has to be "
          + "widened into it; a new lowered value kind needs its widening written here rather than "
          + "a cast that fails at the user.");
    }
  }

  /// True for a fused String type (conforms to BuiltinStringLiteral): a 48-byte record
  /// whose first 40 bytes are a __ManagedMemory, with singleByteGraphemesFlag at offset 40.
  private static bool IsFusedStringType(string? typeName) =>
    typeName != null
    && _resultModule!.TypeDefs.TryGetValue(typeName, out var td)
    && td is IrStructType st && st.ConformingInterfaces.Contains("BuiltinStringLiteral");

  /// True for a fused Character type (conforms to BuiltinCharLiteral): a 40-byte record
  /// identical in layout to a __ManagedMemory.
  private static bool IsFusedCharType(string? typeName) =>
    typeName != null
    && _resultModule!.TypeDefs.TryGetValue(typeName, out var td)
    && td is IrStructType st && st.ConformingInterfaces.Contains("BuiltinCharLiteral");

  /// True for a fused Array/Vector type (conforms to BuiltinArrayLiteral): a 40-byte record
  /// identical in layout to a __ManagedMemory, whose `Element` type param IS its buffer element.
  private static bool IsFusedArrayType(string? typeName) =>
    typeName != null
    && _resultModule!.TypeDefs.TryGetValue(typeName, out var td)
    && td is IrStructType st && st.ConformingInterfaces.Contains("BuiltinArrayLiteral");

  /// True for any of the three fused managed-wrapper types (String, Character, Array/Vector):
  /// a record whose first 40 bytes ARE a __ManagedMemory (buffer@0 … parent_ptr@32).
  private static bool IsFusedManagedWrapper(string? typeName) =>
    IsFusedStringType(typeName) || IsFusedCharType(typeName) || IsFusedArrayType(typeName);

  /// Allocation size of a managed-memory-shaped record: a fused String is 48 bytes (trailing
  /// singleByteGraphemesFlag), a fused Character/Array 40, a bare __ManagedMemory 40. Used so a slice
  /// preserves its source's shape/size (a slice of a String is itself a 48-byte String).
  private static int FusedManagedRecordSize(string? typeName) =>
    IsFusedStringType(typeName) ? StringStructSize
    : IsFusedCharType(typeName) ? CharacterStructSize
    : ManagedMemoryStructSize;

  /// Byte offset of a __ManagedMemory field by its source-level name. Used when writing an
  /// absorbed inner managed struct literal's fields inline into a fused Array/Vector record.
  private static int ManagedFieldOffsetByName(string fieldName) => fieldName switch {
    "buffer" => ManagedFieldBuffer,
    "length" => ManagedFieldLength,
    "capacity" => ManagedFieldCapacity,
    "element_size" => ManagedFieldElementSize,
    "parent_ptr" => ManagedFieldParentPtr,
    _ => throw new InvalidOperationException($"Unknown __ManagedMemory field '{fieldName}'")
  };

  /// The inline element/byte buffer of a byte-fused record: buffer == self + recordSize, living in
  /// the record's own allocation. Recomputed from the record's stack slot at each use because a
  /// preceding runtime call (e.g. a toString conversion) may have clobbered any earlier copy.
  private static StdI64 EmitInlineBufferPtr(
    IrBlock<StandardOp> block, string managedVarName, int recordSize, Dictionary<string, string> varTypes) {
    var recSize = new StdConstI64Op(recordSize);
    block.AddOp(recSize);
    var self = (StdI64)EmitLoad(block, managedVarName, varTypes);
    var buf = new StdAddI64Op(self, recSize.Result);
    block.AddOp(buf);
    return buf.Result;
  }

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

  /// <summary>
  /// Load one half of a two-register value tuple, whether it currently lives in stack slots
  /// or in a heap record. A stack-promoted record has no pointer at all — its fields ARE
  /// named BulkZero slots — which is why this cannot simply be a field load off a base.
  /// </summary>
  private static StdValue EmitValueTupleHalfLoad(
    IrBlock<StandardOp> block, StdValue tupleValue, IrStructType tupleType, int fieldIndex,
    Dictionary<string, string> varTypes) {
    var field = tupleType.Fields[fieldIndex];

    if (tupleValue is StdStackPtr stackPtr && stackPtr.VarName != null
        && _stackVarTags != null && _stackVarTags.TryGetValue(stackPtr.VarName, out var stackTag))
      return EmitLoad(block, StackSlotName(stackTag, tupleType, field.Offset), varTypes);

    if (tupleValue is StdHeapPtr heapPtr && heapPtr.VarName != null)
      return EmitStructFieldLoad(block, heapPtr.VarName, field.Offset, IrType.Resolve(field.Type), varTypes);

    throw new InvalidOperationException(
      $"Value tuple '{tupleType.Name}' half {fieldIndex} is neither a stack nor a heap record "
      + $"(got {tupleValue.GetType().Name}) — the two-register return ABI has no way to read it");
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
        var currentFieldVal = EmitStructRawValueFieldSelect(block, enumType, ordinalValue, qualifiedName);
        EmitStructFieldStore(block, currentFieldVal, parentVarName, field.Offset, field.Type, varTypes);
      }
    }
  }

  /// <summary>
  /// The ordinal an enum operand carries at runtime. An associated-value enum is a heap
  /// pointer whose tag sits at offset 0; every other enum IS its ordinal already.
  /// </summary>
  private static StdI64 EmitEnumOrdinalOperand(
      IrBlock<StandardOp> block, StdValue enumOperand, Dictionary<string, string> varTypes) {
    if (enumOperand is not StdHeapPtr hp) return (StdI64)enumOperand;

    var heapPtr = (StdI64)EmitLoad(block, hp.VarName!, varTypes);
    var tagLoad = new StdLoadIndirectOp(heapPtr, 0, IrType.I64);
    block.AddOp(tagLoad);
    return (StdI64)tagLoad.Result;
  }

  /// <summary>
  /// The select chain for ONE leaf field of a struct-backed enum's raw value: ordinal → that
  /// field's per-variant constant. This is the whole of what reading such a field costs, and
  /// it allocates nothing — the raw values are compile-time constants.
  ///
  /// Shared by the struct-materializing lowering (which runs it once per field) and the fused
  /// `e.rawValue.field` lowering (which runs it once, for the field actually read).
  /// </summary>
  private static StdI64 EmitStructRawValueFieldSelect(
      IrBlock<StandardOp> block, IrEnumType enumType, StdI64 ordinalValue, string qualifiedFieldName) {
    var defaultVal = new StdConstI64Op(0);
    block.AddOp(defaultVal);
    StdI64 currentFieldVal = defaultVal.Result;

    foreach (var enumCase in enumType.Cases) {
      if (enumCase.RawValue is not StructRawValue srv) continue;
      long fieldValue = srv.Fields.First(f => f.FieldName == qualifiedFieldName).Value;

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

    return currentFieldVal;
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
        if (op is MaxonIteratorAdvanceOp or MaxonIteratorCurrentOp) return true;
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

  /// Reload heap-pointer self-field local variables from the self pointer.
  /// Called after method calls that may mutate self-fields (e.g. grow() reallocating arrays,
  /// or an inner method overwriting self.pending and freeing the previous value). Covers
  /// struct-typed fields and associated-value enum-typed (union) fields — both are stored as
  /// heap pointers (IrType.IsHeapAllocated == true), so a stale local would dangle if the
  /// field is reassigned during the call.
  private static void ReloadSelfFieldLocals(IrStructType selfStructType, IrBlock<StandardOp> block, Dictionary<string, string> varTypes, Dictionary<string, string>? selfFieldTempVars = null) {
    foreach (var field in selfStructType.Fields) {
      if (!field.Type.IsHeapAllocated) continue;
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
