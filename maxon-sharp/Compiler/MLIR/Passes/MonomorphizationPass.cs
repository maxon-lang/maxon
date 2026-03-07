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
  private static void CopyCallMetadata(MaxonCallOp source, MaxonCallOp target) {
    target.ArgMutabilities = source.ArgMutabilities;
    target.ArgVarNames = source.ArgVarNames;
    target.IsDiscardedResult = source.IsDiscardedResult;
    target.IsLetDiscardResult = source.IsLetDiscardResult;
    target.CallLine = source.CallLine;
    target.CallColumn = source.CallColumn;
  }

  public static void Run(MlirModule<MaxonOp> module) {
    var allSpecializations = new List<Specialization>();

    // Iterate until no new specializations are found (handles transitive type aliases
    // like Array with Entry where Entry is itself a type alias resolved during an earlier round)
    while (true) {
      var specializations = CollectNeededSpecializations(module);
      if (specializations.Count == 0) break;

      var newFunctions = new List<MlirFunction<MaxonOp>>();
      foreach (var spec in specializations) {
        if (spec.SourceFunc.Body.Blocks.Count == 0) {
          Logger.Debug(LogCategory.Mlir, $"  WARNING: Source function {spec.SourceFunc.Name} has empty body, skipping monomorphization to {spec.ConcreteTypeName}");
          continue;
        }
        var clonedFunc = new FunctionCloner(spec.SourceFunc, spec.ConcreteTypeName, spec.TypeSubstitution, module.TypeAliasSources, module.TypeDefs).Clone();
        newFunctions.Add(clonedFunc);
        Logger.Debug(LogCategory.Mlir, $"Monomorphized {spec.SourceFunc.Name} -> {clonedFunc.Name}");
      }

      foreach (var func in newFunctions) {
        module.Functions.Add(func);
        // Register tuple return types created by type substitution (e.g., __Tuple_i64_String
        // from substituting Element→String in __Tuple_i64_Element)
        if (func.ReturnType is MlirStructType retSt && retSt.IsTuple
            && !module.TypeDefs.ContainsKey(retSt.Name)) {
          module.TypeDefs[retSt.Name] = retSt;
        }
      }

      allSpecializations.AddRange(specializations);
    }

    if (allSpecializations.Count > 0) {
      RewriteCallSites(module, allSpecializations);
    }

    // Stage 2: Specialize functions with interface alias parameters per call-site arg type
    RunInterfaceAliasSpecialization(module);
  }

  internal record Specialization(
    MlirFunction<MaxonOp> SourceFunc,
    string SourceTypeName,
    string ConcreteTypeName,
    TypeSubstitution TypeSubstitution);

  private static List<Specialization> CollectNeededSpecializations(MlirModule<MaxonOp> module) {
    var specializations = new List<Specialization>();

    foreach (var (aliasName, aliasInfo) in module.TypeAliasSources.ToList()) {
      if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) continue;
      if (aliasInfo.TypeParams.Values.Any(t => t is MlirTypeParameterType)) continue;

      var sourceTypeName = aliasInfo.SourceTypeName;

      if (!module.TypeDefs.TryGetValue(sourceTypeName, out var sourceType)) continue;

      List<string> assocTypeNames;
      TypeSubstitution typeSubstitution;
      if (sourceType is MlirStructType sourceStruct) {
        assocTypeNames = sourceStruct.AssociatedTypeNames.Count > 0
          ? sourceStruct.AssociatedTypeNames
          : [.. sourceStruct.TypeParams
              .Where(kv => kv.Value is MlirTypeParameterType)
              .Select(kv => kv.Key)];
        if (assocTypeNames.Count == 0) {
          continue;
        }
        typeSubstitution = TypeSubstitution.Build(sourceStruct, aliasInfo.TypeParams, aliasName, module);
      } else if (sourceType is MlirUnionType sourceUnion) {
        assocTypeNames = sourceUnion.AssociatedTypeNames.Count > 0
          ? sourceUnion.AssociatedTypeNames
          : [.. sourceUnion.TypeParams
              .Where(kv => kv.Value is MlirTypeParameterType)
              .Select(kv => kv.Key)];
        if (assocTypeNames.Count == 0) {
          Logger.Debug(LogCategory.Mlir, $"  SKIP {aliasName} -> {sourceTypeName}: no type params (union)");
          continue;
        }
        typeSubstitution = TypeSubstitution.BuildForUnion(sourceUnion, aliasInfo.TypeParams, aliasName, module);
      } else {
        Logger.Debug(LogCategory.Mlir, $"  SKIP {aliasName} -> {sourceTypeName}: not struct or union ({sourceType.GetType().Name})");
        continue;
      }

      var sourcePrefix = $"{sourceTypeName}.";
      var sourceSuffix = $".{sourceTypeName}.";
      foreach (var func in module.Functions) {
        bool isSourceMethod = func.Name.StartsWith(sourcePrefix);
        bool isSuffixMatch = !isSourceMethod && func.Name.Contains(sourceSuffix);

        if (!isSourceMethod && !isSuffixMatch) continue;
        if (!NeedsSpecializationForType(func, assocTypeNames, sourceTypeName)) continue;

        // Skip conditional extension methods whose where constraints aren't satisfied
        if (func.ExtensionWhereConstraints != null
            && !SatisfiesWhereConstraints(func.ExtensionWhereConstraints, aliasInfo.TypeParams, module))
          continue;

        string methodName;
        if (isSourceMethod) {
          methodName = func.Name[sourcePrefix.Length..];
        } else {
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

  private static bool NeedsSpecializationForType(MlirFunction<MaxonOp> func, List<string> assocTypeNames, string sourceTypeName) {
    foreach (var paramType in func.ParamTypes) {
      if (paramType is MlirTypeParameterType) return true;
      if (paramType is MlirStructType st && (st.Name == sourceTypeName || st.Name == "Self" || assocTypeNames.Any(n => st.Name.Contains(n)))) return true;
      if (paramType is MlirUnionType ut && (ut.Name == sourceTypeName || ut.Name == "Self" || assocTypeNames.Any(n => ut.Name.Contains(n)))) return true;
    }

    if (func.ReturnType is MlirTypeParameterType) return true;
    if (func.ReturnType is MlirStructType retSt && (retSt.Name == sourceTypeName || retSt.Name == "Self" || assocTypeNames.Any(n => retSt.Name.Contains(n)))) return true;
    if (func.ReturnType is MlirUnionType retUt && (retUt.Name == sourceTypeName || retUt.Name == "Self" || assocTypeNames.Any(n => retUt.Name.Contains(n)))) return true;

    return false;
  }

  /// <summary>
  /// Check whether concrete type params satisfy conditional extension where constraints.
  /// </summary>
  private static bool SatisfiesWhereConstraints(
      Dictionary<string, List<string>> whereConstraints,
      Dictionary<string, MlirType> typeParams,
      MlirModule<MaxonOp> module) {
    foreach (var (paramName, requiredInterfaces) in whereConstraints) {
      if (!typeParams.TryGetValue(paramName, out var concreteType)) return false;
      if (concreteType is MlirTypeParameterType) return false;

      var concreteTypeName = MlirType.FormatAsSourceName(concreteType);
      foreach (var requiredInterface in requiredInterfaces) {
        if (!TypeConformsToInterface(concreteTypeName, requiredInterface, module)) return false;
      }
    }
    return true;
  }

  internal static bool TypeConformsToInterface(string typeName, string interfaceName, MlirModule<MaxonOp> module) {
    if (module.TypeDefs.TryGetValue(typeName, out var typeEntry)) {
      if (typeEntry is MlirStructType st && st.ConformingInterfaces.Contains(interfaceName))
        return true;
      if (typeEntry is MlirUnionType et && et.ConformingInterfaces.Contains(interfaceName))
        return true;
    }
    if (module.PrimitiveConformances.TryGetValue(typeName, out var extInterfaces)
        && extInterfaces.Contains(interfaceName))
      return true;
    return false;
  }

  private static string? ResolveCalleeRewrite(string callee, string? resultStructTypeName, List<MaxonValue> args, Dictionary<(string, string), string> calleeMap) {
    // Prioritize self argument type over result type to avoid ambiguity when
    // the return type is itself a specialized type (e.g., Array<ByteArray>.get()
    // returns ByteArray, but should resolve to ByteArrayArray.get, not ByteArray.get).
    if (args.Count > 0 && args[0] is MaxonStruct selfStruct) {
      var key = (callee, selfStruct.TypeName);
      if (calleeMap.TryGetValue(key, out var newCallee)) return newCallee;
    }
    if (resultStructTypeName != null) {
      var key = (callee, resultStructTypeName);
      if (calleeMap.TryGetValue(key, out var newCallee)) return newCallee;
    }
    return null;
  }

  private static void RewriteCallSites(MlirModule<MaxonOp> module, List<Specialization> specializations) {
    var calleeMap = new Dictionary<(string, string), string>();
    foreach (var spec in specializations) {
      var dotIdx = spec.SourceFunc.Name.LastIndexOf('.');
      var methodName = dotIdx >= 0 ? spec.SourceFunc.Name[(dotIdx + 1)..] : spec.SourceFunc.Name;
      var newCallee = $"{spec.ConcreteTypeName}.{methodName}";
      calleeMap[(spec.SourceFunc.Name, spec.ConcreteTypeName)] = newCallee;
    }

    var funcLookup = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var f in module.Functions) {
      funcLookup[f.Name] = f;
    }

    // Iterate to a fixed point: rewrite calls, propagate types across all blocks
    // in the function, then rewrite again until no more rewrites are found.
    // This handles chains like: insertLast() -> n1 -> n1.next() -> fwd -> fwd.value()
    // where each rewrite enables the next through type propagation.
    foreach (var func in module.Functions) {
      bool anyRewrites = true;
      while (anyRewrites) {
        anyRewrites = false;
        foreach (var block in func.Body.Blocks) {
          for (int i = 0; i < block.Operations.Count; i++) {
            var op = block.Operations[i];

            if (op is MaxonCallOp call) {
              var newCallee = ResolveCalleeRewrite(call.Callee, call.ResultStructTypeName, call.Args, calleeMap);
              if (newCallee != null) {
                anyRewrites = true;
                Logger.Debug(LogCategory.Mlir, $"  Rewrote call {call.Callee} -> {newCallee} in {func.Name}");
                var (newResultKind, newResultStructTypeName) = ResolveMonomorphizedResultType(
                  call.ResultKind, call.ResultStructTypeName, newCallee, funcLookup);
                // Update the result value's type name to match the resolved type
                if (newResultStructTypeName != null && call.Result is MaxonStruct resultStruct
                    && resultStruct.TypeName != newResultStructTypeName) {
                  resultStruct.TypeName = newResultStructTypeName;
                }
                if (call is MaxonTryCallOp tryCall) {
                  var newOp = new MaxonTryCallOp(newCallee, tryCall.Args, tryCall.Result, tryCall.ErrorFlag, newResultKind, newResultStructTypeName);
                  CopyCallMetadata(call, newOp);
                  block.Operations[i] = newOp;
                } else {
                  var newOp = new MaxonCallOp(newCallee, call.Args, call.Result, newResultKind, newResultStructTypeName);
                  CopyCallMetadata(call, newOp);
                  block.Operations[i] = newOp;
                }
                if (newResultKind != call.ResultKind && call.Result != null) {
                  UpdateSubsequentAssignOps(block, i + 1, call.Result, newResultKind);
                }
              }
            }
          }
        }
        // After rewriting all blocks, propagate type names across the entire function
        // so that variables defined in one block (e.g., entry) have their concrete types
        // visible in continuation blocks (e.g., otherwise_continue_1)
        PropagateStructTypeNames(func);
      }
    }
  }

  private static (MaxonValueKind?, string?) ResolveMonomorphizedResultType(
      MaxonValueKind? originalKind, string? originalStructTypeName,
      string newCallee, Dictionary<string, MlirFunction<MaxonOp>> funcLookup) {
    if (!funcLookup.TryGetValue(newCallee, out var newFunc) || newFunc.ReturnType == null)
      return (originalKind, originalStructTypeName);

    var kind = newFunc.ReturnType.ToValueKind();
    var typeName = newFunc.ReturnType switch {
      MlirStructType s => s.Name,
      MlirUnionType e => e.Name,
      _ => (string?)null
    };
    return (kind, typeName);
  }

  private static void UpdateSubsequentAssignOps(
      MlirBlock<MaxonOp> block, int startIndex, MaxonValue result, MaxonValueKind? newKind) {
    if (newKind == null) return;
    for (int j = startIndex; j < block.Operations.Count; j++) {
      if (block.Operations[j] is MaxonAssignOp assign && assign.Value == result) {
        block.Operations[j] = new MaxonAssignOp(assign.VarName, assign.Value, assign.IsDeclaration, assign.IsMutable, newKind.Value);
      }
    }
  }

  /// <summary>
  /// After call rewrites, propagate concrete struct type names through assignment chains
  /// across ALL blocks in a function. Variables flow across blocks (e.g., a variable
  /// assigned in entry can be referenced in otherwise_continue_1), so propagation must
  /// span the entire function body.
  /// </summary>
  private static void PropagateStructTypeNames(MlirFunction<MaxonOp> func) {
    // Map: variable name -> concrete struct type name
    var varTypes = new Dictionary<string, string>();
    // Map: value ID -> concrete struct type name
    var valueTypes = new Dictionary<int, string>();

    // Seed from all call results across all blocks.
    // Use indexed iteration to avoid "collection modified during enumeration"
    // when this runs inside the fixed-point rewrite loop.
    for (int bi = 0; bi < func.Body.Blocks.Count; bi++) {
      var ops = func.Body.Blocks[bi].Operations;
      for (int oi = 0; oi < ops.Count; oi++) {
        if (ops[oi] is MaxonCallOp call && call.Result is MaxonStruct callResult) {
          valueTypes[callResult.Id] = callResult.TypeName;
        }
      }
    }

    // Multi-pass: propagate through assignment chains across all blocks until stable
    bool changed = true;
    while (changed) {
      changed = false;
      for (int bi = 0; bi < func.Body.Blocks.Count; bi++) {
        var ops = func.Body.Blocks[bi].Operations;
        for (int oi = 0; oi < ops.Count; oi++) {
          var op = ops[oi];
          if (op is MaxonAssignOp assign) {
            if (valueTypes.TryGetValue(assign.Value.Id, out var concreteType)) {
              if (!varTypes.ContainsKey(assign.VarName)) {
                varTypes[assign.VarName] = concreteType;
                changed = true;
              }
            }
          }
          if (op is MaxonStructVarRefOp varRef) {
            if (varTypes.TryGetValue(varRef.VarName, out var knownType)) {
              if (!valueTypes.ContainsKey(varRef.Result.Id)) {
                valueTypes[varRef.Result.Id] = knownType;
                changed = true;
              }
              if (varRef.Result.TypeName != knownType) {
                varRef.Result.TypeName = knownType;
                changed = true;
              }
            }
          }
        }
      }
    }
  }

  // ============================================================================
  // Stage 2: Interface alias parameter specialization
  // ============================================================================

  private record InterfaceAliasSpec(
    MlirFunction<MaxonOp> SourceFunc,
    string SpecializedName,
    Dictionary<string, MlirType> Substitution);

  private static void RunInterfaceAliasSpecialization(MlirModule<MaxonOp> module) {
    // Find all interface alias types
    var interfaceAliases = new Dictionary<string, MlirStructType>();
    foreach (var (name, type) in module.TypeDefs) {
      if (type is MlirStructType st && st.IsInterfaceAlias)
        interfaceAliases[name] = st;
    }
    if (interfaceAliases.Count == 0) return;

    // Find functions with interface alias parameter types
    var ifaceFuncs = new Dictionary<string, (MlirFunction<MaxonOp> Func, List<(int Index, string AliasName)> Params)>();
    foreach (var func in module.Functions) {
      List<(int, string)>? ifaceParams = null;
      for (int i = 0; i < func.ParamTypes.Count; i++) {
        if (func.ParamTypes[i] is MlirStructType paramSt && interfaceAliases.ContainsKey(paramSt.Name)) {
          ifaceParams ??= [];
          ifaceParams.Add((i, paramSt.Name));
        }
      }
      if (ifaceParams != null) {
        ifaceFuncs[func.Name] = (func, ifaceParams);
      }
    }
    if (ifaceFuncs.Count == 0) return;

    // Scan call sites to determine concrete arg types
    var specs = new List<InterfaceAliasSpec>();
    var callSiteRewrites = new List<(MlirBlock<MaxonOp> Block, int OpIndex, string NewCallee)>();

    foreach (var func in module.Functions.ToList()) {
      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          var op = block.Operations[i];
          string? callee = null;
          List<MaxonValue>? args = null;
          if (op is MaxonCallOp call) { callee = call.Callee; args = call.Args; } else if (op is MaxonTryCallOp tryCall) { callee = tryCall.Callee; args = tryCall.Args; }
          if (callee == null || args == null) continue;
          if (!ifaceFuncs.TryGetValue(callee, out var ifaceInfo)) continue;

          // Build substitution map from interface alias → concrete arg type
          var substitution = new Dictionary<string, MlirType>();
          var nameParts = new List<string>();
          foreach (var (paramIdx, aliasName) in ifaceInfo.Params) {
            if (paramIdx >= args.Count) continue;
            if (args[paramIdx] is not MaxonStruct argStruct) continue;
            var concreteTypeName = argStruct.TypeName;
            if (module.TypeDefs.TryGetValue(concreteTypeName, out var concreteType)) {
              substitution[aliasName] = concreteType;
              nameParts.Add(concreteTypeName);
            }
          }
          if (substitution.Count == 0) continue;

          var specializedName = $"{callee}${string.Join("$", nameParts)}";

          // Record the spec if not already seen
          if (!specs.Any(s => s.SpecializedName == specializedName)) {
            specs.Add(new InterfaceAliasSpec(ifaceInfo.Func, specializedName, substitution));
          }
          callSiteRewrites.Add((block, i, specializedName));
        }
      }
    }

    if (specs.Count == 0) return;

    // Create specialized functions
    foreach (var spec in specs) {
      var subMap = new Dictionary<string, MlirType>(spec.Substitution);
      // Also add Self mapping to preserve the function's owning type
      var dotIdx = spec.SourceFunc.Name.LastIndexOf('.');
      if (dotIdx > 0) {
        var ownerTypeName = spec.SourceFunc.Name[..dotIdx];
        if (module.TypeDefs.TryGetValue(ownerTypeName, out var ownerType))
          subMap.TryAdd("Self", ownerType);
        subMap.TryAdd(ownerTypeName, module.TypeDefs.GetValueOrDefault(ownerTypeName) ?? new MlirStructType(ownerTypeName, []));
      }
      var typeSub = new InterfaceAliasTypeSubstitution(subMap);
      var clonedFunc = CloneWithInterfaceAliasSubstitution(spec.SourceFunc, spec.SpecializedName, typeSub);
      module.Functions.Add(clonedFunc);
      Logger.Debug(LogCategory.Mlir, $"Interface alias specialization: {spec.SourceFunc.Name} -> {spec.SpecializedName}");
    }

    // Rewrite call sites
    var funcLookup = module.Functions.ToDictionary(f => f.Name, f => f);
    foreach (var (block, opIndex, newCallee) in callSiteRewrites) {
      var op = block.Operations[opIndex];
      if (op is MaxonCallOp call) {
        var (resultKind, resultStructTypeName) = ResolveMonomorphizedResultType(
          call.ResultKind, call.ResultStructTypeName, newCallee, funcLookup);
        var newOp = new MaxonCallOp(newCallee, call.Args, call.Result, resultKind, resultStructTypeName);
        CopyCallMetadata(call, newOp);
        block.Operations[opIndex] = newOp;
      } else if (op is MaxonTryCallOp tryCall) {
        var (resultKind, resultStructTypeName) = ResolveMonomorphizedResultType(
          tryCall.ResultKind, tryCall.ResultStructTypeName, newCallee, funcLookup);
        var newOp = new MaxonTryCallOp(newCallee, tryCall.Args, tryCall.Result, tryCall.ErrorFlag, resultKind, resultStructTypeName);
        CopyCallMetadata(tryCall, newOp);
        block.Operations[opIndex] = newOp;
      }
    }

    // Remove stub functions (interface alias method stubs with no body)
    module.Functions.RemoveAll(f => {
      var dotIdx = f.Name.LastIndexOf('.');
      if (dotIdx <= 0) return false;
      var typePart = f.Name[..dotIdx];
      return interfaceAliases.ContainsKey(typePart) && f.Body.Blocks.Count == 0;
    });
  }

  /// Minimal type substitution for interface alias specialization.
  /// Maps interface alias type names to concrete types for callee rewriting.
  private class InterfaceAliasTypeSubstitution(Dictionary<string, MlirType> map) {
    public MlirType SubstituteType(MlirType type) {
      if (type is MlirStructType st && map.TryGetValue(st.Name, out var newType))
        return newType;
      return type;
    }

    public string SubstituteCallee(string callee) {
      var dotIdx = callee.LastIndexOf('.');
      if (dotIdx > 0) {
        var typePart = callee[..dotIdx];
        if (map.TryGetValue(typePart, out var newType))
          return $"{newType.Name}.{callee[(dotIdx + 1)..]}";
      }
      return callee;
    }

    public string SubstituteName(string name) {
      return map.TryGetValue(name, out var newType) ? newType.Name : name;
    }

    public bool TryGetValue(string key, out MlirType value) => map.TryGetValue(key, out value!);
  }

  /// Clone a function replacing interface alias types/callees with concrete types.
  private static MlirFunction<MaxonOp> CloneWithInterfaceAliasSubstitution(
      MlirFunction<MaxonOp> source, string newName, InterfaceAliasTypeSubstitution sub) {
    var newParamTypes = source.ParamTypes.Select(t => sub.SubstituteType(t)).ToList();
    var newReturnType = source.ReturnType != null ? sub.SubstituteType(source.ReturnType) : null;

    var newFunc = new MlirFunction<MaxonOp>(
      newName, [.. source.ParamNames], newParamTypes, newReturnType, source.ThrowsType) {
      IsStdlib = source.IsStdlib,
      SourceLine = source.SourceLine,
      SourceColumn = source.SourceColumn
    };

    // Clone blocks and operations with callee substitution
    var valueMap = new Dictionary<int, MaxonValue>();

    MaxonValue MapValue(MaxonValue old) {
      if (valueMap.TryGetValue(old.Id, out var mapped)) return mapped;
      var newId = MlirContext.Current.NextId();
      MaxonValue newVal = old switch {
        MaxonInteger => new MaxonInteger(newId),
        MaxonFloat => new MaxonFloat(newId),
        MaxonBool => new MaxonBool(newId),
        MaxonByte => new MaxonByte(newId),
        MaxonShort => new MaxonShort(newId),
        MaxonStruct s => new MaxonStruct(newId, sub.SubstituteName(s.TypeName)),
        MaxonEnum e => new MaxonEnum(newId, e.TypeName),
        MaxonFunctionPtr => new MaxonFunctionPtr(newId),
        _ => throw new InvalidOperationException($"Unknown MaxonValue type: {old.GetType()}")
      };
      valueMap[old.Id] = newVal;
      return newVal;
    }

    foreach (var block in source.Body.Blocks) {
      var newBlock = newFunc.Body.AddBlock(block.Name);
      foreach (var op in block.Operations) {
        var cloned = CloneOpWithCalleeSub(op, sub, MapValue, valueMap);
        newBlock.AddOp(cloned);
      }
    }

    return newFunc;
  }

  /// Clone a single op, substituting callees that reference interface alias types.
  private static MaxonOp CloneOpWithCalleeSub(
      MaxonOp op, InterfaceAliasTypeSubstitution sub, Func<MaxonValue, MaxonValue> mapValue, Dictionary<int, MaxonValue> valueMap) {
    switch (op) {
      case MaxonTryCallOp tryCall: {
        var newCallee = sub.SubstituteCallee(tryCall.Callee);
        var newArgs = tryCall.Args.Select(mapValue).ToList();
        var resultStructTypeName = tryCall.ResultStructTypeName != null ? sub.SubstituteName(tryCall.ResultStructTypeName) : null;
        var cloned = new MaxonTryCallOp(newCallee, newArgs, tryCall.ResultKind, resultStructTypeName);
        CopyCallMetadata(tryCall, cloned);
        if (tryCall.Result != null && cloned.Result != null)
          valueMap[tryCall.Result.Id] = cloned.Result;
        valueMap[tryCall.ErrorFlag.Id] = cloned.ErrorFlag;
        return cloned;
      }
      case MaxonCallOp call: {
        var newCallee = sub.SubstituteCallee(call.Callee);
        var newArgs = call.Args.Select(mapValue).ToList();
        var resultStructTypeName = call.ResultStructTypeName != null ? sub.SubstituteName(call.ResultStructTypeName) : null;
        var cloned = new MaxonCallOp(newCallee, newArgs, call.Result != null ? mapValue(call.Result) : null, call.ResultKind, resultStructTypeName);
        CopyCallMetadata(call, cloned);
        return cloned;
      }
      case MaxonAssignOp assign:
        return new MaxonAssignOp(assign.VarName, mapValue(assign.Value), assign.IsDeclaration, assign.IsMutable, assign.ValueKind);
      case MaxonParamOp param: {
        var cloned = new MaxonParamOp(param.Index, param.Name, param.ValueKind);
        valueMap[param.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonStructParamOp sp: {
        var cloned = new MaxonStructParamOp(sp.Index, sp.Name, sub.SubstituteName(sp.StructTypeName));
        valueMap[sp.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonVarRefOp varRef: {
        var cloned = new MaxonVarRefOp(varRef.VarName, varRef.ValueKind);
        valueMap[varRef.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonStructVarRefOp sv: {
        var cloned = new MaxonStructVarRefOp(sv.VarName, sub.SubstituteName(sv.StructTypeName));
        valueMap[sv.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonLiteralOp lit: {
        var cloned = lit.ValueKind switch {
          MaxonValueKind.Integer => new MaxonLiteralOp(lit.IntValue),
          MaxonValueKind.Float => new MaxonLiteralOp(lit.FloatValue),
          MaxonValueKind.Float32 => new MaxonLiteralOp(lit.FloatValue, MaxonValueKind.Float32),
          MaxonValueKind.Bool => new MaxonLiteralOp(lit.BoolValue),
          _ => throw new InvalidOperationException($"Unsupported literal kind: {lit.ValueKind}")
        };
        valueMap[lit.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonBinOp binOp: {
        var cloned = new MaxonBinOp(binOp.Operator, mapValue(binOp.Lhs), mapValue(binOp.Rhs), binOp.OperandKind, binOp.OptimalType);
        valueMap[binOp.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonCondBrOp cb:
        return new MaxonCondBrOp(mapValue(cb.Condition), cb.ThenBlock, cb.ElseBlock);
      case MaxonBrOp br:
        return new MaxonBrOp(br.Target);
      case MaxonPanicOp p:
        return new MaxonPanicOp(p.Message);
      case MaxonRefEqOp req: {
        var cloned = new MaxonRefEqOp(mapValue(req.Lhs), mapValue(req.Rhs), req.Negate);
        valueMap[req.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonReturnOp ret:
        return new MaxonReturnOp(ret.Value != null ? mapValue(ret.Value) : null, ret.IsErrorPropagation);
      case MaxonThrowOp th:
        return new MaxonThrowOp(mapValue(th.ErrorValue), th.ErrorTypeName);
      case MaxonStructLiteralOp structLit: {
        var newFieldValues = structLit.FieldValues.Select(fv => (fv.FieldName, mapValue(fv.Value))).ToList();
        var cloned = new MaxonStructLiteralOp(sub.SubstituteName(structLit.TypeName), newFieldValues) {
          ArrayLiteralTag = structLit.ArrayLiteralTag,
          ArrayLiteralCount = structLit.ArrayLiteralCount
        };
        valueMap[structLit.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonFieldAccessOp fa: {
        var cloned = new MaxonFieldAccessOp(mapValue(fa.StructValue), sub.SubstituteName(fa.TypeName), fa.FieldName, fa.ResultKind,
          fa.ResultStructTypeName != null ? sub.SubstituteName(fa.ResultStructTypeName) : null);
        valueMap[fa.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonFieldAssignOp fa:
        return new MaxonFieldAssignOp(mapValue(fa.StructValue), sub.SubstituteName(fa.TypeName), fa.FieldName, mapValue(fa.NewValue));
      case MaxonManagedMemGetOp memGet: {
        var cloned = new MaxonManagedMemGetOp(mapValue(memGet.ManagedStruct), mapValue(memGet.Index), memGet.ResultKind) {
          IsStructElement = memGet.IsStructElement,
          StructElementTypeName = memGet.StructElementTypeName,
          TypeParamName = memGet.TypeParamName
        };
        valueMap[memGet.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonManagedMemRemoveOp memRemove: {
        var cloned = new MaxonManagedMemRemoveOp(mapValue(memRemove.ManagedStruct), mapValue(memRemove.Index), memRemove.ResultKind) {
          IsStructElement = memRemove.IsStructElement,
          StructElementTypeName = memRemove.StructElementTypeName,
          TypeParamName = memRemove.TypeParamName
        };
        valueMap[memRemove.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonManagedMemSetOp memSet:
        return new MaxonManagedMemSetOp(mapValue(memSet.ManagedStruct), mapValue(memSet.Index), mapValue(memSet.Value), memSet.ElementKind) {
          IsStructElement = memSet.IsStructElement
        };
      case MaxonManagedMemCreateOp mc: {
        var cloned = new MaxonManagedMemCreateOp(mapValue(mc.Count), mc.ElementSize);
        valueMap[mc.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonManagedMemGrowOp mg:
        return new MaxonManagedMemGrowOp(mapValue(mg.ManagedStruct), mapValue(mg.NewCapacity));
      case MaxonManagedMemSetLengthOp sl:
        return new MaxonManagedMemSetLengthOp(mapValue(sl.ManagedStruct), mapValue(sl.NewLength));
      case MaxonManagedMemClearOp memClear:
        return new MaxonManagedMemClearOp(mapValue(memClear.ManagedStruct)) {
          IsStructElement = memClear.IsStructElement,
          StructElementTypeName = memClear.StructElementTypeName,
          TypeParamName = memClear.TypeParamName
        };
      case MaxonManagedMemShiftOp ms:
        return new MaxonManagedMemShiftOp(mapValue(ms.ManagedStruct), mapValue(ms.Index), mapValue(ms.Count), ms.ShiftRight);
      case MaxonManagedMemConcatOp mc: {
        var cloned = new MaxonManagedMemConcatOp(mapValue(mc.Lhs), mapValue(mc.Rhs));
        valueMap[mc.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonManagedMemSliceOp sl: {
        var cloned = new MaxonManagedMemSliceOp(mapValue(sl.Managed), mapValue(sl.Start), mapValue(sl.End));
        valueMap[sl.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonCallRuntimeOp cr: {
        var na = cr.Args.Select(mapValue).ToList();
        var cloned = new MaxonCallRuntimeOp(cr.FunctionName, na, cr.Result != null);
        if (cr.Result != null && cloned.Result != null) valueMap[cr.Result.Id] = cloned.Result;
        return cloned;
      }
      case MaxonTruncOp t: { var c = new MaxonTruncOp(mapValue(t.Input)); valueMap[t.Result.Id] = c.Result; return c; }
      case MaxonIntToFloatOp i: { var c = new MaxonIntToFloatOp(mapValue(i.Input)); valueMap[i.Result.Id] = c.Result; return c; }
      case MaxonCastOp ca: { var c = new MaxonCastOp(mapValue(ca.Input), ca.TargetKind, ca.SourceOptimalType); valueMap[ca.Result.Id] = c.Result; return c; }
      case MaxonBitcastF64ToI64Op bc: { var c = new MaxonBitcastF64ToI64Op(mapValue(bc.Input)); valueMap[bc.Result.Id] = c.Result; return c; }
      case MaxonAbsOp a: { var c = new MaxonAbsOp(mapValue(a.Input)); valueMap[a.Result.Id] = c.Result; return c; }
      case MaxonSqrtOp s: { var c = new MaxonSqrtOp(mapValue(s.Input)); valueMap[s.Result.Id] = c.Result; return c; }
      case MaxonFloorOp f: { var c = new MaxonFloorOp(mapValue(f.Input)); valueMap[f.Result.Id] = c.Result; return c; }
      case MaxonCeilOp ce: { var c = new MaxonCeilOp(mapValue(ce.Input)); valueMap[ce.Result.Id] = c.Result; return c; }
      case MaxonRoundOp r: { var c = new MaxonRoundOp(mapValue(r.Input)); valueMap[r.Result.Id] = c.Result; return c; }
      case MaxonMinOp mi: { var c = new MaxonMinOp(mapValue(mi.Lhs), mapValue(mi.Rhs)); valueMap[mi.Result.Id] = c.Result; return c; }
      case MaxonMaxOp ma: { var c = new MaxonMaxOp(mapValue(ma.Lhs), mapValue(ma.Rhs)); valueMap[ma.Result.Id] = c.Result; return c; }
      case MaxonEnumLiteralOp el: { var c = el.BackingKind is MaxonValueKind.Float or MaxonValueKind.Float32 ? new MaxonEnumLiteralOp(el.EnumTypeName, el.CaseName, el.FloatValue) : new MaxonEnumLiteralOp(el.EnumTypeName, el.CaseName, el.IntValue); valueMap[el.Result.Id] = c.Result; return c; }
      case MaxonEnumParamOp ep: { var c = new MaxonEnumParamOp(ep.Index, ep.Name, ep.EnumTypeName, ep.BackingKind); valueMap[ep.Result.Id] = c.Result; return c; }
      case MaxonEnumVarRefOp ev: { var c = new MaxonEnumVarRefOp(ev.VarName, ev.EnumTypeName, ev.BackingKind); valueMap[ev.Result.Id] = c.Result; return c; }
      case MaxonEnumRawValueOp er: { var c = new MaxonEnumRawValueOp(mapValue(er.EnumValue), er.EnumTypeName, er.ResultKind); valueMap[er.Result.Id] = c.Result; return c; }
      case MaxonErrorFlagToEnumOp ef: { var c = new MaxonErrorFlagToEnumOp(mapValue(ef.ErrorFlag), ef.EnumTypeName, ef.BackingKind, ef.HasAssociatedValues); valueMap[ef.Result.Id] = c.Result; return c; }
      case MaxonGlobalLoadOp gl: { var c = new MaxonGlobalLoadOp(gl.GlobalName, gl.ValueKind); valueMap[gl.Result.Id] = c.Result; return c; }
      case MaxonGlobalStoreOp gs: return new MaxonGlobalStoreOp(gs.GlobalName, mapValue(gs.Value), gs.ValueKind);
      case MaxonFunctionParamOp fp: { var c = new MaxonFunctionParamOp(fp.Index, fp.Name, fp.FunctionType); valueMap[fp.Result.Id] = c.Result; return c; }
      case MaxonFunctionRefOp fr: { var c = new MaxonFunctionRefOp(fr.FunctionName, fr.FunctionType); valueMap[fr.Result.Id] = c.Result; return c; }
      case MaxonFunctionVarRefOp fv: { var c = new MaxonFunctionVarRefOp(fv.VarName, fv.FunctionType); valueMap[fv.Result.Id] = c.Result; return c; }
      case MaxonIndirectCallOp indirect: {
        var newCallee = mapValue(indirect.Callee);
        var newArgs = indirect.Args.Select(mapValue).ToList();
        var cloned = new MaxonIndirectCallOp(newCallee, indirect.CalleeType, newArgs, indirect.ResultKind, indirect.ResultStructTypeName);
        if (indirect.Result != null && cloned.Result != null) valueMap[indirect.Result.Id] = cloned.Result;
        return cloned;
      }
      // Chain (doubly-linked list) ops
      case MaxonChainCreateOp: { var c = new MaxonChainCreateOp(); valueMap[((MaxonChainCreateOp)op).Result.Id] = c.Result; return c; }
      case MaxonChainInsertValueOp ci: { var c = new MaxonChainInsertValueOp(mapValue(ci.Chain), mapValue(ci.Value), ci.AtHead, sub.SubstituteName(ci.ValueKind)); valueMap[ci.Result.Id] = c.Result; return c; }
      case MaxonChainInsertRelativeValueOp cir: { var c = new MaxonChainInsertRelativeValueOp(mapValue(cir.Chain), mapValue(cir.Target), mapValue(cir.Value), cir.After, sub.SubstituteName(cir.ValueKind)); valueMap[cir.Result.Id] = c.Result; return c; }
      case MaxonChainReinsertOp cr: return new MaxonChainReinsertOp(mapValue(cr.Chain), mapValue(cr.Node), cr.AtHead);
      case MaxonChainReinsertRelativeOp crr: return new MaxonChainReinsertRelativeOp(mapValue(crr.Chain), mapValue(crr.Target), mapValue(crr.Node), crr.After);
      case MaxonChainDetachOp cd: return new MaxonChainDetachOp(mapValue(cd.Chain), mapValue(cd.Node));
      case MaxonChainRemoveOp crm: {
        var newVK = sub.SubstituteName(crm.ValueKind);
        var newRK = sub.TryGetValue(crm.ValueKind, out var rvt) ? rvt.ToValueKind() : crm.ResultKind;
        var c = new MaxonChainRemoveOp(mapValue(crm.Chain), mapValue(crm.Node), newVK, newRK);
        valueMap[crm.Result.Id] = c.Result; return c;
      }
      case MaxonChainCountOp cc: { var c = new MaxonChainCountOp(mapValue(cc.Chain)); valueMap[cc.Result.Id] = c.Result; return c; }
      case MaxonChainNodeValueOp cnv: {
        var newVK = sub.SubstituteName(cnv.ValueKind);
        var newRK = sub.TryGetValue(cnv.ValueKind, out var nvt) ? nvt.ToValueKind() : cnv.ResultKind;
        var c = new MaxonChainNodeValueOp(mapValue(cnv.Node), newVK, newRK);
        valueMap[cnv.Result.Id] = c.Result; return c;
      }
      case MaxonChainNodeSetValueOp cns: return new MaxonChainNodeSetValueOp(mapValue(cns.Node), mapValue(cns.Value), sub.SubstituteName(cns.ValueKind));
      case MaxonChainClearOp ccl: return new MaxonChainClearOp(mapValue(ccl.Chain), sub.SubstituteName(ccl.ValueKind));

      default:
        throw new InvalidOperationException($"Interface alias specialization: unhandled op type {op.GetType().Name}");
    }
  }
}
