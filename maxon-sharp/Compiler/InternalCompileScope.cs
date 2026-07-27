namespace MaxonSharp.Compiler;

/// <summary>
/// Compiles an INTERNAL artifact — one the user did not ask for and will never run themselves — with
/// every noisy or artifact-producing compiler flag turned off, restoring them on exit.
///
/// Two callers need this and for the same reason: `maxon build` compiles a project's build-runner,
/// and `maxon test` compiles the test binary. Neither is the user's program, and both have their
/// output CAPTURED — so a flag that makes the emitted binary write to stderr does not merely add
/// noise, it corrupts the only channel the caller is reading. A test binary spewing mm-trace would
/// poison every captured failure; a build-runner doing it fills the pipe its JSON comes back on.
///
/// It is a type rather than a save/restore written at each site because that list was already
/// written down once, inline, and was already INCOMPLETE: <c>LiteralCoverage</c> reports to stderr
/// and was not reset, and <c>MmTraceRawOnly</c> was left to be governed transitively by a flag it is
/// only a modifier of. A second hand-maintained copy would have drifted the same way.
///
/// <c>NoDebugAgent</c> is deliberately NOT reset. It is not noise: it is a decision about what the
/// emitted binary CONTAINS, and an internal artifact built by a compiler invocation that was asked
/// to omit the debug agent has no reason to reinstate it.
///
/// It is a CLASS rather than a struct so that <c>default(T)</c> cannot exist: a defaulted instance
/// would carry all-false saved state and silently turn every flag off on dispose.
/// </summary>
internal sealed class InternalCompileScope : IDisposable {
  private readonly bool _mmTrace;
  private readonly bool _mmTraceRawOnly;
  private readonly bool _mmDebug;
  private readonly bool _asyncTrace;
  private readonly bool _debugStream;
  private readonly bool _debugInfo;
  private readonly bool _coverage;
  private readonly bool _literalCoverage;

  private InternalCompileScope() {
    _mmTrace = Compiler.MmTrace;
    _mmTraceRawOnly = Compiler.MmTraceRawOnly;
    _mmDebug = Compiler.MmDebug;
    _asyncTrace = Compiler.AsyncTrace;
    _debugStream = Compiler.DebugStream;
    _debugInfo = Compiler.DebugInfo;
    _coverage = Compiler.Coverage;
    _literalCoverage = Compiler.LiteralCoverage;

    Compiler.MmTrace = false;
    Compiler.MmTraceRawOnly = false;
    Compiler.MmDebug = false;
    Compiler.AsyncTrace = false;
    Compiler.DebugStream = false;
    // No sidecar: --debug-info is about the USER's program, and an internal artifact writing one
    // would leave a .mxdbg beside a binary nobody debugs.
    Compiler.DebugInfo = false;
    // Instrumenting an internal artifact would count ITS statements into the user's coverage report,
    // and would also trip the "--coverage requires the sidecar" rule just turned off above.
    Compiler.Coverage = false;
    Compiler.LiteralCoverage = false;
  }

  /// <summary>Enter the scope. Dispose restores every flag to what it was.</summary>
  public static InternalCompileScope Enter() => new();

  public void Dispose() {
    Compiler.MmTrace = _mmTrace;
    Compiler.MmTraceRawOnly = _mmTraceRawOnly;
    Compiler.MmDebug = _mmDebug;
    Compiler.AsyncTrace = _asyncTrace;
    Compiler.DebugStream = _debugStream;
    Compiler.DebugInfo = _debugInfo;
    Compiler.Coverage = _coverage;
    Compiler.LiteralCoverage = _literalCoverage;
  }
}
