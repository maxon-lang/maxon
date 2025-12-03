# Maxon Compiler Self-Hosting Plan

## Executive Summary

The Maxon compiler is a well-architected, production-quality compiler with ~31,500 lines of C++ code. It features a complete compilation pipeline from source to native x86-64, a sophisticated MIR-based optimizer (10 optimization passes including Memory SSA), and generates PE/ELF binaries directly. With 88 backend tests passing and 55+ specification files, it's impressively complete for a young language.

**Critical gaps preventing self-hosting:**
1. **No dynamic data structures** (vectors, hash maps, dynamic strings)
2. **No sum types/enums** (needed for AST representation)
3. **No file I/O** (can't read source files)
4. **Limited type system** (no generics, interfaces, or method syntax)

---

## Current Compiler Capabilities

### Architecture

**Compilation Pipeline:**
```
Source (.maxon) → Lexer → Parser → AST → Semantic Analysis → MIR Generation →
MIR Optimization → Register Allocation → x86-64 Code Generation → PE/ELF Binary
```

### Implemented Language Features

#### Type System
- Primitive types: `int` (32-bit), `float` (64-bit), `bool`, `character`, `ptr`
- Fixed-size arrays: `[N]type` with `.length` property
- Structs: User-defined types with field access and nested structs
- Type conversions: Explicit casting with `as` operator
- **Missing:** Generic types, enums, sum types

#### Control Flow
- If statements (single-line and multi-line with block identifiers)
- While loops with labeled break/continue
- For loops with range iteration
- All control structures require matching block identifiers

#### Functions
- Function declarations with parameters and return types
- Extern function declarations (FFI with process isolation)
- Export keyword for cross-file visibility
- Namespaces derived from file paths
- Recursion fully supported

#### Standard Library
- Math functions: sqrt, abs, sin, cos, tan, exp, log, pow, floor, ceil, round, trunc
- I/O: print, print_float
- Formatting: format_int, format_float
- **Missing:** File I/O, string manipulation, dynamic collections

### MIR (Middle Intermediate Representation)

**Complete SSA-based IR with ~50 instructions:**
- Full SSA form with phi nodes
- Comprehensive type system (i1, i8, i32, i64, f64, ptr, arrays, structs)
- Complete instruction set (arithmetic, comparisons, memory ops, control flow, calls)
- Type conversions and bitcasts
- GEP (GetElementPtr) for complex addressing

**Optimization Passes (10 implemented):**
1. Constant Folding
2. Constant Propagation
3. Dead Code Elimination
4. Unreachable Block Elimination
5. Strength Reduction (mul → shl)
6. Algebraic Simplification
7. Copy Propagation
8. PHI Elimination (SSA → register-allocatable form)
9. Redundant Load/Store Elimination (currently disabled due to bug)
10. Simple Function Inlining

**Advanced Features:**
- Memory SSA for precise load/store tracking
- SSA Verifier for correctness checking
- Iterative optimization until fixed-point

### x86-64 Code Generation

**Complete native x86-64 backend:**
- Direct machine code emission (no assembler needed)
- Linear-scan register allocation with spilling
- Support for both Windows (PE) and Linux (ELF) executables
- DWARF debug information generation (Linux)
- Calling convention support: Windows x64 ABI and System V ABI
- SSE2 floating-point instructions
- Position-independent code support

**Binary Writers:**
- PE32+ writer for Windows (with IAT, relocations, security features)
- ELF64 writer for Linux (with symbol tables, program headers)
- No external linker required for simple programs
- COFF library reader for static linking

### Current Limitations

#### Missing Data Structures (Critical)
- **No dynamic arrays/vectors** - Compiler uses `std::vector` extensively
- **No hash maps** - Uses `std::unordered_map` and `std::map` throughout
- **No strings beyond literals** - No string manipulation, concatenation, or dynamic strings
- **No heap allocation API** - Arrays are heap-allocated but no manual malloc/free exposed

#### Missing Language Features (High Priority)
- **No slices** - Can't pass array subsections efficiently
- **No reference types** - All parameters passed by value (except arrays)
- **No return of complex types** - Can't return arrays from functions
- **No enums** - Would need for AST node types, token types
- **No unions/variants** - Critical for AST representation
- **No generics** - Would need for container types
- **No operator overloading** - Limits expressiveness

#### Missing Standard Library (Critical)
- **No file I/O** - Only basic console output
- **No file reading** - Can't read source files
- **No string manipulation** - No split, join, substring, etc.
- **No memory management** - No allocator interface
- **No collections** - No vector, map, set implementations
- **No command-line argument parsing**

---

## Three-Track Approach

### Track 1: Full Self-Hosting (Ambitious, 12-18 months)
Rewrite the entire compiler in Maxon after implementing all missing features.

**Pros:**
- True self-hosting
- Complete language validation
- No C++ dependencies

**Cons:**
- Long timeline
- High risk
- Must rewrite complex low-level code

### Track 2: Partial Self-Hosting (Pragmatic, 6-9 months)
Rewrite high-level components (parser, semantic analyzer) in Maxon while keeping low-level code (MIR, codegen) in C++, using FFI bridges.

**Pros:**
- Faster timeline
- Lower risk
- Keeps proven, optimized code

**Cons:**
- Not fully self-hosting
- FFI complexity
- Split codebase

### Track 3: Incremental Bootstrap (RECOMMENDED, 3-6 months to first milestone)
Implement language features incrementally, rewriting one compiler component at a time in Maxon to validate each new feature.

**Pros:**
- Continuous validation
- Early dogfooding
- Risk distributed
- Can stop at any milestone

**Cons:**
- Requires careful planning
- Temporary FFI bridges

---

## Recommended Approach: Track 3 - Incremental Bootstrap

This approach provides continuous validation and allows you to start using Maxon for development quickly.

### Phase 1: Foundation - Dynamic Data Structures (4-6 weeks)

#### 1.1 Dynamic Arrays (Week 1-2)

**Language Feature:**
```maxon
// Syntax proposal
var numbers = Vector<int>()
numbers.push(42)
var size = numbers.size()
var first = numbers.get(0)
```

**Implementation needed:**
- Generic type support (or template-like mechanism)
- Methods on structs (`.push()`, `.get()`, `.size()`)
- Heap allocation API exposed to Maxon code
- Resize logic with capacity management

**Compiler component to rewrite:** Lexer (~500 lines)
- Token stream is just a dynamic array of tokens
- Simple enough to validate Vector implementation
- No complex data structures needed yet

**Deliverable:** Lexer written in Maxon, callable from C++ via FFI

---

#### 1.2 Dynamic Strings (Week 2-3)

**Language Feature:**
```maxon
var str = String("Hello")
str.append(" World")
var len = str.length()
var sub = str.substring(0, 5)
var parts = str.split(' ')
```

**Implementation needed:**
- String struct with dynamic buffer
- UTF-8 support (already in runtime)
- Common operations: concat, split, substring, find, compare
- String literals to String conversion

**Compiler component to rewrite:** Error reporting module
- Format error messages dynamically
- Concatenate file paths and error descriptions
- Tests lexer + string integration

**Deliverable:** Error formatter in Maxon

---

#### 1.3 Hash Maps (Week 3-4)

**Language Feature:**
```maxon
var symbols = map from string to int
symbols.insert("foo", 42)
var value = symbols.get("foo")
var exists = symbols.contains("foo")
```

**Implementation needed:**
- Hash function for common types
- Collision handling (chaining or open addressing)
- Resizing logic
- special interface for map types in the compiler for initialization

**Compiler component to rewrite:** Symbol table
- Maps identifier names to variable info
- Core data structure for semantic analysis
- Validates map implementation

**Deliverable:** Symbol table module in Maxon

---

### Phase 2: Type System Extensions (4-5 weeks)

#### 2.1 Enums (Week 5)

**Language Feature:**
```maxon
enum TokenType
    IDENTIFIER
    INTEGER
    PLUS
    MINUS
end 'TokenType'

var token = TokenType.IDENTIFIER
if token == TokenType.IDENTIFIER 'check'
    // ...
end 'check'
```

**Implementation needed:**
- Enum declaration syntax
- Enum value access (EnumName.VALUE)
- Comparison operators
- Underlying integer representation

**Compiler component to rewrite:** Token type enum
- Replace C++ `enum class TokenType` with Maxon enum
- Integration with lexer

**Deliverable:** Token definitions in Maxon

---

#### 2.2 Tagged Unions / Sum Types (Week 6-7)

**Language Feature:**
```maxon
union ASTNode
    IntegerLiteral { value int }
    BinaryOp { op TokenType, left ptr, right ptr }
    Identifier { name String }
end 'ASTNode'

var node = ASTNode.IntegerLiteral(42)
match node 'check'
    case IntegerLiteral(val) then print(val)
    case BinaryOp(op, l, r) then // handle binary op
    case Identifier(name) then // handle identifier
end 'check'
```

**Implementation needed:**
- Union declaration syntax
- Tagged union with discriminator
- Pattern matching (or type checking methods)
- Memory layout compatible with C++

**Compiler component to rewrite:** AST nodes
- ~30 AST node types currently in C++
- Core of compiler representation
- Validates union + pattern matching

**Deliverable:** AST node definitions in Maxon

---

#### 2.3 Generic Types (Week 8-9)

**Language Feature:**
```maxon
struct Pair<T, U>
    first T
    second U
end 'Pair'

function makePair<T, U>(a T, b U) Pair<T, U>
    var result = Pair<T, U>{a, b}
    return result
end 'makePair'
```

**Implementation needed:**
- Generic struct declarations
- Generic function declarations
- Type parameter substitution
- Monomorphization at compile time (C++ template approach)

**Compiler component to enhance:** All previous Maxon components
- Refactor Vector, HashMap to use proper generics
- Remove any hacky workarounds

**Deliverable:** Properly generic data structures

---

### Phase 3: I/O and Parsing (5-6 weeks)

#### 3.1 File I/O (Week 10)

**Language Feature:**
```maxon
var file = File.open("source.maxon", FileMode.READ)
var contents = file.readAll()
file.close()

var outFile = File.open("output.txt", FileMode.WRITE)
outFile.write("Hello")
outFile.close()
```

**Implementation needed:**
- File struct wrapping OS handles
- Open, read, write, close operations
- Error handling (return codes or exceptions)
- Platform abstraction (Windows/Linux)

**Compiler component to rewrite:** Source file reader
- Read .maxon files from disk
- Replace C++ `std::ifstream`
- Integration with lexer

**Deliverable:** File I/O module in Maxon

---

#### 3.2 Parser (Week 11-14)

**Language Feature needed:**
- Recursive types (for AST)
- Better error handling (optional/result types)

**Language additions:**
```maxon
struct Optional<T>
    hasValue bool
    value T
end 'Optional'

// Or proper option type
union Option<T>
    Some { value T }
    None
end 'Option'
```

**Compiler component to rewrite:** Parser (~2,000 lines)
- Largest single component so far
- Uses all previous features: Vector, HashMap, String, File I/O, Enums, Unions
- Parses Maxon syntax into AST
- Comprehensive test via existing test suite

**Deliverable:** Parser written in Maxon, producing AST consumable by C++ semantic analyzer

---

### Phase 4: Semantic Analysis (6-8 weeks)

#### 4.1 Advanced Type System Features (Week 15-16)

**Features needed:**
- Interfaces/traits for polymorphism
- Methods on structs (already started)
- Operator overloading (optional but useful)
- Reference types or slices

**Example:**
```maxon
struct TypeChecker
    symbols HashMap<String, Type>
    errors Vector<String>

    function checkExpr(this &TypeChecker, expr &ASTNode) Type
        // Method syntax with 'this' reference
    end 'checkExpr'
end 'TypeChecker'
```

---

#### 4.2 Semantic Analyzer (Week 17-20)

**Compiler component to rewrite:** Semantic analyzer (~3,000 lines)
- Type checking
- Symbol resolution
- Constant folding
- Semantic validation

**Challenges:**
- Complex control flow analysis
- Type unification
- Error recovery
- Scope management

**Deliverable:** Semantic analyzer in Maxon, producing type-checked AST

---

### Phase 5: MIR Generation (6-8 weeks)

#### 5.1 MIR Data Structures (Week 21-22)

**Features needed:**
- Better pointer/reference handling
- Memory management (arena allocator for MIR nodes)
- SSA construction helpers

**Compiler component to rewrite:** MIR builder
- Convert AST to MIR
- SSA construction
- Type lowering

**Deliverable:** MIR generation in Maxon

---

#### 5.2 MIR Optimizer (Week 23-26)

**Compiler component to rewrite:** Optimization passes
- Constant folding
- Dead code elimination
- Copy propagation
- All 10 existing passes

**Challenge:** Memory SSA is complex
- Uses sophisticated graph algorithms
- Dominance frontiers, phi placement
- May keep this in C++ initially

**Deliverable:** Core optimizations in Maxon

---

### Phase 6: Code Generation (Option 1: Keep in C++)

**Recommendation:** Keep x86-64 codegen, register allocation, and binary writers in C++

**Rationale:**
- Most complex, low-level code (~8,000 lines)
- Heavy bit manipulation
- Platform-specific
- Already production-quality
- Limited benefit from rewriting

**Alternative:** Use Maxon for high-level codegen, C++ for low-level

---

### Phase 6: Code Generation (Option 2: Full Bootstrap)

If you want true self-hosting:

#### 6.1 Low-Level Features (Week 27-28)
- Inline assembly
- Bit manipulation (shifts, masks, bitwise ops already exist)
- Pointer arithmetic
- Unsafe memory operations

#### 6.2 Register Allocator (Week 29-31)
- Linear scan algorithm
- Spill code generation
- Interference graph

#### 6.3 x86-64 Encoder (Week 32-35)
- Instruction encoding
- ModR/M, SIB byte generation
- Immediate encoding
- Relocation handling

#### 6.4 Binary Writers (Week 36-40)
- PE32+ writer (Windows)
- ELF64 writer (Linux)
- Symbol tables
- Debug information (DWARF)

**Deliverable:** Fully self-hosting compiler

---

## Implementation Priority Matrix

| Feature | Complexity | Impact | Priority | Timeline |
|---------|------------|--------|----------|----------|
| **Dynamic Arrays** | Medium | Very High | 1 | Week 1-2 |
| **Dynamic Strings** | Medium | Very High | 2 | Week 2-3 |
| **Hash Maps** | High | Very High | 3 | Week 3-4 |
| **Enums** | Low | High | 4 | Week 5 |
| **Tagged Unions** | High | Very High | 5 | Week 6-7 |
| **Generics** | Very High | Medium | 6 | Week 8-9 |
| **File I/O** | Medium | Very High | 7 | Week 10 |
| **Methods on Structs** | Medium | High | 8 | Week 11 |
| **Parser Rewrite** | Very High | High | 9 | Week 12-14 |
| **Interfaces/Traits** | High | Medium | 10 | Week 15-16 |
| **Semantic Analyzer** | Very High | High | 11 | Week 17-20 |
| **MIR Generation** | Very High | Medium | 12 | Week 21-26 |
| **Codegen (optional)** | Very High | Low | 13 | Week 27-40 |

---

## Validation Strategy

At each phase:

1. **Unit tests in Maxon** - Write test framework in Maxon early
2. **Integration tests** - Ensure Maxon components work with C++ components via FFI
3. **Dogfooding** - Use new features immediately to write compiler code
4. **Benchmark** - Compare performance of Maxon vs C++ components
5. **Regression tests** - All 88 backend tests + 55 spec tests must pass

---

## Risk Mitigation

**Risk 1: Performance degradation**
- Maxon code may be slower than optimized C++
- Mitigation: Profile and optimize hot paths, use FFI for critical sections

**Risk 2: FFI complexity**
- Bridging between Maxon and C++ adds complexity
- Mitigation: Clean interface design, minimize crossing boundaries

**Risk 3: Language feature explosion**
- Adding too many features increases maintenance
- Mitigation: Focus on minimal feature set, avoid over-engineering

**Risk 4: Debugging difficulty**
- Debugging compiler written in itself is challenging
- Mitigation: Keep C++ version working, comprehensive logging

---

## Milestones

### Milestone 1: Dynamic Data Structures (Month 1)
✓ Vector, String, HashMap working
✓ Lexer and symbol table rewritten in Maxon
✓ All backend tests passing

### Milestone 2: Type System (Month 2)
✓ Enums, unions, generics working
✓ AST nodes in Maxon
✓ Type-safe compiler internals

### Milestone 3: I/O and Parser (Month 3)
✓ File I/O working
✓ Parser in Maxon
✓ Can parse all existing Maxon code

### Milestone 4: Semantic Analysis (Month 4-5)
✓ Type checker in Maxon
✓ Symbol resolution in Maxon
✓ Full frontend in Maxon

### Milestone 5: MIR Pipeline (Month 6-7)
✓ MIR generation in Maxon
✓ Core optimizations in Maxon
✓ Backend still in C++ but called via FFI

### Milestone 6 (Optional): Full Bootstrap (Month 8-12)
✓ Entire compiler in Maxon
✓ No C++ dependencies
✓ True self-hosting

---

## Alternative: Partial Self-Hosting (Recommended End State)

**Keep in C++:**
- x86-64 codegen (8,000 lines)
- Register allocation
- PE/ELF writers
- Low-level runtime

**Write in Maxon:**
- Lexer (500 lines)
- Parser (2,000 lines)
- Semantic analyzer (3,000 lines)
- MIR generation (2,500 lines)
- High-level optimizations (1,500 lines)

**Total:** ~9,500 lines in Maxon, ~22,000 lines in C++

**Benefits:**
- Shorter timeline (6-7 months vs 12+ months)
- Lower risk
- Keeps proven, optimized low-level code
- Still allows dogfooding Maxon for compiler development
- Can always finish full bootstrap later

---

## Estimated Effort

### Conservative Estimate

**Phase 1 (Data Structures):** 3,000-5,000 lines + testing
**Phase 2 (Type Extensions):** 2,500-3,500 lines
**Phase 3 (Stdlib):** 2,000-3,000 lines
**Phase 4 (Advanced Features):** 3,000-5,000 lines

**Total new Maxon code:** ~10,000-16,500 lines

**Comparison:**
- Current compiler: ~31,500 lines of C++
- Would need: ~15,000 lines of new Maxon features
- Plus: ~30,000 lines to rewrite compiler in Maxon

**Timeline Estimate:** 6-12 months for a dedicated developer

---

## Next Steps

1. **Decide on approach:** Full bootstrap vs partial self-hosting
2. **Start Phase 1.1:** Implement dynamic arrays
3. **Rewrite lexer:** Validate Vector implementation
4. **Continue incrementally:** Follow the phase-by-phase plan
5. **Test continuously:** Ensure all tests pass at each step

---

## Conclusion

The Maxon compiler has excellent foundations for self-hosting:
- No external dependencies (no C runtime)
- Clean architecture (modular, well-separated concerns)
- Solid test coverage (specs + backend tests + fragments)
- Production-ready backend (Winchester x86-64 codegen)
- Working optimization pipeline

**Main blockers:**
1. Dynamic data structures (vectors, hash maps)
2. String manipulation
3. File I/O
4. Sum types/enums for AST representation
5. Generic containers

**Recommendation:** Focus on Phase 1 (data structures) first, as this unlocks the most capability. With dynamic arrays and hash maps in Maxon, you could rewrite significant portions of the compiler incrementally, testing as you go.

The incremental bootstrap approach (Track 3) is recommended because it provides continuous validation, allows early dogfooding, and distributes risk across multiple milestones. You can achieve partial self-hosting in 6-7 months and decide later whether to complete full bootstrap.
