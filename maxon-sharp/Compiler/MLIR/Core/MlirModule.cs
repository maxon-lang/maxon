using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Passes;

namespace MaxonSharp.Compiler.Ir.Core;

public class IrGlobal(string name, IrType type, IrAttribute? initValue = null) {
  public string Name { get; } = name;
  public IrType Type { get; } = type;
  public IrAttribute? InitValue { get; } = initValue;
}

// Represents a type alias with its source type, type parameter substitutions, and visibility metadata
public record TypeAliasInfo(string SourceTypeName, Dictionary<string, IrType>? TypeParams,
    bool IsExported = false, bool IsStdlib = false, string? SourceFilePath = null, string? OwnerTypeName = null) {
  /// Checks if a type name refers to __ManagedMemory, either directly or via a type alias.
  public static bool IsManagedMemoryType(string typeName, Dictionary<string, TypeAliasInfo> typeAliasSources) {
    if (typeName == "__ManagedMemory" || typeName.StartsWith("__ManagedMemory_")) return true;
    return typeAliasSources.TryGetValue(typeName, out var info) && info.SourceTypeName == "__ManagedMemory";
  }

  /// Checks if a type name refers to __ManagedList, either directly or via a type alias.
  public static bool IsManagedListType(string typeName, Dictionary<string, TypeAliasInfo> typeAliasSources) {
    if (typeName == "__ManagedList") return true;
    return typeAliasSources.TryGetValue(typeName, out var info) && info.SourceTypeName == "__ManagedList";
  }

  /// Checks if a type name refers to __ManagedMemoryCursor, either directly or via a type alias.
  public static bool IsManagedCursorType(string typeName, Dictionary<string, TypeAliasInfo> typeAliasSources) {
    if (typeName == "__ManagedMemoryCursor") return true;
    return typeAliasSources.TryGetValue(typeName, out var info) && info.SourceTypeName == "__ManagedMemoryCursor";
  }

}

// Metadata for constant array literals that can be placed in .rdata
public record ConstantArrayLiteralInfo(string RdataLabel, long[] Values, bool IsMutable, int ElementSize, bool IsBitPacked = false);

// Metadata for a module-level global variable (stored in IrModule.GlobalVarInfos for cross-file seeding)
public record GlobalVarMetadata(MaxonValueKind Kind, bool Mutable, string? EnumTypeName = null, string? TypeName = null, bool IsLazy = false);

// Deferred global variable initialization: stores tokens for expressions that must be evaluated at main() entry
public record DeferredGlobalInit(string Name, List<Token> Tokens, int TokenStart, int TokenEnd, bool IsMutable, int Line, int Column, string? SourceFilePath = null);

public class IrModule<TOp> where TOp : IPrintableOp {
  public string EntryFunctionName { get; set; } = "main";
  public List<IrFunction<TOp>> Functions { get; } = [];

  // Lookup indices on Functions — maintained incrementally by AddFunction /
  // RemoveFunction / RemoveFunctionsWhere. Anything that mutates Functions
  // without going through those methods (including renaming a function in
  // place) must call InvalidateFunctionIndex, which forces a full rebuild on
  // the next lookup.
  private bool _indexDirty;

  // Exact full name → function. Names are globally unique, so one entry each.
  private readonly Dictionary<string, IrFunction<TOp>> _exactIndex = [];
  // Overload base name (strip `$...` tail) → list of functions. Used by the
  // parser's overload resolver to pick up `foo`, `foo$i64`, `foo$String`.
  private readonly Dictionary<string, List<IrFunction<TOp>>> _baseNameIndex = [];
  // Last `.`-segment (stripped of any `$...` tail) → list of functions.
  // Drives "unqualified method name" resolution like `greet` → `helpers.greet`.
  private readonly Dictionary<string, List<IrFunction<TOp>>> _shortNameIndex = [];

  /// <summary>
  /// Marks the Functions index as stale so it will be fully rebuilt on next
  /// access. Call this after direct mutations to Functions (e.g. renaming a
  /// function's Name in place) when you can't use RenameFunction.
  /// </summary>
  public void InvalidateFunctionIndex() {
    _indexDirty = true;
  }

  /// <summary>
  /// Renames an existing function in place while keeping the Functions index
  /// consistent. Callers that mutate `func.Name = ...` directly must invalidate
  /// the index instead, which is far more expensive when done on the hot path.
  /// </summary>
  public void RenameFunction(IrFunction<TOp> func, string newName) {
    if (!_indexDirty) UnindexFunction(func);
    func.Name = newName;
    if (!_indexDirty) IndexFunction(func);
  }

  private void EnsureFunctionIndex() {
    if (!_indexDirty) return;
    _exactIndex.Clear();
    _baseNameIndex.Clear();
    _shortNameIndex.Clear();
    foreach (var f in Functions) {
      IndexFunction(f);
    }
    _indexDirty = false;
  }

  private void IndexFunction(IrFunction<TOp> f) {
    // Exact: last writer wins; IrModule's own merge logic enforces single
    // bodies per name, so duplicates only show up transiently during
    // replacement — matching the FirstOrDefault-over-list behavior.
    _exactIndex[f.Name] = f;

    // Base name — the name with any `$` overload suffix removed.
    var baseName = StripOverloadSuffix(f.Name);
    if (!_baseNameIndex.TryGetValue(baseName, out var baseList)) {
      baseList = [];
      _baseNameIndex[baseName] = baseList;
    }
    baseList.Add(f);

    // Short name — the last `.`-segment of the base name.
    var dotIdx = baseName.LastIndexOf('.');
    if (dotIdx >= 0) {
      var shortName = baseName[(dotIdx + 1)..];
      if (!_shortNameIndex.TryGetValue(shortName, out var shortList)) {
        shortList = [];
        _shortNameIndex[shortName] = shortList;
      }
      shortList.Add(f);
    }
  }

  private void UnindexFunction(IrFunction<TOp> f) {
    if (_indexDirty) return; // will be rebuilt from scratch anyway
    if (_exactIndex.TryGetValue(f.Name, out var indexed) && ReferenceEquals(indexed, f))
      _exactIndex.Remove(f.Name);

    var baseName = StripOverloadSuffix(f.Name);
    if (_baseNameIndex.TryGetValue(baseName, out var baseList)) {
      baseList.Remove(f);
      if (baseList.Count == 0) _baseNameIndex.Remove(baseName);
    }

    var dotIdx = baseName.LastIndexOf('.');
    if (dotIdx >= 0) {
      var shortName = baseName[(dotIdx + 1)..];
      if (_shortNameIndex.TryGetValue(shortName, out var shortList)) {
        shortList.Remove(f);
        if (shortList.Count == 0) _shortNameIndex.Remove(shortName);
      }
    }
  }

  private static string StripOverloadSuffix(string name) {
    var dollar = name.IndexOf('$');
    return dollar < 0 ? name : name[..dollar];
  }

  /// <summary>
  /// O(1) exact-name lookup. Returns null if no function with that name exists.
  /// </summary>
  public IrFunction<TOp>? FindFunctionByExactName(string name) {
    EnsureFunctionIndex();
    return _exactIndex.TryGetValue(name, out var f) ? f : null;
  }

  /// <summary>
  /// Returns all functions whose name (with any `$...` overload suffix stripped)
  /// equals the given base name. Used for overload resolution.
  /// </summary>
  public IReadOnlyList<IrFunction<TOp>> FindFunctionsByBaseName(string baseName) {
    EnsureFunctionIndex();
    return _baseNameIndex.TryGetValue(baseName, out var list) ? list : (IReadOnlyList<IrFunction<TOp>>)[];
  }

  /// <summary>
  /// Exact lookup with an overload-base fallback: returns the function with the
  /// exact given name if it exists, otherwise any one function whose base name
  /// (with `$overload` suffix stripped) equals the given name. Used by callers
  /// that just want "some function with that name" and don't care about overload
  /// selection themselves.
  /// </summary>
  public IrFunction<TOp>? FindFunctionByExactOrBaseName(string name) {
    EnsureFunctionIndex();
    if (_exactIndex.TryGetValue(name, out var exact)) return exact;
    if (_baseNameIndex.TryGetValue(name, out var list) && list.Count > 0) return list[0];
    return null;
  }

  /// <summary>
  /// Returns all functions whose last `.`-segment (after stripping any `$...`
  /// overload suffix) equals the given short name. Used for unqualified name
  /// resolution like `greet` → `helpers.greet`.
  /// </summary>
  public IReadOnlyList<IrFunction<TOp>> FindFunctionsByShortName(string shortName) {
    EnsureFunctionIndex();
    return _shortNameIndex.TryGetValue(shortName, out var list) ? list : (IReadOnlyList<IrFunction<TOp>>)[];
  }

  public void RemoveFunction(IrFunction<TOp> func) {
    if (Functions.Remove(func)) {
      UnindexFunction(func);
    }
  }

  public int RemoveFunctionsWhere(Predicate<IrFunction<TOp>> match) {
    int removed = Functions.RemoveAll(f => {
      if (!match(f)) return false;
      UnindexFunction(f);
      return true;
    });
    return removed;
  }

  public List<(string label, byte[] bytes, int alignment)> RdataEntries { get; } = [];
  public List<(string label, byte[] bytes, int alignment)> SymdataEntries { get; } = [];
  public List<(string label, byte[] bytes, int alignment)> UcddataEntries { get; } = [];
  public List<IrGlobal> Globals { get; } = [];
  public Dictionary<string, IrType> TypeDefs { get; } = [];
  public Dictionary<string, Dictionary<int, IrAttribute>> FunctionDefaults { get; } = [];
  // Type alias tracking: aliasName -> TypeAliasInfo (sourceTypeName + typeParams)
  public Dictionary<string, TypeAliasInfo> TypeAliasSources { get; } = [];

  // Constant array literal metadata: struct result ID -> ConstantArrayLiteralInfo
  // Populated by ConstantArrayAnalysisPass, consumed by MaxonToStandardConversion
  public Dictionary<int, ConstantArrayLiteralInfo> ConstantArrayLiterals { get; } = [];

  // Interface associated type names (interfaceName -> list of 'uses' type names)
  public Dictionary<string, List<string>> InterfaceAssociatedTypes { get; } = [];

  // Primitive type conformances from extension blocks (e.g., "int" -> ["Hashable", "Equatable"])
  public Dictionary<string, List<string>> PrimitiveConformances { get; } = [];

  // Conditional conformances from extension blocks on generic types
  // e.g., "extension Array implements Hashable where Element is Hashable"
  public List<(string SourceTypeName, List<string> Interfaces, Dictionary<string, List<string>> WhereConstraints)> ConditionalConformances { get; } = [];

  // Deferred global var/let initializations from all source files, emitted at start of main()
  public List<DeferredGlobalInit> DeferredGlobalInits { get; } = [];

  // Source files containing interface extensions that found no conforming types
  // during initial pre-scan (due to file ordering). Rescanned after all pre-scans.
  public HashSet<string> DeferredExtensionFiles { get; } = [];

  // Non-exported type/enum/typealias names — filtered from _typeRegistry when seeding other files
  public HashSet<string> NonExportedTypeNames { get; } = [];

  // Tag table for mm-trace: index -> symdata label of the type name string.
  // Index 0 = null/no tag. Built during MaxonToStandard lowering, consumed by X86CodeEmitter.
  public List<string?> TagTable { get; set; } = [];

  // Raw type name strings for each tag index (for debugstream tag table embedding).
  // Same indexing as TagTable. Built during MaxonToStandard lowering.
  public List<string?> TagNames { get; set; } = [];

  // Global variable metadata for cross-file seeding (name -> kind/mutability/type info)
  public Dictionary<string, GlobalVarMetadata> GlobalVarInfos { get; } = [];

  // Non-exported global var names — filtered when seeding _globalVars to other files
  public HashSet<string> NonExportedGlobalVarNames { get; } = [];

  // Exported top-level constants (simple `export let` declarations evaluated at compile time)
  public Dictionary<string, object> ExportedConstants { get; } = [];

  // Source file path for each type/enum/typealias (for file-scoped visibility checks)
  public Dictionary<string, string> TypeDefSourceFiles { get; } = [];

  // Ambiguous exported type names (same name from different files)
  public HashSet<string> AmbiguousTypeNames { get; } = [];

  // Struct literal result IDs eligible for stack allocation (no escape).
  // Populated by StackPromotionAnalysisPass, consumed by MaxonToStandardConversion.
  public HashSet<int> StackEligibleStructs { get; } = [];

  public void AddFunction(IrFunction<TOp> func) {
    Functions.Add(func);
    if (!_indexDirty) IndexFunction(func);
  }

  /// <summary>
  /// Resolves a generic type alias (e.g. "Entry" with unresolved Key/Value params)
  /// to its concrete monomorphized name (e.g. "____Tuple_Key_Value_String_i64").
  /// Returns the original name if it's already concrete or has no alias info.
  /// </summary>
  public string ResolveConcreteAlias(string typeName) {
    if (!TypeAliasSources.TryGetValue(typeName, out var aliasInfo)) return typeName;
    if (aliasInfo.TypeParams == null || aliasInfo.TypeParams.Count == 0) return typeName;
    if (!aliasInfo.TypeParams.Values.Any(t => t is IrTypeParameterType)) return typeName;

    // Don't resolve if the name is a concrete user-defined type that happens to
    // share its name with an unresolved internal alias (e.g., user's "Entry" type
    // vs Map's "typealias Entry = (Key, Value)")
    if (TypeDefs.TryGetValue(typeName, out var typeDef) && typeDef is IrStructType st
        && !st.Fields.Any(f => f.Type is IrTypeParameterType))
      return typeName;

    foreach (var (candidateName, candidateInfo) in TypeAliasSources) {
      if (candidateName == typeName) continue;
      if (candidateInfo.SourceTypeName != aliasInfo.SourceTypeName) continue;
      if (candidateInfo.TypeParams == null) continue;
      if (candidateInfo.TypeParams.Values.Any(t => t is IrTypeParameterType)) continue;
      return candidateName;
    }
    return typeName;
  }

  public IrModule<TOp> Clone() {
    var clone = new IrModule<TOp> {
      EntryFunctionName = EntryFunctionName
    };
    foreach (var func in Functions)
      clone.AddFunction(func.DeepClone());
    clone.RdataEntries.AddRange(RdataEntries);
    clone.SymdataEntries.AddRange(SymdataEntries);
    clone.UcddataEntries.AddRange(UcddataEntries);
    clone.Globals.AddRange(Globals);
    foreach (var (k, v) in TypeDefs) clone.TypeDefs[k] = v;
    foreach (var (k, v) in FunctionDefaults) clone.FunctionDefaults[k] = v;
    foreach (var (k, v) in TypeAliasSources) clone.TypeAliasSources[k] = v;
    foreach (var (k, v) in ConstantArrayLiterals) clone.ConstantArrayLiterals[k] = v;
    foreach (var (k, v) in InterfaceAssociatedTypes) clone.InterfaceAssociatedTypes[k] = v;
    foreach (var (k, v) in PrimitiveConformances) clone.PrimitiveConformances[k] = [.. v];
    clone.ConditionalConformances.AddRange(ConditionalConformances);
    clone.DeferredGlobalInits.AddRange(DeferredGlobalInits);
    foreach (var n in NonExportedTypeNames) clone.NonExportedTypeNames.Add(n);
    foreach (var (k, v) in GlobalVarInfos) clone.GlobalVarInfos[k] = v;
    foreach (var n in NonExportedGlobalVarNames) clone.NonExportedGlobalVarNames.Add(n);
    foreach (var (k, v) in TypeDefSourceFiles) clone.TypeDefSourceFiles[k] = v;
    foreach (var n in AmbiguousTypeNames) clone.AmbiguousTypeNames.Add(n);
    clone.TagTable.AddRange(TagTable);
    clone.TagNames.AddRange(TagNames);
    foreach (var (k, v) in ExportedConstants) clone.ExportedConstants[k] = v;
    foreach (var n in StackEligibleStructs) clone.StackEligibleStructs.Add(n);
    return clone;
  }

  public void Merge(IrModule<TOp> other) {
    // Add or replace functions - replace stubs (no body) with full functions (with body)
    var existingByName = Functions.ToDictionary(f => f.Name);
    foreach (var func in other.Functions) {
      if (existingByName.TryGetValue(func.Name, out var existing)) {
        if (func.Body.Blocks.Count > 0 && existing.Body.Blocks.Count == 0) {
          RemoveFunction(existing);
          AddFunction(func);
          existingByName[func.Name] = func;
        } else if (func.Body.Blocks.Count > 0 && existing.Body.Blocks.Count > 0
                   && !ReferenceEquals(func, existing)) {
          throw new CompileError(ErrorCode.SemanticDuplicateDefinition,
            $"Duplicate function '{func.Name}'", func.SourceLine, func.SourceColumn);
        }
      } else {
        AddFunction(func);
        existingByName[func.Name] = func;
      }
    }
    RdataEntries.AddRange(other.RdataEntries);
    SymdataEntries.AddRange(other.SymdataEntries);
    UcddataEntries.AddRange(other.UcddataEntries);
    foreach (var global in other.Globals) {
      if (!Globals.Any(g => g.Name == global.Name))
        Globals.Add(global);
    }
    foreach (var (k, v) in other.TypeDefs)
      TypeDefs[k] = v;
    foreach (var (k, v) in other.FunctionDefaults) FunctionDefaults.TryAdd(k, v);
    foreach (var (k, v) in other.TypeAliasSources) {
      if (TypeAliasSources.TryGetValue(k, out var existing)
          && (existing.IsExported || existing.IsStdlib)
          && (v.IsExported || v.IsStdlib)
          && existing.SourceFilePath != v.SourceFilePath)
        AmbiguousTypeNames.Add(k);
      TypeAliasSources.TryAdd(k, v);
    }
    foreach (var n in other.NonExportedTypeNames) NonExportedTypeNames.Add(n);
    foreach (var (k, v) in other.GlobalVarInfos) GlobalVarInfos.TryAdd(k, v);
    foreach (var n in other.NonExportedGlobalVarNames) NonExportedGlobalVarNames.Add(n);
    foreach (var (k, v) in other.TypeDefSourceFiles) TypeDefSourceFiles.TryAdd(k, v);
    foreach (var n in other.AmbiguousTypeNames) AmbiguousTypeNames.Add(n);
    foreach (var (k, v) in other.ConstantArrayLiterals) ConstantArrayLiterals.TryAdd(k, v);
    foreach (var (k, v) in other.InterfaceAssociatedTypes) InterfaceAssociatedTypes.TryAdd(k, v);
    foreach (var init in other.DeferredGlobalInits) {
      if (!DeferredGlobalInits.Any(d => d.Name == init.Name))
        DeferredGlobalInits.Add(init);
    }
  }
}
