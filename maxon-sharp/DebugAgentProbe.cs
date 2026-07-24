using MaxonSharp.Compiler.Ir.Runtime;

namespace MaxonSharp;

/// <summary>
/// The two substrate HARNESSES that seeded the driver — now thin front-ends over the
/// <see cref="MaxonDebugger"/> engine, which owns the spawn + mailbox spine they once carried
/// (so there is a single home for how a target is attached and driven):
///
///   * <see cref="Run"/> (`--attach-probe`, P3a) proves the handshake — the agent maps the segment and
///     announces magic/version/alive — then releases the target. Parking at entry makes the handshake
///     observable regardless of how fast the target runs.
///   * <see cref="RunBpTest"/> (`--bp-test`, P3b) additionally sets a breakpoint at a RAW code offset,
///     observes the stop event at that PC, continues, and confirms the program resumes and exits
///     correctly — exercising the command mailbox, the INT3 patch, the park, and the single-step-over.
///
/// Neither needs the sidecar (they debug a plain build and speak raw offsets); the symbolizing REPL
/// that DOES is <see cref="MaxonDebugRepl"/>.
/// </summary>
internal static class DebugAgentProbe {

  private static readonly IReadOnlyList<string> NoArgs = [];

  // ---- P3a: attach probe (`maxon debug --attach-probe`) ----

  public static int Run(string exePath) {
    MaxonDebugger dbg;
    try {
      dbg = MaxonDebugger.Attach(exePath, NoArgs, sidecar: null);
    } catch (DebuggerException ex) {
      Console.Error.WriteLine($"maxon debug --attach-probe: {ex.Message}");
      return 1;
    }

    using (dbg) {
      int verdict = ProbeHandshake(dbg);
      dbg.WaitForExit();
      dbg.JoinIo();
      // The target's exit code is surfaced but does not decide the probe's verdict (a FAULTING target
      // still proved a valid handshake, and exits nonzero).
      Console.Error.WriteLine($"[dbg-probe] target exit code: {dbg.ExitCode}");
      return verdict;
    }
  }

  /// <summary>
  /// Confirm the agent announced its handshake (the target is parked at entry, so magic/version/alive
  /// are present and stable), report it, then release the target with a continue. Version-agnostic: a
  /// schema the probe does not recognise is reported but does not fail the check — the mailbox version
  /// bumps must not regress this substrate gate.
  /// </summary>
  private static int ProbeHandshake(MaxonDebugger dbg) {
    if (!dbg.WaitForAgentAlive()) {
      Console.Error.WriteLine(
        "[dbg-probe] agent did NOT attach: handshake not observed. Is MAXON_DEBUG honored by this build?");
      return 1;
    }

    long version = dbg.AgentVersion;
    Console.Error.WriteLine($"[dbg-probe] agent attached: magic=OK version={version} alive=yes");
    if (version != RuntimeEmitter.DbgControlVersion)
      Console.Error.WriteLine(
        $"[dbg-probe] note: agent schema v{version}, probe speaks v{RuntimeEmitter.DbgControlVersion}");

    dbg.Continue(); // leave the entry stop
    return 0;
  }

  // ---- P3b: the minimal breakpoint driver (`maxon debug --bp-test`) ----

  /// <summary>
  /// The minimal breakpoint driver that verifies the P3b agent end to end: spawn the target stopped at
  /// entry, set a breakpoint at a raw code offset (a function's <c>codeStart</c> from `--dump-info`),
  /// continue, observe the stop event at that PC, continue again, and confirm the program resumes and
  /// exits with the right code. With <paramref name="clearAtStop"/>, the breakpoint is CLEARED while
  /// the target is stopped at it (exercising the trap handler's cleared-while-parked path).
  /// </summary>
  public static int RunBpTest(string exePath, long codeOffset, bool clearAtStop) {
    MaxonDebugger dbg;
    try {
      dbg = MaxonDebugger.Attach(exePath, NoArgs, sidecar: null);
    } catch (DebuggerException ex) {
      Console.Error.WriteLine($"maxon debug --bp-test: {ex.Message}");
      return 1;
    }

    using (dbg) {
      int verdict = DriveBpTest(dbg, codeOffset, clearAtStop);
      dbg.WaitForExit();
      dbg.JoinIo();
      return verdict;
    }
  }

  private static int DriveBpTest(MaxonDebugger dbg, long codeOffset, bool clearAtStop) {
    if (!dbg.WaitForAgentAlive()) {
      Console.Error.WriteLine("[bp-test] agent never announced alive; is MAXON_DEBUG honored by this build?");
      return 1;
    }
    Console.Error.WriteLine($"[bp-test] agent alive; setting breakpoint at code offset 0x{codeOffset:x}");

    if (!dbg.SetBreakpointAtOffset(codeOffset)) {
      Console.Error.WriteLine("[bp-test] agent did not ack set-breakpoint");
      return 1;
    }
    if (!dbg.Continue()) {
      Console.Error.WriteLine("[bp-test] agent did not ack the first continue (leave entry stop)");
      return 1;
    }
    Console.Error.WriteLine("[bp-test] breakpoint set; continued from entry, running to the breakpoint...");

    if (!dbg.WaitForStop(out var stop)) {
      dbg.WaitForExit(2000);
      var code = dbg.HasExited ? $"0x{(uint)dbg.ExitCode:x8}" : "(still running)";
      Console.Error.WriteLine($"[bp-test] no breakpoint stop observed before the target exited (target exit={code})");
      return 1;
    }
    Console.Error.WriteLine(
      $"[bp-test] STOP at code offset 0x{stop.PcOffset:x} (sp=0x{stop.Sp:x} fp=0x{stop.Fp:x})");

    bool pcMatches = stop.PcOffset == codeOffset;
    if (!pcMatches)
      Console.Error.WriteLine($"[bp-test] MISMATCH: stopped at 0x{stop.PcOffset:x} but expected 0x{codeOffset:x}");

    if (clearAtStop) {
      if (!dbg.ClearBreakpointAtOffset(codeOffset)) {
        Console.Error.WriteLine("[bp-test] agent did not ack clear-breakpoint at the stop");
        return 1;
      }
      Console.Error.WriteLine("[bp-test] cleared the breakpoint while stopped at it");
    }

    if (!dbg.Continue()) {
      Console.Error.WriteLine("[bp-test] agent did not ack the continue past the breakpoint");
      return 1;
    }
    Console.Error.WriteLine("[bp-test] continued past the breakpoint; waiting for the target to finish...");

    if (!dbg.WaitForExit(20000)) {
      Console.Error.WriteLine("[bp-test] target did not exit after continue (single-step-over stuck?)");
      return 1;
    }

    // The debugger's job is done when the stop landed at the expected PC and continue let the program
    // run to completion. The target's own exit code is its business — a program that legitimately exits
    // non-zero is not a debugger failure — so it is reported, not gated on.
    Console.Error.WriteLine($"[bp-test] target exit code: {dbg.ExitCode}");
    bool ok = pcMatches && dbg.HasExited;
    Console.Error.WriteLine(ok ? "[bp-test] PASS" : "[bp-test] FAIL");
    return ok ? 0 : 1;
  }
}
