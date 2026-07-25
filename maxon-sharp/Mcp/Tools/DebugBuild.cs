using MaxonSharp.Compiler;

namespace MaxonSharp.Mcp.Tools;

/// <summary>
/// A source that could not be turned into a debuggable binary — a file that is not there, or a compile
/// that produced diagnostics. The diagnostics ARE the answer, so they travel with the failure: an agent
/// that asked to debug a program it just wrote needs the compiler's errors, not "build failed".
/// </summary>
internal sealed class McpBuildException(string message) : Exception(message);

/// <summary>
/// Building the program a debug session is about to attach to.
///
/// It compiles through the same <see cref="Compiler.Compiler.Compile"/> entry `maxon build` uses, one
/// file at a time. Single-file only, and a directory is REFUSED rather than half-handled: a project
/// build runs `build.maxon` through a spawned build-runner and resolves an output path from its config,
/// which is a BUILD family's job — the user has already named building as a family that follows this
/// one, and half of it implemented here would be the copy it then has to reconcile with.
/// </summary>
internal static class DebugBuild {

  /// <summary>
  /// Compiles are serialized, because the compiler's options are process-global statics
  /// (<see cref="Compiler.Compiler.DebugInfo"/> and its siblings) and two sessions building at once
  /// would be two writers of one switch. A debug server builds rarely — once per `debug_start` — so a
  /// gate costs nothing and removes the whole class of question.
  /// </summary>
  private static readonly object BuildGate = new();

  /// <summary>
  /// Compile <paramref name="sourcePath"/> for the host and return the executable's path.
  ///
  /// The `.mxdbg` sidecar is FORCED on: a debug session without one has no line table, so a build that
  /// quietly omitted it would produce a binary this server cannot set a single breakpoint in. That is
  /// the same default `maxon build` applies, stated here rather than inherited, because the caller of
  /// this one has no flags to have said it with.
  /// </summary>
  public static string Compile(string sourcePath) {
    if (Directory.Exists(sourcePath))
      throw new McpBuildException(
        $"'{sourcePath}' is a directory. debug_start builds ONE .maxon file (or a snippet); "
        + "build a project with `maxon build <dir>` first and pass its executable's own source file.");

    if (!File.Exists(sourcePath))
      throw new McpBuildException($"source file not found: '{sourcePath}'");

    var target = CompileTarget.Default;
    var outputPath = Program.ResolveOutputPath(sourcePath, Program.GetOutputExtension(target));
    var projectDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
    var sources = new SourceFile[] {
      new(sourcePath, SourceCollector.ReadUpToSeparator(File.ReadAllText(sourcePath)), projectDir),
    };

    lock (BuildGate) {
      var previousDebugInfo = Compiler.Compiler.DebugInfo;
      Compiler.Compiler.DebugInfo = true;
      try {
        // The same cache `maxon build` consults, and consulted for a second reason here: a binary that
        // is already current must not be REWRITTEN, because a debuggee from another session may be
        // running it and the OS will not let its own image be overwritten. An unchanged program is
        // therefore debuggable from two sessions at once; a CHANGED one while a debuggee holds the old
        // image is refused by the OS, which is the honest answer to "replace the file I am running".
        if (BuildCache.IsCacheValid(projectDir, sources, outputPath, target)) return outputPath;

        var result = new Compiler.Compiler().Compile(sources, outputPath, irOutputPath: null,
          dumpStagesBasePath: null, target: target);
        if (!result.Success)
          throw new McpBuildException(string.Join(Environment.NewLine, result.Errors.Select(e => e.Format())));

        BuildCache.WriteCache(projectDir, sources, outputPath, target);
      } finally {
        Compiler.Compiler.DebugInfo = previousDebugInfo;
      }
    }

    return outputPath;
  }
}
