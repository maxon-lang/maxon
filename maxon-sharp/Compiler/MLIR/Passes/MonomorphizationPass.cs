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
    var hot = StageTimer.HotFunctions > 0 ? new List<(string, long)>() : null;
    var hotSw = hot != null ? new System.Diagnostics.Stopwatch() : null;

    // Sub-phase timing: when --timing is on, accumulate per-phase ms and print
    // a single line at the end. Cheap and zero-overhead when disabled.
    var subTimings = StageTimer.Enabled ? new Dictionary<string, long>() : null;
    var subSw = subTimings != null ? new System.Diagnostics.Stopwatch() : null;

    // Iterate until no new specializations are found (handles transitive type aliases
    // like Array with Entry where Entry is itself a type alias resolved during an earlier round)
    int round = 0;
    while (true) {
      // Collect all called methods each round (new cloned functions may add new call sites)
      subSw?.Restart();
      var calledMethods = CollectCalledMethodNames(module);
      if (subTimings != null) StageTimer.Record(subTimings, "collectCalled", subSw!.ElapsedMilliseconds);

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
      subSw?.Restart();
      var specializations = CollectNeededSpecializations(module, calledMethods);
      if (subTimings != null) StageTimer.Record(subTimings, "collectSpecs", subSw!.ElapsedMilliseconds);
      if (specializations.Count == 0) break;

      subSw?.Restart();
      var newFunctions = new List<IrFunction<MaxonOp>>();
      foreach (var spec in specializations) {
        if (spec.SourceFunc.Body.Blocks.Count == 0) {
          continue;
        }
        hotSw?.Restart();
        var clonedFunc = new FunctionCloner(spec.SourceFunc, spec.ConcreteTypeName, spec.TypeSubstitution, module.TypeAliasSources, module.TypeDefs).Clone();
        if (hotSw != null) hot!.Add((spec.SourceFunc.Name, hotSw.ElapsedMilliseconds));
        newFunctions.Add(clonedFunc);
      }
      if (subTimings != null) StageTimer.Record(subTimings, "clone", subSw!.ElapsedMilliseconds);

      subSw?.Restart();
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

      if (subTimings != null) StageTimer.Record(subTimings, "addFuncs", subSw!.ElapsedMilliseconds);

      allSpecializations.AddRange(specializations);
    }

    if (allSpecializations.Count > 0) {
      // Build alias source map for call rewriting fallback (e.g., IterStateArray -> Array)
      _aliasSourceMap = [];
      foreach (var (aliasName, info) in module.TypeAliasSources)
        _aliasSourceMap[aliasName] = info.SourceTypeName;
      subSw?.Restart();
      RewriteCallSites(module, allSpecializations);
      if (subTimings != null) StageTimer.Record(subTimings, "rewriteCalls", subSw!.ElapsedMilliseconds);
      _aliasSourceMap = null;
    }

    // Propagate concrete struct type names through every function so the
    // interface-alias specialization stage sees the right TypeName on call
    // args that traveled through field accesses on previously-specialized
    // call results. RewriteCallSites only invokes the propagation when it
    // performed at least one type-alias rewrite, which can leave callers
    // of interface-alias-only specializations un-propagated.
    {
      var sharedProv = new CalleeProvenanceCache(module);
      int propFuncs = 0;
      var propSw = subTimings != null ? System.Diagnostics.Stopwatch.StartNew() : null;
      foreach (var propFunc in module.Functions) {
        if (propFunc.IsBuiltinSynthetic) continue;
        PropagateStructTypeNames(propFunc, module, sharedProv);
        propFuncs++;
      }
      if (subTimings != null) {
        StageTimer.Record(subTimings, "propagateAll", propSw!.ElapsedMilliseconds);
        Console.Error.WriteLine($"    propagateAll: {propFuncs} funcs in {propSw.ElapsedMilliseconds}ms");
      }
    }

    // Stage 2 + 2b interleaved: Stage 2 specializes functions per
    // interface-typed param's concrete arg type. Stage 2b specializes
    // instance methods of structs with interface-typed fields per the
    // concrete type stored in each field at the call site.
    //
    // The two stages feed each other: a Stage 2 spec retypes a param,
    // which surfaces the concrete-field type of that arg's downstream
    // field-load → creating a Stage 2b spec. The Stage 2b clone retypes
    // self.field accesses, surfacing concrete arg types in deeper calls
    // → creating new Stage 2 specs. Iterate until both reach a fixed
    // point, then stop — without this, calls like applyColoring(regTarget:
    // self.regTarget) inside a Stage 2b-spec'd method are left targeting
    // the unspecialized callee with an interface-typed signature.
    subSw?.Restart();
    bool anyFieldSpecChange = true;
    int fieldSpecRound = 0;
    while (anyFieldSpecChange && fieldSpecRound++ < 20) {
      var roundStart = subTimings != null ? System.Diagnostics.Stopwatch.StartNew() : null;
      var aliasStart = subTimings != null ? System.Diagnostics.Stopwatch.StartNew() : null;
      bool aliasChanged = RunInterfaceAliasSpecialization(module);
      var aliasMs = aliasStart?.ElapsedMilliseconds ?? 0;
      var fieldStart = subTimings != null ? System.Diagnostics.Stopwatch.StartNew() : null;
      bool fieldChanged = RunInterfaceFieldSpecializationRound(module);
      var fieldMs = fieldStart?.ElapsedMilliseconds ?? 0;
      anyFieldSpecChange = aliasChanged || fieldChanged;
      if (subTimings != null) {
        Console.Error.WriteLine(
          $"    ifaceAlias+Field round {fieldSpecRound}: alias={aliasMs}ms(changed={aliasChanged}) field={fieldMs}ms(changed={fieldChanged}) total={roundStart!.ElapsedMilliseconds}ms");
      }
    }
    RemoveUnreachableInterfaceFunctions(module);
    if (subTimings != null) {
      StageTimer.Record(subTimings, "ifaceAlias+Field", subSw!.ElapsedMilliseconds);
      Console.Error.WriteLine($"    ifaceAlias+Field finished after {fieldSpecRound} rounds");
    }

    // Stage 3: Rewrite interface-qualified method callees (e.g. Producer.produce)
    // to the concrete method (Widget.produce) using the self-arg's current
    // concrete TypeName. This handles calls whose result flowed through a
    // variable bound from another function that returned an interface type.
    subSw?.Restart();
    RewriteInterfaceMethodCalls(module);
    if (subTimings != null) StageTimer.Record(subTimings, "ifaceMethods", subSw!.ElapsedMilliseconds);

    // RewriteCallSites, RunInterfaceAliasSpecialization, and
    // RewriteInterfaceMethodCalls rewrite callees on existing call ops. That's
    // an edge-content change the graph can't detect from structural hooks
    // alone, so invalidate explicitly before handing off to downstream passes.
    module.InvalidateCallGraph();

    if (hot != null) StageTimer.PrintHotFunctions("monomorph(clone)", hot);
    if (subTimings != null) Console.Error.WriteLine("  monomorph sub:" + StageTimer.Format(subTimings));
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

  /// <summary>
  /// Bundles the set of called method names with an index keyed by the last
  /// dot-segment of each name (the "method" part). IsMethodCalled resolves
  /// in O(callees-sharing-that-method-name) instead of O(all-callees).
  /// </summary>
  internal sealed class CalledMethodIndex {
    public HashSet<string> All { get; } = [];
    // Method-name (last dot segment) → callees whose last segment equals it.
    // Each callee appears under exactly one bucket. IsMethodCalled's suffix
    // checks only walk this smaller bucket.
    public Dictionary<string, List<string>> ByMethod { get; } = [];

    public void Add(string callee) {
      if (!All.Add(callee)) return;
      var dot = callee.LastIndexOf('.');
      var method = dot >= 0 ? callee[(dot + 1)..] : callee;
      if (!ByMethod.TryGetValue(method, out var list)) {
        list = [];
        ByMethod[method] = list;
      }
      list.Add(callee);
    }

    public bool Contains(string callee) => All.Contains(callee);
  }

  private static List<Specialization> CollectNeededSpecializations(
      IrModule<MaxonOp> module, CalledMethodIndex? calledMethods = null) {
    var specializations = new List<Specialization>();
    // (alias, source-func) pairs already in `specializations`, used to dedup
    // the per-method "is this combination already queued?" check below in O(1)
    // instead of an O(N) linear scan over `specializations`.
    var seenAliasFunc = new HashSet<(string, IrFunction<MaxonOp>)>();

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
          continue;
        }
      } else {
        continue;
      }

      // IsRecursiveTypeNesting depends only on (aliasName, sourceTypeName);
      // hoist it out of the per-method loop so we don't recompute the same
      // answer for every method of the same alias.
      if (IsRecursiveTypeNesting(aliasName, sourceTypeName, module.TypeAliasSources)) {
        continue;
      }

      // Defer TypeSubstitution.Build until we know at least one method needs specialization.
      // Build has side effects (FindConcreteAlias auto-creates type aliases), so calling it
      // eagerly for aliases with no demanded methods would create spurious aliases that
      // trigger infinite monomorphization cascades.
      TypeSubstitution? typeSubstitution = null;

      var sourcePrefix = $"{sourceTypeName}.";
      var sourceSuffix = $".{sourceTypeName}.";
      foreach (var func in module.FindMethodsByType(sourceTypeName)) {
        bool isSourceMethod = func.Name.StartsWith(sourcePrefix);
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

        var specializedName = $"{aliasName}.{methodName}";
        var existingFunc = module.FindFunctionByExactName(specializedName);
        if (existingFunc != null && existingFunc.Body.Blocks.Count > 0) continue;
        if (!seenAliasFunc.Add((aliasName, func))) continue;

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
      string fullFuncName, CalledMethodIndex calledMethods) {
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

    // Walk only callees that share the same method name (last dot segment).
    // Equivalent to the old "foreach (callee in calledMethods) if
    // callee.EndsWith(.methodName)" loop but without the O(all-callees) scan.
    if (!calledMethods.ByMethod.TryGetValue(methodName, out var candidates)) return false;
    var dotMethodLen = methodName.Length + 1; // length of ".methodName"
    foreach (var callee in candidates) {
      // Skip callees that are exactly `methodName` (no dot prefix) — they
      // don't end with ".methodName" and can't match the patterns below.
      if (callee.Length < dotMethodLen) continue;
      var typePrefix = callee[..^dotMethodLen];
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
  private static CalledMethodIndex CollectCalledMethodNames(IrModule<MaxonOp> module) {
    var called = new CalledMethodIndex();
    var graph = module.CallGraph;
    // The shared call graph already surfaces all demand-driven names this pass
    // needs: direct/async callees, function-refs, closure-creates, lazy-init
    // names, plus the Maxon-dialect iterator/clone demands. Drain that into
    // `called` per function — no need for a parallel op walk here.
    foreach (var func in module.Functions) {
      if (func.IsBuiltinSynthetic) continue;
      foreach (var refName in graph.GetReferencedNames(func))
        called.Add(refName);
    }

    // Resolve type alias callees: "OpRefList.push" -> also add "List.push".
    // Snapshot the set first so the iteration doesn't see the new additions.
    var aliasResolved = new List<string>();
    foreach (var callee in called.All) {
      var dotIdx = callee.LastIndexOf('.');
      if (dotIdx <= 0) continue;
      var typePart = callee[..dotIdx];
      var methodPart = callee[(dotIdx + 1)..];
      if (module.TypeAliasSources.TryGetValue(typePart, out var aliasInfo))
        aliasResolved.Add($"{aliasInfo.SourceTypeName}.{methodPart}");
    }
    foreach (var name in aliasResolved) called.Add(name);
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
      // PropagateStructTypeNames is expensive (inner fixed-point over all
      // blocks/ops). Skip the trailing propagation when this pass didn't
      // actually rewrite anything — most functions have zero rewrites.
      while (anyRewrites) {
        if (++rewritePass > 50) {
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
                }
              }
            }

            if (op is MaxonCallOp call) {
              var newCallee = ResolveCalleeRewrite(call.Callee, call.ResultStructTypeName, call.Args, calleeMap, siblingIndex);
              if (newCallee != null && newCallee != call.Callee) {
                anyRewrites = true;
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
        // After rewriting all blocks, propagate type names across the entire
        // function so variables defined in one block (e.g., entry) have their
        // concrete types visible in continuation blocks. Only worth doing when
        // *this pass* actually rewrote something — otherwise we're recomputing
        // the same fixed-point we already settled on the prior pass (or never
        // had any rewrites in the first place).
        if (anyRewrites) PropagateStructTypeNames(func, module);
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
  private static bool PropagateConcreteInterfaceReturns(
      IrModule<MaxonOp> module,
      Dictionary<string, IrFunction<MaxonOp>> funcLookup,
      CalleeProvenanceCache? provCache = null) {
    bool anyChange = false;
    bool changed = true;
    int safety = 0;
    // Reuse the caller's cache when available so we don't rebuild a full
    // module-wide callee body index inside every nested PropagateStructTypeNames
    // call (line below). When this function is invoked from RewriteInterfaceMethodCalls
    // there is no surrounding cache, so allocate a local one — building it once
    // here is still much cheaper than rebuilding it per propagated function.
    var sharedProv = provCache ?? new CalleeProvenanceCache(module);

    // Pre-pass: which functions even have call sites into iface-returning callees?
    // The vast majority of functions have no such calls, so they can be skipped
    // entirely on every outer round. The set is stable across rounds (we don't
    // change which funcs hold which call ops here, only the result TypeNames),
    // so we compute it once.
    var candidateFuncs = new List<IrFunction<MaxonOp>>();
    foreach (var func in module.Functions) {
      if (func.IsBuiltinSynthetic) continue;
      bool hasIfaceReturnCall = false;
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is not MaxonCallOp call) continue;
          if (call.Result is not MaxonStruct) continue;
          if (!funcLookup.TryGetValue(call.Callee, out var cf)) continue;
          if (cf.ReturnType is IrInterfaceType) { hasIfaceReturnCall = true; break; }
        }
        if (hasIfaceReturnCall) break;
      }
      if (hasIfaceReturnCall) candidateFuncs.Add(func);
    }
    if (candidateFuncs.Count == 0) return false;

    // Cache concrete-return inferences per callee so we don't re-infer the
    // same callee N times in the inner loop. Cache keyed by callee identity
    // (the IrFunction reference); inferences are stable within one call to
    // this method since callee bodies aren't being rewritten here.
    var concreteByCallee = new Dictionary<IrFunction<MaxonOp>, string?>(ReferenceEqualityComparer.Instance);

    while (changed && safety++ < 20) {
      changed = false;
      foreach (var func in candidateFuncs) {
        bool localChanged = false;
        foreach (var block in func.Body.Blocks) {
          foreach (var op in block.Operations) {
            if (op is not MaxonCallOp call) continue;
            if (call.Result is not MaxonStruct resultStruct) continue;
            if (!funcLookup.TryGetValue(call.Callee, out var calleeFunc)) continue;
            if (calleeFunc.ReturnType is not IrInterfaceType) continue;
            if (!concreteByCallee.TryGetValue(calleeFunc, out var concrete)) {
              concrete = InferConcreteInterfaceReturnFromFunc(calleeFunc);
              concreteByCallee[calleeFunc] = concrete;
            }
            if (concrete != null && resultStruct.TypeName != concrete) {
              resultStruct.TypeName = concrete;
              changed = true;
              localChanged = true;
              anyChange = true;
            }
          }
        }
        // PropagateStructTypeNames only has work to do when something in this
        // function's call results just changed. Skipping it for unchanged
        // functions saves the bulk of the work (the inner fixpoint walks
        // every block/op of the function multiple times).
        if (localChanged) PropagateStructTypeNames(func, module, sharedProv);
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

  /// Per-callee field-provenance shape: list of (paramIndex, fieldName) pairs
  /// indicating that the callee's returned struct stores parameter `paramIndex`
  /// into field `fieldName`. Computed once per callee from its body and reused
  /// for every call site — the call-site-specific work is just looking up the
  /// matching arg's TypeName, which is cheap.
  private sealed class CalleeProvenanceCache(IrModule<MaxonOp> module) {
    private readonly Dictionary<string, List<(int ParamIndex, string FieldName)>?> _cache = [];
    private readonly IrModule<MaxonOp> _module = module;

    /// Returns the param→field shape for `calleeName`, or null when the callee
    /// doesn't exist or doesn't return a struct via a `Self{f: param}` literal.
    public List<(int ParamIndex, string FieldName)>? Get(string calleeName) {
      if (_cache.TryGetValue(calleeName, out var cached)) return cached;
      var computed = ComputeShape(calleeName);
      _cache[calleeName] = computed;
      return computed;
    }

    private List<(int, string)>? ComputeShape(string calleeName) {
      var callee = _module.FindFunctionByExactName(calleeName);
      if (callee?.Body == null) return null;

      // Single-pass index of the callee's body. Previously each
      // ResolveParamIndex / FindStructLiteralFor call walked the whole
      // body, giving O(returnFields × bodyOps) per callee. With these
      // value-id maps the lookup is O(1) per field.
      Dictionary<int, int>? paramIndexById = null; // value-id → param index
      Dictionary<int, string>? varRefNameById = null; // value-id → varref's variable name
      Dictionary<string, int>? paramIndexByName = null; // var name → param index (for varref-of-param)
      Dictionary<int, MaxonStructLiteralOp>? literalById = null; // value-id → struct literal
      MaxonStruct? returnStruct = null;

      foreach (var block in callee.Body.Blocks) {
        foreach (var op in block.Operations) {
          switch (op) {
            case MaxonStructParamOp sp:
              paramIndexById ??= [];
              paramIndexById[sp.Result.Id] = sp.Index;
              paramIndexByName ??= [];
              paramIndexByName[sp.Name] = sp.Index;
              break;
            case MaxonParamOp p:
              paramIndexById ??= [];
              paramIndexById[p.Result.Id] = p.Index;
              paramIndexByName ??= [];
              paramIndexByName[p.Name] = p.Index;
              break;
            case MaxonStructVarRefOp vr:
              varRefNameById ??= [];
              varRefNameById[vr.Result.Id] = vr.VarName;
              break;
            case MaxonStructLiteralOp lit:
              literalById ??= [];
              literalById[lit.Result.Id] = lit;
              break;
            case MaxonReturnOp { Value: MaxonStruct rs }:
              returnStruct ??= rs;
              break;
          }
        }
      }

      if (returnStruct == null || literalById == null) return null;
      if (!literalById.TryGetValue(returnStruct.Id, out var literal)) return null;

      List<(int, string)>? shape = null;
      foreach (var (fieldName, fieldValue) in literal.FieldValues) {
        int? paramIndex = null;
        if (paramIndexById != null && paramIndexById.TryGetValue(fieldValue.Id, out var direct)) {
          paramIndex = direct;
        } else if (varRefNameById != null && varRefNameById.TryGetValue(fieldValue.Id, out var varName)
            && paramIndexByName != null && paramIndexByName.TryGetValue(varName, out var byName)) {
          paramIndex = byName;
        }
        if (paramIndex == null) continue;
        shape ??= [];
        shape.Add((paramIndex.Value, fieldName));
      }
      return shape;
    }
  }

  /// <summary>
  /// After call rewrites, propagate concrete struct type names through assignment chains
  /// across ALL blocks in a function. Variables flow across blocks (e.g., a variable
  /// assigned in entry can be referenced in otherwise_continue_1), so propagation must
  /// span the entire function body.
  /// </summary>
  private static void PropagateStructTypeNames(
      IrFunction<MaxonOp> func, IrModule<MaxonOp>? module = null,
      CalleeProvenanceCache? provCache = null) {
    // Map: variable name -> concrete struct type name
    var varTypes = new Dictionary<string, string>();
    // Map: value ID -> concrete struct type name
    var valueTypes = new Dictionary<int, string>();
    // Map: (struct-value-id, field-name) -> concrete struct type stored in
    // that field. Populated by inspecting constructor calls — when a callee
    // returns a struct via `Self{field: arg}`, we propagate the call site's
    // concrete arg type to the result struct's field. Read by field-access
    // propagation below to recover the concrete type of an interface-typed
    // field whose declared type is the interface itself.
    var fieldProvenance = new Dictionary<(int StructId, string FieldName), string>();
    // Secondary index by struct-value-id — lets the varref alias-propagation
    // step look up "all field-provenance entries for source value X" in O(1)
    // instead of linear-scanning the whole fieldProvenance dict per varref.
    var fieldProvenanceByStruct = new Dictionary<int, Dictionary<string, string>>();
    // Map: variable name -> list of value IDs assigned to it. Lets a varref
    // for `h` find the original call result it aliases, so field-provenance
    // entries recorded for that call result transfer to the varref's id.
    var varSourceIds = new Dictionary<string, List<int>>();
    // Use the caller-supplied cache when available; otherwise build a local
    // one. The cache memoizes per-callee body inspection across every call
    // site — without it, the callee's blocks would be re-walked for every
    // caller and every fixed-point round.
    var localProvCache = provCache ?? (module != null ? new CalleeProvenanceCache(module) : null);

    void AddProvenance(int structId, string fieldName, string concrete) {
      var key = (structId, fieldName);
      if (fieldProvenance.TryGetValue(key, out var existing) && existing == concrete) return;
      fieldProvenance[key] = concrete;
      if (!fieldProvenanceByStruct.TryGetValue(structId, out var byField)) {
        byField = [];
        fieldProvenanceByStruct[structId] = byField;
      }
      byField[fieldName] = concrete;
    }

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
          // Re-run field-provenance population on each pass so it observes
          // call args whose TypeNames were filled in by an earlier round
          // (e.g. h's TypeName via the call result, then h.t's TypeName
          // via this provenance lookup).
          if (op is MaxonCallOp opCall && opCall.Result is MaxonStruct opCallResult
              && localProvCache != null) {
            var shape = localProvCache.Get(opCall.Callee);
            if (shape != null) {
              foreach (var (paramIndex, fieldName) in shape) {
                if (paramIndex >= opCall.Args.Count) continue;
                if (opCall.Args[paramIndex] is not MaxonStruct argStruct) continue;
                if (string.IsNullOrEmpty(argStruct.TypeName)) continue;
                var key = (opCallResult.Id, fieldName);
                if (!fieldProvenance.TryGetValue(key, out var existing) || existing != argStruct.TypeName) {
                  AddProvenance(opCallResult.Id, fieldName, argStruct.TypeName);
                  changed = true;
                }
              }
            }
          }
          if (op is MaxonAssignOp assign) {
            if (valueTypes.TryGetValue(assign.Value.Id, out var concreteType)) {
              if (!varTypes.ContainsKey(assign.VarName)) {
                varTypes[assign.VarName] = concreteType;
                changed = true;
              }
            }
            // Track all source value IDs assigned to this variable so a
            // later varref can recover field-provenance entries recorded
            // against the source value.
            if (!varSourceIds.TryGetValue(assign.VarName, out var idList)) {
              idList = [];
              varSourceIds[assign.VarName] = idList;
            }
            if (!idList.Contains(assign.Value.Id)) {
              idList.Add(assign.Value.Id);
              changed = true;
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
              // Propagate field-provenance entries through the var alias.
              // A varref reads back the same struct value that was assigned
              // into the variable, so any fields whose concrete types we
              // recorded for that source value also describe this read.
              // Uses the by-struct index so we don't linear-scan the full
              // fieldProvenance dict for each varref.
              if (varSourceIds.TryGetValue(varRef.VarName, out var sourceIds)) {
                foreach (var sourceId in sourceIds) {
                  if (!fieldProvenanceByStruct.TryGetValue(sourceId, out var sourceEntries)) continue;
                  foreach (var (fieldName, concrete) in sourceEntries) {
                    if (fieldProvenance.ContainsKey((varRef.Result.Id, fieldName))) continue;
                    AddProvenance(varRef.Result.Id, fieldName, concrete);
                    changed = true;
                  }
                }
              }
            }
          }
          // Propagate through field access: prefer per-field provenance
          // (recorded from constructor args) over the field's declared type.
          // The declared-type fallback only fires for struct-typed fields —
          // interface-typed fields can't be resolved from the declaration,
          // they need the construction-site concrete type to dispatch.
          if (op is MaxonFieldAccessOp fieldAccess && fieldAccess.Result is MaxonStruct fieldResult) {
            string? resolvedTypeName = null;
            if (fieldProvenance.TryGetValue((fieldAccess.StructValue.Id, fieldAccess.FieldName), out var provType)) {
              resolvedTypeName = provType;
            } else if (valueTypes.TryGetValue(fieldAccess.StructValue.Id, out var structType)
                && module?.TypeDefs.TryGetValue(structType, out var structDef) == true
                && structDef is IrStructType structSt) {
              var field = structSt.GetField(fieldAccess.FieldName);
              if (field?.Type is IrStructType fieldStructType) {
                resolvedTypeName = fieldStructType.Name;
              }
            }
            if (resolvedTypeName != null && fieldResult.TypeName != resolvedTypeName) {
              fieldResult.TypeName = resolvedTypeName;
              valueTypes[fieldResult.Id] = resolvedTypeName;
              changed = true;
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

  /// Stage 2b: specialize instance methods of structs that have interface-
  /// typed fields, per the concrete types stored in those fields at each
  /// call site. Field provenance is recovered by walking constructor calls
  /// and remembering which call arg flows into which struct field. When
  /// `obj.method()` is called and the field provenance gives a concrete
  /// type for one or more interface fields, clone the method with a
  /// substitution that retypes every `field_access .field` against `self`
  /// to the concrete type — so downstream interface-method dispatch on
  /// the field resolves statically.
  ///
  /// Iterates to a fixed point: a freshly-cloned method's body contains
  /// new instance method calls whose self-arg now has propagated field
  /// provenance from upstream specs.
  private static bool RunInterfaceFieldSpecializationRound(IrModule<MaxonOp> module) {
    var typeDefs = module.TypeDefs;
    bool anyChange = false;

    // Collect per-function field provenance for every struct value, so
    // call-site scans can look up the concrete types stored in a self
    // arg's interface-typed fields. Share one CalleeProvenanceCache across
    // every function in this round — the cache memoizes per-callee body
    // inspection, which is the dominant cost of provenance computation.
    var provCache = new CalleeProvenanceCache(module);
    var perFuncProvenance = new Dictionary<IrFunction<MaxonOp>, Dictionary<int, Dictionary<string, string>>>();
    var traceSw = StageTimer.Enabled ? System.Diagnostics.Stopwatch.StartNew() : null;
    int scannedFuncs = 0;
    foreach (var func in module.Functions) {
      if (func.IsBuiltinSynthetic) continue;
      scannedFuncs++;
      var prov = ComputeFieldProvenance(func, module, provCache);
      if (prov.Count > 0) perFuncProvenance[func] = prov;
    }
    if (StageTimer.Enabled) {
      Console.Error.WriteLine(
        $"      fieldSpec.computeProvenance: {scannedFuncs} funcs in {traceSw!.ElapsedMilliseconds}ms, {perFuncProvenance.Count} produced provenance");
    }
    if (perFuncProvenance.Count == 0) return false;

    var rewrites = new List<(IrBlock<MaxonOp> Block, int OpIndex, string NewCallee, MaxonValue Result)>();
    var newSpecs = new Dictionary<string, (IrFunction<MaxonOp> Source, Dictionary<string, IrType> Substitution)>();

    foreach (var (func, prov) in perFuncProvenance) {
      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          var (callee, args, result) = ExtractCallTarget(block.Operations[i]);
          if (callee == null || args == null) continue;
          if (args.Count == 0) continue;
          if (args[0] is not MaxonStruct selfArg) continue;
          if (!prov.TryGetValue(selfArg.Id, out var fieldMap)) continue;

          // The callee must be an instance method whose owner type has at
          // least one of the fields whose concrete type we know. Skip
          // already-specialized names (containing `$` after the last dot)
          // — these belong to a previous round of this pass and the spec
          // body already retypes the relevant field accesses.
          var dotIdx = callee.LastIndexOf('.');
          if (dotIdx <= 0) continue;
          if (callee.IndexOf('$', dotIdx) >= 0) continue;
          var ownerName = callee[..dotIdx];
          if (!typeDefs.TryGetValue(ownerName, out var ownerType)) continue;
          if (ownerType is not IrStructType ownerStruct) continue;

          var subMap = new Dictionary<string, IrType>();
          var nameParts = new List<string>();
          foreach (var (fieldName, concreteName) in fieldMap.OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
            var field = ownerStruct.GetField(fieldName);
            if (field == null) continue;
            string? declaredName = field.Type switch {
              IrInterfaceType iface => iface.Name,
              IrStructType { IsInterfaceAlias: true } st => st.Name,
              _ => null
            };
            if (declaredName == null) continue;
            if (concreteName == declaredName) continue;
            if (!typeDefs.TryGetValue(concreteName, out var concrete)) continue;
            if (concrete is IrInterfaceType) continue;
            if (subMap.TryGetValue(declaredName, out var existing) && existing.Name != concreteName) continue;
            subMap[declaredName] = concrete;
            nameParts.Add(concreteName);
          }
          if (subMap.Count == 0) continue;

          var specName = $"{callee}${string.Join("$", nameParts)}";
          if (specName == callee) continue;
          // Skip if the spec already exists in the module from an earlier round.
          if (module.FindFunctionByExactName(specName) != null) {
            rewrites.Add((block, i, specName, result!));
            continue;
          }
          if (!newSpecs.ContainsKey(specName)) {
            var sourceFunc = module.FindFunctionByExactName(callee);
            if (sourceFunc == null) continue;
            newSpecs[specName] = (sourceFunc, subMap);
          }
          rewrites.Add((block, i, specName, result!));
        }
      }
    }

    var cloneSw = StageTimer.Enabled ? System.Diagnostics.Stopwatch.StartNew() : null;
    foreach (var (specName, (source, subMap)) in newSpecs) {
      var typeSub = new InterfaceAliasTypeSubstitution(subMap);
      var cloned = CloneWithInterfaceAliasSubstitution(source, specName, typeSub);
      module.AddFunction(cloned);
      anyChange = true;
    }
    if (StageTimer.Enabled && newSpecs.Count > 0) {
      Console.Error.WriteLine(
        $"      fieldSpec.clone: {newSpecs.Count} new specs, {rewrites.Count} call rewrites in {cloneSw!.ElapsedMilliseconds}ms");
    }

    var funcLookup = module.Functions.ToDictionary(f => f.Name, f => f);
    foreach (var (block, opIndex, newCallee, _) in rewrites) {
      var op = block.Operations[opIndex];
      if (op is MaxonTryCallOp tryCall && tryCall.Callee != newCallee) {
        var (rk, rst) = ResolveMonomorphizedResultType(tryCall.ResultKind, tryCall.ResultStructTypeName, newCallee, funcLookup);
        UpdateResultTypeName(tryCall.Result, rst);
        var newOp = new MaxonTryCallOp(newCallee, tryCall.Args, tryCall.Result, tryCall.ErrorFlag, rk, rst);
        CopyCallMetadata(tryCall, newOp);
        block.Operations[opIndex] = newOp;
        anyChange = true;
      } else if (op is MaxonCallOp call && call.Callee != newCallee) {
        var (rk, rst) = ResolveMonomorphizedResultType(call.ResultKind, call.ResultStructTypeName, newCallee, funcLookup);
        UpdateResultTypeName(call.Result, rst);
        var newOp = new MaxonCallOp(newCallee, call.Args, call.Result, rk, rst);
        CopyCallMetadata(call, newOp);
        block.Operations[opIndex] = newOp;
        anyChange = true;
      }
    }

    if (anyChange) {
      module.InvalidateFunctionIndex();
      module.InvalidateCallGraph();
    }
    return anyChange;
  }

  /// Inspect a single op and return its callee/args/result triple if it's
  /// a call op, or all-null otherwise. Centralizes the MaxonCallOp /
  /// MaxonTryCallOp branching that otherwise spreads across every scan
  /// site in this pass.
  private static (string? Callee, List<MaxonValue>? Args, MaxonValue? Result) ExtractCallTarget(MaxonOp op) {
    if (op is MaxonCallOp call) return (call.Callee, call.Args, call.Result);
    if (op is MaxonTryCallOp tryCall) return (tryCall.Callee, tryCall.Args, tryCall.Result);
    return (null, null, null);
  }

  /// Build a per-struct-value-id field-provenance map for a function:
  /// for each MaxonStruct value `obj`, record which fields hold what
  /// concrete struct type, traced from constructor calls (MaxonCallOp
  /// returning a struct whose body produces `Self{f: param}`). Aliases
  /// through MaxonStructVarRefOp transfer entries to the alias's value
  /// id so a later read of the same variable still finds the provenance.
  private static Dictionary<int, Dictionary<string, string>> ComputeFieldProvenance(
      IrFunction<MaxonOp> func, IrModule<MaxonOp> module, CalleeProvenanceCache cache) {
    var result = new Dictionary<int, Dictionary<string, string>>();
    // HashSet rather than List<int> so the per-op `idList.Contains` lookup is
    // O(1) instead of O(varSourceIds.Count) — a hot path when a function has
    // many assignments to the same variable.
    var varSourceIds = new Dictionary<string, HashSet<int>>();

    // Seed: every call returning a struct contributes provenance from the
    // callee's body, mapping (resultId, fieldName) → concrete type from the
    // call's matching arg.
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is not MaxonCallOp call || call.Result is not MaxonStruct callRes) continue;
        var shape = cache.Get(call.Callee);
        if (shape == null) continue;
        Dictionary<string, string>? entries = null;
        foreach (var (paramIndex, fieldName) in shape) {
          if (paramIndex >= call.Args.Count) continue;
          if (call.Args[paramIndex] is not MaxonStruct argStruct) continue;
          if (string.IsNullOrEmpty(argStruct.TypeName)) continue;
          if (entries == null) {
            if (!result.TryGetValue(callRes.Id, out entries)) {
              entries = [];
              result[callRes.Id] = entries;
            }
          }
          entries[fieldName] = argStruct.TypeName;
        }
      }
    }

    // Also seed self-field provenance from observed field-access ops. When
    // a Stage 2b spec retypes `field_access .field self` results to a
    // concrete type, that's the only surviving record of the fact that
    // `self.field` holds that concrete type. Recover it by scanning every
    // MaxonStructParamOp's field-access uses and matching the field's
    // declared type (interface) against the access result (concrete).
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is not MaxonFieldAccessOp fa) continue;
        if (fa.Result is not MaxonStruct faRes) continue;
        if (string.IsNullOrEmpty(faRes.TypeName)) continue;
        if (!module.TypeDefs.TryGetValue(fa.TypeName, out var ownerType)) continue;
        if (ownerType is not IrStructType ownerStruct) continue;
        var field = ownerStruct.GetField(fa.FieldName);
        if (field == null) continue;
        // Only meaningful when the declared type is interface but the
        // access result has a concrete type — that gap is how Stage 2b
        // marked the spec'd field as "this concrete impl is what's stored".
        bool declaredIsInterface = field.Type is IrInterfaceType
            || (field.Type is IrStructType { IsInterfaceAlias: true });
        if (!declaredIsInterface) continue;
        if (faRes.TypeName == field.Type.Name) continue;
        if (!module.TypeDefs.TryGetValue(faRes.TypeName, out var resolved)) continue;
        if (resolved is IrInterfaceType) continue;
        if (!result.TryGetValue(fa.StructValue.Id, out var entries)) {
          entries = [];
          result[fa.StructValue.Id] = entries;
        }
        entries[fa.FieldName] = faRes.TypeName;
      }
    }

    // Seed varSourceIds for params that are exposed as variables (instance
    // method `self`, plus any explicit struct/value params). The parser
    // declares each param as a variable of the same name without ever
    // emitting an explicit MaxonAssignOp, so without this seeding a varref
    // for "self" would have no path back to the struct_param's id.
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        switch (op) {
          case MaxonStructParamOp sp:
            if (!varSourceIds.TryGetValue(sp.Name, out var spIds)) {
              spIds = [];
              varSourceIds[sp.Name] = spIds;
            }
            spIds.Add(sp.Result.Id);
            break;
          case MaxonParamOp p:
            if (!varSourceIds.TryGetValue(p.Name, out var pIds)) {
              pIds = [];
              varSourceIds[p.Name] = pIds;
            }
            pIds.Add(p.Result.Id);
            break;
        }
      }
    }

    // Pre-collect assigns and varrefs once. The previous code re-scanned
    // every block's full op list on each fixed-point iteration; with these
    // lists we only walk the lightweight "interesting" ops. This is the
    // dominant cost when a function has thousands of ops but only a few
    // dozen assigns/varrefs.
    var assigns = new List<MaxonAssignOp>();
    var varRefs = new List<MaxonStructVarRefOp>();
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonAssignOp a) assigns.Add(a);
        else if (op is MaxonStructVarRefOp v) varRefs.Add(v);
      }
    }

    // Apply all assigns once up-front — they don't depend on each other,
    // so a single pass is enough to reach the fixed point of varSourceIds.
    foreach (var assign in assigns) {
      if (!varSourceIds.TryGetValue(assign.VarName, out var idSet)) {
        idSet = [];
        varSourceIds[assign.VarName] = idSet;
      }
      idSet.Add(assign.Value.Id);
    }

    // Propagate provenance through MaxonStructVarRefOp aliases. Iterate to
    // a fixed point because a varref's transferred entries may themselves
    // become source entries for a downstream varref reading the same id.
    bool changed = true;
    int iter = 0;
    while (changed && iter++ < 20) {
      changed = false;
      foreach (var varRef in varRefs) {
        if (!varSourceIds.TryGetValue(varRef.VarName, out var sourceIds)) continue;
        foreach (var sourceId in sourceIds) {
          if (!result.TryGetValue(sourceId, out var entries)) continue;
          if (!result.TryGetValue(varRef.Result.Id, out var existing)) {
            result[varRef.Result.Id] = new Dictionary<string, string>(entries);
            changed = true;
            continue;
          }
          foreach (var (k, v) in entries) {
            if (existing.TryAdd(k, v)) changed = true;
          }
        }
      }
    }

    return result;
  }

  /// Build the iface-funcs map: every function with at least one
  /// interface-alias or direct-interface parameter, with each such
  /// param's (index, alias-name) pair. Used by the interface-alias
  /// specialization pass and re-collected after every transitive round
  /// (callee bodies are rewritten in place, so the set drifts).
  private static Dictionary<string, (IrFunction<MaxonOp> Func, List<(int Index, string AliasName)> Params)>
      CollectInterfaceParamFuncs(IrModule<MaxonOp> module, Dictionary<string, IrStructType> interfaceAliases) {
    var result = new Dictionary<string, (IrFunction<MaxonOp>, List<(int, string)>)>();
    foreach (var func in module.Functions) {
      List<(int, string)>? ifaceParams = null;
      for (int i = 0; i < func.ParamTypes.Count; i++) {
        var paramType = func.ParamTypes[i];
        string? aliasName = paramType switch {
          IrStructType paramSt when interfaceAliases.ContainsKey(paramSt.Name) => paramSt.Name,
          IrInterfaceType paramIface => paramIface.Name,
          _ => null
        };
        if (aliasName == null) continue;
        ifaceParams ??= [];
        ifaceParams.Add((i, aliasName));
      }
      if (ifaceParams != null) result[func.Name] = (func, ifaceParams);
    }
    return result;
  }

  /// Returns true if any specialization clones or call-site rewrites
  /// were performed. Lets the outer Stage 2/2b loop short-circuit when
  /// neither stage produces new work.
  private static bool RunInterfaceAliasSpecialization(IrModule<MaxonOp> module) {
    var subTimings = StageTimer.Enabled ? new Dictionary<string, long>() : null;
    var sw = subTimings != null ? new System.Diagnostics.Stopwatch() : null;
    sw?.Restart();

    // Find all interface alias types
    var interfaceAliases = new Dictionary<string, IrStructType>();
    foreach (var (name, type) in module.TypeDefs) {
      if (type is IrStructType st && st.IsInterfaceAlias)
        interfaceAliases[name] = st;
    }
    if (interfaceAliases.Count == 0) return false;

    // Find functions with interface alias or direct interface parameter types
    var ifaceFuncs = CollectInterfaceParamFuncs(module, interfaceAliases);
    if (ifaceFuncs.Count == 0) return false;
    bool madeAnyChange = false;

    if (subTimings != null) { StageTimer.Record(subTimings, "ia.findIfaces", sw!.ElapsedMilliseconds); sw.Restart(); }

    // Scan call sites to determine concrete arg types
    var specs = new List<InterfaceAliasSpec>();
    var seenSpecNames = new HashSet<string>();
    var callSiteRewrites = new List<(IrBlock<MaxonOp> Block, int OpIndex, string NewCallee)>();

    // Reading ops only; no mutations to Functions during this loop, so iterate
    // the underlying list directly instead of taking a defensive copy.
    foreach (var func in module.Functions) {
      if (func.IsBuiltinSynthetic) continue;

      foreach (var block in func.Body.Blocks) {
        for (int i = 0; i < block.Operations.Count; i++) {
          var (callee, args, _) = ExtractCallTarget(block.Operations[i]);
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

          // Dedupe specializations by name — previously an O(N) linear scan
          // over `specs`, making the whole pass quadratic in call-site count.
          if (seenSpecNames.Add(specializedName)) {
            specs.Add(new InterfaceAliasSpec(ifaceInfo.Func, specializedName, substitution));
          }
          callSiteRewrites.Add((block, i, specializedName));
        }
      }
    }

    if (subTimings != null) { StageTimer.Record(subTimings, "ia.scan1", sw!.ElapsedMilliseconds); sw.Restart(); }

    if (specs.Count == 0) {
      if (subTimings != null) Console.Error.WriteLine("    iface sub:" + StageTimer.Format(subTimings));
      return madeAnyChange;
    }
    madeAnyChange = true;

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
        } else {
          var clonedFunc = CloneWithInterfaceAliasSubstitution(spec.SourceFunc, spec.SpecializedName, typeSub);
          module.AddFunction(clonedFunc);
        }
      }
    }

    if (subTimings != null) { StageTimer.Record(subTimings, "ia.cloneSpecs", sw!.ElapsedMilliseconds); sw.Restart(); }

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

    if (subTimings != null) { StageTimer.Record(subTimings, "ia.rewriteCalls", sw!.ElapsedMilliseconds); sw.Restart(); }

    // Remove stub functions (interface alias method stubs with no body)
    module.RemoveFunctionsWhere(f => {
      var dotIdx = f.Name.LastIndexOf('.');
      if (dotIdx <= 0) return false;
      var typePart = f.Name[..dotIdx];
      return interfaceAliases.ContainsKey(typePart) && f.Body.Blocks.Count == 0;
    });

    if (subTimings != null) { StageTimer.Record(subTimings, "ia.removeStubs", sw!.ElapsedMilliseconds); sw.Restart(); }

    // --- Transitive specialization ---
    // The first pass may have created specialized functions whose bodies
    // still contain calls to functions with interface parameters. Run
    // additional clone-only passes until no new specializations are found.
    var alreadySpecialized = new HashSet<string>(specs.Select(s => s.SpecializedName));
    // funcLookup was built once above (line 1113) and kept incrementally in sync
    // with module.AddFunction calls below. Avoids rebuilding an O(Functions)
    // dictionary on every transitive-spec round.
    long propagateMs = 0, rediscoverMs = 0, scanMs = 0, cloneMs = 0, rewriteMs = 0;
    var roundSw = subTimings != null ? new System.Diagnostics.Stopwatch() : null;
    int actualRounds = 0;
    // Track which functions had call sites rewritten in the most recent round,
    // plus newly-cloned functions, so we can re-propagate just those instead of
    // rescanning all 14k+ functions in the module each round. Null = first
    // round, which still does a full module scan.
    HashSet<IrFunction<MaxonOp>>? dirtyFuncs = null;
    for (int extraRound = 0; extraRound < 20; extraRound++) {
      actualRounds++;
      // Before re-scanning, propagate concrete types through calls that
      // return interface types — their result value's TypeName was left as
      // the interface name after cloning, which blocks downstream transitive
      // specialization because iface params get skipped when the arg type is
      // itself an interface. PropagateConcreteInterfaceReturns updates those
      // TypeNames using the callee's inferred concrete return type. After
      // that, propagate concrete struct type names across each function so
      // field-provenance recorded for one specialization's call results
      // surfaces concrete types on field-load arguments to deeper calls
      // (e.g. self.regTarget reads the X64RegAllocTarget that was stored
      // by the FunctionRegAllocator.create specialization).
      roundSw?.Restart();
      var transitiveProv = new CalleeProvenanceCache(module);
      PropagateConcreteInterfaceReturns(module, funcLookup, transitiveProv);
      if (dirtyFuncs == null) {
        // First round: propagate every function — we don't yet know which
        // ones were touched by Stage 1 / earlier passes.
        foreach (var propFunc in module.Functions) {
          if (propFunc.IsBuiltinSynthetic) continue;
          PropagateStructTypeNames(propFunc, module, transitiveProv);
        }
      } else {
        // Subsequent rounds: only propagate functions whose call sites were
        // rewritten or whose body was freshly cloned in the previous round.
        // Other functions can't have new propagation work to do.
        foreach (var propFunc in dirtyFuncs) {
          if (propFunc.IsBuiltinSynthetic) continue;
          PropagateStructTypeNames(propFunc, module, transitiveProv);
        }
      }
      if (roundSw != null) propagateMs += roundSw.ElapsedMilliseconds;

      // Re-discover functions with interface params
      roundSw?.Restart();
      ifaceFuncs = CollectInterfaceParamFuncs(module, interfaceAliases);
      if (ifaceFuncs.Count == 0) break;
      if (roundSw != null) rediscoverMs += roundSw.ElapsedMilliseconds;

      roundSw?.Restart();
      var extraSpecs = new List<InterfaceAliasSpec>();
      var extraSpecNames = new HashSet<string>();
      var extraRewrites = new List<(IrBlock<MaxonOp> Block, int OpIndex, string NewCallee)>();
      // Track callers whose call sites we touched this round so the next
      // round's PropagateStructTypeNames pass only revisits the dirty set.
      var nextDirty = new HashSet<IrFunction<MaxonOp>>();

      // Use the shared call graph to find caller functions of each iface
      // callee, then walk only those callers' ops. The full-module rescan was
      // O(funcs × blocks × ops); this is O(iface-callers × their-blocks × ops)
      // which is dramatically smaller in practice (interface-using callers
      // are a small fraction of the module).
      var callersToScan = new HashSet<IrFunction<MaxonOp>>();
      var graph = module.CallGraph;
      foreach (var ifaceName in ifaceFuncs.Keys) {
        foreach (var caller in graph.GetCallers(ifaceName)) {
          if (caller.IsBuiltinSynthetic) continue;
          callersToScan.Add(caller);
        }
      }
      foreach (var func in callersToScan) {
        foreach (var block in func.Body.Blocks) {
          for (int i = 0; i < block.Operations.Count; i++) {
            var (callee, args, _) = ExtractCallTarget(block.Operations[i]);
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
              nextDirty.Add(func);
              continue;
            }
            if (extraSpecNames.Add(specializedName2))
              extraSpecs.Add(new InterfaceAliasSpec(ifaceInfo2.Func, specializedName2, substitution2));
            extraRewrites.Add((block, i, specializedName2));
            nextDirty.Add(func);
          }
        }
      }

      if (extraSpecs.Count == 0 && extraRewrites.Count == 0) break;
      if (roundSw != null) scanMs += roundSw.ElapsedMilliseconds;

      roundSw?.Restart();
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
        funcLookup[clonedFunc.Name] = clonedFunc; // keep hoisted lookup in sync
        alreadySpecialized.Add(spec.SpecializedName);
        // Newly cloned bodies haven't been propagated yet — mark them dirty
        // for the next round.
        nextDirty.Add(clonedFunc);
      }
      if (roundSw != null) cloneMs += roundSw.ElapsedMilliseconds;

      roundSw?.Restart();
      if (extraRewrites.Count > 0) {
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

      if (roundSw != null) rewriteMs += roundSw.ElapsedMilliseconds;

      // Set up the dirty set for the next round's PropagateStructTypeNames.
      // Functions whose call sites we just rewrote have new concrete types
      // flowing through their bodies; cloned-spec bodies are completely new.
      dirtyFuncs = nextDirty;

      if (extraSpecs.Count == 0) break;
    }

    if (subTimings != null) {
      StageTimer.Record(subTimings, "ia.transitive", sw!.ElapsedMilliseconds);
      Console.Error.WriteLine(
        $"      transitive[{actualRounds}r]: propagate={propagateMs}ms rediscover={rediscoverMs}ms scan={scanMs}ms clone={cloneMs}ms rewrite={rewriteMs}ms");
      sw.Restart();
    }

    if (subTimings != null) {
      Console.Error.WriteLine("    iface sub:" + StageTimer.Format(subTimings));
    }
    return madeAnyChange;
  }

  /// Remove functions with interface-typed params whose only callers are
  /// other interface-param candidates — i.e. functions that survive
  /// because of a private dispatch chain that no live caller reaches.
  /// Splitting this out of `RunInterfaceAliasSpecialization` lets the
  /// interleaved Stage 2/2b loop create new specs across rounds without
  /// the dead-removal yanking intermediate iface funcs that the next
  /// round's transitive scan still needs.
  private static void RemoveUnreachableInterfaceFunctions(IrModule<MaxonOp> module) {
    var selfCandidateSet = new HashSet<IrFunction<MaxonOp>>();
    foreach (var func in module.Functions) {
      for (int i = 0; i < func.ParamTypes.Count; i++) {
        if (func.ParamTypes[i] is IrInterfaceType) { selfCandidateSet.Add(func); break; }
      }
    }
    for (int removeRound = 0; removeRound < 20 && selfCandidateSet.Count > 0; removeRound++) {
      var referencedCallees = new HashSet<string>();
      foreach (var func in module.Functions) {
        if (selfCandidateSet.Contains(func)) continue;
        foreach (var block in func.Body.Blocks) {
          foreach (var op in block.Operations) {
            if (op is MaxonCallOp call) referencedCallees.Add(call.Callee);
            else if (op is MaxonTryCallOp tryCall) referencedCallees.Add(tryCall.Callee);
            else if (op is MaxonClosureCreateOp closure) referencedCallees.Add(closure.FunctionName);
          }
        }
      }
      var toRemove = new HashSet<IrFunction<MaxonOp>>();
      foreach (var f in selfCandidateSet) {
        if (!referencedCallees.Contains(f.Name)) toRemove.Add(f);
      }
      if (toRemove.Count == 0) break;
      module.RemoveFunctionsWhere(f => toRemove.Contains(f));
      selfCandidateSet.ExceptWith(toRemove);
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
    // Newly minted MaxonValues here belong to the source function's id namespace
    // (stdlib-bit set vs unset). Without this flip, cloning a stdlib function would
    // draw user-side ids that could later alias real user MaxonValues in valueMap.
    var prevMode = IrContext.Current.StdlibLoweringMode;
    IrContext.Current.StdlibLoweringMode = source.IsStdlib;
    try {
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
    } finally {
      IrContext.Current.StdlibLoweringMode = prevMode;
    }
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
