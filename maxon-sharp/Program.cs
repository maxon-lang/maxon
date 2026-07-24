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
      "spec-test" => RunSpecTests(args[1..]),
      "error-codes" => ErrorCodeRegistry.Run(args[1..]),
      "batch-rewriter-test" => BatchRewriterTests.RunAll(),
      "mxdbg-selftest" => Debug.MxdbgSelfTest.Run(),
      "debug" => RunDebug(args[1..]),
      "lsp-server" => await RunLspAsync(),
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
    Console.WriteLine("  spec-test [options]      Run spec tests");
    Console.WriteLine("  error-codes <check|generate>");
    Console.WriteLine("                           Verify or regenerate the error-code registry");
    Console.WriteLine("  lsp-server               Start language server (LSP)");
    Console.WriteLine();
    Console.WriteLine("Build options (build, run):");
    Console.WriteLine("  --target=ARCH-OS         Set compilation target (default: x64-windows)");
    Console.WriteLine("                           Examples: x64-windows, arm64-macos, x64-linux");
    Console.WriteLine("  --emit-ir                Write .ir file");
    Console.WriteLine("  --dump-stages            Write IR at each pipeline stage (.1-maxon.ir, etc.)");
    Console.WriteLine("  --mm-trace               Enable runtime memory manager trace output (stderr)");
    Console.WriteLine("  --mm-debug               Enable runtime memory debug checks (magic, canary, poison)");
    Console.WriteLine("  --literal-coverage       Report static-eligibility of managed literal sites to stderr (measurement only)");
    Console.WriteLine("  --async-trace            Enable async/await runtime trace output (stderr)");
    Console.WriteLine("  --debugstream            Enable shared-memory debug stream (use with 'maxon monitor')");
    Console.WriteLine("  --debug-info             Force-write the <output>.mxdbg debug-info sidecar (on by default; exe stays byte-identical)");
    Console.WriteLine("  --no-debug-info          Do not write the <output>.mxdbg debug-info sidecar");
    Console.WriteLine("  --no-debug-agent         Omit the in-process debug agent entirely (hardened build; smaller binary)");
    Console.WriteLine("  --timing                 Print per-stage compile timings to stderr");
    Console.WriteLine("  --timing-functions=N     Also print top-N hottest functions per heavy pass (implies --timing)");
    Console.WriteLine();
    Console.WriteLine("Debugger options (debug):");
    Console.WriteLine("  --dump-info <exe|.mxdbg> Print the sidecar's files, functions, and line table");
    Console.WriteLine("  --symbolize <.mxdbg> <codeOffset...>");
    Console.WriteLine("                           Map .text code offsets to file:line:col");
    Console.WriteLine("  --attach-probe <exe>     Attach the in-process debug agent and read its handshake (P3a)");
    Console.WriteLine("  --bp-test <exe> <off>    Set a breakpoint at a code offset, run, observe the stop, continue (P3b)");
    Console.WriteLine("  <exe>                    Interactive debugging (lands in P3)");
    Console.WriteLine();
    Console.WriteLine("Spec test options:");
    Console.WriteLine("  --filter=PATTERN         Run only tests matching pattern");
    Console.WriteLine("  --workers=N              Use N worker threads (default: ProcessorCount - 2)");
    Console.WriteLine("  --update-required        Force regeneration and update RequiredIR, stderr, and mm-trace blocks");
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

  static int Fail() {
    PrintUsage();
    return 1;
  }

  /// <summary>
  /// The `maxon debug` command. P1 implements the two read-only sidecar surfaces —
  /// `--dump-info &lt;exe|.mxdbg&gt;` and `--symbolize &lt;.mxdbg&gt; &lt;codeOffset...&gt;`. The
  /// interactive REPL is a later phase (P3); a bare `maxon debug &lt;exe&gt;` says so.
  /// </summary>
  static int RunDebug(string[] args) {
    if (args.Length == 0) {
      Console.Error.WriteLine("Usage: maxon debug --dump-info <exe|.mxdbg>");
      Console.Error.WriteLine("       maxon debug --symbolize <.mxdbg> <codeOffset...>");
      Console.Error.WriteLine("       maxon debug --attach-probe <exe>");
      Console.Error.WriteLine("       maxon debug --bp-test <exe> <codeOffset>");
      return 1;
    }

    switch (args[0]) {
      case "--dump-info": {
        if (args.Length < 2) {
          Console.Error.WriteLine("maxon debug --dump-info needs a path to an executable or .mxdbg sidecar.");
          return 1;
        }
        var reader = LoadSidecarOrReport(args[1]);
        return reader == null ? 1 : DumpDebugInfo(reader, args[1]);
      }

      case "--symbolize": {
        if (args.Length < 3) {
          Console.Error.WriteLine("maxon debug --symbolize needs a .mxdbg path and at least one code offset.");
          return 1;
        }
        var reader = LoadSidecarOrReport(args[1]);
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
        return DebugAgentProbe.Run(args[1]);
      }

      case "--bp-test": {
        // P3b breakpoint driver: set a breakpoint at a raw code offset, run, observe the stop, and
        // continue to completion. The offset is a function's codeStart from `--dump-info`. The
        // symbolizing REPL (file:line -> address) is P3c.
        if (args.Length < 3) {
          Console.Error.WriteLine("maxon debug --bp-test needs an executable and a code offset "
            + "(a function's codeStart from --dump-info).");
          return 1;
        }
        if (!TryParseCodeOffset(args[2], out var bpOffset)) {
          Console.Error.WriteLine($"Not a code offset: '{args[2]}' (use decimal or 0x-prefixed hex).");
          return 1;
        }
        return DebugAgentProbe.RunBpTest(args[1], bpOffset);
      }

      default:
        if (args[0].StartsWith('-')) {
          Console.Error.WriteLine($"Unknown 'maxon debug' option: {args[0]}");
          return 1;
        }
        // A bare target path: the interactive session is a later phase.
        Console.WriteLine("Interactive debugging (breakpoints, stepping, the REPL) lands in P3.");
        Console.WriteLine("For now: 'maxon debug --dump-info <exe>' and 'maxon debug --symbolize <.mxdbg> <offset...>'.");
        return 0;
    }
  }

  /// Load a `.mxdbg` sidecar for the given path (a `.mxdbg` file, or a binary whose sidecar is
  /// `<binary>.mxdbg`). Reports a clear reason and returns null on any failure.
  static Debug.MxdbgReader? LoadSidecarOrReport(string path) {
    var sidecarPath = path.EndsWith(Debug.MxdbgFormat.SidecarExtension)
      ? path
      : path + Debug.MxdbgFormat.SidecarExtension;

    if (!File.Exists(sidecarPath)) {
      Console.Error.WriteLine($"No debug info found: '{sidecarPath}' does not exist "
        + "(build with --debug-info to produce it).");
      return null;
    }

    try {
      return new Debug.MxdbgReader(File.ReadAllBytes(sidecarPath));
    } catch (InvalidDataException ex) {
      Console.Error.WriteLine($"Cannot read '{sidecarPath}': {ex.Message}");
      return null;
    }
  }

  static int DumpDebugInfo(Debug.MxdbgReader r, string path) {
    Console.WriteLine($"Debug info: {path}");
    Console.WriteLine($"  target:   {r.Triple}");
    Console.WriteLine($"  build-id: 0x{r.BuildId:x16}");

    Console.WriteLine($"  files ({r.FileCount}):");
    for (uint i = 0; i < r.FileCount; i++) {
      Console.WriteLine($"    [{i}] {r.FileName(i)}");
    }

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

    Console.WriteLine($"  types ({r.TypeCount}):");
    for (uint i = 0; i < r.TypeCount; i++) {
      var t = r.Type(i);
      Console.WriteLine($"    [{i}] {t.Name,-28} {t.Kind}  size={t.Size} align={t.Align}  fields={t.FieldCount}");
      for (uint k = t.FieldFirst; k < t.FieldFirst + t.FieldCount; k++) {
        var fld = r.Field(k);
        Console.WriteLine($"        +0x{fld.Offset:x2}  {fld.Name,-20} : {r.TypeName(fld.TypeId)}");
      }
    }

    Console.WriteLine($"  line table ({r.LineCount}):");
    for (uint i = 0; i < r.LineCount; i++) {
      var l = r.Line(i);
      Console.WriteLine($"    0x{l.CodeOffset:x4}  {l.File}:{l.Line}:{l.Col}{FormatLineFlags(l.Flags)}");
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
      } else if (arg.StartsWith("--target=")) {
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

  static Compiler.CompileTarget ParseTarget(string[] args) {
    foreach (var arg in args) {
      if (arg.StartsWith("--target=")) {
        return Compiler.CompileTarget.Parse(arg["--target=".Length..]);
      }
    }
    return Compiler.CompileTarget.Default;
  }

  static string GetOutputExtension(Compiler.CompileTarget target) {
    return target.Os.ToLowerInvariant() switch {
      "windows" => ".exe",
      "macos" => "",
      "linux" => "",
      var unknown => throw new ArgumentException($"Unknown OS '{unknown}' for output extension. Expected windows, macos, or linux.")
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

    var target = ParseTarget(args);
    var path = GetNonOptionArg(args) ?? Directory.GetCurrentDirectory();

    // The debug-info sidecar is ON BY DEFAULT for `maxon build`; --no-debug-info opts out, and a
    // project's build.maxon can opt out via debug_info:false (resolved below in the build.maxon
    // path, once its config is known). Because the exe is byte-identical whether or not the sidecar
    // is written, this never changes generated code — and the sidecar does NOT bypass the build
    // cache: BuildCache treats an existing sidecar as valid and only misses when one is wanted but
    // absent. So the cache is disabled only for the IR/stage-dump artifacts, as before.
    var useCache = !emitIr && !dumpStages;
    Compiler.Compiler.DebugInfo = _debugInfoOverride ?? true;

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

      var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(path, emitIr, dumpStages);
      var result = CompileAndReportResult(fileSources, outputPath, irOutputPath, dumpStagesBasePath, target);
      if (result == 0 && useCache) BuildCache.WriteCache(projectDir, fileSources, outputPath, target);
      return result;
    }

    if (!Directory.Exists(path)) {
      Console.Error.WriteLine($"File or directory not found: {path}");
      return 1;
    }

    // Directory: check for build.maxon with a build() function
    var buildFile = Path.Combine(path, "build.maxon");
    if (File.Exists(buildFile)) {
      var buildContent = ReadFileContentUntilSeparator(buildFile);
      if (HasMainFunction(buildContent)) {
        Console.Error.WriteLine("build.maxon must not contain a main() function.");
        return 1;
      }
      var exportedFunctions = ListBuildFunctions(buildContent);
      if (exportedFunctions.Any(f => f.name == "build")) {
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

        // Build runner is an internal tool — compile without debug flags so it
        // doesn't spew mm-trace/async-trace to stderr (which would deadlock the
        // capture pipe and isn't useful anyway).
        var savedMmTrace = Compiler.Compiler.MmTrace;
        var savedMmDebug = Compiler.Compiler.MmDebug;
        var savedAsyncTrace = Compiler.Compiler.AsyncTrace;
        var savedDebugStream = Compiler.Compiler.DebugStream;
        var savedDebugInfo = Compiler.Compiler.DebugInfo;
        Compiler.Compiler.MmTrace = false;
        Compiler.Compiler.MmDebug = false;
        Compiler.Compiler.AsyncTrace = false;
        Compiler.Compiler.DebugStream = false;
        // No sidecar for the internal build-runner — --debug-info is for the user's project.
        Compiler.Compiler.DebugInfo = false;
        try {
          if (!(useCache && BuildCache.IsCacheValid(path, buildSources, runPath, target, name: "build-runner"))) {
            // Don't emit IR/dump-stages for the internal build-runner — those flags are for the user's project.
            var compileResult = CompileAndReportResult(buildSources, runPath, irOutputPath: null,
                dumpStagesBasePath: null, target, entryFunction: "build");
            if (compileResult != 0) return compileResult;
            if (useCache) BuildCache.WriteCache(path, buildSources, runPath, target, name: "build-runner");
          }
        } finally {
          Compiler.Compiler.MmTrace = savedMmTrace;
          Compiler.Compiler.MmDebug = savedMmDebug;
          Compiler.Compiler.AsyncTrace = savedAsyncTrace;
          Compiler.Compiler.DebugStream = savedDebugStream;
          Compiler.Compiler.DebugInfo = savedDebugInfo;
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

        var (irOut, dumpBase) = GetOutputPaths(outputPath, emitIr, dumpStages);
        var result = CompileAndReportResult(projectSources, outputPath, irOut,
            dumpBase, target);
        if (result == 0 && useCache) BuildCache.WriteCache(path, allSources, outputPath, target);
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

      var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(mainFile, emitIr, dumpStages);
      var result = CompileAndReportResult(sources, outputPath, irOutputPath, dumpStagesBasePath, target);
      if (result == 0 && useCache) BuildCache.WriteCache(path, sources, outputPath, target);
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

    var target = ParseTarget(buildArgs);
    var cliName = splitIndex < 0 ? null : args[splitIndex];
    // Translate dashes to underscores so CLI uses dashes but Maxon uses underscores
    var functionName = cliName?.Replace('-', '_');

    var directory = Directory.GetCurrentDirectory();
    var buildFile = Path.Combine(directory, "build.maxon");
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

    if (useCache && BuildCache.IsCacheValid(directory, sources, outputPath, target, name: cacheName)) {
      Console.WriteLine($"Using cached build runner for '{cliName}'");
    } else {
      var (irOutputPath, dumpStagesBasePath) = GetOutputPaths(buildFile, emitIr, dumpStages);
      var compileResult = CompileAndReportResult(sources, outputPath, irOutputPath,
          dumpStagesBasePath, target, entryFunction: functionName);
      if (compileResult != 0) return compileResult;
      if (useCache) BuildCache.WriteCache(directory, sources, outputPath, target, name: cacheName);
    }

    return RunExecutable(outputPath, forwardedArgs);
  }

  static int RunFmt(string[] args) {
    var path = args.FirstOrDefault(a => !a.StartsWith('-')) ?? Directory.GetCurrentDirectory();

    List<string> files;
    if (File.Exists(path)) {
      files = [path];
    } else if (Directory.Exists(path)) {
      files = [.. Directory.GetFiles(path, "*.maxon", SearchOption.AllDirectories)
        .Where(f => !Path.GetFileName(f).Equals("build.maxon", StringComparison.OrdinalIgnoreCase))
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

  static string ResolveOutputPath(string mainFile, string ext) {
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
      irOutputPath = Path.ChangeExtension(mainFile, ".ir");
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

    var specTestOptions = new HashSet<string> { "--filter=", "--workers=", "--update-required", "--target=", "--verbose", "--no-batch", "--network" };
    var (_, _, valid) = ParseOptions(args, specTestOptions);
    if (!valid) return Fail();

    string? filter = null;
    int? workers = null;
    bool updateRequired = false;
    bool verbose = false;
    bool noBatch = false;
    bool includeNetwork = false;
    Compiler.CompileTarget? target = null;

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
      } else if (arg.StartsWith("--target=")) {
        target = Compiler.CompileTarget.Parse(arg["--target=".Length..]);
      }
    }

    target ??= Compiler.CompileTarget.Default;

    var projectDir = FindProjectRoot();
    if (projectDir == null) {
      Console.WriteLine("Could not find project root (looking for specs/ directory)");
      return 1;
    }

    var specDir = Path.Combine(projectDir, "specs");
    var fragmentDir = Path.Combine(specDir, $"fragments-{target.Arch}-{target.Os}");
    var tempDir = Path.Combine(projectDir, "temp");

    Compiler.CompileError.ProjectRoot = projectDir;

    var runner = new TestRunner(specDir, fragmentDir, tempDir, projectDir, filter, workers, updateRequired, target, verbose, noBatch, includeNetwork);
    var summary = runner.RunAllSpecTests();

    Logger.Info(LogCategory.Testing, "");
    if (summary.FragmentGenerationErrors > 0) {
      Logger.Error(LogCategory.Testing, $"Fragment generation failed: {summary.FragmentGenerationErrors} error(s) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
      return 1;
    }

    return ReportTestResults(summary);
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
  /// </summary>
  static int ReportTestResults(TestSummary summary) {
    if (summary.Failed == 0) {
      Logger.Info(LogCategory.Testing, $"Tests: {summary.Passed} passed (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
      return 0;
    } else {
      Logger.Error(LogCategory.Testing, $"Tests: {summary.Passed} passed, {summary.Failed} failed (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
      return 1;
    }
  }

  static async Task<int> RunLspAsync() {
    var server = new LspServer();
    await server.RunAsync();
    return 0;
  }

}
