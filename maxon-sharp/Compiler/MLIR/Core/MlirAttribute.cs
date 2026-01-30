namespace MaxonSharp.Compiler.Mlir.Core;

public abstract class MlirAttribute;

public class IntegerAttr(long value, MlirType type) : MlirAttribute {
	public long Value { get; } = value;
	public MlirType Type { get; } = type;
	public override string ToString() => $"{Value} : {Type}";
}
