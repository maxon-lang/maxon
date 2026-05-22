namespace MaxonSharp.Compiler;

/// <summary>
/// Central source-file collection for both the CLI (<c>maxon build</c>) and
/// the LSP server. Both callers must feed <see cref="Compiler.CompileSources"/>
/// the same set of files in the same order — the compiler's pre-scan passes
/// have ordering dependencies, so divergence here produces spurious diagnostics
/// in one client but not the other.
/// </summary>
public static class SourceCollector {
  /// <summary>
  /// Walk <paramref name="directory"/> and collect every .maxon file suitable
  /// as a compiler source. Skips <c>build.maxon</c> (project metadata, not
  /// source) and anything listed in a <c>.maxonignore</c>. Each file's content
  /// is truncated at a <c>---</c> separator line (used by spec tests and the
  /// like to pin test fixtures after the source).
  ///
  /// <paramref name="editorOverrides"/> supplies in-memory buffers keyed by
  /// the file's <em>normalized</em> path (see <see cref="NormalizePath"/>).
  /// When an override matches a discovered file, its content replaces the
  /// disk content — the LSP uses this so an edited-but-unsaved buffer is
  /// what gets compiled.
  /// </summary>
  public static SourceFile[] FromDirectory(
    string directory,
    IReadOnlyDictionary<string, string>? editorOverrides = null
  ) {
    // The walked directory IS the project compile root: every file's namespace
    // is derived as rel(file, directory).parent under the directory-as-module
    // design (Phase 1 plumbing).
    var rootPath = directory;
    var files = new List<SourceFile>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in Directory.GetFiles(directory, "*.maxon", SearchOption.AllDirectories)) {
      if (Path.GetFileName(file).Equals("build.maxon", StringComparison.OrdinalIgnoreCase))
        continue;
      if (MaxonIgnore.IsIgnored(file))
        continue;
      var normalized = NormalizePath(file);
      seen.Add(normalized);
      var content = editorOverrides != null && editorOverrides.TryGetValue(normalized, out var buf)
        ? ReadUpToSeparator(buf)
        : ReadUpToSeparator(File.ReadAllText(file));
      files.Add(new SourceFile(normalized, content, rootPath));
    }
    // Editor-only files (newly created, not yet on disk) still need to be
    // compiled so the LSP can show diagnostics for them.
    if (editorOverrides != null) {
      foreach (var (path, content) in editorOverrides) {
        if (seen.Add(path))
          files.Add(new SourceFile(path, ReadUpToSeparator(content), rootPath));
      }
    }
    return [.. files];
  }

  /// <summary>
  /// Truncate a file's text at the first <c>---</c> separator line. Lines
  /// after the separator are treated as trailing fixture data, not source.
  /// </summary>
  public static string ReadUpToSeparator(string content) {
    var lines = content.Split('\n');
    var sourceLines = new List<string>();
    foreach (var line in lines) {
      if (line.Trim() == "---") break;
      sourceLines.Add(line);
    }
    return string.Join('\n', sourceLines);
  }

  /// <summary>
  /// Normalize a filesystem path to the form used as a source-file key: an
  /// absolute path with forward-slash separators. Matching editor-override
  /// keys against discovered files relies on both sides using this form.
  /// </summary>
  public static string NormalizePath(string path) =>
    Path.GetFullPath(path).Replace('\\', '/');
}
