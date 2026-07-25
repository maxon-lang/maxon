using System.Text;
using System.Text.Json;

namespace MaxonSharp;

/// <summary>
/// The rich guided REPL (P3c) — the first surface over the <see cref="MaxonDebugger"/> engine, and the
/// deliberately-more-ergonomic-than-gdb face the design calls for. Two front-ends share ONE engine and
/// ONE set of renderers (<see cref="RenderStopText"/> / <see cref="RenderStopJson"/>): an interactive
/// line REPL and a <c>--batch --commands=…</c> mode that emits a JSON object per event. The renderers
/// are the reusable primitives P4/DAP/TUI inherit.
///
/// CORE for P3c: <c>break file:line</c> (b), <c>run</c> (r), <c>continue</c> (c), <c>backtrace</c>
/// (bt), <c>quit</c> (q), plus the auto source-context window shown on every stop — the current line
/// marked, a few lines around it, the location, and a symbolized backtrace. Stepping, value-printing,
/// conditional breakpoints, and per-GT control are P4.
/// </summary>
internal static class MaxonDebugRepl {

  /// Source lines shown on each side of the stopped line in the auto-context window.
  private const int SourceWindowRadius = 3;

  /// A rendered source line: its 1-based number, its text, and whether it is the stopped line.
  private readonly record struct SourceLine(uint Number, string Text, bool IsCurrent);

  /// Everything the two renderers need for one stop: the raw stop (its FP/PC, which value inspection
  /// reads locals against), the symbolized location, the source window (empty when the file cannot be
  /// read), and the symbolized backtrace (carrying its own status).
  private readonly record struct StopReport(
    MaxonDebugger.StopInfo Stop,
    string ReasonText,
    MaxonDebugger.SymLocation Location,
    string SourcePath,
    IReadOnlyList<SourceLine> Source,
    MaxonDebugger.BacktraceResult Backtrace);

  // ---- Interactive REPL ----

  public static int RunInteractive(string exePath, IReadOnlyList<string> targetArgs, TimeSpan? stopTimeout) {
    TryEnableUtf8();
    var sidecar = LoadSidecar(exePath);
    if (sidecar == null) return 1;

    MaxonDebugger dbg;
    try {
      dbg = MaxonDebugger.Attach(exePath, targetArgs, sidecar, stopTimeout: stopTimeout);
    } catch (DebuggerException ex) {
      Console.Error.WriteLine($"maxon debug: {ex.Message}");
      return 1;
    }

    using (dbg) {
      if (!dbg.WaitForAgentAlive()) {
        Console.Error.WriteLine("maxon debug: the debug agent never attached (is MAXON_DEBUG honored by this build?).");
        return 1;
      }

      Console.WriteLine($"Debugging {Path.GetFileName(exePath)} — stopped at entry. Type 'help' for commands.");
      var session = new Session(dbg, exePath);
      return session.Loop();
    }
  }

  /// The interactive state: whether the target is stopped (and where), so the prompt is location-aware
  /// and `backtrace`/`continue` know there is a frame to act on.
  private sealed class Session(MaxonDebugger dbg, string exePath) {
    private bool _finished;
    private StopReport? _stop;

    /// The interactive line editor: tab-completion, persistent history, Ctrl-R reverse-search, with a
    /// plain-ReadLine fallback when stdin is not a TTY. One per session so its in-memory history spans the
    /// session and is written back to the shared history file.
    private readonly LineEditor _editor = new(HistoryFilePath());

    /// The one wording of "you must be stopped first", shared by every command that reads the parked
    /// frame (backtrace / print / locals / step / next / finish / until).
    private const string NotStoppedText = "Not stopped — run to a breakpoint first.";

    /// The one wording of a breakpoint the agent did not confirm, shared by the file:line and function
    /// break renderers so they cannot drift. It names both causes because the driver genuinely cannot
    /// tell them apart from the outcome word alone — and the load-bearing half of the sentence is that
    /// the breakpoint is NOT armed, which the agent used to leave the driver to guess wrong.
    private static readonly string BreakUnacknowledgedText =
      "The agent did not confirm the breakpoint, so it is NOT set (the target may have exited, or its "
      + $"{Compiler.Ir.Runtime.RuntimeEmitter.DbgMaxBreakpoints}-breakpoint table is full).";

    public int Loop() {
      while (!_finished) {
        var line = _editor.ReadLine(Prompt(), BuildCompletionContext);
        // EOF (Ctrl-D / piped input drained) ends the session exactly as `quit` does; EndSession
        // below is the ONE place that decides what becomes of a target still parked at that point.
        if (line == null) break;
        Execute(line.Trim());
      }
      dbg.EndSession(MaxonDebugger.SessionEnd.Immediate);
      return 0;
    }

    /// The completion pools for the current state: commands + the sidecar's functions/files always, and the
    /// stopped frame's locals when there is one. Built lazily (only when Tab is pressed) via the editor's
    /// context callback, so an idle prompt does not walk the sidecar each keystroke.
    private CompletionContext BuildCompletionContext() {
      IReadOnlyList<string> locals = _stop is { } report ? dbg.LocalNames(report.Stop.PcOffset) : [];
      return new CompletionContext(CommandWords, dbg.FunctionNames(), dbg.FileNames(), locals, ArgTargetFor);
    }

    private string Prompt() {
      if (_stop is { Location: var loc } && loc.HasFunction) {
        var where = loc.HasLine ? $"{loc.Function} {loc.Line}" : loc.Function;
        return $"(maxon:{where}){PromptGlyph} ";
      }
      return $"(maxon){PromptGlyph} ";
    }

    private void Execute(string input) {
      var (command, word, rest) = ParseCommand(input);
      switch (command) {
        case DebugCommand.Empty:
          break;
        case DebugCommand.Break:
          DoBreak(rest);
          break;
        case DebugCommand.Run:
          DoContinue(isRun: true);
          break;
        case DebugCommand.Continue:
          DoContinue(isRun: false);
          break;
        case DebugCommand.Step:
          DoStepCommand(dbg.StepInto);
          break;
        case DebugCommand.Next:
          DoStepCommand(dbg.StepOver);
          break;
        case DebugCommand.Finish:
          DoStepCommand(dbg.Finish);
          break;
        case DebugCommand.Until:
          DoUntil(rest);
          break;
        case DebugCommand.Backtrace:
          DoBacktrace();
          break;
        case DebugCommand.Print:
          DoPrint(rest);
          break;
        case DebugCommand.Locals:
          DoLocals();
          break;
        case DebugCommand.Help:
          PrintHelp();
          break;
        case DebugCommand.Quit:
          _finished = true;
          break;
        case DebugCommand.Unknown:
          Console.Out.WriteLine($"Unknown command '{word}'.{DidYouMeanCommandSuffix(word)} Type 'help' for the command list.");
          break;
        default:
          throw new InvalidOperationException($"Unhandled command {command}");
      }
    }

    private void DoBreak(string arg) {
      if (arg.Length == 0) {
        Console.Out.WriteLine($"Usage: break <file>:<line>   |   break <function>   [{BreakConditionUsage}]");
        return;
      }
      var (target, condition) = SplitBreakCondition(arg);
      if (TryParseFileLine(target, out var file, out var lineNo)) {
        RenderFileLineBreak(dbg.SetBreakpoint(file, lineNo, condition), file, lineNo, condition);
      } else {
        RenderFunctionBreak(dbg.SetBreakpointAtFunction(target, condition), target, condition);
      }
    }

    private static void RenderFileLineBreak(MaxonDebugger.BreakResult r, string file, uint lineNo,
        string condition) {
      // A condition refusal is reported ahead of the outcome switch because its wording is shared by all
      // four break renderers (text/JSON x file:line/function) — the switches below stay about LOCATION.
      if (ConditionRefusalText(r) is { } refusal) {
        Console.Out.WriteLine($"{refusal} — breakpoint not set.");
        return;
      }

      switch (r.Kind) {
        case MaxonDebugger.BreakKind.NoCode:
          Console.Out.WriteLine($"No code at {file}:{lineNo} (blank line, or no statement there).");
          break;
        case MaxonDebugger.BreakKind.Unacknowledged:
          Console.Out.WriteLine(BreakUnacknowledgedText);
          break;
        case MaxonDebugger.BreakKind.Set:
          var inFn = r.Location.HasFunction ? $" in {r.Location.Function}" : "";
          Console.Out.WriteLine(
            $"Breakpoint set at {file}:{lineNo}{inFn} (0x{r.Offset:x}){ConditionSuffix(condition)}.");
          break;
        default:
          throw new InvalidOperationException($"Unhandled file:line break outcome {r.Kind}");
      }
    }

    private static void RenderFunctionBreak(MaxonDebugger.BreakResult r, string query, string condition) {
      if (ConditionRefusalText(r) is { } refusal) {
        Console.Out.WriteLine($"{refusal} — breakpoint not set.");
        return;
      }

      switch (r.Kind) {
        case MaxonDebugger.BreakKind.Set:
          var at = r.Location.HasLine ? $" ({r.Location.File}:{r.Location.Line})" : "";
          var fn = r.Location.HasFunction ? r.Location.Function : query;
          Console.Out.WriteLine($"Breakpoint set at {fn}{at} (0x{r.Offset:x}){ConditionSuffix(condition)}.");
          break;
        case MaxonDebugger.BreakKind.Unacknowledged:
          Console.Out.WriteLine(BreakUnacknowledgedText);
          break;
        case MaxonDebugger.BreakKind.Ambiguous:
          Console.Out.WriteLine($"'{query}' is ambiguous — candidates: {string.Join(", ", r.Candidates)}. "
            + "Qualify it (e.g. Type.method).");
          break;
        case MaxonDebugger.BreakKind.NoMatch:
          Console.Out.WriteLine($"No function matches '{query}'.{DidYouMeanSuffix(r.Suggestion)}");
          break;
        default:
          throw new InvalidOperationException($"Unhandled function break outcome {r.Kind}");
      }
    }

    private void DoContinue(bool isRun) {
      if (_finished) { Console.Out.WriteLine("The program has already exited."); return; }

      if (!dbg.Continue()) {
        Console.Out.WriteLine($"{ContinueUnackedText}.");
        return;
      }

      var wait = dbg.WaitForStop();
      switch (wait.Status) {
        case MaxonDebugger.StopWaitStatus.Stopped:
          var report = BuildStopReport(dbg, exePath, wait.Stop);
          _stop = report;
          RenderStopText(report, Console.Out);
          break;

        case MaxonDebugger.StopWaitStatus.Exited:
          _stop = null;
          _finished = true;
          dbg.EndSession(MaxonDebugger.SessionEnd.Immediate);
          Console.Out.WriteLine(isRun
            ? $"Program exited with code {dbg.OutcomeText}."
            : $"Program continued to completion; exit code {dbg.OutcomeText}.");
          break;

        case MaxonDebugger.StopWaitStatus.TimedOut:
          // The target is ALIVE and has simply not reached a stop yet, so there is no frame to inspect
          // and — the defect this replaced — no exit to report. It is left RUNNING rather than killed,
          // because a human may legitimately want to keep waiting: the stop stays OUTSTANDING, so a
          // further `continue` resumes the wait instead of posting a second one. (Posting again would be
          // acked the moment the target parked and would resume it, and the driver would then report the
          // stop it had just swallowed — a location the target had already left.) `quit`, EOF and Dispose
          // all still guarantee it cannot outlive the session.
          _stop = null;
          Console.Out.WriteLine($"{TimeoutText(WaitingForContinueStopText, dbg.StopTimeoutText)} The target "
            + $"is still running — 'continue' waits again, 'quit' ends the session. "
            + MaxonDebugger.RaiseStopTimeoutText);
          break;

        default:
          throw new InvalidOperationException($"Unhandled stop-wait status {wait.Status}");
      }
    }

    private void DoBacktrace() {
      if (_stop is null) {
        Console.Out.WriteLine(NotStoppedText);
        return;
      }
      // The target is still parked at the same stop, so a fresh request is authoritative.
      RenderBacktraceText(dbg.Backtrace(), Console.Out);
    }

    private void DoPrint(string rest) {
      if (_stop is not { } report) {
        Console.Out.WriteLine(NotStoppedText);
        return;
      }
      if (rest.Length == 0) {
        Console.Out.WriteLine("Usage: print <expr>   (e.g. print person.home.name)");
        return;
      }
      if (MakeValueRenderer(dbg, out var reason) is not { } renderer) {
        Console.Out.WriteLine($"{reason}.");
        return;
      }
      try {
        RenderValueText(renderer.Evaluate(report.Stop, rest), Console.Out);
      } catch (DebuggerException ex) {
        Console.Out.WriteLine($"print: {ex.Message}.");
      }
    }

    private void DoLocals() {
      if (_stop is not { } report) {
        Console.Out.WriteLine(NotStoppedText);
        return;
      }
      if (MakeValueRenderer(dbg, out var reason) is not { } renderer) {
        Console.Out.WriteLine($"{reason}.");
        return;
      }
      try {
        var values = renderer.Locals(report.Stop);
        if (values.Count == 0) {
          Console.Out.WriteLine("  (no named locals with a stack home here)");
          return;
        }
        Console.Out.WriteLine("locals:");
        foreach (var v in values) RenderValueText(v, Console.Out);
      } catch (DebuggerException ex) {
        Console.Out.WriteLine($"locals: {ex.Message}.");
      }
    }

    /// Run a step op that needs the current parked frame (step / next / finish) and render its outcome.
    private void DoStepCommand(Func<MaxonDebugger.StopInfo, MaxonDebugger.StepOutcome> op) {
      if (_stop is not { } report) {
        Console.Out.WriteLine(NotStoppedText);
        return;
      }
      ApplyStepOutcome(op(report.Stop));
    }

    private void DoUntil(string rest) {
      if (_stop is not { } report) {
        Console.Out.WriteLine(NotStoppedText);
        return;
      }
      if (!uint.TryParse(rest.Trim(), out var line) || line == 0) {
        Console.Out.WriteLine("Usage: until <line>");
        return;
      }
      ApplyStepOutcome(dbg.Until(report.Stop, line));
    }

    /// Render a step outcome the same way every step command does: a Stopped auto-renders the new stop
    /// (source window + location + backtrace, reusing the P3c/P4a renderer) and becomes the new location;
    /// an Exited closes the session; anything else is a one-line reason.
    private void ApplyStepOutcome(MaxonDebugger.StepOutcome outcome) {
      switch (outcome.Kind) {
        case MaxonDebugger.StepOutcomeKind.Stopped:
          var report = BuildStopReport(dbg, exePath, outcome.Stop);
          _stop = report;
          RenderStopText(report, Console.Out);
          break;
        case MaxonDebugger.StepOutcomeKind.Exited:
          _stop = null;
          _finished = true;
          dbg.EndSession(MaxonDebugger.SessionEnd.Immediate);
          Console.Out.WriteLine($"Program exited with code {dbg.OutcomeText}.");
          break;
        case MaxonDebugger.StepOutcomeKind.TimedOut:
          // Intercepted here rather than worded by StepUnavailableReason, because the honest message must
          // state the DEADLINE, which only the live session knows.
          //
          // Unlike a timed-out continue, this ENDS the session. A step runner leaves temp breakpoints
          // patched in the target, and they can only be cleared through a PARKED one — so a target still
          // running here carries traps the driver can no longer reconcile, and letting the session go on
          // would eventually report a stop at a breakpoint the user never set. Ending it is what makes
          // ReleaseTempBreakpoints' skip safe: the patches — and any user condition it suspended to make
          // its own stop fire — die with the target, right here.
          _stop = null;
          _finished = true;
          Console.Out.WriteLine($"{TimeoutText(WaitingForStepStopText, dbg.StopTimeoutText)} The step left "
            + $"the target running under temporary breakpoints that can no longer be cleared, so the "
            + $"session has ended and the target was stopped. {MaxonDebugger.RaiseStopTimeoutText}");
          dbg.EndSession(MaxonDebugger.SessionEnd.Immediate);
          break;
        default:
          Console.Out.WriteLine($"{StepUnavailableReason(outcome.Kind)}.");
          break;
      }
    }

    private static void PrintHelp() {
      Console.Out.WriteLine("Commands:");
      Console.Out.WriteLine("  break <file>:<line>   (b)   set a breakpoint at a source line");
      Console.Out.WriteLine("  break <function>            set a breakpoint at a function's entry (fuzzy: leaf/prefix/typo)");
      Console.Out.WriteLine($"  break <target> {BreakConditionUsage}");
      Console.Out.WriteLine("                              stop only when a scalar local compares true (e.g. break f.maxon:9 if i == 3)");
      Console.Out.WriteLine("  run                   (r)   start the program (continue from entry)");
      Console.Out.WriteLine("  continue              (c)   resume from a breakpoint");
      Console.Out.WriteLine("  step                  (s)   step into: advance one statement, entering calls");
      Console.Out.WriteLine("  next                  (n)   step over: advance one statement, running calls to completion");
      Console.Out.WriteLine("  finish                      run until the current function returns");
      Console.Out.WriteLine("  until <line>          (u)   run until a line in the current function (or it returns)");
      Console.Out.WriteLine("  backtrace             (bt)  show the stopped call stack");
      Console.Out.WriteLine("  locals                      list the stopped function's locals with values");
      Console.Out.WriteLine("  print <expr>          (p)   render a value; dotted paths navigate (person.home.name)");
      Console.Out.WriteLine("  quit                  (q)   end the session");
      Console.Out.WriteLine("On every stop the source line is shown with a → marker, plus a symbolized backtrace.");
    }
  }

  // ---- Batch / JSON ----

  /// <summary>
  /// The batch session's mutable state, threaded through the command dispatch. It replaces the pair of
  /// `ref` parameters every batch command used to carry: a THIRD fact — whether a wait TIMED OUT, which
  /// decides the driver's own exit code — is exactly the addition that made a growing `ref` list the
  /// wrong shape for it.
  /// </summary>
  private sealed class BatchSession {
    /// True once the session can issue no further commands: the target exited, was quit, or timed out.
    public bool Finished;

    /// The last stop the target reached, so a later `print`/`locals` knows which parked frame to read.
    public MaxonDebugger.StopInfo? CurrentStop;

    /// True when the target did NOT run to completion — a wait that hit the deadline, a continue the
    /// agent never acked, or the drain cap. The driver exits NONZERO for all three: CI must not read
    /// any of them as a pass, and a missed breakpoint least of all.
    public bool Incomplete;
  }

  public static int RunBatch(string exePath, IReadOnlyList<string> targetArgs, string commandsSpec,
      TimeSpan? stopTimeout) {
    TryEnableUtf8();
    var sidecar = LoadSidecar(exePath);
    if (sidecar == null) { EmitError("no debug info found for the target"); return 1; }

    if (!TryLoadCommands(commandsSpec, out var commands, out var loadError)) {
      EmitError(loadError);
      return 1;
    }

    MaxonDebugger dbg;
    try {
      // Target stdout -> the driver's STDERR so this mode's JSON stream on stdout stays clean.
      dbg = MaxonDebugger.Attach(exePath, targetArgs, sidecar, targetStdout: Console.Error,
        stopTimeout: stopTimeout);
    } catch (DebuggerException ex) {
      EmitError(ex.Message);
      return 1;
    }

    using (dbg) {
      if (!dbg.WaitForAgentAlive()) {
        EmitError("the debug agent never attached (is MAXON_DEBUG honored by this build?)");
        return 1;
      }

      var session = new BatchSession();
      foreach (var command in commands) {
        if (session.Finished) break;
        RunBatchCommand(dbg, exePath, command, session);
      }

      // Drain any parked state so the target is never left spinning in the stop-the-world loop.
      DrainToExit(dbg, session);

      // Release the target and drain its stdio BEFORE reporting: a session that timed out (or was quit)
      // leaves a LIVE debuggee, and the exit event must describe what actually became of it rather than
      // guess. This is also what unblocks the stdio join, which cannot complete while that target lives.
      dbg.EndSession(MaxonDebugger.SessionEnd.Immediate);
      EmitExit(dbg);
      return session.Incomplete ? 1 : 0;
    }
  }

  private static void RunBatchCommand(MaxonDebugger dbg, string exePath, string commandLine,
      BatchSession session) {
    var (command, word, rest) = ParseCommand(commandLine);
    switch (command) {
      case DebugCommand.Empty:
        break;
      case DebugCommand.Break:
        BatchBreak(dbg, rest);
        break;
      // Run and Continue are the same mechanism; the interactive prompt distinguishes them, batch has
      // no prompt so both post continue and await the next event.
      case DebugCommand.Run:
      case DebugCommand.Continue:
        BatchContinue(dbg, exePath, session);
        break;
      case DebugCommand.Step:
        BatchStepCommand(dbg, exePath, dbg.StepInto, session);
        break;
      case DebugCommand.Next:
        BatchStepCommand(dbg, exePath, dbg.StepOver, session);
        break;
      case DebugCommand.Finish:
        BatchStepCommand(dbg, exePath, dbg.Finish, session);
        break;
      case DebugCommand.Until:
        BatchUntil(dbg, exePath, rest, session);
        break;
      case DebugCommand.Backtrace:
        EmitBacktrace(dbg.Backtrace());
        break;
      case DebugCommand.Locals:
        BatchLocals(dbg, session.CurrentStop);
        break;
      case DebugCommand.Print:
        BatchPrint(dbg, rest, session.CurrentStop);
        break;
      case DebugCommand.Quit:
        session.Finished = true;
        break;
      case DebugCommand.Help:
        EmitError("'help' is interactive-only; not available in --batch");
        break;
      case DebugCommand.Unknown:
        EmitError($"unknown command '{word}'{DidYouMeanCommandSuffix(word)}");
        break;
      default:
        throw new InvalidOperationException($"Unhandled command {command}");
    }
  }

  private static void BatchBreak(MaxonDebugger dbg, string arg) {
    if (arg.Length == 0) { EmitError("break needs <file>:<line> or a function name"); return; }
    var (target, condition) = SplitBreakCondition(arg);
    if (TryParseFileLine(target, out var file, out var lineNo)) {
      BatchBreakFileLine(dbg.SetBreakpoint(file, lineNo, condition), file, lineNo, condition);
    } else {
      BatchBreakFunction(dbg.SetBreakpointAtFunction(target, condition), target, condition);
    }
  }

  private static void BatchBreakFileLine(MaxonDebugger.BreakResult r, string file, uint lineNo,
      string condition) {
    WriteEvent(w => {
      w.WriteString("event", "breakpoint");
      w.WriteString("action", BreakActionName(r.Kind));
      w.WriteString("file", file);
      w.WriteNumber("line", lineNo);
      if (ConditionRefusalText(r) is { } refusal) {
        w.WriteString("reason", refusal);
        return;
      }
      if (r.Kind != MaxonDebugger.BreakKind.NoCode) {
        w.WriteString("offset", HexOffset(r.Offset));
        if (r.Location.HasFunction) w.WriteString("function", r.Location.Function);
        if (condition.Length > 0) w.WriteString("condition", condition);
      }
    });
  }

  private static void BatchBreakFunction(MaxonDebugger.BreakResult r, string query, string condition) =>
      WriteEvent(w => {
    w.WriteString("event", "breakpoint");
    w.WriteString("action", BreakActionName(r.Kind));
    if (ConditionRefusalText(r) is { } refusal) {
      w.WriteString("query", query);
      w.WriteString("reason", refusal);
      return;
    }
    switch (r.Kind) {
      case MaxonDebugger.BreakKind.Set:
      case MaxonDebugger.BreakKind.Unacknowledged:
        if (r.Location.HasFunction) w.WriteString("function", r.Location.Function);
        w.WriteString("offset", HexOffset(r.Offset));
        if (r.Location.HasLine) {
          w.WriteString("file", r.Location.File);
          w.WriteNumber("line", r.Location.Line);
        }
        if (condition.Length > 0) w.WriteString("condition", condition);
        break;
      case MaxonDebugger.BreakKind.Ambiguous:
        w.WriteString("query", query);
        w.WriteStartArray("candidates");
        foreach (var c in r.Candidates) w.WriteStringValue(c);
        w.WriteEndArray();
        break;
      case MaxonDebugger.BreakKind.NoMatch:
        w.WriteString("query", query);
        if (r.Suggestion.Length > 0) w.WriteString("suggestion", r.Suggestion);
        break;
      default:
        throw new InvalidOperationException($"Unhandled function break outcome {r.Kind}");
    }
  });

  private static void BatchContinue(MaxonDebugger dbg, string exePath, BatchSession session) {
    if (!dbg.Continue()) { EmitError(ContinueUnackedText); return; }

    var wait = dbg.WaitForStop();
    switch (wait.Status) {
      case MaxonDebugger.StopWaitStatus.Stopped:
        session.CurrentStop = wait.Stop;
        EmitStop(BuildStopReport(dbg, exePath, wait.Stop));
        break;
      case MaxonDebugger.StopWaitStatus.Exited:
        // Ran to completion: no parked frame to inspect any more.
        session.CurrentStop = null;
        session.Finished = true;
        break;
      case MaxonDebugger.StopWaitStatus.TimedOut:
        // The target is ALIVE and has simply not reached its breakpoint yet. Reporting that as an exit
        // is the defect this replaced — it made a missed breakpoint indistinguishable from a clean run.
        EmitTimeout(WaitingForContinueStopText, dbg);
        EndIncompleteSession(session);
        break;
      default:
        throw new InvalidOperationException($"Unhandled stop-wait status {wait.Status}");
    }
  }

  /// The batch face's wording of "you must be stopped first", shared by locals / print / step / next /
  /// finish / until so they cannot drift.
  private const string NotStoppedBatchText = "not stopped — run to a breakpoint first";

  /// The one wording of a continue the agent never acknowledged, shared by the interactive and batch
  /// continue paths and by the drain loop.
  private const string ContinueUnackedText = "the agent did not acknowledge continue";

  /// Run a step op that needs the current parked frame (step / next / finish) and emit its outcome.
  private static void BatchStepCommand(MaxonDebugger dbg, string exePath,
      Func<MaxonDebugger.StopInfo, MaxonDebugger.StepOutcome> op, BatchSession session) {
    if (session.CurrentStop is not { } stop) { EmitError(NotStoppedBatchText); return; }
    ApplyBatchStepOutcome(dbg, exePath, op(stop), session);
  }

  private static void BatchUntil(MaxonDebugger dbg, string exePath, string rest, BatchSession session) {
    if (session.CurrentStop is not { } stop) { EmitError(NotStoppedBatchText); return; }
    if (!uint.TryParse(rest.Trim(), out var line) || line == 0) { EmitError("until needs a line number"); return; }
    ApplyBatchStepOutcome(dbg, exePath, dbg.Until(stop, line), session);
  }

  /// Emit one step outcome the same way every batch step command does: a Stopped emits the same
  /// `{event:"stop",…}` shape a breakpoint stop does (so a consumer parses steps and breakpoints
  /// identically) and becomes the new parked frame; an Exited ends the run; a TimedOut ends it through
  /// the ONE timeout event; anything else is an error event with the shared reason.
  private static void ApplyBatchStepOutcome(MaxonDebugger dbg, string exePath,
      MaxonDebugger.StepOutcome outcome, BatchSession session) {
    switch (outcome.Kind) {
      case MaxonDebugger.StepOutcomeKind.Stopped:
        session.CurrentStop = outcome.Stop;
        EmitStop(BuildStopReport(dbg, exePath, outcome.Stop));
        break;
      case MaxonDebugger.StepOutcomeKind.Exited:
        session.CurrentStop = null;
        session.Finished = true;
        break;
      case MaxonDebugger.StepOutcomeKind.TimedOut:
        // Intercepted here rather than worded by StepUnavailableReason, because a timeout is not an
        // "unavailable step" — it is a live target, and its report must state the deadline it hit.
        EmitTimeout(WaitingForStepStopText, dbg);
        EndIncompleteSession(session);
        break;
      default:
        EmitError(StepUnavailableReason(outcome.Kind));
        break;
    }
  }

  /// Close a batch session that did not reach the program's own exit: there is no parked frame left to
  /// inspect, no further command can mean anything, and the driver must exit NONZERO. Stated once because
  /// every such ending — a timeout on continue, on a step, or in the drain, an unacked continue, and the
  /// drain cap — owes exactly the same three facts.
  private static void EndIncompleteSession(BatchSession session) {
    session.CurrentStop = null;
    session.Finished = true;
    session.Incomplete = true;
  }

  private static void BatchLocals(MaxonDebugger dbg, MaxonDebugger.StopInfo? currentStop) {
    if (currentStop is not { } stop) { EmitError(NotStoppedBatchText); return; }
    if (MakeValueRenderer(dbg, out var reason) is not { } renderer) { EmitError(reason); return; }
    try {
      var values = renderer.Locals(stop);
      WriteEvent(w => {
        w.WriteString("event", "locals");
        w.WriteStartArray("values");
        foreach (var v in values) WriteValueObject(w, v);
        w.WriteEndArray();
      });
    } catch (DebuggerException ex) {
      EmitError(ex.Message);
    }
  }

  private static void BatchPrint(MaxonDebugger dbg, string rest, MaxonDebugger.StopInfo? currentStop) {
    if (currentStop is not { } stop) { EmitError(NotStoppedBatchText); return; }
    if (rest.Length == 0) { EmitError("print needs an expression (e.g. print person.home.name)"); return; }
    if (MakeValueRenderer(dbg, out var reason) is not { } renderer) { EmitError(reason); return; }
    try {
      var value = renderer.Evaluate(stop, rest);
      WriteEvent(w => {
        w.WriteString("event", "value");
        WriteValueBody(w, value);
      });
    } catch (DebuggerException ex) {
      EmitError(ex.Message);
    }
  }

  /// If the target is still parked when the command list ends, continue past any remaining breakpoints
  /// so it runs to completion — bounded so a re-arming breakpoint cannot loop the driver forever, and
  /// reporting every way the drain can END WITHOUT the program finishing, since each of those leaves a
  /// live target the caller must dispose of rather than a clean exit code to print.
  private static void DrainToExit(MaxonDebugger dbg, BatchSession session) {
    if (session.Finished || dbg.HasExited) return;

    bool draining = true;
    for (int i = 0; i < RuntimeDrainCap && draining; i++) {
      if (!dbg.Continue()) {
        EmitError(ContinueUnackedText);
        EndIncompleteSession(session);
        draining = false;
        break;
      }

      var wait = dbg.WaitForStop();
      switch (wait.Status) {
        case MaxonDebugger.StopWaitStatus.Stopped:
          break;                                       // another breakpoint parked us; continue past it
        case MaxonDebugger.StopWaitStatus.Exited:
          draining = false;
          break;
        case MaxonDebugger.StopWaitStatus.TimedOut:
          EmitTimeout(WaitingForCompletionText, dbg);
          EndIncompleteSession(session);
          draining = false;
          break;
        default:
          throw new InvalidOperationException($"Unhandled stop-wait status {wait.Status}");
      }
    }

    if (draining) {
      EmitError($"the target was still stopping at breakpoints after {RuntimeDrainCap} continues; giving up");
      EndIncompleteSession(session);
    }

    session.Finished = true;
  }

  /// A generous cap on how many parked breakpoints DrainToExit will step past before giving up — far
  /// more than any batch session sets, so it only ever guards against a pathological re-arming loop.
  private const int RuntimeDrainCap = 1024;

  // ---- Shared: an honest timeout ----

  /// <summary>
  /// What a timed-out wait was waiting for. A closed vocabulary stated ONCE, because the same phrase is
  /// DATA in the batch `timeout` event and PROSE in the interactive message — and because naming the wait
  /// is half of what makes the report honest (the other half is the deadline it hit).
  /// </summary>
  private const string WaitingForContinueStopText = "a stop after continue";
  private const string WaitingForStepStopText = "a stop after a step";
  private const string WaitingForCompletionText = "the program to run to completion";

  /// <summary>
  /// The batch face's timeout event. It is DELIBERATELY not an `exit`: the deadline elapsed while the
  /// target was still alive, so the two facts it states — WHAT was awaited and for HOW LONG — are the
  /// ones an `{"event":"exit"}` could never carry, and reporting the wait as an exit is precisely the
  /// wrong answer this replaced. What then became of the target is the following exit event's business.
  /// </summary>
  private static void EmitTimeout(string waitingFor, MaxonDebugger dbg) => WriteEvent(w => {
    w.WriteString("event", "timeout");
    w.WriteString("waitingFor", waitingFor);
    w.WriteNumber("seconds", dbg.StopTimeoutSeconds);
  });

  /// The interactive face's statement of a timeout: the same two facts the batch event carries. What can
  /// be DONE about it differs by which wait ran out — a continue can simply be waited on again, a step
  /// cannot — so the recourse is the caller's sentence and this is only ever the first one.
  private static string TimeoutText(string waitingFor, string limitSeconds) =>
    $"Timed out after {limitSeconds}s waiting for {waitingFor}.";

  // ---- Shared: build a stop report ----

  private static StopReport BuildStopReport(MaxonDebugger dbg, string exePath, MaxonDebugger.StopInfo stop) {
    var loc = dbg.Symbolize(stop.PcOffset);
    var (sourcePath, source) = ReadSourceWindow(loc, exePath);
    var backtrace = dbg.Backtrace();
    return new StopReport(stop, ReasonText(stop.Reason), loc, sourcePath, source, backtrace);
  }

  private static (string Path, IReadOnlyList<SourceLine> Lines) ReadSourceWindow(
      MaxonDebugger.SymLocation loc, string exePath) {
    if (!loc.HasLine) return ("", []);

    var path = ResolveSourcePath(loc.File, exePath);
    if (path == null) return ("", []);

    string[] all;
    try {
      all = File.ReadAllLines(path);
    } catch {
      return ("", []);
    }

    var lines = new List<SourceLine>();
    uint first = loc.Line > SourceWindowRadius ? loc.Line - SourceWindowRadius : 1;
    uint last = loc.Line + SourceWindowRadius;
    for (uint n = first; n <= last && n <= all.Length; n++)
      lines.Add(new SourceLine(n, all[n - 1], n == loc.Line));
    return (path, lines);
  }

  /// Find the source file a sidecar path names: as recorded (relative to the CWD), else by leaf name
  /// next to the executable. Null when it cannot be found — the renderer then omits the window rather
  /// than inventing one.
  private static string? ResolveSourcePath(string sidecarPath, string exePath) {
    if (File.Exists(sidecarPath)) return sidecarPath;
    var exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
    if (exeDir != null) {
      var beside = Path.Combine(exeDir, Path.GetFileName(sidecarPath));
      if (File.Exists(beside)) return beside;
    }
    return null;
  }

  // ---- Shared renderers (reused by P4/DAP/TUI) ----

  /// The current-line marker and prompt glyph. Unicode when the console takes it (checked live, AFTER
  /// TryEnableUtf8 has run, so a switched-on UTF-8 encoding is seen); the ASCII forms are the graceful
  /// fallback the design asks for.
  private static string CurrentLineMarker => SupportsUnicode() ? "→" : ">";
  private static string PromptGlyph => SupportsUnicode() ? "›" : ">";

  private static void RenderStopText(StopReport report, TextWriter w) {
    var loc = report.Location;
    var where = loc.HasLine ? $"{loc.File}:{loc.Line}:{loc.Col}" : "<no line>";
    var inFn = loc.HasFunction ? $" in {loc.Function}" : "";
    w.WriteLine();
    w.WriteLine($"Stopped ({report.ReasonText}) at {where}{inFn}  [0x{report.Location.CodeOffset:x}]");

    if (report.Source.Count > 0) {
      foreach (var sl in report.Source) {
        var marker = sl.IsCurrent ? CurrentLineMarker : " ";
        w.WriteLine($"  {marker} {sl.Number,4} | {sl.Text}");
      }
    } else if (loc.HasLine) {
      w.WriteLine($"  (source for {loc.File} unavailable)");
    }

    RenderBacktraceText(report.Backtrace, w);
  }

  /// The reason a backtrace produced no usable frames, or null when it succeeded — stated ONCE so the
  /// text and JSON faces cannot describe an unsupported agent and an unacked command differently.
  private static string? BacktraceUnavailableReason(MaxonDebugger.BacktraceStatus status) => status switch {
    MaxonDebugger.BacktraceStatus.Ok => null,
    MaxonDebugger.BacktraceStatus.UnsupportedByAgent =>
      "not supported by this binary's debug agent (rebuild to enable)",
    MaxonDebugger.BacktraceStatus.NotAcknowledged =>
      "backtrace command not acknowledged (the target may have exited)",
    _ => throw new InvalidOperationException($"Unhandled backtrace status {status}"),
  };

  private static void RenderBacktraceText(MaxonDebugger.BacktraceResult bt, TextWriter w) {
    if (BacktraceUnavailableReason(bt.Status) is { } reason) {
      w.WriteLine($"  backtrace: {reason}.");
      return;
    }
    if (bt.Frames.Count == 0) {
      w.WriteLine("  backtrace: (no stack — stopped at entry)");
      return;
    }
    w.WriteLine("  backtrace:");
    foreach (var f in bt.Frames) {
      var loc = f.Location;
      var where = loc.HasLine ? $"{loc.File}:{loc.Line}" : "<no line>";
      var fn = loc.HasFunction ? loc.Function : "<unknown>";
      w.WriteLine($"    #{f.Index}  {fn}  at {where}  [0x{f.CodeOffset:x}]");
    }
  }

  // ---- Shared renderers: JSON ----

  private static void EmitStop(StopReport report) => WriteEvent(w => {
    var loc = report.Location;
    w.WriteString("event", "stop");
    w.WriteString("reason", report.ReasonText);
    w.WriteString("offset", HexOffset(loc.CodeOffset));
    if (loc.HasFunction) w.WriteString("function", loc.Function);
    if (loc.HasLine) {
      w.WriteString("file", loc.File);
      w.WriteNumber("line", loc.Line);
      w.WriteNumber("col", loc.Col);
    }

    w.WriteStartArray("source");
    foreach (var sl in report.Source) {
      w.WriteStartObject();
      w.WriteNumber("line", sl.Number);
      w.WriteString("text", sl.Text);
      w.WriteBoolean("current", sl.IsCurrent);
      w.WriteEndObject();
    }
    w.WriteEndArray();

    WriteBacktraceArray(w, "backtrace", report.Backtrace);
  });

  private static void EmitBacktrace(MaxonDebugger.BacktraceResult bt) => WriteEvent(w => {
    w.WriteString("event", "backtrace");
    WriteBacktraceArray(w, "frames", bt);
  });

  private static void WriteBacktraceArray(Utf8JsonWriter w, string name, MaxonDebugger.BacktraceResult bt) {
    // An unavailable backtrace is null + a reason, NEVER an empty array — a consumer must not read
    // "unsupported" or "unacked" as "a real, empty stack."
    if (BacktraceUnavailableReason(bt.Status) is { } reason) {
      w.WriteNull(name);
      w.WriteString($"{name}Unavailable", reason);
      return;
    }
    w.WriteStartArray(name);
    foreach (var f in bt.Frames) {
      w.WriteStartObject();
      w.WriteNumber("frame", f.Index);
      var loc = f.Location;
      if (loc.HasFunction) w.WriteString("function", loc.Function);
      if (loc.HasLine) {
        w.WriteString("file", loc.File);
        w.WriteNumber("line", loc.Line);
      }
      w.WriteString("offset", HexOffset(f.CodeOffset));
      w.WriteEndObject();
    }
    w.WriteEndArray();
  }

  private static void EmitExit(MaxonDebugger dbg) => WriteEvent(w => {
    w.WriteString("event", "exit");
    switch (dbg.Outcome) {
      case MaxonDebugger.TargetOutcome.Exited:
        w.WriteNumber("code", dbg.ExitCode);
        break;
      case MaxonDebugger.TargetOutcome.Terminated:
        // The DRIVER killed the target, so the status it carries is the OS's and not the program's
        // answer; WHAT happened is stated instead of a number that would be read as a result.
        w.WriteBoolean("terminated", true);
        break;
      case MaxonDebugger.TargetOutcome.Running:
        // Unreachable: EndSession runs first and does not return while the target lives. It throws
        // rather than reviving `{"event":"exit","running":true}` — the exact wrong answer this rung
        // removed — if that ordering is ever broken.
        throw new InvalidOperationException("the exit event ran before EndSession released the target");
      default:
        throw new InvalidOperationException($"Unhandled target outcome {dbg.Outcome}");
    }
  });

  private static void EmitError(string message) => WriteEvent(w => {
    w.WriteString("event", "error");
    w.WriteString("message", message);
  });

  /// Write one compact, single-line JSON object to stdout (newline-delimited JSON: one event per line,
  /// so a consumer parses the stream incrementally).
  private static void WriteEvent(Action<Utf8JsonWriter> body) {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false })) {
      w.WriteStartObject();
      body(w);
      w.WriteEndObject();
    }
    Console.Out.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
  }

  // ---- Shared renderers: values (P4a; reused by print / locals across both faces) ----

  /// The message a pre-v4 binary gets: its agent ack-and-ignores the read-memory command, so value
  /// inspection is not available until it is rebuilt. Stated ONCE (like <see cref="BacktraceUnavailableReason"/>)
  /// so the text and JSON faces cannot word "rebuild to enable" differently.
  private const string ValueInspectionUnsupportedText =
    "value inspection is not supported by this binary's debug agent (rebuild to enable)";

  /// Why a memory read the renderer needed could not be satisfied, so a failed read surfaces the same
  /// wording everywhere rather than a bare exception message.
  private static string ReadMemoryUnavailableText(MaxonDebugger.ReadMemoryStatus status) => status switch {
    MaxonDebugger.ReadMemoryStatus.UnsupportedByAgent => ValueInspectionUnsupportedText,
    MaxonDebugger.ReadMemoryStatus.NotAcknowledged => "memory read not acknowledged (the target may have exited)",
    _ => throw new InvalidOperationException($"Unhandled read-memory status {status}"),
  };

  /// <summary>
  /// Build a value renderer over the current session, or return null with <paramref name="unsupportedReason"/>
  /// set when this binary's agent predates value inspection (control version &lt; DbgReadMemMinVersion) —
  /// the P3c UnsupportedByAgent gate, checked ONCE here rather than per read. The renderer's memory reads
  /// go through the version-gated, chunked agent read; a non-Ok read raises a <see cref="DebuggerException"/>
  /// the surface catches so a partial failure reports honestly instead of showing a guessed value.
  /// </summary>
  private static Debug.DbgValueRenderer? MakeValueRenderer(MaxonDebugger dbg, out string unsupportedReason) {
    if (!dbg.ValueInspectionSupported) {
      unsupportedReason = ValueInspectionUnsupportedText;
      return null;
    }
    unsupportedReason = "";
    return new Debug.DbgValueRenderer(dbg.Sidecar!, (addr, len) => {
      var r = dbg.ReadMemory(addr, len);
      return r.Status == MaxonDebugger.ReadMemoryStatus.Ok
        ? r.Data
        : throw new DebuggerException(ReadMemoryUnavailableText(r.Status));
    });
  }

  /// The value-tree TEXT face: one indented line per node (`name (Type) = display`), children nested
  /// under it. Shared by `print` and `locals` so they render identically.
  private static void RenderValueText(Debug.DbgValue value, TextWriter w) => RenderValueTextAt(value, w, 1);

  private static void RenderValueTextAt(Debug.DbgValue v, TextWriter w, int indent) {
    var pad = new string(' ', indent * 2);
    var type = v.TypeName.Length > 0 ? $" ({v.TypeName})" : "";
    var ellipsis = v.Truncated ? " …" : "";
    w.WriteLine($"{pad}{v.Name}{type} = {v.Display}{ellipsis}");
    foreach (var child in v.Children) RenderValueTextAt(child, w, indent + 1);
  }

  /// The value-tree JSON face, written as the fields of the current object (the `value` event inlines
  /// this; a `values[]` element wraps it via <see cref="WriteValueObject"/>). ONE shape, produced once,
  /// so text and JSON never diverge.
  private static void WriteValueBody(Utf8JsonWriter w, Debug.DbgValue v) {
    w.WriteString("name", v.Name);
    if (v.TypeName.Length > 0) w.WriteString("type", v.TypeName);
    w.WriteString("kind", v.Kind.ToString());
    w.WriteString("display", v.Display);
    if (v.Truncated) w.WriteBoolean("truncated", true);
    if (v.Children.Count > 0) {
      w.WriteStartArray("children");
      foreach (var c in v.Children) WriteValueObject(w, c);
      w.WriteEndArray();
    }
  }

  private static void WriteValueObject(Utf8JsonWriter w, Debug.DbgValue v) {
    w.WriteStartObject();
    WriteValueBody(w, v);
    w.WriteEndObject();
  }

  // ---- Small helpers ----

  private static Debug.MxdbgReader? LoadSidecar(string exePath) {
    var sidecarPath = exePath + Debug.MxdbgFormat.SidecarExtension;
    if (!File.Exists(sidecarPath)) {
      Console.Error.WriteLine($"maxon debug: no debug info found ('{sidecarPath}' does not exist; "
        + "build without --no-debug-info to produce it).");
      return null;
    }
    try {
      return new Debug.MxdbgReader(File.ReadAllBytes(sidecarPath));
    } catch (InvalidDataException ex) {
      Console.Error.WriteLine($"maxon debug: cannot read '{sidecarPath}': {ex.Message}");
      return null;
    }
  }

  /// <summary>
  /// `maxon debug --complete '&lt;partial line&gt;' &lt;exe&gt;` — print the completion candidates for a
  /// partial input, one per line, so the pure <see cref="DebugCompletion"/> engine is batch-testable (and
  /// is what an editor/DAP calls). Static over the sidecar alone: there is no live session, so LOCALS
  /// (which need a stopped frame) are not offered here — only commands, functions, and files. Deterministic
  /// (the engine returns sorted, de-duplicated candidates).
  /// </summary>
  public static int RunComplete(string exePath, string partialLine) {
    var sidecar = LoadSidecar(exePath);
    if (sidecar == null) return 1;

    var ctx = new CompletionContext(
      CommandWords,
      MaxonDebugger.FunctionNames(sidecar),
      MaxonDebugger.FileNames(sidecar),
      [],                                   // no live stop → no locals to complete
      ArgTargetFor);
    foreach (var candidate in DebugCompletion.Complete(partialLine, partialLine.Length, ctx))
      Console.Out.WriteLine(candidate);
    return 0;
  }

  /// The persistent command-history file — `~/.maxon_debug_history` via the user profile (`%USERPROFILE%`
  /// on Windows, `$HOME` on POSIX), or null when no home resolves (history then lives only in memory for
  /// the session). The editor guards all history-file I/O, so a null or unwritable path never faults.
  private static string? HistoryFilePath() {
    try {
      var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      return string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".maxon_debug_history");
    } catch (Exception) {
      return null;
    }
  }

  private static bool TryLoadCommands(string spec, out List<string> commands, out string error) {
    commands = [];
    error = "";
    string raw;
    if (spec.StartsWith('@')) {
      var path = spec[1..];
      try {
        raw = File.ReadAllText(path);
      } catch (Exception ex) {
        error = $"cannot read command file '{path}': {ex.Message}";
        return false;
      }
    } else {
      raw = spec;
    }

    // Commands separate on ';' or a newline, so both `--commands='a; b'` and `--commands=@file` (one
    // per line) parse the same way.
    foreach (var part in raw.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      commands.Add(part);
    return true;
  }

  // ---- Conditional breakpoints (P4d-1): the shared grammar split + refusal wording ----

  /// The keyword that introduces a breakpoint condition, and the usage fragment that describes it. One
  /// spelling, so the splitter and every usage/help line stay in step.
  private const string ConditionKeyword = "if";
  private const string BreakConditionUsage = "if <local> <op> <literal>";

  /// The message a `break … if` gets when this binary's agent cannot evaluate conditions. Stated ONCE
  /// (like <see cref="BacktraceUnavailableReason"/>) so the text and JSON faces cannot word it differently.
  private const string CondBpUnsupportedText =
    "conditional breakpoints are not supported by this binary's debug agent (rebuild to enable)";

  /// <summary>
  /// The message for a break REFUSED because of its condition, or null when the outcome is not a
  /// condition refusal. Both refusals mean the SAME thing to the user — nothing was armed — so all four
  /// break renderers route through this rather than each growing two more arms.
  /// </summary>
  private static string? ConditionRefusalText(MaxonDebugger.BreakResult r) => r.Kind switch {
    MaxonDebugger.BreakKind.ConditionUnsupported => CondBpUnsupportedText,
    MaxonDebugger.BreakKind.ConditionInvalid => $"condition not understood: {r.ConditionError}",
    MaxonDebugger.BreakKind.NoCode or MaxonDebugger.BreakKind.Set
      or MaxonDebugger.BreakKind.Unacknowledged or MaxonDebugger.BreakKind.Ambiguous
      or MaxonDebugger.BreakKind.NoMatch => null,
    _ => throw new InvalidOperationException($"Unhandled break kind {r.Kind}"),
  };

  /// The " if <condition>" tail a "breakpoint set" line carries when the breakpoint is conditional —
  /// one spelling for both text renderers.
  private static string ConditionSuffix(string condition) => condition.Length > 0 ? $" if {condition}" : "";

  /// <summary>
  /// Split a `break` argument into its TARGET and its optional `if &lt;condition&gt;` tail, on the FIRST
  /// standalone `if` token. Run BEFORE the file:line-vs-function dispatch, so `break foo.maxon:12 if n == 3`
  /// and `break helper if n == 3` both reach their resolver with a clean target.
  ///
  /// Shared by the interactive and batch break paths: those two are the cross-boundary pair the P4c review
  /// already had to single-source once, so the grammar extension is written here rather than at each end.
  /// "Standalone" means whitespace-delimited on both sides, so a target that merely contains the letters
  /// (`ifStream`, `verify`) is not split apart.
  /// </summary>
  private static (string Target, string Condition) SplitBreakCondition(string arg) {
    for (int i = 1; i + ConditionKeyword.Length <= arg.Length; i++) {
      if (string.CompareOrdinal(arg, i, ConditionKeyword, 0, ConditionKeyword.Length) != 0) continue;
      if (!char.IsWhiteSpace(arg[i - 1])) continue;

      int after = i + ConditionKeyword.Length;
      if (after < arg.Length && !char.IsWhiteSpace(arg[after])) continue;
      return (arg[..i].Trim(), arg[after..].Trim());
    }
    return (arg.Trim(), "");
  }

  private static bool TryParseFileLine(string arg, out string file, out uint line) {
    file = "";
    line = 0;
    int colon = arg.LastIndexOf(':');
    if (colon <= 0) return false;
    file = arg[..colon].Trim();
    return uint.TryParse(arg.AsSpan(colon + 1).Trim(), out line) && line > 0 && file.Length > 0;
  }

  private static (string Cmd, string Remainder) SplitFirst(string input) {
    int sp = input.IndexOf(' ');
    return sp < 0 ? (input, "") : (input[..sp], input[(sp + 1)..].Trim());
  }

  /// The canonical commands both faces dispatch on. `Run` and `Continue` are the same MECHANISM
  /// (continue) but stay distinct here so the interactive prompt can word them differently. `Step`/`Next`/
  /// `Finish`/`Until` are the P4b source-line stepping commands.
  private enum DebugCommand {
    Empty, Break, Run, Continue, Step, Next, Finish, Until, Backtrace, Print, Locals, Help, Quit, Unknown,
  }

  /// One command's vocabulary AND its completion policy: the canonical word, its aliases, and the pool its
  /// argument completes against. Stating all three together keeps them from drifting — a new command adds
  /// ONE row and both faces plus completion learn it at once.
  private readonly record struct CommandSpec(
    DebugCommand Command, string Canonical, string[] Aliases, CompletionArgTarget ArgTarget);

  /// The ONE place the command vocabulary is stated. Both faces classify through <see cref="ParseCommand"/>,
  /// completion draws its command pool and argument pools from <see cref="CommandWords"/> /
  /// <see cref="ArgTargetFor"/>, and "did you mean" suggests over <see cref="CommandWords"/> — all derived
  /// from this table, so a copy in one place and not another cannot happen.
  private static readonly CommandSpec[] CommandTable = [
    new(DebugCommand.Break,     "break",     ["b"],             CompletionArgTarget.FunctionsAndFiles),
    new(DebugCommand.Run,       "run",       ["r"],             CompletionArgTarget.None),
    new(DebugCommand.Continue,  "continue",  ["c"],             CompletionArgTarget.None),
    new(DebugCommand.Step,      "step",      ["s"],             CompletionArgTarget.None),
    new(DebugCommand.Next,      "next",      ["n"],             CompletionArgTarget.None),
    new(DebugCommand.Finish,    "finish",    [],                CompletionArgTarget.None),
    new(DebugCommand.Until,     "until",     ["u"],             CompletionArgTarget.None),
    new(DebugCommand.Backtrace, "backtrace", ["bt", "where"],   CompletionArgTarget.None),
    new(DebugCommand.Print,     "print",     ["p"],             CompletionArgTarget.Locals),
    new(DebugCommand.Locals,    "locals",    [],                CompletionArgTarget.Locals),
    new(DebugCommand.Help,      "help",      ["?", "commands"], CompletionArgTarget.None),
    new(DebugCommand.Quit,      "quit",      ["q", "exit"],     CompletionArgTarget.None),
  ];

  /// The spec a command WORD (canonical or alias) names, or null. The single scan both the classifier and
  /// the completion argument-target resolver share.
  private static CommandSpec? FindSpec(string word) {
    foreach (var spec in CommandTable)
      if (word == spec.Canonical || Array.IndexOf(spec.Aliases, word) >= 0) return spec;
    return null;
  }

  private static (DebugCommand Command, string Word, string Args) ParseCommand(string input) {
    var (word, args) = SplitFirst(input.Trim());
    if (word.Length == 0) return (DebugCommand.Empty, word, args);
    return (FindSpec(word)?.Command ?? DebugCommand.Unknown, word, args);
  }

  /// The canonical command words, sorted — the completion pool at the command position and the "did you
  /// mean" pool for an unknown command.
  internal static readonly IReadOnlyList<string> CommandWords =
    [.. CommandTable.Select(s => s.Canonical).OrderBy(w => w, StringComparer.Ordinal)];

  /// The pool a command's argument completes against, resolved through the ONE classifier so the alias set
  /// is never restated inside the completion engine.
  internal static CompletionArgTarget ArgTargetFor(string firstWord) =>
    FindSpec(firstWord)?.ArgTarget ?? CompletionArgTarget.None;

  /// The one wording of the "did you mean" hint — a leading-space suffix so a caller appends it to a
  /// message unconditionally. Shared by the unknown-command suffix and the function-break no-match hint so
  /// the phrase cannot drift between them. Empty when there is nothing to suggest.
  internal static string DidYouMeanSuffix(string? suggestion) =>
    suggestion is { Length: > 0 } ? $" Did you mean '{suggestion}'?" : "";

  /// The " Did you mean 'x'?" suffix for an unknown or unresolved COMMAND word, through the ONE fuzzy
  /// matcher the function-break suggestion also uses — empty when nothing is close enough.
  private static string DidYouMeanCommandSuffix(string word) =>
    DidYouMeanSuffix(DebugFuzzy.ClosestMatch(word, CommandWords, DebugFuzzy.MaxEditDistance));

  /// The JSON `action` string for a break outcome — the ONE spelling of the break-action vocabulary, so the
  /// file:line and function batch renderers cannot emit a differently-spelled action for the same outcome.
  private static string BreakActionName(MaxonDebugger.BreakKind kind) => kind switch {
    MaxonDebugger.BreakKind.NoCode => "no-code",
    MaxonDebugger.BreakKind.Set => "set",
    MaxonDebugger.BreakKind.Unacknowledged => "unacked",
    MaxonDebugger.BreakKind.Ambiguous => "ambiguous",
    MaxonDebugger.BreakKind.NoMatch => "no-match",
    MaxonDebugger.BreakKind.ConditionUnsupported => "condition-unsupported",
    MaxonDebugger.BreakKind.ConditionInvalid => "condition-invalid",
    _ => throw new InvalidOperationException($"Unhandled break kind {kind}"),
  };

  private static string ReasonText(long reason) => reason switch {
    Compiler.Ir.Runtime.RuntimeEmitter.DbgStopReasonBreakpoint => "breakpoint",
    Compiler.Ir.Runtime.RuntimeEmitter.DbgStopReasonStep => "step",
    _ => $"reason#{reason}",
  };

  /// The message a step outcome that is none of Stopped / Exited / TimedOut renders — stated ONCE (like
  /// <see cref="BacktraceUnavailableReason"/>) so the text and JSON faces cannot word "rebuild" or
  /// "no caller frame" differently. Those three are handled per-face: a stop renders, an exit closes the
  /// session, and a timeout must state the deadline it hit, which only the live session knows.
  private static string StepUnavailableReason(MaxonDebugger.StepOutcomeKind kind) => kind switch {
    MaxonDebugger.StepOutcomeKind.TimedOut =>
      throw new InvalidOperationException("a step timeout is reported by the face, with its deadline"),
    MaxonDebugger.StepOutcomeKind.UnsupportedByAgent =>
      "stepping is not supported by this binary's debug agent (rebuild to enable)",
    MaxonDebugger.StepOutcomeKind.NotAcknowledged =>
      "step command not acknowledged (the target may have exited)",
    MaxonDebugger.StepOutcomeKind.LimitReached =>
      "step limit reached before the next source line (the target is parked mid-statement)",
    MaxonDebugger.StepOutcomeKind.NoCallerFrame =>
      "cannot finish: no caller frame (a frameless leaf or the outermost frame)",
    MaxonDebugger.StepOutcomeKind.NoCode =>
      "no code at that line in the current function",
    _ => throw new InvalidOperationException($"Unhandled step outcome {kind}"),
  };

  private static string HexOffset(long offset) => $"0x{offset:x}";


  private static void TryEnableUtf8() {
    try {
      Console.OutputEncoding = Encoding.UTF8;
    } catch (Exception) {
      // A redirected/!tty handle may reject an encoding change; the ASCII fallback then covers glyphs.
    }
  }

  /// UTF-8 (65001) or UTF-16 (1200) means the console renders the → / › glyphs; anything else gets the
  /// ASCII fallback.
  private static bool SupportsUnicode() =>
    Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage || Console.OutputEncoding.CodePage == 1200;
}
