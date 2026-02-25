# Maxon Compiler Pipeline Design Document

## 1. Pipeline Architecture Overview
The Maxon compiler uses a progressive lowering pipeline built on the MLIR infrastructure. It translates High-Level Abstract Syntax (AST) into progressively lower-level dialects, applying optimizations at the semantic level that best suits them, before hitting a custom Instruction Selection (ISel) phase that emits machine code bytes directly. Maxon uses a custom x86-64 backend without LLVM and generates native executables directly.

**The Pipeline Phases:**
* **Phase 1: Frontend / `MaxHL` (High-Level)**: AST materialization, semantic validation, ownership/lifetime checking, and ranged-type bound insertion.
* **Phase 2: Mid-Level / `MaxMid`**: Desugaring of complex control flow (like `match` and `try`/`otherwise`), ARC injection (`retain`/`release`), and genericization.
* **Phase 3: Core Optimizations**: Utilization of standard MLIR dialects (`scf`, `cf`, `arith`, `memref`) for loop transformations, vectorization, and constant folding.
* **Phase 4: Machine IR / `MaxLIR` (Low-Level)**: Virtual register allocation, ABI lowering, and memory layout legalization.
* **Phase 5: Target Dialects (`MaxX64`, `MaxARM64`)**: Instruction selection, physical register allocation, and OS-specific stack frames.
* **Phase 6: Object Emission**: Native PE/COFF or ELF binary generation.

---

## 2. Dialect Definitions

### 2.1 `MaxHL` (Maxon High-Level Dialect)
Preserves the source code's intent. Code here is not heavily optimized; it exists to run strict semantic verification passes.

* **Types**: `!maxhl.ranged<base, min, max>` (e.g., `!maxhl.ranged<i64, 0, 150>`), `!maxhl.union<cases...>`, `!maxhl.enum<...>`, `!maxhl.struct<Name>`.
* **Op: maxhl.move**: Marks a variable as moved (transferred ownership).
* **Op: maxhl.clone**: Explicit deep copy of a `Cloneable` type (invoked via `.clone()`).
* **Op: maxhl.assert_range**: Emits a runtime panic if an expression falls out of bounds.
* **Op: maxhl.try**: Represents `try...otherwise` error handling.
* **Op: maxhl.match**: High-level pattern matching, including Rust-style range patterns.

### 2.2 `MaxMid` (Maxon Mid-Level Dialect)
The bridge between Maxon semantics and generic compiler concepts. Maxon-specific syntax sugar is removed.

* **Types**: `!maxmid.ptr<T>` (heap references — all structs are reference types), `!maxmid.tagged_union<tag_type, payload_type>`.
* **Op: maxmid.alloc**: Heap allocation.
* **Op: maxmid.retain / maxmid.release**: Reference counting ops for scope-based automatic cleanup.
* **Op: maxmid.construct_tagged**: Lowers union cases and errors to memory layouts (0=ok, 1=err tag).
* **Op: maxmid.extract_payload**: Safe extraction post-check.

### 2.3 `MaxLIR` (Maxon Low-Level IR)
Target-agnostic machine IR. It operates on virtual registers and abstract memory, preparing for the physical CPU.

* **Op: maxlir.vreg_alloc**: Allocates a virtual register.
* **Op: maxlir.load_ext**: Loads a smaller storage type (like `u8` for `int(0 to 150)`) and zero/sign-extends it to a 64-bit virtual register.
* **Op: maxlir.store_trunc**: Truncates a 64-bit register back to memory storage size.
* **Op: maxlir.call_abi**: Abstract call that handles Maxon's "by-value for read, by-ref for mutate" calling convention.

### 2.4 Target Dialects (`MaxX64`, `MaxARM64`)
1:1 mapping with hardware instructions. Operates on physical registers.

* **`MaxX64` Ops**: `maxx64.mov`, `maxx64.add`, `maxx64.imul`, `maxx64.jmp`, `maxx64.cmp`, `maxx64.push`, `maxx64.pop`.
* **`MaxARM64` Ops**: `maxarm.ldr`, `maxarm.str`, `maxarm.add`, `maxarm.b`, `maxarm.cmp`.

---

## 3. The Pass Pipeline Execution Order

### Phase 1: Frontend Semantic Passes (on `MaxHL`)
These passes enforce the language reference rules before any code is modified.

* **`-maxon-verify-purity`**: Validates pure/impure function returns. Throws `E3064` if pure results are discarded, or `E3065` if impure results lack explicit discard via `let _ =`.
* **`-maxon-ownership-checker`**: Performs compile-time dataflow analysis to track `Owned` vs `Moved` states, preventing use-after-move errors.
* **`-maxon-range-enforcement`**: Checks static constants against `!maxhl.ranged` boundaries and injects `maxhl.assert_range` for runtime checks.
* **`-maxon-alias-analysis`**: Validates that struct assignments create references (aliases) and that `.clone()` is used for explicit deep copies when needed.

### Phase 2: Mid-Level Lowering (`MaxHL` to `MaxMid` & Core MLIR)
* **`-maxon-lower-errors`**: Converts `throws ErrorType` functions to return a `!maxmid.tagged_union`. Lowers `maxhl.try` into conditional branches.
* **`-maxon-lower-match`**: Flattens `maxhl.match` into sequences of `scf.if` and `cf.switch`, handling range patterns like `1..=5`.
* **`-maxon-inject-arc`**: Inserts `maxmid.release` at block exits for heap allocations and `maxmid.retain` for aliased references.
* **`-maxon-lower-closures`**: Lambda lifting for closures, capturing variables by reference.

### Phase 3: SOTA Optimization (Standard MLIR)
Now operating heavily on standard MLIR (`scf`, `cf`, `arith`, and `memref`).

* **`-canonicalize` & `-cse`**: Common Subexpression Elimination and Constant Folding.
* **`-sccp`**: Proves range boundaries at compile time to safely strip out `maxhl.assert_range` ops.
* **`-affine-loop-fusion` / `-scf-for-loop-unrolling`**: Optimizes array iterations.
* **`-linalg-vectorization`**: Analyzes loops and converts standard operations into `vector` ops for SIMD.

### Phase 4: Machine Lowering (`MaxLIR`)
* **`-maxlir-legalize-types`**: Maps ranged array storage (e.g., `int(0 to 65535)` to `u16`) while ensuring all local arithmetic continues to use 64-bit operations.
* **`-maxlir-lower-abi`**: Transforms `maxlir.call_abi` to enforce Maxon's parameter passing (pass-by-reference for mutated parameters).

### Phase 5: Target Architecture Lowering (`MaxX64` / `MaxARM64`)
* **`-maxx64-instruction-selection`**: Pattern matches `MaxLIR` operations to specific hardware instructions and SIMD intrinsics.
* **`-maxx64-register-allocation`**: Replaces virtual registers with physical registers, inserting spill/fill code.
* **`-maxx64-prologue-epilogue`**: Allocates OS-specific stack requirements, like the 32-byte shadow space for Windows x64.

### Phase 6: Object Emission (Assembler Pass)
* **Label Resolution**: Calculates relative jump offsets.
* **Section Generation**: Builds `.text` (code), `.rdata` (string literals), and `.data` (global variables).
* **Binary Write**: Directly writes native headers (like PE/COFF for Windows) and links with `maxon-runtime-windows.obj`.

---

## 4. Specific Considerations

### 4.1 Cross-Platform & Target OS
Because the pipeline splits cleanly at Phase 5, compiling for different architectures is just a matter of swapping the target dialect pass. A Windows target configures the backend for PE executables, while a Linux target adjusts for ELF and the System V AMD64 ABI.

### 4.2 Handling "By-Value Read / By-Ref Mutate"
Maxon's parameter passing requires the frontend to track mutations automatically. In `MaxLIR`, when generating a call where the parameter is mutated, the caller allocates stack space, stores the value, and passes a `!maxmid.ptr<i64>` to the callee.
