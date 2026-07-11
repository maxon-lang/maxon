# maxon-shv2 — Ground-Up Rewrite Plan

## Context

`maxon-selfhosted` (v1) works but "got away" architecturally: features that are *integral* — ownership/borrowing and parallel incremental compilation — were left until last, so they were bolted on via ~8 shared `Project` sidetables and a late 7,755-line refcount inserter. The result is slow to compile, slow to test, and uses 5–6 GB of RAM. This makes it very hard to debug.

We are starting fresh as **`maxon-shv2/`**, keeping what is sound (lexer, the tiered IR, query engine, backends) and rebuilding what is entangled, with the hard/integral features designed in **from the first commit** rather than retrofitted. (v1's IR is 4 tiers; shv2 runs **3** — the MIR tier is dropped as dead weight. See "IR tiers" below.)

1. **Static ownership/borrowing** — compile-time move/borrow checking that drops values at scope exit; runtime refcounting only where escape analysis proves genuine sharing.
2. **Parallel incremental compilation** — using the existing green-thread runtime, with the multi-core prerequisites proven *before* the first compiler milestone.
3. **A binary event-log/tracing system** — DebugStream-style binary events written to shared memory (near-zero overhead when off), consumed by `maxon-sharp` as the runner; it powers `mm-trace` for ownership/memory debugging.

Development is **spec-driven**: copy a spec from `specs/` (or author one) into a new **`specs-shv2/`**, implement until it passes, move on. A **living document** (`maxon-shv2/ARCHITECTURE.md`) documents each part of the compiler as it is built, for future agents; `maxon-shv2/DEVLOG.md` tracks milestones and dated findings alongside it.

**Target / final acceptance:** `maxon-shv2.exe` compiling *itself* in **≤30 s**, **≤1.7 GB RAM**, **>90% CPU** across all cores. (v1 reference: `maxon.exe` compiles v1 in ~30 s / 1.7 GB — this is the bar.)

### Locked design decisions (confirmed with the user)
- **Ownership = static-first**, minimize refcounts; RC retained only as a fallback for genuinely shared/escaping values. May reject some v1-accepted programs (accepted).
- **Parser = direct-emit IR** (keep v1's contract: parser emits Maxon-dialect IR directly + populates declaration tables; no separate AST layer). Reuse v1's *structure* (recursive descent + Pratt), rebuilt incrementally.
- **Parallel-runtime prerequisites land FIRST**, before `basic.maxon` compiles.
- **Gating = correctness-only early**; the ≤30 s / ≤1.7 GB / >90% CPU budgets become a **hard gate once shv2 can compile itself**. Minimum-memory / maximum-CPU is a design discipline at every step regardless.

### Two findings that reshape the plan (verified against the tree)
- **The multi-core runtime is already written — in the C# bootstrap.** `maxon-sharp` (`maxon.exe`) compiles shv2 for its whole pre-self-host life and emits shv2's runtime. Its slab allocator (`maxon-sharp/Compiler/MLIR/Runtime/RuntimeEmitter.Allocator.cs`, `EmitSlabAlloc` ~2146–2209) is **already per-P sharded, lock-free, with an ownership gate + cross-P remote-free MPSC queue**; `__sched_max_procs = ncpu`. The "single shared mcache with no lock" blocker in `PARALLEL_CODEGEN_PLAN.md` describes the *v1 self-hosted* runtime, **not** the C# emitter. So Track 0 Phase 0 is **"prove/harden the already-written sharded allocator under >1 live P (which has never happened)"**, not "write a lockless allocator."
- **The binary event log is already built — in the C# bootstrap.** `RuntimeEmitter.DebugStream.cs` + `DebugStreamMonitor.cs` (`maxon monitor`) implement the shared-memory ring, ticket-spinlock reserve, MM/Sched/Depth/Dbg schema, and PE-embedded tag table. The MM hooks already call `__ds_emit_mm_*` under `Compiler.DebugStream`. **Scope of "for free":** this covers **shv2.exe's own process** (`maxon.exe`-compiled, so tracing the *compiler's* allocations/scheduling costs nothing — key for its RAM/leak debugging). Test programs compiled *by shv2* carry the runtime **shv2's backend emits**, so shv2's backend must port the `__ds_*` producer (schema-compatible) into its emitted runtime before mm-trace spec sections can work — scheduled at M6, when heap values/ownership first activate.

**Net:** "prerequisites first" is much cheaper than it sounds — Track 0 is mostly *validate / harden / wire* work in `maxon-sharp`, stdlib, and the spec harness. shv2 inherits the multi-core runtime and event log in its binary.

---

## Architecture

### Directory layout — `maxon-shv2/` (mirrors `maxon-selfhosted/`)

**COPY near-verbatim** (clean boundaries, stdlib-only deps):
- `Compiler/Lexer.maxon` (1,426-line DFA; `tokenize(source) -> TokenArray`, `Token`/`TokenKind`, `LexerError`) + `Compiler/NumberParsing.maxon`.
- IR containers: `Compiler/IR/IrModule.maxon`, `IrBlock.maxon`, `IrValueId.maxon`, `IrFunction.maxon`, `IrPrinter.maxon`; `Compiler/IR/Maxon/SourceRange.maxon`, `Scope.maxon` (Scope gains a per-scope owned-values list).
- Truly self-contained backend pieces: `Targets/Windows/PeWriter.maxon`, `Targets/Linux/ElfWriter.maxon`.
- ~~`Targets/Shared/RegisterAllocator*` (operate on generic structures)~~ — **AMENDED at M1-B2: false on both counts.** v1's allocator is welded to its bespoke `TargetModule` + `TargetOpQuery`/`OpPattern`/register-mask machinery (not generic), and copying it would import the wrong design regardless: regalloc is **~74% of v1's self-compile wall time** against shv2's ≤30 s whole-compile budget. shv2 REBUILDS it — a no-liveness placeholder at M1, the real allocator at M3/M5. See ARCHITECTURE.md's Register allocator section.
- Spec harness: `Testing/SpecParser.maxon`, `Testing/SpecTestRunner.maxon` (trimmed to the block types specs-shv2 uses, plus the new mm-trace block).

**PORT incrementally, structure preserved** (cannot copy verbatim — they consume the dialects we're rebuilding thin):
- `Targets/X64/*`, `Targets/Arm64/*`, `Targets/Shared/StdOpHelpers.maxon`, InstructionScheduler, PrologueEpiloguePass — ported op-by-op as ops appear in the rebuilt StdDialect, starting from the M1 thin slice (mov/ret) and growing with each milestone. Includes the runtime emitters (`emitX64Gt*` etc.), ported from v1 **but with the allocator mirrored from the C# sharded design, not v1's single-shared-mcache** (see Phase F).

**REBUILD incrementally** (same structure, feature-by-feature):
- `Compiler/Parser.maxon` — keep v1's recursive-descent+Pratt direct-emit contract, **one structural change**: it writes into a per-file `FileParseArtifact` instead of mutating shared `Project` (enables parallel parse; see below).
- `Compiler/IR/Maxon/MaxonDialect.maxon` (extended with ownership), `LowerMaxonToStd.maxon`, all `IR/Std/*` opt passes, `IR/Target/*`, `IR/PassPipeline.maxon`, `Compiler/Project.maxon`, `Queries.maxon` + `QueryEngine.maxon` + `QueryDatabase.maxon`, `TypeResolution.maxon`, `SemanticCheck.maxon`, `Compiler.maxon`, `Main.maxon`. *(v1's `IR/MIR/*` is NOT rebuilt — see "IR tiers" below.)*

**CREATE new** (the static-ownership + parallel machinery):
- `Compiler/IR/Own/OwnDialect.maxon` — `own.*` ops + `OwnershipKind` + lifetime ids. **A FILE, not a tier:** the `own.*` ops are a BAND OF `MaxonOp` (`OpCategory.ownership`), because `IrModule` is generic over exactly one op type — an op in the Maxon block stream IS a `MaxonOp`. Never nest them as `MaxonOp.own(OwnOp)`: that costs a second heap box per op, the anti-pattern the 3-tier collapse removed from `StdOp`. See ARCHITECTURE.md's Own tier section.
- `Compiler/IR/Own/OwnershipInfer.maxon` — signature-ownership inference (replaces `ParamConsumeAnalysis` + `ReturnBorrowAnalysis`).
- `Compiler/IR/Own/OwnershipCheck.maxon` — move/borrow/use-after-move checker (evolves `MaxonBorrowCheck.maxon`).
- `Compiler/IR/Own/EscapeAnalysis.maxon` — unique-vs-shared classification.
- `Compiler/IR/Own/InsertDrops.maxon` — static `own.drop` + escape-driven `own.retain/release` (replaces the 7,755-line `InsertRefcounts.maxon`).
- `Compiler/ParseStaging.maxon` — `FileParseArtifact` + deterministic merge (generalizes v1's `ParseDelta.maxon`, which already enumerates the ~25 registries the parser touches).
- `Compiler/ParallelDriver.maxon` — green-thread fan-out over per-file parse + per-function passes.
- `specs-shv2/`, `ARCHITECTURE.md`, `DEVLOG.md`.

### IR tiers with static ownership as a first-class citizen

Tiers are **Maxon → Std → Target** — three, not v1's four. **The MIR tier is deliberately dropped** (amended after M1-C; landed as its own structural commit). Two reasons, both of which only get more expensive to act on later:

- **MIR added no value model.** v1's `lowerStdToMir` is 501 lines of which ~420 are a mechanical 1:1 rename — `ValueId`s pass through verbatim, blocks clone 1:1, `IrFunction`s transfer as-is. Std's `ValueId`s **already are** the infinite virtual registers MIR claimed to introduce. The tier's only real content (~80 lines) is desugaring a handful of Std-only ops, which is a **Std→Std rewrite** (`lowerToMachineForm`), not a tier. Everything v1 ran *on* MIR (`commuteForCoalescing`, `scheduleInstructions`) is now a Std-tier pass.
- **The boundary hid drift.** v1's `MirOp.movReg` has **zero construction sites** yet still forces match arms in the printer, the uses-extractor, `CommuteForCoalescing`, and every backend. A tier wall that nothing crosses is a place dead code goes to survive.

What we give up is v1's *type-level* "no sugar reaches the backend" guarantee (sugar ops existed in `StdOp` with no `MirOp` counterpart, so the boundary made them unrepresentable). It is replaced deliberately, not accidentally: a `sugar` category on `StdOpMeta` + an `assertNoSugarOps` gate at the backend entry + an explicit `panic` arm per sugar variant (never a bare `default`). **Spec tests are the real guarantee** — that was the call, and the type wall was not worth its cost.

**`StdOp` is also FLAT** (one union, no `StdOp.arith(StdArithOp.const(...))` nesting), for a reason v1's own StdDialect header admits: the nesting was a migration artifact kept "so existing pass code that matches on these variants keeps working." Maxon heap-boxes and refcounts every payload-carrying union case, so each nesting level is another heap object — nested is **3 boxes per op**, flat is **1**, on the single most numerous object in the compiler (~half of v1's self-compile cycles go to memory-management churn). Coarse membership is recovered without nesting via the required `StdOpMeta` struct backing (`category`/`role`/`inlinePolicy` + scheduling facts), and variants are declared in **category-contiguous bands** so a pass can cover a whole category with one `match` range arm. **Invariant: append new variants at the END of a band, never insert into the middle** — a range arm silently swallows anything inserted between its endpoints, and this is the one place the "no silent unhandled cases" rule has no compiler backstop.

Ownership is born at the **Maxon tier** (which still has source names, scopes, `SourceRange`) and fully resolved *before* `lowerMaxonToStd`. It lives in **three first-class places, zero sidetables**:
1. **`OwnershipKind` attribute** on every value/binding: `trivial` (scalar, no drop) · `owned` (unique heap, dropped once unless moved) · `borrow` (non-owning view, never dropped, carries a lifetime) · `shared` (escape-promoted to refcount).
2. **Signature ownership modes in the function type**: each param `consume`/`borrow`/`copy`; return `owned`/`borrow`. Callers read ownership straight off the callee signature — the local replacement for v1's whole-module `funcParamConsumes`/`funcReturnsBorrow` fixpoints.
3. **Explicit `Own`-dialect ops in the block stream** (alive Maxon→Std, lowered by `lowerMaxonToStd`): `own.move`, `own.borrow(kind, lifetime)`, `own.drop` (→ `__destruct_T` call), and `own.retain`/`own.release` (**the only surviving runtime refcount ops**, emitted solely for `shared`).

Maxon-tier passes, in order: **OwnershipInfer** (bounded whole-module fixpoint → signature modes) → **OwnershipCheck** (per-function move/borrow/use-after-move + NLL borrow expiry; first program-rejection point) → **EscapeAnalysis** (promote `owned`→`shared` on escape: stored into longer-lived aggregate/global, returned owned where caller can't re-own, captured by escaping closure, or sent across an `async`/channel boundary; sound over-approximation = promote on doubt) → **InsertDrops** (static drops at NLL end-of-life/scope exit, `retain`/`release` for `shared`).

**Contrast with v1:** v1 refcounts every managed value by default and reclaims dynamically (decref→free at 0), deciding ownership late in `LowerMaxonToStd` + `insertRefcounts` from liveness × 8 sidetables. shv2 proves unique ownership statically, drops deterministically at scope exit, and refcounts only escape-promoted `shared` values.

**v1 reuse map:** `MaxonBorrowCheck.maxon` → redesigned/promoted to `OwnershipCheck`. `ParamConsumeAnalysis` + `ReturnBorrowAnalysis` → dropped as passes, folded into `OwnershipInfer` (output = signature types). `InsertRefcounts.maxon` → ~90% dropped, replaced by small `InsertDrops` + thin retain/release lowering. `StdLiveness.maxon` → reused/simplified for NLL/drop placement (backend regalloc keeps its own liveness). `InjectDrops.maxon` (dead in v1) → not ported; its intent realized correctly by `InsertDrops`.

### Pass pipeline (`[F]` per-function parallel-safe, `[M]` whole-module serial, `[m]` per-file parallel-safe)
- **Frontend:** `tokenize [m]` → `parseFile [m]` → `mergeArtifacts [M]` (deterministic, source-path order).
- **Maxon tier:** `resolveTypes [M]` → `semanticCheck [F]` → `ownershipInfer [M]` → `ownershipCheck [F]` → `escapeAnalysis [F]`(+small `[M]` summary) → `insertDrops [F]` → `deadFunctionElimination [M]` → `lowerMaxonToStd [F]`.
- **Std tier `[F]`:** `mem2reg` → `canonicalize` → `cse` → `licm` → `dce` → `inliner [M]` → `dceFunctions [M]` → `insertRangeChecks` → `lowerABI`. *(No `analyzeParamConsumes`/`analyzeReturnBorrows`/`insertRefcounts` — replaced by the Own tier.)*
- **Std tier, machine-level `[F]`** *(what v1 ran on its MIR tier)*: `lowerToMachineForm` (desugars the `sugar`-category ops — descriptors, witness methods, `drop`/`free`, unbox) → `commuteForCoalescing` → `scheduleInstructions`.
- **Std→Target `[F]`:** `assertNoSugarOps [M]` → `lowerStdToX64` → `allocateRegisters` → `insertPrologueEpilogue` → `augmentWithRuntime [M]` → emit → `concatFunctionChunks [M]`. Note `augmentWithRuntime` runs **last, on the TargetModule** — it hand-builds the `mrt_start` entry stub in physical registers with its own explicit frame, so it must land *after* regalloc and prologue/epilogue rather than be reprocessed by them.

### Incremental from the first commit
The query spine is **skeletal from M1**, not retrofitted: content-hash-keyed memoized queries (`querySourceFile` → `queryTokens` → `queryParseOps` → module/mid/code queries) with dependency recording, modeled on v1's `Queries.maxon`/`QueryEngine.maxon`/`QueryDatabase.maxon` but rebuilt against the `FileParseArtifact` staging (per-task dependency buffers merged deterministically, so the query engine is never touched concurrently). Warm-rebuild correctness (edit one file → only its queries re-run; unchanged input → byte-identical output) is asserted continuously from M2 onward, so incrementality never becomes a bolt-on.

### Stdlib & runtime reuse
shv2 compiles programs against the existing `stdlib/` and `runtime.std`/`runtime_wasm.std` (reused as-is; the stdlib compile pipeline must include every user-pipeline pass — v1's fieldInitCheck omission is the cautionary tale). shv2.exe itself links the **C#-emitted** runtime until self-host; binaries **emitted by shv2** get the runtime shv2's backend emits (ported incrementally per milestone: M1 needs only process-exit; MM runtime + DebugStream producer arrive at M6; GT runtime by Phase F).

### Parallel-ready architecture
- **Parse fan-out + deterministic merge** reconciles "direct-emit parser mutates Project" with "parallel per-file parse": the parser writes only into a local `FileParseArtifact` (its MaxonModule fragment + a bundle recording key **and value** for every registry it would touch), so per-file parse is a pure function of `(tokens, prescan summary)` on a per-P arena. `mergeArtifacts [M]` folds artifacts into `Project` in fixed source-path order, doing all duplicate detection at merge time. Token-level prescans (`preRegisterInterfaceNames`, `preRegisterFunctionThrows`) become parallel per-file scans, merged first.
- **Per-function fan-out** turns v1's singleton-wrap shim into real green-thread fan-out: each function is lowered Maxon→Std→Target on a worker into its own arena; only the finished code chunk + content-keyed rdata merge back. This is also the **memory lever** — pipeline one function fully and free its upper-tier forms before the next. Dropping the MIR tier works *with* this lever rather than against it: one fewer whole-module copy to hold, and one heap box per op instead of three.
- **Determinism:** content-derived keys for all shared appends (v1 pattern: FNV-1a panic labels, `__float_<bits>`), ordered per-function merge for the one order-sensitive append (rdata `GlobalDataTable`), and a **1-core-vs-N-core byte-identity harness** as the blocking gate for the whole parallel phase.

---

## Track 0 — Foundations (before `basic.maxon`; mostly `maxon-sharp` + stdlib + harness)

**Recommended internal order: Foundation 2 first** (low-risk, mostly already built, and it provides the observability needed to debug Foundation 1).

### Foundation 2 — Binary event log + mm-trace harness
- **Verify + wire the existing producer.** Confirm `maxon.exe`-compiled binaries (including shv2.exe itself) emit the full MM+sched stream under `Compiler.DebugStream` (already wired in `RuntimeEmitter.MemoryManager.cs`). Feed type names into `EmitDebugStreamTagBlob` so MM events resolve to real names. This gives compiler-process tracing for free; the producer for *shv2-emitted* binaries is M6 work (above).
- **Schema: keep, don't fork.** Preserve the `RuntimeEmitter.cs` schema exactly (128-byte header, ticket spinlock, MM `0x01–0x09`, Sched `0x20–0x2C`, Depth `0x40/41`, Dbg `0x50–0x5E`, `MXDS_TAGS` blob) so `DebugStreamMonitor` works unchanged. New events get new unused type codes, never reinterpret existing ones.
- **Preserve zero-overhead-when-off:** compile-time off (`Compiler.DebugStream==false` → zero instructions emitted) and compiled-in-but-runtime-off (`MAXON_DEBUGSTREAM` unset → `__ds_base==0`, one load + branch per site). Dev builds compile DebugStream *in*, gated at runtime; a release flag strips it.
- **Keep the leak gate.** Do not remove `mrt_leak_check` (exit 101). The event log is the explanatory layer that turns a "101" into a diagnosable leak.
- **mm-trace spec harness (decoder = `maxon-sharp`):** add a new `` ```mm-trace `` fenced-block language; redefine the existing `<!-- MmTrace -->` directive to select binary-log capture instead of `--mm-trace` string stderr. **Orchestration:** shv2's spec runner compiles the fragment with shv2, then invokes `maxon.exe monitor --filter=mm <test.exe>` (which creates the shared segment, spawns the exe with `MAXON_DEBUGSTREAM`, drains the ring, decodes via the PE tag blob) and captures/normalizes its output for comparison — `maxon-sharp` stays the sole owner of the binary-log decoding. Prove the harness end-to-end in Track 0 on a `maxon.exe`-compiled toy program; it starts gating shv2-compiled programs at M6 (once shv2's backend emits the producer). **Normalization for stable goldens:** run mm-trace programs single-threaded (`--max-procs 1`), drop timestamps/addresses, renumber `alloc_id`s to dense `1..N` in first-appearance order. Block format:
  ```
  mm_alloc  <TypeName> #<id> size=<n>
  mm_incref <TypeName> #<id> rc=<n>
  mm_decref <TypeName> #<id> rc=<n>
  mm_free   <TypeName> #<id>
  ```
  Regeneration mirrors the C# `UpdateRequiredInSpecFiles` path with an mm-trace branch.

### Foundation 1 — Prove/harden multi-core green threads (x64-windows only for Track 0)
- **1a.1 Global-lock A/B safety net (ship first):** an env-gated (`MAXON_SLAB_GLOBAL_LOCK`) option to hold the existing `__slab_lock` around the alloc/free fast paths — a *bisection tool* to isolate non-allocator races from allocator races on the first multi-M runs, not the perf target. In `RuntimeEmitter.Allocator.cs`.
- **1a.2 Contention counters:** a lock-wait / ownership-gate-miss counter dumped at exit (byte-identity + leak-check pass even if allocation is fully serialized — only this counter + wall-clock prove the lockless path helps).
- **1a.3 Exercise + harden the never-run cross-P paths** (ownership gate, remote-free MPSC push/drain, acquire/release publication) under the validation harness with leak-check/mm-trace. **Highest-risk item in Track 0** — a validate-and-repair task with real probability of finding a bug.
- **1a.4 `maxon_cpu_count`** runtime helper (direct `GetSystemInfo`, valid before `__gt_init`) → `Process.cpuCount()`.
- **1a.5** Confirm the `__gt_enqueue` worker-spawn gate fires for a pure-CPU GT burst (empirically; no code change expected).
- **1b E3073 relaxation in the C# checker first** (`SemanticCheckPass.cs`, `CheckAsyncYielding` ~163 / `IoStubs` ~100): add a first-class `__Builtins.parallelBoundary()` no-op marker (honest "CPU-parallel work", not fake I/O) and add it to the yield allowlist so shv2's `async` compile tasks compile. Mirror into `maxon-selfhosted` only when shv2 self-hosts (tracked drift).
- **Concurrency API shv2 uses:** a `Parallel.map`/`parallelMap` stdlib helper (buckets `N ≈ max(1, count/(cpuCount()*k))`, spawns `async` bucket tasks calling `parallelBoundary()` once, awaits in input order — mirrors `specs/async-await.md`). Gate parallel codegen on `flag ON && missCount >= ~32 && cpuCount() > 1`; bucketing is the default (each GT mmaps ~1 MiB stack).
- **Deterministic rdata by design:** shv2's backend captures rdata constants chunk-locally and merges into the shared table single-threaded in function order (idempotent-by-label dedup). Documented as a backend invariant from line one, not retrofitted.

### Track 0 validation harness (the gate that says "multi-core truly works")
Built in `maxon-sharp` on a synthetic multi-function program, before shv2 exists:
1. **Byte-identity** serial vs `parallelCodegen` (tiny/below-threshold, ~100-fn, cold-stdlib-scale, warm-rebuild-stays-serial).
2. **A second worker actually ran** — assert via DebugStream sched events (≥2 distinct `P{id}`) or `__sched_active_workers==2`. *Mandatory* — byte-identity alone passes on single-M cooperative execution.
3. **Leak-clean/heap-safe** under mm-trace + exit-101 gate (acceptance test for 1a.3).
4. **Determinism stress** — `--max-procs {1,2,7,ncpu}`, all outputs identical to each other and to serial.

---

## Step 0 — Materialize this plan in the repo
Create `C:\Users\Eric\dev\maxon\maxon-shv2\` and write this plan (this document, verbatim) to `maxon-shv2\PLAN.md` as the first commit alongside initial `ARCHITECTURE.md` / `DEVLOG.md` stubs. The plan in-repo is the working reference; milestones check off against it as they land.

## Milestone sequence (each = spec(s) into `specs-shv2/` + capability to pass)

Correctness-only gate through Phase E; **budget gate becomes hard at Phase F.**

**Phase A — Walking skeleton (thin end-to-end slice, x64-windows)**
- **M1 basics** — compile & run `examples/basic.maxon`; spec `basics.md`. Copy Lexer; thin Parser (function decl, `return`, int literal); thin `MaxonOp` (`literal`, `ret`); minimal lower chain + PE. Ownership scaffolding present but trivial-only. Parser writes to `FileParseArtifact` (staged, single-threaded). **Skeletal query spine from day one** (content-hash-keyed `queryTokens`/`queryParseOps` + dependency recording). Start `ARCHITECTURE.md`.
- **M2 variables** (`variables.md`) — `let`/`var`, block scope, `Scope`. Warm-rebuild assertion joins the gate (unchanged input → cache hit → byte-identical output). **M3 arithmetic** (`arithmetic.md`, `comparison-operators.md`, `unary-operators.md`) — full Pratt precedence.

**Phase B — Control flow & functions**
- **M4** `if-statements.md`, `return-statement.md`, while/break/continue. **M5** `function-declaration.md`, `parameter-labels.md`, `method-calls.md`. **First multi-function compile ⇒ first place parallelism can pay off** (enable per-function fan-out; the runtime prerequisite already exists from Track 0). Signature ownership modes established (all trivial for now).

**Phase C — Static ownership activates (the crux)**
- **M6 heap values & drops** — first `owned` heap value (minimal single-field heap `type` + destructor); `OwnershipCheck`/`EscapeAnalysis`/`InsertDrops` run for real; `own.drop`→`__destruct`. **Where static ownership first bites.** shv2's backend gains the MM runtime **and the DebugStream producer** (schema-compatible port of `RuntimeEmitter.DebugStream.cs` into shv2's emitted runtime) so shv2-compiled programs emit binary MM events — mm-trace spec sections start gating here. New spec `own-drop-basic.md` (mm-trace: one alloc / one free, zero incref/decref).
- **M7 moves & borrows** — port `borrow-checker.md`, `ownership.md` **with redesigned expectations** (some v1-accepted programs now rejected). Use-after-move, double-move, borrow-outlives-owner, NLL mutate-while-borrowed (the original E3070 case).
- **M8 escape → refcount fallback** — a genuinely escaping value promotes to `shared` with `own.retain/release` (the only place refcounting appears). Spec `own-escape-refcount.md`.

**Phase D — Aggregates & strings**
- **M9 structs** (`self-keyword.md`, `static-methods.md`, struct specs) — owned fields → recursive drop. **M10 strings** (`string-type.md`, `string-interpolation.md`) — String is owned heap; real `print()`. **M11 arrays/collections** (`arrays.md`, `array-realloc-dangling-ref.md`) — element ownership, borrow-on-`get`.

**Phase E — Advanced language**
- **M12 enums/unions** (owned payloads → drop). **M13 closures** (`closure-capture.md`) — big escape driver. **M14 interfaces & generics** (`interfaces.md`, `where-clauses.md`, layout/witness tables). **M15 error handling** (`error-handling.md`) — drops on the throw/unwind path.

**Phase F — Self-hosting & budgets**
- **M16 feature-complete** — whatever remains to parse shv2's own source, **including porting shv2's own source to satisfy the borrow checker** (use the explicit `shared` escape hatch where a static proof is impractical). Also: **shv2's backend completes its emitted runtime** — full GT scheduler (port v1's `emitX64Gt*`) **with the allocator mirroring the C# sharded per-P design, NOT v1's single-shared-mcache**, plus mirroring the `parallelBoundary` E3073 relaxation into shv2's own SemanticCheck — so a self-compiled shv2.exe still runs multi-core. **M17 self-compile correctness** — shv2 compiles shv2; the produced compiler passes the spec suite; byte parity across core counts. **M18 budget gate** — full multi-core fan-out at scale; drive to **≤30 s / ≤1.7 GB / >90% CPU**; tune per-P arenas, eager frees, per-function pipelining.

---

## Living documents
Two, both committed alongside code:
- **`maxon-shv2/ARCHITECTURE.md`** — the onboarding document. One section per compiler part, added as it's built (frontend, Maxon dialect, Std dialect, Own tier, pipeline, query spine, parallel driver, backend, event log). It records **operation and invariants** (e.g. the rdata deterministic-merge invariant, the ownership-kind lattice, the parse-staging registry set) — so future agents onboard without re-deriving the design from the code.
- **`maxon-shv2/DEVLOG.md`** — the dated log: the milestone ledger and the recon findings that corrected this plan's premises. Progress and history, not design.

---

## Risks & mitigations
1. **Self-hosting under a stricter checker (highest).** shv2's own source may be rejected by static ownership. Mitigation: the `shared` escape hatch as a pressure valve; a dedicated M16 sub-track to annotate/rewrite source.
2. **Never-run cross-P allocator paths (high).** The C# sharded allocator's ownership gate + remote-free MPSC have zero runtime coverage. Mitigation: global-lock A/B bisection (1a.1), mm-trace/leak-check oracle, contention counter, self-host bootstrap as the strongest end-to-end test.
3. **Runner/subprocess deadlock chains (high).** Multi-M *inside* spec-runner worker processes compounds scheduler + pipe-drain concurrency. Mitigation: **keep the spec-runner harness single-M; only the compile-under-test runs multi-M.**
4. **async-subprocess shard-0 heap corruption (high, specific).** IOCP/non-scheduler-thread allocations have no owning P → fall to shard 0 concurrently with a worker. Mitigation: route non-P-context allocations through the locked path; validate under mm-trace with an async-subprocess workload before declaring Phase 0 done.
5. **Conditional-move drop placement.** A value moved on one branch only needs a drop flag. Mitigation: initially promote conditionally-moved values to `shared`; add real drop flags later as an optimization.
6. **Escape-analysis precision** (too conservative = everything refcounted; too aggressive = UAF). Mitigation: start sound-conservative, measure refcount rate, tighten behind byte-parity + spec suite.
7. **Determinism under parallelism / memory budget with 3 live tiers.** Mitigations: content-keyed appends + ordered per-function merge + 1-vs-N-core parity gate; per-function pipelining with eager frees. (Dropping the MIR tier already removes one whole-module copy from the peak, and flattening `StdOp` cuts each op from 3 heap boxes to 1.)

---

## Verification (how each step is proven end-to-end)
- **Per milestone:** the ported `specs-shv2/` spec passes via the spec runner (`mcp__maxon-dev__run_spec_test` / `spec_test_outcome`); `basic.maxon` runs and exits 42 (`mcp__maxon-dev__run_program`). Ownership milestones (M6+) additionally assert their `mm-trace` block (alloc/free/refcount counts) via `maxon monitor`.
- **Track 0:** the validation harness — byte-identity across core counts, ≥2 workers observed via sched events, leak-clean under mm-trace, `--max-procs {1,2,7,ncpu}` determinism.
- **Parallel phase (ongoing):** 1-core-vs-N-core byte-identical output as a blocking gate.
- **Self-hosting (Phase F):** shv2 compiles shv2, the produced compiler rebuilds and passes the full suite; then the hard budget gate — **≤30 s, ≤1.7 GB RAM, >90% CPU** on self-compile, measured with `--profile-passes` + process RAM/CPU sampling.

### Critical files to create/modify
- `maxon-shv2/Compiler/IR/Own/OwnershipCheck.maxon` (from `maxon-selfhosted/Compiler/IR/Maxon/MaxonBorrowCheck.maxon`) and `Own/InsertDrops.maxon` (replaces `maxon-selfhosted/Compiler/IR/Std/InsertRefcounts.maxon`).
- `maxon-shv2/Compiler/ParseStaging.maxon` (generalizes `maxon-selfhosted/Compiler/ParseDelta.maxon`) + `Compiler/ParallelDriver.maxon`.
- `maxon-shv2/Compiler/IR/Maxon/MaxonDialect.maxon` + `IR/Own/OwnDialect.maxon`; `Compiler/IR/PassPipeline.maxon` (models `maxon-selfhosted/Compiler/IR/PassPipeline.maxon`).
- `maxon-sharp/Compiler/MLIR/Runtime/RuntimeEmitter.Allocator.cs` (Phase-0a global-lock A/B + contention counter), `Compiler/MLIR/Passes/SemanticCheckPass.cs` (`parallelBoundary` E3073 relaxation), `Testing/SpecParser.cs` + `Testing/TestRunner.cs` + `DebugStreamMonitor.cs` (mm-trace harness).
