using System.Collections.Concurrent;
using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Passes;

namespace MaxonSharp.Compiler;

/// <summary>
/// The object-file format an executable is written in.
///
/// <para>It is a property of the OPERATING SYSTEM, not of the architecture — and that distinction is
/// why this type exists. The writer dispatch used to key on <see cref="CompileTarget.Arch"/> alone,
/// so `--target=x64-linux` was handed to the PE writer and produced a WINDOWS executable whose
/// stdlib had been parsed with `#if os(Linux)` taken, while `arm64-windows` produced a Mach-O named
/// `.exe`. Both exited 0.</para>
/// </summary>
public enum ObjectFormat { Pe, MachO }

public record CompileTarget(string Arch, string Os) {
  // The arch and os spellings a triple uses, named once. They are the keys of the roster below, the
  // arms of every switch over a target, and the halves of `Triple`; spelled inline they were four
  // separate copies of one vocabulary.
  public const string X64Arch = "x64";
  public const string Arm64Arch = "arm64";
  public const string WindowsOs = "windows";
  public const string MacosOs = "macos";
  public const string LinuxOs = "linux";

  /// <summary>
  /// Every (arch, os) pair this compiler can WRITE an executable for, and the object format it
  /// writes for it.
  ///
  /// <para>The roster is THIS IMPLEMENTATION's, not the language's: maxon-sharp ships a PE writer
  /// and a Mach-O writer and nothing else — there is no ELF writer anywhere in it — so a pair absent
  /// here has no honest binary to produce. maxon-shv2 is where the real ELF and wasm backends live.
  /// </para>
  ///
  /// <para>⭐ It is read by the writer dispatch, by <see cref="Unsupported"/>'s diagnostic AND by
  /// `--target`'s help text, so what the compiler ADVERTISES cannot outrun what it can EMIT. It had:
  /// the usage text offered `x64-linux` as an example of a target that worked.</para>
  /// </summary>
  private static readonly Dictionary<(string Arch, string Os), ObjectFormat> SupportedTargets = new() {
    [(X64Arch, WindowsOs)] = ObjectFormat.Pe,
    [(Arm64Arch, MacosOs)] = ObjectFormat.MachO,
  };

  /// The roster as targets, for the callers that must ENUMERATE it rather than ask about one target
  /// — the self-test that checks each one's emitted object format, and the triple list below.
  public static IEnumerable<(CompileTarget Target, ObjectFormat Format)> Supported =>
    SupportedTargets.Select(entry => (new CompileTarget(entry.Key.Arch, entry.Key.Os), entry.Value));

  /// The supported triples, sorted and comma-separated — the one roster a diagnostic or a help line
  /// quotes, so neither can drift from the table it describes. Sorted rather than left in insertion
  /// order because a user-visible list must not depend on a dictionary's enumeration order.
  public static string SupportedTriples =>
    string.Join(", ", Supported.Select(supported => supported.Target.Triple).Order());

  public static CompileTarget Default => Native;

  /// <summary>
  /// The machine this compiler is RUNNING on, reported truthfully — including `linux`, which is not
  /// a target it can emit for. Saying so is the point: <see cref="Unsupported"/> then refuses a
  /// plain `maxon build` on a Linux box by name, where mapping the host to something emittable would
  /// hand back the same silently-wrong PE this roster exists to stop.
  /// </summary>
  public static CompileTarget Native {
    get {
      var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch {
        System.Runtime.InteropServices.Architecture.Arm64 => Arm64Arch,
        System.Runtime.InteropServices.Architecture.X64 => X64Arch,
        var unsupported => throw new PlatformNotSupportedException($"Unsupported architecture: {unsupported}")
      };
      var os = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
        System.Runtime.InteropServices.OSPlatform.OSX) ? MacosOs :
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
          System.Runtime.InteropServices.OSPlatform.Linux) ? LinuxOs : WindowsOs;
      return new CompileTarget(arch, os);
    }
  }

  /// <summary>
  /// Maps CompileTarget.Os to the Parser's targetOs parameter value.
  ///
  /// <para>⚠ `Linux` stays, and is NOT governed by <see cref="SupportedTargets"/>. This answers a
  /// different question: which `#if os(...)` branch of the SOURCE is selected. The LANGUAGE knows
  /// three operating systems and `#if os(Linux)` is a legal predicate the parser evaluates on every
  /// target; this COMPILER can write executables for two of them. Deleting the arm would conflate
  /// "cannot emit for Linux" with "does not know what Linux is", and the stdlib's own
  /// `#if os(Windows) … #else` depends on the second being false.</para>
  /// </summary>
  public string ParserOs => Os.ToLowerInvariant() switch {
    MacosOs => "Macos",
    WindowsOs => "Windows",
    LinuxOs => "Linux",
    var unknown => throw new ArgumentException($"Unknown OS '{unknown}' in CompileTarget. Expected macos, windows, or linux.")
  };

  /// The "arch-os" triple (e.g. "x64-windows") — the inverse of <see cref="Parse"/>. Stamped into
  /// the debug-info sidecar so the driver knows which architecture's code it describes.
  public string Triple => $"{Arch}-{Os}";

  /// <summary>
  /// The object format this target's executable is written in.
  ///
  /// <para>Throwing rather than returning a default is deliberate: every entry point that can name a
  /// target refuses an unsupported one first — <see cref="Parse"/> at the `--target` flag and
  /// <see cref="Compiler.Compile"/> for a target that never passed through it — so reaching here
  /// with a pair the roster does not hold means one of those gates was removed, not that a user
  /// asked for something odd. A default here is exactly how the defect worked.</para>
  /// </summary>
  public ObjectFormat ObjectFormat => SupportedTargets.TryGetValue((Arch, Os), out var format)
    ? format
    : throw new InvalidOperationException(
        $"no object writer for '{Triple}' — an unsupported target must be refused before code emission");

  /// <summary>
  /// The one statement of "this compiler cannot write an executable for that target", as an error or
  /// null — the shape <see cref="Compiler.CoverageConflict"/> already uses, and for the same reason:
  /// the rule is needed in two kinds of place and must read identically in both.
  ///
  /// <para><see cref="Parse"/> asks it at the `--target` flag, so a refusal lands before the output
  /// path's extension is chosen and before the build cache is consulted — a cached binary must not
  /// be handed back for a request that should have been refused. <see cref="Compiler.Compile"/> asks
  /// it again, because a CompileTarget also arrives WITHOUT passing through Parse:
  /// <see cref="Native"/> on a Linux host is exactly that, and it is how a plain `maxon build` there
  /// produced a Windows PE.</para>
  /// </summary>
  public CompileError? Unsupported => SupportedTargets.ContainsKey((Arch, Os)) ? null : new CompileError(
    ErrorCode.CodeEmitterUnsupportedTarget,
    $"this compiler cannot write an executable for target '{Triple}'. It supports: {SupportedTriples}. "
    + "maxon-sharp ships a PE writer and a Mach-O writer and no others, so any other target would be "
    + "emitted in some other platform's object format while carrying the requested platform's stdlib "
    + $"semantics — a Windows PE for {X64Arch}-{LinuxOs}, a Mach-O for {Arm64Arch}-{WindowsOs}. "
    + "maxon-shv2 has the ELF and wasm backends; build with it for those targets.");

  /// <summary>
  /// Parses a target triple string like "arm64-macos" into a CompileTarget, refusing one this
  /// compiler cannot write.
  ///
  /// <para>ArgumentException for both refusals because that is already this method's contract for
  /// "not a usable triple", and `maxon test` catches exactly it to turn into a usage message.</para>
  /// </summary>
  public static CompileTarget Parse(string triple) {
    var parts = triple.Split('-', 2);
    if (parts.Length != 2)
      throw new ArgumentException($"Invalid target format '{triple}'. Expected 'arch-os' (e.g., 'arm64-macos').");

    var target = new CompileTarget(parts[0], parts[1]);
    if (target.Unsupported is { } unsupported) throw new ArgumentException(unsupported.Format());

    return target;
  }
}

public record CompileResult(
  bool Success,
  List<CompileError> Errors,
  string? AllStagesIr = null
) {
  /// <summary>
  /// The architecture-specific stage IR (x86 or arm64) — the last stage in <see cref="AllStagesIr"/>.
  /// </summary>
  public string? ArchIr => AllStagesIr == null ? null : PipelineStages.LastSectionBody(AllStagesIr);
};

public class Compiler {
  private readonly IrContext _context = new();

  [ThreadStatic] private static bool _mmTrace;
  public static bool MmTrace { get => _mmTrace; set => _mmTrace = value; }

  // Raw-only trace: when set (with MmTrace), suppress the managed alloc/incref/
  // decref/free/realloc trace lines and emit ONLY mm_raw_alloc/mm_raw_free, so a
  // leak hunt for untagged raw buffers produces a tiny, fast trace instead of the
  // full refcount firehose.
  [ThreadStatic] private static bool _mmTraceRawOnly;
  public static bool MmTraceRawOnly { get => _mmTraceRawOnly; set => _mmTraceRawOnly = value; }

  [ThreadStatic] private static bool _mmDebug;
  public static bool MmDebug { get => _mmDebug; set => _mmDebug = value; }

  [ThreadStatic] private static bool _asyncTrace;
  public static bool AsyncTrace { get => _asyncTrace; set => _asyncTrace = value; }

  [ThreadStatic] private static bool _debugStream;
  public static bool DebugStream { get => _debugStream; set => _debugStream = value; }

  // When set (via --debug-info / BuildConfig.debug_info), the compiler CAPTURES source spans through
  // the pipeline and writes a `<output>.mxdbg` sidecar next to the binary. It gates ONLY observation
  // and the sidecar file — never a single emitted code byte. A build with this on and one with it off
  // produce byte-identical executables (see docs/DEBUGGER_DESIGN.md). Off, nothing is captured and no
  // sidecar is written.
  [ThreadStatic] private static bool _debugInfo;
  public static bool DebugInfo { get => _debugInfo; set => _debugInfo = value; }

  // The dormant in-process debug agent (the `__dbg_*` runtime family) is emitted into EVERY binary
  // by default — always present, dark until MAXON_DEBUG is set (see docs/DEBUGGER_DESIGN.md Part 3).
  // Set (via --no-debug-agent) it is omitted entirely: the ONE sanctioned case where two binaries
  // differ, for hardened deployments that refuse an env-activated debug-control channel. Because the
  // agent is emitted regardless of --debug-info, --debug-info vs --no-debug-info stay byte-identical.
  [ThreadStatic] private static bool _noDebugAgent;
  public static bool NoDebugAgent { get => _noDebugAgent; set => _noDebugAgent = value; }

  // When set (via --coverage), the parser mints a coverage point per user statement and per `if` arm,
  // the emitted code increments a counter at each, and the program writes a `<binary>.mxcov` counter
  // file when it exits. Unlike --debug-info this CHANGES THE EMITTED BYTES — it is an instrumented
  // build variant, in the same family as --debugstream, and like it must key the build cache.
  //
  // It REQUIRES --debug-info (the default): the counters are anonymous without the sidecar's
  // coverage-point table to interpret them, so `--coverage --no-debug-info` is refused by name rather
  // than producing a data file nothing can read.
  [ThreadStatic] private static bool _coverage;
  public static bool Coverage { get => _coverage; set => _coverage = value; }

  [ThreadStatic] private static bool _testing;
  public static bool Testing { get => _testing; set => _testing = value; }

  // When set, LiteralCoverageAnalysisPass ALSO prints its coverage report to stderr. Only the
  // REPORT is optional: the pass itself always runs, and the lowering reads its verdict (see that
  // pass's own doc, which is where the rule is stated). So a build is byte-identical whether or not
  // this flag is set — which is what "measurement only" means here, and it is a fact about the FLAG,
  // never about the pass.
  [ThreadStatic] private static bool _literalCoverage;
  public static bool LiteralCoverage { get => _literalCoverage; set => _literalCoverage = value; }

  /// <summary>
  /// The one statement of "`--coverage` needs the sidecar", as an error or null.
  ///
  /// It is a FUNCTION rather than a check written where it is needed, because it is needed in two
  /// kinds of place: inside <see cref="Compile"/>, so no entry point can compile an invalid
  /// combination, and ahead of the BUILD CACHE, so a cached binary is not handed back for a request
  /// that should have been refused. (It was: with the cache keyed on --coverage and satisfied,
  /// `--coverage --no-debug-info` returned "Compiled ->" and exit 0 without ever reaching the
  /// compiler.) One rule, two gates.
  /// </summary>
  public static CompileError? CoverageConflict() =>
    Coverage && !DebugInfo
      ? new CompileError(ErrorCode.CodeEmitterCoverageNeedsDebugInfo,
          "--coverage requires the debug-info sidecar: the counters it emits are anonymous without the "
          + "sidecar's coverage-point table, so a .mxcov written by this binary could never be interpreted. "
          + "Drop --no-debug-info (or debug_info:false in build.maxon), or drop --coverage.")
      : null;

  /// <summary>
  /// Resets process-wide compile state that would otherwise drift across
  /// independent compiles. The CLI calls this once per invocation; the LSP
  /// calls it before every recompile. Without these resets closure/panic
  /// labels collide and the IR id counter fails to start at %0.
  ///
  /// Also seeds the stdlib-namespace counters past the cached stdlib's max id
  /// so lowering-time stdlib MaxonValues (e.g. MaxonManagedMemSliceOp.Result)
  /// don't alias parser-time stdlib MaxonValues in per-function valueMaps.
  /// Safe to call before the cached stdlib has been built — seeds with 0 in
  /// that case (the stdlib parse runs in its own context and won't collide).
  /// </summary>
  /// <param name="target">
  /// The target this compile is FOR, which decides which cached stdlib's watermarks apply. The
  /// stdlib is parsed per target (its <c>#if os(...)</c> resolve differently), so the two targets'
  /// parses reach different ids and seeding from the wrong one would under-seed the counters.
  /// </param>
  public static void ResetStaticCompileState(IrContext context, CompileTarget target) {
    context.ResetIds();
    var (maxValueId, maxStdValueId) = StdlibLoader.StdlibIdWatermarks(target);
    context.SeedStdlibCounters(maxValueId, maxStdValueId);
    MaxonPanicOp.ResetPanicLabels();
    Parser.ResetClosureCounter();
  }

  public CompileResult Compile(SourceFile[] sources, string outputPath, string? irOutputPath = null, bool returnIr = false, string? dumpStagesBasePath = null, CompileTarget? target = null, string entryFunction = "main") {
    target ??= CompileTarget.Default;
    var userSourceFile = sources.Length == 1 ? sources[0].Path : null;

    if (CoverageConflict() is { } conflict) return new CompileResult(false, [conflict]);

    // Ahead of every other step, including DiscardPreviousOutput: a target with no object writer
    // must cost nothing and must not disturb the previous build's artifacts. This is the gate that
    // sees a target which never passed through CompileTarget.Parse — the LSP's, `maxon test`'s, and
    // above all the DEFAULT, which is the host and is `x64-linux` on a Linux box.
    if (target.Unsupported is { } unsupportedTarget) return new CompileResult(false, [unsupportedTarget]);

    using var _ = _context.PushScope();

    try {
      DiscardPreviousOutput(outputPath, irOutputPath);

      var totalSw = System.Diagnostics.Stopwatch.StartNew();
      var stageSw = StageTimer.Enabled ? System.Diagnostics.Stopwatch.StartNew() : null;
      Logger.Debug(LogCategory.Compiler, "Starting compilation");

      // Stage 1-2: Lex and parse all source files into IR modules
      // Use cached stdlib module, then parse user code into a clone
      var module = StdlibLoader.GetStdlibModule(target);
      module.EntryFunctionName = entryFunction;
      // Where a `--coverage` binary will write its counters. Rooted, so the program writes the same
      // file wherever it is launched from — and so `maxon coverage` finds it beside the binary.
      module.CoverageDataPath = Coverage
        ? Path.GetFullPath(outputPath) + Debug.MxcovFormat.DataExtension : "";

      ResetStaticCompileState(_context, target);

      Dictionary<string, long>? parseTimings = StageTimer.Enabled ? [] : null;
      var parseErrors = CompileSources(module, sources, false, target, parseTimings);
      long parseMs = 0;
      if (stageSw != null) { parseMs = stageSw.ElapsedMilliseconds; stageSw.Restart(); }
      if (parseTimings != null)
        Console.Error.WriteLine("Parse:" + StageTimer.Format(parseTimings) + $" tokens={StageTimer.TokensLexed}");

      if (parseErrors.Count > 0)
        return new CompileResult(false, parseErrors);

      // Stage 3-4: IR pipeline (semantic checks + dialect lowering)
      var pipeline = new IrPipeline();
      var irResult = IrPipeline.Run(module, returnIr, dumpStagesBasePath, target);
      long pipelineMs = 0;
      if (stageSw != null) { pipelineMs = stageSw.ElapsedMilliseconds; stageSw.Restart(); }

      // Write IR if requested
      if (irOutputPath != null) {
        if (irResult.X86Module != null)
          IrPipeline.WriteIrOutput(irResult.X86Module, irOutputPath);
        else if (irResult.ARM64Module != null)
          IrPipeline.WriteIrOutput(irResult.ARM64Module, irOutputPath);
      }

      // Stage 5: Code emission. The ARCHITECTURE picks the emitter, and only the architecture — an
      // instruction encoder depends on nothing else about the target.
      var codeResult = target.Arch switch {
        CompileTarget.Arm64Arch => ARM64CodeEmitterStage.Emit(irResult.ARM64Module!),
        CompileTarget.X64Arch => X86CodeEmitter.Emit(irResult.X86Module!),
        var unsupported => throw new InvalidOperationException(
          $"no code emitter for architecture '{unsupported}': CompileTarget's roster admitted a target this switch does not handle")
      };
      long emitMs = 0;
      if (stageSw != null) { emitMs = stageSw.ElapsedMilliseconds; stageSw.Restart(); }

      // Stage 6: Write the executable. The OPERATING SYSTEM picks the object format — keying this on
      // the ARCHITECTURE is the defect it replaces: `--target=x64-linux` reached the PE writer and
      // exited 0 having produced a Windows executable carrying POSIX stdlib semantics, and
      // `arm64-windows` reached the Mach-O writer and wrote a Mach-O named `.exe`.
      //
      // The imports note is format-specific because the two formats record imports in different
      // places: a PE carries an import table, while a Mach-O reaches its through the GOT and the
      // import NAMES, so quoting `Imports.Count` for one would report 0 for a binary that has them.
      string importsNote;
      switch (target.ObjectFormat) {
        case ObjectFormat.MachO:
          MachOWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Ucddata, symdata: codeResult.Symdata, got: codeResult.Got, importNames: codeResult.ImportNames);
          importsNote = "";
          break;
        case ObjectFormat.Pe:
          PeWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Ucddata, codeResult.Imports, codeResult.Symdata);
          importsNote = $", {codeResult.Imports.Count} imports";
          break;
        default:
          throw new InvalidOperationException(
            $"no object writer for format '{target.ObjectFormat}' (target '{target.Triple}')");
      }

      WriteDebugSidecar(codeResult, outputPath, target);
      if (stageSw != null)
        Console.Error.WriteLine($"Stages: parse={parseMs}ms pipeline={pipelineMs}ms emit={emitMs}ms write={stageSw.ElapsedMilliseconds}ms");
      Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Rdata.Length} bytes rdata, {codeResult.Data.Length} bytes data, {codeResult.Ucddata.Length} bytes ucddata, {codeResult.Symdata.Length} bytes symdata{importsNote} to {outputPath} in {totalSw.ElapsedMilliseconds}ms");

      return new CompileResult(true, [], irResult.AllStagesIr);
    } catch (CompileError ex) {
      if (ex.FilePath == null && userSourceFile != null) {
        ex.FilePath = userSourceFile;
      }
      return new CompileResult(false, [ex]);
    } catch (Exception ex) {
      return new CompileResult(false, [new CompileError(ErrorCode.InternalError, $"{ex.Message}\n{ex.StackTrace}")]);
    }
  }

  /// <summary>
  /// Removes every artifact a compile of this output can publish — the executable, its `.mxdbg`
  /// debug-info sidecar, and its `.ir` sidecar — BEFORE the first step that can fail.
  ///
  /// <para>`FileMode.Create` truncates, but only when the WRITER opens the file, and the writer is
  /// the LAST step of a SUCCESSFUL compile. A lex, parse, semantic or emitter failure never reaches
  /// it, so the previous build's bytes survive untouched and go on answering. Measured: a
  /// `r.maxon` edited to call an undefined function exits 1 with a clean E2004 — and `r.exe`,
  /// `r.exe.mxdbg` and `r.ir` are all still there, and the exe still returns the OLD source's exit
  /// code. Anything that checks the BINARY rather than the build's exit code then gets a confident
  /// wrong answer from stale code.</para>
  ///
  /// <para>⭐ **THE `.ir` SIDECAR GOES UNCONDITIONALLY, NOT ONLY UNDER `--emit-ir`** — the same rule
  /// shv2's `Compiler.discardPreviousOutput` states, and the two must not disagree about it. A build
  /// that produces no sidecar must not leave the PREVIOUS one behind either, or dropping the flag
  /// silently preserves a stale answer beside a fresh exe: a mismatched pair that reads as a matched
  /// one. Measured before this was unconditional: `build --emit-ir` then a FAILING plain `build` left
  /// the first build's `.ir` sitting beside no exe at all. That is why the sidecar path is derived
  /// here from <paramref name="outputPath"/> rather than borrowed from <paramref name="irOutputPath"/>,
  /// which is null whenever the flag is off. Both are listed because they are not always the same
  /// file: `maxon run --emit-ir` writes the sidecar beside `build.maxon` while its binary goes to the
  /// cache directory.</para>
  ///
  /// <para>Absent paths are skipped, and the guard is load-bearing in a way `File.Delete`'s own
  /// tolerance is not: a missing FILE is already a silent no-op, but a missing parent DIRECTORY
  /// raises `DirectoryNotFoundException`, which the catch below would report as a locked artifact and
  /// fail a build that should have succeeded. (shv2 needs no such guard — it ignores the delete
  /// outright and re-reads existence instead, and an `exists` probe there was MEASURED to cost more
  /// than the failing delete it would save. See `maxon-shv2/Testing/ladders/README.md`.) A delete
  /// that genuinely FAILS is a build error and not a warning: the write that follows would fail for
  /// the same reason (a running or read-only exe), and a build that cannot remove the old binary must
  /// not go on to claim it replaced it.</para>
  /// </summary>
  /// <summary>
  /// Every path a compile of <paramref name="outputPath"/> can PUBLISH: the executable, its
  /// `.mxdbg` debug-info sidecar, and its `.ir` sidecar (plus the explicitly-requested
  /// <paramref name="irOutputPath"/>, which is not always the same file — `maxon run --emit-ir`
  /// writes the sidecar beside `build.maxon` while its binary goes to the cache directory).
  ///
  /// <para>THE list, because two parties need it with two different FAILURE POLICIES and they must
  /// not disagree about its CONTENT. <see cref="DiscardPreviousOutput"/> clears it before a build
  /// and fails the build if it cannot; <c>Testing.CompiledArtifact.Delete</c> clears it after a spec
  /// compile and swallows. The test-side copy listed only the first two — so the clause this
  /// method's own caller documents in capitals ("THE `.ir` SIDECAR GOES UNCONDITIONALLY") was
  /// already missing from the other copy, which aims at <c>specs/fragments-*/</c>, a COMMITTED
  /// directory. One fact, two policies.</para>
  /// </summary>
  internal static IEnumerable<string> PublishedOutputPaths(string outputPath, string? irOutputPath = null) {
    yield return outputPath;
    yield return outputPath + Debug.MxdbgFormat.SidecarExtension;
    yield return Path.ChangeExtension(outputPath, IrPipeline.SidecarExtension);

    if (irOutputPath != null) yield return irOutputPath;
  }

  private static void DiscardPreviousOutput(string outputPath, string? irOutputPath) {
    foreach (var path in PublishedOutputPaths(outputPath, irOutputPath)) {
      if (!File.Exists(path)) continue;

      try {
        File.Delete(path);
      } catch (Exception ex) {
        throw new CompileError(ErrorCode.BinaryOutputNotReplaceable,
          $"could not remove the previous build artifact at {path} — it is locked or read-only, so this build cannot replace it: {ex.Message}");
      }
    }
  }

  /// <summary>
  /// Writes the `<output>.mxdbg` debug-info sidecar next to the binary, when --debug-info captured it.
  /// The build-id is the FNV-1a content hash of the emitted `.text` — nothing is embedded in the exe
  /// (the driver recomputes the same hash from the binary's `.text` section later), so --debug-info
  /// adds ZERO bytes to the executable and the two builds are byte-identical.
  ///
  /// Report-and-swallow, modelled on shv2's MetricsEmit.writeMetrics: a sidecar write failure is
  /// logged and ignored, NEVER a build gate. The build has already succeeded; a debugging aid that
  /// could not be written must not fail it.
  /// </summary>
  private static void WriteDebugSidecar(CodeEmitResult codeResult, string outputPath, CompileTarget target) {
    if (codeResult.DebugInfo is not { } writer) return;

    try {
      var image = writer.Build(codeResult.BuildId, target.Triple);
      var sidecarPath = outputPath + Debug.MxdbgFormat.SidecarExtension;
      File.WriteAllBytes(sidecarPath, image);
      Logger.Info(LogCategory.Compiler, $"Wrote {image.Length} bytes debug info to {sidecarPath}");
    } catch (Exception ex) {
      Logger.Error(LogCategory.Compiler, $"Failed to write debug-info sidecar (build not affected): {ex.Message}");
    }
  }

  /// <summary>
  /// Diagnostics for one editor buffer, for the HOST target.
  ///
  /// The host is stated here rather than inherited from a default, because it is a decision: an
  /// editor session has no <c>--target</c> to offer, and the diagnostics it shows must describe the
  /// build the developer gets by typing `maxon build`, which is the host's.
  /// </summary>
  public static List<CompileError> Check(string filePath, string content) {
    var target = CompileTarget.Default;
    var context = new IrContext();
    using var _ = context.PushScope();

    try {
      var stdlibSources = StdlibLoader.LoadStdlibModules();

      // If checking a stdlib file, replace its content in the stdlib sources
      var normalizedPath = Path.GetFullPath(filePath);
      var stdlibIndex = Array.FindIndex(stdlibSources,
        s => Path.GetFullPath(s.Path) == normalizedPath);

      if (stdlibIndex >= 0) {
        // Stdlib file changed - must re-parse stdlib from scratch.
        // Preserve the original SourceFile's RootPath so the replaced entry
        // retains the stdlib anchor (parent of stdlib dir).
        var module = new IrModule<MaxonOp>();
        var modifiedSources = (SourceFile[])stdlibSources.Clone();
        modifiedSources[stdlibIndex] = new SourceFile(filePath, content, modifiedSources[stdlibIndex].RootPath);
        return CompileSources(module, modifiedSources, true, target);
      } else {
        var module = StdlibLoader.GetStdlibModule(target);
        ResetStaticCompileState(context, target);
        // Single-file Check: anchor at the file's parent dir (decision #3).
        var rootPath = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return CompileSources(module, [new SourceFile(filePath, content, rootPath)], false, target);
      }
    } catch (CompileError ex) {
      ex.FilePath ??= filePath;
      return [ex];
    }
  }

  /// <summary>
  /// Every <c>test</c> declaration in <paramref name="sources"/>, in the order the parser reached
  /// them, or the errors that stopped the parse.
  ///
  /// It runs <see cref="CompileSources"/> and NOTHING ELSE, which is what makes it usable on a
  /// project that has no entry point yet: <see cref="SemanticCheckPass"/> — the pass that throws
  /// <see cref="ErrorCode.SemanticNoMain"/> — lives inside <see cref="IrPipeline.Run"/>, downstream
  /// of here. So `maxon test --list` can answer for a project whose test dispatcher has not been
  /// generated, and answer it by PARSING rather than by pattern-matching source text: a regex over
  /// `.test.maxon` would find a `test` inside a comment or a string, would miss the file rule, and
  /// would have to re-derive the name mangling the compiler already performs.
  ///
  /// <see cref="IrFunction{TOp}.DisplayName"/> is the single fact "is a test" (nothing else sets
  /// it), so this asks that question and no other — there is no second definition of what a test is
  /// for the two to disagree about.
  /// </summary>
  /// <param name="target">
  /// The target the tests will be COMPILED for, which decides what is discovered. Required rather
  /// than defaulted to the host: the parser resolves <c>#if os(...)</c> / <c>arch(...)</c>, so a
  /// test behind one of those exists for some targets and not others. Discovering for the host and
  /// building for <c>--target</c> would drop a test from the report without saying so, and emit a
  /// call to one that the real compile cannot resolve.
  /// </param>
  internal static List<CompileError> DiscoverTests(SourceFile[] sources, CompileTarget target,
      out List<DiscoveredTest> tests) {
    tests = [];
    var context = new IrContext();
    using var _ = context.PushScope();

    try {
      var module = StdlibLoader.GetStdlibModule(target);
      ResetStaticCompileState(context, target);
      var errors = CompileSources(module, sources, false, target);
      if (errors.Count > 0) return errors;

      foreach (var func in module.Functions) {
        if (func.DisplayName == null) continue;

        // A test that reached here without a source anchor could not be reported to a human — the
        // report groups by file and points at a line. The parser sets both at each of the two sites
        // that mint a test, so an absent one is a compiler bug, not a user's input.
        if (func.SourceFilePath == null || func.SourceLine == null) {
          return [new CompileError(ErrorCode.InternalError,
            $"test '{func.DisplayName}' ({func.Name}) has no source anchor; "
            + "every test is minted with SourceFilePath and SourceLine set.")];
        }

        tests.Add(new DiscoveredTest(func.Name, func.DisplayName, func.SourceFilePath, func.SourceLine.Value));
      }

      return errors;
    } catch (CompileError ex) {
      return [ex];
    }
  }

  /// <summary>
  /// Run lightweight analysis passes (parameter mutation + borrow check) on
  /// an already-parsed module. Returns any <see cref="CompileError"/>s found.
  /// Used by the LSP to surface E3070 and similar errors that the parse phase
  /// alone cannot detect, without paying the cost of the full IR pipeline.
  /// </summary>
  internal static List<CompileError> RunAnalysisPasses(IrModule<MaxonOp> module) {
    try {
      ParameterMutationAnalysisPass.Run(module);
      BorrowCheckPass.Run(module);
      return [];
    } catch (CompileError ex) {
      return [ex];
    } catch {
      return [];
    }
  }

  /// <summary>
  /// Build the parser for one source file.
  ///
  /// Every pre-scan and parse pass below needs a parser configured identically from the SAME
  /// <see cref="SourceFile"/>, and each used to spell that configuration out at its own call site —
  /// six copies of one argument list, differing only in whether a cache was threaded through. A
  /// field added to <see cref="SourceFile"/> then has six places to be forgotten in, and the pass
  /// that forgot it would simply parse the file under a different rule.
  /// </summary>
  private static Parser NewParser(List<Token> tokens, IrModule<MaxonOp> module, SourceFile source,
      bool isStdLib, string parserOs, string parserArch,
      Dictionary<string, object>? foreignPerspectiveCache = null) =>
    new(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing,
      targetOs: parserOs, targetArch: parserArch, rootPath: source.RootPath,
      foreignPerspectiveCache: foreignPerspectiveCache,
      compilerOwnedDeclarations: source.CompilerOwnedDeclarations);

  /// <param name="target">
  /// The target being built for. REQUIRED, with no host default: the parser resolves
  /// <c>#if os(...)</c> / <c>arch(...)</c> from it, so a call that omitted it silently parsed for the
  /// BUILD MACHINE. That is exactly how the stdlib came to be compiled for the host inside a
  /// cross-compile — one unstated argument, two halves of a compile disagreeing about the OS, and
  /// both halves succeeding. Every caller now has to say which target it means.
  /// </param>
  internal static List<CompileError> CompileSources(IrModule<MaxonOp> module, SourceFile[] sources, bool isStdLib, CompileTarget target, Dictionary<string, long>? timings = null) {
    var parserOs = target.ParserOs;
    var parserArch = target.Arch;
    var errors = new List<CompileError>();
    var failedFiles = new HashSet<string>();
    var sw = timings != null ? new System.Diagnostics.Stopwatch() : null;

    // Foreign top-level-constant perspectives are a pure function of the module's whole-program
    // constant declarations (settled by preScanConstDecls, below, before any fold) and a file path.
    // Share ONE cache across every parser in this compilation — both folding passes (preScan and the
    // full parse) and every file — so a given declarer's perspective is built at most once. A fresh
    // Parser is created per file per pass, so a per-parser cache rebuilt the same perspective in each,
    // making total build work super-quadratic on deep cross-file constant chains. Scoped to this call
    // (not static) so a reused module — e.g. the LSP's cached stdlib module — never serves a stale set.
    var foreignPerspectiveCache = new Dictionary<string, object>();

    // Per-source token cache. The same file is walked by up to 7 passes
    // (PreRegisterTypeNames, PreScanTypeAliasesOnly x3 — declarations, specialize, respecialize —
    // PreScanTopLevelConstantDecls, PreScan, RescanExtensions, Parse). Each pass previously re-lexed from
    // scratch; caching cuts that to one lex per file. Parsers mutate tokens
    // only during full parse (Self-type rewrite, primitive-static method
    // rewrite), which is the last pass, so the shared list is safe across
    // pre-scans. ReportLexerErrors is idempotent on an already-sanitised list.
    var tokensBySource = new Dictionary<string, List<Token>>(sources.Length);

    // When timing is on, route every lex through this Stopwatch so the "lex"
    // bucket isolates tokenization cost from whichever pre-scan happens to
    // trigger the cache miss first.
    var lexSw = timings != null ? new System.Diagnostics.Stopwatch() : null;

    List<Token> TokensFor(SourceFile source) {
      if (!tokensBySource.TryGetValue(source.Path, out var cached)) {
        if (lexSw != null) {
          lexSw.Restart();
          cached = new Lexer(source.Content).Tokenize();
          StageTimer.Record(timings!, "lex", lexSw.ElapsedMilliseconds);
          StageTimer.TokensLexed += cached.Count;
        } else {
          cached = new Lexer(source.Content).Tokenize();
        }
        tokensBySource[source.Path] = cached;
      }
      return cached;
    }

    // When timing, pre-warm the token cache so the "lex" bucket isolates
    // tokenization cost. Without this, lex time falls into whichever pre-scan
    // happens to trigger the cache miss first (typically preRegTypes).
    if (lexSw != null) {
      foreach (var source in sources) TokensFor(source);
    }

    // Pre-register type names from all sources so cross-file references resolve
    // (e.g., Character.maxon references String before String.maxon is parsed)
    sw?.Restart();
    foreach (var source in sources)
      PreRegisterTypeNames(module, source, TokensFor(source), isStdLib);
    if (sw != null) StageTimer.Record(timings!, "preRegTypes", sw.ElapsedMilliseconds);

    // Collect every file's top-level generic typealias DECLARATIONS before any file specializes one,
    // so that "does this project already have a name for this generic instance?" is answered from
    // the whole program rather than from the files read so far. Without it, RegisterConcreteTypeAlias
    // mints a structural name (`Array_ValueId`) for a field whose instance is declared one file later
    // (`ValueIdArray`), and both families of methods get emitted — which name won being decided by
    // the filesystem's enumeration order, sorted on NTFS and hash-ordered on APFS.
    //
    // This is the same whole-project two-step preScanConstDecls below is, for the same reason, and it
    // has to run FIRST for its own: classifying an alias needs its source type's `uses` clause, which
    // PreRegisterTypeNames has just settled whole-program, and nothing more.
    //
    // Per-file errors are deliberately not collected. The specialize phase immediately below walks
    // the same declarations with the same parser and reports them there, with the file marked failed;
    // reporting here as well would duplicate every typealias diagnostic in the compilation.
    //
    // A file that throws contributes the declarations it reached before throwing — a PREFIX of its
    // aliases, not nothing — and that prefix cannot reach a successful compilation: the specialize
    // phase re-walks the same file, records the same error, and CompileSources returns before the
    // full parse whenever any error was collected. So a half-indexed file is always a failed build,
    // never a build that quietly resolved an instance name from half a file.
    sw?.Restart();
    foreach (var source in sources) {
      try {
        var parser = NewParser(TokensFor(source), module, source, isStdLib, parserOs, parserArch);
        parser.PreScanTypeAliasesOnly(module, TypeAliasScanPhase.Declarations);
      } catch (CompileError) {
        // See above: the specialize phase reports it.
      }
    }
    if (sw != null) StageTimer.Record(timings!, "preScanAliasDecls", sw.ElapsedMilliseconds);

    // Pre-scan top-level typealiases from all sources so cross-file typealias
    // references resolve regardless of file processing order
    sw?.Restart();
    foreach (var source in sources) {
      try {
        var tokens = TokensFor(source);
        ReportLexerErrors(tokens, source.Path, errors);
        var parser = NewParser(tokens, module, source, isStdLib, parserOs, parserArch);
        parser.PreScanTypeAliasesOnly(module);
        // PreScanTypeAliasesOnly recovers from per-block errors (e.g. duplicate
        // enum raw value) so the rest of the file's typealiases still register.
        // Surface the recovered errors and mark the file as failed so later
        // passes don't run on a partially-parsed module.
        if (parser.Errors.Count > 0) {
          foreach (var err in parser.Errors) errors.Add(err);
          failedFiles.Add(source.Path);
        }
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
        failedFiles.Add(source.Path);
      }
    }
    if (sw != null) StageTimer.Record(timings!, "preScanAliases", sw.ElapsedMilliseconds);

    // Collect every file's top-level constant DECLARATIONS before any file folds one, so that a
    // constant initializer referencing a constant declared in another file resolves whichever
    // order the files arrive in. Without this, a file's constants are folded at the end of its own
    // pre-scan against only the values of the files pre-scanned before it, and `let A = FOREIGN`
    // compiles or fails on the strength of the filesystem's enumeration order — which is sorted on
    // NTFS and hash-ordered on APFS, so the same tree built on Windows and refused on macOS.
    //
    // Runs after the typealias pre-scan: classifying an initializer as constant-vs-runtime asks
    // whether a name is a ranged type or an enum, and that pass is what settles those whole-program.
    sw?.Restart();
    foreach (var source in sources) {
      if (failedFiles.Contains(source.Path)) continue;
      try {
        var tokens = TokensFor(source);
        var parser = NewParser(tokens, module, source, isStdLib, parserOs, parserArch);
        parser.PreScanTopLevelConstantDecls(module);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
        failedFiles.Add(source.Path);
      }
    }
    if (sw != null) StageTimer.Record(timings!, "preScanConstDecls", sw.ElapsedMilliseconds);

    // Pre-scan all sources to register function signatures, type details, etc.
    // so that cross-file forward references resolve regardless of parse order
    sw?.Restart();
    foreach (var source in sources) {
      if (failedFiles.Contains(source.Path)) continue;
      try {
        var tokens = TokensFor(source);
        var parser = NewParser(tokens, module, source, isStdLib, parserOs, parserArch, foreignPerspectiveCache);
        parser.PreScan(module);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
        failedFiles.Add(source.Path);
      }
    }
    if (sw != null) StageTimer.Record(timings!, "preScan", sw.ElapsedMilliseconds);

    // Re-scan extension blocks for files that had unresolved interface extensions
    // due to file ordering (conforming types in files not yet pre-scanned).
    // Only for non-stdlib: stdlib files are all in one CompileSources call so
    // ordering issues within stdlib are handled by the pre-scan.
    if (!isStdLib && module.DeferredExtensionFiles.Count > 0) {
      sw?.Restart();
      foreach (var source in sources) {
        if (failedFiles.Contains(source.Path)) continue;
        if (!module.DeferredExtensionFiles.Contains(source.Path)) continue;
        try {
          var tokens = TokensFor(source);
          var parser = NewParser(tokens, module, source, isStdLib, parserOs, parserArch);
          parser.RescanExtensionBlocks(module);
        } catch (CompileError ex) {
          ex.FilePath ??= source.Path;
          errors.Add(ex);
          failedFiles.Add(source.Path);
        }
      }
      module.DeferredExtensionFiles.Clear();
      if (sw != null) StageTimer.Record(timings!, "rescanExt", sw.ElapsedMilliseconds);
    }

    // Any pre-scan failure leaves the module in a partially-registered state.
    // Continuing into the full parse would produce cascading false errors (e.g.
    // "Undefined function" for methods that do exist but were never registered).
    // Return early so only the real pre-scan errors are reported.
    if (errors.Count > 0) {
      return errors;
    }

    // Typealias type params may reference placeholder types from PreScan (e.g.,
    // `FooArray = Array with FooEnum` prescanned before FooEnum gets its cases).
    // Now that all types are fully defined, update the references.
    sw?.Restart();
    try {
      RefreshTypeAliasTypeParams(module);
      ResolveStructRawValueEnumRefs(module);
    } catch (CompileError ex) {
      errors.Add(ex);
    }
    if (sw != null) StageTimer.Record(timings!, "refreshAliases", sw.ElapsedMilliseconds);

    if (errors.Count > 0) {
      errors.Add(HaltedError(errors, "type resolution errors prevent full parse"));
      return errors;
    }

    // Re-scan typealiases now that all source struct bodies are fully parsed.
    // The first typealias pre-scan runs before PreScan, so an alias like
    // `MirModule = IrModule with MirOp` specializes against a source struct that
    // still has no fields, freezing the alias with empty fields and unresolved
    // inner aliases. Re-running PreScanTypeAliasesOnly against the now-populated
    // source struct lets RegisterConcreteTypeAlias produce correct fields and
    // per-instance inner aliases (e.g., Array_MirOp for the `ops` field).
    // Without this, compilation is file-order-dependent — passes when the source
    // type's file is pre-scanned before the alias file, fails otherwise.
    sw?.Restart();
    foreach (var source in sources) {
      if (failedFiles.Contains(source.Path)) continue;
      try {
        var tokens = TokensFor(source);
        var parser = NewParser(tokens, module, source, isStdLib, parserOs, parserArch);
        parser.PreScanTypeAliasesOnly(module, TypeAliasScanPhase.Respecialize);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
        failedFiles.Add(source.Path);
      }
    }
    if (sw != null) StageTimer.Record(timings!, "rescanAliases", sw.ElapsedMilliseconds);

    // Function-backed enums need a final resolution AFTER the typealias rescan,
    // because the rescan re-runs PreScanEnum for every file and re-creates each
    // IrEnumType with the placeholder IrFunctionBackingType (signature filled in
    // later). Resolving here uses the now-complete function registry and the
    // post-rescan IrEnumType instances that subsequent passes will read.
    try {
      ResolveFunctionBackedEnumRefs(module);
    } catch (CompileError ex) {
      errors.Add(ex);
    }

    if (errors.Count > 0) {
      errors.Add(HaltedError(errors, "typealias re-scan errors prevent full parse"));
      return errors;
    }

    // Full parse with all signatures known
    sw?.Restart();
    foreach (var source in sources) {
      if (failedFiles.Contains(source.Path)) continue;
      try {
        var tokens = TokensFor(source);
        var parser = NewParser(tokens, module, source, isStdLib, parserOs, parserArch, foreignPerspectiveCache);
        var parsed = parser.Parse();
        module.Merge(parsed);
        // Collect declaration-level errors from parser recovery
        foreach (var err in parser.Errors) errors.Add(err);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
      }
    }
    if (sw != null) StageTimer.Record(timings!, "fullParse", sw.ElapsedMilliseconds);

    return errors;
  }

  /// <summary>
  /// Replaces Error tokens with harmless StringLiteral tokens so parsing can continue.
  /// Builds a "compilation halted" error that points to the same file and line as
  /// the first error in <paramref name="errors"/>, so the user can see exactly where
  /// the phase failed without hunting through cascading false positives.
  /// </summary>
  private static CompileError HaltedError(List<CompileError> errors, string reason) {
    var first = errors[0];
    return new CompileError(ErrorCode.InternalError, $"compilation halted due to errors above: {reason}", first.Line, first.Column) {
      FilePath = first.FilePath
    };
  }

  // Lexer error tokens encode their kind via a sentinel prefix on the token's
  // Value string. The lexer never sees ErrorCode directly (it lives in a
  // separate file), so we round-trip the kind through the token text. The
  // reporter strips the prefix and emits the matching CompileError.
  private static readonly (string Prefix, ErrorCode Code)[] LexerErrorPrefixes = [
    ("__unterminated_string__:", ErrorCode.LexerUnterminatedString),
    ("__unterminated_block_comment__:", ErrorCode.LexerUnterminatedBlockComment),
  ];

  /// When reportErrors is true, also adds CompileErrors to the error list.
  /// </summary>
  private static void ReportLexerErrors(List<Token> tokens, string filePath, List<CompileError>? errors) {
    for (int i = 0; i < tokens.Count; i++) {
      if (tokens[i].Type == TokenType.Error) {
        var tok = tokens[i];
        var (code, message) = ClassifyLexerError(tok.Value);
        errors?.Add(new CompileError(code, message, tok.Line, tok.Column) { FilePath = filePath });
        tokens[i] = new Token(TokenType.StringLiteral, "", tok.Line, tok.Column);
      }
    }
  }

  private static (ErrorCode Code, string Message) ClassifyLexerError(string tokenValue) {
    foreach (var (prefix, code) in LexerErrorPrefixes) {
      if (tokenValue.StartsWith(prefix)) {
        return (code, tokenValue[prefix.Length..]);
      }
    }
    return (ErrorCode.LexerUnescapedBrace, tokenValue);
  }

  private static void RefreshTypeAliasTypeParams(IrModule<MaxonOp> module) {
    foreach (var (_, type) in module.TypeDefs) {
      if (type is not IrStructType structType || structType.TypeParams.Count == 0)
        continue;
      foreach (var key in structType.TypeParams.Keys.ToList()) {
        var paramType = structType.TypeParams[key];

        if (!IrType.MayBeRefreshedByName(paramType))
          continue;

        if (module.TypeDefs.TryGetValue(paramType.Name, out var currentType) && currentType != paramType)
          structType.TypeParams[key] = currentType;
      }
    }
  }

  /// <summary>
  /// Resolves deferred function references in function-backed enum raw values.
  /// PreScanEnum records each case's RawValue as the raw identifier the user
  /// wrote (e.g. "doubleFn") with a placeholder IrFunctionBackingType. After
  /// every file's top-level functions have been pre-scanned, this pass looks
  /// each identifier up against the module's function registry, validates that
  /// all cases share a single signature, and rewrites each case's RawValue to
  /// the fully qualified function name so the lowering pass can emit the
  /// correct StdFuncRefOp for the select chain.
  /// </summary>
  private static void ResolveFunctionBackedEnumRefs(IrModule<MaxonOp> module) {
    foreach (var (_, type) in module.TypeDefs) {
      if (type is not IrEnumType enumType) continue;
      if (enumType.BackingType is not IrFunctionBackingType placeholder) continue;
      // Empty signature is the placeholder marker; a non-empty one means the
      // backing has already been resolved (idempotent across repeated calls).
      if (placeholder.Signature.ParameterTypes.Count > 0 || placeholder.Signature.ReturnType != null)
        continue;

      IrFunctionType? sharedSignature = null;
      string? firstCaseName = null;
      foreach (var enumCase in enumType.Cases) {
        if (enumCase.RawValue is not string ident) continue;
        var line = enumCase.SourceLine ?? 0;
        var col = enumCase.SourceColumn ?? 0;

        var fn = ResolveFunctionByIdent(module, ident)
          ?? throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"function '{ident}' (referenced by enum case '{enumType.Name}.{enumCase.Name}') is not declared",
            line, col);
        var sig = new IrFunctionType([.. fn.ParamTypes], fn.ReturnType);
        if (sharedSignature == null) {
          sharedSignature = sig;
          firstCaseName = enumCase.Name;
        } else if (!Parser.FunctionSignaturesEqual(sharedSignature, sig)) {
          throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
            $"raw value type mismatch: function '{ident}' has signature '{sig.Name}', "
              + $"but case '{enumType.Name}.{firstCaseName}' established signature '{sharedSignature.Name}'",
            line, col);
        }
        // Rewrite the case's RawValue to the fully qualified function name so
        // the codegen pass emits the correct StdFuncRefOp label.
        enumCase.RawValue = fn.Name;
      }

      if (sharedSignature != null) {
        enumType.BackingType = new IrFunctionBackingType(sharedSignature);
      }
    }
  }

  /// Mirrors the bare-name function-reference resolution at expression sites:
  /// exact, then short-name suffix match. Used by the function-backed enum
  /// resolver so the identifier the user wrote (typically unqualified) maps to
  /// whichever function was pre-scanned for it across the module.
  private static IrFunction<MaxonOp>? ResolveFunctionByIdent(IrModule<MaxonOp> module, string ident) {
    var fn = module.FindFunctionByExactName(ident);
    if (fn != null) return fn;
    if (ident.IndexOf('.') < 0) {
      var suffixDot = "." + ident;
      var matches = module.FindFunctionsByShortName(ident)
        .Where(f => f.Name.EndsWith(suffixDot)).ToList();
      if (matches.Count == 1) return matches[0];
    }
    return null;
  }

  /// <summary>
  /// Resolves deferred enum member references in struct-backed enum raw values.
  /// Called after all files are pre-scanned so cross-file enum types are available.
  /// </summary>
  private static void ResolveStructRawValueEnumRefs(IrModule<MaxonOp> module) {
    foreach (var (_, type) in module.TypeDefs) {
      if (type is not IrEnumType enumType) continue;
      foreach (var enumCase in enumType.Cases) {
        if (enumCase.RawValue is not StructRawValue srv) continue;
        if (srv.UnresolvedEnumRefs.Count == 0 && srv.UnresolvedConstRefs.Count == 0) continue;

        foreach (var (fieldName, enumTypeName, caseName, line, column) in srv.UnresolvedEnumRefs) {
          if (!module.TypeDefs.TryGetValue(enumTypeName, out var refType) || refType is not IrEnumType refEnum) {
            throw new CompileError(ErrorCode.SemanticUnknownType,
              $"unknown enum type: '{enumTypeName}'", line, column);
          }
          var refCase = refEnum.GetCase(caseName)
            ?? throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
              $"unknown enum case: '{caseName}' in '{enumTypeName}'", line, column);
          srv.Fields.Add((fieldName, refCase.Ordinal));
        }
        srv.UnresolvedEnumRefs.Clear();

        foreach (var (fieldName, constName, line, column) in srv.UnresolvedConstRefs) {
          if (!module.ExportedConstants.TryGetValue(constName, out var constVal)) {
            throw new CompileError(ErrorCode.SemanticUnknownField,
              $"unknown constant: '{constName}'", line, column);
          }
          long value = constVal switch {
            long l => l,
            double d => BitConverter.DoubleToInt64Bits(d),
            bool b => b ? 1 : 0,
            _ => throw new CompileError(ErrorCode.SemanticUnknownField,
              $"constant '{constName}' is not a numeric or boolean value", line, column)
          };
          srv.Fields.Add((fieldName, value));
        }
        srv.UnresolvedConstRefs.Clear();
      }
    }
  }

  /// <summary>
  /// Token-level registration of the type names ONE file declares — struct, enum/union, interface and
  /// typealias — with the visibility each was declared under. Runs first in <see cref="CompileSources"/>
  /// so a cross-file type reference resolves whichever order the filesystem hands over the files.
  ///
  /// Also THE definition of "which type names does this file publish", which is why it is not private:
  /// <c>FlatNamespaceCheck</c> calls it per file into a throwaway module and reads
  /// <see cref="IrModule{TOp}.TypeDefSourceFiles"/> back. A second scanner asking the same question
  /// would be free to answer it differently — and this one already carries the answers that are not
  /// obvious (a `type` pair inside a signature is not a declaration; a typealias inside an
  /// `export extension` block inherits the block's cross-file visibility).
  /// </summary>
  internal static void PreRegisterTypeNames(IrModule<MaxonOp> module, SourceFile source, List<Token> tokens,
      bool isStdlib = false, Action<TopLevelTypeDeclaration>? onDeclaration = null) {
    int parenDepth = 0;
    // Visibility of the `extension` block currently being scanned (if any), so
    // that a typealias declared directly inside `module extension X` / `export
    // extension X` inherits that block's cross-file visibility. Without this an
    // inner alias would be recorded as file-scoped and a consumer file that
    // parses before the extension's full PreScan would reject it as an unknown
    // cross-file type. -1 means "not inside an extension block".
    int extensionBlockEndDepth = -1;
    bool extensionIsExported = false;
    bool extensionIsModuleVisible = false;
    int blockDepth = 0;
    bool prevWasNewline = true; // first scanned token sits at statement start
    var prevTokenType = TokenType.Newline;
    for (int i = 0; i < tokens.Count - 1; i++) {
      var t = tokens[i];

      // Block-structure tracking (mirrors ProcessExtensionBlock's scanner) so we
      // know when the current `extension` block closes. Done before the
      // paren-depth gate because openers/`end` are statement-level tokens.
      {
        var next = i + 1 < tokens.Count ? tokens[i + 1].Type : TokenType.Eof;
        // `extension` is intentionally excluded here: its opener may be preceded
        // by a `module`/`export` modifier that the loop body consumes (advancing
        // past the `extension` token), so the increment is done in the extension
        // branch below where the token is seen regardless of any modifier.
        // A `test 'name'` declaration opens a block that `end` closes. Uncounted, its `end` would
        // decrement blockDepth below the enclosing level and clear an `extension` block's
        // visibility context early. `test` is contextual, so it is matched by value plus the
        // statement-start guard — `match test 'check'` is the same two tokens mid-statement.
        if (prevWasNewline && Parser.IsTestDeclarationAt(tokens, i)) {
          blockDepth++;
        } else if (t.Type is TokenType.Function or TokenType.If or TokenType.While
            or TokenType.For or TokenType.Match or TokenType.Type
            or TokenType.Enum or TokenType.Union or TokenType.Interface) {
          bool opensBlock = true;
          // `function(` (no name) is a function-type / lambda literal, not a block.
          if (t.Type == TokenType.Function && next == TokenType.LeftParen) opensBlock = false;
          // Match case labels reuse these keywords (`if ... then`, `for ... to`).
          if (next is TokenType.Then or TokenType.Gives or TokenType.To or TokenType.Upto) opensBlock = false;
          // Postfix ternary `if` is mid-expression; block `if` only appears at
          // statement start (after a newline) or directly after `else`.
          if (t.Type == TokenType.If && !prevWasNewline && prevTokenType != TokenType.Else) opensBlock = false;
          if (opensBlock) blockDepth++;
        } else if (t.Type == TokenType.Else) {
          if (next is TokenType.Then or TokenType.Gives or TokenType.To or TokenType.Upto) {
            // Match case label — not a block opener.
          } else if (next == TokenType.If) {
            // `else if` — the upcoming `if` will bump depth.
          } else if (prevTokenType == TokenType.CharacterLiteral) {
            blockDepth++;
          }
        } else if ((t.Type == TokenType.Otherwise && next is TokenType.CharacterLiteral or TokenType.LeftParen)
                   || (t.Type == TokenType.Try && next == TokenType.CharacterLiteral)) {
          blockDepth++;
        } else if (t.Type == TokenType.End) {
          blockDepth--;
          if (extensionBlockEndDepth >= 0 && blockDepth < extensionBlockEndDepth) {
            extensionBlockEndDepth = -1;
            extensionIsExported = false;
            extensionIsModuleVisible = false;
          }
        }
        prevWasNewline = t.Type == TokenType.Newline;
        prevTokenType = t.Type;
      }

      // Track parenthesis nesting so we only recognize type declarations at
      // top level. Without this, a parameter pair like `type StdType` inside
      // a function signature gets misread as a top-level `type StdType`
      // declaration and shadows the real type across files.
      if (t.Type == TokenType.LeftParen) { parenDepth++; continue; }
      if (t.Type == TokenType.RightParen) { parenDepth--; continue; }
      if (parenDepth != 0) continue;

      // `export` / `public` / `module` — the set and the contextual-`module` rule both live on
      // Parser, which is the only party that gets to say what a modifier is.
      var (isExported, isModuleVisible) = Parser.VisibilityAt(tokens, i);
      if ((isExported || isModuleVisible) && i + 1 < tokens.Count) {
        i++;
        t = tokens[i];
      }

      // Entering an `extension` block: count the opener here (block-tracking
      // skips it) and remember its visibility for inner aliases. The matching
      // `end` brings blockDepth back below this level, clearing the context.
      if (t.Type == TokenType.Extension) {
        blockDepth++;
        extensionBlockEndDepth = blockDepth;
        extensionIsExported = isExported;
        extensionIsModuleVisible = isModuleVisible;
      }

      // An inner typealias of a `module`/`export` extension inherits the block's
      // visibility so it propagates across files (matching the parser's
      // PreScanExtensionBlock behavior, but recorded early enough that a
      // consumer file parsing first can still see it).
      if (extensionBlockEndDepth >= 0 && t.Type == TokenType.TypeAlias) {
        isExported = isExported || extensionIsExported;
        isModuleVisible = isModuleVisible || extensionIsModuleVisible;
      }

      if (t.Type == TokenType.Type && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var name = nameToken.Value;
        var assocNames = ParseUsesClauseTokens(tokens, i + 2);
        var structType = new IrStructType(name, [], assocNames);
        SetSourceLocation(structType, source, nameToken);
        module.TypeDefs.TryAdd(name, structType);
        if (!isExported && !isModuleVisible && !isStdlib)
          module.NonExportedTypeNames.Add(name);
        if (isModuleVisible) module.ModuleVisibleTypeNames.Add(name);
        if (source.Path != null) module.TypeDefSourceFiles[name] = source.Path;
        onDeclaration?.Invoke(new TopLevelTypeDeclaration(name, isExported, isModuleVisible, source.Path, nameToken.Line, nameToken.Column));
        i += 1;
      } else if ((t.Type == TokenType.Enum || t.Type == TokenType.Union) && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var typeName = nameToken.Value;
        var namedType = new IrEnumType(typeName, [], null, []) { IsUnion = t.Type == TokenType.Union };
        SetSourceLocation(namedType, source, nameToken);
        module.TypeDefs.TryAdd(typeName, namedType);
        if (!isExported && !isModuleVisible && !isStdlib) module.NonExportedTypeNames.Add(typeName);
        if (isModuleVisible) module.ModuleVisibleTypeNames.Add(typeName);
        if (source.Path != null) module.TypeDefSourceFiles[typeName] = source.Path;
        onDeclaration?.Invoke(new TopLevelTypeDeclaration(typeName, isExported, isModuleVisible, source.Path, nameToken.Line, nameToken.Column));
        i += 1;
      } else if (t.Type == TokenType.Interface && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var ifaceName = nameToken.Value;
        var ifaceType = new IrInterfaceType(ifaceName, []);
        SetSourceLocation(ifaceType, source, nameToken);
        module.TypeDefs.TryAdd(ifaceName, ifaceType);
        var assocNames = ParseUsesClauseTokens(tokens, i + 2);
        if (assocNames.Count > 0)
          module.InterfaceAssociatedTypes.TryAdd(ifaceName, assocNames);
        // Reported with the visibility as WRITTEN, unlike the branches above and below: an interface is
        // never entered into NonExportedTypeNames, so the compiler resolves one across files whatever
        // its modifier says, and a check reading those sets would call every interface exported. What
        // the modifier means for an interface is A2d's question; this callback only reports what is there.
        onDeclaration?.Invoke(new TopLevelTypeDeclaration(ifaceName, isExported, isModuleVisible, source.Path, nameToken.Line, nameToken.Column));
        i += 1;
      } else if (t.Type == TokenType.TypeAlias && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        // Pre-register typealias names as placeholders so cross-file references
        // resolve regardless of file processing order during PreScanTypeAliasesOnly
        var nameToken = tokens[i + 1];
        var aliasName = nameToken.Value;
        if (!module.TypeDefs.ContainsKey(aliasName)) {
          var placeholder = new IrStructType(aliasName, []);
          SetSourceLocation(placeholder, source, nameToken);
          module.TypeDefs[aliasName] = placeholder;
        }
        if (!isExported && !isModuleVisible && !isStdlib)
          module.NonExportedTypeNames.Add(aliasName);
        if (isModuleVisible) module.ModuleVisibleTypeNames.Add(aliasName);
        if (source.Path != null) module.TypeDefSourceFiles[aliasName] = source.Path;
        onDeclaration?.Invoke(new TopLevelTypeDeclaration(aliasName, isExported, isModuleVisible, source.Path, nameToken.Line, nameToken.Column));
        i += 1;
      }
    }
  }

  private static void SetSourceLocation(IrType type, SourceFile source, Token nameToken) {
    type.SourceFilePath = source.Path;
    type.SourceLine = nameToken.Line;
    type.SourceColumn = nameToken.Column;
  }

  /// <summary>
  /// Token-level extraction of `uses A, B, C` clause from a type declaration.
  /// </summary>
  private static List<string> ParseUsesClauseTokens(List<Token> tokens, int startPos) {
    var names = new List<string>();
    if (startPos >= tokens.Count || tokens[startPos].Type != TokenType.Uses)
      return names;
    int pos = startPos + 1; // skip 'uses'
    while (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier) {
      names.Add(tokens[pos].Value);
      pos++;
      if (pos < tokens.Count && tokens[pos].Type == TokenType.Comma)
        pos++;
      else
        break;
    }
    return names;
  }
}

public static class StdlibLoader {
  private static SourceFile[]? _cachedSources;

  /// <summary>
  /// One target's parsed stdlib, together with the two id watermarks that parse minted.
  ///
  /// The watermarks travel WITH the module because they describe it: a user compile seeds its
  /// stdlib-namespace counters past them so lowering-time stdlib MaxonValues don't alias
  /// parser-time ones. Two targets parse different text, so they reach different watermarks, and
  /// a watermark stored apart from its module could be read against the other target's.
  /// </summary>
  private sealed record ParsedStdlib(IrModule<MaxonOp> Module, int MaxValueId, int MaxStdValueId);

  /// <summary>
  /// The parsed stdlib, KEYED BY TARGET — because the parse is target-dependent and the cache
  /// would otherwise decide the answer by whichever target compiled first in the process.
  ///
  /// The parser resolves <c>#if os(...)</c> / <c>arch(...)</c> while it reads, so the stdlib's own
  /// text differs per target: <c>stdlib/Process.maxon</c> declares <c>ExitCode</c> as
  /// <c>int(0 to u32.max)</c> on Windows and <c>int(0 to 255)</c> elsewhere, and
  /// <c>stdlib/FilePath.maxon</c> switches every path separator at seven more sites. A single
  /// shared module would bake the BUILD MACHINE's OS into a cross-compiled binary's stdlib —
  /// silently, because both halves of the compile still succeed.
  ///
  /// Concurrent because the fast path reads it without the lock: the spec runner compiles on
  /// worker threads, and serializing every compile behind the clone would cost the parallelism
  /// the double-checked shape exists to keep.
  /// </summary>
  private static readonly ConcurrentDictionary<CompileTarget, ParsedStdlib> _parsedByTarget = new();
  private static readonly Lock _stdlibLock = new();

  /// <summary>
  /// Guards <see cref="_cachedSources"/> — and it is a SEPARATE lock from <see cref="_stdlibLock"/>
  /// on purpose, not by oversight.
  ///
  /// Reading the stdlib's text takes ~100 ms; PARSING it takes ~1 s. Callers that need only the text —
  /// the build cache, which fingerprints every stdlib source to decide whether a binary is current —
  /// vastly outnumber the one caller that needs the parse, and a single lock would queue a
  /// fingerprint check behind a parse it has no use for. The order is always _stdlibLock →
  /// _sourcesLock (<see cref="GetStdlibModule"/> holds the first while calling
  /// <see cref="LoadStdlibModules"/>) and never the reverse, so the pair cannot deadlock.
  /// </summary>
  private static readonly Lock _sourcesLock = new();

  /// <summary>
  /// The highest stdlib MaxonValue ids (low-bits, without StdlibIdBit) minted while parsing
  /// <paramref name="target"/>'s stdlib. User compiles must seed their IrContext past these so
  /// lowering-time stdlib MaxonValues don't alias parser-time ones in valueMap.
  ///
  /// Zero when that target's stdlib has not been parsed yet — the seeding is then a no-op, which is
  /// correct precisely because the parse that would need seeding past has not happened.
  /// </summary>
  public static (int MaxValueId, int MaxStdValueId) StdlibIdWatermarks(CompileTarget target) =>
    _parsedByTarget.TryGetValue(target, out var parsed)
      ? (parsed.MaxValueId, parsed.MaxStdValueId)
      : (0, 0);

  /// Returns a cached parsed stdlib module clone, for <paramref name="target"/>, ready for user code
  /// compilation. The clone has all functions marked IsStdlib=true.
  public static IrModule<MaxonOp> GetStdlibModule(CompileTarget target) {
    if (_parsedByTarget.TryGetValue(target, out var cached))
      return cached.Module.Clone();

    lock (_stdlibLock) {
      // Re-check: another thread may have parsed this target while this one waited.
      if (_parsedByTarget.TryGetValue(target, out var raced))
        return raced.Module.Clone();

      var context = new IrContext(isStdlibContext: true);
      using var _ = context.PushScope();
      var module = new IrModule<MaxonOp>();
      var sources = LoadStdlibModules();

      // The stdlib-namespace name counters (Parser's stdlib closure counter, MaxonPanicOp's stdlib
      // label cache) are deliberately NOT reset here, even though this is now reachable more than
      // once per process. Resetting them would be a WRONG ANSWER, and the reason is not that the
      // names stay unique across the cached modules — they do NOT, because both counters are
      // [ThreadStatic] (Parser._stdlibClosureCounter, MaxonPanicOp._stdlibPanicLabelCache) and a
      // second target parsed on a second thread starts them at zero. Uniqueness across modules is
      // also not needed: exactly one target's module takes part in any one compile.
      //
      // They no longer need resetting either, because nothing downstream of this parse reads them.
      // Closure names are minted only by the parser, and a cloned panic op now CARRIES the label it
      // was given (MaxonPanicOp.CloneKeepingLabel) instead of re-deriving it from a thread-local
      // cache. Each parse therefore mints against a dictionary only it is adding to, so the names in
      // any one module are unique within that module — which is all that is required.
      //
      // ⚠ This paragraph used to say the opposite: that the cache had to stay a SUPERSET of every
      // module a thread might go on to compile, because the cloners re-minted from it. That was true,
      // and the hazard it described — one label, two messages, whichever reaches symdata first decides
      // what the panic prints — was REAL and is now FIXED (row A1m). It was reproduced before the fix:
      // a panic in Array.resize printed `utf16.maxon:59: value outside typealias 'CodeUnit16'`,
      // because Array.resize re-minted as __stdlib_panic_msg_98, which on the parse thread was utf16's.
      // The comment is rewritten rather than deleted so the next reader learns the trap, not just the
      // rule: this file's own text was the best surviving description of a bug nobody had reproduced.
      var stdlibErrors = Compiler.CompileSources(module, sources, true, target);
      if (stdlibErrors.Count > 0) throw stdlibErrors[0];
      foreach (var func in module.Functions) {
        func.IsStdlib = true;
      }
      // Snapshot the stdlib counters so user compiles can seed their stdlib-namespace
      // counters past these and avoid id collisions during stdlib function lowering.
      _parsedByTarget[target] = new ParsedStdlib(module, context.NextStdlibValueId - 1, context.NextStdlibStdValueId - 1);
      return module.Clone();
    }
  }

  public static string? FindStdlibPath() {
    var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    Logger.Debug(LogCategory.Compiler, $"StdlibLoader: exeDir={exeDir}");
    if (string.IsNullOrEmpty(exeDir)) return null;
    return FindStdlibPath(exeDir);
  }

  /// <summary>
  /// The same upward walk, from a directory the CALLER names. One other asker: <see cref="TreeLock"/>
  /// needs the checkout containing the path a command WRITES INTO — the project directory, the spec
  /// directory — not the one this executable's own stdlib came from. Those two answers are the same
  /// directory in every ordinary invocation and are not required to be; the walk is what both of them
  /// mean by "the checkout", so it is written once.
  /// </summary>
  public static string? FindStdlibPath(string startDir) {
    var path = startDir;
    while (path != null) {
      var stdlibPath = Path.Combine(path, "stdlib");
      Logger.Debug(LogCategory.Compiler, $"StdlibLoader: checking {stdlibPath}");
      if (Directory.Exists(stdlibPath)) {
        Logger.Debug(LogCategory.Compiler, $"StdlibLoader: found stdlib at {stdlibPath}");
        return stdlibPath;
      }
      path = Path.GetDirectoryName(path);
    }
    Logger.Debug(LogCategory.Compiler, "StdlibLoader: stdlib not found");
    return null;
  }

  /// <summary>
  /// The stdlib's source text, read once per process.
  ///
  /// The whole body runs under <see cref="_sourcesLock"/> — there is no unlocked fast-path re-check
  /// ahead of it, deliberately. An uncontended lock is tens of nanoseconds against the ~100 ms of file
  /// reading it guards, so the double-checked form would buy nothing measurable while owing an
  /// argument about publication ordering for the array it hands out. This is called a handful of times
  /// per compile, never in a loop.
  /// </summary>
  public static SourceFile[] LoadStdlibModules() {
    lock (_sourcesLock) {
      if (_cachedSources != null) return _cachedSources;

      var stdlibPath = FindStdlibPath();
      if (stdlibPath == null) return [];

      var allFiles = Directory.GetFiles(stdlibPath, "*.maxon", SearchOption.AllDirectories);

      // stdlib/Internals.maxon is a self-hosted-only file: it uses the
      // `__Internals.*` intrinsic namespace (file-path-gated raw Std-op
      // access) which the C# bootstrap doesn't implement. The bootstrap
      // never needs to compile it because the self-hosted compiler builds
      // its own stdlib cache from a curated whitelist (StdlibLoader.maxon)
      // where Internals.maxon contributes the migrated runtime helpers.
      var files = allFiles.Where(f => Path.GetFileName(f) != "Internals.maxon").ToArray();

      // Sort: Interfaces.maxon first (foundational shared protocols), then
      // helper files (in subdirectories), then remaining top-level files,
      // alphabetically within each group.
      Array.Sort(files, (a, b) => {
        var aIsInterfaces = Path.GetFileName(a) == "Interfaces.maxon" && Path.GetDirectoryName(a) == stdlibPath;
        var bIsInterfaces = Path.GetFileName(b) == "Interfaces.maxon" && Path.GetDirectoryName(b) == stdlibPath;
        if (aIsInterfaces != bIsInterfaces) return aIsInterfaces ? -1 : 1;
        var aIsHelper = Path.GetDirectoryName(a) != stdlibPath;
        var bIsHelper = Path.GetDirectoryName(b) != stdlibPath;
        if (aIsHelper != bIsHelper) return aIsHelper ? -1 : 1;
        return string.Compare(a, b, StringComparison.Ordinal);
      });

      // Per Phase 1 of the directory-as-module redesign, stdlib files anchor
      // at the parent of the stdlib directory so that rel(file, root) yields
      // "stdlib/<subdirs>/<name>.maxon" and namespace = "stdlib.<subdirs>".
      var stdlibRoot = Path.GetDirectoryName(stdlibPath);
      var sources = new List<SourceFile>();
      foreach (var filePath in files)
        sources.Add(new SourceFile(filePath, File.ReadAllText(filePath), stdlibRoot));
      _cachedSources = [.. sources];
      return _cachedSources;
    }
  }

  public static SourceFile[] PrependStdlib(SourceFile[] stdlibSources, SourceFile[] userSources) {
    var combined = new SourceFile[stdlibSources.Length + userSources.Length];
    stdlibSources.CopyTo(combined, 0);
    userSources.CopyTo(combined, stdlibSources.Length);
    return combined;
  }
}

/// <summary>One file handed to the compiler.</summary>
/// <param name="Path">Where it came from. Normalized (forward slashes, absolute) by every collector.</param>
/// <param name="Content">The text to parse — not necessarily what is on disk.</param>
/// <param name="RootPath">The compile root this file's namespace is derived relative to.</param>
/// <param name="CompilerOwnedDeclarations">
/// Names in <paramref name="Content"/> that the COMPILER wrote and therefore may declare with the
/// reserved <c>__</c> prefix, despite this not being a stdlib file.
///
/// It is a set of NAMES rather than a flag on the file, and that is the whole point. `maxon test`
/// appends a generated dispatcher to each test file's own text, so the file is part user's and part
/// compiler's; a file-level exemption would let the USER's half declare <c>__foo</c> too, quietly
/// widening a rule that exists to keep the compiler's namespace disjoint from theirs. Naming the
/// declarations exempts exactly what was generated and nothing else.
/// </param>
public record SourceFile(
  string Path,
  string Content,
  string? RootPath = null,
  IReadOnlySet<string>? CompilerOwnedDeclarations = null);
