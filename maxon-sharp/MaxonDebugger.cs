using System.Diagnostics;
using System.Globalization;
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

  /// <summary>
  /// How long the driver waits for the AGENT to answer — the handshake, or the ack of a posted command.
  /// Generous: a debuggee only has to map a page and patch a byte, but a loaded CI box is slow, so 20s
  /// here means "the agent is broken", which is a driver-side fault.
  ///
  /// It is deliberately NOT the bound on a wait for the target to next STOP. That is a property of the
  /// USER'S PROGRAM — a breakpoint may legitimately be minutes into a run — and is bounded by
  /// <see cref="StopTimeout"/> instead. Sharing one number conflated a slow program with a broken agent.
  /// </summary>
  private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(20);

  /// <summary>
  /// The default bound on a wait for the target to reach its next stop. A debugger waits for its target,
  /// so this is far larger than <see cref="AttachTimeout"/>; it exists only so a `--batch` session cannot
  /// hang CI forever, and <see cref="StopTimeoutFlag"/> overrides it.
  /// </summary>
  private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromMinutes(10);

  /// The CLI flag that overrides <see cref="StopTimeout"/>, spelled HERE next to the deadline it sets, so
  /// the parser that reads it and every message that recommends it cannot drift apart.
  public const string StopTimeoutFlag = "--stop-timeout=";

  /// The recourse offered to a human whose deadline was too short — worded ONCE beside the flag it names,
  /// so no surface can recommend it differently.
  public const string RaiseStopTimeoutText =
    "Raise the deadline with " + StopTimeoutFlag + "<seconds> and run again.";

  /// <see cref="DefaultStopTimeout"/> as the usage banner prints it, so the help text cannot claim a
  /// default the driver does not use.
  public static string DefaultStopTimeoutText => SecondsText(DefaultStopTimeout);

  /// This session's bound on a wait for the next stop (see <see cref="DefaultStopTimeout"/>).
  public TimeSpan StopTimeout { get; }

  /// This session's stop deadline in SECONDS — the ONE value every surface reports, whether as JSON data
  /// or as prose.
  public double StopTimeoutSeconds => StopTimeout.TotalSeconds;

  /// <see cref="StopTimeoutSeconds"/> as text. Deliberately the SHORTEST ROUND-TRIP form, which is exactly
  /// what <c>Utf8JsonWriter.WriteNumber</c> emits for the same double — so the prose face and the JSON
  /// face cannot print different numbers for one deadline. A fixed "0.###" here would have: TimeSpan keeps
  /// 100ns ticks, not whole milliseconds, so `--stop-timeout=0.0004` reads 0.0004 in JSON and would have
  /// rendered as "0s" in prose — the driver claiming a deadline it was not given.
  public string StopTimeoutText => SecondsText(StopTimeout);

  private static string SecondsText(TimeSpan span) =>
    span.TotalSeconds.ToString(CultureInfo.InvariantCulture);

  /// <see cref="StopTimeout"/> as a <c>WaitForExit</c> argument, saturating rather than overflowing the
  /// cast: a deadline beyond ~24 days is still a deadline, and wrapping it would produce a NEGATIVE wait.
  private int StopTimeoutMilliseconds =>
    StopTimeout.TotalMilliseconds >= int.MaxValue ? int.MaxValue : (int)StopTimeout.TotalMilliseconds;

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
      Task stdout, Task stderr, MxdbgReader? sidecar, TimeSpan stopTimeout) {
    _mapping = mapping;
    _accessor = accessor;
    _process = process;
    _stdout = stdout;
    _stderr = stderr;
    Sidecar = sidecar;
    StopTimeout = stopTimeout;
  }

  /// <summary>
  /// Attach to <paramref name="exePath"/>: validate the sidecar's build-id (when one is supplied),
  /// create and map the control segment, and spawn the target parked at entry with its stdio forwarded.
  /// The target's stdout goes to <paramref name="targetStdout"/> (default the driver's stdout); batch
  /// mode passes the driver's stderr so its own JSON stream on stdout stays a clean, parseable channel.
  /// Throws <see cref="DebuggerException"/> with a clear reason on a build-id mismatch or a spawn
  /// failure — the caller reports it and exits nonzero, the way the schema-mismatch refusal does.
  /// <paramref name="stopTimeout"/> overrides <see cref="DefaultStopTimeout"/> for this session.
  /// </summary>
  public static MaxonDebugger Attach(string exePath, IReadOnlyList<string> targetArgs, MxdbgReader? sidecar,
      TextWriter? targetStdout = null, TimeSpan? stopTimeout = null) {
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

      return new MaxonDebugger(mapping, accessor, process, stdout, stderr, sidecar,
        stopTimeout ?? DefaultStopTimeout);
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

  // ---- Polling ----

  /// <summary>
  /// The pause every wait loop takes between two reads of the control segment. A wait here is BIMODAL and
  /// a single strategy cannot serve both ends of it:
  ///
  ///   * a command ack, or the stop that ends one single-step, comes back in MICROSECONDS — and
  ///     <see cref="WalkToNextLine"/> makes up to <see cref="MaxStepInstructions"/> such round trips for
  ///     ONE `step`, so their latency is the whole cost of stepping;
  ///   * a breakpoint stop can be MINUTES away, because it waits on the user's program.
  ///
  /// Yielding throughout serves the first and burns a whole core on the second (measured on a 1M-hit
  /// conditional breakpoint: 8,873 ms wall against 8,859 ms of CPU — 99.8% of a core, 6,156 ms of it
  /// KERNEL time, spent almost entirely in the per-poll yield and the per-poll `HasExited` syscall).
  /// Sleeping throughout serves the second and destroys stepping. So: yield for a hot burst that covers
  /// any agent round trip, then escalate to short sleeps, capped — the long tail then costs nothing.
  /// </summary>
  private struct PollBackoff {

    /// Polls served by a bare yield before the first sleep. Sized to cover an agent round trip with two
    /// orders of magnitude to spare (a measured step round trip is tens of microseconds; a poll is a few),
    /// so stepping never reaches the sleeping phase, while a wait that IS long pays this burst once.
    private const int YieldPolls = 512;

    /// Sleeping polls at one duration before it grows by a millisecond.
    private const int PollsPerSleepStep = 64;

    /// The longest the loop sleeps between two polls. It bounds how late a stop can be NOTICED, so it
    /// stays small enough to be invisible next to the human and CI timescales that reach it.
    private const int MaxSleepMs = 4;

    private int _polls;

    public void Pause() {
      _polls++;
      if (_polls <= YieldPolls) {
        Thread.Yield();
        return;
      }
      Thread.Sleep(Math.Min(1 + (_polls - YieldPolls) / PollsPerSleepStep, MaxSleepMs));
    }
  }

  // ---- Handshake ----

  /// Spin until the agent announces its handshake (magic present AND the alive flag set) or the target
  /// exits / we time out. Reliable because the target is parked at entry until released.
  public bool WaitForAgentAlive() {
    var deadline = DateTime.UtcNow + AttachTimeout;
    var backoff = new PollBackoff();
    while (true) {
      if ((_accessor.ReadInt64(RuntimeEmitter.DbgOffFlags) & RuntimeEmitter.DbgFlagAgentAlive) != 0
          && _accessor.ReadInt64(RuntimeEmitter.DbgOffMagic) == RuntimeEmitter.DbgControlMagic)
        return true;
      if (_process.HasExited) return false;
      // The deadline is tested AFTER the state reads, so a handshake that lands in the final poll window
      // is honored rather than lost to the clock.
      if (DateTime.UtcNow >= deadline) return false;
      backoff.Pause();
    }
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

  /// The registry mirrors what is ARMED IN THE TARGET, so both sides update only once the post has
  /// LANDED. Recording a breakpoint that never armed would make the step runners believe a user bp sits
  /// there and skip planting their own temp, so a `next`/`finish` would run straight past the place it
  /// meant to stop; forgetting one that is still armed is the mirror error, and lets a temp bp be planted
  /// on the user's live 0xCC and then delete it. Both became reachable once a post can be refused outright
  /// (see <see cref="StopOutstanding"/>) rather than only failing after a 20s stall.
  public bool SetBreakpointAtOffset(long codeOffset) {
    if (!PostCommand(RuntimeEmitter.DbgCmdSetBp, codeOffset)) return false;
    _userBreakpointOffsets.Add(codeOffset);
    return true;
  }

  public bool ClearBreakpointAtOffset(long codeOffset) {
    if (!PostCommand(RuntimeEmitter.DbgCmdClearBp, codeOffset)) return false;
    _userBreakpointOffsets.Remove(codeOffset);
    return true;
  }

  /// <summary>
  /// True when <paramref name="stop"/> landed on an offset where the USER has armed a breakpoint —
  /// gdb's "breakpoints win" rule, which the step runners apply to a stop they did not ask for.
  ///
  /// The registry used to be consulted at ARM time only (<see cref="ArmTempIfNeeded"/>, so a temp bp
  /// never deletes a coinciding user bp); this is the STOP-time reading of the same one fact, which is
  /// what makes `finish` and `next` honor a breakpoint hit during their run. Stated ONCE so the two
  /// runners that need it cannot disagree about what counts as the user's breakpoint.
  /// </summary>
  private bool IsAtUserBreakpoint(StopInfo stop) => _userBreakpointOffsets.Contains(stop.PcOffset);

  /// <summary>
  /// Resume the target. When a previous wait TIMED OUT the stop it asked for is still outstanding and the
  /// target was never parked, so there is nothing to resume and nothing to post — the correct continuation
  /// is simply to go on waiting for that stop, which is what returning true lets the caller do. Posting
  /// instead would be acked the instant the target parked and would RESUME it, swallowing the outstanding
  /// stop and reporting a location the target had already left.
  /// </summary>
  public bool Continue() => StopOutstanding || PostCommand(RuntimeEmitter.DbgCmdContinue, 0);

  /// The outcome of resolving+arming a breakpoint, so the resolve→arm DECISION lives once here and each
  /// surface only renders it (a divergent copy of "no code vs set" would be a wrong answer, not a compile
  /// error). <see cref="Ambiguous"/> / <see cref="NoMatch"/> arise only from a `break &lt;function&gt;`
  /// resolution (P4c); a `file:line` resolution never produces them. The two Condition* outcomes (P4d-1)
  /// arise only from a `break … if`, and both mean the breakpoint was NOT armed.
  public enum BreakKind {
    NoCode, Set, Unacknowledged, Ambiguous, NoMatch, ConditionUnsupported, ConditionInvalid,
  }

  /// <summary>
  /// The outcome of a break request. <see cref="Candidates"/> lists the matches for an
  /// <see cref="BreakKind.Ambiguous"/> function name; <see cref="Suggestion"/> is the closest known name
  /// for a <see cref="BreakKind.NoMatch"/> (empty when nothing is close enough); <see cref="ConditionError"/>
  /// is why a <see cref="BreakKind.ConditionInvalid"/> condition could not be resolved. The factories keep
  /// the empty-list / empty-string invariants in one place so no call site scatters them.
  /// </summary>
  public readonly record struct BreakResult(
    BreakKind Kind, uint Offset, SymLocation Location, IReadOnlyList<string> Candidates, string Suggestion,
    string ConditionError) {

    public static BreakResult NoCodeAt() => new(BreakKind.NoCode, 0, default, [], "", "");
    public static BreakResult AmbiguousMatch(IReadOnlyList<string> candidates) =>
      new(BreakKind.Ambiguous, 0, default, candidates, "", "");
    public static BreakResult NoMatchFor(string suggestion) => new(BreakKind.NoMatch, 0, default, [], suggestion, "");

    /// A `break … if` this binary's agent cannot evaluate. Distinct from <see cref="ConditionRefused"/>
    /// because the user actions differ: rebuild the target, versus fix the condition.
    public static BreakResult ConditionUnsupported() =>
      new(BreakKind.ConditionUnsupported, 0, default, [], "", "");

    public static BreakResult ConditionRefused(string reason) =>
      new(BreakKind.ConditionInvalid, 0, default, [], "", reason);

    /// The Set/Unacknowledged pair from an arm result — the resolve→arm outcome BOTH break paths (file:line
    /// and function) share, so an armed-but-unacknowledged breakpoint is worded identically either way.
    public static BreakResult FromAck(bool acked, uint offset, SymLocation location) =>
      new(acked ? BreakKind.Set : BreakKind.Unacknowledged, offset, location, [], "", "");
  }

  /// <summary>
  /// Resolve a `file:line` to its `.text` offset via the sidecar and arm a breakpoint there, optionally
  /// conditional. <see cref="BreakKind.NoCode"/> when the line carries no statement ("no code at that
  /// line"), <see cref="BreakKind.Unacknowledged"/> when the agent did not ack. Requires a sidecar.
  /// </summary>
  public BreakResult SetBreakpoint(string file, uint line, string condition = "") {
    if (Sidecar is not { } s) throw new DebuggerException("no debug info loaded; cannot resolve file:line");
    if (s.LineToOffset(file, line) is not { } off) return BreakResult.NoCodeAt();

    return ArmAtOffset(off, condition);
  }

  // ---- Fuzzy function breakpoints (P4c) ----

  /// <summary>
  /// Resolve <paramref name="name"/> to a function and arm a breakpoint at its FIRST-STATEMENT offset
  /// (past the prologue), the way `break Account.withdraw` / `break helper` targets a function's entry.
  /// The name is matched by a ranked strategy — EXACT full name, then QUALIFIED dotted-suffix
  /// (`String.isAscii` → `stdlib.String.isAscii`), then LEAF segment (`withdraw` → `Account.withdraw`),
  /// then unambiguous PREFIX. A unique match at the strongest non-empty tier is armed; two or more is
  /// reported <see cref="BreakKind.Ambiguous"/> with the candidates (never silently resolved — a wrong
  /// breakpoint target is a wrong answer). When no tier matches, the closest name within a small edit
  /// distance is offered as a <see cref="BreakKind.NoMatch"/> suggestion — edit distance NEVER arms a
  /// breakpoint, so a typo suggests rather than silently stopping at the wrong place. Requires a sidecar.
  /// </summary>
  public BreakResult SetBreakpointAtFunction(string name, string condition = "") {
    if (Sidecar is not { } s)
      throw new DebuggerException("no debug info loaded; cannot resolve a function name");

    var matches = ResolveFunctions(s, name);
    if (matches.Count == 1) return ArmAtOffset(FunctionEntryOffset(s, matches[0]), condition);

    if (matches.Count > 1)
      return BreakResult.AmbiguousMatch([.. matches.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal)]);

    var suggestion = DebugFuzzy.ClosestMatch(name, FunctionNames(s), DebugFuzzy.MaxEditDistance);
    return BreakResult.NoMatchFor(suggestion ?? "");
  }

  /// The named functions matching <paramref name="name"/> at the STRONGEST non-empty resolution tier
  /// (exact → qualified dotted-suffix → leaf segment → prefix). Edit distance is deliberately NOT a tier;
  /// it feeds only the no-match suggestion, so a typo can never silently arm a breakpoint here.
  private static IReadOnlyList<MxdbgReader.FuncInfo> ResolveFunctions(MxdbgReader s, string name) {
    var funcs = new List<MxdbgReader.FuncInfo>((int)s.FunctionCount);
    for (uint i = 0; i < s.FunctionCount; i++) {
      var f = s.Function(i);
      if (f.Name.Length > 0) funcs.Add(f);
    }

    var exact = funcs.Where(f => f.Name == name).ToList();
    if (exact.Count > 0) return exact;

    if (name.Contains('.')) {
      var qualified = funcs.Where(f => f.Name.EndsWith("." + name, StringComparison.Ordinal)).ToList();
      if (qualified.Count > 0) return qualified;
    }

    var leaf = funcs.Where(f => LeafSegment(f.Name) == name).ToList();
    if (leaf.Count > 0) return leaf;

    var prefix = funcs.Where(f => f.Name.StartsWith(name, StringComparison.Ordinal)).ToList();
    return prefix;
  }

  /// The breakpoint offset for a function: the smallest STATEMENT-flagged line-table offset within the
  /// function's `[CodeStart, CodeEnd)` range — its first statement, past the prologue — or CodeStart when
  /// no row is flagged. The same landing `break file:line` plants on the function's opening statement.
  private static uint FunctionEntryOffset(MxdbgReader s, MxdbgReader.FuncInfo fn) {
    uint? best = null;
    for (uint i = fn.LineFirst; i < fn.LineFirst + fn.LineCount; i++) {
      var row = s.Line(i);
      if ((row.Flags & MxdbgFormat.LineFlagStatement) == 0) continue;
      if (row.CodeOffset < fn.CodeStart || row.CodeOffset >= fn.CodeEnd) continue;
      if (best is null || row.CodeOffset < best) best = row.CodeOffset;
    }
    return best ?? fn.CodeStart;
  }

  /// The trailing `.`-delimited segment of a qualified function name (`withdraw` of `Account.withdraw`).
  private static string LeafSegment(string qualifiedName) {
    int dot = qualifiedName.LastIndexOf('.');
    return dot < 0 ? qualifiedName : qualifiedName[(dot + 1)..];
  }

  // ---- Conditional breakpoints (P4d-1) ----

  /// True when this binary's agent evaluates a breakpoint condition in-process before publishing a stop
  /// (control version ≥ DbgCondBpMinVersion). The gate matters more here than for the other capabilities:
  /// an older agent would ack the unknown set-condition command and then stop on EVERY hit, so a silent
  /// downgrade would not be a missing feature but a wrong answer.
  public bool CondBpSupported => AgentVersion >= RuntimeEmitter.DbgCondBpMinVersion;

  /// A parsed and RESOLVED breakpoint condition: exactly the words of the agent's condition record, so
  /// this record and <see cref="PostSetBpCond"/> are the only places the driver knows that layout.
  private readonly record struct ResolvedCondition(long Op, long Imm, long Slot, long Width, long Signed);

  /// <summary>
  /// Arm a breakpoint at an already-resolved <paramref name="offset"/>, optionally conditional. The ONE
  /// place a resolved offset becomes an armed breakpoint, so `break file:line` and `break &lt;function&gt;`
  /// cannot differ in how a condition is attached, gated, or refused.
  ///
  /// A condition is REFUSED, never dropped — for an agent too old to evaluate one, for a condition this
  /// grammar cannot express, and for a set-condition command that fails to land (which un-arms the
  /// breakpoint again). Every one of those paths would otherwise turn "stop when i == 7" into "stop every
  /// time", which is a wrong answer the user has no way to notice.
  /// </summary>
  private BreakResult ArmAtOffset(uint offset, string condition) {
    if (condition.Length == 0)
      return BreakResult.FromAck(SetBreakpointAtOffset(offset), offset, Symbolize(offset));

    if (!CondBpSupported) return BreakResult.ConditionUnsupported();
    if (!TryResolveCondition(offset, condition, out var resolved, out var error))
      return BreakResult.ConditionRefused(error);

    // The condition attaches to the table slot the ARM allocates, so it can only be posted afterwards —
    // and if it does not land, the breakpoint must not be left behind firing unconditionally.
    if (!SetBreakpointAtOffset(offset)) return BreakResult.FromAck(false, offset, Symbolize(offset));
    if (!PostSetBpCond(offset, resolved)) {
      ClearBreakpointAtOffset(offset);
      return BreakResult.FromAck(false, offset, Symbolize(offset));
    }
    return BreakResult.FromAck(true, offset, Symbolize(offset));
  }

  /// The condition grammar's relational operators, paired with the agent opcode each maps to. Ordered
  /// LONGEST-FIRST so `<=` is matched before `<` would swallow its head.
  private static readonly (string Text, long Op)[] ConditionOperators = [
    ("==", RuntimeEmitter.DbgCondOpEq),
    ("!=", RuntimeEmitter.DbgCondOpNe),
    ("<=", RuntimeEmitter.DbgCondOpLe),
    (">=", RuntimeEmitter.DbgCondOpGe),
    ("<", RuntimeEmitter.DbgCondOpLt),
    (">", RuntimeEmitter.DbgCondOpGt),
  ];

  /// The sidecar's spelling of `bool`: a 0/1 byte. It is the one Primitive name the `i`/`u` signedness
  /// convention below does not describe, and it is read UNSIGNED — the same reading
  /// <c>DbgValueRenderer.RenderPrimitive</c> gives it, so a condition on a bool and a `print` of the same
  /// bool can never disagree.
  private const string BoolPrimitiveName = "i1";

  /// <summary>
  /// Parse `&lt;local&gt; &lt;relop&gt; &lt;literal&gt;` and resolve it against the function containing
  /// <paramref name="codeOffset"/>, producing the record the agent evaluates. Deliberately tight: the
  /// agent reads the operand inside a trap handler with no fault guard (the P4a residual), so the grammar
  /// admits only a DIRECT stack-slot scalar local, and everything outside it is refused BY NAME rather
  /// than approximated.
  ///
  /// The compiler's own expression parser is not reusable here — it is private and emits IR as a side
  /// effect of parsing — so this is a standalone scanner over the three-token grammar.
  /// </summary>
  private bool TryResolveCondition(uint codeOffset, string text, out ResolvedCondition condition,
      out string error) {
    condition = default;
    var s = RequireSidecar();
    var src = text.Trim();

    // IDENT — dots are consumed INTO the identifier so a dotted path is refused with a message naming
    // the whole path, rather than tokenizing as `a` followed by an unparseable operator.
    int i = 0;
    while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_' || src[i] == '.')) i++;
    var ident = src[..i];
    if (ident.Length == 0) {
      error = "expected a local variable name before the comparison";
      return false;
    }
    if (ident.Contains('.')) {
      error = $"'{ident}' is a dotted path; a condition can only test a plain local";
      return false;
    }

    while (i < src.Length && char.IsWhiteSpace(src[i])) i++;

    long op = 0;
    bool haveOp = false;
    foreach (var (opText, opCode) in ConditionOperators) {
      if (!src.AsSpan(i).StartsWith(opText)) continue;
      op = opCode;
      i += opText.Length;
      haveOp = true;
      break;
    }
    if (!haveOp) {
      error = $"expected one of {string.Join(' ', ConditionOperators.Select(o => o.Text))} after '{ident}'";
      return false;
    }

    if (!TryParseConditionLiteral(src[i..].Trim(), out long imm, out error)) return false;

    if (s.FunctionAt(codeOffset) is not { } fn) {
      error = "the breakpoint is not inside a known function, so it has no locals to test";
      return false;
    }

    MxdbgReader.LocalInfo? match = null;
    foreach (var loc in StackLocals(s, fn)) {
      if (loc.Name != ident) continue;
      match = loc;
      break;
    }
    if (match is not { } local) {
      var where = fn.Name.Length > 0 ? fn.Name : "that function";
      error = $"no local named '{ident}' with a stack home in {where}";
      return false;
    }

    if (!TryScalarOperandShape(s, local.TypeId, out long width, out long signedFlag, out error)) return false;

    condition = new ResolvedCondition(op, imm, local.LocValue, width, signedFlag);
    return true;
  }

  /// `true` / `false` / an optionally-signed decimal integer — the whole LITERAL grammar. A bool literal
  /// resolves to the 1/0 the runtime actually stores, so `flag == true` and `flag == 1` are one condition.
  private static bool TryParseConditionLiteral(string text, out long value, out string error) {
    error = "";
    value = 0;
    if (text == "true") {
      value = 1;
      return true;
    }
    if (text == "false") return true;
    if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return true;

    value = 0;
    error = text.Length == 0
      ? "expected a literal (a number, true, or false) after the comparison"
      : $"'{text}' is not a supported literal — a condition compares a local against a number, true, or "
        + "false, not against another expression";
    return false;
  }

  /// <summary>
  /// How the agent must read a local of <paramref name="typeId"/>: how many bytes, and whether to
  /// sign-extend them. Anything that is not a scalar integer or bool is REFUSED by name, so an
  /// unsupported operand never yields a breakpoint that quietly ignores its condition.
  ///
  /// Signedness comes from the sidecar's Primitive NAMING CONVENTION (`i&lt;bits&gt;` signed,
  /// `u&lt;bits&gt;` unsigned — DebugInfoBuilder writes the IrType names straight through) and the WIDTH
  /// from the type record's own Size field, so no per-name width table is restated here to drift.
  /// </summary>
  private static bool TryScalarOperandShape(MxdbgReader s, uint typeId, out long width, out long signedFlag,
      out string error) {
    width = 0;
    signedFlag = 0;
    if (typeId >= s.TypeCount) {
      error = "this binary's debug info does not describe that local's type";
      return false;
    }

    var t = s.Type(typeId);
    bool signed;
    switch (t.Kind) {
      // A ranged alias is a signed machine integer, read exactly as DbgValueRenderer reads one.
      case MxdbgTypeKind.IntRanged:
        signed = true;
        break;
      case MxdbgTypeKind.Primitive when t.Name == BoolPrimitiveName:
        signed = false;
        break;
      case MxdbgTypeKind.Primitive when t.Name.StartsWith('i'):
        signed = true;
        break;
      case MxdbgTypeKind.Primitive when t.Name.StartsWith('u'):
        signed = false;
        break;
      default:
        error = $"'{t.Name}' is not an integer or bool; a condition can only compare a scalar local";
        return false;
    }

    width = t.Size;
    if (width != 1 && width != 2 && width != 4 && width != RuntimeEmitter.DbgCondWordSize) {
      error = $"'{t.Name}' occupies {t.Size} bytes, which the agent's operand load does not cover (1, 2, 4, or 8)";
      return false;
    }

    // The agent orders SIGNED. A narrower unsigned operand zero-extends into the non-negative range and
    // compares correctly there, but a full-width one with its top bit set would order as a negative — so
    // it is refused rather than mis-compared.
    if (!signed && width == RuntimeEmitter.DbgCondWordSize) {
      error = $"'{t.Name}' is a full-width unsigned integer, which this condition evaluator cannot order";
      return false;
    }

    signedFlag = signed ? 1 : 0;
    error = "";
    return true;
  }

  // ---- Sidecar name pools (completion + fuzzy suggestions; P4c) ----

  /// The names of every function the sidecar records, skipping the unnamed runtime helpers — the pool the
  /// completion engine and the function-break "did you mean" both draw from. Static so the non-interactive
  /// `--complete` gate can read a bare <see cref="MxdbgReader"/> without a live session.
  public static IReadOnlyList<string> FunctionNames(MxdbgReader s) {
    var names = new List<string>((int)s.FunctionCount);
    for (uint i = 0; i < s.FunctionCount; i++) {
      var name = s.Function(i).Name;
      if (name.Length > 0) names.Add(name);
    }
    return names;
  }

  /// The LEAF names of the sidecar's source files — the spelling `break foo.maxon:N` uses (LineToOffset
  /// matches by trailing path component), so completing the leaf is consistent with how a file resolves.
  public static IReadOnlyList<string> FileNames(MxdbgReader s) {
    var names = new List<string>((int)s.FileCount);
    for (uint i = 0; i < s.FileCount; i++) {
      var leaf = MxdbgReader.LeafPathComponent(s.FileName(i));
      if (leaf.Length > 0) names.Add(leaf);
    }
    return names;
  }

  /// <summary>
  /// The NAMED locals of <paramref name="fn"/> that have a frame-pointer-relative stack home — the only
  /// ones anything can be resolved against at a stop, since a register-only or optimized-out local has no
  /// stable address to read. ONE definition of the local-table window `[LocalFirst, LocalFirst+LocalCount)`
  /// AND of the stack-slot filter, so the completion pool and the breakpoint-condition resolver cannot
  /// drift on which locals are addressable (mirroring <c>DbgValueRenderer.StackLocals</c>, which applies
  /// the same rule to decide which locals `print` and `locals` can read).
  /// </summary>
  private static IEnumerable<MxdbgReader.LocalInfo> StackLocals(MxdbgReader s, MxdbgReader.FuncInfo fn) {
    for (uint i = fn.LocalFirst; i < fn.LocalFirst + fn.LocalCount; i++) {
      var loc = s.Local(i);
      if (loc.LocKind == MxdbgLocKind.StackSlotRbpRel && loc.Name.Length > 0) yield return loc;
    }
  }

  /// The named stack-slot locals of the function enclosing <paramref name="atCodeOffset"/> — the ones a
  /// later `print`/`locals` can actually read (register-only and optimized-out locals carry no stack home,
  /// so completing them would offer a name `print` then rejects). Empty outside any known function.
  public static IReadOnlyList<string> LocalNames(MxdbgReader s, long atCodeOffset) =>
    s.FunctionAt((uint)atCodeOffset) is { } fn ? [.. StackLocals(s, fn).Select(l => l.Name)] : [];

  /// Live-session conveniences over the loaded sidecar — the interactive REPL builds its completion
  /// context through these; the `--complete` gate uses the static forms on a reader it loads directly.
  public IReadOnlyList<string> FunctionNames() => FunctionNames(RequireSidecar());
  public IReadOnlyList<string> FileNames() => FileNames(RequireSidecar());
  public IReadOnlyList<string> LocalNames(long atStopPc) => LocalNames(RequireSidecar(), atStopPc);

  private MxdbgReader RequireSidecar() =>
    Sidecar ?? throw new DebuggerException("no debug info loaded");

  /// <summary>
  /// Post one command to the agent's mailbox and wait for its ack. Writes Cmd + CmdArg, then rings the
  /// doorbell and awaits the ack. Returns false on timeout or target exit — except a Continue the target
  /// races to exit after, which is treated as done (the program legitimately ran to completion).
  /// </summary>
  private bool PostCommand(long cmd, long arg) {
    // The mailbox is only serviced by a PARKED target. With a stop outstanding the target is still
    // running, so a post would sit unacked for the whole AttachTimeout and then — if the target parked
    // meanwhile — be acked and resume it, consuming the stop that was outstanding. Refused fast instead,
    // so a surface reports "not acknowledged" immediately rather than stalling for 20s on each command.
    if (StopOutstanding) return false;

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
  /// Post a set-condition command: stage the condition record at
  /// <see cref="RuntimeEmitter.DbgOffCondStage"/>, then ring the doorbell with CmdArg = the breakpoint's
  /// CODE OFFSET (the agent resolves that to its own table slot, which stays the agent's secret). Split
  /// from <see cref="PostCommand"/> only because it writes the staged record first; the doorbell + ack
  /// discipline is the shared one, and the doorbell's release is what publishes the staged bytes.
  /// </summary>
  private bool PostSetBpCond(long codeOffset, ResolvedCondition c) {
    _accessor.Write(RuntimeEmitter.DbgOffCondStage + RuntimeEmitter.DbgCondOffKind,
      RuntimeEmitter.DbgCondKindScalarCompare);
    _accessor.Write(RuntimeEmitter.DbgOffCondStage + RuntimeEmitter.DbgCondOffOp, c.Op);
    _accessor.Write(RuntimeEmitter.DbgOffCondStage + RuntimeEmitter.DbgCondOffImm, c.Imm);
    _accessor.Write(RuntimeEmitter.DbgOffCondStage + RuntimeEmitter.DbgCondOffSlot, c.Slot);
    _accessor.Write(RuntimeEmitter.DbgOffCondStage + RuntimeEmitter.DbgCondOffWidth, c.Width);
    _accessor.Write(RuntimeEmitter.DbgOffCondStage + RuntimeEmitter.DbgCondOffSigned, c.Signed);

    _accessor.Write(RuntimeEmitter.DbgOffCmd, RuntimeEmitter.DbgCmdSetBpCond);
    _accessor.Write(RuntimeEmitter.DbgOffCmdArg, codeOffset);
    return RingDoorbellAndAwaitAck(RuntimeEmitter.DbgCmdSetBpCond);
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
    var backoff = new PollBackoff();
    while (true) {
      Thread.MemoryBarrier();
      if (_accessor.ReadInt64(RuntimeEmitter.DbgOffAckSeq) >= _cmdSeq) return true;
      if (_process.HasExited) return cmd == RuntimeEmitter.DbgCmdContinue;
      if (DateTime.UtcNow >= deadline) return false;
      backoff.Pause();
    }
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
  /// How a wait for the next stop ended. The three stay DISTINCT — as <see cref="BacktraceStatus"/> and
  /// <see cref="ReadMemoryStatus"/> do — because they demand OPPOSITE reactions, and telling them apart is
  /// the whole point of the type: an <see cref="Exited"/> target is GONE and the session is over, whereas a
  /// <see cref="TimedOut"/> one is still ALIVE and must be reported honestly and released.
  ///
  /// The bool this replaced conflated exactly those two, so a breakpoint the target had simply not reached
  /// yet was published as a clean run to completion — a wrong answer — while the still-live debuggee was
  /// orphaned and the driver blocked forever joining stdio pipes that could never close.
  /// </summary>
  public enum StopWaitStatus {
    /// The agent published a new stop event; <c>Stop</c> holds its reason/PC/SP/FP.
    Stopped,
    /// The target exited before publishing one — it ran to completion.
    Exited,
    /// <see cref="StopTimeout"/> elapsed with the target still running. NOT an exit.
    TimedOut,
  }

  public readonly record struct StopWaitResult(StopWaitStatus Status, StopInfo Stop);

  /// <summary>
  /// True when a stop-wait gave up while the target was still RUNNING, so the stop the driver asked for
  /// has never been consumed and the target is not parked at the agent's mailbox.
  ///
  /// It is the one fact that makes the timeout path SAFE rather than merely honest. Two observed wrong
  /// answers came from not tracking it: a retried `continue` was acked the moment the target parked and
  /// resumed it, so the driver then reported the already-published stop as current — a location the
  /// target had left; and a timed-out step's temp-breakpoint cleanup blocked for the whole
  /// <see cref="AttachTimeout"/> against an agent that could not answer.
  /// </summary>
  public bool StopOutstanding { get; private set; }

  /// <summary>
  /// Wait for the agent to publish a NEW stop event (StopSeq advances past the watermark). Polling against
  /// a watermark lets one session span multiple stops without the next wait returning on a stale seq.
  /// Bounded by <see cref="StopTimeout"/>, NOT by <see cref="AttachTimeout"/>: how long until the target
  /// next stops is a property of the user's program, not of the agent's responsiveness.
  /// </summary>
  public StopWaitResult WaitForStop() {
    var deadline = DateTime.UtcNow + StopTimeout;
    var backoff = new PollBackoff();
    while (true) {
      Thread.MemoryBarrier();
      long seq = _accessor.ReadInt64(RuntimeEmitter.DbgOffStopSeq);
      if (seq > _stopWatermark) {
        _stopWatermark = seq;
        Thread.MemoryBarrier();
        StopOutstanding = false;
        return new StopWaitResult(StopWaitStatus.Stopped, new StopInfo(
          _accessor.ReadInt64(RuntimeEmitter.DbgOffStopReason),
          _accessor.ReadInt64(RuntimeEmitter.DbgOffStopPc),
          _accessor.ReadInt64(RuntimeEmitter.DbgOffStopSp),
          _accessor.ReadInt64(RuntimeEmitter.DbgOffStopFp)));
      }

      if (_process.HasExited) {
        StopOutstanding = false;
        return new StopWaitResult(StopWaitStatus.Exited, default);
      }

      // Both live states are read BEFORE the clock, so a stop (or an exit) that lands in the final poll
      // window is reported as what it is rather than as a timeout.
      if (DateTime.UtcNow >= deadline) {
        StopOutstanding = true;
        return new StopWaitResult(StopWaitStatus.TimedOut, default);
      }

      backoff.Pause();
    }
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
  /// running to completion, a step whose stop never arrived inside <see cref="StopTimeout"/> (the target
  /// is STILL RUNNING — not the same thing as exiting), a runaway that hit the instruction cap, a
  /// <c>finish</c> with no caller frame, and an <c>until</c> whose line has no code each need different
  /// messages, not one "failed".
  public enum StepOutcomeKind {
    Stopped, Exited, TimedOut, UnsupportedByAgent, NotAcknowledged, LimitReached, NoCallerFrame, NoCode,
  }

  public readonly record struct StepOutcome(StepOutcomeKind Kind, StopInfo Stop);

  /// The step outcome a stop-wait produces — ONE mapping, so the three runners that wait for a stop
  /// mid-step cannot disagree about what an exit or a timeout means. Before the tri-state they each read
  /// "no stop" as <see cref="StepOutcomeKind.Exited"/>, which is how a step over a slow call reported the
  /// target as finished while it was still running.
  private static StepOutcome StepOutcomeOf(StopWaitResult wait) => wait.Status switch {
    StopWaitStatus.Stopped => new StepOutcome(StepOutcomeKind.Stopped, wait.Stop),
    StopWaitStatus.Exited => new StepOutcome(StepOutcomeKind.Exited, default),
    StopWaitStatus.TimedOut => new StepOutcome(StepOutcomeKind.TimedOut, default),
    _ => throw new InvalidOperationException($"Unhandled stop-wait status {wait.Status}"),
  };

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
    return StepOutcomeOf(WaitForStop());
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

      // BREAKPOINTS WIN, in the walk too: a user breakpoint on an instruction inside the stepped range
      // would otherwise be walked straight over, since the walk single-steps (the trap byte never runs)
      // and only a STATEMENT boundary ends it. The stop keeps its honest reason "step" — the agent
      // completed a single-step here, the INT3/BRK genuinely did not fire — which is a real distinction
      // from the RunUntilReturn case, where the trap did execute and the agent said so. The LOCATION is
      // the answer either way, and neither face has to invent a reason the agent never published.
      if (IsAtUserBreakpoint(cur)) return new StepOutcome(StepOutcomeKind.Stopped, cur);

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

  /// <summary>
  /// Clear every temp bp WE armed (never a coinciding user bp, which was not added to the list) — the one
  /// cleanup both step runners share.
  ///
  /// A step that TIMED OUT is the one exit that cannot clear them: the target is still running, so each
  /// clear would be posted to an unserviced mailbox. Those patches instead die with the target, which
  /// every timeout path now guarantees — batch ends the session, and the interactive step-timeout arm
  /// ends it too, precisely because temp traps in a running target cannot be reconciled from here.
  /// </summary>
  private void ClearTempBreakpoints(List<long> armed) {
    if (StopOutstanding) return;
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

        var wait = WaitForStop();
        if (wait.Status != StopWaitStatus.Stopped) return StepOutcomeOf(wait);
        var stop = wait.Stop;

        // BREAKPOINTS WIN: a user breakpoint hit while running out reports there, ahead of the frame
        // guard below — which would otherwise read it as recursion re-entering the armed return address
        // and continue straight past it. Its Reason is already DbgStopReasonBreakpoint (the agent
        // published it as one), so it renders as a breakpoint stop at the right place with no new
        // outcome kind; the caller's `finally` cancels the run by clearing our temp bp, exactly as gdb
        // abandons a `finish` that a breakpoint interrupts.
        if (IsAtUserBreakpoint(stop)) return new StepOutcome(StepOutcomeKind.Stopped, stop);

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
  ///
  /// It therefore needs no <see cref="IsAtUserBreakpoint"/> check, and the asymmetry with
  /// <see cref="RunUntilReturn"/> is not an oversight: returning the FIRST stop already honors a user
  /// breakpoint hit on the way, because `until` never continues past a stop in the first place.
  /// </summary>
  private StepOutcome RunToLineOrReturn(long lineOff, long? returnOff) {
    var armed = new List<long>();
    try {
      if (!ArmTempIfNeeded(lineOff, armed)) return new StepOutcome(StepOutcomeKind.NotAcknowledged, default);
      if (returnOff is { } r && r != lineOff && !ArmTempIfNeeded(r, armed))
        return new StepOutcome(StepOutcomeKind.NotAcknowledged, default);
      if (!Continue()) return new StepOutcome(StepOutcomeKind.NotAcknowledged, default);
      return StepOutcomeOf(WaitForStop());
    } finally {
      ClearTempBreakpoints(armed);
    }
  }

  // ---- Process lifetime ----

  public bool HasExited => _process.HasExited;
  public int ExitCode => _process.ExitCode;

  /// True when the DRIVER killed the target rather than the target reaching its own exit.
  private bool _terminated;

  /// <summary>
  /// How the target finished — the ONE classification every surface renders, so the REPL's closing line,
  /// the batch exit event and the probes' report cannot describe the same outcome three different ways.
  /// A <see cref="Terminated"/> target reports THAT rather than an exit code, because the status a killed
  /// process carries is the OS's (−1 on Windows, 137 on POSIX) and publishing it as the program's answer
  /// would be both a wrong answer and platform-specific noise.
  /// </summary>
  public enum TargetOutcome { Exited, Terminated, Running }

  public TargetOutcome Outcome =>
    _terminated ? TargetOutcome.Terminated
    : _process.HasExited ? TargetOutcome.Exited
    : TargetOutcome.Running;

  /// <see cref="Outcome"/> rendered for a human log line — one wording for every prose surface. The JSON
  /// face renders <see cref="Outcome"/> structurally instead of reusing this.
  public string OutcomeText => Outcome switch {
    TargetOutcome.Exited => ExitCode.ToString(CultureInfo.InvariantCulture),
    TargetOutcome.Terminated => "(terminated by the debugger)",
    TargetOutcome.Running =>
      throw new InvalidOperationException("the target has not finished; EndSession must run first"),
    _ => throw new InvalidOperationException($"Unhandled target outcome {Outcome}"),
  };

  /// Wait for a RELEASED target to reach its own exit, bounded by <see cref="StopTimeout"/> — the same
  /// deadline every other wait on the user's program uses, rather than a second number of its own. False
  /// means it is still running.
  public bool WaitForTargetExit() => _process.WaitForExit(StopTimeoutMilliseconds);

  /// Kill a still-running target. Tolerates it exiting between the check and the kill (a normal race).
  /// Private because <see cref="EndSession"/> is the one place a session decides the target must go —
  /// every face that used to call this directly was stating that decision a second time.
  private void Terminate() {
    try {
      if (_process.HasExited) return;
      _process.Kill();
      _terminated = true;
    } catch (InvalidOperationException) {
      // The process exited between the HasExited check and Kill — already gone, nothing to do.
    }
  }

  /// <summary>
  /// How <see cref="EndSession"/> treats a target that has not exited yet. The two arms are the honest
  /// distinction between a target that can still make progress and one that cannot.
  /// </summary>
  public enum SessionEnd {
    /// Wait out <see cref="StopTimeout"/> first: the target was RELEASED and is finishing the user's
    /// program, so its remaining runtime is the program's business rather than a hang.
    AwaitRelease,
    /// Do not wait: the session is abandoning the target (a timeout, a `quit`, a failed handshake, a
    /// frame nothing will continue). A target that cannot make progress is not worth waiting on.
    Immediate,
  }

  /// <summary>
  /// End the session: make sure the target is GONE, then drain its forwarded stdio. EVERY exit path runs
  /// this — a clean completion, `quit`, a timeout, an error, and <see cref="Dispose"/> — because both
  /// halves are obligations rather than conveniences, and each was missed on its own:
  ///
  ///   * a target still parked in the agent's stop-the-world loop OUTLIVES the driver, which is the
  ///     orphaned debuggee a timed-out session used to strand;
  ///   * <see cref="JoinIo"/>'s tasks only complete when the target's pipes CLOSE, so joining them while
  ///     that orphan is alive blocks the driver forever — the same defect's other half.
  ///
  /// Idempotent, so a face may call it and <see cref="Dispose"/> will call it again.
  /// </summary>
  public void EndSession(SessionEnd how) {
    int graceMs = how switch {
      SessionEnd.AwaitRelease => StopTimeoutMilliseconds,
      SessionEnd.Immediate => 0,
      _ => throw new InvalidOperationException($"Unhandled session end {how}"),
    };

    if (!_process.WaitForExit(graceMs)) {
      Terminate();
      // Kill only REQUESTS termination; wait so HasExited / ExitCode are settled before a surface reports.
      _process.WaitForExit();
    }

    JoinIo();
  }

  /// Join the stdio-forwarding tasks so all of the target's output has been written before the driver
  /// prints its own closing lines. Private because it is only ever correct AFTER the target is gone —
  /// <see cref="EndSession"/> is the one place that ordering is stated.
  private void JoinIo() {
    try {
#pragma warning disable VSTHRD002 // synchronous entry point, no SyncContext to deadlock against
      _stdout.Wait();
      _stderr.Wait();
#pragma warning restore VSTHRD002
    } catch (AggregateException) {
      // A forwarding task faulted — the sink was closed, or the pipe broke as the target died. Its
      // remaining output is lost either way, and this runs from Dispose, where rethrowing would MASK
      // the exception that ended the session. Waiting has observed the fault, so it cannot resurface.
    }
  }

  public void Dispose() {
    // The last line of the no-orphan guarantee: an exceptional exit from a session loop skips the face's
    // own EndSession, and would otherwise strand a debuggee spinning in the agent's stop-the-world loop.
    // Immediate, because a session ending in an exception is abandoning its target by definition.
    EndSession(SessionEnd.Immediate);
    _accessor.Dispose();
    _mapping.Dispose();
    _process.Dispose();
  }
}

/// A driver-side refusal (bad sidecar, failed attach) — reported as a tool failure with a nonzero
/// exit, the way DebugStreamMonitor refuses a schema mismatch, NOT as a compiler error code.
internal sealed class DebuggerException(string message) : Exception(message);
