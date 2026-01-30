namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirFunction<TOp>(string name, List<MlirType> paramTypes, MlirType? returnType) where TOp : IMlirOp {
	public string Name { get; } = name;
	public List<MlirType> ParamTypes { get; } = paramTypes;
	public MlirType? ReturnType { get; } = returnType;
	public MlirRegion<TOp> Body { get; } = new();
}
