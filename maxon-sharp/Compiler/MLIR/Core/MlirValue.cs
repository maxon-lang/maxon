namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirValue(int id, MlirType type) {
	public int Id { get; } = id;
	public MlirType Type { get; } = type;
	public MlirOperation? DefiningOp { get; set; }
	public override string ToString() => $"%{Id}";
}
