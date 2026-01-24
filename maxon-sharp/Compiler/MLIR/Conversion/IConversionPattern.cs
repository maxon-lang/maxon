using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Interface for conversion patterns that transform operations from one dialect to another.
/// </summary>
public interface IConversionPattern {
	/// <summary>
	/// The operation type this pattern matches.
	/// </summary>
	Type SourceOperationType { get; }

	/// <summary>
	/// Priority for pattern selection (higher = more preferred).
	/// </summary>
	int Benefit { get; }

	/// <summary>
	/// Attempts to match and rewrite an operation.
	/// Returns true if the pattern matched and the operation was rewritten.
	/// </summary>
	bool MatchAndRewrite(MlirOperation op, ConversionPatternRewriter rewriter);
}

/// <summary>
/// Base class for conversion patterns.
/// </summary>
public abstract class ConversionPattern<TOp> : IConversionPattern where TOp : MlirOperation {
	public Type SourceOperationType => typeof(TOp);
	public virtual int Benefit => 1;

	bool IConversionPattern.MatchAndRewrite(MlirOperation op, ConversionPatternRewriter rewriter) {
		if (op is not TOp typedOp) return false;
		return MatchAndRewrite(typedOp, rewriter);
	}

	/// <summary>
	/// Attempts to match and rewrite a typed operation.
	/// </summary>
	protected abstract bool MatchAndRewrite(TOp op, ConversionPatternRewriter rewriter);
}

/// <summary>
/// Collection of conversion patterns.
/// </summary>
public sealed class ConversionPatternSet {
	private readonly List<IConversionPattern> _patterns = [];

	/// <summary>
	/// Adds a pattern to the set.
	/// </summary>
	public void Add(IConversionPattern pattern) => _patterns.Add(pattern);

	/// <summary>
	/// Adds a pattern of the given type.
	/// </summary>
	public void Add<T>() where T : IConversionPattern, new() => _patterns.Add(new T());

	/// <summary>
	/// Gets all patterns that match the given operation type, sorted by benefit.
	/// </summary>
	public IEnumerable<IConversionPattern> GetMatching(Type opType) =>
		_patterns
			.Where(p => p.SourceOperationType.IsAssignableFrom(opType))
			.OrderByDescending(p => p.Benefit);

	/// <summary>
	/// Gets all patterns.
	/// </summary>
	public IReadOnlyList<IConversionPattern> All => _patterns;
}
