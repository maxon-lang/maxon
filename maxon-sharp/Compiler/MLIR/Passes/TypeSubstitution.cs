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

    // Map associated type names (from uses clause) to concrete types
    foreach (var assocTypeName in sourceStruct.AssociatedTypeNames) {
      if (typeParams.TryGetValue(assocTypeName, out var concreteType)) {
        map[assocTypeName] = concreteType;
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

    // Resolve inner type aliases to their concrete aliases.
    // For Map<Key=String, Value=i64>, "KeyArray = Array with Key" resolves to StringArray,
    // "ValueArray = Array with Value" resolves to IntArray, etc.
    // Uses the full substitution map (includes conformance params like Element -> StringIntPair).
    foreach (var (innerAliasName, aliasInfo) in module.TypeAliasSources) {
      if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) continue;
      bool hasOurTypeParams = aliasInfo.TypeParams.Values.Any(t =>
        t is MlirTypeParameterType tp && map.ContainsKey(tp.ParameterName));
      if (!hasOurTypeParams) continue;

      // Resolve the inner alias's type params through the substitution map
      var resolvedParams = new Dictionary<string, MlirType>();
      foreach (var (paramName, paramType) in aliasInfo.TypeParams) {
        if (paramType is MlirTypeParameterType tp && map.TryGetValue(tp.ParameterName, out var resolved)) {
          // Resolved type may itself be a type alias — use the concrete type from the registry
          if (resolved is MlirStructType resolvedSt && map.TryGetValue(resolvedSt.Name, out var deepResolved)) {
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

    return new TypeSubstitution(map);
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

  // --- Queries ---

  public bool TryGetValue(string key, out MlirType value) => _map.TryGetValue(key, out value!);
  public bool ContainsKey(string key) => _map.ContainsKey(key);
  public MlirType? GetValueOrDefault(string key) => _map.GetValueOrDefault(key);
  public int Count => _map.Count;
  public IEnumerable<string> Keys => _map.Keys;
  public IEnumerable<KeyValuePair<string, MlirType>> Entries => _map;

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
  /// Struct and Enum element types resolve to Integer because they are stored as i64 pointers
  /// in managed memory and passed as scalars through generic function bodies.
  /// </summary>
  public MaxonValueKind SubstituteValueKind(MaxonValueKind kind, string? typeParamName = null) {
    if (kind != MaxonValueKind.TypeParameter) return kind;

    // Use the specific type param name if provided (e.g., "Key", "Value"), otherwise fall back to "Element"
    var paramKey = typeParamName ?? "Element";
    if (!_map.TryGetValue(paramKey, out var concreteType)) return kind;

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
