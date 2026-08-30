using System.Text;
using MaxonSharp.Compiler;
using static MaxonSharp.Testing.MaxonTestProtocol;

namespace MaxonSharp.Testing;

/// <summary>How the compiled test binary should be driven.</summary>
/// <param name="BinaryPath">The binary to spawn. Spawned many times; compiled once.</param>
/// <param name="Workers">How many shards may run at once.</param>
/// <param name="Isolate">One process per test, rather than one per file.</param>
/// <param name="BailAfter">Stop claiming new work once this many tests have failed; null never stops.</param>
/// <param name="TimeoutMs">How long one process may run before it is killed.</param>
internal sealed record TestRunOptions(
  string BinaryPath,
  int Workers,
  bool Isolate,
  int? BailAfter,
  int TimeoutMs);

/// <summary>
/// Runs a compiled test binary and turns what it printed into attributed results.
///
/// Two things shape everything here:
///
/// - <b>Every re-run is a PROCESS SPAWN, never a recompile.</b> All tests are compiled in, always,
///   and which ones run is an argument. So isolating a crash, attributing a leak, and re-running
///   what a dead process never reached all cost a spawn each and leave the build cache warm.
/// - <b>Absence of evidence is reported as absence.</b> A test that was selected and produced no end
///   marker did not pass. <c>panic</c> is uncatchable, so the binary CANNOT report its own death —
///   only the gap in the marker stream can, and treating that gap as anything but a failure is the
///   silent green this command exists to prevent.
/// </summary>
internal static class TestExecutor {
  /// <summary>What a shard reports when the launcher drained no output to attribute.</summary>
  private const string NoOutputCapturedText =
    "the process exited but its output never finished draining, so nothing could be attributed";

  /// <summary>What a re-run reports when a process died before reaching any test at all.</summary>
  private const string NothingStartedText =
    "the process died before this test began, and re-running the remainder made no progress";

  /// <summary>What a test that was still running when its process hit the deadline reports.</summary>
  private const string KilledByTimeoutText =
    "still running when the process hit its deadline, and was killed";

  /// <summary>
  /// How many workers a test harness uses when nobody said: THE WHOLE MACHINE. THE definition — the
  /// spec-test runner takes it from here too, so the two harnesses in this process cannot divide one
  /// machine by different rules.
  /// </summary>
  /// <remarks>
  /// ⭐ MEASURED — this whole suite on one host (6 physical / 12 logical), all at below-normal priority,
  /// runs INTERLEAVED and alternating so run order cannot masquerade as an effect:
  ///   6 workers  90.5 s, 91.3 s                  (mean 90.9)
  ///   10 workers 86.2 s, 84.2 s                  (mean 85.2)
  ///   12 workers 82.2 s, 83.9 s, 85.2 s, 82.9 s  (mean 83.5)
  /// Throughput RISES with worker count and plateaus at the top: undersubscribing to the PHYSICAL count
  /// costs ~8%, and there is no oversubscription cliff to protect against. This used to reserve two
  /// cores; the reserve bought nothing measurable and the whole machine is at worst equal.
  ///
  /// ⛔⛔ AN EARLIER VERSION OF THIS COMMENT CLAIMED 12 WORKERS COST 15% (95.4 s), AND THAT WAS AN
  /// ARTIFACT — a single un-interleaved sample, and the only one taken immediately after a
  /// `dotnet build`. A cold start, not contention; it did not reproduce once under alternation.
  /// ⇒ DO NOT trust an unrepeated wall-clock reading here, and never compare one taken right after a
  /// rebuild against one taken warm: this suite's spread is a few percent and a cold start is bigger.
  ///
  /// ⚠ SIZING FOR THROUGHPUT IS ONLY CORRECT BECAUSE THE RUN IS DE-PRIORITISED.
  /// <see cref="HostPriority"/> owns keeping the box usable, and it costs nothing measurable: at a fixed
  /// 10 workers, dropping to below-normal moved the suite 82.8 s → 83.1 s. Reserving cores for politeness
  /// INSTEAD would cost throughput (the 6-worker row) and still not yield to a foreground process,
  /// because the OS time-slices rather than holding a core aside. Two knobs, two questions — the
  /// politeness one is not here, and must not be reintroduced here.
  ///
  /// The floor is not dead code even though <see cref="Environment.ProcessorCount"/> is documented to
  /// be at least 1: a zero would reach `Math.Min(workers, …)` and spawn NO threads, which is a silent
  /// hang rather than a loud failure.
  /// </remarks>
  public static int DefaultWorkers => Math.Max(1, Environment.ProcessorCount);

  /// <summary>One process launch's worth of work: which file it belongs to, and which tests it runs.</summary>
  private sealed record Shard(int FileIndex, IReadOnlyList<int> TestIndices);

  /// <summary>
  /// Run <paramref name="selected"/> across <paramref name="groups"/>.
  /// </summary>
  /// <param name="onFileComplete">
  /// Invoked once per file, in DISCOVERY ORDER, as soon as that file and every file before it has
  /// finished — never in completion order. Execution is parallel and reporting is not: a run that
  /// printed results as they landed would be unreadable interactively and unusable as a golden,
  /// because the order would depend on which worker won a race.
  /// </param>
  public static TestExecution Run(
      IReadOnlyList<TestFileGroup> groups, IReadOnlySet<int> selected, TestRunOptions options,
      Action<TestFileResults>? onFileComplete) {
    var shards = BuildShards(groups, selected, options.Isolate);
    var collected = groups.Select(_ => new List<UnitTestResult>()).ToArray();

    // DERIVED from the shards rather than recomputed from `selected`. Both would answer "how many
    // of this file's tests are in this run", and if the two ever disagreed the count would never
    // reach zero — stalling the flush and silently dropping every file from there on while the run
    // still exited 0. One computation, so there is nothing to disagree with.
    var pending = new int[groups.Count];
    foreach (var shard in shards) pending[shard.FileIndex] += shard.TestIndices.Count;

    // Built ONCE, not per shard. Every shard needs to resolve an index back to its test, and doing
    // that by rebuilding the whole map each time is work proportional to the WHOLE SUITE paid once
    // per shard — quadratic in a project's size for a lookup that never changes. Read-only from
    // here on, so sharing it across workers needs no lock.
    var lookup = BuildLookup(groups);

    var nextShard = -1;
    var failures = 0;
    var bailed = false;
    var flushLock = new object();
    var nextToFlush = 0;

    var sw = System.Diagnostics.Stopwatch.StartNew();

    void Worker() {
      while (true) {
        // A bail is checked BEFORE claiming, so shards already in flight finish and report. Killing
        // them would throw away results that were already paid for.
        if (options.BailAfter is { } limit && Volatile.Read(ref failures) >= limit) {
          Volatile.Write(ref bailed, true);
          return;
        }

        var claimed = Interlocked.Increment(ref nextShard);
        if (claimed >= shards.Count) return;

        var shard = shards[claimed];
        var results = RunShardGuarded(shard, lookup, options);

        var newFailures = results.Count(r => r.IsFailure);
        if (newFailures > 0) Interlocked.Add(ref failures, newFailures);

        lock (flushLock) {
          collected[shard.FileIndex].AddRange(results);
          pending[shard.FileIndex] -= shard.TestIndices.Count;
          FlushReadyFiles(groups, collected, pending, ref nextToFlush, onFileComplete);
        }
      }
    }

    // No more threads than there is work for them: the last ones would claim nothing and retire.
    var threads = new List<Thread>();
    for (var i = 0; i < Math.Min(options.Workers, Math.Max(1, shards.Count)); i++) {
      var thread = new Thread(Worker) { IsBackground = true };
      thread.Start();
      threads.Add(thread);
    }
    foreach (var thread in threads) thread.Join();

    sw.Stop();

    // Every worker has retired, so nothing further can arrive and every file is as complete as it
    // will ever be. Saying so lets the flush finish.
    //
    // It matters under --bail, which leaves shards unclaimed: some file's count never reached zero,
    // the flush stalled there, and the summary below still counted every result collected — so the
    // run printed N per-test lines and then a total of M > N, with failure detail for tests whose
    // lines never appeared. This is also what covers a file left PARTIALLY done, which zeroing only
    // the untouched files would not.
    Array.Fill(pending, 0);
    FlushReadyFiles(groups, collected, pending, ref nextToFlush, onFileComplete);

    var files = new List<TestFileResults>();
    for (var f = 0; f < groups.Count; f++) {
      if (collected[f].Count == 0) continue;
      // Sorted back into declaration order: a file's tests may have been split across shards under
      // --isolate and finished in any order, but the file declares them in one.
      collected[f].Sort((a, b) => a.Index.CompareTo(b.Index));
      files.Add(new TestFileResults(groups[f].Path, collected[f]));
    }

    return new TestExecution(files, sw.ElapsedMilliseconds, Volatile.Read(ref bailed));
  }

  /// <summary>
  /// Run one shard, turning any exception into results rather than letting it escape.
  ///
  /// A worker runs on a bare <see cref="Thread"/>, and an unhandled exception in one TERMINATES THE
  /// PROCESS — taking with it every result already collected and replacing this command's documented
  /// 0/1/2 with a CLR crash. The paths are real, not theoretical: <c>Process.Start</c> throws
  /// <c>Win32Exception</c> when the binary is locked (an antivirus scanner, a second `maxon test` in
  /// the same tree), and the reader tasks can fault on a broken pipe after a timeout kill, which
  /// <c>--timeout</c> makes routine.
  ///
  /// The shard's tests are reported as not run, naming the exception. That is the truth — they were
  /// selected and nothing observed them — and it keeps the rest of the run reportable.
  ///
  /// The handler's own <c>lookup[index]</c> cannot throw in turn: the shards and the lookup are both
  /// derived from ONE list of groups, so every index a shard holds is a key the lookup has.
  /// <see cref="OutOfMemoryException"/> is deliberately not caught — there is nothing useful to do
  /// with it, and building a result list is exactly the wrong response.
  /// </summary>
  private static List<UnitTestResult> RunShardGuarded(
      Shard shard, IReadOnlyDictionary<int, DiscoveredTest> lookup, TestRunOptions options) {
    try {
      return ExecuteShard(shard.TestIndices, lookup, options);
    } catch (Exception ex) when (ex is not OutOfMemoryException) {
      return [.. shard.TestIndices.Select(index => new UnitTestResult(
        index, lookup[index], TestOutcome.DidNotRun, 0, null,
        $"the harness failed to run this shard: {ex.GetType().Name}: {ex.Message}"))];
    }
  }

  /// <summary>
  /// Emit every file that is finished AND has no unfinished file before it. Called under the
  /// caller's lock; <paramref name="nextToFlush"/> is the low-water mark that makes the sequence a
  /// prefix rather than a set — which is what turns parallel execution back into discovery order.
  /// </summary>
  private static void FlushReadyFiles(
      IReadOnlyList<TestFileGroup> groups, List<UnitTestResult>[] collected, int[] pending,
      ref int nextToFlush, Action<TestFileResults>? onFileComplete) {
    while (nextToFlush < groups.Count) {
      var index = nextToFlush;
      if (pending[index] > 0) return;

      if (collected[index].Count > 0 && onFileComplete != null) {
        onFileComplete(new TestFileResults(groups[index].Path,
          [.. collected[index].OrderBy(r => r.Index)]));
      }
      nextToFlush++;
    }
  }

  private static List<Shard> BuildShards(
      IReadOnlyList<TestFileGroup> groups, IReadOnlySet<int> selected, bool isolate) {
    var shards = new List<Shard>();
    for (var f = 0; f < groups.Count; f++) {
      var indices = groups[f].Tests.Where(t => selected.Contains(t.Index)).Select(t => t.Index).ToList();
      if (indices.Count == 0) continue;

      if (isolate) {
        foreach (var index in indices) shards.Add(new Shard(f, [index]));
      } else {
        shards.Add(new Shard(f, indices));
      }
    }
    return shards;
  }

  /// <summary>
  /// Run one shard to completion, launching as many processes as attribution requires.
  ///
  /// The loop exists because one launch cannot always answer for every test it was given: a process
  /// that dies mid-shard leaves the tests after the casualty unobserved, and the only honest way to
  /// learn what they do is to run them. It terminates because every iteration either produces a
  /// result for at least one test or gives up on the rest by name.
  /// </summary>
  private static List<UnitTestResult> ExecuteShard(
      IReadOnlyList<int> testIndices, IReadOnlyDictionary<int, DiscoveredTest> lookup,
      TestRunOptions options) {
    var results = new List<UnitTestResult>();
    var remaining = testIndices.ToList();

    while (remaining.Count > 0) {
      var launch = Launch(options, remaining);
      var attributed = Attribute(remaining, launch, lookup, options);
      results.AddRange(attributed.Finished);

      if (attributed.NotRun.Count == remaining.Count) {
        // No progress: this launch answered for nothing, so running the same set again would loop.
        // Say so against each test rather than retrying forever or quietly dropping them.
        foreach (var index in attributed.NotRun) {
          results.Add(new UnitTestResult(index, lookup[index], TestOutcome.DidNotRun, 0, null,
            attributed.ShardOutput.Length > 0 ? attributed.ShardOutput : NothingStartedText));
        }
        break;
      }

      remaining = attributed.NotRun;
    }

    return CleanUpCrashOutput(results, testIndices.Count, options, lookup);
  }

  /// <summary>
  /// Re-run each crashed test ALONE, so its report carries only its own output.
  ///
  /// A crash's stderr is the one report a reader most needs to be exact, and in a shared process it
  /// is the least exact: every test that ran before it in that shard wrote to the same stream.
  ///
  /// A shard that held ONE test to begin with is left alone — its output already belongs to it, and
  /// spawning again would cost a process to learn nothing. That is judged from the shard's original
  /// size, not from how many results came back, because a shard whose first test crashed also
  /// produces exactly one result.
  /// </summary>
  private static List<UnitTestResult> CleanUpCrashOutput(
      List<UnitTestResult> results, int shardSize, TestRunOptions options,
      IReadOnlyDictionary<int, DiscoveredTest> lookup) {
    if (shardSize <= 1) return results;

    for (var i = 0; i < results.Count; i++) {
      if (results[i].Outcome != TestOutcome.Crashed) continue;

      var index = results[i].Index;
      var launch = Launch(options, [index]);
      var isolated = Attribute([index], launch, lookup, options);

      var alone = isolated.Finished.FirstOrDefault(r => r.Index == index);
      if (alone == null) continue;

      results[i] = results[i] with {
        Output = alone.Output,
        // A crash that does not reproduce alone is an ORDER dependence, which is the diagnosis. The
        // verdict stands either way: this test did take a process down.
        RecoveredInIsolation = alone.Outcome != TestOutcome.Crashed,
      };
    }
    return results;
  }

  private static Dictionary<int, DiscoveredTest> BuildLookup(IReadOnlyList<TestFileGroup> groups) {
    var lookup = new Dictionary<int, DiscoveredTest>();
    foreach (var group in groups) {
      foreach (var test in group.Tests) lookup[test.Index] = test.Test;
    }
    return lookup;
  }

  private static ProcessRunResult Launch(TestRunOptions options, IReadOnlyList<int> indices) =>
    ProcessLauncher.Run(new ProcessLaunchRequest {
      ExecutablePath = options.BinaryPath,
      Arguments = ProcessArguments.Of(SelectFlag + FormatSelection(indices)),
      TimeoutMs = options.TimeoutMs,
    });

  /// <summary>What one launch settled, and what it left open.</summary>
  private sealed record Attribution(
    List<UnitTestResult> Finished, List<int> NotRun, string ShardOutput);

  /// <summary>
  /// Turn one launch into results.
  ///
  /// The marker stream is read in order, and that ORDER is the whole attribution: output and `threw`
  /// reports belong to whichever test last began, and a begin with no matching end names the test
  /// that was running when the process stopped.
  /// </summary>
  private static Attribution Attribute(
      IReadOnlyList<int> expected, ProcessRunResult launch,
      IReadOnlyDictionary<int, DiscoveredTest> lookup, TestRunOptions options) {
    var events = Parse(launch.Stderr);

    var partials = new Dictionary<int, PartialResult>();
    var shardOutput = new StringBuilder();

    // The test currently BETWEEN its markers — its index and its partial result in one value, or
    // null when no test is running. One variable because it is one fact: it was two (a `current`
    // partial beside an `inFlight` index) that were only ever assigned and cleared together, so the
    // end-marker guard had to test both and agree with itself. Split like that, an edit that
    // cleared one and not the other would not fail — it would mis-attribute every event after it,
    // and a mis-attributed report is exactly the answer this command exists to be trusted about.
    (int Index, PartialResult Result)? inFlight = null;

    foreach (var evt in events) {
      switch (evt) {
        case TestBegan began: {
          var partial = new PartialResult();
          partials[began.Index] = partial;
          inFlight = (began.Index, partial);
          break;
        }

        case TestEnded ended when inFlight?.Index == ended.Index: {
          var partial = inFlight.Value.Result;
          partial.Ended = true;
          partial.Passed = ended.Passed;
          partial.Nanos = ended.Nanos;
          inFlight = null;
          break;
        }

        case TestEnded:
          // An end for a test that did not begin, or for the wrong one. The dispatcher cannot emit
          // that, so it means the stream is damaged; leaving `inFlight` set reports the test that
          // did begin as crashed, which is the loud outcome this deserves.
          break;

        case TestThrew threw when inFlight is { } running:
          running.Result.Thrown =
            new ThrownError(threw.ErrorType, threw.ErrorCase, threw.File, threw.Line);
          break;

        case TestThrew orphan:
          // A report belonging to no test. The compiler emits `threw` only inside a test body and
          // the dispatcher brackets every one, so this means the stream is damaged — kept as shard
          // output rather than dropped, because a discarded error report is the one thing a reader
          // would most want back when trying to explain why the run made no sense.
          Append(shardOutput, $"{orphan.ErrorType}.{orphan.ErrorCase} reported at "
            + $"{orphan.File}:{orphan.Line} outside any test");
          break;

        case TestOutputLine line:
          Append(inFlight?.Result.Output ?? shardOutput, line.Text);
          break;

        default:
          throw new InvalidOperationException($"Unhandled test event {evt.GetType().Name}");
      }
    }

    // A launcher outcome that produced no usable stream is a measurement failure, not a verdict.
    // REPLACES whatever the launcher put in the stream rather than appending to it: that outcome
    // substitutes its own wording ("Stream read timed out") for the output, which parses as one
    // ordinary output line — so the previously-guarded `events.Count == 0` was never true, and the
    // lower layer's phrasing reached the report instead of this one's.
    if (launch.Outcome == ProcessRunOutcome.OutputReadTimedOut) {
      shardOutput.Clear();
      shardOutput.Append(NoOutputCapturedText);
    }

    // A binary that never started produced no stream at all, so every test in the shard comes back
    // unclaimed and `ExecuteShard`'s no-progress arm reports them as DidNotRun. That is the right
    // verdict — nothing observed them — but without this the REASON is lost: the launcher used to
    // throw, and `RunShardGuarded` put the exception's message in each result. Now it returns, so
    // the message has to be carried here or the report reads "nothing started" with no cause.
    if (launch.Outcome == ProcessRunOutcome.LaunchFailed) {
      shardOutput.Clear();
      shardOutput.Append($"the test binary could not be launched: {launch.Stderr}");
    }

    // Materialized ONCE. `StringBuilder.ToString()` copies the whole buffer on every call and caches
    // nothing, so asking for it inside the loop below made the shard's stray output cost
    // O(tests x bytes) to hand out — a product of two things that both grow with a project, for a
    // value that is the same string every time and that `BuildResult` reads on one branch only (a
    // test that began, never ended, was not killed, and wrote nothing of its own). The builder is
    // complete by here: nothing after the OutputReadTimedOut substitution above writes to it, and
    // `AttributeLeak` re-launches processes without touching it.
    var strayOutput = shardOutput.ToString();

    var finished = new List<UnitTestResult>();
    var notRun = new List<int>();

    foreach (var index in expected) {
      if (!partials.TryGetValue(index, out var partial)) {
        notRun.Add(index);
        continue;
      }

      finished.Add(BuildResult(index, lookup[index], partial, launch, strayOutput));
    }

    var leaked = launch.Outcome == ProcessRunOutcome.Exited && launch.IsMemoryLeak;
    if (leaked) finished = AttributeLeak(finished, options);

    return new Attribution(finished, notRun, strayOutput);
  }

  /// <summary>
  /// Decide one test's outcome from its markers plus how the process ended.
  /// </summary>
  /// <param name="shardOutput">
  /// The stream's non-protocol text that belonged to no test — the fallback for a test that died
  /// without writing anything of its own. It is used rather than <c>launch.Stderr</c>, which is the
  /// RAW stream: falling back to that put the wire protocol itself into the user's report, so a
  /// silently-crashing test was reported as its own begin marker.
  /// </param>
  private static UnitTestResult BuildResult(
      int index, DiscoveredTest test, PartialResult partial, ProcessRunResult launch,
      string shardOutput) {
    if (partial.Ended) {
      return new UnitTestResult(index, test, partial.Passed ? TestOutcome.Passed : TestOutcome.Failed,
        partial.Nanos, partial.Thrown, partial.Output.ToString());
    }

    // Began and never ended. Which of the two ways the process stopped is the launcher's to say —
    // it is the only party that knows whether it killed the child or found it dead.
    var timedOut = launch.Outcome == ProcessRunOutcome.TimedOut;
    var outcome = timedOut ? TestOutcome.TimedOut : TestOutcome.Crashed;

    // A killed test wrote no reason of its own — it was still running. Say why in this harness's
    // words rather than passing through the launcher's, so the report does not depend on the
    // wording of a lower layer.
    var output = partial.Output.ToString();
    if (timedOut) output = output.Length == 0 ? KilledByTimeoutText : $"{output}\n{KilledByTimeoutText}";
    else if (output.Length == 0) output = shardOutput;

    return new UnitTestResult(index, test, outcome, 0, partial.Thrown, output);
  }

  /// <summary>
  /// Find which test leaked.
  ///
  /// The leak check runs at process exit and substitutes its own exit code, so it is a fact about
  /// the PROCESS and names nobody. With more than one test in it there is no evidence to reason
  /// from — so each is re-run alone, which is the only thing that turns a process-global signal into
  /// a test-level one. A test that already FAILED keeps its failure: the assertion is the actionable
  /// report, and a leak on the way out of a failing test is a consequence of it.
  /// </summary>
  private static List<UnitTestResult> AttributeLeak(List<UnitTestResult> finished, TestRunOptions options) {
    // One test in the process: it is the only candidate, so no re-run can add anything.
    if (finished.Count == 1) {
      return [.. finished.Select(r =>
        r.Outcome == TestOutcome.Passed ? r with { Outcome = TestOutcome.Leaked } : r)];
    }

    var updated = new List<UnitTestResult>(finished.Count);
    foreach (var result in finished) {
      if (result.Outcome != TestOutcome.Passed) {
        updated.Add(result);
        continue;
      }

      var launch = Launch(options, [result.Index]);
      var leakedAlone = launch.Outcome == ProcessRunOutcome.Exited && launch.IsMemoryLeak;
      updated.Add(leakedAlone ? result with { Outcome = TestOutcome.Leaked } : result);
    }

    return updated;
  }

  private static void Append(StringBuilder sb, string line) {
    if (sb.Length > 0) sb.Append('\n');
    sb.Append(line);
  }

  /// <summary>A result being assembled as the marker stream is walked.</summary>
  private sealed class PartialResult {
    public bool Ended;
    public bool Passed;
    public long Nanos;
    public ThrownError? Thrown;
    public StringBuilder Output { get; } = new();
  }
}
