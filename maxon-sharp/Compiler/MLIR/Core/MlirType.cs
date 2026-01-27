namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// Base class for all MLIR types. Types are uniqued within a context.
/// </summary>
public abstract record MlirType {
	/// <summary>
	/// The dialect that defines this type. Null for builtin types.
	/// </summary>
	public abstract string? Dialect { get; }

	/// <summary>
	/// Human-readable name for printing.
	/// </summary>
	public abstract string Mnemonic { get; }

	/// <summary>
	/// Size in bytes when stored in memory. -1 for unsized types.
	/// </summary>
	public abstract int SizeInBytes { get; }

	/// <summary>
	/// Alignment requirement in bytes.
	/// </summary>
	public virtual int Alignment => SizeInBytes > 0 ? Math.Min(SizeInBytes, 8) : 1;

	/// <summary>
	/// Returns true if this type has a known size.
	/// </summary>
	public bool IsSized => SizeInBytes >= 0;

	/// <summary>
	/// Returns true if this type has Copy semantics (passing doesn't move ownership).
	/// Primitive types (integers, floats, bools) are Copy types.
	/// </summary>
	public virtual bool IsCopyType => false;

	/// <summary>
	/// MLIR-style textual representation.
	/// </summary>
	public override string ToString() => Dialect is null ? Mnemonic : $"!{Dialect}.{Mnemonic}";
}

/// <summary>
/// Marker interface for types that can be used as function return types.
/// </summary>
public interface IFunctionResultType { }

/// <summary>
/// Marker interface for types that can be used in memory operations.
/// </summary>
public interface IMemRefElementType { }
