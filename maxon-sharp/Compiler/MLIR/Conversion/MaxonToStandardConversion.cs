using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static partial class MaxonToStandardConversion {
  [ThreadStatic] private static MlirModule<StandardOp>? _resultModule;
  [ThreadStatic] private static Dictionary<string, string>? _rdataStringCache;
  [ThreadStatic] private static Dictionary<string, HashSet<string>>? _mutatingParams;
  // Maps param name -> ref pointer var name for the current function being lowered
  [ThreadStatic] private static Dictionary<string, string>? _refParamPtrVars;
  // Scope analysis stack for the current function being lowered
  [ThreadStatic] private static List<ScopeInfo?>? _scopeAnalysisStack;
  [ThreadStatic] private static Dictionary<string, ScopeInfo>? _funcScopeAnalysis;
  [ThreadStatic] private static int _nextRdataId;
  [ThreadStatic] private static int _nextStdlibRdataId;
  [ThreadStatic] private static bool _rdataStdlibPhase;
  private static string NextRdataId() =>
    _rdataStdlibPhase ? $"s{_nextStdlibRdataId++}" : $"{_nextRdataId++}";

  public static MlirModule<StandardOp> Run(MlirModule<MaxonOp> module) {
    _rdataStringCache = [];
    _symdataTagCache = [];
    _rdataStdlibPhase = true;
    _nextStdlibRdataId = 0;
    _nextRdataId = 0;
    var result = new MlirModule<StandardOp>();
    _resultModule = result;
    result.RdataEntries.AddRange(module.RdataEntries);
    result.Globals.AddRange(module.Globals);
    foreach (var (k, v) in module.TypeDefs) result.TypeDefs[k] = v;

    // Resolve ranged primitive types so lowering sees base types (i64/f64/i8)
    foreach (var (_, typeDef) in module.TypeDefs) {
      if (typeDef is MlirStructType st)
        foreach (var field in st.Fields)
          field.Type = MlirType.Resolve(field.Type);
    }
    foreach (var func in module.Functions) {
      if (func.ReturnType is MlirRangedPrimitiveType rptRet)
        func.ReturnType = rptRet.OptimalType;
      for (int i = 0; i < func.ParamTypes.Count; i++)
        if (func.ParamTypes[i] is MlirRangedPrimitiveType rptParam)
          func.ParamTypes[i] = rptParam.BaseType;
    }

    // Build a lookup of functions by name for struct-aware call lowering
    var funcLookup = module.Functions.ToDictionary(f => f.Name);

    // Detect mutated params to enable selective pass-by-reference:
    // only params that are assigned to get pointer indirection, others stay by-value.
    var mutatingParams = new Dictionary<string, HashSet<string>>();
    var funcLookupForAnalysis = module.Functions.ToDictionary(f => f.Name);
    // Scan for direct assignments to parameters within each function body
    foreach (var f in module.Functions) {
      var paramNames = new HashSet<string>(f.ParamNames);
      if (paramNames.Count == 0) continue;
      HashSet<string>? mutated = null;
      foreach (var b in f.Body.Blocks) {
        foreach (var op in b.Operations) {
          if (op is MaxonAssignOp assign && !assign.IsDeclaration && paramNames.Contains(assign.VarName)) {
            mutated ??= [];
            mutated.Add(assign.VarName);
          }
        }
      }
      if (mutated != null) {
        mutatingParams[f.Name] = mutated;
        Logger.Debug(LogCategory.Mlir, $"Pass-by-ref: {f.Name} mutates params: {string.Join(", ", mutated)}");
      }
    }
    // Propagate mutations transitively: if F passes its param x to G which mutates it,
    // then F also mutates x (needed so F's callers know to pass x by reference)
    bool changed = true;
    while (changed) {
      changed = false;
      foreach (var f in module.Functions) {
        var paramNames = new HashSet<string>(f.ParamNames);
        if (paramNames.Count == 0) continue;
        foreach (var b in f.Body.Blocks) {
          foreach (var op in b.Operations) {
            if (op is not MaxonCallOp call) continue;
            if (!funcLookupForAnalysis.TryGetValue(call.Callee, out var callee)) continue;
            if (!mutatingParams.TryGetValue(call.Callee, out var calleeMutated)) continue;
            // Check each argument: if it's a var ref to one of F's params,
            // and the callee mutates the corresponding param
            if (call.ArgVarNames == null) continue;
            for (int ci = 0; ci < call.ArgVarNames.Count && ci < callee.ParamNames.Count; ci++) {
              var argVar = call.ArgVarNames[ci];
              if (argVar == null || !paramNames.Contains(argVar)) continue;
              if (!calleeMutated.Contains(callee.ParamNames[ci])) continue;
              // F's param argVar is transitively mutated
              if (!mutatingParams.TryGetValue(f.Name, out var fMutated)) {
                fMutated = [];
                mutatingParams[f.Name] = fMutated;
              }
              if (fMutated.Add(argVar)) changed = true;
            }
          }
        }
      }
    }
    _mutatingParams = mutatingParams;

    bool hasResetAfterStdlib = false;

    foreach (var func in module.Functions) {
      // Reset IDs after stdlib for stable test output
      if (!hasResetAfterStdlib && !func.IsStdlib) {
        MlirContext.Current.ResetIds();
        _rdataStdlibPhase = false;
        _nextRdataId = 0;
        _rdataStringCache = [];
        hasResetAfterStdlib = true;
      }

      var retStructType = ResolveStructReturnType(func.ReturnType, module.TypeDefs);
      var escapingArrayLiterals = FindEscapingArrayLiterals(func);
      var stackAllocIds = new HashSet<int>();

      bool isStructInstanceMethod = IsStructInstanceMethod(func);
      bool isEnumInstanceMethod = IsEnumInstanceMethod(func);
      bool isInstanceMethod = isStructInstanceMethod || isEnumInstanceMethod;
      var selfStructType = isStructInstanceMethod ? ResolveStructType((MlirStructType)func.ParamTypes[0], module.TypeDefs) : null;

      // Only mutated params get pointer indirection; others stay by-value for zero overhead
      var refParamPtrVars = new Dictionary<string, string>();
      if (_mutatingParams != null && _mutatingParams.TryGetValue(func.Name, out var mutatedParamsForSig)) {
        for (int i = 0; i < func.ParamNames.Count; i++) {
          if (func.ParamNames[i] == "self") continue;
          if (mutatedParamsForSig.Contains(func.ParamNames[i])) {
            refParamPtrVars[func.ParamNames[i]] = $"__ref_{func.ParamNames[i]}";
          }
        }
        if (refParamPtrVars.Count > 0)
          Logger.Trace(LogCategory.Mlir, $"Pass-by-ref: {func.Name} receives ref params: {string.Join(", ", refParamPtrVars.Keys)}");
      }

      // Scope analysis for this function (if available)
      module.ScopeAnalysis.TryGetValue(func.Name, out Dictionary<string, ScopeInfo>? funcScopeAnalysis);
      _funcScopeAnalysis = funcScopeAnalysis;
      _scopeAnalysisStack = [];
      module.BlockScopeStacks.TryGetValue(func.Name, out var funcBlockScopeStacks);

      // Collect stack-allocable struct literal IDs and return alloc IDs from scope analysis
      var returnAllocIds = new HashSet<int>();
      if (funcScopeAnalysis != null) {
        foreach (var scopeInfo in funcScopeAnalysis.Values) {
          foreach (var id in scopeInfo.StackAllocIds)
            stackAllocIds.Add(id);
          if (scopeInfo is { CanStaticReturn: true, ReturnAllocResultId: >= 0 })
            returnAllocIds.Add(scopeInfo.ReturnAllocResultId);
        }
      }

      // Build the new function signature:
      // - Struct instance method 'self' param is passed as a heap pointer (i64)
      // - Enum instance method 'self' param is passed as a scalar
      // - Other struct params are passed as heap pointers (i64)
      // - Simple enum params are passed as scalars
      // - Associated-value enum params are passed as heap pointers (i64)
      // - Struct return is an i64 heap pointer returned normally
      var newParamNames = new List<string>();
      var newParamTypes = new List<MlirType>();

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
          newParamTypes.Add(MlirType.I64);
          flatIdx++;
        } else if (isEnumInstanceMethod && i == 0) {
          // Enum instance method self param: pass as scalar
          var enumType = (MlirUnionType)func.ParamTypes[0];
          var backingMlirType = ResolveEnumBackingMlirType(enumType);
          newParamNames.Add("self");
          newParamTypes.Add(backingMlirType);
          flatIdx++;
        } else if (func.ParamTypes[i] is MlirUnionType { HasAssociatedValues: true }) {
          // Associated-value enum param: pass as heap pointer (i64), like structs
          structParamPtrIndex[i] = flatIdx;
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(MlirType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is MlirUnionType enumParamType) {
          // Simple enum param: pass as scalar (or i64 pointer if mutated)
          var backingMlirType = ResolveEnumBackingMlirType(enumParamType);
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(refParamPtrVars.ContainsKey(func.ParamNames[i]) ? MlirType.I64 : backingMlirType);
          flatIdx++;
        } else if (func.ParamTypes[i] is MlirStructType) {
          // Non-self struct param: pass as pointer (i64)
          structParamPtrIndex[i] = flatIdx;
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(MlirType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is MlirFunctionType) {
          // Function-typed param: fn_ptr + hidden env_ptr (2 slots)
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(MlirType.I64);
          flatIdx++;
          newParamNames.Add($"__env_{func.ParamNames[i]}");
          newParamTypes.Add(MlirType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is not MlirStructType and not MlirUnionType) {
          newParamNames.Add(func.ParamNames[i]);
          // Mutated params receive a pointer (i64) instead of the original type
          newParamTypes.Add(refParamPtrVars.ContainsKey(func.ParamNames[i]) ? MlirType.I64 : func.ParamTypes[i]);
          flatIdx++;
        } else {
          throw new InvalidOperationException($"Unhandled parameter type: {func.ParamTypes[i].GetType().Name} for param '{func.ParamNames[i]}'");
        }
      }

      MlirType? newReturnType;
      if (retStructType != null) {
        // Struct return: return heap pointer as i64
        newReturnType = MlirType.I64;
      } else if (func.ReturnType is MlirUnionType { HasAssociatedValues: true }) {
        // Associated-value enum return: return heap pointer as i64
        newReturnType = MlirType.I64;
      } else if (func.ReturnType is MlirUnionType retEnumType) {
        newReturnType = ResolveEnumBackingMlirType(retEnumType);
      } else if (func.ReturnType is not MlirStructType and not MlirUnionType) {
        newReturnType = func.ReturnType;
      } else {
        throw new InvalidOperationException($"Unhandled return type: {func.ReturnType.GetType().Name} in function '{func.Name}'");
      }
      var newFunc = new MlirFunction<StandardOp>(func.Name, newParamNames, newParamTypes, newReturnType, func.ThrowsType) { IsStdlib = func.IsStdlib };
      var valueMap = new Dictionary<MaxonValue, StdValue>();
      var literalMap = new Dictionary<MaxonValue, MaxonLiteralOp>();
      var varTypes = new Dictionary<string, string>();
      // Maps function pointer StdValue IDs to the variable name holding the env_ptr
      var fnEnvVarNames = new Dictionary<int, string>();
      // Direct env_ptr values (avoids store/load when value is already in a register)
      var fnEnvDirectValues = new Dictionary<int, StdValue>();
      // Maps MaxonStruct value IDs to their variable name prefix (for field access)
      var structVarNames = new Dictionary<int, string>();
      // Maps value IDs to their struct type name (for cases where the value is not a MaxonStruct
      // but is semantically a struct, e.g. try_call results from monomorphized generic functions)
      var structValueTypes = new Dictionary<int, string>();
      // Maps variable names to their resolved struct prefix (for cross-block references)
      var varNameToStructPrefix = new Dictionary<string, string>();
      // Maps variable names to their struct type name (for monomorphized type parameter vars)
      var varNameToStructType = new Dictionary<string, string>();
      // Use pre-computed constant array literal metadata from ConstantArrayAnalysisPass
      // Key: struct literal result ID, Value: ConstantArrayLiteralInfo

      _refParamPtrVars = refParamPtrVars;

      // Tracks parameter names (used to distinguish params from locals in some paths)
      var structParamNames = new HashSet<string>();

      // Cache self-field loads: maps "fieldName" → temp var name for struct-typed self fields.
      // Avoids redundant load_indirect from self's heap pointer for the same field within a block.
      // Reset per-block since a cached var stored in one branch may not be defined in another.
      var selfFieldCache = new Dictionary<string, string>();


      foreach (var block in func.Body.Blocks) {
        // Reset scope analysis stack from pre-computed per-block data. Each block's
        // entry scope stack is derived from CFG predecessors, preventing scope_exit
        // ops in one branch from corrupting the stack seen by sibling branches.
        if (funcBlockScopeStacks != null
            && funcBlockScopeStacks.TryGetValue(block.Name, out var blockScopeVars)) {
          _scopeAnalysisStack = [..blockScopeVars
            .Select(sv => funcScopeAnalysis?.GetValueOrDefault(sv))];
        } else {
          _scopeAnalysisStack = [];
        }

        selfFieldCache.Clear();
        var newBlock = newFunc.Body.AddBlock(block.Name);

        // Pre-scan: find struct literals immediately consumed by declaration assigns
        // so we can store fields directly to the target variable.
        // Only for declarations — reassignments need managed cleanup of the old value
        // before the new fields are stored, so they must use an intermediate.
        var structLiteralTargets = new Dictionary<int, string>();
        for (int oi = 0; oi < block.Operations.Count - 1; oi++) {
          if (block.Operations[oi] is MaxonStructLiteralOp lit
            && block.Operations[oi + 1] is MaxonAssignOp assign
            && assign.Value.Id == lit.Result.Id
            && assign.IsDeclaration) {
            structLiteralTargets[lit.Result.Id] = assign.VarName;
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
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(paramOp.Index, paramOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, paramOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, value, varTypes);
                // Dereference to get the initial value
                var loadRef = new StdLoadI64Op(value);
                newBlock.AddOp(loadRef);
                var origType = func.ParamTypes[paramOp.Index];
                var derefType = origType is MlirRangedPrimitiveType rpt ? rpt.BaseType : origType;
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
                var selfPtrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(0, "self", selfPtrVal));
                EmitStore(newBlock, selfPtrVal, "self", varTypes);
                structVarNames[structParamOp.Result.Id] = "self";
                structValueTypes[structParamOp.Result.Id] = structParamOp.StructTypeName;
              } else if (refParamPtrVars.TryGetValue(structParamOp.Name, out string? value)) {
                // Mutated struct param: receive pointer-to-heap-pointer, dereference for local copy
                int ptrFlatIdx = structParamPtrIndex[structParamOp.Index];
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, structParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, value, varTypes);
                // Dereference: load pointer-to-slot, then load the heap pointer from the slot
                var loadRef = new StdLoadI64Op(value);
                newBlock.AddOp(loadRef);
                var deref = new StdLoadIndirectOp(loadRef.Result, 0, MlirType.I64);
                newBlock.AddOp(deref);
                EmitStore(newBlock, (StdI64)deref.Result, structParamOp.Name, varTypes);
                structVarNames[structParamOp.Result.Id] = structParamOp.Name;
                structValueTypes[structParamOp.Result.Id] = structParamOp.StructTypeName;
              } else {
                // Non-self struct param: receive heap pointer, store under the param name
                int ptrFlatIdx = structParamPtrIndex[structParamOp.Index];
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, structParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, structParamOp.Name, varTypes);
                structVarNames[structParamOp.Result.Id] = structParamOp.Name;
                structValueTypes[structParamOp.Result.Id] = structParamOp.StructTypeName;
              }
              structParamNames.Add(structParamOp.Name);
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
              var tempName = $"__enum_{enumConstructOp.Result.Id}";
              var enumTypeDef = (MlirUnionType)module.TypeDefs[enumConstructOp.EnumTypeName];
              int maxPayload = GetMaxFlatPayloadSlots(enumTypeDef);
              int heapSize = 8 + maxPayload * 8;
              var enumPtr = EmitAlloc(newBlock, heapSize, enumConstructOp.EnumTypeName);
              EmitStore(newBlock, enumPtr, tempName, varTypes);

              // Store tag at offset 0
              var tagOp = new StdConstI64Op(enumConstructOp.Ordinal);
              newBlock.AddOp(tagOp);
              newBlock.AddOp(new StdStoreIndirectOp(tagOp.Result, enumPtr, 0, MlirType.I64));

              // Store associated values as payload slots via indirect stores
              int slotIdx = 0;
              for (int ai = 0; ai < enumConstructOp.Args.Count; ai++) {
                int byteOffset = 8 + slotIdx * 8;
                if (structVarNames.TryGetValue(enumConstructOp.Args[ai].Id, out var structSrcName)) {
                  // Heap-pointer payload: store and reparent only if scope-owned.
                  // Parent-owned values (e.g. existing nodes passed as prev pointers)
                  // stay owned by their current parent — avoids circular ownership.
                  var childHeapPtr = (StdI64)EmitLoad(newBlock, structSrcName, varTypes);
                  newBlock.AddOp(new StdStoreIndirectOp(childHeapPtr, enumPtr, byteOffset, MlirType.I64));
                  newBlock.AddOp(new StdCallRuntimeOp("mm_reparent_if_scope_owned", [childHeapPtr, enumPtr], null));
                  slotIdx++;
                } else {
                  // Scalar payload: store directly
                  var argStdVal = valueMap[enumConstructOp.Args[ai]];
                  newBlock.AddOp(new StdStoreIndirectOp(argStdVal, enumPtr, byteOffset, MlirType.I64));
                  slotIdx++;
                }
              }
              // Zero-fill any unused payload slots
              for (int ai = slotIdx; ai < maxPayload; ai++) {
                var zeroOp = new StdConstI64Op(0);
                newBlock.AddOp(zeroOp);
                newBlock.AddOp(new StdStoreIndirectOp(zeroOp.Result, enumPtr, 8 + ai * 8, MlirType.I64));
              }

              structVarNames[enumConstructOp.Result.Id] = tempName;
              structValueTypes[enumConstructOp.Result.Id] = enumConstructOp.EnumTypeName;
              break;
            }
            case MaxonEnumTagOp enumTagOp: {
              if (structVarNames.TryGetValue(enumTagOp.EnumValue.Id, out var enumPrefix)) {
                // Associated-value enum: load tag from heap pointer at offset 0
                var heapPtr = (StdI64)EmitLoad(newBlock, enumPrefix, varTypes);
                var tagLoad = new StdLoadIndirectOp(heapPtr, 0, MlirType.I64);
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
              var enumVarName = structVarNames[enumPayloadOp.EnumValue.Id];
              int flatSlotOffset = enumPayloadOp.PayloadIndex;
              var heapPtr = (StdI64)EmitLoad(newBlock, enumVarName, varTypes);
              int byteOffset = 8 + flatSlotOffset * 8;

              if (enumPayloadOp.ResultKind == MaxonValueKind.Struct && enumPayloadOp.ResultStructTypeName != null) {
                // Struct-typed payload: load heap pointer from payload slot
                var tempStructName = $"__enum_payload_{enumPayloadOp.Result.Id}";
                var loadOp = new StdLoadIndirectOp(heapPtr, byteOffset, MlirType.I64);
                newBlock.AddOp(loadOp);
                EmitStore(newBlock, (StdI64)loadOp.Result, tempStructName, varTypes);
                structVarNames[enumPayloadOp.Result.Id] = tempStructName;
                structValueTypes[enumPayloadOp.Result.Id] = enumPayloadOp.ResultStructTypeName;
              } else if (enumPayloadOp.ResultKind == MaxonValueKind.Enum
                         && enumPayloadOp.ResultStructTypeName != null
                         && module.TypeDefs.TryGetValue(enumPayloadOp.ResultStructTypeName, out var payloadEnumDef)
                         && payloadEnumDef is MlirUnionType payloadEnumType && payloadEnumType.HasAssociatedValues) {
                // Associated-value enum payload: load heap pointer (no unpacking needed)
                var tempName = $"__enum_payload_{enumPayloadOp.Result.Id}";
                var loadOp = new StdLoadIndirectOp(heapPtr, byteOffset, MlirType.I64);
                newBlock.AddOp(loadOp);
                EmitStore(newBlock, (StdI64)loadOp.Result, tempName, varTypes);
                structVarNames[enumPayloadOp.Result.Id] = tempName;
                structValueTypes[enumPayloadOp.Result.Id] = enumPayloadOp.ResultStructTypeName;
              } else {
                var loadOp = new StdLoadIndirectOp(heapPtr, byteOffset, MlirType.I64);
                newBlock.AddOp(loadOp);
                valueMap[enumPayloadOp.Result] = loadOp.Result;
              }
              break;
            }
            case MaxonEnumParamOp enumParamOp: {
              // Check if this is an associated-value enum (passed as heap pointer)
              if (module.TypeDefs.TryGetValue(enumParamOp.EnumTypeName, out var epType)
                  && epType is MlirUnionType epEnumType && epEnumType.HasAssociatedValues) {
                // Receive heap pointer — no unpacking needed, heap pointer IS the enum value
                int ptrFlatIdx = structParamPtrIndex[enumParamOp.Index];
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, enumParamOp.Name, ptrVal));
                if (refParamPtrVars.TryGetValue(enumParamOp.Name, out string? value)) {
                  // Mutated assoc-value enum: receive pointer-to-heap-pointer
                  EmitStore(newBlock, ptrVal, value, varTypes);
                  var loadRef = new StdLoadI64Op(value);
                  newBlock.AddOp(loadRef);
                  var deref = new StdLoadIndirectOp(loadRef.Result, 0, MlirType.I64);
                  newBlock.AddOp(deref);
                  EmitStore(newBlock, (StdI64)deref.Result, enumParamOp.Name, varTypes);
                } else {
                  EmitStore(newBlock, ptrVal, enumParamOp.Name, varTypes);
                }

                structVarNames[enumParamOp.Result.Id] = enumParamOp.Name;
                structValueTypes[enumParamOp.Result.Id] = enumParamOp.EnumTypeName;
              } else if (refParamPtrVars.TryGetValue(enumParamOp.Name, out string? value)) {
                // Mutated simple enum: receive i64 pointer, dereference for local copy
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(enumParamOp.Index, enumParamOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, enumParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, value, varTypes);
                var loadRef = new StdLoadI64Op(value);
                newBlock.AddOp(loadRef);
                var enumBackingType = enumParamOp.BackingKind == MaxonValueKind.Float ? MlirType.F64 : MlirType.I64;
                var deref = new StdLoadIndirectOp(loadRef.Result, 0, enumBackingType);
                newBlock.AddOp(deref);
                valueMap[enumParamOp.Result] = deref.Result;
                EmitStore(newBlock, deref.Result, enumParamOp.Name, varTypes);
              } else if (enumParamOp.BackingKind == MaxonValueKind.Float) {
                var stdResult = new StdF64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(enumParamOp.Index, enumParamOp.Name, stdResult));
                valueMap[enumParamOp.Result] = stdResult;
                EmitStore(newBlock, stdResult, enumParamOp.Name, varTypes);
              } else if (enumParamOp.BackingKind == MaxonValueKind.Integer) {
                var stdResult = new StdI64(MlirContext.Current.NextId());
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
                  && evType is MlirUnionType evEnumType && evEnumType.HasAssociatedValues) {
                // Resolve the struct prefix: either from varNameToStructPrefix or direct varName
                string resolvedPrefix;
                if (varNameToStructPrefix.TryGetValue(enumVarRef.VarName, out var existingPrefix)) {
                  resolvedPrefix = existingPrefix;
                } else {
                  resolvedPrefix = enumVarRef.VarName;
                }
                structVarNames[enumVarRef.Result.Id] = resolvedPrefix;
                structValueTypes[enumVarRef.Result.Id] = enumVarRef.EnumTypeName;
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

              if (structVarNames.TryGetValue(payloadAssign.NewValue.Id, out var newStructSrc)) {
                // Heap-pointer payload: store and reparent only if scope-owned
                var childHeapPtr = (StdI64)EmitLoad(newBlock, newStructSrc, varTypes);
                newBlock.AddOp(new StdStoreIndirectOp(childHeapPtr, enumHeapPtr, byteOffset, MlirType.I64));
                newBlock.AddOp(new StdCallRuntimeOp("mm_reparent_if_scope_owned", [childHeapPtr, enumHeapPtr], null));
              } else {
                var newStdVal = valueMap[payloadAssign.NewValue];
                newBlock.AddOp(new StdStoreIndirectOp(newStdVal, enumHeapPtr, byteOffset, MlirType.I64));
              }
              break;
            }
            case MaxonEnumRawValueOp rawValueOp: {
              // The enum's backing value IS the raw value - just pass through
              var enumStdVal = valueMap[rawValueOp.EnumValue];
              valueMap[rawValueOp.Result] = enumStdVal;
              break;
            }
            case MaxonErrorFlagToEnumOp errToEnumOp: {
              if (errToEnumOp.HasAssociatedValues) {
                // Associated-value error: the error flag IS the heap pointer
                var heapPtr = (StdI64)valueMap[errToEnumOp.ErrorFlag];
                var retVarName = $"__error_enum_{errToEnumOp.Result.Id}";
                EmitStore(newBlock, heapPtr, retVarName, varTypes);
                // No unpacking — heap pointer IS the enum value
                structVarNames[errToEnumOp.Result.Id] = retVarName;
                structValueTypes[errToEnumOp.Result.Id] = errToEnumOp.EnumTypeName;
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
              var enumType = (MlirUnionType)module.TypeDefs[strRawOp.EnumTypeName];
              var ordinalValue = (StdI64)valueMap[strRawOp.EnumValue];
              var (buf, len) = EmitStringUnionToString(enumType, ordinalValue, newBlock, result);
              var tempName = $"__enum_rawval_{strRawOp.Result.Id}";
              var isString = !strRawOp.IsChar;
              EmitManagedStructFromBufLen(tempName, buf, len,
                isString, newBlock, varTypes, structVarNames, strRawOp.Result.Id);
              break;
            }
            case MaxonEnumNameOp enumNameOp: {
              var enumType = (MlirUnionType)module.TypeDefs[enumNameOp.EnumTypeName];
              var stdValue = valueMap[enumNameOp.EnumValue];
              StdI64 ordinalValue;
              if (enumType.BackingType == MlirType.I64) {
                ordinalValue = EmitIntUnionToOrdinal(enumType, (StdI64)stdValue, newBlock);
              } else if (enumType.BackingType == MlirType.F64) {
                ordinalValue = EmitFloatUnionToOrdinal(enumType, (StdF64)stdValue, newBlock);
              } else {
                // Simple enums (no backing type) and string/char-backed enums store ordinals
                ordinalValue = (StdI64)stdValue;
              }
              var (nameBuf, nameLen) = EmitUnionNameLookup(enumType, ordinalValue, newBlock, result);
              var tempName = $"__enum_name_{enumNameOp.Result.Id}";
              EmitManagedStructFromBufLen(tempName, nameBuf, nameLen,
                true, newBlock, varTypes, structVarNames, enumNameOp.Result.Id);
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
              // Heap-allocate the struct and store each field via indirect stores.
              // If this literal is immediately assigned, use the target variable name for the heap pointer.
              var tempName = structLiteralTargets.TryGetValue(structLitOp.Result.Id, out var inlineTarget)
                ? inlineTarget
                : $"__struct_{structLitOp.Result.Id}";
              var structType = (MlirStructType)module.TypeDefs[structLitOp.TypeName];

              // Allocate memory for the struct (stack for eligible, simple for static-free, heap otherwise)
              // Return allocs in CanStaticReturn scopes use mm_alloc so callers can mm_move them
              bool useStackAlloc = stackAllocIds.Contains(structLitOp.Result.Id);
              bool isReturnAlloc = returnAllocIds.Contains(structLitOp.Result.Id);
              var structPtr = useStackAlloc
                ? EmitStackAlloc(newBlock, structType.SizeInBytes)
                : (IsCurrentScopeStaticFree() && !isReturnAlloc)
                  ? EmitAllocSimple(newBlock, structType.SizeInBytes)
                  : EmitAlloc(newBlock, structType.SizeInBytes, structLitOp.TypeName);
              EmitStore(newBlock, structPtr, tempName, varTypes);

              foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                var field = structType.GetField(fieldName)!;
                if (structVarNames.TryGetValue(fieldVal.Id, out var nestedStructName)) {
                  // Struct or associated-value enum field: both are heap pointers now
                  var nestedHeapPtr = EmitLoad(newBlock, nestedStructName, varTypes);
                  EmitStructFieldStore(newBlock, nestedHeapPtr, tempName, field.Offset, MlirType.I64, varTypes);
                  // ChainNode data stays owned by its chain — the wrapper holds a borrowed pointer.
                  // Reparenting would steal ownership from the chain, causing the node to be freed
                  // when the wrapper goes out of scope (even though the chain still needs it).
                  bool isChainNodeData = structValueTypes.TryGetValue(fieldVal.Id, out var nestedTypeName)
                    && IsChainNodeType(nestedTypeName, module.TypeDefs);
                  if (!isChainNodeData)
                    EmitReparent(newBlock, (StdI64)nestedHeapPtr, tempName, varTypes);
                } else {
                  var mappedVal = valueMap[fieldVal];
                  var litFieldStoreType = field.Type is MlirStructType ? MlirType.I64 : field.Type;
                  EmitStructFieldStore(newBlock, mappedVal, tempName, field.Offset, litFieldStoreType, varTypes);
                }
              }

              // For array/vector literals, patch the buffer field to point to element data
              if (structLitOp.ArrayLiteralTag != null) {
                // Access buffer field through heap pointer indirection.
                // For __ManagedMemory, buffer is directly a field. For outer structs (Array, Vector),
                // the managed field is a nested struct whose heap pointer contains the buffer field.
                StdI64 rdataPtr;
                if (module.ConstantArrayLiterals.TryGetValue(structLitOp.Result.Id, out var constArrayInfo)) {
                  int elemSize = constArrayInfo.ElementSize;
                  var rdataBytes = new byte[constArrayInfo.Values.Length * elemSize];
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
                  result.RdataEntries.Add((constArrayInfo.RdataLabel, rdataBytes, elemSize));
                  var leaRdataOp = new StdLeaRdataOp(constArrayInfo.RdataLabel);
                  newBlock.AddOp(leaRdataOp);
                  var rdataPtrOp = new StdPtrToI64Op(leaRdataOp.Result);
                  newBlock.AddOp(rdataPtrOp);
                  rdataPtr = rdataPtrOp.Result;
                } else if (escapingArrayLiterals.Contains(structLitOp.Result.Id)) {
                  // Heap-allocate the buffer and copy element data from stack.
                  // Stack-local element variables become invalid when the function returns,
                  // so the buffer must live on the heap for the array to be safely returned or passed.
                  var leaOp = new StdLeaOp(structLitOp.ArrayLiteralTag);
                  newBlock.AddOp(leaOp);
                  var stackPtr = new StdPtrToI64Op(leaOp.Result);
                  newBlock.AddOp(stackPtr);

                  // Load element_size from the managed memory struct that was already lowered
                  StdI64 elemSizeVal;
                  if (TypeAliasInfo.IsManagedMemoryType(structLitOp.TypeName, module.TypeAliasSources)) {
                    elemSizeVal = (StdI64)EmitStructFieldLoad(newBlock, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
                  } else {
                    var managedFieldForSize = structType.GetField("managed")!;
                    var managedPtrForSize = (StdI64)EmitStructFieldLoad(newBlock, tempName, managedFieldForSize.Offset, MlirType.I64, varTypes);
                    var loadElemSize = new StdLoadIndirectOp(managedPtrForSize, ManagedFieldElementSize, MlirType.I64);
                    newBlock.AddOp(loadElemSize);
                    elemSizeVal = (StdI64)loadElemSize.Result;
                  }

                  var countOp = new StdConstI64Op(structLitOp.ArrayLiteralCount);
                  newBlock.AddOp(countOp);
                  var totalSize = new StdMulI64Op(countOp.Result, elemSizeVal);
                  newBlock.AddOp(totalSize);

                  // Allocate heap buffer as child of the array wrapper so it moves with the array
                  var arrayWrapperPtr = (StdI64)EmitLoad(newBlock, tempName, varTypes);
                  var heapBuf = EmitAllocIn(newBlock, totalSize.Result, arrayWrapperPtr, "Buffer");
                  var copyResult = new StdI64(MlirContext.Current.NextId());
                  newBlock.AddOp(new StdCallRuntimeOp("maxon_memcpy", [heapBuf, stackPtr.Result, totalSize.Result], copyResult));

                  // Reparent struct elements under the array wrapper so they survive scope exit
                  var firstElemVar = $"{structLitOp.ArrayLiteralTag}.0";
                  if (varNameToStructType.ContainsKey(firstElemVar)) {
                    for (int ei = 0; ei < structLitOp.ArrayLiteralCount; ei++) {
                      var elemVar = $"{structLitOp.ArrayLiteralTag}.{ei}";
                      EmitReparent(newBlock, (StdI64)EmitLoad(newBlock, elemVar, varTypes), tempName, varTypes);
                    }
                  }

                  rdataPtr = heapBuf;
                } else {
                  // Stack buffer is safe — array is only used within this function
                  if (structLitOp.SkipZeroInit) {
                    // Reserve stack space without clearing it
                    newBlock.AddOp(new StdBulkZeroOp(structLitOp.ArrayLiteralTag, structLitOp.ArrayLiteralCount, zeroInit: false));
                  }
                  var leaOp = new StdLeaOp(structLitOp.ArrayLiteralTag);
                  newBlock.AddOp(leaOp);
                  var castOp = new StdPtrToI64Op(leaOp.Result);
                  newBlock.AddOp(castOp);
                  rdataPtr = castOp.Result;

                  // Reparent struct elements under the array wrapper so Array.get
                  // returns parent-owned references (mm_incref/mm_decref are no-ops)
                  var firstElemVarStack = $"{structLitOp.ArrayLiteralTag}.0";
                  if (varNameToStructType.ContainsKey(firstElemVarStack)) {
                    for (int ei = 0; ei < structLitOp.ArrayLiteralCount; ei++) {
                      var elemVar = $"{structLitOp.ArrayLiteralTag}.{ei}";
                      EmitReparent(newBlock, (StdI64)EmitLoad(newBlock, elemVar, varTypes), tempName, varTypes);
                    }
                  }
                }

                // Set capacity for heap-allocated writable buffers only.
                // Stack buffers and rdata buffers get capacity=0 (treated as read-only by COW check,
                // and skipped by the __ManagedMemory destructor to avoid freeing non-heap memory).
                bool isConstantBuffer = module.ConstantArrayLiterals.ContainsKey(structLitOp.Result.Id);
                bool isHeapEscapeBuffer = escapingArrayLiterals.Contains(structLitOp.Result.Id) && !isConstantBuffer;
                bool bufferIsWritable = isHeapEscapeBuffer;
                if (TypeAliasInfo.IsManagedMemoryType(structLitOp.TypeName, module.TypeAliasSources)) {
                  // buffer is directly on this struct at offset 0
                  var bufferField = structType.GetField("buffer")!;
                  EmitStructFieldStore(newBlock, rdataPtr, tempName, bufferField.Offset, MlirType.I64, varTypes);
                  // Writable buffers (stack or heap) get capacity=count so COW check passes
                  if (bufferIsWritable) {
                    var capOp = new StdConstI64Op(structLitOp.ArrayLiteralCount);
                    newBlock.AddOp(capOp);
                    EmitStructFieldStore(newBlock, capOp.Result, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
                  }
                } else {
                  // Outer struct (Array, Vector): load the managed field's heap pointer, then store buffer on it
                  var managedField = structType.GetField("managed")!;
                  var managedHeapPtr = (StdI64)EmitStructFieldLoad(newBlock, tempName, managedField.Offset, MlirType.I64, varTypes);
                  // Store buffer on the __ManagedMemory heap object
                  var managedType = (MlirStructType)managedField.Type;
                  var bufferField = managedType.GetField("buffer")!;
                  newBlock.AddOp(new StdStoreIndirectOp(rdataPtr, managedHeapPtr, bufferField.Offset, MlirType.I64));
                  // Writable buffers (stack or heap) get capacity=count so COW check passes
                  if (bufferIsWritable) {
                    var capOp = new StdConstI64Op(structLitOp.ArrayLiteralCount);
                    newBlock.AddOp(capOp);
                    var capField = managedType.GetField("capacity")!;
                    newBlock.AddOp(new StdStoreIndirectOp(capOp.Result, managedHeapPtr, capField.Offset, MlirType.I64));
                  }
                }
              }

              structVarNames[structLitOp.Result.Id] = tempName;
              structValueTypes[structLitOp.Result.Id] = structLitOp.TypeName;

              break;
            }
            case MaxonAssignOp assignOp: {
              // Associated-value enums now use heap pointers (like structs) and fall
              // through to the struct assignment path below.
              // Check structVarNames as the authoritative source: chain ops like
              // chain_node_value report MaxonStruct result type even for primitives,
              // so ValueKind alone is not reliable.
              if (structVarNames.ContainsKey(assignOp.Value.Id)) {
                // Struct assignment: copy the heap pointer (alias, not deep copy)
                // Copy-by-default cloning is handled at the parser level via clone() calls.
                if (!structVarNames.TryGetValue(assignOp.Value.Id, out var srcName))
                  throw new InvalidOperationException($"MaxonAssignOp: structVarNames missing key {assignOp.Value.Id} for assign to '{assignOp.VarName}' (ValueKind={assignOp.ValueKind}) in func {func.Name}");
                var dstName = assignOp.VarName;
                var structTypeName = structValueTypes.TryGetValue(assignOp.Value.Id, out var svt)
                  ? svt
                  : assignOp.Value is MaxonStruct ms
                    ? ms.TypeName
                    : throw new InvalidOperationException($"No struct type info for value #{assignOp.Value.Id} in assign to '{assignOp.VarName}'");
                if (srcName != dstName) {
                  // Decref old value before overwriting (reassignment only)
                  if (!assignOp.IsDeclaration) {
                    EmitDecref(newBlock, dstName, varTypes);
                  }
                  var srcHeapPtr = EmitLoad(newBlock, srcName, varTypes);
                  EmitStore(newBlock, srcHeapPtr, dstName, varTypes);
                }
                // Adjust refcount for the new reference.
                // Skip incref when source is a call return — callee's rc=1 IS our reference.
                bool isFromCallReturn = srcName.StartsWith("__callret_")
                  || srcName.StartsWith("__call_tmp_")
                  || srcName.StartsWith("__interp_tostr_")
                  || srcName.StartsWith("__forin_result_")
                  || srcName.StartsWith("__chain_nav_");
                if (isFromCallReturn
                    && !assignOp.VarName.StartsWith("__call_tmp_")
                    && !assignOp.VarName.StartsWith("__callret_")) {
                  var currentScopeInfo = _scopeAnalysisStack?.Count > 0 ? _scopeAnalysisStack[^1] : null;
                  if (currentScopeInfo?.ReturnSelfValueIds.Contains(assignOp.Value.Id) == true)
                    isFromCallReturn = false;
                }
                if ((assignOp.IsDeclaration || srcName != dstName) && !isFromCallReturn) {
                  EmitIncref(newBlock, assignOp.VarName, varTypes);
                }
                varNameToStructType[assignOp.VarName] = structTypeName;
                if (IsSelfField(isStructInstanceMethod, selfStructType, assignOp.VarName)) {
                  var field = selfStructType!.GetField(assignOp.VarName);
                  if (field != null) {
                    var heapPtr2 = EmitLoad(newBlock, dstName, varTypes);
                    if (field.Type is MlirStructType
                        || (field.Type is MlirUnionType fieldUnion && fieldUnion.HasAssociatedValues)) {
                      // Heap-pointer field: reparent new value under self only if it's
                      // scope-owned (newly allocated). Parent-owned values (e.g., findLast
                      // result pointing into existing chain) must not be detached from
                      // their current owner. Old field values are not eagerly freed —
                      // they remain in self's child list and are cleaned up at scope exit.
                      var selfPtr = (StdI64)EmitLoad(newBlock, "self", varTypes);
                      newBlock.AddOp(new StdCallRuntimeOp("mm_reparent_if_scope_owned", [heapPtr2, selfPtr], null));
                    }
                    if (Compiler.MmTrace)
                      newBlock.AddOp(new StdCallRuntimeOp("mm_trace_move", [heapPtr2], null));
                    EmitStructFieldStore(newBlock, heapPtr2, "self", field.Offset, MlirType.I64, varTypes);
                  }
                }
                structVarNames[assignOp.Value.Id] = dstName;
                structValueTypes[assignOp.Value.Id] = structTypeName;
                varNameToStructPrefix[assignOp.VarName] = dstName;
              } else {
                var mappedValue = valueMap[assignOp.Value];
                // Widen I32/U32 to I64 when the variable was previously stored as I64
                // (e.g., try...otherwise where the default is I64 but the call result is U32)
                if (mappedValue is StdI32 && varTypes.TryGetValue(assignOp.VarName, out var prevType) && prevType == "i64") {
                  mappedValue = EnsureI64(mappedValue is StdU32 u32w ? new StdI32(u32w.Id) : mappedValue, newBlock, signExtend: mappedValue is not StdU32);
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
              }
              // Write back through reference pointer for reassigned mutated parameters
              if (!assignOp.IsDeclaration && _refParamPtrVars != null
                  && _refParamPtrVars.TryGetValue(assignOp.VarName, out var refVarNameForWriteBack)) {
                var refPtr = (StdI64)EmitLoad(newBlock, refVarNameForWriteBack, varTypes);
                var localVal = EmitLoad(newBlock, assignOp.VarName, varTypes);
                var writeBackType = varTypes.TryGetValue(assignOp.VarName, out var vt2) ? VarTypeToMlirType(vt2) : MlirType.I64;
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
                if (field != null && field.Type is not MlirStructType) {
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
                structVarNames[varRef.Result.Id] = structPrefix;
                if (varNameToStructType.TryGetValue(resolvedVarName, out var stType)) {
                  structValueTypes[varRef.Result.Id] = stType;
                }
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
                  var tempVarName = $"__selfref_{structVarRef.Result.Id}";
                  var nestedPtr = EmitStructFieldLoad(newBlock, "self", field.Offset, MlirType.I64, varTypes);
                  EmitStore(newBlock, nestedPtr, tempVarName, varTypes);
                  resolvedName = tempVarName;
                  selfFieldCache[structVarRef.VarName] = tempVarName;
                }
              } else {
                resolvedName = varNameToStructPrefix.GetValueOrDefault(structVarRef.VarName, structVarRef.VarName);
              }
              structVarNames[structVarRef.Result.Id] = resolvedName;
              // Prefer the canonical type from varNameToStructType (set during struct
              // assignment with resolved types) over the StructTypeName from the parser
              // which may contain stale inner alias names (e.g. "Entry" instead of
              // "StringIntPair") when the call-site rewrite preserved the old Result type.
              var resolvedTypeName = varNameToStructType.TryGetValue(structVarRef.VarName, out var vt)
                ? vt
                : structVarRef.StructTypeName;
              structValueTypes[structVarRef.Result.Id] = resolvedTypeName;
              break;
            }
            case MaxonFieldAccessOp fieldAccess: {
              var structName = structVarNames[fieldAccess.StructValue.Id];
              // Resolve the field type and offset from the struct type definition
              var parentTypeName = structValueTypes.TryGetValue(fieldAccess.StructValue.Id, out var ptn) ? ptn : null;
              MlirStructType? parentStructType = null;
              if (parentTypeName != null && module.TypeDefs.TryGetValue(parentTypeName, out var ptDef) && ptDef is MlirStructType pst)
                parentStructType = pst;
              var fieldDef = parentStructType?.GetField(fieldAccess.FieldName);
              // If the field has an unresolved type parameter type (e.g., Entry._1 = Value),
              // resolve by finding a concrete alias with the same source type.
              if (fieldDef != null && fieldDef.Type is MlirTypeParameterType && parentTypeName != null
                  && module.TypeAliasSources.TryGetValue(parentTypeName, out var parentAliasInfo)) {
                foreach (var (candidateName, candidateInfo) in module.TypeAliasSources) {
                  if (candidateName == parentTypeName) continue;
                  if (candidateInfo.SourceTypeName != parentAliasInfo.SourceTypeName) continue;
                  if (candidateInfo.TypeParams == null || candidateInfo.TypeParams.Values.Any(t => t is MlirTypeParameterType)) continue;
                  if (module.TypeDefs.TryGetValue(candidateName, out var candidateDef) && candidateDef is MlirStructType candidateSt) {
                    var resolvedField = candidateSt.GetField(fieldAccess.FieldName);
                    if (resolvedField != null && resolvedField.Type is not MlirTypeParameterType) {
                      fieldDef = resolvedField;
                      break;
                    }
                  }
                }
              }

              if (fieldAccess.ResultKind == MaxonValueKind.Struct) {
                // Struct-typed field: load the nested struct's heap pointer and store it in a temp var
                var tempVarName = $"__field_{fieldAccess.Result.Id}";
                if (fieldDef != null) {
                  var nestedPtr = EmitStructFieldLoad(newBlock, structName, fieldDef.Offset, MlirType.I64, varTypes);
                  EmitStore(newBlock, nestedPtr, tempVarName, varTypes);
                  // For self fields, also initialize the field name variable so that later
                  // code referencing it by name (across conditional blocks) gets the correct value.
                  if (IsSelfField(isStructInstanceMethod, selfStructType, fieldAccess.FieldName)) {
                    EmitStore(newBlock, nestedPtr, fieldAccess.FieldName, varTypes);
                  }
                } else {
                  // Fallback: try loading as a named variable (legacy path)
                  var loaded = EmitLoad(newBlock, $"{structName}.{fieldAccess.FieldName}", varTypes);
                  EmitStore(newBlock, loaded, tempVarName, varTypes);
                }
                structVarNames[fieldAccess.Result.Id] = tempVarName;
                // Only update varNameToStructPrefix if the field name doesn't shadow
                // an existing parameter or local variable (e.g., a param named "data"
                // must not be overwritten by a field access like "existing.data").
                if (!varTypes.ContainsKey(fieldAccess.FieldName)) {
                  varNameToStructPrefix[fieldAccess.FieldName] = tempVarName;
                } else {
                  Logger.Trace(LogCategory.Mlir, $"Skipping varNameToStructPrefix['{fieldAccess.FieldName}'] — shadows existing variable");
                }
                // Propagate type info for the nested struct field
                if (fieldDef?.Type is MlirStructType fieldStructType)
                  structValueTypes[fieldAccess.Result.Id] = fieldStructType.Name;
              } else if (fieldAccess.ResultKind == MaxonValueKind.Enum
                         && fieldAccess.ResultStructTypeName != null
                         && module.TypeDefs.TryGetValue(fieldAccess.ResultStructTypeName, out var faEnumDef)
                         && faEnumDef is MlirUnionType faEnumType && faEnumType.HasAssociatedValues) {
                // Associated-value enum field: load heap pointer (no unpacking needed)
                var tempVarName = $"__field_{fieldAccess.Result.Id}";
                if (fieldDef != null) {
                  var enumPtr = EmitStructFieldLoad(newBlock, structName, fieldDef.Offset, MlirType.I64, varTypes);
                  EmitStore(newBlock, enumPtr, tempVarName, varTypes);
                } else {
                  var loaded = EmitLoad(newBlock, $"{structName}.{fieldAccess.FieldName}", varTypes);
                  EmitStore(newBlock, loaded, tempVarName, varTypes);
                }
                structVarNames[fieldAccess.Result.Id] = tempVarName;
                if (!varTypes.ContainsKey(fieldAccess.FieldName)) {
                  varNameToStructPrefix[fieldAccess.FieldName] = tempVarName;
                }
                // For self fields, store the heap pointer under the field name so that
                // later code referencing it by name (across conditional blocks) gets the correct value.
                if (IsSelfField(isStructInstanceMethod, selfStructType, fieldAccess.FieldName)) {
                  EmitStore(newBlock, EmitLoad(newBlock, tempVarName, varTypes), fieldAccess.FieldName, varTypes);
                  varNameToStructPrefix[fieldAccess.FieldName] = fieldAccess.FieldName;
                }
                structValueTypes[fieldAccess.Result.Id] = fieldAccess.ResultStructTypeName;
              } else {
                // Scalar field: load via indirect access through heap pointer
                if (fieldDef != null) {
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
              var structName = structVarNames[fieldAssign.StructValue.Id];

              // Resolve the field type and offset from the struct type definition
              var faParentTypeName = structValueTypes.TryGetValue(fieldAssign.StructValue.Id, out var faptn) ? faptn : null;
              MlirStructType? faParentStructType = null;
              if (faParentTypeName != null && module.TypeDefs.TryGetValue(faParentTypeName, out var faptDef) && faptDef is MlirStructType fapst)
                faParentStructType = fapst;
              var faFieldDef = faParentStructType?.GetField(fieldAssign.FieldName);

              if (!valueMap.TryGetValue(fieldAssign.NewValue, out StdValue? mappedVal)) {
                if (structVarNames.TryGetValue(fieldAssign.NewValue.Id, out var newValStructName)) {
                  mappedVal = EmitLoad(newBlock, newValStructName, varTypes);
                } else {
                  throw new InvalidOperationException($"MaxonFieldAssignOp: NewValue %{fieldAssign.NewValue.Id} not in valueMap or structVarNames for {structName}.{fieldAssign.FieldName} in func {func.Name}");
                }
              }

              if (faFieldDef != null) {
                var storeType = faFieldDef.Type is MlirStructType or MlirUnionType { HasAssociatedValues: true } ? MlirType.I64 : faFieldDef.Type;
                EmitStructFieldStore(newBlock, mappedVal, structName, faFieldDef.Offset, storeType, varTypes);
                if (faFieldDef.Type is MlirStructType or MlirUnionType { HasAssociatedValues: true }) {
                  EmitReparent(newBlock, (StdI64)mappedVal, structName, varTypes);
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
                Logger.Debug(LogCategory.Mlir, $"Algebraic identity: {binOp.Operator} on {binOp.OperandKind} → eliminated");
                valueMap[binOp.Result] = identityResult;
                break;
              }

              // Load operand from valueMap; fall back to structVarNames for type-parameter
              // fields promoted to Struct kind (they store heap pointers as i64 values)
              if (!valueMap.TryGetValue(binOp.Lhs, out StdValue? lhs)) {
                if (structVarNames.TryGetValue(binOp.Lhs.Id, out var lhsStructVar))
                  lhs = EmitLoad(newBlock, lhsStructVar, varTypes);
                else
                  throw new InvalidOperationException($"BinOp LHS %{binOp.Lhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");
              }
              if (!valueMap.TryGetValue(binOp.Rhs, out StdValue? rhs)) {
                if (structVarNames.TryGetValue(binOp.Rhs.Id, out var rhsStructVar))
                  rhs = EmitLoad(newBlock, rhsStructVar, varTypes);
                else
                  throw new InvalidOperationException($"BinOp RHS %{binOp.Rhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");
              }

              // Use OptimalType to select narrower/unsigned ops
              if (binOp.OperandKind == MaxonValueKind.Integer && binOp.OptimalType is MlirType ot) {
                var signedOt = ot.ToSigned();
                if (signedOt == MlirType.I32 || signedOt == MlirType.I8) {
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

              // F32 values arrive with Float kind from Maxon dialect; dispatch to Float32 ops
              var effectiveKind = binOp.OperandKind == MaxonValueKind.Float && (lhs is StdF32 || rhs is StdF32)
                ? MaxonValueKind.Float32 : binOp.OperandKind;
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
              // Struct values are tracked by structVarNames — load their heap pointers
              var lhsVarName = structVarNames[refEq.Lhs.Id];
              var rhsVarName = structVarNames[refEq.Rhs.Id];
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
            case MaxonScopeEnterOp scopeEnterOp: {
              var scopeInfo = funcScopeAnalysis?.GetValueOrDefault(scopeEnterOp.ResultVar);
              _scopeAnalysisStack?.Add(scopeInfo);
              if (scopeInfo is { NeedsRuntimeFrame: false }
                  or { CanStaticFree: true }
                  or { CanStaticReturn: true }) {
                // All allocations are compile-time managed — elide runtime scope frame
                var sentinel = new StdConstI64Op(0);
                newBlock.AddOp(sentinel);
                EmitStore(newBlock, sentinel.Result, scopeEnterOp.ResultVar, varTypes);
              } else {
                var tagPtr = EmitTagPtr(newBlock, scopeEnterOp.Tag);
                var scopeResult = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdCallRuntimeOp("mm_scope_enter", [tagPtr], scopeResult));
                EmitStore(newBlock, scopeResult, scopeEnterOp.ResultVar, varTypes);
                if (Compiler.MmTrace) {
                  newBlock.AddOp(new StdCallRuntimeOp("mm_trace_scope_enter", [scopeResult], null));
                }
              }
              varTypes[scopeEnterOp.ResultVar] = "i64";
              break;
            }
            case MaxonScopeExitOp scopeExitOp: {
              var scopeInfo = funcScopeAnalysis?.GetValueOrDefault(scopeExitOp.ScopeVar);
              // Pop scope from analysis stack
              if (_scopeAnalysisStack != null) {
                for (int si = _scopeAnalysisStack.Count - 1; si >= 0; si--) {
                  if (_scopeAnalysisStack[si]?.ScopeVar == scopeExitOp.ScopeVar) {
                    _scopeAnalysisStack.RemoveAt(si);
                    break;
                  }
                }
              }
              // Clear chain variables before decref — unlinks all nodes so they
              // can be freed cleanly by the scope exit.
              if (scopeInfo?.RefcountedVars.Count > 0) {
                foreach (var rcVar in scopeInfo.RefcountedVars) {
                  if (scopeInfo.ReturnMoveVars.Contains(rcVar)) continue;
                  if (!varTypes.ContainsKey(rcVar)) continue;
                  if (varNameToStructType.TryGetValue(rcVar, out var rcStructType) && IsChainType(rcStructType, module.TypeDefs)) {
                    var chainPtr = (StdI64)EmitLoad(newBlock, rcVar, varTypes);
                    newBlock.AddOp(new StdCallRuntimeOp("maxon_chain_clear", [chainPtr], null));
                  }
                }
              }
              // Emit decref for all refcounted vars in this scope
              if (scopeInfo?.RefcountedVars.Count > 0) {
                foreach (var rcVar in scopeInfo.RefcountedVars) {
                  if (scopeInfo.ReturnMoveVars.Contains(rcVar)) continue;
                  if (!varTypes.ContainsKey(rcVar)) continue;
                  EmitDecref(newBlock, rcVar, varTypes);
                }
              }
              if (scopeInfo is { NeedsRuntimeFrame: false }) {
                // Scope was elided — refcounted vars have been decreffed above
                break;
              }
              if (scopeInfo is { CanStaticFree: true } or { CanStaticReturn: true }) {
                EmitStaticFree(newBlock, scopeInfo, varTypes);
                break;
              }
              var scopePtr = (StdI64)EmitLoad(newBlock, scopeExitOp.ScopeVar, varTypes);
              if (Compiler.MmTrace) {
                newBlock.AddOp(new StdCallRuntimeOp("mm_trace_scope_exit", [scopePtr], null));
              }
              newBlock.AddOp(new StdCallRuntimeOp("mm_scope_exit", [scopePtr], null));
              break;
            }
            case MaxonMoveOp moveOp: {
              // Skip move for primitive values: after monomorphization, a generic function
              // may have move ops for return values that resolved to primitives. Only heap-
              // allocated values (structs/strings) tracked in varNameToStructPrefix need moves.
              if (!varNameToStructPrefix.ContainsKey(moveOp.VarName)
                  && !varNameToStructType.ContainsKey(moveOp.VarName))
                break;
              // CanStaticReturn: scope frame is elided, so mm_alloc registers the return value
              // directly in the caller's scope via __mm_current_scope — no mm_move needed
              if (moveOp.Tag == "return_move") {
                var moveScopeInfo = funcScopeAnalysis?.GetValueOrDefault(moveOp.DestScopeVar);
                if (moveScopeInfo is { CanStaticReturn: true })
                  break;
              }
              var varPtr = (StdI64)EmitLoad(newBlock, moveOp.VarName, varTypes);
              var scopePtr = (StdI64)EmitLoad(newBlock, moveOp.DestScopeVar, varTypes);
              // For return_move: move to the PARENT scope (caller's scope)
              // ScopeFrame layout: [+8] = parent_scope
              StdI64 destScope;
              if (moveOp.Tag == "return_move") {
                var scopeAsPtr = new StdPtr(scopePtr.Id);
                var loadParent = new StdLoadIndirectOp(scopeAsPtr, 8, MlirType.I64);
                newBlock.AddOp(loadParent);
                destScope = (StdI64)loadParent.Result;
              } else {
                destScope = scopePtr;
              }
              var moveTag = new StdConstI64Op(0);
              newBlock.AddOp(moveTag);
              newBlock.AddOp(new StdCallRuntimeOp("mm_move", [varPtr, destScope, moveTag.Result], null));
              if (Compiler.MmTrace)
                newBlock.AddOp(new StdCallRuntimeOp("mm_trace_move", [varPtr], null));
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
                EmitStore(newBlock, valueMap[globalLoad.Result], tempName, varTypes);
                EmitIncref(newBlock, tempName, varTypes);
                structVarNames[globalLoad.Result.Id] = tempName;
                if (globalLoad.StructTypeName != null)
                  structValueTypes[globalLoad.Result.Id] = globalLoad.StructTypeName;
              }
              break;
            }
            case MaxonGlobalStoreOp globalStore: {
              if (globalStore.ValueKind == MaxonValueKind.Struct) {
                // Resolve the new heap pointer
                StdI64 newHeapPtr;
                if (valueMap.TryGetValue(globalStore.Value, out var mv) && mv is StdI64 i64Val) {
                  newHeapPtr = i64Val;
                } else if (structVarNames.TryGetValue(globalStore.Value.Id, out var srcName)) {
                  newHeapPtr = (StdI64)EmitLoad(newBlock, srcName, varTypes);
                } else {
                  throw new InvalidOperationException($"Cannot store struct value to global '{globalStore.GlobalName}': no struct tracking info");
                }

                bool isModuleInit = func.Name == "__module_init" || func.Name.EndsWith(".__module_init");
                if (!isModuleInit) {
                  // Free old global value before storing new one.
                  // Guard against null (global starts as zero before __module_init runs).
                  var oldGlobalLoad = new StdGlobalLoadI64Op(globalStore.GlobalName);
                  newBlock.AddOp(oldGlobalLoad);
                  newBlock.AddOp(new StdCallRuntimeOp("mm_free_if_nonnull", [oldGlobalLoad.Result], null));

                  // Move the new allocation to the root scope so it outlives the current function
                  var rootScopeResult = new StdI64(MlirContext.Current.NextId());
                  newBlock.AddOp(new StdCallRuntimeOp("mm_get_root_scope", [], rootScopeResult));
                  var moveMode = new StdConstI64Op(0); // mode 0 = move to scope
                  newBlock.AddOp(moveMode);
                  newBlock.AddOp(new StdCallRuntimeOp("mm_move", [newHeapPtr, rootScopeResult, moveMode.Result], null));
                  if (Compiler.MmTrace)
                    newBlock.AddOp(new StdCallRuntimeOp("mm_trace_move", [newHeapPtr], null));
                }

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
              LowerTryCall(tryCallOp, funcLookup, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs);
              break;
            case MaxonCallOp callOp:
              if (TryLowerPrimitiveMethod(callOp, newBlock, valueMap)) break;
              LowerCall(callOp, funcLookup, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs, fnEnvVarNames);
              // After a call that passes variables by reference, reload those variables
              // so subsequent uses see the mutated values instead of stale SSA values
              if (_mutatingParams != null && callOp.ArgVarNames != null
                  && funcLookup.TryGetValue(callOp.Callee, out var calleeForReload)
                  && _mutatingParams.TryGetValue(callOp.Callee, out var mutatedParamsForReload)) {
                for (int ai = 0; ai < callOp.Args.Count && ai < callOp.ArgVarNames.Count; ai++) {
                  var argVarName = callOp.ArgVarNames[ai];
                  if (argVarName == null) continue;
                  if (ai >= calleeForReload.ParamNames.Count) continue;
                  var calleeParamName = calleeForReload.ParamNames[ai];
                  if (!mutatedParamsForReload.Contains(calleeParamName)) continue;
                  if (!varTypes.ContainsKey(argVarName)) continue;
                  // If we forwarded the ref pointer, the callee modified the original location,
                  // not our local copy. Reload the local from the ref pointer first.
                  if (_refParamPtrVars != null && _refParamPtrVars.TryGetValue(argVarName, out var refPtrForReload)) {
                    var refPtr = (StdI64)EmitLoad(newBlock, refPtrForReload, varTypes);
                    var varType = varTypes.TryGetValue(argVarName, out var vt) ? VarTypeToMlirType(vt) : MlirType.I64;
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
              LowerClosureCreate(closureCreateOp, newBlock, valueMap, varTypes, structVarNames, fnEnvVarNames);
              break;
            case MaxonClosureEnvLoadOp envLoadOp:
              LowerClosureEnvLoad(envLoadOp, newBlock, valueMap, varTypes, structVarNames, structValueTypes);
              break;
            case MaxonFunctionParamOp fnParamOp:
              LowerFunctionParam(fnParamOp, newBlock, valueMap, varTypes, fnEnvVarNames, fnEnvDirectValues, paramFlatIndex);
              break;
            case MaxonFunctionVarRefOp fnVarRefOp:
              LowerFunctionVarRef(fnVarRefOp, newBlock, valueMap, varTypes, fnEnvVarNames);
              break;
            case MaxonIndirectCallOp indirectCallOp:
              LowerIndirectCall(indirectCallOp, newBlock, valueMap, varTypes, structVarNames, module.TypeDefs, fnEnvVarNames, fnEnvDirectValues);
              break;
            case MaxonReturnOp retOp:
              LowerReturn(retOp, retStructType, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs);
              break;
            case MaxonThrowOp throwOp:
              LowerThrow(throwOp, newBlock, valueMap, structVarNames, varTypes, module.TypeDefs);
              break;
            case MaxonManagedMemGetOp memGetOp:
              LowerManagedMemGet(memGetOp, newBlock, valueMap, varTypes, structVarNames, structValueTypes);
              break;
            case MaxonManagedMemSetOp memSetOp:
              LowerManagedMemSet(memSetOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemCreateOp memCreateOp:
              LowerManagedMemCreate(memCreateOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemGrowOp memGrowOp:
              LowerManagedMemGrow(memGrowOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemSetLengthOp setLenOp:
              LowerManagedMemSetLength(setLenOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemShiftOp memShiftOp:
              LowerManagedMemShift(memShiftOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemByteGetOp byteGetOp:
              LowerManagedMemByteGet(byteGetOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemByteSetOp byteSetOp:
              LowerManagedMemByteSet(byteSetOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonCStringToManagedOp fromCStringOp:
              LowerCStringToManaged(fromCStringOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedToCStringOp toCStringOp:
              LowerManagedToCString(toCStringOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedWriteStdoutOp managedWriteStdoutOp:
              LowerManagedWriteStdout(managedWriteStdoutOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedWriteStderrOp managedWriteStderrOp:
              LowerManagedWriteStderr(managedWriteStderrOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonPanicOp panicOp:
              LowerPanic(panicOp, newBlock, result);
              break;
            case MaxonStringLiteralOp stringLitOp:
              LowerStringLiteral(stringLitOp, newBlock, varTypes, structVarNames, result);
              break;
            case MaxonByteStringLiteralOp byteStringLitOp:
              LowerByteStringLiteral(byteStringLitOp, newBlock, varTypes, structVarNames, result);
              break;
            case MaxonCharLiteralOp charLitOp:
              LowerCharLiteral(charLitOp, newBlock, varTypes, structVarNames, result);
              break;
            case MaxonStringInterpOp interpOp:
              LowerStringInterp(interpOp, newBlock, valueMap, varTypes, structVarNames, result);
              break;
            case MaxonManagedMemConcatOp concatOp:
              LowerManagedMemConcat(concatOp, newBlock, varTypes, structVarNames);
              break;
            case MaxonManagedMemSliceOp sliceOp:
              LowerManagedMemSlice(sliceOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonMakeCharFromBytesOp makeCharOp:
              LowerMakeCharFromBytes(makeCharOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonCallRuntimeOp callRtOp: {
              var stdArgs = callRtOp.Args.Select(a => {
                if (valueMap.TryGetValue(a, out var mapped))
                  return (StdValue)(StdI64)mapped;
                if (structVarNames.TryGetValue(a.Id, out var structName)) {
                  if (!structValueTypes.TryGetValue(a.Id, out var typeName))
                    throw new InvalidOperationException($"MaxonCallRuntimeOp struct arg {a} has no type in structValueTypes");
                  // Load buffer from managed struct via heap pointer indirection
                  if (TypeAliasInfo.IsManagedMemoryType(typeName, module.TypeAliasSources)) {
                    // structName IS the __ManagedMemory heap pointer, buffer at offset 0
                    return (StdValue)(StdI64)EmitStructFieldLoad(newBlock, structName, ManagedFieldBuffer, MlirType.I64, varTypes);
                  } else {
                    throw new InvalidOperationException(
                      $"MaxonCallRuntimeOp struct arg has unexpected type '{typeName}' — " +
                      "only __ManagedMemory struct args are supported (extract fields before passing to runtime calls)");
                  }
                }
                throw new InvalidOperationException($"MaxonCallRuntimeOp arg {a} not found in valueMap or structVarNames");
              }).ToList();
              if (callRtOp.Result != null) {
                var rtResult = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdCallRuntimeOp(callRtOp.FunctionName, stdArgs, rtResult));
                valueMap[callRtOp.Result] = rtResult;
              } else {
                newBlock.AddOp(new StdCallRuntimeOp(callRtOp.FunctionName, stdArgs, null));
              }
              break;
            }
            // Chain (doubly-linked list) operations
            case MaxonChainCreateOp chainCreateOp:
              LowerChainCreate(chainCreateOp, newBlock, varTypes, structVarNames, structValueTypes);
              break;
            case MaxonChainInsertValueOp chainInsertOp:
              LowerChainInsertValue(chainInsertOp, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs);
              break;
            case MaxonChainInsertRelativeValueOp chainInsertRelOp:
              LowerChainInsertRelativeValue(chainInsertRelOp, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs);
              break;
            case MaxonChainReinsertOp chainReinsertOp:
              LowerChainReinsert(chainReinsertOp, newBlock, varTypes, structVarNames);
              break;
            case MaxonChainReinsertRelativeOp chainReinsertRelOp:
              LowerChainReinsertRelative(chainReinsertRelOp, newBlock, varTypes, structVarNames);
              break;
            case MaxonChainDetachOp chainDetachOp:
              LowerChainDetach(chainDetachOp, newBlock, varTypes, structVarNames);
              break;
            case MaxonChainRemoveOp chainRemoveOp:
              LowerChainRemove(chainRemoveOp, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs);
              break;
            case MaxonChainCountOp chainCountOp:
              LowerChainCount(chainCountOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonChainNodeValueOp chainNodeValueOp:
              LowerChainNodeValue(chainNodeValueOp, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs);
              break;
            case MaxonChainNodeSetValueOp chainNodeSetValueOp:
              LowerChainNodeSetValue(chainNodeSetValueOp, newBlock, valueMap, varTypes, structVarNames, module.TypeDefs);
              break;
            case MaxonChainClearOp chainClearOp:
              LowerChainClear(chainClearOp, newBlock, varTypes, structVarNames);
              break;
            case MaxonChainCursorResetOp cursorResetOp:
              LowerChainCursorReset(cursorResetOp, newBlock, varTypes, structVarNames);
              break;
            case MaxonChainCursorValueOp cursorValueOp:
              LowerChainCursorValue(cursorValueOp, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs);
              break;
            default:
              throw new InvalidOperationException($"No MaxonToStandard conversion for: {op.GetType().Name} ({op.Mnemonic})");
          }
        }
      }
      result.AddFunction(newFunc);
    }

    // Generate __maxon_global_cleanup to release module-level struct variables at exit
    GenerateGlobalCleanup(module, result);

    return result;
  }

  private static void GenerateGlobalCleanup(MlirModule<MaxonOp> module, MlirModule<StandardOp> result) {
    // Only generate if there are struct globals to clean up
    bool hasStructGlobals = module.GlobalVarInfos.Any(kv =>
      kv.Value.Kind == MaxonValueKind.Struct && kv.Value.TypeName != null);
    if (!hasStructGlobals) return;

    var cleanupFunc = new MlirFunction<StandardOp>("__maxon_global_cleanup", [], [], null, null);
    var block = cleanupFunc.Body.AddBlock("entry");

    foreach (var (varName, meta) in module.GlobalVarInfos) {
      if (meta.Kind != MaxonValueKind.Struct) continue;
      if (meta.TypeName == null) continue;
      var globalLoad = new StdGlobalLoadI64Op(varName);
      block.AddOp(globalLoad);
      block.AddOp(new StdCallRuntimeOp("mm_free_if_nonnull", [globalLoad.Result], null));
    }

    block.AddOp(new StdReturnOp(null));
    result.AddFunction(cleanupFunc);
  }

}
