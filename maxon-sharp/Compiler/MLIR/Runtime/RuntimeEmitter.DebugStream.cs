using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;

namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// DebugStream: shared-memory ring buffer for high-performance binary trace output.
/// The monitor (maxon monitor) creates named shared memory and spawns the target.
/// The target opens the pre-existing segment via the MAXON_DEBUGSTREAM env var,
/// writes binary events into the ring buffer, and unmaps on shutdown.
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
  /// Emit __ds_reserve(entry_size_aligned) -> pointer to write location, or 0 if full.
  /// Also writes the entry header (event_type, flags, entry_size, timestamp_delta).
  ///
  /// Args: Arg0 = event_type (byte), Arg1 = entry_size (total, 8-byte aligned)
  /// Returns: Ret = pointer to entry start in shared memory (0 if dropped)
  ///
  /// Stack slots: 0=event_type, 1=entry_size, 2=base, 3=buf_size, 4=buf_mask
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
    // Padding header: event_type=0xFF, flags=0, entry_size=pad_size, timestamp=0
    // Pack: 0xFF | (0 << 8) | (pad_size << 16) | (0 << 32)
    // = 0xFF | (pad_size << 16)
    _b.MovRegReg(VReg.Scratch2, VReg.Arg1); // pad_size
    _b.ShlRegImm(VReg.Scratch2, 16);
    _b.AddRegImm(VReg.Scratch2, DsEvPadding); // 0xFF
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
    //   [0:7] = event_type, [8:15] = 0, [16:31] = entry_size, [32:63] = timestamp_delta
    _b.LoadLocal(VReg.Scratch3, 0); // event_type (in low byte)
    _b.LoadLocal(VReg.Arg0, 1);     // entry_size
    _b.ShlRegImm(VReg.Arg0, 16);
    _b.AddRegReg(VReg.Scratch3, VReg.Arg0); // event_type | (entry_size << 16)
    _b.ShlRegImm(VReg.Scratch2, 32);
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch2); // | (timestamp << 32)
    _b.LoadLocal(VReg.Scratch0, 8); // reload data_ptr
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch3); // write header

    // Advance write_cursor
    _b.LoadLocal(VReg.Scratch1, 5); // wr
    _b.LoadLocal(VReg.Arg0, 1); // entry_size
    _b.AddRegReg(VReg.Scratch1, VReg.Arg0); // wr += entry_size
    _b.LoadLocal(VReg.Scratch2, 2); // base
    _b.StoreIndirect(VReg.Scratch2, DsOffWriteCursor, VReg.Scratch1);

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
  // Per-event-type emitters
  // =========================================================================

  /// <summary>
  /// __ds_emit_mm_alloc(packed_id, alloc_size, scope_ptr)
  /// Writes an MM_ALLOC event: alloc_id(8), tag_index(2), scope_len(2), size(4), [scope_str]
  /// </summary>
  public void EmitDsEmitMmAlloc() {
    _b.FunctionStart("__ds_emit_mm_alloc", 3, 0x60);
    // Slots: 0=packed_id, 1=alloc_size, 2=scope_ptr

    var done = UniqueLabel("ds_mm_alloc_done");

    // Fixed size = 8 (header) + 8 (alloc_id) + 8 (tag+scope_len+size) = 24
    _b.MovRegImm(VReg.Arg0, DsEvMmAlloc);
    _b.MovRegImm(VReg.Arg1, 24);
    _b.Call("__ds_reserve");
    _b.JumpIfZero(VReg.Ret, done);
    // Ret = data_ptr (header already written by __ds_reserve)

    // Write payload at data_ptr + 8
    // alloc_id = packed_id >> 16
    _b.LoadLocal(VReg.Scratch1, 0); // packed_id
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.ShrRegImm(VReg.Scratch2, 16); // alloc_id
    _b.StoreIndirect(VReg.Ret, 8, VReg.Scratch2);

    // tag_index (low 16 bits of packed_id) + scope_len=0 + alloc_size
    // Pack: tag_index(2) | scope_len(2) | alloc_size(4) into 8 bytes at offset 16
    _b.MovRegImm(VReg.Scratch2, 0xFFFF);
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2); // tag_index = packed_id & 0xFFFF
    _b.LoadLocal(VReg.Scratch2, 1); // alloc_size
    _b.ShlRegImm(VReg.Scratch2, 32); // shift size to bits [32:63]
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2); // tag_index | (0 << 16) | (size << 32)
    _b.StoreIndirect(VReg.Ret, 16, VReg.Scratch1);

    _b.DefineLabel(done);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_mm_free(packed_id, scope_ptr)
  /// Writes an MM_FREE event: alloc_id(8), tag_index(2), pad(6)
  /// </summary>
  public void EmitDsEmitMmFree() {
    _b.FunctionStart("__ds_emit_mm_free", 2, 0x40);

    _b.MovRegImm(VReg.Arg0, DsEvMmFree);
    _b.MovRegImm(VReg.Arg1, 24);
    _b.Call("__ds_reserve");
    var done = UniqueLabel("ds_mm_free_done");
    _b.JumpIfZero(VReg.Ret, done);

    // alloc_id
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.ShrRegImm(VReg.Scratch2, 16);
    _b.StoreIndirect(VReg.Ret, 8, VReg.Scratch2);

    // tag_index
    _b.MovRegImm(VReg.Scratch2, 0xFFFF);
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.StoreIndirect(VReg.Ret, 16, VReg.Scratch1);

    _b.DefineLabel(done);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_mm_refcount(event_type, packed_id, new_refcount, scope_ptr)
  /// Writes MM_INCREF/MM_DECREF/MM_TRANSFER event: alloc_id(8), tag_index(2), scope_len(2), new_rc(4)
  /// </summary>
  public void EmitDsEmitMmRefcount() {
    _b.FunctionStart("__ds_emit_mm_refcount", 4, 0x60);
    // Slots: 0=event_type, 1=packed_id, 2=new_refcount, 3=scope_ptr

    _b.LoadLocal(VReg.Arg0, 0); // event_type
    _b.MovRegImm(VReg.Arg1, 24);
    _b.Call("__ds_reserve");
    var done = UniqueLabel("ds_mm_rc_done");
    _b.JumpIfZero(VReg.Ret, done);

    // alloc_id
    _b.LoadLocal(VReg.Scratch1, 1); // packed_id
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.ShrRegImm(VReg.Scratch2, 16);
    _b.StoreIndirect(VReg.Ret, 8, VReg.Scratch2);

    // tag_index(2) | scope_len=0(2) | new_rc(4)
    _b.MovRegImm(VReg.Scratch2, 0xFFFF);
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2); // tag_index
    _b.LoadLocal(VReg.Scratch2, 2); // new_refcount
    _b.ShlRegImm(VReg.Scratch2, 32);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.StoreIndirect(VReg.Ret, 16, VReg.Scratch1);

    _b.DefineLabel(done);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_mm_raw_alloc(raw_id, size)
  /// </summary>
  public void EmitDsEmitMmRawAlloc() {
    _b.FunctionStart("__ds_emit_mm_raw_alloc", 2, 0x40);

    _b.MovRegImm(VReg.Arg0, DsEvMmRawAlloc);
    _b.MovRegImm(VReg.Arg1, 24);
    _b.Call("__ds_reserve");
    var done = UniqueLabel("ds_mm_raw_alloc_done");
    _b.JumpIfZero(VReg.Ret, done);

    _b.LoadLocal(VReg.Scratch1, 0); // raw_id
    _b.StoreIndirect(VReg.Ret, 8, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 1); // size
    _b.StoreIndirect(VReg.Ret, 16, VReg.Scratch1);

    _b.DefineLabel(done);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_mm_raw_free(raw_id)
  /// </summary>
  public void EmitDsEmitMmRawFree() {
    _b.FunctionStart("__ds_emit_mm_raw_free", 1, 0x30);

    _b.MovRegImm(VReg.Arg0, DsEvMmRawFree);
    _b.MovRegImm(VReg.Arg1, 16);
    _b.Call("__ds_reserve");
    var done = UniqueLabel("ds_mm_raw_free_done");
    _b.JumpIfZero(VReg.Ret, done);

    _b.LoadLocal(VReg.Scratch1, 0); // raw_id
    _b.StoreIndirect(VReg.Ret, 8, VReg.Scratch1);

    _b.DefineLabel(done);
    _b.FunctionEnd();
  }

  /// <summary>
  /// __ds_emit_sched(event_type, trace_id)
  /// Generic scheduler event: just trace_id(8) as payload.
  /// </summary>
  public void EmitDsEmitSched() {
    _b.FunctionStart("__ds_emit_sched", 2, 0x40);

    _b.LoadLocal(VReg.Arg0, 0); // event_type
    _b.MovRegImm(VReg.Arg1, 16);
    _b.Call("__ds_reserve");
    var done = UniqueLabel("ds_sched_done");
    _b.JumpIfZero(VReg.Ret, done);

    _b.LoadLocal(VReg.Scratch1, 1); // trace_id
    _b.StoreIndirect(VReg.Ret, 8, VReg.Scratch1);

    _b.DefineLabel(done);
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
  /// Total entry = 8 (header) + 40 (payload) = 48 bytes.
  ///
  /// All callers should pass arg2..arg4 = 0 if unused. Helpers below
  /// (`EmitDbg*`) provide named-argument convenience wrappers.
  /// </summary>
  public void EmitDsEmitDbg() {
    _b.FunctionStart("__ds_emit_dbg", 6, 0x60);
    // Slots: 0=event_type, 1=gt, 2=p_id, 3=arg2, 4=arg3, 5=arg4

    _b.LoadLocal(VReg.Arg0, 0); // event_type
    _b.MovRegImm(VReg.Arg1, 48);
    _b.Call("__ds_reserve");
    var done = UniqueLabel("ds_dbg_done");
    _b.JumpIfZero(VReg.Ret, done);
    // Ret = data_ptr (header already written by __ds_reserve)

    _b.LoadLocal(VReg.Scratch1, 1); // gt
    _b.StoreIndirect(VReg.Ret, 8, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 2); // p_id
    _b.StoreIndirect(VReg.Ret, 16, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 3); // arg2
    _b.StoreIndirect(VReg.Ret, 24, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 4); // arg3
    _b.StoreIndirect(VReg.Ret, 32, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 5); // arg4
    _b.StoreIndirect(VReg.Ret, 40, VReg.Scratch1);

    _b.DefineLabel(done);
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

  /// <summary>kind: 0=local, 1=global, 2=steal_chain, 3=runnext.</summary>
  public void EmitDbgEnqueue(VReg gt, long kind, long ownerPid) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgEnqueue, gt, kind, ownerPid, 0);
  }

  /// <summary>kind: 0=local, 1=global, 2=steal_first, 3=runnext.</summary>
  public void EmitDbgDequeue(VReg gt, long kind, long fromPid) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgDequeue, gt, kind, fromPid, 0);
  }

  /// <summary>Site IDs are DsStatusSite* constants in RuntimeEmitter.cs.</summary>
  public void EmitDbgStatusStore(VReg gt, long oldStatus, long newStatus, long siteId) {
    if (!Compiler.DebugStream) return;
    EmitDbgCallImm(DsEvDbgStatusStore, gt, oldStatus, newStatus, siteId);
  }

  /// <summary>phase: 0=status_set, 1=spin_done, 2=enqueueing.</summary>
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

  /// <summary>
  /// __ds_emit_depth(event_type)
  /// DEPTH_INC or DEPTH_DEC: header-only event, no payload.
  /// </summary>
  public void EmitDsEmitDepth() {
    _b.FunctionStart("__ds_emit_depth", 1, 0x30);

    _b.LoadLocal(VReg.Arg0, 0); // event_type
    _b.MovRegImm(VReg.Arg1, 8); // header only
    _b.Call("__ds_reserve");
    // No payload to write

    _b.FunctionEnd();
  }

  // =========================================================================
  // Emit all debugstream functions
  // =========================================================================

  /// <summary>
  /// Emit all debugstream runtime functions. Call this when Compiler.DebugStream is true.
  /// </summary>
  public void EmitDebugStreamFunctions(List<string?> tagNames) {
    EmitDebugStreamGlobals();
    EmitDebugStreamWriteGlobals();
    EmitDebugStreamTagBlob(tagNames);
    EmitDebugStreamInit();
    EmitDebugStreamShutdown();
    EmitDsReserve();
    EmitDsEmitMmAlloc();
    EmitDsEmitMmFree();
    EmitDsEmitMmRefcount();
    EmitDsEmitMmRawAlloc();
    EmitDsEmitMmRawFree();
    EmitDsEmitSched();
    EmitDsEmitDepth();
    EmitDsEmitDbg();
  }

  // Magic bytes at the start of the tag table blob in symdata, so the monitor can find it by scanning.
  public static readonly byte[] DsTagTableMagic = "MXDS_TAGS\0"u8.ToArray();

  /// <summary>
  /// Emit a packed tag table blob into symdata so the monitor can extract type names
  /// by parsing the PE executable. The blob has a magic prefix for scanning.
  /// Format: [magic: "MXDS_TAGS\0" (10 bytes)][count:u16][len0:u16][name0 bytes]...[lenN:u16][nameN bytes]
  /// </summary>
  public void EmitDebugStreamTagBlob(List<string?> tagNames) {
    var blob = new List<byte>();
    blob.AddRange(DsTagTableMagic);

    ushort count = (ushort)tagNames.Count;
    blob.Add((byte)(count & 0xFF));
    blob.Add((byte)(count >> 8));

    for (int i = 0; i < tagNames.Count; i++) {
      var name = tagNames[i] ?? "";
      var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
      ushort len = (ushort)nameBytes.Length;
      blob.Add((byte)(len & 0xFF));
      blob.Add((byte)(len >> 8));
      blob.AddRange(nameBytes);
    }

    _b.DefineSymdata("__ds_tag_table", [.. blob]);
  }
}
