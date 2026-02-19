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
}
