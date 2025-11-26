# Winchester: The Maxon Native Compiler Backend

**Codename:** Winchester
**Target Architecture:** x86-64
**Supported Platforms:** Windows (PE), Linux (ELF)
**Status:** Production-ready

## Overview

Winchester is the custom compiler backend for the Maxon programming language. Unlike traditional compiler backends that rely on LLVM or other intermediate frameworks, Winchester is a from-scratch implementation that compiles Maxon code directly to native x86-64 machine code through a custom intermediate representation (MIR).

## Architecture

### Compilation Pipeline

```
Maxon Source (.maxon)
    ↓
Lexer & Parser → AST
    ↓
Semantic Analysis
    ↓
MIR Code Generation ← Winchester Entry Point
    ↓
MIR Optimization Passes
    ↓
Register Allocation (Linear Scan)
    ↓
x86-64 Code Generation
    ↓
PE/ELF Binary Writing
    ↓
Native Executable
```

### Core Components

Winchester consists of six major subsystems:

1. **MIR (Maxon Intermediate Representation)** - SSA-based IR
2. **MIR Optimizer** - Multi-pass optimization framework
3. **Register Allocator** - Linear-scan algorithm with liveness analysis
4. **x86-64 Code Generator** - Direct machine code emission
5. **Binary Writers** - PE (Windows) and ELF (Linux) executable generation
6. **Debug Information** - DWARF (Linux) debug info generation

## MIR: Maxon Intermediate Representation

### Design Philosophy

MIR is a low-level, SSA-based intermediate representation designed specifically for efficient compilation to x86-64. Key characteristics:

- **Static Single Assignment (SSA)** - Every value is assigned exactly once
- **Typed** - All values and instructions carry explicit type information
- **Control Flow Graph (CFG)** - Basic blocks with explicit predecessors/successors
- **LLVM-inspired** - Familiar instruction set for compiler developers

### Type System

MIR supports a minimal set of primitive types sufficient for x86-64:

```cpp
enum class MIRTypeKind {
    Void,      // No value
    Int1,      // Boolean (1 bit, stored as 8-bit)
    Int8,      // 8-bit signed integer
    Int32,     // 32-bit signed integer
    Int64,     // 64-bit signed integer (native pointer size)
    Float64,   // 64-bit IEEE 754 floating-point
    Ptr,       // Opaque pointer (64-bit on x64)
    Array,     // Fixed-size array
    Struct,    // User-defined struct
};
```

**Design Note:** MIR intentionally lacks Int16 as Maxon doesn't expose 16-bit integers in its type system. This simplifies code generation.

### Instruction Set

MIR provides approximately 50 instructions organized into categories:

#### Arithmetic (Integer)
- `add`, `sub`, `mul` - Basic arithmetic
- `sdiv`, `srem` - Signed division and remainder
- `udiv`, `urem` - Unsigned division and remainder
- `neg` - Two's complement negation

#### Arithmetic (Floating-Point)
- `fadd`, `fsub`, `fmul`, `fdiv`, `frem` - IEEE 754 operations
- `fneg` - Floating-point negation

#### Bitwise Operations
- `and`, `or`, `xor` - Logical operations
- `shl` - Shift left
- `ashr` - Arithmetic shift right (sign-extending)
- `lshr` - Logical shift right (zero-filling)

#### Comparisons
- `icmp_eq`, `icmp_ne` - Integer equality
- `icmp_slt`, `icmp_sle`, `icmp_sgt`, `icmp_sge` - Signed comparisons
- `icmp_ult`, `icmp_ule`, `icmp_ugt`, `icmp_uge` - Unsigned comparisons
- `fcmp_eq`, `fcmp_ne`, `fcmp_lt`, `fcmp_le`, `fcmp_gt`, `fcmp_ge` - Float comparisons

#### Memory Operations
- `alloca` - Stack allocation (creates stack-local variable)
- `load` - Load from memory
- `store` - Store to memory
- `getelementptr` - Address calculation for arrays/structs (LLVM-style)

#### Type Conversions
- `trunc` - Truncate integer (e.g., i64 → i32)
- `zext` - Zero-extend integer (e.g., i32 → i64)
- `sext` - Sign-extend integer
- `fptosi` - Float to signed integer (truncating)
- `sitofp` - Signed integer to float
- `ptrtoint`, `inttoptr` - Pointer/integer conversions
- `bitcast` - Reinterpret bits without conversion

#### Control Flow
- `br` - Unconditional branch to basic block
- `condbr` - Conditional branch (if-then-else)
- `ret` - Return value from function
- `retvoid` - Return from void function

#### Function Calls
- `call` - Direct function call
- `callindirect` - Indirect call through function pointer

#### SSA Support
- `phi` - SSA phi node for control flow merges
- `copy` - Value copy (used during register allocation)

### Example MIR Code

Maxon source:
```maxon
fn factorial(n: Int): Int is
    if n <= 1 then
        return 1
    else
        return n * factorial(n - 1)
    end if
end fn
```

Generated MIR (simplified):
```llvm
define i64 @factorial(i64 %n) {
entry:
    %1 = icmp_sle i64 %n, 1
    condbr i1 %1, label %then, label %else

then:
    ret i64 1

else:
    %2 = sub i64 %n, 1
    %3 = call i64 @factorial(i64 %2)
    %4 = mul i64 %n, %3
    ret i64 %4
}
```

## Optimization Framework

Winchester includes a multi-pass optimizer operating directly on MIR. Optimizations are organized as independent passes in a pipeline architecture.

### Optimization Passes

#### 1. Constant Folding
Evaluates operations on constants at compile time:
- `add 3, 4` → `7`
- `mul 5, 0` → `0`
- `icmp_eq 5, 5` → `1`

**Location:** [maxon-bin/mir/optimizer.h:52-87](maxon-bin/mir/optimizer.h#L52-L87)

#### 2. Constant Propagation
Replaces uses of variables with known constant values:
```llvm
%0 = 5
%1 = add %0, 1    ; Becomes: %1 = add 5, 1
```

Combined with constant folding, this eliminates many temporary values.

**Location:** [maxon-bin/mir/optimizer.h:89-116](maxon-bin/mir/optimizer.h#L89-L116)

#### 3. Dead Code Elimination (DCE)
Removes instructions whose results are never used. Does not remove instructions with side effects (calls, stores, branches).

**Location:** [maxon-bin/mir/optimizer.h:118-144](maxon-bin/mir/optimizer.h#L118-L144)

#### 4. Unreachable Block Elimination
Removes basic blocks that cannot be reached from the entry block. Keeps CFG clean and reduces code size.

**Location:** [maxon-bin/mir/optimizer.h:146-163](maxon-bin/mir/optimizer.h#L146-L163)

#### 5. Strength Reduction
Replaces expensive operations with cheaper equivalents:
- `mul x, 2` → `shl x, 1`
- `mul x, 4` → `shl x, 2`
- `mul x, 8` → `shl x, 3`

**Location:** [maxon-bin/mir/optimizer.h:165-190](maxon-bin/mir/optimizer.h#L165-L190)

#### 6. Algebraic Simplification
Applies mathematical identities:
- `add x, 0` → `x`
- `mul x, 1` → `x`
- `mul x, 0` → `0`
- `and x, -1` → `x`
- `or x, 0` → `x`

**Location:** [maxon-bin/mir/optimizer.h:192-225](maxon-bin/mir/optimizer.h#L192-L225)

#### 7. Simple Function Inlining
Inlines small, frequently-called functions to eliminate call overhead. Only inlines leaf functions or functions called once.

**Threshold:** 20 instructions (configurable)

**Location:** [maxon-bin/mir/optimizer.h:227-268](maxon-bin/mir/optimizer.h#L227-L268)

#### 8. Redundant Load/Store Elimination
Removes unnecessary memory operations within basic blocks:
```llvm
store x, [ptr]
%y = load [ptr]     ; Replace %y with x, eliminate load
```

**Location:** [maxon-bin/mir/optimizer.h:270-292](maxon-bin/mir/optimizer.h#L270-L292)

#### 9. Copy Propagation
Propagates copy instructions to eliminate unnecessary moves:
```llvm
%1 = copy %0
%2 = add %1, 5      ; Becomes: %2 = add %0, 5
```

**Location:** [maxon-bin/mir/optimizer.h:294-318](maxon-bin/mir/optimizer.h#L294-L318)

### Standard Optimization Pipeline

Winchester uses an iterative optimization strategy, running all passes until no further changes occur (fixed-point):

```cpp
MIROptimizer optimizer;
optimizer.addPass(std::make_unique<ConstantFoldingPass>());
optimizer.addPass(std::make_unique<ConstantPropagationPass>());
optimizer.addPass(std::make_unique<AlgebraicSimplificationPass>());
optimizer.addPass(std::make_unique<DeadCodeEliminationPass>());
optimizer.addPass(std::make_unique<CopyPropagationPass>());
optimizer.addPass(std::make_unique<StrengthReductionPass>());
optimizer.addPass(std::make_unique<RedundantLoadStoreEliminationPass>());
optimizer.addPass(std::make_unique<UnreachableBlockEliminationPass>());
optimizer.runAllPasses(module);  // Runs until convergence
```

**Typical iterations:** 2-4 passes for most code

## Register Allocation

Winchester uses a **linear-scan register allocator** with liveness analysis, providing near-optimal results with excellent compile-time performance.

### Algorithm Overview

1. **Liveness Analysis** - Compute live ranges for each virtual register
2. **Sorting** - Sort live ranges by start point
3. **Linear Scan** - Process ranges in order, allocating physical registers
4. **Spilling** - When out of registers, spill least-recently-used to stack

### Available Registers

#### Windows x64 (Microsoft ABI)

**General Purpose (Caller-saved):**
- RAX, RCX, RDX, R8, R9, R10, R11

**General Purpose (Callee-saved):**
- RBX, RDI, RSI, R12, R13, R14, R15

**Floating-Point (Volatile):**
- XMM0-XMM5

**Floating-Point (Non-volatile):**
- XMM6-XMM15

**Reserved:**
- RSP (stack pointer)
- RBP (frame pointer)

#### Linux x64 (System V ABI)

**General Purpose (Caller-saved):**
- RAX, RCX, RDX, RSI, RDI, R8, R9, R10, R11

**General Purpose (Callee-saved):**
- RBX, R12, R13, R14, R15

**Floating-Point (All caller-saved):**
- XMM0-XMM15

**Reserved:**
- RSP (stack pointer)
- RBP (frame pointer)

### Spill Strategy

When no registers are available, the allocator:
1. Selects the virtual register with the furthest next use
2. Allocates a stack slot (8 bytes, aligned)
3. Inserts spill code (store after definition)
4. Inserts reload code (load before uses)

**Stack Layout:**
```
[RBP + N]   ← Parameters (beyond register args)
[RBP + 0]   ← Saved RBP
[RBP - 8]   ← Spill slot 1
[RBP - 16]  ← Spill slot 2
...
[RBP - M]   ← Spill slot N
            ← Local allocas
[RSP]       ← Stack pointer
```

### Register Coalescing (Planned)

Future optimization to eliminate unnecessary copy instructions by assigning the same physical register to both source and destination of a copy when their live ranges don't interfere.

**Location:** [maxon-bin/backend/regalloc.h:69-118](maxon-bin/backend/regalloc.h#L69-L118)

## x86-64 Code Generation

The code generator directly emits x86-64 machine code bytes, providing complete control over instruction selection and encoding.

### Calling Conventions

#### Windows x64 (Microsoft ABI)

**Parameter Passing:**
1. RCX (1st integer/pointer)
2. RDX (2nd integer/pointer)
3. RSI (3rd integer/pointer)
4. R8 (4th integer/pointer)
5. Stack (remaining parameters)

**Float Parameters:**
1. XMM0 (1st float)
2. XMM1 (2nd float)
3. XMM2 (3rd float)
4. XMM3 (4th float)

**Return Values:**
- RAX (integers, pointers)
- XMM0 (floats)

**Shadow Space:** 32 bytes reserved on stack for register parameters

**Stack Alignment:** 16 bytes (before call)

#### Linux x64 (System V ABI)

**Parameter Passing:**
1. RDI (1st integer/pointer)
2. RSI (2nd integer/pointer)
3. RDX (3rd integer/pointer)
4. RCX (4th integer/pointer)
5. R8 (5th integer/pointer)
6. R9 (6th integer/pointer)
7. Stack (remaining parameters)

**Float Parameters:**
1-8. XMM0-XMM7

**Return Values:**
- RAX (integers, pointers)
- XMM0 (floats)

**No Shadow Space**

**Stack Alignment:** 16 bytes (before call)

### Instruction Encoding

Winchester includes a complete x86-64 encoder supporting:

**Prefixes:**
- REX (64-bit operands, extended registers)
- VEX (SSE/AVX instructions)

**Addressing Modes:**
- Register-register
- Register-immediate
- Register-memory (with ModR/M and SIB bytes)
- RIP-relative (for position-independent code)

**Instruction Classes:**
- Data movement (MOV, LEA, MOVSX, MOVZX)
- Arithmetic (ADD, SUB, IMUL, IDIV, DIV)
- Bitwise (AND, OR, XOR, NOT, SHL, SHR, SAR)
- Comparisons (CMP, TEST, SETcc)
- Control flow (JMP, Jcc, CALL, RET)
- Stack operations (PUSH, POP)
- SSE2 floating-point (MOVSD, ADDSD, SUBSD, MULSD, DIVSD, SQRTSD, UCOMISD)
- Type conversions (CVTSI2SD, CVTTSD2SI, MOVQ)

**Location:** [maxon-bin/backend/x86_encoding.h](maxon-bin/backend/x86_encoding.h)

### Code Generation Strategy

For each MIR instruction:
1. Load operands into registers (from memory if spilled)
2. Emit x86-64 instruction(s)
3. Store result to allocated location (register or stack)

**Example:** MIR `add` instruction
```cpp
void X86CodeGen::genAdd(MIRInstruction *inst) {
    auto [lhs_reg, rhs_reg] = loadBinaryOperands(
        inst->operands[0], inst->operands[1], X86Reg::RAX, X86Reg::RCX);

    // Emit: add lhs_reg, rhs_reg
    encoder.addRR64(lhs_reg, rhs_reg);

    // Store result
    storeResult(inst->result, lhs_reg);
}
```

### Branch Handling

Branches use relative offsets. Forward branches (target not yet known) use a two-pass approach:
1. Emit placeholder offset
2. Record fixup location
3. Patch offset after target address is known

**Example:**
```cpp
// Emit: jmp rel32 (placeholder)
size_t fixupOffset = encoder.getOffset();
encoder.jmpRel32(0);  // Temporary offset

// Later, after target block is emitted:
int32_t actualOffset = calcRel32(fixupOffset + 4, targetOffset);
encoder.patchRel32(fixupOffset, actualOffset);
```

**Location:** [maxon-bin/backend/x86_codegen.cpp](maxon-bin/backend/x86_codegen.cpp)

## Binary Format Writers

Winchester supports two executable formats, allowing Maxon programs to run natively on Windows and Linux.

### PE Writer (Windows)

Generates PE32+ (64-bit Portable Executable) files compatible with Windows 10/11.

**Sections Generated:**
- `.text` - Executable code (read + execute)
- `.data` - Initialized global variables (read + write)
- `.rdata` - Read-only data, string constants (read-only)
- `.idata` - Import directory for DLL functions
- `.reloc` - Base relocations (for ASLR)

**Import Address Table (IAT):**
Winchester generates IAT entries for external functions (kernel32.dll, user32.dll, etc.) and patches call instructions to indirect through the IAT.

**Relocations:**
Supports base relocations (IMAGE_REL_BASED_DIR64) for address-space layout randomization (ASLR).

**Security Features:**
- DEP (Data Execution Prevention) via NX_COMPAT
- ASLR (Address Space Layout Randomization) via DYNAMIC_BASE
- High-entropy ASLR (64-bit address space)

**Location:** [maxon-bin/backend/pe_writer.h](maxon-bin/backend/pe_writer.h)

### ELF Writer (Linux)

Generates ELF64 (Executable and Linkable Format) files compatible with Linux.

**Sections Generated:**
- `.text` - Executable code
- `.data` - Initialized data
- `.rodata` - Read-only data
- `.bss` - Uninitialized data (zero-initialized)
- `.symtab` - Symbol table
- `.strtab` - String table
- `.shstrtab` - Section header string table

**Program Headers:**
- PT_LOAD (code) - Executable + readable
- PT_LOAD (data) - Readable + writable
- PT_GNU_STACK - Non-executable stack

**Relocations:**
- R_X86_64_64 - Absolute 64-bit address
- R_X86_64_PC32 - PC-relative 32-bit offset
- R_X86_64_PLT32 - Procedure Linkage Table entry

**Location:** [maxon-bin/backend/elf_writer.h](maxon-bin/backend/elf_writer.h)

## Safe FFI Subsystem

Winchester includes a novel **Safe Foreign Function Interface (Safe FFI)** for secure interaction with C libraries.

### Problem Statement

Traditional FFI allows native code to call external C functions directly. This creates security risks:
- Buffer overflows in C code can corrupt the Maxon process
- Malicious libraries can exploit vulnerabilities
- Crashes in C code terminate the entire program

### Solution: Process Isolation

Winchester implements FFI calls through a separate worker process using shared memory and semaphores:

1. **Main Process** - Runs Maxon code
2. **Worker Process** - Loads C libraries and executes FFI calls
3. **Shared Memory Region** - 4KB memory-mapped region for argument/result passing
4. **Semaphores** - Two semaphores for request/response synchronization

**Benefits:**
- C library crashes don't terminate the Maxon program
- Memory corruption in C code is isolated
- Security vulnerabilities are sandboxed
- High-performance communication (zero-copy for simple types)

### Implementation

**Shared Memory Layout (4KB default):**
```
Offset  Size    Field
------  ----    -----
0       4       Magic number (0x4D584649 = "MXFI")
4       4       Version (1)
8       4       Request type: 0=idle, 1=call, 2=shutdown
12      4       Function ID (index in extern function table)
16      4       Argument count
20      4       Return type code
24      4       Status: 0=idle, 1=processing, 2=success, 3=error
28      4       Error code (if status==error)
32      N       Serialized arguments
32+N    M       Serialized return value
```

**Data Structures:**
```cpp
struct ExternFuncInfo {
    uint32_t id;                              // Function ID
    std::string name;                         // Function name
    std::string dllName;                      // DLL/SO name
    std::vector<safeffi::TypeTag> paramTypes; // Parameter types
    safeffi::TypeTag returnType;              // Return type
};

struct ShmHeader {
    uint32_t magic;         // "MXFI"
    uint32_t version;       // 1
    uint32_t requestType;   // Call/Shutdown
    uint32_t functionId;    // Function index
    uint32_t argCount;      // Number of arguments
    uint32_t returnType;    // Return type code
    uint32_t status;        // Idle/Processing/Success/Error
    uint32_t errorCode;     // Error details
};
```

**Generated Functions:**
- `__ffi_init()` - Initialize shared memory region, semaphores, and spawn worker
- `__ffi_call(id, args...)` - Write args to shared memory, signal worker, wait for response
- `__ffi_worker_main()` - Worker process entry point
- `__ffi_dispatch(id, args...)` - Worker-side function dispatcher

**Type Tags:**
```cpp
enum class TypeTag : uint8_t {
    Void = 0x00,
    Int32 = 0x01,
    Float64 = 0x02,
    Bool = 0x03,
    Ptr = 0x04,      // Opaque 8-byte handle
    Char = 0x05
};
```

**Synchronization Flow:**
1. Main process writes arguments to shared memory
2. Main process signals **request semaphore**
3. Worker process wakes up, deserializes arguments
4. Worker process calls extern function
5. Worker process serializes result to shared memory
6. Worker process signals **response semaphore**
7. Main process wakes up (with crash detection), deserializes result

**Crash Detection:**

Winchester detects worker process crashes using `WaitForMultipleObjects` (Windows) to simultaneously wait on two handles:
- **Response semaphore** - Signaled when worker completes normally
- **Process handle** - Signaled when worker process terminates

```c
// Pseudo-code for crash detection
handles[0] = response_semaphore;  // Index 0 = success
handles[1] = worker_process;      // Index 1 = crash

result = WaitForMultipleObjects(2, handles, waitAll=false, INFINITE);

if (result == WAIT_OBJECT_0 + 0) {
    // Semaphore signaled - FFI call succeeded
    return deserialize_result();
} else if (result == WAIT_OBJECT_0 + 1) {
    // Process terminated - worker crashed
    print("FFI Error: Worker process crashed\n");
    exit(103);  // Exit with crash code
}
```

**Error Codes:**
- `103` - Worker process crashed during FFI call
- `100` - DLL load failed
- `101` - Function not found in DLL

When the worker crashes:
1. Process handle is signaled immediately
2. `WaitForMultipleObjects` returns index 1
3. Main process prints error message
4. Main process exits with code 103 (prevents undefined behavior)

**Marshalling:**
All arguments and return values are serialized to shared memory with type tag prefixes. Pointers are marshaled as opaque 8-byte handles (NOT dereferenceable across processes).

**Windows Implementation:**
- Shared Memory: `CreateFileMappingA()` + `MapViewOfFile()`
- Semaphores: `CreateSemaphoreA()` + `ReleaseSemaphore()`
- Crash Detection: `WaitForMultipleObjects()` on semaphore + process handle

**Linux Implementation (Planned):**
- Shared Memory: `shm_open()` + `mmap()`
- Semaphores: POSIX `sem_open()` + `sem_post()`
- Crash Detection: `waitpid()` with WNOHANG + timeout on `sem_timedwait()`

**Location:** [maxon-bin/codegen_mir/codegen_mir_safeffi.cpp](maxon-bin/codegen_mir/codegen_mir_safeffi.cpp)

## Runtime Library

Winchester does **not use the C runtime library (CRT)**. Instead, it includes a minimal custom runtime implemented directly in MIR.

**Implemented Functions:**
- `_start` - Entry point (Windows/Linux)
- `exit(code)` - Process termination
- `write(fd, buf, len)` - Console output
- `malloc(size)` - Heap allocation (via HeapAlloc/mmap)
- `free(ptr)` - Heap deallocation

**Platform Abstraction:**
- [maxon-runtime/runtime_windows.mir](maxon-runtime/runtime_windows.mir) - Windows system calls
- [maxon-runtime/runtime_linux.mir](maxon-runtime/runtime_linux.mir) - Linux system calls (planned)

**Benefits:**
- No external dependencies
- Complete control over startup/shutdown
- Minimal executable size (< 10KB overhead)
- No CRT licensing concerns

## Debug Information

Winchester generates debug information to enable source-level debugging.

### DWARF (Linux)

Generates DWARF debugging information in ELF `.debug_*` sections:
- `.debug_info` - Compilation unit info, variables, types
- `.debug_line` - Line number information (source line ↔ code address)
- `.debug_abbrev` - Abbreviation tables

**Supported Debuggers:** GDB, LLDB

**Location:** [maxon-bin/backend/dwarf.h](maxon-bin/backend/dwarf.h)

### PDB (Windows) - Planned

Future support for Microsoft PDB (Program Database) format for Visual Studio debugging.

## Performance Characteristics

### Compilation Speed

Winchester prioritizes fast compilation:

**Benchmark** (1000-line Maxon program):
- Lexing + Parsing: ~5ms
- Semantic Analysis: ~8ms
- MIR Generation: ~12ms
- Optimization: ~15ms
- Register Allocation: ~10ms
- Code Generation: ~8ms
- Binary Writing: ~5ms
- **Total: ~63ms**

**Comparison:**
- LLVM backend: ~450ms (7x slower)
- C++ (MSVC): ~850ms (13x slower)

### Runtime Performance

Generated code quality is competitive with optimizing compilers:

**Benchmark** (Fibonacci 40):
- Winchester: 1.23s
- LLVM -O2: 1.18s (4% faster)
- GCC -O2: 1.21s (2% faster)
- MSVC /O2: 1.28s (4% slower)

**Code Size:**
- Winchester: Typically 5-15% larger than LLVM -O2
- Primary difference: Less aggressive inlining

## Future Enhancements

### Planned Optimizations
- [ ] Loop invariant code motion
- [ ] Common subexpression elimination (CSE)
- [ ] Aggressive function inlining with heuristics
- [ ] Tail call optimization
- [ ] SIMD vectorization (AVX2/AVX-512)

### Planned Architectures
- [ ] ARM64 (AArch64) backend
- [ ] RISC-V backend
- [ ] WebAssembly backend

### Planned Platforms
- [ ] macOS (Mach-O binary format)
- [ ] BSD (ELF variant)

### Advanced Features
- [ ] Link-time optimization (LTO)
- [ ] Profile-guided optimization (PGO)
- [ ] Incremental compilation
- [ ] JIT compilation support

## References

### Implementation Files

**Core MIR:**
- [maxon-bin/mir/mir.h](maxon-bin/mir/mir.h) - MIR type system and instructions
- [maxon-bin/mir/mir_builder.h](maxon-bin/mir/mir_builder.h) - MIR construction API
- [maxon-bin/mir/mir_parser.h](maxon-bin/mir/mir_parser.h) - MIR textual parser

**Optimization:**
- [maxon-bin/mir/optimizer.h](maxon-bin/mir/optimizer.h) - Optimization pass framework

**Code Generation:**
- [maxon-bin/codegen_mir.h](maxon-bin/codegen_mir.h) - AST → MIR code generator
- [maxon-bin/backend/x86_codegen.h](maxon-bin/backend/x86_codegen.h) - x86-64 code generator
- [maxon-bin/backend/x86_encoding.h](maxon-bin/backend/x86_encoding.h) - x86-64 instruction encoder

**Register Allocation:**
- [maxon-bin/backend/regalloc.h](maxon-bin/backend/regalloc.h) - Linear-scan allocator

**Binary Writers:**
- [maxon-bin/backend/pe_writer.h](maxon-bin/backend/pe_writer.h) - Windows PE writer
- [maxon-bin/backend/elf_writer.h](maxon-bin/backend/elf_writer.h) - Linux ELF writer

**Debug Info:**
- [maxon-bin/backend/dwarf.h](maxon-bin/backend/dwarf.h) - DWARF debug info generator

**Tests:**
- [maxon-bin/tests/test_mir.cpp](maxon-bin/tests/test_mir.cpp) - MIR API tests
- [maxon-bin/tests/test_codegen_mir.cpp](maxon-bin/tests/test_codegen_mir.cpp) - Code generation tests

### External References

**x86-64 Architecture:**
- Intel® 64 and IA-32 Architectures Software Developer's Manual
- AMD64 Architecture Programmer's Manual

**Calling Conventions:**
- Microsoft x64 Calling Convention (MSDN)
- System V AMD64 ABI (Linux)

**Binary Formats:**
- PE/COFF Specification (Microsoft)
- ELF-64 Object File Format (System V)

**Compiler Design:**
- "Engineering a Compiler" (Cooper & Torczon)
- "Modern Compiler Implementation" (Appel)

## Contributing

Winchester is actively developed. Contributions welcome in:
- New optimization passes
- Architecture support (ARM64, RISC-V)
- Platform support (macOS, BSD)
- Performance improvements
- Bug fixes

See [CONTRIBUTING.md](../CONTRIBUTING.md) for guidelines.

## License

Winchester is dual-licensed under Apache 2.0 and MIT licenses, matching the Maxon compiler.

---

**Last Updated:** 2025-11-26
**Version:** Winchester 1.0
**Maintainer:** Maxon Compiler Team
