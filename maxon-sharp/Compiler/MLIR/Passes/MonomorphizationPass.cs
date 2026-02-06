using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Specializes generic type methods for concrete type aliases.
///
/// When a generic type like Vector has methods that use associated types (e.g., ElementArray),
/// calling those methods on a concrete alias (e.g., __Vector_3_i64) fails because the function
/// signature has placeholder types while the caller provides concrete types.
///
/// This pass:
/// 1. Identifies functions on generic types with associated-type params/returns
/// 2. For each concrete alias, clones the function with substituted types
/// 3. Rewrites call sites to use the specialized function
/// </summary>
public static class MonomorphizationPass {
  public static void Run(MlirModule<MaxonOp> module) {
    // Collect specializations needed: (sourceFunc, concreteAlias, typeSubstitution)
    var specializations = CollectNeededSpecializations(module);

    if (specializations.Count == 0) return;

    // Clone functions with type substitutions
    var newFunctions = new List<MlirFunction<MaxonOp>>();
    foreach (var spec in specializations) {
      var clonedFunc = CloneAndSpecialize(spec.SourceFunc, spec.ConcreteTypeName, spec.TypeSubstitution, module);
      newFunctions.Add(clonedFunc);
      Logger.Debug(LogCategory.Mlir, $"Monomorphized {spec.SourceFunc.Name} -> {clonedFunc.Name}");
    }

    // Add cloned functions to module
    foreach (var func in newFunctions) {
      module.Functions.Add(func);
    }

    // Rewrite call sites
    RewriteCallSites(module, specializations);
  }

  private record Specialization(
    MlirFunction<MaxonOp> SourceFunc,
    string SourceTypeName,
    string ConcreteTypeName,
    Dictionary<string, MlirType> TypeSubstitution);

  private static List<Specialization> CollectNeededSpecializations(MlirModule<MaxonOp> module) {
    var specializations = new List<Specialization>();

    // Find concrete type aliases that need specialization
    foreach (var (aliasName, aliasInfo) in module.TypeAliasSources) {
      // Skip if no type params (not a generic instantiation)
      if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) continue;

      var sourceTypeName = aliasInfo.SourceTypeName;

      // Find the source struct type to get associated type names
      if (!module.TypeDefs.TryGetValue(sourceTypeName, out var sourceType)) continue;
      if (sourceType is not MlirStructType sourceStruct) continue;
      if (sourceStruct.AssociatedTypeNames.Count == 0) continue;

      // Build type substitution map for this concrete alias
      // Maps associated type names and type-aliased structs to their concrete versions
      var typeSubstitution = BuildTypeSubstitution(sourceStruct, aliasInfo.TypeParams, aliasName, module);

      // Find methods on the source type that need specialization
      // Support both exact prefix match (Set.insert) and suffix match (stdlib.Set.insert)
      var sourcePrefix = $"{sourceTypeName}.";
      var sourceSuffix = $".{sourceTypeName}.";
      foreach (var func in module.Functions) {
        bool isSourceMethod = func.Name.StartsWith(sourcePrefix);
        bool isSuffixMatch = !isSourceMethod && func.Name.Contains(sourceSuffix);

        if (!isSourceMethod && !isSuffixMatch) continue;

        // Check if function uses associated types in params or return
        if (!NeedsSpecialization(func, sourceStruct, module)) continue;

        // Extract method name (last component after TypeName)
        string methodName;
        if (isSourceMethod) {
          methodName = func.Name[sourcePrefix.Length..];
        } else {
          // For suffix match like "stdlib.Set.insert", extract "insert"
          var idx = func.Name.LastIndexOf(sourceSuffix);
          methodName = func.Name[(idx + sourceSuffix.Length)..];
        }

        var specializedName = $"{aliasName}.{methodName}";
        if (module.Functions.Any(f => f.Name == specializedName)) continue;
        if (specializations.Any(s => s.ConcreteTypeName == aliasName && s.SourceFunc == func)) continue;

        specializations.Add(new Specialization(func, sourceTypeName, aliasName, typeSubstitution));
      }
    }

    return specializations;
  }

  private static Dictionary<string, MlirType> BuildTypeSubstitution(
    MlirStructType sourceStruct,
    Dictionary<string, MlirType> typeParams,
    string concreteAliasName,
    MlirModule<MaxonOp> module) {
    // Get or create the concrete alias type
    MlirType concreteAliasType = module.TypeDefs.TryGetValue(concreteAliasName, out var existing)
      ? existing
      : new MlirStructType(concreteAliasName, []);

    var substitution = new Dictionary<string, MlirType> {
      // Self/Vector -> __Vector_3_i64
      [sourceStruct.Name] = concreteAliasType,
      ["Self"] = concreteAliasType
    };

    // Look for typealias definitions in the source struct (e.g., ElementArray -> Array)
    // These are defined as fields with AssociatedType in the source struct
    foreach (var assocTypeName in sourceStruct.AssociatedTypeNames) {
      // The associated type name (e.g., "Element") maps to a concrete type
      if (typeParams.TryGetValue(assocTypeName, out var concreteType)) {
        substitution[assocTypeName] = concreteType;
      }
    }

    // Look for type aliases defined in source type (e.g., "ElementArray is Array with Element")
    // These are tracked in TypeAliasSources where source is the generic type's typealias
    foreach (var (_, candidateInfo) in module.TypeAliasSources) {
      // Skip if not a typealias of a generic struct with our source as the origin
      if (candidateInfo.SourceTypeName != sourceStruct.Name) continue;

      // Check if this typealias uses our type params
      if (candidateInfo.TypeParams != null) {
        // This is a user-defined alias with concrete params - not what we want
        continue;
      }
    }

    // Handle associated type aliases like "ElementArray is Array with Element"
    // These appear as MlirStructTypes with the associated type name and map to "Array"
    // when Element is substituted
    if (module.TypeDefs.TryGetValue(sourceStruct.Name, out var fullSourceDef) &&
      fullSourceDef is MlirStructType fullSourceStruct) {
      // Look for fields that reference associated types
      foreach (var field in fullSourceStruct.Fields) {
        if (field.Type is MlirStructType fieldStruct) {
          // If the field's type name is an associated type name
          if (sourceStruct.AssociatedTypeNames.Contains(fieldStruct.Name)) {
            // This field references an associated type
          }
        }
      }
    }

    // For "ElementArray" (which is "Array with Element"), substitute to "Array"
    // This requires knowing the typealias definitions from parsing
    // The struct type "ElementArray" should map to base struct "Array"
    foreach (var assocTypeName in sourceStruct.AssociatedTypeNames) {
      var aliasedTypeName = $"{assocTypeName}Array"; // Convention: ElementArray, ElementSet, etc.
      if (module.TypeDefs.TryGetValue(aliasedTypeName, out _)) {
        // Check if this is an alias of a generic struct instantiated with the associated type
        // e.g., ElementArray is Array with Element
        // Substitute ElementArray -> Array
        if (module.TypeDefs.TryGetValue("Array", out var arrayType)) {
          substitution[aliasedTypeName] = arrayType;
        }
      }
    }

    return substitution;
  }

  private static bool NeedsSpecialization(MlirFunction<MaxonOp> func, MlirStructType sourceStruct, MlirModule<MaxonOp> module) {
    // Check if function has Element-polymorphic params/return (tracked during parsing)
    if (module.ElementPolymorphicParams.TryGetValue(func.Name, out var elementParams)) {
      // If there are any element-polymorphic params (or return type at index -1), needs specialization
      if (elementParams.Count > 0) return true;
    }

    // Check if any param type is an associated type or alias thereof
    foreach (var paramType in func.ParamTypes) {
      if (IsAssociatedType(paramType, sourceStruct)) return true;
    }

    // Check return type
    if (func.ReturnType != null && IsAssociatedType(func.ReturnType, sourceStruct)) return true;

    return false;
  }

  private static bool IsAssociatedType(MlirType type, MlirStructType sourceStruct) {
    if (type is not MlirStructType st) return false;

    // Direct associated type (e.g., Element)
    if (sourceStruct.AssociatedTypeNames.Contains(st.Name)) return true;

    // Self type
    if (st.Name == sourceStruct.Name || st.Name == "Self") return true;

    // Type alias using associated types (e.g., ElementArray)
    // Check if this struct name contains an associated type name pattern
    foreach (var assocName in sourceStruct.AssociatedTypeNames) {
      if (st.Name.Contains(assocName)) return true;
    }

    return false;
  }

  private static MlirFunction<MaxonOp> CloneAndSpecialize(
    MlirFunction<MaxonOp> sourceFunc,
    string concreteTypeName,
    Dictionary<string, MlirType> typeSubstitution,
    MlirModule<MaxonOp> module) {

    var sourceTypeName = typeSubstitution.FirstOrDefault(kv => kv.Value.Name == concreteTypeName && kv.Key != "Self").Key
               ?? concreteTypeName;

    // Compute new function name
    // Use LastIndexOf to handle namespace-qualified names like "stdlib.Array.push"
    var dotIdx = sourceFunc.Name.LastIndexOf('.');
    var methodName = dotIdx >= 0 ? sourceFunc.Name[(dotIdx + 1)..] : sourceFunc.Name;
    var newFuncName = $"{concreteTypeName}.{methodName}";

    // Get Element-polymorphic params info for this function
    module.ElementPolymorphicParams.TryGetValue(sourceFunc.Name, out HashSet<int>? elementPolymorphicIndices);

    // Get the concrete Element type from the substitution
    MlirType? concreteElementType = typeSubstitution.GetValueOrDefault("Element");
    if (concreteElementType != null) {
      Logger.Debug(LogCategory.Mlir, $"  Element type substitution: Element -> {concreteElementType.Name}");
    } else {
      Logger.Debug(LogCategory.Mlir, $"  No Element type in substitution. TypeSubstitution keys: [{string.Join(", ", typeSubstitution.Keys)}]");
    }

    // Clone param types with substitution, handling Element-polymorphic params
    var newParamTypes = new List<MlirType>();
    for (int i = 0; i < sourceFunc.ParamTypes.Count; i++) {
      var paramType = sourceFunc.ParamTypes[i];
      // If this param is Element-polymorphic and we have a concrete Element type, use it
      if (elementPolymorphicIndices != null && elementPolymorphicIndices.Contains(i) && concreteElementType != null) {
        newParamTypes.Add(concreteElementType);
      } else {
        newParamTypes.Add(SubstituteType(paramType, typeSubstitution));
      }
    }

    // Clone return type with substitution, handling Element-polymorphic return
    MlirType? newReturnType;
    if (elementPolymorphicIndices != null && elementPolymorphicIndices.Contains(-1) && concreteElementType != null) {
      // Return type is Element-polymorphic
      newReturnType = concreteElementType;

    } else {
      newReturnType = sourceFunc.ReturnType != null
        ? SubstituteType(sourceFunc.ReturnType, typeSubstitution)
        : null;
    }

    var newFunc = new MlirFunction<MaxonOp>(
      newFuncName,
      [.. sourceFunc.ParamNames],
      newParamTypes,
      newReturnType,
      sourceFunc.ThrowsType) {
      IsStdlib = sourceFunc.IsStdlib,
      SourceLine = sourceFunc.SourceLine,
      SourceColumn = sourceFunc.SourceColumn
    };

    Logger.Debug(LogCategory.Mlir, $"  Monomorphized func: {newFuncName}({string.Join(", ", newParamTypes)}) -> {newReturnType}");

    // Map from old value IDs to new value IDs
    var valueMap = new Dictionary<int, MaxonValue>();

    // Track variable names that hold float values (Element-polymorphic substituted)
    var floatVars = new HashSet<string>();

    // Clone all blocks and operations
    var extraOps = new List<MaxonOp>();
    foreach (var block in sourceFunc.Body.Blocks) {
      var newBlock = newFunc.Body.AddBlock(block.Name);

      foreach (var op in block.Operations) {
        extraOps.Clear();
        var clonedOp = CloneOp(op, typeSubstitution, valueMap, floatVars, elementPolymorphicIndices, concreteElementType, extraOps);
        foreach (var extra in extraOps) {
          newBlock.AddOp(extra);
        }
        newBlock.AddOp(clonedOp);
      }
    }

    return newFunc;
  }

  private static MlirType SubstituteType(MlirType type, Dictionary<string, MlirType> substitution) {
    if (type is MlirStructType st) {
      if (substitution.TryGetValue(st.Name, out var newType)) {
        return newType;
      }
    }
    return type;
  }

  /// <summary>
  /// Substitutes Element-polymorphic value kinds based on the type substitution.
  /// If Element is substituted to f64, Integer becomes Float.
  /// If Element is substituted to i8, Integer becomes Byte.
  /// If Element is substituted to i1, Integer becomes Bool.
  /// </summary>
  private static MaxonValueKind SubstituteValueKind(MaxonValueKind kind, Dictionary<string, MlirType> substitution) {
    // Substitute Integer kind if Element is mapped to a different type
    if (kind == MaxonValueKind.Integer && substitution.TryGetValue("Element", out var elementType)) {
      if (elementType == MlirType.I64) return MaxonValueKind.Integer;
      if (elementType == MlirType.F64) return MaxonValueKind.Float;
      if (elementType == MlirType.I8) return MaxonValueKind.Byte;
      if (elementType == MlirType.I1) return MaxonValueKind.Bool;
      // Struct type means the element is a user-defined type, keep as Integer
      if (elementType is MlirStructType) return MaxonValueKind.Integer;
      throw new InvalidOperationException($"SubstituteValueKind: unsupported element type '{elementType.Name}'");
    }
    return kind;
  }

  private static MaxonOp CloneOp(
    MaxonOp op,
    Dictionary<string, MlirType> typeSubstitution,
    Dictionary<int, MaxonValue> valueMap,
    HashSet<string> floatVars,
    HashSet<int>? elementPolymorphicIndices,
    MlirType? concreteElementType,
    List<MaxonOp> extraOps) {

    // Determine if we're substituting Element to a float type
    bool substituteToFloat = concreteElementType == MlirType.F64;

    // Helper to check if a param index is Element-polymorphic
    bool IsElementPolymorphic(int paramIndex) =>
      elementPolymorphicIndices?.Contains(paramIndex) ?? false;

    // Helper to map old value to new value, preserving the mapped value's type
    MaxonValue MapValue(MaxonValue old) {
      if (valueMap.TryGetValue(old.Id, out var mapped)) {
        return mapped;
      }
      // Value hasn't been mapped yet - create a new value of the same type
      // (This shouldn't happen for well-formed functions since defs precede uses)
      var newId = MlirContext.Current.NextId();
      MaxonValue newVal = old switch {
        MaxonInteger => new MaxonInteger(newId),
        MaxonFloat => new MaxonFloat(newId),
        MaxonBool => new MaxonBool(newId),
        MaxonByte => new MaxonByte(newId),
        MaxonStruct s => new MaxonStruct(newId, SubstituteName(s.TypeName, typeSubstitution)),
        MaxonEnum e => new MaxonEnum(newId, e.TypeName),
        _ => throw new InvalidOperationException($"Unknown MaxonValue type: {old.GetType()}")
      };
      valueMap[old.Id] = newVal;
      return newVal;
    }

    // Helper to register a mapping from old to new result
    void RegisterResult(MaxonValue oldResult, MaxonValue newResult) {
      valueMap[oldResult.Id] = newResult;
    }

    string SubName(string name) => SubstituteName(name, typeSubstitution);

    switch (op) {
      case MaxonLiteralOp lit: {
        var cloned = lit.ValueKind switch {
          MaxonValueKind.Integer => new MaxonLiteralOp(lit.IntValue),
          MaxonValueKind.Float => new MaxonLiteralOp(lit.FloatValue),
          MaxonValueKind.Bool => new MaxonLiteralOp(lit.BoolValue),
          _ => throw new InvalidOperationException($"Unsupported literal kind: {lit.ValueKind}")
        };
        RegisterResult(lit.Result, cloned.Result);
        return cloned;
      }

      case MaxonAssignOp assign: {
        // If this assignment is for an Element-polymorphic value, substitute the kind
        var valueKind = assign.ValueKind;
        var mappedValue = MapValue(assign.Value);
        if (substituteToFloat && valueKind == MaxonValueKind.Integer) {
          // Check if the source value is Element-polymorphic (mapped from a param that was)
          if (mappedValue is MaxonFloat) {
            valueKind = MaxonValueKind.Float;
          }
        }
        // Track this variable as holding a float value
        if (valueKind == MaxonValueKind.Float) {
          floatVars.Add(assign.VarName);
        }
        return new MaxonAssignOp(assign.VarName, mappedValue, assign.IsDeclaration, assign.IsMutable, valueKind);
      }

      case MaxonParamOp param: {
        // Substitute ValueKind if this parameter is Element-polymorphic
        var valueKind = param.ValueKind;
        if (substituteToFloat && IsElementPolymorphic(param.Index)) {
          valueKind = MaxonValueKind.Float;
        }
        // Track float params so VarRefOps for this param name get correct kind
        if (valueKind == MaxonValueKind.Float) {
          floatVars.Add(param.Name);
        }
        var cloned = new MaxonParamOp(param.Index, param.Name, valueKind);
        RegisterResult(param.Result, cloned.Result);
        return cloned;
      }

      case MaxonStructParamOp structParam: {
        var cloned = new MaxonStructParamOp(structParam.Index, structParam.Name, SubName(structParam.StructTypeName));
        RegisterResult(structParam.Result, cloned.Result);
        return cloned;
      }

      case MaxonVarRefOp varRef: {
        // Substitute ValueKind if this variable was assigned a float value
        var valueKind = varRef.ValueKind;
        if (substituteToFloat && floatVars.Contains(varRef.VarName)) {
          valueKind = MaxonValueKind.Float;
        }
        var cloned = new MaxonVarRefOp(varRef.VarName, valueKind);
        RegisterResult(varRef.Result, cloned.Result);
        return cloned;
      }

      case MaxonStructVarRefOp structVarRef: {
        var cloned = new MaxonStructVarRefOp(structVarRef.VarName, SubName(structVarRef.StructTypeName));
        RegisterResult(structVarRef.Result, cloned.Result);
        return cloned;
      }

      case MaxonBinOp binOp: {
        var mappedLhs = MapValue(binOp.Lhs);
        var mappedRhs = MapValue(binOp.Rhs);
        // Substitute OperandKind if operands are floats
        var operandKind = binOp.OperandKind;
        if (substituteToFloat && operandKind == MaxonValueKind.Integer) {
          if (mappedLhs is MaxonFloat || mappedRhs is MaxonFloat) {
            operandKind = MaxonValueKind.Float;
          }
        }
        var cloned = new MaxonBinOp(binOp.Operator, mappedLhs, mappedRhs, operandKind);
        RegisterResult(binOp.Result, cloned.Result);
        return cloned;
      }

      case MaxonCallOp call: {
        var newCallee = SubstituteCallee(call.Callee, typeSubstitution);
        var newArgs = call.Args.Select(MapValue).ToList();
        var newResultStructTypeName = call.ResultStructTypeName != null ? SubName(call.ResultStructTypeName) : null;
        var cloned = new MaxonCallOp(newCallee, newArgs, call.ResultKind, newResultStructTypeName);
        if (call.Result != null && cloned.Result != null) {
          RegisterResult(call.Result, cloned.Result);
        }
        return cloned;
      }

      case MaxonTryCallOp tryCall: {
        var newCallee = SubstituteCallee(tryCall.Callee, typeSubstitution);
        var newArgs = tryCall.Args.Select(MapValue).ToList();
        var newResultStructTypeName = tryCall.ResultStructTypeName != null ? SubName(tryCall.ResultStructTypeName) : null;
        var cloned = new MaxonTryCallOp(newCallee, newArgs, tryCall.ResultKind, newResultStructTypeName);
        if (tryCall.Result != null && cloned.Result != null) {
          RegisterResult(tryCall.Result, cloned.Result);
        }
        RegisterResult(tryCall.ErrorFlag, cloned.ErrorFlag);
        return cloned;
      }

      case MaxonStructLiteralOp structLit: {
        var newFieldValues = structLit.FieldValues.Select(fv => (fv.FieldName, MapValue(fv.Value))).ToList();

        // For __ManagedMemory structs, substitute element_size based on the Element type substitution.
        // When parsing generic types like Set<Element>, ElementArray{} gets element_size=0 because
        // the Element type is unknown. During monomorphization, we need to patch this to the correct
        // element size based on the concrete Element type.
        if (structLit.TypeName == "__ManagedMemory" && concreteElementType != null) {
          // Find and update the element_size field
          for (int i = 0; i < newFieldValues.Count; i++) {
            if (newFieldValues[i].FieldName == "element_size") {
              // Determine the correct element size from the concrete Element type
              int elementSize = concreteElementType?.SizeInBytes ?? 8;
              // Create a new literal with the correct element size
              var elementSizeLitOp = new MaxonLiteralOp((long)elementSize);
              extraOps.Add(elementSizeLitOp);
              newFieldValues[i] = ("element_size", elementSizeLitOp.Result);
              break;
            }
          }
        }

        var cloned = new MaxonStructLiteralOp(SubName(structLit.TypeName), newFieldValues) {
          ArrayLiteralTag = structLit.ArrayLiteralTag,
          ArrayLiteralCount = structLit.ArrayLiteralCount
        };
        RegisterResult(structLit.Result, cloned.Result);
        return cloned;
      }

      case MaxonFieldAccessOp fieldAccess: {
        var cloned = new MaxonFieldAccessOp(
          MapValue(fieldAccess.StructValue),
          SubName(fieldAccess.TypeName),
          fieldAccess.FieldName,
          fieldAccess.ResultKind,
          fieldAccess.ResultStructTypeName != null ? SubName(fieldAccess.ResultStructTypeName) : null);
        RegisterResult(fieldAccess.Result, cloned.Result);
        return cloned;
      }

      case MaxonFieldAssignOp fieldAssign:
        return new MaxonFieldAssignOp(MapValue(fieldAssign.StructValue), SubName(fieldAssign.TypeName), fieldAssign.FieldName, MapValue(fieldAssign.NewValue));

      case MaxonCondBrOp condBr:
        return new MaxonCondBrOp(MapValue(condBr.Condition), condBr.ThenBlock, condBr.ElseBlock);

      case MaxonBrOp br:
        return new MaxonBrOp(br.Target);

      case MaxonReturnOp ret:
        return new MaxonReturnOp(ret.Value != null ? MapValue(ret.Value) : null, ret.IsErrorPropagation);

      case MaxonThrowOp throwOp:
        return new MaxonThrowOp(MapValue(throwOp.ErrorValue), throwOp.ErrorTypeName);

      case MaxonTruncOp truncOp: {
        var cloned = new MaxonTruncOp(MapValue(truncOp.Input));
        RegisterResult(truncOp.Result, cloned.Result);
        return cloned;
      }

      case MaxonIntToFloatOp itf: {
        var cloned = new MaxonIntToFloatOp(MapValue(itf.Input));
        RegisterResult(itf.Result, cloned.Result);
        return cloned;
      }

      case MaxonCastOp cast: {
        var cloned = new MaxonCastOp(MapValue(cast.Input), cast.TargetKind);
        RegisterResult(cast.Result, cloned.Result);
        return cloned;
      }

      case MaxonAbsOp abs: {
        var cloned = new MaxonAbsOp(MapValue(abs.Input));
        RegisterResult(abs.Result, cloned.Result);
        return cloned;
      }

      case MaxonSqrtOp sqrt: {
        var cloned = new MaxonSqrtOp(MapValue(sqrt.Input));
        RegisterResult(sqrt.Result, cloned.Result);
        return cloned;
      }

      case MaxonFloorOp floor: {
        var cloned = new MaxonFloorOp(MapValue(floor.Input));
        RegisterResult(floor.Result, cloned.Result);
        return cloned;
      }

      case MaxonCeilOp ceil: {
        var cloned = new MaxonCeilOp(MapValue(ceil.Input));
        RegisterResult(ceil.Result, cloned.Result);
        return cloned;
      }

      case MaxonRoundOp round: {
        var cloned = new MaxonRoundOp(MapValue(round.Input));
        RegisterResult(round.Result, cloned.Result);
        return cloned;
      }

      case MaxonMinOp min: {
        var cloned = new MaxonMinOp(MapValue(min.Lhs), MapValue(min.Rhs));
        RegisterResult(min.Result, cloned.Result);
        return cloned;
      }

      case MaxonMaxOp max: {
        var cloned = new MaxonMaxOp(MapValue(max.Lhs), MapValue(max.Rhs));
        RegisterResult(max.Result, cloned.Result);
        return cloned;
      }

      case MaxonEnumLiteralOp enumLit: {
        var cloned = enumLit.BackingKind == MaxonValueKind.Float
          ? new MaxonEnumLiteralOp(enumLit.EnumTypeName, enumLit.CaseName, enumLit.FloatValue)
          : new MaxonEnumLiteralOp(enumLit.EnumTypeName, enumLit.CaseName, enumLit.IntValue);
        RegisterResult(enumLit.Result, cloned.Result);
        return cloned;
      }

      case MaxonEnumParamOp enumParam: {
        var cloned = new MaxonEnumParamOp(enumParam.Index, enumParam.Name, enumParam.EnumTypeName, enumParam.BackingKind);
        RegisterResult(enumParam.Result, cloned.Result);
        return cloned;
      }

      case MaxonEnumVarRefOp enumVarRef: {
        var cloned = new MaxonEnumVarRefOp(enumVarRef.VarName, enumVarRef.EnumTypeName, enumVarRef.BackingKind);
        RegisterResult(enumVarRef.Result, cloned.Result);
        return cloned;
      }

      case MaxonEnumRawValueOp enumRaw: {
        var cloned = new MaxonEnumRawValueOp(MapValue(enumRaw.EnumValue), enumRaw.EnumTypeName, enumRaw.ResultKind);
        RegisterResult(enumRaw.Result, cloned.Result);
        return cloned;
      }

      case MaxonGlobalLoadOp globalLoad: {
        var cloned = new MaxonGlobalLoadOp(globalLoad.GlobalName, globalLoad.ValueKind);
        RegisterResult(globalLoad.Result, cloned.Result);
        return cloned;
      }

      case MaxonGlobalStoreOp globalStore:
        return new MaxonGlobalStoreOp(globalStore.GlobalName, MapValue(globalStore.Value), globalStore.ValueKind);

      case MaxonManagedMemGetOp memGet: {
        // Substitute result kind if Element type was substituted
        var resultKind = SubstituteValueKind(memGet.ResultKind, typeSubstitution);
        var cloned = new MaxonManagedMemGetOp(MapValue(memGet.ManagedStruct), MapValue(memGet.Index), resultKind);
        RegisterResult(memGet.Result, cloned.Result);
        return cloned;
      }

      case MaxonManagedMemSetOp memSet: {
        // Substitute element kind if Element type was substituted
        var elementKind = SubstituteValueKind(memSet.ElementKind, typeSubstitution);
        var mappedValue = MapValue(memSet.Value);
        return new MaxonManagedMemSetOp(MapValue(memSet.ManagedStruct), MapValue(memSet.Index), mappedValue, elementKind);
      }

      case MaxonManagedMemCreateOp memCreate: {
        var cloned = new MaxonManagedMemCreateOp(MapValue(memCreate.Count), memCreate.ElementSize);
        RegisterResult(memCreate.Result, cloned.Result);
        return cloned;
      }

      case MaxonManagedMemGrowOp memGrow:
        return new MaxonManagedMemGrowOp(MapValue(memGrow.ManagedStruct), MapValue(memGrow.NewCapacity));

      case MaxonManagedMemShiftOp memShift:
        return new MaxonManagedMemShiftOp(MapValue(memShift.ManagedStruct), MapValue(memShift.Index), MapValue(memShift.Count), memShift.ShiftRight);

      default:
        throw new InvalidOperationException($"Monomorphization: unhandled op type {op.GetType().Name}");
    }
  }

  private static string SubstituteName(string name, Dictionary<string, MlirType> substitution) {
    return substitution.TryGetValue(name, out var newType) ? newType.Name : name;
  }

  private static string SubstituteCallee(string callee, Dictionary<string, MlirType> substitution) {
    // Check if callee is TypeName.MethodName where TypeName needs substitution
    var dotIdx = callee.LastIndexOf('.');
    if (dotIdx > 0) {
      var typePart = callee[..dotIdx];
      var methodPart = callee[(dotIdx + 1)..];
      if (substitution.TryGetValue(typePart, out var newType)) {
        return $"{newType.Name}.{methodPart}";
      }
    }
    return callee;
  }

  /// <summary>
  /// Determines the concrete type name for a call site by checking the result struct type
  /// or the self argument (first arg) struct type.
  /// </summary>
  private static string? GetConcreteTypeForCall(string? resultStructTypeName, List<MaxonValue> args) {
    if (resultStructTypeName != null) return resultStructTypeName;
    // For void methods (e.g., set), use the self argument's struct type
    if (args.Count > 0 && args[0] is MaxonStruct selfStruct) return selfStruct.TypeName;
    return null;
  }

  private static void RewriteCallSites(MlirModule<MaxonOp> module, List<Specialization> specializations) {
    // Build a map of (original callee, concrete type) -> new callee
    var calleeMap = new Dictionary<(string, string), string>();
    foreach (var spec in specializations) {
      var dotIdx = spec.SourceFunc.Name.LastIndexOf('.');
      var methodName = dotIdx >= 0 ? spec.SourceFunc.Name[(dotIdx + 1)..] : spec.SourceFunc.Name;
      var newCallee = $"{spec.ConcreteTypeName}.{methodName}";
      calleeMap[(spec.SourceFunc.Name, spec.ConcreteTypeName)] = newCallee;
    }

    // For each function, scan call sites and rewrite if needed
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          var op = block.Operations[i];

          if (op is MaxonCallOp call) {
            var concreteType = GetConcreteTypeForCall(call.ResultStructTypeName, call.Args);
            if (concreteType != null) {
              var key = (call.Callee, concreteType);
              if (calleeMap.TryGetValue(key, out var newCallee)) {
                Logger.Debug(LogCategory.Mlir, $"  Rewrote call {call.Callee} -> {newCallee} in {func.Name}");
                block.Operations[i] = new MaxonCallOp(newCallee, call.Args, call.Result, call.ResultKind, call.ResultStructTypeName);
              }
            }
          } else if (op is MaxonTryCallOp tryCall) {
            var concreteType = GetConcreteTypeForCall(tryCall.ResultStructTypeName, tryCall.Args);
            if (concreteType != null) {
              var key = (tryCall.Callee, concreteType);
              if (calleeMap.TryGetValue(key, out var newCallee)) {
                Logger.Debug(LogCategory.Mlir, $"  Rewrote try_call {tryCall.Callee} -> {newCallee} in {func.Name}");
                block.Operations[i] = new MaxonTryCallOp(newCallee, tryCall.Args, tryCall.Result, tryCall.ErrorFlag, tryCall.ResultKind, tryCall.ResultStructTypeName);
              }
            }
          }
        }
      }
    }
  }
}
