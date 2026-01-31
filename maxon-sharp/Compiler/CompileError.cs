namespace MaxonSharp.Compiler;

/// <summary>
/// A structured compile error with error code, message, and optional location.
/// </summary>
public class CompileError(ErrorCode code, string message, int? line = null, int? column = null) : Exception(message) {
  public ErrorCode Code { get; } = code;
  public int? Line { get; } = line;
  public int? Column { get; } = column;
  public string? FilePath { get; set; }

  /// <summary>
  /// The project root directory for making paths relative. If not set, uses current directory.
  /// </summary>
  public static string? ProjectRoot { get; set; }

  /// <summary>
  /// Formats the error for display.
  /// With file path: "error E4009: path/file.maxon:5:10: message"
  /// Without file path: "error E4009: message"
  /// </summary>
  public string Format() {
    if (FilePath != null && Line.HasValue) {
      // Normalize the path relative to project root
      var root = ProjectRoot ?? Environment.CurrentDirectory;
      var fullPath = Path.IsPathRooted(FilePath) ? FilePath : Path.GetFullPath(Path.Combine(root, FilePath));
      var displayPath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
      return $"error {Code.Format()}: {displayPath}:{Line}:{Column ?? 1}: {Message}";
    }
    return $"error {Code.Format()}: {Message}";
  }

  public override string ToString() => Format();
}
