using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Passes;

namespace MaxonSharp.Compiler.Ir.Core;

public class IrGlobal(string name, IrType type, IrAttribute? initValue = null) {
  public string Name { get; } = name;
  public IrType Type { get; } = type;
  public IrAttribute? InitValue { get; } = initValue;
}

// Represents a type alias with its source type, type parameter substitutions, and visibility metadata.
// IsExported and IsModuleVisible are mutually exclusive (enforced at the parser).
public record TypeAliasInfo(string SourceTypeName, Dictionary<string, IrType>? TypeParams,
    bool IsExported = false, bool IsStdlib = false, string? SourceFilePath = null, string? OwnerTypeName = null,
    bool IsModuleVisible = false) {
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

// Metadata for a module-level global variable (stored in IrModule.GlobalVarInfos for cross-file seeding).
// IsExported and IsModuleVisible are mutually exclusive (enforced at the parser).
public record GlobalVarMetadata(MaxonValueKind Kind, bool Mutable, string? EnumTypeName = null, string? TypeName = null, bool IsLazy = false,
    bool IsExported = false, bool IsModuleVisible = false, string? SourceFilePath = null);

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
  // Trailing dotted suffix of the base name (2+ segments) → list of functions.
  // Drives "qualified-method suffix resolution" like `Array.push` matching
  // `stdlib.Array.push` or `stdlib.collections.Array.push`. Excludes the full
  // base name (covered by _baseNameIndex) and the 1-segment short name
  // (covered by _shortNameIndex). Overload-mangled variants (`$T`) share the
  // base name, so both land under the same suffix keys.
  private readonly Dictionary<string, List<IrFunction<TOp>>> _suffixIndex = [];
  // Non-terminal dot-segment of the base name → list of functions. For
  // `stdlib.Foo.bar` both `stdlib` and `Foo` map to this function. Drives
  // "all methods of type T" queries used by monomorphization
  // (CollectNeededSpecializations' per-alias walk). Skips the last segment
  // since that's the method name, not the owning type.
  private readonly Dictionary<string, List<IrFunction<TOp>>> _methodsByTypeIndex = [];

  // Lazy shared call graph. Built on first access and invalidated together
  // with the function index: any structural change that dirties the function
  // index also dirties the call graph. Passes that mutate function bodies
  // (add/remove call ops) without changing the function list must call
  // InvalidateCallGraph() explicitly.
  private IrCallGraph<TOp>? _callGraph;
  public IrCallGraph<TOp> CallGraph => _callGraph ??= new IrCallGraph<TOp>(this, ResolveCallGraphDialect());

  private static CallGraphDialect<TOp> ResolveCallGraphDialect() {
    if (typeof(TOp) == typeof(MaxonOp))
      return (CallGraphDialect<TOp>)(object)CallGraphDialects.Maxon;
    if (typeof(TOp) == typeof(StandardOp))
      return (CallGraphDialect<TOp>)(object)CallGraphDialects.Standard;
    throw new InvalidOperationException($"No CallGraphDialect registered for op type {typeof(TOp).Name}");
  }

  public void InvalidateCallGraph() {
    _callGraph?.Invalidate();
  }

  /// <summary>
  /// Marks the Functions index as stale so it will be fully rebuilt on next
  /// access. Call this after direct mutations to Functions (e.g. renaming a
  /// function's Name in place) when you can't use RenameFunction.
  /// </summary>
  public void InvalidateFunctionIndex() {
    _indexDirty = true;
    _callGraph?.Invalidate();
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
    _callGraph?.Invalidate();
  }

  private void EnsureFunctionIndex() {
    if (!_indexDirty) return;
    _exactIndex.Clear();
    _baseNameIndex.Clear();
    _shortNameIndex.Clear();
    _suffixIndex.Clear();
    _methodsByTypeIndex.Clear();
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
    UpdateNameIndices(f, add: true);
  }

  private void UnindexFunction(IrFunction<TOp> f) {
    if (_indexDirty) return; // will be rebuilt from scratch anyway
    if (_exactIndex.TryGetValue(f.Name, out var indexed) && ReferenceEquals(indexed, f))
      _exactIndex.Remove(f.Name);
    UpdateNameIndices(f, add: false);
  }

  /// <summary>
  /// Adds or removes <paramref name="f"/> from every name-keyed index in one
  /// pass. Splits baseName on `.` and emits:
  ///   - base name → _baseNameIndex
  ///   - last segment → _shortNameIndex
  ///   - each trailing multi-segment suffix → _suffixIndex
  ///   - each non-terminal segment → _methodsByTypeIndex (deduped by name, so
  ///     pathological bases like `Foo.Foo.bar` don't index `f` under `Foo`
  ///     twice and cause monomorphization to specialize it twice).
  /// </summary>
  private void UpdateNameIndices(IrFunction<TOp> f, bool add) {
    var baseName = StripOverloadSuffix(f.Name);
    UpdateList(_baseNameIndex, baseName, f, add);

    // Single linear walk over baseName: record dot positions as we go and emit
    // the suffix/methodsByType keys against those positions instead of calling
    // IndexOf in a loop (each IndexOf is itself O(remaining-length)).
    int len = baseName.Length;
    int lastDot = -1;
    // Most module names have at most a handful of dots (e.g. "a.b.c.d" — 3
    // dots). Stack-allocate a small array; fall back to growing if it
    // overflows on pathological inputs.
    Span<int> dotPositions = stackalloc int[16];
    int dotCount = 0;
    int[]? overflow = null;
    for (int i = 0; i < len; i++) {
      if (baseName[i] != '.') continue;
      if (dotCount < dotPositions.Length) {
        dotPositions[dotCount] = i;
      } else {
        overflow ??= new int[len];
        overflow[dotCount] = i;
      }
      dotCount++;
      lastDot = i;
    }
    if (lastDot < 0) return;

    UpdateList(_shortNameIndex, baseName[(lastDot + 1)..], f, add);

    HashSet<string>? seenSegments = null;
    int segStart = 0;
    for (int k = 0; k < dotCount; k++) {
      int pos = k < dotPositions.Length ? dotPositions[k] : overflow![k];
      if (pos < lastDot)
        UpdateList(_suffixIndex, baseName[(pos + 1)..], f, add);
      var segment = baseName[segStart..pos];
      seenSegments ??= [];
      if (seenSegments.Add(segment))
        UpdateList(_methodsByTypeIndex, segment, f, add);
      segStart = pos + 1;
      if (pos == lastDot) break;
    }
  }

  private static void UpdateList(Dictionary<string, List<IrFunction<TOp>>> index, string key, IrFunction<TOp> f, bool add) {
    if (add) {
      if (!index.TryGetValue(key, out var list)) {
        list = [];
        index[key] = list;
      }
      list.Add(f);
    } else if (index.TryGetValue(key, out var list)) {
      list.Remove(f);
      if (list.Count == 0) index.Remove(key);
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

  /// <summary>
  /// Returns all functions whose base name (any `$...` overload suffix stripped)
  /// ends with <c>.qualifiedSuffix</c>. Used to resolve partial qualifications
  /// like `Array.push` against fully-qualified names such as
  /// `stdlib.Array.push` or `stdlib.collections.Array.push`. The suffix must be
  /// a multi-segment dotted name; single-segment names should go through
  /// <see cref="FindFunctionsByShortName"/>, full names through
  /// <see cref="FindFunctionsByBaseName"/>.
  /// </summary>
  public IReadOnlyList<IrFunction<TOp>> FindFunctionsByQualifiedSuffix(string qualifiedSuffix) {
    EnsureFunctionIndex();
    return _suffixIndex.TryGetValue(qualifiedSuffix, out var list) ? list : (IReadOnlyList<IrFunction<TOp>>)[];
  }

  /// <summary>
  /// Returns all functions whose base name has <paramref name="typeName"/>
  /// as a non-terminal dot-segment — i.e. functions that look like
  /// <c>typeName.method</c> or <c>prefix.typeName.method</c>. This matches
  /// the old <c>StartsWith(typeName + ".") || Contains("." + typeName + ".")</c>
  /// pattern used by monomorphization to find all methods belonging to a
  /// source type across every namespace. Function bodies of free functions in
  /// a file whose last-but-one path segment coincidentally matches a type
  /// name will also land here; callers already filter those out via
  /// <c>NeedsSpecializationForType</c>.
  /// </summary>
  public IReadOnlyList<IrFunction<TOp>> FindMethodsByType(string typeName) {
    EnsureFunctionIndex();
    return _methodsByTypeIndex.TryGetValue(typeName, out var list) ? list : (IReadOnlyList<IrFunction<TOp>>)[];
  }

  public void RemoveFunction(IrFunction<TOp> func) {
    if (Functions.Remove(func)) {
      UnindexFunction(func);
      _callGraph?.Invalidate();
    }
  }

  public int RemoveFunctionsWhere(Predicate<IrFunction<TOp>> match) {
    int removed = Functions.RemoveAll(f => {
      if (!match(f)) return false;
      UnindexFunction(f);
      return true;
    });
    if (removed > 0) _callGraph?.Invalidate();
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

  // Reverse index: sourceTypeName -> aliases for that source. Hot during
  // monomorphization (TypeSubstitution.FindConcreteAlias used to scan every
  // alias linearly). Lazily (re)built when TypeAliasSources.Count differs from
  // the last snapshot — covers the rare bulk writes (parser pre-scan, module
  // merge, lowering's copy pass). The hot writer
  // (TypeSubstitution.FindConcreteAlias auto-create) goes through
  // RegisterTypeAlias to keep the index incrementally correct without
  // triggering a rebuild.
  private readonly Dictionary<string, List<(string AliasName, TypeAliasInfo Info)>> _aliasesBySource = [];
  private int _aliasesBySourceSnapshotCount = -1;
  private static readonly IReadOnlyList<(string AliasName, TypeAliasInfo Info)> EmptyAliasList = [];

  /// <summary>
  /// Records a (alias → source) entry in both <see cref="TypeAliasSources"/>
  /// and the reverse index. Use this for adds during the monomorphization
  /// hot path; bulk pre-monomorph writers can keep writing to the dictionary
  /// directly — the index notices and rebuilds on next read.
  /// </summary>
  public void RegisterTypeAlias(string aliasName, TypeAliasInfo info) {
    bool existed = TypeAliasSources.ContainsKey(aliasName);
    TypeAliasSources[aliasName] = info;
    if (existed) {
      // Overwrite — index entries may now be stale; force rebuild on next read.
      _aliasesBySourceSnapshotCount = -1;
      return;
    }
    if (_aliasesBySourceSnapshotCount == TypeAliasSources.Count - 1) {
      // Index is fresh and our add is the only one. Append directly.
      AddToAliasesBySourceIndex(aliasName, info);
      _aliasesBySourceSnapshotCount = TypeAliasSources.Count;
    }
    // Otherwise the index was already stale; leave it stale and let the next
    // reader rebuild from scratch.
  }

  private void EnsureAliasesBySourceIndex() {
    if (_aliasesBySourceSnapshotCount == TypeAliasSources.Count) return;
    _aliasesBySource.Clear();
    foreach (var (aliasName, info) in TypeAliasSources)
      AddToAliasesBySourceIndex(aliasName, info);
    _aliasesBySourceSnapshotCount = TypeAliasSources.Count;
  }

  private void AddToAliasesBySourceIndex(string aliasName, TypeAliasInfo info) {
    if (!_aliasesBySource.TryGetValue(info.SourceTypeName, out var list)) {
      list = [];
      _aliasesBySource[info.SourceTypeName] = list;
    }
    list.Add((aliasName, info));
  }

  /// <summary>
  /// Returns all (aliasName, TypeAliasInfo) pairs whose SourceTypeName matches.
  /// Empty if none. The returned list is the live index storage — do not
  /// mutate; iterate read-only.
  /// </summary>
  public IReadOnlyList<(string AliasName, TypeAliasInfo Info)> GetAliasesBySource(string sourceTypeName) {
    EnsureAliasesBySourceIndex();
    return _aliasesBySource.TryGetValue(sourceTypeName, out var list) ? list : EmptyAliasList;
  }

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

  // Module-visible type/enum/typealias names — visible to files in the same directory subtree
  // as the declaring file. Looked up against TypeDefSourceFiles for the scope check.
  public HashSet<string> ModuleVisibleTypeNames { get; } = [];

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

  // Module-visible global var names — visible to files in the same directory subtree
  // as the declaring file. Looked up against GlobalVarSourceFiles for the scope check.
  public HashSet<string> ModuleVisibleGlobalVarNames { get; } = [];

  // Source file path for each global var (for file-scoped and module-scoped visibility checks)
  public Dictionary<string, string> GlobalVarSourceFiles { get; } = [];

  // Exported top-level constants (simple `export let` declarations evaluated at compile time)
  public Dictionary<string, object> ExportedConstants { get; } = [];

  // Module-visible top-level constants (compile-time values from `module let` declarations).
  // Looked up against ModuleConstantSourceFiles for the scope check.
  public Dictionary<string, object> ModuleVisibleConstants { get; } = [];

  // Source file path for module-visible constants.
  public Dictionary<string, string> ModuleConstantSourceFiles { get; } = [];

  // Source file path for each type/enum/typealias (for file-scoped visibility checks)
  public Dictionary<string, string> TypeDefSourceFiles { get; } = [];

  // Ambiguous exported type names (same name from different files)
  public HashSet<string> AmbiguousTypeNames { get; } = [];

  // Struct literal result IDs eligible for stack allocation (no escape).
  // Populated by StackPromotionAnalysisPass, consumed by MaxonToStandardConversion.
  public HashSet<int> StackEligibleStructs { get; } = [];

  public void AddFunction(IrFunction<TOp> func) {
    // Defensive: replace any existing function with the same name in place.
    // AddFunction has historically allowed silent duplicates, but downstream
    // passes that use `Functions.ToDictionary(f => f.Name)` crash on them.
    if (_indexDirty) {
      // Index is stale — rebuild it so we can do the lookup quickly. This is
      // worth the cost because the alternative is an O(N) linear scan on
      // every AddFunction call.
      EnsureFunctionIndex();
    }
    if (_exactIndex.TryGetValue(func.Name, out var existing) && !ReferenceEquals(existing, func)) {
      // Replace existing function in-place.
      for (int i = 0; i < Functions.Count; i++) {
        if (ReferenceEquals(Functions[i], existing)) {
          Functions[i] = func;
          break;
        }
      }
      UnindexFunction(existing);
      IndexFunction(func);
      _callGraph?.Invalidate();
      return;
    }
    Functions.Add(func);
    IndexFunction(func);
    _callGraph?.NoteAdded(func);
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
    foreach (var n in ModuleVisibleTypeNames) clone.ModuleVisibleTypeNames.Add(n);
    foreach (var (k, v) in GlobalVarInfos) clone.GlobalVarInfos[k] = v;
    foreach (var n in NonExportedGlobalVarNames) clone.NonExportedGlobalVarNames.Add(n);
    foreach (var n in ModuleVisibleGlobalVarNames) clone.ModuleVisibleGlobalVarNames.Add(n);
    foreach (var (k, v) in GlobalVarSourceFiles) clone.GlobalVarSourceFiles[k] = v;
    foreach (var (k, v) in TypeDefSourceFiles) clone.TypeDefSourceFiles[k] = v;
    foreach (var n in AmbiguousTypeNames) clone.AmbiguousTypeNames.Add(n);
    clone.TagTable.AddRange(TagTable);
    clone.TagNames.AddRange(TagNames);
    foreach (var (k, v) in ExportedConstants) clone.ExportedConstants[k] = v;
    foreach (var (k, v) in ModuleVisibleConstants) clone.ModuleVisibleConstants[k] = v;
    foreach (var (k, v) in ModuleConstantSourceFiles) clone.ModuleConstantSourceFiles[k] = v;
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
    foreach (var n in other.ModuleVisibleTypeNames) ModuleVisibleTypeNames.Add(n);
    foreach (var (k, v) in other.GlobalVarInfos) GlobalVarInfos.TryAdd(k, v);
    foreach (var n in other.NonExportedGlobalVarNames) NonExportedGlobalVarNames.Add(n);
    foreach (var n in other.ModuleVisibleGlobalVarNames) ModuleVisibleGlobalVarNames.Add(n);
    foreach (var (k, v) in other.GlobalVarSourceFiles) GlobalVarSourceFiles.TryAdd(k, v);
    foreach (var (k, v) in other.TypeDefSourceFiles) TypeDefSourceFiles.TryAdd(k, v);
    foreach (var n in other.AmbiguousTypeNames) AmbiguousTypeNames.Add(n);
    foreach (var (k, v) in other.ModuleVisibleConstants) ModuleVisibleConstants.TryAdd(k, v);
    foreach (var (k, v) in other.ModuleConstantSourceFiles) ModuleConstantSourceFiles.TryAdd(k, v);
    foreach (var (k, v) in other.ConstantArrayLiterals) ConstantArrayLiterals.TryAdd(k, v);
    foreach (var (k, v) in other.InterfaceAssociatedTypes) InterfaceAssociatedTypes.TryAdd(k, v);
    foreach (var init in other.DeferredGlobalInits) {
      if (!DeferredGlobalInits.Any(d => d.Name == init.Name))
        DeferredGlobalInits.Add(init);
    }
  }
}
