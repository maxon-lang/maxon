# Maxon-Sharp Implementation Plan

This document tracks progress implementing Maxon language features in maxon-sharp. Each phase builds on the previous, with spec tests run after each phase to validate correctness.

## Current Status

- **Current Phase**: Phase 2 Complete, Ready for Phase 3
- **Last Updated**: 2026-01-24
- **Tests Passing**: 137/137

## Phase Overview

| Phase | Focus | Status | Specs Passing |
|-------|-------|--------|---------------|
| 1 | Primitives & Variables | ✅ Complete | 57/57 |
| 2 | Operators & Functions | ✅ Complete | 137/137 |
| 3 | Control Flow, Types & Inference | ⬜ Not Started | 0/~12 |
| 4 | Enums & Interfaces | ⬜ Not Started | 0/4 |
| 5 | Strings & Collections | ⬜ Not Started | 0/8 |
| 6 | Error Handling & Closures | ⬜ Not Started | 0/4 |
| 7 | Modules & Stdlib | ⬜ Not Started | 0/~10 |
| 8 | Advanced Features | ⬜ Not Started | 0/6 |

---

## Phase 1: Primitives & Variables

**Goal**: Establish the type foundation with basic literals and variable declarations.

**Dependencies**: None (foundational)

**Status**: ✅ Complete

### Specs Implemented

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| int-type.md | 64-bit signed integer type | ✅ | Includes recursive factorial example |
| bool-type.md | Boolean type (true/false) | ✅ | |
| literals.md | Integer literals | ✅ | |
| variables.md | `let`/`var` declarations | ✅ | |
| arithmetic.md | Basic arithmetic operators | ✅ | +, -, *, /, mod |
| optimizations.md | Compiler optimizations | ✅ | Constant folding, DCE, strength reduction |
| float-type.md | 64-bit floating-point type | ✅ | SSE codegen, int↔float promotion |

### Implementation Completed

- [x] Semantic analysis for literal expressions
- [x] Type checking for primitive types (int, bool, float)
- [x] Variable declaration semantic analysis
- [x] MLIR generation for literals
- [x] MLIR generation for variable load/store
- [x] Liveness-aware register allocation
- [x] Callee-saved register preservation across calls
- [x] Function prologue/epilogue generation
- [x] Recursive function support
- [x] Float SSE codegen (movsd, addsd, subsd, mulsd, divsd)
- [x] Int-to-float promotion (arith.sitofp → x86.cvtsi2sd)
- [x] Float-to-int truncation (arith.fptosi → x86.cvttsd2si)
- [x] Float function parameters and return values (XMM0-XMM3 ABI)
- [x] Function return type lookup for proper call codegen

### Notes
- Implemented liveness analysis to detect values live across function calls
- Values live across calls are allocated to callee-saved registers (RBX, R12-R15)
- Push/pop of callee-saved registers inserted in prologue/epilogue
- byte-type.md and character-type.md moved to Phase 5 (require arrays/strings infrastructure)

---

## Phase 2: Operators & Functions

**Goal**: Implement all operators and function infrastructure.

**Dependencies**: Phase 1 (primitives)

**Status**: ✅ Complete

### Specs Implemented

#### Operators

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| comparison-operators.md | ==, !=, <, <=, >, >= | ✅ | All comparisons for int and float |
| bitwise-operators.md | band, bor, bxor, shl, shr | ✅ | Added X86 emission for and/or/xor/shl/shr/sar |
| unary-operators.md | not, - | ✅ | Integer negation via sub 0, x |
| unary-negation.md | Unary minus operator | ✅ | Float negation not yet implemented |

#### Functions

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| function-declaration.md | Function syntax, parameters, returns | ✅ | Renamed from functions.md |
| return-statement.md | Return statements | ✅ | Renamed from return.md |
| parameter-labels.md | Named arguments (a: value) | ✅ | Renamed from named-arguments.md |

#### Math Functions (SSE-based)

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| floor.md | floor() | ✅ | Uses roundsd mode 0x09 |
| ceil.md | ceil() | ✅ | Uses roundsd mode 0x0A |
| round.md | round() | ✅ | Uses roundsd mode 0x08 (banker's rounding) |
| sqrt.md | sqrt() | ✅ | Uses sqrtsd instruction |
| abs.md | abs() | ✅ | Uses andpd with sign bit mask |
| min.md | min() | ✅ | Uses minsd instruction |
| max.md | max() | ✅ | Uses maxsd instruction |
| trunc.md | trunc() | ✅ | Float-to-int conversion |

#### Math Functions (Require CRT - Deferred)

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| pow.md | pow() | ⏸️ Deferred | Requires PE import table for CRT calls |
| log.md | log() | ⏸️ Deferred | Requires PE import table |
| log2.md | log2() | ⏸️ Deferred | Requires PE import table |
| log10.md | log10() | ⏸️ Deferred | Requires PE import table |
| exp.md | exp() | ⏸️ Deferred | Requires PE import table |
| sin.md | sin() | ⏸️ Deferred | Requires PE import table |
| cos.md | cos() | ⏸️ Deferred | Requires PE import table |
| tan.md | tan() | ⏸️ Deferred | Requires PE import table |
| atan2.md | atan2() | ⏸️ Deferred | Requires PE import table |

#### Deferred to Later Phases

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| many-args.md | Functions with many parameters | ⏸️ Deferred | Requires register spilling |
| unused-parameters.md | Discard params with `_` | ⏸️ Deferred | Requires unused variable detection |
| print-function.md | Built-in print function | ⏸️ Deferred | Requires string support |

### Implementation Completed

- [x] Binary operator semantic analysis
- [x] Unary operator semantic analysis (integer negation)
- [x] Type checking for operator operands
- [x] MLIR generation for all operators
- [x] Short-circuit logical operators (and/or) with control flow
- [x] Function declaration semantic analysis
- [x] Parameter binding and type checking
- [x] Return type validation
- [x] Function call semantic analysis
- [x] Named argument resolution (any order, defaults)
- [x] MLIR generation for function calls
- [x] Windows x64 calling convention
- [x] SSE math intrinsics (sqrt, floor, ceil, round, abs, min, max)
- [x] X86 bitwise operations (and, or, xor, shl, shr, sar)
- [x] Integer promotion for min/max functions

### Notes

- Implemented Math dialect with unary (Sqrt, Floor, Ceil, Round, AbsF) and binary (Min, Max) ops
- Added SSE instruction emission: sqrtsd, roundsd, minsd, maxsd, andpd
- Logical operators use control flow blocks for short-circuit evaluation
- Bitwise ops lower from arith dialect (andi, ori, xori, shli, shrsi, shrui) to X86
- CRT-based math functions (sin, cos, tan, log, exp, pow) require PE import table support which is not yet implemented
- Float negation (NegFOp) has no X86 pattern yet - requires xorpd with sign bit mask

---

## Phase 3: Control Flow, Types & Type Inference

**Goal**: Add control flow, struct types, and type inference early to avoid retrofitting.

**Dependencies**: Phase 2 (functions, operators)

### Specs to Implement

#### Control Flow

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| if-else.md | if/else/else-if | ⬜ | |
| while-loops.md | while loops | ⬜ | |
| loop-control.md | break/continue, labeled | ⬜ | |

#### Types

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| structs.md | Type declarations with fields | ⬜ | |
| methods.md | Instance method calls | ⬜ | |
| method-declarations.md | Methods on types | ⬜ | |
| static-methods.md | Static methods | ⬜ | |
| static-fields.md | Static fields, top-level var | ⬜ | |
| self.md | self reference in methods | ⬜ | |
| type-casting.md | `as` operator for casts | ⬜ | |

#### Type Inference

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| type-inference.md | Type inference and checking | ⬜ | |
| contextual-typing.md | Context-based literal typing | ⬜ | |
| numeric-promotion.md | Auto int→float promotion | ⬜ | |

### Implementation Tasks

- [ ] If/else semantic analysis and MLIR
- [ ] While loop semantic analysis and MLIR
- [ ] Break/continue with label tracking
- [ ] Struct type registration
- [ ] Field access semantic analysis
- [ ] Struct layout computation
- [ ] Method resolution
- [ ] `self` binding in methods
- [ ] Static vs instance dispatch
- [ ] Type casting validation
- [ ] Bidirectional type inference
- [ ] Numeric promotion rules

### Notes
<!-- Add implementation notes here as you work -->

---

## Phase 4: Enums & Interfaces

**Goal**: Add enum types and interface system (needed for error handling and match).

**Dependencies**: Phase 3 (structs, type system)

### Specs to Implement

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| enums.md | Enum types (simple, raw, associated) | ⬜ | |
| interfaces.md | Interface declarations | ⬜ | |
| interface-conformance.md | `is` keyword, conformance | ⬜ | |
| match.md | Pattern matching | ⬜ | |

### Implementation Tasks

- [ ] Simple enum semantic analysis
- [ ] Raw value enums
- [ ] Associated value enums
- [ ] Enum case construction
- [ ] Enum discriminant layout
- [ ] Interface declaration semantic analysis
- [ ] Conformance checking
- [ ] Witness table generation
- [ ] Match expression semantic analysis
- [ ] Pattern exhaustiveness checking
- [ ] Match code generation

### Notes
<!-- Add implementation notes here as you work -->

---

## Phase 5: Strings & Collections

**Goal**: Implement managed string type and collection types.

**Dependencies**: Phase 4 (interfaces for Collection)

### Specs to Implement

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| string-literals.md | String literal syntax | ⬜ | |
| string.md | String type (SSO, UTF-8) | ⬜ | |
| string-interpolation.md | `\()` in strings | ⬜ | |
| arrays.md | Array type with get/set/push | ⬜ | |
| managed-arrays.md | Arrays of managed types | ⬜ | |
| collection.md | Collection interface | ⬜ | |
| map.md | Hash map type | ⬜ | |
| set.md | Hash set type | ⬜ | |

### Implementation Tasks

- [ ] String literal parsing and escape sequences
- [ ] String type layout (SSO implementation)
- [ ] String memory management
- [ ] String interpolation lowering
- [ ] Array type semantic analysis
- [ ] Array bounds checking
- [ ] Array growth/reallocation
- [ ] Collection interface implementation
- [ ] Hash function implementation
- [ ] Map bucket layout
- [ ] Set implementation

### Notes
<!-- Add implementation notes here as you work -->

---

## Phase 6: Error Handling & Closures

**Goal**: Add error handling with try/throw/otherwise and closure support.

**Dependencies**: Phase 4 (enums for error types), Phase 5 (strings for error messages)

### Specs to Implement

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| error-handling.md | throw/try/otherwise | ⬜ | |
| if-try.md | if try/let expressions | ⬜ | |
| parsable.md | Parsable interface pattern | ⬜ | |
| closures.md | Function refs, closures | ⬜ | |

### Implementation Tasks

- [ ] Error type semantic analysis
- [ ] throw statement codegen
- [ ] try/otherwise control flow
- [ ] Error propagation
- [ ] if try binding
- [ ] Parsable conformance
- [ ] Closure environment capture analysis
- [ ] Closure struct generation
- [ ] Closure call convention

### Notes
<!-- Add implementation notes here as you work -->

---

## Phase 7: Modules & Stdlib

**Goal**: Multi-file compilation and standard library integration.

**Dependencies**: Phase 6 (error handling for stdlib functions)

### Specs to Implement

#### Module System

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| namespaces.md | File-based namespaces | ⬜ | |
| export.md | export visibility | ⬜ | |
| multi-file.md | Multi-file compilation | ⬜ | |
| qualified-names.md | Namespace.name syntax | ⬜ | |

#### Stdlib

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| stdlib-autodiscovery.md | Auto-linking stdlib | ⬜ | |
| stdlib-core.md | Basic stdlib functions | ⬜ | |
| stdlib-array.md | Array stdlib module | ⬜ | |
| stdlib-set.md | Set stdlib module | ⬜ | |
| stdlib-file.md | File read/write | ⬜ | |
| stdlib-directory.md | Directory operations | ⬜ | |
| commandline-args.md | CommandLine.args() | ⬜ | |

### Implementation Tasks

- [ ] Namespace registration
- [ ] Export visibility checking
- [ ] Multi-file symbol resolution
- [ ] Qualified name lookup
- [ ] Stdlib path discovery
- [ ] Stdlib linking
- [ ] File I/O syscalls
- [ ] Directory syscalls
- [ ] Command line argument passing

### Notes
<!-- Add implementation notes here as you work -->

---

## Phase 8: Advanced Features

**Goal**: Complete remaining advanced features.

**Dependencies**: All previous phases

### Specs to Implement

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| interface-extensions.md | Extension methods on interfaces | ⬜ | |
| generic-interfaces.md | `uses`/`with` type params | ⬜ | |
| grapheme-clusters.md | Unicode EGC support | ⬜ | |
| slices.md | Memory slices | ⬜ | |
| ffi.md | FFI with crash isolation | ⬜ | |
| allocations.md | Memory allocation tracking | ⬜ | |

### Implementation Tasks

- [ ] Interface extension method dispatch
- [ ] Generic type parameter binding
- [ ] Grapheme cluster iteration
- [ ] Slice type layout
- [ ] FFI declaration parsing
- [ ] FFI call marshaling
- [ ] Crash isolation (structured exception handling)
- [ ] Allocation tracking infrastructure

### Notes
<!-- Add implementation notes here as you work -->

---

## Implementation Log

Record significant changes, decisions, and blockers here.

### 2026-01-24 (Phase 2)
- **Phase 2 Complete**: 137 spec tests passing
- Implemented X86 bitwise operations:
  - Added emission code for AndOp, OrOp, XorOp, NotOp, SarOp, ShrOp
  - Added lowering patterns: LowerAndIOp, LowerOrIOp, LowerXOrIOp, LowerShRSIOp, LowerShRUIOp
- Implemented short-circuit logical operators (and/or):
  - Added LowerLogicalExpr in AstToMaxonDialect.cs
  - Uses control flow blocks for lazy evaluation
  - Fixed Mem2RegPass to handle cross-block allocas
- Implemented Math dialect with SSE intrinsics:
  - Created MathOps.cs with SqrtOp, FloorOp, CeilOp, RoundOp, AbsFOp, MinFOp, MaxFOp
  - Added X86 SSE ops: SqrtsdOp, RoundsdOp, MinsdOp, MaxsdOp, AndpdOp
  - Added emission code for all new SSE instructions
- Implemented function enhancements:
  - Named arguments with any-order support
  - Default parameter values
  - Validation for unknown parameter names
- Added register allocation support for new ops
- Fixed int-to-float promotion for min/max functions
- Moved operator specs from archive: comparison-operators.md, bitwise-operators.md, unary-operators.md, unary-negation.md
- Created new math specs: floor.md, ceil.md, round.md, abs.md, trunc.md, min.md, max.md, sqrt.md
- Deferred CRT-based math functions (sin, cos, tan, log, exp, pow) - require PE import table

### 2026-01-24 (Phase 1)
- Created implementation plan
- **Phase 1 Complete**: All 57 spec tests passing
- Implemented liveness-aware register allocation:
  - `AnalyzeLiveAcrossCalls()` detects vregs live across function calls
  - Values live across calls allocated to callee-saved registers (RBX, R12-R15)
  - `InsertCalleeSavedSaveRestore()` adds push/pop in prologue/epilogue
- Fixed `UpdateValueReferences` in DialectConversionPass
- Added parameter copying from ABI registers in FunctionFramePass
- Added float support: `MovqOp` for GPR→XMM transfer, `VRegOperand.IsFloat` for register class tracking
- Added `trunc` builtin and `CvttsdOp` for float-to-int conversion
- Fixed spec parser to extract exit codes from docs examples

---

## Architecture Notes

### Key Files to Modify

- `Compiler/SemanticAnalyzer.cs` - Type checking and validation
- `Compiler/MLIR/Conversion/AstToMaxonDialect.cs` - AST to MLIR conversion
- `Compiler/MLIR/Conversion/MaxonToStandard.cs` - Lowering passes
- `Compiler/MLIR/Conversion/StandardToX86Patterns.cs` - X86 lowering patterns
- `Compiler/MLIR/Emit/X86CodeEmitter.cs` - X86 code emission

### Testing Strategy

Run spec tests after each phase:
```powershell
cd maxon-sharp
.\bin\Debug\net8.0\win-x64\maxonsharp.exe spec-test
```

Or run filtered spec tests:
```powershell
.\bin\Debug\net8.0\win-x64\maxonsharp.exe spec-test --filter=floor
```

### Debugging Tips

- Use `--log=mlir:debug` for MLIR dumps
- Use `--log=codegen:debug` for X86 codegen dumps
- Use `--log=regalloc:debug` for register allocation details
