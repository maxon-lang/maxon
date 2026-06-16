# Libraries in the self-hosted Maxon compiler — full phased design

## Implementation status (updated 2026-06-15)

Branch `feature/libraries`. All work below is committed and verified green on a **cold** cache:
self-hosted spec **2220/2220**, wasm32-wasi **2206/2206**, no memory leaks (+1 each from the Phase 0
harness command). The `maxon test-lib-cache` harness is **16/16 green** (4 cross-process Phase-5
scenarios added).

**Spec-runner overhaul (2026-06-15) — de-batching + run-skip cache (NOT a library phase, but it
supersedes the 2026-06-14 spec-run-speed finding below).** Three commits landed a rework of the
self-hosted spec runner itself, independent of the library tiers:
- **Chunk-cache rdata correctness fix (`18232d4c9`)** — the determinism *Prerequisite* this plan
  always named (Property 1: same compiler → byte-identical binary regardless of cache state) is now
  committed. A reused chunk's lazily-registered float/xmm-mask rdata constants weren't re-registered
  on a chunk-cache HIT → corrupt binary for real `maxon build`s (masked in the suite, which runs
  `enableIncremental=false`). Fixed across 7 files (capture at emit, replay on hit, persist in the
  chunk codec `USER_CHUNK_CACHE_VERSION → 2`, rdata-sort determinism). This is the stable-binary-hash
  foundation the run-skip cache depends on.
- **De-batched the runner (`cd1f7b7ed`)** — removed the batched-compile path entirely (BatchRewriter
  symbol-mangler + shared batched binary + batch→singles fallback); every test now compiles+runs
  per-fragment through the parallel worker pool. **Measured a NET WIN, not a regression:** batched
  61.0 s → de-batched 52.2 s on the full host suite (total compile work −31%, 682 s → 474 s).
  Batching was *costing* wall time, not saving it. De-batching also exposed and fixed 5 latent bugs
  it had masked: struct-`type List` shadowing of the stdlib `List` (a real namespace bug, fixed in
  `cd1f7b7ed` via `resolveAllStructTypes` upsert), wasm `mrt_alloc` pruned by DCE for hand-emitted
  helpers, wasm exit
  code 8080 truncating to 144, the wasm socket-connect 2 s timer racing the 2 s runner budget (→ 1 s),
  and per-spec worker-job timeout scaling under contention.
- **Run-skip cache (`b3e6e880f`, NUL-byte repair `a47024750`)** — after compiling, if the produced
  binary + argv + stdin + target + the run-outcome expectation byte-match a recorded PASS, **skip the
  subprocess run** and replay the prior verdict; compile + all Required* (IR/lowering/rdata) checks
  still run on a hit. Per-spec `run-cache.bin` (magic `MRTC`, `RUN_CACHE_FORMAT_VERSION = 2`), 64-bit
  key (FNV(binary) ‖ FNV(argv+stdin+target+**expectationKey**)), read-modify-union write under atomic
  rename. The expectation in the key is load-bearing: a changed expected-exitcode with an unchanged
  source (→ identical binary) MUST miss + re-run, not replay the stale PASS — verified by gate (c).
  **Warm re-run: 52.2 s → 43.6 s** (skipped 1849 of 2220),
  identical 2220/2220 totals. Caching stacks on de-batching; neither cancels the other.

**Net spec-runner result:** batched 61 s → de-batched-cold 52 s → run-skip-warm 44 s (−28%), and the
run cost itself is driven to ~zero on a warm cache (no compiler rebuild). A compiler rebuild changes
every fragment's binary, so the cache mostly misses right after a compiler change — it helps CI /
spec-edit / flaky re-runs, not the edit-compiler-then-test loop. Phase 6 verification gates all green:
cold/warm parity, edit-re-runs soundness, concurrency (198 cache files parse clean, 0 stray `.tmp`),
wasm 2206/2206.

**Done (library phases):**
- **Phase 1 — `Library` value type + keyed registry.** The ~15 stdlib singletons collapsed into one
  `Library` (in `Compiler/Library.maxon`) held in a `LibraryRegistry`; exported accessors delegate
  through it. Builtin-type bootstrap extracted to `registerBuiltinTypeBootstrap`. Deterministic file
  sort added (spike A2). Byte-behavior-preserving.
- **Phase 2a — cache relocated to the library's own dir.** Cache now lives in `stdlib/.maxon/cache/`
  (shared across projects), filename `stdlib-<target>-<fp>.mxc` with a compiler-fingerprint token for
  coexistence. `maxon clean` command added. Sticky `stdlibCacheProjectDirs` removed. The old
  `noStdlibCache` MCP flag was removed (superseded by `maxon clean`).
- **Phase 2b — atomic cache writes.** New `mrt_file_rename` primitive lowered on **all four targets**
  (Windows MoveFileExW, x64/arm64-linux rename syscall, WASI rename-at); `File.rename` stdlib surface;
  every cache write routes through `writeFileAtomic` (temp + atomic rename). No torn reads.
- **Spec-runner warming removed** (the parent pre-warm + per-worker warm + `warmStdlibCache`). This
  exposed a latent **stale-id leak** the warm had masked: `restoreRegistries` restored
  `topLevelVars[*].slotType` without id-translation → fixed (root cause, not a workaround). mm-trace
  references removed from the self-hosted compiler (they were comment-only; no functional mm-trace
  code exists self-hosted).
- **Phase 3.5 — library-owned id-space (CACHE_FORMAT_VERSION → 81).** The cache codec is now
  **project-independent**: every id-bearing field serializes through the cache's OWN snapshot tables
  (model (b): the library is interned first → its ids are a stable prefix identical in every consumer).
  Deleted `writerGidToProjectGid`/`writerGidToDataGid` + the entire `translate*` family. Persist gate
  removed — **any project (spec workers, user builds) now persists its lazily-grown cache.**
- **Phase 0 — library-cache test harness (`maxon test-lib-cache`) + `--cache-stats`.** New
  `Testing/LibCacheTestRunner.maxon` drives the real compiler as a *subprocess* against isolated
  throwaway cache roots (internal `--cache-root=DIR` flag on `Project`, never a process global — the
  persistent spec workers reuse one process across jobs, so a global would bleed a stale root into a
  worker's real builds). `--cache-stats` emits a machine-readable `CACHE_STATS regalloc_hits=…
  regalloc_misses=… stdlib_funcs=… stdlib_valid=…` line from the existing counters. 9 scenarios: cold
  writes / second-build hit / delete-rebuilds / distinct-roots-coexist / 5 fault-injection cases
  (garbage, truncated, zeroed, mid-body-garbage, stray-tmp) each asserting a clean cold-rebuild
  fallback, exit 0, no crash. The harness caught two real defects, fixed properly:
  - **Cache-format v81 → v82: total-file-length integrity field.** A *truncated* `.mxc` (valid header
    prefix, body cut short) previously ran a body reader off the end of the buffer and panicked. The
    header now carries the writer's final `buf.count()`, validated against the on-disk length before
    any body decode, so a truncated file is a clean miss.
  - **Byte readers never panic.** `readDword`/`readQword` now `throws ByteReadError` (out-of-range) and
    NEVER panic. Each caller decides: code emitters (PE/MachO/Arm64 reloc patchers, reading buffers the
    compiler just emitted — an OOB there is a guaranteed bug) use `otherwise panic(...)` so the bug
    surfaces *at the call site* with context; the **entire cache-decode path** translates the throw to
    `CacheCorruption.invalidEncoding` and falls back to a cold rebuild. ~60 decoder signatures gained
    `throws CacheCorruption` and every invalid-tag / OOB `panic("corrupt cache: …")` became a throw, so
    **no corruption shape can crash the compiler** — proven by the `mid-body-garbage` scenario (corrupt
    body bytes, length intact, so the damage reaches the deep body readers).
- **Phase 3 — two-file cache split (CACHE_FORMAT_VERSION → 83).** The single flat per-target `.mxc`
  splits into a meta file (`stdlib-meta-<target>-<fp>.mxc`: Tier A metadata + Tier B inlinable Std IR,
  5.9 MB) and a per-target file (`stdlib-<target>-<fp>.mxc`: Tier C linkable bytecode, ~0.2 MB). The
  encoder/decoder split into shared-header + `encodeLibraryMetaBody`/`encodeLibraryTargetBody` and
  `decodeLibraryMeta`/`decodeLibraryTarget`; the target file stores the meta's FNV content-hash so the
  two stay paired (a target file is rejected against a mismatched meta). **Key finding:** the hoped-for
  *shared* (cross-target) meta is impossible — a target-independence guard proved x64 ≠ wasm32 because
  `layoutDescriptors`/`structTypes` byte sizes bake in pointer width (wasm32 = 4, x64/arm64 = 8). So
  the meta is **per-target** (the plan's documented fallback); each target's meta is deterministic
  across rebuilds. Scope was **split-only** — per-function Tier B (→ Phase 7) and the Tier-C seek index
  (→ Phase 6) were deferred to their consumer phases. `maxon test-lib-cache` is 12/12 (3 new meta
  scenarios); cross-target host+wasm yields four coexisting files, no panic.

**Key finding — the stdlib-cache persist/sharing win is small, and that is expected (measured
2026-06-14):** The user expected a big repeated-run spec speedup from cross-worker cache sharing;
measurement shows the **ceiling is ~5%**. The stdlib cache only removes stdlib lazy-codegen, which is
a one-time ~5 s/worker-process cost amortized across ~50 tests/worker. The dominant cost (~90%+ of
warm wall time) is **per-test user-program compile + regalloc + link**, paid every test, every run —
which the stdlib cache structurally cannot touch. Evidence: a cold full suite (108 s) was *faster*
than warm runs (95/100/139 s); run-to-run variance (~40 s) dwarfs the ~5 s stdlib budget.

**Refinement (2026-06-15): the repeated-spec-run speedup has TWO independent levers — compile and
run — and they are now both addressed.** The 2026-06-14 conclusion ("the big repeated-run speedup
requires Phase 5") was only half the story: it correctly identified per-test **compile + regalloc**
as the dominant cost and Phase 5's user-program Tier-C chunk cache as the lever for it — but it
treated the **subprocess run** cost as fixed. The de-batching + run-skip work (above) attacks the
*run* lever directly: a warm suite skips the subprocess run for binary-identical-to-a-prior-PASS
tests (1849 of 2220), independent of any compile caching. So the two stack — de-batch + run-skip took
the full host suite 61 s → 44 s on their own, and Phase 5's chunk cache cuts the remaining per-test
compile on top. Both share the same caveat: a **compiler rebuild** changes every fragment's binary,
so both caches mostly miss right after a compiler change (the common edit-compiler-then-test loop);
they pay off for re-runs against an *unchanged* compiler (CI, spec-edit, flaky investigation).

**Parallel clobbering — ✅ FIXED (read-modify-write merge).** Previously: concurrent spec workers
each re-serialized their whole in-memory snapshot via last-writer-wins `rename`, with no merge — so a
single `--workers=N` cold run converged to only **168–197** of the **270** functions a serial run
persists (27–38% lost), and the file size oscillated instead of plateauing. **Fix landed:**
`persistGrownStdlibCache` now does a read-modify-write *merge* under the atomic rename —
`mergeOnDiskGrownStdlibFunctions` re-reads the on-disk Tier-C cache and adopts every real-code
function we still hold as a signature-only stub (filling existing stub slots by name → stub-ordered
`.text` parity preserved), so the written file is the UNION of every concurrent/sequential writer's
lazily-grown bodies. This is what makes **cross-project sharing of grown stdlib generics**
*accumulate* rather than oscillate: verified end-to-end — an `Array<int>` build grows the shared
cache to 75 functions, a separate `Map<String,int>` build finds all 75 and grows to 95, and an
`Array<int>` rebuild then finds all 95 (zero recompile). Recovers little wall time on its own (the ~5%
ceiling above) but is the correctness backbone of the shared-cache convergence the user wanted, and
the same merge discipline Phase 5's user cache adopted from day one.

**ALL PHASES DONE (0–7).** Phase 7 (incremental Tier-C preservation across a stdlib edit) shipped with
a corrected, narrower scope than originally framed — v80 lazy codegen already delivered the regalloc-
skip; Phase 7 preserves unchanged functions' bytecode across an edit instead of discarding the whole
Tier-C cache. See the phase table + the Phase 7 section.

(Detailed Phase 3.5 design + file:line rationale: `library-idspace-design.md`.)

## Context

The self-hosted Maxon compiler hard-wires the standard library as a process-wide singleton: ~15
top-level `var`s in `Compiler/StdlibLoader.maxon` hold the cached stdlib module, its linkable
`StdlibCacheData` blob, its post-DCE StdModule snapshot, its DFE edge table, its typealias
sidecars, and its target-validity flags. The on-disk cache (`stdlib-<target>.mxc`) is written into
**the user project's** `.maxon/cache/` — rebuilt per-project, never shared.

We want a general **Library** concept: each library is compiled and cached separately. stdlib is a
library (special only because of `Internals.maxon` raw `__Internals.*` system ops + the
builtin-type bootstrap). The user program is a library. Additional directories can be specified as
libraries. Each library's cache lives **in the library's own directory** (`<libDir>/.maxon/cache/`,
with the compiler fingerprint + compile-options hash + target encoded in the filename — see *Cache
validity key*) so it is shared between projects, and the cache can be **incrementally updated**
(per-file/per-function), not just whole-library invalidated.

**User decisions:**
- **Import model: implicit** — a library's `export` (global-visibility) symbols become globally
  visible everywhere, exactly like stdlib today. NO new import/use syntax.
- **Discovery: both** a repeatable `--library=DIR` CLI flag AND a `build.maxon` `library("...")`
  manifest entry (alongside the existing `build("name")`).
- **Cache contents (three tiers):** each library's cache must hold (1) **metadata about the
  library** — the parser/type-system registries needed to name and type-check against it; (2)
  **Std-level IR suitable for inlining** — the post-DCE StdModule snapshot + its DFE edges; (3)
  **per-target bytecode suitable for linking** — relocatable per-function machine code (native) or
  wasm function objects, plus global data. Tiers (1) and (2) are target-independent; tier (3) is
  per-target. The format splits along this line so the target-independent tiers aren't duplicated
  across per-target files.
- **Caching applies to user code too.** The user program is a library with its own persistent Tier C
  cache. When a user function inlines a stdlib body, the inlined **Std IR comes from the cache**
  (Tier B) and — critically — the **final post-inline, register-allocated user function** is stored
  in the program's Tier C cache, so it isn't recompiled next build (validated by the function's own
  content hash AND the unchanged-ness of every body it inlined).
- **Performance goal:** avoid re-running **register allocation** (the most expensive pass) wherever
  possible — a cached Tier C function *is* its regalloc result, reused untouched on a hit.
- **Outcome:** stdlib + external library dirs are separately-compiled, separately-cached,
  incrementally-updatable units with per-library caches shared across projects; the user program is
  a project-private cached library with inline-aware reuse of fully-compiled functions.

## Why this fits the existing architecture

The linker is **already name-keyed**. `computeNeededStdlibFunctions` (`StdlibCache.maxon:1730`)
does a transitive closure over `StdlibCacheData.functions` (an `ObjectFunctionArray` of relocatable
per-function machine code) resolving `callFixups.targetName` / `adrFixups.targetName` — names are
global. The per-target backends (`linkX64StdlibFunctions*` in X64Backend, the arm64 equivalent in
Arm64CodeEmitter, the wasm one in MirToWasm) all iterate `stdlibCache.functions` resolving fixups
by name. So **"N libraries" is "iterate the union of N ObjectFunctionArrays and resolve fixups
across the union."** No fixup re-encoding. The cache **codec is path-independent**:
`writeStdlibCache(project, path, …)` / `readStdlibCache(project, path, …)` take the path as a
parameter — moving the cache location needs no format change.

Implicit import is **already satisfied** by the directory-as-module + visibility machinery: a
library's `export` symbol is `global`, so `isVisibleFrom(global, …)` is true for any reader once its
metadata is restored into the user project (the same restore stdlib uses). Each library only needs
to parse with **its own dir as namespace anchor** (`deriveNamespace(filePath, rootPath: libRoot)`).

## The fast cache-hit build path (and a non-goal)

Target scenario: stdlib fully cached from prior builds; a new program calls one function (say
`sha256`). The new program's build must **not recompile any stdlib** — it pulls only `sha256` + its
transitive callees' bytecode from the cache and links them in. The design delivers this:

- **Bytecode is demand-linked.** `computeNeededStdlibFunctions` (StdlibCache.maxon:1730) already
  closes over the program's call seeds by name and pulls only the reachable `ObjectFunction`s from
  the per-target tier (Tier C). A one-function program links one function's chain, not all of stdlib.
  Fast and proportional to usage.
- **Zero stdlib codegen on a cache hit — crucially, zero register allocation.** Regalloc is the
  most expensive pass in the pipeline, and a cached `ObjectFunction` *is* the post-regalloc,
  prologue/epilogue'd machine code (produced by `buildStdlibNativeWith` → `allocateRegisters` →
  `insertPrologueEpilogueWith` → `emitPerFunction`, StdlibLoader.maxon:880-901). Reusing it skips
  regalloc entirely for that function. **Caching Tier C is caching the regalloc result.**
- **Inlinable Std IR is pulled on demand** — the inliner only splices the accessor bodies it
  actually touches; the per-function Tier B store makes that naturally lazy.

**Non-goal: lazy metadata restore.** `restoreStdlibMetadata` (StdlibCache.maxon:2254) re-interns the
**whole** library's Tier A registries (funcReturnTypes / structTypes / signatures / conformances /
layoutDescriptors / witnessTables …) regardless of how much the program uses; those registries are
read at ~200 sites across 14 files (parser, TypeResolution, LowerMaxonToStd, SemanticCheck, …), so
making restore proportional would mean faulting per-symbol through all of them. **We deliberately do
NOT do this** — full metadata restore is registry re-interning with no codegen (milliseconds), an
acceptable fixed cost. The win that matters — eliminating stdlib *recompilation* on a cache hit — is
fully delivered without it. (If a future build ever shows metadata restore as a real bottleneck, the
tier split makes Tier A separately addressable, so lazy restore could be added then as its own
phase; it is out of scope here.)

## Concurrency & atomic writes (shared caches → concurrent writers)

Sharing a cache across projects introduces a hazard that doesn't exist today: **two independent
`maxon build` processes can race to write the same `.mxc`.** Today this is avoided only by accident
of architecture — the spec runner's parent **warms the cache once before spawning read-only workers**
(`warmStdlibCache` then fan-out, SpecTestRunner.maxon), so there is never a concurrent write. The
moment caches live in the shared library dir (Phase 2) and any build may be the one to compile a
library, that gate is gone: two builds of different projects against the same stdlib/external lib can
write the same file simultaneously.

Two facts from the audit shape the fix:
- **Reads are already crash-tolerant but not torn-write-tolerant.** `decodeStdlibCache` validates
  magic + version + checksum + arch and treats a short/garbage file as a **cache miss → rebuild**
  (StdlibCache.maxon:1046-1072), so a partial file never crashes a reader. But the current write is
  in-place `File.writeBinary` (StdlibCache.maxon:678) — no temp+rename — so a reader **can** observe
  a torn file (wasting a from-source rebuild) and two writers can clobber each other.
- **No atomic-rename / lock primitive exists** in the stdlib (File/Directory/FilePath expose none);
  file ops bottom out in `mrt_file_*` in `runtime.std`, lowered per-target.

**Decision: atomic write only (temp-file + atomic rename); no advisory lock.** Every cache write —
stdlib, external libs, and the user-program chunk cache — writes to a sibling temp file then
atomically renames it over the destination, so a reader sees **either** the old whole file **or** the
new whole file, never a torn one. Last-writer-wins. Worst case under a race: two processes both
rebuild the same library and one result harmlessly overwrites the other — **wasted work, never
corruption.** This is sufficient for correctness on every target. An advisory lock (to also avoid the
*redundant* concurrent rebuild) is explicitly **out of scope** — noted as a future optimization
alongside mmap and the record-store, to add only if redundant rebuilds prove a real cost. (WASI has
no file locks anyway; atomic rename via `path_rename` works there, so atomic-write-only is also the
only portable choice.)

**New primitive required (modeled on existing `mrt_file_*`).** Add `mrt_file_rename(src, dst)` to
`runtime.std`, lowered per-target: Windows `MoveFileExW(..., MOVEFILE_REPLACE_EXISTING |
MOVEFILE_WRITE_THROUGH)` (atomic replace), POSIX `rename(2)` (atomic), WASI `path_rename`. Expose it
as `File.rename`/`FilePath.renameTo`, and add a shared `writeFileAtomic(path, bytes)` helper
(write `<path>.tmp` → `mrt_file_rename` → best-effort delete temp on failure) that **all** cache
writers use. Temp name must be unique-enough to avoid two writers colliding on the temp itself (e.g.
include pid; a colliding temp just means one rebuild is wasted, still no corruption). This lands in
**Phase 2** (when the cache first moves to the shared dir and concurrent writers become possible);
later phases' writers (tier-split files, user chunk cache) all route through the same helper.

## Cache validity key (what must match to reuse a cache)

A cached file is safe to reuse only when every input that could change its bytes matches. Today's
header (`decodeStdlibCache`, StdlibCache.maxon:1075-1101) already checks **format version**, **source
checksum** (FNV over library files + runtime), **compiler fingerprint** (binary size + mtime), and
**target** (CPU + OS — today's file is named `stdlib-<target>.mxc`). So target and compiler are
**already keyed** — the gap is **compile options that change emitted code**.

**Add a single `compileOptionsHash` to the header**, checked on read alongside the existing four.
Folding all codegen-affecting options into one hash means a future flag is one line, not a format
bump. Today's contributors:
- **`coverageEnabled`** — coverage instrumentation changes emitted bytes. The *stdlib* cache already
  keeps coverage OUT by design (helpers/globals applied per-compile, StdlibLoader.maxon:784-785,849),
  so it's coverage-agnostic; but the **user-program Tier C chunk cache (Phase 5) stores
  fully-emitted user functions**, so it MUST key on the coverage flag — a `--coverage` build never
  reuses non-instrumented chunks and vice-versa (separate cached chunks per coverage state).
- **`--skip-stdlib-fn=NAME`** (`skippedStdlibFunctions*`) — drops functions from the stdlib cache
  build, so a cache built with a skip differs from one without; include the skip-set in the hash.
- **Future codegen-affecting flags** (opt level, debug info, …) — fold into the same hash as added.

Non-contributors stay out (they don't change output bytes): `--emit-ir`/`--dump-stages` (bypass the
cache entirely already), `--profile-passes`, `--workers`, `--log`. The hash covers **only** options
that change emitted code. Each library cache and the user-program chunk cache carry their own
`compileOptionsHash`; a mismatch is a cache miss → rebuild. (This formalizes spike A3's compiler
fingerprint and extends it to options.)

**Bake the fingerprint into the FILENAME, not just the header (required for coexistence).** A
header-only check makes reuse *correct* but causes two compiler versions (or two option-sets)
sharing a library dir to **stomp on each other**: both write `stdlib-x64-windows.mxc`, each reads the
other's, sees a header mismatch, treats it as a miss, and **overwrites it** — an endless ping-pong
where every build rebuilds. The fix is to make the fingerprint part of the filename so distinct
fingerprints map to distinct, coexisting files:
- Per-target tier: `<lib>-<target>-<compilerFp>-<optsHash>.mxc`
- Shared meta tier: `<lib>-meta-<compilerFp>-<optsHash>.mxc` (no target — preserves cross-target
  meta sharing *within* one compiler+options combo)

where `<compilerFp>` is a short stable hex token derived from `getCompilerFingerprint()` (hash of
size+mtime; add a `compilerFingerprintToken()` helper — the raw `(size, mtime)` tuple isn't
filename-shaped) and `<optsHash>` is the `compileOptionsHash`. The **header check stays** as
defense-in-depth (catches a corrupted file or an astronomically unlikely filename collision), but the
filename is now the primary coexistence key. Two compiler versions on one machine now keep separate
caches and never force each other to rebuild.

**Cleanup: installer offers it; build time NEVER auto-prunes by fingerprint.** Filename-keying means
mismatched caches are no longer overwritten, so files accumulate (one set per compiler × options
combo). It is **critical NOT to prune on write by "fingerprint ≠ current"** — that would re-create
the exact stomping the filename split prevents: two *concurrently-active* compilers (an old release +
a dev build, or two worktrees) would each delete the other's cache on every build. A different
fingerprint means *different*, not *stale*. So the **build never auto-prunes**: caches coexist and
accumulate (a few MB per combo — disk is cheap). Reclamation happens where version churn actually
occurs — **the installer offers to nuke old caches** on install/upgrade (the primary mechanism, with
user consent at the moment a new compiler version is introduced). The installer is a **separate
artifact, out of scope for this plan** (none exists in the repo today) — this plan only notes the
responsibility so the installer work picks it up. What this plan *does* build are the in-compiler /
tooling reclamation paths it can drive:
- **`maxon clean`** — a new command that nukes the caches for **every library this project uses**:
  it resolves the same library set a build would (stdlib via `findStdlibPath` + the project's
  `--library`/`build.maxon library(...)` entries) and deletes all `.mxc` (every fingerprint × options
  × target) + stray `.tmp` under each library's `.maxon/cache/`. *Nuance:* because libraries are
  shared, cleaning a shared lib's cache also affects other projects' next build — expected and
  acceptable for an explicit clean.
- The existing `noStdlibCache` and the MCP deleter (whose `stdlib-*.mxc` glob already matches the
  longer fingerprinted names — widen it to also sweep `*-meta-*.mxc` and stray `*.tmp`).

(Age/LRU-based GC at build time remains a deferred future option, but with the installer handling
upgrade-time churn and `maxon clean` covering project-level reclamation, it is likely unnecessary.)

## Invalidation model: what cascades, and why no persistent reverse graph is needed

Q: *is there a graph that cascades invalidations transitively?* The audit's answer shapes the whole
incremental design:

- **In-process there is a dependency graph** (`depIndex` + `recordDependency`/`areDepsValid`,
  QueryEngine.maxon) — but it is **pull-based** (a file change bumps a global revision; queries
  re-validate on demand) and **rebuilt every run, never persisted**. Per-function queries all
  converge on the whole-module `allMid`; there are **no persisted per-function call edges**.
- **`inlineEdges` is one level** (caller → directly-inlined callees + splice-time hash); it is **not
  persisted** today. `stdFuncEdges` (persisted) is a *forward reachability* table for DFE, **not** an
  invalidation graph and has no reverse edges.

**The cascade for inlining is nonetheless transitive — because the inliner is re-run and the chunk
keyHash is the POST-INLINE body hash.** `computeChunkKeyHash(F)` = `midPerFunc[F].keyHash`, which is
hashed *after* inlining, so an inlined callee's ops are physically in the caller's blocks at hash
time (Project.maxon:616-624). In-process, if deep callee C changes, the inliner re-runs → B's
post-inline body embeds new C → B's keyHash flips → A's post-inline body embeds new B → A's keyHash
flips. **Deep chains cascade for free; no reverse graph required.** The catch is purely a *persistent*
one: a chunk loaded from disk *before* the inliner re-runs can't have its post-inline hash
recomputed — which is exactly why `inlineEdges` + `callerInlineDepsUnchanged` exist (validate a
disk-loaded chunk against stored per-callee hashes). **So the incremental driver MUST re-run the
inliner each build** (cheap vs. codegen); once it has, `midPerFunc[*].keyHash` is post-inline-correct
at every level and the one-level stored edges + per-function keyHash give correct transitive
invalidation without persisting a reverse graph.

**The genuine gap: non-inlined call edges + signature changes.** If B is *called* (not inlined) by A
and B's **signature** changes, A's post-inline hash does NOT change (the call op looks the same), so
the keyHash/inline-edge machinery won't invalidate A. Today this is masked by whole-module recompile
on any revision bump. Across persistent **per-library** caches that is no longer automatic: a
signature change to a library function must invalidate callers in *other* libraries / the user
program. **Decision: a library's whole-cache validity key already covers its own signature changes**
(its source checksum / per-file checksums change → the library rebuilds). What must be added is the
**downstream** edge: a *consumer* (user program or dependent library) must record the **signatures it
depends on** from each library it links — concretely, fold the depended-on library functions'
`funcSignatures`/`funcReturnTypes`/`funcThrowsTypes` hashes into the consumer's `compileOptionsHash`-
style validity key (call it the **upstream-API hash**), so a library API change invalidates the
consumer's chunk cache. Body-only changes to *non-inlined* callees do NOT require consumer
invalidation (the call is by name, resolved at link time against the rebuilt library's bytecode —
A1's name-keyed linker). This keeps the model graph-free: each consumer validates against (its own
sources) + (the API hashes of the libraries it depends on), and inlining cascades via the re-run
inliner + post-inline keyHash. **Settle the exact upstream-API-hash contents in Phase 5** (the user
program is the first real downstream consumer; external lib→lib deps are out of scope until
libraries can depend on each other).

## Cache format: three tiers

Today `StdlibCacheData` (`StdlibCache.maxon:304-488`) is one flat blob written to a **single
per-target file** (`stdlib-x64-windows.mxc`, `stdlib-wasm32-wasi.mxc`). Each per-target file carries
a full copy of the target-independent state, so the metadata + inlinable Std IR are duplicated
across every target's `.mxc`. The library cache must instead split along three tiers (verified
field-by-field against `StdlibCacheData`):

- **Tier A — Library metadata (target-independent).** Everything the user project needs to *name*
  and *type-check against* the library: `typealiases` (+ `typealiasVisibilities` / `typealiasRanges`
  / `typealiasCastCategories`), `funcReturnTypes`, `funcSignatures`, `funcThrowsTypes`,
  `structTypes`, `enumTypes`, `topLevelConstants`, `typeNameTable`,
  `builtin{Array,Dictionary}LiteralTypeName`, `interfaces`, `conformances`, `typeParameters`,
  `interfaceRegistry`, `functionTypes`, `genericInstances`, `genericTypealiases`,
  `layoutDescriptors`, `witnessTables`, `livenessRoots`, `moduleInitFuncs`, `initToStoredVars`,
  `topLevelVars`, `genericFunctionImplicitArity`, `staticRdataValues`, `unresolvedExtensions`,
  `extensionMethodWhereClauses`. (`writerGidToProjectGid` is in-memory only — recomputed on decode,
  never serialized — **but Phase 3.5 deletes it**: under the library-owned id-space, order-faithful
  replay makes the consumer's gid == the cache's gid by construction, so there is nothing to translate.
  Phase 3.5 also promotes `typeNameTable` / `constantStringTable` from in-memory rebuild to
  **serialized authoritative** id-space tables and serializes `genericInstances` as an ordered flat
  list — see that phase for why Tier-A must become self-contained so any consumer can persist.)
- **Tier B — Inlinable Std IR (target-independent), stored per-function.** Today this is the
  monolithic `inlinerStdModule` (post-DCE StdModule snapshot the user inliner splices accessor
  bodies from) + `stdFuncEdges` (precomputed Std-DFE edges over it). For incremental generation
  (see below) Tier B is stored as **per-function Std IR bodies** keyed by name + content hash, plus
  a thin **DCE-membership + edge layer** recomputed over the union. This is feasible because the
  entire Std-tier pipeline (`lowerMaxonToStd`, `injectDrops`, `mem2reg`, `canonicalize`, `cse`,
  `licm`, `lowerABI`) is already classified `perFunction` and runs intra-function via
  `runStdPassPerFunction` (PassPipeline.maxon:592,626-631) — so an unchanged function's Std IR body
  is invariant under edits to other functions. DCE only changes *which* functions survive (module
  membership), not surviving bodies; the snapshot reassembled from the per-function store + the
  recomputed membership is identical to today's monolithic snapshot.
- **Tier C — Linkable bytecode (per-target).** `functions` (native `ObjectFunctionArray`,
  relocatable, name-keyed fixups) **or** `wasmFunctions` (`WasmObjectFunctionArray`), plus
  `globalData`.

**Layout.** Per library dir: a meta file (Tier A + B) and a per-target file (Tier C); both filenames
carry the target + compiler-fingerprint for coexistence (`<lib>-meta-<target>-<fp>.mxc` and
`<lib>-<target>-<fp>.mxc`; the `<optsHash>` component is deferred to Phase 5). The codec already takes
the path as a parameter, so this is a split of the existing serializers (`encodeStdlibCacheBody` →
`encodeLibraryMeta` + `encodeLibraryTarget` bodies + a shared header), not a rewrite. In memory the
loader fills one `StdlibCacheData` from both files (meta first → Tier A+B, then target → Tier C) so the
Phase-4 combined accessors and the existing linkers are unchanged.

**Target-independence was a goal to verify — and it turned out FALSE (resolved Phase 3).** The hope
was that Tier A/B, produced before the `isWasm` split, would be byte-identical across targets so ONE
shared meta file could serve all. A target-independence guard (recompute Tier A/B for a second target,
compare content-hash) proved otherwise: **x64 ≠ wasm32**, because `buildLayoutDescriptors` /
`structTypes` byte sizes bake in the **pointer width** (wasm32 = 4 bytes, x64/arm64 = 8). This is
correct and fundamental, so Phase 3 keys the meta file **per-target** (`<lib>-meta-<target>-<fp>.mxc`)
— the plan's documented fallback. The per-target meta IS deterministic across rebuilds of the same
target (verified), so each target's meta+target pair is stably reusable; only cross-target *dedup* of
the meta is lost (it was never legitimate). The guard is dropped (a per-target meta is only ever
compared against itself).

## Scope: C# bootstrap (maxon-sharp)

**Out of scope** for every phase except one optional Phase-6 sub-step. The per-function relocatable
`.mxc` cache (`CACHE_FORMAT_VERSION = 69`, `StdlibCacheData`, name-keyed `callFixups`) is
self-hosted-only; `maxon-sharp/BuildCache.cs` is a coarse whole-build mtime manifest with no
per-function caching and no `.mxc` format. The "mirror target-specific changes" rule applies to
language/target semantics both compilers emit — not to this cache format. The only possible parity
point is teaching the C# `build.maxon` parser to honor `library("...")` as extra source roots (no
caching); deferrable. Self-hosted is the primary target and the spec-green gate.

## Cross-cutting conventions

- **Library name** = basename of the library root dir, except stdlib which stays literal `stdlib`
  (so cache files keep the `stdlib-…` prefix and the MCP deleter's `stdlib-*.mxc` glob keeps matching
  the longer fingerprinted names until generalized in Phase 6).
- **Two distinct anchors on a Library**: `rootPath` (namespace anchor for `deriveNamespace`) and
  `cacheDirRoot` (where `.maxon/cache/` lives). For stdlib they differ — `rootPath =
  findStdlibProjectRoot()` (parent of `stdlib/`, giving namespaces `stdlib` /
  `stdlib.helpers.string`), `cacheDirRoot = findStdlibPath()` (the `stdlib/` dir itself). For
  external libs they coincide.
- **Byte-parity gate** for any "no behavior change" phase: `build` both targets; `run_spec_test`
  selfhosted (full green); a `noStdlibCache` from-source rebuild; a `wasm32-wasi` cross-target
  build; a cache-on vs cache-off byte-diff of representative exes.

## Verified facts grounding the design

- Stdlib singletons + loader in `StdlibLoader.maxon`: `getStdlibModule` (~474, warm /
  cache-hit / from-source paths), cache path computed at 567-576 from the **user project dir**,
  re-derived in `loadCacheForTarget` (979-982), pinned sticky in `stdlibCacheProjectDirs`;
  `buildAndWriteStdlibCache` (713, full pipeline flagged `isStdlib: true`); discovery
  `findStdlibPath` (337) / `findStdlibProjectRoot` (353) / `collectStdlibFiles` (367); checksum
  `currentStdlibChecksum` (433, FNV over files + runtime.std + runtime_wasm.std); accessors
  `getStdlibCacheData` (1021), `activateStdlibCache` (1025), `isStdlibCacheValid` (1033),
  `getCachedStdlibStdModule` (38), `cachedStdFuncEdges` (52); `resetStdlibLoaderState` (318).
- Cache blob + codec in `StdlibCache.maxon`: `StdlibCacheData` (304+), `ObjectFunction` (221,
  name-keyed `callFixups`/`adrFixups`/`rdataRelocs`), `CACHE_FORMAT_VERSION = 69`, magic `MXC\0`,
  FNV checksum + compiler fingerprint, `writeStdlibCache`/`readStdlibCache` (661/1017, path
  parameterized), `computeNeededStdlibFunctions` (1730), `computeStdlibChecksum` (~524),
  `mergeStdlibGlobalData` (1788).
- Build driver: Main.maxon → Compiler.maxon `compile` (4) → `compileProject` (274) →
  `loadSourceFiles` (326) + `queryCodeResult` → `writeExecutable(…, stdlibCache:
  getStdlibCacheData())` (BackendDispatch.maxon:1651). `augmentWithRuntime` (404) merges runtime +
  user module + cached stdlib, filtering by stdlib namespace; the `cacheHasRuntimeBodies` gate is
  `isStdlibCacheValid()` (415).
- In-process per-function chunk cache (reused in Phase 7): `db.codePerFunc`,
  `computeChunkKeyHash`, `lookupCachedChunkByKeyHash`, `prepareTargetModuleForFunctions`
  (BackendDispatch.maxon:1386-1506); inline-aware invalidation `project.inlineEdges`,
  `recordInlineEdge`, `captureInlineDeps`, `callerInlineDepsUnchanged` (Queries.maxon:768-808).
- Namespace/visibility: `deriveNamespace` (Queries.maxon ~73), `isVisibleFrom` (Project.maxon ~83).
- Manifest/CLI: `parseBuildName(projectRoot)` (Compiler.maxon:306, string-scans `build("`),
  `ProjectManager.maxon` build.maxon discovery; `MaxonArgs.maxon` flag scanner (120-174).
- Internals gating (already library-aware): `readerCanCallStdlibInternals`
  (`MaxonDialect.maxon:1966`), Parser carve-out (`Parser.maxon:551-559`, checks `namespace ==
  "stdlib"` AND filename).
- Builtin bootstrap registrations in `IR/Maxon/LowerMaxonToStd.maxon`; the block is duplicated at
  `getStdlibModule` (483-518) and `ensureStdlibSandboxParsed` (96-114). The `__mm`/`__Managed*`/
  `__Builtins` set is the language runtime surface (every compile); `registerInternalsIntrinsicSignatures`
  is stdlib-only.
- Build tooling assuming old location: `maxon-dev-mcp/mcp/BuildTool.maxon:115-152`
  `deleteStdlibCacheFiles` scans `maxon-selfhosted/.maxon/cache`, `isStdlibCacheFile` matches
  `stdlib-*.mxc`; `Server.maxon:225` schema text; `.claude/CLAUDE.md:27`; `.gitignore`
  `.maxon/cache/`.
- Singleton-accessor consumers (must keep working via delegation): Queries.maxon, Compiler.maxon,
  PassPipeline.maxon, BackendDispatch.maxon, StdDeadFunctionElimination.maxon,
  MonomorphizeExtensions.maxon, SpecTestRunner.maxon, Main.maxon.

## Testing strategy

Two existing harnesses cover the test *shapes* this feature needs, but both are **in-process /
in-memory only** today:
- **`IncrementalTestRunner`** (`maxon test-incremental`, Scenarios A-I): drives a `Project` through
  edit sequences and asserts on **query counters** (`db.codePerFuncHits/Misses`, `db.codePerFunc`
  chunk keyHashes) and **byte-parity** between incremental and full rebuilds. Scenario I already
  tests inline-aware invalidation (editing an inlined callee invalidates the caller's chunk).
- **Spec `<!-- CacheParity -->` directive** (`specs/cache-parity.md`, `runTestParityCheck`):
  double-compiles each test cache-on vs cache-off (via `setStdlibCacheBypassed`) and asserts
  byte-identical `.text`/`.rdata`/IR. This is the *correctness* guard (cache must never change
  output).

The new capabilities — **persistent on-disk per-library caches, the 3-tier split, and cross-process
reuse** — are not exercised by either (every current test runs in one process, in-memory). Per the
decision, build a **dedicated library-cache test harness** rather than overload the spec format.
Cross-process reuse is the **core promise** and gets first-class coverage.

**New harness: `maxon test-libcache`** (Phase 0 below). Add `Command.testLibCache` to MaxonArgs +
a `runLibCacheTests()` in a new `Testing/LibCacheTestRunner.maxon`. It reuses the proven primitives
(`Subprocess.run(Executable.path(...))` already used by IncrementalTestRunner:933,1051; counter
assertions; byte-diff of emitted exes) and adds the missing one: **true process isolation**. Each
scenario drives **real `maxon build` subprocesses** against fixture library dirs in a temp area,
exits, rebuilds in a fresh subprocess, and asserts the on-disk cache was reused.

**Reporting cache hits across the process boundary.** The second build's counters live in *its*
memory, so `maxon build` gains a `--cache-stats` flag that emits one machine-readable stdout line
(e.g. `CACHE_STATS regalloc_funcs=1 chunk_hits=99 chunk_misses=1 lib_meta=hit lib_target=hit`).
The harness parses that line — the same stdout-marker pattern the parallel spec runner already uses
(`WORKER_READY`). A regalloc-funcs count of ~0 on an unchanged rebuild is the direct, observable
proof that "regalloc was skipped." (`--profile-passes`/`dumpPassTimings` is the human-facing
fallback.)

Each phase below lists the scenarios it contributes to this harness, **plus** the existing-harness
coverage it should also get (a new IncrementalTestRunner scenario and/or a `cache-parity.md` case)
so in-process correctness stays gated by the fast suite.

## Comprehensive TDD test list (write these tests first, in order)

Ordered trivial → complex. Each test names the **harness** it lives in, what it **asserts** (using
the existing `assertCounter(label, actual:, expected:)` style, `CACHE_STATS` stdout parsing, or
byte-diff), and the **phase** it gates. Write the test → watch it fail → implement → green. Harness
key: **[IP]** = in-process `IncrementalTestRunner` (counters + byte-parity, fast); **[XP]** =
cross-process `maxon test-libcache` (real subprocess builds, `CACHE_STATS`, on-disk file assertions);
**[SP]** = spec `cache-parity.md` (cache-on-vs-off byte-identity). Many [XP] tests reuse the
synthetic-source builders (`buildSyntheticSource`, `inlineScenarioSource`) and `Subprocess.run`.

**Group 0 — harness self-validation (Phase 0, against the UNMODIFIED compiler)**
1. [XP] cold build writes a stdlib cache file; the file exists afterward.
2. [XP] second subprocess build of the same project = cache hit (`CACHE_STATS` stdlib reused; low
   `regalloc_funcs`); exe byte-identical to the first.
3. [XP] delete the cache file → next build is a cold rebuild (cache re-created, exe identical).
4. [XP] `--cache-stats` emits exactly one well-formed `CACHE_STATS` line parseable by the harness.

**Group 1 — Library refactor is behavior-preserving (Phase 1)**
5. [IP] full spec suite still green (the `Library` indirection changed nothing observable).
6. [XP] byte-diff hello-world + a generics-heavy exe vs the pre-Phase-1 binary → identical.
7. [XP] **determinism (spike A2)**: build the stdlib cache twice → byte-identical `.mxc` (after the
   `collectLibraryFiles` sort).

**Group 2 — cache location, coexistence, atomicity (Phase 2)**
8. [XP] cache now lives under `<stdlibDir>/.maxon/cache/`, not the user project dir.
9. [XP] **shared cache**: build project A, then project B in a different dir → B is a cache hit.
10. [XP] `mrt_file_rename` works on x64 and wasm (round-trip a temp file).
11. [XP] **atomic write**: kill a build mid-write (or inject a failure before rename) → the existing
    cache file is intact (old whole file), no torn `.mxc`, no leftover `.tmp` consumed as cache.
12. [XP] **concurrent writers**: two subprocess builds race against an empty shared cache → both
    succeed, final file is valid, no stray `.tmp`.
13. [XP] **coexistence (the critical one)**: build with fingerprint A; simulate fingerprint B (touch
    binary mtime / stub token) and build → **two** cache files coexist; build with A again → still a
    **HIT** (B did not delete A's cache).
14. [XP] `maxon clean` removes the project's stdlib cache files (all fp/target/`.tmp`).
15. [XP] `noStdlibCache` deletes the matching-fp file and rebuilds from source.

**Group 3 — three-tier split (Phase 3)**
16. [XP] file layout is one meta file + one per-target file (assert presence/absence by name).
17. [SP] `cache-parity.md` still green — byte-identity preserved across the split.
18. [XP] byte-diff exes vs Phase-2 binaries → identical (split is storage-only).
19. [XP] **cross-target meta sharing (spike A4)**: build x64 then wasm in two subprocesses → one
    shared meta file (meta-hash match), each writes only its own target file.
20. [XP] corrupt/delete the meta file → every dependent target file invalidates → rebuild.
21. [IP] reassembled-from-cache Std IR (Tier B) hashes equal to a from-scratch monolithic snapshot
    (round-trip fidelity of the per-function Tier B store).

**Group 4 — N-library link generalization (Phase 4)**
22. [IP/XP] combined-of-one (registry = stdlib only) == stdlib-only → byte-identical exe (proves the
    union machinery is a no-op at N=1).

**Group 5 — external library discovery (Phase 6)**
23. [XP] **new-library acceptance**: a fixture lib with one exported fn, used via `--library=DIR` →
    build+run succeed; the lib's `-meta-…` + `-<target>-…` cache files are produced.
24. [XP] same via `build.maxon library("../foo")` manifest entry.
25. [XP] **implicit import**: the user program calls the lib's exported fn with no import syntax; a
    `module`/`file`-scoped lib decl is NOT visible (compile error).
26. [XP] **cross-project lib share**: two programs vs the same external lib → second build hits the
    lib cache (`CACHE_STATS lib_*=hit`).
27. [XP] external-lib program builds for wasm32-wasi.
28. [XP] **negative**: external lib calling `__Internals.*` is rejected (Internals stays stdlib-only).
29. [XP] **negative**: two libs with the same basename → duplicate-namespace diagnostic.
30. [XP] `maxon clean` now nukes stdlib + every external lib the project uses.

**Group 6 — incremental library rebuild (Phase 7)**
31. [IP] edit one library leaf function → `codePerFuncMisses == 1`, post-edit chunk keyHash differs
    (mirrors Scenario I over a library cache).
32. [XP] **reuse + regalloc-skip**: edit one leaf fn → `CACHE_STATS regalloc_funcs=1` (only that fn
    re-allocated); all others reused.
33. [XP] **incremental == full parity**: warm cache, edit one file, incremental-update; then a
    `noStdlibCache` full rebuild → both `.mxc` payloads AND exe byte-identical.
34. [IP/XP] **one-level inline-dep**: edit a fn directly inlined into another → the caller recompiles.
35. [IP/XP] **DEEP inline chain** (A inlines B inlines C): edit C → B recompiles AND A recompiles
    (transitive cascade via the re-run inliner's post-inline keyHash — the load-bearing case).
36. [XP] add a new file / new function → only the new functions compile; existing ones reused.
37. [XP] remove a function/file → its cache entry drops; dependents that referenced it behave
    correctly (recompile or error).
38. [XP] **meta↔target coupling**: edit one function → only that function's target-tier entry +
    meta-tier Tier B change; OTHER targets' Tier C remain valid (no all-targets invalidation — the
    Phase-7 design point).
39. [XP] wasm incremental update: reused `wasmFunctions` keep their type/funcAddr/globalAddr fixups.

**Group 7 — user program as a library, inline-aware persistent reuse (Phase 5)**
40. [IP] program where `foo` inlines stdlib `Array.get`; rebuild unchanged → `foo` chunk hit; edit
    `Array.get` → `foo` invalidates (extends Scenario I).
41. [XP] **inline-reuse across processes (the core promise)**: build, exit, rebuild fresh subprocess
    with no source change → `CACHE_STATS regalloc_funcs=0` for user fns; persisted `foo` reused; exe
    identical.
42. [XP] **inline-invalidation across processes**: edit `Array.get` body, rebuild stdlib incrementally,
    rebuild program in fresh subprocess → `foo` is a miss; exe reflects new `Array.get`.
43. [XP] **warm == cold parity (cross-process)**: warm rebuild byte-identical to from-scratch cold.
44. [XP] **signature-change invalidation (upstream-API gap)**: change the *signature* of a
    non-inlined library fn `foo` calls → `foo` is a miss (upstream-API hash changed); the exe links
    the new signature.
45. [XP] **body-only of non-inlined callee → still a HIT**: change only the *body* of that
    non-inlined callee → `foo`'s chunk is reused (relinked by name); exe reflects the new body.
46. [XP] **coverage keying**: build non-coverage, then `--coverage` → coverage build is a chunk miss
    (instrumented); switching back hits the non-coverage chunks (separate keyed sets coexist).

**Group 8 — edge cases & adversarial inputs (write alongside the phase each touches)**

*Corruption / partial state (Phase 2-3):*
51. [XP] zero-byte `.mxc` → treated as miss, rebuilt (not a crash).
52. [XP] truncated file (header OK, body cut mid-function) → miss + rebuild (decode's length-prefix
    walk must not panic — see StdlibCache.maxon `readString` OOB).
53. [XP] file with a **future** `CACHE_FORMAT_VERSION` (newer compiler wrote it) → older compiler
    rejects via version check, rebuilds its own.
54. [XP] valid meta file but **truncated/corrupt target** file (and vice-versa) → only the bad tier
    rebuilds; the good tier is reused.
55. [XP] **stale meta-hash**: target file references a meta-hash that no longer matches the present
    meta file (meta replaced underneath it) → target invalidates, not a wrong-reuse.
56. [XP] a leftover `<lib>-…tmp` from a crashed build is NOT mistaken for a cache file (ignored by
    the loader, swept by the deleter).
57. [XP] an unrelated/garbage `.mxc` (or another library's file) dropped in the dir → ignored, not
    loaded as this library's cache.

*Fingerprint / key degradation (Phase 2) — guards a real correctness hole:*
58. [XP] **degraded fingerprint**: `getCompilerFingerprint` returns `(0,0)` on failure
    (`Process.executablePath`/`File.info` fail). Today that would make ALL compilers share one cache
    → silent wrong-reuse across versions. Test: a `(0,0)` token must **disable caching** (treat as
    no-cache) rather than collapse all fingerprints to one shared file.
59. [XP] **checksum file-read failure**: a library file readable at build time but unreadable at
    validate time — `computeStdlibChecksum`'s `otherwise continue` silently skips it, changing the
    checksum. Assert this produces a miss (rebuild), never a stale hit on a different file set.
60. [XP] **checksum/parse order agreement**: the deterministic sort (spike A2) is applied to BOTH the
    checksum input and the parse input — a sorted-parse + unsorted-checksum mismatch must be
    impossible (same `collectLibraryFiles` feeds both).
61. [XP] **options-hash order independence**: the same options given in a different flag order produce
    the same `compileOptionsHash` (so `--coverage --skip-stdlib-fn=x` == `--skip-stdlib-fn=x
    --coverage`); and presence-vs-absence of runtime.std is reflected in the key.

*Filesystem realities (Phase 2) — graceful degradation, not correctness:*
62. [XP] **read-only / unwritable cache dir** → build still succeeds (compiles from source), just
    doesn't persist a cache; no crash, clear (debug-level) log.
63. [XP] cache dir does not exist yet → created on first write.
64. [XP] **disk-full mid-write** (simulate write failure before rename) → existing cache intact (old
    whole file), build still completes from the in-memory result.
65. [XP] library path containing spaces / unicode → cache files written & reused correctly.

*Lifecycle / cross-target (Phase 3, 6):*
66. [XP] **transitive-input change**: edit `runtime.std` (not a library source file) → stdlib cache
    invalidates (checksum folds runtime in).
67. [XP] meta file present, ALL target files deleted → next build re-emits only Tier C, reuses meta.
68. [XP] build target X cold, then target Y → Y reuses X's meta (spike A4), emits only Y's Tier C;
    then re-build X → still a hit (Y didn't disturb X's target file).

*Removal / rename / churn (Phase 7):*
69. [XP] **function renamed** (same body, new name) → old name's cache entry is dropped (not
    resurrected by a stale linker reference); new name compiles fresh.
70. [XP] **edit-then-revert**: edit a function, build; revert it to the exact original, build → the
    revert is a HIT against the still-present original cache entry (content-hash returns to prior
    value), zero recompile.
71. [XP] **two functions swap names** → both recompile correctly, no cross-wiring of cached bytes.
72. [XP] a function becomes unreachable (DCE drops it) then reachable again across edits → correct
    membership in the reassembled snapshot each time.

*Concurrency vs. clean (Phase 2):*
73. [XP] `maxon clean` racing a concurrent build of the same project → no crash; the build either
    rebuilds cleanly or completes from its in-memory result (clean may delete a file mid-build, which
    just degrades to a miss next time).

**Cross-cutting regression gates (run every phase)**
74. [IP] full self-hosted spec suite green vs baseline count.
75. [SP] `cache-parity.md` green.
76. [XP] `maxon test-libcache` green.
77. [XP] wasm32-wasi spec outcome unchanged.

This list is the TDD backlog: Group 0 must pass on the unmodified compiler before Phase 1; each
later group is written before (and gates) its phase; Group 8 edge cases are written alongside the
phase each touches (the tag notes which). **Highest-value correctness guards** — write these
carefully: deep-inline-chain (#35), coexistence (#13), atomic-write race (#11/#12),
signature-vs-body (#44/#45), the **degraded-`(0,0)`-fingerprint hole** (#58, a real silent
wrong-reuse risk that must disable caching rather than collapse all versions to one file), stale
meta-hash (#55), and edit-then-revert (#70). The graceful-degradation edges (#62-#65, #73) assert the
build never *crashes* on a hostile filesystem — it falls back to compiling from source. Several edge
tests need small harness affordances (inject a write failure, stub the fingerprint token, drop a
corrupt/garbage file) — build those fault-injection hooks into `LibCacheTestRunner` in Phase 0.

## Pre-implementation verification (de-risking spikes)

Five load-bearing assumptions were audited against the current code before committing to the plan.
Three are VERIFIED from code alone; one needs a cheap runtime spike; one is a **blocker** that
reshapes Phase 3. Do the spikes FIRST — each is hours, not days, and a bad result changes the plan.

- **A1 — Cross-project bytecode portability: VERIFIED.** Cached `ObjectFunction` code is
  RIP-relative / name-fixup relocatable; the per-target linkers (`linkX64StdlibFunctions`
  X64Backend.maxon:203-251; arm64 + wasm equivalents) rebase **every** `callFixup`/`rdataReloc`/
  `relocation`/`adrFixup` to the linked position and resolve by name. No absolute addresses are baked
  into `code`. **This is the core "reuse stdlib bytecode between projects" claim — it holds.**
  *Operational requirement (already in the restore path):* the user build must call
  `registerGlobalVarsForDataSection` on the cache's persisted `topLevelVars` in the same order, or
  stdlib `.data` slot offsets shift and rdata fixups resolve wrong (StdlibCache.maxon:395-401). The
  per-library restore loop must preserve this.
- **A3 — Compiler-fingerprint invalidation: VERIFIED.** `getCompilerFingerprint`
  (StdlibCache.maxon:544) = (binary size, mtime), persisted in the header and rejected on mismatch
  (decode ~1091). A rebuilt compiler invalidates shared caches. Adequate (not cryptographic) — fine
  for dev/CI. Cross-project sharing is therefore safe across compiler versions.
- **A5 — Per-function regalloc independence: VERIFIED.** `allocateRegistersForFunc` builds a fresh
  per-function `FunctionRegAllocator` owning only that function's CFG/ranges/coloring/spill state
  (RegisterAllocator.maxon:625-702); no module-level register pool or shared spill arena. Reusing a
  neighbor's cached allocation while one function re-allocates is sound — the basis of Phases 6/7.
- **A2 — Cache-build determinism: NEEDS A SPIKE (medium risk).** No `random`/`Clock`/`Date` in the
  build path, and Maps are insertion-ordered — BUT `collectMaxonFilesUnder` (StdlibLoader.maxon:384)
  consumes `Directory.list` **with no sort**, so parse order (→ interner ids → map order → byte
  layout) depends on filesystem enumeration order. **Spike:** build the stdlib cache twice (and on a
  second checkout/path) and byte-diff the `.mxc`; if they differ, add a deterministic sort to
  `collectMaxonFilesUnder` (cheap insurance the cross-project byte-parity gates depend on). Recommend
  adding the sort regardless.
- **A4 — Target-independent Std IR snapshot: REFUTED — reshapes Phase 3 (high risk).** Phase 3 wants
  to store the post-DCE Std IR snapshot (Tier B) ONCE shared across targets, but it is captured
  **after `lowerABI`** (StdlibLoader.maxon:774), and `lowerABI` dispatches on `target.cpu`
  (LowerABI.maxon:40-46). Today `lowerABI` is effectively an identity pass, so the snapshots may
  *happen* to match across targets — but the capture point is structurally target-dependent and will
  diverge the moment real ABI lowering lands. **Fix + spike:** move the snapshot capture to
  immediately after `dce` (before `lowerABI`), then build for x64 and wasm and assert the
  reassembled Tier A+B content hashes are equal. If they still differ, some other pre-snapshot pass
  branches on target → Phase 3 must keep a per-target meta tier (the format already supports that
  fallback; only the dedup win is lost). **This must be resolved before Phase 3 is designed in
  detail.**

These spikes are the literal answer to "what should we verify before diving in." A1/A3/A5 are
settled; A2 and A4 get a runtime check (Phase 0 is a natural place to host them, since the harness
can build twice + diff and build cross-target + compare).

---

## Phase 0 — Library-cache test harness (`maxon test-lib-cache`) + `--cache-stats` ✅ DONE

**Goal.** Stand up the harness and the cross-process observability so each phase lands with executable
coverage. (Built *after* the Phase 1–3.5 refactor rather than before, so its scenarios assert the
now-real shared-cache / fingerprint-coexistence / persist behaviours directly — and immediately
earned their keep by catching two corruption-handling defects; see *Implementation status*.)

**What shipped.**
- **Command + dispatch.** `Command.testLibCache` (`"test-lib-cache"`) in `Compiler/MaxonArgs.maxon`;
  `Main.maxon` dispatches `testLibCache gives runLibCacheTests()`.
- **`--cache-stats`** (`Project.cacheStatsEnabled`): `compileProject` emits
  `CACHE_STATS regalloc_hits=… regalloc_misses=… stdlib_funcs=… stdlib_valid=…` from
  `db.codePerFuncHits/Misses` + `isStdlibCacheValid()` + `countCompiledStdlibFunctions(...)`.
- **Per-scenario isolation via `--cache-root=DIR`** (`Project.cacheRootOverride`, consulted in
  `libraryCachePath`): redirects a build's library cache to a throwaway temp dir so a scenario gets a
  known cold/hit/delete state WITHOUT disturbing the persistent shared cache the spec runner relies on
  for speed. **Per-`Project`, not a process global** — the persistent spec workers reuse one process
  across many jobs, so a global would bleed a stale override into a worker's real spec builds.
- **`Testing/LibCacheTestRunner.maxon`** (single file; no separate `LibCacheFixtures`): spawns the
  running compiler (`Process.executablePath()`) as a subprocess per scenario, parses the `CACHE_STATS`
  line + exit code, and reconstructs the exact `.mxc` filename (`stdlib-<target>-<fp>.mxc`) from
  `compilerFingerprintToken()` to inspect/delete/corrupt the precise file a build reads.
- **9 scenarios:** cold-writes-cache, second-build-is-hit, delete-forces-rebuild,
  distinct-roots-coexist, and 5 fault-injection cases (garbage / truncated / zeroed / mid-body-garbage
  / stray-tmp), each asserting the build recovers with a clean cold rebuild (exit 0, no crash) and the
  cache self-heals on the next build.

**Deliberately deferred fault hooks.** The read-only-dir and write-failure-before-rename injections
from the original sketch aren't implemented — they exercise OS-error paths (best-effort log + skip)
rather than the cache *codec*, which is where the real risk lives; revisit if a write-path bug ever
surfaces. The fingerprint-stub case is implicitly covered: distinct fingerprints already coexist as
distinct files (confirmed on disk), which is the property a version skew relies on.

**Verify.** `maxon test-lib-cache` → **9/9 green**; full spec suite host **2220/2220** + wasm
**2206/2206**, no leaks.

---

## Phase 1 — `Library` value type + keyed registry; stdlib as the first Library

**Goal.** Collapse the ~15 stdlib singletons into one `Library` value type + a keyed registry,
stdlib registered under key `"stdlib"`. Every exported accessor keeps its signature and delegates
to the stdlib Library. Extract the builtin-type bootstrap into one reusable function. No cache-path
move, no format change. **Byte-identical output.**

**Files.** New `Compiler/Library.maxon`; modify `Compiler/StdlibLoader.maxon` (bulk);
`IR/Maxon/LowerMaxonToStd.maxon` (de-dup the bootstrap block). No call-site changes elsewhere.

**New type (`Compiler/Library.maxon`).**

```
export type Library
	export var name as String              // "stdlib" or libRoot basename
	export var rootPath as FilePath        // namespace anchor for deriveNamespace
	export var cacheDirRoot as FilePath    // where .maxon/cache/ lives (Phase 2 uses it)
	export var isStdlib as bool            // gates Internals + runtime-splice specialness

	// warm/parsed state (was cachedStdlibModule, stdlibLoaded, stdlibParsedModuleArr, sidecars)
	export var loaded as bool
	export var cachedModule as MaxonModule
	export var parsedSnapshot as StdlibParseSnapshotArray
	export var typealiases as TypealiasMap
	export var typealiasVisibilities as TypealiasVisibilityMap
	export var typealiasRanges as TypealiasRangeMap
	export var typealiasCastCategories as TypealiasCastCategoryMap

	// compiled cache + linker payload (was cachedStdlibCacheData, stdlibCacheValid,
	//   stdlibCache{Cpu,Os}Ord, cachedStdlibStdModule, cachedStdlibFuncEdges)
	export var cacheData as StdlibCacheData
	export var cacheValid as bool
	export var cacheTargetSet as bool
	export var cacheCpuOrd as MachineWord
	export var cacheOsOrd as MachineWord
	export var inlinerStdModule as StdModule
	export var funcEdges as CachedFuncEdgeMap

	// literal-protocol discovery (was cachedBuiltin{Array,Dictionary}LiteralTypeName)
	export var builtinArrayLiteralTypeName as ByteArray
	export var builtinDictionaryLiteralTypeName as ByteArray

	export static function create(name String, rootPath FilePath, cacheDirRoot FilePath, isStdlib bool) returns Library
end 'Library'

export typealias LibraryArray = Array with Library
export type LibraryRegistry
	export var libs as LibraryArray
	export static function create() returns LibraryRegistry
end 'LibraryRegistry'
```

Store the registry as a single top-level `var libraryRegistry = LibraryRegistry.create()` in
`StdlibLoader.maxon` (an array-of-struct constant constructor is a valid top-level initializer,
same workaround `stdlibParsedModuleArr` already uses). Maxon value types copy by value: mutate the
stdlib entry by index (read → mutate → `reg.libs.set(i, value: lib)`, mirroring the
`applyPendingFuncRenames` set-back idiom). Process-global test/CLI knobs (`stdlibCacheBypassed`,
`skippedStdlibFunctions*`) stay loose vars — not per-library.

**Extracted bootstrap (`Compiler/Library.maxon`).** `registerBuiltinTypeBootstrap(project)` wraps
the `__mm`/`__Managed*`/`__Builtins` registrations (the block duplicated at
`getStdlibModule:483-518` and `ensureStdlibSandboxParsed:96-114`). Replace each duplicate with the
call. `registerInternalsIntrinsicSignatures(project)` stays SEPARATE and stdlib-only (called where
it is today).

**Sub-steps.** (1) Add `Library.maxon`; build. (2) Add `var libraryRegistry` + private
`stdlibLib()` fetch-or-create for key `"stdlib"`; keep old singletons. (3) Re-point each exported
accessor body to read/write through `stdlibLib()` (`getStdlibCacheData`→`.cacheData`;
`getCachedStdlibStdModule`→`.inlinerStdModule`; `cachedStdFuncEdges`→`.funcEdges`;
`isStdlibCacheValid`→`.cacheValid`; literal getters/setters round-trip the lib's ByteArray fields;
`activateStdlibCache` mutates+writes-back). (4) Convert `getStdlibModule`,
`buildAndWriteStdlibCache`, `loadCacheForTarget`, the warm/restore + typealias-snapshot loops to
read/mutate the stdlib Library — logic identical. (5) Replace the three bootstrap blocks. (6)
Delete the dead singletons; `resetStdlibLoaderState` resets the stdlib Library.

**Invariants.** Stdlib Library created once per process, reused across all paths.
`registerBuiltinTypeBootstrap` idempotent. `registerInternalsIntrinsicSignatures` still
stdlib-only. `.mxc` unchanged → version stays 69.

**Determinism (spike A2).** When `collectStdlibFiles`/`collectMaxonFilesUnder` (StdlibLoader.maxon:367/384)
move into the Library abstraction (`collectLibraryFiles`), **add a deterministic sort** on the
collected file list — today the walk consumes `Directory.list` unsorted, so parse order (→ interner
ids → byte layout) depends on filesystem enumeration. Sorting makes the cache build reproducible
across machines/checkouts, which the cross-project byte-parity gates rely on. (Verify with spike A2:
build the cache twice and byte-diff; the sort should make them identical.)

**Verify.** build both; spec-test selfhosted (full green); noStdlibCache rebuild; wasm32-wasi;
**byte-diff a hello-world + a generics-heavy exe vs the pre-Phase-1 binary (must be identical)**;
**spike A2** — build the stdlib cache twice → byte-identical `.mxc`.

---

## Phase 2 — Cache file moves into the library's own directory (+ atomic writes)

**Goal.** Move stdlib's cache to `<stdlibDir>/.maxon/cache/` so it is shared across projects. Drop
the sticky `stdlibCacheProjectDirs`. **Bake the compiler fingerprint into the filename** so two
compiler versions sharing the dir coexist instead of stomping each other (see *Cache validity key*).
**Make cache writes atomic (temp+rename)** — sharing the cache means concurrent independent builds
can now race to write it (see *Concurrency & atomic writes*). Update MCP deleter, schema, docs,
`.gitignore`.

**Files.** `StdlibLoader.maxon` (the two path computations 567-576 / 979-982; remove
`stdlibCacheProjectDirs` + the single-file parent dance); `Compiler/Runtime/runtime.std` +
per-target lowering (`Targets/X64/X64Backend.maxon`, `Targets/Linux`/`Arm64`, `Targets/Wasm/MirToWasm.maxon`)
for the new `mrt_file_rename`; `stdlib/File.maxon` + `stdlib/FilePath.maxon` (expose `rename` /
`renameTo`); `Compiler/StdlibCache.maxon` (`writeStdlibCache` routes through `writeFileAtomic`);
`maxon-dev-mcp/mcp/BuildTool.maxon` (`deleteStdlibCacheFiles`/`isStdlibCacheFile`); `Server.maxon:225`;
`.claude/CLAUDE.md:27`; `.gitignore`.

**Signature.**
```
function libraryCachePath(lib Library, target Target) returns FilePath
	let cacheDir = lib.cacheDirRoot.join(".maxon").join("cache")
	let fp = compilerFingerprintToken()          // short hex of getCompilerFingerprint()
	return cacheDir.join("{lib.name}-{targetToString(target)}-{fp}.mxc")
end
```
(The `<optsHash>` filename component is added in Phase 3 when the options hash lands; in Phase 2 the
filename carries target + compiler fingerprint, which is what makes two compiler versions coexist.)
Set stdlib `cacheDirRoot = findStdlibPath()` (the `stdlib/` dir; keeps the cache adjacent to its
sources and shared across every project resolving that stdlib).

**Sub-steps.** (1) Add `compilerFingerprintToken()` (short hex of `getCompilerFingerprint()`'s
size+mtime). (2) Wire `libraryCachePath(stdlibLib(), target)` at both sites; remove the
`projectDir`/`stdlibCacheProjectDirs` logic and the single-file `project.rootPath` parent resolution
(path no longer depends on the user project). (3) **No auto-prune** — caches coexist (see *Cache
validity key*); add a **`maxon clean`** command (`Command.clean` arm in MaxonArgs + dispatch in
Main.maxon) that nukes the project's library caches — in Phase 2 that's stdlib's
`.maxon/cache/` (all `*.mxc` + `*.tmp`); Phase 6 extends it to also enumerate the project's external
libraries. (4) **Atomic write:** add
`mrt_file_rename` (runtime.std + per-target lowering: Windows `MoveFileExW`
REPLACE_EXISTING|WRITE_THROUGH, POSIX `rename`, WASI `path_rename`), expose `File.rename`, add
`writeFileAtomic(path, bytes)` (write `<path>.<pid>.tmp` → rename → cleanup-on-failure), and route
`writeStdlibCache` through it. (5) MCP `deleteStdlibCacheFiles` scans `<repo>/stdlib/.maxon/cache`
(keep `stdlib-*.mxc` predicate — still matches the longer names; generalized to all libs in Phase 6)
— also delete stray `*.tmp`. (6) Update Server.maxon schema text + CLAUDE.md:27 + `.gitignore` (add
`**/.maxon/cache/`).

**Invariants.** Format unchanged this phase — still a single flat per-target file, now named
`<lib>-<target>-<fp>.mxc` (the three-tier split is Phase 3). `currentStdlibChecksum` unchanged → a
matching-fingerprint cache validates. **Coexistence**: two compiler versions on one machine write
distinct files and never force each other to rebuild — and (critically) **neither deletes the
other's** (no fingerprint-based pruning). **Shared cache**: building project B (different dir, same
compiler) hits the cache project A wrote. **Atomic write**: a reader never observes a torn file — old
whole file or new whole file; a concurrent-write race wastes a rebuild but never corrupts.
`noStdlibCache` must wipe the matching-fingerprint cache before a from-source build — verify
`deletedCacheFiles` lists it.

**Verify.** build both; spec-test selfhosted; **shared-cache test** (build A then B in different
dirs → cache hit on B); **coexistence test** (Phase-0 harness: build with the current compiler, then
simulate a different fingerprint — e.g. touch the binary mtime or stub the token — and build again;
assert TWO cache files coexist; then build with the FIRST fingerprint again and assert it is still a
**cache HIT** — i.e. the second build did NOT delete the first's cache); **atomic-write test** (two
concurrent subprocess builds against the same empty shared cache both succeed, final file valid, no
`.tmp` left); `mrt_file_rename` works on x64/wasm; noStdlibCache deletes the matching-fp file (+
temps) and rebuilds from source.

---

## Phase 3 — Split the cache into two files (meta A+B / target C) (CACHE_FORMAT_VERSION → 83)

**Goal.** Replace the single flat per-target `.mxc` with a two-file layout: a meta file
`<lib>-meta-<target>-<fp>.mxc` (Tier A metadata + Tier B inlinable Std IR) and a per-target file
`<lib>-<target>-<fp>.mxc` (Tier C linkable bytecode + globalData). This makes the "each library's cache
contains metadata, inlinable Std IR, and per-target bytecode" structure explicit and separates the
bulky Tier A+B (5.9 MB) from the small Tier C (~0.2 MB) so each is reused independently across rebuilds.
**Same exe bytes** — only the on-disk file organization changes. **Both files are per-target** (see the
spike-A4 finding below: Tier A+B is NOT target-independent — pointer width leaks into layout — so the
hoped-for cross-target meta sharing is dropped for the documented per-target-meta fallback).

**Scope (locked 2026-06-14): SPLIT ONLY.** Tier B stays a **monolithic blob** in the meta file (NOT
stored per-function) and there is **no Tier-C seek index** this phase. The two forward-looking
sub-items the plan originally folded in "to avoid a second format bump" are **deferred to their
consumer phases** — per-function Tier B to **Phase 7** (incremental, where it's the actual enabler) and
the name-keyed Tier-C index to **Phase 6** (if/when mmap is pursued). Rationale: format bumps are
effectively free (caches regenerate from source), so paying that complexity before a consumer exists is
YAGNI. Each adds its own bump when it lands.

**Files.** `StdlibCache.maxon` (split the serializers + version bump + the two file headers + the
`compileOptionsHash` header field + options-hash computation); `StdlibLoader.maxon`/`Library.maxon`
(read/write the two files; checksum split; add `<optsHash>` to `libraryCachePath`); MCP
`BuildTool.maxon` (delete both `-meta-*.mxc` and `-<target>-*.mxc`).

**Format split.**
- Carve the current `encodeStdlibCacheBody`/`decodeStdlibCache` into `encodeLibraryMeta` (Tier A+B)
  and `encodeLibraryTarget` (Tier C) with matching decoders. Each file gets its own `MXC\0` header:
  magic, `CACHE_FORMAT_VERSION = 83`, compiler fingerprint, source checksum, and the total-length
  integrity field (from Phase 0) — all re-validated on read as defense-in-depth even though the
  filename also encodes the fingerprint. The meta file additionally stores a **content hash of Tier
  A+B**; the per-target file stores that meta-hash so a target file is only valid against a matching
  meta file. (Phase 7 note: a whole-meta hash is fine for the non-incremental split here, but Phase 7
  must refine the target→meta reference to **per-function** granularity — see the *Meta↔target
  coupling* invariant there — so an edit to one function doesn't invalidate every target's Tier C.)
- **`compileOptionsHash` / `<optsHash>` filename component — DEFERRED to Phase 5.** Verified against
  current code: the self-hosted compiler has **no codegen-affecting option that varies the stdlib
  cache's bytes** (no `--skip-stdlib-fn`; coverage/leak-check/async-trace are applied per-compile, not
  baked into stdlib bytecode — StdlibLoader's cache is coverage-agnostic by design). So an options
  hash would be a constant for stdlib today. It first earns its keep in **Phase 5** (the user-program
  chunk cache keys on `coverageEnabled`), so the `<optsHash>` filename segment + the header field land
  there. Phase 3 filenames stay `<lib>-<target>-<fp>.mxc` and add `<lib>-meta-<target>-<fp>.mxc`.
- **Tier B stays a monolithic `inlinerStdModule` blob** in the meta file (+ `stdFuncEdges`), exactly
  as serialized today — just relocated from the flat file into the meta file. *(DEFERRED to Phase 7:
  the per-function `name → (Std IR body, content hash)` store + DCE-membership layer, which is what
  makes incremental Tier B generation possible. It has no consumer until Phase 7, so it lands there
  with its own format bump.)*
- **Spike A4 — RESOLVED: meta is PER-TARGET (Tier A+B is NOT target-independent).** The plan hoped
  for one *shared* meta file across targets. A target-independence guard (recompute Tier A+B for a
  second target, assert the content-hash matches the first) **proved x64 ≠ wasm32**: x64 meta-hash
  `2574101378` vs wasm32 `3418012474`. Root cause: `layoutDescriptors` / `structTypes` byte sizes bake
  in the **pointer width** (wasm32 = 4 bytes, x64/arm64 = 8), which is correct and fundamental — the
  meta legitimately differs by target. So Phase 3 takes the plan's **documented per-target-meta
  fallback**: the meta file is keyed by target (`<lib>-meta-<target>-<fp>.mxc`), and the
  target-independence guard is dropped (each target compares its meta only against itself, which IS
  deterministic across rebuilds — verified). The Tier-B snapshot *is* captured pre-`lowerABI`
  (`runDcePerFunctionAndSnapshot`, PassPipeline.maxon:592), but layout descriptors (built earlier, at
  `buildLayoutDescriptors`) carry the pointer-width dependence regardless. **Net:** we lose
  cross-target meta dedup (which was never legitimate), but keep the real structural win — Tier A+B
  separated from Tier C, each reused across rebuilds of the same target.
- The source checksum splits too: the meta file keys off the library source checksum
  (`currentStdlibChecksum` content); the target file keys off (source checksum + meta-hash +
  target). Both must validate for a cache hit.
- In memory, reassemble a `StdlibCacheData`-shaped value (Tier A+B from meta, Tier C from target)
  so the Phase-4 combined accessors and every existing linker (`computeNeededStdlibFunctions`, the
  per-target `linkX64StdlibFunctions*` / arm64 / wasm emitters) are **unchanged**.
- **Tier-C seek index — DEFERRED to Phase 6.** The per-target file keeps today's sequential
  `[length][raw bytes]` per-function layout (just relocated into its own file). A name-keyed index
  (name → code offset/length + fixup range) enabling the linker's demand-closure to seek + a future
  `File.mmap` has **no consumer until mmap is pursued**, so it moves to Phase 6 with its own bump.
  (Metadata tier A is deliberately NOT zero-decode either — it re-interns ids on restore, and full
  metadata restore is already acceptably fast; see *The fast cache-hit build path* non-goal.)

**Cache-miss / partial-cache lifecycle (the cold path).** The split must define what is generated
when each file is absent. The natural split point in `buildAndWriteStdlibCache`
(`StdlibLoader.maxon:813-877`): Tier A/B is finalized at the metadata-capture block (834-845,
`captureStdlibMetadata` + `inlinerStdModule` + `stdFuncEdges`) which runs **after** the shared
pipeline (`lowerMaxonToStd` → `dce` → `lowerABI` → `lowerStdToMir`) and **before** the per-target
backend emit (857-871, `emitWasmPerFunction` / `buildStdlibNativeWith`). Three cases:

Because the meta file is **per-target** (`<lib>-meta-<target>-<fp>.mxc`), the cases collapse to two
per target:

1. **Neither file present for this target** (cold for this target): run the pipeline through the
   metadata-capture block, write this target's meta file (Tier A+B), then emit Tier C and write the
   per-target file stamped with the meta's content-hash. Both produced from one pipeline run.
2. **Both present and valid**: pure restore, zero compilation (the steady-state hit). A meta-present
   but target-missing partial (e.g. the target file was deleted) re-runs the pipeline and rewrites
   both — the meta rewrite is byte-identical (deterministic) so it's harmless last-writer-wins.

(There is no cross-target "reuse another target's meta" case — the per-target finding above means
x64's meta cannot serve wasm32. The lazy-grown re-serialize, `persistGrownStdlibCache`, DOES skip the
meta rewrite: lazy codegen only fills Tier-C bytecode, so it rewrites the target file alone, pairing
it with the meta-hash recorded in-process.)

Key consequence to state plainly: **generating Tier C inherently regenerates Tier A/B as a
byproduct** — the same pipeline produces both, and you cannot lower to MIR without first producing
the Std IR + layout/witness descriptors. So "compile enough to generate the metadata" and "compile
enough to generate the bytecode" are *the same pipeline run up to the backend split*; the meta tier
is never cheaper to produce alone. What the split buys is **separating Tier A+B from Tier C on disk**
so each is reused independently across same-target rebuilds (and the lazy re-serialize rewrites only
the small Tier-C file), not a cheaper cold build. There is deliberately no "metadata-only" build mode.

**Target-independence guard — REMOVED (the meta is per-target).** The original design recomputed Tier
A/B for a second target and asserted its content hash matched a shared meta file. Since the guard
PROVED Tier A/B is target-dependent (pointer width; see *Cache format*), the meta file is now keyed
per-target and there is nothing to cross-check — each target's meta is only ever compared against
itself, which is deterministic. The cache-parity gate (full rebuild vs cache-hit build → identical
exe) remains the backstop for the split's correctness.

**Sub-steps (as built).** (1) Split the encoder into `encodeLibraryMetaBody` (id-space + `encodeLibraryTierAB`)
+ `encodeLibraryTargetBody`, a shared `writeCacheHeader`, and `writeLibraryMetaCache` (returns the meta
content-hash) / `writeLibraryTargetCache` (stamps it); version bump 82→83. (2) Split the decoder into
`validateCacheHeader` + `decodeLibraryMeta` (fills `data`'s Tier A+B) + `decodeLibraryTarget` (asserts
the stored meta-hash, fills Tier C); `readStdlibCache` reads both files and reassembles. (3) `maxon
clean`'s pattern sweep + the MCP `stdlib-*` glob already match `-meta-<target>-` files. (4)
`Library.cacheMetaHash` records the in-process meta-hash so the lazy re-serialize pairs the target file
correctly. (5) Harness scenarios for the two-file layout + meta corruption/deletion.

**Invariants.** Reassembled `StdlibCacheData` is structurally identical to the pre-split blob →
**identical exe bytes**. A stale/mismatched meta file invalidates the dependent target file (meta-hash
check). Version 83 invalidates all pre-split caches.

**Verify (done).** `maxon test-lib-cache` 12/12 (incl. meta-and-target-both-written,
corrupt-meta-falls-back, delete-meta-forces-rebuild); cross-target host+wasm in one cache root →
**four** coexisting files (`stdlib-meta-<target>-<fp>.mxc` + `stdlib-<target>-<fp>.mxc` per target), no
panic, re-host is a fast hit; full selfhosted + wasm spec suite green from a cold cache (cache-parity
byte-identity is the same-exe-bytes proof); `maxon clean` sweeps every `.mxc` incl. the meta files.

**Test coverage (Phase 0 harness).** New scenarios: (a) the file layout is meta + per-target (assert
file presence/absence); (b) build x64 then wasm in two subprocesses → the second reuses the shared
meta file (meta-hash match), only writes its target file; (c) corrupt/delete the meta file →
dependent target files invalidate. Plus the existing-harness gate: `cache-parity.md` still passes
(byte-identity preserved across the split).

---

## Phase 3.5 — Library-owned id-space (CACHE_FORMAT_VERSION → 81): make Tier-A self-contained so any consumer can persist

**Why this phase exists (the gap Phase 2 surfaced).** Once the cache lives in the shared library dir
(Phase 2) and warming is removed, the *goal* is that any consumer — a spec-runner worker, an ordinary
user build — reads the cache, lazily compiles the functions it first touches, and **persists** the
grown cache so the next process reuses the bytecode. The first two work; **persist is broken by
project-coupling in the codec.** Every id-bearing Tier-A field is serialized by resolving its dense
id through the *consuming* `project`'s interners, not the cache's own snapshot. Ids are insertion-order
indices (`StringInterner.intern`: `let id = values.count()`, `Project.maxon:1876`; same shape for
`typeNames`/`genericInstances`/…), so a consumer whose id-space diverges from the cache-build project's
**panics dereferencing a foreign id** when it re-serializes (`writeConstantValue`'s old
`project.strings.get(foreignId)`). To avoid the panic the persist gate is forced off for every
non-cache-build project, so the on-disk cache is stuck at **metadata-only stubs** — it never
accumulates compiled bytecode. Spec workers each recompile stdlib from scratch every run, discarding
the measured **~12.7× warm-build speedup** (CLI build #2 is 328 ms vs #1's 4187 ms precisely because
the CLI path *can* persist). The fix: give the library cache its **own stable id-space** so the codec
resolves through the cache's snapshots, never the consumer — making re-serialize project-independent.

This is a **refinement of Phase 3's Tier-A format** (it changes exactly the `writerGidToProjectGid`
serialize/recompute boundary Phase 3 defines) and a **hard prerequisite for relaxing the persist
gate**; bring it forward here, before Phase 4's N-library restore loop. **Scope: stdlib only** — the
mechanism generalizes to N libraries via stacked base-offset prefixes, folded into Phase 4.

**The model — "library interned first → its ids are a stable reserved prefix."** Per-library *compound*
`(library, id)` keys were rejected: `MaxonType` arms carry a bare integer (`named(id)`,
`genericInstance(id)`, …, `MaxonDialect.maxon:69-118`), the registries are dense single-namespace
arrays (`GenericInstanceRegistry.get(id) == baseIds.get(id)`), and **~225 read sites across 14 files**
index `project.*` by a bare id with no owner context — compound keys would be a pervasive rewrite.
Instead, the library's registries are interned into a fresh consumer **in the library's own
deterministic capture order**; because interning is order-deterministic and the library is restored
into an empty project before any user type, entry *k* lands at id `base + k` in **every** consumer.
The cache's serialized ids are then directly meaningful after restore — no per-consumer translation.

**Files.** `StdlibCache.maxon` (codec, snapshot tables, restore/replay, `CACHE_FORMAT_VERSION`,
`writeGenericInstanceRegistry`, delete `writerGidToProjectGid`/`writerGidToDataGid` + the decode
identity-mirror); `IR/Maxon/MaxonDialect.maxon` (`writeMaxonType` + payload writers take the cache's
tables, not `project`); `Project.maxon` (`writeConstantValue` — already converted to take a
`StringArray` snapshot, the template for the rest); `StdlibLoader.maxon` (relax the persist gate).

**Design decisions (locked).**
- **Replace, don't augment.** `writerGidToProjectGid`/`writerGidToDataGid` + the `translate*` family
  exist only to recover writer gid ordering that `readMaxonGenericInstance`'s inner-first arg interning
  loses (`StdlibCache.maxon:561-571`). Order-faithful flat replay makes destination gid == cache gid by
  construction, so they become dead and are **deleted** (Step 4), with the mm-leak check as the cutover
  safety net.
- **Format bump 80 → 81.** `typeNameTable` + `constantStringTable` (today in-memory-only, rebuilt on
  decode via the identity-mirror) become the **authoritative, serialized** id-space; `genericInstances`
  is serialized as an **ordered flat list** (capture order; payloads reference library-local
  TypeNameIds + earlier-entry gids, a strict DAG). One-time regeneration on first run; no migration.
- **Lazy-append.** A consumer that lazily compiles a not-yet-cached library function may intern **new**
  ids; they are appended at the library tail `[base+N, …)` in intern order and captured into `data` in
  that order, so re-serialize preserves position == id. The originally-cached **prefix is never
  perturbed**; only the freshly-appended tail can differ between two workers that race — **last-writer-
  wins** (the loser re-reads the winning file next run), accepted (a content-key sort of the delta is a
  deferred option if byte-identical cross-worker caches are ever needed).
- **Unify both capture paths.** `captureStdlibMetadata` (in-memory warmup→worker path) snapshots in the
  same library order the disk codec uses, so the `translate*`-as-identity property holds on both paths.

**Incremental steps (each build + spec-green before the next).**
1. **De-couple the remaining write sites** (`writeMaxonType` named/interface/typeParameter/
   genericInstance/function payloads, `MaxonDialect.maxon:890-954`; `writeGenericInstanceRegistry`,
   `StdlibCache.maxon:3400`) to resolve through the cache's `data` snapshots instead of `project` —
   mirroring the `writeConstantValue(data.constantStringTable, …)` change already landed
   (`StdlibCache.maxon:1282`). No format change yet (snapshots still rebuilt on decode); the win is that
   re-serialize no longer dereferences `project`. Verify: re-serialize a cache from a non-cache-build
   project → no panic.
2. **Serialize `typeNameTable` + `constantStringTable`; bump `CACHE_FORMAT_VERSION` to 81.** Reader
   loads them directly; drop the identity-mirror rebuild for these two.
3. **(RISKIEST) Serialize `genericInstances` as an ordered flat list + order-faithful replay.** Reader
   replays the flat list in order, interning each entry's `(baseId, args)` from already-replayed
   entries, NOT by recursive re-derivation. **A subtle ordering bug here is silent: wrong gid → wrong
   layout descriptor → memory LEAK** (the documented `Array with String` / `__layout_Array_SlotState`
   failure, `StdlibCache.maxon:582-585`), so the acceptance gate for this step is the **mm-leak check**
   (exit ≠ 101) on generic-collection tests, not just pass/fail.
4. **Delete `writerGidToProjectGid`/`writerGidToDataGid` + the `translate*` family** for library ids +
   the decode identity-mirror. Confirm by grep no read path still consults them.
5. **Lazy-append:** new library ids appended at the tail in intern order, captured into `data`. Verify:
   a worker that compiles a previously-uncompiled stdlib fn and re-serializes produces a cache whose
   prefix is byte-identical to the input and whose tail carries the new fn's ids.
6. **Relax the persist gate** for non-cache-build projects.

**Invariants.** The library occupies id range `[base, base+N)` identically in every consumer. The codec
resolves every id through `data`'s own tables — fully project-independent. The originally-cached prefix
is stable across consumers and across re-serializes. The name-keyed linker (Phase A1) is untouched —
this changes Tier-A metadata only, not Tier-C linking.

**Verify (the payoff).** **Cold spec run GROWS the on-disk cache with compiled bytecode** (inspect
`stdlib-*.mxc` size before/after a cold `run_spec_test` — it must exceed metadata-only, now carrying
`functions`/`wasmFunctions` bodies). **A 2nd cold run reuses it** (measurably faster, no stdlib codegen
— assert via `--log=compiler`). **Cross-worker sharing observable** (worker A's persisted bytecode is
read by worker B, not recompiled). Plus the standard gates: full selfhosted + wasm spec green from a
cold cache; `cache-parity.md` byte-identity preserved; generic-collection mm-leak check clean.

---

## Phase 4 — Generalize linker + cross-library inlining/DFE to N library caches

> **✅ Done.** The four combined accessors (`combinedLibraryCache` / `combinedInlinerStdModule` /
> `combinedFuncEdges` / `anyLibraryCacheValid`) landed in `StdlibLoader.maxon`, each short-circuiting
> the **single-valid-library fast path** (`findSoleValidLibrary` → return that library's value
> *verbatim*, no copy/merge) so N=1 is a provable no-op; the N>1 union path (Tier C concat +
> globalData union via `mergeLibraryTierCInto`; `StdModule.merge`; edge-map union) is present but
> dormant until Phase 6. Call sites repointed: `Compiler.maxon` (writeExecutable ×2 +
> `finalIrString`), `Queries.maxon` (`dispatchBackendEmit`), `PassPipeline.maxon` (inliner snapshot),
> `StdDeadFunctionElimination.maxon` (DFE edges), `BackendDispatch.maxon` (`augmentWithRuntime` gate,
> runtime-seed gate, `seedFromCacheCallees`), and the `X64Backend`/`Arm64Backend` link gates. The
> stdlib-only `getStdlibCacheData`/`isStdlibCacheValid` survive **only** for the stdlib-specific
> `CACHE_STATS` line; `getCachedStdlibStdModule`/`cachedStdFuncEdges` were deleted (no callers).
> Metadata restore was factored into the per-library seam `restoreLibraryMetadata(project, libIndex)`
> (the metadata is **not** unioned into the combined cache — id-space translation is
> per-`StdlibCacheData`, so each library restores sequentially). **Verified:** self-hosted build
> clean; host spec 2220/2220 + wasm32-wasi 2206/2206, **zero spec-fragment byte drift** vs the
> committed Phase-3 baseline (warm cache); `test-lib-cache` 12/12; cold cache-off (`maxon clean` +
> filtered run) 242/242. The only cold-run fragment delta is a pre-existing cache-off↔cache-on
> panic-message-label hash difference — **reproduced identically on the stashed Phase-3 compiler**, so
> Phase 4 contributes zero behavioral change.

**Goal.** Make codegen/link consume the *union* of all registered libraries instead of the single
`stdlibCache`. Only stdlib exists at runtime until Phase 6, so **byte-identical** — but it unblocks
N>1.

**Approach (lowest-risk, byte-parity-safe).** Merge the N libraries' **Tier C** payloads into one
combined `StdlibCacheData` at the point codegen needs it (concatenate `functions`/`wasmFunctions`,
union `globalData` by name, **stdlib first** so single-library layout is unchanged). This keeps
`writeExecutable`, the per-target linkers, `mergeStdlibGlobalData`, `seedFromCacheCallees`, and the
`augmentWithRuntime` gate all on a single `StdlibCacheData` value — no hot-path signature churn —
while the content is now the union. **Metadata is deliberately NOT folded into the combined value**
(id-space translation is per-`StdlibCacheData`, so it would mis-translate against the wrong snapshot
tables); instead each library's Tier A metadata is restored sequentially through the per-library
`restoreLibraryMetadata` seam. At N=1 every accessor returns stdlib's value verbatim via the
`findSoleValidLibrary` fast path → no allocation, byte-identical.

**Files.** `Library.maxon`/`StdlibLoader.maxon` (combined accessors); `Compiler.maxon` (252,284) +
`Queries.maxon` (361) swap `getStdlibCacheData()`→`combinedLibraryCache()`; `IR/PassPipeline.maxon`
inliner-snapshot (412) merges all libs' `inlinerStdModule`; `IR/Std/StdDeadFunctionElimination.maxon`
(78) consults the union of `funcEdges`; `Targets/BackendDispatch.maxon` `seedFromCacheCallees` (838)
+ the `isStdlibCacheValid()` gate (415) operate on combined / "any library cache valid".

**Signatures.**
```
export function combinedLibraryCache() returns StdlibCacheData   // stdlib first; memoized per (revision,target)
export function combinedInlinerStdModule() returns StdModule
export function combinedFuncEdges() returns CachedFuncEdgeMap
export function anyLibraryCacheValid() returns bool
```
`getStdlibCacheData`/`getCachedStdlibStdModule`/`cachedStdFuncEdges` repoint to the combined
versions (or replace at their few sites).

**Merge correctness (load-bearing).**
- **Name uniqueness across libs**: each library is namespace-anchored, so free functions are
  auto-qualified under its namespace — collisions only on un-namespaced runtime/builtin helpers
  (`mrt_*`/`mm_*`/`__slab_*`), which **only stdlib's cache carries** (enforced in Phase 6: only
  stdlib splices runtime bodies). A genuine duplicate fully-qualified symbol is a hard error,
  diagnosed at metadata restore.
- **globalData/rdata labels**: `mergeStdlibGlobalData` dedups by name; unioning is safe given
  unique labels. Stdlib panic labels use `__stdlib_panic_msg_`; external libs get a disjoint
  prefix (Phase 6).
- **Metadata restore**: restore each library in turn via `restoreLibraryMetadata` → the existing
  `restoreStdlibMetadata` core; id-space replay (`seedProjectFromCacheIdSpace`, library-owned
  snapshot tables since Phase 3.5) is per-`StdlibCacheData` and re-interns into the destination
  project → sequential restore is correct, and is why metadata stays out of the combined Tier-C value.

**Sub-steps.** (1) ✅ Combined accessors iterate `libraryRegistry`; with only stdlib they return its
exact values → byte-identical (`findSoleValidLibrary` fast path). (2) ✅ Repoint the
`getStdlibCacheData()` sites + snapshot/edges/gate. (3) ✅ Factor metadata restore into the
stdlib-first per-library seam `restoreLibraryMetadata(project, libIndex)`. (4) ✅ The
combined-of-one == stdlib-only invariant is established by the **zero spec-fragment byte drift** over
the full host + wasm suites (every one of 4426 compiled programs is byte-identical through the
combined accessors).

**Invariants.** With exactly one library, every combined accessor returns identical bytes. Merge is
deterministic, stdlib-first, registration-order-stable.

**Verify.** ✅ build both; spec-test selfhosted (host 2220/2220, wasm32-wasi 2206/2206);
**byte-diff combined-of-one vs the Phase-3 baseline — zero fragment drift**; cold cache-off
(`maxon clean` + filtered run) 242/242; `test-lib-cache` 12/12.

---

## Phase 5 — User program as a Library (persistent Tier C; inline-aware reuse)

> **✅ Done.** `Compiler/UserChunkCache.maxon` serializes the in-process per-function chunk cache
> (`db.codePerFunc`) to a project-private `<name>-chunks-<target>-<fp>-<optsHash>.mxc`
> (`USER_CHUNK_CACHE_VERSION = 1`). **Load** runs in `queryCodeResult` right after `queryAllMid` and
> before emit, installing each disk chunk into `db.codePerFunc` so the incremental backend reuses it
> as a hit; **store** runs after emit as an atomic read-modify-write *merge* (re-read on-disk, union
> in-memory-wins, atomic rename) so concurrent builds don't clobber. The file-level validity key is
> format version + user-source checksum + compiler fingerprint + target + **`compileOptionsHash`**
> (coverage today) + **`upstreamApiHash`** (commutative digest of the linked libraries'
> `funcSignatures`/`funcReturnTypes`/`funcThrowsTypes`, signatures-not-bodies). The per-chunk gate is
> the **post-inline `keyHash`** alone — see the finding below.
>
> **Finding — the inline-dep fingerprint is NOT cross-process stable, so the keyHash is the sole
> per-chunk gate.** The plan specified `callerInlineDepsUnchanged` as defense-in-depth (b). In
> practice its per-callee `calleeHash = computeStdContentHashFor(standalone callee)` differs between a
> cold build (callee inlined from freshly-compiled stdlib Std-IR) and a warm build (from the cached
> Tier-B snapshot) — the two render the callee's value/string ids differently. The **post-inline
> keyHash is unaffected** (the callee's ops get renumbered into the caller's value space identically
> either way) and already covers the full inline cascade transitively (the inliner re-runs every
> build). So (b) is both redundant *and* harmful here (it would reject every chunk on the first warm
> build after a cold one), and was dropped; the inline-dep fingerprint is not persisted. Re-adding it
> as a real gate is gated on Phase 7 (cross-process-deterministic per-function Tier B).
>
> **Three bugs found + fixed during bring-up:** (1) an **IR-dump build** must compile every function
> fresh (the chunk-driven backend only lowers the MISS set into `targetModule`, which `finalIrString`
> dumps) → `finishInMemoryCompile` (the `*WithIr` entry) pins `enableIncremental = false`, so IR
> capture sees full bodies; the real `maxon build` path keeps the cache on. (2) A single-file
> `maxon build foo.maxon` sets `rootPath` to the **file**, so the cache landed in a phantom
> `foo.maxon/.maxon/cache/` → `userProjectDir` resolves a file rootPath to its parent. (3) A
> `db.userChunkMisses` counter (persistable misses only, excluding the per-compile `mrt_start` runtime
> entry that never persists) drives the store's no-op skip — without it a fully-warm rebuild
> re-wrote an identical chunk file every build. Plus: single-file builds now name the cache by the
> **source file stem** (`foo` from `foo.maxon`) rather than a generic `program`, so two single-file
> programs sharing a directory get distinct coexisting cache files instead of clobbering each other
> (correctness was always safe via the source-checksum key — only reuse was lost).
>
> **Cache location is project-private BY DESIGN** (plan *Library registration*): the user program's
> chunk cache lives in its OWN `.maxon/cache/`, not shared across projects — unlike the *library*
> caches (stdlib + future external libs) which live in the library's dir and ARE shared cross-project.
> Two different projects don't share user-function bytecode (their functions are different programs).
>
> **`maxon clean`** now also sweeps the project's own `.maxon/cache/` (it took a `projectDir` arg).
>
> **Perf.** Three optimizations keep the incremental-rebuild budget: (1) `upstreamApiHash` is
> memoized per process keyed by the stdlib meta-hash (it walks ~1000+ library signatures and is
> needed for both the load- and store-side keys every build); (2) the load path installs disk chunks
> *unvalidated* — the backend's `isCachedChunkValid` re-checks each keyHash anyway, so a stale chunk
> is a backend miss; revalidating in load just duplicated work; (3) `storeUserChunkCache` short-
> circuits when nothing was recompiled this build AND a valid file exists (the common fully-warm
> rebuild does zero disk I/O). The single-edit incremental still pays a read-modify-write merge
> (~490ms vs ~325ms in-memory-only), so IncrementalTestRunner Scenario F's budget moved 400→700ms
> (still well under the ~740ms off-tier baseline).
>
> Verified: self-hosted build clean; host spec **2220/2220** with **zero fragment drift**; wasm32-wasi
> **2206/2206** (chunk cache off for wasm); lib-cache **16/16** (4 new cross-process scenarios:
> reuse-across-processes, warm==cold parity, source-edit-invalidates, coverage-keys-separately);
> incremental **9/9**.

> **⭐ This is the phase that actually delivers the repeated-build / repeated-spec-run speedup.** The
> 2026-06-14 measurement (see *Implementation status*) showed the stdlib cache caps at ~5% of spec
> wall time; the dominant ~90%+ is the **per-test user-program compile + regalloc** this phase caches.
> When building it, adopt the **read-modify-write merge** discipline (re-read on-disk, union, atomic
> write) from day one so the user cache doesn't inherit the stdlib cache's parallel-clobber defect.

**Goal.** The user program gets its own **persistent on-disk Tier C cache** so that across separate
`maxon build` invocations a user function is not recompiled — and, **critically**, when a user
function inlines a stdlib body, the **final post-inline, register-allocated user function** is cached
and reused next build. Concretely, for `foo` that calls inlinable `Array.get`:
1. The inliner splices `Array.get`'s **Std IR from the cache** (Tier B) into `foo` — covered by
   Phases 3-4.
2. `foo` (now containing the inlined code) is lowered + register-allocated → its Tier C bytecode.
3. **That final `foo` bytecode is stored in the program's Tier C cache.** Next build, if `foo`'s
   source AND the inlined `Array.get` body are both unchanged, `foo` is reused verbatim — no
   re-inlining, no re-lowering, **no regalloc of `foo`**.

**This is mostly already built — it just isn't persisted.** The in-process incremental backend
(`emitBackendIncrementalWith`, BackendDispatch.maxon:1386) already stamps each user function a
**chunk carrying its target-lowered, register-allocated bytes**, keyed by `computeChunkKeyHash` and
gated by `isCachedChunkValid`; on a hit it skips target-lowering/regalloc entirely (the motivation
in its own comments is the `__regalloc-user` bottleneck). And the **inline-aware validity hooks
already exist for exactly this purpose**: `captureInlineDeps(project, callerName)` returns each
inlined callee + its splice-time body hash, and `callerInlineDepsUnchanged(stored, fresh)` is the
predicate — its doc says verbatim "A persistent chunk loaded from disk is safe to reuse only when
BOTH its own keyHash matches AND this returns true … this is the hook the on-disk cache depends on"
(Queries.maxon:768-808). Today these gate only the in-memory `db.codePerFunc`, lost at process exit.
Phase 5 **serializes that chunk cache to disk** and re-validates it on load.

**Files.** `StdlibCache.maxon`/`Library.maxon` (a user-program Tier C cache file + per-chunk
serialization: chunk bytes, `keyHash`, and the `InlineDepArray` fingerprint); `Compiler.maxon` /
`Queries.maxon` (load the persistent chunk cache into `db.codePerFunc` at the head of
`queryCodeResult`; write back the updated chunks at the tail); `BackendDispatch.maxon` (the validity
check already calls `isCachedChunkValid`; extend the load path to additionally require
`callerInlineDepsUnchanged(stored, fresh: captureInlineDeps(...))` for chunks that had inlined bodies).

**Cache key (the correctness crux).** A user function chunk is reusable iff **(a)** its own
`computeChunkKeyHash` matches (the function's own post-mid body is unchanged — and, because the
keyHash is the **post-inline** hash and Phase 5 re-runs the inliner each build, this transitively
covers any change to a body it inlined, at any depth) **AND** **(b)** `callerInlineDepsUnchanged`
holds (defense-in-depth for a chunk validated before the fresh inliner pass) **AND** **(c)** the
**upstream-API hash** of the libraries it links is unchanged (see *Invalidation model*). (a)+(b)
handle inlined callees: if `Array.get`'s body changes, the stdlib incremental rebuild (Phase 7)
flips its Tier B hash → `foo`'s post-inline keyHash/inline-deps change → `foo` recompiles. (c)
handles the **non-inlined signature gap**: if a library function `foo` *calls* (not inlines) changes
its **signature** (`funcSignatures`/`funcReturnTypes`/`funcThrowsTypes`), `foo`'s own post-inline
body is unchanged so (a)/(b) wouldn't fire — but the upstream-API hash (a digest of the depended-on
library functions' signatures) changes, invalidating `foo`. **Body-only** changes to a *non-inlined*
callee do NOT invalidate `foo` (the call is by name, resolved at link time against the rebuilt
library's bytecode — A1's name-keyed linker), so the upstream-API hash covers signatures only, not
bodies. This keeps the model graph-free: `foo` validates against (its own source) + (inlined-callee
hashes) + (the API hash of libraries it links).

**Library registration.** Register the user project as a Library: `cacheDirRoot = project.rootPath`,
`name = parseBuildName(...)`, `isStdlib: false`, namespace anchor `project.rootPath`. Its cache lives
in the project's own `.maxon/cache/` (not shared between projects — the user program is
project-private, unlike stdlib/external libs). It carries **only Tier C + the per-chunk inline-dep
fingerprints** — the user program needs no shared meta tier (its metadata is the live `project`
state of its own build). This is a **new cache file format with its own version constant** (not a
bump to the library `CACHE_FORMAT_VERSION`, since it's a distinct file type — the per-chunk store),
named with the same `-<fp>-<optsHash>` coexistence keying.

**Invariants.**
- **Reuse soundness** = the per-function regalloc property (Phase 7) PLUS inline-dep completeness
  (a)+(b) PLUS upstream-API completeness (c). A chunk is reused only if its own body, every body it
  inlined (transitively, via the re-run inliner's post-inline keyHash), AND the signatures of the
  library functions it links are all unchanged. The re-run inliner each build is what supplies the
  transitive inline cascade (see *Invalidation model*).
- **Upstream-API hash**: the user-program cache stores, per build, a digest of the depended-on
  library functions' `funcSignatures`/`funcReturnTypes`/`funcThrowsTypes`; a mismatch on load
  invalidates the chunk cache. Settle the exact contents here (first real consumer).
- **Parity**: a warm rebuild (chunk-cache hits) must produce a byte-identical exe to a cold rebuild
  (`db.enableIncremental` already guarantees chunk-path == whole-module bytes; Phase 5 extends that
  guarantee across process restarts via the persisted chunks).
- **Staleness safety**: version + compiler-fingerprint + **`compileOptionsHash`** guard on the user
  chunk cache (a compiler change or codegen-option change invalidates all chunks), encoded in the
  cache filename so distinct compilers/option-sets coexist (see *Cache validity key*); deterministic
  chunk serialization order.
- **Coverage correctness**: because `coverageEnabled` is in `compileOptionsHash`, a `--coverage`
  build never reuses non-instrumented chunks and vice-versa — the two keep separate cached chunk sets.

**Verify.** build both; spec-test selfhosted; **coverage-keying test** (build non-coverage, then
`--coverage` → the coverage build is a chunk-cache *miss* and emits instrumented code; switching back
hits the non-coverage chunks).

**Test coverage.** In-process gate (IncrementalTestRunner, extends Scenario I): a program where
`foo` inlines stdlib `Array.get`; rebuild unchanged → `foo` chunk-cache hit (`codePerFuncHits`), exe
byte-identical; edit `Array.get` → `foo` invalidates (keyHash changes via `callerInlineDepsUnchanged`).
**Phase-0 harness, cross-process (the core promise)**: (a) **inline-reuse across processes** — build
the program in one subprocess, exit, rebuild in a fresh subprocess with no source change →
`CACHE_STATS` shows `regalloc_funcs=0` for user functions and the persisted `foo` chunk is reused;
exe byte-identical to the first; (b) **inline-invalidation across processes** — edit `Array.get`'s
body, incremental-rebuild stdlib (Phase 7), rebuild the program in a fresh subprocess → `foo`
recompiles (its `CACHE_STATS` shows it as a miss) and the exe reflects the new `Array.get`; (c)
**warm == cold parity** — a cross-process warm rebuild is byte-identical to a from-scratch cold
build of the same sources; (d) **signature-change invalidation (the upstream-API gap)** — change the
**signature** of a *non-inlined* library function `foo` *calls*, rebuild → `foo`'s chunk is a miss
(upstream-API hash changed) and the exe links the new signature; whereas a **body-only** change to
that same non-inlined callee leaves `foo`'s chunk a **hit** (only the library's bytecode changed,
relinked by name).

---

## Phase 6 — Library discovery: `--library=DIR` flag + `build.maxon` `library("...")` manifest

> **✅ Done (NATIVE + WASM).** External libraries are discovered via a repeatable `--library=DIR` CLI
> flag (`MaxonArgs.libraries`) ∪ `build.maxon` `library("...")` manifest entries (`parseLibraryDirs`,
> line-scanned to avoid a loop-carried-slice MM leak), deduped by normalized path, registered into the
> library registry **before** `queryCodeResult` (via `registerProjectLibraries` in `queryAllModule`,
> stdlib-first). `registerExternalLibrary` (StdlibLoader.maxon) reuses the stdlib build pipeline:
> `buildAndWriteStdlibCache` was generalized to `buildAndWriteLibraryCache(libIndex)` with per-index
> cache accessors (`activateLibraryCache`/`setLibraryCacheMetaHash`), and the library's `isStdlib` flag
> (false for externals) gates the three stdlib-only legs OFF — runtime-body splice, the v80 lazy
> signature-stub native emit, and the eager runtime floor. An external lib instead emits its OWN
> functions' full native bytecode EAGERLY (filtered to the names it declares, so stdlib bodies pulled
> into its pipeline aren't double-emitted; they resolve by name through the Phase-4 union at link), and
> does NOT get `registerInternalsIntrinsicSignatures` / `seedRuntimeBootstrapStdlibRoots`. The
> **panic-label-prefix change the original plan called for was dropped** — panic labels are already
> content-hashed (`__panic_msg_<hash>`) and the `__stdlib_panic_msg_` namespace was deliberately
> removed because tying the prefix to `isStdlib` broke cache-parity; re-adding a per-library prefix
> would itself be a regression. Implicit import needs no new code (a library `export` is `global`).
> `isLibraryNamespace` added; the Internals gate (`readerCanCallStdlibInternals`) tightened from
> filename-only to **also require the file under the stdlib root**, so an external lib's own
> `Internals.maxon` can't call `__Internals.*`. Duplicate-basename libraries → hard error (E3090).
> `maxon clean` extended to sweep stdlib + every external library's cache (registry/manifest/CLI).
>
> **Three real bugs found + fixed during bring-up:**
> - **Manifest-scan MM leak** — `parseLibraryDirs`'s loop-carried-slice `var` reassignment leaked one
>   `StringIndex` per `library("...")` entry (the SSA refcount inserter doesn't release a heap-managed
>   local overwritten on a back-edge with a slice-derived-from-self value). Rewritten to a line-by-line
>   scan with no loop-carried managed local.
> - **Cold-stdlib lazy-grow link bug** — `combinedLibraryCache()` is snapshotted once and threaded into
>   the backend; on the N=1 fast path it ALIASES the registry's stdlib array (so link-time lazy stdlib
>   growth via `ensureStdlibFunctionsCompiled` is visible), but with an external library registered it
>   is a DETACHED union COPY, so lazily-grown stdlib bodies (e.g. `__slab_arena_init` pulled in
>   transitively) were invisible to the linker → the function wasn't linked → the program crashed at
>   runtime startup. Fixed with `libraryLinkCacheAfterLazyGrow(preLink)` — returns the param unchanged
>   on N=1 (byte-identical) and re-derives the union after the lazy grow on N>1 — applied in BOTH the
>   x64 (`linkX64StdlibFunctionsConcat`) and arm64 (`linkArm64StdlibFunctions`) linkers.
> - **Single-file manifest discovery** — `maxon build foo.maxon` sets `rootPath` to the FILE, so
>   `parseLibraryDirs` looked for `foo.maxon/build.maxon`; resolved against `userProjectDir(project)`
>   (the file's parent) instead.
>
> **Wasm external libraries WORK (the original "deferred" concern was wrong).** The plan assumed the
> wasm backend bakes each `call` to an absolute function index over the whole module — but the wasm
> cache is **name-relocatable**, exactly like native: a cached `WasmObjectFunction`'s `callFixups` /
> `typeFixups` / `funcAddrFixups` / `globalAddrFixups` all store NAMES (not baked indices), and
> `emitWasmModule` re-resolves every one at final link against the merged function / type / data tables.
> So `mergeLibraryTierCInto` (which already unions `wasmFunctions` + `globalData` by name) makes an
> external lib's wasm functions link against stdlib's separately-cached bodies for free. The
> implementation: an external lib emits ONLY its own functions via a new
> `emitWasmPerFunctionFiltered(module, onlyOwnNames, emitAll: false, extraCalleeSigs: <stdlib wasm
> sigs>)` — `emitAll:false` skips bodies for the stdlib functions its pipeline pulled in (they live in
> stdlib's cache), and `extraCalleeSigs` seeds `funcSigs` with stdlib's wasm function names so a callee
> not in the lib's MIR still gets a throwaway placeholder index minted at emit time (overwritten at
> link). The E3096 wasm-unsupported guard was removed. `emitWasmPerFunction` now delegates to the
> filtered variant with `emitAll:true` (byte-identical stdlib path).
>
> **Verified:** self-hosted build clean; `maxon test-lib-cache` **20/20** (4 new: external-lib via
> `--library`, via manifest with cold==warm byte-identical exe, duplicate-name rejected, wasm builds);
> host **2220/2220** + wasm **2206/2206**, no leaks, runs skipped binary-identical (byte-parity at 0
> external libs holds across both targets). End-to-end: a program calls an external library's
> `libDouble(21)` with no import syntax → exit 42 on BOTH native and wasm (wasm run under wasmtime),
> cold == warm byte-identical on both targets, cross-project lib-cache share is a hit.

**Goal.** Populate the registry with N>1 libraries. Each extra dir parses with its own namespace
anchor; its `export` symbols become globally visible (implicit import, no syntax); it is compiled +
cached to its own dir (shared meta + per-target tiers, Phase 3 layout); its bodies/metadata flow
through the Phase-4 combined accessors into the user build. **Extend `maxon clean`** (from Phase 2) to
enumerate the full library set — stdlib + every resolved `--library`/manifest dir — and nuke each
one's `.maxon/cache/` (the project-scoped reclamation command).

**Files.** `MaxonArgs.maxon` (repeatable `--library=DIR`); `Compiler.maxon`
(`parseLibraryDirs(projectRoot)` mirroring `parseBuildName`; plumb dirs into the registry before
`queryCodeResult` in `compileProject` + the in-memory entry points); `Library.maxon`/`StdlibLoader.maxon`
(`registerExternalLibrary`); `Queries.maxon` `queryAllModule` (merge every registered library's
module); `Project.maxon` (`isLibraryNamespace`); `MaxonDialect.maxon` (tighten Internals gate);
`maxon-dev-mcp/mcp/BuildTool.maxon` (generalize the deleter's name predicate beyond the `stdlib-`
prefix to match any `<lib>-…-<fp>-<optsHash>.mxc` + `-meta-` + `.tmp`); OPTIONAL maxon-sharp parity
(C# build.maxon honors `library("...")` as extra source roots, no caching).

**Signatures.**
```
// MaxonArgs: export var libraries as StringArray; scan arm arg.startsWith("--library=") → push value
function parseLibraryDirs(projectRoot FilePath) returns FilePathArray   // all library("…") matches
export function registerExternalLibrary(rootDir FilePath, target Target)  // parse+build+cache, isStdlib:false, idempotent
export function isLibraryNamespace(project Project, namespace String) returns bool
```

**External-library compile pipeline.** `registerExternalLibrary` runs the **same cold/partial-cache
lifecycle defined in Phase 3** (no meta → compile through metadata-capture, write meta + per-target
tier; meta present but target missing → re-lower to MIR, reuse meta, write the target tier; both
present and valid → pure restore). It reuses `buildAndWriteStdlibCache`'s pipeline with `isStdlib:
false`, differing in: (a) NO `registerInternalsIntrinsicSignatures` (basename gate already rejects
`__Internals.*` from non-Internals files); `registerBuiltinTypeBootstrap` IS applied (the language
runtime surface). (b) NO runtime-body splice into the cache (make the `if not isWasm
'spliceRuntime'` block stdlib-only) — non-stdlib bodies calling `mrt_*`/`mm_*` resolve those by
name against stdlib's cache via the Phase-4 union closure. (c) **Panic-label prefix**
`__lib_<name>_panic_msg_` (generalize the current boolean `isStdlib` into a `panicLabelPrefix`
string on the pass pipeline) so external panic labels don't collide.

**Build-driver wiring (before `queryCodeResult`).** Library dirs = `args.libraries` (resolved
against cwd) ∪ `parseLibraryDirs(project.rootPath)` (resolved against project root), deduped by
absolute path; `registerExternalLibrary(dir, target)` for each; stdlib stays auto-registered by the
lazy `getStdlibModule` path. `queryAllModule` merges every registered library's `cachedModule`
(empty on cache hit) + the Phase-3 metadata-restore loop.

**Invariants.**
- **Implicit import, zero new syntax**: an external lib's `export` is `global` → `isVisibleFrom`
  true for any user reader; TypeResolution finds it by qualified-name/method-index once metadata is
  restored. `module`/`file` decls stay invisible (correct).
- **Per-library cache in the library's own dir** → shared across projects.
- **Name-collision rule**: two libraries (or a library and the user program) exporting the same
  fully-qualified symbol is a hard error, diagnosed at restore. Dir-derived namespaces mean this
  only happens when two library dirs share a basename → diagnose duplicate basenames at
  registration.
- **Internals stays stdlib-only**: tighten `readerCanCallStdlibInternals` (currently filename-only)
  to also require the stdlib namespace, so an external lib shipping its own `Internals.maxon` can't
  call `__Internals.*`.

**Verify.** build both; spec-test selfhosted (0 extra libs → registry is stdlib-only, Phase-4
byte-parity holds); **negative** (external lib calling `__Internals.*` is rejected).

**Test coverage (Phase 0 harness).** This phase's acceptance tests live in the harness (they need
real subprocess builds + on-disk fixture libs): (a) **new-library acceptance** — a tiny fixture lib
with one exported function, referenced via both `--library=DIR` and `build.maxon library("../foo")`;
build+run succeed and the lib's `.maxon/cache/foo-meta-<fp>-<optsHash>.mxc` +
`foo-<target>-<fp>-<optsHash>.mxc` are produced; (b)
**cross-project share** — two user programs (separate subprocesses, separate dirs) vs the same
external lib; the second build hits the lib cache (`CACHE_STATS lib_meta=hit lib_target=hit`); (c)
**wasm** external-lib build; (d) **name-collision** (two libs with the same basename → diagnosed).

---

## Phase 7 — Incremental per-file/per-function cache update (CACHE_FORMAT_VERSION → 84)

> **✅ Done (native + wasm), with a CORRECTED, narrower scope than this section originally framed.** A
> pre-implementation reassessment found the plan's premise was overtaken by **v80 lazy codegen**, which
> already skips regalloc for unreached/unchanged stdlib functions within a build. And the plan's win (b)
> — "skip the whole-library re-parse + re-lower for unchanged functions" — is **structurally
> impossible**: `computeChunkKeyHash` reads `db.midPerFunc[F].keyHash`, populated only by running the
> per-function mid pipeline, which needs the WHOLE module's type/conformance/layout context. You cannot
> compute a function's content hash from an isolated single-file parse. So Phase 7 **always re-parses +
> re-runs the full mid pipeline** on a stdlib edit (mandatory, for fresh hashes + context).
>
> **What Phase 7 actually delivers (the real prize, win a):** across a stdlib SOURCE EDIT, PRESERVE the
> already-compiled Tier-C bytecode of every function whose Std-IR content hash is unchanged — instead of
> discarding the whole Tier-C cache (v80's behavior) and re-register-allocating (native) / re-emitting
> (wasm) every reached function from scratch.
>
> **Implementation:** `CACHE_FORMAT_VERSION → 84` adds a per-function `funcContentHash` map (funcName →
> op-content-deep Std-IR hash via `computeStdContentHashFor` over the post-DCE `inlinerStdModule`
> snapshot — the exact IR lazy codegen lowers from) stored in the **target tier**, co-located with the
> bodies it gates (so preservation reads bodies + hashes in one narrow, interner-free pass). On a
> rebuild, `preserveUnchangedTierC` (native) reads the OLD target file **self-validated** (against the
> file's OWN stored source-checksum + meta-hash, since both change after an edit — the per-function hash
> is the real per-body gate, and the v84 version gate still rejects pre-v84 files), and fills each fresh
> signature stub whose name's new hash == old hash with the preserved real body + unions the old
> globalData + seeds `lazyCompiledStdlibNames`. `preserveUnchangedWasm` is the eager-target analogue:
> emit ONLY the changed/new functions, copy the unchanged old wasm bodies, and **assemble in
> `mirModule` order** so the target cache is byte-parity-safe vs a full rebuild. The recompute set is
> implicit — every function whose hash flipped (a changed body, or a caller of a changed *inlined*
> callee, since the hash is op-content-deep over the post-inline body) — **no reverse call graph
> needed**. The whole-library outer `currentStdlibChecksum` gate is UNCHANGED, which keeps the 0-edit
> path byte-identical (the spec-suite gate). `funcSourceFile` was dropped (synthesized functions have
> empty source paths, and `writeString` rejects empty strings — it was diagnostic-only and not the
> gate). A `phase7_preserved=N` field was added to the `CACHE_STATS` line for observability.
>
> **Verified (the central invariant — incremental == from-scratch full rebuild):** edit one stdlib
> function (`Math.log10`'s `ln_10` constant), rebuild incrementally, and separately cold-rebuild the
> edited source — the **target cache is BYTE-IDENTICAL** on BOTH targets (native: preserved 31 bodies,
> lazy-compiled 23 instead of 54; wasm: preserved 928 of 929, re-emitted 1). Plus host **2220/2220** +
> wasm **2206/2206**, no leaks, 0-edit path byte-identical (runs skipped binary-identical). A dedicated
> edit-stdlib harness scenario is deferred (it needs snapshot/restore infra the lib-cache harness
> lacks); the manual byte-parity proof on both targets + the two full spec suites + the
> `phase7_preserved` observability field are the verification.

**Goal.** A single changed file in a library rebuilds only the affected functions, regenerating
**both the Std IR (Tier B) and the per-target bytecode (Tier C) incrementally** — not just Tier C.
The dominant win is **avoiding register allocation** (the most expensive pass) for everything but
the recompute set: an unchanged function's cached `ObjectFunction` already encodes its regalloc
result and is reused untouched. Today the `.mxc` is whole-library: any change flips
`currentStdlibChecksum` and forces a full rebuild — re-running regalloc over all of stdlib. Phase 7
persists per-file checksums + per-function chunk identity so the loader re-lowers + re-allocates
only changed functions (+ inline-dep-invalidated callers), reuses cached Std IR + bytecode for
unchanged functions, recomputes the whole-library DCE membership + DFE edges (cheap reachability, no
re-lowering, no regalloc), and rewrites the cache in place. This benefits **stdlib development
itself** (editing one stdlib function re-allocates one function, not the whole library), not just
user-program builds.

**Tier B Std IR is a retained byproduct, not a separate stage.** Tier A metadata
(`captureStdlibMetadata`, StdlibCache.maxon:2039) snapshots `project.*` registries that are populated
at **parse + type-resolution + buildLayoutDescriptors/buildWitnessTables — before `lowerMaxonToStd`**,
so metadata itself does not require Std lowering. But regenerating Tier C bytecode for a changed
function *does* require lowering it to Std IR (Std IR is the unavoidable input to MIR/backend). So
for every function in the recompute set we lower to Std IR anyway — and we simply **keep that Std IR
as Tier B** rather than recomputing it independently. There is no standalone "incremental Std IR
pass": one per-function lowering yields both the Tier B body and (continuing to MIR/backend) the
Tier C bytecode. This is sound because the entire Std-tier pipeline (`lowerMaxonToStd`,
`injectDrops`, `mem2reg`, `canonicalize`, `cse`, `licm`, `lowerABI`) is `perFunction` and
intra-function — "none reach into another function" (PassPipeline.maxon:592,626-631) — so an
unchanged function's cached Std IR body stays valid and is reused verbatim. The one whole-module Std
step is **DCE membership** (which functions survive), recomputed cheaply by reachability over the
union of reused + re-lowered bodies; `stdFuncEdges` + the inliner snapshot are reassembled from that
membership and stay correct. (`queryMidForFunction` / `refreshMidContentHashForFunction`,
Queries.maxon:525/738, already produce per-function Std IR + content hash for the in-process cache;
Phase 7 persists the same into the Phase-3 per-function Tier B store.)

**Reuse existing machinery.** The in-process per-function chunk cache + inline-aware invalidation
already exist for the user pipeline (`db.codePerFunc`, `computeChunkKeyHash`, `queryMidForFunction`,
`prepareTargetModuleForFunctions`; `project.inlineEdges`, `recordInlineEdge`, `captureInlineDeps`,
`callerInlineDepsUnchanged`). Phase 7 makes the *library* cache persist the analogous per-function
Std IR + bytecode identity so the same logic gates which library bodies are reused vs recomputed.

**Files.** `StdlibCache.maxon` (format additions + version bump + codecs + incremental entry
point); `StdlibLoader.maxon`/`Library.maxon` (per-file checksum; incremental rebuild driver
replacing the all-or-nothing gate); `IR/IrFunctionContentHash.maxon` (reuse at the library layer).

**Format additions (drive version 84; these are per-function identity tables stored in the meta
tier alongside Tier A/B).**
```
export var fileChecksums as FileChecksumMap       // relPath -> FNV(file bytes)
export var funcSourceFile as FuncSourceFileMap     // funcName -> declaring relPath
export var funcContentHash as FuncContentHashMap   // funcName -> body content hash
export var funcInlineDeps as FuncInlineDepsMap     // funcName -> InlineDepArray
```
Split `computeStdlibChecksum` to record per-file FNV (keep a whole-library checksum in the header
for the fast "nothing changed" path). `funcSourceFile` is keyed to relative paths so the meta tier
stays shared across targets; the per-target tier reuses `ObjectFunction`s by name against this table.

**Incremental update algorithm (`updateLibraryCacheIncrementally`).**
1. Read existing cache (meta + target); on absent / version / fingerprint mismatch → full rebuild.
2. Compute fresh per-file checksums; `changedFiles` = differing/new, `removedFiles` = gone.
3. Empty change set → fully valid, activate (current whole-checksum hit).
4. Re-parse ONLY the library (cheap vs codegen) and **re-run the inliner** so per-function
   `midPerFunc[*].keyHash` is the fresh **post-inline** hash at every level (this is what makes deep
   inline chains cascade transitively without a reverse graph — see *Invalidation model*).
   **Recompute set** = funcs in changed/new files ∪ funcs whose `funcContentHash` (post-inline
   keyHash) changed (covers cross-file renames from `applyPendingFuncRenames` AND any inlined-callee
   change, since the callee's ops are physically in the caller's post-inline body) ∪ funcs whose
   `funcInlineDeps` fingerprint changed (`callerInlineDepsUnchanged == false` — the defense-in-depth
   check for chunks validated before the inliner re-derives the hash), minus funcs from removed files.
5. **Lower + regalloc the recompute set once, keep both tiers.** Run the per-function Std pipeline
   (`runStdPassPerFunction` over `lowerMaxonToStd` → `injectDrops` → `mem2reg` → `canonicalize` →
   `cse` → `licm` → `lowerABI`) on the recompute set, then continue each through the per-function
   backend (`prepareTargetModuleForFunctions` → `lowerMirToTarget` → **`allocateRegisters`** →
   prologue/epilogue → emit). **Retain the Std IR as Tier B and the emitted `ObjectFunction`/wasm
   function (post-regalloc) as Tier C** — same lowering, both outputs kept. Reuse every other
   function's cached Tier B body AND Tier C bytecode verbatim — **those functions are never
   re-allocated.**
6. Recompute DCE membership + `stdFuncEdges` over the **union** of reused + re-lowered Std bodies
   (whole-module reachability, no re-lowering); reassemble the post-DCE inliner snapshot from it.
7. Rebuild whole-library Tier A metadata from the fresh parse (cheap — registries, not codegen).
   **Write-back is whole-file rewrite** (see below): re-serialize the merged meta tier (if its
   content hash changed) and the merged target tier into fresh buffers and replace each file
   (write-to-temp-then-rename for atomicity).

**Update model: whole-file rewrite, not a database (decided).** The cache is a flat serialized blob,
not a record-mutable store — today `writeStdlibCache` builds the whole `StdlibCacheData` into one
buffer and `File.writeBinary` **overwrites the file** (StdlibCache.maxon:661-681); there is no
in-place record update or append, and no `File.mmap`/random-write primitive exists. Phase 7 keeps
this model: an "update" decodes the old cache, reuses unchanged `ObjectFunction`s + Std IR bodies in
memory, merges in the recomputed ones, and **re-serializes + replaces the whole file**. So updates
are **incremental in COMPUTE** (the expensive part — regalloc/lowering runs only for the recompute
set) but **whole-file in I/O** (a few MB rewritten per edit at stdlib scale — trivial next to
regalloc). Add write-to-temp-then-rename so a crash mid-write can't corrupt a shared cache.

*Deferred future optimization (noted, not designed):* if write I/O ever profiles as a bottleneck,
the format could become a **name-keyed random-access record store** (database-lite) where only
changed function records are rewritten/appended + the index updated, without re-serializing the
whole file — at the cost of record-layout/atomicity/stale-record-compaction complexity. This pairs
naturally with the index-friendly/mmap layout already noted in Phase 3. Out of scope here; whole-file
rewrite is sufficient because the compute win (skipping regalloc) is what matters.

**Invariants (the hard part).**
- **Reused + recompiled bytes == a from-scratch full build** for the same final sources — the
  central claim; extend the existing cache-parity harness to the on-disk cache (after an
  incremental update, a `noStdlibCache` full rebuild must produce a byte-identical `.mxc` payload;
  pin deterministic map iteration order when serializing).
- **Regalloc is per-function → reuse is sound.** `allocateRegisters` dispatches to a per-function
  `FunctionRegAllocator` that owns only that function's CFG/live-ranges/coloring/spill state
  (RegisterAllocator.maxon:562-658); there is no cross-function allocation or shared spill arena. So
  re-allocating one changed function while reusing a neighbor's cached allocation is correct — the
  reused function's coloring never depended on the changed one. This is the property that makes
  "skip regalloc for everything unchanged" valid.
- **Position independence**: `ObjectFunction`s are name-fixup-relocatable, so reusing one while
  neighbors recompile is safe (already true stdlib-vs-user; Phase 7 relies on it within a library).
- **Inline-dep completeness**: if F inlined G and G changed, F recompiles — guaranteed by
  `funcInlineDeps`/`callerInlineDepsUnchanged`, captured at the same splice point as the in-process
  path (`recordInlineEdge`, before DCE drops standalone bodies).
- **Metadata coherence**: structTypes/conformances/witnessTables always rebuilt from the fresh
  parse → cannot drift; only machine-code bytes are selectively reused.
- **Meta↔target coupling under incremental edits (design point to resolve in Phase 7).** A
  function-body edit changes that function's Tier B Std IR, so the *whole-meta* content hash changes
  — which, with a naive whole-meta-hash reference, would invalidate **every** other target's Tier C
  file even though their unchanged-function bytecode is still good. To keep cross-target incremental
  caching, the target→meta reference must be at a granularity that does NOT churn on unrelated
  changes. Options (decide in Phase 7): (i) target files reference **per-function** Tier B hashes
  (the `funcContentHash` table) for only the functions they contain, not a single whole-meta hash;
  or (ii) make the meta-hash mismatch trigger a **per-function revalidation/repair** of the target
  file rather than wholesale invalidation. (i) is cleaner and aligns with the per-function Tier B
  store. This must be settled before Phase 7 ships, or incremental edits silently force all-targets
  rebuilds.
- Version bump invalidates every pre-84 cache.

**Verify.** build both; spec-test selfhosted.

**Test coverage.** New IncrementalTestRunner scenario (in-process, fast gate): edit one stdlib-style
fixture function, assert `codePerFuncMisses == 1` and the post-edit chunk keyHash differs (mirrors
Scenario I, now over a *library* cache). New Phase-0 harness scenarios (on-disk + cross-process):
(a) **incremental-parity** — warm cache, change one library file in a subprocess, incremental-update,
then a `noStdlibCache` full rebuild in another subprocess; both `.mxc` payloads AND the exe
byte-identical; (b) **reuse + regalloc-skip** — edit one leaf function; `CACHE_STATS` shows
`regalloc_funcs=1` (only the edited function re-allocated); (c) **inline-dep** — edit a function
inlined into another; the caller recompiles; (d) **wasm** incremental update (reused `wasmFunctions`
preserve type/funcAddr/globalAddr fixups).

---

## Phase landing order + gates

**Implementation order (reordered 2026-06-14):** the sequence is now **3 → 4 → 5 → 6 → 7** where the
*old* Phase 7 (user-program Tier-C cache — the actual repeated-build speedup) was pulled forward to
**Phase 5**, and the former Phase 5 (multi-library discovery) and Phase 6 (incremental rebuild) shifted
to **6** and **7**. Rationale: the speedup-bearing user-program cache depends only on the name-keyed
linker (done) + the tier split (3/4), NOT on multi-library discovery or stdlib-incremental rebuild, so
moving it ahead of 6/7 delivers the speedup earlier (including for our own build iteration while
implementing 6/7) at zero rework cost. Hard deps preserved: 4 needs 3; 5/6/7 all need 3+4; 6 (discovery)
needs 4's union; 7 (incremental) needs 3's per-function Tier-B store.

| Phase | Status | Lands | Byte-parity claim | Format bump |
|-------|--------|-------|-------------------|-------------|
| 0 | ✅ done | `maxon test-lib-cache` harness (9 scenarios incl. fault injection) + `--cache-stats` + `--cache-root` isolation | asserts post-refactor cold/hit/delete/coexist/corruption behaviour | **82** (length-integrity field; surfaced while hardening readers to never panic) |
| 1 | ✅ done | Library type + registry; stdlib = lib#0; extract builtin bootstrap | identical to pre-change | no |
| 2 | ✅ done | cache → `<libDir>/.maxon/cache`; **fingerprint-in-filename** (coexist, no auto-prune); **atomic write**; `maxon clean`; drop sticky pin | path-only, identical bytes | no |
| 3.5 | ✅ done | **library-owned id-space**: codec resolves through cache's own snapshots, not the consumer; order-faithful replay; delete `writerGidToProjectGid`/`translate*`; relax persist gate so workers persist+share | cold spec run grows on-disk cache; cross-worker sharing | **81** (current on-disk is **82** after Phase 0's length field) |
| 3 | ✅ done | split cache into two files (meta A+B / target C), both per-target; per-target-meta fallback (Tier A+B not target-independent — ptr width) | identical exe bytes | **83** |
| 4 | ✅ done | linker/inliner/DFE over N caches via combined accessors (`combinedLibraryCache`/`combinedInlinerStdModule`/`combinedFuncEdges`/`anyLibraryCacheValid`); per-library restore seam (`restoreLibraryMetadata`) | combined-of-one == stdlib-only (host 2220/2220 + wasm 2206/2206, **zero spec-fragment byte drift**) | no |
| 5 | ✅ done ⭐ | **user program as a Library**: persistent Tier-C chunk cache (`UserChunkCache.maxon`) — cross-process regalloc reuse; `compileOptionsHash` (coverage) + `upstreamApiHash` (lib signatures) + source-checksum + compiler-fp + target validity key; atomic read-modify-write merge | warm rebuild == cold, cross-process (lib-cache 16/16 incl. reuse / parity / source-edit / coverage-keying; host 2220/2220, zero fragment drift) | **new file fmt — `USER_CHUNK_CACHE_VERSION = 1`** |
| 6 | ✅ done | `--library=DIR` + `build.maxon library("…")` discovery; `registerExternalLibrary` (build tail generalized by `libIndex`, eager native emit / filtered wasm emit, no runtime-splice/eager-floor/Internals for external libs); `queryAllModule` merge; `isLibraryNamespace`; Internals gate tightened to under-stdlib-root; `maxon clean` → full library set. **NATIVE + WASM** (wasm linking is name-relocatable — same as native). Found+fixed: a manifest-scan MM leak, the cold-stdlib lazy-grow link bug (`libraryLinkCacheAfterLazyGrow`), single-file manifest discovery (`userProjectDir`) | suite identical with 0 extra libs (host 2220/2220 + wasm 2206/2206, runs skipped binary-identical); lib-cache 20/20 | no |
| 7 | ✅ done | incremental Tier-C preservation across a stdlib source edit (`preserveUnchangedTierC` native / `preserveUnchangedWasm`): a function whose Std-IR content hash is unchanged keeps its already-compiled bytecode instead of being re-regalloc'd / re-emitted. Per-function `funcContentHash` map in the target tier; old cache read self-validated (per-fn hash is the gate, not the source checksum/meta-hash). **Scope corrected vs the original plan:** re-parse + re-mid is MANDATORY each rebuild (`computeChunkKeyHash` needs the full-module pipeline — an isolated single-file parse can't produce a keyHash); the win is preserving the regalloc/emit output, not skipping the front of the pipeline | incremental == full rebuild, BYTE-IDENTICAL on both targets (native: preserved 31, lazy 54→23; wasm: preserved 928/929); host 2220/2220 + wasm 2206/2206 | **→ 84** |

✅ Done: the **read-modify-write merge** fix for the stdlib cache's parallel-clobber defect
(`mergeOnDiskGrownStdlibFunctions` in `persistGrownStdlibCache`) — concurrent/sequential cross-project
builds now converge to the UNION of lazily-grown stdlib functions instead of clobbering (see
*Implementation status*).

Cache contents per the three-tier model: every **library** cache holds **(A) library metadata**,
**(B) inlinable Std IR**, and **(C) per-target linkable bytecode** — A+B shared across targets in
`<lib>-meta-<fp>-<optsHash>.mxc`, C per-target in `<lib>-<target>-<fp>-<optsHash>.mxc`. The
**user-program** cache (Phase 5)
is project-private and carries only Tier C chunks + their inline-dependency fingerprints (its
metadata is the live build's `project` state). A user function that inlines a stdlib body caches the
**final post-inline, register-allocated** function and reuses it next build unless its own source or
an inlined body changed (`computeChunkKeyHash` + `callerInlineDepsUnchanged`).

Every phase gate: `mcp__maxon-dev__build` target `both`; `mcp__maxon-dev__run_spec_test`
compiler `selfhosted` (full green vs baseline); `mcp__maxon-dev__build` selfhosted with
`noStdlibCache: true` (from-source rebuild); `mcp__maxon-dev__spec_test_outcome` target
`wasm32-wasi` (cross-target); cache-on vs cache-off byte-diff of representative exes; **plus
`maxon test-libcache` (the new harness) and `maxon test-incremental` (existing) green** — each
feature phase adds its own scenarios per its *Test coverage* note.

## Critical files

- `maxon-selfhosted/Compiler/Library.maxon` (new — Phase 1)
- `maxon-selfhosted/Compiler/StdlibLoader.maxon`
- `maxon-selfhosted/Compiler/StdlibCache.maxon`
- `maxon-selfhosted/Compiler/Targets/BackendDispatch.maxon`
- `maxon-selfhosted/Compiler/Compiler.maxon`
- `maxon-selfhosted/Compiler/Queries.maxon`
- Phase-6 (discovery) touch points: `Compiler/MaxonArgs.maxon`, `Compiler/Project.maxon`,
  `Compiler/IR/PassPipeline.maxon`, `Compiler/IR/Std/StdDeadFunctionElimination.maxon`,
  `Compiler/MaxonDialect.maxon`
- `maxon-dev-mcp/mcp/BuildTool.maxon`, `maxon-dev-mcp/mcp/Server.maxon` (Phase 2/3/5),
  `Compiler/IR/IrFunctionContentHash.maxon` (Phase 7)
- `IR/Maxon/LowerMaxonToStd.maxon` (extract `registerBuiltinTypeBootstrap` — Phase 1)
- Phase-5 (user-program cache) touch points: `Compiler/Queries.maxon` (`captureInlineDeps` /
  `callerInlineDepsUnchanged` / `computeChunkKeyHash` / `db.codePerFunc` already exist — wire to
  disk), `Compiler/Targets/BackendDispatch.maxon` (`emitBackendIncrementalWith` chunk stamp/lookup),
  `Compiler/Passes/Inliner.maxon` (`recordInlineEdge` already populates `project.inlineEdges`)
- Test harness (Phase 0, new): `maxon-selfhosted/Testing/LibCacheTestRunner.maxon`,
  `maxon-selfhosted/Testing/LibCacheFixtures.maxon`; `Compiler/MaxonArgs.maxon`
  (`Command.testLibCache`, `--cache-stats`); `Main.maxon` (dispatch); `Compiler/QueryDatabase.maxon`
  (counters already exist — emit `CACHE_STATS`); existing `Testing/IncrementalTestRunner.maxon` +
  `specs/cache-parity.md` (per-phase scenario additions)
- Atomic-write primitive (Phase 2, new `mrt_file_rename`): `Compiler/Runtime/runtime.std`;
  per-target lowering in `Compiler/Targets/X64/X64Backend.maxon`, the Linux/arm64 backends, and
  `Compiler/Targets/Wasm/MirToWasm.maxon`; `Compiler/IR/Maxon/LowerMaxonToStd.maxon` (managed-file
  lowering); `stdlib/File.maxon` + `stdlib/FilePath.maxon` (expose `rename`/`renameTo` +
  `writeFileAtomic` helper) — every cache writer routes through it
- `maxon clean` command (Phase 2, extended Phase 6): `Compiler/MaxonArgs.maxon` (`Command.clean`
  arm), `Main.maxon` (dispatch → resolve the project's library set + delete each lib's
  `.maxon/cache/` `*.mxc`/`*.tmp`); reuses the Phase-6 library-discovery resolution
