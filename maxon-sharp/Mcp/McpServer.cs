using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MaxonSharp.Testing;

namespace MaxonSharp.Mcp;

/// <summary>
/// `maxon mcp` — the compiler's own MCP server, over Streamable HTTP, hosting LIVE sessions.
///
/// It is a peer of `lsp-server`: another thin front-end over the engines this binary already owns, not a
/// separate process shelling out to the CLI. Debug is the first tool family; the registry takes more.
///
/// TRANSPORT. One endpoint, <see cref="EndpointPath"/>, taking a single JSON-RPC message per POST and
/// answering `application/json`. Nothing here streams, so no response is ever `text/event-stream` and
/// GET (which exists only to open a server-to-client stream) is answered 405 rather than left to look
/// like a missing route. `initialize` mints an <see cref="SessionHeader"/> the client echoes afterwards,
/// and THAT is what a live debuggee hangs on — session identity comes from the protocol instead of being
/// invented beside it.
///
/// 🔴 SECURITY. This server compiles and runs arbitrary programs, so an open port is remote code
/// execution, and the two defences are not optional:
///   * it binds LOOPBACK ONLY (<see cref="LoopbackPrefixes"/>), and there is no flag to widen it —
///     widening is a design conversation, not a default;
///   * it validates `Origin` on every request, because without that a web page the user merely VISITS
///     can drive this endpoint through the browser (DNS rebinding) and run code as them.
/// Both are gated by <c>scripts/check-debug-goldens.sh</c>, which asserts a non-loopback address cannot
/// connect and that a foreign `Origin` is refused.
///
/// ⚠ HTTP HAS NO EOF. Under stdio a closed pipe says "we are done"; here a client simply vanishes. So a
/// session is reaped FOUR ways — an explicit `debug_stop`, a protocol DELETE, the idle sweep, and
/// shutdown (Ctrl-C included) — and on Windows the whole process tree additionally rides in a job object
/// so that even a `taskkill /F`, which no handler can intercept, cannot leave a debuggee parked forever.
/// </summary>
internal static class McpServer {

  /// The one endpoint. Everything else is a 404, so a mistyped URL fails loudly rather than looking like
  /// a server that ignores requests.
  private const string EndpointPath = "/mcp";

  /// <summary>
  /// The MCP session header. The client gets it from `initialize` and echoes it on every later request;
  /// an unknown or expired value is refused with 404, never quietly given a fresh session — a debugger
  /// that silently forgot your breakpoints and started over is the worst possible answer.
  /// </summary>
  private const string SessionHeader = "Mcp-Session-Id";

  /// <summary>
  /// The protocol revision this server speaks. One version, stated rather than negotiated: the spec's
  /// rule for a client asking for something else is to answer with a version the server does support,
  /// which is exactly this.
  /// </summary>
  private const string ProtocolVersion = "2025-06-18";

  private const string ServerName = "maxon";
  private const string ServerVersion = "0.1.0";

  public const string PortFlag = "--port=";
  public const string IdleTimeoutFlag = "--idle-timeout=";
  public const string MaxSessionsFlag = "--max-sessions=";

  /// A port in the IANA dynamic range, so the default is unlikely to collide with a service. A taken
  /// port is a LOUD failure rather than a silent hop to another one: a client configured for this URL
  /// would otherwise connect to something else entirely.
  private const int DefaultPort = 47821;

  /// <summary>
  /// How long a session may go untouched before it is reaped. It is the only thing standing between a
  /// vanished client and a debuggee parked forever, so it is generous enough for an agent that stops to
  /// think and short enough that an abandoned process does not outlive the conversation.
  /// </summary>
  private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

  /// <summary>
  /// How many sessions may exist at once. EACH ONE CAN OWN A REAL PROCESS, which is the whole reason
  /// there is a cap: a client that reconnects in a loop would otherwise mint debuggees without bound.
  /// Past it, `initialize` refuses by name rather than accepting and failing later.
  /// </summary>
  private const int DefaultMaxSessions = 8;

  private static readonly Dictionary<string, McpSession> Sessions = new(StringComparer.Ordinal);

  /// <summary>
  /// Guards the session table AND the in-flight bookkeeping that makes reaping safe: a session is only
  /// ever removed while it has no request running, and a request only ever attaches to a session that is
  /// still in the table, because both happen under this lock. Without that pairing the idle sweep could
  /// dispose a <see cref="MaxonDebugger"/> out from under a command that was using it.
  /// </summary>
  private static readonly object SessionsGate = new();

  /// <summary>
  /// The job the server and every process it spawns belongs to, held for the process's whole life. When
  /// the last handle to it closes — INCLUDING when the OS tears this process down after a kill nothing
  /// could intercept — every process still in the job is terminated. It is the only orphan guard that
  /// survives `taskkill /F`, and it is a no-op off Windows, where SIGTERM/SIGINT are catchable instead.
  /// </summary>
  private static WindowsJobObject? _processTreeJob;

  public static int Run(string[] args) {
    if (!TryParseOptions(args, out var port, out var idleTimeout, out var maxSessions)) return 1;

    _processTreeJob = AssignSelfToJob();

    // Reclaim what a PREVIOUS server could not: a kill it never saw leaves its scratch directories
    // behind, and startup is the moment to clear them without racing a living server for them.
    McpWorkspace.SweepDeadOwners();

    var listener = new HttpListener();
    foreach (var prefix in LoopbackPrefixes(port)) listener.Prefixes.Add(prefix);

    try {
      listener.Start();
    } catch (HttpListenerException ex) {
      // Loudly, and naming the port: a client configured for this URL must never end up talking to
      // whatever else is on it.
      Console.Error.WriteLine($"maxon mcp: cannot listen on port {port}: {ex.Message}");
      return 1;
    }

    using var idleSweep = StartIdleSweep(idleTimeout);
    InstallShutdownHandlers(listener);

    Console.WriteLine($"maxon mcp: listening on http://127.0.0.1:{port}{EndpointPath}");
    Console.WriteLine($"maxon mcp: loopback only; idle sessions reaped after "
      + $"{idleTimeout.TotalSeconds:0.###}s; at most {maxSessions} concurrent sessions.");

    // AFTER the port is open and announced, not before: the parse is a second of work, and a client
    // that connects during it must find a listener rather than a refused connection.
    StartStdlibWarmup();

    while (listener.IsListening) {
      HttpListenerContext context;
      try {
        context = listener.GetContext();
      } catch (HttpListenerException) {
        break;                                       // the listener was stopped: shutdown, not a fault
      } catch (InvalidOperationException) {
        break;                                       // same, seen when Stop() races GetContext
      }

      // One request per thread-pool thread, and every request runs SYNCHRONOUSLY on it: the collected
      // event sink is thread-static, so a tool call must not hop threads between installing it and
      // reading it back.
      ThreadPool.QueueUserWorkItem(_ => Serve(context, maxSessions));
    }

    ReapAllSessions();
    return 0;
  }

  /// <summary>
  /// The prefixes the listener binds. BOTH loopback families, so a client that resolves `localhost` to
  /// `::1` is not left unable to connect, and NOTHING else — no wildcard, no hostname, no `+`. A host
  /// with no IPv6 stack degrades to IPv4 with a printed reason rather than failing to start or silently
  /// binding less than it claimed.
  /// </summary>
  private static IReadOnlyList<string> LoopbackPrefixes(int port) {
    var prefixes = new List<string> { $"http://127.0.0.1:{port}/" };
    if (Socket.OSSupportsIPv6) prefixes.Add($"http://[::1]:{port}/");
    else Console.Error.WriteLine("maxon mcp: no IPv6 on this host; listening on 127.0.0.1 only.");
    return prefixes;
  }

  private static WindowsJobObject? AssignSelfToJob() {
    try {
      var job = new WindowsJobObject();
      using var self = Process.GetCurrentProcess();
      if (job.AssignProcess(self.Handle)) return job;

      job.Dispose();
    } catch (InvalidOperationException) {
      // Fall through to the warning: the job object could not be created or configured.
    }

    // Said out loud rather than swallowed: without it, a debuggee CAN outlive a hard-killed server, and
    // the operator is the only one who can then clean it up.
    Console.Error.WriteLine("maxon mcp: could not put this process in a job object; a debuggee may "
      + "survive if this server is killed with an unstoppable signal. debug_stop, the idle timeout and "
      + "Ctrl-C are unaffected.");
    return null;
  }

  /// <summary>
  /// Pay the compiler's one-off stdlib parse here, on a background thread, instead of inside whichever
  /// tool call happens to compile first — see <see cref="Tools.DebugBuild.PrewarmStdlib"/> for what it
  /// costs and why the agent should not be the one paying it.
  /// </summary>
  private static void StartStdlibWarmup() =>
    ThreadPool.QueueUserWorkItem(_ => {
      try {
        Tools.DebugBuild.PrewarmStdlib();
      } catch (Exception ex) {
        // Caught BROADLY, and this is the one place that is right: an escaping exception on a pool
        // thread takes the whole process down, and a stdlib that will not pre-parse must not stop a
        // server from serving. The first real compile raises the same failure where a caller can be
        // told about it, which is where a compile failure belongs.
        Console.Error.WriteLine($"maxon mcp: could not pre-parse the stdlib ({ex.Message}); the first "
          + "debug_start will do it instead.");
      }
    });

  // ---- Shutdown ----

  /// <summary>
  /// Every shutdown path this process can OBSERVE ends the same way: stop listening, reap every session.
  /// Ctrl-C is intercepted rather than allowed to terminate, because the default would kill the server
  /// and leave its debuggees parked; SIGTERM gets the same treatment where the platform raises it; and
  /// ProcessExit catches an exit that came some other way. The path none of them can observe — a
  /// `taskkill /F` — is what the job object is for.
  /// </summary>
  private static void InstallShutdownHandlers(HttpListener listener) {
    void Shutdown() {
      if (listener.IsListening) listener.Stop();
      ReapAllSessions();
    }

    Console.CancelKeyPress += (_, e) => {
      e.Cancel = true;
      Console.Error.WriteLine("maxon mcp: interrupted; reaping sessions.");
      Shutdown();
    };

    AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();

    try {
      // HELD in a static, not discarded: a PosixSignalRegistration UNREGISTERS itself when it is
      // finalized, so dropping the handle would leave SIGTERM handling working right up until the first
      // garbage collection — and then silently not.
      _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal => {
        signal.Cancel = true;
        Console.Error.WriteLine("maxon mcp: terminated; reaping sessions.");
        Shutdown();
      });
    } catch (PlatformNotSupportedException) {
      // Windows does not raise SIGTERM; CancelKeyPress and the job object cover it there.
    }
  }

  private static PosixSignalRegistration? _sigterm;

  /// <summary>
  /// Shutdown, and the ONE reap that does not wait for a busy session — deliberately. <see
  /// cref="EndSession"/> can afford to let a running command finish because the server lives on; here
  /// the process is going away, so waiting out a stop timeout would only delay the kill that is the
  /// whole point of the handler. The listener is already stopped, so nothing new can arrive, and an
  /// in-flight command's <see cref="Detach"/> finds the session un-retired and disposes nothing twice.
  /// </summary>
  private static void ReapAllSessions() {
    List<McpSession> doomed;
    lock (SessionsGate) {
      doomed = [.. Sessions.Values];
      Sessions.Clear();
    }
    foreach (var session in doomed) session.Dispose();

    // With every session gone this server's scratch tree is empty, so the ordinary shutdown leaves
    // nothing at all behind — the sweep exists for the shutdowns that never run.
    McpWorkspace.DeleteOwn();
  }

  // ---- Idle reaping ----

  /// <summary>
  /// How the sweep's period is derived from the timeout it enforces. A fraction rather than a fixed
  /// period so a short timeout is still enforced promptly and a long one does not make an idle server
  /// spin; the two bounds keep both ends sane whatever the operator asked for.
  /// </summary>
  private const int IdleSweepsPerTimeout = 4;
  private const double MinIdleSweepMs = 100;
  private const double MaxIdleSweepMs = 5000;

  private static Timer StartIdleSweep(TimeSpan idleTimeout) {
    var period = TimeSpan.FromMilliseconds(Math.Clamp(
      idleTimeout.TotalMilliseconds / IdleSweepsPerTimeout, MinIdleSweepMs, MaxIdleSweepMs));
    return new Timer(_ => SweepIdleSessions(idleTimeout), state: null, period, period);
  }

  private static void SweepIdleSessions(TimeSpan idleTimeout) {
    List<McpSession> doomed = [];
    lock (SessionsGate) {
      foreach (var session in Sessions.Values)
        // A session with a request IN FLIGHT is not idle however long that request has been running: a
        // breakpoint may be minutes away, and reaping the debuggee mid-command would pull the engine out
        // from under it. The clock only starts when the last request finishes.
        if (session.InFlight == 0 && session.IsIdleFor(idleTimeout)) doomed.Add(session);

      foreach (var session in doomed) Sessions.Remove(session.Id);
    }

    foreach (var session in doomed) {
      Console.Error.WriteLine($"maxon mcp: reaping idle session {session.Id}.");
      session.Dispose();
    }
  }

  // ---- One request ----

  private static void Serve(HttpListenerContext context, int maxSessions) {
    try {
      Route(context, maxSessions);
    } catch (Exception ex) {
      // A fault while answering must not take the server down and must not leave the client hanging.
      Console.Error.WriteLine($"maxon mcp: internal error answering a request: {ex}");
      TryRespondInternalError(context);
    } finally {
      try {
        context.Response.Close();
      } catch (Exception) {
        // The client hung up mid-answer; nothing further to do about it.
      }
    }
  }

  private static void Route(HttpListenerContext context, int maxSessions) {
    var request = context.Request;

    // FIRST, ahead of everything: the DNS-rebinding gate. A refused Origin never reaches a tool, never
    // starts a session, and never compiles anything.
    if (!OriginAllowed(request.Headers["Origin"])) {
      RespondText(context, HttpStatusCode.Forbidden,
        "maxon mcp serves loopback origins only (this server compiles and runs programs).");
      return;
    }

    if (request.Url?.AbsolutePath != EndpointPath) {
      RespondText(context, HttpStatusCode.NotFound, $"no such endpoint; MCP is at {EndpointPath}.");
      return;
    }

    switch (request.HttpMethod) {
      case "POST":
        ServePost(context, maxSessions);
        return;

      case "DELETE":
        // The protocol's own "I am done": the client ending its session explicitly, which is also the
        // one reap that needs no timeout to notice.
        RespondText(context, EndSession(request.Headers[SessionHeader]) ? HttpStatusCode.NoContent
          : HttpStatusCode.NotFound, "");
        return;

      case "GET":
        // GET opens a server-to-client stream. Nothing here pushes, so it is refused by name rather than
        // left to look like a broken route.
        RespondText(context, HttpStatusCode.MethodNotAllowed,
          "this server sends no unsolicited messages, so it offers no event stream; POST to " + EndpointPath);
        return;

      default:
        RespondText(context, HttpStatusCode.MethodNotAllowed, $"unsupported method {request.HttpMethod}.");
        return;
    }
  }

  /// <summary>
  /// Is this `Origin` one we serve?
  ///
  /// ABSENT is allowed and PRESENT-BUT-FOREIGN is refused, which is the distinction that matters: a
  /// browser always sends `Origin` on the cross-origin request an attack would have to make, while the
  /// non-browser clients this server is actually for (an MCP host, curl) send none at all. Requiring the
  /// header would therefore reject every real client while adding nothing against the threat.
  /// An unparseable or opaque origin (`null`, from a sandboxed frame) fails the parse and is refused.
  /// </summary>
  private static bool OriginAllowed(string? origin) {
    if (string.IsNullOrEmpty(origin)) return true;
    return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
  }

  private static void ServePost(HttpListenerContext context, int maxSessions) {
    string body;
    using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
      body = reader.ReadToEnd();

    JsonDocument message;
    try {
      message = JsonDocument.Parse(body);
    } catch (JsonException ex) {
      RespondJson(context, HttpStatusCode.OK, RpcError(rawId: "null", RpcCode.ParseError, ex.Message));
      return;
    }

    using (message) {
      if (message.RootElement.ValueKind != JsonValueKind.Object) {
        // Includes a JSON-RPC batch: an array is refused rather than half-served, and this protocol
        // revision removed batching anyway.
        RespondJson(context, HttpStatusCode.OK, RpcError(rawId: "null", RpcCode.InvalidRequest,
          "expected one JSON-RPC object (this server does not accept batches)"));
        return;
      }

      Dispatch(context, message.RootElement, maxSessions);
    }
  }

  private static void Dispatch(HttpListenerContext context, JsonElement message, int maxSessions) {
    var method = message.TryGetProperty("method", out var methodElement)
      && methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString()! : "";

    // A NOTIFICATION carries no `id` and gets no response body at all — only an acknowledgement that it
    // was accepted. `notifications/initialized` is the one every client sends.
    var hasId = message.TryGetProperty("id", out var idElement)
      && idElement.ValueKind != JsonValueKind.Null;
    var rawId = hasId ? idElement.GetRawText() : "null";

    if (method.Length == 0) {
      RespondJson(context, HttpStatusCode.OK,
        RpcError(rawId, RpcCode.InvalidRequest, "missing or non-string `method`"));
      return;
    }

    if (method == "initialize") {
      Initialize(context, rawId, maxSessions);
      return;
    }

    // Every other method belongs to a session. Attaching here — under the same lock the reaper removes
    // sessions with — is what makes "the session is alive for the duration of this request" true.
    var sessionId = context.Request.Headers[SessionHeader];
    if (!TryAttach(sessionId, out var session)) {
      // 404 is the protocol's own answer for a session that is gone, and the client's cue to start a new
      // one. Answering with a FRESH session instead would silently discard everything the caller set up.
      RespondJson(context, HttpStatusCode.NotFound, RpcError(rawId, RpcCode.InvalidRequest,
        string.IsNullOrEmpty(sessionId)
          ? $"missing `{SessionHeader}` — every request after `initialize` must echo the id it returned"
          : $"unknown or expired session '{sessionId}'; call `initialize` to start a new one"));
      return;
    }

    try {
      // One conversation at a time per session: a live debugger has one parked process, and two
      // concurrent commands on it is not a question with an answer.
      lock (session.Gate) {
        DispatchSessionMethod(context, session, message, method, rawId, hasId);
      }
    } finally {
      Detach(session);
    }
  }

  private static void DispatchSessionMethod(HttpListenerContext context, McpSession session,
      JsonElement message, string method, string rawId, bool hasId) {
    switch (method) {
      case "notifications/initialized":
        RespondAccepted(context);
        return;

      case "ping":
        RespondJson(context, HttpStatusCode.OK, RpcResult(rawId, "{}"));
        return;

      case "tools/list":
        RespondJson(context, HttpStatusCode.OK, RpcResult(rawId, ToolsListJson));
        return;

      case "tools/call":
        RespondJson(context, HttpStatusCode.OK, CallTool(session, message, rawId));
        return;

      default:
        if (!hasId) {
          // An unknown NOTIFICATION is still a notification: no response, by the protocol's rule.
          RespondAccepted(context);
          return;
        }
        RespondJson(context, HttpStatusCode.OK,
          RpcError(rawId, RpcCode.MethodNotFound, $"method not found: {method}"));
        return;
    }
  }

  // ---- Sessions ----

  private static void Initialize(HttpListenerContext context, string rawId, int maxSessions) {
    string id;
    lock (SessionsGate) {
      if (Sessions.Count >= maxSessions) {
        // Refused by name rather than accepted and leaked: every session can own a live process.
        RespondJson(context, HttpStatusCode.OK, RpcError(rawId, RpcCode.InvalidRequest,
          $"this server is holding its maximum of {maxSessions} sessions; end one with a DELETE or "
          + $"`debug_stop`, or start the server with {MaxSessionsFlag}<n>"));
        return;
      }

      id = Convert.ToHexString(RandomNumberGenerator.GetBytes(SessionIdBytes)).ToLowerInvariant();
      Sessions[id] = new McpSession(id);
    }

    RespondJson(context, HttpStatusCode.OK, RpcResult(rawId, InitializeJson), sessionId: id);
  }

  /// Long enough that a session id cannot be guessed by a page that got past the Origin check.
  private const int SessionIdBytes = 16;

  /// <summary>
  /// Take the named session for the duration of one request, marking it in-flight so the idle sweep
  /// cannot reap it underneath. Returns false for an id that names nothing — expired, reaped, or never
  /// minted — which is a refusal and never a quietly-created new session.
  /// </summary>
  private static bool TryAttach(string? sessionId, out McpSession session) {
    session = null!;
    if (string.IsNullOrEmpty(sessionId)) return false;

    lock (SessionsGate) {
      if (!Sessions.TryGetValue(sessionId, out var found)) return false;
      found.InFlight++;
      found.Touch();
      session = found;
      return true;
    }
  }

  private static void Detach(McpSession session) {
    bool disposeNow;
    lock (SessionsGate) {
      session.InFlight--;
      // Touched on the way OUT as well as in, so the idle clock measures silence since the last answer
      // rather than time since a long command started.
      session.Touch();

      // The last request to leave a RETIRED session is what disposes it: a DELETE removed it from the
      // table but could not free the engine this command was still using.
      disposeNow = session.Retired && session.InFlight == 0;
    }

    if (disposeNow) session.Dispose();
  }

  /// <summary>
  /// End a session by id, reaping its debuggee. False when no such session exists.
  ///
  /// ⚠ IT DOES NOT DISPOSE A SESSION THAT IS BUSY. Disposing one reaps the <see cref="MaxonDebugger"/>
  /// a running command is in the middle of using, and the command does not fail — it returns whatever
  /// it had rendered so far, so an agent that DELETEd a connection while a `debug_start` was still
  /// running got HTTP 200 and an event list containing the armed breakpoint and no stop, with nothing
  /// in the answer saying it had been cut short. (Measured, not theorised: forced by DELETEing six
  /// seconds into a nine-second run.) The idle sweep already refuses to reap a busy session for the
  /// same reason; a DELETE cannot refuse, because the client has asked for the session to END, so it
  /// retires the session instead — out of the table immediately, so nothing new can attach to it, and
  /// disposed by <see cref="Detach"/> when the last request leaves.
  /// </summary>
  private static bool EndSession(string? sessionId) {
    if (string.IsNullOrEmpty(sessionId)) return false;

    McpSession doomed;
    bool disposeNow;
    lock (SessionsGate) {
      if (!Sessions.TryGetValue(sessionId, out var found)) return false;

      Sessions.Remove(sessionId);
      found.Retired = true;
      disposeNow = found.InFlight == 0;
      doomed = found;
    }

    if (disposeNow) doomed.Dispose();
    return true;
  }

  // ---- Tools ----

  /// <summary>
  /// The `tools/list` answer, built ONCE. The registry is static and a schema is not a function of the
  /// request, so this document is the same bytes on every call — rebuilding it per request was work
  /// whose result could not differ. (It is ~0.2 ms of a ~0.5 ms call; the point is that a constant is
  /// now spelled as one, not the milliseconds.)
  /// </summary>
  private static readonly string ToolsListJson = BuildToolsListJson();

  /// Likewise: one protocol version, one server name, no negotiation — so one document.
  private static readonly string InitializeJson = BuildInitializeJson();

  private static string BuildToolsListJson() {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer)) {
      w.WriteStartObject();
      w.WriteStartArray("tools");
      foreach (var tool in McpToolRegistry.All) {
        w.WriteStartObject();
        w.WriteString("name", tool.Name);
        w.WriteString("description", tool.Description);
        w.WriteStartObject("inputSchema");
        w.WriteString("type", "object");
        McpSchema.WriteProperties(w, tool.Args);
        w.WriteEndObject();
        w.WriteEndObject();
      }
      w.WriteEndArray();
      w.WriteEndObject();
    }
    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  /// <summary>
  /// Run one `tools/call` and word its answer.
  ///
  /// The three catches are the THREE kinds of "no" this server can say, and they are deliberately not
  /// one kind:
  ///   * a malformed call never ran, so it is a JSON-RPC `invalidParams` — the transport is telling the
  ///     client its request was wrong;
  ///   * a tool that RAN and refused (no live session, an ambiguous breakpoint, a program that already
  ///     exited) is a RESULT with `isError`, because the model must read it and choose what to do next;
  ///   * a program that would not COMPILE is the same kind of result, carrying the compiler's own
  ///     diagnostics, because those are the answer rather than a failure to produce one.
  /// Anything else is a genuine fault and escapes to <see cref="Serve"/>, which says so as a 500.
  /// </summary>
  private static string CallTool(McpSession session, JsonElement message, string rawId) {
    if (!message.TryGetProperty("params", out var parameters)
        || parameters.ValueKind != JsonValueKind.Object)
      return RpcError(rawId, RpcCode.InvalidParams, "tools/call requires `params`");

    if (!parameters.TryGetProperty("name", out var nameElement)
        || nameElement.ValueKind != JsonValueKind.String)
      return RpcError(rawId, RpcCode.InvalidParams, "tools/call params need a string `name`");

    var name = nameElement.GetString()!;
    if (McpToolRegistry.Find(name) is not { } tool)
      return RpcError(rawId, RpcCode.MethodNotFound, $"unknown tool: {name}");

    // `arguments` is optional in the wire format. An absent one is an undefined element, in which every
    // lookup finds its key missing — exactly what "called with no arguments" means.
    parameters.TryGetProperty("arguments", out var arguments);

    try {
      return RpcResult(rawId, ToolCallResult(tool.Handler(session, arguments)));
    } catch (McpInvalidParamsException ex) {
      return RpcError(rawId, RpcCode.InvalidParams, $"{name}: {ex.Message}");
    } catch (DebuggerException ex) {
      return RpcResult(rawId, ToolCallResult(McpToolResult.Refusal(ex.Message)));
    } catch (Tools.McpBuildException ex) {
      return RpcResult(rawId, ToolCallResult(
        McpToolResult.Refusal($"the program did not compile:{Environment.NewLine}{ex.Message}")));
    } catch (IOException ex) {
      return RpcResult(rawId, ToolCallResult(McpToolResult.Refusal($"{name}: {ex.Message}")));
    } catch (UnauthorizedAccessException ex) {
      return RpcResult(rawId, ToolCallResult(McpToolResult.Refusal($"{name}: {ex.Message}")));
    }
  }

  /// The MCP shape of a tool's answer: content blocks, plus `isError` when the tool ran and refused.
  private static string ToolCallResult(McpToolResult result) {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer)) {
      w.WriteStartObject();
      w.WriteStartArray("content");
      w.WriteStartObject();
      w.WriteString("type", "text");
      w.WriteString("text", result.Text);
      w.WriteEndObject();
      w.WriteEndArray();
      if (result.IsError) w.WriteBoolean("isError", true);
      w.WriteEndObject();
    }
    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  // ---- Responses ----

  /// The JSON-RPC error codes this server uses. The range -32768..-32000 is the protocol's own.
  private enum RpcCode {
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,
  }

  private static string RpcResult(string rawId, string rawResult) =>
    $"{{\"jsonrpc\":\"2.0\",\"id\":{rawId},\"result\":{rawResult}}}";

  private static string RpcError(string rawId, RpcCode code, string message) {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer)) {
      w.WriteStartObject();
      w.WriteNumber("code", (int)code);
      w.WriteString("message", message);
      w.WriteEndObject();
    }
    return $"{{\"jsonrpc\":\"2.0\",\"id\":{rawId},\"error\":{Encoding.UTF8.GetString(buffer.ToArray())}}}";
  }

  private static string BuildInitializeJson() {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer)) {
      w.WriteStartObject();
      w.WriteString("protocolVersion", ProtocolVersion);
      w.WriteStartObject("capabilities");
      w.WriteStartObject("tools");
      w.WriteEndObject();
      w.WriteEndObject();
      w.WriteStartObject("serverInfo");
      w.WriteString("name", ServerName);
      w.WriteString("version", ServerVersion);
      w.WriteEndObject();
      w.WriteEndObject();
    }
    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  private static void RespondJson(HttpListenerContext context, HttpStatusCode status, string json,
      string? sessionId = null) {
    if (sessionId != null) context.Response.Headers[SessionHeader] = sessionId;
    Respond(context, status, "application/json", json);
  }

  private static void RespondText(HttpListenerContext context, HttpStatusCode status, string text) =>
    Respond(context, status, "text/plain; charset=utf-8", text);

  /// A notification or response the server accepted and owes no body for.
  private static void RespondAccepted(HttpListenerContext context) =>
    Respond(context, HttpStatusCode.Accepted, "text/plain; charset=utf-8", "");

  private static void Respond(HttpListenerContext context, HttpStatusCode status, string contentType,
      string body) {
    var bytes = Encoding.UTF8.GetBytes(body);
    context.Response.StatusCode = (int)status;
    context.Response.ContentType = contentType;
    context.Response.ContentLength64 = bytes.Length;
    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
  }

  private static void TryRespondInternalError(HttpListenerContext context) {
    try {
      RespondJson(context, HttpStatusCode.InternalServerError,
        RpcError("null", RpcCode.InternalError, "the server faulted while answering"));
    } catch (Exception) {
      // The response was already begun or the client is gone; the log line above is the record.
    }
  }

  // ---- Options ----

  private static bool TryParseOptions(string[] args, out int port, out TimeSpan idleTimeout,
      out int maxSessions) {
    port = DefaultPort;
    idleTimeout = DefaultIdleTimeout;
    maxSessions = DefaultMaxSessions;

    foreach (var arg in args) {
      if (arg.StartsWith(PortFlag, StringComparison.Ordinal)) {
        if (!TryParsePort(arg[PortFlag.Length..], out port)) {
          Console.Error.WriteLine($"maxon mcp: {PortFlag}<n> needs a TCP port (1-65535), got "
            + $"'{arg[PortFlag.Length..]}'.");
          return false;
        }
      } else if (arg.StartsWith(IdleTimeoutFlag, StringComparison.Ordinal)) {
        if (!PositiveSeconds.TryParse(arg[IdleTimeoutFlag.Length..], out idleTimeout)) {
          Console.Error.WriteLine($"maxon mcp: {IdleTimeoutFlag}<seconds> "
            + $"{PositiveSeconds.RequirementText}, got '{arg[IdleTimeoutFlag.Length..]}'.");
          return false;
        }
      } else if (arg.StartsWith(MaxSessionsFlag, StringComparison.Ordinal)) {
        if (!int.TryParse(arg[MaxSessionsFlag.Length..], out maxSessions) || maxSessions < 1) {
          Console.Error.WriteLine($"maxon mcp: {MaxSessionsFlag}<n> needs a positive count, got "
            + $"'{arg[MaxSessionsFlag.Length..]}'.");
          return false;
        }
      } else {
        Console.Error.WriteLine($"maxon mcp: unknown option '{arg}'.");
        return false;
      }
    }

    return true;
  }

  private static bool TryParsePort(string text, out int port) =>
    int.TryParse(text, out port) && port is > 0 and < 65536;
}
