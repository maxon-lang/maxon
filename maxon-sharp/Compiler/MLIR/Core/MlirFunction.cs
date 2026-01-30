namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirFunction<TOp>(string name, List<string> paramNames, List<MlirType> paramTypes, MlirType? returnType) where TOp : IPrintableOp {
	public string Name { get; } = name;
	public List<string> ParamNames { get; } = paramNames;
	public List<MlirType> ParamTypes { get; } = paramTypes;
	public MlirType? ReturnType { get; } = returnType;
	public MlirRegion<TOp> Body { get; } = new();
}
