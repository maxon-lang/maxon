using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class X86CodeEmitter {

  // ==========================================================================
  // Memory Manager — refcount-based allocation tracking with hierarchical ownership
  // ==========================================================================
  //
  // Debug mode constants (--mm-debug):
  private const long MmDebugAllocEntryMagic = unchecked((long)0xA10CDEADA10CDEAD);
  private const long MmDebugCanaryValue = unchecked((long)0xCAFEBABEDEADC0DE);
  private const int AllocEntryAllocIdOffset = 0x48; // +72: alloc_id
  private const int AllocEntryMagicOffset = 0x50;   // +80: magic (MmDebug only)
  private static int AllocEntrySize => Compiler.MmDebug ? 88 : 80;
  //
  // AllocEntry (80 bytes, or 88 with MmDebug):
  //   +0   user_ptr        (ptr)
  //   +8   size            (u64)
  //   +16  next            (ptr)     — next sibling in parent's child list
  //   +24  prev            (ptr)     — prev sibling in parent's child list
  //   +32  child_head      (ptr)     — first child allocation
  //   +40  child_tail      (ptr)     — last child allocation
  //   +48  owner_entry     (ptr to parent AllocEntry, or 0 if independent)
  //   +56  tag_cstr        (ptr)     — debug tag
  //   +64  refcount        (u64)     — reference count
  //   +72  alloc_id        (u64)     — unique allocation identity (for trace)
  //   +80  magic           (u64)     — MmDebug only: 0xA10CDEADA10CDEAD
  //
  // Every managed allocation has an 8-byte header at [ptr-8] = pointer to its AllocEntry.

  public void EmitMemoryManagerFunctions() {
    DefineSymdata("__mm_alloc_count", new byte[8]);
    DefineSymdata("__mm_alloc_id_counter", new byte[8]);

    // Null-terminated strings for leak check output
    DefineSymdata("__mm_leak_prefix",
      "MM leak: \0"u8.ToArray());
    DefineSymdata("__mm_leak_suffix",
      " allocation(s) remain\n\0"u8.ToArray());

    // Hex conversion table (kept for panic output)
    DefineSymdata("__mm_hex_chars", "0123456789abcdef\0"u8.ToArray());
    // Scratch buffer for hex/decimal output, 24 bytes
    DefineSymdata("__mm_hex_buf", new byte[24]);

    // Shared strings used by both trace and panic output
    DefineSymdata("__mm_tag_newline", "\n\0"u8.ToArray());
    DefineSymdata("__mm_tag_null", "(null)\0"u8.ToArray());

    if (Compiler.MmTrace) {
      // Trace tag strings — only emitted when --mm-trace is passed
      DefineSymdata("__mm_tag_alloc", "alloc \0"u8.ToArray());
      DefineSymdata("__mm_tag_alloc_in", "alloc_in \0"u8.ToArray());
      DefineSymdata("__mm_tag_free", "free \0"u8.ToArray());
      DefineSymdata("__mm_tag_incref", "incref \0"u8.ToArray());
      DefineSymdata("__mm_tag_decref", "decref \0"u8.ToArray());
      DefineSymdata("__mm_tag_transfer", "transfer \0"u8.ToArray());
      DefineSymdata("__mm_tag_rc_eq", " rc=\0"u8.ToArray());
      DefineSymdata("__mm_tag_hash", " #\0"u8.ToArray());
      DefineSymdata("__mm_tag_lbracket", " [\0"u8.ToArray());
      DefineSymdata("__mm_tag_rbracket", "]\0"u8.ToArray());
      DefineSymdata("__mm_trace_depth", new byte[8]);
      DefineSymdata("__mm_tag_indent", "  \0"u8.ToArray());
      // Runtime allocation tags — only needed for trace output
      DefineSymdata("__rt_tag_buffer", "Buffer\0"u8.ToArray());
      DefineSymdata("__rt_tag_cstring", "CString\0"u8.ToArray());
      DefineSymdata("__rt_tag_cmdline_arg", "CmdLineArg\0"u8.ToArray());
      DefineSymdata("__rt_tag_find_data", "FindData\0"u8.ToArray());
      DefineSymdata("__rt_tag_dir_buffer", "DirBuffer\0"u8.ToArray());
      DefineSymdata("__rt_tag_capture_result", "CaptureResult\0"u8.ToArray());
      DefineSymdata("__rt_tag_pipe_buffer", "PipeBuffer\0"u8.ToArray());
    }

    // Panic messages for invalid memory manager calls
    DefineSymdata("__mm_panic_free_null",
      "mm_free called with NULL pointer\0"u8.ToArray());
    DefineSymdata("__mm_panic_free_unmanaged",
      "mm_free called on unmanaged pointer (no AllocEntry)\0"u8.ToArray());
    DefineSymdata("__mm_panic_alloc_in_null_parent",
      "mm_alloc_in called with NULL parent pointer\0"u8.ToArray());
    DefineSymdata("__mm_panic_alloc_in_unmanaged_parent",
      "mm_alloc_in called with unmanaged parent (no AllocEntry)\0"u8.ToArray());
    DefineSymdata("__mm_panic_realloc_unmanaged",
      "mm_realloc called on unmanaged pointer (no AllocEntry)\0"u8.ToArray());
    // Panic strings for null/unmanaged pointer passed to refcount operations
    DefineSymdata("__mm_panic_decref_null",
      "mm_decref called with NULL pointer\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_decref_unmanaged",
      "mm_decref called on unmanaged pointer (no AllocEntry)\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_incref_null",
      "mm_incref called with NULL pointer\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_incref_unmanaged",
      "mm_incref called on unmanaged pointer (no AllocEntry)\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_decref_check_null",
      "mm_decref_check called with NULL pointer\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_decref_check_unmanaged",
      "mm_decref_check called on unmanaged pointer (no AllocEntry)\n\0"u8.ToArray());
    // Panic string for decref underflow (refcount already zero = double-free / missing incref)
    DefineSymdata("__mm_panic_decref_underflow",
      "mm_decref: refcount underflow (already zero)\n\0"u8.ToArray());
    // Panic strings for __ManagedMemory bounds checks
    DefineSymdata("__mm_panic_index_oob",
      "__ManagedMemory: index out of bounds\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_byte_oob",
      "__ManagedMemory: byte index out of bounds\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_shift_oob",
      "__ManagedMemory: shift out of bounds\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_slice_oob",
      "__ManagedMemory: slice out of bounds\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_setlength_oob",
      "__ManagedMemory: setLength exceeds capacity\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_grow_shrink",
      "__ManagedMemory: grow cannot shrink capacity\n\0"u8.ToArray());

    if (Compiler.MmDebug) {
      DefineSymdata("__mm_panic_entry_magic",
        "mm_debug: AllocEntry magic corrupted (use-after-free or heap corruption)\n\0"u8.ToArray());
      DefineSymdata("__mm_panic_canary",
        "mm_debug: heap canary overwritten (buffer overrun detected)\n\0"u8.ToArray());
      DefineSymdata("__mm_panic_double_free",
        "mm_debug: double-free detected (AllocEntry magic already cleared)\n\0"u8.ToArray());
      DefineSymdata("__mm_panic_heap_null",
        "mm_debug: HeapAlloc returned NULL (out of memory)\n\0"u8.ToArray());
      DefineSymdata("__mm_panic_realloc_null",
        "mm_debug: HeapReAlloc returned NULL (out of memory)\n\0"u8.ToArray());
      DefineSymdata("__mm_panic_entry_selfcheck",
        "mm_debug: entry.user_ptr backpointer mismatch (entry corrupted)\n\0"u8.ToArray());
      DefineSymdata("__mm_panic_canary_tag", "mm_debug canary fail ptr=\0"u8.ToArray());
      DefineSymdata("__mm_panic_magic_tag", "mm_debug magic fail entry=\0"u8.ToArray());
      DefineSymdata("__mm_debug_tag_size", " size=\0"u8.ToArray());
    }

    EmitMmTracePrintTag();
    EmitMmTracePrintHex();
    EmitMmTracePrintI64();
    if (Compiler.MmTrace) {
      EmitMmTracePrintEntryTag();
    }
    EmitMmAlloc();
    EmitMmAllocIn();
    EmitMmRealloc();
    EmitMmFreeEntry();
    EmitMmFree();
    EmitMmFreeIfNonnull();
    EmitMmFreeChildOf();
    EmitMmIncref();
    EmitMmDecref();
    EmitMmDecrefCheck();
    EmitMmDecrefIfOwned();
    EmitMmDecrefManagedElements();
    EmitMmClearManagedElements();
    EmitMmLeakCheck();
    EmitMmValidatePtr();
    EmitChainInsertFirst();
    EmitChainInsertLast();
    EmitChainInsertAfter();
    EmitChainInsertBefore();
    EmitChainUnlink();
    EmitChainClear();
    EmitChainClearManaged();
    EmitChainDecrefValues();
    if (Compiler.MmTrace) {
      EmitMmTracePrintIndent();
      EmitMmTraceAlloc();
      EmitMmTraceFree();
      EmitMmTraceIncref();
      EmitMmTraceDecref();
      EmitMmTraceTransfer();
    }
  }

  // -------------------------------------------------------------------------
  // mm_trace_print_tag(tag_symdata_label) — print a symdata tag string to stderr
  // Args: rcx = pointer to null-terminated string
  // -------------------------------------------------------------------------
  private void EmitMmTracePrintTag() {
    EmitRuntimeFunctionStart("mm_trace_print_tag", 1, 0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_print_hex(value) — print a 64-bit value as "0xHEX" to stderr
  // Args: rcx = 64-bit value to print
  // -------------------------------------------------------------------------
  private void EmitMmTracePrintHex() {
    // Build "0x" + 16 hex digits + null into __mm_hex_buf, then call maxon_write_stderr
    EmitRuntimeFunctionStart("mm_trace_print_hex", 1, 0x30);
    // Save value
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = value

    // Write "0x" prefix to buf
    EmitLeaRegSymdataRel(X86Register.Rdi, "__mm_hex_buf");
    EmitBytes(0xC6, 0x07, 0x30); // MOV byte [rdi], '0'
    EmitBytes(0xC6, 0x47, 0x01, 0x78); // MOV byte [rdi+1], 'x'

    // Load hex chars table
    EmitLeaRegSymdataRel(X86Register.Rsi, "__mm_hex_chars");

    // Convert 16 nybbles (high to low)
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = value
    EmitMovRegImm(X86Register.Rcx, 15); // digit index (15..0)

    DefineLabel("mm_trace_hex_loop");
    // Extract low nybble: RDX = RAX & 0xF
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitBytes(0x48, 0x83, 0xE2, 0x0F); // AND rdx, 0xF
    // Load hex char: DL = [rsi + rdx]
    EmitBytes(0x0F, 0xB6, 0x14, 0x16); // MOVZX edx, byte [rsi+rdx]
    // Store at buf[2 + rcx]
    EmitBytes(0x88, 0x54, 0x0F, 0x02); // MOV byte [rdi + rcx + 2], dl
    // Shift value right by 4
    EmitBytes(0x48, 0xC1, 0xE8, 0x04); // SHR rax, 4
    // Decrement counter and loop while rcx >= 0
    EmitBytes(0x48, 0xFF, 0xC9); // DEC rcx
    EmitJcc("ge", "mm_trace_hex_loop");

    // Null-terminate at buf[18]
    EmitBytes(0xC6, 0x47, 0x12, 0x00); // MOV byte [rdi+18], 0

    // Call maxon_write_stderr(buf)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_print_i64(value) — print a 64-bit integer in decimal to stderr
  // Args: rcx = 64-bit value to print
  // -------------------------------------------------------------------------
  private void EmitMmTracePrintI64() {
    EmitRuntimeFunctionStart("mm_trace_print_i64", 1, 0x30);
    // Save value
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = value

    // If value == 0, print "0" and return
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "mm_trace_i64_nonzero");

    // Write '0' + null to __mm_hex_buf and print it
    EmitLeaRegSymdataRel(X86Register.Rdi, "__mm_hex_buf");
    EmitBytes(0xC6, 0x07, 0x30); // MOV byte [rdi], '0'
    EmitBytes(0xC6, 0x47, 0x01, 0x00); // MOV byte [rdi+1], 0
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);
    EmitJmp("mm_trace_i64_done");

    DefineLabel("mm_trace_i64_nonzero");

    // Write digits right-to-left into __mm_hex_buf
    // RDI = buf end pointer (buf + 23 for null terminator position)
    EmitLeaRegSymdataRel(X86Register.Rdi, "__mm_hex_buf");
    EmitAddRegImm(X86Register.Rdi, 23);
    EmitBytes(0xC6, 0x07, 0x00); // MOV byte [rdi], 0 — null terminator

    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = value
    EmitMovRegImm(X86Register.Rcx, 10); // RCX = divisor

    DefineLabel("mm_trace_i64_loop");
    // RDX:RAX / RCX -> quotient in RAX, remainder in RDX
    EmitBytes(0x48, 0x31, 0xD2); // XOR rdx, rdx
    EmitBytes(0x48, 0xF7, 0xF1); // DIV rcx — RAX = quotient, RDX = remainder
    // Convert remainder to ASCII: DL + '0'
    EmitBytes(0x80, 0xC2, 0x30); // ADD dl, 0x30
    // Decrement pointer and store digit
    EmitBytes(0x48, 0xFF, 0xCF); // DEC rdi
    EmitBytes(0x88, 0x17); // MOV [rdi], dl
    // Loop if quotient != 0
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_trace_i64_loop");

    // Print the string starting at rdi
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    DefineLabel("mm_trace_i64_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // Debug helpers — inline check sequences emitted when Compiler.MmDebug is true.
  // These must be called within an Emit* function body (they emit code inline,
  // not as separate functions).
  // -------------------------------------------------------------------------

  /// <summary>
  /// Emit inline code to validate AllocEntry magic at [RAX + 0x50].
  /// RAX must contain the entry pointer. Uses RCX and RDX as scratch.
  /// labelPrefix must be unique within the calling function.
  /// </summary>
  private void EmitMmDebugCheckEntryMagic(string labelPrefix) {
    // Print diagnostics before panicking so we know which entry failed
    // MOV rcx, [rax+AllocEntryMagicOffset] — entry.magic
    EmitBytes(0x48, 0x8B, 0x48, (byte)AllocEntryMagicOffset);
    EmitMovRegImm(X86Register.Rdx, MmDebugAllocEntryMagic);
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("e", labelPrefix + "_magic_ok");
    // Print "mm_debug magic fail entry=0xHEX\n" then panic
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // save entry ptr (may already be there)
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_magic_tag");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // entry ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_hex")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_entry_magic");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel(labelPrefix + "_magic_ok");
  }

  /// <summary>
  /// Emit inline code to check that RAX (HeapAlloc result) is not NULL.
  /// panicLabel is the symdata label for the panic message.
  /// </summary>
  private void EmitMmDebugCheckHeapAllocNull(string labelPrefix, string panicLabel) {
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", labelPrefix + "_heap_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, panicLabel);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel(labelPrefix + "_heap_ok");
  }

  // -------------------------------------------------------------------------
  // mm_trace_print_entry_tag(entry_ptr) — print tag_cstr from an AllocEntry
  // Args: rcx = entry pointer
  // If entry.tag_cstr is NULL, prints "(null)" instead.
  // -------------------------------------------------------------------------
  private void EmitMmTracePrintEntryTag() {
    EmitRuntimeFunctionStart("mm_trace_print_entry_tag", 1, 0x30);

    // Load entry.tag_cstr from offset +56
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x38); // MOV rax, [rax+56] — entry.tag_cstr

    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_trace_entry_tag_null");

    // Non-null: print the tag string
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);
    EmitJmp("mm_trace_entry_tag_done");

    DefineLabel("mm_trace_entry_tag_null");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    DefineLabel("mm_trace_entry_tag_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mm_trace_print_indent() -> void
  /// Prints 2 spaces for each level of depth in __mm_trace_depth.
  /// </summary>
  private void EmitMmTracePrintIndent() {
    EmitRuntimeFunctionStart("mm_trace_print_indent", 0, 0x30);
    // Load depth into rax
    EmitSymdataLoadI64(X86Register.Rax, "__mm_trace_depth");
    // Store as loop counter at [rbp-8]
    EmitBytes(0x48, 0x89, 0x45, 0xF8); // MOV [rbp-8], rax
    DefineLabel("mm_trace_indent_loop");
    // Load counter
    EmitBytes(0x48, 0x8B, 0x45, 0xF8); // MOV rax, [rbp-8]
    // Test if zero
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_trace_indent_done");
    // Print "  "
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_indent");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    // Decrement counter
    EmitBytes(0x48, 0x8B, 0x45, 0xF8); // MOV rax, [rbp-8]
    EmitBytes(0x48, 0xFF, 0xC8); // DEC rax
    EmitBytes(0x48, 0x89, 0x45, 0xF8); // MOV [rbp-8], rax
    // Jump back
    EmitJmp("mm_trace_indent_loop");
    DefineLabel("mm_trace_indent_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_alloc(size_in_rcx, tag_cstr_in_rdx) -> user_ptr_in_rax
  // Allocates a tracked heap object with an AllocEntry metadata node.
  // refcount starts at 0; the caller's assignment will incref.
  // -------------------------------------------------------------------------
  private void EmitMmAlloc() {
    // Stack: [rbp-8]=size, [rbp-16]=tag, [rbp-40]=raw_ptr, [rbp-48]=entry_ptr,
    //        [rbp-56]=user_ptr, [rbp-64]=alloc_size
    EmitRuntimeFunctionStart("mm_alloc", 2, 0x80);

    // HeapAlloc(size + 8 [+ 8 canary]) for payload with backpointer header
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = size
    EmitAddRegImm(X86Register.Rax, Compiler.MmDebug ? 16 : 8);
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // [rbp-64] = alloc size
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x40);
    if (Compiler.MmDebug) EmitMmDebugCheckHeapAllocNull("mm_alloc_payload", "__mm_panic_heap_null");
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = raw_ptr

    // HeapAlloc(AllocEntrySize) for AllocEntry
    EmitMovRegImm(X86Register.Rax, AllocEntrySize);
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // [rbp-64] = AllocEntrySize
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x40);
    if (Compiler.MmDebug) EmitMmDebugCheckHeapAllocNull("mm_alloc_entry", "__mm_panic_heap_null");
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = entry_ptr

    // entry.user_ptr = raw_ptr + 8
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = raw_ptr
    EmitAddRegImm(X86Register.Rcx, 8);
    EmitMovMemReg(-0x38, X86Register.Rcx, 8); // [rbp-56] = user_ptr
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = entry
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — entry.user_ptr

    // entry.size = size
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — entry.size

    // entry.tag_cstr = tag arg (at +56)
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x38); // MOV [rax+56], rcx — entry.tag_cstr

    // entry.next, prev, child_head, child_tail, owner_entry, refcount = 0
    // (already zero from HEAP_ZERO_MEMORY)

    // Set backpointer header: [raw_ptr] = entry_ptr
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = raw_ptr
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = entry_ptr
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — [raw_ptr] = entry

    // Write entry magic and canary (MmDebug only)
    if (Compiler.MmDebug) {
      // entry.magic = AllocEntryMagic at [entry+0x50]
      EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = entry
      EmitMovRegImm(X86Register.Rcx, MmDebugAllocEntryMagic);
      EmitBytes(0x48, 0x89, 0x48, (byte)AllocEntryMagicOffset); // MOV [rax+magic], rcx

      // Canary at [user_ptr + size]
      EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = user_ptr
      EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = size
      EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx — rax = user_ptr + size
      EmitMovRegImm(X86Register.Rcx, MmDebugCanaryValue);
      EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — write canary
    }

    // Increment global alloc counter
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_count");
    EmitBytes(0x48, 0xFF, 0xC0); // INC rax
    EmitSymdataStoreI64(X86Register.Rax, "__mm_alloc_count");

    // Assign alloc_id: ++__mm_alloc_id_counter → entry.alloc_id
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_id_counter");
    EmitBytes(0x48, 0xFF, 0xC0); // INC rax
    EmitSymdataStoreI64(X86Register.Rax, "__mm_alloc_id_counter");
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // entry_ptr
    EmitBytes(0x48, 0x89, 0x41, (byte)AllocEntryAllocIdOffset); // MOV [rcx+alloc_id], rax

    // Return user_ptr (raw_ptr + 8)
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_alloc_in(size_in_rcx, parent_user_ptr_in_rdx, tag_cstr_in_r8) -> user_ptr_in_rax
  // -------------------------------------------------------------------------
  private void EmitMmAllocIn() {
    // Stack: [rbp-8]=size, [rbp-16]=parent_user_ptr, [rbp-24]=tag,
    //        [rbp-40]=raw_ptr, [rbp-48]=entry_ptr, [rbp-56]=user_ptr,
    //        [rbp-64]=parent_entry, [rbp-72]=alloc_size
    EmitRuntimeFunctionStart("mm_alloc_in", 3, 0x80);

    // If parent_user_ptr == NULL, panic
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = parent_user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_alloc_in_parent_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_alloc_in_null_parent");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_alloc_in_parent_ok");

    // Load parent's AllocEntry from [parent_user_ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry ptr from backpointer
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // [rbp-64] = parent_entry

    // If parent_entry == NULL, panic (unmanaged parent)
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_alloc_in_parent_entry_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_alloc_in_unmanaged_parent");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_alloc_in_parent_entry_ok");

    // Validate parent entry magic (MmDebug only)
    if (Compiler.MmDebug) {
      EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = parent_entry
      EmitMmDebugCheckEntryMagic("mm_alloc_in_parent");
    }

    // HeapAlloc(size + 8 [+ 8 canary]) for payload
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = size
    EmitAddRegImm(X86Register.Rax, Compiler.MmDebug ? 16 : 8);
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // [rbp-72] = alloc size
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x48);
    if (Compiler.MmDebug) EmitMmDebugCheckHeapAllocNull("mm_alloc_in_payload", "__mm_panic_heap_null");
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = raw_ptr

    // HeapAlloc(AllocEntrySize) for AllocEntry
    EmitMovRegImm(X86Register.Rax, AllocEntrySize);
    EmitMovMemReg(-0x48, X86Register.Rax, 8);
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x48);
    if (Compiler.MmDebug) EmitMmDebugCheckHeapAllocNull("mm_alloc_in_entry", "__mm_panic_heap_null");
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = entry_ptr

    // entry.user_ptr = raw_ptr + 8
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitAddRegImm(X86Register.Rcx, 8);
    EmitMovMemReg(-0x38, X86Register.Rcx, 8); // [rbp-56] = user_ptr
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — entry.user_ptr

    // entry.size = size
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — entry.size

    // entry.tag_cstr = tag arg (r8 saved at [rbp-24]) — at +56
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x38); // MOV [rax+56], rcx — entry.tag_cstr

    // entry.owner_entry = parent_entry
    EmitMovRegMem(X86Register.Rcx, -0x40, 8); // RCX = parent_entry
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x30); // MOV [rax+48], rcx — entry.owner_entry

    // entry.next, prev, child_head, child_tail, refcount = 0 (from HEAP_ZERO_MEMORY)

    // Set backpointer header: [raw_ptr] = entry_ptr
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — [raw_ptr] = entry

    // Write entry magic and canary (MmDebug only)
    if (Compiler.MmDebug) {
      // entry.magic = AllocEntryMagic
      EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = entry
      EmitMovRegImm(X86Register.Rcx, MmDebugAllocEntryMagic);
      EmitBytes(0x48, 0x89, 0x48, (byte)AllocEntryMagicOffset); // MOV [rax+magic], rcx

      // Canary at [user_ptr + size]
      EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = user_ptr
      EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = size
      EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx — rax = user_ptr + size
      EmitMovRegImm(X86Register.Rcx, MmDebugCanaryValue);
      EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — write canary
    }

    // Append entry to parent_entry's child list
    EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = parent_entry
    EmitBytes(0x48, 0x8B, 0x40, 0x28); // MOV rax, [rax+40] — parent_entry.child_tail
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_alloc_in_empty_children");

    // Non-empty: old_tail.next = entry, entry.prev = old_tail, parent.child_tail = entry
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = entry
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — old_tail.next = entry
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x41, 0x18); // MOV [rcx+24], rax — entry.prev = old_tail
    EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = parent_entry
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x28); // MOV [rax+40], rcx — parent.child_tail = entry
    EmitJmp("mm_alloc_in_children_done");

    // Empty: parent.child_head = entry, parent.child_tail = entry
    DefineLabel("mm_alloc_in_empty_children");
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x20); // MOV [rax+32], rcx — parent.child_head = entry
    EmitBytes(0x48, 0x89, 0x48, 0x28); // MOV [rax+40], rcx — parent.child_tail = entry

    DefineLabel("mm_alloc_in_children_done");

    // Increment global alloc counter
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_count");
    EmitBytes(0x48, 0xFF, 0xC0); // INC rax
    EmitSymdataStoreI64(X86Register.Rax, "__mm_alloc_count");

    // Assign alloc_id: ++__mm_alloc_id_counter → entry.alloc_id
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_id_counter");
    EmitBytes(0x48, 0xFF, 0xC0); // INC rax
    EmitSymdataStoreI64(X86Register.Rax, "__mm_alloc_id_counter");
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // entry_ptr
    EmitBytes(0x48, 0x89, 0x41, (byte)AllocEntryAllocIdOffset); // MOV [rcx+alloc_id], rax

    if (Compiler.MmTrace) {
      // Trace: "alloc_in " + entry.tag_cstr + newline
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_alloc_in");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
      EmitMovRegMem(X86Register.Rcx, -0x30, 8); // entry_ptr
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    }

    // Return user_ptr
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_realloc(user_ptr_in_rcx, new_size_in_rdx, tag_cstr_in_r8) -> new_user_ptr_in_rax
  // -------------------------------------------------------------------------
  private void EmitMmRealloc() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=new_size, [rbp-24]=tag,
    //        [rbp-40]=entry, [rbp-48]=new_raw, [rbp-56]=alloc_size, [rbp-64]=old_raw
    EmitRuntimeFunctionStart("mm_realloc", 3, 0x80);

    // If ptr == NULL, call mm_alloc(new_size, tag) and return
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_realloc_not_null");

    // NULL path: delegate to mm_alloc
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = new_size
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // RDX = tag
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc")); EmitDword(0);
    EmitJmp("mm_realloc_done");

    DefineLabel("mm_realloc_not_null");

    // Load entry = [ptr - 8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry from backpointer
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = entry

    // If entry == 0, panic (unmanaged pointer)
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_realloc_managed");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_realloc_unmanaged");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);

    DefineLabel("mm_realloc_managed");

    // Validate entry magic (MmDebug only)
    if (Compiler.MmDebug) {
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
      EmitMmDebugCheckEntryMagic("mm_realloc");

      // Verify old canary at [user_ptr + entry.size]
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
      EmitBytes(0x48, 0x8B, 0x48, 0x08); // MOV rcx, [rax+8] — entry.size (old size)
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
      EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx — rax = user_ptr + old_size
      EmitMovRegImm(X86Register.Rcx, MmDebugCanaryValue);
      EmitBytes(0x48, 0x39, 0x08); // CMP [rax], rcx
      EmitJcc("e", "mm_realloc_canary_ok");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_canary");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
      DefineLabel("mm_realloc_canary_ok");
    }

    // Managed path: HeapReAlloc(user_ptr - 8, new_size + 8 [+ 8 canary])
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // user_ptr
    EmitSubRegImm(X86Register.Rax, 8);
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // [rbp-64] = old_raw (ptr - 8)

    EmitMovRegMem(X86Register.Rax, -0x10, 8); // new_size
    EmitAddRegImm(X86Register.Rax, Compiler.MmDebug ? 16 : 8);
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // [rbp-56] = realloc size

    EmitHeapCall("HeapReAlloc", 0x08, rbpSlotR8: -0x40, rbpSlotR9: -0x38);
    if (Compiler.MmDebug) EmitMmDebugCheckHeapAllocNull("mm_realloc_heap", "__mm_panic_realloc_null");
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = new_raw

    // Update entry.user_ptr = new_raw + 8
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = new_raw
    EmitAddRegImm(X86Register.Rcx, 8);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — entry.user_ptr = new_raw + 8

    // Update entry.size = new_size
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — entry.size = new_size

    // Set backpointer: [new_raw] = entry
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = new_raw
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = entry
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — [new_raw] = entry

    // Write new canary (MmDebug only)
    if (Compiler.MmDebug) {
      // canary at [new_user_ptr + new_size]
      EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = new_raw
      EmitAddRegImm(X86Register.Rax, 8); // RAX = new_user_ptr
      EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = new_size
      EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx — rax = new_user_ptr + new_size
      EmitMovRegImm(X86Register.Rcx, MmDebugCanaryValue);
      EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — write canary
    }

    // Return new_raw + 8
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // new_raw
    EmitAddRegImm(X86Register.Rax, 8);

    DefineLabel("mm_realloc_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_free_entry(entry_ptr_in_rcx) -> void (internal)
  // Walks children: if refcount > 0, detach (clear owner_entry).
  // If refcount == 0, recursively free. Then frees the payload and entry.
  // -------------------------------------------------------------------------
  private void EmitMmFreeEntry() {
    // Stack: [rbp-8]=entry_ptr, [rbp-40]=current_child, [rbp-48]=next_child,
    //        [rbp-56]=user_ptr, [rbp-64]=heap_free_arg
    EmitRuntimeFunctionStart("mm_free_entry", 1, 0x80);

    // Debug checks (MmDebug only)
    if (Compiler.MmDebug) {
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry

      // Double-free check: if magic == 0, entry was already freed
      EmitBytes(0x48, 0x8B, 0x48, (byte)AllocEntryMagicOffset); // MOV rcx, [rax+magic]
      EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
      EmitJcc("nz", "mm_free_entry_not_double_free");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_double_free");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
      DefineLabel("mm_free_entry_not_double_free");

      // Magic validation
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry
      EmitMmDebugCheckEntryMagic("mm_free_entry");

      // Self-consistency: [entry.user_ptr - 8] should == entry
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry
      EmitBytes(0x48, 0x8B, 0x08); // MOV rcx, [rax] — entry.user_ptr
      EmitBytes(0x48, 0x8B, 0x49, 0xF8); // MOV rcx, [rcx-8] — backpointer
      EmitCmpRegReg(X86Register.Rcx, X86Register.Rax); // backpointer == entry?
      EmitJcc("e", "mm_free_entry_selfcheck_ok");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_entry_selfcheck");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
      DefineLabel("mm_free_entry_selfcheck_ok");

      // Canary check: [user_ptr + size] == canary
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry
      EmitBytes(0x48, 0x8B, 0x48, 0x08); // MOV rcx, [rax+8] — entry.size
      EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry.user_ptr
      EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx — rax = user_ptr + size
      EmitMovRegImm(X86Register.Rcx, MmDebugCanaryValue);
      EmitBytes(0x48, 0x39, 0x08); // CMP [rax], rcx
      EmitJcc("e", "mm_free_entry_canary_ok");
      // Print diagnostic: "mm_debug canary fail ptr=0xHEX size=N\n"
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_canary_tag");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // entry
      EmitBytes(0x48, 0x8B, 0x08); // MOV rcx, [rax] — entry.user_ptr
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_hex")); EmitDword(0);
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_debug_tag_size");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // entry
      EmitBytes(0x48, 0x8B, 0x48, 0x08); // MOV rcx, [rax+8] — entry.size
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_canary");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
      DefineLabel("mm_free_entry_canary_ok");
    }

    // Walk entry.child_head: for each child, detach or recursively free
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry
    EmitBytes(0x48, 0x8B, 0x40, 0x20); // MOV rax, [rax+32] — entry.child_head
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = current_child

    DefineLabel("mm_free_entry_child_loop");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_entry_children_done");

    // Save next before any modification
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] — child.next
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = next_child

    // Check child.refcount (at +64): if > 0, detach instead of freeing
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current_child (entry)
    EmitBytes(0x48, 0x83, 0x78, 0x40, 0x00); // CMP qword [rax+64], 0 — child.refcount
    EmitJcc("ne", "mm_free_entry_detach_child");

    // refcount == 0: Recurse — mm_free_entry(current_child)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free_entry")); EmitDword(0);
    EmitJmp("mm_free_entry_child_advance");

    // refcount > 0: Detach — clear owner_entry so child becomes independent
    DefineLabel("mm_free_entry_detach_child");
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = child entry
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x48, 0x30); // MOV [rax+48], rcx — child.owner_entry = 0
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — child.next = 0
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — child.prev = 0

    DefineLabel("mm_free_entry_child_advance");
    // Advance: current_child = next_child
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitJmp("mm_free_entry_child_loop");

    DefineLabel("mm_free_entry_children_done");

    // Decrement global alloc counter
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_count");
    EmitBytes(0x48, 0xFF, 0xC8); // DEC rax
    EmitSymdataStoreI64(X86Register.Rax, "__mm_alloc_count");

    // Poison user memory and zero magic (MmDebug only)
    if (Compiler.MmDebug) {
      // Poison fill: memset(entry.user_ptr, 0xDE, entry.size)
      EmitPushReg(X86Register.Rdi); // save RDI (callee-saved on Win64)
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry
      EmitBytes(0x48, 0x8B, 0x38); // MOV rdi, [rax] — rdi = entry.user_ptr
      EmitBytes(0x48, 0x8B, 0x48, 0x08); // MOV rcx, [rax+8] — rcx = entry.size
      EmitMovRegImm(X86Register.Rax, 0xDE); // AL = 0xDE
      EmitBytes(0xF3, 0xAA); // REP STOSB — fill [rdi..rdi+rcx) with 0xDE
      EmitPopReg(X86Register.Rdi); // restore RDI

      // Zero entry magic (for double-free detection)
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry
      EmitMovRegImm(X86Register.Rcx, 0);
      EmitBytes(0x48, 0x89, 0x48, (byte)AllocEntryMagicOffset); // MOV [rax+magic], rcx — zero magic
    }

    // HeapFree the payload: entry.user_ptr - 8
    // Zero the backpointer first so double-free is detected as "unmanaged pointer"
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry.user_ptr
    EmitBytes(0x48, 0xC7, 0x40, 0xF8, 0x00, 0x00, 0x00, 0x00); // MOV qword [rax-8], 0
    EmitSubRegImm(X86Register.Rax, 8);
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // [rbp-56] = user_ptr - 8
    EmitHeapCall("HeapFree", 0, rbpSlotR8: -0x38);

    // HeapFree the entry node itself
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovMemReg(-0x38, X86Register.Rax, 8);
    EmitHeapCall("HeapFree", 0, rbpSlotR8: -0x38);

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_free(user_ptr_in_rcx) -> void
  // Unlinks an allocation from its parent (if any), then calls mm_free_entry.
  // -------------------------------------------------------------------------
  private void EmitMmFree() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=scope_cstr (MmTrace only), [rbp-40]=entry, [rbp-48]=prev,
    //        [rbp-56]=next, [rbp-64]=owner_entry
    EmitRuntimeFunctionStart("mm_free", Compiler.MmTrace ? 2 : 1, 0x80);

    // If ptr == NULL, panic — null should never be passed to mm_free
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_free_ptr_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_free_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_free_ptr_ok");

    // Load entry = [ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax]
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = entry
    // If entry == NULL, panic — mm_free called on unmanaged pointer
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_free_entry_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_free_unmanaged");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_free_entry_ok");

    // Validate entry magic (MmDebug only)
    if (Compiler.MmDebug) {
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
      EmitMmDebugCheckEntryMagic("mm_free");
    }

    // Trace free — emit before unlinking/freeing while entry is still valid
    if (Compiler.MmTrace) {
      EmitMovRegMem(X86Register.Rcx, -0x08, 8); // user_ptr
      EmitMovRegMem(X86Register.Rdx, -0x10, 8); // scope_cstr (from caller, NULL if not provided)
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_free")); EmitDword(0);
    }

    // Check if parent-owned: entry.owner_entry (+48) != 0
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x8B, 0x40, 0x30); // MOV rax, [rax+48] — entry.owner_entry
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // [rbp-64] = owner_entry
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_unlinked"); // not parent-owned, skip unlinking

    // Parent-owned: unlink from parent entry's child list
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — entry.prev
    EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-48] = prev
    EmitBytes(0x48, 0x8B, 0x48, 0x10); // MOV rcx, [rax+16] — entry.next
    EmitMovMemReg(-0x38, X86Register.Rcx, 8); // [rbp-56] = next

    // if prev: prev.next = next  else: owner.child_head = next
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_parent_no_prev");
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — prev.next = next
    EmitJmp("mm_free_parent_prev_done");
    DefineLabel("mm_free_parent_no_prev");
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x20); // MOV [rax+32], rcx — owner.child_head = next
    DefineLabel("mm_free_parent_prev_done");

    // if next: next.prev = prev  else: owner.child_tail = prev
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_parent_no_next");
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — next.prev = prev
    EmitJmp("mm_free_parent_next_done");
    DefineLabel("mm_free_parent_no_next");
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x28); // MOV [rax+40], rcx — owner.child_tail = prev
    DefineLabel("mm_free_parent_next_done");

    DefineLabel("mm_free_unlinked");

    // Call mm_free_entry(entry)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free_entry")); EmitDword(0);

    DefineLabel("mm_free_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_free_if_nonnull(user_ptr_in_rcx) -> void
  // Calls mm_free if ptr is non-null and has a valid backpointer. Silently
  // skips null pointers — used for global struct reassignment where the old
  // value may not have been initialized yet.
  // -------------------------------------------------------------------------
  private void EmitMmFreeIfNonnull() {
    EmitRuntimeFunctionStart("mm_free_if_nonnull", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_if_nonnull_skip");
    // Check backpointer: if [ptr-8] == 0, skip (already freed or unmanaged)
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax]
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_if_nonnull_skip");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    if (Compiler.MmTrace) EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // scope = NULL
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free")); EmitDword(0);
    DefineLabel("mm_free_if_nonnull_skip");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_free_child_of(child_ptr_in_rcx, parent_ptr_in_rdx) -> void
  // Frees child_ptr ONLY if it is still a direct child of parent_ptr.
  // If child_ptr has been reparented elsewhere (e.g., captured as an enum
  // payload), the free is skipped. Used for self-field write-through where
  // the old field value may have been reparented under the new value.
  // -------------------------------------------------------------------------
  private void EmitMmFreeChildOf() {
    EmitRuntimeFunctionStart("mm_free_child_of", 2, 0x30);
    // [rbp-8] = child_ptr, [rbp-16] = parent_ptr

    // If child_ptr == NULL, skip
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = child_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_child_of_skip");

    // Load child_entry = [child_ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — child_entry
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_child_of_skip"); // unmanaged
    if (Compiler.MmDebug) EmitMmDebugCheckEntryMagic("mm_free_child_of");
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = child_entry

    // Load parent_entry = [parent_ptr - 8]
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = parent_ptr
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — parent_entry
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_child_of_skip"); // parent unmanaged

    // Check child_entry.owner_entry (+48) == parent_entry
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = child_entry
    EmitBytes(0x48, 0x8B, 0x49, 0x30); // MOV rcx, [rcx+48] — child_entry.owner_entry
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rax); // owner_entry == parent_entry?
    EmitJcc("ne", "mm_free_child_of_skip"); // not a child of parent, skip

    // Still a child of parent — free it
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // child_ptr
    if (Compiler.MmTrace) EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // scope = NULL
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free")); EmitDword(0);

    DefineLabel("mm_free_child_of_skip");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_incref(user_ptr_in_rcx) -> void
  // Increments refcount for any managed allocation.
  // Panics on NULL or unmanaged pointers — callers must guard against null.
  // -------------------------------------------------------------------------
  private void EmitMmIncref() {
    EmitRuntimeFunctionStart("mm_incref", 1, 0x30);

    // NULL check — panic
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_incref_not_null");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_incref_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_incref_not_null");

    // Load entry = [user_ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry from backpointer

    // Unmanaged check — panic
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_incref_managed");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_incref_unmanaged");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_incref_managed");

    // Validate entry magic (MmDebug only)
    if (Compiler.MmDebug) EmitMmDebugCheckEntryMagic("mm_incref");

    // Increment refcount: entry.refcount += 1 (at +64)
    EmitBytes(0x48, 0xFF, 0x40, 0x40); // INC qword [rax+64] — entry.refcount++

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_decref(user_ptr_in_rcx) -> void
  // Decrements refcount. If it reaches zero, frees it via mm_free_entry.
  // Panics on NULL or unmanaged pointers — callers must guard against null.
  // Panics if refcount is already zero (underflow = bug).
  // -------------------------------------------------------------------------
  private void EmitMmDecref() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=entry
    EmitRuntimeFunctionStart("mm_decref", 1, 0x30);

    // NULL check — panic
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_decref_not_null");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_decref_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_decref_not_null");

    // Load entry = [user_ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = entry

    // Unmanaged check — panic
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_decref_managed");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_decref_unmanaged");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_decref_managed");

    // Validate entry magic (MmDebug only)
    if (Compiler.MmDebug) EmitMmDebugCheckEntryMagic("mm_decref");

    // Check refcount (at +64): if already zero, panic (refcount underflow = bug)
    EmitBytes(0x48, 0x83, 0x78, 0x40, 0x00); // CMP qword [rax+64], 0 — entry.refcount
    EmitJcc("ne", "mm_decref_has_refs");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_decref_underflow");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_decref_has_refs");

    // Decrement refcount
    EmitBytes(0x48, 0xFF, 0x48, 0x40); // DEC qword [rax+64] — entry.refcount--

    // Check if refcount reached zero
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitBytes(0x48, 0x83, 0x78, 0x40, 0x00); // CMP qword [rax+64], 0 — entry.refcount
    EmitJcc("ne", "mm_decref_done");

    // refcount == 0 — free via mm_free(user_ptr) so parent-owned entries are properly unlinked
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitBytes(0x48, 0x8B, 0x08); // MOV rcx, [rax] — entry.user_ptr
    if (Compiler.MmTrace) EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // scope = NULL
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free")); EmitDword(0);

    DefineLabel("mm_decref_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_decref_check(user_ptr_in_rcx) -> new_refcount_in_rax
  // Like mm_decref but returns the new refcount instead of auto-freeing.
  // Caller uses return value to decide if inline destructor + mm_free is needed.
  // Panics on NULL or unmanaged pointers — callers must guard against null.
  // -------------------------------------------------------------------------
  private void EmitMmDecrefCheck() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=entry
    EmitRuntimeFunctionStart("mm_decref_check", 1, 0x30);

    // NULL check — panic
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_decref_check_not_null");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_decref_check_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_decref_check_not_null");

    // Load entry = [user_ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = entry

    // Unmanaged check — panic
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_decref_check_managed");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_decref_check_unmanaged");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_decref_check_managed");

    // Validate entry magic (MmDebug only)
    if (Compiler.MmDebug) EmitMmDebugCheckEntryMagic("mm_decref_check");

    // Check refcount (at +64): if already zero, panic (refcount underflow = bug)
    EmitBytes(0x48, 0x83, 0x78, 0x40, 0x00); // CMP qword [rax+64], 0 — entry.refcount
    EmitJcc("ne", "mm_decref_check_has_refs");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_decref_underflow");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_decref_check_has_refs");

    // Decrement refcount
    EmitBytes(0x48, 0xFF, 0x48, 0x40); // DEC qword [rax+64] — entry.refcount--

    // Return new refcount
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitBytes(0x48, 0x8B, 0x40, 0x40); // MOV rax, [rax+64] — entry.refcount

    DefineLabel("mm_decref_check_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_decref_if_owned(user_ptr_in_rcx) -> void
  // Releases one external reference from a child-owned allocation.
  // Child-owned fields (e.g. __ManagedMemory) start at rc=0 — their parent's
  // mm_free handles cleanup. When shared with another struct, the child is
  // incref'd (rc>0). This function decrements only the external references
  // (rc>0) without freeing, since the parent's mm_free will cascade-free or
  // detach based on the final refcount.
  // -------------------------------------------------------------------------
  private void EmitMmDecrefIfOwned() {
    EmitRuntimeFunctionStart("mm_decref_if_owned", 1, 0x30);

    // NULL check — just return
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_decref_if_owned_not_null");
    EmitJmp("mm_decref_if_owned_done");
    DefineLabel("mm_decref_if_owned_not_null");

    // Load entry = [user_ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_decref_if_owned_managed");
    EmitJmp("mm_decref_if_owned_done");
    DefineLabel("mm_decref_if_owned_managed");

    // Check refcount at entry+64: if zero, skip (no external references)
    EmitBytes(0x48, 0x83, 0x78, 0x40, 0x00); // CMP qword [rax+64], 0
    EmitJcc("ne", "mm_decref_if_owned_has_refs");
    EmitJmp("mm_decref_if_owned_done");
    DefineLabel("mm_decref_if_owned_has_refs");

    // Check owner_entry at entry+48: if non-zero, this is a true child allocation
    // and parent's mm_free cascade will handle it — just decrement rc
    EmitBytes(0x48, 0x83, 0x78, 0x30, 0x00); // CMP qword [rax+48], 0
    EmitJcc("ne", "mm_decref_if_owned_is_child");

    // Independent allocation stored in a child-owned field (e.g. from clone/sharing):
    // use mm_decref for proper cleanup including freeing when rc reaches 0
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = original user_ptr (first arg)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);
    EmitJmp("mm_decref_if_owned_done");

    DefineLabel("mm_decref_if_owned_is_child");
    // True child: decrement refcount without freeing — parent's mm_free decides the fate
    EmitBytes(0x48, 0xFF, 0x48, 0x40); // DEC qword [rax+64] — entry.refcount--

    DefineLabel("mm_decref_if_owned_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_decref_managed_elements(managed_ptr_in_rcx) -> void
  // Decrefs every struct element pointer stored in a __ManagedMemory buffer.
  // Called when an array holding reference-counted struct elements is freed, so that
  // each element's refcount is decremented before the buffer itself is released.
  // Args: rcx = heap pointer to __ManagedMemory struct
  // __ManagedMemory layout: [+0 buffer(ptr), +8 length(i64), +16 capacity(i64), +24 element_size(i64)]
  // The buffer contains 'length' consecutive 8-byte heap pointers (one per element).
  // -------------------------------------------------------------------------
  private void EmitMmDecrefManagedElements() {
    // Stack: [rbp-8]=managed_ptr, [rbp-16]=buffer, [rbp-24]=length, [rbp-32]=index (loop counter)
    EmitRuntimeFunctionStart("mm_decref_managed_elements", 1, 0x50);

    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = managed_ptr

    // Load buffer pointer from managed_ptr[0]
    EmitBytes(0x48, 0x8B, 0x01); // MOV rax, [rcx] — managed_ptr->buffer
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = buffer

    // Load length from managed_ptr[8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // rax = managed_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8] — managed_ptr->length
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = length

    // If length == 0, nothing to decref
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_decref_managed_elements_done");

    // index = 0
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // [rbp-32] = 0

    DefineLabel("mm_decref_managed_elements_loop");
    // if index >= length, done
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // rax = index
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // rcx = length
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ae", "mm_decref_managed_elements_done"); // unsigned >=

    // Load element ptr: buffer[index * 8]
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // rax = index
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // rcx = buffer
    // element_ptr = buffer + index * 8
    EmitBytes(0x48, 0x8B, 0x0C, 0xC1); // MOV rcx, [rcx + rax*8]
    // Null guard: buffer slots can be null (zeroed after remove operations)
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_decref_managed_elements_skip");
    // mm_decref(element_ptr)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);
    DefineLabel("mm_decref_managed_elements_skip");

    // index++
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);
    EmitJmp("mm_decref_managed_elements_loop");

    DefineLabel("mm_decref_managed_elements_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_clear_managed_elements(managed_ptr_in_rcx) -> void
  // Decrefs every struct element pointer and zeroes each slot in the buffer.
  // Called by Array.clear() to safely decref all elements while keeping the
  // buffer alive. Zeroing prevents stale pointers from being double-decref'd
  // when new elements are pushed into the reused slots.
  // Args: rcx = heap pointer to __ManagedMemory struct
  // -------------------------------------------------------------------------
  private void EmitMmClearManagedElements() {
    // Stack: [rbp-8]=managed_ptr, [rbp-16]=buffer, [rbp-24]=length, [rbp-32]=index (loop counter)
    EmitRuntimeFunctionStart("mm_clear_managed_elements", 1, 0x50);

    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = managed_ptr

    // Load buffer pointer from managed_ptr[0]
    EmitBytes(0x48, 0x8B, 0x01); // MOV rax, [rcx] — managed_ptr->buffer
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = buffer

    // Load length from managed_ptr[8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // rax = managed_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8] — managed_ptr->length
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = length

    // If length == 0, nothing to do
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_clear_managed_elements_done");

    // index = 0
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // [rbp-32] = 0

    DefineLabel("mm_clear_managed_elements_loop");
    // if index >= length, done
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // rax = index
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // rcx = length
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ae", "mm_clear_managed_elements_done"); // unsigned >=

    // Load element ptr: buffer[index * 8]
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // rax = index
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // rcx = buffer
    // element_ptr = buffer + index * 8
    EmitBytes(0x48, 0x8B, 0x0C, 0xC1); // MOV rcx, [rcx + rax*8]
    // Null guard: buffer slots can be null (zeroed after remove operations)
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_clear_managed_elements_zero");
    // mm_decref(element_ptr)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);
    DefineLabel("mm_clear_managed_elements_zero");

    // Zero the slot: buffer[index * 8] = 0
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // rax = index
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // rcx = buffer
    // MOV qword [rcx + rax*8], 0
    EmitBytes(0x48, 0xC7, 0x04, 0xC1, 0x00, 0x00, 0x00, 0x00);

    // index++
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);
    EmitJmp("mm_clear_managed_elements_loop");

    DefineLabel("mm_clear_managed_elements_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_leak_check() -> void (never returns if leaks detected)
  // Checks global alloc counter; if nonzero, prints leak count and exits with code 101.
  // -------------------------------------------------------------------------
  private void EmitMmLeakCheck() {
    // Stack: [rbp-56..rbp-80]=24-byte string buffer
    EmitRuntimeFunctionStart("mm_leak_check", 0, 0x80);

    // Check global alloc counter
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_count");
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_leak_check_done"); // count == 0, no leaks

    // Print warning: "MM leak: N allocation(s) remain\n" to stderr
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // save count

    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_leak_prefix");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    // Convert count to string
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);  // RCX = count
    EmitLeaRegMem(X86Register.Rdx, -0x50);     // RDX = &buffer at [rbp-0x50]
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_u64_to_string")); EmitDword(0);

    EmitLeaRegMem(X86Register.Rcx, -0x50);     // RCX = &buffer
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_leak_suffix");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    // Leak is a fatal error — terminate with exit code 101
    EmitMovRegImm(X86Register.Rcx, 101);
    EmitCallImport("kernel32.dll", "ExitProcess");

    DefineLabel("mm_leak_check_done");
    EmitRuntimeFunctionEnd();
  }

  // Validate a pointer has a valid AllocEntry at [ptr-8]
  // Args: rcx = user_ptr, rdx = tag_cstr
  // If ptr is non-null and [ptr-8] == 0, prints diagnostic and panics
  private void EmitMmValidatePtr() {
    DefineSymdata("__mm_validate_tag", "MM VALIDATE ptr=\0"u8.ToArray());
    DefineSymdata("__mm_validate_fail", "VALIDATION FAILED: ptr has no AllocEntry!\n\0"u8.ToArray());

    EmitRuntimeFunctionStart("mm_validate_ptr", 2, 0x30);
    // [rbp-8] = user_ptr, [rbp-16] = tag_cstr

    // If ptr == NULL, skip (null is sometimes ok)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_validate_done");

    // Load [ptr-8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax]

    // If non-zero, entry exists — do magic check if MmDebug, then skip
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_validate_no_entry");
    if (Compiler.MmDebug) EmitMmDebugCheckEntryMagic("mm_validate");
    EmitJmp("mm_validate_done");

    DefineLabel("mm_validate_no_entry");
    // FAILED: print "MM VALIDATE ptr=0xHEX\n" then panic
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_validate_tag");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_hex")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_validate_fail");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);

    DefineLabel("mm_validate_done");
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Trace functions — only emitted when Compiler.MmTrace is true.
  // Called from compiler-generated code, not from runtime functions.
  // ==========================================================================

  /// <summary>
  /// Emit the scope annotation suffix: prints " [scope]" if [rbp-16] (scope_cstr) is non-null,
  /// then prints a newline. Used at the end of every mm_trace_* function.
  /// </summary>
  private void EmitMmTraceScopeAndNewline(string skipLabel) {
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // scope_cstr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", skipLabel);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_lbracket");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rbracket");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    DefineLabel(skipLabel);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
  }

  // -------------------------------------------------------------------------
  // mm_trace_alloc(user_ptr_in_rcx, scope_cstr_in_rdx) -> void
  // Prints: indent + "alloc " + entry_tag + " #N rc=0 [scope]\n"
  // -------------------------------------------------------------------------
  private void EmitMmTraceAlloc() {
    EmitRuntimeFunctionStart("mm_trace_alloc", 2, 0x30);
    // [rbp-8]=user_ptr, [rbp-16]=scope_cstr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_alloc");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    // Load entry from [user_ptr - 8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // user_ptr
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    // Print " #N"
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_hash");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, (byte)AllocEntryAllocIdOffset); // MOV rcx, [rax+alloc_id]
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    // Print " rc=N"
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rc_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, 0x40); // MOV rcx, [rax+64] — entry.refcount
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitMmTraceScopeAndNewline("mm_trace_alloc_no_scope");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_free(user_ptr_in_rcx, scope_cstr_in_rdx) -> void
  // Prints: indent + "free " + tag + " #N [scope]\n"
  // -------------------------------------------------------------------------
  private void EmitMmTraceFree() {
    EmitRuntimeFunctionStart("mm_trace_free", 2, 0x30);
    // [rbp-8]=user_ptr, [rbp-16]=scope_cstr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    // When scope is NULL (free triggered internally by mm_decref), add extra indent
    // to visually show it's a side-effect of the preceding decref
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // scope_cstr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_trace_free_no_extra_indent");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_indent");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    DefineLabel("mm_trace_free_no_extra_indent");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_free");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    // Load entry from [user_ptr - 8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    // Print " #N"
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_hash");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, (byte)AllocEntryAllocIdOffset); // MOV rcx, [rax+alloc_id]
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitMmTraceScopeAndNewline("mm_trace_free_no_scope");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_incref(user_ptr_in_rcx, scope_cstr_in_rdx) -> void
  // Prints: indent + "incref " + tag + " #N rc=N [scope]\n"
  // Called AFTER mm_incref so refcount is already incremented.
  // -------------------------------------------------------------------------
  private void EmitMmTraceIncref() {
    EmitRuntimeFunctionStart("mm_trace_incref", 2, 0x30);
    // [rbp-8]=user_ptr, [rbp-16]=scope_cstr
    // Skip trace if user_ptr is null (mm_incref is a no-op on null)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_trace_incref_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_incref");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    // Print " #N"
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_hash");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, (byte)AllocEntryAllocIdOffset); // MOV rcx, [rax+alloc_id]
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    // Print " rc=N"
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rc_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, 0x40); // MOV rcx, [rax+64] — entry.refcount
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitMmTraceScopeAndNewline("mm_trace_incref_no_scope");
    DefineLabel("mm_trace_incref_null");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_decref(user_ptr_in_rcx, scope_cstr_in_rdx) -> void
  // Prints: indent + "decref " + tag + " #N rc=N [scope]\n"
  // Called BEFORE mm_decref so the entry is still valid (mm_decref may free at rc=0).
  // Prints rc-1 (the value after the upcoming decrement) so the output is consistent
  // with mm_trace_incref which prints the value after increment.
  // -------------------------------------------------------------------------
  private void EmitMmTraceDecref() {
    EmitRuntimeFunctionStart("mm_trace_decref", 2, 0x30);
    // [rbp-8]=user_ptr, [rbp-16]=scope_cstr
    // Skip trace if user_ptr is null (mm_decref is a no-op on null)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_trace_decref_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_decref");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    // Print " #N"
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_hash");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, (byte)AllocEntryAllocIdOffset); // MOV rcx, [rax+alloc_id]
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    // Print " rc=N" (rc-1: value after the upcoming decrement)
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rc_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, 0x40); // MOV rcx, [rax+64] — entry.refcount
    EmitSubRegImm(X86Register.Rcx, 1);  // print rc-1 (value after the upcoming decrement)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitMmTraceScopeAndNewline("mm_trace_decref_no_scope");
    DefineLabel("mm_trace_decref_null");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_transfer(user_ptr_in_rcx, scope_cstr_in_rdx) -> void
  // Prints: indent + "transfer " + tag + " #N rc=N [scope]\n"
  // Called when ownership is transferred to a caller (scope-end keepVars).
  // The refcount is unchanged; prints the current value.
  // -------------------------------------------------------------------------
  private void EmitMmTraceTransfer() {
    EmitRuntimeFunctionStart("mm_trace_transfer", 2, 0x30);
    // [rbp-8]=user_ptr, [rbp-16]=scope_cstr
    // Skip trace if user_ptr is null
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_trace_transfer_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_transfer");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    // Print " #N"
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_hash");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, (byte)AllocEntryAllocIdOffset); // MOV rcx, [rax+alloc_id]
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    // Print " rc=N"
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rc_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, 0x40); // MOV rcx, [rax+64] — entry.refcount
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitMmTraceScopeAndNewline("mm_trace_transfer_no_scope");
    DefineLabel("mm_trace_transfer_null");
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Chain/ChainNode runtime helpers — doubly-linked intrusive list for Chain<T>
  // ==========================================================================
  //
  // ChainNode layout (at user pointer, before the T payload):
  //   +0   next   (ptr to next ChainNode)
  //   +8   prev   (ptr to prev ChainNode)
  //   +16  chain  (ptr to owning Chain, or 0 if detached)
  //
  // Chain layout:
  //   +0   head   (ptr to first ChainNode)
  //   +8   tail   (ptr to last ChainNode)
  //   +16  count  (u64)

  // -------------------------------------------------------------------------
  // maxon_chain_insert_first(chain_ptr_rcx, node_ptr_rdx) -> void
  // Insert node at head of chain. Auto-detaches from old chain if needed.
  // Stack: [rbp-8]=chain_ptr, [rbp-16]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitChainInsertFirst() {
    EmitRuntimeFunctionStart("maxon_chain_insert_first", 2, 0x40);

    // Auto-detach: if node.chain != 0, unlink from old chain first
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] — node.chain
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "chain_insert_first_no_detach");
    // call maxon_chain_unlink(node.chain, node_ptr)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // rcx = old chain
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // rdx = node_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_chain_unlink")); EmitDword(0);
    DefineLabel("chain_insert_first_no_detach");

    // old_head = [chain+0]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x00); // MOV rcx, [rax+0] — old_head
    // rcx = old_head

    // node.next = old_head
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = node_ptr
    EmitBytes(0x48, 0x89, 0x0A); // MOV [rdx], rcx — node.next = old_head

    // node.prev = 0
    EmitMovRegImm(X86Register.Rax, 0);
    EmitBytes(0x48, 0x89, 0x42, 0x08); // MOV [rdx+8], rax — node.prev = 0

    // node.chain = chain_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x89, 0x42, 0x10); // MOV [rdx+16], rax — node.chain = chain_ptr

    // if old_head != 0: old_head.prev = node_ptr
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "chain_insert_first_no_old_head");
    EmitBytes(0x48, 0x89, 0x51, 0x08); // MOV [rcx+8], rdx — old_head.prev = node_ptr
    DefineLabel("chain_insert_first_no_old_head");

    // chain.head = node_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = node_ptr
    EmitBytes(0x48, 0x89, 0x10); // MOV [rax], rdx — chain.head = node_ptr

    // if chain.tail == 0: chain.tail = node_ptr
    EmitBytes(0x48, 0x83, 0x78, 0x08, 0x00); // CMP qword [rax+8], 0
    EmitJcc("ne", "chain_insert_first_tail_ok");
    EmitBytes(0x48, 0x89, 0x50, 0x08); // MOV [rax+8], rdx — chain.tail = node_ptr
    DefineLabel("chain_insert_first_tail_ok");

    // chain.count++
    EmitBytes(0x48, 0xFF, 0x40, 0x10); // INC qword [rax+16]

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_chain_insert_last(chain_ptr_rcx, node_ptr_rdx) -> void
  // Insert node at tail of chain. Auto-detaches from old chain if needed.
  // Stack: [rbp-8]=chain_ptr, [rbp-16]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitChainInsertLast() {
    EmitRuntimeFunctionStart("maxon_chain_insert_last", 2, 0x40);

    // Auto-detach: if node.chain != 0, unlink from old chain first
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] — node.chain
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "chain_insert_last_no_detach");
    // call maxon_chain_unlink(node.chain, node_ptr)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // rcx = old chain
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // rdx = node_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_chain_unlink")); EmitDword(0);
    DefineLabel("chain_insert_last_no_detach");

    // old_tail = [chain+8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x08); // MOV rcx, [rax+8] — old_tail
    // rcx = old_tail

    // node.next = 0
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = node_ptr
    EmitMovRegImm(X86Register.Rax, 0);
    EmitBytes(0x48, 0x89, 0x02); // MOV [rdx], rax — node.next = 0

    // node.prev = old_tail
    EmitBytes(0x48, 0x89, 0x4A, 0x08); // MOV [rdx+8], rcx — node.prev = old_tail

    // node.chain = chain_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x89, 0x42, 0x10); // MOV [rdx+16], rax — node.chain = chain_ptr

    // if old_tail != 0: old_tail.next = node_ptr
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "chain_insert_last_no_old_tail");
    EmitBytes(0x48, 0x89, 0x11); // MOV [rcx], rdx — old_tail.next = node_ptr
    DefineLabel("chain_insert_last_no_old_tail");

    // chain.tail = node_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = node_ptr
    EmitBytes(0x48, 0x89, 0x50, 0x08); // MOV [rax+8], rdx — chain.tail = node_ptr

    // if chain.head == 0: chain.head = node_ptr
    EmitBytes(0x48, 0x83, 0x38, 0x00); // CMP qword [rax], 0
    EmitJcc("ne", "chain_insert_last_head_ok");
    EmitBytes(0x48, 0x89, 0x10); // MOV [rax], rdx — chain.head = node_ptr
    DefineLabel("chain_insert_last_head_ok");

    // chain.count++
    EmitBytes(0x48, 0xFF, 0x40, 0x10); // INC qword [rax+16]

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_chain_insert_after(chain_ptr_rcx, target_ptr_rdx, node_ptr_r8) -> void
  // Insert node after target in chain. Auto-detaches from old chain if needed.
  // Stack: [rbp-8]=chain_ptr, [rbp-16]=target_ptr, [rbp-24]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitChainInsertAfter() {
    EmitRuntimeFunctionStart("maxon_chain_insert_after", 3, 0x40);

    // Auto-detach: if node.chain != 0, unlink from old chain first
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] — node.chain
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "chain_insert_after_no_detach");
    // call maxon_chain_unlink(node.chain, node_ptr)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // rcx = old chain
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // rdx = node_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_chain_unlink")); EmitDword(0);
    DefineLabel("chain_insert_after_no_detach");

    // after = [target+0] (target.next)
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = target_ptr
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — after = target.next
    // rax = after

    // node.next = after
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = node_ptr
    EmitBytes(0x48, 0x89, 0x01); // MOV [rcx], rax — node.next = after

    // node.prev = target_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = target_ptr
    EmitBytes(0x48, 0x89, 0x51, 0x08); // MOV [rcx+8], rdx — node.prev = target_ptr

    // node.chain = chain_ptr
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // RDX = chain_ptr
    EmitBytes(0x48, 0x89, 0x51, 0x10); // MOV [rcx+16], rdx — node.chain = chain_ptr

    // target.next = node_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = target_ptr
    EmitBytes(0x48, 0x89, 0x0A); // MOV [rdx], rcx — target.next = node_ptr

    // if after != 0: after.prev = node_ptr; else: chain.tail = node_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "chain_insert_after_was_tail");
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — after.prev = node_ptr
    EmitJmp("chain_insert_after_linked");
    DefineLabel("chain_insert_after_was_tail");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — chain.tail = node_ptr
    DefineLabel("chain_insert_after_linked");

    // chain.count++
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0xFF, 0x40, 0x10); // INC qword [rax+16]

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_chain_insert_before(chain_ptr_rcx, target_ptr_rdx, node_ptr_r8) -> void
  // Insert node before target in chain. Auto-detaches from old chain if needed.
  // Stack: [rbp-8]=chain_ptr, [rbp-16]=target_ptr, [rbp-24]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitChainInsertBefore() {
    EmitRuntimeFunctionStart("maxon_chain_insert_before", 3, 0x40);

    // Auto-detach: if node.chain != 0, unlink from old chain first
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] — node.chain
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "chain_insert_before_no_detach");
    // call maxon_chain_unlink(node.chain, node_ptr)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // rcx = old chain
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // rdx = node_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_chain_unlink")); EmitDword(0);
    DefineLabel("chain_insert_before_no_detach");

    // before = [target+8] (target.prev)
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = target_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8] — before = target.prev
    // rax = before

    // node.next = target_ptr
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = node_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = target_ptr
    EmitBytes(0x48, 0x89, 0x11); // MOV [rcx], rdx — node.next = target_ptr

    // node.prev = before
    EmitBytes(0x48, 0x89, 0x41, 0x08); // MOV [rcx+8], rax — node.prev = before

    // node.chain = chain_ptr
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // RDX = chain_ptr
    EmitBytes(0x48, 0x89, 0x51, 0x10); // MOV [rcx+16], rdx — node.chain = chain_ptr

    // target.prev = node_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = target_ptr
    EmitBytes(0x48, 0x89, 0x4A, 0x08); // MOV [rdx+8], rcx — target.prev = node_ptr

    // if before != 0: before.next = node_ptr; else: chain.head = node_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "chain_insert_before_was_head");
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — before.next = node_ptr
    EmitJmp("chain_insert_before_linked");
    DefineLabel("chain_insert_before_was_head");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — chain.head = node_ptr
    DefineLabel("chain_insert_before_linked");

    // chain.count++
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0xFF, 0x40, 0x10); // INC qword [rax+16]

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_chain_unlink(chain_ptr_rcx, node_ptr_rdx) -> void
  // Unlink node from chain. No-op if node.chain == 0. Does NOT free.
  // Stack: [rbp-8]=chain_ptr, [rbp-16]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitChainUnlink() {
    EmitRuntimeFunctionStart("maxon_chain_unlink", 2, 0x40);

    // If node.chain == 0, no-op
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] — node.chain
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "chain_unlink_done");

    // prev = [node+8], next = [node+0]
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x08); // MOV rcx, [rax+8] — prev
    EmitBytes(0x48, 0x8B, 0x10); // MOV rdx, [rax] — next
    // rcx = prev, rdx = next

    // Reconnect: if prev != 0: prev.next = next; else: chain.head = next
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "chain_unlink_no_prev");
    EmitBytes(0x48, 0x89, 0x11); // MOV [rcx], rdx — prev.next = next
    EmitJmp("chain_unlink_prev_done");
    DefineLabel("chain_unlink_no_prev");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x89, 0x10); // MOV [rax], rdx — chain.head = next
    DefineLabel("chain_unlink_prev_done");

    // if next != 0: next.prev = prev; else: chain.tail = prev
    EmitBytes(0x48, 0x85, 0xD2); // TEST rdx, rdx
    EmitJcc("z", "chain_unlink_no_next");
    EmitBytes(0x48, 0x89, 0x4A, 0x08); // MOV [rdx+8], rcx — next.prev = prev
    EmitJmp("chain_unlink_next_done");
    DefineLabel("chain_unlink_no_next");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — chain.tail = prev
    DefineLabel("chain_unlink_next_done");

    // Clear node's links: node.next = 0, node.prev = 0, node.chain = 0
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — node.next = 0
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — node.prev = 0
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — node.chain = 0

    // chain.count--
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0xFF, 0x48, 0x10); // DEC qword [rax+16]

    DefineLabel("chain_unlink_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_chain_clear(chain_ptr_rcx) -> void
  // Primitive variant: free each node without decref'ing node values.
  // -------------------------------------------------------------------------
  private void EmitChainClear() => EmitChainClearImpl("maxon_chain_clear", managed: false);

  // -------------------------------------------------------------------------
  // maxon_chain_clear_managed(chain_ptr_rcx) -> void
  // Managed variant: decref each node's heap value before freeing the node.
  // -------------------------------------------------------------------------
  private void EmitChainClearManaged() => EmitChainClearImpl("maxon_chain_clear_managed", managed: true);

  // -------------------------------------------------------------------------
  // Shared implementation for chain clear (primitive vs managed).
  // Walks chain head->next, optionally decrefs node values, frees each node,
  // then zeros the chain metadata.
  // Stack: [rbp-8]=chain_ptr, [rbp-40]=current, [rbp-48]=next
  // -------------------------------------------------------------------------
  private void EmitChainClearImpl(string funcName, bool managed) {
    EmitRuntimeFunctionStart(funcName, 1, 0x60);

    // current = [chain+0] — chain.head
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — chain.head
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = current

    DefineLabel($"{funcName}_loop");
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", $"{funcName}_loop_done");

    // Save next before modifying: next = [current+0]
    EmitBytes(0x48, 0x8B, 0x48, 0x00); // MOV rcx, [rax+0] — current.next
    EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-48] = next

    // Zero node's chain links
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — node.next = 0
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — node.prev = 0
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — node.chain = 0

    if (managed) {
      // Decref the heap value stored in the node (node+24)
      // Trace before decref: mm_decref may free the object, invalidating the pointer
      if (Compiler.MmTrace) {
        EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current (user ptr)
        EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — node.value
        EmitMovRegImm(X86Register.Rdx, 0); // rdx = NULL (no source location)
        EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_decref")); EmitDword(0);
      }
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current (user ptr)
      EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — node.value
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);
    }

    // Free the node via mm_free(user_ptr)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = current (user ptr)
    if (Compiler.MmTrace) EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // scope = NULL
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free")); EmitDword(0);

    // current = next
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitJmp($"{funcName}_loop");

    DefineLabel($"{funcName}_loop_done");

    // Zero chain metadata: head = 0, tail = 0, count = 0
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — chain.head = 0
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — chain.tail = 0
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — chain.count = 0

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_chain_decref_values(chain_ptr_rcx) -> void
  // Walks chain nodes and decrefs each node's heap value without freeing the
  // nodes. Used during chain destruction: mm_free will free the child-owned
  // nodes, but their values need explicit decref first.
  // Stack: [rbp-8]=chain_ptr, [rbp-40]=current
  // -------------------------------------------------------------------------
  private void EmitChainDecrefValues() {
    EmitRuntimeFunctionStart("maxon_chain_decref_values", 1, 0x60);

    // current = [chain+0] — chain.head
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = chain_ptr
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — chain.head
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = current

    DefineLabel("maxon_chain_decref_values_loop");
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "maxon_chain_decref_values_done");

    // Save next before any calls: next = [current+0]
    EmitBytes(0x48, 0x8B, 0x48, 0x00); // MOV rcx, [rax+0] — current.next
    EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-48] = next

    // Decref the heap value stored in the node (node+24)
    if (Compiler.MmTrace) {
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current (user ptr)
      EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — node.value
      EmitMovRegImm(X86Register.Rdx, 0); // rdx = NULL (no source location)
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_decref")); EmitDword(0);
    }
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current (user ptr)
    EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — node.value
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);

    // current = next
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitJmp("maxon_chain_decref_values_loop");

    DefineLabel("maxon_chain_decref_values_done");

    EmitRuntimeFunctionEnd();
  }

}
