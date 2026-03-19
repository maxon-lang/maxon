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

    if (mmTrace) {
      _b.DefineSymdata("__mm_tag_alloc", "alloc \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_free", "free \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_raw_alloc", "raw_alloc\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_raw_free", "raw_free\0"u8.ToArray());
      _b.DefineSymdata("__slab_tag_alloc", "slab_alloc\0"u8.ToArray());
      _b.DefineSymdata("__slab_tag_free", "slab_free\0"u8.ToArray());
      _b.DefineSymdata("__slab_tag_class", " class=\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_incref", "incref \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_decref", "decref \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_transfer", "transfer \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_realloc", "realloc \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_cow", "cow \0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_size_eq", " size=\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_rc_eq", " rc=\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_hash", " #\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_lbracket", " [\0"u8.ToArray());
      _b.DefineSymdata("__mm_tag_rbracket", "]\0"u8.ToArray());
      _b.DefineGlobal("__mm_trace_depth", 8, 0);
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

  /// <summary>
  /// Emit: indent + [extra indent if scope NULL] + "free " + "TypeName #N" + " [scope]" + "\n"
  /// </summary>
  private void EmitInlineTraceFree(string uniquePrefix, int ptrSlot, int scopeSlot) {
    _b.Call("mm_trace_print_indent");
    // Extra indent when scope is NULL (internal free from mm_decref)
    _b.LoadLocal(VReg.Scratch0, scopeSlot);
    var noExtraIndent = $"{uniquePrefix}_no_extra_indent";
    _b.JumpIfNonZero(VReg.Scratch0, noExtraIndent);
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_indent");
    _b.Call("mm_trace_print_tag");
    _b.DefineLabel(noExtraIndent);
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

  /// <summary>Emit: indent + "raw_alloc size=N" [+ " [scope]"] + "\n"</summary>
  private void EmitInlineTraceRawAlloc(string uniquePrefix, int sizeSlot, int scopeSlot) {
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_raw_alloc");
    _b.Call("mm_trace_print_tag");
    EmitTraceSize(sizeSlot);
    EmitTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  /// <summary>Emit: indent + "raw_free\n"</summary>
  private void EmitInlineTraceRawFree() {
    _b.Call("mm_trace_print_indent");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_raw_free");
    _b.Call("mm_trace_print_tag");
    _b.LeaSymdata(VReg.Arg0, "__mm_tag_newline");
    _b.Call("mm_trace_print_tag");
  }

  // =========================================================================
  // mm_incref(user_ptr, [scope_cstr]) -> void
  // Increments refcount at [ptr-8]. Panics on NULL pointer.
  // =========================================================================
  // Stack slots: 0=user_ptr, 1=scope_cstr (trace only)
  public void EmitMmIncref(bool mmTrace) {
    _b.FunctionStart("mm_incref", mmTrace ? 2 : 1, 0x30);

    // NULL check -- panic
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    var notNull = UniqueLabel("mm_incref_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_incref_null");
    _b.Call("maxon_panic");
    _b.DefineLabel(notNull);

    // Atomic increment refcount at [user_ptr - 8]
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.AtomicInc(VReg.Scratch0, MmOffRefcount);

    // Trace incref after increment
    if (mmTrace) {
      EmitInlineTrace("__mm_tag_incref", UniqueLabel("mm_incref_trace"),
        ptrSlot: 0, scopeSlot: 1);
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
    _b.FunctionStart("mm_decref", mmTrace ? 2 : 1, 0x30);

    // NULL check -- panic
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    var notNull = UniqueLabel("mm_decref_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_decref_null");
    _b.Call("maxon_panic");
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
    _b.Call("maxon_panic");
    _b.DefineLabel(hasRefs);

    // Atomic decrement refcount: [ptr-8] -= 1
    // AtomicDec sets zero flag when result reaches 0
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    _b.AtomicDec(VReg.Scratch0, MmOffRefcount);

    // If refcount > 0 after decrement, we're done
    var done = UniqueLabel("mm_decref_done");
    _b.JumpIf(Condition.NotEqual, done); // ZF=0 means refcount > 0

    // refcount == 0: call destructor if non-null, then free
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
    if (mmTrace) _b.ZeroReg(VReg.Arg1); // scope = NULL
    _b.Call("mm_free");

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
    _b.FunctionStart("mm_free", mmTrace ? 2 : 1, 0x60);

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

    // Validate canary in debug mode: [user_ptr + size] must equal MmDebugCanaryValue
    if (mmDebug) {
      // Read alloc_size from [user_ptr - 32]
      _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, MmOffAllocSize); // Scratch1 = total_alloc_size
      // user_size = total_alloc_size - MmHeaderSize
      _b.SubRegImm(VReg.Scratch1, MmHeaderSize);
      // canary_ptr = user_ptr + user_size
      _b.LoadLocal(VReg.Scratch0, 0);
      _b.AddRegReg(VReg.Scratch0, VReg.Scratch1);
      _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 0); // Scratch1 = canary value
      _b.MovRegImm(VReg.Scratch2, MmDebugCanaryValue);
      _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
      var canaryOk = UniqueLabel("mm_free_canary_ok");
      _b.JumpIf(Condition.Equal, canaryOk);
      _b.LeaSymdata(VReg.Arg0, "__mm_panic_canary");
      _b.Call("maxon_panic");
      _b.DefineLabel(canaryOk);
    }

    // Atomic decrement __mm_alloc_count
    _b.LeaGlobal(VReg.Scratch0, "__mm_alloc_count");
    _b.AtomicDec(VReg.Scratch0, 0);

    // Compute raw_ptr = user_ptr - MmHeaderSize; free via slab allocator
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    _b.SubRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = raw_ptr (slab slot base)
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_free");

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
  //              4=alloc_size (scratch), 5=raw_ptr (scratch)
  public void EmitMmAlloc(bool mmTrace, bool mmDebug) {
    _b.FunctionStart("mm_alloc", mmTrace ? 4 : 3, 0x70);

    // Panic if size == 0
    _b.LoadLocal(VReg.Scratch0, 0);
    var sizeOk = UniqueLabel("mm_alloc_size_ok");
    _b.JumpIfNonZero(VReg.Scratch0, sizeOk);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_alloc_zero_size");
    _b.Call("maxon_panic");
    _b.DefineLabel(sizeOk);

    // Compute alloc_size = size + MmHeaderSize (+ 8 for canary if MmDebug)
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = size
    _b.AddRegImm(VReg.Scratch0, mmDebug ? MmHeaderSize + 8 : MmHeaderSize);
    _b.StoreLocal(4, VReg.Scratch0); // slot 4 = alloc_size

    // Allocate alloc_size bytes via slab allocator (zero-initialized)
    _b.LoadLocal(VReg.Arg0, 4); // Arg0 = alloc_size
    _b.Call("__slab_alloc"); // Scratch0 = raw_ptr (slab slot base)
    _b.StoreLocal(5, VReg.Scratch0); // slot 5 = raw_ptr

    // Store total_alloc_size at [raw + 0]
    _b.LoadLocal(VReg.Scratch0, 5);  // Scratch0 = raw_ptr
    _b.LoadLocal(VReg.Scratch1, 4);  // Scratch1 = alloc_size
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1); // [raw + 0] = alloc_size

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

    // Store packed_id at [raw + 8]
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

    // Compute user_ptr = raw + MmHeaderSize, save to slot 5 for trace
    _b.LoadLocal(VReg.Scratch0, 5); // Scratch0 = raw_ptr
    _b.AddRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = user_ptr
    _b.StoreLocal(5, VReg.Scratch0); // slot 5 = user_ptr (reuse raw_ptr slot)

    // Trace alloc
    if (mmTrace) {
      EmitInlineTrace("__mm_tag_alloc", UniqueLabel("mm_alloc_trace"),
        ptrSlot: 5, scopeSlot: 3, sizeSlot: 0);
    }

    // Return user_ptr
    _b.LoadLocal(VReg.Scratch0, 5);
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
  public void EmitMmRealloc(bool mmTrace, bool mmDebug) {
    _b.FunctionStart("mm_realloc", mmTrace ? 4 : 3, 0x80);

    // Panic if new_size == 0
    _b.LoadLocal(VReg.Scratch0, 2);
    var sizeOk = UniqueLabel("mm_realloc_size_ok");
    _b.JumpIfNonZero(VReg.Scratch0, sizeOk);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_realloc_zero_size");
    _b.Call("maxon_panic");
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

    // Allocate new block via slab allocator
    _b.LoadLocal(VReg.Arg0, 4); // Arg0 = new_alloc_size
    _b.Call("__slab_alloc"); // Scratch0 = new_raw_ptr
    _b.StoreLocal(5, VReg.Scratch0); // slot 5 = new_raw_ptr

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
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = user_ptr
    _b.SubRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = old_raw (slab slot base)
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__slab_free");

    // Write canary at [new_user_ptr + new_size] (MmDebug only)
    if (mmDebug) {
      _b.LoadLocal(VReg.Scratch0, 5);  // Scratch0 = new_raw_ptr
      _b.AddRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = new_user_ptr
      _b.LoadLocal(VReg.Scratch1, 2);  // Scratch1 = new_size
      _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // Scratch0 = canary_addr
      _b.MovRegImm(VReg.Scratch1, MmDebugCanaryValue);
      _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch1);
    }

    // Compute new_user_ptr = new_raw + MmHeaderSize, save to slot 6
    _b.LoadLocal(VReg.Scratch0, 5); // Scratch0 = new_raw_ptr
    _b.AddRegImm(VReg.Scratch0, MmHeaderSize); // Scratch0 = new_user_ptr
    _b.StoreLocal(6, VReg.Scratch0);

    // Trace realloc
    if (mmTrace) {
      EmitInlineTrace("__mm_tag_realloc", UniqueLabel("mm_realloc_trace"),
        ptrSlot: 6, scopeSlot: 3, sizeSlot: 2);
    }

    // Return new_user_ptr
    _b.LoadLocal(VReg.Scratch0, 6);
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
  // mm_raw_alloc(size) -> ptr
  //
  // Allocates memory via the slab allocator (__slab_alloc). The slab allocator
  // handles the 16-byte hidden header internally. Returns NULL if size == 0.
  //
  // Header layout (managed by __slab_alloc / __slab_free):
  //   [ptr - 16]: span_ptr (small) or total_alloc_size (large)
  //   [ptr -  8]: flags (0 = small/slab, 1 = large/direct OS)
  //   [ptr     ]: user data  <- returned pointer
  // =========================================================================
  // Stack slots: 0=size, 1=scope_cstr (trace only), 2=result_ptr (trace only)
  public void EmitMmRawAlloc(bool mmTrace) {
    _b.FunctionStart("mm_raw_alloc", mmTrace ? 2 : 1, mmTrace ? 0x50 : 0x30);

    // size == 0 -> return NULL
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = size
    var sizeOk = UniqueLabel("mm_raw_alloc_size_ok");
    _b.JumpIfNonZero(VReg.Scratch0, sizeOk);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(sizeOk);

    // Delegate to __slab_alloc(size) which handles header and slab/large dispatch
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = size
    _b.Call("__slab_alloc");
    // Scratch0 = result ptr

    if (mmTrace) {
      _b.StoreLocal(2, VReg.Scratch0); // save result ptr (clobbered by trace calls)
      EmitInlineTraceRawAlloc(UniqueLabel("mm_raw_alloc_trace"), sizeSlot: 0, scopeSlot: 1);
      _b.LoadLocal(VReg.Scratch0, 2); // restore result ptr
    }

    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // mm_raw_free(ptr) -> void
  //
  // Frees memory allocated by mm_raw_alloc via the slab allocator.
  // Delegates to __slab_free which reads the header to determine
  // slab-free vs OS-free. Silently returns if ptr == NULL.
  // =========================================================================
  // Stack slots: 0=ptr
  public void EmitMmRawFree(bool mmTrace) {
    _b.FunctionStart("mm_raw_free", 1, 0x20);

    // NULL check
    _b.LoadLocal(VReg.Scratch0, 0); // Scratch0 = ptr
    var notNull = UniqueLabel("mm_raw_free_not_null");
    _b.JumpIfNonZero(VReg.Scratch0, notNull);
    _b.FunctionEnd();

    _b.DefineLabel(notNull);

    if (mmTrace) {
      EmitInlineTraceRawFree();
    }

    // Delegate to __slab_free(ptr)
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = ptr
    _b.Call("__slab_free");

    _b.FunctionEnd();
  }

  // =========================================================================
  // mm_raw_realloc(old_ptr, new_size, managedPtr) -> new_ptr
  //
  // Allocates new_size bytes via mm_raw_alloc, copies old data, frees old.
  // old_byte_size = managedPtr->capacity * managedPtr->element_size.
  // =========================================================================
  // Stack slots: 0=old_ptr, 1=new_size, 2=managedPtr, 3=new_ptr, 4=old_byte_size
  public void EmitMmRawRealloc(bool mmTrace) {
    _b.FunctionStart("mm_raw_realloc", 3, mmTrace ? 0x60 : 0x50);

    // Panic if new_size == 0
    _b.LoadLocal(VReg.Scratch0, 1); // Scratch0 = new_size
    var sizeOk = UniqueLabel("mm_raw_realloc_size_ok");
    _b.JumpIfNonZero(VReg.Scratch0, sizeOk);
    _b.LeaSymdata(VReg.Arg0, "__mm_panic_realloc_zero_size");
    _b.Call("maxon_panic");
    _b.DefineLabel(sizeOk);

    // Step 1: Allocate new buffer via mm_raw_alloc(new_size, scope=NULL)
    _b.LoadLocal(VReg.Arg0, 1); // Arg0 = new_size
    if (mmTrace) _b.ZeroReg(VReg.Arg1); // scope = NULL
    _b.Call("mm_raw_alloc");
    // Return value is in Scratch0 (== Ret)
    _b.StoreLocal(3, VReg.Scratch0); // slot 3 = new_ptr

    // Step 2: Compute old_byte_size = managedPtr->capacity * managedPtr->element_size
    _b.LoadLocal(VReg.Scratch0, 2); // Scratch0 = managedPtr
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, 16); // Scratch1 = [managedPtr+16] = capacity
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, 24); // Scratch2 = [managedPtr+24] = element_size
    _b.MulRegReg(VReg.Scratch1, VReg.Scratch2); // Scratch1 = old_byte_size
    _b.StoreLocal(4, VReg.Scratch1); // slot 4 = old_byte_size

    // Step 3: memcpy(new_ptr, old_ptr, old_byte_size)
    _b.LoadLocal(VReg.Arg0, 3); // Arg0 = new_ptr (dst)
    _b.LoadLocal(VReg.Arg1, 0); // Arg1 = old_ptr (src)
    _b.LoadLocal(VReg.Arg2, 4); // Arg2 = old_byte_size (count)
    _b.Call("maxon_memcpy");

    // Step 4: Free old buffer via mm_raw_free(old_ptr)
    _b.LoadLocal(VReg.Arg0, 0); // Arg0 = old_ptr
    _b.Call("mm_raw_free");

    // Trace realloc (if enabled)
    if (mmTrace) {
      // Store new_ptr to trace slot, set scope to NULL
      _b.LoadLocal(VReg.Scratch0, 3);
      _b.StoreLocal(3, VReg.Scratch0); // ensure new_ptr is in slot 3
      _b.ZeroReg(VReg.Scratch0);
      _b.StoreLocal(5, VReg.Scratch0); // slot 5 = scope = NULL
      // ptrSlot=2 (managedPtr, for tag/rc), scopeSlot=5, sizeSlot=1 (new_size)
      EmitInlineTrace("__mm_tag_realloc", UniqueLabel("mm_raw_realloc_trace"),
        ptrSlot: 2, scopeSlot: 5, sizeSlot: 1);
    }

    // Return new_ptr
    _b.LoadLocal(VReg.Scratch0, 3);
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
}
