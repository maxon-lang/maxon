# Self-Hosted Compiler Roadmap

The self-hosted Maxon compiler (`maxon-selfhosted/`) currently passes **3 of ~131 spec tests** (`basics`, `print-function`, `variables`). It has a working end-to-end pipeline (lexer → parser → Maxon dialect → Standard dialect → X86 dialect → code emitter → PE/ELF writer) but only supports a tiny subset of the language. This roadmap brings it to full parity with the C# compiler (`maxon-sharp/`).

Each phase includes X86 + ARM64 backends and PE + ELF output formats. All targets (`x64-windows`, `arm64-windows`, `x64-linux`, `arm64-linux`) are brought to parity within each phase.

## Progress

```
Phase 1:   Core Arithmetic        [x] (no deps)
Phase 2:   Control Flow           [ ] (depends on Phase 1)
Phase 3:   Function Params        [ ] (depends on Phase 1)
Phase 4:   Basic Types            [ ] (depends on Phase 3)
Phase 5:   Structs                [ ] (depends on Phase 3, 4)
Phase 6:   Managed Memory         [ ] (depends on Phase 5)
Phase 7:   Strings                [ ] (depends on Phase 6)
Phase 8:   Error Handling         [ ] (depends on Phase 2, 3)
Phase 9:   Enums                  [ ] (depends on Phase 2, 5)
Phase 10:  Closures               [ ] (depends on Phase 3, 5)
Phase 11:  Interfaces & Generics  [ ] (hybrid model — depends on Phase 5, 9, 10)
  11.0     Foundation: parser      [ ] (interface, <T>, where, from, with, uses)
  11.1     Type system             [ ] (substitution, conformance, where-clauses)
  11.2     Layout descriptors      [ ] (size/align/copy/destroy per concrete type)
  11.3     Witness tables          [ ] (one per (type, interface) conformance)
  11.4     Generic body lowering   [ ] (one body per generic method, implicit params)
  11.5     Per-function queries    [ ] (incremental MIR/code at function granularity)
  11.6     Inliner + @inlinable    [ ] (recover monomorphized perf on hot paths)
  11.7     Validation & polish     [ ] (full spec parity, perf tuning)
Phase 12:  Global Variables       [ ] (depends on Phase 5)
Phase 13:  Collections            [ ] (depends on Phase 6, 8, 11)
Phase 14:  Math Functions         [ ] (depends on Phase 4)
Phase 15:  Advanced Features      [ ] (depends on all above)
Phase 16:  Optimization Passes    [ ] (depends on Phase 15)
```

---

## Current Capabilities

- **Lexer**: DFA tokenizer with hex/binary/octal/underscore/scientific notation literals, `/` operator
- **Parser**: Function declarations (no params), `var`/`let` declarations, `return`, `print()`, `if`/`else`/`else if`, `while` loops, variable reassignment, block scoping, integer/float/boolean literals, full operator precedence (`+`/`-`/`*`/`/`/`mod`, comparisons, bitwise `and`/`or`/`xor`/`shl`/`shr`, unary `-`/`not`), parenthesized expressions, function calls (no args), string interpolation in print, top-level `let` string constants
- **Maxon Dialect**: 37 ops (literal, floatLiteral, return, varDecl, varLoad, varStore, add/sub/mul/div/mod, neg, bitNot/bitAnd/bitOr/bitXor/shl/shr, 6 comparisons, floatCmpEq, condBr, br, label, printLiteral, printInt, funcBegin, funcEnd, call)
- **Standard Dialect**: Mirrors Maxon dialect 1:1
- **X86 Dialect**: prologue/epilogue, movRegImm (32/64-bit), movSlot, loadSlot, addRegReg, subRegReg, imulRegReg, idivRcx, iremRcx, negReg, bitNotReg, andRegReg, orRegReg, xorRegReg, shlRegCl, sarRegCl, cmpRaxRcx, testRaxRax, condJump, jmp, label, float slot ops, ucomisd, printLiteral, printInt, funcLabel, callDirect
- **ARM64 Dialect**: prologue/epilogue, movImm, strToSlot, ldrFromSlot, addRegs, subRegs, mulRegs, sdivRegs, remRegs, negReg, mvnReg, andRegs, orrRegs, eorRegs, lslRegs, asrRegs, cmpRegs, cmpRegZero, condBranch, branch, label, branchLink, funcLabel, printLiteral, printInt, ret
- **Code Emitter**: Emits machine code for all above ops
- **PE/ELF Writers**: Working for simple programs

---

## Phase 1: Core Arithmetic & Expressions (~15 specs)

**Goal**: Complete arithmetic, bitwise, unary, and logical operators. Basic bool type.

### Specs to unlock
`arithmetic`, `arithmetic-operators`, `comparison-operators`, `bitwise-operators`, `unary-operators`, `unary-negation`, `expressions`, `literals`, `parentheses`, `bool-type`, `contextual-literal-typing`, `block-scoping`

### Changes

**Lexer** (`Lexer.maxon`):
- Add tokens: `/`, `%`, `&`, `|`, `^`, `<<`, `>>`, `~`, `!`, `&&`, `||`, `true`, `false`

**Parser** (`Parser.maxon`):
- Add precedence levels: logical-or → logical-and → bitwise-or → bitwise-xor → bitwise-and → shift → add/sub → mul/div/mod
- Parse unary `-`, `!`, `~`
- Parse `true`/`false` as literals (1/0)
- Parse `/` and `%` in mul/div level
- Parse `while` loops
- Parse `var` reassignment (assignment without `var`/`let`)
- Parse block scoping (push/pop scope in symbol table)

**Maxon Dialect** (`MaxonDialect.maxon`):
- Add: `div`, `mod`, `neg`, `not`, `bitAnd`, `bitOr`, `bitXor`, `shl`, `shr`, `logicalAnd`, `logicalOr`

**Standard Dialect** (`StandardDialect.maxon`):
- Add: `divI64`, `remI64`, `negI64`, `notI1`, `andI64`, `orI64`, `xorI64`, `shlI64`, `shrI64`, `andI1`, `orI1`

**MaxonToStandard** (`MaxonToStandardConversion.maxon`):
- Map new Maxon ops to Standard equivalents

**X86 Dialect** (`X86Dialect.maxon`):
- Add: `idivReg`, `cqo`, `andRegReg`, `orRegReg`, `xorRegReg`, `shlRegCl`, `sarRegCl`, `shrRegCl`, `negReg`, `setcc`, `xorRegReg` (for zeroing), `testRegReg`

**StandardToX86** (`StandardToX86Conversion.maxon`):
- Lower div/rem (cqo + idiv pattern)
- Lower bitwise ops
- Lower shift ops (move count to CL register)
- Lower unary negation

**X86CodeEmitter** (`X86CodeEmitter.maxon`):
- Emit machine code for all new X86 ops

**ARM64 Dialect** (`Arm64Dialect.maxon`):
- Add: `sdivRegReg`, `udivRegReg`, `msub` (for remainder), `andRegReg`, `orrRegReg`, `eorRegReg`, `lslRegReg`, `asrRegReg`, `lsrRegReg`, `negReg`, `mvnReg`

**StandardToArm64** (`StandardToArm64Conversion.maxon`):
- Lower div/rem (sdiv + msub pattern for remainder)
- Lower bitwise ops (ARM64 has 3-operand forms)
- Lower shift ops
- Lower unary negation

**Arm64CodeEmitter** (`Arm64CodeEmitter.maxon`):
- Emit machine code for all new ARM64 ops

**ELF Writer** (`ElfWriter.maxon`):
- Ensure ELF generation handles any new section requirements

### Files to modify
- `Compiler/Lexer.maxon`
- `Compiler/Parser.maxon`
- `Compiler/IR/Dialects/MaxonDialect.maxon`
- `Compiler/IR/Dialects/StandardDialect.maxon`
- `Compiler/IR/Conversion/MaxonToStandardConversion.maxon`
- `Compiler/Targets/X86/X86Dialect.maxon`
- `Compiler/Targets/X86/StandardToX86Conversion.maxon`
- `Compiler/Targets/X86/X86CodeEmitter.maxon`
- `Compiler/Targets/Arm64/Arm64Dialect.maxon`
- `Compiler/Targets/Arm64/StandardToArm64Conversion.maxon`
- `Compiler/Targets/Arm64/Arm64CodeEmitter.maxon`
- `Compiler/Targets/Shared/StdOpHelpers.maxon`
- `Compiler/Targets/Linux/ElfWriter.maxon`
- `Testing/SpecTestRunner.maxon` (update whitelist)

---

## Phase 2: Control Flow & While Loops (~8 specs)

**Goal**: Complete control flow — while loops, break/continue, match statements, for-in over ranges.

### Specs to unlock
`while-loops`, `break`, `if-statements` (full), `match-simple`, `ranges`, `return-statement`

### Changes

**Parser**:
- Parse `while` ... `end` with break/continue
- Parse `match` ... `end` with literal/wildcard arms
- Parse `for ... in` with integer ranges
- Parse `else if` chains (currently only if/else)

**Maxon Dialect**:
- Add: `whileBegin`, `whileEnd`, `breakOp`, `continueOp` (or implement via existing `condBr`/`br`/`label`)
- Match lowering uses existing condBr/br

**Note**: While loops and break/continue can be lowered to existing branch/label primitives in the parser itself (emit condBr at loop head, br at loop end, break jumps to after-loop label). No new dialect ops strictly required.

**No backend changes needed** — control flow uses existing `condBr`/`br`/`label` ops which already work on both X86 and ARM64.

### Files to modify
- `Compiler/Parser.maxon`
- `Testing/SpecTestRunner.maxon`

---

## Phase 3: Function Parameters & Multiple Functions (~8 specs)

**Goal**: Functions with parameters, multiple function definitions, function return types, basic type tracking.

### Specs to unlock
`function-declaration`, `parameter-labels`, `assignment`, `pass-by-reference` (basic), `qualified-names`

### Changes

**Parser**:
- Parse function parameters: `function foo(x int, y float) returns int`
- Parse parameter labels: `function foo(label name Type)`
- Track parameter types for type inference
- Support calling functions with arguments: `foo(1, 2)`
- Support variable assignment: `x = expr` (without var/let)

**Maxon Dialect**:
- Add: `paramOp(resultId, slotId, paramIndex)` for loading function parameters

**Standard Dialect**:
- Add: `paramOp(resultId, slotId, paramIndex)`

**X86 Dialect & Emitter**:
- Windows calling convention: RCX, RDX, R8, R9 for first 4 integer args
- Linux calling convention: RDI, RSI, RDX, RCX, R8, R9 for first 6 integer args
- Return values in RAX
- Emit `mov [rbp-offset], reg` in prologue for parameter save

**ARM64 Dialect & Emitter**:
- ARM64 calling convention: X0-X7 for first 8 integer args (same on Linux)
- Return values in X0
- Emit `str Xn, [sp, #offset]` in prologue for parameter save

**StandardToX86/StandardToArm64**:
- Generate parameter loading from calling convention registers to stack slots
- Generate argument passing in function calls

### Files to modify
- All dialect files + conversions + emitters (both X86 and ARM64)
- `Compiler/Parser.maxon`

---

## Phase 4: Basic Types (byte, short, i32, float, bool) (~10 specs)

**Goal**: Support primitive types beyond i64 and basic type casting.

### Specs to unlock
`byte-type`, `short-type`, `float-type`, `type-casting`, `implicit-type-conversion`

### Changes

**Parser**:
- Parse type annotations on variables: `var x int = 5`, `var y float = 3.14`
- Parse type casting: `int(x)`, `float(x)`, `byte(x)`
- Track types through expressions

**Maxon Dialect**:
- Add: `castOp(resultId, sourceId, fromType, toType)`, `intToFloat`, `floatToInt`, `truncI64ToI32`, `extI32ToI64`

**Standard Dialect**:
- Add: `fpToSiI64`, `siToFpF64`, `truncI64ToI32`, `extI32ToI64`, `addF64`, `subF64`, `mulF64`, `divF64`, `cmpF64` variants

**X86 Dialect & Emitter**:
- Add: `cvttsd2si`, `cvtsi2sd`, `addXmm`, `subXmm`, `mulXmm`, `divXmm`, `movXmmMem`, `movMemXmm`, `movzx`
- Emit SSE2 instructions for float arithmetic
- Emit conversion instructions
- Emit movzx for byte/short widening

**ARM64 Dialect & Emitter**:
- Add: `fcvtzs`, `scvtf`, `faddD`, `fsubD`, `fmulD`, `fdivD`, `fmovD`, `ldrD`, `strD`, `uxtb`, `uxth`
- ARM64 uses D-registers (D0-D31) for double-precision float
- Emit NEON/FP instructions for float arithmetic
- Emit zero-extend for byte/short widening

### Files to modify
- All pipeline files (both X86 and ARM64 backends)

---

## Phase 5: Structs (~8 specs)

**Goal**: Struct declarations, struct literals, field access, field assignment.

### Specs to unlock
`structs`, `challenge-nested-structs`, `challenge-struct-field-assign`, `self-keyword`, `module-level-struct-var`

### Changes

**Parser**:
- Parse `type Name ... end` struct declarations
- Parse struct literals: `Name{field: value, ...}`
- Parse field access: `value.field`
- Parse field assignment: `value.field = expr`
- Parse instance methods: `function foo(self, ...)` with `self` keyword
- Build struct type registry tracking field names, types, offsets

**Maxon Dialect**:
- Add: `structLiteral`, `fieldAccess`, `fieldAssign`, `structParam`

**Standard Dialect**:
- Add: `leaOp` (load effective address for struct pointers), `storeIndirect`, `loadIndirect`, `bulkZero`

**X86 Dialect & Emitter**:
- Add: `leaRegMem`, `movIndirectMemReg`, `movRegIndirectMem`, `repMovsb`, `callImport`
- Windows: heap allocation via `HeapAlloc`/`HeapReAlloc`/`HeapFree` (kernel32.dll IAT imports)
- Linux: heap allocation via `brk` syscall or `mmap` syscall

**ARM64 Dialect & Emitter**:
- Add: `adr`, `strIndirect`, `ldrIndirect`, `blImport`
- Linux: heap allocation via `brk`/`mmap` syscalls (SVC #0)

**PE Writer** (`PeWriter.maxon`):
- Add kernel32.dll imports for HeapAlloc, HeapReAlloc, HeapFree, GetProcessHeap

**ELF Writer** (`ElfWriter.maxon`):
- Ensure syscall stubs work for memory allocation on both x64 and arm64

**Memory model**: Structs passed as heap pointers (matching C# compiler). Struct literals allocate on heap via runtime alloc call.

### Files to modify
- All pipeline files (both backends)
- `Compiler/Targets/Windows/PeWriter.maxon`
- `Compiler/Targets/Linux/ElfWriter.maxon`

---

## Phase 6: Managed Memory & Arrays (~10 specs)

**Goal**: `__ManagedMemory` builtins, Array<T> basic operations (push, get, count, iteration).

### Specs to unlock
`arrays`, `stdlib-array`, `for-in loops`, `collection`, `collection-contains`

### Changes

**Standard Dialect**:
- Add: `callRuntime(name, args...)` for maxon_alloc, maxon_realloc, maxon_free, maxon_memmove

**Maxon Dialect**:
- Add: `managedMemCreate`, `managedMemGet`, `managedMemSet`, `managedMemGrow`, `managedMemShift`, `managedMemByteGet`, `managedMemByteSet`, `managedMemConcat`, `managedMemSlice`

**MaxonToStandard**:
- Lower managed memory ops to runtime calls with element size computation

**Parser**:
- Parse `for ... in` loops over arrays
- Parse array indexing: `arr[i]`
- Parse method calls on arrays: `arr.push(x)`, `arr.get(i)`, `arr.count()`
- Parse array literals: `[1, 2, 3]`

**X86 & ARM64 backends**: Both need `callRuntime` lowering — X86 uses IAT calls on Windows and syscalls on Linux, ARM64 uses syscalls on Linux. The runtime call dispatch is handled in `BackendDispatch.maxon` via `OsDescriptor`.

**PE Writer**: Ensure heap API imports are present (from Phase 5)
**ELF Writer**: Ensure syscall-based allocation works for both x64 and arm64

### Files to modify
- All pipeline files (both backends)
- `Compiler/Targets/Shared/OsDescriptor.maxon`
- Stdlib integration needed

---

## Phase 7: String Type & Interpolation (~6 specs)

**Goal**: Full String type with methods, string interpolation, character type. **Replace the temporary `print()`/`printLiteral`/`printInt` ops** with the real `print()` function from stdlib that uses `Stringable` and proper string concatenation.

### Specs to unlock
`string-type`, `string-interpolation`, `character-type`, `byte-string-literal`, `primitive-stringable`

### Changes

**Maxon Dialect**:
- Add: `stringLiteral`, `stringInterp`, `charLiteral`, `byteStringLiteral`
- Remove: `printLiteral`, `printInt` (temporary ops from bootstrap)

**Standard Dialect**:
- Remove: `printLiteral`, `printInt`

**X86 Dialect & Emitter / ARM64 Dialect & Emitter**:
- Remove: `printLiteral`, `printInt` ops and their hardcoded OS write stubs
- The real `print()` function will go through stdlib → String → managed memory → OS write call, using the same `callRuntime`/`callImport` infrastructure from Phases 5–6

**MaxonToStandard**:
- Lower string literals to rdata + managed memory wrapping
- Lower string interpolation to `.toString()` calls + concat sequences
- Lower `print()` as a normal function call to the stdlib `print` function

**Parser**:
- Remove the hardcoded `parsePrintStatement` and `parseStringInterpolation` special cases
- Parse `print(...)` as a regular function call (resolved to stdlib `print`)
- Parse string methods: `.count()`, `.isEmpty()`, `.contains()`, `.slice()`, `.findFirst()`, etc.
- Parse char literals: `'x'`
- Parse byte string literals: `b"..."`

**Note**: This is a breaking transition — earlier phases use the temporary `printLiteral`/`printInt` ops. All existing spec tests (`basics`, `print-function`, `variables`, etc.) must continue passing after the switchover.

### Files to modify
- All pipeline files (both backends)
- `Compiler/StdlibLoader.maxon` (stdlib `print` must be available)

---

## Phase 8: Error Handling (~5 specs)

**Goal**: `try`/`throw`/`otherwise` error handling, `throws` clause, error propagation.

### Specs to unlock
`error-handling`, `if-try`, `missing-return-error`

### Changes

**Parser**:
- Parse `throws` in function signatures
- Parse `throw` statements
- Parse `try ... otherwise 'label' ... end 'label'` blocks
- Parse `if try` conditionals

**Maxon Dialect**:
- Add: `throwOp`, `tryCallOp` (returns result + error flag)

**Standard Dialect**:
- Add: `tryCall`, `errorReturn` (return with RDX error flag)

**X86**:
- Use RDX as error flag register (matching C# compiler convention)
- After tryCall, check RDX and branch to otherwise block

**ARM64**:
- Use X1 as error flag register (matching C# compiler convention)
- After tryCall, check X1 and branch to otherwise block

### Files to modify
- All pipeline files (both backends)

---

## Phase 9: Enums & Match (~8 specs)

**Goal**: Enum type declarations, pattern matching with associated values.

### Specs to unlock
`enums-simple`, `enum-full`, `enum-match-only`, `match-statements`, `match-enum-typed-binding`, `constants` (enum keyword), `enum-struct-field-match`

### Changes

**Parser**:
- Parse `enum Name ... end` declarations (both simple enums and enums with associated values)
- Parse match with enum case extraction: `match val 'l' ... Case(x) then ... end 'l'`
- Parse enum construction: `EnumType.caseName(value)`

**Maxon Dialect**:
- Add: `enumLiteral`, `enumConstruct`, `enumTag`, `enumPayload`, `enumRawValue`, `enumName`

**Memory model**: Enums stored as tag (i64) + max-payload-size buffer. Tag identifies the case, payload holds the associated value.

### Files to modify
- All pipeline files

---

## Phase 10: Closures & First-Class Functions (~5 specs)

**Goal**: Function pointers, closures with captured variables, indirect calls.

### Specs to unlock
`first-class-functions`, `closure-capture`

### Changes

**Parser**:
- Parse function type annotations in parameters
- Parse closure creation (anonymous functions with capture)
- Parse indirect calls through function variables

**Maxon Dialect**:
- Add: `functionRef`, `functionVarRef`, `indirectCall`, `closureCreate`, `closureEnvLoad`

**Standard Dialect**:
- Add: `funcRef`, `indirectCall`, `storePtr`, `loadPtr`

**X86**:
- Add: `callIndirect(reg)`, `leaFuncAddr(name)`

**ARM64**:
- Add: `blrReg`, `adrFunc(name)`

**Closure convention**: Closure is a 2-word struct (function pointer + environment pointer). Environment is a heap-allocated array of captured values. Same convention on all targets.

### Files to modify
- All pipeline files (both backends)

---

## Phase 11: Interfaces & Generics — Hybrid Model (~15 specs)

**Strategic positioning**: this phase is the technical realization of Maxon's positioning as **"Rust-class safety, Swift-class compile times."** It is the most consequential design decision in the project.

**Goal**: Interface declarations, conformance, generic functions and types — using a **hybrid** strategy:

- **Layouts are monomorphized** — `Array<Int>` stores i64s inline, `Array<String>` stores pointers; size/alignment/inline-vs-pointer is per-instantiation
- **Methods are NOT monomorphized** — generic method bodies are compiled once and dispatched through layout descriptors and witness tables (Swift-style)
- **Aggressive inlining at static call sites** recovers monomorphized-quality code on hot paths

**Why hybrid instead of full monomorphization** (which the C# bootstrap uses):

1. **Compile speed** — one body per generic method instead of N per (method × type). Eliminates the multiplicative downstream cost (lowering, codegen, register allocation all do less work)
2. **Real per-function incremental compilation** — caller IR references stable callee names (`Array.push`) instead of specialized ones (`__Array_Int.push`), so a change to a generic body doesn't invalidate caller caches
3. **Smaller binaries** — typically 2–3× smaller code section, better icache behavior
4. **Dynamic dispatch as a first-class language feature** — heterogeneous collections of trait objects, plugin systems, hot-reload all become tractable
5. **Cleaner errors and IDE experience** — diagnostics reference the source generic, not a mangled specialization
6. **Preserved runtime perf** — `@inlinable` on hot stdlib paths recovers monomorphized output at static call sites

**Why now**: this is the cheapest moment in the project to commit to this design. After Phase 11 ships with a different model (e.g., full monomorphization), retrofitting witness tables means tearing apart the type system, the dispatch story, every cached MIR, and every emitted symbol. Doing it now is ~5 months of focused work; doing it later is 12+ months and a full incremental cache invalidation.

**Reference**: see [`docs/hybrid-generics-plan.md`](../docs/hybrid-generics-plan.md) for the full design document, design decisions log, risks, and rationale.

### Specs to unlock

`interfaces`, `interface-conformance`, `interface-extensions`, `equatable`, `primitive-comparable`, `primitive-cloneable`, `primitive-hashable`, `where-clauses`, `conditional-extensions`, `associated-types`, `type-methods`, `static-methods`, `instance-methods`

### Sub-phase overview

| Sub-phase | Focus | Estimate | Risk |
|---|---|---|---|
| 11.0 | Foundation: parser + AST | 2–3 weeks | Low |
| 11.1 | Type system: substitution + conformance | 3–4 weeks | Medium |
| 11.2 | Layout descriptors | 2–3 weeks | Medium |
| 11.3 | Witness tables | 2–3 weeks | Medium |
| 11.4 | Generic body lowering | 4–6 weeks | **High (pivotal piece)** |
| 11.5 | Per-function incremental queries | 2–3 weeks | Medium |
| 11.6 | Inliner + `@inlinable` | 3–5 weeks | Medium |
| 11.7 | Validation & polish | 2–3 weeks | Low |
| **Total** | | **~16–30 weeks** (4–7 months) | |

### Phase 11.0 — Foundation: parser + interface declarations

**Goal**: parser accepts the full surface syntax for generics and interfaces; AST shape is correct. No semantic action yet.

**Changes**:
- **Lexer** ([`Lexer.maxon`](Compiler/Lexer.maxon)): keywords `interface`, `extension`, `extends`, `implements`, `with`, `where`, `from`, `uses` are already tokenized — verify precedence and contextual handling
- **Parser** ([`Parser.maxon`](Compiler/Parser.maxon)): add productions for
  - `interface Name uses T1, T2 ... end` (with optional associated types)
  - `type Name uses T1 implements I1, I2 with(...) ... end`
  - `where T: Comparable` and `where T: Equatable and Hashable`
  - `function foo<T>(x T) returns T where T: Comparable`
  - `extension Iterable uses Element ... end`
  - `from Type implements Interface ... end` (out-of-line conformance)
- **MaxonDialect** ([`MaxonDialect.maxon`](Compiler/IR/Maxon/MaxonDialect.maxon)): extend `MaxonType` with
  - `typeParameter(id TypeNameId)` — unresolved `T` references
  - `genericInstance(baseId TypeNameId, args MaxonTypeArray)` — `Array with Int`
- **Project** ([`Project.maxon`](Compiler/Project.maxon)): add tables
  - `interfaces InterfaceMap` — interface name → method list + associated types
  - `conformances ConformanceMap` — `(typeName, interfaceName)` → conformance entry
  - `typeParameters TypeParamMap` — type param `T` in scope → constraint set

**Reuse**: the C# bootstrap parses all this syntax — grammar is solved. Mostly transcription with adjustments for self-hosted parsing style.

**Tests**: parser-only tests verifying AST shape; spec tests under `specs/interfaces/`, `specs/generics/` start parsing successfully (still fail at lower stages).

### Phase 11.1 — Type system: substitution + conformance

**Goal**: type system represents and reasons about generics. Type checking distinguishes instantiated from uninstantiated types.

**New types**:
```
export type InterfaceType
    var name String
    var methods InterfaceMethodArray   // signature only
    var associatedTypes StringArray
end

export type GenericTypeDecl
    var name String                    // "Array"
    var typeParams StringArray         // ["Element"]
    var constraints ConstraintMap      // "Element" -> [Equatable, Hashable]
    var fields StructFieldArray        // pre-substitution
    var methods FunctionDeclArray
    var conformsTo InterfaceConformanceArray
end

export type TypeSubstitution
    var bindings Map with(String, MaxonType)  // "Element" -> .named(Int)
end
```

**New files**:
- `Compiler/TypeSystem/Substitution.maxon` — pure substitution + conformance lookup
- `Compiler/TypeSystem/Constraints.maxon` — where-clause checking

**Algorithms**:
- `substituteType(type, sub) returns MaxonType` — pure structural walk, no mutation
- `typeConformsTo(type, interface, sub) returns bool` — checks all interface methods + recurses into where-clauses
- Constraint satisfaction is checked at the **call site**, not at the generic body (matters for incremental: bodies never need re-checking when callers change)

**Critical decision**: we are **not** specializing types per call-site arg type the way the C# bootstrap does. Interface-typed parameters stay interface-typed. The witness table makes this work without specialization.

**Tests**: unit tests for substitution correctness, conformance lookup, where-clause satisfaction. No code generation yet.

### Phase 11.2 — Layout descriptors

**Goal**: every concrete instantiation of a generic type gets a layout descriptor, emitted into the data section.

**New IR concept**:
```
export type LayoutDescriptor
    var typeName String                // diagnostic only
    var size MachineWord               // total size in bytes
    var alignment MachineWord
    var elementSize MachineWord        // for collections: stride between elements
    var fieldOffsets MachineWordArray  // for structs: per-field byte offset
    var copyFunc String?               // function name to copy, null = memcpy
    var destroyFunc String?            // function name to destroy/decref, null = noop
    var hasHeapRefs bool               // does this type contain managed pointers?
end
```

**New pass**: `BuildLayoutDescriptors` (`Compiler/Passes/BuildLayoutDescriptors.maxon`)
- Walks all `genericInstance` types reached during type checking
- Computes layout (recursively descending into fields)
- Produces a fresh `named` type for the concrete instantiation (`Array_Int`, `Pair_Int_Float`)
- Emits the descriptor into a new `LayoutDescriptorTable` on `Project`
- Emits descriptors into `.rdata` with stable labels (`__layout_Array_Int`)

**Reuse**: existing `GlobalDataTable` infrastructure for rdata emission.

**Tests**: snapshot tests of generated descriptor tables for `Array<Int>`, `Array<String>`, `Pair<Int, Float>` etc.; verify size/alignment match C# bootstrap output.

### Phase 11.3 — Witness tables for interfaces

**Goal**: every `(type, interface)` conformance gets a witness table emitted as data; interface-typed values have a uniform runtime representation.

**Witness table layout**:
```
__witness_Int_Comparable:
    .quad <ptr to Int.compare>
__witness_String_Comparable:
    .quad <ptr to String.compare>
```
One witness table per `(type, interface)` pair. Method order is fixed per interface (deterministic from interface declaration order).

**Interface-typed values**: fat pointer at runtime
```
{value: ptr_or_inline, witness: ptr_to_witness_table}
```
- For value types ≤ 8 bytes (Int, Bool), value is inlined
- For larger types, a pointer to a heap allocation

**Method dispatch**:
- `x.method()` where `x: SomeInterface` → load witness from x, load method-N, indirect call
- `x.method()` where `x: ConcreteType` (statically known) → direct call to `ConcreteType.method`

**New pass**: `BuildWitnessTables` (`Compiler/Passes/BuildWitnessTables.maxon`)

**Critical decision**: single witness table per `(type, interface)` (Swift-style), not embedded-in-value (Java/Go-style). Reason: smaller value representation, fewer cache misses for collections of interface-typed values.

**Tests**: spec tests for `Iterable`, `Comparable`, `Equatable` calling through trait objects.

### Phase 11.4 — Generic body lowering [PIVOTAL]

**Goal**: generic method bodies are lowered **once** with implicit layout/witness parameters. Calls pass the right descriptors. This is where the project shape diverges most from the C# bootstrap.

**Changes to [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon)**:
- When lowering a generic method like `Array<T>.push(self, value)`:
  - Add implicit parameters: `(self, value, layout: ptr, /* witness if T: Interface */)`
  - Replace operations that depend on `T`'s layout with descriptor-driven ops:
    - `sizeof(T)` → load `[layout + 0]`
    - `alignof(T)` → load `[layout + 8]`
    - `copy(dst, src, T)` → indirect call to `[layout + copyFuncOffset]`
    - `destroy(x, T)` → indirect call to `[layout + destroyFuncOffset]`
- When lowering a call `arr.push(x)` where `arr: Array<Int>`:
  - Look up the layout descriptor label for `Array<Int>`
  - Emit a load of that label as the implicit `layout` argument
  - Emit a regular call to `Array.push` (single shared body, name does NOT include `_Int`)

**New ops in [`StdDialect.maxon`](Compiler/IR/Std/StdDialect.maxon)**:
- `loadLayoutDescriptor(label String) returns ValueId`
- `loadWitnessTable(typeName String, interfaceName String) returns ValueId`
- `descriptorField(layout ValueId, fieldOffset MachineWord) returns ValueId`
- `witnessMethod(witness ValueId, methodIndex int) returns ValueId`

All resolve to existing `loadIndirect` / `funcRef` ops at MIR level — these are sugar over what's already there.

**ABI lowering** ([`LowerABI.maxon`](Compiler/IR/Std/LowerABI.maxon)):
- Implicit layout/witness params are classified as register parameters; existing classifier handles this naturally

**The pivotal property**: after this phase, `Array.push` is **one** function in the emitted module, named `Array.push` (not `Array_Int.push`). Caller IR references `Array.push` regardless of `T`. **This is what unlocks per-function incremental compilation in 11.5.**

**Risks**:
- Refcount insertion: descriptor's `destroy` handles ref-counting per-element; surrounding code does ref-counting on the container itself. Mitigation: extensive testing of refcount-balanced code paths.
- Generic constructors: `Array<T>.create()` needs `T`'s layout to allocate. Solved by passing the descriptor into the constructor.

**Mitigation strategy**: build a parallel test harness that compiles a small "canonical generic test program" (push/pop on `Array<Int>`, `Array<String>`, sort with `Comparable`) and snapshots both IR and runtime output. Run after every commit during this phase.

**Tests**: full Iterable/Iterator/Array test suite must pass. Compare emitted IR shape against C# bootstrap to confirm correctness (perf will diverge — recovered in 11.6).

### Phase 11.5 — Per-function incremental queries

**Goal**: extend the query system to cache MIR and emitted code at function granularity. Edit-one-function rebuild time drops from ~3s to ~50–200ms.

**Only possible because Phase 11.4 made callee names stable** — caller IR doesn't change when callee bodies change.

**New queries in [`Queries.maxon`](Compiler/Queries.maxon)**:
- `queryMidForFunction(project, funcName) returns StdFunction`
- `queryMirForFunction(project, funcName) returns MirFunction`
- `queryCodeForFunction(project, funcName) returns FunctionCode`

**Refactor**:
- [`PassPipeline.maxon`](Compiler/IR/PassPipeline.maxon) — add `runForFunction(funcName)` paths
- Whole-module passes (DFE, layout descriptor generation) stay at `queryAllMid` granularity
- Per-function queries operate after these whole-module passes

**Cache invalidation**:
- `queryMirForFunction(F)` depends on its own body, signatures (not bodies) of functions F calls, and the layout descriptors F uses
- A change to function G's **body** invalidates only `queryMir/CodeForFunction(G)`, not F's caches
- A change to function G's **signature** invalidates F if F calls G

**Tests**: extend [`IncrementalTestRunner.maxon`](Compiler/Testing/IncrementalTestRunner.maxon) with new scenarios:
- "Edit one function body" → only that function's MIR/code recomputed
- "Edit a generic body" → only that generic body's MIR recomputed (not all callers)
- "Add a new instantiation" → new layout descriptor generated; existing function bodies untouched
- "Edit a function signature" → callers of that function recomputed; non-callers untouched

### Phase 11.6 — Inliner + `@inlinable` annotations

**Goal**: aggressive inlining at static call sites recovers monomorphized-quality code on hot paths. **This is what makes the runtime perf cost of the hybrid model approach zero on typical code.**

**New pass**: `Compiler/Passes/Inliner.maxon`
- Operates on Std-level IR before MIR lowering
- For each call site `arr.push(x)` where `arr: Array<Int>`:
  - The layout descriptor is statically known (fixed label reference)
  - The call target is statically known (`Array.push`)
  - If callee is `@inlinable` (or below size threshold and not recursive), inline the body
  - Once inlined, descriptor field loads become loads from known constant addresses
  - Constant folding eliminates descriptor accesses entirely
  - Result: byte-identical to monomorphized output for hot paths

**Heuristics**:
- Always inline: callees marked `@inlinable`, callees < 20 ops, callees with a single call site
- Profile-guided / size-balanced: medium callees, multiple call sites
- Never inline: recursive (without bound), `@noinline`, truly dynamic types

**New annotations**:
- `@inlinable` — hint for the inliner
- `@noinline` — block inlining
- `@alwaysInline` — force inlining; error if unable

**Stdlib annotation pass**:
- Annotate ~20–30 hot stdlib methods: `Array.push`, `Array.get`, `Array.length`, iterator `current`/`advance`, `Optional.unwrap`, etc.
- Mostly mechanical work; recovers 90%+ of monomorphization's runtime perf

**Constant folding extensions**:
- Extend the canonicalize pass to recognize loads from known descriptor addresses and replace with constants
- Without this, inlining alone doesn't recover the perf

**Critical decision**: the inliner **does not** automatically inline unannotated functions. Inlining unannotated functions is too risky for compile time (can blow up code size). Stdlib gets explicit annotations; user code uses size-based heuristics.

**Tests**:
- Benchmarks: inlined hot paths match monomorphized C# bootstrap output to within 5%
- All spec tests pass (inlining is purely an optimization)
- IR snapshots showing descriptor loads constant-folded after inlining

### Phase 11.7 — Validation, polish, parity

**Goal**: spec test parity with C# bootstrap, performance comparable to or better than current full-monomorphization design.

**Performance targets**:
- Compile time of stdlib + selfhosted source: **≤ 60% of current C# bootstrap** (~3s vs current 5s)
- Incremental rebuild of one-function edit: **≤ 200ms**
- Runtime: within **10%** of C# bootstrap output on representative benchmarks; within **2%** on stdlib hot loops (with inlining)
- Binary size: **≤ 50%** of C# bootstrap output

**Validation**:
- All ~131 spec tests pass on self-hosted compiler
- Self-host: self-hosted compiler can compile itself
- Self-host benchmark: self-hosted compiler compiling itself in ≤ 3s

**Diagnostic quality**:
- "could not satisfy `T: Comparable` at call site of `sort` in `main:line 42`" rather than `__Array_Foo.sort not found`
- IR dumps clearly show generic bodies and inlined call sites
- LSP integration: hover on a generic call site shows the inferred substitution

**Documentation updates**:
- [`ARCHITECTURE.md`](ARCHITECTURE.md) updated with hybrid generics design
- New `docs/generics-design.md` describing runtime representation, witness tables, layout descriptors

### Files to modify (summary)

| Sub-phase | File / area |
|---|---|
| 11.0 | [`Lexer.maxon`](Compiler/Lexer.maxon), [`Parser.maxon`](Compiler/Parser.maxon), [`MaxonDialect.maxon`](Compiler/IR/Maxon/MaxonDialect.maxon), [`Project.maxon`](Compiler/Project.maxon) |
| 11.1 | New: `Compiler/TypeSystem/Substitution.maxon`, `Compiler/TypeSystem/Constraints.maxon` |
| 11.2 | New: `Compiler/Passes/BuildLayoutDescriptors.maxon`; [`Project.maxon`](Compiler/Project.maxon) (`LayoutDescriptorTable`) |
| 11.3 | New: `Compiler/Passes/BuildWitnessTables.maxon` |
| 11.4 | [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon), [`StdDialect.maxon`](Compiler/IR/Std/StdDialect.maxon) |
| 11.5 | [`Queries.maxon`](Compiler/Queries.maxon), [`PassPipeline.maxon`](Compiler/IR/PassPipeline.maxon), [`IncrementalTestRunner.maxon`](Compiler/Testing/IncrementalTestRunner.maxon) |
| 11.6 | New: `Compiler/Passes/Inliner.maxon`; stdlib annotations |
| 11.7 | All pipeline files (perf tuning); [`ARCHITECTURE.md`](ARCHITECTURE.md), `docs/generics-design.md` |

### Reference: C# bootstrap mechanics to reuse vs. avoid

The C# bootstrap's [`MonomorphizationPass.cs`](../maxon-sharp/Compiler/MLIR/Passes/MonomorphizationPass.cs) is a useful reference for **type-system mechanics** (substitution algorithm, conformance lookup, recursion detection via `IsRecursiveTypeNesting`).

It is **not a model to port** for the dispatch strategy — we explicitly do not want the function-cloning approach. The bootstrap clones a function per (callee × type-args). The hybrid lowers it once.

### Out of scope for this phase

- **Cross-module inlining / link-time optimization** — separate project; not needed for the self-hosting compile case (everything's in one compilation unit)
- **Profile-guided optimization** — orthogonal; could be added later as a second-stage compilation mode
- **Specialization-by-attribute (`@specialize`)** — future optimization for cases where inlining isn't enough; not needed for v1
- **Type erasure for code size** — Swift's existential containers; we keep monomorphized layouts on purpose because they're a key perf win
- **Backporting to the C# bootstrap** — the C# compiler stays as-is; it's the reference, not the target

---

## Phase 12: Global Variables & Static State (~4 specs)

**Goal**: Module-level variables, static variables on types.

### Specs to unlock
`static-variables`, `top-level-let` (full), `module-level-struct-var`

### Changes

**Parser**:
- Parse module-level `var` declarations (not just `let`)
- Parse `static` keyword on struct fields and methods

**Standard Dialect**:
- Add: `globalLoad`, `globalStore` (for various types)

**X86 Dialect & Emitter**:
- Add: `globalLoad(name, size)`, `globalStore(name, size)`
- RIP-relative addressing for global access

**ARM64 Dialect & Emitter**:
- Add: `globalLoad(name, size)`, `globalStore(name, size)`
- ADRP + ADD for global access (PC-relative page addressing)

**PE Writer**:
- Add `.data` section for global variables
- Initialize global variables at program startup

**ELF Writer**:
- Add `.data` section for global variables (both x64 and arm64)

### Files to modify
- All pipeline files (both backends) + PE writer + ELF writer

---

## Phase 13: Collections (Map, Set, Vector) (~8 specs)

**Goal**: Hash map, hash set, and vector collections.

### Specs to unlock
`map`, `set`, `vector`, `array-hashable`, `map-struct-bytearray`, `map-try-otherwise-block`

### Changes

These are mostly stdlib types built on top of Array and generics. Requires:
- Working generics (Phase 11)
- Working ManagedMemory (Phase 6)
- Hashable interface (Phase 11)
- Error handling (Phase 8)

**Stdlib integration**: Parse and compile the stdlib `.maxon` files as part of the compilation unit.

### Files to modify
- `Compiler/StdlibLoader.maxon`
- May need stdlib parsing improvements

---

## Phase 14: Math Functions (~18 specs)

**Goal**: Built-in math functions (abs, sqrt, floor, ceil, round, min, max, trig, log, exp, pow).

### Specs to unlock
`abs`, `ceil`, `floor`, `round`, `trunc`, `sqrt`, `pow`, `sin`, `cos`, `tan`, `atan2`, `exp`, `log`, `log2`, `log10`, `min`, `max`

### Changes

**Maxon Dialect**:
- Add: `absOp`, `sqrtOp`, `floorOp`, `ceilOp`, `roundOp`, `minOp`, `maxOp`

**X86 Dialect & Emitter**:
- Add: `sqrtXmm`, `roundXmm`, `minXmm`, `maxXmm`, `andMaskRipRel` (for abs)

**ARM64 Dialect & Emitter**:
- Add: `fsqrtD`, `frintaD` (round), `frintnD`, `frintmD` (floor), `frintpD` (ceil), `fminD`, `fmaxD`, `fabsD`

**Trig/log/exp**: These require runtime library calls (C runtime or custom implementations).
- Windows: Import from `ucrtbase.dll`
- Linux: Import from `libm.so` (dynamic linking) or implement via syscalls/soft-float

**PE Writer**:
- Add `ucrtbase.dll` imports for: sin, cos, tan, atan2, exp, log, log2, log10, pow

**ELF Writer**:
- For Linux targets, either link against libm or implement soft-float versions

### Files to modify
- All pipeline files (both backends) + PE writer + ELF writer

---

## Phase 15: Advanced Features & Remaining Specs (~20 specs)

**Goal**: Everything else — tuples, namespaces, exports, panic/stack traces, command-line args, file I/O, etc.

### Specs to unlock
`tuples`, `namespaces`, `export-keyword`, `command-line-args`, `file-io`, `directory`, `panic-stack-trace`, `alloc-tracking`, `codegen-internals`, `managed-memory-element-size`, `stdlib-basic`, `stdlib-autodiscovery`, `grapheme-clusters`, `slice-memory`, `array-managed-elements`, `array-of-bytearray`, `challenge-array-of-structs`, `challenge-struct-lifetime`, `register-allocator`, `unused-variables`, `unused-parameters`, `discarded-results`, `duplicate-functions`, `duplicate-block-identifiers`, `unknown-keyword-error`, `type-checking`, `function-overloads`, `method-calls`, `method-call-on-parameter`, `init-from-literal`, `initablefromarrayliteral`, `parsable-interface`, `ranged-typealias`, `enum-hashable`, `advent`

### Sub-phases

**15a: Semantic Checks** — unused variables/params warnings, duplicate function detection, type checking errors
**15b: Command-line Args** — parse `CommandLine.arguments()`, requires PE import for GetCommandLineW
**15c: File I/O** — File.readText, File.writeText, File.readBinary, etc. via Windows API imports
**15d: Panic & Stack Traces** — panic op that prints stack trace and exits
**15e: Tuples** — destructuring in let/var, tuple return types
**15f: Namespaces & Exports** — module system, `export` keyword, qualified name resolution
**15g: Function Overloads** — overload resolution based on parameter types
**15h: Method Call Syntax** — `value.method(args)` desugaring to `Type.method(value, args)`

---

## Phase 16: Optimization Passes

**Goal**: Match C# compiler's optimization passes for reasonable code quality.

### Passes to implement
1. **Dead store elimination** — remove stores that are never read
2. **Store forwarding** — eliminate redundant loads
3. **Peephole optimization** — simple instruction combining
4. **Dead function elimination** — remove uncalled functions
5. **Constant folding** — evaluate compile-time constant expressions

### Files to modify
- New files in `Compiler/IR/Passes/`

---

## Verification

After each phase:
1. Update the spec whitelist in `Testing/SpecTestRunner.maxon`
2. Build the self-hosted compiler: `./bin/maxon.exe build maxon-selfhosted`
3. Run spec tests for all three targets:
   - `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test` (x64-windows, runs natively)
   - `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test --target=x64-linux` (runs via Docker)
   - `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test --target=arm64-linux` (runs via Docker)
4. Verify all whitelisted tests pass on all targets
5. Cross-check against C# compiler: `./bin/maxon.exe spec-test` to ensure behavioral parity
