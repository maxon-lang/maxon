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
/// <para>⭐ THE SAME QUESTION ONE LEVEL UP IS ALSO HERE: what a spec FILE is worth when nothing runs it.
/// A file the parser refuses, and a file it deliberately skips, both leave their tests unexecuted, and
/// both used to leave the run GREEN and silent — <c>Logger.Error</c> prints and returns, and
/// <c>Logger.Debug</c> is not even read. Those rules have no other coverage and cannot get any: no spec
/// in the corpus is unparseable, and one added to give them coverage would break every run. So they are
/// asserted here, against synthesized spec TEXT.</para>
///
/// <para>It compiles nothing and launches nothing: every row below is a pure function of a synthesized
/// <see cref="ProcessRunResult"/> and a target, or of a synthesized spec file's bytes. A guard that
/// proved this by running a foreign-target suite would cost minutes and would run one on the build
/// machine the moment the wiring it checks went missing.</para>
/// </summary>
public static class SpecRunSelfTest {
  private const string TestName = "self-test";
  private const string FragmentPath = "self-test.test";
  private const string OsReason = "the OS refused it";

  /// A synthesized spec's path. Never read from disk — <see cref="SpecParser.ScanFiles"/> takes the text.
  private const string SpecPath = "self-test-spec.md";

  public static int Run() {
    var host = CompileTarget.Native;
    var failures = 0;

    foreach (var (target, _) in CompileTarget.Supported) {
      // ASKED, NOT RESTATED. Spelled here as its own `host.Os == target.Os`, this line was a second
      // copy of the rule <see cref="TargetRunHost.WhyCannotRun"/> states — and the copies were only
      // in agreement because the rule happens to be OS equality TODAY. That function's own docstring
      // promises the opposite: "should this compiler ever learn to reach a runner, the table goes
      // HERE, in this one function, and every consequence below follows without being told."
      // MEASURED, by making exactly that change: teaching `WhyCannotRun` a runner for windows
      // targets left the RUNNER following correctly and this guard failing — and failing with the
      // sentence "This host CANNOT run that target", which the rule no longer said. A guard that
      // contradicts the rule it guards is a guard somebody deletes.
      var expected = TargetRunHost.WhyCannotRun(target) == null
        ? SpecTestOutcome.Failed
        : SpecTestOutcome.NotRunHere;
      failures += CheckLaunchFailureVerdict(target, host, expected);
    }

    failures += CheckNotRunHereIsNotGreen();
    failures += CheckEveryOutcomeIsCounted();
    failures += CheckUnparseableSpecIsAnError();
    failures += CheckSuspensionNeedsAStatedReason();
    failures += CheckSuspendedSpecsAreCounted();

    if (failures == 0)
      Console.WriteLine(
        // ASCII only: this reports through MSBuild's `Exec`, whose console encoding mangles an em
        // dash into replacement characters - including in the failure text, which is the one message
        // that has to survive.
        $"spec-run-selftest: OK - launch-failure verdicts for {CompileTarget.Supported.Count()} targets "
        + $"against host {host.Triple}, a not-run test reddens the run, and a spec nothing runs is "
        + "counted, reasoned and never silent");
    return failures == 0 ? 0 : 1;
  }

  /// One synthesized spec file, scanned. The spec system's own entry point, so what is asserted below is
  /// what a real run would do with the same bytes.
  private static SpecScan Scan(string content) => SpecParser.ScanFiles([(SpecPath, content)]);

  /// <summary>
  /// A spec the parser refuses stops the run. It used to be written to <c>Logger.Error</c>, which prints
  /// and returns — so the file simply left the suite and the run still exited 0. A spec with a broken
  /// directive is indistinguishable, from the outside, from a spec that was never written.
  /// </summary>
  private static int CheckUnparseableSpecIsAnError() {
    // A test with a `maxon` block and no result check at all: the parser throws on it, which is the
    // whole point — an assertion-less case must not be reported as a passing one.
    var scan = Scan("""
      ---
      feature: self-test
      status: stable
      ---
      ## Tests

      <!-- test: no-result-checks -->
      ```maxon
      function main() returns ExitCode
      	return 0
      end 'main'
      ```
      """);

    if (scan.Errors.Count > 0 && scan.Specs.Count == 0) return 0;

    Console.Error.WriteLine(
      $"spec-run-selftest FAIL: an unparseable spec produced {scan.Errors.Count} error(s) and "
      + $"{scan.Specs.Count} spec(s). A spec this parser refuses must make the run FAIL, not vanish "
      + "from it - a file nobody can parse and a file nobody wrote look identical to a green suite.");
    return 1;
  }

  /// <summary>
  /// Suspending a file costs a stated reason, and suspending one test costs the same. Both roads hand
  /// the work to a runner that cannot be built, so both end in nobody running it; what a reason buys is
  /// that the next reader knows whether that is still true.
  /// </summary>
  private static int CheckSuspensionNeedsAStatedReason() {
    var failures = 0;

    foreach (var status in SpecParser.SuspendingStatuses) {
      var scan = Scan($"""
        ---
        feature: self-test
        status: {status}
        ---
        ## Tests
        """);

      if (scan.Errors.Count != 0) continue;

      Console.Error.WriteLine(
        $"spec-run-selftest FAIL: `status: {status}` with no `{SpecParser.StatusReasonKey}:` was accepted. "
        + "A file no runner in this tree executes must say why, or suspending one costs two words and "
        + "nobody ever revisits it.");
      failures++;
    }

    var bareDirective = Scan("""
      ---
      feature: self-test
      status: stable
      ---
      ## Tests

      <!-- test: handed-to-the-other-runner -->
      <!-- SelfhostedOnly -->
      ```maxon
      function main() returns ExitCode
      	return 0
      end 'main'
      ```
      ```exitcode
      0
      ```
      """);

    if (bareDirective.Errors.Count != 0) return failures;

    Console.Error.WriteLine(
      "spec-run-selftest FAIL: a bare `<!-- SelfhostedOnly -->` was accepted. It takes the test out of "
      + "this suite and hands it to a runner that cannot be built, so it must state a reason exactly as "
      + $"`{SpecParser.StatusReasonKey}:` does.");
    return failures + 1;
  }

  /// <summary>
  /// What is suspended is COUNTED, so the runner's trailer can say it out loud. A skip the report cannot
  /// see is the one this whole mechanism exists to stop being possible.
  /// </summary>
  private static int CheckSuspendedSpecsAreCounted() {
    var scan = Scan("""
      ---
      feature: self-test
      status: selfhosted
      status-reason: a synthesized file, asserting that the census sees it
      ---
      ## Tests

      <!-- test: one -->
      ```maxon
      function main() returns ExitCode
      	return 0
      end 'main'
      ```
      ```exitcode
      0
      ```
      """);

    var census = scan.Census;
    if (scan.Errors.Count == 0 && scan.Specs.Count == 0
        && census.SuspendedFileCount == 1 && census.TestsInSuspendedFiles == 1 && !census.NothingSuspended) {
      return 0;
    }

    Console.Error.WriteLine(
      $"spec-run-selftest FAIL: a suspended spec with a stated reason produced {scan.Errors.Count} "
      + $"error(s), {scan.Specs.Count} live spec(s), and a census of {census.SuspendedFileCount} file(s) "
      + $"/ {census.TestsInSuspendedFiles} test(s). It must run nothing and be counted as exactly one "
      + "file holding one test - a suspension the census cannot see is invisible debt again.");
    return 1;
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
