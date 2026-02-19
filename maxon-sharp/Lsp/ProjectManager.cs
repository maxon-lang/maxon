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
          var project = new Project(projectRoot, false, _publishDiagnostics);
          project.LoadFilesFromDisk();
          return project;
        });
      }
    }

    // No build.maxon found (or single-file forced) — single-file project
    var normalizedFile = Project.NormalizePath(filePath);
    return _projects.GetOrAdd(normalizedFile, _ =>
      new Project(filePath, true, _publishDiagnostics));
  }

  public Project? FindProjectForFile(string filePath) {
    foreach (var project in _projects.Values) {
      if (project.ContainsFile(filePath))
        return project;
    }
    return null;
  }

  public void RemoveFileFromProject(string filePath) {
    var project = FindProjectForFile(filePath);
    if (project == null) return;

    project.NotifyFileClosed(filePath);

    // If single-file project, remove the project entirely
    if (project.IsSingleFile) {
      _projects.TryRemove(Project.NormalizePath(project.RootPath), out _);
    }
  }

  /// <summary>
  /// Walk up from the file's directory looking for a build.maxon file.
  /// Returns the directory containing build.maxon, or null if not found.
  /// </summary>
  private static string? FindProjectRoot(string filePath) {
    var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
    while (dir != null) {
      if (File.Exists(Path.Combine(dir, "build.maxon")))
        return dir;
      var parent = Path.GetDirectoryName(dir);
      if (parent == dir) break;
      dir = parent;
    }
    return null;
  }
}
