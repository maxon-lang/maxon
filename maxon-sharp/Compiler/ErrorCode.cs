namespace MaxonSharp.Compiler;

/// <summary>
/// Extension methods for <see cref="ErrorCode"/>.
///
/// The enum itself is GENERATED into ErrorCode.g.cs from docs/error-codes.txt, the
/// single source of truth for the 4-digit code space shared by all three compilers.
/// To add a code, edit that file and run `maxon error-codes generate`.
/// </summary>
public static class ErrorCodeExtensions {
  /// <summary>
  /// Formats an error code as "E1001", "E2001", etc.
  /// </summary>
  public static string Format(this ErrorCode code) {
    return $"E{(int)code:D4}";
  }
}
