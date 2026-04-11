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
  /// When true, compile with --mm-trace enabled.
  /// </summary>
  public bool MmTrace { get; init; }
  /// <summary>
  /// When true, compile with --async-trace enabled.
  /// </summary>
  public bool AsyncTrace { get; init; }
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
  /// All pipeline stages concatenated with "--- stagename" markers.
  /// Verified during test runs (must match exactly).
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
  /// When true, compile with --mm-trace enabled so runtime memory operations
  /// produce trace output on stderr.
  /// </summary>
  public bool MmTrace { get; set; }
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
  /// All pipeline stages IR concatenated with "--- stagename" markers (parsed from fragment file).
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
  /// <summary>
  /// True if this result came from the cache (not actually executed this run).
  /// </summary>
  public bool CachedPass { get; init; }
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
  FileInfo SpecFile,
  bool NeedsRegeneration,
  bool NeedsCompilation,
  bool NeedsExecution
);

/// <summary>
/// Result of preparing work items from specs.
/// </summary>
public record PrepareResult(
  TestWorkItem[] WorkItems,
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
  /// <summary>
  /// Number of tests that passed from cache (not actually executed this run).
  /// </summary>
  public int CachedPassed { get; init; }
}

/// <summary>
/// A single entry in the executable cache.
/// </summary>
public record CacheEntry(
  string TestKey,              // "specName/testName"
  long FragmentMtimeTicks,
  long ExeMtimeTicks,          // 0 for compiler_error tests (no exe)
  long LastPassTicks,
  string TestType              // "success", "compiler_error", "trace"
);

/// <summary>
/// The test executable cache manifest and per-test results.
/// </summary>
public class TestCache {
  public long CompilerMtimeTicks { get; set; }
  public long StdlibMtimeTicks { get; set; }
  public int SpecCount { get; set; }
  public int TestCount { get; set; }
  public Dictionary<string, CacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
