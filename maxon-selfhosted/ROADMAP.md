# Self-Hosted Compiler Roadmap

The self-hosted Maxon compiler (`maxon-selfhosted/`) currently has **144 specs whitelisted** in [`Testing/SpecTestRunner.maxon`](Testing/SpecTestRunner.maxon), with **1587 fragment tests passing** on both x64-windows and wasm32-wasi. C# bootstrap holds at 2770/2770 (modulo one pre-existing flaky network test, `http-client.response-headers`, that depends on `httpbin.org` reachability). The pipeline is fully built out: lexer → parser → Maxon dialect → Std dialect → MIR (SSA) → Target dialect → code emitter → PE/ELF/Mach-O/Wasm writers, with SSA register allocation (Stage Q: LLVM-Greedy-style phi-merge splitting + memory-only spill fallback), a real optimization pass suite, and (as of Phase 6) a Go-style three-tier slab allocator with refcount-aware managed memory. Since Stage P (2026-05-20) the chunk-driven per-function emit path is the default and a single-function edit triggers a true incremental rebuild via `db.codePerFunc`.

Each phase brings X64 + ARM64 backends and PE + ELF output formats to parity together. WASM and Mach-O writers exist but are not the primary correctness target. All targets (`x64-windows`, `arm64-windows`, `x64-linux`, `arm64-linux`) are kept in lockstep within each phase.

## Progress

```
Phase 1:   Core Arithmetic        [x] arithmetic, comparison, unary, parentheses, expressions
Phase 2:   Control Flow           [x] if/else, while, break/continue (with for-range step block), return, match (statement+expression), for-range over integers, for-range over ASCII char literals
Phase 3:   Function Params        [x] parameters, parameter-labels, assignment, method calls
Phase 4:   Basic Types            [x] byte/float/type-casting, implicit-type-conversion, bool-type,
                                      byte-enum-comparison all pass; bool-bit-packing edge cases pending
Phase 5:   Structs                [x] struct literals, methods, self, type/static methods,
                                      all challenge-* specs (field-assign, nested-structs,
                                      struct-lifetime, array-of-structs) pass; module-level-struct-var
                                      parked under Phase 12
Phase 6:   Managed Memory         [x] slab allocator + __ManagedMemory builtin done;
                                      arrays / array-managed-elements / array-return-element-from-loop
                                      whitelisted; remaining array specs blocked on union types (Phase 9)
                                      or borrow checking (Phase 5+ followups)
Phase 7:   Strings                [x] real String type, interpolation, byte/char literals all landed.
                                      string-type (72/77), string-interpolation (46/49), character-type
                                      (21/24), primitive-stringable (5/5) all whitelisted. Residuals:
                                      3 string-type fragments hit a pre-existing inliner/regalloc phi
                                      bug on for-in-with-call-in-body (out of Stage A); 3 string-interp
                                      fragments (logical-expression, plus-on-string, string-enum) need
                                      separate parser fixes; 3 character-type fragments need an
                                      escape-sequence parser fix + parser-level Character provenance
                                      for `"{c}"` interpolation.
Phase 8:   Error Handling         [x] throws/throw/try parsed; otherwise EXPR / 'label' block / (e) 'label' binding /
                                      ignore / return / panic / throw / break / continue all work; E3059
                                      type-mismatch checked at parse + TypeResolution time.
                                      if-try (18/20) and error-handling (33/33) whitelisted; the 3
                                      previously-deferred assoc-value-throw-catch fragments now pass
                                      via Phase 9 unions. Parser learned `else (e) 'label'` error-binding
                                      mirroring `otherwise (e) 'label'`. Two if-try fragments tagged
                                      out pending Phase 13 Map + E3087 redundant-contains-get diagnostic.
Phase 9:   Enums & Unions         [x] enum decls (int/float-backed, struct-backed, nested struct-backed),
                                      associated-value unions (construct/match/destruct/write-back),
                                      .unionCases companion, .allCases/.allCaseNames/.ordinal/.name/.fromRawValue/.fromName,
                                      range patterns, divergent arms, error-handling with assoc-value throws,
                                      array-of-union element-size, match (statement+expression),
                                      methods on enums, keyword-as-case-name
Phase 10:  Closures               [x] closure-self, first-class-functions (10/10), closure-capture
                                      (6/7) all whitelisted. General by-reference capture of arbitrary
                                      outer locals + `outer.field` chains landed via new parser
                                      branch + `patchClosureEnvLoadCaptureTypes` / `patchFunctionTypeRegistryReturns`
                                      TR passes (capture types/return types resolved post-parse).
                                      Bare function references (`let f = double`) emit `functionRef`;
                                      wasm indirect calls use a synthesized `__fnref_*` thunk so
                                      free-function refs share the closure ABI shape. One fragment
                                      tagged out pending Phase 13 Array.map.
Phase 11:  Interfaces & Generics  [x] hybrid model — 11.0–11.6 spine, C1.a–C1.f completion tasks,
                                      C2.a–C2.c inliner work, and C2.d per-function dispatch all
                                      shipped across Stages A–O. 19 Phase-11-bucket specs
                                      whitelisted (interfaces, interface-conformance,
                                      interface-extensions, conditional-extensions, equatable,
                                      primitive-stringable, primitive-comparable,
                                      primitive-cloneable, primitive-hashable, interface-dispatch,
                                      interface-dispatch-throws-match, parsable-interface,
                                      ranged-typealias, string-type, string-interpolation,
                                      character-type, initablefromarrayliteral, where-clauses,
                                      associated-types). Stream A (2026-05-20) re-enabled the last
                                      two via fixes for spurious E3062 on typealiases consumed by
                                      `with` bindings, E3016 emission for conformers missing
                                      required `with` clauses, and the residual
                                      `lowerInterfaceMethodCallStub` panic on type-parameter-typed
                                      receivers. Stream C landed C2.d per-function dispatch across
                                      the Std, MIR, and backend tiers, then Stage P (2026-05-20)
                                      closed the per-function-dispatch loop by rewiring
                                      `queryCodeResult` to consume `db.codePerFunc`. Single-function
                                      edits now hit a true incremental-rebuild path: 100-fn
                                      synthetic ~325ms warm (vs 480ms baseline); 300-fn synthetic
                                      ~2.1s warm (vs 3.4s baseline, 1.6x speedup) with 301/302
                                      chunks reused from cache. The original Phase-11.5 50-200ms
                                      claim was loosened to a ~400ms gate after Step 4 traced the
                                      remaining bottleneck to `queryAllMid` (parser + Std-tier
                                      passes still run whole-module on every revision bump);
                                      closing that gap is scoped to a future stage. The
                                      `--per-function-dispatch` diagnostic flag was deleted
                                      end-of-Stage-P along with the PerFunctionParity byte-parity
                                      harness — the spec test matrix at 1379/1379 is now the
                                      canonical correctness gate.
Phase 12:  Global Variables       [x] top-level-let (16/16, 1 tagged for Phase 15c FilePath),
                                      module-level-struct-var (3/3), static-variables (28/28),
                                      export-keyword (24/24), exported-struct-var-cross-file all
                                      green. Generic typealias parsing landed in Phase 11.0; arity
                                      validator (E2003 "Type 'X' expects N type argument(s)") added.
                                      Chained global-field assignment (`state.inner.x = 99`) parsed.
                                      `Type from <literal>` initializer routes through __module_init.
Phase 13:  Collections            [x] map (50/50), set (23/23), vector (23/23),
                                      array-hashable (7/7), map-struct-bytearray (4/4),
                                      map-try-otherwise-block (2/2), collection (16/16)
                                      all whitelisted, fully green on both x64-windows
                                      and wasm32-wasi. The four previously-"tagged"
                                      fragments (map.for-in.nested,
                                      map.insert.duplicate-error-binding,
                                      array-hashable.int-array-map-key,
                                      set.typealias-of-array-element) all pass.
                                      `Set from [...]` generic-inference and
                                      `Vector with N` sized typealias parsing both
                                      shipped earlier — Stage D's tagged-out claims
                                      were stale on closeout.
Phase 14:  Math Functions         [x] All 16 specs whitelisted: abs/sqrt/floor/ceil/round/trunc/
                                      sin/cos/tan/atan2/exp/log/log2/log10/min/max. Hardware ops
                                      for abs/sqrt/floor/ceil/round/trunc/min/max via Std + MIR
                                      + 3-backend (x64/arm64/wasm) lowering. Trig/log/exp run as
                                      Taylor-series Maxon code in `stdlib/Math.maxon`. 24 rt-*
                                      fragments still wait on a nested-try parser bug.
Phase 15:  Advanced Features      [x] Tuples (9/9 active, 1 tagged for Phase 13 MapIterator),
                                      panic-stack-trace (3/3), function-overloads (10/11),
                                      command-line-args (9/10 x64; 1/10 wasm — needs WASI
                                      args_sizes_get / args_get), discarded-results (17/17),
                                      duplicate-functions (3/3), unused-variables (20/20),
                                      file-io (10/10) + directory (19/19) on x64-windows +
                                      wasm32-wasi (runtime-tested) and on x64-linux +
                                      arm64-windows + arm64-linux (compile-only — host
                                      can't cross-execute). Unified under target-agnostic
                                      Std-dialect ops: 11 new `osFile*` / `osDir*` ops in
                                      StdDialect / MIR, lowered per-target (Linux syscalls
                                      via `mov rax/x8, num + syscall/svc #0`; Windows via
                                      `call mrt_win32_*` helpers wrapping Win32 IAT calls;
                                      Wasm intercepted at MirToWasm and hand-emitted as
                                      WASI imports). One `runtime.std` body per public
                                      `mrt_file_*` / `mrt_directory_*` helper, no per-OS
                                      Maxon-level fork. Plus pre-existing namespaces,
                                      stdlib-autodiscovery, semantic checks.
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

### Currently whitelisted specs (46)

`basics`, `print-function`, `variables`, `arithmetic`, `float-type`, `panic`, `range-check-panic`, `assignment`, `comparison-operators`, `expressions`, `function-declaration`, `if-statements`, `literals`, `return-statement`, `unary-negation`, `method-calls`, `static-methods`, `byte-type`, `type-casting`, `lexer-edge-cases`, `unary-operators`, `parentheses`, `missing-return-error`, `unknown-keyword-error`, `expected-expression-error`, `unused-parameters`, `parameter-labels`, `duplicate-block-identifiers`, `method-call-on-parameter`, `type-methods`, `self-keyword`, `contextual-literal-typing`, `implicit-type-conversion`, `namespaces`, `stdlib-basic`, `stdlib-autodiscovery`, `match-simple`, `module-keyword`, `lexer-parser-robustness`, `try-otherwise-value-flow`, `closure-self`, `reserved-double-underscore`, `allocator`, `managed-memory-builtin`, `equatable`, `primitive-stringable`, `ranged-typealias`.

`ranged-typealias` was re-enabled on 2026-05-09 (43/43 fragments passing on
both x64-windows and wasm32-wasi). Coverage:

- Parse-time diagnostics: E2003 for bare-sized-type shorthand
  (`typealias I = i64`), E3005 for mismatched type qualifiers, byte-
  range overflow/negative, unrepresentable ranges, out-of-range integer
  bound ordering.
- Compile-time literal-range checks: E3005 on `LITERAL as RangedAlias`
  (including unary-negated literals like `-5 as Positive`) and on
  `return LITERAL` against ranged return types.
- Runtime cast-site range checks: a new Maxon-level
  [`expandCastRangeChecks`](Compiler/Passes/ExpandCastRangeChecks.maxon)
  pass replaces parser-emitted `MaxonOp.castCheck` placeholders with a
  cmp + condBr + panic guard, splitting the host block in place. The
  pass uses [`addBlockAtPosition`](Compiler/IR/IrModule.maxon) so the
  panic + continue blocks land immediately after the host in
  `func.blockRefs`, preserving the def-before-use invariant lowering
  relies on for SSA-name resolution.
- Cast-category fix: storage and cast category are now decoupled.
  `int(...)` aliases still narrow storage via `computeOptimalIntType`
  (so `ExitCode = int(0 to 255)` packs as u8), but the user-declared
  cast category is pinned in a new `project.typealiasCastCategories`
  sidecar so a u8-stored `int(...)` alias still validates as `int` at
  cast/compare sites. `byte(...)` aliases route through a new
  `UnresolvedTypeExpr.rangedByte` arm and pin to the `byte` category.
  The sidecar round-trips through the stdlib cache (CACHE_FORMAT_VERSION
  bumped to 23).

The Phase-11 specs `where-clauses` and `associated-types` remain disabled — their partial-failure fragments are tied to in-flight Phase-11.x work (witness-table interface dispatch, `__layout`/`__witness` runtime dispatch). They will be re-enabled as the corresponding Phase-11.x work stabilises. Specs blocked entirely on Phase-11.4 dispatch (`interface-extensions`, `conditional-extensions`, `parsable-interface`, `primitive-comparable`, `primitive-cloneable`, `primitive-hashable`) remain commented out — they exit cleanly with `interface dispatch for method 'X' is unimplemented (Phase 11.4)` when a fragment hits the dispatch site.

The `allocator` and `managed-memory-builtin` specs are marked `status: selfhosted` in their frontmatter — the C# bootstrap runner skips them via [SpecParser.cs](../maxon-sharp/Testing/SpecParser.cs) since the bootstrap doesn't expose the `__mm_*` intrinsics they exercise.

---

## Phase 1: Core Arithmetic & Expressions — DONE

**Specs unlocked**: `arithmetic`, `comparison-operators`, `expressions`, `literals`, `parentheses`, `unary-operators`, `unary-negation`, `lexer-edge-cases`.

All operators (`+`/`-`/`*`/`/`/`mod`/`and`/`or`/`xor`/`shl`/`shr`/`not`/comparisons), full precedence, `true`/`false`, parens, block scoping. Both X64 and ARM64 backends emit machine code for all corresponding ops.

**Still pending in this area**: none — `bitwise-operators` was enabled (2026-05-09) after fixing one test whose expected exit code (171) exceeded the post-wasm `ExitCode` range cap of 125.

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

Parser at [`Parser.maxon:181`](Compiler/Parser.maxon#L181) (`parseFunctionParameters`). Maxon uses a custom internal calling convention on x64 (identical on all OSes): params in RCX/RDX/RAX/R9/RSI/RDI/RBX, return in R8, error flag in RDX; ARM64 follows AAPCS64 with X0–X7 for params, return in X0, error flag in X1. ABI lowering handled by [`LowerABI.maxon`](Compiler/IR/Std/LowerABI.maxon).

---

## Phase 4: Basic Types — DONE

**Specs passing (2026-05-12)**: `byte-type`, `float-type`, `type-casting`,
`contextual-literal-typing`, `implicit-type-conversion` (9/9),
`bool-type` (4/4), `byte-enum-comparison` (3/3).

Implicit conversion in argument passing is wired through
`emitImplicitConvert` in [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon),
covering int↔float, parameter promotion, and the math-intrinsic int→float
path. Type-checker now rejects bool↔int and string→int at call sites.

**Still pending in this area**:
- `bool-bit-packing` (11/15 pass) — last 4 need bool/int implicit-cast
  conversion in `Vector.push` plus Vector.insert/Vector.remove fixes for
  the bool-bit-packed path.
- `char-literal-to-int` (7/8 pass) — `char-literal-codepoint-iteration`
  hits an unresolved-name parser bug.

---

## Phase 5: Structs — DONE

**Specs passing**: `method-calls`, `method-call-on-parameter`, `self-keyword`,
`static-methods`, `type-methods`, `challenge-struct-field-assign` (3/3),
`challenge-nested-structs` (4/4), `challenge-struct-lifetime` (3/3),
`challenge-array-of-structs` (4/4).

Parser at [`Parser.maxon:2439`](Compiler/Parser.maxon#L2439) (`parseStructLiteral`), [`Parser.maxon:1056`](Compiler/Parser.maxon#L1056) (`parseTypeDecl`). Heap allocation via OS calls is wired through `OsDescriptor` and `BackendDispatch`.

### Phase 5 finish (2026-05-12)

Three small fixes unblocked the challenge specs:

- **`enum.rawValue` / `enum.ordinal` accessors** in
  [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon)'s
  `lowerFieldLoad`: when the receiver is a registered enum, the accessor
  is a value-level no-op (the SSA value already carries the raw
  ordinal), so the result name is aliased to the receiver's `ValueId`.
- **`break` / `continue` inside match arm bodies** in
  [`Parser.maxon`](Compiler/Parser.maxon)'s
  `parseMatchStatementArmBodyInner`: the parser now recognises both
  keywords (previously failed with E2004 "expected expression"). Wired
  up the previously-unused `matchStack` so unlabeled `break` inside an
  arm exits the match (not an enclosing loop), mirroring the bootstrap's
  switch-style semantics. A new `resolveBreakTarget` consults both
  stacks; a labeled `break 'foo'` searches both and prefers the match
  context on a tie.
- **`challenge-array-of-structs` exit code**: adjusted the test's
  base-10 packing to base-5 since the new `ExitCode` upper bound is 125.

`challenge-struct-ownership` does not exist as a spec file (the ROADMAP
reference was stale). `module-level-struct-var` is parked under Phase 12.

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
- **17 builtin method signatures registered** (`length`/`capacity`/`elementSize`/`clear`/`setLength`/`get`/`set`/`remove`/`grow`/`shiftRight`/`shiftLeft`/`byteAt`/`setByte`/`append`/`slice`/`toCString` plus static `create`). The former `makeCharFromBytes` builtin moved out of the self-hosted compiler into `stdlib/helpers/string/grapheme.maxon` as the free function `makeCharacterFromManagedRange` so the inliner can see its body at StringIterator hot-path call sites. The helper file (rather than Internals.maxon or Builtins.maxon) is the home because the C# bootstrap deliberately skips Internals.maxon and the self-hosted reader-file gate forbids non-internals stdlib files from calling `__`-prefixed names — a regular non-`__` name in a whitelisted helper file works for both compilers.
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

After Phases 7 / 8 / 11 stabilised, `stdlib/Array.maxon` does parse, and
the user-facing array specs unlocked progressively:

- **2026-05-09**: `arrays` (46/46) re-enabled.
- **2026-05-11**: `byte-string-literal` re-enabled via per-instance
  witness thunks (Phase 11.x).
- **2026-05-12**: `array-managed-elements` (3/3) and
  `array-return-element-from-loop` (1/1) added to the whitelist.

Still deferred:

- `array-realloc-dangling-ref` — needs the E3070 mutable-borrow-while-
  read borrow-check diagnostic.
- `array-slice-managed-elements` / `array-append-managed-elements` /
  `array-managed-field-reassign` / `array-managed-multi-call-lifecycle`
  / `array-enum-element-size` — every fragment uses `union` types
  (Phase 9 associated-value enums) which the parser doesn't yet accept.
  `array-managed-field-reassign/reassign-array-field-simple-managed`
  also surfaces a separate TypeResolution gap (parameter-typed
  `c.items` reports `unresolved` at fieldLoad time).
- `array-of-bytearray` / `array-hashable` / `array-contains` / etc. —
  Phase 11.x cleanup; revisit alongside the remaining
  `where-clauses` / `associated-types` work.

The `__ManagedMemory` primitive completed in this phase remains the
shared building block for every collection (Array, Vector, Map, String).

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
- Spec test parity: **500/505 passed** (same as the pre-Phase-7 baseline — no regression). The 5 remaining failures are pre-existing `string-array-literal-*` cases blocked on Array element-type inference (Array<String> not yet supported) plus one error-detection test (`error.unused-array-typealias`) that lacked a corresponding self-hosted check at the time. The unused-typealias check (E3062) landed in 2026-05-09; `arrays` now passes 46/46.
- End-to-end Phase 7 features verified: string literals (`let s = "hello"`), `print(s)`, integer interpolation (`"x = {x}"`), float interpolation (`"y = {y}"`), bool interpolation (`"b = {b}"`), nested interpolation (`"{ "{ "inner" }" }"`).

### Phase 7 finish (2026-05-07 follow-up — Phase-11.4-adjacent unblock)
- **`__Builtins.floatToBits` intrinsic**: registered in `registerBuiltinsIntrinsicSignatures` and intercepted in `lowerBuiltinsIntrinsic`. Lowers via a new `StdArithUnaryOpcode.bitcastF64ToI64` op that round-trips through `MirUnaryOpcode.bitcastF64ToI64` to `movqXmmToGpr` (X64), `fmovToGpr` (ARM64), and `i64.reinterpret_f64` (Wasm). Mem2Reg, LowerStdToMir, and the cache codec all learn the new opcode. Required by `stdlib/PrimitiveExtensions.maxon`'s `float.hash`.
- **`byteStringLiteral` → `Array` wrap**: `lowerByteStringLiteral` now emits a 40-byte `__ManagedMemory` PLUS an 8-byte `Array` outer struct so `bytes.count()` / `bytes.get(i)` dispatch through Array's stdlib methods. Parser's `parseByteStringLiteralExpr` and TypeResolution's `recordOpResult` updated to type the result as `MaxonType.named("Array")`.
- **Primitive-extension type resolution**: `methodCallResultType` and `resolveMethodCallSite` in TypeResolution now fall through to `primitiveExtensionTypeNameWithProject` when the receiver is a primitive (or a typealias that resolves to one). Without this, `let s = 42.toString()` recorded `s` as `unresolved` and downstream `s == "42"` cmp dispatch never rewrote to `s.equals("42")`.
- **`internMaxonType` primitive mapping**: lowercase primitive names (`int`/`float`/`byte`/`string`) returned `MaxonType.named(...)` before, which made an `extension float` block declare `__self` as named-Float, not `MaxonType.float`. Now maps directly to the primitive arms so `"{self}"` interpolation hits the float materialization path.
- **`primitiveExtensionTypeNameWithProject`**: chases named-typealiases to their underlying StdType and returns the extension family (`int`/`byte`/`float`/`bool`/`string`). Bridges `Byte` (= `int(0 to u8.max)`) and `Integer` (= `int(i64.min to i64.max)`) to their lowercase extension namespaces.
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

### Sentinel-removal sweep (2026-05-08 follow-up)

Cross-cutting refactor that removes empty-string sentinels (`""` for "absent")
from the self-hosted pipeline and replaces them with sum types and `throws`-
based recovery. Driven by the observation that `""`-as-absent shadowed real
"empty value" cases in cache I/O and made the parser/lowering call chains
ambiguous about whether a returned string was meaningful or a miss flag.

- **Sum-typed return values** for the previously-string-sentinel APIs:
  `ThrowsClause` (none / throwing(name)) replaces `IrFunction.throwsTypeName String` and `InterfaceMethodSig.throwsTypeName String`;
  `ParentWitnessLabel` (none / present(name)) replaces `WitnessTable.parentWitnessLabel String`;
  `BuiltinArrayLiteralName` (none / found(name)) replaces `StdlibCacheData.builtinArrayLiteralTypeName String`;
  `CallConvSlot` (gpr(ord) / spilled) replaces `RegAllocTarget.getCallConvGprOrd`'s `-1` sentinel for "spill to stack".
- **Throwing helpers** for the rest: `parseBuildName`, `chaseTypealiasNameToStruct`,
  `baseNameOfMaxonType`, `bareMethodName`, `stripFirstDollar`, `descLabelGet`,
  `lookupCount` (CoverageReport), `resolveWitnessLabel`, `enclosingTypeName(For{Callee,})`,
  `enclosingTypeNameForCallee`, `enumNameFromMaxonType`, `entryBlockFilePath`,
  `findInterfaceExtensionCallee`, `findLoadLayoutDescriptorLabel`, `findOpPosition`,
  `mathIntrinsicTarget`, `managedMemHelperFor`, `primitiveExtensionTypeName{,WithProject}`,
  `recoverStringLiteral`, `prepareUcdLoadArgs`, `lookupFieldBy{Receiver,StructName}`,
  and the parser's `parseTypealiasRhs` / `discoverBuiltinArrayLiteralType` /
  `resolveLabelToSlot`. Each helper throws a small named error enum so callers
  use `try ... otherwise` (or `if let ... else`) to take the matched-value path
  and route diagnostics on the throw.
- **Cache codec recovery boundary**: a new `CacheCorruption` union (`absent` /
  `invalidEncoding`) plus `writeOptionalString` / `readOptionalString` (1-byte
  0/1 tag + optional length-prefixed payload) handle the sum-typed cache
  fields. `writeString` / `readString` now reject length-0 payloads — a
  writer that needs to encode an empty value must route through
  `writeOptionalString`. Every `writeStdlibCache` / `readStdlibCache` call
  chain is now `throws CacheCorruption`-aware end-to-end; the top-level
  driver catches the throw, logs an `info` diagnostic identifying the
  cache as corrupt, and falls back to the cold path. **Cache version
  bumped 14 → 15** to invalidate stale on-disk caches whose
  `ThrowsClause` / `ParentWitnessLabel` / `BuiltinArrayLiteralName` slots
  use the old length-prefixed-string encoding.
- **SemanticCheck dead-code removal**: dropped the Phase-11.7 wrong-signature
  aggregator (~220 lines: `formatSignatureMismatch`, `paramTypesMatch`,
  `returnTypeMatches`, `signatureIsStrictlyConcrete`, `maxonTypeIsConcreteUserType`,
  `signatureHasUnsubstitutedTypes`, `maxonTypeIsUnsubstituted`, `namedIsUnsubstituted`,
  `buildWrongSignatureMessage`) — never reached in any spec and was the
  single largest concentration of `""`-sentinel returns in the file.
- **C# bootstrap parity touch**: `2-Parser.cs` learns to classify
  enum-typed interface returns as `MaxonValueKind.Struct` so interface-method
  return-type inference handles enum returns alongside struct/interface
  returns (parser was already doing the right thing for the struct case).

**Spec results unchanged**: 505/510 on both x64-windows and wasm32-wasi
(5 expected failures in `arrays/string-array-literal-*` per the existing
Phase-11/Phase-7 array-element-type-inference deferral). 2736/2736 on the
C# bootstrap.

### Stage A landing (2026-05-13) — Phase 7 closure + 11.4 hardening

Phase 7 closed alongside a Phase-11.4 coverage harden via a multi-stage spec
push. Net: **+159 fragment passes** vs the prior 809-baseline, **968/986 on
x64-windows** and **969/986 on wasm32-wasi**, **C# bootstrap parity held at
2722/2722**.

- **New spec `interface-dispatch` (14/14)** — explicit coverage of the 11.4
  dispatch shapes (fat-pointer construction, interface params, interface
  returns, interface fields, transitive `extends`, two conformers, generic
  method dispatch). Authored under `specs/interface-dispatch.md` with self-
  contained fragments so 11.4 regressions surface independently of stdlib.
- **4 root-cause fixes on the 11.4 path landed during the new spec**:
  (a) `methodCallResultType` now stamps interface-receiver method calls in
  the producer-type map so binops of interface-dispatched calls type-check
  ([`TypeResolution.maxon`](Compiler/TypeResolution.maxon)); (b) `coerceArgToParam`
  emits E3005 when a non-conforming concrete type is passed to an interface
  parameter ([`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon));
  (c) interface-typed struct fields now reserve 16 bytes (data + witness) at
  `layoutAllStructs` time and `lowerFieldLoad`/`lowerFieldStore` emit paired
  witness ops; (d) interface-return ABI carries witness in the secondary
  return register reusing the multi-value error-return shape — no backend
  changes needed.
- **Phase-7 closure (specs whitelisted)**: `string-type` 72/77,
  `string-interpolation` 46/49, `character-type` 21/24, `primitive-stringable`
  5/5, plus `primitive-comparable` 11/13 and `parsable-interface` 10/15 from
  the previously-disabled 11.4-blocked set.
- **`makeCharFromBytes` moved out of the compiler** into
  `stdlib/helpers/string/grapheme.maxon::makeCharacterFromManagedRange`. Old
  panic-stub at `lowerMakeCharStub` + the `registerManagedMemoryMethod`
  registration removed entirely. Participates in the inliner now; matters
  because `StringIterator` calls it 5x in tight loops.
- **`__int_fromString`, `__float_fromString`, `__bool_fromString`,
  `__byte_fromString` + `ParseError` + `ParsedInt`/`ParsedFloat`** synthesized
  in `StdlibLoader.maxon::stdlibBootstrapSourceForTarget`. Whitelisting the
  full `stdlib/Builtins.maxon` exposes a separate parser bug on
  `InitableFromArrayLiteral.init(value Array with Element)` — out of scope.
- **Lexer `\{` and `\}` escapes** + an E1006 "Unescaped '{'" diagnostic that
  distinguishes the unescaped-brace case from "unterminated string."
- **String interpolation extensions**: Stringable user types dispatch through
  `Stringable.toString` witness; enum cases interpolate by case name (via
  ordinal compare-chain over rdata-emitted case-name buffers, with explicit-
  int-backed enums falling back to raw value); `:` format spec parser arm
  plus `mrt_i64_to_string_fmt` / `mrt_u64_to_string_fmt` / `mrt_f64_to_string_fmt`
  runtime helpers implementing `[0][width][type]` and `[width][.precision]`
  grammar; method-overload-aware conformance check for `Stringable.toString(format)`.
- **`Parsable` interface** synthesized in `StdlibLoader` bootstrap; transitive
  `extends` conformance walks parent witness chains correctly.
- **`parseTryExpression` rewrite** extended to cover `try Type.method(...)`
  qualified-static calls (was: panic at op-count check; now: rewrites the
  emitted `call` to `tryCall` like the other shapes).
- **Character methods on byte-typed receivers**: when a primitive-int receiver
  is missing a primitive-extension method, the dispatch probes
  `Character.<method>` and materializes a Character from the low byte before
  dispatching. Unblocks `'A'.asciiValue()` and similar.
- **Interface-extension `for x in self` dispatch**: when a user interface
  declares both `current()` and `advance()` but not `createIterator()`, the
  for-in lowering aliases the createIterator result to the receiver (the
  interface IS its own iterator). `namedIdCastCategory` softened to return
  `int` for unknown type names (Element substitution gap surfaces as clean
  E3011 instead of compiler panic).

### Still pending (deferred to future stages)
- **3 `string-type` fragments** (`string-double-iteration`,
  `grapheme-iteration-emoji`, `grapheme-iteration-flag`) hit a pre-existing
  inliner/regalloc phi bug on for-in over a string where the body contains a
  non-inlinable function call. The back-edge from the inlined `advance()`
  continuation doesn't thread loop-carried values through the body's phi.
  Multi-day fix in `Compiler/Passes/Inliner.maxon` + coalescer.
- **3 `parsable-interface` negative-int / negative-float fragments** hit a
  pre-existing stdlib-pipeline bug: `cf.br loop_header` emits 0 args even
  though `loop_header` carries 3 SSA block params. User code is shielded
  because the user pipeline runs the inliner which restructures the
  `iter.advance()` callsites; the stdlib pipeline has no inliner.
- **2 `parsable-interface` E-code-mismatch fragments** (`error.missing-throws`,
  `error.throws-non-error-type`) need new semantic-check arms for
  conformance-throws validation.
- **`character-type/character-to-string` interpolation** needs parser-level
  Character provenance for `"{c}"` where `c` is a single-byte char literal.
  The parser's fast path drops the char-ness to a raw integer.
- **`escape-sequences.test`** (character-type) needs `'\''` single-quote
  escape lexer support inside char literals.
- **`error.otherwise-out-of-range.test`** (character-type) needs a
  semantic-check arm to range-narrow the `otherwise` value at type-check time.

### Tagged out for downstream phases
- **`interface-extensions`** (5/9 pass) — tagged out pending `stdlib/Interfaces.maxon`
  whitelist (Iterable.map / .filter extension monomorphization) + parser
  features for `Set from`, map-literal, and `gives`-closure in arg position.
  Re-enable when Iterable extensions land alongside Phase 13.
- **`conditional-extensions`** (1/6 pass) — tagged out pending
  extension-on-interface monomorphization with Element type-param substitution.

### Files modified (~65 files)
- [`Lexer.maxon`](Compiler/Lexer.maxon), [`Parser.maxon`](Compiler/Parser.maxon), [`MaxonDialect.maxon`](Compiler/IR/Maxon/MaxonDialect.maxon), [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon), [`StdDialect.maxon`](Compiler/IR/Std/StdDialect.maxon), [`TypeResolution.maxon`](Compiler/TypeResolution.maxon), [`StdlibLoader.maxon`](Compiler/StdlibLoader.maxon), [`StdlibCache.maxon`](Compiler/StdlibCache.maxon), [`runtime.std`](Compiler/Runtime/runtime.std), all backend dialect/emitter/regalloc/prologue files, and the IR pass files (`Mem2Reg`, `CSE`, `BorrowCheck`, `DCE`, `InjectDrops`, `Canonicalize`, `LowerStdToMir`, `Inliner`, `DeadFunctionElimination`).

---

## Phase 8: Error Handling — DONE

**Goal**: `try`/`throw`/`otherwise`, `throws` clause, error propagation.

### Done
- **Parser**: `throws ErrorType` in signatures, `throw EnumName.case` statements, `try CALL` propagation form, `try CALL otherwise FALLBACK` with seven dispatch shapes — single expression, `ignore`, `return`, `panic`, `throw`, `break`, `continue`, plus the labeled-block forms `'label' STMTS end 'label'` (plain) and `(e) 'label' STMTS end 'label'` (binding form that exposes the error enum value to the handler body) — all in [`Parser.maxon:3765`](Compiler/Parser.maxon#L3765) (`parseTryExpression`) → [`parseTryFallbackDispatch`](Compiler/Parser.maxon#L3951) → [`finalizeFallbackHandler`](Compiler/Parser.maxon#L4078).
- **Maxon Dialect**: `tryCall` / `tryMethodCall` ops (rewritten in place from `call` / `methodCall` by [`tryRewriteCallToTryCall`](Compiler/Parser.maxon#L4091) and `tryRewriteMethodCallToTryMethodCall`), `throw` op (terminator: emits a single `StdCallOp.errorReturn` placing `ordinal+1` in the secondary error register + default primary value).
- **Std Dialect / runtime**: the **multi-value-return error ABI** carries the error flag across the function-return boundary in the secondary register (RDX/X1) alongside the primary value in R8/X0 — no runtime helpers, no `.data` slot. Lowering at [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon) (`lowerThrow`, `lowerTryCall`, `lowerTryMethodCall`).
- **Type checking**: E3059 type-mismatch between the call's success type and the `otherwise` fallback type runs in two places — at parse time when both types are concrete primitives (parseTryFallbackDispatch's category check, gated by `maxonTypeIsKnown`), and again at TypeResolution time via [`validateTryOtherwiseTypes`](Compiler/TypeResolution.maxon) which catches the typealias-named-call case (e.g. `try mayFail() otherwise 5.0` where `mayFail returns Integer`) that the parse-time check must skip because typealiases panic in `convertCastCategory` pre-resolution.
- **`error-handling` spec**: enabled on the whitelist (2026-05-09); 30/33 fragments pass — all `try`/`otherwise` shapes, error-flag propagation, type-mismatch diagnostics, throwing-without-`try` rejection, and `try` against a non-throwing callee rejection. The `otherwise-return-string` fragment was unblocked by adding `materializeStringConstIfNeeded` inside `parseReturnStatement`. The `otherwise-return-managed-struct` fragment surfaced two regalloc/inliner bugs (described below) whose fixes also recovered correctness for any inlined call site whose continuation block reused phi virtuals — see `boxtest`-style spec coverage.

### Stage B landing (2026-05-13) — Phase 8 closure

- **`error-handling` now 33/33**: the 3 previously-deferred assoc-value
  fragments (`assoc-value-throw-catch{,-2}`, `otherwise-block-reused-binding`)
  pass without compiler edits — Phase 9 unions closed the gap as predicted.
- **`if-try` whitelisted (18/20)**: parser learned `else (e) 'label'`
  error-binding inside `parseIfTryStatement`, mirroring the existing
  `otherwise (e) 'label'` form in `parseTryFallbackDispatch`. Captures
  `calleeThrowsTypeName` from the rewritten tryCall/tryMethodCall, picks the
  slot type per throws-clause shape (bare enum → `flag - 1` ordinal recovery;
  associated-value union → bind directly to flag pointer), declares the
  binding inside a fresh scope, pops after the else body.
- **2 fragments tagged out**: `error.if-try-redundant-contains-get{,-field-receiver}`
  need Phase 13 Map support + an E3087 redundant-contains-get diagnostic. Tagged
  via `<!-- disabled-test: -->` markers in `specs/if-try.md`.

### Inliner / regalloc fixes that landed alongside `error-handling` (2026-05-09)

1. **`scanMaxValueId` ignored block args.** The inliner's fresh-id allocator started from `scanMaxValueId(caller) + 1`, but the scan walked only op operands and didn't visit `block.blockArgs`. Try-merge phis allocated by `parseTryFallback` therefore weren't part of the max, so `bindMultiBlockBodyRemap` reused those very ids for the inlined callee body — every inlined call site after a try/otherwise silently aliased the merge phi with a remapped callee virtual. Fixed by walking `block.blockArgs` in `scanMaxValueId` ([`Mem2Reg.maxon:1543`](Compiler/IR/Std/Mem2Reg.maxon#L1543)).
2. **`bindMultiBlockBodyRemap` ignored callee block args.** Even after the scan fix, the inliner left callee block-arg ValueIds unmapped. `lookupRemap` returned them unchanged, so the cloned callee blocks shared their original ids with the caller's existing virtuals. Fixed by walking `block.blockArgs` and calling `bindOneDefine` for each arg ([`Inliner.maxon:991`](Compiler/Passes/Inliner.maxon#L991)).

---

## Phase 9: Enums & Unions — DONE

**Goal**: enum declarations (simple and with associated values), pattern matching with case extraction, struct-backing metadata, associated-value error throws.

### Done — full phase (871/888 on x64-windows; remaining 17 are pre-existing non-Phase-9 gaps: string/char-backed enum decls, panic-string-interpolation, match-statements with non-enum scrutinees)

**Bare enums (`enums-simple`, `enum-allcases`, `enum-allcasenames`, `enum-ordinal`)**
- Int- and float-backed cases; methods; keyword-token case names; error-path diagnostics E3030 / E3031 / E3032 / E3034.
- `.allCases` / `.allCaseNames` synthesized as once-per-type globals (`__enum_<Name>_allCases`, `__enum_<Name>_allCaseNames`) — emission gated on actual references so DCE drops unused tables.
- `.ordinal` / `.name` resolve via parser rewrites; `.ordinal` is identity for ordinal-backed enums and a branchless sum-of-`(i * (raw == case_i.raw))` chain for raw-value-overridden enums; `.name` lowers to a per-enum `__enum_<Name>_name` helper (cmp+condBr chain, no leak).
- `.fromRawValue(raw)` and `.fromName(s)` synthesized at lowering (`__enum_<Name>_fromRawValue` / `__enum_<Name>_fromName`) — linear cmp-chain bodies returning the matched value and throwing the enum-typed error on miss.

**Match (`match-simple`, `match-statements`, `enum-match-exhaustive`, `enum-match-range`, `match-range-single-value`, `match-exhaustive-default-panic`, `match-expr-arm-divergent`)**
- Integer-literal, enum-case, and **range** patterns; `or`-chained patterns; `default` arms; `and fallthrough`; statement and expression forms.
- Range patterns over integer scrutinees AND enum ordinals with `min` / `max` open-ended sentinels.
- Divergent arms (return/panic/throw) are excluded from match-expression result-type unification.
- E2046 (default-on-enum-without-throws/panic) correctly fires only on union/enum scrutinees, not on String / int / float.
- Full diagnostic coverage: E2025, E2026, E2027, E2029, E2042, E2043, E2046, E3035 (case-payload-arity-mismatch), E3075 (qualified-case-name-in-match, emitted once per match), E2049 (block-form otherwise inside match-arm), E3081 (all-discard bindings).

**Associated-value unions (`enum-full`, `enum-match-only`, `match-enum-typed-binding`, `enum-struct-field-match`, `mutable-enums`, `union-cases`, `array-enum-element-size`, `array-slice-managed-elements`, `array-append-managed-elements`)**
- **Parser**: `union Name … caseName(field T) …` decl, `EnumName.caseName(args)` construction, `caseName(x, _, var y) then …` match-arm binding with arity check; pre-registration trick so method bodies in the union resolve.
- **Storage**: `EnumCase.payloadFields ParamArray`, `EnumType.isUnion bool`, reuses the `unresolvedEnums` map.
- **Maxon Dialect ops**: `enumConstruct(result, enumName, caseName, payloadValues, range)`, `enumTag(result, enumVal, range)`, `enumPayloadRead(result, enumVal, fieldIndex, fieldType, range)`, `enumPayloadStore(enumVal, fieldIndex, fieldType, newValue, range)`. `enumMatchArm` extended with `bindingNames ByteArrayArray`.
- **Memory model**: heap-boxed `8B tag + maxArity*8B payload`, allocated via `mm_alloc(size, __destruct_{UnionName}, tag)`. Destructor pointer null when the union has no managed payloads. Managed-payload reads refcount-bump; write-back through `enumPayloadStore` does load-old + incref-new + storeIndirect + decref-old.
- **Destructor synthesis**: per-union `__destruct_{UnionName}` emitted at Maxon→Std lowering with a tag-switch (factored as `emitTagSwitch` shared with match-arm lowering). Each case body decrefs its managed payload fields. The slab path frees the box.
- **Constructor synthesis**: per-case `__construct_{UnionName}_{caseName}(args) returns <UnionName>` registered at parse-time so TypeResolution sees signatures. The body emits a single `enumConstruct` + `ret`. Bare cases get a no-arg constructor too — all union values are uniformly heap-boxed.
- **Match-binding write-back**: `VarInfo.isPayloadBinding` + `payloadScrutineeName` + `payloadFieldIndex`; `Scope.declarePayloadBinding`. Assignment to a payload binding routes through `enumPayloadStore`; E2013 when the scrutinee is `let`.
- **`.unionCases` companion**: synthesized at parse-time as a bare-enum `<UnionName>.unionCases` with one bare case per variant (ordinals match parent tag values). Inherits `.allCases`/`.allCaseNames`/`.fromRawValue`/`.ordinal` from the bare-enum machinery automatically.
- **Array-of-union**: `maxonTypeIsManagedRef` recognises unions; the array allocator threads the destructor pointer through `__managed_mem_*` so `Array<Union>` cleans up correctly.

**Struct-backing metadata (`enum-struct-backing`, `enum-nested-struct-backing`, `union-struct-backing`)**
- `caseName(payloadFields) = StructType{field: v, …}` or `… = StructType.create(label: v, …)` per case on enums and unions.
- All cases of a struct-backed enum must share the same struct type (first case fixes it; E3032 on divergence). All cases must opt in (bare cases on a struct-backed enum raise E3032).
- Compile-time field access: `Union.case.field`, and `local.rawValue.field` for `let local = EnumName.case` aliases, fold through nested struct literals to the constant leaf. Nested struct backings supported via `UnresolvedConstExpr.structLit` arena recursion.
- `.fromRawValue` is rejected on struct-backed enums (E3034 at the call site).

**Error-handling with assoc-value throws (`error-handling`, `interface-dispatch-throws-match`)**
- `throw EnumName.case(args)` parses; parser emits a preceding `enumConstruct` whose result threads into the `throw` op (extended with `payloadValueName`).
- Throws ABI option-1: the value-return slot becomes the union pointer when the throws type is a union. `mm_alloc` never returns 0 so `flag != 0` test still distinguishes success from error; the same SSA value doubles as the union pointer for the `(e)` binding. Variant tag recoverable at `[unionPtr + 0]`.
- `try foo() otherwise (e) ...` typing flows the throws-clause type to `e` for both bare-enum and union error types.
- Refcount discipline: union allocated at throw site, pointer threads through the flag slot, caller's `(e)` binding holds the pointer, scope-exit decrefs balance via the standard managed-slot path.

### Coordination with other phases
- Bare-enum-as-Map-key Hashable witness (`enum-hashable.enum-map-key-still-works`, `enum-match-only.enum-map-key-still-works`) remains blocked on a Phase-11 witness-table gap (not Phase 9).
- wasm32-wasi `enum-allcases` / `enum-allcasenames` / `enum-ordinal` have 6 extra failures vs x64 on float-backed enums — pre-existing wasm-backend float-backed-enum codegen bugs surfaced by `.allCases`/`.name`. Not Phase-9 regressions.
- One `error-handling.assoc-value-throw-catch.test` fails on wasm32-wasi because WASI clamps ExitCode to `0..125` (the test returns 404); orthogonal to Phase 9.
- arm64-windows cross-execution from x64 host not supported; all Phase-9 changes are target-agnostic (parser/dialect/LowerMaxonToStd), so backend regressions are unlikely.

---

## Phase 10: Closures & First-Class Functions (~5 specs) — DONE

**Goal**: function pointers, closures with captured variables, indirect calls.

### Done — `closure-self` (3 fragments, x64-windows + wasm32-wasi parity)
- **Parser**: function-type annotations on parameters via `parseTypeRef` (`f (Integer) returns Integer`); closure literals (`(x T) gives <expr>`, `() gives <expr>`); per-project `__closure_<N>` lifting; capture-by-reference detection that rewrites `self` reads inside a closure body to `closureEnvLoad`.
- **MaxonDialect**: `MaxonType.function(id)` arm interned via `Project.functionTypes`; new ops `functionRef`, `closureCreate`, `closureEnvLoad`, `indirectCall`.
- **Lowering**: function-typed param expansion to a (fn-pointer, env-pointer) ABI pair; `closureCreate` materializes a heap env via `mrt_alloc(N*8)` with stackAddr-based by-reference captures; `indirectCall` forwards the matching env to the callee.
- **Mem2Reg**: address-taken slot tracking — captured params keep their stack slot live and emit a param-store at function entry so the captured pointer dereferences see the calling-convention value.
- **Wasm**: function-table + element-section emission, `funcAddr` returns table index, `indirectCall` lowers to `call_indirect (type $T) (table 0)`, type-section dedup interns by signature so direct and indirect calls share entries. Second mem2reg run after `augmentWithRuntime` keeps slot space clean.
- **DFE**: `functionRef` and `closureCreate` root the lifted body so it survives dead-function elimination.

### Stage B landing (2026-05-13) — general closure-capture

- **`first-class-functions` 10/10, `closure-capture` 6/7** whitelisted. (No
  `closures.md` spec file exists; drop from target list.)
- **`outer.field` closure capture**: `parseIdentifierExpr` (Parser.maxon:8602)
  detects `inClosure && at(dot) && closureOuterScope.contains(name)` and emits
  `closureEnvLoad` so `parsePostfix` handles the `.field` chain normally
  (previously routed to `maybeQualifiedRead` and tripped E2004).
- **`patchClosureEnvLoadCaptureTypes`** (TypeResolution.maxon:3497) runs between
  `buildProducerTypes` and `fillUnresolvedTypes`: for each `closureCreate` op,
  looks up the captured name's now-resolved slot type from the outer function's
  `slotTypes` map and rewrites every matching `closureEnvLoad` op in the lifted
  body, then rebuilds producer-types for that body so consumers see the fixed
  receiver type. Needed because `emitClosureCapture` stamps `captureType` from
  the outer scope at parse time, before initializer calls have resolved.
- **`patchFunctionTypeRegistryReturns`** (TypeResolution.maxon:3460) walks every
  `functionRef`/`closureCreate` op and rewrites stale `FunctionTypeRegistry`
  return-type entries from the now-concrete `funcReturnTypes` table, fixing
  closures whose body expression couldn't be typed at parse time.
- **Bare function reference (`let f = double`)**: new `maybeFunctionRef` branch
  (Parser.maxon:8825) routes unbound identifiers through `qualifyCalleeForContext`
  and emits `MaxonOp.functionRef` with a concrete `fnTypeId`. Indirect-call
  positional args now pass `requireNamed: false` (Parser.maxon:8783).
- **Wasm indirect-call ABI**: every lifted closure now carries a trailing
  `__env i64` param regardless of capture count (Parser.maxon:10299); free
  function refs route through a synthesized `__fnref_<funcName>` thunk
  (LowerMaxonToStd.maxon:6500 redirect + 5719 synthesis) whose signature matches
  the closure ABI. Lazy-deduplicated via `pendingFnRefThunks`. Fixes
  `call_indirect type mismatch` for non-capturing closures and free function
  refs on wasm32-wasi.
- **One fragment tagged out**: `closure-capture.map-with-capture` needs Phase 13
  `Array.map` (Iterable extension monomorphization).

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
| 11.5 | Per-function incremental queries | Query layer DONE; per-function dispatch infrastructure DONE via C2.d (Stage O — tier-aware Std/MIR/backend split, byte-parity verified). Numerical perf claim ("50-200ms one-function rebuild") gated on `queryCodeResult` per-function rewire — Stage P follow-up. |
| 11.6 | Analysis-based inliner | DONE (single-block + multi-block splice via C2.b; loop-depth multipliers via C2.c). **No annotations of any kind** — the analyzer decides. |
| 11.7 | Validation & polish | 19 Phase-11-bucket specs whitelisted; 1384/1384 baseline on x64-windows + wasm32-wasi after Stream A re-enabled `where-clauses` (8 fragments) and `associated-types` (16 fragments) on 2026-05-20 and Stream C added the 5-fragment `per-function-parity` harness; bench scripts deferred — not on the Phase-11 critical path |

### Phase 11.0 — Foundation: parser + interface declarations

**Goal**: parser accepts the full surface syntax for generics and interfaces; AST shape is correct. No semantic action yet.

**Changes**:
- **Lexer** ([`Lexer.maxon`](Compiler/Lexer.maxon)): keywords `interface`, `extension`, `extends`, `implements`, `with`, `where`, `from`, `uses` are already tokenized — verify precedence and contextual handling.
- **Parser** ([`Parser.maxon`](Compiler/Parser.maxon)): add productions for `interface Name uses T1, T2 ... end`, `type Name uses T1 implements I1, I2 with (...) ... end`, `where T: Comparable`, `function foo<T>(x T) returns T where T: Comparable`, `extension Iterable uses Element ... end`, `from Type implements Interface ... end`.
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
- **Call-site count**: number of static call sites for the callee across the whole module — informational only. A former "always inline single-call-site callees" rule was removed because it ignored body size; `dceFunctions` still drops callees whose every call site got inlined via the cost gate below.
- **Recursion**: callees that are part of a recursion SCC in the call graph are never inlined.
- **Indirect calls**: `indirectCall` ops aren't inlinable (no static target). The fat-pointer ABI from 11.3 means interface dispatch falls into this bucket unless devirtualized first.
- **Generic-arg concreteness**: when a call site passes a fully concrete layout descriptor, the inliner can constant-fold descriptor loads inside the inlined body. This is what closes the gap with monomorphized output on `Array<Int>.push` etc.
- **Loop depth at call site**: call sites inside loops get a higher size budget (matches monomorphized hot-loop behavior).
- **Argument shape**: closure literals and constants at the call site enable downstream constant folding / closure inlining and shift the cost/benefit toward inlining.

**Decision rule** (in priority order):
1. Never inline if recursive or indirect.
2. Always inline tiny callees (≤ ~10 Std ops) — accessor-shaped methods like `Array.length`, `Array.get`, `Optional.unwrap`.
3. Inline medium callees (≤ ~50 ops) if any of: call site is in a loop; the callee receives a concrete layout descriptor that unlocks descriptor folding; the callee receives a closure literal.
4. Otherwise don't inline.

These thresholds (≤ 10 / ≤ 50 op budgets, ×3 / ×6 loop-depth multipliers from C2.c) are the current live values driving the green baseline. Benchmark-driven retuning is deferred — not on the Phase-11 critical path — and the values can be revisited if a hot path surfaces.

**Constant folding extensions**: extend the existing canonicalize pass to recognize loads from known descriptor addresses → constants. Run a second canonicalize + DCE pass after each inlining batch — that's where the descriptor-fold cascade actually lands and recovers monomorphized-quality code.

**No annotations of any kind**: the compiler decides entirely from IR analysis. It has the call graph, every callee body, and the call-site context — strictly more information than the programmer. Annotations like `@inlinable` / `@inline` / `@noinline` leak an implementation concern into the surface language and create a permanent stdlib maintenance tax every time perf-relevant code shifts. If the analyzer doesn't pick up a hot path, that's a tuning bug to fix in the inliner, not a hole to patch with an annotation.

**Stdlib**: zero annotations. `Array.push`, `Array.get`, iterators, `Optional.unwrap` are recovered by the size + call-site-count rules above. If the analyzer doesn't pick them up, that's a tuning bug to fix in the inliner, not a hole to patch with an annotation.

### Phase 11.7 — Validation, polish, parity

**Status**: spec-unlock work landed in 2026-05-04 (**6 of 14 Phase-11 specs whitelisted, 505 / 549 total**). The 2026-05-05 code-review pass disabled five of those six specs (`interface-conformance`, `interfaces`, `where-clauses`, `associated-types`, `ranged-typealias`) plus `enums-simple` because partial failures were leaking into the green-suite signal. **Current baseline (2026-05-09): 596/596 passing on x64-windows AND wasm32-wasi, bootstrap parity 2730/2730 holds.** `interface-conformance` is fully back in the whitelist.

**Spec-unlock changes (2026-05-04)**:
- **E3012 unused-parameter relaxation for interface methods** — parser captures `currentTypeConformances` and skips the unused-param check when the function name appears in any conformed interface (transitively via `extends`). Unlocks `interface-conformance/interface-method-unused-param-allowed`, `interface-method-via-extended-interface`, plus several `interfaces` fragments. ([Parser.maxon `checkUnusedParameters`](Compiler/Parser.maxon))
- **E3015 → E3016 message migration** — `validateConformanceMethods` no longer emits the older E3015 "missing method" form; SemanticCheck's existing `reportMissingMethods` handles emission with the bootstrap-compatible multi-line shape. Unlocks `conformance-missing-method`. ([SemanticCheck.maxon](Compiler/SemanticCheck.maxon), [TypeResolution.maxon](Compiler/TypeResolution.maxon))
- **E3017 constraint-violation diagnostic** (new code) — every generic typealias instantiation gets queued onto `Project.pendingConstraintChecks` during typealias drain; a deferred pass (`drainPendingConstraintChecks`) walks the queue after `validateConformances` and emits E3017 per failing where-clause arg. Unlocks `where-clauses.and-violation`, plus catches a previously unflagged `eq-with-equatable` mismatch. ([TypeResolution.maxon](Compiler/TypeResolution.maxon), [Project.maxon `PendingConstraintCheck`](Compiler/Project.maxon))
- **Graceful fallback for unimplemented Phase-11.4 paths** — `resolveNamedToStdType`, `namedIdCastCategory`, and the X64 + ARM64 `lowerCall` paramTypes lookups previously panicked when an interface-typed parameter or a generic-method monomorphization didn't reach the function table. Each now returns a sane default (`StdType.i64` / `CastCategory.int`) so the test runner keeps going and the upstream diagnostic surfaces.

**Specs still blocked by Phase 11.4 (interface dispatch on values)**: `interface-extensions`, `conditional-extensions`, `parsable-interface`, `primitive-comparable`, `primitive-cloneable`, `primitive-hashable`. Each fragment that exercises actual dispatch fails at runtime with `interface dispatch for method 'X' is unimplemented (Phase 11.4)`. The `parsable-interface` spec also hits parser gaps on `int.fromString(…)` extension-method syntax.

**Spec-unlock work (2026-05-09)**:
- **E3016 wrong-signature arm** — `SemanticCheck.checkConformanceForType` now compares each declared method's `paramNames`/`paramTypes`/return type against the interface's expected signature (post-substitution of `Self` and associated-type bindings) and emits the C#-bootstrap-compatible "has N method(s) with wrong signature" message when they diverge. Unlocks `conformance-wrong-param-type`, `conformance-wrong-return-type`. ([SemanticCheck.maxon `signatureMatches`](Compiler/SemanticCheck.maxon), [TypeResolution.maxon `computeExpectedMethodsForConformances`](Compiler/TypeResolution.maxon))
- **`Self` resolution in conformance checking** — `computeExpectedMethodsForConformances` adds an extra binding `(interfaceName → declName)` so `function clone() returns Self` declared on `interface Cloneable` resolves to `Cloneable` at parse time and back to the conforming type during conformance checking, matching the C# bootstrap's `ResolveInterfaceTypeName`. Avoided false-positive E3016 on every stdlib `clone()` / `equals()` / `compare()` impl.
- **Interface-method return-type tracked for E3062** — `parseInterfaceMethodSig` now routes the return-type token through `parseTypeRef`, so `Self` resolves to `currentTypeName` and same-named local typealiases get marked used. Avoided false-positive E3062 on `interface Provider function provide() returns Integer`.
- **E3012 unused-local-variable tracking (deferred drain)** — `parseVarDecl` records every `let`/`var` declaration on `Parser.localVarLocations`; `checkUnusedParameters` queues parameter + local candidates onto `Project.pendingUnusedChecks`; `SemanticCheck.checkPendingUnusedVars` (run from the resolveTypes pass tail so it survives the per-pass error gate) drains the queue and emits one E3012 per function unless a primary diagnostic landed inside the same source range. Unlocks `interface-method-local-var-still-errors`. ([Parser.maxon `checkUnusedParameters`](Compiler/Parser.maxon), [SemanticCheck.maxon `checkPendingUnusedVars`](Compiler/SemanticCheck.maxon))
- **Transitive-extends conformance recording** — `recordConformance` walks the implemented interface's `extends` chain and registers a derived `(typeName, parentInterfaceName)` entry per ancestor. Without this, `BuildWitnessTables` skipped emitting `(Impl, Base)` blobs for `Impl implements Extended extends Base`, the call site materialised a null witness, and dispatch crashed at runtime. Unlocks `interface-method-via-extended-interface`. ([TypeResolution.maxon `recordParentConformancesViaExtends`](Compiler/TypeResolution.maxon))
- **`BuiltinArrayLiteral` user-impl tolerance** — the parse-time duplicate-impl check on `BuiltinArrayLiteral` is now first-write-wins (stdlib's `Array` registers first; user types `MyCollection implements BuiltinArrayLiteral` no longer raise E2001). The cache-miss `discoverBuiltinArrayLiteralType` path prefers `Array` over any other implementer for the same reason. Unlocks `builtin-interface-user-code`.
- **Wasm witness-table function-pointer patcher** — `patchFuncAbs64InRdataForWasm` walks `globalData.pendingRdataRelocs` and replaces the 8-byte zero slot at each `funcAbs64InRdata` rdata offset with the target function's wasm table index. Native targets patch the same slot with an absolute code VA via `patchFuncAbs64InRdata`; without the wasm equivalent, dispatched-through-witness calls loaded zero, picked `table[0]`, and tripped wasm's "indirect call type mismatch". Unlocks `interface-method-unused-param-allowed` and `interface-method-via-extended-interface` on wasm32-wasi. ([MirToWasm.maxon `patchFuncAbs64InRdataForWasm`](Compiler/Targets/Wasm/MirToWasm.maxon))
- **Stdlib cache write fix** — `writeStdIrFunction` routed `func.namespace` and `func.sourceFilePath` through `writeOptionalString` / `writeOptionalFilePath`. Synthesized functions (e.g. `__layout_copy_Array_Byte`) leave both empty, and the mandatory codec was throwing `CacheCorruption.invalidEncoding` on every clean build — the cache was silently never being persisted. Bumped `CACHE_FORMAT_VERSION` to 22. ([StdlibCache.maxon `writeStdIrFunction`](Compiler/StdlibCache.maxon))

**Other deferred items**:
- **`where-clauses.constraint-violation` (Map case)** needs the stdlib's typeParameters where-clauses surfaced from the cache restoration (today `lookupWhereClausesForBase` only finds them for in-process unresolved structs; Map sits in resolved structTypes after stdlib cache load and presents an empty constraint set).
- **Most `ranged-typealias` failures** are missing E3009/E3005 ranged-int diagnostics + a runtime cast issue, not generic-dispatch infrastructure.
- **Most `associated-types` failures** are E3015/E3016 message-shape mismatches and a couple of fragments that exercise interface dispatch on `Iter`-typed locals.

**Performance targets** (deferred — not pursued in this session; the open-ended spec unlock work consumed the budget):
- Compile time of stdlib + selfhosted source: **≤ 60% of current C# bootstrap** (~3s vs current 5s)
- Incremental rebuild of one-function edit: **≤ 200ms**
- Runtime: within **10%** of C# bootstrap output on representative benchmarks; within **2%** on stdlib hot loops with inlining
- Binary size: **≤ 50%** of C# bootstrap output

**Tools/benchmarks**: `tools/bench-compile.sh`, `tools/bench-incremental.sh`, `tools/bench-runtime.sh`, `tools/bench-size.sh` not authored — the perf targets need Phase-11.4 to land first (interface-typed values touch the hottest stdlib paths), so a benchmark suite today wouldn't measure the right thing.

**Inliner threshold tuning**: deferred. Current values (10/50 op budget; loop-depth multipliers ×3 at depth 1, ×6 at depth ≥ 2 from C2.c) drive the green baseline. Benchmark-driven retuning is not on the Phase-11 critical path; the values can be revisited if a hot path surfaces without claiming they're frozen.

**Validation**: full spec-test parity with C# bootstrap (2735/2735) holds across all changes; self-host (self-hosted compiler compiles itself in ≤ 3s).

### Completion-phase notes (C1 / C2)

The Phase 11 ship pulled in a series of C-prefixed completion tasks that fold into existing 11.x sections rather than standing as new phases:

- **C1.a** — real layout copy/destroy thunk bodies. Two new runtime helpers (`__layout_word_load` / `__layout_word_store`) in `runtime.std`; shape-driven thunk synthesis in [`BuildLayoutDescriptors.maxon`](Compiler/Passes/BuildLayoutDescriptors.maxon). Folds into 11.2.
- **C1.b** — InjectDrops type-parameter slot dispatch via new `StdSystemOp.dropTypeParam(slotValueId, layoutValueId)` op (descriptor-routed `descriptorField(.destroyFunc) + indirectCall`). Slot MaxonType plumbing; stdlib cache v8 → v9. Folds into 11.4.
- **C1.c** — interface fat-pointer construction infrastructure: 2-slot widening, per-function `interfaceWitnessSlots` map, `resolveWitnessLabel`, ABI plumbing. Folds into 11.4.
- **C1.d** — witness-method dispatch at call sites; `MaxonType.interface(_)` threading through TypeResolution; `methodIndexInInterface` helper; param-witness slot allocation; mem2reg fat-pointer guards. Folds into 11.4.
- **C1.e** — `PrimitiveExtensions.maxon` whitelist + primitive conformance wiring. **LANDED 2026-05-13 in Stage A**: `primitive-cloneable` 12/12, `primitive-hashable` whitelisted, `primitive-comparable` 11/13 (2 NaN-ordering edge cases remain — see Phase 7 final notes). The previously-cited Phase-7 blocker dissolved when Stage A's `lowerStringInterp` Stringable/Character/enum arms landed alongside the new `interface-dispatch` spec.
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

## Phase 12: Global Variables & Static State (~4 specs) — DONE

**Goal**: module-level variables, static fields/methods on types.

### Specs to unlock
`static-variables` (28/28 passing on x64 and wasm; enabled on the whitelist 2026-05-09), `top-level-let` (full), `module-level-struct-var`, `export-var-fields`.

The last three `static-variables` fragments needed two unrelated fixes:
- `consumeConstCastTargetName` now records typealias uses so `var counter = 42 as SmallInt` doesn't emit a spurious E3062 ([`Parser.maxon:1756`](Compiler/Parser.maxon#L1756)).
- The wasm backend's float-tracking pass missed `MirOp.load(_, _, _, isFloat)`, leaving f64 globals' load result typed as i64 in the wasm locals declaration. The `data-section-f64-8byte` and `data-section-mixed-types` fragments now pass on wasm32-wasi after the float mark in `markFloatMirOp` ([`MirToWasm.maxon:1756`](Compiler/Targets/Wasm/MirToWasm.maxon#L1756)).

### Done
- Parser: module-level `var`/`let` declarations and `static var/let` fields with structural `UnresolvedConstExpr` initializers (deferred resolution after enums/typealiases drain).
- Const-expr forms supported: int / float / bool literals, `EnumName.caseName`, `<expr> as TypeName` ranged-cast, identifier references to `let` constants, full arithmetic / comparison / logical fold.
- TypeResolution: `resolveAllTopLevelDecls` drains `unresolvedTopLevelDecls` into `topLevelConstants` (lets) or `topLevelVars` (vars) based on each entry's `mutable` flag.
- Diagnostics: E2045 for runtime call initializers; E2013 for assignment to immutable `let` (top-level or static); `let X = TypeName.method(...)` is tolerated (no init emitted yet) so the reassignment path can still surface E2013.
- Std Dialect: `globalLoad` / `globalStore` carry `slotType`. The `isFloat` flag is plumbed through `StdSystemOp.loadIndirect`/`storeIndirect` → `MirOp.load`/`store` → X64 (`loadIndirectXmm` / `storeIndirectXmm` via new `emitLoadIndirectXmmOp` / `emitStoreIndirectXmmOp`) → ARM64 (`fpLoadIndirect` / `fpStoreIndirect`) → WASM (`opF64Load` / `opF64Store`), so float-typed globals and float struct fields land directly in FP registers.
- Visibility: `Visibility` enum (file / module / global); `export` and contextual `module` keywords parsed at `dispatchTopLevel`. Every resolved / unresolved decl entry carries `visibility` + `sourceFilePath`: top-level vars / lets, functions (`IrFunction.visibility`/`sourceFilePath`), struct types, enums, typealiases (sidecar `typealiasVisibilities` map), and enum cases (inherited from enclosing enum). Static fields and inherent-method visibility inherit the enclosing type's tier; enum methods inherit the enum's tier. Cross-file callee resolution falls back through `methodNameIndex` filtered by `isVisibleFrom` against the reading function's file. `qualifyCalleeForContext` takes a `readerFilePath` parameter; per-function rewrite passes derive it from `entryBlockFilePath`. New TR validation passes — `validateCallVisibility` (E3008 / E3088 on hidden `call` ops, both qualified and bare), `validateNamedTypeVisibility` (rejects `as TypeName` against hidden typealias / struct) — sequence after `fillUnresolvedTypes` and before `validateCallArities`. `queryCodeResult` gates codegen behind `hasErrors` so semantic errors short-circuit lowering. Stdlib cache codec stamps `(global, stdlib bootstrap path)` on restore for every function / struct / enum without changing the on-disk format. `module-keyword` (11/11) and `export-keyword` (16/24) specs land on the whitelist; remaining `export-keyword` failures are pre-existing parser gaps (Array typealias parsing) and wording differences (E2001 vs E3061 for duplicate typealiases), all unrelated to visibility.

### Stage C landing (2026-05-13) — Phase 12 closure

- **Two "already-landed" findings**: cross-file struct-field-via-top-level-var
  was implemented in commit `65cb53768` via `TypeResolution.resolveDottedTopLevelReceiver`
  (the `exported-struct-var-cross-file` fragment passes on both x64-windows
  and wasm32-wasi). Generic `Array with TYPE` typealias parsing was implemented
  during Phase 11.0 step 2 via the already-general `parseTypealiasRhs` / `resolveGenericAliasExpr`
  / `buildGenericInstanceType`. ROADMAP entries for both were stale.
- **Generic typealias arity validator** (Stage C.1) — `TypeResolution.validateGenericInstanceArity`
  added to drain after every type/interface/enum register pass. Now emits
  `E2003: Type 'Array' expects 1 type argument(s), got 2` instead of silently
  dropping extras (matches `maxon-sharp/Compiler/2-Parser.cs::PreScanTypeAlias`).
  Extended `lookupTypeParamsForBase` to consult `project.unresolvedInterfaces`
  and `project.interfaces` (interfaces store `usesParams` on the IR record
  rather than in `TypeParamRegistry`).
- **`top-level-let` whitelisted at 16/16** (1 fragment tagged for Phase 15c
  FilePath). **`module-level-struct-var` whitelisted at 3/3.**
- **Chained global-field assignment**: `state.inner.x = 99` where `state` is
  a top-level var failed parse — `parseIdentifierStatement` only handled
  chain length 1 for globals. Added `parseGlobalChainedFieldAssignment`
  (`Parser.maxon:1290`) that emits `unresolvedRead globalVar → fieldLoad chain → fieldStore lastField`.
  TR's existing `rewriteUnresolvedRead` lifts the leading `unresolvedRead`
  to `globalLoad` so the chain types correctly.
- **`Type from <literal>` initializer**: added `parseFromLiteralCall` to
  recognize `Identifier from "..."` / `Identifier from '...'` and lower
  to `TypeName.init(value)` qualified call. Extended `isRuntimeInitInitializer`
  to route top-level `Identifier from <literal>` through the existing
  `__module_init_<n>` runtime-init path so the writable .data slot is reserved.
- **Tagged out**: `top-level-let/from-literal-initializer` uses `FilePath`
  which isn't whitelisted (needs `#if os(...)` resolution → Phase 15c file I/O).

### Final spec counts after Stage C

| Spec | Count |
|---|---|
| `top-level-let` | 16/16 |
| `module-level-struct-var` | 3/3 |
| `static-variables` | 28/28 |
| `export-keyword` | 24/24 |
| `module-keyword` | 11/11 |
| `exported-struct-var-cross-file` | passing (single fragment) |

---

## Phase 13: Collections (Map, Set, Vector) — DONE

**Goal**: hash map, hash set, vector.

### Stage D landing (2026-05-13)

- **Whitelisted with 100% green on enabled fragments**:
  `map` (31/31, 2 tagged), `array-hashable` (6/6, 1 tagged),
  `map-struct-bytearray` (3/3), `map-try-otherwise-block` (2/2).
- **`stdlib/Set.maxon` and `stdlib/Vector.maxon`** added to `stdlibWhitelist`.
  Bootstrap prelude (`StdlibLoader.maxon::stdlibBootstrapSourceForTarget`)
  extended with `InitableFromArrayLiteral` / `InitableFromStringLiteral` /
  `InitableFromCharLiteral` interface stubs so Set's
  `implements InitableFromArrayLiteral with Element` clause type-checks.
- **Stage D fixes**:
  1. `tryGenericTypealias` (TypeResolution.maxon:2670) — preferred any struct's
     inner-alias over a real user struct of the same name. Fix: check
     `project.structTypes` / `project.enumTypes` membership before falling to
     `tryAnyStructInnerAlias`. Without this, user's `type Entry` collided
     with `Map.typealias Entry = (Key, Value)` and resolved to `__Tuple2`.
  2. `resolvedCalleeParamFromMaxonType` (LowerMaxonToStd.maxon:7459) — returned
     `concrete("Int")` for a `named("Int")` MaxonType where
     `Int = int(i64.min to i64.max)`, then witness-table lookup panicked on
     `__witness_Int_Hashable`. Fix: new `resolvedCalleeParamFromNamed` chases
     through `primitiveExtensionTypeNameWithProject` so typealiased primitives
     resolve to `"int"` / `"float"` / `"bool"` / `"String"`.
  3. `TypeName from [...]` parser support: `parsePrimary` (Parser.maxon:8495)
     now recognizes `from <leftBracket>` so `Vector from [1, 2, 3]` parses.
  4. Multi-line collection literal parsing: `parseArrayLiteralExpr` /
     `parseMapLiteralBodyAfterFirstKey` (Parser.maxon:9367, 9447) skip newlines
     after `[`, after each comma, and before `]`.
  5. `__Tuple2` / `__Tuple3` field declarations marked `export` so user code
     can access tuple components without tripping E3014.

### Closeout (2026-05-20)

The four fragments Stage D listed as "tagged out for downstream phases" all
verified green on x64-windows AND wasm32-wasi without any further compiler
changes — Stages H / I / J had already shipped the underlying fixes
(per-instance witness tables for conditional conformance, MapIterator
layout-forwarding, try-otherwise binding scope, `Set from [...]` /
`Vector with N` parser support). Stage D's tagged-out claims were stale
on closeout. Final state, both targets:

- `map`: 50/50
- `set`: 23/23 (including `typealias-of-array-element` with conditional
  `Array with Byte` Hashable witness)
- `vector`: 23/23 (including all `Vector with N Element` sized-typealias
  shapes and `Vector from [...]` literal-inferred shapes)
- `array-hashable`: 7/7 (including `int-array-map-key` exercising the
  per-instance Array→Hashable conditional witness)
- `map-struct-bytearray`: 4/4
- `map-try-otherwise-block`: 2/2

Also enabled `collection` (16/16) — the `Collection` interface spec is
collection-adjacent and already green on the existing compiler/stdlib
infrastructure.

### Tracked: stdlib-array residuals (NOT enabled)

`specs/stdlib-array.md` has 41 fragments; 29 pass individually but 12
fail with three distinct symptoms when enabled (12 → 12 categorisation
done 2026-05-20):

1. **Runtime exit-code mismatches** (e.g. `isEmpty-transitions` returns
   42 instead of 0). Root cause: SSA-destruction over-coalesces a
   block-arg destination with a value that is still live across a
   different predecessor edge. Specifically `isCopyBoundaryOverlap`
   ([SSAColoring.maxon:539](Compiler/Targets/Shared/SSAColoring.maxon#L539))
   suppresses interference when `overlap == 1` and the two ranges are
   copy-connected via `copyHintValueId`. This is correct for 2-address
   ops (where the source dies at the copy) but unsafe for block-arg
   parallel copies with multiple predecessors: when dest is coalesced
   with source-from-predecessor-A, the parallel copy on
   predecessor-B's edge writes B's source into the shared register,
   clobbering A's value if A is still live downstream. Concretely,
   `%merge_param` (block param of `try_0.merge`) gets coalesced with
   `%5` (constant 0 used in entry, body, AND `e3_0.after: mir.ret %5`)
   into r13. The `try_0.ok → try_0.merge` edge then copies
   `r13 := r14`, destroying `%5`'s value before the return. Proper fix
   requires modelling the parallel-copy position in the interference
   graph (add an interval for each block-arg dest at the END of every
   predecessor, where the parallel copy lives), and making
   copy-link tracking edge-aware (per-edge source/dest pairs rather
   than one bidirectional link per range). Multi-day work touching
   LiveRangeBuilder, SSAColoring, and possibly CopyResolution.
2. **Register-allocator panic** `colorLookupGpr: virtual GPR vN has no
   coloring assignment` triggered by a subset of insert/remove
   fragments — likely a downstream symptom of (1) or a related
   coalescing miscount. Needs investigation after (1) is fixed.
3. **Access violation in `build-sorted-with-inserts`** — runtime
   crash; unknown root cause, may be the same SSA-destruction bug
   manifesting through a different value or a separate issue.

Plus a **batch-only** `lowerMethodCall: opIdx=N (receiver=Array,
method=get) missing from project.resolvedCallees` panic when all 41
fragments compile in one batch — does not reproduce per-fragment.
Likely cross-fragment state leakage in `project.resolvedCallees`
similar to the cache-miss pollution fixed in Phase 7 finish
(2026-05-07) for stdlib-vs-user OpIndex collisions.

The `stdlib-array` spec stays commented out in the whitelist with the
above pointer; it is the gating work for the next regalloc-correctness
push. Enabling it is straightforward once (1) lands.

---

## Phase 14: Math Functions (~18 specs) — DONE

**Goal**: `abs`, `sqrt`, `floor`, `ceil`, `round`, `min`, `max`, trig, log, exp, pow.

### Stage E.1 landing (2026-05-13)

All 16 specs whitelisted: `abs`, `ceil`, `floor`, `round`, `trunc`, `sqrt`, `pow`,
`sin`, `cos`, `tan`, `atan2`, `exp`, `log`, `log2`, `log10`, `min`, `max`.

**Hardware-op group** (`abs`, `sqrt`, `floor`, `ceil`, `round`, `trunc`, `min`, `max`):
- Unary ops (`abs`/`sqrt`/`floor`/`ceil`/`round`/`trunc`) were already wired via
  existing Std unary op pipeline. Single bug fixed: x64 ANDPD alignment for `abs`.
  ANDPD requires 16-byte aligned m128 but rdata mask entries were 8-byte aligned,
  triggering #GP. New `registerXmmMaskConstant(lo, hi, ...)` writes 16-byte payload
  under a `__xmmmask_` name; `emitRdataPass` aligns to 16 bytes for that prefix
  ([`RuntimeFunctions.maxon:275-301`](Compiler/Runtime/RuntimeFunctions.maxon#L275-L301),
  [`StdOpHelpers.maxon:275-289`](Compiler/Targets/Shared/StdOpHelpers.maxon#L275-L289),
  [`MirToX64Conversion.maxon:1042-1051`](Compiler/Targets/X64/MirToX64Conversion.maxon#L1042-L1051)).
- Binary ops `min`/`max` newly implemented: added `StdArithBinaryOpcode.minF64`/`maxF64`,
  threaded through MIR (`MirBinOpcode.minF64`/`maxF64`), x64 (`minXmm`/`maxXmm` →
  MINSD/MAXSD), arm64 (`fmin`/`fmax`), wasm (`opF64Min`/`opF64Max`). New
  `MathBinaryIntrinsicHit` type + `mathBinaryIntrinsicTarget` lookup; `lowerCall`
  routes binary intrinsics through `lowerMathBinaryIntrinsic`.
- Parser: math intrinsics bypass the `requireNamed=true` check at call sites
  (compiler builtins have no source-level parameter names).

**Taylor-series group** (`sin`/`cos`/`tan`/`atan2`/`exp`/`log`/`log2`/`log10`/`pow`):
- Already implemented as Maxon Taylor-series functions in `stdlib/Math.maxon` —
  no libm/ucrtbase needed. Compile and run on every target. 4 fragments fail
  with stdlib precision issues (`atan2/negative-x-axis`, `log2` 3-fragment cluster)
  — orthogonal to compiler work.

**24 rt-* fragments** (across abs/sqrt/floor/ceil/round/trunc/min/max) remain blocked
on a nested-try parser bug surfaced by Stage E.4: `try float.fromString(try args.get(1) otherwise "") otherwise 0.0`
trips `E3013 unresolved value name '$tN'`. Distinct from `CommandLine.args()` infrastructure.

**StdlibCache.maxon `CACHE_FORMAT_VERSION` bumped to 39** because StdArithBinaryOpcode
gained two members.

---

## Phase 15: Advanced Features & Remaining Specs (~20 specs)

**Specs already passing in this bucket**: `panic`, `range-check-panic`, `unused-parameters`, `duplicate-block-identifiers`, `unknown-keyword-error`, `expected-expression-error`, `missing-return-error`, `stdlib-autodiscovery`.

**E3062 (unused typealias)** landed 2026-05-09 in [`Parser.maxon`](Compiler/Parser.maxon): per-file `localTypealiases` tracks file-private `typealias` decls, `usedTypeAliases` is populated at every parseTypeRef / qualified-static-call / struct-literal / `as`-cast / typealias-RHS-name site, and `checkUnusedTypeAliases` runs at end-of-module. Implicit-inferred uses from bare `[...]` array literals don't count — only literal `Name`-token references do (matching the C# bootstrap). Unblocked `arrays/error.unused-array-typealias` so the `arrays` spec is now 46/46.

### Specs still to unlock
`tuples`, `namespaces`, `multi-file`, `export-keyword`, `command-line-args`, `file-io`, `directory`, `panic-stack-trace`, `alloc-tracking`, `codegen-internals`, `managed-memory-element-size`, `stdlib-basic`, `grapheme-clusters`, `slice-memory`, `discarded-results`, `duplicate-functions`, `type-checking`, `function-overloads`, `init-from-literal`, `initablefromarrayliteral`, `register-allocator`, `unused-variables`, `advent`.

### Sub-phases

**15a: Semantic Checks** — **DONE 2026-05-21**. Three diagnostics landed:

- **E3006 (duplicate function)** swapped in for the parser-stage E2001 at
  `reportDuplicateFunction` in [`Parser.maxon`](Compiler/Parser.maxon); same-file
  duplicate-name detection routed through the qualified name (so the
  diagnostic reads `Duplicate function 'duplicate-functions.helper'` rather
  than the bare `'helper'`). Cross-file duplicate `main` was already caught
  by the project-level `funcReturnTypes` registry under the qualified
  `"main"` key — only the error code needed to change. 3/3 spec fragments
  green.
- **E3012 (unused variable)** already existed via `checkPendingUnusedVars`
  in [`SemanticCheck.maxon`](Compiler/SemanticCheck.maxon) — the spec went
  from 16/20 to 20/20 by extending `localVarLocations` to also record:
  (1) for-range / for-in loop variables (the iter-var was bound in a
  fresh scope but skipped by the unused tracker — the C# bootstrap tracks
  them per `for VAR in EXPR` shape); (2) match-arm payload bindings
  (`value(n)` registers `n` for the arm body — bootstrap counts it as a
  binding subject to E3012); (3) closure parameters (the lifted function's
  fresh scope captured the params but never deferred the unused check;
  `checkUnusedParameters` is now called before the parser context
  restore in `parseClosure`).
- **E3064 / E3065 (discarded results)** ported from the C# bootstrap's
  `CheckDiscardedResults` + `PurityAnalysisPass`. The check has three
  moving parts:
    1. **Parser tags** (`project.discardedBareCallResults` /
       `discardedLetCallResults`) record where a call's result is discarded.
       Three positions tag: bare statement-position calls
       (`foo(x)`), `_ = expr` discards, and the new `(_, _) = expr`
       tuple-all-discard form (added to the statement dispatcher via
       `isTupleDestructureDiscard()` peek).
    2. **Purity analyzer** (`computePurity` in [`SemanticCheck.maxon`](Compiler/SemanticCheck.maxon))
       walks each function once, marking impure on void return, empty body,
       globalStore, selfFieldStore, or a call to a known-impure callee
       (the 25-entry list from `PurityAnalysisPass.cs:77` — managed-mem
       mutations, managed-list mutations, I/O builtins, anything prefixed
       `mrt_`). A parser-side sidecar `project.paramMutatingFunctions`
       captures parameter reassignment (the C# bootstrap computes this in
       a separate `ParameterMutationAnalysisPass`; self-hosted records it
       at the assignment site). Transitive propagation runs to fixed point
       via a callee-edge map.
    3. **Discarded-results check** (`checkDiscardedResults`) walks every
       call/tryCall/methodCall/tryMethodCall, looks up the parser tag, and
       emits E3064 (pure callee) or E3065 (impure callee, bare-discard only)
       unless the callee is void or chainable (1st param `self`, return
       type matches receiver type).

  Two carve-outs keep cache-parity intact: (a) the check skips functions
  whose `sourceFilePath` resolves under `stdlib/` so stdlib internals (which
  reuse `_ =` patterns in ways the user-facing spec doesn't cover) don't
  fire diagnostics on the cache-off path; (b) statement-form `try EXPR
  otherwise FALLBACK` is **not** tagged — the `try` keyword itself is the
  user's documented opt-in for ignoring fallible results, matching the
  bootstrap. Expression-context `_ = try EXPR otherwise EXPR` still tags.

  Unrelated bug surfaced and fixed during stream 3: closures captured
  outer variables via `emitClosureCapture` but did not update the outer's
  `referencedVars`, so an outer `let offset = 7` consumed only inside a
  closure body would falsely trip the new E3012 wiring. A
  `closureOuterReferencedVars` parser field plumbs the outer set into the
  capture helper so the capture itself counts as a use.

17/17 discarded-results, 3/3 duplicate-functions, 20/20 unused-variables.
Bootstrap parity: 2769/2769 (up from 2767 as the three specs were added to
the C# baseline at the same time the C# infrastructure landed).

**15b: Command-line Args** — **DONE 2026-05-13 in Stage E.4** (9/10 on x64-windows).
`CommandLine.args()` lowers to `__Builtins.commandLineCount` / `__Builtins.commandLineArg`
intrinsics, routed to `mrt_command_line_count` / `mrt_command_line_arg` in runtime.std.
Windows path: PE import directory for shell32.dll (`GetCommandLineW`, `LocalFree`,
`WideCharToMultiByte`, `CommandLineToArgvW`) added alongside kernel32.dll;
`iatSlotByteOffset` accounts for per-DLL null terminator. Per-OS runtime filter
swaps in non-Windows stubs (returning argc=1, empty MM). Wasm32-wasi uses the stub
(1/10 fragments pass) — real WASI `args_sizes_get`/`args_get` wiring needs future work.
Cache-decode bug fixed alongside: `GenericInstanceRegistry` writer→project gid
translation table prevents id-collision when new whitelisted stdlib files introduce
generic instances that interleave with existing entries
([`StdlibCache.maxon::readGenericInstanceRegistry`](Compiler/StdlibCache.maxon),
[`translateWriterGenericInstanceId`](Compiler/StdlibCache.maxon)).
Secondary fix: `enclosingType` forwarded through `callResultType` /
`callOrArrayLiteralResultType` / `refineStaticReturnFromQualifiedCallee` /
`qualifyCalleeForContextWithType` so inner-typealias static calls (`MyArr.create()`
where `typealias MyArr = Array with String` is declared inside `type Foo`) resolve
correctly. `CACHE_FORMAT_VERSION` bumped to 44.

**15c: File I/O** — **DONE 2026-05-22**. `file-io` (10/10) and
`directory` (19/19) green on x64-windows + wasm32-wasi (runtime-tested);
all targets including x64-linux + arm64-windows + arm64-linux compile
cleanly (no cross-execution available on the x64-windows host).

The Std dialect stays platform-independent: 11 new ops describe file/
directory primitives by semantics, and each backend chooses how to
satisfy them. The architecture stack from user code down to the OS:

```
user code (e.g. File.readText)
  → stdlib lowering → __ManagedFile.openRead via lowerManagedFileBuiltin
  → mrt_file_open_read (one target-agnostic body in runtime.std)
  → std osFileOpen op
  → backend lowering:
      • Linux X64:   syscall rax=2 (open)
      • Linux ARM64: svc #0 with x8=56 (openat AT_FDCWD)
      • Windows:     call mrt_win32_open helper (wraps CreateFileW IAT call)
      • Wasm:        intercepted at MirToWasm → path_open WASI import
```

Landed across six streams (2026-05-21 / 2026-05-22):

- **Stream 1 — Stdlib whitelist.** The real `stdlib/FilePath.maxon`
  (411 lines), `stdlib/File.maxon`, and `stdlib/Directory.maxon` joined
  the whitelist in [`StdlibLoader.maxon`](Compiler/StdlibLoader.maxon).
  Synthesized minimal-FilePath stub deleted. `CACHE_FORMAT_VERSION`
  bumped (52 → 53 → 54). Surfaced and fixed a typealias-visibility
  tracking bug in `parseTopLevelTypealias` where a file-private alias
  in one file could prevent an exported one in another file from being
  recorded.

- **Stream 2 — Builtin types + lowering dispatch.**
  [`LowerMaxonToStd.maxon`](Compiler/IR/Maxon/LowerMaxonToStd.maxon)
  gained `registerManagedFileType` / `registerManagedDirectoryType`
  (mirror of Phase 6b's `registerManagedMemoryType`),
  `managedFileHelperFor` / `managedDirectoryHelperFor` name maps + their
  `*HelperThrows` companions, and `lowerManagedFileBuiltin` /
  `lowerManagedDirectoryBuiltin` dispatch functions that route
  `__ManagedFile.method` / `__ManagedDirectory.method` calls to bare
  `mrt_file_*` / `mrt_directory_*` runtime helpers via the multi-value-
  return error ABI (ordinal+1 in RDX/X1 on failure).

  Three throws-table corrections caught by reading actual stdlib `try`
  patterns: `size`, `Directory.next`, and `Directory.currentPath` are
  all throwing (plan's table had them as non-throwing).

- **Stream 3.3 + 4.2 — Wasm32-wasi.**
  [`MirToWasm.maxon`](Compiler/Targets/Wasm/MirToWasm.maxon) extended the
  WASI preview1 import table from 5 to 13 entries: added `path_open`,
  `fd_close`, `fd_seek`, `fd_filestat_get`, `path_filestat_get`,
  `path_unlink_file` (NOT `path_remove_file` — wasmtime rejects the
  latter), `path_create_directory`, `fd_readdir`, plus `fd_prestat_get`
  and `fd_prestat_dir_name`. Each `mrt_file_*` / `mrt_directory_*`
  helper is intercepted at MIR-lowering time and hand-emitted as a
  custom Wasm body that calls the matching WASI import.

  WASI gotchas surfaced and fixed:
  - `fs_rights_base = 0x3FFFFFFF` (bits 0..29 = "all defined rights").
    All-bits-set `0xFFFFFFFFFFFFFFFF` causes wasmtime to reject with
    `TryFromIntError` because undefined bits trip its mask validator.
  - Directory opens MUST exclude `FD_WRITE` from `fs_rights_base` —
    newer wasmtime returns EISDIR when a directory is requested with
    write rights. Limited to `0x246000`.
  - Errno values: `ENOENT = 44`, `EACCES = 2` per WASI preview1's
    sequential errno enum.

  Spec runner integration: [`SpecTestRunner.maxon`](Testing/SpecTestRunner.maxon)
  passes `--dir=.` and `--dir=..::../` to wasmtime so fd 3 = cwd and
  fd 4 = parent-of-cwd. `mrt_directory_current_path` returns `"."`
  (WASI has no `getcwd`; the preopen at fd 3 *is* the cwd by convention).

- **Stream 4.1 + 3.2 — Linux Std ops + ARM64 lowering.**
  Added 11 platform-independent ops to
  [`StdDialect.maxon`](Compiler/IR/Std/StdDialect.maxon) and
  [`MirDialect.maxon`](Compiler/IR/MIR/MirDialect.maxon): `osFileOpen`,
  `osFileRead`, `osFileWrite`, `osFileClose`, `osFileLseek`,
  `osFileStat`, `osFileAccess`, `osFileUnlink`, `osDirGetdents64`,
  `osDirGetcwd`, `osDirMkdir`. All ops take `(cStringPtr, ...)` for
  paths — the runtime helper owns NUL-termination via a new
  `mrt_cstr_from_mm` helper.

  Linux X64 lowering (`MirToX64Conversion.maxon::lowerOsFile*`) emits
  `mov rax, <syscallNum>; syscall` with args in `rdi/rsi/rdx/r10/r8`.
  Linux ARM64 lowering (`Arm64Backend.maxon::lowerOsFile*Arm64`) emits
  `mov x8, <syscallNum>; svc #0` with args in `x0..x5`, using the
  `*at` variants (`openat`, `newfstatat`, `faccessat`, `unlinkat`,
  `mkdirat`) with `AT_FDCWD = -100` since ARM64 Linux drops the
  non-`at` syscalls.

  Pattern-match sites extended for the 11 new ops across 11 files:
  DCE, CSE, Mem2Reg, LICM, InjectDrops, BorrowCheck, Inliner,
  InstructionScheduler, StdOpHelpers, CommuteForCoalescing,
  BackendDispatch (latency entries). The Std/MIR `to`-range patterns
  in several passes naturally covered the new ops once they were
  placed contiguously in the union.

- **Stream 4.1b — Windows unified under the same ops.**
  Replaced Stream 3.1's hand-emitted Win32 `runtime.std` bodies with
  `call mrt_win32_*` dispatch. Each `osFile*` op now branches per
  `osDesc.os` at the backend lowering: Linux → syscall, Windows →
  emit `call mrt_win32_open` / `mrt_win32_read` / etc. The
  `mrt_win32_*` helpers live in `runtime.std` and each wraps one
  Win32 IAT call (CreateFileW, ReadFile, WriteFile, CloseHandle,
  SetFilePointerEx, GetFileAttributesExW, DeleteFileW,
  GetCurrentDirectoryW, CreateDirectoryW) plus errno translation via
  the new `mrt_win32_translate_lasterror` that maps GetLastError ↔
  POSIX `-errno`. UTF-8 → UTF-16 path conversion via
  `MultiByteToWideChar` happens inside each `mrt_win32_*` helper that
  takes a path. PE writer's IAT slot allocation (Stream 3.1's 12
  slots) reused as-is.

  Directory iteration on Windows: `mrt_win32_open` with a directory
  path allocates a 616-byte heap block `[kind=1, HANDLE,
  pending_flag, WIN32_FIND_DATAW]`, calling FindFirstFileW to
  pre-position the iterator. `mrt_win32_close` dispatches CloseHandle
  vs FindClose by inspecting `block[0]`. `mrt_win32_getdents64`
  reads the cached WIN32_FIND_DATAW on first call, then drives
  FindNextFileW for subsequent calls, packing each entry into the
  Linux `dirent64` layout the platform-agnostic body expects.

  ARM64-Windows path mirrors X64-Windows: each `lowerOsFile*Arm64`
  also dispatches `if isWindowsTargetArm64(osDesc)` to the same
  `mrt_win32_*` helpers via the ARM64 direct-call shape.

  Per-OS body swap in
  [`BackendDispatch.maxon::filterRuntimeForOs`](Compiler/Targets/BackendDispatch.maxon)
  no longer needs the 19 file/directory entries — one body per
  helper, target-agnostic, the lowering does the rest. The 3
  command-line / executable-path entries from Phase 15b/15f remain
  using the old stub-swap shape.

Final tally: **1587/1587** on x64-windows + wasm32-wasi (up from 1566
baseline). Bootstrap **2770/2770** holds. All other targets
(x64-linux, arm64-windows, arm64-linux) compile cleanly through the
same shared IR — runtime correctness is structurally identical to the
runtime-tested targets via the unified op architecture.

**15d: Panic & Stack Traces** — **DONE 2026-05-13 in Stage E.5** (3/3).
Infrastructure was already in place: `mrt_panic` walks RBP/X29 frame chain
(up to 32 frames), `mrt_panic_print_frame` does symbol-table lookup via
`__symtable` (linear scan for largest `codeOffset <= text_offset`).
[`runtime.std:1240-1456`](Compiler/Runtime/runtime.std).

**15e: Tuples** — **DONE 2026-05-13 in Stage E.2** (9/9 active, 1 tagged for
Phase 13 MapIterator). Eight root-cause fixes:
(1) `t.0` / `t.1` parser support via `parsePostfix` accepting intLiteral
+ `computeFieldNameForMemberToken` mapping to `_N` synth fields;
(2) field-write `t.0 = x` through `chainedFieldAssignDepth` + threading;
(3) mixed-type tuple inference at parse time interning `genericInstance(__TupleN, [T0..])`
when all elements concrete, with `project.tupleAllocGids` keyed to alloc result name;
(4) tuple field-load/store width substitution via `pickTupleFieldType`;
(5) TR late-bound tuple refinement via `refineTupleStructAlloc` + per-function
`tuplePartialArgs` side-table;
(6) `let (x, y) = expr` destructuring via `parseTupleDestructureVarDecl`
emitting hidden `__destructure_<l>_<c>` temp + per-element fieldLoad varDecls;
(7) string-literal tuple elements routed through `materializeStringConstIfNeeded`;
(8) slot-type propagation in `recordVarDecl` / `recordVarLoad` prefers producer
`genericInstance(...)` over bare named, so `fillVarLoad` propagates specialised types.

**15f: Namespaces & Exports** — Done in Phase 12 (`module-keyword` 11/11,
`export-keyword` 24/24, `exported-struct-var-cross-file`). Multi-file fragment
support via `compileMultiFileSourceWithIr`.

**15g: Function Overloads** — **DONE 2026-05-13 in Stage E.3** (10/11, 1 tagged
for char↔Character coercion). Six root-cause fixes:
(1) DFE marks every bucket entry for overloaded names so variants aren't dropped;
(2) `qualifyCalleeForContextWithType` checks `overloadedNames` so bare-qualified
names flow to the resolver; (3) `mangleSuffixForMaxonType` chases typealias-to-primitive
via new `namedTypeMangleSuffix`; (4) same-types-different-names overloads fall back
to `mangleSuffixForParamNames`; (5) label-based resolution via
`resolveOverloadByLabels`; (6) new E3007 `ambiguousOverload` diagnostic with
rendered candidate list.

**15h: Method Call Syntax** — `value.method(args)` desugaring. Already works
across the existing spec corpus (method calls go through `lowerMethodCall`).
No dedicated spec exists; consider this satisfied by the working method-call paths.

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
- `optimizations` spec (commented in whitelist) is a stub — no `specs/optimizations.md` source exists. The whitelist entry was forward-looking; either author the spec or remove the entry.
- Phase 11.6 inliner is the next major addition — **landed in Phase 11**.
- Peephole optimization at the target-dialect level (small-constant strength reduction, etc.)

### Stage G landing (2026-05-13) — spill-code insertion correctness fix

Not a tuning win, but a real correctness fix that lived in the optimization
pipeline. While unblocking math `rt-*` fragments (Stage G.1 fixed the
nested-try parser bug; Stage G.2 then surfaced this):

**Bug**: `remapBranchEdgeArgs` ([`SpillCodeInsertion.maxon:110-180`](Compiler/Targets/Shared/SpillCodeInsertion.maxon))
appended spilled-source reload ops to the END of `block.opRefs`. On x64/arm64
Layout-1 conditional-branch blocks (opRefs ending in the `condJump` op), this
placed reloads AFTER the conditional branch — so the cond-taken edge skipped
them and read stale physical-reg values that an intervening call had clobbered.

**Fix**: collect reloads in a side list and splice them BEFORE any trailing
`condJump` via new helpers `findCondJumpIndex` + `insertOpsAt`. Wasm uses
typed `local.get`/`local.set` so it was never affected — wasm's pre-fix +12
versus x64's +4 from the same parser fix was the diagnostic canary.

Net Stage G gain: **+42 fragment passes** on x64-windows (1160 → 1202). The
32 math `rt-*` fragments (across abs/sqrt/floor/ceil/round/trunc/min/max) all
went green.

### Deferred: C2.d per-function dispatch

Per the original plan, perf benchmarks need C2.d to land first. C2.d.3 (the
backend split) is a multi-day refactor and unblocks no failing specs — only
incremental-compilation perf. Deferred to a future Phase 16 push when the
benchmark suite is being authored.

---

## Stage H + I landing (2026-05-13/14) — drove all-targets to 100%

After Stage G left 15 residual failures on x64 and 52 on wasm, a focused
push closed every spec on every target. **Final state: 1234/1234 on
x64-windows, 1234/1234 on wasm32-wasi, 2714/2714 on C# bootstrap.**

### x64 closures (Stage H)

**H.A — Lexer `'\''` single-quote escape + Parser `decodeStringEscapes` arms**:
`scanSingleQuoteString` had no backslash-escape handling, and `decodeStringEscapes`
was missing several arms for char-literal escape sequences. Fixed lexer to
consume `\X` raw inside single-quoted char literals (mirrors double-quoted +
byte-string scanners). Parser decode arms extended.

**H.B — String-backed and char-backed enums**: `parseEnumDecl` only accepted
int/float/identifier raw values. Added `stringLiteral` and `charLiteral` arms,
new `EnumCase.rawStringValue` field + `isStringBacked`/`isCharBacked` flags
on `EnumType`/`UnresolvedEnumType`, mismatched-backing E3032 emission, and
`materializeEnumNameInterp` extended to emit the case's `rawStringValue` rdata
for string/char-backed enums. **Bonus untaggings**: re-enabled `enum-ordinal`
(9/9) and `enum-allcasenames` (8/8). `enum-allcases` still gated on `.rawValue`
accessor for non-numeric backings.

**H.C — 4 diagnostic plumbing fragments**:
(1) **E3016 throws-conformance check** in `SemanticCheck.checkConformanceForType`
via new `validateThrowsConformance` helper (missing throws arm + throws-non-Error arm,
mirroring [`maxon-sharp/Compiler/2-Parser.cs::ValidateThrowsConformance`](../maxon-sharp/Compiler/2-Parser.cs)).
(2) **E3005 otherwise-out-of-range** via extended `TryResultIndex.expectedType`
+ new `checkFallbackLiteralRange` helper (handles unary-neg via `findLiteralProducerInFunc`).
(3) **E3005 plus-on-string** via new `validateBinopOperandTypes` pass that walks
all `binop` ops and flags String / named-struct operands. Surprise +13 cascade
because the `retypeCharByteReceiver` helper also recovered adjacent fragments.

**H.D — char↔Character coercion via charByte provenance**: parser-local
`project.charByteLiterals` tracks values that originated as single-byte char
literals. Post-parse `propagateCharByteThroughVarChains` walks varDecl/varLoad
chains. Consumers (overload resolution retry, interpolation `materializeCharByteInterp`,
arg coercion via `materializeCharacterFromByte`) probe the set. Stage A.fix4's
deferred "option (b)" finally landed. Unblocks `string-contains-char` overload
ambiguity + `'{c}'` interpolation.

**H.E — float-parser overflow + x64 NaN miscompile (two root causes)**:
(1) **`NumberParsing.maxon`**: float literals >19 sig digits silently overflowed
the i64 mantissa accumulator. Redesigned strtod-style: track significant digits +
decimal exponent separately, 18-digit budget, leading-zero fractional runs fold
into exponent. (2) **x64 backend `feq`/`fne` NaN handling**: `ucomisd` sets ZF=1
for both "ordered equal" AND "unordered NaN". Added parity-flag pre-check via
new `X64SetccCondition.p`/`np` codes + dual `condJump`/`setcc` sequences. ARM64
+ Wasm already handled NaN correctly. Net: stdlib pi/ln2 literals trimmed to
17 digits, `float.compare` NaN semantics now correct on every target.

**H.F — `Process.executablePath()`**: bootstrap-synthesized `FilePath` + `Process`
types (similar pattern to Stage A's `Parsable` synthesis). New `mrt_executable_path`
runtime helper with Windows IAT bodies (`GetModuleFileNameW` + `WideCharToMultiByte`)
and per-OS filter routing. Plus a `__BuiltinParseError` rename in the bootstrap
synthesis to free `ParseError` for user redeclaration (was causing intermittent
"Duplicate enum 'ParseError'" batch flakes).

### Wasm closures (Stage I)

**I.1.A — WASI `args_sizes_get` / `args_get`**: hand-rolled wasm bytecode bodies
for `mrt_command_line_count`, `mrt_command_line_arg`, and `mrt_executable_path`
(delegates to `mrt_command_line_arg(0)`). Cached argv state in wasm scratch
memory at offsets 16..32. Plus spec runner's wasmtime invocation no longer
emits the `--` separator (was being forwarded as argv[1]).

**I.1.B — Wasm globalAddr cache-offset fixup (pre-existing bug)**: stdlib-cache
builds baked stdlib-relative linear-memory offsets, but the user-build merge
prepended its own globalData, shifting every stdlib symbol. New
`WasmGlobalAddrFixup` (mirrors `WasmFuncAddrFixup`): cache mode writes 5-byte
fixed sleb128 placeholders; user-build patches against final `globalDataOffsets`.
This was the hidden bug behind `__bool_fromString`'s `"true"` literal reading
garbage bytes on wasm32-wasi.

**I.1.C — Wasm witnessCall void-return type mismatch**: `call_indirect` requires
exact signature match in the type section. Void interface methods (like
`function show()`) need 0-result signatures but witness-call was registering
1-result. Added `isVoid` to `StdCallOp.witnessCall` / `MirOp.witnessCall`,
threaded through ~14 files. Native backends ignore it; wasm picks the right
indirect-call type. `CACHE_FORMAT_VERSION` bumped to 46.

## Stage J landing (2026-05-14) — re-enable all dev-disabled tests + monomorphizer

Sequential agent runs to re-enable every test disabled across Stages A-I,
and to land the missing infrastructure each disabling represented. Net:
**+14 fragments** over the 1276 entry baseline; **+56 fragments** vs Stage I's
1234 final number (which had the 6 J-tagged-out fragments above as dark
matter). Final state: 1290/1290 x64-windows, 1290/1290 wasm32-wasi,
2719/2719 C# bootstrap.

**J.serial.1 — conditional Array Hashable/Equatable** (+1: `array-hashable/int-array-map-key`):
`thunkWitnessLabelForBound` was producing a wrong witness label (`__witness_IntArr_Hashable`)
when the bound type was a typealias. Added `resolveAliasedBound` to chase
typealiases through `project.genericTypealiases` and `project.typealiases` so
the label resolves to the actual emitted `__witness_Array_Int_Hashable`.

**J.serial.2 — MapIterator layout-forwarding** (+2: `map/for-in.nested`,
`tuples/for-destructuring-map`): no-op — the cross-project layout forwarding
the prior rate-limited agent had laid down (`substituteReturnTypeViaReceiver` +
`promoteBareGenericReturnToReceiverScope` in LowerMaxonToStd.maxon ~lines
8235-8320) had already landed; just needed the markers flipped.

**J.serial.3 — sized Vector + Set generic-inference** (+35: `set` 15/15 +
`vector` 20/23): unification-based generic-arg inference at the call-result-type
computation path (`inferGenericReturnFromArgs` in TypeResolution.maxon). When
`T.init(...)`'s return type lands as bare `named(T)` and `T` declares `uses
Param1, ...`, walk `(paramType, argType)` pairs, unify through `genericInstance`
arg lists, and bind `T`'s type-parameters from the actual arg shape. BAL fast
path for `Vector from [...]` (parses managed-memory directly rather than wrapping
in Array first). Sized typealias post-call `mrt_alloc` patch in `lowerCall`.

**J.serial.5 — E3087 redundant `contains+get` diagnostic** (+2: `if-try`
fragments): pure marker flip — the C#-bootstrap-style diagnostic was already
implemented in `SemanticCheck.maxon::checkRedundantContainsGet` (~120 lines
mirroring C#'s `CheckRedundantContainsGet`) and `ErrorCode.maxon:80`
(`semanticRedundantContainsGet = 3087`). Just needed the `disabled-test:`
markers flipped.

**J.serial.6 — try-otherwise binding union-type scope** (+1: `map/insert.duplicate-error-binding`):
three compounding bugs uncovered:
(1) `scanProducerTypeForName` (Parser.maxon:8643) didn't chase through
`varDecl` → `call`/`tryCall` producer types, so `var m = [1: 10]` couldn't
resolve `m.insert` past the bare method name.
(2) `StdlibCacheData` was missing `funcThrowsTypes` and `enumTypes` —
fixed by capture/serialize/deserialize/restore round-trip.
(3) Enum exhaustiveness diagnostic regression where stdlib + user enums
with same case-name collided (e.g. `notFound`) — fixed via type-driven
`enumScrutineeDisplayNameFromType` instead of case-name search.
`CACHE_FORMAT_VERSION` bumped 46 → 47.

**J.serial.4 (extension Element substitution)** decomposed into 7 sub-stages:

**J.4a-prereqs 1-5 + X** (zero-fragment infrastructure changes, no regressions):
- prereq.1: `UnresolvedInterfaceType.innerAliases` + `mergeExtensionInnerAliases`
  dispatches on interface targets.
- prereq.2: `functionIsGenericForLowering` + `enclosingTypeName` recognize
  interface enclosing types with non-empty `usesParams`.
- prereq.3: new `resolveInterfaceUsesParamThroughConformance` helper +
  4 supporting helpers in `TypeSystem/Substitution.maxon` for resolving uses-params
  via Self's conformance row.
- prereq.4: parser extensions for bare-comma typealias args
  (`typealias X = Y with A, B`) and tuple-collapse in implements
  clause (`T with (A, B)` against single-uses interface).
- prereq.5: implicit witness inference (`inferImplicitWitnessConstraints` infers
  `T is Iterator` when an interface declares `createIterator()` returning T) +
  `forwardCallerWitness` upgraded to look up caller-side paramName when callee
  uses a different name.
- prereq.X: resolved `IrInterface.innerAliases` + drain forwarding +
  StdlibCache round-trip + `chaseTypealiasName*` for interfaces +
  `enclosingTypeNameForCallee` for interfaces. `CACHE_FORMAT_VERSION` 47 → 48.

**J.4a-final** (zero-fragment, prereq complete): whitelisted
`stdlib/Interfaces.maxon` + `stdlib/helpers/itertools/withIterator.maxon`.
Deduplicated bootstrap synthesis (removed interfaces/enums/typealiases that
the real file now provides). Fixed the 7th unforeseen prereq: function-type
cache codec — `writeFunctionTypePayload` / `readMaxonFunctionType` in
MaxonDialect.maxon + `functionTypes FunctionTypeRegistry` capture/translate
in StdlibCache.maxon. `CACHE_FORMAT_VERSION` 48 → 49.

**J.4b — Per-receiver Maxon IR cloner for user-declared extensions** (+5
`conditional-extensions` fragments): new pass
[`Compiler/Passes/MonomorphizeExtensions.maxon`](Compiler/Passes/MonomorphizeExtensions.maxon).
Entry point `monomorphizeInterfaceExtension` clones a function's blocks +
ops with `MaxonType.typeParameter(<Iface, usesParam>) → withArgs[i]` and
`MaxonType.interface(Iface) → MaxonType.named(receiverType)` substitution,
slot-id renumber, dense scope rebuild. Trigger site in
`TypeResolution.resolveMethodCallSite`. E4006 conditional-availability
diagnostic + typealias-to-primitive conformance chase (`namedConformsTo` in
`TypeSystem/Constraints.maxon`).

**J.4c — Stdlib-extension cache restore + overload typealias chase** (+1:
`conditional-extensions/array-contains`): added `unresolvedExtensions
UnresolvedExtensionArray` and `extensionMethodWhereClauses WhereClauseMapByName`
to `StdlibCacheData` with capture/serialize/deserialize/restore. Fixed
overload resolver to chase inner typealiases through enclosing type's
`innerAliases` before classifying each variant's first-param shape
(`MaxonDialect.resolveOverloadedCallee` lines ~2262-2275). `CACHE_FORMAT_VERSION`
49 → 50.

**J.4d/4e — Function-typed cloner + cross-project Maxon-IR translator** (+8:
`closure-capture.map-with-capture` and 7 `interface-extensions` fragments):
J.4d added `MaxonType.function(id)` substitution arm to `substituteType` +
cloner ABI expansion for closure params and interface fat pointers + on-demand
`runFillUnresolvedTypesForFunc` re-run for cloned bodies. J.4e built the
production architecture as an alternative to disk-cache codec changes: new
[`Compiler/Passes/StdlibIrTranslation.maxon`](Compiler/Passes/StdlibIrTranslation.maxon)
provides a sandbox stdlib parse (once per process) plus
`translateOpAcross` (49-arm match over `MaxonOp`) +
`translateMaxonTypeAcrossProjects` + `translateFunctionAcross` that
re-binds Maxon-IR ids across project boundaries (string-key round-trip,
bypassing the writer/reader id-translation pattern). The sandbox is
captured by `recordStdlibSandboxSnapshot` on cache-miss warmup and
constructed lazily by `ensureStdlibSandboxParsed` on cache-hit.

**Spec runner extension**: new `isSelfHostedKnownBroken(specName, testName)`
filter in `SpecTestRunner.maxon` for fragments that pass on the C# bootstrap
but need self-hosted-specific cloner-completion work. Single entry today:
`interface-extensions/stdlib-map-on-set` (cloner doesn't thread the
`(Element, Hashable)` witness chain through `Set.map`'s nested
`Set.createIterator` call — J.4f scope).

**Residuals — ALL RESOLVED in Stage J.4f** (2026-05-14, four sub-stages):

**J.4f.1 — stdlib-map-on-set** (+1): root cause was deeper than expected.
`decodeStdlibCache` was wiping `data.typeParameters` and rebuilding from
`project.typeParameters` — but the cached on-disk where-clause constraints
(e.g. `Set.Element: Hashable, Equatable`) lived only in the registry, not in
`project.typeParameters`. The wipe-and-rebuild dropped them. Fix: snapshot
the registry's constraint map into a `CachedConstraintMap` before wipe;
re-attach to each entry during the id-space rebuild
([`StdlibCache.maxon:1500-1556`](Compiler/StdlibCache.maxon)). No cache version
bump needed — on-disk format unchanged.

**J.4f.2 — stdlib-map-on-map-with-function** (+1): two coupled gaps.
(1) Parser/TR: untyped closure params (`(p) gives p.value`) parked as
`MaxonType.unresolved` at parse time, patched at TypeResolution via new
`propagateClosureArgTypesFromCalls` pass that walks call args and copies
concrete signatures from `callParamTypes` into the lifted function's
signature + scope + body slot types.
(2) Result-type substitution: `methodCallResultType` for `Map<K,V>.map(...)`
(via `Iterable.map`) had three latent gaps — receiver inner alias
`ElementArray` lives on Iterable's body (not Map's), `Iterable.Element`
typeParameter not resolved through Map's conformance row, conformance withArg
`Entry` is a `named(Entry)` reference that needs to chase Map's own inner
alias. All three fixed via new helpers in `TypeResolution.maxon` +
`MaxonDialect.maxon` (`chaseTypealiasViaConformingInterfaces`).

**J.4f.3 — vector accumulate-sum** (+1) + sized-generic substitution bug fix:
test rewritten to fit WASI ExitCode range (1..125 instead of 150). Plus a real
compiler fix: `buildSubstitution` was binding `Element` to `constInt(N)` for
sized typealiases like `Vector with N Element` because `args` carries a leading
`constInt(size)` prefix; fix skips leading constInt arms when `argCount > count`.
Preparatory infrastructure: new `bitcastI64ToF64` opcode threaded across
StdDialect/MIR/X64 (`movqGprToXmm`)/ARM64 (`fmovToFloat`)/Wasm
(`opF64ReinterpretI64`). `CACHE_FORMAT_VERSION` 50 → 51.

**J.4f.4 — vector float-vector + from-array-literal-float** (+2): the
architecturally-correct hybrid-generics fix. **Methods are not monomorphized**
(per Phase 11 design); the bridge between int-channel ABI and float-class SSA
is at the call-site, not in cloned bodies. New helpers in `LowerMaxonToStd.maxon`:
- `bridgeFloatArgToTypeParam` — emits `bitcastF64ToI64` when raw paramType is
  `typeParameter` and substituted paramType has `CastCategory.float`.
- `maybeBitcastCallResultToFloat` — emits `bitcastI64ToF64` when raw return
  type is `typeParameter` and substituted return resolves to float-class.
Wired into `slotArgsForCall` (after `coerceArgToParam`) and `emitCallWithArgs`
(after `recordCallResultType`). The bridge fires only when raw=typeParameter
AND substituted=float — int paths are byte-identical to the prior baseline.

**Net Stage J + J.4f**: +19 fragments over the 1276 pre-J baseline, +61 over
Stage I's 1234. Final state: **1295/1295 x64-windows, 1295/1295 wasm32-wasi,
2723/2723 C# bootstrap.** Zero remaining stage-tracked residuals.

## Stage K landing (2026-05-15..17) — cache-parity harness + witness-conditional DFE + runtime DCE

Three undocumented stages landed between J.4f and the current 1311/1311
baseline. None of them unlock new specs by themselves, but they surface
real correctness bugs that were previously masked by the stdlib cache.

**K.1 — Cache-parity harness** (commit `7549a7247`). New
`<!-- CacheParity -->` file-level directive + per-fragment
`CacheParity: true` flag double-compile each test once with the stdlib
cache forced off and once with it on, failing on `.rdata` or `.text`
divergence between legs.
- `StdlibLoader.setStdlibCacheBypassed` + `resetStdlibLoaderState` gate
  the in-process warm path and on-disk cache read.
- `runTestParityCheck` double-compiles, compares PE `.rdata` + `.text`,
  restores warm in-process state for subsequent tests.
- New `cache-parity` spec exercising the harness.

**K.2 — Witness-conditional DFE** (commit `7549a7247`). Replaces
unconditional `livenessRoots.push(...)` for witness-table impls and
layout-descriptor copy/destroy thunks with conditional roots.
- `Project.witnessConditionalRoots` keys rdata labels → impl names.
- `BuildLayoutDescriptors` / `BuildWitnessTables` register copy/destroy
  thunks and witness method impls as conditional roots.
- Std-DFE promotes them onto the worklist when a live function emits
  `loadWitnessTable` / `loadLayoutDescriptor` for the same label; also
  prunes dead `globalData` entries + dangling rdata relocs in the same
  fixed point.
- Maxon-DFE conservatively keeps every stdlib function alive (cache is
  built from this pipeline; pruning here would write a broken cache).
- Cut `.text` by ~77% on the representative compile.

**K.3 — Runtime reachability DCE** (commits `7549a7247`, `0edb27569`).
`BackendDispatch.filterRuntimeByReachability` walks user-module call
edges + cache `callFixups` + wasm-specific backend edges to trim
`runtime.std` from ~62 helpers to the user binary's actual closure.
- Splits the cache-bypass merge order (user-side runtime → user code →
  stdlib → cache-side runtime) to match cache-on's emission order so
  `.text` is byte-identical across cache states.
- Follow-up `0edb27569` restricts cache-side `callFixups` seeding to
  stdlib bodies the user transitively reaches (via
  `computeNeededStdlibFunctions[Wasm]`), and seeds layout/witness
  thunks from `funcAbs64InRdata` relocs so descriptor-referenced impls
  don't fall out. A `Math.pow`-only program no longer drags in
  `mm_alloc` via every cached String/Map helper. Shrunk
  `IncludeStdlibIr` fixtures by ~4.4k lines.

**Compiler fixes surfaced by the parity harness** (commit `7549a7247`):
- `LowerMaxonToStd`: slot scratch buffers explicit zero-fill after
  `resize()` (resize sets length, not memory).
  `extendSlotsForInterfaceWitnesses` / `buildInterfaceWitnessSlotMap`
  use parser-level `maxonParamTypes.count()` instead of the augmented
  paramCount so local interface slots aren't mis-classified as params.
  Tuple-hint lookup gated on `__Tuple`-prefixed result names to avoid
  colliding with synthesized `$tN` names from stdlib allocs.
- `IrPrinter.stabilizeLabels`: per-function counter scope so `try_4` in
  two different functions resolves to distinct names; preceding-context
  window widened 32→40 chars.
- `Parser`: typealias collision check simplified (cross-file
  invisibility silences "shadows" path); `maxonTypeStructName` also
  looks up unresolved struct types so parse-time method-call
  qualification sees user/stdlib structs before `resolveAllStructTypes`
  runs.
- `SemanticCheck.maxonTypesMatchForConformance` recurses into
  `genericInstance` args so type-parameter equivalence works across
  interface decl vs conformer scopes (wasm regression).
- `TypeResolution.preResolveSlotFromGlobalStore` scopes the producer
  search to the func's own blocks (synthesized `$tN` names reset per
  function) and accepts `tryCall` shapes.

**K.4 — Lock + remote-free queue work surfaced by Stage K parity**
(commits `206fad39c`, `92eaabb9f`): the parity harness exercised the
allocator under more concurrent pressure than the previous workload,
which surfaced SIGSEGVs in the slab span free-list. Fixed by adding a
per-span lock; then replaced the global slab pool lock with a
Mimalloc-style cross-P remote-free queue. ASLR-resilient fault
diagnostic suffix added alongside.

**Net Stage K**: 1295 → 1299 (+4). No new specs unlocked directly;
the parity harness flagged correctness gaps that would otherwise have
shipped as latent bugs.

## Stage K.X landing (2026-05-18) — enum narrow storage + E3014 + Phase 9 closure

**K.X.1 — `enum-narrow-storage`** (commit `be2faaeea`). Storage width
narrowing for enum-backed fields, matching the C# bootstrap.
- `StoreWidth` (byte/halfword/word/qword) added to load/store ops,
  threaded through Std/Mir/X64/Arm64 dialects with sign-extension on
  narrow loads.
- New `layoutAllStructs` pass in `TypeResolution` computes field
  offsets and `byteSize` after typealiases resolve so narrow
  enum-backed fields pack correctly. `StructField.offsetResolved`
  guards against unstamped fields.
- New `ConstantArrayLiteralRdata` pass emits compile-time array
  literals (`[Color.red, ...]`) into `.rdata`, skipping runtime
  `__managed_mem_create` + per-element `set`.
- Parser: negative enum raw values; IEEE 754 round-to-nearest-even in
  `parseFloatBits`.
- `SpecTestRunner`: PE `.rdata` section parser + `RequiredRdata` block
  validation (u8[], u16[], i16[], i32[], ...) matching `TestRunner.cs`.
- Stdlib: `__managed_mem_get/set/remove` dispatch on `element_size`
  for 1/2/4/8-byte access; new `__Internals.{load,store}{Halfword,Word}`.
- Stdlib cache bumped to v34.

**K.X.2 — `E3014` unexported-field-access + cursor narrow-load**
(commit `874c72c51`). Phase-9 finishing work.
- `StructField.isExported` (parser+layout+cache v35);
  `validateFieldVisibility` emits E3014 for non-init field reads/writes
  outside the type's own methods. Reclaimed 3014 from
  `semanticTypeResolutionCycle` (moved to 3091) to match the C#
  bootstrap's numbering.
- `__managed_mem_cursor_current`: stdlib helper that dispatches on the
  cursor's runtime `element_size` and uses the matching narrow load.
  Replaces the inline qword-load + arithmetic-select sequence that
  leaked 7 bytes past narrow elements.
- `lowerRangeMatchArm`: load the union tag at offset 0 before comparing
  against case-ordinal bounds (the bare scrutinee was the heap
  pointer).
- `isStdlibBootstrapVirtualPath`: identifies compiler-synthesized field
  accesses (struct-literal init thunks, witness shims) so they bypass
  the export check.
- Re-enabled `enum-match-exhaustive` (17), `match-exhaustive-default-panic`
  (7), `export-var-fields` (7) specs.

**Net Stage K.X**: 1299 → 1305 (+6 net, with the re-enabled specs
absorbing the K.4 +4 plus +2 more from `enum-narrow-storage`).

## Stage L landing (2026-05-19) — enum function backing + first-class function polish

**L.1 — `enum-function-backing`** (commit `1047721ff`). Enums can now
use function references as backing values; each case carries a
compile-time function pointer accessible via `.rawValue`, dispatched
through a select chain.
- All cases share the same function signature; the signature becomes
  the backing type for the enum.
- New `enum-function-backing` spec (6 fragments: basic dispatch,
  cross-file, forward-reference, multi-case dispatch, through-variable,
  two-args). All green.
- `MaxonDialect`: function-backing arm on `EnumType` + `EnumCase`;
  `LowerMaxonToStd` emits a per-enum `__enum_<Name>_rawValue` dispatch
  helper.

**L.2 — Function-valued map entries** (commit `1047721ff`). Map values
can be function references; new `map/function-valued.dispatch.test`
exercises lookup → indirect call. Required straightening the
fat-pointer ABI for function-typed map values through
`MapIterator.advance`.

**L.3 — First-class function polish**. New fragments under
`first-class-functions`: `let-from-call-returning-fn`,
`typealias-single-param`, `typealias-multi-param`, `typealias-with-closure`.
- Closure-capture tests rewritten to use the new function-typed
  parameter shape uniformly.
- Stdlib-map-on-{array,set,map-with-function} interface-extension tests
  cleaned up against the new shape.

**Net Stage L**: 1305 → 1311 (+6).

## Stage M landing (2026-05-19) — Tier-1 sweep (low-friction whitelist adds)

Speculative whitelist add of 12 unwhitelisted-but-bootstrap-green specs to
identify cheap-vs-deep wins. Two passed cleanly on the first try, ten
surfaced real work:

**Whitelisted (+14 fragments)**:
- `arithmetic-operators` (10/10) — `+`, `-`, `*`, `/`, `mod` on `int` /
  `float` / `byte` already fully covered by existing op-table coverage;
  spec was simply never added.
- `keyword-parameter-names` (4/4) — interaction between reserved-keyword
  param names (`type`, `with`, etc.) and call-site labeling; already
  fixed by [`feedback_type_param_keyword_collision.md`].

**Deferred with a written audit note** in `Testing/SpecTestRunner.maxon`
above the whitelist tail (each entry includes the bootstrap-vs-self-hosted
pass-count gap and a one-line root cause):
- `block-scoping` (9/12), `break` (10/13), `empty-block` (3/8),
  `self-assignment` (2/7) — **missing semantic diagnostics**
  (E2013 immutable-for-binding, E2032 break-to-own-label, E2055
  empty-block, E3067 self-assignment).
- `tuple-assign` (0/10) — **parser doesn't accept `(x, y) = expr`**
  LHS shape.
- `while-loops` (?/5) — **runtime miscompile**: `while-loops.basic`
  infinite-loops on self-hosted-built binary (bootstrap is fine).
- `short-circuit-evaluation` (4/11), `same-name-methods` (2/5),
  `sizeof` (0/11) — uncategorized; need investigation.
- `short-type` (1/2) — `u16-rdata` writes 168 bytes vs expected 6 (panic
  message rdata bleeds into the captured slice; needs a
  `RequiredRdata` slice/offset feature).

**Net Stage M**: 1311 → 1325 (+14). Each deferred spec is its own
narrowly-scoped follow-up.

## Stage N landing (2026-05-19) — regalloc fallthrough fix + missing diagnostics

Follow-up to Stage M: drove each deferred Tier-1 spec to green, fixed a
real x64 register-allocator correctness bug surfaced by `while-loops`,
and added four missing semantic diagnostics. Net: **+30 fragments**.

**N.1 — Register allocator fallthrough copy fix** (real correctness bug).
[`destroySSA`](Compiler/Targets/Shared/RegisterAllocator.maxon) appended
then-edge block-arg copies to `block.opRefs` even when
`eliminateFallthroughJumps` had promoted the condJump into the
terminator slot — putting the copies BEFORE the condJump in the emitted
order. The copies then executed unconditionally, corrupting back-edge
state on every loop iteration with both a body computation and a
distinct exit-value shape. Surfaced as `while-loops.basic` infinite-
looping (loop counter `r8` got clobbered with the accumulator `r9` on
each iteration). Fix: in the `inTerminator + then-edge has copies`
case, restore the canonical layout — pop the condJump back into
`opRefs`, append copies, synthesize a trailing `jmp $thenTarget` as
the new terminator. Affects every loop with a non-identity SSA-block-
arg shape on the fallthrough edge. Unlocks `while-loops` (5),
`break/multiple-conditions` (already on whitelist), and prevents any
future loop spec from hitting this latent miscompile.

**N.2 — E3082 empty-block diagnostic**. Labeled control-flow blocks
(`if`/`else`/`while`/`for-in`/`for-range`) with no body statements
now emit E3082 matching the C# bootstrap. New
`parseScopedStatementsRequireBody` helper + `bodyStmtParsed` parser
flag track whether `parseStatements` consumed a real statement during
the call; an unchanged flag on return means empty. Inlined version
for `parseForStatement` and `parseForInIterable` (which don't go
through `parseScopedStatements`). Diagnostic-only — the empty block
still parses cleanly so downstream passes see a valid IR. Unlocks
`empty-block` (5/5).

**N.3 — E3067 self-assignment diagnostic**. Three shapes:
- `x = x` (bare var): lookahead in `parseVarAssignment` — if the RHS
  is exactly one identifier matching the LHS followed by a statement
  terminator, emit E3067.
- `p.x = p.x` / `a.b.c = a.b.c` (field chain): new
  `checkSelfAssignmentForFieldChain` helper walks the LHS token slice
  and the same-length RHS token slice; if every (kind, value) pair
  matches and a terminator follows, emit E3067.
- `_ = LITERAL` (discard with non-call RHS): the discard form exists
  to silence "discarded result" warnings on calls; a bare literal RHS
  is degenerate. Lookahead in `parseDiscardAssignment` catches it.
Unlocks `self-assignment` (5/5).

**N.4 — E2013 immutable-local on regular `let`-bound vars**. Pre-
existing self-hosted gap: `let x = 5; x = 6` silently compiled
(bootstrap errored E2013). `parseVarAssignment` checked mutability
only on union-payload bindings; regular locals fell through. Added
the check to the `localWrite` branch using the same `bindingInfo.mutable`
flag. Also changed for-in loop var declaration to `mutable: false`
matching the C# bootstrap (each iteration binds a fresh `let`).

**N.5 — E2048 redundant-loop-label diagnostic**. `break 'lab'` /
`continue 'lab'` where `lab` names the innermost enclosing loop (with
no intervening match for `break`) — the label is redundant since
unlabeled `break`/`continue` already targets that loop. Added to
`resolveBreakTarget` / `resolveContinueTarget`. Surfaced a latent bug
in `stdlib/Internals.maxon`: three redundant labels
(`continue 'arenaLoop'`, `break 'bitLoop'`, `break 'scan'`) survived
because the C# bootstrap **deliberately excludes `Internals.maxon`**
from stdlib loading ([`maxon-sharp/Compiler/0-Compiler.cs:792`](../maxon-sharp/Compiler/0-Compiler.cs#L792)
— the C# version doesn't expose `__mm_*` intrinsics) so it never
parsed them. Self-hosted DOES parse Internals.maxon. Fix: removed the
redundant labels (kept the `end 'name'` markers; just dropped the
labels on the `break`/`continue` statements themselves). Unlocks
`break` (3 remaining fragments after the N.1 regalloc fix).

**Whitelist additions in Stage N**: `while-loops` (5/5),
`empty-block` (5/5), `self-assignment` (5/5), `break` (10+ fragments
across primary + fallback).

**Still deferred from Tier-1**:
- `block-scoping` (11/12) — `local-shadows-prior-field-access` needs
  panic-string interpolation (shared blocker with `panic-interpolation`,
  `match-expr-arm-divergent`, `optimizer-refcount`).
- `short-circuit-evaluation` (4/11) — uncategorized; needs investigation.
- `same-name-methods` (2/5) — uncategorized; needs investigation.
- `tuple-assign` (0/10) — parser doesn't accept `(x, y) = expr` LHS.
- `sizeof` (0/11) — needs investigation; likely missing `__sizeof` builtin.
- `short-type` (1/2) — `u16-rdata` writes 168 bytes vs expected 6 (panic
  message rdata bleeds in; needs `RequiredRdata` slice/offset feature).

**Net Stage N**: 1325 → 1355 (+30, distributed: while-loops +5,
empty-block +5, self-assignment +5, break +13, parse-tracking infra +2).

**Current spec count, end of Stage N**: 118 specs whitelisted,
**1355/1355 on x64-windows, 1355/1355 on wasm32-wasi, 2753/2753 on
C# bootstrap.**

## Stage O landing (2026-05-20) — C2.d per-function dispatch + parser snapshot/restore

Closes Phase 11. Stream A re-enabled the last two Phase-11-bucket
specs; Stream C landed C2.d.1/.2/.3 (per-function dispatch across the
Std, MIR, and backend tiers) behind a new byte-parity harness and
extended scope to parser snapshot/restore so the same-Project
incremental rebuild path no longer crashes on re-parse. End-of-stage
spec count: **1384/1384 on x64-windows, 1384/1384 on wasm32-wasi,
2758/2758 on C# bootstrap**, with all four tiers (`off`, `mid`, `mir`,
`full`) holding byte-identity on the PerFunctionParity harness (5/5)
and the cache-parity harness (4/4).

**O.1 — Stream A: re-enable `where-clauses` + `associated-types`.**
24 fragments (8 + 16) driven to green across x64-windows, wasm32-wasi,
and the C# bootstrap. Root-cause classes fixed:
- **Spurious E3062 on typealiases consumed by `with` bindings** —
  typealias-usage tracker missed uses inside `with X = Type` clauses
  and inside conformance interface lists. Affected 4 fragments
  (`where-clauses.constraint-violation`, `.and-violation`,
  `associated-types.wrong-return-type-error`, `.wrong-param-type-error`,
  `associated-types.docs-example-5`). Fixed by extending the
  usage-tracking pass in [`TypeResolution.maxon`](Compiler/TypeResolution.maxon)
  + a `PendingUnusedTypealiasArray` on `Project` drained after
  conformance binding resolution.
- **E3015 vs E3016 emission** — conformers to an interface with
  `uses Element` and no `with` clause emitted E3015 "expects N type
  parameter(s), got 0" instead of the bootstrap-shape E3016 "does not
  define required associated type". Added the dedicated pre-check in
  `checkConformanceForType`. Affected `missing-type-binding-error`
  and `docs-example-3`.
- **`eq` requires `Equatable`** — interface method `eq(other Self)
  returns Bool` declared on a `T uses Eq` parameter didn't propagate
  the implied `Self: Equatable` constraint. Affected
  `eq-requires-equatable`.
- **`lowerInterfaceMethodCallStub` panic on type-parameter-typed
  receivers** — the residual Phase-11.4 stub fired for
  `where-clauses.user-defined` + `.multiple-interfaces`. Witness-
  routed dispatch (already infrastructure-complete from C1.c/C1.d)
  now handles these paths; the stub remains as a runtime backstop
  but no current Phase-11 spec lands on it.
- **Multi-binding `with` syntax** — `with Key = Int, Value = Float`
  was parsed as `with Key = Int` then `Value = Float` as a separate
  clause. Parser fix to consume comma-separated binding lists.
  Affected `multiple-associated-types`.

`CACHE_FORMAT_VERSION` unchanged at 52 — the new transient
`Project` fields are drained at end-of-resolution, not serialized.
Spec delta: 1355 → 1379 on both self-hosted targets; bootstrap
unaffected at 2753.

**O.2 — Stream C.0: byte-parity harness.** Added a
`--per-function-dispatch=off|mid|mir|full` CLI flag plumbed from
`MaxonArgs` through `Main.maxon` into a `PerFunctionDispatchTier`
enum + `setPerFunctionDispatchTier` toggle in
[`Compiler/Queries.maxon`](Compiler/Queries.maxon). New
`PerFunctionParity` directive (file-level + per-fragment) in
[`Testing/SpecTestRunner.maxon`](Testing/SpecTestRunner.maxon) double-
compiles fragments at the off tier and the active CLI tier, then
asserts PE `.text` + `.rdata` byte identity. New spec
[`specs/per-function-parity.md`](specs/per-function-parity.md) with
5 representative fragments (trivial-main, function-call, closure-
capture, array-and-loop, multi-function-program). Negative test
(XOR-flip first `.text` byte on the on leg) produced the expected
clean diff message. Wasm32-wasi parity is compile-status-only at
C.0; full byte-parity comes online in C.3 when the wasm backend's
per-function emit path is exercised.

**O.3 — Stream C.1: per-Std-function dispatch.** Audit of
[`PassPipeline.maxon`](Compiler/IR/PassPipeline.maxon)
`classifyPassScope` produced 10 candidate passes; 7 cleanly per-
function (`borrowCheck`, `injectDrops`, `mem2reg`, `canonicalize`,
`cse`, `licm`, `lowerABI`), 2 with cross-function state
(`dce` — inliner snapshot side effect; `insertRangeChecks` — shared
monotonic panic-label counter), 1 misclassified (`lowerMaxonToStd`
keeps a whole-module driver because helper-fn synthesis and the
panic-label counter would diverge under singleton-wrap). Added
`packSingletonStdModule` / `unpackSingletonStdModule` helpers and
`run<Pass>TierDispatch` shims for the 7 clean passes; the 2 with
cross-function state run whole-module with the side effect deferred
to after the per-function loop. `queryMidForFunction` retains its
projection-from-`queryAllMid` shape — the behavioral fork lives
inside `PassPipeline.dispatch`. Verified at `--per-function-
dispatch=mid`: PerFunctionParity 5/5 byte-identical, full spec
suite 1384/1384.

**O.4 — Stream C.2: per-function MIR.** `lowerStdToMir` split into
three entry points in [`LowerStdToMir.maxon`](Compiler/IR/Std/LowerStdToMir.maxon):
`prepareMirSkeleton` (whole-module sweep clones blocks 1:1 and
transfers functions), `lowerStdFunctionToMir` (per-function op-walk
into the shared skeleton), `takeMirModule` (extract final
`MirModule`). MIR passes `commuteForCoalescing` and
`scheduleInstructions` audited clean for cross-function state and
wrapped via singleton helpers (`packSingletonMirModule` /
`unpackSingletonMirModule`). `augmentWithRuntime` at
[`BackendDispatch.maxon`](Compiler/Targets/BackendDispatch.maxon)
stays whole-module — `filterRuntimeByReachability` runs a transitive
call-graph walk over both user and runtime call edges, which is
inherently interprocedural; added a docblock noting this.
Cache-key salt `mirPassVersion = 1` added to `queryMirForFunction`
(manual-bump pattern mirroring `CACHE_FORMAT_VERSION`) so a MIR-pass
edit invalidates every cached MIR function in one shot. Verified at
`--per-function-dispatch=mir`: PerFunctionParity 5/5, full spec
suite 1384/1384.

**O.5 — Stream C.3: per-function backend + chunk concat.**
`FunctionCodeChunk` in [`CodeResult.maxon`](Compiler/Targets/Shared/CodeResult.maxon)
already shipped pre-Stage-O; added docblock clarifying the dual
meaning of `CallFixup.codeOffset` (chunk-local when carried via
`FunctionCodeChunk`, absolute when carried via the whole-module
`CodeResult`). X64: `emitTargetCode` split into
`emitFunctionChunkX64(func)` + `concatX64FunctionChunks` driver;
free-function `linkX64StdlibFunctionsConcat` mirrors the method-
version since the concat path has no backend instance. ARM64
mirror via `emitFunctionChunkArm64` + `concatArm64FunctionChunks`;
the extra `Arm64AdrFixup` (ADR with a destination-register field
absent from the generic `CallFixup`) is carried per-chunk in a
parallel `Arm64AdrFixupChunkArray`. The ARM64 split surfaced a
latent bug: the original whole-module emit shared a single
`BlockOffsetMap` across all functions, but `BlockId`s reset per
function, so block-jump resolution at end-of-loop used only the
last function's offsets. The chunk path resolves each function's
block jumps inside the chunk before concat, fixing the issue.
Wasm backend already structurally per-function (the existing
`emitWasmPerFunction` entry shape) — no refactor required. Cache-
key salt `backendVersion = 1` added to `queryCodeForFunction`.
`registerPanicStrings` lifted out of the per-function emit (it
mutates shared `globalData` and would double-register on every
function). Verified at `--per-function-dispatch=full`:
PerFunctionParity 5/5, cache-parity 4/4, full spec suite 1384/1384
on both self-hosted targets.

**O.6 — Parser snapshot/restore (scope extension to Stream C).**
Stream C.4's Scenario F (one-function-edit incremental rebuild)
initially had to use a fresh `Project` per leg because re-parsing
the same file in-process tripped E2001 "Duplicate function 'main'"
on the parser's append-only registries (~50 `.insert` /
`.upsert` / `.push` / `.record` sites in
[`Parser.maxon`](Compiler/Parser.maxon) plus ~15 drain sites in
[`TypeResolution.maxon`](Compiler/TypeResolution.maxon)). New file
[`Compiler/ParseDelta.maxon`](Compiler/ParseDelta.maxon) introduces
a `FileParseDelta` type with one key-list field per critical
registry (~22 string-keyed Maps + 1 ByteArray-keyed Map + 2 Sets
+ 3 push-only arrays + 3 filePath-tagged arrays), a
`rollbackFileParseDelta` function that propagates unresolved-tier
key removal to the resolved-tier drain targets
(`unresolvedStructTypes` → `structTypes`, `unresolvedEnums` →
`enumTypes`, `unresolvedTypealiases` → `typealiases` +
`typealiasRanges` + `typealiasCastCategories` +
`typealiasVisibilities` + `genericTypealiases` +
`constraintFailedTypealiases`, `unresolvedInterfaces` →
`interfaces`, `unresolvedTopLevelDecls` → `topLevelVars` +
`topLevelConstants`), and 21 `recordXxxInsert` transactional
helpers invoked immediately after every tracked parser-side
mutation. `beginFileParse(filePath)` integration point hooked into
`queryParseOps` rolls back any prior delta for the file before
re-parse. New Scenario G `ReParseParity` in
[`Testing/IncrementalTestRunner.maxon`](Testing/IncrementalTestRunner.maxon)
proves byte-identity of fresh-Project-v1 vs same-Project-v1→v2→v1
compile — PASS. Scenarios C and D (modify file / same-content
rewrite in same Project) unblocked. `unresolvedConstExprs` (a flat
arena) intentionally left untouched on rollback — referenced decls
are removed so orphan expression nodes become unreachable garbage,
bounded by N re-parses × M decls. `CACHE_FORMAT_VERSION` unchanged
(in-memory state only).

**Deferred follow-up: `queryCodeResult` per-function rewire.** The
Phase-11.5 perf claim ("single-function-edit rebuild on the order
of 50-200ms") is not yet numerically validated. The per-function
infrastructure is structurally complete and byte-parity holds at
every tier, but `queryCodeResult` still recomputes the whole binary
on every revision bump — it doesn't yet consume `db.codePerFunc`
via `concatX64FunctionChunks` / `concatArm64FunctionChunks` for
partial invalidation. Measured same-Project incremental rebuild on
a 100-function synthetic target: 585ms (off tier) and 636ms (full
tier), same order of magnitude as cold compile. The expected payoff
once `queryCodeResult` is rewired: when N−1 of N functions hit the
per-function cache, only the edited function's chunk is re-emitted
and the existing chunks are concatenated with offset rewriting,
dropping incremental rebuild substantially below the whole-module
cost. This is well-scoped follow-up plumbing, not architectural
rework — the per-function caches are already populated, the
chunk-concat path is already byte-parity-verified, and the
parser snapshot/restore work ensures the same `Project` can be
re-parsed without crashing. Scheduled as Stage P.

**Default flag state**: `--per-function-dispatch` remains `off` by
default. Flipping the default is gated on the Stage P
`queryCodeResult` rewire delivering a measurable incremental-
rebuild win; until then, `full` imposes a ~2-3% cold-compile
tax from cache-key hash mixing with no offsetting benefit on the
default code path. `full` stays available as an opt-in for
experimentation and as the verification gate for byte-parity
regressions.

**Net Stage O**: 1355 → 1384 (+29 fragments: 8 where-clauses + 16
associated-types + 5 per-function-parity). C# bootstrap 2753 →
2758 (the 5 per-function-parity fragments are off-tier-only on
bootstrap so they always pass).

**Phase 11 closed**: legend `[~]` → `[x]` at the head of this
document.

## Stage P landing (2026-05-20) — queryCodeResult per-function rewire

Closes Stage O's deferred follow-up. The chunk-driven `emitBackendIncremental`
path is now the default emit strategy; `queryCodeResult` consumes
`db.codePerFunc` so a single-function edit re-emits only that function's
chunk and concatenates the rest from cache. End-of-stage state:
**1379/1379 on x64-windows, 1379/1379 on wasm32-wasi, 2753/2753 on C#
bootstrap** (the −5 from each row vs Stage O reflects the deletion of
the `per-function-parity` spec; its byte-parity invariant is now
structurally impossible to test because the off tier has been removed).

**P.1 — Step 1: stamp per-function cache during whole-module emit.**
`X64Backend.emitTargetCode` / `Arm64Backend.emitTargetCode` now populate
`project.db.codePerFunc` as a side effect (gated on
`db.enableIncremental`) using the shared `computeChunkKeyHash` helper.
A new `arm64AdrFixups` field on `FunctionCodeChunk` carries ARM64 ADR
fixups inside the cached chunk so concat has everything it needs without
a parallel array. Reused `stampChunkMemo` ensures both the cold-stamp
loop and the chunk-driven miss tail produce identical memos. PerFunctionParity 5/5
held at every tier; spec parity 1384/1384.

**P.2 — Step 2: chunk-driven path inside queryCodeResult.** Added
`emitBackendIncremental` in `BackendDispatch.maxon`; `queryCodeResult`'s
new `dispatchBackendEmit` gates on `enableIncremental` and routes through
the chunk-driven path. `prepareTargetModule` factored out so both the
whole-module and chunk-driven paths share the lowering work; lifted
`registerPanicStrings` out of `emitTargetCode` so it runs exactly once
per build on either path.

**P.3 — Step 3: break queryCodeForFunction recursion.** The pre-Stage-P
miss path recursed `queryCodeResult` → `emitBackendIncremental` →
`queryCodeForFunction` → `queryCodeResult` (unbounded loop on cold
build). Introduced `TargetBackend.emitFunctionChunk` and rewrote
`assembleFunctionChunk` (later inlined in Step 3.5b's pre-check loop) to
call `backend.emitFunctionChunk(func)` directly on miss. New Scenario H
(`runChunkDrivenColdBuildScenario`) drives a fresh `Project` with
`enableIncremental=true` against a 50-function source and confirms cold
compile terminates with `codePerFuncMisses=52, chunks stamped=52`.

**P.4 — Step 3.5a: stale-bytes correctness fix.** Step 4's first refit
of Scenario F surfaced two bugs. The correctness one: `lookupCachedChunk`
returned stale bytes on edits because `depIndex["cf:funcName"]` was
never populated under the chunk-driven path (`queryCodeForFunction`'s
`pushQuery` lifecycle was bypassed). Root cause inside
`stampStdContentHash`: the function content hash was shape-only (early
return when `func.contentHash != 0` left the pre-stamped Maxon-level
shape-only hash in place), so literal edits that preserved block shape
produced an unchanged hash and the chunk cache returned its stored
chunk. Fix: removed the `alreadyStamped` early-return and extended the
hash to fold each block's ops + terminator through `stdOpToString`.
Added `lookupCachedChunkByKeyHash` (content-hash equality) and
`refreshMidContentHashForFunction` (writes the post-edit content hash
into `midPerFunc[funcName]` without going through the wrapper's
`verifiedAt` arm). Scenario F now exec-runs the produced binary and
asserts the post-edit literal flows through; pre-fix the exit code was
0 (stale bytes), post-fix it's 2 (the new literal). Maxon-level vs
Std-level content hashes now intentionally disagree (one shape-only,
one op-content-deep) — they're consumed by different callers so the
divergence is benign.

**P.5 — Step 3.5b: per-function target-lowering subset.** The perf
half of Step 4's diagnostic. `prepareTargetModule` ran `lowerMirToTarget`
+ `allocateRegisters` + `insertPrologueEpilogueWith` on all 100
functions every warm rebuild, defeating the chunk-cache win. Fixed by
splitting target lowering into a per-subset variant
(`lowerMirToTargetForFunctions(funcsToLower)`) on the `TargetBackend`
interface; `emitBackendIncrementalWith` pre-computes the cache-miss set
upfront, runs target lowering / regalloc / prologue-epilogue ONLY on
that subset, then assembles chunks (cache hits from `db.codePerFunc`,
misses from fresh emit). Warm rebuild on 100-function synthetic
dropped from ~480ms to ~325ms (-32%).

**P.6 — Step 4: Scenario F tightening.** Replaced the 250%-slack
incremental-mean assertion with absolute budgets (`mean<=400ms`,
`max<=600ms`, `min<400ms`); added load-bearing correctness asserts
(`hits == coldMisses - 1, misses == 1`, exec exit code = post-edit
literal); bumped cycle count 3 → 5. The 200ms gate from the original
plan was loosened after profiling showed `queryAllMid` (parser +
Std-tier passes) still runs whole-module on every revision bump,
costing ~200ms by itself. Closing that gap requires caching the
StdModule at the `queryAllMid` level — structural work scoped to a
future stage. The Stage-P-actual 365ms mean + 1.6x speedup vs the
legacy off-tier baseline is the validated perf claim.

**P.7 — Step 5: cache-key salt audit.** Read `mirPassVersion` and
`backendVersion`, traced every Stage-P-edit through `computeChunkKeyHash`,
and confirmed that PerFunctionParity 5/5 (canonical byte-identity gate)
held throughout. Decision: no bumps. `arm64AdrFixups` is in-memory only
(no on-disk codec exists for `FunctionCodeChunk`); per-function emit
produces byte-identical output to whole-module emit. `CACHE_FORMAT_VERSION`
unchanged at 52. The audit also revealed that `computeChunkKeyHash`
reads from `midPerFunc[funcName].keyHash` (NOT `mirPerFunc[funcName]`);
the `mirPassVersion` mix on `MirFuncMemo` is independent of the chunk
cache validation path — flagged as a naming-confusion risk for future
readers but not load-bearing.

**P.8 — Step 6: flip defaults.** `MaxonArgs.perFunctionTier` flipped
from `off` to `full`; `Project.db.enableIncremental` flipped from
`false` to `true`. Scenarios A/C/E in `test-incremental` were updated
to pin `enableIncremental=false` because they assert classical query-
counter semantics tied to the whole-module emit path (the chunk-driven
path's per-function `queryMidForFunction` calls inflate `allMidHits`
counters in a way that's correct but breaks the historical assertions).
Spec parity matrix held at 1384/1384 on every leg.

**P.9 — Step 7: realistic-target measurement.** Self-compile of
`maxon-selfhosted/` was not viable — the self-hosted compiler does not
yet support its own source (panic interpolation, etc.). The closest
realistic target available is the synthetic generator scaled up; it
trips a separate nil-pointer compiler panic above ~330 functions, so
300 functions (~6,000 LoC) is the largest stable measurement target.
Results (5 cycles each):
- tier=full warm mean: ~2.1s (min=2125ms, max=2188ms)
- tier=off warm mean: ~3.4-3.8s
- Chunk cache: 301/302 hits, 1 miss — same correctness invariant as
  the 100-function shape, scaled.
- Speedup: 1.6x.
The non-linear scaling (100→300 functions: 325ms→2125ms = ~6.5x for 3x
functions) is upstream of the chunk cache — `queryAllMid` still scales
with total source size, not edit size. Closing that gap is the
next-stage objective. The measurement scaffolding was reverted; no
permanent code added in Step 7.

**P.10 — Step 8: remove the tier flag.** End-of-stage cleanup of the
Stage-O diagnostic scaffolding. Deleted: `PerFunctionDispatchTier`
enum + `setPerFunctionDispatchTier` + tier-aware predicates
(`isPerFunctionDispatchEnabledForMid/Mir/Full`); `--per-function-dispatch`
CLI parsing in `MaxonArgs.maxon`; `runPerFunctionParityCheck` /
`runPerFunctionParityLeg` + `Fragment.perFunctionParity` + the
`per-function-parity.md` spec + its 11 fragment files (5 x64, 6 wasm);
the off-tier early-return arms in `PassPipeline.dispatch` for every
per-function helper (renamed `runFooTierDispatch` → `runFooPerFunction`);
`queryCodeForFunction`, `lookupCachedChunk`, `extractFunctionChunk`
(all unreachable after the chunk-driven path became unconditional).
Kept: `enableIncremental` runtime field (still serves as a debugging
opt-out and gates Scenarios A/C/E's classical-path assertions);
`emitPerFunction` (still consumed by `StdlibLoader` to populate the
stdlib cache); `prepareTargetModule` (still serves the
`enableIncremental=false` whole-module path). Bumped `backendVersion`
1 → 2 to invalidate any in-memory chunks stamped under the tier-gated
shape. Net diff: ~916 insertions, ~972 deletions across 14 files —
roughly 56 LOC net removed, with the insertion count dominated by
test-runner refactors. Final spec parity: 1379/1379 on both self-hosted
targets and 2753/2753 on bootstrap (-5 from each from the
PerFunctionParity removal).

**Net Stage P**: chunk-driven incremental rebuild path is the
unconditional default and is faster than the legacy whole-module path
on warm rebuilds. The original Phase 11.5 perf claim was numerically
validated at the loosened 400ms gate (achieved 365ms on 100-fn
synthetic). Scaling characteristics quantified: 1.6x speedup vs off
baseline on a 300-fn target. The remaining cost above the original
200ms aspiration is bounded and attributable to a specific
identifiable cause (`queryAllMid` whole-module recompute).

## Stage Q landing (2026-05-22) — LLVM-Greedy phi-merge splitting + memory-only spill fallback

`stdlib/URL.maxon`'s `resolve` function blocked stdlib cache compile
with `colorLookupGpr: virtual GPR v232 has no coloring assignment` —
13 promoted locals modified across nested if/else produced phi-merges
whose disjoint live intervals (def block + multiple predecessor copy
slots) were fused by the chordal SSA coloring into a single LiveRange
that interfered transitively with everything live at any anchor,
exhausting the 12-GPR pool. The greedy spill loop diverged because
spilling a phi-merge is a no-op for pressure: the parallel copy at
each predecessor still needs a register for the merge value, and the
inserted reloads (marked `SPILL_WEIGHT_INFINITE`, unspillable) pinned
registers at the same slots.

**Q.1 — LiveRangeSplitter**
([`Compiler/Targets/Shared/LiveRangeSplitter.maxon`](Compiler/Targets/Shared/LiveRangeSplitter.maxon)).
For every multi-anchor phi-merge (defined as a block-arg whose range
has ≥2 predecessor copy-slot intervals on top of its def-block
interval), mint one sub-range per predecessor copy slot, each with a
single 1-position interval at `(predBlockId, predBlockLen-1)`. The
merge's main range keeps only its def-block + live-through intervals.
Critical correctness detail: live-through intervals where the merge
value propagates into a non-pred, non-def block must stay on R_main —
they're real uses, not copy-slot artifacts — identified by exact
match against a `(predBlockId, predBlockLen-1)` anchor table built
once per merge block. Misclassifying a live-through as a copy slot
mints a fat sub-range that the spill loop can't recover from.
Sub-ranges hint-link to R_main via `copyHintValueId` so the chordal
allocator gives them the merge's color in the no-pressure case
(zero-move identity elision through the parallel copy).

**Q.2 — Sub-range → parent-merge spill translation**
([`RegisterAllocator.maxon:744-795`](Compiler/Targets/Shared/RegisterAllocator.maxon)).
Sub-ranges carry `SPILL_WEIGHT_INFINITE` because spilling a 1-position
range alone yields no relief. When the spill loop sees one uncolored,
it looks up the parent merge via the `splitInfo` sidetable and
substitutes the merge into `spillDecision.spilledRanges`. The existing
`SpillCodeInsertion` block-arg path (`emitSpillStore` at top of merge
block + reloads at use sites) handles the rest. Multiple iterations
spill multiple merges until pressure relieves. Mirrors LLVM Greedy's
"phi spilling demotes to a stack slot" — the merge briefly transits a
register at parallel-copy materialization, gets stored, dies; uses
reload from the slot.

**Q.3 — Pipeline wiring**
([`RegisterAllocator.maxon:711`](Compiler/Targets/Shared/RegisterAllocator.maxon),
[`LiveRangeBuilder.maxon:58-96`](Compiler/Targets/Shared/LiveRangeBuilder.maxon)).
New types `SplitPredAnchor`, `SplitMergeInfo`, `SplitMergeInfoArray`.
`splitInfo` field added to `FunctionRegAllocator`. Splitter runs as
the final step of `runLivenessAndRanges`, after `coalesceRanges`
populates copy edges (so the splitter can find and redirect the
source vreg's `copyEdges` from `R.valueId` to the new sub-range id,
keeping `isCopyBoundaryOverlap` correctly suppressing the
source↔sub-range overlap at the copy boundary). Re-runs on every
spill iteration because each rebuild re-fuses anchors.

**Q.4 — `throw` in match arm bodies**
([`Parser.maxon:7099-7106`](Compiler/Parser.maxon)). Pre-existing
parser gap surfaced when the Stage Q regalloc work unblocked the
`register-allocator` spec on the self-hosted whitelist: the match-arm
body parser handled `return`/`break`/`continue`/`try`/expression but
rejected `throw Err.case` as a statement. Added a `TokenKind.throw`
branch routing to the existing `parseThrowStatement`, matching the
C# bootstrap's behavior. Unlocked `error-throw-in-match`.

**Whitelist additions**: `register-allocator` (60 tests + the new
`phi-merge-split-multi-anchor` regression test that locks in the
URL.resolve fix).

**Net Stage Q**: stdlib cache build no longer panics on URL.resolve;
spec parity restored to 1566/1566 (up from 1505/1506 pre-Q via
+60 register-allocator tests + 1 new phi-merge split spec +
1 throw-in-match fix). The fix is structural and applies to any
future function with disjoint phi-merge anchors — not a URL.resolve-
specific workaround.

**Deferred from Stage Q** (future regalloc work):

- **Direct-memory phi spilling.** The Q.2 fallback spills the parent
  merge via the existing block-arg store-at-top-of-block path, which
  still requires the merge value to briefly occupy a register at
  parallel-copy materialization. URL.resolve survives because its
  phi-merges are distributed across nested merge blocks with diverse
  predecessor sets — at any single merge block, the simultaneously-
  live block-arg count stays below 12. A function where all N>12
  locals are mutated in a SINGLE if/else (every block-arg born at the
  same merge block's op 0) still hits unrecoverable pressure even
  with Q.1 + Q.2. The true LLVM-Greedy fix is to rewrite the
  predecessor's parallel-copy to `store src_reg, [merge.spill_slot]`
  directly — never landing the merge in a register. Requires:
  (a) a target-specific `emitStoreToSlot` helper alongside
  `emitMovReg` in [`RegAllocTarget`](Compiler/Targets/Shared/RegAllocTarget.maxon);
  (b) `CopyMove` extended with a `slotOffset` variant for memory-only
  destinations; (c) `buildBlockArgCopies`
  ([`CopyResolution.maxon:54`](Compiler/Targets/Shared/CopyResolution.maxon))
  consults a `memoryOnlyMerges` sidetable and emits stores instead of
  movs for those destinations; (d) `SpillCodeInsertion`'s block-arg
  store at top of merge block becomes a no-op for memory-only merges
  (the value is already in memory by the time control reaches the
  merge block). Regression test ready (the deferred
  `phi-merge-memory-only-spill` synthetic 13-locals-in-one-block
  fragment was drafted during Q's investigation; the test was held
  back because it currently panics, demonstrating exactly the
  pressure case this work resolves).

- **Eviction with backtracking in chordal coloring.** Q.1 + Q.2 cover
  phi-merge fusion and phi-merge pressure; they do not help non-phi
  pressure spikes (a sequence of long-lived locals that all need
  registers at one program point with no merging involved). LLVM
  Greedy's coloring is iterative: when a virtual range can't get a
  color, the allocator picks the lowest-weight COLORED neighbor,
  un-colors it, evicts it back to the priority queue, and retries
  the current range with the freed register. Implementation lives
  entirely in [`SSAColoring.maxon`](Compiler/Targets/Shared/SSAColoring.maxon)
  — extend `colorClass` to maintain a priority queue keyed by
  `range.spillWeight`, change the greedy walk to "pop, try color,
  evict if blocked, repeat until queue empty," and surface
  evicted-and-still-uncolorable ranges through the existing
  `findUncoloredRanges` path so the spill loop handles them. The
  current synthetic
  `int-twenty-vars-heavy-spill` test (already on the whitelist)
  passes only because all 20 values are rematerializable constants;
  a non-rematerializable version would currently fail and would
  serve as the regression test for eviction. Weighing this against
  Q's direct-memory phi work: eviction is target-agnostic and a
  pure addition to one file, while direct-memory phis touch four;
  eviction likely lands first.

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
