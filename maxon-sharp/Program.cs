using System.Diagnostics;
using System.Text;
using MaxonSharp.Compiler;
using MaxonSharp.Lsp;
using MaxonSharp.Testing;

namespace MaxonSharp;

class Program {
  static async Task<int> Main(string[] args) {
    Console.OutputEncoding = Encoding.UTF8;
    Console.InputEncoding = Encoding.UTF8;

    if (args.Length == 0) {
      PrintUsage();
      return 1;
    }

    var command = args[0];

    return command switch {
      "build" => RunBuild(args[1..]),
      "run" => RunRun(args[1..]),
      "fmt" => RunFmt(args[1..]),
      "monitor" => DebugStreamMonitor.Run(args[1..]),
      "test" => TestCommand.Run(args[1..]),
      "spec-test" => RunSpecTests(args[1..]),
      "error-codes" => ErrorCodeRegistry.Run(args[1..]),
      "flat-namespace" => FlatNamespaceCheck.Run(args[1..]),
      "batch-rewriter-test" => BatchRewriterTests.RunAll(),
      "mxdbg-selftest" => Debug.MxdbgSelfTest.Run(),
      "stdlib-target-selftest" => Compiler.StdlibTargetSelfTest.Run(),
      "golden-mint-selftest" => Testing.GoldenMintSelfTest.Run(),
      "spec-run-selftest" => Testing.SpecRunSelfTest.Run(),
      "fmt-selftest" => Testing.FormatterSelfTest.Run(),
      "debug" => RunDebug(args[1..]),
      "coverage" => CoverageCommand.Run(args[1..]),
      "profile" => ProfileCommand.Run(args[1..]),
      "lsp-server" => await RunLspAsync(),
      "mcp" => Mcp.McpServer.Run(args[1..]),
      _ => Fail()
    };
  }

  static void PrintUsage() {
    Console.WriteLine("Usage: maxon <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  build [file|directory]   Compile a .maxon file or project directory");
    Console.WriteLine("  run <function>           Compile build.maxon and run the specified function");
    Console.WriteLine("  fmt [<file|directory>]   Format .maxon source files in-place (default: current directory)");
    Console.WriteLine("  monitor <exe> [args...]  Launch executable with shared-memory debug stream monitor");
    Console.WriteLine("  debug [options] <target> Inspect debug info (--debug-info sidecar); see 'Debugger options'");
    Console.WriteLine("  coverage <run|report> <exe>");
    Console.WriteLine($"                           Run a {CoverageFlag} binary and report line + branch coverage");
    Console.WriteLine("  profile run <exe>        Sample a running program and report where its CPU time went.");
    Console.WriteLine("                           Needs no instrumentation and no rebuild — only the .mxdbg sidecar");
    Console.WriteLine("  test [directory]         Run the project's *.test.maxon unit tests; see 'Unit test options'");
    Console.WriteLine("  spec-test [options]      Run spec tests (the COMPILER's own suite, not a project's)");
    Console.WriteLine("  error-codes <check|generate>");
    Console.WriteLine("                           Verify or regenerate the error-code registry");
    Console.WriteLine("  flat-namespace check     Verify no two files of one of this repository's Maxon");
    Console.WriteLine("                           projects declare the same top-level name");
    Console.WriteLine("  lsp-server               Start language server (LSP)");
    Console.WriteLine("  mcp [options]            Start the MCP server (HTTP, loopback only); see 'MCP options'");
    Console.WriteLine();
    Console.WriteLine("Build options (build, run):");
    // Both lines are DERIVED, never restated. The default is the host, and the roster comes from the
    // one table that also picks the object writer — the old text hard-coded both and was wrong
    // twice: it named a fixed default the code does not use, and it offered x64-linux as a working
    // example of a target this compiler has no writer for.
    Console.WriteLine($"  {TargetFlag}ARCH-OS         Set compilation target (default: the host, {Compiler.CompileTarget.Native.Triple})");
    Console.WriteLine($"                           Supported: {Compiler.CompileTarget.SupportedTriples}");
    Console.WriteLine("  --emit-ir                Write .ir file");
    Console.WriteLine("  --dump-stages            Write IR at each pipeline stage (.1-maxon.ir, etc.)");
    Console.WriteLine("  --mm-trace               Enable runtime memory manager trace output (stderr)");
    Console.WriteLine("  --mm-debug               Enable runtime memory debug checks (magic, canary, poison)");
    Console.WriteLine("  --literal-coverage       Report static-eligibility of managed literal sites to stderr (measurement only)");
    Console.WriteLine("  --async-trace            Enable async/await runtime trace output (stderr)");
    Console.WriteLine("  --debugstream            Enable shared-memory debug stream (use with 'maxon monitor')");
    Console.WriteLine($"  {CoverageFlag}               Instrument for code coverage: the binary counts each statement and");
    Console.WriteLine("                           `if` arm it executes and writes <output>.mxcov on exit. Changes the");
    Console.WriteLine("                           emitted code, so it is a separate build; see 'maxon coverage'");
    Console.WriteLine("  --debug-info             Force-write the <output>.mxdbg debug-info sidecar (on by default; exe stays byte-identical)");
    Console.WriteLine("  --no-debug-info          Do not write the <output>.mxdbg debug-info sidecar");
    Console.WriteLine("  --no-debug-agent         Omit the in-process debug agent entirely (hardened build; smaller binary)");
    Console.WriteLine("  --timing                 Print per-stage compile timings to stderr");
    Console.WriteLine("  --timing-functions=N     Also print top-N hottest functions per heavy pass (implies --timing)");
    Console.WriteLine();
    Console.WriteLine("Debugger options (debug):");
    Console.WriteLine("  <exe> [args...]          Interactive REPL: break file:line, run, continue, backtrace, quit");
    Console.WriteLine("  --batch --commands=<spec> <exe>");
    Console.WriteLine("                           Drive the REPL non-interactively (spec: ';'-separated or @file); JSON stops");
    Console.WriteLine("  --complete '<partial>' <exe>");
    Console.WriteLine("                           List the completion candidates for a partial input line (one per line)");
    Console.WriteLine($"  --dump-info <exe|.mxdbg> [{DumpSectionNames}]");
    Console.WriteLine("                           Print the sidecar. Name sections to print only those (default: all)");
    Console.WriteLine("  --symbolize <.mxdbg> <codeOffset...>");
    Console.WriteLine("                           Map .text code offsets to file:line:col");
    Console.WriteLine("  --attach-probe <exe>     Attach the in-process debug agent and read its handshake (P3a)");
    Console.WriteLine($"  {MaxonDebugger.StopTimeoutFlag}SECS      Bound the wait for the target's next stop "
      + $"(default {MaxonDebugger.DefaultStopTimeoutText}s;");
    Console.WriteLine("                           prefix any live-session form). A timeout is reported as a timeout,");
    Console.WriteLine("                           never as an exit, and the target is released rather than orphaned");
    Console.WriteLine($"  {TargetEnvFlag}N=V         Set a variable in the DEBUGGEE's environment (repeatable;");
    Console.WriteLine("                           prefix any target-spawning form). MAXON_MAX_PROCS=1 pins the");
    Console.WriteLine("                           scheduler to one processor, which makes a green-thread run reproducible");
    Console.WriteLine($"  {MaxonDebugRepl.ThisGtFlag} | {MaxonDebugRepl.StopOthersFlag}");
    Console.WriteLine("                           What a stop does to the OTHER green threads: park only the");
    Console.WriteLine($"                           trapping one ({MaxonDebugRepl.ThisGtFlag}, the default), or hold every");
    Console.WriteLine("                           thread for the duration of the stop. A hold is COOPERATIVE, so a");
    Console.WriteLine("                           thread already running keeps running until it next reaches the");
    Console.WriteLine("                           scheduler — 'threads' reports that as 'pending' rather than 'held'");
    Console.WriteLine("  --bp-test <exe> <off>    Set a breakpoint at a code offset, run, observe the stop, continue (P3b)");
    Console.WriteLine();
    Console.WriteLine("MCP options (mcp) — an agent-facing front-end over the same debugger engine:");
    Console.WriteLine($"  {Mcp.McpServer.PortFlag}N               TCP port to serve on. A taken port fails loudly; it is never");
    Console.WriteLine("                           swapped for another, which would send a configured client elsewhere");
    Console.WriteLine($"  {Mcp.McpServer.IdleTimeoutFlag}SECS      Reap a session nothing has touched for this long. HTTP gives no");
    Console.WriteLine("                           EOF, so this is what stops a vanished client's debuggee parking forever");
    Console.WriteLine($"  {Mcp.McpServer.MaxSessionsFlag}N       How many sessions may exist at once (each can own a live process)");
    Console.WriteLine("  The listener binds LOOPBACK ONLY and validates the Origin header on every request:");
    Console.WriteLine("  this server compiles and runs programs, so an exposed port is remote code execution.");
    Console.WriteLine("  There is deliberately no flag to widen either.");
    Console.WriteLine();
    // Printed by TestCommand rather than restated here — one listing, and in particular one
    // statement of the exit codes, which CI branches on.
    Console.WriteLine("Unit test options (test) — a PROJECT's own tests, not the compiler's suite:");
    TestCommand.WriteOptions(Console.Out);
    Console.WriteLine();
    Console.WriteLine("Spec test options:");
    Console.WriteLine("  --filter=PATTERN         Run only tests matching pattern");
    Console.WriteLine($"  --workers=N              Use N worker threads (default: {Testing.TestExecutor.DefaultWorkers} here)");
    Console.WriteLine("  --update-required        Force regeneration and update RequiredIR, stderr, and mm-trace blocks");
    Console.WriteLine($"  {DebugInfoSpecTestFlag}            Compile EVERY test with debug info on — the path `maxon build`");
    Console.WriteLine("                           takes by default, which a test's own <!-- DebugInfo --> directive");
    Console.WriteLine("                           turns on for one compile at a time");
    Console.WriteLine("  --verbose                Show per-test PASS/FAIL timing logs");
    Console.WriteLine("  --no-batch               Disable per-spec compile batching (each test compiled individually)");
    Console.WriteLine("  --network                Include 'category: network' specs. They reach the public internet,");
    Console.WriteLine("                           so they are excluded from the default gate: they fail on someone");
    Console.WriteLine("                           else's outage or rate limit, not on our code.");
    Console.WriteLine();
    Console.WriteLine("Logging (all commands):");
    Console.WriteLine("  --log=LEVEL              Set all log categories to LEVEL");
    Console.WriteLine("  --log=CATEGORY:LEVEL     Set specific category to LEVEL");
    Console.WriteLine();
    Console.WriteLine("Log levels: none, error, info, debug, trace");
    Console.WriteLine("Log categories: compiler, lexer, parser, semantic, hir, lir, optimizer, codegen, pe, testing");
    Console.WriteLine();
    Console.WriteLine("Testing log levels:");
    Console.WriteLine("  info   - Show failures and summary only");
    Console.WriteLine("  debug  - Also show each passing test");
  }

  /// <summary>
  /// Refuse an option combination the compiler cannot honour, AHEAD of the build cache. A cache hit
  /// returns a binary without ever reaching the compiler, so a rule enforced only inside the compile
  /// is a rule a warm build silently skips. The rule itself lives once, on the compiler.
  /// </summary>
  static bool RefusedOptionCombination() {
    if (Compiler.Compiler.CoverageConflict() is not { } error) return false;
    Console.Error.WriteLine(error.Format());
    return true;
  }

  static int Fail() {
    PrintUsage();
    return 1;
  }

  /// <summary>
  /// The `maxon debug` command. Read-only sidecar surfaces — `--dump-info &lt;exe|.mxdbg&gt;` and
  /// `--symbolize &lt;.mxdbg&gt; &lt;codeOffset...&gt;` (P1) — plus the substrate harnesses
  /// `--attach-probe` (P3a) / `--bp-test` (P3b). A bare `maxon debug &lt;exe&gt; [args]` launches the
  /// interactive REPL (P3c); `--batch --commands=&lt;file|inline&gt; &lt;exe&gt;` drives the same engine
  /// non-interactively, emitting one JSON event per stop.
  /// </summary>
  static int RunDebug(string[] args) {
    // Both session options are consumed as LEADING ones — before the subcommand and before the target —
    // so neither can be mistaken for one of the arguments forwarded verbatim to the debuggee.
    TimeSpan? stopTimeout = null;
    Dictionary<string, string>? targetEnv = null;
    // Tri-state on purpose: null = not mentioned, so asking for BOTH spellings of one setting is a
    // contradiction the parser can see rather than a last-one-wins accident.
    bool? stopOthers = null;

    while (args.Length > 0) {
      if (args[0] is MaxonDebugRepl.ThisGtFlag or MaxonDebugRepl.StopOthersFlag) {
        bool requested = args[0] == MaxonDebugRepl.StopOthersFlag;
        if (stopOthers is { } already && already != requested) {
          Console.Error.WriteLine($"maxon debug: {MaxonDebugRepl.ThisGtFlag} and "
            + $"{MaxonDebugRepl.StopOthersFlag} are the two settings of one option; pick one.");
          return 1;
        }
        stopOthers = requested;
        args = args[1..];
        continue;
      }

      if (args[0].StartsWith(MaxonDebugger.StopTimeoutFlag, StringComparison.Ordinal)) {
        var value = args[0][MaxonDebugger.StopTimeoutFlag.Length..];
        if (!PositiveSeconds.TryParse(value, out var parsed)) {
          Console.Error.WriteLine($"maxon debug: {MaxonDebugger.StopTimeoutFlag}<seconds> "
            + $"{PositiveSeconds.RequirementText} (got '{value}').");
          return 1;
        }
        stopTimeout = parsed;
        args = args[1..];
        continue;
      }

      if (args[0].StartsWith(TargetEnvFlag, StringComparison.Ordinal)) {
        var value = args[0][TargetEnvFlag.Length..];
        if (!TryParseTargetEnv(value, out var name, out var setting)) {
          Console.Error.WriteLine($"maxon debug: {TargetEnvFlag}<NAME>=<VALUE> needs a non-empty variable "
            + $"name followed by '=' (got '{value}').");
          return 1;
        }
        // Repeatable; a repeated NAME takes the LAST value, matching how a shell assignment behaves and
        // how the environment block itself can only hold one value per name.
        targetEnv ??= [];
        targetEnv[name] = setting;
        args = args[1..];
        continue;
      }

      break;
    }

    if (args.Length == 0) {
      Console.Error.WriteLine("Usage: maxon debug <exe> [args...]                 (interactive REPL)");
      Console.Error.WriteLine("       maxon debug --batch --commands=<spec> <exe>  (JSON, non-interactive)");
      Console.Error.WriteLine("       maxon debug --complete '<partial>' <exe>     (list completion candidates)");
      Console.Error.WriteLine("       maxon debug --dump-info <exe|.mxdbg>");
      Console.Error.WriteLine("       maxon debug --symbolize <.mxdbg> <codeOffset...>");
      Console.Error.WriteLine("       maxon debug --attach-probe <exe>");
      Console.Error.WriteLine("       maxon debug --bp-test <exe> <codeOffset>");
      Console.Error.WriteLine($"A live-session form may be prefixed with {MaxonDebugger.StopTimeoutFlag}<seconds>,");
      Console.Error.WriteLine($"and a target-spawning one with {TargetEnvFlag}<NAME>=<VALUE> (repeatable)");
      Console.Error.WriteLine($"and {MaxonDebugRepl.ThisGtFlag} / {MaxonDebugRepl.StopOthersFlag}.");
      return 1;
    }

    switch (args[0]) {
      case "--batch":
        return RunDebugBatch(args[1..], stopTimeout, targetEnv, stopOthers ?? false);

      case "--complete": {
        // Non-interactive completion: `maxon debug --complete '<partial line>' <exe>`. Prints the
        // candidates the interactive Tab key would offer, so the pure completion engine is batch-testable.
        if (RejectSessionOptions(stopTimeout, targetEnv, stopOthers, "--complete")) return 1;
        if (args.Length < 3) {
          Console.Error.WriteLine("maxon debug --complete needs a partial input line and a target executable "
            + "('maxon debug --complete \"<partial>\" <exe>').");
          return 1;
        }
        return MaxonDebugRepl.RunComplete(args[2], args[1]);
      }

      case "--dump-info": {
        if (RejectSessionOptions(stopTimeout, targetEnv, stopOthers, "--dump-info")) return 1;
        if (args.Length < 2) {
          Console.Error.WriteLine("maxon debug --dump-info needs a path to an executable or .mxdbg sidecar.");
          return 1;
        }
        var reader = MaxonDebugRepl.LoadSidecar(args[1]);
        if (reader == null) return 1;
        if (ParseDumpSections(args[2..]) is not { } sections) return 1;
        return DumpDebugInfo(reader, args[1], sections);
      }

      case "--symbolize": {
        if (RejectSessionOptions(stopTimeout, targetEnv, stopOthers, "--symbolize")) return 1;
        if (args.Length < 3) {
          Console.Error.WriteLine("maxon debug --symbolize needs a .mxdbg path and at least one code offset.");
          return 1;
        }
        var reader = MaxonDebugRepl.LoadSidecar(args[1]);
        if (reader == null) return 1;
        return SymbolizeOffsets(reader, args[2..]);
      }

      case "--attach-probe": {
        // P3a substrate check: attach the in-process debug agent and read back its handshake.
        // The interactive session (breakpoints, stepping, REPL) that this grows into lands in P3b+.
        if (args.Length < 2) {
          Console.Error.WriteLine("maxon debug --attach-probe needs a path to a Maxon executable.");
          return 1;
        }
        if (RejectStopOthers(stopOthers, "--attach-probe")) return 1;
        return DebugAgentProbe.Run(args[1], stopTimeout, targetEnv);
      }

      case "--bp-test": {
        // P3b breakpoint driver: set a breakpoint at a raw code offset, run, observe the stop, and
        // continue to completion. The offset is a function's codeStart from `--dump-info`. A trailing
        // "clear" additionally removes the breakpoint while stopped at it (exercises the
        // cleared-while-parked path). The symbolizing REPL (file:line -> address) is P3c.
        if (args.Length < 3) {
          Console.Error.WriteLine("maxon debug --bp-test needs an executable and a code offset "
            + "(a function's codeStart from --dump-info); an optional trailing 'clear' removes it at the stop.");
          return 1;
        }
        if (!TryParseCodeOffset(args[2], out var bpOffset)) {
          Console.Error.WriteLine($"Not a code offset: '{args[2]}' (use decimal or 0x-prefixed hex).");
          return 1;
        }
        bool clearAtStop = args.Length > 3 && args[3] == "clear";
        if (RejectStopOthers(stopOthers, "--bp-test")) return 1;
        return DebugAgentProbe.RunBpTest(args[1], bpOffset, clearAtStop, stopTimeout, targetEnv);
      }

      default:
        if (args[0].StartsWith('-')) {
          Console.Error.WriteLine($"Unknown 'maxon debug' option: {args[0]}");
          return 1;
        }
        // A bare target path (plus any args to forward): launch the interactive REPL.
        return MaxonDebugRepl.RunInteractive(args[0], args[1..], stopTimeout, targetEnv, stopOthers ?? false);
    }
  }

  /// Refuse <see cref="MaxonDebugger.StopTimeoutFlag"/> on a `maxon debug` surface that has no live
  /// session to time out. Accepting it there and doing nothing would be exactly the silent lie the flag
  /// exists to remove.
  static bool RejectStopTimeout(TimeSpan? stopTimeout, string surface) {
    if (stopTimeout == null) return false;

    Console.Error.WriteLine($"maxon debug {surface}: {MaxonDebugger.StopTimeoutFlag}<seconds> bounds a LIVE "
      + "debug session (the REPL, --batch, --attach-probe, --bp-test); reading a sidecar has no target to wait for.");
    return true;
  }

  /// The flag that sets a variable in a SPAWNED TARGET's environment (gdb's `set environment`). Its
  /// headline use is pinning a runtime knob the target reads before `main`: `MAXON_MAX_PROCS` fixes how
  /// many worker processors the scheduler spawns, which is what makes a green-thread transcript
  /// reproducible instead of machine-dependent.
  ///
  /// Internal rather than private because `maxon profile` spawns a target for exactly the same reason
  /// and needs the same knob for the same green-thread runs; a second spelling of one flag is how the
  /// two would come to disagree about what it is called.
  internal const string TargetEnvFlag = "--target-env=";

  /// The flag that names the compilation target. Spelled once: it is read by the option validator,
  /// by <see cref="ParseTarget"/>, by `spec-test`'s own option set and by the usage text, and a
  /// literal at each was four chances for them to disagree about what the flag is called.
  internal const string TargetFlag = "--target=";

  /// `spec-test`'s debug-info flag. Read by the option validator, the parse loop and the usage text,
  /// for the same reason <see cref="TargetFlag"/> is spelled once.
  ///
  /// It REPLACES the `MAXON_SPEC_DEBUG_INFO` environment variable, which was an instrument nothing in
  /// this tree ever set — the shape of switch that rots, since no gate can fail when a variable is
  /// unset. As a flag it can be put in a script (see buildall.sh) and be run deliberately.
  internal const string DebugInfoSpecTestFlag = "--debug-info";

  /// <summary>
  /// Parse a <see cref="TargetEnvFlag"/> value as `NAME=VALUE`. The NAME must be non-empty and must not
  /// itself contain '=' (the first '=' separates), and an EMPTY VALUE is accepted — setting a variable to
  /// the empty string is a real, distinct request from not setting it. Malformed input is REFUSED by the
  /// caller rather than ignored: a silently dropped variable would leave the target running under an
  /// environment the user believes they configured.
  /// </summary>
  internal static bool TryParseTargetEnv(string text, out string name, out string value) {
    name = "";
    value = "";
    int eq = text.IndexOf('=');
    if (eq <= 0) return false;

    name = text[..eq];
    value = text[(eq + 1)..];
    return true;
  }

  /// <summary>
  /// Refuse BOTH session options on a `maxon debug` surface that starts no target. Stated once, because
  /// "these surfaces only read a sidecar" is one fact about the command set and each option restating it
  /// is how the two would eventually disagree about which surfaces those are.
  /// </summary>
  static bool RejectSessionOptions(TimeSpan? stopTimeout, IReadOnlyDictionary<string, string>? targetEnv,
      bool? stopOthers, string surface) =>
    RejectStopTimeout(stopTimeout, surface) || RejectTargetEnv(targetEnv, surface)
      || RejectStopOthers(stopOthers, surface);

  /// Refuse the scheduler-locking pair on a `maxon debug` surface that has no green threads to hold —
  /// the sidecar readers, and the two substrate harnesses, which drive a target through the mailbox but
  /// have no thread commands and no listing to act on. Accepting it there and doing nothing is the same
  /// silent lie the two refusals above exist to prevent.
  static bool RejectStopOthers(bool? stopOthers, string surface) {
    if (stopOthers == null) return false;

    Console.Error.WriteLine($"maxon debug {surface}: {MaxonDebugRepl.ThisGtFlag} / "
      + $"{MaxonDebugRepl.StopOthersFlag} decide what a STOP does to the other green threads, which only "
      + "the REPL and --batch stop at.");
    return true;
  }

  /// Refuse <see cref="TargetEnvFlag"/> on a `maxon debug` surface that spawns no target, exactly as
  /// <see cref="RejectStopTimeout"/> does — accepting it there and doing nothing is the same silent lie.
  static bool RejectTargetEnv(IReadOnlyDictionary<string, string>? targetEnv, string surface) {
    if (targetEnv == null) return false;

    Console.Error.WriteLine($"maxon debug {surface}: {TargetEnvFlag}<NAME>=<VALUE> sets a variable in a "
      + "SPAWNED debuggee (the REPL, --batch); reading a sidecar starts no process to set it in.");
    return true;
  }

  /// <summary>
  /// `maxon debug --batch --commands=&lt;spec&gt; &lt;exe&gt; [args...]` — the non-interactive face of
  /// the REPL engine. <c>--commands</c> is a `;`-separated inline list or `@file`; the first non-option
  /// arg is the target, the rest are forwarded to it. Emits one JSON event per stop to stdout.
  /// </summary>
  static int RunDebugBatch(string[] args, TimeSpan? stopTimeout,
      IReadOnlyDictionary<string, string>? targetEnv, bool stopOthers) {
    string? commands = null;
    string? exe = null;
    var targetArgs = new List<string>();

    foreach (var arg in args) {
      if (arg.StartsWith("--commands=")) {
        commands = arg["--commands=".Length..];
      } else if (exe == null) {
        // Before the target, a '-'-prefixed token is a DRIVER option — reject an unknown one rather
        // than silently forwarding a mistyped flag (e.g. `--comands=`) to the target as an argument.
        if (arg.StartsWith('-')) {
          Console.Error.WriteLine($"maxon debug --batch: unknown option '{arg}' "
            + "(expected --commands=<spec> then <exe> [args...]).");
          return 1;
        }
        exe = arg;
      } else {
        // After the target, everything is forwarded to it verbatim (including any leading '-').
        targetArgs.Add(arg);
      }
    }

    if (commands == null) {
      Console.Error.WriteLine("maxon debug --batch needs --commands=<;-separated spec | @file>.");
      return 1;
    }
    if (exe == null) {
      Console.Error.WriteLine("maxon debug --batch needs a target executable.");
      return 1;
    }
    return MaxonDebugRepl.RunBatch(exe, targetArgs, commands, stopTimeout, targetEnv, stopOthers);
  }

  /// <summary>
  /// A section of the `.mxdbg` dump. `maxon debug --dump-info &lt;path&gt;` prints every one of them;
  /// naming sections after the path prints only those.
  ///
  /// <see cref="Lines"/> and <see cref="Statements"/> are the SAME table asked two different
  /// questions, which is why they share one walk below rather than being two printers. `lines`
  /// answers a DEBUGGER's question — "what source position is this `.text` offset?" — and the offset
  /// is its subject. `statements` answers a GATE's question — "which source positions can be stopped
  /// on, in code order?" — a fact about the SOURCE that must not move when unrelated codegen shifts
  /// every offset in the file. That distinction is not cosmetic: it is measured. Six checks across
  /// three committed debugger goldens were red when this was written, for no reason but a `main` that
  /// had grown somewhere else, and a golden that reds on every codegen change is one that gets
  /// regenerated without being read.
  /// </summary>
  enum DumpSection { Header, Files, Functions, Types, Lines, Statements }

  /// The section names the CLI accepts, DERIVED from the enum rather than listed again, so the parser
  /// and the usage text cannot disagree and a new section cannot be added without a spelling.
  static readonly (string Name, DumpSection Section)[] DumpSections =
    [.. Enum.GetValues<DumpSection>().Select(s => (s.ToString().ToLowerInvariant(), s))];

  static string DumpSectionNames => string.Join('|', DumpSections.Select(s => s.Name));

  /// <summary>
  /// The sections named on the command line, or ALL of them when none are — so the bare
  /// `--dump-info &lt;path&gt;` keeps printing the whole sidecar. Null when a name is not a section,
  /// which is refused (naming the valid set) rather than ignored: a silently dropped section name
  /// would print a dump the caller did not ask for and could not tell apart from the one it wanted.
  /// </summary>
  static HashSet<DumpSection>? ParseDumpSections(string[] names) {
    if (names.Length == 0) return [.. Enum.GetValues<DumpSection>()];

    var sections = new HashSet<DumpSection>();
    foreach (var name in names) {
      var match = DumpSections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
      if (match.Name == null) {
        Console.Error.WriteLine($"maxon debug --dump-info: '{name}' is not a section ({DumpSectionNames}).");
        return null;
      }
      sections.Add(match.Section);
    }
    return sections;
  }

  static int DumpDebugInfo(Debug.MxdbgReader r, string path, HashSet<DumpSection> sections) {
    if (sections.Contains(DumpSection.Header)) {
      Console.WriteLine($"Debug info: {path}");
      Console.WriteLine($"  target:   {r.Triple}");
      Console.WriteLine($"  build-id: 0x{r.BuildId:x16}");
    }

    if (sections.Contains(DumpSection.Files)) {
      Console.WriteLine($"  files ({r.FileCount}):");
      for (uint i = 0; i < r.FileCount; i++) {
        Console.WriteLine($"    [{i}] {r.FileName(i)}");
      }
    }

    if (sections.Contains(DumpSection.Functions)) {
      // The stack-slot offsets are frame-pointer-relative; the frame-pointer REGISTER is per target
      // (x29 on arm64, rbp on x64). The sidecar's offsets are target-agnostic — only this label differs.
      var framePointer = FramePointerRegister(r.Triple);

      Console.WriteLine($"  functions ({r.FunctionCount}):");
      for (uint i = 0; i < r.FunctionCount; i++) {
        var f = r.Function(i);
        Console.WriteLine($"    {f.Name,-32} [0x{f.CodeStart:x4}, 0x{f.CodeEnd:x4})  "
          + $"frame=0x{f.FrameSize:x}  params={f.ParamCount}  lines={f.LineCount}  locals={f.LocalCount}");
        for (uint k = f.LocalFirst; k < f.LocalFirst + f.LocalCount; k++) {
          var lc = r.Local(k);
          Console.WriteLine($"        {lc.Name,-20} {FormatLocation(lc, framePointer)}  : {r.TypeName(lc.TypeId)}");
        }
      }
    }

    if (sections.Contains(DumpSection.Types)) {
      Console.WriteLine($"  types ({r.TypeCount}):");
      for (uint i = 0; i < r.TypeCount; i++) {
        var t = r.Type(i);
        Console.WriteLine($"    [{i}] {t.Name,-28} {t.Kind}  size={t.Size} align={t.Align}  fields={t.FieldCount}");
        for (uint k = t.FieldFirst; k < t.FieldFirst + t.FieldCount; k++) {
          var fld = r.Field(k);
          Console.WriteLine($"        +0x{fld.Offset:x2}  {fld.Name,-20} : {r.TypeName(fld.TypeId)}");
        }
      }
    }

    // One walk for both questions: the offset column is the `lines` half, the source position the
    // `statements` half (see DumpSection). Asking for both prints the full rows.
    bool withOffsets = sections.Contains(DumpSection.Lines);
    if (withOffsets || sections.Contains(DumpSection.Statements)) {
      Console.WriteLine($"  line table ({r.LineCount}):");
      for (uint i = 0; i < r.LineCount; i++) {
        var l = r.Line(i);
        var offset = withOffsets ? $"0x{l.CodeOffset:x4}  " : "";
        Console.WriteLine($"    {offset}{l.File}:{l.Line}:{l.Col}{FormatLineFlags(l.Flags)}");
      }
    }

    return 0;
  }

  // The frame-pointer register the StackSlotRbpRel offsets are relative to, per the sidecar's target:
  // x29 on arm64, rbp on x64. The offsets themselves are target-agnostic (the emitter records the
  // frame-pointer-relative displacement); only this human-readable label differs.
  static string FramePointerRegister(string triple) =>
    triple.StartsWith("arm64", StringComparison.Ordinal) ? "x29" : "rbp";

  static string FormatLocation(Debug.MxdbgReader.LocalInfo lc, string framePointer) => lc.LocKind switch {
    Debug.MxdbgLocKind.StackSlotRbpRel =>
      lc.LocValue >= 0 ? $"[{framePointer}+0x{lc.LocValue:x}]" : $"[{framePointer}-0x{-lc.LocValue:x}]",
    Debug.MxdbgLocKind.Register => $"reg{lc.LocValue}",
    Debug.MxdbgLocKind.OptimizedOut => "<optimized out>",
    _ => throw new InvalidOperationException($"Unknown local location kind {lc.LocKind}"),
  };

  static int SymbolizeOffsets(Debug.MxdbgReader r, string[] offsetArgs) {
    int exitCode = 0;
    foreach (var arg in offsetArgs) {
      if (!TryParseCodeOffset(arg, out var offset)) {
        Console.Error.WriteLine($"Not a code offset: '{arg}' (use decimal or 0x-prefixed hex).");
        exitCode = 1;
        continue;
      }
      var line = r.PcToLine(offset);
      var fn = r.FunctionAt(offset);
      var where = line is { } l ? $"{l.File}:{l.Line}:{l.Col}" : "<no line>";
      var inFn = fn is { } f ? $"  (in {f.Name})" : "";
      Console.WriteLine($"0x{offset:x4}  {where}{inFn}");
    }
    return exitCode;
  }

  static string FormatLineFlags(uint flags) {
    var tags = new List<string>();
    if ((flags & Debug.MxdbgFormat.LineFlagStatement) != 0) tags.Add("statement");
    if ((flags & Debug.MxdbgFormat.LineFlagCoverage) != 0) tags.Add("coverage");
    return tags.Count == 0 ? "" : $"  [{string.Join(", ", tags)}]";
  }

  static bool TryParseCodeOffset(string s, out uint offset) {
    if (s.StartsWith("0x") || s.StartsWith("0X")) {
      return uint.TryParse(s.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture, out offset);
    }
    return uint.TryParse(s, out offset);
  }

  // The `maxon build` debug-info tri-state: null = not mentioned on the CLI (fall back to the
  // build.maxon config, then the on-by-default), true = --debug-info, false = --no-debug-info.
  // Only `maxon build` acts on it (RunBuild); other commands leave the compiler's own default (off),
  // so spec-test and internal run/build-runner compiles never pay for or emit a sidecar.
  static bool? _debugInfoOverride;

  /// The build flag that turns on coverage instrumentation. Spelled once here and referenced by the
  /// usage text, the parser, and the `coverage` command's refusals, so a rename cannot leave one of
  /// them telling the user to pass something that no longer exists.
  internal const string CoverageFlag = "--coverage";

  static (bool emitIr, bool dumpStages, bool valid) ParseOptions(string[] args, HashSet<string>? additionalOptions = null) {
    var emitIr = false;
    var dumpStages = false;
    _debugInfoOverride = null;

    foreach (var arg in args) {
      if (arg == "--emit-ir") {
        emitIr = true;
      } else if (arg == "--dump-stages") {
        dumpStages = true;
      } else if (arg == "--mm-trace") {
        Compiler.Compiler.MmTrace = true;
      } else if (arg == "--mm-trace-raw") {
        Compiler.Compiler.MmTrace = true;
        Compiler.Compiler.MmTraceRawOnly = true;
      } else if (arg == "--mm-debug") {
        Compiler.Compiler.MmDebug = true;
      } else if (arg == "--literal-coverage") {
        Compiler.Compiler.LiteralCoverage = true;
      } else if (arg == "--async-trace") {
        Compiler.Compiler.AsyncTrace = true;
      } else if (arg == "--debugstream") {
        Compiler.Compiler.DebugStream = true;
      } else if (arg == CoverageFlag) {
        Compiler.Compiler.Coverage = true;
      } else if (arg == "--debug-info") {
        _debugInfoOverride = true;
      } else if (arg == "--no-debug-info") {
        _debugInfoOverride = false;
      } else if (arg == "--no-debug-agent") {
        Compiler.Compiler.NoDebugAgent = true;
      } else if (arg == "--timing") {
        Compiler.StageTimer.Enabled = true;
      } else if (arg.StartsWith("--timing-functions=")) {
        if (int.TryParse(arg["--timing-functions=".Length..], out var n) && n > 0) {
          Compiler.StageTimer.Enabled = true;
          Compiler.StageTimer.HotFunctions = n;
        }
      } else if (arg.StartsWith(TargetFlag)) {
        // Recognized as first-class option; parsed individually in each command
      } else if (arg.StartsWith("--log=")) {
        if (!Logger.ParseOption(arg["--log=".Length..])) {
          return (false, false, false);
        }
      } else if (arg.StartsWith('-')) {
        var recognized = false;
        if (additionalOptions != null) {
          foreach (var opt in additionalOptions) {
            if (opt.EndsWith('=') ? arg.StartsWith(opt) : arg == opt) {
              recognized = true;
              break;
            }
          }
        }
        if (!recognized) {
          return (false, false, false);
        }
      }
    }

    return (emitIr, dumpStages, true);
  }

  /// <summary>
  /// The target this invocation compiles for, or null if the compiler refused it — in which case the
  /// diagnostic has already been printed and the caller need only fail.
  ///
  /// <para>The DEFAULT is put through the same refusal as an explicit `--target=`, and that half is
  /// not redundant: the default is the HOST, which on a Linux box is `x64-linux`, so a plain
  /// `maxon build` there used to emit a Windows PE with no flag involved at all.</para>
  ///
  /// <para>It refuses HERE rather than leaving it to <see cref="Compiler.Compiler.Compile"/> because
  /// two things happen first and neither survives an unsupported target: the output file's extension
  /// is chosen from the OS (which used to die on an unknown one with an unhandled ArgumentException
  /// and a stack trace), and the build cache is consulted — a cached binary must not be handed back
  /// for a request that should have been refused.</para>
  /// </summary>
  static Compiler.CompileTarget? ParseTarget(string[] args) {
    try {
      foreach (var arg in args) {
        if (arg.StartsWith(TargetFlag)) {
          return Compiler.CompileTarget.Parse(arg[TargetFlag.Length..]);
        }
      }
    } catch (ArgumentException ex) {
      Console.Error.WriteLine(ex.Message);
      return null;
    }

    var host = Compiler.CompileTarget.Default;
    if (host.Unsupported is { } unsupported) {
      Console.Error.WriteLine(unsupported.Format());
      return null;
    }

    return host;
  }

  /// <summary>
  /// The extension an executable carries on this target's OS.
  ///
  /// <para>Only the OSes <see cref="Compiler.CompileTarget.SupportedTriples"/> lists can reach here:
  /// a target with no object writer is refused at <see cref="ParseTarget"/>, above every caller. The
  /// throwing arm is therefore an assertion that the two agree, and it must stay loud — a target
  /// added to the roster and forgotten here must fail, not quietly acquire an empty extension.</para>
  /// </summary>
  internal static string GetOutputExtension(Compiler.CompileTarget target) {
    return target.Os.ToLowerInvariant() switch {
      Compiler.CompileTarget.WindowsOs => ".exe",
      Compiler.CompileTarget.MacosOs => "",
      var unknown => throw new ArgumentException(
        $"No executable extension is recorded for OS '{unknown}'; it is not one of the targets this compiler writes ({Compiler.CompileTarget.SupportedTriples}).")
    };
  }

  /// <summary>
  /// Gate a `maxon build` of a project that contains a GENERATED error-code registry
  /// (maxon-shv2, maxon-selfhosted) on `error-codes check`. Projects that hold no
  /// generated file — maxon-dev-mcp, a user's program — are not gated: there is nothing
  /// in them for the registry to have drifted from.
  ///
  /// A project outside any Maxon checkout is not gated either. That is not a hole: with
  /// no docs/error-codes.txt above it, it cannot contain a file generated from one.
  /// </summary>
  static int CheckErrorCodeRegistryFor(string projectDir) {
    var root = Compiler.ErrorCodeRegistry.FindRootOrNull(Path.GetFullPath(projectDir));
    if (root == null) return 0;
    if (!Compiler.ErrorCodeRegistry.ConsumesGeneratedRegistry(root, projectDir)) return 0;

    return Compiler.ErrorCodeRegistry.Check(root);
  }

  static int RunBuild(string[] args) {
    var (emitIr, dumpStages, valid) = ParseOptions(args);
    if (!valid) return Fail();

    if (ParseTarget(args) is not { } target) return 1;
    var path = GetNonOptionArg(args) ?? Directory.GetCurrentDirectory();

    // The debug-info sidecar is ON BY DEFAULT for `maxon build`; --no-debug-info opts out, and a
    // project's build.maxon can opt out via debug_info:false (resolved below in the build.maxon
    // path, once its config is known). Because the exe is byte-identical whether or not the sidecar
    // is written, this never changes generated code — and the sidecar does NOT bypass the build
    // cache: BuildCache treats an existing sidecar as valid and only misses when one is wanted but
    // absent. So the cache is disabled only for the IR/stage-dump artifacts, as before.
    var useCache = !emitIr && !dumpStages;
    Compiler.Compiler.DebugInfo = _debugInfoOverride ?? true;
    if (RefusedOptionCombination()) return 1;

    if (File.Exists(path)) {
      // Single file: compile directly
      var content = ReadFileContentUntilSeparator(path);
      var ext = GetOutputExtension(target);
      var outputPath = ResolveOutputPath(path, ext);
      var projectDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
      // Single-file build: anchor at the file's parent dir (decision #3).
      var fileSources = new SourceFile[] { new(path, content, projectDir) };

      if (useCache && BuildCache.IsCacheValid(projectDir, fileSources, outputPath, target)) {
        Console.WriteLine($"Compiled -> {outputPath}");
        return 0;
      }

      // Captured BEFORE the compile — see BuildCache.CaptureInputs for why after is a false green.
      var capturedInputs = BuildCache.CaptureInputs(fileSources, target);
      var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(path, emitIr, dumpStages);
      var result = CompileAndReportResult(fileSources, outputPath, irOutputPath, dumpStagesBasePath, target);
      if (result == 0 && useCache) BuildCache.WriteCache(projectDir, capturedInputs, outputPath);
      return result;
    }

    if (!Directory.Exists(path)) {
      Console.Error.WriteLine($"File or directory not found: {path}");
      return 1;
    }

    // ⭐ **A PROJECT BUILD IS TREE-LEVEL, AND A SINGLE-FILE BUILD IS NOT.** From here down this writes
    // `<project>/.maxon/`, which is the checkout's own output directory and the one two concurrent
    // `maxon build maxon-shv2` runs corrupted — a 12-minute build and a silent exit 1 (see
    // `TreeLock`). The single-file arm above writes beside the file it was handed, which is a scratch
    // directory whenever it matters, so it stays free of the lock: that arm is what the spec runner
    // spawns thousands of, inside a run that already holds it.
    var buildLockExit = TreeLock.Acquire(path);
    if (buildLockExit != 0) return buildLockExit;
    try {
      return RunProjectBuild(path, target, emitIr, dumpStages, useCache);
    } finally {
      TreeLock.Release();
    }
  }

  /// <summary>
  /// The directory arm of <c>build</c>, split out so the tree lock is released through ONE exit — this
  /// body has a dozen returns, and a release written at each of them is a release the next one forgets.
  /// </summary>
  static int RunProjectBuild(string path, Compiler.CompileTarget target, bool emitIr, bool dumpStages, bool useCache) {
    // Directory: check for build.maxon with a build() function
    var buildFile = Path.Combine(path, Compiler.SourceCollector.BuildManifestFileName);
    if (File.Exists(buildFile)) {
      var buildContent = ReadFileContentUntilSeparator(buildFile);
      if (HasMainFunction(buildContent)) {
        Console.Error.WriteLine("build.maxon must not contain a main() function.");
        return 1;
      }
      var exportedFunctions = ListBuildFunctions(buildContent);
      if (exportedFunctions.Any(f => f.name == Compiler.SourceCollector.BuildFunctionName)) {
        var ext = GetOutputExtension(target);

        var projectSources = Compiler.SourceCollector.FromDirectory(path);
        if (projectSources.Length == 0) {
          Console.Error.WriteLine($"No .maxon files found in: {path}");
          return 1;
        }

        // A generated file is verified WHERE IT IS USED, not only where it is produced.
        // `dotnet build` regenerates ErrorCode.g.cs and checks the registry — but
        // `maxon build maxon-shv2` compiles maxon-shv2/Compiler/ErrorCodeRegistry.maxon,
        // which is generated too, and nothing checked it. An shv2 agent could hand-edit
        // that enum, get a green 275/0, and never trip a gate that lives in someone
        // else's build. Ahead of the cache probe: a cached build must not skip a gate.
        var registryGateResult = CheckErrorCodeRegistryFor(path);
        if (registryGateResult != 0) return registryGateResult;

        // The buildFile sits at the project root, so its RootPath is `path`.
        var allSources = new SourceFile[] { new(buildFile, buildContent, path) }.Concat(projectSources).ToArray();
        // The project cache is probed BELOW, once the build-runner yields the config: the effective
        // debug-info state (and thus whether a sidecar must be present for a hit) depends on
        // config.debug_info, unknown until then. Running the separately-cached build-runner first
        // costs one fast exe spawn on a warm build.

        // Cache build.maxon → .maxon-run.exe separately (only depends on build.maxon + compiler).
        // build.maxon is at the project root; pass `path` as its RootPath.
        var buildSources = new SourceFile[] { new(buildFile, buildContent, path) };
        BuildCache.EnsureCacheDir(path);
        var runPath = Path.Combine(BuildCache.GetCacheDir(path), $".maxon-run{ext}");

        // Build runner is an internal tool — compile it quiet, so it doesn't spew mm-trace/
        // async-trace to stderr (which would deadlock the capture pipe below and isn't useful
        // anyway) and doesn't leave a sidecar or a coverage file of its own. The list of flags that
        // means lives in ONE place; `maxon test` compiles its binary under the same rule.
        using (Compiler.InternalCompileScope.Enter()) {
          if (!(useCache && BuildCache.IsCacheValid(path, buildSources, runPath, target, cacheName: "build-runner"))) {
            // Captured BEFORE the compile — see BuildCache.CaptureInputs.
            var runnerInputs = BuildCache.CaptureInputs(buildSources, target);
            // Do not emit IR/dump-stages for the internal build-runner — those flags are for the user's project.
            var compileResult = CompileAndReportResult(buildSources, runPath, irOutputPath: null,
                dumpStagesBasePath: null, target, entryFunction: "build");
            if (compileResult != 0) return compileResult;
            if (useCache) BuildCache.WriteCache(path, runnerInputs, runPath, cacheName: "build-runner");
          }
        }

        var (exitCode, json) = RunExecutableCapture(runPath);
        if (exitCode != 0) return exitCode;

#pragma warning disable CA1869 // Cache and reuse 'JsonSerializerOptions' instances
        var config = System.Text.Json.JsonSerializer.Deserialize<BuildConfig>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
#pragma warning restore CA1869 // Cache and reuse 'JsonSerializerOptions' instances
        if (config == null) {
          Console.Error.WriteLine("build.maxon produced invalid build configuration.");
          return 1;
        }

        // Debug-info precedence: an explicit CLI flag wins; otherwise the project's build.maxon
        // decides (debug_info); otherwise on by default. Only governs whether a <output>.mxdbg is
        // written — the exe is byte-identical regardless.
        Compiler.Compiler.DebugInfo = _debugInfoOverride ?? config.Debug_info;
        if (RefusedOptionCombination()) return 1;

        // If build.maxon supplied an output path with no extension, append the
        // host-target executable extension (".exe" on Windows, none elsewhere)
        // so the same build.maxon works across platforms.
        static string AppendExtIfMissing(string p, string ext) =>
            Path.HasExtension(p) ? p : p + ext;

        string outputPath;
        if (!string.IsNullOrEmpty(config.Output)) {
          outputPath = Path.Combine(path, AppendExtIfMissing(config.Output, ext));
        } else if (!string.IsNullOrEmpty(config.Name)) {
          outputPath = Path.Combine(path, config.Name + ext);
        } else {
          outputPath = Path.Combine(path, "output" + ext);
        }

        var outputDir = Path.GetDirectoryName(outputPath);
        if (outputDir != null && !Directory.Exists(outputDir))
          Directory.CreateDirectory(outputDir);

        // Output path and effective debug-info state are now both known — probe the project cache.
        // A hit means the binary (and, when a sidecar is wanted, that sidecar) is already current.
        if (useCache && BuildCache.IsCacheValid(path, allSources, outputPath, target)) {
          Console.WriteLine($"Compiled -> {outputPath}");
          return 0;
        }

        // Captured BEFORE the compile — see BuildCache.CaptureInputs.
        var projInputs = BuildCache.CaptureInputs(allSources, target);
        var (irOut, dumpBase) = GetOutputPaths(outputPath, emitIr, dumpStages);
        var result = CompileAndReportResult(projectSources, outputPath, irOut,
            dumpBase, target);
        if (result == 0 && useCache) BuildCache.WriteCache(path, projInputs, outputPath);
        return result;
      }
    }

    // No build.maxon or no build() function: compile all files in directory
    var sources = Compiler.SourceCollector.FromDirectory(path);
    if (sources.Length == 0) {
      Console.Error.WriteLine($"No .maxon files found in: {path}");
      return 1;
    }
    var mainFile = FindMainFile(sources, path);
    {
      var ext = GetOutputExtension(target);
      var outputPath = ResolveOutputPath(mainFile, ext);

      if (useCache && BuildCache.IsCacheValid(path, sources, outputPath, target)) {
        Console.WriteLine($"Compiled -> {outputPath}");
        return 0;
      }

      // Captured BEFORE the compile — see BuildCache.CaptureInputs.
      var dirInputs = BuildCache.CaptureInputs(sources, target);
      var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(mainFile, emitIr, dumpStages);
      var result = CompileAndReportResult(sources, outputPath, irOutputPath, dumpStagesBasePath, target);
      if (result == 0 && useCache) BuildCache.WriteCache(path, dirInputs, outputPath);
      return result;
    }
  }

  record BuildConfig {
    public string? Name { get; init; }
    public string? Output { get; init; }
    public string[]? Sources { get; init; }
    public bool Optimize { get; init; }
    public bool Debug_info { get; init; }
  }

  static (int exitCode, string stdout) RunExecutableCapture(string executablePath) {
    var process = new Process {
      StartInfo = new ProcessStartInfo {
        FileName = Path.GetFullPath(executablePath),
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
      }
    };

    process.Start();

    // Read stderr asynchronously to avoid deadlock when the child writes
    // more than the pipe buffer can hold on both stdout and stderr.
    var stderrTask = process.StandardError.ReadToEndAsync();
    var stdout = process.StandardOutput.ReadToEnd();

    process.WaitForExit();
    // Process has exited so the task is complete — .Result won't block.
#pragma warning disable VSTHRD002
    var stderr = stderrTask.Result;
#pragma warning restore VSTHRD002

    if (!string.IsNullOrEmpty(stderr)) {
      Console.Error.Write(stderr);
    }

    return (process.ExitCode, stdout);
  }

  static int RunRun(string[] args) {
    // Split args at the function name: anything before is for the compiler (build flags),
    // anything from the function name onward is forwarded to the runner executable.
    var splitIndex = GetNonOptionArgIndex(args);
    var buildArgs = splitIndex < 0 ? args : args[..splitIndex];
    var forwardedArgs = splitIndex < 0 ? [] : args[(splitIndex + 1)..];

    var (emitIr, dumpStages, valid) = ParseOptions(buildArgs);
    if (!valid) return Fail();

    if (ParseTarget(buildArgs) is not { } target) return 1;
    var cliName = splitIndex < 0 ? null : args[splitIndex];
    // Translate dashes to underscores so CLI uses dashes but Maxon uses underscores
    var functionName = cliName?.Replace('-', '_');

    var directory = Directory.GetCurrentDirectory();
    var buildFile = Path.Combine(directory, Compiler.SourceCollector.BuildManifestFileName);
    if (!File.Exists(buildFile)) {
      Console.Error.WriteLine("No build.maxon found in current directory.");
      return 1;
    }

    var content = ReadFileContentUntilSeparator(buildFile);

    if (HasMainFunction(content)) {
      Console.Error.WriteLine("build.maxon must not contain a main() function.");
      return 1;
    }

    var exportedFunctions = ListBuildFunctions(content);

    if (functionName == null) {
      if (exportedFunctions.Count == 0) {
        Console.Error.WriteLine("No exported functions found in build.maxon.");
      } else {
        PrintAvailableCommands(exportedFunctions);
      }
      return 1;
    }

    // Validate that the requested function exists before compiling
    var allFunctions = ListBuildFunctions(content, exportedOnly: false);
    var isKnown = allFunctions.Any(f => f.name == functionName);
    var isExported = exportedFunctions.Any(f => f.name == functionName);

    if (!isKnown) {
      Console.Error.WriteLine($"Unknown command '{cliName}'.");
      if (exportedFunctions.Count > 0) {
        Console.Error.WriteLine();
        PrintAvailableCommands(exportedFunctions, Console.Error);
      }
      return 1;
    }

    if (!isExported) {
      Console.Error.WriteLine($"Function '{cliName}' is not exported in build.maxon.");
      return 1;
    }

    // build.maxon is at the project root; pass `directory` as its RootPath.
    var sources = new SourceFile[] { new(buildFile, content, directory) };

    var ext = GetOutputExtension(target);
    // The `maxon run` artifact is internal, so no debug-info sidecar is produced here (only
    // `maxon build` turns it on). The cache is disabled only for the IR/stage-dump artifacts.
    var useCache = !emitIr && !dumpStages;
    BuildCache.EnsureCacheDir(directory);
    var outputPath = Path.Combine(BuildCache.GetCacheDir(directory), $".maxon-run-{functionName}{ext}");
    var cacheName = $"run-{functionName}";

    if (useCache && BuildCache.IsCacheValid(directory, sources, outputPath, target, cacheName: cacheName)) {
      Console.WriteLine($"Using cached build runner for '{cliName}'");
    } else {
      // Captured BEFORE the compile — see BuildCache.CaptureInputs.
      var runInputs = BuildCache.CaptureInputs(sources, target);
      var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(buildFile, emitIr, dumpStages);
      var compileResult = CompileAndReportResult(sources, outputPath, irOutputPath,
          dumpStagesBasePath, target, entryFunction: functionName);
      if (compileResult != 0) return compileResult;
      if (useCache) BuildCache.WriteCache(directory, runInputs, outputPath, cacheName: cacheName);
    }

    return RunExecutable(outputPath, forwardedArgs);
  }

  /// Enumerates the .maxon files under `root`, never crossing into a SEPARATE CHECKOUT.
  ///
  /// A directory holding `.git` is its own checkout — a nested clone, or (as under
  /// `.claude/worktrees/`) a git worktree carrying its own uncommitted state. `fmt` rewrites in
  /// place, so descending through that boundary edits files the caller never named and cannot see
  /// in their own `git status`: one accidental whole-tree run rewrote 92 files across two agent
  /// worktrees. The traversal ROOT is deliberately exempt — every checkout contains `.git`, so
  /// pruning it there would make `maxon fmt` inside a repo format nothing at all.
  static IEnumerable<string> EnumerateFormattableFiles(string root) {
    var pending = new Stack<string>();
    pending.Push(root);
    while (pending.Count > 0) {
      var dir = pending.Pop();
      foreach (var sub in Directory.GetDirectories(dir).OrderByDescending(d => d)) {
        var dotGit = Path.Combine(sub, ".git");
        if (Directory.Exists(dotGit) || File.Exists(dotGit)) continue;
        pending.Push(sub);
      }
      foreach (var file in Directory.GetFiles(dir, "*.maxon").OrderBy(f => f)) yield return file;
    }
  }

  static int RunFmt(string[] args) {
    // `fmt` takes no flags. Silently DROPPING an unrecognized one is the most destructive thing it
    // could do: `maxon fmt --check` would find no path, fall back to the current directory, and
    // rewrite the whole tree in place — the opposite of what the flag asked for. Reject instead.
    var unknownFlag = args.FirstOrDefault(a => a.StartsWith('-'));
    if (unknownFlag != null) {
      Console.Error.WriteLine($"fmt: unrecognized option: {unknownFlag}");
      Console.Error.WriteLine("usage: maxon fmt [<file|directory>]   (formats in place; no options)");
      return 1;
    }

    if (args.Length > 1) {
      Console.Error.WriteLine($"fmt: expected at most one path, got {args.Length}: {string.Join(", ", args)}");
      return 1;
    }

    var path = args.FirstOrDefault() ?? Directory.GetCurrentDirectory();

    List<string> files;
    if (File.Exists(path)) {
      files = [path];
    } else if (Directory.Exists(path)) {
      files = [.. EnumerateFormattableFiles(path)
        .Where(f => !Path.GetFileName(f).Equals(Compiler.SourceCollector.BuildManifestFileName, StringComparison.OrdinalIgnoreCase))
        .Where(f => !MaxonIgnore.IsIgnored(f))];
    } else {
      Console.Error.WriteLine($"fmt: path not found: {path}");
      return 1;
    }

    int changed = 0;
    foreach (var file in files) {
      var original = File.ReadAllText(file);
      var formatted = Lsp.MaxonFormatter.Format(original);
      if (formatted != original) {
        File.WriteAllText(file, formatted);
        Console.WriteLine($"formatted: {file}");
        changed++;
      }
    }

    Console.WriteLine($"fmt: {changed} file(s) changed, {files.Count - changed} unchanged.");
    return 0;
  }

  /// <summary>
  /// Prints the "Available commands (from build.maxon):" listing produced by
  /// <see cref="ListBuildFunctions"/>. Underscores in function names are
  /// rewritten to dashes for the user-facing display.
  /// </summary>
  static void PrintAvailableCommands(List<(string name, string? comment)> functions, TextWriter? writer = null) {
    writer ??= Console.Out;
    writer.WriteLine("Available commands (from build.maxon):");
    writer.WriteLine();
    foreach (var (name, comment) in functions) {
      var displayName = name.Replace('_', '-');
      if (comment != null)
        writer.WriteLine($"  {displayName,-24}{comment}");
      else
        writer.WriteLine($"  {displayName}");
    }
  }

  static bool HasMainFunction(string content) {
    foreach (var rawLine in content.Split('\n')) {
      var line = rawLine.Trim();
      if (line.StartsWith("function main(") || line.StartsWith("export function main("))
        return true;
    }
    return false;
  }

  /// <summary>
  /// Extracts top-level function names from build.maxon content.
  /// Returns (name, comment) pairs. `comment` is built from the contiguous
  /// block of `///` doc-comment lines immediately preceding the function
  /// declaration (joined with single spaces). Plain `//` comments are
  /// treated as authoring notes and ignored — only `///` flows through to
  /// the CLI help output.
  /// </summary>
  static List<(string name, string? comment)> ListBuildFunctions(string content, bool exportedOnly = true) {
    var results = new List<(string name, string? comment)>();
    var lines = content.Split('\n');
    var docBuffer = new List<string>();

    foreach (var rawLine in lines) {
      var line = rawLine.Trim();
      if (line.StartsWith("///")) {
        docBuffer.Add(line[3..].Trim());
      } else if (line.StartsWith("//")) {
        // Plain `//` is for in-source notes, not user-facing help. It
        // neither contributes to the doc buffer nor clears it — that way a
        // mixed block (`/// summary` then a `// implementation note`) still
        // shows the `///` line at the top.
        continue;
      } else if (line.StartsWith("export function ") || (!exportedOnly && line.StartsWith("function "))) {
        var rest = line.StartsWith("export function ")
          ? line["export function ".Length..]
          : line["function ".Length..];
        var parenIndex = rest.IndexOf('(');
        if (parenIndex > 0) {
          var name = rest[..parenIndex].Trim();
          var comment = docBuffer.Count == 0 ? null : string.Join(' ', docBuffer);
          results.Add((name, comment));
        }
        docBuffer.Clear();
      } else if (line.Length > 0) {
        docBuffer.Clear();
      }
    }

    return results;
  }

  /// <summary>
  /// Reads file content up to the first "---" separator line. Delegates to
  /// <see cref="Compiler.SourceCollector.ReadUpToSeparator"/> for shared logic.
  /// </summary>
  static string ReadFileContentUntilSeparator(string filePath) =>
    Compiler.SourceCollector.ReadUpToSeparator(File.ReadAllText(filePath));

  /// <summary>
  /// Finds the main file (containing main function) or uses the originally specified file.
  /// </summary>
  static string FindMainFile(SourceFile[] files, string originalPath) {
    if (File.Exists(originalPath))
      return originalPath;

    foreach (var file in files) {
      if (file.Content.Contains("function main"))
        return file.Path;
    }

    foreach (var file in files) {
      if (Path.GetFileName(file.Path).Equals("main.maxon", StringComparison.OrdinalIgnoreCase))
        return file.Path;
    }

    return files.Length > 0 ? files[0].Path : originalPath;
  }

  internal static string ResolveOutputPath(string mainFile, string ext) {
    return Path.ChangeExtension(mainFile, ext == "" ? null : ext);
  }

  /// <summary>
  /// Gets the first non-option argument from the args array.
  /// </summary>
  static string? GetNonOptionArg(string[] args) {
    var index = GetNonOptionArgIndex(args);
    return index < 0 ? null : args[index];
  }

  static int GetNonOptionArgIndex(string[] args) {
    for (var i = 0; i < args.Length; i++) {
      if (!args[i].StartsWith('-')) return i;
    }
    return -1;
  }

  /// <summary>
  /// Gets output paths for IR and dump stages based on flags.
  /// </summary>
  static (string? irOutputPath, string? dumpStagesBasePath) GetOutputPaths(string mainFile, bool emitIr, bool dumpStages) {
    string? irOutputPath = null;
    if (emitIr) {
      irOutputPath = Path.ChangeExtension(mainFile, Compiler.IrPipeline.SidecarExtension);
    }

    string? dumpStagesBasePath = null;
    if (dumpStages) {
      dumpStagesBasePath = Path.ChangeExtension(mainFile, null);
    }

    return (irOutputPath, dumpStagesBasePath);
  }

  /// <summary>
  /// Compiles source files and reports the result.
  /// </summary>
  static int CompileAndReportResult(SourceFile[] sources, string outputPath, string? irOutputPath, string? dumpStagesBasePath, Compiler.CompileTarget? target = null, string entryFunction = "main") {
    var result = new Compiler.Compiler().Compile(sources, outputPath, irOutputPath, dumpStagesBasePath: dumpStagesBasePath, target: target, entryFunction: entryFunction);
    if (!result.Success) {
      foreach (var error in result.Errors)
        Logger.Error(LogCategory.Compiler, error.Format());
    }
    return result.Success ? 0 : 1;
  }

  /// <summary>
  /// Runs a compiled executable and returns its exit code.
  /// </summary>
  static int RunExecutable(string executablePath, string[]? forwardedArgs = null) {
    var startInfo = new ProcessStartInfo {
      FileName = Path.GetFullPath(executablePath),
      UseShellExecute = false,
    };
    if (forwardedArgs != null) {
      foreach (var arg in forwardedArgs) startInfo.ArgumentList.Add(arg);
    }
    var process = new Process { StartInfo = startInfo };

    process.Start();
    process.WaitForExit();

    return process.ExitCode;
  }

  static int RunSpecTests(string[] args) {
    SetupTestLogging();

    var specTestOptions = new HashSet<string> { "--filter=", "--workers=", "--update-required", TargetFlag, "--verbose", "--no-batch", "--network", DebugInfoSpecTestFlag };
    var (_, _, valid) = ParseOptions(args, specTestOptions);
    if (!valid) return Fail();

    string? filter = null;
    int? workers = null;
    bool updateRequired = false;
    bool verbose = false;
    bool noBatch = false;
    bool includeNetwork = false;
    bool debugInfo = false;

    // Through the same door as `build` and `run`, rather than a second `--target=` reader here: the
    // suite must refuse a target the compiler cannot write for exactly the reason a build does, and
    // this loop's own copy neither refused one nor caught the malformed-triple exception.
    if (ParseTarget(args) is not { } target) return 1;

    foreach (var arg in args) {
      if (arg.StartsWith("--filter=")) {
        filter = arg["--filter=".Length..];
      } else if (arg.StartsWith("--workers=")) {
        if (int.TryParse(arg["--workers=".Length..], out var w)) {
          workers = w;
        } else {
          return Fail();
        }
      } else if (arg == "--update-required") {
        updateRequired = true;
      } else if (arg == "--verbose") {
        verbose = true;
      } else if (arg == "--no-batch") {
        noBatch = true;
      } else if (arg == "--network") {
        includeNetwork = true;
      } else if (arg == DebugInfoSpecTestFlag) {
        debugInfo = true;
      }
    }

    // Process-wide because the flag it drives (`Compiler.DebugInfo`) is [ThreadStatic] and the runner
    // compiles on worker threads, so the value has to be reachable from the static that (re)sets those
    // flags on every compile. Set here, once, before any runner exists.
    Testing.TestRunner.ForceDebugInfo = debugInfo;

    // ⚖ THE MINT DOOR, refused before anything is compiled, run or written (user ruling, 2026-08-02 —
    // see TargetRunHost). `--update-required` is the only flag that rewrites goldens the tree already
    // has, and it also drives `TestRunner.UpdateRequiredInSpecFiles`, which regenerates the
    // `RequiredIR:<target>` and `maxoncstderr` blocks INSIDE `specs/*.md` from compile output alone —
    // the one mint in this compiler that no run ever validates. Refusing at the flag is what covers it;
    // there is no second check down there, because the flag is its only way in.
    if (updateRequired && Testing.TargetRunHost.MintRefusalFor(target) is { } mintRefusal) {
      Console.Error.WriteLine($"error: {TargetFlag}{target.Triple} cannot be minted here — {mintRefusal}");
      return TreeLock.NothingRanExitCode;
    }

    // ⚖ THE SAME DOOR, ONE FLAG OVER. A mint records what the compiler emits, and every committed
    // golden in this tree was minted with debug info OFF — the path `maxon build` does NOT take. That
    // the two paths emit identical bytes is exactly what `--debug-info` is here to MEASURE, so minting
    // under it would write the measurement's own subject into the reference and the gate would compare
    // a thing against itself. (The env var this flag replaced carried the rule in its doc comment —
    // "a run under it is not the run the committed goldens pin" — and could not be combined with
    // anything because no script ever set it. As a flag the combination is one word away.)
    if (updateRequired && debugInfo) {
      Console.Error.WriteLine(
        $"error: {DebugInfoSpecTestFlag} cannot mint — it compiles every test down a path the committed "
        + "goldens are not minted from, and a golden written under it would be a measurement of itself. "
        + $"Run --update-required without {DebugInfoSpecTestFlag}.");
      return TreeLock.NothingRanExitCode;
    }

    var projectDir = FindProjectRoot();
    if (projectDir == null) {
      Console.WriteLine("Could not find project root (looking for specs/ directory)");
      return 1;
    }

    var specDir = Path.Combine(projectDir, "specs");
    var fragmentDir = Path.Combine(specDir, $"fragments-{target.Triple}");
    var tempDir = Path.Combine(projectDir, "temp");

    Compiler.CompileError.ProjectRoot = projectDir;

    // The suite rewrites this checkout's goldens and stages its work under the tree's own directories,
    // so two of them in one tree race exactly as two builds do — and a build alongside one replaces
    // the compiler underneath it. One lock covers all three pairings; see `TreeLock`.
    var suiteLockExit = TreeLock.Acquire(specDir);
    if (suiteLockExit != 0) return suiteLockExit;
    try {
      return RunSpecTestSuite(specDir, fragmentDir, tempDir, projectDir, filter, workers, updateRequired, target, verbose, noBatch, includeNetwork);
    } finally {
      TreeLock.Release();
    }
  }

  static int RunSpecTestSuite(string specDir, string fragmentDir, string tempDir, string projectDir,
      string? filter, int? workers, bool updateRequired, Compiler.CompileTarget target,
      bool verbose, bool noBatch, bool includeNetwork) {
    var runner = new TestRunner(specDir, fragmentDir, tempDir, projectDir, filter, workers, updateRequired, target, verbose, noBatch, includeNetwork);
    var summary = runner.RunAllSpecTests();

    Logger.Info(LogCategory.Testing, "");

    // Nothing ran, so there are no per-test counts to print — only the reason there are none.
    if (summary.PreparationErrors > 0) {
      Logger.Error(LogCategory.Testing,
        $"Could not prepare the suite: {summary.PreparationErrors} error(s) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
      return 1;
    }

    // ⚠ THE PER-TEST REPORT IS PRINTED FIRST AND UNCONDITIONALLY, and that is not cosmetic. An
    // ungated golden used to RETURN before the summary, so a run in which eight of three thousand
    // goldens could not be written printed no counts at all — the 3242 verdicts the suite had just
    // spent two minutes producing were discarded in favour of one line about the eight. It is
    // routine on a cross-OS run, where every test with no committed golden yet trips the mint
    // refusal, and that is exactly the run whose per-test report is the whole product.
    var testExit = ReportTestResults(summary);
    if (summary.UngatedGoldens == 0) return testExit;

    Logger.Error(LogCategory.Testing,
      $"Fragment goldens: {summary.UngatedGoldens} could neither be compared nor written (listed "
      + "above). A gate that could not run is not a gate that passed.");
    return 1;
  }

  static string? FindProjectRoot() {
    // Look for specs/ directory to find project root
    var dir = Directory.GetCurrentDirectory();
    while (dir != null) {
      if (Directory.Exists(Path.Combine(dir, "specs"))) {
        return dir;
      }
      dir = Path.GetDirectoryName(dir);
    }
    return null;
  }

  /// <summary>
  /// Sets up logging for test commands (suppresses compiler Info messages).
  /// </summary>
  static void SetupTestLogging() {
    Logger.SetLevel(LogCategory.Compiler, LogLevel.Error);
  }

  /// <summary>
  /// Reports test results in a consistent format.
  ///
  /// <para>⚠ THE "not run here" COUNT IS ON THE SUMMARY LINE, not in a log. A cross-OS run compiles
  /// and gates the whole corpus but can execute none of it, and a reader who saw only "N passed"
  /// would take a partial check for a complete one — which is the exact failure the third outcome
  /// exists to prevent. The verdict itself comes from <see cref="TestSummary.IsGreen"/>, so no other
  /// reporting path can reach a different one from the same results.</para>
  /// </summary>
  static int ReportTestResults(TestSummary summary) {
    var counts = $"{summary.Passed} passed"
      + (summary.Failed > 0 ? $", {summary.Failed} failed" : "")
      + (summary.NotRunHere > 0 ? $", {summary.NotRunHere} not run here" : "");
    var line = $"Tests: {counts} (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms";

    if (summary.IsGreen) {
      Logger.Info(LogCategory.Testing, line);
      return 0;
    }

    Logger.Error(LogCategory.Testing, line);
    if (summary.NotRunHere > 0 && summary.WhyNotRunHere is { } why) {
      Logger.Error(LogCategory.Testing,
        $"  not run here: {why}. They COMPILED, and every check that needs no run — compiler errors, "
        + "RequiredIR, section pins, the committed fragment goldens — was made.");
    }

    return 1;
  }

  static async Task<int> RunLspAsync() {
    var server = new LspServer();
    await server.RunAsync();
    return 0;
  }

}
