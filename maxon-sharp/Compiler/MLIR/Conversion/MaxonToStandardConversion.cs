using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static class MaxonToStandardConversion {
  [ThreadStatic] private static bool _trackAllocs;
  [ThreadStatic] private static MlirModule<StandardOp>? _resultModule;
  [ThreadStatic] private static Dictionary<string, string>? _rdataTagCache;

  public static MlirModule<StandardOp> Run(MlirModule<MaxonOp> module, bool trackAllocs = false) {
    _trackAllocs = trackAllocs;
    _rdataTagCache = [];
    var result = new MlirModule<StandardOp>();
    _resultModule = result;
    result.RdataEntries.AddRange(module.RdataEntries);
    result.Globals.AddRange(module.Globals);
    foreach (var (k, v) in module.TypeDefs) result.TypeDefs[k] = v;

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

    bool hasResetAfterStdlib = false;

    foreach (var func in module.Functions) {
      // Reset IDs after stdlib for stable test output
      if (!hasResetAfterStdlib && !func.IsStdlib) {
        MlirContext.Current.ResetIds();
        hasResetAfterStdlib = true;
      }

      var retStructType = ResolveStructReturnType(func.ReturnType, module.TypeDefs);

      bool isStructInstanceMethod = IsStructInstanceMethod(func);
      bool isEnumInstanceMethod = IsEnumInstanceMethod(func);
      bool isInstanceMethod = isStructInstanceMethod || isEnumInstanceMethod;
      var selfStructType = isStructInstanceMethod ? ResolveStructType((MlirStructType)func.ParamTypes[0], module.TypeDefs) : null;

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
      int flatIdx = newParamNames.Count;

      for (int i = 0; i < func.ParamNames.Count; i++) {
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
          // Simple enum param: pass as scalar
          var backingMlirType = ResolveEnumBackingMlirType(enumParamType);
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(backingMlirType);
          flatIdx++;
        } else if (func.ParamTypes[i] is MlirStructType) {
          // Non-self struct param: pass as pointer (i64)
          structParamPtrIndex[i] = flatIdx;
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(MlirType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is not MlirStructType and not MlirEnumType) {
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(func.ParamTypes[i]);
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
              var stdResult = paramOp.ValueKind.CreateStdValue();
              newBlock.AddOp(new StdParamOp(paramOp.Index, paramOp.Name, stdResult));
              valueMap[paramOp.Result] = stdResult;
              EmitStore(newBlock, stdResult, paramOp.Name, varTypes);
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
              } else {
                // Non-self struct param: receive heap pointer, store under the param name
                int ptrFlatIdx = structParamPtrIndex[structParamOp.Index];
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, structParamOp.Name, ptrVal));
                EmitStore(newBlock, ptrVal, structParamOp.Name, varTypes);
                structVarNames[structParamOp.Result.Id] = structParamOp.Name;
                structValueTypes[structParamOp.Result.Id] = structParamOp.StructTypeName;
                // With heap-allocated reference semantics, struct params are borrowed
                // references — the callee does not own the managed memory.
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
                EmitStore(newBlock, ptrVal, enumParamOp.Name, varTypes);

                // Load tag from offset 0
                var tagLoaded = EmitStructFieldLoad(newBlock, enumParamOp.Name, 0, MlirType.I64, varTypes);
                EmitStore(newBlock, tagLoaded, $"{enumParamOp.Name}.__tag", varTypes);

                // Load payload slots
                int maxPayload = GetMaxFlatPayloadSlots(epEnumType, module.TypeDefs);
                for (int pi = 0; pi < maxPayload; pi++) {
                  var payloadLoaded = EmitStructFieldLoad(newBlock, enumParamOp.Name, 8 + pi * 8, MlirType.I64, varTypes);
                  EmitStore(newBlock, payloadLoaded, $"{enumParamOp.Name}.__payload_{pi}", varTypes);
                }

                structVarNames[enumParamOp.Result.Id] = enumParamOp.Name;
                structValueTypes[enumParamOp.Result.Id] = enumParamOp.EnumTypeName;
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
                  // Nested struct field: load its heap pointer and store it at the field offset
                  var nestedHeapPtr = EmitLoad(newBlock, nestedStructName, varTypes);
                  EmitStructFieldStore(newBlock, nestedHeapPtr, tempName, field.Offset, MlirType.I64, varTypes);
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
                    if (elemSize == 1) {
                      rdataBytes[i] = (byte)constArrayInfo.Values[i];
                    } else {
                      BitConverter.GetBytes(constArrayInfo.Values[i]).CopyTo(rdataBytes, i * elemSize);
                    }
                  }
                  result.RdataEntries.Add((constArrayInfo.RdataLabel, rdataBytes, elemSize));
                  var leaRdataOp = new StdLeaRdataOp(constArrayInfo.RdataLabel);
                  newBlock.AddOp(leaRdataOp);
                  var rdataPtrOp = new StdPtrToI64Op(leaRdataOp.Result);
                  newBlock.AddOp(rdataPtrOp);
                  rdataPtr = rdataPtrOp.Result;
                } else {
                  var leaOp = new StdLeaOp(structLitOp.ArrayLiteralTag);
                  newBlock.AddOp(leaOp);
                  var castOp = new StdPtrToI64Op(leaOp.Result);
                  newBlock.AddOp(castOp);
                  rdataPtr = castOp.Result;
                }

                if (structLitOp.TypeName == "__ManagedMemory") {
                  // buffer is directly on this struct at offset 0
                  var bufferField = structType.GetField("buffer")!;
                  EmitStructFieldStore(newBlock, rdataPtr, tempName, bufferField.Offset, MlirType.I64, varTypes);
                } else {
                  // Outer struct (Array, Vector): load the managed field's heap pointer, then store buffer on it
                  var managedField = structType.GetField("managed")!;
                  var managedHeapPtr = (StdI64)EmitStructFieldLoad(newBlock, tempName, managedField.Offset, MlirType.I64, varTypes);
                  // Store buffer on the __ManagedMemory heap object
                  var managedType = (MlirStructType)managedField.Type;
                  var bufferField = managedType.GetField("buffer")!;
                  newBlock.AddOp(new StdStoreIndirectOp(rdataPtr, managedHeapPtr, bufferField.Offset, MlirType.I64));
                }
              }

              structVarNames[structLitOp.Result.Id] = tempName;
              structValueTypes[structLitOp.Result.Id] = structLitOp.TypeName;

              // Transfer ownership: managed vars used as field values in the struct literal
              // are now owned by the struct literal, not the original variable.
              // For non-variable managed fields (e.g. String literals), emit MOVE+COPY tracking.
              // Skip tracking for internal/builtin struct types (initializers, not transfers).
              if (_trackAllocs && !structLitOp.TypeName.StartsWith("__")
                  && GetManagedFieldName(structType) == null) {
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
              if (_trackAllocs && GetManagedFieldName(structType) == null) {
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
                varNameToStructPrefix[fieldAccess.FieldName] = tempVarName;
                // Propagate type info for the nested struct field
                if (fieldDef?.Type is MlirStructType fieldStructType)
                  structValueTypes[fieldAccess.Result.Id] = fieldStructType.Name;
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
              var mappedVal = valueMap[fieldAssign.NewValue];
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

              var key = (binOp.Operator, binOp.OperandKind);
              if (!BinOpFactories.TryGetValue(key, out var factory))
                throw new InvalidOperationException($"Unsupported binop: {binOp.Operator} on {binOp.OperandKind}");

              if (!valueMap.TryGetValue(binOp.Lhs, out StdValue? lhs))
                throw new InvalidOperationException($"BinOp LHS %{binOp.Lhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");
              if (!valueMap.TryGetValue(binOp.Rhs, out StdValue? rhs))
                throw new InvalidOperationException($"BinOp RHS %{binOp.Rhs.Id} not in valueMap in func {func.Name} block {block.Name}, op: {binOp.Operator} {binOp.OperandKind}");
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
              var input = (StdF64)valueMap[truncOp.Input];
              var stdOp = new StdFpToSiOp(input);
              newBlock.AddOp(stdOp);
              valueMap[truncOp.Result] = stdOp.Result;
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
              LowerUnaryF64(valueMap, newBlock, absOp.Input, absOp.Result,
                input => new StdAbsF64Op(input));
              break;
            case MaxonSqrtOp sqrtOp:
              LowerUnaryF64(valueMap, newBlock, sqrtOp.Input, sqrtOp.Result,
                input => new StdSqrtF64Op(input));
              break;
            case MaxonFloorOp floorOp:
              LowerUnaryF64(valueMap, newBlock, floorOp.Input, floorOp.Result,
                input => new StdFloorF64Op(input));
              break;
            case MaxonCeilOp ceilOp:
              LowerUnaryF64(valueMap, newBlock, ceilOp.Input, ceilOp.Result,
                input => new StdCeilF64Op(input));
              break;
            case MaxonRoundOp roundOp:
              LowerUnaryF64(valueMap, newBlock, roundOp.Input, roundOp.Result,
                input => new StdRoundF64Op(input));
              break;
            case MaxonMinOp minOp:
              LowerBinaryF64(valueMap, newBlock, minOp.Lhs, minOp.Rhs, minOp.Result,
                (l, r) => new StdMinF64Op(l, r));
              break;
            case MaxonMaxOp maxOp:
              LowerBinaryF64(valueMap, newBlock, maxOp.Lhs, maxOp.Rhs, maxOp.Result,
                (l, r) => new StdMaxF64Op(l, r));
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
                  // Byte/int to int: pass through (bytes are already stored as I64)
                  if (input is StdI64 i64) {
                    valueMap[castOp.Result] = i64;
                  } else if (input is StdI32 i32) {
                    // i32 to i64: for now pass through (TODO: implement proper sign-extension if needed)
                    throw new InvalidOperationException("i32 to int conversion not yet implemented");
                  } else if (input is StdF64 f64) {
                    var fpToSi = new StdFpToSiOp(f64);
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
                    var siToFp = new StdSiToFpOp(i64);
                    newBlock.AddOp(siToFp);
                    valueMap[castOp.Result] = siToFp.Result;
                  } else if (input is StdI32 i32) {
                    // i32 to float: need to convert i32 to i64 first, then to float
                    throw new InvalidOperationException("i32 to float conversion not yet implemented");
                  } else if (input is StdF64 f64) {
                    valueMap[castOp.Result] = f64;
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
                default:
                  throw new InvalidOperationException($"Unsupported cast target kind: {castOp.TargetKind}");
              }
              break;
            }
            case MaxonGlobalLoadOp globalLoad: {
              switch (globalLoad.ValueKind) {
                case MaxonValueKind.Integer: {
                  var loadOp = new StdGlobalLoadI64Op(globalLoad.GlobalName);
                  newBlock.AddOp(loadOp);
                  valueMap[globalLoad.Result] = loadOp.Result;
                  break;
                }
                case MaxonValueKind.Float: {
                  var loadOp = new StdGlobalLoadF64Op(globalLoad.GlobalName);
                  newBlock.AddOp(loadOp);
                  valueMap[globalLoad.Result] = loadOp.Result;
                  break;
                }
                case MaxonValueKind.Bool: {
                  var loadOp = new StdGlobalLoadI1Op(globalLoad.GlobalName);
                  newBlock.AddOp(loadOp);
                  valueMap[globalLoad.Result] = loadOp.Result;
                  break;
                }
                default:
                  throw new InvalidOperationException($"Unsupported global variable type: {globalLoad.ValueKind}");
              }
              break;
            }
            case MaxonGlobalStoreOp globalStore: {
              var mappedValue = valueMap[globalStore.Value];
              switch (globalStore.ValueKind) {
                case MaxonValueKind.Integer:
                  newBlock.AddOp(new StdGlobalStoreI64Op((StdI64)mappedValue, globalStore.GlobalName));
                  break;
                case MaxonValueKind.Float:
                  newBlock.AddOp(new StdGlobalStoreF64Op((StdF64)mappedValue, globalStore.GlobalName));
                  break;
                case MaxonValueKind.Bool:
                  newBlock.AddOp(new StdGlobalStoreI1Op((StdBool)mappedValue, globalStore.GlobalName));
                  break;
                default:
                  throw new InvalidOperationException($"Unsupported global variable type: {globalStore.ValueKind}");
              }
              break;
            }
            case MaxonTryCallOp tryCallOp:
              LowerTryCall(tryCallOp, funcLookup, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs);
              break;
            case MaxonCallOp callOp:
              if (TryLowerPrimitiveMethod(callOp, newBlock, valueMap)) break;
              LowerCall(callOp, funcLookup, newBlock, valueMap, varTypes, structVarNames, structValueTypes, module.TypeDefs, managedVarOwners, mutatingFunctions);
              if (_trackAllocs && callOp.Callee.EndsWith(".cstr") && callOp.Result != null)
                cstringResultIds.Add(callOp.Result.Id);
              break;
            case MaxonFunctionRefOp fnRefOp:
              LowerFunctionRef(fnRefOp, newBlock, valueMap);
              break;
            case MaxonFunctionParamOp fnParamOp:
              LowerFunctionParam(fnParamOp, newBlock, valueMap, varTypes);
              break;
            case MaxonFunctionVarRefOp fnVarRefOp:
              LowerFunctionVarRef(fnVarRefOp, newBlock, valueMap);
              break;
            case MaxonIndirectCallOp indirectCallOp:
              LowerIndirectCall(indirectCallOp, newBlock, valueMap, varTypes, structVarNames, module.TypeDefs);
              break;
            case MaxonReturnOp retOp:
              LowerReturn(retOp, retStructType, newBlock, valueMap, varTypes, structVarNames, structValueTypes, managedVarOwners, cstringTrackVars, managedBufferElementInfo, module.TypeDefs, varNameToStructType);
              break;
            case MaxonThrowOp throwOp:
              LowerThrow(throwOp, newBlock, valueMap);
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

  private static MlirType ResolveEnumBackingMlirType(MlirEnumType enumType) {
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
    Dictionary<string, List<string>> managedVarOwners,
    HashSet<string> mutatingFunctions) {
    LowerCallCore(callOp.Callee, callOp.Args, callOp.Result, callOp.ResultKind,
      isTryCall: false, funcLookup, block, valueMap, varTypes, structVarNames,
      structValueTypes, typeDefs, managedVarOwners, mutatingFunctions);
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
    Dictionary<string, List<string>>? managedVarOwners,
    HashSet<string>? mutatingFunctions,
    MaxonValue? errorFlagValue = null) {

    var calleeFunc = ResolveCallee(callee, funcLookup);
    // Use the resolved fully-qualified name for call emission (e.g., "String.hash" → "stdlib.String.hash")
    var resolvedCallee = calleeFunc.Name;
    var calleeRetStructType = ResolveStructReturnType(calleeFunc.ReturnType, typeDefs);
    bool calleeIsStructInstance = IsStructInstanceMethod(calleeFunc);

    var newArgs = new List<StdValue>();

    // With reference semantics, struct args are borrowed references — the caller retains
    // ownership and cleans up. But we still emit MOVE tracking for debugging visibility.
    if (_trackAllocs && managedVarOwners != null && mutatingFunctions != null) {
      bool calleeMutates = mutatingFunctions.Contains(callee) || IsMutatingMethodCall(callee);
      if (calleeMutates) {
        // Emit MOVE for non-self struct args passed to mutating functions (borrow tracking).
        // Unlike value semantics, we do NOT remove from managedVarOwners — caller still owns.
        for (int i = 0; i < args.Count; i++) {
          bool isSelfArg = calleeIsStructInstance && i == 0;
          if (!isSelfArg && structVarNames.TryGetValue(args[i].Id, out var argVarName)
              && managedVarOwners.ContainsKey(argVarName)) {
            EmitTrackMove(block, argVarName);
          }
        }
        // For struct instance method calls where non-self params are managed structs,
        // emit MOVE for the self arg's managed field (the container accepting ownership)
        bool hasManagedStructParam = false;
        bool hasNestedManagedStructParam = false;
        if (calleeIsStructInstance) {
          for (int i = 1; i < calleeFunc.ParamTypes.Count; i++) {
            if (calleeFunc.ParamTypes[i] is MlirStructType paramSt) {
              if (GetManagedFieldName(paramSt) != null) {
                hasManagedStructParam = true;
              } else if (GetManagedElementFieldInfo(paramSt).Count > 0) {
                hasNestedManagedStructParam = true;
              }
            }
          }
        }
        if (calleeIsStructInstance && hasManagedStructParam && !hasNestedManagedStructParam) {
          var selfArg = args[0];
          if (structVarNames.TryGetValue(selfArg.Id, out _)) {
            var selfStructType2 = (MlirStructType)calleeFunc.ParamTypes[0];
            var managedFieldName = GetManagedFieldName(selfStructType2);
            if (managedFieldName != null) {
              EmitTrackMove(block, managedFieldName);
            }
          }
        }
        // For struct params with nested managed fields (e.g. Item with String field),
        // emit MOVE/COPY for each managed field within the struct
        if (calleeIsStructInstance && hasNestedManagedStructParam) {
          for (int i = 1; i < calleeFunc.ParamTypes.Count; i++) {
            if (calleeFunc.ParamTypes[i] is MlirStructType paramSt) {
              var managedFields = GetManagedElementFieldInfo(paramSt);
              foreach (var (_, typeName) in managedFields) {
                EmitTrackMove(block, "managed");
                EmitTrackCopy(block, typeName);
              }
            }
          }
        }
      }
    }

    FlattenCallArgs(args, calleeFunc, block, valueMap, varTypes, structVarNames, newArgs, callee, typeDefs);

    // Check if callee returns an associated-value enum (passed as heap pointer)
    bool calleeRetAssocEnum = calleeFunc.ReturnType is MlirEnumType cret && cret.HasAssociatedValues;

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
        EmitStore(block, callResult, retVarName, varTypes);
        structVarNames[result.Id] = retVarName;
        structValueTypes[result.Id] = calleeRetStructType.Name;
      } else if (calleeRetAssocEnum && callResult != null) {
        // Associated-value enum return: unpack heap pointer into flat vars
        var retEnumType = (MlirEnumType)calleeFunc.ReturnType!;
        var retVarName = $"__callret_{result.Id}";
        EmitStore(block, callResult, retVarName, varTypes);

        // Load tag from offset 0
        var tagLoaded = EmitStructFieldLoad(block, retVarName, 0, MlirType.I64, varTypes);
        EmitStore(block, tagLoaded, $"{retVarName}.__tag", varTypes);

        // Load payload slots
        int maxPayload = GetMaxFlatPayloadSlots(retEnumType, typeDefs);
        for (int pi = 0; pi < maxPayload; pi++) {
          var payloadLoaded = EmitStructFieldLoad(block, retVarName, 8 + pi * 8, MlirType.I64, varTypes);
          EmitStore(block, payloadLoaded, $"{retVarName}.__payload_{pi}", varTypes);
        }

        structVarNames[result.Id] = retVarName;
        structValueTypes[result.Id] = retEnumType.Name;
      } else if (callResult != null) {
        valueMap[result] = callResult;
      }
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
    Dictionary<string, List<string>> managedVarOwners,
    HashSet<string> cstringTrackVars,
    Dictionary<string, List<(int offset, string typeName)>> managedBufferElementInfo,
    Dictionary<string, MlirType> typeDefs,
    Dictionary<string, string> varNameToStructType) {

    // Error propagation: forward the error flag to the caller
    if (retOp.IsErrorPropagation) {
      var mappedErrFlag = valueMap[retOp.Value!];
      block.AddOp(new StdErrorReturnOp(mappedErrFlag));
      return;
    }

    // No self write-back needed: with heap refs, all field mutations go through
    // the heap pointer directly, so the caller sees changes automatically.

    // Associated-value enum return: pack into heap block
    if (retOp.Value != null
        && structVarNames.TryGetValue(retOp.Value.Id, out var enumRetPrefix)
        && structValueTypes.TryGetValue(retOp.Value.Id, out var enumRetTypeName)
        && typeDefs.TryGetValue(enumRetTypeName, out var enumRetTypeDef)
        && enumRetTypeDef is MlirEnumType enumRetType && enumRetType.HasAssociatedValues) {
      int maxPayload = GetMaxFlatPayloadSlots(enumRetType, typeDefs);
      int heapSize = 8 + maxPayload * 8;
      var heapPtr = EmitAlloc(block, heapSize);

      // Store tag at offset 0
      var tagVal = EmitLoad(block, $"{enumRetPrefix}.__tag", varTypes);
      block.AddOp(new StdStoreIndirectOp(tagVal, heapPtr, 0, MlirType.I64));

      // Store payload slots
      for (int pi = 0; pi < maxPayload; pi++) {
        var payloadVal = EmitLoad(block, $"{enumRetPrefix}.__payload_{pi}", varTypes);
        block.AddOp(new StdStoreIndirectOp(payloadVal, heapPtr, 8 + pi * 8, MlirType.I64));
      }

      EmitReturnCleanup(block, cstringTrackVars, managedVarOwners, varTypes, typeDefs, varNameToStructType, managedBufferElementInfo);
      block.AddOp(new StdReturnOp(heapPtr));
      return;
    }

    if (retStructType != null && retOp.Value != null) {
      // Struct return: return the heap pointer as i64
      StdValue retHeapPtr;
      if (structVarNames.TryGetValue(retOp.Value.Id, out var srcName)) {
        retHeapPtr = EmitLoad(block, srcName, varTypes);
        managedVarOwners.Remove(srcName);
      } else {
        // Value is already an i64 (e.g. struct element pointer from managed memory)
        retHeapPtr = valueMap[retOp.Value];
      }

      bool hasCleanup2 = _trackAllocs && (managedVarOwners.Count > 0 || cstringTrackVars.Count > 0);
      if (hasCleanup2) {
        var retSave = $"__ret_save_{MlirContext.Current.NextId()}";
        EmitStore(block, retHeapPtr, retSave, varTypes);
        EmitReturnCleanup(block, cstringTrackVars, managedVarOwners, varTypes, typeDefs, varNameToStructType, managedBufferElementInfo);
        var retReload = EmitLoad(block, retSave, varTypes);
        block.AddOp(new StdReturnOp(retReload));
      } else {
        EmitReturnCleanup(block, cstringTrackVars, managedVarOwners, varTypes, typeDefs, varNameToStructType, managedBufferElementInfo);
        block.AddOp(new StdReturnOp(retHeapPtr));
      }
    } else {
      StdValue? newRetVal = retOp.Value != null ? valueMap[retOp.Value] : null;
      bool hasCleanup = _trackAllocs && (managedVarOwners.Count > 0 || cstringTrackVars.Count > 0);
      if (newRetVal != null && hasCleanup) {
        // Save return value to stack before cleanup (cleanup calls clobber registers)
        var retVarName = $"__ret_save_{MlirContext.Current.NextId()}";
        EmitStore(block, newRetVal, retVarName, varTypes);

        EmitReturnCleanup(block, cstringTrackVars, managedVarOwners, varTypes, typeDefs, varNameToStructType, managedBufferElementInfo);

        var retReload = EmitLoad(block, retVarName, varTypes);
        block.AddOp(new StdReturnOp(retReload));
      } else {
        EmitReturnCleanup(block, cstringTrackVars, managedVarOwners, varTypes, typeDefs, varNameToStructType, managedBufferElementInfo);
        block.AddOp(new StdReturnOp(newRetVal));
      }
    }
  }

  private static void EmitReturnCleanup(
    MlirBlock<StandardOp> block,
    HashSet<string> cstringTrackVars,
    Dictionary<string, List<string>> managedVarOwners,
    Dictionary<string, string> varTypes,
    Dictionary<string, MlirType> typeDefs,
    Dictionary<string, string> varNameToStructType,
    Dictionary<string, List<(int offset, string typeName)>>? managedBufferElementInfo = null) {
    foreach (var csVar in cstringTrackVars)
      EmitTrackCleanup(block, csVar);
    foreach (var (varName, bufferPaths) in managedVarOwners) {
      foreach (var bufferPath in bufferPaths) {
        var elementInfo = managedBufferElementInfo != null && managedBufferElementInfo.TryGetValue(bufferPath, out var info) ? info : null;
        EmitManagedCleanup(block, varName, bufferPath, varTypes, typeDefs, varNameToStructType, elementInfo);
      }
    }
  }

  private static void LowerThrow(
    MaxonThrowOp throwOp,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {
    // The error value is the enum ordinal. Add 1 to make it non-zero (0 = success).
    var errorVal = (StdI64)valueMap[throwOp.ErrorValue];
    var oneOp = new StdConstI64Op(1);
    block.AddOp(oneOp);
    var addOp = new StdAddI64Op(errorVal, oneOp.Result);
    block.AddOp(addOp);
    block.AddOp(new StdErrorReturnOp(addOp.Result));
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
      var enumType = (MlirEnumType)typeDefs[enumTypeName];
      LowerEnumFromRawValue(tryCallOp, enumType, block, valueMap, varTypes, structVarNames);
      return;
    }
    if (tryCallOp.Callee.StartsWith("__enum_fromName:")) {
      var enumTypeName = tryCallOp.Callee["__enum_fromName:".Length..];
      var enumType = (MlirEnumType)typeDefs[enumTypeName];
      LowerEnumFromName(tryCallOp, enumType, block, valueMap, varTypes, structVarNames, structValueTypes);
      return;
    }
    LowerCallCore(tryCallOp.Callee, tryCallOp.Args, tryCallOp.Result,
      tryCallOp.ResultKind, isTryCall: true, funcLookup, block, valueMap, varTypes,
      structVarNames, structValueTypes, typeDefs,
      null, null, tryCallOp.ErrorFlag);
  }

  /// <summary>
  /// Lowers EnumType.fromRawValue(arg) inline as a comparison chain.
  /// For simple/int-backed enums: compares arg against each case's ordinal/raw value.
  /// For float-backed enums: compares arg against each case's float raw value.
  /// For string/char-backed enums: compares arg string against each case's string via memcmp.
  /// Sets error flag to 0 on match, 1 on no match. Result is the matched ordinal.
  /// </summary>
  private static void LowerEnumFromRawValue(
    MaxonTryCallOp tryCallOp,
    MlirEnumType enumType,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var inputArg = tryCallOp.Args[0];

    if (enumType.BackingType is MlirStringBackingType or MlirCharBackingType) {
      // String/char-backed: input is a managed struct, compare against each case's string
      LowerEnumFromRawValueString(tryCallOp, enumType, block, valueMap, varTypes, structVarNames);
    } else if (enumType.BackingType == MlirType.F64) {
      // Float-backed: compare float values, result is the input value itself
      var inputVal = (StdF64)valueMap[inputArg];

      var noMatchFlag = new StdConstI64Op(1);
      block.AddOp(noMatchFlag);
      StdI64 currentErrorFlag = noMatchFlag.Result;

      foreach (var enumCase in enumType.Cases) {
        var caseRawConst = new StdConstF64Op((double)enumCase.RawValue!);
        block.AddOp(caseRawConst);
        var cmpOp = new StdCmpF64Op("eq", inputVal, caseRawConst.Result);
        block.AddOp(cmpOp);

        var zeroFlag = new StdConstI64Op(0);
        block.AddOp(zeroFlag);
        var selectFlag = new StdSelectI64Op(cmpOp.Result, zeroFlag.Result, currentErrorFlag);
        block.AddOp(selectFlag);
        currentErrorFlag = selectFlag.Result;
      }

      valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;
      // The result is the input float value (which IS the enum's runtime representation)
      valueMap[tryCallOp.Result!] = inputVal;
    } else if (enumType.BackingType == MlirType.I64 || enumType.BackingType == null) {
      // Simple (null backing) or int-backed: compare integer values
      var inputVal = (StdI64)valueMap[inputArg];

      var noMatchFlag = new StdConstI64Op(1);
      block.AddOp(noMatchFlag);
      var defaultOrd = new StdConstI64Op(0);
      block.AddOp(defaultOrd);
      StdI64 currentErrorFlag = noMatchFlag.Result;
      StdI64 currentResult = defaultOrd.Result;

      foreach (var enumCase in enumType.Cases) {
        long rawValue = enumType.BackingType == MlirType.I64
          ? (long)enumCase.RawValue!
          : enumCase.Ordinal;

        var caseRawConst = new StdConstI64Op(rawValue);
        block.AddOp(caseRawConst);
        var cmpOp = new StdCmpI64Op("eq", inputVal, caseRawConst.Result);
        block.AddOp(cmpOp);

        // On match: error flag = 0, result = ordinal (or raw value for int-backed)
        var zeroFlag = new StdConstI64Op(0);
        block.AddOp(zeroFlag);
        var selectFlag = new StdSelectI64Op(cmpOp.Result, zeroFlag.Result, currentErrorFlag);
        block.AddOp(selectFlag);
        currentErrorFlag = selectFlag.Result;

        // Result is the runtime value of the enum (ordinal for simple, raw value for int-backed)
        var resultConst = new StdConstI64Op(enumType.BackingType == MlirType.I64 ? rawValue : enumCase.Ordinal);
        block.AddOp(resultConst);
        var selectResult = new StdSelectI64Op(cmpOp.Result, resultConst.Result, currentResult);
        block.AddOp(selectResult);
        currentResult = selectResult.Result;
      }

      valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;
      valueMap[tryCallOp.Result!] = currentResult;
    } else {
      throw new InvalidOperationException($"Unsupported enum backing type for fromRawValue: {enumType.BackingType}");
    }
  }

  /// <summary>
  /// Handles fromRawValue for string/char-backed enums.
  /// Compares input string against each case's string value using length check + memcmp.
  /// </summary>
  private static void LowerEnumFromRawValueString(
    MaxonTryCallOp tryCallOp,
    MlirEnumType enumType,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var inputArg = tryCallOp.Args[0];
    // Input is a String or Character managed struct - load _managed heap pointer, then buffer and length
    var inputStructName = structVarNames[inputArg.Id];
    var inputManagedPtr = (StdI64)EmitStructFieldLoad(block, inputStructName, 0, MlirType.I64, varTypes);
    var inputManagedVar = $"__frv_managed_{MlirContext.Current.NextId()}";
    EmitStore(block, inputManagedPtr, inputManagedVar, varTypes);
    var inputBuf = (StdI64)EmitStructFieldLoad(block, inputManagedVar, ManagedFieldBuffer, MlirType.I64, varTypes);
    var inputLen = (StdI64)EmitStructFieldLoad(block, inputManagedVar, ManagedFieldLength, MlirType.I64, varTypes);

    var noMatchFlag = new StdConstI64Op(1);
    block.AddOp(noMatchFlag);
    var defaultOrd = new StdConstI64Op(0);
    block.AddOp(defaultOrd);
    StdI64 currentErrorFlag = noMatchFlag.Result;
    StdI64 currentResult = defaultOrd.Result;

    foreach (var enumCase in enumType.Cases) {
      var caseString = (string)enumCase.RawValue!;
      var rdataLabel = $"__enum_frv_{enumType.Name}_{enumCase.Name}_{MlirContext.Current.NextId()}";
      var (caseBuf, caseLen) = EmitRdataLiteral(caseString, rdataLabel, block, _resultModule!);
      var bothMatch = EmitStringEquals(inputBuf, inputLen, caseBuf, caseLen, block);

      var zeroFlag = new StdConstI64Op(0);
      block.AddOp(zeroFlag);
      var selectFlag = new StdSelectI64Op(bothMatch, zeroFlag.Result, currentErrorFlag);
      block.AddOp(selectFlag);
      currentErrorFlag = selectFlag.Result;

      var ordConst = new StdConstI64Op(enumCase.Ordinal);
      block.AddOp(ordConst);
      var selectResult = new StdSelectI64Op(bothMatch, ordConst.Result, currentResult);
      block.AddOp(selectResult);
      currentResult = selectResult.Result;
    }

    valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;
    // String/char-backed enums store ordinals at runtime
    valueMap[tryCallOp.Result!] = currentResult;
  }

  /// <summary>
  /// Lowers EnumType.fromName(nameArg, ...associatedArgs) inline as a comparison chain.
  /// Compares input string against each case name using length check + memcmp.
  /// For associated-value enums with compile-time literal name: constructs the full enum.
  /// For associated-value enums with dynamic name: only matches cases without associated values.
  /// </summary>
  private static void LowerEnumFromName(
    MaxonTryCallOp tryCallOp,
    MlirEnumType enumType,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes) {

    var nameArg = tryCallOp.Args[0];
    // Name is always a String managed struct - load _managed heap pointer, then buffer and length
    var nameStructName = structVarNames[nameArg.Id];
    var nameManagedPtr = (StdI64)EmitStructFieldLoad(block, nameStructName, 0, MlirType.I64, varTypes);
    var nameManagedVar = $"__fn_managed_{MlirContext.Current.NextId()}";
    EmitStore(block, nameManagedPtr, nameManagedVar, varTypes);
    var nameBuf = (StdI64)EmitStructFieldLoad(block, nameManagedVar, ManagedFieldBuffer, MlirType.I64, varTypes);
    var nameLen = (StdI64)EmitStructFieldLoad(block, nameManagedVar, ManagedFieldLength, MlirType.I64, varTypes);

    bool hasAssociatedValues = enumType.HasAssociatedValues;
    bool hasExtraArgs = tryCallOp.Args.Count > 1;

    if (hasAssociatedValues) {
      // For associated-value enums, construct as flat struct (tag + payload)
      LowerEnumFromNameAssociated(tryCallOp, enumType, block, valueMap, varTypes,
        structVarNames, structValueTypes, nameBuf, nameLen, hasExtraArgs);
    } else {
      // Simple/raw-value enum: result is an ordinal/raw value
      LowerEnumFromNameSimple(tryCallOp, enumType, block, valueMap, varTypes, nameBuf, nameLen);
    }
  }

  private static void LowerEnumFromNameSimple(
    MaxonTryCallOp tryCallOp,
    MlirEnumType enumType,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    StdI64 nameBuf, StdI64 nameLen) {

    var noMatchFlag = new StdConstI64Op(1);
    block.AddOp(noMatchFlag);
    var defaultResult = new StdConstI64Op(0);
    block.AddOp(defaultResult);
    StdI64 currentErrorFlag = noMatchFlag.Result;
    StdI64 currentResult = defaultResult.Result;

    foreach (var enumCase in enumType.Cases) {
      var rdataLabel = $"__enum_fn_{enumType.Name}_{enumCase.Name}_{MlirContext.Current.NextId()}";
      var (caseBuf, caseLen) = EmitRdataLiteral(enumCase.Name, rdataLabel, block, _resultModule!);
      var isMatch = EmitStringEquals(nameBuf, nameLen, caseBuf, caseLen, block);

      var zeroFlag = new StdConstI64Op(0);
      block.AddOp(zeroFlag);
      var selectFlag = new StdSelectI64Op(isMatch, zeroFlag.Result, currentErrorFlag);
      block.AddOp(selectFlag);
      currentErrorFlag = selectFlag.Result;

      long runtimeValue = enumType.BackingType == MlirType.I64
        ? (long)enumCase.RawValue!
        : enumCase.Ordinal;
      var resultConst = new StdConstI64Op(runtimeValue);
      block.AddOp(resultConst);
      var selectResult = new StdSelectI64Op(isMatch, resultConst.Result, currentResult);
      block.AddOp(selectResult);
      currentResult = selectResult.Result;
    }

    valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;

    if (enumType.BackingType == MlirType.F64) {
      // Float-backed fromName: convert ordinal to float via i64 bit pattern select chain,
      // then reinterpret the bits as f64 through a stack variable
      var bitsVarName = $"__enum_fn_bits_{MlirContext.Current.NextId()}";
      var defaultBits = new StdConstI64Op(0);
      block.AddOp(defaultBits);
      StdI64 currentBits = defaultBits.Result;
      foreach (var enumCase in enumType.Cases) {
        long floatBits = BitConverter.DoubleToInt64Bits((double)enumCase.RawValue!);
        var caseBitsConst = new StdConstI64Op(floatBits);
        block.AddOp(caseBitsConst);
        var ordCheckConst = new StdConstI64Op(enumCase.Ordinal);
        block.AddOp(ordCheckConst);
        var cmpOrdConst = new StdCmpI64Op("eq", currentResult, ordCheckConst.Result);
        block.AddOp(cmpOrdConst);
        var selectBits = new StdSelectI64Op(cmpOrdConst.Result, caseBitsConst.Result, currentBits);
        block.AddOp(selectBits);
        currentBits = selectBits.Result;
      }
      // Store as i64, then load as f64 (reinterpret via same stack slot)
      EmitStore(block, currentBits, bitsVarName, varTypes);
      varTypes[bitsVarName] = "f64";
      var floatResult = (StdF64)EmitLoad(block, bitsVarName, varTypes);
      valueMap[tryCallOp.Result!] = floatResult;
    } else {
      valueMap[tryCallOp.Result!] = currentResult;
    }
  }

  private static void LowerEnumFromNameAssociated(
    MaxonTryCallOp tryCallOp,
    MlirEnumType enumType,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    StdI64 nameBuf, StdI64 nameLen,
    bool hasExtraArgs) {

    // For associated-value enums, store as flat struct (tag + payload)
    var tempName = $"__enum_{tryCallOp.Result!.Id}";
    int maxPayload = enumType.Cases.Max(c => c.AssociatedValues?.Count ?? 0);

    // Initialize with default tag=0 and zero payload
    var defaultTag = new StdConstI64Op(0);
    block.AddOp(defaultTag);
    var tagVarName = $"{tempName}.__tag";
    EmitStore(block, defaultTag.Result, tagVarName, varTypes);
    for (int i = 0; i < maxPayload; i++) {
      var zeroPayload = new StdConstI64Op(0);
      block.AddOp(zeroPayload);
      EmitStore(block, zeroPayload.Result, $"{tempName}.__payload_{i}", varTypes);
    }

    var noMatchFlag = new StdConstI64Op(1);
    block.AddOp(noMatchFlag);
    StdI64 currentErrorFlag = noMatchFlag.Result;

    foreach (var enumCase in enumType.Cases) {
      bool caseHasAssocValues = enumCase.AssociatedValues is { Count: > 0 };

      // For dynamic name (no extra args), skip cases that need associated values
      if (!hasExtraArgs && caseHasAssocValues) continue;

      var rdataLabel = $"__enum_fna_{enumType.Name}_{enumCase.Name}_{MlirContext.Current.NextId()}";
      var (caseBuf, caseLen) = EmitRdataLiteral(enumCase.Name, rdataLabel, block, _resultModule!);
      var isMatch = EmitStringEquals(nameBuf, nameLen, caseBuf, caseLen, block);

      var zeroFlag = new StdConstI64Op(0);
      block.AddOp(zeroFlag);
      var selectFlag = new StdSelectI64Op(isMatch, zeroFlag.Result, currentErrorFlag);
      block.AddOp(selectFlag);
      currentErrorFlag = selectFlag.Result;

      // On match, set the tag
      var tagConst = new StdConstI64Op(enumCase.Ordinal);
      block.AddOp(tagConst);
      var currentTag = (StdI64)EmitLoad(block, tagVarName, varTypes);
      var selectTag = new StdSelectI64Op(isMatch, tagConst.Result, currentTag);
      block.AddOp(selectTag);
      EmitStore(block, selectTag.Result, tagVarName, varTypes);

      if (hasExtraArgs && caseHasAssocValues) {
        for (int ai = 0; ai < enumCase.AssociatedValues!.Count; ai++) {
          var avArg = tryCallOp.Args[1 + ai];
          var avStdVal = valueMap[avArg];
          var currentPayload = (StdI64)EmitLoad(block, $"{tempName}.__payload_{ai}", varTypes);
          var selectPayload = new StdSelectI64Op(isMatch, (StdI64)avStdVal, currentPayload);
          block.AddOp(selectPayload);
          EmitStore(block, selectPayload.Result, $"{tempName}.__payload_{ai}", varTypes);
        }
      }
    }

    valueMap[tryCallOp.ErrorFlag] = currentErrorFlag;
    structVarNames[tryCallOp.Result!.Id] = tempName;
    structValueTypes[tryCallOp.Result!.Id] = enumType.Name;
  }

  /// <summary>
  /// Lower call/try_call arguments for the standard calling convention.
  /// Struct args are passed as heap pointers (i64) directly.
  /// Associated-value enum args are packed into heap blocks and passed as pointers.
  /// Returns null (no self buffer needed with heap-allocated structs).
  /// </summary>
  private static string? FlattenCallArgs(
    List<MaxonValue> args,
    MlirFunction<MaxonOp> calleeFunc,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    List<StdValue> newArgs,
    string calleeName,
    Dictionary<string, MlirType> typeDefs) {
    bool calleeIsEnumInstance = IsEnumInstanceMethod(calleeFunc);

    for (int i = 0; i < args.Count; i++) {
      var arg = args[i];
      if (calleeIsEnumInstance && i == 0) {
        newArgs.Add(valueMap[arg]);
      } else if (calleeFunc.ParamTypes[i] is MlirEnumType enumArgType && enumArgType.HasAssociatedValues
                 && structVarNames.TryGetValue(arg.Id, out var enumPrefix)) {
        // Associated-value enum: pack tag + payload into a heap block
        int maxPayload = GetMaxFlatPayloadSlots(enumArgType, typeDefs);
        int heapSize = 8 + maxPayload * 8;
        var heapPtr = EmitAlloc(block, heapSize);

        // Store tag at offset 0
        var tagVal = EmitLoad(block, $"{enumPrefix}.__tag", varTypes);
        block.AddOp(new StdStoreIndirectOp(tagVal, heapPtr, 0, MlirType.I64));

        // Store payload slots
        for (int pi = 0; pi < maxPayload; pi++) {
          var payloadVal = EmitLoad(block, $"{enumPrefix}.__payload_{pi}", varTypes);
          block.AddOp(new StdStoreIndirectOp(payloadVal, heapPtr, 8 + pi * 8, MlirType.I64));
        }

        newArgs.Add(heapPtr);
      } else if (calleeFunc.ParamTypes[i] is MlirEnumType) {
        newArgs.Add(valueMap[arg]);
      } else if (calleeFunc.ParamTypes[i] is MlirStructType && structVarNames.TryGetValue(arg.Id, out var argStructName)) {
        // Struct arg: pass the heap pointer directly
        var heapPtr = EmitLoad(block, argStructName, varTypes);
        newArgs.Add(heapPtr);
      } else if (calleeFunc.ParamTypes[i] is MlirStructType && valueMap.TryGetValue(arg, out var rawPtrValue)) {
        // Struct arg from managed memory get — the value is already a pointer
        newArgs.Add(rawPtrValue);
      } else if (calleeFunc.ParamTypes[i] is not MlirStructType and not MlirEnumType) {
        newArgs.Add(valueMap[arg]);
      } else {
        throw new InvalidOperationException($"Unhandled call argument type: {calleeFunc.ParamTypes[i].GetType().Name} for arg {i} in call to '{calleeName}'");
      }
    }
    return null;
  }

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
  /// Resolve the standard-level result value type for a call or try_call.
  /// </summary>
  private static StdValue? ResolveCallResultType(MaxonValueKind? resultKind, MlirType? calleeReturnType) {
    if (resultKind == MaxonValueKind.Enum && calleeReturnType is MlirEnumType retEnumType) {
      var backingType = ResolveEnumBackingMlirType(retEnumType);
      if (backingType == MlirType.F64) return new StdF64(MlirContext.Current.NextId());
      return new StdI64(MlirContext.Current.NextId());
    }
    return resultKind?.CreateStdValue();
  }

  private static StdF64 PromoteToF64(StdValue value, MlirBlock<StandardOp> block) {
    if (value is StdF64 f64) {
      return f64;
    } else if (value is StdI64 i64) {
      var conv = new StdSiToFpOp(i64);
      block.AddOp(conv);
      return conv.Result;
    } else {
      throw new InvalidOperationException($"Cannot promote {value.GetType().Name} to F64");
    }
  }

  private static void LowerUnaryF64(
    Dictionary<MaxonValue, StdValue> valueMap,
    MlirBlock<StandardOp> block,
    MaxonValue maxonInput, MaxonValue maxonResult,
    Func<StdF64, StdUnaryF64Op> factory) {
    var input = PromoteToF64(valueMap[maxonInput], block);
    var stdOp = factory(input);
    block.AddOp(stdOp);
    valueMap[maxonResult] = stdOp.Result;
  }

  private static void LowerBinaryF64(
    Dictionary<MaxonValue, StdValue> valueMap,
    MlirBlock<StandardOp> block,
    MaxonValue maxonLhs, MaxonValue maxonRhs, MaxonValue maxonResult,
    Func<StdF64, StdF64, StdBinaryF64Op> factory) {
    var lhs = PromoteToF64(valueMap[maxonLhs], block);
    var rhs = PromoteToF64(valueMap[maxonRhs], block);
    var stdOp = factory(lhs, rhs);
    block.AddOp(stdOp);
    valueMap[maxonResult] = stdOp.Result;
  }

  private static void EmitStore(MlirBlock<StandardOp> block, StdValue value, string varName, Dictionary<string, string> varTypes) {
    switch (value) {
      case StdI64 i64:
        block.AddOp(new StdStoreI64Op(i64, varName));
        varTypes[varName] = "i64";
        break;
      case StdI32:
        throw new InvalidOperationException("StdI32 store not yet implemented (no StdStoreI32Op)");
      case StdF64 f64:
        block.AddOp(new StdStoreF64Op(f64, varName));
        varTypes[varName] = "f64";
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
    var result = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_alloc", [size], result));
    return result;
  }

  private static StdI64 EmitAlloc(MlirBlock<StandardOp> block, long constSize) {
    var sizeOp = new StdConstI64Op(constSize);
    block.AddOp(sizeOp);
    return EmitAlloc(block, sizeOp.Result);
  }

  // __ManagedMemory field offsets (all fields are 8 bytes)
  private const int ManagedFieldBuffer = 0;
  private const int ManagedFieldLength = 8;
  private const int ManagedFieldCapacity = 16;
  private const int ManagedFieldElementSize = 24;

  /// Deep-copy a heap-allocated struct: allocate a new block, copy scalar fields,
  /// and recursively deep-copy any nested struct fields so mutations are independent.
  private static StdI64 EmitStructDeepCopy(
    MlirBlock<StandardOp> block, StdValue srcHeapPtr, string structTypeName,
    Dictionary<string, MlirType> typeDefs, Dictionary<string, string> varTypes) {
    if (!typeDefs.TryGetValue(structTypeName, out var typeDef) || typeDef is not MlirStructType structType)
      throw new InvalidOperationException($"EmitStructDeepCopy: unknown struct type '{structTypeName}'");

    var newHeapPtr = EmitAlloc(block, structType.SizeInBytes);
    foreach (var field in structType.Fields) {
      var loadOp = new StdLoadIndirectOp(srcHeapPtr, field.Offset, MlirType.I64);
      block.AddOp(loadOp);
      if (field.Type is MlirStructType nestedStructType) {
        // Nested struct field: recursively deep-copy
        var nestedCopy = EmitStructDeepCopy(block, loadOp.Result, nestedStructType.Name, typeDefs, varTypes);
        block.AddOp(new StdStoreIndirectOp(nestedCopy, newHeapPtr, field.Offset, MlirType.I64));
      } else {
        // Scalar field: copy the value directly
        block.AddOp(new StdStoreIndirectOp(loadOp.Result, newHeapPtr, field.Offset, MlirType.I64));
      }
    }
    return newHeapPtr;
  }

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
      case "i1": {
        var loadOp = new StdLoadI1Op(varName);
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

  private static bool IsStructInstanceMethod<T>(MlirFunction<T> func) where T : IPrintableOp =>
    func.ParamNames.Count > 0
    && func.ParamNames[0] == "self"
    && func.ParamTypes[0] is MlirStructType;

  private static bool IsSelfField(bool isStructInstanceMethod, MlirStructType? selfStructType, string name) =>
    isStructInstanceMethod && selfStructType != null && selfStructType.GetField(name) != null;

  private static bool IsEnumInstanceMethod<T>(MlirFunction<T> func) where T : IPrintableOp =>
    func.ParamNames.Count > 0
    && func.ParamNames[0] == "self"
    && func.ParamTypes[0] is MlirEnumType;

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
  private static int GetMaxFlatPayloadSlots(MlirEnumType enumType, Dictionary<string, MlirType> typeDefs) {
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
  private static int GetFlatSlotOffset(MlirEnumType enumType, int payloadIndex, Dictionary<string, MlirType> typeDefs) {
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

  // ============================================================================
  // Managed memory lowering helpers
  // ============================================================================

  /// <summary>
  /// Resolve the struct variable name for a managed memory value.
  /// The managed value may be tracked as a struct variable or may need to be loaded.
  /// </summary>
  private static string ResolveManagedVarName(
    MaxonValue managedValue,
    Dictionary<int, string> structVarNames) {
    if (structVarNames.TryGetValue(managedValue.Id, out var varName))
      return varName;
    throw new InvalidOperationException($"Managed memory value %{managedValue.Id} not found in struct variable names");
  }

  /// <summary>
  /// Load the buffer pointer from a heap-allocated __ManagedMemory struct.
  /// The managedVarName variable holds the heap pointer to the __ManagedMemory struct.
  /// buffer is at offset 0 (first field).
  /// </summary>
  private static StdI64 LoadManagedBuffer(
    MlirBlock<StandardOp> block,
    string managedVarName,
    Dictionary<string, string> varTypes) {
    return (StdI64)EmitStructFieldLoad(block, managedVarName, 0, MlirType.I64, varTypes);
  }

  /// <summary>
  /// Compute address: buffer + index * elementSize (runtime element size)
  /// </summary>
  private static StdI64 ComputeElementAddress(
    MlirBlock<StandardOp> block,
    StdI64 buffer,
    StdI64 index,
    StdI64 elementSize) {
    var offsetOp = new StdMulI64Op(index, elementSize);
    block.AddOp(offsetOp);
    var addrOp = new StdAddI64Op(buffer, offsetOp.Result);
    block.AddOp(addrOp);
    return addrOp.Result;
  }

  /// <summary>
  /// __managed_memory_get_unchecked(managed, index): load element from heap buffer.
  /// buffer[index] = *(buffer + index * elementSize)
  /// Element size is read from the managed struct's element_size field.
  /// For struct elements, returns the address of the element in the buffer directly
  /// (the struct data is stored inline, not as a pointer).
  /// </summary>
  private static void LowerManagedMemGet(
    MaxonManagedMemGetOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var addr = ComputeElementAddress(block, buffer, index, elemSize);

    if (op.IsStructElement) {
      // Struct elements are heap pointers stored in the buffer (8 bytes each).
      // Load the pointer, store in a temp var, and register as a struct variable.
      var loadOp = new StdLoadIndirectOp(addr, 0, MlirType.I64);
      block.AddOp(loadOp);
      var tempName = $"__memget_{MlirContext.Current.NextId()}";
      EmitStore(block, loadOp.Result, tempName, varTypes);
      structVarNames[op.Result.Id] = tempName;
      if (op.StructElementTypeName != null)
        structValueTypes[op.Result.Id] = op.StructElementTypeName;
      valueMap[op.Result] = loadOp.Result;
    } else {
      // Determine load type based on result kind
      // For byte/bool, use I8 which triggers zero-extending byte load in x86 codegen
      var elemType = GetManagedMemElementType(op.ResultKind, "LowerManagedMemGet");
      var loadOp = new StdLoadIndirectOp(addr, 0, elemType);
      block.AddOp(loadOp);
      valueMap[op.Result] = loadOp.Result;
    }
  }

  /// <summary>
  /// __managed_memory_set_at(managed, index, value): store element into heap buffer.
  /// Element size is read from the managed struct's element_size field.
  /// For struct elements, copies the full struct data inline into the buffer
  /// (not just a pointer) so elements survive stack frame reuse in loops.
  /// </summary>
  private static void LowerManagedMemSet(
    MaxonManagedMemSetOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
    EmitCowCheck(block, managedVarName, varTypes, elemSize);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var addr = ComputeElementAddress(block, buffer, index, elemSize);

    if (op.IsStructElement) {
      // Struct elements are heap pointers — store the pointer value (i64) into the buffer
      var srcName = structVarNames[op.Value.Id];
      var srcHeapPtr = EmitLoad(block, srcName, varTypes);
      block.AddOp(new StdStoreIndirectOp(srcHeapPtr, addr, 0, MlirType.I64));
    } else {
      // Scalar elements: store directly
      var value = valueMap[op.Value];
      var elemType = GetManagedMemElementType(op.ElementKind, "LowerManagedMemSet");
      block.AddOp(new StdStoreIndirectOp(value, addr, 0, elemType));
    }
  }

  /// <summary>
  /// __managed_memory_create(count, elementSize): allocate heap buffer.
  /// Returns new __ManagedMemory struct (buffer, length, capacity, element_size).
  /// </summary>
  private static void LowerManagedMemCreate(
    MaxonManagedMemCreateOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var count = (StdI64)valueMap[op.Count];
    // Compute byte size = count * elementSize
    var sizeOp = new StdConstI64Op(op.ElementSize);
    block.AddOp(sizeOp);
    var byteSizeOp = new StdMulI64Op(count, sizeOp.Result);
    block.AddOp(byteSizeOp);
    var allocResult = EmitAlloc(block, byteSizeOp.Result);
    // Create a heap-allocated __ManagedMemory struct (4 fields * 8 bytes = 32 bytes)
    var tempName = $"__managed_create_{op.Result.Id}";
    var managedPtr = EmitAlloc(block, 32);
    EmitStore(block, managedPtr, tempName, varTypes);
    // Store fields via indirect access on the heap object
    EmitStructFieldStore(block, allocResult, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
    EmitStructFieldStore(block, count, tempName, ManagedFieldLength, MlirType.I64, varTypes);
    EmitStructFieldStore(block, count, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
    EmitStructFieldStore(block, sizeOp.Result, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
    structVarNames[op.Result.Id] = tempName;
  }

  /// <summary>
  /// __managed_memory_grow(managed, newCapacity): grow heap buffer to new capacity.
  /// Uses realloc to grow (or allocate) the buffer, then updates managed struct fields.
  /// Element size is read from the managed struct's element_size field.
  /// </summary>
  private static void LowerManagedMemGrow(
    MaxonManagedMemGrowOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);

    // Load element_size from the managed struct via heap pointer
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

    EmitCowCheck(block, managedVarName, varTypes, elemSize);
    var newCap = (StdI64)valueMap[op.NewCapacity];

    // Load buffer pointer (now guaranteed to be heap-allocated after COW check)
    var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);

    // Compute new byte size = newCap * elementSize
    var newByteSizeOp = new StdMulI64Op(newCap, elemSize);
    block.AddOp(newByteSizeOp);

    // Realloc: grows buffer in-place or allocates new, copies old data, frees old
    var newBufferResult = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_realloc", [oldBuffer, newByteSizeOp.Result], newBufferResult));

    StdI64 newBufReload;
    if (_trackAllocs) {
      var byteSizeVar = $"__grow_bytesize_{MlirContext.Current.NextId()}";
      EmitStore(block, newByteSizeOp.Result, byteSizeVar, varTypes);
      var newBufVar = $"__grow_newbuf_{MlirContext.Current.NextId()}";
      EmitStore(block, newBufferResult, newBufVar, varTypes);

      var bufReload = (StdI64)EmitLoad(block, newBufVar, varTypes);
      var sizeReload = (StdI64)EmitLoad(block, byteSizeVar, varTypes);
      EmitTrackAlloc(block, bufReload, sizeReload, "array grow");
      EmitTrackIncref(block, "array grow", 1);

      newBufReload = (StdI64)EmitLoad(block, newBufVar, varTypes);
    } else {
      newBufReload = newBufferResult;
    }

    // Update managed struct fields through heap pointer
    EmitStructFieldStore(block, newBufReload, managedVarName, ManagedFieldBuffer, MlirType.I64, varTypes);
    EmitStructFieldStore(block, newCap, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
    // No write-through needed: with heap refs, all field stores go through
    // the heap pointer directly, so the caller sees changes automatically.
  }

  /// <summary>
  /// __managed_memory_shift_right/left(managed, index, count): shift elements in buffer.
  /// For shift_right: move elements [index..index+count-1] to [index+1..index+count] (backwards copy)
  /// For shift_left: move elements [index+1..index+count] to [index..index+count-1] (forward copy)
  /// Implemented as element-by-element copy using indirect load/store.
  /// Element size is read from the managed struct's element_size field.
  /// </summary>
  private static void LowerManagedMemShift(
    MaxonManagedMemShiftOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
    EmitCowCheck(block, managedVarName, varTypes, elemSize);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var count = (StdI64)valueMap[op.Count];

    if (op.ShiftRight) {
      // Shift right: copy from [index+count-1] down to [index], moving each one position right
      // Effectively: for i in (count-1)..0: buffer[index+i+1] = buffer[index+i]
      // We implement this as a memcopy of count elements starting at index, shifted by +1
      var totalOffsetOp = new StdMulI64Op(index, elemSize);
      block.AddOp(totalOffsetOp);
      var srcAddr = new StdAddI64Op(buffer, totalOffsetOp.Result);
      block.AddOp(srcAddr);
      // Dest is src + elementSize (one position to the right)
      var dstAddr = new StdAddI64Op(srcAddr.Result, elemSize);
      block.AddOp(dstAddr);
      // Byte count
      var bytesOp = new StdMulI64Op(count, elemSize);
      block.AddOp(bytesOp);
      // Use memmove-style copy (handles overlapping regions)
      block.AddOp(new StdMemCopyOp(srcAddr.Result, dstAddr.Result, bytesOp.Result));
    } else {
      // Shift left: copy from [index+1] forward, moving each one position left
      var oneConst = new StdConstI64Op(1);
      block.AddOp(oneConst);
      var srcIndex = new StdAddI64Op(index, oneConst.Result);
      block.AddOp(srcIndex);
      var srcOffset = new StdMulI64Op(srcIndex.Result, elemSize);
      block.AddOp(srcOffset);
      var srcAddr = new StdAddI64Op(buffer, srcOffset.Result);
      block.AddOp(srcAddr);
      var dstOffset = new StdMulI64Op(index, elemSize);
      block.AddOp(dstOffset);
      var dstAddr = new StdAddI64Op(buffer, dstOffset.Result);
      block.AddOp(dstAddr);
      var bytesOp = new StdMulI64Op(count, elemSize);
      block.AddOp(bytesOp);
      block.AddOp(new StdMemCopyOp(srcAddr.Result, dstAddr.Result, bytesOp.Result));
    }
  }

  /// <summary>
  /// __managed_memory_byte_at(managed, index): load a single byte from the managed buffer.
  /// Returns the byte zero-extended to i64.
  /// </summary>
  private static void LowerManagedMemByteGet(
    MaxonManagedMemByteGetOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var index = (StdI64)valueMap[op.Index];
    // Compute address: buffer + index (element size is 1 byte)
    var addrOp = new StdAddI64Op(buffer, index);
    block.AddOp(addrOp);
    var loadOp = new StdLoadIndirectOp(addrOp.Result, 0, MlirType.I8);
    block.AddOp(loadOp);
    valueMap[op.Result] = loadOp.Result;
  }

  /// <summary>
  /// Emit a COW (copy-on-write) check for a managed memory struct.
  /// If capacity == 0, the buffer is read-only (rdata) and must be copied to a writable heap allocation.
  /// Updates buffer and capacity fields on the managed struct (and writes through to self if needed).
  /// Element size is passed dynamically (read from the struct's element_size field).
  /// </summary>
  private static void EmitCowCheck(
    MlirBlock<StandardOp> block,
    string managedVarName,
    Dictionary<string, string> varTypes,
    StdI64 elemSize) {
    var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var capacity = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);

    var uid = MlirContext.Current.NextId();
    var cowLenVar = $"__cow_len_{uid}";
    EmitStore(block, length, cowLenVar, varTypes);

    var newBuffer = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_cow_check", [oldBuffer, capacity, length, elemSize], newBuffer));

    EmitStructFieldStore(block, newBuffer, managedVarName, ManagedFieldBuffer, MlirType.I64, varTypes);
    // If COW triggered (capacity was 0), new capacity = length; otherwise keep original
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var cmpOp = new StdCmpI64Op("eq", capacity, zeroConst.Result);
    block.AddOp(cmpOp);
    var lenReload = (StdI64)EmitLoad(block, cowLenVar, varTypes);
    var selectOp = new StdSelectI64Op(cmpOp.Result, lenReload, capacity);
    block.AddOp(selectOp);
    EmitStructFieldStore(block, selectOp.Result, managedVarName, ManagedFieldCapacity, MlirType.I64, varTypes);
    // No write-through needed: heap refs ensure mutations are visible to callers.
  }

  /// <summary>
  /// __managed_memory_set_byte(managed, index, value): store a single byte to the managed buffer.
  /// Performs COW check before writing. Element size is read from the struct for COW allocation.
  /// </summary>
  private static void LowerManagedMemByteSet(
    MaxonManagedMemByteSetOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var elemSize = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldElementSize, MlirType.I64, varTypes);
    EmitCowCheck(block, managedVarName, varTypes, elemSize);

    // Now perform the actual byte write using the writable buffer
    var bufReload = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldBuffer, MlirType.I64, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var value = valueMap[op.Value];
    var addrOp = new StdAddI64Op(bufReload, index);
    block.AddOp(addrOp);
    block.AddOp(new StdStoreIndirectOp(value, addrOp.Result, 0, MlirType.I8));
  }

  /// <summary>
  /// __cstring_to_managed(cstrPtr): convert a null-terminated C string to __ManagedMemory.
  /// Computes strlen, allocates buffer, copies bytes, returns managed struct.
  /// </summary>
  private static void LowerCStringToManaged(
    MaxonCStringToManagedOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var cstrPtr = (StdI64)valueMap[op.CstrPtr];

    // Get string length
    var lenResult = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_strlen", [cstrPtr], lenResult));

    // Allocate buffer
    var allocResult = EmitAlloc(block, lenResult);

    // Copy bytes from cstring to new buffer
    var copyResult = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_memcpy", [allocResult, cstrPtr, lenResult], copyResult));

    // Heap-allocate __ManagedMemory struct and store fields
    var tempName = $"__from_cstring_{op.Result.Id}";
    var managedPtr = EmitAlloc(block, 32);
    EmitStore(block, managedPtr, tempName, varTypes);
    var elemSizeOp = new StdConstI64Op(1);
    block.AddOp(elemSizeOp);
    EmitStructFieldStore(block, allocResult, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
    EmitStructFieldStore(block, lenResult, tempName, ManagedFieldLength, MlirType.I64, varTypes);
    EmitStructFieldStore(block, lenResult, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
    EmitStructFieldStore(block, elemSizeOp.Result, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
    structVarNames[op.Result.Id] = tempName;
  }

  /// <summary>
  /// __managed_memory_to_cstring(managed): return a null-terminated C string pointer.
  /// Calls maxon_to_cstring runtime which checks if buffer[length] is already '\0'.
  /// If so, returns the buffer directly (no allocation). Otherwise, allocates a copy
  /// with null terminator appended. This avoids unnecessary copying for non-slice strings.
  /// </summary>
  private static void LowerManagedToCString(
    MaxonManagedToCStringOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.Managed, structVarNames);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var length = (StdI64)EmitStructFieldLoad(block, managedVarName, ManagedFieldLength, MlirType.I64, varTypes);

    var result = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_to_cstring", [buffer, length], result));
    valueMap[op.Result] = result;
  }

  /// <summary>
  /// __cstring_write_stdout(cstrPtr): write a null-terminated C string to stdout.
  /// Calls maxon_write_stdout runtime function which uses GetStdHandle + WriteFile.
  /// Returns the number of bytes written.
  /// </summary>
  private static void LowerCStringWriteStdout(
    MaxonCStringWriteStdoutOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {
    var cstrPtr = (StdI64)valueMap[op.CstrPtr];
    var result = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_write_stdout", [cstrPtr], result));
    valueMap[op.Result] = result;
  }

  /// <summary>
  /// Emits rdata storage and __ManagedMemory field initialization for a UTF-8 literal value.
  /// Stores bytes in .rdata, emits LEA + buffer/length/capacity fields.
  /// </summary>
  /// <summary>
  /// Encode a string literal into rdata and emit LEA + PtrToI64 to get a buffer pointer and length.
  /// </summary>
  private static (StdI64 Buffer, StdI64 Length) EmitRdataLiteral(
    string value,
    string rdataLabel,
    MlirBlock<StandardOp> block,
    MlirModule<StandardOp> result) {
    var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(value);
    var nullTerminated = new byte[utf8Bytes.Length + 1];
    Array.Copy(utf8Bytes, nullTerminated, utf8Bytes.Length);
    result.RdataEntries.Add((rdataLabel, nullTerminated, 1));

    var leaOp = new StdLeaRdataOp(rdataLabel);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);

    var lenOp = new StdConstI64Op(utf8Bytes.Length);
    block.AddOp(lenOp);

    return (ptrOp.Result, lenOp.Result);
  }

  private static string EmitManagedMemoryLiteral(
    string value,
    int resultId,
    string rdataPrefix,
    string tempPrefix,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    MlirModule<StandardOp> result) {
    var rdataLabel = $"__{rdataPrefix}_{resultId}";
    var (bufferPtr, lengthVal) = EmitRdataLiteral(value, rdataLabel, block, result);

    // Heap-allocate __ManagedMemory struct (32 bytes)
    var managedName = $"__{tempPrefix}_managed_{resultId}";
    var managedPtr = EmitAlloc(block, 32);
    EmitStore(block, managedPtr, managedName, varTypes);

    // Store __ManagedMemory fields via heap pointer
    EmitStructFieldStore(block, bufferPtr, managedName, ManagedFieldBuffer, MlirType.I64, varTypes);
    EmitStructFieldStore(block, lengthVal, managedName, ManagedFieldLength, MlirType.I64, varTypes);
    var capConst = new StdConstI64Op(0);
    block.AddOp(capConst);
    EmitStructFieldStore(block, capConst.Result, managedName, ManagedFieldCapacity, MlirType.I64, varTypes);
    var elemSizeConst = new StdConstI64Op(1);
    block.AddOp(elemSizeConst);
    EmitStructFieldStore(block, elemSizeConst.Result, managedName, ManagedFieldElementSize, MlirType.I64, varTypes);

    // Heap-allocate outer struct (String/Character: _managed + _iterPos = 16 bytes)
    var tempName = $"__{tempPrefix}_{resultId}";
    var outerPtr = EmitAlloc(block, 16);
    EmitStore(block, outerPtr, tempName, varTypes);

    // Store _managed heap pointer in outer struct at offset 0
    var managedPtrReload = EmitLoad(block, managedName, varTypes);
    EmitStructFieldStore(block, managedPtrReload, tempName, 0, MlirType.I64, varTypes);

    structVarNames[resultId] = tempName;
    return tempName;
  }

  private static void LowerStringLiteral(
    MaxonStringLiteralOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    MlirModule<StandardOp> result) {
    var tempName = EmitManagedMemoryLiteral(op.Value, op.Result.Id, "str", "strtmp", block, varTypes, structVarNames, result);

    // Store _iterPos at offset 8 (second field in String struct)
    var iterConst = new StdConstI64Op(0);
    block.AddOp(iterConst);
    EmitStructFieldStore(block, iterConst.Result, tempName, 8, MlirType.I64, varTypes);
  }

  private static void LowerCharLiteral(
    MaxonCharLiteralOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    MlirModule<StandardOp> result) {
    EmitManagedMemoryLiteral(op.Value, op.Result.Id, "chr", "chrtmp", block, varTypes, structVarNames, result);
  }

  private static void LowerStringInterp(
    MaxonStringInterpOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    MlirModule<StandardOp> result) {

    var partInfos = new List<(StdI64 Buffer, StdI64 Length)>();

    foreach (var (IsLiteral, LiteralValue, ExprValue, FormatSpec) in op.Parts) {
      if (IsLiteral) {
        if (string.IsNullOrEmpty(LiteralValue)) continue;

        var litId = MlirContext.Current.NextId();
        var rdataLabel = $"__interp_lit_{litId}";
        partInfos.Add(EmitRdataLiteral(LiteralValue!, rdataLabel, block, result));
      } else {
        var exprValue = ExprValue!;

        if (structVarNames.TryGetValue(exprValue.Id, out var managedVarName)) {
          partInfos.Add(EmitStructInterpolation(managedVarName, block, varTypes));
        } else if (exprValue is MaxonInteger or MaxonByte) {
          partInfos.Add(EmitI64ToString((StdI64)valueMap[exprValue], block, varTypes));
        } else if (exprValue is MaxonFloat) {
          partInfos.Add(EmitF64ToString((StdF64)valueMap[exprValue], block, varTypes));
        } else if (exprValue is MaxonBool) {
          partInfos.Add(EmitBoolToString((StdBool)valueMap[exprValue], block, varTypes));
        } else if (exprValue is MaxonEnum enumValue) {
          partInfos.Add(EmitEnumToString(enumValue, valueMap, block, varTypes, result));
        } else {
          throw new InvalidOperationException(
            $"String interpolation: unsupported expression type {exprValue.GetType().Name} for value %{exprValue.Id}");
        }
      }
    }

    if (partInfos.Count == 0) {
      var tempName = EmitManagedMemoryLiteral("", op.Result.Id, "interp", "interptmp", block, varTypes, structVarNames, result);
      var iterConst = new StdConstI64Op(0);
      block.AddOp(iterConst);
      EmitStore(block, iterConst.Result, $"{tempName}._iterPos", varTypes);
      return;
    }

    // Compute total length
    StdI64 totalLen;
    if (partInfos.Count == 1) {
      totalLen = partInfos[0].Length;
    } else {
      var sum = new StdAddI64Op(partInfos[0].Length, partInfos[1].Length);
      block.AddOp(sum);
      totalLen = sum.Result;
      for (int i = 2; i < partInfos.Count; i++) {
        var add = new StdAddI64Op(totalLen, partInfos[i].Length);
        block.AddOp(add);
        totalLen = add.Result;
      }
    }

    // Allocate buffer (totalLen + 1 for null terminator)
    var oneOp = new StdConstI64Op(1);
    block.AddOp(oneOp);
    var allocSize = new StdAddI64Op(totalLen, oneOp.Result);
    block.AddOp(allocSize);

    var allocResult = EmitAlloc(block, allocSize.Result);

    // Store all values to stack variables since rep movsb clobbers RSI, RDI, RCX
    var interpOffsetVar = $"__interp_offset_{op.Result.Id}";
    var interpBufVar = $"__interp_buf_{op.Result.Id}";
    var interpTotalLenVar = $"__interp_totallen_{op.Result.Id}";
    var zeroOp = new StdConstI64Op(0);
    block.AddOp(zeroOp);
    EmitStore(block, zeroOp.Result, interpOffsetVar, varTypes);
    EmitStore(block, allocResult, interpBufVar, varTypes);
    EmitStore(block, totalLen, interpTotalLenVar, varTypes);

    // Store each part's buffer and length to stack variables
    var partBufVars = new string[partInfos.Count];
    var partLenVars = new string[partInfos.Count];
    for (int i = 0; i < partInfos.Count; i++) {
      partBufVars[i] = $"__interp_partbuf_{op.Result.Id}_{i}";
      partLenVars[i] = $"__interp_partlen_{op.Result.Id}_{i}";
      EmitStore(block, partInfos[i].Buffer, partBufVars[i], varTypes);
      EmitStore(block, partInfos[i].Length, partLenVars[i], varTypes);
    }

    for (int i = 0; i < partInfos.Count; i++) {
      var curBuf = (StdI64)EmitLoad(block, interpBufVar, varTypes);
      var curOff = (StdI64)EmitLoad(block, interpOffsetVar, varTypes);
      var dstAddr = new StdAddI64Op(curBuf, curOff);
      block.AddOp(dstAddr);

      var srcBuf = (StdI64)EmitLoad(block, partBufVars[i], varTypes);
      var srcLen = (StdI64)EmitLoad(block, partLenVars[i], varTypes);
      block.AddOp(new StdMemCopyOp(srcBuf, dstAddr.Result, srcLen));

      // Reload offset and length (clobbered by memcopy) and advance
      var curOff2 = (StdI64)EmitLoad(block, interpOffsetVar, varTypes);
      var partLen = (StdI64)EmitLoad(block, partLenVars[i], varTypes);
      var newOffset = new StdAddI64Op(curOff2, partLen);
      block.AddOp(newOffset);
      EmitStore(block, newOffset.Result, interpOffsetVar, varTypes);
    }

    // Create String struct with heap-allocated __ManagedMemory
    var tempName2 = $"__interptmp_{op.Result.Id}";

    // Heap-allocate __ManagedMemory (32 bytes)
    var interpManagedName = $"__interp_managed_{op.Result.Id}";
    var interpManagedPtr = EmitAlloc(block, 32);
    EmitStore(block, interpManagedPtr, interpManagedName, varTypes);

    var finalBuf = (StdI64)EmitLoad(block, interpBufVar, varTypes);
    EmitStructFieldStore(block, finalBuf, interpManagedName, ManagedFieldBuffer, MlirType.I64, varTypes);

    var finalLen = (StdI64)EmitLoad(block, interpTotalLenVar, varTypes);
    EmitStructFieldStore(block, finalLen, interpManagedName, ManagedFieldLength, MlirType.I64, varTypes);
    EmitStructFieldStore(block, finalLen, interpManagedName, ManagedFieldCapacity, MlirType.I64, varTypes);

    var elemSizeConst2 = new StdConstI64Op(1);
    block.AddOp(elemSizeConst2);
    EmitStructFieldStore(block, elemSizeConst2.Result, interpManagedName, ManagedFieldElementSize, MlirType.I64, varTypes);

    // Heap-allocate String struct (16 bytes: _managed + _iterPos)
    var interpOuterPtr = EmitAlloc(block, 16);
    EmitStore(block, interpOuterPtr, tempName2, varTypes);

    // Store _managed heap pointer at offset 0
    var interpManagedReload = EmitLoad(block, interpManagedName, varTypes);
    EmitStructFieldStore(block, interpManagedReload, tempName2, 0, MlirType.I64, varTypes);

    // Store _iterPos at offset 8
    var iterPosConst = new StdConstI64Op(0);
    block.AddOp(iterPosConst);
    EmitStructFieldStore(block, iterPosConst.Result, tempName2, 8, MlirType.I64, varTypes);

    structVarNames[op.Result.Id] = tempName2;
  }

  /// <summary>
  /// Allocates a 21-byte buffer and calls maxon_i64_to_string runtime to convert
  /// an integer value to its decimal string representation. Returns (buffer, length).
  /// </summary>
  /// <summary>
  /// Allocates a buffer, calls a runtime conversion function, and returns (buffer, length).
  /// Used by EmitI64ToString, EmitF64ToString, and EmitBoolToString.
  /// </summary>
  private static (StdI64 Buffer, StdI64 Length) EmitRuntimeToString(
    StdValue value,
    string runtimeFuncName,
    int bufferSize,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes) {

    var bufResult = EmitAlloc(block, bufferSize);

    // Store buffer pointer so it survives the runtime call
    var bufVarName = $"__tostr_buf_{bufResult.Id}";
    EmitStore(block, bufResult, bufVarName, varTypes);

    var lenResult = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp(runtimeFuncName, [value, bufResult], lenResult));

    var finalBuf = (StdI64)EmitLoad(block, bufVarName, varTypes);
    return (finalBuf, lenResult);
  }

  private static (StdI64 Buffer, StdI64 Length) EmitI64ToString(
    StdI64 intValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
    EmitRuntimeToString(intValue, "maxon_i64_to_string", 21, block, varTypes);

  private static (StdI64 Buffer, StdI64 Length) EmitF64ToString(
    StdF64 floatValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
    EmitRuntimeToString(floatValue, "maxon_f64_to_string", 32, block, varTypes);

  /// <summary>
  /// Handles interpolation of struct values. For String/Character types (which have buffer/length
  /// fields), reads those directly. For Stringable types, calls the toString() method and uses
  /// the returned String's buffer/length.
  /// </summary>
  private static (StdI64 Buffer, StdI64 Length) EmitStructInterpolation(
    string managedVarName,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes) {

    // With heap refs, the String struct has _managed at offset 0.
    // Load the _managed heap pointer, then load buffer and length from it.
    // For types that are __ManagedMemory directly, managedVarName IS the managed struct.
    // Try loading _managed field first (outer struct), then fall back to direct (bare __ManagedMemory).
    var managedPtr = (StdI64)EmitStructFieldLoad(block, managedVarName, 0, MlirType.I64, varTypes);
    // Save the managed pointer so we can use it for both loads
    var managedTempVar = $"__interp_managed_ptr_{MlirContext.Current.NextId()}";
    EmitStore(block, managedPtr, managedTempVar, varTypes);
    var bufLoad = (StdI64)EmitStructFieldLoad(block, managedTempVar, ManagedFieldBuffer, MlirType.I64, varTypes);
    var lenLoad = (StdI64)EmitStructFieldLoad(block, managedTempVar, ManagedFieldLength, MlirType.I64, varTypes);
    return (bufLoad, lenLoad);
  }

  /// <summary>
  /// Converts an enum value to its string representation for interpolation.
  /// For int-backed and simple (ordinal) enums, converts the backing integer to a string.
  /// For float-backed enums, converts the backing float to a string.
  /// For string-backed enums, emits the string value from rdata.
  /// </summary>
  private static (StdI64 Buffer, StdI64 Length) EmitEnumToString(
    MaxonEnum enumValue,
    Dictionary<MaxonValue, StdValue> valueMap,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    MlirModule<StandardOp> result) {

    if (!result.TypeDefs.TryGetValue(enumValue.TypeName, out var typeDef) || typeDef is not MlirEnumType enumType) {
      throw new InvalidOperationException(
        $"String interpolation: enum type '{enumValue.TypeName}' not found in type definitions");
    }

    var backingMlirType = ResolveEnumBackingMlirType(enumType);
    var stdValue = valueMap[enumValue];

    if (enumType.BackingType is MlirStringBackingType or MlirCharBackingType) {
      // String/char-backed enum: the runtime value is the ordinal (i64).
      // We need to emit a lookup that maps ordinal → string rdata label.
      return EmitStringEnumToString(enumType, (StdI64)stdValue, block, result);
    }

    if (backingMlirType == MlirType.F64) {
      return EmitF64ToString((StdF64)stdValue, block, varTypes);
    }
    return EmitI64ToString((StdI64)stdValue, block, varTypes);
  }

  /// <summary>
  /// Emits code to convert a string-backed enum ordinal to its string representation.
  /// Generates a chain of select operations: for each case, compares ordinal and selects
  /// the matching string. Falls back to "?" for unknown ordinals.
  /// </summary>
  private static (StdI64 Buffer, StdI64 Length) EmitStringEnumToString(
    MlirEnumType enumType,
    StdI64 ordinalValue,
    MlirBlock<StandardOp> block,
    MlirModule<StandardOp> result) {

    // Initialize with a fallback "?" value
    var fallbackLabel = $"__strenum_fallback_{MlirContext.Current.NextId()}";
    var (currentBuf, currentLen) = EmitRdataLiteral("?", fallbackLabel, block, result);

    // For each case, compare ordinal and conditionally select the case's string
    foreach (var enumCase in enumType.Cases) {
      if (enumCase.RawValue is not string strValue) continue;

      var caseLabel = $"__strenum_case_{enumType.Name}_{enumCase.Name}_{MlirContext.Current.NextId()}";
      var (caseBuf, caseLen) = EmitRdataLiteral(strValue, caseLabel, block, result);

      var ordConst = new StdConstI64Op(enumCase.Ordinal);
      block.AddOp(ordConst);
      var cmpOp = new StdCmpI64Op("eq", ordinalValue, ordConst.Result);
      block.AddOp(cmpOp);

      // Select: if ordinal matches this case, use caseBuf/caseLen; otherwise keep current
      var selectBuf = new StdSelectI64Op(cmpOp.Result, caseBuf, currentBuf);
      block.AddOp(selectBuf);
      var selectLen = new StdSelectI64Op(cmpOp.Result, caseLen, currentLen);
      block.AddOp(selectLen);

      currentBuf = selectBuf.Result;
      currentLen = selectLen.Result;
    }

    return (currentBuf, currentLen);
  }

  /// Compares two strings (inputBuf/inputLen vs caseBuf/caseLen) using length check + memcmp.
  /// Returns a boolean StdBool that is true if the strings are equal.
  private static StdBool EmitStringEquals(
    StdI64 inputBuf, StdI64 inputLen, StdI64 caseBuf, StdI64 caseLen,
    MlirBlock<StandardOp> block) {
    var lenCmp = new StdCmpI64Op("eq", inputLen, caseLen);
    block.AddOp(lenCmp);
    var memcmpResult = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_memcmp", [inputBuf, caseBuf, caseLen], memcmpResult));
    var oneConst = new StdConstI64Op(1);
    block.AddOp(oneConst);
    var memEq = new StdCmpI64Op("eq", memcmpResult, oneConst.Result);
    block.AddOp(memEq);
    var bothMatch = new StdAndI1Op((StdBool)lenCmp.Result, (StdBool)memEq.Result);
    block.AddOp(bothMatch);
    return bothMatch.Result;
  }

  /// Builds a managed String or Character struct from a (buffer, length) pair.
  /// Heap-allocates both the outer struct and the __ManagedMemory inner struct.
  private static void EmitManagedStructFromBufLen(
    string tempName, StdI64 bufferPtr, StdI64 lengthVal,
    bool hasIterPos, MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes, Dictionary<int, string> structVarNames, int resultId) {
    // Heap-allocate __ManagedMemory (32 bytes)
    var managedName = $"{tempName}__managed";
    var managedPtr = EmitAlloc(block, 32);
    EmitStore(block, managedPtr, managedName, varTypes);
    EmitStructFieldStore(block, bufferPtr, managedName, ManagedFieldBuffer, MlirType.I64, varTypes);
    EmitStructFieldStore(block, lengthVal, managedName, ManagedFieldLength, MlirType.I64, varTypes);
    var capConst = new StdConstI64Op(0);
    block.AddOp(capConst);
    EmitStructFieldStore(block, capConst.Result, managedName, ManagedFieldCapacity, MlirType.I64, varTypes);
    var elemSizeConst = new StdConstI64Op(1);
    block.AddOp(elemSizeConst);
    EmitStructFieldStore(block, elemSizeConst.Result, managedName, ManagedFieldElementSize, MlirType.I64, varTypes);

    // Heap-allocate outer struct (String=16 bytes, Character=8 bytes)
    int outerSize = hasIterPos ? 16 : 8;
    var outerPtr = EmitAlloc(block, outerSize);
    EmitStore(block, outerPtr, tempName, varTypes);

    // Store _managed heap pointer at offset 0
    var managedPtrReload = EmitLoad(block, managedName, varTypes);
    EmitStructFieldStore(block, managedPtrReload, tempName, 0, MlirType.I64, varTypes);

    if (hasIterPos) {
      var iterConst = new StdConstI64Op(0);
      block.AddOp(iterConst);
      EmitStructFieldStore(block, iterConst.Result, tempName, 8, MlirType.I64, varTypes);
    }

    structVarNames[resultId] = tempName;
  }

  /// Converts an int-backed enum raw value to its ordinal via a select chain.
  private static StdI64 EmitIntEnumToOrdinal(
    MlirEnumType enumType, StdI64 rawValue, MlirBlock<StandardOp> block) {
    var fallbackOrd = new StdConstI64Op(0);
    block.AddOp(fallbackOrd);
    StdI64 currentOrd = fallbackOrd.Result;

    foreach (var enumCase in enumType.Cases) {
      var caseRawConst = new StdConstI64Op((long)enumCase.RawValue!);
      block.AddOp(caseRawConst);
      var cmpOp = new StdCmpI64Op("eq", rawValue, caseRawConst.Result);
      block.AddOp(cmpOp);
      var ordConst = new StdConstI64Op(enumCase.Ordinal);
      block.AddOp(ordConst);
      var selectOp = new StdSelectI64Op(cmpOp.Result, ordConst.Result, currentOrd);
      block.AddOp(selectOp);
      currentOrd = selectOp.Result;
    }
    return currentOrd;
  }

  /// Converts a float-backed enum raw value to its ordinal via a select chain.
  private static StdI64 EmitFloatEnumToOrdinal(
    MlirEnumType enumType, StdF64 rawValue, MlirBlock<StandardOp> block) {
    var fallbackOrd = new StdConstI64Op(0);
    block.AddOp(fallbackOrd);
    StdI64 currentOrd = fallbackOrd.Result;

    foreach (var enumCase in enumType.Cases) {
      var caseRawConst = new StdConstF64Op((double)enumCase.RawValue!);
      block.AddOp(caseRawConst);
      var cmpOp = new StdCmpF64Op("eq", rawValue, caseRawConst.Result);
      block.AddOp(cmpOp);
      var ordConst = new StdConstI64Op(enumCase.Ordinal);
      block.AddOp(ordConst);
      var selectOp = new StdSelectI64Op(cmpOp.Result, ordConst.Result, currentOrd);
      block.AddOp(selectOp);
      currentOrd = selectOp.Result;
    }
    return currentOrd;
  }

  /// Looks up an enum case name by ordinal via a select chain. Returns (buffer, length).
  private static (StdI64 Buffer, StdI64 Length) EmitEnumNameLookup(
    MlirEnumType enumType, StdI64 ordinalValue,
    MlirBlock<StandardOp> block, MlirModule<StandardOp> result) {
    var fallbackLabel = $"__enumname_fallback_{MlirContext.Current.NextId()}";
    var (currentBuf, currentLen) = EmitRdataLiteral("?", fallbackLabel, block, result);

    foreach (var enumCase in enumType.Cases) {
      var caseLabel = $"__enumname_{enumType.Name}_{enumCase.Name}_{MlirContext.Current.NextId()}";
      var (caseBuf, caseLen) = EmitRdataLiteral(enumCase.Name, caseLabel, block, result);

      var ordConst = new StdConstI64Op(enumCase.Ordinal);
      block.AddOp(ordConst);
      var cmpOp = new StdCmpI64Op("eq", ordinalValue, ordConst.Result);
      block.AddOp(cmpOp);

      var selectBuf = new StdSelectI64Op(cmpOp.Result, caseBuf, currentBuf);
      block.AddOp(selectBuf);
      var selectLen = new StdSelectI64Op(cmpOp.Result, caseLen, currentLen);
      block.AddOp(selectLen);

      currentBuf = selectBuf.Result;
      currentLen = selectLen.Result;
    }
    return (currentBuf, currentLen);
  }

  /// <summary>
  /// Allocates a 6-byte buffer and calls maxon_bool_to_string runtime to convert
  /// a boolean value to "true" or "false". Returns (buffer, length).
  /// </summary>
  private static (StdI64 Buffer, StdI64 Length) EmitBoolToString(
    StdBool boolValue, MlirBlock<StandardOp> block, Dictionary<string, string> varTypes) =>
    EmitRuntimeToString(boolValue, "maxon_bool_to_string", 6, block, varTypes);

  private static void LowerManagedMemConcat(
    MaxonManagedMemConcatOp op,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var lhsVarName = ResolveManagedVarName(op.Lhs, structVarNames);
    var rhsVarName = ResolveManagedVarName(op.Rhs, structVarNames);

    var lhsBuf = LoadManagedBuffer(block, lhsVarName, varTypes);
    var lhsLen = (StdI64)EmitStructFieldLoad(block, lhsVarName, ManagedFieldLength, MlirType.I64, varTypes);
    var rhsBuf = LoadManagedBuffer(block, rhsVarName, varTypes);
    var rhsLen = (StdI64)EmitStructFieldLoad(block, rhsVarName, ManagedFieldLength, MlirType.I64, varTypes);

    // element_size needed to convert element counts to byte counts
    var lhsElemSize = (StdI64)EmitStructFieldLoad(block, lhsVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

    // Compute byte sizes: elementCount * elementSize
    var lhsBytesOp = new StdMulI64Op(lhsLen, lhsElemSize);
    block.AddOp(lhsBytesOp);
    var rhsBytesOp = new StdMulI64Op(rhsLen, lhsElemSize);
    block.AddOp(rhsBytesOp);

    var totalBytesOp = new StdAddI64Op(lhsBytesOp.Result, rhsBytesOp.Result);
    block.AddOp(totalBytesOp);

    var oneOp = new StdConstI64Op(1);
    block.AddOp(oneOp);
    var allocSizeOp = new StdAddI64Op(totalBytesOp.Result, oneOp.Result);
    block.AddOp(allocSizeOp);

    var allocResult = EmitAlloc(block, allocSizeOp.Result);

    block.AddOp(new StdMemCopyOp(lhsBuf, allocResult, lhsBytesOp.Result));

    var dstAddr = new StdAddI64Op(allocResult, lhsBytesOp.Result);
    block.AddOp(dstAddr);
    block.AddOp(new StdMemCopyOp(rhsBuf, dstAddr.Result, rhsBytesOp.Result));

    // Store element counts (not byte counts) for length/capacity
    var totalLenOp = new StdAddI64Op(lhsLen, rhsLen);
    block.AddOp(totalLenOp);

    // Heap-allocate __ManagedMemory result (32 bytes)
    var tempName = $"__concat_{op.Result.Id}";
    var concatPtr = EmitAlloc(block, 32);
    EmitStore(block, concatPtr, tempName, varTypes);
    EmitStructFieldStore(block, allocResult, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
    EmitStructFieldStore(block, totalLenOp.Result, tempName, ManagedFieldLength, MlirType.I64, varTypes);
    EmitStructFieldStore(block, totalLenOp.Result, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
    EmitStructFieldStore(block, lhsElemSize, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
    structVarNames[op.Result.Id] = tempName;
  }

  private static void LowerManagedMemSlice(
    MaxonManagedMemSliceOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var srcVarName = ResolveManagedVarName(op.Managed, structVarNames);
    var srcBuffer = LoadManagedBuffer(block, srcVarName, varTypes);
    var srcElemSize = (StdI64)EmitStructFieldLoad(block, srcVarName, ManagedFieldElementSize, MlirType.I64, varTypes);

    var start = (StdI64)valueMap[op.Start];
    var end = (StdI64)valueMap[op.End];

    // Convert element index to byte offset: start * element_size
    var startBytesOp = new StdMulI64Op(start, srcElemSize);
    block.AddOp(startBytesOp);

    // Slice buffer points into the existing allocation at byte offset
    var sliceBufferOp = new StdAddI64Op(srcBuffer, startBytesOp.Result);
    block.AddOp(sliceBufferOp);

    // Slice length in elements is end - start
    var sliceLenOp = new StdSubI64Op(end, start);
    block.AddOp(sliceLenOp);

    // capacity=0 marks the slice as read-only so COW triggers on mutation
    var zeroOp = new StdConstI64Op(0);
    block.AddOp(zeroOp);

    // Heap-allocate __ManagedMemory result (32 bytes)
    var tempName = $"__slice_{op.Result.Id}";
    var slicePtr = EmitAlloc(block, 32);
    EmitStore(block, slicePtr, tempName, varTypes);
    EmitStructFieldStore(block, sliceBufferOp.Result, tempName, ManagedFieldBuffer, MlirType.I64, varTypes);
    EmitStructFieldStore(block, sliceLenOp.Result, tempName, ManagedFieldLength, MlirType.I64, varTypes);
    EmitStructFieldStore(block, zeroOp.Result, tempName, ManagedFieldCapacity, MlirType.I64, varTypes);
    EmitStructFieldStore(block, srcElemSize, tempName, ManagedFieldElementSize, MlirType.I64, varTypes);
    structVarNames[op.Result.Id] = tempName;
  }

  /// <summary>
  /// __make_char_from_bytes(managed, pos, len): create a Character from bytes in managed memory.
  /// Allocates a new buffer, copies len bytes from source at pos, and creates a Character struct.
  /// </summary>
  private static void LowerMakeCharFromBytes(
    MaxonMakeCharFromBytesOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    var srcVarName = ResolveManagedVarName(op.Managed, structVarNames);
    var srcBuffer = LoadManagedBuffer(block, srcVarName, varTypes);
    var pos = (StdI64)valueMap[op.Pos];
    var len = (StdI64)valueMap[op.Len];

    // Compute source address: srcBuffer + pos
    var srcAddrOp = new StdAddI64Op(srcBuffer, pos);
    block.AddOp(srcAddrOp);

    // Store len, srcAddr, and dstBuf to stack vars so they survive calls and memcopy
    var lenVar = $"__mkchar_len_{op.Result.Id}";
    EmitStore(block, len, lenVar, varTypes);
    var srcAddrVar = $"__mkchar_src_{op.Result.Id}";
    EmitStore(block, srcAddrOp.Result, srcAddrVar, varTypes);

    // Allocate new buffer
    var newBuf = EmitAlloc(block, len);

    // Store the new buffer pointer (alloc clobbers registers)
    var dstBufVar = $"__mkchar_dst_{op.Result.Id}";
    EmitStore(block, newBuf, dstBufVar, varTypes);

    // Reload values for memcopy (alloc clobbers registers)
    var reloadLen = (StdI64)EmitLoad(block, lenVar, varTypes);
    var reloadSrc = (StdI64)EmitLoad(block, srcAddrVar, varTypes);
    var reloadDst = (StdI64)EmitLoad(block, dstBufVar, varTypes);

    // Copy bytes from source to new buffer
    block.AddOp(new StdMemCopyOp(reloadSrc, reloadDst, reloadLen));

    // Reload all values again after memcopy (rep movsb clobbers RSI/RDI/RCX)
    var finalLen = (StdI64)EmitLoad(block, lenVar, varTypes);
    var finalBuf = (StdI64)EmitLoad(block, dstBufVar, varTypes);

    // Create Character struct with heap-allocated __ManagedMemory
    var charVarName = $"__char_{op.Result.Id}";

    // Heap-allocate __ManagedMemory (32 bytes)
    var charManagedName = $"__char_managed_{op.Result.Id}";
    var charManagedPtr = EmitAlloc(block, 32);
    EmitStore(block, charManagedPtr, charManagedName, varTypes);
    EmitStructFieldStore(block, finalBuf, charManagedName, ManagedFieldBuffer, MlirType.I64, varTypes);
    EmitStructFieldStore(block, finalLen, charManagedName, ManagedFieldLength, MlirType.I64, varTypes);
    EmitStructFieldStore(block, finalLen, charManagedName, ManagedFieldCapacity, MlirType.I64, varTypes);
    var elemSizeConst = new StdConstI64Op(1);
    block.AddOp(elemSizeConst);
    EmitStructFieldStore(block, elemSizeConst.Result, charManagedName, ManagedFieldElementSize, MlirType.I64, varTypes);

    // Heap-allocate Character struct (8 bytes: _managed only)
    var charOuterPtr = EmitAlloc(block, 8);
    EmitStore(block, charOuterPtr, charVarName, varTypes);
    // Store _managed heap pointer at offset 0
    var charManagedReload = EmitLoad(block, charManagedName, varTypes);
    EmitStructFieldStore(block, charManagedReload, charVarName, 0, MlirType.I64, varTypes);
    structVarNames[op.Result.Id] = charVarName;
  }

  private static readonly Dictionary<(MaxonBinOperator, MaxonValueKind), Func<StdValue, StdValue, (StandardOp Op, StdValue Result)>> BinOpFactories = new() {
    { (MaxonBinOperator.Add, MaxonValueKind.Integer), (l, r) => { var op = new StdAddI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Sub, MaxonValueKind.Integer), (l, r) => { var op = new StdSubI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Mul, MaxonValueKind.Integer), (l, r) => { var op = new StdMulI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Div, MaxonValueKind.Integer), (l, r) => { var op = new StdDivI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Mod, MaxonValueKind.Integer), (l, r) => { var op = new StdRemI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Add, MaxonValueKind.Float), (l, r) => { var op = new StdAddF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Sub, MaxonValueKind.Float), (l, r) => { var op = new StdSubF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Mul, MaxonValueKind.Float), (l, r) => { var op = new StdMulF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Div, MaxonValueKind.Float), (l, r) => { var op = new StdDivF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Eq, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("eq", (StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Ne, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("ne", (StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Lt, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("lt", (StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Gt, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("gt", (StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Le, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("le", (StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Ge, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("ge", (StdF64)l, (StdF64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Eq, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("eq", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Ne, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("ne", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Lt, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("lt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Gt, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("gt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Le, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("le", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Ge, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("ge", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    // Bitwise operations (integer only)
    { (MaxonBinOperator.BitAnd, MaxonValueKind.Integer), (l, r) => { var op = new StdAndI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.BitOr, MaxonValueKind.Integer), (l, r) => { var op = new StdOrI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.BitXor, MaxonValueKind.Integer), (l, r) => { var op = new StdXorI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Shl, MaxonValueKind.Integer), (l, r) => { var op = new StdShlI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Shr, MaxonValueKind.Integer), (l, r) => { var op = new StdShrI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    // Byte operations (bytes are represented as I64 at standard level)
    { (MaxonBinOperator.Eq, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("eq", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Ne, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("ne", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Lt, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("lt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Gt, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("gt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Le, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("le", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Ge, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("ge", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Add, MaxonValueKind.Byte), (l, r) => { var op = new StdAddI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    { (MaxonBinOperator.Sub, MaxonValueKind.Byte), (l, r) => { var op = new StdSubI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    // Logical operations (bool)
    { (MaxonBinOperator.And, MaxonValueKind.Bool), (l, r) => { var op = new StdAndI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
    { (MaxonBinOperator.Or, MaxonValueKind.Bool), (l, r) => { var op = new StdOrI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
    { (MaxonBinOperator.BitXor, MaxonValueKind.Bool), (l, r) => { var op = new StdXorI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
    { (MaxonBinOperator.Eq, MaxonValueKind.Bool), (l, r) => { var op = new StdCmpI1Op("eq", (StdBool)l, (StdBool)r); return (op, op.Result); } },
    { (MaxonBinOperator.Ne, MaxonValueKind.Bool), (l, r) => { var op = new StdCmpI1Op("ne", (StdBool)l, (StdBool)r); return (op, op.Result); } },
  };

  // ============================================================================
  // Algebraic identity optimization
  // ============================================================================

  /// <summary>
  /// Attempts to simplify a binary operation when one or both operands are known constants.
  /// Returns true if the identity was applied, with the result value set accordingly.
  /// When a new constant must be emitted (e.g. x*0=0), it is added to the block.
  /// </summary>
  private static bool TryAlgebraicIdentity(
      MaxonBinOp binOp,
      Dictionary<MaxonValue, MaxonLiteralOp> literalMap,
      Dictionary<MaxonValue, StdValue> valueMap,
      MlirBlock<StandardOp> block,
      out StdValue result) {

    literalMap.TryGetValue(binOp.Lhs, out var lhsLit);
    literalMap.TryGetValue(binOp.Rhs, out var rhsLit);

    // No constants — nothing to optimize
    if (lhsLit == null && rhsLit == null) {
      result = null!;
      return false;
    }

    var lhsStd = valueMap[binOp.Lhs];
    var rhsStd = valueMap[binOp.Rhs];

    // Integer / Byte identities
    if (binOp.OperandKind is MaxonValueKind.Integer or MaxonValueKind.Byte) {
      long? lVal = lhsLit?.IntValue;
      long? rVal = rhsLit?.IntValue;

      switch (binOp.Operator) {
        case MaxonBinOperator.Add:
          if (rVal == 0) { result = lhsStd; return true; }
          if (lVal == 0) { result = rhsStd; return true; }
          break;
        case MaxonBinOperator.Sub:
          if (rVal == 0) { result = lhsStd; return true; }
          break;
        case MaxonBinOperator.Mul:
          if (rVal == 1) { result = lhsStd; return true; }
          if (lVal == 1) { result = rhsStd; return true; }
          if (rVal == 0) { result = EmitConstI64(0, block); return true; }
          if (lVal == 0) { result = EmitConstI64(0, block); return true; }
          break;
        case MaxonBinOperator.Div:
          if (rVal == 1) { result = lhsStd; return true; }
          break;
        case MaxonBinOperator.Mod:
          if (rVal == 1) { result = EmitConstI64(0, block); return true; }
          break;
        case MaxonBinOperator.BitAnd:
          if (rVal == 0) { result = EmitConstI64(0, block); return true; }
          if (lVal == 0) { result = EmitConstI64(0, block); return true; }
          break;
        case MaxonBinOperator.BitOr:
          if (rVal == 0) { result = lhsStd; return true; }
          if (lVal == 0) { result = rhsStd; return true; }
          break;
        case MaxonBinOperator.BitXor:
          if (rVal == 0) { result = lhsStd; return true; }
          if (lVal == 0) { result = rhsStd; return true; }
          break;
        case MaxonBinOperator.Shl:
        case MaxonBinOperator.Shr:
          if (rVal == 0) { result = lhsStd; return true; }
          break;
      }
    }

    // Float identities (safe subset — avoids signed-zero and NaN edge cases)
    if (binOp.OperandKind == MaxonValueKind.Float) {
      double? lVal = lhsLit?.FloatValue;
      double? rVal = rhsLit?.FloatValue;

      switch (binOp.Operator) {
        case MaxonBinOperator.Mul:
          if (rVal == 1.0) { result = lhsStd; return true; }
          if (lVal == 1.0) { result = rhsStd; return true; }
          break;
        case MaxonBinOperator.Div:
          if (rVal == 1.0) { result = lhsStd; return true; }
          break;
      }
    }

    // Bool identities
    if (binOp.OperandKind == MaxonValueKind.Bool) {
      bool? lVal = lhsLit?.BoolValue;
      bool? rVal = rhsLit?.BoolValue;

      switch (binOp.Operator) {
        case MaxonBinOperator.And:
          if (rVal == true) { result = lhsStd; return true; }
          if (lVal == true) { result = rhsStd; return true; }
          if (rVal == false) { result = EmitConstI1(false, block); return true; }
          if (lVal == false) { result = EmitConstI1(false, block); return true; }
          break;
        case MaxonBinOperator.Or:
          if (rVal == false) { result = lhsStd; return true; }
          if (lVal == false) { result = rhsStd; return true; }
          if (rVal == true) { result = EmitConstI1(true, block); return true; }
          if (lVal == true) { result = EmitConstI1(true, block); return true; }
          break;
      }
    }

    result = null!;
    return false;
  }

  private static StdI64 EmitConstI64(long value, MlirBlock<StandardOp> block) {
    var op = new StdConstI64Op(value);
    block.AddOp(op);
    return op.Result;
  }

  private static StdBool EmitConstI1(bool value, MlirBlock<StandardOp> block) {
    var op = new StdConstI1Op(value);
    block.AddOp(op);
    return op.Result;
  }

  // ============================================================================
  // Function pointer operations
  // ============================================================================

  private static void LowerFunctionRef(
      MaxonFunctionRefOp fnRefOp,
      MlirBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap) {
    var refOp = new StdFuncRefOp(fnRefOp.FunctionName);
    block.AddOp(refOp);
    valueMap[fnRefOp.Result] = refOp.Result;
  }

  private static void LowerFunctionParam(
      MaxonFunctionParamOp fnParamOp,
      MlirBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap,
      Dictionary<string, string> varTypes) {
    int flatIdx = fnParamOp.Index;
    var paramOp = new StdParamOp(flatIdx, fnParamOp.Name, new StdPtr(MlirContext.Current.NextId()));
    block.AddOp(paramOp);
    valueMap[fnParamOp.Result] = paramOp.Result;
    // Store function pointer to variable so it can be loaded later via StdLoadI64Op
    block.AddOp(new StdStorePtrOp((StdPtr)paramOp.Result, fnParamOp.Name));
    varTypes[fnParamOp.Name] = "ptr";
  }

  private static void LowerFunctionVarRef(
      MaxonFunctionVarRefOp fnVarRefOp,
      MlirBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap) {
    // Function pointers are stored as 8-byte integers (pointers)
    var loadOp = new StdLoadI64Op(fnVarRefOp.VarName);
    block.AddOp(loadOp);
    // Wrap as StdPtr for consistency
    valueMap[fnVarRefOp.Result] = loadOp.Result;
  }

  private static void LowerIndirectCall(
      MaxonIndirectCallOp indirectCallOp,
      MlirBlock<StandardOp> block,
      Dictionary<MaxonValue, StdValue> valueMap,
      Dictionary<string, string> varTypes,
      Dictionary<int, string> structVarNames,
      Dictionary<string, MlirType> typeDefs) {
    var calleeValue = valueMap[indirectCallOp.Callee];
    var newArgs = new List<StdValue>();

    for (int i = 0; i < indirectCallOp.Args.Count; i++) {
      var arg = indirectCallOp.Args[i];
      if (structVarNames.TryGetValue(arg.Id, out var structName)) {
        // Struct args: pass heap pointer directly
        var heapPtr = EmitLoad(block, structName, varTypes);
        newArgs.Add(heapPtr);
      } else {
        newArgs.Add(valueMap[arg]);
      }
    }

    StdValue? resultValue = null;
    string? sretVarName = null;
    if (indirectCallOp.ResultKind == MaxonValueKind.Struct && indirectCallOp.ResultStructTypeName != null
        && typeDefs.TryGetValue(indirectCallOp.ResultStructTypeName, out var retTypeDef) && retTypeDef is MlirStructType) {
      // Struct return: result is a heap pointer (i64)
      resultValue = new StdI64(MlirContext.Current.NextId());
      sretVarName = $"__icallret_{MlirContext.Current.NextId()}";
    } else if (indirectCallOp.ResultKind != null) {
      resultValue = indirectCallOp.ResultKind switch {
        MaxonValueKind.Integer => new StdI64(MlirContext.Current.NextId()),
        MaxonValueKind.Float => new StdF64(MlirContext.Current.NextId()),
        MaxonValueKind.Bool => new StdBool(MlirContext.Current.NextId()),
        MaxonValueKind.Byte => new StdI64(MlirContext.Current.NextId()),
        MaxonValueKind.Enum => new StdI64(MlirContext.Current.NextId()),
        MaxonValueKind.Function => new StdPtr(MlirContext.Current.NextId()),
        MaxonValueKind.TypeParameter => new StdI64(MlirContext.Current.NextId()),
        _ => throw new InvalidOperationException($"Unsupported result kind for indirect call: {indirectCallOp.ResultKind}")
      };
    }

    var callOp = new StdIndirectCallOp(calleeValue, newArgs, resultValue);
    block.AddOp(callOp);

    if (sretVarName != null && indirectCallOp.Result != null && callOp.Result != null) {
      // Struct return: store heap pointer in named variable
      EmitStore(block, callOp.Result, sretVarName, varTypes);
      structVarNames[indirectCallOp.Result.Id] = sretVarName;
    } else if (indirectCallOp.Result != null && callOp.Result != null) {
      valueMap[indirectCallOp.Result] = callOp.Result;
    }
  }

  /// <summary>
  /// Maps MaxonValueKind to the MlirType used for managed memory element access.
  /// Struct, Enum, and Function kinds are stored as pointers (I64).
  /// </summary>
  private static MlirType GetManagedMemElementType(MaxonValueKind kind, string context) {
    return kind switch {
      MaxonValueKind.Integer => MlirType.I64,
      MaxonValueKind.Float => MlirType.F64,
      MaxonValueKind.Byte => MlirType.I8,
      MaxonValueKind.Bool => MlirType.I8,
      MaxonValueKind.Enum => MlirType.I64,
      MaxonValueKind.Struct => MlirType.I64, // struct references are pointers
      MaxonValueKind.Function => MlirType.I64, // function pointers
      MaxonValueKind.TypeParameter => MlirType.I64, // unresolved type parameter, stored as i64
      _ => throw new InvalidOperationException($"{context}: unsupported element kind '{kind}'")
    };
  }

  // ============================================================================
  // Allocation tracking helpers
  // ============================================================================

  /// <summary>
  /// Get or create an rdata label for a tracking tag string.
  /// </summary>
  private static string GetOrCreateTrackingTag(string tag) {
    if (_rdataTagCache!.TryGetValue(tag, out var label))
      return label;
    label = $"__track_tag_{_rdataTagCache.Count}";
    var bytes = System.Text.Encoding.UTF8.GetBytes(tag);
    _resultModule!.RdataEntries.Add((label, bytes, 1));
    _rdataTagCache[tag] = label;
    return label;
  }

  /// <summary>
  /// Emit ops to load a tracking tag string pointer and length.
  /// </summary>
  private static (StdI64 tagPtr, StdI64 tagLen) EmitTrackingTagLoad(
    MlirBlock<StandardOp> block, string tag) {
    var rdataLabel = GetOrCreateTrackingTag(tag);
    var leaOp = new StdLeaRdataOp(rdataLabel);
    block.AddOp(leaOp);
    var ptrOp = new StdPtrToI64Op(leaOp.Result);
    block.AddOp(ptrOp);
    var lenOp = new StdConstI64Op(System.Text.Encoding.UTF8.GetByteCount(tag));
    block.AddOp(lenOp);
    return (ptrOp.Result, lenOp.Result);
  }

  /// <summary>
  /// Emit tracking call: ALLOC #N: X bytes (tag)
  /// </summary>
  private static void EmitTrackAlloc(
    MlirBlock<StandardOp> block, StdI64 ptr, StdI64 size, string tag) {
    if (!_trackAllocs) return;
    var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
    block.AddOp(new StdCallRuntimeOp("maxon_track_alloc", [ptr, size, tagPtr, tagLen]));
  }

  /// <summary>
  /// Emit tracking call: INCREF: tag -> rc=N
  /// </summary>
  private static void EmitTrackIncref(
    MlirBlock<StandardOp> block, string tag, long refcount) {
    if (!_trackAllocs) return;
    var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
    var rcOp = new StdConstI64Op(refcount);
    block.AddOp(rcOp);
    block.AddOp(new StdCallRuntimeOp("maxon_track_incref", [tagPtr, tagLen, rcOp.Result]));
  }

  /// <summary>
  /// Emit tracking call: CLEANUP: tag
  /// </summary>
  private static void EmitTrackCleanup(
    MlirBlock<StandardOp> block, string tag) {
    if (!_trackAllocs) return;
    var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
    block.AddOp(new StdCallRuntimeOp("maxon_track_cleanup", [tagPtr, tagLen]));
  }

  /// <summary>
  /// Emit tracking call: MOVE: tag
  /// </summary>
  private static void EmitTrackMove(
    MlirBlock<StandardOp> block, string tag) {
    if (!_trackAllocs) return;
    var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
    block.AddOp(new StdCallRuntimeOp("maxon_track_move", [tagPtr, tagLen]));
  }

  /// <summary>
  /// Emit tracking call: COPY: tag
  /// </summary>
  private static void EmitTrackCopy(
    MlirBlock<StandardOp> block, string tag) {
    if (!_trackAllocs) return;
    var (tagPtr, tagLen) = EmitTrackingTagLoad(block, tag);
    block.AddOp(new StdCallRuntimeOp("maxon_track_copy", [tagPtr, tagLen]));
  }

  /// <summary>
  /// Returns the offsets of managed fields (__ManagedMemory-containing fields) within a struct type.
  /// Each entry is (byteOffset, fieldTypeName) where byteOffset is the offset of the __ManagedMemory
  /// within the struct element, and fieldTypeName is the name of the field's type (e.g. "String").
  /// Returns empty list if the struct has no managed fields.
  /// </summary>
  private static List<(int offset, string typeName)> GetManagedElementFieldInfo(
    MlirStructType elementType) {
    var result = new List<(int, string)>();
    foreach (var field in elementType.Fields) {
      if (field.Type is MlirStructType fieldStruct) {
        if (fieldStruct.Name == "__ManagedMemory") {
          result.Add((field.Offset, elementType.Name));
        } else {
          // Check nested struct for __ManagedMemory fields
          foreach (var nestedField in fieldStruct.Fields) {
            if (nestedField.Type is MlirStructType nestedType && nestedType.Name == "__ManagedMemory") {
              result.Add((field.Offset + nestedField.Offset, fieldStruct.Name));
              break;
            }
          }
        }
      }
    }
    return result;
  }

  /// <summary>
  /// Gets the element type for an array/collection struct type by looking up the "Element" type parameter.
  /// Only matches types with a field named "managed" (Array convention), not "_managed" (String/Character).
  /// Returns null if the struct is not an array-like collection or the element type is not a struct.
  /// </summary>
  private static MlirStructType? GetArrayElementStructType(
    string structTypeName, Dictionary<string, MlirType> typeDefs) {
    if (!typeDefs.TryGetValue(structTypeName, out var typeDef) || typeDef is not MlirStructType structType)
      return null;
    // Only apply to types with a field named "managed" (Array convention for storing elements)
    if (structType.GetField("managed") is not { Type: MlirStructType { Name: "__ManagedMemory" } })
      return null;
    if (!structType.TypeParams.TryGetValue("Element", out var elementType))
      return null;
    return elementType as MlirStructType;
  }

  /// <summary>
  /// Resolves the element type for an array field within a composite struct (e.g. Map).
  /// For inner aliases like KeyArray whose Element type is a generic parameter (Key),
  /// resolves through the parent struct's TypeParams (Key→String) to get the concrete element type.
  /// </summary>
  private static MlirStructType? ResolveArrayElementType(
    string arrayTypeName, MlirStructType parentStruct, Dictionary<string, MlirType> typeDefs) {
    // First try direct resolution (works for concrete array types)
    var direct = GetArrayElementStructType(arrayTypeName, typeDefs);
    if (direct != null) return direct;

    // For generic inner aliases, the Element type param is an unresolved MlirTypeParameterType.
    // Resolve it through the parent struct's TypeParams.
    if (!typeDefs.TryGetValue(arrayTypeName, out var arrayTypeDef) || arrayTypeDef is not MlirStructType arrayStruct)
      return null;
    if (arrayStruct.GetField("managed") is not { Type: MlirStructType { Name: "__ManagedMemory" } })
      return null;
    if (!arrayStruct.TypeParams.TryGetValue("Element", out var elementType))
      return null;

    // Element type is a generic parameter (e.g. Key) — resolve through parent's TypeParams
    if (elementType is MlirTypeParameterType typeParam) {
      if (parentStruct.TypeParams.TryGetValue(typeParam.ParameterName, out var resolved))
        return resolved as MlirStructType;
    }
    return null;
  }

  private static string? GetManagedFieldName(MlirStructType structType) {
    var managedField = structType.GetField("managed");
    if (managedField != null) return "managed";
    foreach (var field in structType.Fields)
      if (field.Type is MlirStructType nested && nested.Name == "__ManagedMemory")
        return field.Name;
    return null;
  }


  /// <summary>
  /// Finds ALL managed buffer paths within a struct type, recursing into nested structs.
  /// For MultiManaged with fields {numbers: IntArray, text: String, tag: String}, returns
  /// ["varName.numbers.managed.buffer", "varName.text._managed.buffer", "varName.tag._managed.buffer"].
  /// For single-managed structs like IntArray, returns ["varName.managed.buffer"].
  /// </summary>
  private static List<string> GetAllManagedBufferPaths(string varName, string structTypeName, Dictionary<string, MlirType> typeDefs) {
    var result = new List<string>();
    if (structTypeName == "__ManagedMemory") {
      result.Add($"{varName}.buffer");
      return result;
    }
    if (!typeDefs.TryGetValue(structTypeName, out var typeDef) || typeDef is not MlirStructType structType)
      return result;
    foreach (var field in structType.Fields) {
      if (field.Type is MlirStructType fieldStruct) {
        var nestedPaths = GetAllManagedBufferPaths($"{varName}.{field.Name}", fieldStruct.Name, typeDefs);
        result.AddRange(nestedPaths);
      }
    }
    return result;
  }

  /// <summary>
  /// For each buffer path within a struct, determines whether that path's containing array
  /// has elements with managed fields, and if so, records the element info keyed by buffer path.
  /// For a Map with String keys, "m.keys.managed.buffer" gets String's element info,
  /// while "m.values.managed.buffer" and "m.states.managed.buffer" get nothing.
  /// </summary>
  private static void PopulateManagedBufferElementInfo(
    List<string> bufferPaths, string dstName, string structTypeName,
    Dictionary<string, MlirType> typeDefs,
    Dictionary<string, List<(int offset, string typeName)>> managedBufferElementInfo) {
    // For simple array types, check the top-level struct directly
    var topLevelElement = GetArrayElementStructType(structTypeName, typeDefs);
    if (topLevelElement != null) {
      var elemFieldInfo = GetManagedElementFieldInfo(topLevelElement);
      if (elemFieldInfo.Count > 0) {
        foreach (var bufferPath in bufferPaths)
          managedBufferElementInfo[bufferPath] = elemFieldInfo;
      }
      return;
    }

    // For composite types (e.g. Map), resolve each buffer path's containing array type
    if (!typeDefs.TryGetValue(structTypeName, out var typeDef) || typeDef is not MlirStructType structType)
      return;

    foreach (var bufferPath in bufferPaths) {
      // Extract the field name from the buffer path
      // e.g. "m.keys.managed.buffer" → "keys"
      var suffix = bufferPath[(dstName.Length + 1)..];
      var dotIdx = suffix.IndexOf('.');
      if (dotIdx < 0) continue;
      var fieldName = suffix[..dotIdx];

      var field = structType.GetField(fieldName);
      if (field?.Type is not MlirStructType fieldStruct) continue;

      // Resolve element type: for generic inner aliases (e.g. KeyArray with Element=Key),
      // resolve through the parent struct's TypeParams (e.g. Map's Key→String)
      var elementType = ResolveArrayElementType(fieldStruct.Name, structType, typeDefs);
      if (elementType == null) continue;

      var elemFieldInfo = GetManagedElementFieldInfo(elementType);
      if (elemFieldInfo.Count > 0)
        managedBufferElementInfo[bufferPath] = elemFieldInfo;
    }
  }

  /// <summary>
  /// Emit the cleanup sequence for a managed variable before scope exit.
  /// In tracking mode: calls maxon_cleanup_managed which prints CLEANUP/DECREF/FREE and frees.
  /// In non-tracking mode: just calls maxon_free on the buffer.
  /// If elementFieldInfo is provided, iterates all elements and cleans up their managed fields
  /// between the CLEANUP tag and the DECREF/FREE.
  /// </summary>
  private static void EmitManagedCleanup(
    MlirBlock<StandardOp> block,
    string varName,
    string bufferVarPath,
    Dictionary<string, string> varTypes,
    Dictionary<string, MlirType> typeDefs,
    Dictionary<string, string> varNameToStructType,
    List<(int offset, string typeName)>? elementFieldInfo = null) {
    if (!_trackAllocs) return; // TODO: enable non-tracking cleanup once validated

    // With heap refs, bufferVarPath is the variable name holding the __ManagedMemory heap pointer
    // We need to extract the managed var name from the path. The path format is like
    // "varName.managed" or just "varName" for __ManagedMemory directly.
    // Actually, bufferVarPath is something like "arr.managed.buffer" — but with heap refs,
    // managed struct fields aren't accessed as named vars anymore. We need to navigate
    // through heap pointers. The managed var name is the struct that holds the managed field.
    // For now, we need to resolve the managed struct's heap pointer from the path.
    // bufferVarPath format: "varName.fieldPath...buffer" where the last segment before buffer
    // is the __ManagedMemory struct. With heap refs, we need to walk the pointer chain.

    // For single-managed structs like IntArray: path = "arr.managed.buffer"
    //   -> load arr's heap ptr, load managed field (another heap ptr), load buffer/capacity from it
    // For __ManagedMemory directly: path = "varName.buffer"
    //   -> load varName's heap ptr, load buffer/capacity from it
    // For multi-managed: path = "item.numbers.managed.buffer"
    //   -> load item, load numbers, load managed, load buffer/capacity

    // Extract the managed var name (everything before ".buffer")
    var managedBasePath = bufferVarPath[..bufferVarPath.LastIndexOf('.')];

    // Walk the path to get the __ManagedMemory heap pointer
    // managedBasePath could be "arr.managed", "varName", "item.numbers.managed", etc.
    // The base variable is the first segment, then each subsequent segment is a field access.
    var segments = managedBasePath.Split('.');
    var currentVar = segments[0]; // base variable, should be in varTypes as i64

    // Navigate through the struct fields to get to the __ManagedMemory heap pointer.
    // Resolve field offsets using type information.
    var currentTypeName = varNameToStructType.TryGetValue(segments[0], out var vst) ? vst : null;
    for (int si = 1; si < segments.Length; si++) {
      int fieldOffset = 0;
      if (currentTypeName != null && typeDefs.TryGetValue(currentTypeName, out var cType)
          && cType is MlirStructType cStruct) {
        var field = cStruct.GetField(segments[si]);
        if (field != null) {
          fieldOffset = field.Offset;
          currentTypeName = field.Type is MlirStructType fst ? fst.Name : null;
        }
      }
      var nextVar = $"__cleanup_nav_{MlirContext.Current.NextId()}";
      var nextPtr = EmitStructFieldLoad(block, currentVar, fieldOffset, MlirType.I64, varTypes);
      EmitStore(block, nextPtr, nextVar, varTypes);
      currentVar = nextVar;
    }

    // currentVar now holds the __ManagedMemory heap pointer
    var capacity = (StdI64)EmitStructFieldLoad(block, currentVar, ManagedFieldCapacity, MlirType.I64, varTypes);
    var buffer = (StdI64)EmitStructFieldLoad(block, currentVar, ManagedFieldBuffer, MlirType.I64, varTypes);

    if (elementFieldInfo != null && elementFieldInfo.Count > 0) {
      EmitTrackCleanup(block, varName);

      var length = (StdI64)EmitStructFieldLoad(block, currentVar, ManagedFieldLength, MlirType.I64, varTypes);
      var elemSize = (StdI64)EmitStructFieldLoad(block, currentVar, ManagedFieldElementSize, MlirType.I64, varTypes);

      // Build the managed field offsets array in rdata
      var offsetsLabel = $"__managed_offsets_{MlirContext.Current.NextId()}";
      var offsetBytes = new byte[elementFieldInfo.Count * 8];
      for (int i = 0; i < elementFieldInfo.Count; i++) {
        BitConverter.TryWriteBytes(offsetBytes.AsSpan(i * 8), (long)elementFieldInfo[i].offset);
      }
      _resultModule!.RdataEntries.Add((offsetsLabel, offsetBytes, 8));

      var offsetsLea = new StdLeaRdataOp(offsetsLabel);
      block.AddOp(offsetsLea);
      var offsetsPtr = new StdPtrToI64Op(offsetsLea.Result);
      block.AddOp(offsetsPtr);
      var numFields = new StdConstI64Op(elementFieldInfo.Count);
      block.AddOp(numFields);

      // Save values to stack before the call (runtime calls clobber registers)
      var bufSave = $"__cleanup_buf_{MlirContext.Current.NextId()}";
      EmitStore(block, buffer, bufSave, varTypes);
      var capSave = $"__cleanup_cap_{MlirContext.Current.NextId()}";
      EmitStore(block, capacity, capSave, varTypes);

      block.AddOp(new StdCallRuntimeOp("maxon_cleanup_array_elements",
        [buffer, length, elemSize, offsetsPtr.Result, numFields.Result]));

      // Reload buffer and capacity after the call, then just do the DECREF/FREE part
      buffer = (StdI64)EmitLoad(block, bufSave, varTypes);
      capacity = (StdI64)EmitLoad(block, capSave, varTypes);

      // Call maxon_cleanup_managed_nocleanup to do DECREF/FREE without the CLEANUP tag
      var (tagPtr, tagLen) = EmitTrackingTagLoad(block, varName);
      block.AddOp(new StdCallRuntimeOp("maxon_cleanup_managed_free", [capacity, buffer, tagPtr, tagLen]));
    } else {
      var (tagPtr, tagLen) = EmitTrackingTagLoad(block, varName);
      block.AddOp(new StdCallRuntimeOp("maxon_cleanup_managed", [capacity, buffer, tagPtr, tagLen]));
    }
  }

  /// <summary>
  /// Check if a method call name indicates mutation of the self struct.
  /// </summary>
  private static bool IsMutatingMethodCall(string callee) {
    // Method calls are like "IntArray.set", "StringArray.push", etc.
    var dot = callee.LastIndexOf('.');
    if (dot < 0) return false;
    var method = callee[(dot + 1)..];
    return method is "set" or "resize" or "push" or "shift" or "remove" or "pop" or "insert" or "concat";
  }
}
