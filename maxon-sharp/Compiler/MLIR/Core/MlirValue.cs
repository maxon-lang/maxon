namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirValue(int id, MlirType type) {
	public int Id { get; } = id;
	public MlirType Type { get; } = type;
	public IMlirOp? DefiningOp { get; set; }
	public override string ToString() => $"%{Id}";
}
