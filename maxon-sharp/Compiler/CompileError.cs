namespace MaxonSharp;

/// <summary>
/// A structured compile error with error code, message, and optional location.
/// </summary>
public class CompileError(ErrorCode code, string message, int? line = null, int? column = null) : Exception(message) {
	public ErrorCode Code { get; } = code;
	public int? Line { get; } = line;
	public int? Column { get; } = column;

	/// <summary>
	/// Formats the error for display: "error E020: Unexpected token 'foo' at 5:10"
	/// </summary>
	public string Format() {
		var location = Line.HasValue ? $" at {Line}:{Column ?? 1}" : "";
		return $"error {Code.Format()}: {Message}{location}";
	}

	public override string ToString() => Format();
}
