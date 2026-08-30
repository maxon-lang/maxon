using System.Globalization;
using MaxonSharp.Compiler;

namespace MaxonSharp.Compiler.Ir.Core;

public abstract class IrAttribute;

public class IntegerAttr(long value, IrType type, bool writtenNegative = false) : IrAttribute {
  public long Value { get; } = value;
  public IrType Type { get; } = type;

  /// ⭐⭐ Did the SOURCE write this number with a leading minus? — the one fact about the payload
  /// that the payload cannot carry. `= -1` and `= 0xFFFFFFFFFFFFFFFE` both store a negative `long`,
  /// and only the first is a negative NUMBER, which is what decides whether the default fits an
  /// `int(0 to u64.max)` field. It travels from the declaration (which may be in another file) to
  /// the site that materializes the default as a literal, where it is re-stamped onto the minted
  /// value for `IntegerOutOfRange` to spend. False for every non-literal use of this attribute.
  public bool WrittenNegative { get; } = writtenNegative;

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
