using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static partial class MaxonToStandardConversion {
  [ThreadStatic] private static bool _trackAllocs;
  [ThreadStatic] private static MlirModule<StandardOp>? _resultModule;
  [ThreadStatic] private static Dictionary<string, string>? _rdataTagCache;
  [ThreadStatic] private static Dictionary<string, HashSet<string>>? _mutatingParams;
  // Maps param name -> ref pointer var name for the current function being lowered
  [ThreadStatic] private static Dictionary<string, string>? _refParamPtrVars;

  public static MlirModule<StandardOp> Run(MlirModule<MaxonOp> module, bool trackAllocs = false) {
    _trackAllocs = trackAllocs;
    _rdataTagCache = [];
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

    // Pre-analyze: which functions mutate managed struct parameters?
    // Used to determine MOVE vs borrow semantics at call sites.
    var mutatingFunctions = new HashSet<string>();
    if (trackAllocs) {
      foreach (var f in module.Functions) {
        foreach (var b in f.Body.Blocks) {
          foreach (var op in b.Operations) {
            if (op is MaxonCallOp c && IsMutatingMethodCall(c.Callee)) {
              mutatingFunctions.Add(f.Name);
              goto nextFunc;
            }
          }
        }
      nextFunc:;
      }
    }

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
        hasResetAfterStdlib = true;
      }

      var retStructType = ResolveStructReturnType(func.ReturnType, module.TypeDefs);
      var escapingArrayLiterals = FindEscapingArrayLiterals(func);

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
          var enumType = (MlirEnumType)func.ParamTypes[0];
          var backingMlirType = ResolveEnumBackingMlirType(enumType);
          newParamNames.Add("self");
          newParamTypes.Add(backingMlirType);
          flatIdx++;
        } else if (func.ParamTypes[i] is MlirEnumType { HasAssociatedValues: true }) {
          // Associated-value enum param: pass as heap pointer (i64), like structs
          structParamPtrIndex[i] = flatIdx;
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(MlirType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is MlirEnumType enumParamType) {
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
        } else if (func.ParamTypes[i] is not MlirStructType and not MlirEnumType) {
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
      } else if (func.ReturnType is MlirEnumType { HasAssociatedValues: true }) {
        // Associated-value enum return: return heap pointer as i64
        newReturnType = MlirType.I64;
      } else if (func.ReturnType is MlirEnumType retEnumType) {
        newReturnType = ResolveEnumBackingMlirType(retEnumType);
      } else if (func.ReturnType is not MlirStructType and not MlirEnumType) {
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
      // Tracks variable names that own managed memory (for cleanup before return)
      // Key: user variable name (e.g. "arr"), Value: list of paths to buffer fields
      // Single-managed: ["arr.managed.buffer"], Multi-managed: ["item.numbers.managed.buffer", "item.text._managed.buffer", ...]
      var managedVarOwners = new Dictionary<string, List<string>>();
      // Tracks which buffer paths hold arrays of structs with managed fields (for per-element cleanup)
      // Key: buffer path (e.g. "m.keys.managed.buffer"), Value: list of (offset, typeName) for managed fields within elements
      var managedBufferElementInfo = new Dictionary<string, List<(int offset, string typeName)>>();

      // Use pre-computed constant array literal metadata from ConstantArrayAnalysisPass
      // Key: struct literal result ID, Value: ConstantArrayLiteralInfo

      _refParamPtrVars = refParamPtrVars;

      // Track cstring result IDs from .cstr() calls and their variable names
      var cstringResultIds = new HashSet<int>();
      var cstringTrackVars = new HashSet<string>();

      // Cache self-field loads: maps "fieldName" → temp var name for struct-typed self fields.
      // Avoids redundant load_indirect from self's heap pointer for the same field within a block.
      // Reset per-block since a cached var stored in one branch may not be defined in another.
      var selfFieldCache = new Dictionary<string, string>();

      // Track processed blocks to detect loop back-edges
      var processedBlocks = new HashSet<string>();
      // Snapshot of managedVarOwners at each block's entry, keyed by block name
      var managedStateAtBlockEntry = new Dictionary<string, Dictionary<string, List<string>>>();

      foreach (var block in func.Body.Blocks) {
        processedBlocks.Add(block.Name);
        selfFieldCache.Clear();
        if (_trackAllocs)
          managedStateAtBlockEntry[block.Name] = managedVarOwners.ToDictionary(
            kvp => kvp.Key, kvp => new List<string>(kvp.Value));
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

        foreach (var op in block.Operations) {
          switch (op) {
            case MaxonParamOp paramOp: {
              if (refParamPtrVars.ContainsKey(paramOp.Name)) {
                // Mutated param: receive reference pointer, dereference for initial local copy
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(paramOp.Index, paramOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, paramOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, refParamPtrVars[paramOp.Name], varTypes);
                // Dereference to get the initial value
                var loadRef = new StdLoadI64Op(refParamPtrVars[paramOp.Name]);
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
              } else if (refParamPtrVars.ContainsKey(structParamOp.Name)) {
                // Mutated struct param: receive pointer-to-heap-pointer, dereference for local copy
                int ptrFlatIdx = structParamPtrIndex[structParamOp.Index];
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, structParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, refParamPtrVars[structParamOp.Name], varTypes);
                // Dereference: load pointer-to-slot, then load the heap pointer from the slot
                var loadRef = new StdLoadI64Op(refParamPtrVars[structParamOp.Name]);
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
              // Store tag + payload as flat variables, similar to struct literals
              var tempName = $"__enum_{enumConstructOp.Result.Id}";
              var tagOp = new StdConstI64Op(enumConstructOp.Ordinal);
              newBlock.AddOp(tagOp);
              EmitStore(newBlock, tagOp.Result, $"{tempName}.__tag", varTypes);

              var enumTypeDef = (MlirEnumType)module.TypeDefs[enumConstructOp.EnumTypeName];
              var enumCase = enumTypeDef.GetCase(enumConstructOp.CaseName)!;

              // Store associated values as flat payload slots
              // Struct-typed values store their heap pointer as a single i64 slot
              int slotIdx = 0;
              for (int ai = 0; ai < enumConstructOp.Args.Count; ai++) {
                if (structVarNames.TryGetValue(enumConstructOp.Args[ai].Id, out var structSrcName)
                    && enumCase.AssociatedValues![ai].Type is MlirStructType) {
                  // Struct-typed associated value: store heap pointer as single slot
                  var heapPtr = EmitLoad(newBlock, structSrcName, varTypes);
                  EmitStore(newBlock, heapPtr, $"{tempName}.__payload_{slotIdx}", varTypes);
                  slotIdx++;
                } else {
                  var argStdVal = valueMap[enumConstructOp.Args[ai]];
                  EmitStore(newBlock, argStdVal, $"{tempName}.__payload_{slotIdx}", varTypes);
                  slotIdx++;
                }
              }
              // Zero-fill any unused payload slots
              int maxPayload = GetMaxFlatPayloadSlots(enumTypeDef, module.TypeDefs);
              for (int ai = slotIdx; ai < maxPayload; ai++) {
                var zeroOp = new StdConstI64Op(0);
                newBlock.AddOp(zeroOp);
                EmitStore(newBlock, zeroOp.Result, $"{tempName}.__payload_{ai}", varTypes);
              }

              structVarNames[enumConstructOp.Result.Id] = tempName;
              structValueTypes[enumConstructOp.Result.Id] = enumConstructOp.EnumTypeName;
              break;
            }
            case MaxonEnumTagOp enumTagOp: {
              // Load the tag from a flattened associated-value enum
              if (structVarNames.TryGetValue(enumTagOp.EnumValue.Id, out var enumPrefix)) {
                var loaded = EmitLoad(newBlock, $"{enumPrefix}.__tag", varTypes);
                valueMap[enumTagOp.Result] = loaded;
              } else {
                // Fallback: the enum value might be a simple scalar (shouldn't happen for associated-value enums)
                valueMap[enumTagOp.Result] = valueMap[enumTagOp.EnumValue];
              }
              break;
            }
            case MaxonEnumPayloadOp enumPayloadOp: {
              // Load a payload value from flat payload slots
              var enumPrefix2 = structVarNames[enumPayloadOp.EnumValue.Id];
              // Calculate the flat slot offset for this payload index
              var enumType3 = (MlirEnumType)module.TypeDefs[enumPayloadOp.EnumTypeName];
              int flatSlotOffset = GetFlatSlotOffset(enumType3, enumPayloadOp.PayloadIndex, module.TypeDefs);

              if (enumPayloadOp.ResultKind == MaxonValueKind.Struct && enumPayloadOp.ResultStructTypeName != null) {
                // Struct-typed payload: load single heap pointer from the payload slot
                var tempStructName = $"__enum_payload_{enumPayloadOp.Result.Id}";
                var loaded = EmitLoad(newBlock, $"{enumPrefix2}.__payload_{flatSlotOffset}", varTypes);
                EmitStore(newBlock, loaded, tempStructName, varTypes);
                structVarNames[enumPayloadOp.Result.Id] = tempStructName;
                structValueTypes[enumPayloadOp.Result.Id] = enumPayloadOp.ResultStructTypeName;
              } else {
                var loaded2 = EmitLoad(newBlock, $"{enumPrefix2}.__payload_{flatSlotOffset}", varTypes);
                valueMap[enumPayloadOp.Result] = loaded2;
              }
              break;
            }
            case MaxonEnumParamOp enumParamOp: {
              // Check if this is an associated-value enum (passed as heap pointer)
              if (module.TypeDefs.TryGetValue(enumParamOp.EnumTypeName, out var epType)
                  && epType is MlirEnumType epEnumType && epEnumType.HasAssociatedValues) {
                // Receive heap pointer, unpack tag + payload into flat vars
                int ptrFlatIdx = structParamPtrIndex[enumParamOp.Index];
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, enumParamOp.Name, ptrVal));
                if (refParamPtrVars.ContainsKey(enumParamOp.Name)) {
                  // Mutated assoc-value enum: receive pointer-to-heap-pointer
                  EmitStore(newBlock, ptrVal, refParamPtrVars[enumParamOp.Name], varTypes);
                  var loadRef = new StdLoadI64Op(refParamPtrVars[enumParamOp.Name]);
                  newBlock.AddOp(loadRef);
                  var deref = new StdLoadIndirectOp(loadRef.Result, 0, MlirType.I64);
                  newBlock.AddOp(deref);
                  EmitStore(newBlock, (StdI64)deref.Result, enumParamOp.Name, varTypes);
                } else {
                  EmitStore(newBlock, ptrVal, enumParamOp.Name, varTypes);
                }

                UnpackEnumHeapToFlatVars(newBlock, enumParamOp.Name, epEnumType, varTypes, module.TypeDefs);
                structVarNames[enumParamOp.Result.Id] = enumParamOp.Name;
                structValueTypes[enumParamOp.Result.Id] = enumParamOp.EnumTypeName;
              } else if (refParamPtrVars.ContainsKey(enumParamOp.Name)) {
                // Mutated simple enum: receive i64 pointer, dereference for local copy
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                int pFlatIdx = paramFlatIndex.GetValueOrDefault(enumParamOp.Index, enumParamOp.Index);
                newBlock.AddOp(new StdParamOp(pFlatIdx, enumParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, refParamPtrVars[enumParamOp.Name], varTypes);
                var loadRef = new StdLoadI64Op(refParamPtrVars[enumParamOp.Name]);
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
                  && evType is MlirEnumType evEnumType && evEnumType.HasAssociatedValues) {
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
            case MaxonEnumRawValueOp rawValueOp: {
              // The enum's backing value IS the raw value - just pass through
              var enumStdVal = valueMap[rawValueOp.EnumValue];
              valueMap[rawValueOp.Result] = enumStdVal;
              break;
            }
            case MaxonErrorFlagToEnumOp errToEnumOp: {
              if (errToEnumOp.HasAssociatedValues) {
                // Associated-value error: the error flag IS the heap pointer
                // Unpack into flat variables (tag + payload slots)
                var heapPtr = (StdI64)valueMap[errToEnumOp.ErrorFlag];
                var retVarName = $"__error_enum_{errToEnumOp.Result.Id}";
                EmitStore(newBlock, heapPtr, retVarName, varTypes);

                var enumType = (MlirEnumType)module.TypeDefs[errToEnumOp.EnumTypeName];
                UnpackEnumHeapToFlatVars(newBlock, retVarName, enumType, varTypes, module.TypeDefs);
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
              var enumType = (MlirEnumType)module.TypeDefs[strRawOp.EnumTypeName];
              var ordinalValue = (StdI64)valueMap[strRawOp.EnumValue];
              var (buf, len) = EmitStringEnumToString(enumType, ordinalValue, newBlock, result);
              var tempName = $"__enum_rawval_{strRawOp.Result.Id}";
              EmitManagedStructFromBufLen(tempName, buf, len,
                !strRawOp.IsChar, newBlock, varTypes, structVarNames, strRawOp.Result.Id);
              break;
            }
            case MaxonEnumNameOp enumNameOp: {
              var enumType = (MlirEnumType)module.TypeDefs[enumNameOp.EnumTypeName];
              var stdValue = valueMap[enumNameOp.EnumValue];
              StdI64 ordinalValue;
              if (enumType.BackingType == MlirType.I64) {
                ordinalValue = EmitIntEnumToOrdinal(enumType, (StdI64)stdValue, newBlock);
              } else if (enumType.BackingType == MlirType.F64) {
                ordinalValue = EmitFloatEnumToOrdinal(enumType, (StdF64)stdValue, newBlock);
              } else {
                ordinalValue = (StdI64)stdValue;
              }
              var (nameBuf, nameLen) = EmitEnumNameLookup(enumType, ordinalValue, newBlock, result);
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

              // Allocate heap memory for the struct
              var heapPtr = EmitAlloc(newBlock, structType.SizeInBytes);
              EmitStore(newBlock, heapPtr, tempName, varTypes);

              foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                var field = structType.GetField(fieldName)!;
                if (structVarNames.TryGetValue(fieldVal.Id, out var nestedStructName)) {
                  if (structValueTypes.TryGetValue(fieldVal.Id, out var nestedEnumTypeName)
                      && module.TypeDefs.TryGetValue(nestedEnumTypeName, out var nestedEnumDef)
                      && nestedEnumDef is MlirEnumType nestedEnumType && nestedEnumType.HasAssociatedValues) {
                    // Associated-value enums use flat vars but struct fields need a single heap pointer
                    var enumHeapPtr = PackEnumFlatVarsToHeap(newBlock, nestedStructName, nestedEnumType, varTypes, module.TypeDefs);
                    EmitStructFieldStore(newBlock, enumHeapPtr, tempName, field.Offset, MlirType.I64, varTypes);
                  } else {
                    var nestedHeapPtr = EmitLoad(newBlock, nestedStructName, varTypes);
                    EmitStructFieldStore(newBlock, nestedHeapPtr, tempName, field.Offset, MlirType.I64, varTypes);
                  }
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
                  if (structLitOp.TypeName == "__ManagedMemory") {
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

                  var heapBuf = EmitAlloc(newBlock, totalSize.Result);
                  var copyResult = new StdI64(MlirContext.Current.NextId());
                  newBlock.AddOp(new StdCallRuntimeOp("maxon_memcpy", [heapBuf, stackPtr.Result, totalSize.Result], copyResult));

                  if (_trackAllocs) {
                    var bufVar = $"__escape_buf_{MlirContext.Current.NextId()}";
                    EmitStore(newBlock, heapBuf, bufVar, varTypes);
                    var sizeVar = $"__escape_size_{MlirContext.Current.NextId()}";
                    EmitStore(newBlock, totalSize.Result, sizeVar, varTypes);
                    var bufReload = (StdI64)EmitLoad(newBlock, bufVar, varTypes);
                    var sizeReload = (StdI64)EmitLoad(newBlock, sizeVar, varTypes);
                    EmitTrackAlloc(newBlock, bufReload, sizeReload, "array literal");
                    EmitTrackIncref(newBlock, "array literal", 1);
                    heapBuf = (StdI64)EmitLoad(newBlock, bufVar, varTypes);
                  }

                  rdataPtr = heapBuf;
                } else {
                  // Stack buffer is safe — array is only used within this function
                  var leaOp = new StdLeaOp(structLitOp.ArrayLiteralTag);
                  newBlock.AddOp(leaOp);
                  var castOp = new StdPtrToI64Op(leaOp.Result);
                  newBlock.AddOp(castOp);
                  rdataPtr = castOp.Result;
                }

                // Only set capacity for heap-allocated escape buffers, NOT for constant/rdata buffers
                var usedHeapEscape = escapingArrayLiterals.Contains(structLitOp.Result.Id)
                  && !module.ConstantArrayLiterals.ContainsKey(structLitOp.Result.Id);
                if (structLitOp.TypeName == "__ManagedMemory") {
                  // buffer is directly on this struct at offset 0
                  var bufferField = structType.GetField("buffer")!;
                  EmitStructFieldStore(newBlock, rdataPtr, tempName, bufferField.Offset, MlirType.I64, varTypes);
                  // Heap escape buffers are writable — set capacity to element count
                  // so COW check knows this buffer is heap-owned (capacity=0 means read-only/rdata)
                  if (usedHeapEscape) {
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
                  // Heap escape buffers are writable — set capacity to element count
                  if (usedHeapEscape) {
                    var capOp = new StdConstI64Op(structLitOp.ArrayLiteralCount);
                    newBlock.AddOp(capOp);
                    var capField = managedType.GetField("capacity")!;
                    newBlock.AddOp(new StdStoreIndirectOp(capOp.Result, managedHeapPtr, capField.Offset, MlirType.I64));
                  }
                }
              }

              structVarNames[structLitOp.Result.Id] = tempName;
              structValueTypes[structLitOp.Result.Id] = structLitOp.TypeName;

              // Transfer ownership: managed vars used as field values in the struct literal
              // are now owned by the struct literal, not the original variable.
              // For non-variable managed fields (e.g. String literals), emit MOVE+COPY tracking.
              // Skip tracking for internal/builtin struct types (initializers, not transfers).
              if (_trackAllocs && !structLitOp.TypeName.StartsWith("__")
                  && structType != null && GetManagedFieldName(structType) == null) {
                // Check if this struct literal is consumed as a non-self arg to a mutating call.
                // If so, the call handler emits MOVE/COPY tracking — skip Phase 1 here to avoid doubling.
                bool consumedByMutatingCall = false;
                var resultId = structLitOp.Result.Id;
                foreach (var laterOp in block.Operations) {
                  if (laterOp is MaxonCallOp callOp) {
                    for (int ai = 1; ai < callOp.Args.Count; ai++) {
                      if (callOp.Args[ai].Id == resultId) {
                        var calleeName = callOp.Callee;
                        if (mutatingFunctions.Contains(calleeName) || IsMutatingMethodCall(calleeName)) {
                          consumedByMutatingCall = true;
                        }
                        break;
                      }
                    }
                  }
                  if (consumedByMutatingCall) break;
                }

                if (!consumedByMutatingCall) {
                  // Phase 1: MOVE+COPY for non-variable managed fields (String literals, etc.)
                  foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                    // Skip fields backed by tracked managed variables (handled in Phase 2)
                    if (structVarNames.TryGetValue(fieldVal.Id, out var srcName)
                        && managedVarOwners.ContainsKey(srcName))
                      continue;
                    var field = structType.GetField(fieldName)!;
                    if (field.Type is MlirStructType fieldStruct) {
                      var fieldPaths = GetAllManagedBufferPaths("", fieldStruct.Name, module.TypeDefs);
                      if (fieldPaths.Count > 0) {
                        EmitTrackMove(newBlock, "managed");
                        EmitTrackCopy(newBlock, fieldStruct.Name);
                      }
                    }
                  }
                }
              }
              // Phase 2: MOVE for variable-backed managed fields (ownership transfer).
              // Must run for ALL struct types (including auto-generated like __Map_String_i64)
              // to prevent double-free when the parent struct and source variable share buffers.
              if (_trackAllocs && structType != null && GetManagedFieldName(structType) == null) {
                foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                  if (structVarNames.TryGetValue(fieldVal.Id, out var srcVarName)
                      && managedVarOwners.ContainsKey(srcVarName)) {
                    var field = structType.GetField(fieldName)!;
                    if (field.Type is MlirStructType fieldStruct) {
                      var fieldPaths = GetAllManagedBufferPaths("", fieldStruct.Name, module.TypeDefs);
                      if (fieldPaths.Count > 0) {
                        EmitTrackMove(newBlock, srcVarName);
                        managedVarOwners.Remove(srcVarName);
                      }
                    }
                  }
                }
              }
              break;
            }
            case MaxonAssignOp assignOp: {
              // Associated-value enum assignment: copy tag + payload flat vars
              if (structVarNames.TryGetValue(assignOp.Value.Id, out var enumSrc)
                  && structValueTypes.TryGetValue(assignOp.Value.Id, out var enumSrcType)
                  && module.TypeDefs.TryGetValue(enumSrcType, out var enumTypeForAssign)
                  && enumTypeForAssign is MlirEnumType assignEnumType && assignEnumType.HasAssociatedValues) {
                var dstName = assignOp.VarName;
                // Copy tag
                var tagLoaded = EmitLoad(newBlock, $"{enumSrc}.__tag", varTypes);
                EmitStore(newBlock, tagLoaded, $"{dstName}.__tag", varTypes);
                // Copy payload slots (all are flat i64 slots now)
                int maxFlatPayload = GetMaxFlatPayloadSlots(assignEnumType, module.TypeDefs);
                for (int pi = 0; pi < maxFlatPayload; pi++) {
                  var payloadLoaded = EmitLoad(newBlock, $"{enumSrc}.__payload_{pi}", varTypes);
                  EmitStore(newBlock, payloadLoaded, $"{dstName}.__payload_{pi}", varTypes);
                }
                structVarNames[assignOp.Value.Id] = dstName;
                structValueTypes[assignOp.Value.Id] = enumSrcType;
                varNameToStructPrefix[assignOp.VarName] = dstName;
                break;
              }
              // Check structVarNames as fallback to detect struct values that flow
              // through ops not yet updated to track struct kinds.
              if (assignOp.ValueKind == MaxonValueKind.Struct
                  || structVarNames.ContainsKey(assignOp.Value.Id)) {
                // Struct assignment: copy the heap pointer (alias, not deep copy)
                var srcName = structVarNames[assignOp.Value.Id];
                var dstName = assignOp.VarName;
                var structTypeName = structValueTypes.TryGetValue(assignOp.Value.Id, out var svt)
                  ? svt
                  : assignOp.Value is MaxonStruct ms
                    ? ms.TypeName
                    : throw new InvalidOperationException($"No struct type info for value #{assignOp.Value.Id} in assign to '{assignOp.VarName}'");
                // Cleanup old managed memory before reassignment
                if (!isStructInstanceMethod && managedVarOwners.TryGetValue(assignOp.VarName, out var oldBufPaths)) {
                  foreach (var oldBufPath in oldBufPaths) {
                    var oldElementInfo = managedBufferElementInfo.TryGetValue(oldBufPath, out var oldInfo) ? oldInfo : null;
                    EmitManagedCleanup(newBlock, assignOp.VarName, oldBufPath, varTypes, module.TypeDefs, varNameToStructType, oldElementInfo);
                  }
                }
                // Deep-copy when assigning between user variables so value semantics
                // are preserved (mutations to one must not affect the other).
                // Internal temps (__callret_, __struct_, etc.) are already fresh
                // allocations and can be aliased safely.
                if (srcName != dstName) {
                  var srcHeapPtr = EmitLoad(newBlock, srcName, varTypes);
                  if (!srcName.StartsWith("__")) {
                    var copyPtr = EmitStructDeepCopy(newBlock, srcHeapPtr, structTypeName, module.TypeDefs, varTypes);
                    EmitStore(newBlock, copyPtr, dstName, varTypes);
                  } else {
                    EmitStore(newBlock, srcHeapPtr, dstName, varTypes);
                  }
                }
                varNameToStructType[assignOp.VarName] = structTypeName;
                if (IsSelfField(isStructInstanceMethod, selfStructType, assignOp.VarName)) {
                  // Self field: store the heap pointer through self's heap pointer at the field offset
                  var field = selfStructType!.GetField(assignOp.VarName);
                  if (field != null) {
                    var heapPtr2 = EmitLoad(newBlock, dstName, varTypes);
                    EmitStructFieldStore(newBlock, heapPtr2, "self", field.Offset, MlirType.I64, varTypes);
                  }
                }
                structVarNames[assignOp.Value.Id] = dstName;
                structValueTypes[assignOp.Value.Id] = structTypeName;
                varNameToStructPrefix[assignOp.VarName] = dstName;
                // Track managed memory ownership for cleanup
                if (!isStructInstanceMethod && !assignOp.VarName.StartsWith("__")) {
                  var bufferPaths = GetAllManagedBufferPaths(dstName, structTypeName, module.TypeDefs);
                  if (bufferPaths.Count > 0) {
                    managedVarOwners[assignOp.VarName] = bufferPaths;
                    PopulateManagedBufferElementInfo(bufferPaths, dstName, structTypeName, module.TypeDefs, managedBufferElementInfo);
                  }
                }
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
                if (_trackAllocs && cstringResultIds.Contains(assignOp.Value.Id))
                  cstringTrackVars.Add(assignOp.VarName);
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
                         && faEnumDef is MlirEnumType faEnumType && faEnumType.HasAssociatedValues) {
                // Associated-value enums are heap-allocated like structs but need flat vars for downstream match/payload ops
                var tempVarName = $"__field_{fieldAccess.Result.Id}";
                if (fieldDef != null) {
                  var enumPtr = EmitStructFieldLoad(newBlock, structName, fieldDef.Offset, MlirType.I64, varTypes);
                  EmitStore(newBlock, enumPtr, tempVarName, varTypes);
                } else {
                  var loaded = EmitLoad(newBlock, $"{structName}.{fieldAccess.FieldName}", varTypes);
                  EmitStore(newBlock, loaded, tempVarName, varTypes);
                }
                UnpackEnumHeapToFlatVars(newBlock, tempVarName, faEnumType, varTypes, module.TypeDefs);
                structVarNames[fieldAccess.Result.Id] = tempVarName;
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
              if (!valueMap.TryGetValue(fieldAssign.NewValue, out StdValue? mappedVal)) {
                // NewValue might be a struct var ref (e.g., assigning a String to a field).
                // In that case, load its heap pointer from the named variable.
                if (structVarNames.TryGetValue(fieldAssign.NewValue.Id, out var newValStructName)) {
                  mappedVal = EmitLoad(newBlock, newValStructName, varTypes);
                } else {
                  throw new InvalidOperationException($"MaxonFieldAssignOp: NewValue %{fieldAssign.NewValue.Id} not in valueMap or structVarNames for {structName}.{fieldAssign.FieldName} in func {func.Name}");
                }
              }
              // Resolve the field type and offset from the struct type definition
              var faParentTypeName = structValueTypes.TryGetValue(fieldAssign.StructValue.Id, out var faptn) ? faptn : null;
              MlirStructType? faParentStructType = null;
              if (faParentTypeName != null && module.TypeDefs.TryGetValue(faParentTypeName, out var faptDef) && faptDef is MlirStructType fapst)
                faParentStructType = fapst;
              var faFieldDef = faParentStructType?.GetField(fieldAssign.FieldName);

              if (faFieldDef != null) {
                // Store through heap pointer at the field's offset
                // Struct-typed fields store heap pointers (i64), not the struct type directly
                var storeType = faFieldDef.Type is MlirStructType ? MlirType.I64 : faFieldDef.Type;
                EmitStructFieldStore(newBlock, mappedVal, structName, faFieldDef.Offset, storeType, varTypes);
              } else {
                // Fallback: store as named variable
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

              if (!valueMap.TryGetValue(binOp.Lhs, out StdValue? lhs))
                throw new InvalidOperationException($"BinOp LHS %{binOp.Lhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");
              if (!valueMap.TryGetValue(binOp.Rhs, out StdValue? rhs))
                throw new InvalidOperationException($"BinOp RHS %{binOp.Rhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");

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
            case MaxonCondBrOp condBr: {
              var cond = (StdBool)valueMap[condBr.Condition];
              newBlock.AddOp(new StdCondBrOp(cond, condBr.ThenBlock, condBr.ElseBlock));
              break;
            }
            case MaxonBrOp br: {
              // Loop back-edge: cleanup managed vars defined in this iteration
              if (_trackAllocs && processedBlocks.Contains(br.Target)
                  && managedStateAtBlockEntry.TryGetValue(br.Target, out var targetState)) {
                var loopScopedVars = new List<string>();
                foreach (var (varName, _) in managedVarOwners) {
                  if (!targetState.ContainsKey(varName))
                    loopScopedVars.Add(varName);
                }
                foreach (var varName in loopScopedVars) {
                  foreach (var bufferPath in managedVarOwners[varName]) {
                    var elementInfo = managedBufferElementInfo.TryGetValue(bufferPath, out var info) ? info : null;
                    EmitManagedCleanup(newBlock, varName, bufferPath, varTypes, module.TypeDefs, varNameToStructType, elementInfo);
                    // Zero capacity to prevent use-after-free on reinitialization
                    // Navigate through heap pointers to reach the __ManagedMemory struct
                    var managedBase = bufferPath[..bufferPath.LastIndexOf('.')];
                    var navSegments = managedBase.Split('.');
                    var navVar = navSegments[0];
                    var navTypeName = varNameToStructType.TryGetValue(navSegments[0], out var lvst) ? lvst : null;
                    for (int si = 1; si < navSegments.Length; si++) {
                      int navOffset = 0;
                      if (navTypeName != null && module.TypeDefs.TryGetValue(navTypeName, out var lType)
                          && lType is MlirStructType lStruct) {
                        var lField = lStruct.GetField(navSegments[si]);
                        if (lField != null) {
                          navOffset = lField.Offset;
                          navTypeName = lField.Type is MlirStructType fst ? fst.Name : null;
                        }
                      }
                      var nv = $"__loop_nav_{MlirContext.Current.NextId()}";
                      var np = EmitStructFieldLoad(newBlock, navVar, navOffset, MlirType.I64, varTypes);
                      EmitStore(newBlock, np, nv, varTypes);
                      navVar = nv;
                    }
                    var zero = new StdConstI64Op(0);
                    newBlock.AddOp(zero);
                    EmitStructFieldStore(newBlock, zero.Result, navVar, ManagedFieldCapacity, MlirType.I64, varTypes);
                  }
                  // Remove from owner tracking — loop handles its own cleanup
                  managedVarOwners.Remove(varName);
                }
              }
              newBlock.AddOp(new StdBrOp(br.Target));
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
                    // i32 to i64: sign-extend (TODO: implement proper conversion op if needed)
                    // For now, treat as already compatible since both are integer types
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
                    // i32 to i64: for now pass through (TODO: implement proper sign-extension if needed)
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
                structVarNames[globalLoad.Result.Id] = tempName;
                if (globalLoad.StructTypeName != null)
                  structValueTypes[globalLoad.Result.Id] = globalLoad.StructTypeName;
              }
              break;
            }
            case MaxonGlobalStoreOp globalStore: {
              if (globalStore.ValueKind == MaxonValueKind.Struct) {
                // Struct globals store the heap pointer (i64)
                if (valueMap.TryGetValue(globalStore.Value, out var mv) && mv is StdI64 i64Val) {
                  newBlock.AddOp(new StdGlobalStoreI64Op(i64Val, globalStore.GlobalName));
                } else if (structVarNames.TryGetValue(globalStore.Value.Id, out var srcName)) {
                  var heapPtr = (StdI64)EmitLoad(newBlock, srcName, varTypes);
                  newBlock.AddOp(new StdGlobalStoreI64Op(heapPtr, globalStore.GlobalName));
                } else {
                  throw new InvalidOperationException($"Cannot store struct value to global '{globalStore.GlobalName}': no struct tracking info");
                }
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
              LowerCall(callOp, funcLookup, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs, managedVarOwners, mutatingFunctions, fnEnvVarNames);
              if (_trackAllocs && callOp.Callee.EndsWith(".cstr") && callOp.Result != null)
                cstringResultIds.Add(callOp.Result.Id);
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
              LowerReturn(retOp, retStructType, newBlock, valueMap, varTypes, structVarNames, structValueTypes, managedVarOwners, cstringTrackVars, managedBufferElementInfo, module.TypeDefs, varNameToStructType);
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
            case MaxonCStringWriteStdoutOp writeStdoutOp:
              LowerCStringWriteStdout(writeStdoutOp, newBlock, valueMap);
              break;
            case MaxonCStringWriteStderrOp writeStderrOp:
              LowerCStringWriteStderr(writeStderrOp, newBlock, valueMap);
              break;
            case MaxonPanicOp panicOp:
              LowerPanic(panicOp, newBlock, result);
              break;
            case MaxonStringLiteralOp stringLitOp:
              LowerStringLiteral(stringLitOp, newBlock, varTypes, structVarNames, result);
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
                  if (typeName == "__ManagedMemory") {
                    // structName IS the __ManagedMemory heap pointer, buffer at offset 0
                    return (StdValue)(StdI64)EmitStructFieldLoad(newBlock, structName, ManagedFieldBuffer, MlirType.I64, varTypes);
                  } else {
                    // Outer struct: load managed field (offset depends on struct), then buffer
                    // For now, assume managed field is at some known offset. We need type info.
                    // Load the managed field's heap pointer, then load buffer from it.
                    var managedPtr = (StdI64)EmitStructFieldLoad(newBlock, structName, 0, MlirType.I64, varTypes);
                    var managedTmpVar = $"__rt_managed_{MlirContext.Current.NextId()}";
                    EmitStore(newBlock, managedPtr, managedTmpVar, varTypes);
                    return (StdValue)(StdI64)EmitStructFieldLoad(newBlock, managedTmpVar, ManagedFieldBuffer, MlirType.I64, varTypes);
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
            default:
              throw new InvalidOperationException($"No MaxonToStandard conversion for: {op.GetType().Name} ({op.Mnemonic})");
          }
        }
      }
      result.AddFunction(newFunc);
    }
    return result;
  }

}
