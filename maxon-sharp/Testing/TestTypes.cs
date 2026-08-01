namespace MaxonSharp.Testing;

/// <summary>
/// A parsed spec file containing tests and metadata.
/// </summary>
public class SpecFile {
  public required string FilePath { get; init; }
  public required string Feature { get; init; }
  public required string Status { get; init; }
  public required string Category { get; init; }
  public required List<TestCase> Tests { get; init; }
}

/// <summary>
/// A single test case extracted from a spec file.
/// </summary>
public class TestCase {
  public required string Name { get; init; }
  public required string Source { get; init; }
  public required TestExpectation Expectation { get; init; }
  /// <summary>
  /// Command-line arguments to pass when running the test executable.
  /// Parsed from `&lt;!-- Args: ... --&gt;` directives in the spec file.
  /// </summary>
  public string? Args { get; init; }
  /// <summary>
  /// For multi-file tests: list of (FileName, Source) pairs.
  /// When non-null, the test compiles multiple files instead of a single source.
  /// </summary>
  public List<(string FileName, string Source)>? SourceFiles { get; init; }
  /// <summary>
  /// When true, the test runs in mm-trace capture mode: compiled with the
  /// binary DebugStream producer and run under `monitor --filter=mm`, whose
  /// decoded output is normalized and compared against <see cref="MmTraceExpected"/>.
  /// Enabled by a `&lt;!-- MmTrace --&gt;` directive or the presence of an
  /// ```mm-trace expected-trace block.
  /// </summary>
  public bool MmTrace { get; init; }
  /// <summary>
  /// Expected normalized mm-trace output (the body of the ```mm-trace block).
  /// Null when the test declares mm-trace capture mode only via the directive
  /// with no authored golden yet. Round-tripped through the fragment file.
  /// </summary>
  public string? MmTraceExpected { get; init; }
  /// <summary>
  /// When true, compile with --async-trace enabled.
  /// </summary>
  public bool AsyncTrace { get; init; }
  /// <summary>
  /// When true, the test's RUN binary is compiled with debug info on — the default for
  /// `maxon build`, and off for every other spec compile. Enabled by a
  /// `&lt;!-- DebugInfo --&gt;` directive. See <see cref="Fragment.DebugInfo"/> for which of a
  /// test's several compiles this reaches, and why it is only that one.
  /// </summary>
  public bool DebugInfo { get; init; }
  /// <summary>
  /// Per-test runtime timeout in milliseconds. When null, the runner uses
  /// its default (2000ms). Parsed from `&lt;!-- TimeoutMs: N --&gt;` directives
  /// in the spec file. Use for tests that legitimately take longer than the
  /// default (e.g. async tests with multiple subprocess spawns).
  /// </summary>
  public int? TimeoutMs { get; init; }
}

/// <summary>
/// Base class for test expectations.
/// </summary>
public abstract class TestExpectation { }

/// <summary>
/// Expectation for a successful compilation and execution.
/// </summary>
public class SuccessExpectation : TestExpectation {
  public int? ExitCode { get; init; }
  public string? Stdout { get; init; }
  /// <summary>
  /// Expected runtime stderr output (e.g., panic messages with stack traces).
  /// </summary>
  public string? Stderr { get; init; }
  /// <summary>
  /// Expected compiler IR to verify during test runs (must match exactly).
  /// Written to section 2 of the fragment as a fenced `RequiredIR: ``` ... ``` ` block
  /// and pinned as a test input. Distinct from <see cref="Fragment.GeneratedIR"/>,
  /// which is the compiler's *captured* output in section 3.
  /// </summary>
  public string? RequiredIR { get; init; }
  /// <summary>
  /// Typed value lines describing the exact expected .rdata section contents.
  /// Each line is a typed value (e.g. "f64 3.14", "i64[] 10, 20, 30", "utf8 \"hello\0\"").
  /// </summary>
  public string? RequiredRdata { get; init; }
  /// <summary>
  /// Typed value lines describing the exact expected .data section contents.
  /// Each line is a typed value (e.g. "i8 0", "i64 42").
  /// </summary>
  public string? RequiredData { get; init; }
  /// <summary>
  /// When true, the test runs in mm-trace capture mode (see
  /// <see cref="TestCase.MmTrace"/>): compiled with the binary DebugStream
  /// producer and verified against <see cref="MmTraceExpected"/>.
  /// </summary>
  public bool MmTrace { get; set; }
  /// <summary>
  /// Expected normalized mm-trace output (the body of the ```mm-trace block).
  /// Compared against the normalized `monitor --filter=mm` capture. Null when
  /// no mm-trace golden was authored.
  /// </summary>
  public string? MmTraceExpected { get; set; }
  /// <summary>
  /// When true, compile with --async-trace enabled so async runtime operations
  /// produce trace output on stderr.
  /// </summary>
  public bool AsyncTrace { get; set; }
}

/// <summary>
/// Expectation for a compiler error (stderr output).
/// </summary>
public class CompilerErrorExpectation : TestExpectation {
  public required string ExpectedStderr { get; init; }
}

/// <summary>
/// A parsed test fragment file ready for execution.
/// </summary>
public class Fragment {
  public required string FilePath { get; init; }
  public required string TestName { get; init; }
  public required string Source { get; init; }
  public required TestExpectation Expectation { get; init; }
  /// <summary>
  /// Raw IR captured from the compiler at fragment-generation time, parsed from
  /// section 3 of the fragment file (the block following the "// CompiledIR" header).
  ///
  /// This is a snapshot of the compiler's actual output rather than a hand-authored expectation, but
  /// it is NOT unverified: the whole fragment — this section included — is compared byte-for-byte
  /// against the committed golden by <c>TestRunner.CheckFragmentGolden</c>, and a difference fails the
  /// test. The distinction from <see cref="SuccessExpectation.RequiredIR"/> is what is being asserted,
  /// not whether: RequiredIR pins a hand-written EXCERPT of the pre-target IR that a spec author chose
  /// as the point of the test, while this pins the WHOLE post-register-allocation function so that no
  /// change to the emitted code can pass unremarked.
  /// </summary>
  public string? GeneratedIR { get; init; }
  /// <summary>
  /// Command-line arguments to pass when running the test executable.
  /// </summary>
  public string? Args { get; init; }
  /// <summary>
  /// For multi-file tests: list of (FileName, Source) pairs.
  /// When non-null, the test compiles multiple files instead of a single source.
  /// </summary>
  public List<(string FileName, string Source)>? SourceFiles { get; init; }
  /// <summary>
  /// When true, compile with --mm-trace enabled.
  /// </summary>
  public bool MmTrace { get; init; }
  /// <summary>
  /// When true, compile with --async-trace enabled.
  /// </summary>
  public bool AsyncTrace { get; init; }
  /// <summary>
  /// When true, compile with debug info on (the `.mxdbg` sidecar's span capture).
  ///
  /// ⚠ IT REACHES EXACTLY ONE OF A TEST'S COMPILES — <c>TestRunner.CompileToExecutable</c>,
  /// the one whose binary is RUN — and deliberately not the fragment-golden compile, the RequiredIR
  /// regeneration, or the mm-trace capture. Debug info is a pure observer: it changes not one
  /// emitted byte (docs/DEBUGGER_DESIGN.md), so those three artifacts are identical either way and
  /// a flag that could only perturb them buys nothing. What it does change is whether the lowering
  /// passes' span propagation runs at all, and THAT is what the run compile has to survive.
  /// </summary>
  public bool DebugInfo { get; init; }
  /// <summary>
  /// Per-test runtime timeout in milliseconds. When null, the runner uses
  /// its default. See <see cref="TestCase.TimeoutMs"/>.
  /// </summary>
  public int? TimeoutMs { get; init; }
}

/// <summary>
/// Result of running a single test.
/// </summary>
public class TestResult {
  public required string TestName { get; init; }
  public required bool Passed { get; init; }
  public string? ErrorMessage { get; init; }
  public TimeSpan Duration { get; init; }
  public required string FilePath { get; init; }
}

/// <summary>
/// A work item for the unified test pipeline (regenerate + compile + run + check).
/// </summary>
public record TestWorkItem(
  string FragmentPath,
  string ExePath,
  string SpecName,
  string TestName,
  TestCase Test,
  FileInfo SpecFile
);

/// <summary>
/// A batched work item: one compile produces one binary whose dispatcher
/// runs every included test in sequence and prints framing markers around
/// each. The runner invokes the binary ONCE and slices its stdout to
/// recover per-test stdout and exit code; both the compile and the run step
/// are amortized across the batch.
/// </summary>
public record SpecBatchWorkItem(
  string SpecName,
  string BatchExePath,             // path to .spec-cache/<spec>/__batch<ext>
  TestCase[] Tests,                // batchable tests in this spec, in stable order
  FileInfo SpecFile
);

/// <summary>
/// Either a single-fragment work item or a spec batch. Workers process whichever
/// kind they pick up.
/// </summary>
public abstract record AnyWorkItem {
  public sealed record Single(TestWorkItem Item) : AnyWorkItem;
  public sealed record Batch(SpecBatchWorkItem Item) : AnyWorkItem;
}

/// <summary>
/// Result of preparing work items from specs.
/// </summary>
public record PrepareResult(
  AnyWorkItem[] WorkItems,
  int TotalTests,
  List<string> Errors
);

/// <summary>
/// Summary of all test results.
/// </summary>
public class TestSummary {
  public required List<TestResult> Results { get; init; }
  public required int Passed { get; init; }
  public required int Failed { get; init; }
  public required int Total { get; init; }
  public required TimeSpan TotalDuration { get; init; }
  /// <summary>
  /// Number of fragment generation errors (compilation failures during fragment generation).
  /// </summary>
  public int FragmentGenerationErrors { get; init; }
}

