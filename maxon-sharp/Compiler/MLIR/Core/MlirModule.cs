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
//
// ConstParams is here for the same reason TypeParams is: this record is the ONLY description of the
// instance monomorphization has, and TypeSubstitution.FindConcreteAlias decides from it whether a
// declared alias already names the instance in hand. Without it, `Slot = Vector with 4 Element`
// specialized to Int matched a declared `Vec3` and every call on the field went to the 3-element
// family — the parser had already minted the right type, and this table then re-decided it wrongly.
public record TypeAliasInfo(string SourceTypeName, Dictionary<string, IrType>? TypeParams,
    bool IsExported = false, bool IsStdlib = false, string? SourceFilePath = null, string? OwnerTypeName = null,
    bool IsModuleVisible = false, Dictionary<string, long>? ConstParams = null) {
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

// One top-level generic typealias, as the whole-project declaration pass sees it, indexed by the
// INSTANCE it names (see IrModule.DeclaredGenericAliases). Visibility AND the declaring file travel
// with it because a foreign file's parser may be the one that first registers the alias, and the
// record it publishes has to describe THAT declaration rather than its own: a narrower record stops
// SeedFromModule seeding the alias, and a record naming the borrowing file makes that file look like
// the declarer, at which point its own PreScan files the alias as file-private and every other file
// loses the type outright (measured: 45 files, `Unknown type: ValueIdArray`).
// ConstArgs travels too, because a borrowing parser registers the declaration from THIS record and
// nothing else: without it a `typealias Vec3 = Vector with 3 Int` re-registered on another file's
// behalf would come back capacity-less, which is the same instance losing part of its identity.
public record DeclaredGenericAlias(string Name, bool IsExported, bool IsModuleVisible, bool IsStdlib,
    string? SourceFilePath, Dictionary<string, long>? ConstArgs);

/// <summary>
/// One name a generic instance could be compiled under, with the only fact that outranks a
/// spelling. A record rather than two loose parameters because
/// <see cref="InstanceNaming.Outranks"/> is asymmetric: transposing candidate and incumbent inverts
/// the rule, and four positional arguments make that a silent mistake instead of a compile error.
/// </summary>
public readonly record struct InstanceNameCandidate(string Name, bool IsStdlib);

/// <summary>
/// ⭐ WHICH NAME A GENERIC INSTANCE IS COMPILED UNDER — the whole of that rule, in one place, for
/// every party that has to agree about it.
///
/// A generic instance may be named more than once: a project may declare <c>ValueIdArray</c> where
/// another file declares <c>IdArray</c> for the same <c>Array with ValueId</c>, and the compiler may
/// itself have minted the structural <c>__Array_ValueId</c> beside them. All of them denote the same
/// type (<see cref="IrStructType.InstanceKey"/> is what says so), so the choice between them decides
/// nothing but the name the emitted symbols carry — which is exactly why it was never noticed that
/// the choice was made by FILE ORDER. Five sites asked it, four of them by taking the first entry a
/// dictionary happened to hand back:
/// <see cref="Passes.TypeSubstitution"/>'s <c>FindConcreteAlias</c>, the parser's return-type search,
/// its field-alias reuse scan and its map-literal scan. Only <c>Parser.RecordDeclaredGenericAlias</c>
/// answered it order-independently, and this is that answer, moved out so the other four share it
/// rather than each keeping a copy.
///
/// MEASURED, on one unchanged program (two files declaring <c>ZIter</c> and <c>AIter</c> for one
/// <c>ArrayIterator with String</c>, and a third file using the instance without naming it): the
/// third file's emitted calls read <c>ZIter.index</c> in natural source order and <c>AIter.index</c>
/// under <c>MAXON_SOURCE_ORDER=reverse</c>. Same program, same files, different binary.
///
/// THE RANK. <c>stdlib</c> outranks a project declaration — it is compiled first and every project
/// already resolves against it — and within one rank the ordinal-smallest name wins.
///
/// ⚠ ORDINAL IS A TIE-BREAK, NOT A PREFERENCE FOR DECLARED NAMES OVER SYNTHESIZED ONES, and it does
/// not always fall that way. It usually does — a synthesized name is <c>__</c>-prefixed and <c>_</c>
/// (0x5F) sorts after every uppercase letter — but a declared name need not begin with a letter, and
/// this rung's own regenerated goldens show the other outcome: a spec batch's rewritten
/// <c>_b_push_and_get_BoolArray</c> LOSES to <c>__Array_i1</c>. That is fine and is the point. What
/// this rule owes is one answer for one program, not a particular one; a rule that preferred declared
/// names would still have to break ties between two of them, and would break them by file order
/// again.
/// </summary>
public static class InstanceNaming {
  /// <summary>
  /// Whether <paramref name="candidate"/> should replace <paramref name="incumbent"/> as the name
  /// for the instance both denote.
  /// </summary>
  public static bool Outranks(InstanceNameCandidate candidate, InstanceNameCandidate incumbent) {
    if (candidate.IsStdlib != incumbent.IsStdlib) return candidate.IsStdlib;

    return string.CompareOrdinal(candidate.Name, incumbent.Name) < 0;
  }

  /// <summary>
  /// ⭐ WHETHER A REGISTERED ALIAS REALLY NAMES THE INSTANCE IN HAND — the whole test, asked by both
  /// reuse scans (the parser's <c>BestKnownNameForInstance</c> and monomorphization's
  /// <c>FindConcreteAlias</c>), which read their candidates from different tables and must not come
  /// to different conclusions about the same pair.
  ///
  /// Three things disqualify a candidate, and the third is the subtle one:
  /// <list type="number">
  /// <item>A candidate still holding a type PARAMETER is a declaration caught mid-resolution, not a
  ///   competing instance.</item>
  /// <item>A different <see cref="IrStructType.InstanceKey"/> is a different instance.</item>
  /// <item>⭐ A CONTESTED instance is adopted on its IDENTITY, never on the by-name key. Two files'
  ///   <c>typealias Cells = Array with Cell</c> over different <c>Cell</c> ranges are ONE key —
  ///   <c>Array&lt;Element=Cell&gt;</c>, because the key reads a type argument by NAME and must (see
  ///   its header) — and TWO instances, which is exactly why the contested mint spells the argument's
  ///   RANGE into the name it gives one of them. Matching such a candidate on the weaker spelling
  ///   hands one file's storage to the other: measured, an <c>export typealias Cells</c> over
  ///   <c>int(0 to 100000)</c> whose emitted <c>push</c>/<c>get</c> called a file-private neighbour's
  ///   <c>Array_Cell_i64_0to255</c> and read its 70000 back one byte wide, as 112.</item>
  /// </list>
  ///
  /// Which candidates are contested is DERIVED — a registered alias is one exactly when its name is
  /// <see cref="IrStructType.ContestedInstanceName"/> for its own arguments — rather than kept in a
  /// second set that could fall out of step with the mint. Every other candidate is unaffected: two
  /// names for one uncontested instance really do denote it, and demanding identity of them would
  /// mint a duplicate wherever a pass holds a differently-spelled but equivalent argument.
  /// </summary>
  public static bool CandidateDenotesInstance(string candidateName, string sourceName,
      IReadOnlyDictionary<string, IrType> candidateArgs, IReadOnlyDictionary<string, long>? candidateConstArgs,
      IReadOnlyDictionary<string, IrType> wantedArgs, IReadOnlyDictionary<string, long>? wantedConstArgs) {
    if (candidateArgs.Values.Any(t => t is IrTypeParameterType)) return false;
    if (IrStructType.InstanceKey(sourceName, candidateArgs, candidateConstArgs)
        != IrStructType.InstanceKey(sourceName, wantedArgs, wantedConstArgs)) return false;
    if (candidateName != IrStructType.ContestedInstanceName(sourceName, candidateArgs, candidateConstArgs))
      return true;

    return IrStructType.InstanceIdentity(sourceName, candidateArgs, candidateConstArgs)
        == IrStructType.InstanceIdentity(sourceName, wantedArgs, wantedConstArgs);
  }

  /// <summary>
  /// A generic instantiation as the author would have written it — <c>Map with (A_B, C)</c>. The
  /// arguments are ordered by PARAMETER NAME rather than by declaration order, which is the one
  /// ordering available from a substitution dictionary and the same one
  /// <see cref="IrStructType.InstanceKey"/> uses, so the two halves of a collision report cannot
  /// disagree about which argument is which.
  /// </summary>
  public static string RenderInstantiation(string sourceName,
      IReadOnlyDictionary<string, IrType> typeArgs, IReadOnlyDictionary<string, long>? constArgs) {
    var arguments = IrStructType.ConstArgSegments(constArgs)
      .Concat(typeArgs.OrderBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => IrType.FormatAsSourceName(kv.Value)))
      .ToList();

    return arguments.Count == 1
      ? $"{sourceName} with {arguments[0]}"
      : $"{sourceName} with ({string.Join(", ", arguments)})";
  }
}

// Which generic INSTANCE one top-level alias NAME was declared over, and by which file — the two
// facts the contest test needs (see IrModule.DeclaredAliasInstances). The instance is carried as
// IrStructType.InstanceIdentity's spelling rather than as types, because the declaration pass runs
// before any source struct has its fields and the type OBJECTS it holds are re-made by every later
// pass; the spelling is the part that is settled at declaration time and stays settled.
public record DeclaredAliasInstance(string Identity, string? SourceFilePath);

// Metadata for constant array literals that can be placed in .rdata
public record ConstantArrayLiteralInfo(string RdataLabel, long[] Values, bool IsMutable, int ElementSize, bool IsBitPacked = false);

// The record a CONSTANT EMPTY container factory builds — an array literal with zero elements, so
// every field of it is a compile-time constant: no buffer, no length, no capacity, no parent. Only
// the wrapper type (which fixes the record size and the type tag) and the element width vary, so
// those are the whole of it. An empty `Array with Integer` and an empty `Array with String` are
// DIFFERENT records: same width, different element release, different type tag.
public record ConstantEmptyContainerInfo(string TypeName, int ElementSize);

/// One place the lowering MUST insert a materialise: immediately before the op this is keyed by, the
/// local binding <paramref name="Binding"/> is rebound from the shared immortal empty record to a
/// private one, so the write that op performs lands on a record the binding owns.
/// <paramref name="Record"/> says what to build — the same constant the factory would have returned.
///
/// This is the compiler emitting the IR this codebase already writes BY HAND: the 75 `sharedEmptyX`
/// anchors in maxon-shv2 are all `x = materializedX(x)` before a write, for exactly this reason.
public record MaterialisePoint(string Binding, ConstantEmptyContainerInfo Record);

// Metadata for a module-level global variable (stored in IrModule.GlobalVarInfos for cross-file seeding).
// IsExported and IsModuleVisible are mutually exclusive (enforced at the parser).
public record GlobalVarMetadata(MaxonValueKind Kind, bool Mutable, string? EnumTypeName = null, string? TypeName = null, bool IsLazy = false,
    bool IsExported = false, bool IsModuleVisible = false, string? SourceFilePath = null);

// Deferred global variable initialization: stores tokens for expressions that must be evaluated at main() entry
public record DeferredGlobalInit(string Name, List<Token> Tokens, int TokenStart, int TokenEnd, bool IsMutable, int Line, int Column, string? SourceFilePath = null);

// An unfolded top-level `let` whose initializer IS a constant expression, recorded whole-program by
// Parser.PreScanTopLevelConstantDecls before any file folds anything. Cross-file constant resolution
// is by DECLARATION, not by seeding an already-folded VALUE from a file that got pre-scanned first —
// so `let A = FOREIGN` no longer depends on the order the filesystem hands the compiler its files.
//
// Tokens travels with the record for the same reason DeferredGlobalInit carries it: the initializer
// is folded by whichever file first DEMANDS the constant, whose token list is not the declarer's, so
// TokenStart indexes nothing the folding parser holds. SourceFilePath is what the visibility rule is
// applied against — collecting every file's declarations must not widen any file's SCOPE.
public record TopLevelConstantDecl(string Name, List<Token> Tokens, int TokenStart, int TokenEnd,
    int Line, int Column, bool IsExported, bool IsModuleVisible, string? SourceFilePath);

// One type name a file declares, as Compiler.PreRegisterTypeNames sees it — struct, enum/union,
// interface or typealias — reported through that pass's optional callback rather than stored.
//
// It is NOT derivable from IrModule.TypeDefSourceFiles, which is what FlatNamespaceCheck would
// otherwise have read: that map is keyed by NAME, so a second file declaring an existing name
// overwrites the first and the map keeps only the winner — precisely the declaration a
// duplicate-name report has to be able to name. (It is also not written for interfaces at all.)
public record TopLevelTypeDeclaration(string Name, bool IsExported, bool IsModuleVisible,
    string? SourceFilePath, int Line, int Column);

// One top-level `let`/`var` declaration as Parser.WalkTopLevelValueDecls sees it, before anything has
// decided what to do with it. Deliberately WIDER than TopLevelConstantDecl above, which is the subset
// another file may fold: a `var` and a runtime initializer are still names this file publishes into the
// program's one flat top-level namespace, which is the question FlatNamespaceCheck asks.
public record TopLevelValueDeclaration(string Name, int TokenStart, int TokenEnd, int Line, int Column,
    bool IsExported, bool IsModuleVisible, bool IsMutable);

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

  /// Marks only the Functions name-index stale, leaving the call graph as-is.
  /// Use when a function was renamed in place (its body, and therefore its
  /// outgoing call edges, are unchanged) and any accompanying call-site
  /// rewrites are tolerated by the consumer as a stale superset — e.g. the
  /// monomorphization interface-alias loop, whose only in-loop graph reader
  /// (the transitive GetCallers scan) re-reads ops to confirm matches and is
  /// followed by an unconditional InvalidateCallGraph before any other pass.
  /// Renaming in place does not corrupt the graph: callers are keyed by object
  /// reference and callee names embedded in ops are unchanged by the rename.
  public void InvalidateFunctionIndexOnly() {
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

  // Every top-level generic typealias the compilation unit DECLARES, keyed by the generic INSTANCE
  // it names (IrStructType.InstanceKey: source type plus its type AND const arguments by name). Filled
  // whole-project by Compiler's declaration pass BEFORE any file specializes anything, which is the
  // whole point of it: RegisterConcreteTypeAlias has to mint a name for a field whose type is a
  // generic instance, and asking "does the project already call this instance something?" against
  // the aliases registered SO FAR answers from the order the filesystem handed over the files. That
  // is how `Array_ValueId` came to be emitted beside the `ValueIdArray` declared one file later —
  // ~90 duplicated functions across 13 instances, with which name won decided by readdir order.
  //
  // Written once per compilation unit, straight into this module (never through Merge, which only
  // ever carries a full parse's output). Clone copies it because the parsed-stdlib module is cached
  // and a project compile must inherit stdlib's DECLARATIONS while sharing none of its objects.
  public Dictionary<string, DeclaredGenericAlias> DeclaredGenericAliases { get; } = [];

  // The same declarations, indexed the other way round — by alias NAME, carrying the INSTANCE that
  // name was declared over. The index above answers "what does the project call this instance?"; this
  // one answers "do two files mean two instances by this name?", which is the contest
  // (<see cref="ContestedGenericAliasNames"/>). Filled in the same whole-project pass and read only
  // to fill that set, so the answer is settled before the first file mints anything — see the ⭐ note
  // on ContestedGenericAliasNames for why a LATE answer is not merely late but wrong.
  public Dictionary<string, DeclaredAliasInstance> DeclaredAliasInstances { get; } = [];

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

  // Functions that do nothing but build and return a constant EMPTY container (`Array.create()`):
  // function name -> the record they return. Populated by ConstantArrayAnalysisPass; read by
  // LiteralCoverageAnalysisPass (which counts each such CALL as a literal site) and by
  // MaxonToStandardConversion (which replaces a never-written-through call with the shared record).
  // Keyed by FUNCTION rather than by literal site because the factory body is shared by every
  // caller: one caller pushing into its result must not cost every other caller its empty record.
  public Dictionary<string, ConstantEmptyContainerInfo> ConstantEmptyContainerFactories { get; } = [];

  // Interface associated type names (interfaceName -> list of 'uses' type names)
  public Dictionary<string, List<string>> InterfaceAssociatedTypes { get; } = [];

  // Primitive type conformances from extension blocks (e.g., "int" -> ["Hashable", "Equatable"])
  public Dictionary<string, List<string>> PrimitiveConformances { get; } = [];

  // Conditional conformances from extension blocks on generic types
  // e.g., "extension Array implements Hashable where Element is Hashable"
  public List<(string SourceTypeName, List<string> Interfaces, Dictionary<string, List<string>> WhereConstraints)> ConditionalConformances { get; } = [];

  // Deferred global var/let initializations from all source files, emitted at start of main()
  public List<DeferredGlobalInit> DeferredGlobalInits { get; } = [];

  // Static-literal escape analysis result (LiteralCoverageAnalysisPass): the MaxonValue result
  // ids of string/byte/char literal sites proven never-mutated, so the lowering may emit each
  // as ONE shared immortal .data record instead of a per-evaluation heap allocation. A sound
  // LOWER BOUND — an id's absence only ever costs a heap allocation, never correctness. Null
  // means the analysis has not run (e.g. a unit test bypassing the pipeline); the lowering
  // then treats every literal as non-eligible.
  public HashSet<int>? StaticEligibleLiteralIds { get; set; }

  // The other half of that verdict: sites whose record is shared even though the program DOES write
  // through them, because every such write has an insertion point here. Keyed by the writing op (by
  // reference — the lowering walks these same objects), because a materialise is a statement about a
  // point in the program, not about a value.
  //
  // ⛔ THE SOUNDNESS DIRECTION HERE IS THE OPPOSITE OF StaticEligibleLiteralIds. A missing entry
  // there costs an allocation; a missing entry HERE is a write through a record shared with every
  // other empty container of its type. So a site reaches this map only when the analysis holds a
  // placement for EVERY sink marking its component — see LiteralCoverageAnalysisPass.PlanMaterialise,
  // which fails closed on anything it cannot place.
  //
  // Deliberately NOT copied by Clone/Merge, unlike ConstantArrayLiterals: those keys are ids, these
  // are op REFERENCES, and a clone re-makes every op — copied entries would name ops in the wrong
  // module. The pass that fills it runs after both, so there is nothing to carry.
  public Dictionary<TOp, List<MaterialisePoint>> MaterialisePoints { get; } = [];

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

  // Names the `__DebugStream` builtin interned at compile time (phase names, event names),
  // indexed by the u16 a Log event carries. Index 0 = no name. Embedded in the executable as
  // the MXDS_STRS blob, so the monitor prints a real name and the emitting program never
  // builds a string. Built during MaxonToStandard lowering.
  public List<string?> DebugStreamNames { get; set; } = [];

  // The `--coverage` instrumentation's minted points, in counter order. Populated by the parser
  // (the one place the user's own control flow is still distinguishable from the branches lowering
  // synthesizes) and read by the emitter, which sizes `__cov_image` from it and hands it to the
  // debug-info builder for the sidecar's coverage table. Empty on every other build.
  public CoveragePointTable CoveragePoints { get; set; } = new();

  // Where a `--coverage` binary writes its counters. The compiler knows the output path exactly, so
  // it is baked into the binary rather than derived at run time from the program's own executable
  // path — see RuntimeEmitter.Coverage.cs. Empty on every other build.
  public string CoverageDataPath { get; set; } = "";

  // Global variable metadata for cross-file seeding (name -> kind/mutability/type info)
  public Dictionary<string, GlobalVarMetadata> GlobalVarInfos { get; } = [];

  // Non-exported global var names — filtered when seeding _globalVars to other files
  public HashSet<string> NonExportedGlobalVarNames { get; } = [];

  // Module-visible global var names — visible to files in the same directory subtree
  // as the declaring file. Looked up against GlobalVarSourceFiles for the scope check.
  public HashSet<string> ModuleVisibleGlobalVarNames { get; } = [];

  // Source file path for each global var (for file-scoped and module-scoped visibility checks)
  public Dictionary<string, string> GlobalVarSourceFiles { get; } = [];

  // Every file's top-level constant DECLARATIONS, unfolded, collected before any file folds. This is
  // what makes a cross-file constant reference resolvable no matter which file the compiler reads
  // first; ExportedConstants below carries only the ALREADY-FOLDED values of the files pre-scanned
  // so far, which is necessarily order-dependent and cannot answer a forward reference.
  public List<TopLevelConstantDecl> TopLevelConstantDecls { get; } = [];

  // Exported top-level constants (simple `export let` declarations evaluated at compile time)
  public Dictionary<string, object> ExportedConstants { get; } = [];

  // Module-visible top-level constants (compile-time values from `module let` declarations).
  // Looked up against ModuleConstantSourceFiles for the scope check.
  public Dictionary<string, object> ModuleVisibleConstants { get; } = [];

  // Source file path for module-visible constants.
  public Dictionary<string, string> ModuleConstantSourceFiles { get; } = [];

  // Source file path for each type/enum/typealias (for file-scoped visibility checks)
  public Dictionary<string, string> TypeDefSourceFiles { get; } = [];

  /// <summary>
  /// ⭐ TYPEALIAS NAMES NO FILE CAN CHOOSE BETWEEN, AND — THE PART THAT MADE IT A MAP — WHICH FILES
  /// DECLARED THEM. E3063's whole job is to tell the author what to qualify with, so it must name
  /// EVERY candidate; a bare set of names left <see cref="Passes"/>' caller rebuilding the candidate
  /// list from <see cref="TypeAliasSources"/>, which holds ONE record per name and therefore
  /// structurally cannot answer. Measured: a program with two exported <c>Cell</c>s was told
  /// "Candidates: dirB.Cell" — one candidate, and not always the one the reader was looking at.
  ///
  /// Membership is DERIVED from this map rather than tracked beside it, so a name cannot be ambiguous
  /// with nothing to qualify with, nor have candidates while nothing refuses it.
  /// </summary>
  public Dictionary<string, SortedSet<string>> AmbiguousTypeDeclarers { get; } = [];

  /// <summary>
  /// ⭐ EVERY DECLARATION OF EACH TYPEALIAS NAME, one per declaring file — the SET the ambiguity rule
  /// is a relation over.
  ///
  /// ⚠ IT EXISTS BECAUSE ONE INCUMBENT CANNOT STAND FOR THE SET. The rule was asked of the new
  /// declaration against whichever declaration currently owned <see cref="TypeAliasSources"/>'s single
  /// record — and that owner is picked by a DIFFERENT rule (the widest reach wins), while
  /// <see cref="AliasScope.AreAmbiguous"/> is not transitive. MEASURED: two <c>module typealias
  /// Cell</c>s in one directory are refused on their own, and adding an unrelated project-root
  /// <c>export typealias Cell</c> — which is exempt against each of them by the enclosure rule, and
  /// which is strictly wider so it keeps the record — made the same two files compile. Whether a pair
  /// is legal must not depend on what ELSE the program declares.
  ///
  /// Keyed file-then-name so a file has exactly one declaration of a name here, however many passes
  /// re-walk it: a second entry for one file would be compared against the first as if two files had
  /// declared it.
  /// </summary>
  public Dictionary<string, Dictionary<string, AliasSite>> AliasDeclarationSites { get; } = [];

  /// <summary>
  /// ⭐ THE ONE WRITER of both alias-scope tables: record this declaration, and mark the name
  /// ambiguous against every declaration of it already known. Membership in
  /// <see cref="AmbiguousTypeDeclarers"/> is therefore DERIVED from the sites and cannot describe a
  /// different set of declarations than the sites do.
  ///
  /// ⚠ A LATER RECORD MAY NOT ERASE A TYPE AN EARLIER ONE KNEW. One file's declaration is recorded
  /// by several paths across the pre-scans and the merge, and only some of them hold the type it
  /// binds — <see cref="Merge"/> reads it out of the incoming module's <see cref="TypeDefs"/>, which
  /// is exactly where a file-private contested mint deliberately withholds the bare name. Taking the
  /// last write wholesale would let that path blank out what the parser recorded, and the reader
  /// would silently fall back to the flat table for a declaration the compiler knows the type of.
  /// </summary>
  public void RecordAliasDeclaration(string typeName, AliasSite site) {
    if (!AliasDeclarationSites.TryGetValue(typeName, out var sites)) {
      sites = new Dictionary<string, AliasSite>(StringComparer.Ordinal);
      AliasDeclarationSites[typeName] = sites;
    }

    foreach (var (otherFile, other) in sites) {
      if (otherFile == site.File) continue;
      if (!AliasScope.AreAmbiguous(other, site)) continue;

      AddAmbiguousTypeDeclarers(typeName, otherFile, site.File);
    }

    if (site.DeclaredType == null && sites.TryGetValue(site.File, out var known))
      site = site with { DeclaredType = known.DeclaredType };

    sites[site.File] = site;
  }

  /// <summary>Record that <paramref name="declarerPath"/> is one of the files whose declaration of
  /// <paramref name="typeName"/> makes it ambiguous.</summary>
  private void AddAmbiguousTypeDeclarers(string typeName, params string[] declarerPaths) {
    if (!AmbiguousTypeDeclarers.TryGetValue(typeName, out var declarers)) {
      declarers = new SortedSet<string>(StringComparer.Ordinal);
      AmbiguousTypeDeclarers[typeName] = declarers;
    }
    foreach (var path in declarerPaths) declarers.Add(path);
  }

  /// <summary>
  /// ⭐ THE GENERIC-INSTANCE TYPEALIAS NAMES TWO FILES DECLARE OVER DIFFERENT INSTANCES.
  ///
  /// A plain <c>typealias</c> is file-local (specs/duplicate-typealias.md), so two files may each
  /// declare <c>Cells = Array with Cell</c> over their own <c>Cell</c> and both declarations are
  /// legal. But a generic instance also names a FAMILY OF EMITTED METHODS, and those go into the
  /// program's one flat symbol namespace under the type's own name — so the bare name cannot be
  /// both instances' identity. Last-writer-wins in <see cref="TypeDefs"/> made it whichever file
  /// merged last: MEASURED, a file declaring <c>Cell = int(0 to 100000)</c> pushed 70000 into its
  /// own array and read back 112, because another file's one-byte <c>Cell</c> had decided the
  /// stride, and the same program with the files renamed answered correctly.
  ///
  /// A declaring file therefore registers its instance under the STRUCTURAL name that instance would
  /// have had if nothing declared it (<see cref="IrStructType.InstanceIdentity"/>'s spelling), and
  /// the alias stays a per-file spelling of it.
  ///
  /// ⚠ WHETHER THE BARE NAME STILL NAMES A TYPE IN HERE DEPENDS ON THE DECLARATION'S VISIBILITY, and
  /// this said flatly that it never did. Only a FILE-PRIVATE declarer withholds it
  /// (<c>Parser.PublishesBareNameToModule</c>): nothing outside that file may write the name, so
  /// nothing outside it needs the entry. A <c>module</c> or <c>export</c> declarer is renamed for its
  /// own code but goes on publishing the bare name, because a file in its scope may write that name
  /// and resolves it through this table — withheld, such a reader found only the pre-scan's empty
  /// placeholder and died in lowering.
  ///
  /// ⭐ WHICH INSTANCE THOSE READERS GET IS NO LONGER THE FLAT TABLE'S LAST WRITE. It was, and that
  /// was this record's acknowledged residual for two rungs: an exported <c>Cells</c> over
  /// <c>int(0 to 100000)</c> declared beside an exported or module-visible <c>Cells</c> over
  /// <c>int(0 to 255)</c> read its own 70000 back as 112, with no diagnostic, whichever of them merged
  /// last. A reader now resolves the bare name to the declaration that governs IT
  /// (<see cref="AliasScope.NearestDeclarationFor"/>), and because it does, EVERY contested
  /// declaration this compilation owns is renamed rather than only the ones no foreign file may name
  /// — see <c>Parser.IsRenameableAliasDeclaration</c>, whose reach test that cure retired.
  ///
  /// ⚠ RECORDED BY THE WHOLE-PROJECT DECLARATION PASS (<see cref="DeclaredAliasInstances"/>), which
  /// is the ONLY pass that has read every file and minted nothing — so both declarations are renamed,
  /// the answer does not depend on file order, and, just as importantly, no pass that mints ever sees
  /// this answer CHANGE. Recorded any later it flipped a parameter type's name between two passes,
  /// which registered one function twice and crashed the compiler; see RecordAliasInstanceForContest.
  /// </summary>
  public HashSet<string> ContestedGenericAliasNames { get; } = [];

  // Struct literal result IDs eligible for stack allocation (no escape).
  // Populated by StackPromotionAnalysisPass, consumed by MaxonToStandardConversion.
  public HashSet<int> StackEligibleStructs { get; } = [];

  /// <summary>
  /// Names of the functions that hand their result back in two registers rather than as a
  /// pointer to a heap record. Populated by ValueTupleAbiPass; read by
  /// StackPromotionAnalysisPass and MaxonToStandardConversion.
  ///
  /// This names the functions that DO use the value ABI, rather than the ones excluded from
  /// it, so that the empty set — an uncomputed one — means "every function returns a heap
  /// record". That is the pre-existing convention, so a caller and callee that never consult
  /// this still agree. Naming the exclusions instead would make a missed analysis read as
  /// "everything returns registers", which is a miscompile rather than a missed optimisation.
  /// </summary>
  public HashSet<string> ValueTupleReturnFunctions { get; } = [];

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

  /// <summary>
  /// A copy that shares no object a compile WRITES TO — which is what its one caller, the
  /// process-wide parsed-stdlib cache, needs it to mean. A compile writes to the types it is handed
  /// (see <see cref="TypeGraphCopier"/>), so a clone that copied the function bodies but shared the
  /// TYPE GRAPH left every compile in a process editing the same stdlib. That is board row A4r: the
  /// emitted binary depended on whether another program had been compiled first.
  ///
  /// Four object families are therefore copied outright: FUNCTIONS, the TYPE GRAPH, the OPS, and the
  /// SSA VALUES — and an op's or a value's reference INTO the type graph is rebound through the same
  /// <see cref="TypeGraphCopier"/> as a TypeDef's, so the two stay one object exactly as they were
  /// one object in the original.
  ///
  /// ⚠ What is deliberately still shared, and why it is not the same bug: <see cref="IrAttribute"/>
  /// instances (a struct field's <c>DefaultValue</c>, a global's <c>InitValue</c>, a
  /// <c>FunctionDefaults</c> entry). Every subclass is get-only and no pass writes through one, so an
  /// attribute carries no per-compile conclusion for a later compile to inherit — which is precisely
  /// what ops and types DID carry. If an attribute ever becomes settable, it joins the list above.
  /// </summary>
  public IrModule<TOp> Clone() {
    var clone = new IrModule<TOp> {
      EntryFunctionName = EntryFunctionName
    };
    var typeCopier = new TypeGraphCopier();
    var copyOp = OpCopierForDialect(typeCopier);
    foreach (var func in Functions)
      clone.AddFunction(func.DeepClone(typeCopier, copyOp));
    clone.RdataEntries.AddRange(RdataEntries);
    clone.SymdataEntries.AddRange(SymdataEntries);
    clone.UcddataEntries.AddRange(UcddataEntries);
    foreach (var global in Globals)
      clone.Globals.Add(new IrGlobal(global.Name, typeCopier.Copy(global.Type)!, global.InitValue));
    foreach (var (k, v) in TypeDefs) clone.TypeDefs[k] = typeCopier.Copy(v)!;
    foreach (var (k, v) in FunctionDefaults) clone.FunctionDefaults[k] = v;
    foreach (var (k, v) in TypeAliasSources) clone.TypeAliasSources[k] = CopyAliasInfo(v, typeCopier);
    foreach (var (k, v) in DeclaredGenericAliases) clone.DeclaredGenericAliases[k] = v;
    foreach (var (k, v) in DeclaredAliasInstances) clone.DeclaredAliasInstances[k] = v;
    foreach (var (k, v) in ConstantArrayLiterals) clone.ConstantArrayLiterals[k] = v;
    foreach (var (k, v) in ConstantEmptyContainerFactories) clone.ConstantEmptyContainerFactories[k] = v;
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
    // Both tables are copied rather than the membership being re-derived from the sites: a clone is
    // the same module, and re-deriving would make the copy disagree with the original wherever the
    // rule has changed since the original was built.
    //
    // ⚠ A site's DeclaredType goes through the SAME TypeGraphCopier as the TypeDefs entry above, so
    // the clone's scoped declaration and its type table are ONE object exactly as they were one
    // object in the original. Copied by reference it would hand every compile in the process the
    // cached stdlib module's own types to write to, which is the A4r bug this whole method exists
    // to close, re-entered through the scope table.
    foreach (var (n, sites) in AliasDeclarationSites) {
      var clonedSites = new Dictionary<string, AliasSite>(sites.Count, StringComparer.Ordinal);
      foreach (var (file, site) in sites)
        clonedSites[file] = site with { DeclaredType = typeCopier.Copy(site.DeclaredType) };
      clone.AliasDeclarationSites[n] = clonedSites;
    }
    foreach (var (n, declarers) in AmbiguousTypeDeclarers) clone.AddAmbiguousTypeDeclarers(n, [.. declarers]);
    foreach (var n in ContestedGenericAliasNames) clone.ContestedGenericAliasNames.Add(n);
    clone.TagTable.AddRange(TagTable);
    clone.TagNames.AddRange(TagNames);
    clone.DebugStreamNames.AddRange(DebugStreamNames);
    clone.TopLevelConstantDecls.AddRange(TopLevelConstantDecls);
    foreach (var (k, v) in ExportedConstants) clone.ExportedConstants[k] = v;
    foreach (var (k, v) in ModuleVisibleConstants) clone.ModuleVisibleConstants[k] = v;
    foreach (var (k, v) in ModuleConstantSourceFiles) clone.ModuleConstantSourceFiles[k] = v;
    foreach (var n in StackEligibleStructs) clone.StackEligibleStructs.Add(n);
    foreach (var n in ValueTupleReturnFunctions) clone.ValueTupleReturnFunctions.Add(n);
    return clone;
  }

  /// <summary>
  /// The op copier for this module's dialect. Only the MAXON tier has one, because only the Maxon
  /// tier is ever cloned — the parsed-stdlib cache is a Maxon module. A Standard or target module
  /// reaching here would need its own copier, and getting the identity function instead would be a
  /// silent half-copy, so it is refused out loud.
  ///
  /// It is handed the CALLER's <paramref name="typeCopier"/> rather than making its own, so an op's
  /// type reference and the TypeDef of the same name end up as one object in the clone, exactly as
  /// they were one object in the original.
  /// </summary>
  private static Func<TOp, TOp> OpCopierForDialect(TypeGraphCopier typeCopier) {
    if (typeof(TOp) != typeof(MaxonOp))
      throw new InvalidOperationException(
        $"IrModule<{typeof(TOp).Name}>.Clone has no op copier for that dialect; only the Maxon tier is cloned");

    var opCopier = new OpGraphCopier(typeCopier);
    return op => (TOp)(object)opCopier.Copy((MaxonOp)(object)op!);
  }

  /// An alias's TypeParams dictionary is the alias's own, not the source type's, so the clone needs
  /// its own — and its values name types the clone must reach through its own graph.
  private static TypeAliasInfo CopyAliasInfo(TypeAliasInfo info, TypeGraphCopier typeCopier) {
    if (info.TypeParams == null) return info;

    var typeParams = new Dictionary<string, IrType>(info.TypeParams.Count);
    foreach (var (name, type) in info.TypeParams) typeParams[name] = typeCopier.Copy(type)!;
    return info with { TypeParams = typeParams };
  }

  public void Merge(IrModule<TOp> other) {
    // Add or replace functions - replace stubs (no body) with full functions
    // (with body). Look up via the module's exact-name index (maintained
    // incrementally by Add/RemoveFunction) rather than rebuilding a dictionary
    // of all functions on every merge — the latter made parse O(files ×
    // accumulated-functions), i.e. quadratic in the project size.
    foreach (var func in other.Functions) {
      var existing = FindFunctionByExactName(func.Name);
      if (existing != null) {
        if (func.Body.Blocks.Count > 0 && existing.Body.Blocks.Count == 0) {
          RemoveFunction(existing);
          AddFunction(func);
        } else if (func.Body.Blocks.Count > 0 && existing.Body.Blocks.Count > 0
                   && !ReferenceEquals(func, existing)) {
          throw new CompileError(ErrorCode.SemanticDuplicateDefinition,
            $"Duplicate function '{func.Name}'", func.SourceLine, func.SourceColumn);
        }
      } else {
        AddFunction(func);
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
      // The same question CopyTypeAliasesToModule asks, asked through the same RECORDER rather than
      // of a second hand-written rule. The copy that lived here read "both exported or stdlib,
      // different files", which marks a project export beside a stdlib export of one name — a pair
      // `project-export-shadows-stdlib-export` requires to be legal — and misses every `module`
      // declaration, whose subtree meets an export's whole-program reach just as squarely. It also
      // compared the incoming record against ONE incumbent, which is the hole AliasDeclarationSites
      // exists to close; going through the recorder means this path cannot keep its own answer.
      if (v.SourceFilePath != null)
        RecordAliasDeclaration(k, AliasSite.Of(v, other.TypeDefs.GetValueOrDefault(k)));

      TypeAliasSources.TryAdd(k, v);
    }
    foreach (var (k, sites) in other.AliasDeclarationSites)
      foreach (var site in sites.Values) RecordAliasDeclaration(k, site);
    foreach (var n in other.NonExportedTypeNames) NonExportedTypeNames.Add(n);
    foreach (var n in other.ModuleVisibleTypeNames) ModuleVisibleTypeNames.Add(n);
    foreach (var (k, v) in other.GlobalVarInfos) GlobalVarInfos.TryAdd(k, v);
    foreach (var n in other.NonExportedGlobalVarNames) NonExportedGlobalVarNames.Add(n);
    foreach (var n in other.ModuleVisibleGlobalVarNames) ModuleVisibleGlobalVarNames.Add(n);
    foreach (var (k, v) in other.GlobalVarSourceFiles) GlobalVarSourceFiles.TryAdd(k, v);
    foreach (var (k, v) in other.TypeDefSourceFiles) TypeDefSourceFiles.TryAdd(k, v);
    foreach (var (n, declarers) in other.AmbiguousTypeDeclarers) AddAmbiguousTypeDeclarers(n, [.. declarers]);
    foreach (var n in other.ContestedGenericAliasNames) ContestedGenericAliasNames.Add(n);
    foreach (var (k, v) in other.ModuleVisibleConstants) ModuleVisibleConstants.TryAdd(k, v);
    foreach (var (k, v) in other.ModuleConstantSourceFiles) ModuleConstantSourceFiles.TryAdd(k, v);
    foreach (var (k, v) in other.ConstantArrayLiterals) ConstantArrayLiterals.TryAdd(k, v);
    foreach (var (k, v) in other.ConstantEmptyContainerFactories) ConstantEmptyContainerFactories.TryAdd(k, v);
    foreach (var (k, v) in other.InterfaceAssociatedTypes) InterfaceAssociatedTypes.TryAdd(k, v);
    foreach (var init in other.DeferredGlobalInits) {
      if (!DeferredGlobalInits.Any(d => d.Name == init.Name))
        DeferredGlobalInits.Add(init);
    }
    // Keyed by name AND declaring file: two files may each declare a file-private constant of the
    // same name, and they are different declarations that must both survive the merge.
    foreach (var decl in other.TopLevelConstantDecls) {
      if (!TopLevelConstantDecls.Any(d => d.Name == decl.Name && d.SourceFilePath == decl.SourceFilePath))
        TopLevelConstantDecls.Add(decl);
    }
  }
}
