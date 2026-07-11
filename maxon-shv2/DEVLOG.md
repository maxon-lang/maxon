# maxon-shv2 — Development Log (living document)

This is the onboarding document for `maxon-shv2`, the ground-up rewrite of the
Maxon self-hosted compiler. It is **not a changelog**. Each section documents
the *operation and invariants* of one part of the compiler as that part is
built, so a future agent can understand the design without re-deriving it from
the code. See [`PLAN.md`](./PLAN.md) for the full plan, milestone sequence, and
locked design decisions.

**Reading order for a new contributor:** Design Pillars → then whichever
subsystem section is relevant. The subsystem sections are filled in as the
corresponding code lands (they are stubs until then).

---

## Design pillars (why shv2 exists)

v1 (`maxon-selfhosted`) works but the two *integral* features — static
ownership/borrowing and parallel incremental compilation — were retrofitted late
(≈8 shared `Project` sidetables + a 7,755-line refcount inserter), making it
slow and memory-hungry (5–6 GB). shv2 designs these in from the first commit:

1. **Static ownership/borrowing** — compile-time move/borrow checking that drops
   values deterministically at scope exit; runtime refcounting only where escape
   analysis proves genuine sharing.
2. **Parallel incremental compilation** — green-thread fan-out over per-file
   parse and per-function passes, with the multi-core runtime prerequisites
   proven *before* the first compiler milestone.
3. **Binary event-log tracing** — DebugStream binary events to shared memory
   (near-zero overhead when off), decoded by `maxon-sharp` as the runner; powers
   `mm-trace` for ownership/memory debugging.

**Final acceptance:** `maxon-shv2.exe` compiling itself in **≤30 s**,
**≤1.7 GB RAM**, **>90% CPU** across all cores.

---

## Core invariants (fill in as subsystems land)

These are the load-bearing invariants the plan calls out. Each is documented in
full in its subsystem section once built; listed here as an index.

- **Ownership-kind lattice** — `trivial` · `owned` · `borrow` · `shared`.
  Born at the Maxon tier, fully resolved before `lowerMaxonToStd`. Three
  first-class homes, zero sidetables: (1) `OwnershipKind` attribute on every
  value/binding, (2) signature ownership modes in the function type
  (param `consume`/`borrow`/`copy`, return `owned`/`borrow`), (3) explicit
  `own.*` ops in the block stream. *(→ Own tier section)*
- **Parse-staging registry set** — the parser writes only into a per-file
  `FileParseArtifact` (MaxonModule fragment + key-and-value bundle for every
  registry it would touch); `mergeArtifacts [M]` folds them into `Project` in
  fixed source-path order, doing all duplicate detection at merge time.
  *(→ Frontend / parse-staging section)*
- **rdata deterministic-merge invariant** — the backend captures rdata constants
  chunk-locally and merges them into the shared `GlobalDataTable`
  single-threaded in function order (idempotent-by-label dedup). Content-derived
  keys for all other shared appends (FNV-1a panic labels, `__float_<bits>`).
  *(→ Backend section)*
- **DebugStream schema is frozen** — 128-byte header, ticket spinlock, MM
  `0x01–0x09`, Sched `0x20–0x2C`, Depth `0x40/41`, Dbg `0x50–0x5E`, `MXDS_TAGS`
  blob. New events get new unused type codes; existing codes are never
  reinterpreted. *(→ Event-log section)*
- **1-core-vs-N-core byte identity** — blocking gate for the entire parallel
  phase. *(→ Parallel driver section)*

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

## Subsystem sections

### Frontend (lexer, parser, parse-staging)

**Type layer landed (M1 Chunk A).** `Compiler/Lexer.maxon` is copied verbatim
(`tokenize(source) -> TokenArray throws LexerError`, table-driven DFA) with its
two non-stdlib deps brought along: a trimmed `Compiler/Logger.maxon`
(LogLevel/LogCategory + emit) and the `ByteOffset` typealias in
`Targets/Shared/BinaryHelpers.maxon`. `NumberParsing.maxon` (parseInt) copied +
one root-cause fix (a byte-element-type mismatch in `b"…"` vs `.toByteArray()`
comparison, latent in v1, masked there by the compile cache). IR containers are
in: `IR/IrValueId.maxon` (zero-dep typealiases), `IR/IrBlock.maxon`,
`IR/IrModule.maxon` (generic `uses Op`; `MaxonModule = IrModule with MaxonOp`),
`IR/IrFunction.maxon` (deliberately **thinned** — dropped v1's per-ValueId
refcount side-tables + 6-way `OwnershipClass` + generics/ABI cone, which the
static Own tier and later milestones supersede), `IR/Maxon/SourceRange.maxon`,
`IR/Maxon/Scope.maxon`. The **parser, ParseStaging, and query spine are Chunk C**
(not yet built). `Compiler/Project.maxon` exists only as a THIN foundation
(homes `VarInfo`/`Visibility`/`FieldOffset`/`StringArray`/etc.); Chunk C must
**grow** it (add the struct + ~28 registries + `createProject`), not redefine it.

**Scope ownership scaffold:** `Scope.ownedStack` runs parallel to `frameStack`,
pushed/popped in lockstep by `pushScope`/`popScope` (panic on unmatched pop —
they must stay parallel). All 4 `declare*` paths funnel through
`recordInCurrentFrame`, which records into `ownedStack` **only when the binding
is non-trivial** — inert at M1 (all bindings trivial), drained at M6 to emit
`own.drop`/`own.release`.

### Maxon dialect

**Landed (M1 Chunk A), thin + growable.** `IR/Maxon/MaxonDialect.maxon`:
- `MaxonOp` union — `literal(result, value, valueType, range)` + `ret(retVal,
  range)` only, each tagged `= MaxonOpMeta{category}` (v1's invariant: a new op
  cannot be added without declaring its `OpCategory`). Values are **name slices**
  (`ByteArray` over the lexer buffer / synthetic `$tN`), not SSA ids — SSA
  numbering is assigned only at the Maxon→Std boundary.
- `MaxonType` union — `boolean/integer/float/named(TypeNameId)/unresolved`
  (enough to type `int` + `ExitCode`); width/signedness lives in the typealias
  registry, not here. `unresolved` is the parser's placeholder; TypeResolution
  (Chunk C) must eliminate it before lowering. Void is not a MaxonType —
  `MaxonReturnType{void, value}` carries return slots.
- **`OwnershipKind` lattice** — `trivial | owned | borrow | shared`, the plan's
  first-class Maxon-tier ownership. Three homes, zero sidetables: (1) the
  attribute (`VarInfo.ownership`, defaulted `trivial`), (2) signature modes in
  the function type (M5/M6), (3) explicit `own.*` ops (M6). `isTrivialOwnership`
  is the shared classifier. Present-but-inert at M1; M6 activates it.

Later milestones GROW `MaxonType` (string/char/function/generic/interface arms),
`MaxonOp` (var/arith/call/control-flow), and the signature ownership modes.

### Own tier (ownership infer / check / escape / drops)
_stub — filled in at M6._

### Pass pipeline
_stub — filled in at M1, extended each milestone._

### Query spine (incremental)
_stub — skeletal from M1; warm-rebuild assertion joins the gate at M2._

### Parallel driver

**Runtime multi-core proven (Track 0 / Foundation 1, x64-windows).** The C#
emitter's green-thread scheduler + sharded allocator run correctly across many
worker Ps. Empirical: a 32-GT CPU/alloc burst compiled by `maxon.exe`, observed
under `maxon monitor`, ran on **16 distinct worker Ps** (P0–P15 = ncpu) unclamped
and **exactly 1 (P0)** under `MAXON_MAX_PROCS=1`, both producing the correct
deterministic result — so the `__gt_enqueue` worker-spawn gate fires for a
pure-CPU burst (1a.5), and the `MAXON_MAX_PROCS` clamp is a real single-thread
knob. Concurrency primitives (`maxon-sharp`-only, no shared-stdlib change):
`__Builtins.cpuCount()` (`maxon_cpu_count`, `GetSystemInfo`/`sysconf`, valid
pre-`__gt_init`), `__Builtins.parallelBoundary()` (`maxon_parallel_boundary`,
empty runtime stub + `IoStubs` entry so CPU-bound `async` passes E3073),
`MAXON_MAX_PROCS` clamp in `__gt_init` (both backends). Bisection tools:
`MAXON_SLAB_GLOBAL_LOCK` spinlock + `MAXON_SLAB_STATS` contention counters
(lock-wait, ownership-gate-miss) + `__Builtins.schedMaxActiveWorkers()`
high-water mark. **The validation harness (`maxon-shv2/track0/`) closed 1a.3**:
an allocation-torture program (main allocates managed arrays, hands them to
`async` tasks without retaining a ref → the arg is freed on the *worker* P → a
cross-P remote-free push back to P0) drove the remote-free MPSC to **7775
pushes** and, at high concurrency, surfaced a real **~2.5% NULL crash** in the
C#-emitted `__gt_spawn` (it passed `gt` to `__gt_enqueue` in R10, a Win64
caller-saved reg that `LeaveCriticalSection` clobbers on its contended
wake-a-waiter path; the main-thread spawn path — which main's own `mainThread`
GT with `stackBase==0` takes — doesn't preserve R10). Fixed by reloading `gt`
from its stack slot before the enqueue, matching ARM64. This is a latent
scheduler bug affecting **every** async/multi-core program `maxon.exe` emits
(including shv2.exe once it exists). The alloc-side ownership gate stayed at 0
misses throughout (untriggered backstop). The rdata deterministic-merge
invariant (below) is shv2's own future backend concern, not yet exercised here.

_Per-function fan-out enabled at M5._

### Backend (Std → MIR → Target, runtime emitters)
_stub — thin mov/ret slice at M1; MM runtime + DebugStream producer at M6; GT
scheduler at Phase F._

### Event log & mm-trace harness

**Producer (maxon-sharp / C# bootstrap) — verified working as of Track 0.**
Binaries compiled by `maxon.exe` with `--debugstream` emit a binary MM event
stream (128-byte header + 8-byte packed entry headers + ticket-spinlock reserve;
schema frozen in `RuntimeEmitter.cs:41-148`). Only four MM codes are produced
live: `mm_alloc`/`mm_free`/`mm_incref`/`mm_decref` (0x01–0x04) plus depth
inc/dec (0x40/41) around destructor cascades. The Sched subsystem (0x20–0x2C)
and several Dbg/raw codes are **dead** (decoder-only); real scheduler tracing
rides the Dbg events (0x50–0x5E). Type-name resolution is **automatic**: every
heap allocation flows through `EmitAlloc`→`EnsureTagIndex`, whose names land in
the PE `.symtab` `MXDS_TAGS` blob (`module.TagNames` →
`EmitAllMemoryManagerFunctions`) — so `maxon monitor` prints real names
(`String`, `__ManagedMemory`), no extra wiring needed.

**Consumer = `maxon monitor [--filter=mm] <exe>`** (`DebugStreamMonitor.cs`) —
creates the shared segment, spawns the child with `MAXON_DEBUGSTREAM` set, drains
the ring, decodes via a hand-rolled PE parser, prints
`[+SSSS.mmm] <indent>mm_<verb> <Tag> #<id> [size=|rc=]<n>` to stdout (summary to
stderr). It forwards the child's own stdout, so trace lines are identified by the
`[+…]` timestamp prefix.

**Two-tier gating (preserve in shv2's own producer at M6):** compile-time
`Compiler.DebugStream` (zero instructions when off) + runtime `MAXON_DEBUGSTREAM`
(`__ds_base==0`). Wart to NOT reproduce: MM events pay two real CALLs before the
runtime-off check, unlike Dbg events which inline the `__ds_base` guard at the
call site. shv2 should inline the guard for every event family.

**mm-trace spec harness (this repo's C# harness).** `<!-- MmTrace -->` +
`` ```mm-trace `` block ⇒ compile with `DebugStream=true`, run under
`maxon monitor --filter=mm` (subprocess), **normalize**, compare. Normalization
(deterministic goldens): keep only `[+…]`-prefixed lines, strip the timestamp
prefix + indent, dense-renumber `#<id>` in first-appearance order. Verified
byte-stable across runs for single-green-thread programs. `--max-procs 1`
(needed only for programs that spawn green threads) does **not exist yet** —
Foundation 1 dependency; the harness sets `MAXON_MAX_PROCS=1` defensively (no-op
until F1). mm-trace tests stay off the batched path (per-process ring buffer).

---

## Milestone ledger

Checkboxes track landing against `PLAN.md`. Correctness-only gate through
Phase E; budget gate (≤30 s / ≤1.7 GB / >90% CPU) becomes hard at Phase F.

- [x] **Step 0** — plan + DEVLOG materialized in repo
- [x] **Track 0 / Foundation 2** — binary event log + mm-trace harness (C# harness: `mm-trace` block + redefined `<!-- MmTrace -->` → `maxon monitor` capture + normalize + regen; proof spec `specs/mm-trace.md`; producer verified end-to-end)
- [x] **Track 0 / Foundation 1** — multi-core green threads hardened (primitives + global-lock A/B + counters; **found & fixed a real ~2.5% multi-core crash** — x86 `__gt_spawn` passed `gt` in R10 to `__gt_enqueue` without reloading after `LeaveCriticalSection` clobbers it; fix mirrors ARM64's existing reload)
- [x] **Track 0** — validation harness (`maxon-shv2/track0/`): byte-identical aggregate across `MAXON_MAX_PROCS {1,2,7,16}`, 16 vs 1 workers, leak-clean + balanced mm-trace (568 alloc == 568 free), **remote-free MPSC path exercised (7775 cross-P pushes)**, global-lock A/B parity. 480+ clean high-concurrency runs post-fix.
- [ ] **M1** basics · [ ] **M2** variables · [ ] **M3** arithmetic
- [ ] **M4** control flow · [ ] **M5** functions (fan-out)
- [ ] **M6** heap+drops · [ ] **M7** moves+borrows · [ ] **M8** escape→refcount
- [ ] **M9** structs · [ ] **M10** strings · [ ] **M11** arrays
- [ ] **M12** enums · [ ] **M13** closures · [ ] **M14** interfaces/generics · [ ] **M15** error handling
- [ ] **M16** feature-complete · [ ] **M17** self-compile · [ ] **M18** budget gate
