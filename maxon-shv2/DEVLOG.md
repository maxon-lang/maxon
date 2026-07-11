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

- **Three tiers, flat op unions** — `Maxon → Std → Target`; NO MIR tier (v1's
  added no value model — Std's `ValueId`s already are the virtual registers).
  Every dialect's op union is FLAT with a required `*OpMeta` struct backing, never
  nested: Maxon boxes each payload-carrying union case, so nesting costs a heap
  object per level on the compiler's most numerous object. *(→ Std dialect section)*
- **Band-append invariant** — `StdOp` variants sit in category-contiguous bands so
  passes can cover a category with one `match` range arm. **A new variant is
  appended at the END of its band, never inserted into the middle** — a range arm
  silently swallows anything inserted between its endpoints, with no missing-case
  error. This is the ONE place "no silent unhandled cases" has no compiler
  backstop; reviewers enforce it. *(→ Std dialect section)*
- **No sugar reaches the backend** — `sugar`-category `StdOp`s are eliminated by
  the Std→Std `lowerToMachineForm` pass. Guarded by `assertNoSugarOps` in
  `buildBackend` (the target-neutral entry, so every backend inherits it) + an
  explicit `panic` arm per sugar variant (never a bare `default`). Replaces the
  type-level guarantee v1 got from its MIR boundary; spec tests are the real
  guarantee. *(→ Std dialect section)*
- **Live ops ≠ `module.ops`** — `IrModule.ops` is append-only; block-rebuilding
  passes leave the ops they replaced behind as orphans. Any pass asking "what ops
  are actually here?" must go through `IrModule.liveOpIndices(func)`, the single
  home of the blockRefs → opRefs+terminator walk. Scanning the flat array sees dead
  ops from every prior pass. *(→ Std dialect section)*
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
`IR/Maxon/Scope.maxon`.

**Parser + parse-staging landed (M1 Chunk C).** `Compiler/Parser.maxon` is the
thin `return <int-literal>` slice: `parseModule → dispatchTopLevel(function) →
parseFunction → parseOptionalReturnType → parseStatements → parseReturnStatement
→ parseExpression → parsePrimary → parseIntLiteral → emitLiteral`, with a small
index cursor and a per-function synthetic-name counter (`$tN`). Its ONE
structural change from v1 (the parallel-ready seam): **it takes no `Project` and
writes exclusively into a per-file `FileParseArtifact`** — the file's own
`MaxonModule` fragment, its own LOCAL `TypeNameInterner`, and an ordered
`funcReturnTypes` contribution array. So a file's parse is a pure function of
`(tokens, filePath, namespace)` with zero shared state (M5's per-file fan-out
needs no further parser change). Anything outside the M1 slice (params,
non-`return` statements, operators, user types) is rejected with a positioned
`ParseError` that `queryParseOps` turns into a project diagnostic — the parser
itself never touches `Project` or `project.diagnostics`.

**`Compiler/ParseStaging.maxon`** owns `FileParseArtifact` + `mergeArtifact(project,
target, artifact)`, the **single writer** of the shared `Project` derived
registries. It (1) folds the artifact's local interner into `project.typeNames`
(via `TypeNameInterner.foldInto`, returning a `TypeNameRemap`), (2) remaps the
artifact's `named(id)` references when the fold moved ids (M2 multi-file path;
identity for M1's single file → skipped), (3) offset-merges the `MaxonModule`
fragment into the accumulator via `IrModule.merge`, and (4) commits the
`funcReturnTypes` entries. Because the parser wrote nothing to `Project`
speculatively, there is **no ParseDelta rollback dance** — v1's ~28-registry
rollback machinery is replaced by "artifacts are the source of truth; rebuild
the derived registries from them on every `queryAllModule` miss"
(`resetMergeTargets`). The registry set is one family (`funcReturnTypes`) at M1;
it grows toward v1's ~28 per milestone, each as an ordered contribution array.
Interner NOTE: for a *cached* artifact, a non-identity remap must clone before
rewriting (it must not mutate the cache in place) — a documented M2 refinement,
never triggered at M1 (single file → identity remap).

`Compiler/Project.maxon` **grew** the `Project` struct (`db`/`funcReturnTypes`/
`typeNames`/`rootPath`/`target`/`diagnostics`/`globalData`) + `createProject` +
the diagnostic sink helpers, GROWING (not redefining) the Chunk-A foundation.

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
- `MaxonType` union — `boolean/integer/float/named(TypeNameId)/exitCode/
  unresolved`. `named` is the parser's interned type reference; `unresolved` its
  placeholder. **`exitCode` (added M1 Chunk C)** is the RESOLVED form of the
  `ExitCode` builtin alias — a width-FREE tag like `boolean`/`integer`. Its
  unsigned-32-bit width is assigned only at the Maxon→Std boundary
  (`maxonTypeToStdType` → `StdType.u32`), so the "MaxonType carries no width;
  width collapse happens only in lowering" invariant holds. TypeResolution
  eliminates `named`/`unresolved` before lowering. Void is not a MaxonType —
  `MaxonReturnType{void, value}` carries return slots.
- **`OwnershipKind` lattice** — `trivial | owned | borrow | shared`, the plan's
  first-class Maxon-tier ownership. Three homes, zero sidetables: (1) the
  attribute (`VarInfo.ownership`, defaulted `trivial`), (2) signature modes in
  the function type (M5/M6), (3) explicit `own.*` ops (M6). `isTrivialOwnership`
  is the shared classifier. Present-but-inert at M1; M6 activates it.

Later milestones GROW `MaxonType` (string/char/function/generic/interface arms),
`MaxonOp` (var/arith/call/control-flow), and the signature ownership modes.

### Std dialect

**Landed M1 Chunk A; restructured post-M1 (flat + 3-tier).** `IR/Std/StdDialect.maxon`
is the mid-level tier — and, since the MIR tier was dropped, also the
**machine-level** tier. Two invariants define its shape, and both are load-bearing:

**1. There is NO MIR tier.** Tiers are `Maxon → Std → Target`. v1's `lowerStdToMir`
was ~90% a mechanical 1:1 rename: Std's `ValueId`s **already are** the infinite
virtual registers MIR claimed to introduce, so the tier bought no new value model —
only a whole extra module copy in RSS and a wall that hid dead code (v1's
`MirOp.movReg` has zero construction sites yet still forces match arms in five
places). Everything v1 ran *on* MIR (`commuteForCoalescing`, `scheduleInstructions`)
is a Std-tier pass here, and MIR's one piece of real content — desugaring — becomes
a **Std→Std** pass, `lowerToMachineForm`.

**2. `StdOp` is FLAT** — one union, no `StdOp.arith(StdArithOp.const(StdArithConst))`
nesting. Maxon heap-boxes and refcounts every payload-carrying union case, so each
nesting level is another heap object: nested = **3 boxes per op**, flat = **1**, on
the most numerous object in the whole compiler. (v1's own StdDialect header admits
the nesting was a migration artifact kept "so existing pass code that matches on
these variants keeps working," not a design.) Coarse membership — "is this any kind
of call?" — is recovered without nesting from the `StdOpMeta` struct backing every
variant is REQUIRED to carry: `category` (`StdOpCategory`), `role` (`OpRole`:
plain/ret/errorReturn/param), the two inliner axes (`isPure`,
`isUnsupportedInInlineBody`), and the scheduler facts (`isMemory`/`isStore`/
`isCall`/`clobbersFlags`/`isCmp`). A new variant cannot be added without declaring
all of it.

**Purity is declared PER VARIANT, and that is the flat union's real payoff.** v1
could only answer "is this op side-effect-free?" by CATEGORY, at the match site:
`Inliner.isPureOp` reads `arith gives true; control gives true; call(c) gives
c.rawValue.isPureForInlining; memory/system give false`. A category blanket has no
way to say a *trapping* op is impure — v1's `arith gives true` calls an integer
`div` pure even though it faults on divide-by-zero. With per-variant metadata, M3's
`div` simply declares `isPure: false` and nothing else changes. (`clobbersFlags` is
a lowering artifact, not a side effect: a flag-clobbering op is still `isPure`.)

**Bands, and the one rule that has no compiler backstop.** Variants are declared in
**category-contiguous bands** in `StdOpCategory` order (arith · control · call ·
memory · system · sugar), so a pass that treats a whole category uniformly covers it
with ONE `match` range arm instead of one arm per variant. The cost: a range arm
names its endpoints, so it **silently swallows anything inserted between them** — no
missing-case error, and the new op is misclassified at every range-arm site at once.

> **INVARIANT: append a new variant at the END of its band. Never insert into the
> middle of one.** This is the single place the project's "no silent unhandled cases"
> rule is a convention rather than a compiler-enforced property. Reviewers must
> enforce it by hand.

**The sugar band.** v1 enforced "no sugar reaches the backend" with a *type*: sugar
ops lived in `StdOp` and had no `MirOp` counterpart, so the tier boundary made them
unrepresentable downstream. Collapsing the tier gives that up; the replacement is
deliberate, not accidental — (1) `assertNoSugarOps(module)`, a whole-module gate
run by **`buildBackend`**, the single target-neutral backend entry (NOT inside the
x64 lowering — sugar is a property of the Std tier, not of x64, so putting it at the
one shared entry means arm64/wasm inherit it instead of each having to remember to
re-add it), and (2) an explicit `panic("… must be desugared by lowerToMachineForm")`
match arm per sugar variant (never a bare `default`, which is exactly what would
swallow a new case). **Spec tests are the real guarantee.** Both guards are vacuous
at M1 — no sugar variants exist yet — so `lowerToMachineForm` itself is NOT written;
it arrives with the first sugar op (M6 `drop`/`free`, M14 descriptors/witness
tables).

`assertNoSugarOps` walks **live** ops via `IrModule.liveOpIndices(func)`, never the
flat `module.ops` array. That array is append-only: block-rebuilding passes clear a
block's `opRefs` and re-append fresh ops, leaving the replaced ops behind as orphans.
`lowerToMachineForm` is exactly such a pass, so the sugar ops it desugars *survive in
`module.ops`* — scanning the flat array would panic on dead entries and report a
desugaring failure that never happened. `liveOpIndices` is now the single home of the
funcs → blockRefs → blocks → opRefs+terminator descent (`liveOpCount` goes through it
too).

Flat does **not** mean one variant per opcode: a family sharing an operand shape
(`add`/`sub`/`mul`) stays one variant with an opcode field, but a member needing
*different metadata* (a compare sets `isCmp`) splits out, because the `StdOpMeta`
backing attaches per variant and cannot reach an opcode buried in a field. M3 lands
`binOp(… opcode StdBinOpcode)` + a separate `cmp(…)` for exactly that reason.

Today the union holds `const(resultId, value, valueType)` and `ret(retValId)`.
`const` carries a `StdType` rather than v1's `constI64`/`constF64` opcode pair — it
subsumes MIR's `isFloat` bool and makes i32/u8/f32 representable without new
opcodes. `StdType`/`StdTypeInfo`/`CastCategory`/`StdReturnType` are unchanged from
Chunk A.

### Own tier (ownership infer / check / escape / drops)
_stub — filled in at M6._

### Type resolution & semantic check (Maxon tier)

**Landed (M1 Chunk C).** `TypeResolution.resolveTypes(project, module)` replaces
every `named`/`unresolved` `MaxonType` in the module's function signatures with a
concrete one and re-syncs each resolved return type into `project.funcReturnTypes`
(SemanticCheck's source of truth). M1 SHORTCUT: the only named type resolved is
the `ExitCode` builtin alias, recognized **by name** without loading the stdlib
(→ `MaxonType.exitCode` → u32); any other named type panics loudly (user types +
the ranged-typealias registry arrive at M9). `SemanticCheck.semanticCheck(project)`
is the two `basics.md` checks reading the resolved registry: **E3001** (no `main`)
and **E3002** (`main` does not return ExitCode; the M1-exercised case is a void
`main`). Both append a `Diagnostic`; the pipeline's error gate then bails.

### Pass pipeline

**Landed (M1 Chunk C); MIR pass removed post-M1.** `PassPipeline` owns the two tier
modules it actually drives (`maxonModule` → `stdModule`) + a `project` handle + the
scheduled `PassKind` list. It owns NO target module — the pipeline's last tier is
Std, and the backend is backend-direct — so the write-once-read-never `targetModule`
field (and the `PipelinePhase` enum, which had no callers) went out with `mirModule`
as the same vestigial-tier residue. `dispatch` wires each pass to its free function:
`resolveTypes`/`semanticCheck` (read the Maxon module + project), `lowerMaxonToStd`
(produces the next tier). `run()` gained the **error gate**: after each pass,
`projectHasErrors → throw CompileError` — so a program that fails semanticCheck
(e.g. has no `main` for the backend entry stub to call) never reaches
lowering/backend. The driver (`Compiler.compile`) builds the pipeline with
`buildDefaultPipeline()`, runs it, then goes backend-direct (`buildBackend` →
`writeExecutable`; a CodeResult is not an IR module).

**The pipeline ends at `StdModule`** — there is no second "backend pipeline" over a
machine tier, because there is no machine tier (see Std dialect). Everything that
would have gone there grows in HERE, appended after `lowerMaxonToStd`: the Std opt
passes (M3+), the Own-tier passes (M6), and `lowerToMachineForm` (M6).

Diagnostics are printed by `compile` on the failure path and the error is
re-signaled as a FRESH `compileError` (re-throwing the *caught* error NULL-increfs
under the C#-emitted refcount runtime; a fresh throw of the same arm is clean — a
driver-shape gotcha worth remembering).

### Query spine (incremental)

**Landed (M1 Chunk C), content-hash-keyed.** `QueryDatabase`/`QueryEngine`/
`Queries` implement `querySourceFile → queryTokens → queryParseOps →
queryAllModule`. The load-bearing difference from v1: **memo validity is a
content-hash compare, not a revision-counter walk** (PLAN.md §78). `fileChanged`
computes an FNV-1a `ContentHash` per file (no shared `currentRevision` counter to
contend on — parallel-safe); each per-file memo stamps the `keyHash` it was
derived from and is valid iff that still equals the file's current hash;
`queryAllModule` keys its merged-module memo on the COMPOSITE hash folded over
every file's hash in source-path order. `queryParseOps` produces a
`FileParseArtifact` and touches no `Project` state (the parser is pure);
`queryAllModule` on a miss `resetMergeTargets` + re-folds artifacts via
`mergeArtifact`, so there is no rollback. The dependency graph
(`recordDependency`/`clearDepsFor`/`activeQueryStack`) is recorded from day one —
M1's single file never drives an invalidation, but recording it now is what lets
the **M2 warm-rebuild byte-identity assertion** join without retrofitting.

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

### Backend (Std → Target, runtime emitters)
_Thin mov/ret slice landed at M1 (Chunk B2); MM runtime + DebugStream producer at
M6; GT scheduler at Phase F._ The M1 driver (`Compiler.compile`) feeds the parsed,
resolved, lowered `StdModule` straight into `buildBackend(stdModule, target,
globalData)` → `writeExecutable`.

`Std → Target` is the **only** tier boundary below Std (there is no MIR — see Std
dialect). `lowerStdToX64` (`Targets/X64/StdToX64Conversion.maxon`) asserts
`assertNoSugarOps` on entry, clones the block skeleton 1:1, then walks each function
lowering `StdOp`s to `TargetOp`s with still-virtual registers; `allocateRegisters` →
`insertPrologueEpilogue` → `augmentWithRuntime` → `emitFunctionChunk` →
`concatX64FunctionChunks` finish the job. The virtual registers the allocator colors
are Std's own `ValueId`s, unchanged since `lowerMaxonToStd` minted them.

`CodeResult.stdModule` (v1: `mirModule`) carries the mid-level module the backend
lowered FROM. v1 kept its MIR module here because the **wasm** backend consumes the
machine-level IR directly instead of going through a target dialect; with the tier
gone, Std *is* that machine-level IR, so wasm (M17) reads this field. x64/PE ignore
it.

The driver CLI is `maxon-shv2 build <file|directory> [-o <output>]`; a single-file
build writes next to the source (`basic.maxon` → `basic.exe`). **End-to-end
proven:** `maxon-shv2.exe build examples/basic.maxon` produces a PE that exits
**42**; a program with no `main` prints exactly `error E3001: No 'main' function
found` (exit 1); a void-returning `main` prints exactly `error E3002: Function
'main' must return ExitCode` (exit 1).

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
  see the Std dialect section for the full rationale (MIR added no value model;
  nesting cost 3 heap boxes per op instead of 1). Done at M1 because the same
  merge costs ~160 lines now and ~1,500 in v1. All three M1 gates re-verified
  (exit 42 byte-identical at 28 bytes of `.text`; E3001; E3002).
- [ ] **M2** variables · [ ] **M3** arithmetic
- [ ] **M4** control flow · [ ] **M5** functions (fan-out)
- [ ] **M6** heap+drops · [ ] **M7** moves+borrows · [ ] **M8** escape→refcount
- [ ] **M9** structs · [ ] **M10** strings · [ ] **M11** arrays
- [ ] **M12** enums · [ ] **M13** closures · [ ] **M14** interfaces/generics · [ ] **M15** error handling
- [ ] **M16** feature-complete · [ ] **M17** self-compile · [ ] **M18** budget gate
