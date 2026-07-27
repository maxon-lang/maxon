using System.Diagnostics;

namespace MaxonSharp.Debug;

/// <summary>
/// How a profile run is CONFIGURED, as both faces spell it. One record, so the CLI's flags and the MCP
/// tool's arguments cannot come to mean different things by the same word.
/// </summary>
/// <param name="RateHz">Samples per second per running thread.</param>
/// <param name="MinPercent">The share below which the printed tables summarize rather than list.</param>
/// <param name="TargetEnv">
/// Variables set in the PROFILED program's environment, applied over the inherited one. It is here for
/// the reason `maxon debug` has the same option: `MAXON_MAX_PROCS` decides how many worker processors
/// the scheduler starts, and a green-thread profile taken without pinning it is a measurement of the
/// machine as much as of the program.
/// </param>
public readonly record struct ProfileOptions(TimeSpan Timeout, double RateHz, double MinPercent,
  IReadOnlyDictionary<string, string>? TargetEnv);

/// <summary>
/// Running a program under the sampler and joining what it caught with the binary's debug info — the
/// steps BOTH faces perform, in one place. The CLI (`maxon profile`) and the MCP family differ only in
/// where the program's own output goes and how a refusal is delivered.
///
/// Nothing here is instrumented into the target and nothing is asked of the debug agent: a profiled
/// binary is the SAME binary, built the same way, and can be one that is already in production. That is
/// the whole reason a sampler was built rather than reusing the phase hooks — see
/// <see cref="ProfileSampler"/>.
/// </summary>
public static class ProfileRunner {

  /// <summary>
  /// The default bound on a profiled run, the same ten minutes and the same reason as coverage's: it
  /// exists only so a target that never finishes cannot hang the tool that launched it. Unlike
  /// coverage, reaching it is not fatal to the measurement — the samples taken up to that point are
  /// real, and the report says the run was PARTIAL rather than throwing them away.
  /// </summary>
  public static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromMinutes(10);

  /// <summary>
  /// The default sampling rate. A kilohertz gives a thousand readings per second of runtime, so even a
  /// one-second program clears <see cref="ProfileReport.MinimumTrustworthySamples"/> by an order of
  /// magnitude, while costing the target only a suspend/resume pair per millisecond per running thread.
  ///
  /// ⚠ WHAT THAT PAIR COSTS, MEASURED rather than assumed, because "negligible" was the assumption and
  /// it is not negligible. Against a NULL CONTROL — the same launcher, the same attach, the same module
  /// snapshot, only <c>--rate=1</c> so nothing is ever sampled — a CPU-bound single-threaded target runs
  /// <b>+8.1%</b> longer at this rate (median 0.9326 s vs 0.8630 s, n=21 per arm, arms order-randomized
  /// within each repetition, ranges disjoint). The curve to it is not linear and it saturates:
  /// +1.2% at 100 Hz, +4.9% at 250, +7.1% at 500, +8.1% at 1000. Only about a third of that is the
  /// suspension itself (23 us held x 927 samples = 21 ms of the 70 ms); the rest is what suspending and
  /// resuming does to a thread's scheduling and caches, which is not something a sampler can avoid — it
  /// is the price of the measurement, and it is worth knowing rather than assuming away.
  /// </summary>
  public const double DefaultRateHz = 1000;

  /// <summary>
  /// The fastest rate this accepts. Refused rather than silently clamped, which would report a rate that
  /// was not used.
  ///
  /// ⚠⚠ THIS CEILING IS NOT THE ONE THAT BINDS. **The real ceiling is the OPERATING SYSTEM's, not the
  /// sampler's**, and it sits near 1.75 kHz — far below this number. Stated here because this is the
  /// constant a reader questions when a profile does not collect what they asked for.
  ///
  /// The reason written here used to be the suspend/resume share, and that is measurably not it. The
  /// sampler's own per-tick work is 48-57 us, good for ~18 kHz; what caps the loop is the granularity of
  /// the OS waitable timer it ticks on. MEASURED: at <c>--rate=5000</c> and <c>--rate=10000</c>
  /// essentially every tick is already past its deadline (1609/1610 and 1616/1617) and the wait STILL
  /// returns after ~520 us, so both collect ~1,750 ticks/second. It does not scale with the number of
  /// running threads (a two-processor green-thread run reads the same 517 us), which is what identifies
  /// the timer rather than the work.
  ///
  /// Two things were considered and REFUSED, both deliberately:
  ///   * Lowering this to ~2000. ~0.5 ms is THIS host's granularity, not a portable constant, so it
  ///     would trade an honest over-ask for a promise about hardware nobody has measured.
  ///   * Delivering the nominal rate by skipping waits whose deadline has already passed. That returns
  ///     the requested COUNT as a burst, and 50 readings 20 us apart are not 50 readings — a sampler
  ///     that inflates its count with non-independent observations is worse than one that under-delivers.
  ///
  /// What is done instead: the report states the rate it ACHIEVED
  /// (<see cref="ProfileReport.AchievedRateHz"/>) beside the one requested, so over-asking past the
  /// timer's granularity is self-documenting — the reader sees exactly what they got.
  /// </summary>
  public const double MaxRateHz = 10000;

  /// The default reporting floor. One percent of a kilohertz run is ten samples a second — below that a
  /// row is noise, and listing it would make two runs of one program produce different reports.
  public const double DefaultMinPercent = 1.0;

  public const string TimeoutFlag = "--timeout=";
  public const string RateFlag = "--rate=";
  public const string MinPercentFlag = "--min-percent=";

  // Deliberately NO "raise the deadline and run again" recourse, unlike its coverage twin. A coverage
  // run that hits its deadline has NOTHING to report — the counters are written by the program on its
  // way out — so it refuses and offers a longer deadline. A profile that hits its deadline has every
  // sample it took, so it reports them as PARTIAL. There is no refusal to attach recourse to, and a
  // constant that reads well by analogy but nothing ever prints is exactly the dead claim this tree's
  // error-code registry refuses elsewhere.

  public static ProfileOptions Defaults =>
    new(DefaultRunTimeout, DefaultRateHz, DefaultMinPercent, TargetEnv: null);

  /// <see cref="DefaultRunTimeout"/> as the usage banner prints it, through the one seconds rule.
  public static string DefaultRunTimeoutText => PositiveSeconds.Text(DefaultRunTimeout);

  /// <summary>
  /// Why a rate is not usable, or null. Refused rather than clamped for the reason every deadline in
  /// this tree is: a rate the caller did not ask for, reported back as though they had, is a
  /// measurement setting that silently is not in force.
  /// </summary>
  /// <remarks>
  /// ⚠ The wording is the one <see cref="MaxRateHz"/> documents, and that is not a style point. This
  /// sentence used to blame the suspend/resume pair — the reason the constant was FIRST given and that
  /// the optimizing pass then MEASURED to be false (the per-tick work is 48-57 us, good for ~18 kHz).
  /// The measurement corrected the constant's own comment and left this copy standing, so the only
  /// statement a USER ever saw was the one the code already knew was wrong. Two spellings of one fact,
  /// and the wrong one was the one that shipped.
  /// </remarks>
  public static string? RateRefusal(double rateHz) =>
    double.IsNaN(rateHz) || rateHz <= 0 ? "needs a positive number of samples per second"
    : rateHz > MaxRateHz ? $"cannot exceed {MaxRateHz} samples per second. Note that asking for anything"
      + " near it is already an over-ask: the loop ticks on an OS waitable timer whose granularity caps"
      + " it near 1750 Hz on a typical host, and the report states the rate it ACHIEVED beside the one"
      + " requested so you can see what you got"
    : null;

  /// Why a reporting floor is not usable, or null. 100% would hide everything including the root, which
  /// is a report that has measured something and shows none of it.
  public static string? MinPercentRefusal(double minPercent) =>
    double.IsNaN(minPercent) || minPercent < 0 || minPercent >= 100
      ? "needs a share in [0, 100) — it is the percentage below which a row is summarized rather than listed"
      : null;

  /// <summary>
  /// Profile <paramref name="exePath"/>: validate its debug info, launch it, sample it until it exits
  /// or the deadline passes, and join what was caught into a report. Null with a refusal naming the
  /// defect when the run could not be measured at all.
  ///
  /// The sidecar is loaded and vouched for BEFORE anything is launched, deliberately: a profile with no
  /// symbols is a list of addresses, so running the program first would burn the user's time to arrive
  /// at a refusal that was knowable up front.
  /// </summary>
  public static ProfileReport? Run(string exePath, IReadOnlyList<string> targetArgs, ProfileOptions options,
      Action<string> onOutput, out string error) {
    if (ProfileSampler.UnsupportedReason is { } unsupported) {
      error = unsupported;
      return null;
    }

    // Asked before the sidecar is read, so a target that is simply not there is reported as that rather
    // than as whatever the sidecar loader makes of a file it cannot open.
    if (TargetLauncher.MissingExecutable(exePath) is { } missing) {
      error = missing;
      return null;
    }

    var sidecar = MxdbgSidecar.TryLoad(exePath,
      "a profile needs it to name the code its samples land in", out _, out error);
    if (sidecar == null) return null;

    if (!BinaryBuildId.TryLocateTextSection(exePath, out uint textImageOffset, out uint textSize, out error))
      return null;

    SamplingSession? session = null;
    var run = TargetLauncher.Run(exePath, targetArgs, options.Timeout, onOutput,
      process => session = SamplingSession.Start(process, sidecar, textImageOffset, textSize, options.RateHz),
      options.TargetEnv);

    if (run.ExitCode is null && !run.TimedOut) {
      error = run.Error;
      return null;
    }

    if (session == null) {
      // Reached only when the sampler could not attach to a process that DID start — a target that was
      // gone before its module list could even be read. Reported rather than rendered as a profile of
      // nothing, which is what a zero-sample report would look like.
      error = $"{ReportPath.Display(exePath)} could not be sampled: it ended before the sampler attached."
        + " Profile a program that runs long enough to be measured.";
      return null;
    }

    // A sampler that stopped early collected a PREFIX, and presenting a prefix as a profile is the
    // "instrument that lies" case: the shares would be real numbers about the wrong interval, with
    // nothing in the report to say so. Refused instead, naming the fault.
    if (session.Fault is { } fault) {
      error = $"the sampler stopped early and this profile would describe only part of the run: {fault}";
      return null;
    }

    error = "";
    return session.Collector.Finish(exePath, run.ExitCode, runCompleted: !run.TimedOut, options.RateHz,
      session.Ticks, session.SampledSeconds, options.MinPercent);
  }

  /// <summary>
  /// The sampler, the thread that drives it, and how long it ran — tied to the lifetime of the run.
  ///
  /// It exists to be DISPOSED by <see cref="TargetLauncher.Run"/>: there is no path out of a launched
  /// run that skips the dispose, so there is no path that leaves a sampler thread alive after its
  /// target is gone — including the timeout kill, which is the one a hand-rolled stop would miss.
  /// </summary>
  private sealed class SamplingSession(ProfileSampler sampler, Thread thread, ProfileCollector collector)
      : IDisposable {

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public ProfileCollector Collector => collector;

    /// <summary>
    /// Why the sampling thread stopped before it was asked to, or null.
    ///
    /// It exists because of what an UNCAUGHT exception on that thread would do, which is worse than
    /// losing a profile: .NET terminates the process on an unhandled background-thread exception, so
    /// this tool would die holding a live debuggee it had spawned — an ORPHAN, and one still suspended
    /// if the fault landed anywhere but the sampling path's own `finally`. Catching it here lets the
    /// normal end-of-run path kill the target and lets the caller refuse honestly.
    /// </summary>
    public string? Fault { get; private set; }

    /// Set once, from the sampling thread, before it ends; read only after <see cref="Dispose"/> has
    /// joined that thread, which is the happens-before edge that makes the plain field safe.
    private void RecordFault(string fault) => Fault = fault;

    /// The window the samples were drawn from — measured around the SAMPLER, not around the process, so
    /// the rate a reader divides by is the rate that was actually in force for this many seconds.
    public double SampledSeconds => _clock.Elapsed.TotalSeconds;

    /// How many times the sampler ticked, over exactly the <see cref="SampledSeconds"/> above — the two
    /// halves of the ACHIEVED rate, taken from the same interval so their ratio means something. Read
    /// only after <see cref="Dispose"/> has joined the sampling thread.
    public long Ticks => sampler.Ticks;

    public static SamplingSession? Start(Process process, MxdbgReader sidecar, uint textImageOffset,
        uint textSize, double rateHz) {
      if (!TryResolveTextBase(process, textImageOffset, out ulong textBase)) return null;

      // From here the sampler owns an event handle and a native CONTEXT allocation, and only Dispose
      // returns them. Nothing between the attach and the successful Start is expected to throw — but
      // "expected" is not a release policy, and the one thing that WOULD throw here is running out of
      // the very resources this holds.
      var sampler = ProfileSampler.Attach(process);
      try {
        // Snapshotted here, while the target is certainly alive and before a single sample is taken, so
        // every foreign frame the run produces can be attributed to the module that owns it.
        var collector = new ProfileCollector(sidecar, textBase, textSize, ProfileModuleMap.Snapshot(process));

        // A dedicated thread rather than the thread pool: this one runs for the whole program and spends
        // its life in a timed wait, which is precisely what a pool thread must not do. Background, so a
        // sampler cannot by itself keep this process alive.
        SamplingSession? session = null;
        var thread = new Thread(() => {
          try {
            sampler.Run(TimeSpan.FromSeconds(1.0 / rateHz), collector.Accept);
          } catch (Exception ex) {
            session!.RecordFault($"{ex.GetType().Name}: {ex.Message}");
          }
        }) {
          IsBackground = true,
          Name = "maxon-profile-sampler",
        };

        // Constructed BEFORE the thread starts, so the delegate above can never observe a null session.
        session = new SamplingSession(sampler, thread, collector);
        thread.Start();
        return session;
      } catch {
        // Ownership has not reached the session yet, so nothing else will ever dispose this.
        sampler.Dispose();
        throw;
      }
    }

    public void Dispose() {
      sampler.Stop();
      thread.Join();
      _clock.Stop();

      // Only after the JOIN: the collector is written from the sampler's thread without a lock and the
      // report is read from this one, and the join is the happens-before edge that makes that safe.
      sampler.Dispose();
    }
  }

  /// <summary>
  /// Where this run's `.text` actually is: the main module's load address plus the section's offset
  /// within the image.
  ///
  /// The base has to come from the LIVE process rather than the PE's preferred `ImageBase`, because the
  /// emitted image sets DYNAMIC_BASE — reading the preferred base would produce a plausible number that
  /// is wrong by the ASLR slide, which symbolizes every sample to a confidently incorrect function.
  ///
  /// The retry is a real race, not defensiveness: `MainModule` reads the loader's module list, which is
  /// briefly empty in a process that has just been created, and it throws rather than waiting.
  /// </summary>
  private static bool TryResolveTextBase(Process process, uint textImageOffset, out ulong textBase) {
    textBase = 0;

    for (int attempt = 0; attempt < ModuleListAttempts; attempt++) {
      try {
        if (process.MainModule is { } module) {
          textBase = (ulong)module.BaseAddress.ToInt64() + textImageOffset;
          return true;
        }
      } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) {
        if (process.HasExited) return false;
      }

      Thread.Sleep(ModuleListRetryMilliseconds);
    }

    return false;
  }

  private const int ModuleListAttempts = 50;
  private const int ModuleListRetryMilliseconds = 10;
}
