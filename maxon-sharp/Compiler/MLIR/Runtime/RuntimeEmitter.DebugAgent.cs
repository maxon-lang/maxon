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
  ///
  /// v8 (P4d-2a) adds GREEN-THREAD VISIBILITY: the <see cref="DbgCmdGtList"/> /
  /// <see cref="DbgCmdGtBacktrace"/> commands and the record array they fill
  /// (<see cref="DbgOffGtCount"/> / <see cref="DbgOffGtTruncated"/> / <see cref="DbgOffGtStopped"/> /
  /// <see cref="DbgOffGtRecords"/>), all previously-zero bytes after <see cref="DbgOffCmdResult"/>, so no
  /// earlier field moves. Same honesty gate as every capability above, and it matters here for the
  /// v3/v4 reason rather than the v6 one: a v7 agent acks the unknown list command without writing a
  /// record, and its zeroed count reads as a perfectly plausible "this program has no green threads" —
  /// a wrong answer dressed as an answer. So the driver refuses `threads` below this version instead.
  ///
  /// v9 (P4d-2b) adds GREEN-THREAD CONTROL: the <see cref="DbgCmdGtHold"/> / <see cref="DbgCmdGtRelease"/>
  /// commands, the <see cref="DbgOffStopOthers"/> mode word, and two more words per green-thread record
  /// (<see cref="DbgGtRecOffTopFp"/>, which is what lets `print`/`locals` read a SELECTED thread's frame,
  /// and <see cref="DbgGtRecOffHold"/>, which reports whether the debugger owns that thread). The record
  /// grew, so this version is what tells the driver how wide a record is — reading a v9 array with a v8
  /// stride does not fail, it silently reports OTHER THREADS' fields, which is the worst shape of wrong
  /// answer this segment can produce. Both new commands and the mode word are refused below it.
  ///
  /// v10 (P4e) adds DEBUGSTREAM CORRELATION: <see cref="DbgOffStopDsMark"/>, the DebugStream ring's
  /// write watermark at the moment a stop is published, so the driver can render exactly the trace
  /// entries between the previous stop's mark and this one's. It is a previously-zero word after
  /// <see cref="DbgOffStopOthers"/>, so no earlier field moves — and the gate matters for the v3/v4
  /// reason: a v9 agent never writes the word, and its zero is a PERFECTLY LEGITIMATE watermark
  /// ("nothing has been traced yet"), so an ungated driver would render a confident empty slice for a
  /// program that traced plenty. <see cref="DbgTraceMinVersion"/> refuses instead.
  public const long DbgControlVersion = 10;

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

  // Green-thread record array (agent writes, driver reads) — v8. Placed at the first free offset after
  // DbgOffCmdResult (0x4B8 + 8); DbgOffGtRecords + DbgMaxGreenThreads * DbgGtRecSize = 0x4D8 + 32*56
  // = 0xBD8 < 4096, so the whole array stays inside the one control page.
  //
  // DbgOffGtStopped is the raw GreenThread* the trapping M was running when it parked — the ONE fact
  // the driver cannot derive, because a stop event reports a PC/SP/FP and those name a stopped THREAD,
  // not a stopped GREEN thread. 0 means the stop was taken on a thread that owns no processor (so it
  // has no current green thread), which the driver reports rather than guessing at.
  public const int DbgOffGtCount = 0x4C0;      // i64 — records actually written
  public const int DbgOffGtTruncated = 0x4C8;  // i64 — 1 = there were MORE live GTs than the array holds
  public const int DbgOffGtStopped = 0x4D0;    // i64 — GreenThread* the stop is parked on (0 = none)
  public const int DbgOffGtRecords = 0x4D8;    // DbgMaxGreenThreads records of DbgGtRecSize bytes

  /// Stop-others mode (driver writes once, before spawn) — v9. Non-zero asks the agent to hold EVERY
  /// green thread for the duration of each stop, so the rest of the program is frozen while the user
  /// looks at it; zero is the P3b behaviour, where only the trapping processor is parked.
  ///
  /// It is DERIVED from the end of the record array rather than hand-placed, because that array's size
  /// is itself a product of two constants — the one field placement a reader cannot check by eye, and
  /// the one that would silently overlap if <see cref="DbgGtRecSize"/> grew again. The emit-time check
  /// in <see cref="EmitDebugAgentFunctions"/> bounds the whole segment.
  ///
  /// A pre-spawn MODE word rather than a command, exactly like <see cref="DbgOffStopAtEntry"/>: it must
  /// already be in force at the entry stop, which happens before the driver can post anything.
  public const int DbgOffStopOthers = DbgOffGtRecords + DbgMaxGreenThreads * DbgGtRecSize;

  /// <summary>
  /// The DebugStream WATERMARK at the instant a stop was published (agent writes, driver reads) — v10.
  /// It is the ring's `write_cursor`: how many bytes of trace entries the debuggee had reserved by the
  /// time it parked. The driver renders the entries between the PREVIOUS stop's mark and this one's,
  /// which is the question a user actually has — "what happened since you were last stopped".
  ///
  /// It is a WATERMARK rather than a timestamp on purpose: a stop PARKS the thread, so wall-clock
  /// readings taken around one describe the debugger, not the program. A byte position in the ring is
  /// the only thing that partitions the trace at exactly the instant the stop was taken.
  ///
  /// Written by <c>__dbg_publish_stop</c> BEFORE its release-bump of <see cref="DbgOffStopSeq"/>, so a
  /// driver that has seen the new sequence number has seen the mark that goes with it.
  /// </summary>
  public const int DbgOffStopDsMark = DbgOffStopOthers + DbgGtWordSize;

  // What a NEGATIVE mark means. A real watermark is a byte count, so it can never be negative — and
  // ZERO is a legitimate one ("nothing has been traced yet"), which is exactly why "unavailable" cannot
  // be spelled 0: rendering that as "no events" is a wrong answer wearing an empty answer's costume.
  //
  // The two cases stay DISTINCT because the user's next step differs. NoStream is a compile-time fact
  // (this binary has no trace hooks at all — rebuild it with `--debugstream`); Detached is a runtime one
  // (the hooks are there but nothing opened the ring), and telling one as the other would send a user to
  // rebuild a binary that is already right.
  public const long DbgStopDsMarkNoStream = -1;
  public const long DbgStopDsMarkDetached = -2;

  /// The control-segment version at which the agent publishes <see cref="DbgOffStopDsMark"/>. The
  /// driver refuses trace correlation below it rather than reading the word, whose zero at v9 reads as
  /// a believable "nothing was traced".
  public const long DbgTraceMinVersion = 10;

  /// One past the last byte the agent writes in the control segment — the ONE expression the emit-time
  /// page check reads, so adding a field means extending this and nothing else.
  public const int DbgControlSegmentHighWater = DbgOffStopDsMark + DbgGtWordSize;

  /// The most green threads one enumeration reports. It bounds BOTH the record array (which must fit the
  /// control page) and the LIST WALK itself, and the second is what makes it load-bearing rather than a
  /// capacity number: the walk of `__gt_all_head` runs in the trap handler and is deliberately UNLOCKED
  /// (see <see cref="EmitDbgGtScan"/>), so a chain another M is mutating under us must not be able to
  /// spin the debuggee forever. A truncated list SAYS SO through <see cref="DbgOffGtTruncated"/>; a
  /// silently short thread list is a wrong answer.
  public const int DbgMaxGreenThreads = 32;

  /// The width of every word in a green-thread record — the same "state it once and DERIVE the size"
  /// discipline as <see cref="DbgCondWordSize"/>.
  public const int DbgGtWordSize = 8;

  /// <see cref="DbgGtWordSize"/> as a shift, for the emitters that turn an index into a byte offset.
  /// It is DERIVED (and the derivation CHECKED, in <see cref="EmitDebugAgentFunctions"/>) rather than
  /// written as a bare 3 beside a size of 8: two spellings of one width is the shape where a widened
  /// table keeps indexing itself by the old stride and reads its neighbour's entry.
  public const int DbgGtWordShift = 3;

  // Green-thread record layout (offsets within one DbgOffGtRecords entry).
  //
  // Status and OnCpu are deliberately SEPARATE, and conflating them would be a wrong answer: `status` is
  // the runtime's own state word, which is set to Running BEFORE a context switch INTO a green thread and
  // is never cleared when it parks, so a parked GT routinely reads `running`. OnCpu is the debugger's
  // question — "is some processor's currentGt this thread right now" — and it is the ONLY safe gate on
  // reading a GT's saved rsp/rbp (see EmitDbgGtBacktrace).
  //
  // EntryPc is meaningful exactly when Proc == DbgGtNotAProc: a P's inline main-thread GT is not spawned
  // from a function and has no entry, and that is DERIVED from Proc rather than written down twice.
  //
  // TopFp is the FRAME POINTER the TopPc belongs to, and it is what makes `gt <id>` more than a label:
  // a value is read as [fp + slot], so a thread the driver has SELECTED needs its frame pointer, not
  // only its code offset. It is published beside TopPc rather than fetched by a second command because
  // the two are one fact about one frame, and a second fetch could see a different one.
  public const int DbgGtRecOffHandle = 0 * DbgGtWordSize;   // raw GreenThread* — the driver's identity key
  public const int DbgGtRecOffStatus = 1 * DbgGtWordSize;   // GtLayout.GtStatus* as the runtime holds it
  public const int DbgGtRecOffOnCpu = 2 * DbgGtWordSize;    // 1 = currentGt of an ACTIVE processor
  public const int DbgGtRecOffEntryPc = 3 * DbgGtWordSize;  // entry function's code offset (0 = none)
  public const int DbgGtRecOffProc = 4 * DbgGtWordSize;     // owning P index, or DbgGtNotAProc
  public const int DbgGtRecOffTopKind = 5 * DbgGtWordSize;  // DbgGtTopFrame*
  public const int DbgGtRecOffTopPc = 6 * DbgGtWordSize;    // top frame's code offset (per TopKind)
  public const int DbgGtRecOffTopFp = 7 * DbgGtWordSize;    // top frame's frame pointer (0 = none)
  public const int DbgGtRecOffHold = 8 * DbgGtWordSize;     // DbgGtHold*

  /// One green-thread record: the nine words above, DERIVED rather than restated so the stride the
  /// agent strides by cannot drift from the fields it holds.
  public const int DbgGtRecSize = 9 * DbgGtWordSize;

  /// Sentinel <see cref="DbgGtRecOffProc"/> for a SPAWNED green thread, which belongs to no processor —
  /// it migrates between them. A real P index is always &gt;= 0, so this cannot collide.
  public const long DbgGtNotAProc = -1;

  // What a record's TopKind says about TopPc — three genuinely different things, kept apart because the
  // driver must symbolize them differently and must never invent the third:
  //   None   — there is no readable top frame. Either the thread is ON-CPU somewhere (its saved rsp/rbp
  //            are stale, and walking them yields a plausible-looking WRONG backtrace), or it is parked
  //            but has not started (a spawned GT's rbp is 0 until its first context switch).
  //   Exact  — TopPc is the exact stopped PC. Only the STOPPED green thread has one.
  //   Return — TopPc is a RETURN ADDRESS off the parked frame chain, so the driver applies the −1
  //            call-site bias when symbolizing it, exactly as it does for backtrace frames 1..N.
  public const long DbgGtTopFrameNone = 0;
  public const long DbgGtTopFrameExact = 1;
  public const long DbgGtTopFrameReturn = 2;

  // What a record's Hold word says about the DEBUGGER's ownership of that thread. Two states, and the
  // difference between them is exactly the cooperative limit this rung is honest about:
  //   None    — the debugger has not asked for this thread.
  //   Held    — a hold is in force AND the thread is not executing on any processor, so the scheduler
  //             cannot start it: it will not run again until the hold is dropped. That is true whether
  //             or not it has physically reached __dbg_gt_dequeue_filtered yet, because that filter is
  //             passed BEFORE a thread runs, never after.
  //   Pending — a hold is in force but the thread IS executing on a processor. A cooperative park
  //             cannot reach into a running thread, so it keeps running until it next interacts with
  //             the scheduler — and a thread that never does (a compute loop) stays here forever. It
  //             is a DISTINCT word from Held precisely so nothing can report the one as the other.
  public const long DbgGtHoldNone = 0;
  public const long DbgGtHoldHeld = 1;
  public const long DbgGtHoldPending = 2;

  /// The control-segment version at which the agent first ANSWERS a command. Below it the word is the
  /// zeroed segment's 0, which is indistinguishable from a refusal, so the driver falls back to the old
  /// weaker contract there (an ack means the command was processed) rather than calling every command on
  /// an older binary refused.
  public const long DbgCmdResultMinVersion = 7;

  /// <summary>
  /// The control-segment version at which the agent's green-thread surface — enumeration AND control —
  /// is trustworthy. The driver refuses `threads`, `gt-backtrace`, `gt`, `gt-park`, `gt-resume` and
  /// `--stop-others` below it rather than reading the record array, which at v8 would render as an
  /// empty — and entirely believable — thread list for a program that plainly has green threads.
  ///
  /// ⚠ It is ONE number for BOTH halves, and this rung is where it MOVED (8 → 9) rather than gaining a
  /// second gate beside it. The contract asked for a separate control gate, but the RECORD GREW at v9
  /// (<see cref="DbgGtRecOffTopFp"/> / <see cref="DbgGtRecOffHold"/>), and a stride change is not a
  /// capability that can be missing: a v9 driver reading a v8 array does not see an absent field, it
  /// reads the NEXT thread's handle as this thread's frame pointer. So visibility cannot outlive the
  /// stride either, and two gates here would be the same number written twice — the one bug this
  /// codebase keeps closing.
  /// </summary>
  public const long DbgGtMinVersion = 9;

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
  public const long DbgCmdGtList = 8;
  public const long DbgCmdGtBacktrace = 9;
  public const long DbgCmdGtHold = 10;
  public const long DbgCmdGtRelease = 11;

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

  /// <summary>
  /// How many green threads the debugger can hold INDIVIDUALLY at once (`gt-park`). Agent .data, like
  /// the breakpoint table and for the same reasons, and a full table is REFUSED through
  /// <see cref="DbgOffCmdResult"/> rather than silently dropped — a park the user was told took effect
  /// and did not is a thread that keeps running under a debugger that says it is stopped.
  ///
  /// It bounds only the by-name holds. `--stop-others` holds every thread through
  /// <see cref="DbgGtHoldAllGlobal"/> and needs no slot at all, which is why 16 is generous rather than
  /// a limit anybody meets: a human parks a handful of threads by name.
  /// </summary>
  public const int DbgMaxHeldGreenThreads = 16;

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

  // ---- Green-thread HOLD state (P4d-2b), agent .data ----
  //
  // ⚠ TWO SIDES, AND THEY MUST NOT BE CONFUSED. The words below are written by the TRAP HANDLER (the
  // park loop, servicing a driver command) and read by the SCHEDULER (__gt_dequeue, on an ordinary
  // worker thread). That split is what keeps the agent async-signal-safe while still being able to take
  // the scheduler's own lock: the handler only ever stores a word, and every queue manipulation —
  // which needs SchedLock — happens on the scheduler side, where taking it is ordinary.
  //
  //   __dbg_gt_hold      — the handles `gt-park` named (0 = free slot). Handler writes, scheduler reads.
  //   __dbg_gt_hold_all  — non-zero while a `--stop-others` stop is in force. Handler writes.
  //   __dbg_gt_held_head — the chain of threads the scheduler has actually caught and set aside, linked
  //                        through GtOffNext (safe: a caught thread is in no queue, which is exactly the
  //                        condition under which the free list reuses that same field). Scheduler only.
  //   __dbg_gt_readmit   — the doorbell that says "a hold was dropped; re-offer what you are holding".
  //                        Handler sets it, the scheduler clears it under the lock. It exists so the
  //                        common case (a hold in force, nothing to release) costs ONE load in the
  //                        dequeue path instead of a lock and a walk.
  public const string DbgGtHoldGlobal = "__dbg_gt_hold_table";
  public const string DbgGtHoldAllGlobal = "__dbg_gt_hold_all";
  public const string DbgGtHeldHeadGlobal = "__dbg_gt_held_head";
  public const string DbgGtReadmitGlobal = "__dbg_gt_readmit";

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

    // All four start zero, which is "no thread is held" — so an un-debugged program's __gt_dequeue
    // never reaches any of this. What its dispatcher DOES cost is stated once, where the dispatcher is
    // (RuntimeEmitter.Scheduler.cs, EmitGtDequeue), and measured rather than inferred: a second
    // statement of it here is how a hot path's cost comes to be described two ways and wrongly in both.
    _b.DefineGlobal(DbgGtHoldGlobal, DbgMaxHeldGreenThreads * DbgGtWordSize, 0);
    _b.DefineGlobal(DbgGtHoldAllGlobal, DbgGtWordSize, 0);
    _b.DefineGlobal(DbgGtHeldHeadGlobal, DbgGtWordSize, 0);
    _b.DefineGlobal(DbgGtReadmitGlobal, DbgGtWordSize, 0);
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
  /// Emit <paramref name="funcLabel"/>(matchVal) -> the index into the i64 table
  /// <paramref name="tableGlobal"/> (of <paramref name="entryCount"/> entries) whose entry equals
  /// matchVal, or -1. Pure scan, no calls.
  ///
  /// TWO agent tables are keyed this way and both use the SAME "0 means free" convention, so the scan is
  /// written once and instantiated twice rather than copied:
  ///   * <c>__dbg_bp_slot</c> over <see cref="DbgBpAddrGlobal"/> — an absolute `.text` address finds an
  ///     existing breakpoint, and 0 finds a free slot (a real breakpoint address is never 0);
  ///   * <c>__dbg_gt_hold_slot</c> over <see cref="DbgGtHoldGlobal"/> — a GreenThread* finds an existing
  ///     hold, and 0 finds a free slot (a real green thread is never at address 0).
  /// </summary>
  private void EmitDbgTableSlotScan(string funcLabel, string tableGlobal, int entryCount) {
    _b.FunctionStart(funcLabel, 1, 0x40);

    var loopLabel = UniqueLabel(funcLabel + "_loop");
    var foundLabel = UniqueLabel(funcLabel + "_found");
    var missLabel = UniqueLabel(funcLabel + "_miss");

    _b.LeaGlobal(VReg.Scratch1, tableGlobal);             // base
    _b.ZeroReg(VReg.Scratch2);                            // i = 0

    _b.DefineLabel(loopLabel);
    _b.CmpRegImm(VReg.Scratch2, entryCount);
    _b.JumpIf(Condition.AboveEqual, missLabel);
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.ShlRegImm(VReg.Scratch3, 3);
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch1);           // &table[i]
    _b.LoadIndirect(VReg.Ret, VReg.Scratch3, 0);          // table[i]
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
  /// <paramref name="dest"/> = <paramref name="index"/> * <paramref name="stride"/>, clobbering
  /// <paramref name="scratch"/>.
  ///
  /// A record stride is not a power of two and the neutral backend has no multiply-by-immediate, so it
  /// is emitted as a sum of shifts — DERIVED from the constant's own set bits rather than from
  /// hand-written shift amounts, which would be a second copy of the record size that a later field
  /// addition could silently leave behind. Shared by BOTH record tables (the breakpoint conditions and
  /// the green-thread records) so neither can grow a private copy of the derivation.
  /// </summary>
  private void EmitDbgScaledIndex(VReg dest, VReg index, VReg scratch, int stride) {
    // dest is zeroed and scratch is overwritten before either is added, so an index aliasing either one
    // is destroyed and the arithmetic silently produces a wrong ADDRESS — the sort of thing that reads
    // as a corrupt record rather than as a bug in here. Both current callers pass three distinct
    // registers; this makes that a requirement rather than a coincidence.
    if (dest == index || scratch == index)
      throw new InvalidOperationException(
        $"{nameof(EmitDbgScaledIndex)} needs its index in a register distinct from dest and scratch");

    _b.ZeroReg(dest);
    for (int bit = 63; bit >= 0; bit--) {
      if ((stride & (1L << bit)) == 0) continue;
      _b.MovRegReg(scratch, index);
      _b.ShlRegImm(scratch, bit);
      _b.AddRegReg(dest, scratch);
    }
  }

  /// <summary>
  /// <paramref name="dest"/> = &amp;__dbg_bp_cond[<paramref name="slotIdx"/>], clobbering
  /// <paramref name="scratch"/>. The one place the condition table is indexed, so the three users
  /// (zero / evaluate / store) cannot disagree about its stride.
  /// </summary>
  private void EmitDbgCondRecAddr(VReg dest, VReg slotIdx, VReg scratch) {
    EmitDbgScaledIndex(dest, slotIdx, scratch, DbgCondRecSize);
    _b.LeaGlobal(scratch, DbgBpCondGlobal);
    _b.AddRegReg(dest, scratch);
  }

  /// <summary>
  /// Jump to <paramref name="noStopLabel"/> unless a stop event has actually been PUBLISHED, given the
  /// segment base in <paramref name="segmentBase"/>. Clobbers <paramref name="scratch"/>.
  ///
  /// Two readers ask this and neither can be allowed to answer it privately: the stopped-thread
  /// backtrace, to decide "empty trace", and the green-thread record, to decide whether the stopped
  /// thread has an EXACT top frame. Both are false at the entry stop, where the agent parks before any
  /// breakpoint. Change how a published stop is detected in one place only and the other keeps the old
  /// rule — surfacing as a wrong top frame or a wrong empty trace, never as a compile error.
  /// </summary>
  private void EmitDbgJumpIfNoStopPublished(VReg segmentBase, VReg scratch, string noStopLabel) {
    _b.LoadIndirect(scratch, segmentBase, DbgOffStopFp);
    _b.JumpIfZero(scratch, noStopLabel);
  }

  /// <summary>
  /// Write the walk window for the stack green thread <paramref name="gt"/> is running on into the frame
  /// slots <paramref name="slotStackLow"/> / <paramref name="slotStackHigh"/>: from
  /// <paramref name="sp"/> up to that stack's end.
  ///
  /// The upper bound is <c>__gt_stack_high</c>, the runtime's own answer, shared with mrt_panic and
  /// mrt_fault_backtrace — the agent must not carry a second opinion about where a stack ends, because
  /// getting it wrong is not a cosmetic difference, it FAULTS. A green-thread stack is
  /// GtLayout.GtInitialStackSize and the spawn trampoline's frame pointer sits at the very top of it, so
  /// a walk bounded by the 64 MiB fallback reads the return-address word one past the end and takes an
  /// access violation inside the trap handler. That is exactly what the stopped-thread backtrace did the
  /// moment a breakpoint could first be taken on a green-thread stack.
  ///
  /// The low bound is <paramref name="sp"/> itself rather than the stack's base: every live frame is
  /// above the stack pointer the walk starts from, so it is both correct and tighter, and it is the one
  /// bound that also means something for a stack with no recorded extent.
  ///
  /// Makes a Call, so callers must have nothing live in a caller-saved register across it.
  /// </summary>
  private void EmitDbgStackWindow(VReg gt, VReg sp, int slotStackLow, int slotStackHigh) {
    _b.StoreLocal(slotStackLow, sp);                      // consumed first, so sp may be any register
    _b.MovRegReg(VReg.Arg0, gt);
    _b.LoadLocal(VReg.Arg1, slotStackLow);
    _b.Call("__gt_stack_high");
    _b.StoreLocal(slotStackHigh, VReg.Ret);
  }

  /// <summary>
  /// <paramref name="dest"/> = &amp;records[<paramref name="index"/>] in the control segment at
  /// <paramref name="segmentBase"/>, clobbering <paramref name="scratch"/>. The green-thread twin of
  /// <see cref="EmitDbgCondRecAddr"/>, and it exists for the same reason: the stride and the ARRAY BASE
  /// are one fact, and the two users (append a record, read one back for a per-thread backtrace) must
  /// not each carry their own copy of it.
  /// </summary>
  private void EmitDbgGtRecAddr(VReg dest, VReg index, VReg segmentBase, VReg scratch) {
    EmitDbgScaledIndex(dest, index, scratch, DbgGtRecSize);
    _b.AddRegReg(dest, segmentBase);
    _b.AddRegImm(dest, DbgOffGtRecords);
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

    EmitDbgStopDsMark(VReg.Scratch1, VReg.Scratch2, VReg.Scratch3);

    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffStopSeq);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.StoreRelease(VReg.Scratch1, DbgOffStopSeq, VReg.Scratch2);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// Store <see cref="DbgOffStopDsMark"/> — the DebugStream watermark this stop is correlated against
  /// (P4e). <paramref name="baseReg"/> holds the control-segment base; the two scratch registers are
  /// clobbered.
  ///
  /// The `--debugstream` arm is a COMPILE-TIME branch, and it has to be: `__ds_base` is emitted only by
  /// <see cref="EmitDebugStreamGlobals"/>, which runs only under that flag, while the agent is emitted
  /// into EVERY binary. Referencing the global unconditionally would not degrade — it would fail to
  /// resolve. So a binary without the hooks stores the compile-time constant that SAYS it has none,
  /// which is the same fact the driver needs for its refusal, arrived at without a second flag to keep
  /// in step with this one.
  ///
  /// Cost: this runs inside the trap handler, once per published stop — a path that has already taken a
  /// kernel exception dispatch — so it is nowhere near the scheduler hooks invariant 2 governs.
  /// </summary>
  private void EmitDbgStopDsMark(VReg baseReg, VReg markReg, VReg dsBaseReg) {
    if (Compiler.DebugStream) {
      var storeLabel = UniqueLabel("dbg_publish_ds_mark");

      _b.LoadGlobal(dsBaseReg, "__ds_base");
      _b.MovRegImm(markReg, DbgStopDsMarkDetached);
      _b.JumpIfZero(dsBaseReg, storeLabel);
      _b.LoadIndirect(markReg, dsBaseReg, DsOffWriteCursor);
      _b.DefineLabel(storeLabel);
    } else {
      _b.MovRegImm(markReg, DbgStopDsMarkNoStream);
    }

    _b.StoreIndirect(baseReg, DbgOffStopDsMark, markReg);
  }

  /// <summary>
  /// __dbg_backtrace() — walk the STOPPED frame's saved-rbp chain and write a bounded array of `.text`
  /// code offsets into the control segment (DbgOffBtFrames), with the count in DbgOffBtCount. Frame 0
  /// is the exact stop-PC offset; frames 1..N are the return addresses up the chain (the driver applies
  /// the −1 call-site bias when symbolizing those). Reads the stopped fp/sp/pc the last stop event
  /// published into the segment, so it is only meaningful at a breakpoint stop; at the entry stop (no
  /// stop published, fp == 0) it writes count 0.
  ///
  /// It reuses mrt_fault_backtrace's frame discipline through <see cref="EmitDbgWalkFrames"/> — the ONE
  /// frame-chain walk in the agent, shared with the per-green-thread backtrace — so a corrupt rbp
  /// degrades to a short trace, never a wild read or a second fault.
  /// </summary>
  private void EmitDbgBacktrace() {
    _b.FunctionStart("__dbg_backtrace", 0, 0x80);

    var emptyLabel = UniqueLabel("dbg_bt_empty");
    var storeCountLabel = UniqueLabel("dbg_bt_store_count");
    var doneLabel = UniqueLabel("dbg_bt_done");

    // Frame slots: 0=base 1=count. The walk itself is a Call now, so both live across it.
    const int slotBase = 0;
    const int slotCount = 1;
    const int slotStackLow = 2;
    const int slotStackHigh = 3;

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // detached: write nothing
    _b.StoreLocal(slotBase, VReg.Scratch1);

    // No stop published yet (the entry stop parks before any breakpoint) -> empty trace.
    EmitDbgJumpIfNoStopPublished(VReg.Scratch1, VReg.Scratch2, emptyLabel);

    // Frame 0 = the exact stop-PC offset (already a validated `.text` code offset). BtFrames[0] = it,
    // and the shared walk then fills 1..N from the saved-rbp chain.
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, DbgOffStopPc);
    _b.StoreIndirect(VReg.Scratch1, DbgOffBtFrames, VReg.Scratch3);

    // The stopped frame's window comes from the thread the stop was taken ON — which since
    // P4d-GT-STACK may be a GREEN thread, whose stack is three orders of magnitude smaller than the
    // fallback window and whose topmost frame pointer sits at its very top. The park loop runs on that
    // same thread, so this processor's currentGt IS the stopped thread; a null P or a thread with no
    // recorded extent (a processor's inline main-thread GT, i.e. an OS thread stack) falls back to the
    // sane window, exactly as the per-green-thread walk does.
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffStopSp);
    EmitLoadCurrentGtOrZero(VReg.Scratch0);
    EmitDbgStackWindow(VReg.Scratch0, VReg.Scratch2, slotStackLow, slotStackHigh);

    _b.LoadLocal(VReg.Scratch1, slotBase);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, DbgOffStopFp);
    _b.LoadLocal(VReg.Arg1, slotStackLow);
    _b.LoadLocal(VReg.Arg2, slotStackHigh);
    _b.MovRegImm(VReg.Arg3, 1);                           // frame 0 is already written
    _b.Call("__dbg_walk_frames");
    _b.StoreLocal(slotCount, VReg.Ret);
    _b.Jump(storeCountLabel);

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
  /// __dbg_text_offset(absAddr) -> the `.text` CODE OFFSET of <c>absAddr</c>, or 0 when it is null or
  /// outside `.text`. The ONE place an absolute code address becomes an offset the sidecar can resolve,
  /// used by the frame walk (a return address off a stack) and by the green-thread enumeration (a
  /// spawned thread's entry function) — two callers that must not come to disagree about what counts as
  /// a real code address.
  ///
  /// Zero is an unambiguous "no" because code offset 0 is `mrt_start` itself, the runtime's first byte,
  /// which is neither a return address nor a user entry point (the same reasoning that lets
  /// <c>__dbg_bp_addr[i] == 0</c> mean "free slot"). Contains no Call, so it uses scratch freely.
  /// </summary>
  private void EmitDbgTextOffset() {
    _b.FunctionStart("__dbg_text_offset", 1, 0x40);

    var noneLabel = UniqueLabel("dbg_text_off_none");

    _b.LoadLocal(VReg.Scratch2, 0);                       // absAddr
    _b.JumpIfZero(VReg.Scratch2, noneLabel);

    // off = absAddr − &mrt_start, rejected against &symtable − &mrt_start (the exact `.text` bound the
    // panic symbolizer trusts). The compare is UNSIGNED, so an address BELOW the text base reads as a
    // huge offset and is rejected by that same single test.
    _b.LeaFuncAddr(VReg.Scratch3, "mrt_start");
    _b.SubRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.LeaSymdata(VReg.Scratch1, _b.SymbolTableLabel);
    _b.SubRegReg(VReg.Scratch1, VReg.Scratch3);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, noneLabel);

    _b.MovRegReg(VReg.Ret, VReg.Scratch2);
    _b.FunctionEnd();

    _b.DefineLabel(noneLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_frame_ra(fp, stackLow, stackHigh) -> the `.text` CODE OFFSET of the return address saved in
  /// the frame at <c>fp</c>, or 0 when this is not a frame worth walking.
  ///
  /// This is mrt_fault_backtrace's per-frame validation, extracted so there is exactly ONE statement of
  /// what makes a frame trustworthy: a non-null fp inside the caller's stack window, and a return
  /// address that resolves through <see cref="EmitDbgTextOffset"/>.
  ///
  /// The stack window is the CALLER's business, and parameterising it is the whole point: the
  /// stopped-thread walk trusts the fault handler's fixed window, while a parked green thread carries
  /// its own exact extent in its own struct.
  /// </summary>
  private void EmitDbgFrameRa() {
    _b.FunctionStart("__dbg_frame_ra", 3, 0x40);

    var noneLabel = UniqueLabel("dbg_frame_ra_none");

    EmitDbgJumpIfNotAFrame(FrameArgSlotFp, FrameArgSlotStackLow, FrameArgSlotStackHigh, noneLabel);

    _b.LoadLocal(VReg.Scratch1, FrameArgSlotFp);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, 8);         // ra = [fp + 8]
    _b.Call("__dbg_text_offset");
    _b.FunctionEnd();

    _b.DefineLabel(noneLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  // The (fp, stackLow, stackHigh) triple every frame primitive takes, in the order it is passed —
  // named once so the two functions and the shared test below cannot disagree about which slot is which.
  private const int FrameArgSlotFp = 0;
  private const int FrameArgSlotStackLow = 1;
  private const int FrameArgSlotStackHigh = 2;

  /// <summary>
  /// Jump to <paramref name="badLabel"/> unless the frame pointer in frame slot <paramref name="slotFp"/>
  /// is one we may DEREFERENCE: non-null and wholly inside [stackLow, stackHigh). Emitted inline (like
  /// <see cref="EmitDbgStackWindow"/>) rather than called, so asking it twice costs no extra trap-handler
  /// frame — depth on that path is what P4d-GT-STACK's reserve is spent on.
  ///
  /// It is the ONE statement of what makes a frame link readable, and both readers of a frame need it:
  /// <see cref="EmitDbgFrameRa"/> reads the return address at [fp + 8], and <see cref="EmitDbgFrameNext"/>
  /// reads the saved frame pointer at [fp] and must vouch for the ANSWER as well as the input — a
  /// frame pointer handed to the driver becomes the base of a `print`, which reads memory at [fp + slot].
  ///
  /// Clobbers Scratch1/Scratch2.
  /// </summary>
  private void EmitDbgJumpIfNotAFrame(int slotFp, int slotStackLow, int slotStackHigh, string badLabel) {
    _b.LoadLocal(VReg.Scratch1, slotFp);
    _b.JumpIfZero(VReg.Scratch1, badLabel);
    _b.LoadLocal(VReg.Scratch2, slotStackLow);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Below, badLabel);                 // below the stack's low bound
    // The frame link is TWO words — [fp] and [fp + 8] — so the high bound has to leave room for both,
    // not merely for fp itself. `fp < high` alone would read a qword AT high, which is one past the end
    // of a green thread's stack, where the spawn trampoline's frame pointer sits.
    _b.LoadLocal(VReg.Scratch2, slotStackHigh);
    _b.AddRegImm(VReg.Scratch1, GtLayout.FrameLinkBytes);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Above, badLabel);                 // fp + 16 > high: the link runs off the end
  }

  /// <summary>
  /// __dbg_frame_next(fp, stackLow, stackHigh) -> the CALLER's frame pointer saved at [fp], or 0 when
  /// the chain ends there.
  ///
  /// The other half of <see cref="EmitDbgFrameRa"/>: that one answers "which code does this frame return
  /// to", this one answers "whose frame is that code running in" — and the pair is what a debugger needs,
  /// because a return address names a function and only a frame pointer can read its locals.
  ///
  /// Both the walk's ADVANCE and the per-thread top-frame record come through here, so "when does a
  /// chain end" is stated once. The answer must ASCEND (a stack grows down, so a caller's frame is at a
  /// higher address; a non-ascending link is a corrupt or terminating chain) and must itself be a frame
  /// we could dereference — which is the load-bearing half here rather than a formality, since this
  /// result is published to the driver and read as the base of a `print`.
  /// </summary>
  private void EmitDbgFrameNext() {
    _b.FunctionStart("__dbg_frame_next", 3, 0x40);

    var noneLabel = UniqueLabel("dbg_frame_next_none");

    // Frame slot 3 holds the candidate, so the shared test can be asked about it the same way.
    const int slotNext = 3;

    EmitDbgJumpIfNotAFrame(FrameArgSlotFp, FrameArgSlotStackLow, FrameArgSlotStackHigh, noneLabel);

    _b.LoadLocal(VReg.Scratch1, FrameArgSlotFp);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, 0);     // next = [fp]
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.JumpIf(Condition.BelowEqual, noneLabel);           // not ascending: the chain ends here
    _b.StoreLocal(slotNext, VReg.Scratch2);

    EmitDbgJumpIfNotAFrame(slotNext, FrameArgSlotStackLow, FrameArgSlotStackHigh, noneLabel);

    _b.LoadLocal(VReg.Ret, slotNext);
    _b.FunctionEnd();

    _b.DefineLabel(noneLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_walk_frames(fp, stackLow, stackHigh, startIndex) -> the new frame count. Walks the saved-rbp
  /// chain from <c>fp</c>, appending each frame's return-address code offset to DbgOffBtFrames from
  /// <c>startIndex</c>, and stops at <see cref="DbgMaxBacktraceFrames"/>.
  ///
  /// The ONE frame-chain walk in the agent: the stopped-thread backtrace enters it with the fault
  /// handler's window and startIndex 1 (frame 0 being the exact stop PC), and the per-green-thread
  /// backtrace enters it with that thread's own stack extent and startIndex 0. Writing a second walk for
  /// the green-thread case is exactly the duplication that would let the two disagree about what a
  /// trustworthy frame is.
  ///
  /// The ascending guard ends the walk AFTER storing the current frame, so a corrupt chain yields a
  /// short trace rather than a wild one. Every value lives in a frame slot because the per-frame
  /// validation is a Call, and on arm64 every register is call-clobbered.
  /// </summary>
  private void EmitDbgWalkFrames() {
    _b.FunctionStart("__dbg_walk_frames", 4, 0x60);

    var walkLabel = UniqueLabel("dbg_walk_loop");
    var doneLabel = UniqueLabel("dbg_walk_done");

    // Frame slots: 0..2 = the window params, 3 = the running count (seeded with startIndex), 4 = the
    // offset the validation just returned.
    const int slotFp = 0;
    const int slotStackLow = 1;
    const int slotStackHigh = 2;
    const int slotCount = 3;
    const int slotOffset = 4;

    _b.DefineLabel(walkLabel);
    _b.LoadLocal(VReg.Scratch0, slotCount);
    _b.CmpRegImm(VReg.Scratch0, DbgMaxBacktraceFrames);
    _b.JumpIf(Condition.AboveEqual, doneLabel);           // array full

    _b.LoadLocal(VReg.Arg0, slotFp);
    _b.LoadLocal(VReg.Arg1, slotStackLow);
    _b.LoadLocal(VReg.Arg2, slotStackHigh);
    _b.Call("__dbg_frame_ra");
    _b.JumpIfZero(VReg.Ret, doneLabel);                   // not a frame worth walking
    _b.StoreLocal(slotOffset, VReg.Ret);

    // BtFrames[count] = off. The base is reloaded (the call clobbered it) and re-checked: a shutdown
    // that raced this walk unmaps the segment, and a store through it would fault rather than truncate.
    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);
    _b.LoadLocal(VReg.Scratch2, slotCount);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch3, slotOffset);
    _b.StoreIndirect(VReg.Scratch1, DbgOffBtFrames, VReg.Scratch3);

    _b.LoadLocal(VReg.Scratch0, slotCount);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(slotCount, VReg.Scratch0);

    // Advance through the ONE statement of where a chain ends, shared with the per-thread top-frame
    // record, so a corrupt link cannot end the walk here and be trusted there.
    _b.LoadLocal(VReg.Arg0, slotFp);
    _b.LoadLocal(VReg.Arg1, slotStackLow);
    _b.LoadLocal(VReg.Arg2, slotStackHigh);
    _b.Call("__dbg_frame_next");
    _b.JumpIfZero(VReg.Ret, doneLabel);
    _b.StoreLocal(slotFp, VReg.Ret);
    _b.Jump(walkLabel);

    _b.DefineLabel(doneLabel);
    _b.LoadLocal(VReg.Ret, slotCount);
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

  // =========================================================================
  // Green-thread visibility (P4d-2a) — enumeration and per-thread frames
  // =========================================================================
  //
  // ⚠ THE ENUMERATION IS A UNION, NOT A LIST WALK. `__gt_all_head` is written in exactly two places —
  // the prepend in __gt_spawn and the unlink in __gt_trampoline — so it holds SPAWNED threads only.
  // Each processor's main-thread green thread lives INLINE in its P at POffMainThread and is
  // initialised directly, so it is never linked in. A list built from `__gt_all_head` alone therefore
  // omits main's own thread, which is the one the debugger is usually STOPPED ON — the most visible
  // wrong answer this command can give. Both halves are walked, and the acceptance golden stops in
  // `main` precisely so the omitted half is the stopped one.
  //
  // ⚠ THE WALK IS DELIBERATELY UNLOCKED. `__gt_all_head` is guarded by a critical section (x86) /
  // os_unfair_lock (arm64), and the contract for this rung asked for the walk to take it. It does not,
  // for a reason that outranks the consistency it would buy: on POSIX the debug trap handler is a
  // SIGTRAP handler, and taking a lock in a signal handler is the classic self-deadlock — the
  // interrupted thread may already hold it, and os_unfair_lock is not recursive. The agent's stated
  // property is async-signal-safety, and the runtime's own precedent agrees: `__gt_cleanup` walks this
  // same list unlocked, and mrt_fault_backtrace walks stacks unlocked. So this follows the handler-context
  // discipline the codebase already uses, and what it actually provides is a BOUND — DbgMaxGreenThreads
  // — which is what keeps a chain another M is mutating from spinning the debuggee.
  //
  // Be precise about what is NOT provided: the NODES are not validated. A node unlinked and recycled
  // while we walk yields a garbage stackBase/stackSize, and the frame walk then bounds itself with a
  // window read from that same untrusted memory. What keeps that from being a wild read today is the
  // runtime's own ordering — __gt_trampoline unlinks a thread from this list BEFORE its stack is freed,
  // so a node still on the list still owns its stack — plus the `.text` range check on every return
  // address. That is a narrow accepted risk, not a validated walk, and calling it one would be the kind
  // of overstated comment a later reader trusts.

  /// <summary>
  /// __dbg_p_at(index) -> the ACTIVE processor at <c>index</c>, or 0.
  ///
  /// The one place "is there a live processor here" is decided, so the two walks that need it — the
  /// main-thread half of the enumeration and the on-cpu test — cannot come to filter differently. A P
  /// struct is allocated for every possible processor at startup, so the POffStatus filter is not
  /// defensive: an unused P's inline main-thread GT is a zeroed struct, not a thread.
  /// </summary>
  private void EmitDbgProcAt() {
    _b.FunctionStart("__dbg_p_at", 1, 0x40);

    var missLabel = UniqueLabel("dbg_p_at_miss");

    _b.LoadGlobal(VReg.Scratch1, "__sched_procs");
    _b.JumpIfZero(VReg.Scratch1, missLabel);              // scheduler not initialised
    _b.LoadLocal(VReg.Scratch2, 0);                       // index
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadIndirect(VReg.Ret, VReg.Scratch1, 0);          // procs[index]
    _b.JumpIfZero(VReg.Ret, missLabel);

    _b.LoadIndirect(VReg.Scratch2, VReg.Ret, GtLayout.POffStatus);
    _b.CmpRegImm(VReg.Scratch2, GtLayout.PStatusActive);
    _b.JumpIf(Condition.NotEqual, missLabel);
    _b.FunctionEnd();

    _b.DefineLabel(missLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_gt_on_cpu(gt) -> 1 when <c>gt</c> is the currentGt of some ACTIVE processor, else 0.
  ///
  /// ⚠ THIS IS THE PARK GATE, and it is the only safe form of it. A green thread's saved rsp/rbp are
  /// meaningful ONLY while no M is executing on its stack: __gt_context_switch saves them and only THEN
  /// republishes currentGt, so a thread that is no processor's currentGt has already been saved off its
  /// stack. Walking a running thread's saved chain is the "two Ms run one GT on two different stacks"
  /// bug class the scheduler names in __gt_timer_check, in its read-only form — it does not corrupt the
  /// debuggee, it produces a PLAUSIBLE-LOOKING WRONG BACKTRACE, which is worse than refusing.
  ///
  /// It deliberately does NOT gate on GtOffIoYielded, though the runtime's own park gate does and this
  /// rung's contract said to. That flag is not a general "parked" signal: __gt_spawn initialises it to 1
  /// ("start as yielded so the first __io_complete_gt doesn't spin") and nothing clears it when a thread
  /// is resumed, so a RUNNING thread carries 1 and gating on it would admit exactly the case it looks
  /// like it excludes. It is only meaningful in the runtime's own narrow use — an I/O submit clears it
  /// across the park it is about to perform. Processor ownership is the fact the debugger actually needs
  /// and is directly observable, so that is what is asked.
  /// </summary>
  private void EmitDbgGtOnCpu() {
    _b.FunctionStart("__dbg_gt_on_cpu", 1, 0x60);

    var loopLabel = UniqueLabel("dbg_on_cpu_loop");
    var nextLabel = UniqueLabel("dbg_on_cpu_next");
    var foundLabel = UniqueLabel("dbg_on_cpu_found");
    var missLabel = UniqueLabel("dbg_on_cpu_miss");

    const int slotGt = 0;
    const int slotIndex = 1;

    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(slotIndex, VReg.Scratch0);

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch0, slotIndex);
    _b.LoadGlobal(VReg.Scratch1, "__sched_num_procs");
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, missLabel);

    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__dbg_p_at");
    _b.JumpIfZero(VReg.Ret, nextLabel);

    _b.LoadIndirect(VReg.Scratch1, VReg.Ret, GtLayout.POffCurrentGt);
    _b.LoadLocal(VReg.Scratch2, slotGt);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Equal, foundLabel);

    _b.DefineLabel(nextLabel);
    _b.LoadLocal(VReg.Scratch0, slotIndex);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(slotIndex, VReg.Scratch0);
    _b.Jump(loopLabel);

    _b.DefineLabel(foundLabel);
    _b.MovRegImm(VReg.Ret, 1);
    _b.FunctionEnd();

    _b.DefineLabel(missLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_gt_frames(gt) -> the frame count written to DbgOffBtFrames for a PARKED green thread.
  ///
  /// The thread LIST (which wants only the top frame) and the per-thread BACKTRACE both come through
  /// here, so they cannot disagree; which window to trust is <see cref="EmitDbgStackWindow"/>'s answer,
  /// shared with the stopped-thread backtrace for the same reason.
  ///
  /// It does NOT check parked-ness: the callers do, because they have different things to say about a
  /// running thread. Seeding the walk from the saved rbp means frame 0 is the return address into
  /// whatever called the parking runtime function — i.e. the user code that is waiting — which is the
  /// frame a reader actually wants, and it needs the call-site bias like any return address.
  /// </summary>
  private void EmitDbgGtFrames() {
    _b.FunctionStart("__dbg_gt_frames", 1, 0x60);

    const int slotGt = 0;
    const int slotStackLow = 1;
    const int slotStackHigh = 2;

    // ⚠ WHY THE FALLBACK IS SAFE HERE, written where it is DEPENDED ON. For a thread with no recorded
    // extent EmitDbgStackWindow seeds a 64 MiB window from the stack pointer passed in — here gt->rsp,
    // a SAVED one — and the park gate above only established that no processor is RUNNING this thread,
    // not that its stack still exists. For a processor's inline thread that holds because of two facts
    // elsewhere: a P struct is VirtualAlloc'd zeroed, so an unused P's inline mainThread has rbp == 0
    // and the walk stops at once; and a P is set PStatusUnused only on the shutdown path
    // (X86CodeEmitter.Runtime.cs / ARM64CodeEmitter.Runtime.cs worker exit), so an ACTIVE P never
    // carries a stale mainThread.rbp pointing into an OS-thread stack that has gone away. Make workers
    // exit when idle — a natural future optimisation — and this becomes a wild read inside a trap
    // handler. The rbp == 0 check in __dbg_frame_ra is the only thing standing behind it.
    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, GtLayout.GtOffRsp);
    EmitDbgStackWindow(VReg.Scratch0, VReg.Scratch2, slotStackLow, slotStackHigh);

    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, GtLayout.GtOffRbp);
    _b.LoadLocal(VReg.Arg1, slotStackLow);
    _b.LoadLocal(VReg.Arg2, slotStackHigh);
    _b.ZeroReg(VReg.Arg3);                                // this thread's frame 0 is a return address
    _b.Call("__dbg_walk_frames");
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_gt_record(gt, procId) — append one green thread to the record array, or set the TRUNCATED
  /// flag when the array is full. <c>procId</c> is the owning processor index for a P's inline
  /// main-thread GT, or <see cref="DbgGtNotAProc"/> for a spawned one.
  ///
  /// Called from BOTH halves of the union so a main-thread thread and a spawned thread are described
  /// identically — every field, including the top frame, is decided once here.
  /// </summary>
  private void EmitDbgGtRecord() {
    _b.FunctionStart("__dbg_gt_record", 2, 0x80);

    var truncatedLabel = UniqueLabel("dbg_gt_rec_truncated");
    var stoppedTopLabel = UniqueLabel("dbg_gt_rec_stopped_top");
    var parkedTopLabel = UniqueLabel("dbg_gt_rec_parked_top");
    var noTopLabel = UniqueLabel("dbg_gt_rec_no_top");
    var storeTopLabel = UniqueLabel("dbg_gt_rec_store_top");
    var holdPendingLabel = UniqueLabel("dbg_gt_rec_hold_pending");
    var holdNoneLabel = UniqueLabel("dbg_gt_rec_hold_none");
    var holdStoreLabel = UniqueLabel("dbg_gt_rec_hold_store");
    var doneLabel = UniqueLabel("dbg_gt_rec_done");

    // Frame slots: 0=gt 1=procId (params) 2=base 3=rec 4=onCpu 5=topKind 6=topPc 7=topFp,
    // 8/9 = the stack window the parked top-frame arm walks in.
    const int slotGt = 0;
    const int slotProc = 1;
    const int slotBase = 2;
    const int slotRec = 3;
    const int slotOnCpu = 4;
    const int slotTopKind = 5;
    const int slotTopPc = 6;
    const int slotTopFp = 7;
    const int slotStackLow = 8;
    const int slotStackHigh = 9;

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // detached: nothing to write into
    _b.StoreLocal(slotBase, VReg.Scratch1);

    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffGtCount);
    _b.CmpRegImm(VReg.Scratch2, DbgMaxGreenThreads);
    _b.JumpIf(Condition.AboveEqual, truncatedLabel);

    EmitDbgGtRecAddr(VReg.Scratch3, VReg.Scratch2, VReg.Scratch1, VReg.Scratch0);
    _b.StoreLocal(slotRec, VReg.Scratch3);

    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.StoreIndirect(VReg.Scratch3, DbgGtRecOffHandle, VReg.Scratch0);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, GtLayout.GtOffStatus);
    _b.StoreIndirect(VReg.Scratch3, DbgGtRecOffStatus, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch2, slotProc);
    _b.StoreIndirect(VReg.Scratch3, DbgGtRecOffProc, VReg.Scratch2);

    // Entry function, as a validated code offset. A P's inline main-thread GT has no entry function at
    // all (its funcPtr is a zeroed field), which __dbg_text_offset reports as 0 — the same "none" the
    // driver reads from the Proc field, so the two cannot contradict each other.
    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, GtLayout.GtOffFuncPtr);
    _b.Call("__dbg_text_offset");
    _b.LoadLocal(VReg.Scratch3, slotRec);
    _b.StoreIndirect(VReg.Scratch3, DbgGtRecOffEntryPc, VReg.Ret);

    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.Call("__dbg_gt_on_cpu");
    _b.StoreLocal(slotOnCpu, VReg.Ret);
    _b.LoadLocal(VReg.Scratch3, slotRec);
    _b.StoreIndirect(VReg.Scratch3, DbgGtRecOffOnCpu, VReg.Ret);

    // Top frame. The STOPPED thread's is exact (the agent published its PC), and it is checked first
    // because that thread is also on-cpu — it is the one on-cpu thread whose stack is standing still.
    _b.LoadLocal(VReg.Scratch1, slotBase);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffGtStopped);
    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch0);
    _b.JumpIf(Condition.Equal, stoppedTopLabel);

    // Running on some processor: its saved rsp/rbp are stale, so there is no frame to report.
    _b.LoadLocal(VReg.Scratch2, slotOnCpu);
    _b.JumpIfNonZero(VReg.Scratch2, noTopLabel);
    _b.Jump(parkedTopLabel);

    _b.DefineLabel(stoppedTopLabel);
    // A stop event must actually have been published, or the recorded PC is a zero nobody set.
    EmitDbgJumpIfNoStopPublished(VReg.Scratch1, VReg.Scratch2, noTopLabel);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffStopPc);
    _b.StoreLocal(slotTopPc, VReg.Scratch2);
    // The stopped thread's frame pointer is the one the stop event itself published — the SAME word
    // `print` and `locals` already read the stopped frame's locals through, so selecting the stopped
    // thread with `gt <id>` can only ever agree with not having selected anything.
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffStopFp);
    _b.StoreLocal(slotTopFp, VReg.Scratch2);
    _b.MovRegImm(VReg.Scratch2, DbgGtTopFrameExact);
    _b.StoreLocal(slotTopKind, VReg.Scratch2);
    _b.Jump(storeTopLabel);

    _b.DefineLabel(parkedTopLabel);
    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.Call("__dbg_gt_frames");
    _b.JumpIfZero(VReg.Ret, noTopLabel);                  // never started, or an unwalkable chain
    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffBtFrames);
    _b.StoreLocal(slotTopPc, VReg.Scratch2);
    _b.MovRegImm(VReg.Scratch2, DbgGtTopFrameReturn);
    _b.StoreLocal(slotTopKind, VReg.Scratch2);

    // The frame pointer that return address belongs to is the NEXT link up from the saved one, and it
    // is validated by the same rule the walk itself just used — it has to be, because the driver reads
    // a `print` operand at [fp + slot] and nothing downstream of here re-checks it. A window this walk
    // could not vouch for yields 0, which the driver reports as "no frame to read" rather than guessing.
    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, GtLayout.GtOffRsp);
    EmitDbgStackWindow(VReg.Scratch0, VReg.Scratch2, slotStackLow, slotStackHigh);
    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, GtLayout.GtOffRbp);
    _b.LoadLocal(VReg.Arg1, slotStackLow);
    _b.LoadLocal(VReg.Arg2, slotStackHigh);
    _b.Call("__dbg_frame_next");
    _b.StoreLocal(slotTopFp, VReg.Ret);
    _b.Jump(storeTopLabel);

    _b.DefineLabel(noTopLabel);
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreLocal(slotTopPc, VReg.Scratch2);
    _b.StoreLocal(slotTopFp, VReg.Scratch2);
    _b.MovRegImm(VReg.Scratch2, DbgGtTopFrameNone);
    _b.StoreLocal(slotTopKind, VReg.Scratch2);

    _b.DefineLabel(storeTopLabel);
    _b.LoadLocal(VReg.Scratch3, slotRec);
    _b.LoadLocal(VReg.Scratch2, slotTopKind);
    _b.StoreIndirect(VReg.Scratch3, DbgGtRecOffTopKind, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch2, slotTopPc);
    _b.StoreIndirect(VReg.Scratch3, DbgGtRecOffTopPc, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch2, slotTopFp);
    _b.StoreIndirect(VReg.Scratch3, DbgGtRecOffTopFp, VReg.Scratch2);

    // Whether the DEBUGGER owns this thread, decided by the SAME predicate the scheduler's dequeue
    // filter asks — so what the listing says and what the scheduler does cannot disagree. "Held" and
    // "pending" differ only by whether a processor is executing it right now, which is exactly the
    // cooperative limit: a hold reaches a thread at its next scheduler interaction, and a thread that is
    // running has not had one yet.
    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.Call("__dbg_gt_should_hold");
    _b.LoadLocal(VReg.Scratch3, slotRec);
    _b.JumpIfZero(VReg.Ret, holdNoneLabel);
    _b.LoadLocal(VReg.Scratch2, slotOnCpu);
    _b.JumpIfNonZero(VReg.Scratch2, holdPendingLabel);
    _b.MovRegImm(VReg.Scratch2, DbgGtHoldHeld);
    _b.Jump(holdStoreLabel);

    _b.DefineLabel(holdPendingLabel);
    _b.MovRegImm(VReg.Scratch2, DbgGtHoldPending);
    _b.Jump(holdStoreLabel);

    _b.DefineLabel(holdNoneLabel);
    _b.MovRegImm(VReg.Scratch2, DbgGtHoldNone);

    _b.DefineLabel(holdStoreLabel);
    _b.StoreIndirect(VReg.Scratch3, DbgGtRecOffHold, VReg.Scratch2);

    // Published LAST, so a count the driver reads always covers a fully-written record.
    _b.LoadLocal(VReg.Scratch1, slotBase);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffGtCount);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, DbgOffGtCount, VReg.Scratch2);
    _b.Jump(doneLabel);

    _b.DefineLabel(truncatedLabel);
    _b.MovRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, DbgOffGtTruncated, VReg.Scratch2);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_gt_scan(findHandle) -> 1 if <c>findHandle</c> is a live green thread, else 0. ALWAYS
  /// republishes the whole record array as it goes; pass 0 to enumerate without asking about anything.
  ///
  /// Enumerating and "is this handle still live" are ONE walk on purpose. The per-thread backtrace has
  /// to re-validate its target — the debuggee's other processors kept running while this one was parked,
  /// so the thread named by a record may have completed and had its struct recycled onto a free list —
  /// and a second walk written for that question would be a second answer to "what green threads exist".
  ///
  /// It also publishes DbgOffGtStopped: the trapping M's own currentGt, read through the SAME
  /// LoadCurrentP the runtime uses everywhere. That is the fact a stop event structurally cannot carry —
  /// a PC/SP/FP names a stopped THREAD, not a stopped GREEN thread — and it is why the trap thunks did
  /// not need a line for any of this. A thread that owns no processor (LoadCurrentP is null there)
  /// publishes 0, and the driver says so rather than naming a thread.
  /// </summary>
  private void EmitDbgGtScan() {
    _b.FunctionStart("__dbg_gt_scan", 1, 0x80);

    var procLoopLabel = UniqueLabel("dbg_gt_scan_ploop");
    var procNextLabel = UniqueLabel("dbg_gt_scan_pnext");
    var spawnedInitLabel = UniqueLabel("dbg_gt_scan_spawned_init");
    var spawnedLoopLabel = UniqueLabel("dbg_gt_scan_spawned");
    var truncatedLabel = UniqueLabel("dbg_gt_scan_truncated");
    var doneLabel = UniqueLabel("dbg_gt_scan_done");

    // Frame slots: 0=findHandle (param) 1=base 2=index/nodes 3=gt 4=found.
    const int slotFind = 0;
    const int slotBase = 1;
    const int slotCursor = 2;
    const int slotGt = 3;
    const int slotFound = 4;

    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(slotFound, VReg.Scratch0);

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // detached: nothing to publish into
    _b.StoreLocal(slotBase, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch1, DbgOffGtCount, VReg.Scratch0);
    _b.StoreIndirect(VReg.Scratch1, DbgOffGtTruncated, VReg.Scratch0);

    // The stopped green thread = this M's currentGt. Published BEFORE any record is written, because
    // __dbg_gt_record reads it back to decide whose top frame is the exact stopped PC.
    EmitLoadCurrentGtOrZero(VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch1, slotBase);
    _b.StoreIndirect(VReg.Scratch1, DbgOffGtStopped, VReg.Scratch2);

    // --- Half 1: each ACTIVE processor's inline main-thread green thread ---
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(slotCursor, VReg.Scratch0);

    _b.DefineLabel(procLoopLabel);
    _b.LoadLocal(VReg.Scratch0, slotCursor);
    _b.LoadGlobal(VReg.Scratch1, "__sched_num_procs");
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, spawnedInitLabel);

    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__dbg_p_at");
    _b.JumpIfZero(VReg.Ret, procNextLabel);

    _b.MovRegReg(VReg.Scratch1, VReg.Ret);
    _b.AddRegImm(VReg.Scratch1, GtLayout.POffMainThread);
    _b.StoreLocal(slotGt, VReg.Scratch1);

    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.LoadLocal(VReg.Arg1, slotCursor);                  // the processor it belongs to
    _b.Call("__dbg_gt_record");
    EmitDbgGtScanMatch(slotGt, slotFind, slotFound);

    _b.DefineLabel(procNextLabel);
    _b.LoadLocal(VReg.Scratch0, slotCursor);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(slotCursor, VReg.Scratch0);
    _b.Jump(procLoopLabel);

    // --- Half 2: the spawned-thread list ---
    // The cursor is reused as a NODE COUNT here: it is what bounds an unlocked walk of a list another M
    // may be mutating, and DbgMaxGreenThreads is the right bound because the array cannot hold more
    // anyway. Reaching it with the chain still going is reported as truncation, never as the end.
    _b.DefineLabel(spawnedInitLabel);
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(slotCursor, VReg.Scratch0);
    _b.LoadGlobal(VReg.Scratch1, "__gt_all_head");
    _b.StoreLocal(slotGt, VReg.Scratch1);

    _b.DefineLabel(spawnedLoopLabel);
    _b.LoadLocal(VReg.Scratch1, slotGt);
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // end of the list
    _b.LoadLocal(VReg.Scratch0, slotCursor);
    _b.CmpRegImm(VReg.Scratch0, DbgMaxGreenThreads);
    _b.JumpIf(Condition.AboveEqual, truncatedLabel);

    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.MovRegImm(VReg.Arg1, DbgGtNotAProc);
    _b.Call("__dbg_gt_record");
    EmitDbgGtScanMatch(slotGt, slotFind, slotFound);

    _b.LoadLocal(VReg.Scratch1, slotGt);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, GtLayout.GtOffAllNext);
    _b.StoreLocal(slotGt, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch0, slotCursor);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(slotCursor, VReg.Scratch0);
    _b.Jump(spawnedLoopLabel);

    _b.DefineLabel(truncatedLabel);
    _b.LoadLocal(VReg.Scratch1, slotBase);
    _b.MovRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, DbgOffGtTruncated, VReg.Scratch2);

    _b.DefineLabel(doneLabel);
    _b.LoadLocal(VReg.Ret, slotFound);
    _b.FunctionEnd();
  }

  /// <summary>
  /// Emit the "was this the handle we were asked about" test both halves of
  /// <see cref="EmitDbgGtScan"/> perform on the thread they just recorded. A find handle of 0 is the
  /// enumerate-only case and matches nothing, because 0 is never a live green thread.
  /// </summary>
  private void EmitDbgGtScanMatch(int slotGt, int slotFind, int slotFound) {
    var skipLabel = UniqueLabel("dbg_gt_scan_nomatch");

    _b.LoadLocal(VReg.Scratch1, slotFind);
    _b.JumpIfZero(VReg.Scratch1, skipLabel);
    _b.LoadLocal(VReg.Scratch2, slotGt);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.NotEqual, skipLabel);
    _b.MovRegImm(VReg.Scratch1, 1);
    _b.StoreLocal(slotFound, VReg.Scratch1);

    _b.DefineLabel(skipLabel);
  }

  /// <summary>
  /// __dbg_gt_backtrace() — fill DbgOffBtFrames/DbgOffBtCount for the green thread at record index
  /// DbgOffCmdArg, or REFUSE (<see cref="DbgCmdResultRefused"/>) and publish an empty trace.
  ///
  /// The argument is an INDEX INTO THE AGENT'S OWN LAST-PUBLISHED ARRAY, never a pointer. That is a
  /// security property, not a convenience: __dbg_set_bp already had to grow a bounds check so a driver
  /// could not turn a code offset into an arbitrary write, and a raw GreenThread* here would be the
  /// arbitrary-READ twin of it. The agent therefore only ever dereferences a thread it found itself.
  ///
  /// Two refusals, both real, and the driver words them apart from what it already knows: the thread
  /// COMPLETED while this processor was parked (re-validated by rescanning, because its struct may since
  /// have been recycled onto a free list), or it is RUNNING on another processor, where the saved rsp/rbp
  /// are stale and a walk would produce a plausible-looking wrong backtrace.
  /// </summary>
  private void EmitDbgGtBacktrace() {
    _b.FunctionStart("__dbg_gt_backtrace", 0, 0x80);

    var refusedLabel = UniqueLabel("dbg_gt_bt_refused");
    var doneLabel = UniqueLabel("dbg_gt_bt_done");

    // Frame slots: 0=base 1=gt.
    const int slotBase = 0;
    const int slotGt = 1;

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);
    _b.StoreLocal(slotBase, VReg.Scratch1);

    // Published empty up front, so every refusal below leaves an empty trace rather than whichever
    // frames the previous command happened to leave in the array.
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreIndirect(VReg.Scratch1, DbgOffBtCount, VReg.Scratch0);

    EmitDbgGtRecordHandleFromCmdArg(slotBase, slotGt, refusedLabel);

    // Still live? The rescan republishes the array, which is why the handle is read out FIRST.
    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.Call("__dbg_gt_scan");
    _b.JumpIfZero(VReg.Ret, refusedLabel);

    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.Call("__dbg_gt_on_cpu");
    _b.JumpIfNonZero(VReg.Ret, refusedLabel);            // the park gate

    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.Call("__dbg_gt_frames");
    _b.LoadLocal(VReg.Scratch1, slotBase);
    _b.StoreIndirect(VReg.Scratch1, DbgOffBtCount, VReg.Ret);
    _b.Jump(doneLabel);

    _b.DefineLabel(refusedLabel);
    EmitDbgStoreCmdResult(DbgCmdResultRefused);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // Green-thread control (P4d-2b) — a COOPERATIVE per-thread hold
  // =========================================================================
  //
  // ⭐ THERE IS NO PRIMITIVE THAT PARKS AN ARBITRARY GREEN THREAD, and OS thread suspension is refused
  // outright: `SuspendThread` can stop an M inside the allocator or holding the scheduler lock, which
  // trades a debugger feature for a deadlocked debuggee. So a hold is a REFUSAL TO SCHEDULE, taken at
  // the one place the scheduler decides what runs next.
  //
  // ⭐ THAT PLACE IS `__gt_dequeue`'s RESULT, and it is the whole design:
  //   * It is the ONE choke point, and the claim is DERIVED rather than enumerated: every
  //     `__gt_context_switch` whose `to` is a green thread takes it from a `__gt_dequeue` call a few
  //     instructions above, and every other switch targets `P->mainThread` — a processor's own inline
  //     scheduler thread, which is not a thread anyone can hold (see __dbg_gt_should_hold). Reviewed
  //     against all twelve dequeue callers (the worker loop, `__gt_await`, `__gt_try_await`,
  //     `__gt_yield`, `maxon_sleep`, `maxon_yield`'s main-thread arm, `__gt_cleanup`, and the
  //     net/pipe/io submit paths); a LIST here would be one more place to keep in step, and the list
  //     this comment first carried named five while there were eleven, then eleven while there were
  //     twelve. The DERIVATION above is what holds; the count is only ever a dated reading of it.
  //   * A thread caught there has NOT RUN YET, which is what makes a hold safe rather than racy: its
  //     saved rsp/rbp are the ones its last context switch wrote, so the debugger can walk it and read
  //     its locals; and it cannot complete out from under a hold, because completing requires running.
  //   * A thread that is ALREADY running has not reached that point and will not until it next
  //     interacts with the scheduler. That is the cooperative limit, and it is REPORTED
  //     (DbgGtHoldPending) rather than hidden behind a timeout that claims success.
  //
  // ⚠ TWO SIDES, ONE MECHANISM, AND THE SPLIT IS LOAD-BEARING. Everything above `__dbg_gt_hold_add` runs
  // in the TRAP HANDLER, servicing a driver command, and touches nothing but agent words — no lock, no
  // queue — because on POSIX that handler is a SIGTRAP handler. Everything from `__dbg_gt_hold_push`
  // down runs on an ORDINARY SCHEDULER THREAD inside `__gt_dequeue`, where taking the scheduler's own
  // lock is not merely allowed but required: the held chain is reachable from every processor.

  /// <summary>
  /// __dbg_gt_should_hold(gt) -> 1 when the debugger has asked for this thread to stop running.
  ///
  /// The ONE predicate, asked by BOTH the scheduler's dequeue filter (which acts on it) and the record
  /// the driver reads (which reports it). Two statements of it would let the listing say "held" about a
  /// thread the scheduler was about to run, which is the one wrong answer this whole rung must not give.
  ///
  /// A processor's INLINE scheduler thread is excluded first, and not defensively: it is never enqueued,
  /// so it can never reach the dequeue filter, so a hold on it is a promise nothing could keep. The test
  /// is `stackBase == 0` — the runtime's own spelling of "has no stack of its own" (__gt_stack_high,
  /// __io_complete_gt, __gt_signal_waiter), rather than a second opinion about what a scheduler thread is.
  ///
  /// ⭐ NO HOLD SURVIVES SCHEDULER SHUTDOWN, and that is a CORRECTNESS rule, not a courtesy. Once main
  /// has returned, `__gt_cleanup` cancels every live thread and then drains the run queue on the main
  /// thread — and a thread the debugger still owns is one that drain can never reach, because it is off
  /// every queue. `__gt_live_count` therefore never falls to zero, and cleanup's "threads alive, nothing
  /// runnable" arm waits on `__io_done_event` with an INFINITE timeout, for an I/O completion that no
  /// longer has anyone to produce it. MEASURED on this branch before the rule existed: `gt-park` on a
  /// green thread that main does NOT await wedged the debuggee at exit 2/2 (the driver had to kill it),
  /// against 1/8 for the identical program without the park. A hold is a tool for inspecting a RUNNING
  /// program; after main returns there is nothing left to hold it for, and the alternative to dropping it
  /// is a debuggee that never exits.
  /// </summary>
  private void EmitDbgGtShouldHold() {
    _b.FunctionStart("__dbg_gt_should_hold", 1, 0x40);

    var noLabel = UniqueLabel("dbg_should_hold_no");
    var yesLabel = UniqueLabel("dbg_should_hold_yes");

    const int slotGt = 0;

    _b.LoadLocal(VReg.Scratch1, slotGt);
    _b.JumpIfZero(VReg.Scratch1, noLabel);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, GtLayout.GtOffStackBase);
    _b.JumpIfZero(VReg.Scratch2, noLabel);                 // a processor's own scheduler thread

    _b.LoadGlobal(VReg.Scratch2, "__sched_shutdown_flag");
    _b.JumpIfNonZero(VReg.Scratch2, noLabel);              // shutting down: see the summary above

    _b.LoadGlobal(VReg.Scratch2, DbgGtHoldAllGlobal);
    _b.JumpIfNonZero(VReg.Scratch2, yesLabel);             // --stop-others: every thread is held

    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.Call("__dbg_gt_hold_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, noLabel);

    _b.DefineLabel(yesLabel);
    _b.MovRegImm(VReg.Ret, 1);
    _b.FunctionEnd();

    _b.DefineLabel(noLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  /// <summary>
  /// Emit the load of the green-thread record named by <see cref="DbgOffCmdArg"/> into
  /// <paramref name="slotHandle"/>, jumping to <paramref name="refusedLabel"/> when the index does not
  /// name a record in the array the agent last published. Expects the segment base in
  /// <paramref name="slotBase"/>.
  ///
  /// The argument is an INDEX INTO THE AGENT'S OWN ARRAY, never a pointer, for the reason
  /// <see cref="EmitDbgGtBacktrace"/> states: a raw GreenThread* from the driver would be an
  /// arbitrary-read (and here an arbitrary-WRITE, since a hold makes the agent store the value) primitive.
  /// All three per-thread commands resolve their argument the same way, stated once.
  ///
  /// Clobbers Scratch0..Scratch3.
  /// </summary>
  private void EmitDbgGtRecordHandleFromCmdArg(int slotBase, int slotHandle, string refusedLabel) {
    _b.LoadLocal(VReg.Scratch1, slotBase);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffCmdArg);   // record index
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, DbgOffGtCount);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.JumpIf(Condition.AboveEqual, refusedLabel);        // unsigned: a negative index is huge here

    EmitDbgGtRecAddr(VReg.Scratch3, VReg.Scratch2, VReg.Scratch1, VReg.Scratch0);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch3, DbgGtRecOffHandle);
    _b.JumpIfZero(VReg.Scratch2, refusedLabel);           // a record with no thread in it
    _b.StoreLocal(slotHandle, VReg.Scratch2);
  }

  /// <summary>
  /// __dbg_gt_hold_add() — record a hold for the green thread at record index
  /// <see cref="DbgOffCmdArg"/>, or REFUSE (<see cref="DbgCmdResultRefused"/>) when the table is full.
  ///
  /// It deliberately checks NEITHER that the thread is off-cpu NOR that it is not a scheduler thread:
  /// the driver refuses both before posting (it holds the very listing that answers them), and
  /// __dbg_gt_should_hold already declines to act on a scheduler thread. Re-testing here would be a
  /// second statement of a rule that already has one, and the two could then disagree about what the
  /// user was told.
  ///
  /// A FULL TABLE is the one thing only the agent knows, so it is the one thing it answers.
  /// </summary>
  private void EmitDbgGtHoldAdd() {
    _b.FunctionStart("__dbg_gt_hold_add", 0, 0x60);

    var refusedLabel = UniqueLabel("dbg_gt_hold_refused");
    var doneLabel = UniqueLabel("dbg_gt_hold_done");

    const int slotBase = 0;
    const int slotHandle = 1;

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);
    _b.StoreLocal(slotBase, VReg.Scratch1);

    EmitDbgGtRecordHandleFromCmdArg(slotBase, slotHandle, refusedLabel);

    // Already held: the request is satisfied, so it succeeds rather than consuming a second slot.
    _b.LoadLocal(VReg.Arg0, slotHandle);
    _b.Call("__dbg_gt_hold_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.GreaterEqual, doneLabel);

    _b.ZeroReg(VReg.Arg0);                                // 0 = a free slot
    _b.Call("__dbg_gt_hold_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, refusedLabel);              // table full

    EmitDbgGtHoldEntryAddr(VReg.Scratch1, VReg.Ret);
    _b.LoadLocal(VReg.Scratch3, slotHandle);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch3);
    _b.Jump(doneLabel);

    _b.DefineLabel(refusedLabel);
    EmitDbgStoreCmdResult(DbgCmdResultRefused);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_gt_release() — drop the hold on the green thread at record index
  /// <see cref="DbgOffCmdArg"/> and ring the readmit doorbell, or REFUSE when no hold names it.
  ///
  /// Clearing the table entry is not enough on its own: a thread the scheduler already CAUGHT is off
  /// every run queue and sitting on the held chain, where nothing will look at it again until a
  /// `__gt_dequeue` is told to. The doorbell is that telling, and it is set here rather than derived on
  /// the scheduler side so the common case — a hold in force with nothing to release — stays one load.
  /// </summary>
  private void EmitDbgGtRelease() {
    _b.FunctionStart("__dbg_gt_release", 0, 0x60);

    var refusedLabel = UniqueLabel("dbg_gt_release_refused");
    var doneLabel = UniqueLabel("dbg_gt_release_done");

    const int slotBase = 0;
    const int slotHandle = 1;

    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);
    _b.StoreLocal(slotBase, VReg.Scratch1);

    EmitDbgGtRecordHandleFromCmdArg(slotBase, slotHandle, refusedLabel);

    _b.LoadLocal(VReg.Arg0, slotHandle);
    _b.Call("__dbg_gt_hold_slot");
    _b.CmpRegImm(VReg.Ret, 0);
    _b.JumpIf(Condition.Less, refusedLabel);              // no hold names this thread

    EmitDbgGtHoldEntryAddr(VReg.Scratch1, VReg.Ret);
    _b.ZeroReg(VReg.Scratch3);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch3);

    EmitDbgGtRingReadmit();
    _b.Jump(doneLabel);

    _b.DefineLabel(refusedLabel);
    EmitDbgStoreCmdResult(DbgCmdResultRefused);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// Ring the readmit doorbell: "a hold was dropped — re-offer whatever you are holding". Emitted at
  /// both the places a hold can go away (one thread by name, and every thread when a `--stop-others`
  /// stop ends), so neither can drop a hold and leave the thread stranded on the held chain.
  /// Clobbers Scratch0.
  private void EmitDbgGtRingReadmit() {
    _b.MovRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal(DbgGtReadmitGlobal, VReg.Scratch0);
  }

  /// Emit <paramref name="dst"/> = &amp;<see cref="DbgGtHoldGlobal"/>[<paramref name="index"/>]. Written
  /// once because BOTH sides of a hold address one entry — `gt-park` writes the handle in and
  /// `gt-resume` writes a zero over it — and the element width is the sort of thing that gets changed
  /// in one of two places. <paramref name="index"/> is read before <paramref name="dst"/> is written,
  /// so the two may be the same register. Clobbers Scratch2.
  private void EmitDbgGtHoldEntryAddr(VReg dst, VReg index) {
    _b.MovRegReg(VReg.Scratch2, index);
    _b.ShlRegImm(VReg.Scratch2, DbgGtWordShift);
    _b.LeaGlobal(dst, DbgGtHoldGlobal);
    _b.AddRegReg(dst, VReg.Scratch2);
  }

  /// Emit the end of a `--stop-others` freeze: stop catching NEW threads, and ring the doorbell that
  /// hands back the ones already caught. BOTH halves are needed and neither is the other's duplicate,
  /// which is exactly why the pair is emitted from one place — the park loop leaves by two exits and a
  /// freeze dropped at only one of them is a debuggee frozen forever. Clobbers Scratch0/Scratch3.
  private void EmitDbgGtReleaseAllHolds() {
    _b.ZeroReg(VReg.Scratch3);
    _b.StoreGlobal(DbgGtHoldAllGlobal, VReg.Scratch3);
    EmitDbgGtRingReadmit();
  }

  /// <summary>
  /// Emit the push of the green thread in frame slot <paramref name="slotGt"/> onto the held chain,
  /// under the SCHEDULER'S OWN LOCK. Inline rather than a call because both users are already inside
  /// `__gt_dequeue` and the sequence is five instructions.
  ///
  /// The chain is linked through <c>GtOffNext</c>, which is safe for exactly the reason the per-P free
  /// list may use the same field: a thread on this chain is in no run queue. It is the SCHEDULER's lock
  /// rather than a lock of the agent's own because the chain is reachable from every processor and this
  /// code already runs where that lock is the ordinary one to take — the trap handler never comes here.
  ///
  /// Clobbers Scratch0..Scratch3 and everything <c>LockAcquire</c>/<c>LockRelease</c> clobber, so
  /// <paramref name="slotGt"/> is a frame slot and is re-loaded after the lock is taken.
  /// </summary>
  private void EmitDbgGtHoldPush(int slotGt) {
    _b.LockAcquire(_b.SchedLockLabel);
    _b.LoadLocal(VReg.Scratch1, slotGt);
    _b.LoadGlobal(VReg.Scratch2, DbgGtHeldHeadGlobal);
    _b.StoreIndirect(VReg.Scratch1, GtLayout.GtOffNext, VReg.Scratch2);
    _b.StoreGlobal(DbgGtHeldHeadGlobal, VReg.Scratch1);
    _b.LockRelease(_b.SchedLockLabel);
  }

  /// <summary>
  /// __dbg_gt_readmit_held() — hand every thread the debugger no longer wants held back to the
  /// scheduler.
  ///
  /// It runs on a SCHEDULER thread, from the top of `__gt_dequeue`, and that is the whole reason the
  /// trap handler never has to touch a queue: a release is a word the handler stores, and the work of
  /// re-enqueuing happens later, on a thread where `__gt_enqueue` — with its lock, its wake and its
  /// worker spawn — is an ordinary call.
  ///
  /// The chain is DETACHED under the lock in one swap and then walked PRIVATELY, so `__gt_enqueue` is
  /// never called with the lock held (it takes the same one) and two processors arriving together
  /// cannot both walk it: the second finds an empty chain and a cleared doorbell.
  ///
  /// Threads that are STILL held are pushed back rather than dropped, which is what makes releasing one
  /// thread out of several correct — and what stops this from becoming a churn loop that re-enqueues a
  /// held thread only to catch it again on the next line.
  /// </summary>
  private void EmitDbgGtReadmitHeld() {
    _b.FunctionStart("__dbg_gt_readmit_held", 0, 0x60);

    var loopLabel = UniqueLabel("dbg_readmit_loop");
    var stillHeldLabel = UniqueLabel("dbg_readmit_still_held");
    var nextLabel = UniqueLabel("dbg_readmit_next");
    var doneLabel = UniqueLabel("dbg_readmit_done");

    const int slotChain = 0;
    const int slotNode = 1;

    // Detach the whole chain and clear the doorbell in one critical section: from here the chain is
    // this processor's private list, so nothing below needs the lock except a push-back.
    _b.LockAcquire(_b.SchedLockLabel);
    _b.LoadGlobal(VReg.Scratch1, DbgGtHeldHeadGlobal);
    _b.StoreLocal(slotChain, VReg.Scratch1);
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreGlobal(DbgGtHeldHeadGlobal, VReg.Scratch2);
    _b.StoreGlobal(DbgGtReadmitGlobal, VReg.Scratch2);
    _b.LockRelease(_b.SchedLockLabel);

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch1, slotChain);
    _b.JumpIfZero(VReg.Scratch1, doneLabel);
    _b.StoreLocal(slotNode, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, GtLayout.GtOffNext);
    _b.StoreLocal(slotChain, VReg.Scratch2);
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch1, GtLayout.GtOffNext, VReg.Scratch2);

    _b.LoadLocal(VReg.Arg0, slotNode);
    _b.Call("__dbg_gt_should_hold");
    _b.JumpIfNonZero(VReg.Ret, stillHeldLabel);

    _b.LoadLocal(VReg.Arg0, slotNode);
    _b.Call("__gt_enqueue");
    _b.Jump(nextLabel);

    _b.DefineLabel(stillHeldLabel);
    EmitDbgGtHoldPush(slotNode);

    _b.DefineLabel(nextLabel);
    _b.Jump(loopLabel);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_gt_dequeue_filtered() -> the next green thread this processor may RUN, or 0.
  ///
  /// `__gt_dequeue`'s answer, minus every thread the debugger is holding. Threads it declines are set
  /// aside on the held chain rather than put back, because putting one back would hand it straight to
  /// the next dequeue and spin the processor; and set-aside is safe precisely because the caught thread
  /// has not run, so nothing else refers to it.
  ///
  /// The readmit check is FIRST, so a thread released while the target was parked is back in the run
  /// queue before this processor decides there is nothing to do — which is what keeps a `gt-resume`
  /// followed by `continue` from wedging a program that is waiting on the released thread.
  ///
  /// SHUTDOWN IS THE SECOND DOORBELL, and it has to be READ rather than rung: nothing calls the agent
  /// when the scheduler starts winding down, so a thread already ON the held chain has no other way
  /// back — and `__gt_cleanup`'s drain cannot finish without it (see __dbg_gt_should_hold, which stops
  /// holding at the same moment, so this hands each thread back exactly once).
  /// </summary>
  private void EmitDbgGtDequeueFiltered() {
    _b.FunctionStart("__dbg_gt_dequeue_filtered", 0, 0x60);

    var loopLabel = UniqueLabel("dbg_deq_filter_loop");
    var readmitLabel = UniqueLabel("dbg_deq_filter_readmit");
    var noReadmitLabel = UniqueLabel("dbg_deq_filter_no_readmit");
    var doneLabel = UniqueLabel("dbg_deq_filter_done");

    const int slotGt = 0;

    _b.LoadGlobal(VReg.Scratch1, DbgGtReadmitGlobal);
    _b.JumpIfNonZero(VReg.Scratch1, readmitLabel);
    _b.LoadGlobal(VReg.Scratch1, "__sched_shutdown_flag");
    _b.JumpIfZero(VReg.Scratch1, noReadmitLabel);

    _b.DefineLabel(readmitLabel);
    _b.Call("__dbg_gt_readmit_held");

    _b.DefineLabel(noReadmitLabel);

    _b.DefineLabel(loopLabel);
    _b.Call("__gt_dequeue_ready");
    // Stored BEFORE the zero test, so the single return path below reads a slot that has always been
    // written — including the "nothing runnable" answer, which is a real result and not a fall-through.
    _b.StoreLocal(slotGt, VReg.Ret);
    _b.JumpIfZero(VReg.Ret, doneLabel);

    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.Call("__dbg_gt_should_hold");
    _b.JumpIfZero(VReg.Ret, doneLabel);                   // free to run: this is the answer

    EmitDbgGtHoldPush(slotGt);
    _b.Jump(loopLabel);

    _b.DefineLabel(doneLabel);
    _b.LoadLocal(VReg.Ret, slotGt);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __dbg_park_loop() — the stop-the-world pause. Spin on the command doorbell (CmdSeq), dispatching
  /// set-breakpoint / set-condition / clear-breakpoint / backtrace / read-memory / green-thread control
  /// (and acking each), until the driver sends continue; yield the slice between polls so the pause does
  /// not peg a core. Reused by the entry stop and the breakpoint-hit stop. Returns when continue arrives
  /// (or the agent detaches).
  ///
  /// It is also where <c>--stop-others</c> lives, and its scope is EXACTLY one stop: the hold on every
  /// green thread goes on here, on the way in, and comes off on the way out through `continue`. Two
  /// consequences are deliberate:
  ///   * a STEP does not lift it, because a step is still a stop — letting the rest of the program run
  ///     for the duration of one instruction is not a coherent thing to offer, and one source-line step
  ///     is up to MaxStepInstructions round trips, each of which would otherwise re-enqueue and re-catch
  ///     every thread in the program;
  ///   * the DETACH exit lifts it, because a debuggee whose driver has gone must not stay frozen.
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
    var gtListLabel = UniqueLabel("dbg_park_gt_list");
    var gtBtLabel = UniqueLabel("dbg_park_gt_backtrace");
    var gtHoldLabel = UniqueLabel("dbg_park_gt_hold");
    var gtReleaseLabel = UniqueLabel("dbg_park_gt_release");
    var ackLabel = UniqueLabel("dbg_park_ack");
    var contLabel = UniqueLabel("dbg_park_continue");
    var stepLabel = UniqueLabel("dbg_park_step");
    var exitLabel = UniqueLabel("dbg_park_exit");
    var releaseAllLabel = UniqueLabel("dbg_park_release_all");
    var noStopOthersLabel = UniqueLabel("dbg_park_no_stop_others");
    var doneLabel = UniqueLabel("dbg_park_done");

    // Freeze the rest of the program for the duration of this stop, if the session asked for it. Set
    // BEFORE the first command is served, so the very first `threads` already reports the world as the
    // user asked for it rather than as it was a moment ago.
    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, noStopOthersLabel);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DbgOffStopOthers);
    _b.JumpIfZero(VReg.Scratch2, noStopOthersLabel);
    _b.MovRegImm(VReg.Scratch2, 1);
    _b.StoreGlobal(DbgGtHoldAllGlobal, VReg.Scratch2);

    _b.DefineLabel(noStopOthersLabel);

    _b.DefineLabel(loopLabel);
    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.JumpIfZero(VReg.Scratch1, doneLabel);              // detached
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, DbgOffCmdSeq);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, DbgOffAckSeq);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.JumpIf(Condition.Equal, idleLabel);               // no new command

    _b.StoreLocal(0, VReg.Scratch2);                     // remember the seq we are about to process

    // The command's outcome, published ahead of the ack that makes it visible. Ok is the default because
    // most commands genuinely cannot fail; the ones that CAN — set-breakpoint, set-condition, and the
    // three per-thread green-thread commands — overwrite it where they fail, so a new command that can
    // fail says so at the point it fails rather than needing a line here. Written BEFORE the dispatch so
    // the paths that leave the loop (continue / step) carry it too, and so a handler's own store wins.
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
    _b.CmpRegImm(VReg.Ret, DbgCmdGtList);
    _b.JumpIf(Condition.Equal, gtListLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdGtBacktrace);
    _b.JumpIf(Condition.Equal, gtBtLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdGtHold);
    _b.JumpIf(Condition.Equal, gtHoldLabel);
    _b.CmpRegImm(VReg.Ret, DbgCmdGtRelease);
    _b.JumpIf(Condition.Equal, gtReleaseLabel);
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

    _b.DefineLabel(gtListLabel);                         // publish the thread records, then ack (below)
    _b.ZeroReg(VReg.Arg0);                               // enumerate only: no handle to look for
    _b.Call("__dbg_gt_scan");
    _b.Jump(ackLabel);

    _b.DefineLabel(gtBtLabel);                           // fill the frame array for one thread, then ack
    _b.Call("__dbg_gt_backtrace");
    _b.Jump(ackLabel);

    _b.DefineLabel(gtHoldLabel);                         // stop scheduling one thread, then ack (below)
    _b.Call("__dbg_gt_hold_add");
    _b.Jump(ackLabel);

    _b.DefineLabel(gtReleaseLabel);                      // let one thread run again, then ack (below)
    _b.Call("__dbg_gt_release");
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
    // The `--stop-others` freeze ends with the stop that raised it. Both halves are needed and neither
    // is the other's duplicate: clearing the flag stops NEW threads being caught, and the doorbell is
    // what hands back the ones already caught — which nothing else would ever look at again.
    _b.Jump(releaseAllLabel);

    _b.DefineLabel(stepLabel);
    _b.MovRegImm(VReg.Ret, DbgStepModeUser);
    _b.StoreGlobal(DbgStepModeGlobal, VReg.Ret);

    // Ack and return. Base and seq are RELOADED rather than carried in registers, exactly as the ack
    // above does: the release path below writes agent globals on the way here, and which registers a
    // StoreGlobal needs is the backend's business, not something to assume from here.
    _b.DefineLabel(exitLabel);
    _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
    _b.LoadLocal(VReg.Scratch2, 0);
    _b.StoreRelease(VReg.Scratch1, DbgOffAckSeq, VReg.Scratch2);
    _b.FunctionEnd();

    // Reached by `continue`. Scratch1/Scratch2 (base and seq) are reloaded by the ack it falls into.
    _b.DefineLabel(releaseAllLabel);
    EmitDbgGtReleaseAllHolds();
    _b.Jump(exitLabel);

    // The DETACH exit, and it drops the freeze through the SAME pair for the same reason a `continue`
    // does — a debuggee whose driver has gone must not be left frozen. ⚠ What actually saves it here is
    // NOT the doorbell: the dequeue filter is gated on `__dbg_base`, and reaching this label means that
    // is already 0, so nothing will read either word again. The rule that un-freezes a detached debuggee
    // is `__dbg_gt_should_hold`'s SHUTDOWN arm, which fires while the filter is still live — this pair
    // is here so the agent's own state is consistent whichever exit was taken, not as the cure.
    _b.DefineLabel(doneLabel);
    EmitDbgGtReleaseAllHolds();
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
    // The control segment's last and largest field is a PRODUCT of two constants, which is a different
    // kind of fragile from every field before it: those are hand-placed offsets whose arithmetic a
    // reader can check by eye, while raising DbgMaxGreenThreads by one line silently pushes the record
    // array off the end of the mapped page — and the agent writes that array from inside a trap handler,
    // so the symptom would be the debuggee faulting rather than a wrong number. The comments above state
    // the arithmetic; this CHECKS it, at build time, because a stated invariant is a test nobody wrote.
    if (DbgControlSegmentHighWater > DbgControlSegmentSize)
      throw new InvalidOperationException(
        $"the debug control segment overflows its {DbgControlSegmentSize}-byte page: its last field ends "
        + $"at {DbgControlSegmentHighWater}. Shrink DbgMaxGreenThreads ({DbgMaxGreenThreads}) or "
        + $"DbgGtRecSize ({DbgGtRecSize}), or grow the segment.");

    // The shift and the size are one width in two notations, and only a check makes them stay one.
    if (1 << DbgGtWordShift != DbgGtWordSize)
      throw new InvalidOperationException(
        $"DbgGtWordShift ({DbgGtWordShift}) does not describe DbgGtWordSize ({DbgGtWordSize}): the "
        + "agent would index its green-thread tables by a stride they are not laid out with.");

    EmitDebugAgentGlobals();
    EmitDbgTableSlotScan("__dbg_bp_slot", DbgBpAddrGlobal, DbgMaxBreakpoints);
    EmitDbgTableSlotScan("__dbg_gt_hold_slot", DbgGtHoldGlobal, DbgMaxHeldGreenThreads);
    EmitDbgSetBp();
    EmitDbgClearBp();
    EmitDbgBpOrigOfAddr();
    EmitDbgCondZero();
    EmitDbgCondHolds();
    EmitDbgSetBpCond();
    EmitDbgPublishStop();
    EmitDbgTextOffset();
    EmitDbgFrameRa();
    EmitDbgFrameNext();
    EmitDbgWalkFrames();
    EmitDbgBacktrace();
    EmitDbgReadMem();
    EmitDbgProcAt();
    EmitDbgGtOnCpu();
    EmitDbgGtFrames();
    EmitDbgGtShouldHold();
    EmitDbgGtRecord();
    EmitDbgGtScan();
    EmitDbgGtBacktrace();
    EmitDbgGtHoldAdd();
    EmitDbgGtRelease();
    EmitDbgGtReadmitHeld();
    EmitDbgGtDequeueFiltered();
    EmitDbgParkLoop();
    EmitDbgOnBreakpoint();
    EmitDbgOnStep();
    EmitDbgPrepareStepAt();
    EmitDebugAgentInit();
    EmitDebugAgentShutdown();
  }
}
