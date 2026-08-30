using System.Diagnostics;

namespace MaxonSharp.Testing;

/// <summary>
/// Drops this process — and therefore every process it spawns — to background scheduling priority,
/// so a test run does not make the machine unusable while it is going.
/// </summary>
/// <remarks>
/// <para>
/// WHY PRIORITY RATHER THAN FEWER WORKERS. The obvious lever is to run fewer workers and leave a core
/// free, and it is the weaker one: it gives up throughput UNCONDITIONALLY, including on an idle box,
/// and it still does not buy responsiveness, because the OS does not hold a core aside — it
/// time-slices, so the reserved core is contended the moment anything else wants it. Priority inverts
/// both halves: an idle machine gives the run every core it has, and a foreground process preempts it
/// the instant one appears. <see cref="TestExecutor.DefaultWorkers"/> is sized for throughput for
/// exactly this reason, and says so.
/// </para>
/// <para>
/// WHY THIS PROCESS AND NOT THE JOB OBJECT. <see cref="WindowsJobObject"/> could carry a priority
/// class, and it would cover the wrong half: this harness COMPILES IN-PROCESS — the parallel pass
/// driver, the fragment compiles — and only LAUNCHES the finished binaries, which are the cheap part.
/// Lowering the parent covers the compilation and the children both, because a child inherits the
/// parent's priority class on Windows and the parent's nice value under POSIX. The job object is also
/// shared with the MCP server's orphan guard, which must NOT be demoted with it.
/// </para>
/// <para>
/// WHY BELOW-NORMAL AND NOT IDLE. Idle-priority work can be starved for as long as something else
/// wants the CPU, and the harnesses bound how long they will wait — a per-test timeout here, and a
/// multi-minute wedge watchdog in the shv2 pool. A starve long enough to trip either is reported as a
/// HARNESS failure, which is a wrong answer pointed at the wrong file. Below-normal yields to
/// anything at normal priority and still gets scheduled.
/// </para>
/// </remarks>
internal static class HostPriority {
  /// <summary>
  /// Lower this process to background priority. Safe to call once per process, before any worker
  /// thread or child process exists.
  /// </summary>
  /// <remarks>
  /// A refusal is REPORTED AND SURVIVED rather than thrown: an OS that declines to renice us is not a
  /// reason to fail a test run, but it is a reason the operator should know why the box got busy.
  /// </remarks>
  public static void EnterBackground() {
    try {
      using var self = Process.GetCurrentProcess();
      self.PriorityClass = ProcessPriorityClass.BelowNormal;
    } catch (Exception ex) when (ex is PlatformNotSupportedException
                                    or InvalidOperationException
                                    or System.ComponentModel.Win32Exception) {
      Console.Error.WriteLine(
        $"maxon: could not lower this process to background priority ({ex.Message}); "
        + "the run continues at normal priority and may make the machine sluggish.");
    }
  }
}
