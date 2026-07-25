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
  ///
  /// v3 (P3c) adds the <see cref="DbgCmdBacktrace"/> command and the frame-count / frame-array fields
  /// it fills (<see cref="DbgOffBtCount"/> / <see cref="DbgOffBtFrames"/>) — previously-zero segment
  /// bytes, so no existing field's meaning changes. The version matters here for HONESTY, not layout: a
  /// v2 agent (a binary built before P3c) has no backtrace handler and its park loop would ACK the
  /// unknown command without filling the array, so the driver gates <c>backtrace</c> on version ≥ 3 and
  /// reports "not supported by this binary" rather than showing an empty trace it would read as real.
  ///
  /// v4 (P4a) adds the <see cref="DbgCmdReadMem"/> command and the read-length / status / result-buffer
  /// fields it fills (<see cref="DbgOffReadLen"/> / <see cref="DbgOffReadStatus"/> /
  /// <see cref="DbgOffReadBuf"/>) — again previously-zero bytes after the backtrace array, so no earlier
  /// field moves. Same honesty gate as v3: a v3 agent would ACK the unknown read command without filling
  /// the buffer, so the driver gates value inspection on version ≥ 4 and reports "needs a rebuilt agent"
  /// rather than reading zeroed garbage as if it were the debuggee's memory.
  ///
  /// v5 (P4b) adds the <see cref="DbgCmdStep"/> command (single-step one debuggee instruction, then
  /// publish a <see cref="DbgStopReasonStep"/> stop and re-park) and the <see cref="DbgOffTextBase"/>
  /// field <c>__dbg_init</c> now fills (the absolute `.text` base, so the driver can turn a return
  /// address it reads off the stack into a code offset). <see cref="DbgOffTextBase"/> is a
  /// previously-zero byte after the read buffer, so no earlier field moves. Same honesty gate: a v4 agent
  /// would ACK the unknown step command without single-stepping, so the driver gates the four step
  /// commands (step/next/finish/until) on version ≥ 5 and reports "needs a rebuilt agent" rather than
  /// hanging on a step that never publishes.
  ///
  /// v6 (P4d-1) adds CONDITIONAL breakpoints: the <see cref="DbgCmdSetBpCond"/> command, the
  /// <see cref="DbgOffCondStage"/> staging area the driver writes the condition record into, and the
  /// agent-private <see cref="DbgBpCondGlobal"/> table it is copied to (indexed by the SAME slot
  /// <see cref="EmitDbgBpSlot"/> assigns, so there is one address→slot mapping in the system).
  /// <see cref="DbgOffCondStage"/> sits after <see cref="DbgOffTextBase"/>, so no earlier field moves.
  /// Same honesty gate as v3/v4/v5, and here it MATTERS MORE than elsewhere: a v5 agent would ack the
  /// unknown set-condition command and then stop unconditionally, which is not a missing feature but a
  /// WRONG ANSWER (a breakpoint that fires when the user said it should not). So the driver refuses to
  /// arm a conditional breakpoint below version 6 rather than silently downgrading it.
  ///
  /// v7 adds <see cref="DbgOffCmdResult"/>, the word a command ANSWERS with — an ack only ever said "I
  /// processed this", never "I could do it", so a `break` the agent silently dropped (a full table, an
  /// offset outside `.text`) was reported to the user as set. It is a schema addition, so it takes a
  /// version of its own rather than widening v6 in place: an agent that announces v6 does not write the
  /// word, its zero reads as <see cref="DbgCmdResultRefused"/>, and a driver that believed it would call
  /// every command on that binary refused. Gated by <see cref="DbgCmdResultMinVersion"/> exactly like
  /// every capability above.
  public const long DbgControlVersion = 7;

  /// The control-segment version at which the agent first understood <see cref="DbgCmdBacktrace"/>.
  /// The driver refuses to trust the frame array from an older agent (which would ack-and-ignore).
  public const long DbgBacktraceMinVersion = 3;

  /// The control-segment version at which the agent first understood <see cref="DbgCmdReadMem"/>. The
  /// driver refuses to read the result buffer from an older agent (which would ack-and-ignore, leaving
  /// the buffer whatever it was) — the same UnsupportedByAgent discipline the backtrace gate uses.
  public const long DbgReadMemMinVersion = 4;

  /// The control-segment version at which the agent first understood <see cref="DbgCmdStep"/> and filled
  /// <see cref="DbgOffTextBase"/>. The driver refuses source-line stepping against an older agent (which
  /// would ack-and-ignore the step, publishing no stop) — the same UnsupportedByAgent discipline.
  public const long DbgStepMinVersion = 5;

  /// The control-segment version at which the agent first understood <see cref="DbgCmdSetBpCond"/> and
  /// evaluates a condition before publishing a breakpoint stop. The driver REFUSES `break … if` below
  /// this — it never arms the breakpoint unconditionally instead, because an older agent would ack the
  /// unknown command and then stop on every hit, which is a wrong answer rather than a missing feature.
  public const long DbgCondBpMinVersion = 6;

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

  // Backtrace-result channel (agent writes, driver reads) — v3:
  //   In response to DbgCmdBacktrace the agent walks the STOPPED frame's saved-rbp chain (reusing the
  //   mrt_fault_backtrace discipline) and writes a bounded array of `.text` code offsets — frame 0 is
  //   the exact stop PC offset, frames 1..N are return-address offsets (the driver applies the −1
  //   call-site bias when it symbolizes those). DbgOffBtCount holds how many were written. The park
  //   loop's ack store-release publishes both, so a driver that has seen the ack sees a complete array.
  public const int DbgOffBtCount = 0x68;
  public const int DbgOffBtFrames = 0x70;

  // Read-memory channel (driver writes ReadLen with the addr in DbgOffCmdArg; agent writes Status + Buf)
  // — v4. Placed after the backtrace frame array, which ends at DbgOffBtFrames + DbgMaxBacktraceFrames*8
  // = 0x70 + 64*8 = 0x270, so these bytes were previously zero. In response to DbgCmdReadMem the agent
  // copies ReadLen (clamped to DbgReadBufCap) bytes of the debuggee's own memory at DbgOffCmdArg into
  // DbgOffReadBuf and stores 1 into DbgOffReadStatus. The park loop's ack store-release publishes the
  // buffer, so a driver that has seen the ack sees the copied bytes.
  public const int DbgOffReadLen = 0x270;     // i64 — bytes the agent should copy (second command arg)
  public const int DbgOffReadStatus = 0x278;  // i64 — 1 = the agent performed the copy
  public const int DbgOffReadBuf = 0x280;     // result buffer, DbgReadBufCap bytes (0x280..0x480)

  // Text-base field (agent writes once at init, driver reads once) — v5. Placed at the first free offset
  // after the read buffer (DbgOffReadBuf + DbgReadBufCap = 0x480), so it was previously zero. __dbg_init
  // stores the ABSOLUTE `.text` base (&mrt_start) here; the driver subtracts it from an absolute return
  // address it reads off the debuggee's stack (via DbgCmdReadMem) to get a code OFFSET — the base the
  // sidecar resolves against, ASLR-independent. Dark-when-unset is unaffected (one store, only when
  // attached). 0x480 + 8 = 0x488 &lt; 4096, so it stays inside the one control page.
  public const int DbgOffTextBase = 0x480;    // i64 — absolute &mrt_start (the `.text` load address)

  // Condition staging area (driver writes, agent reads) — v6. Placed at the first free offset after
  // DbgOffTextBase (0x480 + 8), so it was previously zero. The driver writes ONE DbgCondRecSize-byte
  // condition record here and posts DbgCmdSetBpCond with DbgOffCmdArg = the breakpoint's CODE OFFSET;
  // the agent resolves that offset to a table slot through the SAME __dbg_bp_slot every other command
  // uses and copies the staged record into __dbg_bp_cond[slot]. Staging (rather than a per-slot array in
  // the shared segment) keeps the driver from needing to know which slot the agent picked — the slot
  // assignment stays the agent's single secret. 0x488 + 48 = 0x4B8 &lt; 4096, so it stays inside the page.
  public const int DbgOffCondStage = 0x488;

  // Command-outcome word (agent writes, driver reads) — v7. Placed at the first free offset after the
  // condition staging area (0x488 + 48), so it was previously zero. 0x4B8 + 8 = 0x4C0 &lt; 4096.
  //
  // It exists because an ACK only ever said "I processed your command", never "I could DO it", and the
  // driver read the ack as success: __dbg_set_bp silently ignores an offset outside `.text` AND a full
  // 16-slot table, so a 17th `break` was reported to the user as "Breakpoint set" and then never fired
  // (measured: 17 breakpoints armed, the 17th never hit). __dbg_set_bp_cond has the same shape — it is
  // a no-op when no breakpoint is armed at that offset, which is exactly what a dropped arm leaves.
  //
  // Written by the park loop BEFORE the ack's store-release, so a driver that has seen the ack sees the
  // outcome. The loop publishes Ok before dispatching, and a handler that could not do what it was asked
  // overwrites it — the commands that cannot fail (continue / step / backtrace / read) therefore need no
  // line of their own, and a NEW command that CAN fail says so where it fails.
  public const int DbgOffCmdResult = 0x4B8;
  public const long DbgCmdResultOk = 1;
  public const long DbgCmdResultRefused = 0;

  /// The control-segment version at which the agent first ANSWERS a command. Below it the word is the
  /// zeroed segment's 0, which is indistinguishable from a refusal, so the driver falls back to the old
  /// weaker contract there (an ack means the command was processed) rather than calling every command on
  /// an older binary refused.
  public const long DbgCmdResultMinVersion = 7;

  /// The width of every word in a condition record. The record is six i64s so the agent can read each
  /// with the neutral 8-byte <see cref="IEmitterBackend.LoadIndirect"/> and the driver can write each
  /// with one accessor write — stated once, and the record's SIZE is derived from it below.
  public const int DbgCondWordSize = 8;

  // Condition-record layout (offsets within one __dbg_bp_cond entry AND within DbgOffCondStage — the
  // staged record IS the stored record, copied verbatim, so there is exactly one layout to agree on).
  public const int DbgCondOffKind = 0 * DbgCondWordSize;    // DbgCondKind* — what the rest of the record means
  public const int DbgCondOffOp = 1 * DbgCondWordSize;      // DbgCondOp* — the relational operator
  public const int DbgCondOffImm = 2 * DbgCondWordSize;     // the right-hand literal, sign-extended to 64 bits
  public const int DbgCondOffSlot = 3 * DbgCondWordSize;    // SIGNED frame-pointer-relative offset: addr = fp + slot
  public const int DbgCondOffWidth = 4 * DbgCondWordSize;   // bytes to load at that address: DbgCondOperandWidths
  public const int DbgCondOffSigned = 5 * DbgCondWordSize;  // 1 = sign-extend the loaded value, 0 = zero-extend

  /// One condition record: the six words above. DERIVED from the field count and the word size rather
  /// than written down again, so the stride the agent strides by cannot drift from the fields it holds.
  public const int DbgCondRecSize = 6 * DbgCondWordSize;

  // What a condition record's Kind word means. ANY other value is treated as Unconditional (see
  // __dbg_cond_holds): stopping too often is visible and harmless, whereas silently SKIPPING a stop on a
  // record the agent did not understand would be a wrong answer the user cannot see.
  public const long DbgCondKindUnconditional = 0;
  public const long DbgCondKindScalarCompare = 1;

  /// <summary>
  /// The operand widths <see cref="EmitDbgCondHolds"/> can load, in bytes. The agent emits ONE dispatch
  /// arm per entry and the driver refuses an operand of any other width, both by reading THIS array — so
  /// the closed set is stated once. Two independent lists would fail in the dangerous direction: a width
  /// the driver accepted and the agent did not recognise reaches the agent's unrecognised-width arm,
  /// which STOPS — i.e. a `break … if` that fires on every hit, the wrong answer conditions exist to
  /// remove. The full word must be present (it is the no-extension arm) and is deliberately last.
  /// </summary>
  public static readonly int[] DbgCondOperandWidths = [1, 2, 4, DbgCondWordSize];

  /// <summary>
  /// One relational operator of a scalar-compare condition: the wire code in the record, the surface
  /// spelling `break … if` accepts, and the branch the agent emits for it.
  ///
  /// All three live in ONE row for the same reason as the widths above, and the failure was the same
  /// shape: the grammar's operator table and the evaluator's dispatch each enumerated this set, and an
  /// operator added to the grammar but not the evaluator would reach the agent's unrecognised-operator
  /// arm — which STOPS, turning a condition into a breakpoint that fires unconditionally.
  ///
  /// The comparison is always performed SIGNED by the agent; the driver guarantees that is correct by
  /// refusing any operand whose 64-bit normalized value could exceed the signed range (see
  /// <c>MaxonDebugger.TryScalarOperandShape</c>).
  /// </summary>
  public readonly record struct DbgCondOperator(long Code, string Text, Condition Branch);

  /// The whole operator vocabulary. Row ORDER carries no meaning: the parser matches longest-text-first
  /// by DERIVING that order (else `&lt;` would swallow the head of `&lt;=` depending on how this reads).
  public static readonly DbgCondOperator[] DbgCondOperators = [
    new(1, "==", Condition.Equal),
    new(2, "!=", Condition.NotEqual),
    new(3, "<", Condition.Less),
    new(4, "<=", Condition.LessEqual),
    new(5, ">", Condition.Greater),
    new(6, ">=", Condition.GreaterEqual),
  ];

  /// The read-memory result-buffer capacity: the most bytes one DbgCmdReadMem copies. The driver chunks
  /// a larger read into ≤ this many bytes per command; the agent clamps ReadLen to it so a driver bug
  /// can never make the copy run past DbgOffReadBuf. 512 keeps DbgOffReadBuf..+512 = 0x280..0x480 well
  /// inside the one control page (0x480 &lt; 4096) — one number both ends step by.
  public const int DbgReadBufCap = 512;

  /// flags bit 0: the in-process agent has mapped the segment and armed its trap handler. Released
  /// with a store-release AFTER the magic/version, so a consumer that sees this bit also sees them.
  public const long DbgFlagAgentAlive = 1;

  // Command opcodes written to DbgOffCmd by the driver. DbgCmdNone is the zeroed-segment default and
  // is never dispatched (the agent only acts when CmdSeq advances past AckSeq).
  public const long DbgCmdNone = 0;
  public const long DbgCmdSetBp = 1;
  public const long DbgCmdClearBp = 2;
  public const long DbgCmdContinue = 3;
  public const long DbgCmdBacktrace = 4;
  public const long DbgCmdReadMem = 5;
  public const long DbgCmdStep = 6;
  public const long DbgCmdSetBpCond = 7;

  // Stop reasons written to DbgOffStopReason by the agent. "breakpoint" (P3b) and "step" (P4b, published
  // after a DbgCmdStep single-step completes) — kept DISTINCT so the driver's step loop can tell a step
  // stop from a breakpoint it happened to land on, and each renders with its own reason text.
  public const long DbgStopReasonBreakpoint = 1;
  public const long DbgStopReasonStep = 2;

  /// The frame-array capacity (DbgOffBtFrames holds this many i64 code offsets). ONE number both ends
  /// step by — the agent stops walking at it, the driver reads no more than it — so they cannot
  /// disagree on the array's length. Its real bound is the control PAGE, not the fault backtrace's
  /// walk cap (deliberately NOT coupled to <see cref="GtLayout.MaxBacktraceFrames"/>: raising that
  /// anti-spin cap must not silently overflow this page). 64 is generous for a debug trace and fits
  /// with room to spare: 0x70 + 64*8 = 0x270 &lt; 4096.
  public const int DbgMaxBacktraceFrames = 64;

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
  //   __dbg_bp_cond[i] — the DbgCondRecSize-byte condition record for that breakpoint (Kind 0 = fires
  //                      unconditionally). A THIRD array over the SAME slot index, deliberately: the
  //                      one address→slot mapping in the system is __dbg_bp_slot, and adding a second
  //                      offset→record lookup beside it is exactly the duplicated-fact bug this project
  //                      keeps closing. Agent .data, not the shared segment — the driver stages a record
  //                      and never reads the table back, and a debuggee's own frame layout should not
  //                      sit in a same-user-readable segment any more than its saved code bytes do.
  public const string DbgBpAddrGlobal = "__dbg_bp_addr";
  public const string DbgBpOrigGlobal = "__dbg_bp_orig";
  public const string DbgBpCondGlobal = "__dbg_bp_cond";

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

  // Single-step DISPOSITION (agent .data). Both the step-over-a-breakpoint machinery (continue past a
  // bp) and a user source-step arm the SAME hardware single-step (x86 EFLAGS.TF; arm64 a temp bp at
  // pc+4); this flag is what the trap handler reads to decide what to do once that step completes. It is
  // a TRI-STATE, not the two-value flag the P4b brief sketched, because user-stepping broke the old
  // ownership signal: the step-over path could rely on `__dbg_step_addr != 0` to mean "this single-step
  // is ours", but a user step from a NON-breakpoint location legitimately has step_addr == 0, so a
  // distinct "none" state is needed to still reject a stray single-step (x86) / decide the disposition
  // (arm64). The park loop sets it from the releasing command; the trap handler consumes it and resets
  // to None.
  public const string DbgStepModeGlobal = "__dbg_step_mode";
  public const long DbgStepModeNone = 0;    // no single-step armed by us: a stray step defers to the fault chain
  public const long DbgStepModeOverBp = 1;  // continue past a breakpoint: resume silently once the step completes
  public const long DbgStepModeUser = 2;    // a user source-step: publish a step stop and re-park once it completes

  /// <summary>Emit the agent's globals: the mapped-base pointer (0 = dark), the env-var name, the
  /// breakpoint table (address / original byte / condition), and the single-step-over state.</summary>
  public void EmitDebugAgentGlobals() {
    // Base pointer to the mapped control segment. 0 means the agent is dark: nothing mapped, no
    // handler armed. Every `__dbg_*` entry point bails on base == 0, so a dark agent is free.
    _b.DefineGlobal("__dbg_base", 8, 0);
    _b.DefineSymdata("__dbg_env_name",
      System.Text.Encoding.UTF8.GetBytes(DbgActivationEnvVar + "\0"));

    _b.DefineGlobal(DbgBpAddrGlobal, DbgMaxBreakpoints * 8, 0);
    _b.DefineGlobal(DbgBpOrigGlobal, DbgMaxBreakpoints * 8, 0);
    // 0 = DbgCondKindUnconditional in every record: before any driver stages one, every breakpoint fires
    // on every hit, which is the pre-P4d-1 behavior every existing golden still asserts.
    _b.DefineGlobal(DbgBpCondGlobal, DbgMaxBreakpoints * DbgCondRecSize, 0);
    _b.DefineGlobal(DbgStepAddrGlobal, 8, 0);
    _b.DefineGlobal(DbgStepTempAddrGlobal, 8, 0);
    _b.DefineGlobal(DbgStepTempOrigGlobal, 8, 0);
    _b.DefineGlobal(DbgStepModeGlobal, 8, 0);   // 0 = DbgStepModeNone: no user/over-bp single-step armed
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

    // Publish the absolute `.text` base (&mrt_start) so the driver can turn an absolute return address it
    // reads off the stack into a code offset (needed by step-over/finish). One store, only when attached.
    _b.LeaFuncAddr(VReg.Scratch2, "mrt_start");
    _b.StoreIndirect(VReg.Scratch1, DbgOffTextBase, VReg.Scratch2);

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
  /// __dbg_set_bp(codeOffset) — arm a breakpoint at &mrt_start + codeOffset. REFUSED
  /// (<see cref="DbgCmdResultRefused"/>, but still acked by the park loop) when the offset is outside
  /// `.text` or when the table is full — both used to be SILENT, and an ack the driver read as success
  /// is how a 17th `break` was reported "set" and then never fired. Re-arming an address that already
  /// carries one leaves the PATCH alone (re-saving would store the trap byte itself as the "original")
  /// but still resets its CONDITION — see the re-arm path below.
  ///
  /// Its postcondition is therefore uniform, which is what the driver relies on: after a SUCCESSFUL
  /// __dbg_set_bp, a breakpoint at that address is armed and UNCONDITIONAL. Only
  /// <see cref="EmitDbgSetBpCond"/> makes one conditional, so "is this breakpoint conditional" has
  /// exactly one writer and no path that leaves it stale.
  /// </summary>
  private void EmitDbgSetBp() {
    _b.FunctionStart("__dbg_set_bp", 1, 0x80);

    var doneLabel = UniqueLabel("dbg_set_bp_done");
    var refusedLabel = UniqueLabel("dbg_set_bp_refused");
    var reArmLabel = UniqueLabel("dbg_set_bp_rearm");

    // BOUNDS: a driver-supplied offset must never let the patch below write 0xCC/BRK outside `.text`.
    // textsize = &symtable - &mrt_start is the exact bound the panic symbolizer trusts (the symbol
    // table sits immediately past `.text`); an UNSIGNED compare rejects negatives as huge values too.
    _b.LeaSymdata(VReg.Scratch1, _b.SymbolTableLabel);
    _b.LeaFuncAddr(VReg.Scratch2, "mrt_start");
    _b.SubRegReg(VReg.Scratch1, VReg.Scratch2);           // textsize
    _b.LoadLocal(VReg.Scratch2, 0);                       // codeOffset
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, refusedLabel);        // outside .text

    EmitDbgAbsFromOffset();

    _b.LoadLocal(VReg.Arg0, 1);
    _b.Call("__dbg_bp_slot");                             // already set?
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.GreaterEqual, reArmLabel);

    _b.ZeroReg(VReg.Arg0);
    _b.Call("__dbg_bp_slot");                             // find a free slot
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, refusedLabel);              // table full
    _b.StoreLocal(2, VReg.Ret);                           // slot2 = idx

    // Slots are recycled, so a freshly allocated one must not inherit the condition of whatever
    // breakpoint used to live here — an unconditional `break` would otherwise silently acquire a dead
    // condition and stop at the wrong times. Zeroed BEFORE the patch, so the slot is never briefly armed
    // with a stale condition attached.
    _b.LoadLocal(VReg.Arg0, 2);
    _b.Call("__dbg_cond_zero");

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
    _b.Jump(doneLabel);

    // Re-arm of an address that is ALREADY patched. The trap byte is in place and bp_orig holds the real
    // one, so the patch must not be redone — but the slot's CONDITION must still be reset, because a bare
    // `break` here means "stop every time". Without this, `break f:9 if i == 4` followed by `break f:9`
    // leaves the dead condition attached while the driver reports an unconditional breakpoint: the run
    // skips hits the user was told it would take, with nothing on screen to say why. Ret still holds the
    // existing slot index from the lookup above (the compare that branched here does not clobber it).
    _b.DefineLabel(reArmLabel);
    _b.MovRegReg(VReg.Arg0, VReg.Ret);
    _b.Call("__dbg_cond_zero");
    _b.Jump(doneLabel);

    _b.DefineLabel(refusedLabel);
    EmitDbgStoreCmdResult(DbgCmdResultRefused);

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

    // The freed slot's condition dies with it — the other half of the recycled-slot guard in
    // __dbg_set_bp. Both ends zero it, so a stale condition cannot outlive the breakpoint that set it
    // however the slot is reused.
    _b.LoadLocal(VReg.Arg0, 2);
    _b.Call("__dbg_cond_zero");

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

  // =========================================================================
  // Conditional breakpoints (P4d-1) — the condition table and its evaluator
  // =========================================================================

  /// <summary>
  /// <paramref name="dest"/> = &amp;__dbg_bp_cond[<paramref name="slotIdx"/>], clobbering
  /// <paramref name="scratch"/>. The one place the condition table is indexed, so the three users
  /// (zero / evaluate / store) cannot disagree about its stride.
  ///
  /// <see cref="DbgCondRecSize"/> is not a power of two and the neutral backend has no
  /// multiply-by-immediate, so the stride is emitted as a sum of shifts — DERIVED from the constant's
  /// own set bits rather than from a hand-written pair of shift amounts, which would be a second copy of
  /// the record size that a later field addition could silently leave behind.
  /// </summary>
  private void EmitDbgCondRecAddr(VReg dest, VReg slotIdx, VReg scratch) {
    _b.ZeroReg(dest);
    for (int bit = 63; bit >= 0; bit--) {
      if ((DbgCondRecSize & (1L << bit)) == 0) continue;
      _b.MovRegReg(scratch, slotIdx);
      _b.ShlRegImm(scratch, bit);
      _b.AddRegReg(dest, scratch);
    }
    _b.LeaGlobal(scratch, DbgBpCondGlobal);
    _b.AddRegReg(dest, scratch);
  }

  /// <summary>
  /// <paramref name="dest"/> = the little-endian unsigned integer of <paramref name="byteCount"/> bytes
  /// at [<paramref name="addr"/>], clobbering <paramref name="scratch"/>.
  ///
  /// <see cref="IEmitterBackend"/> exposes an 8-byte <see cref="IEmitterBackend.LoadIndirect"/> and a
  /// 1-byte <see cref="IEmitterBackend.LoadIndirectByte"/> and nothing between, so a 2- or 4-byte operand
  /// is assembled from single bytes — the same reason and the same idiom as
  /// <see cref="EmitDbgReadMem"/>'s copy loop. Adding a sized load to the neutral interface would be a
  /// cross-target change (an arm64 twin, a third caller-visible width rule) to save a handful of
  /// instructions in a path that runs once per breakpoint hit, so the bytes are assembled instead.
  /// Unrolled, because <paramref name="byteCount"/> is a compile-time constant per width arm: every
  /// shift amount is the constant 8, so no variable-shift primitive is needed either.
  /// </summary>
  private void EmitDbgLoadUnsignedBytes(VReg dest, VReg addr, VReg scratch, int byteCount) {
    _b.ZeroReg(dest);
    for (int i = byteCount - 1; i >= 0; i--) {
      _b.ShlRegImm(dest, 8);                            // no-op on the first (dest is still 0)
      _b.LoadIndirectByte(scratch, addr, i);
      _b.OrRegReg(dest, scratch);
    }
  }

  /// <summary>
  /// __dbg_cond_zero(slotIdx) — reset a breakpoint slot's condition record to "fires unconditionally".
  ///
  /// The stale-record trap this closes: slots are RECYCLED. A conditional breakpoint that is cleared and
  /// a plain one later armed would land on the same index, and without this the plain breakpoint would
  /// silently inherit the dead condition and stop at the wrong times — a wrong answer with no symptom at
  /// the surface. So both ends of a slot's life zero it: <see cref="EmitDbgSetBp"/> when it allocates one
  /// and <see cref="EmitDbgClearBp"/> when it frees one. Zeroing the WHOLE record (not just Kind) keeps
  /// the invariant a reader can check by eye: a free slot's record is all zeroes.
  /// </summary>
  private void EmitDbgCondZero() {
    _b.FunctionStart("__dbg_cond_zero", 1, 0x40);

    _b.LoadLocal(VReg.Arg0, 0);
    EmitDbgCondRecAddr(VReg.Scratch1, VReg.Arg0, VReg.Scratch2);

    _b.ZeroReg(VReg.Scratch3);
    for (int off = 0; off < DbgCondRecSize; off += DbgCondWordSize)
      _b.StoreIndirect(VReg.Scratch1, off, VReg.Scratch3);

    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_set_bp_cond(codeOffset) — copy the driver's staged condition record
  /// (<see cref="DbgOffCondStage"/>) into the condition table entry for the breakpoint armed at
  /// &amp;mrt_start + codeOffset. REFUSED (<see cref="DbgCmdResultRefused"/>, still acked by the park
  /// loop) when no breakpoint is armed there: there is nothing to attach a condition to, and the driver
  /// must not report a conditional breakpoint it does not have.
  ///
  /// The offset→slot step goes through <see cref="EmitDbgBpSlot"/> — the SAME lookup set/clear/hit all
  /// use — so a condition can never end up attached to a different slot than the breakpoint it names.
  /// </summary>
  private void EmitDbgSetBpCond() {
    _b.FunctionStart("__dbg_set_bp_cond", 1, 0x80);

    var doneLabel = UniqueLabel("dbg_set_bp_cond_done");
    var refusedLabel = UniqueLabel("dbg_set_bp_cond_refused");

    // Frame slots: 0=codeOffset (param) 1=abs (EmitDbgAbsFromOffset) 2=slotIdx 3=dst 4=src.
    const int slotAbs = 1;
    const int slotIdx = 2;
    const int slotDst = 3;
    const int slotSrc = 4;

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // detached: no segment to read or answer into

    EmitDbgAbsFromOffset();                              // slot1 = &mrt_start + codeOffset

    _b.LoadLocal(VReg.Arg0, slotAbs);
    _b.Call("__dbg_bp_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, refusedLabel);             // no breakpoint armed there
    _b.StoreLocal(slotIdx, VReg.Ret);

    _b.LoadLocal(VReg.Arg0, slotIdx);
    EmitDbgCondRecAddr(VReg.Scratch1, VReg.Arg0, VReg.Scratch2);
    _b.StoreLocal(slotDst, VReg.Scratch1);

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.AddRegImm(VReg.Scratch1, DbgOffCondStage);
    _b.StoreLocal(slotSrc, VReg.Scratch1);

    // Word-for-word copy: the staged record and the stored record share ONE layout, so this needs no
    // per-field knowledge and cannot mis-map a field if one is added.
    for (int off = 0; off < DbgCondRecSize; off += DbgCondWordSize) {
      _b.LoadLocal(VReg.Scratch1, slotSrc);
      _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, off);
      _b.LoadLocal(VReg.Scratch1, slotDst);
      _b.StoreIndirect(VReg.Scratch1, off, VReg.Scratch2);
    }
    _b.Jump(doneLabel);

    _b.DefineLabel(refusedLabel);
    EmitDbgStoreCmdResult(DbgCmdResultRefused);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// Publish the outcome of the command currently being processed into
  /// <see cref="DbgOffCmdResult"/> — the answer the park loop's ack store-release then makes visible.
  /// Clobbers Scratch1/Scratch2, so it is only ever emitted where neither is live.
  ///
  /// Guarded on a live base rather than assuming one: every caller runs under the park loop, where the
  /// base is non-zero, but a store through a detached (unmapped) segment would be a fault rather than a
  /// missing answer — and a detached agent never acks, so the driver already reads that as a refusal.
  /// </summary>
  private void EmitDbgStoreCmdResult(long result) {
    var skipLabel = UniqueLabel("dbg_cmd_result_skip");

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, skipLabel);
    _b.MovRegImm(VReg.Scratch2, result);
    _b.StoreIndirect(VReg.Scratch1, DbgOffCmdResult, VReg.Scratch2);

    _b.DefineLabel(skipLabel);
  }

  /// <summary>
  /// Emit one width arm of <see cref="EmitDbgCondHolds"/>: load <paramref name="byteCount"/> little-endian
  /// bytes of the operand, extend them to 64 bits per the record's Signed word, leave the result in frame
  /// slot <paramref name="slotValue"/>, and jump to <paramref name="doneLabel"/>.
  ///
  /// Sign extension is `value -= 2^bits when value >= 2^(bits-1)` — a compare and a subtract, so it needs
  /// no variable shift. Both constants are DERIVED from <paramref name="byteCount"/>, so a width arm
  /// cannot disagree with the width it handles. The compare is UNSIGNED because the just-assembled value
  /// is a zero-extended byte string, always in [0, 2^bits) and so never negative here.
  ///
  /// Both constants are MATERIALIZED INTO A REGISTER rather than passed to
  /// <see cref="IEmitterBackend.CmpRegImm"/>, and that is load-bearing, not stylistic: the x86 backend's
  /// compare-immediate encodes at most a sign-extended imm32 and SILENTLY TRUNCATES anything wider, so a
  /// 4-byte operand's sign bit (0x8000_0000) would be compared as 0xFFFF_FFFF_8000_0000 and every
  /// negative 4-byte value would extend the wrong way — on x86 only, since the arm64 backend materializes
  /// out-of-range immediates itself. Register-materializing here is correct on both and needs no change
  /// to a shared backend.
  /// </summary>
  private void EmitDbgCondWidthArm(int byteCount, int slotRec, int slotAddr, int slotValue,
      string armLabel, string doneLabel) {
    // The full-width case has no bits above it to extend into and would shift by 64; it is handled
    // directly by its own arm, so reaching here with it is an emitter bug, not a runtime condition.
    if (byteCount >= DbgCondWordSize)
      throw new InvalidOperationException(
        $"{nameof(EmitDbgCondWidthArm)} is for NARROW operands; {byteCount} bytes needs no sign extension");

    var storeLabel = UniqueLabel(armLabel + "_store");

    _b.DefineLabel(armLabel);
    _b.LoadLocal(VReg.Scratch0, slotAddr);
    EmitDbgLoadUnsignedBytes(VReg.Scratch1, VReg.Scratch0, VReg.Scratch2, byteCount);

    _b.LoadLocal(VReg.Scratch2, slotRec);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch2, DbgCondOffSigned);
    _b.JumpIfZero(VReg.Scratch3, storeLabel);            // zero-extend: the assembled value is final

    int bits = byteCount * 8;
    _b.MovRegImm(VReg.Scratch2, 1L << (bits - 1));       // this width's sign bit
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Below, storeLabel);              // sign bit clear: already the right value
    _b.MovRegImm(VReg.Scratch3, 1L << bits);             // 2^bits: the wrap this width's negatives sit above
    _b.SubRegReg(VReg.Scratch1, VReg.Scratch3);

    _b.DefineLabel(storeLabel);
    _b.StoreLocal(slotValue, VReg.Scratch1);
    _b.Jump(doneLabel);
  }

  /// <summary>
  /// Emit one operator arm of <see cref="EmitDbgCondHolds"/>: compare the loaded operand against the
  /// record's immediate and branch to <paramref name="trueLabel"/> / <paramref name="falseLabel"/>.
  ///
  /// The comparison is dispatched on the operator BEFORE the compare executes, because reading the
  /// operator word would itself clobber the flags the compare sets.
  /// </summary>
  private void EmitDbgCondCompareArm(string armLabel, Condition cond, int slotRec, int slotValue,
      string trueLabel, string falseLabel) {
    _b.DefineLabel(armLabel);
    _b.LoadLocal(VReg.Scratch1, slotRec);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgCondOffImm);
    _b.LoadLocal(VReg.Scratch1, slotValue);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(cond, trueLabel);
    _b.Jump(falseLabel);
  }

  /// <summary>
  /// __dbg_cond_holds(slotIdx, fp) -> 1 if the breakpoint in <c>slotIdx</c> should STOP, 0 if this hit
  /// should be skipped. Called from <see cref="EmitDbgOnBreakpoint"/> between the slot lookup and the
  /// stop publish, so a false condition costs one table read and never reaches the driver at all — the
  /// in-process evaluation docs/DEBUGGER_DESIGN.md specifies, rather than a stop-and-ask round trip per
  /// loop iteration.
  ///
  /// <c>fp</c> is the trapping context's frame pointer, the SAME value <c>__dbg_publish_stop</c> reports
  /// and the driver's value renderer resolves <c>fp + slot</c> against — so a condition reads a local at
  /// exactly the address <c>print</c> would read it at, and the two can never disagree about where a
  /// local lives.
  ///
  /// EVERY unhandled shape returns 1 (STOP): an unrecognized Kind, an unrecognized width, an
  /// unrecognized operator. That direction is deliberate and is the only safe one — a spurious stop is
  /// visible to the user and costs a `continue`, while silently skipping a stop the user asked for is a
  /// wrong answer they have no way to notice. It is the emitted-code form of "no silent unhandled cases".
  ///
  /// Contains no Call, so its state lives in frame slots across the branchy dispatch, matching
  /// <see cref="EmitDbgBacktrace"/>'s discipline.
  /// </summary>
  private void EmitDbgCondHolds() {
    _b.FunctionStart("__dbg_cond_holds", 2, 0x80);

    // Frame slots: 0=slotIdx (param) 1=fp (param) 2=rec 3=addr 4=value.
    const int slotIdxArg = 0;
    const int slotFpArg = 1;
    const int slotRec = 2;
    const int slotAddr = 3;
    const int slotValue = 4;

    var stopLabel = UniqueLabel("dbg_cond_stop");
    var skipLabel = UniqueLabel("dbg_cond_skip");
    var compareLabel = UniqueLabel("dbg_cond_compare");

    // One arm per supported width, and one per operator — both ENUMERATED from the tables the driver
    // validates against, so the evaluator and the grammar cannot come to know a different closed set.
    var widthLabels = DbgCondOperandWidths.ToDictionary(w => w, w => UniqueLabel($"dbg_cond_w{w}"));
    var opLabels = DbgCondOperators.ToDictionary(o => o.Code, o => UniqueLabel($"dbg_cond_op{o.Code}"));

    _b.LoadLocal(VReg.Arg0, slotIdxArg);
    EmitDbgCondRecAddr(VReg.Scratch1, VReg.Arg0, VReg.Scratch2);
    _b.StoreLocal(slotRec, VReg.Scratch1);

    // Kind: anything that is not a scalar compare — including the zeroed record of an unconditional
    // breakpoint, and any value a newer driver might introduce — stops.
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgCondOffKind);
    _b.CmpRegImm(VReg.Scratch2, DbgCondKindScalarCompare);
    _b.JumpIf(Condition.NotEqual, stopLabel);

    // addr = fp + slot (a SIGNED frame-relative offset, so a plain add is the whole computation).
    _b.LoadLocal(VReg.Scratch2, slotFpArg);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, DbgCondOffSlot);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.StoreLocal(slotAddr, VReg.Scratch2);

    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgCondOffWidth);
    foreach (int width in DbgCondOperandWidths) {
      _b.CmpRegImm(VReg.Scratch2, width);
      _b.JumpIf(Condition.Equal, widthLabels[width]);
    }
    _b.Jump(stopLabel);                                  // unrecognized width: stop rather than guess

    foreach (int width in DbgCondOperandWidths.Where(w => w != DbgCondWordSize))
      EmitDbgCondWidthArm(width, slotRec, slotAddr, slotValue, widthLabels[width], compareLabel);

    // A full-width operand needs no extension: the eight bytes ARE the 64-bit value either way. Laid out
    // LAST so it falls straight into the operator dispatch instead of jumping to it.
    _b.DefineLabel(widthLabels[DbgCondWordSize]);
    _b.LoadLocal(VReg.Scratch1, slotAddr);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, 0);
    _b.StoreLocal(slotValue, VReg.Scratch2);

    // Operator dispatch. Every ordering comparison is SIGNED: the driver normalizes the operand into the
    // signed 64-bit space before it ever gets here — a signed type is sign-extended above, and an
    // unsigned type is accepted only at a width narrower than 64 bits, so its zero-extended value is
    // always non-negative. An 8-byte UNSIGNED operand (whose top bit would flip the meaning of a signed
    // compare) is refused at `break` time rather than mis-compared here.
    _b.DefineLabel(compareLabel);
    _b.LoadLocal(VReg.Scratch1, slotRec);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgCondOffOp);
    foreach (var op in DbgCondOperators) {
      _b.CmpRegImm(VReg.Scratch2, op.Code);
      _b.JumpIf(Condition.Equal, opLabels[op.Code]);
    }
    _b.Jump(stopLabel);                                  // unrecognized operator: stop rather than guess

    foreach (var op in DbgCondOperators)
      EmitDbgCondCompareArm(opLabels[op.Code], op.Branch, slotRec, slotValue, stopLabel, skipLabel);

    _b.DefineLabel(skipLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();

    _b.DefineLabel(stopLabel);
    _b.MovRegImm(VReg.Ret, 1);
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
  /// __dbg_backtrace() — walk the STOPPED frame's saved-rbp chain and write a bounded array of `.text`
  /// code offsets into the control segment (DbgOffBtFrames), with the count in DbgOffBtCount. Frame 0
  /// is the exact stop-PC offset; frames 1..N are the return addresses up the chain (the driver applies
  /// the −1 call-site bias when symbolizing those). Reads the stopped fp/sp/pc the last stop event
  /// published into the segment, so it is only meaningful at a breakpoint stop; at the entry stop (no
  /// stop published, fp == 0) it writes count 0.
  ///
  /// It reuses mrt_fault_backtrace's frame discipline — range-validate each frame against the stopped
  /// [sp, sp + FaultStackWindowBytes) window, require the chain to strictly ascend, and reject a
  /// return address outside `.text` — so a corrupt rbp degrades to a short trace, never a wild read or
  /// a second fault. It is a stopped-thread walk (the single-M MVP); per-GT switching is P4.
  ///
  /// Contains no Call, so its persistent state (base, current fp, count, the stack window, the text
  /// base + size) lives in frame slots and the loop uses scratch registers freely.
  /// </summary>
  private void EmitDbgBacktrace() {
    _b.FunctionStart("__dbg_backtrace", 0, 0x80);

    var walkLabel = UniqueLabel("dbg_bt_walk");
    var emptyLabel = UniqueLabel("dbg_bt_empty");
    var storeCountLabel = UniqueLabel("dbg_bt_store_count");
    var doneLabel = UniqueLabel("dbg_bt_done");

    // Frame slots: 0=base 1=fp 2=count 3=stackLow 4=stackHigh 5=textBase 6=textSize.
    const int slotBase = 0;
    const int slotFp = 1;
    const int slotCount = 2;
    const int slotStackLow = 3;
    const int slotStackHigh = 4;
    const int slotTextBase = 5;
    const int slotTextSize = 6;

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // detached: write nothing
    _b.StoreLocal(slotBase, VReg.Scratch1);

    // fp = the stopped frame pointer. Zero at the entry stop (no stop event published) -> empty trace.
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffStopFp);
    _b.StoreLocal(slotFp, VReg.Scratch2);
    _b.JumpIfZero(VReg.Scratch2, emptyLabel);

    // Frame 0 = the exact stop-PC offset (already a validated `.text` code offset). BtFrames[0] = it.
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, DbgOffStopPc);
    _b.StoreIndirect(VReg.Scratch1, DbgOffBtFrames, VReg.Scratch3);
    _b.MovRegImm(VReg.Scratch2, 1);
    _b.StoreLocal(slotCount, VReg.Scratch2);

    // stackLow = stopped sp; stackHigh = stackLow + FaultStackWindowBytes (the same sane window the
    // fault backtrace trusts, larger than any thread or grown green-thread stack).
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffStopSp);
    _b.StoreLocal(slotStackLow, VReg.Scratch2);
    _b.AddRegImm(VReg.Scratch2, GtLayout.FaultStackWindowBytes);
    _b.StoreLocal(slotStackHigh, VReg.Scratch2);

    // textBase = &mrt_start; textSize = &symtable − textBase (the exact `.text` bound the symbolizer
    // trusts; an UNSIGNED compare below then rejects a negative offset as a huge value in one test).
    _b.LeaFuncAddr(VReg.Scratch2, "mrt_start");
    _b.StoreLocal(slotTextBase, VReg.Scratch2);
    _b.LeaSymdata(VReg.Scratch3, _b.SymbolTableLabel);
    _b.SubRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.StoreLocal(slotTextSize, VReg.Scratch3);

    _b.DefineLabel(walkLabel);
    _b.LoadLocal(VReg.Scratch0, slotCount);
    _b.CmpRegImm(VReg.Scratch0, DbgMaxBacktraceFrames);
    _b.JumpIf(Condition.AboveEqual, storeCountLabel);     // array full

    _b.LoadLocal(VReg.Scratch1, slotFp);                  // current frame
    _b.JumpIfZero(VReg.Scratch1, storeCountLabel);
    _b.LoadLocal(VReg.Scratch2, slotStackLow);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Below, storeCountLabel);          // below the stopped sp
    _b.LoadLocal(VReg.Scratch2, slotStackHigh);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.AboveEqual, storeCountLabel);     // beyond the sane window

    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, 8);     // ra = [fp + 8]
    _b.JumpIfZero(VReg.Scratch2, storeCountLabel);
    _b.LoadLocal(VReg.Scratch3, slotTextBase);
    _b.SubRegReg(VReg.Scratch2, VReg.Scratch3);           // off = ra − textBase
    _b.LoadLocal(VReg.Scratch3, slotTextSize);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.JumpIf(Condition.AboveEqual, storeCountLabel);     // outside .text (unsigned: catches negative)

    // BtFrames[count] = off  ->  [base + DbgOffBtFrames + count*8] = off
    _b.LoadLocal(VReg.Scratch0, slotBase);
    _b.LoadLocal(VReg.Scratch3, slotCount);
    _b.ShlRegImm(VReg.Scratch3, 3);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch3);
    _b.StoreIndirect(VReg.Scratch0, DbgOffBtFrames, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch0, slotCount);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(slotCount, VReg.Scratch0);

    // Advance with the ascending guard: next = [fp]; a non-ascending link ends the walk AFTER this
    // frame (already stored), so a corrupt chain yields a short trace, not a wild one.
    _b.LoadLocal(VReg.Scratch1, slotFp);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, 0);     // next = [fp]
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.JumpIf(Condition.BelowEqual, storeCountLabel);     // next <= fp -> stop
    _b.StoreLocal(slotFp, VReg.Scratch2);
    _b.Jump(walkLabel);

    _b.DefineLabel(emptyLabel);
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(slotCount, VReg.Scratch0);

    _b.DefineLabel(storeCountLabel);
    _b.LoadLocal(VReg.Scratch1, slotBase);
    _b.LoadLocal(VReg.Scratch2, slotCount);
    _b.StoreIndirect(VReg.Scratch1, DbgOffBtCount, VReg.Scratch2);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_read_mem() — copy ReadLen bytes of the debuggee's own memory at DbgOffCmdArg into the
  /// control segment's DbgOffReadBuf, then store 1 into DbgOffReadStatus. The parked GT runs this in the
  /// trap-handler context, so — like <see cref="EmitDbgBacktrace"/> — it allocates nothing, calls
  /// nothing, and keeps its state in scratch registers and frame slots: a plain bounded byte-copy loop.
  /// The driver only asks for addresses derived from valid locations (a stack slot, a followed struct
  /// pointer), and clamps each request to <see cref="DbgReadBufCap"/>; the loop clamps ReadLen to the
  /// same cap so a driver bug can never write past the buffer. A hardware fault guard for a dangling
  /// pointer is a documented P4a residual — the MVP does the raw copy.
  ///
  /// Written ONCE in the target-neutral builder (like <see cref="EmitDbgBacktrace"/>), so it covers x86
  /// and arm64 together; unlike the breakpoint patch it needs no W^X flip or single-step, since it only
  /// reads memory and writes the segment.
  /// </summary>
  private void EmitDbgReadMem() {
    _b.FunctionStart("__dbg_read_mem", 0, 0x40);

    var lenOkLabel = UniqueLabel("dbg_read_len_ok");
    var copyLabel = UniqueLabel("dbg_read_copy");
    var copyDoneLabel = UniqueLabel("dbg_read_copy_done");
    var doneLabel = UniqueLabel("dbg_read_done");

    // Frame slots: 0=base 1=src 2=dst 3=len 4=i. The copy loop contains no Call, so its state lives in
    // frame slots, matching the backtrace walk's slot discipline.
    const int slotBase = 0;
    const int slotSrc = 1;
    const int slotDst = 2;
    const int slotLen = 3;
    const int slotIndex = 4;

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // detached: copy nothing
    _b.StoreLocal(slotBase, VReg.Scratch1);

    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffCmdArg);   // src = the debuggee address to read
    _b.StoreLocal(slotSrc, VReg.Scratch2);

    // dst = base + DbgOffReadBuf, computed ONCE, then advanced by i each iteration. The copy stores each
    // byte at [dst + i] with a ZERO displacement because StoreIndirectByte offers only a [base + constant
    // disp] form — there is no base-plus-index addressing here — so the varying per-byte index must be
    // folded into a base register regardless. Advancing a base pointer is the idiomatic copy-loop form.
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.AddRegImm(VReg.Scratch2, DbgOffReadBuf);
    _b.StoreLocal(slotDst, VReg.Scratch2);

    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, DbgOffReadLen);  // len

    // Clamp len to [0, DbgReadBufCap] with an UNSIGNED compare (a negative len reads as a huge value and
    // is clamped down too), so the copy can never run past the buffer.
    _b.CmpRegImm(VReg.Scratch3, DbgReadBufCap);
    _b.JumpIf(Condition.BelowEqual, lenOkLabel);
    _b.MovRegImm(VReg.Scratch3, DbgReadBufCap);
    _b.DefineLabel(lenOkLabel);
    _b.StoreLocal(slotLen, VReg.Scratch3);

    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(slotIndex, VReg.Scratch0);              // i = 0

    _b.DefineLabel(copyLabel);
    _b.LoadLocal(VReg.Scratch0, slotIndex);               // i
    _b.LoadLocal(VReg.Scratch1, slotLen);                 // len
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, copyDoneLabel);       // i >= len (unsigned): done

    _b.LoadLocal(VReg.Scratch1, slotSrc);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);           // src + i
    _b.LoadIndirectByte(VReg.Scratch2, VReg.Scratch1, 0); // b = [src + i]

    _b.LoadLocal(VReg.Scratch1, slotDst);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);           // dst + i
    _b.StoreIndirectByte(VReg.Scratch1, 0, VReg.Scratch2); // [dst + i] = b (zero disp: see note above)

    _b.LoadLocal(VReg.Scratch0, slotIndex);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(slotIndex, VReg.Scratch0);              // i++
    _b.Jump(copyLabel);

    _b.DefineLabel(copyDoneLabel);
    _b.LoadLocal(VReg.Scratch1, slotBase);
    _b.MovRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, DbgOffReadStatus, VReg.Scratch2); // status = 1 (copy performed)

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_park_loop() — the stop-the-world pause. Spin on the command doorbell (CmdSeq), dispatching
  /// set-breakpoint / set-condition / clear-breakpoint / backtrace / read-memory (and acking each), until
  /// the driver sends continue; yield the slice between polls so the pause does not peg a core. Reused by the entry
  /// stop and the breakpoint-hit stop. Returns when continue arrives (or the agent detaches).
  /// </summary>
  private void EmitDbgParkLoop() {
    _b.FunctionStart("__dbg_park_loop", 0, 0x40);

    var loopLabel = UniqueLabel("dbg_park_loop");
    var idleLabel = UniqueLabel("dbg_park_idle");
    var setLabel = UniqueLabel("dbg_park_set");
    var setCondLabel = UniqueLabel("dbg_park_set_cond");
    var clearLabel = UniqueLabel("dbg_park_clear");
    var btLabel = UniqueLabel("dbg_park_backtrace");
    var readLabel = UniqueLabel("dbg_park_read");
    var ackLabel = UniqueLabel("dbg_park_ack");
    var contLabel = UniqueLabel("dbg_park_continue");
    var stepLabel = UniqueLabel("dbg_park_step");
    var exitLabel = UniqueLabel("dbg_park_exit");
    var doneLabel = UniqueLabel("dbg_park_done");

    _b.DefineLabel(loopLabel);
    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // detached
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, DbgOffCmdSeq);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, DbgOffAckSeq);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.JumpIf(Condition.Equal, idleLabel);               // no new command

    _b.StoreLocal(0, VReg.Scratch2);                     // remember the seq we are about to process

    // The command's outcome, published ahead of the ack that makes it visible. Ok is the default because
    // most commands genuinely cannot fail; the two that CAN — set-breakpoint and set-condition — overwrite
    // it where they fail, so a new command that can fail says so at the point it fails rather than needing
    // a line here. Written BEFORE the dispatch so the paths that leave the loop (continue / step) carry it
    // too, and so a handler's own store always wins.
    _b.MovRegImm(VReg.Scratch3, DbgCmdResultOk);
    _b.StoreIndirect(VReg.Scratch1, DbgOffCmdResult, VReg.Scratch3);

    _b.LoadIndirect(VReg.Ret, VReg.Scratch1, DbgOffCmd);
    _b.CmpRegImm(VReg.Ret, DbgCmdContinue);
    _b.JumpIf(Condition.Equal, contLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdSetBp);
    _b.JumpIf(Condition.Equal, setLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdClearBp);
    _b.JumpIf(Condition.Equal, clearLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdBacktrace);
    _b.JumpIf(Condition.Equal, btLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdReadMem);
    _b.JumpIf(Condition.Equal, readLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdStep);
    _b.JumpIf(Condition.Equal, stepLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdSetBpCond);
    _b.JumpIf(Condition.Equal, setCondLabel);
    _b.Jump(ackLabel);                                   // unknown command: ack and keep waiting

    _b.DefineLabel(setLabel);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, DbgOffCmdArg);
    _b.Call("__dbg_set_bp");
    _b.Jump(ackLabel);

    _b.DefineLabel(setCondLabel);                        // attach the staged condition, then ack (below)
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, DbgOffCmdArg);
    _b.Call("__dbg_set_bp_cond");
    _b.Jump(ackLabel);

    _b.DefineLabel(clearLabel);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, DbgOffCmdArg);
    _b.Call("__dbg_clear_bp");
    _b.Jump(ackLabel);

    _b.DefineLabel(btLabel);                             // fill the frame array, then ack (below)
    _b.Call("__dbg_backtrace");
    _b.Jump(ackLabel);

    _b.DefineLabel(readLabel);                           // fill the read buffer, then ack (below)
    _b.Call("__dbg_read_mem");
    _b.Jump(ackLabel);

    _b.DefineLabel(ackLabel);                            // reload base (a call clobbered it), ack, loop
    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.LoadLocal(VReg.Scratch2, 0);
    _b.StoreRelease(VReg.Scratch1, DbgOffAckSeq, VReg.Scratch2);
    _b.Jump(loopLabel);

    _b.DefineLabel(idleLabel);
    _b.OsYield();
    _b.Jump(loopLabel);

    // continue / step both EXIT the loop; they differ only in the single-step disposition they leave for
    // the trap handler. The breakpoint path re-arms a single-step-over after park returns REGARDLESS, so
    // continue must leave OverBp (else the handler would defer its own step-over trap), and step leaves
    // User (the handler then publishes a step stop). Scratch1=base and Scratch2=seq are untouched by the
    // dispatch compares, so the ack below still has them; Ret is free to carry the mode immediate.
    _b.DefineLabel(contLabel);
    _b.MovRegImm(VReg.Ret, DbgStepModeOverBp);
    _b.StoreGlobal(DbgStepModeGlobal, VReg.Ret);
    _b.Jump(exitLabel);

    _b.DefineLabel(stepLabel);
    _b.MovRegImm(VReg.Ret, DbgStepModeUser);
    _b.StoreGlobal(DbgStepModeGlobal, VReg.Ret);

    _b.DefineLabel(exitLabel);                           // ack (no call clobbered base/seq), return
    _b.StoreRelease(VReg.Scratch1, DbgOffAckSeq, VReg.Scratch2);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_on_breakpoint(bpAbsAddr, sp, fp) -> 1 if bpAbsAddr is a known breakpoint (the trapping thread
  /// has been dealt with), 0 if it is not ours (the target thunk then defers to the fault chain). The
  /// neutral half of the trap dispatch; the platform thunk supplies the trapping context and applies the
  /// single-step-over afterwards.
  ///
  /// "Dealt with" covers TWO outcomes, and both return 1. If the slot's condition holds (or there is
  /// none) a stop event is published and the thread parks until continue. If it does NOT hold, the hit is
  /// SKIPPED: no publish, no park, and the thunk's post-return path — disarm, single-step the real
  /// instruction, re-arm, resume — carries execution past the breakpoint exactly as a continue would.
  /// That is why neither backend's trap thunk needed a line for conditional breakpoints: the skip path
  /// reuses, verbatim, the step-over dance the thunk already performs after every hit.
  /// </summary>
  private void EmitDbgOnBreakpoint() {
    _b.FunctionStart("__dbg_on_breakpoint", 3, 0x60);

    var missLabel = UniqueLabel("dbg_on_bp_miss");
    var skipLabel = UniqueLabel("dbg_on_bp_skip");

    _b.LoadLocal(VReg.Arg0, 0);                           // bpAbsAddr
    _b.Call("__dbg_bp_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, missLabel);

    // Evaluate the slot's condition BEFORE publishing: a hit whose condition is false must not reach the
    // driver at all, or a `break … if` inside a hot loop would cost a round trip per iteration.
    _b.MovRegReg(VReg.Arg0, VReg.Ret);                    // slot index (Ret is clobbered by the call)
    _b.LoadLocal(VReg.Arg1, 2);                           // fp — the frame the condition's local lives in
    _b.Call("__dbg_cond_holds");
    _b.JumpIfZero(VReg.Ret, skipLabel);

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

    // Condition false: skip the stop, but leave behind EXACTLY what a released park would have.
    //
    // The park loop is what normally sets __dbg_step_mode, from the command that released it — and the
    // thunk's post-return step-over depends on that: x86's single-step trap treats mode == None as "not
    // ours" and DEFERS to the fault chain, which for the step this very path is about to arm would mean
    // an unhandled STATUS_SINGLE_STEP killing the debuggee. Skipping the park therefore has to leave the
    // OverBp disposition the park loop's continue leaves, and the invariant the thunks rely on becomes:
    // __dbg_on_breakpoint returning 1 has ALWAYS set the step disposition. (arm64 resumes silently on
    // mode == None and would survive without this; it is set on both because one function returning one
    // contract to two callers is the point.)
    _b.DefineLabel(skipLabel);
    _b.MovRegImm(VReg.Scratch1, DbgStepModeOverBp);
    _b.StoreGlobal(DbgStepModeGlobal, VReg.Scratch1);
    _b.MovRegImm(VReg.Ret, 1);
    _b.FunctionEnd();

    _b.DefineLabel(missLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_on_step(absPc, sp, fp) — the step twin of <see cref="EmitDbgOnBreakpoint"/>: publish a
  /// <see cref="DbgStopReasonStep"/> stop for the just-completed single-step and park until the next
  /// command. Unlike on_breakpoint it does NO slot lookup — a step stop is unconditional, not "is this
  /// address one of ours" — so it only converts the absolute PC to a code offset (the base the sidecar
  /// resolves) and publishes. Called from each backend's trap thunk, which supplies the stepped
  /// context's PC/SP/FP; the publish+park pair lives here so the two backends cannot word a step stop
  /// differently.
  /// </summary>
  private void EmitDbgOnStep() {
    _b.FunctionStart("__dbg_on_step", 3, 0x40);

    // pcOffset = absPc − &mrt_start (same base __dbg_on_breakpoint and the panic symbolizer subtract).
    _b.LeaFuncAddr(VReg.Scratch1, "mrt_start");
    _b.LoadLocal(VReg.Scratch2, 0);                      // absPc
    _b.SubRegReg(VReg.Scratch2, VReg.Scratch1);          // pcOffset

    _b.MovRegImm(VReg.Arg0, DbgStopReasonStep);
    _b.MovRegReg(VReg.Arg1, VReg.Scratch2);
    _b.LoadLocal(VReg.Arg2, 1);                          // sp
    _b.LoadLocal(VReg.Arg3, 2);                          // fp
    _b.Call("__dbg_publish_stop");

    _b.Call("__dbg_park_loop");
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_prepare_step_at(absPc) — arm the DATA side of a user single-step that is about to execute the
  /// instruction at absPc: if a user breakpoint sits there, DISARM it (so the real instruction runs, not
  /// the trap) and record it in <see cref="DbgStepAddrGlobal"/> so the trap handler re-arms it once the
  /// step completes; otherwise leave step_addr 0 (nothing to re-arm). It does NOT touch the hardware
  /// single-step itself (EFLAGS.TF / the temp bp) — that is per-backend and the thunk sets it after this
  /// returns. Shared by x86 and arm64 so the "step past a breakpoint we happen to be sitting on" logic is
  /// written once. The first user step from a breakpoint stop needs no call here — the breakpoint path
  /// already disarmed the bp it parked on — but every SUBSEQUENT step lands on an arbitrary instruction
  /// that may coincide with another breakpoint, which this handles.
  /// </summary>
  private void EmitDbgPrepareStepAt() {
    _b.FunctionStart("__dbg_prepare_step_at", 1, 0x40);

    var noBpLabel = UniqueLabel("dbg_prep_no_bp");

    _b.LoadLocal(VReg.Arg0, 0);                          // absPc
    _b.Call("__dbg_bp_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, noBpLabel);                // no breakpoint here → step_addr stays 0

    _b.LoadLocal(VReg.Arg0, 0);
    _b.Call("__dbg_bp_orig_of_addr");                    // Ret = original code unit
    _b.MovRegReg(VReg.Arg1, VReg.Ret);                   // arg1 = orig
    _b.LoadLocal(VReg.Arg0, 0);                          // arg0 = absPc
    _b.Call("__dbg_disarm_bp");                          // restore the real instruction so the step runs it
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.StoreGlobal(DbgStepAddrGlobal, VReg.Scratch1);    // remember it for the post-step re-arm
    _b.FunctionEnd();

    _b.DefineLabel(noBpLabel);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreGlobal(DbgStepAddrGlobal, VReg.Scratch1);    // no bp to re-arm after this step
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
    EmitDbgCondZero();
    EmitDbgCondHolds();
    EmitDbgSetBpCond();
    EmitDbgPublishStop();
    EmitDbgBacktrace();
    EmitDbgReadMem();
    EmitDbgParkLoop();
    EmitDbgOnBreakpoint();
    EmitDbgOnStep();
    EmitDbgPrepareStepAt();
    EmitDebugAgentInit();
    EmitDebugAgentShutdown();
  }
}
