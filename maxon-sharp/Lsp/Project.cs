using System.Collections.Concurrent;
using MaxonSharp.Compiler;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
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

  private readonly Action<DocumentUri, Container<Diagnostic>> _publishDiagnostics = publishDiagnostics;
  private readonly bool _isStdlibProject = IsUnderStdlib(rootPath);

  public void NotifyFileChanged(string filePath, string content) {
    _fileContents[NormalizePath(filePath)] = content;
    ScheduleRecompile();
  }

  public void NotifyFileClosed(string filePath) {
    var normalized = NormalizePath(filePath);
    if (IsSingleFile) {
      _fileContents.TryRemove(normalized, out _);
    } else {
      // Revert to disk content so the project still compiles with this file
      try {
        var diskContent = File.ReadAllText(filePath);
        _fileContents[normalized] = diskContent;
      } catch {
        _fileContents.TryRemove(normalized, out _);
      }
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
  /// Returns all file contents currently tracked by the project (path -> content).
  /// </summary>
  public IEnumerable<KeyValuePair<string, string>> GetFileContents() => _fileContents;

  public List<CompileError> GetDiagnostics(string filePath) {
    return _diagnostics.TryGetValue(NormalizePath(filePath), out var errors) ? errors : [];
  }

  /// <summary>
  /// Load all .maxon files from a project directory into _fileContents.
  /// Called once when the project is created.
  /// </summary>
  public void LoadFilesFromDisk() {
    if (IsSingleFile) return;
    try {
      var files = Directory.GetFiles(RootPath, "*.maxon", SearchOption.AllDirectories);
      foreach (var file in files) {
        var normalized = NormalizePath(file);
        // Don't overwrite files already provided by the editor
        if (!_fileContents.ContainsKey(normalized)) {
          _fileContents[normalized] = File.ReadAllText(file);
        }
      }
    } catch {
      // Directory may not exist or be inaccessible
    }
  }

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
    var context = new MlirContext();
    using var scope = context.PushScope();

    // Build source file array from current contents
    var sources = _fileContents
      .Select(kv => new SourceFile(kv.Key, kv.Value))
      .ToArray();

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
        context.ResetIds();
        Compiler.Compiler.CompileSources(module, sources, false);

        // Success: update cached completion info
        _lastSuccessfulCompletionInfo = new CompletionInfo(
          module.TypeDefs,
          module.Functions,
          [],
          module.TypeAliasSources
        );
      } catch (CompileError ex) {
        // Record error for the appropriate file
        var errorFile = ex.FilePath != null ? NormalizePath(ex.FilePath) : sources[0].Path;
        if (newDiagnostics.TryGetValue(errorFile, out List<CompileError>? value))
          value.Add(ex);
        else
          newDiagnostics[errorFile] = [ex];
      } catch (Exception ex) {
        // Unexpected error — publish a synthetic diagnostic on the first file
        PublishSyntheticError(sources[0].Path, ex.Message);
        return;
      }
    }

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
