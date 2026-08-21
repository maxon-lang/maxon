namespace MaxonSharp.Compiler.Ir.Core;

public class IrFunction<TOp>(string name, List<string> paramNames, List<IrType> paramTypes, IrType? returnType, IrType? throwsType = null) where TOp : IPrintableOp {
  public string Name { get; internal set; } = name;

  // The PROSE name a `test` declaration was written with, verbatim, and null for every other
  // function. A test carries two names because its written name cannot be its symbol: it flows
  // into name mangling, the PE/Mach-O symbol table, the .mxdbg sidecar and panic stack traces,
  // none of which accept spaces or punctuation. `Name` is therefore the sanitized
  // `<namespace>.__test_<sanitized>`, and this is what a report shows a human.
  //
  // Non-null also MARKS the function as a test entry point — nothing in the program calls a
  // test, so dead-function elimination roots it (see DeadFunctionElimination.WalkReachableAndPrune).
  // That makes this field the single fact "is a test", which is why nothing else may set it.
  public string? DisplayName { get; set; }

  public List<string> ParamNames { get; } = paramNames;
  public List<IrType> ParamTypes { get; } = paramTypes;
  public IrType? ReturnType { get; set; } = returnType;
  public IrType? ThrowsType { get; } = throwsType;
  public IrRegion<TOp> Body { get; } = new();
  public bool IsStdlib { get; set; }
  public bool IsExported { get; set; }
  // True for `module function`: visible to other files in the same directory subtree.
  // Mutually exclusive with IsExported (enforced at the parser).
  public bool IsModuleVisible { get; set; }
  public string? SourceFilePath { get; set; }
  public int? SourceLine { get; set; }
  public int? SourceColumn { get; set; }
  // Where constraints from conditional extensions (param name -> required interface names)
  // When set, monomorphization should skip cloning this method for concrete types
  // whose associated type bindings don't satisfy these constraints.
  public Dictionary<string, List<string>>? ExtensionWhereConstraints { get; set; }
  // Purity: true if the function has no side effects (set by PurityAnalysisPass)
  public bool IsPure { get; set; } = true;
  // True when the function returns `self` (borrowed reference, not a new allocation)
  public bool ReturnsSelf { get; set; }
  // True when the function is a static method (no implicit self parameter)
  public bool IsStatic { get; set; }
  // True for synthetic metadata-only functions registered for builtin __Managed* methods.
  // These have no body and are never called via MaxonCallOp — they exist for type validation and LSP.
  public bool IsBuiltinSynthetic { get; set; }

  // True for the top-level function a `function(...) gives ...` closure body was LIFTED into.
  //
  // It is emitted ONCE, and that is the whole content of the flag: monomorphization clones a
  // generic's methods per instance, and a lifted closure is not one of them — it is a sibling at
  // module scope that no instantiation reaches. So anything inside it that would have to be
  // resolved against the enclosing method's instance simply cannot be, and `countof(Self)` is
  // refused there (E2072) rather than reaching a later pass with an unsubstituted operand.
  public bool IsLiftedClosure { get; set; }

  // Parameters that are directly reassigned (need pass-by-reference ABI).
  // Set by MaxonToStandardConversion before lowering.
  public HashSet<string>? ReassignedParams { get; set; }

  // Parameters whose reachable data is mutated (direct assignment, field mutation,
  // or builtin ops on self-derived fields). Used for E3063 immutability enforcement.
  // Superset of ReassignedParams. Set by MaxonToStandardConversion before lowering.
  public HashSet<string>? MutatedParams { get; set; }

  // Parameter indices that the function mutates (assignment, field mutation, or
  // mutating method calls). Used by BorrowCheckPass for borrow/mutation conflict detection.
  public HashSet<int>? MutatedParamIndices { get; set; }

  // Parameters that escape the function (aliased, stored to heap/global/closure,
  // or passed to a callee that escapes them). Used by StackPromotionAnalysisPass.
  public HashSet<string>? EscapingParams { get; set; }

  // Parameter indices that are borrow-only — the callee does not extend the
  // parameter reference's lifetime past the call. A parameter is borrow-only
  // when no tainted value (derived from the param's SSA via local stores and
  // loads) reaches an mm_incref, an indirect store into heap memory, a return
  // op, or a retaining parameter position on another call. Indirect calls
  // (closure invocations) are conservatively treated as retaining all args.
  //
  // Set by ParameterRetentionAnalysisPass on the Standard dialect IR and
  // consumed by RefcountOptimizationPass to skip borrow-only direct calls
  // when scanning an incref/decref window for aliasing events.
  public HashSet<int>? BorrowOnlyParamIndices { get; set; }

  // Debug-info side-table: op -> source position (see docs/DEBUGGER_DESIGN.md, SourceSpan).
  //
  // KEYED BY OP REFERENCE. Every op is a non-record class (MaxonOp/StandardOp/X86Op/ARM64Op are
  // abstract classes, never records), so the default dictionary comparer is reference identity —
  // exactly what we want, since two structurally-equal ops are still distinct instructions.
  //
  // Populated ONLY under --debug-info, and never read by codegen: the field is null (allocating
  // nothing) in a release build, and even when present it changes not one emitted byte. That is the
  // "pure observer" contract — it is why the sidecar can be produced without perturbing .text.
  private Dictionary<TOp, SourceSpan>? _debugSpans;

  public void SetDebugSpan(TOp op, SourceSpan span) => (_debugSpans ??= [])[op] = span;

  public bool TryGetDebugSpan(TOp op, out SourceSpan span) {
    if (_debugSpans != null) return _debugSpans.TryGetValue(op, out span);
    span = default;
    return false;
  }

  // Debug-info side-tables for local-variable location lists (see docs/DEBUGGER_DESIGN.md, P2b).
  //
  // A local's SOURCE-level type is erased by the Standard dialect (every store carries only
  // i64/f64/ptr), so it must be captured in MaxonToStandardConversion — the last pass that still
  // knows a `let p = Point(...)` names `Point` — and carried forward, keyed by the variable NAME
  // (stable across lowering, unlike an op reference). LocalSlotOffsets is the complementary
  // name -> rbp/x29-relative stack-slot offset the machine conversion assigns. The two are joined at
  // emit into the sidecar's local records.
  //
  // Both are populated ONLY under --debug-info and never read by codegen: null (allocating nothing) in
  // a release build, and even when present they change not one emitted byte — the "pure observer"
  // contract that lets the sidecar be produced without perturbing .text.
  private Dictionary<string, string>? _localSourceTypes;
  private Dictionary<string, int>? _localSlotOffsets;

  public IReadOnlyDictionary<string, string>? LocalSourceTypes => _localSourceTypes;
  public IReadOnlyDictionary<string, int>? LocalSlotOffsets => _localSlotOffsets;

  /// Record the name -> source-type table (MaxonToStandardConversion, where the source type is still
  /// known). The machine conversion later carries it forward via <see cref="SetLocalDebugInfo"/>.
  public void SetLocalSourceTypes(Dictionary<string, string> types) => _localSourceTypes = types;

  /// Attach BOTH local side-tables to this machine-dialect function: the slot offsets the target
  /// conversion just computed, and the source-type table carried (by variable name, stable across
  /// lowering) from the <paramref name="source"/> function it lowers. One call so the x64 and arm64
  /// conversions cannot attach one table and forget the other — the same cross-target-divergence guard
  /// as DebugInfoBuilder.FrameSizeFromPrologue. Source types are copied, not aliased, since neither map
  /// is mutated afterward — mirroring how <see cref="CopySourceAnchorFrom"/> carries the file anchor.
  public void SetLocalDebugInfo<TOther>(Dictionary<string, int> slotOffsets, IrFunction<TOther> source)
      where TOther : IPrintableOp {
    _localSlotOffsets = slotOffsets;
    if (source.LocalSourceTypes is { } types) _localSourceTypes = new Dictionary<string, string>(types);
  }

  /// Carry the source anchor (file/line/column) forward from the function this one lowers. The file
  /// is a per-function fact with one home, so each lowering pass copies it once through here rather
  /// than repeating the three-field assignment in its new-function initializer.
  public void CopySourceAnchorFrom<TOther>(IrFunction<TOther> source) where TOther : IPrintableOp {
    SourceFilePath = source.SourceFilePath;
    SourceLine = source.SourceLine;
    SourceColumn = source.SourceColumn;
  }

  /// Create an independent deep copy of this function.
  /// <param name="typeCopier">
  /// Maps every type in the signature into the CLONE's own type graph. Required rather than optional
  /// because a signature that still points into the original graph is the half-copy this whole
  /// mechanism exists to prevent — see <see cref="TypeGraphCopier"/>. <c>ThrowsType</c> is get-only,
  /// so the mapping has to happen here, at construction, and cannot be patched up by the caller.
  /// </param>
  /// <param name="copyOp">
  /// Copies one op into the clone's own op graph. Also required, and for the same reason: an op
  /// carries per-compile conclusions (a call's resolved callee, a value's concrete struct type), so a
  /// clone that shared ops with its template let one compile read another's.
  /// </param>
  public IrFunction<TOp> DeepClone(TypeGraphCopier typeCopier, Func<TOp, TOp> copyOp) {
    var clone = new IrFunction<TOp>(Name, [.. ParamNames], [.. ParamTypes.Select(t => typeCopier.Copy(t)!)],
        typeCopier.Copy(ReturnType), typeCopier.Copy(ThrowsType)) {
      DisplayName = DisplayName,
      IsStdlib = IsStdlib,
      IsExported = IsExported,
      IsModuleVisible = IsModuleVisible,
      SourceFilePath = SourceFilePath,
      SourceLine = SourceLine,
      SourceColumn = SourceColumn,
      ExtensionWhereConstraints = ExtensionWhereConstraints,
      IsPure = IsPure,
      ReturnsSelf = ReturnsSelf,
      IsStatic = IsStatic,
      IsBuiltinSynthetic = IsBuiltinSynthetic,
      ReassignedParams = ReassignedParams != null ? [.. ReassignedParams] : null,
      MutatedParams = MutatedParams != null ? [.. MutatedParams] : null,
      MutatedParamIndices = MutatedParamIndices != null ? [.. MutatedParamIndices] : null,
      EscapingParams = EscapingParams != null ? [.. EscapingParams] : null,
      BorrowOnlyParamIndices = BorrowOnlyParamIndices != null ? [.. BorrowOnlyParamIndices] : null
    };
    var opCopies = new Dictionary<TOp, TOp>();
    foreach (var block in Body.Blocks) {
      var clonedBlock = new IrBlock<TOp>(block.Name);
      foreach (var op in block.Operations) {
        // One op object listed twice must stay one op object in the clone: the copy is looked up, not
        // remade, so the clone reproduces the original's op identity and not merely its op sequence.
        if (!opCopies.TryGetValue(op, out var copied)) {
          copied = copyOp(op);
          opCopies[op] = copied;
        }
        clonedBlock.Operations.Add(copied);
      }
      clone.Body.Blocks.Add(clonedBlock);
    }
    // Spans are keyed by OP, and the clone's ops are new objects — so the keys have to travel with
    // them. Keyed by the original here, looked up by the copy there.
    if (_debugSpans != null) {
      clone._debugSpans = [];
      foreach (var (op, span) in _debugSpans)
        if (opCopies.TryGetValue(op, out var copied)) clone._debugSpans[copied] = span;
    }
    return clone;
  }
}

/// <summary>
/// Encoding of the debug-info local NAME -> SOURCE type map (<see cref="IrFunction{TOp}.LocalSourceTypes"/>).
/// The map is filled while lowering (MaxonToStandardConversion) and read at emit (DebugInfoBuilder);
/// this is the ONE home for the "reused name, conflicting type" rule so the two ends cannot disagree.
///
/// A single stack slot in the bootstrap carries ONE loclist entry spanning the whole function. If a
/// variable NAME is reused across sibling scopes for DIFFERENT source types and both bindings earn a
/// slot, no single type is honest over that range — so the name is POISONED and OMITTED from the
/// sidecar rather than confidently labeled with whichever type was seen first (the design doc's
/// forbidden "instrument that lies").
/// </summary>
public static class DebugLocalTypes {
  // Marks a name recorded under two different source types. A NUL cannot appear in a real type name,
  // so this never collides with a captured type; it is never written to the sidecar (the emit-side
  // filter drops it before it reaches the type or local tables).
  private const string Conflicted = "\0conflicted";

  /// Record <paramref name="name"/> -> <paramref name="typeName"/> on first sight; a later record of
  /// the same name with a DIFFERENT type poisons it, and it stays poisoned. A same-type re-record
  /// (e.g. a loop-carried var stored each iteration) is not a conflict.
  public static void Record(Dictionary<string, string> map, string name, string typeName) {
    if (!map.TryGetValue(name, out var existing)) map[name] = typeName;
    else if (existing != typeName && existing != Conflicted) map[name] = Conflicted;
  }

  /// True when a recorded type is the conflict poison — the local must be OMITTED from the sidecar.
  public static bool IsConflicted(string typeName) => typeName == Conflicted;
}
