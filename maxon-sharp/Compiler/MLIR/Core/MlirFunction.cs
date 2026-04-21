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

  /// Create an independent deep copy of this function.
  public IrFunction<TOp> DeepClone() {
    var clone = new IrFunction<TOp>(Name, [.. ParamNames], [.. ParamTypes], ReturnType, ThrowsType) {
      IsStdlib = IsStdlib,
      IsExported = IsExported,
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
    return clone;
  }
}
