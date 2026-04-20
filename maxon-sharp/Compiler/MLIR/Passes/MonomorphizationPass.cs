using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

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

  public static void Run(IrModule<MaxonOp> module) {
    var allSpecializations = new List<Specialization>();

    // Iterate until no new specializations are found (handles transitive type aliases
    // like Array with Entry where Entry is itself a type alias resolved during an earlier round)
    int round = 0;
    while (true) {
      // Collect all called methods each round (new cloned functions may add new call sites)
      var calledMethods = CollectCalledMethodNames(module);

      if (++round > 20) {
        var lastSpecs = CollectNeededSpecializations(module, calledMethods);
        // Show unique source->concrete pairs, deduped by source function base name
        var seen = new HashSet<string>();
        var specLines = new List<string>();
        foreach (var s in lastSpecs) {
          var baseName = s.SourceFunc.Name;
          var dotIdx = baseName.LastIndexOf('.');
          if (dotIdx > 0) baseName = baseName[..dotIdx];
          var key = $"{baseName} -> {s.ConcreteTypeName}";
          if (!seen.Add(key)) continue;
          var subs = string.Join(", ", s.TypeSubstitution.Entries
            .Where(kv => kv.Key != "Self" && kv.Key != baseName)
            .Select(kv => $"{kv.Key}={kv.Value.Name}"));
          specLines.Add($"{s.SourceFunc.Name} -> {s.ConcreteTypeName}{(subs.Length > 0 ? $" ({subs})" : "")}");
        }
        throw new InvalidOperationException(
          $"Monomorphization exceeded 20 rounds — likely infinite type recursion.\n" +
          $"Round 21 would generate {lastSpecs.Count} specialization(s), {specLines.Count} unique source(s):\n  " +
          string.Join("\n  ", specLines));
      }
      var specializations = CollectNeededSpecializations(module, calledMethods);
      if (specializations.Count == 0) break;
      Logger.Debug(LogCategory.Ir, $"Monomorphization round {round}: {specializations.Count} specialization(s)");

      var newFunctions = new List<IrFunction<MaxonOp>>();
      foreach (var spec in specializations) {
        if (spec.SourceFunc.Body.Blocks.Count == 0) {
          Logger.Debug(LogCategory.Ir, $"  WARNING: Source function {spec.SourceFunc.Name} has empty body, skipping monomorphization to {spec.ConcreteTypeName}");
          continue;
        }
        var clonedFunc = new FunctionCloner(spec.SourceFunc, spec.ConcreteTypeName, spec.TypeSubstitution, module.TypeAliasSources, module.TypeDefs).Clone();
        newFunctions.Add(clonedFunc);
        Logger.Debug(LogCategory.Ir, $"Monomorphized {spec.SourceFunc.Name} -> {clonedFunc.Name}");
      }

      foreach (var func in newFunctions) {
        // Remove empty stubs with the same name (created by extension processing
        // for type aliases before monomorphization fills in the body)
        var existingStub = module.FindFunctionByExactName(func.Name);
        if (existingStub != null && existingStub.Body.Blocks.Count == 0) {
          module.RemoveFunction(existingStub);
        }
        module.AddFunction(func);
        // Register return types created by type substitution (e.g., __Tuple_i64_String
        // from substituting Element→String in __Tuple_i64_Element, or iterator types
        // like __ArrayIterator_Integer from monomorphizing ArrayIterator with Element=Integer)
        if (func.ReturnType is IrStructType retSt && !module.TypeDefs.ContainsKey(retSt.Name)) {
          module.TypeDefs[retSt.Name] = retSt;
        }
        // Also register the self parameter struct type if it's a new concrete type
        if (func.ParamTypes.Count > 0 && func.ParamTypes[0] is IrStructType selfSt
            && !module.TypeDefs.ContainsKey(selfSt.Name)) {
          module.TypeDefs[selfSt.Name] = selfSt;
        }
      }

      allSpecializations.AddRange(specializations);
    }

    if (allSpecializations.Count > 0) {
      // Build alias source map for call rewriting fallback (e.g., IterStateArray -> Array)
      _aliasSourceMap = [];
      foreach (var (aliasName, info) in module.TypeAliasSources)
        _aliasSourceMap[aliasName] = info.SourceTypeName;
      RewriteCallSites(module, allSpecializations);
      _aliasSourceMap = null;
    }

    // Stage 2: Specialize functions with interface alias parameters per call-site arg type
    RunInterfaceAliasSpecialization(module);

    // Stage 3: Rewrite interface-qualified method callees (e.g. Producer.produce)
    // to the concrete method (Widget.produce) using the self-arg's current
    // concrete TypeName. This handles calls whose result flowed through a
    // variable bound from another function that returned an interface type.
    RewriteInterfaceMethodCalls(module);

    // Diagnostic: warn about any functions still carrying IrInterfaceType parameters after specialization
    foreach (var func in module.Functions) {
      for (int i = 0; i < func.ParamTypes.Count; i++) {
        if (func.ParamTypes[i] is IrInterfaceType ifaceType) {
          Logger.Debug(LogCategory.Ir, $"Post-monomorphization: {func.Name} still has IrInterfaceType param '{func.ParamNames[i]}' ({ifaceType.Name})");
        }
      }
    }
  }

  /// <summary>
  /// After interface-alias specialization, interface-qualified method callees
  /// (e.g. "Producer.produce") may remain on calls whose self argument now
  /// carries a concrete TypeName (e.g. "Widget"). Rewrite those callees to the
  /// concrete method. Propagation through variable assignments is handled by
  /// PropagateConcreteInterfaceReturns before this pass reads call args.
  /// </summary>
  private static void RewriteInterfaceMethodCalls(IrModule<MaxonOp> module) {
    var interfaceTypeNames = new HashSet<string>();
    foreach (var (name, type) in module.TypeDefs) {
      if (type is IrInterfaceType) interfaceTypeNames.Add(name);
      if (type is IrStructType st && st.IsInterfaceAlias) interfaceTypeNames.Add(name);
    }
    if (interfaceTypeNames.Count == 0) return;

    var funcLookup = module.Functions.ToDictionary(f => f.Name, f => f);
    var funcNames = new HashSet<string>(funcLookup.Keys);

    bool anyChange = true;
    int safety = 0;
    while (anyChange && safety++ < 20) {
      // Propagate inferred concrete types from interface-returning callees
      // onto their call results, so the self-arg of a downstream method call
      // sees the concrete type that we're about to dispatch against.
      anyChange = PropagateConcreteInterfaceReturns(module, funcLookup);

      foreach (var func in module.Functions) {
        if (func.IsBuiltinSynthetic) continue;
        // Rewrite interface-qualified method callees (Producer.produce) to the
        // concrete method (Widget.produce) using the self-arg's now-concrete
        // TypeName.
        foreach (var block in func.Body.Blocks) {
          for (int i = 0; i < block.Operations.Count; i++) {
            if (block.Operations[i] is not MaxonCallOp call) continue;
            if (call is MaxonTryCallOp) continue;
            var callee = call.Callee;
            var dotIdx = callee.LastIndexOf('.');
            if (dotIdx <= 0) continue;
            var typePart = callee[..dotIdx];
            if (!interfaceTypeNames.Contains(typePart)) continue;
            if (call.Args.Count == 0 || call.Args[0] is not MaxonStruct selfArg) continue;
            if (selfArg.TypeName == typePart) continue;
            var methodPart = callee[(dotIdx + 1)..];
            var concreteCallee = $"{selfArg.TypeName}.{methodPart}";
            if (!funcNames.Contains(concreteCallee)) continue;
            var newOp = new MaxonCallOp(concreteCallee, call.Args, call.Result, call.ResultKind, call.ResultStructTypeName);
            CopyCallMetadata(call, newOp);
            block.Operations[i] = newOp;
            anyChange = true;
          }
        }
      }
    }
  }

  internal record Specialization(
    IrFunction<MaxonOp> SourceFunc,
    string SourceTypeName,
    string ConcreteTypeName,
    TypeSubstitution TypeSubstitution);

  private static List<Specialization> CollectNeededSpecializations(
      IrModule<MaxonOp> module, HashSet<string>? calledMethods = null) {
    var specializations = new List<Specialization>();

    foreach (var (aliasName, aliasInfo) in module.TypeAliasSources.ToList()) {
      if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) continue;
      if (aliasInfo.TypeParams.Values.Any(t => t is IrTypeParameterType)) continue;

      var sourceTypeName = aliasInfo.SourceTypeName;

      if (!module.TypeDefs.TryGetValue(sourceTypeName, out var sourceType)) continue;

      List<string> assocTypeNames;
      IrStructType? sourceStruct = null;
      IrEnumType? sourceEnum = null;
      if (sourceType is IrStructType ss) {
        sourceStruct = ss;
        assocTypeNames = ss.AssociatedTypeNames.Count > 0
          ? ss.AssociatedTypeNames
          : [.. ss.TypeParams
              .Where(kv => kv.Value is IrTypeParameterType)
              .Select(kv => kv.Key)];
        if (assocTypeNames.Count == 0) {
          continue;
        }
      } else if (sourceType is IrEnumType se) {
        sourceEnum = se;
        assocTypeNames = se.AssociatedTypeNames.Count > 0
          ? se.AssociatedTypeNames
          : [.. se.TypeParams
              .Where(kv => kv.Value is IrTypeParameterType)
              .Select(kv => kv.Key)];
        if (assocTypeNames.Count == 0) {
          Logger.Debug(LogCategory.Ir, $"  SKIP {aliasName} -> {sourceTypeName}: no type params (enum)");
          continue;
        }
      } else {
        Logger.Debug(LogCategory.Ir, $"  SKIP {aliasName} -> {sourceTypeName}: not struct or enum ({sourceType.GetType().Name})");
        continue;
      }

      // Defer TypeSubstitution.Build until we know at least one method needs specialization.
      // Build has side effects (FindConcreteAlias auto-creates type aliases), so calling it
      // eagerly for aliases with no demanded methods would create spurious aliases that
      // trigger infinite monomorphization cascades.
      TypeSubstitution? typeSubstitution = null;

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

        // Demand-driven: skip methods that are never called anywhere
        if (calledMethods != null && !IsMethodCalled(methodName, sourceTypeName, aliasName, func.Name, calledMethods))
          continue;

        // Detect recursive type nesting: if the alias name contains the source type name
        // recursively (e.g., __List___List___List_X), this is unbounded type recursion.
        // Skip to prevent infinite monomorphization.
        if (IsRecursiveTypeNesting(aliasName, sourceTypeName, module.TypeAliasSources)) {
          Logger.Debug(LogCategory.Ir, $"  SKIP {aliasName}.{methodName}: recursive type nesting");
          continue;
        }

        var specializedName = $"{aliasName}.{methodName}";
        var existingFunc = module.FindFunctionByExactName(specializedName);
        if (existingFunc != null && existingFunc.Body.Blocks.Count > 0) continue;
        if (specializations.Any(s => s.ConcreteTypeName == aliasName && s.SourceFunc == func)) continue;

        // Lazily build type substitution only when we have a real demand
        typeSubstitution ??= sourceStruct != null
            ? TypeSubstitution.Build(sourceStruct, aliasInfo.TypeParams, aliasName, module)
            : TypeSubstitution.BuildForEnum(sourceEnum!, aliasInfo.TypeParams, aliasName, module);

        specializations.Add(new Specialization(func, sourceTypeName, aliasName, typeSubstitution));
      }
    }

    // Set source type method names on each TypeSubstitution so that SubstituteCallee
    // and FunctionCloner can distinguish real methods from free functions that share
    // a file namespace with the source type (e.g., IrModule.cloneIrBlock is a free
    // function in IrModule.maxon, not a method of the IrModule generic type).
    var substitutionMethodNames = new Dictionary<TypeSubstitution, (string sourceTypeName, HashSet<string> methods)>();
    foreach (var spec in specializations) {
      if (!substitutionMethodNames.TryGetValue(spec.TypeSubstitution, out var entry)) {
        entry = (spec.SourceTypeName, new HashSet<string>());
        substitutionMethodNames[spec.TypeSubstitution] = entry;
      }
      var dotIdx = spec.SourceFunc.Name.LastIndexOf('.');
      if (dotIdx >= 0)
        entry.methods.Add(spec.SourceFunc.Name[(dotIdx + 1)..]);
    }
    foreach (var (sub, (sourceTypeName, methods)) in substitutionMethodNames) {
      sub.SetSourceTypeMethodNames(sourceTypeName, methods);
    }

    return specializations;
  }

  internal static bool NeedsSpecializationForType(IrFunction<MaxonOp> func, List<string> assocTypeNames, string sourceTypeName) {
    foreach (var paramType in func.ParamTypes) {
      if (paramType is IrTypeParameterType) return true;
      if (paramType is IrStructType st && (st.Name == sourceTypeName || st.Name == "Self" || assocTypeNames.Any(n => st.Name.Contains(n)))) return true;
      if (paramType is IrEnumType ut && (ut.Name == sourceTypeName || ut.Name == "Self" || assocTypeNames.Any(n => ut.Name.Contains(n)))) return true;
    }

    if (func.ReturnType is IrTypeParameterType) return true;
    if (func.ReturnType is IrStructType retSt && (retSt.Name == sourceTypeName || retSt.Name == "Self" || assocTypeNames.Any(n => retSt.Name.Contains(n)))) return true;
    if (func.ReturnType is IrEnumType retUt && (retUt.Name == sourceTypeName || retUt.Name == "Self" || assocTypeNames.Any(n => retUt.Name.Contains(n)))) return true;

    return false;
  }

  /// <summary>
  /// Detects recursive type nesting that would cause infinite monomorphization.
  /// Two checks:
  /// 1. Auto-generated alias names like __List___List___List_X where the source type
  ///    prefix appears 2+ times.
  /// 2. Type param chain: if the alias's element type is itself an alias of the same
  ///    source type (e.g., IntListList = List with IntList, where IntList = List with Integer),
  ///    and the alias name is auto-generated (__List_IntListList), that's recursive nesting.
  /// </summary>
  private static bool IsRecursiveTypeNesting(string aliasName, string sourceTypeName,
      Dictionary<string, TypeAliasInfo> typeAliasSources) {
    // Check 1: auto-generated alias name contains the source type prefix multiple times
    var searchPattern = $"__{sourceTypeName}_";
    int count = 0;
    int idx = 0;
    while ((idx = aliasName.IndexOf(searchPattern, idx, StringComparison.Ordinal)) >= 0) {
      count++;
      idx += searchPattern.Length;
    }
    if (count >= 2) return true;

    // Check 2: only for auto-generated aliases (__List_IntListList), check if type params
    // contain a type that is itself an alias of the same source.
    // e.g., __List_IntListList where IntListList is List with IntList and IntList is List with Integer
    // This creates List<List<List<Integer>>> -> List<List<List<List<Integer>>>> -> ...
    // User-defined aliases like IntListList = List with IntList are valid and should not be blocked.
    if (!aliasName.StartsWith("__")) return false;
    if (!typeAliasSources.TryGetValue(aliasName, out var aliasInfo)) return false;
    if (aliasInfo.TypeParams == null) return false;
    foreach (var (_, paramType) in aliasInfo.TypeParams) {
      var paramTypeName = paramType.Name;
      // Walk the alias chain: if paramType -> sourceA -> sourceB -> ... reaches sourceTypeName, it's recursive
      int depth = 0;
      var current = paramTypeName;
      while (depth++ < 5 && typeAliasSources.TryGetValue(current, out var paramAliasInfo)) {
        if (paramAliasInfo.SourceTypeName == sourceTypeName) return true;
        current = paramAliasInfo.SourceTypeName;
      }
    }

    return false;
  }

  /// <summary>
  /// Checks whether a method has any call site in the module.
  /// Handles multiple naming conventions: "Type.method", "alias.method",
  /// "stdlib.Type.method", "stdlib.pkg.Type.method", and full function names.
  /// </summary>
  private static bool IsMethodCalled(string methodName, string sourceTypeName, string aliasName,
      string fullFuncName, HashSet<string> calledMethods) {
    // Direct match: "List.push", "OpRefList.push"
    if (calledMethods.Contains($"{sourceTypeName}.{methodName}")) return true;
    if (calledMethods.Contains($"{aliasName}.{methodName}")) return true;
    // Full function name: "stdlib.List.push"
    if (calledMethods.Contains(fullFuncName)) return true;

    // Check if any called method ends with ".{methodName}" and has a type prefix
    // matching the source or alias. This handles namespace-qualified calls like
    // "stdlib.helpers.itertools.ArrayIterator.next" matching against call sites
    // that reference "__ArrayIterator_String.next".
    // Instead of scanning all callees, check common patterns:
    if (calledMethods.Contains($"stdlib.{sourceTypeName}.{methodName}")) return true;

    // Match calls to already-specialized names: any callee ending with ".{methodName}"
    // whose type prefix is an alias of sourceTypeName. We check if any callee
    // contains the method name and could be a specialization of this source method.
    var suffix = $".{methodName}";
    // Also check exact alias-qualified callee with common namespace patterns
    if (calledMethods.Any(c => c.EndsWith($"{aliasName}.{methodName}"))) return true;
    foreach (var callee in calledMethods) {
      if (!callee.EndsWith(suffix)) continue;
      var typePrefix = callee[..^suffix.Length];
      // Strip namespace prefixes to get the base type
      var lastDot = typePrefix.LastIndexOf('.');
      var baseType = lastDot >= 0 ? typePrefix[(lastDot + 1)..] : typePrefix;
      if (baseType == sourceTypeName || baseType == aliasName) return true;
      // Also match when the alias name appears anywhere in the prefix (handles deeply
      // nested namespace paths like "stdlib.helpers.itertools.__ArrayIterator_String")
      if (typePrefix.Contains(aliasName)) return true;
    }

    return false;
  }

  /// <summary>
  /// Collects all method names referenced by call sites across the module.
  /// Used for demand-driven monomorphization: only specialize methods that are actually called.
  /// </summary>
  private static HashSet<string> CollectCalledMethodNames(IrModule<MaxonOp> module) {
    var called = new HashSet<string>();
    foreach (var func in module.Functions) {
      if (func.IsBuiltinSynthetic) continue;

      // Skip generic/unresolved functions — their bodies contain calls with type-parameter
      // names that aren't real call sites. Only concrete functions contribute demand.
      if (func.ParamTypes.Any(t => t is IrTypeParameterType)) continue;
      if (func.ReturnType is IrTypeParameterType) continue;

      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonCallOp call)
            called.Add(call.Callee);
          else if (op is MaxonIteratorAdvanceOp iterAdv) {
            // for-in loops generate deferred advance() calls and require createIterator
            called.Add($"{iterAdv.IterableTypeName}.advance");
            called.Add($"{iterAdv.IteratorAliasName}.advance");
            called.Add($"{iterAdv.IterableTypeName}.createIterator");
            // Resolve iterator alias to its source type (e.g., ArrayIter -> ArrayIterator)
            if (module.TypeAliasSources.TryGetValue(iterAdv.IteratorAliasName, out var iterAliasInfo))
              called.Add($"{iterAliasInfo.SourceTypeName}.advance");
            // Resolve iterable alias to its source type for createIterator
            if (module.TypeAliasSources.TryGetValue(iterAdv.IterableTypeName, out var iterableAliasInfo))
              called.Add($"{iterableAliasInfo.SourceTypeName}.createIterator");
          } else if (op is MaxonIteratorCurrentOp iterCur) {
            called.Add($"{iterCur.IterableTypeName}.current");
            called.Add($"{iterCur.IteratorAliasName}.current");
            if (module.TypeAliasSources.TryGetValue(iterCur.IteratorAliasName, out var iterAliasInfo2))
              called.Add($"{iterAliasInfo2.SourceTypeName}.current");
          }
        }
      }
    }
    // Add implicit clone demand for types stored in managed memory containers.
    // CloneSynthesisPass (runs after monomorphization) synthesizes clone() for struct
    // element types found in MaxonManagedMemGetOp. Those synthesized clones call
    // field-level clones (e.g., __ManagedMemory_X.clone), so we must ensure those
    // field types' clone methods are also monomorphized.
    foreach (var func in module.Functions) {
      if (func.IsBuiltinSynthetic) continue;

      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonManagedMemGetOp { IsStructElement: true, StructElementTypeName: string elemType }) {
            // The element type itself needs clone
            called.Add($"{elemType}.clone");
            // Also demand clone for all struct fields of this element type
            if (module.TypeDefs.TryGetValue(elemType, out var elemDef) && elemDef is IrStructType elemStruct) {
              foreach (var field in elemStruct.Fields) {
                if (field.Type is IrStructType fieldSt)
                  called.Add($"{fieldSt.Name}.clone");
              }
            }
          }
        }
      }
    }

    // Resolve type alias callees: "OpRefList.push" -> also add "List.push"
    var aliasResolved = new HashSet<string>();
    foreach (var callee in called) {
      var dotIdx = callee.LastIndexOf('.');
      if (dotIdx <= 0) continue;
      var typePart = callee[..dotIdx];
      var methodPart = callee[(dotIdx + 1)..];
      if (module.TypeAliasSources.TryGetValue(typePart, out var aliasInfo))
        aliasResolved.Add($"{aliasInfo.SourceTypeName}.{methodPart}");
    }
    called.UnionWith(aliasResolved);
    return called;
  }

  /// <summary>
  /// Check whether concrete type params satisfy conditional extension where constraints.
  /// </summary>
  private static bool SatisfiesWhereConstraints(
      Dictionary<string, List<string>> whereConstraints,
      Dictionary<string, IrType> typeParams,
      IrModule<MaxonOp> module) {
    foreach (var (paramName, requiredInterfaces) in whereConstraints) {
      if (!typeParams.TryGetValue(paramName, out var concreteType)) return false;
      if (concreteType is IrTypeParameterType) return false;

      var concreteTypeName = IrType.FormatAsSourceName(concreteType);
      foreach (var requiredInterface in requiredInterfaces) {
        if (!TypeConformsToInterface(concreteTypeName, requiredInterface, module)) return false;
      }
    }
    return true;
  }

  internal static bool TypeConformsToInterface(string typeName, string interfaceName, IrModule<MaxonOp> module) {
    if (module.TypeDefs.TryGetValue(typeName, out var typeEntry)) {
      if (typeEntry is IrStructType st && st.ConformingInterfaces.Contains(interfaceName))
        return true;
      if (typeEntry is IrEnumType et && et.ConformingInterfaces.Contains(interfaceName))
        return true;
    }
    if (module.PrimitiveConformances.TryGetValue(typeName, out var extInterfaces)
        && extInterfaces.Contains(interfaceName))
      return true;
    return false;
  }

  private static string? ResolveCalleeRewrite(string callee, string? resultStructTypeName, List<MaxonValue> args,
      Dictionary<(string, string), string> calleeMap,
      Dictionary<(string, string), List<(string concreteType, string target)>>? siblingIndex) {
    // Prioritize self argument type over result type to avoid ambiguity when
    // the return type is itself a specialized type (e.g., Array<ByteArray>.get()
    // returns ByteArray, but should resolve to ByteArrayArray.get, not ByteArray.get).
    if (args.Count > 0 && args[0] is MaxonStruct selfStruct) {
      var key = (callee, selfStruct.TypeName);
      if (calleeMap.TryGetValue(key, out var newCallee)) return newCallee;
      // Try with stdlib. prefix (FunctionCloner strips namespace in SubstituteCallee)
      key = ($"stdlib.{callee}", selfStruct.TypeName);
      if (calleeMap.TryGetValue(key, out newCallee)) return newCallee;

      // Resolve callee type prefix through alias: ArrayIter.next -> stdlib.ArrayIterator.next
      if (_aliasSourceMap != null) {
        var dotIdx = callee.LastIndexOf('.');
        if (dotIdx >= 0) {
          var calleeTypePrefix = callee[..dotIdx];
          var calleeMethod = callee[(dotIdx + 1)..];
          if (_aliasSourceMap.TryGetValue(calleeTypePrefix, out var resolvedSource)) {
            var resolvedCallee = $"{resolvedSource}.{calleeMethod}";
            // Try direct lookup with resolved callee + self type
            key = (resolvedCallee, selfStruct.TypeName);
            if (calleeMap.TryGetValue(key, out newCallee)) return newCallee;
          }
        }
        // Resolve type alias on self arg: find the matching concrete sibling
        if (_aliasSourceMap.TryGetValue(selfStruct.TypeName, out var aliasSource)) {
          // Direct lookup by source name
          key = (callee, aliasSource);
          if (calleeMap.TryGetValue(key, out newCallee)) return newCallee;
          // Use pre-built sibling index instead of linear scan
          if (siblingIndex != null && siblingIndex.TryGetValue((callee, aliasSource), out var siblings)) {
            if (siblings.Count == 1) return siblings[0].target;
            // Ambiguous: disambiguate using the current function's name
            if (_currentRewriteFunc != null) {
              var funcTypeParts = _currentRewriteFunc.Name.Split('.');
              if (funcTypeParts.Length >= 2) {
                var funcTypeName = funcTypeParts[0];
                foreach (var (concreteType, target) in siblings) {
                  var underscoreIdx = concreteType.LastIndexOf('_');
                  if (underscoreIdx > 0) {
                    var elemSuffix = concreteType[(underscoreIdx + 1)..];
                    if (funcTypeName.EndsWith($"_{elemSuffix}")) return target;
                  }
                }
              }
            }
          }
        }
      }
    }
    if (resultStructTypeName != null) {
      var key = (callee, resultStructTypeName);
      if (calleeMap.TryGetValue(key, out var newCallee)) return newCallee;
    }
    return null;
  }

  [ThreadStatic] private static Dictionary<string, string>? _aliasSourceMap;
  [ThreadStatic] private static IrFunction<MaxonOp>? _currentRewriteFunc;

  private static void RewriteCallSites(IrModule<MaxonOp> module, List<Specialization> specializations) {
    var calleeMap = new Dictionary<(string, string), string>();
    foreach (var spec in specializations) {
      var dotIdx = spec.SourceFunc.Name.LastIndexOf('.');
      var methodName = dotIdx >= 0 ? spec.SourceFunc.Name[(dotIdx + 1)..] : spec.SourceFunc.Name;
      var newCallee = $"{spec.ConcreteTypeName}.{methodName}";
      calleeMap[(spec.SourceFunc.Name, spec.ConcreteTypeName)] = newCallee;
    }

    // Add entries for equivalent type aliases: if IterStateArray and StateArray share the same
    // source type and type params, calls on IterStateArray should resolve to StateArray's methods.
    if (_aliasSourceMap != null) {
      // Group aliases by source type
      var sourceToAliases = new Dictionary<string, List<string>>();
      foreach (var (alias, source) in _aliasSourceMap) {
        if (!sourceToAliases.TryGetValue(source, out var list)) {
          list = [];
          sourceToAliases[source] = list;
        }
        list.Add(alias);
      }
      // For each calleeMap entry, add entries for sibling aliases with the same source
      var additions = new List<((string, string) Key, string Value)>();
      foreach (var ((sourceFunc, concreteType), target) in calleeMap) {
        if (!_aliasSourceMap.TryGetValue(concreteType, out var source)) continue;
        if (!sourceToAliases.TryGetValue(source, out var siblings)) continue;
        foreach (var sibling in siblings) {
          if (sibling == concreteType) continue;
          var siblingKey = (sourceFunc, sibling);
          if (!calleeMap.ContainsKey(siblingKey))
            additions.Add((siblingKey, target));
        }
      }
      foreach (var (key, value) in additions)
        calleeMap.TryAdd(key, value);
    }

    var funcLookup = new Dictionary<string, IrFunction<MaxonOp>>();
    foreach (var f in module.Functions) {
      funcLookup[f.Name] = f;
    }

    // Pre-build sibling alias index: (callee, aliasSource) -> [(concreteType, target)]
    // This replaces O(N) linear scans in ResolveCalleeRewrite with O(1) lookups.
    Dictionary<(string, string), List<(string concreteType, string target)>>? siblingIndex = null;
    if (_aliasSourceMap != null) {
      siblingIndex = [];
      foreach (var ((mapCallee, mapConcreteType), mapTarget) in calleeMap) {
        if (_aliasSourceMap.TryGetValue(mapConcreteType, out var mapSource)) {
          var idxKey = (mapCallee, mapSource);
          if (!siblingIndex.TryGetValue(idxKey, out var list)) {
            list = [];
            siblingIndex[idxKey] = list;
          }
          list.Add((mapConcreteType, mapTarget));
        }
      }
    }

    // Pre-build iterator-wrapper .create index: (wrapperTypeName, argTypeName) -> (concreteTypeName, concreteCreateName)
    // Covers iterator wrappers that take an Iterator as their single constructor arg
    // and whose first type parameter is the source iterator type.
    var iterWrapperTypes = new HashSet<string> { "WithIterIterator" };
    var iterWrapperCreateIndex = new Dictionary<(string WrapperType, string ArgTypeName), (string ConcreteTypeName, string ConcreteCreate)>();
    foreach (var spec in specializations) {
      if (!spec.SourceFunc.Name.EndsWith(".create")) continue;
      if (!iterWrapperTypes.Contains(spec.SourceTypeName)) continue;
      foreach (var (_, ct) in spec.TypeSubstitution.Entries) {
        if (ct is IrStructType cts) {
          var concreteCreate = $"{spec.ConcreteTypeName}.create";
          if (funcLookup.ContainsKey(concreteCreate))
            iterWrapperCreateIndex.TryAdd((spec.SourceTypeName, cts.Name), (spec.ConcreteTypeName, concreteCreate));
        }
      }
    }

    // Cache for iterator method resolution: (iterableTypeName, methodName) -> resolved name
    // Key is e.g. ("Array", "advance") or ("ArrayIter", "current").
    var iteratorMethodCache = new Dictionary<(string IterableTypeName, string MethodName), string?>();

    // Try candidate names for a given type+method pair across known stdlib path prefixes.
    // Returns the first name found in funcLookup, or null if none match.
    string? TryFindFunc(string typeName, string methodName) {
      string[] prefixes = ["", "stdlib.", "stdlib.helpers.itertools.", "stdlib.helpers.string."];
      foreach (var prefix in prefixes) {
        var candidate = $"{prefix}{typeName}.{methodName}";
        if (funcLookup.ContainsKey(candidate)) return candidate;
      }
      return null;
    }

    string? ResolveIteratorMethodName(string iterableTypeName, string methodName) {
      if (iteratorMethodCache.TryGetValue((iterableTypeName, methodName), out var cached)) return cached;
      string? resolved = null;
      var createIterName = TryFindFunc(iterableTypeName, "createIterator");
      IrFunction<MaxonOp>? createIterFunc = null;
      if (createIterName != null) createIterFunc = funcLookup[createIterName];
      if (createIterFunc?.ReturnType is IrStructType concreteIterType) {
        var candidate = TryFindFunc(concreteIterType.Name, methodName);
        if (candidate != null) {
          resolved = candidate;
        } else {
          // Use generic name for deferred resolution (prefix '~')
          resolved = $"~stdlib.{concreteIterType.Name}.{methodName}";
        }
      }
      iteratorMethodCache[(iterableTypeName, methodName)] = resolved;
      return resolved;
    }

    // Iterate to a fixed point: rewrite calls, propagate types across all blocks
    // in the function, then rewrite again until no more rewrites are found.
    foreach (var func in module.Functions) {
      if (func.IsBuiltinSynthetic) continue;

      _currentRewriteFunc = func;
      bool anyRewrites = true;
      int rewritePass = 0;
      while (anyRewrites) {
        if (++rewritePass > 50) {
          // Log what's being rewritten
          foreach (var b in func.Body.Blocks)
            foreach (var o in b.Operations)
              if (o is MaxonCallOp c) Logger.Debug(LogCategory.Ir, $"  LOOP: {c.Callee} in {func.Name}");
          throw new InvalidOperationException($"Infinite rewrite loop in {func.Name} (pass {rewritePass})");
        }
        anyRewrites = false;
        foreach (var block in func.Body.Blocks) {
          for (int i = 0; i < block.Operations.Count; i++) {
            var op = block.Operations[i];

            // Resolve deferred iterator advance() ops -> MaxonTryCallOp to the concrete
            // iterator's advance() method. advance() throws IterationError.exhausted at end.
            if (op is MaxonIteratorAdvanceOp iterAdvOp) {
              var cachedName = ResolveIteratorMethodName(iterAdvOp.IterableTypeName, "advance");
              if (cachedName != null) {
                var effectiveName = cachedName.StartsWith('~') ? cachedName[1..] : cachedName;
                // advance() returns void — pass null for result and no element kind
                var newOp = new MaxonTryCallOp(effectiveName, iterAdvOp.Args, null, iterAdvOp.ErrorFlag, null, null);
                block.Operations[i] = newOp;
                anyRewrites = true;
                Logger.Debug(LogCategory.Ir, $"  Resolved iterator_advance -> {effectiveName}{(cachedName.StartsWith('~') ? " (deferred)" : "")} in {func.Name}");
              }
            }

            // Resolve deferred iterator current() ops -> MaxonCallOp (infallible read).
            if (op is MaxonIteratorCurrentOp iterCurOp) {
              var cachedName = ResolveIteratorMethodName(iterCurOp.IterableTypeName, "current");
              if (cachedName != null) {
                var effectiveName = cachedName.StartsWith('~') ? cachedName[1..] : cachedName;
                var (resKind, resStructType) = ResolveMonomorphizedResultType(
                  iterCurOp.ElementKind, iterCurOp.ElementStructTypeName, effectiveName, funcLookup);
                // Preserve existing Result value identity so consumers remain wired up.
                var newOp = new MaxonCallOp(effectiveName, iterCurOp.Args, iterCurOp.Result, resKind, resStructType);
                block.Operations[i] = newOp;
                anyRewrites = true;
                Logger.Debug(LogCategory.Ir, $"  Resolved iterator_current -> {effectiveName}{(cachedName.StartsWith('~') ? " (deferred)" : "")} in {func.Name}");
              }
            }

            // Resolve calls to generic iterator-wrapper .create (EnumeratedIterator, WithIterIterator)
            // by matching the arg's concrete iterator type to find the right specialization.
            if (op is MaxonCallOp enumCall && !enumCall.Callee.StartsWith("__")
                && enumCall.Callee.EndsWith(".create")
                && enumCall.Args.Count > 0 && enumCall.Args[0] is MaxonStruct enumArgStruct) {
              var enumDotIdx = enumCall.Callee.LastIndexOf('.');
              var enumTypePart = enumDotIdx >= 0 ? enumCall.Callee[..enumDotIdx] : "";
              string? wrapperSource = null;
              if (iterWrapperTypes.Contains(enumTypePart)) {
                wrapperSource = enumTypePart;
              } else if (_aliasSourceMap != null && _aliasSourceMap.TryGetValue(enumTypePart, out var enumSrc) && iterWrapperTypes.Contains(enumSrc)) {
                wrapperSource = enumSrc;
              }
              if (wrapperSource != null) {
                if (iterWrapperCreateIndex.TryGetValue((wrapperSource, enumArgStruct.TypeName), out var enumMatch)
                    && enumMatch.ConcreteTypeName != enumTypePart) {
                  var (resKind, resStructType) = ResolveMonomorphizedResultType(
                    enumCall.ResultKind, enumCall.ResultStructTypeName, enumMatch.ConcreteCreate, funcLookup);
                  var newOp = new MaxonCallOp(enumMatch.ConcreteCreate, enumCall.Args, enumCall.Result, resKind, resStructType);
                  CopyCallMetadata(enumCall, newOp);
                  if (resStructType != null && enumCall.Result is MaxonStruct rs && rs.TypeName != resStructType)
                    rs.TypeName = resStructType;
                  block.Operations[i] = newOp;
                  anyRewrites = true;
                  Logger.Debug(LogCategory.Ir, $"  Resolved {wrapperSource}.create -> {enumMatch.ConcreteCreate} in {func.Name}");
                }
              }
            }

            if (op is MaxonCallOp call) {
              var newCallee = ResolveCalleeRewrite(call.Callee, call.ResultStructTypeName, call.Args, calleeMap, siblingIndex);
              if (newCallee != null && newCallee != call.Callee) {
                anyRewrites = true;
                Logger.Debug(LogCategory.Ir, $"  Rewrote call {call.Callee} -> {newCallee} in {func.Name}");
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
        PropagateStructTypeNames(func, module);
      }
    }
  }

  private static (MaxonValueKind?, string?) ResolveMonomorphizedResultType(
      MaxonValueKind? originalKind, string? originalStructTypeName,
      string newCallee, Dictionary<string, IrFunction<MaxonOp>> funcLookup) {
    if (!funcLookup.TryGetValue(newCallee, out var newFunc) || newFunc.ReturnType == null)
      return (originalKind, originalStructTypeName);

    var kind = newFunc.ReturnType.ToValueKind();
    var typeName = newFunc.ReturnType switch {
      IrStructType s => s.Name,
      IrEnumType e => e.Name,
      IrInterfaceType i => InferConcreteInterfaceReturnFromFunc(newFunc) ?? i.Name,
      _ => (string?)null
    };
    return (kind, typeName);
  }

  /// <summary>
  /// Mirror of Parser.InferConcreteInterfaceReturn for post-monomorphization
  /// callee rewrites: when a specialized callee's declared return type is an
  /// interface, scan its body for a unique concrete return type. Without this,
  /// rewriting to a specialized callee leaves the interface name on the result
  /// and downstream dispatch (e.g. `.produce()` calls) fails to resolve.
  /// </summary>
  private static string? InferConcreteInterfaceReturnFromFunc(IrFunction<MaxonOp> func) {
    if (func.Body == null) return null;
    string? singleConcrete = null;
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is not MaxonReturnOp { Value: { } retVal }) continue;
        var concreteName = retVal switch {
          MaxonStruct s => s.TypeName,
          _ => null
        };
        if (concreteName == null) return null;
        if (singleConcrete == null) singleConcrete = concreteName;
        else if (singleConcrete != concreteName) return null;
      }
    }
    return singleConcrete;
  }

  /// Update a call result's TypeName in place so downstream references (e.g. a
  /// chained `.method()` call on the result) see the concrete type. Once we
  /// rewrite to a concrete specialization we must propagate the concrete type
  /// through the result MaxonStruct, otherwise the next method dispatch looks
  /// up nonexistent interface-qualified methods like `Producer.produce`.
  private static void UpdateResultTypeName(MaxonValue? result, string? newTypeName) {
    if (newTypeName == null) return;
    if (result is MaxonStruct rs && rs.TypeName != newTypeName) rs.TypeName = newTypeName;
  }

  /// For each call whose callee has an interface return type, if the callee's
  /// body unambiguously returns a single concrete implementation, overwrite
  /// the call's result TypeName with that concrete type and propagate it
  /// through assignments. Used between transitive-specialization rounds so
  /// downstream calls see concrete self-arg types — without this, chained
  /// method dispatch on the result resolves to nonexistent interface-qualified
  /// methods like `Producer.produce`.
  private static bool PropagateConcreteInterfaceReturns(IrModule<MaxonOp> module, Dictionary<string, IrFunction<MaxonOp>> funcLookup) {
    bool anyChange = false;
    bool changed = true;
    int safety = 0;
    while (changed && safety++ < 20) {
      changed = false;
      foreach (var func in module.Functions) {
        if (func.IsBuiltinSynthetic) continue;
        foreach (var block in func.Body.Blocks) {
          foreach (var op in block.Operations) {
            if (op is not MaxonCallOp call) continue;
            if (call.Result is not MaxonStruct resultStruct) continue;
            if (!funcLookup.TryGetValue(call.Callee, out var calleeFunc)) continue;
            if (calleeFunc.ReturnType is not IrInterfaceType) continue;
            var concrete = InferConcreteInterfaceReturnFromFunc(calleeFunc);
            if (concrete != null && resultStruct.TypeName != concrete) {
              resultStruct.TypeName = concrete;
              changed = true;
              anyChange = true;
            }
          }
        }
        PropagateStructTypeNames(func, module);
      }
    }
    return anyChange;
  }

  private static void UpdateSubsequentAssignOps(
      IrBlock<MaxonOp> block, int startIndex, MaxonValue result, MaxonValueKind? newKind) {
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
  private static void PropagateStructTypeNames(IrFunction<MaxonOp> func, IrModule<MaxonOp>? module = null) {
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
        // Seed from struct parameter ops (self parameter of instance methods)
        if (ops[oi] is MaxonStructParamOp structParam) {
          valueTypes[structParam.Result.Id] = structParam.StructTypeName;
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
          // Propagate through field access: if we know the struct's concrete type,
          // look up the field's concrete type from TypeDefs
          if (op is MaxonFieldAccessOp fieldAccess && fieldAccess.Result is MaxonStruct fieldResult) {
            if (valueTypes.TryGetValue(fieldAccess.StructValue.Id, out var structType)
                && module?.TypeDefs.TryGetValue(structType, out var structDef) == true
                && structDef is IrStructType structSt) {
              var field = structSt.GetField(fieldAccess.FieldName);
              if (field?.Type is IrStructType fieldStructType
                  && fieldResult.TypeName != fieldStructType.Name) {
                fieldResult.TypeName = fieldStructType.Name;
                valueTypes[fieldResult.Id] = fieldStructType.Name;
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
    IrFunction<MaxonOp> SourceFunc,
    string SpecializedName,
    Dictionary<string, IrType> Substitution);

  private static void RunInterfaceAliasSpecialization(IrModule<MaxonOp> module) {
    // Find all interface alias types
    var interfaceAliases = new Dictionary<string, IrStructType>();
    foreach (var (name, type) in module.TypeDefs) {
      if (type is IrStructType st && st.IsInterfaceAlias)
        interfaceAliases[name] = st;
    }
    if (interfaceAliases.Count == 0) return;

    // Find functions with interface alias or direct interface parameter types
    var ifaceFuncs = new Dictionary<string, (IrFunction<MaxonOp> Func, List<(int Index, string AliasName)> Params)>();
    foreach (var func in module.Functions) {
      List<(int, string)>? ifaceParams = null;
      for (int i = 0; i < func.ParamTypes.Count; i++) {
        if (func.ParamTypes[i] is IrStructType paramSt && interfaceAliases.ContainsKey(paramSt.Name)) {
          ifaceParams ??= [];
          ifaceParams.Add((i, paramSt.Name));
        } else if (func.ParamTypes[i] is IrInterfaceType paramIface) {
          ifaceParams ??= [];
          ifaceParams.Add((i, paramIface.Name));
        }
      }
      if (ifaceParams != null) {
        ifaceFuncs[func.Name] = (func, ifaceParams);
      }
    }
    if (ifaceFuncs.Count == 0) return;

    // Scan call sites to determine concrete arg types
    var specs = new List<InterfaceAliasSpec>();
    var callSiteRewrites = new List<(IrBlock<MaxonOp> Block, int OpIndex, string NewCallee)>();

    foreach (var func in module.Functions.ToList()) {
      if (func.IsBuiltinSynthetic) continue;

      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          var op = block.Operations[i];
          string? callee = null;
          List<MaxonValue>? args = null;
          if (op is MaxonCallOp call) { callee = call.Callee; args = call.Args; } else if (op is MaxonTryCallOp tryCall) { callee = tryCall.Callee; args = tryCall.Args; }
          if (callee == null || args == null) continue;
          if (!ifaceFuncs.TryGetValue(callee, out var ifaceInfo)) continue;

          // Build substitution map from interface alias → concrete arg type
          var substitution = new Dictionary<string, IrType>();
          var nameParts = new List<string>();
          foreach (var (paramIdx, aliasName) in ifaceInfo.Params) {
            if (paramIdx >= args.Count) continue;
            if (args[paramIdx] is not MaxonStruct argStruct) continue;
            var concreteTypeName = argStruct.TypeName;
            if (module.TypeDefs.TryGetValue(concreteTypeName, out var concreteType)
                && concreteType is not IrInterfaceType) {
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

    // Group specs by source function to enable in-place mutation
    var specsBySource = specs.GroupBy(s => s.SourceFunc.Name).ToDictionary(g => g.Key, g => g.ToList());

    // Create specialized functions
    foreach (var (sourceName, sourceSpecs) in specsBySource) {
      // Clone all but the last specialization; mutate the source in-place for the last one
      for (int si = 0; si < sourceSpecs.Count; si++) {
        var spec = sourceSpecs[si];
        var subMap = new Dictionary<string, IrType>(spec.Substitution);
        // Also add Self mapping to preserve the function's owning type
        var dotIdx = spec.SourceFunc.Name.LastIndexOf('.');
        if (dotIdx > 0) {
          var ownerTypeName = spec.SourceFunc.Name[..dotIdx];
          if (module.TypeDefs.TryGetValue(ownerTypeName, out var ownerType))
            subMap.TryAdd("Self", ownerType);
          subMap.TryAdd(ownerTypeName, module.TypeDefs.GetValueOrDefault(ownerTypeName) ?? new IrStructType(ownerTypeName, []));
        }
        var typeSub = new InterfaceAliasTypeSubstitution(subMap);

        // Check if source has any direct IrInterfaceType params (not just aliases).
        // If so, always clone — the source may be needed for transitive specialization.
        bool hasDirectIfaceParam = spec.SourceFunc.ParamTypes.Any(pt => pt is IrInterfaceType);
        bool isLast = si == sourceSpecs.Count - 1;
        if (isLast && !hasDirectIfaceParam) {
          // Mutate the source function in-place instead of cloning.
          // Renaming the function invalidates IrModule's lookup indices.
          MutateInterfaceAliasTypes(spec.SourceFunc, spec.SpecializedName, typeSub);
          module.InvalidateFunctionIndex();
          Logger.Debug(LogCategory.Ir, $"Interface alias specialization (in-place): {sourceName} -> {spec.SpecializedName}");
        } else {
          var clonedFunc = CloneWithInterfaceAliasSubstitution(spec.SourceFunc, spec.SpecializedName, typeSub);
          module.AddFunction(clonedFunc);
          Logger.Debug(LogCategory.Ir, $"Interface alias specialization: {sourceName} -> {spec.SpecializedName}");
        }
      }
    }

    // Rewrite call sites
    var funcLookup = module.Functions.ToDictionary(f => f.Name, f => f);
    foreach (var (block, opIndex, newCallee) in callSiteRewrites) {
      var op = block.Operations[opIndex];
      if (op is MaxonTryCallOp tryCall) {
        var (resultKind, resultStructTypeName) = ResolveMonomorphizedResultType(
          tryCall.ResultKind, tryCall.ResultStructTypeName, newCallee, funcLookup);
        UpdateResultTypeName(tryCall.Result, resultStructTypeName);
        var newOp = new MaxonTryCallOp(newCallee, tryCall.Args, tryCall.Result, tryCall.ErrorFlag, resultKind, resultStructTypeName);
        CopyCallMetadata(tryCall, newOp);
        block.Operations[opIndex] = newOp;
      } else if (op is MaxonCallOp call) {
        var (resultKind, resultStructTypeName) = ResolveMonomorphizedResultType(
          call.ResultKind, call.ResultStructTypeName, newCallee, funcLookup);
        UpdateResultTypeName(call.Result, resultStructTypeName);
        var newOp = new MaxonCallOp(newCallee, call.Args, call.Result, resultKind, resultStructTypeName);
        CopyCallMetadata(call, newOp);
        block.Operations[opIndex] = newOp;
      }
    }

    // Remove stub functions (interface alias method stubs with no body)
    module.RemoveFunctionsWhere(f => {
      var dotIdx = f.Name.LastIndexOf('.');
      if (dotIdx <= 0) return false;
      var typePart = f.Name[..dotIdx];
      return interfaceAliases.ContainsKey(typePart) && f.Body.Blocks.Count == 0;
    });

    // --- Transitive specialization ---
    // The first pass may have created specialized functions whose bodies
    // still contain calls to functions with interface parameters. Run
    // additional clone-only passes until no new specializations are found.
    var alreadySpecialized = new HashSet<string>(specs.Select(s => s.SpecializedName));
    for (int extraRound = 0; extraRound < 20; extraRound++) {
      Logger.Debug(LogCategory.Ir, $"Interface alias transitive round {extraRound}:");

      // Before re-scanning, propagate concrete types through calls that
      // return interface types — their result value's TypeName was left as
      // the interface name after cloning, which blocks downstream transitive
      // specialization because iface params get skipped when the arg type is
      // itself an interface. PropagateConcreteInterfaceReturns updates those
      // TypeNames using the callee's inferred concrete return type.
      PropagateConcreteInterfaceReturns(module, module.Functions.ToDictionary(f => f.Name, f => f));

      // Re-discover functions with interface params
      ifaceFuncs.Clear();
      foreach (var func in module.Functions) {
        List<(int, string)>? ifaceParams2 = null;
        for (int i = 0; i < func.ParamTypes.Count; i++) {
          if (func.ParamTypes[i] is IrStructType paramSt && interfaceAliases.ContainsKey(paramSt.Name)) {
            ifaceParams2 ??= [];
            ifaceParams2.Add((i, paramSt.Name));
          } else if (func.ParamTypes[i] is IrInterfaceType paramIface) {
            ifaceParams2 ??= [];
            ifaceParams2.Add((i, paramIface.Name));
          }
        }
        if (ifaceParams2 != null)
          ifaceFuncs[func.Name] = (func, ifaceParams2);
      }
      if (ifaceFuncs.Count == 0) break;

      var extraSpecs = new List<InterfaceAliasSpec>();
      var extraRewrites = new List<(IrBlock<MaxonOp> Block, int OpIndex, string NewCallee)>();

      foreach (var func in module.Functions.ToList()) {
        if (func.IsBuiltinSynthetic) continue;
        foreach (var block in func.Body.Blocks) {
          for (int i = 0; i < block.Operations.Count; i++) {
            var op = block.Operations[i];
            string? callee = null;
            List<MaxonValue>? args = null;
            if (op is MaxonCallOp c2) { callee = c2.Callee; args = c2.Args; } else if (op is MaxonTryCallOp tc2) { callee = tc2.Callee; args = tc2.Args; }
            if (callee == null || args == null) continue;
            if (!ifaceFuncs.TryGetValue(callee, out var ifaceInfo2)) continue;

            var substitution2 = new Dictionary<string, IrType>();
            var nameParts2 = new List<string>();
            foreach (var (paramIdx, aliasName) in ifaceInfo2.Params) {
              if (paramIdx >= args.Count) continue;
              if (args[paramIdx] is not MaxonStruct argStruct2) continue;
              if (module.TypeDefs.TryGetValue(argStruct2.TypeName, out var concreteType2)
                  && concreteType2 is not IrInterfaceType) {
                substitution2[aliasName] = concreteType2;
                nameParts2.Add(argStruct2.TypeName);
              }
            }
            if (substitution2.Count == 0) continue;

            var specializedName2 = $"{callee}${string.Join("$", nameParts2)}";
            if (alreadySpecialized.Contains(specializedName2)) {
              extraRewrites.Add((block, i, specializedName2));
              continue;
            }
            if (!extraSpecs.Any(s => s.SpecializedName == specializedName2))
              extraSpecs.Add(new InterfaceAliasSpec(ifaceInfo2.Func, specializedName2, substitution2));
            extraRewrites.Add((block, i, specializedName2));
          }
        }
      }

      Logger.Debug(LogCategory.Ir, $"  Round {extraRound}: {extraSpecs.Count} new specs, {extraRewrites.Count} rewrites, {ifaceFuncs.Count} iface funcs remaining");
      if (extraSpecs.Count == 0 && extraRewrites.Count == 0) break;

      foreach (var spec in extraSpecs) {
        var subMap = new Dictionary<string, IrType>(spec.Substitution);
        var dotIdx = spec.SourceFunc.Name.LastIndexOf('.');
        if (dotIdx > 0) {
          var ownerTypeName = spec.SourceFunc.Name[..dotIdx];
          if (module.TypeDefs.TryGetValue(ownerTypeName, out var ownerType))
            subMap.TryAdd("Self", ownerType);
          subMap.TryAdd(ownerTypeName, module.TypeDefs.GetValueOrDefault(ownerTypeName) ?? new IrStructType(ownerTypeName, []));
        }
        var typeSub = new InterfaceAliasTypeSubstitution(subMap);
        var clonedFunc = CloneWithInterfaceAliasSubstitution(spec.SourceFunc, spec.SpecializedName, typeSub);
        module.AddFunction(clonedFunc);
        Logger.Debug(LogCategory.Ir, $"Interface alias specialization (transitive): {spec.SourceFunc.Name} -> {spec.SpecializedName}");
        alreadySpecialized.Add(spec.SpecializedName);
      }

      if (extraRewrites.Count > 0) {
        funcLookup = module.Functions.ToDictionary(f => f.Name, f => f);
        foreach (var (block, opIndex, newCallee) in extraRewrites) {
          var op = block.Operations[opIndex];
          if (op is MaxonTryCallOp tryCall2) {
            var (rk, rst) = ResolveMonomorphizedResultType(tryCall2.ResultKind, tryCall2.ResultStructTypeName, newCallee, funcLookup);
            UpdateResultTypeName(tryCall2.Result, rst);
            var newOp = new MaxonTryCallOp(newCallee, tryCall2.Args, tryCall2.Result, tryCall2.ErrorFlag, rk, rst);
            CopyCallMetadata(tryCall2, newOp);
            block.Operations[opIndex] = newOp;
          } else if (op is MaxonCallOp call2) {
            var (rk, rst) = ResolveMonomorphizedResultType(call2.ResultKind, call2.ResultStructTypeName, newCallee, funcLookup);
            UpdateResultTypeName(call2.Result, rst);
            var newOp = new MaxonCallOp(newCallee, call2.Args, call2.Result, rk, rst);
            CopyCallMetadata(call2, newOp);
            block.Operations[opIndex] = newOp;
          } else {
            throw new InvalidOperationException($"Monomorphization rewrite: expected call op at block index {opIndex}, got {op.GetType().Name}");
          }
        }
      }

      if (extraSpecs.Count == 0) break;
    }

    // Remove dead functions with interface-typed params that are no longer called.
    // Iterate until stable: removing one dead function may make others unreachable.
    for (int removeRound = 0; removeRound < 20; removeRound++) {
      var referencedCallees = new HashSet<string>();
      foreach (var func in module.Functions) {
        // Skip functions that are themselves candidates for removal — their
        // references to other interface-param functions shouldn't keep them alive.
        bool isSelfCandidate = func.ParamTypes.Any(pt => pt is IrInterfaceType);
        if (isSelfCandidate) continue;
        foreach (var block in func.Body.Blocks) {
          foreach (var op in block.Operations) {
            if (op is MaxonCallOp call) referencedCallees.Add(call.Callee);
            else if (op is MaxonTryCallOp tryCall) referencedCallees.Add(tryCall.Callee);
            else if (op is MaxonClosureCreateOp closure) referencedCallees.Add(closure.FunctionName);
          }
        }
      }
      int removedCount = module.RemoveFunctionsWhere(f => {
        bool hasIfaceParam = false;
        for (int i = 0; i < f.ParamTypes.Count; i++) {
          if (f.ParamTypes[i] is IrInterfaceType) { hasIfaceParam = true; break; }
        }
        return hasIfaceParam && !referencedCallees.Contains(f.Name);
      });
      if (removedCount == 0) break;
    }
  }

  /// Minimal type substitution for interface alias specialization.
  /// Maps interface alias type names to concrete types for callee rewriting.
  private class InterfaceAliasTypeSubstitution(Dictionary<string, IrType> map) {
    public IrType SubstituteType(IrType type) {
      if (type is IrStructType st && map.TryGetValue(st.Name, out var newType))
        return newType;
      if (type is IrInterfaceType iface && map.TryGetValue(iface.Name, out var newIfaceType))
        return newIfaceType;
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

    public bool TryGetValue(string key, out IrType value) => map.TryGetValue(key, out value!);

    /// Check if the resolved "Element" type parameter (or named param) is bool.
    public bool IsBitPackedElement(string? typeParamName) {
      var paramName = typeParamName ?? "Element";
      return map.TryGetValue(paramName, out var resolved) && resolved == IrType.I1;
    }
  }

  /// Mutate a function in-place, replacing interface alias types/callees with concrete types.
  /// Avoids deep cloning by updating the existing function's name, signature, and op properties.
  private static void MutateInterfaceAliasTypes(
      IrFunction<MaxonOp> func, string newName, InterfaceAliasTypeSubstitution sub) {
    func.Name = newName;
    for (int i = 0; i < func.ParamTypes.Count; i++)
      func.ParamTypes[i] = sub.SubstituteType(func.ParamTypes[i]);
    if (func.ReturnType != null)
      func.ReturnType = sub.SubstituteType(func.ReturnType);

    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations)
        MutateOpTypes(op, sub);
    }
  }

  /// Mutate type names and callees on a single op in-place.
  private static void MutateOpTypes(MaxonOp op, InterfaceAliasTypeSubstitution sub) {
    switch (op) {
      case MaxonTryCallOp tryCall:
        tryCall.Callee = sub.SubstituteCallee(tryCall.Callee);
        if (tryCall.ResultStructTypeName != null)
          tryCall.ResultStructTypeName = sub.SubstituteName(tryCall.ResultStructTypeName);
        if (tryCall.Result is MaxonStruct tryRes) tryRes.TypeName = sub.SubstituteName(tryRes.TypeName);
        break;
      case MaxonCallOp call:
        call.Callee = sub.SubstituteCallee(call.Callee);
        if (call.ResultStructTypeName != null)
          call.ResultStructTypeName = sub.SubstituteName(call.ResultStructTypeName);
        if (call.Result is MaxonStruct callRes) callRes.TypeName = sub.SubstituteName(callRes.TypeName);
        break;
      case MaxonStructParamOp sp:
        sp.StructTypeName = sub.SubstituteName(sp.StructTypeName);
        sp.Result.TypeName = sub.SubstituteName(sp.Result.TypeName);
        break;
      case MaxonStructVarRefOp sv:
        sv.StructTypeName = sub.SubstituteName(sv.StructTypeName);
        sv.Result.TypeName = sub.SubstituteName(sv.Result.TypeName);
        break;
      case MaxonStructLiteralOp structLit:
        structLit.TypeName = sub.SubstituteName(structLit.TypeName);
        structLit.Result.TypeName = sub.SubstituteName(structLit.Result.TypeName);
        structLit.IsBitPacked = structLit.IsBitPacked || sub.IsBitPackedElement(null);
        break;
      case MaxonFieldAccessOp fa:
        fa.TypeName = sub.SubstituteName(fa.TypeName);
        if (fa.ResultStructTypeName != null)
          fa.ResultStructTypeName = sub.SubstituteName(fa.ResultStructTypeName);
        if (fa.Result is MaxonStruct faRes) faRes.TypeName = sub.SubstituteName(faRes.TypeName);
        break;
      case MaxonFieldAssignOp faa:
        faa.TypeName = sub.SubstituteName(faa.TypeName);
        break;
      case MaxonSizeofOp sz:
        sz.TypeName = sub.SubstituteName(sz.TypeName);
        break;
      case MaxonManagedMemClearOp memClear:
        memClear.IsBitPacked = memClear.IsBitPacked || sub.IsBitPackedElement(memClear.TypeParamName);
        break;
      case MaxonManagedMemAppendOp ma:
        ma.IsBitPacked = ma.IsBitPacked || sub.IsBitPackedElement(ma.TypeParamName);
        break;
      case MaxonManagedListInsertValueOp ci:
        ci.ValueKind = sub.SubstituteName(ci.ValueKind);
        break;
      case MaxonManagedListInsertRelativeValueOp cir:
        cir.ValueKind = sub.SubstituteName(cir.ValueKind);
        break;
      case MaxonManagedListRemoveOp crm:
        crm.ValueKind = sub.SubstituteName(crm.ValueKind);
        if (crm.Result is MaxonStruct crmRes) crmRes.TypeName = sub.SubstituteName(crmRes.TypeName);
        break;
      case MaxonManagedListNodeValueOp cnv:
        cnv.ValueKind = sub.SubstituteName(cnv.ValueKind);
        if (cnv.Result is MaxonStruct cnvRes) cnvRes.TypeName = sub.SubstituteName(cnvRes.TypeName);
        break;
      case MaxonManagedListNodeSetValueOp cns:
        cns.ValueKind = sub.SubstituteName(cns.ValueKind);
        break;
      case MaxonManagedListClearOp ccl:
        ccl.ValueKind = sub.SubstituteName(ccl.ValueKind);
        break;
      case MaxonManagedListNodePtrValueOp cpv:
        cpv.ValueKind = sub.SubstituteName(cpv.ValueKind);
        if (cpv.Result is MaxonStruct cpvRes) cpvRes.TypeName = sub.SubstituteName(cpvRes.TypeName);
        break;
    }
  }

  /// Clone a function replacing interface alias types/callees with concrete types.
  private static IrFunction<MaxonOp> CloneWithInterfaceAliasSubstitution(
      IrFunction<MaxonOp> source, string newName, InterfaceAliasTypeSubstitution sub) {
    var newParamTypes = source.ParamTypes.Select(t => sub.SubstituteType(t)).ToList();
    var newReturnType = source.ReturnType != null ? sub.SubstituteType(source.ReturnType) : null;

    var newFunc = new IrFunction<MaxonOp>(
      newName, [.. source.ParamNames], newParamTypes, newReturnType, source.ThrowsType) {
      IsStdlib = source.IsStdlib,
      SourceLine = source.SourceLine,
      SourceColumn = source.SourceColumn
    };

    // Clone blocks and operations with callee substitution
    var valueMap = new Dictionary<int, MaxonValue>();

    MaxonValue MapValue(MaxonValue old) {
      if (valueMap.TryGetValue(old.Id, out var mapped)) return mapped;
      var newId = IrContext.Current.NextId();
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
      case MaxonAssignOp assign: {
        var cloned = new MaxonAssignOp(assign.VarName, mapValue(assign.Value), assign.IsDeclaration, assign.IsMutable, assign.ValueKind) {
          OwnerFlags = assign.OwnerFlags,
          ForceHeap = assign.ForceHeap
        };
        return cloned;
      }
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
        return new MaxonPanicOp(p.Message, p.IsStdlib);
      case MaxonPanicDynamicOp pd:
        return new MaxonPanicDynamicOp((MaxonStruct)mapValue(pd.MessageStruct));
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
          ArrayLiteralCount = structLit.ArrayLiteralCount,
          IsBitPacked = structLit.IsBitPacked || sub.IsBitPackedElement(null)
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
      case MaxonManagedMemClearOp memClear:
        return new MaxonManagedMemClearOp(mapValue(memClear.ManagedStruct)) {
          IsStructElement = memClear.IsStructElement,
          StructElementTypeName = memClear.StructElementTypeName,
          TypeParamName = memClear.TypeParamName,
          IsBitPacked = memClear.IsBitPacked || sub.IsBitPackedElement(memClear.TypeParamName)
        };
      case MaxonManagedMemAppendOp ma:
        return new MaxonManagedMemAppendOp(mapValue(ma.ManagedStruct), mapValue(ma.Other)) {
          IsStructElement = ma.IsStructElement,
          TypeParamName = ma.TypeParamName,
          IsBitPacked = ma.IsBitPacked || sub.IsBitPackedElement(ma.TypeParamName)
        };
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
      case MaxonSizeofOp sz: { var c = new MaxonSizeofOp(sub.SubstituteName(sz.TypeName)); valueMap[sz.Result.Id] = c.Result; return c; }
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
      case MaxonEnumOrdinalOp eo: { var c = new MaxonEnumOrdinalOp(mapValue(eo.EnumValue), eo.EnumTypeName); valueMap[eo.Result.Id] = c.Result; return c; }
      case MaxonEnumNameOp en: { var c = new MaxonEnumNameOp(mapValue(en.EnumValue), en.EnumTypeName); valueMap[en.Result.Id] = c.Result; return c; }
      case MaxonEnumStringRawValueOp esr: { var c = new MaxonEnumStringRawValueOp(mapValue(esr.EnumValue), esr.EnumTypeName, esr.IsChar); valueMap[esr.Result.Id] = c.Result; return c; }
      case MaxonEnumStructRawValueOp esrv: { var c = new MaxonEnumStructRawValueOp(mapValue(esrv.EnumValue), esrv.EnumTypeName, esrv.StructTypeName); valueMap[esrv.Result.Id] = c.Result; return c; }
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
      // ManagedList (doubly-linked list) ops
      case MaxonManagedListCreateOp: { var c = new MaxonManagedListCreateOp(); valueMap[((MaxonManagedListCreateOp)op).Result.Id] = c.Result; return c; }
      case MaxonManagedListInsertValueOp ci: { var c = new MaxonManagedListInsertValueOp(mapValue(ci.ManagedList), mapValue(ci.Value), ci.AtHead, sub.SubstituteName(ci.ValueKind)); valueMap[ci.Result.Id] = c.Result; return c; }
      case MaxonManagedListInsertRelativeValueOp cir: { var c = new MaxonManagedListInsertRelativeValueOp(mapValue(cir.ManagedList), mapValue(cir.Target), mapValue(cir.Value), cir.After, sub.SubstituteName(cir.ValueKind)); valueMap[cir.Result.Id] = c.Result; return c; }
      case MaxonManagedListReinsertOp cr: return new MaxonManagedListReinsertOp(mapValue(cr.ManagedList), mapValue(cr.Node), cr.AtHead);
      case MaxonManagedListReinsertRelativeOp crr: return new MaxonManagedListReinsertRelativeOp(mapValue(crr.ManagedList), mapValue(crr.Target), mapValue(crr.Node), crr.After);
      case MaxonManagedListDetachOp cd: return new MaxonManagedListDetachOp(mapValue(cd.ManagedList), mapValue(cd.Node));
      case MaxonManagedListRemoveOp crm: {
        var newVK = sub.SubstituteName(crm.ValueKind);
        var newRK = sub.TryGetValue(crm.ValueKind, out var rvt) ? rvt.ToValueKind() : crm.ResultKind;
        var c = new MaxonManagedListRemoveOp(mapValue(crm.ManagedList), mapValue(crm.Node), newVK, newRK);
        valueMap[crm.Result.Id] = c.Result; return c;
      }
      case MaxonManagedListCountOp cc: { var c = new MaxonManagedListCountOp(mapValue(cc.ManagedList)); valueMap[cc.Result.Id] = c.Result; return c; }
      case MaxonManagedListNodeValueOp cnv: {
        var newVK = sub.SubstituteName(cnv.ValueKind);
        var newRK = sub.TryGetValue(cnv.ValueKind, out var nvt) ? nvt.ToValueKind() : cnv.ResultKind;
        var c = new MaxonManagedListNodeValueOp(mapValue(cnv.Node), newVK, newRK);
        valueMap[cnv.Result.Id] = c.Result; return c;
      }
      case MaxonManagedListNodeSetValueOp cns: return new MaxonManagedListNodeSetValueOp(mapValue(cns.Node), mapValue(cns.Value), sub.SubstituteName(cns.ValueKind));
      case MaxonManagedListClearOp ccl: return new MaxonManagedListClearOp(mapValue(ccl.ManagedList), sub.SubstituteName(ccl.ValueKind));
      case MaxonManagedListHeadPtrOp chp: { var c = new MaxonManagedListHeadPtrOp(mapValue(chp.ManagedList)); valueMap[chp.Result.Id] = c.Result; return c; }
      case MaxonManagedListNodePtrNextOp cpn: { var c = new MaxonManagedListNodePtrNextOp(mapValue(cpn.CursorPtr)); valueMap[cpn.Result.Id] = c.Result; return c; }
      case MaxonManagedListNodePtrValueOp cpv: {
        var newVK = sub.SubstituteName(cpv.ValueKind);
        var newRK = sub.TryGetValue(cpv.ValueKind, out var pvt) ? pvt.ToValueKind() : cpv.ResultKind;
        var c = new MaxonManagedListNodePtrValueOp(mapValue(cpv.CursorPtr), newVK, newRK);
        valueMap[cpv.Result.Id] = c.Result; return c;
      }

      case MaxonScopeEndOp scopeEnd:
        return new MaxonScopeEndOp(scopeEnd.VarsToClean, scopeEnd.KeepVars);
      case MaxonStringLiteralOp strLit: {
        var c = new MaxonStringLiteralOp(strLit.Value, strLit.StringTypeName);
        valueMap[strLit.Result.Id] = c.Result;
        return c;
      }
      case MaxonStringInterpOp interp: {
        var newParts = interp.Parts.Select(p => (p.IsLiteral, p.LiteralValue, p.ExprValue != null ? mapValue(p.ExprValue) : (MaxonValue?)null, p.FormatSpec, p.OptimalType)).ToList();
        var c = new MaxonStringInterpOp(newParts, interp.StringTypeName);
        valueMap[interp.Result.Id] = c.Result;
        return c;
      }
      case MaxonByteStringLiteralOp bstrLit: {
        var c = new MaxonByteStringLiteralOp(bstrLit.Value, bstrLit.ArrayTypeName);
        valueMap[bstrLit.Result.Id] = c.Result;
        return c;
      }

      case MaxonClosureCreateOp closureCreate: {
        var newCaptured = closureCreate.CapturedValues.Select(mapValue).ToList();
        var c = new MaxonClosureCreateOp(
          closureCreate.FunctionName,
          closureCreate.FunctionType,
          newCaptured,
          [.. closureCreate.CapturedNames],
          [.. closureCreate.CapturedKinds],
          [.. closureCreate.CapturedStructTypes]);
        valueMap[closureCreate.Result.Id] = c.Result;
        return c;
      }

      // Enum / union ops — mirror the FunctionCloner.cs handling.
      case MaxonEnumConstructOp ec: {
        var c = new MaxonEnumConstructOp(sub.SubstituteName(ec.EnumTypeName), ec.CaseName, ec.TagValue, [.. ec.Args.Select(mapValue)]);
        valueMap[ec.Result.Id] = c.Result;
        return c;
      }
      case MaxonEnumTagOp et: {
        var c = new MaxonEnumTagOp(mapValue(et.EnumValue), sub.SubstituteName(et.EnumTypeName));
        valueMap[et.Result.Id] = c.Result;
        return c;
      }
      case MaxonEnumPayloadAssignOp epa:
        return new MaxonEnumPayloadAssignOp(epa.EnumVarName, sub.SubstituteName(epa.EnumTypeName), epa.PayloadIndex, mapValue(epa.NewValue));
      case MaxonEnumPayloadOp payload: {
        var c = new MaxonEnumPayloadOp(mapValue(payload.EnumValue), sub.SubstituteName(payload.EnumTypeName), payload.PayloadIndex, payload.ResultKind, payload.ResultStructTypeName);
        valueMap[payload.Result.Id] = c.Result;
        return c;
      }

      default:
        throw new InvalidOperationException($"Interface alias specialization: unhandled op type {op.GetType().Name}");
    }
  }
}
