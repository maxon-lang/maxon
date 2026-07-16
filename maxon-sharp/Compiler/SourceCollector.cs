namespace MaxonSharp.Compiler;

/// <summary>
/// Central source-file collection for both the CLI (<c>maxon build</c>) and
/// the LSP server, so that both feed <see cref="Compiler.CompileSources"/> the
/// same set of files.
///
/// Resolution of top-level CONSTANTS does not depend on this order: every file's
/// constant declarations are collected before any of them is folded (see
/// Parser.PreScanTopLevelConstantDecls). Other pre-scan passes still carry
/// ordering dependencies — a duplicate type name resolves first-file-wins, and
/// auto-conformance synthesis only sees the files pre-scanned before it — so a
/// divergence in this order can still produce a diagnostic in one client and not
/// the other, until those are order-independent too.
///
/// <see cref="SourceOrderEnvVar"/> is the seam that proves the claim rather than
/// asserting it: a real project built forwards and backwards must produce
/// byte-identical output and diagnostics.
/// </summary>
public static class SourceCollector {
  /// <summary>
  /// Test seam: set to <c>reverse</c> to hand the compiler every discovered file in the exact
  /// opposite order. Order-independence is a property of the RESOLVER, and the only way to keep
  /// it true is to be able to run the real corpus both ways and diff — a suite that only ever
  /// runs in one order proves nothing, which is how a filesystem-order bug survived here for
  /// months (sorted on NTFS, hash-ordered on APFS: the tree that built on Windows was refused on
  /// macOS). An environment variable rather than a CLI flag so it reaches subprocess builds too.
  /// </summary>
  public const string SourceOrderEnvVar = "MAXON_SOURCE_ORDER";
  private const string ReverseOrder = "reverse";

  /// <summary>
  /// Apply <see cref="SourceOrderEnvVar"/>. An unrecognized value throws rather than falling
  /// back to natural order: a typo'd seam that silently measures nothing is worse than no seam.
  /// </summary>
  private static List<SourceFile> ApplyRequestedOrder(List<SourceFile> files) {
    var requested = Environment.GetEnvironmentVariable(SourceOrderEnvVar);
    if (string.IsNullOrEmpty(requested)) return files;
    if (requested == ReverseOrder) {
      files.Reverse();
      return files;
    }
    throw new ArgumentException(
      $"{SourceOrderEnvVar}='{requested}' is not a known source order; the only supported value is '{ReverseOrder}'");
  }
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
    return [.. ApplyRequestedOrder(files)];
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
