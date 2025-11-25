# LLVM Elimination Plan: Custom x86-64 Backend for Maxon

## Overview

Replace LLVM with a fully custom backend that generates x86-64 machine code directly, implements basic optimizations, and produces PE/ELF executables with basic DWARF debug info support.

**Status**: Phases 1-8 Complete - MIR codegen created as parallel implementation  
**Target**: Self-contained compiler with no external code generation dependencies

---

## Unit Test Infrastructure ✓ COMPLETE

A comprehensive test suite has been created using Catch2 framework in `maxon-bin/tests/`:

| Test File | Test Cases | Assertions | Description |
|-----------|------------|------------|-------------|
| `test_mir.cpp` | 17 | 171 | MIR construction, types, instructions, control flow |
| `test_x86_encoding.cpp` | 23 | 223 | x86-64 instruction encoding, ModR/M, SIB, REX |
| `test_x86_codegen.cpp` | ~20 | ~100 | X86CodeGen: instruction selection, calling conventions |
| `test_regalloc.cpp` | 14 | 31 | Liveness analysis, linear-scan allocation, spilling |
| `test_executable_writers.cpp` | 16 | 61 | ELF/PE structure generation and validation |
| `test_dwarf.cpp` | 25 | 94 | DWARF debug info generation |
| `test_optimizer.cpp` | 24 | 80 | All optimization passes |
| `test_mir_parser.cpp` | 13 | 61 | MIR textual format parsing |
| `test_codegen_mir.cpp` | ~50 | ~150 | AST-to-MIR code generation |
| **Total** | **~200** | **~970** | |

**Build**: `cd maxon-bin/tests/build && ninja && ./run_all_backend_tests.exe`

All tests pass with `-Werror` / `-WX` (warnings-as-errors) enabled.

---

## Phase 1: Maxon IR (MIR) Design ✓ COMPLETE

### 1.1 Core IR Data Structures
- [x] Create `maxon-bin/mir/mir.h` with IR types, values, and instructions
- [x] Define type system: `MIRType` (i32, i64, f64, ptr, array, struct)
- [x] Define value representation: `MIRValue` (virtual registers, constants, globals)
- [x] Define instruction set: arithmetic, comparisons, memory, control flow, calls

### 1.2 Control Flow Representation
- [x] Implement `MIRBasicBlock` with predecessor/successor tracking
- [x] Implement `MIRFunction` containing basic blocks and parameters
- [x] Implement `MIRModule` containing functions and globals
- [x] Add SSA phi nodes for control flow merges

### 1.3 MIR Builder API
- [x] Create `maxon-bin/mir/mir_builder.h` with builder pattern API
- [x] Implement instruction creation helpers
- [x] Implement type conversion utilities
- [x] Add validation/verification pass

**Files Created**:
- `maxon-bin/mir/mir.h` (~400 lines)
- `maxon-bin/mir/mir.cpp` (~450 lines)
- `maxon-bin/mir/mir_builder.h` (~100 lines)
- `maxon-bin/mir/mir_builder.cpp` (~400 lines)

**Unit Tests** (`test_mir.cpp`):
- Type creation and comparison (i32, i64, f64, ptr, arrays, structs)
- Value creation (virtual registers, constants, globals)
- Instruction creation and operand handling
- Basic block construction with phi nodes
- Function and module assembly
- Control flow graph verification

---

## Phase 2: x86-64 Code Generation ✓ COMPLETE

### 2.1 Instruction Encoding
- [x] Create `maxon-bin/backend/x86_encoding.h` with instruction encoding tables
- [x] Implement ModR/M and SIB byte generation
- [x] Implement REX prefix handling for 64-bit operations
- [x] Implement immediate and displacement encoding

### 2.2 Instruction Selection
- [x] Create `maxon-bin/backend/x86_codegen.cpp` for MIR → x86 translation
- [x] Implement arithmetic operations (add, sub, mul, div, mod)
- [x] Implement comparison operations with condition codes
- [x] Implement memory operations (load, store, lea)
- [x] Implement control flow (jmp, jcc, call, ret)
- [x] Implement floating-point using SSE2 scalar instructions

### 2.3 Calling Conventions
- [x] Implement Microsoft x64 ABI (Windows): RCX, RDX, R8, R9 for args
- [x] Implement System V AMD64 ABI (Linux): RDI, RSI, RDX, RCX, R8, R9 for args
- [x] Handle stack alignment (16-byte boundary)
- [x] Implement shadow space (Windows) / red zone (Linux)

**Files Created**:
- `maxon-bin/backend/x86_encoding.h` (~200 lines)
- `maxon-bin/backend/x86_encoding.cpp` (~1000 lines)
- `maxon-bin/backend/x86_codegen.h` (~150 lines)
- `maxon-bin/backend/x86_codegen.cpp` (~800 lines)

**Unit Tests** (`test_x86_encoding.cpp`):
- MOV instruction variants (reg-reg, reg-imm, reg-mem, mem-reg)
- Arithmetic instructions (ADD, SUB, IMUL, IDIV with proper sign extension)
- Comparison instructions (CMP, TEST) and condition codes
- Control flow (JMP, Jcc, CALL, RET)
- Stack operations (PUSH, POP, LEA for stack access)
- SSE2 floating-point (MOVSD, ADDSD, SUBSD, MULSD, DIVSD, CVTSI2SD, etc.)
- REX prefix generation for 64-bit operations
- ModR/M and SIB byte encoding
- RIP-relative addressing

---

## Phase 3: Register Allocation ✓ COMPLETE

### 3.1 Liveness Analysis
- [x] Implement live-in/live-out computation per basic block (in regalloc.cpp)
- [x] Compute live ranges for each virtual register
- [x] BlockLiveness struct with def/use/liveIn/liveOut sets

### 3.2 Linear-Scan Allocator
- [x] Create `maxon-bin/backend/regalloc.cpp`
- [x] Implement linear-scan algorithm over live ranges
- [x] Handle 16 general-purpose registers (RAX-R15, excluding RSP/RBP)
- [x] Handle XMM0-XMM15 for floating-point
- [x] Implement spilling to stack when registers exhausted

### 3.3 Stack Frame Layout
- [x] Compute stack frame size for locals and spills
- [x] Generate prologue/epilogue (push RBP, sub RSP, etc.)
- [x] Handle callee-saved register preservation
- [x] Register coalescing for copy elimination

**Files Created**:
- `maxon-bin/backend/regalloc.h` (~150 lines)
- `maxon-bin/backend/regalloc.cpp` (~500 lines)

**Unit Tests** (`test_regalloc.cpp`):
- Live range computation (def/use tracking)
- Live-in/live-out analysis per basic block
- Register assignment for non-overlapping ranges
- Spilling when registers exhausted
- Callee-saved register handling
- Register coalescing for copy elimination
- Multi-block liveness with control flow

---

## Phase 4: Executable Generation ✓ COMPLETE

### 4.1 ELF Writer (Linux)
- [x] Create `maxon-bin/backend/elf_writer.cpp`
- [x] Implement ELF64 header generation
- [x] Implement section headers (.text, .data, .bss, .rodata)
- [x] Implement symbol table (.symtab) and string table (.strtab)
- [x] Implement program headers for loadable segments
- [x] Generate static executable with proper entry point

### 4.2 PE Writer (Windows)
- [x] Create `maxon-bin/backend/pe_writer.cpp`
- [x] Implement PE/COFF headers (DOS stub, PE signature, COFF header)
- [x] Implement optional header with entry point
- [x] Implement sections (.text, .data, .rdata, .idata, .reloc)
- [x] Implement import directory for DLL imports
- [x] Handle base relocations for ASLR support

**Files Created**:
- `maxon-bin/backend/elf_writer.h` (~250 lines)
- `maxon-bin/backend/elf_writer.cpp` (~450 lines)
- `maxon-bin/backend/pe_writer.h` (~230 lines)
- `maxon-bin/backend/pe_writer.cpp` (~500 lines)

### 4.3 Object File Support (Optional)
- [ ] Implement .o/.obj output for separate compilation
- [ ] Support linking multiple object files

### 4.4 End-to-End Validation ✓ COMPLETE
- [x] Generate PE executable that calls ExitProcess(42)
- [x] Verify executable runs and returns correct exit code
- [x] Import table correctly resolved by Windows loader

**Files Created**:
- `maxon-bin/backend/elf_writer.h` (~250 lines)
- `maxon-bin/backend/elf_writer.cpp` (~500 lines)
- `maxon-bin/backend/pe_writer.h` (~300 lines)
- `maxon-bin/backend/pe_writer.cpp` (~600 lines)

**Unit Tests** (`test_executable_writers.cpp`):
- ELF header validation (magic, class, machine type)
- ELF section generation (.text, .data, .bss, .symtab, .strtab)
- PE DOS header and PE signature
- PE optional header and data directories
- PE section table (.text, .data, .rdata, .idata)
- Import directory and IAT generation

---

## Phase 5: Basic Optimizations ✓ COMPLETE

### 5.1 Constant Folding
- [x] Create `maxon-bin/mir/optimizer.cpp`
- [x] Fold arithmetic on constants at compile time
- [x] Fold comparisons on constants
- [x] Propagate constants through assignments

### 5.2 Dead Code Elimination
- [x] Remove instructions whose results are never used
- [x] Remove unreachable basic blocks
- [x] Remove unused functions

### 5.3 Simple Inlining
- [x] Inline small leaf functions (< N instructions)
- [x] Inline functions called only once

### 5.4 Peephole Optimizations
- [x] Strength reduction (mul by power of 2 → shift)
- [x] Algebraic simplifications (x + 0 → x, x * 1 → x)
- [x] Redundant load/store elimination

**Files Created**:
- `maxon-bin/mir/optimizer.h` (~320 lines)
- `maxon-bin/mir/optimizer.cpp` (~1050 lines)

**Unit Tests** (`test_optimizer.cpp`):
- Constant folding: `add 3, 4` → `7`, `mul 5, 0` → `0`
- Constant propagation: `x = 5; y = x + 1` → `y = 6`
- Dead code elimination: remove unused assignments
- Unreachable block removal after unconditional jumps
- Strength reduction: `x * 8` → `x << 3`
- Algebraic simplification: `x + 0` → `x`, `x * 1` → `x`
- Load/store elimination: redundant load after store to same address
- Copy propagation: replace uses of copy with original value
- Simple function inlining: inline small leaf functions
- Integration tests: multiple passes working together

---

## Phase 6: DWARF Debug Info ✓ COMPLETE

### 6.1 Line Number Tables
- [x] Create `maxon-bin/backend/dwarf.cpp`
- [x] Generate `.debug_line` section with line number program
- [x] Map instruction addresses to source lines
- [x] Support multiple source files

### 6.2 Function Information
- [x] Generate `.debug_info` section with compile unit DIE
- [x] Generate function DIEs with name, address range
- [x] Generate parameter and local variable DIEs with locations

### 6.3 Supporting Sections
- [x] Generate `.debug_abbrev` section with abbreviation tables
- [x] Generate `.debug_str` section for string pool
- [x] Generate `.debug_aranges` for address range lookup

**Files Created**:
- `maxon-bin/backend/dwarf.h` (~300 lines)
- `maxon-bin/backend/dwarf.cpp` (~600 lines)

**Unit Tests** (`test_dwarf.cpp`):
- `.debug_abbrev` section format and encoding
- `.debug_info` compile unit DIE structure
- `.debug_info` function DIE with name and address ranges
- `.debug_info` variable DIE with location expressions
- `.debug_line` line number program header
- `.debug_line` opcodes (DW_LNS_advance_pc, DW_LNS_advance_line, etc.)
- `.debug_str` string table generation
- `.debug_aranges` address range lookup table
- ULEB128/SLEB128 encoding

---

## Phase 7: Runtime Library Port ✓ COMPLETE

The runtime library has been ported to textual MIR format with a new MIR parser. This allows the runtime to be written in a human-readable format similar to LLVM IR, then parsed and compiled alongside user code.

### 7.1 MIR Text Format Parser
- [x] Create `maxon-bin/mir/mir_parser.h` - Parser API and data structures
- [x] Create `maxon-bin/mir/mir_parser.cpp` - Full parser implementation
- [x] Support types: void, i1, i8, i32, i64, f64, ptr, arrays
- [x] Support instructions: arithmetic, comparisons, memory, control flow, phi
- [x] Support function definitions and declarations
- [x] Support module merging (runtime.mir + platform.mir)

### 7.2 Core Runtime Functions (runtime.mir)
- [x] `memset` - Memory initialization with byte value
- [x] `floor`, `ceil`, `round`, `trunc` - Math rounding functions
- [x] `sin`, `cos`, `tan` - Trigonometric functions with range reduction
- [x] `__sin_kernel`, `__cos_kernel`, `__tan_kernel` - Taylor series kernels
- [x] `__rem_pio2` - Range reduction for trigonometric functions

### 7.3 Platform Functions
- [x] Windows (`runtime_windows.mir`):
  - `malloc` via HeapAlloc, `free` via HeapFree
  - `exit` via ExitProcess, `write_stdout` via WriteFile
  - `__chkstk` for large stack allocations
- [x] Linux (`runtime_linux.mir`):
  - `malloc` via mmap syscall (no-op free)
  - `exit` via syscall 60, `write_stdout` via syscall 1

**Files Created**:
- `maxon-bin/mir/mir_parser.h` (~190 lines)
- `maxon-bin/mir/mir_parser.cpp` (~1250 lines)
- `maxon-runtime/runtime.mir` (~600 lines) - Core runtime in textual MIR
- `maxon-runtime/runtime_windows.mir` (~60 lines) - Windows platform functions
- `maxon-runtime/runtime_linux.mir` (~80 lines) - Linux platform functions

**Unit Tests** (`test_mir_parser.cpp`):
- Type parsing: all primitive types and arrays
- Arithmetic operations: add, sub, mul, div, mod (signed/unsigned)
- Comparison operations: icmp (eq, ne, slt, sgt, etc.), fcmp
- Control flow: br (conditional/unconditional), ret, phi
- Memory operations: alloca, load, store, getelementptr
- Conversions: sext, zext, trunc, fptosi, sitofp
- Function calls: call with return value, void calls
- Runtime functions: memset, floor, sin parsing (structural verification)

| Test File | Test Cases | Assertions | Description |
|-----------|------------|------------|-------------|
| `test_mir_parser.cpp` | 13 | 61 | MIR textual format parsing |

---

## Phase 8: Codegen Refactoring ✓ COMPLETE

A new `MIRCodeGenerator` class was created as a parallel implementation to the existing LLVM-based `CodeGenerator`. This allows gradual migration while maintaining the working LLVM backend.

### 8.1 Replace LLVM IR Generation
- [x] Create `codegen_mir.cpp` to emit MIR instead of LLVM IR
- [x] Create `codegen_mir_expr.cpp` for MIR expression generation
- [x] Create `codegen_mir_stmt.cpp` for MIR statement generation
- [x] Create `codegen_mir_function.cpp` for MIR function generation

### 8.2 Replace LLVM Output
- [x] Create `codegen_mir_output.cpp` to use new x86 backend
- [x] Implement `optimize()` with MIR optimizer passes
- [x] Implement `writeExecutable()` with PE/ELF writers
- [x] Platform-specific code for Windows and Linux

### 8.3 New Header Files
- [x] Create `codegen_mir.h` with MIR-based CodeGenerator class
- [x] Use MIR types (MIRModule, MIRBuilder, MIRType, MIRValue)
- [x] Define new MIRCodeGenerator class interface

**Files Created**:
- `maxon-bin/codegen_mir.h` (~120 lines) - Header with class definition
- `maxon-bin/codegen_mir.cpp` (~300 lines) - Main implementation
- `maxon-bin/codegen_mir/codegen_mir_expr.cpp` (~450 lines) - Expression generation
- `maxon-bin/codegen_mir/codegen_mir_stmt.cpp` (~650 lines) - Statement generation
- `maxon-bin/codegen_mir/codegen_mir_function.cpp` (~90 lines) - Function generation
- `maxon-bin/codegen_mir/codegen_mir_output.cpp` (~230 lines) - Output/executable generation
- `maxon-bin/tests/test_codegen_mir.cpp` (~1200 lines) - Unit tests

**CMakeLists.txt Updated**:
- Added MIR library files (mir.cpp, mir_builder.cpp, mir_parser.cpp, optimizer.cpp)
- Added backend files (x86_codegen.cpp, x86_encoding.cpp, regalloc.cpp)
- Added executable writers (pe_writer.cpp, elf_writer.cpp, dwarf.cpp)

**Unit Tests** (`test_codegen_mir.cpp`):
- Expression codegen: integer/float/bool literals, binary ops, unary ops
- Statement codegen: var/let declarations, if/else, while, for, break/continue
- Function codegen: parameters, locals, calls, recursion
- Type codegen: arrays, structs, array access, member access
- Entry point generation: _start function creation
- Integration: fibonacci, sum_array programs

---

## Phase 9: Build System Cleanup (TODO)

### 9.1 Remove LLVM Dependencies
- [ ] Remove LLVM from `CMakeLists.txt` (llvm_map_components_to_libnames)
- [ ] Remove LLD library linking
- [ ] Update include paths

### 9.2 Update Makefile
- [ ] Remove `llc` usage for runtime compilation
- [ ] Add assembly step for new runtime files
- [ ] Update runtime copy to bin folder

### 9.3 Remove LLVM Files
- [ ] Delete `llvm-config.txt`
- [ ] Delete `llvm.sh` and LLVM download scripts

**Files to Modify**:
- `CMakeLists.txt`
- `maxon-bin/CMakeLists.txt`
- `lsp-server/CMakeLists.txt`
- `Makefile`

**Files to Delete**:
- `llvm-config.txt`
- `llvm.sh`
- `scripts/download-llvm.sh`
- `scripts/build-llvm-linux.sh`

**Validation Tests**:
- Fresh clone → build succeeds without LLVM installed
- All `language-tests/fragments/` pass with new backend
- Benchmark: compile time and output size vs LLVM backend

---

## Testing Strategy

### Unit Test Infrastructure ✓ COMPLETE
- [x] Catch2 framework integrated in `maxon-bin/tests/`
- [x] CMake build with `-Werror` / `-WX` (warnings-as-errors)
- [x] `run_all_backend_tests.exe` runs all backend tests
- [x] Individual test executables for focused testing

### Continuous Validation
- [ ] Keep existing `language-tests/fragments/` test suite
- [ ] Run all spec tests after each phase
- [ ] Compare output against LLVM backend during transition

### Phase-Specific Unit Tests
| Phase | Test File | Status |
|-------|-----------|--------|
| Phase 1 (MIR) | `test_mir.cpp` | ✓ Complete |
| Phase 2 (x86 Encoding) | `test_x86_encoding.cpp` | ✓ Complete |
| Phase 2 (x86 CodeGen) | `test_x86_codegen.cpp` | ✓ Complete |
| Phase 3 (RegAlloc) | `test_regalloc.cpp` | ✓ Complete |
| Phase 4 (ELF/PE) | `test_executable_writers.cpp` | ✓ Complete |
| Phase 5 (Optimizer) | `test_optimizer.cpp` | ✓ Complete |
| Phase 6 (DWARF) | `test_dwarf.cpp` | ✓ Complete |
| Phase 7 (MIR Parser) | `test_mir_parser.cpp` | ✓ Complete |
| Phase 8 (AST→MIR) | `test_codegen_mir.cpp` | ✓ Complete |

### End-to-End Tests ✓ VERIFIED
- [x] Generate PE executable from hand-written x86 assembly
- [x] Execute generated PE and verify exit code
- [x] Import resolution (kernel32.dll!ExitProcess) works correctly

---

## Milestones

| Milestone | Description | Status |
|-----------|-------------|--------|
| M1 | MIR design complete, can represent all Maxon programs | ✓ Complete |
| M2 | x86 codegen works for simple arithmetic programs | ✓ Complete |
| M3 | Register allocator working, can compile real programs | ✓ Complete |
| M4 | PE executable generation working on Windows | ✓ Complete |
| M5 | ELF executable generation working on Linux | ✓ Complete (untested) |
| M6 | DWARF debug info generation | ✓ Complete |
| M7 | Unit test suite with 660+ assertions | ✓ Complete |
| M8 | End-to-end: generate and run PE executable | ✓ Complete |
| M9 | Optimizations implemented | ✓ Complete |
| M10 | Runtime library ported to x86-64 assembly | TODO |
| M11 | Codegen refactored to emit MIR | TODO |
| M12 | All language tests passing with new backend | TODO |
| M13 | LLVM fully removed, build simplified | TODO |

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        Maxon Compiler                           │
├─────────────────────────────────────────────────────────────────┤
│  Source (.maxon)                                                │
│       │                                                         │
│       ▼                                                         │
│  ┌─────────┐   ┌─────────┐   ┌───────────────────┐             │
│  │  Lexer  │ → │ Parser  │ → │ Semantic Analyzer │             │
│  └─────────┘   └─────────┘   └───────────────────┘             │
│       │                              │                          │
│       │         UNCHANGED            │                          │
│       ▼                              ▼                          │
├─────────────────────────────────────────────────────────────────┤
│                              │                                  │
│                              ▼                                  │
│                     ┌──────────────┐                            │
│                     │  MIR Builder │  (NEW - replaces LLVM IR)  │
│                     └──────────────┘                            │
│                              │                                  │
│                              ▼                                  │
│                     ┌──────────────┐                            │
│                     │  Optimizer   │  (NEW - replaces LLVM O3)  │
│                     └──────────────┘                            │
│                              │                                  │
│                              ▼                                  │
│                     ┌──────────────┐                            │
│                     │ Reg Alloc    │  (NEW)                     │
│                     └──────────────┘                            │
│                              │                                  │
│                              ▼                                  │
│                     ┌──────────────┐                            │
│                     │ x86 CodeGen  │  (NEW - replaces LLVM MC)  │
│                     └──────────────┘                            │
│                              │                                  │
│               ┌──────────────┴──────────────┐                   │
│               ▼                              ▼                  │
│      ┌──────────────┐              ┌──────────────┐            │
│      │  ELF Writer  │              │  PE Writer   │            │
│      │   (Linux)    │              │  (Windows)   │            │
│      └──────────────┘              └──────────────┘            │
│               │                              │                  │
│               ▼                              ▼                  │
│           a.out                          a.exe                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Notes

- **SSE2 for floating-point**: Use scalar SSE2 instructions (addsd, mulsd, etc.) instead of x87 FPU for simpler code and better performance on modern CPUs.
- **Start with Linux**: ELF format is simpler than PE. Get Linux working first, then port to Windows.
- **Keep LLVM backend temporarily**: During development, maintain the LLVM backend behind a flag to compare outputs and validate correctness.
- **Incremental migration**: Refactor one codegen file at a time, running tests after each change.
- **Unit tests first**: Write unit tests before implementing each phase to catch regressions early.
- **End-to-end validation**: The backend can now generate working PE executables - this proves the core infrastructure is sound.
