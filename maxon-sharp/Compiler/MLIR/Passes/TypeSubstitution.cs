using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Encapsulates the mapping from generic type parameter names to concrete types
/// for a single monomorphization specialization. Provides substitution operations
/// for types, value kinds, names, and callees.
/// </summary>
internal class TypeSubstitution {
  private readonly Dictionary<string, IrType> _map;
  private Dictionary<string, TypeAliasInfo>? _typeAliasSources;
  private string? _sourceTypeName;
  private HashSet<string>? _sourceTypeMethodNames;

  private TypeSubstitution(Dictionary<string, IrType> map) {
    _map = map;
  }

  /// <summary>
  /// Attach type alias sources for callee resolution through alias chains.
  /// </summary>
  public void SetTypeAliasSources(Dictionary<string, TypeAliasInfo> sources) {
    _typeAliasSources = sources;
  }

  /// <summary>
  /// Set method names belonging to the source type so SubstituteCallee can
  /// distinguish real methods from free functions sharing a file namespace.
  /// </summary>
  public void SetSourceTypeMethodNames(string sourceTypeName, HashSet<string> methodNames) {
    _sourceTypeName = sourceTypeName;
    _sourceTypeMethodNames = methodNames;
  }

  /// <summary>
  /// Returns true if the given method name belongs to the source type.
  /// When no method names have been set, defaults to true for backwards compatibility.
  /// </summary>
  public bool IsSourceTypeMethod(string methodName) {
    return _sourceTypeMethodNames == null || _sourceTypeMethodNames.Contains(methodName);
  }

  // --- Building ---

  /// <summary>
  /// Builds a complete type substitution for a concrete alias of a generic type.
  /// Three-stage resolution:
  /// 1. Direct mapping of associated type names to concrete types
  /// 2. Conformance-bound type param resolution through inner alias chains
  /// 3. Inner type alias resolution (e.g., KeyArray → StringArray)
  /// </summary>
  public static TypeSubstitution Build(
      IrStructType sourceStruct,
      Dictionary<string, IrType> typeParams,
      string concreteAliasName,
      IrModule<MaxonOp> module) {
    // Get or create the concrete alias type
    IrType concreteAliasType = module.TypeDefs.TryGetValue(concreteAliasName, out var existing)
      ? existing
      : new IrStructType(concreteAliasName, []);

    var map = new Dictionary<string, IrType> {
      // Self/Vector -> __Vector_3_i64
      [sourceStruct.Name] = concreteAliasType,
      ["Self"] = concreteAliasType
    };

    // Map associated type names (from uses clause) to concrete types.
    // For types without a uses clause (e.g., tuples), fall back to TypeParams keys,
    // then to the alias's own typeParams keys (for auto-created types where the source
    // struct carries no generic info but the alias does).
    // Re-resolve through module.TypeDefs to pick up fully-parsed types
    // (alias type params may reference stale placeholders from pre-scan ordering)
    IEnumerable<string> paramNames = sourceStruct.AssociatedTypeNames.Count > 0
      ? sourceStruct.AssociatedTypeNames
      : sourceStruct.TypeParams
          .Where(kv => kv.Value is IrTypeParameterType)
          .Select(kv => kv.Key);
    foreach (var assocTypeName in paramNames) {
      if (typeParams.TryGetValue(assocTypeName, out var concreteType)) {
        // Re-resolve through TypeDefs to pick up fully-parsed types, but only if the
        // resolved type has the same name (avoid cross-contamination from stale entries)
        if (module.TypeDefs.TryGetValue(concreteType.Name, out var resolved)
            && resolved != concreteType && resolved.Name == concreteType.Name) {
          map[assocTypeName] = resolved;
        } else {
          map[assocTypeName] = concreteType;
        }
      }
    }

    // Resolve associated type params that are generic base types or type aliases
    // to their concrete aliases using other params in the map.
    // Examples: Source=Array -> Source=Array_i64, Iter=ArrayIter -> Iter=__ArrayIterator_String
    foreach (var assocTypeName in paramNames) {
      if (!map.TryGetValue(assocTypeName, out var assocType)) continue;
      if (assocType is not IrStructType assocStruct) continue;

      // If the type is a type alias, resolve to its source first
      var resolveSourceName = assocStruct.Name;
      if (module.TypeAliasSources.TryGetValue(resolveSourceName, out var aliasInfo))
        resolveSourceName = aliasInfo.SourceTypeName;

      // Only resolve if the resolved source is a DIFFERENT type from the enclosing one.
      // When resolveSourceName == sourceStruct.Name (e.g., Element=ByteArray → source Array,
      // and we're building for Array), resolving would find the alias itself (ByteArrayArray),
      // causing infinite nesting. Inner aliases like Iter=ArrayIter (source ArrayIterator ≠ Array)
      // are fine to resolve.
      if (resolveSourceName == sourceStruct.Name) continue;
      // Existence check for "any alias for this source" — index-backed instead
      // of an O(TypeAliasSources) Values.Any scan.
      if (module.GetAliasesBySource(resolveSourceName).Count > 0) {
        if (module.TypeDefs.TryGetValue(resolveSourceName, out var assocTypeDef)
            && assocTypeDef is IrStructType assocSourceStruct) {
          var assocTypeNames2 = assocSourceStruct.AssociatedTypeNames.Count > 0
            ? assocSourceStruct.AssociatedTypeNames
            : [.. assocSourceStruct.TypeParams
                .Where(kv => kv.Value is IrTypeParameterType)
                .Select(kv => kv.Key)];
          var resolvedAssocParams = new Dictionary<string, IrType>();
          bool allResolved = true;
          foreach (var atn in assocTypeNames2) {
            if (map.TryGetValue(atn, out var resolved))
              resolvedAssocParams[atn] = resolved;
            else { allResolved = false; break; }
          }
          if (allResolved && resolvedAssocParams.Count > 0) {
            var concreteAlias = FindConcreteAlias(module, resolveSourceName, resolvedAssocParams);
            if (concreteAlias != null) {
              map[assocTypeName] = concreteAlias;
            }
          }
        }
      }
    }

    // Resolve conformance-bound type params (e.g., Element = Entry for Map : Iterable with Entry).
    // These are in the source struct's TypeParams but not in the concrete alias's typeParams.
    // Resolve them through inner alias chain (Entry -> Pair with (Key, Value) -> StringIntPair).
    foreach (var (paramName, paramValue) in sourceStruct.TypeParams) {
      if (map.ContainsKey(paramName)) continue;
      if (paramValue is IrStructType innerAlias && module.TypeAliasSources.TryGetValue(innerAlias.Name, out TypeAliasInfo? innerInfo)) {
        if (innerInfo.TypeParams != null) {
          var resolvedInnerParams = new Dictionary<string, IrType>();
          bool allResolved = true;
          foreach (var (ipn, ipt) in innerInfo.TypeParams) {
            if (ipt is IrTypeParameterType tp && map.TryGetValue(tp.ParameterName, out var resolved))
              resolvedInnerParams[ipn] = resolved;
            else if (ipt is not IrTypeParameterType)
              resolvedInnerParams[ipn] = ipt;
            else { allResolved = false; break; }
          }
          if (allResolved) {
            var concreteType = FindConcreteAlias(module, innerInfo.SourceTypeName, resolvedInnerParams);
            if (concreteType != null) {
              map[paramName] = concreteType;
              map[innerAlias.Name] = concreteType;
            }
          }
        }
      }
    }

    // Collect type names referenced by this struct's fields, TypeParams, and methods
    // to scope inner alias resolution to only relevant aliases.
    // This prevents cross-type pollution (e.g., Map's Element=Entry leaking into Array's ElementMemory).
    var referencedNames = new HashSet<string>();
    foreach (var field in sourceStruct.Fields)
      referencedNames.Add(field.Type.Name);
    foreach (var (_, paramValue) in sourceStruct.TypeParams) {
      referencedNames.Add(paramValue.Name);
      if (paramValue is IrStructType paramStruct)
        referencedNames.Add(paramStruct.Name);
    }
    // Also include type names from method signatures on this type and its conforming interfaces.
    // FindMethodsByType returns all functions whose qualified name has sourceStruct.Name
    // as a non-terminal dot segment — a superset of the old StartsWith/EndsWith probe
    // (which matched first or second-to-last only). Over-inclusion here is conservative:
    // referencedNames is a filter that opts aliases in to resolution, so extra names can
    // only cause more inner aliases to resolve, never incorrect substitution.
    foreach (var func in module.FindMethodsByType(sourceStruct.Name)) {
      if (func.ReturnType is IrStructType retSt)
        referencedNames.Add(retSt.Name);
      foreach (var pt in func.ParamTypes)
        if (pt is IrStructType paramSt)
          referencedNames.Add(paramSt.Name);
    }

    // Run inner alias resolution in a loop to handle dependencies between aliases
    // (e.g., EnumSelf depends on Iter being resolved first to ArrayIter → __ArrayIterator_Integer)
    for (int pass = 0; pass < 3; pass++) {
      var prevSnapshot = map.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
      ResolveInnerTypeAliases(map, sourceStruct.TypeParams, module, referencedNames, sourceTypeName: sourceStruct.Name);
      bool changed = map.Any(kv => !prevSnapshot.TryGetValue(kv.Key, out var prev) || prev != kv.Value.Name);
      if (!changed) break;
    }

    // Map inner ranged typealiases to their per-instance concrete aliases.
    // E.g., "Index" → IrRangedPrimitiveType("FunctionPool__Index", ...)
    if (module.TypeDefs.TryGetValue(concreteAliasName, out var concreteAliasDef)
        && concreteAliasDef is IrStructType concreteStructDef) {
      foreach (var (innerName, concreteRanged) in concreteStructDef.InnerRangedAliases) {
        map[innerName] = concreteRanged;
      }
    }

    return new TypeSubstitution(map);
  }

  /// Builds a type substitution for a concrete alias of a generic enum type.
  public static TypeSubstitution BuildForEnum(
      IrEnumType sourceEnum,
      Dictionary<string, IrType> typeParams,
      string concreteAliasName,
      IrModule<MaxonOp> module) {
    IrType concreteAliasType = module.TypeDefs.TryGetValue(concreteAliasName, out var existing)
      ? existing
      : new IrEnumType(concreteAliasName, []) { IsUnion = sourceEnum.IsUnion };

    var map = new Dictionary<string, IrType> {
      [sourceEnum.Name] = concreteAliasType,
      ["Self"] = concreteAliasType
    };

    foreach (var assocTypeName in sourceEnum.AssociatedTypeNames) {
      if (typeParams.TryGetValue(assocTypeName, out var concreteType)) {
        if (module.TypeDefs.TryGetValue(concreteType.Name, out var resolved) && resolved != concreteType) {
          map[assocTypeName] = resolved;
        } else {
          map[assocTypeName] = concreteType;
        }
      }
    }

    // Collect type names referenced by this enum's TypeParams
    var referencedNames = new HashSet<string>();
    foreach (var (_, paramValue) in sourceEnum.TypeParams)
      referencedNames.Add(paramValue.Name);

    ResolveInnerTypeAliases(map, sourceEnum.TypeParams, module, referencedNames, sourceTypeName: sourceEnum.Name);

    return new TypeSubstitution(map);
  }

  /// Resolves inner type aliases (e.g., KeyArray, ValueArray) to their concrete aliases
  /// by substituting type parameters through the current map. Handles deep resolution
  /// when a resolved type is itself an internal alias name (e.g., Entry → StringIntPair).
  /// Only resolves aliases that are referenced by the source type's fields or TypeParams,
  /// to avoid cross-type pollution (e.g., Array's ElementMemory being resolved through
  /// Map's Element=Entry binding).
  private static void ResolveInnerTypeAliases(
      Dictionary<string, IrType> map,
      Dictionary<string, IrType> sourceTypeParams,
      IrModule<MaxonOp> module,
      IEnumerable<string>? referencedTypeNames = null,
      string? sourceTypeName = null) {
    // Collect type names referenced by the source struct (fields + TypeParams values)
    // to scope resolution to only relevant aliases
    HashSet<string>? scopedNames = referencedTypeNames != null ? [.. referencedTypeNames] : null;

    // Phase 1: resolve directly referenced aliases
    foreach (var (innerAliasName, aliasInfo) in module.TypeAliasSources.ToList()) {
      if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) continue;
      if (scopedNames != null && !scopedNames.Contains(innerAliasName)) continue;

      bool hasOurTypeParams = aliasInfo.TypeParams.Values.Any(t =>
        t is IrTypeParameterType tp && map.ContainsKey(tp.ParameterName));
      if (!hasOurTypeParams) continue;

      var resolvedParams = new Dictionary<string, IrType>();
      foreach (var (paramName, paramType) in aliasInfo.TypeParams) {
        if (paramType is IrTypeParameterType tp && map.TryGetValue(tp.ParameterName, out var resolved)) {
          if (resolved is IrStructType resolvedSt
              && sourceTypeParams.ContainsKey(resolvedSt.Name)
              && map.TryGetValue(resolvedSt.Name, out var deepResolved)) {
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
        map[innerAliasName] = concreteInnerType;
      }
    }

    // Phase 1b: resolve standalone generic types referenced by method signatures.
    // E.g., MapIterator (uses Key, Value) is referenced by Map.createIterator's return type.
    // These types aren't in TypeAliasSources but need concrete specializations.
    // Skip the source type itself to avoid recursive alias explosion (e.g., Array resolving
    // Element=__Array_X → __Array___Array_X → __Array___Array___Array_X → ...).
    if (scopedNames != null) {
      foreach (var refName in scopedNames) {
        if (map.ContainsKey(refName)) continue;
        if (refName == sourceTypeName) continue;
        if (module.TypeAliasSources.ContainsKey(refName)) continue;
        if (!module.TypeDefs.TryGetValue(refName, out var refDef)) continue;
        if (refDef is not IrStructType refStruct || refStruct.AssociatedTypeNames.Count == 0) continue;

        // Check if this generic type's associated type params can be resolved through the map
        var resolvedParams = new Dictionary<string, IrType>();
        bool allResolved = true;
        foreach (var atn in refStruct.AssociatedTypeNames) {
          if (map.TryGetValue(atn, out var resolved))
            resolvedParams[atn] = resolved;
          else { allResolved = false; break; }
        }
        if (!allResolved || resolvedParams.Count == 0) continue;

        // Find or create a concrete alias
        var concreteType = FindConcreteAlias(module, refName, resolvedParams);
        if (concreteType != null) {
          map[refName] = concreteType;
        }
      }
    }

    // Phase 2: resolve aliases that are field types of resolved inner types.
    // E.g., if KeyArray resolved to Array_i64, look up Array (source) and Array_i64 (concrete),
    // and map field type name differences: ElementMemory → __ManagedMemory_i64.
    // When multiple inner aliases map the same field type to different concrete types
    // (e.g., KeyArray's ElementMemory → __ManagedMemory_String vs ValueArray's ElementMemory →
    // __ManagedMemory_i64), the mapping is ambiguous and must be excluded.
    if (scopedNames != null) {
      var extraMappings = new Dictionary<string, IrType>();
      var conflicted = new HashSet<string>();
      foreach (var (aliasName, resolvedType) in map) {
        if (resolvedType is not IrStructType resolvedStruct) continue;
        // Find the source type for this alias
        if (!module.TypeAliasSources.TryGetValue(aliasName, out var aliasInfo2)) continue;
        if (!module.TypeDefs.TryGetValue(aliasInfo2.SourceTypeName, out var sourceDef)) continue;
        if (sourceDef is not IrStructType sourceStruct) continue;
        // Find the concrete type
        if (!module.TypeDefs.TryGetValue(resolvedStruct.Name, out var concreteDef)) continue;
        if (concreteDef is not IrStructType concreteStruct) continue;
        // Map old field type names to new field types
        for (int i = 0; i < sourceStruct.Fields.Count && i < concreteStruct.Fields.Count; i++) {
          var oldFieldTypeName = sourceStruct.Fields[i].Type.Name;
          var newFieldType = concreteStruct.Fields[i].Type;
          if (oldFieldTypeName != newFieldType.Name && !map.ContainsKey(oldFieldTypeName)) {
            if (extraMappings.TryGetValue(oldFieldTypeName, out var existing)) {
              if (existing.Name != newFieldType.Name)
                conflicted.Add(oldFieldTypeName);
            } else {
              extraMappings[oldFieldTypeName] = newFieldType;
            }
          }
        }
      }
      foreach (var (k, v) in extraMappings) {
        if (!conflicted.Contains(k))
          map.TryAdd(k, v);
      }
    }
  }

  /// Searches TypeAliasSources for a concrete alias whose source type matches and whose
  /// type params match the resolved params exactly. Returns the type definition or null.
  /// If no matching alias exists and the source type is known, auto-creates one.
  private static IrType? FindConcreteAlias(
      IrModule<MaxonOp> module,
      string sourceTypeName,
      Dictionary<string, IrType> resolvedParams) {
    // Look up only aliases that match this source — module keeps the reverse
    // index up to date, so this avoids the prior O(TypeAliasSources) scan.
    foreach (var (candidateName, candidateInfo) in module.GetAliasesBySource(sourceTypeName)) {
      if (candidateInfo.TypeParams == null) continue;
      if (candidateInfo.TypeParams.Values.Any(t => t is IrTypeParameterType)) continue;
      if (candidateInfo.TypeParams.Count != resolvedParams.Count) continue;
      bool match = true;
      foreach (var (pn, pt) in resolvedParams) {
        if (!candidateInfo.TypeParams.TryGetValue(pn, out var ct) || ct.Name != pt.Name) { match = false; break; }
      }
      if (match && module.TypeDefs.TryGetValue(candidateName, out var candidateType))
        return candidateType;
    }

    // Auto-create a concrete alias if the source type exists and all params are resolved
    if (resolvedParams.Values.Any(t => t is IrTypeParameterType)) return null;
    if (!module.TypeDefs.TryGetValue(sourceTypeName, out var sourceType)) return null;

    // Prevent recursive type nesting: if any resolved param is itself an alias whose source
    // is the same type we're creating an alias for, this would create unbounded nesting
    // (e.g., FindConcreteAlias("List", {Element: IntListList}) where IntListList = List with IntList
    // would create __List_IntListList = List<List<IntList>> which triggers infinite depth).
    foreach (var (_, paramType) in resolvedParams) {
      var paramName = paramType.Name;
      int depth = 0;
      var current = paramName;
      while (depth++ < 5 && module.TypeAliasSources.TryGetValue(current, out var paramAliasInfo)) {
        if (paramAliasInfo.SourceTypeName == sourceTypeName) return null;
        current = paramAliasInfo.SourceTypeName;
      }
    }

    var paramSuffix = string.Join("_", resolvedParams.Values.Select(t => t.Name));
    var autoAliasName = $"__{sourceTypeName}_{paramSuffix}";
    if (module.TypeDefs.TryGetValue(autoAliasName, out IrType? value)) {
      return value;
    }

    if (sourceType is IrStructType sourceStruct) {
      var concreteFields = new List<IrStructField>();
      foreach (var field in sourceStruct.Fields) {
        var fieldType = resolvedParams.TryGetValue(field.Type.Name, out var concreteType)
          ? concreteType
          : field.Type;
        concreteFields.Add(new IrStructField(field.Name, fieldType, field.IsExported, field.IsMutable, field.DefaultValue));
      }
      var newType = new IrStructType(autoAliasName, concreteFields,
        conformingInterfaces: [.. sourceStruct.ConformingInterfaces],
        typeParams: resolvedParams,
        isTuple: sourceStruct.IsTuple);

      // Apply conditional conformances from extension blocks
      foreach (var (ccSourceType, interfaces, whereConstraints) in module.ConditionalConformances) {
        if (ccSourceType != sourceTypeName) continue;
        bool satisfied = true;
        foreach (var (paramName, requiredInterfaces) in whereConstraints) {
          if (!resolvedParams.TryGetValue(paramName, out var ccConcreteType)) { satisfied = false; break; }
          if (ccConcreteType is IrTypeParameterType) { satisfied = false; break; }
          var concreteTypeName = IrType.FormatAsSourceName(ccConcreteType);
          foreach (var iface in requiredInterfaces) {
            if (!MonomorphizationPass.TypeConformsToInterface(concreteTypeName, iface, module)) { satisfied = false; break; }
          }
          if (!satisfied) break;
        }
        if (satisfied) {
          foreach (var iface in interfaces)
            newType.ConformingInterfaces.Add(iface);
        }
      }

      module.TypeDefs[autoAliasName] = newType;
      module.RegisterTypeAlias(autoAliasName, new TypeAliasInfo(sourceTypeName, resolvedParams));
      return newType;
    }

    if (sourceType is IrEnumType sourceEnum) {
      // Self-referential substitution: source enum name → concrete alias
      var autoAliasPlaceholder = new IrEnumType(autoAliasName, [],
        sourceEnum.BackingType,
        [.. sourceEnum.ConformingInterfaces],
        typeParams: resolvedParams) { IsUnion = sourceEnum.IsUnion };
      module.TypeDefs[autoAliasName] = autoAliasPlaceholder;

      var fullParams = new Dictionary<string, IrType>(resolvedParams) {
        [sourceTypeName] = autoAliasPlaceholder,
        ["Self"] = autoAliasPlaceholder
      };

      var concreteCases = new List<IrEnumCase>();
      foreach (var c in sourceEnum.Cases) {
        if (c.AssociatedValues is { Count: > 0 }) {
          var concreteValues = new List<(string Name, IrType Type)>();
          foreach (var (name, type) in c.AssociatedValues) {
            var newCaseType = fullParams.TryGetValue(type.Name, out var ct) ? ct : type;
            concreteValues.Add((name, newCaseType));
          }
          concreteCases.Add(new IrEnumCase(c.Name, c.Ordinal, associatedValues: concreteValues));
        } else {
          concreteCases.Add(c);
        }
      }

      var newEnumType = new IrEnumType(autoAliasName, concreteCases,
        sourceEnum.BackingType,
        [.. sourceEnum.ConformingInterfaces],
        typeParams: resolvedParams) { IsUnion = sourceEnum.IsUnion };
      module.TypeDefs[autoAliasName] = newEnumType;
      module.RegisterTypeAlias(autoAliasName, new TypeAliasInfo(sourceTypeName, resolvedParams));
      return newEnumType;
    }

    return null;
  }

  // --- Queries ---

  public bool TryGetValue(string key, out IrType value) => _map.TryGetValue(key, out value!);
  public bool ContainsKey(string key) => _map.ContainsKey(key);
  public IrType? GetValueOrDefault(string key) => _map.GetValueOrDefault(key);
  public int Count => _map.Count;
  public IEnumerable<string> Keys => _map.Keys;
  public IEnumerable<KeyValuePair<string, IrType>> Entries => _map;

  /// Returns true if the given type name is a primitive type after substitution.
  /// Checks both as a map key (e.g., "Item") and as an already-resolved name (e.g., "Int").
  /// Used to avoid emitting equals() calls for types that were originally type parameters
  /// but resolved to primitives after monomorphization.
  public bool IsPrimitiveAlias(string typeName) {
    // Direct lookup: typeName is a type parameter key (e.g., "Item" → Int)
    if (_map.TryGetValue(typeName, out var concreteType))
      return IsPrimitiveIrType(concreteType);
    // Reverse lookup: typeName is the resolved name (e.g., "Int") — check if any map
    // value with that name is primitive
    foreach (var entry in _map.Values) {
      if (entry.Name == typeName)
        return IsPrimitiveIrType(entry);
    }
    return false;
  }

  private static bool IsPrimitiveIrType(IrType type) => type switch {
    { } t when t == IrType.I64 || t == IrType.F64 || t == IrType.F32
            || t == IrType.I1 || t == IrType.I8 || t == IrType.I16 || t == IrType.U16 => true,
    IrRangedPrimitiveType => true,
    _ => false
  };

  // --- Substitution Operations ---

  /// <summary>
  /// Substitutes a type through the map. Handles IrTypeParameterType, IrStructType,
  /// and IrFunctionType (recursive substitution of param/return types).
  /// </summary>
  public IrType SubstituteType(IrType type) {
    if (type is IrTypeParameterType tp) {
      return _map.TryGetValue(tp.ParameterName, out var newType) ? newType : type;
    }
    if (type is IrStructType st) {
      if (_map.TryGetValue(st.Name, out var newType)) {
        return newType;
      }
      // Tuple types with unresolved type parameters (e.g., __Tuple_i64_Element)
      // need recursive field substitution to produce concrete tuple types
      if (st.IsTuple && st.Fields.Any(f => f.Type is IrTypeParameterType tpp && _map.ContainsKey(tpp.ParameterName))) {
        var resolvedFieldTypes = st.Fields.Select(f => SubstituteType(f.Type)).ToList();
        return IrStructType.CreateTupleType(resolvedFieldTypes);
      }
      // Non-tuple struct types with unresolved type parameters in fields or TypeParams
      // (e.g., Iterator types like ArrayIterator with Element field types, or MapIterator
      // with Key/Value in TypeParams) — create a concrete specialization
      if (!st.IsTuple && (st.Fields.Any(f => f.Type is IrTypeParameterType tpp && _map.ContainsKey(tpp.ParameterName))
          || st.TypeParams.Values.Any(v => v is IrTypeParameterType tpp2 && _map.ContainsKey(tpp2.ParameterName)))) {
        var resolvedFields = st.Fields.Select(f => {
          var resolvedType = SubstituteType(f.Type);
          return new IrStructField(f.Name, resolvedType, f.IsExported, f.IsMutable, f.DefaultValue);
        }).ToList();
        var concreteName = SubstituteName(st.Name);
        return new IrStructType(concreteName, resolvedFields,
          associatedTypeNames: [.. st.AssociatedTypeNames],
          typeParams: new Dictionary<string, IrType>(st.TypeParams.Select(kv =>
            new KeyValuePair<string, IrType>(kv.Key, SubstituteType(kv.Value)))),
          conformingInterfaces: [.. st.ConformingInterfaces]);
      }
    }
    if (type is IrEnumType ut) {
      if (_map.TryGetValue(ut.Name, out var newType)) {
        return newType;
      }
    }
    if (type is IrRangedPrimitiveType rpt) {
      if (_map.TryGetValue(rpt.Name, out var newRangedType))
        return newRangedType;
    }
    if (type is IrFunctionType ft) {
      var newParams = ft.ParameterTypes.Select(p => SubstituteType(p)).ToList();
      var newReturn = ft.ReturnType != null ? SubstituteType(ft.ReturnType) : null;
      if (!newParams.SequenceEqual(ft.ParameterTypes) || newReturn != ft.ReturnType) {
        return new IrFunctionType(newParams, newReturn);
      }
    }
    return type;
  }

  /// <summary>
  /// Resolves TypeParameter kinds to concrete kinds based on the type substitution.
  /// Struct types resolve to Struct, associated-value enums to Enum, and simple
  /// enums to Integer (no heap pointer needed). Primitives resolve to their native kinds.
  /// </summary>
  public MaxonValueKind SubstituteValueKind(MaxonValueKind kind, string? typeParamName = null) {
    if (kind != MaxonValueKind.TypeParameter) return kind;

    // Use the specific type param name if provided (e.g., "Key", "Value"), otherwise fall back to "Element"
    var paramKey = typeParamName ?? "Element";
    if (!_map.TryGetValue(paramKey, out var concreteType)) return kind;

    return concreteType switch {
      { } t when t == IrType.I64 => MaxonValueKind.Integer,
      { } t when t == IrType.F64 => MaxonValueKind.Float,
      { } t when t == IrType.F32 => MaxonValueKind.Float32,
      { } t when t == IrType.I1 => MaxonValueKind.Bool,
      { } t when t == IrType.I8 => MaxonValueKind.Byte,
      { } t when t == IrType.I16 || t == IrType.U16 => MaxonValueKind.Short,
      IrStructType => MaxonValueKind.Struct,
      IrEnumType ut when ut.HasAssociatedValues => MaxonValueKind.Enum,
      IrEnumType => MaxonValueKind.Integer,
      IrRangedPrimitiveType rpt => rpt.BaseType.ToValueKind(),
      IrTypeParameterType => MaxonValueKind.TypeParameter,
      _ => throw new InvalidOperationException($"SubstituteValueKind: unsupported type '{concreteType}' for param '{paramKey}'")
    };
  }

  /// <summary>
  /// Substitutes a type name through the map. Returns the concrete type's name
  /// if found, otherwise the original name.
  /// </summary>
  public string SubstituteName(string name) {
    return _map.TryGetValue(name, out var newType) ? newType.Name : name;
  }

  /// <summary>
  /// Substitutes a callee name of the form "TypeName.MethodName" through the map.
  /// </summary>
  public string SubstituteCallee(string callee) {
    var dotIdx = callee.LastIndexOf('.');
    if (dotIdx > 0) {
      var typePart = callee[..dotIdx];
      var methodPart = callee[(dotIdx + 1)..];
      // "?" is a placeholder for an unresolved type parameter (e.g., from for-in element
      // equality comparisons). Resolve through the Element type param if available.
      if (typePart == "?" && _map.TryGetValue("Element", out var elemType)) {
        var resolvedName = elemType.Name;
        if (_typeAliasSources != null && _typeAliasSources.TryGetValue(resolvedName, out var aliasInfo2))
          resolvedName = aliasInfo2.SourceTypeName;
        return $"{resolvedName}.{methodPart}";
      }
      if (_map.TryGetValue(typePart, out var newType)) {
        // Don't rewrite free functions that share a file namespace with the source type.
        // Only check when the prefix matches the source type name (not type parameters).
        if (_sourceTypeMethodNames != null && typePart == _sourceTypeName
            && !_sourceTypeMethodNames.Contains(methodPart))
          return callee;
        var resolvedName = newType is IrRangedPrimitiveType rpt
          ? IrType.FormatAsSourceName(rpt.BaseType)
          : newType.Name;
        // If the resolved name is a type alias (e.g., ArrayIter), resolve it to its source
        // so that RewriteCallSites can find the monomorphized concrete function.
        if (_typeAliasSources != null && _typeAliasSources.TryGetValue(resolvedName, out var aliasInfo)) {
          resolvedName = aliasInfo.SourceTypeName;
        }
        return $"{resolvedName}.{methodPart}";
      }
    }
    return callee;
  }
}
