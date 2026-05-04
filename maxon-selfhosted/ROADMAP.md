# Self-Hosted Compiler Roadmap

The self-hosted Maxon compiler (`maxon-selfhosted/`) currently has **43 specs whitelisted** in [`Testing/SpecTestRunner.maxon`](Testing/SpecTestRunner.maxon), with **452 fragment tests passing** on x64-windows. The pipeline is fully built out: lexer → parser → Maxon dialect → Std dialect → MIR (SSA) → Target dialect → code emitter → PE/ELF/Mach-O/Wasm writers, with SSA register allocation, a real optimization pass suite, and (as of Phase 6) a Go-style three-tier slab allocator with refcount-aware managed memory.

Each phase brings X64 + ARM64 backends and PE + ELF output formats to parity together. WASM and Mach-O writers exist but are not the primary correctness target. All targets (`x64-windows`, `arm64-windows`, `x64-linux`, `arm64-linux`) are kept in lockstep within each phase.

## Progress

```
Phase 1:   Core Arithmetic        [x] arithmetic, comparison, unary, parentheses, expressions
Phase 2:   Control Flow           [x] if/else, while, break/continue (with for-range step block), return, match (statement+expression), for-range over integers, for-range over ASCII char literals
Phase 3:   Function Params        [x] parameters, parameter-labels, assignment, method calls
Phase 4:   Basic Types            [~] byte/float/type-casting work; implicit-type-conversion edge cases pending
Phase 5:   Structs                [~] struct literals, methods, self, type/static methods passing;
                                      challenge-* and field-assign edge cases still pending
Phase 6:   Managed Memory         [~] slab allocator + __ManagedMemory builtin done; arrays/for-in deferred to Phase 7+11
Phase 7:   Strings                [ ] real String type — still using bootstrap printLiteral/printInt shims
Phase 8:   Error Handling         [~] throws/throw/try parsed; otherwise EXPR / 'label' block / (e) 'label' binding /
                                      ignore / return / panic / throw / break / continue all work; E3059
                                      type-mismatch checked at parse + TypeResolution time
Phase 9:   Enums                  [~] enum decls (int/float-backed), match (statement+expression),
                                      methods on enums, keyword-as-case-name; associated values pending
Phase 10:  Closures               [~] closure-self landed (function types in params, closure expressions,
                                      env capture for `self`, indirect calls on x64/arm64/wasm);
                                      general closure-capture (arbitrary outer locals) still pending
Phase 11:  Interfaces & Generics  [ ] hybrid model — see sub-phases below
Phase 12:  Global Variables       [~] top-level let / var (int / float / bool / enum / `as`-cast /
                                      let-ref initializers), static var/let, float-typed globals
                                      via XMM load/store; struct runtime initializers possible
                                      now (slab allocator landed in Phase 6); Array literal
                                      globals still need Phases 7+8+11
Phase 13:  Collections            [ ] Map, Set, Vector
Phase 14:  Math Functions         [ ] sqrt, trig, log, exp, pow, etc.
Phase 15:  Advanced Features      [~] semantic checks (unused-parameters, duplicate-blocks,
                                      unknown-keyword, missing-return, panic) passing;
                                      tuples/namespaces/file-io/command-line-args still pending
Phase 16:  Optimization Passes    [~] DCE, CSE, LICM, Mem2Reg, canonicalize, dead-function-elimination,
                                      store forwarding all implemented; tuning + coverage remain
```

Legend: `[x]` complete, `[~]` partially done, `[ ]` not started.

---

## Current Capabilities

### Front end
- **Lexer** ([`Lexer.maxon`](Compiler/Lexer.maxon), 1142 lines): full DFA tokenizer including hex/binary/octal/underscore/scientific literals, all operator tokens (`/` `%` `&` `|` `^` `<<` `>>` `~` `!` `&&` `||`), keywords (`true`/`false`/`while`/`break`/`continue`/`match`/`for`/`in`/`interface`/`extension`/`extends`/`implements`/`with`/`where`/`from`/`uses`).
- **Parser** ([`Parser.maxon`](Compiler/Parser.maxon), 5851 lines): function declarations with parameters and parameter labels, `var`/`let`, type annotations, `return`, `print()`, full `if`/`else if`/`else`, `while`/`break`/`continue`, `for VAR in N to/upto M 'label'` (integer or single-byte char-literal bounds; dedicated step block so `continue` advances the iter), `match` (statement and expression), `try ... otherwise ...` in seven dispatch shapes including labeled-block and binding-block forms, `throws` clause and `throw EnumName.case`, variable reassignment, block scoping, integer/float/bool/single-byte char literals, full operator precedence with all arithmetic/bitwise/comparison/logical/unary operators, parenthesized expressions, function calls with arguments, string interpolation in print, type casting (`int(x)`, `float(x)`, `byte(x)`), struct literals + field access, instance/static/type methods, `self`, `enum` declarations with int and float raw values, function-type annotations on parameters and closure literals (`(x T) gives <expr>`) with `self`-capture support.

### Mid end
- **Maxon Dialect** ([`Compiler/IR/Maxon/`](Compiler/IR/Maxon/)): MaxonDialect, MaxonPrinter, scope tracking, source ranges, dead-function-elimination, `LowerMaxonToStd`.
- **Std Dialect** ([`Compiler/IR/Std/`](Compiler/IR/Std/), 16 files): StdDialect/Module/Printer plus passes — `BorrowCheck`, `Canonicalize`, `CfgAnalysis`, `CommonSubexpressionElimination`, `DeadCodeElimination`, `InjectDrops`, `InsertRangeChecks`, `InstrumentCoverage`, `LoopInvariantCodeMotion`, `LowerABI`, `LowerStdToMir`, `Mem2Reg`.
- **MIR** ([`Compiler/IR/MIR/`](Compiler/IR/MIR/)): SSA-form MIR dialect, printer, copy-coalescing pass.
- **Type resolution** ([`TypeResolution.maxon`](Compiler/TypeResolution.maxon)) — name lookup, cast / compare validation between Maxon and Std lowering, plus `validateTryOtherwiseTypes` for the post-typealias-resolution E3059 check on `__try_*_result` slots.
- **Query system** ([`Queries.maxon`](Compiler/Queries.maxon), [`QueryDatabase.maxon`](Compiler/QueryDatabase.maxon), [`QueryEngine.maxon`](Compiler/QueryEngine.maxon)) — query-based incremental pipeline.

### Back end
- **Shared regalloc infrastructure** ([`Compiler/Targets/Shared/`](Compiler/Targets/Shared/), 18 files): SSA-based register allocator (904 lines), liveness analysis, live-range builder, SSA coloring, spill-code insertion, copy resolution, instruction scheduler, prologue/epilogue, parallel-copy sequentialization, OS descriptor.
- **X64 backend** ([`Compiler/Targets/X64/`](Compiler/Targets/X64/)): X64Dialect, X64Backend, X64CodeEmitter (via shared scaffolding), X64RegisterAlloc, X64PrologueEpilogue, X64Latency, X64OpQuery, MirToX64Conversion.
- **ARM64 backend** ([`Compiler/Targets/Arm64/`](Compiler/Targets/Arm64/)): full mirror of X64 backend.
- **Object writers**: PE ([`Targets/Windows/PeWriter.maxon`](Compiler/Targets/Windows/PeWriter.maxon)), ELF ([`Targets/Linux/ElfWriter.maxon`](Compiler/Targets/Linux/ElfWriter.maxon)), Mach-O ([`Targets/Macos/MachOWriter.maxon`](Compiler/Targets/Macos/MachOWriter.maxon)), Wasm ([`Targets/Wasm/`](Compiler/Targets/Wasm/)).

### Currently whitelisted specs (43)

`basics`, `print-function`, `variables`, `arithmetic`, `float-type`, `panic`, `range-check-panic`, `assignment`, `comparison-operators`, `expressions`, `function-declaration`, `if-statements`, `literals`, `return-statement`, `unary-negation`, `method-calls`, `static-methods`, `byte-type`, `type-casting`, `lexer-edge-cases`, `unary-operators`, `parentheses`, `missing-return-error`, `unknown-keyword-error`, `expected-expression-error`, `unused-parameters`, `parameter-labels`, `duplicate-block-identifiers`, `method-call-on-parameter`, `type-methods`, `self-keyword`, `contextual-literal-typing`, `implicit-type-conversion`, `namespaces`, `stdlib-basic`, `stdlib-autodiscovery`, `enums-simple`, `match-simple`, `module-keyword`, `lexer-parser-robustness`, `try-otherwise-value-flow`, `closure-self`, `allocator`, `managed-memory-builtin`.

The `allocator` and `managed-memory-builtin` specs are marked `status: selfhosted` in their frontmatter — the C# bootstrap runner skips them via [SpecParser.cs](../maxon-sharp/Testing/SpecParser.cs) since the bootstrap doesn't expose the `__mm_*` intrinsics they exercise.

---

## Phase 1: Core Arithmetic & Expressions — DONE

**Specs unlocked**: `arithmetic`, `comparison-operators`, `expressions`, `literals`, `parentheses`, `unary-operators`, `unary-negation`, `lexer-edge-cases`.

All operators (`+`/`-`/`*`/`/`/`mod`/`and`/`or`/`xor`/`shl`/`shr`/`not`/comparisons), full precedence, `true`/`false`, parens, block scoping. Both X64 and ARM64 backends emit machine code for all corresponding ops.

**Still pending in this area**: `bitwise-operators` spec (commented in whitelist) — sanity-check that it's a parser/test gap rather than a missing op, then enable.

---

## Phase 2: Control Flow & While Loops — MOSTLY DONE

**Specs unlocked**: `if-statements` (full else-if chains), `return-statement`, plus `while`/`break`/`continue` exercised through other passing specs.

Parser entry points exist at [`Parser.maxon:1372`](Compiler/Parser.maxon#L1372) (`parseWhileStatement`), [`Parser.maxon:1442`](Compiler/Parser.maxon#L1442) (`parseBreakStatement`), [`Parser.maxon:1454`](Compiler/Parser.maxon#L1454) (`parseContinueStatement`), [`Parser.maxon:2985`](Compiler/Parser.maxon#L2985) (`parseForStatement`). Lowering uses existing `condBr`/`br`/`label` ops on both backends.

**For-range support**: `for VAR in START to/upto END 'label'` over integer bounds and ASCII byte char literals (`'a' to 'z'`). The loop emits a dedicated step block that increments the iter variable; `continue` jumps to the step block (was previously the header, which spun forever) — see `LoopContext.continueBlockId`. The `ranges` spec now passes 15/22 fragments (the remaining 7 need the `Range` stdlib type with iterator protocol — Phase 11 — and string interpolation on Character — Phase 7).

**Still pending in this area**:
- richer `if-statements` edge cases (commented in whitelist)

---

## Phase 3: Function Parameters & Multiple Functions — DONE

**Specs unlocked**: `function-declaration`, `parameter-labels`, `assignment`, `method-calls`, `method-call-on-parameter`, `static-methods`, `type-methods`, `self-keyword`, `unused-parameters`.

Parser at [`Parser.maxon:181`](Compiler/Parser.maxon#L181) (`parseFunctionParameters`). Calling conventions wired up for Windows (RCX/RDX/R8/R9) and SysV (RDI/RSI/RDX/RCX/R8/R9) on x64; X0–X7 on ARM64. ABI lowering handled by [`LowerABI.maxon`](Compiler/IR/Std/LowerABI.maxon).

---

## Phase 4: Basic Types — IN PROGRESS

**Specs passing**: `byte-type`, `float-type`, `type-casting`, `contextual-literal-typing`.

**Currently failing**: `implicit-type-conversion` — 13 fragment-level failures spanning int↔float/byte parameter coercion, math intrinsic int promotion, and three "should-error" cases (`no-string-to-int`, `no-bool-to-int`, `no-int-to-bool`) that are accepting bad code or producing the wrong diagnostic. One representative error:

```
error E3013: lowerMaxonToStd:0:0: lowerMaxonToStd: unresolved value name '$t1'
```

This is the first concrete blocker before Phase 6 work can begin.

**Action**: fix the implicit-conversion paths in [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon) (likely missing implicit-cast insertion in argument passing) and tighten the type-checker to reject the bool↔int and string→int cases.

---

## Phase 5: Structs — MOSTLY DONE

**Specs passing**: `method-calls`, `method-call-on-parameter`, `self-keyword`, `static-methods`, `type-methods`.

Parser at [`Parser.maxon:2439`](Compiler/Parser.maxon#L2439) (`parseStructLiteral`), [`Parser.maxon:1056`](Compiler/Parser.maxon#L1056) (`parseTypeDecl`). Heap allocation via OS calls is wired through `OsDescriptor` and `BackendDispatch`.

**Still pending in this area**:
- `challenge-struct-field-assign` (commented in whitelist) — direct field assignment edge cases
- `challenge-nested-structs` — nested struct ownership
- `challenge-struct-ownership`, `challenge-struct-lifetime`, `challenge-array-of-structs` — interact with full ownership/borrow checking
- `module-level-struct-var` — depends on Phase 12

---

## Phase 6: Managed Memory — MOSTLY DONE

**Original goal**: `__ManagedMemory` builtins, `Array<T>` operations, `for-in` over arrays.

**Actual outcome**: the **allocator + `__ManagedMemory` builtin** landed; **user-facing `Array<T>` / `for-in` / array literals** are deferred to land on top of Phases 7 (Strings), 8 (full `throws`/`throw`), and 11 (interfaces & generics) since [`stdlib/Array.maxon`](../stdlib/Array.maxon) depends on all three. The `__ManagedMemory` primitive done here is the building block those phases will compose on top of.

### What landed (sub-phases 6a + 6b)

**Sub-phase 6a — port the C# slab allocator (23 steps).** Reproduces the bootstrap's Go-style three-tier slab allocator + MM raw + MM tracked layers ([RuntimeEmitter.Allocator.cs](../maxon-sharp/Compiler/MLIR/Runtime/RuntimeEmitter.Allocator.cs), [RuntimeEmitter.MemoryManager.cs](../maxon-sharp/Compiler/MLIR/Runtime/RuntimeEmitter.MemoryManager.cs)) inside self-hosted's `runtime.std`.

- **9 new ops added to [`StdSystemOp`](Compiler/IR/Std/StdDialect.maxon) + [`MirOp`](Compiler/IR/MIR/MirDialect.maxon)**: `osAllocPages` / `osAllocPagesLarge` / `osFreePages`, `atomicInc` / `atomicDec` / `atomicXadd`, `osLockInit` / `osLockAcquire` / `osLockRelease`. Full lowering on X64 (`LOCK INC`/`LOCK DEC`/`LOCK XADD` via [`X64Backend.maxon`](Compiler/Targets/X64/X64Backend.maxon); `VirtualAlloc`/`VirtualFree`/`Critical*Section` IAT calls) and ARM64 (LDAXR/STLXR LL/SC sequences via [`Arm64CodeEmitter.maxon`](Compiler/Targets/Arm64/Arm64CodeEmitter.maxon); `mmap`/`munmap` syscalls; locks elided to no-ops since self-hosted compilation is single-threaded).
- **5 new IAT slots in [PeWriter.maxon](Compiler/Targets/Windows/PeWriter.maxon)**: `VirtualAlloc`, `VirtualFree`, `InitializeCriticalSection`, `EnterCriticalSection`, `LeaveCriticalSection`.
- **~30 functions ported to [`runtime.std`](Compiler/Runtime/runtime.std)**: bitmap chunk allocator (`__slab_arena_init`, `__slab_arena_alloc_chunks`, `__slab_arena_free_chunks`), mspan / mcentral / mcache fast path (`__slab_alloc_class`, `__slab_refill_mcache`, `__slab_meta_alloc`/`free`), two-level radix arena map (`__slab_arena_map_set`/`get`/`ensure`), OS-direct path for >32KB (`__slab_os_direct_alloc`/`free`), public `__slab_alloc(size)` / `__slab_free(ptr)`, refcounted MM tracked layer (`mm_alloc` with destructor + tag header, `mm_incref`/`mm_decref` via atomic-xadd, `mm_free`/`mm_realloc`), MM raw layer (`mm_raw_alloc`/`free`/`realloc`).
- **`mrt_start` boots `__slab_init`** before `main()` runs; `mrt_alloc` body replaced with a one-line `mm_raw_alloc` call so all existing struct/closure allocations transparently switch to the new path.
- **14 mutable globals + 2 read-only size-class tables** registered in [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon) via `registerSlabAndMmGlobalsForDataSection`.
- **Test intrinsics** `__mm_raw_alloc` / `__mm_alloc` / `__mm_decref` / `__mm_incref` / `__mm_alloc_count` / `__mm_raw_alloc_count` registered as user-callable via `registerMmIntrinsicSignatures` so spec tests can exercise the allocator directly.
- **27 fragment tests in [`specs/allocator.md`](../specs/allocator.md)** (status: selfhosted) covering basic alloc, multi-size workloads, free + reuse, OS-direct path for 100MB allocations, refcount semantics, all 18 size-class boundaries, arena fill stress, interleaved alloc/free, and mixed raw/tracked allocation. **27/27 passing.**

**Sub-phase 6b — `__ManagedMemory` builtin (8 steps).** Builds the compiler-builtin `__ManagedMemory` struct that all higher-level collections (Array, Vector, Map, String) will eventually wrap.

- **`__ManagedMemory` registered as a 5-field struct + 8-case `__ManagedMemoryError` enum** at compiler startup in `LowerMaxonToStd.maxon` via `registerManagedMemoryType`. Layout exactly matches the C# bootstrap's `[buffer @ 0, length @ 8, capacity @ 16, element_size @ 24, parent_ptr @ 32]` (40 bytes).
- **18 builtin method signatures registered** (`length`/`capacity`/`elementSize`/`clear`/`setLength`/`get`/`set`/`remove`/`grow`/`shiftRight`/`shiftLeft`/`byteAt`/`setByte`/`append`/`slice`/`toCString`/`makeCharFromBytes` plus static `create`).
- **`__destruct___ManagedMemory(self)` runtime function** branches on `parent_ptr` to free the buffer (root: `parent_ptr == -1`), skip (rdata-backed: `parent_ptr == -2`), or `mm_decref` the parent (slice view).
- **`lowerManagedMemBuiltin`** intercepts `__ManagedMemory.*` method calls in `lowerMethodCall` and dispatches to **12 `__managed_mem_*` runtime helpers** (one per throwing method) that perform bounds checks + emit `mrt_set_error(ordinal+1)` on failure. The Phase-4 `try ... otherwise default` machinery picks up the error flag automatically.
- **Parser support for `typealias NAME = __ManagedMemory with TYPE`** in [`Parser.maxon`](Compiler/Parser.maxon) so test code can write `typealias IntMem = __ManagedMemory with Int`.
- **12 fragment tests in [`specs/managed-memory-builtin.md`](../specs/managed-memory-builtin.md)** (status: selfhosted) cover `create`, `setLength`/`get`/`set` round-trip, `grow`, `append`, `slice`, byte-granular access, `clear`, all three out-of-bounds error paths, and the destructor freeing through `mm_decref`. **12/12 passing.**

### Real bugs surfaced and fixed during convergence

The full pipeline running on the new allocator surfaced five latent bugs that had never been exercised before:
1. `__slab_*` and `__mm_*` globals were being routed to read-only `.rdata` instead of writable `.data`, causing access violations on the first atomic increment of `__mm_raw_alloc_count`. Fixed in [`X64Backend.maxon`](Compiler/Targets/X64/X64Backend.maxon)'s `relocKindForLabel` and the equivalent ARM64 path.
2. X64 shift lowering could clobber its own count register (RCX) because the regalloc didn't see the implicit physical-register def. Fixed in [`X64RegisterAlloc.maxon`](Compiler/Targets/X64/X64RegisterAlloc.maxon)'s `getImplicitGprDefs`.
3. `__slab_alloc_class`'s retry loop kept a value live across `__slab_refill_mcache` in a caller-saved register; restructured to recompute after the call.
4. The instruction scheduler created a self-edge dependency on `osFreePages` because it was classified as both a store and a call. Fixed in [`InstructionScheduler.maxon`](Compiler/Targets/Shared/InstructionScheduler.maxon)'s `buildDependencies`.
5. `mrt_set_error(0)` collided with the "no error in flight" sentinel — every `__managed_mem_*` helper now sends `ordinal+1` over the wire (matching the existing `lowerThrow` convention).

Plus two non-allocator infra fixes: [`PeWriter.maxon`](Compiler/Targets/Windows/PeWriter.maxon) `dataRawSize` now grows to align the larger prelude; [`StdlibLoader.maxon`](Compiler/StdlibLoader.maxon)'s cache-stripping filter was replaced with a runtime-module-list query (added `isRuntimeFunctionName` helper in [`BackendDispatch.maxon`](Compiler/Targets/BackendDispatch.maxon)) so new runtime functions don't need a manual prefix-list update.

### What's deferred and why

The user-facing `arrays` / `stdlib-array` / `collection` / `for-in over arrays` / `array-managed-elements` / `array-of-bytearray` / `array-hashable` specs all depend on [`stdlib/Array.maxon`](../stdlib/Array.maxon), which presupposes:
- **Phase 7 (real Strings)** for `panic("...")` interpolated messages inside `Array.ensureCapacity` etc.
- **Phase 8 (full throws/throw)** for `Array.get(i) throws ArrayError`, `try managed.get(...) otherwise throw ArrayError.indexOutOfBounds`, etc.
- **Phase 11 (interfaces & generics)** for `Array uses Element`, `implements BuiltinArrayLiteral, Iterable with(Element, ArrayIter), Cloneable`, `extension Array where Element is Equatable`.

Once those land, parsing `stdlib/Array.maxon` becomes possible and the deferred specs unlock automatically — the `__ManagedMemory` primitive completed in Phase 6 is the building block they all wrap.

---

## Phase 7: String Type & Interpolation (~6 specs)

**Goal**: real `String` type with methods, real `print()` from stdlib. **This phase removes the temporary `print()`/`printLiteral`/`printInt` ops** still in the bootstrap.

### Specs to unlock
`string-type`, `string-interpolation`, `character-type`, `byte-string-literal`, `primitive-stringable`, `strings`.

### Changes

**Maxon Dialect**: add `stringLiteral`, `stringInterp`, `charLiteral`, `byteStringLiteral`. Remove `printLiteral`, `printInt`.

**Std Dialect & both backend dialects/emitters**: remove `printLiteral`, `printInt` and their hardcoded OS write stubs. The real `print()` will go through stdlib → String → managed memory → OS write call, reusing the `callRuntime`/`callImport` infrastructure from Phases 5–6.

**Lowering**: lower string literals to rdata + managed memory wrapping; lower string interpolation to `.toString()` + concat sequences; lower `print()` as a regular function call to stdlib `print`.

**Parser**: remove the hardcoded `parsePrintStatement` and `parseStringInterpolation` special cases. Parse string methods (`.count()`, `.isEmpty()`, `.contains()`, `.slice()`, `.findFirst()`), char literals `'x'`, byte string literals `b"..."`.

**Note**: this is a breaking transition — every currently passing spec (`basics`, `print-function`, `variables`, etc.) must continue passing after the switchover.

### Files to modify
- All pipeline files (both backends), [`StdlibLoader.maxon`](Compiler/StdlibLoader.maxon).

---

## Phase 8: Error Handling — MOSTLY DONE

**Goal**: `try`/`throw`/`otherwise`, `throws` clause, error propagation.

### Done
- **Parser**: `throws ErrorType` in signatures, `throw EnumName.case` statements, `try CALL` propagation form, `try CALL otherwise FALLBACK` with seven dispatch shapes — single expression, `ignore`, `return`, `panic`, `throw`, `break`, `continue`, plus the labeled-block forms `'label' STMTS end 'label'` (plain) and `(e) 'label' STMTS end 'label'` (binding form that exposes the error enum value to the handler body) — all in [`Parser.maxon:3765`](Compiler/Parser.maxon#L3765) (`parseTryExpression`) → [`parseTryFallbackDispatch`](Compiler/Parser.maxon#L3951) → [`finalizeFallbackHandler`](Compiler/Parser.maxon#L4078).
- **Maxon Dialect**: `tryCall` op (rewritten in place from `call` by [`tryRewriteCallToTryCall`](Compiler/Parser.maxon#L4091)), `throw` op (terminator: emits `mrt_set_error(ordinal+1)` + default ret).
- **Std Dialect / runtime**: `mrt_set_error(flag)` and `mrt_get_error_and_clear()` runtime helpers carry the error flag across the function-return ABI; lowering at [`LowerMaxonToStd.maxon:1975`](Compiler/IR/Maxon/LowerMaxonToStd.maxon#L1975) (`lowerThrow`) and [`LowerMaxonToStd.maxon:2024`](Compiler/IR/Maxon/LowerMaxonToStd.maxon#L2024) (`lowerTryCall`).
- **Type checking**: E3059 type-mismatch between the call's success type and the `otherwise` fallback type runs in two places — at parse time when both types are concrete primitives (parseTryFallbackDispatch's category check, gated by `maxonTypeIsKnown`), and again at TypeResolution time via [`validateTryOtherwiseTypes`](Compiler/TypeResolution.maxon) which catches the typealias-named-call case (e.g. `try mayFail() otherwise 5.0` where `mayFail returns Integer`) that the parse-time check must skip because typealiases panic in `convertCastCategory` pre-resolution.
- **`error-handling` spec**: 29/33 fragments now pass — all `try`/`otherwise` shapes, error-flag propagation, type-mismatch diagnostics, throwing-without-`try` rejection, and `try` against a non-throwing callee rejection.

### Still pending
- 3 `error-handling` fragments need Phase 9's union types for associated-value error enums (`assoc-value-throw-catch{,-2}`, `otherwise-block-reused-binding`).
- 1 `error-handling` fragment needs Phase 7 (`otherwise-return-string`).
- `if-try` spec not yet whitelisted; sanity-check it parses and runs through the existing infrastructure.
- The spec is **not** yet on the whitelist because per-fragment skip isn't implemented; whitelisting requires landing the four stragglers above.

---

## Phase 9: Enums & Match (~8 specs)

**Goal**: enum declarations (simple and with associated values), pattern matching with case extraction.

### Done
- `enums-simple` (14 fragments): enum declarations with int and float raw values, methods on enums, `match` as both statement and expression, `gives` and `then` arms, keyword-token case names (`function`, `return`, `end`, `if`, …), error-path diagnostics E3030 / E3031 / E3032 / E3034.
- `match-simple` (31 fragments): integer-literal patterns, `or`-chained patterns, `default` arms, `and fallthrough` chains, statement and expression forms, full diagnostic coverage — E2025 (`fallthrough`+`return`), E2026 (non-exhaustive enum match), E2027 (duplicate pattern), E2029 (default-not-last), E2042 (missing block id), E2043 (block-id mismatch), E2046 (default-on-enum-without-throws/panic).
- Pipeline integration: enum cases registered as top-level constants so `EnumName.case` flows through the existing `unresolvedRead → literal` rewrite. The enum itself is registered as a typealias to int (or f64 for float-backed) so parameters / return types / locals lower without per-call special cases.

### Still pending
`enum-full`, `enum-match-only`, `match-statements`, `match-enum-typed-binding`, `enum-struct-field-match`, `enum-hashable` — associated values, range patterns (`1 to 5 then …`).

### Changes still required for full phase
**Parser**: `match val 'l' ... Case(x) then ... end 'l'` (case-binding form), associated-value enum construction (`EnumType.caseName(value)`), `to`/`upto` range patterns on integer scrutinees.

**Maxon Dialect**: `enumConstruct`, `enumTag`, `enumPayload`, `enumRawValue`, `enumName` (only needed when associated values land — simple enums lower to plain integer literals through the existing `literal` op).

**Memory model**: tag (i64) + max-payload-size buffer for unions. Today's int-backed enums need no payload.

---

## Phase 10: Closures & First-Class Functions (~5 specs) — IN PROGRESS

**Goal**: function pointers, closures with captured variables, indirect calls.

### Done — `closure-self` (3 fragments, x64-windows + wasm32-wasi parity)
- **Parser**: function-type annotations on parameters via `parseTypeRef` (`f (Integer) returns Integer`); closure literals (`(x T) gives <expr>`, `() gives <expr>`); per-project `__closure_<N>` lifting; capture-by-reference detection that rewrites `self` reads inside a closure body to `closureEnvLoad`.
- **MaxonDialect**: `MaxonType.function(id)` arm interned via `Project.functionTypes`; new ops `functionRef`, `closureCreate`, `closureEnvLoad`, `indirectCall`.
- **Lowering**: function-typed param expansion to a (fn-pointer, env-pointer) ABI pair; `closureCreate` materializes a heap env via `mrt_alloc(N*8)` with stackAddr-based by-reference captures; `indirectCall` forwards the matching env to the callee.
- **Mem2Reg**: address-taken slot tracking — captured params keep their stack slot live and emit a param-store at function entry so the captured pointer dereferences see the calling-convention value.
- **Wasm**: function-table + element-section emission, `funcAddr` returns table index, `indirectCall` lowers to `call_indirect (type $T) (table 0)`, type-section dedup interns by signature so direct and indirect calls share entries. Second mem2reg run after `augmentWithRuntime` keeps slot space clean.
- **DFE**: `functionRef` and `closureCreate` root the lifted body so it survives dead-function elimination.

### Specs to unlock (still pending)
`first-class-functions`, `closure-capture`, `closures` — require general by-reference capture of arbitrary outer locals (not just `self`), plus optional refcount-aware env destruction.

---

## Phase 11: Interfaces & Generics — Hybrid Model (~15 specs)

**Strategic positioning**: this phase is the technical realization of Maxon's positioning as **"Rust-class safety, Swift-class compile times."** It is the most consequential design decision in the project.

**Goal**: interface declarations, conformance, generic functions and types — using a **hybrid** strategy:

- **Layouts are monomorphized** — `Array<Int>` stores i64s inline, `Array<String>` stores pointers; size/alignment/inline-vs-pointer is per-instantiation
- **Methods are NOT monomorphized** — generic method bodies are compiled once and dispatched through layout descriptors and witness tables (Swift-style)
- **Aggressive inlining at static call sites** recovers monomorphized-quality code on hot paths

**Why hybrid instead of full monomorphization** (which the C# bootstrap uses):

1. **Compile speed** — one body per generic method instead of N per (method × type). Eliminates the multiplicative downstream cost (lowering, codegen, register allocation all do less work).
2. **Real per-function incremental compilation** — caller IR references stable callee names (`Array.push`) instead of specialized ones (`__Array_Int.push`), so a change to a generic body doesn't invalidate caller caches.
3. **Smaller binaries** — typically 2–3× smaller code section, better icache behavior.
4. **Dynamic dispatch as a first-class language feature** — heterogeneous collections of trait objects, plugin systems, hot-reload all become tractable.
5. **Cleaner errors and IDE experience** — diagnostics reference the source generic, not a mangled specialization.
6. **Preserved runtime perf** — `@inlinable` on hot stdlib paths recovers monomorphized output at static call sites.

**Why now**: this is the cheapest moment in the project to commit to this design. After Phase 11 ships with a different model, retrofitting witness tables means tearing apart the type system, the dispatch story, every cached MIR, and every emitted symbol.

**Reference**: see [`docs/hybrid-generics-plan.md`](../docs/hybrid-generics-plan.md) for the full design document.

### Specs to unlock
`interfaces`, `interface-conformance`, `interface-extensions`, `equatable`, `primitive-comparable`, `primitive-cloneable`, `primitive-hashable`, `where-clauses`, `conditional-extensions`, `associated-types`, `instance-methods`, `pair`, `parsable-interface`, `ranged-typealias`.

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
- **Lexer** ([`Lexer.maxon`](Compiler/Lexer.maxon)): keywords `interface`, `extension`, `extends`, `implements`, `with`, `where`, `from`, `uses` are already tokenized — verify precedence and contextual handling.
- **Parser** ([`Parser.maxon`](Compiler/Parser.maxon)): add productions for `interface Name uses T1, T2 ... end`, `type Name uses T1 implements I1, I2 with(...) ... end`, `where T: Comparable`, `function foo<T>(x T) returns T where T: Comparable`, `extension Iterable uses Element ... end`, `from Type implements Interface ... end`.
- **MaxonDialect**: extend `MaxonType` with `typeParameter(id TypeNameId)` (unresolved `T`) and `genericInstance(baseId TypeNameId, args MaxonTypeArray)` (`Array with Int`).
- **Project** ([`Project.maxon`](Compiler/Project.maxon)): add `interfaces InterfaceMap`, `conformances ConformanceMap`, `typeParameters TypeParamMap`.

**Reuse**: the C# bootstrap parses all this syntax — grammar is solved.

**Tests**: parser-only tests verifying AST shape; spec tests under `specs/interfaces/`, `specs/generics/` start parsing successfully (still fail at lower stages).

### Phase 11.1 — Type system: substitution + conformance

**Goal**: type system represents and reasons about generics. Type checking distinguishes instantiated from uninstantiated types.

**New types** (`InterfaceType`, `GenericTypeDecl`, `TypeSubstitution`) and new files (`Compiler/TypeSystem/Substitution.maxon`, `Compiler/TypeSystem/Constraints.maxon`).

**Algorithms**: pure structural `substituteType`, conformance lookup `typeConformsTo`, where-clause satisfaction. Constraint checking happens at **call sites**, not at generic bodies — critical for incremental.

**Critical decision**: we are **not** specializing types per call-site arg type the way the C# bootstrap does. The witness table makes this work without specialization.

### Phase 11.2 — Layout descriptors

**Goal**: every concrete instantiation of a generic type gets a layout descriptor, emitted into rdata.

**New IR concept** `LayoutDescriptor` (size, alignment, elementSize, fieldOffsets, copyFunc, destroyFunc, hasHeapRefs).

**New pass**: `Compiler/Passes/BuildLayoutDescriptors.maxon` — walks `genericInstance` types, computes layout, emits descriptors with stable labels (`__layout_Array_Int`).

**Reuse**: existing `GlobalDataTable` rdata-emission infrastructure.

### Phase 11.3 — Witness tables for interfaces

**Goal**: every `(type, interface)` conformance gets a witness table in data; interface-typed values are fat pointers `{value, witness}`.

**Layout**: one witness table per `(type, interface)`; method order fixed per interface declaration. Value types ≤ 8 bytes inline; larger types boxed.

**New pass**: `Compiler/Passes/BuildWitnessTables.maxon`.

**Critical decision**: single witness table per `(type, interface)` (Swift-style), not embedded-in-value (Java/Go-style).

### Phase 11.4 — Generic body lowering [PIVOTAL]

**Goal**: generic method bodies lowered **once** with implicit layout/witness parameters. This is where the project shape diverges most from the C# bootstrap.

**Changes to [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon)**:
- Generic methods get implicit `(layout: ptr, witness?: ptr)` params
- `sizeof(T)` → load `[layout + 0]`; `alignof(T)` → load `[layout + 8]`; `copy/destroy` via descriptor function pointers
- Call sites resolve the layout label statically and pass it as the implicit argument

**New ops in [`StdDialect.maxon`](Compiler/IR/Std/StdDialect.maxon)**: `loadLayoutDescriptor`, `loadWitnessTable`, `descriptorField`, `witnessMethod` — all sugar over `loadIndirect`/`funcRef`.

**ABI lowering** ([`LowerABI.maxon`](Compiler/IR/Std/LowerABI.maxon)): implicit layout/witness params are register-class; existing classifier handles them.

**The pivotal property**: after this phase, `Array.push` is **one** function in the emitted module, named `Array.push` (not `Array_Int.push`). **This is what unlocks per-function incremental compilation in 11.5.**

**Risks**: refcount insertion (descriptor's `destroy` handles per-element refcount), generic constructors (descriptor passed in). Mitigation: parallel snapshot test harness for `Array<Int>`, `Array<String>`, `sort` with `Comparable`.

### Phase 11.5 — Per-function incremental queries

**Goal**: query system caches MIR and emitted code at function granularity. One-function rebuild drops from ~3s to ~50–200ms.

**Only possible because Phase 11.4 made callee names stable.**

**New queries in [`Queries.maxon`](Compiler/Queries.maxon)**: `queryMidForFunction`, `queryMirForFunction`, `queryCodeForFunction`.

**Refactor**: [`PassPipeline.maxon`](Compiler/IR/PassPipeline.maxon) gains `runForFunction(funcName)` paths; whole-module passes (DFE, layout descriptor build) stay at `queryAllMid` granularity.

**Cache invalidation**: function body change ⇒ only that function's MIR/code; signature change ⇒ callers recomputed; non-callers untouched.

### Phase 11.6 — Inliner + `@inlinable` annotations

**Goal**: aggressive inlining at static call sites recovers monomorphized-quality code. **Drives runtime perf cost of the hybrid model toward zero on typical code.**

**New pass**: `Compiler/Passes/Inliner.maxon` (operates on Std-level IR before MIR).

**Heuristics**: always inline `@inlinable` / single-call-site / <20 ops; size-balanced for medium; never for recursive / `@noinline` / truly dynamic.

**New annotations**: `@inlinable`, `@noinline`, `@alwaysInline`.

**Stdlib annotation pass**: ~20–30 hot stdlib methods (`Array.push`, `Array.get`, `Array.length`, iterators, `Optional.unwrap`).

**Constant folding extensions**: extend the existing canonicalize pass to recognize loads from known descriptor addresses → constants.

### Phase 11.7 — Validation, polish, parity

**Performance targets**:
- Compile time of stdlib + selfhosted source: **≤ 60% of current C# bootstrap** (~3s vs current 5s)
- Incremental rebuild of one-function edit: **≤ 200ms**
- Runtime: within **10%** of C# bootstrap output on representative benchmarks; within **2%** on stdlib hot loops with inlining
- Binary size: **≤ 50%** of C# bootstrap output

**Validation**: full spec-test parity with C# bootstrap; self-host (self-hosted compiler compiles itself in ≤ 3s).

### Files to modify (summary)

| Sub-phase | File / area |
|---|---|
| 11.0 | [`Lexer.maxon`](Compiler/Lexer.maxon), [`Parser.maxon`](Compiler/Parser.maxon), [`MaxonDialect.maxon`](Compiler/IR/Maxon/MaxonDialect.maxon), [`Project.maxon`](Compiler/Project.maxon) |
| 11.1 | New: `Compiler/TypeSystem/Substitution.maxon`, `Compiler/TypeSystem/Constraints.maxon` |
| 11.2 | New: `Compiler/Passes/BuildLayoutDescriptors.maxon`; [`Project.maxon`](Compiler/Project.maxon) (`LayoutDescriptorTable`) |
| 11.3 | New: `Compiler/Passes/BuildWitnessTables.maxon` |
| 11.4 | [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon), [`StdDialect.maxon`](Compiler/IR/Std/StdDialect.maxon) |
| 11.5 | [`Queries.maxon`](Compiler/Queries.maxon), [`PassPipeline.maxon`](Compiler/IR/PassPipeline.maxon), [`IncrementalTestRunner.maxon`](Testing/IncrementalTestRunner.maxon) |
| 11.6 | New: `Compiler/Passes/Inliner.maxon`; stdlib annotations |
| 11.7 | All pipeline files (perf tuning); architecture docs |

### Out of scope for this phase

Cross-module / link-time inlining; profile-guided optimization; `@specialize` attribute; type erasure for code size; backporting to the C# bootstrap.

---

## Phase 12: Global Variables & Static State (~4 specs) — IN PROGRESS

**Goal**: module-level variables, static fields/methods on types.

### Specs to unlock
`static-variables` (25/28 passing), `top-level-let` (full), `module-level-struct-var`, `export-var-fields`.

### Done
- Parser: module-level `var`/`let` declarations and `static var/let` fields with structural `UnresolvedConstExpr` initializers (deferred resolution after enums/typealiases drain).
- Const-expr forms supported: int / float / bool literals, `EnumName.caseName`, `<expr> as TypeName` ranged-cast, identifier references to `let` constants, full arithmetic / comparison / logical fold.
- TypeResolution: `resolveAllTopLevelDecls` drains `unresolvedTopLevelDecls` into `topLevelConstants` (lets) or `topLevelVars` (vars) based on each entry's `mutable` flag.
- Diagnostics: E2045 for runtime call initializers; E2013 for assignment to immutable `let` (top-level or static); `let X = TypeName.method(...)` is tolerated (no init emitted yet) so the reassignment path can still surface E2013.
- Std Dialect: `globalLoad` / `globalStore` carry `slotType`. The `isFloat` flag is plumbed through `StdSystemOp.loadIndirect`/`storeIndirect` → `MirOp.load`/`store` → X64 (`loadIndirectXmm` / `storeIndirectXmm` via new `emitLoadIndirectXmmOp` / `emitStoreIndirectXmmOp`) → ARM64 (`fpLoadIndirect` / `fpStoreIndirect`) → WASM (`opF64Load` / `opF64Store`), so float-typed globals and float struct fields land directly in FP registers.
- Visibility: `Visibility` enum (file / module / global); `export` and contextual `module` keywords parsed at `dispatchTopLevel`. Every resolved / unresolved decl entry carries `visibility` + `sourceFilePath`: top-level vars / lets, functions (`IrFunction.visibility`/`sourceFilePath`), struct types, enums, typealiases (sidecar `typealiasVisibilities` map), and enum cases (inherited from enclosing enum). Static fields and inherent-method visibility inherit the enclosing type's tier; enum methods inherit the enum's tier. Cross-file callee resolution falls back through `methodNameIndex` filtered by `isVisibleFrom` against the reading function's file. `qualifyCalleeForContext` takes a `readerFilePath` parameter; per-function rewrite passes derive it from `entryBlockFilePath`. New TR validation passes — `validateCallVisibility` (E3008 / E3088 on hidden `call` ops, both qualified and bare), `validateNamedTypeVisibility` (rejects `as TypeName` against hidden typealias / struct) — sequence after `fillUnresolvedTypes` and before `validateCallArities`. `queryCodeResult` gates codegen behind `hasErrors` so semantic errors short-circuit lowering. Stdlib cache codec stamps `(global, stdlib bootstrap path)` on restore for every function / struct / enum without changing the on-disk format. `module-keyword` (11/11) and `export-keyword` (16/24) specs land on the whitelist; remaining `export-keyword` failures are pre-existing parser gaps (Array typealias parsing) and wording differences (E2001 vs E3061 for duplicate typealiases), all unrelated to visibility.

### Still pending
- **Array literal globals** (`var xs = [1, 2, 3]`): no array-literal value emission yet (Maxon-side `parseArrayLiteralExpr` is still a parse-stub) and no `__module_init` runtime-init function. Phase 6's slab allocator + `__ManagedMemory` are now in place, but the user-facing `Array<T>` wrapper still depends on Phases 7+8+11.
- **Cross-file struct field via top-level var** (`shared.value = 42` from another file): self-hosted parses the dotted access as a single `unresolvedRead` rather than a globalLoad → fieldLoad chain. Needed for the `exported-struct-var-cross-file` spec.
- **Generic `Array with TYPE` typealias parsing**: Phase 6 added narrow support for `typealias NAME = __ManagedMemory with TYPE` only. The general `Array with Integer` form still depends on Phase 11.0; affects four `export-keyword` tests that exercise typealias visibility on `Array`-shaped aliases.

---

## Phase 13: Collections (Map, Set, Vector) (~8 specs)

**Goal**: hash map, hash set, vector.

### Specs to unlock
`map`, `set`, `vector`, `array-hashable`, `map-struct-bytearray`, `map-try-otherwise-block`, `stdlib-set`.

These are stdlib types built on Array + generics + Hashable. Phase 6's `__ManagedMemory` primitive provides the storage layer they ultimately wrap; the remaining blockers are Phase 7 (Strings, for panic messages and the String element type), Phase 8 (full `throws`/`throw` propagation), and Phase 11 (interfaces/generics for `Hashable`/`Equatable`/conditional extensions).

**Stdlib integration**: parse and compile stdlib `.maxon` files as part of the compilation unit (most work happens in [`StdlibLoader.maxon`](Compiler/StdlibLoader.maxon) and [`StdlibCache.maxon`](Compiler/StdlibCache.maxon)).

---

## Phase 14: Math Functions (~18 specs)

**Goal**: `abs`, `sqrt`, `floor`, `ceil`, `round`, `min`, `max`, trig, log, exp, pow.

### Specs to unlock
`abs`, `ceil`, `floor`, `round`, `trunc`, `sqrt`, `pow`, `sin`, `cos`, `tan`, `atan2`, `exp`, `log`, `log2`, `log10`, `min`, `max`.

### Changes

**Maxon Dialect**: `absOp`, `sqrtOp`, `floorOp`, `ceilOp`, `roundOp`, `minOp`, `maxOp`.

**X64**: `sqrtXmm`, `roundXmm`, `minXmm`, `maxXmm`, `andMaskRipRel` (for abs).
**ARM64**: `fsqrtD`, `frintaD`, `frintnD`, `frintmD`, `frintpD`, `fminD`, `fmaxD`, `fabsD`.

**Trig/log/exp**: runtime library calls. Windows imports `ucrtbase.dll`. Linux: link libm or implement soft-float.

---

## Phase 15: Advanced Features & Remaining Specs (~20 specs)

**Specs already passing in this bucket**: `panic`, `range-check-panic`, `unused-parameters`, `duplicate-block-identifiers`, `unknown-keyword-error`, `expected-expression-error`, `missing-return-error`, `stdlib-autodiscovery`.

### Specs still to unlock
`tuples`, `namespaces`, `multi-file`, `export-keyword`, `command-line-args`, `file-io`, `directory`, `panic-stack-trace`, `alloc-tracking`, `codegen-internals`, `managed-memory-element-size`, `stdlib-basic`, `grapheme-clusters`, `slice-memory`, `discarded-results`, `duplicate-functions`, `type-checking`, `function-overloads`, `init-from-literal`, `initablefromarrayliteral`, `register-allocator`, `unused-variables`, `advent`.

### Sub-phases
**15a: Semantic Checks** — extend the type-checker for `discarded-results`, `unused-variables`, `duplicate-functions`, `function-overloads`, full `type-checking` parity.
**15b: Command-line Args** — `CommandLine.arguments()`; PE import for `GetCommandLineW`.
**15c: File I/O** — `File.readText`, `File.writeText`, etc. via OS imports.
**15d: Panic & Stack Traces** — extend existing panic op with frame walking.
**15e: Tuples** — destructuring in `let`/`var`, tuple return types.
**15f: Namespaces & Exports** — module system, `export` keyword, qualified names. The spec runner now supports multi-file fragments (`// --- file: name.maxon` markers): each file becomes a distinct compilation unit via `compileMultiFileSourceWithIr`, so cross-file references resolve like a real on-disk project.
**15g: Function Overloads** — overload resolution by parameter types.
**15h: Method Call Syntax** — `value.method(args)` desugaring.

---

## Phase 16: Optimization Passes — IN PROGRESS

**Already implemented** (in [`Compiler/IR/Std/`](Compiler/IR/Std/) and [`Compiler/IR/Maxon/`](Compiler/IR/Maxon/)):

1. **Dead code elimination** ([`DeadCodeElimination.maxon`](Compiler/IR/Std/DeadCodeElimination.maxon))
2. **Common subexpression elimination** ([`CommonSubexpressionElimination.maxon`](Compiler/IR/Std/CommonSubexpressionElimination.maxon))
3. **Loop-invariant code motion** ([`LoopInvariantCodeMotion.maxon`](Compiler/IR/Std/LoopInvariantCodeMotion.maxon))
4. **Mem2Reg** ([`Mem2Reg.maxon`](Compiler/IR/Std/Mem2Reg.maxon))
5. **Canonicalization** ([`Canonicalize.maxon`](Compiler/IR/Std/Canonicalize.maxon))
6. **Dead function elimination** ([`DeadFunctionElimination.maxon`](Compiler/IR/Maxon/DeadFunctionElimination.maxon))
7. **Copy coalescing** ([`CommuteForCoalescing.maxon`](Compiler/IR/MIR/CommuteForCoalescing.maxon))
8. **Instruction scheduling** ([`InstructionScheduler.maxon`](Compiler/Targets/Shared/InstructionScheduler.maxon))

**Remaining work**:
- Tuning + benchmark coverage on representative spec workloads
- `optimizations` spec (commented in whitelist) needs to be re-enabled and pass
- Phase 11.6 inliner is the next major addition
- Peephole optimization at the target-dialect level (small-constant strength reduction, etc.)

---

## Verification

After each phase:
1. Update the spec whitelist in [`Testing/SpecTestRunner.maxon`](Testing/SpecTestRunner.maxon).
2. Build the self-hosted compiler: `./bin/maxon.exe build maxon-selfhosted`.
3. Run spec tests for all targets:
   - `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test` (x64-windows, native)
   - `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test --target=x64-linux` (Docker)
   - `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test --target=arm64-linux` (Docker)
4. Verify all whitelisted tests pass on all targets.
5. Cross-check against C# compiler: `./bin/maxon.exe spec-test` to ensure behavioral parity.
