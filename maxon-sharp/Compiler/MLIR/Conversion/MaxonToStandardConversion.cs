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
      var selfStructType = isStructInstanceMethod ? (MlirStructType)func.ParamTypes[0] : null;

      // Build the new function signature:
      // - Struct instance method 'self' param is passed as a pointer (by reference)
      // - Enum instance method 'self' param is passed as a scalar
      // - Other struct params are passed as pointers (caller allocates stack copy)
      // - Enum params are passed as scalars
      // - Struct return adds a hidden sret pointer as first param
      var newParamNames = new List<string>();
      var newParamTypes = new List<MlirType>();

      if (retStructType != null) {
        newParamNames.Add("__sret");
        newParamTypes.Add(MlirType.I64);
      }

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
        } else if (func.ParamTypes[i] is MlirEnumType enumParamType) {
          // Enum param: pass as scalar
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
        newReturnType = null;
      } else if (func.ReturnType is MlirEnumType retEnumType) {
        newReturnType = ResolveEnumBackingMlirType(retEnumType);
      } else if (func.ReturnType is not MlirStructType and not MlirEnumType) {
        newReturnType = func.ReturnType;
      } else {
        throw new InvalidOperationException($"Unhandled return type: {func.ReturnType.GetType().Name} in function '{func.Name}'");
      }
      var newFunc = new MlirFunction<StandardOp>(func.Name, newParamNames, newParamTypes, newReturnType, func.ThrowsType) { IsStdlib = func.IsStdlib };
      var valueMap = new Dictionary<MaxonValue, StdValue>();
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
      // Key: user variable name (e.g. "arr"), Value: path to buffer field (e.g. "arr.managed.buffer")
      var managedVarOwners = new Dictionary<string, string>();

      // Use pre-computed constant array literal metadata from ConstantArrayAnalysisPass
      // Key: struct literal result ID, Value: ConstantArrayLiteralInfo

      // Track cstring result IDs from .cstr() calls and their variable names
      var cstringResultIds = new HashSet<int>();
      var cstringTrackVars = new HashSet<string>();

      // Track processed blocks to detect loop back-edges
      var processedBlocks = new HashSet<string>();
      // Snapshot of managedVarOwners at each block's entry, keyed by block name
      var managedStateAtBlockEntry = new Dictionary<string, Dictionary<string, string>>();

      bool sretParamEmitted = false;
      foreach (var block in func.Body.Blocks) {
        processedBlocks.Add(block.Name);
        if (_trackAllocs)
          managedStateAtBlockEntry[block.Name] = new Dictionary<string, string>(managedVarOwners);
        var newBlock = newFunc.Body.AddBlock(block.Name);

        // In the entry block of sret functions, save the sret pointer (param 0)
        // to a named stack variable so it survives register clobbering.
        if (retStructType != null && !sretParamEmitted) {
          var sretParam = new StdI64(MlirContext.Current.NextId());
          newBlock.AddOp(new StdParamOp(0, "__sret", sretParam));
          EmitStore(newBlock, sretParam, "__sret", varTypes);
          sretParamEmitted = true;
        }

        foreach (var op in block.Operations) {
          switch (op) {
            case MaxonParamOp paramOp: {
              var stdResult = paramOp.ValueKind.CreateStdValue();
              int adjustedIndex = paramOp.Index + (retStructType != null ? 1 : 0);
              newBlock.AddOp(new StdParamOp(adjustedIndex, paramOp.Name, stdResult));
              valueMap[paramOp.Result] = stdResult;
              EmitStore(newBlock, stdResult, paramOp.Name, varTypes);
              break;
            }
            case MaxonStructParamOp structParamOp: {
              if (isStructInstanceMethod && structParamOp.Name == "self") {
                // Instance method self: receive as pointer, save it, load fields indirectly
                int selfFlatIdx = retStructType != null ? 1 : 0;
                var selfPtrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(selfFlatIdx, "__self_ptr", selfPtrVal));
                EmitStore(newBlock, selfPtrVal, "__self_ptr", varTypes);
                // Load each field from the self pointer into named variables
                LoadStructFieldsFromPointer(newBlock, selfPtrVal, "self", selfStructType!, varTypes);
                structVarNames[structParamOp.Result.Id] = "self";
                structValueTypes[structParamOp.Result.Id] = structParamOp.StructTypeName;
              } else {
                // Non-self struct param: receive as pointer, load fields from it
                int ptrFlatIdx = structParamPtrIndex[structParamOp.Index];
                var structType = (MlirStructType)func.ParamTypes[structParamOp.Index];
                var ptrVarName = $"__{structParamOp.Name}_ptr";
                var ptrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(ptrFlatIdx, ptrVarName, ptrVal));
                EmitStore(newBlock, ptrVal, ptrVarName, varTypes);
                LoadStructFieldsFromPointer(newBlock, ptrVal, structParamOp.Name, structType, varTypes);
                structVarNames[structParamOp.Result.Id] = structParamOp.Name;
                structValueTypes[structParamOp.Result.Id] = structParamOp.StructTypeName;
                // Mutating functions own their managed struct params (MOVE semantics)
                if (mutatingFunctions.Contains(func.Name)) {
                  var bufferPath = GetManagedBufferPath(structParamOp.Name, structParamOp.StructTypeName, module.TypeDefs);
                  if (bufferPath != null)
                    managedVarOwners[structParamOp.Name] = bufferPath;
                }
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
            case MaxonEnumParamOp enumParamOp: {
              int adjustedIndex = enumParamOp.Index + (retStructType != null ? 1 : 0);
              if (enumParamOp.BackingKind == MaxonValueKind.Float) {
                var stdResult = new StdF64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(adjustedIndex, enumParamOp.Name, stdResult));
                valueMap[enumParamOp.Result] = stdResult;
                EmitStore(newBlock, stdResult, enumParamOp.Name, varTypes);
              } else if (enumParamOp.BackingKind == MaxonValueKind.Integer) {
                var stdResult = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(adjustedIndex, enumParamOp.Name, stdResult));
                valueMap[enumParamOp.Result] = stdResult;
                EmitStore(newBlock, stdResult, enumParamOp.Name, varTypes);
              } else {
                throw new InvalidOperationException($"Unsupported enum backing kind: {enumParamOp.BackingKind}");
              }
              break;
            }
            case MaxonEnumVarRefOp enumVarRef: {
              var loaded = EmitLoad(newBlock, enumVarRef.VarName, varTypes);
              valueMap[enumVarRef.Result] = loaded;
              break;
            }
            case MaxonEnumRawValueOp rawValueOp: {
              // The enum's backing value IS the raw value - just pass through
              var enumStdVal = valueMap[rawValueOp.EnumValue];
              valueMap[rawValueOp.Result] = enumStdVal;
              break;
            }
            case MaxonLiteralOp litOp: {
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
              // Store each field value to named slots.
              var tempName = $"__struct_{structLitOp.Result.Id}";
              var structType = (MlirStructType)module.TypeDefs[structLitOp.TypeName];
              foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                var fieldVarName = $"{tempName}.{fieldName}";
                if (structVarNames.TryGetValue(fieldVal.Id, out var nestedStructName)) {
                  // Nested struct field: copy all sub-fields
                  var field = structType.GetField(fieldName)!;
                  var nestedType = (MlirStructType)field.Type;
                  CopyStructFields(newBlock, nestedStructName, fieldVarName, nestedType, varTypes);
                } else {
                  var mappedVal = valueMap[fieldVal];
                  EmitStore(newBlock, mappedVal, fieldVarName, varTypes);
                }
              }

              // For array/vector literals, patch the buffer field to point to element data
              if (structLitOp.ArrayLiteralTag != null) {
                // __ManagedMemory structs have buffer directly; outer structs (Array, Vector) nest it under .managed
                var bufferVarName = structLitOp.TypeName == "__ManagedMemory"
                  ? $"{tempName}.buffer"
                  : $"{tempName}.managed.buffer";

                if (module.ConstantArrayLiterals.TryGetValue(structLitOp.Result.Id, out var constArrayInfo)) {
                  // Constant array: store element data in .rdata with correct element size
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

                  // Point buffer to rdata; capacity stays 0 (read-only).
                  // Runtime COW check will copy to heap on first write.
                  newBlock.AddOp(new StdStoreI64Op(rdataPtrOp.Result, bufferVarName));
                } else {
                  // Non-constant array: LEA to get address of the element buffer on stack
                  var leaOp = new StdLeaOp(structLitOp.ArrayLiteralTag);
                  newBlock.AddOp(leaOp);
                  var castOp = new StdPtrToI64Op(leaOp.Result);
                  newBlock.AddOp(castOp);
                  newBlock.AddOp(new StdStoreI64Op(castOp.Result, bufferVarName));
                }
                varTypes[bufferVarName] = "i64";
              }

              structVarNames[structLitOp.Result.Id] = tempName;
              structValueTypes[structLitOp.Result.Id] = structLitOp.TypeName;
              break;
            }
            case MaxonAssignOp assignOp: {
              // Check structVarNames as fallback to detect struct values that flow
              // through ops not yet updated to track struct kinds.
              if (assignOp.ValueKind == MaxonValueKind.Struct
                  || structVarNames.ContainsKey(assignOp.Value.Id)) {
                // Struct assignment: copy all fields from source to destination
                var srcName = structVarNames[assignOp.Value.Id];
                var dstName = assignOp.VarName;
                // Get struct type name: prefer the MaxonStruct's TypeName, fall back to
                // structValueTypes for values that are semantically structs but typed as
                // integers (e.g. try_call results from monomorphized generic functions)
                var structTypeName = assignOp.Value is MaxonStruct ms
                  ? ms.TypeName
                  : structValueTypes[assignOp.Value.Id];
                var structType = (MlirStructType)module.TypeDefs[structTypeName];
                // Cleanup old managed memory before reassignment
                if (!isStructInstanceMethod && managedVarOwners.TryGetValue(assignOp.VarName, out var oldBufPath))
                  EmitManagedCleanup(newBlock, assignOp.VarName, oldBufPath, varTypes);
                CopyStructFields(newBlock, srcName, dstName, structType, varTypes);
                // In struct instance methods, assigning to a self field struct must also
                // update the self.X variables, write through __self_ptr, and use
                // "self.X" as the canonical name so later method calls read from
                // the self-prefixed variables (which always exist).
                varNameToStructType[assignOp.VarName] = structTypeName;
                if (IsSelfField(isStructInstanceMethod, selfStructType, assignOp.VarName)) {
                  var selfDstName = $"self.{assignOp.VarName}";
                  CopyStructFields(newBlock, dstName, selfDstName, structType, varTypes);
                  EmitStructWriteThrough(newBlock, selfDstName, assignOp.VarName, structType, selfStructType!, varTypes);
                  structVarNames[assignOp.Value.Id] = selfDstName;
                  structValueTypes[assignOp.Value.Id] = structTypeName;
                  varNameToStructPrefix[assignOp.VarName] = selfDstName;
                } else {
                  structVarNames[assignOp.Value.Id] = dstName;
                  structValueTypes[assignOp.Value.Id] = structTypeName;
                  varNameToStructPrefix[assignOp.VarName] = dstName;
                }
                // Track managed memory ownership for cleanup
                if (!isStructInstanceMethod && !assignOp.VarName.StartsWith("__")) {
                  var bufferPath = GetManagedBufferPath(dstName, structTypeName, module.TypeDefs);
                  if (bufferPath != null)
                    managedVarOwners[assignOp.VarName] = bufferPath;
                }
              } else {
                var mappedValue = valueMap[assignOp.Value];
                // In struct instance methods, self field assignments store to the self-prefixed
                // variable and write through __self_ptr. Using only "self.X" avoids divergence
                // between "X" and "self.X" when conditional blocks assign to "X".
                if (IsSelfField(isStructInstanceMethod, selfStructType, assignOp.VarName)) {
                  EmitStore(newBlock, mappedValue, $"self.{assignOp.VarName}", varTypes);
                  EmitSelfFieldWriteThrough(newBlock, mappedValue, assignOp.VarName, selfStructType!);
                } else {
                  EmitStore(newBlock, mappedValue, assignOp.VarName, varTypes);
                }
                if (_trackAllocs && cstringResultIds.Contains(assignOp.Value.Id))
                  cstringTrackVars.Add(assignOp.VarName);
              }
              break;
            }
            case MaxonVarRefOp varRef: {
              // In instance methods, cross-block references to self fields use bare names
              // but varTypes stores them with "self." prefix from LoadStructFieldsFromPointer
              var resolvedVarName = varRef.VarName;
              if (!varTypes.ContainsKey(resolvedVarName) && isStructInstanceMethod) {
                var selfPrefixed = $"self.{resolvedVarName}";
                if (varTypes.ContainsKey(selfPrefixed))
                  resolvedVarName = selfPrefixed;
              }
              // After monomorphization, a VarRefOp originally typed as Integer may
              // actually refer to a struct variable (e.g., for-in loop element from
              // Array<String>.next). If the variable is a struct prefix, handle it
              // as a struct reference.
              if (!varTypes.ContainsKey(resolvedVarName) && varNameToStructPrefix.TryGetValue(resolvedVarName, out string? structPrefix)) {
                structVarNames[varRef.Result.Id] = structPrefix;
                if (varNameToStructType.TryGetValue(resolvedVarName, out var stType)) {
                  structValueTypes[varRef.Result.Id] = stType;
                }
                break;
              }
              var loaded = EmitLoad(newBlock, resolvedVarName, varTypes);
              valueMap[varRef.Result] = loaded;
              break;
            }
            case MaxonStructVarRefOp structVarRef: {
              // Self fields always resolve to "self.X" — this takes priority over
              // varNameToStructPrefix which can be overwritten by other parameters
              // with same field names (e.g., both self and other have _managed)
              string resolvedName;
              if (IsSelfField(isStructInstanceMethod, selfStructType, structVarRef.VarName)) {
                resolvedName = $"self.{structVarRef.VarName}";
              } else {
                resolvedName = varNameToStructPrefix.GetValueOrDefault(structVarRef.VarName, structVarRef.VarName);
              }
              structVarNames[structVarRef.Result.Id] = resolvedName;
              structValueTypes[structVarRef.Result.Id] = structVarRef.StructTypeName;
              break;
            }
            case MaxonFieldAccessOp fieldAccess: {
              var structName = structVarNames[fieldAccess.StructValue.Id];
              var fieldVarName = $"{structName}.{fieldAccess.FieldName}";
              if (fieldAccess.ResultKind == MaxonValueKind.Struct) {
                // Struct-typed field: record the name mapping for nested field access
                structVarNames[fieldAccess.Result.Id] = fieldVarName;
                // Record for cross-block references (e.g. "managed" -> "self.managed")
                varNameToStructPrefix[fieldAccess.FieldName] = fieldVarName;
              } else {
                var loaded = EmitLoad(newBlock, fieldVarName, varTypes);
                valueMap[fieldAccess.Result] = loaded;
              }
              break;
            }
            case MaxonFieldAssignOp fieldAssign: {
              var structName = structVarNames[fieldAssign.StructValue.Id];
              var fieldVarName = $"{structName}.{fieldAssign.FieldName}";
              var mappedVal = valueMap[fieldAssign.NewValue];
              EmitStore(newBlock, mappedVal, fieldVarName, varTypes);
              // Write through self pointer for struct instance methods
              if (isStructInstanceMethod && selfStructType != null) {
                if (structName == "self") {
                  EmitSelfFieldWriteThrough(newBlock, mappedVal, fieldAssign.FieldName, selfStructType);
                } else if (structName.StartsWith("self.")) {
                  // Nested struct field (e.g. self.managed.length): compute total offset
                  EmitNestedSelfFieldWriteThrough(newBlock, mappedVal, structName, fieldAssign.FieldName, selfStructType);
                }
              }
              break;
            }
            case MaxonBinOp binOp: {
              var key = (binOp.Operator, binOp.OperandKind);
              if (!BinOpFactories.TryGetValue(key, out var factory))
                throw new InvalidOperationException($"Unsupported binop: {binOp.Operator} on {binOp.OperandKind}");

              var lhs = valueMap[binOp.Lhs];
              var rhs = valueMap[binOp.Rhs];
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
                foreach (var (varName, bufferPath) in managedVarOwners) {
                  if (!targetState.ContainsKey(varName))
                    loopScopedVars.Add(varName);
                }
                foreach (var varName in loopScopedVars) {
                  EmitManagedCleanup(newBlock, varName, managedVarOwners[varName], varTypes);
                  // Zero capacity to prevent use-after-free on reinitialization
                  var capacityPath = managedVarOwners[varName];
                  capacityPath = capacityPath[..capacityPath.LastIndexOf('.')] + ".capacity";
                  var zero = new StdConstI64Op(0);
                  newBlock.AddOp(zero);
                  newBlock.AddOp(new StdStoreI64Op(zero.Result, capacityPath));
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
            case MaxonCallOp callOp:
              if (TryLowerPrimitiveMethod(callOp, newBlock, valueMap)) break;
              LowerCall(callOp, funcLookup, newBlock, valueMap, varTypes, structVarNames, structValueTypes, isStructInstanceMethod, selfStructType, module.TypeDefs, managedVarOwners, mutatingFunctions);
              if (_trackAllocs && callOp.Callee.EndsWith(".cstr") && callOp.Result != null)
                cstringResultIds.Add(callOp.Result.Id);
              break;
            case MaxonFunctionRefOp fnRefOp:
              LowerFunctionRef(fnRefOp, newBlock, valueMap);
              break;
            case MaxonFunctionParamOp fnParamOp:
              LowerFunctionParam(fnParamOp, newBlock, valueMap, retStructType != null);
              break;
            case MaxonFunctionVarRefOp fnVarRefOp:
              LowerFunctionVarRef(fnVarRefOp, newBlock, valueMap);
              break;
            case MaxonIndirectCallOp indirectCallOp:
              LowerIndirectCall(indirectCallOp, newBlock, valueMap);
              break;
            case MaxonReturnOp retOp:
              LowerReturn(retOp, retStructType, newBlock, valueMap, varTypes, structVarNames, isStructInstanceMethod, selfStructType, managedVarOwners, cstringTrackVars);
              break;
            case MaxonThrowOp throwOp:
              LowerThrow(throwOp, newBlock, valueMap);
              break;
            case MaxonTryCallOp tryCallOp:
              LowerTryCall(tryCallOp, funcLookup, newBlock, valueMap, varTypes, structVarNames, structValueTypes, isStructInstanceMethod, selfStructType, module.TypeDefs);
              break;
            case MaxonManagedMemGetOp memGetOp:
              LowerManagedMemGet(memGetOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemSetOp memSetOp:
              LowerManagedMemSet(memSetOp, newBlock, valueMap, varTypes, structVarNames, isStructInstanceMethod, selfStructType);
              break;
            case MaxonManagedMemCreateOp memCreateOp:
              LowerManagedMemCreate(memCreateOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemGrowOp memGrowOp:
              LowerManagedMemGrow(memGrowOp, newBlock, valueMap, varTypes, structVarNames, isStructInstanceMethod, selfStructType);
              break;
            case MaxonManagedMemShiftOp memShiftOp:
              LowerManagedMemShift(memShiftOp, newBlock, valueMap, varTypes, structVarNames, isStructInstanceMethod, selfStructType);
              break;
            case MaxonManagedMemByteGetOp byteGetOp:
              LowerManagedMemByteGet(byteGetOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemByteSetOp byteSetOp:
              LowerManagedMemByteSet(byteSetOp, newBlock, valueMap, varTypes, structVarNames, isStructInstanceMethod, selfStructType);
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
              LowerStringInterp(interpOp, newBlock, valueMap, varTypes, structVarNames, structValueTypes, funcLookup, result);
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
              var stdArgs = callRtOp.Args.Select(a => (StdValue)(StdI64)valueMap[a]).ToList();
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
    if (enumType.BackingType is MlirStringBackingType) return MlirType.I64;
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
    bool isStructInstanceMethod,
    MlirStructType? selfStructType,
    Dictionary<string, MlirType> typeDefs,
    Dictionary<string, string> managedVarOwners,
    HashSet<string> mutatingFunctions) {
    LowerCallCore(callOp.Callee, callOp.Args, callOp.Result, callOp.ResultKind,
      isTryCall: false, funcLookup, block, valueMap, varTypes, structVarNames,
      structValueTypes, isStructInstanceMethod, selfStructType, typeDefs,
      managedVarOwners, mutatingFunctions);
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
    bool isStructInstanceMethod,
    MlirStructType? selfStructType,
    Dictionary<string, MlirType> typeDefs,
    Dictionary<string, string>? managedVarOwners,
    HashSet<string>? mutatingFunctions,
    MaxonValue? errorFlagValue = null) {

    var calleeFunc = ResolveCallee(callee, funcLookup);
    var calleeRetStructType = ResolveStructReturnType(calleeFunc.ReturnType, typeDefs);
    bool calleeIsStructInstance = IsStructInstanceMethod(calleeFunc);

    var newArgs = new List<StdValue>();
    var sretVarName = SetupSretArg(calleeRetStructType, result?.Id ?? 0, block, varTypes, newArgs);

    // Emit MOVE tracking before flattening args (ownership transfer)
    if (_trackAllocs && managedVarOwners != null && mutatingFunctions != null) {
      bool calleeMutates = mutatingFunctions.Contains(callee) || IsMutatingMethodCall(callee);
      if (calleeMutates) {
        for (int i = 0; i < args.Count; i++) {
          bool isSelfArg = calleeIsStructInstance && i == 0;
          if (!isSelfArg && structVarNames.TryGetValue(args[i].Id, out var argVarName)
              && managedVarOwners.ContainsKey(argVarName)) {
            EmitTrackMove(block, argVarName);
            managedVarOwners.Remove(argVarName);
          }
        }
        // For struct instance method calls where non-self params are managed structs,
        // emit MOVE for the self arg's managed field (the container accepting ownership)
        bool hasManagedStructParam = false;
        if (calleeIsStructInstance) {
          for (int i = 1; i < calleeFunc.ParamTypes.Count; i++) {
            if (calleeFunc.ParamTypes[i] is MlirStructType paramSt
                && GetManagedFieldName(paramSt) != null) {
              hasManagedStructParam = true;
              break;
            }
          }
        }
        if (calleeIsStructInstance && hasManagedStructParam) {
          var selfArg = args[0];
          if (structVarNames.TryGetValue(selfArg.Id, out _)) {
            var selfStructType2 = (MlirStructType)calleeFunc.ParamTypes[0];
            var managedFieldName = GetManagedFieldName(selfStructType2);
            if (managedFieldName != null) {
              EmitTrackMove(block, managedFieldName);
            }
          }
        }
      }
    }

    var selfBufName = FlattenCallArgs(args, calleeFunc, calleeIsStructInstance, block, valueMap, varTypes, structVarNames, newArgs, callee);

    // Emit call or try_call
    StdValue? callResult = calleeRetStructType != null
      ? null
      : ResolveCallResultType(resultKind, calleeFunc.ReturnType);
    if (isTryCall) {
      var tryCall = new StdTryCallOp(callee, newArgs, callResult);
      block.AddOp(tryCall);
      if (errorFlagValue != null) {
        valueMap[errorFlagValue] = tryCall.ErrorFlag;
        EmitStore(block, tryCall.ErrorFlag, "__error_flag", varTypes);
      }
    } else {
      block.AddOp(new StdCallOp(callee, newArgs, callResult));
    }

    // Map results
    if (result != null) {
      if (calleeRetStructType != null) {
        structVarNames[result.Id] = sretVarName!;
        structValueTypes[result.Id] = calleeRetStructType.Name;
      } else if (callResult != null) {
        valueMap[result] = callResult;
      }
    }

    var calleeSelfType = calleeIsStructInstance ? (MlirStructType)calleeFunc.ParamTypes[0] : null;
    ReadBackSelfFields(args, selfBufName, calleeSelfType, block, varTypes,
      structVarNames, isStructInstanceMethod, selfStructType);
  }

  private static void LowerReturn(
    MaxonReturnOp retOp,
    MlirStructType? retStructType,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    bool isStructInstanceMethod,
    MlirStructType? selfStructType,
    Dictionary<string, string> managedVarOwners,
    HashSet<string> cstringTrackVars) {

    // Error propagation: forward the error flag to the caller
    if (retOp.IsErrorPropagation) {
      var mappedErrFlag = valueMap[retOp.Value!];
      block.AddOp(new StdErrorReturnOp(mappedErrFlag));
      return;
    }

    // For instance methods, write self fields back to __self_ptr before returning
    if (isStructInstanceMethod && selfStructType != null) {
      var selfPtrLoad = new StdLoadI64Op("__self_ptr");
      block.AddOp(selfPtrLoad);
      WriteStructFieldsToPointer(block, "self", selfPtrLoad.Result, selfStructType, varTypes);
    }

    if (retStructType != null && retOp.Value != null) {
      var sretLoad = new StdLoadI64Op("__sret");
      block.AddOp(sretLoad);

      if (structVarNames.TryGetValue(retOp.Value.Id, out var srcName)) {
        // Normal path: struct is flattened on the stack, copy fields to sret buffer
        WriteStructFieldsToPointer(block, srcName, sretLoad.Result, retStructType, varTypes);
      } else {
        // Struct element returned from managed memory: the value is a pointer to
        // the struct's raw bytes in the heap buffer. Load fields via that pointer
        // and copy them into the sret buffer.
        var elemPtr = (StdI64)valueMap[retOp.Value];
        var tempName = $"__ret_elem_{retOp.Value.Id}";
        LoadStructFieldsFromPointer(block, elemPtr, tempName, retStructType, varTypes);
        WriteStructFieldsToPointer(block, tempName, sretLoad.Result, retStructType, varTypes);
      }

      EmitReturnCleanup(block, cstringTrackVars, managedVarOwners, varTypes);
      block.AddOp(new StdReturnOp());
    } else {
      StdValue? newRetVal = retOp.Value != null ? valueMap[retOp.Value] : null;
      bool hasCleanup = _trackAllocs && (managedVarOwners.Count > 0 || cstringTrackVars.Count > 0);
      if (newRetVal != null && hasCleanup) {
        // Save return value to stack before cleanup (cleanup calls clobber registers)
        var retVarName = $"__ret_save_{MlirContext.Current.NextId()}";
        EmitStore(block, newRetVal, retVarName, varTypes);

        EmitReturnCleanup(block, cstringTrackVars, managedVarOwners, varTypes);

        var retReload = EmitLoad(block, retVarName, varTypes);
        block.AddOp(new StdReturnOp(retReload));
      } else {
        EmitReturnCleanup(block, cstringTrackVars, managedVarOwners, varTypes);
        block.AddOp(new StdReturnOp(newRetVal));
      }
    }
  }

  private static void EmitReturnCleanup(
    MlirBlock<StandardOp> block,
    HashSet<string> cstringTrackVars,
    Dictionary<string, string> managedVarOwners,
    Dictionary<string, string> varTypes) {
    foreach (var csVar in cstringTrackVars)
      EmitTrackCleanup(block, csVar);
    foreach (var (varName, bufferPath) in managedVarOwners)
      EmitManagedCleanup(block, varName, bufferPath, varTypes);
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
    bool isStructInstanceMethod,
    MlirStructType? selfStructType,
    Dictionary<string, MlirType> typeDefs) {
    LowerCallCore(tryCallOp.Callee, tryCallOp.Args, tryCallOp.Result,
      tryCallOp.ResultKind, isTryCall: true, funcLookup, block, valueMap, varTypes,
      structVarNames, structValueTypes, isStructInstanceMethod, selfStructType, typeDefs,
      null, null, tryCallOp.ErrorFlag);
  }

  /// <summary>
  /// Lower call/try_call arguments for the standard calling convention.
  /// Struct args are passed as pointers to stack copies.
  /// Returns the self buffer name if a struct instance method self arg was allocated.
  /// </summary>
  private static string? FlattenCallArgs(
    List<MaxonValue> args,
    MlirFunction<MaxonOp> calleeFunc,
    bool calleeIsStructInstance,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    List<StdValue> newArgs,
    string calleeName) {
    bool calleeIsEnumInstance = IsEnumInstanceMethod(calleeFunc);
    string? selfBufName = null;

    for (int i = 0; i < args.Count; i++) {
      var arg = args[i];
      if (calleeIsEnumInstance && i == 0) {
        newArgs.Add(valueMap[arg]);
      } else if (calleeIsStructInstance && i == 0 && structVarNames.TryGetValue(arg.Id, out var selfName)) {
        var calleeSelfStructType = (MlirStructType)calleeFunc.ParamTypes[0];
        selfBufName = $"__selfbuf_{MlirContext.Current.NextId()}";
        var selfPtr = AllocateAndCopyStructToStack(block, selfName, selfBufName, calleeSelfStructType, varTypes);
        newArgs.Add(selfPtr);
      } else if (calleeFunc.ParamTypes[i] is MlirEnumType) {
        newArgs.Add(valueMap[arg]);
      } else if (calleeFunc.ParamTypes[i] is MlirStructType argStructType && structVarNames.TryGetValue(arg.Id, out var argStructName)) {
        // Allocate stack copy and pass pointer
        var bufName = $"__argbuf_{MlirContext.Current.NextId()}";
        var argPtr = AllocateAndCopyStructToStack(block, argStructName, bufName, argStructType, varTypes);
        newArgs.Add(argPtr);
      } else if (calleeFunc.ParamTypes[i] is not MlirStructType and not MlirEnumType) {
        newArgs.Add(valueMap[arg]);
      } else {
        throw new InvalidOperationException($"Unhandled call argument type: {calleeFunc.ParamTypes[i].GetType().Name} for arg {i} in call to '{calleeName}'");
      }
    }
    return selfBufName;
  }

  private static MlirFunction<MaxonOp> ResolveCallee(string calleeName, Dictionary<string, MlirFunction<MaxonOp>> funcLookup) {
    if (funcLookup.TryGetValue(calleeName, out var calleeFunc))
      return calleeFunc;
    var suffixPattern = $".{calleeName}";
    return funcLookup.Values.FirstOrDefault(f => f.Name.EndsWith(suffixPattern))
      ?? throw new InvalidOperationException($"Function '{calleeName}' not found in module");
  }

  private static string? SetupSretArg(MlirStructType? calleeRetStructType,
                                      int resultId,
                                      MlirBlock<StandardOp> block,
                                      Dictionary<string, string> varTypes,
                                      List<StdValue> newArgs) {
    if (calleeRetStructType == null) return null;
    var sretVarName = $"__sret_{resultId}";
    AllocateStructOnStack(block, sretVarName, calleeRetStructType, varTypes);
    var leaOp = new StdLeaOp(sretVarName);
    block.AddOp(leaOp);
    newArgs.Add(leaOp.Result);
    return sretVarName;
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

  /// <summary>
  /// After a struct instance method call, read back modified self fields from
  /// the self buffer and write through __self_ptr if needed.
  /// </summary>
  private static void ReadBackSelfFields(
    List<MaxonValue> args,
    string? selfBufName,
    MlirStructType? calleeSelfStructType,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    bool isStructInstanceMethod,
    MlirStructType? selfStructType) {
    // selfBufName is only set by FlattenCallArgs for struct instance methods
    if (selfBufName != null && calleeSelfStructType != null && args.Count > 0
        && structVarNames.TryGetValue(args[0].Id, out var callerSelfName)) {
      CopyStructFields(block, selfBufName, callerSelfName, calleeSelfStructType, varTypes);

      // If we're inside a struct instance method and the subcall was on self,
      // write all updated fields through __self_ptr so our caller sees the changes.
      if (isStructInstanceMethod && selfStructType != null && callerSelfName == "self") {
        var selfPtrLoad = new StdLoadI64Op("__self_ptr");
        block.AddOp(selfPtrLoad);
        WriteStructFieldsToPointer(block, "self", selfPtrLoad.Result, selfStructType, varTypes);
      }
    }
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

  private static void EmitZeroStore(MlirBlock<StandardOp> block, MlirType fieldType, string varName, Dictionary<string, string> varTypes) {
    if (fieldType == MlirType.F64) {
      var zeroOp = new StdConstF64Op(0.0);
      block.AddOp(zeroOp);
      EmitStore(block, zeroOp.Result, varName, varTypes);
    } else if (fieldType == MlirType.I1) {
      var zeroOp = new StdConstI1Op(false);
      block.AddOp(zeroOp);
      EmitStore(block, zeroOp.Result, varName, varTypes);
    } else if (fieldType == MlirType.I64 || fieldType == MlirType.I8 || fieldType is MlirEnumType) {
      var zeroOp = new StdConstI64Op(0);
      block.AddOp(zeroOp);
      EmitStore(block, zeroOp.Result, varName, varTypes);
    } else if (fieldType == MlirType.Fn) {
      var zeroOp = new StdConstI64Op(0);
      block.AddOp(zeroOp);
      var nullPtr = new StdPtr(zeroOp.Result.Id);
      EmitStore(block, nullPtr, varName, varTypes);
    } else if (fieldType == MlirType.I32) {
      throw new InvalidOperationException($"EmitZeroStore: I32 zero-store not yet implemented for '{varName}'");
    } else if (fieldType is MlirStructType) {
      throw new InvalidOperationException($"EmitZeroStore: struct zero-store not yet implemented for '{varName}' of type '{fieldType}'");
    } else {
      throw new InvalidOperationException($"EmitZeroStore: unsupported field type '{fieldType}' for '{varName}'");
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
  /// Copy all fields from one struct variable to another.
  /// </summary>
  private static void CopyStructFields(
    MlirBlock<StandardOp> block,
    string srcName, string dstName,
    MlirStructType structType,
    Dictionary<string, string> varTypes) {
    foreach (var field in structType.Fields) {
      var srcFieldVar = $"{srcName}.{field.Name}";
      var dstFieldVar = $"{dstName}.{field.Name}";
      if (field.Type is MlirStructType nestedStructType) {
        CopyStructFields(block, srcFieldVar, dstFieldVar, nestedStructType, varTypes);
      } else {
        var loaded = EmitLoad(block, srcFieldVar, varTypes);
        EmitStore(block, loaded, dstFieldVar, varTypes);
      }
    }
  }

  /// <summary>
  /// Allocate struct fields on stack in reverse order (for proper stack layout).
  /// Returns the base variable name for LEA.
  /// </summary>
  private static void AllocateStructOnStack(
    MlirBlock<StandardOp> block,
    string varName,
    MlirStructType structType,
    Dictionary<string, string> varTypes) {
    for (int fi = structType.Fields.Count - 1; fi >= 0; fi--) {
      var field = structType.Fields[fi];
      var fieldVarName = $"{varName}.{field.Name}";
      if (field.Type is MlirStructType nestedStructType) {
        AllocateStructOnStack(block, fieldVarName, nestedStructType, varTypes);
      } else {
        EmitZeroStore(block, field.Type, fieldVarName, varTypes);
      }
    }
  }

  /// <summary>
  /// Load all struct fields from a pointer into named variables.
  /// Handles nested struct fields by computing sub-pointers and recursing.
  /// </summary>
  private static void LoadStructFieldsFromPointer(
    MlirBlock<StandardOp> block,
    StdValue ptr,
    string varName,
    MlirStructType structType,
    Dictionary<string, string> varTypes) {
    foreach (var field in structType.Fields) {
      var fieldVarName = $"{varName}.{field.Name}";
      if (field.Type is MlirStructType nestedStructType) {
        // Compute pointer to nested struct: base + field offset
        var offsetOp = new StdConstI64Op(field.Offset);
        block.AddOp(offsetOp);
        var subPtrOp = new StdAddI64Op((StdI64)ptr, offsetOp.Result);
        block.AddOp(subPtrOp);
        LoadStructFieldsFromPointer(block, subPtrOp.Result, fieldVarName, nestedStructType, varTypes);
      } else {
        var loadOp = new StdLoadIndirectOp(ptr, field.Offset, field.Type);
        block.AddOp(loadOp);
        EmitStore(block, loadOp.Result, fieldVarName, varTypes);
      }
    }
  }

  /// <summary>
  /// Write all struct fields from named variables to a pointer.
  /// </summary>
  private static void WriteStructFieldsToPointer(
    MlirBlock<StandardOp> block,
    string srcName,
    StdValue ptr,
    MlirStructType structType,
    Dictionary<string, string> varTypes) {
    foreach (var field in structType.Fields) {
      var srcFieldVar = $"{srcName}.{field.Name}";
      if (field.Type is MlirStructType nestedStructType) {
        // Compute pointer to nested struct location within parent
        var offsetOp = new StdConstI64Op(field.Offset);
        block.AddOp(offsetOp);
        var subPtrOp = new StdAddI64Op((StdI64)ptr, offsetOp.Result);
        block.AddOp(subPtrOp);
        WriteStructFieldsToPointer(block, srcFieldVar, subPtrOp.Result, nestedStructType, varTypes);
      } else {
        var loaded = EmitLoad(block, srcFieldVar, varTypes);
        block.AddOp(new StdStoreIndirectOp(loaded, ptr, field.Offset, field.Type));
      }
    }
  }

  /// <summary>
  /// Allocate struct on stack, copy fields from source, and return LEA result.
  /// </summary>
  private static StdPtr AllocateAndCopyStructToStack(
    MlirBlock<StandardOp> block,
    string srcName,
    string bufName,
    MlirStructType structType,
    Dictionary<string, string> varTypes) {
    // Emit field stores in reverse order so the first field gets the
    // lowest stack address, then LEA points to the first field.
    for (int fi = structType.Fields.Count - 1; fi >= 0; fi--) {
      var field = structType.Fields[fi];
      var srcField = $"{srcName}.{field.Name}";
      var dstField = $"{bufName}.{field.Name}";
      if (field.Type is MlirStructType nestedStructType) {
        AllocateAndCopyStructToStackNested(block, srcField, dstField, nestedStructType, varTypes);
      } else {
        var loaded = EmitLoad(block, srcField, varTypes);
        EmitStore(block, loaded, dstField, varTypes);
      }
    }
    var leaOp = new StdLeaOp(bufName);
    block.AddOp(leaOp);
    return leaOp.Result;
  }

  private static void AllocateAndCopyStructToStackNested(
    MlirBlock<StandardOp> block,
    string srcName,
    string dstName,
    MlirStructType structType,
    Dictionary<string, string> varTypes) {
    for (int fi = structType.Fields.Count - 1; fi >= 0; fi--) {
      var field = structType.Fields[fi];
      var srcField = $"{srcName}.{field.Name}";
      var dstField = $"{dstName}.{field.Name}";
      if (field.Type is MlirStructType nestedStructType) {
        AllocateAndCopyStructToStackNested(block, srcField, dstField, nestedStructType, varTypes);
      } else {
        var loaded = EmitLoad(block, srcField, varTypes);
        EmitStore(block, loaded, dstField, varTypes);
      }
    }
  }

  private static void EmitSelfFieldWriteThrough(
    MlirBlock<StandardOp> block, StdValue value, string fieldName, MlirStructType selfStructType) {
    var field = selfStructType.GetField(fieldName);
    if (field != null) {
      var selfPtrLoad = new StdLoadI64Op("__self_ptr");
      block.AddOp(selfPtrLoad);
      block.AddOp(new StdStoreIndirectOp(value, selfPtrLoad.Result, field.Offset, field.Type));
    }
  }

  /// <summary>
  /// Write through self pointer for nested struct fields (e.g. self.managed.length).
  /// Walks the field path to compute the total offset from __self_ptr.
  /// </summary>
  private static void EmitNestedSelfFieldWriteThrough(
    MlirBlock<StandardOp> block, StdValue value, string structName, string leafFieldName,
    MlirStructType selfStructType) {
    // structName is e.g. "self.managed" — strip "self." prefix and walk the path
    var path = structName["self.".Length..];
    int totalOffset = 0;
    var currentType = selfStructType;
    foreach (var segment in path.Split('.')) {
      var field = currentType.GetField(segment);
      if (field == null) return;
      totalOffset += field.Offset;
      if (field.Type is MlirStructType nestedType)
        currentType = nestedType;
      else
        return; // path doesn't lead to a nested struct
    }
    // Now add the leaf field offset
    var leafField = currentType.GetField(leafFieldName);
    if (leafField == null) return;
    totalOffset += leafField.Offset;
    var selfPtrLoad = new StdLoadI64Op("__self_ptr");
    block.AddOp(selfPtrLoad);
    block.AddOp(new StdStoreIndirectOp(value, selfPtrLoad.Result, totalOffset, leafField.Type));
  }

  /// <summary>
  /// Write all leaf fields of a struct through the self pointer.
  /// Used when assigning a struct value to a self field (e.g. elements = newElements).
  /// </summary>
  private static void EmitStructWriteThrough(
    MlirBlock<StandardOp> block, string varPrefix, string selfFieldName,
    MlirStructType structType, MlirStructType selfStructType,
    Dictionary<string, string> varTypes) {
    var selfField = selfStructType.GetField(selfFieldName);
    if (selfField == null) return;
    int baseOffset = selfField.Offset;
    var selfPtrLoad = new StdLoadI64Op("__self_ptr");
    block.AddOp(selfPtrLoad);
    EmitStructWriteThroughRecursive(block, varPrefix, structType, baseOffset, selfPtrLoad.Result, varTypes);
  }

  private static void EmitStructWriteThroughRecursive(
    MlirBlock<StandardOp> block, string varPrefix,
    MlirStructType structType, int baseOffset, StdValue selfPtr,
    Dictionary<string, string> varTypes) {
    foreach (var field in structType.Fields) {
      var fieldVar = $"{varPrefix}.{field.Name}";
      if (field.Type is MlirStructType nestedType) {
        EmitStructWriteThroughRecursive(block, fieldVar, nestedType, baseOffset + field.Offset, selfPtr, varTypes);
      } else {
        var loaded = EmitLoad(block, fieldVar, varTypes);
        block.AddOp(new StdStoreIndirectOp(loaded, selfPtr, baseOffset + field.Offset, field.Type));
      }
    }
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
  /// Load the buffer pointer from a managed memory struct variable.
  /// </summary>
  private static StdI64 LoadManagedBuffer(
    MlirBlock<StandardOp> block,
    string managedVarName,
    Dictionary<string, string> varTypes) {
    return (StdI64)EmitLoad(block, $"{managedVarName}.buffer", varTypes);
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
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var elemSize = (StdI64)EmitLoad(block, $"{managedVarName}.element_size", varTypes);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var addr = ComputeElementAddress(block, buffer, index, elemSize);

    if (op.IsStructElement) {
      // Struct elements are stored inline in the buffer. Return the computed
      // address directly so downstream code (LoadStructFieldsFromPointer) can
      // read the struct fields from the buffer without an extra indirection.
      valueMap[op.Result] = addr;
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
    Dictionary<int, string> structVarNames,
    bool isStructInstanceMethod,
    MlirStructType? selfStructType) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var elemSize = (StdI64)EmitLoad(block, $"{managedVarName}.element_size", varTypes);
    EmitCowCheck(block, managedVarName, varTypes, isStructInstanceMethod, selfStructType, elemSize);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var value = valueMap[op.Value];
    var addr = ComputeElementAddress(block, buffer, index, elemSize);

    if (op.IsStructElement) {
      // Struct elements: copy the full struct data from the source pointer
      // into the buffer using memcpy. This ensures the data is stored inline
      // and doesn't become stale when the source stack frame is reused.
      var copyResult = new StdI64(MlirContext.Current.NextId());
      block.AddOp(new StdCallRuntimeOp("maxon_memcpy", [addr, (StdI64)value, elemSize], copyResult));
    } else {
      // Scalar elements: store directly
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
    // Store as a __ManagedMemory struct: buffer=ptr, length=count, capacity=count, element_size
    var tempName = $"__managed_create_{op.Result.Id}";
    EmitStore(block, allocResult, $"{tempName}.buffer", varTypes);
    EmitStore(block, count, $"{tempName}.length", varTypes);
    EmitStore(block, count, $"{tempName}.capacity", varTypes);
    EmitStore(block, sizeOp.Result, $"{tempName}.element_size", varTypes);
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
    Dictionary<int, string> structVarNames,
    bool isStructInstanceMethod,
    MlirStructType? selfStructType) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);

    // Load element_size from the managed struct
    var elemSize = (StdI64)EmitLoad(block, $"{managedVarName}.element_size", varTypes);

    EmitCowCheck(block, managedVarName, varTypes, isStructInstanceMethod, selfStructType, elemSize);
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
      // Save byte size and buffer pointer before tracking calls (which clobber registers)
      var byteSizeVar = $"__grow_bytesize_{MlirContext.Current.NextId()}";
      EmitStore(block, newByteSizeOp.Result, byteSizeVar, varTypes);
      var newBufVar = $"__grow_newbuf_{MlirContext.Current.NextId()}";
      EmitStore(block, newBufferResult, newBufVar, varTypes);

      var bufReload = (StdI64)EmitLoad(block, newBufVar, varTypes);
      var sizeReload = (StdI64)EmitLoad(block, byteSizeVar, varTypes);
      EmitTrackAlloc(block, bufReload, sizeReload, "array grow");
      EmitTrackIncref(block, "array grow", 1);

      // Reload new buffer from stack (tracking calls clobbered the register)
      newBufReload = (StdI64)EmitLoad(block, newBufVar, varTypes);
    } else {
      newBufReload = newBufferResult;
    }

    // Update managed struct fields
    EmitStore(block, newBufReload, $"{managedVarName}.buffer", varTypes);
    EmitStore(block, newCap, $"{managedVarName}.capacity", varTypes);

    // Write through to self pointer if in struct instance method
    if (isStructInstanceMethod && selfStructType != null && managedVarName.StartsWith("self.")) {
      EmitNestedSelfFieldWriteThrough(block, newBufReload, managedVarName, "buffer", selfStructType);
      EmitNestedSelfFieldWriteThrough(block, newCap, managedVarName, "capacity", selfStructType);
    }
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
    Dictionary<int, string> structVarNames,
    bool isStructInstanceMethod,
    MlirStructType? selfStructType) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var elemSize = (StdI64)EmitLoad(block, $"{managedVarName}.element_size", varTypes);
    EmitCowCheck(block, managedVarName, varTypes, isStructInstanceMethod, selfStructType, elemSize);
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
    bool isStructInstanceMethod,
    MlirStructType? selfStructType,
    StdI64 elemSize) {
    var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var capacity = (StdI64)EmitLoad(block, $"{managedVarName}.capacity", varTypes);
    var length = (StdI64)EmitLoad(block, $"{managedVarName}.length", varTypes);

    var uid = MlirContext.Current.NextId();
    var cowLenVar = $"__cow_len_{uid}";
    EmitStore(block, length, cowLenVar, varTypes);

    var newBuffer = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_cow_check", [oldBuffer, capacity, length, elemSize], newBuffer));

    EmitStore(block, newBuffer, $"{managedVarName}.buffer", varTypes);
    // If COW triggered (capacity was 0), new capacity = length; otherwise keep original
    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);
    var cmpOp = new StdCmpI64Op("eq", capacity, zeroConst.Result);
    block.AddOp(cmpOp);
    var lenReload = (StdI64)EmitLoad(block, cowLenVar, varTypes);
    var selectOp = new StdSelectI64Op(cmpOp.Result, lenReload, capacity);
    block.AddOp(selectOp);
    EmitStore(block, selectOp.Result, $"{managedVarName}.capacity", varTypes);

    if (isStructInstanceMethod && selfStructType != null && managedVarName.StartsWith("self.")) {
      EmitNestedSelfFieldWriteThrough(block, newBuffer, managedVarName, "buffer", selfStructType);
      EmitNestedSelfFieldWriteThrough(block, selectOp.Result, managedVarName, "capacity", selfStructType);
    }
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
    Dictionary<int, string> structVarNames,
    bool isStructInstanceMethod,
    MlirStructType? selfStructType) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var elemSize = (StdI64)EmitLoad(block, $"{managedVarName}.element_size", varTypes);
    EmitCowCheck(block, managedVarName, varTypes, isStructInstanceMethod, selfStructType, elemSize);

    // Now perform the actual byte write using the writable buffer
    var bufReload = (StdI64)EmitLoad(block, $"{managedVarName}.buffer", varTypes);
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

    // Set up __ManagedMemory struct fields
    var tempName = $"__from_cstring_{op.Result.Id}";
    var elemSizeOp = new StdConstI64Op(1);
    block.AddOp(elemSizeOp);

    EmitStore(block, allocResult, $"{tempName}.buffer", varTypes);
    EmitStore(block, lenResult, $"{tempName}.length", varTypes);
    EmitStore(block, lenResult, $"{tempName}.capacity", varTypes);
    EmitStore(block, elemSizeOp.Result, $"{tempName}.element_size", varTypes);
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
    var length = (StdI64)EmitLoad(block, $"{managedVarName}.length", varTypes);

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

    var tempName = $"__{tempPrefix}_{resultId}";
    var bufferVar = $"{tempName}._managed.buffer";
    var lengthVar = $"{tempName}._managed.length";
    var capacityVar = $"{tempName}._managed.capacity";
    var elemSizeVar = $"{tempName}._managed.element_size";

    EmitStore(block, bufferPtr, bufferVar, varTypes);
    EmitStore(block, lengthVal, lengthVar, varTypes);

    var capConst = new StdConstI64Op(0);
    block.AddOp(capConst);
    EmitStore(block, capConst.Result, capacityVar, varTypes);

    // Strings use byte-level elements, so element_size = 1
    var elemSizeConst = new StdConstI64Op(1);
    block.AddOp(elemSizeConst);
    EmitStore(block, elemSizeConst.Result, elemSizeVar, varTypes);

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

    var iterConst = new StdConstI64Op(0);
    block.AddOp(iterConst);
    EmitStore(block, iterConst.Result, $"{tempName}._iterPos", varTypes);
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
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirFunction<MaxonOp>> funcLookup,
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
          partInfos.Add(EmitStructInterpolation(exprValue, managedVarName, FormatSpec, block, varTypes,
            structVarNames, structValueTypes, funcLookup, result));
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

    // Create String struct - reload from stack variables
    var tempName2 = $"__interptmp_{op.Result.Id}";

    var finalBuf = (StdI64)EmitLoad(block, interpBufVar, varTypes);
    EmitStore(block, finalBuf, $"{tempName2}._managed.buffer", varTypes);

    var finalLen = (StdI64)EmitLoad(block, interpTotalLenVar, varTypes);
    EmitStore(block, finalLen, $"{tempName2}._managed.length", varTypes);
    EmitStore(block, finalLen, $"{tempName2}._managed.capacity", varTypes);

    // String interpolation creates byte-level managed memory
    var elemSizeConst2 = new StdConstI64Op(1);
    block.AddOp(elemSizeConst2);
    EmitStore(block, elemSizeConst2.Result, $"{tempName2}._managed.element_size", varTypes);

    var iterPosConst = new StdConstI64Op(0);
    block.AddOp(iterPosConst);
    EmitStore(block, iterPosConst.Result, $"{tempName2}._iterPos", varTypes);

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
    MaxonValue exprValue,
    string managedVarName,
    string? formatSpec,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<int, string> structValueTypes,
    Dictionary<string, MlirFunction<MaxonOp>> funcLookup,
    MlirModule<StandardOp> result) {

    // Check if this struct has managed memory buffer/length fields (String/Character)
    foreach (var prefix in new[] { $"{managedVarName}._managed", managedVarName }) {
      if (varTypes.ContainsKey($"{prefix}.buffer")) {
        var bufLoad = (StdI64)EmitLoad(block, $"{prefix}.buffer", varTypes);
        var lenLoad = (StdI64)EmitLoad(block, $"{prefix}.length", varTypes);
        return (bufLoad, lenLoad);
      }
    }

    // Not a String/Character — must be a Stringable type. Call toString("") on it.
    string? structTypeName = null;
    if (exprValue is MaxonStruct ms) {
      structTypeName = ms.TypeName;
    } else if (structValueTypes.TryGetValue(exprValue.Id, out var svt)) {
      structTypeName = svt;
    }
    if (structTypeName == null) {
      throw new InvalidOperationException(
        $"String interpolation: struct value %{exprValue.Id} has no type name for Stringable call");
    }

    return EmitStringableToStringCall(structTypeName, managedVarName, formatSpec, block,
      varTypes, structVarNames, funcLookup, result);
  }

  /// <summary>
  /// Calls toString(format) on a Stringable struct and returns the resulting String's buffer/length.
  /// </summary>
  private static (StdI64 Buffer, StdI64 Length) EmitStringableToStringCall(
    string structTypeName,
    string selfVarName,
    string? formatSpec,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    Dictionary<string, MlirFunction<MaxonOp>> funcLookup,
    MlirModule<StandardOp> result) {

    // Find the toString method for this type (try exact match, then suffix match)
    var toStringName = $"{structTypeName}.toString";
    if (!funcLookup.TryGetValue(toStringName, out var toStringFunc)) {
      var suffixPattern = $".{toStringName}";
      toStringFunc = funcLookup.Values.FirstOrDefault(f => f.Name.EndsWith(suffixPattern));
      if (toStringFunc != null) {
        toStringName = toStringFunc.Name;
      } else {
        throw new InvalidOperationException(
          $"String interpolation: type '{structTypeName}' does not have a toString method");
      }
    }

    var calleeRetStructType = ResolveStructReturnType(toStringFunc.ReturnType, result.TypeDefs);

    var newArgs = new List<StdValue>();

    // Allocate sret for the returned String
    var sretVarName = $"__interp_tostr_sret_{MlirContext.Current.NextId()}";
    AllocateStructOnStack(block, sretVarName, calleeRetStructType!, varTypes);
    var sretLea = new StdLeaOp(sretVarName);
    block.AddOp(sretLea);
    newArgs.Add(sretLea.Result);

    // Pass self pointer: allocate a copy of the struct on stack
    var selfStructType = (MlirStructType)result.TypeDefs[structTypeName];
    var selfBufName = $"__interp_selfbuf_{MlirContext.Current.NextId()}";
    var selfPtr = AllocateAndCopyStructToStack(block, selfVarName, selfBufName, selfStructType, varTypes);
    newArgs.Add(selfPtr);

    // Pass format string argument (empty string for default, or format spec)
    var formatValue = formatSpec ?? "";
    var formatLitName = $"__interp_fmt_{MlirContext.Current.NextId()}";
    var formatTempName = EmitManagedMemoryLiteral(formatValue, MlirContext.Current.NextId(),
      "fmt", formatLitName, block, varTypes, structVarNames, result);

    // String type has _iterPos field beyond __ManagedMemory fields
    var iterConst = new StdConstI64Op(0);
    block.AddOp(iterConst);
    EmitStore(block, iterConst.Result, $"{formatTempName}._iterPos", varTypes);

    // The format arg is a String struct passed by pointer
    var formatStructType = calleeRetStructType!; // String type
    var formatBufName = $"__interp_fmtbuf_{MlirContext.Current.NextId()}";
    var formatPtr = AllocateAndCopyStructToStack(block, formatTempName, formatBufName, formatStructType, varTypes);
    newArgs.Add(formatPtr);

    // Call toString
    block.AddOp(new StdCallOp(toStringName, newArgs));

    // Read buffer/length from the sret result
    var retBufLoad = (StdI64)EmitLoad(block, $"{sretVarName}._managed.buffer", varTypes);
    var retLenLoad = (StdI64)EmitLoad(block, $"{sretVarName}._managed.length", varTypes);
    return (retBufLoad, retLenLoad);
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

    if (enumType.BackingType is MlirStringBackingType) {
      // String-backed enum: the runtime value is the ordinal (i64).
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
    var lhsLen = (StdI64)EmitLoad(block, $"{lhsVarName}.length", varTypes);
    var rhsBuf = LoadManagedBuffer(block, rhsVarName, varTypes);
    var rhsLen = (StdI64)EmitLoad(block, $"{rhsVarName}.length", varTypes);

    // element_size needed to convert element counts to byte counts
    var lhsElemSize = (StdI64)EmitLoad(block, $"{lhsVarName}.element_size", varTypes);

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

    var tempName = $"__concat_{op.Result.Id}";
    EmitStore(block, allocResult, $"{tempName}.buffer", varTypes);
    EmitStore(block, totalLenOp.Result, $"{tempName}.length", varTypes);
    EmitStore(block, totalLenOp.Result, $"{tempName}.capacity", varTypes);
    EmitStore(block, lhsElemSize, $"{tempName}.element_size", varTypes);
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
    var srcElemSize = (StdI64)EmitLoad(block, $"{srcVarName}.element_size", varTypes);

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

    var tempName = $"__slice_{op.Result.Id}";
    EmitStore(block, sliceBufferOp.Result, $"{tempName}.buffer", varTypes);
    EmitStore(block, sliceLenOp.Result, $"{tempName}.length", varTypes);
    EmitStore(block, zeroOp.Result, $"{tempName}.capacity", varTypes);
    EmitStore(block, srcElemSize, $"{tempName}.element_size", varTypes);
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

    // Create Character struct: Character._managed = {buffer, length, capacity, element_size}
    var charVarName = $"__char_{op.Result.Id}";
    EmitStore(block, finalBuf, $"{charVarName}._managed.buffer", varTypes);
    EmitStore(block, finalLen, $"{charVarName}._managed.length", varTypes);
    EmitStore(block, finalLen, $"{charVarName}._managed.capacity", varTypes);
    // Characters are byte-level
    var elemSizeConst = new StdConstI64Op(1);
    block.AddOp(elemSizeConst);
    EmitStore(block, elemSizeConst.Result, $"{charVarName}._managed.element_size", varTypes);
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
      bool hasSret) {
    int flatIdx = fnParamOp.Index + (hasSret ? 1 : 0);
    var paramOp = new StdParamOp(flatIdx, fnParamOp.Name, new StdPtr(MlirContext.Current.NextId()));
    block.AddOp(paramOp);
    valueMap[fnParamOp.Result] = paramOp.Result;
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
      Dictionary<MaxonValue, StdValue> valueMap) {
    var calleeValue = valueMap[indirectCallOp.Callee];
    var newArgs = new List<StdValue>();

    foreach (var arg in indirectCallOp.Args) {
      newArgs.Add(valueMap[arg]);
    }

    StdValue? resultValue = null;
    if (indirectCallOp.ResultKind != null) {
      resultValue = indirectCallOp.ResultKind switch {
        MaxonValueKind.Integer => new StdI64(MlirContext.Current.NextId()),
        MaxonValueKind.Float => new StdF64(MlirContext.Current.NextId()),
        MaxonValueKind.Bool => new StdBool(MlirContext.Current.NextId()),
        MaxonValueKind.Byte => new StdI64(MlirContext.Current.NextId()),
        MaxonValueKind.Struct => throw new NotImplementedException("Struct return from indirect call not yet implemented"),
        MaxonValueKind.Enum => new StdI64(MlirContext.Current.NextId()),
        MaxonValueKind.Function => new StdPtr(MlirContext.Current.NextId()),
        _ => throw new InvalidOperationException($"Unsupported result kind for indirect call: {indirectCallOp.ResultKind}")
      };
    }

    var callOp = new StdIndirectCallOp(calleeValue, newArgs, resultValue);
    block.AddOp(callOp);

    if (indirectCallOp.Result != null && callOp.Result != null) {
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

  private static string? GetManagedFieldName(MlirStructType structType) {
    var managedField = structType.GetField("managed");
    if (managedField != null) return "managed";
    foreach (var field in structType.Fields)
      if (field.Type is MlirStructType nested && nested.Name == "__ManagedMemory")
        return field.Name;
    return null;
  }

  /// <summary>
  /// Determines the buffer field path for a struct variable that owns managed memory.
  /// Returns null if the struct type does not contain managed memory.
  /// </summary>
  private static string? GetManagedBufferPath(string varName, string structTypeName, Dictionary<string, MlirType> typeDefs) {
    if (structTypeName == "__ManagedMemory")
      return $"{varName}.buffer";
    if (typeDefs.TryGetValue(structTypeName, out var typeDef) && typeDef is MlirStructType structType) {
      var fieldName = GetManagedFieldName(structType);
      if (fieldName != null)
        return $"{varName}.{fieldName}.buffer";
    }
    return null;
  }

  /// <summary>
  /// Emit the cleanup sequence for a managed variable before scope exit.
  /// In tracking mode: calls maxon_cleanup_managed which prints CLEANUP/DECREF/FREE and frees.
  /// In non-tracking mode: just calls maxon_free on the buffer.
  /// </summary>
  private static void EmitManagedCleanup(
    MlirBlock<StandardOp> block,
    string varName,
    string bufferVarPath,
    Dictionary<string, string> varTypes) {
    if (!_trackAllocs) return; // TODO: enable non-tracking cleanup once validated
    // Derive capacity path from buffer path (e.g. "arr.managed.buffer" -> "arr.managed.capacity")
    var capacityPath = bufferVarPath[..bufferVarPath.LastIndexOf('.')] + ".capacity";
    var capacity = (StdI64)EmitLoad(block, capacityPath, varTypes);
    var buffer = (StdI64)EmitLoad(block, bufferVarPath, varTypes);
    var (tagPtr, tagLen) = EmitTrackingTagLoad(block, varName);
    block.AddOp(new StdCallRuntimeOp("maxon_cleanup_managed", [capacity, buffer, tagPtr, tagLen]));
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
