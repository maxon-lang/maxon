namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// Base class for compile-time constant attributes attached to operations.
/// </summary>
public abstract record MlirAttribute {
	/// <summary>
	/// The dialect that defines this attribute, or null for builtin.
	/// </summary>
	public abstract string? Dialect { get; }

	/// <summary>
	/// Human-readable name for printing.
	/// </summary>
	public abstract string Mnemonic { get; }
}

/// <summary>
/// Integer constant attribute.
/// </summary>
public record IntegerAttr(long Value, int BitWidth = 64) : MlirAttribute {
	public override string? Dialect => null;
	public override string Mnemonic => "integer";
	public override string ToString() => $"{Value} : i{BitWidth}";
}

/// <summary>
/// Floating-point constant attribute.
/// </summary>
public record FloatAttr(double Value, int BitWidth = 64) : MlirAttribute {
	public override string? Dialect => null;
	public override string Mnemonic => "float";
	public override string ToString() => $"{Value} : f{BitWidth}";
}

/// <summary>
/// Boolean constant attribute.
/// </summary>
public record BoolAttr(bool Value) : MlirAttribute {
	public override string? Dialect => null;
	public override string Mnemonic => "bool";
	public override string ToString() => Value ? "true" : "false";
}

/// <summary>
/// String constant attribute.
/// </summary>
public record StringAttr(string Value) : MlirAttribute {
	public override string? Dialect => null;
	public override string Mnemonic => "string";
	public override string ToString() => $"\"{Value.Replace("\"", "\\\"")}\"";
}

/// <summary>
/// Symbol reference attribute (references a named symbol like a function).
/// </summary>
public record SymbolRefAttr(string Symbol) : MlirAttribute {
	public override string? Dialect => null;
	public override string Mnemonic => "symbol_ref";
	public override string ToString() => $"@{Symbol}";
}

/// <summary>
/// Type attribute (wraps a type as an attribute).
/// </summary>
public record TypeAttr(MlirType Type) : MlirAttribute {
	public override string? Dialect => null;
	public override string Mnemonic => "type";
	public override string ToString() => Type.ToString();
}

/// <summary>
/// Array attribute containing other attributes.
/// </summary>
public record ArrayAttr(IReadOnlyList<MlirAttribute> Elements) : MlirAttribute {
	public override string? Dialect => null;
	public override string Mnemonic => "array";
	public override string ToString() => $"[{string.Join(", ", Elements)}]";
}

/// <summary>
/// Dictionary attribute mapping string keys to attributes.
/// </summary>
public record DictAttr(IReadOnlyDictionary<string, MlirAttribute> Elements) : MlirAttribute {
	public override string? Dialect => null;
	public override string Mnemonic => "dict";
	public override string ToString() => $"{{{string.Join(", ", Elements.Select(kv => $"{kv.Key} = {kv.Value}"))}}}";
}

/// <summary>
/// Unit attribute (represents presence, like a flag).
/// </summary>
public record UnitAttr : MlirAttribute {
	public static readonly UnitAttr Instance = new();
	public override string? Dialect => null;
	public override string Mnemonic => "unit";
	public override string ToString() => "unit";
	private UnitAttr() { }
}
