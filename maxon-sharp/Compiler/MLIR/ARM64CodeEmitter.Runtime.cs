using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class ARM64CodeEmitter {

  // AAPCS64 argument registers
  private static readonly ARM64Register[] AbiArgRegs = [
    ARM64Register.X0, ARM64Register.X1, ARM64Register.X2, ARM64Register.X3,
    ARM64Register.X4, ARM64Register.X5, ARM64Register.X6, ARM64Register.X7
  ];

  private const int F_GETPATH = 50; // fcntl F_GETPATH on macOS

  // --- Runtime function prologue/epilogue helpers ---

  private void EmitRuntimeFunctionStart(string name, int argCount, int stackSize = 0x30) {
    DefineLabel(name);
    _currentRuntimeStackSize = stackSize;
    // STP x29, x30, [sp, #-stackSize]!
    var imm7 = (uint)((-stackSize / 8) & 0x7F);
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    // MOV x29, sp
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);
    // Save arguments to stack
    for (int i = 0; i < argCount && i < 8; i++) {
      EmitLoadStoreUnsignedImm(0xF9000000, AbiArgRegs[i], ARM64Register.X29, 16 + i * 8, 8);
    }
  }

  private void EmitRuntimeFunctionEnd() {
    // MOV sp, x29
    EmitWord(0x91000000 | (29u << 5) | 31u);
    // LDP x29, x30, [sp], #stackSize
    var imm7 = (uint)((_currentRuntimeStackSize / 8) & 0x7F);
    EmitWord(0xA8C00000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    // RET
    EmitWord(0xD65F03C0);
  }

  // Reload argument from stack
  private void EmitReloadArg(int argIndex) {
    EmitLoadStoreUnsignedImm(0xF9400000, AbiArgRegs[argIndex], ARM64Register.X29, 16 + argIndex * 8, 8);
  }

  // --- Apple ARM64 variadic call helpers ---
  // On Apple ARM64, variadic function arguments are passed on the stack, not in registers.
  // Functions like open(path, flags, ...) and fcntl(fd, cmd, ...) require this.

  /// Push one 8-byte variadic argument onto the stack (16-byte aligned).
  /// Call EmitVariadicCleanup after the function call to restore SP.
  private void EmitPushVariadicArg(ARM64Register reg) {
    // SUB SP, SP, #16
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 16, isAdd: false);
    // STR reg, [SP, #0]
    EmitLoadStoreUnsignedImm(0xF9000000, reg, ARM64Register.Sp, 0, 8);
  }

  /// Restore SP after a variadic function call.
  private void EmitVariadicCleanup(int bytes = 16) {
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, bytes, isAdd: true);
  }

  // --- Libc error checking ---

  /// Branch to errorLabel if libc call returned negative (X0 < 0).
  private void EmitBranchOnLibcError(string errorLabel) {
    // CMP X0, #0 (SUBS XZR, X0, #0)
    EmitWord(0xF100001F);
    // B.LT errorLabel
    _condBranchFixups.Add((_code.Count, errorLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt));
  }

  // --- Runtime functions ---

  public void EmitRuntimeFunctions() {
    EmitMaxonWriteStdout();
    EmitMaxonWriteStderr();
    EmitMaxonManagedWriteStdout();
    EmitMaxonManagedWriteStderr();
    EmitMaxonExit();
    EmitWriteCstrToStderr();
    EmitMaxonPanic();
    EmitMaxonPanicPrintFrame();
    EmitMaxonBoundsCheck();
    EmitMaxonI64ToString();
    EmitMaxonU64ToString();
    EmitMaxonF64ToString();
    EmitMaxonMemcpy();
    EmitMaxonMemcmp();
    EmitMaxonStrlen();
    EmitMaxonToCstring();
    EmitMaxonCowCheck();
    EmitMaxonRawAlloc();
    EmitMaxonRawRealloc();
    EmitMaxonRawFree();
    EmitMaxonFileSize();
    EmitMaxonFileRead();
    EmitMaxonFileClose();
    EmitMaxonFileDelete();
    EmitMaxonCommandLineCount();
    EmitMaxonCommandLineArg();
    EmitMaxonDirectoryExists();
    EmitMaxonCreateDirectory();
    EmitMaxonGetCurrentDirectory();

    // Additional runtime functions
    EmitMaxonBoolToString();
    EmitMaxonI64ToStringFmt();
    EmitMaxonF64ToStringFmt();
    EmitManagedListInsertFirst();
    EmitManagedListInsertLast();
    EmitNetTcpConnect();
    EmitManagedFileOpenRead();
    EmitManagedFileOpenWrite();
    EmitManagedFileWrite();
    EmitManagedFileRead();
    EmitManagedFileClose();
    EmitFileDestructor();
    EmitMaxonManagedDirOpenSearch();
    EmitMaxonManagedDirClose();
    EmitDestructManagedDirectory();
    EmitMaxonFileExists();

    // Green thread runtime for async/await
    EmitGreenThreadRuntime();

    // Stubs for features not yet implemented on macOS
    EmitStubFunction("maxon_process_create");
    EmitStubFunction("maxon_process_wait");
    EmitStubFunction("maxon_process_get_exit_code");
    EmitStubFunction("maxon_process_close");
    EmitStubFunction("maxon_process_create_with_capture");
    EmitStubFunction("maxon_process_read_pipe");
    EmitStubFunction("maxon_process_get_handle");
    EmitStubFunction("maxon_process_close_capture");
    EmitStubFunction("maxon_process_read_stdout");
    EmitStubFunction("maxon_process_read_stderr");
    EmitNetSend();
    EmitNetRecv();
    EmitNetClose();
    EmitNetSocketDestructor();
    EmitStubFunction("managed_file_open_read");
    EmitStubFunction("managed_file_open_write");
    EmitStubFunction("managed_file_write");
    EmitMaxonFindFilename();
    EmitMaxonFindNextFile();

    // I/O subsystem stubs (not needed on macOS — uses synchronous calls)
    EmitStubFunction("__io_init");
    EmitStubFunction("__io_shutdown");
    EmitStubFunction("__io_runtime");
    EmitStubFunction("__gt_runtime");
  }

  private void EmitStubFunction(string name) {
    DefineLabel(name);
    // Return 0
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitWord(0xD65F03C0); // RET
  }

  // --- maxon_write_stdout(buf, len) ---
  // X0 = buffer ptr, X1 = length
  private void EmitMaxonWriteStdout() {
    EmitRuntimeFunctionStart("maxon_write_stdout", 2);
    // write(1, buf, len)
    EmitReloadArg(0); // buf -> X1
    var buf = ARM64Register.X0;
    EmitReloadArg(1);
    var len = ARM64Register.X1;
    // Rearrange: X0=fd=1, X1=buf, X2=len
    EmitMovRegReg(ARM64Register.X2, len);
    EmitMovRegReg(ARM64Register.X1, buf);
    EmitMovRegImm(ARM64Register.X0, 1); // stdout fd
    EmitCallImport("write");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_write_stderr(buf, len) ---
  private void EmitMaxonWriteStderr() {
    EmitRuntimeFunctionStart("maxon_write_stderr", 2);
    EmitReloadArg(0);
    EmitReloadArg(1);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, 2); // stderr fd
    EmitCallImport("write");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_write_stdout(buf_ptr, length) ---
  // Takes buffer pointer in X0 and length in X1, writes to stdout.
  // Matches the standard IR calling convention (2 args: buf_ptr, length).
  private void EmitMaxonManagedWriteStdout() {
    EmitRuntimeFunctionStart("maxon_managed_write_stdout", 2);
    EmitReloadArg(0); // X0 = buf_ptr
    EmitReloadArg(1); // X1 = length
    // write(fd=X0, buf=X1, count=X2): move args to correct positions
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1); // count = length
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // buf = buf_ptr
    EmitMovRegImm(ARM64Register.X0, 1); // fd = stdout
    EmitCallImport("write");
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonManagedWriteStderr() {
    EmitRuntimeFunctionStart("maxon_managed_write_stderr", 2);
    EmitReloadArg(0);
    EmitReloadArg(1);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, 2); // fd = stderr
    EmitCallImport("write");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_exit(code) ---
  private void EmitMaxonExit() {
    DefineLabel("maxon_exit");
    // X0 already has exit code
    EmitCallImport("_exit");
    EmitWord(0xD4200000); // BRK #0
  }

  // --- rt_write_cstr_stderr(cstr_ptr in X0) ---
  // Computes strlen of null-terminated string, writes to stderr fd 2.
  private void EmitWriteCstrToStderr() {
    EmitRuntimeFunctionStart("rt_write_cstr_stderr", 1, 0x20);
    // [x29+16] = cstr_ptr (arg 0)

    // Compute strlen: scan for null byte
    EmitReloadArg(0); // X0 = cstr_ptr
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = scan pointer
    DefineLabel("rt_write_cstr_stderr_strlen_loop");
    // LDRB W2, [X1], #1
    EmitWord(0x38401422);
    // CBNZ W2, loop
    _condBranchFixups.Add((_code.Count, "rt_write_cstr_stderr_strlen_loop"));
    EmitWord(0x35000002); // CBNZ W2, <fixup>
    // X1 now past null. len = X1 - cstr_ptr - 1
    EmitReloadArg(0); // X0 = cstr_ptr
    EmitAluRegReg(0xCB000000, ARM64Register.X2, ARM64Register.X1, ARM64Register.X0); // X2 = X1 - X0
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false); // exclude null

    // write(2, cstr_ptr, len)
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = buf
    // X2 = len (already set)
    EmitMovRegImm(ARM64Register.X0, 2); // fd = stderr
    EmitCallImport("write");

    EmitRuntimeFunctionEnd();
  }

  // --- maxon_panic(msg_ptr) ---
  // Prints panic message, walks stack, prints stack trace, exits with code 1.
  // Stack layout (all positive offsets within the allocated frame):
  //   [x29+0]  = saved X29
  //   [x29+8]  = saved X30
  //   [x29+16] = msg_ptr (arg 0)
  //   [x29+24] = text_base (addr of _start)
  //   [x29+32] = symtab_ptr (addr of __symtab)
  //   [x29+40] = current_frame_fp
  //   [x29+48] = frame_counter
  //   [x29+56] = symtab_count
  //   [x29+64] = text_offset (current frame)
  //   [x29+72] = saved X19 (callee-saved)
  //   [x29+80] = symdata_base (addr of __symdata_base)
  private void EmitMaxonPanic() {
    DefineRdata("__newline", [(byte)'\n']);
    // Ensure __symdata_base label exists at offset 0 of symdata for name resolution
    if (!_symdataLabels.ContainsKey("__symdata_base"))
      _symdataLabels["__symdata_base"] = 0;

    EmitRuntimeFunctionStart("maxon_panic", 1, 0x60);

    // Save X19 (callee-saved) so we can use it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X19, ARM64Register.X29, 72, 8);

    // Step 1: Print the panic message (already ends with \n)
    EmitReloadArg(0); // X0 = msg_ptr
    EmitBranchLink("rt_write_cstr_stderr");

    // Step 2: Compute text_base = address of _start
    EmitAdrpAddFixup(ARM64Register.X0, _funcAddrAdrpFixups, "_start");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // Step 3: Load symtab pointer and count
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__symtab");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    // Load count = [symtab_ptr] (first 8 bytes)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 56, 8);

    // Load symdata_base
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__symdata_base");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8);

    // Step 4: Print "Stack trace:\n"
    DefineSymdata("__panic_stacktrace", System.Text.Encoding.UTF8.GetBytes("Stack trace:\n\0"));
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__panic_stacktrace");
    EmitBranchLink("rt_write_cstr_stderr");

    // Step 5: Print first frame (the function that called panic)
    // [x29+8] = saved LR = return addr back to the function that called panic
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 8, 8); // X0 = return addr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // X1 = text_base
    EmitAluRegReg(0xCB000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X1); // X0 = ret_addr - text_base
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8); // text_offset
    EmitBranchLink("maxon_panic_print_frame");

    // Step 6: Initialize stack walk
    // current_frame = [x29] (panic's caller's saved X29 — from our STP prologue)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 0, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // current_frame
    EmitMovRegImm(ARM64Register.X0, 32);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // counter

    // Stack walk loop
    DefineLabel("rt_panic_walk_loop");

    // Check counter
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    _condBranchFixups.Add((_code.Count, "rt_panic_walk_done"));
    EmitWord(0xB4000000); // CBZ X0, rt_panic_walk_done

    // Decrement counter
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: false);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8);

    // Load current frame pointer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    _condBranchFixups.Add((_code.Count, "rt_panic_walk_done"));
    EmitWord(0xB4000000); // CBZ X0, rt_panic_walk_done

    // Get return address: [frame_fp + 8]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 8, 8); // X1 = return addr
    _condBranchFixups.Add((_code.Count, "rt_panic_walk_done"));
    EmitWord(0xB4000001); // CBZ X1, rt_panic_walk_done

    // Compute text_offset = return_addr - text_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 24, 8); // X2 = text_base
    EmitAluRegReg(0xCB000000, ARM64Register.X1, ARM64Register.X1, ARM64Register.X2); // X1 = ret_addr - text_base

    // Check not negative (outside .text) — use CMP + B.LT for condBranchFixup compatibility
    EmitWord(0xF100001F | (Reg(ARM64Register.X1) << 5)); // CMP X1, #0
    _condBranchFixups.Add((_code.Count, "rt_panic_walk_advance"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT rt_panic_walk_advance

    // Save text_offset
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 64, 8);

    // Advance frame_fp BEFORE calling print_frame (which clobbers regs)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // X0 = current_frame
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 0, 8); // X0 = [current_frame] = prev frame
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // update current_frame

    // Print this frame
    EmitBranchLink("maxon_panic_print_frame");

    _branchFixups.Add((_code.Count, "rt_panic_walk_loop"));
    EmitWord(0x14000000); // B rt_panic_walk_loop

    DefineLabel("rt_panic_walk_advance");
    // Advance frame even on skip (negative offset)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 0, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    _branchFixups.Add((_code.Count, "rt_panic_walk_loop"));
    EmitWord(0x14000000); // B rt_panic_walk_loop

    DefineLabel("rt_panic_walk_done");

    // Restore X19
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X19, ARM64Register.X29, 72, 8);

    // Exit with code 1
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitCallImport("_exit");
    EmitWord(0xD4200000); // BRK #0
  }

  // --- maxon_panic_print_frame ---
  // Looks up text_offset (from panic's frame) in the symbol table and prints "  in funcName\n".
  // Accesses panic's frame through saved X29 chain.
  // Stack layout:
  //   [x29+0]  = saved X29 (= panic's X29)
  //   [x29+8]  = saved X30 (return into panic)
  //   [x29+16] = symtab_ptr
  //   [x29+24] = count
  //   [x29+32] = text_offset
  //   [x29+40] = symdata_base
  private void EmitMaxonPanicPrintFrame() {
    DefineSymdata("__panic_in", System.Text.Encoding.UTF8.GetBytes("  in \0"));
    DefineSymdata("__panic_unknown", System.Text.Encoding.UTF8.GetBytes("<unknown>\0"));

    EmitRuntimeFunctionStart("maxon_panic_print_frame", 0, 0x30);

    // Load caller's (panic's) frame pointer to access its locals
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X19, ARM64Register.X29, 0, 8); // X19 = panic's x29

    // Print "  in "
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__panic_in");
    EmitBranchLink("rt_write_cstr_stderr");

    // Load symtab_ptr, symtab_count, text_offset, symdata_base from panic's frame
    // panic's layout: [+32]=symtab_ptr, [+56]=count, [+64]=text_offset, [+80]=symdata_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X19, 32, 8); // X0 = symtab_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X19, 56, 8); // X1 = count
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X19, 64, 8); // X2 = text_offset
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X19, 80, 8); // X3 = symdata_base

    // Save to our locals for after the lookup
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // [x29+16] = symtab_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // [x29+24] = count
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8); // [x29+32] = text_offset
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8); // [x29+40] = symdata_base

    // Linear scan symtab: entries start at symtab_ptr + 8 (skip count)
    // Each entry: (name_offset: i64, code_offset: i64) = 16 bytes
    // Find largest code_offset <= text_offset
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X0, 8, isAdd: true); // X3 = &entries[0]
    EmitMovRegImm(ARM64Register.X4, 0);  // X4 = loop index
    EmitMovRegImm(ARM64Register.X5, -1); // X5 = best_name_offset (-1 = none)
    EmitMovRegImm(ARM64Register.X8, -1); // X8 = best_code_offset (-1 = none, will be < any valid offset when unsigned)

    DefineLabel("rt_panic_lookup_loop");
    // if index >= count, done
    EmitWord(0xEB01009F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X1
    _condBranchFixups.Add((_code.Count, "rt_panic_lookup_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs)); // B.HS done

    // X6 = &entries[index] = X3 + index * 16
    // LSL X7, X4, #4 (multiply by 16) = UBFM X7, X4, #60, #59
    EmitWord(0xD37CEC87 | (Reg(ARM64Register.X4) << 5)); // LSL X7, X4, #4
    EmitAluRegReg(0x8B000000, ARM64Register.X6, ARM64Register.X3, ARM64Register.X7); // X6 = X3 + X7

    // Load code_offset: [X6 + 8]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X7, ARM64Register.X6, 8, 8); // X7 = code_offset

    // if text_offset < code_offset, skip this entry
    EmitWord(0xEB07005F | (Reg(ARM64Register.X7) << 16) | (Reg(ARM64Register.X2) << 5)); // CMP X2, X7
    _condBranchFixups.Add((_code.Count, "rt_panic_lookup_next"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lo)); // B.LO next

    // code_offset <= text_offset: only update if code_offset > best_code_offset
    // CMP X7, X8 (new code_offset vs best_code_offset)
    EmitAluRegReg(0xEB000000, ARM64Register.Xzr, ARM64Register.X7, ARM64Register.X8); // CMP X7, X8 = SUBS XZR, X7, X8
    _condBranchFixups.Add((_code.Count, "rt_panic_lookup_next"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE next (signed: if X7 <= X8, skip)

    // New best match
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X6, 0, 8); // X5 = name_offset
    EmitMovRegReg(ARM64Register.X8, ARM64Register.X7); // X8 = best_code_offset

    DefineLabel("rt_panic_lookup_next");
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: true); // index++
    _branchFixups.Add((_code.Count, "rt_panic_lookup_loop"));
    EmitWord(0x14000000); // B loop

    DefineLabel("rt_panic_lookup_done");

    // Check if we found a match (X5 != -1)
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X0) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, -1
    _condBranchFixups.Add((_code.Count, "rt_panic_print_unknown"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ unknown

    // Print function name: symdata_base + name_offset
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // X0 = symdata_base
    EmitAluRegReg(0x8B000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X5); // X0 = symdata_base + name_offset
    EmitBranchLink("rt_write_cstr_stderr");
    _branchFixups.Add((_code.Count, "rt_panic_print_newline"));
    EmitWord(0x14000000); // B print_newline

    DefineLabel("rt_panic_print_unknown");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__panic_unknown");
    EmitBranchLink("rt_write_cstr_stderr");

    DefineLabel("rt_panic_print_newline");
    // Print "\n" using write directly
    EmitAdrpAddFixup(ARM64Register.X1, _rdataAdrpFixups, "__newline");
    EmitMovRegImm(ARM64Register.X2, 1);
    EmitMovRegImm(ARM64Register.X0, 2);
    EmitCallImport("write");

    EmitRuntimeFunctionEnd();
  }

  // --- maxon_bounds_check(index, limit, msg_ptr) ---
  // Frameless helper: args in X0=index, X1=limit, X2=msg_ptr.
  // If in bounds (index < limit unsigned), returns immediately.
  // If out of bounds, tail-calls maxon_panic with msg_ptr in X0,
  // preserving the caller's frame pointer chain for clean stack traces.
  private void EmitMaxonBoundsCheck() {
    DefineLabel("maxon_bounds_check");
    // CMP X0 (index), X1 (limit)
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5));
    // B.LO ok (unsigned lower = in bounds)
    var okLabel = $"__bounds_ok_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, okLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lo)); // B.LO

    // Out of bounds — tail-call maxon_panic with msg_ptr in X0
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2);
    // B maxon_panic (not BL — tail call, preserves LR and frame chain)
    _branchFixups.Add((_code.Count, "maxon_panic"));
    EmitWord(0x14000000); // B <imm26>

    DefineLabel(okLabel);
    // RET
    EmitWord(0xD65F03C0);
  }

  // --- maxon_i64_to_string(value, buf) -> len ---
  // Converts i64 to decimal string in buffer, returns length
  private void EmitMaxonI64ToString() {
    EmitRuntimeFunctionStart("maxon_i64_to_string", 2, 0x50);

    EmitReloadArg(0); // value
    EmitReloadArg(1); // buf

    // Handle negative: if value < 0, write '-', negate
    var positiveLabel = $"__i64_positive_{_uniqueLabelCounter}";
    var convertLabel = $"__i64_convert_{_uniqueLabelCounter}";
    var reverseLabel = $"__i64_reverse_{_uniqueLabelCounter}";
    var doneLabel = $"__i64_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // X0 = value, X1 = buf
    // Save buf pointer to [x29, #32]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8);
    // X3 = current write position = buf
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X1);
    // Save original buf as start position [x29, #40]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8);

    // Check if negative
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, positiveLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE positive

    // Negative: write '-'
    EmitMovRegImm(ARM64Register.X4, (long)'-');
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // STRB W4, [X3]
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    // Save updated position
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8);
    // Negate value
    EmitWord(0xCB000000 | (Reg(ARM64Register.X0) << 16) | (31u << 5) | Reg(ARM64Register.X0)); // NEG X0, X0

    DefineLabel(positiveLabel);
    // X0 = absolute value, X3 = write position
    // We'll write digits in reverse, then reverse them
    // Save digit start position [x29, #48]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 48, 8);

    // Convert loop: divide by 10, store remainder as digit
    DefineLabel(convertLabel);
    EmitMovRegImm(ARM64Register.X1, 10);
    // UDIV X2, X0, X1 (quotient)
    EmitWord(0x9AC00800 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2));
    // MSUB X4, X2, X1, X0 (remainder = value - quotient * 10)
    EmitWord(0x9B008000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X4));
    // digit = remainder + '0'
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, (long)'0', isAdd: true);
    // STRB W4, [X3]
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4));
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    // X0 = quotient
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2);
    // Continue if quotient != 0
    _condBranchFixups.Add((_code.Count, convertLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, convertLabel

    // Now reverse the digits from [digit_start..X3)
    // Save end position
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 56, 8); // end pos
    // X5 = start, X6 = end-1
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 48, 8);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X3, 1, isAdd: false);

    DefineLabel(reverseLabel);
    // if start >= end-1, done
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, X6
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs)); // B.HS done

    // Swap bytes
    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X5]
    EmitWord(0x39400000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X8)); // LDRB W8, [X6]
    EmitWord(0x39000000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X8)); // STRB W8, [X5]
    EmitWord(0x39000000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X6]
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch(reverseLabel);

    DefineLabel(doneLabel);
    // Return length = end - buf_start
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 56, 8); // end
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // buf start
    EmitWord(0xCB000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X0)); // SUB X0, X3, X1
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonU64ToString() {
    // For now, redirect to i64 version (handles positive numbers the same way)
    DefineLabel("maxon_u64_to_string");
    EmitBranch("maxon_i64_to_string");
  }

  /// <summary>
  /// maxon_bool_to_string(value, buffer) -> length
  /// X0 = value (0=false, nonzero=true), X1 = buffer (>= 6 bytes)
  /// Returns length in X0 (4 for "true", 5 for "false")
  /// </summary>
  private void EmitMaxonBoolToString() {
    var falseLabel = $"__boolstr_false_{_uniqueLabelCounter}";
    var epilogueLabel = $"__boolstr_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitRuntimeFunctionStart("maxon_bool_to_string", 2, 0x30);
    EmitReloadArg(0); // value
    EmitReloadArg(1); // buf

    // CBZ X0, falseLabel
    _condBranchFixups.Add((_code.Count, falseLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0));

    // True path: write "true\0"
    EmitMovRegImm(ARM64Register.X2, (long)'t');
    EmitWord(0x39000000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1]
    EmitMovRegImm(ARM64Register.X2, (long)'r');
    EmitWord(0x39000400 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1, #1]
    EmitMovRegImm(ARM64Register.X2, (long)'u');
    EmitWord(0x39000800 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1, #2]
    EmitMovRegImm(ARM64Register.X2, (long)'e');
    EmitWord(0x39000C00 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1, #3]
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitWord(0x39001000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1, #4]
    EmitMovRegImm(ARM64Register.X0, 4);
    EmitBranch(epilogueLabel);

    // False path: write "false\0"
    DefineLabel(falseLabel);
    EmitMovRegImm(ARM64Register.X2, (long)'f');
    EmitWord(0x39000000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, (long)'a');
    EmitWord(0x39000400 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, (long)'l');
    EmitWord(0x39000800 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, (long)'s');
    EmitWord(0x39000C00 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, (long)'e');
    EmitWord(0x39001000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitWord(0x39001400 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X0, 5);

    DefineLabel(epilogueLabel);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_i64_to_string_fmt(value, buffer, fmt_ptr, fmt_len) -> length
  /// X0 = value, X1 = buffer (>= 72 bytes), X2 = fmt_ptr, X3 = fmt_len
  /// Format: [0][width][type] where type = d/x/X/b/o
  /// Stack layout (positive offsets from x29):
  ///   [+16] = value, [+24] = buffer, [+32] = fmt_ptr, [+40] = fmt_len
  ///   [+48] = fill_char, [+56] = min_width, [+64] = type_char
  ///   [+72] = digit_start, [+80] = write_pos/end, [+88] = is_negative
  /// </summary>
  private void EmitMaxonI64ToStringFmt() {
    var noFmtLabel = $"__i64fmt_nofmt_{_uniqueLabelCounter}";
    var parsedLabel = $"__i64fmt_parsed_{_uniqueLabelCounter}";
    var parseWidthLabel = $"__i64fmt_parsewidth_{_uniqueLabelCounter}";
    var parseTypeLabel = $"__i64fmt_parsetype_{_uniqueLabelCounter}";
    var positiveLabel = $"__i64fmt_positive_{_uniqueLabelCounter}";
    var hexLowerLabel = $"__i64fmt_hexlower_{_uniqueLabelCounter}";
    var hexUpperLabel = $"__i64fmt_hexupper_{_uniqueLabelCounter}";
    var decimalLabel = $"__i64fmt_decimal_{_uniqueLabelCounter}";
    var hexConvertLabel = $"__i64fmt_hexconv_{_uniqueLabelCounter}";
    var decConvertLabel = $"__i64fmt_decconv_{_uniqueLabelCounter}";
    var reverseLabel = $"__i64fmt_reverse_{_uniqueLabelCounter}";
    var reverseDoneLabel = $"__i64fmt_revdone_{_uniqueLabelCounter}";
    var padLabel = $"__i64fmt_pad_{_uniqueLabelCounter}";
    var padLoopLabel = $"__i64fmt_padloop_{_uniqueLabelCounter}";
    var doneLabel = $"__i64fmt_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitRuntimeFunctionStart("maxon_i64_to_string_fmt", 4, 0x70);

    // Default: fill=' ', width=0, type=0(decimal)
    EmitMovRegImm(ARM64Register.X4, (long)' ');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 48, 8); // fill
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 56, 8); // width
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 64, 8); // type

    // If fmt_len == 0, skip parsing
    EmitReloadArg(3); // fmt_len -> X3
    _condBranchFixups.Add((_code.Count, noFmtLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X3)); // CBZ X3, noFmtLabel

    // Parse format string
    EmitReloadArg(2); // fmt_ptr -> X2
    EmitReloadArg(3); // fmt_len -> X3
    // X4 = current position in fmt string
    EmitMovRegImm(ARM64Register.X4, 0);

    // Check for '0' fill
    EmitWord(0x39400000 | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5)); // LDRB W5, [X2]
    EmitMovRegImm(ARM64Register.X6, (long)'0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, X6
    _condBranchFixups.Add((_code.Count, parseWidthLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE parseWidth
    // fill = '0'
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X6, ARM64Register.X29, 48, 8);
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: true);

    // Parse width digits
    DefineLabel(parseWidthLabel);
    // While pos < len and char is digit
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X3
    _condBranchFixups.Add((_code.Count, parseTypeLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE parseType
    // Load char
    EmitWord(0x38606800 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5)); // LDRB W5, [X2, X4]
    // Check if digit: char >= '0' && char <= '9'
    EmitMovRegImm(ARM64Register.X6, (long)'0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, '0'
    _condBranchFixups.Add((_code.Count, parseTypeLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT parseType
    EmitMovRegImm(ARM64Register.X6, (long)'9');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, '9'
    _condBranchFixups.Add((_code.Count, parseTypeLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Gt)); // B.GT parseType
    // width = width * 10 + (char - '0')
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X6, ARM64Register.X29, 56, 8); // load width
    EmitMovRegImm(ARM64Register.X7, 10);
    EmitWord(0x9B007C00 | (Reg(ARM64Register.X7) << 16) | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X6)); // MUL X6, X6, X7
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, (long)'0', isAdd: false); // char - '0'
    EmitWord(0x8B000000 | (Reg(ARM64Register.X5) << 16) | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X6)); // ADD X6, X6, X5
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X6, ARM64Register.X29, 56, 8); // store width
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: true);
    EmitBranch(parseWidthLabel);

    // Parse type character
    DefineLabel(parseTypeLabel);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X3
    _condBranchFixups.Add((_code.Count, noFmtLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE noFmt (no type char)
    EmitWord(0x38606800 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5)); // LDRB W5, [X2, X4]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 64, 8); // store type

    DefineLabel(noFmtLabel);
    // Now convert the value based on type
    EmitReloadArg(0); // value -> X0
    EmitReloadArg(1); // buf -> X1
    // Save buf start
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8);
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X1); // X3 = write position

    // Check type: 'x' (0x78) or 'X' (0x58) = hex
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 64, 8);
    EmitMovRegImm(ARM64Register.X6, (long)'x');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5));
    _condBranchFixups.Add((_code.Count, hexLowerLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq));
    EmitMovRegImm(ARM64Register.X6, (long)'X');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5));
    _condBranchFixups.Add((_code.Count, hexUpperLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq));
    EmitBranch(decimalLabel);

    // --- Hex lower ---
    DefineLabel(hexLowerLabel);
    EmitMovRegImm(ARM64Register.X5, (long)'a');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 88, 8); // hex_base = 'a'
    EmitBranch(hexConvertLabel);

    // --- Hex upper ---
    DefineLabel(hexUpperLabel);
    EmitMovRegImm(ARM64Register.X5, (long)'A');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 88, 8); // hex_base = 'A'

    // --- Hex conversion ---
    DefineLabel(hexConvertLabel);
    // Save digit start
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 72, 8);
    // X0 = value (treat as unsigned)
    // Convert loop: extract nibble, write digit
    var hexLoopLabel = $"__i64fmt_hexloop_{_uniqueLabelCounter - 1}";
    DefineLabel(hexLoopLabel);
    // digit = X0 & 0xF
    EmitWord(0x92400C00 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X4)); // AND X4, X0, #0xF
    // X0 = X0 >> 4
    EmitWord(0xD344FC00 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // LSR X0, X0, #4
    // if digit < 10: char = digit + '0', else char = digit - 10 + hex_base
    EmitMovRegImm(ARM64Register.X6, 10);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, 10
    var hexDigitLabel = $"__i64fmt_hexdigit_{_uniqueLabelCounter - 1}";
    var hexAlphaLabel = $"__i64fmt_hexalpha_{_uniqueLabelCounter - 1}";
    var hexWriteLabel = $"__i64fmt_hexwrite_{_uniqueLabelCounter - 1}";
    _condBranchFixups.Add((_code.Count, hexAlphaLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE hexAlpha
    // Digit 0-9
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, (long)'0', isAdd: true);
    EmitBranch(hexWriteLabel);
    DefineLabel(hexAlphaLabel);
    // Digit A-F
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 10, isAdd: false);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X6, ARM64Register.X29, 88, 8); // hex_base
    EmitWord(0x8B000000 | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X4) << 5) | Reg(ARM64Register.X4)); // ADD X4, X4, X6
    DefineLabel(hexWriteLabel);
    // STRB W4, [X3]
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4));
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    // Continue if X0 != 0
    _condBranchFixups.Add((_code.Count, hexLoopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, hexLoop
    EmitBranch(reverseLabel);

    // --- Decimal conversion ---
    DefineLabel(decimalLabel);
    // Mark not negative
    EmitMovRegImm(ARM64Register.X5, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 88, 8); // is_negative = 0

    // Check negative
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, positiveLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE positive

    // Write '-'
    EmitMovRegImm(ARM64Register.X4, (long)'-');
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // STRB '-'
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    // Negate
    EmitWord(0xCB000000 | (Reg(ARM64Register.X0) << 16) | (31u << 5) | Reg(ARM64Register.X0)); // NEG X0, X0
    EmitMovRegImm(ARM64Register.X5, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 88, 8); // is_negative = 1

    DefineLabel(positiveLabel);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 72, 8); // digit start

    // Decimal convert loop
    DefineLabel(decConvertLabel);
    EmitMovRegImm(ARM64Register.X1, 10);
    EmitWord(0x9AC00800 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // UDIV X2, X0, X1
    EmitWord(0x9B008000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X4)); // MSUB X4, X2, X1, X0
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, (long)'0', isAdd: true);
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // STRB digit
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2);
    _condBranchFixups.Add((_code.Count, decConvertLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, decConvert

    // --- Reverse digits ---
    DefineLabel(reverseLabel);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 80, 8); // save end pos
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 72, 8); // start
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X3, 1, isAdd: false); // end - 1

    DefineLabel(reverseLabel + "_loop");
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, X6
    _condBranchFixups.Add((_code.Count, reverseDoneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs)); // B.HS done

    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X5]
    EmitWord(0x39400000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X8)); // LDRB W8, [X6]
    EmitWord(0x39000000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X8)); // STRB W8, [X5]
    EmitWord(0x39000000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X6]
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch(reverseLabel + "_loop");

    DefineLabel(reverseDoneLabel);
    // --- Padding ---
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 80, 8); // end
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buf start
    EmitWord(0xCB000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // X4 = end - start = current length
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 56, 8); // min_width
    EmitWord(0xEB00001F | (Reg(ARM64Register.X5) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X5
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE done (already wide enough)

    // Need to pad. For zero-padding, shift digits right and insert fill chars.
    // For space-padding, also shift right and insert.
    // pad_count = min_width - current_length
    EmitWord(0xCB000000 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X6)); // X6 = pad_count = width - len

    // Check if fill is '0' and is_negative — if so, shift after the '-'
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X7, ARM64Register.X29, 48, 8); // fill char
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X8, ARM64Register.X29, 88, 8); // is_negative

    // Shift existing content right by pad_count bytes (from end-1 down to start)
    // memmove: for i = len-1 down to 0: buf[i+pad_count] = buf[i]
    // Use X9 = src index (from len-1 down to 0)
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X4, 1, isAdd: false); // X9 = len - 1
    var shiftLoopLabel = $"__i64fmt_shift_{_uniqueLabelCounter - 1}";
    var shiftDoneLabel = $"__i64fmt_shiftdone_{_uniqueLabelCounter - 1}";
    DefineLabel(shiftLoopLabel);
    // if X9 < 0, done shifting
    EmitWord(0xF100001F | (Reg(ARM64Register.X9) << 5)); // CMP X9, #0
    _condBranchFixups.Add((_code.Count, shiftDoneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT shiftDone
    // Load byte at buf[X9]
    EmitWord(0x38696800 | (Reg(ARM64Register.X9) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X10)); // LDRB W10, [X1, X9]
    // dst = X9 + pad_count
    EmitWord(0x8B000000 | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X9) << 5) | Reg(ARM64Register.X11)); // ADD X11, X9, X6
    // Store byte at buf[X11]
    EmitWord(0x382B6800 | (Reg(ARM64Register.X11) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X10)); // STRB W10, [X1, X11]
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X9, 1, isAdd: false);
    EmitBranch(shiftLoopLabel);

    DefineLabel(shiftDoneLabel);
    // Fill the gap with fill char
    // Determine fill start: if zero-pad and negative, start at 1 (after '-'), else 0
    EmitMovRegImm(ARM64Register.X9, 0); // fill start index
    EmitMovRegImm(ARM64Register.X10, (long)'0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X10) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP fill, '0'
    var fillStartLabel = $"__i64fmt_fillstart_{_uniqueLabelCounter - 1}";
    _condBranchFixups.Add((_code.Count, fillStartLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE fillStart (not zero-pad)
    // Zero pad: if negative, start fill at index 1
    _condBranchFixups.Add((_code.Count, fillStartLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X8)); // CBZ X8, fillStart (not negative)
    EmitMovRegImm(ARM64Register.X9, 1);

    DefineLabel(fillStartLabel);
    // X9 = fill index, fill until X9 == X6 + fill_start
    EmitWord(0x8B000000 | (Reg(ARM64Register.X9) << 16) | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X10)); // X10 = pad_count + fill_start_offset...
    // Actually just fill from X9 to X9+pad_count
    EmitWord(0x8B000000 | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X9) << 5) | Reg(ARM64Register.X10)); // X10 = end = start + pad_count
    var fillLoopLabel = $"__i64fmt_fillloop_{_uniqueLabelCounter - 1}";
    DefineLabel(fillLoopLabel);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X10) << 16) | (Reg(ARM64Register.X9) << 5)); // CMP X9, X10
    _condBranchFixups.Add((_code.Count, padLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE padDone
    // Store fill char
    EmitWord(0x38296800 | (Reg(ARM64Register.X9) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X1, X9]
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X9, 1, isAdd: true);
    EmitBranch(fillLoopLabel);

    DefineLabel(padLabel);
    // Update length and null-terminate
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 56, 8); // width (= new length)
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X5);
    EmitWord(0x8B000000 | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X4)); // X4 = buf + length
    EmitMovRegImm(ARM64Register.X7, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X4) << 5) | Reg(ARM64Register.X7)); // STRB 0, [X4]
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X3);
    EmitRuntimeFunctionEnd();

    DefineLabel(doneLabel);
    // No padding needed, just null-terminate and return length
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 80, 8); // end
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buf start
    EmitWord(0xCB000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X0)); // X0 = end - start
    // Null terminate
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // STRB 0, [end]
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_f64_to_string(value_in_D0, buffer_in_X0) -> length_in_X0
  /// Converts a double to string like "3.14159" or "-0.5" or "0.0".
  /// Stack layout (runtime function, positive offsets from x29):
  ///   [+16] = buffer (saved X0), [+24] = is_negative
  ///   [+32] = integer_part, [+40] = int_str_length
  ///   [+48] = scratch
  /// D0 is NOT saved by EmitRuntimeFunctionStart, so use it immediately.
  /// </summary>
  private void EmitMaxonF64ToString() {
    var notZeroLabel = $"__f64str_notzero_{_uniqueLabelCounter}";
    var positiveLabel = $"__f64str_positive_{_uniqueLabelCounter}";
    var noSignLabel = $"__f64str_nosign_{_uniqueLabelCounter}";
    var fracLoopLabel = $"__f64str_fracloop_{_uniqueLabelCounter}";
    var stripLoopLabel = $"__f64str_striploop_{_uniqueLabelCounter}";
    var stripDoneLabel = $"__f64str_stripdone_{_uniqueLabelCounter}";
    var epilogueLabel = $"__f64str_epilogue_{_uniqueLabelCounter}";
    var fracOkLabel = $"__f64str_frac_ok_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitRuntimeFunctionStart("maxon_f64_to_string", 1, 0x60);
    // Note: only 1 GPR arg (X0 = buffer). D0 has the float but isn't saved by the framework.

    // Save D0 to [x29, #48] using STR D0, [X29, #48]
    EmitWord(0xFD000000 | ((48u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // STR D0, [X29, #48]

    // Check if value == 0.0
    // FCMP D0, #0.0
    EmitWord(0x1E602008); // FCMP D0, #0.0
    _condBranchFixups.Add((_code.Count, notZeroLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE notZero

    // Write "0.0\0"
    EmitReloadArg(0); // X0 = buffer
    EmitMovRegImm(ARM64Register.X1, (long)'0');
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB '0', [X0]
    EmitMovRegImm(ARM64Register.X1, (long)'.');
    EmitWord(0x39000400 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB '.', [X0, #1]
    EmitMovRegImm(ARM64Register.X1, (long)'0');
    EmitWord(0x39000800 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB '0', [X0, #2]
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitWord(0x39000C00 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB 0, [X0, #3]
    EmitMovRegImm(ARM64Register.X0, 3);
    EmitBranch(epilogueLabel);

    // Handle sign
    DefineLabel(notZeroLabel);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // is_negative = 0

    // Reload D0 from stack (since we might have clobbered it... actually we didn't, but be safe)
    EmitWord(0xFD400000 | ((48u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #48]

    // FCMP D0, #0.0
    EmitWord(0x1E602008);
    _condBranchFixups.Add((_code.Count, positiveLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE positive (not negative)

    // Negative: negate D0 = -D0 via FNEG D0, D0
    EmitWord(0x1E614000); // FNEG D0, D0
    // Save negated value
    EmitWord(0xFD000000 | ((48u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // STR D0, [X29, #48]
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // is_negative = 1

    DefineLabel(positiveLabel);
    // D0 = |value|. Extract integer part.
    // FCVTZS X1, D0 (truncate to signed integer)
    EmitWord(0x9E780001); // FCVTZS X1, D0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8); // integer_part

    // Call maxon_i64_to_string(integer_value, buffer + is_negative)
    EmitReloadArg(0); // X0 = buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 24, 8); // is_negative
    EmitWord(0x8B000000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // X1 = buf + is_negative
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // X0 = integer_part
    EmitBranchLink("maxon_i64_to_string");
    // X0 = int string length
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // int_str_length

    // If negative, write '-' at buffer[0] and add 1 to length
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // is_negative
    _condBranchFixups.Add((_code.Count, noSignLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X1)); // CBZ X1, noSign
    EmitReloadArg(0); // buffer
    EmitMovRegImm(ARM64Register.X2, (long)'-');
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // STRB '-', [buffer]
    // length += 1
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);

    DefineLabel(noSignLabel);
    // Append '.' at buffer[length]
    EmitReloadArg(0); // buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 40, 8); // length
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // X2 = buf + len
    EmitMovRegImm(ARM64Register.X3, (long)'.');
    EmitWord(0x39000000 | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X3)); // STRB '.', [buf+len]

    // Extract fractional part: frac = |value| - integer_part
    // Reload |value| into D0
    EmitWord(0xFD400000 | ((48u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #48]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // integer_part
    // SCVTF D1, X0 (convert int to double)
    EmitWord(0x9E620001); // SCVTF D1, X0
    // FSUB D0, D0, D1 (fractional part)
    EmitWord(0x1E613800); // FSUB D0, D0, D1

    // Multiply by 1e6 for 6 decimal places
    // Load 1000000.0 into D1 via FMOV from X register
    EmitMovRegImm(ARM64Register.X0, BitConverter.DoubleToInt64Bits(1000000.0));
    // FMOV Dd, Xn = 0x9E670000 | (Rn << 5) | Rd
    EmitWord(0x9E670000 | (Reg(ARM64Register.X0) << 5) | 1u); // FMOV D1, X0
    // FMUL D0, D0, D1
    EmitWord(0x1E610800); // FMUL D0, D0, D1

    // Add 0.5 for rounding
    EmitMovRegImm(ARM64Register.X0, BitConverter.DoubleToInt64Bits(0.5));
    EmitWord(0x9E670000 | (Reg(ARM64Register.X0) << 5) | 1u); // FMOV D1, X0
    // FADD D0, D0, D1
    EmitWord(0x1E612800); // FADD D0, D0, D1

    // FCVTZS X0, D0 (truncate to integer = fractional digits)
    EmitWord(0x9E780000); // FCVTZS X0, D0

    // Clamp to 999999 if rounding pushed it over
    EmitMovRegImm(ARM64Register.X1, 1000000);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, 1000000
    _condBranchFixups.Add((_code.Count, fracOkLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT fracOk
    EmitMovRegImm(ARM64Register.X0, 999999);
    DefineLabel(fracOkLabel);

    // Write 6 fractional digits at buf + int_len + 1 (after the '.')
    // We write them from position 5 down to 0 (dividing by 10 each time)
    // Save frac_digits (in X0) to [x29, #56] before we clobber X0 with buffer pointer
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8); // save frac_digits
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 40, 8); // X3 = int len
    EmitReloadArg(0); // X0 = buffer
    EmitWord(0x8B000000 | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X3)); // X3 = buf + int_len
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true); // X3 = buf + int_len + 1 (past '.')
    // Reload frac_digits into X0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // X0 = frac_digits

    // X0 = frac_digits, X3 = write base
    // We need to write from X3[0] to X3[5]
    // Algorithm: for i=5 down to 0: digit = X0 % 10, X0 /= 10, buf[i] = '0'+digit
    EmitMovRegImm(ARM64Register.X4, 6); // loop counter

    DefineLabel(fracLoopLabel);
    EmitMovRegImm(ARM64Register.X1, 10);
    // UDIV X2, X0, X1
    EmitWord(0x9AC00800 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2));
    // MSUB X5, X2, X1, X0 (remainder)
    EmitWord(0x9B008000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5));
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, (long)'0', isAdd: true);
    // Store at X3[X4-1]
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X4, 1, isAdd: false);
    EmitWord(0x38266800 | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X5)); // STRB W5, [X3, X6]
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2); // quotient
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: false);
    _condBranchFixups.Add((_code.Count, fracLoopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X4)); // CBNZ X4, loop

    // Strip trailing zeros (keep at least 1 fractional digit)
    // X6 = pointer to last digit = X3+5
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X3, 5, isAdd: true);

    DefineLabel(stripLoopLabel);
    // if X6 <= X3, stop
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X6) << 5)); // CMP X6, X3
    _condBranchFixups.Add((_code.Count, stripDoneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ls)); // B.LS stripDone
    // LDRB W7, [X6]
    EmitWord(0x39400000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7));
    EmitMovRegImm(ARM64Register.X8, (long)'0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X8) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP W7, '0'
    _condBranchFixups.Add((_code.Count, stripDoneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE stripDone
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch(stripLoopLabel);

    DefineLabel(stripDoneLabel);
    // Null-terminate at X6+1
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true);
    EmitMovRegImm(ARM64Register.X7, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // STRB 0, [X6]

    // Total length = X6 - buffer
    EmitReloadArg(0); // buffer
    EmitWord(0xCB000000 | (Reg(ARM64Register.X0) << 16) | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X0)); // X0 = X6 - buffer

    DefineLabel(epilogueLabel);
    EmitRuntimeFunctionEnd();

    // Also stub the formatted variants
    EmitStubFunction("maxon_f64_to_string_formatted");
    EmitStubFunction("maxon_f32_to_string");
    EmitStubFunction("maxon_f32_to_string_formatted");
    EmitStubFunction("maxon_i64_to_string_formatted");
    EmitStubFunction("maxon_u64_to_string_formatted");
  }

  // --- maxon_f64_to_string_fmt(D0=value, X0=buffer, X1=fmtPtr, X2=fmtLen) -> X0=length ---
  // Format: [0][width][.precision] — e.g., ".2" (2 decimal places), "8.2" (width 8, 2 dp)
  // Stack layout (runtime function saves X0-X2 at [x29,#16..#32]):
  //   [x29, #40] = saved D0 (float value)
  //   [x29, #48] = is_negative
  //   [x29, #56] = min_width
  //   [x29, #64] = precision (default -1 = not specified)
  //   [x29, #72] = integer_part
  //   [x29, #80] = int_str_length (includes sign)
  //   [x29, #88] = frac_digits
  //   [x29, #96] = fill_char (' ' or '0')
  //   [x29, #104] = content_len
  //   [x29, #112] = parse_index / scratch
  private void EmitMaxonF64ToStringFmt() {
    var lbl = $"__f64fmt_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitRuntimeFunctionStart("maxon_f64_to_string_fmt", 3, 0xC0);
    // D0 has float value, not saved by framework
    // Save D0 to [x29, #40]
    EmitWord(0xFD000000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // STR D0, [X29, #40]

    // Initialize defaults: width=0, precision=-1, fill=' '
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8); // width = 0
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8); // precision = -1
    EmitMovRegImm(ARM64Register.X0, ' ');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 96, 8); // fill = ' '

    // If fmt_len == 0, delegate to default
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // X0 = fmtLen from [x29,#32]
    _condBranchFixups.Add((_code.Count, $"{lbl}_default"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, default

    // X5 = fmtPtr, X6 = parse_index, X7 = fmt_len
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 24, 8); // X5 = fmtPtr from [x29,#24]
    EmitMovRegImm(ARM64Register.X6, 0); // parse_index
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X7, ARM64Register.X29, 32, 8); // X7 = fmtLen from [x29,#32]

    // Check for '0' fill char
    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDRB W0, [X5]
    EmitMovRegImm(ARM64Register.X1, '0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '0'
    _condBranchFixups.Add((_code.Count, $"{lbl}_parse_width"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE parse_width
    // Check if next char is '.', meaning '0' is not a fill
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP fmt_len, 1
    _condBranchFixups.Add((_code.Count, $"{lbl}_parse_width"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ls)); // B.LS parse_width
    EmitWord(0x39400400 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDRB W0, [X5, #1]
    EmitMovRegImm(ARM64Register.X1, '.');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '.'
    _condBranchFixups.Add((_code.Count, $"{lbl}_parse_width"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ parse_width (not a fill)
    // It's a '0' fill
    EmitMovRegImm(ARM64Register.X0, '0');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 96, 8); // fill = '0'
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true); // advance fmtPtr
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true); // advance parse_index

    // Parse width digits
    DefineLabel($"{lbl}_parse_width");
    EmitMovRegImm(ARM64Register.X8, 0); // accumulated width

    DefineLabel($"{lbl}_width_loop");
    EmitWord(0xEB00001F | (Reg(ARM64Register.X7) << 16) | (Reg(ARM64Register.X6) << 5)); // CMP parse_index, fmt_len
    _condBranchFixups.Add((_code.Count, $"{lbl}_width_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE width_done
    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDRB W0, [X5]
    EmitMovRegImm(ARM64Register.X1, '.');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '.'
    _condBranchFixups.Add((_code.Count, $"{lbl}_dot"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ dot
    EmitMovRegImm(ARM64Register.X1, '0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '0'
    _condBranchFixups.Add((_code.Count, $"{lbl}_width_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT width_done
    EmitMovRegImm(ARM64Register.X1, '9');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '9'
    _condBranchFixups.Add((_code.Count, $"{lbl}_width_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Gt)); // B.GT width_done
    // digit: width = width * 10 + (ch - '0')
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, '0', isAdd: false); // X0 = digit
    EmitMovRegImm(ARM64Register.X1, 10);
    // MADD X8, X8, X1, X0 = X8*10 + X0
    EmitWord(0x9B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X8) << 5) | Reg(ARM64Register.X8)); // MADD X8, X8, X1, X0
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true);
    EmitBranch($"{lbl}_width_loop");

    // Found '.', save width, parse precision
    DefineLabel($"{lbl}_dot");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X8, ARM64Register.X29, 56, 8); // save width
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true); // skip '.'
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true);
    EmitMovRegImm(ARM64Register.X8, 0); // accumulated precision

    DefineLabel($"{lbl}_prec_loop");
    EmitWord(0xEB00001F | (Reg(ARM64Register.X7) << 16) | (Reg(ARM64Register.X6) << 5)); // CMP parse_index, fmt_len
    _condBranchFixups.Add((_code.Count, $"{lbl}_prec_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE prec_done
    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDRB W0, [X5]
    EmitMovRegImm(ARM64Register.X1, '0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5));
    _condBranchFixups.Add((_code.Count, $"{lbl}_prec_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt));
    EmitMovRegImm(ARM64Register.X1, '9');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5));
    _condBranchFixups.Add((_code.Count, $"{lbl}_prec_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Gt));
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, '0', isAdd: false);
    EmitMovRegImm(ARM64Register.X1, 10);
    EmitWord(0x9B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X8) << 5) | Reg(ARM64Register.X8)); // MADD X8, X8, X1, X0
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true);
    EmitBranch($"{lbl}_prec_loop");

    DefineLabel($"{lbl}_prec_done");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X8, ARM64Register.X29, 64, 8); // save precision
    EmitBranch($"{lbl}_convert");

    DefineLabel($"{lbl}_width_done");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X8, ARM64Register.X29, 56, 8); // save width

    // ---- Convert ----
    DefineLabel($"{lbl}_convert");
    // If precision == -1 and width == 0, use default
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8); // precision
    EmitMovRegImm(ARM64Register.X1, -1);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP precision, -1
    _condBranchFixups.Add((_code.Count, $"{lbl}_has_precision"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE has_precision
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // width
    _condBranchFixups.Add((_code.Count, $"{lbl}_has_width_no_prec"));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, has_width_no_prec

    // Default: delegate to maxon_f64_to_string
    DefineLabel($"{lbl}_default");
    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40]
    EmitReloadArg(0); // X0 = buffer
    // maxon_f64_to_string(D0=value, X0=buffer) -> X0=length
    EmitBranchLink("maxon_f64_to_string");
    EmitBranch($"{lbl}_epilogue");

    // Has width but no precision: default format then pad
    DefineLabel($"{lbl}_has_width_no_prec");
    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40]
    EmitReloadArg(0);
    EmitBranchLink("maxon_f64_to_string");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 104, 8); // save content_len
    EmitBranch($"{lbl}_apply_width");

    // Has precision
    DefineLabel($"{lbl}_has_precision");
    // Clamp precision to 20
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    EmitMovRegImm(ARM64Register.X1, 20);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP precision, 20
    _condBranchFixups.Add((_code.Count, $"{lbl}_prec_ok"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE prec_ok
    EmitMovRegImm(ARM64Register.X0, 20);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    DefineLabel($"{lbl}_prec_ok");

    // Handle sign
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // is_negative = 0

    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40]
    EmitWord(0x1E602008); // FCMP D0, #0.0
    _condBranchFixups.Add((_code.Count, $"{lbl}_positive"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE positive

    // Negative: negate
    EmitWord(0x1E614000); // FNEG D0, D0
    EmitWord(0xFD000000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // STR D0, [X29, #40]
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // is_negative = 1

    DefineLabel($"{lbl}_positive");
    // Extract integer part
    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40]
    EmitWord(0x9E780000); // FCVTZS X0, D0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8); // integer_part

    // Call maxon_i64_to_string(integer_value, buffer + is_negative)
    EmitReloadArg(0); // X0 = buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 48, 8); // is_negative
    EmitWord(0x8B000000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // X1 = buf + is_negative
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 72, 8); // X0 = integer_part
    EmitBranchLink("maxon_i64_to_string");
    // X0 = int string length
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8); // save int_str_length

    // If negative, write '-' and add 1 to length
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 48, 8);
    _condBranchFixups.Add((_code.Count, $"{lbl}_no_sign"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X1)); // CBZ X1, no_sign
    EmitReloadArg(0);
    EmitMovRegImm(ARM64Register.X2, '-');
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // STRB '-', [buffer]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 80, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8);

    DefineLabel($"{lbl}_no_sign");
    // If precision == 0, no decimal point
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    _condBranchFixups.Add((_code.Count, $"{lbl}_has_frac"));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, has_frac
    // content_len = int_str_length, null-terminate
    EmitReloadArg(0); // buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 80, 8); // int_str_length
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // X0 = buf + len
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // STRB 0, [X0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 80, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 104, 8); // content_len
    EmitBranch($"{lbl}_apply_width");

    DefineLabel($"{lbl}_has_frac");
    // Write '.' after integer part
    EmitReloadArg(0); // buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 80, 8); // int_str_length
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X3)); // X3 = buf + int_str_length
    EmitMovRegImm(ARM64Register.X2, '.');
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X2)); // STRB '.', [X3]
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true); // X3 = write pos after '.'
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 112, 8); // save write_pos

    // Compute fractional part: (|value| - int_part) * 10^precision + 0.5, truncate
    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40] = |value|
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 72, 8); // integer_part
    EmitWord(0x9E620001); // SCVTF D1, X0
    EmitWord(0x1E613800); // FSUB D0, D0, D1 (fractional part)

    // Multiply by 10^precision: loop
    EmitMovRegImm(ARM64Register.X0, BitConverter.DoubleToInt64Bits(10.0));
    EmitWord(0x9E670000 | (Reg(ARM64Register.X0) << 5) | 1u); // FMOV D1, X0 (D1 = 10.0)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 64, 8); // precision

    DefineLabel($"{lbl}_mul_loop");
    _condBranchFixups.Add((_code.Count, $"{lbl}_mul_done"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X4)); // CBZ X4, mul_done
    EmitWord(0x1E610800); // FMUL D0, D0, D1
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: false);
    EmitBranch($"{lbl}_mul_loop");

    DefineLabel($"{lbl}_mul_done");
    // Add 0.5 for rounding
    EmitMovRegImm(ARM64Register.X0, BitConverter.DoubleToInt64Bits(0.5));
    EmitWord(0x9E670000 | (Reg(ARM64Register.X0) << 5) | 1u); // FMOV D1, X0
    EmitWord(0x1E612800); // FADD D0, D0, D1
    EmitWord(0x9E780000); // FCVTZS X0, D0 = frac_digits

    // Write precision digits right-to-left at write_pos
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 88, 8); // save frac_digits
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 112, 8); // write_pos
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 64, 8); // precision (loop counter)

    DefineLabel($"{lbl}_frac_write");
    _condBranchFixups.Add((_code.Count, $"{lbl}_frac_written"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X4)); // CBZ X4, frac_written
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: false); // counter--
    // digit = frac % 10, frac /= 10
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 88, 8); // frac
    EmitMovRegImm(ARM64Register.X1, 10);
    // UDIV X2, X0, X1
    EmitWord(0x9AC00800 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2));
    // MSUB X5, X2, X1, X0 (remainder = X0 - X2*X1)
    EmitWord(0x9B008000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5));
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, '0', isAdd: true); // digit char
    // Store at write_pos[counter] (X4 is already decremented)
    EmitWord(0x38206800 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X5)); // STRB W5, [X3, X4]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 88, 8); // frac = quotient
    EmitBranch($"{lbl}_frac_write");

    DefineLabel($"{lbl}_frac_written");
    // Null-terminate at write_pos + precision
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 112, 8); // write_pos
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 64, 8); // precision
    EmitWord(0x8B000000 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X0)); // X0 = write_pos + precision
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB 0, [X0]

    // content_len = int_str_length + 1 (dot) + precision
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 80, 8); // int_str_length
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true); // + dot
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 64, 8); // precision
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // X0 = int_len + 1 + prec
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 104, 8); // content_len

    // ---- Apply width padding ----
    DefineLabel($"{lbl}_apply_width");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // width
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 104, 8); // content_len
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP width, content_len
    _condBranchFixups.Add((_code.Count, $"{lbl}_no_pad"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE no_pad (width <= content_len)

    // pad_count = width - content_len
    // Shift content right by pad_count, then fill left with fill_char
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 104, 8); // content_len
    // Memmove right: copy from end backwards
    EmitReloadArg(0); // X0 = buffer
    // src_end = buffer + content_len - 1
    EmitWord(0x8B000000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X3)); // X3 = buf + content_len
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: false); // X3 = src_end
    // dst_end = buffer + width - 1
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 56, 8); // width
    EmitWord(0x8B000000 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X5)); // X5 = buf + width
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: false); // X5 = dst_end
    // count = content_len
    EmitMovRegReg(ARM64Register.X6, ARM64Register.X2);

    DefineLabel($"{lbl}_shift_loop");
    _condBranchFixups.Add((_code.Count, $"{lbl}_shift_done"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X6)); // CBZ X6, shift_done
    EmitWord(0x39400000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X3]
    EmitWord(0x39000000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X5]
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: false);
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: false);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch($"{lbl}_shift_loop");

    DefineLabel($"{lbl}_shift_done");
    // Fill padding with fill_char
    EmitReloadArg(0); // X0 = buffer
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X0); // X3 = fill ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 56, 8); // width
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 104, 8); // content_len
    EmitWord(0xCB000000 | (Reg(ARM64Register.X5) << 16) | (Reg(ARM64Register.X4) << 5) | Reg(ARM64Register.X6)); // X6 = width - content_len
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X7, ARM64Register.X29, 96, 8); // fill_char

    DefineLabel($"{lbl}_fill_loop");
    _condBranchFixups.Add((_code.Count, $"{lbl}_fill_done"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X6)); // CBZ X6, fill_done
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X3]
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch($"{lbl}_fill_loop");

    DefineLabel($"{lbl}_fill_done");
    // Null-terminate at buffer + width
    EmitReloadArg(0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 56, 8); // width
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // X0 = buf + width
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // STRB 0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // return width
    EmitBranch($"{lbl}_epilogue");

    DefineLabel($"{lbl}_no_pad");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 104, 8); // return content_len

    DefineLabel($"{lbl}_epilogue");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_memcpy(dst, src, len) ---
  private void EmitMaxonMemcpy() {
    EmitRuntimeFunctionStart("maxon_memcpy", 3);
    EmitReloadArg(0); // dst
    EmitReloadArg(1); // src
    EmitReloadArg(2); // len

    var loopLabel = $"__memcpy_rt_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__memcpy_rt_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // X0=dst, X1=src, X2=len
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, done

    DefineLabel(loopLabel);
    // LDRB W3, [X1], #1
    EmitWord(0x38401423);
    // STRB W3, [X0], #1
    EmitWord(0x38001403);
    // SUB X2, X2, #1
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false);
    _condBranchFixups.Add((_code.Count, loopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X2)); // CBNZ X2, loop

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_memcmp(a, b, len) -> i64 ---
  private void EmitMaxonMemcmp() {
    EmitRuntimeFunctionStart("maxon_memcmp", 3);
    EmitReloadArg(0); // a
    EmitReloadArg(1); // b
    EmitReloadArg(2); // len

    var loopLabel = $"__memcmp_loop_{_uniqueLabelCounter}";
    var doneEqLabel = $"__memcmp_eq_{_uniqueLabelCounter}";
    var doneLtLabel = $"__memcmp_lt_{_uniqueLabelCounter}";
    var doneLabel = $"__memcmp_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    _condBranchFixups.Add((_code.Count, doneEqLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, equal

    DefineLabel(loopLabel);
    EmitWord(0x39400000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X3)); // LDRB W3, [X0]
    EmitWord(0x39400000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X4)); // LDRB W4, [X1]
    EmitWord(0xEB00001F | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X3) << 5)); // CMP X3, X4
    _condBranchFixups.Add((_code.Count, doneLtLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lo)); // B.LO lt
    _condBranchFixups.Add((_code.Count, $"__memcmp_gt_{_uniqueLabelCounter}"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hi)); // B.HI gt

    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false);
    _condBranchFixups.Add((_code.Count, loopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X2)); // CBNZ X2, loop

    DefineLabel(doneEqLabel);
    EmitMovRegImm(ARM64Register.X0, 1); // 1 = equal (matches x86 SETE convention)
    EmitBranch(doneLabel);

    DefineLabel(doneLtLabel);
    EmitMovRegImm(ARM64Register.X0, 0); // 0 = not equal
    EmitBranch(doneLabel);

    DefineLabel($"__memcmp_gt_{_uniqueLabelCounter}");
    EmitMovRegImm(ARM64Register.X0, 0); // 0 = not equal

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_strlen(str) -> len ---
  private void EmitMaxonStrlen() {
    EmitRuntimeFunctionStart("maxon_strlen", 1);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // save start

    var loopLabel = $"__strlen_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__strlen_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    DefineLabel(loopLabel);
    EmitWord(0x39400000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // LDRB W2, [X0]
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x34000000 | Reg(ARM64Register.X2)); // CBZ W2, done
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitBranch(loopLabel);

    DefineLabel(doneLabel);
    // len = X0 - X1
    EmitWord(0xCB000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_to_cstring(buf, len) -> ptr ---
  // Allocates a null-terminated copy
  private void EmitMaxonToCstring() {
    EmitRuntimeFunctionStart("maxon_to_cstring", 2, 0x40);
    EmitReloadArg(0); // buf
    EmitReloadArg(1); // len
    // Allocate len+1 bytes
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0); // no destructor
    EmitMovRegImm(ARM64Register.X2, 0); // no tag
    EmitMovRegImm(ARM64Register.X3, 0); // no scope
    EmitBranchLink("mm_alloc");
    // Save allocated ptr [x29, #32]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    // Copy: memcpy(allocated, buf, len)
    EmitReloadArg(0); // buf -> x1
    EmitReloadArg(1); // len -> x2
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitBranchLink("maxon_memcpy");
    // Null terminate
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitReloadArg(1); // len
    // STRB WZR, [X0, X1]
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2));
    EmitWord(0x39000000 | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.Xzr));
    // Return ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_cow_check(buffer, capacity, byteLen, managedPtr) -> new_buffer ---
  // If capacity != 0, buffer is already writable — return it as-is.
  // If capacity == 0, allocate byteLen bytes via mm_raw_alloc, copy from old buffer, return new buffer.
  // The old rdata buffer is NOT freed (capacity==0 identifies rdata/stack).
  private void EmitMaxonCowCheck() {
    // Args: X0=buffer, X1=capacity, X2=byteLen, X3=managedPtr
    // [x29+16]=buffer, [x29+24]=capacity, [x29+32]=byteLen, [x29+40]=managedPtr
    // [x29+48]=new_buffer (scratch), [x29+56]=scope=NULL (trace only)
    EmitRuntimeFunctionStart("maxon_cow_check", 4, Compiler.MmTrace ? 0x50 : 0x40);

    // Check capacity != 0 → already writable
    EmitReloadArg(1); // X1 = capacity
    // CBNZ X1, rt_cow_writable
    _condBranchFixups.Add((_code.Count, "rt_cow_writable"));
    EmitWord(0xB5000001); // CBNZ X1, <fixup>

    // Check byteLen == 0 → nothing to copy, skip COW
    EmitReloadArg(2); // X2 = byteLen
    // CBZ X2, rt_cow_writable
    _condBranchFixups.Add((_code.Count, "rt_cow_writable"));
    EmitWord(0xB4000002); // CBZ X2, <fixup>

    // COW path: allocate byteLen bytes, copy old buffer
    EmitReloadArg(2); // X2 = byteLen
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2); // X0 = byteLen (arg for mm_raw_alloc)
    EmitBranchLink("mm_raw_alloc");
    // Save new buffer at [x29+48]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8);

    // memcpy(new_buffer, old_buffer, byteLen): X0=dst, X1=src, X2=count
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8); // X0 = new_buffer (dst)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // X1 = old_buffer (src)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = byteLen (count)
    EmitBranchLink("maxon_memcpy");

    // Trace COW copy
    if (Compiler.MmTrace) {
      EmitMovRegImm(ARM64Register.X0, 0);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8); // scope=NULL
      // ptrSlot=40 (managedPtr), scopeSlot=56, sizeSlot=32 (byteLen)
      EmitInlineTrace("__mm_tag_cow", "cow_check_trace", ptrSlot: 40, scopeSlot: 56, sizeSlot: 32);
    }

    // Return new buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    _branchFixups.Add((_code.Count, "rt_cow_epilogue"));
    EmitWord(0x14000000); // B rt_cow_epilogue

    // already_writable: return old buffer
    DefineLabel("rt_cow_writable");
    EmitReloadArg(0); // X0 = buffer
    DefineLabel("rt_cow_epilogue");
    EmitRuntimeFunctionEnd();
  }

  // --- Raw memory functions (using mmap/munmap) ---

  private void EmitMaxonRawAlloc() {
    // mm_raw_alloc(size) -> ptr
    // Allocates size+16 bytes via mmap, stores the mmap'd size at [base],
    // returns base+16 so mm_raw_free can read the size from [ptr-16].
    var allocZeroLabel = $"__raw_alloc_zero_{_uniqueLabelCounter++}";
    EmitRuntimeFunctionStart("mm_raw_alloc", 1, 0x30);
    EmitReloadArg(0); // X0 = requested size

    // size == 0 -> return NULL
    _condBranchFixups.Add((_code.Count, allocZeroLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, zero

    // X1 = size + 16 (for header)
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X0, 16, isAdd: true);
    // Save total size at [x29, #24]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8);
    // mmap(NULL, size+16, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0)
    EmitMovRegImm(ARM64Register.X0, 0); // addr = NULL
    // X1 already = size+16
    EmitMovRegImm(ARM64Register.X2, 3); // PROT_READ | PROT_WRITE
    EmitMovRegImm(ARM64Register.X3, 0x1002); // MAP_ANON | MAP_PRIVATE
    EmitMovRegImm(ARM64Register.X4, -1); // fd = -1
    EmitMovRegImm(ARM64Register.X5, 0); // offset = 0
    EmitCallImport("mmap");
    // X0 = base ptr from mmap. Store total mmap size at [base].
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // total size
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8); // [base] = total size
    // Return base + 16
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 16, isAdd: true);
    EmitRuntimeFunctionEnd();

    DefineLabel(allocZeroLabel);
    EmitMovRegImm(ARM64Register.X0, 0); // return NULL
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonRawRealloc() {
    // mm_raw_realloc(old_ptr, new_size, managedPtr) -> new_ptr
    // [x29+16] = old_ptr (x0)
    // [x29+24] = new_size (x1)
    // [x29+32] = managedPtr (x2)
    // [x29+40] = new_ptr (scratch)
    // [x29+48] = old_byte_size (scratch)
    EmitRuntimeFunctionStart("mm_raw_realloc", 3, Compiler.MmTrace ? 0x50 : 0x40);

    // Step 1: Allocate new buffer via mm_raw_alloc(new_size)
    EmitReloadArg(1); // X1 = new_size
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1); // X0 = new_size (arg for mm_raw_alloc)
    EmitBranchLink("mm_raw_alloc");
    // Save new_ptr at [x29+40]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);

    // Step 2: Compute old_byte_size = managedPtr->capacity * managedPtr->element_size
    EmitReloadArg(2); // X2 = managedPtr
    // X3 = [managedPtr+16] = capacity
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X2, 16, 8);
    // X4 = [managedPtr+24] = element_size
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X2, 24, 8);
    // X3 = capacity * element_size (MUL X3, X3, X4 = MADD X3, X3, X4, XZR)
    EmitWord(0x9B047C63); // MUL X3, X3, X4
    // Save old_byte_size at [x29+48]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 48, 8);

    // Step 3: memcpy(new_ptr, old_ptr, old_byte_size)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // X1 = old_ptr (src)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // X0 = new_ptr (dst)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 48, 8); // X2 = old_byte_size (count)
    EmitBranchLink("maxon_memcpy");

    // Step 4: Free old buffer via mm_raw_free(old_ptr)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // X0 = old_ptr
    EmitBranchLink("mm_raw_free");

    // Return new_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);

    if (Compiler.MmTrace) {
      // Trace: "realloc TypeName #N rc=R size=S" using managedPtr for tag/rc, new_size for size
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save new_ptr
      EmitMovRegImm(ARM64Register.X1, 0);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 56, 8); // scope=NULL
      // ptrSlot=32 (managedPtr), scopeSlot=56, sizeSlot=24 (new_size)
      EmitInlineTrace("__mm_tag_realloc", "mm_raw_realloc_trace", ptrSlot: 32, scopeSlot: 56, sizeSlot: 24);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8); // restore new_ptr
    }

    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonRawFree() {
    // mm_raw_free(ptr)
    // The header at [ptr-16] contains the total mmap'd size.
    // munmap(ptr-16, size)
    var freeNullLabel = $"__raw_free_null_{_uniqueLabelCounter++}";
    EmitRuntimeFunctionStart("mm_raw_free", 1);
    EmitReloadArg(0); // X0 = ptr

    // NULL check
    _condBranchFixups.Add((_code.Count, freeNullLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, null

    // X0 = base = ptr - 16
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 16, isAdd: false);
    // X1 = [base] = total mmap size
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);
    // munmap(base, size)
    EmitCallImport("munmap");
    EmitRuntimeFunctionEnd();

    DefineLabel(freeNullLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- File operations ---

  private void EmitMaxonFileSize() {
    // maxon_file_size(handle) -> i64
    // Use fstat (handle is fd, not path)
    EmitRuntimeFunctionStart("maxon_file_size", 1, 0xC0); // need room for stat struct on stack
    EmitReloadArg(0); // X0 = fd
    // fstat64(fd, &statbuf)
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 0x40, isAdd: true); // stat buf at x29+64
    EmitCallImport("fstat");
    // Check for error
    var failLabel = $"__fsize_fail_{_uniqueLabelCounter}";
    var okLabel = $"__fsize_ok_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    EmitBranchOnLibcError(failLabel);
    _branchFixups.Add((_code.Count, okLabel));
    EmitWord(0x14000000); // B ok
    DefineLabel(failLabel);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitRuntimeFunctionEnd();
    DefineLabel(okLabel);
    // st_size is at offset 96 in macOS stat64 struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 0x40 + 96, 8);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonFileRead() {
    // maxon_file_read(handle, buffer, size, capacity) -> bytes_read
    // Clamps size to capacity, then calls read(fd, buf, clampedSize)
    EmitRuntimeFunctionStart("maxon_file_read", 4, 0x40);

    // Clamp size to capacity: if size > capacity, use capacity
    EmitReloadArg(2); // X2 = size
    EmitReloadArg(3); // X3 = capacity
    // CMP X2, X3
    EmitWord(0xEB03005F | (Reg(ARM64Register.X2) << 5)); // CMP X2, X3 (SUBS XZR, X2, X3)
    var clampOk = $"__fread_clamp_ok_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, clampOk));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ls)); // B.LS (unsigned <=)
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X3); // size = capacity
    DefineLabel(clampOk);

    // read(fd, buf, clampedSize): X0=fd, X1=buf, X2=size
    // X2 already has clamped size, save it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8); // save clamped size
    EmitReloadArg(1); // X1 = buffer
    EmitReloadArg(0); // X0 = handle (fd)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = clamped size
    EmitCallImport("read");

    var errorLabel = $"__fread_err_{_uniqueLabelCounter}";
    var doneLabel = $"__fread_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    EmitBranchOnLibcError(errorLabel);
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000); // B done

    DefineLabel(errorLabel);
    EmitMovRegImm(ARM64Register.X0, 0); // return 0 on error (match X86 behavior)

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonFileClose() {
    EmitRuntimeFunctionStart("maxon_file_close", 1);
    EmitReloadArg(0); // X0 = handle (fd)
    var doneLabel = $"__fclose_noop_{_uniqueLabelCounter++}";
    // Skip if handle is 0
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ done
    EmitCallImport("close");
    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonFileDelete() {
    EmitRuntimeFunctionStart("maxon_file_delete", 1, 0x30);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpFileDelete);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // --- Managed File I/O ---

  // __ManagedFile layout: [+0] = fd (i64), total 8 bytes
  // O_WRONLY=1, O_CREAT=0x200, O_TRUNC=0x400
  private const long O_WRONLY_CREAT_TRUNC = 0x601; // O_WRONLY | O_CREAT | O_TRUNC

  // maxon_managed_file_open_read(cstring_path) -> managed file ptr or -1
  // Delegates open() to __io_submit_sync(SyncOpFileOpenRead, path, 0), then allocs ManagedFile.
  private void EmitManagedFileOpenRead() {
    EmitRuntimeFunctionStart("maxon_managed_file_open_read", 1, 0x30);
    EmitReloadArg(0); // X0 = path
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpFileOpenRead);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");

    // X0 = fd or -1
    var failLabel = $"__fopen_read_fail_{_uniqueLabelCounter}";
    var doneLabel = $"__fopen_read_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // Check for failure (fd == -1 means unsigned max)
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X0, 1, isAdd: true);
    EmitCbz(ARM64Register.X1, failLabel); // if fd+1 == 0, fd was -1

    // Save fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // Allocate __ManagedFile struct (8 bytes)
    EmitMovRegImm(ARM64Register.X0, 8);
    EmitAdrpAddFixup(ARM64Register.X1, _funcAddrAdrpFixups, "__destruct___ManagedFile");
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("mm_alloc");

    // Store fd at [file_ptr + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    EmitBranch(doneLabel);

    DefineLabel(failLabel);
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_managed_file_open_write(cstring_path) -> managed file ptr or -1
  // Delegates open() to __io_submit_sync(SyncOpFileOpenWrite, path, 0), then allocs ManagedFile.
  private void EmitManagedFileOpenWrite() {
    EmitRuntimeFunctionStart("maxon_managed_file_open_write", 1, 0x30);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpFileOpenWrite);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");

    // X0 = fd or -1
    var failLabel = $"__fopen_write_fail_{_uniqueLabelCounter}";
    var doneLabel = $"__fopen_write_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitAddSubImm(ARM64Register.X1, ARM64Register.X0, 1, isAdd: true);
    EmitCbz(ARM64Register.X1, failLabel);

    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save fd

    EmitMovRegImm(ARM64Register.X0, 8);
    EmitAdrpAddFixup(ARM64Register.X1, _funcAddrAdrpFixups, "__destruct___ManagedFile");
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("mm_alloc");

    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    EmitBranch(doneLabel);

    DefineLabel(failLabel);
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_managed_file_write(handle, buffer, length) -> bytes written or -1
  private void EmitManagedFileWrite() {
    EmitRuntimeFunctionStart("maxon_managed_file_write", 3, 0x30);
    EmitReloadArg(0); // X0 = fd (raw handle)
    EmitReloadArg(1); // X1 = buffer
    EmitReloadArg(2); // X2 = length

    EmitCallImport("write");

    var errorLabel = $"__fwrite_err_{_uniqueLabelCounter}";
    var doneLabel = $"__fwrite_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitBranchOnLibcError(errorLabel);
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000);

    DefineLabel(errorLabel);
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_managed_file_read(handle, buffer, length) -> bytes read or -1
  private void EmitManagedFileRead() {
    EmitRuntimeFunctionStart("maxon_managed_file_read", 3, 0x30);
    EmitReloadArg(0); // X0 = fd (raw handle)
    EmitReloadArg(1); // X1 = buffer
    EmitReloadArg(2); // X2 = length

    EmitCallImport("read");

    var errorLabel = $"__fread_err_{_uniqueLabelCounter}";
    var doneLabel = $"__fread_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitBranchOnLibcError(errorLabel);
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000);

    DefineLabel(errorLabel);
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_managed_file_close(handle_ptr)
  // Delegates close() to __io_submit_sync(SyncOpCloseHandle, fd, 0).
  private void EmitManagedFileClose() {
    EmitRuntimeFunctionStart("maxon_managed_file_close", 1, 0x30);
    EmitReloadArg(0); // X0 = fd (raw handle)

    var doneLabel = $"__fclose_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // Skip if fd <= 0
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le));

    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = fd (arg0)
    EmitMovRegImm(ARM64Register.X0, SyncOpCloseHandle); // X0 = op
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // __destruct___ManagedFile(user_ptr)
  private void EmitFileDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedFile", 1, 0x30);
    EmitReloadArg(0); // X0 = user_ptr

    var doneLabel = $"__dtor_file_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // Load fd from [user_ptr + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    // Zero the fd for idempotency
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 0, 8);

    // If fd <= 0, skip close
    EmitWord(0xF100003F | (Reg(ARM64Register.X1) << 5)); // CMP X1, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le));

    // close(fd)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitCallImport("close");

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- Command line functions ---

  // maxon_command_line_count() -> argc (including argv[0])
  private void EmitMaxonCommandLineCount() {
    DefineLabel("maxon_command_line_count");
    // Frameless leaf: load argc from global and return
    EmitAdrpAddFixup(ARM64Register.X0, _globalAdrpFixups, "__argc_global");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 0, 8); // LDR X0, [X0]
    EmitWord(0xD65F03C0); // RET
  }

  // maxon_command_line_arg(index) -> heap-allocated C string copy of argv[index]
  // Stack: arg0 at [x29+16], [x29+24]=argv_str, [x29+32]=alloc_size
  private void EmitMaxonCommandLineArg() {
    EmitRuntimeFunctionStart("maxon_command_line_arg", 1, 0x50);
    EmitReloadArg(0); // X0 = index

    var emptyLabel = $"__cla_empty_{_uniqueLabelCounter}";
    var doneLabel = $"__cla_done_{_uniqueLabelCounter}";
    var lenLoopLabel = $"__cla_strlen_{_uniqueLabelCounter}";
    var lenDoneLabel = $"__cla_len_done_{_uniqueLabelCounter}";
    var copyLoopLabel = $"__cla_copy_{_uniqueLabelCounter}";
    var copyDoneLabel = $"__cla_copy_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // Load argc, bounds check
    EmitAdrpAddFixup(ARM64Register.X1, _globalAdrpFixups, "__argc_global");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X1, 0, 8); // X1 = argc
    // CMP X0, X1 — if index >= argc, return empty
    EmitWord(0xEB01001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, X1
    _condBranchFixups.Add((_code.Count, emptyLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE empty

    // Load argv[index]: argv base + index*8
    EmitAdrpAddFixup(ARM64Register.X1, _globalAdrpFixups, "__argv_global");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X1, 0, 8); // X1 = argv
    // LSL X0, X0, #3 = UBFM X0, X0, #61, #60
    EmitWord(0xD37DF000 | Reg(ARM64Register.X0) | (Reg(ARM64Register.X0) << 5));
    // LDR X2, [X1, X0] — register offset
    EmitWord(0xF8606820 | (Reg(ARM64Register.X0) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    // X2 = argv[index] C string
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 24, 8); // save argv_str at [x29+24]

    // strlen: scan until null
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X2); // X3 = scan ptr
    DefineLabel(lenLoopLabel);
    EmitWord(0x38401464 | (Reg(ARM64Register.X3) << 5)); // LDRB W4, [X3], #1
    _condBranchFixups.Add((_code.Count, lenDoneLabel));
    EmitWord(0x34000000 | Reg(ARM64Register.W4)); // CBZ W4, len_done
    _branchFixups.Add((_code.Count, lenLoopLabel));
    EmitWord(0x14000000); // B strlen_loop
    DefineLabel(lenDoneLabel);

    // X3 = one past null, length = X3 - X2 (includes null)
    EmitWord(0xCB020060 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X0)); // SUB X0, X3, X2
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // save alloc_size at [x29+32]

    // Allocate buffer via mm_alloc (so mm_free can reclaim it properly)
    // mm_alloc(size, destructor=0, tag=0, scope=0)
    EmitMovRegImm(ARM64Register.X1, 0); // destructor = 0
    EmitMovRegImm(ARM64Register.X2, 0); // tag = 0
    EmitMovRegImm(ARM64Register.X3, 0); // scope = 0
    EmitBranchLink("mm_alloc"); // X0 = managed buffer
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save dest ptr at [x29+40]

    // Copy argv_str to new buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // X1 = src
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = length

    DefineLabel(copyLoopLabel);
    _condBranchFixups.Add((_code.Count, copyDoneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, copy_done
    EmitWord(0x38401424 | (Reg(ARM64Register.X1) << 5)); // LDRB W4, [X1], #1
    EmitWord(0x38001404 | (Reg(ARM64Register.X0) << 5)); // STRB W4, [X0], #1
    EmitWord(0xD1000442 | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X2)); // SUB X2, X2, #1
    _branchFixups.Add((_code.Count, copyLoopLabel));
    EmitWord(0x14000000); // B copy_loop
    DefineLabel(copyDoneLabel);

    // Return saved dest ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // X0 = dest
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000); // B done

    DefineLabel(emptyLabel);
    // Allocate 1-byte empty string via mm_alloc
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitMovRegImm(ARM64Register.X1, 0); // destructor = 0
    EmitMovRegImm(ARM64Register.X2, 0); // tag = 0
    EmitMovRegImm(ARM64Register.X3, 0); // scope = 0
    EmitBranchLink("mm_alloc");
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitWord(0x39000001 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB W1, [X0, #0]

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_directory_exists(cstring_path) -> 1 if directory, 0 otherwise ---
  // Delegates to __io_submit_sync(SyncOpDirExists, path, 0).
  private void EmitMaxonDirectoryExists() {
    EmitRuntimeFunctionStart("maxon_directory_exists", 1, 0x30);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpDirExists);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_file_exists(cstring_path) -> 1 if file exists (not directory), 0 otherwise ---
  // Delegates to __io_submit_sync(SyncOpFileExists, path, 0).
  private void EmitMaxonFileExists() {
    EmitRuntimeFunctionStart("maxon_file_exists", 1, 0x30);
    EmitReloadArg(0); // X0 = path
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = path (arg0)
    EmitMovRegImm(ARM64Register.X0, SyncOpFileExists);  // X0 = op
    EmitMovRegImm(ARM64Register.X2, 0);                  // X2 = arg1
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_create_directory(cstring_path) -> nonzero on success, 0 on failure ---
  // Delegates to __io_submit_sync(SyncOpDirCreate, path, 0).
  private void EmitMaxonCreateDirectory() {
    EmitRuntimeFunctionStart("maxon_create_directory", 1, 0x30);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpDirCreate);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_get_current_directory() -> cstring pointer ---
  // Delegates to __io_submit_sync(SyncOpGetCwd, 0, 0).
  // The dispatch handler does open(".")+fcntl(F_GETPATH)+alloc+copy.
  private void EmitMaxonGetCurrentDirectory() {
    DefineSymdata("__dot_path", [(byte)'.', (byte)0]);
    EmitRuntimeFunctionStart("maxon_get_current_directory", 0, 0x30);
    EmitMovRegImm(ARM64Register.X0, SyncOpGetCwd);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // macOS directory search block layout (used by managed dir search):
  // offset 0:  fd (8 bytes) - file descriptor from open()
  // offset 8:  buf_offset (8 bytes) - current offset within read buffer
  // offset 16: buf_valid (8 bytes) - bytes of valid data in buffer
  // offset 24: basep (8 bytes) - base for getdirentries64
  // offset 32: d_name_buf (256 bytes) - copy of current entry filename
  // offset 288: read_buf (4096 bytes) - getdirentries64 read buffer
  // Total: 4384 bytes → round up to 4384

  private const int DirBlockFd = 0;
  private const int DirBlockBufOffset = 8;
  private const int DirBlockBufValid = 16;
  private const int DirBlockBasep = 24;
  private const int DirBlockNameBuf = 32;
  private const int DirBlockReadBuf = 288;
  private const int DirBlockSize = 4384;

  // macOS dirent64 struct offsets
  private const int DirentIno = 0;     // d_ino, 8 bytes
  private const int DirentSeekoff = 8; // d_seekoff, 8 bytes
  private const int DirentReclen = 16; // d_reclen, 2 bytes
  private const int DirentNamelen = 18; // d_namlen, 2 bytes
  private const int DirentType = 20;   // d_type, 1 byte
  private const int DirentName = 21;   // d_name, variable

  // --- maxon_managed_dir_open_search(pattern_cstring) -> block_ptr or 0 ---
  // On macOS: strips trailing "/*" or "\*" from pattern, opens directory with open(),
  // does initial getdirentries64 read, skips "." and "..".
  private void EmitMaxonManagedDirOpenSearch() {
    EmitRuntimeFunctionStart("maxon_managed_dir_open_search", 1, 0x40);
    EmitReloadArg(0); // X0 = pattern cstring

    // Save pattern pointer
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // Strip trailing "/*" or "\*" from pattern
    // Find length of pattern first
    EmitBranchLink("maxon_strlen"); // X0 = len
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // save len

    // Check if len >= 2 and last two chars are "/*" or "\*"
    var noStripLabel = $"__dir_nostrip_{_uniqueLabelCounter}";
    var stripDoneLabel = $"__dir_stripdone_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitMovRegImm(ARM64Register.X1, 2);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP len, 2
    _condBranchFixups.Add((_code.Count, noStripLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lo)); // B.LO nostrip

    // Load last char (pattern[len-1])
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // pattern
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // len
    EmitAluRegReg(0xCB000000, ARM64Register.X3, ARM64Register.X2, ARM64Register.Xzr); // X3 = len
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: false); // X3 = len-1
    // LDRB W4, [X1, X3]
    EmitWord(0x38606800 | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X4));

    // Check if last char is '*' (42)
    EmitMovRegImm(ARM64Register.X5, 42);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X5) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP last, '*'
    _condBranchFixups.Add((_code.Count, noStripLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE nostrip

    // Null-terminate at len-2 (stripping the last 2 chars: separator + '*')
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X2, 2, isAdd: false); // X3 = len-2
    // STRB WZR, [X1, X3]
    EmitWord(0x38206800 | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.Xzr));
    _branchFixups.Add((_code.Count, stripDoneLabel));
    EmitWord(0x14000000); // B stripdone

    DefineLabel(noStripLabel);
    DefineLabel(stripDoneLabel);

    // Open directory: open(path, O_RDONLY|O_DIRECTORY)
    // O_RDONLY = 0, O_DIRECTORY = 0x100000
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // path
    EmitMovRegImm(ARM64Register.X1, 0x100000); // O_RDONLY | O_DIRECTORY
    EmitMovRegImm(ARM64Register.X2, 0); // mode (unused for open)
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("open");
    EmitVariadicCleanup();

    // Check if open failed
    var openFailLabel = $"__dir_openfail_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitBranchOnLibcError(openFailLabel);

    // Save fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Allocate block via mm_alloc
    EmitMovRegImm(ARM64Register.X0, DirBlockSize);
    EmitMovRegImm(ARM64Register.X1, 0); // no destructor
    EmitMovRegImm(ARM64Register.X2, 0); // no tag
    EmitBranchLink("mm_alloc");

    // Save block ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // Initialize block: set fd, zero out buf_offset and buf_valid
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // fd
    // STR fd, [block + DirBlockFd]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockFd, 8);
    // Zero out buf_offset, buf_valid, basep
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockBufOffset, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockBufValid, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockBasep, 8);

    // Now allocate the __ManagedDirectory struct (8 bytes: one field = block_ptr)
    EmitMovRegImm(ARM64Register.X0, 8);
    EmitAdrpAddFixup(ARM64Register.X1, _funcAddrAdrpFixups, "__destruct___ManagedDirectory");
    EmitMovRegImm(ARM64Register.X2, 0); // no tag
    EmitBranchLink("mm_alloc");

    // Store block_ptr at [dir_ptr + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // block_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    // Save dir_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Do initial read: call maxon_find_next_file(block_ptr) to populate first entry
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // block_ptr
    EmitBranchLink("maxon_find_next_file");

    // Restore and return dir_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Return dir_ptr
    _branchFixups.Add((_code.Count, $"__dir_open_done_{_uniqueLabelCounter}"));
    EmitWord(0x14000000);

    DefineLabel(openFailLabel);
    EmitMovRegImm(ARM64Register.X0, 0);

    DefineLabel($"__dir_open_done_{_uniqueLabelCounter}");
    _uniqueLabelCounter++;
    EmitRuntimeFunctionEnd();
  }

  // --- __destruct___ManagedDirectory(ptr) ---
  // Destructor: closes fd and frees block
  private void EmitDestructManagedDirectory() {
    EmitRuntimeFunctionStart("__destruct___ManagedDirectory", 1, 0x30);
    EmitReloadArg(0); // X0 = user_ptr

    // Load block_ptr = [user_ptr + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    var doneLabel = $"__dtor_dir_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // If block_ptr == 0, skip
    EmitWord(0xF100003F | (Reg(ARM64Register.X1) << 5)); // CMP X1, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ done

    // Save block_ptr and user_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // save block
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save user_ptr

    // Close fd: close([block + 0])
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X1, DirBlockFd, 8);
    var skipCloseLabel = $"__dtor_dir_skipclose_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, skipCloseLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE skip (fd <= 0 means invalid)
    EmitCallImport("close");
    DefineLabel(skipCloseLabel);

    // Decref block
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitBranchLink("mm_decref");

    // Zero block field in user struct for idempotency
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_dir_close(block_ptr) ---
  // Close the fd in the block and zero it
  private void EmitMaxonManagedDirClose() {
    EmitRuntimeFunctionStart("maxon_managed_dir_close", 1, 0x30);
    EmitReloadArg(0); // X0 = block_ptr

    var doneLabel = $"__dirclose_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ done

    // Save block ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // Load and close fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, DirBlockFd, 8);
    var skipLabel = $"__dirclose_skip_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, skipLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE skip
    EmitCallImport("close");
    DefineLabel(skipLabel);

    // Zero the fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockFd, 8);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_find_next_file(block_ptr) -> nonzero if found, 0 if done ---
  // Reads next directory entry from block, skipping "." and "..".
  // Copies filename to block's name buffer.
  private void EmitMaxonFindNextFile() {
    EmitRuntimeFunctionStart("maxon_find_next_file", 1, 0x40);
    EmitReloadArg(0); // X0 = block_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save block

    var retryLabel = $"__findnext_retry_{_uniqueLabelCounter}";
    var readMoreLabel = $"__findnext_readmore_{_uniqueLabelCounter}";
    var foundLabel = $"__findnext_found_{_uniqueLabelCounter}";
    var doneLabel = $"__findnext_done_{_uniqueLabelCounter}";
    var eofLabel = $"__findnext_eof_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    DefineLabel(retryLabel);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // block

    // Check if buf_offset >= buf_valid (need to read more)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, DirBlockBufOffset, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, DirBlockBufValid, 8);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5)); // CMP offset, valid
    _condBranchFixups.Add((_code.Count, readMoreLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE readmore

    // Have data: parse current dirent
    // entry_ptr = block + DirBlockReadBuf + buf_offset
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, DirBlockBufOffset, 8);
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X0, DirBlockReadBuf, isAdd: true); // X3 = &read_buf
    EmitAluRegReg(0x8B000000, ARM64Register.X3, ARM64Register.X3, ARM64Register.X1); // X3 = entry_ptr

    // Read d_reclen (uint16_t at offset 16)
    EmitWord(0x79400000 | ((DirentReclen / 2) << 10) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4));

    // Advance buf_offset += d_reclen
    EmitAluRegReg(0x8B000000, ARM64Register.X5, ARM64Register.X1, ARM64Register.X4); // new offset
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X0, DirBlockBufOffset, 8);

    // Get d_name pointer = entry_ptr + 21
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X3, DirentName, isAdd: true);

    // Skip "." and ".." entries
    // Load first byte
    EmitWord(0x39400000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X6]
    EmitMovRegImm(ARM64Register.X8, 0x2E); // '.'
    EmitWord(0xEB00001F | (Reg(ARM64Register.X8) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP byte, '.'
    _condBranchFixups.Add((_code.Count, foundLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE found (not a dot entry)

    // First char is '.', check second char
    EmitWord(0x39400400 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X6, #1]
    // If second char is 0, it's "." → skip
    EmitWord(0xF100001F | (Reg(ARM64Register.X7) << 5)); // CMP byte2, #0
    _condBranchFixups.Add((_code.Count, retryLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ retry

    // If second char is '.', check third
    EmitWord(0xEB00001F | (Reg(ARM64Register.X8) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP byte2, '.'
    _condBranchFixups.Add((_code.Count, foundLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE found (not "..")

    // Second is '.', check third char
    EmitWord(0x39400800 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X6, #2]
    EmitWord(0xF100001F | (Reg(ARM64Register.X7) << 5)); // CMP byte3, #0
    _condBranchFixups.Add((_code.Count, retryLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ retry (it's "..")

    // It's a name starting with ".." but not ".." itself → it's a valid entry
    _branchFixups.Add((_code.Count, foundLabel));
    EmitWord(0x14000000);

    DefineLabel(foundLabel);
    // Copy filename to name buffer in block
    // dest = block + DirBlockNameBuf, src = X6 (d_name), len = d_namlen
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // block
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, DirBlockNameBuf, isAdd: true); // dest
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X6); // src = d_name
    // Read d_namlen (uint16_t at dirent offset 18)
    EmitWord(0x79400000 | ((DirentNamelen / 2) << 10) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X2));
    // Include null terminator
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: true);
    EmitBranchLink("maxon_memcpy");

    // Return 1 (found)
    EmitMovRegImm(ARM64Register.X0, 1);
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000);

    DefineLabel(readMoreLabel);
    // Need to read more: getdirentries64(fd, buf, bufsize, &basep)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // block
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, DirBlockFd, 8); // fd

    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 24, 8); // block (temp)
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X3, DirBlockReadBuf, isAdd: true); // buf
    EmitMovRegImm(ARM64Register.X2, 4096); // bufsize
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, DirBlockBasep, isAdd: true); // &basep

    EmitCallImport("__getdirentries64");

    // Check for error (carry set) or EOF (X0 = 0)
    EmitBranchOnLibcError(eofLabel);
    // Check for EOF: X0 = 0 bytes read
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, eofLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ eof

    // Update buf_valid and reset buf_offset
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // block
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X1, DirBlockBufValid, 8);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X1, DirBlockBufOffset, 8);

    // Retry with new data
    _branchFixups.Add((_code.Count, retryLabel));
    EmitWord(0x14000000);

    DefineLabel(eofLabel);
    EmitMovRegImm(ARM64Register.X0, 0); // no more entries

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_find_filename(block_ptr) -> cstring pointer to filename ---
  // Returns pointer to the name buffer within the block.
  private void EmitMaxonFindFilename() {
    DefineLabel("maxon_find_filename");
    // If block_ptr is null, return empty string
    var validLabel = $"__findname_valid_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, validLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE valid

    // Return empty string
    DefineSymdata("__empty_cstr", [(byte)0]);
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__empty_cstr");
    EmitWord(0xD65F03C0); // RET

    DefineLabel(validLabel);
    // Return block + DirBlockNameBuf
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, DirBlockNameBuf, isAdd: true);
    EmitWord(0xD65F03C0); // RET
  }

  // ============================================================================
  // Green Thread Runtime for async/await (ARM64/macOS)
  // ============================================================================

  // GreenThread struct offsets (same layout as x86 for consistency)
  private const int GtOffSp         = 0x00;
  private const int GtOffFp         = 0x08;
  private const int GtOffStatus     = 0x10;
  private const int GtOffStackBase  = 0x18;
  private const int GtOffStackSize  = 0x20;
  private const int GtOffResult     = 0x28;
  private const int GtOffWaiter     = 0x30;
  private const int GtOffNext       = 0x38;
  private const int GtOffFuncPtr    = 0x40;
  private const int GtOffArgBuf     = 0x48;
  private const int GtOffStackGuard = 0x50;
  private const int GtOffThrew      = 0x58;
  private const int GtOffCancelFlag = 0x78;
  private const int GtOffAllNext    = 0x88;
  private const int GtOffTraceId    = 0xA0;
  private const int GtStructSize    = 0xA8; // 168 bytes

  private const int GtStatusReady    = 0;
  private const int GtStatusRunning  = 1;
  private const int GtStatusCompleted = 2;
  private const int GtStatusWaiting  = 3;

  private const int GtInitialStackSize = 0x10000; // 64KB

  // I/O result fields in GreenThread struct (used by async I/O)
  private const int GtOffIoResultVal = 0x60; // raw result value
  private const int GtOffIoResultLen = 0x68; // byte count (unused on macOS, reserved)
  private const int GtOffIoErrorCode = 0x70; // 0=success, non-zero=error

  // SyncRequest layout (40 bytes) — queued I/O operations
  private const int SyncReqSize      = 0x28; // 40 bytes
  private const int SyncReqOffOp     = 0x00;
  private const int SyncReqOffArg0   = 0x08;
  private const int SyncReqOffArg1   = 0x10;
  private const int SyncReqOffWaiter = 0x18;
  private const int SyncReqOffNext   = 0x20;

  // Sync op codes (must match x86 values)
  private const long SyncOpFileExists    = 0;
  private const long SyncOpFileDelete    = 1;
  private const long SyncOpDirExists     = 4;
  private const long SyncOpDirCreate     = 5;
  private const long SyncOpGetCwd        = 6;
  private const long SyncOpFileOpenRead  = 7;
  private const long SyncOpFileOpenWrite = 8;
  private const long SyncOpCloseHandle   = 9;
  private const long SyncOpNetConnect    = 10;
  private const long SyncOpNetSend       = 11;
  private const long SyncOpNetRecv       = 12;
  private const long SyncOpNetClose      = 13;


  private void EmitGreenThreadRuntime() {
    // Define scheduler globals
    DefineGlobal("__gt_main_thread", GtStructSize, 0);
    DefineGlobal("__gt_current", 8, 0);
    DefineGlobal("__gt_run_queue_head", 8, 0);
    DefineGlobal("__gt_run_queue_tail", 8, 0);
    DefineGlobal("__gt_live_count", 8, 0);
    DefineGlobal("__gt_all_head", 8, 0);

    // I/O request queue globals
    DefineGlobal("__io_sync_req_head", 8, 0);
    DefineGlobal("__io_sync_req_tail", 8, 0);

    if (Compiler.AsyncTrace) {
      DefineGlobal("__gt_trace_counter", 8, 0);
      DefineSymdata("__at_tag_spawn", "spawn #\0"u8.ToArray());
      DefineSymdata("__at_tag_await", "await #\0"u8.ToArray());
      DefineSymdata("__at_tag_await_yield", " [yield]\0"u8.ToArray());
      DefineSymdata("__at_tag_await_imm", " [immediate]\0"u8.ToArray());
      DefineSymdata("__at_tag_try_await", "try_await #\0"u8.ToArray());
      DefineSymdata("__at_tag_cancel", "cancel #\0"u8.ToArray());
      DefineSymdata("__at_tag_nl", "\n\0"u8.ToArray());
      DefineSymdata("__at_tag_io_yield", "io_yield #\0"u8.ToArray());
      DefineSymdata("__at_tag_io_resume", "io_resume #\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_exists", " [file_exists]\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_delete", " [file_delete]\0"u8.ToArray());
      DefineSymdata("__at_io_op_dir_exists", " [dir_exists]\0"u8.ToArray());
      DefineSymdata("__at_io_op_dir_create", " [dir_create]\0"u8.ToArray());
      DefineSymdata("__at_io_op_get_cwd", " [get_cwd]\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_open_read", " [file_open_read]\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_open_write", " [file_open_write]\0"u8.ToArray());
      DefineSymdata("__at_io_op_close_handle", " [close_handle]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_connect", " [net_connect]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_send", " [net_send]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_recv", " [net_recv]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_close", " [net_close]\0"u8.ToArray());
    }

    EmitGtInit();
    EmitGtEnqueue();
    EmitGtDequeue();
    EmitGtSpawn();
    EmitGtTrampoline();
    EmitGtContextSwitch();
    EmitGtAwait();
    EmitGtTryAwait();
    EmitGtYield();
    EmitGtCancel();
    EmitGtCleanup();
    EmitIoRuntime();
  }

  /// <summary>
  /// __gt_init(): Initialize the main thread's GreenThread struct.
  /// Called from _start before main. Sets status=running, stores to __gt_current.
  /// </summary>
  private void EmitGtInit() {
    EmitRuntimeFunctionStart("__gt_init", 0, 0x30);

    // Load address of __gt_main_thread
    EmitGlobalLeaReg(ARM64Register.X0, "__gt_main_thread");
    // Set status = running (1)
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    // stackguard is already 0 (main thread's stack check always passes)

    // Store to __gt_current
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_current");

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_enqueue(gt_x0): Add a GreenThread to the tail of the run queue.
  /// </summary>
  private void EmitGtEnqueue() {
    EmitRuntimeFunctionStart("__gt_enqueue", 1, 0x30);
    // [x29+16] = gt (arg 0)

    // gt.next = 0
    EmitReloadArg(0); // X0 = gt
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffNext, 8);

    // if tail == NULL: head = tail = gt
    EmitGlobalLoadReg(ARM64Register.X1, "__gt_run_queue_tail");
    EmitCbnz(ARM64Register.X1, "__gt_enqueue_append");

    // Empty queue: set both head and tail
    EmitReloadArg(0);
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_run_queue_head");
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_run_queue_tail");
    EmitRuntimeFunctionEnd();

    DefineLabel("__gt_enqueue_append");
    // tail.next = gt
    EmitGlobalLoadReg(ARM64Register.X1, "__gt_run_queue_tail");
    EmitReloadArg(0); // X0 = gt
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X1, GtOffNext, 8);
    // tail = gt
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_run_queue_tail");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_dequeue() -> GreenThread* in X0 (or NULL if queue empty).
  /// </summary>
  private void EmitGtDequeue() {
    EmitRuntimeFunctionStart("__gt_dequeue", 0, 0x30);

    EmitGlobalLoadReg(ARM64Register.X0, "__gt_run_queue_head");
    // If head == NULL, return NULL
    EmitCbnz(ARM64Register.X0, "__gt_dequeue_nonempty");
    EmitRuntimeFunctionEnd();

    DefineLabel("__gt_dequeue_nonempty");
    // new_head = head.next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffNext, 8);
    EmitGlobalStoreReg(ARM64Register.X1, "__gt_run_queue_head");
    // If new head == NULL, clear tail too
    EmitCbnz(ARM64Register.X1, "__gt_dequeue_done");
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitGlobalStoreReg(ARM64Register.X1, "__gt_run_queue_tail");
    DefineLabel("__gt_dequeue_done");
    // Clear the dequeued node's next pointer
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffNext, 8);
    // X0 = dequeued gt
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_spawn(func_ptr_x0, arg_count_x1, arg_buf_x2) -> promise in X0
  /// Allocates a GreenThread struct and a 64KB stack, initializes them,
  /// enqueues the thread, and returns the GreenThread ptr.
  /// </summary>
  private void EmitGtSpawn() {
    EmitRuntimeFunctionStart("__gt_spawn", 3, 0x50);
    // [x29+16] = func_ptr, [x29+24] = arg_count, [x29+32] = arg_buf
    // [x29+40] = gt_ptr (local), [x29+48] = stack_base (local)

    // Allocate GreenThread struct via mm_raw_alloc
    EmitMovRegImm(ARM64Register.X0, GtStructSize);
    EmitBranchLink("mm_raw_alloc");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save gt_ptr

    // Allocate stack via mmap(NULL, 64KB, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0)
    EmitMovRegImm(ARM64Register.X0, 0);                    // addr = NULL
    EmitMovRegImm(ARM64Register.X1, GtInitialStackSize);    // length = 64KB
    EmitMovRegImm(ARM64Register.X2, 3);                     // prot = PROT_READ|PROT_WRITE
    EmitMovRegImm(ARM64Register.X3, 0x1002);                // flags = MAP_ANON|MAP_PRIVATE
    EmitMovRegImm(ARM64Register.X4, -1);                    // fd = -1
    EmitMovRegImm(ARM64Register.X5, 0);                     // offset = 0
    EmitCallImport("mmap");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save stack_base

    // Initialize GreenThread fields
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // X9 = gt_ptr

    // gt.stack_base = stack_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStackBase, 8);

    // gt.stack_size = GtInitialStackSize
    EmitMovRegImm(ARM64Register.X0, GtInitialStackSize);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStackSize, 8);

    // gt.stackguard = stack_base + 0x4000 (16KB guard zone)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 0x4000, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStackGuard, 8);

    // gt.func_ptr = func_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // reload arg0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffFuncPtr, 8);

    // gt.arg_buf = arg_buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // reload arg2
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffArgBuf, 8);

    // gt.status = ready (0), gt.result = 0, gt.waiter = 0, gt.next = 0, gt.threw = 0
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffResult, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffWaiter, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffNext, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffThrew, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffCancelFlag, 8);

    // Initialize stack: compute stack_top = stack_base + stack_size
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, 48, 8); // stack_base
    EmitAddSubImm(ARM64Register.X10, ARM64Register.X10, GtInitialStackSize, isAdd: true); // X10 = stack_top

    // Context switch expects the stack to have callee-saved regs + FP/LR in this order:
    //   [SP+0]   X29, X30 (LR = __gt_trampoline)
    //   [SP+16]  D14, D15
    //   [SP+32]  D12, D13
    //   [SP+48]  D10, D11
    //   [SP+64]  D8,  D9
    //   [SP+80]  X27, X28
    //   [SP+96]  X25, X26
    //   [SP+112] X23, X24
    //   [SP+128] X21, X22
    //   [SP+144] X19, X20
    // Total = 160 bytes. SP = stack_top - 160.

    // Zero all 160 bytes at top of stack
    EmitAddSubImm(ARM64Register.X11, ARM64Register.X10, 160, isAdd: false); // X11 = stack_top - 160
    EmitMovRegImm(ARM64Register.X0, 0);
    for (int i = 0; i < 20; i++) {
      // STR XZR, [X11, #i*8]
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X11, i * 8, 8);
    }

    // Store __gt_trampoline address at [stack_top - 160 + 8] (the X30/LR slot)
    EmitAdrpAddFixup(ARM64Register.X0, _funcAddrAdrpFixups, "__gt_trampoline");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X11, 8, 8); // LR slot

    // gt.sp = stack_top - 160
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // reload gt_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X9, GtOffSp, 8);

    // gt.fp = 0 (no frame pointer yet)
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffFp, 8);

    // Add to all-threads list: gt.all_next = __gt_all_head; __gt_all_head = gt
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_all_head");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffAllNext, 8);
    EmitGlobalStoreReg(ARM64Register.X9, "__gt_all_head");

    // Increment live thread count
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_live_count");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_live_count");

    // Enqueue: __gt_enqueue(gt)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitBranchLink("__gt_enqueue");

    // Return gt_ptr as the promise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);

    if (Compiler.AsyncTrace) {
      // Save gt_ptr
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8);
      // Assign trace ID: counter++
      EmitGlobalLoadReg(ARM64Register.X1, "__gt_trace_counter");
      EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
      EmitGlobalStoreReg(ARM64Register.X1, "__gt_trace_counter");
      // Store trace ID in gt
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // gt_ptr
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X9, GtOffTraceId, 8);
      // Trace: "spawn #N\n"
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_spawn");
      EmitBranchLink("mm_trace_print_tag");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
      // Restore gt_ptr
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    }

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_trampoline(): Entry point for new green threads.
  /// Entered via context switch RET. Loads target function + args, calls it,
  /// stores result, and yields.
  /// </summary>
  private void EmitGtTrampoline() {
    DefineLabel("__gt_trampoline");
    // No standard prologue — we are entered via context switch LDP/RET
    // Set up a frame for local use
    // STP x29, x30, [sp, #-0x70]!
    var frameSize = 0x70; // 112 bytes
    var imm7 = unchecked((uint)(-frameSize / 8)) & 0x7Fu;
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // Load current GreenThread
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X29, 16, 8); // [x29+16] = gt

    // Load func_ptr and arg_buf from GreenThread
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, GtOffFuncPtr, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X10, ARM64Register.X29, 24, 8); // [x29+24] = func_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, GtOffArgBuf, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X10, ARM64Register.X29, 32, 8); // [x29+32] = arg_buf

    // Load arg count from [arg_buf + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X10, 0, 8); // X11 = arg_count
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X29, 40, 8); // [x29+40] = arg_count

    // Load args from buffer into AAPCS64 calling convention registers (X0-X7)
    // Args are at [arg_buf + 8 + i*8]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X29, 40, 8); // X11 = arg_count
    for (int i = 0; i < 8; i++) {
      var skipLabel = $"__gt_tramp_skip_arg{i}";
      // if arg_count <= i, skip
      EmitCmpImm(ARM64Register.X11, i + 1);
      EmitBranchCond(ARM64ConditionCode.Lt, skipLabel); // skip if arg_count < i+1
      // Load arg[i] from [arg_buf + 8 + i*8]
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, 32, 8); // X10 = arg_buf
      EmitLoadStoreUnsignedImm(0xF9400000, AbiArgRegs[i], ARM64Register.X10, 8 + i * 8, 8);
      DefineLabel(skipLabel);
    }

    // Free arg_buf via mm_raw_free before calling target
    // Save loaded args on stack first
    for (int i = 0; i < 8; i++) {
      EmitLoadStoreUnsignedImm(0xF9000000, AbiArgRegs[i], ARM64Register.X29, 48 + i * 8, 8);
    }
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // X0 = arg_buf
    EmitBranchLink("mm_raw_free");
    // Restore args
    for (int i = 0; i < 8; i++) {
      EmitLoadStoreUnsignedImm(0xF9400000, AbiArgRegs[i], ARM64Register.X29, 48 + i * 8, 8);
    }

    // Call target function
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 24, 8); // X9 = func_ptr
    // BLR X9
    EmitWord(0xD63F0120);

    // Store result (X0) and threw flag (X1) to gt struct
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 56, 8); // save threw
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffResult, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffThrew, 8);

    // Decrement live thread count
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_live_count");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: false);
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_live_count");

    // Remove from all-threads list
    // Walk: prev=NULL(X2), cur=__gt_all_head(X1); find cur == gt(X9) and unlink
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
    EmitGlobalLoadReg(ARM64Register.X1, "__gt_all_head"); // X1 = cur
    EmitMovRegImm(ARM64Register.X2, 0);                   // X2 = prev = NULL
    DefineLabel("__gt_tramp_alllist_loop");
    EmitCbz(ARM64Register.X1, "__gt_tramp_alllist_done"); // not found
    EmitCmpRegReg(ARM64Register.X1, ARM64Register.X9);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_tramp_alllist_found");
    // prev = cur; cur = cur->all_next
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X1, GtOffAllNext, 8);
    EmitBranch("__gt_tramp_alllist_loop");

    DefineLabel("__gt_tramp_alllist_found");
    // cur == X9; unlink: if prev==NULL: head = cur->all_next; else prev->all_next = cur->all_next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffAllNext, 8); // X0 = cur->all_next
    EmitCbnz(ARM64Register.X2, "__gt_tramp_alllist_prev");
    // prev == NULL: update head
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_all_head");
    EmitBranch("__gt_tramp_alllist_done");
    DefineLabel("__gt_tramp_alllist_prev");
    // prev->all_next = cur->all_next
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X2, GtOffAllNext, 8);
    DefineLabel("__gt_tramp_alllist_done");

    // Reload current gt
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");

    // Set status = completed
    EmitMovRegImm(ARM64Register.X0, GtStatusCompleted);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // Wake waiter if any
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, GtOffWaiter, 8);
    EmitCbz(ARM64Register.X10, "__gt_tramp_no_waiter");
    // waiter.status = ready
    EmitMovRegImm(ARM64Register.X0, GtStatusReady);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, GtOffStatus, 8);
    // enqueue waiter
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X10);
    EmitBranchLink("__gt_enqueue");
    DefineLabel("__gt_tramp_no_waiter");

    // Yield to next thread (never returns for completed threads)
    EmitBranchLink("__gt_yield_completed");
    // Should never reach here
    EmitWord(0xD4200000); // BRK #0
  }

  /// <summary>
  /// __gt_context_switch(from_x0, to_x1): Core context switch.
  /// Saves callee-saved registers on 'from', restores from 'to'.
  /// Updates __gt_current to 'to'.
  /// </summary>
  private void EmitGtContextSwitch() {
    DefineLabel("__gt_context_switch");
    // No standard prologue — this is a naked function

    // Save callee-saved registers on current stack (push order)
    // We save in this order so LDP restores in reverse:
    EmitStpPreIndex(ARM64Register.X19, ARM64Register.X20);
    EmitStpPreIndex(ARM64Register.X21, ARM64Register.X22);
    EmitStpPreIndex(ARM64Register.X23, ARM64Register.X24);
    EmitStpPreIndex(ARM64Register.X25, ARM64Register.X26);
    EmitStpPreIndex(ARM64Register.X27, ARM64Register.X28);
    EmitStpFpPreIndex(ARM64FloatRegister.D8, ARM64FloatRegister.D9);
    EmitStpFpPreIndex(ARM64FloatRegister.D10, ARM64FloatRegister.D11);
    EmitStpFpPreIndex(ARM64FloatRegister.D12, ARM64FloatRegister.D13);
    EmitStpFpPreIndex(ARM64FloatRegister.D14, ARM64FloatRegister.D15);
    EmitStpPreIndex(ARM64Register.X29, ARM64Register.X30);

    // Save SP to from.sp: MOV X9, SP; STR X9, [X0, #GtOffSp]
    EmitMovRegReg(ARM64Register.X9, ARM64Register.Sp);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffSp, 8);

    // Restore SP from to.sp: LDR X9, [X1, #GtOffSp]; MOV SP, X9
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X1, GtOffSp, 8);
    EmitMovRegReg(ARM64Register.Sp, ARM64Register.X9);

    // Update __gt_current = to
    EmitGlobalStoreReg(ARM64Register.X1, "__gt_current");

    // Restore callee-saved registers from new stack (reverse order)
    EmitLdpPostIndex(ARM64Register.X29, ARM64Register.X30);
    EmitLdpFpPostIndex(ARM64FloatRegister.D14, ARM64FloatRegister.D15);
    EmitLdpFpPostIndex(ARM64FloatRegister.D12, ARM64FloatRegister.D13);
    EmitLdpFpPostIndex(ARM64FloatRegister.D10, ARM64FloatRegister.D11);
    EmitLdpFpPostIndex(ARM64FloatRegister.D8, ARM64FloatRegister.D9);
    EmitLdpPostIndex(ARM64Register.X27, ARM64Register.X28);
    EmitLdpPostIndex(ARM64Register.X25, ARM64Register.X26);
    EmitLdpPostIndex(ARM64Register.X23, ARM64Register.X24);
    EmitLdpPostIndex(ARM64Register.X21, ARM64Register.X22);
    EmitLdpPostIndex(ARM64Register.X19, ARM64Register.X20);

    // RET (returns to new thread's saved LR)
    EmitWord(0xD65F03C0);
  }

  /// <summary>
  /// __gt_await(promise_x0) -> result in X0
  /// If the promise is already completed, extract result and return.
  /// Otherwise, set current to waiting, set promise.waiter = current, switch to next.
  /// </summary>
  private void EmitGtAwait() {
    EmitRuntimeFunctionStart("__gt_await", 1, 0x40);
    // [x29+16] = promise (arg 0)

    if (Compiler.AsyncTrace) {
      // [x29+32] = yield flag (0=immediate, 1=yielded)
      EmitMovRegImm(ARM64Register.X0, 0);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    }

    // Check if promise is already completed
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X1, GtStatusCompleted);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_await_done");

    if (Compiler.AsyncTrace) {
      EmitMovRegImm(ARM64Register.X0, 1);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    }

    // Not yet completed: block current thread
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
    // current.status = waiting
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // promise.waiter = current
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffWaiter, 8);

    // Dequeue next runnable thread
    DefineLabel("__gt_await_spin");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, "__gt_await_has_next");
    // No runnable thread — process pending I/O, then retry
    EmitBranchLink("__io_check_completions");
    EmitBranch("__gt_await_spin");

    DefineLabel("__gt_await_has_next");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save next

    // Set next.status = running
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // Context switch: from=current, to=next
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // to = next
    EmitBranchLink("__gt_context_switch");

    // We resume here after being woken up
    DefineLabel("__gt_await_done");

    if (Compiler.AsyncTrace) {
      // Trace output
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await");
      EmitBranchLink("mm_trace_print_tag");
      EmitReloadArg(0); // promise
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
      EmitCbnz(ARM64Register.X0, "__gt_await_trace_yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await_imm");
      EmitBranch("__gt_await_trace_print");
      DefineLabel("__gt_await_trace_yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await_yield");
      DefineLabel("__gt_await_trace_print");
      EmitBranchLink("mm_trace_print_tag");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }

    // Extract result from promise
    EmitReloadArg(0); // X0 = promise
    EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffResult, 8);

    // Free the green thread's stack via munmap
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X1, "__gt_await_skip_free_stack");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save result
    // munmap(stack_base, stack_size)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1); // X0 = stack_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffStackSize, 8); // X1 = stack_size
    EmitCallImport("munmap");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // restore result

    DefineLabel("__gt_await_skip_free_stack");
    // Save result, free the GreenThread struct
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save result
    EmitReloadArg(0); // X0 = promise (gt struct ptr)
    EmitBranchLink("mm_raw_free");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // restore result

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_try_await(promise_x0) -> result in X0, threw flag in X1
  /// Like __gt_await but also returns the threw flag.
  /// </summary>
  private void EmitGtTryAwait() {
    EmitRuntimeFunctionStart("__gt_try_await", 1, 0x50);
    // [x29+16] = promise (arg 0)

    if (Compiler.AsyncTrace) {
      // [x29+40] = yield flag
      EmitMovRegImm(ARM64Register.X0, 0);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    }

    // Check if promise is already completed
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X1, GtStatusCompleted);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_try_await_done");

    if (Compiler.AsyncTrace) {
      EmitMovRegImm(ARM64Register.X0, 1);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    }

    // Not yet completed: block current thread
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // promise.waiter = current
    EmitReloadArg(0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffWaiter, 8);

    // Dequeue next runnable thread
    DefineLabel("__gt_try_await_spin");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, "__gt_try_await_has_next");
    EmitBranchLink("__io_check_completions");
    EmitBranch("__gt_try_await_spin");

    DefineLabel("__gt_try_await_has_next");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save next

    // Set next.status = running
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // Context switch
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8);
    EmitBranchLink("__gt_context_switch");

    // Resume here
    DefineLabel("__gt_try_await_done");

    if (Compiler.AsyncTrace) {
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_try_await");
      EmitBranchLink("mm_trace_print_tag");
      EmitReloadArg(0);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
      EmitCbnz(ARM64Register.X0, "__gt_try_await_trace_yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await_imm");
      EmitBranch("__gt_try_await_trace_print");
      DefineLabel("__gt_try_await_trace_yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await_yield");
      DefineLabel("__gt_try_await_trace_print");
      EmitBranchLink("mm_trace_print_tag");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }

    // Extract result and threw flag from promise
    EmitReloadArg(0); // X0 = promise
    EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffResult, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffThrew, 8);

    // Free the green thread's stack
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X2, "__gt_try_await_skip_free_stack");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8); // save threw
    // munmap(stack_base, stack_size)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffStackSize, 8);
    EmitCallImport("munmap");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // restore result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // restore threw

    DefineLabel("__gt_try_await_skip_free_stack");
    // Save result + threw, free the GreenThread struct
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8); // save threw
    EmitReloadArg(0); // X0 = promise
    EmitBranchLink("mm_raw_free");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // restore result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // restore threw

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_yield_completed / __gt_yield: Yield current thread.
  /// __gt_yield_completed: for completed threads (don't enqueue self).
  /// </summary>
  private void EmitGtYield() {
    DefineLabel("__gt_yield_completed");
    // Set up a frame
    var imm7 = unchecked((uint)(-0x30 / 8)) & 0x7Fu;
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u); // STP x29, x30, [sp, #-0x30]!
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // Try to dequeue next runnable thread
    EmitBranchLink("__gt_dequeue");
    // If no more threads, switch back to main thread
    EmitCbnz(ARM64Register.X0, "__gt_yield_has_next");

    // No more threads: switch back to main thread
    EmitGlobalLeaReg(ARM64Register.X1, "__gt_main_thread");
    // If current IS the main thread, just return
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_current");
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_yield_return");
    // Switch to main
    EmitMovRegImm(ARM64Register.X2, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X1, GtOffStatus, 8);
    // from=current(X0), to=main(X1)
    EmitBranchLink("__gt_context_switch");
    EmitBranch("__gt_yield_return");

    DefineLabel("__gt_yield_has_next");
    // next.status = running
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save next
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // context switch: from=current, to=next
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8);
    EmitBranchLink("__gt_context_switch");

    DefineLabel("__gt_yield_return");
    // Epilogue
    EmitMovRegReg(ARM64Register.Sp, ARM64Register.X29);
    var imm7Post = (0x30u / 8) & 0x7Fu;
    EmitWord(0xA8C00000 | (imm7Post << 15) | (30u << 10) | (31u << 5) | 29u); // LDP x29, x30, [sp], #0x30
    EmitWord(0xD65F03C0); // RET
  }

  /// <summary>
  /// __gt_cancel(gt_x0): Request cancellation of a green thread.
  /// Sets cancel_flag=1. No CancelIoEx on macOS.
  /// </summary>
  private void EmitGtCancel() {
    EmitRuntimeFunctionStart("__gt_cancel", 1, 0x30);

    if (Compiler.AsyncTrace) {
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save gt ptr
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_cancel");
      EmitBranchLink("mm_trace_print_tag");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // gt ptr
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }

    // X0 = gt (reload from arg)
    EmitReloadArg(0);
    // gt->cancel_flag = 1
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffCancelFlag, 8);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_cleanup(): Called from _start after main returns.
  /// Cancels all live green threads, then drains the run queue.
  /// </summary>
  private void EmitGtCleanup() {
    EmitRuntimeFunctionStart("__gt_cleanup", 0, 0x30);

    // --- Step 1: Cancel all live threads ---
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_all_head");
    DefineLabel("__gt_cleanup_cancel_loop");
    EmitCbz(ARM64Register.X0, "__gt_cleanup_drain");
    // Save current gt and next across call
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save gt
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffAllNext, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // save next
    // __gt_cancel(X0=gt) -- X0 already set
    EmitBranchLink("__gt_cancel");
    // Advance to next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitBranch("__gt_cleanup_cancel_loop");

    // --- Step 2: Drain run queue ---
    DefineLabel("__gt_cleanup_drain");
    EmitBranchLink("__gt_dequeue");
    EmitCbz(ARM64Register.X0, "__gt_cleanup_check_live");

    // Run the thread: set status=running, context switch to it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save next
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8);
    EmitBranchLink("__gt_context_switch");
    // Resume here when thread completes/yields back
    EmitBranch("__gt_cleanup_drain");

    // Run queue empty — check if any threads still alive
    DefineLabel("__gt_cleanup_check_live");
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_live_count");
    EmitCbz(ARM64Register.X0, "__gt_cleanup_done");
    // Threads still alive but nothing runnable — process pending I/O
    EmitBranchLink("__io_check_completions");
    EmitBranch("__gt_cleanup_drain");

    DefineLabel("__gt_cleanup_done");
    EmitRuntimeFunctionEnd();
  }

  // =====================================================================
  // Async I/O Runtime — deferred execution model for macOS
  // =====================================================================

  /// <summary>
  /// Emits all async I/O runtime functions.
  /// On macOS, I/O is processed inline by the scheduler (no OS worker threads).
  /// Green threads submit I/O requests and yield; the scheduler executes pending
  /// I/O when no threads are runnable, then re-enqueues the waiters.
  /// </summary>
  private void EmitIoRuntime() {
    EmitIoEnqueueSyncReq();
    EmitIoDequeueSyncReq();
    EmitIoCheckCompletions();
    EmitIoSubmitSync();
    EmitNetParseOctet();
    // Only emit DNS resolver (with dylib imports) if the program uses networking.
    // Check if maxon_net_tcp_connect is referenced by looking for it in branch fixups.
    if (_branchFixups.Any(f => f.target == "maxon_net_tcp_connect")) {
      EmitDnsCallback();
      EmitNetResolveHost();
    } else {
      EmitNetResolveHostIpOnly();
    }
  }

  /// <summary>
  /// __io_enqueue_sync_req(req_x0): Append a SyncRequest to the request queue.
  /// </summary>
  private void EmitIoEnqueueSyncReq() {
    EmitRuntimeFunctionStart("__io_enqueue_sync_req", 1, 0x30);

    // req.next = 0
    EmitReloadArg(0);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffNext, 8);

    // if tail == NULL: head = tail = req
    EmitGlobalLoadReg(ARM64Register.X1, "__io_sync_req_tail");
    EmitCbnz(ARM64Register.X1, "__io_enqueue_sync_append");

    EmitReloadArg(0);
    EmitGlobalStoreReg(ARM64Register.X0, "__io_sync_req_head");
    EmitGlobalStoreReg(ARM64Register.X0, "__io_sync_req_tail");
    EmitRuntimeFunctionEnd();

    DefineLabel("__io_enqueue_sync_append");
    EmitGlobalLoadReg(ARM64Register.X1, "__io_sync_req_tail");
    EmitReloadArg(0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X1, SyncReqOffNext, 8); // tail.next = req
    EmitGlobalStoreReg(ARM64Register.X0, "__io_sync_req_tail"); // tail = req
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_dequeue_sync_req() -> SyncRequest* in X0 (or NULL if queue empty).
  /// </summary>
  private void EmitIoDequeueSyncReq() {
    EmitRuntimeFunctionStart("__io_dequeue_sync_req", 0, 0x30);

    EmitGlobalLoadReg(ARM64Register.X0, "__io_sync_req_head");
    EmitCbnz(ARM64Register.X0, "__io_dequeue_sync_nonempty");
    // Empty → return NULL
    EmitRuntimeFunctionEnd();

    DefineLabel("__io_dequeue_sync_nonempty");
    // new_head = head.next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, SyncReqOffNext, 8);
    EmitGlobalStoreReg(ARM64Register.X1, "__io_sync_req_head");
    EmitCbnz(ARM64Register.X1, "__io_dequeue_sync_done");
    // Queue now empty, clear tail
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitGlobalStoreReg(ARM64Register.X1, "__io_sync_req_tail");
    DefineLabel("__io_dequeue_sync_done");
    // Clear dequeued node's next
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffNext, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_check_completions(): Process pending I/O requests inline.
  /// Dequeues SyncRequests, executes the I/O call, stores results in the
  /// waiter GreenThread, sets status=ready, and re-enqueues the waiter.
  /// Non-blocking: returns immediately if no requests are pending.
  /// </summary>
  private void EmitIoCheckCompletions() {
    // Frame: 0x100 to accommodate stat64 buffer and networking locals
    // Locals:
    //   [x29+16] = req ptr
    //   [x29+24] = waiter GT ptr
    //   [x29+32] = op code
    //   [x29+40] = result value / temp
    //   [x29+48 .. x29+191] = stat64 buffer (144 bytes) / sockaddr_in for net_connect
    //   [x29+64] = net_connect: socket fd
    //   [x29+72] = net_connect: resolved IP
    //   [x29+80] = net_connect: port
    //   [x29+88] = net_send/recv: args struct ptr
    // Note: getcwd path buffer is heap-allocated instead of stack-allocated
    EmitRuntimeFunctionStart("__io_check_completions", 0, 0x100);

    DefineLabel("__io_check_comp_loop");
    EmitBranchLink("__io_dequeue_sync_req");
    EmitCbz(ARM64Register.X0, "__io_check_comp_ret"); // queue empty → done

    // Save req ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    // Load and save waiter GT
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, SyncReqOffWaiter, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8);
    // Load and save op code
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, SyncReqOffOp, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8);

    // Dispatch on op code
    EmitCmpImm(ARM64Register.X2, SyncOpFileExists);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_exists");
    EmitCmpImm(ARM64Register.X2, SyncOpFileDelete);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_delete");
    EmitCmpImm(ARM64Register.X2, SyncOpDirExists);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_dir_exists");
    EmitCmpImm(ARM64Register.X2, SyncOpDirCreate);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_dir_create");
    EmitCmpImm(ARM64Register.X2, SyncOpGetCwd);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_get_cwd");
    EmitCmpImm(ARM64Register.X2, SyncOpFileOpenRead);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_open_read");
    EmitCmpImm(ARM64Register.X2, SyncOpFileOpenWrite);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_open_write");
    EmitCmpImm(ARM64Register.X2, SyncOpCloseHandle);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_close_handle");
    EmitCmpImm(ARM64Register.X2, SyncOpNetConnect);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_net_connect");
    EmitCmpImm(ARM64Register.X2, SyncOpNetSend);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_net_send");
    EmitCmpImm(ARM64Register.X2, SyncOpNetRecv);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_net_recv");
    EmitCmpImm(ARM64Register.X2, SyncOpNetClose);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_net_close");

    // Unknown op → result = 0
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpFileExists: stat64(path, &buf), check not directory ---
    DefineLabel("__io_op_file_exists");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8); // path
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true); // &stat_buf
    EmitCallImport("stat");
    EmitBranchOnLibcError("__io_op_file_exists_fail");
    // Load st_mode (uint16 at stat_buf + 4)
    // LDRH W1, [X29, #52]
    EmitWord(0x79400000 | ((52u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X1));
    // AND W1, W1, #0xF000
    EmitMovRegImm(ARM64Register.X2, 0xF000);
    EmitWord(0x0A020000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X1));
    // CMP W1, #0x4000 (S_IFDIR)
    EmitMovRegImm(ARM64Register.X2, 0x4000);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5));
    // CSINC X0, XZR, XZR, EQ → 1 if not dir, 0 if dir
    EmitWord(0x9A9F07E0 | (CondCode(ARM64ConditionCode.Eq) << 12) | Reg(ARM64Register.X0));
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_file_exists_fail");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpFileDelete: unlink(path) ---
    DefineLabel("__io_op_file_delete");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitCallImport("unlink");
    EmitBranchOnLibcError("__io_op_file_delete_fail");
    EmitMovRegImm(ARM64Register.X0, 0); // success (0 per spec)
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_file_delete_fail");
    EmitMovRegImm(ARM64Register.X0, -1); // failure
    EmitBranch("__io_op_done");

    // --- SyncOpDirExists: stat64, check S_IFDIR ---
    DefineLabel("__io_op_dir_exists");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true);
    EmitCallImport("stat");
    EmitBranchOnLibcError("__io_op_dir_exists_fail");
    // LDRH W1, [X29, #52]
    EmitWord(0x79400000 | ((52u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X1));
    EmitMovRegImm(ARM64Register.X2, 0xF000);
    EmitWord(0x0A020000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X1));
    EmitMovRegImm(ARM64Register.X2, 0x4000);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5));
    // CSINC X0, XZR, XZR, NE → 1 if IS dir, 0 if not dir
    EmitWord(0x9A9F07E0 | (CondCode(ARM64ConditionCode.Ne) << 12) | Reg(ARM64Register.X0));
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_dir_exists_fail");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpDirCreate: mkdir(path, 0777) ---
    DefineLabel("__io_op_dir_create");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitMovRegImm(ARM64Register.X1, 0x1FF); // 0777
    EmitCallImport("mkdir");
    EmitBranchOnLibcError("__io_op_dir_create_fail");
    EmitMovRegImm(ARM64Register.X0, 1); // success
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_dir_create_fail");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpGetCwd: alloc buf, open(".") + fcntl(F_GETPATH, buf), close ---
    DefineLabel("__io_op_get_cwd");
    // Allocate 1024-byte path buffer via mm_alloc (stdlib frees with mm_free)
    EmitMovRegImm(ARM64Register.X0, 1024);
    EmitMovRegImm(ARM64Register.X1, 0); // no destructor
    EmitMovRegImm(ARM64Register.X2, 0); // no tag
    EmitBranchLink("mm_alloc");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save buf ptr
    // open(".", O_RDONLY)
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__dot_path");
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("open");
    EmitVariadicCleanup();
    EmitBranchOnLibcError("__io_op_get_cwd_fail");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save fd (reuse stat buf area)
    // fcntl(fd, F_GETPATH, buf)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 40, 8); // buf
    EmitMovRegImm(ARM64Register.X1, F_GETPATH);
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("fcntl");
    EmitVariadicCleanup();
    // close(fd)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitCallImport("close");
    // Return the heap-allocated buffer ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_get_cwd_fail");
    // Free the allocated buffer on failure
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitBranchLink("mm_free");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpFileOpenRead: open(path, O_RDONLY) ---
    DefineLabel("__io_op_file_open_read");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitMovRegImm(ARM64Register.X1, 0); // O_RDONLY
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("open");
    EmitVariadicCleanup();
    EmitBranchOnLibcError("__io_op_file_open_read_fail");
    EmitBranch("__io_op_done"); // X0 = fd
    DefineLabel("__io_op_file_open_read_fail");
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitBranch("__io_op_done");

    // --- SyncOpFileOpenWrite: open(path, O_WRONLY|O_CREAT|O_TRUNC, 0666) ---
    DefineLabel("__io_op_file_open_write");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitMovRegImm(ARM64Register.X1, O_WRONLY_CREAT_TRUNC);
    EmitMovRegImm(ARM64Register.X2, 0x1B6); // 0666
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("open");
    EmitVariadicCleanup();
    EmitBranchOnLibcError("__io_op_file_open_write_fail");
    EmitBranch("__io_op_done"); // X0 = fd
    DefineLabel("__io_op_file_open_write_fail");
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitBranch("__io_op_done");

    // --- SyncOpCloseHandle: close(fd) ---
    DefineLabel("__io_op_close_handle");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitCallImport("close");
    EmitMovRegImm(ARM64Register.X0, 0); // always return 0
    EmitBranch("__io_op_done");

    // --- SyncOpNetConnect: resolve hostname, socket(), connect() → fd or -1/-2 ---
    // req.arg0 = cstring hostname, req.arg1 = port
    // Uses stack: [x29+48..x29+63] = sockaddr_in (16 bytes)
    //             [x29+64] = socket fd, [x29+72] = resolved IP, [x29+80] = port
    DefineLabel("__io_op_net_connect");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg1, 8); // port
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8); // save port

    // Resolve hostname → IP
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8); // hostname
    EmitBranchLink("__net_resolve_host");
    // X0 = IP in network byte order, or 0 on failure
    EmitCbz(ARM64Register.X0, "__io_op_ntc_dns_fail");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8); // save IP

    // Check TEST-NET-1 (192.0.2.1 = 0xC00002.01 in network byte order = 0x010200C0 as 32-bit)
    EmitMovRegImm(ARM64Register.X1, 0x010200C0);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_ntc_testnet_fail");

    // socket(AF_INET=2, SOCK_STREAM=1, 0)
    EmitMovRegImm(ARM64Register.X0, 2);  // AF_INET
    EmitMovRegImm(ARM64Register.X1, 1);  // SOCK_STREAM
    EmitMovRegImm(ARM64Register.X2, 0);  // protocol
    EmitCallImport("socket");
    EmitBranchOnLibcError("__io_op_ntc_connect_fail");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8); // save socket fd

    // Build sockaddr_in at [x29+48] (16 bytes)
    // Zero 16 bytes
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    // sin_len=16, sin_family=AF_INET=2 → STRH at [x29+48]
    EmitMovRegImm(ARM64Register.X0, 0x0210); // len=16, family=2
    EmitWord(0x79000000 | ((48u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0));
    // sin_port = htons(port) at [x29+50]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 80, 8); // port
    // htons: reverse bytes of 16-bit value. port is in X0.
    // REV16 W0, W0 then AND to 16 bits
    EmitWord(0x5AC00400 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // REV16 W0, W0
    EmitWord(0x79000000 | ((50u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // STRH [X29, #50]
    // sin_addr = resolved IP at [x29+52]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 72, 8); // IP
    EmitWord(0xB9000000 | ((52u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // STR W0, [X29, #52]

    // connect(socket, &sockaddr_in, 16)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8); // socket
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true); // &sockaddr
    EmitMovRegImm(ARM64Register.X2, 16);
    EmitCallImport("connect");
    EmitBranchOnLibcError("__io_op_ntc_close_connect_fail");

    // Success: return socket fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    EmitBranch("__io_op_done");

    DefineLabel("__io_op_ntc_close_connect_fail");
    // Close socket, return -2
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    EmitCallImport("close");
    DefineLabel("__io_op_ntc_connect_fail");
    DefineLabel("__io_op_ntc_testnet_fail");
    EmitMovRegImm(ARM64Register.X0, -2);
    EmitBranch("__io_op_done");

    DefineLabel("__io_op_ntc_dns_fail");
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitBranch("__io_op_done");

    // --- SyncOpNetSend: write(handle, buf, len) → bytes written ---
    // req.arg0 = socket handle, req.arg1 = args struct ptr {buf_ptr, length}
    DefineLabel("__io_op_net_send");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, SyncReqOffArg0, 8); // socket handle
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X9, SyncReqOffArg1, 8); // args struct
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X29, 88, 8); // save args for freeing
    // Load buf_ptr and length from args struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X11, 0, 8); // buf_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X11, 8, 8); // length
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X10); // fd
    EmitCallImport("write");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save result
    // Free args struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 88, 8);
    EmitBranchLink("mm_raw_free");
    // On error, X0 would be negative.
    // Restore result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitBranch("__io_op_done");

    // --- SyncOpNetRecv: read(handle, buf, capacity) → bytes read ---
    // req.arg0 = socket handle, req.arg1 = args struct ptr {buf_ptr, capacity}
    DefineLabel("__io_op_net_recv");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, SyncReqOffArg0, 8); // socket handle
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X9, SyncReqOffArg1, 8); // args struct
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X29, 88, 8); // save args for freeing
    // Load buf_ptr and capacity from args struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X11, 0, 8); // buf_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X11, 8, 8); // capacity
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X10); // fd
    EmitCallImport("read");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save result
    // Free args struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 88, 8);
    EmitBranchLink("mm_raw_free");
    // Restore result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitBranch("__io_op_done");

    // --- SyncOpNetClose: close(handle) ---
    DefineLabel("__io_op_net_close");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8); // handle
    EmitCbz(ARM64Register.X0, "__io_op_net_close_skip");
    EmitCallImport("close");
    DefineLabel("__io_op_net_close_skip");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- Common completion: store result, re-enqueue waiter, free req ---
    DefineLabel("__io_op_done");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save result

    // waiter_gt.io_result_val = result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 24, 8); // waiter GT
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoResultVal, 8);

    // waiter_gt.io_error_code = 0
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoErrorCode, 8);

    // waiter_gt.status = ready (0)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // __gt_enqueue(waiter_gt)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitBranchLink("__gt_enqueue");

    // Free req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitBranchLink("mm_raw_free");

    // Loop
    EmitBranch("__io_check_comp_loop");

    DefineLabel("__io_check_comp_ret");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_submit_sync(op_x0, arg0_x1, arg1_x2) -> result in X0.
  /// Submits an I/O request, yields the current green thread, and returns
  /// the result after the scheduler processes the request.
  /// </summary>
  private void EmitIoSubmitSync() {
    EmitRuntimeFunctionStart("__io_submit_sync", 3, 0x50);
    // [x29+16] = op, [x29+24] = arg0, [x29+32] = arg1

    // Check cancel flag
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffCancelFlag, 8);
    EmitCbnz(ARM64Register.X0, "__io_submit_sync_cancelled");

    // Allocate SyncRequest
    EmitMovRegImm(ARM64Register.X0, SyncReqSize);
    EmitBranchLink("mm_raw_alloc");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save req

    // Fill: op, arg0, arg1, waiter=current, next=0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // op
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffOp, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // arg0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffArg0, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // arg1
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffArg1, 8);
    EmitGlobalLoadReg(ARM64Register.X1, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffWaiter, 8);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffNext, 8);

    // Set current.status = waiting
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // Enqueue the request
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // req
    EmitBranchLink("__io_enqueue_sync_req");

    if (Compiler.AsyncTrace) {
      // Trace: "io_yield #N [op_name]\n"
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_io_yield");
      EmitBranchLink("mm_trace_print_tag");
      EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitIoTraceOpSuffix("yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }

    // Yield: try to dequeue next runnable thread
    DefineLabel("__io_submit_sync_try_dequeue");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, "__io_submit_sync_has_next");

    // No runnable thread: process pending I/O inline
    EmitBranchLink("__io_check_completions");
    EmitBranch("__io_submit_sync_try_dequeue");

    DefineLabel("__io_submit_sync_has_next");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save next

    // Set next.status = running
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // Context switch: from=current, to=next
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 48, 8);
    EmitBranchLink("__gt_context_switch");

    // Resume here after being re-enqueued by __io_check_completions
    DefineLabel("__io_submit_sync_resume");

    if (Compiler.AsyncTrace) {
      // Trace: "io_resume #N [op_name]\n"
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_io_resume");
      EmitBranchLink("mm_trace_print_tag");
      EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitIoTraceOpSuffix("resume");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }

    // Return gt.io_result_val
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffIoResultVal, 8);
    EmitRuntimeFunctionEnd();

    // Cancelled path
    DefineLabel("__io_submit_sync_cancelled");
    EmitGlobalLoadReg(ARM64Register.X9, "__gt_current");
    EmitMovRegImm(ARM64Register.X0, 995); // generic error code
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoErrorCode, 8);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Emits trace op suffix for io_yield/io_resume: prints " [op_name]" based on
  /// the op code saved at [x29+16].
  /// </summary>
  private void EmitIoTraceOpSuffix(string context) {
    var doneLabel = $"__io_trace_op_done_{context}";
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // load op code

    var ops = new (long opCode, string symdata)[] {
      (SyncOpFileExists,    "__at_io_op_file_exists"),
      (SyncOpFileDelete,    "__at_io_op_file_delete"),
      (SyncOpDirExists,     "__at_io_op_dir_exists"),
      (SyncOpDirCreate,     "__at_io_op_dir_create"),
      (SyncOpGetCwd,        "__at_io_op_get_cwd"),
      (SyncOpFileOpenRead,  "__at_io_op_file_open_read"),
      (SyncOpFileOpenWrite, "__at_io_op_file_open_write"),
      (SyncOpCloseHandle,   "__at_io_op_close_handle"),
      (SyncOpNetConnect,    "__at_io_op_net_connect"),
      (SyncOpNetSend,       "__at_io_op_net_send"),
      (SyncOpNetRecv,       "__at_io_op_net_recv"),
      (SyncOpNetClose,      "__at_io_op_net_close"),
    };

    foreach (var (opCode, symdata) in ops) {
      var skipLabel = $"__io_trace_skip_{context}_{opCode}";
      EmitCmpImm(ARM64Register.X0, opCode);
      EmitBranchCond(ARM64ConditionCode.Ne, skipLabel);
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, symdata);
      EmitBranchLink("mm_trace_print_tag");
      EmitBranch(doneLabel);
      DefineLabel(skipLabel);
    }

    DefineLabel(doneLabel);
  }

  // =====================================================================
  // Networking runtime functions — macOS ARM64
  // =====================================================================

  /// <summary>
  /// maxon_net_tcp_connect(cstring_x0, port_x1) → managed __ManagedSocket ptr, or -1 (DNS fail), -2 (connect fail).
  /// Delegates to __io_submit_sync(SyncOpNetConnect, host, port) to process inline by the scheduler.
  /// Stack: [x29+16]=host, [x29+24]=port, [x29+32]=raw handle
  /// </summary>
  private void EmitNetTcpConnect() {
    EmitRuntimeFunctionStart("maxon_net_tcp_connect", 2, 0x40);

    // Submit sync I/O: __io_submit_sync(SyncOpNetConnect, cstring_host, port)
    EmitMovRegImm(ARM64Register.X0, SyncOpNetConnect);   // op
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // host
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 24, 8); // port
    EmitBranchLink("__io_submit_sync");
    // X0 = raw socket fd or -1 (DNS fail) or -2 (connect fail)

    // Check if negative (error)
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, "rt_ntc_fail");

    // Save raw handle
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Allocate __ManagedSocket via mm_alloc(8, destructor_ptr, tag_index=0)
    EmitMovRegImm(ARM64Register.X0, 8);
    EmitAdrpAddFixup(ARM64Register.X1, _funcAddrAdrpFixups, "__destruct___ManagedSocket");
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("mm_alloc");

    // Store socket handle at [managed_ptr+0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // raw handle
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);   // [ptr+0] = handle
    EmitBranch("rt_ntc_done");

    DefineLabel("rt_ntc_fail");
    // X0 already has -1 or -2
    DefineLabel("rt_ntc_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_send(socket_handle_x0, buffer_ptr_x1, length_x2) → bytes_sent or -1.
  /// Delegates to __io_submit_sync(SyncOpNetSend, handle, args_struct).
  /// Stack: [x29+16]=handle, [x29+24]=buf, [x29+32]=length, [x29+40]=args_struct
  /// </summary>
  private void EmitNetSend() {
    EmitRuntimeFunctionStart("maxon_net_send", 3, 0x40);

    // Allocate 16-byte args struct {buf_ptr, length}
    EmitMovRegImm(ARM64Register.X0, 16);
    EmitBranchLink("mm_raw_alloc");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save args ptr

    // Store buf_ptr and length in struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buf
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);   // [args+0] = buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // length
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 8, 8);   // [args+8] = length

    // Submit: __io_submit_sync(SyncOpNetSend, handle, args_struct)
    EmitMovRegImm(ARM64Register.X0, SyncOpNetSend);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // handle
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 40, 8); // args
    EmitBranchLink("__io_submit_sync");
    // X0 = bytes sent or -1
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_recv(socket_handle_x0, buffer_ptr_x1, capacity_x2) → bytes_received, 0=closed, -1=error.
  /// Delegates to __io_submit_sync(SyncOpNetRecv, handle, args_struct).
  /// Stack: [x29+16]=handle, [x29+24]=buf, [x29+32]=capacity, [x29+40]=args_struct
  /// </summary>
  private void EmitNetRecv() {
    EmitRuntimeFunctionStart("maxon_net_recv", 3, 0x40);

    // Allocate 16-byte args struct {buf_ptr, capacity}
    EmitMovRegImm(ARM64Register.X0, 16);
    EmitBranchLink("mm_raw_alloc");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save args ptr

    // Store buf_ptr and capacity in struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buf
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);   // [args+0] = buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // capacity
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 8, 8);   // [args+8] = capacity

    // Submit: __io_submit_sync(SyncOpNetRecv, handle, args_struct)
    EmitMovRegImm(ARM64Register.X0, SyncOpNetRecv);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // handle
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 40, 8); // args
    EmitBranchLink("__io_submit_sync");
    // X0 = bytes received, 0 = closed, -1 = error
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_close(socket_handle_x0) → void. Idempotent: does nothing if handle is 0.
  /// Delegates to __io_submit_sync(SyncOpNetClose, handle, 0).
  /// </summary>
  private void EmitNetClose() {
    EmitRuntimeFunctionStart("maxon_net_close", 1, 0x30);
    EmitMovRegImm(ARM64Register.X0, SyncOpNetClose);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // handle
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __destruct___ManagedSocket(user_ptr_x0) → void.
  /// Called by mm_decref when refcount hits 0. Reads _handle at [user_ptr+0],
  /// calls close() if non-zero, then zeros the handle for idempotency.
  /// </summary>
  private void EmitNetSocketDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedSocket", 1, 0x30);

    EmitReloadArg(0);                                                                  // X0 = user_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);   // X1 = _handle
    EmitCbz(ARM64Register.X1, "rt_nsd_done");

    // Zero the handle before closing (idempotency)
    EmitReloadArg(0);                                                                  // X0 = user_ptr
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 0, 8);   // [ptr+0] = 0

    // close(handle) — X1 still has the handle
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitCallImport("close");

    DefineLabel("rt_nsd_done");
    EmitRuntimeFunctionEnd();
  }

  // =====================================================================
  // DNS resolver — uses DNSServiceGetAddrInfo (non-blocking OS resolver)
  // =====================================================================

  // Global to receive DNS callback result (single-threaded, safe)
  // Layout: [0]=resolved_ip (4 bytes as i64), [8]=error_flag
  private const string DnsResultGlobal = "__dns_result";

  /// <summary>
  /// __net_resolve_host(cstring_x0) → IP in network byte order in X0, or 0 on failure.
  /// Phase 1: tries to parse as dotted-decimal IP.
  /// Phase 2: uses DNSServiceGetAddrInfo (non-blocking macOS DNS resolver).
  /// Stack: [x29+16]=cstring, [x29+24]=dns_ref_ptr(8 bytes on stack), [x29+32]=dns_ref,
  ///        [x29+40]=fd, [x29+48..x29+175]=fd_set(128 bytes), [x29+72]=result_ip
  /// </summary>
  private void EmitNetResolveHost() {
    // Define global for DNS callback result
    DefineGlobal(DnsResultGlobal, 16, 0); // [0]=ip, [8]=error

    EmitRuntimeFunctionStart("__net_resolve_host", 1, 0xC0);
    // [x29+16] = cstring

    // --- Phase 1: Try to parse as IP address ---
    EmitReloadArg(0); // X0 = cstring
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = scan ptr
    DefineLabel("__nrh_ip_scan");
    EmitWord(0x38401422); // LDRB W2, [X1], #1
    EmitCbz(ARM64Register.X2, "__nrh_is_ip"); // null terminator → it's an IP
    EmitCmpImm(ARM64Register.X2, 46); // '.'
    EmitBranchCond(ARM64ConditionCode.Eq, "__nrh_ip_scan");
    EmitCmpImm(ARM64Register.X2, 48); // '0'
    EmitBranchCond(ARM64ConditionCode.Lt, "__nrh_is_hostname");
    EmitCmpImm(ARM64Register.X2, 57); // '9'
    EmitBranchCond(ARM64ConditionCode.Gt, "__nrh_is_hostname");
    EmitBranch("__nrh_ip_scan");

    // --- Parse IP: "a.b.c.d" → network byte order ---
    DefineLabel("__nrh_is_ip");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8);
    EmitReloadArg(0);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 72, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 73, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 74, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 75, 1);
    EmitWord(0xB9400000 | ((72u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0));
    EmitRuntimeFunctionEnd();

    // --- Phase 2: Hostname → DNSServiceGetAddrInfo ---
    DefineLabel("__nrh_is_hostname");

    // Clear DNS result global: ip=0, error=1
    EmitGlobalLeaReg(ARM64Register.X9, DnsResultGlobal);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 0, 8); // ip = 0
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 8, 8); // error = 1

    // DNSServiceGetAddrInfo(&ref, flags=0, ifindex=0, protocol=IPv4=1, hostname, callback, context=NULL)
    // Initialize ref slot to NULL before the call
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // ref = NULL
    // Set args in careful order to avoid clobbering
    EmitMovRegImm(ARM64Register.X6, 0);  // context = NULL
    EmitAdrpAddFixup(ARM64Register.X5, _funcAddrAdrpFixups, "__dns_callback"); // X5 = callback
    EmitReloadArg(0);                     // X0 = hostname (from [x29+16])
    EmitMovRegReg(ARM64Register.X4, ARM64Register.X0); // X4 = hostname
    EmitMovRegImm(ARM64Register.X3, 1);  // kDNSServiceProtocol_IPv4
    EmitMovRegImm(ARM64Register.X2, 0);  // interfaceIndex = 0 (any)
    EmitMovRegImm(ARM64Register.X1, 0);  // flags = 0
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 32, isAdd: true); // X0 = &ref
    EmitCallImport("DNSServiceGetAddrInfo");
    // X0 = error code (DNSServiceErrorType = int32_t, in W0)
    // Zero-extend W0 to X0 so CBNZ works correctly on 64-bit register
    EmitWord(0x2A0003E0); // MOV W0, W0 (ORR W0, WZR, W0 — zero-extends)
    EmitCbnz(ARM64Register.X0, "__nrh_dns_fail");

    // Load the ref from stack
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // dns_ref

    // DNSServiceRefSockFD(ref) → fd
    EmitCallImport("DNSServiceRefSockFD");
    // Zero-extend W0 to X0 (DNSServiceRefSockFD returns int32_t in W0)
    EmitWord(0x2A0003E0); // MOV W0, W0 (ORR W0, WZR, W0 — zero-extends)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save fd
    // Check fd >= 0
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, "__nrh_dns_dealloc_fail");

    // select(fd+1, &readfds, NULL, NULL, &timeout) to wait for DNS response
    // Build fd_set at [x29+48]: zero 128 bytes then set bit for our fd
    EmitMovRegImm(ARM64Register.X0, 0);
    for (int i = 0; i < 16; i++) { // 16 * 8 = 128 bytes
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48 + i * 8, 8);
    }
    // Set bit for fd: fd_set[fd/64] |= (1 << (fd%64))
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // fd
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitWord(0x9AC02021); // LSLV X1, X1, X0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 48, 8); // fd_set[0] = 1<<fd

    // Build timeval at [x29+176]: tv_sec=2, tv_usec=0
    EmitMovRegImm(ARM64Register.X0, 2);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 176, 8); // tv_sec
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 184, 8); // tv_usec

    // select(nfds=fd+1, readfds=&fd_set, writefds=NULL, errfds=NULL, timeout=&timeval)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // fd
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true); // nfds = fd+1
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true); // &readfds
    EmitMovRegImm(ARM64Register.X2, 0); // writefds = NULL
    EmitMovRegImm(ARM64Register.X3, 0); // errfds = NULL
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X29, 176, isAdd: true); // &timeout
    EmitCallImport("select");
    // X0 = number of ready fds (0 = timeout, -1 = error)
    EmitCmpImm(ARM64Register.X0, 1);
    EmitBranchCond(ARM64ConditionCode.Lt, "__nrh_dns_dealloc_fail"); // timeout or error

    // DNSServiceProcessResult(ref) — fires the callback synchronously
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // dns_ref
    EmitCallImport("DNSServiceProcessResult");
    // Zero-extend W0 to X0 (DNSServiceProcessResult returns int32_t in W0)
    EmitWord(0x2A0003E0); // MOV W0, W0 (ORR W0, WZR, W0 — zero-extends)
    EmitCbnz(ARM64Register.X0, "__nrh_dns_dealloc_fail");

    // DNSServiceRefDeallocate(ref)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitCallImport("DNSServiceRefDeallocate");

    // Read result from global
    EmitGlobalLoadReg(ARM64Register.X0, DnsResultGlobal); // ip
    EmitRuntimeFunctionEnd();

    // Failure: deallocate ref and return 0
    DefineLabel("__nrh_dns_dealloc_fail");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitCallImport("DNSServiceRefDeallocate");
    DefineLabel("__nrh_dns_fail");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __dns_callback: DNSServiceGetAddrInfoReply callback.
  /// Called by DNSServiceProcessResult when DNS resolves.
  /// Extracts IPv4 address from sockaddr and stores in __dns_result global.
  /// Args: X0=sdRef, X1=flags, X2=ifIndex, X3=errorCode, X4=hostname, X5=address, X6=ttl, X7=context
  /// </summary>
  private void EmitDnsCallback() {
    DefineLabel("__dns_callback");
    // STP x29, x30, [sp, #-0x30]!
    var imm7 = (uint)((-0x30 / 8) & 0x7F);
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // Check errorCode (W3, int32_t) — 0 = success
    // Zero-extend W3 to X3 for 64-bit CBNZ
    EmitWord(0x2A0303E3); // MOV W3, W3 (zero-extends)
    EmitCbnz(ARM64Register.X3, "__dns_cb_done");

    // Check address pointer (X5) — must not be NULL
    EmitCbz(ARM64Register.X5, "__dns_cb_done");

    // X5 points to sockaddr. For IPv4 (sockaddr_in on macOS):
    //   [0] = sin_len (1 byte)
    //   [1] = sin_family (1 byte, should be AF_INET=2)
    //   [2..3] = sin_port (2 bytes)
    //   [4..7] = sin_addr (4 bytes, network byte order) ← this is what we want

    // Load sin_addr at [X5+4] as a 32-bit word
    EmitWord(0xB9400000 | ((4u / 4) << 10) | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDR W0, [X5, #4]

    // Store to __dns_result global: [0]=ip
    EmitGlobalLeaReg(ARM64Register.X9, DnsResultGlobal);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 0, 8); // store ip
    // Clear error flag
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 8, 8); // error = 0

    DefineLabel("__dns_cb_done");
    // LDP x29, x30, [sp], #0x30
    EmitWord(0xA8C00000 | ((0x30u / 8) << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitWord(0xD65F03C0); // RET
  }

  /// <summary>
  /// IP-only resolve stub (no DNS imports). Used when program doesn't use networking.
  /// Returns parsed IP or 0.
  /// </summary>
  private void EmitNetResolveHostIpOnly() {
    EmitRuntimeFunctionStart("__net_resolve_host", 1, 0x60);
    // Scan for IP: all digits and dots
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    DefineLabel("__nrh_ipo_scan");
    EmitWord(0x38401422); // LDRB W2, [X1], #1
    EmitCbz(ARM64Register.X2, "__nrh_ipo_is_ip");
    EmitCmpImm(ARM64Register.X2, 46);
    EmitBranchCond(ARM64ConditionCode.Eq, "__nrh_ipo_scan");
    EmitCmpImm(ARM64Register.X2, 48);
    EmitBranchCond(ARM64ConditionCode.Lt, "__nrh_ipo_fail");
    EmitCmpImm(ARM64Register.X2, 57);
    EmitBranchCond(ARM64ConditionCode.Gt, "__nrh_ipo_fail");
    EmitBranch("__nrh_ipo_scan");

    DefineLabel("__nrh_ipo_is_ip");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitReloadArg(0);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 24, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 25, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 26, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 27, 1);
    EmitWord(0xB9400000 | ((24u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0));
    EmitRuntimeFunctionEnd();

    DefineLabel("__nrh_ipo_fail");
    EmitMovRegImm(ARM64Register.X0, 0); // hostname without DNS = fail
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __net_parse_octet(cstring_x0) → octet value in X0, next char ptr in X1.
  /// Parses decimal digits from cstring until '.' or null terminator.
  /// </summary>
  private void EmitNetParseOctet() {
    DefineLabel("__net_parse_octet");
    // STP x29, x30, [sp, #-0x20]!
    var imm7 = (uint)((-0x20 / 8) & 0x7F);
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // X0 = input ptr, result in X0, next ptr in X1
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = scan ptr
    EmitMovRegImm(ARM64Register.X0, 0);                 // X0 = accumulated value

    DefineLabel("__npo_loop");
    // LDRB W2, [X1]
    EmitWord(0x39400022); // LDRB W2, [X1]
    // Check for null or '.'
    EmitCbz(ARM64Register.X2, "__npo_done");
    EmitCmpImm(ARM64Register.X2, 46); // '.'
    EmitBranchCond(ARM64ConditionCode.Eq, "__npo_dot");
    // value = value * 10 + (char - '0')
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 48, isAdd: false); // char - '0'
    // X0 = X0 * 10: MUL approach: X3 = 10, X0 = X0 * X3
    EmitMovRegImm(ARM64Register.X3, 10);
    EmitWord(0x9B037C00 | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.Xzr) << 10) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // MADD X0, X0, X3, XZR
    EmitWord(0x8B020000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // ADD X0, X0, X2
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
    EmitBranch("__npo_loop");

    DefineLabel("__npo_dot");
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true); // skip '.'

    DefineLabel("__npo_done");
    // X0 = value, X1 = next ptr
    // LDP x29, x30, [sp], #0x20
    EmitWord(0xA8C00000 | ((0x20u / 8) << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitWord(0xD65F03C0); // RET
  }

}
