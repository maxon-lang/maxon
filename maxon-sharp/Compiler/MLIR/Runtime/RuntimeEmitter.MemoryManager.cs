namespace MaxonSharp.Compiler.Mlir.Runtime;

/// <summary>
/// Memory manager functions, emitted once for both platforms.
/// </summary>
public partial class RuntimeEmitter {

  /// <summary>
  /// Emit all memory manager global data (panic strings, trace tags, etc.).
  /// Must be called before emitting MM functions.
  /// </summary>
  public void EmitMmGlobals(bool mmTrace, bool mmDebug) {
    // Mutable counters — must be defined as globals (not symdata) so LeaGlobal resolves correctly
    _b.DefineGlobal("__mm_alloc_count", 8, 0);
    _b.DefineGlobal("__mm_alloc_id_counter", 8, 0);

    _b.DefineSymdata("__mm_leak_prefix", "MM leak: \0"u8.ToArray());
    _b.DefineSymdata("__mm_leak_suffix", " allocation(s) remain\n\0"u8.ToArray());
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
      _b.DefineSymdata("__mm_scope_find_first_file", "find_first_file\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_get_cwd", "get_cwd\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_capture", "capture\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_pipe_read", "pipe_read\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_realloc", "realloc\0"u8.ToArray());
      _b.DefineSymdata("__mm_scope_managed_list_insert", "managed_list_insert\0"u8.ToArray());
    }

    _b.DefineSymdata("__mm_panic_decref_null",
      "mm_decref called with NULL pointer\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_incref_null",
      "mm_incref called with NULL pointer\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_decref_underflow",
      "mm_decref: refcount underflow (already zero)\n\0"u8.ToArray());
    _b.DefineSymdata("__mm_panic_index_oob",
      "__ManagedMemory: index out of bounds\n\0"u8.ToArray());
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
  public void EmitMmDecref(bool mmTrace) {
    bool ds = Compiler.DebugStream;
    _b.FunctionStart("mm_decref", (mmTrace || ds) ? 2 : 1, 0x30);

    // NULL check -- panic
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    var notNull = UniqueLabel("mm_decref_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_decref_null");
    _b.Call("mrt_panic");
    _b.DefineLabel(notNull);

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

    // Set tag context and depth for slab/OS traces
    if (mmTrace) {
      _b.LoadLocal(VReg.Scratch0, 0); // user_ptr
      _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, MmOffPackedId); // packed_id
      _b.StoreGlobal("__mm_trace_tag_ctx", VReg.Scratch0);
      EmitTraceDepthInc();
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

    // Zero the user area so managed pointers in uninitialized array slots are NULL.
    // Without this, array.set on fresh capacity slots would decref garbage pointers.
    _b.LoadLocal(VReg.Scratch0, 6); // ptr = user_ptr
    _b.LoadLocal(VReg.Scratch1, 6); // end = user_ptr
    _b.LoadLocal(VReg.Arg0, 0);     // size
    _b.AddRegReg(VReg.Scratch1, VReg.Arg0); // end = user_ptr + size
    EmitZeroFillLoop("mm_alloc");

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

    // Allocate new block via slab allocator
    _b.LoadLocal(VReg.Arg0, 4); // Arg0 = new_alloc_size
    _b.Call("__slab_alloc"); // Scratch0 = new_raw_ptr
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

    // Zero the new space beyond old_size to prevent use-after-free when
    // array.set decrefs uninitialized slots after realloc.
    _b.LoadLocal(VReg.Scratch0, 5); // new_raw_ptr
    _b.AddRegImm(VReg.Scratch0, MmHeaderSize); // new_user_ptr
    _b.LoadLocal(VReg.Scratch1, 1); // old_size
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // Scratch0 = ptr = new_user_ptr + old_size
    _b.LoadLocal(VReg.Scratch1, 5); // new_raw_ptr
    _b.AddRegImm(VReg.Scratch1, MmHeaderSize); // new_user_ptr
    _b.LoadLocal(VReg.Arg0, 2); // new_size
    _b.AddRegReg(VReg.Scratch1, VReg.Arg0); // Scratch1 = end = new_user_ptr + new_size
    EmitZeroFillLoop("mm_realloc");

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

    // Zero the allocated block to prevent use-after-free when managed array
    // elements in uninitialized capacity slots get decreffed.
    _b.LoadLocal(VReg.Scratch0, 2); // ptr
    _b.LoadLocal(VReg.Scratch1, 2); // end = ptr
    _b.LoadLocal(VReg.Arg0, 0);     // size
    _b.AddRegReg(VReg.Scratch1, VReg.Arg0); // end = ptr + size
    EmitZeroFillLoop("mm_raw_alloc");

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
    // When element_size == 0 (bit-packed bool sentinel), use (capacity + 7) >> 3 instead.
    _b.LoadLocal(VReg.Scratch0, 2); // Scratch0 = managedPtr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16); // Scratch1 = capacity
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 24); // Scratch2 = element_size
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

    // Step 4: Free old buffer via mm_raw_free(old_ptr, scope=[realloc])
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = old_ptr
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_realloc");
    _b.Call("mm_raw_free");

    if (mmTrace) {
      EmitTraceDepthDec();
    }

    // Return new_ptr
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // maxon_string_ensure_cap(buffer, length, capacity, requiredCap) -> buffer
  //
  // Ensures a string's backing buffer has at least requiredCap bytes of capacity.
  // Three cases:
  //   1. capacity >= requiredCap: return buffer as-is (no-op)
  //   2. capacity == 0 (rdata/literal): alloc requiredCap bytes, copy length bytes from old buffer
  //   3. capacity < requiredCap (heap): realloc via mm_raw_alloc + memcpy + mm_raw_free
  // Returns the (possibly new) buffer pointer.
  // =========================================================================
  // Stack slots: 0=buffer, 1=length, 2=capacity, 3=requiredCap
  //              4=new_buffer (scratch)
  public void EmitStringEnsureCap(bool mmTrace) {
    _b.FunctionStart("maxon_string_ensure_cap", 4, mmTrace ? 0x50 : 0x40);

    // Check: capacity >= requiredCap → return buffer as-is
    _b.LoadLocal(VReg.Scratch0, 2); // Scratch0 = capacity
    _b.LoadLocal(VReg.Scratch1, 3); // Scratch1 = requiredCap
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    var needGrow = UniqueLabel("str_ensure_need_grow");
    _b.JumpIf(Condition.Below, needGrow); // unsigned: capacity < requiredCap

    // No growth needed — return existing buffer
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ReturnValue(VReg.Scratch0);

    // Needs growth: allocate new buffer of requiredCap bytes
    _b.DefineLabel(needGrow);
    _b.LoadLocal(VReg.Arg0, 3); // Arg0 = requiredCap
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_realloc");
    _b.Call("mm_raw_alloc");
    _b.StoreLocal(4, VReg.Scratch0); // slot 4 = new_buffer

    // Copy length bytes from old buffer to new buffer
    _b.LoadLocal(VReg.Arg0, 4); // Arg0 = new_buffer (dst)
    _b.LoadLocal(VReg.Arg1, 0); // Arg1 = old_buffer (src)
    _b.LoadLocal(VReg.Arg2, 1); // Arg2 = length (count)
    _b.Call("maxon_memcpy");

    // If capacity > 0 (heap-allocated), free the old buffer
    _b.LoadLocal(VReg.Scratch0, 2); // Scratch0 = capacity
    var skipFree = UniqueLabel("str_ensure_skip_free");
    _b.JumpIfZero(VReg.Scratch0, skipFree);
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = old_buffer
    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_realloc");
    _b.Call("mm_raw_free");
    _b.DefineLabel(skipFree);

    // Return new_buffer
    _b.LoadLocal(VReg.Scratch0, 4);
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
    // ManagedMemory: [+0]=buf, [+8]=len, [+16]=capacity, [+24]=element_size
    // Slots: 0=managed_ptr, 1=buf, 2=len, 3=idx
    _b.FunctionStart("mm_decref_managed_elements", 1, 0x60);
    _b.LoadLocal(VReg.Scratch0, 0); // managed_ptr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // buf
    _b.StoreLocal(1, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 8); // len
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
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // buf
    _b.StoreLocal(1, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 8); // len
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

  /// <summary>mm_clear_managed_elements(managed_ptr): Decref and zero each element slot.</summary>
  public void EmitMmClearManagedElements(bool mmTrace) {
    _b.FunctionStart("mm_clear_managed_elements", 1, 0x60);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // buf
    _b.StoreLocal(1, VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 8); // len
    _b.StoreLocal(2, VReg.Scratch1);
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(3, VReg.Scratch0);

    var loopLabel = UniqueLabel("mm_clear_elems_loop");
    var doneLabel = UniqueLabel("mm_clear_elems_done");
    var zeroLabel = UniqueLabel("mm_clear_elems_zero");

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, doneLabel);

    // Compute element address = buf + idx*8
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.ShlRegImm(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0); // elem_addr
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch1, 0); // elem

    // Null guard: skip decref, go to zero
    _b.JumpIfZero(VReg.Arg0, zeroLabel);

    if (mmTrace) _b.LeaSymdata(VReg.Arg1, "__mm_scope_managed_elements");
    else _b.ZeroReg(VReg.Arg1);
    _b.Call("mm_decref");

    _b.DefineLabel(zeroLabel);
    // Zero the slot: [buf + idx*8] = 0
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.ShlRegImm(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreIndirect(VReg.Scratch1, 0, VReg.Scratch0);

    _b.LoadLocal(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);
    _b.Jump(loopLabel);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>Emit all three managed-elements functions.</summary>
  public void EmitMmManagedElementsFunctions(bool mmTrace) {
    EmitMmDecrefManagedElements(mmTrace);
    EmitMmIncrefManagedElements(mmTrace);
    EmitMmClearManagedElements(mmTrace);
  }

  // =========================================================================
  // mm_leak_check / mm_validate_ptr
  // =========================================================================

  /// <summary>mm_leak_check(exit_code): If __mm_alloc_count > 0, print leak message and return 101. Otherwise return exit_code unchanged.</summary>
  public void EmitMmLeakCheck() {
    _b.FunctionStart("mm_leak_check", 1, 0x30);
    _b.LoadGlobal(VReg.Scratch0, "__mm_alloc_count");
    var noLeakLabel = UniqueLabel("mm_leak_check_no_leak");
    var doneLabel = UniqueLabel("mm_leak_check_done");
    _b.JumpIfZero(VReg.Scratch0, noLeakLabel);
    // Print "MM leak: "
    _b.LeaSymdata(VReg.Arg0, "__mm_leak_prefix");
    _b.Call(_b.WriteStderrLabel);
    // Print count
    _b.LoadGlobal(VReg.Arg0, "__mm_alloc_count");
    _b.Call("mm_trace_print_i64");
    // Print " allocation(s) remain\n"
    _b.LeaSymdata(VReg.Arg0, "__mm_leak_suffix");
    _b.Call(_b.WriteStderrLabel);
    // Return 101 (leak exit code)
    _b.MovRegImm(VReg.Ret, 101);
    _b.Jump(doneLabel);
    // No leak: return original exit code
    _b.DefineLabel(noLeakLabel);
    _b.LoadLocal(VReg.Ret, 0); // arg0 = original exit code
    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
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

  /// Zero-fill memory from Scratch0 (start) to Scratch1 (end) in 8-byte steps.
  /// Caller must set up Scratch0 and Scratch1 before calling.
  private void EmitZeroFillLoop(string labelPrefix) {
    var zeroLoop = UniqueLabel($"{labelPrefix}_zero_loop");
    var zeroDone = UniqueLabel($"{labelPrefix}_zero_done");
    _b.DefineLabel(zeroLoop);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, zeroDone);
    _b.ZeroReg(VReg.Arg0);
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Arg0); // *ptr = 0
    _b.AddRegImm(VReg.Scratch0, 8); // ptr += 8
    _b.Jump(zeroLoop);
    _b.DefineLabel(zeroDone);
  }
}
