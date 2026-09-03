using System.Text;
using MaxonSharp.Compiler;
using MaxonSharp.Debug;

namespace MaxonSharp.Testing;

/// <summary>
/// How one test finished. The four failing shapes are NOT collapsed into "failed", because they
/// are found by different evidence and a reader has to act on them differently.
/// </summary>
internal enum TestOutcome {
  /// <summary>The body returned. Reported by the binary itself.</summary>
  Passed,

  /// <summary>
  /// The body threw. A test throws exactly <c>TestFailure</c>, so this is a failed assertion or a
  /// foreign error the compiler's substituted handler converted — the attached
  /// <see cref="UnitTestResult.Thrown"/>, if any, is what tells those apart.
  /// </summary>
  Failed,

  /// <summary>
  /// The test began and never ended: it took the process down with it. <c>panic</c> is uncatchable
  /// (<c>specs/safety.md</c>), so nothing inside the binary can report this — only the absence of an
  /// end marker can.
  /// </summary>
  Crashed,

  /// <summary>
  /// The test was selected for a process that died before reaching it. NOT a pass: nothing observed
  /// it, and calling that green is the exact lie this whole command exists to prevent.
  /// </summary>
  DidNotRun,

  /// <summary>The process outlived <c>--timeout</c> while this test was running, and was killed.</summary>
  TimedOut,

  /// <summary>
  /// The test held an allocation at exit. Detected process-globally (exit code
  /// <see cref="ProcessLauncher.MemoryLeakExitCode"/>), so it is only ever attributed to a test by
  /// re-running that test alone.
  /// </summary>
  Leaked,
}

/// <summary>
/// The error a test reported through <c>__TestReport.threw</c>: an error that reached the end of a
/// test body unhandled. Absent when a test failed its own assertion, which throws
/// <c>TestFailure</c> and is not foreign.
/// </summary>
internal sealed record ThrownError(string ErrorType, string ErrorCase, string File, int Line);

/// <summary>One test's result.</summary>
/// <param name="Index">Position in discovery order — the number that crossed the wire.</param>
/// <param name="Test">What was discovered, carrying both of the test's names.</param>
/// <param name="Outcome">How it finished.</param>
/// <param name="Nanos">
/// Wall time the BINARY measured around the body, or 0 for an outcome that never produced an end
/// marker. Measured inside the process rather than around it so it is the test's own cost and not
/// the harness's.
/// </param>
/// <param name="Thrown">The unhandled error, when one was reported.</param>
/// <param name="Output">
/// The stderr this test wrote, attributed by its position between the begin and end markers. For a
/// crash it is the process's dying output, which is where the panic message is.
/// </param>
/// <param name="RecoveredInIsolation">
/// Set when a test that crashed a shared process completed normally on its isolated re-run. It does
/// not soften the verdict — the test did kill a process — but it is the diagnosis (the failure
/// depends on what ran before it), and inventing a clean pass out of it would hide that.
/// </param>
internal sealed record UnitTestResult(
  int Index,
  DiscoveredTest Test,
  TestOutcome Outcome,
  long Nanos,
  ThrownError? Thrown,
  string Output,
  bool RecoveredInIsolation = false) {

  /// <summary>Whether this result should redden the run. Everything that is not a pass does.</summary>
  public bool IsFailure => Outcome != TestOutcome.Passed;
}

/// <summary>One test file's results, in the order the file declares them.</summary>
internal sealed record TestFileResults(string Path, IReadOnlyList<UnitTestResult> Results);

/// <summary>
/// The order every face of a report emits its per-file sections in: ascending by the display path
/// that heads each section.
///
/// A directory enumeration's order is a property of the FILESYSTEM — NTFS answers <c>loud</c> before
/// <c>quiet</c>, APFS the other way — so a report that inherited it says something different on each
/// host and can never be a committed expectation. Ordering here rather than at the walk is what
/// leaves the RUN in the filesystem's order: an order dependence that changes a RESULT must still be
/// free to surface.
///
/// The measure is UTF-8 BYTES because that is what the other compiler compares, and ordinal UTF-16
/// disagrees with it above the BMP; both reports have to be the same bytes. It is total — one file
/// is one group, and no two groups name the same path.
/// </summary>
internal static class ReportOrder {
  public static int[] Of(IReadOnlyList<TestFileGroup> groups) {
    var keys = groups.Select(g => Encoding.UTF8.GetBytes(ReportPath.Display(g.Path))).ToArray();
    var order = Enumerable.Range(0, groups.Count).ToArray();
    Array.Sort(order, (left, right) => keys[left].AsSpan().SequenceCompareTo(keys[right]));
    return order;
  }
}

/// <summary>
/// What the EXECUTOR observed: results, how long running them took, and whether it stopped early.
///
/// Separate from <see cref="TestRunReport"/> because the executor genuinely does not know the rest —
/// it is handed a binary and never learns what producing it cost. Returning a whole report from here
/// would mean filling the compile fields with zeros that read as measurements.
/// </summary>
internal sealed record TestExecution(IReadOnlyList<TestFileResults> Files, long RunMs, bool Bailed);

/// <summary>
/// Everything one <c>maxon test</c> run produced, and the single model both renderings read.
///
/// Aggregates are COMPUTED here rather than stored, exactly as <c>CoverageReport</c> does it: a
/// stored count is a second copy of a fact the list already holds, and the two can disagree. Text
/// and JSON therefore cannot report different totals for one run — there is only one place a total
/// comes from.
/// </summary>
/// <param name="Files">Results grouped by file, in <see cref="ReportOrder"/>.</param>
/// <param name="CompileMs">Wall time spent producing the binary; 0 when the cache was warm.</param>
/// <param name="RunMs">Wall time spent running it, including every re-run attribution needed.</param>
/// <param name="Compiled">
/// Whether this run compiled, as opposed to being served entirely by the build cache. Reported
/// because "compile 0ms" is otherwise indistinguishable from a compile that did not happen.
/// </param>
/// <param name="Bailed">
/// Whether <c>--bail</c> stopped the run before every selected test had been claimed. It is reported
/// because the totals below then describe what RAN and not what was selected, and a reader who does
/// not know that would read a short green list as a complete one.
/// </param>
internal sealed record TestRunReport(
  IReadOnlyList<TestFileResults> Files,
  long CompileMs,
  long RunMs,
  bool Compiled,
  bool Bailed) {

  public IEnumerable<UnitTestResult> AllResults => Files.SelectMany(f => f.Results);

  public int Total => AllResults.Count();
  public int Passed => AllResults.Count(r => r.Outcome == TestOutcome.Passed);
  public int Failed => AllResults.Count(r => r.IsFailure);
  public int FileCount => Files.Count;
}
