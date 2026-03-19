namespace MaxonSharp.Compiler.Mlir.Runtime;

/// <summary>
/// Go-inspired three-tier slab allocator for the Maxon runtime.
///
/// Architecture:
///   Per-P mcache  (lock-free fast path: one cached mspan per size class per P)
///        |  refill
///     mcentral    (one per size class, locked)
///        |  grow
///      mheap      (global, requests pages from OS via OsAllocPages)
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
  // Classes 0-14 use 1 page (4096 bytes). Classes 15-17 use multiple pages.

  private static readonly int[] SlabClassSizes = [
    8, 16, 24, 32, 48, 64, 96, 128,
    192, 256, 384, 512, 1024, 2048, 4096,
    8192, 16384, 32768
  ];

  private static readonly int[] SlabObjsPerSpan = [
    512, 256, 170, 128, 85, 64, 42, 32,
    21, 16, 10, 8, 4, 2, 1,
    4, 2, 1
  ];

  // Pages per span: classes 0-14 = 1 page (4096 bytes).
  // Classes 15-17 use multiple pages to fit their slot sizes.
  // Objs = (pages * 4096) / slot_size — must be exact.
  //   Class 15: 8192 bytes/slot, 8 pages = 32768 bytes, 4 slots
  //   Class 16: 16384 bytes/slot, 8 pages = 32768 bytes, 2 slots
  //   Class 17: 32768 bytes/slot, 8 pages = 32768 bytes, 1 slot
  private static readonly int[] SlabPagesPerSpan = [
    1, 1, 1, 1, 1, 1, 1, 1,
    1, 1, 1, 1, 1, 1, 1,
    8, 8, 8
  ];

  private const int SlabNumClasses = 18;
  private const int SlabMaxSmallSize = 32768;
  private const int SlabHeaderSize = 16; // header stored before user pointer
  private const int PageSize = 4096;

  // mspan struct layout (48 bytes)
  private const int MspanOffBaseAddr = 0x00;
  private const int MspanOffSlotSize = 0x08;
  private const int MspanOffFreeList = 0x10;
  private const int MspanOffFreeCount = 0x18;
  private const int MspanOffNextSpan = 0x20;
  private const int MspanOffClassIndex = 0x28;
  private const int MspanStructSize = 0x30; // 48 bytes

  // mcentral struct layout (64 bytes, padded for CRITICAL_SECTION on Windows)
  // We use 18 separate lock labels instead of inline lock storage.
  // The mcentral array just stores partial_head and full_head per class.
  // Layout per class entry (16 bytes):
  //   +0x00: partial_head (mspan*)
  //   +0x08: full_head    (mspan*)
  private const int McentralEntrySize = 16;

  // Global data labels
  private const string McentralArrayLabel = "__slab_mcentral_array";
  private const string MspanPoolPtrLabel = "__slab_mspan_pool_ptr";
  private const string MspanPoolEndLabel = "__slab_mspan_pool_end";
  private const string MspanPoolLockLabel = "__slab_mspan_pool_lock";
  private const string McacheBaseLabel = "__slab_mcache_base";
  private const string SlabClassSizesLabel = "__slab_class_sizes";
  private const string SlabObjsLabel = "__slab_objs_per_span";
  private const string SlabPagesLabel = "__slab_pages_per_span";
  private const string SlabInitDoneLabel = "__slab_init_done";

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

    // mspan pool bump allocator
    _b.DefineGlobal(MspanPoolPtrLabel, 8, 0);
    _b.DefineGlobal(MspanPoolEndLabel, 8, 0);

    // mcache base pointer (allocated at init time: max_procs * 18 * 8 bytes)
    _b.DefineGlobal(McacheBaseLabel, 8, 0);

    // Init done flag
    _b.DefineGlobal(SlabInitDoneLabel, 8, 0);

    // Lock for mspan pool allocation
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
    var pagesPerSpanData = new byte[SlabNumClasses * 8];
    for (int i = 0; i < SlabNumClasses; i++) {
      BitConverter.TryWriteBytes(classSizesData.AsSpan(i * 8), (long)SlabClassSizes[i]);
      BitConverter.TryWriteBytes(objsPerSpanData.AsSpan(i * 8), (long)SlabObjsPerSpan[i]);
      BitConverter.TryWriteBytes(pagesPerSpanData.AsSpan(i * 8), (long)SlabPagesPerSpan[i]);
    }
    _b.DefineSymdata(SlabClassSizesLabel, classSizesData);
    _b.DefineSymdata(SlabObjsLabel, objsPerSpanData);
    _b.DefineSymdata(SlabPagesLabel, pagesPerSpanData);
  }

  // =========================================================================
  // EmitAllocatorInit: __slab_init()
  //
  // Called during scheduler init (after P structs are allocated).
  // 1. Allocate mcache array: max_procs * 18 * 8 bytes (zero-initialized)
  // 2. Initialize mspan pool: allocate one 4096-byte page for mspan headers
  // 3. On Windows: InitializeCriticalSection for all locks
  // =========================================================================
  // Stack slots: 0 = max_procs, 1 = mcache_alloc_size, 2 = scratch
  public void EmitAllocatorInit() {
    _b.FunctionStart("__slab_init", 0, 0x40);

    // Check if already initialized
    _b.LoadGlobal(VReg.Scratch0, SlabInitDoneLabel);
    var alreadyDone = UniqueLabel("slab_init_done");
    _b.JumpIfNonZero(VReg.Scratch0, alreadyDone);

    // Mark as initialized
    _b.MovRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal(SlabInitDoneLabel, VReg.Scratch0);

    // Step 1: Allocate mcache array
    // Size = max_procs * 18 * 8 = max_procs * 144
    _b.LoadGlobal(VReg.Scratch0, "__sched_max_procs");
    _b.StoreLocal(0, VReg.Scratch0);
    _b.MovRegImm(VReg.Scratch1, SlabNumClasses * 8); // 144
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.StoreLocal(1, VReg.Scratch0); // save alloc size

    // Allocate via OsAllocPages (zero-initialized)
    _b.OsAllocPages(VReg.Scratch1, VReg.Scratch0);
    _b.StoreGlobal(McacheBaseLabel, VReg.Scratch1);

    // Step 2: Allocate initial mspan pool page (4096 bytes = ~85 mspans)
    _b.MovRegImm(VReg.Scratch0, PageSize);
    _b.OsAllocPages(VReg.Scratch1, VReg.Scratch0);
    _b.StoreGlobal(MspanPoolPtrLabel, VReg.Scratch1);
    // pool_end = pool_ptr + 4096
    _b.AddRegImm(VReg.Scratch1, PageSize);
    _b.StoreGlobal(MspanPoolEndLabel, VReg.Scratch1);

    // Step 3: Initialize locks (Windows only — os_unfair_lock is zero-init on macOS)
    if (_b.IsWindows) {
      _b.LockAcquire(MspanPoolLockLabel); // This calls InitializeCriticalSection implicitly? No.
      _b.LockRelease(MspanPoolLockLabel);
      // Actually, we need to call InitializeCriticalSection explicitly.
      // The LockAcquire/LockRelease on IEmitterBackend do EnterCriticalSection/LeaveCriticalSection
      // which require the CS to be initialized first. We need a different approach.
      // We'll emit the init calls using LeaGlobal + CallImport.
      // But IEmitterBackend doesn't expose CallImport for arbitrary DLL functions.
      // Looking at the existing code, CRITICAL_SECTION init is done in platform-specific
      // SchedInit. We'll need to call __slab_init from SchedInit AFTER the CS initialization.
      //
      // Actually, let me use a simpler approach: just use the LockAcquire/LockRelease API
      // which calls EnterCriticalSection/LeaveCriticalSection. The issue is that these locks
      // need to be initialized first. The cleanest solution is to have the platform-specific
      // SchedInit code initialize these CRITICAL_SECTIONs before calling __slab_init.
      //
      // For now, mark this as needing external initialization and skip the lock init here.
      // The platform code (EmitSchedInit in X86CodeEmitter.Runtime.cs) will call
      // InitializeCriticalSection for each slab lock.
    }

    _b.DefineLabel(alreadyDone);
    _b.FunctionEnd();
  }

  // =========================================================================
  // EmitMspanAlloc: __slab_mspan_alloc(class_index) -> mspan*
  //
  // Allocates a new mspan for the given size class:
  // 1. Allocate an mspan header from the global mspan pool (bump allocator)
  // 2. Allocate page(s) from OS for the actual slots
  // 3. Build the intrusive free list through all slots
  // 4. Return the mspan pointer
  // =========================================================================
  // Stack slots: 0=class_index, 1=mspan_ptr, 2=page_base, 3=slot_size,
  //              4=num_objs, 5=loop_counter, 6=num_pages
  public void EmitMspanAlloc() {
    _b.FunctionStart("__slab_mspan_alloc", 1, 0x60);

    // --- Allocate mspan header from pool ---
    _b.LockAcquire(MspanPoolLockLabel);

    // Check if pool has room for one more mspan (48 bytes)
    _b.LoadGlobal(VReg.Scratch0, MspanPoolPtrLabel);
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.AddRegImm(VReg.Scratch1, MspanStructSize);
    _b.LoadGlobal(VReg.Scratch2, MspanPoolEndLabel);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    var poolOk = UniqueLabel("mspan_pool_ok");
    _b.JumpIf(Condition.BelowEqual, poolOk);

    // Pool exhausted: allocate another page
    _b.MovRegImm(VReg.Scratch0, PageSize);
    _b.OsAllocPages(VReg.Scratch1, VReg.Scratch0);
    _b.StoreGlobal(MspanPoolPtrLabel, VReg.Scratch1);
    _b.MovRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.AddRegImm(VReg.Scratch1, PageSize);
    _b.StoreGlobal(MspanPoolEndLabel, VReg.Scratch1);
    // Scratch0 = new pool_ptr (start of fresh page)

    _b.DefineLabel(poolOk);
    // Scratch0 = mspan_ptr (current pool_ptr, or fresh page start)
    // Need to reload pool_ptr since the pool_ok path may have Scratch0 from the check
    _b.LoadGlobal(VReg.Scratch0, MspanPoolPtrLabel);
    _b.StoreLocal(1, VReg.Scratch0); // slot 1 = mspan_ptr

    // Advance pool_ptr by MspanStructSize
    _b.AddRegImm(VReg.Scratch0, MspanStructSize);
    _b.StoreGlobal(MspanPoolPtrLabel, VReg.Scratch0);

    _b.LockRelease(MspanPoolLockLabel);

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

    // num_pages = pages_per_span[class_index]
    _b.LeaSymdata(VReg.Scratch0, SlabPagesLabel);
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.ShlRegImm(VReg.Scratch1, 3);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 0);
    _b.StoreLocal(6, VReg.Scratch0); // slot 6 = num_pages

    // --- Allocate pages from OS ---
    // alloc_size = num_pages * 4096
    _b.LoadLocal(VReg.Scratch0, 6);
    _b.ShlRegImm(VReg.Scratch0, 12); // * 4096
    _b.OsAllocPages(VReg.Scratch1, VReg.Scratch0); // Scratch1 = page_base
    _b.StoreLocal(2, VReg.Scratch1); // slot 2 = page_base

    // --- Initialize mspan fields ---
    _b.LoadLocal(VReg.Scratch0, 1); // mspan_ptr
    _b.LoadLocal(VReg.Scratch1, 2); // page_base
    _b.StoreIndirect(VReg.Scratch0, MspanOffBaseAddr, VReg.Scratch1);

    _b.LoadLocal(VReg.Scratch1, 3); // slot_size
    _b.StoreIndirect(VReg.Scratch0, MspanOffSlotSize, VReg.Scratch1);

    _b.LoadLocal(VReg.Scratch1, 4); // num_objs = free_count initially
    _b.StoreIndirect(VReg.Scratch0, MspanOffFreeCount, VReg.Scratch1);

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

    // Lock: we need to use per-class locks. Since IEmitterBackend.LockAcquire takes
    // a label string, and we need runtime-computed lock selection, we use a different
    // approach: emit a switch on class_index to call the right lock.
    // Actually, this is complex. Let me use a simpler approach: since class_index
    // is a runtime value, we'll use the mspan_pool_lock as a single global lock
    // for all mcentral operations. This is simpler and correct (just less parallel).
    // We can optimize to per-class locks later.
    _b.LockAcquire(MspanPoolLockLabel);

    // Check partial_head
    _b.LoadLocal(VReg.Scratch0, 1); // mcentral_addr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // partial_head
    var hasPartial = UniqueLabel("mcentral_has_partial");
    _b.JumpIfNonZero(VReg.Scratch1, hasPartial);

    // No partial spans: allocate new one
    // Save mcentral_addr first (Call will clobber)
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
  // Returns a span with free slots back to its class's mcentral partial list.
  // Called when a mcache slot is being replaced (old span returned to mcentral).
  // =========================================================================
  // Stack slots: 0=span_ptr
  public void EmitMcentralReturnSpan() {
    _b.FunctionStart("__slab_mcentral_return_span", 1, 0x30);

    _b.LockAcquire(MspanPoolLockLabel);

    // Get class_index from span
    _b.LoadLocal(VReg.Scratch0, 0); // span_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MspanOffClassIndex); // class_index

    // Compute mcentral entry address
    _b.LeaGlobal(VReg.Scratch2, McentralArrayLabel);
    _b.ShlRegImm(VReg.Scratch1, 4); // class_index * 16
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1); // Scratch2 = mcentral_addr

    // Prepend span to partial_head: span->next = old_partial_head
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch2, 0); // old partial_head
    _b.LoadLocal(VReg.Scratch0, 0); // span_ptr
    _b.StoreIndirect(VReg.Scratch0, MspanOffNextSpan, VReg.Scratch1); // span->next = old_head
    _b.StoreIndirect(VReg.Scratch2, 0, VReg.Scratch0); // partial_head = span

    _b.LockRelease(MspanPoolLockLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // Slab trace helpers
  // =========================================================================

  /// <summary>Emit: indent + "slab_alloc size=N class=C\n"</summary>
  private void EmitInlineTraceSlabAlloc(int sizeSlot, int classSlot) {
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_alloc");
    _b.Call("mm_trace_print_tag");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_size_eq");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, sizeSlot);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_class");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, classSlot);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  /// <summary>Emit: indent + "slab_free size=N class=C\n"</summary>
  private void EmitInlineTraceSlabFree(int sizeSlot, int classSlot) {
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_free");
    _b.Call("mm_trace_print_tag");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_size_eq");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, sizeSlot);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__slab_tag_class");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, classSlot);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  // =========================================================================
  // EmitSlabAlloc: __slab_alloc(size) -> user_ptr
  //
  // Fast-path slab allocation:
  // 1. If size > 32768: large object path (OsAllocPages directly)
  // 2. Look up size class
  // 3. Load P->id, compute mcache slot address
  // 4. If cached span has free slots: pop from free list, return
  // 5. Else: refill from mcentral, retry
  //
  // Returns a user_ptr with a 16-byte header:
  //   [user_ptr - 16] = span_ptr (small) or total_alloc_size (large)
  //   [user_ptr -  8] = 0 (small) or 1 (large)
  // =========================================================================
  // Stack slots: 0=size, 1=class_index, 2=P_id, 3=mcache_slot_addr,
  //              4=span_ptr, 5=alloc_result
  public void EmitSlabAlloc(bool mmTrace) {
    _b.FunctionStart("__slab_alloc", 1, 0x60);

    // Check if allocator is initialized
    _b.LoadGlobal(VReg.Scratch0, SlabInitDoneLabel);
    var slabReady = UniqueLabel("slab_alloc_ready");
    _b.JumpIfNonZero(VReg.Scratch0, slabReady);

    // Fallback: allocator not initialized, use direct OS allocation
    // This handles early allocations before __slab_init is called
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize); // total = size + 16
    _b.OsAllocPages(VReg.Scratch1, VReg.Scratch0); // Scratch1 = base
    // Store total_alloc_size at [base + 0]
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0);
    // Store flags = 1 (large) at [base + 8]
    _b.MovRegImm(VReg.Scratch0, 1);
    _b.StoreIndirect(VReg.Scratch1, 8, VReg.Scratch0);
    // user_ptr = base + 16
    _b.AddRegImm(VReg.Scratch1, SlabHeaderSize);
    _b.StoreLocal(5, VReg.Scratch1); // slot 5 = user_ptr
    if (mmTrace) {
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(1, VReg.Scratch0); // class_index = -1 (fallback)
      EmitInlineTraceSlabAlloc(sizeSlot: 0, classSlot: 1);
    }
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(slabReady);

    // --- Large object check ---
    // Must account for the 16-byte header: effective_size = size + 16
    // Route to large path if effective_size > max class size (32768),
    // i.e., size > 32768 - 16 = 32752
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.CmpRegImm(VReg.Scratch0, SlabMaxSmallSize - SlabHeaderSize);
    var smallPath = UniqueLabel("slab_alloc_small");
    _b.JumpIf(Condition.BelowEqual, smallPath);

    // Large object path: allocate size + 16 bytes directly from OS
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize); // total = size + 16
    _b.OsAllocPages(VReg.Scratch1, VReg.Scratch0); // Scratch1 = base
    // Store total_alloc_size at [base + 0]
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0);
    // Store flags = 1 (large) at [base + 8]
    _b.MovRegImm(VReg.Scratch0, 1);
    _b.StoreIndirect(VReg.Scratch1, 8, VReg.Scratch0);
    // user_ptr = base + 16
    _b.AddRegImm(VReg.Scratch1, SlabHeaderSize);
    _b.StoreLocal(5, VReg.Scratch1); // slot 5 = user_ptr
    if (mmTrace) {
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(1, VReg.Scratch0); // class_index = -1 (large)
      EmitInlineTraceSlabAlloc(sizeSlot: 0, classSlot: 1);
    }
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);

    // --- Small object path ---
    _b.DefineLabel(smallPath);

    // Look up size class: linear scan of class_sizes table
    // Find first class where class_sizes[i] >= (size + SlabHeaderSize)
    // Wait - the slab header is stored INSIDE the slab slot. So we need:
    // effective_size = size + SlabHeaderSize (16 bytes for the span_ptr + flags header)
    // Actually, re-thinking: the 16-byte header is part of the allocation.
    // The caller asks for `size` bytes. We need to allocate size + 16 bytes from a slab slot.
    // So we look up the class for (size + 16).
    // But the slots contain user data + header. We need a slot big enough for both.
    // Let's compute effective_size = size + SlabHeaderSize.
    _b.LoadLocal(VReg.Scratch0, 0); // size
    _b.AddRegImm(VReg.Scratch0, SlabHeaderSize); // effective_size = size + 16

    // Linear scan: class_index = 0
    _b.ZeroReg(VReg.Scratch1); // class_index = 0
    _b.LeaSymdata(VReg.Scratch2, SlabClassSizesLabel);

    var classLoop = UniqueLabel("slab_class_loop");
    var classFound = UniqueLabel("slab_class_found");

    _b.DefineLabel(classLoop);
    // If class_index >= 18, this shouldn't happen (size <= 32768 + 16 < max class)
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
      EmitInlineTraceSlabAlloc(sizeSlot: 0, classSlot: 1);
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
  // Free a slab-allocated object:
  // 1. Read header: span_ptr = [ptr-16], flags = [ptr-8]
  // 2. If flags == 1 (large): OsFreePages (OS already zeroes on unmap)
  // 3. Else: zero user data area, then return slot to span's free list.
  //    Zeroing on free ensures stale pointers crash on access (use-after-free
  //    detection) and slots are ready to use without zeroing on alloc.
  // =========================================================================
  // Stack slots: 0=user_ptr, 1=span_ptr (scratch), 2=slot_size (scratch)
  public void EmitSlabFree(bool mmTrace) {
    _b.FunctionStart("__slab_free", 1, mmTrace ? 0x40 : 0x30);

    // NULL check
    _b.LoadLocal(VReg.Scratch0, 0);
    var notNull = UniqueLabel("slab_free_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.FunctionEnd();

    _b.DefineLabel(notNull);

    // Read flags at [ptr - 8]
    _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -8); // flags

    var largeFree = UniqueLabel("slab_free_large");
    _b.JumpIfNonZero(VReg.Scratch1, largeFree);

    // --- Small object free ---
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
      EmitInlineTraceSlabFree(sizeSlot: 2, classSlot: 1);
      // Reload span_ptr (clobbered by trace calls) from user_ptr header
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // span_ptr
      _b.StoreLocal(1, VReg.Scratch1);
      // Reload slot_size from span
      _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, MspanOffSlotSize);
      _b.StoreLocal(2, VReg.Scratch2);
    }

    // Zero user data: __slab_memzero(user_ptr, slot_size - SlabHeaderSize)
    // This catches use-after-free via null-dereference and pre-zeros for next alloc.
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

    _b.FunctionEnd();

    // --- Large object free ---
    _b.DefineLabel(largeFree);

    if (mmTrace) {
      // user_size = total_alloc_size (at [ptr-16]) - SlabHeaderSize
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // total_alloc_size
      _b.SubRegImm(VReg.Scratch1, SlabHeaderSize); // user_size
      _b.StoreLocal(1, VReg.Scratch1); // slot 1 = user_size
      _b.MovRegImm(VReg.Scratch0, -1);
      _b.StoreLocal(2, VReg.Scratch0); // slot 2 = class_index = -1
      EmitInlineTraceSlabFree(sizeSlot: 1, classSlot: 2);
    }

    // Read total_alloc_size from [ptr - 16]
    _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, -16); // total_alloc_size

    // base = user_ptr - 16
    _b.SubRegImm(VReg.Scratch0, SlabHeaderSize);

    // OsFreePages(base, total_alloc_size)
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
    EmitAllocatorInit();
    EmitSlabMemzero();
    EmitMspanAlloc();
    EmitMcentralGetSpan();
    EmitMcentralReturnSpan();
    EmitSlabAlloc(mmTrace);
    EmitSlabFree(mmTrace);
  }
}
