# Maxon Programming Language - Development History

This document outlines the development history of the Maxon programming language project, compiled from the git commit history.

## Project Overview

**Author**: Eric Stern  
**Total Commits**: 224  
**Development Period**: November 10, 2025 - November 27, 2025 (18 days)  
**License**: Dual-licensed under Apache 2.0 and MIT

Maxon is a statically-typed programming language with a custom native x86-64 backend, designed for clear syntax and robust IDE support.

---

## Development Timeline

### Day 1: November 10, 2025 - Project Foundation

The project began with a rapid initial setup, establishing core infrastructure:

- **Initial commit** - Basic project structure
- **Build system** - CMake and Ninja integration for fast builds
- **LLVM backend** - Initial linking with LLVM for code generation
- **LSP Server** - Language Server Protocol implementation started
- **VS Code Extension** - Initial extension scaffolding
- **Test framework** - Test fragments system established
- **Core implementation** - Basic language features functional
- **Debugging support** - Debug information generation
- **Single-line if statements** - Control flow enhancement

Key commits:
- `64a8b1b` - initial commit
- `5589673` - added lsp
- `efcd1ff` - finished implementing
- `549bbb3` - added debugging

### Day 2: November 11, 2025 - Standard Library & Tooling

Focused on building out the standard library and improving developer experience:

- **Standard library (stdlib)** - Multiple commits establishing core library functions
- **Print function** - Console output capabilities
- **Entry point system** - Program structure finalization
- **Import system** - Cross-file module imports from stdlib
- **Debugger improvements** - Enhanced debugging capabilities
- **Error messages** - Better compiler diagnostics
- **Licensing** - Apache 2.0 and MIT dual licensing established

Key commits:
- `56e00ab` - added print
- `fcf4611` - import from stdlib
- `b7b25b4` - set license

### Day 3: November 12, 2025 - VS Code Integration & Floats

Major focus on IDE integration and floating-point support:

- **VS Code Extension** - Full extension functionality
- **Completions** - Code completion support
- **LSP Diagnostics** - Real-time error reporting
- **Code Actions** - Quick fixes and refactoring
- **Block identifiers** - Syntax highlighting for block labels
- **Floating-point types** - Complete float implementation
- **Qualified names** - Namespace support
- **Spectral-norm example** - First benchmark example

Key commits:
- `ded7fcf` - vscode extension works
- `946f580` - vscode extension completions
- `2c758fe` - float implementation
- `cacc447` - spectral-norm example

### Days 4-5: November 17-18, 2025 - Testing & Runtime

Building out testing infrastructure and runtime capabilities:

- **Runtime library** - memset and __chkstk implementations
- **printFloat function** - Floating-point output
- **Parallel test execution** - Faster test runs
- **Dual IR generation** - Test fragment improvements
- **Modern LLVM optimization** - Updated optimization passes
- **VS Code extension roadmap** - Planning future features

Key commits:
- `19199ba` - added runtime library for memset and __chkstk
- `89d5668` - made language tests parallel
- `49bebcc` - change to modern llvm optimization

### Day 6: November 19, 2025 - Structs & LSP Improvements

Introduction of composite types and LSP enhancements:

- **Structs** - User-defined composite types
- **N-body example** - Physics simulation benchmark
- **Hover support** - Type information on hover
- **Linked editing** - Synchronized renaming
- **printFloat precision** - Formatted float output
- **Dead code elimination** - Optimization pass
- **Namespace separator** - Syntax refinement (:: separator)

Key commits:
- `82b4680` - added structs and nbody example
- `8c9a609` - added hover to vscode extension
- `5e2c12c` - added linked editing to the extension
- `40ab995` - added dead code elimination

### Days 7-8: November 20-21, 2025 - Spec-Driven Development

Major architectural shift to specification-driven development:

- **Spec-driven workflow** - Specs as single source of truth
- **HTML documentation** - Generated from spec files
- **Test fragment extraction** - Automated from specs
- **Boolean type** - Added bool primitive
- **Trigonometric functions** - sin, cos, tan
- **For loops** - Iterator-based loops
- **Language reference** - Comprehensive documentation
- **Code refactoring** - Major cleanup of parser, analyzer, codegen

Key commits:
- `3f908aa` - spec driven development
- `202d599` - added bool type
- `e3afd0d` - added for loops and iterators
- `929fe90` - added language reference
- `dea8c3d` - refactored parser.cpp

### Day 9: November 22, 2025 - Block Labels & Specs

Control flow improvements and spec system refinement:

- **Break with labels** - `break 'label'` for named loop exit
- **Block identifier highlighting** - VS Code syntax support
- **No duplicate block identifiers** - Semantic validation
- **Timeout handling** - Test runner robustness
- **Spec format updates** - Improved specification structure
- **Float to int casting** - Explicit disallowance

Key commits:
- `c549393` - added break 'label'
- `7905dd9` - no duplicate block identifiers
- `ff4bb49` - added timeout to regen and test runner

### Day 10: November 23, 2025 - Cross-Platform Support

Expanding platform support beyond Windows:

- **Dev container** - Docker-based development environment
- **Linux build** - Platform portability
- **LLVM source build** - Building LLVM from source on Linux
- **Embedded LLVM** - LLVM integrated into build
- **Docgen conversion** - C++ documentation generator

Key commits:
- `ce44b7a` - added dev container
- `49d1a17` - build llvm from source on linux
- `343e846` - embedded llvm works

### Day 11: November 24, 2025 - Build Stabilization

Fixing cross-platform issues and stabilizing builds:

- **Windows build fixes** - Path slash handling
- **Linux self-test** - Platform verification
- **Docgen fixes** - Documentation generator bugs
- **Grammar updates** - TextMate grammar refinements

Key commits:
- `f76f968` - fixed windows build
- `3d0ceeb` - fixed self test on linux
- `4721a45` - works on linux now

### Day 12: November 25, 2025 - Winchester Backend

Major architectural change - introduction of custom backend:

- **Winchester IR** - Custom intermediate representation
- **Phased implementation** - 9 phases of backend development
- **Backend tests** - Comprehensive test suite (80+ tests)
- **LLVM removal** - Removing LLVM dependency
- **Pointer removal** - Simplifying memory model
- **Runtime cleanup** - Streamlined runtime library

Key commits:
- `c4d1415` - initial implementation of winchester
- `c32235e` - phase 9, some tests are passing
- `b34778a` - all backend tests pass
- `879cf61` - removed pointers

### Day 13: November 26, 2025 - Safe FFI & Memory

Advanced features for safety and memory management:

- **Safe FFI** - Foreign function interface with crash isolation
- **Job objects** - Windows process management for FFI safety
- **MIR Verifier** - Intermediate representation validation
- **Phi elimination** - SSA simplification
- **MemorySSA** - Memory analysis pass
- **Dynamic arrays** - Runtime-sized arrays
- **Instruction counts** - Performance metrics

Key commits:
- `f67d934` - safe ffi implementation works
- `a7eb2f9` - added MIR verifier
- `1bbb1a0` - implemented MemorySSA
- `31b8b81` - dynamic arrays

### Day 14: November 27, 2025 - Strings & SIMD

Latest features and performance optimizations:

- **String implementation** - Complete string type support
- **SIMD lexer** - Vectorized lexical analysis
- **SIMD parser** - Vectorized parsing
- **Symbol resolution optimization** - Faster name lookup

Key commits:
- `cd1250f` - string implementation complete
- `3589097` - implemented simd lexer
- `69d79d2` - implemented simd parser
- `9dc6cff` - implemented Symbol Resolution Optimization

---

## Feature Evolution

### Type System

| Date | Feature | Commit |
|------|---------|--------|
| Nov 10 | int type | Initial |
| Nov 12 | float type | `2c758fe` |
| Nov 18 | Byte literals | Various |
| Nov 19 | Structs | `82b4680` |
| Nov 20 | bool type | `202d599` |
| Nov 26 | Dynamic arrays | `31b8b81` |
| Nov 27 | String type | `cd1250f` |

### Control Flow

| Date | Feature | Commit |
|------|---------|--------|
| Nov 10 | if/else, while | Initial |
| Nov 10 | Single-line if | `432dd7b` |
| Nov 21 | for loops | `e3afd0d` |
| Nov 22 | break 'label' | `c549393` |

### Standard Library

| Date | Feature | Commit |
|------|---------|--------|
| Nov 11 | print() | `56e00ab` |
| Nov 18 | printFloat() | `ceeb7dc` |
| Nov 19 | Math functions | `82b4680` |
| Nov 20 | Trigonometry (sin, cos, tan) | Various |
| Nov 21 | log2() | `e0659c5` |

### Backend Evolution

| Date | Backend | Notes |
|------|---------|-------|
| Nov 10-24 | LLVM | Initial backend |
| Nov 24 | Cranelift | Experimental branch |
| Nov 25 | Winchester | Custom native x86-64 |

---

## Architecture Milestones

### 1. Initial Architecture (Nov 10)
- Lexer → Parser → AST → LLVM IR → Native code
- LSP server for IDE support
- VS Code extension

### 2. Spec-Driven Development (Nov 20)
- Specification files as single source of truth
- Automated test extraction
- Generated documentation

### 3. Winchester Backend (Nov 25)
- Custom MIR (Mid-level IR)
- Direct x86-64 code generation
- No external compiler dependencies
- Safe FFI with crash isolation

### 4. Performance Optimizations (Nov 27)
- SIMD-accelerated lexer
- SIMD-accelerated parser
- Optimized symbol resolution

---

## Test Suite Growth

The project maintains an extensive test suite:

- **Backend tests**: 80+ tests in `backend-tests/`
- **Language tests**: Fragment-based tests in `language-tests/`
- **Debugger tests**: In `debugger-tests/`
- **LSP tests**: VS Code extension tests

---

## Branches

| Branch | Purpose | Status |
|--------|---------|--------|
| `main` | Primary development | Active |
| `cranelift` | Cranelift backend experiment | Archived |

---

## Statistics

- **Total commits**: 224
- **Files changed**: 713+
- **Lines added**: 218,838+
- **Spec files**: 56 feature specifications
- **Example programs**: 9 (including benchmark programs)
- **Development velocity**: ~12.4 commits/day average

---

## Notable Examples

1. **hello-world.maxon** - Basic introduction
2. **spectral-norm.maxon** - Numeric benchmark
3. **nbody.maxon** - Physics simulation
4. **fannkuch-redux.maxon** - Algorithm benchmark

---

*Document generated from git history on November 27, 2025*
