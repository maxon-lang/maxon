using System.Globalization;

namespace MaxonSharp.Compiler.Mlir.Core;

public abstract class MlirAttribute;

public class IntegerAttr(long value, MlirType type) : MlirAttribute {
	public long Value { get; } = value;
	public MlirType Type { get; } = type;
	public override string ToString() => $"{Value} : {Type}";
}

public class FloatAttr(double value, MlirType type) : MlirAttribute {
	public double Value { get; } = value;
	public MlirType Type { get; } = type;
	public override string ToString() => $"{Value.ToString(CultureInfo.InvariantCulture)} : {Type}";
}

public class TypeAttr(MlirType type) : MlirAttribute {
	public MlirType Type { get; } = type;
	public override string ToString() => Type.ToString();
}

public class StringAttr(string value) : MlirAttribute {
	public string Value { get; } = value;
	public override string ToString() => Value;
}
