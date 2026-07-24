namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// The in-process debug agent (P3a substrate) — a `__dbg_*` runtime family emitted into EVERY
/// binary the way the `__ds_*` DebugStream family is, but UNCONDITIONALLY (not gated on
/// `--debugstream`). It is dormant: <c>__dbg_init</c> does one env-var read at startup and, unless
/// MAXON_DEBUG is set, returns having mapped nothing and armed nothing (see docs/DEBUGGER_DESIGN.md,
/// "~Zero idle cost"). The one build flag that omits it entirely is <c>--no-debug-agent</c>.
///
/// P3a is SUBSTRATE ONLY. When MAXON_DEBUG is set the agent (a) maps a named control segment and
/// announces a handshake a consumer reads to confirm "attached", and (b) installs a trap handler
/// that CHAINS with the CPU-fault handler. It patches no breakpoints, steps nothing, and carries no
/// command mailbox — those are P3b, and they are what bring the self-modifying-code / W^X risk.
///
/// It REUSES the DebugStream substrate rather than forking it: the same
/// <see cref="IEmitterBackend.OsOpenAndMapSharedMemory"/> create/open helper, the same env-var
/// activation shape (the var's VALUE names the segment, exactly as MAXON_DEBUGSTREAM does), and the
/// same versioned-handshake discipline (a magic + a schema version, announced before the segment is
/// declared live). The open-segment preamble and the unmap-at-shutdown tail are literally shared
/// (<see cref="EmitOpenActivatedSegment"/>, <see cref="EmitSharedSegmentShutdown"/>), so the agent
/// and DebugStream cannot drift apart on how a segment is opened or torn down.
/// </summary>
public partial class RuntimeEmitter {

  // =========================================================================
  // Debug agent control segment
  // =========================================================================

  /// Magic at offset 0 of the agent's control segment. Distinct from <see cref="DsMagic"/> so a
  /// consumer can never mistake a DebugStream ring for an agent segment (or vice-versa) — both ends
  /// read it back as one i64 and compare, so its ASCII shape ("MXDBAGNT") is only for the reader.
  public const long DbgControlMagic = unchecked((long)0x4D58444241474E54);

  /// The agent control-segment schema version. Like <see cref="DsVersion"/>, this is the one number
  /// the two ends must agree on; bump it when the meaning of a control-segment byte changes.
  public const long DbgControlVersion = 1;

  /// The whole agent control segment: one page. P3a uses only the handshake header; the P3b
  /// command/response mailbox grows into the rest of the page, so a consumer that maps this much
  /// today needs no re-mapping when the mailbox lands.
  public const long DbgControlSegmentSize = 4096;

  // Control-segment header offsets (from the mapped base). The consumer decodes off these SAME
  // constants — a handshake read against different offsets than the agent wrote is the "instrument
  // that lies" this project keeps closing, so there is one definition and both ends step by it.
  public const int DbgOffMagic = 0x00;
  public const int DbgOffVersion = 0x08;
  public const int DbgOffFlags = 0x10;

  /// flags bit 0: the in-process agent has mapped the segment and armed its trap handler. Released
  /// with a store-release AFTER the magic/version, so a consumer that sees this bit also sees them.
  public const long DbgFlagAgentAlive = 1;

  /// The env var whose presence activates the agent; its VALUE names the control segment (a Win32
  /// section name on Windows, a MAP_SHARED file path elsewhere), exactly as MAXON_DEBUGSTREAM does
  /// for the ring. Stated ONCE: the agent's `__dbg_env_name` symdata (what the compiled binary reads)
  /// and the consumer that sets the var (DebugAgentProbe) both derive from this, so a drift that would
  /// leave the agent silently dark is impossible.
  public const string DbgActivationEnvVar = "MAXON_DEBUG";

  // =========================================================================
  // Shared shared-memory substrate (used by BOTH the agent and DebugStream)
  // =========================================================================

  /// The scratch env-value buffer both inits read into on Windows: 128 bytes = slots 16..31, top
  /// slot at <see cref="EnvBufferTopSlot"/>. The size and the top slot are one fact (8 * 16 = 128),
  /// stated once so a change to one cannot silently disagree with the other.
  private const int EnvBufferBytes = 128;
  private const int EnvBufferTopSlot = 31;

  /// <summary>
  /// The opening move of BOTH shared-memory inits: read the activation env var, and on success
  /// open + map the named segment of <paramref name="segmentSize"/> bytes, leaving the mapped base
  /// in <see cref="VReg.Ret"/>. Branches to <paramref name="disabledLabel"/> when the var is unset
  /// (the agent's dark path) or the named segment cannot be opened (nothing created it). The caller
  /// owns what "disabled" means for its own globals.
  ///
  /// The env var's VALUE is the segment's name (a Win32 section name on Windows, a MAP_SHARED file
  /// path elsewhere) — a consumer creates the segment and passes the name via the env var, exactly
  /// as `maxon monitor` does for the ring. On Windows the value is read into the slots-16..31 buffer;
  /// off Windows getenv returns a pointer straight into the environment block.
  ///
  /// Clobbers Arg0..Arg5, Scratch3 (via OsOpenAndMapSharedMemory) and the env buffer.
  /// </summary>
  private void EmitOpenActivatedSegment(string envNameSymbol, long segmentSize, string disabledLabel) {
    if (_b.IsWindows) {
      _b.LeaSymdata(VReg.Arg0, envNameSymbol);        // lpName
      _b.LeaLocal(VReg.Arg1, EnvBufferTopSlot);       // lpBuffer
      _b.MovRegImm(VReg.Arg2, EnvBufferBytes);        // nSize
      _b.CallImport("GetEnvironmentVariableA");
      _b.JumpIfZero(VReg.Ret, disabledLabel);         // 0 chars copied = not set
      _b.LeaLocal(VReg.Arg0, EnvBufferTopSlot);       // segment name = the env value
    } else {
      _b.LeaSymdata(VReg.Arg0, envNameSymbol);
      _b.CallImport("getenv");
      _b.JumpIfZero(VReg.Ret, disabledLabel);
      _b.MovRegReg(VReg.Arg0, VReg.Ret);              // segment name = getenv result
    }

    _b.MovRegImm(VReg.Arg1, segmentSize);
    _b.OsOpenAndMapSharedMemory(VReg.Ret, VReg.Arg0, VReg.Arg1);
    _b.JumpIfZero(VReg.Ret, disabledLabel);           // segment not present / map failed
  }

  /// <summary>
  /// The shutdown tail both shared-memory families share: if attached (the base global is non-zero),
  /// clear the liveness flag so a consumer sees the producer go away, unmap the segment, and zero the
  /// base global. It does NOT destroy the named segment — the consumer that created it owns that.
  ///
  /// Clearing the whole flags qword clears liveness without touching the version, which lives in its
  /// own field: the version is a fact about the BINARY that a consumer draining a dead producer still
  /// needs, so it must outlive liveness.
  /// </summary>
  private void EmitSharedSegmentShutdown(string funcLabel, string baseGlobal, int flagsOffset,
      long segmentSize) {
    _b.FunctionStart(funcLabel, 0, 0x30);

    var doneLabel = UniqueLabel(funcLabel + "_done");

    _b.LoadGlobal(VReg.Scratch0, baseGlobal);
    _b.JumpIfZero(VReg.Scratch0, doneLabel);

    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, flagsOffset, VReg.Scratch1);

    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.MovRegImm(VReg.Arg1, segmentSize);
    _b.OsUnmapSharedMemory(VReg.Arg0, VReg.Arg1);

    _b.ZeroReg(VReg.Scratch0);
    _b.StoreGlobal(baseGlobal, VReg.Scratch0);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // Agent globals + init + shutdown
  // =========================================================================

  /// <summary>Emit the agent's globals: the mapped-base pointer (0 = dark) and the env-var name.</summary>
  public void EmitDebugAgentGlobals() {
    // Base pointer to the mapped control segment. 0 means the agent is dark: nothing mapped, no
    // handler armed. Every `__dbg_*` entry point bails on base == 0, so a dark agent is free.
    _b.DefineGlobal("__dbg_base", 8, 0);
    _b.DefineSymdata("__dbg_env_name",
      System.Text.Encoding.UTF8.GetBytes(DbgActivationEnvVar + "\0"));
  }

  /// <summary>
  /// __dbg_init — called once at startup, before user code. One env-var read; if MAXON_DEBUG is
  /// unset the agent stays dark and returns. If set, map the control segment, announce the handshake
  /// (magic, version, then — last, store-released — the "agent alive" flag), and arm the trap
  /// handler. Ordering is deliberate: the handler is armed BEFORE liveness is announced, so a
  /// consumer that observes "alive" can trust the agent is fully attached.
  ///
  /// Frame mirrors <c>__debugstream_init</c>: slot 0 = saved base (survives the calls below), slots
  /// 16..31 = the Windows env-value buffer.
  /// </summary>
  public void EmitDebugAgentInit() {
    _b.FunctionStart("__dbg_init", 0, 0x110);

    var disabledLabel = UniqueLabel("dbg_init_disabled");
    var doneLabel = UniqueLabel("dbg_init_done");

    EmitOpenActivatedSegment("__dbg_env_name", DbgControlSegmentSize, disabledLabel);

    // Ret = mapped base. Keep it in slot 0 across the trap-handler install (which clobbers scratch),
    // and publish it to the global so every other __dbg_* entry point can find it.
    _b.StoreLocal(0, VReg.Ret);
    _b.StoreGlobal("__dbg_base", VReg.Ret);

    // Announce the schema: magic first (this IS an agent segment), then the version.
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.MovRegImm(VReg.Scratch2, DbgControlMagic);
    _b.StoreIndirect(VReg.Scratch1, DbgOffMagic, VReg.Scratch2);
    _b.MovRegImm(VReg.Scratch2, DbgControlVersion);
    _b.StoreIndirect(VReg.Scratch1, DbgOffVersion, VReg.Scratch2);

    // Arm the trap handler. It CHAINS with __gt_fault_handler rather than shadowing it: on Windows
    // it is a VEH installed at the FRONT that defers every exception it does not own (in P3a, all of
    // them) to the rest of the chain — the fault thunk still handles AVs and the panic backtrace is
    // intact. On POSIX it owns SIGTRAP only, a signal distinct from the fault handler's
    // SIGSEGV/SIGFPE/SIGBUS, so the two never contend. See EmitDbgTrapHandlerThunk.
    _b.OsInstallTrapHandler("__dbg_trap_handler_thunk");

    // Announce liveness LAST, store-released so the magic/version above are visible to any consumer
    // that has seen this flag. Only now is the handshake complete.
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.MovRegImm(VReg.Scratch2, DbgFlagAgentAlive);
    _b.StoreRelease(VReg.Scratch1, DbgOffFlags, VReg.Scratch2);

    _b.Jump(doneLabel);

    _b.DefineLabel(disabledLabel);
    // Keep __dbg_base at 0 (agent dark). Nothing was mapped and no handler was armed.
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreGlobal("__dbg_base", VReg.Scratch0);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>__dbg_shutdown — clear the liveness flag and unmap the control segment at process exit.</summary>
  public void EmitDebugAgentShutdown() {
    EmitSharedSegmentShutdown("__dbg_shutdown", "__dbg_base", DbgOffFlags, DbgControlSegmentSize);
  }

  /// <summary>
  /// Emit the always-present debug agent. Called UNCONDITIONALLY (unless --no-debug-agent), unlike
  /// the DebugStream family, because the agent is dark-by-default rather than opt-in — its only
  /// startup cost when MAXON_DEBUG is unset is a single env-var read. The target-specific trap-handler
  /// thunk (`__dbg_trap_handler_thunk`) is emitted alongside the fault thunk by each backend.
  /// </summary>
  public void EmitDebugAgentFunctions() {
    EmitDebugAgentGlobals();
    EmitDebugAgentInit();
    EmitDebugAgentShutdown();
  }
}
