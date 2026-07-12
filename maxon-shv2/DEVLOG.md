# maxon-shv2 — Development Log

The dated record of `maxon-shv2`, the ground-up rewrite of the Maxon self-hosted
compiler: what has landed, and what we learned while landing it (recon findings,
corrected premises, bugs found along the way).

**This is not the onboarding document.** Three documents, three jobs:
- [`PLAN.md`](./PLAN.md) — the plan: milestone sequence and locked design
  decisions (what we intend to build, in what order).
- [`ARCHITECTURE.md`](./ARCHITECTURE.md) — **read this first.** The design
  pillars, the core invariants, and one section per subsystem documenting its
  *operation and invariants* as it lands, so a future agent onboards without
  re-deriving the design from the code.
- **This document** — the milestone ledger + dated findings.

---

## Track 0 findings — corrections to PLAN.md premises (from recon, 2026-07-10)

Read these before starting Foundation 1. Detailed maps live in the recon set
(session scratchpad `.../recon/0{1..6}-*.md`); the load-bearing corrections:

- **"The C# sharded allocator has never run with >1 live P" is false as
  stated.** A 2nd worker M is spawned on the *first* `spawn`/`async` on any
  multi-core host — `__gt_enqueue`'s worker-spawn scan is unconditional
  (`RuntimeEmitter.Scheduler.cs:171-262`), and `subprocess-async-parallel.test`
  already exercises concurrent async under the C# bootstrap. What is genuinely
  **uncovered** is narrower and is the real Foundation-1 risk: the *lock-free*
  cross-P paths in `RuntimeEmitter.Allocator.cs` — the **ownership gate**
  (`:2181-2198`, re-probe `:2255-2264`) and the **remote-free MPSC** (push
  `:2575-2607`, drain `:2322-2399`) — have no known exercise. The self-hosted
  compiler dogfoods a *different*, coarse-locked reimplementation
  (`stdlib/Internals.maxon`), so heavy self-host load gives these paths zero
  coverage. 1a.5 is likely a rubber-stamp; **1a.3 is the substantive work.**
- **`__slab_lock` does not exist in the C# emitter.** 1a.1 must add a brand-new
  `MAXON_SLAB_GLOBAL_LOCK`-gated lock bracketing the *entire bodies* of
  `EmitSlabAlloc` (`:2081-2282`) and `EmitSlabFree` (`:2412-2638`). Use a
  **spinlock, not a CriticalSection**: the self-hosted runtime's coarse
  `__slab_lock` hit a real bug where a contended `EnterCriticalSection`
  kernel-wait on a green thread's small stack corrupted it
  (`maxon-selfhosted/.../X64Backend.maxon:7451-7467`), fixed by routing lock
  ops through the per-P 64KB system stack. A test-and-set spinlock sidesteps
  that class entirely. (The self-hosted's coarse `__slab_lock` is its
  *permanent* design, not a toggle — evidence the coarse path is the proven
  baseline.)
- **`--max-procs 1` / `MAXON_MAX_PROCS` genuinely does not exist** and is needed
  by two Track-0 consumers (deterministic mm-trace goldens for thread-spawning
  programs; the validation-harness `--max-procs {1,2,7,ncpu}` stress). It
  belongs in Foundation 1: read `MAXON_MAX_PROCS` in `__gt_init` and clamp
  `__sched_max_procs = min(ncpu, MAXON_MAX_PROCS)`; with 1, the spawn gate
  (`active_workers < max_procs`) never fires → single-threaded, deterministic.
  Until it lands, mm-trace goldens are only safe for programs that never spawn
  a green thread (the Foundation-2 proof program is one such).
- **`maxon_cpu_count` does not exist** (1a.4 is genuine per-backend extraction:
  `GetSystemInfo` on x86, `sysconf` on ARM64), and must be valid *before*
  `__gt_init` (today ncpu is only read inside `__gt_init`'s prologue).
- **Plan risk #4 (P-less alloc) is a null-deref, not a race.** Frees from raw OS
  threads (IOCP/sync-worker/fault-handler) are already routed to the span's real
  owning P; `__slab_alloc` has *no* NULL-P guard and every P-less path avoids it
  by construction (direct `VirtualAlloc`). A global lock does **not** fix this —
  it needs a NULL-P guard + shard-0 fallback in `__slab_alloc`, or continued
  enforcement that no P-less thread reaches it.
- **Foundation 1 scoping (decision):** keep it entirely within `maxon-sharp`
  (C# emitter/parser/semcheck) with **no shared-stdlib changes** in Track 0. The
  validation-harness torture test calls `__Builtins.cpuCount()` + `async`
  directly; the public `Process.cpuCount()` + `Parallel.map` stdlib API is
  **deferred to M5** (its first real consumer — shv2's per-function fan-out),
  because adding them to the *shared* `stdlib/` risks the self-hosted stdlib
  compile (cross-compiler builtin support), and shv2 doesn't use them until M5.
  Note `PARALLEL_CODEGEN_PLAN.md` describes the *self-hosted* single-shared-
  mcache allocator; shv2's Track 0 hardens the *C# emitter's* already-sharded
  allocator (`RuntimeEmitter.Allocator.cs`) — different allocators.
- **E3073 relaxation:** the C# edit adds the *runtime symbol* string
  `"maxon_parallel_boundary"` to `IoStubs` (`SemanticCheckPass.cs:100-158`) — NOT
  `"__Builtins.parallelBoundary"` (that qualified name is only for the
  self-hosted mirror). Plus a `RuntimeCallIntrinsic` builtin entry in
  `2-Parser.cs` (model on `forceSegfault` `:9506-9508`) and a bare-ret
  `EmitMaxonParallelBoundary()` in **both** `X86CodeEmitter.Runtime.cs` and
  `ARM64CodeEmitter.Runtime.cs`. The builtin must emit a real (empty) runtime
  fn, else its caller becomes invisible to the E3073 yields walk.

---

## M1-B2 finding — the register allocator cannot be copied (2026-07-10)

PLAN.md listed `Targets/Shared/RegisterAllocator*` under **COPY near-verbatim**, on the
premise that it "operates on generic structures." **Both halves of that are false**, and
the plan is amended:

- **Not generic.** v1's 2,233-line SSA-dominance colorer (LiveRangeBuilder + SpillManager
  + ApplyColoring + a density-gated recolor/split engine) is welded to v1's bespoke
  `TargetModule` (an `x64Ops` array behind a `cpu` discriminator) and needs the
  `TargetOpQuery` / `OpPattern` / register-mask machinery to extract an op's register
  operands. None of that scaffold exists in shv2's thin `IrModule with TargetOp` tier, so
  a verbatim port would drag in the whole regalloc cone — for a program with exactly ONE
  virtual register.
- **Copying it would import the wrong design anyway.** Register allocation is **~74% of
  v1's self-compile wall time** (~418 s of 561 s; worst single function 58 s), against
  shv2's ≤30 s budget for the *entire* self-compile. v1's reactive spill/color loop
  rebuilds the interference graph from scratch every iteration; that is the thing to not
  inherit.

M1 ships a **no-liveness placeholder** instead (distinct GPR per distinct value, hard panic
on pool exhaustion — correct-by-construction at M1's 1 virtual register, never a
miscompile). The real allocator is designed and built at M3/M5, when arithmetic and
multi-function programs create the first real register pressure. The v1 lessons it must be
built around — the graph rebuild is the cost; a one-shot MIN spiller is not a drop-in;
sub-phase timers from commit 1 — are recorded in ARCHITECTURE.md's Register allocator
section.

---

## Plan adopted — Minimal-Core / core-first (2026-07-11)

The original M1–M18 `PLAN.md` was **replaced** by the Minimal-Core plan (user-adopted).
The M-numbered ledger below stays valid via the **Stage ↔ M mapping in PLAN.md's
header**. What changed:
- **Generics BEFORE ownership** (Stage 2): ownership's `own.drop` on a type-parameter
  value needs the runtime layout descriptor, and `String`/`Array` are themselves
  generic — so generics can't come after ownership. Order is now structs → generics →
  interfaces → **heap+ownership+drops** → moves/borrows → escape → Array → String → Map.
  (Original had ownership at M6, generics at M14 — reversed.)
- **Self-host = compass, not gate.** A `selfhost-distance` ratchet (`stageUnits` over
  shv2's own source; the ranked "TOP UNSUPPORTED" table IS the roadmap; per-unit
  non-regression CI). Not yet built (Stage-0 gap 0.4).
- **`stdlib-shv2/` pruned fork** + `MAXON_STDLIB` override (Stage-0 gaps 0.2/0.3) — the
  only per-build stdlib lever (no `.mxc` cache until Stage 5). Leaf dir name MUST be
  `stdlib` (namespace/monomorphization trap).
- **Workstream R** = the runtime shv2's backend must *emit* (~5–7k lines: slab, refcount,
  `__destruct`, `__ManagedMemory`, DebugStream producer, GT scheduler), sliced into
  Stage 2 (R1@2.4 gates mm-trace; R2@2.8; R3@Stage 5). shv2 excludes `Internals.maxon`
  and emits natively (follows the C# bootstrap).
- shv2's own source has **9 core-violating sites** (7 closures, 1 conditional conformance,
  1 `Set`) to rewrite so it stays in "core" (Stage-0 gap 0.5).
- x64-windows only through self-host.

## Register allocator design adopted — PURE Design B (2026-07-11)

M5's allocator design was chosen by an **adversarial evaluation** (5 independent lenses +
3 skeptics, over both candidate designs, verified against the real shv2/v1 source). Two
designs competed: **A** = SSA linear-scan + greedy fallback (always compiles, spills hot
code); **B** ("mighty-pudding") = SSA chordal coloring + gap splitting that **refuses to
emit hot spill code and errors (`E5001`)** instead, telling the author which values to
remove from the loop. **Chosen: pure Design B.** Canonical design in
[`docs/REGISTER_ALLOCATOR.md`](./docs/REGISTER_ALLOCATOR.md) (adopted mighty-pudding + a
corrections header — read the header first).

- **Why B's mechanism** (won unanimously): the `Reuse(i)` operand model deletes the
  two-address `mov` at the root; the **`AllocChecker`** (symbolic verifier under
  `spec-test`) is the only thing that catches a self-consistent miscompile — decisive
  because *fragments are outputs, not gates*; the dense representation (ProgPoint / bitset
  liveness / no `Map` in the hot path) dodges the heap-box trap; and the **dominating-reload**
  spill placement retires v1's SplitKit failure (survived every refutation attempt).
- **Why B's contract** (`E5001`, not always-spill): Maxon is written by AI agents, for whom
  a deterministic one-round-trip error is a feature, not friction; and dogfooding `E5001` on
  the compiler's own code forces it within the Stage-5 self-compile budget instead of hiding
  slow spills. The hybrid "spill + warn" was rejected (a warning gets ignored → hidden slow
  code returns). Pure-B is also *simpler* (no greedy fallback, no hot-spill path).
- **Corrections folded into the doc header:** ABI = the existing custom one (5 callee-saved
  {rbx,r12-r15}, return R8, rsi/rdi caller-saved) — the doc body's "7 survive a call" / "pool
  14" are wrong for it; the **specific register count is a tunable, not the crux** (E5001
  exists at any count). **Make-or-break = eliminate FALSE `E5001`:** a skeptic confirmed the
  design-as-written over-counts via a used-in-loop cardinality gate that double-counts
  loop-carried copy pairs; the overflow decision must instead be the true per-point maxlive
  (χ=ω) after biased coloring collapses copies — verified by the `AllocChecker`. Retire M4b's
  `EliminatePhis` (consume `blockArgs`/`branchEdges` directly; SSA-destruction after coloring).

## Milestone ledger

Checkboxes track landing against `PLAN.md`. Correctness-only gate through
Phase E; budget gate (≤30 s / ≤1.7 GB / >90% CPU) becomes hard at Phase F.
The design each milestone establishes is documented in
[`ARCHITECTURE.md`](./ARCHITECTURE.md), not here.

- [x] **Step 0** — plan + DEVLOG materialized in repo
- [x] **Track 0 / Foundation 2** — binary event log + mm-trace harness (C# harness: `mm-trace` block + redefined `<!-- MmTrace -->` → `maxon monitor` capture + normalize + regen; proof spec `specs/mm-trace.md`; producer verified end-to-end)
- [x] **Track 0 / Foundation 1** — multi-core green threads hardened (primitives + global-lock A/B + counters; **found & fixed a real ~2.5% multi-core crash** — x86 `__gt_spawn` passed `gt` in R10 to `__gt_enqueue` without reloading after `LeaveCriticalSection` clobbers it; fix mirrors ARM64's existing reload)
- [x] **Track 0** — validation harness (`maxon-shv2/track0/`): byte-identical aggregate across `MAXON_MAX_PROCS {1,2,7,16}`, 16 vs 1 workers, leak-clean + balanced mm-trace (568 alloc == 568 free), **remote-free MPSC path exercised (7775 cross-P pushes)**, global-lock A/B parity. 480+ clean high-concurrency runs post-fix.
- [x] **M1** basics — thin frontend + driver: content-hash query spine
  (`querySourceFile→queryTokens→queryParseOps→queryAllModule`), parse-staging
  (`FileParseArtifact` + `mergeArtifact`), thin `Parser` (`function`/`return`/int
  literal), `resolveTypes` (ExitCode→u32 builtin), `semanticCheck` (E3001/E3002),
  `Compiler`/`Main` driver. `maxon-shv2 build examples/basic.maxon` → exit 42;
  E3001/E3002 error paths verified. Spec `specs-shv2/basics.md`.
- [x] **M1 post / structural** — **3-tier collapse**: `StdOp` flattened
  (`StdOpMeta` backing: category/role/inlinePolicy + scheduler facts, declared in
  category-contiguous bands) and the **MIR tier deleted** (`IR/MIR/*`,
  `LowerStdToMir.maxon` gone; `MirToX64Conversion` → `StdToX64Conversion`,
  `lowerMirToX64` → `lowerStdToX64`; `CodeResult.mirModule` → `stdModule`).
  Amends PLAN.md's original "tiers stay Maxon → Std → MIR → Target" decision —
  see ARCHITECTURE.md's Std dialect section for the full rationale (MIR added no
  value model; nesting cost 3 heap boxes per op instead of 1). Done at M1 because
  the same merge costs ~160 lines now and ~1,500 in v1. All three M1 gates
  re-verified (exit 42 byte-identical at 28 bytes of `.text`; E3001; E3002).
- [x] **M1 post / structural** — **Maxon values are `ValueId`s, minted by the
  parser.** The `name → ValueId` step in `lowerMaxonToStd` was a **bijection
  between two dense counters**: v1's Maxon result names were synthetic `$tN`
  strings off a per-function counter (`mintSynthName`), and the ids were a second
  per-function counter — so the mapping cost a heap `String` + heap `ByteArray`
  per value plus a byte-sequence-keyed hash map (O(len) compares; one insert + one
  lookup per value) and constructed **no SSA** (user vars are `VarSlot`s; `mem2reg`
  does real SSA at the Std tier). `MaxonOp` operands are now `ValueId`s;
  `NameToValueIdMap`/`defineName`/`resolveName` are deleted; the lowering passes
  ids through verbatim, leaving it to do what it is actually for — the width/ABI
  collapse and the 1:N desugaring. Identifier TEXT that is not a value (field /
  method / struct names) stays `ByteArray`. Also: the void-return sentinel (an
  empty `ByteArray`) became a distinct `MaxonOp.retVoid` variant — `ValueId` has no
  empty value, and sentinels are forbidden; `MaxonOp` gained the band-append
  invariant + an `OpCategory.ownership` band so M6's `own.*` ops land as MaxonOp
  variants rather than a nested `MaxonOp.own(OwnOp)` (see ARCHITECTURE.md's Own tier
  section); `ValueIdArray`/`ByteArrayArray` were rehomed to `IrValueId`/
  `GlobalDataTable`.
  **Source spans came off the op too.** `SourceRange` is a `type` (reference
  semantics), so a trailing `range` field was a POINTER to a heap-boxed SourceRange
  — one live heap object per op, retained for the module's lifetime, on the most
  numerous object in the compiler. Spans now live in `SourceRangeTable`, an
  op-parallel store of **four dense scalar columns**; a `SourceRange` is
  materialized only by `get`, i.e. only when a diagnostic or LSP query asks (the
  cold path). NOTE an `Array with SourceRange` would NOT have worked — it is an
  array of pointers to the same boxes; the scalar columns are the whole point. The
  table is MAXON-TIER (`FileParseArtifact` per file → `Project` whole-program, via
  `mergeArtifact`); Std/Target carry no spans, so the post-inline peak module pays
  nothing. Ops and spans are appended in lockstep by the single choke point
  `FileParseArtifact.emitOp`/`emitTerminator`, and `record` asserts index ==
  table-count on every op, so an op appended behind the choke point's back panics
  on the next emit rather than silently shifting every later span by one.
  Validated against v1 first: of 80 `ByteArray` operand
  occurrences across v1's 51 `MaxonOp` variants, ~72 are values and 8 are identifier
  text. Done now because v1's parser has 27 mint sites and 266 `ByteArray`
  references to shv2's 3 and 7 (~38× cheaper), and because M6's Own tier is specced
  against `Scope.ownedStack` (`Array with String`) — after that this is a rewrite,
  not a representation change. `MaxonType` stays a **boxed union**: packing it into
  an i64 would forfeit compiler-checked payload extraction, and the static guarantee
  is worth more than the allocations. All M1 gates re-verified (exit 42, **byte-
  identical** 28-byte `.text`, sha `5d9944a7…`; E3001; E3002). shv2's own `.text`
  686,888 → 675,517 (−11,371).
- [x] **M2** variables — `let`/`var` + variable references (parse-time `Scope`
  value-binding: `declareValueBinding`/`lookupValue`, name→`ValueId`; a ref
  resolves to the initializer's SSA id, so `let x = 42; return x` lowers
  *identically* to `return 42` — no IR op for let/var/refs) + binary `+`
  (`MaxonOp.binOp`/`MaxonBinOp{add}` → `StdOp.binOp`/`StdBinOpcode{add}` appended
  at the END of the arith band → x64 `mov`+`add` via the shared `encodeRmRegDirect`
  primitive). `let x: int` rejected with **E2010** (positioned `file:line:col`).
  **Warm-rebuild gate joins the battery**: `maxon-shv2 verify-warm-rebuild <file>`
  asserts determinism (two fresh-`Project` cold compiles → byte-identical
  `CodeResult`) + incrementality (re-`queryAllModule` on unchanged input →
  content-hash cache hit). Spec `specs-shv2/variables.md` (test 4, top-level
  string + `if` + `==`, deferred to M10/M4/M3).
- [x] **M3** arithmetic — **Pratt precedence parser** (replaces M2's left-fold:
  precedence climber `parseBinary(minPrec)`, right operand at `prec+1` for
  left-assoc; `parseUnary` is the leaf, operand is a PRIMARY so unary binds
  tightest but doesn't chain → `- -x` is E2004) + integer `-`/`*`
  (`MaxonBinOp`/`StdBinOpcode` gain `sub`/`mul`, same `binOp` variant; x64 `sub`
  `0x29` + `imul` two-byte `0F AF` + `neg` `F7 /3`, all via the shared
  `encodeModRmDirect`/`encodeModRmExtDirect` primitives) + unary minus
  (`MaxonOp.unaryOp`/`neg`, always runtime `neg`, no const-fold). Precedence proven
  (`10 + 5 * 2` → 20, imul before add). **Milestone-boundary reshaping** (spec-
  driven): comparison operators → **M4** (all their tests observe via `if`);
  `mod`/`/` → **M5** (x64 `idiv` fixed RAX/RDX needs the real allocator; every `/`
  test also needs `trunc`/params/loops) — deferred ops reject with a positioned
  `E3010 … arrives at Mn` note. Placeholder allocator suffices (M3 expressions are
  small). Specs `specs-shv2/{arithmetic,unary-operators}.md`. **Phase A complete.**
- **Tooling** — **spec-test runner landed** (`maxon-shv2 spec-test`, `Testing/SpecParser`+`SpecTestRunner`): parses `specs-shv2/*.md`, compiles each active test through shv2 (subprocess), compares exit-code / normalized `maxoncstderr`; deferred tests live under `## Deferred` (HTML comments don't nest). Replaces hand-driving. 27/0.
- [x] **M4a** control flow (comparison + `if`) — comparison operators (`==`/`!=`/`<`/`>`/`<=`/`>=` as a `cmp` op, fused `cmp`+`jcc`, un-deferred from M3) + `if`/`else`/`else-if` (**first multi-block functions**: intra-function `jmp`/`jcc` rel32 resolved in `emitFunctionChunk` via `BlockStartOffsetMap`/`resolveBlockJumps`, forward+backward-ready for loops) + `return` in branches. New `control` band (`condBranch`/`branch`) between arith and call. Built in a parallel worktree; a review caught a **real miscompile** — a comparison in value position (`let b = x==10; …`) read stale flags after an intervening flag-setter — fixed by restricting comparisons to the sole top-level operator of an `if` condition (E3010 otherwise) until bool materialization/`setcc` lands. Specs `specs-shv2/{comparison-operators,if-statements,return-statement}.md`.
- [x] **M4b** `while`/`break`/`continue` + `var` reassignment — **first backward-branching CFG**, built in the parser from M4a's terminators (no new `StdOp`/`TargetOp`): preheader → header (fused `cmp`+`jcc`, `condBranch(body, exit)`) → body (back-edge `branch`) → exit; `break`/`continue` → `branch` to a **loop-context stack**'s exit/header (E2047 no-target, E2048 own-label). `var` reassignment is **on-the-fly SSA** (`Scope.setValue` rebinds the SSA `ValueId` — no slot/`store`/`load`); phis are `IrBlock.blockArgs`+`branchEdges`, minted by the parser at loop headers / break-reached exits / `if`-continuations (this fixed an `if c; x=2; end; return x` reading a stale pre-`if` value). New Std-tier pass **`EliminatePhis`** (Phase 4) resolves them: conservative single-use coalescing (union-find, no liveness needed → `sum = sum + i` coalesces to no move) + `StdOp.copy` (arith-band end, `clobbersFlags:false`) for the rest, then clears phi metadata so the backend sees plain M4a-shape multi-block SSA. **Chose on-the-fly SSA over porting v1's `Mem2Reg.maxon` (2,327 L)** — v1's IDF mem2reg needs `alloca`/`store`/`load` + a dominance-frontier module + a rename pass shv2 lacks at M4b, and the parser lands phis at the *same blocks* v1's IDF would (cross-checked against v1's `parseWhileStatement` + `placePhiNodes`); port it at M5+ for unstructured CFG. Gates: shv2 build green, **specs-shv2 34/0**, warm-rebuild determinism (74 bytes identical, so `EliminatePhis`'s `Map`s are order-independent), fragments regenerated (canonical loop → 15, 0 `mrt_start`). **M5 hand-offs (documented in code):** liveness-based coalescing (register-heavy loops + the two cases left un-coalesced here), critical-edge splitting (a copy at a `condBranch` pred runs on both edges — harmless under the exclusive-register placeholder colorer), `while true` booleans (E2004 until `setcc`). Specs `specs-shv2/{assignment,while-loops,break}.md`. · [ ] **M5** functions (fan-out) + **real register allocator**
- [ ] **M6** heap+drops · [ ] **M7** moves+borrows · [ ] **M8** escape→refcount
- [ ] **M9** structs · [ ] **M10** strings · [ ] **M11** arrays
- [ ] **M12** enums · [ ] **M13** closures · [ ] **M14** interfaces/generics · [ ] **M15** error handling
- [ ] **M16** feature-complete · [ ] **M17** self-compile · [ ] **M18** budget gate
