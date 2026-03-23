using System.Globalization;
using MaxonSharp.Compiler;

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

public class EnumAttr(string enumTypeName, string caseName) : MlirAttribute {
  public string EnumTypeName { get; } = enumTypeName;
  public string CaseName { get; } = caseName;
  public override string ToString() => $"{EnumTypeName}.{CaseName}";
}

/// Stores the tokens for a default value expression, re-parsed at each call site.
/// This allows any literal expression to be used as a default value without
/// needing a separate attribute type for each literal kind.
public class TokenRangeAttr(List<Token> tokens) : MlirAttribute {
  public List<Token> Tokens { get; } = tokens;
  public override string ToString() => string.Join(" ", Tokens.Select(t => t.Value));
}
