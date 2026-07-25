using System.Diagnostics;

namespace MaxonSharp;

/// <summary>
/// A spawned target's forwarded stdio, and the ONE statement of the ORDER a session with one must end
/// in. Both halves are obligations rather than conveniences, and each has been missed on its own:
///
///   * a target that outlives its consumer is an ORPHAN — for `maxon debug`, one still parked in the
///     agent's stop loop with nothing left to release it;
///   * a forwarding task only completes when its pipe CLOSES, so joining while that orphan is alive
///     blocks the consumer FOREVER — the same defect's other half.
///
/// So: wait out the grace period, kill what is left, wait for the kill to settle, and only then join.
/// It is stated here once because THREE places owe it — the debugger's session end, the monitor's
/// normal exit, and the monitor's schema-mismatch refusal — and the third is how the pairing came to
/// be hand-rolled a third time in a different subsystem.
///
/// The grace period is the whole difference between those callers: a target that was RELEASED and is
/// finishing the user's program deserves to be waited for (its remaining runtime is the program's
/// business), while one the consumer is abandoning does not.
/// </summary>
internal sealed class TargetStdio {

  private readonly Process _process;
  private readonly Task _stdout;
  private readonly Task _stderr;

  private TargetStdio(Process process, Task stdout, Task stderr) {
    _process = process;
    _stdout = stdout;
    _stderr = stderr;
  }

  /// <summary>
  /// Start forwarding <paramref name="process"/>'s stdout and stderr, a line at a time. The sinks are
  /// callbacks rather than writers because the two consumers need different disciplines around the
  /// write itself — the monitor interleaves the target's stdout with its own trace lines under a lock,
  /// the debugger writes straight through to whichever stream its face reserved for the target.
  /// </summary>
  public static TargetStdio Forward(Process process, Action<string> stdoutLine, Action<string> stderrLine) {
    var stdout = Task.Run(() => {
      var sr = process.StandardOutput;
      while (sr.ReadLine() is { } line) stdoutLine(line);
    });
    var stderr = Task.Run(() => {
      var sr = process.StandardError;
      while (sr.ReadLine() is { } line) stderrLine(line);
    });
    return new TargetStdio(process, stdout, stderr);
  }

  /// <summary>
  /// End the session: give the target <paramref name="graceMilliseconds"/> to finish on its own
  /// (<see cref="Timeout.Infinite"/> to wait as long as it takes), kill it if it does not, and then
  /// drain the forwarded stdio. Returns true when THIS call killed it, which is the one fact a caller
  /// cannot recover afterwards — a killed target's exit status is the OS's, not the program's answer.
  ///
  /// Idempotent: a second call finds the target already gone, kills nothing, and joins tasks that have
  /// already completed.
  /// </summary>
  public bool EndAndJoin(int graceMilliseconds) {
    bool killed = false;

    if (!_process.WaitForExit(graceMilliseconds)) {
      killed = Kill();
      // Kill only REQUESTS termination; wait so HasExited / ExitCode are settled before a caller reports.
      _process.WaitForExit();
    }

    Join();
    return killed;
  }

  /// Kill the target and everything it spawned — a debuggee's children are as much this session's
  /// orphans as the debuggee itself. Tolerates it exiting between the check and the kill (a normal race).
  private bool Kill() {
    try {
      if (_process.HasExited) return false;
      _process.Kill(entireProcessTree: true);
      return true;
    } catch (InvalidOperationException) {
      // The process exited between the HasExited check and Kill — already gone, nothing to do.
      return false;
    }
  }

  private void Join() {
    try {
#pragma warning disable VSTHRD002 // synchronous entry point, no SyncContext to deadlock against
      _stdout.Wait();
      _stderr.Wait();
#pragma warning restore VSTHRD002
    } catch (AggregateException) {
      // A forwarding task faulted — the sink was closed, or the pipe broke as the target died. Its
      // remaining output is lost either way, and this runs from Dispose paths too, where rethrowing
      // would MASK the exception that ended the session. Waiting has observed the fault, so it cannot
      // resurface.
    }
  }
}
