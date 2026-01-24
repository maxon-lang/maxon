using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

/// <summary>
/// Registry for MLIR dialects. Manages dialect loading and lookup.
/// </summary>
public sealed class DialectRegistry {
	private readonly Dictionary<string, IDialect> _dialects = [];

	/// <summary>
	/// Registers a dialect.
	/// </summary>
	public void Register(IDialect dialect) {
		_dialects[dialect.Name] = dialect;
	}

	/// <summary>
	/// Gets a dialect by name.
	/// </summary>
	public IDialect? Get(string name) =>
		_dialects.TryGetValue(name, out var dialect) ? dialect : null;

	/// <summary>
	/// Gets a dialect by name, throwing if not found.
	/// </summary>
	public IDialect GetRequired(string name) =>
		Get(name) ?? throw new KeyNotFoundException($"Dialect '{name}' not registered");

	/// <summary>
	/// Gets all registered dialects.
	/// </summary>
	public IEnumerable<IDialect> All => _dialects.Values;

	/// <summary>
	/// Returns true if a dialect is registered.
	/// </summary>
	public bool Contains(string name) => _dialects.ContainsKey(name);

	/// <summary>
	/// Initializes all dialects with the given context.
	/// </summary>
	public void InitializeAll(MlirContext context) {
		foreach (var dialect in _dialects.Values) {
			dialect.Initialize(context);
		}
	}

	/// <summary>
	/// Registers all standard dialects.
	/// </summary>
	public void RegisterStandardDialects() {
		Register(new Builtin.BuiltinDialect());
		Register(new Arith.ArithDialect());
		Register(new MemRef.MemRefDialect());
		Register(new Func.FuncDialect());
		Register(new Cf.CfDialect());
		Register(new Maxon.MaxonDialect());
		Register(new X86.X86Dialect());
	}
}
