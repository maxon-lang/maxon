using MaxonSharp.Debug;

namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// Memory manager functions, emitted once for both platforms.
/// </summary>
public partial class RuntimeEmitter {

  /// <summary>
  /// Emit all memory manager global data (panic strings, trace tags, etc.).
  /// Must be called before emitting MM functions.
  /// </summary>
  public void EmitMmGlobals(bool mmTrace, bool mmDebug, List<string?> tagTable) {
    // Mutable counters — must be defined as globals (not symdata) so LeaGlobal resolves correctly
    _b.DefineGlobal("__mm_alloc_count", 8, 0);
    _b.DefineGlobal("__mm_alloc_id_counter", 8, 0);
    _b.DefineGlobal("__mm_raw_alloc_count", 8, 0);

    // ---- Memory-traffic byte counters (always on, like the three above) --------
    //
    // WHY THEY ARE UNCONDITIONAL: --mm-debug and --mm-trace change codegen, so a
    // binary that can report its memory is a DIFFERENT binary from the one being
    // timed. A suite that measures a compiler's time AND memory in one run therefore
    // needs counters that survive a release build. These are them, and their accessors
    // (EmitMmCounterAccessors) are what `__Builtins.mm*()` reads.
    //
    // TWO LAYERS, because the allocator has two and they answer different questions:
    //
    //   TRACKED  (mm_alloc / mm_realloc / mm_free) — every managed OBJECT: a struct,
    //            an array's outer handle, a __ManagedMemory, a String. Header-carrying,
    //            counted by __mm_alloc_count / __mm_alloc_id_counter above.
    //   RAW      (mm_raw_alloc / mm_raw_free) — the header-free BUFFERS behind those
    //            objects: array elements, string bytes. This is where the byte VOLUME
    //            lives; the tracked layer alone sees little but 8- and 24-byte handles
    //            and would report a compiler's memory traffic as nearly flat.
    //
    // NOT GLOBAL WORDS — PER-P SLOTS, AND NOT ATOMIC. That is the whole design, and it
    // was forced by measurement. Both counters sit in the allocator's hot path, so the
    // obvious `lock xadd` on a shared global costs a locked read-modify-write on EVERY
    // allocation: measured at +3% on a fixed shv2 compile, and a first cut that also
    // carried live/peak bytes (a locked add on the FREE path too) cost +9%. That is the
    // instrument perturbing the subject it exists to measure, and it would be a permanent
    // tax on every binary this compiler emits.
    //
    // A P owns its slot and is run by at most one M at a time (the GMP single-writer
    // invariant the slab's own free-list already rests on), so a PLAIN add to that slot
    // is exact with no lock at all — measured at +1%. Slots are one cache line apart, so
    // two Ps accumulating concurrently do not ping-pong a shared line. The accessors sum
    // the slots.
    //
    // Threads with no P (the IOCP completion loop, the sync worker, the fault handler)
    // and allocations that land before __slab_init has run have nowhere to accumulate, so
    // they fall back to a genuinely shared word — and THAT one is atomic. It is off the
    // measured path and rarely touched.
    //
    // The result is a counter that is monotonic and EXACT: it deltas cleanly across a
    // phase boundary and is bit-for-bit reproducible on the same input, which is what
    // lets a suite gate on memory where it cannot gate on wall time.
    //
    // There is deliberately no live-bytes or peak-bytes counter. mm_raw_free is handed a
    // bare pointer with no header and no size, so raw bytes cannot be given back, and a
    // "live bytes" that silently omitted every array buffer would be worse than none.
    // Peak memory, if it is ever wanted, belongs at the slab/OS layer where the sizes are
    // already known and the path is cold.
    _b.DefineGlobal(MmBytesByPLabel, 8, 0);          // -> max_procs * 64 bytes (see __slab_init)
    _b.DefineGlobal(MmAllocBytesNoPLabel, 8, 0);     // atomic fallback, tracked layer
    _b.DefineGlobal(MmRawAllocBytesNoPLabel, 8, 0);  // atomic fallback, raw layer
    _b.DefineGlobal(MmRawAllocTotalNoPLabel, 8, 0);  // atomic fallback, raw ALLOCATION COUNT

    // Per-tag live-allocation counters for --mm-debug leak breakdown.
    // Array of int64, one slot per tag index (index 0 reserved for "no tag").
    // Size at least one slot so LeaGlobal always resolves.
    if (mmDebug) {
      int tagCount = System.Math.Max(tagTable.Count, 1);
      _b.DefineGlobal("__mm_alloc_count_by_tag", tagCount * 8, 0);
    }

    _b.DefineSymdata("__mm_leak_prefix", "MM leak: \0"u8.ToArray());
    _b.DefineSymdata("__mm_leak_suffix", " allocation(s) remain\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_raw_leak_prefix", "MM raw leak: \0"u8.ToArray());
    if (mmDebug) {
      _b.DefineSymdata("__mm_leak_by_tag_indent", "  \0"u8.ToArray());
      _b.DefineSymdata("__mm_leak_by_tag_space", " \0"u8.ToArray());
      _b.DefineSymdata("__mm_leak_raw_label", " (raw)\n\0"u8.ToArray());
      _b.DefineSymdata("__mm_leak_tag_newline", "\n\0"u8.ToArray());
    }
    _b.DefineSymdata("__mm_hex_chars", "0123456789abcdef\0"u8.ToArray());
    _b.DefineSymdata("__mm_hex_buf", new byte[24]);
    _b.DefineSymdata("__mm_tag_newline", "\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_tag_null", "(null)\0"u8.ToArray());
    _b.DefineSymdata("__mm_tag_minus_one", "-1\0"u8.ToArray());

    if (mmTrace) {
      // MM-layer trace tags (prefixed with mm_)
      _b.DefineSymdata("__mm_tag_alloc", "mm_alloc \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_free", "mm_free \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_raw_alloc", "mm_raw_alloc\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_raw_free", "mm_raw_free\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_incref", "mm_incref \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_decref", "mm_decref \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_transfer", "mm_transfer \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_realloc", "mm_realloc \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_cow", "mm_cow \0"u8.ToArray());
      // Slab-layer trace tags (prefixed with sl_)
      _b.DefineSymdata("__slab_tag_alloc", "sl_alloc\0"u8.ToArray());
      _b.DefineSymdata("__slab_tag_free", "sl_free\0"u8.ToArray());
      _b.DefineSymdata("__slab_tag_class", " class=\0"u8.ToArray());
      // OS-layer trace tags (unchanged)
      _b.DefineSymdata("__slab_tag_os_alloc", "os_alloc\0"u8.ToArray());
      _b.DefineSymdata("__slab_tag_os_free", "os_free\0"u8.ToArray());
      _b.DefineSymdata("__slab_tag_init", "sl_init\0"u8.ToArray());
      // Common trace formatting
      _b.DefineSymdata("__mm_tag_size_eq", " size=\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_rc_eq", " rc=\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_hash", " #\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_hash_r", " #R\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_space", " \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_lbracket", " [\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_rbracket", "]\0"u8.ToArray());
      _b.DefineGlobal("__mm_trace_depth", 8, 0);
      _b.DefineGlobal("__mm_trace_tag_ctx", 8, 0);
      _b.DefineGlobal("__mm_raw_alloc_id_counter", 8, 0);
      _b.DefineSymdata("__mm_tag_indent", "  \0"u8.ToArray());
      _b.DefineSymdata("__rt_tag_buffer", "Buffer\0"u8.ToArray());
      _b.DefineSymdata("__rt_tag_cstring", "CString\0"u8.ToArray());
      _b.DefineSymdata("__rt_tag_cmdline_arg", "CmdLineArg\0"u8.ToArray());
      _b.DefineSymdata("__rt_tag_find_data", "FindData\0"u8.ToArray());
      _b.DefineSymdata("__rt_tag_dir_buffer", "DirBuffer\0"u8.ToArray());
      _b.DefineSymdata("__rt_tag_capture_result", "CaptureResult\0"u8.ToArray());
      _b.DefineSymdata("__rt_tag_pipe_buffer", "PipeBuffer\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_managed_elements", "~ManagedElements\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_managed_list_detach", "managed_list_detach\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_managed_list_clear", "managed_list_clear\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_managed_list_decref_values", "managed_list_decref_values\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_find_close", "find_close\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_cow_copy", "cow_copy\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_cmdline_arg", "cmdline_arg\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_exe_path", "exe_path\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_find_first_file", "find_first_file\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_get_cwd", "get_cwd\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_capture", "capture\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_pipe_read", "pipe_read\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_realloc", "realloc\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_managed_list_insert", "managed_list_insert\0"u8.ToArray());
    }

    _b.DefineSymdata("__mm_panic_decref_null",
      "mm_decref called with NULL pointer\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_decref_bad_ptr",
      "mm_decref called with invalid pointer (negative/sentinel value, possible use-after-free)\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_incref_null",
      "mm_incref called with NULL pointer\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_decref_underflow",
      "mm_decref: refcount underflow (already zero)\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_index_oob",
      "__ManagedMemory: index out of bounds\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_decref_elems_bad_elemsize",
      "mm_decref_managed_elements: element_size != 8 (non-pointer elements in managed element walk)\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_byte_oob",
      "__ManagedMemory: byte index out of bounds\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_shift_oob",
      "__ManagedMemory: shift out of bounds\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_slice_oob",
      "__ManagedMemory: slice out of bounds\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_setlength_oob",
      "__ManagedMemory: setLength exceeds capacity\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_grow_shrink",
      "__ManagedMemory: grow cannot shrink capacity\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_alloc_zero_size",
      "__ManagedMemory: alloc size must be > 0\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_realloc_zero_size",
      "__ManagedMemory: realloc size must be > 0\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_element_size_zero",
      "__ManagedMemory: element_size must be > 0\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_cursor_oob",
      "__ManagedMemoryCursor: position out of bounds (current() at exhausted cursor)\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_list_empty",
      "__ManagedList: operation on empty list (cursorValue() before cursorStart())\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_list_node_not_in_list",
      "__ManagedList: node does not belong to this list (insertAfter/Before/detach/remove)\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_create_negative_count",
      "__ManagedMemory: create() count must be >= 0\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_file_stat_index_oob",
      "__ManagedFile.statField: index must be in [0, 6)\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_file_stat_null_buffer",
      "__ManagedFile.statField/statFree: buffer must not be null\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_dir_filename_null_block",
      "__ManagedDirectory.filename: _block is null (called on closed iterator)\n\0"u8.ToArray());
    _b.DefineSymdata("__slab_panic_sentinel_owning_p",
      "__slab_free: cross-P free hit owning_p sentinel; span already returned to mcentral (Mimalloc invariant violation)\n\0"u8.ToArray());
    // Unconditional (NOT mmDebug-gated): a NULL from VirtualAlloc/mmap must fault
    // cleanly as "out of memory" instead of a downstream nil-store to [NULL]
    // (an opaque addr=0x0 access violation). Every __slab_os_alloc caller
    // dereferences its result, so the check belongs in the allocator itself.
    _b.DefineSymdata("__slab_panic_oom",
      "out of memory: VirtualAlloc/mmap returned NULL (committed memory exhausted)\n\0"u8.ToArray());

    if (mmDebug) {
      _b.DefineSymdata("__mm_panic_canary",
        "mm_debug: heap canary overwritten (buffer overrun detected)\n\0"u8.ToArray());
      _b.DefineSymdata("__mm_panic_heap_null",
        "mm_debug: VirtualAlloc/mmap returned NULL (out of memory)\n\0"u8.ToArray());
      _b.DefineSymdata("__mm_panic_realloc_null",
        "mm_debug: realloc returned NULL (out of memory)\n\0"u8.ToArray());
      _b.DefineSymdata("__mm_panic_canary_tag", "mm_debug canary fail ptr=\0"u8.ToArray());
      _b.DefineSymdata("__mm_debug_tag_size", " size=\0"u8.ToArray());
    }
  }

  // =========================================================================
  // Inline trace helpers
  // =========================================================================
  // These emit inline code sequences that call the already-emitted trace
  // runtime functions (mm_trace_print_tag, mm_trace_print_i64, etc.).
  // ptrSlot/scopeSlot/sizeSlot are logical stack frame slot indices.

  /// <summary>
  /// Emit: indent + tagLabel + "TypeName #N" [+ " rc=R"] [+ " size=S"] [+ " [scope]"] + "\n"
  /// </summary>
  private void EmitInlineTrace(string tagLabel, string uniquePrefix, int ptrSlot, int scopeSlot,
      bool printRc = true, int rcSubtract = 0, int? sizeSlot = null) {
    if (Compiler.MmTraceRawOnly) return; // raw-only: suppress managed alloc/incref/decref/realloc lines
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, tagLabel);
    _b.Call("mm_trace_print_tag");
    EmitTraceTagAndId(ptrSlot);
    if (printRc) EmitTraceRc(ptrSlot, rcSubtract);
    if (sizeSlot.HasValue) EmitTraceSize(sizeSlot.Value);
    EmitTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  /// <summary>Emit: indent + "mm_free TypeName #N [scope]\n"</summary>
  private void EmitInlineTraceFree(string uniquePrefix, int ptrSlot, int scopeSlot) {
    if (Compiler.MmTraceRawOnly) return; // raw-only: suppress managed free lines
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_free");
    _b.Call("mm_trace_print_tag");
    EmitTraceTagAndId(ptrSlot);
    EmitTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  /// <summary>Print "TypeName #N" from packed_id at [user_ptr - 24].</summary>
  private void EmitTraceTagAndId(int ptrSlot) {
    // mm_trace_print_packed_tag(user_ptr)
    _b.LoadLocal(VReg.Arg0, ptrSlot);
    _b.Call("mm_trace_print_packed_tag");
    // Print " #"
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_hash");
    _b.Call("mm_trace_print_tag");
    // Print alloc_id = [ptr-24] >> 16
    _b.LoadLocal(VReg.Scratch0, ptrSlot); // Scratch0 = user_ptr
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, MmOffPackedId); // Arg0 = [ptr-24]
    _b.ShrRegImm(VReg.Arg0, 16); // Arg0 = alloc_id
    _b.Call("mm_trace_print_i64");
  }

  /// <summary>Print " rc=N" from refcount at [user_ptr - 8].</summary>
  private void EmitTraceRc(int ptrSlot, int rcSubtract = 0) {
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_rc_eq");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Scratch0, ptrSlot); // Scratch0 = user_ptr
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, MmOffRefcount); // Arg0 = [ptr-8]
    if (rcSubtract > 0) _b.SubRegImm(VReg.Arg0, rcSubtract);
    _b.Call("mm_trace_print_i64");
  }

  /// <summary>Print " size=N" from size at [frame + sizeSlot].</summary>
  private void EmitTraceSize(int sizeSlot) {
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_size_eq");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, sizeSlot);
    _b.Call("mm_trace_print_i64");
  }

  /// <summary>Print " [scope]" if scope is non-null, then "\n".</summary>
  private void EmitTraceScopeAndNewline(string skipLabel, int scopeSlot) {
    _b.LoadLocal(VReg.Scratch0, scopeSlot);
    _b.JumpIfZero(VReg.Scratch0, skipLabel);
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_lbracket");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, scopeSlot);
    _b.Call("mm_trace_print_tag");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_rbracket");
    _b.Call("mm_trace_print_tag");
    _b.DefineLabel(skipLabel);
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  /// <summary>Emit: indent + "mm_raw_alloc #RN size=S" [+ " [scope]"] + "\n"</summary>
  private void EmitInlineTraceRawAlloc(string uniquePrefix, int sizeSlot, int scopeSlot,
      int? rawIdSlot = null) {
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_raw_alloc");
    _b.Call("mm_trace_print_tag");
    if (rawIdSlot.HasValue) {
      _b.LeaSymdata(VReg.Arg0, "__mm_tag_hash_r");
      _b.Call("mm_trace_print_tag");
      _b.LoadLocal(VReg.Arg0, rawIdSlot.Value);
      _b.Call("mm_trace_print_i64");
    }
    EmitTraceSize(sizeSlot);
    EmitTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  /// <summary>Emit: indent + "mm_raw_free #RN" [+ " [scope]"] + "\n"</summary>
  private void EmitInlineTraceRawFree(string uniquePrefix, int ptrSlot, int scopeSlot) {
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_raw_free");
    _b.Call("mm_trace_print_tag");
    // Print " #R" + raw_alloc_id via linked list lookup
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_hash_r");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, ptrSlot); // ptr
    _b.Call("__mm_raw_id_lookup"); // Scratch0 = raw_alloc_id
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("mm_trace_print_i64");
    EmitTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  // =========================================================================
  // Trace depth helpers — increment/decrement __mm_trace_depth for
  // hierarchical indentation of child operations.
  // =========================================================================

  private void EmitTraceDepthInc() {
    _b.LoadGlobal(VReg.Scratch0, "__mm_trace_depth");
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal("__mm_trace_depth", VReg.Scratch0);
  }

  private void EmitTraceDepthDec() {
    _b.LoadGlobal(VReg.Scratch0, "__mm_trace_depth");
    _b.SubRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal("__mm_trace_depth", VReg.Scratch0);
  }

  /// <summary>
  /// Emit trace from a packed_id stored in a stack slot (for mm_alloc where the
  /// pointer doesn't exist yet): indent + tagLabel + "TypeName #N" [+ " size=S"] [+ " [scope]"] + "\n"
  /// </summary>
  private void EmitInlineTraceFromPackedId(string tagLabel, string uniquePrefix,
      int packedIdSlot, int scopeSlot, int? sizeSlot = null) {
    if (Compiler.MmTraceRawOnly) return; // raw-only: suppress managed alloc/realloc lines
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, tagLabel);
    _b.Call("mm_trace_print_tag");
    // Print type name from tag_index (low 16 bits of packed_id)
    _b.LoadLocal(VReg.Scratch0, packedIdSlot);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.MovRegImm(VReg.Scratch1, 0xFFFF);
    _b.AndRegReg(VReg.Arg0, VReg.Scratch1);
    _b.Call("mm_tag_lookup"); // Ret = cstr pointer
    _b.MovRegReg(VReg.Arg0, VReg.Ret);
    _b.Call("mm_trace_print_tag");
    // Print " #N" from alloc_id (upper bits >> 16)
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_hash");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Scratch0, packedIdSlot);
    _b.ShrRegImm(VReg.Scratch0, 16);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("mm_trace_print_i64");
    if (sizeSlot.HasValue) EmitTraceSize(sizeSlot.Value);
    EmitTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  // =========================================================================
  // Immortal fast-path shared by mm_incref and mm_decref.
  //
  // Emits: if [user_ptr - 8] == MmImmortalRefcount, return immediately (the object is a
  // shared static-literal record — incref/decref are no-ops and it is never freed). Assumes
  // user_ptr is in slot 0 and already known non-null. Clobbers Scratch0/1/2; both callers
  // reload what they need afterwards.
  // =========================================================================
  private void EmitImmortalReturnGuard(string labelPrefix) {
    _b.LoadLocal(VReg.Scratch0, 0);                          // Scratch0 = user_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffRefcount); // Scratch1 = refcount
    _b.MovRegImm(VReg.Scratch2, MmImmortalRefcount);
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    var notImmortal = UniqueLabel(labelPrefix + "_not_immortal");
    _b.JumpIf(Condition.NotEqual, notImmortal);
    _b.FunctionEnd();                                        // immortal: no-op return
    _b.DefineLabel(notImmortal);
  }

  // =========================================================================
  // mm_incref(user_ptr, [scope_cstr]) -> void
  // Increments refcount at [ptr-8]. Panics on NULL pointer.
  // =========================================================================
  // Stack slots: 0=user_ptr, 1=scope_cstr (trace only)
  public void EmitMmIncref(bool mmTrace) {
    bool ds = Compiler.DebugStream;
    _b.FunctionStart("mm_incref", (mmTrace || ds) ? 2 : 1, 0x30);

    // NULL check -- panic
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    var notNull = UniqueLabel("mm_incref_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_incref_null");
    _b.Call("mrt_panic");
    _b.DefineLabel(notNull);

    // IMMORTAL fast-path: a static-literal record carries MmImmortalRefcount in its refcount
    // slot. Incrementing a shared immortal object is a no-op — return before touching the
    // atomic. One extra load+movabs+cmp+branch on the hot path (measured negligible).
    EmitImmortalReturnGuard("mm_incref_immortal");

    // Atomic increment refcount at [user_ptr - 8]
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.AtomicInc(VReg.Scratch0, MmOffRefcount);

    // Trace incref after increment
    if (mmTrace) {
      EmitInlineTrace("__mm_tag_incref", UniqueLabel("mm_incref_trace"),
        ptrSlot: 0, scopeSlot: 1);
    }

    if (ds) {
      // __ds_emit_mm_refcount(event_type=DsEvMmIncref, packed_id, new_refcount, scope_ptr)
      _b.MovRegImm(VReg.Arg0, DsEvMmIncref);
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Arg1, VReg.Scratch0, MmOffPackedId); // packed_id
      _b.LoadIndirect(VReg.Arg2, VReg.Scratch0, MmOffRefcount); // new refcount
      _b.LoadLocal(VReg.Arg3, 1); // scope_ptr
      _b.Call("__ds_emit_mm_refcount");
    }

    _b.FunctionEnd();
  }

  // =========================================================================
  // mm_decref(user_ptr, [scope_cstr]) -> void
  //
  // Decrements refcount at [ptr-8]. If rc reaches 0:
  //   1. Load destructor from [ptr-16], if non-zero call it with ptr in arg0
  //   2. Call mm_free(ptr, scope=NULL)
  // Panics on NULL or refcount underflow.
  // =========================================================================
  // Stack slots: 0=user_ptr, 1=scope_cstr (trace only)
  public void EmitMmDecref(bool mmTrace, bool mmDebug = false) {
    bool ds = Compiler.DebugStream;
    _b.FunctionStart("mm_decref", (mmTrace || ds) ? 2 : 1, 0x30);

    // NULL check -- panic
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    var notNull = UniqueLabel("mm_decref_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_decref_null");
    _b.Call("mrt_panic");
    _b.DefineLabel(notNull);

    // Invalid pointer guard: catch pointers that obviously aren't heap addresses.
    // Rejects negatives (kernel space, -1 sentinel, etc.) via a signed compare,
    // then also rejects small positive values (< 0x10000 — the kernel reserves
    // the first 64 KiB so no userspace heap can ever live there; seeing one of
    // these means a non-pointer value was misrouted into decref, e.g. a small
    // integer from a match binding that the scope-end cleanup mistakenly
    // classified as managed).
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ZeroReg(VReg.Scratch1);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    var ptrNotNegative = UniqueLabel("mm_decref_ptr_positive");
    _b.JumpIf(Condition.Greater, ptrNotNegative);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_decref_bad_ptr");
    _b.Call("mrt_panic");
    _b.DefineLabel(ptrNotNegative);
    if (mmDebug) {
      _b.LoadLocal(VReg.Scratch0, 0);
      _b.MovRegImm(VReg.Scratch1, 0x10000);
      _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
      var ptrAboveLow = UniqueLabel("mm_decref_ptr_above_low");
      _b.JumpIf(Condition.AboveEqual, ptrAboveLow);
      _b.LeaSymdata(VReg.Arg0, "__mm_panic_decref_bad_ptr");
      _b.Call("mrt_panic");
      _b.DefineLabel(ptrAboveLow);
    }
    // Debug-only: also catch use-after-free poison pattern
    if (mmDebug) {
      _b.LoadLocal(VReg.Scratch0, 0);
      _b.MovRegImm(VReg.Scratch1, unchecked((long)0xDEADDEADDEADDEAD));
      _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
      var notPoison = UniqueLabel("mm_decref_not_poison");
      _b.JumpIf(Condition.NotEqual, notPoison);
      _b.LeaSymdata(VReg.Arg0, "__mm_panic_decref_bad_ptr");
      _b.Call("mrt_panic");
      _b.DefineLabel(notPoison);
    }

    // IMMORTAL fast-path: a static-literal record carries MmImmortalRefcount in its refcount
    // slot. Decrementing it is a no-op — return before the trace, the underflow check, the atomic
    // dec, the destructor, and mm_free, so a shared immortal is never counted and never freed.
    // Placed AFTER the invalid-pointer guards: this loads [ptr-8], and those guards (which only
    // compare the pointer VALUE) must reject a misrouted non-pointer — e.g. a negative sentinel —
    // before anything dereferences it, or the load here would fault instead of panicking cleanly.
    // An immortal record is a positive high .data address, so it passes every guard untouched.
    EmitImmortalReturnGuard("mm_decref_immortal");

    // Trace decref before modifying refcount (prints rc-1)
    if (mmTrace) {
      EmitInlineTrace("__mm_tag_decref", UniqueLabel("mm_decref_trace"),
        ptrSlot: 0, scopeSlot: 1, printRc: true, rcSubtract: 1);
    }

    // Check refcount underflow: if [ptr-8] == 0, panic (double-free)
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffRefcount); // Scratch1 = refcount
    var hasRefs = UniqueLabel("mm_decref_has_refs");
    _b.JumpIfNonZero(VReg.Scratch1, hasRefs);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_decref_underflow");
    _b.Call("mrt_panic");
    _b.DefineLabel(hasRefs);

    // Atomic decrement refcount: [ptr-8] -= 1
    // AtomicDec sets zero flag when result reaches 0
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    _b.AtomicDec(VReg.Scratch0, MmOffRefcount);

    if (ds) {
      // Emit decref event (after decrement, new rc = [ptr-8])
      _b.MovRegImm(VReg.Arg0, DsEvMmDecref);
      _b.LoadLocal(VReg.Scratch0, 0);
      _b.LoadIndirect(VReg.Arg1, VReg.Scratch0, MmOffPackedId);
      _b.LoadIndirect(VReg.Arg2, VReg.Scratch0, MmOffRefcount);
      _b.LoadLocal(VReg.Arg3, 1);
      _b.Call("__ds_emit_mm_refcount");
    }

    // If refcount > 0 after decrement, we're done
    var done = UniqueLabel("mm_decref_done");
    if (ds) {
      // The ds call above clobbered flags, so re-read refcount
      _b.LoadLocal(VReg.Scratch0, 0);
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffRefcount);
      _b.JumpIfNonZero(VReg.Scratch1, done);
    } else {
      // AtomicDec set zero flag — use it directly
      _b.JumpIf(Condition.NotEqual, done);
    }

    // refcount == 0: call destructor if non-null, then free
    if (mmTrace) EmitTraceDepthInc();
    if (ds) {
      _b.MovRegImm(VReg.Arg0, DsEvDepthInc);
      _b.Call("__ds_emit_depth");
    }

    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffDestructor); // Scratch1 = destructor_fn_ptr
    var noDestructor = UniqueLabel("mm_decref_no_destructor");
    _b.JumpIfZero(VReg.Scratch1, noDestructor);

    // Call destructor(user_ptr)
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = user_ptr
    _b.CallIndirect(VReg.Scratch1);

    _b.DefineLabel(noDestructor);

    // mm_free(user_ptr, scope=NULL)
    _b.LoadLocal(VReg.Arg0, 0);
    if (mmTrace || ds) _b.ZeroReg(VReg.Arg1); // scope = NULL
    _b.Call("mm_free");

    if (mmTrace) EmitTraceDepthDec();
    if (ds) {
      _b.MovRegImm(VReg.Arg0, DsEvDepthDec);
      _b.Call("__ds_emit_depth");
    }

    _b.DefineLabel(done);
    _b.FunctionEnd();
  }

  // =========================================================================
  // Memory-traffic counter updates (see EmitMmGlobals for the design and the
  // measurements that forced it)
  // =========================================================================

  // The per-P counter table: one CACHE LINE per P, so two Ps accumulating at the same time
  // never share a line. Three cumulative counters live in it, at the offsets below.
  // Allocated and zeroed by __slab_init, which is also the only place that knows
  // __sched_max_procs — hence the table's size lives there and its shape lives here.
  public const string MmBytesByPLabel = "__mm_bytes_by_p";
  public const int MmBytesPerPStrideShift = 6;                       // 64 bytes
  public const int MmBytesPerPStride = 1 << MmBytesPerPStrideShift;
  public const int MmBytesOffTracked = 0;
  public const int MmBytesOffRaw = 8;

  // THE CUMULATIVE COUNT OF RAW ALLOCATIONS, and it is here rather than in an atomic word
  // for the same reason the byte counters are: mm_raw_alloc is the hot path.
  //
  // WHY IT EXISTS AT ALL. The tracked layer's cumulative count is __mm_alloc_id_counter,
  // which mm_alloc must bump anyway to mint an id — so counting tracked objects was free and
  // it was done. The RAW layer had only a LIVE count (__mm_raw_alloc_count, for the leak
  // check) and no cumulative one, so `allocs` — which is read as "how many allocations did
  // this program make" — could not see a single array buffer, nor a single REGROW of one.
  // Measured: changing the Array growth policy moved the byte column 22% at the top rung and
  // the alloc column by ZERO, at every rung. That is the same argument the byte counters were
  // added under (a bytes figure that omitted the raw layer "would report a compiler's memory
  // traffic as very nearly flat"), and it applies to the COUNT exactly as it did to the
  // volume: array growth IS reallocation, and reallocation lives entirely in this layer.
  public const int MmCountOffRaw = 16;

  // The shared fallback words, for allocations with no P to accumulate into: raw OS
  // threads (no TLS P) and anything allocated before __slab_init has built the table.
  // Genuinely shared, hence genuinely atomic — and off the hot path, so the cost is moot.
  private const string MmAllocBytesNoPLabel = "__mm_alloc_bytes_no_p";
  private const string MmRawAllocBytesNoPLabel = "__mm_raw_alloc_bytes_no_p";
  private const string MmRawAllocTotalNoPLabel = "__mm_raw_alloc_total_no_p";

  /// <summary>
  /// Add to one of this P's cumulative counters — a plain, unlocked add, because the P owns
  /// the slot (see EmitMmGlobals). Falls back to an atomic add on a shared word when there is
  /// no P, or before the table exists.
  ///
  /// <paramref name="sizeSlot"/> names the stack slot holding the amount to add; pass null to
  /// add ONE, which is how the allocation COUNTERS use it and how the BYTE counters do not.
  ///
  /// Cumulative by construction: nothing on the free path ever undoes it.
  ///
  /// Clobbers Scratch0/1/2.
  /// </summary>
  private void EmitPerPCumulativeAdd(int fieldOffset, string fallbackGlobal, int? sizeSlot) {
    var fallback = UniqueLabel("mm_counter_no_p");
    var done = UniqueLabel("mm_counter_done");

    _b.LoadGlobal(VReg.Scratch1, MmBytesByPLabel);
    _b.JumpIfZero(VReg.Scratch1, fallback); // pre-__slab_init allocation
    _b.LoadCurrentP(VReg.Scratch0);
    _b.JumpIfZero(VReg.Scratch0, fallback); // raw OS thread, no P

    // slot = table + p->id * MmBytesPerPStride
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, GtLayout.POffId);
    _b.ShlRegImm(VReg.Scratch0, MmBytesPerPStrideShift);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);

    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, fieldOffset);
    EmitLoadAddend(sizeSlot);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch0, fieldOffset, VReg.Scratch1);
    _b.Jump(done);

    _b.DefineLabel(fallback);
    EmitLoadAddend(sizeSlot);
    _b.LeaGlobal(VReg.Scratch0, fallbackGlobal);
    _b.AtomicXadd(VReg.Scratch0, 0, VReg.Scratch2);

    _b.DefineLabel(done);
  }

  /// <summary>The amount EmitPerPCumulativeAdd adds, into Scratch2: the i64 in a stack slot
  /// for a byte volume, or the literal 1 for an allocation count.</summary>
  private void EmitLoadAddend(int? sizeSlot) {
    if (sizeSlot is int slot) {
      _b.LoadLocal(VReg.Scratch2, slot);
    } else {
      _b.MovRegImm(VReg.Scratch2, 1);
    }
  }

  // =========================================================================
  // mm_free(user_ptr, [scope_cstr]) -> void
  //
  // Decrements __mm_alloc_count and releases OS pages for (user_ptr - MmHeaderSize).
  // Does NOT call destructor or check refcount — caller handles that.
  // Silently returns if user_ptr is NULL.
  // =========================================================================
  // Stack slots: 0=user_ptr, 1=scope_cstr (trace only)
  public void EmitMmFree(bool mmTrace, bool mmDebug) {
    bool ds = Compiler.DebugStream;
    _b.FunctionStart("mm_free", (mmTrace || ds) ? 2 : 1, 0x60);

    // NULL check — silently return (mm_decref already ensures non-null, but be safe)
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    var notNull = UniqueLabel("mm_free_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.FunctionEnd();
    _b.DefineLabel(notNull);

    // Trace free
    if (mmTrace) {
      EmitInlineTraceFree(UniqueLabel("mm_free_trace"), ptrSlot: 0, scopeSlot: 1);
    }

    if (ds) {
      // __ds_emit_mm_free(packed_id, scope_ptr)
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, MmOffPackedId);
      _b.LoadLocal(VReg.Arg1, 1); // scope_ptr
      _b.Call("__ds_emit_mm_free");
    }

    // Validate canary in debug mode: [user_ptr + size] must equal MmDebugCanaryValue
    if (mmDebug) {
      // Read alloc_size from [user_ptr - 32]
      _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffAllocSize); // Scratch1 = total_alloc_size
      // user_size = total_alloc_size - MmHeaderSize - 8 (canary)
      _b.SubRegImm(VReg.Scratch1, MmHeaderSize + 8);
      // canary_ptr = user_ptr + user_size
      _b.LoadLocal(VReg.Scratch0, 0);
      _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // Scratch1 = canary value
      _b.MovRegImm(VReg.Scratch2, MmDebugCanaryValue);
      _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
      var canaryOk = UniqueLabel("mm_free_canary_ok");
      _b.JumpIf(Condition.Equal, canaryOk);
      _b.LeaSymdata(VReg.Arg0, "__mm_panic_canary");
      _b.Call("mrt_panic");
      _b.DefineLabel(canaryOk);
    }

    // Atomic decrement __mm_alloc_count
    _b.LeaGlobal(VReg.Scratch0, "__mm_alloc_count");
    _b.AtomicDec(VReg.Scratch0, 0);

    // Under --mm-debug: atomic decrement __mm_alloc_count_by_tag[tag_index]
    if (mmDebug) {
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffPackedId); // packed_id
      _b.MovRegImm(VReg.Scratch2, 0xFFFF);
      _b.AndRegReg(VReg.Scratch1, VReg.Scratch2); // Scratch1 = tag_index
      _b.ShlRegImm(VReg.Scratch1, 3); // byte offset
      _b.LeaGlobal(VReg.Scratch0, "__mm_alloc_count_by_tag");
      _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
      _b.AtomicDec(VReg.Scratch0, 0);
    }

    // Set tag context and depth for slab/OS traces
    if (mmTrace) {
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, MmOffPackedId); // packed_id
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch0);
      EmitTraceDepthInc();
    }

    // Poison user data area to detect use-after-free (debug only).
    // Write 0xDEADDEADDEADDEAD across the user area, bounded by user_size so we
    // don't stomp neighboring slab slots when user_size < 24 bytes. Writing past
    // user_ptr + user_size would land in the canary (at user_ptr + user_size)
    // or, for small-class slots, in the next slot's data — including that
    // slot's freelist next-pointer if it sits on the freelist, silently
    // corrupting the allocator.
    if (mmDebug) {
      // user_size = alloc_size - MmHeaderSize - 8 (canary); still in Scratch1 from
      // the canary check above, but reload via alloc_size for clarity.
      _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffAllocSize);
      _b.SubRegImm(VReg.Scratch1, MmHeaderSize + 8); // Scratch1 = user_size
      _b.MovRegImm(VReg.Scratch2, unchecked((long)0xDEADDEADDEADDEAD));

      // Poison user[0] if user_size >= 8
      _b.CmpRegImm(VReg.Scratch1, 8);
      var skipPoison0 = UniqueLabel("mm_free_skip_poison0");
      _b.JumpIf(Condition.Below, skipPoison0);
      _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2);
      _b.DefineLabel(skipPoison0);

      // Poison user[8] if user_size >= 16
      _b.CmpRegImm(VReg.Scratch1, 16);
      var skipPoison1 = UniqueLabel("mm_free_skip_poison1");
      _b.JumpIf(Condition.Below, skipPoison1);
      _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch2);
      _b.DefineLabel(skipPoison1);

      // Poison user[16] if user_size >= 24
      _b.CmpRegImm(VReg.Scratch1, 24);
      var skipPoison2 = UniqueLabel("mm_free_skip_poison2");
      _b.JumpIf(Condition.Below, skipPoison2);
      _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch2);
      _b.DefineLabel(skipPoison2);
    }

    // Compute raw_ptr = user_ptr - MmHeaderSize; free via slab allocator
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    _b.SubRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = raw_ptr (slab slot base)
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_free");

    if (mmTrace) {
      EmitTraceDepthDec();
    }

    _b.FunctionEnd();
  }

  // =========================================================================
  // mm_alloc(size, destructor, tag_index, [scope_cstr]) -> user_ptr
  //
  // Allocates a tracked heap object with inline header.
  // OS-allocates (size + MmHeaderSize [+ 8 canary if MmDebug]) zero-initialized bytes.
  // Header layout at raw_ptr:
  //   [raw +  0]: total_alloc_size
  //   [raw +  8]: packed_id = (alloc_id << 16 | tag_index)
  //   [raw + 16]: destructor_fn_ptr
  //   [raw + 24]: refcount = 0
  //   [raw + 32]: user data  <- returned pointer
  // Increments __mm_alloc_count and __mm_alloc_id_counter.
  // =========================================================================
  // Stack slots: 0=size, 1=destructor, 2=tag_index, 3=scope_cstr (trace only)
  //              4=alloc_size (scratch), 5=raw_ptr (scratch), 6=packed_id (trace only)
  public void EmitMmAlloc(bool mmTrace, bool mmDebug) {
    bool ds = Compiler.DebugStream;
    _b.FunctionStart("mm_alloc", (mmTrace || ds) ? 4 : 3, 0x80);

    // Panic if size == 0
    _b.LoadLocal(VReg.Scratch0, 0);
    var sizeOk = UniqueLabel("mm_alloc_size_ok");
    _b.JumpIfNonZero(VReg.Scratch0, sizeOk);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_alloc_zero_size");
    _b.Call("mrt_panic");
    _b.DefineLabel(sizeOk);

    // Compute alloc_size = size + MmHeaderSize (+ 8 for canary if MmDebug)
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = size
    _b.AddRegImm(VReg.Scratch0, mmDebug ? MmHeaderSize + 8 : MmHeaderSize);
    _b.StoreLocal(4, VReg.Scratch0); // slot 4 = alloc_size

    // Atomic increment __mm_alloc_count
    _b.LeaGlobal(VReg.Scratch0, "__mm_alloc_count");
    _b.AtomicInc(VReg.Scratch0, 0);

    // Cumulative tracked bytes. The requested USER size (slot 0), not alloc_size, so the
    // number is identical under --mm-debug — whose canary would otherwise silently add 8
    // bytes to every allocation in the report, making the debug and release builds
    // disagree about a figure the suite gates on.
    EmitPerPCumulativeAdd(MmBytesOffTracked, MmAllocBytesNoPLabel, sizeSlot: 0);

    // Under --mm-debug: atomic increment __mm_alloc_count_by_tag[tag_index]
    if (mmDebug) {
      _b.LeaGlobal(VReg.Scratch0, "__mm_alloc_count_by_tag");
      _b.LoadLocal(VReg.Scratch1, 2); // tag_index
      _b.ShlRegImm(VReg.Scratch1, 3); // byte offset = tag_index * 8
      _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
      _b.AtomicInc(VReg.Scratch0, 0);
    }

    // Atomic fetch-and-increment __mm_alloc_id_counter; Scratch2 = new alloc_id
    _b.MovRegImm(VReg.Scratch2, 1);
    _b.LeaGlobal(VReg.Scratch0, "__mm_alloc_id_counter");
    _b.AtomicXadd(VReg.Scratch0, 0, VReg.Scratch2); // Scratch2 = old value
    _b.AddRegImm(VReg.Scratch2, 1); // Scratch2 = new alloc_id (old + 1)

    // Pack (alloc_id << 16 | tag_index) into Scratch2
    _b.ShlRegImm(VReg.Scratch2, 16); // Scratch2 = alloc_id << 16
    _b.LoadLocal(VReg.Scratch1, 2);  // Scratch1 = tag_index
    _b.OrRegReg(VReg.Scratch2, VReg.Scratch1); // Scratch2 = packed_id
    _b.StoreLocal(6, VReg.Scratch2); // slot 6 = packed_id (preserved across slab_alloc)

    // Trace mm_alloc BEFORE slab call (top-down order: mm → sl → os)
    if (mmTrace) {
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch2); // set tag context for slab/OS traces
      EmitInlineTraceFromPackedId("__mm_tag_alloc", UniqueLabel("mm_alloc_trace"),
        packedIdSlot: 6, scopeSlot: 3, sizeSlot: 0);
      EmitTraceDepthInc();
    }

    if (ds) {
      // __ds_emit_mm_alloc(packed_id, size, scope_ptr)
      _b.LoadLocal(VReg.Arg0, 6); // packed_id
      _b.LoadLocal(VReg.Arg1, 0); // size
      _b.LoadLocal(VReg.Arg2, 3); // scope_ptr
      _b.Call("__ds_emit_mm_alloc");
    }

    // Allocate alloc_size bytes via slab allocator (zero-initialized)
    _b.LoadLocal(VReg.Arg0, 4); // Arg0 = alloc_size
    _b.Call("__slab_alloc"); // Scratch0 = raw_ptr (slab slot base)
    _b.StoreLocal(5, VReg.Scratch0); // slot 5 = raw_ptr

    if (mmTrace) {
      EmitTraceDepthDec();
    }

    // Store total_alloc_size at [raw + 0]
    _b.LoadLocal(VReg.Scratch0, 5);  // Scratch0 = raw_ptr
    _b.LoadLocal(VReg.Scratch1, 4);  // Scratch1 = alloc_size
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // [raw + 0] = alloc_size

    // Store packed_id at [raw + 8] (loaded from saved slot)
    _b.LoadLocal(VReg.Scratch2, 6); // Scratch2 = packed_id
    _b.LoadLocal(VReg.Scratch0, 5); // Scratch0 = raw_ptr
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch2); // [raw + 8] = packed_id

    // Store destructor at [raw + 16]
    _b.LoadLocal(VReg.Scratch1, 1); // Scratch1 = destructor
    _b.LoadLocal(VReg.Scratch0, 5); // Scratch0 = raw_ptr
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1); // [raw + 16] = destructor

    // Store refcount = 0 at [raw + 24]
    _b.LoadLocal(VReg.Scratch0, 5); // Scratch0 = raw_ptr
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 24, VReg.Scratch1); // [raw + 24] = 0

    // Write canary at [user_ptr + size] (MmDebug only)
    if (mmDebug) {
      _b.LoadLocal(VReg.Scratch0, 5);  // Scratch0 = raw_ptr
      _b.AddRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = user_ptr
      _b.LoadLocal(VReg.Scratch1, 0);  // Scratch1 = size
      _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // Scratch0 = user_ptr + size = canary addr
      _b.MovRegImm(VReg.Scratch1, MmDebugCanaryValue);
      _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // [canary_addr] = canary
    }

    // Compute user_ptr = raw + MmHeaderSize
    _b.LoadLocal(VReg.Scratch0, 5); // Scratch0 = raw_ptr
    _b.AddRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = user_ptr
    _b.StoreLocal(6, VReg.Scratch0); // slot 6 = user_ptr (reuse packed_id slot)

    // NO zero-fill here. __slab_alloc now guarantees zeroed memory, so the user
    // area is already NULL — which is what array.set on fresh capacity slots
    // relies on (it decrefs the old occupant, and a garbage pointer there would
    // fault). Zeroing again would be a second pass over the whole allocation.

    // Return user_ptr
    _b.LoadLocal(VReg.Scratch0, 6);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // mm_realloc(user_ptr, old_size, new_size, [scope_cstr]) -> new_user_ptr
  //
  // Reallocates a managed allocation. Preserves the header (packed_id, destructor, refcount).
  // If user_ptr is NULL, delegates to mm_alloc(new_size, destructor=0, tag=0).
  // =========================================================================
  // Stack slots: 0=user_ptr, 1=old_size, 2=new_size, 3=scope_cstr (trace only, passed from call site)
  //              4=new_alloc_size (scratch), 5=new_raw_ptr (scratch), 6=new_user_ptr (scratch)
  //              7=packed_id (trace only)
  public void EmitMmRealloc(bool mmTrace, bool mmDebug) {
    _b.FunctionStart("mm_realloc", mmTrace ? 4 : 3, 0x90);

    // Panic if new_size == 0
    _b.LoadLocal(VReg.Scratch0, 2);
    var sizeOk = UniqueLabel("mm_realloc_size_ok");
    _b.JumpIfNonZero(VReg.Scratch0, sizeOk);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_realloc_zero_size");
    _b.Call("mrt_panic");
    _b.DefineLabel(sizeOk);

    // If ptr == NULL, delegate to mm_alloc(new_size, destructor=0, tag=0, scope)
    _b.LoadLocal(VReg.Scratch0, 0);
    var notNull = UniqueLabel("mm_realloc_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);

    _b.LoadLocal(VReg.Arg0, 2); // new_size
    _b.ZeroReg(VReg.Arg1);      // destructor = 0
    _b.ZeroReg(VReg.Arg2);      // tag_index = 0
    if (mmTrace) {
      _b.LeaSymdata(VReg.Arg3, "__mm_scope_realloc");
    }
    _b.Call("mm_alloc");
    _b.FunctionEnd(); // return (RAX/X0 = result from mm_alloc)

    _b.DefineLabel(notNull);

    // Compute new_alloc_size = new_size + MmHeaderSize (+ 8 canary if MmDebug)
    _b.LoadLocal(VReg.Scratch0, 2); // Scratch0 = new_size
    _b.AddRegImm(VReg.Scratch0, mmDebug ? MmHeaderSize + 8 : MmHeaderSize);
    _b.StoreLocal(4, VReg.Scratch0); // slot 4 = new_alloc_size

    // Cumulative tracked bytes. A realloc allocates a whole new block and memcpy's into
    // it, so it contributes the FULL new_size, not the growth: that is real traffic, and
    // it is exactly the traffic a quadratic in array growth would show up as. mm_alloc is
    // not on this path (the block comes straight from __slab_alloc_raw below), so nothing
    // else would account for it.
    EmitPerPCumulativeAdd(MmBytesOffTracked, MmAllocBytesNoPLabel, sizeSlot: 2);

    // A REALLOC IS AN ALLOCATION, AND IT IS COUNTED AS ONE — for the same reason its bytes
    // are. This path takes a fresh block off the slab and frees the old one, so a counter
    // that billed the bytes and not the COUNT would report one half of a real event, and
    // nothing else on this path would account for it (mm_alloc is not called here).
    //
    // The LIVE count (__mm_alloc_count) is deliberately NOT touched: one block was born as
    // one died, so the number of live objects is unchanged and the leak check that reads it
    // stays exact. That is also what makes the free side come out right with no free counter
    // at all — the probes derive `frees = Δtotal − Δlive` (PhaseProbe.elapsed), so a realloc
    // reports as precisely what it is: one allocation and one free.
    //
    // It consumes an alloc id it never assigns. The reallocated block KEEPS its original
    // packed_id — a realloc MOVES an object, it does not create one, and the trace must be
    // able to follow it across the move — so ids stay unique and monotone but stop being
    // dense. Nothing indexes them; the trace and the leak report only print them.
    //
    // (ARRAY GROWTH DOES NOT COME THROUGH HERE. `__ManagedMemory.grow` lowers to
    // mm_raw_realloc, because an array's element buffer is a RAW, header-free allocation;
    // this function reallocates a TRACKED, header-carrying one. The raw layer's counterpart
    // to this count is MmCountOffRaw, and that is the one array growth moves.)
    _b.LeaGlobal(VReg.Scratch0, "__mm_alloc_id_counter");
    _b.AtomicInc(VReg.Scratch0, 0);

    // Trace mm_realloc BEFORE slab calls (top-down order)
    if (mmTrace) {
      // Read packed_id from old allocation header [user_ptr - 24]
      _b.LoadLocal(VReg.Scratch0, 0);
      _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, MmOffPackedId);
      _b.StoreLocal(7, VReg.Scratch0); // slot 7 = packed_id
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch0);
      EmitInlineTraceFromPackedId("__mm_tag_realloc", UniqueLabel("mm_realloc_trace"),
        packedIdSlot: 7, scopeSlot: 3, sizeSlot: 2);
      EmitTraceDepthInc();
    }

    // Allocate the new block WITHOUT zeroing (Go's growslice: mallocgc with
    // needzero=false). Every byte of it is written before anyone can read it —
    // the maxon_memcpy below fills [0, MmHeaderSize + old_size) and the tail
    // zero at the end of this function fills [old_size, new_size). Asking the
    // slab to zero it first would be a wasted full pass over the buffer.
    //
    // This is one of only two audited __slab_alloc_raw callers. See the audit
    // rule on RuntimeEmitter.EmitSlabAllocRaw before adding another.
    _b.LoadLocal(VReg.Arg0, 4); // Arg0 = new_alloc_size
    _b.Call("__slab_alloc_raw"); // Scratch0 = new_raw_ptr
    _b.StoreLocal(5, VReg.Scratch0); // slot 5 = new_raw_ptr

    if (mmTrace) {
      EmitTraceDepthDec();
    }

    // Copy header + old data from old block to new block
    // old_raw = user_ptr - MmHeaderSize
    _b.LoadLocal(VReg.Scratch0, 0);  // Scratch0 = user_ptr
    _b.SubRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = old_raw
    _b.MovRegReg(VReg.Arg1, VReg.Scratch0);   // Arg1 = old_raw (src)
    // copy_size = MmHeaderSize + min(old_size, new_size)
    // For simplicity: copy MmHeaderSize + old_size (caller guarantees new_size >= old content)
    _b.LoadLocal(VReg.Scratch1, 1); // Scratch1 = old_size
    _b.AddRegImm(VReg.Scratch1, MmHeaderSize); // Scratch1 = copy_size
    _b.MovRegReg(VReg.Arg2, VReg.Scratch1);   // Arg2 = copy_size
    _b.LoadLocal(VReg.Scratch0, 5);  // Scratch0 = new_raw_ptr
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);   // Arg0 = new_raw_ptr (dst)
    _b.Call("maxon_memcpy");

    // Update total_alloc_size in new header [new_raw + 0]
    _b.LoadLocal(VReg.Scratch0, 5); // Scratch0 = new_raw_ptr
    _b.LoadLocal(VReg.Scratch1, 4); // Scratch1 = new_alloc_size
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // [new_raw + 0] = new_alloc_size

    // Free old block via slab allocator
    if (mmTrace) {
      // Restore tag context (may have been clobbered by slab_alloc traces)
      _b.LoadLocal(VReg.Scratch0, 7); // packed_id
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch0);
      EmitTraceDepthInc();
    }
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    _b.SubRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = old_raw (slab slot base)
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_free");
    if (mmTrace) {
      EmitTraceDepthDec();
    }

    // Write canary at [new_user_ptr + new_size] (MmDebug only)
    if (mmDebug) {
      _b.LoadLocal(VReg.Scratch0, 5);  // Scratch0 = new_raw_ptr
      _b.AddRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = new_user_ptr
      _b.LoadLocal(VReg.Scratch1, 2);  // Scratch1 = new_size
      _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // Scratch0 = canary_addr
      _b.MovRegImm(VReg.Scratch1, MmDebugCanaryValue);
      _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);
    }

    // Zero the GROWN TAIL [old_size, new_size). This is the other half of the
    // __slab_alloc_raw bargain above: memcpy filled the prefix, this fills the
    // rest, and between them every byte of the new buffer is written. It is not
    // redundant work — it is what MAKES the raw allocation safe.
    //
    // It is also load-bearing on its own terms: array.set decrefs the old
    // occupant of a slot, so fresh capacity must read as NULL, not garbage.
    //
    // ptr  = new_user_ptr + old_size
    // size = new_size - old_size
    _b.LoadLocal(VReg.Scratch0, 5); // new_raw_ptr
    _b.AddRegImm(VReg.Scratch0, MmHeaderSize); // new_user_ptr
    _b.LoadLocal(VReg.Scratch1, 1); // old_size
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // ptr
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.LoadLocal(VReg.Scratch0, 2); // new_size
    _b.LoadLocal(VReg.Scratch1, 1); // old_size
    _b.SubRegReg(VReg.Scratch0, VReg.Scratch1); // size = new_size - old_size
    _b.MovRegReg(VReg.Arg1, VReg.Scratch0);
    _b.Call("__slab_memzero");

    // Compute new_user_ptr = new_raw + MmHeaderSize
    _b.LoadLocal(VReg.Scratch0, 5); // Scratch0 = new_raw_ptr
    _b.AddRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = new_user_ptr

    // Return new_user_ptr
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // Raw allocator: mm_raw_alloc, mm_raw_free, mm_raw_realloc
  //
  // These provide untracked memory allocation (no refcount header) used by
  // green thread stacks, scheduler structures, I/O buffers, etc.
  // Unified across x86 and ARM64: both use OsAllocPages/OsFreePages with a
  // 16-byte hidden header storing the total allocation size.
  //
  // Layout:
  //   [base +  0]: total_alloc_size  (8 bytes, for OsFreePages on munmap)
  //   [base +  8]: (reserved/padding for 16-byte alignment)
  //   [base + 16]: user data         <- returned pointer
  //
  // mm_raw_free reads size from [ptr - 16] and frees [ptr - 16].
  // =========================================================================

  // =========================================================================
  // mm_raw_alloc(size, [scope_cstr]) -> ptr
  //
  // Allocates memory via the slab allocator (__slab_alloc). The slab allocator
  // handles the 16-byte hidden header internally. Returns NULL if size == 0.
  //
  // Header layout (managed by __slab_alloc / __slab_free):
  //   [ptr - 16]: span_ptr (small) or total_alloc_size (large)
  //   [ptr -  8]: raw_alloc_id << 8 | flags (0=slab, 1=arena-large, 2=OS-direct)
  //   [ptr     ]: user data  <- returned pointer
  // =========================================================================
  // Stack slots: 0=size, 1=scope_cstr (trace only), 2=result_ptr, 3=raw_alloc_id (trace only)
  public void EmitMmRawAlloc(bool mmTrace) {
    _b.FunctionStart("mm_raw_alloc", mmTrace ? 2 : 1, mmTrace ? 0x50 : 0x30);

    // size == 0 -> return NULL
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = size
    var sizeOk = UniqueLabel("mm_raw_alloc_size_ok");
    _b.JumpIfNonZero(VReg.Scratch0, sizeOk);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(sizeOk);

    // Atomic increment __mm_raw_alloc_count
    _b.LeaGlobal(VReg.Scratch0, "__mm_raw_alloc_count");
    _b.AtomicInc(VReg.Scratch0, 0);

    // Cumulative raw bytes. This is where a compiler's byte VOLUME actually lives: array
    // element buffers and string bytes are header-free raw buffers, and the tracked layer
    // above sees only their 8- and 24-byte handles.
    EmitPerPCumulativeAdd(MmBytesOffRaw, MmRawAllocBytesNoPLabel, sizeSlot: 0);

    // Cumulative raw allocation COUNT — the twin of the volume above, and the only counter
    // that can see array growth: `mm_raw_realloc` grows a buffer by calling straight into
    // this function, so every regrow arrives here and is one allocation. Without it the count
    // above it (`__mm_alloc_id_counter`, tracked-only) reports a growth-policy change as a
    // dead-flat zero while the bytes move by a fifth. See MmCountOffRaw.
    EmitPerPCumulativeAdd(MmCountOffRaw, MmRawAllocTotalNoPLabel, sizeSlot: null);

    if (mmTrace) {
      // Assign raw alloc ID
      _b.MovRegImm(VReg.Scratch2, 1);
      _b.LeaGlobal(VReg.Scratch0, "__mm_raw_alloc_id_counter");
      _b.AtomicXadd(VReg.Scratch0, 0, VReg.Scratch2); // Scratch2 = old value
      _b.AddRegImm(VReg.Scratch2, 1); // Scratch2 = new raw_alloc_id
      _b.StoreLocal(3, VReg.Scratch2); // save raw_alloc_id

      // Trace mm_raw_alloc BEFORE slab call
      _b.ZeroReg(VReg.Scratch0);
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch0); // no managed tag
      EmitInlineTraceRawAlloc(UniqueLabel("mm_raw_alloc_trace"), sizeSlot: 0, scopeSlot: 1,
        rawIdSlot: 3);
      EmitTraceDepthInc();
    }

    // Delegate to __slab_alloc(size) which handles slab/arena-large/OS-direct dispatch
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = size
    _b.Call("__slab_alloc");
    // Scratch0 = result ptr
    _b.StoreLocal(2, VReg.Scratch0); // save result ptr

    if (mmTrace) {
      EmitTraceDepthDec();
      // Store raw_alloc_id in tracking list (header-free: no [ptr-8] to use)
      _b.LoadLocal(VReg.Arg0, 2); // result ptr
      _b.LoadLocal(VReg.Arg1, 3); // raw_alloc_id
      _b.Call("__mm_raw_id_insert");
    }

    // NO zero-fill here. __slab_alloc already returns zeroed memory, which is
    // what makes it safe for managed array elements in uninitialized capacity
    // slots to be decref'd (they read as NULL, not garbage).

    _b.LoadLocal(VReg.Scratch0, 2);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // mm_raw_free(ptr, [scope_cstr]) -> void
  //
  // Frees memory allocated by mm_raw_alloc via the slab allocator.
  // Delegates to __slab_free which uses the arena map and OS-direct list
  // to determine the free path. Silently returns if ptr == NULL.
  // =========================================================================
  // Stack slots: 0=ptr, 1=scope_cstr (trace only)
  public void EmitMmRawFree(bool mmTrace) {
    _b.FunctionStart("mm_raw_free", mmTrace ? 2 : 1, 0x30);

    // NULL check
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = ptr
    var notNull = UniqueLabel("mm_raw_free_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.FunctionEnd();

    _b.DefineLabel(notNull);

    // Atomic decrement __mm_raw_alloc_count
    _b.LeaGlobal(VReg.Scratch0, "__mm_raw_alloc_count");
    _b.AtomicDec(VReg.Scratch0, 0);

    if (mmTrace) {
      EmitInlineTraceRawFree(UniqueLabel("mm_raw_free_trace"), ptrSlot: 0, scopeSlot: 1);
      _b.ZeroReg(VReg.Scratch0);
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch0); // no managed tag
      EmitTraceDepthInc();
    }

    // Delegate to __slab_free(ptr)
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = ptr
    _b.Call("__slab_free");

    if (mmTrace) {
      EmitTraceDepthDec();
    }

    _b.FunctionEnd();
  }

  // =========================================================================
  // mm_raw_realloc(old_ptr, new_size, managedPtr) -> new_ptr
  //
  // Allocates new_size bytes via mm_raw_alloc, copies old data, frees old.
  // old_byte_size = managedPtr->capacity * managedPtr->element_size,
  // or (capacity + 7) >> 3 when element_size == 0 (bit-packed bool sentinel).
  // =========================================================================
  // Stack slots: 0=old_ptr, 1=new_size, 2=managedPtr, 3=new_ptr, 4=old_byte_size
  //              5=scope (trace only), 6=packed_id (trace only)
  public void EmitMmRawRealloc(bool mmTrace) {
    _b.FunctionStart("mm_raw_realloc", 3, mmTrace ? 0x70 : 0x50);

    // Panic if new_size == 0
    _b.LoadLocal(VReg.Scratch0, 1); // Scratch0 = new_size
    var sizeOk = UniqueLabel("mm_raw_realloc_size_ok");
    _b.JumpIfNonZero(VReg.Scratch0, sizeOk);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_realloc_zero_size");
    _b.Call("mrt_panic");
    _b.DefineLabel(sizeOk);

    // Trace mm_realloc BEFORE child operations (top-down order)
    if (mmTrace) {
      _b.ZeroReg(VReg.Scratch0);
      _b.StoreLocal(5, VReg.Scratch0); // slot 5 = scope = NULL
      // Read packed_id from managedPtr's header: managedPtr is a user_ptr, packed_id at [ptr-24]
      _b.LoadLocal(VReg.Scratch0, 2); // managedPtr
      _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, MmOffPackedId);
      _b.StoreLocal(6, VReg.Scratch0); // slot 6 = packed_id
      EmitInlineTraceFromPackedId("__mm_tag_realloc", UniqueLabel("mm_raw_realloc_trace"),
        packedIdSlot: 6, scopeSlot: 5, sizeSlot: 1);
      EmitTraceDepthInc();
    }

    // Step 1: Allocate new buffer via mm_raw_alloc(new_size, scope=[realloc])
    _b.LoadLocal(VReg.Arg0, 1); // Arg0 = new_size
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_realloc");
    _b.Call("mm_raw_alloc");
    // Return value is in Scratch0 (== Ret)
    _b.StoreLocal(3, VReg.Scratch0); // slot 3 = new_ptr

    // Step 2: Compute old_byte_size from managedPtr->capacity and managedPtr->element_size.
    // At MmemBitPackedElementSize (0), use (capacity + 7) >> 3 instead.
    _b.LoadLocal(VReg.Scratch0, 2); // Scratch0 = managedPtr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffCapacity);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, MmemOffElementSize);
    var notBitPacked = UniqueLabel("mm_raw_realloc_not_bit_packed");
    _b.JumpIfNonZero(VReg.Scratch2, notBitPacked);
    // Bit-packed path: old_byte_size = (capacity + 7) >> 3
    _b.AddRegImm(VReg.Scratch1, 7);
    _b.ShrRegImm(VReg.Scratch1, 3);
    var storeOldSize = UniqueLabel("mm_raw_realloc_store_old_size");
    _b.Jump(storeOldSize);
    _b.DefineLabel(notBitPacked);
    // Normal path: old_byte_size = capacity * element_size
    _b.MulRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.DefineLabel(storeOldSize);
    _b.StoreLocal(4, VReg.Scratch1); // slot 4 = old_byte_size

    // Step 3: memcpy(new_ptr, old_ptr, old_byte_size)
    _b.LoadLocal(VReg.Arg0, 3); // Arg0 = new_ptr (dst)
    _b.LoadLocal(VReg.Arg1, 0); // Arg1 = old_ptr (src)
    _b.LoadLocal(VReg.Arg2, 4); // Arg2 = old_byte_size (count)
    _b.Call("maxon_memcpy");

    // Step 4: Free the old buffer — UNLESS it is INLINE (parent_ptr == MmParentInline). An inline
    // buffer lives inside the record's own allocation (self + recordSize), not a slab slot base, so
    // mm_raw_free'ing it would corrupt the heap. A realloc DETACHES it: new_ptr above is a normal
    // external slab allocation, so the record becomes a plain ROOT owner (parent_ptr = 0) and the
    // old inline bytes simply die with the record's own slot when it is freed.
    _b.LoadLocal(VReg.Scratch0, 2); // Scratch0 = managedPtr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffParent); // Scratch1 = parent_ptr
    _b.CmpRegImm(VReg.Scratch1, MmParentInline);
    var inlineDetach = UniqueLabel("mm_raw_realloc_inline_detach");
    var afterFree = UniqueLabel("mm_raw_realloc_after_free");
    _b.JumpIf(Condition.Equal, inlineDetach);

    // External buffer: free it normally.
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = old_ptr
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_realloc");
    _b.Call("mm_raw_free");
    _b.Jump(afterFree);

    // Inline buffer: skip the free and clear the inline sentinel — the record now owns new_ptr.
    _b.DefineLabel(inlineDetach);
    _b.LoadLocal(VReg.Scratch0, 2); // managedPtr
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, MmemOffParent, VReg.Scratch1); // parent_ptr = 0 (ROOT)
    _b.DefineLabel(afterFree);

    if (mmTrace) {
      EmitTraceDepthDec();
    }

    // Return new_ptr
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // maxon_string_ensure_cap(buffer, length, capacity, requiredCap, parentPtr) -> buffer
  //
  // Ensures a string's backing buffer has at least requiredCap bytes of capacity.
  // Three cases:
  //   1. capacity >= requiredCap: return buffer as-is (no-op)
  //   2. capacity < 0 (rdata/slice): alloc requiredCap bytes, copy length bytes from old buffer
  //   3. capacity < requiredCap (heap): realloc via mm_raw_alloc + memcpy + mm_raw_free
  // Returns the (possibly new) buffer pointer.
  //
  // parentPtr is the record's parent_ptr field. When it is MmParentInline (-3) the old buffer is
  // INLINE in the record's own allocation (not a slab slot base), so it must NOT be mm_raw_free'd
  // even though capacity >= 0 — the grow DETACHES the record to the freshly-allocated external
  // buffer and the caller resets parent_ptr to 0. The inline bytes die with the record's own slot.
  // =========================================================================
  // Stack slots: 0=buffer, 1=length, 2=capacity, 3=requiredCap, 4=parentPtr
  //              5=new_buffer (scratch)
  public void EmitStringEnsureCap(bool mmTrace) {
    _b.FunctionStart("maxon_string_ensure_cap", 5, mmTrace ? 0x60 : 0x50);

    // If capacity < 0 (signed), always need growth:
    //   capacity == -2 (rdata) or capacity == -1 (slice) can't be used in-place
    _b.LoadLocal(VReg.Scratch0, 2); // Scratch0 = capacity
    _b.CmpRegImm(VReg.Scratch0, 0);
    var needGrow = UniqueLabel("str_ensure_need_grow");
    _b.JumpIf(Condition.Less, needGrow); // signed: capacity < 0

    // capacity >= 0: Check if we actually need growth
    _b.LoadLocal(VReg.Scratch1, 3); // Scratch1 = requiredCap
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.Below, needGrow); // unsigned: capacity < requiredCap

    // No growth needed — return existing buffer
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ReturnValue(VReg.Scratch0);

    // Needs growth: allocate new buffer of requiredCap bytes
    _b.DefineLabel(needGrow);
    _b.LoadLocal(VReg.Arg0, 3); // Arg0 = requiredCap
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_realloc");
    _b.Call("mm_raw_alloc");
    _b.StoreLocal(5, VReg.Scratch0); // slot 5 = new_buffer

    // Copy length bytes from old buffer to new buffer
    _b.LoadLocal(VReg.Arg0, 5); // Arg0 = new_buffer (dst)
    _b.LoadLocal(VReg.Arg1, 0); // Arg1 = old_buffer (src)
    _b.LoadLocal(VReg.Arg2, 1); // Arg2 = length (count)
    _b.Call("maxon_memcpy");

    // Free old buffer only if it is an owned heap buffer:
    //   capacity < 0            (rdata/slice)  → don't free (buffer belongs to parent / is static)
    //   parent_ptr == MmParentInline (inline)  → don't free (inline in the record's own slot); the
    //                                             grow has DETACHED to new_buffer, caller resets parent
    _b.LoadLocal(VReg.Scratch0, 2); // Scratch0 = capacity
    _b.CmpRegImm(VReg.Scratch0, 0);
    var skipFree = UniqueLabel("str_ensure_skip_free");
    _b.JumpIf(Condition.Less, skipFree); // signed: capacity < 0 → don't free
    _b.LoadLocal(VReg.Scratch0, 4); // Scratch0 = parentPtr
    _b.CmpRegImm(VReg.Scratch0, MmParentInline);
    _b.JumpIf(Condition.Equal, skipFree); // inline buffer → don't free
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = old_buffer
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_realloc");
    _b.Call("mm_raw_free");
    _b.DefineLabel(skipFree);

    // Return new_buffer
    _b.LoadLocal(VReg.Scratch0, 5);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // maxon_cow_struct_detach(managedPtr, byteLen) -> managedPtr (same or new)
  //
  // Handles struct-level COW when a parent __ManagedMemory has refcount > 1
  // (meaning a slice holds a reference). Allocates a new struct + buffer,
  // copies data, decrefs old struct, returns the new struct pointer.
  // If no detach needed (refcount == 1 or capacity < 0), returns managedPtr unchanged.
  // =========================================================================
  // Stack slots: 0=managedPtr, 1=byteLen, 2=new_struct (scratch), 3=new_buffer (scratch)
  public void EmitCowStructDetach(bool mmTrace) {
    _b.FunctionStart("maxon_cow_struct_detach", 2, mmTrace ? 0x60 : 0x50);

    // Fast path: if capacity < 0, no struct detach needed (buffer COW handles it)
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = managedPtr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16); // Scratch1 = capacity (offset 16 in struct)
    _b.CmpRegImm(VReg.Scratch1, 0);
    var noDetach = UniqueLabel("cow_detach_done");
    _b.JumpIf(Condition.Less, noDetach); // capacity < 0: rdata or slice

    // Check refcount: if refcount == 1, sole owner, no detach needed
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffRefcount); // [managedPtr - 8] = refcount
    _b.CmpRegImm(VReg.Scratch1, 1);
    _b.JumpIf(Condition.Equal, noDetach); // refcount == 1: sole owner

    // --- Struct-level COW: allocate a new record of the SAME SIZE and copy it whole ---
    // The record's size is read from its allocation header (alloc_size = user_size +
    // MmHeaderSize), so a fused String (48 bytes, with a trailing singleByteGraphemesFlag) is preserved
    // rather than truncated to a bare 40-byte __ManagedMemory. The whole user region is
    // memcpy'd, so every field — including singleByteGraphemesFlag and any future trailing field — carries
    // over; only buffer/capacity/parent are then overwritten to make the copy an owner.

    // Step 1: user_size = alloc_size - MmHeaderSize, spilled for the struct-wide copy below.
    _b.LoadLocal(VReg.Scratch0, 0); // managedPtr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffAllocSize); // alloc_size = user_size + header
    _b.SubRegImm(VReg.Scratch1, MmHeaderSize);
    _b.StoreLocal(4, VReg.Scratch1); // slot 4 = user_size

    // Step 2: allocate the new record (same size), carrying the old destructor + tag_index.
    _b.LoadLocal(VReg.Scratch0, 0); // managedPtr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffDestructor); // destructor_fn_ptr
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, MmOffPackedId); // packed_id
    _b.MovRegImm(VReg.Arg0, 0xFFFF);
    _b.AndRegReg(VReg.Scratch0, VReg.Arg0); // Scratch0 = tag_index (lower 16 bits)
    _b.LoadLocal(VReg.Arg0, 4); // size = user_size
    _b.MovRegReg(VReg.Arg1, VReg.Scratch1); // destructor
    _b.MovRegReg(VReg.Arg2, VReg.Scratch0); // tag_index
    if (mmTrace) _b.ZeroReg(VReg.Arg3); // scope = NULL
    _b.Call("mm_alloc");
    _b.StoreLocal(2, VReg.Scratch0); // slot 2 = new_struct

    // Step 3: allocate the new raw buffer (byteLen bytes) and copy the element data into it.
    _b.LoadLocal(VReg.Arg0, 1); // byteLen
    if (mmTrace) _b.ZeroReg(VReg.Arg1); // scope = NULL
    _b.Call("mm_raw_alloc");
    _b.StoreLocal(3, VReg.Scratch0); // slot 3 = new_buffer
    _b.LoadLocal(VReg.Arg0, 3); // dst = new_buffer
    _b.LoadLocal(VReg.Scratch0, 0); // managedPtr
    _b.LoadIndirect(VReg.Arg1, VReg.Scratch0, 0); // src = old buffer (offset 0)
    _b.LoadLocal(VReg.Arg2, 1); // count = byteLen
    _b.Call("maxon_memcpy");

    // Step 4: copy the WHOLE old record into the new one (all fields, whatever the size).
    _b.LoadLocal(VReg.Arg0, 2); // dst = new_struct
    _b.LoadLocal(VReg.Arg1, 0); // src = old managedPtr
    _b.LoadLocal(VReg.Arg2, 4); // count = user_size
    _b.Call("maxon_memcpy");

    // Step 5: make the copy an independent owner of its fresh buffer.
    _b.LoadLocal(VReg.Scratch1, 2); // new struct
    _b.LoadLocal(VReg.Scratch0, 3); // new buffer
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0); // new.buffer = new_buffer
    _b.LoadLocal(VReg.Scratch0, 0); // old managedPtr
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, 8); // old.length
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.StoreIndirect(VReg.Scratch1, 16, VReg.Arg0); // new.capacity = old.length (now owned)
    _b.ZeroReg(VReg.Arg0);
    _b.StoreIndirect(VReg.Scratch1, 32, VReg.Arg0); // new.parentPtr = 0

    // Step 6: Set refcount on new struct to 1 (mm_alloc initializes to 0)
    _b.LoadLocal(VReg.Scratch1, 2); // new struct
    _b.MovRegImm(VReg.Arg0, 1);
    _b.StoreIndirect(VReg.Scratch1, MmOffRefcount, VReg.Arg0); // refcount = 1

    // Step 7: Decref old struct (drops one reference; slices still hold theirs)
    _b.LoadLocal(VReg.Arg0, 0); // old managedPtr
    if (mmTrace) _b.ZeroReg(VReg.Arg1); // scope = NULL
    _b.Call("mm_decref");

    // Return new struct
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.ReturnValue(VReg.Scratch0);

    // No detach needed — return original managedPtr
    _b.DefineLabel(noDetach);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // maxon_current_time_ms() -> i64 (milliseconds since boot/epoch)
  //
  // Simple wrapper around the platform's monotonic clock.
  // =========================================================================
  public void EmitCurrentTimeMs() {
    _b.FunctionStart("maxon_current_time_ms", 0, 0x20);
    _b.GetCurrentTimeMs(VReg.Scratch0, scratchSlot: 0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // maxon_current_time_nanos() -> i64 (monotonic nanoseconds)
  //
  // The high-resolution sibling of maxon_current_time_ms. Both exist: the ms clock
  // reads the coarse OS tick (15.6 ms on Windows), which is the cheaper read and a
  // fine answer to "what time is it, roughly"; this one reads the performance
  // counter, which is what any measurement shorter than a scheduler tick requires.
  //
  // The green-thread timer heap calls THIS one. A coarse tick is not a legal
  // deadline: quantized to ~15.6 ms, a deadline can expire before the requested
  // duration has actually elapsed, which made sleep(30) return in 16 ms. Both
  // maxon_sleep and __gt_timer_check therefore anchor to this clock, so it is a
  // hard dependency of the scheduler, not just of Clock.nowNanos().
  //
  // The frame carries 0x40 bytes rather than the ms clock's 0x20: the POSIX and
  // Win32 entry points both write their result through an out-parameter, so
  // slots 0 and 1 are reserved as that buffer (see IEmitterBackend's
  // GetCurrentTimeNanos scratchSlot contract) on top of the 0x20 call shadow
  // space Windows requires.
  // =========================================================================
  public void EmitCurrentTimeNanos() {
    _b.FunctionStart("maxon_current_time_nanos", 0, 0x40);
    _b.GetCurrentTimeNanos(VReg.Scratch0, scratchSlot: 0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // maxon_current_unix_time_seconds() -> i64 (WALL-CLOCK seconds since 1970-01-01 UTC)
  //
  // The odd one out, and deliberately so. The two clocks above are MONOTONIC: their
  // absolute values are meaningless (milliseconds since boot, performance-counter ticks)
  // and only the difference between two readings means anything. That makes them right
  // for durations and useless for dates — no amount of arithmetic turns "42 seconds since
  // this machine booted" into a calendar day.
  //
  // This one reads the wall clock, so it can answer "what is today's date" and must never
  // be used to measure a duration: the system clock can be stepped backwards (NTP, a user
  // changing the timezone), and a duration computed across such a step comes out negative.
  //
  // The frame carries 0x40 like the nanosecond clock: both platform entry points write
  // their result through an out-parameter (a FILETIME on Windows, a timespec on POSIX), so
  // slots 0 and 1 are reserved as that buffer on top of Windows's 0x20 call shadow space.
  // =========================================================================
  public void EmitCurrentUnixTimeSeconds() {
    _b.FunctionStart("maxon_current_unix_time_seconds", 0, 0x40);
    _b.GetCurrentUnixTimeSeconds(VReg.Scratch0, scratchSlot: 0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // maxon_thread_cpu_ticks() -> i64 (CPU time consumed by the CALLING THREAD)
  //
  // The fourth clock, and the only one that is not a clock: it advances solely while this
  // thread is scheduled on a core. The three above all measure WALL time, so a duration
  // taken with them includes every other process on the box — which is why a compiler
  // phase timed on a busy machine reports the machine. A single scale-test run once read
  // its parse phase at x5.03 then x1.78 across a DOUBLING ladder; that is preemption, not
  // a growth curve, and this counter cannot see preemption at all.
  //
  // It is NOT a retired-instruction count and is not reproducible to the digit — it still
  // moves with turbo, thermal throttling and cache pressure from other cores. It is good to
  // a few percent, against a signal (linear x2 vs quadratic x4) with a 100% margin.
  //
  // ⚠ THE UNIT DIFFERS BY PLATFORM AND NOTHING CONVERTS IT: TSC ticks on Windows,
  // nanoseconds on POSIX. QueryPerformanceFrequency is the performance counter's rate, not
  // the TSC's, so there is no honest normalization to write here — and a dishonest one
  // would be worse than the divergence. Callers compare ratios, which are unit-free.
  //
  // The frame carries 0x40 for the same reason the two clocks above do: both platform entry
  // points write through an out-parameter (a ULONG64 on Windows, a timespec on POSIX), so
  // slots 0 and 1 are that buffer on top of Windows's 0x20 call shadow space.
  // =========================================================================
  public void EmitThreadCpuTicks() {
    _b.FunctionStart("maxon_thread_cpu_ticks", 0, 0x40);
    _b.GetThreadCpuTicks(VReg.Scratch0, scratchSlot: 0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // The memory-traffic counter accessors: one runtime function per counter a caller can
  // read, each a bare load. Together they are what makes a compiler's memory measurable
  // from inside itself, in a RELEASE binary — the same binary whose time is being
  // measured, which is the whole point (--mm-debug and --mm-trace change codegen, so a
  // binary that reports its memory the old way is not the binary under test).
  //
  // Reachable from ordinary Maxon as `__Builtins.mmAllocTotal()` etc. (registered in
  // 2-Parser.cs's CompilerBuiltins table), so no stdlib wrapper is needed.
  //
  // WHY SIX: `frees` is not counted, it is DERIVED as `Δtotal − Δlive` — which needs BOTH a
  // cumulative and a live counter, IN EACH LAYER. So: cumulative + live for tracked objects,
  // cumulative + live for raw buffers, and the two cumulative byte volumes. A caller sums the
  // layers (PhaseProbe does, for counts exactly as it already did for bytes); it is the
  // runtime's job to keep them separate and exact, because only the raw live count can say
  // whether a BUFFER leaked.
  //
  // maxon_mm_raw_alloc_total is the newest, and it is the one that closed the hole: an
  // `allocs` figure summed from the other five could not see an array buffer being allocated
  // OR regrown, so it read zero-change through a growth-policy change that moved 62 MB.
  // =========================================================================
  public void EmitMmCounterAccessors() {
    EmitGlobalReader("maxon_mm_alloc_total", "__mm_alloc_id_counter");
    EmitGlobalReader("maxon_mm_alloc_live", "__mm_alloc_count");
    EmitGlobalReader("maxon_mm_raw_alloc_live", "__mm_raw_alloc_count");
    EmitPerPCounterReader("maxon_mm_alloc_bytes", MmBytesOffTracked, MmAllocBytesNoPLabel);
    EmitPerPCounterReader("maxon_mm_raw_alloc_bytes", MmBytesOffRaw, MmRawAllocBytesNoPLabel);
    EmitPerPCounterReader("maxon_mm_raw_alloc_total", MmCountOffRaw, MmRawAllocTotalNoPLabel);
  }

  /// <summary>A runtime function that returns the i64 held in one global. Shaped exactly
  /// like EmitCurrentTimeNanos: FunctionStart -> read -> ReturnValue.</summary>
  private void EmitGlobalReader(string funcName, string globalLabel) {
    _b.FunctionStart(funcName, 0, 0x20);
    _b.LoadGlobal(VReg.Scratch0, globalLabel);
    _b.ReturnValue(VReg.Scratch0);
  }

  /// <summary>
  /// Sum one per-P counter across every P's slot, plus the no-P fallback word — a byte volume
  /// or an allocation count, the read is the same. This is the read side of the per-P scheme
  /// in EmitMmGlobals: the write side is a plain unlocked add precisely because the read side
  /// pays for it here instead, and reads happen a few dozen times per compile against millions
  /// of allocations.
  ///
  /// Racy against a concurrently-allocating P by construction — it walks the slots one at
  /// a time. That is exactly as racy as asking "how much memory has this program allocated"
  /// of a running program is, and callers that need an exact answer (the scale suite) read
  /// it at a point where they own every thread that allocates.
  ///
  /// Stack slots: 0 = accumulator, 1 = index, 2 = table base.
  /// </summary>
  private void EmitPerPCounterReader(string funcName, int fieldOffset, string fallbackGlobal) {
    _b.FunctionStart(funcName, 0, 0x40);

    // Seed with the no-P total, so a thread that never had a P is never lost.
    _b.LoadGlobal(VReg.Scratch0, fallbackGlobal);
    _b.StoreLocal(0, VReg.Scratch0);

    var done = UniqueLabel("mm_bytes_sum_done");
    _b.LoadGlobal(VReg.Scratch0, MmBytesByPLabel);
    _b.StoreLocal(2, VReg.Scratch0);
    // No table => __slab_init never ran => the fallback word is the whole story.
    _b.JumpIfZero(VReg.Scratch0, done);

    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(1, VReg.Scratch0); // i = 0

    var loop = UniqueLabel("mm_bytes_sum_loop");
    _b.DefineLabel(loop);
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.LoadGlobal(VReg.Scratch1, "__sched_max_procs");
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, done);

    _b.ShlRegImm(VReg.Scratch0, MmBytesPerPStrideShift);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);           // &slot[i]
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, fieldOffset);
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.StoreLocal(0, VReg.Scratch1);                      // acc += slot[i]

    _b.LoadLocal(VReg.Scratch0, 1);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(1, VReg.Scratch0);
    _b.Jump(loop);

    _b.DefineLabel(done);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // maxon_enter_background_priority() -> i64
  //
  // Drops this process to background priority and answers what the OS then
  // reports. Backs `__Builtins.enterBackgroundPriority()`. The answer is a
  // Windows priority class or a POSIX nice value depending on the lane, and
  // the two are not comparable — see IEmitterBackend.EnterBackgroundPriority,
  // which also carries the thread-inheritance contract this leans on.
  // =========================================================================
  public void EmitEnterBackgroundPriority() {
    _b.FunctionStart("maxon_enter_background_priority", 0, 0x20);
    _b.EnterBackgroundPriority(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // maxon_current_process_id() -> i64 (OS process ID, zero-extended)
  //
  // Windows: GetCurrentProcessId. POSIX: getpid. The returned value is
  // stable for the process's lifetime and differs across concurrent
  // processes — stdlib uses it to disambiguate temp-file names when a
  // parent spawns multiple subprocesses that share the same filesystem.
  // =========================================================================
  public void EmitCurrentProcessId() {
    _b.FunctionStart("maxon_current_process_id", 0, 0x20);
    _b.GetCurrentProcessId(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // mm_raw_alloc_260() -> ptr
  //
  // Convenience wrapper: allocates exactly 260 bytes (for path buffers).
  // =========================================================================
  public void EmitMmRawAlloc260(bool mmTrace) {
    _b.FunctionStart("mm_raw_alloc_260", 0, 0x20);
    _b.MovRegImm(VReg.Arg0, 260);
    if (mmTrace) _b.ZeroReg(VReg.Arg1); // scope = NULL
    _b.Call("mm_raw_alloc");
    _b.FunctionEnd();
  }

  // =========================================================================
  // Trace print functions (standalone runtime functions)
  // =========================================================================

  /// <summary>mm_trace_print_tag(cstr_ptr): Write null-terminated C string to stderr.</summary>
  public void EmitMmTracePrintTag() {
    _b.FunctionStart("mm_trace_print_tag", 1, 0x20);
    _b.LoadLocal(VReg.Arg0, 0); // reload cstr ptr
    _b.Call(_b.WriteStderrLabel);
    _b.FunctionEnd();
  }

  /// <summary>mm_trace_print_i64(value): Print 64-bit integer in decimal to stderr.</summary>
  public void EmitMmTracePrintI64() {
    // Slot 0 = value (arg).
    // Slots 4-6 = 24-byte string buffer (3 qword slots = 24 bytes).
    // The buffer must be placed at HIGH slot numbers (low addresses) because
    // maxon_u64_to_string writes bytes at buf[0], buf[1], ..., buf[20] — i.e.,
    // at increasing addresses.  LeaLocal gives the address of the slot, so if
    // we used slot 1 (= rbp-0x10), writing 21 bytes upward would reach rbp+0x0B,
    // corrupting the saved RBP and return address.  Slot 4 (= rbp-0x28) keeps
    // the 24-byte buffer entirely within the 0x60-byte frame.
    _b.FunctionStart("mm_trace_print_i64", 1, 0x60);
    _b.LoadLocal(VReg.Arg0, 0);    // Arg0 = value
    _b.LeaLocal(VReg.Arg1, 4);     // Arg1 = &buf (at rbp-0x28, 24 bytes upward to rbp-0x11)
    _b.Call("maxon_u64_to_string");
    _b.LeaLocal(VReg.Arg0, 4);     // Arg0 = &buf
    _b.Call(_b.WriteStderrLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// mm_trace_print_class(value): Print slab class index to stderr.
  /// Prints "-1" for the sentinel value used by arena-large and OS-direct paths.
  /// </summary>
  public void EmitMmTracePrintClass() {
    _b.FunctionStart("mm_trace_print_class", 1, 0x20);
    _b.LoadLocal(VReg.Scratch0, 0); // value
    _b.CmpRegImm(VReg.Scratch0, -1);
    var notMinusOne = UniqueLabel("mm_trace_class_not_minus_one");
    _b.JumpIf(Condition.NotEqual, notMinusOne);
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_minus_one");
    _b.Call(_b.WriteStderrLabel);
    _b.FunctionEnd();
    _b.DefineLabel(notMinusOne);
    _b.LoadLocal(VReg.Arg0, 0);
    _b.Call("mm_trace_print_i64");
    _b.FunctionEnd();
  }

  /// <summary>mm_trace_print_hex(value): Print 64-bit value as "0xHEX" to stderr.</summary>
  public void EmitMmTracePrintHex() {
    // Stack layout:
    //   Slot 0 = value (arg)
    //   Slot 1 = hex_chars base address (symdata ptr)
    //   Slot 2 = loop counter (15..0)
    //   Slots 6-8 = 24-byte buffer for "0x" + 16 hex chars + null (20 bytes used, 24 allocated)
    // The buffer is placed at high slot numbers (low addresses) because the write_stderr
    // function reads bytes at increasing addresses from the buffer pointer. Placing it at
    // slot 1 would write past rbp, corrupting the saved frame pointer and return address.
    _b.FunctionStart("mm_trace_print_hex", 1, 0x70);

    var bufSlot = 6; // rbp - 0x38; 24 bytes upward reaches rbp - 0x21, safely in-frame

    // Write '0' at buf[0], 'x' at buf[1]
    _b.LeaLocal(VReg.Scratch0, bufSlot);  // Scratch0 = buf base
    _b.MovRegImm(VReg.Scratch1, '0');
    _b.StoreIndirectByte(VReg.Scratch0, 0, VReg.Scratch1);
    _b.MovRegImm(VReg.Scratch1, 'x');
    _b.StoreIndirectByte(VReg.Scratch0, 1, VReg.Scratch1);

    // Load hex_chars address into slot 1
    _b.LeaSymdata(VReg.Scratch0, "__mm_hex_chars");
    _b.StoreLocal(1, VReg.Scratch0);

    // Load value, init loop counter = 15
    _b.LoadLocal(VReg.Scratch0, 0);  // Scratch0 = value
    _b.MovRegImm(VReg.Scratch1, 15);
    _b.StoreLocal(2, VReg.Scratch1); // slot 2 = counter

    var loopLabel = UniqueLabel("mm_trace_hex_loop");
    var doneLabel = UniqueLabel("mm_trace_hex_done");

    _b.DefineLabel(loopLabel);

    // Extract low nibble of value: Scratch2 = value & 0xF
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch0);
    _b.MovRegImm(VReg.Scratch3, 0xF);
    _b.AndRegReg(VReg.Scratch2, VReg.Scratch3);

    // Look up hex char: Scratch2 = hex_chars[Scratch2]
    _b.LoadLocal(VReg.Scratch3, 1);  // hex_chars base
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch2); // &hex_chars[nibble]
    _b.LoadIndirectByte(VReg.Scratch2, VReg.Scratch3, 0); // Scratch2 = char

    // Store at buf[2 + counter]: Scratch3 = buf + 2 + counter
    _b.LeaLocal(VReg.Scratch3, bufSlot);  // buf base
    _b.LoadLocal(VReg.Scratch1, 2); // counter
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch1); // buf + counter
    _b.AddRegImm(VReg.Scratch3, 2);  // buf + 2 + counter
    _b.StoreIndirectByte(VReg.Scratch3, 0, VReg.Scratch2);

    // Shift value right by 4
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ShrRegImm(VReg.Scratch0, 4);
    _b.StoreLocal(0, VReg.Scratch0);

    // counter--; loop while counter >= 0
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.SubRegImm(VReg.Scratch1, 1);
    _b.StoreLocal(2, VReg.Scratch1);
    _b.CmpRegImm(VReg.Scratch1, 0);
    _b.JumpIf(Condition.GreaterEqual, loopLabel);

    // Null-terminate at buf[18]
    _b.LeaLocal(VReg.Scratch0, bufSlot);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirectByte(VReg.Scratch0, 18, VReg.Scratch1);

    // Print
    _b.LeaLocal(VReg.Arg0, bufSlot);
    _b.Call(_b.WriteStderrLabel);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>mm_trace_print_packed_tag(user_ptr): Extract tag index, look up name, print it.</summary>
  public void EmitMmTracePrintPackedTag() {
    _b.FunctionStart("mm_trace_print_packed_tag", 1, 0x30);
    // Load packed_id from [user_ptr - 24]
    _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, MmOffPackedId); // packed_id ([ptr-24])
    // Extract low 16 bits = tag_index
    _b.MovRegImm(VReg.Scratch1, 0xFFFF);
    _b.AndRegReg(VReg.Arg0, VReg.Scratch1);
    _b.Call("mm_tag_lookup"); // Ret = cstr
    _b.MovRegReg(VReg.Arg0, VReg.Ret);
    _b.Call("mm_trace_print_tag");
    _b.FunctionEnd();
  }

  /// <summary>mm_trace_print_indent(): Print 2 spaces for each level of __mm_trace_depth.</summary>
  public void EmitMmTracePrintIndent() {
    _b.FunctionStart("mm_trace_print_indent", 0, 0x30);
    // Load depth -> slot 0
    _b.LoadGlobal(VReg.Scratch0, "__mm_trace_depth");
    _b.StoreLocal(0, VReg.Scratch0);

    var loopLabel = UniqueLabel("mm_trace_indent_loop");
    var doneLabel = UniqueLabel("mm_trace_indent_done");

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.JumpIfZero(VReg.Scratch0, doneLabel);
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_indent");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.SubRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(0, VReg.Scratch0);
    _b.Jump(loopLabel);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>mm_tag_lookup(tag_index): Returns cstr pointer to type name, or "__mm_tag_null".</summary>
  public void EmitMmTagLookup(List<string?> tagTable) {
    _b.FunctionStart("mm_tag_lookup", 1, 0x20);
    _b.LoadLocal(VReg.Scratch0, 0); // tag_index
    for (int i = 1; i < tagTable.Count; i++) {
      var label = tagTable[i];
      if (label == null) continue;
      _b.CmpRegImm(VReg.Scratch0, i);
      var skipLabel = UniqueLabel("mm_tag_lookup_skip");
      _b.JumpIf(Condition.NotEqual, skipLabel);
      _b.LeaSymdata(VReg.Ret, label);
      _b.FunctionEnd(); // return (leaves via ret)
      _b.DefineLabel(skipLabel);
    }
    // Default: return __mm_tag_null
    _b.LeaSymdata(VReg.Ret, "__mm_tag_null");
    _b.FunctionEnd();
  }

  /// <summary>mm_trace_transfer(ptr, scope): Print "transfer TypeName #N rc=N [scope]" to stderr.</summary>
  public void EmitMmTraceTransfer() {
    // Slots: 0=ptr, 1=scope
    _b.FunctionStart("mm_trace_transfer", 2, 0x30);
    if (Compiler.MmTraceRawOnly) { _b.FunctionEnd(); return; } // raw-only: no transfer output
    _b.LoadLocal(VReg.Scratch0, 0); // ptr
    var nullLabel = UniqueLabel("mm_trace_transfer_null");
    _b.JumpIfZero(VReg.Scratch0, nullLabel);
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_transfer");
    _b.Call("mm_trace_print_tag");
    EmitTraceTagAndId(0);
    EmitTraceRc(0);
    EmitTraceScopeAndNewline(UniqueLabel("mm_trace_transfer_no_scope"), 1);
    _b.DefineLabel(nullLabel);
    _b.FunctionEnd();
  }

  /// <summary>Emit all trace-related functions. Always emits print and tag_lookup.
  /// Only emits packed_tag/indent/transfer when mmTrace is true.</summary>
  public void EmitMmTraceFunctions(bool mmTrace, List<string?> tagTable) {
    EmitMmTracePrintTag();
    EmitMmTracePrintHex();
    EmitMmTracePrintI64();
    EmitMmTracePrintClass();
    EmitMmTagLookup(tagTable);
    if (mmTrace) {
      EmitMmTracePrintPackedTag();
      EmitMmTracePrintIndent();
      EmitMmTraceTransfer();
    }
  }

  // =========================================================================
  // Managed elements functions (array element refcount management)
  // =========================================================================

  /// <summary>mm_decref_managed_elements(managed_ptr): Decref each element pointer in buffer.</summary>
  public void EmitMmDecrefManagedElements(bool mmTrace) {
    // Slots: 0=managed_ptr, 1=buf, 2=len, 3=idx
    _b.FunctionStart("mm_decref_managed_elements", 1, 0x60);

    // Safety check: element_size must be 8 (heap pointers). If a __ManagedMemory
    // with non-pointer elements (e.g., integers) reaches this function, the
    // destructor was generated incorrectly. Panic early instead of decrefing
    // integer values as pointers (which causes use-after-free / segfaults).
    _b.LoadLocal(VReg.Scratch0, 0); // managed_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffElementSize);
    _b.CmpRegImm(VReg.Scratch1, MmemManagedElementSize);
    var elemSizeOk = UniqueLabel("mm_decref_elems_size_ok");
    _b.JumpIf(Condition.Equal, elemSizeOk);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_decref_elems_bad_elemsize");
    _b.Call("mrt_panic");
    _b.DefineLabel(elemSizeOk);

    _b.LoadLocal(VReg.Scratch0, 0); // managed_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffBuffer);
    _b.StoreLocal(1, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffLength);
    _b.StoreLocal(2, VReg.Scratch1);
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(3, VReg.Scratch0); // idx = 0

    var loopLabel = UniqueLabel("mm_decref_elems_loop");
    var doneLabel = UniqueLabel("mm_decref_elems_done");
    var skipLabel = UniqueLabel("mm_decref_elems_skip");

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch0, 3); // idx
    _b.LoadLocal(VReg.Scratch1, 2); // len
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, doneLabel);

    // elem = buf[idx * 8]: base = buf + idx*8
    _b.LoadLocal(VReg.Scratch0, 3); // idx
    _b.ShlRegImm(VReg.Scratch0, 3); // idx * 8
    _b.LoadLocal(VReg.Scratch1, 1); // buf
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0); // buf + idx*8
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, 0); // elem = [buf + idx*8]

    // Null guard
    _b.JumpIfZero(VReg.Arg0, skipLabel);

    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_elements");
    else _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_decref");

    _b.DefineLabel(skipLabel);
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);
    _b.Jump(loopLabel);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>mm_incref_managed_elements(managed_ptr): Incref each element pointer in buffer.</summary>
  public void EmitMmIncrefManagedElements(bool mmTrace) {
    _b.FunctionStart("mm_incref_managed_elements", 1, 0x60);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffBuffer);
    _b.StoreLocal(1, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffLength);
    _b.StoreLocal(2, VReg.Scratch1);
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(3, VReg.Scratch0);

    var loopLabel = UniqueLabel("mm_incref_elems_loop");
    var doneLabel = UniqueLabel("mm_incref_elems_done");
    var skipLabel = UniqueLabel("mm_incref_elems_skip");

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, doneLabel);

    _b.LoadLocal(VReg.Scratch0, 3);
    _b.ShlRegImm(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, 0);

    _b.JumpIfZero(VReg.Arg0, skipLabel);

    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_elements");
    else _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_incref");

    _b.DefineLabel(skipLabel);
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);
    _b.Jump(loopLabel);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // THE CAPACITY-SLOT INVARIANT
  //
  //     Every slot in [length, capacity) of an owned buffer reads as ZERO.
  //
  // This is what makes lengthening the live range safe. setLength (and so
  // Array.resize) does not — CANNOT — initialize the slots it exposes: every
  // caller that lengthens the buffer STAGES the value FIRST and publishes it
  // with setLength SECOND (push = set-then-setLength; insert = shiftRight +
  // set-then-setLength; string building = create + setByte-then-setLength). A
  // setLength that zeroed [oldLength, newLength) on the way up would erase every
  // one of those values. So the slots must ALREADY be zero when exposed, which
  // means zeroing them on the way OUT — in the operations that VACATE a slot:
  // clear, the shrink path of setLength, and remove.
  //
  // Zero is the right value for BOTH element classes, which is why one
  // byte-level, type-agnostic primitive serves both: 0 for a scalar element (the
  // documented "new elements are zero-initialized") and NULL for a managed
  // pointer — the element walks skip a null slot and LowerManagedMemGet reports
  // it as an empty slot rather than dereferencing it.
  //
  // Fresh capacity is NEVER re-zeroed, because it is already zero at birth:
  // mm_raw_alloc hands back zeroed slab memory, and mm_raw_realloc memcpy's only
  // the live prefix into such a buffer, leaving the grown tail zero. A resize
  // that grows into fresh memory therefore pays nothing here — only slots that
  // were actually occupied are ever zeroed.
  //
  // The self-hosted twin of these two helpers is __managed_mem_vacate_range /
  // __managed_mem_zero_elements_range in stdlib/Internals.maxon (a file this
  // bootstrap excludes — it lowers the __managed_mem_* builtins itself).
  // =========================================================================

  /// <summary>
  /// mm_vacate_managed_elements(managed_ptr, start, end): release each managed
  /// element in the half-open slot range, then erase the slots it left behind.
  ///
  /// The two halves belong together. A release without the erase leaves a
  /// dangling pointer above `length` for the next regrow to hand back, and the
  /// teardown walk then decrefs it a second time (double-free). An erase without
  /// the release leaks the element. Callers: clear (0, length) and the shrink
  /// path of setLength (newLength, oldLength).
  ///
  /// SKIPPED for a buffer this record does not own (capacity &lt; 0), on the same
  /// terms and for the same reason as its twin mm_zero_element_range: the slots
  /// are not ours to write, and the elements in them are not ours to release —
  /// a borrowed buffer's +1 refs belong to whoever owns the buffer, so releasing
  /// them here would be a double-free rather than a leak avoided.
  ///
  /// The two runtime helpers are the two arms of ONE dispatcher
  /// (EmitVacateElementRange), which hands them the same (record, start, end)
  /// and differs only in whether the departing elements are also released. They
  /// must therefore agree on WHOSE memory they may touch. Only the scalar arm
  /// used to carry the check, so the answer depended on the element class: safe
  /// for a byte array, an out-of-bounds write plus a stray decref for a managed
  /// one. It was unreachable — the only records with a negative capacity are
  /// rdata literals and the zero-copy views chained off them, and rdata is only
  /// ever emitted for byte/int/bool/short elements — but that is a property of
  /// four other files (ConstantArrayAnalysisPass, the string-literal record, the
  /// slice arms in Strings.cs), not of this one, and the setLength clamp made
  /// setLength(0) on a non-owned record newly reachable. A guard that costs one
  /// compare per CALL (not per element) buys the invariant locally.
  ///
  /// Managed elements are always 8-byte heap pointers, so the stride is fixed.
  /// </summary>
  // Stack slots: 0=managed_ptr, 1=start, 2=end, 3=buf, 4=idx
  public void EmitMmVacateManagedElements(bool mmTrace) {
    _b.FunctionStart("mm_vacate_managed_elements", 3, 0x60);

    var loopLabel = UniqueLabel("mm_vacate_elems_loop");
    var doneLabel = UniqueLabel("mm_vacate_elems_done");
    var zeroLabel = UniqueLabel("mm_vacate_elems_zero");

    // capacity < 0 => rdata / read-only view: neither the slots nor the elements
    // in them are this record's to touch.
    _b.LoadLocal(VReg.Scratch0, 0); // managed_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffCapacity);
    _b.CmpRegImm(VReg.Scratch1, 0);
    _b.JumpIf(Condition.Less, doneLabel);

    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffBuffer);
    _b.StoreLocal(3, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch0, 1); // start
    _b.StoreLocal(4, VReg.Scratch0); // idx = start

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch0, 4); // idx
    _b.LoadLocal(VReg.Scratch1, 2); // end
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, doneLabel);

    // Compute element address = buf + idx*8
    _b.LoadLocal(VReg.Scratch0, 4);
    _b.ShlRegImm(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0); // elem_addr
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, 0); // elem

    // Null guard: an already-empty slot still gets (re-)zeroed, never decref'd.
    _b.JumpIfZero(VReg.Arg0, zeroLabel);

    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_elements");
    else _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_decref");

    _b.DefineLabel(zeroLabel);
    // Zero the slot: [buf + idx*8] = 0. Recomputed rather than kept live across
    // the mm_decref call, which clobbers the scratch registers.
    _b.LoadLocal(VReg.Scratch0, 4);
    _b.ShlRegImm(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0);

    _b.LoadLocal(VReg.Scratch0, 4);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(4, VReg.Scratch0);
    _b.Jump(loopLabel);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// mm_zero_element_range(managed_ptr, start, end): erase the slots in the
  /// half-open range. The primitive-element counterpart of
  /// mm_vacate_managed_elements — there is no element to release, only a slot to
  /// clear, so that a later regrow of the length reads back 0 rather than the
  /// previous occupant's value.
  ///
  /// SKIPPED for a buffer this record does not own (capacity &lt; 0: an
  /// rdata-backed array literal, or a read-only view). Writing there would fault
  /// on read-only memory — and the invariant needs no help: setLength compares
  /// the new length against a NEGATIVE capacity and so rejects every value,
  /// meaning such a record cannot lengthen without first going through grow,
  /// whose COW copies the live elements into a fresh ZEROED heap buffer. The
  /// mandatory COW re-establishes the invariant by construction.
  /// </summary>
  // Stack slots: 0=managed_ptr, 1=start, 2=end, 3=buf, 4=element_size,
  //              5=cursor (idx, or the byte cursor), 6=bytes_remaining
  public void EmitMmZeroElementRange() {
    _b.FunctionStart("mm_zero_element_range", 3, 0x60);

    var doneLabel = UniqueLabel("mm_zero_range_done");
    var packedLabel = UniqueLabel("mm_zero_range_packed");
    var packedLoopLabel = UniqueLabel("mm_zero_range_packed_loop");
    var qwordLoopLabel = UniqueLabel("mm_zero_range_qword");
    var byteLoopLabel = UniqueLabel("mm_zero_range_byte");

    // capacity < 0 => rdata / read-only view: not ours to write.
    _b.LoadLocal(VReg.Scratch0, 0); // managed_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffCapacity);
    _b.CmpRegImm(VReg.Scratch1, 0);
    _b.JumpIf(Condition.Less, doneLabel);

    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffBuffer);
    _b.JumpIfZero(VReg.Scratch1, doneLabel); // never-allocated buffer
    _b.StoreLocal(3, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmemOffElementSize);
    _b.StoreLocal(4, VReg.Scratch1);
    // MmemBitPackedElementSize (0) routes to the per-bit clear below.
    _b.JumpIfZero(VReg.Scratch1, packedLabel);

    // --- byte-strided elements: zero [buf + start*es, buf + end*es) ---
    // bytes_remaining = (end - start) * element_size
    _b.LoadLocal(VReg.Scratch0, 2); // end
    _b.LoadLocal(VReg.Scratch1, 1); // start
    _b.SubRegReg(VReg.Scratch0, VReg.Scratch1); // count = end - start
    _b.LoadLocal(VReg.Scratch1, 4); // element_size
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.StoreLocal(6, VReg.Scratch0);
    _b.JumpIfZero(VReg.Scratch0, doneLabel);

    // cursor = buf + start * element_size
    _b.LoadLocal(VReg.Scratch0, 1); // start
    _b.LoadLocal(VReg.Scratch1, 4); // element_size
    _b.MulRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 3); // buf
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.StoreLocal(5, VReg.Scratch1);

    // Zero BYTE-EXACTLY: qwords while at least 8 remain, then a byte tail.
    //
    // NOT __slab_memzero — it rounds the length UP to a whole qword, which is
    // sound only for a region that ENDS at the end of its allocation (mm_realloc's
    // grown tail). This range ends at `end`, which is a partial, unaligned offset
    // into the buffer: rounding up would write as many as 7 bytes past it, and when
    // `end` sits at the buffer's end that lands OUTSIDE the slab slot, corrupting
    // the next object. (It did: a String's `setLength(len+1)` / `setLength(len)`
    // NUL-terminator dance in File.readText shrinks by one byte at an arbitrary
    // offset, and the rounded-up zero scribbled over the following allocation.)
    _b.DefineLabel(qwordLoopLabel);
    _b.LoadLocal(VReg.Scratch0, 6); // bytes_remaining
    _b.CmpRegImm(VReg.Scratch0, MachineWordBytes);
    _b.JumpIf(Condition.Below, byteLoopLabel);
    _b.LoadLocal(VReg.Scratch1, 5); // cursor
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0);
    _b.AddRegImm(VReg.Scratch1, MachineWordBytes);
    _b.StoreLocal(5, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch0, 6);
    _b.SubRegImm(VReg.Scratch0, MachineWordBytes);
    _b.StoreLocal(6, VReg.Scratch0);
    _b.Jump(qwordLoopLabel);

    _b.DefineLabel(byteLoopLabel);
    _b.LoadLocal(VReg.Scratch0, 6); // bytes_remaining
    _b.JumpIfZero(VReg.Scratch0, doneLabel);
    _b.LoadLocal(VReg.Scratch1, 5); // cursor
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreIndirectByte(VReg.Scratch1, 0, VReg.Scratch0);
    _b.AddRegImm(VReg.Scratch1, 1);
    _b.StoreLocal(5, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch0, 6);
    _b.SubRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(6, VReg.Scratch0);
    _b.Jump(byteLoopLabel);

    // --- bit-packed elements: clear one bit per element ---
    // A packed field is not byte-aligned, so a byte-range zero would flatten the
    // live neighbours sharing the boundary bytes. Clear the bits individually.
    _b.DefineLabel(packedLabel);
    _b.LoadLocal(VReg.Scratch0, 1); // start
    _b.StoreLocal(5, VReg.Scratch0); // idx = start

    _b.DefineLabel(packedLoopLabel);
    _b.LoadLocal(VReg.Scratch0, 5); // idx
    _b.LoadLocal(VReg.Scratch1, 2); // end
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, doneLabel);

    _b.LoadLocal(VReg.Scratch1, 3); // buf
    _b.BitTestAndReset(VReg.Scratch1, 0, VReg.Scratch0); // buf[idx] = false

    _b.LoadLocal(VReg.Scratch0, 5);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(5, VReg.Scratch0);
    _b.Jump(packedLoopLabel);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>Emit the managed-element walk helpers plus the slot-erase primitive.</summary>
  public void EmitMmManagedElementsFunctions(bool mmTrace) {
    EmitMmDecrefManagedElements(mmTrace);
    EmitMmIncrefManagedElements(mmTrace);
    EmitMmVacateManagedElements(mmTrace);
    EmitMmZeroElementRange();
  }

  // =========================================================================
  // mm_leak_check / mm_validate_ptr
  // =========================================================================

  /// <summary>mm_leak_check(exit_code): If __mm_alloc_count > 0 or __mm_raw_alloc_count > 0, print leak message and return 101. Otherwise return exit_code unchanged.
  /// Under --mm-debug, after the tracked-leak line, also prints a breakdown by type tag and a "(raw)" line for untagged leaks.</summary>
  // Stack slots: 0=exit_code (arg), 1=result_exit_code (scratch)
  public void EmitMmLeakCheck(bool mmDebug, List<string?> tagTable) {
    _b.FunctionStart("mm_leak_check", 1, 0x30);

    // Save original exit code to slot 1 (Ret aliases Scratch0, so we can't keep it in a register)
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.StoreLocal(1, VReg.Scratch0);

    // MAXON_SLAB_STATS exit dump (PLAN 1a.2): stderr-only, no-op unless enabled.
    // Runs here so it lands on the normal process-exit path alongside the leak gate.
    EmitSlabStatsDump();

    // The `--coverage` dump, on the same normal-exit path. It allocates nothing — its output path is
    // baked into the binary as symdata — so it cannot disturb the leak counters read below, and the
    // ordering here is only "before the process is judged", not a constraint the dump imposes.
    if (Compiler.Coverage) {
      _b.MovRegImm(VReg.Arg0, MxcovFormat.StatusCompleted);
      _b.Call(CoverageDumpLabel);
    }

    EmitLeakCheckForCounter("__mm_alloc_count", "__mm_leak_prefix", "tracked");

    // Under --mm-debug, print a per-tag breakdown of the tracked leaks (covers managed allocs).
    if (mmDebug) {
      EmitLeakCheckPerTagBreakdown(tagTable);
    }

    EmitLeakCheckForCounter("__mm_raw_alloc_count", "__mm_raw_leak_prefix", "raw");

    // Return result from slot 1
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.ReturnValue(VReg.Scratch0);
  }

  /// <summary>Emits a single leak-check block: if counterGlobal > 0, print prefix + count + suffix and set exit code to 101 in slot 1.</summary>
  private void EmitLeakCheckForCounter(string counterGlobal, string prefixSymdata, string labelTag) {
    _b.LoadGlobal(VReg.Scratch0, counterGlobal);
    var noLeak = UniqueLabel($"mm_leak_check_no_{labelTag}");
    _b.JumpIfZero(VReg.Scratch0, noLeak);
    _b.LeaSymdata(VReg.Arg0, prefixSymdata);
    _b.Call(_b.WriteStderrLabel);
    _b.LoadGlobal(VReg.Arg0, counterGlobal);
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__mm_leak_suffix");
    _b.Call(_b.WriteStderrLabel);
    _b.MovRegImm(VReg.Scratch0, 101);
    _b.StoreLocal(1, VReg.Scratch0);
    _b.DefineLabel(noLeak);
  }

  /// <summary>Emits an unrolled walk over __mm_alloc_count_by_tag[]. For each non-zero slot,
  /// prints "  <count> <TypeName>\n". Also prints "  <raw_count> (raw)\n" when __mm_raw_alloc_count > 0.
  /// Only called under --mm-debug.</summary>
  private void EmitLeakCheckPerTagBreakdown(List<string?> tagTable) {
    // Unroll across the compile-time-known tag table. Index 0 is the reserved
    // "no tag" slot; skip null entries (sparse table).
    for (int i = 1; i < tagTable.Count; i++) {
      var label = tagTable[i];
      if (label == null) continue;

      // Load __mm_alloc_count_by_tag[i] into Scratch0
      _b.LeaGlobal(VReg.Scratch0, "__mm_alloc_count_by_tag");
      _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, i * 8);
      var skipLabel = UniqueLabel("mm_leak_tag_skip");
      _b.JumpIfZero(VReg.Scratch0, skipLabel);

      // Print "  "
      _b.LeaSymdata(VReg.Arg0, "__mm_leak_by_tag_indent");
      _b.Call(_b.WriteStderrLabel);
      // Print count
      _b.LeaGlobal(VReg.Scratch0, "__mm_alloc_count_by_tag");
      _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, i * 8);
      _b.Call("mm_trace_print_i64");
      // Print " "
      _b.LeaSymdata(VReg.Arg0, "__mm_leak_by_tag_space");
      _b.Call(_b.WriteStderrLabel);
      // Print type name
      _b.LeaSymdata(VReg.Arg0, label);
      _b.Call(_b.WriteStderrLabel);
      // Print "\n"
      _b.LeaSymdata(VReg.Arg0, "__mm_leak_tag_newline");
      _b.Call(_b.WriteStderrLabel);

      _b.DefineLabel(skipLabel);
    }

    // Untagged raw allocations: print "  <count> (raw)\n" if __mm_raw_alloc_count > 0.
    _b.LoadGlobal(VReg.Scratch0, "__mm_raw_alloc_count");
    var skipRaw = UniqueLabel("mm_leak_raw_skip");
    _b.JumpIfZero(VReg.Scratch0, skipRaw);
    _b.LeaSymdata(VReg.Arg0, "__mm_leak_by_tag_indent");
    _b.Call(_b.WriteStderrLabel);
    _b.LoadGlobal(VReg.Arg0, "__mm_raw_alloc_count");
    _b.Call("mm_trace_print_i64");
    _b.LeaSymdata(VReg.Arg0, "__mm_leak_raw_label");
    _b.Call(_b.WriteStderrLabel);
    _b.DefineLabel(skipRaw);
  }

  /// <summary>mm_validate_ptr(user_ptr, tag_cstr): Panics if ptr is non-null but has zero refcount.</summary>
  public void EmitMmValidatePtr() {
    _b.DefineSymdata("__mm_validate_tag", "MM VALIDATE ptr=\0"u8.ToArray());
    _b.DefineSymdata("__mm_validate_fail", "VALIDATION FAILED: ptr has zero refcount!\n\0"u8.ToArray());

    _b.FunctionStart("mm_validate_ptr", 2, 0x30);
    _b.LoadLocal(VReg.Scratch0, 0); // ptr
    var doneLabel = UniqueLabel("mm_validate_done");
    // Null is OK
    _b.JumpIfZero(VReg.Scratch0, doneLabel);
    // Load refcount at [ptr - 8]
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffRefcount); // [ptr-8]
    _b.JumpIfNonZero(VReg.Scratch1, doneLabel); // nonzero = valid
    // Failed: print "MM VALIDATE ptr=0xHEX\n" then panic
    _b.LeaSymdata(VReg.Arg0, "__mm_validate_tag");
    _b.Call("mm_trace_print_tag");
    _b.LoadLocal(VReg.Arg0, 0); // ptr
    _b.Call("mm_trace_print_hex");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
    _b.LeaSymdata(VReg.Arg0, "__mm_validate_fail");
    _b.Call("mrt_panic");
    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // ManagedList runtime functions
  // =========================================================================
  // ManagedListNode layout: [+0]=next, [+8]=prev, [+16]=list, [+24]=value
  // ManagedList layout: [+0]=head, [+8]=tail, [+16]=count

  public void EmitManagedListFunctions(bool mmTrace) {
    EmitManagedListInsertFirst(mmTrace);
    EmitManagedListInsertLast(mmTrace);
    EmitManagedListInsertAfter(mmTrace);
    EmitManagedListInsertBefore(mmTrace);
    EmitManagedListUnlink();
    EmitManagedListClear(mmTrace);
    EmitManagedListClearManaged(mmTrace);
    EmitManagedListDecrefValues();
  }

  private void EmitManagedListInsertFirst(bool mmTrace) {
    // Slots: 0=list_ptr, 1=node_ptr, 2=old_head
    _b.FunctionStart("maxon_managed_list_insert_first", 2, 0x50);
    // Auto-detach: if node.list != 0, unlink and decref
    _b.LoadLocal(VReg.Scratch0, 1); // node_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16); // node.list
    var noDetach = UniqueLabel("mli_first_no_detach");
    _b.JumpIfZero(VReg.Scratch1, noDetach);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch1); // old list
    _b.LoadLocal(VReg.Arg1, 1);
    _b.Call("maxon_managed_list_unlink");
    _b.LoadLocal(VReg.Arg0, 1); // node_ptr
    _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_decref");
    _b.DefineLabel(noDetach);
    // old_head = [list+0]
    _b.LoadLocal(VReg.Scratch0, 0); // list_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // old_head
    _b.StoreLocal(2, VReg.Scratch1); // save old_head
    // node.next = old_head; node.prev = 0; node.list = list_ptr
    _b.LoadLocal(VReg.Scratch0, 1); // node_ptr
    _b.LoadLocal(VReg.Scratch1, 2); // old_head
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // node.next = old_head
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1); // node.prev = 0
    _b.LoadLocal(VReg.Scratch1, 0); // list_ptr
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1); // node.list = list_ptr
    // if old_head != 0: old_head.prev = node_ptr
    _b.LoadLocal(VReg.Scratch1, 2); // old_head
    var noOldHead = UniqueLabel("mli_first_no_old_head");
    _b.JumpIfZero(VReg.Scratch1, noOldHead);
    _b.LoadLocal(VReg.Scratch0, 1); // node_ptr
    _b.StoreIndirect(VReg.Scratch1, 8, VReg.Scratch0); // old_head.prev = node_ptr
    _b.DefineLabel(noOldHead);
    // list.head = node_ptr
    _b.LoadLocal(VReg.Scratch0, 0); // list_ptr
    _b.LoadLocal(VReg.Scratch1, 1); // node_ptr
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);
    // if list.tail == 0: list.tail = node_ptr
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 8); // list.tail
    var tailOk = UniqueLabel("mli_first_tail_ok");
    _b.JumpIfNonZero(VReg.Scratch2, tailOk);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1); // list.tail = node_ptr
    _b.DefineLabel(tailOk);
    // list.count++
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 16);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch2);
    // Incref node
    _b.LoadLocal(VReg.Arg0, 1);
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_list_insert");
    else _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_incref");
    _b.FunctionEnd();
  }

  private void EmitManagedListInsertLast(bool mmTrace) {
    // Slots: 0=list_ptr, 1=node_ptr, 2=old_tail
    _b.FunctionStart("maxon_managed_list_insert_last", 2, 0x50);
    // Auto-detach
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16);
    var noDetach = UniqueLabel("mli_last_no_detach");
    _b.JumpIfZero(VReg.Scratch1, noDetach);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch1);
    _b.LoadLocal(VReg.Arg1, 1);
    _b.Call("maxon_managed_list_unlink");
    _b.LoadLocal(VReg.Arg0, 1);
    _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_decref");
    _b.DefineLabel(noDetach);
    // old_tail = [list+8]
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 8); // old_tail
    _b.StoreLocal(2, VReg.Scratch1);
    // node.next = 0; node.prev = old_tail; node.list = list_ptr
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // node.next = 0
    _b.LoadLocal(VReg.Scratch1, 2); // old_tail
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1); // node.prev = old_tail
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1); // node.list = list_ptr
    // if old_tail != 0: old_tail.next = node_ptr
    _b.LoadLocal(VReg.Scratch1, 2);
    var noOldTail = UniqueLabel("mli_last_no_old_tail");
    _b.JumpIfZero(VReg.Scratch1, noOldTail);
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0); // old_tail.next = node_ptr
    _b.DefineLabel(noOldTail);
    // list.tail = node_ptr
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1);
    // if list.head == 0: list.head = node_ptr
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 0);
    var headOk = UniqueLabel("mli_last_head_ok");
    _b.JumpIfNonZero(VReg.Scratch2, headOk);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);
    _b.DefineLabel(headOk);
    // list.count++
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 16);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch2);
    // Incref node
    _b.LoadLocal(VReg.Arg0, 1);
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_list_insert");
    else _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_incref");
    _b.FunctionEnd();
  }

  private void EmitManagedListInsertAfter(bool mmTrace) {
    // Slots: 0=list_ptr, 1=target_ptr, 2=node_ptr, 3=after (target.next)
    _b.FunctionStart("maxon_managed_list_insert_after", 3, 0x60);
    // Auto-detach
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16);
    var noDetach = UniqueLabel("mli_after_no_detach");
    _b.JumpIfZero(VReg.Scratch1, noDetach);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch1);
    _b.LoadLocal(VReg.Arg1, 2);
    _b.Call("maxon_managed_list_unlink");
    _b.LoadLocal(VReg.Arg0, 2);
    _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_decref");
    _b.DefineLabel(noDetach);
    // after = target.next
    _b.LoadLocal(VReg.Scratch0, 1); // target_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // after = target.next
    _b.StoreLocal(3, VReg.Scratch1);
    // node.next = after; node.prev = target; node.list = list_ptr
    _b.LoadLocal(VReg.Scratch0, 2); // node_ptr
    _b.LoadLocal(VReg.Scratch1, 3);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // node.next = after
    _b.LoadLocal(VReg.Scratch1, 1); // target_ptr
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1); // node.prev = target
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1); // node.list = list_ptr
    // target.next = node_ptr
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0);
    // if after != 0: after.prev = node_ptr; else: list.tail = node_ptr
    _b.LoadLocal(VReg.Scratch1, 3); // after
    var wasTail = UniqueLabel("mli_after_was_tail");
    var linked = UniqueLabel("mli_after_linked");
    _b.JumpIfZero(VReg.Scratch1, wasTail);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreIndirect(VReg.Scratch1, 8, VReg.Scratch0); // after.prev = node_ptr
    _b.Jump(linked);
    _b.DefineLabel(wasTail);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1); // list.tail = node_ptr
    _b.DefineLabel(linked);
    // list.count++
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16);
    _b.AddRegImm(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1);
    // Incref node
    _b.LoadLocal(VReg.Arg0, 2);
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_list_insert");
    else _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_incref");
    _b.FunctionEnd();
  }

  private void EmitManagedListInsertBefore(bool mmTrace) {
    // Slots: 0=list_ptr, 1=target_ptr, 2=node_ptr, 3=before (target.prev)
    _b.FunctionStart("maxon_managed_list_insert_before", 3, 0x60);
    // Auto-detach
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16);
    var noDetach = UniqueLabel("mli_before_no_detach");
    _b.JumpIfZero(VReg.Scratch1, noDetach);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch1);
    _b.LoadLocal(VReg.Arg1, 2);
    _b.Call("maxon_managed_list_unlink");
    _b.LoadLocal(VReg.Arg0, 2);
    _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_decref");
    _b.DefineLabel(noDetach);
    // before = target.prev
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 8); // before = target.prev
    _b.StoreLocal(3, VReg.Scratch1);
    // node.next = target; node.prev = before; node.list = list_ptr
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.LoadLocal(VReg.Scratch1, 1); // target_ptr
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // node.next = target
    _b.LoadLocal(VReg.Scratch1, 3); // before
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1); // node.prev = before
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1); // node.list = list_ptr
    // target.prev = node_ptr
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreIndirect(VReg.Scratch1, 8, VReg.Scratch0);
    // if before != 0: before.next = node_ptr; else: list.head = node_ptr
    _b.LoadLocal(VReg.Scratch1, 3); // before
    var wasHead = UniqueLabel("mli_before_was_head");
    var linked = UniqueLabel("mli_before_linked");
    _b.JumpIfZero(VReg.Scratch1, wasHead);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0); // before.next = node_ptr
    _b.Jump(linked);
    _b.DefineLabel(wasHead);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // list.head = node_ptr
    _b.DefineLabel(linked);
    // list.count++
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16);
    _b.AddRegImm(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1);
    // Incref node
    _b.LoadLocal(VReg.Arg0, 2);
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_list_insert");
    else _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_incref");
    _b.FunctionEnd();
  }

  public void EmitManagedListUnlink() {
    // Slots: 0=list_ptr, 1=node_ptr, 2=prev, 3=next
    _b.FunctionStart("maxon_managed_list_unlink", 2, 0x60);
    _b.LoadLocal(VReg.Scratch0, 1); // node_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16); // node.list
    var done = UniqueLabel("mlu_done");
    _b.JumpIfZero(VReg.Scratch1, done);
    // prev = [node+8], next = [node+0]
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 8); // prev
    _b.StoreLocal(2, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // next
    _b.StoreLocal(3, VReg.Scratch1);
    // if prev != 0: prev.next = next; else: list.head = next
    _b.LoadLocal(VReg.Scratch1, 2); // prev
    _b.LoadLocal(VReg.Scratch0, 3); // next
    var noPrev = UniqueLabel("mlu_no_prev");
    var prevDone = UniqueLabel("mlu_prev_done");
    _b.JumpIfZero(VReg.Scratch1, noPrev);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0); // prev.next = next
    _b.Jump(prevDone);
    _b.DefineLabel(noPrev);
    _b.LoadLocal(VReg.Scratch2, 0); // list_ptr
    _b.StoreIndirect(VReg.Scratch2, 0, VReg.Scratch0); // list.head = next
    _b.DefineLabel(prevDone);
    // if next != 0: next.prev = prev; else: list.tail = prev
    _b.LoadLocal(VReg.Scratch0, 3); // next
    _b.LoadLocal(VReg.Scratch1, 2); // prev
    var noNext = UniqueLabel("mlu_no_next");
    var nextDone = UniqueLabel("mlu_next_done");
    _b.JumpIfZero(VReg.Scratch0, noNext);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1); // next.prev = prev
    _b.Jump(nextDone);
    _b.DefineLabel(noNext);
    _b.LoadLocal(VReg.Scratch2, 0);
    _b.StoreIndirect(VReg.Scratch2, 8, VReg.Scratch1); // list.tail = prev
    _b.DefineLabel(nextDone);
    // Clear node links: next=0, prev=0, list=0
    _b.LoadLocal(VReg.Scratch0, 1); // node_ptr
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1);
    // list.count--
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16);
    _b.SubRegImm(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1);
    _b.DefineLabel(done);
    _b.FunctionEnd();
  }

  private void EmitManagedListClear(bool mmTrace) => EmitManagedListClearImpl("maxon_managed_list_clear", managed: false, mmTrace);
  private void EmitManagedListClearManaged(bool mmTrace) => EmitManagedListClearImpl("maxon_managed_list_clear_managed", managed: true, mmTrace);

  private void EmitManagedListClearImpl(string funcName, bool managed, bool mmTrace) {
    // Slots: 0=list_ptr, 1=current, 2=next
    _b.FunctionStart(funcName, 1, 0x50);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // list.head = current
    _b.StoreLocal(1, VReg.Scratch1);

    var loopLabel = UniqueLabel($"{funcName}_loop");
    var loopDone = UniqueLabel($"{funcName}_done");

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch0, 1); // current
    _b.JumpIfZero(VReg.Scratch0, loopDone);
    // Save next
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // current.next
    _b.StoreLocal(2, VReg.Scratch1);
    // Clear node links
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1);

    if (managed) {
      // Decref value at [node+24]
      _b.LoadLocal(VReg.Scratch0, 1);
      _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, 24); // node.value
      if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_list_clear");
      else _b.ZeroReg(VReg.Arg1);
      _b.Call("mm_decref");
    }

    // Decref/free node
    _b.LoadLocal(VReg.Arg0, 1);
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_list_clear");
    else _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_decref");

    // current = next
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreLocal(1, VReg.Scratch0);
    _b.Jump(loopLabel);

    _b.DefineLabel(loopDone);
    // Zero list metadata
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, 16, VReg.Scratch1);
    _b.FunctionEnd();
  }

  private void EmitManagedListDecrefValues() {
    // Slots: 0=list_ptr, 1=current, 2=next
    _b.FunctionStart("maxon_managed_list_decref_values", 1, 0x50);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0);
    _b.StoreLocal(1, VReg.Scratch1);

    var loopLabel = UniqueLabel("mldv_loop");
    var doneLabel = UniqueLabel("mldv_done");

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.JumpIfZero(VReg.Scratch0, doneLabel);
    // Save next
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0);
    _b.StoreLocal(2, VReg.Scratch1);
    // Decref value at [node+24]
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch0, 24);
    _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_decref");
    // current = next
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.StoreLocal(1, VReg.Scratch0);
    _b.Jump(loopLabel);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

}
