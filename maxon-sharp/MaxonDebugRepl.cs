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
  /// read), the symbolized backtrace (carrying its own status), and the DebugStream activity that led
  /// here (carrying its own status too).
  private readonly record struct StopReport(
    MaxonDebugger.StopInfo Stop,
    string ReasonText,
    MaxonDebugger.SymLocation Location,
    string SourcePath,
    IReadOnlyList<SourceLine> Source,
    MaxonDebugger.BacktraceResult Backtrace,
    MaxonDebugger.TraceSliceResult Trace);

  // ---- Interactive REPL ----

  public static int RunInteractive(string exePath, IReadOnlyList<string> targetArgs, TimeSpan? stopTimeout,
      IReadOnlyDictionary<string, string>? targetEnv, bool stopOthers) {
    TryEnableUtf8();
    var sidecar = LoadSidecar(exePath);
    if (sidecar == null) return 1;

    MaxonDebugger dbg;
    try {
      dbg = MaxonDebugger.Attach(exePath, targetArgs, sidecar, stopTimeout: stopTimeout,
        targetEnv: targetEnv, stopOthers: stopOthers);
    } catch (DebuggerException ex) {
      Console.Error.WriteLine($"maxon debug: {ex.Message}");
      return 1;
    }

    using (dbg) {
      if (!dbg.WaitForAgentAlive()) {
        Console.Error.WriteLine("maxon debug: the debug agent never attached (is MAXON_DEBUG honored by this build?).");
        return 1;
      }
      if (StopOthersUnsupportedText(dbg) is { } unsupported) {
        Console.Error.WriteLine($"maxon debug: {unsupported}.");
        dbg.EndSession(MaxonDebugger.SessionEnd.Immediate);
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
        case DebugCommand.Threads:
          RenderThreadsText(dbg.ListGreenThreads(), Console.Out);
          break;
        case DebugCommand.GtBacktrace:
          DoGtBacktrace(rest);
          break;
        case DebugCommand.GtSelect:
          DoGtSelect(rest);
          break;
        case DebugCommand.GtPark:
          DoGtHold(rest, park: true);
          break;
        case DebugCommand.GtResume:
          DoGtHold(rest, park: false);
          break;
        case DebugCommand.Print:
          DoPrint(rest);
          break;
        case DebugCommand.Locals:
          DoLocals();
          break;
        case DebugCommand.Trace:
          DoTrace();
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
      // A `gt <id>` selection redirects the WHOLE inspection surface, not only `print` — a backtrace of
      // the stopped thread beside locals from another one would be the most confusing pair of true
      // statements this REPL could make.
      if (dbg.SelectedGreenThread is { } selected) {
        RenderGtBacktraceText(selected.Id, dbg.GtBacktrace(selected.Id), Console.Out);
        return;
      }
      // The target is still parked at the same stop, so a fresh request is authoritative.
      RenderBacktraceText(dbg.Backtrace(), Console.Out);
    }

    /// A per-green-thread backtrace always RE-LISTS first: the id the user typed is only meaningful
    /// against a current listing, and the record index the engine posts is only meaningful against the
    /// array the agent last published. Listing here keeps those two in step without the user having to
    /// remember to type `threads` first.
    private void DoGtBacktrace(string rest) {
      if (!TryParseGtId(rest, out int id)) {
        Console.Out.WriteLine($"Usage: {GtBacktraceUsageText}");
        return;
      }

      RenderGtBacktraceText(id, dbg.GtBacktrace(id), Console.Out);
    }

    /// `gt <id>` — switch the inspection surface to another green thread; bare `gt` reports which one it
    /// is on. Deliberately does NOT print that thread's whole stack: `backtrace` does, and now describes
    /// the selected thread, so printing it here would be the same answer twice under two commands.
    private void DoGtSelect(string rest) {
      if (rest.Trim().Length == 0) {
        Console.Out.WriteLine(dbg.SelectedGreenThread is { } current
          ? $"On green thread #{current.Id} {GreenThreadName(current)}. {GtSelectUsageText}"
          : $"On the stopped green thread. {GtSelectUsageText}");
        return;
      }
      if (!TryParseGtId(rest, out int id)) {
        Console.Out.WriteLine($"Usage: {GtSelectUsageText}");
        return;
      }

      var result = dbg.SelectGreenThread(id);
      if (GtThreadCommandReason(result.Status) is { } reason) {
        Console.Out.WriteLine($"gt {id}: {reason}.");
        return;
      }
      Console.Out.WriteLine(GtSelectedText(id, result.Thread));
    }

    private void DoGtHold(string rest, bool park) {
      var verb = GtHoldCommandName(park);
      if (!TryParseGtId(rest, out int id)) {
        Console.Out.WriteLine($"Usage: {verb} <id>   (an id from 'threads')");
        return;
      }

      var result = park ? dbg.ParkGreenThread(id) : dbg.ResumeGreenThread(id);
      if (GtThreadCommandReason(result.Status) is { } reason) {
        Console.Out.WriteLine($"{verb} {id}: {reason}.");
        return;
      }
      Console.Out.WriteLine(GtHoldDoneText(id, park, result.Thread));
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
        RenderValueText(renderer.Evaluate(dbg.InspectionFrame(report.Stop), rest), Console.Out);
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
        var values = renderer.Locals(dbg.InspectionFrame(report.Stop));
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

    /// `trace` — the WHOLE window of DebugStream activity since the previous stop, where the automatic
    /// stop window shows only its tail. It answers even when there is nothing to show, refusals
    /// included, which is exactly the difference between a command and an orientation panel.
    private void DoTrace() {
      if (_stop is null) {
        Console.Out.WriteLine(NotStoppedText);
        return;
      }
      // Re-asked rather than taken from the stop report: the target is still parked at the same stop, so
      // a fresh slice is authoritative and covers whatever the ring has committed since it was built.
      RenderTraceText(dbg.TraceSlice(), limit: null, Console.Out);
    }

    /// Run a step op that needs the current parked frame (step / next / finish) and render its outcome.
    private void DoStepCommand(Func<MaxonDebugger.StopInfo, MaxonDebugger.StepOutcome> op) {
      if (_stop is not { } report) {
        Console.Out.WriteLine(NotStoppedText);
        return;
      }
      if (StepRefusedWhileSelectedText(dbg) is { } refusal) {
        Console.Out.WriteLine($"{refusal}.");
        return;
      }
      ApplyStepOutcome(op(report.Stop));
    }

    private void DoUntil(string rest) {
      if (_stop is not { } report) {
        Console.Out.WriteLine(NotStoppedText);
        return;
      }
      if (StepRefusedWhileSelectedText(dbg) is { } refusal) {
        Console.Out.WriteLine($"{refusal}.");
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
      Console.Out.WriteLine("  threads               (gts) list the live green threads, with the stop marked");
      Console.Out.WriteLine("  gt-backtrace <id>     (gtbt) backtrace one green thread (parked ones only)");
      Console.Out.WriteLine("  gt <id>                     switch backtrace/print/locals to another green thread");
      Console.Out.WriteLine("  gt-park <id>                stop scheduling one green thread (it stays inspectable)");
      Console.Out.WriteLine("  gt-resume <id>              let a parked green thread run again");
      Console.Out.WriteLine("  locals                      list the stopped function's locals with values");
      Console.Out.WriteLine("  print <expr>          (p)   render a value; dotted paths navigate (person.home.name)");
      Console.Out.WriteLine("  trace                 (tr)  the DebugStream events recorded since the previous stop");
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
      TimeSpan? stopTimeout, IReadOnlyDictionary<string, string>? targetEnv, bool stopOthers) {
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
        stopTimeout: stopTimeout, targetEnv: targetEnv, stopOthers: stopOthers);
    } catch (DebuggerException ex) {
      EmitError(ex.Message);
      return 1;
    }

    using (dbg) {
      if (!dbg.WaitForAgentAlive()) {
        EmitError("the debug agent never attached (is MAXON_DEBUG honored by this build?)");
        return 1;
      }
      if (StopOthersUnsupportedText(dbg) is { } unsupported) {
        EmitError(unsupported);
        dbg.EndSession(MaxonDebugger.SessionEnd.Immediate);
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

      // A crashed debuggee IS an incomplete session — the program did not run to completion, and any
      // breakpoint still ahead of it was missed. It folds in HERE rather than in the command loop
      // because it is only knowable once the target is gone and its status is settled.
      if (dbg.Outcome == MaxonDebugger.TargetOutcome.Crashed) session.Incomplete = true;

      EmitOutcome(dbg);
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
        // Redirected by a `gt <id>` selection for the reason the interactive face states: a backtrace of
        // one thread beside locals from another is the worst pair of true statements available.
        if (dbg.SelectedGreenThread is { } selected) EmitGtBacktrace(selected.Id, dbg.GtBacktrace(selected.Id));
        else EmitBacktrace(dbg.Backtrace());
        break;
      case DebugCommand.Threads:
        EmitThreads(dbg.ListGreenThreads());
        break;
      case DebugCommand.GtBacktrace:
        BatchGtBacktrace(dbg, rest);
        break;
      case DebugCommand.GtSelect:
        BatchGtSelect(dbg, rest);
        break;
      case DebugCommand.GtPark:
        BatchGtHold(dbg, rest, park: true);
        break;
      case DebugCommand.GtResume:
        BatchGtHold(dbg, rest, park: false);
        break;
      case DebugCommand.Locals:
        BatchLocals(dbg, session.CurrentStop);
        break;
      case DebugCommand.Print:
        BatchPrint(dbg, rest, session.CurrentStop);
        break;
      case DebugCommand.Trace:
        BatchTrace(dbg, session.CurrentStop);
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
    if (StepRefusedWhileSelectedText(dbg) is { } refusal) { EmitError(refusal); return; }
    ApplyBatchStepOutcome(dbg, exePath, op(stop), session);
  }

  private static void BatchUntil(MaxonDebugger dbg, string exePath, string rest, BatchSession session) {
    if (session.CurrentStop is not { } stop) { EmitError(NotStoppedBatchText); return; }
    if (StepRefusedWhileSelectedText(dbg) is { } refusal) { EmitError(refusal); return; }
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

  /// The batch twin of <see cref="Session.DoGtBacktrace"/>, and it re-lists for the same reason: an id
  /// only means something against a current listing, so a `--commands` script that asks for one without
  /// a preceding `threads` still gets a correct answer rather than an empty one.
  private static void BatchGtBacktrace(MaxonDebugger dbg, string rest) {
    if (!TryParseGtId(rest, out int id)) {
      EmitError($"gt-backtrace needs a green-thread id ({GtBacktraceUsageText})");
      return;
    }

    EmitGtBacktrace(id, dbg.GtBacktrace(id));
  }

  private static void BatchGtSelect(MaxonDebugger dbg, string rest) {
    if (!TryParseGtId(rest, out int id)) {
      EmitError($"gt needs a green-thread id ({GtSelectUsageText})");
      return;
    }

    var result = dbg.SelectGreenThread(id);
    WriteEvent(w => {
      w.WriteString("event", "gt-select");
      w.WriteNumber("id", id);
      if (GtThreadCommandReason(result.Status) is { } reason) {
        w.WriteString("action", "refused");
        w.WriteString("reason", reason);
        return;
      }
      w.WriteString("action", result.Thread is { IsStopped: true } ? "stopped-thread" : "selected");
      if (result.Thread is { } t && t.TopKind != MaxonDebugger.GtTopFrame.None) {
        w.WriteStartObject("frame");
        WriteLocationFields(w, t.TopLocation);
        w.WriteEndObject();
      }
    });
  }

  private static void BatchGtHold(MaxonDebugger dbg, string rest, bool park) {
    var verb = GtHoldCommandName(park);
    if (!TryParseGtId(rest, out int id)) {
      EmitError($"{verb} needs a green-thread id (an id from 'threads')");
      return;
    }

    var result = park ? dbg.ParkGreenThread(id) : dbg.ResumeGreenThread(id);
    WriteEvent(w => {
      w.WriteString("event", verb);
      w.WriteNumber("id", id);
      if (GtThreadCommandReason(result.Status) is { } reason) {
        w.WriteString("action", "refused");
        w.WriteString("reason", reason);
        return;
      }
      w.WriteString("action", park ? "parked" : "resumed");
    });
  }

  private static void BatchLocals(MaxonDebugger dbg, MaxonDebugger.StopInfo? currentStop) {
    if (currentStop is not { } stop) { EmitError(NotStoppedBatchText); return; }
    if (MakeValueRenderer(dbg, out var reason) is not { } renderer) { EmitError(reason); return; }
    try {
      var values = renderer.Locals(dbg.InspectionFrame(stop));
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
      var value = renderer.Evaluate(dbg.InspectionFrame(stop), rest);
      WriteEvent(w => {
        w.WriteString("event", "value");
        WriteValueBody(w, value);
      });
    } catch (DebuggerException ex) {
      EmitError(ex.Message);
    }
  }

  /// The batch twin of <see cref="Session.DoTrace"/>: the WHOLE window, and an answer even when there
  /// is nothing in it. Unlike the stop event's optional array, this one is always present — as a list
  /// or as a null-plus-reason — because a script that ASKED for the trace must be told why it is not
  /// getting one.
  private static void BatchTrace(MaxonDebugger dbg, MaxonDebugger.StopInfo? currentStop) {
    if (currentStop is null) { EmitError(NotStoppedBatchText); return; }

    var slice = dbg.TraceSlice();
    WriteEvent(w => {
      w.WriteString("event", "trace");
      WriteTraceArray(w, slice, limit: null);
    });
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
    return new StopReport(stop, ReasonText(stop.Reason), loc, sourcePath, source, backtrace,
      dbg.TraceSlice());
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

    if (StopCarriesTrace(report.Trace)) RenderTraceText(report.Trace, StopTraceTailLimit, w);
  }

  // ---- Shared renderers: DebugStream correlation (P4e) ----

  /// <summary>
  /// How many trace events the automatic stop window shows. It is a TAIL, and bounded for the reason
  /// the source window has a radius: a stop report is orientation, and a program that allocates in a
  /// loop can put thousands of events between two stops. The full window is one `trace` away, and the
  /// header says how many are in it, so the bound can never be mistaken for the whole story.
  /// </summary>
  private const int StopTraceTailLimit = 10;

  /// <summary>
  /// Does a STOP carry a trace panel at all? Orientation stays quiet when there is nothing to orient
  /// with: a binary without the `--debugstream` hooks — every binary, by default — would otherwise
  /// carry a refusal on every stop of every session, which is a true sentence nobody asked for. (The
  /// `trace` COMMAND always answers, refusals included; that is the difference between a command and
  /// an orientation panel.)
  ///
  /// But it must NOT stay quiet for a window that is empty BECAUSE its contents were lost. "Nothing
  /// happened" and "everything that happened was thrown away" are different answers, and only the
  /// second one owes the user a warning — measured: a stop taken while another processor flooded the
  /// ring reported no trace panel at all while the session had dropped 95,810,410 events.
  ///
  /// Stated ONCE because BOTH faces render this panel from the same report, and a predicate written
  /// twice is two answers to "did this stop show a trace?" waiting to drift apart.
  /// </summary>
  private static bool StopCarriesTrace(MaxonDebugger.TraceSliceResult slice) =>
    slice.Status == MaxonDebugger.TraceStatus.Ok
    && (slice.Events.Count > 0 || TraceLossText(slice) != null);

  /// The message a binary WITHOUT the trace hooks gets. It names the flag, because that is the entire
  /// content of the answer: DebugStream's event emission is opt-in (it costs a load and a branch at
  /// every trace site plus calls on the alloc/free/refcount paths), so most debuggees emit nothing at
  /// all — and rendering that as an empty list would read as "nothing happened", which is a different
  /// and wrong answer.
  private const string TraceNoStreamText =
    "this binary was built without --debugstream, so it emits no trace events "
    + "(rebuild it with --debugstream to record them)";

  /// <summary>
  /// Why a trace slice could not be produced, or null when it was — stated ONCE so the text and JSON
  /// faces cannot describe an old agent, a binary with no hooks and a detached ring differently. Each
  /// sends the user somewhere different, which is why they are four sentences and not one.
  /// </summary>
  private static string? TraceUnavailableReason(MaxonDebugger.TraceSliceResult slice) => slice.Status switch {
    MaxonDebugger.TraceStatus.Ok => null,
    MaxonDebugger.TraceStatus.UnsupportedByAgent =>
      "trace correlation is not supported by this binary's debug agent (rebuild to enable)",
    MaxonDebugger.TraceStatus.NoStreamInBinary => TraceNoStreamText,
    MaxonDebugger.TraceStatus.StreamDetached =>
      "this binary has the --debugstream hooks but never attached to the trace ring this session "
      + "created, so nothing was recorded",
    MaxonDebugger.TraceStatus.SchemaMismatch => slice.Reason,
    _ => throw new InvalidOperationException($"Unhandled trace status {slice.Status}"),
  };

  /// <summary>
  /// What a slice LOST, or null when it lost nothing: the producer's own drops (the 2 MB ring filled
  /// faster than the driver drained it), this driver's window cap, and an entry that was still being
  /// written when the target parked. Reported rather than swallowed — a slice that is quietly short is
  /// the same wrong answer as an empty list that actually meant "unavailable".
  /// </summary>
  private static string? TraceLossText(MaxonDebugger.TraceSliceResult slice) {
    var losses = new List<string>();
    if (slice.Dropped > 0) losses.Add($"{slice.Dropped} dropped by the debuggee (its trace ring filled)");
    if (slice.NotRetained > 0) losses.Add($"{slice.NotRetained} older events not retained");
    if (slice.Incomplete)
      losses.Add("an entry below the watermark was still being written, so the slice stops short of the stop");
    return losses.Count > 0 ? string.Join("; ", losses) : null;
  }

  /// <summary>
  /// The trace-slice TEXT face, shared by the automatic stop window (bounded by
  /// <paramref name="limit"/>) and the `trace` command (unbounded, <c>null</c>).
  ///
  /// It carries NO TIMESTAMP, and that is a decision rather than an omission: a stop PARKS the thread,
  /// so a clock reading taken around one describes the debugger, not the program. What a slice states
  /// is an ORDER and a WINDOW — "these, in this sequence, since you were last stopped". `maxon monitor`
  /// is the face that prints a timeline, because it is watching one.
  /// </summary>
  private static void RenderTraceText(MaxonDebugger.TraceSliceResult slice, int? limit, TextWriter w) {
    if (TraceUnavailableReason(slice) is { } reason) {
      w.WriteLine($"  trace: {reason}.");
      return;
    }

    // Read ONCE and rendered on EVERY arm below. The empty arm used to return before it, so a window
    // whose every event had been dropped or evicted printed "no trace events since the previous stop"
    // — the JSON face reporting the loss at that same stop, and the two faces disagreeing about what
    // had happened. Measured: 95,810,410 dropped and 56,122,099 not retained, rendered as "no events".
    string? loss = TraceLossText(slice);

    if (slice.Events.Count == 0) {
      w.WriteLine(loss is null
        ? "  trace: (no trace events since the previous stop)"
        : "  trace: nothing from this window survived to be shown:");
    } else {
      int omitted = TraceOmittedCount(slice, limit);
      var window = omitted > 0 ? $", most recent {slice.Events.Count - omitted}" : "";
      w.WriteLine($"  trace ({slice.Events.Count} since the previous stop{window}):");
      for (int i = omitted; i < slice.Events.Count; i++) w.WriteLine($"    {slice.Events[i].Text}");
      if (omitted > 0) w.WriteLine($"    … {omitted} earlier — 'trace' shows the whole window.");
    }

    if (loss is { } text) w.WriteLine($"    ⚠ {text}.");
  }

  /// <summary>
  /// How many of a slice's events a face leaves out under <paramref name="limit"/> — always the OLDEST,
  /// because a slice describes what led to a stop and the tail is the part nearest to it.
  ///
  /// ONE computation, because the automatic stop window is rendered by BOTH faces and a bound applied
  /// twice is a bound that can differ: `trace` in a transcript and `trace` in the JSON stream would then
  /// be two answers to one question, which is the whole thing the shared renderers exist to prevent.
  /// </summary>
  private static int TraceOmittedCount(MaxonDebugger.TraceSliceResult slice, int? limit) =>
    limit is { } n && n < slice.Events.Count ? slice.Events.Count - n : 0;

  /// The trace-slice JSON face. An unavailable slice is null + a reason, NEVER an empty array — the
  /// same discipline the frame lists follow, and for the same reason: a consumer must not read
  /// "this binary has no trace hooks" as "this program did nothing".
  private static void WriteTraceArray(Utf8JsonWriter w, MaxonDebugger.TraceSliceResult slice, int? limit) {
    if (WriteUnavailable(w, "trace", TraceUnavailableReason(slice))) return;

    // Present only when they have something to say, so a session that loses nothing emits exactly the
    // shape it would have without this rung — and a consumer that sees one knows it means something.
    if (slice.Dropped > 0) w.WriteNumber("traceDropped", slice.Dropped);
    if (slice.NotRetained > 0) w.WriteNumber("traceNotRetained", slice.NotRetained);
    if (slice.Incomplete) w.WriteBoolean("traceIncomplete", true);

    int omitted = TraceOmittedCount(slice, limit);
    if (omitted > 0) w.WriteNumber("traceOmitted", omitted);

    w.WriteStartArray("trace");
    for (int i = omitted; i < slice.Events.Count; i++) {
      w.WriteStartObject();
      w.WriteString("family", slice.Events[i].Family);
      w.WriteString("text", slice.Events[i].Text);
      w.WriteEndObject();
    }
    w.WriteEndArray();
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

  private static void RenderBacktraceText(MaxonDebugger.BacktraceResult bt, TextWriter w) =>
    RenderFramesText("backtrace", BacktraceUnavailableReason(bt.Status), bt.Frames, w,
      "(no stack — stopped at entry)");

  /// <summary>
  /// The frame-list TEXT face, shared by the stopped-thread backtrace and the per-green-thread one so a
  /// frame is worded identically whichever walk produced it. <paramref name="unavailableReason"/> is
  /// null when the walk succeeded — an EMPTY frame list is a real answer (a thread stopped at entry, or
  /// one that has never run) and says so rather than borrowing the unavailable wording.
  /// </summary>
  private static void RenderFramesText(string label, string? unavailableReason,
      IReadOnlyList<MaxonDebugger.Frame> frames, TextWriter w, string emptyText) {
    if (unavailableReason is { } reason) {
      w.WriteLine($"  {label}: {reason}.");
      return;
    }
    if (frames.Count == 0) {
      // An empty list is a real answer, not a failure — and it has exactly one cause per caller, so the
      // caller words it. A stopped thread with no frames is stopped at entry; a green thread with none
      // has not started. Neither is the unavailable wording above.
      w.WriteLine($"  {label}: {emptyText}");
      return;
    }
    w.WriteLine($"  {label}:");
    foreach (var f in frames) w.WriteLine($"    #{f.Index}  {FrameText(f)}");
  }

  /// One frame as a line of prose — the ONE spelling of "function at file:line [offset]", so the stop
  /// window, a backtrace and a green-thread top frame cannot describe the same frame three ways.
  private static string FrameText(MaxonDebugger.Frame f) => LocationText(f.Location) + $"  [{HexOffset(f.CodeOffset)}]";

  /// A resolved location as prose, honest about each half it could not resolve.
  private static string LocationText(MaxonDebugger.SymLocation loc) {
    var where = loc.HasLine ? $"{loc.File}:{loc.Line}" : "<no line>";
    var fn = loc.HasFunction ? loc.Function : "<unknown>";
    return $"{fn}  at {where}";
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

    // The SAME predicate the text face applies — see StopCarriesTrace — because "did this stop show a
    // trace?" must have one answer, whichever face is asking.
    if (StopCarriesTrace(report.Trace)) WriteTraceArray(w, report.Trace, StopTraceTailLimit);
  });

  private static void EmitBacktrace(MaxonDebugger.BacktraceResult bt) => WriteEvent(w => {
    w.WriteString("event", "backtrace");
    WriteBacktraceArray(w, "frames", bt);
  });

  private static void WriteBacktraceArray(Utf8JsonWriter w, string name, MaxonDebugger.BacktraceResult bt) =>
    WriteFramesOrReason(w, name, BacktraceUnavailableReason(bt.Status), bt.Frames);

  /// <summary>
  /// The frame-list JSON face, shared by every producer of frames. An unavailable list is null + a
  /// reason, NEVER an empty array — a consumer must not read "unsupported", "unacked" or "running on a
  /// processor" as "a real, empty stack."
  /// </summary>
  private static void WriteFramesOrReason(Utf8JsonWriter w, string name, string? unavailableReason,
      IReadOnlyList<MaxonDebugger.Frame> frames) {
    if (WriteUnavailable(w, name, unavailableReason)) return;

    w.WriteStartArray(name);
    foreach (var f in frames) {
      w.WriteStartObject();
      w.WriteNumber("frame", f.Index);
      WriteLocationFields(w, f.Location);
      w.WriteEndObject();
    }
    w.WriteEndArray();
  }

  /// <summary>
  /// The UNAVAILABLE half of every list the batch face emits: a null under the list's own name plus a
  /// `&lt;name&gt;Unavailable` reason beside it, and NEVER an empty array. Returns true when it wrote
  /// the refusal, so a caller reads as "refused, or here is the list".
  ///
  /// Stated once because every list this face emits owes it — frames, green threads, trace events —
  /// and the shape is the whole guarantee: a consumer must not be able to read "unsupported by this
  /// binary" as "a real, empty answer".
  /// </summary>
  private static bool WriteUnavailable(Utf8JsonWriter w, string name, string? unavailableReason) {
    if (unavailableReason is not { } reason) return false;

    w.WriteNull(name);
    w.WriteString($"{name}Unavailable", reason);
    return true;
  }

  /// A resolved location's JSON fields — the ONE spelling, so a frame and a green thread's top frame
  /// carry the same keys. The function and line are omitted when they could not be resolved; the OFFSET
  /// never is, because it is what the location IS (a code offset the sidecar was asked about) and is the
  /// only field that is always known. Writing it here rather than at each call site also removes the
  /// second copy of it that `Frame.CodeOffset` had become — `Symbolize` records its unbiased input as
  /// `SymLocation.CodeOffset`, so the two were always the same number spelled twice.
  private static void WriteLocationFields(Utf8JsonWriter w, MaxonDebugger.SymLocation loc) {
    if (loc.HasFunction) w.WriteString("function", loc.Function);
    if (loc.HasLine) {
      w.WriteString("file", loc.File);
      w.WriteNumber("line", loc.Line);
    }
    w.WriteString("offset", HexOffset(loc.CodeOffset));
  }

  // ---- Shared renderers: green threads (P4d-2a) ----

  /// What EVERY green-thread command says about an agent too old to serve it. One sentence, because they
  /// share one capability gate (<c>MaxonDebugger.GreenThreadsSupported</c>) and answering the same
  /// question several ways is how they would come to describe one binary differently.
  private const string GreenThreadsUnsupportedText =
    "green threads are not supported by this binary's debug agent (rebuild to enable)";

  /// <summary>
  /// Why a session must not start, or null when it may. `--stop-others` is written into the control
  /// segment BEFORE the target is spawned, so it is the one option whose support cannot be checked where
  /// it is applied — and an agent that does not read that word freezes nothing at all while every
  /// listing looks perfectly normal. It is therefore a REFUSAL to start rather than a warning: the whole
  /// point of the flag is that the user is about to trust what they see.
  /// </summary>
  private static string? StopOthersUnsupportedText(MaxonDebugger dbg) =>
    dbg.StopOthersRequested && !dbg.GreenThreadsSupported
      ? $"{StopOthersFlag} needs a rebuilt debug agent — this binary's cannot hold green threads, so the "
        + "rest of the program would keep running while the session reported it stopped"
      : null;

  /// The two spellings of what a stop does to the OTHER green threads. They are one setting with two
  /// names rather than two flags, so asking for both is a contradiction the parser refuses.
  public const string ThisGtFlag = "--this-gt";
  public const string StopOthersFlag = "--stop-others";

  /// The reason a green-thread listing produced nothing, or null when it succeeded — stated ONCE so the
  /// text and JSON faces cannot tell the user to rebuild when the target merely exited.
  /// What every green-thread command says when the LISTING it depends on could not be taken. Shared,
  /// because a per-thread command's first act is to list — so its failure is the listing's failure and
  /// must not acquire a second wording on the way out.
  private const string GtListUnavailableText = "threads command not acknowledged (the target may have exited)";

  private static string? GtListUnavailableReason(MaxonDebugger.GtListStatus status) => status switch {
    MaxonDebugger.GtListStatus.Ok => null,
    MaxonDebugger.GtListStatus.UnsupportedByAgent => GreenThreadsUnsupportedText,
    MaxonDebugger.GtListStatus.NotAcknowledged => GtListUnavailableText,
    _ => throw new InvalidOperationException($"Unhandled green-thread list status {status}"),
  };

  /// An id no green thread has ever carried in this session, and one that named a thread at an EARLIER
  /// stop. They are stated apart because the next step differs, and the second is the common one: ids are
  /// re-minted on every resume, so `threads; next; threads` renumbers the same live threads and the id a
  /// user just read is genuinely gone. "No green thread has id 3" would send them looking for a thread
  /// that is sitting right there under a new number.
  private const string GtUnknownIdText = "no green thread has that id in this session";
  private const string GtStaleIdText =
    "that id named a green thread at an earlier stop — ids are re-minted every time the target resumes, "
    + "so run 'threads' for the current list";

  /// What every green-thread command says about a thread executing on a processor. ONE sentence, because
  /// it is ONE fact — the park gate — and the four commands that hit it (`gt`, `gt-park`, `gt-backtrace`
  /// and the listing's own top-frame column) must not describe it as four different limitations.
  private const string GtRunningOnCpuText =
    "that green thread is running on a processor, so it has no stable stack to walk and a cooperative "
    + "park cannot reach it until it next interacts with the scheduler";

  /// <summary>
  /// Why a command that named ONE green thread did not do it, or null when it did — the single wording
  /// of every per-thread refusal, for `gt-backtrace`, `gt`, `gt-park` and `gt-resume` alike.
  ///
  /// One function because it answers one closed vocabulary (<c>MaxonDebugger.GtThreadStatus</c>), and
  /// because the alternative was measured: three near-identical switches sharing four of their arms,
  /// which is how a stale id comes to be worded as an unknown one in three places and correctly in a
  /// fourth. A member a given command cannot produce simply never reaches its caller.
  /// </summary>
  private static string? GtThreadCommandReason(MaxonDebugger.GtThreadStatus status) => status switch {
    MaxonDebugger.GtThreadStatus.Ok => null,
    MaxonDebugger.GtThreadStatus.UnsupportedByAgent => GreenThreadsUnsupportedText,
    MaxonDebugger.GtThreadStatus.NotListed => GtListUnavailableText,
    MaxonDebugger.GtThreadStatus.UnknownId => GtUnknownIdText,
    MaxonDebugger.GtThreadStatus.StaleId => GtStaleIdText,
    MaxonDebugger.GtThreadStatus.RunningOnCpu => GtRunningOnCpuText,
    MaxonDebugger.GtThreadStatus.NoFrame =>
      "that green thread has no readable frame yet (it has not started, or the agent could not vouch "
      + "for its frame chain), so there is nothing to inspect",
    MaxonDebugger.GtThreadStatus.SchedulerThread =>
      "that is a processor's scheduler thread, which is the processor itself — it is never scheduled, "
      + "so it cannot be parked",
    MaxonDebugger.GtThreadStatus.StoppedThread =>
      "that is the green thread the debugger is stopped on; it is already stopped, and parking it would "
      + "only stop it being resumed",
    MaxonDebugger.GtThreadStatus.Refused =>
      "the agent did not carry that out — a park needs a free slot in its "
      + $"{Compiler.Ir.Runtime.RuntimeEmitter.DbgMaxHeldGreenThreads}-thread table, a resume needs a park "
      + "to undo (a '--stop-others' freeze is lifted by 'continue', not by 'gt-resume'), a backtrace "
      + "needs a thread that did not complete while the target was parked, and all of them need a "
      + "target that is still there",
    _ => throw new InvalidOperationException($"Unhandled green-thread command status {status}"),
  };

  /// <summary>
  /// The runtime's own state word for a green thread. Reported ALONGSIDE the on-cpu fact rather than
  /// folded into it: `running` here means the scheduler last set it running, which a PARKED thread still
  /// reads, so collapsing the two would turn an honest pair of facts into a wrong one.
  ///
  /// An unrecognised value RENDERS (`status#N`) where its sibling <c>MaxonDebugger.TopFrameKindOf</c>
  /// THROWS, and the asymmetry is deliberate rather than an oversight. That one decodes a word the AGENT
  /// wrote from a closed set both ends share, so a value outside it means the segment is being misread.
  /// This one is a field of a live runtime struct, read unlocked, from a thread another processor may be
  /// recycling underneath us — a value nobody expected is a benign race, and crashing the debugger on it
  /// would be strictly worse than showing it. It is not a silent default: the raw value is printed.
  /// </summary>
  private static string GreenThreadStatusText(long status) => status switch {
    Compiler.Ir.Runtime.GtLayout.GtStatusReady => "ready",
    Compiler.Ir.Runtime.GtLayout.GtStatusRunning => "running",
    Compiler.Ir.Runtime.GtLayout.GtStatusCompleted => "completed",
    Compiler.Ir.Runtime.GtLayout.GtStatusWaiting => "waiting",
    _ => $"status#{status}",
  };

  /// Where a green thread is with respect to a PROCESSOR — the fact that decides whether its stack can
  /// be walked. Three words, because "the stop" and "running somewhere else" are as different as
  /// running and parked.
  private static string GreenThreadCpuText(MaxonDebugger.GreenThread t) =>
    t.IsStopped ? "stopped" : t.OnCpu ? "on-cpu" : "parked";

  /// <summary>
  /// Whether the DEBUGGER owns this thread, or null when it does not — a column that appears only when
  /// it has something to say, so an un-parked listing reads exactly as it did before this rung.
  ///
  /// "pending" is the honest word for a hold that is in force against a thread the scheduler is not in a
  /// position to stop yet. It is NOT a synonym for held, and the difference is the whole cooperative
  /// story: a `held` thread will not run again until it is resumed, a `pending` one is running right now.
  /// </summary>
  private static string? GreenThreadHoldText(MaxonDebugger.GtHold hold) => hold switch {
    MaxonDebugger.GtHold.None => null,
    MaxonDebugger.GtHold.Held => "held",
    MaxonDebugger.GtHold.Pending => "pending",
    _ => throw new InvalidOperationException($"Unhandled green-thread hold {hold}"),
  };

  /// A green thread's display name: its entry function, or which processor's scheduler thread it is.
  private static string GreenThreadName(MaxonDebugger.GreenThread t) =>
    t.IsSchedulerThread ? $"<scheduler P{t.ProcId}>"
    : t.EntryFunction.Length > 0 ? t.EntryFunction
    : "<unknown>";

  /// <summary>
  /// Why a green thread has NO FRAMES. THREE causes, none of them a failure, and the stopped one has to
  /// be tested FIRST: the stopped thread is also on-cpu, so an on-cpu-first test told the user their own
  /// stopped thread was "running on a processor" — exactly the wrong answer, since the one thing that
  /// thread is not doing is running. It happens at the entry stop, where the agent has parked before
  /// publishing any stop event and so has no PC to report.
  ///
  /// ⭐ It answers for BOTH surfaces that can show a frameless thread — the `threads` listing's top-frame
  /// column and an empty `gt-backtrace` — because they are the SAME walk over the SAME thread. Worded
  /// separately they disagreed, and did: `gt-backtrace` carried a literal "not started — no frames yet",
  /// which at the entry stop it printed about `main`, one line under a listing correctly saying that
  /// thread was stopped before any frame was published.
  /// </summary>
  ///
  /// The SCHEDULER-THREAD arm is a fourth cause and not a variation of the third: a processor's inline
  /// thread runs on the OS thread's own stack, so the frame above its saved one is the thread-entry
  /// thunk, which is outside `.text` and correctly ends the walk. Reporting that as "not started" was
  /// measured wrong the moment a second processor appeared — the worker in question was executing a
  /// green thread at that very instant.
  private static string GreenThreadNoFramesReason(MaxonDebugger.GreenThread t) =>
    t.IsStopped ? "stopped at entry, before any frame was published"
    : t.OnCpu ? "running on a processor — no stable stack to walk"
    : t.IsSchedulerThread ? "the processor's scheduler loop — its caller is the OS thread entry, outside the program's code"
    : "not started — no frames yet";

  /// Why a green thread's LISTED top frame is absent, or null when it has one. The agent reports no top
  /// frame in exactly the cases above, so the reason is that one rule and the kind word is the test.
  private static string? TopFrameUnavailableReason(MaxonDebugger.GreenThread t) =>
    t.TopKind != MaxonDebugger.GtTopFrame.None ? null : GreenThreadNoFramesReason(t);

  /// How an empty `gt-backtrace` frame list reads. The thread is absent only when the id resolved to
  /// none, which always carries a refusal reason that is printed INSTEAD of this — so the fallback
  /// wording is unreachable rather than a silent default, and it is still a true sentence if that ever
  /// changes.
  private static string GtEmptyFramesText(MaxonDebugger.GtBacktraceResult bt) =>
    bt.Thread is { } t ? $"({GreenThreadNoFramesReason(t)})" : "(no frames)";

  private static void RenderThreadsText(MaxonDebugger.GreenThreadList list, TextWriter w) {
    if (GtListUnavailableReason(list.Status) is { } reason) {
      w.WriteLine($"threads: {reason}.");
      return;
    }

    w.WriteLine($"green threads ({list.Threads.Count}):");
    int nameWidth = 0;
    foreach (var t in list.Threads) nameWidth = Math.Max(nameWidth, GreenThreadName(t).Length);

    foreach (var t in list.Threads) {
      var marker = t.IsStopped ? CurrentLineMarker : " ";
      var where = TopFrameUnavailableReason(t) is { } why ? why : LocationText(t.TopLocation);
      var hold = GreenThreadHoldText(t.Hold) is { } h ? $"[{h}] " : "";
      w.WriteLine($"  {marker} #{t.Id}  {GreenThreadName(t).PadRight(nameWidth)}  "
        + $"{GreenThreadStatusText(t.Status),-9} {GreenThreadCpuText(t),-7}  {hold}{where}");
    }

    // A truncated list is a WRONG ANSWER unless it says so — the agent's array is bounded, and a reader
    // must not take a short list for the whole set.
    if (list.Truncated)
      w.WriteLine($"  … more than {Compiler.Ir.Runtime.RuntimeEmitter.DbgMaxGreenThreads} green threads "
        + "are live; this list is truncated.");
  }

  private static void EmitThreads(MaxonDebugger.GreenThreadList list) => WriteEvent(w => {
    w.WriteString("event", "threads");
    if (GtListUnavailableReason(list.Status) is { } reason) {
      w.WriteNull("threads");
      w.WriteString("threadsUnavailable", reason);
      return;
    }

    if (list.Truncated) w.WriteBoolean("truncated", true);
    w.WriteStartArray("threads");
    foreach (var t in list.Threads) {
      w.WriteStartObject();
      w.WriteNumber("id", t.Id);
      w.WriteString("kind", t.IsSchedulerThread ? "scheduler" : "green");
      if (t.IsSchedulerThread) w.WriteNumber("proc", t.ProcId);
      else if (t.EntryFunction.Length > 0) w.WriteString("entry", t.EntryFunction);
      w.WriteString("status", GreenThreadStatusText(t.Status));
      w.WriteString("cpu", GreenThreadCpuText(t));
      // Omitted when the debugger holds nothing, so a session that never parks a thread emits exactly
      // the shape it did before this rung — and a consumer reading `hold` knows it means something.
      if (GreenThreadHoldText(t.Hold) is { } hold) w.WriteString("hold", hold);
      if (TopFrameUnavailableReason(t) is { } why) {
        w.WriteNull("topFrame");
        w.WriteString("topFrameUnavailable", why);
      } else {
        w.WriteStartObject("topFrame");
        WriteLocationFields(w, t.TopLocation);
        w.WriteEndObject();
      }
      w.WriteEndObject();
    }
    w.WriteEndArray();
  });

  private static void EmitGtBacktrace(int id, MaxonDebugger.GtBacktraceResult bt) => WriteEvent(w => {
    w.WriteString("event", "gt-backtrace");
    w.WriteNumber("id", id);
    WriteFramesOrReason(w, "frames", GtThreadCommandReason(bt.Status), bt.Frames);
  });

  /// The usage line for `gt-backtrace`, shared by both faces so they cannot describe the argument
  /// differently.
  private const string GtBacktraceUsageText = "gt-backtrace <id>   (an id from 'threads')";

  private const string GtSelectUsageText =
    "gt <id> switches to another green thread (an id from 'threads'); bare 'gt' reports the current one";

  /// The frame-list face of a per-thread backtrace, shared by `gt-backtrace <id>` and by `backtrace`
  /// while a thread is selected — which ARE the same answer and must not be worded twice.
  private static void RenderGtBacktraceText(int id, MaxonDebugger.GtBacktraceResult bt, TextWriter w) =>
    RenderFramesText($"green thread #{id}", GtThreadCommandReason(bt.Status), bt.Frames, w,
      GtEmptyFramesText(bt));

  /// The canonical spelling of each hold command, so its usage line, its refusal prefix and its JSON
  /// event name are one string rather than three that happen to match.
  private static string GtHoldCommandName(bool park) => park ? "gt-park" : "gt-resume";

  /// What `gt <id>` reports on success. The STOPPED thread is named as such rather than as a selection,
  /// because that is what the engine did with it: selecting it clears the selection.
  private static string GtSelectedText(int id, MaxonDebugger.GreenThread? thread) =>
    thread is { IsStopped: true }
      ? $"Now on green thread #{id} (the stopped thread) — 'backtrace', 'print' and 'locals' describe it."
      : $"Now on green thread #{id}"
        + (thread is { } t ? $" {GreenThreadName(t)}" : "")
        + " — 'backtrace', 'print' and 'locals' describe it until the target resumes.";

  private static string GtHoldDoneText(int id, bool park, MaxonDebugger.GreenThread? thread) {
    var name = thread is { } t ? $" {GreenThreadName(t)}" : "";
    return park
      ? $"Green thread #{id}{name} parked — the scheduler will not run it until 'gt-resume {id}'."
      : $"Green thread #{id}{name} resumed — the scheduler may run it again.";
  }

  /// <summary>
  /// Why a step command refuses while another green thread is selected, or null when nothing is.
  ///
  /// `gt <id>` moves what the debugger LOOKS AT; it cannot move what the target RUNS, because a stop
  /// belongs to one thread and stepping resumes that thread's processor. Refusing is the honest answer:
  /// silently stepping the stopped thread while the user is reading another one's locals would produce a
  /// stop report about a thread they were not looking at.
  /// </summary>
  private static string? StepRefusedWhileSelectedText(MaxonDebugger dbg) =>
    dbg.SelectedGreenThread is { } t
      ? $"stepping runs the thread the target is STOPPED on, and green thread #{t.Id} is selected — "
        + "select the stopped thread (marked in 'threads') or 'continue' first"
      : null;

  /// <summary>
  /// Parse a green-thread id argument. It parses as the driver's OWN id type, and that is the fix rather
  /// than a tidy-up: parsing as `uint` and casting made `gt-backtrace 4294967295` answer
  /// `{"event":"gt-backtrace","id":-1,…}` — the refusal was right, but the response named a request the
  /// user never made, and a batch consumer correlating by id is handed a number it never sent.
  /// Ids are minted from 1, so 0 and negatives are refused as usage errors rather than looked up.
  /// </summary>
  private static bool TryParseGtId(string text, out int id) =>
    int.TryParse(text.Trim(), out id) && id > 0;

  /// <summary>
  /// The batch face's ONE closing event. A CRASH is a different event, not an exit with an odd number:
  /// a process the OS killed for an unhandled exception has no exit code, it has a termination status,
  /// and writing that status into `code` is exactly what let `{"event":"exit","code":-1073741819}` read
  /// as a completed run. The `terminated` arm has said so structurally since P4b; the crash arm is the
  /// same refusal applied to the OTHER way a target ends without answering.
  /// </summary>
  private static void EmitOutcome(MaxonDebugger dbg) => WriteEvent(w => {
    switch (dbg.Outcome) {
      case MaxonDebugger.TargetOutcome.Exited:
        w.WriteString("event", "exit");
        w.WriteNumber("code", dbg.ExitCode);
        break;
      case MaxonDebugger.TargetOutcome.Crashed:
        w.WriteString("event", "crash");
        w.WriteString("status", dbg.CrashStatusText);
        break;
      case MaxonDebugger.TargetOutcome.Terminated:
        // The DRIVER killed the target, so the status it carries is the OS's and not the program's
        // answer; WHAT happened is stated instead of a number that would be read as a result.
        w.WriteString("event", "exit");
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
    Empty, Break, Run, Continue, Step, Next, Finish, Until, Backtrace, Threads, GtBacktrace, GtSelect,
    GtPark, GtResume, Print, Locals, Trace, Help, Quit, Unknown,
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
    new(DebugCommand.Threads,   "threads",   ["gts"],           CompletionArgTarget.None),
    new(DebugCommand.GtBacktrace, "gt-backtrace", ["gtbt"],     CompletionArgTarget.None),
    new(DebugCommand.GtSelect,  "gt",        ["thread"],        CompletionArgTarget.None),
    new(DebugCommand.GtPark,    "gt-park",   ["gtpark"],        CompletionArgTarget.None),
    new(DebugCommand.GtResume,  "gt-resume", ["gtresume"],      CompletionArgTarget.None),
    new(DebugCommand.Print,     "print",     ["p"],             CompletionArgTarget.Locals),
    new(DebugCommand.Locals,    "locals",    [],                CompletionArgTarget.Locals),
    new(DebugCommand.Trace,     "trace",     ["tr"],            CompletionArgTarget.None),
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
