using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

/// <summary>
/// Interface for MLIR dialects. Each dialect defines a set of types, operations, and attributes.
/// </summary>
public interface IDialect {
	/// <summary>
	/// The dialect namespace (e.g., "arith", "func", "maxon").
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Types defined by this dialect.
	/// </summary>
	IEnumerable<Type> Types { get; }

	/// <summary>
	/// Operations defined by this dialect.
	/// </summary>
	IEnumerable<Type> Operations { get; }

	/// <summary>
	/// Attributes defined by this dialect.
	/// </summary>
	IEnumerable<Type> Attributes { get; }

	/// <summary>
	/// Initialize dialect-specific state.
	/// </summary>
	void Initialize(MlirContext context) { }
}

/// <summary>
/// Base class for dialect implementations.
/// </summary>
public abstract class DialectBase : IDialect {
	public abstract string Name { get; }

	public virtual IEnumerable<Type> Types => [];
	public virtual IEnumerable<Type> Operations => [];
	public virtual IEnumerable<Type> Attributes => [];

	public virtual void Initialize(MlirContext context) { }
}
