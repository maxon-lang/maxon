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
  /// All pipeline stages concatenated with "--- stagename" markers.
  /// Verified during test runs (must match exactly).
  /// </summary>
  public string? RequiredMLIR { get; init; }
  /// <summary>
  /// Typed value lines describing the exact expected .rdata section contents.
  /// Each line is a typed value (e.g. "f64 3.14", "i64[] 10, 20, 30", "utf8 \"hello\0\"").
  /// </summary>
  public string? RequiredRdata { get; init; }
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
  public string? GeneratedMLIR { get; init; }
}

/// <summary>
/// Result of running a single test.
/// </summary>
public class TestResult {
  public required string TestName { get; init; }
  public required bool Passed { get; init; }
  public string? ErrorMessage { get; init; }
  public TimeSpan Duration { get; init; }
}

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
