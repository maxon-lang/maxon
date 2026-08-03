using MaxonSharp.Compiler;

namespace MaxonSharp.Testing;

/// <summary>
/// The self-test for what a spec test is WORTH when its binary never started, wired as
/// `maxon spec-run-selftest` and run by every `dotnet build`.
///
/// <para>⭐ IT PINS THE VERDICT, NOT THE TABLE. Which targets this host can execute is
/// <see cref="TargetRunHost.WhyCannotRun"/>'s answer and is already pinned, row by row, by
/// <see cref="GoldenMintSelfTest"/> — repeating it here would be the same fact written down twice.
/// What is this guard's own is the mapping the fact FEEDS: a launch that failed becomes
/// <see cref="SpecTestOutcome.NotRunHere"/> when the host never could have run the binary, and a plain
/// <see cref="SpecTestOutcome.Failed"/> when it should have. Both directions matter and they fail
/// differently:</para>
/// <list type="bullet">
/// <item>Lose the first and `spec-test --target=x64-windows` on a Mac goes back to reporting three
///   thousand FAILURES for a machine limit — noise a reader cannot act on, and indistinguishable
///   from a real regression.</item>
/// <item>Lose the SECOND — excuse every launch failure — and a binary this compiler had just emitted
///   and could not execute would be quietly waved through. A codegen bug that produces an unrunnable
///   program is precisely what running the program is there to catch, so that direction is the
///   dangerous one, and it is the row a careless simplification takes out.</item>
/// </list>
///
/// <para>⚠ NO SPEC CASE CAN REACH ANY OF THIS, for the same reason the mint rule cannot be reached: a
/// spec case is Maxon source the runner compiles, it cannot ask which host it is on, and the corpus is
/// only ever run natively (`scripts/cross-target-gate.sh` reaches arm64-macOS by ssh-ing to a Mac), so
/// the not-run arm is never taken on any machine that runs the suite.</para>
///
/// <para>It compiles nothing and launches nothing: every row below is a pure function of a synthesized
/// <see cref="ProcessRunResult"/> and a target. A guard that proved this by running a foreign-target
/// suite would cost minutes and would run one on the build machine the moment the wiring it checks
/// went missing.</para>
/// </summary>
public static class SpecRunSelfTest {
  private const string TestName = "self-test";
  private const string FragmentPath = "self-test.test";
  private const string OsReason = "the OS refused it";

  public static int Run() {
    var host = CompileTarget.Native;
    var failures = 0;

    foreach (var (target, _) in CompileTarget.Supported) {
      var expected = host.Os == target.Os ? SpecTestOutcome.Failed : SpecTestOutcome.NotRunHere;
      failures += CheckLaunchFailureVerdict(target, host, expected);
    }

    failures += CheckNotRunHereIsNotGreen();
    failures += CheckEveryOutcomeIsCounted();

    if (failures == 0)
      Console.WriteLine(
        // ASCII only: this reports through MSBuild's `Exec`, whose console encoding mangles an em
        // dash into replacement characters - including in the failure text, which is the one message
        // that has to survive.
        $"spec-run-selftest: OK - launch-failure verdicts for {CompileTarget.Supported.Count()} targets "
        + $"against host {host.Triple}, and a not-run test reddens the run");
    return failures == 0 ? 0 : 1;
  }

  /// <summary>
  /// A launch that failed earns NotRunHere on a target this host cannot execute and Failed on one it
  /// can. The two arms are asserted from the SAME synthesized launch, so the only thing that can move
  /// the verdict is the target.
  /// </summary>
  private static int CheckLaunchFailureVerdict(CompileTarget target, CompileTarget host, SpecTestOutcome expected) {
    var launch = new ProcessRunResult(ProcessRunOutcome.LaunchFailed, ProcessLauncher.NoExitCodeFromProcess, "", OsReason);

    if (TestRunner.WhyUncomparable(launch, target) is not { } why) {
      Console.Error.WriteLine(
        $"spec-run-selftest FAIL: {target.Triple}: a binary that never started was reported as "
        + "comparable output, so its expectations would be checked against nothing.");
      return 1;
    }

    var actual = TestRunner.Uncomparable(TestName, FragmentPath, TimeSpan.Zero, why).Outcome;
    if (actual == expected) return 0;

    Console.Error.WriteLine(
      $"spec-run-selftest FAIL: {target.Triple} on a {host.Os} host: a failed launch must be "
      + $"{expected} and was {actual}. "
      + (expected == SpecTestOutcome.Failed
        ? "This host CAN run that target, so a binary that would not start is a defect and must not be excused."
        : "This host CANNOT run that target, so the test was never observed and must not be reported as a failure of the test."));
    return 1;
  }

  /// <summary>
  /// A test nothing ran must not be a pass and must not leave the run green. Asserted through
  /// <see cref="TestSummary"/> because that is where both facts are decided for every reporting path.
  /// </summary>
  private static int CheckNotRunHereIsNotGreen() {
    var summary = new TestSummary {
      Results = [TestResult.NotRunHere(TestName, FragmentPath, TimeSpan.Zero, OsReason)],
      TotalDuration = TimeSpan.Zero,
    };

    var failures = 0;
    if (summary.Passed != 0) {
      Console.Error.WriteLine(
        $"spec-run-selftest FAIL: a run of one not-run test reports {summary.Passed} passed. "
        + "A test nothing executed is not a test that passed.");
      failures++;
    }

    if (summary.IsGreen) {
      Console.Error.WriteLine(
        "spec-run-selftest FAIL: a run whose only test could not be run reports GREEN. A gate that "
        + "could not run is not a gate that passed.");
      failures++;
    }

    return failures;
  }

  /// <summary>
  /// Every result lands in exactly one column. The totals are computed from one list, so a new
  /// outcome that nobody counted would make them disagree with <c>Total</c> rather than hide.
  /// </summary>
  private static int CheckEveryOutcomeIsCounted() {
    var summary = new TestSummary {
      Results = [
        TestResult.Pass(TestName, FragmentPath, TimeSpan.Zero),
        TestResult.Fail(TestName, FragmentPath, TimeSpan.Zero, OsReason),
        TestResult.NotRunHere(TestName, FragmentPath, TimeSpan.Zero, OsReason),
      ],
      TotalDuration = TimeSpan.Zero,
    };

    var counted = summary.Passed + summary.Failed + summary.NotRunHere;
    if (counted == summary.Total) return 0;

    Console.Error.WriteLine(
      $"spec-run-selftest FAIL: {summary.Total} results but {counted} counted across passed/failed/"
      + "not-run-here. A test that falls out of every column has been dropped from the report.");
    return 1;
  }
}
