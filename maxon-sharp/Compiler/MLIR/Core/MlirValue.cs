namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// An SSA value in MLIR. Each value has a unique ID and a type.
/// Values are produced by operations or block arguments.
/// </summary>
public sealed class MlirValue {
	[ThreadStatic]
	private static int _nextId;

	/// <summary>
	/// Unique identifier for this value within the function.
	/// </summary>
	public int Id { get; }

	/// <summary>
	/// The type of this value.
	/// </summary>
	public MlirType Type { get; }

	/// <summary>
	/// The operation that defines this value, or null if it's a block argument.
	/// </summary>
	public MlirOperation? DefiningOp { get; internal set; }

	/// <summary>
	/// The block argument that defines this value, or null if it's an operation result.
	/// </summary>
	public MlirBlockArgument? DefiningBlockArg { get; internal set; }

	/// <summary>
	/// Optional name for debugging/printing.
	/// </summary>
	public string? Name { get; set; }

	public MlirValue(MlirType type) {
		Id = _nextId++;
		Type = type;
	}

	public MlirValue(int id, MlirType type) {
		Id = id;
		Type = type;
	}

	/// <summary>
	/// Resets the global ID counter. Used for testing.
	/// </summary>
	public static void ResetIdCounter() => _nextId = 0;

	public override string ToString() => Name is not null ? $"%{Name}" : $"%{Id}";

	public override bool Equals(object? obj) => obj is MlirValue other && Id == other.Id;
	public override int GetHashCode() => Id;
}

/// <summary>
/// A block argument - a value defined at a block entry point (replaces PHI nodes).
/// </summary>
public sealed class MlirBlockArgument {
	/// <summary>
	/// The value this block argument produces.
	/// </summary>
	public MlirValue Value { get; }

	/// <summary>
	/// The block this argument belongs to.
	/// </summary>
	public MlirBlock Block { get; }

	/// <summary>
	/// Index of this argument in the block's argument list.
	/// </summary>
	public int Index { get; }

	/// <summary>
	/// The type of this argument.
	/// </summary>
	public MlirType Type => Value.Type;

	public MlirBlockArgument(MlirBlock block, int index, MlirType type) {
		Block = block;
		Index = index;
		Value = new MlirValue(type) { DefiningBlockArg = this };
	}

	public override string ToString() => $"{Block.Name}[{Index}]:{Value.Type}";
}
