namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirFunction<TOp>(string name, List<string> paramNames, List<MlirType> paramTypes, MlirType? returnType, MlirType? throwsType = null) where TOp : IPrintableOp {
  public string Name { get; internal set; } = name;
  public List<string> ParamNames { get; } = paramNames;
  public List<MlirType> ParamTypes { get; } = paramTypes;
  public MlirType? ReturnType { get; set; } = returnType;
  public MlirType? ThrowsType { get; } = throwsType;
  public MlirRegion<TOp> Body { get; } = new();
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

  // Parameters that are directly reassigned (need pass-by-reference ABI).
  // Set by MaxonToStandardConversion before lowering.
  public HashSet<string>? ReassignedParams { get; set; }

  // Parameters whose reachable data is mutated (direct assignment, field mutation,
  // or builtin ops on self-derived fields). Used for E3063 immutability enforcement.
  // Superset of ReassignedParams. Set by MaxonToStandardConversion before lowering.
  public HashSet<string>? MutatedParams { get; set; }

  /// Create an independent deep copy of this function.
  public MlirFunction<TOp> DeepClone() {
    var clone = new MlirFunction<TOp>(Name, [.. ParamNames], [.. ParamTypes], ReturnType, ThrowsType) {
      IsStdlib = IsStdlib,
      IsExported = IsExported,
      SourceFilePath = SourceFilePath,
      SourceLine = SourceLine,
      SourceColumn = SourceColumn,
      ExtensionWhereConstraints = ExtensionWhereConstraints,
      IsPure = IsPure,
      ReturnsSelf = ReturnsSelf,
      ReassignedParams = ReassignedParams != null ? new HashSet<string>(ReassignedParams) : null,
      MutatedParams = MutatedParams != null ? new HashSet<string>(MutatedParams) : null
    };
    foreach (var block in Body.Blocks) {
      var clonedBlock = new MlirBlock<TOp>(block.Name);
      clonedBlock.Operations.AddRange(block.Operations);
      clone.Body.Blocks.Add(clonedBlock);
    }
    return clone;
  }
}
