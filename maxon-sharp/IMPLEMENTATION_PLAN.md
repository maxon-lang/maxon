# Maxon-Sharp Implementation Plan

This document tracks progress implementing Maxon language features in maxon-sharp. Each phase builds on the previous, with spec tests run after each phase to validate correctness.

## Current Status

- **Current Phase**: Phase 1 (In Progress)
- **Last Updated**: 2026-01-24
- **Tests Passing**: 52/52

## Phase Overview

| Phase | Focus | Status | Specs Passing |
|-------|-------|--------|---------------|
| 1 | Primitives & Variables | 🔄 In Progress | 52/52 (partial) |
| 2 | Operators & Functions | ⬜ Not Started | 0/~25 |
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

**Status**: 🔄 In Progress (int/bool done, float/byte/char pending)

### Specs Implemented

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| int-type.md | 64-bit signed integer type | ✅ | Includes recursive factorial example |
| bool-type.md | Boolean type (true/false) | ✅ | |
| literals.md | Integer literals | ✅ | |
| variables.md | `let`/`var` declarations | ✅ | |
| arithmetic.md | Basic arithmetic operators | ✅ | +, -, *, /, mod |
| optimizations.md | Compiler optimizations | ✅ | Constant folding, DCE, strength reduction |

### Specs Still Pending

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| float-type.md | 64-bit floating-point type | ⬜ | In archive |
| byte-type.md | 8-bit unsigned byte type | ⬜ | In archive |
| character-type.md | Unicode character type | ⬜ | In archive |

### Implementation Completed

- [x] Semantic analysis for literal expressions
- [x] Type checking for primitive types (int, bool)
- [x] Variable declaration semantic analysis
- [x] MLIR generation for literals
- [x] MLIR generation for variable load/store
- [x] Liveness-aware register allocation
- [x] Callee-saved register preservation across calls
- [x] Function prologue/epilogue generation
- [x] Recursive function support

### Implementation Remaining

- [ ] Float type semantic analysis and codegen
- [ ] Byte type semantic analysis and codegen
- [ ] Character type semantic analysis and codegen

### Notes
- Implemented liveness analysis to detect values live across function calls
- Values live across calls are allocated to callee-saved registers (RBX, R12-R15)
- Push/pop of callee-saved registers inserted in prologue/epilogue
- Float SSE infrastructure exists (MovqOp, addsd, etc.) but float type spec not yet moved from archive

---

## Phase 2: Operators & Functions

**Goal**: Implement all operators and function infrastructure.

**Dependencies**: Phase 1 (primitives)

### Specs to Implement

#### Operators

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| arithmetic-operators.md | +, -, *, /, mod | ⬜ | |
| comparison-operators.md | ==, !=, <, <=, >, >= | ⬜ | |
| bitwise-operators.md | band, bor, bxor, shl, shr | ⬜ | |
| unary-operators.md | not, - | ⬜ | |
| negation.md | Negation operator | ⬜ | |

#### Functions

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| functions.md | Function syntax, parameters, returns | ⬜ | |
| return.md | Return statements | ⬜ | |
| named-arguments.md | Named arguments (a: value) | ⬜ | |
| many-parameters.md | Functions with many parameters | ⬜ | |
| discarded-parameters.md | Discard params with `_` | ⬜ | |
| print.md | Built-in print function | ⬜ | |

#### Math Functions (batch)

| Spec | Description | Status | Notes |
|------|-------------|--------|-------|
| floor.md | floor() | ⬜ | |
| ceil.md | ceil() | ⬜ | |
| round.md | round() | ⬜ | |
| sqrt.md | sqrt() | ⬜ | |
| pow.md | pow() | ⬜ | |
| abs.md | abs() | ⬜ | |
| min.md | min() | ⬜ | |
| max.md | max() | ⬜ | |
| log.md | log() | ⬜ | |
| exp.md | exp() | ⬜ | |
| sin.md | sin() | ⬜ | |
| cos.md | cos() | ⬜ | |
| tan.md | tan() | ⬜ | |
| atan2.md | atan2() | ⬜ | |

### Implementation Tasks

- [ ] Binary operator semantic analysis
- [ ] Unary operator semantic analysis
- [ ] Type checking for operator operands
- [ ] MLIR generation for all operators
- [ ] Function declaration semantic analysis
- [ ] Parameter binding and type checking
- [ ] Return type validation
- [ ] Function call semantic analysis
- [ ] Named argument resolution
- [ ] MLIR generation for function calls
- [ ] Calling convention implementation
- [ ] Math intrinsics (x87 or SSE)

### Notes
<!-- Add implementation notes here as you work -->

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

### 2026-01-24
- Created implementation plan
- **Phase 1 Complete**: All 52 spec tests passing
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
- `Compiler/MLIR/MaxonDialect.cs` - MLIR operation generation
- `Compiler/MLIR/MaxonToStandard.cs` - Lowering passes
- `Compiler/CodeEmitter.cs` - X86 code generation

### Testing Strategy

Run spec tests after each phase:
```powershell
cd maxon-sharp
dotnet test --filter "Category=PhaseN"  # When test categories are set up
```

Or run individual spec tests:
```powershell
dotnet test --filter "FullyQualifiedName~SpecName"
```

### Debugging Tips

- Use `--verbosity detailed` for test output
- MLIR dumps: Set `MAXON_DUMP_MLIR=1`
- AST dumps: Set `MAXON_DUMP_AST=1`
