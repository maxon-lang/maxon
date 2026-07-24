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

  /// The offsets where the USER (not a transient step temp bp) has armed a breakpoint. The agent stores
  /// one 0xCC/BRK per offset, so a step's temp bp set where a user bp already sits would SHARE that byte
  /// and the temp's cleanup would silently delete the user's bp. The step temp-bp runners consult this to
  /// leave a coinciding user bp untouched. Populated by the user-facing arm/clear below; the temp runners
  /// arm/clear through PostCommand directly, so a temp never enters this set.
  private readonly HashSet<long> _userBreakpointOffsets = [];

  public bool SetBreakpointAtOffset(long codeOffset) {
    _userBreakpointOffsets.Add(codeOffset);
    return PostCommand(RuntimeEmitter.DbgCmdSetBp, codeOffset);
  }

  public bool ClearBreakpointAtOffset(long codeOffset) {
    _userBreakpointOffsets.Remove(codeOffset);
    return PostCommand(RuntimeEmitter.DbgCmdClearBp, codeOffset);
  }

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

  // ---- Source-line stepping (P4b) ----

  /// True when this binary's agent understands source-line stepping (control version ≥ DbgStepMinVersion).
  /// The four step surfaces check this once, so a pre-v5 binary gets one clean "rebuild to enable" message
  /// rather than a hang on a step the agent would ack-and-ignore — the P3c/P4a UnsupportedByAgent lesson.
  public bool SteppingSupported => AgentVersion >= RuntimeEmitter.DbgStepMinVersion;

  /// The absolute `.text` load address the agent published at init (<see cref="RuntimeEmitter.DbgOffTextBase"/>).
  /// Only meaningful once the agent is alive (v5+).
  private ulong TextBase => (ulong)_accessor.ReadInt64(RuntimeEmitter.DbgOffTextBase);

  /// Convert an absolute `.text` address (e.g. a return address read off the debuggee's stack) into the
  /// code offset the sidecar and every DbgOff* PC field use — ASLR-independent, since it subtracts the
  /// agent's own reported load base.
  public long OffsetOf(ulong absCodeAddr) => (long)(absCodeAddr - TextBase);

  /// Why a step produced no stop, or Stopped with the new location. The kinds stay DISTINCT (as
  /// <see cref="BacktraceStatus"/> and <see cref="ReadMemoryStatus"/> do) so each surface words its own
  /// advice: an unsupported agent (rebuild), an unacked command (target may have exited), the target
  /// running to completion, a runaway that hit the instruction cap, a <c>finish</c> with no caller frame,
  /// and an <c>until</c> whose line has no code each need different messages, not one "failed".
  public enum StepOutcomeKind {
    Stopped, Exited, UnsupportedByAgent, NotAcknowledged, LimitReached, NoCallerFrame, NoCode,
  }

  public readonly record struct StepOutcome(StepOutcomeKind Kind, StopInfo Stop);

  /// A generous cap on machine instructions single-stepped for ONE source step, so a statement compiling
  /// to a pathological amount of code — or a step-INTO that descends into a runtime function with no line
  /// info and single-steps all of it — stops honestly rather than spinning the driver forever.
  private const int MaxStepInstructions = 500_000;

  /// A generous cap on Continue passes <see cref="RunUntilReturn"/> makes before giving up, so a callee
  /// (or finish target) whose frames never unwind past the guard cannot loop the driver forever.
  private const int MaxReturnContinues = 100_000;

  /// <summary>
  /// Single-step ONE debuggee instruction and return the new stop. Posts <see cref="RuntimeEmitter.DbgCmdStep"/>
  /// (the agent acks it, exits the park loop, arms a hardware single-step, and publishes a step stop when
  /// it completes), then waits for that stop. Version-gated; a pre-v5 agent would ack-and-ignore, so it is
  /// refused rather than hung on. <see cref="StepOutcomeKind.Exited"/> when the stepped instruction ran the
  /// program to completion.
  /// </summary>
  public StepOutcome Step() {
    if (!SteppingSupported) return new StepOutcome(StepOutcomeKind.UnsupportedByAgent, default);
    if (!PostCommand(RuntimeEmitter.DbgCmdStep, 0))
      return new StepOutcome(_process.HasExited ? StepOutcomeKind.Exited : StepOutcomeKind.NotAcknowledged, default);
    return WaitForStop(out var stop)
      ? new StepOutcome(StepOutcomeKind.Stopped, stop)
      : new StepOutcome(StepOutcomeKind.Exited, default);
  }

  /// <summary>
  /// Step INTO the next source statement: single-step (descending into any call) until the PC lands on a
  /// statement-boundary line row whose line differs from the start line, or the start function returns to
  /// a shallower frame. <paramref name="start"/> is the frame being stepped from.
  /// </summary>
  public StepOutcome StepInto(StopInfo start) => WalkToNextLine(start, stepOver: false);

  /// <summary>
  /// Step OVER the next source statement: like <see cref="StepInto"/>, but a call is run to completion (a
  /// temp bp at its return address) instead of descended into.
  /// </summary>
  public StepOutcome StepOver(StopInfo start) => WalkToNextLine(start, stepOver: true);

  /// <summary>
  /// The shared step-into / step-over walk: single-step until the next source statement in view. Stops
  /// when (a) the PC lands on a statement-boundary line row differing from the start line, or (b) the
  /// start function returns to a shallower frame (Sp above the start Sp). For step-over, a step that lands
  /// in a DEEPER frame outside the start function is a call we entered — it is run to its return address
  /// (via a temp bp) and stepping resumes in the start frame. Bounded by <see cref="MaxStepInstructions"/>.
  /// </summary>
  private StepOutcome WalkToNextLine(StopInfo start, bool stepOver) {
    if (!SteppingSupported) return new StepOutcome(StepOutcomeKind.UnsupportedByAgent, default);
    if (Sidecar is not { } s) throw new DebuggerException("no debug info loaded; cannot step");

    uint startLine = s.PcToLine((uint)start.PcOffset)?.Line ?? 0;
    var startFn = s.FunctionAt((uint)start.PcOffset);
    long startSp = start.Sp;

    StopInfo cur = start;
    for (int i = 0; i < MaxStepInstructions; i++) {
      var stepped = Step();
      if (stepped.Kind != StepOutcomeKind.Stopped) return stepped;   // Exited / NotAcked / Unsupported
      cur = stepped.Stop;

      // Returned to a shallower frame: we stepped OUT of the start function; stop in the caller.
      if (cur.Sp > startSp) return new StepOutcome(StepOutcomeKind.Stopped, cur);

      uint off = (uint)cur.PcOffset;

      // step-over: a deeper frame OUTSIDE the start function means we entered a callee. Run it to its
      // return address, then re-evaluate in the start frame.
      if (stepOver && cur.Sp < startSp && OutsideFunction(off, startFn)) {
        var ran = RunCalleeToReturn(cur);
        if (ran.Kind != StepOutcomeKind.Stopped) return ran;
        cur = ran.Stop;
        if (cur.Sp > startSp) return new StepOutcome(StepOutcomeKind.Stopped, cur);   // returned past start too
        off = (uint)cur.PcOffset;
      }

      if (IsStatementStop(off, startLine)) return new StepOutcome(StepOutcomeKind.Stopped, cur);
    }
    return new StepOutcome(StepOutcomeKind.LimitReached, cur);
  }

  /// <summary>
  /// Run out of the current function: the caller's return address is <see cref="Backtrace"/> frame 1's
  /// RAW code offset (the biased form is only for symbolizing it, not for planting a bp). Arm a temp bp
  /// there, run to it, and report the caller-frame stop. <see cref="StepOutcomeKind.NoCallerFrame"/> when
  /// there is no caller (a frameless leaf the rbp-walk cannot unwind, or the top frame).
  /// </summary>
  public StepOutcome Finish(StopInfo start) {
    if (!SteppingSupported) return new StepOutcome(StepOutcomeKind.UnsupportedByAgent, default);
    var bt = Backtrace();
    if (bt.Status != BacktraceStatus.Ok || bt.Frames.Count < 2)
      return new StepOutcome(StepOutcomeKind.NoCallerFrame, default);
    return RunUntilReturn(bt.Frames[1].CodeOffset, start.Sp);
  }

  /// <summary>
  /// Run until <paramref name="line"/> in the CURRENT function, or the frame returns first. Resolves the
  /// line to an offset in the current function's file; <see cref="StepOutcomeKind.NoCode"/> when the line
  /// carries no statement or is not in this function. Arms a temp bp at the line AND (when there is a
  /// caller) at the return address, so a frame that returns before reaching the line stops honestly in the
  /// caller rather than running away.
  /// </summary>
  public StepOutcome Until(StopInfo start, uint line) {
    if (!SteppingSupported) return new StepOutcome(StepOutcomeKind.UnsupportedByAgent, default);
    if (Sidecar is not { } s) throw new DebuggerException("no debug info loaded; cannot run until");

    var loc = Symbolize(start.PcOffset);
    if (!loc.HasLine || s.LineToOffset(loc.File, line) is not { } untilOff)
      return new StepOutcome(StepOutcomeKind.NoCode, default);

    // The target line must live in the current function (until runs to a line in THIS frame, gdb-style).
    if (s.FunctionAt((uint)start.PcOffset) is { } fn && (untilOff < fn.CodeStart || untilOff >= fn.CodeEnd))
      return new StepOutcome(StepOutcomeKind.NoCode, default);

    long? retOff = null;
    var bt = Backtrace();
    if (bt.Status == BacktraceStatus.Ok && bt.Frames.Count >= 2) retOff = bt.Frames[1].CodeOffset;

    return RunToLineOrReturn(untilOff, retOff);
  }

  /// True when <paramref name="off"/> is exactly a statement-boundary line row whose line differs from
  /// <paramref name="startLine"/> — the stop condition both step-into and step-over share. Exact
  /// (row.CodeOffset == off) because single-stepping lands on arbitrary instructions; only a landing ON a
  /// statement's entry offset is a source stop, matching where `break file:line` plants.
  private bool IsStatementStop(uint off, uint startLine) {
    if (Sidecar is not { } s || s.PcToLine(off) is not { } row) return false;
    return row.CodeOffset == off
      && (row.Flags & MxdbgFormat.LineFlagStatement) != 0
      && row.Line != startLine;
  }

  /// True when <paramref name="off"/> is outside <paramref name="fn"/>'s `.text` range (or there is no
  /// enclosing function) — the "we entered a callee / returned out" test step-over uses.
  private static bool OutsideFunction(uint off, MxdbgReader.FuncInfo? fn) =>
    fn is not { } f || off < f.CodeStart || off >= f.CodeEnd;

  /// <summary>
  /// The callee we just stepped into is parked at its entry, so [Sp] is the absolute return address the
  /// CALL pushed (no prologue has run yet). Read it, and run the callee to completion via a temp bp there.
  /// Recursion inside the callee is absorbed by <see cref="RunUntilReturn"/>'s frame guard.
  /// </summary>
  private StepOutcome RunCalleeToReturn(StopInfo calleeEntry) {
    var read = ReadMemory((ulong)calleeEntry.Sp, 8);
    if (read.Status != ReadMemoryStatus.Ok) return new StepOutcome(StepOutcomeKind.NotAcknowledged, default);
    ulong retAbs = BitConverter.ToUInt64(read.Data, 0);
    return RunUntilReturn(OffsetOf(retAbs), calleeEntry.Sp);
  }

  /// <summary>
  /// Arm a TEMP breakpoint at <paramref name="offset"/> unless a USER breakpoint already sits there — in
  /// which case the user's bp already stops execution at that offset, so we must not arm (the agent would
  /// share the single 0xCC/BRK) nor later clear it (which would silently delete the user's bp). Appends to
  /// <paramref name="armed"/> only what WE armed, so the caller's cleanup touches only its own temps.
  /// Returns false only on a genuine arm failure. Arms/clears through <see cref="PostCommand"/> directly so
  /// a temp never enters <see cref="_userBreakpointOffsets"/>.
  /// </summary>
  private bool ArmTempIfNeeded(long offset, List<long> armed) {
    if (_userBreakpointOffsets.Contains(offset)) return true;   // a user bp already stops here; leave it
    if (!PostCommand(RuntimeEmitter.DbgCmdSetBp, offset)) return false;
    armed.Add(offset);
    return true;
  }

  /// Clear every temp bp WE armed (never a coinciding user bp, which was not added to the list) — the one
  /// cleanup both step runners share.
  private void ClearTempBreakpoints(List<long> armed) {
    foreach (var off in armed) PostCommand(RuntimeEmitter.DbgCmdClearBp, off);
  }

  /// <summary>
  /// Arm a temp bp at <paramref name="retOff"/>, Continue until the stop is in a frame shallower than
  /// <paramref name="innerSp"/> (skipping a hit in an equal-or-deeper frame, which is recursion re-entering
  /// the same return address), and clear the temp bp — even on early return, so no patch leaks. If a user
  /// bp already sits at <paramref name="retOff"/> it is used as-is (and left intact).
  /// </summary>
  private StepOutcome RunUntilReturn(long retOff, long innerSp) {
    var armed = new List<long>();
    if (!ArmTempIfNeeded(retOff, armed)) return new StepOutcome(StepOutcomeKind.NotAcknowledged, default);
    try {
      for (int i = 0; i < MaxReturnContinues; i++) {
        if (!Continue()) return new StepOutcome(StepOutcomeKind.NotAcknowledged, default);
        if (!WaitForStop(out var stop)) return new StepOutcome(StepOutcomeKind.Exited, default);
        if (stop.Sp > innerSp) return new StepOutcome(StepOutcomeKind.Stopped, stop);
        // else: a hit at an equal-or-deeper frame (recursion) — keep unwinding.
      }
      return new StepOutcome(StepOutcomeKind.LimitReached, default);
    } finally {
      ClearTempBreakpoints(armed);
    }
  }

  /// <summary>
  /// Arm a temp bp at <paramref name="lineOff"/> and (when a caller exists) at <paramref name="returnOff"/>,
  /// Continue once, and report the resulting stop — the caller reads the stop's location to see whether the
  /// line was reached or the frame returned first. Temp bps WE armed are always cleared, so no patch leaks;
  /// a coinciding user bp is used as-is and left intact. Unlike <see cref="RunUntilReturn"/> the first stop
  /// is the answer (no deeper-frame skipping).
  /// </summary>
  private StepOutcome RunToLineOrReturn(long lineOff, long? returnOff) {
    var armed = new List<long>();
    try {
      if (!ArmTempIfNeeded(lineOff, armed)) return new StepOutcome(StepOutcomeKind.NotAcknowledged, default);
      if (returnOff is { } r && r != lineOff && !ArmTempIfNeeded(r, armed))
        return new StepOutcome(StepOutcomeKind.NotAcknowledged, default);
      if (!Continue()) return new StepOutcome(StepOutcomeKind.NotAcknowledged, default);
      return WaitForStop(out var stop)
        ? new StepOutcome(StepOutcomeKind.Stopped, stop)
        : new StepOutcome(StepOutcomeKind.Exited, default);
    } finally {
      ClearTempBreakpoints(armed);
    }
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
