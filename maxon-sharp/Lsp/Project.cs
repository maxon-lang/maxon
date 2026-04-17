using System.Collections.Concurrent;
using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Ir.Core;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace MaxonSharp.Lsp;

public class Project(
  string rootPath,
  bool isSingleFile,
  Action<DocumentUri, Container<Diagnostic>> publishDiagnostics
  ) {
  public string RootPath { get; } = rootPath;
  public bool IsSingleFile { get; } = isSingleFile;

  private readonly ConcurrentDictionary<string, string> _fileContents = new();
  private CompletionInfo? _lastSuccessfulCompletionInfo;
  private readonly ConcurrentDictionary<string, List<CompileError>> _diagnostics = new();

  private CancellationTokenSource? _debounceCts;
  private readonly object _debounceLock = new();
  private volatile bool _closed;

  private readonly Action<DocumentUri, Container<Diagnostic>> _publishDiagnostics = publishDiagnostics;
  private readonly bool _isStdlibProject = IsUnderStdlib(rootPath);

  public void NotifyFileChanged(string filePath, string content) {
    _fileContents[NormalizePath(filePath)] = content;
    ScheduleRecompile();
  }

  /// <summary>
  /// Called when a directory (or other non-.maxon path) is deleted. Drops any
  /// editor buffers under that path and clears diagnostics for any files the
  /// last compile had errors on. The next recompile reads the directory fresh
  /// via SourceCollector so deleted files simply disappear from the source
  /// set. Returns true if this project had any tracked state under
  /// <paramref name="dirPath"/>.
  /// </summary>
  public bool NotifyPathDeleted(string dirPath) {
    var prefix = NormalizePath(dirPath) + "/";
    var pruned = false;
    foreach (var path in _fileContents.Keys) {
      if (path.StartsWith(prefix, StringComparison.Ordinal)) {
        if (_fileContents.TryRemove(path, out _)) pruned = true;
      }
    }
    foreach (var path in _diagnostics.Keys) {
      if (path.StartsWith(prefix, StringComparison.Ordinal)) {
        if (_diagnostics.TryRemove(path, out _)) {
          pruned = true;
          try {
            var uri = DocumentUri.FromFileSystemPath(path);
            _publishDiagnostics(uri, new Container<Diagnostic>());
          } catch {
            // URI conversion may fail for unusual paths
          }
        }
      }
    }
    if (pruned) ScheduleRecompile();
    return pruned;
  }

  public void NotifyFileClosed(string filePath) {
    var normalized = NormalizePath(filePath);
    if (IsSingleFile) {
      // Cancel any pending recompile so it can't publish stale diagnostics
      // after the project has been torn down. _closed also blocks any
      // in-flight Recompile from publishing.
      _closed = true;
      lock (_debounceLock) {
        _debounceCts?.Cancel();
        _debounceCts = null;
      }
      _fileContents.TryRemove(normalized, out _);
      _diagnostics.TryRemove(normalized, out _);
      try {
        var uri = DocumentUri.FromFileSystemPath(filePath);
        _publishDiagnostics(uri, new Container<Diagnostic>());
      } catch {
        // URI conversion may fail for unusual paths
      }
    } else {
      // Multi-file project: drop the editor buffer so the next compile reads
      // fresh disk content for this file via SourceCollector.
      _fileContents.TryRemove(normalized, out _);
    }
  }

  public bool ContainsFile(string filePath) {
    var normalized = NormalizePath(filePath);
    if (IsSingleFile)
      return normalized == NormalizePath(RootPath);
    var normalizedRoot = NormalizePath(RootPath);
    return normalized.StartsWith(normalizedRoot + "/", StringComparison.Ordinal);
  }

  public CompletionInfo? GetCompletionInfo() => _lastSuccessfulCompletionInfo;

  /// <summary>
  /// Returns every .maxon source in the project (path -> content), matching
  /// what <c>maxon build</c> would compile. For multi-file projects this
  /// walks the directory via <see cref="SourceCollector"/>, with editor
  /// buffers overriding on-disk content for files open in the editor.
  /// Used by the LSP's go-to-definition text search.
  /// </summary>
  public IEnumerable<KeyValuePair<string, string>> GetFileContents() {
    var sources = CollectSources();
    foreach (var s in sources)
      yield return new KeyValuePair<string, string>(s.Path, s.Content);
  }

  public bool IsEmpty => IsSingleFile ? _fileContents.IsEmpty : CollectSources().Length == 0;

  public List<CompileError> GetDiagnostics(string filePath) {
    return _diagnostics.TryGetValue(NormalizePath(filePath), out var errors) ? errors : [];
  }

  /// <summary>
  /// Build the source array for this project exactly like <c>maxon build</c>
  /// would: delegate to the shared <see cref="SourceCollector"/>. Editor
  /// buffers in <c>_fileContents</c> override disk content so unsaved edits
  /// are what get compiled. Single-file projects skip the directory walk.
  /// </summary>
  private SourceFile[] CollectSources() {
    if (IsSingleFile) {
      return [.. _fileContents.Select(kv => new SourceFile(kv.Key, kv.Value))];
    }
    try {
      return SourceCollector.FromDirectory(RootPath, _fileContents);
    } catch {
      // Directory may have been removed mid-compile.
      return [];
    }
  }

  /// <summary>
  /// Triggers a debounced recompile without changing any editor buffer state.
  /// Used when an external disk change should cause the project to rebuild.
  /// </summary>
  public void NotifyExternalChange() => ScheduleRecompile();

  private void ScheduleRecompile() {
    CancellationToken token;
    lock (_debounceLock) {
      _debounceCts?.Cancel();
      _debounceCts = new CancellationTokenSource();
      token = _debounceCts.Token;
    }

    _ = Task.Run(async () => {
      try {
        await Task.Delay(300, token);
        Recompile();
      } catch (OperationCanceledException) {
        // Superseded by a newer change
      }
    });
  }

  private void Recompile() {
    var context = new IrContext();
    using var scope = context.PushScope();

    // Use the same collector maxon build uses — same files, same order, same
    // `---` truncation. Editor buffers override disk content.
    var sources = CollectSources();
    if (sources.Length == 0) return;

    // Track which files had errors
    var newDiagnostics = new Dictionary<string, List<CompileError>>();
    foreach (var source in sources)
      newDiagnostics[source.Path] = [];

    // Stdlib files are already compiled in the cached stdlib — use cached module for completion info
    if (_isStdlibProject) {
      if (_lastSuccessfulCompletionInfo == null) {
        try {
          var stdlibModule = StdlibLoader.GetStdlibModule();
          _lastSuccessfulCompletionInfo = new CompletionInfo(
            stdlibModule.TypeDefs,
            stdlibModule.Functions,
            [],
            stdlibModule.TypeAliasSources
          );
        } catch {
          // Stdlib compilation failure — fall through with no completion info
        }
      }
    } else {
      try {
        var module = StdlibLoader.GetStdlibModule();
        Compiler.Compiler.ResetStaticCompileState(context);
        var compileErrors = Compiler.Compiler.CompileSources(module, sources, false);

        if (compileErrors.Count == 0) {
          // Success: update cached completion info
          _lastSuccessfulCompletionInfo = new CompletionInfo(
            module.TypeDefs,
            module.Functions,
            [],
            module.TypeAliasSources
          );
        } else {
          foreach (var error in compileErrors) {
            var errorFile = error.FilePath != null ? NormalizePath(error.FilePath) : sources[0].Path;
            if (newDiagnostics.TryGetValue(errorFile, out var list))
              list.Add(error);
            else
              newDiagnostics[errorFile] = [error];
          }
        }
      } catch (Exception ex) {
        // Unexpected error — publish a synthetic diagnostic on the first file
        PublishSyntheticError(sources[0].Path, ex.Message);
        return;
      }
    }

    // If the project was closed while we were compiling, discard results
    // so we don't resurrect diagnostics on a closed document.
    if (_closed) return;

    // Update stored diagnostics and publish
    foreach (var (filePath, errors) in newDiagnostics) {
      _diagnostics[filePath] = errors;

      var lspDiagnostics = errors.Select(error => new Diagnostic {
        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
          new Position((error.Line ?? 1) - 1, (error.Column ?? 1) - 1),
          new Position((error.Line ?? 1) - 1, (error.Column ?? 1))
        ),
        Severity = DiagnosticSeverity.Error,
        Source = "maxon",
        Message = error.Message,
        Code = error.Code.Format()
      }).ToList();

      try {
        var uri = DocumentUri.FromFileSystemPath(filePath);
        _publishDiagnostics(uri, new Container<Diagnostic>(lspDiagnostics));
      } catch {
        // URI conversion may fail for unusual paths
      }
    }
  }

  private void PublishSyntheticError(string filePath, string message) {
    try {
      var uri = DocumentUri.FromFileSystemPath(filePath);
      _publishDiagnostics(uri, new Container<Diagnostic>(new Diagnostic {
        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
          new Position(0, 0), new Position(0, 1)),
        Severity = DiagnosticSeverity.Error,
        Source = "maxon",
        Message = message
      }));
    } catch {
      // URI conversion may fail
    }
  }

  private static bool IsUnderStdlib(string path) {
    var stdlibPath = StdlibLoader.FindStdlibPath();
    if (stdlibPath == null) return false;
    return NormalizePath(path).StartsWith(NormalizePath(stdlibPath), StringComparison.OrdinalIgnoreCase);
  }

  internal static string NormalizePath(string path) =>
    Path.GetFullPath(path).Replace('\\', '/');
}
