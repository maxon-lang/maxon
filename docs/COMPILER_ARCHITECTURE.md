# Maxon Compiler Architecture

**Version:** 1.3  
**Last Updated:** 2025-12-01  
**Status:** Production-ready

## Overview

The Maxon compiler is a from-scratch native compiler that transforms Maxon source code directly into standalone executables for Windows and Linux. It does not use LLVM or any external compiler infrastructure—everything from lexing to binary generation is implemented in C++.

**Key Features:**
- Fast compilation (~60ms for 1000 lines)
- No external dependencies (no LLVM, no C runtime)
- Native x86-64 code generation
- Windows (PE) and Linux (ELF) support
- SIMD-optimized lexer using AVX2/SSE4.2
- Safe FFI with process isolation

## Compilation Pipeline

```
┌─────────────────────────────────────────────────────────────────────┐
│                         FRONTEND                                     │
├─────────────────────────────────────────────────────────────────────┤
│  Source Code (.maxon)                                               │
│       ↓                                                             │
│  Lexer (SIMD-optimized) ──→ Token Stream                           │
│       ↓                                                             │
│  Parser (Recursive Descent) ──→ Abstract Syntax Tree (AST)         │
│       ↓                                                             │
│  Semantic Analyzer ──→ Type-checked AST + Diagnostics              │
├─────────────────────────────────────────────────────────────────────┤
│                         BACKEND (Winchester)                         │
├─────────────────────────────────────────────────────────────────────┤
│  MIR Code Generator ──→ SSA-based Intermediate Representation      │
│       ↓                                                             │
│  Optimizer (10 passes) ──→ Optimized MIR                           │
│       ↓                                                             │
│  Register Allocator (Linear Scan) ──→ Physical Register Assignment │
│       ↓                                                             │
│  x86-64 Code Generator ──→ Machine Code Bytes                      │
│       ↓                                                             │
│  Binary Writer (PE/ELF) ──→ Native Executable                      │
└─────────────────────────────────────────────────────────────────────┘
```

## Frontend

### Lexer

The lexer converts source text into a stream of tokens. It uses SIMD instructions (AVX2/SSE4.2) for high-performance character classification and keyword matching.

**Key optimizations:**
- Processes 32 characters at a time using AVX2 vector operations
- Perfect hash function for O(1) keyword recognition
- Cache-efficient Structure-of-Arrays (SoA) token storage
- String interning to deduplicate identifier strings

**Components:**
- `lexer.h/cpp` — Main lexer class and token definitions
- `lexer/lexer_platform.h` — Platform-specific SIMD intrinsics
- `lexer/lexer_char_class.h` — Vectorized character classification
- `lexer/lexer_keyword_matcher.h` — Perfect hash keyword matching
- `lexer/lexer_number_parser.h` — Number literal parsing
- `token_stream.h` — SoA token storage with string interning

### Parser

The parser transforms tokens into an Abstract Syntax Tree (AST) using recursive descent. It includes optimizations for fast lookahead and block boundary detection.

**Key features:**
- Recursive descent parsing (predictive, no backtracking)
- Block boundary precomputation for O(1) end-matching
- Lookahead cache for common token patterns
- Detailed error messages with line/column information

**Components:**
- `parser.h` — Parser class declaration
- `parser.cpp` — Core parsing logic
- `parser/parser_expr.cpp` — Expression parsing
- `parser/parser_stmt.cpp` — Statement parsing
- `parser/parser_decl.cpp` — Declaration parsing
- `parser_support.h` — Block boundary analyzer and lookahead cache
- `ast.h` — AST node definitions

### Semantic Analyzer

The semantic analyzer validates the AST and performs type checking. It catches errors like undefined variables, type mismatches, and incorrect function calls.

**Responsibilities:**
- Type inference and checking
- Variable scope resolution
- Function signature validation
- Constant expression evaluation
- Unused variable/parameter warnings
- Error and warning diagnostics
- Symbol resolution optimization (builds function index map for O(1) codegen lookups)
- Generic type instantiation and monomorphization (see below)

**Components:**
- `semantic_analyzer.h/cpp` — Main analyzer
- `semantic_analyzer/semantic_analyzer_expr.cpp` — Expression analysis
- `semantic_analyzer/semantic_analyzer_stmt.cpp` — Statement analysis

### Generic Type Instantiation (Monomorphization)

Maxon uses **monomorphization** for generic types like `Map from K to V`. Instead of runtime polymorphism, the compiler generates specialized versions of generic types for each concrete type combination used.

**How it works:**

1. **Declaration:** Generic types are declared with type parameters:
   ```maxon
   type Map from KeyType to ValueType
       _keys [16]KeyType
       _values [16]ValueType
       ...
   end 'map'
   ```

2. **Instantiation:** When used with concrete types, a specialized type is created:
   ```maxon
   var ages = Map from string to int  ' Creates map<string,int>
   var scores = Map from int to float ' Creates map<int,float>
   ```

3. **Specialization:** The compiler generates unique type types with concrete fields:
   - `map<string,int>` has `_keys [16]string` and `_values [16]int`
   - `map<int,float>` has `_keys [16]int` and `_values [16]float`

**Method Resolution:**

Generic methods use a **type binding system** during code generation:

1. When compiling `ages.get(name)`, the compiler knows `ages` is `map<string,int>`
2. The `currentTypeBindings` map holds `{KeyType → string, ValueType → int}`
3. Method calls like `key.hash()` resolve to `string.hash()` via the bindings
4. Primitive type methods (int.hash, int.equals, etc.) are inlined directly

**Primitive Type Methods:**

Built-in types have compiler-implemented methods for generic compatibility:
- `int.hash()`, `int.equals(other int)` — For using int as map keys
- `byte.hash()`, `byte.equals(other byte)` — For using byte as map keys
- `character.hash()`, `character.equals(other character)` — For using character as map keys
- `string.hash()`, `string.equals(other string)` — String methods (implemented in stdlib)

**Benefits:**
- Zero runtime overhead (no vtables or type erasure)
- Type-safe at compile time
- Optimized code for each type combination
- Dead code elimination removes unused specializations

## Backend (Winchester)

The backend, codenamed "Winchester," is a complete native code generator that compiles MIR to x86-64 machine code.

### MIR (Maxon Intermediate Representation)

MIR is an SSA-based intermediate representation similar to LLVM IR but simpler. Every value is assigned exactly once, making dataflow analysis straightforward.

**Type system:**
- `void`, `i1` (bool), `i8`, `i32`, `i64`, `f64`
- `ptr` (opaque pointer)
- `array<T, N>`, `type { ... }`

**Instruction categories:**
- Arithmetic: `add`, `sub`, `mul`, `sdiv`, `fadd`, `fsub`, `fmul`, `fdiv`
- Comparisons: `icmp_eq`, `icmp_lt`, `fcmp_eq`, etc.
- Memory: `alloca`, `load`, `store`, `getelementptr`
- Control flow: `br`, `condbr`, `ret`, `call`
- SSA: `phi`, `copy`

**Components:**
- `mir/mir.h` — MIR types and instructions
- `mir/mir_builder.h` — Builder API for constructing MIR
- `codegen_mir.h/cpp` — AST to MIR translation (uses function ID map for O(1) call resolution)

### Optimizer

The optimizer runs multiple passes over MIR until no more changes occur (fixed-point iteration).

**Optimization passes:**
1. **Constant Folding** — Evaluate `3 + 4` → `7` at compile time
2. **Constant Propagation** — Replace variable uses with known values
3. **Dead Code Elimination** — Remove unused instructions
4. **Unreachable Block Elimination** — Remove dead basic blocks
5. **Strength Reduction** — Replace `x * 2` with `x << 1`
6. **Algebraic Simplification** — Apply `x + 0` → `x`, `x * 1` → `x`
7. **Function Inlining** — Inline small functions (≤20 instructions)
8. **Copy Propagation** — Eliminate unnecessary copies
9. **Mem2Reg (SSA Promotion)** — Promote stack allocas to SSA registers with PHI nodes
10. **PHI Elimination** — Convert SSA form for register allocation

**Components:**
- `mir/optimizer.h/cpp` — Optimization framework and passes

### Register Allocator

The register allocator uses linear-scan allocation with liveness analysis. When registers run out, it spills values to stack slots.

**Available registers (Windows x64):**
- General purpose: RAX, RBX, RCX, RDX, RSI, RDI, R8-R9, R12-R15
- Floating point: XMM0-XMM15
- Reserved: RSP, RBP, R10, R11 (scratch)

**Components:**
- `backend/regalloc.h/cpp` — Linear-scan allocator

### x86-64 Code Generator

The code generator emits x86-64 machine code bytes directly, with full support for both Windows and Linux calling conventions.

**Supported instructions:**
- Data movement: MOV, LEA, PUSH, POP
- Arithmetic: ADD, SUB, IMUL, IDIV
- Logic: AND, OR, XOR, SHL, SHR
- Control flow: JMP, Jcc, CALL, RET
- Floating point (SSE2): MOVSD, ADDSD, MULSD, SQRTSD, etc.

**Components:**
- `backend/x86_codegen.h/cpp` — Machine code generation
- `backend/x86_encoding.h/cpp` — Instruction encoding

### Binary Writers

The compiler can output either Windows PE or Linux ELF executables.

**PE Writer (Windows):**
- Generates PE32+ (64-bit) executables
- Supports imports (kernel32.dll, etc.)
- ASLR and DEP security features

**ELF Writer (Linux):**
- Generates ELF64 executables
- Standard section layout (.text, .data, .rodata)
- Non-executable stack

**Components:**
- `backend/pe_writer.h/cpp` — Windows PE generation
- `backend/elf_writer.h/cpp` — Linux ELF generation

## Runtime Library

The compiler includes a minimal runtime written in MIR (not C). This eliminates C runtime dependencies.

**Runtime functions:**
- `_start` — Program entry point
- `exit` — Process termination
- `malloc`, `free` — Heap allocation
- Math functions: `sqrt`, `sin`, `cos`, `tan`, `log`, `exp`, `pow`, etc.

**Components:**
- `maxon-runtime/runtime.mir` — Platform-independent runtime
- `maxon-runtime/runtime_windows.mir` — Windows system calls
- `maxon-runtime/runtime_linux.mir` — Linux system calls

## Standard Library

Maxon includes a standard library written in Maxon itself:

- `stdlib/fmt/` — String formatting (`format_int`, `format_float`, etc.)
- `stdlib/collections/` — Collection types (`Map from K to V`)
- `stdlib/io/` — File I/O (planned)
- `stdlib/math/` — Additional math functions

### Collections Library

The collections library provides generic data structures:

**Map (Hash Map):**
- Generic hash map: `Map from KeyType to ValueType`
- Open addressing with linear probing
- Automatic resizing at 75% load factor
- Supports any key type with `hash()` and `equals()` methods
- Location: `stdlib/collections/map.maxon`

## Safe FFI

For calling external C libraries, Maxon provides a Safe FFI that runs external code in a separate worker process. This isolates crashes and security vulnerabilities.

**How it works:**
1. Main process writes arguments to shared memory
2. Worker process executes the C function
3. Result is passed back through shared memory
4. If worker crashes, main process detects it and exits gracefully

**Components:**
- `codegen_mir/codegen_mir_safeffi.cpp` — Safe FFI code generation

## LSP Server

The compiler includes an LSP (Language Server Protocol) server for IDE integration.

**Features:**
- Syntax highlighting (semantic tokens)
- Go to definition
- Find references
- Hover information
- Code completion
- Diagnostics (errors/warnings)
- Code formatting
- Rename refactoring

**Components:**
- `lsp-server/` — Complete LSP implementation

## VS Code Extension

A VS Code extension provides IDE support using the LSP server.

**Features:**
- Syntax highlighting (TextMate grammar + semantic tokens)
- All LSP features (completions, hover, go-to-definition, etc.)
- Block identifier matching (visual highlighting)
- Code formatting with customizable indentation
- Compiler Explorer panel (MIR and x86-64 assembly views with optimization toggle)
- Integrated debugging (planned)

**Components:**
- `vscode-extension/` — Extension source code

## Directory Structure

```
maxon/
├── maxon-bin/           # Compiler source code
│   ├── lexer/           # SIMD lexer support files
│   ├── parser/          # Parser implementation files
│   ├── mir/             # MIR infrastructure
│   ├── backend/         # x86-64 code generation
│   ├── codegen_mir/     # AST → MIR translation
│   ├── semantic_analyzer/ # Type checking
│   └── tests/           # Unit tests
├── maxon-runtime/       # Runtime library (MIR)
├── lsp-server/          # Language server
├── vscode-extension/    # VS Code extension
├── stdlib/              # Standard library (Maxon)
├── specs/               # Language specifications
├── language-tests/      # Integration tests
└── docs/                # Documentation
```

## Build System

The project uses CMake with Ninja for fast builds.

```bash
# Build everything
make all

# Build just the compiler
make compiler

# Build just the LSP server
make lsp

# Run all tests
make test
```

## Performance

**Compilation speed** (1000-line program):
- Total: ~60ms
- 7× faster than LLVM backend

**Generated code quality:**
- Within 5% of LLVM -O2 on benchmarks
- Competitive with GCC and MSVC

## Future Plans

- ARM64 backend
- WebAssembly backend
- macOS support (Mach-O)
- Incremental compilation
- JIT compilation
- More optimization passes (loop optimizations, CSE)

## References

**Source locations:**
- Frontend: `maxon-bin/lexer.h`, `parser.h`, `semantic_analyzer.h`
- Backend: `maxon-bin/mir/`, `maxon-bin/backend/`
- Runtime: `maxon-runtime/`
- LSP: `lsp-server/`

**Language reference:** See `docs/LANGUAGE_REFERENCE.md`

**Spec format:** See `docs/SPECS.md`
