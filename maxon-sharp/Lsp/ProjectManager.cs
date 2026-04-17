using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace MaxonSharp.Lsp;

public class ProjectManager(Action<DocumentUri, Container<Diagnostic>> publishDiagnostics) {
  private readonly ConcurrentDictionary<string, Project> _projects = new();
  private readonly Action<DocumentUri, Container<Diagnostic>> _publishDiagnostics = publishDiagnostics;

  public Project GetOrCreateProject(string filePath, bool forceSingleFile = false) {
    // Check existing projects first
    var existing = FindProjectForFile(filePath);
    if (existing != null) return existing;

    if (!forceSingleFile) {
      // Discover project root by walking up looking for build.maxon
      var projectRoot = FindProjectRoot(filePath);

      if (projectRoot != null) {
        var normalizedRoot = Project.NormalizePath(projectRoot);
        return _projects.GetOrAdd(normalizedRoot, _ => {
          LspLog.Info($"Loaded multi-file project at {projectRoot}");
          return new Project(projectRoot, false, _publishDiagnostics);
        });
      }
    }

    // No build.maxon found (or single-file forced) — single-file project
    var normalizedFile = Project.NormalizePath(filePath);
    return _projects.GetOrAdd(normalizedFile, _ => {
      LspLog.Info($"Loaded single-file project for {filePath}");
      return new Project(filePath, true, _publishDiagnostics);
    });
  }

  public Project? FindProjectForFile(string filePath) {
    foreach (var project in _projects.Values) {
      if (project.ContainsFile(filePath))
        return project;
    }
    return null;
  }

  /// <summary>
  /// Called when a non-.maxon path (typically a directory) is deleted. Asks
  /// any project whose files could be under that path to prune them. Does an
  /// O(1) prefix check per project root to decide whether to dispatch — so
  /// unrelated projects pay almost nothing.
  /// </summary>
  public void NotifyPathDeleted(string path) {
    var normalized = Project.NormalizePath(path);
    var prefix = normalized + "/";
    foreach (var project in _projects.Values) {
      if (project.IsSingleFile) {
        // Single-file project: drop it entirely if its file lives under the
        // deleted path.
        var filePath = Project.NormalizePath(project.RootPath);
        if (filePath.StartsWith(prefix, StringComparison.Ordinal)) {
          RemoveFileFromProject(project.RootPath);
        }
        continue;
      }

      // Multi-file project: only dispatch if the deleted path is inside the
      // project root (otherwise it can't possibly own anything there).
      var projectRoot = Project.NormalizePath(project.RootPath);
      if (normalized != projectRoot &&
          !normalized.StartsWith(projectRoot + "/", StringComparison.Ordinal)) {
        continue;
      }

      project.NotifyPathDeleted(path);
      // If the entire project root went away, drop the now-empty project so a
      // re-created directory gets a fresh project (and we don't leak the entry).
      if (project.IsEmpty) {
        _projects.TryRemove(projectRoot, out _);
        LspLog.Info($"Unloaded multi-file project at {project.RootPath} (directory deleted)");
      }
    }
  }

  public void RemoveFileFromProject(string filePath) {
    var project = FindProjectForFile(filePath);
    if (project == null) return;

    project.NotifyFileClosed(filePath);

    // If single-file project, remove the project entirely
    if (project.IsSingleFile) {
      _projects.TryRemove(Project.NormalizePath(project.RootPath), out _);
      LspLog.Info($"Unloaded single-file project for {project.RootPath}");
    }
  }

  /// <summary>
  /// Walk up from the file's directory, stopping at the first build.maxon we
  /// find. If that build.maxon declares an exported build() function, return
  /// its directory as the project root; otherwise return null (the file should
  /// be treated as single-file). A bare build.maxon without build() is a
  /// task-runner script, not a project marker — see Program.cs line 259, where
  /// `maxon build` applies the same rule.
  /// </summary>
  private static string? FindProjectRoot(string filePath) {
    var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
    while (dir != null) {
      var buildFile = Path.Combine(dir, "build.maxon");
      if (File.Exists(buildFile))
        return HasExportedBuildFunction(buildFile) ? dir : null;
      var parent = Path.GetDirectoryName(dir);
      if (parent == dir) break;
      dir = parent;
    }
    return null;
  }

  /// <summary>
  /// Returns true if the given build.maxon declares `export function build(`.
  /// Uses a line-scan (no lexer/parser) to stay cheap — project-root discovery
  /// runs on every file open.
  /// </summary>
  private static bool HasExportedBuildFunction(string buildFilePath) {
    try {
      foreach (var rawLine in File.ReadLines(buildFilePath)) {
        var line = rawLine.TrimStart();
        if (line.StartsWith("export function build(") ||
            line.StartsWith("export function build "))
          return true;
      }
    } catch {
      // File may have been deleted between File.Exists and ReadLines
    }
    return false;
  }
}
