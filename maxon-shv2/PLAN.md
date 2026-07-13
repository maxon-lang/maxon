# maxon-shv2 — The Two-Phase Plan

> **THE SPINE (user-set, 2026-07-13). Two goals, in order:**
>
> ### 🚩 **PHASE 1 — shv2 runs its own spec tests**, the way `maxon-selfhosted` does:
> ### **shv2 compiles its own spec harness**, and the shv2-compiled harness runs
> ### `specs-shv2/` — **IN PARALLEL, on a green-thread worker pool.**
> ### *(The mini-bootstrap. Formerly "Gate H".)*
>
> ### 🚩 **PHASE 2 — shv2 fully self-hosts.** shv2 compiles shv2; 3-stage bootstrap
> ### fixpoint (stage-2 == stage-3, byte-identical).
>
> Everything below is subordinate to those two. Phase 1's boundary is **measured**, not
> guessed (§"Phase 1 — the measured boundary"); Phase 2 is *"Phase 1, plus exactly what
> shv2's own source adds."*
>
> **Why this split earns its keep:** the harness is a **real, already-trusted, nontrivial
> Maxon program — ~30× smaller than the compiler**. A miscompile surfaces at 1/30th the
> debugging cost, on code we already know is correct. It is the nearest point at which
> "does shv2 compile real Maxon?" gets an answer, and it comes with a **free differential
> oracle** (below). Self-host is the back half of the project no matter how it is sequenced;
> Phase 1 is the checkpoint that keeps that from being a six-month integration hole.

---

## History of this document

- **ADOPTED 2026-07-11** — replaced the original M1–M18 plan (in git history). Core-first;
  load-bearing consequence: **generics before ownership** (`64b4dd60e`).
- **AMENDED 2026-07-13 (a)** — the `selfhost-distance` compass is **CUT**; `Set` reclassified
  into core.
- **AMENDED 2026-07-13 (b)** — merged the forked core-first draft. `String` is a **BUILTIN**
  gated on the runtime, not on generics; **closures and conditional conformance are IN core**;
  errors moved up.
- **RESTRUCTURED 2026-07-13 (c) — THIS REVISION.** Re-spined into the two phases above.
  Milestones renumbered `P1.x` / `P2.x`. **Three boundary corrections, each grep-verified
  against the harness — the previous revision's "Gate H" table was wrong on all three:**
  1. **Owned payloads in unions are PHASE 1**, not deferred — `SpecExpectation.compilerError(text String)`
     ([SpecParser.maxon:54](maxon-shv2/Testing/SpecParser.maxon#L54)) and
     `TestOutcome.fail(reason String)` ([SpecTestRunner.maxon:71](maxon-shv2/Testing/SpecTestRunner.maxon#L71))
     each carry a **managed `String` payload**.
  2. **Ranged typealiases are PHASE 1**, not last — `LineIndex = int(0 to u64.max)`,
     `ExitCodeValue = int(i64.min to i64.max)`
     ([SpecParser.maxon:41,46](maxon-shv2/Testing/SpecParser.maxon#L41)). Cheap here: the
     ranges are wide, so the checks are near-vacuous — but the *mechanism* must exist.
  3. **Extensions are NOT used by the harness** (the draft claimed 2; grep says **0**). They
     are forced only by the *stdlib* cone, at declaration level.
  And the finding that makes the split real: **§"The stdlib cone" — the EMIT/DECLARE split.**
- **AMENDED 2026-07-13 (d) — `async` / GREEN THREADS ARE IN CORE, IN PHASE 1.** Running the spec
  suite means running it **in parallel** — that is what `maxon-selfhosted` does
  ([SpecTestRunner.maxon:3401](maxon-selfhosted/Testing/SpecTestRunner.maxon#L3401)
  `runAllSpecTestsParallel`: a persistent worker-subprocess pool driven by `async`/`await` +
  Promises over the green-thread runtime), and it is part of the Phase-1 goal. **So `async` leaves
  "Beyond" and enters core, and Workstream R3 (the GT scheduler) is UN-DEFERRED into Phase 1.**
  shv2's current harness is serial *by deliberate omission*
  ([SpecTestRunner.maxon:11](maxon-shv2/Testing/SpecTestRunner.maxon#L11): *"Trimmed HARD from v1's
  SpecTestRunner: no fragment cache, no parallel worker pool"*) — Phase 1 grows that pool back.
  **The load-bearing consequence: async CO-LANDS with closures and escape at P1.5** — a value
  captured into a green thread **escapes**, exactly as a closure capturing into a heap env block
  does. Building escape analysis single-threaded and adding async later bolts a *second capture
  channel* onto `EscapeAnalysis` — v1's `sys.dropTypeParam` split-brain mistake, precisely.

**Stage ↔ old-M mapping** (so DEVLOG's M-numbered ledger stays interpretable): old M1–M5 =
**Stage 1 / scalar core, DONE**. Old M6–M15 = the mechanism ladder, now split across
**P1.1–P1.9** and **P2.1–P2.5**. Old M16–M17 = **Phase 2's fixpoint**. Old M18 = **Beyond /
budgets**. Old "Stage 0" (tooling) is **CLOSED**. Old "Stage 4" (broaden) is **Beyond**.

---

## Context

`maxon-shv2` is the ground-up rewrite of `maxon-selfhosted` (v1, 191,487 lines). shv2 is at
**21,038 lines**, with a working `spec-test` runner (**specs-shv2 126/0 across 19 files**), a
warm-rebuild determinism gate, the full scalar core, and a **linear** SSA-chordal register
allocator. The bones are good — content-hash query spine, parse-staging, 3 IR tiers
(Maxon→Std→Target, no MIR), flat `StdOp`, x64/PE backend.

**The scalar core is DONE. The mechanism ladder is the live work, and the next step is
P1.1 (structs).**

> **Note the two senses of "shv2 runs its spec tests."** shv2 *already* has a `spec-test`
> command (126/0) — but that harness is compiled by **`maxon.exe`**, the C# bootstrap. Phase 1
> is the other sense, the one `maxon-selfhosted` has and shv2 does not: **shv2 compiles the
> harness itself.** That is the whole difference, and it is the entire mechanism ladder.

**The goal:** implement every hard mechanism of Maxon *minimally*, so the compiler can be built
and validated with fast iterations.

### The honest sizing

Self-host is **~30–45k lines of new Maxon** away (from 21,038 today), and "Maxon-core" is
**~75–80% of the language**, not 25% — it excludes only async, general `Iterable`/associated
types, and higher-order/sort. **That is exactly why Phase 1 exists.**

**Two items on the path are unbudgeted in the original plan documents:**

1. **The runtime shv2 must *emit*** (~5–7k lines of hand-assembled machine code). A
   shv2-compiled binary has to *run* — so shv2's backend must emit the slab allocator,
   refcounting, `__destruct` cascades, and `__ManagedMemory`. The original plan *scheduled*
   this but budgeted zero lines for it. Here it is **Workstream R**, sliced into Phase 1
   (**R1 @ P1.2** — the first milestone that cannot even be *gated* without it).
2. **The runtime-binding decision.** v1 binds its runtime by *compiling* `stdlib/Internals.maxon`
   (3,862) through a file-path-gated `__Internals` intrinsic + `StdlibLoader.maxon` (2,572). The
   C# bootstrap instead *excludes* `Internals.maxon`
   ([0-Compiler.cs:879](maxon-sharp/Compiler/0-Compiler.cs#L879)) and emits its runtime natively.
   **Decision: shv2 follows the C# bootstrap** — exclude `Internals.maxon`, emit natively (that
   *is* Workstream R), register the `__Managed*` builtin surface. v1's 6,434 combined lines are
   the road not taken, not a budget line.

**Explicitly deferred: the stdlib cache.** v1's is ~6k lines (`.mxc`, format v89) and its
version history — **v81→v89, every bump a soundness bug** in id-space stability or hash coverage
— is a warning label. Revisited *after* the compiler works, approach chosen fresh. One cheap
hedge meanwhile: keep every id-producing table (`TypeNameInterner.foldInto`/`TypeNameRemap`)
serializable-in-order, so a future cache has a stable id space to build on rather than a retrofit.

---

## Locked decisions

| | Choice |
|---|---|
| **Generics** | **Dictionary-passing + 64-byte layout descriptors + witness tables** — v1's design. (Rejected: monomorphization. It contaminates 3 files instead of 20, but trades code size and compile time — exactly the resources the ≤30 s / ≤1.7 GB budget exists to conserve.) |
| **String** | **A BUILTIN, at P1.2** — gated on the RUNTIME, *not* on generics. It is the FIRST heap value. v1 shipped String in Phase 7; generics in Phase 11. |
| **Closures** | **IN core** — co-landed with escape analysis at **P1.5**, because closure capture *is* the canonical escape. The `LazyMessage` sites are kept as dogfood. |
| **`async` / green threads** | **IN core, at P1.5** *(reversed 2026-07-13 — was "Beyond")*. Parallel spec execution is part of the Phase-1 goal, and a green-thread capture **IS** an escape — so it co-lands with closures + escape, never after. Brings **Workstream R3 (the GT scheduler) into Phase 1.** Its dogfood + acceptance test is the parallel harness; it also un-blocks **P2.6** per-function fan-out, which was always going to need it. |
| **Conditional conformance** | **IN core** — *declared* in Phase 1 (the stdlib forces it), *emitted* at **P2.2**. It is a hard mechanism, and this plan implements hard mechanisms rather than dodging them. See the PRINCIPLE. |
| **`Map` / `Set`** | multi-param generics + one `Hashable` constraint. No `Iterable`-on-Map, no tuple `Entry`. **P2.3.** `Set` rides Map's exact mechanism. |
| **Iteration** | **Hardcode `for-in`** over Array/Range/String. No general `Iterable`/associated types. |
| **Errors** | v1's design **verbatim**: dual-register `(value, errorFlag)`. No unwind tables. Already minimal. |
| **Stdlib lowering** | **Reachability-seeded — lower only the stdlib bodies the program transitively reaches.** *Not an optimization: it is what makes Phase 1 a phase.* See §"The stdlib cone." |
| **Stdlib fork** | `stdlib-shv2/stdlib/` — **un-deferred**, and re-justified: it is the backstop on Phase 1's boundary, not a ~1 s speedup. See §"The stdlib cone." |
| **Targets** | x64-windows only through Phase 2. |

### PRINCIPLE — when may we rewrite shv2's own source?

> **Rewriting shv2's source to dodge a mechanism is a SCOPE CUT, not a simplification.**
> Use it *only* for mechanisms deliberately deferred as out-of-scope (async, wasm).
> **NEVER** for a mechanism that is merely *hard* (closures, conditional conformance) —
> that is the whole point of this plan.

shv2's own source is **the best test corpus available**: every construct it uses is a free,
real, integration-level test of a mechanism, written by someone who wasn't trying to test it.
Deleting a construct to avoid implementing it costs twice — it defers the mechanism *and*
throws away the test.

This is why `LazyMessage` stays (closure dogfood) and `GlobalDedupMap` stays as
`Map with (ByteArray, String)` (the conditional-conformance acceptance test). **Do not re-key
it on a `ContentHash`.**

**The same trap, freshly walked into and rejected (2026-07-13) — the "cheap parallel runner."**
`stdlib/Subprocess.maxon` exposes a split spawn/wait API (`spawn()` → a live `StreamingSubprocess`
handle, `wait()`, `waitWithTimeout()`). It is therefore *possible* to parallelize the spec suite
with **no green threads at all**: spawn N children over disjoint test shards, have each write its
results to a file (so no pipe ever fills and deadlocks), then blocking-`wait()` each in turn — the
children run concurrently, so wall time is `max`, not `sum`. **~80 lines, zero new mechanisms.**
**REJECTED.** It buys a fast test run and *defers the single most retrofit-hostile mechanism left
in the language*. Green-thread capture is an **escape**; discovering that after `EscapeAnalysis`
ships single-threaded means retrofitting it with a second capture channel. **Do the hard thing
early: implement `async`.** The parallel harness is not a task to be completed by the cheapest
route — it is `async`'s **dogfood and acceptance test**, exactly as `LazyMessage` is for closures.

The principle applies to **shv2's source, not the stdlib.** Pruning the stdlib *fork* is
explicitly sanctioned — see below.

`Set` is the counter-example that proves the boundary, and it is the signal the cut compass told
us to watch for: rewriting it was cheaper than implementing it right up until it wasn't
(`StdValueUseSet` is now load-bearing in four Std passes). It is in core at P2.3. **If a second
such case appears, re-open the compass decision.**

---

## ⭐ STRING IS A BUILTIN — gated on the RUNTIME, not on generics

**The single biggest ordering correction in this plan.** An earlier revision put `String` after
generics and `Array`, on the premise that `String` **is** `__ManagedMemory with Byte`. **That
premise is false**, and the whole Phase-1 ladder depends on it being false.

**v1 shipped `String` in Phase 7. Generics + layout descriptors landed in Phase 11** — four
phases later. The Phase-7 bootstrap declaration (commit `5fc3b569a`, `StdlibLoader.maxon:74`)
was, in full:

```maxon
export interface BuiltinStringLiteral
end 'BuiltinStringLiteral'                  // EMPTY marker — no `init` requirement

export type String implements BuiltinStringLiteral
	export var managed __ManagedMemory      // BARE — not `__ManagedMemory with Byte`
	export var isAsciiFlag bool
end 'String'                                // ZERO methods
```

…and string literals, interpolation, and `print` all worked. `stdlib/String.maxon` was not
compiled at all.

**Why it works — carry this over as deliberate design.** `__ManagedMemory` is a
compiler-registered, **hardcoded 40-byte struct** — verified at
[LowerMaxonToStd.maxon:1523](maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon#L1523)
(`registerManagedMemoryStruct`): `buffer@0, length@8, capacity@16, element_size@24,
parent_ptr@32`, every field `offsetResolved: true`, stamped with the synthetic source path
`stdlib-bootstrap.maxon`. **`element_size` is a RUNTIME FIELD.** Element-size polymorphism is a
data field, not a compile-time dictionary — *that single decision is why String needs no layout
descriptor.*

`let s = "hello"` lowers to: an rdata blob (`__istr_<id>`, NUL-terminated) → a
`__ManagedMemory` with **`capacity = -2`** (the rdata sentinel: the destructor must not free
`.rdata`) → a 16-byte `String` envelope `{mm@0, isAscii@8}` via
`__mm_alloc(16, &__destruct_String)`, classified `freshRc0`. **Zero stdlib calls.**
`__destruct_String` is *compiler-synthesized* from the registered field list, not a stdlib body.
Interpolation is a **compiler-emitted memcpy chain** (`mrt_alloc` + `memcpy` per part), not a
stdlib `concat`.

**Minimal builtin surface:** `MaxonType.string` primitive arm · `MaxonOp.stringConst` +
`stringInterp` · rdata interning (shv2 **already has this** — `GlobalDataTable`) · hardcoded
`__ManagedMemory` + `String` structs · marker-interface discovery · synthesized
`__destruct_String` · the MM runtime (**Workstream R1**).

**Two things to carry over, one to fix:**
- **Carry:** the dual representation — `MaxonType.string` (pre-discovery tag) ↔
  `MaxonType.named(String)` (post-discovery), with `builtinStringLiteralType()` falling back to
  `MaxonType.string`. This is what lets the parser synthesize string-typed IR *before* the stdlib
  decl has been seen.
- **Carry:** `element_size` inside the `__ManagedMemory` record (above).
- **FIX:** v1 **panics** when no type implements `BuiltinStringLiteral`
  ([TypeResolution.maxon:417](maxon-selfhosted/Compiler/TypeResolution.maxon#L417)). The C#
  bootstrap raises a proper `CompileError`
  ([2-Parser.cs:15615](maxon-sharp/Compiler/2-Parser.cs#L15615)). **Emit a real diagnostic.**

**Generics-before-ownership is REFINED, not reversed.** That decision's real intent was *"never
retrofit the descriptor drop path."* That intent binds **`Array`** (managed elements), not
**`String`** (trivial elements). We keep it by **declaring `own.drop` with BOTH arms at P1.2** —
the concrete arm (`__destruct_T`) live immediately, the descriptor-mediated arm
(`destroyFunc@40`) declared but unreachable until generics at P1.6. `LayoutDescriptor` is 218
lines in v1; defining it early costs almost nothing and buys the whole design-in.

**Deferred to generics — exactly two string-adjacent things:**
- **`b"…"` byte-string literals** — `lowerByteStringLiteral` interns an `Array with Byte` gid and
  *requires* a layout descriptor. The **only** string op genuinely gated on generics ⇒ **P1.7**.
- **`"{userStruct}"`** interpolation — the `stringable` arm dispatches through a witness table
  ⇒ **P2.1**. *(The harness interpolates only primitives and `String` — grep-verified — so
  Phase 1 needs only the builtin arms.)*

---

## Phase 1 — the measured boundary

**Grep-verified against `maxon-shv2/Testing/` (704 lines: `SpecParser` 297 + `SpecTestRunner`
407), not guessed.** The harness is *already* architecturally standalone:
`runSpecDir(compilerExe FilePath, specDir, …)`
([SpecTestRunner.maxon:116](maxon-shv2/Testing/SpecTestRunner.maxon#L116)) takes the compiler as
a **parameter** and spawns it as a subprocess — deliberately, so "a compiler crash is isolated to
the one test that triggered it." Extracting a standalone `spec-runner` program is nearly free.

| The harness **USES** (⇒ Phase 1 must EMIT) | The harness does **NOT** use |
|---|---|
| structs · enums · **unions** · `match` | **`Map`** — **0** uses |
| **`String`** + interpolation of **primitives/String only** (0 struct interpolation) | **`Set`** — **0** uses |
| heap · ownership · drops | **witness-table dispatch** — 0 `implements`, 0 struct interpolation |
| **owned `String` payloads in union cases** — `compilerError(text String)`, `fail(reason String)` | **conditional-conformance dispatch** — every `.contains` is `String.contains`, not `Array.contains` |
| `throws` / `try` / `otherwise` — **36 sites** | **`extension`** — **0** uses *(the draft said 2)* |
| **generics** — `Array with {SpecTest, SpecTestResult, FilePath}` ⇒ **managed elements** | the entire compiler cone — no IR, no allocator, no backend |
| **`Array`** | |
| `for-in` — **5** sites | |
| **ranged typealiases** — `int(0 to u64.max)`, `int(i64.min to i64.max)` | |
| stdlib: `File` · `FilePath` · `Directory` · `Subprocess` · `Clock` · `Process` · `Print` | |
| ⚠ **`async` / `await` / Promises + closures** — **from the parallel worker pool** (below) | |

> **⚠ The table above is measured against the CURRENT 704-line harness, which is SERIAL — and
> that is not Phase 1's target.** shv2's harness is serial by *deliberate omission*
> ([SpecTestRunner.maxon:11](maxon-shv2/Testing/SpecTestRunner.maxon#L11): *"Trimmed HARD from
> v1's SpecTestRunner: no fragment cache, no parallel worker pool"*). The Phase-1 goal is parity
> with `maxon-selfhosted`, whose
> [`runAllSpecTestsParallel`](maxon-selfhosted/Testing/SpecTestRunner.maxon#L3401) is a
> **persistent worker-subprocess pool** driven by `async`/`await` + Promises over the
> green-thread runtime (`try await drainPromise otherwise 'workerDied`). **So the harness must
> first GROW that pool back (P1.0b), and the boundary is measured against the upgraded harness** —
> which adds `async`, `await`, Promises, closures, and async subprocess stdio to the EMIT set, and
> brings **Workstream R3** into Phase 1.
>
> *(For calibration, not as a reason to skip it: the serial suite is currently **3.0 s for 126
> tests**. Parallelism is not an iteration-speed emergency today — it is in Phase 1 because
> `async` is a hard mechanism that must not be retrofitted, and because it is what the Phase-1
> goal actually means.)*

### ⚠ The stdlib cone — the EMIT/DECLARE split, and why it decides everything

**Phase 1's boundary is NOT "what the harness uses."** shv2 parses **all 48 stdlib files**, and
the stdlib *declares* mechanisms the harness never dispatches through:

- [stdlib/Subprocess.maxon:120](stdlib/Subprocess.maxon#L120) — `typealias EnvMap = Map with String, String`.
  The harness **must** have `Subprocess` (it spawns the compiler under test) and calls
  `Subprocess.run(exe, arguments:)` — **never the env-map overload**.
- [stdlib/Array.maxon:406](stdlib/Array.maxon#L406) — `Array implements Hashable, Equatable where
  Element is Hashable and Equatable`. The harness has `Array with SpecTest` but never `==` or
  `.contains` on an Array.
- `stdlib/{Array,Interfaces,PrimitiveExtensions}.maxon` declare **`extension`** blocks the harness
  never calls into.

⇒ **The rule:**

> **Phase 1 = EMIT(what the harness uses) ∪ DECLARE(what the stdlib cone declares).**
> Declaration-level support — *parse it, resolve it, emit nothing* — is far cheaper than
> emission-level, and it is all `Map`, conditional conformance, interfaces, and `extension`
> need in Phase 1. **Phase 2 promotes each from DECLARE to EMIT.**

⇒ **The architectural obligation that makes the rule hold:**

> **shv2 must lower only the stdlib bodies a program transitively reaches** (reachability-seeded
> lowering, as both v1 and the C# bootstrap do). **This is not an optimization — it is what makes
> Phase 1 a phase.** If shv2 lowers whole files instead, `EnvMap` drags in `Map` → `Map` drags in
> the `Hashable` constraint → that drags in **interfaces + witness tables**, and Phase 2's entire
> generics tail collapses back into Phase 1.

⇒ **The backstop, and why the pruned fork is UN-DEFERRED:** `stdlib-shv2/stdlib/` was deferred on
2026-07-13 as "saves ~1 s of a 4.2 s build, gates nothing." **That justification is now false.**
The fork is the lever that *bounds Phase 1's language surface* — if reachability-seeded lowering
proves insufficient, pruning the `EnvMap` overload out of the fork is the fallback that keeps
`Map` in Phase 2. Its value is a *boundary*, not a second.
- **TRAP (unchanged, and fatal):** the stdlib dir's **LEAF NAME *is* the namespace**
  ([2-Parser.cs:804](maxon-sharp/Compiler/2-Parser.cs#L804)), and `"stdlib."` is hardcoded in
  [MonomorphizationPass.cs:754,522,619](maxon-sharp/Compiler/MLIR/Passes/MonomorphizationPass.cs#L754).
  A dir named `stdlib-shv2` ⇒ namespace `stdlib-shv2` ⇒ monomorphization **silently** misses every
  call site. **The path must be `stdlib-shv2/stdlib/`**, and `FindStdlibPath()` must hard-error if
  the leaf isn't `stdlib`.

**FIRST TASK OF PHASE 1 (before P1.1): measure the cone.** Compile the harness with `maxon.exe`
and list which stdlib functions actually get codegen'd. That list *is* Phase 1's true stdlib
surface, and it settles the `Map` question with data instead of argument.

---

## Phase 1 — the ladder

Each rung is individually shippable, with one spec. **Core-first is a *re-ordering*, not a
replacement** — if that isn't explicit, Phase 1 becomes a six-month integration hole with no
green build.

| # | Mechanism | Note |
|---|---|---|
| **P1.0a** | **grow the harness's parallel worker pool back** | **The acceptance target must exist before it can be a target.** Port `maxon-selfhosted`'s [`runAllSpecTestsParallel`](maxon-selfhosted/Testing/SpecTestRunner.maxon#L3401) worker-subprocess pool into `maxon-shv2/Testing/`. Written in Maxon, compiled by **`maxon.exe`**, green under today's gates — so it lands *now*, and every later rung is measured against the real Phase-1 target instead of the serial stub |
| **P1.0b** | **measure the stdlib cone** | against the **upgraded** harness. Cheap, and it sets the boundary — see above |
| **P1.1** | structs · enums · unions · `match` | concrete, trivial-ownership only. **← NEXT** |
| **P1.2** | **heap + ownership + drops + `String`** ⭐ | **THE CRUX, and String is the FIRST heap value** — real, needed by everything, and trivially-elemented so it forces no descriptor. Hardcoded `__ManagedMemory`(40B) + `String`(16B) bootstrap structs; rdata `capacity = -2` sentinel; synthesized `__destruct_String`; interpolation of **primitives**. Runtime slice **R1** lands here — mm-trace gates from here and **cannot run without it**. **`own.drop` declares BOTH arms now**; the descriptor arm is unreachable until P1.6 |
| **P1.3** | **owned payloads in enums/unions** | *moved into Phase 1* — `compilerError(text String)`, `fail(reason String)`. Needs only P1.1 + P1.2's drops. Errors (P1.4) want it too: the harness calls `e.displayReason()` |
| **P1.4** | moves + borrows (NLL) · **errors** | first program-rejection point. `throws`/`try`/`otherwise` (v1's dual-register `(value, errorFlag)`, verbatim) + drops on the error edge. **36 harness sites** |
| **P1.5** | **closures + `async` + escape → `shared`** ⭐⭐ | **THE THREE ARE ONE MECHANISM, AND THEY CO-LAND — this is the plan's "do the hard things early" in its purest form.** Capture-into-heap **IS** escape: a closure captures into an env block; a green thread captures into a task frame. Escape analysis is needed for heap correctness regardless — so build all three together and `EscapeAnalysis` gets **both** capture channels *from birth*. Land escape single-threaded and add `async` later and you bolt a **second capture channel** onto it: v1's `sys.dropTypeParam` split-brain mistake, exactly. Minimal closure = int capture, 0-arg, heap env, uniform `(args, env)` ABI (v1 lifts at parse time). Minimal `async` = `async`/`await` + Promise + the worker pool's needs. Escape is the **only** place refcounts appear. **Track `% values promoted to shared`** — if it's 40%, static ownership bought nothing. **Runtime slice R3 lands here** (the GT scheduler + async subprocess stdio) |
| **P1.6** | **generics + layout descriptors** ⭐ | declarations + instantiation. ⇒ `own.drop`'s descriptor arm goes **LIVE** here |
| **P1.7** | **`Array`** | = P1.6 ∘ P1.2 — the first real integration proof (managed elements → element-destroy through the descriptor). ⇒ unlocks **`b"…"` byte-string literals** |
| **P1.8** | `String` methods · `for-in` | real `String.equals` body (struct-`cmp` → `methodCall`); hardcoded `for-in` over Array/Range/String |
| **P1.9** | **ranged typealiases** | *moved into Phase 1* — `ExpandCastRangeChecks` + `InsertRangeChecks`. Cheap here: the harness's ranges are wide (`0 to u64.max`), so the checks are near-vacuous — but the mechanism must exist |
| 🚩 | **PHASE 1 GATE** | below |

### 🚩 PHASE 1 GATE — the differential oracle

**Acceptance — and step 1 is the whole point:**
1. Compile the **parallel** harness with **`maxon.exe`** → `spec-runner-ref.exe`.
2. Compile the **parallel** harness with **`maxon-shv2.exe`** → `spec-runner-shv2.exe`.
3. Run **both** against `specs-shv2/`, both driving `maxon-shv2.exe` as the compiler-under-test,
   **both on an N-worker pool**.
4. **The two must produce identical results.**

If shv2 miscompiles the harness, the shv2-built harness's own verdicts are untrustworthy — so the
`maxon.exe`-built harness is the oracle that catches it. **Without step 1 this gate could pass
while silently broken.**

**Additional gates the parallel pool brings — and they are a feature, not a tax:**
- **Worker-count invariance:** `-j1` and `-jN` must produce **identical results**. This is the
  same shape as the 1-core-vs-N-core byte-identity gate the plan already demands of P2.6's
  fan-out, and it is the sharpest test of `async` correctness that exists — a dropped or
  double-freed capture shows up as a flake, and a flake is a bug.
- **`mm-trace` must stay clean under the pool.** A green-thread capture that leaks or
  double-decrefs is exactly what P1.5 exists to get right, and this is where it is proven.

**Then the circle closes:** the shv2-compiled, shv2-parallel harness runs the spec suite that
tests shv2 — which is what `maxon-selfhosted` does, and is the Phase-1 goal.

---

## Phase 2 — self-host

**= Phase 1, plus exactly what shv2's own 21,038-line source adds.** Bounded by measurement
against that source:

| # | Mechanism | Forced by |
|---|---|---|
| **P2.1** | **interfaces + witness tables** | static conformance (`Hashable`, `Stringable`). **No** existentials — shv2 stores nothing at interface type. ⇒ promotes the stdlib's interface decls DECLARE→EMIT, and unlocks `"{userStruct}"` interpolation |
| **P2.2** | **conditional conformance** | per-gid witness tables + synthesized thunks — one witness blob per `GenericInstanceId` (`Array<Byte>: Equatable`, …), each method slot pointing at a thunk with the interface-declared signature whose body forwards to the shared generic impl, materializing that instance's implicit layout/witness args (v1: `synthesizeWitnessThunks`). **The last big generics piece.** ⇒ **[GlobalDataTable.maxon:23](maxon-shv2/Compiler/Targets/Shared/GlobalDataTable.maxon#L23)** `Map with (ByteArray, String)` compiles — **its acceptance test, kept deliberately** |
| **P2.3** | **`Map` + `Set`** | multi-param generics + `Hashable`. 12 Map typealiases in shv2; `Set` rides the identical mechanism — [StdDialect.maxon:333](maxon-shv2/Compiler/IR/Std/StdDialect.maxon#L333) `StdValueUseSet` is load-bearing in `ElimTrivialBlockArgs`, `FoldConstOperands`, `PruneDeadBlockArgs` |
| **P2.4** | **`extension`** | promotes the stdlib's extension blocks DECLARE→EMIT |
| **P2.5** | **closure dogfood** | shv2's `LazyMessage` sites ([Logger.maxon:35](maxon-shv2/Compiler/Logger.maxon#L35)) compile — the acceptance test for P1.5 |
| **P2.6** | **per-function fan-out** | the one carry-over from the scalar core (M5's original scope, never built). Both seams exist (`PassPipeline.classifyPass`; the parser is already a pure function of its file) — and **the runtime under it now exists, because P1.5 brought R3 forward**. Gate: **1-core-vs-N-core byte identity** |
| 🚩 | **PHASE 2 GATE — self-host** | **3-stage bootstrap fixpoint: stage-2 == stage-3, byte-identical**, and stage-2 shv2 passes the whole `specs-shv2/` suite |

**Byte-identity is a cliff, not a ramp** — it demands determinism in rdata ordering, hash
iteration order, name mangling, and float formatting. It is **already gated from Stage 1 on the
toy corpus**: `specs-shv2/fragments/` goldens are compared, so a clean `git status` after a spec
run *proves* byte-identical codegen. Keep it that way and the fixpoint is a ramp.

**Budget a core-drift pass in Phase 2.** With the compass cut, nothing enforces "shv2's source
stays in core," and it demonstrably drifted during the scalar core alone (`panic()` 148→626, a
second `Set`). shv2 pointing at its own source names every violation for free. Every *known* one
is now **in core** (closures, conditional conformance, `Set`), so this should be small. **Watch
for a violation that is NOT a mechanical rewrite** — that, not the drift itself, is the signal to
re-open the compass decision.

---

## Workstream R — the runtime shv2 must EMIT ⚠

**~5–7k lines, on the critical path — a workstream, NOT a stage.** shv2-compiled binaries carry
the runtime *shv2's backend hand-assembles*; a shv2-compiled harness cannot **run** without it.
Sequencing it after the ladder would contradict P1.2's own mm-trace gate, so it lands in slices,
each WITH the milestone that first needs it:

- **R1 @ P1.2** — slab allocator · `__mm_incref`/`__mm_decref` · the `__destruct_*` cascade ·
  `__ManagedMemory` · the **string runtime** (`mrt_alloc_with_dtor`, `memcpy`,
  `mrt_i64_to_string` / `mrt_f64_to_string` / `mrt_bool_to_string` — interpolation is a
  compiler-emitted memcpy chain, so these are the only helpers it needs — and `mrt_write_stdout`)
  · the DebugStream producer (schema-compatible port of `RuntimeEmitter.DebugStream.cs`), which is
  what lets mm-trace gate P1.2 onward. v1's force-seeded bootstrap roots (`__slab_init`,
  `__mm_alloc`, `__managed_mem_create`, …) become a real obligation here.
  **The allocator R1 emits must ALWAYS RETURN ZEROED MEMORY, from commit 1** — it is a property of
  the allocator, not a thing each caller remembers. Non-zeroing alloc cost v1 at least three
  separately root-caused bugs (the `__gt_spawn` `cancel_flag` deadlock, the socket
  `OVERLAPPED.hEvent` IOCP hang, and `mrt_alloc`'s Map/Set hash tables decref'ing garbage), each
  "fixed" by bolting a zeroing loop onto the caller. Retrofitting the guarantee later means
  re-auditing every raw-buffer call site. See **ARCHITECTURE.md → "Allocator: the zeroing
  contract"** for the full design (Go's `needzero` model, the bump cursor, `__slab_alloc_raw` + its
  audit rule, and the memzero size ladder R1's backend must emit).
- **R2 @ P1.8** — UCD table access, for the real `String` methods.
- **R3 @ P1.5 — UN-DEFERRED into Phase 1 (2026-07-13).** The **GT scheduler** (`emitX64Gt*` port;
  allocator mirrored from the C# **sharded** design, **not** v1's single-shared-mcache) **+ async
  subprocess stdio** (v1's pool does `try await drainPromise` on worker stdout, so the pipes must
  be non-blocking — IOCP on Windows). *Previously scheduled at "Beyond," on the claim that "a
  single-threaded shv2 is acceptable through both phases — shv2's own source uses no `async`."*
  **That claim is dead:** the Phase-1 goal is a **parallel** spec harness, so the harness *is* an
  async user, and P2.6's per-function fan-out was always going to be a second one. Deferring R3
  would mean shipping `EscapeAnalysis` single-threaded and retrofitting green-thread capture into
  it later — the exact retrofit this plan exists to prevent. **Doing it at P1.5 pays twice.**
  ⚠ Note the zeroing contract (R1) is *load-bearing here*: two of the three bugs it was written
  for were green-thread bugs (`__gt_spawn`'s `cancel_flag` deadlock; the socket
  `OVERLAPPED.hEvent` IOCP hang).

Per the runtime-binding decision (Context): shv2 **excludes `Internals.maxon` and emits natively**
— builtin registration for the `__Managed*` surface replaces v1's `__Internals` mechanism +
`StdlibLoader` (6,434 lines, the road not taken).

---

## Beyond the two phases

**Broaden:** general `Iterable` + associated types · `List`/Json/… · arm64 + wasm · coverage ·
inliner · port the 273-file / ~2,573-case spec suite. *(`async`/green threads LEFT this list on
2026-07-13 — they are core, at P1.5. Porting the full suite is also what makes the parallel pool
pay for itself: v1 needs 12 workers for ~2,573 cases.)*

**Budgets:** **≤30 s / ≤1.7 GB / >90% CPU** on self-compile. Runtime multi-core is already proven
(Track 0). **Caching is revisited here, on a working compiler, with the approach chosen fresh** —
not ported from v1's `.mxc`.

---

## Closed — do not reopen

- **`selfhost-distance` (the compass)** — ❌ **CUT 2026-07-13.** It was sold as "the ranked
  TOP-UNSUPPORTED table IS the roadmap," but the roadmap is ordered by *reasons* a frequency table
  cannot discover (dictionary-passing forces the descriptor drop path before `Array`; `String` is
  a runtime-gated builtin). **We know what we need to implement.** Its real price, re-measured: a
  parser recovery mode + making `panic()` recoverable at **626** sites (the plan guessed 148, and
  projected ~600 only by *mid*-ladder; we hit it at the *end of the scalar core*). It buys a
  number, not a feature. `SelfhostDistance.maxon` was never written — the cut is docs-only.
  **The Phase 1 gate is the mitigation:** it answers "does shv2 compile real Maxon?" on 704 lines
  instead of 21k, which is most of what the compass promised for none of its cost.
- **The `panic()` audit** and **parser recovery mode** — VOID with the compass. shv2 needs neither.
- **The `spec-test` runner** — ✅ **SHIPPED** (`72ffe87a9`). `Compiler.maxon` grew `compileSource()`
  + `CompileOutcome` (returns `project.diagnostics` instead of printing them — `Diagnostic.render()`
  already emits the exact `error EXXXX:` wire format `maxoncstderr` compares against). **Do NOT
  port v1's 7,699-line harness.**
- **Rewriting shv2's core-violating sites** — VOID. Closures and conditional conformance are *in
  core*; the sites are the tests.

### Traps that survive (each is a silent miscompile, not an error)

| # | Trap | Consequence |
|---|---|---|
| 1 | **The stdlib dir's LEAF NAME *is* the namespace** ([2-Parser.cs:804](maxon-sharp/Compiler/2-Parser.cs#L804)); `"stdlib."` hardcoded in [MonomorphizationPass.cs:754](maxon-sharp/Compiler/MLIR/Passes/MonomorphizationPass.cs#L754) | `stdlib-shv2` as a leaf ⇒ monomorphization silently misses every call site. **Path must be `stdlib-shv2/stdlib/`** |
| 2 | UCD `.bin` files resolve from `<stdlib>/helpers/string/` ([MaxonToStandardConversion.cs:2629](maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L2629)) | A pruned fork without `ucd_bmp.bin`/`ucd_supp.bin` ⇒ hard throw mid-lowering |
| 3 | `_cachedSources`/`_cachedStdlibModule` are **process-static** ([0-Compiler.cs:803](maxon-sharp/Compiler/0-Compiler.cs#L803)) | Use an **env var, not a per-invocation flag** — the C# TestRunner batches many compiles per process and would serve whichever stdlib loaded first |
| 4 | `__Managed*Error` case ordinals must match `Builtins.maxon` **exactly** ([2-Parser.cs:1116](maxon-sharp/Compiler/2-Parser.cs#L1116)) | Reordering a case re-points a builtin's `throwsType` at the wrong ordinal |
| 5 | `ExitCode` lives in **`Process.maxon`** | Drop it from the fork and `main() returns ExitCode` won't resolve |
| 6 | Spec fragments need a **trailing newline** | Every spec test dies with a lexer EOF error |
| 7 | *(inverse)* `BuildCache` is **already** stdlib-path-keyed ([BuildCache.cs:52](maxon-sharp/BuildCache.cs#L52)) | Don't "fix" it. But never set `MAXON_STDLIB` globally — v1's own `findStdlibPath()` ignores it and the two compilers would diverge |

---

## Execution — how this parallelizes across agents

**The project has two axes, and only one of them parallelizes.**

- **The mechanism ladder is SERIAL.** heap/drops → String → owned payloads → errors → escape →
  generics → `Array`. These cannot be split: `own.drop` routes through the layout descriptor,
  `Array` *is* generics ∘ ownership. An agent "doing ownership in parallel with generics" would be
  writing against an IR that doesn't exist yet.
- **The pipeline (within any one rung) is PARALLEL.** Every rung touches Parser →
  TypeResolution/SemanticCheck → LowerMaxonToStd → Std passes → Backend → specs, and those are
  *different files*. Fix the dialect ops first and four agents write against them simultaneously.

⇒ **The coordinator owns the contract; agents own layers.** Two rules make it work:

> **RULE 1 — one file, one owner, per wave.** Never let two agents hold the same file.
> **RULE 2 — the coordinator writes the dialect ops BEFORE the wave launches**
> (`MaxonDialect.maxon`, `StdDialect.maxon`, `TargetDialect.maxon` + the `*OpMeta` backing), and
> hands agents a concrete golden-IR example for a sample program. The op definitions ARE the
> contract; agents coding against a contract that is still moving is the failure mode that makes
> parallel agents net-negative.

### The per-rung layer split

| Agent | Owns |
|---|---|
| **P** | `Compiler/Parser.maxon` (+ `ParseStaging.maxon`) |
| **T** | `Compiler/TypeResolution.maxon`, `SemanticCheck.maxon` |
| **L** | `Compiler/IR/Maxon/LowerMaxonToStd.maxon` (+ Std passes) |
| **B** | `Targets/X64/**`, `Targets/Shared/**` |
| **S** | `specs-shv2/<feature>.md` — authors the spec *before* the others finish (it is the acceptance test, so writing it first is a feature, not a nicety) |

**Coordinator-owned, in NO brief:** `Main.maxon` (agents want to add commands — wire them after
they land).

### Standing agent brief requirements (learned the hard way in v1)

Every brief MUST: (a) name the **specific v1 file to port**, with line numbers, and the shv2
**divergences to adapt to** (no MIR tier; `project.diagnostics` is first-class; `FileParseArtifact`
staging); (b) state the **exclusive file list** and the coordinator-owned files it must not touch;
(c) name the **traps** for that area; (d) require the report to **justify every deviation** and to
never claim a step passed unless it was run.

### Isolation & integration

Worktree-isolated agents; the coordinator merges and then **builds + runs `spec-test`** at each
integration point. That build is not distrust of the agents — it is the only place the coupling
between layers is actually exercised. Integration is inherently serial and is the real limit on
wave size: **beyond ~4–5 agents per wave, integration dominates and adding agents makes it
slower.**

### The workstreams that run ALONGSIDE the waves

Big, self-contained, stable input contract — the best parallel value in the project because they
never block on the ladder:

- **Workstream R — the emitted runtime** (~5–7k lines; R1 @ P1.2 · R2 @ P1.8 · R3 @ Beyond). Its
  contract is the MM ABI, fixed at P1.2.
- ~~**Workstream A — the register allocator**~~ ✅ **DONE** (M5.1→M5.12, and *linear*). The plan's
  biggest single risk, retired.
- **Workstream S — spec-suite triage.** Pure analysis over the 273 files / ~2,573 cases: which spec
  ports at which rung. Zero code contention; feeds agent **S** in every wave.

---

## Where we are

**The scalar core is DONE** (old M1–M5): `let`/`var`/block scope · full-Pratt
arithmetic/comparison/unary · `if`/`else` · `while`/`break`/`continue` (on-the-fly SSA +
`EliminatePhis`) · functions with params + calls · integer `/` and `mod`.

**The register allocator shipped and beat its own brief.** Register allocation was ~74% of v1's
self-compile wall time (~418 s of 561 s) against shv2's ≤30 s *whole-compile* budget; shv2's is
**linear** (M5.10 killed the quadratics for 8.8×; M5.11 replaced the fixpoint with SSA
path-exploration liveness + sparse live sets), with a cold-spill live-range splitter, `E5001` +
`ValueOrigin` for genuine pressure, and a custom ABI. **This was the plan's single biggest risk and
it is retired.**

| | v1 | shv2 (est.) |
|---|---:|---:|
| Parser | 21,862 | 6–8k |
| TypeResolution | 12,142 | 4–5k |
| LowerMaxonToStd | 16,135 | 6–8k |
| Memory model (`Own/*` vs `InsertRefcounts`) | 7,755 | 3–4k |
| Std passes | ~10k | 5–6k |
| x64 backend + emitters | 16,030 | 8–10k |
| Register allocator | 8,520 | ✅ **DONE** (linear) |
| **Workstream R (emitted runtime)** | *(inside X64Backend)* | **5–7k** |
| Testing | 7,699 | ✅ **704** |
| **Total** | **191,487** | **~50–65k** |

**Current: 21,038.** Self-compile is **~30–45k lines away** — and the hardest *single* piece of it
(the allocator) is already behind us.

---

## Verification

- **Per rung:** `maxon-shv2 spec-test` stays green (**126/0** as of 2026-07-13); ownership rungs
  (P1.2+) also assert an `mm-trace` block via `maxon monitor`.
- **Per commit:** `specs-shv2/fragments/` **clean** under `git status` after a spec run — the
  goldens are compared, so an empty diff *proves* byte-identical codegen. Plus
  `verify-warm-rebuild` PASS. These, not a distance metric, are the continuous gates.
- **From P1.5:** track **`% values promoted to shared`** — if it's 40%, static ownership bought
  nothing.
- **🚩 PHASE 1 GATE:** the differential oracle — `maxon.exe`-built and `maxon-shv2.exe`-built
  **parallel** harnesses must produce **identical** results over `specs-shv2/`. Plus
  **worker-count invariance** (`-j1` == `-jN`) and a clean `mm-trace` under the pool. Then the
  shv2-built harness becomes the runner.
- **🚩 PHASE 2 GATE:** stage-2 == stage-3 **byte-identical**; stage-2 shv2 passes the suite.
- **Beyond:** ≤30 s / ≤1.7 GB / >90% CPU on self-compile.

## Critical files

- **New:** `maxon-shv2/Compiler/IR/LayoutDescriptor.maxon` (declared at P1.2, live at P1.6);
  `maxon-shv2/Compiler/IR/Own/{OwnDialect,OwnershipInfer,OwnershipCheck,EscapeAnalysis,InsertDrops}.maxon`;
  `stdlib-shv2/stdlib/` (leaf name **must** be `stdlib`).
  ✅ done: `maxon-shv2/Testing/{SpecParser,SpecTestRunner}.maxon`.
  ~~`maxon-shv2/Compiler/SelfhostDistance.maxon`~~ (**cut — never written**).
- **Kept as tests, NOT rewritten** (per the PRINCIPLE):
  [GlobalDataTable.maxon:23](maxon-shv2/Compiler/Targets/Shared/GlobalDataTable.maxon#L23)
  (`GlobalDedupMap` — the conditional-conformance acceptance test, P2.2) ·
  [Logger.maxon:35](maxon-shv2/Compiler/Logger.maxon#L35) (`LazyMessage` — closure dogfood, P2.5) ·
  [StdDialect.maxon:333](maxon-shv2/Compiler/IR/Std/StdDialect.maxon#L333) (`StdValueUseSet` — why
  `Set` is core, P2.3).
- **Modified:** [Parser.maxon](maxon-shv2/Compiler/Parser.maxon) (grows every rung; **no recovery
  mode** — that was the compass's requirement);
  [Compiler.maxon](maxon-shv2/Compiler/Compiler.maxon) (`compileSource` ✅);
  [Main.maxon](maxon-shv2/Main.maxon) (`spec-test` ✅; the standalone `spec-runner` extraction at the
  Phase 1 gate); [0-Compiler.cs:846](maxon-sharp/Compiler/0-Compiler.cs#L846) (`MAXON_STDLIB`, for
  the fork).
- **Reference (read, don't copy):** `maxon-selfhosted/Compiler/IR/LayoutDescriptor.maxon`,
  `Compiler/Passes/BuildWitnessTables.maxon`, `Compiler/IR/Std/InsertRefcounts.maxon`, and
  [LowerMaxonToStd.maxon:1523](maxon-selfhosted/Compiler/IR/Maxon/LowerMaxonToStd.maxon#L1523)
  (`registerManagedMemoryStruct` — the hardcoded 40-byte record String rides on).
