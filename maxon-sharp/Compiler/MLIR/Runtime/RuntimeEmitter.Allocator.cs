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
/// Header-free design: there is no per-object header. Instead, a two-level
/// arena map (L1 -> L2 -> spans[]) maps any pointer back to its owning mspan.
/// __slab_free always receives slot_base directly (mm_free subtracts MmHeaderSize,
/// mm_raw_free passes the slab_user_ptr which IS slot_base), so no division is needed.
///
/// OS-direct allocations (larger than the arena) are tracked via a separate
/// linked list (OsDirectListLabel) so they can be freed without the arena map.
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
    1024, 512, 341, 256, 170, 128, 85, 64,
    42, 32, 21, 16, 8, 4, 2,
    4, 2, 1
  ];

  private const int SlabNumClasses = 18;
  // Stack slot used by EmitInlineTraceOsAlloc/OsFree to spill the size across calls.
  // Must be within the frame size of any caller that invokes these helpers.
  private const int OsTraceScratchSlot = 7;
  private const int SlabMaxSmallSize = 32768;
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

  // Arena map: two-level radix tree for pointer -> span lookup
  // L1: 256 entries, each covering 512GB (ptr >> 39). L1 array = 2KB.
  // L2: 8192 entries per L1 slot, each pointing to a spans[] array. L2 = 64KB (lazy).
  // spans[]: 8192 entries per 64MB arena (one per 8KB page). spans = 64KB (lazy).
  private const int ArenaMapL1Size = 256;
  private const int ArenaMapL2Size = 8192;
  private const int ArenaPageShift = 13;       // 8KB pages
  private const int ArenaPagesPerArena = ArenaSize >> ArenaPageShift; // 8192
  private const int ArenaMapL1Shift = 39;
  private const int ArenaMapL2Shift = 26;
  private const int ArenaMapL2Mask = ArenaMapL2Size - 1; // 0x1FFF
  private const int ArenaMapPageMask = ArenaPagesPerArena - 1; // 0x1FFF

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

  // Arena map globals
  private const string ArenaMapL1Label = "__slab_arena_map_l1";
  // OS-direct allocation list (for header-free free path)
  private const string OsDirectListLabel = "__slab_os_direct_head";
  // Raw alloc ID tracking list (trace only)
  private const string RawAllocIdListLabel = "__mm_raw_alloc_id_list";

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

    // Arena map L1 base pointer
    _b.DefineGlobal(ArenaMapL1Label, 8, 0);

    // OS-direct linked list head
    _b.DefineGlobal(OsDirectListLabel, 8, 0);

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

    // Ensure arena map L2 and spans[] arrays exist for this new arena.
    // A 64MB arena may straddle two 64MB-aligned chunks (VirtualAlloc doesn't
    // guarantee 64MB alignment), so ensure both the base and end addresses.
    _b.LoadLocal(VReg.Arg0, 1); // new arena base
    _b.Call("__slab_arena_map_ensure");
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.AddRegImm(VReg.Scratch0, ArenaSize - 1); // last byte of arena
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_map_ensure");

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
  // 2. Allocate arena map L1 array, register first arena
  // 3. Initialize locks (Windows only)
  // =========================================================================
  // Stack slots: 0=mcache_size, 1=mcache_ptr, 2=l1_ptr
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

    // Step 2: Allocate arena map L1 array: ArenaMapL1Size * 8 bytes
    _b.MovRegImm(VReg.Arg0, ArenaMapL1Size * 8);
    _b.Call("__slab_arena_alloc");
    _b.StoreLocal(2, VReg.Scratch0); // save l1_ptr

    // Memzero the L1 array
    _b.LoadLocal(VReg.Arg0, 2);
    _b.MovRegImm(VReg.Arg1, ArenaMapL1Size * 8);
    _b.Call("__slab_memzero");

    // Store L1 base pointer in global
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreGlobal(ArenaMapL1Label, VReg.Scratch0);

    // Step 3: Register the first arena in the arena map
    // Ensure both base and end in case arena straddles two 64MB-aligned chunks
    _b.LoadGlobal(VReg.Scratch0, ArenaEndLabel);
    _b.SubRegImm(VReg.Scratch0, ArenaSize); // arena_base
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_map_ensure");
    _b.LoadGlobal(VReg.Scratch0, ArenaEndLabel);
    _b.SubRegImm(VReg.Scratch0, 1); // last byte of arena
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_map_ensure");

    // Step 4: Initialize locks (Windows only — os_unfair_lock is zero-init on macOS)
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
    // Page-align the arena bump pointer before allocating span data.
    // This ensures each span starts on a page boundary, preventing
    // two spans from sharing an arena map page entry.
    _b.LoadGlobal(VReg.Scratch0, ArenaBaseLabel);
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.MovRegImm(VReg.Scratch2, (1 << ArenaPageShift) - 1); // page mask
    _b.AndRegReg(VReg.Scratch1, VReg.Scratch2); // low bits of arena_ptr
    var alreadyAligned = UniqueLabel("mspan_arena_aligned");
    _b.JumpIfZero(VReg.Scratch1, alreadyAligned);
    // padding = page_size - low_bits
    _b.MovRegImm(VReg.Scratch0, 1 << ArenaPageShift);
    _b.SubRegReg(VReg.Scratch0, VReg.Scratch1); // padding
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_arena_alloc"); // waste padding bytes to align
    _b.DefineLabel(alreadyAligned);

    _b.LoadLocal(VReg.Scratch0, 3); // slot_size
    _b.LoadLocal(VReg.Scratch1, 4); // num_objs
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1); // data_size = slot_size * num_objs
    // Round up to page boundary so span occupies complete pages
    _b.AddRegImm(VReg.Scratch0, (1 << ArenaPageShift) - 1);
    _b.MovRegImm(VReg.Scratch1, ~(long)((1 << ArenaPageShift) - 1));
    _b.AndRegReg(VReg.Scratch0, VReg.Scratch1);
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

  /// <summary>
  /// Bump-allocates a large object from the arena (>32768 bytes, ≤64MB).
  /// No per-object header; arena-large objects cannot be individually freed.
  /// </summary>
  private void EmitArenaLargeObjectAlloc(int sizeSlot, int classSlot, int resultSlot, bool mmTrace) {
    // Emit sl_alloc trace BEFORE the arena call (top-down order)
    if (mmTrace) {
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(classSlot, VReg.Scratch0);
      EmitInlineTraceSlabAlloc(UniqueLabel("sl_alloc_arena_large_trace"), sizeSlot, classSlot);
      EmitTraceDepthInc();
    }
    _b.LoadLocal(VReg.Arg0, sizeSlot); // size (no header overhead)
    _b.Call("__slab_arena_alloc"); // Scratch0 = ptr
    _b.StoreLocal(resultSlot, VReg.Scratch0);
    if (mmTrace) {
      EmitTraceDepthDec();
    }
  }

  /// <summary>
  /// Allocates a huge object directly from the OS (>64MB).
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
    _b.LoadLocal(VReg.Arg0, sizeSlot); // size (no header overhead)
    _b.Call("__slab_os_alloc"); // Scratch0 = ptr
    _b.StoreLocal(resultSlot, VReg.Scratch0);
    if (mmTrace) {
      EmitTraceDepthDec();
    }

    // Register in OS-direct linked list for freeing
    _b.LoadLocal(VReg.Arg0, resultSlot); // ptr
    _b.LoadLocal(VReg.Arg1, sizeSlot);   // size
    _b.Call("__slab_os_direct_insert");
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
  // EmitSlabAlloc: __slab_alloc(size) -> ptr
  //
  // Header-free allocation routing:
  // 1. size > ArenaSize: OS-direct (tracked in linked list, OsFreePages on free)
  // 2. size > SlabMaxSmallSize: arena-backed large (bump-allocated, no per-object free)
  // 3. else: slab fast path (arena map lookup on free, returns to span free list)
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

    // --- OS-direct check: size > ArenaSize ---
    // Allocations larger than the arena itself must bypass the arena entirely.
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.CmpRegImm(VReg.Scratch0, ArenaSize);
    var notOsDirect = UniqueLabel("slab_alloc_not_os_direct");
    _b.JumpIf(Condition.BelowEqual, notOsDirect);

    EmitOsDirectObjectAlloc(sizeSlot: 0, classSlot: 1, resultSlot: 5, mmTrace);
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(notOsDirect);

    // --- Arena-large check ---
    // Route to arena-large path if size > max class size (32768)
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.CmpRegImm(VReg.Scratch0, SlabMaxSmallSize);
    var smallPath = UniqueLabel("slab_alloc_small");
    _b.JumpIf(Condition.BelowEqual, smallPath);

    // Arena-large path: bump-allocate from arena, no per-object free
    EmitArenaLargeObjectAlloc(sizeSlot: 0, classSlot: 1, resultSlot: 5, mmTrace);
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    // --- Small object path ---
    _b.DefineLabel(smallPath);

    // Look up size class: linear scan of class_sizes table
    // Find first class where class_sizes[i] >= size
    _b.LoadLocal(VReg.Scratch0, 0); // size (no header overhead)

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

    // Clear the free-list next pointer at slot[0] (was used as intrusive linked list)
    _b.LoadLocal(VReg.Scratch0, 5); // slot_base = user_ptr (header-free)
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);

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
    // reachable via the arena map, so freed slots will still find and
    // increment the span's free_count.
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
  // EmitSlabFree: __slab_free(slot_base)
  //
  // Header-free free path:
  // 1. Look up span via arena map (__slab_span_lookup)
  // 2. If found: slab object — zero slot, return to span free list
  // 3. If not found: try OS-direct list (__slab_os_direct_remove)
  // 4. If OS-direct: free via OsFreePages
  // 5. Otherwise: arena-large, no-op (bump-allocated)
  // =========================================================================
  // Stack slots: 0=slot_base, 1=span_ptr, 2=slot_size, 3=class_index
  public void EmitSlabFree(bool mmTrace) {
    _b.FunctionStart("__slab_free", 1, 0x50);

    // NULL check
    _b.LoadLocal(VReg.Scratch0, 0);
    var notNull = UniqueLabel("slab_free_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.FunctionEnd();

    _b.DefineLabel(notNull);

    // Look up span via arena map
    _b.LoadLocal(VReg.Arg0, 0); // slot_base
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

    // --- Slab object free ---
    _b.LoadLocal(VReg.Scratch0, 1); // span_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffSlotSize);
    _b.StoreLocal(2, VReg.Scratch1); // slot_size

    if (mmTrace) {
      _b.LoadLocal(VReg.Scratch0, 1);
      _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, MspanOffClassIndex);
      _b.StoreLocal(3, VReg.Scratch0);
      EmitInlineTraceSlabFree(UniqueLabel("sl_free_slab_trace"), sizeSlot: 2, classSlot: 3);
    }

    // Zero the slot: __slab_memzero(slot_base, slot_size)
    _b.LoadLocal(VReg.Arg0, 0);
    _b.LoadLocal(VReg.Arg1, 2);
    _b.Call("__slab_memzero");

    // Reload span_ptr (saved before memzero; slot 1 is untouched by __slab_memzero)
    _b.LoadLocal(VReg.Scratch1, 1); // Scratch1 = span_ptr

    // Push slot_base onto span's free list
    _b.LoadLocal(VReg.Scratch0, 0); // slot_base
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffFreeList);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2); // [slot_base] = old_head
    _b.StoreIndirect(VReg.Scratch1, MspanOffFreeList, VReg.Scratch0); // free_list = slot_base

    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffFreeCount);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, MspanOffFreeCount, VReg.Scratch2);

    // If span fully free, return to mcentral
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, MspanOffTotalSlots);
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch0);
    var notFullyFree = UniqueLabel("slab_free_not_fully_free");
    _b.JumpIf(Condition.NotEqual, notFullyFree);

    _b.LoadLocal(VReg.Arg0, 1); // span_ptr
    _b.Call("__slab_mcentral_return_span");

    _b.DefineLabel(notFullyFree);
    _b.FunctionEnd();

    // --- Not a slab span: try OS-direct list ---
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
    _b.FunctionEnd();

    // --- Arena-large free: no-op ---
    _b.DefineLabel(notOsDirect);
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitArenaMapEnsure: __slab_arena_map_ensure(arena_base)
  //
  // Ensures L2 and spans[] arrays exist in the arena map for the given arena
  // address. Called while the arena lock is held (from EmitArenaAlloc).
  // =========================================================================
  // Stack slots: 0=arena_base, 1=l1_slot_addr/l2_slot_addr, 2=l2_ptr/spans_ptr. Frame 0x30.
  public void EmitArenaMapEnsure() {
    _b.FunctionStart("__slab_arena_map_ensure", 1, 0x30);

    // If L1 array not allocated yet (during __slab_init bootstrap), skip
    _b.LoadGlobal(VReg.Scratch0, ArenaMapL1Label);
    var l1Ready = UniqueLabel("arena_map_l1_ready");
    _b.JumpIfNonZero(VReg.Scratch0, l1Ready);
    _b.FunctionEnd();
    _b.DefineLabel(l1Ready);

    // l1_index = arena_base >> ArenaMapL1Shift
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

    // Allocate L2 array: ArenaMapL2Size * 8 bytes
    _b.StoreLocal(1, VReg.Scratch2); // save l1_slot_addr
    _b.MovRegImm(VReg.Arg0, ArenaMapL2Size * 8);
    _b.Call("__slab_arena_alloc");
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

    // l2_index = (arena_base >> ArenaMapL2Shift) & ArenaMapL2Mask
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

    // Allocate spans array: ArenaPagesPerArena * 8 bytes
    _b.StoreLocal(1, VReg.Scratch2); // save l2_slot_addr
    _b.MovRegImm(VReg.Arg0, ArenaPagesPerArena * 8);
    _b.Call("__slab_arena_alloc");
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
  // EmitSpanRegister: __slab_span_register(span_ptr)
  //
  // Registers all pages of a span in the arena map so that any pointer
  // within the span can be mapped back to its owning mspan via span lookup.
  // =========================================================================
  // Stack slots: 0=span_ptr, 1=base_addr, 2=num_pages, 3=loop_i. Frame 0x40.
  public void EmitSpanRegister() {
    _b.FunctionStart("__slab_span_register", 1, 0x40);

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

    var loopStart = UniqueLabel("span_register_loop");
    var loopDone = UniqueLabel("span_register_done");

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

    // spans_ptr[page_index * 8] = span_ptr
    _b.ShlRegImm(VReg.Scratch0, 3);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch2);
    _b.LoadLocal(VReg.Scratch1, 0);
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

    // page_index = (ptr >> 12) & 0x3FFF
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
  // Inserts a new entry into the OS-direct linked list.
  // Each entry is 24 bytes: [ptr, size, next]. Allocated from arena.
  // =========================================================================
  // Stack slots: 0=ptr, 1=size. Frame 0x20.
  public void EmitOsDirectInsert() {
    _b.FunctionStart("__slab_os_direct_insert", 2, 0x20);

    _b.MovRegImm(VReg.Arg0, 24);
    _b.Call("__slab_arena_alloc");

    // entry[0] = ptr
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);

    // entry[8] = size
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1);

    // entry[16] = global[OsDirectListLabel] (old head)
    _b.LoadGlobal(VReg.Scratch1, OsDirectListLabel);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1);

    // global[OsDirectListLabel] = entry
    _b.StoreGlobal(OsDirectListLabel, VReg.Scratch0);

    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitOsDirectRemove: __slab_os_direct_remove(ptr) -> size
  //
  // Finds and removes an entry from the OS-direct linked list, returns size.
  // Returns 0 if not found.
  // =========================================================================
  // Stack slots: 0=ptr, 1=prev_next_addr, 2=entry. Frame 0x30.
  public void EmitOsDirectRemove() {
    _b.FunctionStart("__slab_os_direct_remove", 1, 0x30);

    _b.LeaGlobal(VReg.Scratch0, OsDirectListLabel);
    _b.StoreLocal(1, VReg.Scratch0);

    _b.LoadGlobal(VReg.Scratch0, OsDirectListLabel);
    _b.StoreLocal(2, VReg.Scratch0);

    var loopStart = UniqueLabel("os_direct_remove_loop");
    var notFound = UniqueLabel("os_direct_remove_not_found");
    var found = UniqueLabel("os_direct_remove_found");

    _b.DefineLabel(loopStart);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.JumpIfZero(VReg.Scratch0, notFound);

    // if entry[0] == ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0);
    _b.LoadLocal(VReg.Scratch2, 0);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Equal, found);

    // prev_next_addr = &entry[16]
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.AddRegImm(VReg.Scratch0, 16);
    _b.StoreLocal(1, VReg.Scratch0);

    // entry = entry[16]
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 16);
    _b.StoreLocal(2, VReg.Scratch0);
    _b.Jump(loopStart);

    _b.DefineLabel(found);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 8); // size
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 16); // next
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2); // unlink
    _b.ReturnValue(VReg.Scratch1);

    _b.DefineLabel(notFound);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // EmitRawAllocIdInsert: __mm_raw_id_insert(ptr, raw_alloc_id)
  //
  // Inserts a new entry into the raw alloc ID tracking linked list.
  // Each entry is 24 bytes: [ptr, raw_alloc_id, next]. Allocated from arena.
  // Only used when mmTrace is enabled.
  // =========================================================================
  public void EmitRawAllocIdInsert() {
    _b.FunctionStart("__mm_raw_id_insert", 2, 0x20);

    _b.MovRegImm(VReg.Arg0, 24);
    _b.Call("__slab_arena_alloc");

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
    EmitArenaMapEnsure();
    EmitArenaAlloc(mmTrace);
    EmitAllocatorInit(mmTrace);
    EmitSpanRegister();
    EmitSpanLookup();
    EmitOsDirectInsert();
    EmitOsDirectRemove();
    EmitRawAllocIdInsert();
    EmitRawAllocIdLookup();
    EmitMspanAlloc();
    EmitMcentralGetSpan();
    EmitMcentralReturnSpan();
    EmitSlabAlloc(mmTrace);
    EmitSlabFree(mmTrace);
  }
}
