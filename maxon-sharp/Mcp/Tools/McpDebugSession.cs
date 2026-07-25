using System.Text;

namespace MaxonSharp.Mcp.Tools;

/// <summary>
/// ONE live debuggee, held across HTTP requests — the thing this whole rung exists to make possible.
///
/// It owns a <see cref="MaxonDebugger"/> engine and the <see cref="MaxonDebugRepl.CommandSession"/> the
/// shared dispatcher threads through commands, so a tool call is a single line handed to
/// <see cref="MaxonDebugRepl.RunCommand"/> and the events it emitted. There is no second stop renderer
/// and no second command grammar here: everything below is lifecycle.
/// </summary>
internal sealed class McpDebugSession : IDisposable {

  private readonly MaxonDebugger _debugger;
  private readonly MaxonDebugRepl.CommandSession _commands = new();
  private readonly StringBuilder _output;

  public string ExePath { get; }

  /// <summary>
  /// Why this session can no longer be commanded, or null while it can. It is set the moment the
  /// program's own run ends — an exit, a crash, or a terminate — because that is when the parked frame
  /// every other tool reads stops existing. Refusals quote it, so "there is no session" and "the session
  /// you had has finished, and here is what became of it" stay different answers.
  /// </summary>
  public string? EndedReason { get; private set; }

  private McpDebugSession(MaxonDebugger debugger, string exePath, StringBuilder output) {
    _debugger = debugger;
    ExePath = exePath;
    _output = output;
  }

  /// <summary>
  /// Attach to <paramref name="exePath"/> and park it at entry, ready for breakpoints. The prologue is
  /// the batch face's, in the same order and with the same refusals — an agent that never attached must
  /// be told so rather than handed a session whose every later command fails obscurely.
  ///
  /// Throws <see cref="DebuggerException"/> on any refusal, having left nothing running.
  /// </summary>
  public static McpDebugSession Start(string exePath, IReadOnlyList<string> targetArgs,
      IReadOnlyDictionary<string, string>? targetEnv, TimeSpan stopTimeout, bool stopOthers) {
    var sidecar = MaxonDebugRepl.TryLoadSidecar(exePath, out var reason)
      ?? throw new DebuggerException(reason);

    // Both of the debuggee's streams are buffered rather than forwarded: this server has no console of
    // its own that a caller can see, so a `print` written to a terminal nobody is watching is output
    // LOST. It is drained into whichever tool result comes next.
    //
    // '\n' rather than the host's line ending, because this is a captured VALUE returned over a
    // protocol, not something being written to a console — the same text should come back whichever
    // machine the server runs on.
    var output = new StringBuilder();
    void Buffer(string line) {
      lock (output) output.Append(line).Append('\n');
    }

    var debugger = MaxonDebugger.Attach(exePath, targetArgs, sidecar, targetStdout: Buffer,
      stopTimeout: stopTimeout, targetEnv: targetEnv, stopOthers: stopOthers, targetStderr: Buffer);

    try {
      if (!debugger.WaitForAgentAlive())
        throw new DebuggerException(
          "the debug agent never attached (is MAXON_DEBUG honored by this build?)");
      if (MaxonDebugRepl.StopOthersUnsupportedText(debugger) is { } unsupported)
        throw new DebuggerException(unsupported);
    } catch {
      // The no-orphan obligation at the one moment no session owns the target yet.
      debugger.Dispose();
      throw;
    }

    return new McpDebugSession(debugger, exePath, output);
  }

  /// <summary>
  /// Run command lines through the shared dispatcher and return the events they emitted. Usually one —
  /// a tool call is a command — but `debug_start` arms its breakpoints and runs in a single answer.
  ///
  /// A command that ENDS the program stops the list where the batch face's loop does, and closes the
  /// session inside the same call: the closing outcome event lands in the result the agent is already
  /// reading, and the debuggee is reaped the instant it stops being one. The batch face does this at the
  /// end of its script; here every call is potentially the last.
  /// </summary>
  public IReadOnlyList<string> Run(params string[] commandLines) => MaxonDebugRepl.CollectEvents(() => {
    foreach (var commandLine in commandLines) {
      if (_commands.Finished) break;
      MaxonDebugRepl.RunCommand(_debugger, ExePath, commandLine, _commands);
    }
    if (_commands.Finished) Close();
  });

  /// Terminate the debuggee and report what became of it — `debug_stop`, and the same closing sequence a
  /// program that ended on its own goes through.
  public IReadOnlyList<string> Stop() => MaxonDebugRepl.CollectEvents(Close);

  /// <summary>
  /// Everything the debuggee has written since the last drain. Complete for a CLOSED session, because
  /// <see cref="MaxonDebugger.EndSession"/> joins the forwarding tasks before <see cref="Close"/>
  /// reports; for a live one it is what has arrived so far, which is what a stop can honestly say about
  /// a program that is still running.
  /// </summary>
  public string DrainOutput() {
    lock (_output) {
      var text = _output.ToString();
      _output.Clear();
      return text;
    }
  }

  /// <summary>
  /// Release the target, then report the outcome — in that order, and for the reason the batch face
  /// states: a session that timed out or was stopped leaves a LIVE debuggee, and the outcome event must
  /// describe what actually became of it rather than guess. Idempotent, so <see cref="Dispose"/> may
  /// call it after `debug_stop` already did.
  /// </summary>
  private void Close() {
    if (EndedReason != null) return;

    _debugger.EndSession(MaxonDebugger.SessionEnd.Immediate);
    MaxonDebugRepl.EmitOutcome(_debugger);
    // Worded as the CLI faces word it — `OutcomeText` already parenthesizes the abnormal endings, so a
    // second pair of brackets here produced "finished ((terminated by the debugger))".
    EndedReason = $"the debuggee has finished with code {_debugger.OutcomeText}";
  }

  public void Dispose() {
    // Not through Close(): a disposal may be a reap of a session nobody is listening to any more (an
    // idle sweep, a shutdown), and an outcome event emitted there has no result to travel in. The kill
    // is what matters and MaxonDebugger.Dispose performs it.
    EndedReason ??= "the debuggee was reaped";
    _debugger.Dispose();
  }
}
