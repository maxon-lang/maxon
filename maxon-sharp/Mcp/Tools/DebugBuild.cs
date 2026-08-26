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
  /// Parse the stdlib into the compiler's in-process cache, compiling nothing.
  ///
  /// EXACTLY ONE compile in a server's life pays for that parse — and by default it is the agent's
  /// first <c>debug_start</c>, which is the call it is most likely to be sitting and waiting on.
  /// Measured on this machine: a first <c>debug_start</c> that compiles costs ~1260 ms against ~330 ms
  /// for every one after it, and a first one that HITS the build cache still costs ~138 ms against
  /// ~27 ms, because even the cache check reads every stdlib source to fingerprint it.
  ///
  /// Called while the server is idle, this spends that second where nobody is waiting.
  ///
  /// ⚠ It deliberately does NOT take <see cref="BuildGate"/>, and both halves of that matter.
  /// It does not NEED it: the gate exists because a compile writes process-global option statics, and
  /// the one this class touches — <see cref="Compiler.Compiler.DebugInfo"/> — cannot reach a stdlib
  /// parse, because both sites that read it AND it with `!isStdlib` (`2-Parser.cs` captureSpan,
  /// `MaxonToStandardConversion` captureDebugLocals). That is also why one cached stdlib module is
  /// already shared between a `maxon build` and a `debug_start` today.
  /// And it must NOT take it: holding the gate here would park a cache-HIT `debug_start` behind a
  /// parse whose result it never uses — measured at 138 ms turning into 1293 ms, a regression this
  /// warm-up would otherwise have caused rather than cured. Without the gate that call waits only on
  /// the stdlib SOURCE cache, which is separately locked for exactly this reason.
  ///
  /// It warms the HOST's stdlib, which is the only one this class can ever ask for — a debug session
  /// runs the binary it builds, so it builds for the machine it is running on. The stdlib cache is
  /// keyed by target, so warming any other target's would be work nothing here consumes.
  /// </summary>
  public static void PrewarmStdlib() => StdlibLoader.GetStdlibModule(Compiler.CompileTarget.Default);

  /// <summary>
  /// Compile <paramref name="sourcePath"/> for the host and return the executable's path.
  ///
  /// The `.mxdbg` sidecar is FORCED on: a debug session without one has no line table, so a build that
  /// quietly omitted it would produce a binary this server cannot set a single breakpoint in. That is
  /// the same default `maxon build` applies, stated here rather than inherited, because the caller of
  /// this one has no flags to have said it with.
  ///
  /// ⚠ <paramref name="debugStream"/> is STATED by the caller rather than left to the process default,
  /// and that is a correctness point, not a convenience. The trace hooks are a property of how a binary
  /// was BUILT, and this method rebuilds whatever it is pointed at: with the flag unavailable, a
  /// `debug_start` on a program the user had built `--debugstream` recompiled it WITHOUT the hooks and
  /// overwrote their binary — after which `debug_trace` truthfully reported "built without
  /// --debugstream" about a build this server had just made. That left the tool's only interesting
  /// answer unreachable through the entire server. (Measured: the exe's hash changed across one
  /// `debug_start`.)
  ///
  /// <paramref name="coverage"/> is stated by the caller for exactly the same reason, and it is the
  /// same hazard: coverage instrumentation is a property of the BUILD, so a `debug_start` that
  /// silently rebuilt a user's instrumented binary without it would leave the coverage family
  /// truthfully reporting "not built with --coverage" about a binary this server had just replaced.
  /// </summary>
  public static string Compile(string sourcePath, bool debugStream, bool coverage) {
    if (Directory.Exists(sourcePath))
      throw new McpBuildException(
        $"'{sourcePath}' is a directory. debug_start builds ONE .maxon file (or a snippet); "
        + "build a project with `maxon build <dir>` first and pass its executable's own source file.");

    if (!File.Exists(sourcePath))
      throw new McpBuildException($"source file not found: '{sourcePath}'");

    // The host is refused HERE, not left to Compiler.Compile below, for Program.ParseTarget's two
    // stated reasons — both of which this method reaches first: GetOutputExtension on the very next
    // line dies on an OS the roster does not hold with an unhandled ArgumentException, and
    // BuildCache.IsCacheValid would otherwise hand back a cached binary for a request that should
    // have been refused.
    var target = CompileTarget.Default;
    if (target.Unsupported is { } unsupported) throw new McpBuildException(unsupported.Format());

    var outputPath = Program.ResolveOutputPath(sourcePath, Program.GetOutputExtension(target));
    var projectDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
    var sources = new SourceFile[] {
      new(sourcePath, SourceCollector.ReadUpToSeparator(File.ReadAllText(sourcePath)), projectDir),
    };

    lock (BuildGate) {
      var previousDebugInfo = Compiler.Compiler.DebugInfo;
      var previousDebugStream = Compiler.Compiler.DebugStream;
      var previousCoverage = Compiler.Compiler.Coverage;
      Compiler.Compiler.DebugInfo = true;
      // Set BEFORE the cache probe below, which compares the option against the manifest: a binary
      // built with the other setting must be seen as out of date rather than handed back as current.
      Compiler.Compiler.DebugStream = debugStream;
      Compiler.Compiler.Coverage = coverage;
      try {
        // The same cache `maxon build` consults, and consulted for a second reason here: a binary that
        // is already current must not be REWRITTEN, because a debuggee from another session may be
        // running it and the OS will not let its own image be overwritten. An unchanged program is
        // therefore debuggable from two sessions at once; a CHANGED one while a debuggee holds the old
        // image is refused by the OS, which is the honest answer to "replace the file I am running".
        if (BuildCache.IsCacheValid(projectDir, sources, outputPath, target)) return outputPath;

        // Captured BEFORE the compile — see BuildCache.CaptureInputs.
        var debugInputs = BuildCache.CaptureInputs(sources, target);
        var result = new Compiler.Compiler().Compile(sources, outputPath, irOutputPath: null,
          dumpStagesBasePath: null, target: target);
        if (!result.Success)
          throw new McpBuildException(string.Join(Environment.NewLine, result.Errors.Select(e => e.Format())));

        BuildCache.WriteCache(projectDir, debugInputs, outputPath);
      } finally {
        Compiler.Compiler.DebugInfo = previousDebugInfo;
        Compiler.Compiler.DebugStream = previousDebugStream;
        Compiler.Compiler.Coverage = previousCoverage;
      }
    }

    return outputPath;
  }
}
