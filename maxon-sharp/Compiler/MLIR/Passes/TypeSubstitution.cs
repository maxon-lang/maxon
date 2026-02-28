using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Encapsulates the mapping from generic type parameter names to concrete types
/// for a single monomorphization specialization. Provides substitution operations
/// for types, value kinds, names, and callees.
/// </summary>
internal class TypeSubstitution {
  private readonly Dictionary<string, MlirType> _map;

  private TypeSubstitution(Dictionary<string, MlirType> map) {
    _map = map;
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
      MlirStructType sourceStruct,
      Dictionary<string, MlirType> typeParams,
      string concreteAliasName,
      MlirModule<MaxonOp> module) {
    // Get or create the concrete alias type
    MlirType concreteAliasType = module.TypeDefs.TryGetValue(concreteAliasName, out var existing)
      ? existing
      : new MlirStructType(concreteAliasName, []);

    var map = new Dictionary<string, MlirType> {
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
          .Where(kv => kv.Value is MlirTypeParameterType)
          .Select(kv => kv.Key);
    foreach (var assocTypeName in paramNames) {
      if (typeParams.TryGetValue(assocTypeName, out var concreteType)) {
        if (module.TypeDefs.TryGetValue(concreteType.Name, out var resolved) && resolved != concreteType) {
          map[assocTypeName] = resolved;
        } else {
          map[assocTypeName] = concreteType;
        }
      }
    }

    // Resolve conformance-bound type params (e.g., Element = Entry for Map : Iterable with Entry).
    // These are in the source struct's TypeParams but not in the concrete alias's typeParams.
    // Resolve them through inner alias chain (Entry -> Pair with (Key, Value) -> StringIntPair).
    foreach (var (paramName, paramValue) in sourceStruct.TypeParams) {
      if (map.ContainsKey(paramName)) continue;
      if (paramValue is MlirStructType innerAlias && module.TypeAliasSources.TryGetValue(innerAlias.Name, out TypeAliasInfo? innerInfo)) {
        if (innerInfo.TypeParams != null) {
          var resolvedInnerParams = new Dictionary<string, MlirType>();
          bool allResolved = true;
          foreach (var (ipn, ipt) in innerInfo.TypeParams) {
            if (ipt is MlirTypeParameterType tp && map.TryGetValue(tp.ParameterName, out var resolved))
              resolvedInnerParams[ipn] = resolved;
            else if (ipt is not MlirTypeParameterType)
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

    ResolveInnerTypeAliases(map, sourceStruct.TypeParams, module);

    return new TypeSubstitution(map);
  }

  /// Builds a type substitution for a concrete alias of a generic union type.
  public static TypeSubstitution BuildForUnion(
      MlirUnionType sourceUnion,
      Dictionary<string, MlirType> typeParams,
      string concreteAliasName,
      MlirModule<MaxonOp> module) {
    MlirType concreteAliasType = module.TypeDefs.TryGetValue(concreteAliasName, out var existing)
      ? existing
      : new MlirUnionType(concreteAliasName, []);

    var map = new Dictionary<string, MlirType> {
      [sourceUnion.Name] = concreteAliasType,
      ["Self"] = concreteAliasType
    };

    foreach (var assocTypeName in sourceUnion.AssociatedTypeNames) {
      if (typeParams.TryGetValue(assocTypeName, out var concreteType)) {
        if (module.TypeDefs.TryGetValue(concreteType.Name, out var resolved) && resolved != concreteType) {
          map[assocTypeName] = resolved;
        } else {
          map[assocTypeName] = concreteType;
        }
      }
    }

    ResolveInnerTypeAliases(map, sourceUnion.TypeParams, module);

    return new TypeSubstitution(map);
  }

  /// Resolves inner type aliases (e.g., KeyArray, ValueArray) to their concrete aliases
  /// by substituting type parameters through the current map. Handles deep resolution
  /// when a resolved type is itself an internal alias name (e.g., Entry → StringIntPair).
  private static void ResolveInnerTypeAliases(
      Dictionary<string, MlirType> map,
      Dictionary<string, MlirType> sourceTypeParams,
      MlirModule<MaxonOp> module) {
    foreach (var (innerAliasName, aliasInfo) in module.TypeAliasSources.ToList()) {
      if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) continue;
      bool hasOurTypeParams = aliasInfo.TypeParams.Values.Any(t =>
        t is MlirTypeParameterType tp && map.ContainsKey(tp.ParameterName));
      if (!hasOurTypeParams) continue;

      var resolvedParams = new Dictionary<string, MlirType>();
      foreach (var (paramName, paramType) in aliasInfo.TypeParams) {
        if (paramType is MlirTypeParameterType tp && map.TryGetValue(tp.ParameterName, out var resolved)) {
          // Only deep-resolve through the map when the resolved type is one of the
          // source type's own internal names (type params or aliases). This avoids
          // chaining through user types that collide with internal alias names
          // (e.g., user's "Entry" type vs Map's "typealias Entry = (Key, Value)").
          if (resolved is MlirStructType resolvedSt
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
  }

  /// Searches TypeAliasSources for a concrete alias whose source type matches and whose
  /// type params match the resolved params exactly. Returns the type definition or null.
  /// If no matching alias exists and the source type is known, auto-creates one.
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

    // Auto-create a concrete alias if the source type exists and all params are resolved
    if (resolvedParams.Values.Any(t => t is MlirTypeParameterType)) return null;
    if (!module.TypeDefs.TryGetValue(sourceTypeName, out var sourceType)) return null;

    var paramSuffix = string.Join("_", resolvedParams.Values.Select(t => t.Name));
    var autoAliasName = $"__{sourceTypeName}_{paramSuffix}";
    if (module.TypeDefs.TryGetValue(autoAliasName, out MlirType? value)) {
      return value;
    }

    if (sourceType is MlirStructType sourceStruct) {
      var concreteFields = new List<MlirStructField>();
      foreach (var field in sourceStruct.Fields) {
        var fieldType = resolvedParams.TryGetValue(field.Type.Name, out var concreteType)
          ? concreteType
          : field.Type;
        concreteFields.Add(new MlirStructField(field.Name, fieldType, field.IsExported, field.IsMutable, field.DefaultValue));
      }
      var newType = new MlirStructType(autoAliasName, concreteFields,
        conformingInterfaces: [.. sourceStruct.ConformingInterfaces],
        typeParams: resolvedParams);

      // Apply conditional conformances from extension blocks
      foreach (var (ccSourceType, interfaces, whereConstraints) in module.ConditionalConformances) {
        if (ccSourceType != sourceTypeName) continue;
        bool satisfied = true;
        foreach (var (paramName, requiredInterfaces) in whereConstraints) {
          if (!resolvedParams.TryGetValue(paramName, out var ccConcreteType)) { satisfied = false; break; }
          if (ccConcreteType is MlirTypeParameterType) { satisfied = false; break; }
          var concreteTypeName = MlirType.FormatAsSourceName(ccConcreteType);
          foreach (var iface in requiredInterfaces) {
            if (!MonomorphizationPass.TypeConformsToInterface(concreteTypeName, iface, module)) { satisfied = false; break; }
          }
          if (!satisfied) break;
        }
        if (satisfied) {
          foreach (var iface in interfaces)
            if (!newType.ConformingInterfaces.Contains(iface))
              newType.ConformingInterfaces.Add(iface);
        }
      }

      module.TypeDefs[autoAliasName] = newType;
      module.TypeAliasSources[autoAliasName] = new TypeAliasInfo(sourceTypeName, resolvedParams);
      return newType;
    }

    if (sourceType is MlirUnionType sourceUnion) {
      // Self-referential substitution: source union name → concrete alias
      var autoAliasPlaceholder = new MlirUnionType(autoAliasName, [],
        sourceUnion.BackingType,
        [.. sourceUnion.ConformingInterfaces],
        typeParams: resolvedParams);
      module.TypeDefs[autoAliasName] = autoAliasPlaceholder;

      var fullParams = new Dictionary<string, MlirType>(resolvedParams) {
        [sourceTypeName] = autoAliasPlaceholder,
        ["Self"] = autoAliasPlaceholder
      };

      var concreteCases = new List<MlirEnumCase>();
      foreach (var c in sourceUnion.Cases) {
        if (c.AssociatedValues is { Count: > 0 }) {
          var concreteValues = new List<(string Name, MlirType Type)>();
          foreach (var (name, type) in c.AssociatedValues) {
            var newCaseType = fullParams.TryGetValue(type.Name, out var ct) ? ct : type;
            concreteValues.Add((name, newCaseType));
          }
          concreteCases.Add(new MlirEnumCase(c.Name, c.Ordinal, associatedValues: concreteValues));
        } else {
          concreteCases.Add(c);
        }
      }

      var newUnionType = new MlirUnionType(autoAliasName, concreteCases,
        sourceUnion.BackingType,
        [.. sourceUnion.ConformingInterfaces],
        typeParams: resolvedParams);
      module.TypeDefs[autoAliasName] = newUnionType;
      module.TypeAliasSources[autoAliasName] = new TypeAliasInfo(sourceTypeName, resolvedParams);
      return newUnionType;
    }

    return null;
  }

  // --- Queries ---

  public bool TryGetValue(string key, out MlirType value) => _map.TryGetValue(key, out value!);
  public bool ContainsKey(string key) => _map.ContainsKey(key);
  public MlirType? GetValueOrDefault(string key) => _map.GetValueOrDefault(key);
  public int Count => _map.Count;
  public IEnumerable<string> Keys => _map.Keys;
  public IEnumerable<KeyValuePair<string, MlirType>> Entries => _map;

  /// Returns true if the given type name is a primitive type after substitution.
  /// Checks both as a map key (e.g., "Item") and as an already-resolved name (e.g., "Int").
  /// Used to avoid emitting equals() calls for types that were originally type parameters
  /// but resolved to primitives after monomorphization.
  public bool IsPrimitiveAlias(string typeName) {
    // Direct lookup: typeName is a type parameter key (e.g., "Item" → Int)
    if (_map.TryGetValue(typeName, out var concreteType))
      return IsPrimitiveMlirType(concreteType);
    // Reverse lookup: typeName is the resolved name (e.g., "Int") — check if any map
    // value with that name is primitive
    foreach (var entry in _map.Values) {
      if (entry.Name == typeName)
        return IsPrimitiveMlirType(entry);
    }
    return false;
  }

  private static bool IsPrimitiveMlirType(MlirType type) => type switch {
    { } t when t == MlirType.I64 || t == MlirType.F64 || t == MlirType.F32
            || t == MlirType.I1 || t == MlirType.I8 || t == MlirType.I16 || t == MlirType.U16 => true,
    MlirRangedPrimitiveType => true,
    _ => false
  };

  // --- Substitution Operations ---

  /// <summary>
  /// Substitutes a type through the map. Handles MlirTypeParameterType, MlirStructType,
  /// and MlirFunctionType (recursive substitution of param/return types).
  /// </summary>
  public MlirType SubstituteType(MlirType type) {
    if (type is MlirTypeParameterType tp) {
      return _map.TryGetValue(tp.ParameterName, out var newType) ? newType : type;
    }
    if (type is MlirStructType st) {
      if (_map.TryGetValue(st.Name, out var newType)) {
        return newType;
      }
      // Tuple types with unresolved type parameters (e.g., __Tuple_i64_Element)
      // need recursive field substitution to produce concrete tuple types
      if (st.IsTuple && st.Fields.Any(f => f.Type is MlirTypeParameterType tpp && _map.ContainsKey(tpp.ParameterName))) {
        var resolvedFieldTypes = st.Fields.Select(f => SubstituteType(f.Type)).ToList();
        return MlirStructType.CreateTupleType(resolvedFieldTypes);
      }
    }
    if (type is MlirUnionType ut) {
      if (_map.TryGetValue(ut.Name, out var newType)) {
        return newType;
      }
    }
    if (type is MlirFunctionType ft) {
      var newParams = ft.ParameterTypes.Select(p => SubstituteType(p)).ToList();
      var newReturn = ft.ReturnType != null ? SubstituteType(ft.ReturnType) : null;
      if (!newParams.SequenceEqual(ft.ParameterTypes) || newReturn != ft.ReturnType) {
        return new MlirFunctionType(newParams, newReturn);
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
      { } t when t == MlirType.I64 => MaxonValueKind.Integer,
      { } t when t == MlirType.F64 => MaxonValueKind.Float,
      { } t when t == MlirType.F32 => MaxonValueKind.Float32,
      { } t when t == MlirType.I1 => MaxonValueKind.Bool,
      { } t when t == MlirType.I8 => MaxonValueKind.Byte,
      { } t when t == MlirType.I16 || t == MlirType.U16 => MaxonValueKind.Short,
      MlirStructType => MaxonValueKind.Struct,
      MlirUnionType ut when ut.HasAssociatedValues => MaxonValueKind.Enum,
      MlirUnionType => MaxonValueKind.Integer,
      MlirRangedPrimitiveType rpt => rpt.BaseType.ToValueKind(),
      MlirTypeParameterType => MaxonValueKind.TypeParameter,
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
      if (_map.TryGetValue(typePart, out var newType)) {
        return $"{newType.Name}.{methodPart}";
      }
    }
    return callee;
  }
}
