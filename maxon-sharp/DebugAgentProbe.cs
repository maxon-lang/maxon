using MaxonSharp.Compiler.Ir.Runtime;

namespace MaxonSharp;

/// <summary>
/// The P3a debug-agent attach probe — the minimal CONSUMER that proves the in-process agent's
/// handshake, and the seed of the P3 `maxon debug` driver. It does exactly what a driver's attach
/// step does and nothing more: create the control segment, spawn the target with MAXON_DEBUG naming
/// it, and read back the handshake the agent announces (`__dbg_init`). Breakpoints, the command
/// mailbox, and the REPL are P3b+.
///
/// It mirrors <see cref="DebugStreamMonitor"/>'s create-and-spawn shape and reuses the same
/// <see cref="SharedMapping"/> helper, so the agent's control segment is opened exactly the way the
/// DebugStream ring is. The target's stdout/stderr are forwarded, so a panicking target still prints
/// its symbolized backtrace through the probe (the "fault path intact with the trap handler armed"
/// gate).
/// </summary>
internal static class DebugAgentProbe {

  /// Stem of the control-segment name; its pid+random suffix keeps concurrent probes from colliding.
  private const string ControlSegmentPrefix = "maxon_dbg_";

  public static int Run(string exePath) {
    if (!File.Exists(exePath)) {
      Console.Error.WriteLine($"maxon debug --attach-probe: executable not found: {exePath}");
      return 1;
    }

    long size = RuntimeEmitter.DbgControlSegmentSize;
    using var mapping = SharedMapping.Create(size, ControlSegmentPrefix);
    using var accessor = mapping.Map.CreateViewAccessor(0, size);

    // A fresh segment is zeroed; the agent writes magic, version, then the alive flag. Nothing to
    // seed from this side for P3a.

    var psi = new System.Diagnostics.ProcessStartInfo {
      FileName = Path.GetFullPath(exePath),
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
    };
    // The agent reads this SAME var name from its `__dbg_env_name` symdata (one definition, so the
    // producer and consumer cannot drift and leave the agent silently dark).
    psi.EnvironmentVariables[RuntimeEmitter.DbgActivationEnvVar] = mapping.SegmentName;

    using var process = new System.Diagnostics.Process { StartInfo = psi };
    process.Start();

    var stdoutTask = Task.Run(() => {
      var sr = process.StandardOutput;
      while (sr.ReadLine() is { } line) Console.WriteLine(line);
    });
    var stderrTask = Task.Run(() => {
      var sr = process.StandardError;
      while (sr.ReadLine() is { } line) Console.Error.WriteLine(line);
    });

    // Watch the liveness flag while the target runs. The magic and version are written once and
    // never cleared, so they survive the target's exit; only the alive flag is cleared at shutdown.
    bool sawAlive = false;
    while (!process.HasExited) {
      long flags = accessor.ReadInt64(RuntimeEmitter.DbgOffFlags);
      if ((flags & RuntimeEmitter.DbgFlagAgentAlive) != 0) sawAlive = true;
      Thread.Yield();
    }
    process.WaitForExit();
#pragma warning disable VSTHRD002 // synchronous entry point, no SyncContext to deadlock against
    stdoutTask.Wait();
    stderrTask.Wait();
#pragma warning restore VSTHRD002

    long finalMagic = accessor.ReadInt64(RuntimeEmitter.DbgOffMagic);
    long finalVersion = accessor.ReadInt64(RuntimeEmitter.DbgOffVersion);
    long finalFlags = accessor.ReadInt64(RuntimeEmitter.DbgOffFlags);

    bool magicOk = finalMagic == RuntimeEmitter.DbgControlMagic;
    bool versionOk = finalVersion == RuntimeEmitter.DbgControlVersion;
    bool detached = (finalFlags & RuntimeEmitter.DbgFlagAgentAlive) == 0;

    if (!magicOk) {
      Console.Error.WriteLine(
        $"[dbg-probe] agent did NOT attach: magic=0x{finalMagic:x16} "
        + $"(expected 0x{RuntimeEmitter.DbgControlMagic:x16}). Is MAXON_DEBUG honored by this build?");
      return 1;
    }

    Console.Error.WriteLine(
      $"[dbg-probe] agent alive: magic=OK version={finalVersion} "
      + $"(alive observed during run: {(sawAlive ? "yes" : "no")})");

    if (!versionOk)
      Console.Error.WriteLine(
        $"[dbg-probe] WARNING: agent schema v{finalVersion}, probe speaks v{RuntimeEmitter.DbgControlVersion}");

    Console.Error.WriteLine(detached
      ? "[dbg-probe] agent detached cleanly (alive flag cleared at shutdown)"
      : "[dbg-probe] WARNING: alive flag still set after exit (unclean detach)");

    // The probe's own verdict is independent of the target's exit code, which we surface too.
    Console.Error.WriteLine($"[dbg-probe] target exit code: {process.ExitCode}");
    return magicOk && versionOk && detached ? 0 : 1;
  }
}
