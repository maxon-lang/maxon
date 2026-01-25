namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// MLIR context - holds type uniquing and global state.
/// </summary>
public sealed class MlirContext {
	private readonly Dictionary<string, MlirType> _typeCache = [];

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

	/// <summary>
	/// Resets all global state (ID counters, type cache).
	/// </summary>
	public void Reset() {
		_typeCache.Clear();
		MlirValue.ResetIdCounter();
		MlirBlock.ResetIdCounter();
	}
}
