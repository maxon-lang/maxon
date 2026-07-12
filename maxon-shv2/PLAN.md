# maxon-shv2 — Minimal-Core Plan

> **ADOPTED 2026-07-11.** This replaces the original M1–M18 plan (preserved in git
> history) — the user adopted its core-first ordering, whose load-bearing consequence
> is **generics BEFORE ownership** (Stage 2: ownership's `own.drop` on a type-parameter
> value needs the runtime layout descriptor, so generics must exist first; strings/arrays
> are themselves generic and can't precede it either). The self-host reframe (compass,
> not gate), the `stdlib-shv2/` fork, and the `selfhost-distance` ratchet come with it.
>
> **Stage ↔ old-M mapping** (so DEVLOG's M-numbered ledger stays interpretable):
> - **Stage 0** (test loop + tooling) — NEW; no old-M. `0.1 spec-test` **DONE** (`72ffe87a9`).
>   Pending: `0.2` MAXON_STDLIB, `0.3` stdlib-shv2 fork, `0.4` selfhost-distance compass,
>   `0.5` rewrite the 9 core-violating sites, `0.6` one-command.
> - **Stage 1** (scalar core) = old M1–M5. **DONE:** M1 basics, M2 variables, M3 arithmetic,
>   M4a comparison+if. **IN PROGRESS:** M4b while/break/continue + mem2reg. **PENDING:** M5
>   functions+params+calls **+ the REAL register allocator** (Stage 1's one design item).
> - **Stage 2** (hard mechanisms, **generics-before-ownership**) = old M6–M15, REORDERED:
>   2.1 structs → 2.2 generics → 2.3 interfaces → 2.4 heap+ownership+drops → 2.5 moves/borrows
>   → 2.6 escape→shared → 2.7 Array → 2.8 String → 2.9 owned-payloads → 2.10 Map → 2.11 for-in
>   → 2.12 errors → 2.13 ranged-typealias. (Original: ownership at M6, generics at M14.)
> - **Stage 3** (self-host, 3-stage fixpoint) = old M16–M17. **Stage 4** (broaden:
>   closures/async/general-Iterable/Set/arm64/wasm/full-suite) = NEW. **Stage 5** (budgets
>   ≤30s/≤1.7GB/>90% CPU) = old M18. **Workstream R** (emitted runtime) slices into Stage 2.

## Context

`maxon-shv2` is the ground-up rewrite of `maxon-selfhosted` (v1, 191,487 lines). shv2
is at **M1**: 7,961 lines, compiles `function main() returns ExitCode / return 42` to a
working PE. The bones are good — content-hash query spine, parse-staging, 3 IR tiers
(Maxon→Std→Target, no MIR), flat `StdOp`, x64/PE backend.

**The goal:** implement every hard mechanism of Maxon *minimally*, so the compiler can
be built and validated with fast iterations.

### The honest sizing (this corrects an earlier draft of this plan)

I initially pitched "self-host early" as a shortcut. **It isn't, and the numbers say so.**
The critical path to self-compile is **~45–55k lines of new Maxon**, and "Maxon-core"
turns out to be **~75–80% of the language**, not 25% — it excludes only closures, async,
general `Iterable`, extensions, and higher-order/sort. Self-compile is the back half of
the project no matter how it is sequenced.

**Two items on that path are unbudgeted in the current plan documents:**

1. **The runtime shv2 must *emit*** (~5–7k lines of hand-assembled machine code). A
   self-compiled shv2 has to *run* — so shv2's backend must emit the slab allocator,
   refcounting, `__destruct` cascades, and `__ManagedMemory`. `PLAN.md` *schedules* this
   (MM runtime + DebugStream producer at M6, GT scheduler at M16) but budgets zero lines
   for it. Here it is **Workstream R** — sliced into Stage 2, not a stage of its own,
   because 2.4's own mm-trace gate cannot run without the first slice.
2. **The runtime-binding decision.** v1 binds its runtime by *compiling*
   `stdlib/Internals.maxon` (3,862) through a file-path-gated `__Internals` intrinsic
   mechanism + `StdlibLoader.maxon` (2,572). The C# bootstrap instead *excludes*
   `Internals.maxon` outright ([0-Compiler.cs:879](maxon-sharp/Compiler/0-Compiler.cs#L879))
   and emits its runtime natively. Neither plan doc chooses for shv2. **Decision: shv2
   follows the C# bootstrap** — exclude `Internals.maxon`, emit natively (that *is*
   Workstream R), register the `__Managed*` builtin surface. v1's 6,434 combined lines
   are the road not taken, not an additional budget line.

**Explicitly deferred: the stdlib cache.** v1's is ~6k lines (`.mxc`, format v89) and its
version history — **v81→v89, every bump a soundness bug** in id-space stability or hash
coverage — is a warning label. Caching is revisited *after* the compiler works; the
approach will be reconsidered from scratch rather than ported. **Consequence: every shv2
build re-parses the stdlib from source, so the pruned `stdlib-shv2/` (0.3) is the *only*
lever on per-build stdlib cost** — which raises its value from "nice speedup" to "the
iteration-speed mechanism." One cheap hedge worth keeping in the meantime: keep every
id-producing table (`TypeNameInterner.foldInto`/`TypeNameRemap`) serializable-in-order, so
a future cache has a stable id space to build on rather than a retrofit.

**So self-host is the compass, not a gate.** What it genuinely buys is a
`selfhost-distance` ratchet that makes the next feature fall out of measured data
("412 sites need `let`") instead of a guessed milestone order — and that enforces
"shv2's source stays in core" in CI rather than as a suggestion.

**Intended outcome:** a ~50–65k-line compiler that self-hosts, with every pervasive
mechanism landed *before* the passes that must be aware of it.

---

## Locked decisions

| | Choice |
|---|---|
| **Generics** | **Dictionary-passing + 64-byte layout descriptors + witness tables** — v1's design. (Rejected: monomorphization. It contaminates 3 files instead of 20, but trades code size and compile time — exactly the resources the ≤30 s / ≤1.7 GB budget exists to conserve.) |
| **Self-host** | **Compass, not a gate.** `selfhost-distance` ratchet from day one; no "early" promise. |
| **Conditional conformance** | **OUT of core** — see the one-line finding below. |
| **Core `Map`** | multi-param generics + one `Hashable` constraint. No `Iterable`-on-Map, no tuple `Entry`. |
| **Iteration** | **Hardcode `for-in`** over Array/Range/String. No general `Iterable`/associated types in core. |
| **Errors** | v1's design **verbatim**: dual-register `(value, errorFlag)`. No unwind tables. Already minimal. |
| **Stdlib** | `stdlib-shv2/`, a *pruned fork* (the C# bootstrap hard-couples to `__ManagedMemory` + marker interfaces). |
| **Test loop** | Minimal spec runner, **before any new language feature**. |
| **Targets** | x64-windows only through self-host. |

### The one-line finding that removes the hardest feature from core

Under dictionary-passing, **conditional conformance** (`Array implements Hashable,
Equatable where Element is Hashable and Equatable`, [stdlib/Array.maxon:406](stdlib/Array.maxon#L406))
forces **per-gid witness tables + synthesized thunks** — the nastiest part of the
generics story.

shv2's source needs it in **exactly one place**:
[GlobalDataTable.maxon:23](maxon-shv2/Compiler/Targets/Shared/GlobalDataTable.maxon#L23)
— `Map with (ByteArray, String)`, an rdata constant-dedup index. Every *other* Map key
(`String`, `FilePath`, `ValueId`→`int`) conforms to `Hashable` **unconditionally**.

**Fix: key that map on a content hash instead of raw bytes.** shv2 already has
`ContentHash.maxon` (FNV-1a). Keying on the hash is faster than hashing a `ByteArray`
through a witness *and* it deletes conditional conformance + per-gid thunks from the
core. **Collision guard (required):** a 64-bit FNV-1a collision would silently alias two
different constants to one rdata label — a silent miscompile, the exact class the
Stage-0 trap table exists for. So the value keeps the original bytes:
`Map with (ContentHash, DedupEntry)` where `DedupEntry = {bytes, label}`, byte-equality
verified on every hit, `panic` on mismatch. This is the "restrict shv2's source to core"
discipline paying rent — and nobody had budgeted for actively rewriting shv2's source to
fit core.

**The same discipline applies to closures:** shv2 has **7** escaping closure sites
(4× `logDebug`, 2× `logInfo`, 1× `logError`), all of the shape
`log*(cat, message: function() gives "…{captured}")` against
`Logger.maxon:35 typealias LazyMessage`. Rewrite them as `if logEnabled(cat) …` guards
(~30 lines) and the entire closure cone leaves the critical path.

---

## Maxon-core — the language boundary

Bounded by *measurement against shv2's own 7,961-line source*, not by guess:

| Construct | Sites in shv2's source | Verdict |
|---|---:|---|
| `throws` / `try` / `otherwise` | 102 in Lexer alone | **IN** |
| `for-in` | 97 | **IN** — hardcoded, which is what keeps associated types out |
| ranged `typealias` | 33 | **IN** (~800 localized lines; also a repo code-quality rule) |
| `Map with (K,V)` | 12 typealiases | **IN** (simplified) |
| generic decls + instantiation | `IrModule uses Op`; 40+ `Array with X` | **IN** — *pervasive* |
| `union` + `match` | 21 | **IN** |
| enum with struct `rawValue` | the whole `StdOpMeta` design | **IN** |
| interfaces | only `implements Error` (9×) — an **empty marker** | **IN, static only.** Witness tables are needed for the *stdlib* (`Hashable`), not shv2's code |
| string interpolation | pervasive | **IN** |
| **closures** | **7** | **OUT** — rewrite the 7 sites |
| **conditional conformance** | **1** (`GlobalDedupMap`) | **OUT** — rewrite the 1 site |
| `Set` | 1 | **OUT** — rewrite the 1 site (`DroppedNameSet`, [IrModule.maxon:18](maxon-shv2/Compiler/IR/IrModule.maxon#L18)) at 0.5 |
| tuples · `extension` · `async` · sort/higher-order | **0** | OUT |

**Also OUT** (Stage 4): general `Iterable`/associated types · arm64/wasm/macOS/Linux ·
coverage · inliner.

---

## Stage 0 — The loop (before any new language feature)

shv2 has **no test runner**. `specs-shv2/basics.md` exists but *nothing executes it* —
and its `status: selfhosted` frontmatter makes the C# runner skip it outright. DEVLOG's
`[x] M1 … Spec specs-shv2/basics.md` is **false**; M1 was hand-verified. Nothing else is
safe until this exists.

- **0.1 `maxon-shv2 spec-test`** (~665 lines new). New
  `maxon-shv2/Testing/{SpecParser,SpecRunner}.maxon`; grow `Compiler.maxon` with
  `compileSource()` + `CompileOutcome` (returns `project.diagnostics` instead of printing
  them — `Diagnostic.render()` already emits the exact `error EXXXX:` wire format
  `maxoncstderr` compares against). Block types: ` ```maxon `/` ```exitcode `/
  ` ```stdout `/` ```maxoncstderr ` only. **Do NOT port v1's 7,699-line harness** — shv2
  won't be able to compile a line of it for a year.
- **0.2 `MAXON_STDLIB` override** in
  [0-Compiler.cs:846](maxon-sharp/Compiler/0-Compiler.cs#L846) `FindStdlibPath()` (~50
  lines C#). Default path byte-for-byte unchanged so v1 and the 273-spec suite cannot
  regress.
- **0.3 `stdlib-shv2/stdlib/`** — the pruned fork. The bootstrap parses **48 files /
  11,007 lines** today (`SearchOption.AllDirectories`; only `Internals.maxon` excluded) —
  the 15 `helpers/` files (~3,046 lines) count too, and mostly must stay
  (MonomorphizationPass hardcodes the `stdlib.helpers.itertools.` /
  `stdlib.helpers.string.` prefixes; the UCD machinery lives there). Dropping the 14
  top-level files Json, URL, Math, Set, Http, Console, Sha256, List, Tcp, Range, Log,
  Unicode, Vector, Sleep (−3,575 lines) leaves **19 top-level files (~4,386 lines) +
  helpers ≈ 7.4k of 11k — a ~32% cut** of stdlib frontend work per build (not the 45% an
  earlier draft claimed from top-level-only accounting). Keep the marker-interface
  providers, `PrimitiveExtensions`, `Print` + `PrintError` (`print`/`printError` live
  there, NOT in the dropped Console — Main and every diagnostic path need them),
  `Process` (it holds `ExitCode`), `Build`, `Subprocess` + `Clock` (the runner needs
  them). Dropping `Range` is safe: for-in over `a to b` desugars directly to a while
  loop (per Range.maxon's own header) and shv2 never uses a range in expression
  position. The fork carries no `Internals.maxon` at all (both compilers exclude it —
  see the runtime-binding decision). Sub-task: audit the 15 `helpers/` files and drop
  any unreachable from the kept set.
- **0.4 `maxon-shv2 selfhost-distance`** (~485 lines) — the compass. See below.
- **0.5** Rewrite the 9 core-violating sites: the 7 closures (`if logEnabled(cat)`
  guards), the `GlobalDedupMap` key (ContentHash + byte-verify), and the one `Set`
  (`DroppedNameSet`, [IrModule.maxon:18](maxon-shv2/Compiler/IR/IrModule.maxon#L18) →
  `Map with (String, bool)`). Delete `LazyMessage`.
- **0.6** One command: build → spec-test → distance.

**Gate:** `spec-test` green on `basics.md` — the first time M1 is verified by anything
but hand.

### Stage-0 traps (each is a silent miscompile, not an error)

| # | Trap | Consequence |
|---|---|---|
| 1 | **The stdlib dir's LEAF NAME *is* the namespace** ([2-Parser.cs:804](maxon-sharp/Compiler/2-Parser.cs#L804)), and `"stdlib."` is hardcoded in [MonomorphizationPass.cs:754,522,619](maxon-sharp/Compiler/MLIR/Passes/MonomorphizationPass.cs#L754) | A dir named `stdlib-shv2` ⇒ namespace `stdlib-shv2` ⇒ monomorphization silently misses every call site. **The path must be `stdlib-shv2/stdlib/`**, and `FindStdlibPath()` must hard-error if the leaf isn't `stdlib`. |
| 2 | UCD `.bin` files resolve from `<stdlib>/helpers/string/` ([MaxonToStandardConversion.cs:2629](maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L2629)) | Pruned stdlib without `ucd_bmp.bin`/`ucd_supp.bin` ⇒ hard throw mid-lowering |
| 3 | `_cachedSources`/`_cachedStdlibModule` are **process-static** ([0-Compiler.cs:803](maxon-sharp/Compiler/0-Compiler.cs#L803)) | Use an **env var, not a per-invocation flag** — the C# TestRunner batches many compiles per process and would serve whichever stdlib loaded first |
| 4 | `__Managed*Error` case ordinals must match `Builtins.maxon` **exactly** ([2-Parser.cs:1116](maxon-sharp/Compiler/2-Parser.cs#L1116)) | Reordering a case re-points builtin `throwsType` at the wrong ordinal |
| 5 | `ExitCode` lives in **`Process.maxon`**, not where you'd guess | Drop it and `main() returns ExitCode` won't resolve |
| 6 | Spec fragments need a **trailing newline** ([SpecTestRunner.maxon:1045](maxon-selfhosted/Testing/SpecTestRunner.maxon#L1045)) | Every spec test dies with a lexer EOF error |
| 7 | **`panic()` is not catchable** — 148 sites in shv2 | `selfhost-distance` crashes instead of counting. **Audit all 148 now** (`INVARIANT` = keep vs `NOT-YET-IMPLEMENTED` = record-and-recover); by mid-Stage-2 there will be ~600. Record-and-recover on the resolve/lower/emit paths means threading `throws` through those call chains — plumbing well beyond parser recovery, NOT covered by 0.4's ~485-line reporter estimate. |
| 8 | A recovery-mode parse artifact **must not enter the query memo** (the parse query: [Queries.maxon:58](maxon-shv2/Compiler/Queries.maxon#L58); the memo tables: `QueryDatabase.maxon:70-72`) | `selfhost-distance` poisons the cache |
| 9 | *(inverse)* `BuildCache` is **already** stdlib-path-keyed ([BuildCache.cs:52](maxon-sharp/BuildCache.cs#L52)) | Don't "fix" it. But never set `MAXON_STDLIB` globally — v1's own `findStdlibPath()` ignores it and the two compilers would diverge. |

### The compass — `selfhost-distance`

`stageUnits = filesLexed + filesParsed + funcsResolved + funcsLowered + funcsEmitted` —
the *reported* compass number. Monotone within a run (each term counts only *successes*,
and a unit reaches stage *k+1* only if stage *k* passed) — but **NOT across commits**:
the denominator is shv2's own source, and legitimate refactors shrink it (the M1-post
MIR-tier deletion removed whole files). So the CI ratchet is **per-unit non-regression
on surviving units**, not a scalar comparison: every `(file|func, stage)` pair that
passed at the previous commit and still exists must still pass. New units may fail
(that's the roadmap); deleted units drop out; a passing unit that regresses fails CI.
That per-unit rule is the *only* thing that actually enforces "shv2's source stays in
core."

```
SELFHOST DISTANCE   43
  files  lexed 39/39   parsed 4/39      funcs  resolved 0/612  lowered 0/612  emitted 0/612
  FIRST BLOCKING:  Lexer.maxon:5:1  top-level `typealias`
  TOP UNSUPPORTED (1,844 sites):   412 `let`   288 type decl   201 binary op   174 `if`
```

The ranked table **is** the roadmap, and it reprioritizes itself after every milestone.
Prerequisites (both wanted anyway): a parser **recovery mode** — Maxon's mandatory
labeled `end '<label>'` makes `skipToMatchingEnd` ~30 lines rather than a heuristic —
and the panic audit (trap 7). Report the pair `(SHD, specs_passing)`: SHD=0 means shv2
*accepts* its source, not that it compiles it *correctly*.

---

## Stage 1 — Scalar core (port, don't design)

`let`/`var`/block scope · full-Pratt arithmetic/comparison/unary · `if`/`while`/`break`/
`continue` · functions with params + calls.

**The one design item: the real register allocator.** Register allocation is **~74% of
v1's self-compile wall time** (~418 s of 561 s) against shv2's ≤30 s *whole-compile*
budget. Build it here with **sub-phase timers in the first commit** — v1's "74%" stood
for months with no sub-phase attribution. Do not copy v1's: its reactive spill/color loop
rebuilds the interference graph every iteration.

---

## Stage 2 — Every hard mechanism, minimal + integrated

**Dictionary-passing forces this order, and it is not the order in the current PLAN.md:**

- **Strings and arrays *cannot* precede generics.** `String` **is** `__ManagedMemory with
  Byte` ([String.maxon:29](stdlib/String.maxon#L29)); `Array` is a generic declaration
  that instantiates a generic *over its own type parameter*
  ([Array.maxon:14](stdlib/Array.maxon#L14)).
- **Ownership must follow generics, not precede it.** `own.drop` on a type-parameter
  value cannot name a static `__destruct_T` — it must route through the descriptor's
  `destroyFunc@40`. v1 bolted this on as a *second, separate release domain*
  (`sys.dropTypeParam`, alongside the 7,755-line `InsertRefcounts`). Landing generics
  first means `InsertDrops` is **descriptor-aware from its first commit** instead of
  retrofitted.

| # | Mechanism | Note |
|---|---|---|
| 2.1 | structs · enums · unions · `match` | concrete, trivial-ownership only |
| 2.2 | **generics** ⭐ | declarations + instantiation + layout descriptors; over scalars first |
| 2.3 | **interfaces + witness tables** | static conformance (`Hashable`). **No** conditional conformance, **no** existentials — shv2 stores nothing at interface type |
| 2.4 | **`__ManagedMemory` + heap + ownership + drops** ⭐ | THE CRUX. `own.drop` descriptor-aware from commit 1. Runtime slice **R1** lands here (Workstream R) — mm-trace gates from here and cannot run without it |
| 2.5 | moves + borrows (NLL) | first program-rejection point |
| 2.6 | escape → `shared` | the **only** place refcounts appear. **Track `% values promoted to shared`** — if it's 40%, static ownership bought nothing |
| 2.7 | **`Array`** | = 2.2 ∘ 2.4 — the first real integration proof |
| 2.8 | **`String`** + interpolation | = Array-of-Byte + `BuiltinStringLiteral`; runtime slice **R2** |
| 2.9 | owned payloads in enums/unions | |
| 2.10 | **`Map`** | multi-param generics + `Hashable` constraint |
| 2.11 | hardcoded `for-in` | Array / Range / String |
| 2.12 | **errors** | `throws`/`try`/`otherwise`; drops on the error edge. v1's dual-register flag verbatim |
| 2.13 | ranged typealiases | `ExpandCastRangeChecks` + `InsertRangeChecks` |

**Stage 2 stays a ladder of individually-shippable increments, one spec each.** Core-first
is a *re-ordering*, not a replacement — if that isn't explicit, Stage 2 becomes a
six-month integration hole with no green build.

---

## Workstream R — the runtime shv2 must EMIT ⚠

**~5–7k lines, on the critical path — and a workstream, NOT a stage.** shv2-compiled
binaries carry the runtime *shv2's backend hand-assembles*; a self-compiled shv2 cannot
run without it. Sequencing it *after* Stage 2 would contradict 2.4's own mm-trace gate,
so it lands in slices, each WITH the Stage-2 milestone that first needs it:

- **R1 @ 2.4:** slab allocator · `__mm_incref`/`__mm_decref` · the `__destruct_*`
  cascade · `__ManagedMemory` · the DebugStream producer (schema-compatible port of
  `RuntimeEmitter.DebugStream.cs`) — this is what lets mm-trace gate 2.4 onward. v1's
  force-seeded bootstrap roots (`__slab_init`, `__mm_alloc`, `__managed_mem_create`, …)
  become a real obligation here.
  **The allocator R1 emits must ALWAYS RETURN ZEROED MEMORY, from commit 1** — it is a
  property of the allocator, not a thing each caller remembers. Non-zeroing alloc cost
  v1 at least three separately root-caused bugs (the `__gt_spawn` `cancel_flag`
  deadlock, the socket `OVERLAPPED.hEvent` IOCP hang, and `mrt_alloc`'s Map/Set hash
  tables decref'ing garbage), each "fixed" by bolting a zeroing loop onto the caller.
  Retrofitting the guarantee later means re-auditing every raw-buffer call site.
  See **ARCHITECTURE.md → "Allocator: the zeroing contract"** for the full design
  (Go's `needzero` model, the bump cursor, `__slab_alloc_raw` + its audit rule, and the
  memzero size ladder R1's backend must emit).
- **R2 @ 2.8:** string runtime (`BuiltinStringLiteral` backing, UCD table access).
- **R3 @ Stage 5 (latest):** the GT scheduler (`emitX64Gt*` port; allocator mirrored
  from the C# sharded design, not v1's single-shared-mcache). A single-threaded
  self-compiled shv2 is acceptable through Stages 3–4 — shv2's own source uses no
  `async` until per-function fan-out arrives at Stage 5.

Per the runtime-binding decision (Context): shv2 **excludes `Internals.maxon` and emits
natively** — builtin registration for the `__Managed*` surface replaces v1's
`__Internals` mechanism + `StdlibLoader` (6,434 lines, the road not taken).

## Stage 3 — Self-host

shv2 compiles shv2 → **3-stage bootstrap fixpoint** (stage-2 == stage-3, byte-identical);
stage-2 shv2 passes the whole `specs-shv2/` suite. **Byte-identity is a cliff, not a
ramp** — it demands determinism in rdata ordering, hash iteration order, name mangling,
and float formatting. **Gate byte-identity from Stage 1 on the toy corpus**, the way the
plan already gates 1-core-vs-N-core.

## Stage 4 — Broaden
async/green threads · closures · general `Iterable` + associated types · conditional
conformance + per-gid witness thunks · `Set`/`List`/Json/… · arm64 + wasm · coverage ·
inliner · port the 273-file / ~2,573-case spec suite.

## Stage 5 — Budgets
Parallel per-function fan-out at scale; **≤30 s / ≤1.7 GB / >90% CPU**. Runtime multi-core
is already proven (Track 0). **Caching is revisited here, on a working compiler, with the
approach chosen fresh** — not ported from v1's `.mxc`.

---

## Scope

| | v1 | shv2-core (est.) |
|---|---:|---:|
| Parser | 21,862 | 6–8k |
| TypeResolution | 12,142 | 4–5k |
| LowerMaxonToStd | 16,135 | 6–8k |
| Memory model (`Own/*` vs `InsertRefcounts`) | 7,755 | 3–4k |
| Std passes | ~10k | 5–6k |
| x64 backend + emitters | 16,030 | 8–10k |
| Register allocator | 8,520 | 4–6k |
| **Workstream R (emitted runtime)** | *(inside X64Backend)* | **5–7k** |
| Testing | 7,699 | ~665 |
| **Total** | **191,487** | **~50–65k** |

Current: **7,961**. Self-compile is **~45–55k lines away**. That is the project.

---

## Verification

- **Per milestone:** `maxon-shv2 spec-test` stays green; ownership milestones (2.4+) also
  assert an `mm-trace` block via `maxon monitor`.
- **Continuous:** the `selfhost-distance` **per-unit ratchet** — no surviving
  `(unit, stage)` pass may regress (the scalar may legitimately shrink on refactors; see
  the compass) — this is what enforces "shv2's source stays in core." Track `% values
  promoted to shared` alongside it.
- **From Stage 1:** byte-identity on repeat compiles, so Stage 3's fixpoint is a ramp.
- **Stage 3:** stage-2 == stage-3 byte-identical; stage-2 shv2 passes the suite.
- **Stage 5:** ≤30 s / ≤1.7 GB / >90% CPU on self-compile.

## Critical files

- **New:** `maxon-shv2/Testing/{SpecParser,SpecRunner}.maxon`;
  `maxon-shv2/Compiler/SelfhostDistance.maxon`; `maxon-shv2/Compiler/IR/LayoutDescriptor.maxon`;
  `maxon-shv2/Compiler/IR/Own/{OwnDialect,OwnershipInfer,OwnershipCheck,EscapeAnalysis,InsertDrops}.maxon`;
  `stdlib-shv2/stdlib/` (leaf name **must** be `stdlib`).
- **Modified:** [0-Compiler.cs:846](maxon-sharp/Compiler/0-Compiler.cs#L846) (`MAXON_STDLIB`);
  [Main.maxon](maxon-shv2/Main.maxon) (`spec-test`, `selfhost-distance`);
  [Parser.maxon](maxon-shv2/Compiler/Parser.maxon) (recovery mode; grows every milestone);
  [Compiler.maxon](maxon-shv2/Compiler/Compiler.maxon) (`compileSource`);
  [GlobalDataTable.maxon:23](maxon-shv2/Compiler/Targets/Shared/GlobalDataTable.maxon#L23) +
  [Logger.maxon:35](maxon-shv2/Compiler/Logger.maxon#L35) +
  [IrModule.maxon:18](maxon-shv2/Compiler/IR/IrModule.maxon#L18) (the 9 core-violating sites);
  [maxon-shv2/PLAN.md](maxon-shv2/PLAN.md) (replaced by this; keep a stage↔M mapping so
  DEVLOG's M-numbered ledger stays interpretable).
- **Reference (read, don't copy):** `maxon-selfhosted/Compiler/IR/LayoutDescriptor.maxon`,
  `Compiler/Passes/BuildWitnessTables.maxon`, `Compiler/IR/Std/InsertRefcounts.maxon`.
