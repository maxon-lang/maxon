namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirFunction<TOp>(string name, List<string> paramNames, List<MlirType> paramTypes, MlirType? returnType, MlirType? throwsType = null) where TOp : IPrintableOp {
  public string Name { get; } = name;
  public List<string> ParamNames { get; } = paramNames;
  public List<MlirType> ParamTypes { get; } = paramTypes;
  public MlirType? ReturnType { get; } = returnType;
  public MlirType? ThrowsType { get; } = throwsType;
  public MlirRegion<TOp> Body { get; } = new();
  public bool IsStdlib { get; set; }
  public int? SourceLine { get; set; }
  public int? SourceColumn { get; set; }
}
