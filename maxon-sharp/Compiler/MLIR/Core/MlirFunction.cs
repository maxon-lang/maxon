namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirFunction(string name, List<MlirType> paramTypes, MlirType? returnType) {
	public string Name { get; } = name;
	public List<MlirType> ParamTypes { get; } = paramTypes;
	public MlirType? ReturnType { get; } = returnType;
	public MlirRegion Body { get; } = new();
}
