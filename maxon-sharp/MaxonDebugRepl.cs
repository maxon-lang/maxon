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

  public static int RunInteractive(string exePath, IReadOnlyList<string> targetArgs) {
    TryEnableUtf8();
    var sidecar = LoadSidecar(exePath);
    if (sidecar == null) return 1;

    MaxonDebugger dbg;
    try {
      dbg = MaxonDebugger.Attach(exePath, targetArgs, sidecar);
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

    /// The one wording of "you must be stopped first", shared by every command that reads the parked
    /// frame (backtrace / print / locals / step / next / finish / until).
    private const string NotStoppedText = "Not stopped — run to a breakpoint first.";

    public int Loop() {
      while (!_finished) {
        Console.Out.Write(Prompt());
        Console.Out.Flush();
        var line = Console.In.ReadLine();
        if (line == null) {                // EOF (piped input drained): leave the target parked-then-kill
          dbg.Terminate();
          break;
        }
        Execute(line.Trim());
      }
      dbg.WaitForExit(2000);
      dbg.JoinIo();
      return 0;
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
          dbg.Terminate();
          _finished = true;
          break;
        case DebugCommand.Unknown:
          Console.Out.WriteLine($"Unknown command '{word}'. Type 'help' for the command list.");
          break;
        default:
          throw new InvalidOperationException($"Unhandled command {command}");
      }
    }

    private void DoBreak(string arg) {
      if (!TryParseFileLine(arg, out var file, out var lineNo)) {
        Console.Out.WriteLine("Usage: break <file>:<line>");
        return;
      }
      var r = dbg.SetBreakpoint(file, lineNo);
      switch (r.Kind) {
        case MaxonDebugger.BreakKind.NoCode:
          Console.Out.WriteLine($"No code at {file}:{lineNo} (blank line, or no statement there).");
          break;
        case MaxonDebugger.BreakKind.Unacknowledged:
          Console.Out.WriteLine("The agent did not acknowledge the breakpoint.");
          break;
        case MaxonDebugger.BreakKind.Set:
          var inFn = r.Location.HasFunction ? $" in {r.Location.Function}" : "";
          Console.Out.WriteLine($"Breakpoint set at {file}:{lineNo}{inFn} (0x{r.Offset:x}).");
          break;
        default:
          throw new InvalidOperationException($"Unhandled break outcome {r.Kind}");
      }
    }

    private void DoContinue(bool isRun) {
      if (_finished) { Console.Out.WriteLine("The program has already exited."); return; }

      if (!dbg.Continue()) {
        Console.Out.WriteLine("The agent did not acknowledge continue.");
        return;
      }

      if (dbg.WaitForStop(out var stop)) {
        var report = BuildStopReport(dbg, exePath, stop);
        _stop = report;
        RenderStopText(report, Console.Out);
        return;
      }

      // No further stop: the program ran to completion.
      _stop = null;
      _finished = true;
      dbg.WaitForExit(2000);
      dbg.JoinIo();
      Console.Out.WriteLine(isRun
        ? $"Program exited with code {ExitCodeText(dbg)}."
        : $"Program continued to completion; exit code {ExitCodeText(dbg)}.");
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
          dbg.WaitForExit(2000);
          dbg.JoinIo();
          Console.Out.WriteLine($"Program exited with code {ExitCodeText(dbg)}.");
          break;
        default:
          Console.Out.WriteLine($"{StepUnavailableReason(outcome.Kind)}.");
          break;
      }
    }

    private static void PrintHelp() {
      Console.Out.WriteLine("Commands:");
      Console.Out.WriteLine("  break <file>:<line>   (b)   set a breakpoint at a source line");
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

  public static int RunBatch(string exePath, IReadOnlyList<string> targetArgs, string commandsSpec) {
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
      dbg = MaxonDebugger.Attach(exePath, targetArgs, sidecar, targetStdout: Console.Error);
    } catch (DebuggerException ex) {
      EmitError(ex.Message);
      return 1;
    }

    using (dbg) {
      if (!dbg.WaitForAgentAlive()) {
        EmitError("the debug agent never attached (is MAXON_DEBUG honored by this build?)");
        return 1;
      }

      bool finished = false;
      // The last stop the target reached, so a later `print`/`locals` knows which parked frame to read.
      MaxonDebugger.StopInfo? currentStop = null;
      foreach (var command in commands) {
        if (finished) break;
        RunBatchCommand(dbg, exePath, command, ref finished, ref currentStop);
      }

      // Drain any parked state so the target is never left spinning in the stop-the-world loop.
      DrainToExit(dbg, ref finished);
      EmitExit(dbg);
      dbg.JoinIo();
      return 0;
    }
  }

  private static void RunBatchCommand(MaxonDebugger dbg, string exePath, string commandLine, ref bool finished,
      ref MaxonDebugger.StopInfo? currentStop) {
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
        BatchContinue(dbg, exePath, ref finished, ref currentStop);
        break;
      case DebugCommand.Step:
        BatchStepCommand(dbg, exePath, dbg.StepInto, ref finished, ref currentStop);
        break;
      case DebugCommand.Next:
        BatchStepCommand(dbg, exePath, dbg.StepOver, ref finished, ref currentStop);
        break;
      case DebugCommand.Finish:
        BatchStepCommand(dbg, exePath, dbg.Finish, ref finished, ref currentStop);
        break;
      case DebugCommand.Until:
        BatchUntil(dbg, exePath, rest, ref finished, ref currentStop);
        break;
      case DebugCommand.Backtrace:
        EmitBacktrace(dbg.Backtrace());
        break;
      case DebugCommand.Locals:
        BatchLocals(dbg, currentStop);
        break;
      case DebugCommand.Print:
        BatchPrint(dbg, rest, currentStop);
        break;
      case DebugCommand.Quit:
        dbg.Terminate();
        finished = true;
        break;
      case DebugCommand.Help:
        EmitError("'help' is interactive-only; not available in --batch");
        break;
      case DebugCommand.Unknown:
        EmitError($"unknown command '{word}'");
        break;
      default:
        throw new InvalidOperationException($"Unhandled command {command}");
    }
  }

  private static void BatchBreak(MaxonDebugger dbg, string arg) {
    if (!TryParseFileLine(arg, out var file, out var lineNo)) {
      EmitError("break needs <file>:<line>");
      return;
    }
    var r = dbg.SetBreakpoint(file, lineNo);
    var action = r.Kind switch {
      MaxonDebugger.BreakKind.NoCode => "no-code",
      MaxonDebugger.BreakKind.Set => "set",
      MaxonDebugger.BreakKind.Unacknowledged => "unacked",
      _ => throw new InvalidOperationException($"Unhandled break outcome {r.Kind}"),
    };
    WriteEvent(w => {
      w.WriteString("event", "breakpoint");
      w.WriteString("action", action);
      w.WriteString("file", file);
      w.WriteNumber("line", lineNo);
      if (r.Kind != MaxonDebugger.BreakKind.NoCode) {
        w.WriteString("offset", HexOffset(r.Offset));
        if (r.Location.HasFunction) w.WriteString("function", r.Location.Function);
      }
    });
  }

  private static void BatchContinue(MaxonDebugger dbg, string exePath, ref bool finished,
      ref MaxonDebugger.StopInfo? currentStop) {
    if (!dbg.Continue()) { EmitError("the agent did not acknowledge continue"); return; }

    if (dbg.WaitForStop(out var stop)) {
      currentStop = stop;
      EmitStop(BuildStopReport(dbg, exePath, stop));
      return;
    }
    // Ran to completion: no parked frame to inspect any more.
    currentStop = null;
    finished = true;
    dbg.WaitForExit(2000);
  }

  /// The batch face's wording of "you must be stopped first", shared by locals / print / step / next /
  /// finish / until so they cannot drift.
  private const string NotStoppedBatchText = "not stopped — run to a breakpoint first";

  /// Run a step op that needs the current parked frame (step / next / finish) and emit its outcome.
  private static void BatchStepCommand(MaxonDebugger dbg, string exePath,
      Func<MaxonDebugger.StopInfo, MaxonDebugger.StepOutcome> op, ref bool finished,
      ref MaxonDebugger.StopInfo? currentStop) {
    if (currentStop is not { } stop) { EmitError(NotStoppedBatchText); return; }
    ApplyBatchStepOutcome(dbg, exePath, op(stop), ref finished, ref currentStop);
  }

  private static void BatchUntil(MaxonDebugger dbg, string exePath, string rest, ref bool finished,
      ref MaxonDebugger.StopInfo? currentStop) {
    if (currentStop is not { } stop) { EmitError(NotStoppedBatchText); return; }
    if (!uint.TryParse(rest.Trim(), out var line) || line == 0) { EmitError("until needs a line number"); return; }
    ApplyBatchStepOutcome(dbg, exePath, dbg.Until(stop, line), ref finished, ref currentStop);
  }

  /// Emit one step outcome the same way every batch step command does: a Stopped emits the same
  /// `{event:"stop",…}` shape a breakpoint stop does (so a consumer parses steps and breakpoints
  /// identically) and becomes the new parked frame; an Exited ends the run; anything else is an error
  /// event with the shared reason.
  private static void ApplyBatchStepOutcome(MaxonDebugger dbg, string exePath, MaxonDebugger.StepOutcome outcome,
      ref bool finished, ref MaxonDebugger.StopInfo? currentStop) {
    switch (outcome.Kind) {
      case MaxonDebugger.StepOutcomeKind.Stopped:
        currentStop = outcome.Stop;
        EmitStop(BuildStopReport(dbg, exePath, outcome.Stop));
        break;
      case MaxonDebugger.StepOutcomeKind.Exited:
        currentStop = null;
        finished = true;
        dbg.WaitForExit(2000);
        break;
      default:
        EmitError(StepUnavailableReason(outcome.Kind));
        break;
    }
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
  /// so it runs to completion — bounded so a re-arming breakpoint cannot loop the driver forever.
  private static void DrainToExit(MaxonDebugger dbg, ref bool finished) {
    if (finished || dbg.HasExited) return;

    for (int i = 0; i < RuntimeDrainCap && !dbg.HasExited; i++) {
      if (!dbg.Continue()) break;
      if (!dbg.WaitForStop(out _)) break;   // ran to exit
    }
    dbg.WaitForExit(2000);
    finished = true;
  }

  /// A generous cap on how many parked breakpoints DrainToExit will step past before giving up — far
  /// more than any batch session sets, so it only ever guards against a pathological re-arming loop.
  private const int RuntimeDrainCap = 1024;

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
    if (dbg.HasExited) w.WriteNumber("code", dbg.ExitCode);
    else w.WriteBoolean("running", true);
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

  /// The ONE place the command vocabulary (canonical words + aliases) is stated. Both the interactive
  /// REPL and the batch runner classify through this, so adding a P4 command (`step`, `threads`, …)
  /// here teaches BOTH faces at once — a copy in one and not the other would have one face accept a
  /// word the other rejects.
  private static (DebugCommand Command, string Word, string Args) ParseCommand(string input) {
    var (word, args) = SplitFirst(input.Trim());
    var command = word switch {
      "" => DebugCommand.Empty,
      "break" or "b" => DebugCommand.Break,
      "run" or "r" => DebugCommand.Run,
      "continue" or "c" => DebugCommand.Continue,
      "step" or "s" => DebugCommand.Step,
      "next" or "n" => DebugCommand.Next,
      "finish" => DebugCommand.Finish,
      "until" or "u" => DebugCommand.Until,
      "backtrace" or "bt" or "where" => DebugCommand.Backtrace,
      "print" or "p" => DebugCommand.Print,
      "locals" => DebugCommand.Locals,
      "help" or "?" or "commands" => DebugCommand.Help,
      "quit" or "q" or "exit" => DebugCommand.Quit,
      _ => DebugCommand.Unknown,
    };
    return (command, word, args);
  }

  private static string ReasonText(long reason) => reason switch {
    Compiler.Ir.Runtime.RuntimeEmitter.DbgStopReasonBreakpoint => "breakpoint",
    Compiler.Ir.Runtime.RuntimeEmitter.DbgStopReasonStep => "step",
    _ => $"reason#{reason}",
  };

  /// The message a non-Stopped/non-Exited step outcome renders — stated ONCE (like
  /// <see cref="BacktraceUnavailableReason"/>) so the text and JSON faces cannot word "rebuild" or
  /// "no caller frame" differently. Stopped and Exited are handled per-face (a stop renders, an exit
  /// closes the session).
  private static string StepUnavailableReason(MaxonDebugger.StepOutcomeKind kind) => kind switch {
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

  private static string ExitCodeText(MaxonDebugger dbg) => dbg.HasExited ? dbg.ExitCode.ToString() : "(unknown)";

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
