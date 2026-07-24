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
  ///
  /// v2 (P3b) grew the P3a handshake header into a bidirectional mailbox: a driver→agent command slot
  /// with an ack doorbell, a stop-at-entry mode flag, and an agent→driver stop-event slot (see the
  /// DbgOff* offsets below). The header fields v1 defined (magic/version/flags) keep their meaning and
  /// offsets, so a v1 consumer still reads the handshake correctly — the driver reports whatever
  /// version it finds rather than refusing a v2 agent, and only the P3b mailbox commands need v2.
  public const long DbgControlVersion = 2;

  /// The whole agent control segment: one page. The P3a handshake header and the P3b command/stop
  /// mailbox both live in this one page, so a consumer that maps this much needs no re-mapping.
  public const long DbgControlSegmentSize = 4096;

  // Control-segment layout (offsets from the mapped base). The driver decodes off these SAME
  // constants — a mailbox read against different offsets than the agent wrote is the "instrument
  // that lies" this project keeps closing, so there is one definition and both ends step by it.
  //
  // Handshake header (v1, unchanged):
  public const int DbgOffMagic = 0x00;
  public const int DbgOffVersion = 0x08;
  public const int DbgOffFlags = 0x10;

  // Command channel (driver writes, agent reads) — v2:
  //   The driver fills Cmd + CmdArg, then release-bumps CmdSeq (the doorbell). The agent
  //   acquire-loads CmdSeq, dispatches the command, then release-writes CmdSeq into AckSeq. The
  //   driver posts one command at a time and waits for AckSeq == the seq it posted, so Cmd/CmdArg are
  //   stable across the agent's read.
  public const int DbgOffCmdSeq = 0x18;
  public const int DbgOffCmd = 0x20;
  public const int DbgOffCmdArg = 0x28;
  public const int DbgOffAckSeq = 0x30;

  // Mode (driver writes once, before spawn) — v2:
  //   Non-zero asks the agent to PARK at entry (after the handshake, before user code) so the driver
  //   can set breakpoints first, gdb-style. Zero is the P3a behavior: __dbg_init returns straight
  //   away, which is why the P3a attach probe — which never sets this — still runs the target to
  //   completion unchanged.
  public const int DbgOffStopAtEntry = 0x38;

  // Stop-event channel (agent writes, driver reads) — v2:
  //   The agent fills Reason/Pc/Sp/Fp, then release-bumps StopSeq. The driver polls StopSeq for a new
  //   value and then reads the fields. StopPc is a CODE OFFSET (pc - &mrt_start), the same base the
  //   panic symbolizer subtracts, so the driver resolves it through the sidecar independent of ASLR.
  public const int DbgOffStopSeq = 0x40;
  public const int DbgOffStopReason = 0x48;
  public const int DbgOffStopPc = 0x50;
  public const int DbgOffStopSp = 0x58;
  public const int DbgOffStopFp = 0x60;

  /// flags bit 0: the in-process agent has mapped the segment and armed its trap handler. Released
  /// with a store-release AFTER the magic/version, so a consumer that sees this bit also sees them.
  public const long DbgFlagAgentAlive = 1;

  // Command opcodes written to DbgOffCmd by the driver. DbgCmdNone is the zeroed-segment default and
  // is never dispatched (the agent only acts when CmdSeq advances past AckSeq).
  public const long DbgCmdNone = 0;
  public const long DbgCmdSetBp = 1;
  public const long DbgCmdClearBp = 2;
  public const long DbgCmdContinue = 3;

  // Stop reasons written to DbgOffStopReason by the agent. Only "breakpoint" exists in P3b; stepping
  // and async-break reasons join it at P4.
  public const long DbgStopReasonBreakpoint = 1;

  /// The breakpoint table capacity. Held in agent .data (NOT the shared segment — the driver does not
  /// need the saved original bytes, and code bytes should not sit in a same-user-readable segment).
  /// A small fixed table: the driver sets a handful of breakpoints, never hundreds.
  public const int DbgMaxBreakpoints = 16;

  /// The x86 INT3 breakpoint opcode the agent patches into `.text`. The ARM64 counterpart (`BRK #0`)
  /// lives in the ARM64 backend's `__dbg_arm_bp`, since the patch width and encoding differ by ISA.
  public const byte DbgX86BreakpointOpcode = 0xCC;

  /// ARM64 `BRK #0` — the 4-byte trap the agent patches into `.text` on arm64. Stated here so the one
  /// place a reader looks for "the trap instruction" holds both ISAs' encodings.
  public const long DbgArm64BreakpointWord = 0xD4200000;

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

  // Breakpoint-table globals (agent .data). Two parallel arrays indexed by slot:
  //   __dbg_bp_addr[i] — the absolute `.text` address patched (0 = free slot).
  //   __dbg_bp_orig[i] — the original code unit saved there (low byte on x86, low word on arm64),
  //                      so the trap can be removed without re-reading the now-0xCC/BRK byte.
  public const string DbgBpAddrGlobal = "__dbg_bp_addr";
  public const string DbgBpOrigGlobal = "__dbg_bp_orig";

  // Single-step-over state (one breakpoint is stepped over at a time under the stop-the-world MVP):
  //   __dbg_step_addr      — the breakpoint being stepped over (0 = not stepping). x86 re-arms this
  //                          on the follow-up single-step trap; arm64 re-arms it when the temp bp hits.
  //   __dbg_step_temp_addr — arm64 only: the temporary bp planted at pc+4 to single-step over (macOS
  //                          gives userspace no hardware single-step). x86 leaves this 0 (it uses the
  //                          trap flag instead).
  //   __dbg_step_temp_orig — arm64 only: the original word under the temp bp, to restore it.
  public const string DbgStepAddrGlobal = "__dbg_step_addr";
  public const string DbgStepTempAddrGlobal = "__dbg_step_temp_addr";
  public const string DbgStepTempOrigGlobal = "__dbg_step_temp_orig";

  /// <summary>Emit the agent's globals: the mapped-base pointer (0 = dark), the env-var name, the
  /// breakpoint table, and the single-step-over state.</summary>
  public void EmitDebugAgentGlobals() {
    // Base pointer to the mapped control segment. 0 means the agent is dark: nothing mapped, no
    // handler armed. Every `__dbg_*` entry point bails on base == 0, so a dark agent is free.
    _b.DefineGlobal("__dbg_base", 8, 0);
    _b.DefineSymdata("__dbg_env_name",
      System.Text.Encoding.UTF8.GetBytes(DbgActivationEnvVar + "\0"));

    _b.DefineGlobal(DbgBpAddrGlobal, DbgMaxBreakpoints * 8, 0);
    _b.DefineGlobal(DbgBpOrigGlobal, DbgMaxBreakpoints * 8, 0);
    _b.DefineGlobal(DbgStepAddrGlobal, 8, 0);
    _b.DefineGlobal(DbgStepTempAddrGlobal, 8, 0);
    _b.DefineGlobal(DbgStepTempOrigGlobal, 8, 0);
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

    // Entry stop: if the driver asked to control startup (StopAtEntry != 0), PARK here — after the
    // handshake, before user code — so it can set breakpoints and then send continue, gdb-style. A
    // consumer that leaves StopAtEntry zeroed (the P3a attach probe) skips the park and runs the
    // target straight through, so the P3a behavior is preserved byte-for-byte.
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffStopAtEntry);
    _b.JumpIfZero(VReg.Scratch2, doneLabel);
    _b.Call("__dbg_park_loop");

    _b.Jump(doneLabel);

    _b.DefineLabel(disabledLabel);
    // Keep __dbg_base at 0 (agent dark). Nothing was mapped and no handler was armed.
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreGlobal("__dbg_base", VReg.Scratch0);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_shutdown — restore `.text` to pristine (disarm every still-armed breakpoint) and then
  /// tear the control segment down (clear liveness, unmap, zero __dbg_base). Called from _start at
  /// process exit. Disarming first matters: it leaves no stray INT3/BRK in `.text`, and — with the
  /// trap handler's `__dbg_base != 0` guard — closes the P3a-review contract that a fault delivered
  /// after shutdown must not dereference the now-unmapped segment (it can't: base is zero, so the
  /// handler defers to the fault chain).
  /// </summary>
  public void EmitDebugAgentShutdown() {
    // The segment teardown is shared with (structurally identical to) the DebugStream shutdown, so it
    // is emitted via the same helper rather than duplicated — __dbg_shutdown calls it as a tail.
    EmitSharedSegmentShutdown("__dbg_segment_teardown", "__dbg_base", DbgOffFlags,
      DbgControlSegmentSize);

    _b.FunctionStart("__dbg_shutdown", 0, 0x60);

    var teardownLabel = UniqueLabel("dbg_shutdown_teardown");
    var loopLabel = UniqueLabel("dbg_shutdown_loop");
    var nextLabel = UniqueLabel("dbg_shutdown_next");
    var doneLabel = UniqueLabel("dbg_shutdown_done");

    // Dark agent: nothing was mapped, no breakpoints armed.
    _b.LoadGlobal(VReg.Scratch0, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch0, doneLabel);

    // Disarm every active table slot (slot 0 of the frame holds the loop counter across the
    // __dbg_disarm_bp call, which clobbers every scratch register on arm64).
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(0, VReg.Scratch0);

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch0, 0);                       // i
    _b.CmpRegImm(VReg.Scratch0, DbgMaxBreakpoints);
    _b.JumpIf(Condition.AboveEqual, teardownLabel);

    _b.ShlRegImm(VReg.Scratch0, 3);                       // i*8
    _b.LeaGlobal(VReg.Scratch1, DbgBpAddrGlobal);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);           // &bp_addr[i]
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, 0);     // addr
    _b.JumpIfZero(VReg.Scratch2, nextLabel);              // free slot

    _b.LeaGlobal(VReg.Arg2, DbgBpOrigGlobal);
    _b.AddRegReg(VReg.Arg2, VReg.Scratch0);               // &bp_orig[i]
    _b.LoadIndirect(VReg.Arg1, VReg.Arg2, 0);             // orig -> disarm arg1
    _b.MovRegReg(VReg.Arg0, VReg.Scratch2);               // addr -> disarm arg0
    _b.ZeroReg(VReg.Scratch3);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch3);    // bp_addr[i] = 0 (free before the call)
    _b.Call("__dbg_disarm_bp");

    _b.DefineLabel(nextLabel);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(0, VReg.Scratch0);
    _b.Jump(loopLabel);

    _b.DefineLabel(teardownLabel);
    _b.Call("__dbg_segment_teardown");

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // Breakpoint table + command mailbox (platform-neutral)
  // =========================================================================
  //
  // These read the OS trap context / patch `.text` only through two target-emitted primitives —
  // `__dbg_arm_bp(addr) -> orig` and `__dbg_disarm_bp(addr, orig)` — which encapsulate the ISA
  // difference (INT3 vs BRK, VirtualProtect vs mprotect, the icache flush). Everything else (the
  // table, the command loop, the stop-event publish) is written once here.
  //
  // Register discipline: on arm64 EVERY VReg is call-clobbered, so any value needed across a `Call`
  // is spilled to a frame slot and reloaded. Loops that contain no call keep their state in scratch
  // registers.

  /// <summary>
  /// __dbg_bp_slot(matchVal) -> slot index whose __dbg_bp_addr equals matchVal, or -1. Called with an
  /// absolute address to find an existing breakpoint, or with 0 to find a free slot (a real
  /// breakpoint address is never 0). Pure scan, no calls.
  /// </summary>
  private void EmitDbgBpSlot() {
    _b.FunctionStart("__dbg_bp_slot", 1, 0x40);

    var loopLabel = UniqueLabel("dbg_bp_slot_loop");
    var foundLabel = UniqueLabel("dbg_bp_slot_found");
    var missLabel = UniqueLabel("dbg_bp_slot_miss");

    _b.LeaGlobal(VReg.Scratch1, DbgBpAddrGlobal);         // base
    _b.ZeroReg(VReg.Scratch2);                            // i = 0

    _b.DefineLabel(loopLabel);
    _b.CmpRegImm(VReg.Scratch2, DbgMaxBreakpoints);
    _b.JumpIf(Condition.AboveEqual, missLabel);
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.ShlRegImm(VReg.Scratch3, 3);
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch1);           // &bp_addr[i]
    _b.LoadIndirect(VReg.Ret, VReg.Scratch3, 0);          // bp_addr[i]
    _b.CmpRegReg(VReg.Ret, VReg.Arg0);
    _b.JumpIf(Condition.Equal, foundLabel);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.Jump(loopLabel);

    _b.DefineLabel(missLabel);
    _b.MovRegImm(VReg.Ret, -1);
    _b.FunctionEnd();

    _b.DefineLabel(foundLabel);
    _b.MovRegReg(VReg.Ret, VReg.Scratch2);
    _b.FunctionEnd();
  }

  /// <summary>
  /// abs = &amp;mrt_start + (codeOffset in frame slot 0), stored to frame slot 1 (and left in Scratch1).
  /// The one place the driver-supplied code offset is turned into an absolute `.text` address; shared
  /// by set/clear so they cannot drift on which base an offset is relative to.
  /// </summary>
  private void EmitDbgAbsFromOffset() {
    _b.LeaFuncAddr(VReg.Scratch1, "mrt_start");           // text base
    _b.LoadLocal(VReg.Scratch2, 0);                       // codeOffset
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);           // abs
    _b.StoreLocal(1, VReg.Scratch1);                      // slot1 = abs
  }

  /// <summary>
  /// __dbg_set_bp(codeOffset) — arm a breakpoint at &mrt_start + codeOffset. Ignored (but still acked
  /// by the park loop) when the offset is outside `.text`, when one is already set there (re-arming
  /// would save the trap byte itself as the "original"), or when the table is full.
  /// </summary>
  private void EmitDbgSetBp() {
    _b.FunctionStart("__dbg_set_bp", 1, 0x80);

    var doneLabel = UniqueLabel("dbg_set_bp_done");

    // BOUNDS: a driver-supplied offset must never let the patch below write 0xCC/BRK outside `.text`.
    // textsize = &symtable - &mrt_start is the exact bound the panic symbolizer trusts (the symbol
    // table sits immediately past `.text`); an UNSIGNED compare rejects negatives as huge values too.
    _b.LeaSymdata(VReg.Scratch1, _b.SymbolTableLabel);
    _b.LeaFuncAddr(VReg.Scratch2, "mrt_start");
    _b.SubRegReg(VReg.Scratch1, VReg.Scratch2);           // textsize
    _b.LoadLocal(VReg.Scratch2, 0);                       // codeOffset
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, doneLabel);           // outside .text -> ignore

    EmitDbgAbsFromOffset();

    _b.LoadLocal(VReg.Arg0, 1);
    _b.Call("__dbg_bp_slot");                             // already set?
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.GreaterEqual, doneLabel);

    _b.ZeroReg(VReg.Arg0);
    _b.Call("__dbg_bp_slot");                             // find a free slot
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, doneLabel);                 // table full
    _b.StoreLocal(2, VReg.Ret);                           // slot2 = idx

    _b.LoadLocal(VReg.Arg0, 1);                           // abs
    _b.Call("__dbg_arm_bp");                              // orig
    _b.StoreLocal(3, VReg.Ret);                           // slot3 = orig

    _b.LoadLocal(VReg.Scratch2, 2);
    _b.ShlRegImm(VReg.Scratch2, 3);                       // idx*8
    _b.LeaGlobal(VReg.Scratch1, DbgBpAddrGlobal);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch3, 1);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch3);    // bp_addr[idx] = abs
    _b.LeaGlobal(VReg.Scratch1, DbgBpOrigGlobal);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch3, 3);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch3);    // bp_orig[idx] = orig

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_clear_bp(codeOffset) — restore the original byte at &mrt_start + codeOffset and free the
  /// table slot. A no-op if no breakpoint is set there.
  /// </summary>
  private void EmitDbgClearBp() {
    _b.FunctionStart("__dbg_clear_bp", 1, 0x80);

    var doneLabel = UniqueLabel("dbg_clear_bp_done");

    EmitDbgAbsFromOffset();

    _b.LoadLocal(VReg.Arg0, 1);
    _b.Call("__dbg_bp_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, doneLabel);                 // not set
    _b.StoreLocal(2, VReg.Ret);                           // idx

    _b.LoadLocal(VReg.Scratch2, 2);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.LeaGlobal(VReg.Scratch1, DbgBpOrigGlobal);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, 0);     // orig
    _b.StoreLocal(3, VReg.Scratch3);                      // spill across the call

    _b.LoadLocal(VReg.Arg0, 1);                           // abs
    _b.LoadLocal(VReg.Arg1, 3);                           // orig
    _b.Call("__dbg_disarm_bp");

    _b.LoadLocal(VReg.Scratch2, 2);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.LeaGlobal(VReg.Scratch1, DbgBpAddrGlobal);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.ZeroReg(VReg.Scratch3);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch3);    // bp_addr[idx] = 0

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_bp_orig_of_addr(absAddr) -> the saved original code unit for the breakpoint at absAddr, or
  /// 0 if none. Used by the trap dispatch to disarm a breakpoint for single-step-over without a
  /// second copy of the table walk.
  /// </summary>
  private void EmitDbgBpOrigOfAddr() {
    _b.FunctionStart("__dbg_bp_orig_of_addr", 1, 0x40);

    var missLabel = UniqueLabel("dbg_orig_miss");

    _b.LoadLocal(VReg.Arg0, 0);
    _b.Call("__dbg_bp_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, missLabel);

    _b.ShlRegImm(VReg.Ret, 3);                            // idx*8
    _b.LeaGlobal(VReg.Scratch1, DbgBpOrigGlobal);
    _b.AddRegReg(VReg.Scratch1, VReg.Ret);
    _b.LoadIndirect(VReg.Ret, VReg.Scratch1, 0);          // orig
    _b.FunctionEnd();

    _b.DefineLabel(missLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_publish_stop(reason, pcOffset, sp, fp) — fill the stop-event fields and release-bump
  /// StopSeq so the driver, which polls StopSeq, sees a complete event. Bails if the agent detached.
  /// </summary>
  private void EmitDbgPublishStop() {
    _b.FunctionStart("__dbg_publish_stop", 4, 0x40);

    var doneLabel = UniqueLabel("dbg_publish_done");

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);

    _b.StoreIndirect(VReg.Scratch1, DbgOffStopReason, VReg.Arg0);
    _b.StoreIndirect(VReg.Scratch1, DbgOffStopPc, VReg.Arg1);
    _b.StoreIndirect(VReg.Scratch1, DbgOffStopSp, VReg.Arg2);
    _b.StoreIndirect(VReg.Scratch1, DbgOffStopFp, VReg.Arg3);

    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffStopSeq);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.StoreRelease(VReg.Scratch1, DbgOffStopSeq, VReg.Scratch2);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_park_loop() — the stop-the-world pause. Spin on the command doorbell (CmdSeq), dispatching
  /// set-breakpoint / clear-breakpoint (and acking each), until the driver sends continue; yield the
  /// slice between polls so the pause does not peg a core. Reused by the entry stop and the
  /// breakpoint-hit stop. Returns when continue arrives (or the agent detaches).
  /// </summary>
  private void EmitDbgParkLoop() {
    _b.FunctionStart("__dbg_park_loop", 0, 0x40);

    var loopLabel = UniqueLabel("dbg_park_loop");
    var idleLabel = UniqueLabel("dbg_park_idle");
    var setLabel = UniqueLabel("dbg_park_set");
    var clearLabel = UniqueLabel("dbg_park_clear");
    var ackLabel = UniqueLabel("dbg_park_ack");
    var contLabel = UniqueLabel("dbg_park_continue");
    var doneLabel = UniqueLabel("dbg_park_done");

    _b.DefineLabel(loopLabel);
    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // detached
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, DbgOffCmdSeq);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, DbgOffAckSeq);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.JumpIf(Condition.Equal, idleLabel);               // no new command

    _b.StoreLocal(0, VReg.Scratch2);                     // remember the seq we are about to process
    _b.LoadIndirect(VReg.Ret, VReg.Scratch1, DbgOffCmd);
    _b.CmpRegImm(VReg.Ret, DbgCmdContinue);
    _b.JumpIf(Condition.Equal, contLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdSetBp);
    _b.JumpIf(Condition.Equal, setLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdClearBp);
    _b.JumpIf(Condition.Equal, clearLabel);
    _b.Jump(ackLabel);                                   // unknown command: ack and keep waiting

    _b.DefineLabel(setLabel);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, DbgOffCmdArg);
    _b.Call("__dbg_set_bp");
    _b.Jump(ackLabel);

    _b.DefineLabel(clearLabel);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, DbgOffCmdArg);
    _b.Call("__dbg_clear_bp");
    _b.Jump(ackLabel);

    _b.DefineLabel(ackLabel);                            // reload base (a call clobbered it), ack, loop
    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.LoadLocal(VReg.Scratch2, 0);
    _b.StoreRelease(VReg.Scratch1, DbgOffAckSeq, VReg.Scratch2);
    _b.Jump(loopLabel);

    _b.DefineLabel(idleLabel);
    _b.OsYield();
    _b.Jump(loopLabel);

    _b.DefineLabel(contLabel);                           // ack the continue (no call clobbered our regs), return
    _b.StoreRelease(VReg.Scratch1, DbgOffAckSeq, VReg.Scratch2);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_on_breakpoint(bpAbsAddr, sp, fp) -> 1 if bpAbsAddr is a known breakpoint (a stop event was
  /// published and the thread parked until continue), 0 if it is not ours (the target thunk then
  /// defers to the fault chain). The neutral half of the trap dispatch; the platform thunk supplies
  /// the trapping context and applies the single-step-over afterwards.
  /// </summary>
  private void EmitDbgOnBreakpoint() {
    _b.FunctionStart("__dbg_on_breakpoint", 3, 0x60);

    var missLabel = UniqueLabel("dbg_on_bp_miss");

    _b.LoadLocal(VReg.Arg0, 0);                           // bpAbsAddr
    _b.Call("__dbg_bp_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, missLabel);

    _b.LeaFuncAddr(VReg.Scratch1, "mrt_start");
    _b.LoadLocal(VReg.Scratch2, 0);
    _b.SubRegReg(VReg.Scratch2, VReg.Scratch1);          // pcOffset = abs - text base

    _b.MovRegImm(VReg.Arg0, DbgStopReasonBreakpoint);
    _b.MovRegReg(VReg.Arg1, VReg.Scratch2);
    _b.LoadLocal(VReg.Arg2, 1);                           // sp
    _b.LoadLocal(VReg.Arg3, 2);                           // fp
    _b.Call("__dbg_publish_stop");

    _b.Call("__dbg_park_loop");

    _b.MovRegImm(VReg.Ret, 1);
    _b.FunctionEnd();

    _b.DefineLabel(missLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  /// <summary>
  /// Emit the always-present debug agent. Called UNCONDITIONALLY (unless --no-debug-agent), unlike
  /// the DebugStream family, because the agent is dark-by-default rather than opt-in — its only
  /// startup cost when MAXON_DEBUG is unset is a single env-var read. The target-specific trap-handler
  /// thunk (`__dbg_trap_handler_thunk`) and the two `.text`-patch primitives (`__dbg_arm_bp` /
  /// `__dbg_disarm_bp`) are emitted alongside the fault thunk by each backend.
  /// </summary>
  public void EmitDebugAgentFunctions() {
    EmitDebugAgentGlobals();
    EmitDbgBpSlot();
    EmitDbgSetBp();
    EmitDbgClearBp();
    EmitDbgBpOrigOfAddr();
    EmitDbgPublishStop();
    EmitDbgParkLoop();
    EmitDbgOnBreakpoint();
    EmitDebugAgentInit();
    EmitDebugAgentShutdown();
  }
}
