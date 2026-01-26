namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// Interface for operations that can be folded at compile time.
/// </summary>
public interface IFoldable {
	/// <summary>
	/// Attempts to fold this operation to a constant.
	/// Returns the folded operation, or null if folding is not possible.
	/// </summary>
	MlirOperation? TryFold();
}
