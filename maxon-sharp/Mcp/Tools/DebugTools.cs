using System.Text;
using System.Text.Json;

namespace MaxonSharp.Mcp.Tools;

/// <summary>
/// The DEBUG family — the first family of `maxon mcp`, and a thin one on purpose.
///
/// ⭐ Every tool here does the same three things: compose ONE command line from its arguments, hand it to
/// <see cref="MaxonDebugRepl.RunCommand"/> — the same dispatcher `maxon debug --batch` uses — and return
/// the events it emitted. There is no stop renderer in this file, no breakpoint-outcome vocabulary, and
/// no command classifier: those exist once, in the driver, and this surface is a caller of them. The
/// command WORDS are read out of the driver's own command table (<see cref="MaxonDebugRepl.CommandWord"/>)
/// rather than spelled again here, so composing a line and parsing it back cannot drift apart.
///
/// What IS here is the part MCP adds: a session that outlives a request.
/// </summary>
internal static class DebugTools {

  /// <summary>
  /// The default bound on a wait for the target's next stop. Measured in SECONDS, deliberately unlike
  /// the CLI's ten minutes: a human at a REPL who set the wrong breakpoint sees nothing happen and hits
  /// Ctrl-C, while an agent's tool call has no such observer — it simply blocks the conversation. So the
  /// default here is short enough that a breakpoint which will never be hit comes back as an honest
  /// timeout event, and `stopTimeoutSeconds` raises it for a program that genuinely runs long.
  /// </summary>
  private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(20);

  /// The three stepping commands, named by the driver's own table so this list cannot come to offer a
  /// word the dispatcher does not know.
  private static readonly string[] StepKinds = [
    MaxonDebugRepl.CommandWord(MaxonDebugRepl.DebugCommand.Step),
    MaxonDebugRepl.CommandWord(MaxonDebugRepl.DebugCommand.Next),
    MaxonDebugRepl.CommandWord(MaxonDebugRepl.DebugCommand.Finish),
  ];

  /// The four per-green-thread actions, likewise. `debug_gt` is one tool rather than four for the same
  /// reason `debug_step` is one rather than three: the dispatcher already takes the word.
  private static readonly string[] GtActions = [
    MaxonDebugRepl.CommandWord(MaxonDebugRepl.DebugCommand.GtSelect),
    MaxonDebugRepl.CommandWord(MaxonDebugRepl.DebugCommand.GtBacktrace),
    MaxonDebugRepl.CommandWord(MaxonDebugRepl.DebugCommand.GtPark),
    MaxonDebugRepl.CommandWord(MaxonDebugRepl.DebugCommand.GtResume),
  ];

  public static IReadOnlyList<McpTool> Tools { get; } = [
    new("debug_start",
      "Build a Maxon program, launch it under the debugger stopped at entry, arm the given breakpoints, "
      + "and run to the first one. Returns the debugger events that produced — the breakpoint outcomes, "
      + "then either a `stop` (with its source window, backtrace and location) or the program's `exit` if "
      + "nothing stopped it. THE SESSION STAYS LIVE afterwards: call debug_locals / debug_step / "
      + "debug_eval / debug_continue against the same Mcp-Session-Id to keep inspecting the same parked "
      + "process, and debug_stop when done. A compile error comes back as the compiler's own diagnostics.",
      [
        McpSource.Arg("debug"),
        new("breakpoints", McpSchema.ArrayOf(
          "Breakpoints to arm before the program runs. Each is exactly what `break` takes: `<file>:<line>` "
          + "or a function name (matched exactly, then as Type.method, then as a leaf or prefix — an "
          + "ambiguous name is REPORTED with its candidates and NOT armed), optionally followed by "
          + "`if <local> <op> <literal>` for a condition evaluated inside the debuggee.",
          McpSchema.String("One breakpoint specification.")), Required: false),
        new("args", McpSchema.ArrayOf("Command-line arguments passed to the debuggee.",
          McpSchema.String("One argument.")), Required: false),
        new("env", McpSchema.StringMap(
          "Environment variables set in the DEBUGGEE. MAXON_MAX_PROCS=1 pins its green-thread scheduler "
          + "to one processor, which is what makes a concurrent program's run reproducible.",
          "The variable's value."), Required: false),
        new("stopTimeoutSeconds", McpSchema.Number(
          "How long to wait for the program to reach its next stop before reporting an honest timeout "
          + $"(default {DefaultStopTimeout.TotalSeconds}). Raise it for a program that legitimately runs "
          + "for a while before the breakpoint."), Required: false),
        new("stopOthers", McpSchema.Boolean(
          "What a stop does to the OTHER green threads: false (default) parks only the thread that "
          + "trapped, true holds every green thread for the duration of the stop. The hold is "
          + "COOPERATIVE, so a thread already on a processor keeps running until it next reaches the "
          + "scheduler, which `debug_threads` reports as 'pending' rather than 'held'."), Required: false),
        new("debugStream", McpSchema.Boolean(
          "Build the program with the DebugStream trace hooks (`maxon build --debugstream`), which is "
          + "what makes debug_trace able to return events rather than say it has none. Off by default "
          + "because the hooks cost real time in the debuggee. Note that this tool BUILDS the program, "
          + "so this setting — not whatever the binary on disk was last built with — decides what the "
          + "session gets."), Required: false),
      ],
      Start),

    new("debug_break",
      "Arm a breakpoint on the live session. `target` is `<file>:<line>` or a function name; the outcome "
      + "is reported honestly and distinctly — set, no-code (that line compiled to nothing), ambiguous "
      + "(with the candidate list, and NOTHING armed), or no-match (with a spelling suggestion).",
      [
        new("target", McpSchema.String("`<file>:<line>` or a function name."), Required: true),
        new("condition", McpSchema.String(
          "Optional `<local> <op> <literal>` evaluated INSIDE the debuggee on every hit, so a breakpoint "
          + "in a hot loop costs microseconds per miss rather than a round trip. Deliberately narrow: a "
          + "scalar stack local against an int or bool literal. A dotted path, a float, a String, or "
          + "local-vs-local is REFUSED and left unarmed rather than approximated."), Required: false),
      ],
      (session, args) => Run(session, MaxonDebugRepl.DebugCommand.Break,
        McpArgs.RequireString(args, "target")
          + MaxonDebugRepl.ConditionSuffix(McpArgs.OptionalString(args, "condition") ?? ""))),

    new("debug_clear",
      "Remove a breakpoint. `target` takes exactly what debug_break takes and resolves it exactly the "
      + "same way, so `clear` removes what the identical `break` armed. Removing one that was never "
      + "there is reported `not-set` rather than as a success — an agent that mistyped a target must not "
      + "be told it removed something.",
      [new("target", McpSchema.String(
        "`<file>:<line>` or a function name — the same target you armed. No condition: a breakpoint is "
        + "removed whole, conditional or not."), Required: true)],
      (session, args) => Run(session, MaxonDebugRepl.DebugCommand.Clear,
        McpArgs.RequireString(args, "target"))),

    new("debug_continue",
      "Resume the debuggee and run to the next stop. Returns a `stop` event, or the program's `exit` / "
      + "`crash` if it finished — in which case the session is closed and the process reaped in the same "
      + "call.",
      [],
      (session, _) => Run(session, MaxonDebugRepl.DebugCommand.Continue)),

    new("debug_step",
      "Step one SOURCE LINE. `step` descends into a call; `next` runs a call to completion and stops on "
      + "the following line; `finish` runs to the caller. All three stop early at an intervening user "
      + "breakpoint, and report honestly when they cannot be done (a frameless leaf has no caller frame).",
      [new("kind", McpSchema.StringEnum("Which step.", StepKinds), Required: true)],
      (session, args) => Run(session, McpArgs.RequireChoice(args, "kind", StepKinds))),

    new("debug_until",
      "Run until a given LINE of the current function is reached, or until that function returns — the "
      + "loop escape: it leaves a loop you are stepping through without stepping every remaining "
      + "iteration. A line with no code in the current function is refused rather than approximated.",
      [new("line", McpSchema.Integer("A 1-based source line in the currently stopped function."),
        Required: true)],
      (session, args) => Run(session, MaxonDebugRepl.DebugCommand.Until,
        McpArgs.RequireInt(args, "line").ToString(System.Globalization.CultureInfo.InvariantCulture))),

    new("debug_locals",
      "The named locals of the stopped frame, as type-aware value trees: a String as text plus length, a "
      + "struct as a named-field subtree, an enum by the case its runtime discriminant actually holds. "
      + "Refuses honestly when the program is not stopped.",
      [],
      (session, _) => Run(session, MaxonDebugRepl.DebugCommand.Locals)),

    new("debug_eval",
      "Evaluate a value PATH against the stopped frame — `person`, `person.home.name`, `arr[2]` — and "
      + "return it as the same value tree debug_locals produces. It reads memory; it does not call "
      + "functions or evaluate arithmetic.",
      [new("expr", McpSchema.String("A local name, optionally followed by `.field` and `[index]` steps."),
        Required: true)],
      (session, args) => Run(session, MaxonDebugRepl.DebugCommand.Print,
        McpArgs.RequireString(args, "expr"))),

    new("debug_backtrace",
      "The stopped thread's call stack, each frame symbolized to function and file:line. While a green "
      + "thread is selected with debug_gt it describes THAT thread instead.",
      [],
      (session, _) => Run(session, MaxonDebugRepl.DebugCommand.Backtrace)),

    new("debug_threads",
      "Every green thread the runtime knows about: its id, entry function, scheduler status, whether it "
      + "is on a processor, whether the debugger is holding it, and its top frame. Ids are RE-MINTED on "
      + "every resume, so list again after continuing rather than reusing an id you read earlier.",
      [],
      (session, _) => Run(session, MaxonDebugRepl.DebugCommand.Threads)),

    new("debug_gt",
      "Act on ONE green thread by id (from debug_threads). `gt` selects it, so backtrace/locals/eval "
      + "describe it until the target resumes; `gt-backtrace` walks its stack without selecting it; "
      + "`gt-park` stops the scheduler from running it and `gt-resume` releases it. A thread executing on "
      + "a processor cannot be walked or parked immediately and says so rather than being approximated.",
      [
        new("action", McpSchema.StringEnum("What to do with the thread.", GtActions), Required: true),
        new("id", McpSchema.Integer("The green-thread id from the most recent debug_threads."),
          Required: true),
      ],
      (session, args) => Run(session,
        $"{McpArgs.RequireChoice(args, "action", GtActions)} {McpArgs.RequireInt(args, "id")}")),

    new("debug_trace",
      "The DebugStream events the debuggee recorded since the previous stop — allocations, refcounts, "
      + "frees — which is what correlates a stop with what the program was DOING to get there. Only a "
      + "binary built with --debugstream emits them, which is what debug_start's `debugStream: true` "
      + "asks for; a binary without them says so rather than returning an empty list, because 'no "
      + "hooks' and 'nothing happened' are different answers.",
      [],
      (session, _) => Run(session, MaxonDebugRepl.DebugCommand.Trace)),

    new("debug_stop",
      "Terminate the debuggee and close the session, reporting what became of it. Every session owns a "
      + "real process, so call this when finished rather than abandoning it — though a session that goes "
      + "silent is reaped on the server's idle timeout, and every session is reaped on shutdown.",
      [],
      (session, _) => {
        var debug = Live(session);
        return Result(debug, debug.Stop());
      }),
  ];

  // ---- Handlers ----

  private static McpToolResult Start(McpSession session, JsonElement args) {
    if (session.Debug is { EndedReason: null } live)
      throw new DebuggerException(
        $"this session already has a live debuggee ('{live.ExePath}'). Call debug_stop first — a second "
        + "start would abandon a running process. (A different Mcp-Session-Id gets its own debuggee.)");

    // A session whose program already finished is REPLACED rather than refused: the agent has read its
    // outcome, and holding the corpse would only make it invent a second connection to get on with.
    session.Debug?.Dispose();
    session.Debug = null;

    var debug = McpDebugSession.Start(
      DebugBuild.Compile(McpSource.Resolve(session, args),
        McpArgs.OptionalBool(args, "debugStream") ?? false, coverage: false),
      McpArgs.OptionalStringArray(args, "args"),
      McpArgs.OptionalStringMap(args, "env"),
      StopTimeoutOf(args),
      McpArgs.OptionalBool(args, "stopOthers") ?? false);
    session.Debug = debug;

    // Arm every breakpoint, then run — through the ONE dispatcher, so a breakpoint set here is reported
    // by exactly the events `maxon debug --batch` would have emitted for the same script.
    var commands = McpArgs.OptionalStringArray(args, "breakpoints")
      .Select(spec => $"{MaxonDebugRepl.CommandWord(MaxonDebugRepl.DebugCommand.Break)} {spec}")
      .Append(MaxonDebugRepl.CommandWord(MaxonDebugRepl.DebugCommand.Run))
      .ToArray();
    return Result(debug, debug.Run(commands));
  }

  /// Run a command that takes no argument.
  private static McpToolResult Run(McpSession session, MaxonDebugRepl.DebugCommand command) =>
    Run(session, MaxonDebugRepl.CommandWord(command));

  /// Run a command with an argument, composed the ONE way: the table's word, then the rest of the line.
  private static McpToolResult Run(McpSession session, MaxonDebugRepl.DebugCommand command, string rest) =>
    Run(session, $"{MaxonDebugRepl.CommandWord(command)} {rest}");

  private static McpToolResult Run(McpSession session, string commandLine) {
    var debug = Live(session);
    return Result(debug, debug.Run(commandLine));
  }

  /// <summary>
  /// This session's live debuggee, or a refusal that says WHICH of the two things is missing. "There is
  /// no session" and "the session you had has finished, and here is what became of it" send an agent
  /// somewhere different, so they are never flattened into one message — and neither is ever answered
  /// with an empty result, which would read as "the program has no locals".
  /// </summary>
  private static McpDebugSession Live(McpSession session) {
    if (session.Debug is not { } debug)
      throw new DebuggerException(
        "no debug session on this connection — call debug_start first (and keep sending the same "
        + "Mcp-Session-Id, which is what the live debuggee hangs on).");

    if (debug.EndedReason is { } ended)
      throw new DebuggerException($"{ended}; there is nothing left to inspect. Call debug_start again.");

    return debug;
  }

  /// <summary>
  /// A tool's answer: the events the command emitted, verbatim, plus anything the debuggee printed.
  ///
  /// The events are SPLICED as raw JSON rather than re-rendered — they are already the driver's own
  /// single-sourced objects, and re-encoding them as strings would hand an agent JSON to parse twice.
  /// `output` is present only when there is some, so a session whose program prints nothing emits
  /// exactly the shape it would have without this field.
  /// </summary>
  private static McpToolResult Result(McpDebugSession debug, IReadOnlyList<string> events) {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false })) {
      w.WriteStartObject();
      w.WriteString("exe", debug.ExePath);

      w.WriteStartArray("events");
      foreach (var line in events) w.WriteRawValue(line);
      w.WriteEndArray();

      var output = debug.DrainOutput();
      if (output.Length > 0) w.WriteString("output", output);

      w.WriteEndObject();
    }
    return McpToolResult.Json(Encoding.UTF8.GetString(buffer.ToArray()));
  }

  private static TimeSpan StopTimeoutOf(JsonElement args) {
    if (McpArgs.OptionalNumber(args, "stopTimeoutSeconds") is not { } seconds) return DefaultStopTimeout;

    // Refused rather than clamped, through the SAME rule `--stop-timeout=` is refused by: a deadline
    // that is non-positive — or that merely ROUNDS to zero, which is how the two used to disagree —
    // times out every wait before the target could possibly stop, and would be reported as a setting
    // rather than as the broken one it is.
    if (!PositiveSeconds.TryFrom(seconds, out var stopTimeout))
      throw new McpInvalidParamsException(
        $"`stopTimeoutSeconds` {PositiveSeconds.RequirementText}, got {seconds}");

    return stopTimeout;
  }

}
