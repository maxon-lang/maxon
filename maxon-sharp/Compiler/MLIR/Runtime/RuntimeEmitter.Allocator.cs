namespace MaxonSharp.Compiler.Mlir.Runtime;

/// <summary>
/// Go-inspired three-tier slab allocator for the Maxon runtime.
///
/// Architecture:
///   Per-P mcache  (lock-free fast path: one cached mspan per size class per P)
///        |  refill
///     mcentral    (one per size class, locked)
///        |  grow
///      arena      (64MB bump allocator, requests arenas from OS using large pages when available)
///
/// Size classes 0-17 cover allocations from 8 to 32768 bytes.
/// Allocations larger than 32768 bytes go directly to OsAllocPages.
///
/// Every allocation (both slab and large) has a 16-byte header:
///   [user_ptr - 16]: span_ptr (small) or alloc_size (large)
///   [user_ptr -  8]: flags    (0 = small/slab, 1 = large/direct OS)
///
/// This header is compatible with the existing mm_raw_alloc 16-byte header
/// scheme: mm_raw_free checks flags to decide slab-free vs OS-free.
/// </summary>
public partial class RuntimeEmitter {

  // =========================================================================
  // Size class tables
  // =========================================================================
  // 18 size classes. For each: the slot size and objects-per-span.

  public static readonly int[] SlabClassSizes = [
    8, 16, 24, 32, 48, 64, 96, 128,
    192, 256, 384, 512, 1024, 2048, 4096,
    8192, 16384, 32768
  ];

  private static readonly int[] SlabObjsPerSpan = [
    512, 256, 170, 128, 85, 64, 42, 32,
    21, 16, 10, 8, 4, 2, 1,
    4, 2, 1
  ];

  private const int SlabNumClasses = 18;
  // Stack slot used by EmitInlineTraceOsAlloc/OsFree to spill the size across calls.
  // Must be within the frame size of any caller that invokes these helpers.
  private const int OsTraceScratchSlot = 7;
  private const int SlabMaxSmallSize = 32768;
  private const int SlabHeaderSize = 16; // header stored before user pointer
  private const int ArenaSize = 64 * 1024 * 1024; // 64MB

  // mspan struct layout (48 bytes)
  private const int MspanOffBaseAddr = 0x00;
  private const int MspanOffSlotSize = 0x08;
  private const int MspanOffFreeList = 0x10;
  private const int MspanOffFreeCount = 0x18;
  private const int MspanOffNextSpan = 0x20;
  private const int MspanOffClassIndex = 0x28;
  private const int MspanOffTotalSlots = 0x30;
  private const int MspanStructSize = 0x38; // 56 bytes

  // mcentral struct layout
  // Layout per class entry (16 bytes):
  //   +0x00: partial_head (mspan*)
  //   +0x08: full_head    (mspan*)
  private const int McentralEntrySize = 16;

  // Global data labels
  private const string McentralArrayLabel = "__slab_mcentral_array";
  private const string MspanPoolLockLabel = "__slab_mspan_pool_lock";
  private const string McacheBaseLabel = "__slab_mcache_base";
  private const string SlabClassSizesLabel = "__slab_class_sizes";
  private const string SlabObjsLabel = "__slab_objs_per_span";
  private const string SlabInitDoneLabel = "__slab_init_done";
  private const string ArenaBaseLabel = "__slab_arena_ptr";
  private const string ArenaEndLabel = "__slab_arena_end";

  // Lock labels for mcentral (18 separate locks)
  private static string McentralLockLabel(int classIndex) =>
    $"__slab_mcentral_lock_{classIndex}";

  // =========================================================================
  // EmitAllocatorGlobals: Define all slab allocator global data.
  // Must be called before emitting allocator functions.
  // =========================================================================
  public void EmitAllocatorGlobals() {
    // mcentral array: 18 entries * 16 bytes = 288 bytes
    _b.DefineGlobal(McentralArrayLabel, SlabNumClasses * McentralEntrySize, 0);

    // Arena bump allocator globals
    _b.DefineGlobal(ArenaBaseLabel, 8, 0);
    _b.DefineGlobal(ArenaEndLabel, 8, 0);

    // mcache base pointer (allocated at init time: max_procs * 18 * 8 bytes)
    _b.DefineGlobal(McacheBaseLabel, 8, 0);

    // Init done flag
    _b.DefineGlobal(SlabInitDoneLabel, 8, 0);

    // Lock for arena allocation (reused for mcentral too)
    if (_b.IsWindows) {
      _b.DefineGlobal(MspanPoolLockLabel, 40, 0); // CRITICAL_SECTION
    } else {
      _b.DefineGlobal(MspanPoolLockLabel, 8, 0);  // os_unfair_lock (4 bytes, padded to 8)
    }

    // Per-class mcentral locks
    for (int i = 0; i < SlabNumClasses; i++) {
      if (_b.IsWindows) {
        _b.DefineGlobal(McentralLockLabel(i), 40, 0);
      } else {
        _b.DefineGlobal(McentralLockLabel(i), 8, 0);
      }
    }

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
  // EmitOsAllocPages: __slab_os_alloc(size) -> ptr
  //
  // Allocates `size` bytes from the OS with large-page preference.
  // Tries OsAllocLargePages first (2MB TLB entries, lower TLB pressure);
  // falls back to OsAllocPages if large pages are unavailable or unsupported.
  // All OS memory in the slab allocator flows through this single call site.
  // =========================================================================
  // Stack slots: 0=size, 1=ptr. OsTraceScratchSlot=7 requires frame >= 0x40.
  public void EmitOsAllocPages(bool mmTrace) {
    _b.FunctionStart("__slab_os_alloc", 1, 0x50);

    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.OsAllocLargePages(VReg.Scratch1, VReg.Scratch0); // NULL on failure or unsupported
    var gotPages = UniqueLabel("os_alloc_got");
    _b.JumpIfNonZero(VReg.Scratch1, gotPages);

    _b.LoadLocal(VReg.Scratch0, 0);
    _b.OsAllocPages(VReg.Scratch1, VReg.Scratch0);

    _b.DefineLabel(gotPages);
    _b.StoreLocal(1, VReg.Scratch1); // save ptr; slot 1 survives trace calls
    if (mmTrace) {
      _b.LoadLocal(VReg.Scratch0, 0); // size
      EmitInlineTraceOsAlloc(UniqueLabel("os_alloc_trace"), VReg.Scratch0);
    }
    _b.LoadLocal(VReg.Scratch0, 1); // ptr
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitArenaAlloc: __slab_arena_alloc(size) -> ptr
  //
  // Bump-allocates `size` bytes from the current 64MB arena.
  // If the arena is exhausted, calls __slab_os_alloc to get a new arena.
  // Thread-safe: acquires MspanPoolLockLabel around the bump.
  // =========================================================================
  // Stack slots: 0=size (from Arg0), 1=result, 2=saved_tag_ctx. Frame 0x30.
  public void EmitArenaAlloc(bool mmTrace) {
    _b.FunctionStart("__slab_arena_alloc", 1, 0x30);

    _b.LockAcquire(MspanPoolLockLabel);

    // Check if arena_ptr + size <= arena_end
    _b.LoadGlobal(VReg.Scratch0, ArenaBaseLabel);
    _b.LoadLocal(VReg.Scratch1, 0);                  // size
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);      // arena_ptr + size
    _b.LoadGlobal(VReg.Scratch2, ArenaEndLabel);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    var arenaOk = UniqueLabel("arena_ok");
    _b.JumpIf(Condition.BelowEqual, arenaOk);

    // Arena exhausted: allocate a new one via __slab_os_alloc
    // Clear tag context for arena OS alloc (arena is shared infrastructure, not tied to any object)
    if (mmTrace) {
      _b.LoadGlobal(VReg.Scratch0, "__mm_trace_tag_ctx");
      _b.StoreLocal(2, VReg.Scratch0); // save tag context
      _b.ZeroReg(VReg.Scratch0);
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch0); // clear for arena alloc
      EmitTraceDepthInc();
    }
    _b.MovRegImm(VReg.Arg0, ArenaSize);
    _b.Call("__slab_os_alloc"); // Scratch0 = new arena base
    _b.StoreLocal(1, VReg.Scratch0); // save arena base (slot 1) across trace calls
    if (mmTrace) {
      EmitTraceDepthDec();
      _b.LoadLocal(VReg.Scratch1, 2); // restore tag context
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch1);
    }
    _b.LoadLocal(VReg.Scratch0, 1); // reload arena base
    _b.StoreGlobal(ArenaBaseLabel, VReg.Scratch0);
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.AddRegImm(VReg.Scratch1, ArenaSize);
    _b.StoreGlobal(ArenaEndLabel, VReg.Scratch1);

    _b.DefineLabel(arenaOk);
    // result = current arena_ptr; advance arena_ptr by size
    _b.LoadGlobal(VReg.Scratch0, ArenaBaseLabel);
    _b.StoreLocal(1, VReg.Scratch0);
    _b.LoadLocal(VReg.Scratch1, 0); // size
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.StoreGlobal(ArenaBaseLabel, VReg.Scratch0);

    _b.LockRelease(MspanPoolLockLabel);

    _b.LoadLocal(VReg.Scratch0, 1);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitAllocatorInit: __slab_init()
  //
  // Called during scheduler init (after P structs are allocated).
  // 1. Allocate mcache array via arena, then memzero it
  // 2. Initialize locks (Windows only)
  // =========================================================================
  // Stack slots: 0=mcache_size, 1=mcache_ptr
  public void EmitAllocatorInit(bool mmTrace) {
    _b.FunctionStart("__slab_init", 0, 0x30);

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

    // Step 1: Allocate mcache array via arena
    // Size = max_procs * 18 * 8 = max_procs * 144
    _b.LoadGlobal(VReg.Scratch0, "__sched_max_procs");
    _b.MovRegImm(VReg.Scratch1, SlabNumClasses * 8); // 144
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.StoreLocal(0, VReg.Scratch0); // save mcache_size

    // Allocate via __slab_arena_alloc
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_alloc");
    // Scratch0 = arena alloc result
    _b.StoreGlobal(McacheBaseLabel, VReg.Scratch0);
    _b.StoreLocal(1, VReg.Scratch0); // save mcache_ptr

    // Memzero the mcache array (arena memory is not zero-initialized)
    _b.LoadLocal(VReg.Arg0, 1); // mcache_ptr
    _b.LoadLocal(VReg.Arg1, 0); // mcache_size
    _b.Call("__slab_memzero");

    // Step 2: Initialize locks (Windows only — os_unfair_lock is zero-init on macOS)
    if (_b.IsWindows) {
      // See comment in original: CRITICAL_SECTION init is done in platform-specific
      // SchedInit before calling __slab_init.
    }

    if (mmTrace) {
      EmitTraceDepthDec();
    }

    _b.DefineLabel(alreadyDone);
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitMspanAlloc: __slab_mspan_alloc(class_index) -> mspan*
  //
  // Allocates a new mspan for the given size class:
  // 1. Allocate an mspan header from the arena
  // 2. Allocate span data (slot_size * num_objs) from the arena
  // 3. Build the intrusive free list through all slots
  // 4. Return the mspan pointer
  // =========================================================================
  // Stack slots: 0=class_index, 1=mspan_ptr, 2=page_base, 3=slot_size,
  //              4=num_objs, 5=loop_counter
  public void EmitMspanAlloc() {
    _b.FunctionStart("__slab_mspan_alloc", 1, 0x50);

    // --- Allocate mspan header from arena ---
    _b.MovRegImm(VReg.Arg0, MspanStructSize);
    _b.Call("__slab_arena_alloc");
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

    // --- Allocate span data from arena: slot_size * num_objs ---
    _b.LoadLocal(VReg.Scratch0, 3); // slot_size
    _b.LoadLocal(VReg.Scratch1, 4); // num_objs
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1); // data_size = slot_size * num_objs
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_alloc");
    _b.StoreLocal(2, VReg.Scratch0); // slot 2 = page_base

    // --- Initialize mspan fields ---
    _b.LoadLocal(VReg.Scratch0, 1); // mspan_ptr
    _b.LoadLocal(VReg.Scratch1, 2); // page_base
    _b.StoreIndirect(VReg.Scratch0, MspanOffBaseAddr, VReg.Scratch1);

    _b.LoadLocal(VReg.Scratch1, 3); // slot_size
    _b.StoreIndirect(VReg.Scratch0, MspanOffSlotSize, VReg.Scratch1);

    _b.LoadLocal(VReg.Scratch1, 4); // num_objs = free_count initially
    _b.StoreIndirect(VReg.Scratch0, MspanOffFreeCount, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffTotalSlots, VReg.Scratch1); // total_slots = num_objs

    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffNextSpan, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MspanOffFreeList, VReg.Scratch1); // will be set below

    _b.LoadLocal(VReg.Scratch1, 0); // class_index
    _b.StoreIndirect(VReg.Scratch0, MspanOffClassIndex, VReg.Scratch1);

    // --- Build intrusive free list ---
    // For each slot i (0..num_objs-1):
    //   addr = page_base + i * slot_size
    //   [addr] = page_base + (i+1) * slot_size   (next pointer)
    // Last slot: [addr] = 0 (end of list)
    // free_list = page_base (first slot)

    // Set free_list = page_base
    _b.LoadLocal(VReg.Scratch0, 1); // mspan_ptr
    _b.LoadLocal(VReg.Scratch1, 2); // page_base
    _b.StoreIndirect(VReg.Scratch0, MspanOffFreeList, VReg.Scratch1);

    // Loop: i = 0
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(5, VReg.Scratch0); // slot 5 = i

    var loopStart = UniqueLabel("mspan_build_freelist");
    var loopDone = UniqueLabel("mspan_build_done");
    var lastSlot = UniqueLabel("mspan_last_slot");

    _b.DefineLabel(loopStart);
    _b.LoadLocal(VReg.Scratch0, 5); // i
    _b.LoadLocal(VReg.Scratch1, 4); // num_objs
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, loopDone);

    // addr = page_base + i * slot_size
    _b.LoadLocal(VReg.Scratch0, 5); // i
    _b.LoadLocal(VReg.Scratch1, 3); // slot_size
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1); // i * slot_size
    _b.LoadLocal(VReg.Scratch1, 2); // page_base
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // Scratch0 = addr

    // Check if this is the last slot
    _b.LoadLocal(VReg.Scratch1, 5); // i
    _b.AddRegImm(VReg.Scratch1, 1); // i + 1
    _b.LoadLocal(VReg.Scratch2, 4); // num_objs
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.AboveEqual, lastSlot);

    // Not last: [addr] = page_base + (i+1) * slot_size
    _b.LoadLocal(VReg.Scratch1, 5); // i
    _b.AddRegImm(VReg.Scratch1, 1); // i + 1
    _b.LoadLocal(VReg.Scratch2, 3); // slot_size
    _b.MulRegReg(VReg.Scratch1, VReg.Scratch2); // (i+1) * slot_size
    _b.LoadLocal(VReg.Scratch2, 2); // page_base
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2); // next_addr
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // [addr] = next_addr
    var afterStore = UniqueLabel("mspan_after_store");
    _b.Jump(afterStore);

    _b.DefineLabel(lastSlot);
    // Last slot: [addr] = 0
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);

    _b.DefineLabel(afterStore);
    // i++
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(5, VReg.Scratch0);
    _b.Jump(loopStart);

    _b.DefineLabel(loopDone);

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
    // Ret (Scratch0) = new span
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
    _b.LockRelease(MspanPoolLockLabel);

    // Return span
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitMcentralReturnSpan: __slab_mcentral_return_span(span_ptr)
  //
  // Returns a fully-free span back to its class's mcentral partial list so other
  // Ps can reuse it. Also evicts any stale mcache pointer to this span across all
  // Ps — a P that last used this span may still have it cached even though
  // free_count just hit total_slots, and if another P dequeues the span before
  // the stale P allocates from it, we'd have two Ps manipulating the same free list
  // without a lock.
  // =========================================================================
  // Stack slots: 0=span_ptr, 1=class_offset (class_index*8), 2=loop_i, 3=mcache_base
  public void EmitMcentralReturnSpan() {
    _b.FunctionStart("__slab_mcentral_return_span", 1, 0x60);

    _b.LockAcquire(MspanPoolLockLabel);

    // Get class_index from span; compute class_offset = class_index * 8
    _b.LoadLocal(VReg.Scratch0, 0); // span_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffClassIndex); // class_index
    _b.ShlRegImm(VReg.Scratch1, 3); // class_offset = class_index * 8
    _b.StoreLocal(1, VReg.Scratch1); // slot 1 = class_offset

    // Compute mcentral entry address: mcentral_array + class_index * 16
    // class_index = class_offset / 8, so class_index * 16 = class_offset * 2
    _b.LeaGlobal(VReg.Scratch2, McentralArrayLabel);
    _b.ShlRegImm(VReg.Scratch1, 1); // class_offset * 2 = class_index * 16
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1); // Scratch2 = mcentral_addr

    // Prepend span to partial_head: span->next = old_partial_head
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch2, 0); // old partial_head
    _b.LoadLocal(VReg.Scratch0, 0); // span_ptr
    _b.StoreIndirect(VReg.Scratch0, MspanOffNextSpan, VReg.Scratch1); // span->next = old_head
    _b.StoreIndirect(VReg.Scratch2, 0, VReg.Scratch0); // partial_head = span

    // Evict stale mcache pointers: iterate all Ps and null any slot that still
    // points to this span. Held under MspanPoolLockLabel, which __slab_alloc also
    // acquires before installing a new span into an mcache slot (via
    // __slab_mcentral_get_span), so the eviction and installation are serialised.
    _b.LoadGlobal(VReg.Scratch0, McacheBaseLabel);
    _b.StoreLocal(3, VReg.Scratch0); // slot 3 = mcache_base

    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(2, VReg.Scratch0); // slot 2 = i = 0

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
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1); // i * 144
    _b.LoadLocal(VReg.Scratch1, 3); // mcache_base
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // mcache_base + i*144
    _b.LoadLocal(VReg.Scratch1, 1); // class_offset
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // mcache_slot_addr

    // If *mcache_slot == span_ptr, clear it
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // *mcache_slot
    _b.LoadLocal(VReg.Scratch2, 0); // span_ptr
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.NotEqual, evictNext);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // *mcache_slot = NULL

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

  // Header flags stored at [user_ptr - 8]:
  //   0 = small slab object (has span_ptr at [user_ptr-16]; returns to free list on free)
  //   1 = arena-backed large object (bump-allocated; no individual free)
  //   2 = OS-direct huge object (>ArenaSize; calls OsFreePages on free)
  private const int FlagArenaLarge = 1;
  private const int FlagOsDirect = 2;

  /// <summary>
  /// Bump-allocates a large object from the arena (32753–67108848 bytes).
  /// Delegates the actual allocation to __slab_arena_alloc, then writes the
  /// 16-byte header ([base+0]=total_size, [base+8]=FlagArenaLarge) and
  /// advances resultSlot to the user pointer (base + SlabHeaderSize).
  /// </summary>
  private void EmitArenaLargeObjectAlloc(int sizeSlot, int classSlot, int resultSlot, bool mmTrace) {
    // Emit sl_alloc trace BEFORE the arena call (top-down order)
    if (mmTrace) {
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(classSlot, VReg.Scratch0);
      EmitInlineTraceSlabAlloc(UniqueLabel("sl_alloc_arena_large_trace"), sizeSlot, classSlot);
      EmitTraceDepthInc();
    }
    _b.LoadLocal(VReg.Scratch0, sizeSlot);
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize); // total = size + 16
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_alloc"); // Ret/Scratch0 = base
    _b.StoreLocal(resultSlot, VReg.Scratch0);
    if (mmTrace) {
      EmitTraceDepthDec();
    }
    // Write header (no trace — already emitted above)
    EmitWriteLargeObjectHeader(sizeSlot, resultSlot, FlagArenaLarge);
  }

  /// <summary>
  /// Allocates a huge object directly from the OS (>67108848 bytes).
  /// Falls outside the arena entirely: each allocation calls __slab_os_alloc
  /// and each free calls VirtualFree/munmap.
  /// </summary>
  private void EmitOsDirectObjectAlloc(int sizeSlot, int classSlot, int resultSlot, bool mmTrace) {
    // Emit sl_alloc trace BEFORE the OS call (top-down order: sl → os)
    if (mmTrace) {
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(classSlot, VReg.Scratch0);
      EmitInlineTraceSlabAlloc(UniqueLabel("sl_alloc_os_direct_trace"), sizeSlot, classSlot);
      EmitTraceDepthInc();
    }
    _b.LoadLocal(VReg.Scratch0, sizeSlot);
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize); // total = size + 16
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_os_alloc"); // Scratch0 = base; os_alloc trace emitted inside
    _b.StoreLocal(resultSlot, VReg.Scratch0);
    if (mmTrace) {
      EmitTraceDepthDec();
    }
    // Write header (no trace — already emitted above)
    EmitWriteLargeObjectHeader(sizeSlot, resultSlot, FlagOsDirect);
  }

  /// <summary>
  /// Writes the 16-byte slab header into the base pointer stored in resultSlot,
  /// then advances resultSlot to the user pointer (base + SlabHeaderSize).
  /// Called after the base pointer has been saved to resultSlot by both large-object paths.
  /// Emits "slab_alloc size=N class=-1" trace if mmTrace is true.
  /// </summary>
  private void EmitWriteLargeObjectHeader(int sizeSlot, int resultSlot, int flags) {
    // Reload base — trace calls in the caller may have clobbered registers
    _b.LoadLocal(VReg.Scratch1, resultSlot); // base
    _b.LoadLocal(VReg.Scratch0, sizeSlot);
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize); // total = size + 16
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0); // [base+0] = total_size
    _b.MovRegImm(VReg.Scratch0, flags);
    _b.StoreIndirect(VReg.Scratch1, 8, VReg.Scratch0); // [base+8] = flags
    _b.AddRegImm(VReg.Scratch1, SlabHeaderSize);
    _b.StoreLocal(resultSlot, VReg.Scratch1); // resultSlot = user_ptr
  }

  /// <summary>
  /// If __mm_trace_tag_ctx != 0, prints " TypeName #N".
  /// Used by slab/OS trace helpers to show which managed allocation triggered the operation.
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
    _b.Call("mm_trace_print_class"); // prints -1 for arena-large / OS-direct paths
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  /// <summary>Emit: indent + "os_alloc [TypeName #N] size=N\n"</summary>
  private void EmitInlineTraceOsAlloc(string uniquePrefix, VReg sizeReg) {
    _b.MovRegReg(VReg.Arg0, sizeReg);
    _b.StoreLocal(OsTraceScratchSlot, VReg.Arg0); // spill across calls
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
    _b.Call("mm_trace_print_class"); // prints -1 for arena-large / OS-direct paths
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  // =========================================================================
  // EmitSlabAlloc: __slab_alloc(size) -> user_ptr
  //
  // Allocation routing:
  // 1. size > ArenaSize - SlabHeaderSize: OS-direct (flags=2, OsFreePages on free)
  // 2. size > SlabMaxSmallSize - SlabHeaderSize: arena-backed large (flags=1, no per-object free)
  // 3. else: slab fast path (flags=0, returns to span free list on free)
  //
  // Returns a user_ptr with a 16-byte header:
  //   [user_ptr - 16] = span_ptr (slab) or total_size (large/OS-direct)
  //   [user_ptr -  8] = 0 (slab), 1 (arena-large), 2 (OS-direct)
  // =========================================================================
  // Stack slots: 0=size, 1=class_index, 2=P_id, 3=mcache_slot_addr,
  //              4=span_ptr, 5=alloc_result
  public void EmitSlabAlloc(bool mmTrace) {
    _b.FunctionStart("__slab_alloc", 1, 0x60);

    // Check if allocator is initialized
    _b.LoadGlobal(VReg.Scratch0, SlabInitDoneLabel);
    var slabReady = UniqueLabel("slab_alloc_ready");
    _b.JumpIfNonZero(VReg.Scratch0, slabReady);

    // Fallback: allocator not initialized — use OS-direct path.
    // The arena lock (CRITICAL_SECTION on Windows) is not initialized yet,
    // so we cannot call __slab_arena_alloc safely.
    EmitOsDirectObjectAlloc(sizeSlot: 0, classSlot: 1, resultSlot: 5, mmTrace);
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(slabReady);

    // --- OS-direct check: size > ArenaSize - SlabHeaderSize ---
    // Allocations larger than the arena itself must bypass the arena entirely.
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.CmpRegImm(VReg.Scratch0, ArenaSize - SlabHeaderSize);
    var notOsDirect = UniqueLabel("slab_alloc_not_os_direct");
    _b.JumpIf(Condition.BelowEqual, notOsDirect);

    EmitOsDirectObjectAlloc(sizeSlot: 0, classSlot: 1, resultSlot: 5, mmTrace);
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(notOsDirect);

    // --- Arena-large check ---
    // Must account for the 16-byte header: effective_size = size + 16
    // Route to arena-large path if effective_size > max class size (32768),
    // i.e., size > 32768 - 16 = 32752
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.CmpRegImm(VReg.Scratch0, SlabMaxSmallSize - SlabHeaderSize);
    var smallPath = UniqueLabel("slab_alloc_small");
    _b.JumpIf(Condition.BelowEqual, smallPath);

    // Arena-large path: bump-allocate from arena, no per-object free
    EmitArenaLargeObjectAlloc(sizeSlot: 0, classSlot: 1, resultSlot: 5, mmTrace);
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    // --- Small object path ---
    _b.DefineLabel(smallPath);

    // Look up size class: linear scan of class_sizes table
    // Find first class where class_sizes[i] >= (size + SlabHeaderSize)
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize); // effective_size = size + 16

    // Linear scan: class_index = 0
    _b.ZeroReg(VReg.Scratch1); // class_index = 0
    _b.LeaSymdata(VReg.Scratch2, SlabClassSizesLabel);

    var classLoop = UniqueLabel("slab_class_loop");
    var classFound = UniqueLabel("slab_class_found");

    _b.DefineLabel(classLoop);
    // Load class_sizes[class_index]
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch1); // class_index
    _b.ShlRegImm(VReg.Scratch3, 3); // * 8
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch2); // &class_sizes[i]
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch3, 0); // class_sizes[i]
    _b.CmpRegReg(VReg.Scratch3, VReg.Scratch0); // class_sizes[i] >= effective_size?
    _b.JumpIf(Condition.AboveEqual, classFound);
    _b.AddRegImm(VReg.Scratch1, 1); // class_index++
    _b.Jump(classLoop);

    _b.DefineLabel(classFound);
    _b.StoreLocal(1, VReg.Scratch1); // slot 1 = class_index

    // --- Load mcache slot ---
    // mcache_slot_addr = mcache_base + P->id * 144 + class_index * 8
    _b.LoadCurrentP(VReg.Scratch0); // Scratch0 = P*
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, POffId); // P->id
    _b.StoreLocal(2, VReg.Scratch0); // save P_id

    _b.MovRegImm(VReg.Scratch1, SlabNumClasses * 8); // 144
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1); // P_id * 144
    _b.LoadGlobal(VReg.Scratch1, McacheBaseLabel);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // mcache_base + P_id * 144
    _b.LoadLocal(VReg.Scratch1, 1); // class_index
    _b.ShlRegImm(VReg.Scratch1, 3); // class_index * 8
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // mcache_slot_addr
    _b.StoreLocal(3, VReg.Scratch0); // slot 3 = mcache_slot_addr

    // Load cached span pointer
    var retryLabel = UniqueLabel("slab_alloc_retry");
    _b.DefineLabel(retryLabel);

    _b.LoadLocal(VReg.Scratch0, 3); // mcache_slot_addr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // span = *mcache_slot_addr
    var needRefill = UniqueLabel("slab_need_refill");
    _b.JumpIfZero(VReg.Scratch1, needRefill);

    // Check if span has free slots
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffFreeCount);
    _b.JumpIfZero(VReg.Scratch2, needRefill);

    // --- Fast path: pop from free list ---
    _b.StoreLocal(4, VReg.Scratch1); // save span_ptr

    // result = span->free_list
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, MspanOffFreeList); // result = free_list head
    _b.StoreLocal(5, VReg.Scratch0); // save alloc_result

    // span->free_list = [result] (next pointer at result[0])
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 0); // next = [result]
    _b.LoadLocal(VReg.Scratch1, 4); // span_ptr
    _b.StoreIndirect(VReg.Scratch1, MspanOffFreeList, VReg.Scratch2);

    // span->free_count--
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffFreeCount);
    _b.SubRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, MspanOffFreeCount, VReg.Scratch2);

    // Write header: [result + 0] = span_ptr, [result + 8] = 0 (small)
    _b.LoadLocal(VReg.Scratch0, 5); // result (slot base)
    _b.LoadLocal(VReg.Scratch1, 4); // span_ptr
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // [result] = span_ptr
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1); // [result+8] = 0

    // Compute user_ptr and user_size, then zero the user data area.
    // Reused slab slots may contain stale data; callers expect zeroed memory.
    _b.LoadLocal(VReg.Scratch0, 5); // slot base
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize); // user_ptr = slot_base + 16
    _b.StoreLocal(5, VReg.Scratch0); // reuse slot 5 = user_ptr

    // Slot is pre-zeroed by __slab_free — no memzero needed on alloc.
    if (mmTrace) {
      EmitInlineTraceSlabAlloc(UniqueLabel("sl_alloc_small_trace"), sizeSlot: 0, classSlot: 1);
    }

    // Return user_ptr (reload from slot 5 since trace calls may clobber regs)
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    // --- Refill path ---
    _b.DefineLabel(needRefill);

    // The current cached span (if any) is exhausted (free_count == 0).
    // Don't return it to mcentral's partial list — it has no free slots,
    // so putting it in the partial list would cause infinite loops when
    // it gets immediately re-taken but still has no free slots.
    // Instead, just clear the mcache slot. The exhausted span remains
    // accessible via the span_ptr stored in each allocated slot's header,
    // so freed slots will still increment the span's free_count.
    _b.LoadLocal(VReg.Scratch0, 3); // mcache_slot_addr
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // *mcache_slot = NULL

    // Get a new span from mcentral
    _b.LoadLocal(VReg.Arg0, 1); // class_index
    _b.Call("__slab_mcentral_get_span");
    // Ret (Scratch0) = new span
    _b.StoreLocal(4, VReg.Scratch0); // save new span

    // Store in mcache slot
    _b.LoadLocal(VReg.Scratch1, 3); // mcache_slot_addr
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0); // *mcache_slot = new_span

    // Retry allocation from the new span
    _b.Jump(retryLabel);
  }

  // =========================================================================
  // EmitSlabFree: __slab_free(user_ptr)
  //
  // Free a slab-allocated object based on flags at [ptr-8]:
  //   0 (slab):        zero user data, return slot to span free list
  //   1 (arena-large): no-op — arena memory is not individually freed
  //   2 (OS-direct):   OsFreePages — object was too large for the arena
  // =========================================================================
  // Stack slots: 0=user_ptr, 1=span_ptr, 2=slot_size
  public void EmitSlabFree(bool mmTrace) {
    _b.FunctionStart("__slab_free", 1, 0x40);

    // NULL check
    _b.LoadLocal(VReg.Scratch0, 0);
    var notNull = UniqueLabel("slab_free_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.FunctionEnd();

    _b.DefineLabel(notNull);

    // Read flags at [ptr - 8] — low byte is flags, upper bytes may contain raw_alloc_id
    _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -8); // flags (possibly with raw_id in upper bytes)
    _b.MovRegImm(VReg.Scratch2, 0xFF);
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2); // mask to low byte

    var arenaLargeFree = UniqueLabel("slab_free_arena_large");
    var osDirect = UniqueLabel("slab_free_os_direct");

    // flags == 0: slab path (fall through)
    // flags == 1: arena-large (no-op)
    // flags == 2: OS-direct
    _b.CmpRegImm(VReg.Scratch1, FlagArenaLarge);
    _b.JumpIf(Condition.Equal, arenaLargeFree);
    _b.CmpRegImm(VReg.Scratch1, FlagOsDirect);
    _b.JumpIf(Condition.Equal, osDirect);

    // --- Slab object free (flags == 0) ---
    // Read span_ptr from [ptr - 16]
    _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // span_ptr
    _b.StoreLocal(1, VReg.Scratch1); // save span_ptr

    // Load slot_size from span
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffSlotSize); // slot_size
    _b.StoreLocal(2, VReg.Scratch2); // save slot_size

    if (mmTrace) {
      // Compute user_size = slot_size - SlabHeaderSize for trace
      _b.SubRegImm(VReg.Scratch2, SlabHeaderSize); // user_size
      _b.StoreLocal(2, VReg.Scratch2);
      // class_index is in span->class_index
      _b.LoadLocal(VReg.Scratch0, 1); // span_ptr
      _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, MspanOffClassIndex); // class_index
      _b.StoreLocal(1, VReg.Scratch0); // reuse slot 1 = class_index for trace
      EmitInlineTraceSlabFree(UniqueLabel("sl_free_slab_trace"), sizeSlot: 2, classSlot: 1);
      // Reload span_ptr (clobbered by trace calls) from user_ptr header
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // span_ptr
      _b.StoreLocal(1, VReg.Scratch1);
      // Reload slot_size from span
      _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffSlotSize);
      _b.StoreLocal(2, VReg.Scratch2);
    }

    // Zero user data: __slab_memzero(user_ptr, slot_size - SlabHeaderSize)
    // Catches use-after-free via null-dereference; pre-zeros for next alloc.
    _b.LoadLocal(VReg.Scratch0, 2); // slot_size
    _b.SubRegImm(VReg.Scratch0, SlabHeaderSize); // user_size = slot_size - 16
    _b.LoadLocal(VReg.Arg0, 0); // user_ptr
    _b.MovRegReg(VReg.Arg1, VReg.Scratch0); // user_size
    _b.Call("__slab_memzero");

    // Reload span_ptr (clobbered by Call)
    _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // span_ptr

    // slot_base = user_ptr - 16 (the header is at the start of the slot)
    _b.SubRegImm(VReg.Scratch0, SlabHeaderSize); // Scratch0 = slot_base

    // Push slot_base onto span's free list:
    //   [slot_base] = span->free_list (old head becomes next pointer)
    //   span->free_list = slot_base
    //   span->free_count++
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffFreeList); // old free_list head
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2); // [slot_base] = old_head (next ptr)
    _b.StoreIndirect(VReg.Scratch1, MspanOffFreeList, VReg.Scratch0); // free_list = slot_base

    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffFreeCount);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, MspanOffFreeCount, VReg.Scratch2);

    // If span is now fully free (free_count == total_slots), return it to mcentral
    // so other Ps can reuse it instead of allocating fresh spans from the arena.
    // __slab_mcentral_return_span evicts any stale mcache pointers across all Ps.
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, MspanOffTotalSlots);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch0); // free_count == total_slots?
    var notFullyFree = UniqueLabel("slab_free_not_fully_free");
    _b.JumpIf(Condition.NotEqual, notFullyFree);

    _b.LoadLocal(VReg.Arg0, 1); // span_ptr
    _b.Call("__slab_mcentral_return_span");

    _b.DefineLabel(notFullyFree);
    _b.FunctionEnd();

    // --- Arena-large free (flags == 1): no-op ---
    // Arena memory is bump-allocated and not individually freed.
    // The arena itself is held for the lifetime of the process.
    _b.DefineLabel(arenaLargeFree);
    if (mmTrace) {
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // total_size
      _b.SubRegImm(VReg.Scratch1, SlabHeaderSize); // user_size
      _b.StoreLocal(1, VReg.Scratch1);
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(2, VReg.Scratch0); // class_index = -1
      EmitInlineTraceSlabFree(UniqueLabel("sl_free_arena_large_trace"), sizeSlot: 1, classSlot: 2);
    }
    _b.FunctionEnd();

    // --- OS-direct free (flags == 2) ---
    _b.DefineLabel(osDirect);

    if (mmTrace) {
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // total_size
      _b.SubRegImm(VReg.Scratch1, SlabHeaderSize); // user_size
      _b.StoreLocal(1, VReg.Scratch1);
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(2, VReg.Scratch0); // class_index = -1
      EmitInlineTraceSlabFree(UniqueLabel("sl_free_os_direct_trace"), sizeSlot: 1, classSlot: 2);
    }

    // Read total_size from [ptr - 16]
    _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // total_size

    // base = user_ptr - 16
    _b.SubRegImm(VReg.Scratch0, SlabHeaderSize);

    if (mmTrace) {
      EmitTraceDepthInc();
      EmitInlineTraceOsFree(UniqueLabel("os_free_trace"), VReg.Scratch1); // Scratch1 = total_size
      EmitTraceDepthDec();
    }
    // Reload base and size after trace calls clobber registers
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // total_size
    _b.SubRegImm(VReg.Scratch0, SlabHeaderSize);        // base

    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.MovRegReg(VReg.Arg1, VReg.Scratch1);
    _b.OsFreePages(VReg.Arg0, VReg.Arg1);

    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitSlabMemzero: __slab_memzero(ptr, size)
  //
  // Zeroes `size` bytes starting at `ptr`. Size must be a multiple of 8.
  // Used to zero-initialize slab slots before returning to the caller,
  // since reused slab slots may contain stale data from previous allocations.
  // =========================================================================
  // Stack slots: 0=ptr, 1=size
  public void EmitSlabMemzero() {
    _b.FunctionStart("__slab_memzero", 2, 0x20);

    // Loop: zero 8 bytes at a time
    var loopStart = UniqueLabel("slab_memzero_loop");
    var loopDone = UniqueLabel("slab_memzero_done");

    _b.DefineLabel(loopStart);
    _b.LoadLocal(VReg.Scratch0, 1); // remaining size
    _b.JumpIfZero(VReg.Scratch0, loopDone);

    // [ptr] = 0
    _b.LoadLocal(VReg.Scratch0, 0); // ptr
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);

    // ptr += 8
    _b.AddRegImm(VReg.Scratch0, 8);
    _b.StoreLocal(0, VReg.Scratch0);

    // size -= 8
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.SubRegImm(VReg.Scratch0, 8);
    _b.StoreLocal(1, VReg.Scratch0);

    _b.Jump(loopStart);
    _b.DefineLabel(loopDone);
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitAllocatorFunctions: Emit all allocator functions and wire into
  // mm_raw_alloc / mm_raw_free.
  // =========================================================================
  public void EmitAllocatorFunctions(bool mmTrace) {
    EmitAllocatorGlobals();
    EmitSlabMemzero();
    EmitOsAllocPages(mmTrace);
    EmitArenaAlloc(mmTrace);
    EmitAllocatorInit(mmTrace);
    EmitMspanAlloc();
    EmitMcentralGetSpan();
    EmitMcentralReturnSpan();
    EmitSlabAlloc(mmTrace);
    EmitSlabFree(mmTrace);
  }
}
