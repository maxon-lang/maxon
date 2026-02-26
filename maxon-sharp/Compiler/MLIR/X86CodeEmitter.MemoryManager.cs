using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class X86CodeEmitter {

  // ==========================================================================
  // Memory Manager — scope-based allocation tracking with hierarchical ownership
  // ==========================================================================
  //
  // ScopeFrame (56 bytes):
  //   +0   scope_id        (u64)
  //   +8   parent_scope    (ptr)
  //   +16  alloc_head      (ptr)
  //   +24  alloc_tail      (ptr)
  //   +32  tag_cstr        (ptr)
  //   +40  alloc_count     (u64)
  //   +48  depth           (u64)
  //
  // AllocEntry (80 bytes):
  //   +0   user_ptr        (ptr)
  //   +8   size            (u64)
  //   +16  next            (ptr)
  //   +24  prev            (ptr)
  //   +32  child_head      (ptr)
  //   +40  child_tail      (ptr)
  //   +48  owner_entry     (ptr to parent AllocEntry, or NULL if scope-owned)
  //   +56  owner_scope     (ptr to owning ScopeFrame, or NULL if parent-owned)
  //   +64  tag_cstr        (ptr)
  //   +72  refcount        (u64)
  //
  // Every managed allocation has an 8-byte header at [ptr-8] = pointer to its AllocEntry.

  public void EmitMemoryManagerFunctions() {
    DefineSymdata("__mm_current_scope", new byte[8]);
    DefineSymdata("__mm_root_scope", new byte[8]);
    DefineSymdata("__mm_scope_id_counter", new byte[8]);
    DefineSymdata("__mm_alloc_count", new byte[8]);

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
      DefineSymdata("__mm_tag_scope_enter", "scope_enter \0"u8.ToArray());
      DefineSymdata("__mm_tag_scope_exit", "scope_exit \0"u8.ToArray());
      DefineSymdata("__mm_tag_alloc", "alloc \0"u8.ToArray());
      DefineSymdata("__mm_tag_alloc_in", "alloc_in \0"u8.ToArray());
      DefineSymdata("__mm_tag_free", "free \0"u8.ToArray());
      DefineSymdata("__mm_tag_incref", "incref \0"u8.ToArray());
      DefineSymdata("__mm_tag_decref", "decref \0"u8.ToArray());
      DefineSymdata("__mm_tag_move", "move \0"u8.ToArray());
      DefineSymdata("__mm_tag_move_arrow", " -> \0"u8.ToArray());
      DefineSymdata("__mm_tag_rc_eq", " rc=\0"u8.ToArray());
      DefineSymdata("__mm_tag_depth_open", " (depth=\0"u8.ToArray());
      DefineSymdata("__mm_tag_owned_open", " (\0"u8.ToArray());
      DefineSymdata("__mm_tag_owned_suffix", " owned)\0"u8.ToArray());
      DefineSymdata("__mm_tag_close_paren", ")\0"u8.ToArray());
      DefineSymdata("__mm_tag_space2", "  \0"u8.ToArray());
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
    DefineSymdata("__mm_panic_move_null",
      "mm_move called with NULL pointer\0"u8.ToArray());
    DefineSymdata("__mm_panic_move_unmanaged",
      "mm_move called on unmanaged pointer (no AllocEntry)\0"u8.ToArray());
    DefineSymdata("__mm_panic_move_child",
      "mm_move called on parent-owned allocation (use mode=1)\0"u8.ToArray());
    DefineSymdata("__mm_panic_alloc_in_null_parent",
      "mm_alloc_in called with NULL parent pointer\0"u8.ToArray());
    DefineSymdata("__mm_panic_alloc_in_unmanaged_parent",
      "mm_alloc_in called with unmanaged parent (no AllocEntry)\0"u8.ToArray());
    DefineSymdata("__mm_panic_realloc_unmanaged",
      "mm_realloc called on unmanaged pointer (no AllocEntry)\0"u8.ToArray());
    DefineSymdata("__mm_panic_scope_exit_mismatch",
      "mm_scope_exit called with wrong scope (not current scope)\0"u8.ToArray());
    DefineSymdata("__mm_panic_alloc_count_underflow",
      "mm_scope_exit: global alloc count went negative (double free?)\0"u8.ToArray());
    // Debug strings for enhanced panic output
    DefineSymdata("__mm_debug_move_ptr", "mm_move bad ptr=\0"u8.ToArray());
    DefineSymdata("__mm_debug_dest", " dest=\0"u8.ToArray());
    DefineSymdata("__mm_debug_mode", " mode=\0"u8.ToArray());
    DefineSymdata("__mm_debug_free_ptr", "mm_free bad ptr=\0"u8.ToArray());
    // Panic strings for refcount checks
    DefineSymdata("__mm_panic_scope_exit_refcount",
      "mm_scope_exit: allocation has non-zero refcount\n\0"u8.ToArray());
    DefineSymdata("__mm_panic_decref_negative",
      "mm_decref: refcount went negative\n\0"u8.ToArray());

    EmitMmTracePrintTag();
    EmitMmTracePrintHex();
    EmitMmTracePrintI64();
    if (Compiler.MmTrace) {
      EmitMmTracePrintIndent();
      EmitMmTracePrintEntryTag();
    }
    EmitMmScopeEnter();
    EmitMmScopeExit();
    EmitMmAlloc();
    EmitMmAllocIn();
    EmitMmRealloc();
    EmitMmMove();
    EmitMmFreeEntry();
    EmitMmFree();
    EmitMmFreeIfNonnull();
    EmitMmReparentIfNonnull();
    EmitMmGetRootScope();
    EmitMmAllocSimple();
    EmitMmFreeSimple();
    EmitMmIncref();
    EmitMmDecref();
    EmitMmSetOwner();
    EmitMmLeakCheck();
    EmitMmValidatePtr();
    if (Compiler.MmTrace) {
      EmitMmTraceScopeEnter();
      EmitMmTraceScopeExit();
      EmitMmTraceAlloc();
      EmitMmTraceFree();
      EmitMmTraceIncref();
      EmitMmTraceDecref();
      EmitMmTraceMove();
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
    // Decrement counter
    EmitBytes(0x48, 0xFF, 0xC9); // DEC rcx
    // Loop if rcx >= 0 (signed)
    EmitBytes(0x48, 0x83, 0xF9, 0x00); // CMP rcx, 0
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
  // mm_trace_print_indent() — print 2*depth spaces based on __mm_current_scope
  // Args: none
  // -------------------------------------------------------------------------
  private void EmitMmTracePrintIndent() {
    EmitRuntimeFunctionStart("mm_trace_print_indent", 0, 0x30);

    // Load __mm_current_scope
    EmitSymdataLoadI64(X86Register.Rax, "__mm_current_scope");
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_trace_indent_done");

    // Load scope.depth at offset +48
    EmitBytes(0x48, 0x8B, 0x40, 0x30); // MOV rax, [rax+48] — scope.depth
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // [rbp-8] = depth

    DefineLabel("mm_trace_indent_loop");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = remaining depth
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_trace_indent_done");

    // Print "  " (2 spaces)
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_space2");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_write_stderr")); EmitDword(0);

    // Decrement depth counter
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0xFF, 0xC8); // DEC rax
    EmitMovMemReg(-0x08, X86Register.Rax, 8);
    EmitJmp("mm_trace_indent_loop");

    DefineLabel("mm_trace_indent_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_print_entry_tag(entry_ptr) — print tag_cstr from an AllocEntry
  // Args: rcx = entry pointer
  // If entry.tag_cstr is NULL, prints "(null)" instead.
  // -------------------------------------------------------------------------
  private void EmitMmTracePrintEntryTag() {
    EmitRuntimeFunctionStart("mm_trace_print_entry_tag", 1, 0x30);

    // Load entry.tag_cstr from offset +64
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry_ptr
    EmitBytes(0x48, 0x8B, 0x80, 0x40, 0x00, 0x00, 0x00); // MOV rax, [rax+64] — entry.tag_cstr

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

  // -------------------------------------------------------------------------
  // mm_scope_enter(tag_cstr_in_rcx) -> scope_ptr_in_rax
  // -------------------------------------------------------------------------
  private void EmitMmScopeEnter() {
    // Stack: [rbp-8]=tag_cstr, [rbp-40]=new scope ptr, [rbp-48]=new scope_id
    EmitRuntimeFunctionStart("mm_scope_enter", 1, 0x60);

    // Increment __mm_scope_id_counter
    EmitSymdataLoadI64(X86Register.Rax, "__mm_scope_id_counter");
    EmitAddRegImm(X86Register.Rax, 1);
    EmitSymdataStoreI64(X86Register.Rax, "__mm_scope_id_counter");
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = new scope_id

    // HeapAlloc(56) with HEAP_ZERO_MEMORY for ScopeFrame
    EmitMovRegImm(X86Register.Rax, 56);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = 56 (size for HeapAlloc R8)
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x28);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = new scope ptr

    // Set scope_id at [scope+0]
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = scope_id
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = scope ptr
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — scope.scope_id = id

    // Set parent_scope at [scope+8] = __mm_current_scope
    EmitSymdataLoadI64(X86Register.Rcx, "__mm_current_scope");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx — scope.parent_scope

    // Set tag_cstr at [scope+32]
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = tag arg
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x20); // MOV [rax+32], rcx — scope.tag_cstr

    // alloc_head, alloc_tail, alloc_count are already zero from HEAP_ZERO_MEMORY

    // depth = parent_scope ? parent_scope.depth + 1 : 0
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = scope ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8] — scope.parent_scope
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_scope_enter_depth_zero");
    EmitBytes(0x48, 0x8B, 0x40, 0x30); // MOV rax, [rax+48] — parent_scope.depth
    EmitAddRegImm(X86Register.Rax, 1);
    EmitJmp("mm_scope_enter_depth_store");
    DefineLabel("mm_scope_enter_depth_zero");
    EmitBytes(0x31, 0xC0); // XOR eax, eax
    DefineLabel("mm_scope_enter_depth_store");
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = scope ptr
    EmitBytes(0x48, 0x89, 0x41, 0x30); // MOV [rcx+48], rax — scope.depth = computed depth

    // Set __mm_current_scope = new scope
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitSymdataStoreI64(X86Register.Rax, "__mm_current_scope");

    // Return scope ptr
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_scope_exit(scope_ptr_in_rcx) -> void
  // -------------------------------------------------------------------------
  private void EmitMmScopeExit() {
    // Stack: [rbp-8]=scope_ptr, [rbp-40]=current entry, [rbp-48]=next entry
    EmitRuntimeFunctionStart("mm_scope_exit", 1, 0x60);

    // Validate scope_ptr matches __mm_current_scope
    EmitSymdataLoadI64(X86Register.Rax, "__mm_current_scope");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = scope_ptr arg
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "mm_scope_exit_scope_valid");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_scope_exit_mismatch");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_scope_exit_scope_valid");

    // Walk alloc_head: for each entry, call mm_free_entry(entry)
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = scope_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] — scope.alloc_head
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = current entry

    DefineLabel("mm_scope_exit_loop");
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current entry
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_scope_exit_loop_done");

    // Save next pointer before freeing: next = entry.next (at entry+16)
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] — entry.next
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = next

    // Panic if entry has non-zero refcount
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = current entry
    EmitBytes(0x48, 0x83, 0x78, 0x48, 0x00); // CMP qword [rax+72], 0 — entry.refcount
    EmitJcc("e", "mm_scope_exit_refcount_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_scope_exit_refcount");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_scope_exit_refcount_ok");

    // Call mm_free_entry(current entry)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free_entry")); EmitDword(0);

    // Advance to next
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitJmp("mm_scope_exit_loop");

    DefineLabel("mm_scope_exit_loop_done");

    // Check that __mm_alloc_count didn't underflow (go negative)
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_count");
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("ns", "mm_scope_exit_count_ok"); // SF=0 means non-negative
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_alloc_count_underflow");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_scope_exit_count_ok");

    // Restore parent scope: __mm_current_scope = scope.parent_scope
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = scope_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8] — scope.parent_scope
    EmitSymdataStoreI64(X86Register.Rax, "__mm_current_scope");

    // HeapFree the scope frame itself
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // reuse slot for HeapFree R8
    EmitHeapCall("HeapFree", 0, rbpSlotR8: -0x28);

    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_alloc(size_in_rcx, tag_cstr_in_rdx) -> user_ptr_in_rax
  // -------------------------------------------------------------------------
  private void EmitMmAlloc() {
    // Stack: [rbp-8]=size, [rbp-16]=tag, [rbp-40]=raw_ptr, [rbp-48]=entry_ptr,
    //        [rbp-56]=user_ptr, [rbp-64]=scope_ptr, [rbp-72]=alloc_size
    EmitRuntimeFunctionStart("mm_alloc", 2, 0x80);

    // Load current scope
    EmitSymdataLoadI64(X86Register.Rax, "__mm_current_scope");
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // [rbp-64] = scope_ptr

    // HeapAlloc(size + 8) for payload with backpointer header
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = size
    EmitAddRegImm(X86Register.Rax, 8);
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // [rbp-72] = size + 8
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x48);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = raw_ptr

    // HeapAlloc(80) for AllocEntry
    EmitMovRegImm(X86Register.Rax, 80);
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // [rbp-72] = 80
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x48);
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

    // entry.tag_cstr = tag arg
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x88, 0x40, 0x00, 0x00, 0x00); // MOV [rax+64], rcx — entry.tag_cstr

    // entry.owner_entry = NULL, entry.refcount = 0 (already zero from HEAP_ZERO_MEMORY)

    // entry.owner_scope = scope_ptr
    EmitMovRegMem(X86Register.Rcx, -0x40, 8); // RCX = scope_ptr
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x38); // MOV [rax+56], rcx — entry.owner_scope

    // Set backpointer header: [raw_ptr] = entry_ptr
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = raw_ptr
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = entry_ptr
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — [raw_ptr] = entry

    // Append entry to scope's linked list
    // Check if scope.alloc_tail != NULL
    EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = scope_ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x18); // MOV rax, [rax+24] — scope.alloc_tail
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_alloc_empty_list");

    // Non-empty list: tail.next = entry, entry.prev = tail, scope.alloc_tail = entry
    // RAX = old tail
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = entry
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — old_tail.next = entry
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = entry
    EmitBytes(0x48, 0x89, 0x41, 0x18); // MOV [rcx+24], rax — entry.prev = old_tail
    EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = scope_ptr
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = entry
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — scope.alloc_tail = entry
    EmitJmp("mm_alloc_list_done");

    // Empty list: scope.alloc_head = entry, scope.alloc_tail = entry
    DefineLabel("mm_alloc_empty_list");
    EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = scope_ptr
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = entry
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — scope.alloc_head = entry
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — scope.alloc_tail = entry

    DefineLabel("mm_alloc_list_done");

    // Increment scope.alloc_count
    EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = scope_ptr
    EmitBytes(0x48, 0xFF, 0x40, 0x28); // INC qword [rax+40] — scope.alloc_count++

    // Increment global alloc counter
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_count");
    EmitBytes(0x48, 0xFF, 0xC0); // INC rax
    EmitSymdataStoreI64(X86Register.Rax, "__mm_alloc_count");

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

    // HeapAlloc(size + 8) for payload
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = size
    EmitAddRegImm(X86Register.Rax, 8);
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // [rbp-72] = size + 8
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x48);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = raw_ptr

    // HeapAlloc(80) for AllocEntry
    EmitMovRegImm(X86Register.Rax, 80);
    EmitMovMemReg(-0x48, X86Register.Rax, 8);
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x48);
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

    // entry.tag_cstr = tag arg (r8 saved at [rbp-24])
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x88, 0x40, 0x00, 0x00, 0x00); // MOV [rax+64], rcx — entry.tag_cstr

    // entry.owner_entry = parent_entry
    EmitMovRegMem(X86Register.Rcx, -0x40, 8); // RCX = parent_entry
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x30); // MOV [rax+48], rcx — entry.owner_entry

    // entry.owner_scope = NULL (already zero from HEAP_ZERO_MEMORY)

    // Set backpointer header: [raw_ptr] = entry_ptr
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx — [raw_ptr] = entry

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

    if (Compiler.MmTrace) {
      // Trace: indent + "alloc_in " + entry.tag_cstr + newline
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

    // Managed path: HeapReAlloc(user_ptr - 8, new_size + 8)
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // user_ptr
    EmitSubRegImm(X86Register.Rax, 8);
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // [rbp-64] = old_raw (ptr - 8)

    EmitMovRegMem(X86Register.Rax, -0x10, 8); // new_size
    EmitAddRegImm(X86Register.Rax, 8);
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // [rbp-56] = new_size + 8

    EmitHeapCall("HeapReAlloc", 0x08, rbpSlotR8: -0x40, rbpSlotR9: -0x38);
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

    // Return new_raw + 8
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // new_raw
    EmitAddRegImm(X86Register.Rax, 8);

    DefineLabel("mm_realloc_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_move(user_ptr_in_rcx, dest_ptr_in_rdx, mode_in_r8) -> void
  // Unified move: transfers allocation ownership to a new owner.
  //   mode=0: dest is a scope pointer (move to scope's alloc list)
  //   mode=1: dest is an allocation's user pointer (reparent under allocation)
  // -------------------------------------------------------------------------
  private void EmitMmMove() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=dest_ptr, [rbp-24]=mode,
    //        [rbp-40]=entry, [rbp-48]=owner_scope_or_entry, [rbp-56]=prev, [rbp-64]=next,
    //        [rbp-72]=dest_entry (for mode=1)
    EmitRuntimeFunctionStart("mm_move", 3, 0x80);

    // If ptr == NULL, panic
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_move_ptr_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_move_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_move_ptr_ok");

    // Load entry = [ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax]
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = entry

    // If entry == 0, print ptr value then panic (unmanaged pointer)
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_move_entry_ok");
    // Print "mm_move bad ptr=" and the pointer value for debugging
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_debug_move_ptr");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // user_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_hex")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_debug_dest");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // dest_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_hex")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_debug_mode");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // mode
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_hex")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_move_unmanaged");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_move_entry_ok");

    // Mode 0 (scope transfer): skip for parent-owned allocations — they stay
    // owned by their parent (e.g., Array.get returns a reference, not a detached copy)
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = mode
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_move_skip_parent_check"); // mode=1 always proceeds
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
    EmitBytes(0x48, 0x83, 0x78, 0x30, 0x00); // CMP qword [rax+48], 0 — entry.owner_entry
    EmitJcc("ne", "mm_move_done"); // parent-owned + mode=0 → no-op
    DefineLabel("mm_move_skip_parent_check");

    // Load entry.prev and entry.next for unlinking
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // entry
    EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — entry.prev
    EmitMovMemReg(-0x38, X86Register.Rcx, 8); // [rbp-56] = prev
    EmitBytes(0x48, 0x8B, 0x48, 0x10); // MOV rcx, [rax+16] — entry.next
    EmitMovMemReg(-0x40, X86Register.Rcx, 8); // [rbp-64] = next

    // Check if scope-owned: entry.owner_scope (at +56)
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x8B, 0x40, 0x38); // MOV rax, [rax+56] — entry.owner_scope
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = owner_scope
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_move_unlink_parent_owned");

    // --- Scope-owned: unlink from scope's alloc list ---
    // if prev: prev.next = next  else: scope.alloc_head = next
    EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = prev
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_move_scope_no_prev");
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — prev.next = next
    EmitJmp("mm_move_scope_prev_done");
    DefineLabel("mm_move_scope_no_prev");
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = owner_scope
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — scope.alloc_head = next
    DefineLabel("mm_move_scope_prev_done");

    // if next: next.prev = prev  else: scope.alloc_tail = prev
    EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = next
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_move_scope_no_next");
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — next.prev = prev
    EmitJmp("mm_move_scope_next_done");
    DefineLabel("mm_move_scope_no_next");
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = owner_scope
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — scope.alloc_tail = prev
    DefineLabel("mm_move_scope_next_done");

    // Decrement scope.alloc_count
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0xFF, 0x48, 0x28); // DEC qword [rax+40]

    EmitJmp("mm_move_unlinked");

    // --- Parent-owned: unlink from parent entry's child list ---
    DefineLabel("mm_move_unlink_parent_owned");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x8B, 0x40, 0x30); // MOV rax, [rax+48] — entry.owner_entry
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = owner_entry (reuse slot)

    // if prev: prev.next = next  else: owner_entry.child_head = next
    EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = prev
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_move_parent_no_prev");
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — prev.next = next
    EmitJmp("mm_move_parent_prev_done");
    DefineLabel("mm_move_parent_no_prev");
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = owner_entry
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x20); // MOV [rax+32], rcx — owner_entry.child_head = next
    DefineLabel("mm_move_parent_prev_done");

    // if next: next.prev = prev  else: owner_entry.child_tail = prev
    EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = next
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_move_parent_no_next");
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — next.prev = prev
    EmitJmp("mm_move_parent_next_done");
    DefineLabel("mm_move_parent_no_next");
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = owner_entry
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x28); // MOV [rax+40], rcx — owner_entry.child_tail = prev
    DefineLabel("mm_move_parent_next_done");

    DefineLabel("mm_move_unlinked");

    // Clear entry's prev/next for clean insertion
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — entry.next = NULL
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — entry.prev = NULL

    // Branch on mode: 0 = scope, 1 = reparent under allocation
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = mode
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_move_to_parent");

    // ===== Mode 0: Link into dest scope's alloc list =====
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = dest_scope
    EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — dest_scope.alloc_tail
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_move_scope_dest_empty");

    // Non-empty: old_tail.next = entry, entry.prev = old_tail, dest.alloc_tail = entry
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
    EmitBytes(0x48, 0x89, 0x41, 0x10); // MOV [rcx+16], rax — old_tail.next = entry
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — entry.prev = old_tail
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = dest_scope
    EmitBytes(0x48, 0x89, 0x41, 0x18); // MOV [rcx+24], rax — dest_scope.alloc_tail = entry
    EmitJmp("mm_move_scope_dest_done");

    DefineLabel("mm_move_scope_dest_empty");
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = dest_scope
    EmitBytes(0x48, 0x89, 0x41, 0x10); // MOV [rcx+16], rax — dest_scope.alloc_head = entry
    EmitBytes(0x48, 0x89, 0x41, 0x18); // MOV [rcx+24], rax — dest_scope.alloc_tail = entry

    DefineLabel("mm_move_scope_dest_done");

    // Increment dest_scope.alloc_count
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = dest_scope
    EmitBytes(0x48, 0xFF, 0x40, 0x28); // INC qword [rax+40]

    // Set entry.owner_scope = dest_scope, entry.owner_entry = NULL
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = dest_scope
    EmitBytes(0x48, 0x89, 0x48, 0x38); // MOV [rax+56], rcx — entry.owner_scope = dest_scope
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x48, 0x30); // MOV [rax+48], rcx — entry.owner_entry = NULL

    EmitJmp("mm_move_link_done");

    // ===== Mode 1: Reparent under dest allocation =====
    DefineLabel("mm_move_to_parent");

    // Resolve dest_entry = [dest_ptr - 8]
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax]
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // [rbp-72] = dest_entry

    // Link entry into dest_entry's child list
    EmitMovRegMem(X86Register.Rax, -0x48, 8); // RAX = dest_entry
    EmitBytes(0x48, 0x8B, 0x48, 0x28); // MOV rcx, [rax+40] — dest_entry.child_tail
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_move_parent_dest_empty");

    // Non-empty: old_tail.next = entry, entry.prev = old_tail, parent.child_tail = entry
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
    EmitBytes(0x48, 0x89, 0x41, 0x10); // MOV [rcx+16], rax — old_tail.next = entry
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — entry.prev = old_tail
    EmitMovRegMem(X86Register.Rcx, -0x48, 8); // RCX = dest_entry
    EmitBytes(0x48, 0x89, 0x41, 0x28); // MOV [rcx+40], rax — dest_entry.child_tail = entry
    EmitJmp("mm_move_parent_dest_done");

    DefineLabel("mm_move_parent_dest_empty");
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
    EmitMovRegMem(X86Register.Rcx, -0x48, 8); // RCX = dest_entry
    EmitBytes(0x48, 0x89, 0x41, 0x20); // MOV [rcx+32], rax — dest_entry.child_head = entry
    EmitBytes(0x48, 0x89, 0x41, 0x28); // MOV [rcx+40], rax — dest_entry.child_tail = entry

    DefineLabel("mm_move_parent_dest_done");

    // Set entry.owner_entry = dest_entry, entry.owner_scope = NULL, refcount = 0
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = entry
    EmitMovRegMem(X86Register.Rcx, -0x48, 8); // RCX = dest_entry
    EmitBytes(0x48, 0x89, 0x48, 0x30); // MOV [rax+48], rcx — entry.owner_entry = dest_entry
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x48, 0x38); // MOV [rax+56], rcx — entry.owner_scope = NULL
    EmitBytes(0x48, 0x89, 0x48, 0x48); // MOV [rax+72], rcx — entry.refcount = 0 (now parent-owned)

    DefineLabel("mm_move_link_done");

    DefineLabel("mm_move_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_free_entry(entry_ptr_in_rcx) -> void (internal)
  // Recursively frees all children, then frees the payload and entry itself.
  // -------------------------------------------------------------------------
  private void EmitMmFreeEntry() {
    // Stack: [rbp-8]=entry_ptr, [rbp-40]=current_child, [rbp-48]=next_child,
    //        [rbp-56]=user_ptr, [rbp-64]=heap_free_arg
    EmitRuntimeFunctionStart("mm_free_entry", 1, 0x80);

    // Walk entry.child_head: for each child, recursively call mm_free_entry
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = entry
    EmitBytes(0x48, 0x8B, 0x40, 0x20); // MOV rax, [rax+32] — entry.child_head
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = current_child

    DefineLabel("mm_free_entry_child_loop");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_entry_children_done");

    // Save next before recursing
    EmitBytes(0x48, 0x8B, 0x40, 0x10); // MOV rax, [rax+16] — child.next
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = next_child

    // Recurse: mm_free_entry(current_child)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free_entry")); EmitDword(0);

    // Advance
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitJmp("mm_free_entry_child_loop");

    DefineLabel("mm_free_entry_children_done");

    if (Compiler.MmTrace) {
      // Trace: indent + "free " + entry.tag_cstr + newline
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_free");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
      EmitMovRegMem(X86Register.Rcx, -0x08, 8); // entry_ptr
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
      EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    }

    // Decrement global alloc counter
    EmitSymdataLoadI64(X86Register.Rax, "__mm_alloc_count");
    EmitBytes(0x48, 0xFF, 0xC8); // DEC rax
    EmitSymdataStoreI64(X86Register.Rax, "__mm_alloc_count");

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
  // Unlinks an allocation from its owner, then calls mm_free_entry.
  // -------------------------------------------------------------------------
  private void EmitMmFree() {
    // Stack: [rbp-8]=user_ptr, [rbp-40]=entry, [rbp-48]=prev, [rbp-56]=next,
    //        [rbp-64]=owner_scope, [rbp-72]=owner_entry
    EmitRuntimeFunctionStart("mm_free", 1, 0x80);

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
    // If entry == NULL, print ptr value then panic — mm_free called on unmanaged pointer
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_free_entry_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_debug_free_ptr");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // user_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_hex")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_free_unmanaged");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_free_entry_ok");

    // Load prev and next
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — entry.prev
    EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-48] = prev
    EmitBytes(0x48, 0x8B, 0x48, 0x10); // MOV rcx, [rax+16] — entry.next
    EmitMovMemReg(-0x38, X86Register.Rcx, 8); // [rbp-56] = next

    // Check owner_scope
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x8B, 0x40, 0x38); // MOV rax, [rax+56] — entry.owner_scope
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // [rbp-64] = owner_scope
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_parent_owned");

    // Scope-owned: unlink from scope's alloc list
    // if prev: prev.next = next  else: scope.alloc_head = next
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_scope_no_prev");
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — prev.next = next
    EmitJmp("mm_free_scope_prev_done");
    DefineLabel("mm_free_scope_no_prev");
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — scope.alloc_head = next
    DefineLabel("mm_free_scope_prev_done");

    // if next: next.prev = prev  else: scope.alloc_tail = prev
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_scope_no_next");
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — next.prev = prev
    EmitJmp("mm_free_scope_next_done");
    DefineLabel("mm_free_scope_no_next");
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — scope.alloc_tail = prev
    DefineLabel("mm_free_scope_next_done");

    // Decrement scope.alloc_count
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitBytes(0x48, 0xFF, 0x48, 0x28); // DEC qword [rax+40]

    EmitJmp("mm_free_unlinked");

    // Parent-owned: unlink from parent entry's child list
    DefineLabel("mm_free_parent_owned");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x8B, 0x40, 0x30); // MOV rax, [rax+48] — entry.owner_entry
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // [rbp-72] = owner_entry

    // if prev: prev.next = next  else: owner.child_head = next
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_parent_no_prev");
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — prev.next = next
    EmitJmp("mm_free_parent_prev_done");
    DefineLabel("mm_free_parent_no_prev");
    EmitMovRegMem(X86Register.Rax, -0x48, 8);
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
    EmitMovRegMem(X86Register.Rax, -0x48, 8);
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
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free")); EmitDword(0);
    DefineLabel("mm_free_if_nonnull_skip");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_reparent_if_nonnull(child_ptr_in_rcx, parent_ptr_in_rdx) -> void
  // Calls mm_move(child, parent, mode=1) if child is non-null and managed.
  // Used to establish parent-child ownership for enum payload slots that
  // contain heap pointers (e.g., ListNode.next).
  // -------------------------------------------------------------------------
  private void EmitMmReparentIfNonnull() {
    EmitRuntimeFunctionStart("mm_reparent_if_nonnull", 2, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = child_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_reparent_if_nonnull_skip");
    // Check backpointer: if [ptr-8] == 0, skip (unmanaged)
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax]
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_reparent_if_nonnull_skip");
    // mm_move(child_ptr, parent_ptr, mode=1)
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // child_ptr
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // parent_ptr
    EmitMovRegImm(X86Register.R8, 1); // mode=1 (reparent)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_move")); EmitDword(0);
    DefineLabel("mm_reparent_if_nonnull_skip");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_get_root_scope() -> root_scope_ptr_in_rax
  // Returns the root scope pointer stored in __mm_root_scope symdata.
  // Used by global struct reassignment to move values to the root scope.
  // -------------------------------------------------------------------------
  private void EmitMmGetRootScope() {
    EmitRuntimeFunctionStart("mm_get_root_scope", 0, 0x10);
    EmitSymdataLoadI64(X86Register.Rax, "__mm_root_scope");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_alloc_simple(size_in_rcx) -> ptr_in_rax
  // HeapAlloc with HEAP_ZERO_MEMORY + 8-byte NULL sentinel header.
  // The first 8 bytes are zero so [user_ptr - 8] == 0 identifies unmanaged allocations.
  // -------------------------------------------------------------------------
  private void EmitMmAllocSimple() {
    // Stack: [rbp-8]=size, [rbp-16]=size+8
    EmitRuntimeFunctionStart("mm_alloc_simple", 1, 0x30);

    // Add 8 to size for the NULL sentinel header
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = size
    EmitAddRegImm(X86Register.Rax, 8);
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = size + 8

    // HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, size + 8)
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x10);

    // Return raw + 8 (skip past the NULL sentinel)
    EmitAddRegImm(X86Register.Rax, 8);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_free_simple(ptr_in_rcx) -> void
  // HeapFree adjusted for sentinel — subtracts 8 to get raw pointer. Skips NULL.
  // -------------------------------------------------------------------------
  private void EmitMmFreeSimple() {
    // Stack: [rbp-8]=ptr, [rbp-16]=raw_ptr
    EmitRuntimeFunctionStart("mm_free_simple", 1, 0x30);

    // Skip if ptr is NULL
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_free_simple_done");

    // Subtract 8 to get back to the raw pointer (before sentinel header)
    EmitSubRegImm(X86Register.Rax, 8);
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = raw_ptr

    // HeapFree(GetProcessHeap(), 0, raw_ptr)
    EmitHeapCall("HeapFree", 0, rbpSlotR8: -0x10);

    DefineLabel("mm_free_simple_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_incref(user_ptr_in_rcx) -> void
  // Increments refcount for a managed, scope-owned allocation.
  // Skips NULL, unmanaged (no backpointer), and parent-owned allocations.
  // -------------------------------------------------------------------------
  private void EmitMmIncref() {
    EmitRuntimeFunctionStart("mm_incref", 1, 0x30);

    // NULL check
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_incref_done");

    // Load entry = [user_ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry from backpointer

    // Unmanaged check (mm_alloc_simple / stack alloc)
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_incref_done");

    // Parent-owned check: entry.owner_entry != 0 means parent-owned
    EmitBytes(0x48, 0x83, 0x78, 0x30, 0x00); // CMP qword [rax+48], 0 — entry.owner_entry
    EmitJcc("ne", "mm_incref_done");

    // Increment refcount: entry.refcount += 1
    EmitBytes(0x48, 0xFF, 0x40, 0x48); // INC qword [rax+72] — entry.refcount++

    DefineLabel("mm_incref_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_decref(user_ptr_in_rcx) -> void
  // Decrements refcount. When it reaches zero, reclaims the allocation to the
  // current scope by unlinking from the old scope and linking into current.
  // Skips NULL, unmanaged, and parent-owned allocations.
  // -------------------------------------------------------------------------
  private void EmitMmDecref() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=entry, [rbp-24]=old_owner_scope,
    //        [rbp-40]=prev, [rbp-48]=next, [rbp-56]=current_scope
    EmitRuntimeFunctionStart("mm_decref", 1, 0x80);

    // NULL check
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_decref_done");

    // Load entry = [user_ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = entry

    // Unmanaged check
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_decref_done");

    // Parent-owned check: entry.owner_entry != 0
    EmitBytes(0x48, 0x83, 0x78, 0x30, 0x00); // CMP qword [rax+48], 0 — entry.owner_entry
    EmitJcc("ne", "mm_decref_done");

    // Decrement refcount: entry.refcount -= 1
    EmitBytes(0x48, 0xFF, 0x48, 0x48); // DEC qword [rax+72] — entry.refcount--

    // Panic if refcount went negative (sign flag set after DEC)
    EmitJcc("ns", "mm_decref_not_negative");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_decref_negative");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_decref_not_negative");

    // Check if refcount reached zero
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitBytes(0x48, 0x83, 0x78, 0x48, 0x00); // CMP qword [rax+72], 0 — entry.refcount
    EmitJcc("ne", "mm_decref_done");

    // Refcount is zero — reclaim ownership to current scope
    EmitSymdataLoadI64(X86Register.Rdx, "__mm_current_scope");
    EmitMovMemReg(-0x38, X86Register.Rdx, 8); // [rbp-56] = current_scope

    // Check if already owned by current scope
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitBytes(0x48, 0x8B, 0x48, 0x38); // MOV rcx, [rax+56] — entry.owner_scope
    EmitMovMemReg(-0x18, X86Register.Rcx, 8); // [rbp-24] = old_owner_scope
    EmitBytes(0x48, 0x39, 0xD1); // CMP rcx, rdx — old_owner_scope vs current_scope
    EmitJcc("e", "mm_decref_done");

    // --- Unlink entry from old scope's alloc list ---
    // Load prev and next
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — entry.prev
    EmitMovMemReg(-0x28, X86Register.Rcx, 8); // [rbp-40] = prev
    EmitBytes(0x48, 0x8B, 0x48, 0x10); // MOV rcx, [rax+16] — entry.next
    EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-48] = next

    // if prev: prev.next = next  else: old_scope.alloc_head = next
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = prev
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_decref_unlink_no_prev");
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = next
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — prev.next = next
    EmitJmp("mm_decref_unlink_prev_done");
    DefineLabel("mm_decref_unlink_no_prev");
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = old_owner_scope
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = next
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — scope.alloc_head = next
    DefineLabel("mm_decref_unlink_prev_done");

    // if next: next.prev = prev  else: old_scope.alloc_tail = prev
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = next
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_decref_unlink_no_next");
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = prev
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — next.prev = prev
    EmitJmp("mm_decref_unlink_next_done");
    DefineLabel("mm_decref_unlink_no_next");
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = old_owner_scope
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = prev
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — scope.alloc_tail = prev
    DefineLabel("mm_decref_unlink_next_done");

    // Decrement old scope.alloc_count
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = old_owner_scope
    EmitBytes(0x48, 0xFF, 0x48, 0x28); // DEC qword [rax+40] — scope.alloc_count--

    // --- Clear entry's prev/next for clean insertion ---
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — entry.next = NULL
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — entry.prev = NULL

    // --- Link entry into current scope's alloc list ---
    EmitMovRegMem(X86Register.Rdx, -0x38, 8); // RDX = current_scope
    EmitBytes(0x48, 0x8B, 0x4A, 0x18); // MOV rcx, [rdx+24] — current_scope.alloc_tail
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_decref_link_empty");

    // Non-empty: old_tail.next = entry, entry.prev = old_tail, scope.alloc_tail = entry
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitBytes(0x48, 0x89, 0x41, 0x10); // MOV [rcx+16], rax — old_tail.next = entry
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — entry.prev = old_tail
    EmitBytes(0x48, 0x89, 0x42, 0x18); // MOV [rdx+24], rax — scope.alloc_tail = entry
    EmitJmp("mm_decref_link_done");

    DefineLabel("mm_decref_link_empty");
    // Empty: scope.alloc_head = entry, scope.alloc_tail = entry
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitBytes(0x48, 0x89, 0x42, 0x10); // MOV [rdx+16], rax — scope.alloc_head = entry
    EmitBytes(0x48, 0x89, 0x42, 0x18); // MOV [rdx+24], rax — scope.alloc_tail = entry

    DefineLabel("mm_decref_link_done");

    // Increment current scope.alloc_count
    EmitMovRegMem(X86Register.Rdx, -0x38, 8); // RDX = current_scope
    EmitBytes(0x48, 0xFF, 0x42, 0x28); // INC qword [rdx+40] — scope.alloc_count++

    // Set entry.owner_scope = current_scope
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = entry
    EmitMovRegMem(X86Register.Rdx, -0x38, 8); // RDX = current_scope
    EmitBytes(0x48, 0x89, 0x50, 0x38); // MOV [rax+56], rdx — entry.owner_scope = current_scope

    DefineLabel("mm_decref_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_set_owner(user_ptr_in_rcx, new_owner_scope_in_rdx) -> void
  // Moves an allocation to a shallower scope if new_owner_scope.depth <
  // entry.owner_scope.depth. Used to extend lifetime when storing into
  // a longer-lived container.
  // -------------------------------------------------------------------------
  private void EmitMmSetOwner() {
    // Stack: [rbp-8]=user_ptr, [rbp-16]=new_owner_scope,
    //        [rbp-24]=entry, [rbp-32]=old_owner_scope,
    //        [rbp-40]=prev, [rbp-48]=next
    EmitRuntimeFunctionStart("mm_set_owner", 2, 0x80);

    // NULL check
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = user_ptr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_set_owner_done");

    // Load entry = [user_ptr - 8]
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = entry

    // Unmanaged check
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_set_owner_done");

    // Parent-owned check: entry.owner_entry != 0
    EmitBytes(0x48, 0x83, 0x78, 0x30, 0x00); // CMP qword [rax+48], 0 — entry.owner_entry
    EmitJcc("ne", "mm_set_owner_done");

    // Load old owner scope
    EmitBytes(0x48, 0x8B, 0x48, 0x38); // MOV rcx, [rax+56] — entry.owner_scope
    EmitMovMemReg(-0x20, X86Register.Rcx, 8); // [rbp-32] = old_owner_scope

    // If no owner scope, skip
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_set_owner_done");

    // Compare depths: new_owner_scope.depth >= old_owner_scope.depth -> skip
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = new_owner_scope
    EmitBytes(0x4C, 0x8B, 0x42, 0x30); // MOV r8, [rdx+48] — new_owner_scope.depth
    EmitBytes(0x4C, 0x8B, 0x49, 0x30); // MOV r9, [rcx+48] — old_owner_scope.depth
    EmitBytes(0x4D, 0x39, 0xC8); // CMP r8, r9 — new_depth vs old_depth
    EmitJcc("ge", "mm_set_owner_done");

    // --- Unlink entry from old scope's alloc list ---
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = entry
    EmitBytes(0x48, 0x8B, 0x48, 0x18); // MOV rcx, [rax+24] — entry.prev
    EmitMovMemReg(-0x28, X86Register.Rcx, 8); // [rbp-40] = prev
    EmitBytes(0x48, 0x8B, 0x48, 0x10); // MOV rcx, [rax+16] — entry.next
    EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-48] = next

    // if prev: prev.next = next  else: old_scope.alloc_head = next
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = prev
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_set_owner_unlink_no_prev");
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = next
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — prev.next = next
    EmitJmp("mm_set_owner_unlink_prev_done");
    DefineLabel("mm_set_owner_unlink_no_prev");
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // RAX = old_owner_scope
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = next
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — scope.alloc_head = next
    DefineLabel("mm_set_owner_unlink_prev_done");

    // if next: next.prev = prev  else: old_scope.alloc_tail = prev
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = next
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "mm_set_owner_unlink_no_next");
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = prev
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — next.prev = prev
    EmitJmp("mm_set_owner_unlink_next_done");
    DefineLabel("mm_set_owner_unlink_no_next");
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // RAX = old_owner_scope
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = prev
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — scope.alloc_tail = prev
    DefineLabel("mm_set_owner_unlink_next_done");

    // Decrement old scope.alloc_count
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // RAX = old_owner_scope
    EmitBytes(0x48, 0xFF, 0x48, 0x28); // DEC qword [rax+40] — scope.alloc_count--

    // --- Clear entry's prev/next for clean insertion ---
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = entry
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx — entry.next = NULL
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — entry.prev = NULL

    // --- Link entry into new_owner_scope's alloc list ---
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = new_owner_scope
    EmitBytes(0x48, 0x8B, 0x4A, 0x18); // MOV rcx, [rdx+24] — new_scope.alloc_tail
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_set_owner_link_empty");

    // Non-empty: old_tail.next = entry, entry.prev = old_tail, scope.alloc_tail = entry
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = entry
    EmitBytes(0x48, 0x89, 0x41, 0x10); // MOV [rcx+16], rax — old_tail.next = entry
    EmitBytes(0x48, 0x89, 0x48, 0x18); // MOV [rax+24], rcx — entry.prev = old_tail
    EmitBytes(0x48, 0x89, 0x42, 0x18); // MOV [rdx+24], rax — scope.alloc_tail = entry
    EmitJmp("mm_set_owner_link_done");

    DefineLabel("mm_set_owner_link_empty");
    // Empty: scope.alloc_head = entry, scope.alloc_tail = entry
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = entry
    EmitBytes(0x48, 0x89, 0x42, 0x10); // MOV [rdx+16], rax — scope.alloc_head = entry
    EmitBytes(0x48, 0x89, 0x42, 0x18); // MOV [rdx+24], rax — scope.alloc_tail = entry

    DefineLabel("mm_set_owner_link_done");

    // Increment new scope.alloc_count
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = new_owner_scope
    EmitBytes(0x48, 0xFF, 0x42, 0x28); // INC qword [rdx+40] — scope.alloc_count++

    // Set entry.owner_scope = new_owner_scope
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = entry
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = new_owner_scope
    EmitBytes(0x48, 0x89, 0x50, 0x38); // MOV [rax+56], rdx — entry.owner_scope = new_owner_scope

    DefineLabel("mm_set_owner_done");
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_leak_check() -> void
  // Walks the scope chain and reports any remaining allocations to stderr.
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

    DefineLabel("mm_leak_check_done");
    EmitRuntimeFunctionEnd();
  }

  // Validate a pointer has a valid AllocEntry at [ptr-8]
  // Args: rcx = user_ptr, rdx = tag_cstr
  // If ptr is non-null and [ptr-8] == 0, prints diagnostic and panics
  private void EmitMmValidatePtr() {
    DefineSymdata("__mm_validate_tag", "MM VALIDATE ptr=\0"u8.ToArray());
    DefineSymdata("__mm_validate_backptr", " backptr=\0"u8.ToArray());
    DefineSymdata("__mm_validate_at", " at=\0"u8.ToArray());
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

    // If non-zero, skip (valid)
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_validate_done");

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

  // -------------------------------------------------------------------------
  // mm_trace_scope_enter(scope_ptr_in_rcx) -> void
  // Prints: indent + "scope_enter " + tag + " (depth=N)" + newline
  // -------------------------------------------------------------------------
  private void EmitMmTraceScopeEnter() {
    EmitRuntimeFunctionStart("mm_trace_scope_enter", 1, 0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_scope_enter");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    // Print scope.tag_cstr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = scope ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x20); // MOV rcx, [rax+32] — scope.tag_cstr
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_trace_se_tag_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitJmp("mm_trace_se_tag_done");
    DefineLabel("mm_trace_se_tag_null");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    DefineLabel("mm_trace_se_tag_done");
    // Print " (depth="
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_depth_open");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = scope ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x30); // MOV rcx, [rax+48] — scope.depth
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_close_paren");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_scope_exit(scope_ptr_in_rcx) -> void
  // Prints: indent + "scope_exit " + tag + " (N owned)" + newline
  // -------------------------------------------------------------------------
  private void EmitMmTraceScopeExit() {
    EmitRuntimeFunctionStart("mm_trace_scope_exit", 1, 0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_scope_exit");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    // Print scope.tag_cstr
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = scope ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x20); // MOV rcx, [rax+32] — scope.tag_cstr
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "mm_trace_sx_tag_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitJmp("mm_trace_sx_tag_done");
    DefineLabel("mm_trace_sx_tag_null");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_null");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    DefineLabel("mm_trace_sx_tag_done");
    // Print " ("
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_owned_open");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = scope ptr
    EmitBytes(0x48, 0x8B, 0x48, 0x28); // MOV rcx, [rax+40] — scope.alloc_count
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_owned_suffix");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_alloc(user_ptr_in_rcx) -> void
  // Prints: indent + "alloc " + tag + " rc=N" + newline
  // -------------------------------------------------------------------------
  private void EmitMmTraceAlloc() {
    // Stack: [rbp-8]=user_ptr
    EmitRuntimeFunctionStart("mm_trace_alloc", 1, 0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_alloc");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    // Load entry from [user_ptr - 8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // user_ptr
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    // rc=
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rc_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // user_ptr
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, 0x48); // MOV rcx, [rax+72] — entry.refcount
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_free(user_ptr_in_rcx) -> void
  // Prints: indent + "free " + tag + newline
  // -------------------------------------------------------------------------
  private void EmitMmTraceFree() {
    EmitRuntimeFunctionStart("mm_trace_free", 1, 0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_free");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    // Load entry from [user_ptr - 8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_incref(user_ptr_in_rcx) -> void
  // Prints: indent + "incref " + tag + " rc=N" + newline
  // Called AFTER mm_incref so refcount is already incremented.
  // -------------------------------------------------------------------------
  private void EmitMmTraceIncref() {
    EmitRuntimeFunctionStart("mm_trace_incref", 1, 0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_incref");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rc_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, 0x48); // MOV rcx, [rax+72] — entry.refcount
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_decref(user_ptr_in_rcx) -> void
  // Prints: indent + "decref " + tag + " rc=N" + newline
  // Called AFTER mm_decref so refcount is already decremented.
  // -------------------------------------------------------------------------
  private void EmitMmTraceDecref() {
    EmitRuntimeFunctionStart("mm_trace_decref", 1, 0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_decref");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rc_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitBytes(0x48, 0x8B, 0x48, 0x48); // MOV rcx, [rax+72] — entry.refcount
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  // -------------------------------------------------------------------------
  // mm_trace_move(user_ptr_in_rcx) -> void
  // Prints: indent + "move " + tag + newline
  // Simplified — doesn't show destination since it's complex (scope vs parent).
  // -------------------------------------------------------------------------
  private void EmitMmTraceMove() {
    EmitRuntimeFunctionStart("mm_trace_move", 1, 0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_move");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitSubRegImm(X86Register.Rax, 8);
    EmitBytes(0x48, 0x8B, 0x00); // MOV rax, [rax] — entry
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_entry_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

}
