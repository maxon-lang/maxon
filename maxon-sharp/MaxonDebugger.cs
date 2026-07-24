using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using MaxonSharp.Compiler.Ir.Runtime;
using MaxonSharp.Debug;

namespace MaxonSharp;

/// <summary>
/// The `maxon debug` driver ENGINE (P3c) — the single brain every surface (the REPL, the P3a/P3b
/// harnesses, and later the MCP / DAP / TUI) drives. It owns the mechanism side of a debug session:
///
///   * spawn the target STOPPED AT ENTRY with <c>MAXON_DEBUG</c> naming a control segment it creates
///     and maps (reusing <see cref="SharedMapping"/>, exactly as <see cref="DebugStreamMonitor"/> maps
///     the ring), forwarding the target's stdio;
///   * load + VALIDATE the `.mxdbg` sidecar's build-id against the binary's own `.text`
///     (<see cref="BinaryBuildId"/>) — a sidecar that does not describe THIS binary is refused, never
///     used to print wrong line numbers;
///   * post mailbox commands (set/clear breakpoint, continue, backtrace) and read stop events, all
///     through the ONE set of <see cref="RuntimeEmitter"/> offset/opcode constants the agent wrote by;
///   * translate meaning against the sidecar — <see cref="ResolveLine"/> (file:line → code offset),
///     <see cref="Symbolize"/> (code offset → file:line:col + function), and <see cref="Backtrace"/>
///     (the stopped GT's frame offsets, each symbolized).
///
/// It is the evolution of the P3a/P3b <see cref="DebugAgentProbe"/> seed: the spawn + mailbox spine
/// that lived there now lives here once, and the two harness commands call into it, so there is a
/// single home for how a target is attached and driven.
/// </summary>
internal sealed class MaxonDebugger : IDisposable {

  /// Stem of the control-segment name; its pid+random suffix keeps concurrent sessions from colliding.
  private const string ControlSegmentPrefix = "maxon_dbg_";

  /// How long the driver waits for the agent to reach a handshake / ack / stop before giving up.
  /// Generous: a debuggee only has to map a page and patch a byte, but a loaded CI box is slow.
  private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(20);

  private readonly SharedMapping _mapping;
  private readonly MemoryMappedViewAccessor _accessor;
  private readonly Process _process;
  private readonly Task _stdout;
  private readonly Task _stderr;

  /// The validated sidecar, or null for a session that only needs the substrate (the P3a attach probe
  /// debugs a binary that may have no sidecar). Symbolization requires it.
  public MxdbgReader? Sidecar { get; }

  private long _cmdSeq;
  private long _stopWatermark;

  /// Where a stopped thread is, as the agent reported it. <see cref="PcOffset"/> is a `.text` code
  /// offset (ASLR-independent), the base the sidecar resolves.
  public readonly record struct StopInfo(long Reason, long PcOffset, long Sp, long Fp);

  /// A code offset resolved through the sidecar. <see cref="HasLine"/> is false for a location with no
  /// source row (`<no line>`), <see cref="Function"/> empty for one outside every known function —
  /// each stated honestly rather than guessed.
  public readonly record struct SymLocation(
    long CodeOffset, bool HasLine, string File, uint Line, uint Col, string Function) {
    public bool HasFunction => Function.Length > 0;
  }

  /// One backtrace frame: its code offset, whether it is a return address (frames above frame 0, which
  /// need the −1 call-site bias to symbolize to the CALL rather than the instruction after it), and its
  /// resolved location.
  public readonly record struct Frame(int Index, long CodeOffset, bool IsReturnAddress, SymLocation Location);

  private MaxonDebugger(SharedMapping mapping, MemoryMappedViewAccessor accessor, Process process,
      Task stdout, Task stderr, MxdbgReader? sidecar) {
    _mapping = mapping;
    _accessor = accessor;
    _process = process;
    _stdout = stdout;
    _stderr = stderr;
    Sidecar = sidecar;
  }

  /// <summary>
  /// Attach to <paramref name="exePath"/>: validate the sidecar's build-id (when one is supplied),
  /// create and map the control segment, and spawn the target parked at entry with its stdio forwarded.
  /// The target's stdout goes to <paramref name="targetStdout"/> (default the driver's stdout); batch
  /// mode passes the driver's stderr so its own JSON stream on stdout stays a clean, parseable channel.
  /// Throws <see cref="DebuggerException"/> with a clear reason on a build-id mismatch or a spawn
  /// failure — the caller reports it and exits nonzero, the way the schema-mismatch refusal does.
  /// </summary>
  public static MaxonDebugger Attach(string exePath, IReadOnlyList<string> targetArgs, MxdbgReader? sidecar,
      TextWriter? targetStdout = null) {
    var stdoutSink = targetStdout ?? Console.Out;

    if (!File.Exists(exePath))
      throw new DebuggerException($"executable not found: {exePath}");

    if (sidecar != null)
      ValidateBuildId(exePath, sidecar);

    long size = RuntimeEmitter.DbgControlSegmentSize;
    var mapping = SharedMapping.Create(size, ControlSegmentPrefix);
    MemoryMappedViewAccessor? accessor = null;
    try {
      accessor = mapping.Map.CreateViewAccessor(0, size);
      // A fresh segment is zeroed; StopAtEntry is the one field seeded before spawn, so the driver can
      // set breakpoints before user code runs, gdb-style.
      accessor.Write(RuntimeEmitter.DbgOffStopAtEntry, 1L);

      var psi = new ProcessStartInfo {
        FileName = Path.GetFullPath(exePath),
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      };
      foreach (var a in targetArgs) psi.ArgumentList.Add(a);
      // The agent reads this SAME var name from its `__dbg_env_name` symdata (one definition, so the
      // producer and consumer cannot drift and leave the agent silently dark).
      psi.EnvironmentVariables[RuntimeEmitter.DbgActivationEnvVar] = mapping.SegmentName;

      var process = new Process { StartInfo = psi };
      try {
        process.Start();
      } catch (Exception ex) {
        // A non-native binary (e.g. a cross-compiled target run on this host) or a missing loader
        // fails here; report it as a clean refusal, not an uncaught Win32Exception.
        process.Dispose();
        throw new DebuggerException($"cannot launch '{exePath}': {ex.Message}");
      }

      var stdout = Task.Run(() => {
        var sr = process.StandardOutput;
        while (sr.ReadLine() is { } line) stdoutSink.WriteLine(line);
      });
      var stderr = Task.Run(() => {
        var sr = process.StandardError;
        while (sr.ReadLine() is { } line) Console.Error.WriteLine(line);
      });

      return new MaxonDebugger(mapping, accessor, process, stdout, stderr, sidecar);
    } catch {
      accessor?.Dispose();
      mapping.Dispose();
      throw;
    }
  }

  /// Refuse a sidecar whose build-id does not match the binary's `.text` — the "an instrument that
  /// lies is worse than none" rule the DebugStream handshake established. An unparseable binary is
  /// refused too: a sidecar that cannot be checked is exactly the stale-instrument case.
  private static void ValidateBuildId(string exePath, MxdbgReader sidecar) {
    if (!BinaryBuildId.TryCompute(exePath, out var actual, out var error))
      throw new DebuggerException($"cannot validate debug info against '{exePath}': {error}");

    if (actual != sidecar.BuildId)
      throw new DebuggerException(
        $"debug info does not match '{exePath}': sidecar build-id 0x{sidecar.BuildId:x16}, "
        + $"binary 0x{actual:x16}. Rebuild to regenerate the sidecar.");
  }

  // ---- Handshake ----

  /// Spin until the agent announces its handshake (magic present AND the alive flag set) or the target
  /// exits / we time out. Reliable because the target is parked at entry until released.
  public bool WaitForAgentAlive() {
    var deadline = DateTime.UtcNow + AttachTimeout;
    while (DateTime.UtcNow < deadline) {
      if ((_accessor.ReadInt64(RuntimeEmitter.DbgOffFlags) & RuntimeEmitter.DbgFlagAgentAlive) != 0
          && _accessor.ReadInt64(RuntimeEmitter.DbgOffMagic) == RuntimeEmitter.DbgControlMagic)
        return true;
      if (_process.HasExited) return false;
      Thread.Yield();
    }
    return false;
  }

  /// The control-segment schema version the agent announced.
  public long AgentVersion => _accessor.ReadInt64(RuntimeEmitter.DbgOffVersion);

  // ---- Commands ----

  public bool SetBreakpointAtOffset(long codeOffset) =>
    PostCommand(RuntimeEmitter.DbgCmdSetBp, codeOffset);

  public bool ClearBreakpointAtOffset(long codeOffset) =>
    PostCommand(RuntimeEmitter.DbgCmdClearBp, codeOffset);

  public bool Continue() => PostCommand(RuntimeEmitter.DbgCmdContinue, 0);

  /// The outcome of resolving+arming a `file:line` breakpoint, so the resolve→arm DECISION lives once
  /// here and each surface only renders it (a divergent copy of "no code vs set" would be a wrong
  /// answer, not a compile error).
  public enum BreakKind { NoCode, Set, Unacknowledged }

  public readonly record struct BreakResult(BreakKind Kind, uint Offset, SymLocation Location);

  /// <summary>
  /// Resolve a `file:line` to its `.text` offset via the sidecar and arm a breakpoint there.
  /// <see cref="BreakKind.NoCode"/> when the line carries no statement ("no code at that line"),
  /// <see cref="BreakKind.Unacknowledged"/> when the agent did not ack. Requires a sidecar.
  /// </summary>
  public BreakResult SetBreakpoint(string file, uint line) {
    if (Sidecar is not { } s) throw new DebuggerException("no debug info loaded; cannot resolve file:line");
    if (s.LineToOffset(file, line) is not { } off) return new BreakResult(BreakKind.NoCode, 0, default);

    bool acked = SetBreakpointAtOffset(off);
    return new BreakResult(acked ? BreakKind.Set : BreakKind.Unacknowledged, off, Symbolize(off));
  }

  /// <summary>
  /// Post one command to the agent's mailbox and wait for its ack. Writes Cmd + CmdArg, then rings the
  /// doorbell and awaits the ack. Returns false on timeout or target exit — except a Continue the target
  /// races to exit after, which is treated as done (the program legitimately ran to completion).
  /// </summary>
  private bool PostCommand(long cmd, long arg) {
    _accessor.Write(RuntimeEmitter.DbgOffCmd, cmd);
    _accessor.Write(RuntimeEmitter.DbgOffCmdArg, arg);
    return RingDoorbellAndAwaitAck(cmd);
  }

  /// <summary>
  /// Post a read-memory command: Cmd = ReadMem, CmdArg = addr, ReadLen = len (the ONE command that
  /// takes a second arg), then ring the doorbell and await the ack. Split from <see cref="PostCommand"/>
  /// only because it writes that extra field; the doorbell + ack discipline is shared so the two cannot
  /// drift on how a command is published or awaited.
  /// </summary>
  private bool PostReadChunk(ulong addr, int len) {
    _accessor.Write(RuntimeEmitter.DbgOffCmd, RuntimeEmitter.DbgCmdReadMem);
    _accessor.Write(RuntimeEmitter.DbgOffCmdArg, (long)addr);
    _accessor.Write(RuntimeEmitter.DbgOffReadLen, len);
    return RingDoorbellAndAwaitAck(RuntimeEmitter.DbgCmdReadMem);
  }

  /// <summary>
  /// Ring the command doorbell for a command whose fields are already written, and wait for its ack.
  /// After a full barrier (matching the agent's release/acquire on CmdSeq) it bumps CmdSeq to a fresh
  /// sequence and waits for AckSeq to reach it. Returns false on timeout or target exit — except a
  /// Continue the target races to exit after, treated as done. Shared by every mailbox poster so the
  /// publish-and-await protocol lives in exactly one place.
  /// </summary>
  private bool RingDoorbellAndAwaitAck(long cmd) {
    Thread.MemoryBarrier();
    _cmdSeq += 1;
    _accessor.Write(RuntimeEmitter.DbgOffCmdSeq, _cmdSeq);

    var deadline = DateTime.UtcNow + AttachTimeout;
    while (DateTime.UtcNow < deadline) {
      Thread.MemoryBarrier();
      if (_accessor.ReadInt64(RuntimeEmitter.DbgOffAckSeq) >= _cmdSeq) return true;
      if (_process.HasExited) return cmd == RuntimeEmitter.DbgCmdContinue;
      Thread.Yield();
    }
    return false;
  }

  // ---- Memory reads (value inspection) ----

  /// Why a memory read produced no bytes, kept DISTINCT (as <see cref="BacktraceStatus"/> is) so a
  /// surface never reports a command that failed to ack as "rebuild to enable" — the two need different
  /// user actions.
  public enum ReadMemoryStatus {
    /// The agent filled the result buffer; <c>Data</c> holds the requested bytes.
    Ok,
    /// The agent predates the read-memory command (control version &lt; DbgReadMemMinVersion); a v3 agent
    /// would ack-and-ignore, so its buffer bytes must NOT be read as the debuggee's memory.
    UnsupportedByAgent,
    /// The command was posted but never acked (timeout / target exited mid-request).
    NotAcknowledged,
  }

  public readonly record struct ReadMemoryResult(ReadMemoryStatus Status, byte[] Data);

  /// True when this binary's agent understands value inspection (control version ≥ DbgReadMemMinVersion).
  /// A surface checks this once before rendering values, so a pre-v4 binary gets one clean "unsupported"
  /// message rather than a per-read failure.
  public bool ValueInspectionSupported => AgentVersion >= RuntimeEmitter.DbgReadMemMinVersion;

  /// <summary>
  /// Read <paramref name="len"/> bytes of the debuggee's memory at <paramref name="addr"/>, chunked into
  /// ≤ <see cref="RuntimeEmitter.DbgReadBufCap"/> reads through the parked agent. Gated on the agent
  /// version: a v3 agent would ack the unknown command without filling the buffer, so its bytes are
  /// refused (<see cref="ReadMemoryStatus.UnsupportedByAgent"/>) rather than read as real memory — the
  /// P3c UnsupportedByAgent lesson. The renderer only asks for addresses derived from valid locations
  /// (a stack slot, a followed non-null pointer), so a raw copy is safe for the MVP.
  /// </summary>
  public ReadMemoryResult ReadMemory(ulong addr, int len) {
    if (len < 0) throw new ArgumentOutOfRangeException(nameof(len));
    if (!ValueInspectionSupported) return new ReadMemoryResult(ReadMemoryStatus.UnsupportedByAgent, []);

    var buf = new byte[len];
    int done = 0;
    while (done < len) {
      int chunk = Math.Min(len - done, RuntimeEmitter.DbgReadBufCap);
      if (!PostReadChunk(addr + (ulong)done, chunk))
        return new ReadMemoryResult(ReadMemoryStatus.NotAcknowledged, []);

      // The park loop's ack store-release published the buffer; acquire it before copying out.
      Thread.MemoryBarrier();
      for (int i = 0; i < chunk; i++)
        buf[done + i] = _accessor.ReadByte(RuntimeEmitter.DbgOffReadBuf + i);
      done += chunk;
    }
    return new ReadMemoryResult(ReadMemoryStatus.Ok, buf);
  }

  // ---- Stop events ----

  /// <summary>
  /// Wait for the agent to publish a NEW stop event (StopSeq advances past the watermark), returning
  /// its reason/PC/SP/FP. Returns false when the target exits or ran to completion first. Polling
  /// against a watermark lets one session span multiple stops without the next wait returning on a
  /// stale seq.
  /// </summary>
  public bool WaitForStop(out StopInfo stop) {
    stop = default;
    var deadline = DateTime.UtcNow + AttachTimeout;
    while (DateTime.UtcNow < deadline) {
      Thread.MemoryBarrier();
      long seq = _accessor.ReadInt64(RuntimeEmitter.DbgOffStopSeq);
      if (seq > _stopWatermark) {
        _stopWatermark = seq;
        Thread.MemoryBarrier();
        stop = new StopInfo(
          _accessor.ReadInt64(RuntimeEmitter.DbgOffStopReason),
          _accessor.ReadInt64(RuntimeEmitter.DbgOffStopPc),
          _accessor.ReadInt64(RuntimeEmitter.DbgOffStopSp),
          _accessor.ReadInt64(RuntimeEmitter.DbgOffStopFp));
        return true;
      }
      if (_process.HasExited) return false;
      Thread.Yield();
    }
    return false;
  }

  // ---- Symbolization ----

  /// <summary>
  /// Resolve a `.text` code offset to file:line:col + function via the sidecar. With
  /// <paramref name="returnAddressBias"/>, the offset is symbolized as a return address (offset − 1),
  /// so a call that is a function's final instruction resolves to the CALLING line rather than the next
  /// one — the same bias the panic backtrace applies. Requires a sidecar.
  /// </summary>
  public SymLocation Symbolize(long codeOffset, bool returnAddressBias = false) {
    if (Sidecar is not { } s) throw new DebuggerException("no debug info loaded; cannot symbolize");

    uint lookup = (uint)codeOffset;
    if (returnAddressBias && lookup > 0) lookup -= 1;

    var fn = s.FunctionAt(lookup);
    var line = s.PcToLine(lookup);
    return line is { } l
      ? new SymLocation(codeOffset, true, l.File, l.Line, l.Col, fn?.Name ?? "")
      : new SymLocation(codeOffset, false, "", 0, 0, fn?.Name ?? "");
  }

  // ---- Backtrace ----

  /// Why a backtrace request produced no frames, kept DISTINCT so a surface never reports a command
  /// that failed to ack as "unsupported, rebuild to enable" (the two need different user actions).
  public enum BacktraceStatus {
    /// The agent filled the frame array (which may still be empty — a stop at entry has no stack).
    Ok,
    /// The agent predates the backtrace command (control version &lt; DbgBacktraceMinVersion); a v2
    /// agent would ack-and-ignore, so its empty array must NOT be read as a real trace.
    UnsupportedByAgent,
    /// The command was posted but never acked (timeout / target exited mid-request).
    NotAcknowledged,
  }

  public readonly record struct BacktraceResult(BacktraceStatus Status, IReadOnlyList<Frame> Frames);

  /// <summary>
  /// Request the stopped GT's backtrace: post the backtrace command, read the frame array the agent
  /// filled, and symbolize each frame. Frame 0 is the exact stop PC; frames above it are return
  /// addresses symbolized with the call-site bias. The <see cref="BacktraceStatus"/> distinguishes a
  /// genuinely unsupported agent from a command that failed to ack, so the surfaces report each with
  /// the right advice.
  /// </summary>
  public BacktraceResult Backtrace() {
    if (AgentVersion < RuntimeEmitter.DbgBacktraceMinVersion)
      return new BacktraceResult(BacktraceStatus.UnsupportedByAgent, []);
    if (!PostCommand(RuntimeEmitter.DbgCmdBacktrace, 0))
      return new BacktraceResult(BacktraceStatus.NotAcknowledged, []);

    // The park loop's ack store-release published the array; acquire it before reading.
    Thread.MemoryBarrier();
    long count = _accessor.ReadInt64(RuntimeEmitter.DbgOffBtCount);
    if (count < 0) count = 0;
    if (count > RuntimeEmitter.DbgMaxBacktraceFrames) count = RuntimeEmitter.DbgMaxBacktraceFrames;

    var frames = new List<Frame>((int)count);
    for (int i = 0; i < count; i++) {
      long off = _accessor.ReadInt64(RuntimeEmitter.DbgOffBtFrames + (long)i * 8);
      bool isReturnAddress = i > 0;
      frames.Add(new Frame(i, off, isReturnAddress, Symbolize(off, returnAddressBias: isReturnAddress)));
    }
    return new BacktraceResult(BacktraceStatus.Ok, frames);
  }

  // ---- Process lifetime ----

  public bool HasExited => _process.HasExited;
  public int ExitCode => _process.ExitCode;

  public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);

  public void WaitForExit() => _process.WaitForExit();

  /// End the session by killing a still-running target — `quit` from the REPL, where the debuggee is
  /// parked in the agent's stop-the-world loop and would otherwise never resume. Tolerates the target
  /// exiting between the check and the kill (a normal race), so it is safe to call from Dispose too.
  public void Terminate() {
    try {
      if (!_process.HasExited) _process.Kill();
    } catch (InvalidOperationException) {
      // The process exited between the HasExited check and Kill — already gone, nothing to do.
    }
  }

  /// Join the stdio-forwarding tasks so all of the target's output has been written before the driver
  /// prints its own closing lines.
  public void JoinIo() {
#pragma warning disable VSTHRD002 // synchronous entry point, no SyncContext to deadlock against
    _stdout.Wait();
    _stderr.Wait();
#pragma warning restore VSTHRD002
  }

  public void Dispose() {
    // Never leave a parked target orphaned — an exceptional exit from the session loop would otherwise
    // strand a debuggee spinning in the agent's stop-the-world loop. The normal paths have already
    // driven it to exit, so this only bites on failure.
    Terminate();
    _accessor.Dispose();
    _mapping.Dispose();
    _process.Dispose();
  }
}

/// A driver-side refusal (bad sidecar, failed attach) — reported as a tool failure with a nonzero
/// exit, the way DebugStreamMonitor refuses a schema mismatch, NOT as a compiler error code.
internal sealed class DebuggerException(string message) : Exception(message);
