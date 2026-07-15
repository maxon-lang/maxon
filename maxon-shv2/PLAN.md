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
**21,038 lines**, with a working `spec-test` runner (**specs-shv2 281/0** as of 2026-07-15 — it was
126/0 across 19 files when this plan was written; Workstream S's on-demand ports are what grew it), a
warm-rebuild determinism gate, the full scalar core, and a **linear** SSA-chordal register
allocator. The bones are good — content-hash query spine, parse-staging, 3 IR tiers
(Maxon→Std→Target, no MIR), flat `StdOp`, x64/PE backend.

**~~The scalar core is DONE.~~ FALSE — measured 2026-07-13 against `/specs`: 48 of 2,746.** The 126
tests it passes were written by shv2, for shv2, and not one of them ever used a parenthesis. The core
was missing grouping, `true`/`false`, `not`/`and`/`or`, block scoping, void functions, top-level
`typealias`, floats, chars, and bitwise ops — and it turned `a / 0` into a hardware trap.

> ### ✅ **P1.0d IS CLOSED (2026-07-15). The scalar core is done — MEASURED this time: `specs-shv2` 355/355.**
> Every gap the 2026-07-13 sweep found is fixed, floats last (P1.0d.4: an XMM register class, the SSE
> backend, `trunc`, and the conversion band). **`main` is 355/0; the C# bootstrap is 3004/0.**
> **⇒ THE LIVE WORK IS NOW `P1.0r` (the ALLOCATOR + refcounting runtime — Workstream R1's core).**
>
> ⚠ **Read P1.0d.4's ledger rows before starting the next rung** — the contract was wrong TEN times, nine of
> them ONE FACT WRITTEN DOWN TWICE, and the rung shipped two of its OWN features (float negation,
> float phis) with zero coverage until the corpus and the instrument were made to look. **Step 0 is
> the SPEC PORT LIST, and it is the coordinator's.**

> ## ⭐⭐ RESTRUCTURED 2026-07-15 — **P1.1 COULD NOT BE BUILT AS WRITTEN. A STRUCT IS A HEAP VALUE.**
>
> **The old ladder said `P1.1` structs ("concrete, trivial-ownership only") came BEFORE `P1.2`'s heap, and
> that `String` is "the FIRST heap value". Both are FALSE, and they were falsified by MEASURING the
> bootstrap** — the oracle — not by reading it:
>
> | Probe | Result | What it proves |
> |---|---|---|
> | `mutate(p)` writes `q.x = 90`, caller reads `p.x` | **95** (=90+5), not 6 | a struct **ALIASES** — reference semantics, not value |
> | `sizeof(Outer)`, `Outer{p Point, n Integer}` | **16**, not 24 | a struct-typed field is an **8-byte POINTER**, never inlined |
> | `var q = p` where `p` is `let` | **E3078** *"…or use `clone()`"* | the language **POLICES** struct aliasing — it knows |
> | both reference compilers | `IsHeapAllocated => true` **unconditionally**; v1 `__mm_alloc` + `freshRc0` | they AGREE, with no exception for scalar fields |
>
> ⇒ **A struct needs `__mm_alloc` ⇒ it needs Workstream R1 ⇒ which the plan scheduled AFTER it.** The
> dependency was inverted. **And `String` is NOT the first heap value — a scalar struct is, and it is a
> far SIMPLER one**: no rdata `capacity = -2` sentinel, no interning, no 40-byte `__ManagedMemory`
> record, no interpolation memcpy chain, no `BuiltinStringLiteral` marker-interface discovery. A struct
> is `__mm_alloc(size, null)` + `loadIndirect`/`storeIndirect` at an offset. **That is the whole thing.**
>
> ⚠ **The stack/value-struct escape hatch is CLOSED, on evidence rather than taste**: it returns **6
> instead of 95** and **24 instead of 16** — wrong answers on cases already in the port list — and
> nested structs force the heap regardless (an 8-byte field slot cannot inline a 16-byte `Point`). It is
> also the retrofit the PRINCIPLE forbids. **v1 independently confirms the direction**: *"Plain structs
> used to leak… conflating drop-tracked vs heap-managed is what leaked every plain heap struct (it was
> allocated untracked via `mrt_alloc`)"* — **v1 TRIED the cheap non-refcounted struct path, and it leaked.**
>
> **⇒ THE ORDER IS NOW: heap → structs → enums+`match` → `String`** (user ruling, 2026-07-15).
>
> ### ⚠ WHY THE NUMBERS DID NOT CHANGE — and this is load-bearing
> **The rungs are NOT renumbered, deliberately.** `specs-shv2/` carries **153 `disabled-test:` reason
> markers that cite a rung BY NUMBER** (41 × `P1.2`, 33 × `P1.9`, 24 × `P1.1`, 21 × `P1.7`, 12 × `P1.4`,
> 9 × `P1.8`) — and §"the disabled-test reasons ARE the ranked roadmap" is what makes them the roadmap.
> **Renumbering would silently re-point 77+ of them at the wrong rung** — the project's own signature bug
> (ONE FACT WRITTEN DOWN TWICE) at the ladder level, and the markers would go on looking right.
> ⇒ The allocator takes a **NEW** number in the established pre-P1.1 slice convention (`P1.0a`…`P1.0d`):
> **`P1.0r`** — `r` for Workstream **R**. `P1.1` splits into **`P1.1a` structs** / **`P1.1b` enums+`match`**,
> both of which its 24 markers already read correctly. **`P1.2` KEEPS ITS NUMBER** and keeps meaning
> `String`; it merely stops being *first*. Every existing marker still resolves.

> **Note the two senses of "shv2 runs its spec tests."** shv2 *already* has a `spec-test`
> command (281/0) — but that harness is compiled by **`maxon.exe`**, the C# bootstrap. Phase 1
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
| **`async` / green threads** | **IN core, at P1.5** *(reversed 2026-07-13 — was "Beyond")*. Parallel spec execution is part of the Phase-1 goal, and a green-thread capture **IS** an escape — so it co-lands with closures + escape, never after. Brings **Workstream R3 (the GT scheduler) into Phase 1.** Its dogfood + acceptance test is the parallel harness; it also un-blocks **P2.6** per-function fan-out, which was always going to need it. **⭐ AND: give `Promise` the ERROR TYPE — see below. Do not port the bootstrap's shape.** |
| **Conditional conformance** | **IN core** — *declared* in Phase 1 (the stdlib forces it), *emitted* at **P2.2**. It is a hard mechanism, and this plan implements hard mechanisms rather than dodging them. See the PRINCIPLE. |
| **`Map` / `Set`** | multi-param generics + one `Hashable` constraint. No `Iterable`-on-Map, no tuple `Entry`. **P2.3.** `Set` rides Map's exact mechanism. |
| **Iteration** | **Hardcode `for-in`** over Array/Range/String. No general `Iterable`/associated types. |
| **Errors** | v1's design **verbatim**: dual-register `(value, errorFlag)`. No unwind tables. Already minimal. |
| **Stdlib lowering** | **Reachability-seeded — lower only the stdlib bodies the program transitively reaches.** *Not an optimization: it is what makes Phase 1 a phase.* See §"The stdlib cone." |
| **Stdlib fork** | `stdlib-shv2/stdlib/` — ❌ **NOT NEEDED. Re-deferred 2026-07-13 on measurement (P1.0c).** It was un-deferred as the backstop for `Map`, *"if reachability-seeded lowering proves insufficient."* **It proved sufficient** — `Map` is laid out and never codegen'd. The one edge it *could* have cut (`String.trim()` → `CharacterSet` → `Set`) we chose NOT to cut: do the hard things early ⇒ `Set` is in Phase 1 (P1.7b). **So the fork now gates nothing, and its original ~1 s justification is still dead.** Do not resurrect it without a NEW reason. |
| **Targets** | x64-windows only through Phase 2. |

### ⭐ P1.5: `Promise` MUST CARRY ITS ERROR TYPE — `Promise with (T, E)`, not `Promise with T`

> ## ✅ REVERSED, SAME DAY — **PORT THE BOOTSTRAP'S SHAPE. IT IS NOW THE RIGHT ONE** (`34b77758e`)
>
> This section used to say **"Do NOT port its shape"**, because the bootstrap had the broken one. **The
> bootstrap has been FIXED**, along exactly the lines this section prescribed, so the instruction inverts:
> **shv2 should PORT `Promise with (T, E)` from the bootstrap rather than invent it.** The design below is
> no longer a plan — it is a working, gated implementation (C# suite 2915/0), and it is the cheapest kind
> of prior art: *the same design, already debugged.*
>
> **What the fix proved, and it is stronger than the argument that predicted it:**
> - **BOTH bits are now dead and REMOVED** — not merely unused. The proof is in the codegen: the promise
>   box drops **24 → 8 bytes** and loses **two stores per boxed promise**, and the `otherwise` emitter
>   loses its runtime-branched decref for the straight-line one a *static* type affords. `ErrorType` is
>   the whole story; `Throws` is *derived* (`ErrorType != null`), so there is **no second copy to drift**.
> - **`throws_` turned out to be WRITE-ONLY for its entire life.** Nothing ever read it — while a comment
>   claimed *"the runtime branches on it."* The comment described a compiler nobody wrote. (The same
>   disease as `TargetOpMeta.setsFlags` and the `xor reg,reg` idiom that never existed. **Three instances
>   in one day.**)
> - ⚠ **It was leaking in shv2's OWN worker pool, live.** `DrainPromise` was `Promise with StringArray`
>   while `drainResultsThunk` throws the **union** `SubprocessError`; the box said "not a heap pointer",
>   the conditional decref never fired, and **every dead worker leaked its error** — invisible to a green
>   suite, because that path only runs when a worker dies. It is now `Promise with (StringArray,
>   SubprocessError)` and binds `(e)`, which both releases the payload *and* reports which failure killed
>   the worker.
>
> **`await` is LINEAR** (user decision, 2026-07-14; `E3100`): a promise is awaited **exactly once**, and a
> second await *reachable* from the first is a compile error — flow-sensitive, keyed on the **binding**, so
> the central `for p in promises 'each' … await p … end` idiom stays legal (the loop re-arms `p` each
> iteration) and two awaits in **mutually exclusive branches** are fine. The double-free is now
> **unrepresentable**, not fixed.
> ⚠ **Known boundary, carry it into P1.5:** linearity is enforced on **bindings, not through containers** —
> awaiting the same *array slot* twice is not statically caught. That needs ownership tracking through the
> container, which is **shv2's ownership milestone**, and shv2 is the compiler that will have it.

**Learned from a real bug in the bootstrap (2026-07-14).** *(The history below is kept because it is the
argument for the design, and it is the reason to trust it.)*

The bootstrap's `MaxonPromise` stored **two BITS distilled from the callee's `throws` clause** (`Throws`,
`ErrorIsHeapPtr`) and **threw the type away**. Everything downstream then had to reconstruct what it
could from a bit, and it went wrong in four separate places:

- `try await p otherwise (e)` **bound `e` as `int`** — the *value* was right all along (the error
  ordinal), only the **type** was lost, so the binding fell back to `Integer`.
- An **associated-value** union error emitted **no decref** ⇒ the payload **LEAKED**.
- `async` and `await` in **different blocks** re-tagged the promise and **re-erased** the type.
- **Awaiting a thunk that throws `A` inside a function that throws `B`** skipped the type check
  entirely and **reinterpreted A's ordinals as B's tags — a silent miscompile.**

⚠ And it bit **shv2's own worker pool**: `drainResultsThunk` throws the union `SubprocessError`, the box
said "not a heap pointer", the conditional decref never fired, and **every dead worker leaked its
error.** Invisible to a green suite, because that path only runs when a worker dies.

**The erasure is not a threading bug — it is the TYPE.** `Promise with T` has one parameter, the
*result*; boxing therefore has nowhere to keep `E`, and no amount of plumbing recovers it (an
`Array with (Promise with Integer)` may legally hold promises from two functions with different
`throws`). The bootstrap's `errorIsHeapPtr` runtime bit exists **only** to paper over that erasure.

⇒ **shv2 builds `Promise` with the error type in it from the start. The runtime bit then becomes
unnecessary**, and all four bugs above are unrepresentable rather than fixed.

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

**MEASURED 2026-07-13 (P1.0c) against the UPGRADED, PARALLEL harness — NOT GREPPED, COMPILED.**
`maxon-shv2/Testing/` (`SpecParser` 364 + `SpecTestRunner` 467 + `SpecWorkerPool` 1091 = **1,922
lines**) plus the `Main` a standalone runner needs. A standalone `spec-runner` was extracted (the
harness verbatim + `Compiler/Target.maxon` verbatim + a 2-symbol shim + `Main` minus the driver
commands), built with `maxon.exe --emit-ir`, and its EMIT set read off **both** the emitted X86 module
**and** the PE COFF symbol table (`llvm-nm`) — **146 stdlib functions, the two lists identical.** The
extracted runner **runs**: it drives a real 2-worker green-thread pool.

| The harness **EMITs** (⇒ Phase 1 must CODEGEN) | The harness **DECLAREs only** (⇒ Phase 2) |
|---|---|
| structs · enums · **unions** · `match` | ✅ **`Map`** — **0** instantiation, **0** bodies. Its `EnvMap` arms in `Subprocess.Environment` compile to a bare `mm_decref`: **laid out, never codegen'd.** ⇒ **Reachability-seeded lowering WORKS, and the pruned fork is NOT needed for `Map`** |
| **`String`** + interpolation of primitives, `String`, **and one STRUCT** (`FilePath`, via `Stringable`) — `Main.maxon:233-236` | **conditional conformance** — `Array implements Hashable, Equatable where Element is …` (`Array.maxon:415`) is **NOT reached** |
| heap · ownership · drops | **`extension`** — **0**. Not one of the 4 `Array` / 3 `Interfaces` / 3 `PrimitiveExtensions` blocks emits a method |
| **owned `String` payloads in union cases** | **existentials / interface-typed storage** — 0 |
| `throws` / `try` / `otherwise` — **80 `try`, 16 `throws`** | the entire compiler cone — no IR, no allocator, no backend |
| **generics** — `Array with {SpecTest, SpecTestResult, FilePath, SpecJob, SpecPlan, …}` ⇒ managed elements | `Json` · `List` · `Math` · `Range` · `Sha256` · `Vector` · `Log` · `Clock` · `HttpClient` · `TcpClient` · `Ascii` · `Unicode` · `helpers/sort/*` |
| **`Array`** · `for-in` — **18** sites | |
| **ranged typealiases** — **8** | |
| ⭐ **`Set` — EMITTED, and not by anything the harness wrote.** `CharSet = Set with Character` (`CharacterSet.maxon:19`) arrives via **`String.trim()` (13 sites)** → `CharacterSet.whitespacesAndNewlines()`. Emits `CharSet.{init,insert,contains,grow}` + `HashSlotArray`/`StateArray` | |
| ⭐ **`Hashable` + `Equatable` — EMITTED**, as `Set`'s element constraints: `stdlib.Character.hash` / `.equals` (type-body conformances) | |
| **`async` / `await` / `Promise`** — from the pool. **`Promise` has ZERO stdlib bodies** (a compiler-synthesised facade); the real surface is **runtime** — `__gt_spawn` · `__gt_try_await` · `__gt_is_complete` ⇒ **Workstream R3** | |
| **async subprocess stdio** — `StreamingSubprocess` ×11 + `Stdin` ×7 (+23 fns — the pool's ENTIRE stdlib delta over the serial harness) | |
| ⚠ **CLOSURES — 0.** *(The old table claimed the pool added them. It did not.)* `SpecWorkerPool.maxon:1000` is `async drainResultsThunk(handle.child)` — `async` on a **direct free-function call**, chosen deliberately to avoid capturing a struct. **The escape channel Phase 1 exercises is the async call's ARGUMENTS, not a closure env — two distinct channels, and only one is tested.** P1.5 must not mistake one for the other | |

### ⇒ `Set` + `Hashable` + WITNESS TABLES are PHASE 1 (decided 2026-07-13)

*"`Set` rides `Map`'s exact mechanism"* is now **false as sequencing**: **`Set` is reached and `Map` is
not.** It cannot ride a mechanism that arrives after it.

**`String.trim()` is about the most innocuous call in the stdlib, and it pulls in `Set` → `Hashable` →
`Equatable`.** This is exactly the class of thing §"the stdlib cone" was written to catch — and it
caught `Map` while missing `Set`.

The option was to cut the `trim()` → `CharacterSet` → `Set` edge in the `stdlib-shv2/` fork (sanctioned:
the PRINCIPLE binds shv2's own source, not the stdlib). **REJECTED, on this plan's governing principle:
DO THE HARD THINGS EARLY.** So **`P2.1` interfaces+witness and `P2.3` `Set`** are **promoted into Phase 1**
(as **P1.7a** / **P1.7b**), forced by a real program rather than a bespoke test. Phase 1 ≈ Phase 2 minus
`Map`, `extension`, and conditional conformance.

⚠ **One consequence is INFERRED, not measured, and it is the thing to watch.** The bootstrap
**monomorphizes**, so it discharges `Element is Hashable` as a *static direct call* and emits **zero**
witness tables (measured: zero indirect calls in the whole module). **shv2's locked design is the
opposite** — dictionary-passing + witness tables — under which `element.hash()` on a *type parameter* has
no route except a witness slot. The only architecture-matched compiler that could settle it is v1, and
**v1 NO LONGER BUILDS** (see below).

### ⚠ Two methodological corrections, both of which cost this plan a wrong answer

1. **Measure the PROGRAM, not the DIRECTORY.** The old table was *"grep-verified against
   `maxon-shv2/Testing/`"* — and `Testing/` really is clean (0 bare struct interpolations; all 13 of its
   `.toString()` calls are explicit). But **the Phase-1 artifact is `Testing/` + a `Main`**, and
   `Main.maxon:233-236`'s error reporter interpolates a bare `FilePath` — a **struct**, through
   `Stringable`. Four sites. A grep of the wrong scope reported zero.
2. **Compile it, don't grep it.** Every one of the four corrections here (`Set` used, `Map` unused,
   closures 0, struct interpolation ≠ 0) was invisible to grep and obvious to the linker.

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

⇒ ~~**The backstop, and why the pruned fork is UN-DEFERRED**~~ ❌ **RESOLVED BY MEASUREMENT — the fork
is NOT needed.** It was un-deferred as the lever that bounds Phase 1's surface, *"if reachability-seeded
lowering proves insufficient"*, naming `EnvMap`/`Map` as the case.

**P1.0c settled it with the machine code: reachability-seeded lowering IS sufficient for `Map`.** Zero
`Map` instantiation, zero `Map` bodies, in a 401-function emitted module. `__destruct_Environment` *is*
emitted — and its `EnvMap` arms compile to a bare **`x64.call mm_decref`**, a header-driven refcount drop
that needs only the *resolve-time* fact that the payload is managed. **It never names `Map` and never
needs a `Map` body.** That is the EMIT/DECLARE split, visible in the emitted code.

The one edge the fork could still have cut — `String.trim()` → `CharacterSet` → `Set` — we chose **not**
to cut (do the hard things early; `Set` is now P1.7b). **So the fork gates nothing. Do not resurrect it
without a NEW reason.**
- **TRAP (kept for the day someone does):** the stdlib dir's **LEAF NAME *is* the namespace**
  ([2-Parser.cs:804](maxon-sharp/Compiler/2-Parser.cs#L804)), and `"stdlib."` is hardcoded in
  [MonomorphizationPass.cs:754,522,619](maxon-sharp/Compiler/MLIR/Passes/MonomorphizationPass.cs#L754).
  A dir named `stdlib-shv2` ⇒ namespace `stdlib-shv2` ⇒ monomorphization **silently** misses every
  call site. **The path must be `stdlib-shv2/stdlib/`**, and `FindStdlibPath()` must hard-error if
  the leaf isn't `stdlib`.

**✅ DONE (P1.0c, 2026-07-13) — see §"Phase 1 — the measured boundary" above for the result.** ~~FIRST
TASK OF PHASE 1 (before P1.1): measure the cone.~~ Compile the harness with `maxon.exe`
and list which stdlib functions actually get codegen'd. That list *is* Phase 1's true stdlib
surface, and it settles the `Map` question with data instead of argument.

---

## Phase 1 — the ladder

Each rung is individually shippable, with one spec. **Core-first is a *re-ordering*, not a
replacement** — if that isn't explicit, Phase 1 becomes a six-month integration hole with no
green build.

| # | Mechanism | Note |
|---|---|---|
| **P1.0o** | **the compiler traces ITSELF — Workstream O1** ⭐ | **FIRST, because it is the instrument the rest of the ladder is debugged with.** shv2's stderr `Logger` dies the moment P1.0a interleaves N workers into one stream. A `__DebugStream` builtin in **`maxon.exe`** + 4 new event codes + a sink behind `Logger`'s existing API ⇒ binary events into the shared-memory ring, demuxed per-worker by `maxon monitor`. **Depends on NOTHING in Phase 1** — the bootstrap already carries the ring, the reserve, and the monitor *(see Workstream O)* |
| **P1.0a** | **grow the harness's parallel worker pool back** | **The acceptance target must exist before it can be a target.** Port `maxon-selfhosted`'s [`runAllSpecTestsParallel`](maxon-selfhosted/Testing/SpecTestRunner.maxon#L3401) worker pool into `maxon-shv2/Testing/`. Written in Maxon, compiled by **`maxon.exe`**, green under today's gates — so it lands *now*, and every later rung is measured against the real Phase-1 target instead of the serial stub. **Workstream S is what makes it pay:** the corpus takes the suite from 126 tests to thousands |
| **P1.0b** | **Workstream S — the `disabled-test:` marker, and ON-DEMAND porting** ⭐ | *(see Workstream S.)* The marker is SHIPPED (`362b07b72`). **The bulk port is NOT, and will not be** (user directive): spec files are copied from `/specs` **on demand, by the rung that needs them**, not as a corpus dump. A trial bulk sweep was run once, as a MEASUREMENT, and then discarded — see P1.0d, which is what it found |
| **P1.0d** | **complete the SCALAR CORE** ⭐⭐ | **NEW, and it exists because the sweep proved this plan's central claim false.** See "The scalar core is NOT done" below. **SLICE 1 ✅ DONE** (parens · `true`/`false` · block scoping · void fns · top-level `typealias`) — suite **126 → 159**. **SLICES REMAINING ← NEXT:** see below |
| **P1.0d.2** | `not` / `and` / `or` · bitwise · character literals | ✅ **DONE.** Short-circuit `and`/`or` is control flow, so it landed as blocks + phis on the parser's on-the-fly SSA. Bitwise + chars are new `StdOp`s in the existing integer register class — **APPENDED at the END of a band** (a `match` range arm silently swallows anything inserted mid-band) |
| **P1.0d.3** | **`a / 0` ⇒ a clean panic** | ✅ **DONE 2026-07-15** — suite **279 → 281**. It escaped as a raw `0xC0000094` with **empty stderr**; it is now exit **1** + `panic: integer divide by zero` + a symbolized backtrace, and `specs/safety.md`'s `divide-by-zero` + `mod-by-zero` are ported and ENABLED. **Workstream R's first slice.** Three things this rung settled, each bigger than the divide: (1) **the fault is caught by a VEH thunk, not a divisor check** — shv2 is x64-only and the CPU raises `#DE` for free, so a `cmp`/`branch` before every `idiv` would be the scope cut the PRINCIPLE names; **NO gt redirect** (the reference's fault path rides green threads, which arrive at P1.5) — the thunk prints and exits in place, so the context travels as ordinary arguments and needs no fault globals at all. (2) ⭐ **FRAME POINTERS, on every function, leaves included** — see below. (3) the harness grew a ` ```stderr ` fence: `maxoncstderr` is the COMPILER's stderr, and a program's **RUNTIME** stderr was a thing no spec could pin |
| **P1.0d.5a** | **top-level `let`** ⚠ **the plan never listed this one either** | ✅ **DONE 2026-07-15** — suite **281 → 295**. **Found by probing, not by the ladder** — and it has **its own stable spec nobody had found: `specs/top-level-let.md`, 17 cases**, which the bootstrap passes. *(Missed twice because a grep for `global|static|module|init` does not match the filename — **look for a feature's own spec BY NAME.**)* **A module-scope constant is a NUMBER: it inlines as an ordinary `literal` at each use ⇒ no IR op, no `.data`, no relocation, NO BACKEND AT ALL.** Zero pre-existing goldens moved, which is the proof. Design: an arena of `(name, visibility, file, token range)` + a memoized DFS with cycle detection, driven inside `queryProgramSignatures` — that is what makes **forward references** and cross-file `export let` work. The evaluator **shares `TypeRules`' folds** (one opinion on constant arithmetic; only the climb loop forks, because `and`/`or` are *control flow* in a body and a *value* at file scope). ⚠ **Still `disabled-test:`: `basic-float-constant` (P1.0d.4), `file-private-same-name-cross-file` (P1.9 `as`), `from-literal-initializer` (P1.2 `String`)** |
| **P1.0d.5b** | **top-level `var` (GLOBALS)** ⚠ **another one the plan never listed** | ✅ **DONE 2026-07-15** — suite **295 → 317**. A module-scope global with a **real `.data` slot**. ⭐ **This rung created the Std MEMORY band** (`globalAddr` + `loadIndirect` + `storeIndirect`, v1's shape — NOT a fused `globalLoad`, because P1.1/P1.2 need the general pair) — **which begins retiring R1 @ P1.2's precondition** (see the R1 box: the Std-IR runtime route was blocked on the Std tier producing only `arith`/`call`). Plus `DataSectionEntry` in `GlobalDataTable`, a `.data` section in `PeWriter`, and the `dataSectionRipRelDisp32` arm. **NO `__module_init`** — initializers are constant-only, so they const-evaluate into `.data` **bytes**. ⚠ **`loadIndirect` MUST be `isPure: false`** or a global's read **hoists out of a loop that writes it** — a silent wrong answer, pinned by a spec. ⭐ **shv2 does NOT inherit the bootstrap's aliasing bug (OPEN.md #17):** identity is per-FILE **by mechanism** (`fileScopedDeclKey(name, readerFilePath)`), label = bare name + `$1` **only on collision** (path-free, so goldens stay stable; `$` is structurally unwritable — `isAlphaNum` is `[A-Za-z0-9_]`). **The corpus had no `var` twin of `file-private-same-name-cross-file`, so we wrote one**: shv2 returns **118**, the bootstrap **212**. ✅ **`short-circuit-elision.md` RETIRED** — its 5 divide-by-zero workaround cases are superseded by the real corpus cases; its 9 genuine *lowering* tests moved to `short-circuit-lowering.md` (goldens are **R100 renames** — proof the codegen did not move). ✅ **`RequiredData` blocks now actually compare** (they were **silently ignored**) |
| **P1.0d.4** | **floats (f64)** | ✅ **DONE 2026-07-15 — suite 317/19 → 355/0, and P1.0d IS CLOSED.** Wave 1 (an all-caller-saved XMM pool; a float across a call force-spills). ⭐ **ONE `X64Register` enum, xmm0-15 at rawValue 16-31 — the low nibble IS the encoding number, so `and 0xF` encodes and bit 4 classifies.** The design that paid for itself: **CLASS-AGNOSTIC OPS, CLASS-DISPATCHED ENCODERS** — a move/spill/reload/swap is a class-agnostic *concept*, so there is no float spill op, no float move, no float `xchg`; the encoder picks `mov` vs `movsd` from `regClassOf`. ⇒ `SsaDestruction` needs **no float case**, the splitter no float spill op, coloring is class-blind, and a spill slot is 8 bytes = an f64. *(v1 mints a SECOND enum and pays a parallel op per emit, threading a bare `regClass` int through each — that tax is absent here.)* **The class travels FORWARD** (`ValueClassColumns`, per FUNCTION — ValueIds are function-local — produced by `StdToX64Conversion`, consumed by the allocator; the Target tier carries no types, so it *cannot* derive one). ⚠ **`operandType` is the SOURCE and the OPCODE names the result** — true only for `siToFp`/`fpToSi`, the dialect's only cross-class ops. `cmp` is ALWAYS `i1` (a float compare's answer is a GPR bool). Float `/` is `StdBinOpcode.div` on `binOp`, **not** `StdOp.div`: `idiv` faults and `divsd` does not, and purity is the band's membership rule. ⭐ **`<`/`<=` SWAP OPERANDS** into the already-NaN-correct `ja`/`jae` family (`ucomisd` sets CF on unordered) — one jump, four of six predicates shape-identical to an integer compare; only `==`/`!=` need parity, **as a second BLOCK**, so `IrBlock.CondBranch`'s one-branch-per-block invariant is never bent and v1's phi-copy miscompile is **unrepresentable, not fixed** (`float-compare-branch.md`, 11/11). **`E3009`, not a new code**, for narrowing — see the ⚠ row below |
| | ⚠ **What P1.0d.4 cost, and it is the ledger** | **The contract was WRONG TEN TIMES**, and every one shares a shape: **TYPE surfaces specified without OPERATIONS** — `operandType` with no ops consuming it, a register enum with no instructions, a float type with no conversions (**`trunc` had 0 hits in shv2**). **Nine were ONE FACT WRITTEN DOWN TWICE**, the file's own through-line: `classPoolSize` returned the FILE's width (16) where the POOL's (14) was meant *with the correct number in the comment directly above it*; `globalStdType` kept a second tag→StdType table that had diverged (no `float` arm); `notOperatorFor`'s range arm swallowed `float` **while its header boasted the `match` had FIXED that exact fall-through**; `floatCondCodeForPred` + `floatCmpSwapsOperands` had to agree with nothing making them; `valueConfinedTo` and `analyzePressure` gave opposite answers about a ∅-allowed value; the bootstrap **disagreed with itself** (`CoerceValueToExpectedKind` rejected float→int, `ConvertArgToParamType` truncated it) — **and the fix for THAT re-spelled the integer family 19 lines from its home**, which the review caught. ⭐⭐ **`Parser.mintPhi` hardcoded `argType: i64`, and its comment NAMED THIS RUNG**: *"the scaffold the float (XMM) register class will need at P1.0d.4, and that is when it acquires a consumer and a reason to be right."* **The rung gave it the consumer and not the reason** ⇒ `var f = 0.0` in a loop — the commonest float idiom there is — **crashed the emitter**, and the authored spec could not see it (its phis carry INTs). **Only extending the instrument found it.** ⇒ **READ THE COMMENT THAT NAMES YOUR RUNG** |
| | ⚠ **Deferred out of P1.0d.4, named not hidden** | **Wave 2 = the float ABI** (a float PARAM/RETURN needs the xmm arg slots; **measured** — `takeFloat(42.0)`, a literal with no coercion, panics in the callee's ABI reception). Win64 preserves all **128 bits** of xmm6-15 and `movsd` ZEROES 64-127, so callee-saved XMMs drag in 16-byte slots + leaf misalignment together — that is why Wave 1 is all-caller-saved. Also: **`floor`/`ceil`/`round`** (`roundsd` is SSE4.1, a three-byte escape) ⇒ their own rung; **float const-FOLDING** is rejected (E2015) — it needs software IEEE-754 in the compiler, and v1 does not do it either; **`0.0 - x` ≠ true negation on `-(+0.0)`** — unobservable until `print` (P1.2), and v1 has shipped it for its whole life; **`f64→f32` still coerces implicitly** — the same lossy-narrowing bug wearing different types, outside the float→int ruling, **unresolved**; **`trunc` is reserved** wherever a call is parsed, silently shadowing a user function (v1 has the identical property for its whole intrinsic set) |
| **P1.0c** | **measure the stdlib cone** | against the **upgraded** harness. Cheap, and it sets the boundary — see above |

### ⚠⚠ "The scalar core is DONE" was FALSE — measured 2026-07-13

The 126 spec tests shv2 passes were all written **by shv2, for shv2**. Run against `/specs` — the real,
accumulated definition of the language, written by people who were not trying to make shv2 look good —
the scalar core scored **48 passing out of 2,746**. *Not one of the 126 self-authored tests had ever
used a parenthesis.* **This is exactly the bias Workstream S exists to break, and it broke it on the
first run.** Every item below was reproduced against the compiler, not inferred:

| Gap | Evidence |
|---|---|
| **parenthesized expressions** | `(a + b) * c` ⇒ `E2004: Expected expression but got '('`. The Pratt parser has **no grouping primary** |
| **`true` / `false` literals** | ⇒ `E2004: Expected expression but got 'true'`. The `bool` TYPE exists (comparisons make it, `if` eats it); only the literals are missing |
| **`not` / `and` / `or`** | not parseable |
| **top-level `typealias`** | ⇒ `E2015: Unsupported: top-level typealias (M1 parses only function declarations)`. **1080 cases** — the single biggest blocker in the corpus, and the plan had it scheduled LAST, at P1.9. Most `/specs` files simply OPEN with `typealias Integer = int(i64.min to i64.max)` |
| **block scoping** | `Parser.maxon` calls **neither** `Scope.pushScope` nor `popScope` — both are correct, and both are **DEAD CODE** (`Scope.maxon:267,276`). Every `if`/`while` body declares into the FUNCTION frame. One twin silently accepts an invalid program; the other feeds malformed SSA to the allocator and panics (`RegisterAllocator.maxon:645` — a **symptom**, not the cause) |
| **void functions** | **ANY** void function panics the compiler, *even if never called* (`IrBlock.maxon:238`, no terminator). A second, distinct panic fires on an explicit bare `return` (`LowerMaxonToStd.maxon:352`), whose own message wrongly assumes only `main` can be void |
| **`a / 0`** | escapes as a raw `0xC0000094` hardware trap. `specs/safety.md` requires a clean `panic` + exit 1 |
| **floats · chars · bitwise** | had **no rung anywhere on the ladder**. Folded into P1.0d (user decision): they are scalar primitives, not mechanisms. ✅ **ALL THREE DONE** — bitwise + chars at P1.0d.2, floats at P1.0d.4 |

**✅ EVERY ROW ABOVE IS NOW CLOSED. P1.0d IS DONE, and the SCALAR CORE IS — this time measured, not asserted: 355/355, from 48-of-2,746 against `/specs` when the claim was first made.** The table above is kept because the lesson outlived it: *"the scalar core is DONE"* was **false**, and it was false because the 126 tests it rested on were **written by shv2, for shv2** — not one had ever used a parenthesis. **The corpus is the only judge that was not trying to make shv2 look good.**

⚠ **AND THAT IS STILL TRUE OF THE THING YOU ARE ABOUT TO SHIP.** P1.0d.4 shipped **float negation** and **float phis** with **ZERO test coverage** — the first was caught only by porting `unary-negation.md`, the second only by teaching `scale-test` to emit a float. Both were features the rung *itself wrote*. ⇒ **Step 0 of a rung is the SPEC PORT LIST, and it is the coordinator's** (now in the rung skill, `03b4a47dc`). Inheriting a ported corpus is not doing it.

### ⭐ FRAME POINTERS, on every function — decided at P1.0d.3, but the forcing reason is **P1.5**

**shv2 now emits `push rbp` / `mov rbp, rsp` in EVERY prologue, leaves included** (user decision,
2026-07-15). It previously used frame-pointer-omitted (rsp-only) frames **while still reserving rbp** —
a **strictly dominated** state: it paid the register cost and took none of the benefit. **rbp stays
NON-allocatable; the pool is unchanged at 14.** Addressing stays **rsp-relative** — the divergence from
v1 is deliberate and narrow: **rbp is for the CHAIN, not for addressing.**

**The stack trace only EXPOSED this. The reason it is not a P1.0d.3 detail is
[`__gt_morestack`](maxon-selfhosted/Compiler/Targets/X64/X64Backend.maxon#L7094):** v1 grows a green-thread
stack by allocating 2×, copying, and then **FIXING THE SAVED-RBP CHAIN by the relocation offset** (step 6b
walks the chain on the new stack, adjusting every saved rbp in the old range). GT stacks are 2 KB initial
and **RELOCATE** on growth, Go-`runtime.morestack`-style. ⇒ **A relocating stack must find and fix every
interior frame pointer, and the chain is how it finds them.** **R3 @ P1.5 commits shv2 to exactly that
model**, so shv2 needs frame pointers there *regardless of panics*. Landing them now moved all 279 goldens
**once**; retrofitting at P1.5 moves the same goldens **then**, onto a shipped runtime. *Do the hard things
early.*

⚠ **A frameless leaf could not be spared:** you cannot unwind OUT of one without knowing its frame layout,
and the faulting `main` **is** one — so framing only "framed" functions does not help. (And because no shv2
function ever wrote rbp, rbp was *invariant program-wide*: walking `[rbp]` gave garbage, not a short trace.)
**`.pdata`/`.xdata` + `RtlVirtualUnwind` is the FPO-correct alternative and was rejected: NO compiler in
this tree emits unwind info**, so it has no port source and is its own rung — and it does **not** solve
morestack's fixup, which needs the chain anyway.

⇒ **P1.0d lands before P1.1**, and it pulls the **declaration half of P1.9 forward** (parse + resolve a
top-level ranged `typealias`; the corpus's ranges are wide, so the *checks* — `ExpandCastRangeChecks` /
`InsertRangeChecks` — stay at P1.9 where they belong). Floats need a whole new register bank (there is
**no XMM class** in the allocator today), so they are the last slice of the rung, not the first.
| **P1.0r** | **R1-core — the ALLOCATOR + refcounting runtime** ⭐⭐ | **← NEXT.** *(Was "R1 @ P1.2". Promoted ahead of structs 2026-07-15 — see the RESTRUCTURED box: a struct is a heap value, so the heap cannot come second.)* The slab allocator · `__mm_alloc` · `__mm_incref`/`__mm_decref` · the `__destruct_*` cascade. **The allocator ALWAYS RETURNS ZEROED MEMORY, from commit 1** — a property of the allocator, not of each caller (it cost v1 three separately root-caused bugs; see ARCHITECTURE.md → "Allocator: the zeroing contract"). ⚠ **THIS RUNG MUST DECIDE THE RUNTIME'S *FORM*** — hand-assembled machine code vs. v1's `runtime.std` (6,049 lines of Std-IR text through the ordinary backend). **The excuse for deferring it has now EXPIRED: its precondition was the Std memory band, and P1.0d.5b shipped it.** See the R1 box in Workstream R — **the named risk is inertia**, 6 hand-assembled functions becoming 5,000 by default. **Write the reason down.** ⚠ Its acceptance test is **P1.1a**, not a bespoke spec: an allocator with no heap value to allocate is untestable, so the two are adjacent on purpose |
| **P1.1a** | **structs** — R1's DOGFOOD ⭐ | *(was the struct half of `P1.1`; its `disabled-test:` markers read correctly unchanged.)* concrete, **trivial-ownership only** — scalar/float fields, so the destructor is NULL and no field write increfs. Heap-boxed via `__mm_alloc`, **uniform 8-byte field slots** (`sizeof` PINS this: `sizeof(Point)`=16, `sizeof(Vec3)`=24, and `sizeof(Outer{p Point, n Integer})`=**16** — a struct field is a POINTER). Field access = `loadIndirect`/`storeIndirect` at a real offset, **the ops P1.0d.5b already built and whose comments NAME this rung**. Methods · `self` · `static function create` · `Self{…}` (construction is restricted to the type's own methods — **E3076**) |
| **P1.1b** | **enums + `match`** | *(was the enum/match half of `P1.1`.)* **HEAP-FREE — this is why it is its own slice**: both references collapse a payload-free enum to an `int` typealias + constants, and `match` is a chain of two-way `condBranch` blocks (**never bend `IrBlock.CondBranch`'s one-branch-per-block invariant** — P1.0d.4's float compare already set that precedent). Payload-free `union` rides the identical path. ⭐ **RANGE ARMS USE ORDINAL ORDER** — the declaration order — **NOT the raw value** (user ruling, 2026-07-15). ⚠ **The bootstrap gets this WRONG and shv2 must not copy it**: see OPEN.md #21 |
| **P1.2** | **`String` + ownership + drops** ⭐ | **THE CRUX.** ~~String is the FIRST heap value~~ — **FALSE, corrected 2026-07-15: a scalar struct is (P1.1a), and it is simpler.** String is the first *non-trivial* heap value, and that is still the point: real, needed by everything, and trivially-elemented so it forces no descriptor. Hardcoded `__ManagedMemory`(40B) + `String`(16B) bootstrap structs; rdata `capacity = -2` sentinel; synthesized `__destruct_String`; interpolation of **primitives**. **It rides P1.0r's `__mm_alloc` rather than introducing one** — which is the whole benefit of the reorder. mm-trace gates from here. **`own.drop` declares BOTH arms now**; the descriptor arm is unreachable until P1.6 |
| **P1.3** | **owned payloads in enums/unions** | *moved into Phase 1* — `compilerError(text String)`, `fail(reason String)`. Needs only P1.1a/P1.1b + P1.2's drops. Errors (P1.4) want it too: the harness calls `e.displayReason()` |
| **P1.4** | moves + borrows (NLL) · **errors** | first program-rejection point. `throws`/`try`/`otherwise` (v1's dual-register `(value, errorFlag)`, verbatim) + drops on the error edge. **36 harness sites** |
| **P1.5** | **closures + `async` + escape → `shared`** ⭐⭐ | **THE THREE ARE ONE MECHANISM, AND THEY CO-LAND — this is the plan's "do the hard things early" in its purest form.** Capture-into-heap **IS** escape: a closure captures into an env block; a green thread captures into a task frame. Escape analysis is needed for heap correctness regardless — so build all three together and `EscapeAnalysis` gets **both** capture channels *from birth*. Land escape single-threaded and add `async` later and you bolt a **second capture channel** onto it: v1's `sys.dropTypeParam` split-brain mistake, exactly. Minimal closure = int capture, 0-arg, heap env, uniform `(args, env)` ABI (v1 lifts at parse time). Minimal `async` = `async`/`await` + Promise + the worker pool's needs. Escape is the **only** place refcounts appear. **Track `% values promoted to shared`** — if it's 40%, static ownership bought nothing. **Runtime slice R3 lands here** (the GT scheduler + async subprocess stdio) |
| **P1.6** | **generics + layout descriptors** ⭐ | declarations + instantiation. ⇒ `own.drop`'s descriptor arm goes **LIVE** here |
| **P1.7** | **`Array`** | = P1.6 ∘ P1.2 — the first real integration proof (managed elements → element-destroy through the descriptor). ⇒ unlocks **`b"…"` byte-string literals** |
| **P1.7a** | **interfaces + witness tables** ⭐ **(promoted from P2.1, 2026-07-13)** | Static conformance (`Hashable`, `Equatable`, `Stringable`). **No existentials** — shv2 stores nothing at interface type. **Forced into Phase 1 by MEASUREMENT (P1.0c):** `Set`'s element constraint needs `Character.hash`/`.equals` dispatched, and `Main.maxon:233-236` interpolates a bare `FilePath` **struct** through `Stringable`. Under dictionary-passing there is no route to `element.hash()` on a type parameter *except* a witness slot. ⇒ promotes the stdlib's interface decls DECLARE→EMIT, and unlocks `"{userStruct}"` |
| **P1.7b** | **`Set` + `Hashable`/`Equatable`** ⭐ **(promoted from P2.3, 2026-07-13)** | **Forced into Phase 1 by MEASUREMENT (P1.0c), and by nothing the harness wrote:** `String.trim()` (13 sites) → `CharacterSet.whitespacesAndNewlines()` → `typealias CharSet = Set with Character` ([CharacterSet.maxon:19](stdlib/CharacterSet.maxon#L19)). *"`Set` rides `Map`'s exact mechanism"* is **false as sequencing** — **`Set` is reached and `Map` is NOT.** The `stdlib-shv2/` fork could have cut the `trim()`→`CharacterSet` edge; **REJECTED — do the hard things early.** `Map` stays in Phase 2 (multi-param generics; genuinely unreached) |
| **P1.8** | `String` methods · `for-in` | real `String.equals` body (struct-`cmp` → `methodCall`); hardcoded `for-in` over Array/Range/String. ⚠ **`trim()` lands here and it is the thing that dragged `Set` in** — so P1.7a/P1.7b must precede it |
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

## Workstream S — `/specs` DRIVES development, from now on ⭐

**Every rung is driven by porting the real spec files from [`/specs`](specs/) into
[`/specs-shv2`](specs-shv2/) — not by authoring a bespoke spec for the occasion.** Start as early
as possible; the first slice runs *before* P1.1.

**Why the corpus, and not hand-written specs.** `/specs` is **276 files / ~2,584 `exitcode` cases**
— the accumulated definition of the language, written against real semantics by someone who was not
trying to make shv2 look good. A spec authored fresh for a rung tests what its author *remembered*
to test. A ported one tests what the language *actually promises*, including every edge case a past
bug already paid for. shv2's five allocator stress specs make the point: written as a corpus shv2
had not seen, they turned up **two real bugs** in code that was green.

**The formats are IDENTICAL — porting is `cp`.** shv2's `SpecParser` was modeled on the main suite
and shares its every convention: **275 of the 276** `/specs` files already use the same
`<!-- test: <name> -->` markers under the same `## Tests` heading.

> ⚠ **CORRECTED 2026-07-15 — this paragraph asserted a capability that was never built, which is this
> plan committing the project's own signature bug.** It claimed *"the **four** fences shv2 accepts
> (` ```maxon ` / ` ```exitcode ` / ` ```stdout ` / ` ```maxoncstderr `) cover ~6,980 of 7,875 fences"*
> and dismissed ` ```stderr ` as *"triage, not a porting layer."* **Measured against the source:**
> - **There is NO ` ```stdout ` fence. There never was.** `SpecParser.maxon` defines exactly
>   `SourceFence` / `ExitCodeFence` / `CompilerErrorFence` — **three**. `/specs` has **711** ` ```stdout `
>   blocks. It is moot only because a shv2 program **cannot yet produce stdout** (no `print`, no `String`
>   until **P1.2**) — so it is P1.2's obligation, not a gap today. **Do not let the count be re-asserted
>   from this document.**
> - ` ```stderr ` was **32 blocks, and it was NOT triage** — it is a program's **RUNTIME** stderr, wholly
>   distinct from ` ```maxoncstderr ` (the COMPILER's). It is what `specs/safety.md` pins a panic with, so
>   **P1.0d.3 had to build it** (`bdff8491f`). ✅ **Now shipped**, with `stripFaultRipSuffix` mirroring the
>   reference's `TestRunner.cs:1427`.
>
> ⇒ **Fences shv2 accepts today: FOUR** — ` ```maxon ` · ` ```exitcode ` · ` ```maxoncstderr ` ·
> ` ```stderr `. **Missing: ` ```stdout ` (711 blocks, gated on P1.2)**, ` ```text ` (243, mostly prose),
> ` ```mm-trace ` (1). **There is still no translation step to build.**

### ⚠ The hard part: port at TEST granularity, not FILE granularity

**Most spec files depend on far more of the language than the feature they name.** Measured across
all **3,259** ` ```maxon ` blocks in `/specs`: **36% use a string literal, 32% declare a
type/union, 26% use `try`/`throws`, 24% call `print`.** `specs/arithmetic.md` is about `+` and
`mod`, but a sibling case in the same file will happily `print` an interpolated string.

⇒ **A spec FILE is not a portable unit. A test CASE is.** File-level `status: draft` — which is all
[SpecTestRunner.maxon:137](maxon-shv2/Testing/SpecTestRunner.maxon#L137) supports today — is
therefore *the wrong granularity*: it would strand a file's in-core cases behind its out-of-core
ones.

**Use the marker the project already has: `<!-- disabled-test: <name> -->`.** It is the established
convention, honored by **both** existing runners — v1 skips it at
[SpecTestRunner.maxon:2233](maxon-selfhosted/Testing/SpecTestRunner.maxon#L2233), and the C#
runner's marker regex is `<!--\s*(?:disabled-)?test:\s*\S+\s*-->`
([TestRunner.cs:1760](maxon-sharp/Testing/TestRunner.cs#L1760)) — and the in-tree usage already
carries a **reason on the following comment line**:

```
<!-- disabled-test: http-client.async-trace-interleave -->
<!-- AsyncTrace -->
```

> **So the ONE piece of machinery Workstream S must build is small: teach shv2's `SpecParser` to
> recognize `disabled-test:` and skip it** — parsed as a test boundary, never compiled, never run,
> and **no `.test` golden generated**. Goldens then accrete only as tests are enabled, and the
> fragment diff stays reviewable instead of becoming a ten-thousand-file dump.
>
> **Port convention:** flip `test:` → `disabled-test:` for any case shv2 cannot yet pass, and put
> **the rung that unlocks it** in the reason comment (`<!-- P1.2 String -->`). Enabling a test is
> then a one-word diff, and it is the deliverable of the rung that earns it.

**Keep the copied file otherwise byte-identical to its `/specs` original**, so a future `diff`
against upstream shows real drift rather than porting noise. The marker flip is the *only*
sanctioned edit.

### ⇒ The disabled-test reasons ARE the ranked roadmap — the thing the compass promised, for free

Because every disabled case names the rung that unlocks it, one `grep` **groups the entire
remaining language surface by milestone**:

```
$ grep -A1 -h 'disabled-test:' specs-shv2/*.md | grep -o 'P1\.[0-9]*' | sort | uniq -c | sort -rn
    412 P1.2      ← String + heap unlocks 412 cases
    288 P1.6      ← generics
    201 P1.4      ← errors
    ...
```

That is **exactly** the `TOP UNSUPPORTED` table `selfhost-distance` was going to produce — the one
thing genuinely lost when the compass was cut. Here it costs **nothing**: no parser recovery mode,
no 626 recoverable panics, no 485-line reporter. And it is strictly better, because it ranks by
**cases that must actually pass**, not by syntax-node frequency.

### ⇒ The draft count is the ratchet — and it is what the cut compass was reaching for

`selfhost-distance` was meant to answer *"how much of the language does shv2 have, and what should I
build next?"* It was cut because its true price was a parser recovery mode plus making `panic()`
recoverable at 626 sites — **it bought a number, not a feature.**

**The un-drafted spec count answers the same question for free**, and answers it *better*:
- **Zero new infrastructure.** The runner, the frontmatter key, and the skip are already shipped.
- It measures **BEHAVIOUR** (the spec runs and produces the right answer), where the compass measured
  only **ACCEPTANCE** (the frontend didn't choke). SHD=0 would have meant "shv2 parses its source,"
  not "shv2 compiles it correctly." A green spec means it works.
- The ranked list of *what is still draft* is a roadmap, in the same way the compass's
  `TOP UNSUPPORTED` table was meant to be — but grounded in cases that must actually pass.

**The gate: an ENABLED test may never be re-disabled.** That is the per-unit non-regression ratchet
the compass promised, enforced on behaviour and at no cost. *(This does not reopen the compass — it
retires the last argument for it.)*

### The slices

- **S1 — ❌ THE BULK PORT IS CANCELLED (user directive, 2026-07-13). Port spec files ON DEMAND, by
  the rung that needs them.** The projection here was **≥650 of 3,259 cases portable today**. The
  sweep was run once, as a *measurement*, and the real number was **48 of 2,746** — because most
  `/specs` files open with a top-level `typealias` (1080 cases) and because the core cannot parse a
  parenthesis. **A bulk port would therefore have landed ~2,700 `disabled-test:` markers: 98% of the
  corpus shelved. That is not a roadmap, it is noise** — the ranked-by-rung grep only says anything
  once the scalar core is complete.
  ⇒ The sweep **already did its job**: it named P1.0d. It is discarded, and from here **each rung
  copies in exactly the `/specs` files it needs** (P1.0d takes `parentheses.md`, `bool-type.md`,
  `block-scoping.md`, `empty-block.md`, `discarded-results.md`, `ranged-typealias.md`, …). The
  `disabled-test:` marker still governs *within* a ported file, because a ported file still mixes
  in-core cases with out-of-core ones — that is unchanged, and it is why the marker exists.
  **The lesson stands, and it is the whole point of Workstream S: a corpus shv2 did not author found,
  on its FIRST run, eight gaps and four outright bugs in code that was green.**
- **S2 — every rung, P1.1 onward: a rung is DONE when the cases it unlocks are ENABLED and green.**
  Author a bespoke spec only where no real one exists — i.e. for shv2-specific surface (allocator
  stress, IR goldens, `E5001` pressure), never for a language feature `/specs` already covers.
- **S3 — triage the remainder:** the 19 `status: selfhosted` files (where v1 and the C# bootstrap
  genuinely conflict), the ` ```text `/` ```stderr `/` ```mm-trace ` blocks, and any case whose
  expectation is bootstrap-specific rather than language-level.

### ⚠ This is what makes P1.0a (the parallel pool) pay for itself

At today's measured **~24 ms/test**, the full corpus is **~2,584 tests ≈ 60 s serially** — the
iteration loop stops being instant exactly when the corpus lands. On a 12-worker pool it is a few
seconds. **So the pool should land before or with the bulk port, and the two justify each other:**
`async` is in Phase 1 because it is a hard mechanism that must not be retrofitted (§P1.5), and the
corpus is what makes it *also* the thing keeping the loop fast.

---

## Phase 2 — self-host

**= Phase 1, plus exactly what shv2's own 21,038-line source adds.** Bounded by measurement
against that source:

| # | Mechanism | Forced by |
|---|---|---|
| ~~**P2.1**~~ | ~~interfaces + witness tables~~ | ⬆ **PROMOTED TO PHASE 1 (P1.7a)** — P1.0c measured `Stringable` and `Hashable` dispatch inside the harness's own cone |
| **P2.2** | **conditional conformance** | per-gid witness tables + synthesized thunks — one witness blob per `GenericInstanceId` (`Array<Byte>: Equatable`, …), each method slot pointing at a thunk with the interface-declared signature whose body forwards to the shared generic impl, materializing that instance's implicit layout/witness args (v1: `synthesizeWitnessThunks`). **The last big generics piece.** ⇒ **[GlobalDataTable.maxon:23](maxon-shv2/Compiler/Targets/Shared/GlobalDataTable.maxon#L23)** `Map with (ByteArray, String)` compiles — **its acceptance test, kept deliberately** |
| **P2.3** | **`Map`** *(`Set` ⬆ PROMOTED to P1.7b)* | multi-param generics. **`Map` is genuinely unreached by the harness — P1.0c proved it with the machine code** (its `EnvMap` arms compile to a bare `mm_decref`), so reachability-seeded lowering holds and it stays here. 12 `Map` typealiases in shv2's own source. ⚠ **`Set` does NOT wait for it** — it was reached in Phase 1 by `String.trim()`, which is why *"`Set` rides `Map`'s exact mechanism"* was false as sequencing |
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

> ### ⭐ R1 @ P1.2 MUST DECIDE THE RUNTIME'S **FORM**. P1.0d.3 DEFERRED IT — DELIBERATELY, NOT BY INERTIA.
>
> **The panic runtime (P1.0d.3) is HAND-ASSEMBLED machine code**, per the budget line above and the C#
> bootstrap's approach. That was a **user decision (2026-07-15), taken with the alternative on the table**,
> and it was scoped to *that rung* — ~6 functions, needing **zero** new Std ops. **It is NOT a decision
> about the other ~5–7k lines, and R1 must make that one on purpose.**
>
> **The alternative, and it is real: v1 does NOT hand-assemble its runtime.**
> [`maxon-selfhosted/Compiler/Runtime/runtime.std`](maxon-selfhosted/Compiler/Runtime/runtime.std) is
> **6,049 lines of Std-level IR text**, parsed by
> [`StdParser.maxon`](maxon-selfhosted/Compiler/Runtime/StdParser.maxon) (992 lines) into `IrFunction`s
> and pushed through the ordinary backend — so **the runtime gets register allocation and
> target-independence for free**, and every later slice is IR text rather than bytes. v1 hand-emits
> **only** the VEH thunk (`X64Backend.maxon:6948`), because the OS calls it with a raw ABI. **That hybrid
> is the shape to weigh.**
>
> **Why it was NOT adopted at P1.0d.3:** shv2's Std tier declares `memory`/`system` bands but **only
> produces `arith` and `call`** — no `globalAddr`, `loadIndirect`, `funcAddr`, `osWrite`, `osExit`. The IR
> route needs those, and they are **P1.2's own work**. ⇒ **At R1 the precondition is satisfied and the
> excuse expires. Choose then, with the ops in hand, and write the reason down here.**
> ⚠ **The named risk is inertia: 6 hand-assembled functions becoming 5,000 by default.** This box exists
> so that cannot happen quietly.

- **R1 @ P1.2** — slab allocator · `__mm_incref`/`__mm_decref` · the `__destruct_*` cascade ·
  `__ManagedMemory` · the **string runtime** (`mrt_alloc_with_dtor`, `memcpy`,
  `mrt_i64_to_string` / `mrt_f64_to_string` / `mrt_bool_to_string` — interpolation is a
  compiler-emitted memcpy chain, so these are the only helpers it needs — and `mrt_write_stdout`)
  · the DebugStream producer (schema-compatible port of `RuntimeEmitter.DebugStream.cs`), which is
  what lets mm-trace gate P1.2 onward. v1's force-seeded bootstrap roots (`__slab_init`,
  `__mm_alloc`, `__managed_mem_create`, …) become a real obligation here.
  **⇒ The port must also carry the `__DebugStream` builtin (Workstream O3)** — the `Log` events
  (`0x60–0x63`) are emitted by *shv2's own source*, so if shv2's backend does not emit the builtin,
  **the compiler's self-trace dies exactly at the self-host boundary**, which is the point at which
  it is most needed.
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

## Workstream O — the compiler traces ITSELF ⭐

**Small, and it lands FIRST — it is a debugging instrument, and an instrument that arrives after
the bug is worthless.** shv2's `Logger` today formats a `String` and prints it to **stderr**. That
survives exactly as long as the compiler is single-threaded. **P1.0a interleaves N workers into one
stderr**, and P1.5 puts green threads under them; at that point a text log is not degraded, it is
*useless* — you cannot tell which worker, which compilation unit, or which phase a line came from,
and the lines themselves are torn.

**The mechanism already exists — the bootstrap has all of it.** `maxon.exe` compiles binaries that
carry a shared-memory ring (128-byte header, 8-byte packed entry headers, ticket-spinlock reserve),
and `maxon monitor` creates the segment, spawns the child with `MAXON_DEBUGSTREAM` set, drains the
ring, and decodes it — parsing real names out of a `MXDS_TAGS` blob in the PE. **shv2 is a Maxon
program compiled by `maxon.exe`, so it already runs under that monitor.** The one missing link:
the producer is wired only into *runtime internals* (mm / sched / dbg events). **Nothing lets user
Maxon source put an event into the ring.** That is the whole gap, and it is a builtin.

⇒ **Workstream O does NOT depend on Workstream R.** It is not blocked on the shv2 runtime, on
ownership, or on `String`. It can land against the compiler as it stands *today*.

**Two tiers, because the hot tier must not allocate.** A formatted-text log inside the register
allocator would (a) allocate into the very `mm` stream you are trying to read and (b) cost more
than the work it is measuring. `Logger`'s `LazyMessage` thunk does not save you: the closure env is
built at the call site whether or not the level check passes.

- **Tier 1 — structured, ZERO-ALLOC.** Event code + fixed numeric args, exactly like the existing
  Dbg events. Names are **interned at compile time** into a `MXDS_STRS` PE blob (the `MXDS_TAGS`
  trick, reused), so an event carries a `u16` id and the monitor prints the real name. No `String`,
  no thunk, no allocation. **This is the tier the passes use.**
- **Tier 2 — text.** A length-prefixed UTF-8 tail for the rare human message. Allocating, and it
  says so. `entry_size` is a `u16`, so an entry caps at 64 KiB — truncate, never tear.

**Every event carries `gt` + `p_id`.** Obtained the way `EmitDbgCallCore` gets them (`LoadCurrentP`,
NULL-guarded). **This is the entire point of the workstream** — it is what lets the monitor demux N
workers back into per-worker, per-unit timelines instead of a shuffled pile. An event also carries
a `unit_id` (which fragment / which function), so a parallel harness run reads as one timeline per
unit rather than one per process.

**The schema is FROZEN — so these are NEW codes, in a free range** (`0x5F–0xFD` is unused):

| code | event | payload after the 8-byte entry header |
|---|---|---|
| `0x60` | `LOG_PHASE_BEGIN` | `gt`(8) · `p_id`(8) · `phase_id`(2) `rsvd`(2) `unit_id`(4) — 32B entry |
| `0x61` | `LOG_PHASE_END` | *(same)* |
| `0x62` | `LOG_EVENT` | `gt`(8) · `p_id`(8) · `cat`(1) `lvl`(1) `event_id`(2) `unit_id`(4) · `arg0`(8) · `arg1`(8) — 48B entry, the Dbg shape |
| `0x63` | `LOG_TEXT` | `gt`(8) · `p_id`(8) · `cat`(1) `lvl`(1) `len`(2) `unit_id`(4) · UTF-8 tail, zero-padded to 8 |

**Keep the two-tier gating, and inline the guard** — the rule ARCHITECTURE.md already states for
shv2's producer applies here from commit 1: compile-time (`--debugstream` off ⇒ **zero
instructions**), runtime (`__ds_base == 0` ⇒ inline bail, *before* any CALL). The C# producer's MM
events pay two real CALLs before the runtime-off check; do not reproduce that wart.

### The slices

- **O1 — NOW, before P1.0a.** A `__DebugStream` builtin in **`maxon-sharp`** (callable from Maxon
  source), the four event codes, the `MXDS_STRS` intern blob, a DebugStream **sink behind
  `Logger`'s existing category/level API** (the call sites do not change), and `maxon monitor
  --filter=log` decode. **No dependency on anything in Phase 1.**
- **O2 — with P1.0a.** `gt`/`p_id`/`unit_id` demux in the monitor: per-worker and per-unit
  timelines. The pool's first debugging tool exists the day the pool does — not a month after it.
- **O3 — inside R1 @ P1.2.** shv2's own backend emits the same builtin, so the trace **survives
  self-host**. This is not new work: it is an *obligation on* the DebugStream producer port R1
  already carries. Name it there or it will be forgotten.

### ⚠ It is also the replacement for a timing instrument P1.0a is about to BREAK

`CompileTimings`/`CompileMemory` accumulate into **global** per-phase timers. Under a worker pool
two workers are in different phases at once, and a global accumulator cannot express that — the
numbers do not just get noisy, they stop *meaning* anything. `LOG_PHASE_BEGIN`/`END` give properly
nested, per-worker, per-unit spans, and the disjoint-and-sums-to-total check gets computed **from
the trace** instead of from an accumulator that is structurally unable to be right. *(This is the
"a dominant cost hid in the wrong timing bucket" failure, four times over in v1, and the parallel
pool is about to make the bucket itself invalid.)*

**And the trace can be a GOLDEN.** Normalize it the way `mm-trace` already normalizes — drop the
timestamp, dense-renumber ids in first-appearance order — and the **worker-count-invariance gate**
at the Phase-1 GATE sharpens from "`-j1` and `-jN` agree on the *verdicts*" to "they agree on the
**per-unit event sequence**." That is a far tighter net, and it costs nothing extra to hold.

---

## Beyond the two phases

**Broaden:** general `Iterable` + associated types · `List`/Json/… · arm64 + wasm · coverage ·
inliner. *(Two things LEFT this list on 2026-07-13. **`async`/green threads** are core, at P1.5.
**Porting the spec suite** is no longer an endgame chore — it is **Workstream S**, the driver of
every rung, starting at P1.0b.)*

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
- **Workstream S — the spec corpus IS the development driver.** See its own section below. *(It was
  scoped here as "pure analysis — which spec ports at which rung," with the porting itself deferred
  to Beyond. That was backwards: the corpus is the driver, not the aftermath.)*

---

## Where we are

**The scalar core is ~~DONE~~ INCOMPLETE** (old M1–M5). What genuinely works: `let`/`var` · full-Pratt
arithmetic/comparison/unary · `if`/`else` · `while`/`break`/`continue` (on-the-fly SSA +
`EliminatePhis`) · functions with params + calls · integer `/` and `mod`.

⚠ **What "block scope" in that list actually means: nothing.** `Scope.pushScope`/`popScope` exist,
are correct, and are **never called** — a `let` inside an `if` leaks to the function frame. The claim
survived because every one of the 126 tests was written by shv2, for shv2. **P1.0d closes this and
the seven other measured gaps** (parens, `true`/`false`, `not`/`and`/`or`, void functions, top-level
`typealias`, floats/chars/bitwise, and divide-by-zero-as-hardware-trap). See the ladder.

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

- **Per rung:** `maxon-shv2 spec-test` stays green (**281/0** as of 2026-07-15, and growing with
  every Workstream-S port); ownership rungs (P1.2+) also assert an `mm-trace` block via
  `maxon monitor`.
- **The ratchet (Workstream S):** **an ENABLED spec case may never be re-disabled.** Behavioural,
  per-unit, and free — this is what the cut compass was for. A rung's deliverable is the set of
  `disabled-test:` markers it flips to `test:`.
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
