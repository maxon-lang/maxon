namespace MaxonSharp.Compiler.Ir.Core;

public class IrFunction<TOp>(string name, List<string> paramNames, List<IrType> paramTypes, IrType? returnType, IrType? throwsType = null) where TOp : IPrintableOp {
  public string Name { get; internal set; } = name;
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
  public IrFunction<TOp> DeepClone() {
    var clone = new IrFunction<TOp>(Name, [.. ParamNames], [.. ParamTypes], ReturnType, ThrowsType) {
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
    foreach (var block in Body.Blocks) {
      var clonedBlock = new IrBlock<TOp>(block.Name);
      clonedBlock.Operations.AddRange(block.Operations);
      clone.Body.Blocks.Add(clonedBlock);
    }
    // The clone shares op references (AddRange above copies the list, not the ops), so the same
    // op->span keys apply verbatim. Copy them so a monomorphized instance keeps its source lines.
    if (_debugSpans != null) clone._debugSpans = new Dictionary<TOp, SourceSpan>(_debugSpans);
    return clone;
  }
}
