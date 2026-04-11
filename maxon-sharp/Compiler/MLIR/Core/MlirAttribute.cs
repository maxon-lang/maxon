using System.Globalization;
using MaxonSharp.Compiler;

namespace MaxonSharp.Compiler.Ir.Core;

public abstract class IrAttribute;

public class IntegerAttr(long value, IrType type) : IrAttribute {
  public long Value { get; } = value;
  public IrType Type { get; } = type;
  public override string ToString() => $"{Value} : {Type}";
}

public class FloatAttr(double value, IrType type) : IrAttribute {
  public double Value { get; } = value;
  public IrType Type { get; } = type;
  public override string ToString() => $"{Value.ToString(CultureInfo.InvariantCulture)} : {Type}";
}

public class TypeAttr(IrType type) : IrAttribute {
  public IrType Type { get; } = type;
  public override string ToString() => Type.ToString();
}

public class StringAttr(string value) : IrAttribute {
  public string Value { get; } = value;
  public override string ToString() => Value;
}

public class EnumAttr(string enumTypeName, string caseName) : IrAttribute {
  public string EnumTypeName { get; } = enumTypeName;
  public string CaseName { get; } = caseName;
  public override string ToString() => $"{EnumTypeName}.{CaseName}";
}

/// Stores the tokens for a default value expression, re-parsed at each call site.
/// This allows any literal expression to be used as a default value without
/// needing a separate attribute type for each literal kind.
public class TokenRangeAttr(List<Token> tokens) : IrAttribute {
  public List<Token> Tokens { get; } = tokens;
  public override string ToString() => string.Join(" ", Tokens.Select(t => t.Value));
}
