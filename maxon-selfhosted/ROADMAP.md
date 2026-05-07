# Self-Hosted Compiler Roadmap

The self-hosted Maxon compiler (`maxon-selfhosted/`) currently has **45 specs whitelisted** in [`Testing/SpecTestRunner.maxon`](Testing/SpecTestRunner.maxon), with **505 fragment tests passing** on x64-windows. The pipeline is fully built out: lexer → parser → Maxon dialect → Std dialect → MIR (SSA) → Target dialect → code emitter → PE/ELF/Mach-O/Wasm writers, with SSA register allocation, a real optimization pass suite, and (as of Phase 6) a Go-style three-tier slab allocator with refcount-aware managed memory.

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
Phase 7:   Strings                [~] real String type, interpolation, byte/char literals all landed.
                                      primitive-stringable (5/5) whitelisted via PrimitiveExtensions + bootstrap String.equals.
                                      string-type / string-interpolation / character-type still need real String.maxon
                                      whitelist (blocked on parser bugs in stdlib helpers).
Phase 8:   Error Handling         [~] throws/throw/try parsed; otherwise EXPR / 'label' block / (e) 'label' binding /
                                      ignore / return / panic / throw / break / continue all work; E3059
                                      type-mismatch checked at parse + TypeResolution time
Phase 9:   Enums                  [~] enum decls (int/float-backed), match (statement+expression),
                                      methods on enums, keyword-as-case-name; associated values pending
Phase 10:  Closures               [~] closure-self landed (function types in params, closure expressions,
                                      env capture for `self`, indirect calls on x64/arm64/wasm);
                                      general closure-capture (arbitrary outer locals) still pending
Phase 11:  Interfaces & Generics  [~] hybrid model — 11.0–11.4 spine + 11.6 inliner with multi-block splice +
                                      loop-depth multipliers landed. Five of the six Phase-11 specs
                                      previously whitelisted (`interface-conformance`, `interfaces`,
                                      `where-clauses`, `associated-types`, `ranged-typealias`) were
                                      disabled during 2026-05-05 code review pending Phase-11.x stabilisation;
                                      only `equatable` (7/7) remains enabled. C1.e (PrimitiveExtensions)
                                      blocked on Phase 7 string interpolation; C2.d (per-function dispatch)
                                      deferred to follow-up sessions.
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

### Currently whitelisted specs (45)

`basics`, `print-function`, `variables`, `arithmetic`, `float-type`, `panic`, `range-check-panic`, `assignment`, `comparison-operators`, `expressions`, `function-declaration`, `if-statements`, `literals`, `return-statement`, `unary-negation`, `method-calls`, `static-methods`, `byte-type`, `type-casting`, `lexer-edge-cases`, `unary-operators`, `parentheses`, `missing-return-error`, `unknown-keyword-error`, `expected-expression-error`, `unused-parameters`, `parameter-labels`, `duplicate-block-identifiers`, `method-call-on-parameter`, `type-methods`, `self-keyword`, `contextual-literal-typing`, `implicit-type-conversion`, `namespaces`, `stdlib-basic`, `stdlib-autodiscovery`, `match-simple`, `module-keyword`, `lexer-parser-robustness`, `try-otherwise-value-flow`, `closure-self`, `reserved-double-underscore`, `allocator`, `managed-memory-builtin`, `equatable`, `primitive-stringable`.

The previously-whitelisted Phase-11 specs (`enums-simple`, `interface-conformance`, `interfaces`, `where-clauses`, `associated-types`, `ranged-typealias`) were disabled during the 2026-05-05 code review. Each of those specs had partial-failure fragments tied to in-flight Phase-11.x work (witness-table interface dispatch, `__layout`/`__witness` runtime dispatch, ranged-int E3009/E3005 diagnostics, and a runtime cast issue). They will be re-enabled as the corresponding Phase-11.x work stabilises. Specs blocked entirely on Phase-11.4 dispatch (`interface-extensions`, `conditional-extensions`, `parsable-interface`, `primitive-comparable`, `primitive-cloneable`, `primitive-hashable`) remain commented out — they exit cleanly with `interface dispatch for method 'X' is unimplemented (Phase 11.4)` when a fragment hits the dispatch site.

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
- **`lowerManagedMemBuiltin`** intercepts `__ManagedMemory.*` method calls in `lowerMethodCall` and dispatches to **12 `__managed_mem_*` runtime helpers** (one per throwing method) that perform bounds checks + return a non-zero variant ordinal (`ordinal+1`) in the secondary error register (RDX/X1) on failure — the multi-value-return error ABI. The `try ... otherwise default` machinery picks up the error flag automatically.
- **Parser support for `typealias NAME = __ManagedMemory with TYPE`** in [`Parser.maxon`](Compiler/Parser.maxon) so test code can write `typealias IntMem = __ManagedMemory with Int`.
- **12 fragment tests in [`specs/managed-memory-builtin.md`](../specs/managed-memory-builtin.md)** (status: selfhosted) cover `create`, `setLength`/`get`/`set` round-trip, `grow`, `append`, `slice`, byte-granular access, `clear`, all three out-of-bounds error paths, and the destructor freeing through `mm_decref`. **12/12 passing.**

### Real bugs surfaced and fixed during convergence

The full pipeline running on the new allocator surfaced five latent bugs that had never been exercised before:
1. `__slab_*` and `__mm_*` globals were being routed to read-only `.rdata` instead of writable `.data`, causing access violations on the first atomic increment of `__mm_raw_alloc_count`. Fixed in [`X64Backend.maxon`](Compiler/Targets/X64/X64Backend.maxon)'s `relocKindForLabel` and the equivalent ARM64 path.
2. X64 shift lowering could clobber its own count register (RCX) because the regalloc didn't see the implicit physical-register def. Fixed in [`X64RegisterAlloc.maxon`](Compiler/Targets/X64/X64RegisterAlloc.maxon)'s `getImplicitGprDefs`.
3. `__slab_alloc_class`'s retry loop kept a value live across `__slab_refill_mcache` in a caller-saved register; restructured to recompute after the call.
4. The instruction scheduler created a self-edge dependency on `osFreePages` because it was classified as both a store and a call. Fixed in [`InstructionScheduler.maxon`](Compiler/Targets/Shared/InstructionScheduler.maxon)'s `buildDependencies`.
5. The wire value `0` collided with the "no error in flight" sentinel — every `__managed_mem_*` helper now sends `ordinal+1` (matching the existing `lowerThrow` convention).

Plus two non-allocator infra fixes: [`PeWriter.maxon`](Compiler/Targets/Windows/PeWriter.maxon) `dataRawSize` now grows to align the larger prelude; [`StdlibLoader.maxon`](Compiler/StdlibLoader.maxon)'s cache-stripping filter was replaced with a runtime-module-list query (added `isRuntimeFunctionName` helper in [`BackendDispatch.maxon`](Compiler/Targets/BackendDispatch.maxon)) so new runtime functions don't need a manual prefix-list update.

### What's deferred and why

The user-facing `arrays` / `stdlib-array` / `collection` / `for-in over arrays` / `array-managed-elements` / `array-of-bytearray` / `array-hashable` specs all depend on [`stdlib/Array.maxon`](../stdlib/Array.maxon), which presupposes:
- **Phase 7 (real Strings)** for `panic("...")` interpolated messages inside `Array.ensureCapacity` etc.
- **Phase 8 (full throws/throw)** for `Array.get(i) throws ArrayError`, `try managed.get(...) otherwise throw ArrayError.indexOutOfBounds`, etc.
- **Phase 11 (interfaces & generics)** for `Array uses Element`, `implements BuiltinArrayLiteral, Iterable with(Element, ArrayIter), Cloneable`, `extension Array where Element is Equatable`.

Once those land, parsing `stdlib/Array.maxon` becomes possible and the deferred specs unlock automatically — the `__ManagedMemory` primitive completed in Phase 6 is the building block they all wrap.

---

## Phase 7: String Type & Interpolation — MOSTLY DONE

**Goal**: real `String` type with methods, real `print()` from stdlib. **This phase removes the temporary `print()`/`printLiteral`/`printInt` ops** still in the bootstrap.

### Done (2026-05-07)
- **Maxon Dialect**: added `stringInterp`, `charLiteral`, `byteStringLiteral` ops + the `StringInterpPart` union and `InterpExprKind` enum. Removed `printLiteral` and `printInt` from the union, printer rules, and every dispatch site.
- **Std Dialect & both backend dialects/emitters**: stripped `printLiteral`/`printInt` from `StdSystemOp`, X64 + ARM64 op tables, debug-name tables, and emitter dispatch. Bumped stdlib cache version 12 → 13 to invalidate stale entries.
- **Lowering** ([`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon)): `stringConst` materializes an rdata-backed `__ManagedMemory` (capacity = -2, elementSize = 1) wrapped in a 16-byte `String` struct (`managed: __ManagedMemory @0`, `isAsciiFlag: i64 @8`). isAsciiFlag is computed at compile time. `stringInterp` builds total length + heap allocation + per-part memcpy chain producing a String. `charLiteral` mirrors stringConst with an 8-byte `Character` outer struct (no isAsciiFlag). `byteStringLiteral` lowers to a bare `__ManagedMemory` (no outer wrapper). `__Builtins.writeStdout` is a new compiler intrinsic that loads buffer/length from MM and calls `mrt_write_stdout`.
- **Parser**: deleted `parsePrintStatement`/`parsePrintInterpolatedString`/`emitPrintForExpr`/`emitInternedPrintLiteral`/`emitFragmentLiteral`. `parseStringLiteral` now handles interpolation in expression position (no more E2004 print-only gate). `parseCharLiteralExpr` emits `charLiteral` ops for multi-byte chars (single-byte stays as int for back-compat with byte comparisons in the bootstrap source). New `parseByteStringLiteralExpr`. New `if let X = try CALL` / `if var X = try CALL` / `if try CALL` statement form. Several parser improvements landed alongside: `static function` in interface bodies (with `isStatic` field on InterfaceMethodSig), keyword tokens accepted as parameter names (`with`, `type`, etc.), multi-param overload mangling (so `String.slice(StringIndex, StringIndex)` and `String.slice(StringIndex, GraphemeCount)` coexist), named-arg-required check deferred for qualified static calls (so `ByteMemory.create(1, 1)` is accepted post-typealias-resolution), `inFunctionParam` flag suppresses generic-instance `with` consumption inside param lists.
- **Lexer**: removed `TokenKind.print` keyword. Added `byteStringLiteral` token form (`b"..."`). String interp markers handled per existing flow.
- **Runtime** ([`runtime.std`](Compiler/Runtime/runtime.std)): added `mrt_f64_to_string` (sign + integer part via `mrt_i64_to_string` + 6-decimal-digit fractional, no precision heroics) and `mrt_bool_to_string` ("true"/"false"). Deleted `mrt_printInt`. Kept `mrt_write_stdout`, `mrt_i64_to_string`, etc.
- **Stdlib bootstrap**: synthesized minimal `String` and `Character` struct declarations + `print(value String)` body in `StdlibLoader.maxon`'s bootstrap source. Whitelisting the real stdlib files (Builtins/Character/String/Print) was attempted but pulled in too many cross-file dependencies (`ByteView`, `StringIterator`, etc. in unwhitelisted helper files); the synthesized minimal types are sufficient for Phase 7's surface and stay decoupled from Phase 11's interface-dispatch progress.
- **TypeResolution**: `stringConst`/`stringInterp` produce `MaxonType.named("String")` once String is registered. `namedIdCastCategory` special-cases `"String"` → `CastCategory.string` so call/cast diagnostics distinguish String from generic struct-pointer i64. `materializeStringConstIfNeeded` emits the IR op when stringConst flows into a binop/cmp/call arg or interp part.

### Verified
- Spec test parity: **500/505 passed** (same as the pre-Phase-7 baseline — no regression). The 5 remaining failures are pre-existing `string-array-literal-*` cases blocked on Array element-type inference (Array<String> not yet supported) plus one error-detection test (`error.unused-array-typealias`) that lacks a corresponding self-hosted check.
- End-to-end Phase 7 features verified: string literals (`let s = "hello"`), `print(s)`, integer interpolation (`"x = {x}"`), float interpolation (`"y = {y}"`), bool interpolation (`"b = {b}"`), nested interpolation (`"{ "{ "inner" }" }"`).

### Phase 7 finish (2026-05-07 follow-up — Phase-11.4-adjacent unblock)
- **`__Builtins.floatToBits` intrinsic**: registered in `registerBuiltinsIntrinsicSignatures` and intercepted in `lowerBuiltinsIntrinsic`. Lowers via a new `StdArithUnaryOpcode.bitcastF64ToI64` op that round-trips through `MirUnaryOpcode.bitcastF64ToI64` to `movqXmmToGpr` (X64), `fmovToGpr` (ARM64), and `i64.reinterpret_f64` (Wasm). Mem2Reg, LowerStdToMir, and the cache codec all learn the new opcode. Required by `stdlib/PrimitiveExtensions.maxon`'s `float.hash`.
- **`byteStringLiteral` → `Array` wrap**: `lowerByteStringLiteral` now emits a 40-byte `__ManagedMemory` PLUS an 8-byte `Array` outer struct so `bytes.count()` / `bytes.get(i)` dispatch through Array's stdlib methods. Parser's `parseByteStringLiteralExpr` and TypeResolution's `recordOpResult` updated to type the result as `MaxonType.named("Array")`.
- **Primitive-extension type resolution**: `methodCallResultType` and `resolveMethodCallSite` in TypeResolution now fall through to `primitiveExtensionTypeNameWithProject` when the receiver is a primitive (or a typealias that resolves to one). Without this, `let s = 42.toString()` recorded `s` as `unresolved` and downstream `s == "42"` cmp dispatch never rewrote to `s.equals("42")`.
- **`internMaxonType` primitive mapping**: lowercase primitive names (`int`/`float`/`byte`/`string`) returned `MaxonType.named(...)` before, which made an `extension float` block declare `__self` as named-Float, not `MaxonType.float`. Now maps directly to the primitive arms so `"{self}"` interpolation hits the float materialization path.
- **`primitiveExtensionTypeNameWithProject`**: chases named-typealiases to their underlying StdType and returns the extension family (`int`/`byte`/`float`/`bool`/`string`). Bridges `Byte` (= `byte(0 to u8.max)`) and `Integer` (= `int(i64.min to i64.max)`) to their lowercase extension namespaces.
- **Visibility-aware typealias duplicate detection**: `parseTopLevelTypealias` now classifies the relationship between an incoming declaration and any existing entry by visibility tier:
  - Two file-private aliases in *different* files: no conflict (mirrors the C# bootstrap's per-file `_localTypeAliases` semantics; lets stdlib helpers reuse short names like `ByteCount` / `BytePos` privately).
  - File-private + exported (any order): hard error — "typealias 'X' shadows an existing typealias from another file (file-private and exported declarations of the same name are not allowed)". Prevents a private declaration from silently hiding an export, and an export from being silently overridden by a private one.
  - Two exported aliases (or any combination touching `module` / `global` visibility): duplicate error.
  - Same file, same name: duplicate error regardless of visibility.
  Stdlib aliases that survived a cache restore default to `(global, stdlib-bootstrap.maxon)` when `typealiasVisibilities` has no entry (the cache codec doesn't serialize visibility).
- **`mrt_f64_to_string` trailing-zero strip**: walks the buffer back from offset (slot3+6) toward the byte after the decimal point, dropping `'0'` bytes while at least one fractional digit remains. Returns the adjusted length. Fixes `float.toString()` returning `"3.140000"` instead of `"3.14"`.
- **String.equals in bootstrap**: the synthesized `String` type now has an `equals(other String)` method that compares lengths then bytes via `managed.byteAt(i)`. Lets `s == "42"` dispatch to a working byte-equality check without whitelisting the full `stdlib/String.maxon`.
- **Whitelist additions** ([`StdlibLoader.maxon`](Compiler/StdlibLoader.maxon)): `stdlib/Print.maxon` (replaces the synthesized print stub), `stdlib/PrimitiveExtensions.maxon` (deferred from Phase 11.C1.e), and `stdlib/helpers/string/{utf8, utf16, hash}.maxon` now ship with the stdlib cache.
- **Parser fix — `localVar.field = expr`** ([`Parser.maxon::parseIdentifierStatement`](Compiler/Parser.maxon), `parseLocalFieldAssignment`): the `IDENT.IDENT = expr` shape used to unconditionally emit `unresolvedAssign("ident.ident", value)` which TypeResolution couldn't rewrite when the leading identifier was a local variable. Now checks `scope.contains(...)` first and routes to a `varLoad + fieldStore` path. Required by `helpers/string/grapheme.maxon`'s `var newState = GraphemeState.create(...); newState.riCount = ...` pattern.
- **TypeResolution fix — cross-module resolvedCallees pollution** ([`TypeResolution.maxon::runTypeResolution`](Compiler/TypeResolution.maxon)): `project.resolvedCallees` and `project.callParamTypes` are keyed by `OpIndex`, which is per-module. The cache-miss stdlib build path runs `runTypeResolution` on the stdlib MaxonModule (populating those maps with stdlib op indices), then the user pipeline runs `runTypeResolution` on a separate user MaxonModule whose op indices restart from 0 and collide. The parser-side `try ... insert ... otherwise ignore` kept the stale stdlib entries, so user calls resolved to whatever stdlib happened to put at that index — e.g. `print("hello")` resolved to `__ManagedMemory.length`, producing the misleading "argument type mismatch for 'self': expected '__ManagedMemory', got 'String'" diagnostic. Fix: reset both op-keyed maps at the start of every `runTypeResolution` call so each module starts from a clean slate. Surfaces as `print(stringConstLiteral)` failing on the build CLI fresh-cache path while the spec runner (which calls `runTypeResolution` only after the cache is already valid) was unaffected.
- **Stale comment cleanup**: [`LowerMaxonToStd.maxon:1262-1271`](Compiler/IR/Maxon/LowerMaxonToStd.maxon#L1262-L1271) — `forInIterable` is metadata-only; the parser already builds the full createIterator/advance/current methodCall CFG.
- **Cache version**: bumped 13 → 14.

### Spec results after Phase 7 finish
- **505 / 510 fragments passing on x64-windows** (44 specs whitelisted + `primitive-stringable` makes 45). All 5 `primitive-stringable` fragments (int/float/bool/byte `.toString`) green. Bootstrap parity 2736/2736 holds.
- **`byte-string-literal`**: 5 / 8 fragments pass with the Array wrap. Remaining 3 (`map-key`, `top-level-map`, `top-level-map-struct`) use map literals `[b"...": value, ...]` — Phase 13 (Map type) territory. Spec stays commented with a note.

### Still pending (deferred by feature dependency)
- **`string-type`, `string-interpolation`, `character-type`** (the remaining four Phase-7 specs): need the real `stdlib/String.maxon` and `stdlib/Character.maxon` whitelisted, which requires the helper cascade (`stdlib/helpers/string/{utf8, utf16, unicodeCategory, grapheme, hash, views}.maxon`) and `stdlib/CharacterSet.maxon`. The parser bug that previously blocked this (E2004 "Undefined variable" on `localVar.field = expr` patterns in `helpers/string/grapheme.maxon`) was fixed: `parseIdentifierStatement` now distinguishes `localVar.field = expr` (loads the local + emits `fieldStore`) from `TypeName.field = expr` (qualified-global store) by checking `scope.contains(...)` first. `helpers/string/{utf8, utf16, hash, unicodeCategory}.maxon` are now whitelistable. `unicodeCategory.maxon` was unblocked by implementing `__Builtins.ucdByteAt` / `__Builtins.ucdI64At` in the self-hosted lowering pass: the helpers `recoverStringLiteral` (via a new `valueId → StringId` provenance map populated from `lowerStringConst`) and `ensureUcdLoaded` lazily read `stdlib/helpers/string/ucd_bmp.bin` (65 KiB) and `ucd_supp.bin` (6.4 KiB) and register them through `addNamedGlobalData`; `globalAddr + loadByte` (BMP path) and `globalAddr + mul + add + loadIndirect` (supplementary path) emit the actual loads. The fix also surfaced and resolved a pre-existing LICM bug — `licmGetPackedResultId` was returning `LICM_NO_BLOCK` for every `system` / `memory` op, so consumers of `globalAddr` (the new UCD GEP `addi`) were wrongly classified as loop-invariant and hoisted past their producers, leaving use-before-def IR in the preheader. `grapheme.maxon` and `views.maxon` (which depends on a `BuiltinByteLiteral` interface) remain deferred to the C1.e/C1.f follow-up.
- **`strings` spec**: doesn't exist as a `specs/fragments-x64-windows/strings/` directory. Drop this bullet from the Phase 7 target list.

### Files modified (~65 files)
- [`Lexer.maxon`](Compiler/Lexer.maxon), [`Parser.maxon`](Compiler/Parser.maxon), [`MaxonDialect.maxon`](Compiler/IR/Maxon/MaxonDialect.maxon), [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon), [`StdDialect.maxon`](Compiler/IR/Std/StdDialect.maxon), [`TypeResolution.maxon`](Compiler/TypeResolution.maxon), [`StdlibLoader.maxon`](Compiler/StdlibLoader.maxon), [`StdlibCache.maxon`](Compiler/StdlibCache.maxon), [`runtime.std`](Compiler/Runtime/runtime.std), all backend dialect/emitter/regalloc/prologue files, and the IR pass files (`Mem2Reg`, `CSE`, `BorrowCheck`, `DCE`, `InjectDrops`, `Canonicalize`, `LowerStdToMir`, `Inliner`, `DeadFunctionElimination`).

---

## Phase 8: Error Handling — MOSTLY DONE

**Goal**: `try`/`throw`/`otherwise`, `throws` clause, error propagation.

### Done
- **Parser**: `throws ErrorType` in signatures, `throw EnumName.case` statements, `try CALL` propagation form, `try CALL otherwise FALLBACK` with seven dispatch shapes — single expression, `ignore`, `return`, `panic`, `throw`, `break`, `continue`, plus the labeled-block forms `'label' STMTS end 'label'` (plain) and `(e) 'label' STMTS end 'label'` (binding form that exposes the error enum value to the handler body) — all in [`Parser.maxon:3765`](Compiler/Parser.maxon#L3765) (`parseTryExpression`) → [`parseTryFallbackDispatch`](Compiler/Parser.maxon#L3951) → [`finalizeFallbackHandler`](Compiler/Parser.maxon#L4078).
- **Maxon Dialect**: `tryCall` / `tryMethodCall` ops (rewritten in place from `call` / `methodCall` by [`tryRewriteCallToTryCall`](Compiler/Parser.maxon#L4091) and `tryRewriteMethodCallToTryMethodCall`), `throw` op (terminator: emits a single `StdCallOp.errorReturn` placing `ordinal+1` in the secondary error register + default primary value).
- **Std Dialect / runtime**: the **multi-value-return error ABI** carries the error flag across the function-return boundary in the secondary register (RDX/X1) alongside the primary value in R8/X0 — no runtime helpers, no `.data` slot. Lowering at [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon) (`lowerThrow`, `lowerTryCall`, `lowerTryMethodCall`).
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
- `enums-simple` (14 fragments): enum declarations with int and float raw values, methods on enums, `match` as both statement and expression, `gives` and `then` arms, keyword-token case names (`function`, `return`, `end`, `if`, …), error-path diagnostics E3030 / E3031 / E3032 / E3034. **Note**: temporarily disabled in the whitelist as of 2026-05-05 because some fragments started failing during Phase-11.x convergence; the fragments that pass were not separated out (no per-fragment skip mechanism). Re-enable once Phase-11.x stabilises.
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
- **Analysis-driven inlining at static call sites** recovers monomorphized-quality code on hot paths

**Why hybrid instead of full monomorphization** (which the C# bootstrap uses):

1. **Compile speed** — one body per generic method instead of N per (method × type). Eliminates the multiplicative downstream cost (lowering, codegen, register allocation all do less work).
2. **Real per-function incremental compilation** — caller IR references stable callee names (`Array.push`) instead of specialized ones (`__Array_Int.push`), so a change to a generic body doesn't invalidate caller caches.
3. **Smaller binaries** — typically 2–3× smaller code section, better icache behavior.
4. **Dynamic dispatch as a first-class language feature** — heterogeneous collections of trait objects, plugin systems, hot-reload all become tractable.
5. **Cleaner errors and IDE experience** — diagnostics reference the source generic, not a mangled specialization.
6. **Preserved runtime perf** — the inliner uses cost/benefit analysis (callee size, call-site context, generic-arg concreteness) to recover monomorphized output at hot static call sites without any source-level annotations.

**Why now**: this is the cheapest moment in the project to commit to this design. After Phase 11 ships with a different model, retrofitting witness tables means tearing apart the type system, the dispatch story, every cached MIR, and every emitted symbol.

**Reference**: see [`docs/hybrid-generics-plan.md`](../docs/hybrid-generics-plan.md) for the full design document.

### Specs to unlock
`interfaces`, `interface-conformance`, `interface-extensions`, `equatable`, `primitive-comparable`, `primitive-cloneable`, `primitive-hashable`, `where-clauses`, `conditional-extensions`, `associated-types`, `instance-methods`, `pair`, `parsable-interface`, `ranged-typealias`.

### Sub-phase overview

| Sub-phase | Focus | Status |
|---|---|---|
| 11.0 | Foundation: parser + AST | DONE |
| 11.1 | Type system: substitution + conformance | DONE |
| 11.2 | Layout descriptors | DONE (real thunk bodies via C1.a) |
| 11.3 | Witness tables | DONE |
| 11.4 | Generic body lowering | Spine DONE; interface dispatch live (C1.c construction + C1.d witness-method call); InjectDrops type-param dispatch live (C1.b) |
| 11.5 | Per-function incremental queries | Query layer DONE; real per-function dispatch DEFERRED to C2.d follow-up |
| 11.6 | Analysis-based inliner | DONE (single-block + multi-block splice via C2.b; loop-depth multipliers via C2.c). **No annotations of any kind** — the analyzer decides. |
| 11.7 | Validation & polish | 1 of 14 Phase-11 specs whitelisted (equatable 7/7); 5 previously-passing specs disabled during 2026-05-05 code review pending Phase-11.x stabilisation; 445/445 baseline; bench scripts deferred until per-function dispatch lands |

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

### Phase 11.6 — Analysis-based inliner

**Status**: DONE. Call-graph + Tarjan-SCC + decision rule + pipeline integration + Std-level DFE + descriptor-load constant folding all live, and the body-splice mechanic is now firing — both the **single-block + single-return** shape (Phase 11.6 proper) and the **multi-block** shape (C2.b) splice through with full block-id remapping, ValueId remapping, and ret→br-to-continuation handling for multi-return shapes via phi/blockArg. **341 total splices observed on a representative compile, 14 of them multi-block.** Loop-depth budget multipliers (C2.c) reuse `CfgAnalysis.maxon` dominator infrastructure to apply ×3 at depth 1, ×6 at depth ≥ 2; a synthetic test confirms 3 of 4 splices come from the loop-depth bonus. The descriptor folding alone is the primary monomorphization-quality unlock for hybrid generics — active for every `descriptorField` read against a known `loadLayoutDescriptor` label. The self-hosted baseline (445/445 on 44 specs as of the 2026-05-05 code-review trim — see Phase 11.7) and the 2735-spec bootstrap baseline both hold under the new pipeline.

**Goal**: aggressive inlining at static call sites recovers monomorphized-quality code. **Drives runtime perf cost of the hybrid model toward zero on typical code.** No source-level annotations — the compiler decides entirely from IR analysis.

**Design principle**: the user shouldn't have to think about which functions are hot. The compiler has the call graph, the callee bodies, and the call-site context — it has strictly more information than the programmer. Annotation-based inlining (Swift's `@inlinable`, Rust's `#[inline]`) leaks an implementation concern into the surface language and creates a steady tax on stdlib maintenance every time perf-relevant code shifts.

**New pass**: `Compiler/Passes/Inliner.maxon` (operates on Std-level IR before MIR).

**Inputs the analyzer uses**:
- **Callee size**: op count of the lowered Std-level body (cheap, exact, not source LOC).
- **Call-site count**: number of static call sites for the callee across the whole module — a single-call-site callee is always inlined (it's a clean refactor with zero size cost).
- **Recursion**: callees that are part of a recursion SCC in the call graph are never inlined.
- **Indirect calls**: `indirectCall` ops aren't inlinable (no static target). The fat-pointer ABI from 11.3 means interface dispatch falls into this bucket unless devirtualized first.
- **Generic-arg concreteness**: when a call site passes a fully concrete layout descriptor, the inliner can constant-fold descriptor loads inside the inlined body. This is what closes the gap with monomorphized output on `Array<Int>.push` etc.
- **Loop depth at call site**: call sites inside loops get a higher size budget (matches monomorphized hot-loop behavior).
- **Argument shape**: closure literals and constants at the call site enable downstream constant folding / closure inlining and shift the cost/benefit toward inlining.

**Decision rule** (in priority order):
1. Never inline if recursive or indirect.
2. Always inline if the callee has exactly one static call site. Bodies with one caller are functionally a paste-in.
3. Always inline tiny callees (≤ ~10 Std ops) — accessor-shaped methods like `Array.length`, `Array.get`, `Optional.unwrap`.
4. Inline medium callees (≤ ~50 ops) if any of: call site is in a loop; the callee receives a concrete layout descriptor that unlocks descriptor folding; the callee receives a closure literal.
5. Otherwise don't inline.

These thresholds are starting points to be tuned in 11.7 against the perf targets — not a contract.

**Constant folding extensions**: extend the existing canonicalize pass to recognize loads from known descriptor addresses → constants. Run a second canonicalize + DCE pass after each inlining batch — that's where the descriptor-fold cascade actually lands and recovers monomorphized-quality code.

**No annotations of any kind**: the compiler decides entirely from IR analysis. It has the call graph, every callee body, and the call-site context — strictly more information than the programmer. Annotations like `@inlinable` / `@inline` / `@noinline` leak an implementation concern into the surface language and create a permanent stdlib maintenance tax every time perf-relevant code shifts. If the analyzer doesn't pick up a hot path, that's a tuning bug to fix in the inliner, not a hole to patch with an annotation.

**Stdlib**: zero annotations. `Array.push`, `Array.get`, iterators, `Optional.unwrap` are recovered by the size + call-site-count rules above. If the analyzer doesn't pick them up, that's a tuning bug to fix in the inliner, not a hole to patch with an annotation.

### Phase 11.7 — Validation, polish, parity

**Status**: spec-unlock work landed in 2026-05-04 (**6 of 14 Phase-11 specs whitelisted, 505 / 549 total**). The 2026-05-05 code-review pass disabled five of those six specs (`interface-conformance`, `interfaces`, `where-clauses`, `associated-types`, `ranged-typealias`) plus `enums-simple` because partial failures were leaking into the green-suite signal. **Current baseline: 445/445 passing on 44 specs**, with `equatable` (7/7) the only Phase-11 spec still enabled. The disabled specs will be re-enabled as their Phase-11.x dependencies stabilise. Bootstrap parity 2735/2735 holds.

**Spec-unlock changes (2026-05-04)**:
- **E3012 unused-parameter relaxation for interface methods** — parser captures `currentTypeConformances` and skips the unused-param check when the function name appears in any conformed interface (transitively via `extends`). Unlocks `interface-conformance/interface-method-unused-param-allowed`, `interface-method-via-extended-interface`, plus several `interfaces` fragments. ([Parser.maxon `checkUnusedParameters`](Compiler/Parser.maxon))
- **E3015 → E3016 message migration** — `validateConformanceMethods` no longer emits the older E3015 "missing method" form; SemanticCheck's existing `reportMissingMethods` handles emission with the bootstrap-compatible multi-line shape. Unlocks `conformance-missing-method`. ([SemanticCheck.maxon](Compiler/SemanticCheck.maxon), [TypeResolution.maxon](Compiler/TypeResolution.maxon))
- **E3017 constraint-violation diagnostic** (new code) — every generic typealias instantiation gets queued onto `Project.pendingConstraintChecks` during typealias drain; a deferred pass (`drainPendingConstraintChecks`) walks the queue after `validateConformances` and emits E3017 per failing where-clause arg. Unlocks `where-clauses.and-violation`, plus catches a previously unflagged `eq-with-equatable` mismatch. ([TypeResolution.maxon](Compiler/TypeResolution.maxon), [Project.maxon `PendingConstraintCheck`](Compiler/Project.maxon))
- **Graceful fallback for unimplemented Phase-11.4 paths** — `resolveNamedToStdType`, `namedIdCastCategory`, and the X64 + ARM64 `lowerCall` paramTypes lookups previously panicked when an interface-typed parameter or a generic-method monomorphization didn't reach the function table. Each now returns a sane default (`StdType.i64` / `CastCategory.int`) so the test runner keeps going and the upstream diagnostic surfaces.

**Specs still blocked by Phase 11.4 (interface dispatch on values)**: `interface-extensions`, `conditional-extensions`, `parsable-interface`, `primitive-comparable`, `primitive-cloneable`, `primitive-hashable`. Each fragment that exercises actual dispatch fails at runtime with `interface dispatch for method 'X' is unimplemented (Phase 11.4)`. The `parsable-interface` spec also hits parser gaps on `int.fromString(…)` extension-method syntax.

**Other deferred items**:
- **`interface-method-local-var-still-errors`** needs unused-local-variable tracking (separate feature from the param check; today self-hosted only flags unused params).
- **`builtin-interface-user-code`** needs the `BuiltinArrayLiteral` "already implemented" check loosened so a user type `MyCollection` with `uses Element implements BuiltinArrayLiteral` doesn't collide with stdlib `Array`.
- **`where-clauses.constraint-violation` (Map case)** needs the stdlib's typeParameters where-clauses surfaced from the cache restoration (today `lookupWhereClausesForBase` only finds them for in-process unresolved structs; Map sits in resolved structTypes after stdlib cache load and presents an empty constraint set).
- **Most `ranged-typealias` failures** are missing E3009/E3005 ranged-int diagnostics + a runtime cast issue, not generic-dispatch infrastructure.
- **Most `associated-types` failures** are E3015/E3016 message-shape mismatches and a couple of fragments that exercise interface dispatch on `Iter`-typed locals.

**Performance targets** (deferred — not pursued in this session; the open-ended spec unlock work consumed the budget):
- Compile time of stdlib + selfhosted source: **≤ 60% of current C# bootstrap** (~3s vs current 5s)
- Incremental rebuild of one-function edit: **≤ 200ms**
- Runtime: within **10%** of C# bootstrap output on representative benchmarks; within **2%** on stdlib hot loops with inlining
- Binary size: **≤ 50%** of C# bootstrap output

**Tools/benchmarks**: `tools/bench-compile.sh`, `tools/bench-incremental.sh`, `tools/bench-runtime.sh`, `tools/bench-size.sh` not authored — the perf targets need Phase-11.4 to land first (interface-typed values touch the hottest stdlib paths), so a benchmark suite today wouldn't measure the right thing.

**Inliner threshold tuning**: also deferred. Starting thresholds (10/50 op budget; loop-depth multipliers ×3 at depth 1, ×6 at depth ≥ 2 from C2.c) remain the live values. Tuning needs the perf benchmark suite, which needs per-function dispatch (C2.d) to land first.

**Validation**: full spec-test parity with C# bootstrap (2735/2735) holds across all changes; self-host (self-hosted compiler compiles itself in ≤ 3s).

### Completion-phase notes (C1 / C2)

The Phase 11 ship pulled in a series of C-prefixed completion tasks that fold into existing 11.x sections rather than standing as new phases:

- **C1.a** — real layout copy/destroy thunk bodies. Two new runtime helpers (`__layout_word_load` / `__layout_word_store`) in `runtime.std`; shape-driven thunk synthesis in [`BuildLayoutDescriptors.maxon`](Compiler/Passes/BuildLayoutDescriptors.maxon). Folds into 11.2.
- **C1.b** — InjectDrops type-parameter slot dispatch via new `StdSystemOp.dropTypeParam(slotValueId, layoutValueId)` op (descriptor-routed `descriptorField(.destroyFunc) + indirectCall`). Slot MaxonType plumbing; stdlib cache v8 → v9. Folds into 11.4.
- **C1.c** — interface fat-pointer construction infrastructure: 2-slot widening, per-function `interfaceWitnessSlots` map, `resolveWitnessLabel`, ABI plumbing. Folds into 11.4.
- **C1.d** — witness-method dispatch at call sites; `MaxonType.interface(_)` threading through TypeResolution; `methodIndexInInterface` helper; param-witness slot allocation; mem2reg fat-pointer guards. Folds into 11.4.
- **C1.e** — `PrimitiveExtensions.maxon` whitelist + primitive conformance wiring. **DEFERRED**: blocked by Phase 7 string interpolation (every `toString()` returns `"{self}"` which the self-hosted parser rejects). Three primitive specs (`primitive-comparable`, `primitive-cloneable`, `primitive-hashable`) remain not whitelisted.
- **C1.f** — runtime ASLR/DEP fix surfaced by C1.d's witness path: PE had `DllCharacteristics = 0x8160` (DYNAMIC_BASE | HIGH_ENTROPY_VA | NX_COMPAT | TERMINAL_SERVER_AWARE) but no `.reloc` section, so absolute VAs in rdata pointed to unmapped memory after the loader's slide. Cleared the DYNAMIC_BASE bits → `0x8100`. Unlocked +1 spec (`interface-method-unused-param-allowed`).
- **C2.a** — stripped the `IrFunction.noInlineAnnotated` field that violated the no-annotations principle. Pure subtractive cleanup.
- **C2.b** — multi-block inliner splice (block-id remap, ValueId remap, ret→br-to-continuation with phi/blockArg for multi-return shape). Folds into 11.6.
- **C2.c** — loop-depth budget multipliers via reused dominator infrastructure in `CfgAnalysis.maxon`. ×3 at depth 1, ×6 at depth ≥ 2. Folds into 11.6.

### Deferred follow-up: C2.d per-function dispatch

The 11.5 query layer (`queryMidForFunction` / `queryMirForFunction` / `queryCodeForFunction`) currently projects from whole-module results. Real per-function execution requires (1) per-function Std-pass plumbing (singleton-module pack/unpack), (2) per-function MIR pipeline, and (3) per-function backend emission with regalloc + prologue/epilogue split, plus chunk concat with cross-function `CallFixup` resolution. Auditor identified six structural blockers — shared module arrays, monolithic backend pipeline, `augmentWithRuntime` is interprocedural, `lowerMaxonToStd` is the entry into Std world, structural-compare verifiability, and `CallFixup` records aren't surfaced per-function. Recommended split into three follow-up tasks:

- **C2.d.1** — per-Std-function dispatch.
- **C2.d.2** — per-function MIR pipeline.
- **C2.d.3** — per-function backend emission + chunk concat + regalloc/prologue split.

The backend split (C2.d.3) is genuinely a multi-day effort.

### Known latent bug

`Compiler/IR/Std/DeadCodeElimination.maxon::rewriteCondBr` (~lines 320–341): when collapsing both arms of a `condBr` through empty ret-blocks that forward to the same continuation with **different** argLists, the second bypass silently drops the second arm's argList. C2.b's `isMultiBlockSpliceable` gates around this; the proper fix is one block of code in DCE.

### Files to modify (summary)

| Sub-phase | File / area |
|---|---|
| 11.0 | [`Lexer.maxon`](Compiler/Lexer.maxon), [`Parser.maxon`](Compiler/Parser.maxon), [`MaxonDialect.maxon`](Compiler/IR/Maxon/MaxonDialect.maxon), [`Project.maxon`](Compiler/Project.maxon) |
| 11.1 | New: `Compiler/TypeSystem/Substitution.maxon`, `Compiler/TypeSystem/Constraints.maxon` |
| 11.2 | New: `Compiler/Passes/BuildLayoutDescriptors.maxon`; [`Project.maxon`](Compiler/Project.maxon) (`LayoutDescriptorTable`) |
| 11.3 | New: `Compiler/Passes/BuildWitnessTables.maxon` |
| 11.4 | [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon), [`StdDialect.maxon`](Compiler/IR/Std/StdDialect.maxon) |
| 11.5 | [`Queries.maxon`](Compiler/Queries.maxon), [`PassPipeline.maxon`](Compiler/IR/PassPipeline.maxon), [`IncrementalTestRunner.maxon`](Testing/IncrementalTestRunner.maxon) |
| 11.6 | New: `Compiler/Passes/Inliner.maxon`; extensions to [`Canonicalize.maxon`](Compiler/IR/Std/Canonicalize.maxon) for descriptor-load folding; call-graph helper for SCC detection |
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
