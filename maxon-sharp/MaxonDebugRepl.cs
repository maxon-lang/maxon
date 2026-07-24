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

  /// Everything the two renderers need for one stop: the symbolized location, the source window (empty
  /// when the file cannot be read), and the symbolized backtrace (null when the agent predates it).
  private readonly record struct StopReport(
    string ReasonText,
    MaxonDebugger.SymLocation Location,
    string SourcePath,
    IReadOnlyList<SourceLine> Source,
    IReadOnlyList<MaxonDebugger.Frame>? Backtrace);

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
      if (input.Length == 0) return;

      var (cmd, rest) = SplitFirst(input);
      switch (cmd) {
        case "break" or "b":
          DoBreak(rest);
          break;
        case "run" or "r" or "continue" or "c":
          DoContinue(cmd is "run" or "r");
          break;
        case "backtrace" or "bt" or "where":
          DoBacktrace();
          break;
        case "help" or "?" or "commands":
          PrintHelp();
          break;
        case "quit" or "q" or "exit":
          dbg.Terminate();
          _finished = true;
          break;
        default:
          Console.Out.WriteLine($"Unknown command '{cmd}'. Type 'help' for the command list.");
          break;
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
      if (_stop is not { } report) {
        Console.Out.WriteLine("Not stopped — run to a breakpoint first.");
        return;
      }
      var bt = dbg.Backtrace() ?? report.Backtrace;
      RenderBacktraceText(bt, Console.Out);
    }

    private static void PrintHelp() {
      Console.Out.WriteLine("Commands:");
      Console.Out.WriteLine("  break <file>:<line>   (b)   set a breakpoint at a source line");
      Console.Out.WriteLine("  run                   (r)   start the program (continue from entry)");
      Console.Out.WriteLine("  continue              (c)   resume from a breakpoint");
      Console.Out.WriteLine("  backtrace             (bt)  show the stopped call stack");
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
      foreach (var command in commands) {
        if (finished) break;
        RunBatchCommand(dbg, exePath, command, ref finished);
      }

      // Drain any parked state so the target is never left spinning in the stop-the-world loop.
      DrainToExit(dbg, ref finished);
      EmitExit(dbg);
      dbg.JoinIo();
      return 0;
    }
  }

  private static void RunBatchCommand(MaxonDebugger dbg, string exePath, string command, ref bool finished) {
    var (cmd, rest) = SplitFirst(command.Trim());
    switch (cmd) {
      case "":
        break;
      case "break" or "b":
        BatchBreak(dbg, rest);
        break;
      case "run" or "r" or "continue" or "c":
        BatchContinue(dbg, exePath, ref finished);
        break;
      case "backtrace" or "bt" or "where":
        EmitBacktrace(dbg.Backtrace());
        break;
      case "quit" or "q" or "exit":
        dbg.Terminate();
        finished = true;
        break;
      default:
        EmitError($"unknown command '{cmd}'");
        break;
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

  private static void BatchContinue(MaxonDebugger dbg, string exePath, ref bool finished) {
    if (!dbg.Continue()) { EmitError("the agent did not acknowledge continue"); return; }

    if (dbg.WaitForStop(out var stop)) {
      EmitStop(BuildStopReport(dbg, exePath, stop));
      return;
    }
    finished = true;
    dbg.WaitForExit(2000);
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
    return new StopReport(ReasonText(stop.Reason), loc, sourcePath, source, backtrace);
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

  private static void RenderBacktraceText(IReadOnlyList<MaxonDebugger.Frame>? frames, TextWriter w) {
    if (frames == null) {
      w.WriteLine("  backtrace: not supported by this binary's debug agent (rebuild to enable).");
      return;
    }
    if (frames.Count == 0) {
      w.WriteLine("  backtrace: (no stack — stopped at entry)");
      return;
    }
    w.WriteLine("  backtrace:");
    foreach (var f in frames) {
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

  private static void EmitBacktrace(IReadOnlyList<MaxonDebugger.Frame>? frames) => WriteEvent(w => {
    w.WriteString("event", "backtrace");
    if (frames == null) {
      w.WriteBoolean("supported", false);
      return;
    }
    WriteBacktraceArray(w, "frames", frames);
  });

  private static void WriteBacktraceArray(Utf8JsonWriter w, string name,
      IReadOnlyList<MaxonDebugger.Frame>? frames) {
    if (frames == null) { w.WriteNull(name); return; }
    w.WriteStartArray(name);
    foreach (var f in frames) {
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

  private static string ReasonText(long reason) => reason switch {
    Compiler.Ir.Runtime.RuntimeEmitter.DbgStopReasonBreakpoint => "breakpoint",
    _ => $"reason#{reason}",
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
