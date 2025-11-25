# LLVM Elimination Plan: Custom x86-64 Backend for Maxon

## Overview

Replace LLVM with a fully custom backend that generates x86-64 machine code directly, implements basic optimizations, and produces PE/ELF executables with basic DWARF debug info support.

**Status**: Phase 1-4, 6 Complete - Core backend infrastructure implemented  
**Target**: Self-contained compiler with no external code generation dependencies

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
- `maxon-bin/mir/mir.h` (~300 lines)
- `maxon-bin/mir/mir.cpp` (~400 lines)
- `maxon-bin/mir/mir_builder.h` (~100 lines)
- `maxon-bin/mir/mir_builder.cpp` (~350 lines)

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
- `maxon-bin/backend/x86_encoding.cpp` (~700 lines)
- `maxon-bin/backend/x86_codegen.h` (~150 lines)
- `maxon-bin/backend/x86_codegen.cpp` (~650 lines)

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
- `maxon-bin/backend/regalloc.cpp` (~400 lines)

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

**Files to Create**:
- `maxon-bin/backend/elf_writer.h`
- `maxon-bin/backend/elf_writer.cpp`
- `maxon-bin/backend/pe_writer.h`
- `maxon-bin/backend/pe_writer.cpp`

---

## Phase 5: Basic Optimizations (TODO)

### 5.1 Constant Folding
- [ ] Create `maxon-bin/mir/optimizer.cpp`
- [ ] Fold arithmetic on constants at compile time
- [ ] Fold comparisons on constants
- [ ] Propagate constants through assignments

### 5.2 Dead Code Elimination
- [ ] Remove instructions whose results are never used
- [ ] Remove unreachable basic blocks
- [ ] Remove unused functions

### 5.3 Simple Inlining
- [ ] Inline small leaf functions (< N instructions)
- [ ] Inline functions called only once

### 5.4 Peephole Optimizations
- [ ] Strength reduction (mul by power of 2 → shift)
- [ ] Algebraic simplifications (x + 0 → x, x * 1 → x)
- [ ] Redundant load/store elimination

**Files to Create**:
- `maxon-bin/mir/optimizer.h`
- `maxon-bin/mir/optimizer.cpp`
- `maxon-bin/mir/const_fold.cpp`
- `maxon-bin/mir/dce.cpp`

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
- `maxon-bin/backend/dwarf.h` (~250 lines)
- `maxon-bin/backend/dwarf.cpp` (~500 lines)

---

## Phase 7: Runtime Library Port (TODO)

### 7.1 Core Runtime (x86-64 Assembly)
- [ ] Port `memset` to x86-64 assembly
- [ ] Port `malloc`/`free` implementations
- [ ] Port math functions (floor, ceil, round, trunc)

### 7.2 Trigonometric Functions
- [ ] Port `sin`, `cos`, `tan` with SSE2 instructions
- [ ] Port kernel functions (`__sin_kernel`, `__cos_kernel`, `__tan_kernel`)
- [ ] Port range reduction (`__rem_pio2`)

### 7.3 Platform Functions
- [ ] Port Windows-specific functions (HeapAlloc wrapper, WriteFile wrapper)
- [ ] Port Linux-specific functions (syscall wrappers)
- [ ] Create build system to assemble runtime

**Files to Create**:
- `maxon-runtime/runtime_x64.asm` (or .s)
- `maxon-runtime/platform_windows_x64.asm`
- `maxon-runtime/platform_linux_x64.asm`

---

## Phase 8: Codegen Refactoring

### 8.1 Replace LLVM IR Generation
- [ ] Modify `codegen.cpp` to emit MIR instead of LLVM IR
- [ ] Update `codegen_expr.cpp` for MIR expression generation
- [ ] Update `codegen_stmt.cpp` for MIR statement generation
- [ ] Update `codegen_function.cpp` for MIR function generation

### 8.2 Replace LLVM Output
- [ ] Update `codegen_output.cpp` to use new x86 backend
- [ ] Replace `optimize()` with MIR optimizer
- [ ] Replace `writeObjectFile()` with ELF/PE writer
- [ ] Replace `writeExecutable()` with ELF/PE writer

### 8.3 Update Header Files
- [ ] Remove LLVM includes from `codegen.h`
- [ ] Replace LLVM types with MIR types
- [ ] Update CodeGenerator class interface

**Files to Modify**:
- `maxon-bin/codegen.h`
- `maxon-bin/codegen.cpp`
- `maxon-bin/codegen/*.cpp`

---

## Phase 9: Build System Cleanup

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

---

## Testing Strategy

### Continuous Validation
- [ ] Keep existing `language-tests/fragments/` test suite
- [ ] Run all spec tests after each phase
- [ ] Compare output against LLVM backend during transition

### Phase-Specific Tests
- [ ] Phase 1: Unit tests for MIR construction and validation
- [ ] Phase 2: Assembly output verification for simple functions
- [ ] Phase 3: Register allocation correctness tests
- [ ] Phase 4: Binary format validation (readelf, dumpbin)
- [ ] Phase 5: Optimization correctness tests
- [ ] Phase 6: Debugger integration tests (gdb, lldb)

---

## Milestones

| Milestone | Description | Target |
|-----------|-------------|--------|
| M1 | MIR design complete, can represent all Maxon programs | Week 2 |
| M2 | x86 codegen works for simple arithmetic programs | Week 4 |
| M3 | Register allocator working, can compile real programs | Week 6 |
| M4 | ELF executable generation working on Linux | Week 8 |
| M5 | PE executable generation working on Windows | Week 10 |
| M6 | All language tests passing with new backend | Week 12 |
| M7 | Optimizations implemented, performance parity | Week 14 |
| M8 | DWARF debug info working | Week 16 |
| M9 | LLVM fully removed, build simplified | Week 18 |

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
