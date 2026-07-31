namespace MaxonSharp.Compiler;

/// <summary>
/// Asserts that the stdlib is parsed FOR THE TARGET BEING BUILT, and that the per-target stdlib
/// cache keeps two targets apart inside one process.
///
/// <para>Wired as `maxon stdlib-target-selftest`, mirroring `mxdbg-selftest` and
/// `batch-rewriter-test`, because NO SPEC TEST CAN REACH THIS FACT. The spec runner builds one
/// target per process and compares the RequiredIR block of the target it was given, so on any single
/// machine host == target and both halves of the bug are invisible: a stdlib parsed for the BUILD
/// MACHINE agrees with the user code whenever they are the same machine, and a single cached module
/// shared between targets is never asked for a second target. A spec case would pass for the same
/// reason it passed before the fix, which is the one kind of test worth less than none.</para>
///
/// <para>The observation is <c>main</c>'s return type in the Standard IR, which IS
/// <c>stdlib/Process.maxon</c>'s <c>ExitCode</c>: <c>#if os(Windows)</c> declares it
/// <c>int(0 to u32.max)</c> and every other OS <c>int(0 to 255)</c>. One observation covers the whole
/// blast radius — <c>stdlib/FilePath.maxon</c> switches its path separator at seven further sites —
/// because every one of them is the same <c>#if os(...)</c>, resolved by the same parser, configured
/// from the same target. They share a door, so they share a verdict.</para>
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

  public static int Run() {
    // INTERLEAVED on purpose. The SECOND `x64-windows` entry is the one that catches a cache keyed
    // by nothing: by then `arm64-macos` has been parsed, and a single shared module would answer the
    // Windows compile with the macOS stdlib. A straight windows-then-macos pass would miss exactly
    // that, and it is the failure mode that threading the target INTRODUCES if the key is forgotten.
    //
    // Both entries are named rather than one of them being "the host", so this reads the same on a
    // Windows box and on a Mac: each is a cross-compile somewhere.
    CompileTarget[] sequence = [
      new("x64", "windows"),
      new("arm64", "macos"),
      new("x64", "windows"),
      new("arm64", "macos"),
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
    } finally {
      Logger.SetLevel(LogCategory.Compiler, previousCompilerLevel);
    }

    if (failures == 0)
      Console.WriteLine($"stdlib-target-selftest: OK - {sequence.Length} compiles across {sequence.Distinct().Count()} targets");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// What <c>stdlib/Process.maxon</c> promises for this OS. It restates the stdlib's own
  /// <c>#if os(Windows)</c> rather than deriving it, because a test that derived its expectation
  /// from the thing under test would agree with any answer the compiler gave.
  /// </summary>
  private static string ExpectedExitCodeType(CompileTarget target) => target.Os switch {
    "windows" => WindowsExitCodeType,
    "macos" or "linux" => PosixExitCodeType,
    var unknown => throw new ArgumentException(
      $"no ExitCode expectation is recorded for OS '{unknown}'; stdlib/Process.maxon must be consulted before adding it")
  };

  /// <summary>
  /// Compiles the probe for <paramref name="target"/> and reports the type <c>main</c> returns.
  ///
  /// It runs the WHOLE compile rather than reading the cached stdlib module's alias table, because
  /// the defect was a target that reached one half of a compile and not the other — so the thing
  /// worth observing is what the two halves agreed on, not what either believed alone.
  /// </summary>
  private static string MainReturnType(CompileTarget target) {
    var tempDir = Path.Combine(Path.GetTempPath(), $"maxon-stdlib-target-selftest-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);

    try {
      var sources = new[] { new SourceFile(Path.Combine(tempDir, ProbeFileName), ProbeSource, tempDir) };
      var result = new Compiler().Compile(sources, Path.Combine(tempDir, "probe"), returnIr: true, target: target);

      if (!result.Success)
        throw new InvalidOperationException(
          $"the probe failed to compile: {string.Join("; ", result.Errors.Select(e => e.Message))}");
      if (result.AllStagesIr == null)
        throw new InvalidOperationException("the probe compiled but returned no IR to read");

      return ReadMainReturnType(result.AllStagesIr);
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
