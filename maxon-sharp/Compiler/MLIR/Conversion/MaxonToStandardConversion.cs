using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static class MaxonToStandardConversion {
  public static MlirModule<StandardOp> Run(MlirModule<MaxonOp> module) {
    var result = new MlirModule<StandardOp>();
    result.RdataEntries.AddRange(module.RdataEntries);
    result.Globals.AddRange(module.Globals);
    foreach (var (k, v) in module.TypeDefs) result.TypeDefs[k] = v;

    // Build a lookup of functions by name for struct-aware call lowering
    var funcLookup = module.Functions.ToDictionary(f => f.Name);

    foreach (var func in module.Functions) {
      var retStructType = func.ReturnType as MlirStructType;

      bool isInstanceMethod = IsInstanceMethod(func);
      var selfStructType = isInstanceMethod ? (MlirStructType)func.ParamTypes[0] : null;

      // Build the new function signature:
      // - Instance method 'self' param is passed as a pointer (by reference)
      // - Other struct params are flattened to individual scalar params
      // - Struct return adds a hidden sret pointer as first param
      var newParamNames = new List<string>();
      var newParamTypes = new List<MlirType>();

      if (retStructType != null) {
        newParamNames.Add("__sret");
        newParamTypes.Add(MlirType.I64);
      }

      // Map from original param index to flattened param indices
      var paramIndexMap = new Dictionary<int, List<(int flatIndex, MlirStructField field)>>();
      int flatIdx = newParamNames.Count;

      for (int i = 0; i < func.ParamNames.Count; i++) {
        if (isInstanceMethod && i == 0) {
          // Instance method self param: pass as pointer (i64)
          newParamNames.Add("__self_ptr");
          newParamTypes.Add(MlirType.I64);
          flatIdx++;
        } else if (func.ParamTypes[i] is MlirStructType structType) {
          var fieldIndices = new List<(int, MlirStructField)>();
          foreach (var field in structType.Fields) {
            newParamNames.Add($"{func.ParamNames[i]}.{field.Name}");
            newParamTypes.Add(field.Type);
            fieldIndices.Add((flatIdx, field));
            flatIdx++;
          }
          paramIndexMap[i] = fieldIndices;
        } else {
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(func.ParamTypes[i]);
          flatIdx++;
        }
      }

      MlirType? newReturnType = retStructType != null ? null : func.ReturnType;
      var newFunc = new MlirFunction<StandardOp>(func.Name, newParamNames, newParamTypes, newReturnType);
      var valueMap = new Dictionary<MaxonValue, StdValue>();
      var varTypes = new Dictionary<string, string>();
      // Maps MaxonStruct value IDs to their variable name prefix (for field access)
      var structVarNames = new Dictionary<int, string>();

      bool sretParamEmitted = false;
      foreach (var block in func.Body.Blocks) {
        var newBlock = newFunc.Body.AddBlock(block.Name);

        // In the entry block of sret functions, save the sret pointer (param 0)
        // to a named stack variable so it survives register clobbering.
        if (retStructType != null && !sretParamEmitted) {
          var sretParam = new StdI64(MlirContext.Current.NextId());
          newBlock.AddOp(new StdParamOp(0, "__sret", sretParam));
          newBlock.AddOp(new StdStoreI64Op(sretParam, "__sret"));
          varTypes["__sret"] = "i64";
          sretParamEmitted = true;
        }

        foreach (var op in block.Operations) {
          switch (op) {
            case MaxonParamOp paramOp: {
              var stdResult = paramOp.ValueKind.CreateStdValue();
              // Adjust index: if this function has sret, scalar params shift by 1.
              // Also account for struct params that were flattened before this one.
              int adjustedIndex = ComputeFlatParamIndex(paramOp.Index, func, retStructType != null);
              newBlock.AddOp(new StdParamOp(adjustedIndex, paramOp.Name, stdResult));
              valueMap[paramOp.Result] = stdResult;
              EmitStore(newBlock, stdResult, paramOp.Name, varTypes);
              break;
            }
            case MaxonStructParamOp structParamOp: {
              if (isInstanceMethod && structParamOp.Name == "self") {
                // Instance method self: receive as pointer, save it, load fields indirectly
                int selfFlatIdx = retStructType != null ? 1 : 0;
                var selfPtrVal = new StdI64(MlirContext.Current.NextId());
                newBlock.AddOp(new StdParamOp(selfFlatIdx, "__self_ptr", selfPtrVal));
                newBlock.AddOp(new StdStoreI64Op(selfPtrVal, "__self_ptr"));
                varTypes["__self_ptr"] = "i64";
                // Load each field from the self pointer into named variables
                LoadStructFieldsFromPointer(newBlock, selfPtrVal, "self", selfStructType!, varTypes);
                structVarNames[structParamOp.Result.Id] = "self";
              } else {
                // Non-self struct param: flattened to individual scalar params.
                var fieldMappings = paramIndexMap[structParamOp.Index];
                foreach (var (paramFlatIdx, field) in fieldMappings) {
                  var fieldVarName = $"{structParamOp.Name}.{field.Name}";
                  var fieldResult = StdValueFactory.CreateStdValueForType(field.Type);
                  newBlock.AddOp(new StdParamOp(paramFlatIdx, fieldVarName, fieldResult));
                  EmitStore(newBlock, fieldResult, fieldVarName, varTypes);
                }
                structVarNames[structParamOp.Result.Id] = structParamOp.Name;
              }
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
                case MaxonValueKind.Struct:
                  throw new InvalidOperationException("Struct literals are not MaxonLiteralOp");
              }
              break;
            }
            case MaxonStructLiteralOp structLitOp: {
              // Store each field value to named slots.
              var tempName = $"__struct_{structLitOp.Result.Id}";
              var structType = module.TypeDefs[structLitOp.TypeName];
              foreach (var (fieldName, fieldVal) in structLitOp.FieldValues) {
                var fieldVarName = $"{tempName}.{fieldName}";
                var mappedVal = valueMap[fieldVal];
                EmitStore(newBlock, mappedVal, fieldVarName, varTypes);
              }
              structVarNames[structLitOp.Result.Id] = tempName;
              break;
            }
            case MaxonAssignOp assignOp: {
              if (assignOp.ValueKind == MaxonValueKind.Struct) {
                // Struct assignment: copy all fields from source to destination
                var srcName = structVarNames[assignOp.Value.Id];
                var dstName = assignOp.VarName;
                var structTypeName = ((MaxonStruct)assignOp.Value).TypeName;
                var structType = module.TypeDefs[structTypeName];
                CopyStructFields(newBlock, srcName, dstName, structType, varTypes);
                // After assignment, the struct value is now known by both names
                structVarNames[assignOp.Value.Id] = dstName;
              } else {
                var mappedValue = valueMap[assignOp.Value];
                EmitStore(newBlock, mappedValue, assignOp.VarName, varTypes);
                // In instance methods, writing to a self field variable must also
                // write through the self pointer so changes propagate to the caller.
                if (isInstanceMethod && selfStructType != null) {
                  EmitSelfFieldWriteThrough(newBlock, mappedValue, assignOp.VarName, selfStructType);
                }
              }
              break;
            }
            case MaxonVarRefOp varRef: {
              var loaded = EmitLoad(newBlock, varRef.VarName, varTypes);
              valueMap[varRef.Result] = loaded;
              break;
            }
            case MaxonStructVarRefOp structVarRef: {
              // Just record the variable name mapping
              structVarNames[structVarRef.Result.Id] = structVarRef.VarName;
              break;
            }
            case MaxonFieldAccessOp fieldAccess: {
              var structName = structVarNames[fieldAccess.StructValue.Id];
              var fieldVarName = $"{structName}.{fieldAccess.FieldName}";
              var loaded = EmitLoad(newBlock, fieldVarName, varTypes);
              valueMap[fieldAccess.Result] = loaded;
              break;
            }
            case MaxonFieldAssignOp fieldAssign: {
              var structName = structVarNames[fieldAssign.StructValue.Id];
              var fieldVarName = $"{structName}.{fieldAssign.FieldName}";
              var mappedVal = valueMap[fieldAssign.NewValue];
              EmitStore(newBlock, mappedVal, fieldVarName, varTypes);
              // Write through self pointer for instance methods
              if (isInstanceMethod && structName == "self" && selfStructType != null) {
                EmitSelfFieldWriteThrough(newBlock, mappedVal, fieldAssign.FieldName, selfStructType);
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
                  // Cast to byte: mask with 0xFF
                  var maskOp = new StdConstI64Op(0xFF);
                  newBlock.AddOp(maskOp);
                  var andOp = new StdAndI64Op((StdI64)input, maskOp.Result);
                  newBlock.AddOp(andOp);
                  valueMap[castOp.Result] = andOp.Result;
                  break;
                }
                case MaxonValueKind.Integer: {
                  // Byte/int to int: pass through (bytes are already stored as I64)
                  if (input is StdI64 i64) {
                    valueMap[castOp.Result] = i64;
                  } else if (input is StdF64 f64) {
                    var fpToSi = new StdFpToSiOp(f64);
                    newBlock.AddOp(fpToSi);
                    valueMap[castOp.Result] = fpToSi.Result;
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
                  } else if (input is StdF64 f64) {
                    valueMap[castOp.Result] = f64;
                  } else {
                    throw new InvalidOperationException($"Unsupported cast to float from: {input.GetType().Name}");
                  }
                  break;
                }
                case MaxonValueKind.Bool:
                  throw new InvalidOperationException("Cannot cast to bool");
                case MaxonValueKind.Struct:
                  throw new InvalidOperationException("Cannot cast to struct");
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
                case MaxonValueKind.Byte:
                case MaxonValueKind.Struct:
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
                case MaxonValueKind.Byte:
                case MaxonValueKind.Struct:
                  throw new InvalidOperationException($"Unsupported global variable type: {globalStore.ValueKind}");
              }
              break;
            }
            case MaxonCallOp callOp:
              LowerCall(callOp, funcLookup, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonReturnOp retOp:
              LowerReturn(retOp, retStructType, newBlock, valueMap, varTypes, structVarNames);
              break;
            default:
              throw new InvalidOperationException($"No MaxonToStandard conversion for: {op.GetType().Name} ({op.Mnemonic})");
          }
        }
      }
      result.AddFunction(newFunc);
    }
    return result;
  }

  /// <summary>
  /// Compute the flat parameter index for a scalar param, accounting for
  /// sret offset and preceding struct params that expanded to multiple flat params.
  /// Instance method self (param 0 with struct type named "self") is passed as
  /// a single pointer, not flattened.
  /// </summary>
  private static int ComputeFlatParamIndex(int originalIndex, MlirFunction<MaxonOp> func, bool hasSret) {
    bool isInstanceMethod = IsInstanceMethod(func);
    int flatIdx = hasSret ? 1 : 0;
    for (int i = 0; i < originalIndex; i++) {
      if (isInstanceMethod && i == 0) {
        // Self is passed as a single pointer param
        flatIdx += 1;
      } else if (func.ParamTypes[i] is MlirStructType st) {
        flatIdx += st.Fields.Count;
      } else {
        flatIdx += 1;
      }
    }
    return flatIdx;
  }

  private static void LowerCall(
    MaxonCallOp callOp,
    Dictionary<string, MlirFunction<MaxonOp>> funcLookup,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var calleeFunc = funcLookup[callOp.Callee];
    var calleeRetStructType = calleeFunc.ReturnType as MlirStructType;

    var newArgs = new List<StdValue>();

    // If callee returns struct, allocate local space and pass sret pointer.
    string? sretVarName = null;
    if (calleeRetStructType != null) {
      sretVarName = $"__sret_{callOp.Result!.Id}";
      AllocateStructOnStack(block, sretVarName, calleeRetStructType, varTypes);
      var leaOp = new StdLeaOp(sretVarName);
      block.AddOp(leaOp);
      newArgs.Add(leaOp.Result);
    }

    // Detect instance method call: first param is "self" with struct type
    bool calleeIsInstanceMethod = IsInstanceMethod(calleeFunc);
    string? selfBufName = null;

    // Flatten struct arguments to individual field values
    for (int i = 0; i < callOp.Args.Count; i++) {
      var arg = callOp.Args[i];
      if (calleeIsInstanceMethod && i == 0 && structVarNames.TryGetValue(arg.Id, out var selfName)) {
        // Instance method self: pass pointer to struct fields on stack.
        var calleeSelfStructType = (MlirStructType)calleeFunc.ParamTypes[0];
        selfBufName = $"__selfbuf_{MlirContext.Current.NextId()}";
        var selfPtr = AllocateAndCopyStructToStack(block, selfName, selfBufName, calleeSelfStructType, varTypes);
        newArgs.Add(selfPtr);
      } else if (calleeFunc.ParamTypes[i] is MlirStructType argStructType && structVarNames.TryGetValue(arg.Id, out var argStructName)) {
        foreach (var field in argStructType.Fields) {
          var fieldVarName = $"{argStructName}.{field.Name}";
          var loaded = EmitLoad(block, fieldVarName, varTypes);
          newArgs.Add(loaded);
        }
      } else {
        newArgs.Add(valueMap[arg]);
      }
    }

    if (calleeRetStructType != null) {
      var funcCall = new StdCallOp(callOp.Callee, newArgs);
      block.AddOp(funcCall);
      if (callOp.Result != null) {
        structVarNames[callOp.Result.Id] = sretVarName!;
      }
    } else {
      var callResult = callOp.ResultKind?.CreateStdValue();
      var funcCall = new StdCallOp(callOp.Callee, newArgs, callResult);
      block.AddOp(funcCall);
      if (callOp.Result != null && funcCall.Result != null) {
        valueMap[callOp.Result] = funcCall.Result;
      }
    }

    // After instance method call, read back modified self fields from the
    // self buffer into the caller's original struct variable.
    if (calleeIsInstanceMethod && selfBufName != null && callOp.Args.Count > 0
        && structVarNames.TryGetValue(callOp.Args[0].Id, out var callerSelfName)) {
      var calleeSelfStructType = (MlirStructType)calleeFunc.ParamTypes[0];
      CopyStructFields(block, selfBufName, callerSelfName, calleeSelfStructType, varTypes);
    }
  }

  private static void LowerReturn(
    MaxonReturnOp retOp,
    MlirStructType? retStructType,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    if (retStructType != null && retOp.Value != null) {
      // Write struct fields through the sret pointer, reloaded from the
      // stack variable where we saved it at entry time.
      var srcName = structVarNames[retOp.Value.Id];
      var sretLoad = new StdLoadI64Op("__sret");
      block.AddOp(sretLoad);
      WriteStructFieldsToPointer(block, srcName, sretLoad.Result, retStructType, varTypes);
      block.AddOp(new StdReturnOp());
    } else {
      StdValue? newRetVal = retOp.Value != null ? valueMap[retOp.Value] : null;
      block.AddOp(new StdReturnOp(newRetVal));
    }
  }

  private static void LowerUnaryF64(
    Dictionary<MaxonValue, StdValue> valueMap,
    MlirBlock<StandardOp> block,
    MaxonValue maxonInput, MaxonValue maxonResult,
    Func<StdF64, StdUnaryF64Op> factory) {
    var input = (StdF64)valueMap[maxonInput];
    var stdOp = factory(input);
    block.AddOp(stdOp);
    valueMap[maxonResult] = stdOp.Result;
  }

  private static void LowerBinaryF64(
    Dictionary<MaxonValue, StdValue> valueMap,
    MlirBlock<StandardOp> block,
    MaxonValue maxonLhs, MaxonValue maxonRhs, MaxonValue maxonResult,
    Func<StdF64, StdF64, StdBinaryF64Op> factory) {
    var lhs = (StdF64)valueMap[maxonLhs];
    var rhs = (StdF64)valueMap[maxonRhs];
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
      case StdF64 f64:
        block.AddOp(new StdStoreF64Op(f64, varName));
        varTypes[varName] = "f64";
        break;
      case StdBool b:
        block.AddOp(new StdStoreI1Op(b, varName));
        varTypes[varName] = "i1";
        break;
      case StdPtr:
        throw new InvalidOperationException("Cannot store a pointer value directly - struct fields should be stored individually");
      case StdI32:
        throw new InvalidOperationException("Cannot store I32 values in variable slots");
      default:
        throw new InvalidOperationException($"Unsupported StdValue type for store: {value.GetType().Name}");
    }
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
    } else if (fieldType == MlirType.I64) {
      var zeroOp = new StdConstI64Op(0);
      block.AddOp(zeroOp);
      EmitStore(block, zeroOp.Result, varName, varTypes);
    } else {
      throw new InvalidOperationException($"Unsupported field type for zero-store: {fieldType}");
    }
  }

  private static bool IsInstanceMethod<T>(MlirFunction<T> func) where T : IPrintableOp =>
    func.ParamNames.Count > 0
    && func.ParamNames[0] == "self"
    && func.ParamTypes[0] is MlirStructType;

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
      var loaded = EmitLoad(block, srcFieldVar, varTypes);
      EmitStore(block, loaded, dstFieldVar, varTypes);
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
      EmitZeroStore(block, field.Type, fieldVarName, varTypes);
    }
  }

  /// <summary>
  /// Load all struct fields from a pointer into named variables.
  /// </summary>
  private static void LoadStructFieldsFromPointer(
    MlirBlock<StandardOp> block,
    StdValue ptr,
    string varName,
    MlirStructType structType,
    Dictionary<string, string> varTypes) {
    foreach (var field in structType.Fields) {
      var fieldVarName = $"{varName}.{field.Name}";
      var loadOp = new StdLoadIndirectOp(ptr, field.Offset, field.Type);
      block.AddOp(loadOp);
      EmitStore(block, loadOp.Result, fieldVarName, varTypes);
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
      var loaded = EmitLoad(block, srcFieldVar, varTypes);
      block.AddOp(new StdStoreIndirectOp(loaded, ptr, field.Offset, field.Type));
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
      var loaded = EmitLoad(block, srcField, varTypes);
      var dstField = $"{bufName}.{field.Name}";
      EmitStore(block, loaded, dstField, varTypes);
    }
    var leaOp = new StdLeaOp(bufName);
    block.AddOp(leaOp);
    return leaOp.Result;
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
    // Logical operations (bool)
    { (MaxonBinOperator.And, MaxonValueKind.Bool), (l, r) => { var op = new StdAndI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
    { (MaxonBinOperator.Or, MaxonValueKind.Bool), (l, r) => { var op = new StdOrI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
    { (MaxonBinOperator.BitXor, MaxonValueKind.Bool), (l, r) => { var op = new StdXorI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
  };

}
