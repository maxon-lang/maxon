# In-Process Multi-Core Codegen via the Green-Thread Runtime

## Context

**Goal:** make the Maxon self-hosted compiler use multiple CPU cores so a cold/large
build is faster. A cold stdlib compile runs **~992 register-allocator invocations**;
the ROADMAP measures whole-module regalloc+emit at **~480 ms on a 100-function
synthetic**. Today that loop is strictly sequential — one core does all of it.

**Decision made by the user:** parallelize **in-process** via the existing
green-thread (`async`/`await`) runtime — *not* via a multi-process worker pool.
This plan therefore commits to extending the runtime/compiler so CPU-bound codegen
tasks run on multiple OS threads, and does not pursue the subprocess alternative.

**Why this is viable (the load-bearing research finding):** the runtime is already a
Go-style **GMP scheduler** that spawns worker OS threads lazily, one per logical CPU.
A worker dequeues a green thread (GT) and runs its body **to completion on that OS
thread** — control only leaves the GT at `await`/`yield`/`return`. So a CPU-bound GT
*already* runs on whatever core dequeued it. The supporting machinery is already
multi-core-safe:

- **Worker spawn** (`__gt_enqueue` spawn phase, X64Backend.maxon / Arm64MacosGreenThread.maxon):
  spawns a new worker on a stopped P whenever `runnable_work > active_workers &&
  active_workers < __sched_max_procs`. `__sched_max_procs = ncpu`
  (`GetSystemInfo` on Windows, `sysconf(_SC_NPROCESSORS_ONLN)` on macOS). **No code
  change needed** — the gate has no "is this I/O work?" condition.
- **Allocator** has the per-P sharding *infrastructure* (`__slab_mcache_base`, 256
  shards) but the fast path is **NOT lock-safe today** — this is the one premise the
  codebase contradicts, so it is called out up front. The comment in
  [LowerMaxonToStd.maxon](maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon)
  (~lines 665–671) is explicit and **future-tense**: *"The slab fast path uses a
  **single shared mcache**, so **once** the green-thread scheduler runs GTs on multiple
  OS threads, every __slab_alloc / __slab_free **must hold this lock**."* In other
  words the fast path is a single shared mcache that does **not** take `__slab_lock`
  today; the per-P lockless mcache the architecture envisions is **not wired up**. True
  multi-OS-thread GT execution has never run (the spec runner pins itself to one M via
  `maxon_gt_set_single_threaded()`, capping `__sched_max_procs` to 1). This makes the
  allocator a **prerequisite**, not a free ride — see **Phase 0**.
- **Refcounts** are atomic (`__mm_incref`/`__mm_decref` → `atomicInc`/`atomicXadd`).
- **Per-P state is migration-safe**: a GT stores no owning-P pointer; the allocator
  reads the current P via TLS on every call.

**The two real blockers** are (a) the allocator fast path above (**Phase 0**) and (b)
the compile-time gate **E3073** (`checkAsyncYielding` in SemanticCheck.maxon), which
*rejects* `async` on a function that never yields — exactly our CPU-bound regalloc
tasks. There is **zero preemption**, but we don't need it: regalloc tasks are short and
run to completion; fairness is a non-issue for a batch of equal-weight jobs. The two
secondary items are (1) make sure workers actually get spawned for a burst of CPU GTs
and (2) make the per-function emit's one piece of *ordering-sensitive* shared mutable
state (rdata-constant registration) deterministic under concurrency.

Note the two distinct shared-state problems, which must not be conflated: the
**allocator race** (Phase 0) is a *correctness/throughput* problem affecting **every**
heap op a worker performs, while the **rdata-constant order** (Phase 4) is a
*determinism* problem affecting one specific append. Byte-identity testing catches the
second but **not** the first — a global lock held on every alloc keeps output identical
while silently serializing the workers — so Phase 0 needs its own contention
measurement, not just the byte-diff gate.

## Approach

Split the existing sequential per-function codegen loop into a fan-out of `async`
tasks (bucketed by function, see granularity), `await` them all, then run the
deterministic concat/link single-threaded. Concretely:

0. **Make the slab allocator safe under multi-OS-thread GTs** (Phase 0) — the one
   premise the codebase contradicts: the fast path is a single shared mcache with no
   lock today. This is a prerequisite, not a free ride.
1. **Unblock CPU-bound `async`** by introducing an explicit *parallel-work* boundary
   the E3073 fixed-point recognizes (so we don't have to lie about I/O).
2. **Fan out** the regalloc+emit of the miss-set across `async` tasks in the parent.
3. **Make the one ordering-sensitive write** (lazy rdata-constant registration)
   deterministic, keeping output **byte-identical** by registering constants in a
   canonical order during the single-threaded merge.
4. **Gate** behind a flag + a function-count threshold + `cpuCount() > 1`, with serial
   fallback for small builds and for wasm32-wasi.

Codegen is the primary track (Phases 1–6) because it is the dominant cost *and* cleanly
per-function. The **front-end (lex/parse)** is a serial prefix that blocks codegen, so
shrinking it matters more as codegen scales (Amdahl) — it is sequenced as an explicit
**later track (Phase 7)** that goes **measure → cut redundant serial work (no threads) →
parallel lex → concurrent parse only if measured-dominant**, reusing the same
`async`/`cpuCount`/byte-identity infrastructure built in Phases 1–4.

### Why this loop, and why it's safe to parallelize

The loop is `for func in pipeline.mirModule.functions 'collect'` in
[BackendDispatch.maxon](maxon-selfhosted/Compiler/Targets/BackendDispatch.maxon)
(`emitBackendIncrementalWith`, ~lines 1847–1876). Each iteration calls
`backend.emitFunctionChunk(targetFunc)`, producing a **self-contained,
position-independent `FunctionCodeChunk`** (chunk-local offsets; all addresses
resolved later in `concatX64FunctionChunks`). Register allocation
(`allocateRegistersForFunc`) is a pure function of its input `TargetFunc` — no shared
state. The **only** shared mutable write in the per-function path is lazy rdata
constant registration (`registerFloatConstant` / `registerXmmMaskConstant` in
[StdOpHelpers.maxon](maxon-selfhosted/Compiler/Targets/Shared/StdOpHelpers.maxon),
~lines 437–455), which appends to the shared `GlobalDataTable`. That registration is
already mirrored chunk-locally: `captureChunkRdataConstants` copies each chunk's lazy
constants into `chunk.rdataConstants`, so **each chunk already carries its own
constants** and the shared table can be rebuilt deterministically afterward.

## Plan

### Phase 0 — Make the slab allocator safe under multi-OS-thread GTs (prerequisite)

This is **new scope the original draft missed** and it gates everything else, because
regalloc is allocation-heavy and every worker hits the allocator on essentially every
op. Today the fast path is a single shared mcache with no lock on the hot path
([LowerMaxonToStd.maxon](maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon)
~665–671). Running codegen GTs on multiple workers without addressing this is a heap
data race. Two viable routes:

- **0a. Correctness-first (cheap, ships the milestone): hold `__slab_lock` on the fast
  path.** Make `__slab_alloc` / `__slab_free` take the existing global `__slab_lock`
  unconditionally when `__sched_max_procs > 1`. This is the *minimum* that makes
  multi-core GT execution sound. It will serialize allocation, so it is a **correctness
  baseline, not the performance target** — it exists to prove byte-identity and shake
  out non-allocator races first, with the lock contention measured (not assumed away).
- **0b. Performance: wire up the per-P lockless mcache the architecture already
  sketches.** Give each P its own mcache shard (`__slab_mcache_base` already reserves
  256 shards), lockless pop/push on the owning P, route cross-P frees through an MPSC
  remote-free queue, and take `__slab_lock` **only** on refill/slow-path. This is the
  design the code comment anticipates but **has never been implemented or exercised** —
  treat it as real, non-trivial runtime work, not a config flip.

**Order:** do **0a** before the Phase-3 milestone (so the first parallel run is at
least *correct*), then measure `__slab_lock` contention on the cold-stdlib regalloc
burst. If contention is a meaningful fraction of emit time (likely — regalloc allocates
constantly), do **0b** before claiming speedup. The Phase-3 byte-identity milestone
will pass under 0a alone, so it **cannot** be the signal for whether 0b is needed —
the speedup measurement (Verification #5) and an explicit lock-wait counter are.

**Cross-target:** the lock/mcache work is x64-windows and arm64-macos
([X64Backend.maxon](maxon-selfhosted/Compiler/Targets/X64/X64Backend.maxon),
[Arm64MacosGreenThread.maxon](maxon-selfhosted/Compiler/Targets/Arm64/Arm64MacosGreenThread.maxon)).
wasm32-wasi is single-threaded and unaffected.

### Phase 1 — A CPU-parallel boundary that passes E3073 (the unblock)

The E3073 gate is a call-graph fixed-point: a function "yields" if it (transitively)
calls something in the hardcoded `isAsyncYieldStub` list (SemanticCheck.maxon, ~lines
1027–1099, 1189–1213). Rather than falsely tagging regalloc as I/O, add a **first-class
CPU-parallel marker** so the semantics read honestly.

- Add a no-op builtin `__Builtins.parallelBoundary()` (a scheduler hint; lowers to a
  cheap `__gt_yield`-style reschedule point or nothing). Register its signature +
  lowering in [LowerMaxonToStd.maxon](maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon),
  modeled on the existing `gtSetSingleThreaded` builtin (the closest template:
  zero-arg, void, scheduler-related).
- Add `"__Builtins.parallelBoundary"` to `isAsyncYieldStub` so any function that calls
  it is treated as a legitimate `async` target. The CPU-bound codegen task function
  calls `__Builtins.parallelBoundary()` once at entry.
- **Cross-target:** lower on x64-windows and arm64-macos; on wasm32-wasi it can be a
  pure no-op (codegen is serial there anyway).

Net effect: `async emitFunctionTask(...)` compiles, instead of being rejected by E3073.

### Phase 2 — `cpuCount()` stdlib API

The runtime already computes ncpu (`__sched_max_procs` / `__sched_alloc_procs` via
`GetSystemInfo` / `sysconf`) but **does not expose it to user code**. Add it so worker
count is core-aware rather than a magic number.

- Runtime helper `maxon_cpu_count` in the backends: call `GetSystemInfo`
  (Windows) / `sysconf(_SC_NPROCESSORS_ONLN)` (macOS) directly — **independent of
  green-thread init** so it works before any `async` runs — clamp to `>= 1`.
- Builtin `__Builtins.cpuCount()` (signature + lowering in LowerMaxonToStd.maxon,
  modeled on `gtSetSingleThreaded`).
- Public `Process.cpuCount()` in [stdlib/Process.maxon](stdlib/Process.maxon)
  (small file, already hosts `executablePath()`). Returns `1` on wasm32-wasi.

### Phase 3 — Fan out the codegen loop with `async`/`await`

In `emitBackendIncrementalWith` ([BackendDispatch.maxon](maxon-selfhosted/Compiler/Targets/BackendDispatch.maxon)),
behind a `project.db.parallelCodegen` flag (default false initially):

- Extract the per-function body (target-lower if needed + `emitFunctionChunk`) into a
  standalone function `emitOneFunctionTask(...) returns FunctionCodeChunk` that calls
  `__Builtins.parallelBoundary()` and does **not** touch shared `globalData` for
  ordering-sensitive state (it produces a chunk whose `rdataConstants` are captured
  chunk-locally via the existing `captureChunkRdataConstants`).
- Replace the serial `collect` loop's **miss-set** work with: spawn one
  `async emitOneFunctionTask(missFunc)` per miss (or per bucket — see granularity),
  collect the promises into a `Promise`-array, then `await` each in
  `mirModule.functions` order. This mirrors the proven array-of-promises pattern in
  `specs/async-await.md` (push `async work(i)` into an array, then `for p in arr: await p`).
- Cache **hits** stay on the existing fast path (no task needed).

**Granularity:** use **buckets as the default**, not one-task-per-function. The
smallest milestone may wrap a single function for simplicity, but the scaled fan-out
should bucket (N functions per task, N ≈ `max(1, missCount / (cpuCount()*k))`) weighted
by MIR op-count (the regalloc-cost proxy). Two independent reasons force this, both
sharpened by verification: (1) each GT mmaps a **1 MiB stack**, so one-per-function
across the cold-stdlib's ~992 misses is ~1 GiB of concurrent stacks — bucketing bounds
concurrent GTs to ≈`cpuCount()*k`; (2) fewer, longer GTs **reduce allocator pressure**,
which matters directly given Phase 0 (under the 0a global lock, every GT entry/exit and
every op contends — coarser tasks mean fewer contention points). One-task-per-function
remains a fallback only if buckets show measurable imbalance.

**Parent runs single-threaded otherwise:** only the `emitOneFunctionTask` GTs are the
parallel work; the surrounding driver is ordinary code. Workers are spawned
automatically by `__gt_enqueue` as the burst of runnable GTs exceeds active workers,
up to ncpu.

### Phase 4 — Deterministic merge (byte-identical output)

Code layout is already deterministic: `concatX64FunctionChunks` lays out chunks in
`mirModule.functions` order, which the parent preserves by `await`-ing/assembling in
that order. The remaining determinism hinge is **rdata-constant registration order**:

- After all chunks are collected, register every chunk's `rdataConstants` into the
  shared `globalData` **single-threaded, in a canonical order** =
  `mirModule.functions` order, then each chunk's local `rdataConstants.names` order.
  Registration is idempotent-by-label (`lookupGlobalData` dedup), so the first-seen
  position of each distinct constant becomes a pure function of function order —
  independent of which worker emitted it or when. Reuse the existing
  `reregisterChunkRdataConstants` call, just guaranteeing canonical iteration order
  for all chunks (hits and misses alike).
- Add a debug-gated assertion: hash the final `globalData.names` sequence and the
  emitted `.text`/`.rdata`, compare serial vs parallel.

### Phase 5 — Work-stealing balance (optional, only if measured imbalance)

The deferred `__gt_steal_work` (steal half a random peer P's local queue before
parking) is **not implemented** on the current single-M arm64 path; x64 has the
reference. If profiling shows idle workers while one P's queue is backed up, implement
the half-queue steal. For an equal-weight regalloc batch this may be unnecessary —
**measure before building it.**

### Phase 6 — Gating & fallback

- Enable only when `parallelCodegen` flag is on AND `missFuncs.count() >= threshold`
  (start ~32) AND `Process.cpuCount() > 1`. Below threshold → existing serial loop
  untouched (the common single-edit warm rebuild must never pay spawn cost).
- **wasm32-wasi:** force serial. It has no OS threads (asyncify is single-threaded
  software) and the chunk path is already disabled there. `__Builtins.parallelBoundary`
  is a no-op; `cpuCount()` returns 1; the threshold/`cpuCount()>1` gate keeps it serial.

### Phase 7 — Shrink the serial front-end prefix (later, separate track)

**Why this matters more than its raw % suggests.** Parsing is a **serial prefix** of
every compile: codegen cannot begin until `queryAllModule` → `queryAllMid` completes.
So by Amdahl's law, once codegen is parallelized to near-zero (Phases 1–6), the
*unparallelized front-end becomes the dominant floor* on cold-build wall time. The
ROADMAP's "~400 ms of ~2 s" understates the end state — after codegen scales, the
front-end is most of what's left. So we do want to minimize it, and the right order is
**measure → cut redundant serial work (no threads) → parallel lex → (conditionally)
concurrent parse**, cheapest-and-safest first.

#### 7a. Measure the real lex-vs-parse split (do this first)

Pass timing already exists — the `--profile-passes` flag (Main.maxon) prints a
descending breakdown, and the front end already has **separate `__lex` and `__parse`
buckets** recorded in `queryTokens` / `queryParseOps`
([Queries.maxon](maxon-selfhosted/Compiler/Queries.maxon), the `recordPassSince("__lex", …)`
and `recordPassSince("__parse", …)` calls). `__emitBackend`, `__queryAllMid`,
`__regalloc-user`/`__regalloc-rt` are also bucketed. **Run `--profile-passes` on a cold
stdlib build and a 300-fn synthetic to get the actual `__lex` vs `__parse` vs
`__queryAllMid` split before building anything.** This decides whether parallel *lexing*
alone suffices or whether the hard concurrent-*parsing* refactor is justified.

**The prescans are completely unmetered today** (verified: `preRegisterInterfaceNames`
and `preRegisterFunctionThrows` contain no `passTimerStart`/`recordPassSince`, so their
cost is hidden inside `__queryAllMid`). Adding a `__prescan` bucket is therefore a
**precondition** for 7b — the "roughly halves prescan cost" claim below is currently
*unmeasured* and must be backed by this bucket before the fusion work is justified.

#### 7b. Serial-prefix reductions that need NO concurrency (do these regardless)

These shrink the prefix for *every* target including wasm, with no thread-safety risk —
the highest value-to-risk ratio in the whole plan's front-end:

- **Fuse the two prescans into one pass.** `preRegisterInterfaceNames` and
  `preRegisterFunctionThrows` ([Queries.maxon](maxon-selfhosted/Compiler/Queries.maxon),
  ~lines 214–271 and ~360–366) each loop over *all* files iterating the (cached) token
  stream — two full whole-project token walks. They operate on the same tokens with no
  dependency between them, so they can share one walk. **Caveat (verified): the two use
  different scan strategies** — `preRegisterInterfaceNames` is a raw keyword/paren-depth
  token scan, while `preRegisterFunctionThrows` constructs a `Parser` per file and calls
  `parser.prescanThrowsHeaders()`. Fusion means driving both scanners over a single
  per-file token walk (one `queryTokens` per file feeding both), not literally
  concatenating two loop bodies. Still a clean win — saves one whole-project
  `queryTokens` pass — but scope it as "share the walk," not "merge the logic." Expected
  to roughly halve prescan cost; confirm against the new `__prescan` bucket (7a).
- **Skip `beginFileParse` rollback on a token cache hit.** `queryParseOps` calls
  `beginFileParse` (parser-side-effect rollback) before every parse; on a warm rebuild
  where a file's tokens are unchanged, that cleanup is wasted. Gate it on the
  `queryTokens` cache-miss signal. (Incremental-build win; cold build unaffected.)
- **Cache stdlib/library tokenization.** `parseStdlibSource` / `parseLibrarySource`
  ([StdlibLoader.maxon](maxon-selfhosted/Compiler/StdlibLoader.maxon), ~lines 723, 747)
  call `tokenize()` directly, bypassing `tokenCache` and re-lexing on every process
  start. Route through the per-file token cache (or a process-global one). Minor on
  small projects, real on multi-library builds.

#### 7c. Parallel lexing (low-risk concurrency)

`tokenize(source)` ([Lexer.maxon](maxon-selfhosted/Compiler/Lexer.maxon), ~line 1269) is
a **pure function of the file bytes** — no shared writes, no interning. Tokenize all
files concurrently using the Phase 1–3 `async` mechanism: spawn pure lex tasks that each
return a `TokenArray`, then have the parent populate `tokenCache` and record `queryTokens`
deps **single-threaded** after the fan-out (so the query engine is never touched
concurrently). After 7b makes tokens the single shared input to prescans+parse, this
front-loads *all* lexing in parallel before the serial parse begins. If 7a shows `__lex`
is a meaningful slice, this is most of the realizable front-end win for low risk.

#### 7d. Concurrent parsing (the hard part — only if 7a/7c leave parse dominant)

Parsing is **not** parallel-safe today: `queryParseOps`
([Queries.maxon](maxon-selfhosted/Compiler/Queries.maxon), ~line 106) mutates project
globals *during* the parse — three interners (`project.typeNames`, `project.strings`,
`project.calleeNames` — [Project.maxon](maxon-selfhosted/Compiler/Project.maxon)
~lines 2070–2175) and registries (`funcReturnTypes`, `methodNameIndex`, …), none
thread-safe — and the query engine records deps onto a *shared* `activeQueryStack` +
`dependencies` + `depIndex` ([QueryEngine.maxon](maxon-selfhosted/Compiler/QueryEngine.maxon),
~lines 49–66, 253–261), whose "top of stack = current query" invariant breaks under
concurrent push/pop. Making parse concurrent requires:

- **Per-task thread-local interners + deterministic merge** into the project interners
  after all parses (merge in `sourcePaths` order → IDs identical to serial; avoids
  lock contention on the hottest path).
- **Thread-local active-query stacks + per-task dependency buffers**, merged after the
  parse fan-out, so the incremental-compilation backbone stays correct.
- The whole-project prescans (7b) already run before the parse loop, so forward
  references are resolved — which *helps* per-file parse independence once the
  shared-write problem is solved.

This is a large, invasive change to the incremental-compilation core. **Only pursue it
if 7a measurement + 7b reductions + 7c parallel lexing leave `__parse` as a measured,
dominant remaining cost.**

#### 7e. Determinism (applies to 7c and 7d)

Interner IDs and the dependency graph must come out byte-identical to the serial build
(merge thread-local state in `sourcePaths` order). Verify with the same byte-identical
binary diff and self-host bootstrap gates as the codegen track.

**Recommended order within this track:** 7a (measure) → 7b (serial cuts, no threads) →
7c (parallel lex) → re-measure → 7d (concurrent parse) only if justified.

## Critical files

- [maxon-selfhosted/Compiler/Targets/BackendDispatch.maxon](maxon-selfhosted/Compiler/Targets/BackendDispatch.maxon)
  — `emitBackendIncrementalWith`: the fan-out, the `emitOneFunctionTask` extraction,
  and the canonical-order merge live here (~lines 1847–1876).
- [maxon-selfhosted/Compiler/SemanticCheck.maxon](maxon-selfhosted/Compiler/SemanticCheck.maxon)
  — `checkAsyncYielding` / `reportIfNonYielding` / `isAsyncYieldStub`: add the
  `parallelBoundary` marker to the yield allowlist (~lines 1027–1213).
- [maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon](maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon)
  — register + lower `__Builtins.parallelBoundary` and `__Builtins.cpuCount`, modeled
  on the existing `gtSetSingleThreaded` builtin.
- [maxon-selfhosted/Compiler/Targets/X64/X64Backend.maxon](maxon-selfhosted/Compiler/Targets/X64/X64Backend.maxon)
  and [.../Arm64/Arm64MacosGreenThread.maxon](maxon-selfhosted/Compiler/Targets/Arm64/Arm64MacosGreenThread.maxon)
  — **Phase 0:** the slab fast-path lock (0a) and per-P lockless mcache + MPSC remote-free
  (0b) live in the `__slab_alloc`/`__slab_free`/`__gt_enqueue` runtime emitters here;
  also the `maxon_cpu_count` runtime helper; verify the `__gt_enqueue` worker-spawn gate
  fires for a CPU-GT burst; (optional) implement `__gt_steal_work`.
- [maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon](maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon)
  — the `__slab_lock` / single-shared-mcache definition + the future-tense comment
  (~665–671) that documents the Phase-0 gap; this is where the "must hold this lock"
  contract is recorded.
- [maxon-selfhosted/Compiler/Targets/Shared/StdOpHelpers.maxon](maxon-selfhosted/Compiler/Targets/Shared/StdOpHelpers.maxon)
  — `registerFloatConstant` / `registerXmmMaskConstant` / `captureChunkRdataConstants` /
  `reregisterChunkRdataConstants`: confirm chunk-local capture is complete so the
  parent merge is the *only* writer of `globalData` for these constants.
- [stdlib/Process.maxon](stdlib/Process.maxon) — public `Process.cpuCount()`.

Front-end track (Phase 7):
- [maxon-selfhosted/Compiler/Queries.maxon](maxon-selfhosted/Compiler/Queries.maxon)
  — `queryTokens`/`queryParseOps` (`__lex`/`__parse` buckets), `preRegisterInterfaceNames`
  + `preRegisterFunctionThrows` (fuse into one pass), `queryAllModule`/`queryAllMid`,
  `beginFileParse` (skip-on-cache-hit).
- [maxon-selfhosted/Compiler/Lexer.maxon](maxon-selfhosted/Compiler/Lexer.maxon)
  — `tokenize` (pure; the parallel-lex unit).
- [maxon-selfhosted/Compiler/StdlibLoader.maxon](maxon-selfhosted/Compiler/StdlibLoader.maxon)
  — `parseStdlibSource`/`parseLibrarySource` (route through token cache instead of direct `tokenize`).
- [maxon-selfhosted/Compiler/Project.maxon](maxon-selfhosted/Compiler/Project.maxon)
  and [maxon-selfhosted/Compiler/QueryEngine.maxon](maxon-selfhosted/Compiler/QueryEngine.maxon)
  — the interners + query-engine state to make thread-local-then-merge (only if Phase 7d is taken on).

## Smallest viable first milestone

**Prove a single `async` CPU-bound regalloc task runs on a worker thread and produces
byte-identical output, x64-windows only:**

1. Phase 0a (hold `__slab_lock` on the fast path when `__sched_max_procs > 1`) — without
   this the milestone is a heap race, not a clean test.
2. Phase 1 (`parallelBoundary` + E3073 unblock) and a minimal Phase 3 that wraps
   *one* miss-set function's emit in `async emitOneFunctionTask(...)` + `await`.
3. Build a small multi-function program twice — serial vs the flag — and confirm the
   output binary is **byte-identical**.
4. **Confirm the GT actually ran on a second worker** (not just cooperatively on M0):
   check `__sched_active_workers` reached 2, or that the GT observed a different TLS P.
   Byte-identity does **not** prove multi-core execution happened.

If byte-identity holds **and** a second worker ran, the per-function chunk model is
sound and the rest is scaling (fan out all misses) + determinism hardening (Phase 4) +
the Phase-0 contention question. If byte-identity fails, the rdata-ordering work
(Phase 4) is bigger than estimated and must be re-scoped first. **Caveat that makes
this milestone necessary but not sufficient:** under 0a's global lock it will pass even
if allocation is fully serialized — so it validates *correctness and that a worker
spawned*, but the *speedup* question is deferred to the scaled fan-out + Phase-0
contention measurement, not answered here.

## Verification

1. **Byte-identical binary diff (primary gate).** `mcp__maxon-dev__build` the same
   program twice (serial vs parallel flag) and diff binaries byte-for-byte. Cases:
   tiny program (below threshold → identical path), 100-fn synthetic (ROADMAP's
   regalloc benchmark), cold stdlib compile (~992 regalloc invocations), warm
   single-edit rebuild (must stay serial). Any diff = determinism bug (almost
   certainly rdata-constant order, Phase 4).
2. **Determinism stress.** Run the parallel flag repeatedly with varied effective
   worker counts (`cpuCount` clamps, or a debug `--max-procs` knob): 1, 2, 7, ncpu.
   All outputs must be identical to each other and to serial — varied scheduling is
   the cheapest fuzzer for the merge order.
3. **Full spec suite.** `mcp__maxon-dev__run_spec_test` (and `spec_test_outcome` for
   triage) with parallel codegen forced on — identical test outcomes vs serial.
   `fix-spec-tests` skill is the loop.
4. **Self-hosted bootstrap (strongest test).** `mcp__maxon-dev__run_self_hosted_test`
   — compile the compiler *with itself* under parallel codegen, then have that
   compiler rebuild + pass the suite. A miscompiled chunk anywhere in 992+ functions
   surfaces as a broken stage-2 compiler.
5. **Speedup measurement.** Use the existing `recordPassSince("__emitTargetCode", ...)`
   timing in BackendDispatch.maxon (and `mcp__maxon-dev__dump_stages`) to compare
   serial vs parallel emit wall-clock on the 100-fn synthetic (~480 ms baseline) and
   the cold stdlib. Report speedup and the crossover function count (validates the
   Phase-6 threshold).
6. **Memory safety + allocator contention (Phase 0 gate).** `mcp__maxon-dev__mm_trace_analyze`
   / leak-check (exit 101) on parallel runs — concurrent slab alloc/free + cross-P frees
   are the riskiest new interaction; confirm no leaks/double-frees/heap corruption. This
   is the primary correctness check for Phase 0a (the global lock must actually make
   concurrent allocation sound). **Separately**, instrument a `__slab_lock` wait/spin
   counter (or contended-acquire count) and read it on the cold-stdlib regalloc burst:
   high contention under 0a is the empirical trigger for doing 0b (per-P lockless
   mcache). Byte-identity will **not** reveal this — only the counter and the speedup
   number (#5) will.
7. **Front-end measurement (gates Phase 7).** Build with `--profile-passes` on a cold
   stdlib compile and a 300-fn synthetic; read the `__lex` / `__parse` / `__prescan` /
   `__queryAllMid` breakdown. This is the empirical gate for *how far* to take Phase 7 —
   serial cuts (7b) are unconditional, parallel lex (7c) is worthwhile only if `__lex` is
   a meaningful slice, and concurrent parse (7d) only if `__parse` stays dominant after
   7b+7c. After codegen scales, re-measure: Amdahl makes whatever front-end remains the
   new wall-clock floor.

## Risks

- **rdata-constant ordering non-determinism (HIGH, contained).** The whole correctness
  story. Mitigated by Phase 4's canonical single-threaded registration; verified by
  repeated-N byte-diff. Tractable because dedup is idempotent-by-label and the parent
  controls function order.
- **Allocator races under true multi-core GT execution (HIGH — promoted from the
  draft's MEDIUM).** Refcounts are genuinely atomic (`atomicInc`/`atomicXadd`), so those
  are fine. But the slab **fast path is a single shared mcache with no lock today** and
  has **never** run under multiple OS threads (spec runner pins to one M). This is not a
  "limited exercise" risk — it is a guaranteed data race the moment Phase 3 runs, which
  is why it is pulled forward into **Phase 0** as a prerequisite rather than left as a
  reactive fix. 0a (global lock) makes it correct; 0b (per-P lockless mcache) makes it
  fast. Mm-trace + bootstrap verify correctness; a `__slab_lock`-wait counter and the
  speedup measurement verify it isn't silently serialized.
- **Worker actually spawns for a CPU burst (MEDIUM).** The `__gt_enqueue` gate should
  spawn up to ncpu workers, but this hasn't been driven by a pure-CPU burst before.
  Verify empirically (Phase 3 milestone): spawn N CPU GTs on an N-core box, confirm N
  workers run concurrently (wall-clock ≈ serial/N, not ≈ serial).
- **No preemption (LOW for this workload).** A long non-yielding GT monopolizes its
  worker. Regalloc tasks are short and equal-weight, so this only risks tail imbalance,
  addressed by bucketing/work-stealing (Phase 5) if measured — not a correctness issue.
- **GT stack cost (LOW, mitigated by default).** Each GT mmaps ~1 MiB. One-task-per-
  function across ~992 misses ≈ 1 GiB of concurrent stacks — which is exactly why
  Phase 3 makes **bucketing the default**, bounding concurrent GT count to ≈ncpu·k.
