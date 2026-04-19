using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  [ThreadStatic] private static IrModule<StandardOp>? _resultModule;
  [ThreadStatic] private static Dictionary<string, string>? _rdataStringCache;
  // Maps param name -> ref pointer var name for the current function being lowered
  [ThreadStatic] private static Dictionary<string, string>? _refParamPtrVars;
  // Tracks struct parameter names for the current function (not owned by us, no cleanup needed)
  [ThreadStatic] private static HashSet<string>? _structParamNames;
  [ThreadStatic] private static Dictionary<string, string>? _stackVarTags;
  [ThreadStatic] private static int _nextRdataId;
  [ThreadStatic] private static int _nextStdlibRdataId;
  [ThreadStatic] private static bool _rdataStdlibPhase;
  [ThreadStatic] private static HashSet<string>? _loadedUcdLabels;
  [ThreadStatic] private static string? _currentFuncName;
  [ThreadStatic] private static string? _currentFuncSourceFile;
  [ThreadStatic] private static int? _currentFuncSourceLine;
  [ThreadStatic] private static Dictionary<string, string>? _symdataContextCache;
  // Tracks type destructor functions that need to be generated (one per concrete type).
  [ThreadStatic] private static Dictionary<string, DestructorRequest>? _destructorRequests;
  private static string NextRdataId() =>
    _rdataStdlibPhase ? $"s{_nextStdlibRdataId++}" : $"{_nextRdataId++}";

  public static IrModule<StandardOp> Run(IrModule<MaxonOp> module) {
    _rdataStringCache = [];
    _symdataTagCache = [];
    _tagIndexMap = [];
    _nextTagIndex = 1;
    _symdataContextCache = [];
    _destructorRequests = [];
    _loadedUcdLabels = [];
    _rdataStdlibPhase = true;
    _nextStdlibRdataId = 0;
    _nextRdataId = 0;
    var result = new IrModule<StandardOp>();
    _resultModule = result;
    result.EntryFunctionName = module.EntryFunctionName;
    result.RdataEntries.AddRange(module.RdataEntries);
    result.Globals.AddRange(module.Globals);
    foreach (var (k, v) in module.TypeDefs) result.TypeDefs[k] = v;
    foreach (var (k, v) in module.TypeAliasSources) result.TypeAliasSources.TryAdd(k, v);

    // Resolve ranged primitive types so lowering sees base types (i64/f64/i8)
    foreach (var (_, typeDef) in module.TypeDefs) {
      if (typeDef is IrStructType st)
        foreach (var field in st.Fields)
          field.Type = IrType.Resolve(field.Type);
    }
    foreach (var func in module.Functions) {
      if (func.ReturnType is IrRangedPrimitiveType rptRet)
        func.ReturnType = rptRet.OptimalType;
      for (int i = 0; i < func.ParamTypes.Count; i++)
        if (func.ParamTypes[i] is IrRangedPrimitiveType rptParam)
          func.ParamTypes[i] = rptParam.BaseType;
    }

    // Build a lookup of functions by name for struct-aware call lowering
    var funcLookup = module.Functions.ToDictionary(f => f.Name);

    // Parameter mutation analysis is done by ParameterMutationAnalysisPass (runs earlier in pipeline).
    // ReassignedParams, MutatedParams, and MutatedParamIndices are already set on each function.

    bool hasResetAfterStdlib = false;

    foreach (var func in module.Functions) {
      // Skip synthetic builtin method stubs — they exist for type validation only
      if (func.IsBuiltinSynthetic) continue;

      // Skip generic functions that still have unresolved type parameters —
      // these are source templates that were monomorphized into concrete specializations.
      if (HasUnresolvedTypeParameters(func, module)) {
        continue;
      }

      // Reset IDs after stdlib for stable test output
      if (!hasResetAfterStdlib && !func.IsStdlib) {
        IrContext.Current.ResetIds();
        _rdataStdlibPhase = false;
        _nextRdataId = 0;
        _rdataStringCache = [];
        hasResetAfterStdlib = true;
      }

      var retStructType = ResolveStructReturnType(func.ReturnType, module.TypeDefs);
      _currentFuncName = func.Name;
      _currentFuncSourceFile = func.SourceFilePath;
      _currentFuncSourceLine = func.SourceLine;
      bool isStructInstanceMethod = IsStructInstanceMethod(func);
      bool isEnumInstanceMethod = IsEnumInstanceMethod(func);
      bool isInstanceMethod = isStructInstanceMethod || isEnumInstanceMethod;
      var selfStructType = isStructInstanceMethod ? ResolveStructType((IrStructType)func.ParamTypes[0], module.TypeDefs) : null;

      // Only reassigned params get pointer indirection; others stay by-value for zero overhead
      var refParamPtrVars = new Dictionary<string, string>();
      if (func.ReassignedParams != null) {
        for (int i = 0; i < func.ParamNames.Count; i++) {
          if (func.ParamNames[i] == "self") continue;
          if (func.ReassignedParams.Contains(func.ParamNames[i])) {
            refParamPtrVars[func.ParamNames[i]] = $"__ref_{func.ParamNames[i]}";
          }
        }
        if (refParamPtrVars.Count > 0)
          Logger.Trace(LogCategory.Ir, $"Pass-by-ref: {func.Name} receives ref params: {string.Join(", ", refParamPtrVars.Keys)}");
      }

      // Build the new function signature:
      // - Struct instance method 'self' param is passed as a heap pointer (i64)
      // - Enum instance method 'self' param is passed as a scalar
      // - Other struct params are passed as heap pointers (i64)
      // - Simple enum params are passed as scalars
      // - Associated-value enum params are passed as heap pointers (i64)
      // - Struct return is an i64 heap pointer returned normally
      var newParamNames = new List<string>();
      var newParamTypes = new List<IrType>();

      // Map from original struct param index to its flat param index (pointer slot)
      var structParamPtrIndex = new Dictionary<int, int>();
      // Map from original param index to flat param index (for all params)
      var paramFlatIndex = new Dictionary<int, int>();
      int flatIdx = newParamNames.Count;

      for (int i = 0; i < func.ParamNames.Count; i++) {
        paramFlatIndex[i] = flatIdx;
        if (isStructInstanceMethod && i == 0) {
          // Struct instance method self param: pass as pointer (i64)
          newParamNames.Add("__self_ptr");
          newParamTypes.Add(IrType.I64);
          flatIdx++;
        } else if (isEnumInstanceMethod && i == 0) {
          // Enum instance method self param: pass as scalar
          var enumType = (IrEnumType)func.ParamTypes[0];
          var backingIrType = ResolveEnumBackingIrType(enumType);
          newParamNames.Add("self");
          newParamTypes.Add(backingIrType);
          flatIdx++;
        } else if (func.ParamTypes[i] is IrEnumType { HasAssociatedValues: true }) {
          // Associated-value enum param: pass as heap pointer (i64), like structs
          structParamPtrIndex[i] = flatIdx;
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(IrType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is IrEnumType enumParamType) {
          // Simple enum param: pass as scalar (or i64 pointer if mutated)
          var backingIrType = ResolveEnumBackingIrType(enumParamType);
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(refParamPtrVars.ContainsKey(func.ParamNames[i]) ? IrType.I64 : backingIrType);
          flatIdx++;
        } else if (func.ParamTypes[i] is IrStructType or IrInterfaceType) {
          // Non-self struct/interface param: pass as pointer (i64)
          structParamPtrIndex[i] = flatIdx;
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(IrType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is IrFunctionType) {
          // Function-typed param: fn_ptr + hidden env_ptr (2 slots)
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(IrType.I64);
          flatIdx++;
          newParamNames.Add($"__env_{func.ParamNames[i]}");
          newParamTypes.Add(IrType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is not IrStructType and not IrEnumType) {
          newParamNames.Add(func.ParamNames[i]);
          // Mutated params receive a pointer (i64) instead of the original type
          newParamTypes.Add(refParamPtrVars.ContainsKey(func.ParamNames[i]) ? IrType.I64 : func.ParamTypes[i]);
          flatIdx++;
        } else {
          throw new InvalidOperationException($"Unhandled parameter type: {func.ParamTypes[i].GetType().Name} for param '{func.ParamNames[i]}'");
        }
      }

      IrType? newReturnType;
      if (retStructType != null) {
        // Struct return: return heap pointer as i64
        newReturnType = IrType.I64;
      } else if (func.ReturnType is IrInterfaceType) {
        // Interface return: the returned value is a heap pointer to a
        // concrete implementation, same ABI as a struct return.
        newReturnType = IrType.I64;
      } else if (func.ReturnType is IrEnumType { HasAssociatedValues: true }) {
        // Associated-value enum return: return heap pointer as i64
        newReturnType = IrType.I64;
      } else if (func.ReturnType is IrEnumType retEnumType) {
        newReturnType = ResolveEnumBackingIrType(retEnumType);
      } else if (func.ReturnType is not IrStructType and not IrEnumType) {
        newReturnType = func.ReturnType;
      } else {
        throw new InvalidOperationException($"Unhandled return type: {func.ReturnType.GetType().Name} in function '{func.Name}'");
      }
      var newFunc = new IrFunction<StandardOp>(func.Name, newParamNames, newParamTypes, newReturnType, func.ThrowsType) { IsStdlib = func.IsStdlib };
      var valueMap = new Dictionary<MaxonValue, StdValue>();
      var literalMap = new Dictionary<MaxonValue, MaxonLiteralOp>();
      var varTypes = new Dictionary<string, string>();
      // Maps function pointer StdValue IDs to the variable name holding the env_ptr
      var fnEnvVarNames = new Dictionary<int, string>();
      // Direct env_ptr values (avoids store/load when value is already in a register)
      var fnEnvDirectValues = new Dictionary<int, StdValue>();
      // Maps variable names to their resolved struct prefix (for cross-block references)
      var varNameToStructPrefix = new Dictionary<string, string>();
      // Maps variable names to their struct type name (for monomorphized type parameter vars)
      var varNameToStructType = new Dictionary<string, string>();
      // Variables that are stack-allocated structs (skip refcounting and scope cleanup)
      var stackAllocatedVars = new HashSet<string>();
      // Maps stack-allocated variable name to its BulkZero tag (for direct field access)
      var stackVarTags = new Dictionary<string, string>();
      _stackVarTags = stackVarTags;
      _varNameToStructType = varNameToStructType;
      var temps = new VarRegistry();
      // Use pre-computed constant array literal metadata from ConstantArrayAnalysisPass
      // Key: struct literal result ID, Value: ConstantArrayLiteralInfo

      _refParamPtrVars = refParamPtrVars;
      _structParamNames = [];

      // Tracks parameter names (used to distinguish params from locals in some paths)
      var structParamNames = new HashSet<string>();

      // Cache self-field loads: maps "fieldName" → temp var name for struct-typed self fields.
      // Avoids redundant load_indirect from self's heap pointer for the same field within a block.
      // Reset per-block since a cached var stored in one branch may not be defined in another.
      var selfFieldCache = new Dictionary<string, string>();

      // Tracks temp vars created for self-field accesses (e.g. __field_1234 for keys).
      // When a sibling method call may mutate self-fields, these temps must also be reloaded.
      var selfFieldTempVars = new Dictionary<string, string>();

      foreach (var block in func.Body.Blocks) {
        selfFieldCache.Clear();
        var newBlock = newFunc.Body.AddBlock(block.Name);

        // Pre-scan: find heap-allocating ops immediately consumed by declaration assigns
        // so they can store directly into the target variable, avoiding a temp.
        // Only for declarations — reassignments need managed cleanup of the old value
        // before the new fields are stored, so they must use an intermediate.
        var inlineTargets = new Dictionary<int, string>();
        for (int oi = 0; oi < block.Operations.Count - 1; oi++) {
          int? resultId = block.Operations[oi] switch {
            MaxonStructLiteralOp s => s.Result.Id,
            MaxonManagedMemSliceOp s => s.Result.Id,
            MaxonManagedMemCreateOp c => c.Result.Id,
            MaxonStringLiteralOp s => s.Result.Id,
            MaxonByteStringLiteralOp b => b.Result.Id,
            MaxonCharLiteralOp c => c.Result.Id,
            MaxonStringInterpOp i => i.Result.Id,
            MaxonCStringToManagedOp c => c.Result.Id,
            MaxonEnumConstructOp e => e.Result.Id,
            MaxonManagedListCreateOp c => c.Result.Id,
            _ => null
          };
          if (resultId != null
            && block.Operations[oi + 1] is MaxonAssignOp assign
            && assign.Value.Id == resultId
            && assign.IsDeclaration
            && !module.StackEligibleStructs.Contains(resultId.Value)) {
            inlineTargets[resultId.Value] = assign.VarName;
          }
        }

        // Pre-scan: find struct literal result IDs consumed as field values of another
        // struct literal. These get incref'd by the parent field store (line ~700) so they
        // must NOT receive a second incref or scope cleanup registration.
        var structLitFieldValueIds = new HashSet<int>();
        // Pre-scan: find struct literal / enum construct result IDs directly returned or thrown.
        // LowerReturn/LowerThrow handle their ownership transfer, so they must not be
        // incref'd or cleaned up here.
        var structLitReturnIds = new HashSet<int>();
        foreach (var op in block.Operations) {
          if (op is MaxonStructLiteralOp parentLit) {
            foreach (var (_, fieldVal) in parentLit.FieldValues) {
              structLitFieldValueIds.Add(fieldVal.Id);
            }
          } else if (op is MaxonReturnOp retOp && retOp.Value != null) {
            structLitReturnIds.Add(retOp.Value.Id);
          } else if (op is MaxonThrowOp throwOp) {
            structLitReturnIds.Add(throwOp.ErrorValue.Id);
          }
        }

        // Pre-scan: detect contiguous zero-init array element sequences that can
        // be replaced with a single StdBulkZeroOp during lowering.
        var bulkZeroSkipOps = new HashSet<MaxonOp>();
        var bulkZeroEmitPoints = new Dictionary<MaxonOp, (string tag, int count)>();
        {
          int i = 0;
          var ops = block.Operations;
          while (i < ops.Count - 1) {
            // Look for: MaxonLiteralOp(0) + MaxonAssignOp(__tag.N, isDecl)
            if (ops[i] is MaxonLiteralOp lit0
                && lit0.ValueKind == MaxonValueKind.Integer && lit0.IntValue == 0
                && ops[i + 1] is MaxonAssignOp assign0
                && assign0.Value.Id == lit0.Result.Id
                && assign0.IsDeclaration) {
              var dotIdx = assign0.VarName.IndexOf('.');
              if (dotIdx >= 0 && assign0.VarName.StartsWith("__arr_")) {
                var tag = assign0.VarName[..dotIdx];
                int groupStart = i;
                int count = 0;
                // Collect all consecutive zero-init pairs with the same tag
                while (i < ops.Count - 1
                    && ops[i] is MaxonLiteralOp litN
                    && litN.ValueKind == MaxonValueKind.Integer && litN.IntValue == 0
                    && ops[i + 1] is MaxonAssignOp assignN
                    && assignN.Value.Id == litN.Result.Id
                    && assignN.IsDeclaration
                    && assignN.VarName.StartsWith($"{tag}.")) {
                  count++;
                  i += 2;
                }
                if (count >= 8) {
                  // Mark all ops in the group for skipping
                  for (int j = groupStart; j < groupStart + count * 2; j++)
                    bulkZeroSkipOps.Add(ops[j]);
                  // First op in group triggers bulk zero emission
                  bulkZeroEmitPoints[ops[groupStart]] = (tag, count);
                }
                continue;
              }
            }
            i++;
          }
        }

        foreach (var op in block.Operations) {
          if (bulkZeroSkipOps.Contains(op)) {
            if (bulkZeroEmitPoints.TryGetValue(op, out var bzInfo))
              newBlock.AddOp(new StdBulkZeroOp(bzInfo.tag, bzInfo.count));
            continue;
          }
          switch (op) {
            case MaxonParamOp paramOp: {
              if (refParamPtrVars.TryGetValue(paramOp.Name, out string? value)) {
                // Mutated param: receive reference pointer, dereference for initial local copy
                var ptrVal = new StdI64(IrContext.Current.NextId());
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(paramOp.Index, paramOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, paramOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, value, varTypes);
                // Dereference to get the initial value
                var loadRef = new StdLoadI64Op(value);
                newBlock.AddOp(loadRef);
                var origType = func.ParamTypes[paramOp.Index];
                var derefType = origType is IrRangedPrimitiveType rpt ? rpt.BaseType : origType;
                var deref = new StdLoadIndirectOp(loadRef.Result, 0, derefType);
                newBlock.AddOp(deref);
                valueMap[paramOp.Result] = deref.Result;
                EmitStore(newBlock, deref.Result, paramOp.Name, varTypes);
              } else {
                // Non-mutated param: existing behavior
                var stdResult = paramOp.ValueKind.CreateStdValue();
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(paramOp.Index, paramOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, paramOp.Name, stdResult));
                valueMap[paramOp.Result] = stdResult;
                EmitStore(newBlock, stdResult, paramOp.Name, varTypes);
              }
              break;
            }
            case MaxonStructParamOp structParamOp: {
              if (isStructInstanceMethod && structParamOp.Name == "self") {
                // Instance method self: receive heap pointer as parameter, store as "self"
                var selfPtrVal = new StdI64(IrContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(0, "self", selfPtrVal));
                EmitStore(newBlock, selfPtrVal, "self", varTypes);
                valueMap[structParamOp.Result] = new StdHeapPtr(structParamOp.Result.Id, structParamOp.StructTypeName, "self");
              } else if (refParamPtrVars.TryGetValue(structParamOp.Name, out string? value)) {
                // Mutated struct param: receive pointer-to-heap-pointer, dereference for local copy
                int ptrFlatIdx = structParamPtrIndex[structParamOp.Index];
                var ptrVal = new StdI64(IrContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, structParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, value, varTypes);
                // Dereference: load pointer-to-slot, then load the heap pointer from the slot
                var loadRef = new StdLoadI64Op(value);
                newBlock.AddOp(loadRef);
                var deref = new StdLoadIndirectOp(loadRef.Result, 0, IrType.I64);
                newBlock.AddOp(deref);
                EmitStore(newBlock, (StdI64)deref.Result, structParamOp.Name, varTypes);
                valueMap[structParamOp.Result] = new StdHeapPtr(structParamOp.Result.Id, structParamOp.StructTypeName, structParamOp.Name);
              } else {
                // Non-self struct param: receive heap pointer, store under the param name
                int ptrFlatIdx = structParamPtrIndex[structParamOp.Index];
                var ptrVal = new StdHeapPtr(IrContext.Current.NextId(), structParamOp.StructTypeName, structParamOp.Name);
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, structParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, structParamOp.Name, varTypes);
                valueMap[structParamOp.Result] = ptrVal;
              }
              structParamNames.Add(structParamOp.Name);
              _structParamNames?.Add(structParamOp.Name);
              break;
            }
            case MaxonEnumLiteralOp enumLitOp: {
              if (enumLitOp.BackingKind == MaxonValueKind.Float) {
                var newOp = new StdConstF64Op(enumLitOp.FloatValue);
                newBlock.AddOp(newOp);
                valueMap[enumLitOp.Result] = newOp.Result;
              } else if (enumLitOp.BackingKind == MaxonValueKind.Integer) {
                var newOp = new StdConstI64Op(enumLitOp.IntValue);
                newBlock.AddOp(newOp);
                valueMap[enumLitOp.Result] = newOp.Result;
              } else {
                throw new InvalidOperationException($"Unsupported enum backing kind: {enumLitOp.BackingKind}");
              }
              break;
            }
            case MaxonEnumConstructOp enumConstructOp: {
              // Heap-allocate the enum: [tag:i64 @ 0, payload_0:i64 @ 8, payload_1:i64 @ 16, ...]
              var tempName = inlineTargets.TryGetValue(enumConstructOp.Result.Id, out var enumInlineTarget)
                ? enumInlineTarget
                : temps.CreateTemp("enum", enumConstructOp.Result.Id, enumConstructOp.EnumTypeName, OwnershipFlags.None);
              var enumTypeDef = (IrEnumType)module.TypeDefs[enumConstructOp.EnumTypeName];
              int maxPayload = GetMaxFlatPayloadSlots(enumTypeDef);
              int heapSize = 8 + maxPayload * 8;
              var enumPtr = EmitAlloc(newBlock, heapSize, enumConstructOp.EnumTypeName, scopeName: func.Name);
              EmitStore(newBlock, enumPtr, tempName, varTypes);

              // Store tag at offset 0
              var tagOp = new StdConstI64Op(enumConstructOp.TagValue);
              newBlock.AddOp(tagOp);
              newBlock.AddOp(new StdStoreIndirectOp(tagOp.Result, enumPtr, 0, IrType.I64));

              // Store associated values as payload slots via indirect stores
              int slotIdx = 0;
              for (int ai = 0; ai < enumConstructOp.Args.Count; ai++) {
                int byteOffset = 8 + slotIdx * 8;
                if (valueMap.TryGetValue(enumConstructOp.Args[ai], out var ecArgSv) && ecArgSv is StdHeapPtr ecArgHp) {
                  // Heap-pointer payload: store and incref — enum holds a reference
                  var childHeapPtr = (StdI64)EmitLoad(newBlock, ecArgHp.VarName!, varTypes);
                  newBlock.AddOp(new StdStoreIndirectOp(childHeapPtr, enumPtr, byteOffset, IrType.I64));
                  EmitIncrefValue(newBlock, childHeapPtr, scopeName: func.Name);
                  slotIdx++;
                } else {
                  // Scalar payload: store directly
                  var argStdVal = valueMap[enumConstructOp.Args[ai]];
                  newBlock.AddOp(new StdStoreIndirectOp(argStdVal, enumPtr, byteOffset, IrType.I64));
                  slotIdx++;
                }
              }
              // Zero-fill any unused payload slots
              for (int ai = slotIdx; ai < maxPayload; ai++) {
                var zeroOp = new StdConstI64Op(0);
                newBlock.AddOp(zeroOp);
                newBlock.AddOp(new StdStoreIndirectOp(zeroOp.Result, enumPtr, 8 + ai * 8, IrType.I64));
              }

              valueMap[enumConstructOp.Result] = new StdHeapPtr(enumConstructOp.Result.Id, enumConstructOp.EnumTypeName, tempName);

              // Orphan enum construct temps need incref + scope cleanup when they are not
              // consumed by a named variable (inlineTargets) or returned directly.
              // Mirrors the struct literal orphan pattern — without this, enum values
              // passed as borrowed function arguments leak when the callee doesn't consume them.
              if (!inlineTargets.ContainsKey(enumConstructOp.Result.Id)
                  && !structLitFieldValueIds.Contains(enumConstructOp.Result.Id)
                  && !structLitReturnIds.Contains(enumConstructOp.Result.Id)) {
                EmitIncrefValue(newBlock, enumPtr, scopeName: func.Name);
                varNameToStructType[tempName] = enumConstructOp.EnumTypeName;
                temps.MarkTempOrphan(tempName);
              }

              break;
            }
            case MaxonEnumTagOp enumTagOp: {
              if (valueMap.TryGetValue(enumTagOp.EnumValue, out var enumPrefixSv) && enumPrefixSv is StdHeapPtr enumPrefixHp) {
                // Associated-value enum: load tag from heap pointer at offset 0
                var heapPtr = (StdI64)EmitLoad(newBlock, enumPrefixHp.VarName!, varTypes);
                var tagLoad = new StdLoadIndirectOp(heapPtr, 0, IrType.I64);
                newBlock.AddOp(tagLoad);
                valueMap[enumTagOp.Result] = tagLoad.Result;
              } else {
                // Simple enums without associated values pass the ordinal directly
                valueMap[enumTagOp.Result] = valueMap[enumTagOp.EnumValue];
              }
              break;
            }
            case MaxonEnumPayloadOp enumPayloadOp: {
              // Load a payload value from the heap-allocated enum via indirect load
              var enumVarName = ((StdHeapPtr)valueMap[enumPayloadOp.EnumValue]).VarName!;
              int flatSlotOffset = enumPayloadOp.PayloadIndex;
              var heapPtr = (StdI64)EmitLoad(newBlock, enumVarName, varTypes);
              int byteOffset = 8 + flatSlotOffset * 8;

              if (enumPayloadOp.ResultKind == MaxonValueKind.Struct && enumPayloadOp.ResultStructTypeName != null) {
                // Struct-typed payload: load heap pointer from payload slot
                var tempStructName = temps.CreateTemp("enum_payload", enumPayloadOp.Result.Id, enumPayloadOp.ResultStructTypeName, OwnershipFlags.Borrowed);
                var loadOp = new StdLoadIndirectOp(heapPtr, byteOffset, IrType.I64);
                newBlock.AddOp(loadOp);
                EmitStore(newBlock, (StdI64)loadOp.Result, tempStructName, varTypes);
                valueMap[enumPayloadOp.Result] = new StdHeapPtr(enumPayloadOp.Result.Id, enumPayloadOp.ResultStructTypeName, tempStructName);
              } else if (enumPayloadOp.ResultKind == MaxonValueKind.Enum
                         && enumPayloadOp.ResultStructTypeName != null
                         && module.TypeDefs.TryGetValue(enumPayloadOp.ResultStructTypeName, out var payloadEnumDef)
                         && payloadEnumDef is IrEnumType payloadEnumType && payloadEnumType.HasAssociatedValues) {
                // Associated-value enum payload: load heap pointer (no unpacking needed)
                var tempName = temps.CreateTemp("enum_payload", enumPayloadOp.Result.Id, enumPayloadOp.ResultStructTypeName, OwnershipFlags.Borrowed);
                var loadOp = new StdLoadIndirectOp(heapPtr, byteOffset, IrType.I64);
                newBlock.AddOp(loadOp);
                EmitStore(newBlock, (StdI64)loadOp.Result, tempName, varTypes);
                valueMap[enumPayloadOp.Result] = new StdHeapPtr(enumPayloadOp.Result.Id, enumPayloadOp.ResultStructTypeName, tempName);
              } else {
                var loadOp = new StdLoadIndirectOp(heapPtr, byteOffset, IrType.I64);
                newBlock.AddOp(loadOp);
                if (enumPayloadOp.ResultKind == MaxonValueKind.Bool) {
                  // Bool payloads are stored as i64 in heap slots; convert to i1 via != 0
                  var zeroLit = new StdConstI64Op(0);
                  newBlock.AddOp(zeroLit);
                  var cmpOp = new StdCmpI64Op("ne", (StdI64)loadOp.Result, zeroLit.Result);
                  newBlock.AddOp(cmpOp);
                  valueMap[enumPayloadOp.Result] = cmpOp.Result;
                } else {
                  valueMap[enumPayloadOp.Result] = loadOp.Result;
                }
              }
              break;
            }
            case MaxonEnumParamOp enumParamOp: {
              // Check if this is an associated-value enum (passed as heap pointer)
              if (module.TypeDefs.TryGetValue(enumParamOp.EnumTypeName, out var epType)
                  && epType is IrEnumType epEnumType && epEnumType.HasAssociatedValues) {
                // Receive heap pointer — no unpacking needed, heap pointer IS the enum value
                int ptrFlatIdx = structParamPtrIndex[enumParamOp.Index];
                var ptrVal = new StdI64(IrContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, enumParamOp.Name, ptrVal));
                if (refParamPtrVars.TryGetValue(enumParamOp.Name, out string? value)) {
                  // Mutated assoc-value enum: receive pointer-to-heap-pointer
                  EmitStore(newBlock, ptrVal, value, varTypes);
                  var loadRef = new StdLoadI64Op(value);
                  newBlock.AddOp(loadRef);
                  var deref = new StdLoadIndirectOp(loadRef.Result, 0, IrType.I64);
                  newBlock.AddOp(deref);
                  EmitStore(newBlock, (StdI64)deref.Result, enumParamOp.Name, varTypes);
                } else {
                  EmitStore(newBlock, ptrVal, enumParamOp.Name, varTypes);
                }

                valueMap[enumParamOp.Result] = new StdHeapPtr(enumParamOp.Result.Id, enumParamOp.EnumTypeName, enumParamOp.Name);
                _structParamNames?.Add(enumParamOp.Name);
              } else if (refParamPtrVars.TryGetValue(enumParamOp.Name, out string? value)) {
                // Mutated simple enum: receive i64 pointer, dereference for local copy
                var ptrVal = new StdI64(IrContext.Current.NextId());
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(enumParamOp.Index, enumParamOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, enumParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, value, varTypes);
                var loadRef = new StdLoadI64Op(value);
                newBlock.AddOp(loadRef);
                var enumBackingType = enumParamOp.BackingKind == MaxonValueKind.Float ? IrType.F64 : IrType.I64;
                var deref = new StdLoadIndirectOp(loadRef.Result, 0, enumBackingType);
                newBlock.AddOp(deref);
                valueMap[enumParamOp.Result] = deref.Result;
                EmitStore(newBlock, deref.Result, enumParamOp.Name, varTypes);
              } else if (enumParamOp.BackingKind == MaxonValueKind.Float) {
                var stdResult = new StdF64(IrContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(enumParamOp.Index, enumParamOp.Name, stdResult));
                valueMap[enumParamOp.Result] = stdResult;
                EmitStore(newBlock, stdResult, enumParamOp.Name, varTypes);
              } else if (enumParamOp.BackingKind == MaxonValueKind.Integer) {
                var stdResult = new StdI64(IrContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(enumParamOp.Index, enumParamOp.Name, stdResult));
                valueMap[enumParamOp.Result] = stdResult;
                EmitStore(newBlock, stdResult, enumParamOp.Name, varTypes);
              } else {
                throw new InvalidOperationException($"Unsupported enum backing kind: {enumParamOp.BackingKind}");
              }
              break;
            }
            case MaxonEnumVarRefOp enumVarRef: {
              // Check if this is an associated-value enum (stored as flat vars)
              if (module.TypeDefs.TryGetValue(enumVarRef.EnumTypeName, out var evType)
                  && evType is IrEnumType evEnumType && evEnumType.HasAssociatedValues) {
                // Resolve the struct prefix: either from varNameToStructPrefix or direct varName
                string resolvedPrefix;
                if (varNameToStructPrefix.TryGetValue(enumVarRef.VarName, out var existingPrefix)) {
                  resolvedPrefix = existingPrefix;
                } else {
                  resolvedPrefix = enumVarRef.VarName;
                }
                valueMap[enumVarRef.Result] = new StdHeapPtr(enumVarRef.Result.Id, enumVarRef.EnumTypeName, resolvedPrefix);
              } else if (IsSelfField(isStructInstanceMethod, selfStructType, enumVarRef.VarName)) {
                // Simple enum stored as a self field — load from self's heap pointer
                var field = selfStructType!.GetField(enumVarRef.VarName)!;
                var loaded = EmitStructFieldLoad(newBlock, "self", field.Offset, field.Type, varTypes);
                valueMap[enumVarRef.Result] = loaded;
              } else {
                var loaded = EmitLoad(newBlock, enumVarRef.VarName, varTypes);
                valueMap[enumVarRef.Result] = loaded;
              }
              break;
            }
            case MaxonEnumPayloadAssignOp payloadAssign: {
              // Write a value back to a specific payload slot via heap-pointer indirection
              var resolvedPrefix = varNameToStructPrefix.GetValueOrDefault(payloadAssign.EnumVarName, payloadAssign.EnumVarName);
              var enumHeapPtr = (StdI64)EmitLoad(newBlock, resolvedPrefix, varTypes);
              int byteOffset = 8 + payloadAssign.PayloadIndex * 8;

              if (valueMap.TryGetValue(payloadAssign.NewValue, out var newStructSrcSv) && newStructSrcSv is StdHeapPtr newStructSrcHp) {
                // Heap-pointer payload: decref old value, store new, incref new
                var oldPayloadLoad = new StdLoadIndirectOp(enumHeapPtr, byteOffset, IrType.I64);
                newBlock.AddOp(oldPayloadLoad);
                EmitDecrefValueIfNonnull(newBlock, (StdI64)oldPayloadLoad.Result, scopeName: func.Name);
                var childHeapPtr = (StdI64)EmitLoad(newBlock, newStructSrcHp.VarName!, varTypes);
                var enumHeapPtrReload = (StdI64)EmitLoad(newBlock, resolvedPrefix, varTypes);
                newBlock.AddOp(new StdStoreIndirectOp(childHeapPtr, enumHeapPtrReload, byteOffset, IrType.I64));
                EmitIncrefValue(newBlock, childHeapPtr, scopeName: func.Name);
              } else {
                var newStdVal = valueMap[payloadAssign.NewValue];
                newBlock.AddOp(new StdStoreIndirectOp(newStdVal, enumHeapPtr, byteOffset, IrType.I64));
              }
              break;
            }
            case MaxonEnumRawValueOp rawValueOp: {
              var enumStdVal = valueMap[rawValueOp.EnumValue];
              if (enumStdVal is StdHeapPtr rawHp) {
                // Associated-value enum: load tag from heap offset 0
                var heapPtr = (StdI64)EmitLoad(newBlock, rawHp.VarName!, varTypes);
                var tagLoad = new StdLoadIndirectOp(heapPtr, 0, IrType.I64);
                newBlock.AddOp(tagLoad);
                valueMap[rawValueOp.Result] = tagLoad.Result;
              } else {
                // Simple enum: the backing value IS the raw value - just pass through
                valueMap[rawValueOp.Result] = enumStdVal;
              }
              break;
            }
            case MaxonErrorFlagToEnumOp errToEnumOp: {
              if (errToEnumOp.HasAssociatedValues) {
                // Associated-value error: the error flag IS the heap pointer
                var heapPtr = (StdI64)valueMap[errToEnumOp.ErrorFlag];
                var retVarName = temps.CreateTemp("error_enum", errToEnumOp.Result.Id, errToEnumOp.EnumTypeName, OwnershipFlags.None);
                EmitStore(newBlock, heapPtr, retVarName, varTypes);
                // No unpacking — heap pointer IS the enum value
                valueMap[errToEnumOp.Result] = new StdHeapPtr(errToEnumOp.Result.Id, errToEnumOp.EnumTypeName, retVarName);
              } else {
                // Simple error enum: subtract 1 from error flag to recover ordinal
                var errorFlagVal = (StdI64)valueMap[errToEnumOp.ErrorFlag];
                var oneOp = new StdConstI64Op(1);
                newBlock.AddOp(oneOp);
                var subOp = new StdSubI64Op(errorFlagVal, oneOp.Result);
                newBlock.AddOp(subOp);
                valueMap[errToEnumOp.Result] = subOp.Result;
              }
              break;
            }
            case MaxonEnumStringRawValueOp strRawOp: {
              var enumType = (IrEnumType)module.TypeDefs[strRawOp.EnumTypeName];
              var ordinalValue = (StdI64)valueMap[strRawOp.EnumValue];
              var (buf, len) = EmitStringEnumToString(enumType, ordinalValue, newBlock, result);
              var isString = !strRawOp.IsChar;
              var rawValTypeName = isString ? "String" : "Character";
              var tempName = temps.CreateTemp("enum_rawval", strRawOp.Result.Id, rawValTypeName, OwnershipFlags.None);
              var strRawHp = EmitManagedStructFromBufLen(tempName, buf, len,
                isString, newBlock, varTypes,
                allocTag: rawValTypeName);
              valueMap[strRawOp.Result] = strRawHp;
              break;
            }
            case MaxonEnumStructRawValueOp structRawOp: {
              var enumType = (IrEnumType)module.TypeDefs[structRawOp.EnumTypeName];
              var structType = (IrStructType)module.TypeDefs[structRawOp.StructTypeName];
              var stdValue = valueMap[structRawOp.EnumValue];

              // Extract ordinal from the enum value
              StdI64 ordinalValue;
              if (stdValue is StdHeapPtr rawHp) {
                // Associated-value enum: load tag from heap offset 0
                var heapPtr = (StdI64)EmitLoad(newBlock, rawHp.VarName!, varTypes);
                var tagLoad = new StdLoadIndirectOp(heapPtr, 0, IrType.I64);
                newBlock.AddOp(tagLoad);
                ordinalValue = (StdI64)tagLoad.Result;
              } else {
                ordinalValue = (StdI64)stdValue;
              }

              // Allocate struct on the heap
              var tempName = temps.CreateTemp("enum_rawval", structRawOp.Result.Id, structRawOp.StructTypeName, OwnershipFlags.None);
              var structPtr = EmitAlloc(newBlock, structType.SizeInBytes, structRawOp.StructTypeName, scopeName: func.Name);
              EmitStore(newBlock, structPtr, tempName, varTypes);

              // For each struct field, emit a select chain mapping ordinal -> field value
              EmitStructRawValueFields(newBlock, structType, enumType, ordinalValue,
                tempName, "", temps, varTypes, func.Name, module.TypeDefs);

              valueMap[structRawOp.Result] = new StdHeapPtr(structRawOp.Result.Id, structRawOp.StructTypeName, tempName);
              break;
            }
            case MaxonEnumOrdinalOp ordinalOp: {
              var enumType = (IrEnumType)module.TypeDefs[ordinalOp.EnumTypeName];
              var stdValue = valueMap[ordinalOp.EnumValue];
              StdI64 ordinalValue;
              if (stdValue is StdHeapPtr ordHp) {
                // Associated-value enum: load tag from heap, then convert to ordinal
                var heapPtr = (StdI64)EmitLoad(newBlock, ordHp.VarName!, varTypes);
                var tagLoad = new StdLoadIndirectOp(heapPtr, 0, IrType.I64);
                newBlock.AddOp(tagLoad);
                if (enumType.BackingType == IrType.I64) {
                  ordinalValue = EmitIntEnumToPositionIndex(enumType, (StdI64)tagLoad.Result, newBlock);
                } else {
                  // Tag is the ordinal for non-int-backed or auto-incremented enums
                  ordinalValue = (StdI64)tagLoad.Result;
                }
              } else if (enumType.BackingType == IrType.I64) {
                ordinalValue = EmitIntEnumToPositionIndex(enumType, (StdI64)stdValue, newBlock);
              } else if (enumType.BackingType == IrType.F64) {
                ordinalValue = EmitFloatEnumToPositionIndex(enumType, (StdF64)stdValue, newBlock);
              } else {
                // Simple enums (no backing type) and string/char-backed enums store ordinals directly
                ordinalValue = (StdI64)stdValue;
              }
              valueMap[ordinalOp.Result] = ordinalValue;
              break;
            }
            case MaxonEnumNameOp enumNameOp: {
              var enumType = (IrEnumType)module.TypeDefs[enumNameOp.EnumTypeName];
              var stdValue = valueMap[enumNameOp.EnumValue];
              StdI64 ordinalValue;
              if (stdValue is StdHeapPtr nameHp) {
                // Associated-value enum: load tag from heap, then convert to ordinal for name lookup
                var heapPtr = (StdI64)EmitLoad(newBlock, nameHp.VarName!, varTypes);
                var tagLoad = new StdLoadIndirectOp(heapPtr, 0, IrType.I64);
                newBlock.AddOp(tagLoad);
                if (enumType.BackingType == IrType.I64) {
                  ordinalValue = EmitIntEnumToOrdinal(enumType, (StdI64)tagLoad.Result, newBlock);
                } else {
                  ordinalValue = (StdI64)tagLoad.Result;
                }
              } else if (enumType.BackingType == IrType.I64) {
                ordinalValue = EmitIntEnumToOrdinal(enumType, (StdI64)stdValue, newBlock);
              } else if (enumType.BackingType == IrType.F64) {
                ordinalValue = EmitFloatEnumToOrdinal(enumType, (StdF64)stdValue, newBlock);
              } else {
                // Simple enums (no backing type) and string/char-backed enums store ordinals
                ordinalValue = (StdI64)stdValue;
              }
              var (nameBuf, nameLen) = EmitEnumNameLookup(enumType, ordinalValue, newBlock, result);
              var tempName = temps.CreateTemp("enum_name", enumNameOp.Result.Id, "String", OwnershipFlags.None);
              var enumNameHp = EmitManagedStructFromBufLen(tempName, nameBuf, nameLen,
                true, newBlock, varTypes,
                allocTag: "String");
              valueMap[enumNameOp.Result] = enumNameHp;
              break;
            }
            case MaxonLiteralOp litOp: {
              literalMap[litOp.Result] = litOp;
              switch (litOp.ValueKind) {
                case MaxonValueKind.Integer: {
                  var newOp = new StdConstI64Op(litOp.IntValue);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Float: {
                  var newOp = new StdConstF64Op(litOp.FloatValue);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Float32: {
                  var newOp = new StdConstF32Op((float)litOp.FloatValue);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Bool: {
                  var newOp = new StdConstI1Op(litOp.BoolValue);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Byte: {
                  var newOp = new StdConstI64Op(litOp.IntValue & 0xFF);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                case MaxonValueKind.Short: {
                  var newOp = new StdConstI64Op(litOp.IntValue & 0xFFFF);
                  newBlock.AddOp(newOp);
                  valueMap[litOp.Result] = newOp.Result;
                  break;
                }
                default:
                  throw new InvalidOperationException($"Unsupported literal kind: {litOp.ValueKind}");
              }
              break;
            }
            case MaxonStructLiteralOp structLitOp: {
              if (module.TypeDefs[structLitOp.TypeName] is not IrStructType structType)
                throw new InvalidOperationException($"StructLiteral type '{structLitOp.TypeName}' resolved to {module.TypeDefs[structLitOp.TypeName].GetType().Name} in func '{func.Name}'");

              // Stack allocation path: decompose struct into named field variables
              if (module.StackEligibleStructs.Contains(structLitOp.Result.Id)) {
                // Stack-allocate: reserve contiguous stack space and use a pointer,
                // identical to heap structs but without mm_alloc/refcounting.
                // Find the target variable name from the immediately following declaration assign
                // so we store the pointer directly as 't' instead of an intermediate '__stack_N'.
                string? tempName2 = null;
                var blockOps2 = block.Operations;
                for (int si = 0; si < blockOps2.Count - 1; si++) {
                  if (ReferenceEquals(blockOps2[si], structLitOp)
                      && blockOps2[si + 1] is MaxonAssignOp sa && sa.IsDeclaration && sa.Value.Id == structLitOp.Result.Id) {
                    tempName2 = sa.VarName;
                    break;
                  }
                }
                tempName2 ??= temps.CreateTemp("stack", structLitOp.Result.Id, structLitOp.TypeName, OwnershipFlags.None);
                var stackTag = $"__stk_{tempName2}";
                var fieldCount = Math.Max(structType.Fields.Count, 1);

                // Reserve stack space (skip zero-init — fields are immediately overwritten)
                newBlock.AddOp(new StdBulkZeroOp(stackTag, fieldCount, zeroInit: false));

                // Store each field directly to the BulkZero slots (no pointer indirection).
                // Fields are stored in reverse slot order so that the LEA (which returns the
                // lowest stack address) produces a pointer where [ptr+0] = field 0.
                foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                  var field = structType.GetField(fieldName)!;
                  var mappedVal = valueMap[fieldVal];
                  var slotIndex = fieldCount - 1 - (field.Offset / 8);
                  var slotName = $"{stackTag}.{slotIndex}";
                  EmitStore(newBlock, mappedVal, slotName, varTypes);
                }

                // No LEA/pointer store here — the pointer is emitted lazily
                // in FlattenCallArgs only when the struct is actually passed to a function.

                valueMap[structLitOp.Result] = new StdStackPtr(structLitOp.Result.Id, structLitOp.TypeName, tempName2);
                stackAllocatedVars.Add(tempName2);
                stackVarTags[tempName2] = stackTag;
                break;
              }

              // Heap-allocate the struct and store each field via indirect stores.
              // If this literal is immediately assigned, use the target variable name for the heap pointer.
              var tempName = inlineTargets.TryGetValue(structLitOp.Result.Id, out var inlineTarget)
                ? inlineTarget
                : temps.CreateTemp("struct", structLitOp.Result.Id, structLitOp.TypeName, OwnershipFlags.None);

              // Allocate memory for the struct on the heap
              var structPtr = EmitAlloc(newBlock, structType.SizeInBytes, structLitOp.TypeName, scopeName: func.Name);
              EmitStore(newBlock, structPtr, tempName, varTypes);

              foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                var field = structType.GetField(fieldName)!;
                if (valueMap.TryGetValue(fieldVal, out var nestedStructNameSv) && nestedStructNameSv is StdHeapPtr nestedStructNameHp) {
                  // Struct or associated-value enum field: both are heap pointers now
                  var nestedHeapPtr = EmitLoad(newBlock, nestedStructNameHp.VarName!, varTypes);
                  EmitStructFieldStore(newBlock, nestedHeapPtr, tempName, field.Offset, IrType.I64, varTypes);
                  // Incref — the struct holds a reference to this nested value
                  EmitIncrefValue(newBlock, (StdI64)nestedHeapPtr, scopeName: func.Name);
                } else {
                  var mappedVal = valueMap[fieldVal];
                  var litFieldStoreType = field.Type is IrStructType ? IrType.I64 : field.Type;
                  EmitStructFieldStore(newBlock, mappedVal, tempName, field.Offset, litFieldStoreType, varTypes);
                  // If the field is heap-allocated but the value wasn't tracked as a StdHeapPtr
                  // (e.g. runtime call results), we still need to incref
                  if (field.Type.IsHeapAllocated && mappedVal is StdI64 stdI64)
                    EmitIncrefValue(newBlock, stdI64, scopeName: func.Name);
                }
              }

              // Runtime guard: panic if __ManagedMemory is created with element_size == 0
              // (skip for bit-packed bools where element_size = 0 is the valid sentinel)
              if (TypeAliasInfo.IsManagedMemoryType(structLitOp.TypeName, module.TypeAliasSources) && !structLitOp.IsBitPacked) {
                var elemSizeCheck = (StdI64)EmitStructFieldLoad(newBlock, tempName, ManagedFieldElementSize, IrType.I64, varTypes);
                var zeroConst = new StdConstI64Op(0);
                newBlock.AddOp(zeroConst);
                // bounds_check(0, element_size, msg) panics if 0 >= element_size, i.e. element_size == 0
                EmitBoundsCheck(newBlock, zeroConst.Result, elemSizeCheck, "__mm_panic_element_size_zero");
              }

              // For array/vector literals, patch the buffer field to point to element data
              if (structLitOp.ArrayLiteralTag != null) {
                // Access buffer field through heap pointer indirection.
                // For __ManagedMemory, buffer is directly a field. For outer structs (Array, Vector),
                // the managed field is a nested struct whose heap pointer contains the buffer field.
                StdI64 rdataPtr;
                if (module.ConstantArrayLiterals.TryGetValue(structLitOp.Result.Id, out var constArrayInfo)) {
                  byte[] rdataBytes;
                  int rdataAlignment;
                  if (constArrayInfo.IsBitPacked) {
                    // Bit-packed bools: pack values as individual bits
                    int byteCount = (constArrayInfo.Values.Length + 7) / 8;
                    rdataBytes = new byte[byteCount];
                    for (int i = 0; i < constArrayInfo.Values.Length; i++) {
                      if (constArrayInfo.Values[i] != 0)
                        rdataBytes[i >> 3] |= (byte)(1 << (i & 7));
                    }
                    rdataAlignment = 1;
                  } else {
                    int elemSize = constArrayInfo.ElementSize;
                    rdataBytes = new byte[constArrayInfo.Values.Length * elemSize];
                    for (int i = 0; i < constArrayInfo.Values.Length; i++) {
                      switch (elemSize) {
                        case 1:
                          rdataBytes[i] = (byte)constArrayInfo.Values[i];
                          break;
                        case 2:
                          BitConverter.GetBytes((ushort)constArrayInfo.Values[i]).CopyTo(rdataBytes, i * elemSize);
                          break;
                        case 4:
                          BitConverter.GetBytes((int)constArrayInfo.Values[i]).CopyTo(rdataBytes, i * elemSize);
                          break;
                        case 8:
                          BitConverter.GetBytes(constArrayInfo.Values[i]).CopyTo(rdataBytes, i * elemSize);
                          break;
                        default:
                          throw new InvalidOperationException($"Unsupported constant array element size: {elemSize}");
                      }
                    }
                    rdataAlignment = elemSize;
                  }
                  result.RdataEntries.Add((constArrayInfo.RdataLabel, rdataBytes, rdataAlignment));
                  var leaRdataOp = new StdLeaRdataOp(constArrayInfo.RdataLabel);
                  newBlock.AddOp(leaRdataOp);
                  var rdataPtrOp = new StdPtrToI64Op(leaRdataOp.Result);
                  newBlock.AddOp(rdataPtrOp);
                  rdataPtr = rdataPtrOp.Result;
                } else if (structLitOp.SkipZeroInit) {
                  // Large scratch buffers with skipZeroInit stay on the stack — they are
                  // temporary within a single function and not stored to heap objects.
                  newBlock.AddOp(new StdBulkZeroOp(structLitOp.ArrayLiteralTag, structLitOp.ArrayLiteralCount, zeroInit: false));
                  var leaOp = new StdLeaOp(structLitOp.ArrayLiteralTag);
                  newBlock.AddOp(leaOp);
                  var castOp = new StdPtrToI64Op(leaOp.Result);
                  newBlock.AddOp(castOp);
                  rdataPtr = castOp.Result;
                } else {
                  // Heap-allocate the buffer. Stack buffers are unsafe because
                  // __gt_morestack can relocate the stack, making embedded stack pointers stale.
                  var leaOp = new StdLeaOp(structLitOp.ArrayLiteralTag);
                  newBlock.AddOp(leaOp);
                  var stackPtr = new StdPtrToI64Op(leaOp.Result);
                  newBlock.AddOp(stackPtr);

                  // Load element_size from the managed memory struct that was already lowered
                  StdI64 elemSizeVal;
                  if (TypeAliasInfo.IsManagedMemoryType(structLitOp.TypeName, module.TypeAliasSources)) {
                    elemSizeVal = (StdI64)EmitStructFieldLoad(newBlock, tempName, ManagedFieldElementSize, IrType.I64, varTypes);
                  } else {
                    var managedFieldForSize = structType.GetField("managed")!;
                    var managedPtrForSize = (StdI64)EmitStructFieldLoad(newBlock, tempName, managedFieldForSize.Offset, IrType.I64, varTypes);
                    var loadElemSize = new StdLoadIndirectOp(managedPtrForSize, ManagedFieldElementSize, IrType.I64);
                    newBlock.AddOp(loadElemSize);
                    elemSizeVal = (StdI64)loadElemSize.Result;
                  }

                  var countOp = new StdConstI64Op(structLitOp.ArrayLiteralCount);
                  newBlock.AddOp(countOp);
                  StdI64 totalSize;
                  StdI64 copySize;
                  if (structLitOp.IsBitPacked) {
                    // Bit-packed bools: byte size = (count + 7) >> 3
                    totalSize = ComputeBitPackedByteSize(newBlock, countOp.Result);
                    // Stack still has 1 byte per element, so copy count bytes from stack
                    copySize = countOp.Result;
                  } else {
                    var mulOp = new StdMulI64Op(countOp.Result, elemSizeVal);
                    newBlock.AddOp(mulOp);
                    totalSize = mulOp.Result;
                    copySize = mulOp.Result;
                  }

                  // Allocate heap buffer as raw memory (no refcount header)
                  var heapBuf = EmitRawAlloc(newBlock, totalSize, label: "cow.buf", scopeName: _currentFuncName);
                  if (structLitOp.IsBitPacked) {
                    // Pack bool values from stack (byte-per-element) into bit-packed heap buffer.
                    // Zero the heap buffer first, then set each bit from the stack elements.
                    // Since count is known at compile time, we unroll the loop.
                    for (int bi = 0; bi < structLitOp.ArrayLiteralCount; bi++) {
                      var elemVar = $"{structLitOp.ArrayLiteralTag}.{bi}";
                      var elemVal = (StdI64)EmitLoad(newBlock, elemVar, varTypes);
                      var bitIndex = new StdConstI64Op(bi);
                      newBlock.AddOp(bitIndex);
                      EmitBitSet(newBlock, heapBuf, bitIndex.Result, elemVal);
                    }
                  } else {
                    var copyResult = new StdI64(IrContext.Current.NextId());
                    newBlock.AddOp(new StdCallRuntimeOp("maxon_memcpy", [heapBuf, stackPtr.Result, copySize], copyResult));
                  }

                  // Incref struct elements — the array holds references to them
                  var firstElemVar = $"{structLitOp.ArrayLiteralTag}.0";
                  if (varNameToStructType.ContainsKey(firstElemVar)) {
                    for (int ei = 0; ei < structLitOp.ArrayLiteralCount; ei++) {
                      var elemVar = $"{structLitOp.ArrayLiteralTag}.{ei}";
                      EmitIncrefValue(newBlock, (StdI64)EmitLoad(newBlock, elemVar, varTypes), scopeName: func.Name);
                    }
                  }

                  rdataPtr = heapBuf;
                }

                // Writable (non-constant) buffers get capacity=count so COW check passes.
                // Constant (rdata) and skipZeroInit (stack scratch) buffers get capacity=-2
                // (rdata sentinel: read-only for COW, skipped by destructor to avoid freeing non-heap memory).
                bool isConstantBuffer = module.ConstantArrayLiterals.ContainsKey(structLitOp.Result.Id);
                // skipZeroInit buffers are stack-allocated (not heap) — capacity must be -2 so the destructor
                // does not call mm_raw_free on a stack address (which would corrupt the process heap).
                bool bufferIsWritable = !isConstantBuffer && !structLitOp.SkipZeroInit;
                if (TypeAliasInfo.IsManagedMemoryType(structLitOp.TypeName, module.TypeAliasSources)) {
                  // buffer is directly on this struct at offset 0
                  var bufferField = structType.GetField("buffer")!;
                  EmitStructFieldStore(newBlock, rdataPtr, tempName, bufferField.Offset, IrType.I64, varTypes);
                  var capOp = new StdConstI64Op(bufferIsWritable ? structLitOp.ArrayLiteralCount : -2);
                  newBlock.AddOp(capOp);
                  EmitStructFieldStore(newBlock, capOp.Result, tempName, ManagedFieldCapacity, IrType.I64, varTypes);
                } else {
                  // Outer struct (Array, Vector): load the managed field's heap pointer, then store buffer on it
                  var managedField = structType.GetField("managed")!;
                  var managedHeapPtr = (StdI64)EmitStructFieldLoad(newBlock, tempName, managedField.Offset, IrType.I64, varTypes);
                  // Store buffer on the __ManagedMemory heap object
                  var managedType = (IrStructType)managedField.Type;
                  var bufferField = managedType.GetField("buffer")!;
                  newBlock.AddOp(new StdStoreIndirectOp(rdataPtr, managedHeapPtr, bufferField.Offset, IrType.I64));
                  var capOp = new StdConstI64Op(bufferIsWritable ? structLitOp.ArrayLiteralCount : -2);
                  newBlock.AddOp(capOp);
                  var capField = managedType.GetField("capacity")!;
                  newBlock.AddOp(new StdStoreIndirectOp(capOp.Result, managedHeapPtr, capField.Offset, IrType.I64));
                }
              }

              valueMap[structLitOp.Result] = new StdHeapPtr(structLitOp.Result.Id, structLitOp.TypeName, tempName);

              // Orphan struct literal temps (__struct_N) need incref + scope cleanup when they
              // are not consumed by another construct that manages their lifetime:
              //  - inlineTargets: inlined into a named variable (parser handles cleanup)
              //  - structLitFieldValueIds: nested field value (parent field store handles incref)
              //  - structLitReturnIds: returned directly (LowerReturn handles incref + transfer)
              if (!inlineTargets.ContainsKey(structLitOp.Result.Id)
                  && !structLitFieldValueIds.Contains(structLitOp.Result.Id)
                  && !structLitReturnIds.Contains(structLitOp.Result.Id)) {
                // Orphan: not consumed by a named var. Incref to establish scope reference,
                // scope_end's mm_decref will release it.
                EmitIncrefValue(newBlock, structPtr, scopeName: func.Name);
                varNameToStructType[tempName] = structLitOp.TypeName;
                temps.MarkTempOrphan(tempName);
              }

              break;
            }
            case MaxonAssignOp assignOp: {
              // Associated-value enums now use heap pointers (like structs) and fall
              // through to the struct assignment path below.
              // Check valueMap for StdHeapPtr as the authoritative source: managed list ops like
              // managed_list_node_value report MaxonStruct result type even for primitives,
              // so ValueKind alone is not reliable.
              if (valueMap.TryGetValue(assignOp.Value, out var assignSv) && assignSv is StdHeapPtr assignHp) {
                // Struct assignment: copy the heap pointer (alias, not deep copy)
                // Copy-by-default cloning is handled at the parser level via clone() calls.
                var srcName = assignHp.VarName
                  ?? throw new InvalidOperationException($"MaxonAssignOp: StdHeapPtr missing VarName for value {assignOp.Value.Id} for assign to '{assignOp.VarName}' (ValueKind={assignOp.ValueKind}) in func {func.Name}");
                var dstName = assignOp.VarName;
                var structTypeName = assignHp.TypeName
                  ?? (assignOp.Value is MaxonStruct ms
                    ? ms.TypeName
                    : throw new InvalidOperationException($"No struct type info for value #{assignOp.Value.Id} in assign to '{assignOp.VarName}'"));
                // Stack pointer: no refcounting needed (no refcount header on stack memory).
                // Skip incref/decref; decref old value only if dst was heap-allocated.
                if (assignHp is StdStackPtr || stackAllocatedVars.Contains(srcName)) {
                  if (srcName != dstName) {
                    // If dst previously held a heap pointer, decref it before overwriting
                    if (varTypes.ContainsKey(dstName) && !stackAllocatedVars.Contains(dstName)) {
                      var oldHeapPtr = (StdI64)EmitLoad(newBlock, dstName, varTypes);
                      EmitDecrefValueIfNonnull(newBlock, oldHeapPtr, scopeName: func.Name);
                    }
                  }
                  stackAllocatedVars.Add(dstName);
                  // Propagate stack tag so aliases resolve to the same BulkZero slots
                  if (stackVarTags.TryGetValue(srcName, out var srcTag))
                    stackVarTags[dstName] = srcTag;
                } else {
                  if (srcName != dstName) {
                    // Decref old value before overwriting. Guarded by varTypes
                    // so the first store skips the decref (no previous value);
                    // reassignments and loop-header re-stores release the old ref.
                    // Skip decref when this is a new declaration and the slot previously
                    // held a non-struct value (e.g., integer for-loop variable reused as
                    // a string for-loop variable) — the old value is not a heap pointer.
                    if (varTypes.ContainsKey(dstName)
                        && !(assignOp.IsDeclaration && !varNameToStructType.ContainsKey(dstName))) {
                      if (!varNameToStructType.ContainsKey(dstName))
                        varNameToStructType[dstName] = structTypeName;
                      var oldHeapPtr = (StdI64)EmitLoad(newBlock, dstName, varTypes);
                      EmitDecrefValueIfNonnull(newBlock, oldHeapPtr, scopeName: func.Name);
                    }
                    var srcHeapPtr = EmitLoad(newBlock, srcName, varTypes);
                    EmitStore(newBlock, srcHeapPtr, dstName, varTypes);
                  }
                  // Incref for the new reference (rc=0 at alloc, every assignment increfs).
                  // Skip incref when ownership was transferred from a callee return —
                  // but NOT for SelfReturn (borrowed reference that needs its own incref).
                  var isSelfReturn = temps.TempHasFlag(srcName, OwnershipFlags.SelfReturn);
                  var isOwnsRef = temps.TempHasFlag(srcName, OwnershipFlags.OwnsRef);
                  var isCallRetTransfer = !isSelfReturn
                      && (assignOp.OwnerFlags?.HasFlag(OwnershipFlags.CallReturn) == true
                          || temps.IsCallReturnTransfer(srcName));
                  if (isCallRetTransfer || isOwnsRef) {
                    temps.ConsumeTempOwnership(srcName);
                  } else if (assignOp.IsDeclaration || srcName != dstName) {
                    EmitIncref(newBlock, assignOp.VarName, varTypes, scopeName: func.Name);
                  }
                }
                varNameToStructType[assignOp.VarName] = structTypeName;
                if (IsSelfField(isStructInstanceMethod, selfStructType, assignOp.VarName)) {
                  var field = selfStructType!.GetField(assignOp.VarName);
                  if (field != null) {
                    // Self-field write-through: just store the new value to self's heap field.
                    // The regular assign path above already handled decref/incref for the
                    // local variable, which aliases the self field.
                    var heapPtr2 = EmitLoad(newBlock, dstName, varTypes);
                    EmitStructFieldStore(newBlock, heapPtr2, "self", field.Offset, IrType.I64, varTypes);
                  }
                }
                valueMap[assignOp.Value] = stackAllocatedVars.Contains(dstName)
                  ? new StdStackPtr(assignOp.Value.Id, structTypeName, dstName)
                  : new StdHeapPtr(assignOp.Value.Id, structTypeName, dstName);
                varNameToStructPrefix[assignOp.VarName] = dstName;
              } else {
                var mappedValue = valueMap[assignOp.Value];
                // Widen I32/U32 to I64 when the variable was previously stored as I64
                // (e.g., try...otherwise where the default is I64 but the call result is U32)
                if (mappedValue is StdI32 && varTypes.TryGetValue(assignOp.VarName, out var prevType) && prevType == "i64") {
                  mappedValue = EnsureI64(mappedValue is StdU32 u32w ? new StdI32(u32w.Id) : mappedValue, newBlock, signExtend: mappedValue is not StdU32);
                }
                // A re-declaration of a name that a previous scope registered as
                // managed (e.g. two sibling `try ... otherwise (e) 'a'/'b'` blocks
                // with different error-enum types) needs to clear the stale
                // varNameToStructType entry: the slot is storing a fresh,
                // non-struct value, and a later EmitLoad on this slot would
                // otherwise fabricate a StdHeapPtr from the old registration.
                if (assignOp.IsDeclaration && assignOp.Value is not MaxonStruct) {
                  varNameToStructType.Remove(assignOp.VarName);
                }
                // For self fields, store through self's heap pointer only.
                // Cross-block references load from the heap pointer directly.
                if (IsSelfField(isStructInstanceMethod, selfStructType, assignOp.VarName)) {
                  var field = selfStructType!.GetField(assignOp.VarName);
                  if (field != null)
                    EmitStructFieldStore(newBlock, mappedValue, "self", field.Offset, field.Type, varTypes);
                } else {
                  EmitStore(newBlock, mappedValue, assignOp.VarName, varTypes);
                }
                // For struct-typed values that bypassed the StdHeapPtr path (e.g., try-await
                // results which are raw StdI64 heap pointers), register in varNameToStructType
                // so that scope_end emits mm_decref.
                if (assignOp.Value is MaxonStruct msNonHp) {
                  varNameToStructType[assignOp.VarName] = msNonHp.TypeName;
                }
              }
              // Write back through reference pointer for reassigned mutated parameters
              if (!assignOp.IsDeclaration && _refParamPtrVars != null
                  && _refParamPtrVars.TryGetValue(assignOp.VarName, out var refVarNameForWriteBack)) {
                var refPtr = (StdI64)EmitLoad(newBlock, refVarNameForWriteBack, varTypes);
                var localVal = EmitLoad(newBlock, assignOp.VarName, varTypes);
                var writeBackType = varTypes.TryGetValue(assignOp.VarName, out var vt2) ? VarTypeToIrType(vt2) : IrType.I64;
                newBlock.AddOp(new StdStoreIndirectOp(localVal, refPtr, 0, writeBackType));
              }
              break;
            }
            case MaxonVarRefOp varRef: {
              var resolvedVarName = varRef.VarName;
              // In instance methods, self fields are always loaded from self's heap pointer.
              // They don't exist as local variables.
              if (isStructInstanceMethod && selfStructType != null) {
                var field = selfStructType.GetField(resolvedVarName);
                if (field != null && field.Type is not IrStructType) {
                  // Load scalar self field via heap pointer
                  var loaded = EmitStructFieldLoad(newBlock, "self", field.Offset, field.Type, varTypes);
                  valueMap[varRef.Result] = loaded;
                  break;
                }
              }
              // After monomorphization, a VarRefOp originally typed as Integer may
              // actually refer to a struct variable. If the variable is a struct prefix,
              // handle it as a struct reference.
              if (!varTypes.ContainsKey(resolvedVarName) && varNameToStructPrefix.TryGetValue(resolvedVarName, out string? structPrefix)) {
                var resolvedType = varNameToStructType.TryGetValue(resolvedVarName, out var stType)
                  ? stType
                  : (valueMap.TryGetValue(varRef.Result, out var existSv) && existSv is StdHeapPtr existHp ? existHp.TypeName : "unknown");
                valueMap[varRef.Result] = new StdHeapPtr(varRef.Result.Id, resolvedType, structPrefix);
                break;
              }
              var loaded2 = EmitLoad(newBlock, resolvedVarName, varTypes);
              valueMap[varRef.Result] = loaded2;
              break;
            }
            case MaxonStructVarRefOp structVarRef: {
              // With heap refs, self fields are accessed via indirect loads from self's heap pointer.
              // For struct-typed self fields, load the nested heap pointer and store in a temp var.
              // Cache the load so repeated references to the same self field reuse the temp var.
              string resolvedName;
              if (IsSelfField(isStructInstanceMethod, selfStructType, structVarRef.VarName)) {
                if (selfFieldCache.TryGetValue(structVarRef.VarName, out var cachedName)) {
                  resolvedName = cachedName;
                } else {
                  var field = selfStructType!.GetField(structVarRef.VarName)!;
                  var tempVarName = temps.CreateTemp("selfref", structVarRef.Result.Id, structVarRef.StructTypeName, OwnershipFlags.Borrowed);
                  var nestedPtr = EmitStructFieldLoad(newBlock, "self", field.Offset, IrType.I64, varTypes);
                  EmitStore(newBlock, nestedPtr, tempVarName, varTypes);
                  resolvedName = tempVarName;
                  selfFieldCache[structVarRef.VarName] = tempVarName;
                }
              } else {
                resolvedName = varNameToStructPrefix.GetValueOrDefault(structVarRef.VarName, structVarRef.VarName);
              }
              // Prefer the canonical type from varNameToStructType (set during struct
              // assignment with resolved types) over the StructTypeName from the parser
              // which may contain stale inner alias names (e.g. "Entry" instead of
              // "StringIntPair") when the call-site rewrite preserved the old Result type.
              var resolvedTypeName = varNameToStructType.TryGetValue(structVarRef.VarName, out var vt)
                ? vt
                : structVarRef.StructTypeName;
              valueMap[structVarRef.Result] = stackAllocatedVars.Contains(resolvedName)
                ? new StdStackPtr(structVarRef.Result.Id, resolvedTypeName, resolvedName)
                : new StdHeapPtr(structVarRef.Result.Id, resolvedTypeName, resolvedName);
              break;
            }
            case MaxonFieldAccessOp fieldAccess: {
              var structName = ((StdHeapPtr)valueMap[fieldAccess.StructValue]).VarName!;
              // Resolve the field type and offset from the struct type definition
              var parentTypeName = valueMap.TryGetValue(fieldAccess.StructValue, out var ptnSv2) && ptnSv2 is StdHeapPtr ptnHp2 ? ptnHp2.TypeName : null;
              IrStructType? parentStructType = null;
              if (parentTypeName != null && module.TypeDefs.TryGetValue(parentTypeName, out var ptDef) && ptDef is IrStructType pst)
                parentStructType = pst;
              var fieldDef = parentStructType?.GetField(fieldAccess.FieldName);
              // If the field has an unresolved type parameter type (e.g., Entry._1 = Value),
              // resolve by finding a concrete alias with the same source type.
              if (fieldDef != null && fieldDef.Type is IrTypeParameterType && parentTypeName != null
                  && module.TypeAliasSources.TryGetValue(parentTypeName, out var parentAliasInfo)) {
                foreach (var (candidateName, candidateInfo) in module.TypeAliasSources) {
                  if (candidateName == parentTypeName) continue;
                  if (candidateInfo.SourceTypeName != parentAliasInfo.SourceTypeName) continue;
                  if (candidateInfo.TypeParams == null || candidateInfo.TypeParams.Values.Any(t => t is IrTypeParameterType)) continue;
                  if (module.TypeDefs.TryGetValue(candidateName, out var candidateDef) && candidateDef is IrStructType candidateSt) {
                    var resolvedField = candidateSt.GetField(fieldAccess.FieldName);
                    if (resolvedField != null && resolvedField.Type is not IrTypeParameterType) {
                      fieldDef = resolvedField;
                      break;
                    }
                  }
                }
              }

              if (fieldAccess.ResultKind == MaxonValueKind.Struct) {
                // Struct-typed field: load the nested struct's heap pointer and store it in a temp var
                var fieldTypeName = fieldDef?.Type is IrStructType fst ? fst.Name : (fieldAccess.ResultStructTypeName ?? "unknown");
                var tempVarName = temps.CreateTemp("field", fieldAccess.Result.Id, fieldTypeName, OwnershipFlags.Borrowed);
                if (fieldDef != null) {
                  var nestedPtr = EmitStructFieldLoad(newBlock, structName, fieldDef.Offset, IrType.I64, varTypes);
                  EmitStore(newBlock, nestedPtr, tempVarName, varTypes);
                  // For self fields accessed via self, also initialize the field name variable so that
                  // later code referencing it by name (across conditional blocks) gets the correct value.
                  // Only do this when the field access is on self, not on another struct that happens
                  // to have the same field name (e.g., other.managed vs self.managed in append).
                  if (structName == "self" && IsSelfField(isStructInstanceMethod, selfStructType, fieldAccess.FieldName)) {
                    EmitStore(newBlock, nestedPtr, fieldAccess.FieldName, varTypes);
                    // Track this temp so ReloadSelfFieldLocals can update it after calls
                    selfFieldTempVars[fieldAccess.FieldName] = tempVarName;
                  }
                } else {
                  // Fallback: try loading as a named variable (legacy path)
                  var loaded = EmitLoad(newBlock, $"{structName}.{fieldAccess.FieldName}", varTypes);
                  EmitStore(newBlock, loaded, tempVarName, varTypes);
                }
                // Propagate type info for the nested struct field
                var resolvedFieldType = fieldDef?.Type is IrStructType fieldStructType ? fieldStructType.Name : fieldTypeName;
                valueMap[fieldAccess.Result] = new StdHeapPtr(fieldAccess.Result.Id, resolvedFieldType, tempVarName);
                // Only update varNameToStructPrefix if the field name doesn't shadow
                // an existing parameter or local variable (e.g., a param named "data"
                // must not be overwritten by a field access like "existing.data").
                if (!varTypes.ContainsKey(fieldAccess.FieldName)) {
                  varNameToStructPrefix[fieldAccess.FieldName] = tempVarName;
                } else {
                  Logger.Trace(LogCategory.Ir, $"Skipping varNameToStructPrefix['{fieldAccess.FieldName}'] — shadows existing variable");
                }
              } else if (fieldAccess.ResultKind == MaxonValueKind.Enum
                         && fieldAccess.ResultStructTypeName != null
                         && module.TypeDefs.TryGetValue(fieldAccess.ResultStructTypeName, out var faEnumDef)
                         && faEnumDef is IrEnumType faEnumType && faEnumType.HasAssociatedValues) {
                // Associated-value enum field: load heap pointer (no unpacking needed)
                var tempVarName = temps.CreateTemp("field", fieldAccess.Result.Id, fieldAccess.ResultStructTypeName!, OwnershipFlags.Borrowed);
                if (fieldDef != null) {
                  var enumPtr = EmitStructFieldLoad(newBlock, structName, fieldDef.Offset, IrType.I64, varTypes);
                  EmitStore(newBlock, enumPtr, tempVarName, varTypes);
                } else {
                  var loaded = EmitLoad(newBlock, $"{structName}.{fieldAccess.FieldName}", varTypes);
                  EmitStore(newBlock, loaded, tempVarName, varTypes);
                }
                valueMap[fieldAccess.Result] = new StdHeapPtr(fieldAccess.Result.Id, fieldAccess.ResultStructTypeName!, tempVarName);
                if (!varTypes.ContainsKey(fieldAccess.FieldName)) {
                  varNameToStructPrefix[fieldAccess.FieldName] = tempVarName;
                }
                // For self fields, store the heap pointer under the field name so that
                // later code referencing it by name (across conditional blocks) gets the correct value.
                if (IsSelfField(isStructInstanceMethod, selfStructType, fieldAccess.FieldName)) {
                  EmitStore(newBlock, EmitLoad(newBlock, tempVarName, varTypes), fieldAccess.FieldName, varTypes);
                  varNameToStructPrefix[fieldAccess.FieldName] = fieldAccess.FieldName;
                }
              } else {
                // Scalar field access
                if (fieldDef != null && valueMap[fieldAccess.StructValue] is StdStackPtr stackPtr
                    && stackPtr.VarName != null && stackVarTags.TryGetValue(stackPtr.VarName, out var faTag)) {
                  // Stack struct: load directly from BulkZero slot (no pointer indirection)
                  var faFieldCount = ((IrStructType)module.TypeDefs[stackPtr.TypeName]).Fields.Count;
                  var slotName = $"{faTag}.{Math.Max(faFieldCount, 1) - 1 - (fieldDef.Offset / 8)}";
                  var loaded = EmitLoad(newBlock, slotName, varTypes);
                  valueMap[fieldAccess.Result] = loaded;
                } else if (fieldDef != null) {
                  var loaded = EmitStructFieldLoad(newBlock, structName, fieldDef.Offset, fieldDef.Type, varTypes);
                  valueMap[fieldAccess.Result] = loaded;
                } else {
                  // Fallback: try loading as a named variable (legacy path)
                  var loaded = EmitLoad(newBlock, $"{structName}.{fieldAccess.FieldName}", varTypes);
                  valueMap[fieldAccess.Result] = loaded;
                }
              }
              break;
            }
            case MaxonFieldAssignOp fieldAssign: {
              var structName = ((StdHeapPtr)valueMap[fieldAssign.StructValue]).VarName!;

              // Resolve the field type and offset from the struct type definition
              var faParentTypeName = valueMap.TryGetValue(fieldAssign.StructValue, out var faptnSv2) && faptnSv2 is StdHeapPtr faptnHp2 ? faptnHp2.TypeName : null;
              IrStructType? faParentStructType = null;
              if (faParentTypeName != null && module.TypeDefs.TryGetValue(faParentTypeName, out var faptDef) && faptDef is IrStructType fapst)
                faParentStructType = fapst;
              var faFieldDef = faParentStructType?.GetField(fieldAssign.FieldName);

              if (!valueMap.TryGetValue(fieldAssign.NewValue, out StdValue? mappedVal)) {
                throw new InvalidOperationException($"MaxonFieldAssignOp: NewValue %{fieldAssign.NewValue.Id} not in valueMap for {structName}.{fieldAssign.FieldName} in func {func.Name}");
              }
              // StdHeapPtr values must be loaded from their temp variable
              if (mappedVal is StdHeapPtr newValHp) {
                mappedVal = EmitLoad(newBlock, newValHp.VarName!, varTypes);
              }

              if (faFieldDef != null && valueMap[fieldAssign.StructValue] is StdStackPtr faStackPtr
                  && faStackPtr.VarName != null && stackVarTags.TryGetValue(faStackPtr.VarName, out var fsTag)
                  && !faFieldDef.Type.IsHeapAllocated) {
                // Stack struct with primitive field: store directly to BulkZero slot
                var fsFieldCount = ((IrStructType)module.TypeDefs[faStackPtr.TypeName]).Fields.Count;
                var slotName = $"{fsTag}.{Math.Max(fsFieldCount, 1) - 1 - (faFieldDef.Offset / 8)}";
                EmitStore(newBlock, mappedVal, slotName, varTypes);
              } else if (faFieldDef != null) {
                var storeType = faFieldDef.Type is IrStructType or IrEnumType { HasAssociatedValues: true } ? IrType.I64 : faFieldDef.Type;
                if (faFieldDef.Type is IrStructType or IrEnumType { HasAssociatedValues: true }) {
                  // Decref old field value before overwriting (may be null if field not yet initialized)
                  var oldFieldVal = (StdI64)EmitStructFieldLoad(newBlock, structName, faFieldDef.Offset, IrType.I64, varTypes);
                  EmitDecrefValueIfNonnull(newBlock, oldFieldVal, scopeName: func.Name);
                }
                EmitStructFieldStore(newBlock, mappedVal, structName, faFieldDef.Offset, storeType, varTypes);
                if (faFieldDef.Type is IrStructType or IrEnumType { HasAssociatedValues: true }) {
                  // Incref new value — the struct field holds a reference
                  EmitIncrefValue(newBlock, (StdI64)mappedVal, scopeName: func.Name);
                }
              } else {
                EmitStore(newBlock, mappedVal, $"{structName}.{fieldAssign.FieldName}", varTypes);
              }
              // No write-through needed: self is a heap pointer, and all field stores
              // go through the heap pointer directly, so the caller sees changes.
              if (structName == "self") selfFieldCache.Remove(fieldAssign.FieldName);
              break;
            }
            case MaxonBinOp binOp: {
              if (TryAlgebraicIdentity(binOp, literalMap, valueMap, newBlock, out var identityResult)) {
                Logger.Debug(LogCategory.Ir, $"Algebraic identity: {binOp.Operator} on {binOp.OperandKind} → eliminated");
                valueMap[binOp.Result] = identityResult;
                break;
              }

              // Load operand from valueMap; fall back to StdHeapPtr for type-parameter
              // fields promoted to Struct kind (they store heap pointers as i64 values)
              if (!valueMap.TryGetValue(binOp.Lhs, out StdValue? lhs)) {
                throw new InvalidOperationException($"BinOp LHS %{binOp.Lhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");
              }
              if (lhs is StdHeapPtr lhsHpBinOp)
                lhs = EmitLoad(newBlock, lhsHpBinOp.VarName!, varTypes);
              if (!valueMap.TryGetValue(binOp.Rhs, out StdValue? rhs)) {
                throw new InvalidOperationException($"BinOp RHS %{binOp.Rhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");
              }
              if (rhs is StdHeapPtr rhsHpBinOp)
                rhs = EmitLoad(newBlock, rhsHpBinOp.VarName!, varTypes);

              // Use OptimalType to select narrower/unsigned ops
              if (binOp.OperandKind == MaxonValueKind.Integer && binOp.OptimalType is IrType ot) {
                var signedOt = ot.ToSigned();
                if (signedOt == IrType.I32 || signedOt == IrType.I8) {
                  var i32Lhs = EnsureI32(lhs, newBlock);
                  var i32Rhs = EnsureI32(rhs, newBlock);
                  var (i32Op, i32Result) = ot.IsUnsigned
                    ? CreateUnsignedI32BinOp(binOp.Operator, i32Lhs, i32Rhs)
                    : CreateSignedI32BinOp(binOp.Operator, i32Lhs, i32Rhs);
                  newBlock.AddOp(i32Op);
                  valueMap[binOp.Result] = ot.IsUnsigned && i32Result is StdI32 ? new StdU32(i32Result.Id) : i32Result;
                  break;
                }
                if (ot.IsUnsigned) {
                  var i64Lhs = lhs is StdI64 l64 ? l64 : (StdI64)EnsureI64(lhs is StdU32 uw ? new StdI32(uw.Id) : lhs, newBlock, signExtend: false);
                  var i64Rhs = rhs is StdI64 r64 ? r64 : (StdI64)EnsureI64(rhs is StdU32 uw2 ? new StdI32(uw2.Id) : rhs, newBlock, signExtend: false);
                  var (unsignedOp, unsignedResult) = CreateUnsignedIntBinOp(binOp.Operator, i64Lhs, i64Rhs);
                  newBlock.AddOp(unsignedOp);
                  valueMap[binOp.Result] = unsignedResult;
                  break;
                }
              }

              // Widen narrowed operands back to i64 for full-width integer ops
              if (binOp.OperandKind == MaxonValueKind.Integer) {
                if (lhs is StdI32 or StdU32) lhs = EnsureI64(lhs is StdU32 u ? new StdI32(u.Id) : lhs, newBlock, signExtend: lhs is not StdU32);
                if (rhs is StdI32 or StdU32) rhs = EnsureI64(rhs is StdU32 u2 ? new StdI32(u2.Id) : rhs, newBlock, signExtend: rhs is not StdU32);
              }

              // Enums are compared as integers at the standard level
              var baseKind = binOp.OperandKind == MaxonValueKind.Enum ? MaxonValueKind.Integer : binOp.OperandKind;
              // F32 values arrive with Float kind from Maxon dialect; dispatch to Float32 ops
              var effectiveKind = baseKind == MaxonValueKind.Float && (lhs is StdF32 || rhs is StdF32)
                ? MaxonValueKind.Float32 : baseKind;
              if (effectiveKind == MaxonValueKind.Float32) {
                if (lhs is StdF64 lhsF64) { var cvt = new StdF64ToF32Op(lhsF64); newBlock.AddOp(cvt); lhs = cvt.Result; }
                if (rhs is StdF64 rhsF64) { var cvt = new StdF64ToF32Op(rhsF64); newBlock.AddOp(cvt); rhs = cvt.Result; }
              }
              var key = (binOp.Operator, effectiveKind);
              if (!BinOpFactories.TryGetValue(key, out var factory))
                throw new InvalidOperationException($"Unsupported binop: {binOp.Operator} on {binOp.OperandKind} in func {func.Name} block {block.Name}");

              var (newOp, factoryResult) = factory(lhs, rhs);
              newBlock.AddOp(newOp);
              valueMap[binOp.Result] = factoryResult;
              break;
            }
            case MaxonRefEqOp refEq: {
              // Struct values are tracked by StdHeapPtr in valueMap — load their heap pointers
              var lhsVarName = ((StdHeapPtr)valueMap[refEq.Lhs]).VarName!;
              var rhsVarName = ((StdHeapPtr)valueMap[refEq.Rhs]).VarName!;
              var lhsPtr = (StdI64)EmitLoad(newBlock, lhsVarName, varTypes);
              var rhsPtr = (StdI64)EmitLoad(newBlock, rhsVarName, varTypes);
              var predicate = refEq.Negate ? "ne" : "eq";
              var cmpOp = new StdCmpI64Op(predicate, lhsPtr, rhsPtr);
              newBlock.AddOp(cmpOp);
              valueMap[refEq.Result] = cmpOp.Result;
              break;
            }
            case MaxonCondBrOp condBr: {
              var cond = (StdBool)valueMap[condBr.Condition];
              newBlock.AddOp(new StdCondBrOp(cond, condBr.ThenBlock, condBr.ElseBlock));
              break;
            }
            case MaxonBrOp br: {
              newBlock.AddOp(new StdBrOp(br.Target));
              break;
            }
            case MaxonScopeEndOp scopeEnd: {
              var keep = scopeEnd.KeepVars;

              // Pre-incref Borrowed field temps that are being returned.
              // When returning `structVar.field`, the field is loaded into a Borrowed temp.
              // The scope cleanup decrefs structVar (whose destructor decrefs the field),
              // but the Borrowed temp still holds a pointer to the field.
              // Incref the field BEFORE scope cleanup so it survives the destructor.
              foreach (var retId in structLitReturnIds) {
                foreach (var (mv, sv) in valueMap) {
                  if (mv.Id == retId && sv is StdHeapPtr retFieldHp && retFieldHp.VarName != null
                      && temps.TempHasFlag(retFieldHp.VarName, OwnershipFlags.Borrowed)) {
                    var fieldPtr = (StdI64)EmitLoad(newBlock, retFieldHp.VarName, varTypes);
                    EmitIncrefValueIfNonnull(newBlock, fieldPtr, scopeName: func.Name);
                    // Mark as SelfReturn so LowerReturn doesn't incref again
                    temps.SetTempFlag(retFieldHp.VarName, OwnershipFlags.SelfReturn);
                    break;
                  }
                }
              }

              // Process in reverse order: variables declared later (containers,
              // iterators) must be freed before their backing stores (array
              // element slots) so destructors can still read live element data.
              var varsToClean = scopeEnd.VarsToClean.ToList();
              varsToClean.Reverse();
              foreach (var v in varsToClean) {
                if (keep != null && keep.Contains(v)) {
                  // Ownership transferred to caller — skip decref but emit trace if enabled.
                  if (Compiler.MmTrace && varNameToStructType.ContainsKey(v)) {
                    var transferPtr = EmitLoad(newBlock, v, varTypes);
                    var transferScopePtr = EmitTagPtr(newBlock, func.Name);
                    newBlock.AddOp(new StdCallRuntimeOp("mm_trace_transfer", [transferPtr, transferScopePtr], null));
                  }
                  continue;
                }
                if (_structParamNames != null && _structParamNames.Contains(v)) continue;
                // Self fields are owned by the heap-allocated struct; the struct destructor
                // handles their cleanup when self is freed. Decref'ing them here would
                // double-free after a field reassignment (assign decrefs old, scope_end
                // would then decref the new value still held by the field).
                if (IsSelfField(isStructInstanceMethod, selfStructType, v)) continue;
                // Stack-allocated structs need no refcount cleanup — stack reclaims them
                if (stackAllocatedVars.Contains(v)) continue;
                // Parser-attached metadata is authoritative about this binding's
                // type at scope-exit, which matters when two sibling scopes reuse
                // the same name with different kinds (e.g. `try ... otherwise (e) 'a' ... end 'a'`
                // with an assoc-value enum followed by a `try ... otherwise (e) 'b' ... end 'b'`
                // with a simple-enum error). Trust VarMetadata over the stale
                // varNameToStructType registration that a prior scope may have
                // left behind: skip decref when the metadata names a non-managed
                // type (no StructTypeName, or a simple enum with no associated
                // values — those are just integer ordinals). Also clear
                // varNameToStructType so that later EmitLoads on the same slot
                // don't return a fabricated StdHeapPtr — once the scope that
                // owned the managed value ends, the slot is back to being plain
                // storage for whatever the next scope stores there.
                if (scopeEnd.VarMetadata != null
                    && scopeEnd.VarMetadata.TryGetValue(v, out var meta)) {
                  bool notManaged = meta.StructTypeName == null
                    || (module.TypeDefs.TryGetValue(meta.StructTypeName, out var metaTy)
                        && metaTy is IrEnumType metaEnumTy
                        && !metaEnumTy.HasAssociatedValues);
                  if (notManaged) {
                    varNameToStructType.Remove(v);
                    continue;
                  }
                }
                // Only decref if this var is actually managed (has a struct type)
                if (!varNameToStructType.ContainsKey(v)) continue;
                // Simple mm_decref — destructors handle field cleanup when rc reaches 0
                var heapPtr = (StdI64)EmitLoad(newBlock, v, varTypes);
                EmitDecrefValueIfNonnull(newBlock, heapPtr, scopeName: func.Name);
                // Zero the slot so other paths see NULL (null-guarded decref skips it)
                var zeroOp = new StdConstI64Op(0);
                newBlock.AddOp(zeroOp);
                newBlock.AddOp(new StdStoreI64Op(zeroOp.Result, v));
              }
              // Build set of orphan temps that back a returned value — these must
              // survive scope cleanup so LowerReturn can read and transfer them.
              var returnedOrphanTemps = new HashSet<string>();
              foreach (var retId in structLitReturnIds) {
                // Find the temp name for this returned value via valueMap StdHeapPtr
                foreach (var (mv, sv) in valueMap) {
                  if (mv.Id == retId && sv is StdHeapPtr retHpTemp && retHpTemp.VarName != null
                      && temps.TempHasFlag(retHpTemp.VarName, OwnershipFlags.Orphan)) {
                    returnedOrphanTemps.Add(retHpTemp.VarName);
                    break;
                  }
                }
              }
              // Decref orphan temps created during lowering (not in parser scope tracking)
              foreach (var tempName in temps.OrphanTemps) {
                if (returnedOrphanTemps.Contains(tempName)) continue;
                var orphanPtr = (StdI64)EmitLoad(newBlock, tempName, varTypes);
                EmitDecrefValueIfNonnull(newBlock, orphanPtr, scopeName: func.Name);
                var zeroGlobal = new StdConstI64Op(0);
                newBlock.AddOp(zeroGlobal);
                newBlock.AddOp(new StdStoreI64Op(zeroGlobal.Result, tempName));
              }
              break;
            }
            case MaxonTruncOp truncOp: {
              var mappedInput = valueMap[truncOp.Input];
              if (mappedInput is StdF64 f64Input) {
                var stdOp = new StdFpToSiOp(f64Input);
                newBlock.AddOp(stdOp);
                valueMap[truncOp.Result] = stdOp.Result;
              } else if (mappedInput is StdF32 f32Input) {
                var stdOp = new StdFpToSiF32Op(f32Input);
                newBlock.AddOp(stdOp);
                valueMap[truncOp.Result] = stdOp.Result;
              } else if (mappedInput is StdI64 or StdI32) {
                // Ranged int types resolve to integer standard values; truncation only applies to float-to-int
                valueMap[truncOp.Result] = mappedInput;
              } else {
                throw new InvalidOperationException($"MaxonTruncOp: unexpected input type {mappedInput.GetType().Name}");
              }
              break;
            }
            case MaxonBitcastF64ToI64Op bitcastOp: {
              var input = (StdF64)valueMap[bitcastOp.Input];
              var stdOp = new StdBitcastF64ToI64Op(input);
              newBlock.AddOp(stdOp);
              valueMap[bitcastOp.Result] = stdOp.Result;
              break;
            }
            case MaxonIntToFloatOp intToFloatOp: {
              var input = (StdI64)valueMap[intToFloatOp.Input];
              var stdOp = new StdSiToFpOp(input);
              newBlock.AddOp(stdOp);
              valueMap[intToFloatOp.Result] = stdOp.Result;
              break;
            }
            case MaxonSizeofOp sizeofOp: {
              var sizeofType = ResolveSizeofType(sizeofOp.TypeName, module);
              var constOp = new StdConstI64Op((long)sizeofType.SizeInBytes);
              newBlock.AddOp(constOp);
              valueMap[sizeofOp.Result] = constOp.Result;
              break;
            }
            case MaxonAbsOp absOp:
              LowerUnaryFloat(valueMap, newBlock, absOp.Input, absOp.Result, i => new StdAbsF32Op(i), i => new StdAbsF64Op(i));
              break;
            case MaxonSqrtOp sqrtOp:
              LowerUnaryFloat(valueMap, newBlock, sqrtOp.Input, sqrtOp.Result, i => new StdSqrtF32Op(i), i => new StdSqrtF64Op(i));
              break;
            case MaxonFloorOp floorOp:
              LowerUnaryFloat(valueMap, newBlock, floorOp.Input, floorOp.Result, i => new StdFloorF32Op(i), i => new StdFloorF64Op(i));
              break;
            case MaxonCeilOp ceilOp:
              LowerUnaryFloat(valueMap, newBlock, ceilOp.Input, ceilOp.Result, i => new StdCeilF32Op(i), i => new StdCeilF64Op(i));
              break;
            case MaxonRoundOp roundOp:
              LowerUnaryFloat(valueMap, newBlock, roundOp.Input, roundOp.Result, i => new StdRoundF32Op(i), i => new StdRoundF64Op(i));
              break;
            case MaxonMinOp minOp:
              LowerBinaryFloat(valueMap, newBlock, minOp.Lhs, minOp.Rhs, minOp.Result, (l, r) => new StdMinF32Op(l, r), (l, r) => new StdMinF64Op(l, r));
              break;
            case MaxonMaxOp maxOp:
              LowerBinaryFloat(valueMap, newBlock, maxOp.Lhs, maxOp.Rhs, maxOp.Result, (l, r) => new StdMaxF32Op(l, r), (l, r) => new StdMaxF64Op(l, r));
              break;
            case MaxonCastOp castOp: {
              var input = valueMap[castOp.Input];
              switch (castOp.TargetKind) {
                case MaxonValueKind.Byte: {
                  // Cast to byte: convert to i64 if needed, then mask with 0xFF
                  StdI64 intInput;
                  if (input is StdI64 alreadyI64) {
                    intInput = alreadyI64;
                  } else if (input is StdI32 i32Input) {
                    throw new InvalidOperationException("i32 to byte conversion not yet implemented");
                  } else if (input is StdF64 f64Input) {
                    var fpToSi = new StdFpToSiOp(f64Input);
                    newBlock.AddOp(fpToSi);
                    intInput = fpToSi.Result;
                  } else if (input is StdF32 f32Input) {
                    var fpToSi = new StdFpToSiF32Op(f32Input);
                    newBlock.AddOp(fpToSi);
                    intInput = fpToSi.Result;
                  } else if (input is StdBool boolInput) {
                    // Bool to byte: bool is already 0 or 1, just reinterpret as i64
                    // Create a StdI64 that shares the same ID (reinterpretation)
                    intInput = new StdI64(boolInput.Id);
                  } else if (input is StdPtr) {
                    throw new InvalidOperationException("Cannot cast pointer to byte");
                  } else {
                    throw new InvalidOperationException($"Cannot cast {input.GetType().Name} to byte");
                  }
                  var maskOp = new StdConstI64Op(0xFF);
                  newBlock.AddOp(maskOp);
                  var andOp = new StdAndI64Op(intInput, maskOp.Result);
                  newBlock.AddOp(andOp);
                  valueMap[castOp.Result] = andOp.Result;
                  break;
                }
                case MaxonValueKind.Integer: {
                  // Byte/short/int to int: pass through (sub-word types are stored as I64)
                  if (input is StdI64 i64) {
                    valueMap[castOp.Result] = i64;
                  } else if (input is StdI32 i32) {
                    throw new InvalidOperationException("i32 to int conversion not yet implemented");
                  } else if (input is StdF64 f64) {
                    var fpToSi = new StdFpToSiOp(f64);
                    newBlock.AddOp(fpToSi);
                    valueMap[castOp.Result] = fpToSi.Result;
                  } else if (input is StdF32 f32) {
                    var fpToSi = new StdFpToSiF32Op(f32);
                    newBlock.AddOp(fpToSi);
                    valueMap[castOp.Result] = fpToSi.Result;
                  } else if (input is StdBool boolInput) {
                    // Bool to int: bool is already 0 or 1, reinterpret as i64
                    valueMap[castOp.Result] = new StdI64(boolInput.Id);
                  } else if (input is StdPtr) {
                    throw new InvalidOperationException("Cannot cast pointer to int (use explicit ptr_to_i64 operation)");
                  } else {
                    throw new InvalidOperationException($"Unsupported cast to int from: {input.GetType().Name}");
                  }
                  break;
                }
                case MaxonValueKind.Float: {
                  if (input is StdI64 i64) {
                    var sourceIsUnsigned = castOp.SourceIsUnsigned;
                    if (sourceIsUnsigned) {
                      var uiToFp = new StdUiToFpOp(i64);
                      newBlock.AddOp(uiToFp);
                      valueMap[castOp.Result] = uiToFp.Result;
                    } else {
                      var siToFp = new StdSiToFpOp(i64);
                      newBlock.AddOp(siToFp);
                      valueMap[castOp.Result] = siToFp.Result;
                    }
                  } else if (input is StdI32 i32) {
                    // i32 to float: need to convert i32 to i64 first, then to float
                    throw new InvalidOperationException("i32 to float conversion not yet implemented");
                  } else if (input is StdF64 f64) {
                    valueMap[castOp.Result] = f64;
                  } else if (input is StdF32 f32) {
                    // f32 to f64: widen
                    var promote = new StdF32ToF64Op(f32);
                    newBlock.AddOp(promote);
                    valueMap[castOp.Result] = promote.Result;
                  } else if (input is StdBool boolInput) {
                    // Bool to float: convert bool (0 or 1) to float
                    var asI64 = new StdI64(boolInput.Id);
                    var siToFp = new StdSiToFpOp(asI64);
                    newBlock.AddOp(siToFp);
                    valueMap[castOp.Result] = siToFp.Result;
                  } else if (input is StdPtr) {
                    throw new InvalidOperationException("Cannot cast pointer to float");
                  } else {
                    throw new InvalidOperationException($"Unsupported cast to float from: {input.GetType().Name}");
                  }
                  break;
                }
                case MaxonValueKind.Float32: {
                  if (input is StdI64 i64) {
                    var sourceIsUnsigned = castOp.SourceIsUnsigned;
                    if (sourceIsUnsigned) {
                      var uiToFp = new StdUiToFpF32Op(i64);
                      newBlock.AddOp(uiToFp);
                      valueMap[castOp.Result] = uiToFp.Result;
                    } else {
                      var siToFp = new StdSiToFpF32Op(i64);
                      newBlock.AddOp(siToFp);
                      valueMap[castOp.Result] = siToFp.Result;
                    }
                  } else if (input is StdF32 f32) {
                    valueMap[castOp.Result] = f32;
                  } else if (input is StdF64 f64) {
                    // f64 to f32: narrow
                    var narrow = new StdF64ToF32Op(f64);
                    newBlock.AddOp(narrow);
                    valueMap[castOp.Result] = narrow.Result;
                  } else if (input is StdBool boolInput) {
                    var asI64 = new StdI64(boolInput.Id);
                    var siToFp = new StdSiToFpF32Op(asI64);
                    newBlock.AddOp(siToFp);
                    valueMap[castOp.Result] = siToFp.Result;
                  } else {
                    throw new InvalidOperationException($"Unsupported cast to f32 from: {input.GetType().Name}");
                  }
                  break;
                }
                case MaxonValueKind.Short: {
                  // Cast to short: pass through (short uses i64 at standard level)
                  if (input is StdI64 i64) {
                    valueMap[castOp.Result] = i64;
                  } else {
                    throw new InvalidOperationException($"Unsupported cast to short from: {input.GetType().Name}");
                  }
                  break;
                }
                case MaxonValueKind.Bool:
                case MaxonValueKind.Struct:
                case MaxonValueKind.Enum:
                case MaxonValueKind.Function:
                case MaxonValueKind.TypeParameter:
                  throw new InvalidOperationException($"Unsupported cast target kind: {castOp.TargetKind}");
              }
              break;
            }
            case MaxonGlobalLoadOp globalLoad: {
              // Lazy static field: emit guard check and conditional init call
              if (globalLoad.LazyGuardName != null && globalLoad.LazyInitFuncName != null) {
                var guardLoad = new StdGlobalLoadI1Op(globalLoad.LazyGuardName);
                newBlock.AddOp(guardLoad);

                // Branch: if guard is true, skip init; if false, call init
                var initBlockLabel = $"__lazy_init_{globalLoad.Result.Id}";
                var mergeBlockLabel = $"__lazy_merge_{globalLoad.Result.Id}";

                newBlock.AddOp(new StdCondBrOp(guardLoad.Result, mergeBlockLabel, initBlockLabel));

                // Merge block follows immediately (fall-through when guard is true)
                newBlock = newFunc.Body.AddBlock(mergeBlockLabel);

                // Init block: call the lazy init function, then branch to merge
                var initBlock = newFunc.Body.AddBlock(initBlockLabel);
                initBlock.AddOp(new StdCallOp(globalLoad.LazyInitFuncName, []));
                initBlock.AddOp(new StdBrOp(mergeBlockLabel));
              }

              StandardOp loadOp = globalLoad.ValueKind switch {
                MaxonValueKind.Integer or MaxonValueKind.Enum => new StdGlobalLoadI64Op(globalLoad.GlobalName),
                MaxonValueKind.Float => new StdGlobalLoadF64Op(globalLoad.GlobalName),
                MaxonValueKind.Float32 => new StdGlobalLoadF32Op(globalLoad.GlobalName),
                MaxonValueKind.Bool => new StdGlobalLoadI1Op(globalLoad.GlobalName),
                MaxonValueKind.Byte => new StdGlobalLoadI8Op(globalLoad.GlobalName),
                MaxonValueKind.Short => new StdGlobalLoadI16Op(globalLoad.GlobalName),
                MaxonValueKind.Struct => new StdGlobalLoadI64Op(globalLoad.GlobalName),
                MaxonValueKind.Function or MaxonValueKind.TypeParameter or _ =>
                  throw new InvalidOperationException($"Cannot use {globalLoad.ValueKind} as global variable type"),
              };
              newBlock.AddOp(loadOp);
              valueMap[globalLoad.Result] = loadOp switch {
                StdGlobalLoadI64Op i64 => i64.Result,
                StdGlobalLoadF64Op f64 => f64.Result,
                StdGlobalLoadF32Op f32 => f32.Result,
                StdGlobalLoadI1Op i1 => i1.Result,
                StdGlobalLoadI8Op i8 => i8.Result,
                StdGlobalLoadI16Op i16 => i16.Result,
                _ => throw new InvalidOperationException()
              };
              if (globalLoad.ValueKind == MaxonValueKind.Struct) {
                var tempName = $"__global_{globalLoad.GlobalName}_{globalLoad.Result.Id}";
                temps.RegisterTemp(tempName, globalLoad.StructTypeName ?? "unknown", OwnershipFlags.Orphan);
                EmitStore(newBlock, valueMap[globalLoad.Result], tempName, varTypes);
                EmitIncref(newBlock, tempName, varTypes, scopeName: func.Name);
                var globalTypeName = globalLoad.StructTypeName ?? "unknown";
                valueMap[globalLoad.Result] = new StdHeapPtr(globalLoad.Result.Id, globalTypeName, tempName);
                if (globalLoad.StructTypeName != null) {
                  varNameToStructType[tempName] = globalLoad.StructTypeName;
                }
              }
              break;
            }
            case MaxonGlobalStoreOp globalStore: {
              if (globalStore.ValueKind == MaxonValueKind.Struct) {
                // Resolve the new heap pointer -- check StdHeapPtr before StdI64
                // since StdHeapPtr extends StdI64 and needs a load from its temp variable
                StdI64 newHeapPtr;
                if (valueMap.TryGetValue(globalStore.Value, out var mv) && mv is StdHeapPtr srcNameHp) {
                  newHeapPtr = (StdI64)EmitLoad(newBlock, srcNameHp.VarName!, varTypes);
                } else if (mv is StdI64 i64Val) {
                  newHeapPtr = i64Val;
                } else {
                  throw new InvalidOperationException($"Cannot store struct value to global '{globalStore.GlobalName}': no struct tracking info");
                }

                bool isModuleInit = func.Name == "__module_init";
                if (!isModuleInit) {
                  // Decref old global value before storing new one (may be null if not yet assigned).
                  var oldGlobalLoad = new StdGlobalLoadI64Op(globalStore.GlobalName);
                  newBlock.AddOp(oldGlobalLoad);
                  EmitDecrefValueIfNonnull(newBlock, oldGlobalLoad.Result, scopeName: func.Name);
                }

                // Incref the new value — the global holds a reference
                EmitIncrefValue(newBlock, newHeapPtr, scopeName: func.Name);
                newBlock.AddOp(new StdGlobalStoreI64Op(newHeapPtr, globalStore.GlobalName));
              } else {
                var mappedValue = valueMap[globalStore.Value];
                var storeOp = globalStore.ValueKind switch {
                  MaxonValueKind.Integer or MaxonValueKind.Enum =>
                    (StandardOp)new StdGlobalStoreI64Op((StdI64)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Float =>
                    new StdGlobalStoreF64Op((StdF64)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Float32 =>
                    new StdGlobalStoreF32Op((StdF32)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Bool =>
                    new StdGlobalStoreI1Op((StdBool)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Byte =>
                    new StdGlobalStoreI8Op((StdI64)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Short =>
                    new StdGlobalStoreI16Op((StdI64)mappedValue, globalStore.GlobalName),
                  MaxonValueKind.Struct or MaxonValueKind.Function or MaxonValueKind.TypeParameter or _ =>
                    throw new InvalidOperationException($"Cannot use {globalStore.ValueKind} as global variable type"),
                };
                newBlock.AddOp(storeOp);
              }
              break;
            }
            case MaxonTryCallOp tryCallOp:
              LowerTryCall(tryCallOp, funcLookup, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              if (isStructInstanceMethod) {
                selfFieldCache.Clear();
                ReloadSelfFieldLocals(selfStructType!, newBlock, varTypes, selfFieldTempVars);
              }
              break;
            case MaxonAsyncCallOp asyncCallOp:
              LowerAsyncCall(asyncCallOp, newBlock, valueMap, varTypes);
              break;
            case MaxonAwaitOp awaitOp:
              LowerAwait(awaitOp, newBlock, valueMap);
              break;
            case MaxonTryAwaitOp tryAwaitOp:
              LowerTryAwait(tryAwaitOp, newBlock, valueMap);
              break;
            case MaxonCancelPromiseOp cancelOp:
              LowerCancelPromise(cancelOp, newBlock, valueMap);
              break;
            case MaxonCallOp callOp:
              if (TryLowerPrimitiveMethod(callOp, newBlock, valueMap)) break;
              LowerCall(callOp, funcLookup, newBlock, valueMap, varTypes, module.TypeDefs, fnEnvVarNames: fnEnvVarNames, temps: temps);
              // Method calls may mutate self-fields (e.g. grow() reallocates arrays),
              // so cached self-field loads must be invalidated and struct-typed
              // field locals must be reloaded from the self pointer
              if (isStructInstanceMethod) {
                selfFieldCache.Clear();
                ReloadSelfFieldLocals(selfStructType!, newBlock, varTypes, selfFieldTempVars);
              }
              // After a call that passes variables by reference, reload those variables
              // so subsequent uses see the mutated values instead of stale SSA values
              if (callOp.ArgVarNames != null
                  && funcLookup.TryGetValue(callOp.Callee, out var calleeForReload)
                  && calleeForReload.ReassignedParams != null) {
                for (int ai = 0; ai < callOp.Args.Count && ai < callOp.ArgVarNames.Count; ai++) {
                  var argVarName = callOp.ArgVarNames[ai];
                  if (argVarName == null) continue;
                  if (ai >= calleeForReload.ParamNames.Count) continue;
                  var calleeParamName = calleeForReload.ParamNames[ai];
                  if (!calleeForReload.ReassignedParams.Contains(calleeParamName)) continue;
                  if (!varTypes.ContainsKey(argVarName)) continue;
                  // If we forwarded the ref pointer, the callee modified the original location,
                  // not our local copy. Reload the local from the ref pointer first.
                  if (_refParamPtrVars != null && _refParamPtrVars.TryGetValue(argVarName, out var refPtrForReload)) {
                    var refPtr = (StdI64)EmitLoad(newBlock, refPtrForReload, varTypes);
                    var varType = varTypes.TryGetValue(argVarName, out var vt) ? VarTypeToIrType(vt) : IrType.I64;
                    var loadIndirect = new StdLoadIndirectOp(refPtr, 0, varType);
                    newBlock.AddOp(loadIndirect);
                    EmitStore(newBlock, loadIndirect.Result, argVarName, varTypes);
                  }
                  var reloaded = EmitLoad(newBlock, argVarName, varTypes);
                  valueMap[callOp.Args[ai]] = reloaded;
                }
              }
              break;
            case MaxonFunctionRefOp fnRefOp:
              LowerFunctionRef(fnRefOp, newBlock, valueMap);
              break;
            case MaxonClosureCreateOp closureCreateOp:
              LowerClosureCreate(closureCreateOp, newBlock, valueMap, varTypes, fnEnvVarNames, varNameToStructType, temps);
              break;
            case MaxonClosureEnvLoadOp envLoadOp:
              LowerClosureEnvLoad(envLoadOp, newBlock, valueMap, varTypes, temps);
              break;
            case MaxonFunctionParamOp fnParamOp:
              LowerFunctionParam(fnParamOp, newBlock, valueMap, varTypes, fnEnvVarNames, fnEnvDirectValues, paramFlatIndex);
              break;
            case MaxonFunctionVarRefOp fnVarRefOp:
              LowerFunctionVarRef(fnVarRefOp, newBlock, valueMap, varTypes, fnEnvVarNames);
              break;
            case MaxonIndirectCallOp indirectCallOp:
              LowerIndirectCall(indirectCallOp, newBlock, valueMap, varTypes, module.TypeDefs, fnEnvVarNames, fnEnvDirectValues, temps);
              if (isStructInstanceMethod) {
                selfFieldCache.Clear();
                ReloadSelfFieldLocals(selfStructType!, newBlock, varTypes, selfFieldTempVars);
              }
              break;
            case MaxonReturnOp retOp: {
              LowerReturn(retOp, retStructType, newBlock, valueMap, varTypes, module.TypeDefs, func.Name, temps, func.ReturnsSelf);
              break;
            }
            case MaxonThrowOp throwOp: {
              LowerThrow(throwOp, newBlock, valueMap, varTypes, module.TypeDefs);
              break;
            }
            case MaxonManagedMemGetOp memGetOp:
              LowerManagedMemGet(memGetOp, newFunc, ref newBlock, valueMap, varTypes, temps);
              if (valueMap[memGetOp.Result] is StdHeapPtr memGetHp) {
                valueMap[memGetOp.Result] = new StdHeapPtr(memGetOp.Result.Id, memGetHp.TypeName, memGetHp.VarName!);
              }
              break;
            case MaxonManagedMemRemoveOp memRemoveOp:
              LowerManagedMemRemove(memRemoveOp, newFunc, ref newBlock, valueMap, varTypes, temps);
              if (valueMap[memRemoveOp.Result] is StdHeapPtr memRemoveHp) {
                valueMap[memRemoveOp.Result] = new StdHeapPtr(memRemoveOp.Result.Id, memRemoveHp.TypeName, memRemoveHp.VarName!);
              }
              break;
            case MaxonManagedMemSetOp memSetOp:
              LowerManagedMemSet(memSetOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedMemCreateOp memCreateOp:
              LowerManagedMemCreate(memCreateOp, newBlock, valueMap, varTypes, temps,
                inlineTargets.GetValueOrDefault(memCreateOp.Result.Id));
              if (valueMap[memCreateOp.Result] is StdHeapPtr memCreateHp) {
                valueMap[memCreateOp.Result] = new StdHeapPtr(memCreateOp.Result.Id, memCreateHp.TypeName, memCreateHp.VarName!);
              }
              break;
            case MaxonManagedMemGrowOp memGrowOp:
              LowerManagedMemGrow(memGrowOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedMemSetLengthOp setLenOp:
              LowerManagedMemSetLength(setLenOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedMemClearOp memClearOp:
              LowerManagedMemClear(memClearOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedMemShiftOp memShiftOp:
              LowerManagedMemShift(memShiftOp, newFunc, ref newBlock, valueMap, varTypes);
              break;
            case MaxonManagedMemByteGetOp byteGetOp:
              LowerManagedMemByteGet(byteGetOp, newBlock, valueMap, varTypes);
              break;
            case MaxonUcdByteLoadOp ucdByteOp:
              LowerUcdByteLoad(ucdByteOp, newBlock, valueMap, result);
              break;
            case MaxonUcdI64LoadOp ucdI64Op:
              LowerUcdI64Load(ucdI64Op, newBlock, valueMap, result);
              break;
            case MaxonManagedMemByteSetOp byteSetOp:
              LowerManagedMemByteSet(byteSetOp, newBlock, valueMap, varTypes);
              break;
            case MaxonCStringToManagedOp fromCStringOp:
              LowerCStringToManaged(fromCStringOp, newBlock, valueMap, varTypes, temps,
                inlineTargets.GetValueOrDefault(fromCStringOp.Result.Id));
              if (valueMap[fromCStringOp.Result] is StdHeapPtr fromCStringHp) {
                valueMap[fromCStringOp.Result] = new StdHeapPtr(fromCStringOp.Result.Id, fromCStringHp.TypeName, fromCStringHp.VarName!);
              }
              break;
            case MaxonManagedToCStringOp toCStringOp:
              LowerManagedToCString(toCStringOp, newFunc, ref newBlock, valueMap, varTypes);
              break;
            case MaxonManagedWriteStdoutOp managedWriteStdoutOp:
              LowerManagedWriteStdout(managedWriteStdoutOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedWriteStderrOp managedWriteStderrOp:
              LowerManagedWriteStderr(managedWriteStderrOp, newBlock, valueMap, varTypes);
              break;
            case MaxonPanicOp panicOp:
              LowerPanic(panicOp, newBlock, result);
              break;
            case MaxonPanicDynamicOp panicDynOp:
              LowerPanicDynamic(panicDynOp, newBlock, valueMap, varTypes);
              break;
            case MaxonStringLiteralOp stringLitOp:
              LowerStringLiteral(stringLitOp, newBlock, valueMap, varTypes, result, temps,
                inlineTargets.GetValueOrDefault(stringLitOp.Result.Id));
              break;
            case MaxonByteStringLiteralOp byteStringLitOp:
              LowerByteStringLiteral(byteStringLitOp, newBlock, valueMap, varTypes, result, temps,
                inlineTargets.GetValueOrDefault(byteStringLitOp.Result.Id));
              break;
            case MaxonCharLiteralOp charLitOp:
              LowerCharLiteral(charLitOp, newBlock, valueMap, varTypes, result, temps,
                inlineTargets.GetValueOrDefault(charLitOp.Result.Id));
              break;
            case MaxonStringInterpOp interpOp:
              LowerStringInterp(interpOp, newBlock, valueMap, varTypes, result, temps,
                inlineTargets.GetValueOrDefault(interpOp.Result.Id));
              break;
            case MaxonManagedMemAppendOp memAppendOp:
              LowerManagedMemAppend(memAppendOp, newFunc, ref newBlock, valueMap, varTypes);
              break;
            case MaxonManagedMemSliceOp sliceOp:
              LowerManagedMemSlice(sliceOp, newFunc, ref newBlock, valueMap, varTypes, temps,
                inlineTargets.GetValueOrDefault(sliceOp.Result.Id));
              break;
            case MaxonMakeCharFromBytesOp makeCharOp:
              LowerMakeCharFromBytes(makeCharOp, newBlock, valueMap, varTypes, temps);
              break;
            // __ManagedMemoryCursor operations (non-throwing — throwing ops go through MaxonCallOp)
            case MaxonCursorCurrentOp cursorCurrentOp:
              LowerCursorCurrent(cursorCurrentOp, newBlock, valueMap, varTypes, temps);
              break;
            case MaxonCursorIndexOp cursorIndexOp:
              LowerCursorIndex(cursorIndexOp, newBlock, valueMap, varTypes);
              break;
            case MaxonCallRuntimeOp callRtOp: {
              var stdArgs = callRtOp.Args.Select(a => {
                if (valueMap.TryGetValue(a, out var mapped)) {
                  if (mapped is StdHeapPtr hp && hp.VarName != null) {
                    var typeName = hp.TypeName;
                    // Load buffer from managed struct via heap pointer indirection
                    if (TypeAliasInfo.IsManagedMemoryType(typeName, module.TypeAliasSources)) {
                      // hp.VarName IS the __ManagedMemory heap pointer, buffer at offset 0
                      return (StdValue)(StdI64)EmitStructFieldLoad(newBlock, hp.VarName, ManagedFieldBuffer, IrType.I64, varTypes);
                    } else if (typeName == "__ManagedFile") {
                      // Pass the __ManagedFile heap pointer itself; runtime (maxon_file_close)
                      // reads _handle at offset 0 and zeros it before submitting close.
                      return (StdValue)(StdI64)EmitLoad(newBlock, hp.VarName, varTypes);
                    } else {
                      throw new InvalidOperationException(
                        $"MaxonCallRuntimeOp struct arg has unexpected type '{typeName}' -- " +
                        "only __ManagedMemory struct args are supported (extract fields before passing to runtime calls)");
                    }
                  }
                  return (StdValue)(StdI64)mapped;
                }
                throw new InvalidOperationException($"MaxonCallRuntimeOp arg {a} not found in valueMap");
              }).ToList();
              // When tracing, mm_free/mm_raw_free take 2 params (ptr, scope) — add NULL scope if caller only passes ptr
              if (Compiler.MmTrace && (callRtOp.FunctionName == "mm_free" || callRtOp.FunctionName == "mm_raw_free") && stdArgs.Count == 1) {
                var nullScope = new StdConstI64Op(0);
                newBlock.AddOp(nullScope);
                stdArgs.Add(nullScope.Result);
              }
              if (callRtOp.Result != null) {
                var rtResult = new StdI64(IrContext.Current.NextId());
                newBlock.AddOp(new StdCallRuntimeOp(callRtOp.FunctionName, stdArgs, rtResult));
                valueMap[callRtOp.Result] = rtResult;
              } else {
                newBlock.AddOp(new StdCallRuntimeOp(callRtOp.FunctionName, stdArgs, null));
              }
              break;
            }
            // ManagedList (doubly-linked list) operations
            case MaxonManagedListCreateOp managedListCreateOp:
              LowerManagedListCreate(managedListCreateOp, newBlock, valueMap, varTypes, temps,
                inlineTargets.GetValueOrDefault(managedListCreateOp.Result.Id));
              break;
            case MaxonManagedListInsertValueOp managedListInsertOp:
              LowerManagedListInsertValue(managedListInsertOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListInsertRelativeValueOp managedListInsertRelOp:
              LowerManagedListInsertRelativeValue(managedListInsertRelOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListReinsertOp managedListReinsertOp:
              LowerManagedListReinsert(managedListReinsertOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedListReinsertRelativeOp managedListReinsertRelOp:
              LowerManagedListReinsertRelative(managedListReinsertRelOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedListDetachOp managedListDetachOp:
              LowerManagedListDetach(managedListDetachOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedListRemoveOp managedListRemoveOp:
              LowerManagedListRemove(managedListRemoveOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListCountOp managedListCountOp:
              LowerManagedListCount(managedListCountOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedListNodeValueOp managedListNodeValueOp:
              LowerManagedListNodeValue(managedListNodeValueOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListNodeSetValueOp managedListNodeSetValueOp:
              LowerManagedListNodeSetValue(managedListNodeSetValueOp, newBlock, valueMap, varTypes, module.TypeDefs);
              break;
            case MaxonManagedListClearOp managedListClearOp:
              LowerManagedListClear(managedListClearOp, newBlock, valueMap, varTypes, module.TypeDefs);
              break;
            case MaxonManagedListCursorResetOp cursorResetOp:
              LowerManagedListCursorReset(cursorResetOp, newBlock, valueMap, varTypes);
              break;
            case MaxonManagedListCursorValueOp cursorValueOp:
              LowerManagedListCursorValue(cursorValueOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            case MaxonManagedListHeadPtrOp headPtrOp:
              LowerManagedListHeadPtr(headPtrOp, newBlock, valueMap, varTypes, temps);
              break;
            case MaxonManagedListNodePtrNextOp nodePtrNextOp:
              LowerManagedListNodePtrNext(nodePtrNextOp, newBlock, valueMap, varTypes, temps);
              break;
            case MaxonManagedListNodePtrValueOp nodePtrValueOp:
              LowerManagedListNodePtrValue(nodePtrValueOp, newBlock, valueMap, varTypes, module.TypeDefs, temps);
              break;
            default:
              throw new InvalidOperationException($"No MaxonToStandard conversion for: {op.GetType().Name} ({op.Mnemonic})");
          }
        }
      }

      // Zero-initialize stack slots for all vars that scope_end will decref,
      // so paths that skip the scope (e.g. untaken if-branches) see NULL.
      var allScopeVars = new HashSet<string>();
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonScopeEndOp seo) {
            foreach (var v in seo.VarsToClean)
              allScopeVars.Add(v);
          }
        }
      }
      // Zero-initialize all managed scope vars and orphan temps in the entry block
      // so that unreached conditional paths see NULL instead of garbage
      var orphanTempNames = temps.OrphanTemps.ToHashSet();
      foreach (var orphan in orphanTempNames)
        allScopeVars.Add(orphan);
      if (allScopeVars.Count > 0 && newFunc.Body.Blocks.Count > 0) {
        var entryBlock = newFunc.Body.Blocks[0];
        int insertIdx = 0;
        foreach (var v in allScopeVars) {
          if (_structParamNames != null && _structParamNames.Contains(v)) continue;
          if (IsSelfField(isStructInstanceMethod, selfStructType, v)) continue;
          if (!varNameToStructType.ContainsKey(v) && !temps.IsTempManaged(v)) continue;  // only managed vars need zeroing
          var zeroOp = new StdConstI64Op(0);
          var storeOp = new StdStoreI64Op(zeroOp.Result, v);
          entryBlock.Operations.Insert(insertIdx, zeroOp);
          insertIdx++;
          entryBlock.Operations.Insert(insertIdx, storeOp);
          insertIdx++;
        }
      }

      result.AddFunction(newFunc);
    }

    // Generate __maxon_global_cleanup to release module-level struct variables at exit
    GenerateGlobalCleanup(module, result);

    // Generate per-element-type destructor functions for containers whose elements
    // Generate per-type destructor functions (called by mm_decref when rc reaches 0)
    GenerateTypeDestructors(result);

    // Build tag table for mm-trace (maps tag_index -> symdata label)
    EmitTagTable(result);

    return result;
  }

  private static void GenerateGlobalCleanup(IrModule<MaxonOp> module, IrModule<StandardOp> result) {
    // Only generate if there are non-lazy struct globals to clean up
    bool hasEagerStructGlobals = module.GlobalVarInfos.Any(kv =>
      kv.Value.Kind == MaxonValueKind.Struct && kv.Value.TypeName != null && !kv.Value.IsLazy);
    bool hasLazyStructGlobals = module.GlobalVarInfos.Any(kv =>
      kv.Value.Kind == MaxonValueKind.Struct && kv.Value.TypeName != null && kv.Value.IsLazy);
    if (!hasEagerStructGlobals && !hasLazyStructGlobals) return;

    var cleanupFunc = new IrFunction<StandardOp>("__maxon_global_cleanup", [], [], null, null);
    var block = cleanupFunc.Body.AddBlock("entry");

    foreach (var (varName, meta) in module.GlobalVarInfos) {
      if (meta.Kind != MaxonValueKind.Struct) continue;
      if (meta.TypeName == null) continue;

      if (meta.IsLazy) {
        // Only decref lazy statics that were actually initialized
        var guardName = $"{varName}.__initialized";
        var guardLoad = new StdGlobalLoadI1Op(guardName);
        block.AddOp(guardLoad);
        var skipLabel = $"__cleanup_skip_{varName.Replace('.', '_')}";
        var cleanupLabel = $"__cleanup_{varName.Replace('.', '_')}";
        block.AddOp(new StdCondBrOp(guardLoad.Result, cleanupLabel, skipLabel));
        block = cleanupFunc.Body.AddBlock(cleanupLabel);
        var globalLoad = new StdGlobalLoadI64Op(varName);
        block.AddOp(globalLoad);
        EmitDecrefValueIfNonnull(block, globalLoad.Result, scopeName: "__maxon_global_cleanup");
        block.AddOp(new StdBrOp(skipLabel));
        block = cleanupFunc.Body.AddBlock(skipLabel);
      } else {
        var globalLoad = new StdGlobalLoadI64Op(varName);
        block.AddOp(globalLoad);
        EmitDecrefValueIfNonnull(block, globalLoad.Result, scopeName: "__maxon_global_cleanup");
      }
    }

    block.AddOp(new StdReturnOp(null));
    result.AddFunction(cleanupFunc);
  }

  /// <summary>
  /// Checks if a type that uses an Element type parameter has a resolved, heap-allocated
  /// Element type. First checks the type's own TypeParams, then falls back to searching
  /// wrapper types that contain this type as a field.
  /// </summary>
  /// Resolves an IrType through TypeDefs to get the canonical definition,
  /// catching stale placeholders (e.g., IrStructType registered for a ranged primitive).
  private static IrType ResolveCanonicalType(IrType type) {
    return _resultModule!.TypeDefs.TryGetValue(type.Name, out var canonical) ? canonical : type;
  }

  private static bool HasManagedElementType(string typeName, IrStructType resolved) {
    var typeAliasSources = _resultModule!.TypeAliasSources;

    // First try the type's own TypeParams (may be resolved directly)
    if (typeAliasSources.TryGetValue(typeName, out var aliasInfo)
        && aliasInfo.TypeParams != null
        && aliasInfo.TypeParams.TryGetValue("Element", out var aliasElemType)
        && aliasElemType is not IrTypeParameterType) {
      if (ResolveCanonicalType(aliasElemType).IsHeapAllocated) return true;
    }
    if (resolved.TypeParams.TryGetValue("Element", out var selfElemType)
        && selfElemType is not IrTypeParameterType) {
      if (ResolveCanonicalType(selfElemType).IsHeapAllocated) return true;
    }

    // Fall back: find the managed memory alias's element type from alias sources
    // (e.g., ByteMemory -> __ManagedMemory with Byte -> Element = Byte)
    if (typeAliasSources.TryGetValue(typeName, out var mmAlias) && mmAlias.TypeParams != null) {
      foreach (var (_, paramType) in mmAlias.TypeParams) {
        if (paramType is not IrTypeParameterType) {
          if (ResolveCanonicalType(paramType).IsHeapAllocated) return true;
        }
      }
    }
    return false;
  }

  /// <summary>
  /// Registers a type for destructor generation. Looks up the type in typeDefs and
  /// records its managed fields so a destructor function can be synthesized.
  /// </summary>
  private static void RegisterTypeForDestructor(string typeName) {
    var typeDefs = _resultModule!.TypeDefs;
    var typeAliasSources = _resultModule!.TypeAliasSources;
    _destructorRequests ??= [];
    if (_destructorRequests.ContainsKey(typeName)) return;

    if (!typeDefs.TryGetValue(typeName, out var typeDef)) return;

    // These types have hand-written runtime destructors — skip synthesis
    // to avoid emitting a duplicate (no-op) synthesized destructor that
    // would shadow the real one.
    if (typeName is "__ManagedSocket" or "__ManagedDirectory" or "__ManagedFile") return;

    if (typeDef is IrStructType structType) {
      var resolved = ResolveStructType(structType, typeDefs);
      bool isManagedMemory = TypeAliasInfo.IsManagedMemoryType(typeName, typeAliasSources);
      bool isManagedList = TypeAliasInfo.IsManagedListType(typeName, typeAliasSources);

      // __ManagedList types: destructor calls managed_list_clear or managed_list_clear_managed to walk nodes.
      // managed_list_clear_managed decrefs each node's value before decrefing the node itself.
      if (isManagedList) {
        bool hasManagedElems = HasManagedElementType(typeName, resolved);
        var clearFunc = hasManagedElems ? "maxon_managed_list_clear_managed" : "maxon_managed_list_clear";
        _destructorRequests[typeName] = new DestructorRequest(typeName, [], ManagedListClearFunc: clearFunc);
        return;
      }

      // Check if this __ManagedMemory type holds heap-allocated elements
      bool needsManagedElementCleanup = isManagedMemory && HasManagedElementType(typeName, resolved);

      // Safety cross-check: verify element type is genuinely heap-allocated after
      // resolving through TypeDefs (catches stale placeholders like RegInt registered
      // as IrStructType instead of IrRangedPrimitiveType).
      if (needsManagedElementCleanup && isManagedMemory) {
        IrType? elemType = null;
        if (typeAliasSources.TryGetValue(typeName, out var mmInfo) && mmInfo.TypeParams != null
            && mmInfo.TypeParams.TryGetValue("Element", out var et))
          elemType = et;
        if (elemType == null && resolved.TypeParams.TryGetValue("Element", out var selfEt))
          elemType = selfEt;
        if (elemType != null && !ResolveCanonicalType(elemType).IsHeapAllocated) {
          Logger.Debug(LogCategory.Ir, $"  Destructor safety: overriding NeedsManagedElementCleanup for {typeName} " +
            $"(Element '{elemType.Name}' resolves to {ResolveCanonicalType(elemType).GetType().Name}, IsHeapAllocated=false)");
          needsManagedElementCleanup = false;
        }
      }

      bool isManagedCursor = TypeAliasInfo.IsManagedCursorType(typeName, typeAliasSources);

      var managedFields = new List<(int Offset, string FieldTypeName, bool IsRawBuffer)>();
      foreach (var field in resolved.Fields) {
        if (IsFieldHeapAllocated(field, typeDefs)) {
          var fieldTypeName = (field.Type as IrStructType)?.Name ?? field.Type.Name;
          managedFields.Add((field.Offset, fieldTypeName, false));
        } else if (isManagedMemory && field.Name == "buffer") {
          // __ManagedMemory.buffer is a raw pointer (I64) that needs mm_raw_free
          managedFields.Add((field.Offset, "raw_buffer", true));
        } else if (isManagedCursor && field.Name == "source_ptr") {
          // __ManagedMemoryCursor.source_ptr is a heap pointer to the source __ManagedMemory that needs mm_decref
          managedFields.Add((field.Offset, "__ManagedMemory", false));
        }
      }
      _destructorRequests[typeName] = new DestructorRequest(typeName, managedFields,
        NeedsManagedElementCleanup: needsManagedElementCleanup);
    } else if (typeDef is IrEnumType enumType && enumType.HasAssociatedValues) {
      // Enum types with associated values — the destructor dispatches on tag
      _destructorRequests[typeName] = new DestructorRequest(typeName, []);
    }
  }

  /// <summary>
  /// Generates destructor functions for all registered types. Each destructor takes a
  /// raw user pointer and mm_decrefs all managed fields. Called by mm_decref when rc reaches 0.
  /// </summary>
  private static void GenerateTypeDestructors(IrModule<StandardOp> result) {
    if (_destructorRequests == null || _destructorRequests.Count == 0) return;

    foreach (var (typeName, request) in _destructorRequests) {
      var destructorName = $"__destruct_{typeName}";
      var func = new IrFunction<StandardOp>(destructorName, ["ptr"], [IrType.I64], null, null);
      var entry = func.Body.AddBlock("entry");

      var paramOp = new StdParamOp(0, "ptr", new StdI64(IrContext.Current.NextId()));
      entry.AddOp(paramOp);
      var ptr = (StdI64)paramOp.Result;
      entry.AddOp(new StdStoreI64Op(ptr, "__destr_ptr"));

      if (result.TypeDefs.TryGetValue(typeName, out var typeDef) && typeDef is IrEnumType enumType && enumType.HasAssociatedValues) {
        // Enum destructor: load tag, dispatch to per-case cleanup
        // For each case with managed payloads, check tag and mm_decref them
        for (int ci = 0; ci < enumType.Cases.Count; ci++) {
          var caseInfo = enumType.Cases[ci];
          var managedPayloads = new List<(int slotIndex, IrType type)>();
          if (caseInfo.AssociatedValues != null) {
            for (int pi = 0; pi < caseInfo.AssociatedValues.Count; pi++) {
              if (caseInfo.AssociatedValues[pi].Type.IsHeapAllocated)
                managedPayloads.Add((pi, caseInfo.AssociatedValues[pi].Type));
            }
          }
          if (managedPayloads.Count == 0) continue;

          // Re-load tag in each check block to avoid cross-block value references
          var ptrLoad = new StdLoadI64Op("__destr_ptr");
          entry.AddOp(ptrLoad);
          var tagLoad = new StdLoadIndirectOp(ptrLoad.Result, 0, IrType.I64);
          entry.AddOp(tagLoad);
          var tagConst = new StdConstI64Op(ci);
          entry.AddOp(tagConst);
          var tagCmp = new StdCmpI64Op("eq", (StdI64)tagLoad.Result, tagConst.Result);
          entry.AddOp(tagCmp);
          var caseBlock = $"case_{ci}";
          var nextBlock = ci < enumType.Cases.Count - 1 ? $"check_{ci + 1}" : "done";
          entry.AddOp(new StdCondBrOp(tagCmp.Result, caseBlock, nextBlock));

          var caseBody = func.Body.AddBlock(caseBlock);
          foreach (var (slotIndex, _) in managedPayloads) {
            var casePtr = new StdLoadI64Op("__destr_ptr");
            caseBody.AddOp(casePtr);
            int byteOffset = 8 + slotIndex * 8;
            var payloadLoad = new StdLoadIndirectOp(casePtr.Result, byteOffset, IrType.I64);
            caseBody.AddOp(payloadLoad);
            EmitDecrefValueIfNonnull(caseBody, (StdI64)payloadLoad.Result, $"~{typeName}");
          }
          caseBody.AddOp(new StdBrOp("done"));

          // Continue checking for next case
          if (ci < enumType.Cases.Count - 1) {
            entry = func.Body.AddBlock(nextBlock);
          }
        }

        // If we fell through all cases without a match, jump to done
        if (entry.Operations.Count == 0 || entry.Operations[^1] is not StdBrOp and not StdCondBrOp) {
          entry.AddOp(new StdBrOp("done"));
        }
      } else if (request.ManagedListClearFunc != null) {
        // ManagedList destructor: call managed_list_clear or managed_list_clear_managed to walk and free all nodes
        var managedListPtr = new StdLoadI64Op("__destr_ptr");
        entry.AddOp(managedListPtr);
        entry.AddOp(new StdCallRuntimeOp(request.ManagedListClearFunc, [managedListPtr.Result], null));
        entry.AddOp(new StdBrOp("done"));
      } else {
        // Struct destructor: mm_decref each managed field, mm_raw_free raw buffers
        var destructorScope = $"~{typeName}";

        int fieldIdx = 0;
        foreach (var (offset, fieldTypeName, isRawBuffer) in request.ManagedFields) {
          var fieldPtrLoad = new StdLoadI64Op("__destr_ptr");
          entry.AddOp(fieldPtrLoad);
          var fieldLoad = new StdLoadIndirectOp(fieldPtrLoad.Result, offset, IrType.I64);
          entry.AddOp(fieldLoad);
          if (isRawBuffer) {
            // Raw buffer inside __ManagedMemory: three modes based on capacity
            //   capacity == -1  (slice): mm_decref(parentPtr) — buffer belongs to parent
            //   capacity == -2  (rdata): nothing — static data, no cleanup
            //   capacity >= 0   (owned): mm_raw_free(buffer) — we own the buffer
            var capPtrLoad = new StdLoadI64Op("__destr_ptr");
            entry.AddOp(capPtrLoad);
            var capLoad = new StdLoadIndirectOp(capPtrLoad.Result, ManagedFieldCapacity, IrType.I64);
            entry.AddOp(capLoad);

            // Check for slice mode: capacity == -1
            var negOne = new StdConstI64Op(-1);
            entry.AddOp(negOne);
            var isSlice = new StdCmpI64Op("eq", (StdI64)capLoad.Result, negOne.Result);
            entry.AddOp(isSlice);
            var sliceCleanupBlock = $"slice_cleanup_{fieldIdx}";
            var checkOwnedBlock = $"check_owned_{fieldIdx}";
            var skipBlock = $"skip_buf_{fieldIdx}";
            entry.AddOp(new StdCondBrOp(isSlice.Result, sliceCleanupBlock, checkOwnedBlock));

            // Slice cleanup: mm_decref(parentPtr)
            var sliceBody = func.Body.AddBlock(sliceCleanupBlock);
            var parentPtrLoad = new StdLoadI64Op("__destr_ptr");
            sliceBody.AddOp(parentPtrLoad);
            var parentLoad = new StdLoadIndirectOp(parentPtrLoad.Result, ManagedFieldParentPtr, IrType.I64);
            sliceBody.AddOp(parentLoad);
            EmitDecrefValueIfNonnull(sliceBody, (StdI64)parentLoad.Result, $"~{typeName}");
            sliceBody.AddOp(new StdBrOp(skipBlock));

            // Check owned mode: capacity != -2 (rdata sentinel)
            var ownedEntry = func.Body.AddBlock(checkOwnedBlock);
            var capReload = new StdLoadI64Op("__destr_ptr");
            ownedEntry.AddOp(capReload);
            var capReloadVal = new StdLoadIndirectOp(capReload.Result, ManagedFieldCapacity, IrType.I64);
            ownedEntry.AddOp(capReloadVal);
            var negTwo = new StdConstI64Op(-2);
            ownedEntry.AddOp(negTwo);
            var capNeRdata = new StdCmpI64Op("ne", (StdI64)capReloadVal.Result, negTwo.Result);
            ownedEntry.AddOp(capNeRdata);
            var freeBlock = $"free_buf_{fieldIdx}";
            ownedEntry.AddOp(new StdCondBrOp(capNeRdata.Result, freeBlock, skipBlock));

            var freeBody = func.Body.AddBlock(freeBlock);

            // Heap-backed buffer with managed elements: decref each element
            // before freeing (COW copy owns its own element references)
            if (request.NeedsManagedElementCleanup) {
              var selfPtr = new StdLoadI64Op("__destr_ptr");
              freeBody.AddOp(selfPtr);
              freeBody.AddOp(new StdCallRuntimeOp("mm_decref_managed_elements", [selfPtr.Result], null));
            }

            var bufPtrLoad = new StdLoadI64Op("__destr_ptr");
            freeBody.AddOp(bufPtrLoad);
            var bufLoad = new StdLoadIndirectOp(bufPtrLoad.Result, offset, IrType.I64);
            freeBody.AddOp(bufLoad);
            if (Compiler.MmTrace) {
              var nullScope = new StdConstI64Op(0);
              freeBody.AddOp(nullScope);
              freeBody.AddOp(new StdCallRuntimeOp("mm_raw_free", [bufLoad.Result, nullScope.Result], null));
            } else {
              freeBody.AddOp(new StdCallRuntimeOp("mm_raw_free", [bufLoad.Result], null));
            }
            freeBody.AddOp(new StdBrOp(skipBlock));

            entry = func.Body.AddBlock(skipBlock);
          } else {
            // Heap-allocated field: decref triggers the field's own destructor
            // (self-contained cleanup — managed element fields handle their own buffer walk)
            EmitDecrefValueIfNonnull(entry, (StdI64)fieldLoad.Result, destructorScope);
          }
          fieldIdx++;
        }
        entry.AddOp(new StdBrOp("done"));
      }

      // done: return
      var done = func.Body.AddBlock("done");
      done.AddOp(new StdReturnOp(null));

      result.AddFunction(func);
    }
  }

  private static void EnsureUcddataLoaded(string label, IrModule<StandardOp> module) {
    if (_loadedUcdLabels!.Contains(label)) return;
    if (module.UcddataEntries.Any(e => e.label == label)) {
      _loadedUcdLabels.Add(label);
      return;
    }
    var binName = label.TrimStart('_') + ".bin";
    var stdlibPath = StdlibLoader.FindStdlibPath() ?? throw new InvalidOperationException($"Cannot find stdlib path for ucd data '{label}'");
    var binPath = Path.Combine(stdlibPath, "helpers", "string", binName);
    if (!File.Exists(binPath)) throw new InvalidOperationException($"UCD binary file not found: {binPath}");
    module.UcddataEntries.Add((label, File.ReadAllBytes(binPath), 8));
    _loadedUcdLabels.Add(label);
  }

  private static void LowerUcdByteLoad(MaxonUcdByteLoadOp op, IrBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap, IrModule<StandardOp> result) {
    EnsureUcddataLoaded(op.UcddataLabel, result);
    var leaOp = new StdLeaUcddataOp(op.UcddataLabel);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);
    var index = (StdI64)valueMap[op.ByteOffset];
    var addrOp = new StdAddI64Op(ptrOp.Result, index);
    block.AddOp(addrOp);
    var loadOp = new StdLoadIndirectOp(addrOp.Result, 0, IrType.I8);
    block.AddOp(loadOp);
    valueMap[op.Result] = loadOp.Result;
  }

  private static void LowerUcdI64Load(MaxonUcdI64LoadOp op, IrBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap, IrModule<StandardOp> result) {
    EnsureUcddataLoaded(op.UcddataLabel, result);
    var leaOp = new StdLeaUcddataOp(op.UcddataLabel);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);
    var index = (StdI64)valueMap[op.Index];
    var scaleOp = new StdConstI64Op(8);
    block.AddOp(scaleOp);
    var byteOffOp = new StdMulI64Op(index, scaleOp.Result);
    block.AddOp(byteOffOp);
    var addrOp = new StdAddI64Op(ptrOp.Result, byteOffOp.Result);
    block.AddOp(addrOp);
    var loadOp = new StdLoadIndirectOp(addrOp.Result, 0, IrType.I64);
    block.AddOp(loadOp);
    valueMap[op.Result] = loadOp.Result;
  }

}
