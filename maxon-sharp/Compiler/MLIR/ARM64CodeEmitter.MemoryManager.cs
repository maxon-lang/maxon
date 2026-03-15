using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class ARM64CodeEmitter {

  // Memory manager header layout:
  // [ptr - 32]: total_alloc_size (8 bytes) — used by mm_free for munmap
  // [ptr - 24]: packed: (alloc_id << 16 | tag_index) (8 bytes)
  // [ptr - 16]: destructor_fn_ptr (8 bytes)
  // [ptr -  8]: refcount (8 bytes)
  // [ptr     ]: user data
  private const int MmHeaderSize = 32;

  public void EmitMemoryManagerFunctions(List<string?>? tagTable) {
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

    // Managed list operations
    EmitManagedListInsertAfter();
    EmitManagedListInsertBefore();
    EmitManagedListUnlink();
    EmitManagedListClear();
    EmitManagedListClearManaged();
    EmitManagedListDecrefValues();

    // Trace functions
    EmitMmTracePrintTag();
    EmitStubFunction("mm_trace_print_hex");
    EmitMmTracePrintI64();
    EmitMmTracePrintPackedTag();
    EmitMmTracePrintIndent();
    EmitMmTraceTransfer();
    EmitMmTagLookup(tagTable);

    // Always needed for alloc_id tracking and trace depth
    DefineGlobal("__mm_alloc_count", 8, 0);
    DefineGlobal("__mm_alloc_id_counter", 8, 0);
    DefineGlobal("__mm_trace_depth", 8, 0);

    if (Compiler.MmTrace) {
      DefineSymdata("__mm_tag_alloc", "alloc \0"u8.ToArray());
      DefineSymdata("__mm_tag_free", "free \0"u8.ToArray());
      DefineSymdata("__mm_tag_incref", "incref \0"u8.ToArray());
      DefineSymdata("__mm_tag_decref", "decref \0"u8.ToArray());
      DefineSymdata("__mm_tag_transfer", "transfer \0"u8.ToArray());
      DefineSymdata("__mm_tag_realloc", "realloc \0"u8.ToArray());
      DefineSymdata("__mm_tag_cow", "cow \0"u8.ToArray());
      DefineSymdata("__mm_scope_list_insert", "managed_list_insert\0"u8.ToArray());
      DefineSymdata("__mm_scope_list_clear", "managed_list_clear\0"u8.ToArray());
      DefineSymdata("__mm_scope_managed_elements", "~ManagedElements\0"u8.ToArray());
      DefineSymdata("__mm_tag_size_eq", " size=\0"u8.ToArray());
      DefineSymdata("__mm_tag_rc_eq", " rc=\0"u8.ToArray());
      DefineSymdata("__mm_tag_hash", " #\0"u8.ToArray());
      DefineSymdata("__mm_tag_lbracket", " [\0"u8.ToArray());
      DefineSymdata("__mm_tag_rbracket", "]\0"u8.ToArray());
      DefineSymdata("__mm_tag_indent", "  \0"u8.ToArray());
      DefineSymdata("__mm_tag_newline", "\n\0"u8.ToArray());
    }

    // Shared strings used by tag lookup default
    DefineSymdata("__mm_tag_null", "?\0"u8.ToArray());

    // Define panic strings in symdata (must match X86 set for all standard IR references)
    DefineSymdata("__mm_panic_decref_null", "mm_decref: null pointer\0"u8.ToArray());
    DefineSymdata("__mm_panic_incref_null", "mm_incref: null pointer\0"u8.ToArray());
    DefineSymdata("__mm_panic_decref_underflow", "mm_decref: refcount underflow\0"u8.ToArray());
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
  }

  // --- mm_alloc(size, destructor, tag, scope) -> ptr ---
  // Stack layout:
  //   [x29+16] = arg0 (X0) = size
  //   [x29+24] = arg1 (X1) = destructor
  //   [x29+32] = arg2 (X2) = tag_index
  //   [x29+40] = arg3 (X3) = scope_cstr
  //   [x29+48] = total_alloc_size
  //   [x29+56] = raw_ptr / user_ptr (reused)
  private void EmitMmAlloc() {
    EmitRuntimeFunctionStart("mm_alloc", 4, 0x50);
    // Allocate size + MmHeaderSize via mmap
    EmitReloadArg(0); // size
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, MmHeaderSize, isAdd: true);
    // Save total size [x29, #48]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8);

    // mmap(NULL, totalSize, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0)
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // size
    EmitMovRegImm(ARM64Register.X0, 0); // addr
    EmitMovRegImm(ARM64Register.X2, 3); // PROT_READ | PROT_WRITE
    EmitMovRegImm(ARM64Register.X3, 0x1002); // MAP_ANON | MAP_PRIVATE
    EmitMovRegImm(ARM64Register.X4, -1);
    EmitMovRegImm(ARM64Register.X5, 0);
    EmitCallImport("mmap"); // mmap

    // X0 = raw ptr, save it to [x29, #56]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8);

    // Store total_alloc_size at [raw + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 48, 8); // reload total size
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    // Increment __mm_alloc_count
    EmitGlobalLoadReg(ARM64Register.X1, "__mm_alloc_count");
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
    EmitGlobalStoreReg(ARM64Register.X1, "__mm_alloc_count");

    // Increment __mm_alloc_id_counter
    EmitGlobalLoadReg(ARM64Register.X1, "__mm_alloc_id_counter");
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
    EmitGlobalStoreReg(ARM64Register.X1, "__mm_alloc_id_counter");

    // Pack (alloc_id << 16 | tag_index) and store at [raw + 8]
    // Reload raw_ptr and build packed value
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // X0 = raw_ptr
    EmitGlobalLoadReg(ARM64Register.X1, "__mm_alloc_id_counter"); // X1 = alloc_id
    EmitReloadArg(2); // X2 = tag_index
    // ORR X1, X2, X1, LSL #16 — packed = (alloc_id << 16) | tag_index
    // Encoding: sf=1, opc=01, shift=00(LSL), N=0, Rm=X1(1), imm6=010000(16), Rn=X2(2), Rd=X1(1)
    EmitWord(0xAA014041);
    // Store packed value at [raw + 8]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 8, 8);

    // Store destructor at [raw + 16]
    EmitReloadArg(1); // destructor -> X1
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 16, 8);
    // Store refcount = 0 at [raw + 24]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X0, 24, 8);
    // Compute user ptr = raw + MmHeaderSize
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, MmHeaderSize, isAdd: true);

    // Trace alloc after user_ptr is computed (in X0)
    if (Compiler.MmTrace) {
      // Save user_ptr to [x29, #56] (reuse raw_ptr slot)
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8);
      EmitInlineTrace("__mm_tag_alloc", "mm_alloc_trace", ptrSlot: 56, scopeSlot: 40, sizeSlot: 16);
      // Restore user_ptr to X0 for return
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    }

    EmitRuntimeFunctionEnd();
  }

  // --- mm_realloc(ptr, old_size, new_size) -> new_ptr ---
  private void EmitMmRealloc() {
    EmitRuntimeFunctionStart("mm_realloc", 3, Compiler.MmTrace ? 0x60 : 0x50);
    // Save new_size for trace before it gets overwritten
    EmitReloadArg(2); // new_size
    if (Compiler.MmTrace) {
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // [x29+48] = new_size
    }
    // Compute new total alloc size = new_size + MmHeaderSize
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, MmHeaderSize, isAdd: true);
    // Save new total alloc size [x29, #40]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    // Allocate new block via mmap
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitMovRegImm(ARM64Register.X2, 3);
    EmitMovRegImm(ARM64Register.X3, 0x1002);
    EmitMovRegImm(ARM64Register.X4, -1);
    EmitMovRegImm(ARM64Register.X5, 0);
    EmitCallImport("mmap");
    // Save new raw ptr [x29, #32]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Copy header + data from old to new
    // old raw = ptr - MmHeaderSize
    EmitReloadArg(0);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X0, MmHeaderSize, isAdd: false); // old raw
    EmitReloadArg(1); // old_size
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X0, MmHeaderSize, isAdd: true); // old_size + header
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // new raw
    EmitBranchLink("maxon_memcpy");

    // Update total_alloc_size in new block header [raw + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // new raw
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 40, 8); // new total size
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8); // [raw+0] = new total size

    // Unmap old: munmap(old_raw, old_size + header)
    EmitReloadArg(0);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, MmHeaderSize, isAdd: false);
    EmitReloadArg(1);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, MmHeaderSize, isAdd: true);
    EmitCallImport("munmap"); // munmap

    // Return new user ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, MmHeaderSize, isAdd: true);

    if (Compiler.MmTrace) {
      // Save new user ptr for trace, use scope=NULL
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8); // [x29+56] = new_user_ptr
      EmitMovRegImm(ARM64Register.X1, 0);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 64, 8); // [x29+64] = scope=NULL
      // ptrSlot=56 (user_ptr), scopeSlot=64, sizeSlot=48 (saved new_size)
      EmitInlineTrace("__mm_tag_realloc", "mm_realloc_trace", ptrSlot: 56, scopeSlot: 64, sizeSlot: 48);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // restore user ptr
    }

    EmitRuntimeFunctionEnd();
  }

  // --- mm_free(ptr, scope) ---
  private void EmitMmFree() {
    EmitRuntimeFunctionStart("mm_free", 2, 0x40);
    EmitReloadArg(0);

    // Check null
    var notNullLabel = $"__mm_free_notnull_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, notNullLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, notNull
    EmitRuntimeFunctionEnd();

    DefineLabel(notNullLabel);

    // Load destructor: [ptr - 16]
    EmitReloadArg(0);
    var noDestructorLabel = $"__mm_free_nodestr_{_uniqueLabelCounter++}";
    // LDUR X1, [X0, #-16]
    EmitWord(0xF85F0001 | (Reg(ARM64Register.X0) << 5));
    _condBranchFixups.Add((_code.Count, noDestructorLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X1)); // CBZ X1, noDestructor
    // Call destructor(ptr)
    EmitReloadArg(0);
    EmitWord(0xD63F0000 | (Reg(ARM64Register.X1) << 5)); // BLR X1

    DefineLabel(noDestructorLabel);

    // Trace free AFTER destructor (so destructor's trace output appears first)
    if (Compiler.MmTrace) {
      EmitInlineTraceFree("mm_free_trace", ptrSlot: 16, scopeSlot: 24);
    }

    // Decrement __mm_alloc_count
    EmitGlobalLoadReg(ARM64Register.X1, "__mm_alloc_count");
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: false);
    EmitGlobalStoreReg(ARM64Register.X1, "__mm_alloc_count");
    // munmap(raw_ptr, total_alloc_size)
    // Load total_alloc_size from [ptr - 32]
    EmitReloadArg(0); // X0 = user_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, -32, 8); // X1 = total_alloc_size
    // raw_ptr = user_ptr - MmHeaderSize
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, MmHeaderSize, isAdd: false); // X0 = raw_ptr
    EmitCallImport("munmap"); // munmap(raw_ptr, total_alloc_size)
    EmitRuntimeFunctionEnd();
  }

  // --- mm_incref(ptr, scope) ---
  private void EmitMmIncref() {
    EmitRuntimeFunctionStart("mm_incref", 2, Compiler.MmTrace ? 0x40 : 0x30);
    EmitReloadArg(0);
    // Check null
    var notNullLabel = $"__mm_incref_notnull_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, notNullLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, notNull
    EmitRuntimeFunctionEnd();

    DefineLabel(notNullLabel);
    // refcount = [ptr - 8]
    // LDUR X1, [X0, #-8]
    EmitWord(0xF85F8001 | (Reg(ARM64Register.X0) << 5));
    // ADD X1, X1, #1
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
    // STUR X1, [X0, #-8]
    EmitWord(0xF81F8001 | (Reg(ARM64Register.X0) << 5));

    // Trace incref after increment
    if (Compiler.MmTrace) {
      EmitInlineTrace("__mm_tag_incref", "mm_incref_trace", ptrSlot: 16, scopeSlot: 24);
    }

    EmitRuntimeFunctionEnd();
  }

  // --- mm_decref(ptr, scope) ---
  private void EmitMmDecref() {
    EmitRuntimeFunctionStart("mm_decref", 2, Compiler.MmTrace ? 0x40 : 0x30);
    EmitReloadArg(0);
    var notNullLabel = $"__mm_decref_notnull_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, notNullLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, notNull
    EmitRuntimeFunctionEnd();

    DefineLabel(notNullLabel);

    // Trace decref before modifying refcount (prints rc-1)
    if (Compiler.MmTrace) {
      EmitInlineTrace("__mm_tag_decref", "mm_decref_trace", ptrSlot: 16, scopeSlot: 24, rcSubtract: 1);
      EmitReloadArg(0); // reload X0 = user_ptr
    }

    // Load refcount
    // LDUR X1, [X0, #-8]
    EmitWord(0xF85F8001 | (Reg(ARM64Register.X0) << 5));

    // Decrement refcount
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: false);
    // STUR X1, [X0, #-8]
    EmitWord(0xF81F8001 | (Reg(ARM64Register.X0) << 5));

    // If decremented refcount == 0, free the object
    var freeLabel = $"__mm_decref_free_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    _condBranchFixups.Add((_code.Count, freeLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X1)); // CBZ X1, free (rc is now 0)
    EmitRuntimeFunctionEnd();

    DefineLabel(freeLabel);
    // Call mm_free(ptr, scope=NULL) — NULL scope signals internal free from decref
    EmitReloadArg(0);
    EmitMovRegImm(ARM64Register.X1, 0); // scope = NULL (triggers extra indent in trace)
    EmitBranchLink("mm_free");
    EmitRuntimeFunctionEnd();
  }

  // --- mm_decref_managed_elements(list_ptr) ---
  private void EmitMmDecrefManagedElements() {
    // Iterate managed list elements and decref each
    // list: [ptr+0]=buf, [ptr+8]=len
    EmitRuntimeFunctionStart("mm_decref_managed_elements", 1, 0x40);
    EmitReloadArg(0);
    // Load buf and len
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 0, 8); // buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X0, 8, 8); // len
    // Save buf [x29, #32], len [x29, #40], idx=0 [x29, #48]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8);
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 48, 8);

    var loopLabel = $"__decref_elems_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__decref_elems_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    DefineLabel(loopLabel);
    // if idx >= len, done
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 40, 8);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X3
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs)); // B.HS done

    // Load element: buf[idx*8] using register offset with LSL #3
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = buf
    // LDR X0, [X2, X4, LSL #3] — barrel shifter computes buf + idx*8
    EmitWord(0xF8647840 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X0));

    // mm_decref(element, scope)
    if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__mm_scope_managed_elements");
    else EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_decref");

    // idx++
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 48, 8);
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 48, 8);
    EmitBranch(loopLabel);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- mm_incref_managed_elements(managed_ptr) ---
  // Increfs every element pointer in a __ManagedMemory buffer.
  // Layout: [+0]=buf, [+8]=len
  private void EmitMmIncrefManagedElements() {
    EmitRuntimeFunctionStart("mm_incref_managed_elements", 1, 0x40);
    EmitReloadArg(0);
    // Load buf and len
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 0, 8); // buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X0, 8, 8); // len
    // Save buf [x29, #32], len [x29, #40], idx=0 [x29, #48]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8);
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 48, 8);

    var loopLabel = $"__incref_elems_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__incref_elems_done_{_uniqueLabelCounter}";
    var skipLabel = $"__incref_elems_skip_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    DefineLabel(loopLabel);
    // if idx >= len, done
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 40, 8);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X3
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs)); // B.HS done

    // Load element: buf[idx*8]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = buf
    EmitWord(0xF8647840 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X0));

    // Null guard: skip null elements
    _condBranchFixups.Add((_code.Count, skipLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, skip

    // mm_incref(element, scope=NULL)
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_incref");

    DefineLabel(skipLabel);
    // idx++
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 48, 8);
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 48, 8);
    EmitBranch(loopLabel);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- mm_clear_managed_elements(managed_ptr) ---
  // Decrefs every element pointer and zeroes each slot.
  // Layout: [+0]=buf, [+8]=len
  private void EmitMmClearManagedElements() {
    EmitRuntimeFunctionStart("mm_clear_managed_elements", 1, 0x40);
    EmitReloadArg(0);
    // Load buf and len
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 0, 8); // buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X0, 8, 8); // len
    // Save buf [x29, #32], len [x29, #40], idx=0 [x29, #48]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8);
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 48, 8);

    var loopLabel = $"__clear_elems_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__clear_elems_done_{_uniqueLabelCounter}";
    var zeroLabel = $"__clear_elems_zero_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    DefineLabel(loopLabel);
    // if idx >= len, done
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 40, 8);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X3
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs)); // B.HS done

    // Load element: buf[idx*8]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = buf
    EmitWord(0xF8647840 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X0));

    // Null guard: skip decref for null elements
    _condBranchFixups.Add((_code.Count, zeroLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, zero

    // mm_decref(element, scope)
    if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__mm_scope_managed_elements");
    else EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_decref");

    DefineLabel(zeroLabel);
    // Zero the slot: buf[idx*8] = 0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 48, 8); // idx
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // buf
    // STR XZR, [X2, X4, LSL #3]
    EmitWord(0xF8247840 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.Xzr));

    // idx++
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 48, 8);
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 48, 8);
    EmitBranch(loopLabel);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- mm_leak_check() ---
  private void EmitMmLeakCheck() {
    // Stub: no-op for now
    DefineLabel("mm_leak_check");
    EmitWord(0xD65F03C0); // RET
  }

  // --- mm_validate_ptr(ptr) ---
  private void EmitMmValidatePtr() {
    DefineLabel("mm_validate_ptr");
    EmitWord(0xD65F03C0); // RET
  }

  // =========================================================================
  // Managed List operations
  // =========================================================================
  // ManagedListNode layout: [+0]=next, [+8]=prev, [+16]=list, [+24]=value
  // ManagedList layout: [+0]=head, [+8]=tail, [+16]=count

  // --- maxon_managed_list_insert_first(list_ptr, node_ptr) ---
  private void EmitManagedListInsertFirst() {
    // Args: X0=list_ptr [x29,#16], X1=node_ptr [x29,#24]
    EmitRuntimeFunctionStart("maxon_managed_list_insert_first", 2, 0x40);

    // Auto-detach: if node.list != 0, unlink from old list and decref
    EmitReloadArg(1); // X1 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X1, 16, 8); // X2 = node.list
    var noDetachLabel = $"__mli_first_no_detach_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, noDetachLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, no_detach
    // Unlink from old list: unlink(old_list, node)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2); // X0 = old list
    EmitBranchLink("maxon_managed_list_unlink");
    // Decref node (old list releases reference)
    EmitReloadArg(1); // X0 = node_ptr (reload into X1, move to X0)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitMovRegImm(ARM64Register.X1, 0); // scope=NULL
    EmitBranchLink("mm_decref");
    DefineLabel(noDetachLabel);

    // old_head = [list+0]
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 0, 8); // X2 = old_head
    // Save old_head to [x29, #32]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8);

    // node.next = old_head
    EmitReloadArg(1); // X1 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X1, 0, 8); // [node+0] = old_head

    // node.prev = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X1, 8, 8); // [node+8] = 0

    // node.list = list_ptr
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X1, 16, 8); // [node+16] = list_ptr

    // if old_head != 0: old_head.prev = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = old_head
    var noOldHeadLabel = $"__mli_first_no_old_head_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, noOldHeadLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, no_old_head
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X2, 8, 8); // old_head.prev = node_ptr
    DefineLabel(noOldHeadLabel);

    // list.head = node_ptr
    EmitReloadArg(0); // X0 = list_ptr
    EmitReloadArg(1); // X1 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8); // [list+0] = node_ptr

    // if list.tail == 0: list.tail = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 8, 8); // X2 = list.tail
    var tailOkLabel = $"__mli_first_tail_ok_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, tailOkLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X2)); // CBNZ X2, tail_ok
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 8, 8); // list.tail = node_ptr
    DefineLabel(tailOkLabel);

    // list.count++
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 16, 8); // X2 = count
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 16, 8); // [list+16] = count+1

    // Incref node (list holds counted reference)
    EmitReloadArg(1); // X1 = node_ptr
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__mm_scope_list_insert");
    else EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_incref");

    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_list_insert_last(list_ptr, node_ptr) ---
  private void EmitManagedListInsertLast() {
    // Args: X0=list_ptr [x29,#16], X1=node_ptr [x29,#24]
    EmitRuntimeFunctionStart("maxon_managed_list_insert_last", 2, 0x40);

    // Auto-detach
    EmitReloadArg(1); // X1 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X1, 16, 8); // X2 = node.list
    var noDetachLabel = $"__mli_last_no_detach_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, noDetachLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, no_detach
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2);
    EmitBranchLink("maxon_managed_list_unlink");
    EmitReloadArg(1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitMovRegImm(ARM64Register.X1, 0); // scope=NULL
    EmitBranchLink("mm_decref");
    DefineLabel(noDetachLabel);

    // old_tail = [list+8]
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 8, 8); // X2 = old_tail
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8); // save old_tail

    // node.next = 0
    EmitReloadArg(1); // X1 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X1, 0, 8); // [node+0] = 0

    // node.prev = old_tail
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X1, 8, 8); // [node+8] = old_tail

    // node.list = list_ptr
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X1, 16, 8); // [node+16] = list_ptr

    // if old_tail != 0: old_tail.next = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = old_tail
    var noOldTailLabel = $"__mli_last_no_old_tail_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, noOldTailLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, no_old_tail
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X2, 0, 8); // old_tail.next = node_ptr
    DefineLabel(noOldTailLabel);

    // list.tail = node_ptr
    EmitReloadArg(0); // X0 = list_ptr
    EmitReloadArg(1); // X1 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 8, 8); // list.tail = node_ptr

    // if list.head == 0: list.head = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 0, 8); // X2 = list.head
    var headOkLabel = $"__mli_last_head_ok_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, headOkLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X2)); // CBNZ X2, head_ok
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8); // list.head = node_ptr
    DefineLabel(headOkLabel);

    // list.count++
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 16, 8);
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 16, 8);

    // Incref node
    EmitReloadArg(1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__mm_scope_list_insert");
    else EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_incref");

    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_list_insert_after(list_ptr, target_ptr, node_ptr) ---
  private void EmitManagedListInsertAfter() {
    // Args: X0=list_ptr [x29,#16], X1=target_ptr [x29,#24], X2=node_ptr [x29,#32]
    EmitRuntimeFunctionStart("maxon_managed_list_insert_after", 3, 0x50);

    // Auto-detach
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X2, 16, 8); // X3 = node.list
    var noDetachLabel = $"__mli_after_no_detach_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, noDetachLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X3)); // CBZ X3, no_detach
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X3); // X0 = old list
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X2); // X1 = node
    EmitBranchLink("maxon_managed_list_unlink");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // X0 = node_ptr
    EmitMovRegImm(ARM64Register.X1, 0); // scope=NULL
    EmitBranchLink("mm_decref");
    DefineLabel(noDetachLabel);

    // after = target.next
    EmitReloadArg(1); // X1 = target_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X1, 0, 8); // X3 = target.next (after)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8); // save after

    // node.next = after
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X2, 0, 8); // node.next = after

    // node.prev = target_ptr
    EmitReloadArg(1); // X1 = target_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X2, 8, 8); // node.prev = target

    // node.list = list_ptr
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X2, 16, 8); // node.list = list

    // target.next = node_ptr
    EmitReloadArg(1); // X1 = target_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X1, 0, 8); // target.next = node

    // if after != 0: after.prev = node_ptr; else: list.tail = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 40, 8); // X3 = after
    var wasTailLabel = $"__mli_after_was_tail_{_uniqueLabelCounter}";
    var linkedLabel = $"__mli_after_linked_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    _condBranchFixups.Add((_code.Count, wasTailLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X3)); // CBZ X3, was_tail
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X3, 8, 8); // after.prev = node
    EmitBranch(linkedLabel);
    DefineLabel(wasTailLabel);
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 8, 8); // list.tail = node
    DefineLabel(linkedLabel);

    // list.count++
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X0, 16, 8);
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X0, 16, 8);

    // Incref node
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__mm_scope_list_insert");
    else EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_incref");

    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_list_insert_before(list_ptr, target_ptr, node_ptr) ---
  private void EmitManagedListInsertBefore() {
    // Args: X0=list_ptr [x29,#16], X1=target_ptr [x29,#24], X2=node_ptr [x29,#32]
    EmitRuntimeFunctionStart("maxon_managed_list_insert_before", 3, 0x50);

    // Auto-detach
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X2, 16, 8); // X3 = node.list
    var noDetachLabel = $"__mli_before_no_detach_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, noDetachLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X3)); // CBZ X3, no_detach
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X3);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X2);
    EmitBranchLink("maxon_managed_list_unlink");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitMovRegImm(ARM64Register.X1, 0); // scope=NULL
    EmitBranchLink("mm_decref");
    DefineLabel(noDetachLabel);

    // before = target.prev
    EmitReloadArg(1); // X1 = target_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X1, 8, 8); // X3 = target.prev (before)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8); // save before

    // node.next = target_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = node_ptr
    EmitReloadArg(1); // X1 = target_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X2, 0, 8); // node.next = target

    // node.prev = before
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 40, 8); // X3 = before
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X2, 8, 8); // node.prev = before

    // node.list = list_ptr
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X2, 16, 8); // node.list = list

    // target.prev = node_ptr
    EmitReloadArg(1); // X1 = target_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X1, 8, 8); // target.prev = node

    // if before != 0: before.next = node_ptr; else: list.head = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 40, 8); // X3 = before
    var wasHeadLabel = $"__mli_before_was_head_{_uniqueLabelCounter}";
    var linkedLabel = $"__mli_before_linked_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    _condBranchFixups.Add((_code.Count, wasHeadLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X3)); // CBZ X3, was_head
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X3, 0, 8); // before.next = node
    EmitBranch(linkedLabel);
    DefineLabel(wasHeadLabel);
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 0, 8); // list.head = node
    DefineLabel(linkedLabel);

    // list.count++
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X0, 16, 8);
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X0, 16, 8);

    // Incref node
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__mm_scope_list_insert");
    else EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_incref");

    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_list_unlink(list_ptr, node_ptr) ---
  private void EmitManagedListUnlink() {
    // Args: X0=list_ptr [x29,#16], X1=node_ptr [x29,#24]
    EmitRuntimeFunctionStart("maxon_managed_list_unlink", 2, 0x40);

    // If node.list == 0, no-op
    EmitReloadArg(1); // X1 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X1, 16, 8); // X2 = node.list
    var doneLabel = $"__mlu_done_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, done

    // prev = [node+8], next = [node+0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X1, 8, 8); // X2 = prev
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X1, 0, 8); // X3 = next
    // Save prev [x29,#32], next [x29,#40]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8);

    // if prev != 0: prev.next = next; else: list.head = next
    var noPrevLabel = $"__mlu_no_prev_{_uniqueLabelCounter}";
    var prevDoneLabel = $"__mlu_prev_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    _condBranchFixups.Add((_code.Count, noPrevLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, no_prev
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X2, 0, 8); // prev.next = next
    EmitBranch(prevDoneLabel);
    DefineLabel(noPrevLabel);
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X0, 0, 8); // list.head = next
    DefineLabel(prevDoneLabel);

    // if next != 0: next.prev = prev; else: list.tail = prev
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 40, 8); // X3 = next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = prev
    var noNextLabel = $"__mlu_no_next_{_uniqueLabelCounter}";
    var nextDoneLabel = $"__mlu_next_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    _condBranchFixups.Add((_code.Count, noNextLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X3)); // CBZ X3, no_next
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X3, 8, 8); // next.prev = prev
    EmitBranch(nextDoneLabel);
    DefineLabel(noNextLabel);
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 8, 8); // list.tail = prev
    DefineLabel(nextDoneLabel);

    // Clear node links: next=0, prev=0, list=0
    EmitReloadArg(1); // X1 = node_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X1, 0, 8); // node.next = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X1, 8, 8); // node.prev = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X1, 16, 8); // node.list = 0

    // list.count--
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 16, 8);
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 16, 8);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_list_clear(list_ptr) ---
  private void EmitManagedListClear() => EmitManagedListClearImpl("maxon_managed_list_clear", managed: false);

  // --- maxon_managed_list_clear_managed(list_ptr) ---
  private void EmitManagedListClearManaged() => EmitManagedListClearImpl("maxon_managed_list_clear_managed", managed: true);

  // Shared clear implementation
  private void EmitManagedListClearImpl(string funcName, bool managed) {
    // Args: X0=list_ptr [x29,#16]
    // Extra stack: [x29,#24]=current, [x29,#32]=next
    EmitRuntimeFunctionStart(funcName, 1, 0x50);

    // current = list.head
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8); // X1 = list.head
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // save current

    var loopLabel = $"__{funcName}_loop_{_uniqueLabelCounter}";
    var loopDoneLabel = $"__{funcName}_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    DefineLabel(loopLabel);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // X0 = current
    _condBranchFixups.Add((_code.Count, loopDoneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, done

    // Save next before modifying
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8); // X1 = current.next
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8); // save next

    // Zero node links
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X0, 0, 8); // next = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X0, 8, 8); // prev = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X0, 16, 8); // list = 0

    if (managed) {
      // Decref the heap value at [node+24]
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // X0 = current
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 24, 8); // X0 = node.value
      if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__mm_scope_list_clear");
      else EmitMovRegImm(ARM64Register.X1, 0);
      EmitBranchLink("mm_decref");
    }

    // Decref/free the node itself
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // X0 = current
    if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__mm_scope_list_clear");
    else EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_decref");

    // current = next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitBranch(loopLabel);

    DefineLabel(loopDoneLabel);

    // Zero list metadata
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X0, 0, 8); // head = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X0, 8, 8); // tail = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X0, 16, 8); // count = 0

    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_list_decref_values(list_ptr) ---
  private void EmitManagedListDecrefValues() {
    // Args: X0=list_ptr [x29,#16]
    // Extra stack: [x29,#24]=current, [x29,#32]=next
    EmitRuntimeFunctionStart("maxon_managed_list_decref_values", 1, 0x50);

    // current = list.head
    EmitReloadArg(0); // X0 = list_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8);

    var loopLabel = $"__mldv_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__mldv_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    DefineLabel(loopLabel);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // X0 = current
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, done

    // Save next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8); // X1 = current.next
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8);

    // Decref value at [node+24]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 24, 8); // X0 = node.value
    EmitMovRegImm(ARM64Register.X1, 0); // scope=NULL
    EmitBranchLink("mm_decref");

    // current = next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitBranch(loopLabel);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mm_trace_print_tag(cstr_ptr_x0): Print a null-terminated string to stderr.
  /// </summary>
  private void EmitMmTracePrintTag() {
    EmitRuntimeFunctionStart("mm_trace_print_tag", 1, 0x30);
    EmitReloadArg(0); // X0 = cstr_ptr
    EmitBranchLink("rt_write_cstr_stderr");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mm_trace_print_i64(value_x0): Print a 64-bit integer in decimal to stderr.
  /// Uses a 24-byte buffer on the stack.
  /// </summary>
  private void EmitMmTracePrintI64() {
    EmitRuntimeFunctionStart("mm_trace_print_i64", 1, 0x40);
    // [x29+16] = value (arg 0)
    // We'll build the decimal string right-to-left in a stack buffer [x29+24..x29+47] (24 bytes)

    EmitReloadArg(0); // X0 = value

    // If value == 0, print "0"
    EmitCbnz(ARM64Register.X0, "mm_trace_i64_nonzero");
    // Store "0\0" at [x29+24]
    EmitMovRegImm(ARM64Register.X1, 0x30); // '0'
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X1, ARM64Register.X29, 24, 1); // STRB
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X1, ARM64Register.X29, 25, 1); // STRB null
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 24, isAdd: true);
    EmitBranchLink("rt_write_cstr_stderr");
    EmitBranch("mm_trace_i64_done");

    DefineLabel("mm_trace_i64_nonzero");
    // X0 = value, X9 = pointer to end of buffer (null terminator position)
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X29, 47, isAdd: true); // end of 24-byte buf
    // Null-terminate
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X1, ARM64Register.X9, 0, 1);

    EmitMovRegImm(ARM64Register.X2, 10); // divisor

    DefineLabel("mm_trace_i64_loop");
    // UDIV X3, X0, X2 (quotient)
    EmitWord(0x9AC20803); // UDIV X3, X0, X2
    // MSUB X4, X3, X2, X0 → X4 = X0 - X3*X2 (remainder)
    EmitWord(0x9B028064); // MSUB X4, X3, X2, X0
    // Convert to ASCII: X4 = X4 + '0'
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 0x30, isAdd: true);
    // Decrement pointer
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X9, 1, isAdd: false);
    // Store digit: STRB W4, [X9]
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X4, ARM64Register.X9, 0, 1);
    // quotient → value
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X3);
    // Loop if value != 0
    EmitCbnz(ARM64Register.X0, "mm_trace_i64_loop");

    // Print: X0 = pointer to first digit (X9)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitBranchLink("rt_write_cstr_stderr");

    DefineLabel("mm_trace_i64_done");
    EmitRuntimeFunctionEnd();
  }

  // =========================================================================
  // Trace runtime functions
  // =========================================================================

  /// <summary>
  /// mm_trace_print_packed_tag(user_ptr in X0): Extract tag_index from [ptr-24],
  /// look up tag string, and print it.
  /// </summary>
  private void EmitMmTracePrintPackedTag() {
    if (!Compiler.MmTrace) {
      EmitStubFunction("mm_trace_print_packed_tag");
      return;
    }
    EmitRuntimeFunctionStart("mm_trace_print_packed_tag", 1, 0x30);
    // [x29+16] = user_ptr
    EmitReloadArg(0); // X0 = user_ptr
    // LDUR X0, [X0, #-24] -- load packed value
    EmitWord(0xF85E8000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    // AND X0, X0, #0xFFFF -- extract tag_index
    EmitMovRegImm(ARM64Register.X1, 0xFFFF);
    // AND X0, X0, X1
    EmitWord(0x8A010000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    // Call mm_tag_lookup(tag_index) -> X0 = cstr
    EmitBranchLink("mm_tag_lookup");
    // Call mm_trace_print_tag(cstr)
    EmitBranchLink("mm_trace_print_tag");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mm_trace_print_indent(): Print 2 spaces for each level of __mm_trace_depth.
  /// </summary>
  private void EmitMmTracePrintIndent() {
    if (!Compiler.MmTrace) {
      EmitStubFunction("mm_trace_print_indent");
      return;
    }
    EmitRuntimeFunctionStart("mm_trace_print_indent", 0, 0x30);
    // Load __mm_trace_depth into X0
    EmitGlobalLoadReg(ARM64Register.X0, "__mm_trace_depth");
    // Store as loop counter at [x29+16]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8);

    DefineLabel("mm_trace_indent_loop");
    // Load counter
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    // If zero, done
    var doneLabel = "mm_trace_indent_done";
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, done
    // Print "  "
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_indent");
    EmitBranchLink("mm_trace_print_tag");
    // Decrement counter
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: false);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitBranch("mm_trace_indent_loop");

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mm_trace_transfer(ptr in X0, scope in X1): Print transfer trace line.
  /// </summary>
  private void EmitMmTraceTransfer() {
    if (!Compiler.MmTrace) {
      EmitStubFunction("mm_trace_transfer");
      return;
    }
    EmitRuntimeFunctionStart("mm_trace_transfer", 2, 0x30);
    // [x29+16]=ptr, [x29+24]=scope
    EmitReloadArg(0); // X0 = ptr
    // If null, return
    var nullLabel = "mm_trace_transfer_null";
    _condBranchFixups.Add((_code.Count, nullLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, null
    // Print indent
    EmitBranchLink("mm_trace_print_indent");
    // Print "transfer "
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_transfer");
    EmitBranchLink("mm_trace_print_tag");
    // Print TagAndId
    EmitTraceTagAndId(16);
    // Print Rc
    EmitTraceRc(16);
    // Print ScopeAndNewline
    EmitMmTraceScopeAndNewline("mm_trace_transfer_no_scope", 24);
    DefineLabel(nullLabel);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mm_tag_lookup(tag_index in X0) -> cstr in X0
  /// Loop through tag table entries. Return matching tag string or default.
  /// </summary>
  private void EmitMmTagLookup(List<string?>? tagTable) {
    EmitRuntimeFunctionStart("mm_tag_lookup", 1, 0x30);
    // [x29+16] = tag_index
    if (tagTable != null) {
      for (int i = 1; i < tagTable.Count; i++) {
        var label = tagTable[i];
        if (label == null) continue;
        EmitReloadArg(0); // X0 = tag_index
        EmitMovRegImm(ARM64Register.X1, i);
        // CMP X0, X1
        EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5));
        var skipLabel = $"mm_tag_lookup_skip_{_uniqueLabelCounter++}";
        _condBranchFixups.Add((_code.Count, skipLabel));
        EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE skip
        // Match: return tag string address (label is a symdata key defined during MLIR lowering)
        EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, label);
        EmitRuntimeFunctionEnd();
        DefineLabel(skipLabel);
      }
    }
    // Default: return "?"
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_null");
    EmitRuntimeFunctionEnd();
  }

  // =========================================================================
  // Inline trace helper methods (emit ARM64 code inline, not standalone functions)
  // =========================================================================

  /// <summary>
  /// Print "TypeName " from packed tag at [x29+ptrSlot], then " #N" where N = alloc_id.
  /// </summary>
  private void EmitTraceTagAndId(int ptrSlot) {
    // Call mm_trace_print_packed_tag(ptr)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, ptrSlot, 8);
    EmitBranchLink("mm_trace_print_packed_tag");
    // Print " #"
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_hash");
    EmitBranchLink("mm_trace_print_tag");
    // Print alloc_id = [ptr-24] >> 16
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, ptrSlot, 8);
    // LDUR X0, [X0, #-24]
    EmitWord(0xF85E8000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    // LSR X0, X0, #16 = UBFM X0, X0, #16, #63
    EmitWord(0xD350FC00 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    EmitBranchLink("mm_trace_print_i64");
  }

  /// <summary>
  /// Print " rc=N" from user_ptr at [x29+ptrSlot]. rcSubtract adjusts displayed value.
  /// </summary>
  private void EmitTraceRc(int ptrSlot, int rcSubtract = 0) {
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_rc_eq");
    EmitBranchLink("mm_trace_print_tag");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, ptrSlot, 8);
    // LDUR X0, [X0, #-8] (refcount)
    EmitWord(0xF85F8000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    if (rcSubtract > 0) EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, rcSubtract, isAdd: false);
    EmitBranchLink("mm_trace_print_i64");
  }

  /// <summary>
  /// Print " size=N" from size value at [x29+sizeSlot].
  /// </summary>
  private void EmitTraceSize(int sizeSlot) {
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_size_eq");
    EmitBranchLink("mm_trace_print_tag");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, sizeSlot, 8);
    EmitBranchLink("mm_trace_print_i64");
  }

  /// <summary>
  /// Print " [scope]" if scope is non-null, then print newline.
  /// </summary>
  private void EmitMmTraceScopeAndNewline(string skipLabel, int scopeSlot) {
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, scopeSlot, 8);
    _condBranchFixups.Add((_code.Count, skipLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, skip
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_lbracket");
    EmitBranchLink("mm_trace_print_tag");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, scopeSlot, 8);
    EmitBranchLink("mm_trace_print_tag");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_rbracket");
    EmitBranchLink("mm_trace_print_tag");
    DefineLabel(skipLabel);
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_newline");
    EmitBranchLink("mm_trace_print_tag");
  }

  /// <summary>
  /// Emit inline trace: indent + tag + "TypeName #N rc=R [scope]\n".
  /// </summary>
  private void EmitInlineTrace(string tagLabel, string uniquePrefix, int ptrSlot, int scopeSlot,
      bool printRc = true, int rcSubtract = 0, int? sizeSlot = null) {
    EmitBranchLink("mm_trace_print_indent");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, tagLabel);
    EmitBranchLink("mm_trace_print_tag");
    EmitTraceTagAndId(ptrSlot);
    if (printRc) EmitTraceRc(ptrSlot, rcSubtract);
    if (sizeSlot.HasValue) EmitTraceSize(sizeSlot.Value);
    EmitMmTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  /// <summary>
  /// Emit inline free trace: indent (+ extra indent if scope is NULL) + "free " + tag + " #N [scope]\n".
  /// </summary>
  private void EmitInlineTraceFree(string uniquePrefix, int ptrSlot, int scopeSlot) {
    EmitBranchLink("mm_trace_print_indent");
    // Extra indent when scope is NULL (internal free from mm_decref)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, scopeSlot, 8);
    _condBranchFixups.Add((_code.Count, $"{uniquePrefix}_no_extra_indent"));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, skip_extra
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_indent");
    EmitBranchLink("mm_trace_print_tag");
    DefineLabel($"{uniquePrefix}_no_extra_indent");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_free");
    EmitBranchLink("mm_trace_print_tag");
    EmitTraceTagAndId(ptrSlot);
    EmitMmTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }
}
