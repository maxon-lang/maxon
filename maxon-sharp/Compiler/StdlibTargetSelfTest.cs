namespace MaxonSharp.Compiler;

/// <summary>
/// The compiler's TARGET self-test. Two facts, one door: the stdlib is parsed FOR THE TARGET BEING
/// BUILT (and the per-target cache keeps two targets apart inside one process), and the executable
/// is WRITTEN IN THAT TARGET'S OBJECT FORMAT — or the target is refused outright.
///
/// <para>Wired as `maxon stdlib-target-selftest`, mirroring `mxdbg-selftest` and
/// `batch-rewriter-test`, because NO SPEC TEST CAN REACH EITHER FACT. The spec runner builds one
/// target per process and compares the RequiredIR block of the target it was given, so on any single
/// machine host == target and both halves of the stdlib bug are invisible: a stdlib parsed for the
/// BUILD MACHINE agrees with the user code whenever they are the same machine, and a single cached
/// module shared between targets is never asked for a second target. A spec case would pass for the
/// same reason it passed before the fix, which is the one kind of test worth less than none. The
/// object-format half is out of reach for a plainer reason: the spec format has no per-case
/// `--target` directive at all, so no `.md` case can name the target it is compiled for.</para>
///
/// <para>The stdlib observation is <c>main</c>'s return type in the Standard IR, which IS
/// <c>stdlib/Process.maxon</c>'s <c>ExitCode</c>: <c>#if os(Windows)</c> declares it
/// <c>int(0 to u32.max)</c> and every other OS <c>int(0 to 255)</c>. One observation covers the whole
/// blast radius — <c>stdlib/FilePath.maxon</c> switches its path separator at seven further sites —
/// because every one of them is the same <c>#if os(...)</c>, resolved by the same parser, configured
/// from the same target. They share a door, so they share a verdict.</para>
///
/// <para>The object-format observation is the emitted file's FIRST BYTES, because that is the fact
/// the defect falsified and nothing else in the pipeline states it: with the writer chosen by
/// architecture alone, <c>--target=x64-linux</c> exited 0 having written <c>MZ</c>, and
/// <c>arm64-windows</c> wrote a Mach-O named <c>.exe</c>. Every stage before the writer was
/// perfectly correct, so only the bytes on disk can tell the two apart.</para>
/// </summary>
public static class StdlibTargetSelfTest {
  /// <summary>
  /// The whole probe program. It names <c>ExitCode</c> and nothing else, so the compiled signature
  /// cannot be explained by anything but the stdlib the compile was given.
  /// </summary>
  private const string ProbeFileName = "stdlib-target-selftest.maxon";
  private const string ProbeSource = "function main() returns ExitCode\n\treturn 0\nend 'main'\n";

  /// The Standard-dialect spelling of <c>int(0 to u32.max)</c> — <c>ExitCode</c> on Windows.
  private const string WindowsExitCodeType = "u32";

  /// The Standard-dialect spelling of <c>int(0 to 255)</c> — <c>ExitCode</c> on every other OS.
  private const string PosixExitCodeType = "u8";

  private const string MainSignaturePrefix = "func @main() -> ";

  /// <summary>
  /// The bytes each object format starts a file with. <c>MZ</c> is the DOS header a Windows PE still
  /// begins with; <c>CF FA ED FE</c> is the 64-bit Mach-O magic (0xFEEDFACF, little-endian on disk).
  /// They are compared as BYTES rather than the file being parsed, because the question is only
  /// "which platform's format is this", and the first four bytes answer it unambiguously.
  /// </summary>
  private static readonly Dictionary<ObjectFormat, byte[]> FormatMagic = new() {
    [ObjectFormat.Pe] = [0x4D, 0x5A],
    [ObjectFormat.MachO] = [0xCF, 0xFA, 0xED, 0xFE],
  };

  /// <summary>
  /// The targets this compiler must REFUSE, one per way of being unsupported.
  ///
  /// <para>Every entry is a spelling that used to compile and exit 0 while producing a binary in the
  /// wrong platform's format, plus one unknown OS. They are listed rather than derived from the
  /// roster's complement because the complement is infinite, and because the point of the list is
  /// that each of these was a SEPARATE observed wrong answer: the row that opened this was filed
  /// against <c>x64-linux</c> alone, and fixing only that spelling would have left three.</para>
  /// </summary>
  private static readonly string[] RefusedTriples = [
    // A PE, with a stdlib parsed under `#if os(Linux)` — the filed symptom.
    $"{CompileTarget.X64Arch}-{CompileTarget.LinuxOs}",
    // A Mach-O, same defect wearing the other architecture.
    $"{CompileTarget.Arm64Arch}-{CompileTarget.LinuxOs}",
    // A PE with macOS stdlib semantics.
    $"{CompileTarget.X64Arch}-{CompileTarget.MacosOs}",
    // The loudest one: a Mach-O written to a path ending in `.exe`.
    $"{CompileTarget.Arm64Arch}-{CompileTarget.WindowsOs}",
    // An OS no part of the compiler knows. It used to reach output-path handling and die there with
    // an unhandled ArgumentException and a stack trace, exit 127 — a crash, not a diagnostic.
    $"{CompileTarget.X64Arch}-plan9",
  ];

  public static int Run() {
    // INTERLEAVED on purpose. The SECOND `x64-windows` entry is the one that catches a cache keyed
    // by nothing: by then `arm64-macos` has been parsed, and a single shared module would answer the
    // Windows compile with the macOS stdlib. A straight windows-then-macos pass would miss exactly
    // that, and it is the failure mode that threading the target INTRODUCES if the key is forgotten.
    //
    // Both entries are named rather than one of them being "the host", so this reads the same on a
    // Windows box and on a Mac: each is a cross-compile somewhere.
    CompileTarget[] sequence = [
      new(CompileTarget.X64Arch, CompileTarget.WindowsOs),
      new(CompileTarget.Arm64Arch, CompileTarget.MacosOs),
      new(CompileTarget.X64Arch, CompileTarget.WindowsOs),
      new(CompileTarget.Arm64Arch, CompileTarget.MacosOs),
    ];

    // The four probe binaries are INTERNAL artifacts in exactly this class's sense — nobody runs
    // them, they exist to be read once and deleted — so they are built under the one scope the
    // codebase already keeps for that, rather than under whatever flags the process happens to
    // carry. Without it a caller with --mm-trace or --debugstream set would have the probe's own
    // emitted binary write to the stream this check reports on.
    using var _ = InternalCompileScope.Enter();

    // This runs on EVERY `dotnet build` (see MaxonSharp.csproj, CheckStdlibTargetCache), where the
    // four "Wrote N bytes ..." lines the compiler logs at Info would be eight lines of noise per
    // build for a check whose whole output is one line. Errors still print.
    var previousCompilerLevel = Logger.GetLevel(LogCategory.Compiler);
    Logger.SetLevel(LogCategory.Compiler, LogLevel.Error);

    var failures = 0;
    try {
      foreach (var target in sequence) {
        string actual;
        try {
          actual = MainReturnType(target);
        } catch (Exception ex) {
          Console.Error.WriteLine($"stdlib-target-selftest FAIL: {target.Triple}: {ex.Message}");
          failures++;
          continue;
        }

        var expected = ExpectedExitCodeType(target);
        if (actual == expected) continue;

        // ASCII only, here and in the OK line below. This check reports through MSBuild's `Exec`,
        // whose console encoding mangles an em dash into three replacement characters — and it does
        // it to the FAILURE text too, which is the one message that has to survive.
        Console.Error.WriteLine(
          $"stdlib-target-selftest FAIL: {target.Triple}: ExitCode compiled to '{actual}', expected '{expected}' - "
          + "the stdlib was parsed for the wrong OS.");
        failures++;
      }

      failures += CheckObjectFormats();
      failures += CheckUnsupportedTargetsRefused();
    } finally {
      Logger.SetLevel(LogCategory.Compiler, previousCompilerLevel);
    }

    if (failures == 0)
      Console.WriteLine(
        $"stdlib-target-selftest: OK - {sequence.Length} stdlib compiles across {sequence.Distinct().Count()} targets, "
        + $"{CompileTarget.Supported.Count()} object formats, {RefusedTriples.Length} targets refused");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// What <c>stdlib/Process.maxon</c> promises for this OS. It restates the stdlib's own
  /// <c>#if os(Windows)</c> rather than deriving it, because a test that derived its expectation
  /// from the thing under test would agree with any answer the compiler gave.
  /// </summary>
  private static string ExpectedExitCodeType(CompileTarget target) => target.Os switch {
    CompileTarget.WindowsOs => WindowsExitCodeType,
    // `linux` is listed although no target here compiles for it: this states what the STDLIB
    // promises per OS, and stdlib/Process.maxon still says `int(0 to 255)` for Linux. Which OSes
    // this compiler can WRITE a binary for is a different question, asked below.
    CompileTarget.MacosOs or CompileTarget.LinuxOs => PosixExitCodeType,
    var unknown => throw new ArgumentException(
      $"no ExitCode expectation is recorded for OS '{unknown}'; stdlib/Process.maxon must be consulted before adding it")
  };

  /// <summary>
  /// Every supported target emits its OWN object format.
  ///
  /// <para>This is the negative control the refusal check needs and the assertion the defect
  /// falsified: withdrawing a target is only worth anything if the ones that remain still emit
  /// correctly, and a refusal that grew too broad would be caught here rather than by a user.</para>
  /// </summary>
  private static int CheckObjectFormats() {
    var failures = 0;

    foreach (var (target, format) in CompileTarget.Supported) {
      var expected = FormatMagic[format];

      byte[] actual;
      try {
        actual = Probe(target, (result, outputPath) => ExecutableHead(result, outputPath, expected.Length));
      } catch (Exception ex) {
        // Reported as a FAIL like every other way this check can go wrong, rather than escaping as
        // an unhandled exception: this runs inside `dotnet build`, where a stack trace says far less
        // about the compiler than one sentence does.
        Console.Error.WriteLine($"stdlib-target-selftest FAIL: {target.Triple}: {ex.Message}");
        failures++;
        continue;
      }

      if (actual.SequenceEqual(expected)) continue;

      // ASCII only - see the FAIL text in Run().
      Console.Error.WriteLine(
        $"stdlib-target-selftest FAIL: {target.Triple}: executable starts {Hex(actual)}, expected {Hex(expected)} ({format}) - "
        + "the object writer was chosen for the wrong platform.");
      failures++;
    }

    return failures;
  }

  /// <summary>
  /// The first <paramref name="length"/> bytes of the executable a successful probe wrote — the only
  /// place the object format is stated as a fact rather than as an intention.
  /// </summary>
  private static byte[] ExecutableHead(CompileResult result, string outputPath, int length) {
    if (!result.Success)
      throw new InvalidOperationException(
        $"the probe failed to compile: {string.Join("; ", result.Errors.Select(e => e.Format()))}");
    if (!File.Exists(outputPath))
      throw new InvalidOperationException("the probe reported success but wrote no executable");

    using var stream = File.OpenRead(outputPath);
    var head = new byte[length];

    return stream.ReadAtLeast(head, length, throwOnEndOfStream: false) == length
      ? head
      : throw new InvalidOperationException($"the executable is shorter than {length} bytes");
  }

  /// <summary>
  /// A target this compiler has no writer for is REFUSED, at both doors and with nothing written.
  ///
  /// <para>Both doors, because they see different requests. <c>CompileTarget.Parse</c> is the
  /// `--target=` flag; <c>Compile</c> is every target that never passed through it — the LSP's,
  /// `maxon test`'s, and above all the DEFAULT, which is the host and would be <c>x64-linux</c> on a
  /// Linux box. A gate at only one of them leaves the other emitting the wrong platform's binary.
  /// </para>
  /// </summary>
  private static int CheckUnsupportedTargetsRefused() {
    var failures = 0;
    var expectedCode = ErrorCode.CodeEmitterUnsupportedTarget.Format();

    foreach (var triple in RefusedTriples) {
      try {
        var parsed = CompileTarget.Parse(triple);
        Console.Error.WriteLine(
          $"stdlib-target-selftest FAIL: {triple}: CompileTarget.Parse accepted it, returning {parsed.Triple} - "
          + "the --target flag is not gated.");
        failures++;
      } catch (ArgumentException ex) when (ex.Message.Contains(expectedCode, StringComparison.Ordinal)) {
        // The `--target` door refused it by name, which is what this arm is for.
      } catch (ArgumentException ex) {
        Console.Error.WriteLine(
          $"stdlib-target-selftest FAIL: {triple}: CompileTarget.Parse refused it without {expectedCode}: {ex.Message}");
        failures++;
      }

      // Constructed directly, deliberately bypassing Parse: this is the shape a host default or an
      // in-process caller hands to Compile, and it is the half the flag check cannot cover.
      var parts = triple.Split('-', 2);
      failures += CheckCompileRefuses(new CompileTarget(parts[0], parts[1]), expectedCode);
    }

    return failures;
  }

  private static int CheckCompileRefuses(CompileTarget target, string expectedCode) => Probe(target, (result, outputPath) => {
    if (result.Success) {
      Console.Error.WriteLine(
        $"stdlib-target-selftest FAIL: {target.Triple}: Compile SUCCEEDED for a target with no object writer - "
        + "it produced a binary in some other platform's format.");
      return 1;
    }

    var failures = 0;
    if (!result.Errors.Any(error => error.Code == ErrorCode.CodeEmitterUnsupportedTarget)) {
      Console.Error.WriteLine(
        $"stdlib-target-selftest FAIL: {target.Triple}: Compile failed without {expectedCode}: "
        + string.Join("; ", result.Errors.Select(e => e.Format())));
      failures++;
    }

    // A refusal that still wrote bytes would be the original defect with a diagnostic bolted on.
    if (File.Exists(outputPath)) {
      Console.Error.WriteLine(
        $"stdlib-target-selftest FAIL: {target.Triple}: Compile refused the target but wrote {outputPath} anyway.");
      failures++;
    }

    return failures;
  });

  /// <summary>
  /// Compiles the probe for <paramref name="target"/> into a throwaway directory and hands the
  /// result — and the path it was told to write — to <paramref name="inspect"/>.
  ///
  /// <para>It runs the WHOLE compile rather than interrogating the compiler's intermediate state,
  /// because both defects this file guards were a target that reached one half of a compile and not
  /// the other. What is worth observing is what the halves AGREED on, not what either believed
  /// alone — for the stdlib that is the IR, and for the object format it is the bytes on disk.</para>
  /// </summary>
  private static T Probe<T>(CompileTarget target, Func<CompileResult, string, T> inspect) {
    var tempDir = Path.Combine(Path.GetTempPath(), $"maxon-stdlib-target-selftest-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);

    try {
      var outputPath = Path.Combine(tempDir, "probe");
      var sources = new[] { new SourceFile(Path.Combine(tempDir, ProbeFileName), ProbeSource, tempDir) };

      return inspect(new Compiler().Compile(sources, outputPath, returnIr: true, target: target), outputPath);
    } finally {
      // Report rather than swallow: a temp directory this could not remove is worth saying out loud,
      // but it is not a reason to fail a check about the compiler's target handling.
      try {
        Directory.Delete(tempDir, recursive: true);
      } catch (Exception ex) {
        Console.Error.WriteLine($"stdlib-target-selftest: could not remove {tempDir}: {ex.Message}");
      }
    }
  }

  /// <summary>Compiles the probe for <paramref name="target"/> and reports the type <c>main</c> returns.</summary>
  private static string MainReturnType(CompileTarget target) => Probe(target, (result, _) => {
    if (!result.Success)
      throw new InvalidOperationException(
        $"the probe failed to compile: {string.Join("; ", result.Errors.Select(e => e.Message))}");
    if (result.AllStagesIr == null)
      throw new InvalidOperationException("the probe compiled but returned no IR to read");

    return ReadMainReturnType(result.AllStagesIr);
  });

  /// The bytes as `4d 5a`, so a FAIL line says what was actually on disk. ASCII only - see Run().
  private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

  /// <summary>
  /// <c>main</c>'s return type as the STANDARD dialect spells it — the last stage at which the type
  /// is still named. The x64 and arm64 dialects have already lowered it to a register, which is the
  /// same register either way, so the answer is unreadable one stage later.
  /// </summary>
  private static string ReadMainReturnType(string allStagesIr) {
    // Through PipelineStages rather than a fourth private scanner for the same marker: this check
    // exists because two halves of a compile disagreed about one fact, and a reader of the dump
    // format that only this file knows is that shape again, one level down.
    foreach (var (name, body) in PipelineStages.Split(allStagesIr)) {
      if (name != PipelineStages.Standard) continue;

      foreach (var rawLine in body.Split('\n')) {
        var line = rawLine.Trim();
        if (!line.StartsWith(MainSignaturePrefix)) continue;

        // The line is `func @main() -> u32 {`; the type is everything up to the opening brace.
        var tail = line[MainSignaturePrefix.Length..].Trim();
        var typeEnd = tail.IndexOf(' ');

        return typeEnd < 0 ? tail : tail[..typeEnd];
      }
    }

    throw new InvalidOperationException(
      $"no '{MainSignaturePrefix}' line in the '{PipelineStages.Standard}' section of the probe's IR");
  }
}
