namespace MaxonSharp.Debug;

/// <summary>
/// How a driver-side report names a file on disk.
///
/// One rule, because three subsystems print paths into transcripts that are DIFFED against goldens —
/// the coverage report, the profile report, and the launcher's own refusals — and a second copy would
/// not fail to compile, it would make one of them machine-specific and red on every other checkout.
/// </summary>
public static class ReportPath {

  /// <summary>
  /// Relative to the working directory when the file is under it, so a transcript is the same on every
  /// machine, and absolute when it is not — which is honest, because a file outside the tree has no
  /// shorter true name. Separators are normalized so a Windows run and a POSIX run agree.
  /// </summary>
  public static string Display(string path) {
    try {
      var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
      return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
        ? path.Replace('\\', '/')
        : relative.Replace('\\', '/');
    } catch (ArgumentException) {
      return path.Replace('\\', '/');
    }
  }
}
