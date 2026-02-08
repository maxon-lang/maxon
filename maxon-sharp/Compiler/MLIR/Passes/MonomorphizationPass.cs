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
      var clonedFunc = CloneAndSpecialize(spec.SourceFunc, spec.ConcreteTypeName, spec.TypeSubstitution);
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

      // Extension typealiases like "ElementArray = Array with Element" have unresolved
      // type params — they aren't concrete instantiations and can't be specialized
      if (aliasInfo.TypeParams.Values.Any(t => t is MlirTypeParameterType)) continue;

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
        if (!NeedsSpecialization(func, sourceStruct)) continue;

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

    // Map associated type names (from uses clause) to concrete types
    foreach (var assocTypeName in sourceStruct.AssociatedTypeNames) {
      if (typeParams.TryGetValue(assocTypeName, out var concreteType)) {
        substitution[assocTypeName] = concreteType;
      }
    }

    // Resolve conformance-bound type params (e.g., Element = Entry for Map : Iterable with Entry).
    // These are in the source struct's TypeParams but not in the concrete alias's typeParams.
    // Resolve them through inner alias chain (Entry -> Pair with (Key, Value) -> StringIntPair).
    foreach (var (paramName, paramValue) in sourceStruct.TypeParams) {
      if (substitution.ContainsKey(paramName)) continue;
      if (paramValue is MlirStructType innerAlias && module.TypeAliasSources.TryGetValue(innerAlias.Name, out TypeAliasInfo? innerInfo)) {
        if (innerInfo.TypeParams != null) {
          var resolvedInnerParams = new Dictionary<string, MlirType>();
          bool allResolved = true;
          foreach (var (ipn, ipt) in innerInfo.TypeParams) {
            if (ipt is MlirTypeParameterType tp && substitution.TryGetValue(tp.ParameterName, out var resolved))
              resolvedInnerParams[ipn] = resolved;
            else if (ipt is not MlirTypeParameterType)
              resolvedInnerParams[ipn] = ipt;
            else { allResolved = false; break; }
          }
          if (allResolved) {
            var concreteType = FindConcreteAlias(module, innerInfo.SourceTypeName, resolvedInnerParams);
            if (concreteType != null) {
              substitution[paramName] = concreteType;
              substitution[innerAlias.Name] = concreteType;
            }
          }
        }
      }
    }

    // Resolve inner type aliases to their concrete aliases.
    // For Map<Key=String, Value=i64>, "KeyArray = Array with Key" resolves to StringArray,
    // "ValueArray = Array with Value" resolves to IntArray, etc.
    // Uses the full substitution map (includes conformance params like Element -> StringIntPair).
    foreach (var (innerAliasName, aliasInfo) in module.TypeAliasSources) {
      if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) continue;
      bool hasOurTypeParams = aliasInfo.TypeParams.Values.Any(t =>
        t is MlirTypeParameterType tp && substitution.ContainsKey(tp.ParameterName));
      if (!hasOurTypeParams) continue;

      // Resolve the inner alias's type params through the substitution map
      var resolvedParams = new Dictionary<string, MlirType>();
      foreach (var (paramName, paramType) in aliasInfo.TypeParams) {
        if (paramType is MlirTypeParameterType tp && substitution.TryGetValue(tp.ParameterName, out var resolved)) {
          // Resolved type may itself be a type alias — use the concrete type from the registry
          if (resolved is MlirStructType resolvedSt && substitution.TryGetValue(resolvedSt.Name, out var deepResolved)) {
            resolvedParams[paramName] = deepResolved;
          } else {
            resolvedParams[paramName] = resolved;
          }
        } else {
          resolvedParams[paramName] = paramType;
        }
      }

      var concreteInnerType = FindConcreteAlias(module, aliasInfo.SourceTypeName, resolvedParams);
      if (concreteInnerType != null) {
        substitution[innerAliasName] = concreteInnerType;
      }
    }

    return substitution;
  }

  /// Searches TypeAliasSources for a concrete alias whose source type matches and whose
  /// type params match the resolved params exactly. Returns the type definition or null.
  private static MlirType? FindConcreteAlias(
      MlirModule<MaxonOp> module,
      string sourceTypeName,
      Dictionary<string, MlirType> resolvedParams) {
    foreach (var (candidateName, candidateInfo) in module.TypeAliasSources) {
      if (candidateInfo.SourceTypeName != sourceTypeName) continue;
      if (candidateInfo.TypeParams == null) continue;
      if (candidateInfo.TypeParams.Values.Any(t => t is MlirTypeParameterType)) continue;
      if (candidateInfo.TypeParams.Count != resolvedParams.Count) continue;
      bool match = true;
      foreach (var (pn, pt) in resolvedParams) {
        if (!candidateInfo.TypeParams.TryGetValue(pn, out var ct) || ct.Name != pt.Name) { match = false; break; }
      }
      if (match && module.TypeDefs.TryGetValue(candidateName, out var candidateType))
        return candidateType;
    }
    return null;
  }

  private static bool NeedsSpecialization(MlirFunction<MaxonOp> func, MlirStructType sourceStruct) {
    // Check if any param type is a type parameter
    foreach (var paramType in func.ParamTypes) {
      if (paramType is MlirTypeParameterType) return true;
      if (IsAssociatedType(paramType, sourceStruct)) return true;
    }

    // Check return type
    if (func.ReturnType is MlirTypeParameterType) return true;
    if (func.ReturnType != null && IsAssociatedType(func.ReturnType, sourceStruct)) return true;

    return false;
  }

  private static bool IsAssociatedType(MlirType type, MlirStructType sourceStruct) {
    if (type is not MlirStructType st) return false;

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
    Dictionary<string, MlirType> typeSubstitution) {

    var sourceTypeName = typeSubstitution.FirstOrDefault(kv => kv.Value.Name == concreteTypeName && kv.Key != "Self").Key
               ?? concreteTypeName;

    // Compute new function name
    // Use LastIndexOf to handle namespace-qualified names like "stdlib.Array.push"
    var dotIdx = sourceFunc.Name.LastIndexOf('.');
    var methodName = dotIdx >= 0 ? sourceFunc.Name[(dotIdx + 1)..] : sourceFunc.Name;
    var newFuncName = $"{concreteTypeName}.{methodName}";

    // Derive element-polymorphic param indices from function signature types
    var elementPolymorphicIndices = new HashSet<int>();
    for (int i = 0; i < sourceFunc.ParamTypes.Count; i++) {
      if (sourceFunc.ParamTypes[i] is MlirTypeParameterType) {
        elementPolymorphicIndices.Add(i);
      }
    }

    // Get the concrete Element type from the substitution
    MlirType? concreteElementType = typeSubstitution.GetValueOrDefault("Element");
    if (concreteElementType != null) {
      Logger.Debug(LogCategory.Mlir, $"  Element type substitution: Element -> {concreteElementType.Name}");
    } else {
      Logger.Debug(LogCategory.Mlir, $"  No Element type in substitution. TypeSubstitution keys: [{string.Join(", ", typeSubstitution.Keys)}]");
    }

    // Clone param types with substitution
    var newParamTypes = new List<MlirType>();
    for (int i = 0; i < sourceFunc.ParamTypes.Count; i++) {
      var paramType = sourceFunc.ParamTypes[i];
      if (paramType is MlirTypeParameterType tp
          && typeSubstitution.TryGetValue(tp.ParameterName, out var concreteType)) {
        newParamTypes.Add(concreteType);
      } else {
        newParamTypes.Add(SubstituteType(paramType, typeSubstitution));
      }
    }

    // Clone return type with substitution
    MlirType? newReturnType;
    if (sourceFunc.ReturnType is MlirTypeParameterType retTp
        && typeSubstitution.TryGetValue(retTp.ParameterName, out var concreteRetType)) {
      newReturnType = concreteRetType;
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

    // For multi-parameter generics (Map<Key, Value>), track which type parameter
    // each variable corresponds to, so we can resolve TypeParameter kinds correctly.
    // e.g., param "key" has type Key, so varTypeParams["key"] = "Key"
    var varTypeParams = new Dictionary<string, string>();
    for (int i = 0; i < sourceFunc.ParamTypes.Count; i++) {
      if (sourceFunc.ParamTypes[i] is MlirTypeParameterType tp
          && i < sourceFunc.ParamNames.Count) {
        varTypeParams[sourceFunc.ParamNames[i]] = tp.ParameterName;
      }
    }

    // Track which type param names resolve to struct types
    var structTypeParams = new HashSet<string>();
    foreach (var (paramName, concreteType) in typeSubstitution) {
      if (concreteType is MlirStructType && paramName != "Self"
          && !paramName.EndsWith("Array") && paramName != "Entry") {
        structTypeParams.Add(paramName);
      }
    }

    // Track variables that hold struct values (populated during cloning)
    // Maps var name → struct type name (e.g., "__try_result_13" → "String")
    var structVars = new Dictionary<string, string>();
    // Seed from params that resolve to struct types
    foreach (var (varName, typeParamName) in varTypeParams) {
      if (structTypeParams.Contains(typeParamName)
          && typeSubstitution.TryGetValue(typeParamName, out var ct) && ct is MlirStructType st) {
        structVars[varName] = st.Name;
      }
    }

    // Clone all blocks and operations
    var extraOps = new List<MaxonOp>();
    foreach (var block in sourceFunc.Body.Blocks) {
      var newBlock = newFunc.Body.AddBlock(block.Name);

      foreach (var op in block.Operations) {
        extraOps.Clear();
        var clonedOp = CloneOp(op, typeSubstitution, valueMap, floatVars, elementPolymorphicIndices, concreteElementType, extraOps, varTypeParams, structTypeParams, structVars);
        foreach (var extra in extraOps) {
          newBlock.AddOp(extra);
        }
        newBlock.AddOp(clonedOp);
      }
    }

    // Post-processing: fix __ManagedMemory element_size for multi-parameter generic types.
    // For types like Map<Key, Value>, the inner arrays (KeyArray, ValueArray) each need
    // different element sizes. The monomorphized struct literals reference __ManagedMemory
    // with element_size=0 (unresolved at parse time). Fix them by finding the wrapper struct
    // that consumes each __ManagedMemory and determining element size from its resolved type.
    if (concreteElementType == null && typeSubstitution.Count > 2) {
      PatchManagedMemoryElementSizes(newFunc, typeSubstitution);
    }

    return newFunc;
  }

  private static MlirType SubstituteType(MlirType type, Dictionary<string, MlirType> substitution) {
    if (type is MlirTypeParameterType tp) {
      return substitution.TryGetValue(tp.ParameterName, out var newType) ? newType : type;
    }
    if (type is MlirStructType st) {
      if (substitution.TryGetValue(st.Name, out var newType)) {
        return newType;
      }
    }
    if (type is MlirFunctionType ft) {
      var newParams = ft.ParameterTypes.Select(p => SubstituteType(p, substitution)).ToList();
      var newReturn = ft.ReturnType != null ? SubstituteType(ft.ReturnType, substitution) : null;
      if (!newParams.SequenceEqual(ft.ParameterTypes) || newReturn != ft.ReturnType) {
        return new MlirFunctionType(newParams, newReturn);
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
  /// <summary>
  /// Resolves TypeParameter kinds to concrete kinds based on the Element type substitution.
  /// Struct and Enum element types resolve to Integer because they are stored as i64 pointers
  /// in managed memory and passed as scalars through generic function bodies.
  /// </summary>
  private static MaxonValueKind SubstituteValueKind(MaxonValueKind kind, Dictionary<string, MlirType> substitution, string? typeParamName = null) {
    if (kind != MaxonValueKind.TypeParameter) return kind;

    // Use the specific type param name if provided (e.g., "Key", "Value"), otherwise fall back to "Element"
    var paramKey = typeParamName ?? "Element";
    if (!substitution.TryGetValue(paramKey, out var concreteType)) return kind;

    return concreteType switch {
      { } t when t == MlirType.I64 => MaxonValueKind.Integer,
      { } t when t == MlirType.F64 => MaxonValueKind.Float,
      { } t when t == MlirType.I1 => MaxonValueKind.Bool,
      { } t when t == MlirType.I8 => MaxonValueKind.Byte,
      MlirStructType or MlirEnumType => MaxonValueKind.Integer,
      MlirTypeParameterType => MaxonValueKind.TypeParameter,
      _ => throw new InvalidOperationException($"SubstituteValueKind: unsupported type '{concreteType}' for param '{paramKey}'")
    };
  }

  private static MaxonOp CloneOp(
    MaxonOp op,
    Dictionary<string, MlirType> typeSubstitution,
    Dictionary<int, MaxonValue> valueMap,
    HashSet<string> floatVars,
    HashSet<int>? elementPolymorphicIndices,
    MlirType? concreteElementType,
    List<MaxonOp> extraOps,
    Dictionary<string, string>? varTypeParams = null,
    HashSet<string>? structTypeParams = null,
    Dictionary<string, string>? structVars = null) {

    // Determine if we're substituting Element to a float type
    bool substituteToFloat = concreteElementType == MlirType.F64;

    // Helper: resolve a variable's type param name and check if it maps to a struct
    bool IsStructTypeParam(string? typeParamName) {
      if (typeParamName == null || structTypeParams == null) return false;
      return structTypeParams.Contains(typeParamName);
    }

    string? GetVarTypeParam(string varName) {
      return varTypeParams?.GetValueOrDefault(varName);
    }

    string? GetStructTypeName(string typeParamName) {
      if (typeSubstitution.TryGetValue(typeParamName, out var t) && t is MlirStructType st)
        return st.Name;
      return null;
    }

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
        MaxonFunctionPtr => new MaxonFunctionPtr(newId),
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
        var valueKind = assign.ValueKind;
        var mappedValue = MapValue(assign.Value);
        if (valueKind == MaxonValueKind.TypeParameter) {
          // Check if the mapped value is a struct (from a struct-resolving type param)
          if (mappedValue is MaxonStruct ms) {
            // Track this variable as holding a struct value
            structVars?.TryAdd(assign.VarName, ms.TypeName);
            return new MaxonAssignOp(assign.VarName, mappedValue, assign.IsDeclaration, assign.IsMutable, MaxonValueKind.Struct);
          }
          valueKind = substituteToFloat && mappedValue is MaxonFloat
            ? MaxonValueKind.Float
            : SubstituteValueKind(valueKind, typeSubstitution);
        }
        if (valueKind == MaxonValueKind.Float) {
          floatVars.Add(assign.VarName);
        }
        return new MaxonAssignOp(assign.VarName, mappedValue, assign.IsDeclaration, assign.IsMutable, valueKind);
      }

      case MaxonParamOp param: {
        // Check if this TypeParameter param resolves to a struct type
        var paramTypeParam = GetVarTypeParam(param.Name);
        if (param.ValueKind == MaxonValueKind.TypeParameter && IsStructTypeParam(paramTypeParam)) {
          var structTypeName = GetStructTypeName(paramTypeParam!);
          var cloned = new MaxonStructParamOp(param.Index, param.Name, structTypeName!);
          RegisterResult(param.Result, cloned.Result);
          return cloned;
        }
        var valueKind = SubstituteValueKind(param.ValueKind, typeSubstitution, paramTypeParam);
        if (substituteToFloat && IsElementPolymorphic(param.Index)) {
          valueKind = MaxonValueKind.Float;
        }
        if (valueKind == MaxonValueKind.Float) {
          floatVars.Add(param.Name);
        }
        var scalarParam = new MaxonParamOp(param.Index, param.Name, valueKind);
        RegisterResult(param.Result, scalarParam.Result);
        return scalarParam;
      }

      case MaxonStructParamOp structParam: {
        var cloned = new MaxonStructParamOp(structParam.Index, structParam.Name, SubName(structParam.StructTypeName));
        RegisterResult(structParam.Result, cloned.Result);
        return cloned;
      }

      case MaxonVarRefOp varRef: {
        // Check if this variable holds a struct value (from type param → struct resolution)
        if (varRef.ValueKind == MaxonValueKind.TypeParameter
            && structVars != null && structVars.TryGetValue(varRef.VarName, out var svTypeName)) {
          var cloned = new MaxonStructVarRefOp(varRef.VarName, svTypeName);
          RegisterResult(varRef.Result, cloned.Result);
          return cloned;
        }
        var varTp = GetVarTypeParam(varRef.VarName);
        if (varRef.ValueKind == MaxonValueKind.TypeParameter && IsStructTypeParam(varTp)) {
          var structTypeName = GetStructTypeName(varTp!);
          var cloned = new MaxonStructVarRefOp(varRef.VarName, structTypeName!);
          RegisterResult(varRef.Result, cloned.Result);
          return cloned;
        }
        var valueKind = SubstituteValueKind(varRef.ValueKind, typeSubstitution, varTp);
        if (substituteToFloat && floatVars.Contains(varRef.VarName)) {
          valueKind = MaxonValueKind.Float;
        }
        var scalarRef = new MaxonVarRefOp(varRef.VarName, valueKind);
        RegisterResult(varRef.Result, scalarRef.Result);
        return scalarRef;
      }

      case MaxonStructVarRefOp structVarRef: {
        var cloned = new MaxonStructVarRefOp(structVarRef.VarName, SubName(structVarRef.StructTypeName));
        RegisterResult(structVarRef.Result, cloned.Result);
        return cloned;
      }

      case MaxonBinOp binOp: {
        var mappedLhs = MapValue(binOp.Lhs);
        var mappedRhs = MapValue(binOp.Rhs);
        // For Eq/Ne where operands resolved to structs, convert to equals() method call.
        // Check mappedLhs type rather than OperandKind because SubstituteValueKind maps
        // struct types to Integer, so the original TypeParameter kind is lost by this point.
        if (binOp.Operator is MaxonBinOperator.Eq or MaxonBinOperator.Ne
            && mappedLhs is MaxonStruct lhsStruct) {
          var equalsCallee = $"{lhsStruct.TypeName}.equals";
          var callOp = new MaxonCallOp(equalsCallee, [mappedLhs, mappedRhs], MaxonValueKind.Bool, null);
          if (binOp.Operator == MaxonBinOperator.Ne) {
            extraOps.Add(callOp);
            var trueOp = new MaxonLiteralOp(true);
            extraOps.Add(trueOp);
            var xorOp = new MaxonBinOp(MaxonBinOperator.BitXor, callOp.Result!, trueOp.Result, MaxonValueKind.Bool);
            RegisterResult(binOp.Result, xorOp.Result);
            return xorOp;
          }
          RegisterResult(binOp.Result, callOp.Result!);
          return callOp;
        }
        var operandKind = SubstituteValueKind(binOp.OperandKind, typeSubstitution);
        if (substituteToFloat && (mappedLhs is MaxonFloat || mappedRhs is MaxonFloat)) {
          operandKind = MaxonValueKind.Float;
        }
        var cloned = new MaxonBinOp(binOp.Operator, mappedLhs, mappedRhs, operandKind);
        RegisterResult(binOp.Result, cloned.Result);
        return cloned;
      }

      case MaxonCallOp call: {
        var newCallee = SubstituteCallee(call.Callee, typeSubstitution);
        var newArgs = call.Args.Select(MapValue).ToList();
        var newResultStructTypeName = call.ResultStructTypeName != null ? SubName(call.ResultStructTypeName) : null;
        var resultKind = call.ResultKind.HasValue ? SubstituteValueKind(call.ResultKind.Value, typeSubstitution) : call.ResultKind;
        if (resultKind == MaxonValueKind.Struct && newResultStructTypeName == null && concreteElementType is MlirStructType callStructType)
          newResultStructTypeName = callStructType.Name;
        if (resultKind == MaxonValueKind.Enum && newResultStructTypeName == null && concreteElementType is MlirEnumType callEnumType)
          newResultStructTypeName = callEnumType.Name;
        // For TypeParameter results, resolve via the self arg's concrete inner alias type
        ResolveTypeParameterResult(call.ResultKind, newArgs, typeSubstitution, ref resultKind, ref newResultStructTypeName);
        var cloned = new MaxonCallOp(newCallee, newArgs, resultKind, newResultStructTypeName);
        if (call.Result != null && cloned.Result != null) {
          RegisterResult(call.Result, cloned.Result);
        }
        return cloned;
      }

      case MaxonTryCallOp tryCall: {
        var newCallee = SubstituteCallee(tryCall.Callee, typeSubstitution);
        var newArgs = tryCall.Args.Select(MapValue).ToList();
        var newResultStructTypeName = tryCall.ResultStructTypeName != null ? SubName(tryCall.ResultStructTypeName) : null;
        var resultKind = tryCall.ResultKind.HasValue ? SubstituteValueKind(tryCall.ResultKind.Value, typeSubstitution) : tryCall.ResultKind;
        if (resultKind == MaxonValueKind.Struct && newResultStructTypeName == null && concreteElementType is MlirStructType tryStructType)
          newResultStructTypeName = tryStructType.Name;
        if (resultKind == MaxonValueKind.Enum && newResultStructTypeName == null && concreteElementType is MlirEnumType tryEnumType)
          newResultStructTypeName = tryEnumType.Name;
        // For TypeParameter results, resolve via the self arg's concrete inner alias type
        ResolveTypeParameterResult(tryCall.ResultKind, newArgs, typeSubstitution, ref resultKind, ref newResultStructTypeName);
        var cloned = new MaxonTryCallOp(newCallee, newArgs, resultKind, newResultStructTypeName);
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
        // Substitute result kind using the specific type param name (Key, Value, or Element)
        var resultKind = SubstituteValueKind(memGet.ResultKind, typeSubstitution, memGet.TypeParamName);
        var paramKey = memGet.TypeParamName ?? "Element";
        var isStructElem = typeSubstitution.TryGetValue(paramKey, out var getElemType) && getElemType is MlirStructType;
        var cloned = new MaxonManagedMemGetOp(MapValue(memGet.ManagedStruct), MapValue(memGet.Index), resultKind) {
          IsStructElement = isStructElem,
          TypeParamName = memGet.TypeParamName
        };
        RegisterResult(memGet.Result, cloned.Result);
        return cloned;
      }

      case MaxonManagedMemSetOp memSet: {
        // Substitute element kind if Element type was substituted
        var elementKind = SubstituteValueKind(memSet.ElementKind, typeSubstitution);
        var isStructElem = typeSubstitution.TryGetValue("Element", out var setElemType) && setElemType is MlirStructType;
        var mappedValue = MapValue(memSet.Value);
        return new MaxonManagedMemSetOp(MapValue(memSet.ManagedStruct), MapValue(memSet.Index), mappedValue, elementKind) { IsStructElement = isStructElem };
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

      case MaxonManagedMemConcatOp memConcat: {
        var cloned = new MaxonManagedMemConcatOp(MapValue(memConcat.Lhs), MapValue(memConcat.Rhs));
        RegisterResult(memConcat.Result, cloned.Result);
        return cloned;
      }

      case MaxonManagedMemSliceOp memSlice: {
        var cloned = new MaxonManagedMemSliceOp(MapValue(memSlice.Managed), MapValue(memSlice.Start), MapValue(memSlice.End));
        RegisterResult(memSlice.Result, cloned.Result);
        return cloned;
      }

      case MaxonCallRuntimeOp callRtOp: {
        var newArgs = callRtOp.Args.Select(a => MapValue(a)).ToList();
        var cloned = new MaxonCallRuntimeOp(callRtOp.FunctionName, newArgs, callRtOp.Result != null);
        if (callRtOp.Result != null && cloned.Result != null)
          RegisterResult(callRtOp.Result, cloned.Result);
        return cloned;
      }

      case MaxonFunctionParamOp funcParam: {
        var cloned = new MaxonFunctionParamOp(funcParam.Index, funcParam.Name, funcParam.FunctionType);
        RegisterResult(funcParam.Result, cloned.Result);
        return cloned;
      }

      case MaxonFunctionRefOp funcRef: {
        var cloned = new MaxonFunctionRefOp(funcRef.FunctionName, funcRef.FunctionType);
        RegisterResult(funcRef.Result, cloned.Result);
        return cloned;
      }

      case MaxonFunctionVarRefOp funcVarRef: {
        var newFuncType = (MlirFunctionType)SubstituteType(funcVarRef.FunctionType, typeSubstitution);
        var cloned = new MaxonFunctionVarRefOp(funcVarRef.VarName, newFuncType);
        RegisterResult(funcVarRef.Result, cloned.Result);
        return cloned;
      }

      case MaxonIndirectCallOp indirectCall: {
        var newCallee = MapValue(indirectCall.Callee);
        var newArgs = indirectCall.Args.Select(a => MapValue(a)).ToList();
        var resultKind = indirectCall.ResultKind.HasValue
          ? SubstituteValueKind(indirectCall.ResultKind.Value, typeSubstitution)
          : (MaxonValueKind?)null;
        var newCalleeType = (MlirFunctionType)SubstituteType(indirectCall.CalleeType, typeSubstitution);
        var newResultStructTypeName = indirectCall.ResultStructTypeName != null ? SubName(indirectCall.ResultStructTypeName) : null;
        var cloned = new MaxonIndirectCallOp(newCallee, newCalleeType, newArgs, resultKind, newResultStructTypeName);
        if (indirectCall.Result != null && cloned.Result != null)
          RegisterResult(indirectCall.Result, cloned.Result);
        return cloned;
      }

      default:
        throw new InvalidOperationException($"Monomorphization: unhandled op type {op.GetType().Name}");
    }
  }

  private static string SubstituteName(string name, Dictionary<string, MlirType> substitution) {
    return substitution.TryGetValue(name, out var newType) ? newType.Name : name;
  }

  /// When a call returns TypeParameter and the self arg is a concrete inner alias (e.g., StringArray),
  /// resolve the Element type to determine if the result should be Struct.
  /// For example: Array.get on StringArray returns String (a struct).
  private static void ResolveTypeParameterResult(
      MaxonValueKind? originalKind, List<MaxonValue> newArgs,
      Dictionary<string, MlirType> typeSubstitution,
      ref MaxonValueKind? resultKind, ref string? resultStructTypeName) {
    if (originalKind != MaxonValueKind.TypeParameter) return;
    if (newArgs.Count == 0) return;
    if (newArgs[0] is not MaxonStruct selfStruct) return;

    // Find the concrete alias in the substitution values
    foreach (var (key, concreteType) in typeSubstitution) {
      if (concreteType is MlirStructType st && st.Name == selfStruct.TypeName) {
        // Check if this alias has an Element type param that resolves to a struct
        if (st.TypeParams != null && st.TypeParams.TryGetValue("Element", out var elemType)) {
          if (elemType is MlirStructType elemStruct) {
            resultKind = MaxonValueKind.Struct;
            resultStructTypeName = elemStruct.Name;
          } else if (elemType is MlirEnumType elemEnum) {
            resultKind = MaxonValueKind.Enum;
            resultStructTypeName = elemEnum.Name;
          }
        }
        break;
      }
    }
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
  /// Resolve a callee rewrite by trying the result struct type first, then the self arg type.
  /// For instance methods like Array.get that return an element type (e.g., Pair), the result
  /// type won't match the container type alias (__Array_Pair), so we fall back to the self arg.
  /// </summary>
  private static string? ResolveCalleeRewrite(string callee, string? resultStructTypeName, List<MaxonValue> args, Dictionary<(string, string), string> calleeMap) {
    // Try result struct type first (works for most cases)
    if (resultStructTypeName != null) {
      var key = (callee, resultStructTypeName);
      if (calleeMap.TryGetValue(key, out var newCallee)) return newCallee;
    }
    // Fall back to self arg type (for methods that return element type, not container type)
    if (args.Count > 0 && args[0] is MaxonStruct selfStruct) {
      var key = (callee, selfStruct.TypeName);
      if (calleeMap.TryGetValue(key, out var newCallee)) return newCallee;
    }
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

    // Build function lookup for resolving monomorphized return types
    var funcLookup = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var f in module.Functions) {
      funcLookup[f.Name] = f;
    }

    // For each function, scan call sites and rewrite if needed
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          var op = block.Operations[i];

          if (op is MaxonCallOp call) {
            var newCallee = ResolveCalleeRewrite(call.Callee, call.ResultStructTypeName, call.Args, calleeMap);
            if (newCallee != null) {
              Logger.Debug(LogCategory.Mlir, $"  Rewrote call {call.Callee} -> {newCallee} in {func.Name}");
              var (newResultKind, newResultStructTypeName) = ResolveMonomorphizedResultType(
                call.ResultKind, call.ResultStructTypeName, newCallee, funcLookup);
              block.Operations[i] = new MaxonCallOp(newCallee, call.Args, call.Result, newResultKind, newResultStructTypeName);
              if (newResultKind != call.ResultKind && call.Result != null) {
                UpdateSubsequentAssignOps(block, i + 1, call.Result, newResultKind);
              }
            }
          } else if (op is MaxonTryCallOp tryCall) {
            var newCallee = ResolveCalleeRewrite(tryCall.Callee, tryCall.ResultStructTypeName, tryCall.Args, calleeMap);
            if (newCallee != null) {
              Logger.Debug(LogCategory.Mlir, $"  Rewrote try_call {tryCall.Callee} -> {newCallee} in {func.Name}");
              var (newResultKind, newResultStructTypeName) = ResolveMonomorphizedResultType(
                tryCall.ResultKind, tryCall.ResultStructTypeName, newCallee, funcLookup);
              block.Operations[i] = new MaxonTryCallOp(newCallee, tryCall.Args, tryCall.Result, tryCall.ErrorFlag, newResultKind, newResultStructTypeName);
              if (newResultKind != tryCall.ResultKind && tryCall.Result != null) {
                UpdateSubsequentAssignOps(block, i + 1, tryCall.Result, newResultKind);
              }
            }
          }
        }
      }
    }
  }

  /// <summary>
  /// Resolve the actual result type for a rewritten call by looking up the monomorphized function.
  /// When a generic function (e.g. Array.next) returns a type parameter (Element),
  /// the monomorphized version (e.g. StringArray.next) returns the concrete type (String).
  /// </summary>
  private static (MaxonValueKind?, string?) ResolveMonomorphizedResultType(
      MaxonValueKind? originalKind, string? originalStructTypeName,
      string newCallee, Dictionary<string, MlirFunction<MaxonOp>> funcLookup) {
    if (!funcLookup.TryGetValue(newCallee, out var newFunc) || newFunc.ReturnType == null)
      return (originalKind, originalStructTypeName);

    var kind = newFunc.ReturnType.ToValueKind();
    var typeName = newFunc.ReturnType switch {
      MlirStructType s => s.Name,
      MlirEnumType e => e.Name,
      _ => (string?)null
    };
    return (kind, typeName);
  }

  /// <summary>
  /// Update MaxonAssignOps that reference the given result value to use the new value kind.
  /// This is needed when monomorphization changes a function's return type (e.g. from
  /// TypeParameter to concrete Struct).
  /// </summary>
  private static void UpdateSubsequentAssignOps(
      MlirBlock<MaxonOp> block, int startIndex, MaxonValue result, MaxonValueKind? newKind) {
    if (newKind == null) return;
    for (int j = startIndex; j < block.Operations.Count; j++) {
      if (block.Operations[j] is MaxonAssignOp assign && assign.Value == result) {
        block.Operations[j] = new MaxonAssignOp(assign.VarName, assign.Value, assign.IsDeclaration, assign.IsMutable, newKind.Value);
      }
    }
  }

  /// Fix __ManagedMemory element_size for multi-parameter generic types (e.g., Map<Key, Value>).
  /// Finds each struct literal that wraps a __ManagedMemory, determines the correct element
  /// size from the resolved type alias, and patches the element_size field.
  private static void PatchManagedMemoryElementSizes(
      MlirFunction<MaxonOp> func,
      Dictionary<string, MlirType> typeSubstitution) {
    foreach (var block in func.Body.Blocks) {
      // Build a map of result IDs to their __ManagedMemory struct literal ops
      var managedMemOps = new Dictionary<int, (MaxonStructLiteralOp Op, int BlockIndex)>();
      for (int i = 0; i < block.Operations.Count; i++) {
        if (block.Operations[i] is MaxonStructLiteralOp mmOp && mmOp.TypeName == "__ManagedMemory") {
          managedMemOps[mmOp.Result.Id] = (mmOp, i);
        }
      }

      if (managedMemOps.Count == 0) continue;

      // Find struct literals that wrap these __ManagedMemory ops (via "managed" field)
      for (int i = 0; i < block.Operations.Count; i++) {
        if (block.Operations[i] is not MaxonStructLiteralOp wrapperOp) continue;
        if (wrapperOp.TypeName == "__ManagedMemory") continue;

        foreach (var (fieldName, fieldVal) in wrapperOp.FieldValues) {
          if (fieldName != "managed") continue;
          if (!managedMemOps.TryGetValue(fieldVal.Id, out var mmInfo)) continue;

          // Determine element size from the wrapper's type alias type params
          int? elemSize = GetElementSizeFromResolvedAlias(wrapperOp.TypeName, typeSubstitution);
          if (elemSize == null || elemSize == 0) continue;

          // Patch the element_size field in the __ManagedMemory struct literal
          var mmOp = mmInfo.Op;
          for (int fi = 0; fi < mmOp.FieldValues.Count; fi++) {
            if (mmOp.FieldValues[fi].FieldName != "element_size") continue;
            // Create new literal with correct element size and insert before the __ManagedMemory op
            var newLit = new MaxonLiteralOp((long)elemSize.Value);
            block.Operations.Insert(mmInfo.BlockIndex, newLit);
            mmOp.FieldValues[fi] = ("element_size", newLit.Result);
            // Adjust indices since we inserted an op
            foreach (var key in managedMemOps.Keys.ToList()) {
              var (Op, BlockIndex) = managedMemOps[key];
              if (BlockIndex >= mmInfo.BlockIndex)
                managedMemOps[key] = (Op, BlockIndex + 1);
            }
            i++;
            break;
          }
        }
      }
    }
  }

  /// Get the element size for a resolved type alias by looking up its type definition.
  /// The typeName should already be resolved via SubName (e.g., "StringArray" not "KeyArray").
  private static int? GetElementSizeFromResolvedAlias(
      string typeName, Dictionary<string, MlirType> typeSubstitution) {
    // The typeName is the result of SubName — a concrete alias like "StringArray".
    // Find it in the substitution values to get its type definition with Element type param.
    foreach (var (_, concreteType) in typeSubstitution) {
      if (concreteType is MlirStructType st && st.Name == typeName) {
        if (st.TypeParams != null && st.TypeParams.TryGetValue("Element", out var elemType) && elemType is not MlirTypeParameterType) {
          return elemType.SizeInBytes;
        }
      }
    }
    return null;
  }
}
