using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;

namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// Go-inspired three-tier slab allocator for the Maxon runtime.
///
/// Architecture:
///   Per-P mcache  (lock-free fast path: one cached mspan per size class per P)
///        |  refill
///     mcentral    (one per size class, locked)
///        |  grow
///      arena      (64MB bitmap-based chunk allocator, 8KB chunks)
///
/// Size classes 0-17 cover allocations from 8 to 32768 bytes.
/// Allocations 32769-64MB go through arena-large (mspan with class_index=-1).
/// Allocations larger than 64MB go directly to OsAllocPages (OS-direct).
///
/// Header-free design: there is no per-object header. Instead, a two-level
/// arena map (L1 -> L2 -> spans[]) maps any pointer back to its owning mspan.
/// __slab_free always receives slot_base directly (mm_free subtracts MmHeaderSize,
/// mm_raw_free passes the slab_user_ptr which IS slot_base), so no division is needed.
///
/// Each 64MB arena reserves chunk 0 for metadata: next_arena pointer, arena_size,
/// and a 1024-byte bitmap (8192 bits, one per 8KB chunk). Bit=1 means free, bit=0 means used.
///
/// OS-direct allocations (larger than 64MB) are tracked via a dynamic array of
/// (ptr, size) pairs so they can be freed without the arena map.
/// </summary>
public partial class RuntimeEmitter {

  // =========================================================================
  // Size class tables
  // =========================================================================

  public static readonly int[] SlabClassSizes = [
    8, 16, 24, 32, 48, 64, 96, 128,
    192, 256, 384, 512, 1024, 2048, 4096,
    8192, 16384, 32768
  ];

  private static readonly int[] SlabObjsPerSpan = [
    1024, 512, 341, 256, 170, 128, 85, 64,
    42, 32, 21, 16, 8, 4, 2,
    4, 2, 1
  ];

  // Chunk size = 8KB (arena page size)
  private const int ChunkShift = 13;
  private const int ChunkSize = 1 << ChunkShift; // 8192
  private const int ChunksPerArena = ArenaSize >> ChunkShift; // 8192

  // Arena metadata layout at chunk 0:
  //   +0x000: next_arena (8 bytes)
  //   +0x008: (reserved, 8 bytes)
  //   +0x010: bitmap     (1024 bytes, 8192 bits, 1=free, 0=used)
  private const int ArenaMetaOffNext = 0x00;
  private const int ArenaMetaOffBitmap = 0x10;

  private const int SlabNumClasses = 18;
  private const int OsTraceScratchSlot = 7;
  private const int SlabMaxSmallSize = 32768;
  private const int ArenaSize = 64 * 1024 * 1024; // 64MB

  // mspan struct layout (72 bytes used; 80-byte slot for 16-byte alignment)
  private const int MspanOffBaseAddr = 0x00;
  private const int MspanOffSlotSize = 0x08;
  private const int MspanOffFreeList = 0x10;
  private const int MspanOffFreeCount = 0x18;
  private const int MspanOffNextSpan = 0x20;
  private const int MspanOffClassIndex = 0x28;
  private const int MspanOffTotalSlots = 0x30;
  private const int MspanOffArenaBase = 0x38;
  // Mimalloc-style owning-P identifier. The P that currently owns this span has
  // exclusive write access to span->free_list (via __slab_alloc fast path and the
  // same-thread __slab_free local path). 0xFFFFFFFF means "in mcentral / unowned"
  // — see __slab_mcentral_return_span.
  private const int MspanOffOwningP = 0x40;
  // 0x48 is RESERVED so this layout stays field-for-field identical to the
  // self-hosted mspan (stdlib/Internals.maxon), where 0x48 is the scavenger's
  // grace state. The C# runtime has no scavenger, so the word is unused here.
  //
  // Go's mspan.freeindex, held as an address rather than an index: slots in
  // [bump_next, bump_end) have NEVER been handed out, so they still hold the
  // zeroes the OS gave us and need no memzero on alloc. bump_end is DERIVED,
  // never stored: base_addr + slot_size * total_slots.
  private const int MspanOffBumpNext = 0x50;
  // Bump-allocator slot for an mspan header rounded up to 16-byte alignment so
  // successive headers stay 16-byte aligned within the metadata chunk. The
  // mspan field block runs from 0x00..0x58; the trailing 8 bytes are padding.
  private const int MspanMetaSlotSize = 0x60; // 96 bytes
  private const uint MspanOwningPSentinel = 0xFFFFFFFFu; // span in mcentral, no owner

  // Arena map: two-level radix tree for pointer -> span lookup
  private const int ArenaMapL1Size = 256;
  private const int ArenaMapL2Size = 8192;
  private const int ArenaPageShift = 13;       // = ChunkShift
  private const int ArenaPagesPerArena = ArenaSize >> ArenaPageShift; // 8192
  private const int ArenaMapL1Shift = 39;
  private const int ArenaMapL2Shift = 26;
  private const int ArenaMapL2Mask = ArenaMapL2Size - 1; // 0x1FFF
  private const int ArenaMapPageMask = ArenaPagesPerArena - 1; // 0x1FFF

  // mcentral struct layout: 16 bytes per class (partial_head + full_head)
  private const int McentralEntrySize = 16;

  // Global data labels
  private const string McentralArrayLabel = "__slab_mcentral_array";
  private const string MspanPoolLockLabel = "__slab_mspan_pool_lock";
  private const string McacheBaseLabel = "__slab_mcache_base";
  private const string SlabClassSizesLabel = "__slab_class_sizes";
  private const string SlabObjsLabel = "__slab_objs_per_span";
  private const string SlabInitDoneLabel = "__slab_init_done";

  // -------------------------------------------------------------------------
  // MAXON_SLAB_GLOBAL_LOCK A/B safety net (PLAN 1a.1) + MAXON_SLAB_STATS
  // contention counters (PLAN 1a.2). All runtime-cached flags are set once from
  // their env vars during scheduler init (per-backend __gt_init) and read on the
  // allocator hot path as a single correctly-predicted branch when unset.
  // -------------------------------------------------------------------------
  // The global lock itself: a test-and-set spinlock word (0 = free, 1 = held).
  // Deliberately NOT a kernel-wait lock: a contended EnterCriticalSection on a
  // green thread's small stack once corrupted the self-hosted runtime — a
  // test-and-set sidesteps the kernel entirely.
  private const string SlabGlobalLockLabel = "__slab_global_lock";
  // Runtime-cached "MAXON_SLAB_GLOBAL_LOCK is set" flag. Public so the per-backend
  // env-read in __gt_init can publish it. 0 = disabled (default hot path).
  public const string SlabGlobalLockEnabledLabel = "__slab_global_lock_enabled";
  // Runtime-cached "MAXON_SLAB_STATS is set" flag. Public for the same reason.
  public const string SlabStatsEnabledLabel = "__slab_stats_enabled";
  // Counters (atomic; they run under multi-P). Only touched when stats are on.
  private const string SlabLockWaitCountLabel = "__slab_lock_wait_count";
  private const string SlabOwnershipGateMissCountLabel = "__slab_ownership_gate_miss_count";
  // Cross-P remote-free MPSC pushes (PLAN 1a.3). Bumped whenever __slab_free
  // routes a slot onto its owner P's remote_free queue instead of freeing it
  // locally — i.e. the freed span's owning_p != the freeing thread's P. This is
  // the direct observability of the never-run cross-P free path; a value > 0
  // proves the torture harness actually exercised it.
  private const string SlabRemoteFreeCountLabel = "__slab_remote_free_count";
  // Exit-dump line fragments (stderr only — must never pollute program stdout).
  private const string SlabStatsPrefixLabel = "__slab_stats_prefix";
  private const string SlabStatsMidLabel = "__slab_stats_mid";
  private const string SlabStatsRemoteFreeLabel = "__slab_stats_remote_free";
  private const string SlabStatsNewlineLabel = "__slab_stats_newline";

  // Spinlock word sentinels.
  private const long SlabGlobalLockFree = 0;
  private const long SlabGlobalLockHeld = 1;

  // Arena list (linked list of 64MB arenas via chunk 0 metadata)
  private const string ArenaListHeadLabel = "__slab_arena_list_head";
  // Last arena_base from __slab_arena_alloc_chunks (for callers to read)
  private const string ArenaLastBaseLabel = "__slab_arena_last_base";

  // Arena map globals
  private const string ArenaMapL1Label = "__slab_arena_map_l1";

  // Metadata slab (64-byte slots for mspan headers etc.)
  private const string MetaFreeHeadLabel = "__slab_meta_free_head";
  private const string MetaBumpPtrLabel = "__slab_meta_bump_ptr";
  private const string MetaBumpEndLabel = "__slab_meta_bump_end";

  // OS-direct tracking array (dynamic array of (ptr, size) pairs)
  private const string OsDirectArrayLabel = "__slab_os_direct_array";
  private const string OsDirectCountLabel = "__slab_os_direct_count";
  private const string OsDirectCapacityLabel = "__slab_os_direct_capacity";

  // Raw alloc ID tracking list (trace only)
  private const string RawAllocIdListLabel = "__mm_raw_alloc_id_list";

  // Lock labels for mcentral (18 separate locks)
  private static string McentralLockLabel(int classIndex) =>
    $"__slab_mcentral_lock_{classIndex}";

  // =========================================================================
  // EmitAllocatorGlobals
  // =========================================================================
  public void EmitAllocatorGlobals() {
    // mcentral array: 18 entries * 16 bytes = 288 bytes
    _b.DefineGlobal(McentralArrayLabel, SlabNumClasses * McentralEntrySize, 0);

    // mcache base pointer
    _b.DefineGlobal(McacheBaseLabel, 8, 0);

    // Init done flag
    _b.DefineGlobal(SlabInitDoneLabel, 8, 0);

    // Global-lock A/B safety net + contention counters (PLAN 1a.1 / 1a.2).
    _b.DefineGlobal(SlabGlobalLockLabel, 8, 0);         // spinlock word (0=free / 1=held)
    _b.DefineGlobal(SlabGlobalLockEnabledLabel, 8, 0);  // runtime-cached MAXON_SLAB_GLOBAL_LOCK flag
    _b.DefineGlobal(SlabStatsEnabledLabel, 8, 0);       // runtime-cached MAXON_SLAB_STATS flag
    _b.DefineGlobal(SlabLockWaitCountLabel, 8, 0);      // failed-CAS spin iterations (real contention)
    _b.DefineGlobal(SlabOwnershipGateMissCountLabel, 8, 0); // fast-path ownership-gate misses (cross-P traffic)
    _b.DefineGlobal(SlabRemoteFreeCountLabel, 8, 0);    // cross-P remote-free MPSC pushes (cross-P frees)
    _b.DefineSymdata(SlabStatsPrefixLabel, "[slab-stats] lock_wait=\0"u8.ToArray());
    _b.DefineSymdata(SlabStatsMidLabel, " ownership_gate_miss=\0"u8.ToArray());
    _b.DefineSymdata(SlabStatsRemoteFreeLabel, " remote_free=\0"u8.ToArray());
    _b.DefineSymdata(SlabStatsNewlineLabel, "\n\0"u8.ToArray());

    // Arena list head and last-base
    _b.DefineGlobal(ArenaListHeadLabel, 8, 0);
    _b.DefineGlobal(ArenaLastBaseLabel, 8, 0);

    // Lock for arena allocation
    if (_b.IsWindows) {
      _b.DefineGlobal(MspanPoolLockLabel, 40, 0); // CRITICAL_SECTION
    } else {
      _b.DefineGlobal(MspanPoolLockLabel, 24, 0); // recursive spinlock: [lock(8), owner(8), count(8)]
    }

    // Per-class mcentral locks
    for (int i = 0; i < SlabNumClasses; i++) {
      if (_b.IsWindows) {
        _b.DefineGlobal(McentralLockLabel(i), 40, 0);
      } else {
        _b.DefineGlobal(McentralLockLabel(i), 8, 0);
      }
    }

    // Arena map L1 base pointer
    _b.DefineGlobal(ArenaMapL1Label, 8, 0);

    // Metadata slab globals
    _b.DefineGlobal(MetaFreeHeadLabel, 8, 0);
    _b.DefineGlobal(MetaBumpPtrLabel, 8, 0);
    _b.DefineGlobal(MetaBumpEndLabel, 8, 0);

    // OS-direct tracking array
    _b.DefineGlobal(OsDirectArrayLabel, 8, 0);
    _b.DefineGlobal(OsDirectCountLabel, 8, 0);
    _b.DefineGlobal(OsDirectCapacityLabel, 8, 0);

    // Raw alloc ID tracking list (trace only)
    _b.DefineGlobal(RawAllocIdListLabel, 8, 0);

    // Size class lookup tables as symdata (read-only)
    var classSizesData = new byte[SlabNumClasses * 8];
    var objsPerSpanData = new byte[SlabNumClasses * 8];
    for (int i = 0; i < SlabNumClasses; i++) {
      BitConverter.TryWriteBytes(classSizesData.AsSpan(i * 8), (long)SlabClassSizes[i]);
      BitConverter.TryWriteBytes(objsPerSpanData.AsSpan(i * 8), (long)SlabObjsPerSpan[i]);
    }
    _b.DefineSymdata(SlabClassSizesLabel, classSizesData);
    _b.DefineSymdata(SlabObjsLabel, objsPerSpanData);
  }

  // =========================================================================
  // EmitSlabMemzero: __slab_memzero(ptr, size)
  //
  // Zeroes `size` bytes starting at `ptr`, ROUNDED UP to a whole number of
  // qwords: ceil(size / 8). On x86: REP STOSQ. On ARM64: tight STR loop.
  //
  // Rounding UP (rather than truncating) is deliberate, and it is safe:
  //   * Every slab-internal caller (slot_size, span footprint, chunk run) passes
  //     a multiple of 8 already, so the rounding is a no-op for them.
  //   * The one caller that does NOT is mm_realloc's grown-tail zero, whose
  //     region ends at raw + MmHeaderSize + new_size. The enclosing slot's class
  //     size is a multiple of 8 and is >= MmHeaderSize + new_size, hence also
  //     >= roundup8(MmHeaderSize + new_size) — so the overshoot always lands
  //     inside the same slot and can never touch a neighbour.
  // Truncating instead would leave up to 7 bytes of that tail holding the
  // previous occupant's bytes, which is exactly the class of bug this design
  // exists to eliminate.
  // =========================================================================
  // Stack slots: 0=ptr, 1=size
  public void EmitSlabMemzero() {
    _b.FunctionStart("__slab_memzero", 2, 0x20);

    var done = UniqueLabel("slab_memzero_done");
    _b.LoadLocal(VReg.Arg0, 1); // size
    _b.JumpIfZero(VReg.Arg0, done);

    _b.LoadLocal(VReg.Arg5, 0);    // dest ptr (RDI on x86)
    _b.ZeroReg(VReg.Scratch0);     // value = 0 (RAX on x86)
    _b.AddRegImm(VReg.Arg0, 7);    // count = ceil(size / 8)
    _b.ShrRegImm(VReg.Arg0, 3);
    _b.FillMemoryQwords(VReg.Arg5, VReg.Scratch0, VReg.Arg0);

    _b.DefineLabel(done);
    _b.FunctionEnd();
  }

  /// <summary>
  /// Inline helper: fill bitmap (128 qwords) with all-1s via FillMemoryQwords,
  /// then clear bit 0 (metadata chunk). <paramref name="baseSlot"/> holds the arena base.
  /// Clobbers Scratch0, Arg0, Arg5.
  /// </summary>
  private void EmitBitmapInitAndClearBit0(int baseSlot) {
    // dest = arena_base + ArenaMetaOffBitmap
    _b.LoadLocal(VReg.Arg5, baseSlot);
    _b.AddRegImm(VReg.Arg5, ArenaMetaOffBitmap);
    // value = -1 (all bits free)
    _b.MovRegImm(VReg.Scratch0, -1);
    // count = 128 qwords
    _b.MovRegImm(VReg.Arg0, 128);
    _b.FillMemoryQwords(VReg.Arg5, VReg.Scratch0, VReg.Arg0);

    // Clear bit 0 (metadata chunk is always used)
    _b.LoadLocal(VReg.Scratch0, baseSlot);
    _b.ZeroReg(VReg.Scratch1);
    _b.BitTestAndReset(VReg.Scratch0, ArenaMetaOffBitmap, VReg.Scratch1);
  }

  // =========================================================================
  // EmitOsAllocPages: __slab_os_alloc(size) -> ptr
  //
  // Allocates `size` bytes from the OS with large-page preference.
  // =========================================================================
  // Stack slots: 0=size, 1=ptr. OsTraceScratchSlot=7 requires frame >= 0x40.
  public void EmitOsAllocPages(bool mmTrace) {
    _b.FunctionStart("__slab_os_alloc", 1, 0x50);

    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.OsAllocLargePages(VReg.Scratch1, VReg.Scratch0); // NULL on failure
    var gotPages = UniqueLabel("os_alloc_got");
    _b.JumpIfNonZero(VReg.Scratch1, gotPages);

    _b.LoadLocal(VReg.Scratch0, 0);
    _b.OsAllocPages(VReg.Scratch1, VReg.Scratch0);

    _b.DefineLabel(gotPages);
    // Both the large-page and regular-page paths return NULL on failure. Every
    // caller (__slab_arena_alloc_chunks, __slab_mspan_alloc, the os-direct
    // paths) stores through this pointer unchecked, so a NULL here would fault
    // as an opaque [NULL] access violation deep in an unrelated frame. Convert
    // it to a clean, unconditional "out of memory" panic here at the source.
    var allocOk = UniqueLabel("os_alloc_ok");
    _b.JumpIfNonZero(VReg.Scratch1, allocOk);
    _b.LeaSymdata(VReg.Arg0, "__slab_panic_oom");
    _b.Call("mrt_panic");
    _b.DefineLabel(allocOk);
    _b.StoreLocal(1, VReg.Scratch1); // save ptr
    if (mmTrace) {
      _b.LoadLocal(VReg.Scratch0, 0); // size
      EmitInlineTraceOsAlloc(UniqueLabel("os_alloc_trace"), VReg.Scratch0);
    }
    _b.LoadLocal(VReg.Scratch0, 1); // ptr
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitArenaMapEnsure: __slab_arena_map_ensure(addr)
  //
  // Ensures L2 and spans[] arrays exist in the arena map for the given address.
  // Called while the arena lock is held.
  // =========================================================================
  // Stack slots: 0=addr, 1=l1_slot_addr/l2_slot_addr, 2=l2_ptr/spans_ptr. Frame 0x30.
  public void EmitArenaMapEnsure() {
    _b.FunctionStart("__slab_arena_map_ensure", 1, 0x30);

    // If L1 array not allocated yet (during __slab_init bootstrap), skip
    _b.LoadGlobal(VReg.Scratch0, ArenaMapL1Label);
    var l1Ready = UniqueLabel("arena_map_l1_ready");
    _b.JumpIfNonZero(VReg.Scratch0, l1Ready);
    _b.FunctionEnd();
    _b.DefineLabel(l1Ready);

    // l1_index = addr >> ArenaMapL1Shift
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ShrRegImm(VReg.Scratch0, ArenaMapL1Shift);

    // l1_base = global[ArenaMapL1Label]
    _b.LoadGlobal(VReg.Scratch1, ArenaMapL1Label);

    // l2_ptr = l1_base[l1_index * 8]
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch0);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch2, 0);

    var l2Exists = UniqueLabel("arena_map_l2_exists");
    var l2Ready = UniqueLabel("arena_map_l2_ready");
    _b.JumpIfNonZero(VReg.Scratch3, l2Exists);

    // Allocate L2 array: ArenaMapL2Size * 8 = 65536 bytes = 8 chunks
    _b.StoreLocal(1, VReg.Scratch2); // save l1_slot_addr
    _b.MovRegImm(VReg.Arg0, 8); // 8 chunks
    _b.Call("__slab_arena_alloc_chunks");
    _b.StoreLocal(2, VReg.Scratch0);

    // __slab_memzero(l2_ptr, ArenaMapL2Size * 8)
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.MovRegImm(VReg.Arg1, ArenaMapL2Size * 8);
    _b.Call("__slab_memzero");

    // l1_base[l1_index * 8] = l2_ptr
    _b.LoadLocal(VReg.Scratch2, 1);
    _b.LoadLocal(VReg.Scratch3, 2);
    _b.StoreIndirect(VReg.Scratch2, 0, VReg.Scratch3);
    _b.Jump(l2Ready);

    _b.DefineLabel(l2Exists);
    _b.StoreLocal(2, VReg.Scratch3);

    _b.DefineLabel(l2Ready);

    // l2_index = (addr >> ArenaMapL2Shift) & ArenaMapL2Mask
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ShrRegImm(VReg.Scratch0, ArenaMapL2Shift);
    _b.MovRegImm(VReg.Scratch1, ArenaMapL2Mask);
    _b.AndRegReg(VReg.Scratch0, VReg.Scratch1);

    // spans_ptr = l2_ptr[l2_index * 8]
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch0);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch2, 0);

    var spansExist = UniqueLabel("arena_map_spans_exist");
    _b.JumpIfNonZero(VReg.Scratch3, spansExist);

    // Allocate spans array: ArenaPagesPerArena * 8 = 65536 bytes = 8 chunks
    _b.StoreLocal(1, VReg.Scratch2); // save l2_slot_addr
    _b.MovRegImm(VReg.Arg0, 8); // 8 chunks
    _b.Call("__slab_arena_alloc_chunks");
    _b.StoreLocal(2, VReg.Scratch0);

    // __slab_memzero(spans_ptr, ArenaPagesPerArena * 8)
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.MovRegImm(VReg.Arg1, ArenaPagesPerArena * 8);
    _b.Call("__slab_memzero");

    // l2_ptr[l2_index * 8] = spans_ptr
    _b.LoadLocal(VReg.Scratch2, 1);
    _b.LoadLocal(VReg.Scratch3, 2);
    _b.StoreIndirect(VReg.Scratch2, 0, VReg.Scratch3);

    _b.DefineLabel(spansExist);
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitMetaAlloc: __slab_meta_alloc() -> ptr
  //
  // Allocates a 64-byte metadata slot from the metadata slab.
  // Uses intrusive free list first, then bump allocator within a chunk,
  // then allocates a new chunk from the arena.
  // =========================================================================
  // Stack slots: (none needed as args). Frame 0x20.
  public void EmitMetaAlloc() {
    _b.FunctionStart("__slab_meta_alloc", 0, 0x20);

    // Check free list first
    _b.LoadGlobal(VReg.Scratch0, MetaFreeHeadLabel);
    var noFreeSlot = UniqueLabel("meta_alloc_no_free");
    _b.JumpIfZero(VReg.Scratch0, noFreeSlot);

    // Pop from free list: result = head; head = [head]
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // next
    _b.StoreGlobal(MetaFreeHeadLabel, VReg.Scratch1);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(noFreeSlot);

    // Check bump allocator: if bump_ptr + MspanMetaSlotSize <= bump_end
    _b.LoadGlobal(VReg.Scratch0, MetaBumpPtrLabel);
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.AddRegImm(VReg.Scratch1, MspanMetaSlotSize);
    _b.LoadGlobal(VReg.Scratch2, MetaBumpEndLabel);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    var needNewChunk = UniqueLabel("meta_alloc_new_chunk");
    _b.JumpIf(Condition.Above, needNewChunk);

    // Bump: result = bump_ptr; bump_ptr += MspanMetaSlotSize
    _b.StoreGlobal(MetaBumpPtrLabel, VReg.Scratch1);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(needNewChunk);

    // Allocate a new chunk from arena
    _b.MovRegImm(VReg.Arg0, 1);
    _b.Call("__slab_arena_alloc_chunks");
    // Scratch0 = new chunk base
    _b.StoreLocal(0, VReg.Scratch0); // save chunk base

    // bump_ptr = chunk + MspanMetaSlotSize (first slot is the return value)
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.AddRegImm(VReg.Scratch1, MspanMetaSlotSize);
    _b.StoreGlobal(MetaBumpPtrLabel, VReg.Scratch1);

    // bump_end = chunk + ChunkSize
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.AddRegImm(VReg.Scratch1, ChunkSize);
    _b.StoreGlobal(MetaBumpEndLabel, VReg.Scratch1);

    // Return chunk base (first 64-byte slot)
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitMetaFree: __slab_meta_free(ptr)
  //
  // Returns a 64-byte metadata slot to the free list.
  // =========================================================================
  public void EmitMetaFree() {
    _b.FunctionStart("__slab_meta_free", 1, 0x20);

    _b.LoadLocal(VReg.Scratch0, 0); // ptr
    _b.LoadGlobal(VReg.Scratch1, MetaFreeHeadLabel);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // [ptr] = old head
    _b.StoreGlobal(MetaFreeHeadLabel, VReg.Scratch0); // head = ptr

    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitArenaAllocChunks: __slab_arena_alloc_chunks(num_chunks) -> ptr
  //
  // Allocates num_chunks contiguous 8KB chunks from arenas via bitmap scan.
  // Walks the arena list looking for consecutive free bits in the bitmap.
  // If no arena has space, allocates a new 64MB arena from the OS.
  // Thread-safe: acquires MspanPoolLockLabel.
  // Sets __slab_arena_last_base to the arena base of the arena used.
  //
  // Bitmap scan uses qword-level operations: loads 64 bits at a time,
  // fast-paths all-used (==0) and all-free (==-1) qwords, and uses
  // BSF for partial qwords to find runs of consecutive free bits.
  // =========================================================================
  // Stack slots: 0=num_chunks, 1=result, 2=arena_ptr, 3=prev_ptr,
  //              4=saved_tag_ctx, 5=qword_idx, 6=run_start, 7=run_len,
  //              8=bit_offset (partial scan), 9=clear_qword_idx, 10=clear_end_qword
  // Frame 0x70.
  public void EmitArenaAllocChunks(bool mmTrace) {
    _b.FunctionStart("__slab_arena_alloc_chunks", 1, 0x70);

    _b.LockAcquire(MspanPoolLockLabel);

    var retryLabel = UniqueLabel("arena_alloc_retry");
    _b.DefineLabel(retryLabel);

    // prev_ptr = address of ArenaListHeadLabel
    _b.LeaGlobal(VReg.Scratch0, ArenaListHeadLabel);
    _b.StoreLocal(3, VReg.Scratch0);

    _b.LoadGlobal(VReg.Scratch0, ArenaListHeadLabel);
    _b.StoreLocal(2, VReg.Scratch0); // arena_ptr

    var arenaLoop = UniqueLabel("arena_alloc_loop");
    var arenaNext = UniqueLabel("arena_alloc_next");
    var newArena = UniqueLabel("arena_alloc_new");
    var foundChunks = UniqueLabel("arena_alloc_found");

    // --- Outer arena loop ---
    _b.DefineLabel(arenaLoop);
    _b.LoadLocal(VReg.Scratch0, 2); // arena_ptr
    _b.JumpIfZero(VReg.Scratch0, newArena);

    // Init scan: qword_idx=0, run_len=0
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(5, VReg.Scratch0); // qword_idx = 0
    _b.StoreLocal(7, VReg.Scratch0); // run_len = 0

    // --- Qword-level scan loop ---
    var qwordLoop = UniqueLabel("arena_qword_loop");
    var qwordNext = UniqueLabel("arena_qword_next");
    var allUsed = UniqueLabel("arena_all_used");
    var allFree = UniqueLabel("arena_all_free");
    var partialScan = UniqueLabel("arena_partial_scan");
    var partialLoop = UniqueLabel("arena_partial_loop");
    var partialGap = UniqueLabel("arena_partial_gap");
    var partialOnesAll = UniqueLabel("arena_partial_ones_all");
    var partialOnesCount = UniqueLabel("arena_partial_ones_count");
    var partialSkipStart = UniqueLabel("arena_partial_skip_start");
    var trailingZeros = UniqueLabel("arena_trailing_zeros");

    _b.DefineLabel(qwordLoop);
    _b.LoadLocal(VReg.Scratch0, 5); // qword_idx
    _b.CmpRegImm(VReg.Scratch0, ChunksPerArena / 64); // 128
    _b.JumpIf(Condition.AboveEqual, arenaNext);

    // Load qword from bitmap: arena + 0x10 + qword_idx * 8
    _b.LoadLocal(VReg.Scratch1, 2); // arena_ptr
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch0);
    _b.ShlRegImm(VReg.Scratch2, 3); // qword_idx * 8
    _b.AddRegImm(VReg.Scratch2, ArenaMetaOffBitmap);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2); // bitmap qword addr
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, 0); // Scratch0 = qword

    // Mask bit 0 for first qword (metadata chunk is always used)
    _b.LoadLocal(VReg.Scratch1, 5); // qword_idx
    var skipMask = UniqueLabel("arena_skip_mask");
    _b.JumpIfNonZero(VReg.Scratch1, skipMask);
    _b.MovRegImm(VReg.Scratch1, -2); // 0xFFFFFFFFFFFFFFFE
    _b.AndRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.DefineLabel(skipMask);

    // Fast path: all used (qword == 0)
    _b.JumpIfZero(VReg.Scratch0, allUsed);

    // Fast path: all free (qword == -1)
    _b.MovRegImm(VReg.Scratch1, -1);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.Equal, allFree);

    // --- Partial qword: enter inner scan ---
    _b.Jump(partialScan);

    // --- All used: reset run, advance qword ---
    _b.DefineLabel(allUsed);
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(7, VReg.Scratch0); // run_len = 0
    _b.Jump(qwordNext);

    // --- All free: add 64 to run, check if enough ---
    _b.DefineLabel(allFree);
    _b.LoadLocal(VReg.Scratch0, 7); // run_len
    var skipSetStartAllFree = UniqueLabel("arena_all_free_skip_start");
    _b.JumpIfNonZero(VReg.Scratch0, skipSetStartAllFree);
    // run_start = qword_idx * 64
    _b.LoadLocal(VReg.Scratch1, 5); // qword_idx
    _b.ShlRegImm(VReg.Scratch1, 6); // * 64
    _b.StoreLocal(6, VReg.Scratch1); // run_start
    _b.DefineLabel(skipSetStartAllFree);
    _b.LoadLocal(VReg.Scratch0, 7);
    _b.AddRegImm(VReg.Scratch0, 64);
    _b.StoreLocal(7, VReg.Scratch0); // run_len += 64
    // Check if run_len >= num_chunks
    _b.LoadLocal(VReg.Scratch1, 0); // num_chunks
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, foundChunks);
    _b.Jump(qwordNext);

    // --- Partial qword scan ---
    // Scratch0 = qword value (from the load above)
    // Use slot 8 for bit_offset within this qword
    _b.DefineLabel(partialScan);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreLocal(8, VReg.Scratch1); // bit_offset = 0

    _b.DefineLabel(partialLoop);
    // qword is in Scratch0 — reload if needed after stack operations
    _b.JumpIfZero(VReg.Scratch0, trailingZeros);

    // gap = BSF(qword): find first set bit
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.BitScanForward(VReg.Scratch1, VReg.Scratch1); // Scratch1 = gap

    // If gap > 0, there are 'gap' used bits before the next free bit — break run
    _b.JumpIfZero(VReg.Scratch1, partialGap);

    // gap > 0: reset run_len, advance bit_offset, shift out gap
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreLocal(7, VReg.Scratch2); // run_len = 0
    _b.LoadLocal(VReg.Scratch2, 8);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1); // bit_offset += gap
    _b.StoreLocal(8, VReg.Scratch2);
    _b.ShrRegReg(VReg.Scratch0, VReg.Scratch1); // qword >>= gap

    _b.DefineLabel(partialGap);
    // Now bit 0 of Scratch0 is set (free). Count consecutive 1s via NOT + BSF.
    _b.MovRegImm(VReg.Scratch2, -1);
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.XorRegReg(VReg.Scratch1, VReg.Scratch2); // Scratch1 = ~qword
    _b.JumpIfZero(VReg.Scratch1, partialOnesAll); // all remaining bits are 1

    // ones_run = BSF(~qword)
    _b.BitScanForward(VReg.Scratch1, VReg.Scratch1); // Scratch1 = ones_run
    _b.Jump(partialOnesCount);

    // All remaining bits in qword are 1
    _b.DefineLabel(partialOnesAll);
    _b.MovRegImm(VReg.Scratch1, 64);
    _b.LoadLocal(VReg.Scratch2, 8); // bit_offset
    _b.SubRegReg(VReg.Scratch1, VReg.Scratch2); // ones_run = 64 - bit_offset

    _b.DefineLabel(partialOnesCount);
    // Scratch1 = ones_run. Update run_start if starting a new run.
    _b.LoadLocal(VReg.Scratch2, 7); // run_len
    _b.JumpIfNonZero(VReg.Scratch2, partialSkipStart);

    // run_start = qword_idx * 64 + bit_offset
    _b.LoadLocal(VReg.Scratch3, 5); // qword_idx
    _b.ShlRegImm(VReg.Scratch3, 6); // * 64
    _b.LoadLocal(VReg.Scratch2, 8); // bit_offset
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.StoreLocal(6, VReg.Scratch3); // run_start

    _b.DefineLabel(partialSkipStart);
    // run_len += ones_run
    _b.LoadLocal(VReg.Scratch2, 7);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1); // run_len + ones_run
    _b.StoreLocal(7, VReg.Scratch2);

    // Check if run_len >= num_chunks
    _b.LoadLocal(VReg.Scratch3, 0); // num_chunks
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.JumpIf(Condition.AboveEqual, foundChunks);

    // bit_offset += ones_run
    _b.LoadLocal(VReg.Scratch2, 8);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1); // bit_offset + ones_run
    _b.StoreLocal(8, VReg.Scratch2);

    // qword >>= ones_run (shift out the run of 1s)
    _b.ShrRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.Jump(partialLoop);

    // --- Trailing zeros: remaining bits in qword are all 0 (used) ---
    _b.DefineLabel(trailingZeros);
    // If bit_offset < 64, trailing zeros break the run
    _b.LoadLocal(VReg.Scratch0, 8); // bit_offset
    _b.CmpRegImm(VReg.Scratch0, 64);
    _b.JumpIf(Condition.AboveEqual, qwordNext); // consumed all 64 bits, run survives
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(7, VReg.Scratch0); // run_len = 0

    // --- Advance to next qword ---
    _b.DefineLabel(qwordNext);
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(5, VReg.Scratch0); // qword_idx++
    _b.Jump(qwordLoop);

    // --- Next arena ---
    _b.DefineLabel(arenaNext);
    // prev_ptr = &arena->next
    _b.LoadLocal(VReg.Scratch0, 2); // arena_ptr
    _b.AddRegImm(VReg.Scratch0, ArenaMetaOffNext);
    _b.StoreLocal(3, VReg.Scratch0); // prev_ptr = &arena->next
    // arena = arena->next
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, ArenaMetaOffNext);
    _b.StoreLocal(2, VReg.Scratch0);
    _b.Jump(arenaLoop);

    // --- Found consecutive free chunks ---
    _b.DefineLabel(foundChunks);

    // Batch-clear bits: process one qword at a time with load-AND-store.
    // qword range: [run_start >> 6 .. (run_start + num_chunks - 1) >> 6]
    _b.LoadLocal(VReg.Scratch0, 6); // run_start
    _b.ShrRegImm(VReg.Scratch0, 6); // start_qword = run_start >> 6
    _b.StoreLocal(9, VReg.Scratch0); // clear_qword_idx

    _b.LoadLocal(VReg.Scratch0, 6); // run_start
    _b.LoadLocal(VReg.Scratch1, 0); // num_chunks
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.SubRegImm(VReg.Scratch0, 1); // run_start + num_chunks - 1
    _b.ShrRegImm(VReg.Scratch0, 6); // end_qword
    _b.StoreLocal(10, VReg.Scratch0);

    var clearQwordLoop = UniqueLabel("arena_clear_qword_loop");
    var clearQwordDone = UniqueLabel("arena_clear_qword_done");

    _b.DefineLabel(clearQwordLoop);
    _b.LoadLocal(VReg.Scratch0, 9); // clear_qword_idx
    _b.LoadLocal(VReg.Scratch1, 10); // end_qword
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.Above, clearQwordDone);

    // Compute mask of bits to clear in this qword.
    // Start with mask = -1 (all bits), then mask off bits outside the range.
    _b.MovRegImm(VReg.Scratch2, -1); // mask = all bits

    // If this is the first qword: mask &= (-1 << (run_start & 63))
    // i.e., clear low bits below run_start
    _b.LoadLocal(VReg.Scratch3, 6); // run_start
    _b.ShrRegImm(VReg.Scratch3, 6); // run_start >> 6 = first qword
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch3); // clear_qword_idx == first?
    var notFirstQword = UniqueLabel("arena_clear_not_first");
    _b.JumpIf(Condition.NotEqual, notFirstQword);
    // low_bit = run_start & 63
    _b.LoadLocal(VReg.Scratch3, 6);
    _b.MovRegImm(VReg.Scratch1, 63);
    _b.AndRegReg(VReg.Scratch3, VReg.Scratch1); // low_bit
    // start_mask = -1 << low_bit
    _b.MovRegImm(VReg.Scratch1, -1);
    _b.ShlRegReg(VReg.Scratch1, VReg.Scratch3);
    _b.AndRegReg(VReg.Scratch2, VReg.Scratch1); // mask &= start_mask
    _b.DefineLabel(notFirstQword);

    // If this is the last qword: mask &= (-1 >> (63 - ((run_start + num_chunks - 1) & 63)))
    _b.LoadLocal(VReg.Scratch0, 9); // clear_qword_idx
    _b.LoadLocal(VReg.Scratch1, 10); // end_qword
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    var notLastQword = UniqueLabel("arena_clear_not_last");
    _b.JumpIf(Condition.NotEqual, notLastQword);
    // high_bit = (run_start + num_chunks - 1) & 63
    _b.LoadLocal(VReg.Scratch3, 6); // run_start
    _b.LoadLocal(VReg.Scratch1, 0); // num_chunks
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch1);
    _b.SubRegImm(VReg.Scratch3, 1); // run_start + num_chunks - 1
    _b.MovRegImm(VReg.Scratch1, 63);
    _b.AndRegReg(VReg.Scratch3, VReg.Scratch1); // high_bit
    // shift_amount = 63 - high_bit
    _b.MovRegImm(VReg.Scratch1, 63);
    _b.SubRegReg(VReg.Scratch1, VReg.Scratch3); // 63 - high_bit
    // end_mask = -1 >> shift_amount (logical right shift)
    _b.MovRegImm(VReg.Scratch3, -1);
    _b.ShrRegReg(VReg.Scratch3, VReg.Scratch1);
    _b.AndRegReg(VReg.Scratch2, VReg.Scratch3); // mask &= end_mask
    _b.DefineLabel(notLastQword);

    // Scratch2 = final mask. Load qword, clear masked bits, store back.
    // addr = arena + 0x10 + clear_qword_idx * 8
    _b.LoadLocal(VReg.Scratch0, 9); // clear_qword_idx
    _b.ShlRegImm(VReg.Scratch0, 3); // * 8
    _b.AddRegImm(VReg.Scratch0, ArenaMetaOffBitmap);
    _b.LoadLocal(VReg.Scratch1, 2); // arena_ptr
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // addr

    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // old qword
    // inv_mask = ~mask
    _b.MovRegImm(VReg.Scratch3, -1);
    _b.XorRegReg(VReg.Scratch2, VReg.Scratch3); // inv_mask = mask XOR -1
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2); // old & ~mask
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);

    // clear_qword_idx++
    _b.LoadLocal(VReg.Scratch0, 9);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(9, VReg.Scratch0);
    _b.Jump(clearQwordLoop);

    _b.DefineLabel(clearQwordDone);

    // Store arena_base in ArenaLastBaseLabel
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreGlobal(ArenaLastBaseLabel, VReg.Scratch0);

    // Compute result = arena + run_start * ChunkSize
    _b.LoadLocal(VReg.Scratch0, 6); // run_start
    _b.ShlRegImm(VReg.Scratch0, ChunkShift);
    _b.LoadLocal(VReg.Scratch1, 2); // arena_ptr
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.StoreLocal(1, VReg.Scratch0); // result

    _b.LockRelease(MspanPoolLockLabel);
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.ReturnValue(VReg.Scratch0);

    // --- No arena has space: allocate new one ---
    _b.DefineLabel(newArena);

    // Save tag context for OS alloc (arena is infrastructure, not tied to an object)
    if (mmTrace) {
      _b.LoadGlobal(VReg.Scratch0, "__mm_trace_tag_ctx");
      _b.StoreLocal(4, VReg.Scratch0);
      _b.ZeroReg(VReg.Scratch0);
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch0);
    }

    _b.MovRegImm(VReg.Arg0, ArenaSize);
    _b.Call("__slab_os_alloc");
    // Scratch0 = new arena base
    _b.StoreLocal(2, VReg.Scratch0); // arena_ptr = new base

    if (mmTrace) {
      _b.LoadLocal(VReg.Scratch0, 4);
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch0);
    }

    // Init metadata at chunk 0
    _b.LoadLocal(VReg.Scratch0, 2); // new_base

    // [new_base + 0] = old arena_list_head (next)
    _b.LoadGlobal(VReg.Scratch1, ArenaListHeadLabel);
    _b.StoreIndirect(VReg.Scratch0, ArenaMetaOffNext, VReg.Scratch1);

    EmitBitmapInitAndClearBit0(baseSlot: 2);

    // Prepend to arena list
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreGlobal(ArenaListHeadLabel, VReg.Scratch0);

    // Ensure arena map for both base and end
    _b.LoadLocal(VReg.Arg0, 2); // new_base
    _b.Call("__slab_arena_map_ensure");
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.AddRegImm(VReg.Scratch0, ArenaSize - 1); // last byte of arena
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_map_ensure");

    // Retry — the new arena has plenty of free space
    _b.Jump(retryLabel);
  }

  // =========================================================================
  // EmitArenaFreeChunks: __slab_arena_free_chunks(arena_base, chunk_index, num_chunks)
  //
  // Sets bitmap bits chunk_index..chunk_index+num_chunks-1 back to 1 (free).
  // Uses qword-level load-OR-store instead of per-bit BTS.
  //
  // ZEROES THE CHUNK RUN BEFORE RELEASING IT. This is what upholds the
  // allocator's central invariant:
  //
  //     every chunk run handed out by __slab_arena_alloc_chunks is all-zero.
  //
  // Fresh arena address space satisfies that for free — VirtualAlloc(MEM_COMMIT)
  // and anonymous mmap both hand back zeroed pages (this is exactly Go's
  // "sysAlloc obtains a large chunk of ZEROED memory from the operating system").
  // But this runtime RECYCLES chunks: __slab_free's arena-large branch returns a
  // dead object's chunk run here, so without this memzero __slab_arena_alloc_chunks
  // could hand a span, a metadata chunk, or an arena-map table a run still holding
  // the previous occupant's bytes.
  //
  // That matters now in a way it did not before: __slab_alloc no longer zeroes
  // slots it carves from a span's never-used bump region — it TRUSTS them to be
  // zero. This function is where that trust is paid for. Deleting this memzero
  // silently hands out garbage.
  // =========================================================================
  // Stack slots: 0=arena_base, 1=chunk_index, 2=num_chunks, 3=qword_idx, 4=end_qword
  public void EmitArenaFreeChunks() {
    _b.FunctionStart("__slab_arena_free_chunks", 3, 0x40);

    // Zero the run BEFORE taking the lock and before the bits say "free": no
    // other thread can observe these chunks until the bitmap publishes them.
    // addr = arena_base + (chunk_index << ChunkShift); size = num_chunks << ChunkShift.
    _b.LoadLocal(VReg.Scratch0, 1); // chunk_index
    _b.ShlRegImm(VReg.Scratch0, ChunkShift);
    _b.LoadLocal(VReg.Scratch1, 0); // arena_base
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.LoadLocal(VReg.Scratch1, 2); // num_chunks
    _b.ShlRegImm(VReg.Scratch1, ChunkShift);
    _b.MovRegReg(VReg.Arg1, VReg.Scratch1);
    _b.Call("__slab_memzero");

    _b.LockAcquire(MspanPoolLockLabel);

    // qword range: [chunk_index >> 6 .. (chunk_index + num_chunks - 1) >> 6]
    _b.LoadLocal(VReg.Scratch0, 1); // chunk_index
    _b.ShrRegImm(VReg.Scratch0, 6); // start_qword
    _b.StoreLocal(3, VReg.Scratch0); // qword_idx

    _b.LoadLocal(VReg.Scratch0, 1); // chunk_index
    _b.LoadLocal(VReg.Scratch1, 2); // num_chunks
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.SubRegImm(VReg.Scratch0, 1); // chunk_index + num_chunks - 1
    _b.ShrRegImm(VReg.Scratch0, 6); // end_qword
    _b.StoreLocal(4, VReg.Scratch0);

    var setLoop = UniqueLabel("arena_free_qword_loop");
    var setDone = UniqueLabel("arena_free_qword_done");

    _b.DefineLabel(setLoop);
    _b.LoadLocal(VReg.Scratch0, 3); // qword_idx
    _b.LoadLocal(VReg.Scratch1, 4); // end_qword
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.Above, setDone);

    // Compute mask: start with -1, mask off bits outside the range
    _b.MovRegImm(VReg.Scratch2, -1); // mask = all bits

    // If this is the first qword: mask &= (-1 << (chunk_index & 63))
    _b.LoadLocal(VReg.Scratch3, 1); // chunk_index
    _b.ShrRegImm(VReg.Scratch3, 6); // first_qword
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch3);
    var notFirst = UniqueLabel("arena_free_not_first");
    _b.JumpIf(Condition.NotEqual, notFirst);
    _b.LoadLocal(VReg.Scratch3, 1); // chunk_index
    _b.MovRegImm(VReg.Scratch1, 63);
    _b.AndRegReg(VReg.Scratch3, VReg.Scratch1); // low_bit
    _b.MovRegImm(VReg.Scratch1, -1);
    _b.ShlRegReg(VReg.Scratch1, VReg.Scratch3);
    _b.AndRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.DefineLabel(notFirst);

    // If this is the last qword: mask &= (-1 >> (63 - ((chunk_index + num_chunks - 1) & 63)))
    _b.LoadLocal(VReg.Scratch0, 3); // qword_idx
    _b.LoadLocal(VReg.Scratch1, 4); // end_qword
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    var notLast = UniqueLabel("arena_free_not_last");
    _b.JumpIf(Condition.NotEqual, notLast);
    _b.LoadLocal(VReg.Scratch3, 1); // chunk_index
    _b.LoadLocal(VReg.Scratch1, 2); // num_chunks
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch1);
    _b.SubRegImm(VReg.Scratch3, 1); // chunk_index + num_chunks - 1
    _b.MovRegImm(VReg.Scratch1, 63);
    _b.AndRegReg(VReg.Scratch3, VReg.Scratch1); // high_bit
    _b.MovRegImm(VReg.Scratch1, 63);
    _b.SubRegReg(VReg.Scratch1, VReg.Scratch3); // 63 - high_bit
    _b.MovRegImm(VReg.Scratch3, -1);
    _b.ShrRegReg(VReg.Scratch3, VReg.Scratch1);
    _b.AndRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.DefineLabel(notLast);

    // Load qword, OR with mask, store back
    _b.LoadLocal(VReg.Scratch0, 3); // qword_idx
    _b.ShlRegImm(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, ArenaMetaOffBitmap);
    _b.LoadLocal(VReg.Scratch1, 0); // arena_base
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // addr

    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // old qword
    _b.OrRegReg(VReg.Scratch1, VReg.Scratch2); // old | mask
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);

    // qword_idx++
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);
    _b.Jump(setLoop);

    _b.DefineLabel(setDone);

    _b.LockRelease(MspanPoolLockLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitAllocatorInit: __slab_init()
  //
  // Called during scheduler init (after P structs are allocated).
  // 1. Allocate first arena from OS, init bitmap
  // 2. Allocate arena map L1 via chunks, register first arena
  // 3. Init metadata slab
  // 4. Allocate mcache array via chunks
  // 5. Init OS-direct tracking array
  // =========================================================================
  // Stack slots: 0=arena_base, 1=ptr, 2=l1_ptr, 3=mcache_size, 4=mcache_chunks
  // Frame 0x50.
  public void EmitAllocatorInit(bool mmTrace) {
    _b.FunctionStart("__slab_init", 0, 0x50);

    // Check if already initialized
    _b.LoadGlobal(VReg.Scratch0, SlabInitDoneLabel);
    var alreadyDone = UniqueLabel("slab_init_done");
    _b.JumpIfNonZero(VReg.Scratch0, alreadyDone);

    // Mark as initialized
    _b.MovRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal(SlabInitDoneLabel, VReg.Scratch0);

    // Trace: sl_init\n + depth++
    if (mmTrace) {
      _b.LeaSymdata(VReg.Arg0, "__slab_tag_init");
      _b.Call("mm_trace_print_tag");
      _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
      _b.Call("mm_trace_print_tag");
      EmitTraceDepthInc();
    }

    // Step 1: Allocate first arena from OS
    _b.MovRegImm(VReg.Arg0, ArenaSize);
    _b.Call("__slab_os_alloc");
    _b.StoreLocal(0, VReg.Scratch0); // arena_base

    // Init metadata at chunk 0: next = NULL
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, ArenaMetaOffNext, VReg.Scratch1);

    EmitBitmapInitAndClearBit0(baseSlot: 0);

    // Set arena list head
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.StoreGlobal(ArenaListHeadLabel, VReg.Scratch0);

    // Step 2: Init arena map — ensure map entries for first arena
    _b.LoadLocal(VReg.Arg0, 0); // arena_base
    _b.Call("__slab_arena_map_ensure");
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.AddRegImm(VReg.Scratch0, ArenaSize - 1);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_map_ensure");

    // Step 3: Allocate L1 array: 1 chunk (256*8=2048 bytes fits in 8KB)
    _b.MovRegImm(VReg.Arg0, 1);
    _b.Call("__slab_arena_alloc_chunks");
    _b.StoreLocal(2, VReg.Scratch0); // l1_ptr

    // Memzero L1 array
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.MovRegImm(VReg.Arg1, ArenaMapL1Size * 8);
    _b.Call("__slab_memzero");

    // Store L1 base pointer
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreGlobal(ArenaMapL1Label, VReg.Scratch0);

    // Re-ensure map now that L1 exists
    _b.LoadLocal(VReg.Arg0, 0); // arena_base
    _b.Call("__slab_arena_map_ensure");
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.AddRegImm(VReg.Scratch0, ArenaSize - 1);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_map_ensure");

    // Step 4: Init metadata slab — allocate one chunk
    _b.MovRegImm(VReg.Arg0, 1);
    _b.Call("__slab_arena_alloc_chunks");
    _b.StoreGlobal(MetaBumpPtrLabel, VReg.Scratch0);
    _b.AddRegImm(VReg.Scratch0, ChunkSize);
    _b.StoreGlobal(MetaBumpEndLabel, VReg.Scratch0);
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreGlobal(MetaFreeHeadLabel, VReg.Scratch0);

    // Step 5: Allocate mcache: max_procs * 18 * 8
    _b.LoadGlobal(VReg.Scratch0, "__sched_max_procs");
    _b.MovRegImm(VReg.Scratch1, SlabNumClasses * 8); // 144
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.StoreLocal(3, VReg.Scratch0); // mcache_size

    // mcache_chunks = ceil(mcache_size / ChunkSize) = (mcache_size + ChunkSize - 1) >> ChunkShift
    _b.AddRegImm(VReg.Scratch0, ChunkSize - 1);
    _b.ShrRegImm(VReg.Scratch0, ChunkShift);
    _b.StoreLocal(4, VReg.Scratch0);

    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_alloc_chunks");
    _b.StoreLocal(1, VReg.Scratch0); // mcache_ptr

    // Memzero mcache
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.LoadLocal(VReg.Arg1, 3); // mcache_size
    _b.Call("__slab_memzero");

    _b.LoadLocal(VReg.Scratch0, 1);
    _b.StoreGlobal(McacheBaseLabel, VReg.Scratch0);

    // Step 6: Allocate the per-P memory-traffic byte counters: max_procs * 64 bytes,
    // one cache line each. Sized here because this is the only place that already knows
    // __sched_max_procs; its shape (the two counters inside a line) lives with the rest
    // of the MM counters in RuntimeEmitter.MemoryManager.cs, which explains why they are
    // per-P and unlocked rather than one shared atomic word.
    //
    // Until this runs, __mm_bytes_by_p is NULL and the counters route to their atomic
    // fallback words — which is exactly what makes an allocation before slab init safe.
    _b.LoadGlobal(VReg.Scratch0, "__sched_max_procs");
    _b.MovRegImm(VReg.Scratch1, MmBytesPerPStride);
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.StoreLocal(3, VReg.Scratch0); // byte_table_size

    _b.AddRegImm(VReg.Scratch0, ChunkSize - 1);
    _b.ShrRegImm(VReg.Scratch0, ChunkShift);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_alloc_chunks");
    _b.StoreLocal(1, VReg.Scratch0); // byte_table_ptr

    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.LoadLocal(VReg.Arg1, 3);
    _b.Call("__slab_memzero");

    _b.LoadLocal(VReg.Scratch0, 1);
    _b.StoreGlobal(MmBytesByPLabel, VReg.Scratch0);

    if (mmTrace) {
      EmitTraceDepthDec();
    }

    _b.DefineLabel(alreadyDone);
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitSpanRegister / EmitSpanUnregister
  //
  // Register writes span_ptr into each arena map page entry so span lookup works.
  // Unregister writes NULL to reclaim the entries when an arena-large span is freed.
  // =========================================================================
  public void EmitSpanRegister() => EmitSpanMapUpdate("__slab_span_register", writeSpanPtr: true);
  public void EmitSpanUnregister() => EmitSpanMapUpdate("__slab_span_unregister", writeSpanPtr: false);

  // Stack slots: 0=span_ptr, 1=base_addr, 2=num_pages, 3=loop_i. Frame 0x40.
  private void EmitSpanMapUpdate(string functionName, bool writeSpanPtr) {
    _b.FunctionStart(functionName, 1, 0x40);

    // Load span fields
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffBaseAddr);
    _b.StoreLocal(1, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, MspanOffSlotSize);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch0, MspanOffTotalSlots);

    // total_bytes = slot_size * total_slots
    _b.MulRegReg(VReg.Scratch2, VReg.Scratch3);

    // num_pages = (total_bytes + page_size - 1) >> page_shift
    _b.AddRegImm(VReg.Scratch2, (1 << ArenaPageShift) - 1);
    _b.ShrRegImm(VReg.Scratch2, ArenaPageShift);
    _b.StoreLocal(2, VReg.Scratch2);

    // i = 0
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(3, VReg.Scratch0);

    var loopStart = UniqueLabel("span_map_update_loop");
    var loopDone = UniqueLabel("span_map_update_done");

    _b.DefineLabel(loopStart);
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, loopDone);

    // page_addr = base_addr + i * page_size
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.ShlRegImm(VReg.Scratch0, ArenaPageShift);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // page_addr

    // l1_index = page_addr >> ArenaMapL1Shift
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.ShrRegImm(VReg.Scratch1, ArenaMapL1Shift);

    // l1_base = global[ArenaMapL1Label]
    _b.LoadGlobal(VReg.Scratch2, ArenaMapL1Label);

    // l2_ptr = l1_base[l1_index * 8]
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch1);
    _b.ShlRegImm(VReg.Scratch3, 3);
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch3, 0);

    // l2_index = (page_addr >> ArenaMapL2Shift) & ArenaMapL2Mask
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.ShrRegImm(VReg.Scratch1, ArenaMapL2Shift);
    _b.MovRegImm(VReg.Scratch3, ArenaMapL2Mask);
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch3);

    // spans_ptr = l2_ptr[l2_index * 8]
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch1);
    _b.ShlRegImm(VReg.Scratch3, 3);
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch3, 0);

    // page_index = (page_addr >> ArenaPageShift) & ArenaMapPageMask
    _b.ShrRegImm(VReg.Scratch0, ArenaPageShift);
    _b.MovRegImm(VReg.Scratch1, ArenaMapPageMask);
    _b.AndRegReg(VReg.Scratch0, VReg.Scratch1);

    // spans_ptr[page_index * 8] = span_ptr or NULL
    _b.ShlRegImm(VReg.Scratch0, 3);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch2);
    if (writeSpanPtr) {
      _b.LoadLocal(VReg.Scratch1, 0);
    } else {
      _b.ZeroReg(VReg.Scratch1);
    }
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);

    // i++
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);
    _b.Jump(loopStart);

    _b.DefineLabel(loopDone);
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitSpanLookup: __slab_span_lookup(ptr) -> span_ptr_or_null
  //
  // Looks up the span for any pointer via the two-level arena map.
  // Returns NULL if the pointer is not in any registered span.
  // =========================================================================
  // Stack slots: 0=ptr. Frame 0x20.
  public void EmitSpanLookup() {
    _b.FunctionStart("__slab_span_lookup", 1, 0x20);

    var returnNull = UniqueLabel("span_lookup_null");

    // l1_base = global[ArenaMapL1Label]
    _b.LoadGlobal(VReg.Scratch0, ArenaMapL1Label);
    _b.JumpIfZero(VReg.Scratch0, returnNull);

    // l1_index = ptr >> ArenaMapL1Shift
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.ShrRegImm(VReg.Scratch1, ArenaMapL1Shift);

    // bounds check
    _b.CmpRegImm(VReg.Scratch1, ArenaMapL1Size);
    _b.JumpIf(Condition.AboveEqual, returnNull);

    // l2_ptr = l1_base[l1_index * 8]
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch0);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch2, 0);
    _b.JumpIfZero(VReg.Scratch2, returnNull);

    // l2_index = (ptr >> ArenaMapL2Shift) & ArenaMapL2Mask
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ShrRegImm(VReg.Scratch0, ArenaMapL2Shift);
    _b.MovRegImm(VReg.Scratch1, ArenaMapL2Mask);
    _b.AndRegReg(VReg.Scratch0, VReg.Scratch1);

    // spans_ptr = l2_ptr[l2_index * 8]
    _b.ShlRegImm(VReg.Scratch0, 3);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 0);
    _b.JumpIfZero(VReg.Scratch0, returnNull);

    // page_index = (ptr >> ArenaPageShift) & ArenaMapPageMask
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.ShrRegImm(VReg.Scratch1, ArenaPageShift);
    _b.MovRegImm(VReg.Scratch2, ArenaMapPageMask);
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2);

    // span = spans_ptr[page_index * 8]
    _b.ShlRegImm(VReg.Scratch1, 3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, 0);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(returnNull);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitOsDirectInsert: __slab_os_direct_insert(ptr, size)
  //
  // Inserts a new entry into the OS-direct sorted array.
  // Each entry is 16 bytes: [ptr(8), size(8)], sorted by ptr ascending.
  // Binary searches for insertion point, shifts entries right, inserts.
  // Lazy-allocates the array on first use. Grows if full.
  // =========================================================================
  // Stack slots: 0=ptr, 1=size, 2=count, 3=capacity, 4=insert_idx, 5=new_array, 6=i
  public void EmitOsDirectInsert() {
    _b.FunctionStart("__slab_os_direct_insert", 2, 0x50);

    // Lazy init: if capacity == 0, allocate first page
    _b.LoadGlobal(VReg.Scratch0, OsDirectCapacityLabel);
    var alreadyInit = UniqueLabel("os_direct_insert_init_done");
    _b.JumpIfNonZero(VReg.Scratch0, alreadyInit);

    _b.MovRegImm(VReg.Arg0, 4096);
    _b.Call("__slab_os_alloc");
    _b.StoreGlobal(OsDirectArrayLabel, VReg.Scratch0);
    _b.MovRegImm(VReg.Scratch0, 4096 / 16); // 256 entries
    _b.StoreGlobal(OsDirectCapacityLabel, VReg.Scratch0);

    _b.DefineLabel(alreadyInit);

    _b.LoadGlobal(VReg.Scratch0, OsDirectCountLabel);
    _b.StoreLocal(2, VReg.Scratch0); // count
    _b.LoadGlobal(VReg.Scratch0, OsDirectCapacityLabel);
    _b.StoreLocal(3, VReg.Scratch0); // capacity

    // Check if we need to grow
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.LoadLocal(VReg.Scratch1, 3);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    var noGrow = UniqueLabel("os_direct_insert_no_grow");
    _b.JumpIf(Condition.Below, noGrow);

    // Grow: new_cap = capacity * 2, new_size = new_cap * 16
    _b.LoadLocal(VReg.Scratch0, 3); // capacity
    _b.ShlRegImm(VReg.Scratch0, 1); // new_cap = capacity * 2
    _b.StoreLocal(3, VReg.Scratch0); // update capacity local

    _b.ShlRegImm(VReg.Scratch0, 4); // new_cap * 16
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_os_alloc");
    _b.StoreLocal(5, VReg.Scratch0); // new_array

    // Copy old entries
    _b.LoadGlobal(VReg.Scratch0, OsDirectArrayLabel);
    _b.StoreLocal(6, VReg.Scratch0); // old_array (reuse slot 6 temporarily)

    _b.ZeroReg(VReg.Scratch0);
    var copyLoop = UniqueLabel("os_direct_insert_copy_loop");
    var copyDone = UniqueLabel("os_direct_insert_copy_done");
    // i in Scratch0
    _b.DefineLabel(copyLoop);
    _b.LoadLocal(VReg.Scratch1, 2); // count
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, copyDone);

    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.ShlRegImm(VReg.Scratch1, 4); // i * 16
    _b.LoadLocal(VReg.Scratch2, 6); // old_array
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch2, 0); // ptr
    _b.LoadLocal(VReg.Scratch2, 5); // new_array
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch2, 0, VReg.Scratch3);
    // Reload old for size
    _b.LoadLocal(VReg.Scratch3, 6);
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch3, 8); // size
    _b.StoreIndirect(VReg.Scratch2, 8, VReg.Scratch3);

    _b.AddRegImm(VReg.Scratch0, 1);
    _b.Jump(copyLoop);
    _b.DefineLabel(copyDone);

    // Free old array
    _b.LoadLocal(VReg.Arg0, 6); // old_array
    _b.LoadGlobal(VReg.Scratch0, OsDirectCapacityLabel);
    _b.ShlRegImm(VReg.Scratch0, 4);
    _b.MovRegReg(VReg.Arg1, VReg.Scratch0);
    _b.OsFreePages(VReg.Arg0, VReg.Arg1);

    // Update globals
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.StoreGlobal(OsDirectArrayLabel, VReg.Scratch0);
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.StoreGlobal(OsDirectCapacityLabel, VReg.Scratch0);

    _b.DefineLabel(noGrow);

    // Binary search for insertion point: find first i where array[i].ptr > ptr
    // lo=0, hi=count
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(4, VReg.Scratch0); // lo = 0
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreLocal(6, VReg.Scratch0); // hi = count

    var bsLoop = UniqueLabel("os_direct_insert_bs_loop");
    var bsDone = UniqueLabel("os_direct_insert_bs_done");
    _b.DefineLabel(bsLoop);
    _b.LoadLocal(VReg.Scratch0, 4); // lo
    _b.LoadLocal(VReg.Scratch1, 6); // hi
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, bsDone);

    // mid = (lo + hi) >> 1
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // lo + hi
    _b.ShrRegImm(VReg.Scratch0, 1); // mid

    // Load array[mid].ptr
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0); // save mid
    _b.ShlRegImm(VReg.Scratch0, 4); // mid * 16
    _b.LoadGlobal(VReg.Scratch2, OsDirectArrayLabel);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 0); // array[mid].ptr

    // Compare with target ptr
    _b.LoadLocal(VReg.Scratch2, 0); // target ptr
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch2);
    var goHigh = UniqueLabel("os_direct_insert_bs_high");
    _b.JumpIf(Condition.Below, goHigh);

    // array[mid].ptr >= ptr: hi = mid
    _b.StoreLocal(6, VReg.Scratch1); // hi = mid
    _b.Jump(bsLoop);

    _b.DefineLabel(goHigh);
    // array[mid].ptr < ptr: lo = mid + 1
    _b.AddRegImm(VReg.Scratch1, 1);
    _b.StoreLocal(4, VReg.Scratch1); // lo = mid + 1
    _b.Jump(bsLoop);

    _b.DefineLabel(bsDone);
    // insert_idx = lo (slot 4)

    // Shift entries [insert_idx..count-1] right by one, starting from the end
    // i = count
    _b.LoadLocal(VReg.Scratch0, 2); // count
    _b.StoreLocal(6, VReg.Scratch0); // i = count

    var shiftLoop = UniqueLabel("os_direct_insert_shift_loop");
    var shiftDone = UniqueLabel("os_direct_insert_shift_done");
    _b.DefineLabel(shiftLoop);
    _b.LoadLocal(VReg.Scratch0, 6); // i
    _b.LoadLocal(VReg.Scratch1, 4); // insert_idx
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.BelowEqual, shiftDone);

    // array[i] = array[i-1]
    _b.LoadLocal(VReg.Scratch0, 6);
    _b.SubRegImm(VReg.Scratch0, 1); // i-1
    _b.ShlRegImm(VReg.Scratch0, 4); // (i-1) * 16
    _b.LoadGlobal(VReg.Scratch1, OsDirectArrayLabel);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // src = &array[i-1]
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 0); // src.ptr
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch0, 8); // src.size
    _b.AddRegImm(VReg.Scratch0, 16); // dst = &array[i]
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch3);

    // i--
    _b.LoadLocal(VReg.Scratch0, 6);
    _b.SubRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(6, VReg.Scratch0);
    _b.Jump(shiftLoop);

    _b.DefineLabel(shiftDone);

    // Insert at insert_idx
    _b.LoadLocal(VReg.Scratch0, 4); // insert_idx
    _b.ShlRegImm(VReg.Scratch0, 4); // insert_idx * 16
    _b.LoadGlobal(VReg.Scratch1, OsDirectArrayLabel);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // entry_addr

    _b.LoadLocal(VReg.Scratch1, 0); // ptr
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 1); // size
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1);

    // count++
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal(OsDirectCountLabel, VReg.Scratch0);

    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitOsDirectRemove: __slab_os_direct_remove(ptr) -> size
  //
  // Finds and removes an entry from the sorted OS-direct array via binary
  // search. Shifts entries left to maintain sort order.
  // Returns 0 if not found.
  // =========================================================================
  // Stack slots: 0=ptr, 1=array, 2=count, 3=lo, 4=hi, 5=found_size, 6=i
  public void EmitOsDirectRemove() {
    _b.FunctionStart("__slab_os_direct_remove", 1, 0x50);

    _b.LoadGlobal(VReg.Scratch0, OsDirectArrayLabel);
    _b.StoreLocal(1, VReg.Scratch0); // array
    _b.LoadGlobal(VReg.Scratch0, OsDirectCountLabel);
    _b.StoreLocal(2, VReg.Scratch0); // count

    var notFound = UniqueLabel("os_direct_remove_not_found");

    // If count == 0, not found
    _b.JumpIfZero(VReg.Scratch0, notFound);

    // Binary search: lo=0, hi=count
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(3, VReg.Scratch0); // lo = 0
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreLocal(4, VReg.Scratch0); // hi = count

    var bsLoop = UniqueLabel("os_direct_remove_bs_loop");
    var bsDone = UniqueLabel("os_direct_remove_bs_done");
    _b.DefineLabel(bsLoop);
    _b.LoadLocal(VReg.Scratch0, 3); // lo
    _b.LoadLocal(VReg.Scratch1, 4); // hi
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, bsDone);

    // mid = (lo + hi) >> 1
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.ShrRegImm(VReg.Scratch0, 1); // mid

    // Load array[mid].ptr
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0); // save mid
    _b.ShlRegImm(VReg.Scratch0, 4); // mid * 16
    _b.LoadLocal(VReg.Scratch2, 1); // array
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 0); // array[mid].ptr

    _b.LoadLocal(VReg.Scratch2, 0); // target ptr
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch2);
    var goHigh = UniqueLabel("os_direct_remove_bs_high");
    var goLow = UniqueLabel("os_direct_remove_bs_low");
    _b.JumpIf(Condition.Below, goHigh);
    _b.JumpIf(Condition.Above, goLow);

    // Found: array[mid].ptr == target. mid is in Scratch1.
    // Save size from array[mid]
    _b.MovRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.ShlRegImm(VReg.Scratch0, 4);
    _b.LoadLocal(VReg.Scratch2, 1);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch0, 8); // size
    _b.StoreLocal(5, VReg.Scratch3); // save size

    // Shift entries [mid+1..count-1] left by one
    // i = mid
    _b.StoreLocal(6, VReg.Scratch1); // i = mid

    var shiftLoop = UniqueLabel("os_direct_remove_shift_loop");
    var shiftDone = UniqueLabel("os_direct_remove_shift_done");
    _b.DefineLabel(shiftLoop);
    _b.LoadLocal(VReg.Scratch0, 6); // i
    _b.LoadLocal(VReg.Scratch1, 2); // count
    _b.SubRegImm(VReg.Scratch1, 1); // count - 1
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, shiftDone);

    // array[i] = array[i+1]
    _b.LoadLocal(VReg.Scratch0, 6);
    _b.AddRegImm(VReg.Scratch0, 1); // i+1
    _b.ShlRegImm(VReg.Scratch0, 4); // (i+1) * 16
    _b.LoadLocal(VReg.Scratch1, 1); // array
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // src = &array[i+1]
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 0); // src.ptr
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch0, 8); // src.size
    _b.SubRegImm(VReg.Scratch0, 16); // dst = &array[i]
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch3);

    _b.LoadLocal(VReg.Scratch0, 6);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(6, VReg.Scratch0); // i++
    _b.Jump(shiftLoop);

    _b.DefineLabel(shiftDone);

    // count--
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.SubRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal(OsDirectCountLabel, VReg.Scratch0);

    // Return size
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(goHigh);
    // array[mid].ptr < target: lo = mid + 1
    _b.AddRegImm(VReg.Scratch1, 1);
    _b.StoreLocal(3, VReg.Scratch1);
    _b.Jump(bsLoop);

    _b.DefineLabel(goLow);
    // array[mid].ptr > target: hi = mid
    _b.StoreLocal(4, VReg.Scratch1);
    _b.Jump(bsLoop);

    _b.DefineLabel(bsDone);
    // Binary search exhausted without finding exact match

    _b.DefineLabel(notFound);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitRawAllocIdInsert: __mm_raw_id_insert(ptr, raw_alloc_id)
  //
  // Inserts a new entry into the raw alloc ID tracking linked list.
  // Each entry is allocated from the metadata slab (64 bytes):
  //   [+0]: ptr, [+8]: raw_alloc_id, [+16]: next
  // =========================================================================
  public void EmitRawAllocIdInsert() {
    _b.FunctionStart("__mm_raw_id_insert", 2, 0x20);

    _b.Call("__slab_meta_alloc");
    // Scratch0 = new entry

    _b.LoadLocal(VReg.Scratch1, 0);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // entry[0] = ptr

    _b.LoadLocal(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1); // entry[8] = raw_alloc_id

    _b.LoadGlobal(VReg.Scratch1, RawAllocIdListLabel);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1); // entry[16] = old head

    _b.StoreGlobal(RawAllocIdListLabel, VReg.Scratch0); // head = entry

    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitRawAllocIdLookup: __mm_raw_id_lookup(ptr) -> raw_alloc_id
  //
  // Finds and removes an entry from the raw alloc ID tracking linked list.
  // Returns 0 if not found.
  // =========================================================================
  public void EmitRawAllocIdLookup() {
    _b.FunctionStart("__mm_raw_id_lookup", 1, 0x30);

    _b.LeaGlobal(VReg.Scratch0, RawAllocIdListLabel);
    _b.StoreLocal(1, VReg.Scratch0);

    _b.LoadGlobal(VReg.Scratch0, RawAllocIdListLabel);
    _b.StoreLocal(2, VReg.Scratch0);

    var loopStart = UniqueLabel("raw_id_lookup_loop");
    var notFound = UniqueLabel("raw_id_lookup_not_found");
    var found = UniqueLabel("raw_id_lookup_found");

    _b.DefineLabel(loopStart);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.JumpIfZero(VReg.Scratch0, notFound);

    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // entry->ptr
    _b.LoadLocal(VReg.Scratch2, 0); // target ptr
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Equal, found);

    _b.LoadLocal(VReg.Scratch0, 2);
    _b.AddRegImm(VReg.Scratch0, 16);
    _b.StoreLocal(1, VReg.Scratch0); // prev_next_addr = &entry->next

    _b.LoadLocal(VReg.Scratch0, 2);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 16);
    _b.StoreLocal(2, VReg.Scratch0); // entry = entry->next
    _b.Jump(loopStart);

    _b.DefineLabel(found);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 8); // raw_alloc_id
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 16); // next
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2); // unlink
    _b.ReturnValue(VReg.Scratch1);

    _b.DefineLabel(notFound);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitMspanAlloc: __slab_mspan_alloc(class_index) -> mspan*
  //
  // Allocates a new mspan for the given size class:
  // 1. Allocate an mspan header from metadata slab
  // 2. Allocate span data (slot_size * num_objs) as chunks from arena
  // 3. Leave the slot region PRISTINE (empty free list + bump cursor) — see below
  // 4. Register span in arena map
  // 5. Return the mspan pointer
  // =========================================================================
  // Stack slots: 0=class_index, 1=mspan_ptr, 2=page_base, 3=slot_size,
  //              4=num_objs, 6=arena_base
  public void EmitMspanAlloc() {
    _b.FunctionStart("__slab_mspan_alloc", 1, 0x50);

    // --- Allocate mspan header from metadata slab ---
    _b.Call("__slab_meta_alloc");
    _b.StoreLocal(1, VReg.Scratch0); // slot 1 = mspan_ptr

    // --- Look up class parameters from tables ---
    // slot_size = class_sizes[class_index]
    _b.LeaSymdata(VReg.Scratch0, SlabClassSizesLabel);
    _b.LoadLocal(VReg.Scratch1, 0); // class_index
    _b.ShlRegImm(VReg.Scratch1, 3); // class_index * 8
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 0); // slot_size
    _b.StoreLocal(3, VReg.Scratch0); // slot 3 = slot_size

    // num_objs = objs_per_span[class_index]
    _b.LeaSymdata(VReg.Scratch0, SlabObjsLabel);
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.ShlRegImm(VReg.Scratch1, 3);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 0);
    _b.StoreLocal(4, VReg.Scratch0); // slot 4 = num_objs

    // --- Allocate span data: num_chunks = ceil(slot_size * num_objs / ChunkSize) ---
    _b.LoadLocal(VReg.Scratch0, 3); // slot_size
    _b.LoadLocal(VReg.Scratch1, 4); // num_objs
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1); // data_size
    _b.AddRegImm(VReg.Scratch0, ChunkSize - 1);
    _b.ShrRegImm(VReg.Scratch0, ChunkShift); // num_chunks
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_alloc_chunks");
    _b.StoreLocal(2, VReg.Scratch0); // slot 2 = page_base

    // Load arena_base from global (set by alloc_chunks)
    _b.LoadGlobal(VReg.Scratch0, ArenaLastBaseLabel);
    _b.StoreLocal(6, VReg.Scratch0); // slot 6 = arena_base

    // --- Initialize mspan fields ---
    _b.LoadLocal(VReg.Scratch0, 1); // mspan_ptr
    _b.LoadLocal(VReg.Scratch1, 2); // page_base
    _b.StoreIndirect(VReg.Scratch0, MspanOffBaseAddr, VReg.Scratch1);

    _b.LoadLocal(VReg.Scratch1, 3); // slot_size
    _b.StoreIndirect(VReg.Scratch0, MspanOffSlotSize, VReg.Scratch1);

    _b.LoadLocal(VReg.Scratch1, 4); // num_objs = free_count initially
    _b.StoreIndirect(VReg.Scratch0, MspanOffFreeCount, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffTotalSlots, VReg.Scratch1);

    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffNextSpan, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffFreeList, VReg.Scratch1);

    _b.LoadLocal(VReg.Scratch1, 0); // class_index
    _b.StoreIndirect(VReg.Scratch0, MspanOffClassIndex, VReg.Scratch1);

    _b.LoadLocal(VReg.Scratch1, 6); // arena_base
    _b.StoreIndirect(VReg.Scratch0, MspanOffArenaBase, VReg.Scratch1);

    // Initial owning_p = current P's id. Defensive: __slab_mcentral_get_span
    // (the sole caller) re-stamps owning_p before returning, so any escape via
    // partial-list / mcache will see the caller's id. Stamping here as well
    // guarantees the field is never left at a poisoned post-malloc value if a
    // future code path exposes a freshly-allocated span without going through
    // mcentral_get_span.
    _b.LoadCurrentP(VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch1, POffId);
    // Store-release pairs with the load-acquire of owning_p in __slab_free so a
    // cross-P free on ARM64 (weak memory) observes a coherent owner, not a stale one.
    _b.StoreRelease(VReg.Scratch0, MspanOffOwningP, VReg.Scratch1);

    // --- Free list starts EMPTY; the whole span is one virgin bump region ---
    //
    // This span's pages came straight from __slab_arena_alloc_chunks, so every
    // byte in them is zero (see EmitArenaFreeChunks for why that holds even
    // though chunks are recycled). Leave them that way.
    //
    // We deliberately do NOT thread an intrusive free list through the slots
    // here. Writing a next-pointer into slot[0] of all 1024 slots would dirty
    // every one of them, and a dirty slot must be memzeroed before it can be
    // handed to a caller. Building the list is therefore not merely wasted work
    // — it is what would FORCE a memzero on every first-time allocation. By
    // leaving the region pristine, __slab_alloc can carve from it with no
    // zeroing at all (Go's mspan.freeindex; see MspanOffBumpNext).
    //
    // free_list is already 0 from the field init above. bump_next = page_base;
    // bump_end is derived as base_addr + slot_size * total_slots.
    _b.LoadLocal(VReg.Scratch0, 1); // mspan_ptr
    _b.LoadLocal(VReg.Scratch1, 2); // page_base
    _b.StoreIndirect(VReg.Scratch0, MspanOffBumpNext, VReg.Scratch1);

    // Register span in arena map
    _b.LoadLocal(VReg.Arg0, 1); // mspan_ptr
    _b.Call("__slab_span_register");

    // Return mspan_ptr
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitMcentralGetSpan: __slab_mcentral_get_span(class_index) -> mspan*
  //
  // Gets an mspan with free slots for the given class:
  // 1. Lock the class's mcentral
  // 2. If partial_head != NULL, take it
  // 3. Else allocate a new span via __slab_mspan_alloc
  // 4. Unlock and return
  // =========================================================================
  // Stack slots: 0=class_index, 1=mcentral_addr, 2=span
  public void EmitMcentralGetSpan() {
    _b.FunctionStart("__slab_mcentral_get_span", 1, 0x40);

    // Compute mcentral entry address: mcentral_array + class_index * 16
    _b.LeaGlobal(VReg.Scratch0, McentralArrayLabel);
    _b.LoadLocal(VReg.Scratch1, 0); // class_index
    _b.ShlRegImm(VReg.Scratch1, 4); // * 16
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.StoreLocal(1, VReg.Scratch0); // slot 1 = mcentral_addr

    _b.LockAcquire(MspanPoolLockLabel);

    // Check partial_head
    _b.LoadLocal(VReg.Scratch0, 1); // mcentral_addr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // partial_head
    var hasPartial = UniqueLabel("mcentral_has_partial");
    _b.JumpIfNonZero(VReg.Scratch1, hasPartial);

    // No partial spans: allocate new one
    _b.LoadLocal(VReg.Arg0, 0); // class_index
    _b.Call("__slab_mspan_alloc");
    _b.StoreLocal(2, VReg.Scratch0);
    var gotSpan = UniqueLabel("mcentral_got_span");
    _b.Jump(gotSpan);

    _b.DefineLabel(hasPartial);
    // Take partial_head
    _b.StoreLocal(2, VReg.Scratch1); // slot 2 = span = partial_head
    // Update partial_head = span->next_span
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffNextSpan);
    _b.LoadLocal(VReg.Scratch0, 1); // mcentral_addr
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2); // partial_head = span->next_span
    // Clear span->next_span
    _b.LoadLocal(VReg.Scratch0, 2); // span
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffNextSpan, VReg.Scratch1);

    _b.DefineLabel(gotSpan);
    // Stamp owning_p = current P's id. This is the single point where mcentral
    // hands a span off to a P, so it covers both the fresh-alloc path and the
    // partial-list re-cache path (which may be a different P from the one that
    // originally returned the span to mcentral). The store happens under the
    // mcentral lock to serialise against __slab_mcentral_return_span's sentinel
    // write — no remote-free can observe a stale owning_p once the span is
    // visible outside mcentral.
    _b.LoadCurrentP(VReg.Scratch3);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch3, POffId);
    _b.LoadLocal(VReg.Scratch0, 2); // span
    // Store-release (see __slab_free's load-acquire): publishes the new owner to
    // lockless cross-P readers, not just to threads that take the mcentral lock.
    _b.StoreRelease(VReg.Scratch0, MspanOffOwningP, VReg.Scratch3);

    _b.LockRelease(MspanPoolLockLabel);

    // Return span
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitMcentralReturnSpan: __slab_mcentral_return_span(span_ptr)
  //
  // Returns a fully-free span back to its class's mcentral partial list.
  // Also evicts any stale mcache pointer to this span across all Ps.
  // =========================================================================
  // Stack slots: 0=span_ptr, 1=class_offset (class_index*8), 2=loop_i, 3=mcache_base
  public void EmitMcentralReturnSpan() {
    _b.FunctionStart("__slab_mcentral_return_span", 1, 0x60);

    _b.LockAcquire(MspanPoolLockLabel);

    // Clear owning_p to the "in mcentral / unowned" sentinel under the mcentral
    // lock. Cross-P free invariant: __slab_mcentral_return_span is only called
    // from the local free path's "free_count == total_slots" branch, which
    // means the owning P has just accounted for every slot in this span — so
    // no slot from this span can still be in flight in any other P's
    // remote-free queue. Stamping the sentinel here, combined with the
    // defensive panic in __slab_free's remote path, makes any future invariant
    // violation crash loudly rather than corrupt freed memory.
    _b.LoadLocal(VReg.Scratch0, 0); // span_ptr
    _b.MovRegImm(VReg.Scratch3, (long)(uint)MspanOwningPSentinel);
    // Store-release (see __slab_free's load-acquire): a lockless cross-P free must
    // observe the sentinel (and skip-into-panic) rather than a stale prior owner.
    _b.StoreRelease(VReg.Scratch0, MspanOffOwningP, VReg.Scratch3);

    // Get class_index from span; compute class_offset = class_index * 8
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffClassIndex);
    _b.ShlRegImm(VReg.Scratch1, 3); // class_offset = class_index * 8
    _b.StoreLocal(1, VReg.Scratch1);

    // Compute mcentral entry address: class_offset * 2 = class_index * 16
    _b.LeaGlobal(VReg.Scratch2, McentralArrayLabel);
    _b.ShlRegImm(VReg.Scratch1, 1); // class_offset * 2 = class_index * 16
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1); // Scratch2 = mcentral_addr

    // Prepend span to partial_head
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch2, 0); // old partial_head
    _b.LoadLocal(VReg.Scratch0, 0); // span_ptr
    _b.StoreIndirect(VReg.Scratch0, MspanOffNextSpan, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch2, 0, VReg.Scratch0); // partial_head = span

    // Evict stale mcache pointers
    _b.LoadGlobal(VReg.Scratch0, McacheBaseLabel);
    _b.StoreLocal(3, VReg.Scratch0);

    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(2, VReg.Scratch0); // i = 0

    var evictLoop = UniqueLabel("mcentral_return_evict_loop");
    var evictDone = UniqueLabel("mcentral_return_evict_done");
    var evictNext = UniqueLabel("mcentral_return_evict_next");

    _b.DefineLabel(evictLoop);
    _b.LoadLocal(VReg.Scratch0, 2); // i
    _b.LoadGlobal(VReg.Scratch1, "__sched_max_procs");
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, evictDone);

    // mcache_slot = mcache_base + i * (SlabNumClasses * 8) + class_offset
    _b.LoadLocal(VReg.Scratch0, 2); // i
    _b.MovRegImm(VReg.Scratch1, SlabNumClasses * 8); // 144
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 3); // mcache_base
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 1); // class_offset
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // mcache_slot_addr

    // If *mcache_slot == span_ptr, clear it. Acquire/release so the victim P's
    // load-acquire of its slot observes this clear. The ownership gate in
    // __slab_alloc is the actual safety net for the compare-then-clear window:
    // even if a stale span survives here, the gate rejects it (owning_p != self).
    _b.LoadAcquire(VReg.Scratch1, VReg.Scratch0, 0);
    _b.LoadLocal(VReg.Scratch2, 0); // span_ptr
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.NotEqual, evictNext);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreRelease(VReg.Scratch0, 0, VReg.Scratch1);

    _b.DefineLabel(evictNext);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(2, VReg.Scratch0); // i++
    _b.Jump(evictLoop);

    _b.DefineLabel(evictDone);
    _b.LockRelease(MspanPoolLockLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // Slab trace helpers
  // =========================================================================

  /// <summary>
  /// Allocates an arena-large object (>32768 bytes, <=64MB).
  /// Uses mspan with class_index=-1, registered in arena map, freeable via bitmap.
  /// </summary>
  private void EmitArenaLargeObjectAlloc(int sizeSlot, int classSlot, int resultSlot,
                                          int spanSlot, int arenaBaseSlot, bool mmTrace) {
    if (mmTrace) {
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(classSlot, VReg.Scratch0);
      EmitInlineTraceSlabAlloc(UniqueLabel("sl_alloc_arena_large_trace"), sizeSlot, classSlot);
      EmitTraceDepthInc();
    }

    // num_chunks = (size + ChunkSize - 1) >> ChunkShift
    _b.LoadLocal(VReg.Scratch0, sizeSlot);
    _b.AddRegImm(VReg.Scratch0, ChunkSize - 1);
    _b.ShrRegImm(VReg.Scratch0, ChunkShift);
    _b.StoreLocal(spanSlot, VReg.Scratch0); // temporarily store num_chunks in spanSlot

    // Allocate mspan from metadata slab
    _b.Call("__slab_meta_alloc");
    _b.StoreLocal(arenaBaseSlot, VReg.Scratch0); // temporarily store mspan in arenaBaseSlot

    // Allocate data chunks
    _b.LoadLocal(VReg.Arg0, spanSlot); // num_chunks
    _b.Call("__slab_arena_alloc_chunks");
    _b.StoreLocal(resultSlot, VReg.Scratch0); // data ptr

    // Load arena_base from global
    _b.LoadGlobal(VReg.Scratch3, ArenaLastBaseLabel);

    // Init mspan fields
    _b.LoadLocal(VReg.Scratch0, arenaBaseSlot); // mspan ptr

    _b.LoadLocal(VReg.Scratch1, resultSlot); // data
    _b.StoreIndirect(VReg.Scratch0, MspanOffBaseAddr, VReg.Scratch1);

    // slot_size = num_chunks << ChunkShift
    _b.LoadLocal(VReg.Scratch1, spanSlot); // num_chunks
    _b.ShlRegImm(VReg.Scratch1, ChunkShift);
    _b.StoreIndirect(VReg.Scratch0, MspanOffSlotSize, VReg.Scratch1);

    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffFreeCount, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffFreeList, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffNextSpan, VReg.Scratch1);
    // An arena-large span is never served by the class fast path (free_count is 0
    // and it never enters an mcache), so its bump cursor is never read. Init it
    // anyway: mspan headers come from __slab_meta_alloc, which recycles them
    // through a free list, so an uninitialised field here is stale data, not zero.
    _b.StoreIndirect(VReg.Scratch0, MspanOffBumpNext, VReg.Scratch1);

    _b.MovRegImm(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffTotalSlots, VReg.Scratch1);

    _b.MovRegImm(VReg.Scratch1, -1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffClassIndex, VReg.Scratch1);

    _b.StoreIndirect(VReg.Scratch0, MspanOffArenaBase, VReg.Scratch3); // arena_base

    // Register span in arena map
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_span_register");

    if (mmTrace) {
      EmitTraceDepthDec();
    }
  }

  /// <summary>
  /// Allocates a huge object directly from the OS (>64MB).
  /// Tracked in the OS-direct dynamic array.
  /// </summary>
  private void EmitOsDirectObjectAlloc(int sizeSlot, int classSlot, int resultSlot, bool mmTrace) {
    if (mmTrace) {
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(classSlot, VReg.Scratch0);
      EmitInlineTraceSlabAlloc(UniqueLabel("sl_alloc_os_direct_trace"), sizeSlot, classSlot);
      EmitTraceDepthInc();
    }
    _b.LoadLocal(VReg.Arg0, sizeSlot);
    _b.Call("__slab_os_alloc");
    _b.StoreLocal(resultSlot, VReg.Scratch0);
    if (mmTrace) {
      EmitTraceDepthDec();
    }

    // Register in OS-direct array
    _b.LoadLocal(VReg.Arg0, resultSlot); // ptr
    _b.LoadLocal(VReg.Arg1, sizeSlot);   // size
    _b.Call("__slab_os_direct_insert");
  }

  /// <summary>
  /// If __mm_trace_tag_ctx != 0, prints " TypeName #N".
  /// </summary>
  private void EmitTraceTagCtx(string uniquePrefix) {
    var skipLabel = $"{uniquePrefix}_no_tag_ctx";
    _b.LoadGlobal(VReg.Scratch0, "__mm_trace_tag_ctx");
    _b.JumpIfZero(VReg.Scratch0, skipLabel);
    // Print " "
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_space");
    _b.Call("mm_trace_print_tag");
    // Extract tag_index = low 16 bits, look up name
    _b.LoadGlobal(VReg.Scratch0, "__mm_trace_tag_ctx");
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.MovRegImm(VReg.Scratch1, 0xFFFF);
    _b.AndRegReg(VReg.Arg0, VReg.Scratch1);
    _b.Call("mm_tag_lookup");
    _b.MovRegReg(VReg.Arg0, VReg.Ret);
    _b.Call("mm_trace_print_tag");
    // Print " #N" from alloc_id (upper bits >> 16)
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_hash");
    _b.Call("mm_trace_print_tag");
    _b.LoadGlobal(VReg.Scratch0, "__mm_trace_tag_ctx");
    _b.ShrRegImm(VReg.Scratch0, 16);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("mm_trace_print_i64");
    _b.DefineLabel(skipLabel);
  }

  /// <summary>Emit: indent + "sl_alloc [TypeName #N] size=S class=C\n"</summary>
  private void EmitInlineTraceSlabAlloc(string uniquePrefix, int sizeSlot, int classSlot) {
    if (Compiler.MmTraceRawOnly) return; // raw-only: suppress slab traces
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_alloc");
    _b.Call("mm_trace_print_tag");
    EmitTraceTagCtx(uniquePrefix);
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_size_eq");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, sizeSlot);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_class");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, classSlot);
    _b.Call("mm_trace_print_class");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  /// <summary>Emit: indent + "os_alloc [TypeName #N] size=N\n"</summary>
  private void EmitInlineTraceOsAlloc(string uniquePrefix, VReg sizeReg) {
    if (Compiler.MmTraceRawOnly) return; // raw-only: suppress os-alloc traces
    _b.MovRegReg(VReg.Arg0, sizeReg);
    _b.StoreLocal(OsTraceScratchSlot, VReg.Arg0);
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_os_alloc");
    _b.Call("mm_trace_print_tag");
    EmitTraceTagCtx(uniquePrefix);
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_size_eq");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, OsTraceScratchSlot);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  /// <summary>Emit: indent + "os_free [TypeName #N] size=N\n"</summary>
  private void EmitInlineTraceOsFree(string uniquePrefix, VReg sizeReg) {
    if (Compiler.MmTraceRawOnly) return; // raw-only: suppress os-free traces
    _b.MovRegReg(VReg.Arg0, sizeReg);
    _b.StoreLocal(OsTraceScratchSlot, VReg.Arg0);
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_os_free");
    _b.Call("mm_trace_print_tag");
    EmitTraceTagCtx(uniquePrefix);
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_size_eq");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, OsTraceScratchSlot);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  /// <summary>Emit: indent + "sl_free [TypeName #N] size=N class=C\n"</summary>
  private void EmitInlineTraceSlabFree(string uniquePrefix, int sizeSlot, int classSlot) {
    if (Compiler.MmTraceRawOnly) return; // raw-only: suppress slab traces
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_free");
    _b.Call("mm_trace_print_tag");
    EmitTraceTagCtx(uniquePrefix);
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_size_eq");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, sizeSlot);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_class");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, classSlot);
    _b.Call("mm_trace_print_class");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  // =========================================================================
  // MAXON_SLAB_GLOBAL_LOCK helpers (PLAN 1a.1) + MAXON_SLAB_STATS counters (1a.2)
  // =========================================================================

  /// <summary>
  /// Emit the MAXON_SLAB_GLOBAL_LOCK acquire, gated on the runtime-cached
  /// <see cref="SlabGlobalLockEnabledLabel"/> flag. When disabled (the default),
  /// the hot path is a single load + one correctly-predicted branch (taken, over
  /// the spin body, to the skip label). When enabled,
  /// spins on an atomic compare-exchange of <see cref="SlabGlobalLockLabel"/>
  /// (0 -> 1). This is a test-and-set spinlock, not a kernel-wait lock: a
  /// contended kernel wait on a green thread's small stack once corrupted the
  /// self-hosted runtime (PLAN 1a.1), so we never park in the kernel here.
  ///
  /// Under MAXON_SLAB_STATS, each failed CAS bumps
  /// <see cref="SlabLockWaitCountLabel"/> — the count of real spin iterations,
  /// i.e. genuine lock contention.
  ///
  /// Clobbers Scratch0..Scratch3. Safe at the call site: acquire runs at
  /// function entry before any live value exists (the sole arg is already spilled
  /// to slot 0 by the prologue). Not recursively acquirable: the allocator slow
  /// paths call into mcentral/arena helpers (a separate MspanPoolLock) but never
  /// back into __slab_alloc / __slab_free, so this non-recursive lock is safe.
  /// </summary>
  private void EmitSlabGlobalLockAcquire() {
    var skip = UniqueLabel("slab_glock_acq_skip");
    var spin = UniqueLabel("slab_glock_acq_spin");
    var acquired = UniqueLabel("slab_glock_acq_got");
    var noStat = UniqueLabel("slab_glock_acq_no_stat");

    // Gate: default hot path is one load + one correctly-predicted branch.
    _b.LoadGlobal(VReg.Scratch0, SlabGlobalLockEnabledLabel);
    _b.JumpIfZero(VReg.Scratch0, skip);

    // Re-materialize the lock address and CAS inputs every iteration so we stay
    // robust against AtomicCAS's implicit clobbers (it trashes Scratch0/RAX).
    // The extra ops only run when the lock is enabled AND contended.
    _b.DefineLabel(spin);
    _b.LeaGlobal(VReg.Scratch2, SlabGlobalLockLabel);
    _b.MovRegImm(VReg.Scratch0, SlabGlobalLockFree); // expected = 0
    _b.MovRegImm(VReg.Scratch1, SlabGlobalLockHeld); // desired = 1
    _b.AtomicCAS(VReg.Scratch2, 0, VReg.Scratch0, VReg.Scratch1); // success -> Scratch3 != 0
    _b.JumpIfNonZero(VReg.Scratch3, acquired);

    // CAS failed: the lock is held elsewhere. Count the spin under stats, then retry.
    _b.LoadGlobal(VReg.Scratch0, SlabStatsEnabledLabel);
    _b.JumpIfZero(VReg.Scratch0, noStat);
    _b.LeaGlobal(VReg.Scratch0, SlabLockWaitCountLabel);
    _b.AtomicInc(VReg.Scratch0, 0);
    _b.DefineLabel(noStat);
    _b.Jump(spin);

    _b.DefineLabel(acquired);
    _b.DefineLabel(skip);
  }

  /// <summary>
  /// Emit the MAXON_SLAB_GLOBAL_LOCK release, gated on the same enabled flag.
  /// Publishes the lock word back to 0 with a store-release (STLR on ARM64, a
  /// plain store on x86 TSO), pairing with the acquire's CAS. Clobbers
  /// Scratch0/Scratch2. Safe at every return point: the caller has already parked
  /// its return value in a stack slot before calling this, so register clobbers
  /// do not corrupt the result.
  /// </summary>
  private void EmitSlabGlobalLockRelease() {
    var skip = UniqueLabel("slab_glock_rel_skip");
    _b.LoadGlobal(VReg.Scratch0, SlabGlobalLockEnabledLabel);
    _b.JumpIfZero(VReg.Scratch0, skip);

    _b.LeaGlobal(VReg.Scratch2, SlabGlobalLockLabel);
    _b.MovRegImm(VReg.Scratch0, SlabGlobalLockFree);
    _b.StoreRelease(VReg.Scratch2, 0, VReg.Scratch0);

    _b.DefineLabel(skip);
  }

  /// <summary>
  /// Emit a MAXON_SLAB_STATS-gated atomic increment of an 8-byte contention
  /// counter <paramref name="counterLabel"/>. The default (stats-off) path is a
  /// single load + correctly-predicted not-taken branch; the atomic increment
  /// only runs when stats are enabled. Atomic because these counters are bumped
  /// from multiple Ps concurrently. Clobbers Scratch0. Shared by every
  /// off-hot-path allocator counter so the (load/gate/lea/atomic-inc) shape lives
  /// in exactly one place — see the individual call sites for what each counter
  /// measures (ownership-gate miss, cross-P remote free).
  /// </summary>
  private void EmitStatsGatedAtomicInc(string counterLabel) {
    var skip = UniqueLabel("slab_stat_no_count");
    _b.LoadGlobal(VReg.Scratch0, SlabStatsEnabledLabel);
    _b.JumpIfZero(VReg.Scratch0, skip);
    _b.LeaGlobal(VReg.Scratch0, counterLabel);
    _b.AtomicInc(VReg.Scratch0, 0);
    _b.DefineLabel(skip);
  }

  /// <summary>
  /// Emit the MAXON_SLAB_STATS exit dump: one stderr line
  /// "[slab-stats] lock_wait=&lt;n&gt; ownership_gate_miss=&lt;n&gt; remote_free=&lt;n&gt;".
  /// Gated on the runtime-cached stats flag (no-op when unset). Called from
  /// mm_leak_check so it runs once on the normal process-exit path. Kept on stderr
  /// (never stdout) so it never pollutes program output.
  /// </summary>
  public void EmitSlabStatsDump() {
    var skip = UniqueLabel("slab_stats_dump_skip");
    _b.LoadGlobal(VReg.Scratch0, SlabStatsEnabledLabel);
    _b.JumpIfZero(VReg.Scratch0, skip);

    _b.LeaSymdata(VReg.Arg0, SlabStatsPrefixLabel);
    _b.Call(_b.WriteStderrLabel);
    _b.LoadGlobal(VReg.Arg0, SlabLockWaitCountLabel);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, SlabStatsMidLabel);
    _b.Call(_b.WriteStderrLabel);
    _b.LoadGlobal(VReg.Arg0, SlabOwnershipGateMissCountLabel);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, SlabStatsRemoteFreeLabel);
    _b.Call(_b.WriteStderrLabel);
    _b.LoadGlobal(VReg.Arg0, SlabRemoteFreeCountLabel);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, SlabStatsNewlineLabel);
    _b.Call(_b.WriteStderrLabel);

    _b.DefineLabel(skip);
  }

  /// <summary>
  /// span->free_count-- . Shared by the free-list-pop and bump-carve arms of the
  /// alloc fast path so the two can never drift.
  ///
  /// INV-1: free_count == |free_list| + (bump_end - bump_next) / slot_size.
  /// Exactly four sites may write free_count — this one (-1, on BOTH arms),
  /// __slab_free's push (+1), __slab_mspan_alloc (= total_slots), and the
  /// mcentral reclaim (= total_slots). Anything else double-issues a slot.
  ///
  /// Reads span_ptr from stack slot 4; clobbers Scratch1, Scratch2.
  /// </summary>
  private void EmitSlabAllocFreeCountDec() {
    _b.LoadLocal(VReg.Scratch1, 4); // span_ptr
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffFreeCount);
    _b.SubRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, MspanOffFreeCount, VReg.Scratch2);
  }

  // =========================================================================
  // EmitSlabAlloc: __slab_alloc(size) -> ptr        — returns ZEROED memory
  // EmitSlabAllocRaw: __slab_alloc_raw(size) -> ptr — does NOT zero
  //
  // Header-free allocation routing:
  // 1. size > ArenaSize: OS-direct (tracked in dynamic array)
  // 2. size > SlabMaxSmallSize: arena-large (mspan + chunks, freeable)
  // 3. else: slab fast path (mcache -> mcentral -> mspan_alloc)
  //
  // This is Go's `mallocgc(size, typ, needzero bool)`: the CALLER declares
  // whether it needs zeroed memory. Two emitted bodies rather than one body with
  // a runtime flag — the machine code is a couple of KB and this keeps the hot
  // path free of an extra argument and an extra branch.
  //
  // __slab_alloc_raw is a SHARP TOOL. See the audit rule on EmitSlabAllocRaw.
  // =========================================================================
  // Stack slots: 0=size, 1=class_index, 2=P_id, 3=mcache_slot_addr,
  //              4=span_ptr, 5=alloc_result, 6=arena_base_tmp, 7=scratch
  public void EmitSlabAlloc(bool mmTrace)
    => EmitSlabAllocBody("__slab_alloc", needzero: true, mmTrace);

  /// <summary>
  /// __slab_alloc_raw(size) — allocation WITHOUT zeroing. Go's needzero=false.
  ///
  /// AUDIT RULE — read this before adding a caller:
  ///
  ///   __slab_alloc_raw may ONLY be used where the caller provably writes EVERY
  ///   BYTE of the returned region before anything else can read it, AND the
  ///   region is never walked as managed pointers.
  ///
  ///   A region is "walked as managed pointers" if __destruct___ManagedMemory's
  ///   element walk, a Map/Set slot-table teardown, or an array.set old-occupant
  ///   decref will ever run over it. Such a buffer MUST come from __slab_alloc —
  ///   a non-zeroed slot read as a pointer and decref'd is precisely the bug this
  ///   whole design exists to prevent (a sparsely-filled Map hash table whose
  ///   unwritten slots held the previous occupant's garbage).
  ///
  ///   When in doubt, use __slab_alloc. It is now cheap: a slot carved from a
  ///   span's virgin bump region costs NOTHING to zero.
  ///
  /// The audited caller set is deliberately tiny, mirroring Go's (rawbyteslice /
  /// rawstring / growslice): mm_realloc's new buffer, which memcpy's the prefix
  /// and explicitly zeroes the grown tail — together covering every byte.
  /// </summary>
  public void EmitSlabAllocRaw(bool mmTrace)
    => EmitSlabAllocBody("__slab_alloc_raw", needzero: false, mmTrace);

  private void EmitSlabAllocBody(string symbol, bool needzero, bool mmTrace) {
    _b.FunctionStart(symbol, 1, 0x60);

    // MAXON_SLAB_GLOBAL_LOCK A/B safety net: bracket the entire body. No-op unless
    // enabled; when enabled, serialises alloc against alloc/free so the lock-free
    // ownership-gate / remote-free paths can be A/B-bisected. Released before every
    // return below. (size arg is already spilled to slot 0, so this can clobber regs.)
    EmitSlabGlobalLockAcquire();

    // Check if allocator is initialized
    _b.LoadGlobal(VReg.Scratch0, SlabInitDoneLabel);
    var slabReady = UniqueLabel("slab_alloc_ready");
    _b.JumpIfNonZero(VReg.Scratch0, slabReady);

    // Fallback: allocator not initialized — use OS-direct path
    EmitOsDirectObjectAlloc(sizeSlot: 0, classSlot: 1, resultSlot: 5, mmTrace);
    EmitSlabGlobalLockRelease();
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(slabReady);

    // --- OS-direct check: size > ArenaSize ---
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.CmpRegImm(VReg.Scratch0, ArenaSize);
    var notOsDirect = UniqueLabel("slab_alloc_not_os_direct");
    _b.JumpIf(Condition.BelowEqual, notOsDirect);

    EmitOsDirectObjectAlloc(sizeSlot: 0, classSlot: 1, resultSlot: 5, mmTrace);
    EmitSlabGlobalLockRelease();
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(notOsDirect);

    // --- Arena-large check: size > max class size (32768) ---
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.CmpRegImm(VReg.Scratch0, SlabMaxSmallSize);
    var smallPath = UniqueLabel("slab_alloc_small");
    _b.JumpIf(Condition.BelowEqual, smallPath);

    // Arena-large path: mspan + bitmap chunks, registered in arena map
    EmitArenaLargeObjectAlloc(sizeSlot: 0, classSlot: 1, resultSlot: 5,
                              spanSlot: 4, arenaBaseSlot: 6, mmTrace);
    EmitSlabGlobalLockRelease();
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    // --- Small object path ---
    _b.DefineLabel(smallPath);

    // Look up size class: linear scan of class_sizes table
    _b.LoadLocal(VReg.Scratch0, 0); // size

    _b.ZeroReg(VReg.Scratch1); // class_index = 0
    _b.LeaSymdata(VReg.Scratch2, SlabClassSizesLabel);

    var classLoop = UniqueLabel("slab_class_loop");
    var classFound = UniqueLabel("slab_class_found");

    _b.DefineLabel(classLoop);
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch1);
    _b.ShlRegImm(VReg.Scratch3, 3);
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch3, 0);
    _b.CmpRegReg(VReg.Scratch3, VReg.Scratch0);
    _b.JumpIf(Condition.AboveEqual, classFound);
    _b.AddRegImm(VReg.Scratch1, 1);
    _b.Jump(classLoop);

    _b.DefineLabel(classFound);
    _b.StoreLocal(1, VReg.Scratch1); // class_index

    // --- Load mcache slot ---
    _b.LoadCurrentP(VReg.Scratch0);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, POffId);
    _b.StoreLocal(2, VReg.Scratch0); // P_id

    _b.MovRegImm(VReg.Scratch1, SlabNumClasses * 8); // 144
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.LoadGlobal(VReg.Scratch1, McacheBaseLabel);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.ShlRegImm(VReg.Scratch1, 3);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.StoreLocal(3, VReg.Scratch0); // mcache_slot_addr

    var retryLabel = UniqueLabel("slab_alloc_retry");
    _b.DefineLabel(retryLabel);

    // --- Lock-free fast path ---
    // span->free_list is mutated only by the owning P (Mimalloc single-writer
    // invariant; see __slab_free's localPath comment). Cross-P frees bypass
    // free_list entirely — they push onto P->remote_free_head instead and the
    // owner drains lazily on the slow path below. This means the prior global
    // MspanPoolLock that serialised alloc against cross-P free is no longer
    // needed on the fast path.
    _b.LoadLocal(VReg.Scratch0, 3);
    // Acquire: pairs with the StoreRelease publishers of *mcache_slot (slow-path
    // refill, eviction clear) so observing a span pointer also observes that
    // span's field stores.
    _b.LoadAcquire(VReg.Scratch1, VReg.Scratch0, 0); // span = *mcache_slot
    var slowPath = UniqueLabel("slab_alloc_slow_path");
    var gateMiss = UniqueLabel("slab_alloc_gate_miss");
    _b.JumpIfZero(VReg.Scratch1, slowPath);

    // Check if span has free slots
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffFreeCount);
    _b.JumpIfZero(VReg.Scratch2, slowPath);

    // Ownership gate (the core multi-M fix): only pop from a span THIS P still
    // owns. A span returned to mcentral (owning_p = sentinel) or handed to
    // another P keeps free_count != 0, so the emptiness check above does NOT
    // catch a recycled span; a lock-free pop from it would corrupt a span
    // another P owns. owning_p is load-acquire (pairs with its StoreRelease
    // writers). On x86 TSO this whole gate is plain loads + a compare.
    //
    // owning_p == self is STABLE across the non-atomic pop below: every writer
    // of this span's free_list/free_count — this fast-path pop, the remote-free
    // drain push, the local-free push, and return_span's sentinel-stamp — runs
    // on the OWNING P, and a P is bound to exactly one M (OS thread) at a time.
    // So once the gate observes self-ownership, no other thread can mutate this
    // span before the pop completes; the single-writer invariant holds.
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, MspanOffOwningP);
    _b.LoadCurrentP(VReg.Scratch3);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch3, POffId);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    // Not owned by this P: divert through the ownership-gate-miss counter (PLAN
    // 1a.2) on the way to the slow path. The hot (owned) path falls through with a
    // single not-taken branch, unchanged. The earlier empty-cache / empty-span
    // bailouts above jump straight to slowPath and are deliberately NOT counted —
    // only a genuine cross-P ownership mismatch is a "gate miss".
    _b.JumpIf(Condition.NotEqual, gateMiss);

    // Fast path. Two sources of a free slot, and they differ in whether the
    // memory is dirty:
    //
    //   free_list != 0  -> a RECYCLED slot. It still holds the previous
    //                      occupant's bytes (plus the free-list link we wrote
    //                      into slot[0] when it was freed), so it MUST be
    //                      zeroed before the caller sees it.
    //
    //   free_list == 0  -> carve from the span's VIRGIN bump region. These
    //                      slots have never been handed out, and their pages
    //                      came zeroed from the arena, so they are ALREADY
    //                      zero. No memzero at all.
    //
    // This is Go's mallocgc, with one refinement: Go's needzero is per-SPAN, so
    // Go re-zeroes even never-used slots in a span that has seen a single free.
    // Our cursor is per-SLOT, so we never pay for a slot that was never dirtied.
    //
    // The free-list pop is the FALL-THROUGH (it is the steady state once a span
    // has cycled); the bump carve is the out-of-line branch.
    _b.StoreLocal(4, VReg.Scratch1); // save span_ptr

    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, MspanOffFreeList);
    var bumpCarve = UniqueLabel("slab_alloc_bump");
    _b.JumpIfZero(VReg.Scratch0, bumpCarve);

    // --- Recycled slot: pop the free list. ---
    _b.StoreLocal(5, VReg.Scratch0); // alloc_result

    // span->free_list = [result] (next pointer)
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 0);
    _b.LoadLocal(VReg.Scratch1, 4);
    _b.StoreIndirect(VReg.Scratch1, MspanOffFreeList, VReg.Scratch2);

    EmitSlabAllocFreeCountDec();

    // Zero the recycled slot. This subsumes the old slot[0]-clear (the link is
    // inside the region we are about to wipe) AND the old zero-on-free: memory
    // is now cleaned when it is handed OUT, not when it is handed back, so a
    // slot that is freed and never re-allocated is never zeroed at all.
    //
    // In mmDebug this also wipes __mm_free's 0xDEAD poison — which is exactly
    // right: the poison's job is to catch reads BETWEEN free and re-alloc, and
    // it now survives untouched for that whole window.
    if (needzero) {
      _b.LoadLocal(VReg.Arg0, 5);     // slot_base
      _b.LoadLocal(VReg.Scratch0, 4); // span_ptr
      _b.LoadIndirect(VReg.Arg1, VReg.Scratch0, MspanOffSlotSize);
      _b.Call("__slab_memzero");
    }

    var haveSlot = UniqueLabel("slab_alloc_have_slot");
    _b.Jump(haveSlot);

    // --- Virgin slot: carve from the bump region. Already zero. ---
    _b.DefineLabel(bumpCarve);
    _b.LoadLocal(VReg.Scratch1, 4); // span_ptr
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, MspanOffBumpNext);
    _b.StoreLocal(5, VReg.Scratch0); // alloc_result = bump_next

    // bump_next += slot_size
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffSlotSize);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch1, MspanOffBumpNext, VReg.Scratch0);

    EmitSlabAllocFreeCountDec();

    _b.DefineLabel(haveSlot);

    if (mmTrace) {
      EmitInlineTraceSlabAlloc(UniqueLabel("sl_alloc_small_trace"), sizeSlot: 0, classSlot: 1);
    }

    EmitSlabGlobalLockRelease();
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    // Ownership-gate miss: span is cached but owned by another P. Count the
    // cross-P event (stats-gated), then fall through into the slow path, which
    // drains remote frees and refills via mcentral. Placed out of line so the hot
    // path never touches it.
    _b.DefineLabel(gateMiss);
    // Ownership-gate miss: the span is cached but owned by another P — genuine
    // cross-P allocator traffic. Counted even with the global lock OFF, so it
    // distinguishes "cross-P activity happens" from "cross-P activity races on
    // the lock-free paths".
    EmitStatsGatedAtomicInc(SlabOwnershipGateMissCountLabel);

    // --- Slow path: drain remote frees, then either re-serve from cache or
    // ask mcentral for a span. Drain runs before mcentral_get_span so that a
    // burst of cross-P frees aimed at this P's spans can be reclaimed without
    // grabbing a fresh span from the central pool. ---
    _b.DefineLabel(slowPath);

    _b.LoadCurrentP(VReg.Arg0);
    _b.Call("__slab_drain_remote_frees");

    // Re-probe the cached span — drain may have refilled it (or evicted it via
    // __slab_mcentral_return_span if it reached full).
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadAcquire(VReg.Scratch1, VReg.Scratch0, 0); // span = *mcache_slot (acquire)
    var stillEmpty = UniqueLabel("slab_alloc_still_empty");
    _b.JumpIfZero(VReg.Scratch1, stillEmpty);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffFreeCount);
    _b.JumpIfZero(VReg.Scratch2, stillEmpty);
    // The re-probe must apply the SAME ownership gate as the fast path, else a
    // non-owned-but-non-empty cached span would bounce fast-path(gate-reject) ->
    // slow-path -> re-probe(retry) forever. Not owned -> refill via mcentral.
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, MspanOffOwningP);
    _b.LoadCurrentP(VReg.Scratch3);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch3, POffId);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.JumpIf(Condition.NotEqual, stillEmpty);
    // Cached span has slots again and we still own it — jump back to fast path.
    _b.Jump(retryLabel);

    _b.DefineLabel(stillEmpty);
    // No slots in cache. Clear the mcache slot (idempotent — may already be NULL).
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreRelease(VReg.Scratch0, 0, VReg.Scratch1);

    _b.LoadLocal(VReg.Arg0, 1); // class_index
    _b.Call("__slab_mcentral_get_span");
    _b.StoreLocal(4, VReg.Scratch0);

    _b.LoadLocal(VReg.Scratch1, 3);
    // Release: publish the span pointer after its lock-initialised fields, so a
    // cross-P LoadAcquire of this slot (or the eviction's read) sees both.
    _b.StoreRelease(VReg.Scratch1, 0, VReg.Scratch0); // *mcache_slot = new_span

    _b.Jump(retryLabel);
  }

  /// <summary>
  /// Emit code that pushes <paramref name="slotReg"/> onto
  /// <paramref name="spanReg"/>->free_list, increments free_count, and if the
  /// span has now become fully free (free_count == total_slots) calls
  /// __slab_mcentral_return_span(span).
  ///
  /// <paramref name="t0"/> and <paramref name="t1"/> must be distinct from
  /// spanReg, slotReg, and each other. Caller is responsible for saving the
  /// slot_base and the span pointer in stack slots if they need them after the
  /// helper returns (the mcentral call clobbers all scratch regs).
  ///
  /// Used by both the same-thread free path in __slab_free and the per-node
  /// loop body in __slab_drain_remote_frees — the single-writer invariant on
  /// span->free_list holds in both, so no locking is required.
  /// </summary>
  private void EmitPushSlotOntoSpanFreeList(VReg spanReg, VReg slotReg,
                                            VReg t0, VReg t1) {
    // old_head = span->free_list; slot[0] = old_head; span->free_list = slot.
    _b.LoadIndirect(t0, spanReg, MspanOffFreeList);
    _b.StoreIndirect(slotReg, 0, t0);
    _b.StoreIndirect(spanReg, MspanOffFreeList, slotReg);

    // span->free_count++. Result kept in t1 so the fully-free compare below
    // doesn't reload it.
    _b.LoadIndirect(t1, spanReg, MspanOffFreeCount);
    _b.AddRegImm(t1, 1);
    _b.StoreIndirect(spanReg, MspanOffFreeCount, t1);

    _b.LoadIndirect(t0, spanReg, MspanOffTotalSlots);
    _b.CmpRegReg(t1, t0);
    var notFullyFree = UniqueLabel("push_slot_not_full");
    _b.JumpIf(Condition.NotEqual, notFullyFree);
    _b.MovRegReg(VReg.Arg0, spanReg);
    _b.Call("__slab_mcentral_return_span");
    _b.DefineLabel(notFullyFree);
  }

  // =========================================================================
  // EmitSlabDrainRemoteFrees: __slab_drain_remote_frees(P*)
  //
  // Mimalloc-style drain of the per-P MPSC remote-free queue. Called only by
  // the owning P (on the __slab_alloc slow path). One atomic CAS detaches the
  // entire chain (head -> NULL), then we walk it and push each slot onto its
  // owning span's free_list. Because we ARE the owner, the per-span pushes
  // are lockless (single-writer invariant).
  //
  // Spans that reach free_count == total_slots during the walk get returned
  // to mcentral immediately — the mcentral return path takes MspanPoolLock
  // internally, which is fine: it's an off-fast-path event.
  //
  // No trace emission here: cross-P queue traffic is inherently non-
  // deterministic (depends on which worker P serviced an alloc), and surfacing
  // it in --mm-trace would break the byte-exact stderr comparisons spec tests
  // rely on. The MPSC mechanics are still observable via wall-clock perf and
  // through __slab_mcentral_return_span counters; deterministic-trace users
  // get refcount-shape events only.
  // =========================================================================
  // Stack slots: 0=P_ptr (arg), 1=current_node, 2=next_node
  public void EmitSlabDrainRemoteFrees() {
    _b.FunctionStart("__slab_drain_remote_frees", 1, 0x30);

    var drainDone = UniqueLabel("drain_done");

    // Detach the entire queue with a single CAS: head -> NULL, head retained
    // in slot 1 (parked before the CAS because x86's AtomicCAS clobbers RAX).
    //   retry:
    //     old = P->remote_free_head           // also parked to slot 1
    //     if old == NULL: nothing to drain
    //     if CAS(head, old, NULL): start walking
    //     else: retry
    var detachRetry = UniqueLabel("drain_detach_retry");
    _b.DefineLabel(detachRetry);
    _b.LoadLocal(VReg.Scratch2, 0); // P*
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch2, POffRemoteFreeHead); // expected (in RAX on x86)
    _b.JumpIfZero(VReg.Scratch0, drainDone);
    _b.StoreLocal(1, VReg.Scratch0); // park chain head BEFORE CAS clobbers Scratch0/RAX
    _b.ZeroReg(VReg.Scratch1); // desired = NULL
    _b.AtomicCAS(VReg.Scratch2, POffRemoteFreeHead, VReg.Scratch0, VReg.Scratch1);
    _b.CmpRegImm(VReg.Scratch3, 0);
    _b.JumpIf(Condition.Equal, detachRetry);

    // Walk the detached chain. current_node lives in Scratch1, refreshed from
    // slot 1 each iteration (`next` is parked across the span_lookup call).
    _b.LoadLocal(VReg.Scratch1, 1); // current_node = chain head

    var walkLoop = UniqueLabel("drain_walk");
    _b.DefineLabel(walkLoop);
    _b.JumpIfZero(VReg.Scratch1, drainDone);

    // next = current_node[0]
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, 0);
    _b.StoreLocal(2, VReg.Scratch0); // park next on stack across __slab_span_lookup
    _b.StoreLocal(1, VReg.Scratch1); // re-park current so we can pass it to lookup via Arg0

    // span = __slab_span_lookup(current_node)
    _b.MovRegReg(VReg.Arg0, VReg.Scratch1);
    _b.Call("__slab_span_lookup");
    // Scratch0 = span_ptr. Per the cross-P invariant, this lookup must succeed
    // for any slot that was ever pushed onto a remote queue — the slot lives
    // in a span the arena map knows about and the span hasn't been freed
    // (otherwise it couldn't have been allocated in the first place).
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch0); // span (preserve across reloads)
    _b.LoadLocal(VReg.Scratch1, 1); // reload current_node

    // Push current_node onto span->free_list and conditionally hand the span
    // back to mcentral if it has become fully free.
    EmitPushSlotOntoSpanFreeList(spanReg: VReg.Scratch3, slotReg: VReg.Scratch1,
                                 t0: VReg.Scratch0, t1: VReg.Scratch2);

    // Advance: current_node = next; loop.
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.Jump(walkLoop);

    _b.DefineLabel(drainDone);
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitSlabFree: __slab_free(slot_base)
  //
  // Header-free free path:
  // 1. Look up span via arena map (__slab_span_lookup)
  // 2. If found with class_index == -1: arena-large free (chunks + unregister + meta_free)
  // 3. If found with class_index >= 0: normal slab free (zero, push to free list)
  // 4. If not found: OS-direct remove + OsFreePages
  // =========================================================================
  // Stack slots: 0=slot_base, 1=span_ptr, 2=slot_size, 3=class_index,
  //              4=chunk_index, 5=num_chunks, 6=arena_base
  public void EmitSlabFree(bool mmTrace) {
    _b.FunctionStart("__slab_free", 1, 0x50);

    // MAXON_SLAB_GLOBAL_LOCK A/B safety net: bracket the entire body (mirror of
    // __slab_alloc). Released before every return below. No-op unless enabled.
    // (slot_base arg is already spilled to slot 0, so this can clobber regs.)
    EmitSlabGlobalLockAcquire();

    // NULL check
    _b.LoadLocal(VReg.Scratch0, 0);
    var notNull = UniqueLabel("slab_free_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    EmitSlabGlobalLockRelease();
    _b.FunctionEnd();

    _b.DefineLabel(notNull);

    // Look up span via arena map
    _b.LoadLocal(VReg.Arg0, 0);
    _b.Call("__slab_span_lookup");
    _b.StoreLocal(1, VReg.Scratch0); // span_ptr or NULL

    var notSlabSpan = UniqueLabel("slab_free_not_slab");
    _b.JumpIfZero(VReg.Scratch0, notSlabSpan);

    // Verify pointer is within span bounds (defensive check)
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffBaseAddr);
    _b.LoadLocal(VReg.Scratch2, 0);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.JumpIf(Condition.Below, notSlabSpan);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, MspanOffSlotSize);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch0, MspanOffTotalSlots);
    _b.MulRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch2, 0);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, notSlabSpan);

    // Check if arena-large (class_index == -1)
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffClassIndex);
    _b.StoreLocal(3, VReg.Scratch1); // class_index
    _b.CmpRegImm(VReg.Scratch1, -1);
    var normalSlabFree = UniqueLabel("slab_free_normal");
    _b.JumpIf(Condition.NotEqual, normalSlabFree);

    // --- Arena-large free ---
    if (mmTrace) {
      _b.LoadLocal(VReg.Scratch0, 1);
      _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, MspanOffSlotSize);
      _b.StoreLocal(2, VReg.Scratch0); // slot_size (= allocation size)
      EmitInlineTraceSlabFree(UniqueLabel("sl_free_arena_large_trace"), sizeSlot: 2, classSlot: 3);
    }

    // chunk_index = (span->base_addr - span->arena_base) >> ChunkShift
    _b.LoadLocal(VReg.Scratch0, 1); // span
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffBaseAddr);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, MspanOffArenaBase);
    _b.StoreLocal(6, VReg.Scratch2); // arena_base
    _b.SubRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.ShrRegImm(VReg.Scratch1, ChunkShift);
    _b.StoreLocal(4, VReg.Scratch1); // chunk_index

    // num_chunks = span->slot_size >> ChunkShift
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffSlotSize);
    _b.ShrRegImm(VReg.Scratch1, ChunkShift);
    _b.StoreLocal(5, VReg.Scratch1); // num_chunks

    // __slab_arena_free_chunks(arena_base, chunk_index, num_chunks)
    _b.LoadLocal(VReg.Arg0, 6); // arena_base
    _b.LoadLocal(VReg.Arg1, 4); // chunk_index
    _b.LoadLocal(VReg.Arg2, 5); // num_chunks
    _b.Call("__slab_arena_free_chunks");

    // __slab_span_unregister(span)
    _b.LoadLocal(VReg.Arg0, 1);
    _b.Call("__slab_span_unregister");

    // __slab_meta_free(span)
    _b.LoadLocal(VReg.Arg0, 1);
    _b.Call("__slab_meta_free");

    EmitSlabGlobalLockRelease();
    _b.FunctionEnd();

    // --- Normal slab object free ---
    _b.DefineLabel(normalSlabFree);

    _b.LoadLocal(VReg.Scratch0, 1); // span_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffSlotSize);
    _b.StoreLocal(2, VReg.Scratch1); // slot_size

    if (mmTrace) {
      EmitInlineTraceSlabFree(UniqueLabel("sl_free_slab_trace"), sizeSlot: 2, classSlot: 3);
    }

    // NO zeroing here. Memory is cleaned when it is handed OUT (__slab_alloc's
    // free-list-pop arm), not when it is handed back. Two reasons:
    //
    //   * A slot that is freed and never re-allocated is never zeroed at all —
    //     and at process exit that is most of the heap.
    //   * Zeroing on free would dirty the slot, defeating the whole point: the
    //     alloc side distinguishes recycled slots (dirty, must zero) from virgin
    //     bump-region slots (already zero, free of charge). It cannot make that
    //     distinction if free has already touched the memory.
    //
    // mmDebug's 0xDEADDEADDEADDEAD poison (written by mm_free) is now simply left
    // in place, which is strictly better: it survives the entire free->realloc
    // window it exists to police, and __slab_alloc's memzero wipes it at exactly
    // the moment the slot legitimately becomes live again.

    // Mimalloc-style local-vs-remote routing.
    //
    // Read the span's current owning_p and the calling thread's P id. If they
    // match, this is a same-thread free: push directly onto span->free_list
    // (single-writer invariant — see __slab_alloc's fast path comment). If
    // they don't match, this is a cross-thread free: push the slot onto the
    // OWNER P's remote-free MPSC queue and return. The owner will drain it on
    // its next __slab_alloc slow path.
    //
    // Special case: raw OS threads (the Windows IOCP completion loop, the
    // sync worker, the fault handler) call mm_raw_free without ever having
    // had a Maxon P assigned via TLS — they are external producers from the
    // allocator's perspective. LoadCurrentP returns NULL on those threads, so
    // we force them down the remote path: they are by construction not the
    // owning P of any span, so a CAS-push onto the owner's queue is correct.
    _b.LoadLocal(VReg.Scratch0, 1); // span_ptr
    // Load-acquire: on ARM64 a plain load could read a stale owning_p (the previous
    // owner, or a value from before the span was sentinel-stamped), misrouting the
    // free onto the wrong P's queue and corrupting that span's free_count. Pairs with
    // the StoreRelease publishers in mspan_alloc / mcentral_get_span / return_span.
    // No-op vs LoadIndirect on x86 (TSO already orders every load as acquire).
    _b.LoadAcquire(VReg.Scratch1, VReg.Scratch0, MspanOffOwningP);
    _b.StoreLocal(4, VReg.Scratch1); // owning_p (saved for the remote-path target lookup)
    _b.LoadCurrentP(VReg.Scratch2);
    var localPath = UniqueLabel("slab_free_local");
    var remotePath = UniqueLabel("slab_free_remote");
    // Raw OS threads (no Maxon P): skip the id compare and go straight to remote.
    _b.JumpIfZero(VReg.Scratch2, remotePath);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch2, POffId);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Equal, localPath);

    // Defensive: detect the sentinel owning_p (= span has been returned to
    // mcentral). Per the cross-P free invariant in __slab_mcentral_return_span,
    // this is unreachable in correct code — the owning P drains all in-flight
    // remote frees for a span before returning it to mcentral. CLAUDE.md "no
    // silent fallthrough": a logic bug that lets a remote free through after
    // sentinel-stamping would otherwise silently corrupt an unrelated P's
    // queue; panic loudly instead.
    _b.MovRegImm(VReg.Scratch3, (long)(uint)MspanOwningPSentinel);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch3);
    var notSentinel = UniqueLabel("slab_free_owner_ok");
    _b.JumpIf(Condition.NotEqual, notSentinel);
    _b.LeaSymdata(VReg.Arg0, "__slab_panic_sentinel_owning_p");
    _b.Call("mrt_panic");
    _b.DefineLabel(notSentinel);
    _b.Jump(remotePath);

    // --- Local free path: no locking, no atomics ---
    // Single-writer invariant on span->free_list (see __slab_alloc): the
    // owning P is the ONLY mutator across all of (alloc-fast pop, drain push,
    // local-free push, initial setup). Cross-P frees never touch this list.
    // mcentral_return_span (invoked by the helper when free_count reaches
    // total_slots) takes the pool lock internally — still locked-as-before;
    // only the per-span push above is lockless.
    _b.DefineLabel(localPath);
    _b.LoadLocal(VReg.Scratch1, 1); // span_ptr
    _b.LoadLocal(VReg.Scratch0, 0); // slot_base
    EmitPushSlotOntoSpanFreeList(spanReg: VReg.Scratch1, slotReg: VReg.Scratch0,
                                 t0: VReg.Scratch2, t1: VReg.Scratch3);
    EmitSlabGlobalLockRelease();
    _b.FunctionEnd();

    // --- Remote free path: CAS-push onto target P's remote_free_head ---
    // Treiber-stack push, with the new node's "next" pointer reused as the
    // free-list link slot[0]. The CAS retry loop reloads `old` each iteration
    // because x86's AtomicCAS clobbers Scratch0 (= RAX) — keeping the loop
    // identical across backends rather than micro-optimising for ARM64.
    //
    // No trace emission: see EmitSlabDrainRemoteFrees for the rationale
    // (cross-P routing is non-deterministic and would break spec tests).
    _b.DefineLabel(remotePath);

    // This slot is being freed by a thread that is NOT the span's owning P (a
    // worker P freeing another P's object, or a raw OS thread with no P at all).
    // It is the cross-P free the ownership gate was built for — count it (stats-
    // gated) as direct proof the never-run remote-free MPSC path executed. Bumped
    // BEFORE the target-P lookup, which reloads owning_p/slot_base from stack
    // slots 4/0, so the Scratch0 clobber here is harmless.
    EmitStatsGatedAtomicInc(SlabRemoteFreeCountLabel);

    // target_P = __sched_procs[owning_p]
    _b.LoadGlobal(VReg.Scratch2, "__sched_procs");
    _b.LoadLocal(VReg.Scratch1, 4); // owning_p
    _b.ShlRegImm(VReg.Scratch1, 3); // *8 (pointer slot)
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch2, 0); // target_P*
    _b.StoreLocal(4, VReg.Scratch2); // park target_P across the CAS retry loop

    var remoteRetry = UniqueLabel("slab_free_remote_retry");
    _b.DefineLabel(remoteRetry);

    // old = target_P->remote_free_head
    _b.LoadLocal(VReg.Scratch2, 4); // target_P
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch2, POffRemoteFreeHead); // expected = old (in Scratch0/RAX so CAS can clobber it)
    // slot[0] = old
    _b.LoadLocal(VReg.Scratch1, 0); // slot_base = desired
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0);
    // CAS(target_P->remote_free_head, expected=Scratch0, desired=Scratch1). Result in Scratch3.
    _b.AtomicCAS(VReg.Scratch2, POffRemoteFreeHead, VReg.Scratch0, VReg.Scratch1);
    _b.CmpRegImm(VReg.Scratch3, 0);
    _b.JumpIf(Condition.Equal, remoteRetry);

    EmitSlabGlobalLockRelease();
    _b.FunctionEnd();

    // --- Not a slab span: try OS-direct ---
    _b.DefineLabel(notSlabSpan);

    _b.LoadLocal(VReg.Arg0, 0); // slot_base
    _b.Call("__slab_os_direct_remove");
    _b.StoreLocal(2, VReg.Scratch0); // size (0 if not found)

    var notOsDirect = UniqueLabel("slab_free_not_os_direct");
    _b.JumpIfZero(VReg.Scratch0, notOsDirect);

    // OS-direct free
    if (mmTrace) {
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(3, VReg.Scratch0);
      EmitInlineTraceSlabFree(UniqueLabel("sl_free_os_direct_trace"), sizeSlot: 2, classSlot: 3);
      EmitTraceDepthInc();
      _b.LoadLocal(VReg.Scratch0, 2);
      EmitInlineTraceOsFree(UniqueLabel("os_free_trace"), VReg.Scratch0);
      EmitTraceDepthDec();
    }

    _b.LoadLocal(VReg.Arg0, 0); // slot_base
    _b.LoadLocal(VReg.Arg1, 2); // size
    _b.OsFreePages(VReg.Arg0, VReg.Arg1);
    EmitSlabGlobalLockRelease();
    _b.FunctionEnd();

    // --- Not found anywhere: no-op ---
    _b.DefineLabel(notOsDirect);
    EmitSlabGlobalLockRelease();
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitAllocatorFunctions: Emit all allocator functions.
  // =========================================================================
  public void EmitAllocatorFunctions(bool mmTrace) {
    EmitAllocatorGlobals();
    EmitSlabMemzero();
    EmitOsAllocPages(mmTrace);
    EmitArenaMapEnsure();
    EmitMetaAlloc();
    EmitMetaFree();
    EmitArenaAllocChunks(mmTrace);
    EmitArenaFreeChunks();
    EmitAllocatorInit(mmTrace);
    EmitSpanRegister();
    EmitSpanUnregister();
    EmitSpanLookup();
    EmitOsDirectInsert();
    EmitOsDirectRemove();
    EmitRawAllocIdInsert();
    EmitRawAllocIdLookup();
    EmitMspanAlloc();
    EmitMcentralGetSpan();
    EmitMcentralReturnSpan();
    EmitSlabDrainRemoteFrees();
    EmitSlabAlloc(mmTrace);
    EmitSlabAllocRaw(mmTrace);
    EmitSlabFree(mmTrace);
  }
}
