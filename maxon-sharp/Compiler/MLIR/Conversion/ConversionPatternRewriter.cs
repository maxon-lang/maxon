using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Provides utilities for rewriting operations during dialect conversion.
/// </summary>
public sealed class ConversionPatternRewriter(MlirBlock block, int insertPoint) {
	private readonly MlirBlock _block = block;
	private int _insertPoint = insertPoint;
	private readonly Dictionary<MlirValue, MlirValue> _valueMap = [];

	/// <summary>
	/// Gets or sets the mapped value for an original value.
	/// </summary>
	public MlirValue GetRemapped(MlirValue original) =>
			_valueMap.TryGetValue(original, out var mapped) ? mapped : original;

	/// <summary>
	/// Maps an original value to a replacement value.
	/// </summary>
	public void Map(MlirValue original, MlirValue replacement) =>
			_valueMap[original] = replacement;

	/// <summary>
	/// Inserts a new operation at the current insert point.
	/// </summary>
	public T Insert<T>(T op) where T : MlirOperation {
		_block.InsertOperation(_insertPoint++, op);
		return op;
	}

	/// <summary>
	/// Replaces the results of an old operation with the results of a new operation.
	/// </summary>
	public void ReplaceOp(MlirOperation oldOp, MlirOperation newOp) {
		for (int i = 0; i < oldOp.Results.Count && i < newOp.Results.Count; i++) {
			Map(oldOp.Results[i], newOp.Results[i]);
		}
	}

	/// <summary>
	/// Replaces a single-result operation with a value.
	/// </summary>
	public void ReplaceOpWithValue(MlirOperation oldOp, MlirValue newValue) {
		if (oldOp.Results.Count == 1) {
			Map(oldOp.Results[0], newValue);
		}
	}

	/// <summary>
	/// Creates a new value of the given type (for use as an intermediate).
	/// </summary>
	public static MlirValue CreateValue(MlirType type) => new(type);

	/// <summary>
	/// The current insertion block.
	/// </summary>
	public MlirBlock Block => _block;

	/// <summary>
	/// Gets all value mappings for updating uses.
	/// </summary>
	public IReadOnlyDictionary<MlirValue, MlirValue> ValueMappings => _valueMap;
}
