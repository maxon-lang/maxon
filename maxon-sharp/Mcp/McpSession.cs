using MaxonSharp.Mcp.Tools;

namespace MaxonSharp.Mcp;

/// <summary>
/// One client's session, minted by `initialize` and named by the `Mcp-Session-Id` header the client
/// echoes on every later request.
///
/// ⭐ THE SESSION IS WHY THE TRANSPORT IS HTTP. A tool call is not the unit of work here: an agent sets
/// a breakpoint, looks at what it caught, and decides what to ask next. That needs a debuggee that is
/// still alive between two requests, and the header the protocol already defines is exactly the handle
/// for it — so session identity is inherited rather than invented.
///
/// ⚠ AND IT IS WHY REAPING IS THIS CLASS'S JOB. Under stdio a closed pipe is an unmistakable "we are
/// done"; over HTTP a client simply VANISHES, with no close and no signal, and a debuggee parked in the
/// agent's stop-the-world loop would sit there forever. So a session is disposable, and every path that
/// can end one — an explicit `debug_stop`, a DELETE, the idle sweep, server shutdown, Ctrl-C — ends it
/// through <see cref="Dispose"/>.
/// </summary>
internal sealed class McpSession(string id) : IDisposable {

  public string Id { get; } = id;

  /// <summary>
  /// Serializes the requests of ONE session. A live debugger is a single conversation with one parked
  /// process — two concurrent `debug_step`s on one session is not a thing that has an answer — so the
  /// second request waits rather than racing. It is bounded by the same stop deadline every wait on the
  /// user's program is, so waiting here cannot outlast a tool call.
  ///
  /// It also pins the collected-event sink, which is thread-static: one request at a time per session
  /// means the thread that installs the collector is the thread that runs the command.
  /// </summary>
  public object Gate { get; } = new();

  private long _lastActivityTicks = DateTime.UtcNow.Ticks;

  /// <summary>
  /// How many requests are running against this session right now. It is the idle sweep's veto: a
  /// session with a request in flight is NOT idle however long that request has been running, because a
  /// breakpoint can legitimately be minutes away — and reaping a debuggee out from under a command that
  /// is using it would be a crash the caller could never explain. Read and written only under the
  /// server's session lock, which is what pairs it with the removal it guards.
  /// </summary>
  public int InFlight { get; set; }

  /// <summary>
  /// This session's live debuggee, or null when it has never started one. It is a NAMED field rather
  /// than a bag of per-family state because there is exactly one family with live state today and a
  /// typed field is what makes <see cref="Dispose"/> obviously complete. A future family that holds
  /// something live adds a field here and a line below — which is the whole extension point, and it is
  /// smaller than the indirection that would replace it.
  /// </summary>
  public McpDebugSession? Debug { get; set; }

  /// <summary>
  /// This session's scratch directory, created on first use.
  ///
  /// It belongs to the SESSION rather than to the debug session it is built for, and that is the fix
  /// rather than a tidy-up: a snippet that does not compile produces no debug session at all, so a
  /// workspace owned by one would be written and then owned by nobody. Here it is reaped whenever the
  /// session is, which is every way a session can end — and the one way it CANNOT (a kill this process
  /// never sees) is what <see cref="McpWorkspace.SweepDeadOwners"/> reclaims for the next server.
  /// </summary>
  public string Workspace() {
    Directory.CreateDirectory(McpWorkspace.ForSession(Id));
    return McpWorkspace.ForSession(Id);
  }

  public void Dispose() {
    // The debuggee FIRST: it holds its own executable open, so a workspace deleted before the kill
    // would leave the one file that matters behind.
    Debug?.Dispose();
    Debug = null;

    McpWorkspace.Delete(McpWorkspace.ForSession(Id));
  }

  /// Mark the session as used NOW, so the idle sweep measures silence rather than age.
  public void Touch() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

  /// True when nothing has touched this session for <paramref name="idleTimeout"/> — the one signal HTTP
  /// gives that a vanished client is never coming back.
  public bool IsIdleFor(TimeSpan idleTimeout) =>
    DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc) > idleTimeout;
}
