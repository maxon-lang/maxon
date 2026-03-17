using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class X86CodeEmitter {

  // ==========================================================================
  // Memory Manager -- pure reference counting with inline header
  // ==========================================================================
  //
  // Allocation Header Layout (24 bytes inline, before user pointer):
  //   [ptr - 24]: alloc_id            (8 bytes, sequential allocation ID for trace correlation)
  //   [ptr - 16]: destructor_fn_ptr   (8 bytes, 0 = no destructor needed)
  //   [ptr -  8]: refcount            (8 bytes, starts at 0)
  //   [ptr     ]: user data           (size bytes)
  //
  // Debug mode (--mm-debug) adds an 8-byte canary after user data at [ptr + size].
  //
  // Key invariant: mm_alloc returns a pointer with rc=0. Every assignment (store
  // to a variable) calls mm_incref, and every scope-end/overwrite calls mm_decref.
  // This ensures uniform refcount semantics without conditional first-incref logic.
  //
  // Inline header layout: [ptr-24]=packed_id, [ptr-16]=destructor, [ptr-8]=refcount
  private const int MmHeaderSize = 24;

  // Debug mode constants
  private const long MmDebugCanaryValue = unchecked((long)0xCAFEBABEDEADC0DE);

  public void EmitMemoryManagerFunctions(List<string?>? tagTable = null) {
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
      // Trace tag strings -- only emitted when --mm-trace is passed
      DefineSymdata("__mm_tag_alloc", "alloc \0"u8.ToArray());
      DefineSymdata("__mm_tag_free", "free \0"u8.ToArray());
      DefineSymdata("__mm_tag_incref", "incref \0"u8.ToArray());
      DefineSymdata("__mm_tag_decref", "decref \0"u8.ToArray());
      DefineSymdata("__mm_tag_transfer", "transfer \0"u8.ToArray());
      DefineSymdata("__mm_tag_realloc", "realloc \0"u8.ToArray());
      DefineSymdata("__mm_tag_cow", "cow \0"u8.ToArray());
      DefineSymdata("__mm_tag_size_eq", " size=\0"u8.ToArray());
      DefineSymdata("__mm_tag_rc_eq", " rc=\0"u8.ToArray());
      DefineSymdata("__mm_tag_hash", " #\0"u8.ToArray());
      DefineSymdata("__mm_tag_lbracket", " [\0"u8.ToArray());
      DefineSymdata("__mm_tag_rbracket", "]\0"u8.ToArray());
      DefineSymdata("__mm_trace_depth", new byte[8]);
      DefineSymdata("__mm_tag_indent", "  \0"u8.ToArray());
      // Runtime allocation tags -- only needed for trace output
      DefineSymdata("__rt_tag_buffer", "Buffer\0"u8.ToArray());
      DefineSymdata("__rt_tag_cstring", "CString\0"u8.ToArray());
      DefineSymdata("__rt_tag_cmdline_arg", "CmdLineArg\0"u8.ToArray());
      DefineSymdata("__rt_tag_find_data", "FindData\0"u8.ToArray());
      DefineSymdata("__rt_tag_dir_buffer", "DirBuffer\0"u8.ToArray());
      DefineSymdata("__rt_tag_capture_result", "CaptureResult\0"u8.ToArray());
      DefineSymdata("__rt_tag_pipe_buffer", "PipeBuffer\0"u8.ToArray());
      // Scope strings for runtime-internal mm_decref calls
      DefineSymdata("__mm_scope_managed_elements", "~ManagedElements\0"u8.ToArray());
      DefineSymdata("__mm_scope_managed_list_detach", "managed_list_detach\0"u8.ToArray());
      DefineSymdata("__mm_scope_managed_list_clear", "managed_list_clear\0"u8.ToArray());
      DefineSymdata("__mm_scope_managed_list_decref_values", "managed_list_decref_values\0"u8.ToArray());
      DefineSymdata("__mm_scope_find_close", "find_close\0"u8.ToArray());
      // Scope strings for runtime-internal mm_alloc/mm_incref calls
      DefineSymdata("__mm_scope_cow_copy", "cow_copy\0"u8.ToArray());
      DefineSymdata("__mm_scope_cmdline_arg", "cmdline_arg\0"u8.ToArray());
      DefineSymdata("__mm_scope_find_first_file", "find_first_file\0"u8.ToArray());
      DefineSymdata("__mm_scope_get_cwd", "get_cwd\0"u8.ToArray());
      DefineSymdata("__mm_scope_capture", "capture\0"u8.ToArray());
      DefineSymdata("__mm_scope_pipe_read", "pipe_read\0"u8.ToArray());
      DefineSymdata("__mm_scope_realloc", "realloc\0"u8.ToArray());
      DefineSymdata("__mm_scope_managed_list_insert", "managed_list_insert\0"u8.ToArray());
    }

    // Panic messages for invalid memory manager calls
    DefineSymdata("__mm_panic_decref_null",
      "mm_decref called with NULL pointer\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_incref_null",
      "mm_incref called with NULL pointer\n\0"u8.ToArray());
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
    DefineSymdata("__mm_panic_alloc_zero_size",
      "__ManagedMemory: alloc size must be > 0\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_realloc_zero_size",
      "__ManagedMemory: realloc size must be > 0\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_element_size_zero",
      "__ManagedMemory: element_size must be > 0\n\0"u8.ToArray());

    if (Compiler.MmDebug) {
      DefineSymdata("__mm_panic_canary",
        "mm_debug: heap canary overwritten (buffer overrun detected)\n\0"u8.ToArray());
      DefineSymdata("__mm_panic_heap_null",
        "mm_debug: HeapAlloc returned NULL (out of memory)\n\0"u8.ToArray());
      DefineSymdata("__mm_panic_realloc_null",
        "mm_debug: HeapReAlloc returned NULL (out of memory)\n\0"u8.ToArray());
      DefineSymdata("__mm_panic_canary_tag", "mm_debug canary fail ptr=\0"u8.ToArray());
      DefineSymdata("__mm_debug_tag_size", " size=\0"u8.ToArray());
    }

    EmitMmTracePrintTag();
    EmitMmTracePrintHex();
    EmitMmTracePrintI64();
    // Always emit tag lookup so mm_leak_check can print type names
    EmitMmTagLookup(tagTable ?? []);
    if (Compiler.MmTrace) {
      EmitMmTracePrintPackedTag();
    }
    EmitMmAlloc();
    EmitMmRealloc();
    EmitMmFree();
    EmitMmIncref();
    EmitMmDecref();

    EmitMmDecrefManagedElements();
    EmitMmIncrefManagedElements();
    EmitMmClearManagedElements();
    EmitMmLeakCheck();
    EmitMmValidatePtr();
    EmitManagedListInsertFirst();
    EmitManagedListInsertLast();
    EmitManagedListInsertAfter();
    EmitManagedListInsertBefore();
    EmitManagedListUnlink();
    EmitManagedListClear();
    EmitManagedListClearManaged();
    EmitManagedListDecrefValues();
    if (Compiler.MmTrace) {
      EmitMmTracePrintIndent();
      EmitMmTraceTransfer();
    }
  }

  // -------------------------------------------------------------------------
  // mm_trace_print_tag(tag_symdata_label) -- print a symdata tag string to stderr
  // Args: rcx = pointer to null-terminated string
  // -------------------------------------------------------------------------
  private void EmitMmTracePrintTag() {
    EmitRuntimeFunctionStart("mm_trace_print_tag", 1, 0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_print_hex(value) -- print a 64-bit value as "0xHEX" to stderr
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
  // mm_trace_print_i64(value) -- print a 64-bit integer in decimal to stderr
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
    EmitBytes(0xC6, 0x07, 0x00); // MOV byte [rdi], 0 -- null terminator

    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = value
    EmitMovRegImm(X86Register.Rcx, 10); // RCX = divisor

    DefineLabel("mm_trace_i64_loop");
    // RDX:RAX / RCX -> quotient in RAX, remainder in RDX
    EmitBytes(0x48, 0x31, 0xD2); // XOR rdx, rdx
    EmitBytes(0x48, 0xF7, 0xF1); // DIV rcx -- RAX = quotient, RDX = remainder
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
  // Debug helpers -- inline check sequences emitted when Compiler.MmDebug is true.
  // These must be called within an Emit* function body (they emit code inline,
  // not as separate functions).
  // -------------------------------------------------------------------------

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
  // mm_tag_lookup(tag_index_rcx) -> cstr_rax
  // Takes a tag index (lower 16 bits of packed [ptr-24] value), returns a
  // pointer to the type name C string. Index 0 returns "(null)".
  // Generated as a compare-and-branch chain from the tag table.
  // -------------------------------------------------------------------------
  private void EmitMmTagLookup(List<string?> tagTable) {
    EmitRuntimeFunctionStart("mm_tag_lookup", 1, 0x10);
    // [rbp-8] = tag_index
    for (int i = 1; i < tagTable.Count; i++) {
      var label = tagTable[i];
      if (label == null) continue;
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = tag_index
      EmitBytes(0x48, 0x83, 0xF8, (byte)i); // CMP rax, i (imm8 — works for i < 128)
      if (i >= 128) {
        // For larger indices, use CMP rax, imm32
        // Overwrite the last 2 bytes with proper encoding
        _code.RemoveRange(_code.Count - 2, 2);
        EmitBytes(0x48, 0x3D); EmitDword(i); // CMP rax, imm32
      }
      EmitJcc("nz", $"mm_tag_lookup_skip_{i}");
      EmitLeaRegSymdataRel(X86Register.Rax, label);
      EmitRuntimeFunctionEnd();
      DefineLabel($"mm_tag_lookup_skip_{i}");
    }
    // Default: return "(null)"
    EmitLeaRegSymdataRel(X86Register.Rax, "__mm_tag_null");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_print_packed_tag(user_ptr_rcx) -- extract tag_index from [ptr-24],
  // look up tag string, and print it followed by a space.
  // -------------------------------------------------------------------------
  private void EmitMmTracePrintPackedTag() {
    EmitRuntimeFunctionStart("mm_trace_print_packed_tag", 1, 0x30);
    // [rbp-8] = user_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x8B, 0x48, 0xE8); // MOV rcx, [rax-24] -- packed value
    EmitBytes(0x48, 0x81, 0xE1, 0xFF, 0xFF, 0x00, 0x00); // AND rcx, 0xFFFF -- tag_index
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_tag_lookup")); EmitDword(0);
    // RAX = tag cstr pointer
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
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
  // mm_alloc(size_in_rcx, destructor_fn_ptr_in_rdx, tag_index_in_r8) -> user_ptr_in_rax
  //
  // Allocates a tracked heap object with inline header.
  // Single HeapAlloc of size+24 (+8 canary if MmDebug).
  // Stores (alloc_id << 16 | tag_index) at [raw], destructor at [raw+8], rc=0 at [raw+16].
  // Returns raw+24. Increments __mm_alloc_count and __mm_alloc_id_counter.
  // -------------------------------------------------------------------------
  private void EmitMmAlloc() {
    // Stack layout:
    //   [rbp-8]  = arg1 (rcx) = size
    //   [rbp-16] = arg2 (rdx) = destructor_fn_ptr
    //   [rbp-24] = arg3 (r8)  = tag_index (small integer, 0 = no tag)
    //   [rbp-32] = arg4 (r9)  = scope_cstr (MmTrace only)
    //   [rbp-40] = raw_ptr
    //   [rbp-48] = alloc_size
    EmitRuntimeFunctionStart("mm_alloc", Compiler.MmTrace ? 4 : 3, 0x60);

    // Panic if size == 0
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = size
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_alloc_size_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_alloc_zero_size");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_alloc_size_ok");

    // Compute alloc_size = size + 24 (+ 8 canary for MmDebug)
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = size
    EmitAddRegImm(X86Register.Rax, Compiler.MmDebug ? MmHeaderSize + 8 : MmHeaderSize);
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = alloc_size

    // HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, alloc_size)
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x30);
    if (Compiler.MmDebug) EmitMmDebugCheckHeapAllocNull("mm_alloc", "__mm_panic_heap_null");
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = raw_ptr

    // Atomically increment global alloc counter
    EmitLeaRegSymdataRel(X86Register.R11, "__mm_alloc_count");
    EmitBytes(0xF0, 0x49, 0xFF, 0x03); // LOCK INC qword [R11]

    // Atomically get-and-increment alloc_id counter via LOCK XADD
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitLeaRegSymdataRel(X86Register.R11, "__mm_alloc_id_counter");
    // LOCK XADD [R11], RCX — atomically: old=[R11]; [R11]+=RCX; RCX=old
    EmitBytes(0xF0, 0x49, 0x0F, 0xC1, 0x0B);
    // RCX now holds the OLD alloc_id; the counter has been incremented
    EmitAddRegImm(X86Register.Rcx, 1); // RCX = new alloc_id (old + 1, matching original behavior)

    // Pack (alloc_id << 16 | tag_index) into [raw + 0]
    // RCX = alloc_id (just incremented)
    EmitBytes(0x48, 0xC1, 0xE1, 0x10); // SHL rcx, 16
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // RDX = tag_index
    EmitBytes(0x48, 0x09, 0xD1); // OR rcx, rdx -- rcx = (alloc_id << 16) | tag_index
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = raw_ptr
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx -- [raw+0] = packed

    // Store destructor_fn_ptr at [raw + 8]
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = destructor
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = raw_ptr
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx -- [raw+8] = destructor

    // Store refcount = 0 at [raw + 16]
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = raw_ptr
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx -- [raw+16] = rc=0

    // Write canary at [user_ptr + size] (MmDebug only)
    if (Compiler.MmDebug) {
      // user_ptr = raw + 24, canary at user_ptr + size = raw + 24 + size
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = raw
      EmitAddRegImm(X86Register.Rax, MmHeaderSize); // RAX = user_ptr
      EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = size
      EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx -- rax = user_ptr + size
      EmitMovRegImm(X86Register.Rcx, MmDebugCanaryValue);
      EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx -- write canary
    }

    // Return user_ptr = raw + 24
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = raw
    EmitAddRegImm(X86Register.Rax, MmHeaderSize);

    // Trace alloc after user_ptr is computed (in RAX)
    if (Compiler.MmTrace) {
      EmitMovMemReg(-0x28, X86Register.Rax, 8); // save user_ptr to [rbp-40]
      EmitInlineTrace("__mm_tag_alloc", "mm_alloc_trace", -0x28, -0x20, sizeSlot: -0x08);
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // restore user_ptr into RAX for return
    }

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_realloc(user_ptr_in_rcx, new_size_in_rdx, tag_cstr_in_r8) -> new_user_ptr_in_rax
  //
  // Reallocates a managed allocation with inline header. The header (24 bytes)
  // is preserved across the realloc. If user_ptr is NULL, delegates to mm_alloc.
  // The refcount, tag, and destructor are preserved by HeapReAlloc.
  // -------------------------------------------------------------------------
  private void EmitMmRealloc() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=new_size, [rbp-24]=tag,
    //        [rbp-40]=old_raw, [rbp-48]=realloc_size, [rbp-56]=new_raw
    EmitRuntimeFunctionStart("mm_realloc", 3, 0x60);

    // Panic if new_size == 0
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = new_size
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_realloc_size_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_realloc_zero_size");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_realloc_size_ok");

    // If ptr == NULL, delegate to mm_alloc(new_size, destructor=0, tag)
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_realloc_not_null");

    // NULL path: mm_alloc(size=new_size, destructor=0, tag=tag, scope=realloc)
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = new_size
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // RDX = destructor = 0
    EmitMovRegMem(X86Register.R8, -0x18, 8); // R8 = tag
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.R9, "__mm_scope_realloc");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc")); EmitDword(0);
    EmitJmp("mm_realloc_done");

    DefineLabel("mm_realloc_not_null");

    // Compute old_raw = user_ptr - 24
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitSubRegImm(X86Register.Rax, MmHeaderSize);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = old_raw

    // Compute realloc_size = new_size + 24 (+ 8 canary for MmDebug)
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = new_size
    EmitAddRegImm(X86Register.Rax, Compiler.MmDebug ? MmHeaderSize + 8 : MmHeaderSize);
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = realloc_size

    // HeapReAlloc(heap, HEAP_ZERO_MEMORY, old_raw, realloc_size)
    EmitHeapCall("HeapReAlloc", 0x08, rbpSlotR8: -0x28, rbpSlotR9: -0x30);
    if (Compiler.MmDebug) EmitMmDebugCheckHeapAllocNull("mm_realloc_heap", "__mm_panic_realloc_null");
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // [rbp-56] = new_raw

    // Write canary at [new_user_ptr + new_size] (MmDebug only)
    if (Compiler.MmDebug) {
      EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = new_raw
      EmitAddRegImm(X86Register.Rax, MmHeaderSize); // RAX = new_user_ptr
      EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = new_size
      EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx -- rax = new_user_ptr + new_size
      EmitMovRegImm(X86Register.Rcx, MmDebugCanaryValue);
      EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx -- write canary
    }

    // Return new_user_ptr = new_raw + 24
    EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = new_raw
    EmitAddRegImm(X86Register.Rax, MmHeaderSize);

    if (Compiler.MmTrace) {
      EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = new_user_ptr (reuse old_raw slot)
      EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
      EmitMovMemReg(-0x20, X86Register.Rcx, 8); // [rbp-32] = 0 (no scope)
      EmitInlineTrace("__mm_tag_realloc", "mm_realloc_trace", -0x28, -0x20, sizeSlot: -0x10);
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // restore RAX = new_user_ptr
    }

    DefineLabel("mm_realloc_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_free(user_ptr_in_rcx, [scope_cstr_in_rdx]) -> void
  //
  // Low-level free: decrements __mm_alloc_count and calls HeapFree on
  // (user_ptr - 24). Does NOT call destructor or check refcount -- the
  // caller (mm_decref or inline destruct) is responsible for that.
  // -------------------------------------------------------------------------
  private void EmitMmFree() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=scope_cstr (MmTrace only), [rbp-40]=raw_ptr
    EmitRuntimeFunctionStart("mm_free", Compiler.MmTrace ? 2 : 1, 0x60);

    // Trace free if enabled
    if (Compiler.MmTrace) {
      EmitInlineTraceFree("mm_free_trace", -0x08, -0x10);
    }

    // Atomically decrement global alloc counter
    EmitLeaRegSymdataRel(X86Register.R11, "__mm_alloc_count");
    EmitBytes(0xF0, 0x49, 0xFF, 0x0B); // LOCK DEC qword [R11]

    // HeapFree the raw allocation: user_ptr - 24
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitSubRegImm(X86Register.Rax, MmHeaderSize);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = raw_ptr
    EmitHeapCall("HeapFree", 0, rbpSlotR8: -0x28);

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_incref(user_ptr_in_rcx, [scope_cstr_in_rdx]) -> void
  // Increments refcount at [ptr-8]. Panics on NULL pointer.
  // -------------------------------------------------------------------------
  private void EmitMmIncref() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=scope_cstr (MmTrace only)
    EmitRuntimeFunctionStart("mm_incref", Compiler.MmTrace ? 2 : 1, 0x30);

    // NULL check -- panic
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_incref_not_null");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_incref_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_incref_not_null");

    // Increment refcount: [user_ptr - 8] += 1 (atomic for thread safety)
    EmitBytes(0xF0, 0x48, 0xFF, 0x40, 0xF8); // LOCK INC qword [rax-8]

    // Trace incref after increment
    if (Compiler.MmTrace) {
      EmitInlineTrace("__mm_tag_incref", "mm_incref_trace", -0x08, -0x10);
    }

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_decref(user_ptr_in_rcx, [scope_cstr_in_rdx]) -> void
  //
  // Decrements refcount at [ptr-8]. If rc reaches 0:
  //   1. Load destructor from [ptr-16], if non-zero call it with ptr in rcx
  //   2. Call mm_free(ptr) which does HeapFree(ptr-24) and decrements alloc_count
  // Panics on NULL or refcount underflow.
  // -------------------------------------------------------------------------
  private void EmitMmDecref() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=scope_cstr (MmTrace only)
    EmitRuntimeFunctionStart("mm_decref", Compiler.MmTrace ? 2 : 1, 0x30);

    // NULL check -- panic
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_decref_not_null");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_decref_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_decref_not_null");

    // Trace decref before modifying refcount (header still valid, prints rc-1)
    if (Compiler.MmTrace) {
      EmitInlineTrace("__mm_tag_decref", "mm_decref_trace", -0x08, -0x10, printRc: true, rcSubtract: 1);
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // reload RAX = user_ptr
    }

    // Check refcount: if [ptr-8] == 0, panic (underflow / double-free)
    EmitBytes(0x48, 0x83, 0x78, 0xF8, 0x00); // CMP qword [rax-8], 0
    EmitJcc("ne", "mm_decref_has_refs");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_decref_underflow");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_decref_has_refs");

    // Decrement refcount: [ptr-8] -= 1 (atomic for thread safety)
    // LOCK DEC sets ZF=1 when result reaches zero, used to branch to free path
    EmitBytes(0xF0, 0x48, 0xFF, 0x48, 0xF8); // LOCK DEC qword [rax-8]
    EmitJcc("ne", "mm_decref_done"); // ZF=0 means refcount > 0, skip free

    // refcount == 0: call destructor if non-null, then free
    // Load destructor from [ptr-16]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0xF0); // MOV rax, [rax-16] -- destructor_fn_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_decref_no_destructor");

    // Call destructor(user_ptr) -- destructor ptr is in RAX
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = user_ptr (arg for destructor)
    EmitBytes(0xFF, 0xD0); // CALL rax

    DefineLabel("mm_decref_no_destructor");

    // mm_free(user_ptr)
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = user_ptr
    if (Compiler.MmTrace) EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // scope = NULL
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free")); EmitDword(0);

    DefineLabel("mm_decref_done");
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
    EmitBytes(0x48, 0x8B, 0x01); // MOV rax, [rcx] -- managed_ptr->buffer
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = buffer

    // Load length from managed_ptr[8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // rax = managed_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8] -- managed_ptr->length
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
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_elements");
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

  // mm_incref_managed_elements(managed_ptr_in_rcx) -> void
  // Increfs every struct element pointer stored in a __ManagedMemory buffer.
  // Called after concat/memcpy to ensure copied element pointers have correct refcounts.
  // Args: rcx = heap pointer to __ManagedMemory struct
  // __ManagedMemory layout: [+0 buffer(ptr), +8 length(i64), +16 capacity(i64), +24 element_size(i64)]
  private void EmitMmIncrefManagedElements() {
    // Stack: [rbp-8]=managed_ptr, [rbp-16]=buffer, [rbp-24]=length, [rbp-32]=index (loop counter)
    EmitRuntimeFunctionStart("mm_incref_managed_elements", 1, 0x50);

    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = managed_ptr

    // Load buffer pointer from managed_ptr[0]
    EmitBytes(0x48, 0x8B, 0x01); // MOV rax, [rcx] -- managed_ptr->buffer
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = buffer

    // Load length from managed_ptr[8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // rax = managed_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8] -- managed_ptr->length
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = length

    // If length == 0, nothing to incref
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_incref_managed_elements_done");

    // index = 0
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // [rbp-32] = 0

    DefineLabel("mm_incref_managed_elements_loop");
    // if index >= length, done
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // rax = index
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // rcx = length
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ae", "mm_incref_managed_elements_done"); // unsigned >=

    // Load element ptr: buffer[index * 8]
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // rax = index
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // rcx = buffer
    // element_ptr = buffer + index * 8
    EmitBytes(0x48, 0x8B, 0x0C, 0xC1); // MOV rcx, [rcx + rax*8]
    // Null guard: buffer slots can be null
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_incref_managed_elements_skip");
    // mm_incref(element_ptr)
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_elements");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_incref")); EmitDword(0);
    DefineLabel("mm_incref_managed_elements_skip");

    // index++
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);
    EmitJmp("mm_incref_managed_elements_loop");

    DefineLabel("mm_incref_managed_elements_done");
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
    EmitBytes(0x48, 0x8B, 0x01); // MOV rax, [rcx] -- managed_ptr->buffer
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = buffer

    // Load length from managed_ptr[8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // rax = managed_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8] -- managed_ptr->length
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
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_elements");
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
    // Stack layout:
    //   [rbp-8]  = saved alloc_count
    //   [rbp-56..rbp-80] = 24-byte string buffer for i64-to-string
    EmitRuntimeFunctionStart("mm_leak_check", 0, 0x60);

    // Check global alloc counter
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_count");
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_leak_check_done"); // count == 0, no leaks

    // Print warning: "MM leak: N allocation(s) remain\n" to stderr
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // [rbp-8] = count

    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_leak_prefix");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    // Convert count to string
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);  // RCX = count
    EmitLeaRegMem(X86Register.Rdx, -0x50);     // RDX = &buffer at [rbp-0x50]
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_u64_to_string")); EmitDword(0);

    EmitLeaRegMem(X86Register.Rcx, -0x50);     // RCX = &buffer
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_leak_suffix");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    // Leak is a fatal error -- terminate with exit code 101
    EmitMovRegImm(X86Register.Rcx, 101);
    EmitCallImport("kernel32.dll", "ExitProcess");

    DefineLabel("mm_leak_check_done");
    EmitRuntimeFunctionEnd();
  }

  // Validate a pointer has a valid inline header at [ptr-8]
  // Args: rcx = user_ptr, rdx = tag_cstr
  // If ptr is non-null and [ptr-8] == 0, prints diagnostic and panics
  private void EmitMmValidatePtr() {
    DefineSymdata("__mm_validate_tag", "MM VALIDATE ptr=\0"u8.ToArray());
    DefineSymdata("__mm_validate_fail", "VALIDATION FAILED: ptr has zero refcount!\n\0"u8.ToArray());

    EmitRuntimeFunctionStart("mm_validate_ptr", 2, 0x30);
    // [rbp-8] = user_ptr, [rbp-16] = tag_cstr

    // If ptr == NULL, skip (null is sometimes ok)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_validate_done");

    // Load refcount at [ptr-8]
    EmitBytes(0x48, 0x8B, 0x48, 0xF8); // MOV rcx, [rax-8] -- refcount
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "mm_validate_done"); // nonzero refcount = valid

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
  // Trace helpers -- only used when Compiler.MmTrace is true.
  // Trace output for alloc/free/incref/decref is inlined directly into those
  // functions. Only mm_trace_transfer remains as a standalone runtime function
  // (called from compiler-generated code for ownership transfer annotations).
  // ==========================================================================

  /// <summary>
  /// Emit the scope annotation suffix: prints " [scope]" if [rbp-16] (scope_cstr) is non-null,
  /// then prints a newline. Used at the end of every mm_trace_* function.
  /// </summary>
  private void EmitMmTraceScopeAndNewline(string skipLabel, int scopeSlot = -0x10) {
    EmitMovRegMem(X86Register.Rax, scopeSlot, 8); // scope_cstr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", skipLabel);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_lbracket");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, scopeSlot, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rbracket");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    DefineLabel(skipLabel);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
  }

  /// Inline helper: emit code to print "Type #N" from user_ptr at [rbp + ptrSlot].
  /// Reads packed value from [ptr-24], extracts tag_index and alloc_id.
  private void EmitTraceTagAndId(int ptrSlot) {
    // Print tag name via mm_trace_print_packed_tag(user_ptr)
    EmitMovRegMem(X86Register.Rcx, ptrSlot, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_packed_tag")); EmitDword(0);
    // Print " #N" where N = alloc_id = [ptr-24] >> 16
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_hash");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, ptrSlot, 8); // user_ptr
    EmitBytes(0x48, 0x8B, 0x48, 0xE8); // MOV rcx, [rax-24] -- packed value
    EmitBytes(0x48, 0xC1, 0xE9, 0x10); // SHR rcx, 16 -- alloc_id
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
  }

  /// Inline helper: emit code to print " rc=N" from user_ptr at [rbp + ptrSlot].
  /// rcSubtract > 0 prints rc minus that value (e.g., 1 for decref trace to show post-decrement).
  private void EmitTraceRc(int ptrSlot, int rcSubtract = 0) {
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rc_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, ptrSlot, 8); // user_ptr
    EmitBytes(0x48, 0x8B, 0x48, 0xF8); // MOV rcx, [rax-8] -- refcount
    if (rcSubtract > 0) EmitSubRegImm(X86Register.Rcx, rcSubtract);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
  }

  /// Inline helper: emit code to print " size=N" from size value at [rbp + sizeSlot].
  private void EmitTraceSize(int sizeSlot) {
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_size_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, sizeSlot, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
  }

  /// <summary>
  /// Emit inline trace output: indent + tagLabel + "TypeName #N rc=R [scope]\n".
  /// ptrSlot/scopeSlot are rbp-relative offsets for the user_ptr and scope_cstr.
  /// If printRc is false, the " rc=N" part is omitted (used for free).
  /// rcSubtract adjusts the displayed refcount (1 for decref to show post-decrement).
  /// sizeSlot, if set, prints " size=N" after the refcount.
  /// </summary>
  private void EmitInlineTrace(string tagLabel, string uniquePrefix, int ptrSlot, int scopeSlot,
      bool printRc = true, int rcSubtract = 0, int? sizeSlot = null) {
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, tagLabel);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitTraceTagAndId(ptrSlot);
    if (printRc) EmitTraceRc(ptrSlot, rcSubtract);
    if (sizeSlot.HasValue) EmitTraceSize(sizeSlot.Value);
    EmitMmTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  /// <summary>
  /// Emit inline free trace: indent (+ extra indent if scope is NULL) + "free " + tag + " #N [scope]\n".
  /// </summary>
  private void EmitInlineTraceFree(string uniquePrefix, int ptrSlot, int scopeSlot) {
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    // When scope is NULL (free triggered internally by mm_decref), add extra indent
    EmitMovRegMem(X86Register.Rax, scopeSlot, 8); // scope_cstr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", $"{uniquePrefix}_no_extra_indent");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_indent");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    DefineLabel($"{uniquePrefix}_no_extra_indent");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_free");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitTraceTagAndId(ptrSlot);
    EmitMmTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  // -------------------------------------------------------------------------
  // mm_trace_transfer(user_ptr_in_rcx, scope_cstr_in_rdx) -> void
  // Prints: indent + "transfer " + tag + " #N rc=N [scope]\n"
  // -------------------------------------------------------------------------
  private void EmitMmTraceTransfer() {
    EmitRuntimeFunctionStart("mm_trace_transfer", 2, 0x30);
    // [rbp-8]=user_ptr, [rbp-16]=scope_cstr
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_trace_transfer_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_transfer");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitTraceTagAndId(-0x08);
    EmitTraceRc(-0x08);
    EmitMmTraceScopeAndNewline("mm_trace_transfer_no_scope");
    DefineLabel("mm_trace_transfer_null");
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // ManagedList/ManagedListNode runtime helpers -- doubly-linked intrusive list for ManagedList<T>
  // ==========================================================================
  //
  // ManagedListNode layout (at user pointer, before the T payload):
  //   +0   next   (ptr to next ManagedListNode)
  //   +8   prev   (ptr to prev ManagedListNode)
  //   +16  list   (ptr to owning ManagedList, or 0 if detached)
  //
  // ManagedList layout:
  //   +0   head   (ptr to first ManagedListNode)
  //   +8   tail   (ptr to last ManagedListNode)
  //   +16  count  (u64)

  // -------------------------------------------------------------------------
  // maxon_managed_list_insert_first(list_ptr_rcx, node_ptr_rdx) -> void
  // Insert node at head of managed list. Auto-detaches from old list if needed.
  // Stack: [rbp-8]=list_ptr, [rbp-16]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitManagedListInsertFirst() {
    EmitRuntimeFunctionStart("maxon_managed_list_insert_first", 2, 0x40);

    // Auto-detach: if node.list != 0, unlink from old list and release old list's reference
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] -- node.list
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "managed_list_insert_first_no_detach");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // rcx = old list
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // rdx = node_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_managed_list_unlink")); EmitDword(0);
    // Decref node — old list releases its counted reference
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = node_ptr
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_detach");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);
    DefineLabel("managed_list_insert_first_no_detach");

    // old_head = [list+0]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x00); // MOV rcx, [rax+0] -- old_head
    // rcx = old_head

    // node.next = old_head
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = node_ptr
    EmitBytes(0x48, 0x89, 0x0A); // MOV [rdx], rcx -- node.next = old_head

    // node.prev = 0
    EmitMovRegImm(X86Register.Rax, 0);
    EmitBytes(0x48, 0x89, 0x42, 0x08); // MOV [rdx+8], rax -- node.prev = 0

    // node.list = list_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x89, 0x42, 0x10); // MOV [rdx+16], rax -- node.list = list_ptr

    // if old_head != 0: old_head.prev = node_ptr
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "managed_list_insert_first_no_old_head");
    EmitBytes(0x48, 0x89, 0x51, 0x08); // MOV [rcx+8], rdx -- old_head.prev = node_ptr
    DefineLabel("managed_list_insert_first_no_old_head");

    // list.head = node_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = node_ptr
    EmitBytes(0x48, 0x89, 0x10); // MOV [rax], rdx -- list.head = node_ptr

    // if list.tail == 0: list.tail = node_ptr
    EmitBytes(0x48, 0x83, 0x78, 0x08, 0x00); // CMP qword [rax+8], 0
    EmitJcc("ne", "managed_list_insert_first_tail_ok");
    EmitBytes(0x48, 0x89, 0x50, 0x08); // MOV [rax+8], rdx -- list.tail = node_ptr
    DefineLabel("managed_list_insert_first_tail_ok");

    // list.count++
    EmitBytes(0x48, 0xFF, 0x40, 0x10); // INC qword [rax+16]

    // Incref node — list holds a counted reference
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = node_ptr
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_insert");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_incref")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_managed_list_insert_last(list_ptr_rcx, node_ptr_rdx) -> void
  // Insert node at tail of managed list. Auto-detaches from old list if needed.
  // Stack: [rbp-8]=list_ptr, [rbp-16]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitManagedListInsertLast() {
    EmitRuntimeFunctionStart("maxon_managed_list_insert_last", 2, 0x40);

    // Auto-detach: if node.list != 0, unlink from old list and release old list's reference
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] -- node.list
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "managed_list_insert_last_no_detach");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // rcx = old list
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // rdx = node_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_managed_list_unlink")); EmitDword(0);
    // Decref node — old list releases its counted reference
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = node_ptr
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_detach");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);
    DefineLabel("managed_list_insert_last_no_detach");

    // old_tail = [list+8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x08); // MOV rcx, [rax+8] -- old_tail
    // rcx = old_tail

    // node.next = 0
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = node_ptr
    EmitMovRegImm(X86Register.Rax, 0);
    EmitBytes(0x48, 0x89, 0x02); // MOV [rdx], rax -- node.next = 0

    // node.prev = old_tail
    EmitBytes(0x48, 0x89, 0x4A, 0x08); // MOV [rdx+8], rcx -- node.prev = old_tail

    // node.list = list_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x89, 0x42, 0x10); // MOV [rdx+16], rax -- node.list = list_ptr

    // if old_tail != 0: old_tail.next = node_ptr
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "managed_list_insert_last_no_old_tail");
    EmitBytes(0x48, 0x89, 0x11); // MOV [rcx], rdx -- old_tail.next = node_ptr
    DefineLabel("managed_list_insert_last_no_old_tail");

    // list.tail = node_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = node_ptr
    EmitBytes(0x48, 0x89, 0x50, 0x08); // MOV [rax+8], rdx -- list.tail = node_ptr

    // if list.head == 0: list.head = node_ptr
    EmitBytes(0x48, 0x83, 0x38, 0x00); // CMP qword [rax], 0
    EmitJcc("ne", "managed_list_insert_last_head_ok");
    EmitBytes(0x48, 0x89, 0x10); // MOV [rax], rdx -- list.head = node_ptr
    DefineLabel("managed_list_insert_last_head_ok");

    // list.count++
    EmitBytes(0x48, 0xFF, 0x40, 0x10); // INC qword [rax+16]

    // Incref node — list holds a counted reference
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = node_ptr
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_insert");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_incref")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_managed_list_insert_after(list_ptr_rcx, target_ptr_rdx, node_ptr_r8) -> void
  // Insert node after target in managed list. Auto-detaches from old list if needed.
  // Stack: [rbp-8]=list_ptr, [rbp-16]=target_ptr, [rbp-24]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitManagedListInsertAfter() {
    EmitRuntimeFunctionStart("maxon_managed_list_insert_after", 3, 0x40);

    // Auto-detach: if node.list != 0, unlink from old list and release old list's reference
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] -- node.list
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "managed_list_insert_after_no_detach");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // rcx = old list
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // rdx = node_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_managed_list_unlink")); EmitDword(0);
    // Decref node — old list releases its counted reference
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = node_ptr
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_detach");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);
    DefineLabel("managed_list_insert_after_no_detach");

    // after = [target+0] (target.next)
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = target_ptr
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] -- after = target.next
    // rax = after

    // node.next = after
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = node_ptr
    EmitBytes(0x48, 0x89, 0x01); // MOV [rcx], rax -- node.next = after

    // node.prev = target_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = target_ptr
    EmitBytes(0x48, 0x89, 0x51, 0x08); // MOV [rcx+8], rdx -- node.prev = target_ptr

    // node.list = list_ptr
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // RDX = list_ptr
    EmitBytes(0x48, 0x89, 0x51, 0x10); // MOV [rcx+16], rdx -- node.list = list_ptr

    // target.next = node_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = target_ptr
    EmitBytes(0x48, 0x89, 0x0A); // MOV [rdx], rcx -- target.next = node_ptr

    // if after != 0: after.prev = node_ptr; else: list.tail = node_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "managed_list_insert_after_was_tail");
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx -- after.prev = node_ptr
    EmitJmp("managed_list_insert_after_linked");
    DefineLabel("managed_list_insert_after_was_tail");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx -- list.tail = node_ptr
    DefineLabel("managed_list_insert_after_linked");

    // list.count++
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0xFF, 0x40, 0x10); // INC qword [rax+16]

    // Incref node — list holds a counted reference
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = node_ptr
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_insert");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_incref")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_managed_list_insert_before(list_ptr_rcx, target_ptr_rdx, node_ptr_r8) -> void
  // Insert node before target in managed list. Auto-detaches from old list if needed.
  // Stack: [rbp-8]=list_ptr, [rbp-16]=target_ptr, [rbp-24]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitManagedListInsertBefore() {
    EmitRuntimeFunctionStart("maxon_managed_list_insert_before", 3, 0x40);

    // Auto-detach: if node.list != 0, unlink from old list and release old list's reference
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] -- node.list
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "managed_list_insert_before_no_detach");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // rcx = old list
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // rdx = node_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_managed_list_unlink")); EmitDword(0);
    // Decref node — old list releases its counted reference
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = node_ptr
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_detach");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);
    DefineLabel("managed_list_insert_before_no_detach");

    // before = [target+8] (target.prev)
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = target_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8] -- before = target.prev
    // rax = before

    // node.next = target_ptr
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = node_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = target_ptr
    EmitBytes(0x48, 0x89, 0x11); // MOV [rcx], rdx -- node.next = target_ptr

    // node.prev = before
    EmitBytes(0x48, 0x89, 0x41, 0x08); // MOV [rcx+8], rax -- node.prev = before

    // node.list = list_ptr
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // RDX = list_ptr
    EmitBytes(0x48, 0x89, 0x51, 0x10); // MOV [rcx+16], rdx -- node.list = list_ptr

    // target.prev = node_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = target_ptr
    EmitBytes(0x48, 0x89, 0x4A, 0x08); // MOV [rdx+8], rcx -- target.prev = node_ptr

    // if before != 0: before.next = node_ptr; else: list.head = node_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "managed_list_insert_before_was_head");
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx -- before.next = node_ptr
    EmitJmp("managed_list_insert_before_linked");
    DefineLabel("managed_list_insert_before_was_head");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx -- list.head = node_ptr
    DefineLabel("managed_list_insert_before_linked");

    // list.count++
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0xFF, 0x40, 0x10); // INC qword [rax+16]

    // Incref node — list holds a counted reference
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = node_ptr
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_insert");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_incref")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_managed_list_unlink(list_ptr_rcx, node_ptr_rdx) -> void
  // Unlink node from managed list. No-op if node.list == 0. Does NOT free.
  // Stack: [rbp-8]=list_ptr, [rbp-16]=node_ptr
  // -------------------------------------------------------------------------
  private void EmitManagedListUnlink() {
    EmitRuntimeFunctionStart("maxon_managed_list_unlink", 2, 0x40);

    // If node.list == 0, no-op
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] -- node.list
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "managed_list_unlink_done");

    // prev = [node+8], next = [node+0]
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x08); // MOV rcx, [rax+8] -- prev
    EmitBytes(0x48, 0x8B, 0x10); // MOV rdx, [rax] -- next
    // rcx = prev, rdx = next

    // Reconnect: if prev != 0: prev.next = next; else: list.head = next
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "managed_list_unlink_no_prev");
    EmitBytes(0x48, 0x89, 0x11); // MOV [rcx], rdx -- prev.next = next
    EmitJmp("managed_list_unlink_prev_done");
    DefineLabel("managed_list_unlink_no_prev");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x89, 0x10); // MOV [rax], rdx -- list.head = next
    DefineLabel("managed_list_unlink_prev_done");

    // if next != 0: next.prev = prev; else: list.tail = prev
    EmitBytes(0x48, 0x85, 0xD2); // TEST rdx, rdx
    EmitJcc("z", "managed_list_unlink_no_next");
    EmitBytes(0x48, 0x89, 0x4A, 0x08); // MOV [rdx+8], rcx -- next.prev = prev
    EmitJmp("managed_list_unlink_next_done");
    DefineLabel("managed_list_unlink_no_next");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx -- list.tail = prev
    DefineLabel("managed_list_unlink_next_done");

    // Clear node's links: node.next = 0, node.prev = 0, node.list = 0
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = node_ptr
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx -- node.next = 0
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx -- node.prev = 0
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx -- node.list = 0

    // list.count--
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0xFF, 0x48, 0x10); // DEC qword [rax+16]

    DefineLabel("managed_list_unlink_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_managed_list_clear(list_ptr_rcx) -> void
  // Primitive variant: free each node without decref'ing node values.
  // -------------------------------------------------------------------------
  private void EmitManagedListClear() => EmitManagedListClearImpl("maxon_managed_list_clear", managed: false);

  // -------------------------------------------------------------------------
  // maxon_managed_list_clear_managed(list_ptr_rcx) -> void
  // Managed variant: decref each node's heap value before freeing the node.
  // -------------------------------------------------------------------------
  private void EmitManagedListClearManaged() => EmitManagedListClearImpl("maxon_managed_list_clear_managed", managed: true);

  // -------------------------------------------------------------------------
  // Shared implementation for managed list clear (primitive vs managed).
  // Walks list head->next, optionally decrefs node values, frees each node,
  // then zeros the list metadata.
  // Stack: [rbp-8]=list_ptr, [rbp-40]=current, [rbp-48]=next
  // -------------------------------------------------------------------------
  private void EmitManagedListClearImpl(string funcName, bool managed) {
    EmitRuntimeFunctionStart(funcName, 1, 0x60);

    // current = [list+0] -- list.head
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] -- list.head
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = current

    DefineLabel($"{funcName}_loop");
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", $"{funcName}_loop_done");

    // Save next before modifying: next = [current+0]
    EmitBytes(0x48, 0x8B, 0x48, 0x00); // MOV rcx, [rax+0] -- current.next
    EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-48] = next

    // Zero node's list links
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx -- node.next = 0
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx -- node.prev = 0
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx -- node.list = 0

    if (managed) {
      // Decref the heap value stored in the node (node+24)
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current (user ptr)
      EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] -- node.value
      if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_clear");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);
    }

    // Free the node via mm_decref(user_ptr) -- each node is its own allocation
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = current (user ptr)
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_clear");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);

    // current = next
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitJmp($"{funcName}_loop");

    DefineLabel($"{funcName}_loop_done");

    // Zero list metadata: head = 0, tail = 0, count = 0
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx -- list.head = 0
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx -- list.tail = 0
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx -- list.count = 0

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // maxon_managed_list_decref_values(list_ptr_rcx) -> void
  // Walks managed list nodes and decrefs each node's heap value without freeing
  // the nodes. Used during list destruction: the nodes themselves will be freed
  // separately, but their values need explicit decref first.
  // Stack: [rbp-8]=list_ptr, [rbp-40]=current
  // -------------------------------------------------------------------------
  private void EmitManagedListDecrefValues() {
    EmitRuntimeFunctionStart("maxon_managed_list_decref_values", 1, 0x60);

    // current = [list+0] -- list.head
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = list_ptr
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] -- list.head
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = current

    DefineLabel("maxon_managed_list_decref_values_loop");
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "maxon_managed_list_decref_values_done");

    // Save next before any calls: next = [current+0]
    EmitBytes(0x48, 0x8B, 0x48, 0x00); // MOV rcx, [rax+0] -- current.next
    EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-48] = next

    // Decref the heap value stored in the node (node+24)
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current (user ptr)
    EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] -- node.value
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_managed_list_decref_values");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_decref")); EmitDword(0);

    // current = next
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitJmp("maxon_managed_list_decref_values_loop");

    DefineLabel("maxon_managed_list_decref_values_done");

    EmitRuntimeFunctionEnd();
  }

}
