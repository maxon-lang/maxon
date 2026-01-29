namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// MLIR context - holds type uniquing and ID generation state.
/// Use <see cref="PushScope"/> to establish an ambient context for operations.
/// </summary>
public sealed class MlirContext {
	private static readonly AsyncLocal<MlirContext?> _current = new();

	/// <summary>
	/// Gets the current ambient context. Throws if no context is active.
	/// </summary>
	public static MlirContext Current =>
		_current.Value ?? throw new InvalidOperationException(
			"No MlirContext is active. Wrap compilation in 'using (context.PushScope()) { ... }'");

	private readonly Dictionary<string, MlirType> _typeCache = [];
	private int _nextValueId;
	private int _nextBlockId;
	private int _nextVRegId; // Starts at 0 and goes negative for temps

	/// <summary>
	/// Gets the next unique value ID.
	/// </summary>
	public int NextValueId() => _nextValueId++;

	/// <summary>
	/// Gets the next unique block ID.
	/// </summary>
	public int NextBlockId() => _nextBlockId++;

	/// <summary>
	/// Gets the next unique temporary virtual register ID (negative to avoid conflicts).
	/// </summary>
	public int NextVRegId() => --_nextVRegId;

	/// <summary>
	/// Resets ID counters to zero. Used by RenumberValues.
	/// </summary>
	public void ResetIdCounters() {
		_nextValueId = 0;
		_nextBlockId = 0;
	}

	/// <summary>
	/// Pushes this context as the ambient context. Dispose the result to restore the previous context.
	/// </summary>
	public IDisposable PushScope() {
		var previous = _current.Value;
		_current.Value = this;
		return new ContextScope(previous);
	}

	private sealed class ContextScope(MlirContext? previous) : IDisposable {
		public void Dispose() => _current.Value = previous;
	}

	/// <summary>
	/// Gets or creates a uniqued type.
	/// </summary>
	public T GetType<T>(string key, Func<T> factory) where T : MlirType {
		if (_typeCache.TryGetValue(key, out var existing))
			return (T)existing;
		var type = factory();
		_typeCache[key] = type;
		return type;
	}

	/// <summary>
	/// Clears type caching (useful for testing).
	/// </summary>
	public void ClearTypeCache() => _typeCache.Clear();
}
