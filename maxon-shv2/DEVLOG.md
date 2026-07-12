# maxon-shv2 — Development Log

The dated record of `maxon-shv2`, the ground-up rewrite of the Maxon self-hosted
compiler: what has landed, and what we learned while landing it (recon findings,
corrected premises, bugs found along the way).

**This is not the onboarding document.** Three documents, three jobs:
- [`PLAN.md`](./PLAN.md) — the plan: milestone sequence and locked design
  decisions (what we intend to build, in what order).
- [`ARCHITECTURE.md`](./ARCHITECTURE.md) — **read this first.** The design
  pillars, the core invariants, and one section per subsystem documenting the
  compiler's *current* design and state, so a future agent onboards without
  re-deriving it from the code.
- **This document** — the milestone ledger + dated findings.

**Doc convention:** when a subsystem changes, `ARCHITECTURE.md` is **edited in
place** to describe the new design — it carries no "was X, now Y" narrative and no
milestone attribution. The story of the change (what landed when, what it replaced,
what we learned, which premises turned out false) lives **here**.

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
remove from the loop. **Chosen: pure Design B.**

The adopted design lived in its own `docs/REGISTER_ALLOCATOR.md` (a proposal doc: an
adoption header, a corrections list, and a Phase 0–8 build plan). Once the allocator was
built, that document was **folded into `ARCHITECTURE.md`'s register-allocator section** and
deleted — the enduring content (the E5001 contract, Rules 1–3, the data-representation
discipline, the regalloc2 lineage, the known limits) is design, and the phase plan had become
history. **There is no separate allocator design doc; ARCHITECTURE.md is canonical.**

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
- **Corrections applied to the proposal as adopted** (all now reflected in the built allocator):
  ABI = the existing custom one (5 callee-saved {rbx,r12-r15}, return R8, rsi/rdi caller-saved) —
  the proposal's "7 survive a call" was standard-Win64 and wrong for it; the **specific register
  count is a tunable, not the crux** (E5001 exists at any count). Its "reserve r10/r11 as scratch"
  was likewise dropped — the pool is all 14 GPRs except rsp/rbp, since SSA destruction breaks copy
  cycles with `xchg` and the IAT call is RIP-relative. **Make-or-break = eliminate FALSE `E5001`:**
  a skeptic confirmed the design-as-written over-counts via a used-in-loop cardinality gate that
  double-counts loop-carried copy pairs, which would fire E5001 on ordinary accumulator loops; the
  overflow decision is therefore made on the true per-point maxlive (χ=ω) **after** biased coloring
  collapses copy-related values — never on a cardinality gate. Retire M4b's `EliminatePhis` (consume
  `blockArgs`/`branchEdges` directly; SSA destruction after coloring).

## M5 findings — what the reviews caught, and the lesson worth keeping (2026-07-12)

Every M5 chunk ran **implement → verify (build + *run the binaries*) → adversarial review → fix → commit**.
The reviews were not ceremony: each caught defects that the **green spec suite and the `AllocChecker` both
passed**. These are exactly the bugs that otherwise stay invisible until the compiler is compiling itself,
where they are brutal to find.

- **A critical silent miscompile in the call ABI.** Parameter-*capture* moves (`mov v, argReg[i]`) read a
  physical *source*, but the arg-*setup* forbidding is a physical-*def* mechanism — it does not mirror to
  sources. So an early parameter's capture destination could be colored onto a *later* parameter's incoming
  register and clobber it before that parameter's own capture read it. Any ≥3-param function passing an
  early parameter as a non-first call argument, with a later parameter live across the call, silently
  returned the wrong answer — **no diagnostic, 61/61 green, checker green.** Fixed by forbidding each
  parameter from every *other* parameter's incoming register.
- **THE LESSON: the `AllocChecker` only catches what it MODELS.** It did not model the incoming argument
  registers at function entry, so a physical-*source* move was untracked and the safety net was simply blind
  there. The fix hardened the *model* (seed each parameter into its incoming register), not just the bug.
  **When adding codegen that reads or writes physical registers the checker does not track, extend the
  checker's model FIRST** — until then, "the suite is green" means nothing in that region.
- **Fragments are OUTPUTS, not gates.** `spec-test` regenerates them, so a wrong-but-self-consistent
  allocator passes them. The `AllocChecker` — plus actually *running* the produced binaries — is what gates
  correctness.
- **False *rejections* are Design B's characteristic failure mode, and they clustered in the splitter.**
  Caught pre-commit: a reload placed before a block's *first* use rather than at the eviction point (so a
  value used on both sides of a pressure peak never actually split → spurious "did not converge"); a
  peak-finder using *raw* pressure while the guard used the *exact corrected* pressure; the reduced pool at
  calls/`idiv` ignored (6 values live across a call panicked in the colorer); a degenerate Belady sentinel
  (`u64.max` compares as signed −1 on `int(0 to u64.max)`) that silently collapsed farthest-next-use to
  first-fit; and parameters / rematerialized constants false-panicking as "compiler-introduced". None
  reached history.
- **Measurement trap — a false alarm that cost a cycle.** A process exit code read via bash `$?` is
  truncated to 8 bits. A correct program returning 382 shows **126**, which *looks* like a miscompile. Verify
  a suspected miscompile with a **self-checking** program (`return 0` iff correct) before believing it.

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
- [x] **M4b** `while`/`break`/`continue` + `var` reassignment — **first backward-branching CFG**, built in the parser from M4a's terminators (no new `StdOp`/`TargetOp`): preheader → header (fused `cmp`+`jcc`, `condBranch(body, exit)`) → body (back-edge `branch`) → exit; `break`/`continue` → `branch` to a **loop-context stack**'s exit/header (E2047 no-target, E2048 own-label). `var` reassignment is **on-the-fly SSA** (`Scope.setValue` rebinds the SSA `ValueId` — no slot/`store`/`load`); phis are `IrBlock.blockArgs`+`branchEdges`, minted by the parser at loop headers / break-reached exits / `if`-continuations (this fixed an `if c; x=2; end; return x` reading a stale pre-`if` value). New Std-tier pass **`EliminatePhis`** (Phase 4) resolves them: conservative single-use coalescing (union-find, no liveness needed → `sum = sum + i` coalesces to no move) + `StdOp.copy` (arith-band end, `clobbersFlags:false`) for the rest, then clears phi metadata so the backend sees plain M4a-shape multi-block SSA. **Chose on-the-fly SSA over porting v1's `Mem2Reg.maxon` (2,327 L)** — v1's IDF mem2reg needs `alloca`/`store`/`load` + a dominance-frontier module + a rename pass shv2 lacks at M4b, and the parser lands phis at the *same blocks* v1's IDF would (cross-checked against v1's `parseWhileStatement` + `placePhiNodes`); port it at M5+ for unstructured CFG. Gates: shv2 build green, **specs-shv2 34/0**, warm-rebuild determinism (74 bytes identical, so `EliminatePhis`'s `Map`s are order-independent), fragments regenerated (canonical loop → 15, 0 `mrt_start`). **M5 hand-offs (documented in code):** liveness-based coalescing (register-heavy loops + the two cases left un-coalesced here), critical-edge splitting (a copy at a `condBranch` pred runs on both edges — harmless under the exclusive-register placeholder colorer), `while true` booleans (E2004 until `setcc`). Specs `specs-shv2/{assignment,while-loops,break}.md`.
- [x] **M5.1** register-allocator SPINE (`b1e748b4d`) — the real allocator (pure Design B: SSA chordal
  coloring), replacing M1's `MinimalColorer` placeholder. Landed the operand model
  (`TargetOperands.targetOpOperands`, one exhaustive match; packed operands, `implicitUses`/`implicitDefs`
  masks), dense liveness (`FuncCfg` built once, back-edge loop depth, live-in/out to a **fixpoint** — M4b's
  back edges make a single reverse pass wrong), the biased forward-sweep colorer, SSA destruction after
  coloring, the `AllocChecker`, and sub-phase timers from commit 1 (v1's "regalloc = 74% of self-compile"
  stood for months with **zero** attribution). **Retired M4b's `EliminatePhis`** — the pass, its `StdOp.copy`
  op, and its x64 lowering are all deleted; the allocator consumes `blockArgs`/`branchEdges` directly. Biased
  coloring is strictly better than the old use-count coalescing (it collapses the induction variables M4b had
  to leave un-coalesced — `assignment-in-loop` and `while-loops.continue` loop bodies became copy-free), so
  `EliminatePhis` bought nothing the allocator does not do better. Also settled M4b's other hand-off:
  **critical-edge splitting** (a pre-pass, so an edge copy never runs on a sibling edge). Pool was 9
  (caller-saved incl. R8); hot overflow panicked.
- [x] **M5.2** ISel quality rework (`da25d25bf`) — **Reuse model**: delete the pre-emitted two-address seed
  `mov`; ISel emits one reuse-def op and the allocator materializes `mov dest, input` ONLY when the input
  outlives (`allocateReuseDef`), so the common dies-case coalesces with zero copies. Plus 3-operand `lea`
  (`+`, `a±imm`), 3-operand `imul`-imm, `cmp`-imm, the Std-tier `binOpImm`/`cmpImm`, and the
  **`foldConstOperands`** Std→Std pass (commutative canonicalization + immediate rewrite + DCE; const-lhs
  `sub` never swapped). `10 + 5*2` dropped from 6 insns/3 regs to 3/2. A 5-finding adversarial review folded
  in, all codegen-neutral — notably `maxPressure` now counts the reuse-copy `+1` transient (input outlives +
  another operand dies → `{lhs,rhs,dest}` simultaneously live), keeping the exact-χ the E5001 contract needs
  (same class as M5.1's dead-phi correction). Deferred `foldConstants` (const⊕const) — it would collapse the
  test programs to `mov r8,k` and erase the codegen the fragments exist to show. 37/37.
- [x] **M5.4** integer `/` and `mod` (`ca57bf63b`) — the **first hard fixed-register constraint**. `StdOp.div`/
  `mod` (own variants, `isPure: false` — `idiv` traps), x64 `mov rax,dividend; cqo; idivReg divisor; mov
  result, rax|rdx`; new `cqo`/`idivReg` TargetOps carrying implicit RAX/RDX masks. The divisor is kept out of
  RAX/RDX by TWO mechanisms: the clobber→forbidden path (for values live *across*) plus a new
  `forbidOperandsFromImplicit` — needed because the divisor **dies** at the `idiv` and is therefore absent
  from the live-across set. Band-append working as designed: appending at the union END made stale
  `… to iatCall` range arms fail to compile until extended. Divide-by-zero / `INT_MIN/-1` raise a raw `#DE`
  (the fault handler is a Workstream-R deliverable). 45/45.
- [x] **M5.5-M5.6** functions + parameters + calls — first functions that take arguments and call each other, on the M5.1-M5.4 register allocator. **Frontend:** `parseFunctionParameters` (`name type`, `=` default rejected `unsupported`, >6 params rejected — the ABI register-arg cap); parameters bind to their reserved `ValueId` `i` (0..paramCount-1) as immutable value bindings. New **`funcSignatures`** registry (names + types) mirroring `funcReturnTypes` exactly (`FileParseArtifact` contribution → `Project` field → `mergeArtifact` fold → `remapArtifact` named-type remap → `resolveTypes` re-sync). `MaxonOp.call(result, callee ByteArray, args, argLabels, argRanges)` in a NEW `callFree` band BEFORE `plain`. Parse: 1st arg positional, 2nd+ labelled (`consumeArgLabel` → E2052/E2053), call-expr in `parsePrimary`, bare-call statement. SemanticCheck validates each call against its signature (E3030 unknown fn / E3031 arity / E3032 unknown label / E3033 dup) via the shared `slotCallArgs` (the one label→position mapping, also used by lowering). **Lowering:** `lowerCall` reorders labels→positional and emits `StdOp.call`; one `StdOp.param(i, i, type)` per param at entry (both new in the Std call band). **Custom ABI:** args in `[rcx, rdx, rax, r9, rsi, rdi]` (v1's order minus rbx — rbx is CALLEE-saved here), return in R8. **Backend/allocator:** param entry = `mov virtual(i), physical(argReg[i])` (the existing fixed-reg hint elides it when the value lands in its own arg register); call args = plain `mov argReg[k], arg_k` pre-moves + `callDirect` + `mov result, r8` — NO sequencer needed, because `callDirect`'s caller-saved `implicitDefs = 0xFC7` (rbx EXCLUDED, asserted at runtime) forbids each arg register for any value live across the move, so forward-order emission never reads a clobbered register (the `project_call_arg_parallel_copy_fix` class). `callDirect` carries no explicit operands, so `forbidOperandsFromImplicit` is a no-op on it — the position-aware pitfall the M5.4 review flagged is SIDESTEPPED, not patched. Pool GREW to 14 (added callee-saved rbx/r12-r15); values live across a call are forbidden all caller-saved → colored callee-saved. New `TargetOp.pushReg`/`popReg` (post-regalloc, band-appended); `X64PrologueEpilogue` push/pops exactly the callee-saved a function's coloring used and reserves an aligned frame (32B shadow + parity-corrected padding so rsp ≡ 0 at the call — `roundUpToMod`). Gates: shv2 build clean, **specs-shv2 61/0** (45 + new `functions.md`: labelled/multi/nested/0-arg calls, recursion factorial+fib, call-in-loop, value-live-across-call → rbx push/pop, 5 error diagnostics), AllocChecker green on every function (broken-probe confirmed it panics on a live-across value in a clobbered register), warm-rebuild byte-identical, objdump-verified frame (balanced push/pop, `call` rel32, 16-aligned). **M5.7 note (documented in code):** `maxPressure ≤ pool` is necessary-not-sufficient at a call — a value live across is forbidden all 9 caller-saved, so its effective pool is the 5 callee-saved; the exact E5001 deficit must model each call point against that reduced pool. Specs `specs-shv2/functions.md`.
- [x] **M5.3** cold-spill live-range splitter (`1d81de734` — landed *after* M5.4/M5.5-M5.6; the numbering is
  the plan's, not the landing order). Replaces the colorer's panic-on-overflow with cold spilling: a value
  idle across a loop (or a fixed-register point) is split out via **dominating reloads** — each reload placed
  in the use's block (or the outermost idle loop's preheader) so it dominates its uses and needs **no phi and
  no SSA reconstruction**, which is what retires v1's SplitKit failure. Each reload defines a fresh `ValueId`
  (SSA preserved); integer constants are **rematerialized** rather than spilled. **Rule 2:** no store/reload
  inside a loop that uses the value (asserted) — so a loop body a spilled value doesn't touch is
  byte-identical to the un-spilled version. New `storeSlotReg`/`loadRegSlot` ops (rsp-base SIB, slots above
  shadow space); the `AllocChecker` gained slot tracking with a store-side identity cross-check.
  **HOT overflow still PANICS** — E5001 replaces it at M5.7.
  Two review rounds hardened it before commit: (1) Belady split at the **eviction point** (not before the
  block's first use), so a value used both before and after a peak is genuinely relieved — this had been a
  false "did not converge" panic; (2) `analyzePressure` uses the exact per-point pressure (reuse-copy
  transient + dead-phi corrections) so the peak-finder and the feasibility guard agree; (3) **reduced-pool
  modelling** at fixed-register points (`reducedPoolSizeAt(op) = popcount(pool ∖ implicitDefs)`) — a value
  live across a call competes for the 5 callee-saved, across an `idiv` for `pool ∖ {rax,rdx}`, so 6 values
  live across a call now spill cleanly instead of panicking in the colorer; (4) proper multi-split (a value
  crossing K peaks is spilled/reloaded around each), and a degenerate-Belady sentinel bug (`u64.max` compared
  as signed −1) that had silently collapsed farthest-next-use selection to first-fit. Gates: **specs-shv2
  72/0**, a 144-case adversarial matrix with zero miscompiles, warm-rebuild byte-deterministic.
- [x] **M5.7** `E5001` + `ValueOrigin` — the last M5 piece, and the one the whole allocator design exists
  to serve: a HOT overflow now raises a **source-mapped compile error** instead of the splitter's
  placeholder panic. New `IR/Maxon/ValueOrigin.maxon` — the `(funcIndex, ValueId) → Maxon OpIndex` table
  (three dense scalar columns, recorded in the `emitOp`/`emitTerminator` choke points, folded whole-program
  by `mergeArtifact`) that reconnects a Target-tier value to source, since spans die at the Maxon→Std
  boundary by design. New `Targets/Shared/RegisterPressureDiagnostic.maxon` renders the message: the exact
  deficit, the **reduced** register count that actually binds (5 callee-saved across a call, `pool ∖
  {rax,rdx}` at an `idiv` — never the nominal 14), each blocking value's def site ranked
  cheapest-to-move-first, and the named transformation. It cannot false-positive: the feasibility decision
  was already made by the splitter against those same per-point reduced pools; this only maps it to source.
  A loop-carried value is a phi with no defining op, so it is chased to the incoming value it copies; a
  value with NO origin is a Rule-3 compiler defect and panics rather than print a misleading location.
  `allocateRegisters`/`buildBackend` now `throw CompileError` (through a FRESH re-throw gate — the
  documented rethrow gotcha). An adversarial review then caught three make-or-break defects, all fixed
  before commit: a **nested-loop false E5001** (a genuine M5.3 bug — the parser lays a nested loop's exit
  block *before* the inner-loop blocks, so the splitter's seq-based before/after-peak classification
  misfired; fixed at the ROOT by an **RPO block reorder** so layout order matches execution order); a
  **parameter used in a hot loop** false-panicking as "compiler-introduced" (parameters are never minted
  through `emitOp`, so they had no origin — they now carry their declaration span); and a **remat/reload
  fresh id** in the blocking set doing the same (it now chases back to its source value's origin via
  `SplitLineage`, so Rule 3 fires only for a genuinely sourceless value). Also fixed a `CompileError` leak
  on the first-ever backend throw path (exit 101, which would have corrupted the `maxoncstderr` compare).
  Gates: **specs-shv2 78/0**, `register-pressure.md` asserting the byte-exact message via `maxoncstderr`,
  the E5001 text md5-stable across runs, and a no-false-E5001 matrix (straight-line to 40 values,
  idle-across-loop to 45, nested loops, params live across calls) all compiling clean. (`8ab422598`)
- [x] **M5.8** docs + cleanup (`e133375db`) — folded `docs/REGISTER_ALLOCATOR.md` (the adopted proposal +
  its corrections header + the Phase 0–8 build plan) into ARCHITECTURE.md's register-allocator section and
  deleted it: its enduring content is design, its phase plan is history. Added a `--log=<level>` /
  `--log=<category>:<level>` CLI flag (an unrecognized spec is reported, not silently ignored). Corrected
  `IrBlock.branchEdges`' stale doc comment (it is populated by the parser's M4b on-the-fly SSA and consumed
  by the allocator's SSA destruction — NOT by `lowerStdToX64`). `IrBlock.clone` was already removed at M5.1.

> ### STAGE 1 COMPLETE — 2026-07-12, `e133375db`, **specs-shv2 78/0**
> The full scalar language — `let`/`var`, arithmetic, `if`/`while`/`break`/`continue`, `mod` and `/`, and
> functions with parameters and calls — running on the **real register allocator**: biased SSA chordal
> coloring → cold-spill splitter (dominating reloads, remat) → `E5001` hot-pressure diagnostic, all guarded
> by the `AllocChecker`. Stage 1's one design item (the allocator) is done and its contract is complete:
> **spill cold, error hot.** Next: **Stage 0 tooling** (the `selfhost-distance` compass + the pruned
> `stdlib-shv2` fork — the loop that gates Stage 2), then **Stage 2** (generics BEFORE ownership).
- [ ] **per-function fan-out** — carried by M5's original "functions (fan-out)" scope but NOT built by
  M5.1–M5.6. Both seams exist (`PassPipeline.classifyPass` labels each pass `wholeModule`/`perFunction`;
  the parser is already a pure function of its file), and the runtime under it is proven (Track 0); nothing
  drives them. Blocking gate when it starts: **1-core-vs-N-core byte identity**.
**The remaining ledger follows the ADOPTED plan's order — generics BEFORE ownership** (see "Plan adopted"
above). The original list here was the pre-adoption M6–M18 numbering, which had ownership at M6 and generics
at M14 — *reversed*, because `own.drop` on a type-parameter value needs the runtime layout descriptor, and
`String`/`Array` are themselves generic.

- [ ] **Stage 0 tooling** — the `selfhost-distance` compass (its ranked TOP-UNSUPPORTED table IS the
  roadmap), the pruned `stdlib-shv2/stdlib/` fork + `MAXON_STDLIB` override, and the 9 core-violating
  rewrites. This is the loop that gates Stage 2.
- [ ] **Stage 2** — 2.1 structs · 2.2 **generics** (dictionary-passing + layout descriptors + witness
  tables) · 2.3 interfaces · 2.4 **heap + ownership + drops** (Workstream **R1** — the emitted runtime:
  slab / refcount / `__destruct` / DebugStream producer; also what gates mm-trace) · 2.5 moves+borrows ·
  2.6 escape→refcount · 2.7 Array · 2.8 String (R2) · 2.9 owned payloads · 2.10 Map · 2.11 for-in ·
  2.12 error handling · 2.13 ranged typealias
- [ ] **Stage 3** self-host · [ ] **Stage 4** broaden · [ ] **Stage 5** budget gate (≤30 s / ≤1.7 GB /
  >90% CPU; Workstream **R3** = the GT scheduler)
