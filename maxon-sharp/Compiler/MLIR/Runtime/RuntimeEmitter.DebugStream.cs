using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;

namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// DebugStream: shared-memory ring buffer for high-performance binary trace output.
/// The monitor (maxon monitor) creates named shared memory and spawns the target.
/// The target opens the pre-existing segment via the MAXON_DEBUGSTREAM env var,
/// writes binary events into the ring buffer, and unmaps on shutdown.
///
/// THE RING PROTOCOL, in three moments — because two of them used to be one, and that was a race:
///
///   1. RESERVE (`__ds_reserve`, under the ticket lock). Claim the bytes, write the entry HEADER
///      with the commit bit CLEAR, advance `write_cursor` with a release store, drop the lock.
///      The entry is now VISIBLE to the monitor. Its payload is still whatever the ring last
///      held there.
///   2. PAYLOAD (the caller, outside the lock — this is the bulk of the work, and for LOG_TEXT
///      it is a byte-copy loop).
///   3. COMMIT (`__ds_commit`). Set the commit bit with a RELEASE store. The entry is now
///      READABLE, and only now.
///
/// The monitor decodes nothing — and advances past nothing — that does not carry the commit bit
/// (<see cref="DsEntryFlagCommitted"/>). Before it existed, step 1 published the cursor and step
/// 2 wrote the payload afterwards, so the monitor could copy and decode an entry whose payload
/// had not been written: stale ring bytes, reported as events. That hit EVERY family — mm, sched,
/// dbg and log alike — and it is why the pairing of (1) and (3) is enforced structurally, in
/// <c>EmitDsEntryBody</c>, rather than left to each call site to remember.
/// </summary>
public partial class RuntimeEmitter {

  // =========================================================================
  // Globals
  // =========================================================================

  /// <summary>
  /// Emit global variables used by the debugstream runtime.
  /// Must be called during code emission when DebugStream is enabled.
  /// </summary>
  public void EmitDebugStreamGlobals() {
    // Base pointer to the mapped shared memory region (0 = not attached)
    _b.DefineGlobal("__ds_base", 8, 0);
    // Buffer size (read from header at init, cached for fast access)
    _b.DefineGlobal("__ds_buf_size", 8, 0);
    // Buffer size mask (buf_size - 1, for modulo via AND)
    _b.DefineGlobal("__ds_buf_mask", 8, 0);
    // Ticket spinlock for serializing __ds_reserve. Without this, two threads
    // can both reserve overlapping ranges and produce torn event headers
    // (observed in early traces: 0x1cc P-id values where arg qwords leaked
    // into the header slot of an unfinished neighboring event).
    //   __ds_reserve_next: next ticket number to hand out
    //   __ds_reserve_now:  ticket currently being served
    // Acquire: my_ticket = atomic_xadd(__ds_reserve_next, 1); spin while
    //          __ds_reserve_now != my_ticket.
    // Release: atomic_xadd(__ds_reserve_now, 1).
    _b.DefineGlobal("__ds_reserve_next", 8, 0);
    _b.DefineGlobal("__ds_reserve_now", 8, 0);
    // Env var name
    _b.DefineSymdata("__ds_env_name", "MAXON_DEBUGSTREAM\0"u8.ToArray());
  }

  // =========================================================================
  // Init: __debugstream_init
  // =========================================================================

  /// <summary>
  /// Emit the __debugstream_init function.
  /// Called during startup. Checks the MAXON_DEBUGSTREAM environment variable.
  /// If set, opens the named shared memory, maps it, stores the base pointer
  /// in __ds_base, and sets producer_alive flag.
  /// If not set, returns immediately (debugstream disabled).
  ///
  /// Stack frame slots:
  ///   0..15 = general scratch
  ///   16..31 = GetEnvironmentVariableA buffer (128 bytes, Windows only)
  ///   17 = mapped base pointer (reused after env var read)
  /// </summary>
  public void EmitDebugStreamInit() {
    // 32 slots * 8 = 256 bytes + alignment overhead = 0x110
    _b.FunctionStart("__debugstream_init", 0, 0x110);

    var disabledLabel = UniqueLabel("ds_init_disabled");
    var doneLabel = UniqueLabel("ds_init_done");

    if (_b.IsWindows) {
      // Windows: GetEnvironmentVariableA("MAXON_DEBUGSTREAM", buf, 128)
      // buf occupies slots 16..31 (128 bytes). LeaLocal(31) = [RBP - 256], buffer grows upward to [RBP - 129].
      _b.LeaSymdata(VReg.Arg0, "__ds_env_name");   // lpName
      _b.LeaLocal(VReg.Arg1, 31);                   // lpBuffer
      _b.MovRegImm(VReg.Arg2, 128);                 // nSize
      _b.CallImport("GetEnvironmentVariableA");
      // Ret = chars copied, 0 if not set
      _b.JumpIfZero(VReg.Ret, disabledLabel);
    } else {
      _b.LeaSymdata(VReg.Arg0, "__ds_env_name");
      _b.CallImport("getenv");
      _b.JumpIfZero(VReg.Ret, disabledLabel);
    }

    // Open the named shared memory
    if (_b.IsWindows) {
      _b.LeaLocal(VReg.Arg0, 31); // buffer with env var value
    } else {
      _b.MovRegReg(VReg.Arg0, VReg.Ret);
    }
    _b.MovRegImm(VReg.Arg1, DsHeaderSize + DsDefaultBufferSize + 65536);
    _b.OsOpenAndMapSharedMemory(VReg.Ret, VReg.Arg0, VReg.Arg1);
    _b.JumpIfZero(VReg.Ret, disabledLabel);

    // Ret = mapped base pointer. Save to slot 17 and to global.
    _b.StoreLocal(17, VReg.Ret);
    _b.StoreGlobal("__ds_base", VReg.Ret);

    // Validate magic number
    _b.MovRegReg(VReg.Scratch1, VReg.Ret); // Scratch1 = base
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DsOffMagic);
    _b.MovRegImm(VReg.Scratch3, DsMagic);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.JumpIf(Condition.NotEqual, disabledLabel);

    // Read buffer_size from header and cache in globals
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DsOffBufferSize);
    _b.StoreGlobal("__ds_buf_size", VReg.Scratch2);
    // buf_mask = buf_size - 1
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.SubRegImm(VReg.Scratch3, 1);
    _b.StoreGlobal("__ds_buf_mask", VReg.Scratch3);

    // Set flags.producer_alive = 1
    _b.MovRegImm(VReg.Scratch2, DsFlagProducerAlive);
    _b.StoreIndirect(VReg.Scratch1, DsOffFlags, VReg.Scratch2);

    // Read start_timestamp from header into a global for fast delta computation
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, DsOffStartTimestamp);
    _b.StoreGlobal("__ds_start_ts", VReg.Scratch2);

    _b.Jump(doneLabel);

    _b.DefineLabel(disabledLabel);
    // Ensure __ds_base stays 0 (debugstream disabled)
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreGlobal("__ds_base", VReg.Scratch0);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // Shutdown: __debugstream_shutdown
  // =========================================================================

  /// <summary>
  /// Emit the __debugstream_shutdown function.
  /// Called during process shutdown. Sets producer_alive=0, unmaps the shared memory.
  /// </summary>
  public void EmitDebugStreamShutdown() {
    _b.FunctionStart("__debugstream_shutdown", 0, 0x30);

    var doneLabel = UniqueLabel("ds_shutdown_done");

    // Load base pointer
    _b.LoadGlobal(VReg.Scratch0, "__ds_base");
    _b.JumpIfZero(VReg.Scratch0, doneLabel);

    // Clear producer_alive flag
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, DsOffFlags, VReg.Scratch1);

    // Unmap
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.MovRegImm(VReg.Arg1, DsHeaderSize + DsDefaultBufferSize + 65536);
    _b.OsUnmapSharedMemory(VReg.Arg0, VReg.Arg1);

    // Clear global
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreGlobal("__ds_base", VReg.Scratch0);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // Additional globals for debugstream
  // =========================================================================

  /// <summary>
  /// Emit additional globals needed by the event writing functions.
  /// Called after EmitDebugStreamGlobals.
  /// </summary>
  public void EmitDebugStreamWriteGlobals() {
    // Cached start timestamp for delta computation
    _b.DefineGlobal("__ds_start_ts", 8, 0);
  }

  // =========================================================================
  // Core ring buffer write: __ds_reserve
  // =========================================================================

  /// <summary>
  /// Emit __ds_reserve(event_type, entry_size) -> pointer to write location, or 0 if full.
  /// Writes the entry header and publishes the entry as UNCOMMITTED.
  ///
  /// Args: Arg0 = event_type (byte), Arg1 = entry_size (total, 8-byte aligned)
  /// Returns: Ret = pointer to entry start in shared memory (0 if dropped)
  ///
  /// THE ENTRY IS NOT READABLE WHEN THIS RETURNS. The header goes down with flags = 0 (see
  /// <see cref="DsEntryFlagCommitted"/>), the cursor advances and the ring lock is released —
  /// all before the caller has written a single byte of payload. Publishing the cursor is what
  /// makes the entry VISIBLE; setting the commit bit, in <c>__ds_commit</c>, is what makes it
  /// READABLE. The two are deliberately different moments, because the payload must be written
  /// outside the lock (it is the bulk of the work, and a LOG_TEXT tail can be kilobytes).
  ///
  /// Stack slots: 0=event_type, 1=entry_size, 2=base, 3=buf_size, 4=buf_mask,
  ///              5=write_cursor, 6..7=timestamp scratch, 8=data_ptr, 9=ticket
  /// </summary>
  public void EmitDsReserve() {
    _b.FunctionStart("__ds_reserve", 2, 0x60);

    var dropLabel = UniqueLabel("ds_reserve_drop");
    var noPadLabel = UniqueLabel("ds_reserve_nopad");
    var doneLabel = UniqueLabel("ds_reserve_done");
    var recheckLabel = UniqueLabel("ds_reserve_recheck");
    var spinLabel = UniqueLabel("ds_reserve_spin");

    // Save args
    _b.StoreLocal(0, VReg.Arg0); // event_type
    _b.StoreLocal(1, VReg.Arg1); // entry_size

    // Load base, bail if not attached
    _b.LoadGlobal(VReg.Scratch0, "__ds_base");
    _b.JumpIfZero(VReg.Scratch0, dropLabel);
    _b.StoreLocal(2, VReg.Scratch0); // base

    // ---- Acquire ticket lock ----
    // my_ticket = atomic_xadd(__ds_reserve_next, 1)
    _b.MovRegImm(VReg.Scratch1, 1);
    _b.LeaGlobal(VReg.Scratch2, "__ds_reserve_next");
    _b.AtomicXadd(VReg.Scratch2, 0, VReg.Scratch1); // Scratch1 = old next
    _b.StoreLocal(9, VReg.Scratch1); // save my_ticket
    // Spin while __ds_reserve_now != my_ticket
    _b.DefineLabel(spinLabel);
    _b.LoadGlobal(VReg.Scratch0, "__ds_reserve_now");
    _b.LoadLocal(VReg.Scratch1, 9);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.NotEqual, spinLabel);
    // Lock acquired. Reload base into Scratch0 for the rest of the function
    // (the slot 2 cache survives the spin).
    _b.LoadLocal(VReg.Scratch0, 2); // base

    // Load cached buf_size and buf_mask
    _b.LoadGlobal(VReg.Scratch1, "__ds_buf_size");
    _b.StoreLocal(3, VReg.Scratch1);
    _b.LoadGlobal(VReg.Scratch2, "__ds_buf_mask");
    _b.StoreLocal(4, VReg.Scratch2);

    // Load write_cursor
    _b.LoadLocal(VReg.Scratch0, 2); // base
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, DsOffWriteCursor); // wr
    _b.StoreLocal(5, VReg.Scratch1); // save wr

    // Load read_cursor
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, DsOffReadCursor); // rd

    // Check space: used = wr - rd; if used + entry_size > buf_size, drop
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch1); // wr
    _b.SubRegReg(VReg.Scratch3, VReg.Scratch2); // used = wr - rd

    // Track peak buffer usage (Scratch0 = base, Scratch3 = used)
    var peakOkLabel = UniqueLabel("ds_reserve_peak_ok");
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, DsOffPeakUsed); // current peak
    _b.CmpRegReg(VReg.Scratch3, VReg.Arg0);
    _b.JumpIf(Condition.BelowEqual, peakOkLabel);
    _b.StoreIndirect(VReg.Scratch0, DsOffPeakUsed, VReg.Scratch3); // new peak
    _b.DefineLabel(peakOkLabel);

    _b.LoadLocal(VReg.Arg0, 1); // entry_size
    _b.AddRegReg(VReg.Scratch3, VReg.Arg0); // used + entry_size
    _b.LoadLocal(VReg.Arg1, 3); // buf_size
    _b.CmpRegReg(VReg.Scratch3, VReg.Arg1);
    _b.JumpIf(Condition.Above, dropLabel);

    // Check wrap: pos = wr & mask; if pos + entry_size > buf_size, write padding
    _b.LoadLocal(VReg.Scratch1, 5); // wr
    _b.LoadLocal(VReg.Scratch2, 4); // mask
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2); // pos = wr & mask
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch1); // pos
    _b.LoadLocal(VReg.Arg0, 1); // entry_size
    _b.AddRegReg(VReg.Scratch3, VReg.Arg0); // pos + entry_size
    _b.LoadLocal(VReg.Arg1, 3); // buf_size
    _b.CmpRegReg(VReg.Scratch3, VReg.Arg1);
    _b.JumpIf(Condition.BelowEqual, noPadLabel);

    // Need padding: pad_size = buf_size - pos
    _b.LoadLocal(VReg.Arg1, 3); // buf_size
    _b.SubRegReg(VReg.Arg1, VReg.Scratch1); // pad_size = buf_size - pos
    // Write padding entry at data_ptr = base + DsHeaderSize + pos
    _b.LoadLocal(VReg.Scratch0, 2); // base
    _b.AddRegImm(VReg.Scratch0, DsHeaderSize);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // data_ptr = base + header + pos
    // Padding header: event_type=0xFF, flags=COMMITTED, entry_size=pad_size, timestamp=0.
    // Pack: (pad_size << DsEntrySizeShift) | DsPaddingHeaderTypeAndFlags.
    //
    // BORN COMMITTED, and it has to be: a padding entry is written entirely here, has no payload
    // and no second writer, so nothing would ever come back to set its bit — the monitor, which
    // refuses to advance past an uncommitted entry, would stall on it forever. This store is
    // ordered ahead of the write_cursor release below, so a reader that sees the cursor sees it.
    _b.MovRegReg(VReg.Scratch2, VReg.Arg1); // pad_size
    _b.ShlRegImm(VReg.Scratch2, DsEntrySizeShift);
    _b.AddRegImm(VReg.Scratch2, DsPaddingHeaderTypeAndFlags);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2); // write padding header

    // Advance write_cursor by pad_size
    _b.LoadLocal(VReg.Scratch1, 5); // wr
    _b.AddRegReg(VReg.Scratch1, VReg.Arg1); // wr += pad_size
    _b.StoreLocal(5, VReg.Scratch1); // save new wr

    // Re-check space after padding
    _b.DefineLabel(recheckLabel);
    _b.LoadLocal(VReg.Scratch0, 2); // base
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, DsOffReadCursor); // re-read rd
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch1); // wr (updated)
    _b.SubRegReg(VReg.Scratch3, VReg.Scratch2); // used
    _b.LoadLocal(VReg.Arg0, 1); // entry_size
    _b.AddRegReg(VReg.Scratch3, VReg.Arg0); // used + entry_size
    _b.LoadLocal(VReg.Arg1, 3); // buf_size
    _b.CmpRegReg(VReg.Scratch3, VReg.Arg1);
    _b.JumpIf(Condition.Above, dropLabel);

    _b.DefineLabel(noPadLabel);
    // pos = wr & mask (wr may have been updated by padding)
    _b.LoadLocal(VReg.Scratch1, 5); // wr
    _b.LoadLocal(VReg.Scratch2, 4); // mask
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2); // pos

    // data_ptr = base + DsHeaderSize + pos
    _b.LoadLocal(VReg.Scratch0, 2); // base
    _b.AddRegImm(VReg.Scratch0, DsHeaderSize);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // data_ptr
    _b.StoreLocal(8, VReg.Scratch0); // save data_ptr (GetCurrentTimeMs clobbers all scratch regs)

    // Get timestamp delta (clobbers Arg0..Arg4, Scratch0..Scratch2)
    _b.GetCurrentTimeMs(VReg.Scratch2, 6); // uses scratch slots 6,7
    _b.LoadGlobal(VReg.Scratch3, "__ds_start_ts");
    _b.SubRegReg(VReg.Scratch2, VReg.Scratch3); // delta = now - start

    // Pack header into one 8-byte value:
    //   [0:7] = event_type, [8:15] = flags = 0, [16:31] = entry_size, [32:63] = timestamp_delta
    //
    // flags = 0 is NOT filler — it is the entry's UNCOMMITTED state (DsEntryFlagCommitted), and
    // it falls out for free because event_type is a byte and entry_size starts at bit 16.
    _b.LoadLocal(VReg.Scratch3, 0); // event_type (in low byte)
    _b.LoadLocal(VReg.Arg0, 1);     // entry_size
    _b.ShlRegImm(VReg.Arg0, DsEntrySizeShift);
    _b.AddRegReg(VReg.Scratch3, VReg.Arg0); // event_type | (entry_size << 16)
    _b.ShlRegImm(VReg.Scratch2, DsEntryTimestampShift);
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch2); // | (timestamp << 32)
    _b.LoadLocal(VReg.Scratch0, 8); // reload data_ptr
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch3); // write header

    // Advance write_cursor — a RELEASE store, and the ring's one publish point.
    //
    // It orders every store above it (this entry's header, and any padding entry written on the
    // way) ahead of the cursor that makes them reachable. The monitor bounds its header walk by
    // this cursor, so pairing the two is what lets it trust that a header inside [read, write)
    // is THIS entry's header and not a previous generation's bytes at the same ring offset —
    // stale bytes that would carry a stale COMMIT BIT and a stale entry_size, and desynchronise
    // the walk. On x86 this is a plain MOV; on ARM64 it is STLR, and there it is load-bearing.
    _b.LoadLocal(VReg.Scratch1, 5); // wr
    _b.LoadLocal(VReg.Arg0, 1); // entry_size
    _b.AddRegReg(VReg.Scratch1, VReg.Arg0); // wr += entry_size
    _b.LoadLocal(VReg.Scratch2, 2); // base
    _b.StoreRelease(VReg.Scratch2, DsOffWriteCursor, VReg.Scratch1);

    // Increment total_events
    _b.AtomicInc(VReg.Scratch2, DsOffTotalEvents);

    // ---- Release ticket lock ----
    _b.MovRegImm(VReg.Scratch1, 1);
    _b.LeaGlobal(VReg.Scratch2, "__ds_reserve_now");
    _b.AtomicXadd(VReg.Scratch2, 0, VReg.Scratch1);

    // Return data_ptr
    _b.LoadLocal(VReg.Scratch0, 8);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(dropLabel);
    // Increment dropped_events counter
    _b.LoadGlobal(VReg.Scratch0, "__ds_base");
    _b.JumpIfZero(VReg.Scratch0, doneLabel);
    _b.AtomicInc(VReg.Scratch0, DsOffDroppedEvents);
    _b.DefineLabel(doneLabel);
    // Release lock if we acquired it. The drop path jumps in from two places:
    // (a) base==0 at function entry — we never acquired; skip.
    // (b) buffer-full check after acquiring — we DID acquire.
    // Distinguish by re-checking base: if base==0 we didn't acquire.
    var skipReleaseLabel = UniqueLabel("ds_reserve_no_release");
    _b.LoadGlobal(VReg.Scratch0, "__ds_base");
    _b.JumpIfZero(VReg.Scratch0, skipReleaseLabel);
    _b.MovRegImm(VReg.Scratch1, 1);
    _b.LeaGlobal(VReg.Scratch2, "__ds_reserve_now");
    _b.AtomicXadd(VReg.Scratch2, 0, VReg.Scratch1);
    _b.DefineLabel(skipReleaseLabel);
    _b.ZeroReg(VReg.Ret);
    _b.FunctionEnd();
  }

  // =========================================================================
  // Commit: __ds_commit
  // =========================================================================

  /// <summary>
  /// Emit __ds_commit(data_ptr): mark the entry at <c>data_ptr</c> COMPLETE — payload written,
  /// safe to read. Args: Arg0 = the pointer <c>__ds_reserve</c> returned.
  ///
  /// The read-modify-write of the header word needs NO atomic. `__ds_reserve` handed this entry
  /// to exactly one thread, that thread is the only one that ever writes the entry again, and
  /// the monitor only reads it — so nothing can race the OR.
  ///
  /// What it does need is RELEASE ordering, which is why this is <c>StoreRelease</c> and not a
  /// plain store: the payload writes must be visible to the monitor BEFORE the bit that claims
  /// they are there. Reversed, the monitor sees a committed entry and copies a stale payload —
  /// exactly the torn read the commit bit exists to prevent, just moved one level down. On x86
  /// TSO the release is free (a plain MOV); on ARM64 it compiles to STLR, and the ARM64 backend
  /// is a real target, so this is a correctness rule, not a portability nicety.
  /// </summary>
  public void EmitDsCommit() {
    _b.FunctionStart("__ds_commit", 1, 0x30);

    _b.LoadLocal(VReg.Scratch0, 0); // data_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // the header __ds_reserve wrote
    _b.MovRegImm(VReg.Scratch2, DsEntryHeaderCommittedBit);
    _b.OrRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.StoreRelease(VReg.Scratch0, 0, VReg.Scratch1);

    _b.FunctionEnd();
  }

  // =========================================================================
  // Per-event-type emitters
  // =========================================================================

  /// <summary>
  /// Emit the reserve → payload → COMMIT sequence that every DebugStream event body IS.
  ///
  /// THIS IS THE ONLY PLACE `__ds_reserve` IS CALLED, and it always pairs the reserve with a
  /// `__ds_commit`. A producer therefore cannot publish an entry and forget to commit it: the
  /// pairing is not a convention each call site is trusted to follow, it is the only shape a
  /// call site can have — a new event family gets its commit by virtue of existing. That is
  /// worth the indirection, because a missed commit does not lose one event: an uncommitted
  /// entry is never skipped while its producer lives, so the monitor STALLS ON IT FOREVER.
  ///
  /// <paramref name="stageReserveArgs"/> must leave Arg0 = event_type and Arg1 = entry_size.
  /// <paramref name="writePayload"/> writes the payload through <see cref="VReg.Ret"/>, which
  /// holds the entry pointer, and may clobber every other register. The pointer is also spilled
  /// to <paramref name="dataPtrSlot"/>, because the commit needs it back afterwards.
  /// </summary>
  private void EmitDsEntryBody(int dataPtrSlot, Action stageReserveArgs, Action writePayload) {
    var done = UniqueLabel("ds_entry_done");

    stageReserveArgs();
    _b.Call("__ds_reserve");
    // 0 = the ring is detached or full. The entry was never reserved, so there is nothing to
    // commit — this is a DROPPED event (counted in the ring header), not a deferred one.
    _b.JumpIfZero(VReg.Ret, done);

    _b.StoreLocal(dataPtrSlot, VReg.Ret);
    writePayload();

    _b.LoadLocal(VReg.Arg0, dataPtrSlot);
    _b.Call("__ds_commit");

    _b.DefineLabel(done);
  }

  /// The <c>valueSlot</c> an mm event passes when it has no 32-bit value to carry.
  private const int DsMmNoValueSlot = -1;

  /// <summary>
  /// Write the payload shared by every mm event that carries one: alloc_id, then the packed
  /// tag_index(2) | scope_len(2) | value(4) word. `value` is an allocation size for the alloc
  /// family and a new refcount for the refcount family — the same 32-bit slot either way, which
  /// is why they pack HERE rather than in four near-identical copies.
  ///
  /// <paramref name="valueSlot"/> is <see cref="DsMmNoValueSlot"/> for an event with no value
  /// (mm_free), which leaves the slot zero. Clobbers Scratch1, Scratch2.
  /// </summary>
  private void EmitDsStoreMmPayload(int packedIdSlot, int valueSlot) {
    _b.LoadLocal(VReg.Scratch1, packedIdSlot);
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.ShrRegImm(VReg.Scratch2, DsMmPackedIdShift); // alloc_id
    _b.StoreIndirect(VReg.Ret, DsMmOffAllocId, VReg.Scratch2);

    _b.MovRegImm(VReg.Scratch2, DsMmTagIndexMask);
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2); // tag_index, with scope_len left 0

    if (valueSlot != DsMmNoValueSlot) {
      _b.LoadLocal(VReg.Scratch2, valueSlot);
      _b.ShlRegImm(VReg.Scratch2, DsMmValueShift);
      _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    }

    _b.StoreIndirect(VReg.Ret, DsMmOffPacked, VReg.Scratch1);
  }

  /// <summary>
  /// __ds_emit_mm_alloc(packed_id, alloc_size, scope_ptr)
  /// MM_ALLOC: alloc_id(8), tag_index(2), scope_len(2), size(4).
  /// </summary>
  public void EmitDsEmitMmAlloc() {
    _b.FunctionStart("__ds_emit_mm_alloc", 3, 0x60);
    // Slots: 0=packed_id, 1=alloc_size, 2=scope_ptr, 3=data_ptr

    EmitDsEntryBody(dataPtrSlot: 3,
      () => {
        _b.MovRegImm(VReg.Arg0, DsEvMmAlloc);
        _b.MovRegImm(VReg.Arg1, DsMmEntrySize);
      },
      () => EmitDsStoreMmPayload(packedIdSlot: 0, valueSlot: 1));

    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_mm_free(packed_id, scope_ptr)
  /// MM_FREE: alloc_id(8), tag_index(2), pad(6). No value — a free has no size to report.
  /// </summary>
  public void EmitDsEmitMmFree() {
    _b.FunctionStart("__ds_emit_mm_free", 2, 0x40);
    // Slots: 0=packed_id, 1=scope_ptr, 2=data_ptr

    EmitDsEntryBody(dataPtrSlot: 2,
      () => {
        _b.MovRegImm(VReg.Arg0, DsEvMmFree);
        _b.MovRegImm(VReg.Arg1, DsMmEntrySize);
      },
      () => EmitDsStoreMmPayload(packedIdSlot: 0, valueSlot: DsMmNoValueSlot));

    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_mm_refcount(event_type, packed_id, new_refcount, scope_ptr)
  /// MM_INCREF / MM_DECREF / MM_TRANSFER: alloc_id(8), tag_index(2), scope_len(2), new_rc(4).
  /// </summary>
  public void EmitDsEmitMmRefcount() {
    _b.FunctionStart("__ds_emit_mm_refcount", 4, 0x60);
    // Slots: 0=event_type, 1=packed_id, 2=new_refcount, 3=scope_ptr, 4=data_ptr

    EmitDsEntryBody(dataPtrSlot: 4,
      () => {
        _b.LoadLocal(VReg.Arg0, 0); // event_type
        _b.MovRegImm(VReg.Arg1, DsMmEntrySize);
      },
      () => EmitDsStoreMmPayload(packedIdSlot: 1, valueSlot: 2));

    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_mm_raw_alloc(raw_id, size)
  /// A raw allocation carries no tag and no scope, so its size takes the packed word outright.
  /// </summary>
  public void EmitDsEmitMmRawAlloc() {
    _b.FunctionStart("__ds_emit_mm_raw_alloc", 2, 0x40);
    // Slots: 0=raw_id, 1=size, 2=data_ptr

    EmitDsEntryBody(dataPtrSlot: 2,
      () => {
        _b.MovRegImm(VReg.Arg0, DsEvMmRawAlloc);
        _b.MovRegImm(VReg.Arg1, DsMmEntrySize);
      },
      () => {
        _b.LoadLocal(VReg.Scratch1, 0); // raw_id
        _b.StoreIndirect(VReg.Ret, DsMmOffAllocId, VReg.Scratch1);
        _b.LoadLocal(VReg.Scratch1, 1); // size
        _b.StoreIndirect(VReg.Ret, DsMmOffRawSize, VReg.Scratch1);
      });

    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_mm_raw_free(raw_id)
  /// </summary>
  public void EmitDsEmitMmRawFree() {
    _b.FunctionStart("__ds_emit_mm_raw_free", 1, 0x30);
    // Slots: 0=raw_id, 1=data_ptr

    EmitDsEntryBody(dataPtrSlot: 1,
      () => {
        _b.MovRegImm(VReg.Arg0, DsEvMmRawFree);
        _b.MovRegImm(VReg.Arg1, DsMmRawFreeEntrySize);
      },
      () => {
        _b.LoadLocal(VReg.Scratch1, 0); // raw_id
        _b.StoreIndirect(VReg.Ret, DsMmOffAllocId, VReg.Scratch1);
      });

    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_sched(event_type, trace_id)
  /// Generic scheduler event: just trace_id(8) as payload.
  /// </summary>
  public void EmitDsEmitSched() {
    _b.FunctionStart("__ds_emit_sched", 2, 0x40);
    // Slots: 0=event_type, 1=trace_id, 2=data_ptr

    EmitDsEntryBody(dataPtrSlot: 2,
      () => {
        _b.LoadLocal(VReg.Arg0, 0); // event_type
        _b.MovRegImm(VReg.Arg1, DsSchedEntrySize);
      },
      () => {
        _b.LoadLocal(VReg.Scratch1, 1); // trace_id
        _b.StoreIndirect(VReg.Ret, DsSchedOffTraceId, VReg.Scratch1);
      });

    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_dbg(event_type, gt, p_id, arg2, arg3, arg4)
  /// Generic per-slot debug event. Payload after the 8-byte header is:
  ///   [+8]   gt         (8 bytes) — green thread pointer
  ///   [+16]  p_id       (8 bytes) — owning processor id (or 0 if none, e.g. IOCP thread)
  ///   [+24]  arg2       (8 bytes) — event-specific
  ///   [+32]  arg3       (8 bytes) — event-specific
  ///   [+40]  arg4       (8 bytes) — event-specific
  ///
  /// All callers should pass arg2..arg4 = 0 if unused. Helpers below
  /// (`EmitDbg*`) provide named-argument convenience wrappers.
  /// </summary>
  public void EmitDsEmitDbg() {
    _b.FunctionStart("__ds_emit_dbg", 6, 0x60);
    // Slots: 0=event_type, 1=gt, 2=p_id, 3=arg2, 4=arg3, 5=arg4, 6=data_ptr

    EmitDsEntryBody(dataPtrSlot: 6,
      () => {
        _b.LoadLocal(VReg.Arg0, 0); // event_type
        _b.MovRegImm(VReg.Arg1, DsDbgEntrySize);
      },
      () => {
        _b.LoadLocal(VReg.Scratch1, 1); // gt
        _b.StoreIndirect(VReg.Ret, DsDbgOffGt, VReg.Scratch1);
        _b.LoadLocal(VReg.Scratch1, 2); // p_id
        _b.StoreIndirect(VReg.Ret, DsDbgOffPid, VReg.Scratch1);
        _b.LoadLocal(VReg.Scratch1, 3); // arg2
        _b.StoreIndirect(VReg.Ret, DsDbgOffArg2, VReg.Scratch1);
        _b.LoadLocal(VReg.Scratch1, 4); // arg3
        _b.StoreIndirect(VReg.Ret, DsDbgOffArg3, VReg.Scratch1);
        _b.LoadLocal(VReg.Scratch1, 5); // arg4
        _b.StoreIndirect(VReg.Ret, DsDbgOffArg4, VReg.Scratch1);
      });

    _b.FunctionEnd();
  }

  /// <summary>
  /// Emit an inline call to __ds_emit_dbg(eventType, gt, P->id, arg2, arg3, arg4).
  /// Wraps args in a fast-bail-when-disabled prologue: load __ds_base, jump-if-zero
  /// over the entire call. Only emits the bail when DebugStream is enabled at compile
  /// time; if DebugStream is off this helper is a no-op (no instructions emitted).
  ///
  /// Clobbers Arg0..Arg5, Scratch0..Scratch3.
  /// </summary>
  public void EmitDbgCall(byte eventType, VReg gt, VReg arg2, VReg arg3, VReg arg4) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallCore(eventType, gt, () => {
      // Move into Arg regs in reverse order so lower Args stay free as sources.
      _b.MovRegReg(VReg.Arg5, arg4);
      _b.MovRegReg(VReg.Arg4, arg3);
      _b.MovRegReg(VReg.Arg3, arg2);
    });
  }

  /// <summary>
  /// Variant of EmitDbgCall that accepts immediate (constant) arg2/arg3/arg4 values.
  /// </summary>
  public void EmitDbgCallImm(byte eventType, VReg gt, long arg2, long arg3, long arg4) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallCore(eventType, gt, () => {
      _b.MovRegImm(VReg.Arg5, arg4);
      _b.MovRegImm(VReg.Arg4, arg3);
      _b.MovRegImm(VReg.Arg3, arg2);
    });
  }

  // Shared body for EmitDbgCall/EmitDbgCallImm. The 6-argument call requires
  // Arg0..Arg5 set simultaneously, so callers stage arg2..arg4 first (via the
  // delegate), then we load P->id into Arg2 and finally set Arg1=gt and Arg0=event.
  // P->id is loaded inline from current P*; LoadCurrentP returns NULL on
  // non-scheduler threads (e.g. IOCP), so we guard with a zero check.
  private void EmitDbgCallCore(byte eventType, VReg gt, Action stageExtraArgs) {
    var skip = UniqueLabel("dbg_skip");
    _b.LoadGlobal(VReg.Scratch3, "__ds_base");
    _b.JumpIfZero(VReg.Scratch3, skip);

    stageExtraArgs();
    _b.MovRegReg(VReg.Arg1, gt);
    _b.LoadCurrentP(VReg.Scratch3);
    var pNull = UniqueLabel("dbg_p_null");
    var pDone = UniqueLabel("dbg_p_done");
    _b.JumpIfZero(VReg.Scratch3, pNull);
    _b.LoadIndirect(VReg.Arg2, VReg.Scratch3, POffId);
    _b.Jump(pDone);
    _b.DefineLabel(pNull);
    _b.ZeroReg(VReg.Arg2);
    _b.DefineLabel(pDone);

    _b.MovRegImm(VReg.Arg0, eventType);
    _b.Call("__ds_emit_dbg");

    _b.DefineLabel(skip);
  }

  // -------------------------------------------------------------------------
  // Tight wrappers around EmitDbgCall / EmitDbgCallImm for each per-slot event.
  // Each is a no-op when DebugStream is disabled at compile time. Callers are
  // expected to have any state they need across the call already spilled to
  // local slots, since the underlying Call clobbers all caller-saved registers.
  // -------------------------------------------------------------------------

  public void EmitDbgRunnextSet(VReg gt) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgRunnextSet, gt, 0, 0, 0);
  }

  public void EmitDbgRunnextTake(VReg gt) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgRunnextTake, gt, 0, 0, 0);
  }

  public void EmitDbgRunnextDisplace(VReg displaced, VReg newGt) {
    if (!Compiler.DebugStream) return;
    EmitDbgCall(DsEvDbgRunnextDisplace, displaced, newGt, /*arg3=*/displaced, /*arg4=*/displaced);
    // arg3/arg4 ignored by formatter; pass any reg to satisfy the helper signature.
  }

  /// <summary>`kind` is a DsDbgQueue* constant — the monitor decodes it off the same ones.</summary>
  public void EmitDbgEnqueue(VReg gt, long kind, long ownerPid) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgEnqueue, gt, kind, ownerPid, 0);
  }

  /// <summary>`kind` is a DsDbgQueue* constant — the monitor decodes it off the same ones.</summary>
  public void EmitDbgDequeue(VReg gt, long kind, long fromPid) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgDequeue, gt, kind, fromPid, 0);
  }

  /// <summary>Site IDs are DsStatusSite* constants in RuntimeEmitter.cs.</summary>
  public void EmitDbgStatusStore(VReg gt, long oldStatus, long newStatus, long siteId) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgStatusStore, gt, oldStatus, newStatus, siteId);
  }

  /// <summary>`phase` is a DsDbgIoPhase* constant — the monitor decodes it off the same ones.</summary>
  public void EmitDbgIoComplete(VReg gt, long phase) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgIoComplete, gt, phase, 0, 0);
  }

  public void EmitDbgFreeListPush(VReg gt, VReg newLen) {
    if (!Compiler.DebugStream) return;
    EmitDbgCall(DsEvDbgFreeListPush, gt, newLen, /*arg3=*/newLen, /*arg4=*/newLen);
    // arg3/arg4 ignored; passing newLen avoids needing another reg.
  }

  public void EmitDbgFreeListPop(VReg gt, VReg newLen) {
    if (!Compiler.DebugStream) return;
    EmitDbgCall(DsEvDbgFreeListPop, gt, newLen, newLen, newLen);
  }

  public void EmitDbgWloopRunGt(VReg gt) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgWloopRunGt, gt, 0, 0, 0);
  }

  public void EmitDbgAwaitDeqRun(VReg gt) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgAwaitDeqRun, gt, 0, 0, 0);
  }

  public void EmitDbgTrampolineCompleted(VReg gt) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgTrampolineCompleted, gt, 0, 0, 0);
  }

  public void EmitDbgTimerFire(VReg gt) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgTimerFire, gt, 0, 0, 0);
  }

  public void EmitDbgCsxEntry(VReg from, VReg to, VReg fromRsp, VReg fromRbp) {
    if (!Compiler.DebugStream) return;
    EmitDbgCall(DsEvDbgCsxEntry, from, to, fromRsp, fromRbp);
  }

  public void EmitDbgCsxExit(VReg from, VReg to, VReg toRsp, VReg toRbp) {
    if (!Compiler.DebugStream) return;
    EmitDbgCall(DsEvDbgCsxExit, from, to, toRsp, toRbp);
  }

  // =========================================================================
  // Log events (Workstream O): the events USER MAXON SOURCE emits
  // =========================================================================

  /// <summary>
  /// Write the emitting green thread and its owning processor id into a Log entry.
  ///
  /// EVERY Log event carries both, and that is the entire point of the family: it is what
  /// lets the monitor demux N interleaved workers back into per-worker timelines instead of
  /// a shuffled pile. The identity is read HERE rather than passed in from the call site,
  /// so a call site costs only its own arguments — and so there is exactly one NULL guard
  /// to get right. <see cref="IEmitterBackend.LoadCurrentP"/> returns NULL on non-scheduler
  /// threads (the IOCP pool), where both fields correctly read 0.
  ///
  /// <paramref name="dataPtr"/> must be a register other than Scratch1/Scratch2.
  /// </summary>
  private void EmitDsStoreLogIdentity(VReg dataPtr) {
    var pNull = UniqueLabel("ds_log_p_null");
    var pDone = UniqueLabel("ds_log_p_done");

    _b.LoadCurrentP(VReg.Scratch1);
    _b.JumpIfZero(VReg.Scratch1, pNull);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffCurrentGt);
    _b.StoreIndirect(dataPtr, DsLogOffGt, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffId);
    _b.StoreIndirect(dataPtr, DsLogOffPid, VReg.Scratch2);
    _b.Jump(pDone);

    _b.DefineLabel(pNull);
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreIndirect(dataPtr, DsLogOffGt, VReg.Scratch2);
    _b.StoreIndirect(dataPtr, DsLogOffPid, VReg.Scratch2);

    _b.DefineLabel(pDone);
  }

  /// <summary>
  /// __ds_emit_log_phase(event_type, name_id, unit_id)
  /// LOG_PHASE_BEGIN / LOG_PHASE_END. Payload: gt(8) p_id(8) phase_id(2) rsvd(2) unit_id(4).
  ///
  /// `name_id` indexes the MXDS_STRS blob, so the span carries a u16 and the monitor prints
  /// the real phase name — no string is built, and nothing is allocated.
  /// </summary>
  public void EmitDsEmitLogPhase() {
    _b.FunctionStart("__ds_emit_log_phase", 3, 0x60);
    // Slots: 0=event_type, 1=name_id, 2=unit_id, 3=data_ptr

    EmitDsEntryBody(dataPtrSlot: 3,
      () => {
        _b.LoadLocal(VReg.Arg0, 0); // event_type
        _b.MovRegImm(VReg.Arg1, DsLogPhaseEntrySize);
      },
      () => {
        EmitDsStoreLogIdentity(VReg.Ret);

        // phase_id(2) | rsvd(2) | unit_id(4). rsvd stays 0 because the masked phase_id
        // occupies only bits [0:15], and the unit_id shift discards anything above bit 31.
        _b.LoadLocal(VReg.Scratch1, 1); // name_id
        _b.MovRegImm(VReg.Scratch2, DsLogU16FieldMask);
        _b.AndRegReg(VReg.Scratch1, VReg.Scratch2);
        _b.LoadLocal(VReg.Scratch2, 2); // unit_id
        _b.ShlRegImm(VReg.Scratch2, DsLogUnitIdShift);
        _b.OrRegReg(VReg.Scratch1, VReg.Scratch2);
        _b.StoreIndirect(VReg.Ret, DsLogOffFields, VReg.Scratch1);
      });

    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_log_event(name_id, cat, lvl, unit_id, arg0, arg1)
  /// LOG_EVENT — the structured, ZERO-ALLOC tier the compiler passes use. Payload reuses the
  /// 48-byte Dbg shape: gt(8) p_id(8) cat(1) lvl(1) event_id(2) unit_id(4) arg0(8) arg1(8).
  /// </summary>
  public void EmitDsEmitLogEvent() {
    _b.FunctionStart("__ds_emit_log_event", 6, 0x60);
    // Slots: 0=name_id, 1=cat, 2=lvl, 3=unit_id, 4=arg0, 5=arg1, 6=data_ptr

    EmitDsEntryBody(dataPtrSlot: 6,
      () => {
        _b.MovRegImm(VReg.Arg0, DsEvLogEvent);
        _b.MovRegImm(VReg.Arg1, DsLogEventEntrySize);
      },
      () => {
        EmitDsStoreLogIdentity(VReg.Ret);

        EmitDsPackLogFields(catSlot: 1, lvlSlot: 2, u16Slot: 0, unitSlot: 3);
        _b.StoreIndirect(VReg.Ret, DsLogOffFields, VReg.Scratch1);

        _b.LoadLocal(VReg.Scratch1, 4); // arg0
        _b.StoreIndirect(VReg.Ret, DsLogOffArg0, VReg.Scratch1);
        _b.LoadLocal(VReg.Scratch1, 5); // arg1
        _b.StoreIndirect(VReg.Ret, DsLogOffArg1, VReg.Scratch1);
      });

    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_log_text(cat, lvl, unit_id, ptr, len)
  /// LOG_TEXT — the rare human message. Payload: gt(8) p_id(8) cat(1) lvl(1) len(2) unit_id(4),
  /// then the UTF-8 bytes, zero-padded to an 8-byte boundary.
  ///
  /// TRUNCATE, NEVER TEAR: `entry_size` is a u16, so a message longer than
  /// <see cref="DsLogTextMaxBytes"/> is clamped rather than split across entries — a split
  /// would need a continuation code the frozen schema does not have, and a reader that
  /// re-assembles it.
  /// </summary>
  public void EmitDsEmitLogText() {
    _b.FunctionStart("__ds_emit_log_text", 5, 0x60);
    // Slots: 0=cat, 1=lvl, 2=unit_id, 3=ptr, 4=len (clamped in place), 5=data_ptr,
    //        6=align8(len)

    EmitDsEntryBody(dataPtrSlot: 5,
      () => {
        var lenOk = UniqueLabel("ds_log_text_len_ok");

        // Clamp the length, and write it back: both the header's len field and the entry size
        // must be computed from the SAME (clamped) value, or the reader walks off the entry.
        _b.LoadLocal(VReg.Scratch1, 4);
        _b.CmpRegImm(VReg.Scratch1, DsLogTextMaxBytes);
        _b.JumpIf(Condition.BelowEqual, lenOk);
        _b.MovRegImm(VReg.Scratch1, DsLogTextMaxBytes);
        _b.StoreLocal(4, VReg.Scratch1);
        _b.DefineLabel(lenOk);

        // entry_size = DsLogTextFixedSize + align8(len)
        _b.MovRegReg(VReg.Scratch2, VReg.Scratch1);
        _b.AddRegImm(VReg.Scratch2, DsEntryAlignBias);
        _b.MovRegImm(VReg.Scratch3, DsEntryAlignMask);
        _b.AndRegReg(VReg.Scratch2, VReg.Scratch3);
        _b.StoreLocal(6, VReg.Scratch2); // aligned tail size, reused by the pad-zeroing below
        _b.AddRegImm(VReg.Scratch2, DsLogTextFixedSize);

        _b.MovRegReg(VReg.Arg1, VReg.Scratch2);
        _b.MovRegImm(VReg.Arg0, DsEvLogText);
      },
      () => {
        var copyLoop = UniqueLabel("ds_log_text_copy");
        var copyDone = UniqueLabel("ds_log_text_copied");

        EmitDsStoreLogIdentity(VReg.Ret);

        EmitDsPackLogFields(catSlot: 0, lvlSlot: 1, u16Slot: 4, unitSlot: 2);
        _b.StoreIndirect(VReg.Ret, DsLogOffFields, VReg.Scratch1);

        // Zero the FINAL qword of the tail before copying. The ring is reused memory, so the pad
        // bytes [len, align8(len)) would otherwise carry a previous entry's payload. Only the
        // last qword can hold padding — align8(len) - 8 < len for every len > 0 — so one store
        // suffices.
        _b.LoadLocal(VReg.Scratch2, 6); // align8(len)
        _b.JumpIfZero(VReg.Scratch2, copyDone);
        _b.MovRegReg(VReg.Arg3, VReg.Ret);
        _b.AddRegImm(VReg.Arg3, DsLogOffText);
        _b.AddRegReg(VReg.Arg3, VReg.Scratch2);
        _b.SubRegImm(VReg.Arg3, DsEntryAlign);
        _b.ZeroReg(VReg.Scratch3);
        _b.StoreIndirect(VReg.Arg3, 0, VReg.Scratch3);

        // Byte-copy the message. The backend's indirect byte ops take a constant offset, so the
        // cursors advance instead of being indexed.
        _b.LoadLocal(VReg.Arg2, 3);      // src
        _b.LoadLocal(VReg.Arg3, 5);      // data_ptr
        _b.AddRegImm(VReg.Arg3, DsLogOffText); // dst
        _b.LoadLocal(VReg.Scratch2, 4);  // remaining
        _b.DefineLabel(copyLoop);
        _b.JumpIfZero(VReg.Scratch2, copyDone);
        _b.LoadIndirectByte(VReg.Scratch3, VReg.Arg2, 0);
        _b.StoreIndirectByte(VReg.Arg3, 0, VReg.Scratch3);
        _b.AddRegImm(VReg.Arg2, 1);
        _b.AddRegImm(VReg.Arg3, 1);
        _b.SubRegImm(VReg.Scratch2, 1);
        _b.Jump(copyLoop);
        _b.DefineLabel(copyDone);

        // The commit that EmitDsEntryBody appends here is the one that matters most in this
        // family: the tail is the longest payload the ring carries, so it is the widest window
        // between "entry visible" and "entry readable" — and the easiest one to tear.
      });

    _b.FunctionEnd();
  }

  /// <summary>
  /// Pack the DsLogOffFields word into Scratch1: cat(1) | lvl(1) | u16(2) | unit_id(4).
  ///
  /// LOG_EVENT and LOG_TEXT pack this word identically — only the meaning of the 16-bit slot
  /// differs (an interned `event_id` for one, a byte `len` for the other) — so they pack HERE,
  /// once. A second copy would eventually disagree with this one about a shift.
  ///
  /// Clobbers Scratch1..Scratch3.
  /// </summary>
  private void EmitDsPackLogFields(int catSlot, int lvlSlot, int u16Slot, int unitSlot) {
    _b.LoadLocal(VReg.Scratch1, catSlot);
    _b.MovRegImm(VReg.Scratch3, DsLogCatMask);
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch3);

    _b.LoadLocal(VReg.Scratch2, lvlSlot);
    _b.MovRegImm(VReg.Scratch3, DsLogLvlMask);
    _b.AndRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.ShlRegImm(VReg.Scratch2, DsLogLvlShift);
    _b.OrRegReg(VReg.Scratch1, VReg.Scratch2);

    _b.LoadLocal(VReg.Scratch2, u16Slot);
    _b.MovRegImm(VReg.Scratch3, DsLogU16FieldMask);
    _b.AndRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.ShlRegImm(VReg.Scratch2, DsLogU16FieldShift);
    _b.OrRegReg(VReg.Scratch1, VReg.Scratch2);

    // unit_id occupies bits [32:63]; the shift itself discards anything that would not fit.
    _b.LoadLocal(VReg.Scratch2, unitSlot);
    _b.ShlRegImm(VReg.Scratch2, DsLogUnitIdShift);
    _b.OrRegReg(VReg.Scratch1, VReg.Scratch2);
  }

  /// <summary>
  /// __ds_emit_depth(event_type)
  /// DEPTH_INC or DEPTH_DEC: header-only event, no payload.
  ///
  /// It still COMMITS. There is nothing to publish, but the monitor's rule is uniform — an entry
  /// it has not seen committed is one it will not decode or step over — so an event that skipped
  /// the commit because "it has no payload" would stop the drain dead at that entry. Going
  /// through the same helper as every other family is what makes that impossible to get wrong.
  /// </summary>
  public void EmitDsEmitDepth() {
    _b.FunctionStart("__ds_emit_depth", 1, 0x30);
    // Slots: 0=event_type, 1=data_ptr

    EmitDsEntryBody(dataPtrSlot: 1,
      () => {
        _b.LoadLocal(VReg.Arg0, 0); // event_type
        _b.MovRegImm(VReg.Arg1, DsEntryHeaderSize); // header only
      },
      writePayload: () => { });

    _b.FunctionEnd();
  }

  // =========================================================================
  // Emit all debugstream functions
  // =========================================================================

  /// <summary>
  /// Emit all debugstream runtime functions. Call this when Compiler.DebugStream is true.
  /// </summary>
  public void EmitDebugStreamFunctions(List<string?> tagNames, List<string?> logNames) {
    EmitDebugStreamGlobals();
    EmitDebugStreamWriteGlobals();
    EmitDebugStreamNameBlob(DsTagTableMagic, "__ds_tag_table", tagNames);
    EmitDebugStreamNameBlob(DsStrTableMagic, "__ds_str_table", logNames);
    EmitDebugStreamInit();
    EmitDebugStreamShutdown();
    EmitDsReserve();
    EmitDsCommit();
    EmitDsEmitMmAlloc();
    EmitDsEmitMmFree();
    EmitDsEmitMmRefcount();
    EmitDsEmitMmRawAlloc();
    EmitDsEmitMmRawFree();
    EmitDsEmitSched();
    EmitDsEmitDepth();
    EmitDsEmitDbg();
    EmitDsEmitLogPhase();
    EmitDsEmitLogEvent();
    EmitDsEmitLogText();
  }

  // Magic bytes at the start of each interned-name blob in symdata, so the monitor can find
  // it by scanning the PE. MXDS_TAGS holds the mm allocation TYPE names; MXDS_STRS holds the
  // names the `__DebugStream` builtin interned at compile time (phase names, event names).
  // Two blobs, one format — a Log event carries a u16 into MXDS_STRS and stays zero-alloc.
  public static readonly byte[] DsTagTableMagic = "MXDS_TAGS\0"u8.ToArray();
  public static readonly byte[] DsStrTableMagic = "MXDS_STRS\0"u8.ToArray();

  /// <summary>
  /// Emit a packed name-table blob into symdata so the monitor can resolve a u16 index back
  /// to a real name by parsing the executable — no name is ever built at runtime.
  /// Format: [magic (10 bytes)][count:u16][len0:u16][name0 bytes]...[lenN:u16][nameN bytes]
  /// </summary>
  public void EmitDebugStreamNameBlob(byte[] magic, string symdataLabel, List<string?> names) {
    var blob = new List<byte>();
    blob.AddRange(magic);

    ushort count = (ushort)names.Count;
    blob.Add((byte)(count & 0xFF));
    blob.Add((byte)(count >> 8));

    foreach (var entry in names) {
      var nameBytes = System.Text.Encoding.UTF8.GetBytes(entry ?? "");
      ushort len = (ushort)nameBytes.Length;
      blob.Add((byte)(len & 0xFF));
      blob.Add((byte)(len >> 8));
      blob.AddRange(nameBytes);
    }

    _b.DefineSymdata(symdataLabel, [.. blob]);
  }
}
