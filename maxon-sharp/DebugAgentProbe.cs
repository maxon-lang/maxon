using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using MaxonSharp.Compiler.Ir.Runtime;

namespace MaxonSharp;

/// <summary>
/// The `maxon debug` attach probe / breakpoint driver — the minimal CONSUMER of the in-process debug
/// agent, and the seed of the P3c REPL. Two surfaces share one spine (create the control segment,
/// spawn the target STOPPED AT ENTRY with MAXON_DEBUG naming the segment, forward its stdio):
///
///   * <see cref="Run"/> (`--attach-probe`, P3a) proves the handshake — the agent maps the segment and
///     announces magic/version/alive — then releases the target. Parking at entry makes the handshake
///     observable regardless of how fast the target runs, which an external "watch, don't control"
///     poll cannot guarantee against a trivial program.
///   * <see cref="RunBpTest"/> (`--bp-test`, P3b) additionally sets a breakpoint at a raw code offset,
///     observes the stop event at that PC, continues, and confirms the program resumes and exits
///     correctly — exercising the command mailbox, the INT3 patch, the park, and the single-step-over.
///
/// It mirrors <see cref="DebugStreamMonitor"/>'s create-and-spawn shape and reuses the same
/// <see cref="SharedMapping"/> helper, so the agent's control segment is opened exactly the way the
/// DebugStream ring is. Symbolizing file:line ↔ address is the P3c REPL.
/// </summary>
internal static class DebugAgentProbe {

  /// Stem of the control-segment name; its pid+random suffix keeps concurrent probes from colliding.
  private const string ControlSegmentPrefix = "maxon_dbg_";

  /// How long the driver waits for the agent to reach a handshake / ack / stop before giving up.
  /// Generous: a debuggee only has to map a page and patch a byte, but a loaded CI box is slow.
  private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(20);

  /// A spawned target with its stdio-forwarding tasks, so both surfaces join them identically at exit.
  private sealed record AttachedTarget(Process Process, Task Stdout, Task Stderr);

  // ---- P3a: attach probe (`maxon debug --attach-probe`) ----

  public static int Run(string exePath) {
    if (!File.Exists(exePath)) {
      Console.Error.WriteLine($"maxon debug --attach-probe: executable not found: {exePath}");
      return 1;
    }

    long size = RuntimeEmitter.DbgControlSegmentSize;
    using var mapping = SharedMapping.Create(size, ControlSegmentPrefix);
    using var accessor = mapping.Map.CreateViewAccessor(0, size);

    var target = SpawnStoppedAtEntry(exePath, accessor, mapping);
    using var process = target.Process;

    int verdict = ProbeHandshake(accessor, process);

    process.WaitForExit();
    JoinIo(target);
    // The target's exit code is surfaced but does not decide the probe's verdict (a FAULTING target
    // still proved a valid handshake, and exits nonzero).
    Console.Error.WriteLine($"[dbg-probe] target exit code: {process.ExitCode}");
    return verdict;
  }

  /// <summary>
  /// Confirm the agent announced its handshake (the target is parked at entry, so magic/version/alive
  /// are present and stable), report it, then release the target with a continue. Version-agnostic: a
  /// schema the probe does not recognise is reported but does not fail the check — the v1 → v2 mailbox
  /// bump must not regress this substrate gate.
  /// </summary>
  private static int ProbeHandshake(MemoryMappedViewAccessor accessor, Process process) {
    if (!WaitForAgentAlive(accessor, process)) {
      Console.Error.WriteLine(
        "[dbg-probe] agent did NOT attach: handshake not observed. Is MAXON_DEBUG honored by this build?");
      return 1;
    }

    long version = accessor.ReadInt64(RuntimeEmitter.DbgOffVersion);
    Console.Error.WriteLine($"[dbg-probe] agent attached: magic=OK version={version} alive=yes");
    if (version != RuntimeEmitter.DbgControlVersion)
      Console.Error.WriteLine(
        $"[dbg-probe] note: agent schema v{version}, probe speaks v{RuntimeEmitter.DbgControlVersion}");

    long seq = 0;
    PostCommand(accessor, process, RuntimeEmitter.DbgCmdContinue, 0, ref seq); // leave the entry stop
    return 0;
  }

  // ---- P3b: the minimal breakpoint driver (`maxon debug --bp-test`) ----

  /// <summary>
  /// The minimal breakpoint driver that verifies the P3b agent end to end: spawn the target stopped
  /// at entry, set a breakpoint at a raw code offset (a function's <c>codeStart</c> from
  /// <c>--dump-info</c>), continue, observe the stop event at that PC, continue again, and confirm the
  /// program resumes and exits with the right code.
  /// </summary>
  public static int RunBpTest(string exePath, long codeOffset) {
    if (!File.Exists(exePath)) {
      Console.Error.WriteLine($"maxon debug --bp-test: executable not found: {exePath}");
      return 1;
    }

    long size = RuntimeEmitter.DbgControlSegmentSize;
    using var mapping = SharedMapping.Create(size, ControlSegmentPrefix);
    using var accessor = mapping.Map.CreateViewAccessor(0, size);

    var target = SpawnStoppedAtEntry(exePath, accessor, mapping);
    using var process = target.Process;

    int verdict = DriveBpTest(accessor, process, codeOffset);

    process.WaitForExit();
    JoinIo(target);
    return verdict;
  }

  /// <summary>
  /// Runs the set-bp / continue / observe-stop / continue handshake against a parked target. Returns 0
  /// only if the stop is observed at the expected PC AND the program then exits 0. Any timeout or
  /// mismatch is a red result, reported plainly.
  /// </summary>
  private static int DriveBpTest(MemoryMappedViewAccessor accessor, Process process, long codeOffset) {
    if (!WaitForAgentAlive(accessor, process)) {
      Console.Error.WriteLine("[bp-test] agent never announced alive; is MAXON_DEBUG honored by this build?");
      return 1;
    }
    Console.Error.WriteLine($"[bp-test] agent alive; setting breakpoint at code offset 0x{codeOffset:x}");

    long seq = 0;
    if (!PostCommand(accessor, process, RuntimeEmitter.DbgCmdSetBp, codeOffset, ref seq)) {
      Console.Error.WriteLine("[bp-test] agent did not ack set-breakpoint");
      return 1;
    }
    if (!PostCommand(accessor, process, RuntimeEmitter.DbgCmdContinue, 0, ref seq)) {
      Console.Error.WriteLine("[bp-test] agent did not ack the first continue (leave entry stop)");
      return 1;
    }
    Console.Error.WriteLine("[bp-test] breakpoint set; continued from entry, running to the breakpoint...");

    if (!WaitForStop(accessor, process, out long stopPc, out long stopSp, out long stopFp)) {
      process.WaitForExit(2000);
      var code = process.HasExited ? $"0x{(uint)process.ExitCode:x8}" : "(still running)";
      Console.Error.WriteLine($"[bp-test] no breakpoint stop observed before the target exited "
        + $"(target exit={code})");
      return 1;
    }
    Console.Error.WriteLine(
      $"[bp-test] STOP at code offset 0x{stopPc:x} (sp=0x{stopSp:x} fp=0x{stopFp:x})");

    bool pcMatches = stopPc == codeOffset;
    if (!pcMatches)
      Console.Error.WriteLine(
        $"[bp-test] MISMATCH: stopped at 0x{stopPc:x} but expected 0x{codeOffset:x}");

    if (!PostCommand(accessor, process, RuntimeEmitter.DbgCmdContinue, 0, ref seq)) {
      Console.Error.WriteLine("[bp-test] agent did not ack the continue past the breakpoint");
      return 1;
    }
    Console.Error.WriteLine("[bp-test] continued past the breakpoint; waiting for the target to finish...");

    if (!process.WaitForExit((int)AttachTimeout.TotalMilliseconds)) {
      Console.Error.WriteLine("[bp-test] target did not exit after continue (single-step-over stuck?)");
      return 1;
    }

    Console.Error.WriteLine($"[bp-test] target exit code: {process.ExitCode}");
    bool ok = pcMatches && process.ExitCode == 0;
    Console.Error.WriteLine(ok ? "[bp-test] PASS" : "[bp-test] FAIL");
    return ok ? 0 : 1;
  }

  // ---- shared spawn / mailbox plumbing ----

  /// <summary>
  /// Seed StopAtEntry, spawn the target with MAXON_DEBUG naming the segment, and forward its stdio.
  /// Parking at entry is what makes the handshake observable and lets the driver set breakpoints
  /// before user code runs; the caller releases the target with a continue command.
  /// </summary>
  private static AttachedTarget SpawnStoppedAtEntry(string exePath, MemoryMappedViewAccessor accessor,
      SharedMapping mapping) {
    // A fresh segment is zeroed; StopAtEntry is the one field seeded before spawn.
    accessor.Write(RuntimeEmitter.DbgOffStopAtEntry, 1L);

    var psi = new ProcessStartInfo {
      FileName = Path.GetFullPath(exePath),
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
    };
    // The agent reads this SAME var name from its `__dbg_env_name` symdata (one definition, so the
    // producer and consumer cannot drift and leave the agent silently dark).
    psi.EnvironmentVariables[RuntimeEmitter.DbgActivationEnvVar] = mapping.SegmentName;

    var process = new Process { StartInfo = psi };
    process.Start();

    var stdoutTask = Task.Run(() => {
      var sr = process.StandardOutput;
      while (sr.ReadLine() is { } line) Console.WriteLine(line);
    });
    var stderrTask = Task.Run(() => {
      var sr = process.StandardError;
      while (sr.ReadLine() is { } line) Console.Error.WriteLine(line);
    });

    return new AttachedTarget(process, stdoutTask, stderrTask);
  }

  private static void JoinIo(AttachedTarget target) {
#pragma warning disable VSTHRD002 // synchronous entry point, no SyncContext to deadlock against
    target.Stdout.Wait();
    target.Stderr.Wait();
#pragma warning restore VSTHRD002
  }

  /// Spin until the agent announces its handshake — magic present AND the alive flag set — or the
  /// target exits / we time out. Reliable because the target is parked at entry until released.
  private static bool WaitForAgentAlive(MemoryMappedViewAccessor accessor, Process process) {
    var deadline = DateTime.UtcNow + AttachTimeout;
    while (DateTime.UtcNow < deadline) {
      if ((accessor.ReadInt64(RuntimeEmitter.DbgOffFlags) & RuntimeEmitter.DbgFlagAgentAlive) != 0
          && accessor.ReadInt64(RuntimeEmitter.DbgOffMagic) == RuntimeEmitter.DbgControlMagic)
        return true;
      if (process.HasExited) return false;
      Thread.Yield();
    }
    return false;
  }

  /// <summary>
  /// Post one command to the agent's mailbox and wait for its ack. Writes Cmd + CmdArg, then (after a
  /// full barrier, matching the agent's release/acquire on CmdSeq) bumps CmdSeq to a fresh sequence,
  /// and waits for AckSeq to reach it. Returns false on timeout or target exit.
  /// </summary>
  private static bool PostCommand(MemoryMappedViewAccessor accessor, Process process, long cmd, long arg,
      ref long seq) {
    accessor.Write(RuntimeEmitter.DbgOffCmd, cmd);
    accessor.Write(RuntimeEmitter.DbgOffCmdArg, arg);
    Thread.MemoryBarrier();
    seq += 1;
    accessor.Write(RuntimeEmitter.DbgOffCmdSeq, seq);

    var deadline = DateTime.UtcNow + AttachTimeout;
    while (DateTime.UtcNow < deadline) {
      Thread.MemoryBarrier();
      if (accessor.ReadInt64(RuntimeEmitter.DbgOffAckSeq) >= seq) return true;
      // Continue is acked from the park just before the target resumes; a target that exits cleanly
      // after the FINAL continue may race the ack, so treat exit-after-continue as done.
      if (process.HasExited) return cmd == RuntimeEmitter.DbgCmdContinue;
      Thread.Yield();
    }
    return false;
  }

  /// Wait for the agent to publish a stop event (StopSeq advances), returning its PC/SP/FP.
  private static bool WaitForStop(MemoryMappedViewAccessor accessor, Process process,
      out long stopPc, out long stopSp, out long stopFp) {
    stopPc = stopSp = stopFp = 0;
    var deadline = DateTime.UtcNow + AttachTimeout;
    while (DateTime.UtcNow < deadline) {
      Thread.MemoryBarrier();
      if (accessor.ReadInt64(RuntimeEmitter.DbgOffStopSeq) > 0) {
        Thread.MemoryBarrier();
        stopPc = accessor.ReadInt64(RuntimeEmitter.DbgOffStopPc);
        stopSp = accessor.ReadInt64(RuntimeEmitter.DbgOffStopSp);
        stopFp = accessor.ReadInt64(RuntimeEmitter.DbgOffStopFp);
        return true;
      }
      if (process.HasExited) return false;
      Thread.Yield();
    }
    return false;
  }
}
