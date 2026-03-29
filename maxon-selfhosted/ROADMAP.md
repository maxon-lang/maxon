# Self-Hosted Compiler Roadmap

The self-hosted Maxon compiler (`maxon-selfhosted/`) currently passes **3 of ~131 spec tests** (`basics`, `print-function`, `variables`). It has a working end-to-end pipeline (lexer → parser → Maxon dialect → Standard dialect → X86 dialect → code emitter → PE/ELF writer) but only supports a tiny subset of the language. This roadmap brings it to full parity with the C# compiler (`maxon-sharp/`).

Each phase includes X86 + ARM64 backends and PE + ELF output formats. All targets (`x86_64-windows`, `aarch64-windows`, `x86_64-linux`, `aarch64-linux`) are brought to parity within each phase.

## Progress

```
Phase 1:  Core Arithmetic        [x] (no deps)
Phase 2:  Control Flow           [ ] (depends on Phase 1)
Phase 3:  Function Params        [ ] (depends on Phase 1)
Phase 4:  Basic Types            [ ] (depends on Phase 3)
Phase 5:  Structs                [ ] (depends on Phase 3, 4)
Phase 6:  Managed Memory         [ ] (depends on Phase 5)
Phase 7:  Strings                [ ] (depends on Phase 6)
Phase 8:  Error Handling         [ ] (depends on Phase 2, 3)
Phase 9:  Unions                 [ ] (depends on Phase 2, 5)
Phase 10: Closures               [ ] (depends on Phase 3, 5)
Phase 11: Interfaces & Generics  [ ] (depends on Phase 5, 9, 10)
Phase 12: Global Variables       [ ] (depends on Phase 5)
Phase 13: Collections            [ ] (depends on Phase 6, 8, 11)
Phase 14: Math Functions         [ ] (depends on Phase 4)
Phase 15: Advanced Features      [ ] (depends on all above)
Phase 16: Optimization Passes    [ ] (depends on Phase 15)
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
- `Compiler/MLIR/Dialects/MaxonDialect.maxon`
- `Compiler/MLIR/Dialects/StandardDialect.maxon`
- `Compiler/MLIR/Conversion/MaxonToStandardConversion.maxon`
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
- Ensure syscall stubs work for memory allocation on both x86_64 and aarch64

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
**ELF Writer**: Ensure syscall-based allocation works for both x86_64 and aarch64

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

## Phase 9: Unions (Enums) & Match (~8 specs)

**Goal**: Union type declarations, pattern matching with associated values.

### Specs to unlock
`unions-simple`, `union-full`, `union-match-only`, `match-statements`, `match-union-typed-binding`, `constants` (enum keyword), `union-struct-field-match`

### Changes

**Parser**:
- Parse `union Name ... end` declarations
- Parse `enum Name ... end` declarations (simple constants)
- Parse match with union case extraction: `match val 'l' ... Case(x) then ... end 'l'`
- Parse union construction: `UnionType.caseName(value)`

**Maxon Dialect**:
- Add: `enumLiteral`, `enumConstruct`, `enumTag`, `enumPayload`, `enumRawValue`, `enumName`

**Memory model**: Unions stored as tag (i64) + max-payload-size buffer. Tag identifies the case, payload holds the associated value.

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

## Phase 11: Interfaces & Generics (~15 specs)

**Goal**: Interface declarations, conformance, generic functions and types, monomorphization.

### Specs to unlock
`interfaces`, `interface-conformance`, `interface-extensions`, `equatable`, `primitive-comparable`, `primitive-cloneable`, `primitive-hashable`, `where-clauses`, `conditional-extensions`, `associated-types`, `type-methods`, `static-methods`, `instance-methods`

### Changes

**Parser**:
- Parse `interface Name ... end` declarations
- Parse generic type parameters: `function foo<T>(x T)`
- Parse where clauses: `where T: Equatable`
- Parse `from` keyword for conformance declarations

**Monomorphization**:
- Implement a monomorphization pass that creates concrete instantiations of generic functions/types
- Track which concrete types are used and generate specialized versions

**Interface dispatch**:
- Static dispatch via monomorphization (no vtables needed for most cases)
- Extension methods attached to types at compile time

**This is the largest and most complex phase.** It requires:
1. A type registry tracking all types and their interface conformances
2. Generic instantiation logic
3. Method resolution for dot-syntax calls

### Files to modify
- All pipeline files
- New file: `Compiler/MonomorphizationPass.maxon`

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
- Add `.data` section for global variables (both x86_64 and aarch64)

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
`tuples`, `namespaces`, `export-keyword`, `command-line-args`, `file-io`, `directory`, `panic-stack-trace`, `alloc-tracking`, `codegen-internals`, `managed-memory-element-size`, `stdlib-basic`, `stdlib-autodiscovery`, `grapheme-clusters`, `slice-memory`, `array-managed-elements`, `array-of-bytearray`, `challenge-array-of-structs`, `challenge-struct-lifetime`, `register-allocator`, `unused-variables`, `unused-parameters`, `discarded-results`, `duplicate-functions`, `duplicate-block-identifiers`, `unknown-keyword-error`, `type-checking`, `function-overloads`, `method-calls`, `method-call-on-parameter`, `init-from-literal`, `initablefromarrayliteral`, `parsable-interface`, `ranged-typealias`, `union-hashable`, `advent`

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
- New files in `Compiler/MLIR/Passes/`

---

## Verification

After each phase:
1. Update the spec whitelist in `Testing/SpecTestRunner.maxon`
2. Build the self-hosted compiler: `maxon build maxon-selfhosted`
3. Run spec tests for all three targets:
   - `./maxon-selfhosted/maxon-selfhosted.exe spec-test` (x86_64-windows, runs natively)
   - `./maxon-selfhosted/maxon-selfhosted.exe spec-test --target=x86_64-linux` (runs via Docker)
   - `./maxon-selfhosted/maxon-selfhosted.exe spec-test --target=aarch64-linux` (runs via Docker)
4. Verify all whitelisted tests pass on all targets
5. Cross-check against C# compiler: `./maxon-sharp/bin/Debug/net8.0/win-x64/maxon.exe spec-test` to ensure behavioral parity
