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

      bool isStructInstanceMethod = IsStructInstanceMethod(func);
      bool isEnumInstanceMethod = IsEnumInstanceMethod(func);
      bool isInstanceMethod = isStructInstanceMethod || isEnumInstanceMethod;
      var selfStructType = isStructInstanceMethod ? (MlirStructType)func.ParamTypes[0] : null;

      // Build the new function signature:
      // - Struct instance method 'self' param is passed as a pointer (by reference)
      // - Enum instance method 'self' param is passed as a scalar
      // - Other struct params are flattened to individual scalar params
      // - Enum params are passed as scalars
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
        if (isStructInstanceMethod && i == 0) {
          // Struct instance method self param: pass as pointer (i64)
          newParamNames.Add("__self_ptr");
          newParamTypes.Add(MlirType.I64);
          flatIdx++;
        } else if (isEnumInstanceMethod && i == 0) {
          // Enum instance method self param: pass as scalar
          var enumType = (MlirEnumType)func.ParamTypes[0];
          var backingMlirType = enumType.BackingType == MlirType.F64 ? MlirType.F64 : MlirType.I64;
          newParamNames.Add("self");
          newParamTypes.Add(backingMlirType);
          flatIdx++;
        } else if (func.ParamTypes[i] is MlirEnumType enumParamType) {
          // Enum param: pass as scalar
          var backingMlirType = enumParamType.BackingType == MlirType.F64 ? MlirType.F64 : MlirType.I64;
          newParamNames.Add(func.ParamNames[i]);
          newParamTypes.Add(backingMlirType);
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
        newReturnType = retEnumType.BackingType == MlirType.F64 ? MlirType.F64 : MlirType.I64;
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
      // Maps variable names to their resolved struct prefix (for cross-block references)
      var varNameToStructPrefix = new Dictionary<string, string>();

      // Pre-scan: find constant array literals (arrays with all-literal elements).
      // Both let and var arrays get rdata; var arrays additionally copy to heap (COW).
      var constantArrayLiterals = new Dictionary<int, (string rdataLabel, long[] values, bool isMutable)>();
      foreach (var block in func.Body.Blocks) {
        // Collect MaxonLiteralOp results and MaxonStructLiteralOp results
        var literalValues = new Dictionary<int, long>();
        var structLiterals = new Dictionary<int, MaxonStructLiteralOp>();
        foreach (var op in block.Operations) {
          if (op is MaxonLiteralOp lit && lit.ValueKind == MaxonValueKind.Integer)
            literalValues[lit.Result.Id] = lit.IntValue;
          if (op is MaxonStructLiteralOp slit)
            structLiterals[slit.Result.Id] = slit;
        }
        // Find array assigns with all-constant elements (both let and var)
        foreach (var op in block.Operations) {
          if (op is not MaxonAssignOp { ValueKind: MaxonValueKind.Struct } assignOp) continue;
          if (!structLiterals.TryGetValue(assignOp.Value.Id, out var arrayStructLit)) continue;
          if (arrayStructLit.TypeName != "Array" || arrayStructLit.ArrayLiteralTag == null) continue;

          var tag = arrayStructLit.ArrayLiteralTag;
          int count = arrayStructLit.ArrayLiteralCount;
          // Collect element values from the element assign ops
          var elementValues = new long[count];
          bool allConstant = true;
          foreach (var elemOp in block.Operations) {
            if (elemOp is not MaxonAssignOp elemAssign) continue;
            if (!elemAssign.VarName.StartsWith($"{tag}.")) continue;
            var indexStr = elemAssign.VarName[($"{tag}.".Length)..];
            if (!int.TryParse(indexStr, out var idx)) continue;
            if (!literalValues.TryGetValue(elemAssign.Value.Id, out var val)) {
              allConstant = false;
              break;
            }
            elementValues[idx] = val;
          }
          if (allConstant) {
            var rdataLabel = $"__const_array_{assignOp.VarName}";
            constantArrayLiterals[arrayStructLit.Result.Id] = (rdataLabel, elementValues, assignOp.IsMutable);
          }
        }
      }

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
              if (isStructInstanceMethod && structParamOp.Name == "self") {
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
              int adjustedIndex = ComputeFlatParamIndex(enumParamOp.Index, func, retStructType != null);
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

              // For array literals, patch the buffer field to point to element data
              if (structLitOp.ArrayLiteralTag != null && structLitOp.TypeName == "Array") {
                var managedName = $"{tempName}.managed";
                var bufferVarName = $"{managedName}.buffer";

                if (constantArrayLiterals.TryGetValue(structLitOp.Result.Id, out var constArrayInfo)) {
                  // Constant array: store element data in .rdata
                  var rdataBytes = new byte[constArrayInfo.values.Length * 8];
                  for (int i = 0; i < constArrayInfo.values.Length; i++) {
                    BitConverter.GetBytes(constArrayInfo.values[i]).CopyTo(rdataBytes, i * 8);
                  }
                  result.RdataEntries.Add((constArrayInfo.rdataLabel, rdataBytes, 8));

                  var leaRdataOp = new StdLeaRdataOp(constArrayInfo.rdataLabel);
                  newBlock.AddOp(leaRdataOp);
                  var rdataPtrOp = new StdPtrToI64Op(leaRdataOp.Result);
                  newBlock.AddOp(rdataPtrOp);

                  if (constArrayInfo.isMutable) {
                    // COW: allocate heap buffer and copy rdata contents into it
                    // Store rdata ptr and reload after call (call clobbers registers)
                    var rdataPtrVar = $"__cow_rdata_{constArrayInfo.rdataLabel}";
                    newBlock.AddOp(new StdStoreI64Op(rdataPtrOp.Result, rdataPtrVar));
                    varTypes[rdataPtrVar] = "i64";

                    var allocSizeOp = new StdConstI64Op(rdataBytes.Length);
                    newBlock.AddOp(allocSizeOp);
                    var heapBuf = new StdI64(MlirContext.Current.NextId());
                    newBlock.AddOp(new StdCallRuntimeOp("maxon_alloc", [allocSizeOp.Result], heapBuf));

                    // Reload rdata ptr and byte count for memcopy (clobbered by call)
                    var rdataPtrReload = (StdI64)EmitLoad(newBlock, rdataPtrVar, varTypes);
                    var copySizeOp = new StdConstI64Op(rdataBytes.Length);
                    newBlock.AddOp(copySizeOp);
                    newBlock.AddOp(new StdMemCopyOp(rdataPtrReload, heapBuf, copySizeOp.Result));
                    newBlock.AddOp(new StdStoreI64Op(heapBuf, bufferVarName));
                  } else {
                    // Immutable: point directly to rdata
                    newBlock.AddOp(new StdStoreI64Op(rdataPtrOp.Result, bufferVarName));
                  }
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
              break;
            }
            case MaxonAssignOp assignOp: {
              if (assignOp.ValueKind == MaxonValueKind.Struct) {
                // Struct assignment: copy all fields from source to destination
                var srcName = structVarNames[assignOp.Value.Id];
                var dstName = assignOp.VarName;
                var structTypeName = ((MaxonStruct)assignOp.Value).TypeName;
                var structType = (MlirStructType)module.TypeDefs[structTypeName];
                CopyStructFields(newBlock, srcName, dstName, structType, varTypes);
                // After assignment, the struct value is now known by both names
                structVarNames[assignOp.Value.Id] = dstName;
                // Record variable name mapping for cross-block references
                varNameToStructPrefix[assignOp.VarName] = dstName;
              } else {
                var mappedValue = valueMap[assignOp.Value];
                EmitStore(newBlock, mappedValue, assignOp.VarName, varTypes);
                // In struct instance methods, writing to a self field variable must also
                // write through the self pointer so changes propagate to the caller.
                if (isStructInstanceMethod && selfStructType != null) {
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
              // Look up the resolved prefix from prior assignments, or use the raw variable name
              var resolvedName = varNameToStructPrefix.GetValueOrDefault(structVarRef.VarName, structVarRef.VarName);
              structVarNames[structVarRef.Result.Id] = resolvedName;
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
                  } else if (input is StdF64 f64Input) {
                    var fpToSi = new StdFpToSiOp(f64Input);
                    newBlock.AddOp(fpToSi);
                    intInput = fpToSi.Result;
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
              LowerCall(callOp, funcLookup, newBlock, valueMap, varTypes, structVarNames, isStructInstanceMethod, selfStructType);
              break;
            case MaxonReturnOp retOp:
              LowerReturn(retOp, retStructType, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonThrowOp throwOp:
              LowerThrow(throwOp, newBlock, valueMap);
              break;
            case MaxonTryCallOp tryCallOp:
              LowerTryCall(tryCallOp, funcLookup, newBlock, valueMap, varTypes, structVarNames, isStructInstanceMethod, selfStructType);
              break;
            case MaxonManagedMemGetOp memGetOp:
              LowerManagedMemGet(memGetOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemSetOp memSetOp:
              LowerManagedMemSet(memSetOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemCreateOp memCreateOp:
              LowerManagedMemCreate(memCreateOp, newBlock, valueMap, varTypes, structVarNames);
              break;
            case MaxonManagedMemGrowOp memGrowOp:
              LowerManagedMemGrow(memGrowOp, newBlock, valueMap, varTypes, structVarNames, isStructInstanceMethod, selfStructType);
              break;
            case MaxonManagedMemShiftOp memShiftOp:
              LowerManagedMemShift(memShiftOp, newBlock, valueMap, varTypes, structVarNames);
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
    bool isStructMethod = IsStructInstanceMethod(func);
    bool isEnumMethod = IsEnumInstanceMethod(func);
    int flatIdx = hasSret ? 1 : 0;
    for (int i = 0; i < originalIndex; i++) {
      if ((isStructMethod || isEnumMethod) && i == 0) {
        // Self is passed as a single param (pointer for struct, scalar for enum)
        flatIdx += 1;
      } else if (func.ParamTypes[i] is MlirStructType st) {
        flatIdx += st.Fields.Count;
      } else if (func.ParamTypes[i] is not MlirStructType and not MlirEnumType) {
        flatIdx += 1;
      } else {
        throw new InvalidOperationException($"Unhandled parameter type in flat index computation: {func.ParamTypes[i].GetType().Name}");
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
    Dictionary<int, string> structVarNames,
    bool isStructInstanceMethod,
    MlirStructType? selfStructType) {
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

    var selfBufName = FlattenCallArgs(callOp.Args, calleeFunc, block, valueMap, varTypes, structVarNames, newArgs, callOp.Callee);

    if (calleeRetStructType != null) {
      var funcCall = new StdCallOp(callOp.Callee, newArgs);
      block.AddOp(funcCall);
      if (callOp.Result != null) {
        structVarNames[callOp.Result.Id] = sretVarName!;
      }
    } else {
      var callResult = ResolveCallResultType(callOp.ResultKind, calleeFunc.ReturnType);
      var funcCall = new StdCallOp(callOp.Callee, newArgs, callResult);
      block.AddOp(funcCall);
      if (callOp.Result != null && funcCall.Result != null) {
        valueMap[callOp.Result] = funcCall.Result;
      }
    }

    ReadBackSelfFields(callOp.Args, calleeFunc, selfBufName, block, varTypes,
      structVarNames, isStructInstanceMethod, selfStructType);
  }

  private static void LowerReturn(
    MaxonReturnOp retOp,
    MlirStructType? retStructType,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {

    // Error propagation: forward the error flag to the caller
    if (retOp.IsErrorPropagation) {
      var mappedErrFlag = valueMap[retOp.Value!];
      block.AddOp(new StdErrorReturnOp(mappedErrFlag));
      return;
    }

    if (retStructType != null && retOp.Value != null) {
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
    bool isStructInstanceMethod,
    MlirStructType? selfStructType) {

    var calleeFunc = funcLookup[tryCallOp.Callee];
    var newArgs = new List<StdValue>();

    var selfBufName = FlattenCallArgs(tryCallOp.Args, calleeFunc, block, valueMap, varTypes, structVarNames, newArgs, tryCallOp.Callee);

    var stdResult = ResolveCallResultType(tryCallOp.ResultKind, calleeFunc.ReturnType);
    var stdTryCall = new StdTryCallOp(tryCallOp.Callee, newArgs, stdResult);
    block.AddOp(stdTryCall);

    if (tryCallOp.Result != null && stdResult != null) {
      valueMap[tryCallOp.Result] = stdResult;
    }

    valueMap[tryCallOp.ErrorFlag] = stdTryCall.ErrorFlag;
    block.AddOp(new StdStoreI64Op(stdTryCall.ErrorFlag, "__error_flag"));
    varTypes["__error_flag"] = "i64";

    ReadBackSelfFields(tryCallOp.Args, calleeFunc, selfBufName, block, varTypes,
      structVarNames, isStructInstanceMethod, selfStructType);
  }

  /// <summary>
  /// Flatten call/try_call arguments for the standard calling convention.
  /// Returns the self buffer name if a struct instance method self arg was allocated.
  /// </summary>
  private static string? FlattenCallArgs(
    List<MaxonValue> args,
    MlirFunction<MaxonOp> calleeFunc,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    List<StdValue> newArgs,
    string calleeName) {
    bool calleeIsStructInstance = IsStructInstanceMethod(calleeFunc);
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
        foreach (var field in argStructType.Fields) {
          var fieldVarName = $"{argStructName}.{field.Name}";
          var loaded = EmitLoad(block, fieldVarName, varTypes);
          newArgs.Add(loaded);
        }
      } else if (calleeFunc.ParamTypes[i] is not MlirStructType and not MlirEnumType) {
        newArgs.Add(valueMap[arg]);
      } else {
        throw new InvalidOperationException($"Unhandled call argument type: {calleeFunc.ParamTypes[i].GetType().Name} for arg {i} in call to '{calleeName}'");
      }
    }
    return selfBufName;
  }

  /// <summary>
  /// Resolve the standard-level result value type for a call or try_call.
  /// </summary>
  private static StdValue? ResolveCallResultType(MaxonValueKind? resultKind, MlirType? calleeReturnType) {
    if (resultKind == MaxonValueKind.Enum && calleeReturnType is MlirEnumType retEnumType) {
      return retEnumType.BackingType == MlirType.F64
        ? new StdF64(MlirContext.Current.NextId())
        : new StdI64(MlirContext.Current.NextId());
    }
    return resultKind?.CreateStdValue();
  }

  /// <summary>
  /// After a struct instance method call, read back modified self fields from
  /// the self buffer and write through __self_ptr if needed.
  /// </summary>
  private static void ReadBackSelfFields(
    List<MaxonValue> args,
    MlirFunction<MaxonOp> calleeFunc,
    string? selfBufName,
    MlirBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames,
    bool isStructInstanceMethod,
    MlirStructType? selfStructType) {
    bool calleeIsStructInstance = IsStructInstanceMethod(calleeFunc);
    if (calleeIsStructInstance && selfBufName != null && args.Count > 0
        && structVarNames.TryGetValue(args[0].Id, out var callerSelfName)) {
      var calleeSelfStructType = (MlirStructType)calleeFunc.ParamTypes[0];
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
    if (value is StdF64 f64) return f64;
    if (value is StdI64 i64) {
      var conv = new StdSiToFpOp(i64);
      block.AddOp(conv);
      return conv.Result;
    }
    throw new InvalidOperationException($"Cannot promote {value.GetType().Name} to F64");
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
      case StdF64 f64:
        block.AddOp(new StdStoreF64Op(f64, varName));
        varTypes[varName] = "f64";
        break;
      case StdBool b:
        block.AddOp(new StdStoreI1Op(b, varName));
        varTypes[varName] = "i1";
        break;
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

  private static bool IsStructInstanceMethod<T>(MlirFunction<T> func) where T : IPrintableOp =>
    func.ParamNames.Count > 0
    && func.ParamNames[0] == "self"
    && func.ParamTypes[0] is MlirStructType;

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
  /// Compute address: buffer + index * elementSize
  /// </summary>
  private static StdI64 ComputeElementAddress(
    MlirBlock<StandardOp> block,
    StdI64 buffer,
    StdI64 index,
    int elementSize) {
    var sizeOp = new StdConstI64Op(elementSize);
    block.AddOp(sizeOp);
    var offsetOp = new StdMulI64Op(index, sizeOp.Result);
    block.AddOp(offsetOp);
    var addrOp = new StdAddI64Op(buffer, offsetOp.Result);
    block.AddOp(addrOp);
    return addrOp.Result;
  }

  /// <summary>
  /// __managed_memory_get_unchecked(managed, index): load element from heap buffer.
  /// buffer[index] = *(buffer + index * elementSize)
  /// </summary>
  private static void LowerManagedMemGet(
    MaxonManagedMemGetOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var addr = ComputeElementAddress(block, buffer, index, op.ElementSize);
    // Load from computed address: treat as indirect load from base=addr, offset=0
    var loadOp = new StdLoadIndirectOp(addr, 0, MlirType.I64);
    block.AddOp(loadOp);
    valueMap[op.Result] = loadOp.Result;
  }

  /// <summary>
  /// __managed_memory_set_at(managed, index, value): store element into heap buffer.
  /// </summary>
  private static void LowerManagedMemSet(
    MaxonManagedMemSetOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var value = valueMap[op.Value];
    var addr = ComputeElementAddress(block, buffer, index, op.ElementSize);
    // Store to computed address
    block.AddOp(new StdStoreIndirectOp(value, addr, 0, MlirType.I64));
  }

  /// <summary>
  /// __managed_memory_create(count, elementSize): allocate heap buffer.
  /// Returns new __ManagedMemory struct (buffer, length, capacity).
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
    // Call maxon_alloc(byteSize)
    var allocResult = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_alloc", [byteSizeOp.Result], allocResult));
    // Store as a __ManagedMemory struct: buffer=ptr, length=count, capacity=count
    var tempName = $"__managed_create_{op.Result.Id}";
    EmitStore(block, allocResult, $"{tempName}.buffer", varTypes);
    EmitStore(block, count, $"{tempName}.length", varTypes);
    EmitStore(block, count, $"{tempName}.capacity", varTypes);
    structVarNames[op.Result.Id] = tempName;
  }

  /// <summary>
  /// __managed_memory_grow(managed, newCapacity): grow heap buffer to new capacity.
  /// Uses realloc to grow (or allocate) the buffer, then updates managed struct fields.
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
    var newCap = (StdI64)valueMap[op.NewCapacity];

    // Load old buffer pointer
    var oldBuffer = LoadManagedBuffer(block, managedVarName, varTypes);

    // Compute new byte size = newCap * elementSize
    var elemSizeOp = new StdConstI64Op(op.ElementSize);
    block.AddOp(elemSizeOp);
    var newByteSizeOp = new StdMulI64Op(newCap, elemSizeOp.Result);
    block.AddOp(newByteSizeOp);

    // Realloc: grows buffer in-place or allocates new, copies old data, frees old
    var newBufferResult = new StdI64(MlirContext.Current.NextId());
    block.AddOp(new StdCallRuntimeOp("maxon_realloc", [oldBuffer, newByteSizeOp.Result], newBufferResult));

    // Update managed struct fields
    EmitStore(block, newBufferResult, $"{managedVarName}.buffer", varTypes);
    EmitStore(block, newCap, $"{managedVarName}.capacity", varTypes);

    // Write through to self pointer if in struct instance method
    if (isStructInstanceMethod && selfStructType != null && managedVarName.StartsWith("self.")) {
      EmitNestedSelfFieldWriteThrough(block, newBufferResult, managedVarName, "buffer", selfStructType);
      EmitNestedSelfFieldWriteThrough(block, newCap, managedVarName, "capacity", selfStructType);
    }
  }

  /// <summary>
  /// __managed_memory_shift_right/left(managed, index, count): shift elements in buffer.
  /// For shift_right: move elements [index..index+count-1] to [index+1..index+count] (backwards copy)
  /// For shift_left: move elements [index+1..index+count] to [index..index+count-1] (forward copy)
  /// Implemented as element-by-element copy using indirect load/store.
  /// </summary>
  private static void LowerManagedMemShift(
    MaxonManagedMemShiftOp op,
    MlirBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> structVarNames) {
    var managedVarName = ResolveManagedVarName(op.ManagedStruct, structVarNames);
    var buffer = LoadManagedBuffer(block, managedVarName, varTypes);
    var index = (StdI64)valueMap[op.Index];
    var count = (StdI64)valueMap[op.Count];

    // Compute base address = buffer + index * elementSize
    var elemSizeConst = new StdConstI64Op(op.ElementSize);
    block.AddOp(elemSizeConst);

    if (op.ShiftRight) {
      // Shift right: copy from [index+count-1] down to [index], moving each one position right
      // Effectively: for i in (count-1)..0: buffer[index+i+1] = buffer[index+i]
      // We implement this as a memcopy of count elements starting at index, shifted by +1
      var totalOffsetOp = new StdMulI64Op(index, elemSizeConst.Result);
      block.AddOp(totalOffsetOp);
      var srcAddr = new StdAddI64Op(buffer, totalOffsetOp.Result);
      block.AddOp(srcAddr);
      // Dest is src + elementSize (one position to the right)
      var oneElemOp = new StdConstI64Op(op.ElementSize);
      block.AddOp(oneElemOp);
      var dstAddr = new StdAddI64Op(srcAddr.Result, oneElemOp.Result);
      block.AddOp(dstAddr);
      // Byte count
      var bytesOp = new StdMulI64Op(count, elemSizeConst.Result);
      block.AddOp(bytesOp);
      // Use memmove-style copy (handles overlapping regions)
      block.AddOp(new StdMemCopyOp(srcAddr.Result, dstAddr.Result, bytesOp.Result));
    } else {
      // Shift left: copy from [index+1] forward, moving each one position left
      var oneConst = new StdConstI64Op(1);
      block.AddOp(oneConst);
      var srcIndex = new StdAddI64Op(index, oneConst.Result);
      block.AddOp(srcIndex);
      var srcOffset = new StdMulI64Op(srcIndex.Result, elemSizeConst.Result);
      block.AddOp(srcOffset);
      var srcAddr = new StdAddI64Op(buffer, srcOffset.Result);
      block.AddOp(srcAddr);
      var dstOffset = new StdMulI64Op(index, elemSizeConst.Result);
      block.AddOp(dstOffset);
      var dstAddr = new StdAddI64Op(buffer, dstOffset.Result);
      block.AddOp(dstAddr);
      var bytesOp = new StdMulI64Op(count, elemSizeConst.Result);
      block.AddOp(bytesOp);
      block.AddOp(new StdMemCopyOp(srcAddr.Result, dstAddr.Result, bytesOp.Result));
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
  };

}
