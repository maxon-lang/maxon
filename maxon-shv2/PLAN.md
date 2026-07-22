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
**44,671 lines** (measured 2026-07-16 — see "The honest sizing"), with a working `spec-test` runner
(**specs-shv2 371/0** as of 2026-07-16 — it was
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
> ### ✅ **`P1.0r` IS CLOSED (2026-07-15) — shv2 HAS A HEAP. `specs-shv2` 355 → 357/0.**
> `osAllocPages` (the `system` band's FIRST producer) · a VirtualAlloc bump allocator, **always zeroed** ·
> `__mm_alloc`/`__mm_decref`/`__mm_free` as a **builder-built `StdModule` through the ordinary backend** —
> no IR text, no parser · `__mm_alloc_count` + **a leak gate that fires** · and the first heap value:
> `type` · layout · `Self{}` · `static create` · field access. **`Point.create(3, y: 4)` ⇒ 7.**
> ### ✅ **`P1.1a` WAVE 2 IS CLOSED (2026-07-16) — a struct field can be WRITTEN. `specs-shv2` 357 → 371/0.**
> Field WRITES (`p.x = 30`) · instance + field MUTABILITY (both **E2013**, no new code) · field DEFAULTS
> (`export var value = 0`, type inferred from the literal). **NO new IR op** — a field write is
> `storeIndirect`, the exact mirror of the read `emitFieldLoad` already emitted.
> ### ✅ **`P1.1a` WAVE 3 IS CLOSED (2026-07-16) — INSTANCE METHODS + `self`, and E3014. `specs-shv2` 371 → 399/0.**
> Instance methods (`p.add(10)`) · `self` · bare-field access (`count` **and** `self.count` — ONE path,
> both lower to `loadIndirect(__self, offset)`; v1's `selfFieldLoad`/`selfFieldStore` split NOT ported) ·
> field VISIBILITY **E3014** (`type B` reading `type A`'s private field, TYPE-scoped not file-scoped) ·
> and — folded in — **E3011 at a field declaration** (`var n as Nonexistent` was a silent i64; the
> `undeclared` recovery cited an error gate that never ran). **NO new IR op** — a method is an ordinary
> direct call, `__self` at param 0, BORROWED (the caller owns the +1; the consume sink is unreachable —
> a struct-typed field is uninstantiable here). Scaffold (`VarInfo.isSelfField`, `Scope.declareSelfField`)
> got its first consumer, exactly as `Parser.maxon:1996` promised. **⇒ GREP FOR YOUR RUNG'S NAME FIRST —
> it paid a fourth time.** Ports: `type-methods.md` (5 enabled, `method-call-on-call-result` disabled ←
> P1.1b `match` + a primitive-receiver method), `self-keyword.md` (6), `export-var-fields.md` (8).
>
> ### 🔴 THE FINDS (both silent WRONG ANSWERS, both the signature disease, both caught by an INDEPENDENT pass):
> - **The type-vs-file gate was enforced by NOTHING** (coordinator sabotage). `requireFieldAccessible`
>   asserted "the gate is the TYPE, not the FILE ... measured" — but every corpus case reaches the field
>   from `main()` (inside no type), so weakening the predicate to *"inside ANY type"* — a compiler where
>   every method reads every type's private state — left the suite at **392/0**. FOURTH instance of #27's
>   shape (#4c, #23, #27). Pinned now by `field-visibility-is-type-scoped.md` (2 sabotage-red + 1 positive).
> - **`next.a` compiled to `self.a`; `next.readA()` to `self.readA()` — exit 0, no diagnostic** (independent
>   review). A self-field alias has `boundValue: 0`, and **ValueId 0 IS the receiver**; three sites read
>   `binding.boundValue` themselves where `parseVariableReference` guarded it — one fact, four homes. Fixed
>   by DERIVE: `requireStructBase` returns the box WITH the layout (`StructBase`), one reader of `boundValue`.
>   Pinned by `self-field-struct-typed.md` (4). ⇒ **OPEN.md #29.**
> - **The receiver-borrow REASON was false as written** (review): it claimed "scalar fields ⇒ no sink", but
>   `next = other` DOES emit an un-increfed heap store. The CONCLUSION survives — bare `self`-as-value is
>   refused (E2015) and a struct-typed field is uninstantiable, so no live struct pointer ever reaches a
>   field — but for a different reason, now written where load-bearing. ⇒ **OPEN.md #29.**
> ### ⚠ AND THE SCALE CORPUS IS BLIND TO THE MECHANISM JUST LANDED (optimizer) — the instance-method path
> measures **zero** because the corpus emits no methods. Same shape as #4d (closed for structs/heap/globals/
> idiv). Methods are the knob nobody added. ⇒ **OPEN.md #30.**
> ### ✅ **`P1.1b` WAVE A IS CLOSED (2026-07-16) — `match` ON SCALARS. `specs-shv2` 399 → 462/0.**
> `match` STATEMENT (`then` arms) + EXPRESSION (`gives` arms; closed a separate parser gap — `match` was not
> a primary → E2004) · value / `or` / int RANGE (`to` inclusive, `upto` exclusive, `min`/`max` open, negative)
> / float-range patterns · a `default` arm (a non-enum match requires one, E2026; must be last, E2029) ·
> the scalar-match diagnostics E2025/26/27/28/29/42/43 + block-statement (shv2 claims added; the codes
> pre-existed for csharp/selfhosted). **NO new IR op, and NO result slot** — a match is a chain of two-way
> `condBranch` test-blocks, and the `gives` join is a **block-arg phi** (`mintPhi` + `recordBranchEdge`),
> exactly as `if`/`else` carries a var. **Both references join through a slot (v1 via Mem2Reg, the bootstrap
> via a mutable slot); shv2's on-the-fly SSA does not and must not — that is why it has no Mem2Reg pass.**
> This was **P1.1b sliced into two waves** (scalar match first, then enums). **⇒ NEXT = P1.1b WAVE B**
> (payload-free enum + union + enum match + the **OPEN #21 ordinal range arms**), then `P1.2` (`String`).
>
> ### ⚠ THREE THINGS worth carrying (the last two were caught AFTER the implementer, one after the review):
> - **The implementer AGENT DIED to an API error** after reaching green but before committing. Nothing was
>   lost: the coordinator verified the whole state on a fresh build, confirmed the block-arg join by reading
>   the crux, spot-checked a novel input, and resolved the one question the agent died on (repeated
>   `matcharm`/`matchnext` labels are the established convention — `ifcont`/`whilehdr` reuse labels the same
>   way; blocks are keyed by BlockId). **A mid-flight death corrupts nothing when nothing merges unverified.**
> - **The scalar exhaustiveness gate (E2026) was pinned by NOTHING** (review sabotage): the only
>   not-exhaustive corpus case is an *enum* one (disabled), so `if not sawDefault throw E2026` had zero
>   coverage and disabling it kept the suite green. Fixed by an authored `error.match-not-exhaustive`. **The
>   #27 disease, a fifth time** — the review now sabotages every new gate by reflex.
> - **A cross-register-class match EXPRESSION panicked the backend** (`1 gives 5  default gives 7.5` → a float
>   give and an int give into one phi → `X64Backend:603 crosses register files`). The oracle UNIFIES such
>   arms (promote int→float); shv2 HAS `promoteToFloat` + the lattice, but a promoted match's RESULT is a
>   float **value with no nameable type yet** (`typealias F = float(…)` is itself E2015) — the promotion has
>   no consumer, the `mintPhi` trap. **A crash must never ship**, so it is REFUSED now (positioned E2015,
>   pinned by `match-expr-divergent-class.md`), and the promotion is filed for the float rung. ⇒ **OPEN.md #31.**
>   *(Same-class int/bool arms are untouched and match the oracle exactly — E3005 on a bool→int return.)*
>
> ### ✅ **`P1.1b` WAVE B IS CLOSED (2026-07-16) — payload-free ENUM + UNION + enum match + OPEN #21 ranges. `specs-shv2` 462 → 508/0. ⇒ P1.1b IS FULLY CLOSED.**
> `enum`/`union` DECLARATION (auto-increment tags, `= N` resets the counter to N+1, float-backed as i64 bit
> patterns) · `Color.red` ⇒ its tag · **enum `match`** rides Wave A's `condBranch` chain (case-name→tag +
> exhaustiveness E2026) · **⭐ ORDINAL RANGE ARMS (OPEN #21)** · payload-free `union` == enum · diagnostics
> E3030/E3031/E3032/E3034/E3075/E2046. **NO new IR op.** The new artifact is a **parallel-column `EnumLayout`**
> (array index = declaration index, `caseTags` column = the tag) — so the bootstrap's `Ordinal` "one field two
> meanings" hazard that IS OPEN #21 is **unrepresentable**. **The #21 rule** — a range is the OR of covered
> cases *by declaration index*, kept as a two-compare tag range only where no uncovered case's tag falls
> between the covered extremes — is implemented from the bootstrap `645a690cc` (v1's range code is the unfixed
> bug), and **VERIFIED against the oracle**: `Code{ok=500 notFound=200 serverError=404}` `ok to notFound`
> excludes serverError (→42). The review sabotaged the #21 predicate → 2 tests RED (pinned at runtime AND
> codegen). **⇒ P1.1b FULLY CLOSED. Then P1.2 (the crux) began — see its box below.**
>
> ### ✅ **`P1.2` (String — THE CRUX) IS FULLY CLOSED (Waves A–D, 2026-07-16..17) — literals + `print` + `==` (A), primitive INTERPOLATION → the first OWNED heap String + the drop model (B), static single-owner MOVES + use-after-move (C), and `String.append` growth/detach + a String's own drop (D). `specs-shv2` 508 → 523 (A) → 564 (B) → 602 (C) → 611 (D).**
> P1.2 is sliced into 4 waves along the heap-alloc / leak-gate boundary. **Wave A (ownership-free):** string
> literals as **immortal `.rdata`** (a fused 48B record, `capacity=-2`, **NO `__mm_alloc`, no drop** — leak gate
> trivially green), `print` (→ `__print_string`, a `WriteFile` runtime fn — shv2's FIRST stdout, ever), `==`/`!=`
> (→ `__str_eq`). **Wave B (the leak gate's + static-ownership drop model's real debut):** `"pre {x} suf"` lowers
> to ONE `__mm_alloc`'d fused record with its UTF-8 bytes INLINE (`buffer=record+48`, `parent=-3`, `capacity=length`),
> concatenating each part; `__int_to_string` (branch-free → **i64.min-safe**, no negate), `__bool_to_string`,
> `__str_copy`; an UNBOUND `print("{x}")` temp is **STATEMENT-scoped** (drops per loop iteration), a BOUND
> `let s = "{x}"` transfers to the binding (scope-exit drop); ownership keys on a **PROVENANCE bit** — owned-interp
> vs borrowed-rdata literal share `ValueTypeTag.string`, so a per-value `valueOwnsHeap` column rides in lockstep
> with `valueTags`. int-backed enum interpolation rides the integral path for free. **⇒ NEXT = WAVE C** (moves +
> use-after-move), then D (growth/`append`/detach). ⚠ **`__destruct_String` NOT built in B** — every B interp
> result is INLINE, so `__mm_free` reclaims record+bytes together and the destructor is `TrivialDestructor` (0);
> it arrives with the external-buffer consumer in Wave D. **See OPEN.md — the value-class column fix (a backend
> panic on a valid program, coordinator-found) and the O(N²) provenance find (optimizer).**
>
> ### ✅ **THE OWNERSHIP / DROP MODEL WAS HARDENED post-Wave-B (2026-07-16, user: "leaks are not ok"), 564 → 578/0, merged (`deac21315`).** Three MANIFESTING leaks (exit 101), all fixed:
> - **Returning an OWNED String across a call** (OPEN #37): a `returns String` fn now hands back a uniformly OWNED String — the CALLER owns the result (`mintOwnedCallResult`→`trackOwnedTemp`), the callee MOVES an owned return out, a borrowed literal/param return is heap-PROMOTED to owned (`promoteToOwnedString`). String PARAMS are reachable and promote. (This is the caller-side of borrow-vs-consume, PULLED FORWARD from P1.4 because the user won't ship a leak; the full param borrow-vs-consume ruling still stays P1.4.)
> - **Owned bindings in a nested block** (OPEN #38): the Wave B `closeBlock` "fail-safe leak" is retired — owned bindings drop on every edge that leaves a block ALIVE, exactly once (fall-through, `break`/`continue` down to `LoopContext.ownedFloor`; `emitScopeDrops` gained a `floor` param). `while … let s = build(i) … end` and struct-in-block no longer leak.
> - **A returned loop-local binding** — the sibling non-returning iterations leaked (the return move-out permanently stripped `ownedBindings`, which the fall-through drop also reads): found + fixed by the INDEPENDENT review (`deac21315`), the signature "one fact, two readers" bug.
> - **OPEN #39 reassignment drop (`4a2ae34f1`) — FIXED.** Then **WAVE C ✅ CLOSED (`6c06e698c`..`75bd75fac`, 587→597): static single-owner MOVES + use-after-move (E3102, shv2's FIRST ownership program-rejection).** `let u=t`/`s=t` MOVE (source poisoned `VarInfo.movedFrom`, drop-skipped, read = E3102); reassign REVIVES; conditional moves poison conservatively past the merge. Fixes OPEN #41 (`s=t` double-free → move). Independent review caught a paren-`(t)` gate hole that reintroduced the double-free; optimizer removed an O(N²) poison scan.
> - **OPEN #42 (use-after-move at field/method/store sites) ✅ FIXED (`51f543fdd`, 597→602)** — one shared `requireBindingLive` guard at the two funnels; sabotage-proven by the review.
>
> ### ✅ **`P1.2` WAVE D IS CLOSED (2026-07-17, `42292af79`..`ea165a935`, 602 → 611/0) — `String.append` grows to an EXTERNAL buffer + a String's OWN drop. P1.2 IS FULLY CLOSED.**
> `s.append(t)` on an owned root that outgrows its inline capacity **DETACHES**: `__str_append` `__mm_alloc`s a fresh external byte buffer (`destructor 0` — raw bytes), copies the live bytes in, frees the old buffer only if it was itself a root (`parent == DetachedRootParent = -1`), publishes `buffer`/`capacity`/`parent=-1`, then blits `other`'s bytes at the old length. Growth is EXACT (`requiredLen`). A String now carries its OWN drop: `emitDecref` is **tag-routed** — `ValueTypeTag.string` → `__str_decref` (the refcount RMW, then `__mm_free` of the external buffer when `parent == -1`), everything else → `__mm_decref`. `E3019` (existing code, shv2 claim added) rejects `.append` on a `let` binding (immutable). **`byteLength()` reads `length@8`.** ⚠ **DEVIATIONS, both ACCEPTED (thesis-aligned):** (1) **`__str_decref` is dispatched STATICALLY** by the tag-router, NOT via a dynamic `funcAddr`/`callIndirect` destructor stored in the record's MM header. The dynamic per-record `__destruct_String` cascade (`funcAddr`/`callIndirect`, deferred here per PLAN.md:291) is **forced only when a struct/array FIELD can hold a String** — a box's own `__mm_free` must then reach the field's destructor without the parser knowing the static tag — which is exactly **P1.4** (struct-with-String-field). Until then the tag is known statically at every drop site, so static dispatch is correct AND cheaper. (2) A **`var` String is EAGERLY PROMOTED** to an owned heap copy at binding (a `var` bound to a borrowed rdata literal is read-only `.rdata` — `append` would write through it), via `promoteToOwnedString`. The review deduped a real find: the refcount-RMW protocol was copied into both `__mm_decref` and `__str_decref` (`emitRefcountCheckToLastOwner`, the LOCK-prefix P1.5 note moved onto the shared helper). ⚠ **OPEN #43 (self-append `s.append(s)` UAF — the grow path frees the old buffer before the blit reads the aliased source; MASKED under bump alloc)** filed as its own follow-up.
>
> ### ⇒ **REMAINING P1.2 owned-value follow-ups, both bump-allocator-MASKED UAFs that a recycling `__mm_free` turns into wrong answers — each its own rung, best done adjacent to the free list:** **OPEN #40** (borrowed→owned reassignment in a nested block — the height-stack can't own SHALLOWER than the current block; needs a depth-model rework) · **OPEN #43** (`s.append(s)` self-append frees the old buffer before the blit).
>
> ### ✅ **`P1.3` SLICE 1 IS CLOSED (2026-07-17, `84db86992`..`e79e3d233`, 611 → 617/0) — SCALAR-payload unions/enums are HEAP-BOXED. P1.3 is SLICED: scalar first (this), managed next.**
> A payload-bearing union/enum is now a **heap box**: `8 + maxArity*8`, i64 tag@0, scalar payload slot `i`@`8+i*8`, unused slots zero-filled — structurally a struct-box (P1.1a) + a tag, so its drop reuses the **trivial destructor** (a scalar payload owns nothing). Construct (`emitEnumBox`), match (tag@0 + the P1.1b cmp/condBr ladder), scalar extract, discard `_`, multi-field scalar cases, payload-free-case-in-a-boxed-union. **No new IR op.** Plus **E3066** rejecting union `==`/`!=`/`<`/`>`/`<=`/`>=` (reference equality — they compared box ADDRESSES; the review caught `<` etc. silently miscompiling where the implementer had only guarded `==`/`!=`). The box is enrolled as an owned heap temp (`trackOwnedTemp`), so scope-drop / move-on-bind / `var`-reassign came free from the String machinery. A payload-FREE union stays the bare-i64-tag P1.1b form. Review also de-duped the `__mm_alloc` ABI (written **4×**) into `emitBoxAlloc`.
> ⚠ **THE PLANNED CORPUS UNLOCK WAS WRONG — Slice 1 unlocked only 1 positive + 3 error cases**, not the `union-cases.md`/`match-enum-or-pattern.md` the survey listed. The implementer PROBED and found those gated on **COMPANION features the corpus bundles with unions** — a synthesized `.unionCases` companion enum + `.name`/`.rawValue`/`.ordinal` accessors, `for-in`/`.allCases`, and **union-as-PARAMETER (E2015 → P1.4)** — NOT owned payloads. ⇒ **Owned-payload support in ISOLATION unlocks little corpus; the bulk needs a companion/accessor rung + P1.4.** ([[feedback-survey-verdicts-need-probes]]: an "unlockable" verdict from a READ is not one from a RUN.)
> ⚠ **A boxed-union RETURN type would have LEAKED (exit 101) — now REJECTED (E2015), the interim.** A boxed-union call-result is `named`-tagged (indistinguishable from a bare enum), so `valueIsOwned` misses it and the caller never drops the box. Rather than ship a reachable leak ("leaks are not ok"), a payload-bearing-union return type is refused at the return-type annotation, **symmetric with the union-PARAM E2015** already deferred. **⇒ P1.4 lifts param+return together with the full cross-call boxed-union ownership** (caller adopts the returned box via a `named`+`isBoxed` layout lookup) — **OPEN #44**.
> **⇒ NEXT for P1.3 = SLICE 2 (MANAGED payloads — String/struct):** construct = **move the String in**, match-bind = **move-out**, and a tag-conditional **STATIC-cascade** drop calling each managed field's own `__str_decref` (static because the union type is known at every drop site — the same ruling as Wave D). Targets: the harness's `compilerError(text String)`/`fail(reason String)` and OPEN.md's staged `rc-enum-struct-payload-freed`.
>
> ### ✅ **`P1.3` SLICE 2 IS CLOSED (2026-07-18, `0006306a2`..`31f773eaa`, 617 → 641/0) — MANAGED (String + struct) payloads: move-in / move-out / tag-conditional static-cascade drop. P1.3's OWNED-LOCAL payload story is DONE (cross-call stays P1.4).**
> A union case field may now be a **String or struct** (a heap pointer in the payload slot). **Construct = MOVE** (`moveInPayload`: no incref; source poisoned E3102; a borrowed literal promoted to owned first). **Match-bind = MOVE-OUT** (`moveOutManagedPayload`: binding owns, the box slot is NULLED; the scrutinee becomes `partiallyMoved` = E3102-on-read but STILL dropped). **Drop = tag-conditional STATIC cascade** (`__destruct_<U>`, synthesized in `MmRuntime.installUnionDestructors`: per live case drop each still-present managed field via its own destructor — String→`__str_decref`, struct→`__mm_decref`, null-guarded — then `__mm_free`). ONE classifier (`SignatureIndex.classifyUnionPayload`) drives construct/extract/drop/synthesis (moved-in set == dropped set). The box drops uniformly at scope exit via the null-guarded cascade (a design deviation from per-arm consume — identical observable semantics, simpler). Optimizer killed an O(unions×drops) destructor-install scan (a `Set`). **`rc-enum-struct-payload-freed` STAYS disabled** — it matches a BORROWED param (P1.4 borrow-consume), cleanly rejected (E2015); 14 authored owned-local specs (`union-managed-payload.md`) cover the mechanism instead.
> ### ⚠ **SLICE 2's review found TWO REACHABLE match-flow bugs, both PRE-EXISTING (Slice 1 / P1.2, shipped on main) — FIXED as a corrective rung (`fa685a7a1`), NOT deferred ("leaks are not ok", now codified):** (1) a **temporary union scrutinee** (`match U.case(x) {…}`) leaked its box (exit **101**) — the temp was never enrolled as a match-scoped owned binding; now enrolled + dropped on all four exits (return/break/continue/fall-through). (2) an owned **`gives` value** double-freed / UAF'd (**0xC0000005**) — it fed a non-owned result phi; now an owned give TRANSFERS to the phi (`transferGiveToPhi`/`promoteBorrowedGive`, phi marked owned via `trackOwnedTemp`). Independent review sabotage-proved every mechanism + probed 10 edge cases clean. **RESIDUALS → their own rungs:** give-type-consistency is unchecked (**OPEN #45** — a `String`+`int` give-mix segfaults, pre-existing + unreachable by well-typed code; `finalizeMatchMerge` checks register-class, not type identity); a bootstrap fixpoint non-convergence forced a documented 2-line `recordBranchEdge` bypass, byte-identical IR (**OPEN #46**).
> ### ⇒ **P1.3 is functionally COMPLETE for owned-local payload unions (scalar + managed). What remains is cross-call (union param + return = P1.4, OPEN #44) and the companion/accessor rung that unlocks the bulk of the payload-union corpus.**
>
> ### ✅ **`P1.4` STARTED — SLICED into P1.4a (cross-call OWNERSHIP) + P1.4b (ERRORS), which the survey confirmed separate CLEANLY (~0 entanglement). USER RULING 2026-07-18: cross-call params are BORROW-by-default, consume-by-use.**
> A param is a **BORROW** (caller keeps ownership, value usable after the call, callee never drops it); a **CONSUME** (move) only where the callee moves it into durable storage (a struct field); a return **ADOPTS** (owned move-out, caller drops once). Chosen because both reference compilers borrow-by-default, the harness overwhelmingly borrows (the same `test SpecTest` handed to four helpers in a row), and String already works this way; consume-on-pass would reject the pervasive `f(s); use(s)`.
> ### ✅ **`P1.4a` WAVE 1 IS CLOSED (2026-07-18, `354995f3f`..`f087e3152`, 641 → 647/0) — BORROW struct/union PARAMS + ADOPT struct/union RETURNS. OPEN #44 CLOSED.**
> Purely PARSE-TIME: `resolveNamedStruct` re-tags a `named` denoting a declared `type` → `structRef` at the param-base / return / call-result sites (the registry is complete in the real parse). ⭐ **Ownership unified onto the `valueOwnsHeap` provenance BIT** — `valueIsOwned(value,tag)` collapsed to `valueIsOwnedHeap(value)`, the `tagIsStructRef` proxy DELETED, because a borrowed struct param and a fresh owned struct box **both** carry the `structRef` tag; only the bit (set by `trackOwnedTemp` on every owned producer, absent on a borrow) distinguishes them. A borrow is never dropped by the callee; the caller drops its owned value once. `mintOwnedCallResult` now adopts a struct/boxed-union return (a boxed union via a `named`+`isBoxed` LAYOUT lookup, `valueIsBoxedUnion` — a bare enum owns no box); the Slice-1 `rejectBoxedUnionReturnType` is removed. Review sabotage-proved the bit BOTH directions (remove it from a producer → leak 101; add it to a param → double-free 101). **⭐ CLOSED A PRE-EXISTING LATENT UAF:** constructing a struct with a MANAGED field (`Self{name: name}` from a String param) stored a dangling borrow (bump-masked) — now a clean **E2015** (`rejectManagedStructFields`, whole-layout scan so every construction path is caught). **⇒ NEXT = P1.4a WAVE 2 (CONSUME-into-field):** struct-with-managed-field + its destructor cascade + the per-param ownership inference (`.create(inner Inner)` → `Self{inner: inner}`, moving a payload out of a borrowed union param). ⚠ **OPEN #47** filed: rejecting a String field DEFAULT leaks 4 compiler allocations (exit 101) on the error-unwind path — pre-existing, unrelated to borrow/adopt, its own follow-up.
>
> ### ✅ **`P1.4a` WAVE 2 IS CLOSED (2026-07-18, `d9fa9f62c`..`86e704a59`, 647 → 670/0) — CONSUME-into-field + a PATH-SENSITIVE move model. The pre-existing conditional-move leak is FIXED at the root.**
> **The consume half:** a struct may hold a MANAGED field (String/struct); a param moved into a field is CONSUMED (the caller's arg is moved-from, E3102). A struct destructor cascade `__destruct_<Struct>` (generalizing Slice 2's union cascade via one `managedFieldDropCallee` router — which also FIXED a latent leak the union cascade had: a nested managed struct was dropped via `__mm_decref` not its destructor) frees the managed fields, statically dispatched. The param-consume analysis rides the token SWEEP (`ProgramSignatures.funcParamConsumes`, keyed on the declaration hash for cross-file re-parse); scope = the DIRECT sink (`.create(f X)→Self{f:f}`), the transitive/recursive fixpoint cleanly REJECTED (E2015). A boxed-union struct FIELD stays E2015 (a later rung).
> **⭐ THE PATH-SENSITIVE MOVE MODEL (the review's blocker → USER RULING: "the real fix, no runtime flags").** Wave 2's review found a REACHABLE conditional-move leak (exit 101), **PRE-EXISTING in the core move model** — `VarInfo.movedFrom` was MONOTONIC, so a value moved on one `if`/`match` branch was drop-SKIPPED on EVERY path, leaking on the not-moved path (verified: it already leaked on main with union payloads + owned locals; Wave 2's struct-field consume was just one more trigger). **FIXED** via compile-time drop elaboration: `captureMoveMark`/`restoreMoveMark` snapshot the move bit around a branch, `reconcileMovesAtMerge` settles each owned binding at every JOIN — moved on some edges + live on others ⇒ **drop it on the LIVE edges** (NO runtime flags). Wired into `finalizeIfNoElse`/`finalizeIfElse`/`finalizeMatchMerge`. Conditional moves are now ACCEPTED and correct (dropped exactly once per path); reads past a maybe-moved join stay conservative E3102. Review sabotage-proved all three guards + probed every control-flow shape (triple-nested-if 8 combos, match arms, loops). **⚠ ONE CORRECTNESS BOUNDARY:** moving an OWNED binding declared OUTSIDE a loop from inside the loop body is REJECTED (E2015) — the back edge would re-move it into a double-free; accepting it needs a loop-exit reconcile (a follow-up). A loop-body-LOCAL move works.
> ⭐⭐ **This is the SECOND consecutive rung whose review found a PRE-EXISTING move-model leak** (Slice 2 was the first) — the move model's path-insensitivity kept surfacing as new constructs reached it; now closed at the root. Rebased over the parallel repo's **arm64-macOS backend + `/`/`mod` div-by-zero-throwing** landing (bootstrap now 133 codes). **⇒ REMAINING P1.4 = P1.4b (ERRORS).** Follow-ups: the loop-exit-reconcile (accept an outer-move that breaks a loop), **OPEN #48** (the `.spec-tmp` staging race).
>
> ### ✅ **`P1.4b` WAVE 1 IS CLOSED (2026-07-18, merged `9b9455698`, 670 → 719/0) — the SCALAR-error CORE. `throws`/`try`/`otherwise`/`throw`/`panic` end-to-end, on v1's dual-register `(value, errorFlag)` ABI VERBATIM.**
> A `throws` fn returns TWO registers: value in **R8**, an i64 error flag in **R10** (arm64 x0/x9) — `0`=no error, `ordinal+1`=a scalar enum case. New IR ops: `tryCall`/`throwError`/`propagateError` (Maxon), `tryCall`/`errorReturn` (Std, `role: OpRole.errorReturn` — the placeholder got its producer). The parser desugars `try` → `tryCall` + `compare` + `condBranch`; `(e)` binds `flag - 1` typed from the callee's static `throws` clause (so **NO `errorIsHeapPtr` runtime bit** — statically decidable). Error-edge drops ride P1.4a's path-sensitive reconciliation (trivial for scalars). **⭐ THE HIGHEST-RISK PIECE — the two-result capture** (`result←R8, flag←R10`): a naive `mov;mov` miscompiles when the allocator colors the pair cyclically (v1's array-sort bug), so it routes through `SsaDestruction`'s parallel-copy resolver (breaks a swap with `xchg`, no scratch — cleaner than v1). R10 chosen as a caller-saved non-arg register, mirroring R8's own "no return/arg conflict". Coordinator verified it under register pressure (both a success result and a thrown flag live across two `tryCall`s → correct). **Reject diagnostics: E3054/E3055/E3057/E3058/E3059** (shv2 claims added to the shared registry — numbers already existed; E2049 block-form-in-arm reused). **48 of 49 acceptance cases green** (the 49th, `try-pure-let-discard`/E3064, DEFERRED — shv2 has no purity classification; every E3064/E3065 discard case is disabled for the same reason). Ported `error-handling.md` (29 scalar) + `try-otherwise-value-flow.md` (5) byte-identical + flipped 13 staged markers.
> **Pre-existing bugs FIXED (blocked error enums):** enum `implements Error` was parsed as TWO enum cases (`implements`, `Error`) → `skipOptionalImplementsClause`; `as` casts were unimplemented (E2015/E2045) → inert numeric-cast pass-through added (range-check is OPEN #51).
> **⭐⭐ THE SIGNATURE BUG, FOUND BY REVIEW AGAIN:** the error-flag `+1` bias was written TWICE across a tier boundary (encode in `lowerThrowError`, decode in `parseOtherwiseBlock`) — a change to one would silently bind `match e` to the wrong case. DERIVED from ONE `ErrorFlagOrdinalBias` constant (byte-identical codegen). Optimizer confirmed nothing superlinear (linear +35…+159 allocs, a stale-baseline reading corrected).
> **⭐⭐ TWO reachable crashes on VALID constructs, found by adversarial PROBING (suite green over both):** (1) a **throwing fn returning a MANAGED type** (`returns String throws E`) accept-and-CRASHED (segfault) — its tryCall result is NULL on the error edge, genuinely Wave-2 — now a clean **E2015 reject** (`no accept-and-crash`, consistent with how the rung rejects every other unsupported error construct) rather than shipped; **OPEN #49** (the feature = Wave 2). (2) a throwing fn returning a **float** hits a PRE-EXISTING general float-return codegen gap (`StdToX64Conversion.maxon:1783` "float returns not supported until M3" — regular float-returns panic too) — filed **OPEN #50**, out of P1.4b's scope.
> ⚠ **Rebased over the parallel repo's float-`/`-language-level-throwing bootstrap landing** (`8c79208ec`, `stdlib/Builtins.maxon` now `try`s a division) — the STALE-BIN trap: rebuilt the bootstrap from the new main before shv2 would compile the stdlib. **⇒ REMAINING P1.4b = WAVE 2 (managed error VALUES):** `throw`ing a payload union (String/struct), the `(e)` binding OWNING + dropping the caught box, owned managed LOCALS dropped on the error edge — **the HARNESS slice** (`CompileError`/`SubprocessError` unions), and the removal of the OPEN #49 managed-return gate. [[project-p14b-errors-plan]]
>
> ### ✅ **`P1.4b` WAVE 2a IS CLOSED (2026-07-18, merged `8ca5225f2`, 719 → 726/0) — managed error VALUES (boxed-union throw/catch/drop). MOSTLY REUSE.**
> The flag register (R10) now carries the box **POINTER verbatim** for a boxed-union error (vs `ordinal+1` for a scalar enum) — distinguished STATICALLY via the derive-once helper `throwsTypeIsBoxedUnion` (Parser:4767, read at the throw-encode + both catch-decode sites; both it and `valueIsBoxedUnion` delegate to `EnumLayout.isBoxed()`, the ONE decider — cannot drift). The op carries a pre-classified **`errorIsBoxed bool`** (replaced a DEAD write-only `errorType` field on `throwError` — lowering lacks the enum registry, so classify once in the parser and ride the bit). `(e)` binds the box verbatim + is enrolled OWNED → drops via `__destruct_<U>` at handler `end`. **Almost entirely REUSE** — P1.3's box construct (`emitEnumBox`/`moveInPayload`) + `__destruct_<U>` cascade, Wave 1's ABI/capture, P1.4a's drops — plus 3 small branches. **NO new IR ops** (only the field change), NO MmRuntime edit.
> **⭐⭐ THE THROUGH-LINE, a THIRD consecutive time: independent review found a LEAK the implementer missed** — the `return` and `throw` hand-off exits agreed on the owned move-out (`moveOutForExit`) but **DIVERGED on the rest of the cleanup**: `throw` did NOT drain leftover borrowed temps the way `parseReturnStatement` does, so `throw E.problem(len("val={x}"))` leaked the interpolation temp (101) while the byte-identical `return` was clean. Fixed by factoring the WHOLE hand-off sequence (move-out + drain temps + scope drops + restore owned list) into ONE `emitHandoffExitDrops` both call — the signature-bug class (two exits agreeing on part, diverging on the rest) AGAIN. Optimizer confirmed nothing superlinear (true cost 0 — corpus has no error handling) and the change REDUCES duplication.
> **The implementer also self-caught a leak by probing** (the value/ignore/single-stmt `otherwise` forms weren't dropping the caught box → 101; fixed to enroll uniformly) and **gated a latent double-free** (`throw <borrowed union param>` → E2015, the throw twin of the borrowed-aggregate-return refusal) and **authored a String-payload coverage spec** (nested-managed-payload drop via `__destruct_<U>` — the portable corpus payloads are all SCALAR-in-box, so this is the only proof the payload itself drops). Every managed-error path probed leak-free (nested String drop, `throw e` re-throw transfer, all 5 `otherwise` forms, break/continue-in-loop). **OPEN #52** (cross-file union-payload interner PANIC, declared-after-use), **OPEN #53** (labeled union-construction args unparsed, blocks a byte-identical port). **⇒ REMAINING P1.4b = Wave 2b (managed RETURN — removes the OPEN #49 gate) + Wave 2c (union self-FIELDS + reload) + cross-file `throws`-prescan.** [[project-p14b-wave2a-closed]]
>
> ### ✅ **`P1.4b` WAVE 2b IS CLOSED (2026-07-18, merged `d16a45f0f`, 736/0 on the wasm-rebased main) — managed RETURN. The one UAF-risky piece of P1.4b; OPEN #49 gate REMOVED.**
> A `try f()` where `f` returns a MANAGED type: the tryCall `result` (R8) is owned on the ok edge, **NULL on the error edge** (a throw zeroes it); `mintOwnedCallResult` tracked it in the ENTRY block (flows to both), so an error-edge drain decref'd NULL → segfault (the exit-139 OPEN #49 gated). FIX = **re-scope the managed result to the `tryok` block ONLY** — `removeFromPendingTemps(result)` in `desugarTry` before the handler parses, then re-enrol owned in `tryok` per the three finish paths (`finishTerminatedTry` re-push; `finishValueTry` owned phi + promote a borrowed-literal fallback; `finishFellThroughTry` decref in okBlock). Deleted the E2015 gate + `returnTypeOwnsHeapBox`/`payloadClassOwnsHeapBox` + the reject spec. Strengthened `parseUnboundOtherwiseForm`: a managed result with a **tag-mismatched or borrowed** `otherwise` fallback is refused E2015 (else a wild free at the phi's single drop). NO new IR ops. I verified the segfault surface myself (throw-taken + managed result discarded → exit 7, no crash).
> **⭐⭐ THE SIGNATURE BUG, review found it a 4TH consecutive P1.4 time:** a **fell-through** `otherwise` block in a VALUE position leaked (managed, exit 101) — the "a valueless handler can't be assigned" rule was enforced at ONE of two spellings (`otherwise ignore` guarded, block-fall-through NOT). Consolidated into ONE E3059 check at `finishFellThroughTry` (the point both collapse to `fellThrough`), removing the duplication.
> **⭐⭐ A SYSTEMIC PRE-EXISTING type-soundness hole, CONFIRMED (review + me): shv2 does NOT name-check aggregate type IDENTITY.** Every struct shares the `structRef` tag / every union `named`, and the checks are tag-level only — so `return BoxB` from a `returns BoxA` fn (or a mismatched `otherwise` fallback) COMPILES and drops with the WRONG destructor = a wild free / **segfault (exit 139), reachable with NO error handling at all** (since P1.1 structs). NOT a Wave-2b regression — filed **OPEN #54** as a dedicated corrective rung. **USER (2026-07-18): v1 fixed it — PORT v1's type-identity check, don't re-derive.** ✅ **OPEN #54 Slice A LANDED 2026-07-18** (`16c729b07`..`406987b5d`, 736→741): ported v1's exact-interned-name check into ONE shared `aggregateNameOf`/`namedAggregatesConflict` pair, applied additively at return / reassign / `otherwise`-value; both confirmed 139s now compile-reject (E3005/E3059). Review: **the signature bug is ABSENT** (first clean P1.4-era review). ✅ **Slice B LANDED 2026-07-18** (`aa2ac2509`..`2a50ea55c`, 741→743): published `valueNameIds` on `IrFunction` + a `checkArgTypes` name branch → the struct consuming-call-arg wild-free (139) now rejects E3005 for ALL call shapes; `namedAggregatesConflict` moved to a shared `TypeRules.maxon` home; 2nd consecutive clean review. **Slice B2** (UNION call-arg — param name erased before SemanticCheck, needs a pre-erasure carrier, #52-adjacent) + **Slice C** (value-position `match … gives` merge = OPEN #45) remain.
> ⚠ **Rebased over the parallel repo's `wasm32-wasi` SCALAR backend landing** (+ a later MCP `--target` + wasm-docs landing) — resolved the `optimization-log.md` two-table conflict by keeping their re-baselined version (my pre-wasm rows were stale) + re-recorded a post-wasm Wave 2b scale row. **FIXED a pre-existing Windows-UAC bug their spec runner introduced** (`SpecTestRunner` named the temp exe after `test.name`, so a test `...its-update` tripped Windows installer-detection → "requires elevation" spawn fail; now a keyword-free `hostprog` stem). **⇒ REMAINING P1.4b = Wave 2c (union self-FIELDS + reload); + OPEN #54 Slice B2 (UNION call-arg).** ✅ **The cross-file `throws`-prescan (OPEN #52 backward-ordering slice) is VERIFIED CLOSED 2026-07-19** — the "declaration-order union-layout prescan" already exists (`queryProgramSignatures`→`foldFile` seeds every file's union layouts + `throws` clauses before any body parses), and every `classifyUnionPayload` site adopts the payload id into the interner it resolves against, so the `:582` panic is structurally unreachable; a spec-only rung (767→771) enabled `error.cross-file-throws-caught-later-file` + pinned the previously-uncovered backward managed-payload shapes. Slice C = OPEN #45 (closed). [[project-p14b-wave2b-closed]]
>
> ### ✅ **`P1.4b` WAVE 2c IS CLOSED (2026-07-20, merged `807180533`, 782 → 788/0) — boxed-union struct FIELDS. The LAST P1.4b mechanism; only OPEN #54 Slice B2 now remains for all of P1.4.**
> A `type` field may be a payload-bearing (boxed) union (`var pending as OuterErr`). Reclassified `PayloadClass.nestedUnion` unmanaged→**MANAGED but kept DISTINCT from `managed`**, carrying the field's own `named(<union>)` type — so the existing String/struct-field machinery (construct-MOVE, reassign-drops-old, `__destruct_<T>` cascade via `structFieldIsManaged`) handles it, while the union-PAYLOAD path (`requirePayloadLowerable`, store width from `payloadStorageOf` which can't carry `named`) still rejects a nested payload cleanly. The struct-FIELD path derives its width from `fieldStorageType` instead, so it lowers fine. `throw <boxed-union field>` is a MOVE (shv2 has no `__mm_incref`): null the field slot (recorded by `emitFieldLoad` in 4 scalar `lastFieldRead*` fields, matched by SSA value identity) so the container's cascade skips it. Deleted the blanket `rejectUnsupportedManagedStructField`. Enabled construct/reassign/scope-drop/owned-local-throw + a nested-String-payload cascade; the union-payload reject stays E2015 (P1.7).
> **⭐⭐ THE INDEPENDENT REVIEW FOUND A REACHABLE SILENT SEGFAULT (139) the x64 suite was green over** — `throw self.field` of a boxed-union field through a BORROWED receiver, caller CATCHES then re-reads the moved-out field: `moveOutThrownField` nulls the field through the borrow (the CALLER's box), sound if the caller tears the container down (propagation) but a use-after-move on re-read, and undetectable without cross-call move tracking. It was INCONSISTENT with the twin `moveOutManagedPayload`, which already REFUSES a borrowed scrutinee. **FIX (the disciplined clean-reject, matching P1.3 Slice 1's boxed-union-return reject): gate the field move-out on an OWNED-LOCAL container** (`valueIsOwnedHeap(lastFieldReadBox)` — an owned local dies with its frame, so no surviving caller can read the nulled slot). A borrowed `self`-field falls to a clean E2015, split from the borrowed-param reject so the param keeps its exact message (zero golden churn). The rung's real deliverable (owned-local construct/read/reassign/drop/throw-out) is untouched. Verified the exact repro now rejects (exit 1, was 139); owned-local still exit 20; suite 788/0 workers-invariant; scale delta 0; arm64 owned-local cross-compiles clean.
> **⏭ TWO P1.5 follow-ups FILED (OPEN #64, #65):** re-enable the BORROWED-container field throw once escape analysis can tell a re-reading caller from a tearing-down one; and implicit-self method resolution (bare `bump()` → E3004). The disabled `propagate-throw-through-local-struct` needs BOTH and stays disabled. [[project-p14b-wave2c-closed]]
>
> ### ✅✅ **`P1.4` IS COMPLETE (2026-07-20, `8b2bec781`, 792/0) — the whole moves+borrows+errors rung is DONE.** OPEN #54 Slice B2 (UNION call-arg type-identity) was the last piece.
> A `union`/`enum` PARAMETER is erased to bare `integer` by `resolveTypes` (`TypeResolution.resolveNamedType:192`, the OPPOSITE of a struct's surviving `structRef(id)`), so `SemanticCheck.checkArgTypes` could not name-check a mismatched union arg — dropped under the DECLARED param's `__destruct` = wild free (139 consumed / latent borrowed); the oracle rejects E3005. FIX = a pre-erasure per-param NAME carrier (`FuncSignature.paramAggregateNames`, filled at parse where the param is still `named`, threaded through `syncResolvedSignature`, read in `checkArgTypes` via the generalized `aggregateNameFor` + shared `namedAggregatesConflict` → E3005). NAME-based (ByteArray, no ids) ⇒ structurally immune to the #52 cross-file interner landmine — the independent review probed that 5 ways (id-shift, declared-after-use) all oracle-matched, found NO over-rejection, and judged the `aggregateNameFor`/`aggregateNameOf` twins a FORCED tier-boundary copy (comparison single-homed). Scope cut: `maxonOpIdxToString` (#51) is NOT in shv2 (a v1 printer). **⇒ NEXT MAJOR = P1.5 (closures + async + escape analysis)** — co-lands async with closures (see the async note); also re-enables OPEN #64 (borrowed-container field throw) + #65 (implicit-self). [[project-open54-slice-b2-closed]]
>
> ### ✅ **`P1.5` STARTED — sliced 5 ways** (A1 fn-values → A2 closures+escape → B1 GT-core → B2 await/Promise → C subprocess+harness). USER RULING 2026-07-20: start A1, take P1.5 through the parallel-harness gate; scheduler/stack-model rulings deferred to B1/B2. **⭐ P1.5 is the biggest rung: closures + async + escape analysis + the GT scheduler runtime (R3); shv2 had ZERO async/closure/GT code (only lexed `async`/`await`).** [[project_p15_decomposition]]
> ### ✅ **`P1.5-A2` SUB-SLICED → A2a then A2b** (the closure corpus has sub-deps: some cases need Array/P1.7, interfaces/P1.7a).
> ### ✅ **`P1.5-A2a` IS CLOSED (2026-07-20, merged, 813 → 821/0 x64 + 800 → 808/0 wasm) — NON-CAPTURING closure literals. FRONT-END ONLY, no new IR op.**
> `function(params) gives <expr>` parses as a primary; a closure that captures NOTHING lifts to a top-level `<enclosing>$closure_<k>` (per-outer-fn counter, source-deterministic) and yields a plain `functionRef` — reusing A1's whole machinery. Body parses into a swapped-in fn context (own scope/value-numbering/blocks/columns/drop-sets), restored after; return type = body type, filed in a file-local `closureReturns` map (a lifted closure never reaches the whole-program token sweep). **Capture DETECTION = the A2b seam**: an identifier resolving into `closureCaptureScopes` (a STACK of enclosing scopes, so a closure-in-closure detects any enclosing frame) → clean E2015 (A2b replaces the refusal with real env handling). ValueOrigin fix: functions pushed up-front + `currentFuncIndex` records each landing index (codegen-neutral for closure-free programs). ⭐ **Review found the recurring signature bug**: only 1 of 4 `lastFieldRead*` fields was saved/restored across the closure-body context swap (one logical record) → a reachable miscompile; fixed to save/restore all four. Enabled 6 `first-class-functions.md` + `closure-capture.md`'s no-capture-regression/string-literal-body (targets x64-windows+wasm32-wasi). ⏭ **`non-capturing-closure-through-ternary` STAYS disabled — OPEN #71: shv2 doesn't parse the inline conditional `a if c else b` (E2010); a prerequisite for ALL A2b ternary cases, its own front-end rung.** Plus OPEN #72 (bootstrap aliased-self-field-store double-free). **⇒ NEXT = A2b (capturing closures + env block + parse-time E3099 escape rejection).**
> ### ✅ **`P1.5-A2b` SUB-SLICED → A2b-1 (done) + A2b-2 (next).** Two implementer stop-and-report forks: (1) capture must be **BY-VALUE** — shv2 has no addressable stack slots (`stackAddr`/mem2reg absent), so v1/bootstrap's by-reference env is impossible; the env stores the captured VALUE (one load), correct for the corpus (all capture immutable `let`s), a documented divergence from the spec doc's "by reference"; (2) pass-down forces the uniform `(args,env)` ABI which churns A1's goldens → split off as A2b-2.
> ### ✅ **`P1.5-A2b-1` IS CLOSED (2026-07-20, merged, 821→833/0 x64 + 808→821/0 wasm) — CAPTURING closures (int, by-value) + env block + parse-time E3099 escape rejection. IN-FRAME calls; ZERO A1 golden churn.**
> Closure value = (`functionRef` fn ptr) + (env ptr on the per-fn `valueClosureEnv`/`valueIsCapturingClosure` columns, swapped like `valueTags` — **replaces v1's `fnEnvVarNames` sidetable**). **NO new IR op**: env = `emitBoxAlloc(n*8)` (trivial dtor, owns nothing) + `storeIndirect` by value; read = one `loadIndirect`; the call appends env via existing `indirectCall`. The lifted `<enclosing>$closure_<k>` gets a trailing `__env` param **only when it captures** (non-capturing stays A2a-identical → zero churn — the call site reads `valueClosureEnv` to pick env-carrying vs A2a's env-less callIndirect). Env dropped once at creation-scope exit via `ownedBindings.push` (scope-scoped, NOT `trackOwnedTemp` which frees before first call). E3099 parse-time, value-keyed on the stable `boundValue` (no name-bridge).
> ### ⭐⭐ **THE INDEPENDENT REVIEW FOUND FOUR REACHABLE MEMORY-SAFETY HOLES (all fixed `96daddca6`, each spec-pinned):** a capturing closure reaching a sink that doesn't carry its env — **struct construction** (panic), **call-arg pass-down** (SEGFAULT 139, env not threaded → E2015 until A2b-2), **match `gives` arm** (SEGFAULT → E3099), **`try…otherwise <closure>`** (SEGFAULT 139 → E3099). F3/F4 = reachable twins of the disabled ternary tests. Env-drop/UAF verified clean x64+wasm; over-rejection none; E3099 sabotage-proved load-bearing. **⭐ Design note → OPEN #73: the escape check is PER-SITE (enumerated) — a DERIVED invariant ("a capturing-closure value may appear only as an indirect-call callee or a frame-local binding") would be structurally robust (its own hardening rung).** Deferrals → OPEN #74 (payload-binding E2013 divergence), #75 (name-not-leaked needs struct-copy-return), #76 (bound-outside-loop x64 E5001 pressure, wasm-only). **⇒ NEXT = A2b-2 (uniform (args,env) ABI + pass-down + managed capture; the A1 golden churn lands here).**
> ### ✅ **`P1.5-A2b-2` IS CLOSED (2026-07-20, merged, 833→842/0 x64 + 821→829/0 wasm) — the uniform `(args, env)` ABI + closure PASS-DOWN + managed (struct/String) capture + the interprocedural closure-escape summary. ⇒ P1.5-A2 (closures + escape) IS DONE.**
> A function value is now **(fn ptr, env ptr)** uniformly, so a capturing closure passes DOWN to a generic `apply(f,x)`. **Plain function as a value** lowers through a `__fnref_<name>` THUNK (`Runtime/FnRefThunk.maxon`) with `(args, __env)` forwarding to the target, env=null — so wasm's functype-checked `call_indirect` matches and DIRECT calls stay unchurned. A function-typed PARAM carries a companion env **appended** after the user params (no source-slot remap). Env threaded once per call-shape (`finalizeCallArgs`/`appendCalleeEnvArgs` at lowering for direct/try; `emitClosureEnvArg` for indirect). **Managed capture = a BORROWED pointer by value** (env owns nothing, valid because the escape rule keeps the frame alive). **A1 golden CHURN (endorsed, contained): 24 function-value fragments moved** (`[name]→[__fnref_name]` + null-env companion arg), no other spec.
> ### ⭐⭐ **THE INDEPENDENT REVIEW FOUND A REACHABLE SEGFAULT (139) — removing A2b-1's blanket arg-guard reopened the INTERPROCEDURAL store/return escape** (a capturing closure passed to a callee that persists it → the companion env, valid only for the call, is lost → null-env nil-deref). **Fixed (`f6b8ec98e`) with the per-parameter ESCAPE SUMMARY** = OPEN #13's P1.5 promise: `SemanticCheck.buildEscapeSummary`, a function-typed param ESCAPES iff used anywhere except as an `indirectCall` callee (a WHITELIST — sound-by-construction, the #73 DERIVE cure); a capturing closure at an escaping param → E3099, a plain function passes. The implementer found a 3rd route (loop-phi/block-arg) by probing; the coordinator adversarially probed 7 routes (all E3099) + the accepts (green). ⚠ shv2 is now STRICTER than the bootstrap here (the bootstrap deferred #13). Diagnostic-only (0 codegen change), scale flat +11. **Deferrals/finds → OPEN #77** (string-backed error-enum raw values block try-call), **#78** (wasm non-i64-width function-value `call_indirect` trap), **#79** (closure-with-fn-param). **⇒ NEXT = the async half of P1.5: B1 (GT scheduler core), with the scheduler/stack-model rulings due to the user; and the small #71 (inline conditional) unlocks the deferred ternary closure cases.** [[project_p15_decomposition]]
> ### ✅ **`P1.5-B1a` IS CLOSED (2026-07-21, merged, → 930/0 host + 902/0 wasm) — the SINGLE-M COOPERATIVE GREEN-THREAD SCHEDULER CORE. `async f(args)` spawns a green thread and yields a Promise handle (an i64 GT pointer); `await p` drives a cooperative scheduler to completion and collects the scalar result. x64-windows.**
> **USER RULINGS (2026-07-21):** (1) **fixed-stack-FIRST → morestack B1a′** — B1a uses a FIXED 1 MiB `osAllocPages` stack (arm64's model, which runs the whole corpus incl. `stack-growth` by demand-paging), isolating relocating morestack (the highest-risk piece — a saved-rbp-chain-fixup bug is a silent stack-corruption UAF) into its own later slice B1a′; frame pointers were already paid for. (2) **defer E3073** (the "async never yields" gate) + **author BESPOKE scalar scheduler specs** — the corpus satisfies E3073 with `File.exists`, which shv2 has no file I/O for. **THE SPLIT:** HAND-ASSEMBLED raw bytes = `__gt_context_switch` + `__gt_trampoline` ONLY (`Targets/X64/X64GtRuntime.maxon` — rsp/rbp swap + ret-into-a-fresh-frame, unrepresentable in Std; neither touches `.data` — the `to` GT rides through in rdx); BUILDER-BUILT `StdModule` = `__gt_init/spawn/enqueue/dequeue/await` + a `.data` current-GT global + a global FIFO run queue (`Runtime/GtRuntime.maxon`, mirroring MmRuntime). Single-M (NO work-stealing/atomics/TLS — B1b), scalar (managed/float async arg or result → clean E2015 reject via the shared `tagIsAsyncScalar`, never leak). GT struct offsets byte-compatible with the authoritative GtLayout so B1a′/B1b graft cleanly. **The single-M crux:** `gt.waiter` is repurposed as "the GT to return to on completion," set by the await drive right before each switch-in; `await` is the sole scheduler driver (drains ready GTs until the awaited promise completes) — handles out-of-order, nested, and re-await. 12→13 bespoke `async-scheduler.md` cases (basic/parallel/sequence/spawn-arg/multiple-args/nested/spawn-immediately-awaited/spawn-not-awaited + 4 rejects + the leak guard). **⭐ THE INDEPENDENT REVIEW CONFIRMED the raw ABI byte-correct by DISASSEMBLY** (callee-saved set = `calleeSavedOrder` not rsi/rdi/rax which are shv2 arg-registers; trampoline parks gt in r12 before the arg loads; `call funcPtr` 16-aligned) and **fixed duplication itself** (`b8a595169`): the callee-saved set was written twice → now derives from `calleeSavedOrder`; the frame-size fact was comment-bound → now a compile-time `assertGtFrameConsistent` (sabotage-verified RED). **⭐ THE ONE MERGE BLOCKER — an unbounded GT stack leak** (every `__gt_spawn` committed a fresh 1 MiB `osAllocPages` stack with no free path; 15000 spawns → 14.27 GB, exhausts commit → crash; invisible to the `__mm` gate) — **fixed (`d29dee4a8`) with a STACK FREE-LIST**: `__gt_await` recycles a completed GT's 1 MiB stack from the WAITER side (after the switch off it lands back on `selfGt`), `__gt_spawn` pops-or-commits, re-laying the trampoline frame; the GT STRUCT stays RESIDENT so re-await still reads `p.result`. **15000 spawns 14.27 GB → 0.4 MB; 100k-loop 35 ms exit 0.** Residuals FILED not silent (review-sanctioned) → **OPEN #87** (the ~80 B struct bump-leaks per spawn — reclaiming it needs promise-liveness → B2 / a recycling allocator → B1c), **#88** (a NEVER-awaited GT never completes → its stack is never recycled; `while true { async f() }` still leaks → B2 linearity/exit-drain). Optimizer: **no superlinear defect** — B1a's own cost is +6,160 allocs/rung5, strictly LINEAR (the one new dense `valuePromiseMarks` per-value column). Rebased onto the parallel repo's x64-linux + P1.9 landings (3 conflicts resolved as COMBINATIONS: OS-gated panic-runtime + GT-chunks; `encodeCallReg` + `x64Syscall`; both log rows); OPEN issue-number COLLISION with the parallel repo's #82/#83 fixed by renumbering to #87/#88. **⇒ NEXT = B2 (await/Promise with the ERROR TYPE + timer/sleep + escape→`shared` + linear-await E3100), then C (async subprocess stdio + the parallel harness = the Phase-1 gate).** [[project_p15_b1_gt_scheduler]]
> ### ✅ **`P1.5-B2a` IS CLOSED (2026-07-21, merged `35349acfc`, → 946/0 host + 908/0 wasm) — LINEAR-AWAIT (E3100). `await` is LINEAR: a second await REACHABLE from a first, of the same promise, is a compile error. Closes OPEN #87's stated blocker (re-await is now unrepresentable). COMPILE-TIME only, single-M irrelevant.**
> ⭐ **THE SHV2-NATIVE DIVERGENCE (the rewrite's thesis in action): NO GreenThreadId column.** The bootstrap needs a GreenThreadId because ITS parser mints a fresh SSA value on every cross-block read (a re-tag op) and copies the promise on `let q = p`; shv2 does NEITHER — `parseVariableReference` returns `binding.boundValue` VERBATIM (SSA dominance, no re-tag) and `let q = p` binds q to p's ValueId. **So the promise's SSA ValueId ALREADY IS the stable green-thread identity** — aliasing shares it, cross-block reads share it, a re-arm (`p = async g()`) mints a fresh `asyncCall` ValueId. A GreenThreadId column would be a 1:1 relabelling = one fact written twice. NOT the E3102 move model either (both names stay live+awaitable, so `let q = p; await p` is ACCEPTED). The pass = a per-function CFG worklist reachability (`checkLinearAwait`/`reachableReawait`, `SemanticCheck.maxon`, gated behind the non-allocating `functionHasAwait`), keyed on the await's promise ValueId; **a path DIES at the `asyncCall` that RE-DEFINES the tracked ValueId** (the re-arm — since shv2 emits no assign op, re-passing the definition IS the kill), which separates `double-await-in-loop` (defn OUTSIDE the loop → refused) from a fresh-`let`-per-iteration loop (re-armed → accepted). Ported the ALGORITHM from the bootstrap's `CheckLinearAwait`/`FindReachableAwaitOf`; the identity mechanism is shv2's. 11 bespoke scalar `async-linearity.md` twins (6 double-await rejects → E3100, target-independent so they pass on x64 AND wasm; 5 linear accepts incl. exclusive-branches / rearm-after-await / ternary-arms, x64-gated). Optimizer: **own-cost EXACTLY ZERO** (A/B on/off bit-identical — the corpus emits no async so `functionHasAwait` skips the whole pass). **⭐ The independent review DIED mid-report** (hung compiling its probes — the same background-command hang the optimizer hit earlier); **the coordinator COMPLETED the verification independently** by running the dead reviewer's own adversarial probes + a batch: reachable double-awaits across conditional / nested-block / loop-back-edge / early-return CFGs → E3100; legal await-loops (fresh-per-iter), out-of-order, nested → accepted; the `await-aliased-loop-element` twin FAITHFUL (the corpus drains a `Promise`-Array needing P1.6 generics, so the twin re-spawns per iteration, testing the SAME alias+re-arm property). **NO over/under-rejection found.** OPEN #87 assessed NOT closed (the struct recycle is a delicate multi-part scheduler change — separate arg-buffer co-recycle, nested-await LIFO, GT0 safety — deferred to B1c per "only if clean"); **#88 stays deferred** (never-awaited is LEGAL — `spawn-not-awaited` pins it — so its fix is a drain-at-exit, NOT a reject; E3100 does not close it). New limitation FILED → **OPEN #89** (await of a LOOP-CARRIED/block-arg promise → E2015: the promise mark and the linearity both key on ONE `asyncCall` ValueId, and a reassigned-in-loop promise is a block-arg; pre-existing, SOUND, hits NO corpus case, → B2c). **⇒ NEXT = B2b (error-carrying `try await` — reuses the `throws` dual-value ABI, grows the GT struct + trampoline on x64+arm64), then B2c (Promise STORAGE — generics-coupled, P1.6), timer/`sleep` (own MID-BODY-YIELD rung), C (subprocess stdio + parallel harness = Phase-1 gate); B1b (multi-M) + B1a′ (morestack) + B1c (sharded alloc + managed async) remain.** [[project_p15_b1_gt_scheduler]]
> ### ✅ **`P1.5-B2b` IS CLOSED (2026-07-21, merged, → 957/0 host + 911/0 wasm) — ERROR-CARRYING `try await` (E3057/E3059). A throwing `async` fn is awaited with `try await p otherwise <h>`; the error rides shv2's EXISTING dual-register throws ABI (result R8 / flag R10). x64-windows.**
> ⭐ **Reuses the `throws` ABI end to end** — no new error machinery. `tryCall(result, errorFlag, __gt_try_await)` at the site; `__gt_try_await` is a THROWING-signature driver ending `errorReturn(gt.result, gt.threw)`. **The runtime change is MINIMAL:** the GT struct grows ONE field `threw@0x58` (0x50→0x60, byte-compat with the authoritative GtLayout); the hand-assembled trampoline gets ONE extra store `R10 → gt.threw` after the R8→result store (R10 was the arg-buffer scratch, provably dead after `call funcPtr`; captured unconditionally — garbage for a non-throwing thunk, but `gt.threw` is read ONLY by `__gt_try_await`, never by `__gt_await`). `__gt_await`/`__gt_try_await` refolded into ONE `buildGtAwaitDriver(name, throwing)` sharing the whole scheduler drive, differing only at the exit (`ret` vs `errorReturn`). Front-end: `parseTryCallTarget` accepts `await`; the promise carries the callee's ErrorType; **VOID async now awaits** (the handle is tagged `integer`, await-result derived from the callee — a `void` tag can't bind). The linearity pass EXTENDED to count `tryAwait` sites (double `try await` → E3100, locked in by a coordinator-added twin the review flagged as an untested gap). **⭐⭐ TWO LATENT SILENT-WILD-FREE HOLES FIXED (both caught by the corpus):** (1) plain `await` of a THROWING async COMPILED and dropped the error (the trampoline ignored R10) → now **E3057**; (2) ordinary `try f()` NEVER checked propagate error-type compatibility (`try throwsA()` in a `throws BError` fn compiled) → now **E3059** in the SHARED propagate path (fixes `try call()` AND `try await`). shv2's `throws` clause is a SINGLE type name (can't express `throws A|B`), so exact-name-match is correct + oracle-parity — union-membership over-rejection is structurally impossible. Optimizer: **own-cost ZERO** (corpus emits no `try`/`throws`/`async` — the whole front end is corpus-blind; baseline un-stale this time). Review: **MERGEABLE** — soundness verified with boxed/associated-value errors (error VALUE survives R8/R10, success sets R10=0 so no garbage-decref, boxed errors freed exactly once), trampoline R10 disassembly-verified in-bounds, duplication clean; fixed a whitespace mis-indent. 10 bespoke `async-try-await.md` twins (8 `try-await.*` + E3057 + E3059) + the double-try-await E3100 twin. Rebased onto the parallel repo's P1.9 follow-ups (clean). ⏭ Minor (reported, not filed): `asyncCallCalleeOf` runs 2× per await site (0 corpus cost); bare plain-`await` STATEMENT form unsupported (pre-existing). **⇒ NEXT = B2c (Promise STORAGE — generics-coupled, P1.6; also closes #89's block-arg await), timer/`sleep` (own MID-BODY-YIELD rung), C (subprocess stdio + parallel harness = Phase-1 gate); B1b (multi-M) + B1a′ (morestack) + B1c (sharded alloc + managed async) remain.** [[project_p15_b1_gt_scheduler]]
> ### ✅ **`P1.5 MID-BODY YIELD + NETPOLLER (sleep)` IS CLOSED (2026-07-21, merged, → 963/0 host + 911/0 wasm; rebased onto the parallel P1.6-A, 972/0 on the merged tree) — THE R3 SUBSTRATE. A GT can now SUSPEND MID-BODY: `sleep(ms)` parks the GT on a timer and yields; the scheduler NETPOLLS the earliest deadline when the run queue empties, re-enqueuing a parked GT when its deadline fires. This is the suspend/resume substrate the async subprocess harness rides. x64-windows.**
> ⭐ **THE YIELD REUSES `__gt_context_switch` — no new hand-assembled primitive** (`X64GtRuntime` bytes untouched). `__gt_sleep` parks (`status=waiting`, `io_yielded@0xA8=1`) and `context_switch(self, self.waiter)`; the driver KEEPS a `waiting` GT's stack (vs recycling a `completed` one) and switches back INTO it when its timer fires. **The `__gt_await` rewrite (the crux):** the drive loop is factored into a shared **`__gt_drive_until(targetGt, targetStatus)`** — `__gt_await`/`__gt_try_await` are now thin wrappers (B2a linearity + B2b error-carrying preserved, review-probed). **Completed-vs-yielded recycle gate** (the #1 stack-UAF risk): recycle only `status==completed`. **Netpoll:** on a dequeue miss, `timer_check(now)` wakes due GTs, else `Sleep(⌈(earliest−now)/1e6⌉ ms)` (real wait, never busy-spin). **GT0 fork:** a top-level `sleep` (`waiter==0`) has no driver, so it SELF-DRIVES `__gt_drive_until(self, ready)`; `timer_check` sets the current self-driver's status=ready WITHOUT enqueueing (else double-schedule). Deadline-ordered netpoll ⇒ deterministic interleave. **Timer store:** an UNSORTED O(n) fixed array (cap 256, 16-byte entries) — a deliberate divergence from the reference min-heap (a hand-Std sift-down is the subtle-corruption class this substrate must avoid; n=concurrent sleepers ≤2; layout min-heap-compatible for a later retrofit). **Clock:** `__gt_now_ns` = QPC via the OVERFLOW-SAFE SPLIT identity `(ticks/freq)*1e9 + ((ticks mod freq)*1e9)/freq` — NO 128-bit MUL/DIV, NO new Target op (kept in the Std tier, monotonic + overflow-safe for centuries). 2 new system-band StdOps `osReadClock`/`osSleepMs`; **3 always-present IatSlot imports** QPC/QPF/Sleep (in `PeWriter`, NOT `OsImportSlot` — that's the macOS enum, the implementer's correction of the brief). GT struct grew 0x60→0xB0 (`io_yielded@0xA8` + reserved 0x60–0xA0 for the future subprocess/IOCP fields, byte-compat with GtLayout). ⭐ **THE IMPLEMENTER STOP-AND-REPORTED TWO FORKS** (top-level-sleep-has-no-driver; the OS-import scope) — both resolved from the oracle, coordinator-approved; it also corrected two errors in the brief (OsImportSlot; the clock math). Optimizer: **no superlinear defect** — flat **+16 allocs/compile** (the 3 unconditional imports; the whole yield/timer path is `usesGt`-gated + corpus-blind). Review: **NO BLOCKERS** — 15 adversarial probes (stack-UAF-aliasing correct; the `waiting ⟺ live-timer-entry` invariant makes false deadlock-bails impossible; no double-schedule; B2a/B2b intact). 6 bespoke `async-sleep.md` twins. ⏭ Left (cosmetic, reviewer-judged non-defects): repeated timer-entry-address arithmetic (shared named constants, no silent-divergence path); the `TimerNoDeadline=0` domain sentinel. **⇒ NEXT (per the path-to-harness survey) = async SUBPROCESS spawn + yielding await (`cmd /c exit 3`→await→3, the harness SEED, rides THIS substrate), then subprocess stdio; the harness (C) also needs the ladder P1.6/P1.7/P1.8 the parallel repo is landing (P1.6-A generics now on main). B2c/B1c gated on P1.7 Array; B1b (multi-M) + B1a′ (morestack) OFF the critical path.** [[project_p15_b1_gt_scheduler]]
> ### ✅ **`P1.5 ASYNC SUBPROCESS SPAWN + YIELDING AWAIT` IS CLOSED (2026-07-21, merged, 976 → 984/0 host) — THE HARNESS SEED. A green thread spawns a Windows child (`cmd /c exit N`), does a YIELDING wait (parks the GT; the single-M scheduler netpolls parked children with `WaitForMultipleObjects`, resuming the GT on child exit), and returns the scalar exit code. Rides the mid-body-yield + timer-netpoll substrate. x64-windows.**
> New builtin `runProcess(cmd String) -> int` (name-recognized like `sleep`, borrows the String, returns int → usable in value position). 5 new system-band StdOps (`osProcessSpawn`=CreateProcessA / `osProcessExitCode`=GetExitCodeProcess / `osWaitHandle`=WaitForSingleObject / `osWaitHandles`=WaitForMultipleObjects / `osCloseHandle`) + 5 kernel32 IatSlots (append-only; Kernel32DirEntries stays 1). Runtime (`GtRuntime.maxon`): `GtOffIoHandle=0x80` (the reserved slot filled), a PROCESS STORE parallel to the timer store (two 64-slot parallel arrays `procHandles`/`procGts` fed straight to `WaitForMultipleObjects` — NO gather copy), `__gt_process_run` mirroring `__gt_sleep`'s park (slab scratch STARTUPINFOA+PROCESS_INFORMATION+mutable-cmdline-copy → CreateProcessA → park → reap via GetExitCodeProcess from the durable `io_handle`), and a netpoll extension in `__gt_drive_until` (pc==0 preserves the pre-subprocess timer path byte-for-byte; pc≥1 blocks on `WaitForMultipleObjects(handles, earliest-timer-delta or INFINITE)`, then re-checks via loopHdr — NOT netpoll — with a fresh-clock timer re-check).
> ⭐ **THE DESIGN DIVERGES FROM BOTH REFERENCES, and the divergence is the thesis:** v1 and the bootstrap BLOCK the M on `WaitForSingleObject(hProcess)` and get subprocess concurrency from MULTI-M work-stealing (their true-yield `SyncOpProcessWait` path is built-but-DEAD in both). shv2 is SINGLE-M, so it cannot block the M — the netpoll blocks on the handle set only when the run queue is empty, exactly as the timer netpoll already blocks on `osSleepMs`. The bootstrap validates the OUTPUT (exit code), not this mechanism; the yield is proven by `interleave-with-sleep` (21 not 12). **THE #1 RISK (TIB stack bounds on CreateProcessA from a GT stack) EVAPORATED** — shv2's FIXED 1 MiB GT stack (vs v1's 2 KB) runs CreateProcessA correctly with RSP outside the main-thread TIB bounds; NO TIB save/restore added to `__gt_context_switch` (the v1 TAKE was not needed). Probed first via a synchronous blocking wait before layering the netpoll yield.
> ### 🔴 THE FINDS (coordinator verification + independent review — the process working):
> - **A REACHABLE HEAP OVERFLOW (coordinator):** `__gt_proc_add` wrote `procHandles[count]` with NO bound check; >64 concurrently-parked children (a loop of `async runProcess` then one await drives them all to their park) overran the 64-slot arrays → **segfault 139 (sabotage-verified)**. FIXED: a capacity guard `osExit(ProcStoreOverflowExitCode=70)` at 64 — a documented, SAFE hard bound (`WaitForMultipleObjects`' MAXIMUM_WAIT_OBJECTS; >64 = the IOCP C rung, no consumer yet). Pinned by `store-overflow-aborts`.
> - **A COVERAGE GAP (coordinator):** NO committed test parked ≥2 children concurrently (sequence/spawn-loop are one-at-a-time). Added `multi-concurrent` (3 parked → count=3 WaitForMultipleObjects + parallel-array swap-remove → 123).
> - **THE SIGNATURE BUG, review found it (`a9b3d00b4`):** the Win64 outgoing-arg ABI (5th+ stack-arg displacement + outgoing frame size) was written TWICE — `createProcessArgDisp`/`createProcessOutgoingArgBytes` byte-identical copies of the pre-existing `writeFileArgDisp`/`stringPrintOutgoingArgBytes`; a change to one miscompiles the other's calls. Collapsed into shared `win64OutgoingArgDisp`/`win64OutgoingArgBytes` (byte-identical codegen). Review assessed `buildGtProcCheck` vs `buildGtTimerCheck` as SHAPE-not-fact duplication and correctly declined to force-merge (the parallel-array store is load-bearing).
> Optimizer: nothing superlinear; own cost FLAT +28 allocs/+2,040 bytes = the 5 unconditional PE imports (`buildGtProcCheck`'s O(n²) is capped at 64 → linear-in-practice). 8 bespoke `async-subprocess.md` cases (exit-code / sequence / multi-concurrent / interleave-with-sleep / spawn-loop / spawn-failure-aborts / store-overflow-aborts / non-string-arg-rejected). **⇒ NEXT per the path-to-harness survey = async subprocess STDIO (pipes) — needs P1.6 generics / P1.7 Array / P1.8 String-methods (the harness cone); the async R3 substrate (yield + netpoll + timer + subprocess-exit) is now COMPLETE.** Residuals → OPEN #92 (per-call slab scratch bump-leak, B1c-reclaimed) + #93 (runProcess spawn-failure/overflow abort silently — a throwing `runProcess` is a follow-up). [[project_p15_b1_gt_scheduler]]
> ### ✅ **`P1.5-B1a′ slice 1` IS CLOSED (2026-07-22, merged, 989/0 unchanged host) — the GT→SYSTEM-STACK SYSCALL SHIM. A green thread's Win32 kernel calls (CreateProcessA, WriteFile, WaitFor*, Sleep, QPC, VirtualAlloc) now run on a single global 64 KB SYSTEM stack, not the GT's own stack, with the NT_TIB stack bounds (`gs:[0x08]`/`gs:[0x10]`) repointed at the system stack for the call. GT stacks STAY 1 MiB, so nothing faults today — TRANSPARENT infrastructure whose correctness proof is "every spec stays green through it"; it is what makes slice 2's 2 KB relocating GT stacks safe. x64-windows.**
> Ported v1's `emitX64GtIatCallOnSystemStack`/`SystemStackEnter` (`X64Backend.maxon` ~8290-8409), with TWO shv2-native divergences forced by the register-allocated Std runtime: **(1) COPY, not write-after-switch** — v1/bootstrap switch RSP then write the call's stack args on the system stack (sound only for HAND-assembled runtime); shv2's `__print_string`/`__gt_process_run` are builder-built Std addressing spills AND outgoing args RSP-relative, so an early switch would make a spill reload read the system stack = silent miscompile. The shim wraps ONLY the `iatCall` (RSP unchanged through arg-setup) and COPIES the N true stack-arg words (CreateProcessA 6, WriteFile 1) from the GT frame to the system stack after the switch; ≤4/>4-arg unify into one helper keyed on the import's stack-arg count. **(2) TIB in the CONTEXT SWITCH** — `__gt_context_switch` now SAVES the suspending GT's TIB bounds and LOADS the resumed GT's, so `gs:[0x08]`/`gs:[0x10]` always describe the executing stack (GT0's real bounds captured on the first switch away from it). ⚠ **This SUPERSEDES the subprocess-seed box's "NO TIB change needed"** — that held only because 1 MiB GT stacks + no shim never probed the TIB into a fault; the shim (and slice 2's 2 KB stacks) make TIB coherence load-bearing. Gated on `moduleUsesGt` (0 non-async fragments churn; non-GT codegen byte-identical) and self-gates at runtime (`currentGt==0 || stackBase==0 → direct call`, so GT0/main is unshimmed).
> 🔴 **REVIEW found THE SIGNATURE BUG in the rung's own new code:** `gtShimStackArgWords` restated "CreateProcessA = 6 / WriteFile = 1" as its OWN constants — a THIRD copy of a fact the isel owns (`CreateProcessOutgoingSlots`), with a comment CLAIMING a drift-safety property the code did not have. A future CreateProcessA arg-layout change would silently miscompile (kernel reads garbage stack args). FIXED: the count lives ONCE in `StdToX64Conversion.maxon`, exported, read by the shim — isel reservation + shim copy now derive from one fact (byte-identical). Review adversarially PROBED the hand-asm (register discipline vs the actual `iatCall` implicitDefs mask, copy offsets, TIB coherence across full switch cycles, guard, alignment, no-nesting) — all correct. Optimizer: nothing superlinear, all scale-test deltas 0 (shim never fires on the non-GT corpus). **⇒ NEXT = slice 2: relocating morestack + shrink GT stacks to 2 KB** (the prologue guard, the hand `__gt_morestack` chain-walk chunk, `osFreePages`, the alloc/free/free-old lifecycle) — the deep-recursion red baseline; kernel-safety already solved by this shim. [[project_p15_b1_gt_scheduler]]
> ### ✅ **`P1.5-B1a′ slice 2` IS CLOSED (2026-07-22, merged, 993/0 → rebased onto P1.6-B2 = **1002/0** on the merged tree) — RELOCATING MORESTACK + 2 KB GREEN-THREAD STACKS. ⇒ P1.5-B1a′ (relocating morestack) IS COMPLETE. GT stacks now START at 2 KB and GROW-AND-RELOCATE on a per-function prologue-guard fault: `__gt_morestack` copies the stack to a 2× `VirtualAlloc`, WALKS the saved-rbp chain fixing every interior frame pointer by the relocation delta, frees the old stack, and returns onto the new one. This is the model the frame-pointer-on-every-function decision (P1.0d.3) was MADE for. x64-windows.**
> Ported v1 `X64Backend.maxon:7102` (morestack) + `:7226` (chain walk) + `:7286` (guard) line-by-line. Prologue guard (byte-level in `emitFunctionChunk`, gated `usesGt && !isRuntimeFunction && frameBytes>0`; uses r10/r11, non-arg regs): `cmp rsp−frameBytes−margin, gt.stackGuard@0x50 ; jae skip ; else call __gt_morestack`. Reuses slice-1's scratch stack + TIB helpers. New `osFreePages` Std op (→ `VirtualFree`; rejects non-x64-windows with `panic` — the GT runtime is x64-windows-only, so the arms are unreachable). Free-list DELETED → alloc-on-spawn / free-old-on-grow / free-on-complete (shrinks OPEN #88's never-awaited leak from 1 MiB to a 2 KB seed). Runtime (`__`/`mrt_`) functions EXEMPT from the guard (they must not re-enter the grower; safe because their kernel calls are shimmed onto the scratch stack by slice 1, and their frames fit 2 KB — verified empirically). ⭐ SOUNDNESS: shv2 has no addressable stack slots, so the ONLY stack→stack pointers are saved-rbp links + `gt.sp`/`gt.fp`; the walk fixes the chain, and `gt.sp`/`gt.fp` are stale-but-overwritten-on-next-switch (a switch-IN requires a prior switch-OUT). x64 does NOT fix `gt.sp`/`gt.fp` (only arm64 does).
> 🔴 **RED-BEFORE-GREEN:** guard-off at 2 KB → deep recursion ACCESS-VIOLATES (0xC0000005 — a 2 KB stack cannot hold 200 frames); guard-on → green. New `async-stack-growth.md` (deep-recursion / multi-growth ≥4 / grow-across-yield / grow-then-complete-then-respawn). REVIEW adversarially probed `deepRecurse(1000/3000)` through 5–7 growths, main-thread deep recursion (GT0 never grows), yield-at-deepest-frame + resume, sibling-frees-while-parked-deep, print+String at depth 800 (margin coverage), CreateProcessA at depth 600 on a grown stack — all exact, no leak; and FIXED a cross-boundary duplication (the "repoint TIB at the system stack" idiom open-coded in BOTH the shim and morestack → shared `emitRepointTibToSystemStack`). Optimizer: growth policy DOUBLES (amortized O(1)/word), guard O(1)/fn, no superlinearity; the flat +216 B is the one new unconditional `VirtualFree` import (pre-existing import-table design — every x64-windows PE lists all IatSlots; a `usesGt`-conditional import table would be its own optimization rung). ⇒ **The full R3 async substrate AND the relocating morestack it was designed for are DONE.** Off the critical path: B1b (multi-M) + arm64/wasm morestack parity. [[project_p15_b1_gt_scheduler]]
> ### ✅ **`P1.6-A` IS CLOSED (2026-07-21, merged, 957 → 966/0) — GENERIC TYPES over TRIVIAL type arguments. FRONT-END ONLY, no layout descriptor, no new IR op.**
> `type X uses T` declarations + `typealias Y = X with C` instantiation. A type parameter is an **opaque 8-byte value** (`MaxonType.typeParameter` → i64), so a generic type's method bodies compile **ONCE** against `T` — no substitution, no monomorphization, no descriptor. **⭐ THE CENTRAL INVARIANT: a trivial generic instantiation lowers to BYTE-IDENTICAL Target IR as the equivalent monomorphic struct** (whole program, verified). `Self{}` in a shared body allocs `fieldCount*8` with destructor pointer 0 (trivial); a struct type-argument is a **BORROW** (the store into a `T` field is not a move — the caller retains + drops it), which keeps the shared trivial destructor sound. **Managed type args are REJECTED** (E2057, deferred to P1.6-B) so no leak ships (4 coordinator + 10 reviewer adversarial leak/UAF probes all clean). New arms `typeParameter(TypeParamId)`/`genericInstance(GenericInstanceId)` band-appended past `function`; args held OUT OF LINE in an **O(1) composite-key `GenericInstanceRegistry`** (NOT v1's O(n²) structural scan). New codes: **E2055** GenericBaseNotGeneric (oracle-parity "has no associated types"), **E2056** GenericArityMismatch, **E2057** GenericManagedArg. Generic FREE functions are unsupported (oracle-parity E2010 — generics are type-level only). 9 bespoke `generic-types.md` tests; `sizeof.type-parameter`×2 ported disabled → P1.6-B. **⭐ Independent review killed a latent silent-wrong-answer**: `paramNamesIn`/`paramTypeParamFlagsIn` were two index-aligned parallel walks (a misaligned flag → wrong-param borrow → struct leak) — merged into ONE walk (structural alignment). Optimizer: **no superlinear defect** (own cost linear, corpus-blind to `with`). ⚠ **maxon-sharp bug found (NOT triggered, cleanly worked around)**: the bootstrap tracks droppable union cases in a u64 bitmask, so a Maxon union's **65th** managed arm corrupts its synthesized destructor; shv2's `ParseError` is at exactly 64, so the 3 generic diagnostics route through a new `FileParseArtifact.diagnostics` channel (single-sink, review-verified sound) — **the bootstrap fix is a separate follow-up**. **⇒ NEXT = P1.6-B (the layout descriptor goes LIVE: managed-T + dictionary-passing + drop-through-`destroyFunc@40` + `sizeof(T)`), then P1.6-C (per-instance ranged typealiases).**
> ### ✅ **`P1.6-B1` IS CLOSED (2026-07-21, merged `96790fa01`, rebased onto async-subprocess main → 989/0 host) — `sizeof(T)` via a LIVE LAYOUT DESCRIPTOR + the DICTIONARY-PASSING substrate. The descriptor-READ half of P1.6-B; managed-T + drop is B2. x64-windows.**
> ⭐ **DECISION (settled by the committed architecture, not a user ruling): DICTIONARY-PASSING, not monomorphization.** The two references disagree — the bootstrap MONOMORPHIZES (`MonomorphizationPass`, `sizeof` folds post-substitution), v1 threads a runtime descriptor. shv2 follows v1 because P1.6-A committed to shared bodies ("compiled ONCE against opaque T") + PLAN's "dictionary-passing + 64-byte descriptors" + the `funcAbs64InRdata` scaffolding. The bootstrap is the BEHAVIOR oracle (managed T allowed, `sizeof(T)`=`sizeof(concrete)`: bool 1, 2-int-struct 16, String 48 = the full envelope, drop exactly-once). NEW `Compiler/IR/LayoutDescriptor.maxon` — a 64-byte descriptor byte-ABI-identical to v1's (`size@0`/`alignment@8`/`elementSize@16`/`flags@24`/`copyFunc@32`/`destroyFunc@40`/`fieldOffsetsPtr@48`/`elementLogicalSize@56`); at B1 only the DATA fields are non-zero (funcptr slots 0 ⇒ **NO `funcAbs64InRdata` reloc yet** — that's B2), the gid→label registry is an **O(1) Map** (NOT v1's linear scan). `sizeof` is NET-NEW (no `sizeof` existed for any type): lexer keyword + parser + `MaxonOp.sizeofType`; a CONCRETE operand folds to a literal in the PARSER (`mergeArtifact` doesn't remap op operands, so a `structRef` id can't ride the op cross-file), a `typeParameter` operand emits `sizeofType(typeParameter)` lowering to a runtime read of the threaded descriptor's `elementLogicalSize@56`. **Dictionary-passing substrate:** a generic method that (transitively) reads `sizeof(T)` reserves ONE hidden trailing i64 descriptor param (slot in `[0,paramCount)` for the `ParamCrossForbid` clobber guard); a concrete call site materializes `rdataAddr(__layout_…)`, a self-receiver call FORWARDS the enclosing descriptor. Also closed a P1.6-A gap: a generic-alias constructor result is retyped `structRef(base)`→`genericInstance(gid)` so the receiver carries the instance identity, and `genericInstance` joins the managed-aggregate boundary (owned box dropped once; the type-arg store is a BORROW) — a genuine leak-fix.
> ### 🔴 THE FINDS (coordinator adversarial probing + independent review — the process working): **FOUR reachable compiler PANICS on valid programs, ALL the committed suite was green over, ALL fixed (never filed):** (1) transitive `self.helper()` where the helper reads `sizeof(T)` (non-transitive pre-scan + `self` typed `structRef`) → FIXED with a transitive fixpoint + self-receiver forwarding; (2) multi-parameter `sizeof(K)` — the implementer REPORTED a clean rejection that did NOT fire → FIXED to a genuine E2015; (3) `sizeof(T)` in a STATIC method and (4) in a CLOSURE (both receiverless) — found by the independent review → FIXED to a clean E2015 (no `__self` in scope). Optimizer found + fixed the ONE superlinearity: `solveDescriptorNeedFixpoint` was **O(N²+N·C)** (Gauss-Seidel iterate-to-stable, quadratic in a generic type's method count on a deep self-call chain, measured 288→30 ms parse at N=800) → **O(N+C)** reverse-reachability BFS over a CSR reversed self-call graph, byte-identical codegen. Review deduped 3 (the hand-rolled CSR → shared `CsrGraph.buildCsr`; `Parser` size-compute clone → the one `ProgramSignatures` authority; `registerRdataConstant`/`…Labeled` bookkeeping → `appendRdataPayload`). ⏭ **BOUNDED LIMITATION (documented, blocks no spec):** forwarding a descriptor from a non-`self` same-base value (`let c = self.makeCopy(); c.directSize()`, or `combine(other Self)`) → clean E2015, deferred to a later slice. Acceptance: flipped `sizeof.type-parameter` (→1) + `sizeof.type-parameter-struct` (→16); added bespoke `sizeof.concrete` (25), `sizeof.self-forward` (1), `sizeof.transitive-two-hop` (16). ⚠ maxon-sharp `fmt` mis-parses union/enum keyword-named cases (`await(`→`await (`) — reverted by hand, a separate bootstrap follow-up. **⇒ NEXT = P1.6-B2 (managed type args + drop-through-`destroyFunc@40`: the `funcAbs64InRdata` reloc across the 6 targets + WASM funcref-table, destroy-thunk synthesis, revise the trivial-scalar classification, remove E2057), then P1.6-C (per-instance ranged typealiases).**
> ### ✅ **`P1.6-B2` IS CLOSED (2026-07-21, merged `94269ee23`, rebased onto P1.5-B1a′ main → 998/0 host + 938/0 wasm) — MANAGED generic type arguments, OWNED + dropped EXACTLY ONCE. `Box with String` works end to end. x64-windows (+ wasm scalar; a managed arg is Beyond wasm's scalar core).**
> ⭐⭐ **THE B1-BANNER "NEXT" WAS WRONG, and a survey caught it BEFORE the wave: shv2 drop is NOT a header-destructor pointer.** `emitBoxAlloc` always bakes `TrivialDestructor`; the drop callee is picked STATICALLY at the parser decref site (`decrefCalleeFor`). A concrete `Box with String` is dropped at a CONCRETE site where the value carries `genericInstance(gid)` (via `retypeGenericAliasConstructorResult`), so the parser knows the gid ⇒ STATIC per-instance destructor synthesis. **⇒ B2 needs NO `funcAbs64InRdata` and NO descriptor `destroyFunc@40` (they stay 0/panicking).** That reloc path is needed ONLY to drop an OPAQUE `T` at RUNTIME inside a shared body (an overwrite `self.value = newT`, or `Array` element teardown) — DEFERRED (folds into **P1.7 `Array`**); the full reloc implementation map (mirror `StringBufferFixup`, `GlobalDataTable.pendingRdataRelocs`, debug-symbol VA resolve, wasm funcref-index) is captured for that day.
> ⭐ **THE DESIGN RULING — MODEL A (owning containers), settled by the plan's charter (NOT a user ruling).** The implementer STOP-and-REPORTED that the naive "synthesize a destructor that drops the field" DOUBLE-FREES: shv2 generic instances BORROWED their `T` (the P1.6-A invariant, `Parser.maxon:2509` — the box aliases, the caller keeps the +1), and with NO `__mm_incref` a box destructor + the caller both drop → underflow. Coordinator ratified OWNING containers: a MANAGED type argument is CONSUMED into the box at the concrete construction site (reversing the P1.6-A type-param-borrow invariant FOR MANAGED INSTANCES ONLY — trivial args still borrow, goldens byte-identical; authorized because the borrow was an explicit deferral and the invariant's own comment says it exists only "because the trivial destructor never drops it"), and the box drops it via `synthesizeGenericInstanceDestructor` (a clone of `synthesizeStructDestructor` sharing `emitFieldCascadeDestructor`). The consume: `scanConsumeBits` records a per-constructor-param TYPE-PARAM-POSITION feed; the concrete call site consumes the arg iff `genericInstanceArgIsOwned(gid, pos)`. The own/borrow boundary is ONE classifier read by BOTH the consume gate and the drop gate ⇒ **consumed set == dropped set by construction.** E2057 RETIRED to `reserved`; nested `with` parses + interns via a flat pre-order `GenericArgNode`; a `T`-field read on a concrete receiver retypes to the substituted arg (a borrow).
> ### 🔴 THE FINDS (the process working): the implementer's stop-and-report (→ Model A) + the independent review found a **reachable DOUBLE-FREE the green suite hid** — a single MANAGED value stored into TWO owning fields (`Self{a: v, b: v}`), consumed once but dropped twice (exit 101). PRE-EXISTING in the monomorphic struct path; B2 makes it reachable via a generic managed arg. FIXED AT THE ROOT (shared struct-literal layer): the 2nd store of a moved managed value is a **use-after-move (E3102)**, closing BOTH forms (shv2 is move-only, no `__mm_incref`). Coordinator adversarial probes confirmed the owning thesis (double-hop escape reads valid content; var-reassign double-free-free). Optimizer: NO superlinear — the +87..+2,195 linear delta is a per-call `ConstructorInstance` union box (0.02%, mirrors `ReceiverArg`); a cheap shared-global fix hit a UAF (the bootstrap MOVES the union) and was correctly REVERTED. Review deduped `resolveArgList`, dropped 2 orphan fragments. Acceptance: converted `error.managed-string-arg`/`error.managed-struct-field-arg` → success, enabled `generic-nested-trivial`, authored leak-gated `managed-string-arg-loop` / `managed-instance-escape-owns-content` / `nested-managed-cascade` / `managed-string-arg-moved-not-double-freed` / `generic-string-member-via-generic` + `error.generic-double-store-managed` + monomorphic `error.managed-double-store` + `scalar-double-store`. ⏭ **BOUNDED (documented, sound):** a TRIVIAL generic instance's double-store (`DPair with Integer`, `Self{a:v,b:v}`) is ALSO rejected E3102 (shared body compiled once against opaque `T`, move-only has no clone) — stricter than monomorphic `IntPair`, niche. **OPEN #94** filed (a union payload typed as a generic-instance alias constructs/drops but can't be match-bound — clean E2015, beyond-scope). **⇒ NEXT = P1.6-C (per-instance ranged typealiases); the deferred descriptor-`@40`/`funcAbs64InRdata` opaque-T runtime drop folds into P1.7 `Array`.**
> ### ✅ **`P1.6-C` IS CLOSED (2026-07-22, merged `e65a164c9`, rebased onto P1.5-B1a′ slice-2 main → 1011/0 host + 947/0 wasm) — PER-INSTANCE RANGED TYPEALIASES. ⭐⭐ THIS COMPLETES P1.6 (A + B1 + B2 + C). Front-end / type-system only, no codegen. x64-windows.**
> A ranged `typealias Idx = int(…)` declared inside a generic type is NOMINALLY DISTINCT per instantiation: `WrapperA.Idx ≠ WrapperB.Idx` even when both are `Wrapper with Integer` — distinctness keyed on the INSTANCE-ALIAS NAME, not the type args. Referenced as `Instance.Idx`; cross-instance misuse → E3005; `as` converts between compatible per-instance aliases; a per-instance Idx DECAYS to plain int where a plain numeric is expected. The Idx is a scalar → erases to i64 before lowering, so NO codegen, no new IR op (P1.6-A's "front-end only" envelope). **DESIGN = Option B** — ride `MaxonType.named` with a qualified REGISTERED name, reusing the struct/boxed-union nominal-identity machinery (`namedAggregatesConflict`/`aggregateNameOf`), settled vs a new arm. The bootstrap is the behavior oracle (v1 mirrors it).
> ⭐ **THE MODEL MISMATCH the implementer STOP-and-reported: `WrapperA` and `WrapperB` intern to ONE `GenericInstanceId` (keyed `(baseId,args)`) and method dispatch produces ONE shared `Wrapper.setTag` signature — so the alias NAME is NOT recoverable from a value's gid; it is a PARSE-TIME source fact.** Coordinator greenlit a parser-side `valueInstanceAlias` map (value→originating-alias-name, propagated through `Self`-returning methods, delivered per-function via `FunctionRangeChecks`) + a parse-time method-arg check; free-function syntactically-qualified params (`takeStrTag(t StrWrapper.Idx)`) keep the SemanticCheck route. Rejected: distinct gids (breaks descriptor/destructor dedup), a per-value IrFunction column (can't produce the EXPECTED name for a shared method), monomorphized signatures (breaks the shared-body thesis).
> ### 🔴 THE FINDS (coordinator oracle-probing + review — the process working): **TWO reachable front-end wrong-answers the green suite hid, both FIXED:** (1) the `as`-cast RETAGGED ITS SOURCE in place → `let b = aTag as WB.Idx` corrupted `aTag`'s nominal type → a later valid use of `aTag` false-E3005'd; FIXED to emit a DISTINCT `value + 0` result (source preserved). (2) a per-instance value OVER-REJECTED on RETURN/reassignment/coercion instead of DECAYING to plain int (`return w.getTag()` from an `int` fn → false E3005 where the oracle exits 42); FIXED with ONE `aggregatesConflict` authority — the per-instance conflict fires ONLY when the TARGET is itself a per-instance alias (a per-instance source into a plain numeric decays; struct/union unaffected, caught by the tag check first). Optimizer REFUTED the implementer's "allocates nothing" claim (a REAL +2,505..+56,197 alloc regression: `Parser.create` minted 5 collections/construction + a per-call union box) → FIXED allocation-neutral (module-global shared-empty COW + a `hasInnerAliasParams()` gate), ~99.4% removed. Review traced the module-global COW invariant sound (all writes behind an is-identity guard) + consolidated the aggregate-conflict rule. Acceptance: the 5 canonical `per-instance-typealias.md` cases + bespoke `cast-preserves-source` / `error.cast-does-not-launder-source` / `return-decays-to-plain` / `reassign-decays-to-plain`. ⏭ **BOUNDED (matches the oracle, NOT a defect):** factory/constructor inner-alias args aren't nominally checked (`WB.create(tag: aWAIdx)` decays — the bootstrap does too) — a future strictness slice. **⇒ P1.6 IS COMPLETE. NEXT = P1.7 `Array` (= P1.6 ∘ P1.2), which also picks up the DEFERRED descriptor-`@40`/`funcAbs64InRdata` opaque-`T` runtime drop.**
> ### ◑ **`P1.7` Slice 3a — CONCRETE managed-element arrays (THE CRUX, first half) — CLOSED (2026-07-22, merged `15ace5d01`, rebased over parallel P1.5 #68/#78 → 1100 → 1140/0). P1.7 still ◑ — Slice 3b (the reloc) + Slice 4 + residuals remain.**
> `Array with String`/struct/enum where the element type is CONCRETE (statically known). The element-DESTROY on array drop: `element_destroy@40` is stamped at the concrete construction site with **`funcAddr(__destruct_<Element>)`** (a runtime store into the heap record via the P1.5-A1 code→register path — **NO `funcAbs64InRdata` reloc**; that's for opaque-`T`, = Slice 3b), and `__arr_decref` runs a null-guarded per-slot walk calling that fn. ⭐ **THE SHV2 DEPARTURE FROM v1 IS REALIZED: the walk calls the element's DESTROY FUNCTION once per live slot, NOT v1's `&__mm_decref` refcount-net** (shv2 is move-only). Ownership: push/set/insert MOVE-in (consume, E3102 on reuse); get/first/last = element BORROW; pop/remove = OWNED move-out. Element_size = 8 (pointer) for managed. Optimizer found + fixed a REAL **O(files×types)→O(types)** superlinearity (`internArrayLiteralAggregateInstances` re-interned every declared type per aggregate-literal file — invisible to the array-free scale corpus, caught by reading).
> ### 🔴 THE FINDS (independent review — the process working, THREE this slice): **(1) a NEW reachable LEAK the green suite hid — the commit CLAIMED `set`/`clear`/`resize`-shrink destroy the vacated managed elements, but the destroy walk was wired ONLY into `__arr_decref`** (claim ≠ code = one-fact-written-twice); `a.set(0,s3)`/`a.clear()`/`a.resize(1)` all LEAKED (exit 101). FIXED: one shared `emitDestroyRangeIfManaged` gate across all 4 vacating ops (verified: no 101). **(2) the mandated fix — `f(x, x)` cross-arg DOUBLE-CONSUME silently double-freed (exit 101)** where the struct-literal guard rejects `Self{a:v,b:v}` with E3102 (the check existed for literals, not call args). FIXED: `transferConsumedArgs` shares `isRepeatedOwningMove` with `rejectDoubleOwningStore` (single-homed), precisely scoped to both-positions-CONSUME (two-borrow is fine); bespoke `moves.md` tests pin both. **(3) verified the 2 implementer pre-existing fixes SOUND** (borrowed-long-literal→owned promotion is a real heap copy; `self.arr.push`/`try self.arr.get` field-chains parse). **⇒ SPEC PORT (Slice 3a, ~29 B-cases):** stdlib-array(8 string-elem), array-managed-elements(3), array-enum-element-size, challenge-array-of-structs, struct-array-get-refcount(2), array-slots(6 struct), struct-enum-array-grow(managed), array-of-bytearray(1), + more. **RESIDUALS (named):** (a) ⭐ **borrow-on-get + `try arr.get(i) otherwise <owned>` = a BUMP-MASKED latent UAF** on the taken error path (the owned fallback drops on the error edge; a read then hits freed-but-bump-intact memory) — same class as the accepted P1.2 #40/#43 bump-masked UAFs. **The honest cure is a THESIS DECISION for the user: incref-on-get (contradicts static-ownership) OR copy-on-get OR E3070 borrow-liveness — deliberately absent in move-only.** Revisit at P1.8/when the allocator recycles. (b) **nested union payload** (union-in-union) / **enum-field `.rawValue` via field chain** / **enum-case match pattern** / **>6-param M5 ABI** — pre-existing mechanism gaps (union/match/ABI), each disabled with reason; (c) **array-push transitive-consume** (consume-by-use fixpoint doesn't treat array-push as consuming) + **E2013 param-field mutation** — the Slice-1 borrow divergence; (d) **E3070 borrow-across-mutation** (`array-realloc-dangling-ref`, 3) — no borrow-liveness infra, deferred. **⇒ NEXT = P1.7 Slice 3b (opaque-`T` managed elements — build the `funcAbs64InRdata` reloc + descriptor `destroyFunc@40`), then Slice 4 (slice/append/clone).**
> ### ◑ **`P1.7` Slice 2 — byte-string `b"…"` literals — CLOSED (2026-07-22, merged `82e8f35a9`, rebased over the parallel P1.5 value-merge cluster → 1082 → 1100/0). P1.7 still ◑ — Slices 3, 4 + residuals remain.**
> A `b"…"` literal lowers to an rdata-backed **`Array with Byte`**: 48-byte record, `buffer@0` → an immortal `.rdata` blob (Latin-1, `element_size@24 = 1` byte-packed), `capacity@16 = -2` (the immortal-rdata sentinel), `element_destroy@40 = 0`. It rides Slice 1's Array-with-Byte (gid, descriptor, accessors already existed). Codepoint > 0xFF ⇒ **E1004** at parse time (the **self-hosted oracle** behavior — the C# bootstrap silently Latin-1-transliterates and is NOT the oracle here; `byte-string-literal-codepoint-range.md` is `status: selfhosted`). E1004 (`LexerInvalidEscape`) was REUSED (a shv2 claim added to the existing csharp+selfhosted code — matching self-hosted's overload), so **maxon-sharp/selfhosted stayed untouched**. The escape decoder shares String's `escapeByte` table (one authority; only the loop wrapper + `\xNN`/`\uNNNN` + Latin-1 collapse fork — a justified, documented fork). Optimizer: **inert on the array-free scale corpus** + linear-by-reading audit (decode O(literal-len); blob registration reuses the amortized-O(1) `registerRdataConstant` cursor).
> ### 🔴 THE FINDS (independent review — the process working again): **TWO reachable bugs the green suite hid, BOTH FIXED before merge (both the "one fact written twice" shape):** (1) `try b"…".get(…)` was REJECTED — the try-able-literal-primary set had an array-literal arm but no byte-string arm (drifted); FIXED to mirror the array arm (verified). (2) **growing a byte-string LEAKED (exit 101) + freed `.rdata`** — `__arr_grow`'s old-buffer free lacked the `capacity<0` sentinel guard that `__arr_decref` got (`var a = b"hi"; a.push(88)` → 101 + `__mm_free` of a never-alloc'd rdata address); FIXED by gating the grow free-site on `capacity>=0` (verified: exit 0, no 101). **⇒ SPEC PORT (Slice 2):** `byte-string-literal.md` (12 C-cases), `byte-string-literal-codepoint-range.md` (3, E1004), `byte-type.md` (4 prereq). **RESIDUALS (named):** (a) **top-level `b"…"`** (`top-level-let`/`-var`/`dead-global`) — shv2 has NO top-level MANAGED constants (`let G="hi"` at module scope is E2004); this GENERALIZES the "module-level arrays" residual to "top-level managed const" ⇒ a named P1.7/P1.8 follow-up; (b) **bare `ByteArray`/`Array with Byte` type annotations** in a signature/struct-field don't parse (need a `typealias`) — pre-existing shv2 generic-annotation limit (E3011), not a byte-string defect; (c) **element_size 8-vs-1**: Slice-1 `ByteArray.create()` gives element_size **8** (word-slot) but a byte-string gives **1** (byte-packed) — INERT today (element_size is a runtime record field, each array internally consistent, unmixable until Slice 4 append; review confirmed correct reads on both) — DEFER unifying storage-width sizing to when it's observable (P1.8 `bytearray-element-size`). **⇒ NEXT = P1.7 Slice 3 (managed elements — THE CRUX: build the `funcAbs64InRdata` reloc path + descriptor `destroyFunc@40` + the per-slot element-DESTROY walk), then Slice 4 (slice/append/clone).**
> ### ◑ **`P1.7` IS IN PROGRESS — SLICED A/B/C/D. Slice 1 (Array SCALAR/TRIVIAL-ELEMENT CORE) CLOSED (2026-07-22, merged `94b4f667c`, 1011 → 1079/0). NOT COMPLETE — 3 slices + residuals remain.**
> ⭐ **shv2 SYNTHESIZES `Array` NATIVELY — it does NOT compile `stdlib/Array.maxon`** (shv2 never reads stdlib; the whole runtime is hand-built in `Runtime/*.maxon`, like String). `Array` = a builtin one-param generic (SignatureIndex) riding P1.6's `GenericInstanceRegistry`; its value = a pointer to a fused **48-byte** `__mm_alloc`'d backing record (`buffer@0/length@8/capacity@16/element_size@24/parent@32/element_destroy@40`, a DISTINCT record from String's — slot@40 is `element_destroy` not `isAscii`) pointing at a **separate** `__mm_alloc`'d element span (two allocs, like a detached String). **`element_size@24` is the FIRST runtime-polymorphic element size in shv2** — every helper reads it and moves exactly that many bytes via 5 shared byte-granular builders, so ONE helper set serves every element type. New `Compiler/Runtime/ArrayRuntime.maxon` (~975L): create/push/get/set/count/capacity/is_empty/reserve/resize/first/last/pop/clear/insert/remove/decref/grow + `__arr_grown_cap` (Go nextslicecap, ported VERBATIM from `stdlib/Array.maxon:65-95` WITH its scale-test-bend warning). Parser: `[…]` literal desugar, 16 array methods, field-chain receivers (`b.ops.push(1)`), `_ =` discard. `get/set/first/last/pop/remove` THROW (dual-register `tryCall`, `ArrayError.indexOutOfBounds`). **element_destroy@40 = 0 at this slice (TRIVIAL elements only — no element-walk, no reloc); the drop path is single-sited (`decrefCalleeFor`→`genericInstanceBoxDropCallee`→`__arr_decref`) with the "if element_destroy@40 != 0 walk-and-destroy else free" seam in place for Slice 3.** Optimizer: **allocation-neutral** (+0 allocs / +8 bytes flat = one `usesArray` bool; the implementer's flagged +29.. delta PROVEN a stale-baseline artifact — the parent binary reproduces it). Capacity-slot invariant (`[length,capacity)` reads zero) VERIFIED to hold — so Slice 3 is safe.
> ### 🔴 THE FINDS (independent review — the process working): **TWO reachable wrong-answers the green suite hid, BOTH FIXED before merge:** (1) a **throwing accessor without `try` silently swallowed the OOB error** (`let x = a.get(0)` returned dummy 0 where the oracle rejects) — the compiler-internal callee skip bypassed E3057; FIXED with an `isThrowingArrayRuntimeCallee` authority + E3057 (verified: `let x=a.get(0)`→E3057). (2) **integer overflow in `__arr_grow` → heap overflow** (`reserve(2^61)`+push wrapped `capacity*element_size` to 0, under-alloc'd, wrote OOB and RETURNED a value) — FIXED with a round-trip-division overflow guard → abort exit 71 (verified). Review also deduped the `lowerTryCall` runtime-call fork → shared `requireRuntimeCallPositional`. **⇒ SPEC PORT (Slice 1, category-A ~68 enabled):** `arrays`(25), `stdlib-array`(27), `collection`(5), `initablefromarrayliteral`(4), `array-slots`(1), `struct-enum-array-grow`(2), `byte-enum-comparison`(5). **RESIDUALS (named, not hidden):** (a) **module-level arrays** (`array-literal-with-dependency`, `get-empty-module-level-array`) — need const→rdata OR a `__module_init` shv2 lacks ⇒ a named P1.7 follow-up; (b) **value-position `try` on a field-chain array accessor** (`try bag.items.get(0) otherwise 0`) → clean E2015 (the non-throwing twin works; asymmetric) — parseTryCallTarget doesn't run the postfix loop after field access, fix touches ownership-under-`try` ⇒ P1.7 follow-up; (c) `basic-nested-array` (Array-over-a-type-PARAM in a generic body) ⇒ **Slice 3** (dictionary-passing element integration); (d) `field-assign-*` (mutating a PARAM's struct field) → pre-existing shv2 **E2013** borrow rule (a divergence from the bootstrap, NOT an Array bug); (e) `enum-coerces-as-return` (enum→byte RETURN coercion) — a separate coercion mechanism. **BOOTSTRAP-ORACLE BUG (worked around, for a separate follow-up):** maxon-sharp `StandardToX86Conversion` crashes **E9001** on an shv2 fn whose body is `return self.method(...)` (tail-return of a call result). **⇒ NEXT = P1.7 Slice 2 (byte-string `b"…"`), then Slice 3 (managed elements — THE CRUX: `funcAbs64InRdata` reloc + descriptor `destroyFunc@40` + element-destroy walk), then Slice 4 (slice/append/clone).**
> ### ✅ **`P1.5-A1` IS CLOSED (2026-07-20, merged, 792 → 813/0 on x64 + wasm) — NON-CAPTURING FUNCTION VALUES, front-to-back.**
> `let f = double`, function-typed `typealias`/params/fields, indirect calls, forward references, and the function-vs-int (E3005) / throwing-function-as-value (E3101) type discipline. **NO closure literals** (`function(…) gives` = A2). NEW IR ops: Maxon `functionRef`/`indirectCall`; Std `funcAddr`/`callIndirect`; x64 Target `leaRegFunc` (`.text` twin of `leaRegRdata`, via the existing `CallFixup` channel — no PeWriter change) + `callReg` (`FF /2`, clobber `0xFFFF0FC7` held equal to `callerSavedMask()` by `assertCallClobberConsistent`). **⭐ The scaffold named this rung throughout** (`leaFuncAddr`, `arm64BranchLinkReg`, the `closureProducer`/`awaiting` op-category bands, StdOpMeta's `indirectCall` note, E3101's registry doc) — "grep for your rung's name" paid again. Function type is NAME-based (`MaxonType.function(id)` on the `(tag,nameId)` column — no interned FunctionTypeRegistry); forward refs resolve at PARSE time via the complete whole-program sweep (no TypeResolution rewrite — honors "extend the ONE sweep"). E3005 single-homed in `checkDeclaredType`→`functionIntoNonFunction` (`TypeRules.maxon:222`). Ported `first-class-functions.md` (21 of 49 enabled; the rest = closure literals→A2, `field-void-statement`→Array/P1.7, `cross-file-extension-typealias-param`→generics/P1.6). ⚠ Two `##` prose sub-headings in the Tests region were demoted to `###` (shv2's `SpecParser` bounds the region at the next `##` — the `## Deferred` convention; NOT a workaround).
> ### ⭐ wasm IMPLEMENTED (user directive 2026-07-20, not deferred): a funcref TABLE (section 4) + active ELEMENT segment (section 9) + `call_indirect` — `funcAddr`→`i64.const <funcIdx>` (table index == funcIdx), `callIndirect`→`i32.wrap` + `call_indirect (type $sig) (table 0)`. The `call_indirect` return-type is derived wasm-locally (every A1 fn value is `(i64ⁿ)→i64`, so the sig is an arg-count-keyed pre-interned functype — NO cross-tier op change). Full wasm suite 779→**800/0** under wasmtime. **⚠ arm64 DEFERRED** — `arm64EmitLeaFuncAddr` panics (the `.text`-function ADRP+ADD relocation channel was OMITTED per `CodeResult.maxon`, "grow those fields back at the arm64 milestone"); cases restricted `x64-windows, wasm32-wasi`; **OPEN #67** wires it (shared with A2). Optimizer: LINEAR (per-file signature registry + forward-ref resolution both x2.0 on the doubling ladder, +2,494 allocs/rung). Independent review: **MERGE, no harmful duplication, no latent bug in the enabled slice** (probed 6-arg indirect call / callee-reg liveness, E2004-vs-functionRef, E3101 at all routes, E3005 at all coercion sites, wasm i64 narrowing); fixed a "fully wired" arm64 comment lie (`cc8b154cc`). ⏭ Follow-ups filed: **OPEN #68** (statement-position indirect calls / void fn types uncallable), **#69** (2 unpinned E3005 routing sites → 815/0), **#70** (bootstrap match-arm managed-Array-tail-field miscompile). **⇒ NEXT = P1.5-A2 (capturing closures + the Own-tier EscapeAnalysis).** [[project_p15a1_contract]]
>
> ### ⚠ WAVE A — worth carrying (a big slice; the process caught the finds):
> - **The plan was WRONG at the code, TWICE, both forced+implemented:** (1) `buffer@0` is a data→data pointer
>   the fixed-base image has NO base relocation for, so a new **`PeWriter.patchStringBufferFixups`** bakes
>   `ImageBase + rdataRva + blobOffset` after RVAs are known; (2) the spec runner had **no `stdout` fence**
>   (OPEN #2's live warning) — `print` could not be gated without adding one (faking a golden is forbidden).
> - **⭐ The optimizer found a REAL O(N²)** the memory-only instrument is blind to (compile-TIME, allocates
>   nothing): `registerRdataConstant` re-summed all prior rdata payloads per registration; each literal
>   registers TWO constants and literals-per-program are **unbounded** → quadratic at self-host scale. Fixed to
>   O(N) with a running total, offsets bit-identical (verified: 9 interleaved literals print byte-correct).
> - **⭐ The independent review found a SILENT MISCOMPILE this rung introduced** (the self-review missed it,
>   again): the `__` predicate was widened from `__mm_`-only to **any `__` prefix** (needed — `__print_string`/
>   `__str_eq` have no signature), but shv2 never emitted **E2051**, so a USER `function __add(a,b)` called as
>   `__add(7)` now bypasses arity validation and runs with an uninitialised arg (exit 65543) where the
>   bootstrap rejects it. **Reserved-namespace only — NO in-language program is affected, no corpus case** — so
>   deferred (a surgical narrow hits a circular `MmRuntime`↔`StringRuntime` dep; the clean fix is E2051, a
>   6-site slice). Comment corrected to be honest. ⇒ **OPEN.md #35 — the IMMEDIATE P1.2 follow-up.**
> - The backend breadth (50 files, the register allocator etc.) is justified: band-append of the new
>   `leaRegRdata` `TargetOp` + `WriteFile`'s stack-arg/out-param. `print` is the first call needing a stack arg.
>
> ### ⚠ WAVE B — four things worth carrying (the process caught them; nothing shipped wrong):
> - **A trailing enum-arm `and fallthrough` panicked the backend** (unterminated block) — found in the
>   implementer's SELF-review, fixed, regression spec added. Same class as Wave A's crash: *a backend panic on
>   a valid program*. **Every wave this rung has surfaced one; the pattern is real.**
> - **E3034 said "unknown ENUM case" for a UNION** — the noun was hardcoded where E2026/E2046 derive it from
>   `EnumLayout.kindWord()`. ONE FACT WRITTEN THREE WAYS, one disagreeing (the signature disease). Found by the
>   INDEPENDENT review (not the self-review), threaded through `kindWord`, pinned by `error.unknown-union-case`.
>   **462→508 became 507→508.** The self-review is the pass that MISSES things; the independent one is why.
> - **The implementer DEFERRED `enum-method` against the brief** (I listed it enable; it needs instance methods
>   on an ENUM receiver — `match self` over an enum — a distinct mechanism). shv2 rejects it with a clean
>   positioned E2015. **Correct call: the plan was wrong at the code, and it reported rather than shipping it
>   broken.** ⇒ instance-methods-on-enums wants a rung; see OPEN.md #26's overloading gap for the shape.
> - **A flagged "wrong-accept" was a FALSE ALARM** — the implementer thought `Color.red == Color.green` should
>   be E3066, but the ORACLE accepts it too (E3066 is for *payload* unions). Measured before recording: shv2
>   matches the oracle. **Probe the oracle before believing a divergence.** ⇒ two real follow-ups: **OPEN.md #33**
>   (negative-FLOAT enum raw values reject with a misleading E2010 where the bootstrap accepts — a bounded clean
>   reject) and **#34** (union E2026/E2046 wording — shv2 says "union" consistently; the bootstrap hardcodes
>   "enum"; no golden pins either).
>
> **The rung was three deliberately-DROPPED facts acquiring their consumers**, and `readStructFieldInto`'s
> own comment said so before it was written: *"`let` AND `var` ARE READ IDENTICALLY AND THE DISTINCTION IS
> DROPPED… that is P1.1a wave 2… Recording the bit now would be a `StructLayout` column nothing reads."*
> **E3086's user-visible message already promised the mechanism too** — *"and it has no default value"*.
> ⇒ **The rule keeps paying: GREP FOR YOUR RUNG'S NAME FIRST.** Wave 1 left three notes and all three were right.
>
> **What it DECLINED to copy, and why the decline is the point.** v1 desugars each default into a synthetic
> `__field_init_*` nullary function and splices `call + fieldStore(isInit: true)` in a post-TypeResolution
> pass. **Its own comment gives the reason, and the reason does not hold here:** *"the parser is pure-record
> and can't see the type's field list."* **shv2's parser CAN** — `queryProgramSignatures` is a whole-program
> pass that runs before any body parses, which is why `requireAllFieldsInitialized` already reads it. So the
> `method-before-field` ordering hazard that forces v1's whole architecture is **structurally absent**, and
> the copy had no reason. Defaults fold at the declaration (type from the literal's TOKEN KIND — v1's
> `inferShorthandFieldType` shape, no const-evaluator) and materialize in the `Self{}` loop that already
> walks the layout. `isInit` was left behind for the same test: shv2's construction store is a different
> code path from the assignment statement, so the flag would have had **no consumer**.
>
> ### 🔴 THE FIND: **`StdOpMeta.isPure` HAS ZERO READERS, and SIX places called it a live correctness constraint**
> **The THIRD instance of "a gate reported PASS while structurally blind" (#4c, #23), and a SABOTAGE found it
> again.** `loadIndirect` flipped to `isPure: true` — the exact edit six sites call a silent wrong answer —
> and the suite reads **371/0**, including P1.0d.5b's own `global-load-not-hoisted`. **Nothing hoists**
> (there is no LICM/CSE/inliner), so the property holds for a reason nobody had written down, while the
> reason everybody HAD written down enforces nothing. **P1.0d.5b's row below said "pinned by a spec" — it was
> not; that claim is now corrected in place.** `isPure` is KEPT (unlike #7's `setsFlags`, which was a *second*
> home for a live fact — this is the SOLE declaration and its reader is scheduled), and **six of `StdOpMeta`'s
> nine fields have no reader at all.** ⇒ **OPEN.md #27.**
>
> ### ⚠ AND THE RUNG WROTE ITS OWN SUBJECT TWICE — caught by the independent review, not the author
> `if not binding.mutable → throw assignToImmutable` landed at **two** sites with nothing making them agree.
> A rung *about* one-fact-written-twice committed it, in the fact it was adding. Now one
> `requireMutableBinding`; `binding.mutable` is read at **exactly one site** — against the bootstrap's 3
> readers / 4 message copies and v1's 6. **This is why the review is independent and why it runs LAST.**
>
> ### ⚠ KNOWN GAPS, named not hidden
> - **`struct-field-default` stays `disabled-test:` and its reason is CORRECTED — it needs FUNCTION
>   OVERLOADING** (two `Counter.create` arities ⇒ **E3006**), not defaults. **Overloading is on NO rung of
>   this ladder** though the stdlib has overload sets (§487's "env-map overload"). ⇒ **OPEN.md #26**; it needs
>   a number, and that is a PLAN decision. Field defaults are covered instead by `field-initialization.md`'s
>   `all-defaults` / `literal-overrides-default` / `mixed-default-and-literal` — real corpus cases, all green.
> - **`as Type = <non-literal>` is deferred and rejected LOUDLY (E2015 naming the gap).** It needs token
>   capture + replay at each `Self{}` site and has **ZERO reachable consumers** — every corpus case wanting it
>   also wants Array (P1.7), String (P1.2) or a struct-typed field. Building it now is P1.0r's error #2 again.
>   *(⚠ The contract claimed the oracle rejects `= 2 + 3` outright. **FALSE, and measured by the implementer:**
>   `as Integer = 2 + 3` ⇒ 5 and `as Integer = pick()` ⇒ 4. The grammar is ASYMMETRIC — un-annotated takes one
>   literal, annotated takes any expression. The deferral survived; its stated reason did not.)*
> - **The corpus cannot see this rung's cost dimension.** `structTypeDecl` emits exactly **two** fields per
>   generated type while the ladder doubles the NUMBER of types, so `indexOfField`'s O(F²) lives in a
>   dimension `scale-test` structurally cannot measure — F had to be measured against real source instead
>   (**max 22, mean 4.14 over 166 declarations** ⇒ linear in program size; DEBT, not a bug). ⇒ **OPEN.md #28.**
>
> ⚠⚠ **READ THIS BEFORE THE NEXT RUNG — the rung was RE-SLICED TWICE and the CONTRACT was wrong FOUR times,
> every one the SAME error: SOMETHING SHIPPED WITHOUT A CONSUMER.**
> - The plan said structs before the heap. **A struct IS a heap value** (measured: aliasing ⇒ 95 not 6;
>   `sizeof(Outer{p Point, n Integer})` ⇒ **16** not 24 ⇒ a struct field is a POINTER). The dependency was
>   inverted, and *"String is the FIRST heap value"* was false.
> - Then `P1.0r.1 = the vocabulary` — **all four items had NO consumer**, so it was `mintPhi` one step
>   earlier. Slice by **CONSUMER, never by layer.**
> - Then the contract's golden emitted `funcAddr __destruct_Point` while its **own footnote** said a
>   trivial struct passes `const 0` — **the footnote deleted the golden's only consumer.** The
>   coordinator committed the exact error it had warned the implementer about **one paragraph earlier**.
>   ⇒ `funcAddr`/`callIndirect` **deferred to P1.2**, where `__destruct_String` is a real consumer.
> - Then the contract mandated **`__mm_incref`, which the review found had ZERO call sites** — emitted into
>   every binary, never executed. **Deleted.** Its call site is decided by the borrow-vs-consume ruling that
>   does not exist yet: **it had no correct call site to be missing.**
>
> ⭐ **What made the absence CORRECT rather than merely unused:** the refcount word is written by **NOTHING**
> — *the slab's zeroing contract is its only initializer* — so with no incref anywhere, `rc` is **provably 0
> at every drop**. The zeroing contract is load-bearing for the refcount model itself.
>
> ### 🔴 THE FIND: `iatCall` SAID "full call-barrier semantics" AND CLOBBERED NOTHING
> `TargetDialect.iatCall` carried **`implicitDefs: 0`** under a comment reading *"Full call-barrier
> semantics."* **ONE FACT WRITTEN TWICE, DISAGREEING** — in the op metadata. Inert while the only `iatCall`
> was `mrt_start`'s hand-built `ExitProcess` (nothing live across it); **P1.0r put one inside a
> register-allocated function and it went live the same day.** `__slab_alloc` published `base+base` instead
> of `base+size` ⇒ `next > end` forever ⇒ the bump path never runs again ⇒ **one 64 KiB VirtualAlloc per
> object. 133.74 MB for 2,000 Points → 412 KB fixed.**
> ⚠⚠ **It never produced a wrong USER-VISIBLE answer — which is why `specs-shv2` 357/0, the worker-invariance
> gate, AND `scale-test` were ALL GREEN OVER IT. Only reading the emitted machine code found it.**
> ### ✅ **AND THE FIX MADE IT UNREPRESENTABLE — the part to COPY, not the mask** (verified 2026-07-16 by
> trying to put the bug back: `implicitDefs: 0` now compiles **NOTHING**). `assertCallClobberConsistent`
> compares the op metadata against `RegisterAllocator.callerSavedMask` on **every `allocateRegisters`** and
> panics naming the drift. ⇒ **For ONE FACT WRITTEN TWICE, make the two copies CHECK EACH OTHER. That beats
> every gate, because a gate can be pointed at the wrong program and a check cannot.**
> *(The corpus blind spot the entry blamed is ALSO closed now — 2026-07-16, five knobs; structs, the heap,
> globals and the idiv path are all generated. But it was **never what would have caught this**: `scale-test`
> COMPILES each rung and never RUNS it, so shv2's **emitted** runtime is measured by nothing. See OPEN.md
> #23 / #4d.)*
> *(Also fixed: a void function falling off the end never dropped ⇒ **101 on a valid program** — the drop hung
> only off `parseReturnStatement`; and a diagnostic hardcoded `float` under a comment calling it "the only tag
> that can trip this rule" — **a theorem P1.0r falsified in the same commit, without touching the line it
> falsified.**)*
>
> ### ⚠ KNOWN GAPS, named not hidden
> - **A struct declared in a NESTED BLOCK and not returned LEAKS** (verified: a valid program returning 7
>   gives **101**). `closeBlock` emits no drop **deliberately**: a drop there cannot be correct without
>   deciding whether `q = p` retains, and that IS the P1.4 ruling. Wrong here is a **use-after-free**; the gap
>   is a **loud 101**. Fail-safe over untested — reviewed and agreed.
> - **`struct-param` · `struct-return` · `struct-literal-as-arg` stay `disabled-test:` — P1.4, USER RULING
>   2026-07-15.** A struct crossing a call boundary as a PARAMETER forces the **borrow-vs-consume** choice,
>   and **the two references genuinely disagree**: the bootstrap **BORROWS + retains-on-store** (*"on a throw
>   nothing was ever retained, so nothing is owed"*); v1 **CONSUMES** — which is documented as what MADE its
>   member-alias leak. `ARCHITECTURE.md:453`'s `OwnershipKind` lattice expresses both and is *"present but
>   inert."* ⭐ **The leak gate is what made this observable a rung EARLY** — that is the gate working.
> - **`__mm_decref`'s `rc-1` arm is unreachable** (nothing can make `rc` nonzero). **Kept, deliberately**:
>   deleting it collapses decref into a thin wrapper over `__mm_free` and unwinds the 24-byte header. The
>   distinction from `__mm_incref`: incref was never *asked* anything; decref's *"am I the last owner?"* is
>   asked and answered on every drop — the "no" arm is unreachable by the **program set**, not speculative.
> - **Cross-file struct id-remap**: written, sabotage-probed by the reviewer (fires at 2 files, correct — 57),
>   but **no spec case covers it** because the corpus port's enabled set is under user ruling.
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
> command (371/0) — but that harness is compiled by **`maxon.exe`**, the C# bootstrap. Phase 1
> is the other sense, the one `maxon-selfhosted` has and shv2 does not: **shv2 compiles the
> harness itself.** That is the whole difference, and it is the entire mechanism ladder.

**The goal:** implement every hard mechanism of Maxon *minimally*, so the compiler can be built
and validated with fast iterations.

### The honest sizing

Self-host is **~5–20k lines of new Maxon** away (from **44,671** today — measured 2026-07-16; the
`21,038` this line used to cite was 2× stale, see "The honest sizing"), and "Maxon-core" is
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
| **Frontend / pre-scan** | **The parser stays a PURE PER-FILE function of its own tokens.** Whole-program facts come from the ONE `queryProgramSignatures` sweep — extended by **arms**, never by a second pre-pass or an inline `self.signatures.*` shadow-resolver that keeps a second copy of `TypeResolution`'s logic. Sweep + parse both **fan out at P2.6** (parallel extract → path-ordered serial merge → parallel parse, reusing the sweep's cached tokens). *This is the antidote to v1's disease — v1 kept ADDING pre-passes for missing info and slowed every time; shv2 has ONE, and keeping it one is a decision, not an accident.* See **⭐ Frontend parallelism** below. |
| **Conditional conformance** | **IN core** — *declared* in Phase 1 (the stdlib forces it), *emitted* at **P2.2**. It is a hard mechanism, and this plan implements hard mechanisms rather than dodging them. See the PRINCIPLE. |
| **`Map` / `Set`** | multi-param generics + one `Hashable` constraint. No `Iterable`-on-Map, no tuple `Entry`. **P2.3.** `Set` rides Map's exact mechanism. |
| **Iteration** | **Hardcode `for-in`** over Array/Range/String. No general `Iterable`/associated types. |
| **Errors** | v1's design **verbatim**: dual-register `(value, errorFlag)`. No unwind tables. Already minimal. |
| **Stdlib lowering** | **Reachability-seeded — lower only the stdlib bodies the program transitively reaches.** *Not an optimization: it is what makes Phase 1 a phase.* See §"The stdlib cone." |
| **Stdlib fork** | `stdlib-shv2/stdlib/` — ❌ **NOT NEEDED. Re-deferred 2026-07-13 on measurement (P1.0c).** It was un-deferred as the backstop for `Map`, *"if reachability-seeded lowering proves insufficient."* **It proved sufficient** — `Map` is laid out and never codegen'd. The one edge it *could* have cut (`String.trim()` → `CharacterSet` → `Set`) we chose NOT to cut: do the hard things early ⇒ `Set` is in Phase 1 (P1.7b). **So the fork now gates nothing, and its original ~1 s justification is still dead.** Do not resurrect it without a NEW reason. |
| **Targets** | ~~x64-windows only through Phase 2.~~ **SUPERSEDED — arm64-macOS pulled forward (user ruling 2026-07-17), parallel to Phase 1.** The arm64-macOS **scalar-core backend LANDED 2026-07-18** (`85237f70d`): shv2 emits running, ad-hoc-signed Mach-O, and `specs-shv2` runs GREEN natively on arm64 (**497/0**) via new per-target `<!-- targets: -->` gating; ~150 M2+ (heap/String/struct/union/print) tests are gated to x64-windows until the arm64 **runtime floor** (the next arm64 rung). Neutral register model — one allocator, both ISAs; x64 codegen byte-identical. **wasm32-wasi SCALAR-CORE backend LANDED 2026-07-18 (`9a6c6b45d`), reversing the "wasm Beyond" lock — user-authorized.** A **WASI Preview2 COMPONENT** backend, sibling of x64/arm64: it branches off `buildBackend` and consumes the **Std module directly** (no register allocator, no Target dialect — the `wasm32` panics in RegisterAllocator/RegBits/SsaDestruction/TargetPrinter stay dead-but-exhaustive), maps every SSA ValueId to a wasm local, and reconstructs control flow with a **`loop`+`br_table` state-machine dispatcher** (no relooper). Emits a core module (`WasmBinary`/`StdToWasm`), wrapped into a component via the vendored `wasm-tools` (`embed`+`new`, `WasmComponent`), run under the vendored `wasmtime` with `-S cli-exit-with-code=y`. The spec runner gained a **cross-compile-and-run path** (`--target=wasm32-wasi` builds + runs under wasmtime) the per-target `<!-- targets: -->` gating (originally an additive per-target opt-in; **REPLACED 2026-07-19 (`588d82f3a`) by the UNIFORM rule — every test runs on EVERY target unless it declares a restriction, used only for a technical reason** — see the sweep note below). **93/0 exit-code-checked** across the ~16 Tier-1 scalar files; then **FLOATS (`f64`) landed 2026-07-18 (`ab462f0dd`)** — native `f64`, NO runtime / NO data section (f64 ALU/compare + native `f64.const` + `siToFp`/`fpToSi` conversions), un-gating float-type + float-compare-branch: **106/0**. Then the **MEMORY BAND landed 2026-07-18 (`4c8a8f297`)** — top-level `var` globals via a linear-memory active **data segment** (`globalAddr`→`i32.const <base+slot>`, `loadIndirect`/`storeIndirect`→`iN.load`/`store` with a memarg; **addresses are i32**, data base 1024 as a null guard; `GlobalDataTable` threaded through `buildWasmBackend`), un-gating global-load-not-hoisted + top-level-let + the 6 NaN-via-globals float tests: **128/0**. Then **HEAP / String / `print` LANDED 2026-07-18 (`674e104b6`) in two phases** — Phase A (`91e108c45`): the shared pointer-valtype fix (coerce every load/store address to **i32 at the access site** — this RESOLVED the prerequisite that used to sit here), `rdataAddr` + a second `.rdata` data segment with String-record buffer-pointer **relocation**, and `osWriteStdout` → WASI Preview2 stdout (`get-stdout` + `blocking-write-and-flush`); Phase B (`149a9ff7f`): `osAllocPages` → `memory.grow`, the **byte-load band** (`i32.load8_u`/`store8`), and the **leak gate** (`$run` calls `__mm_leak_check`, ported from the native entry stub, verified firing on an induced leak). **The allocator, refcount, and String builders all emit for free via the Std-IR function path** — so structs-with-String-fields + unions-with-managed-payloads came with it, drops and all. Un-gated heap Strings / interpolation / append / equality / drops / structs / unions: **228/0**. Mostly PORTED from v1's `MirToWasm.maxon` (user direction — the wasm emitter is a mechanical translator hitting the identical WASI surface, the one place a near-verbatim v1 port is right). ⚠ Width coercion (i64↔i32) at every value boundary — ret, call-arg, phi-edge, binOp/cmp/div-mod/unary operands — recovers the widths the register backends leave implicit in 64-bit registers (four such gaps found by integration + review, all reproduced + fixed + regression-tested). Then **ERROR HANDLING landed 2026-07-19 (`634f957fb`)** — `throws`/`try`/`otherwise`/`throw`/`panic` via wasm **MULTI-VALUE returns**: a throwing function's signature becomes `(params) → (resultType, i64)` (the native dual-register R8/R10 ABI mapped to a 2-result wasm function — verified surviving the component pipeline), `tryCall` captures both results (flag on top), `errorReturn` pushes both, `osExit` → `exit-with-code`; managed/boxed-union error payloads verified, leak-clean. ⭐ **COVERAGE SWEEP + GATING FLIP (`934154310`, `588d82f3a`, user-directed 2026-07-19):** the per-slice marking had only marked each implementer's own files, leaving ~450 already-passing tests unmarked — so the gating rule was flipped from per-target opt-in to **UNIFORM (all targets run every test unless RESTRICTED for a technical reason)**, all `wasm32-wasi` markers deleted, and only the genuine exceptions restricted (ISA-specific register-allocation tests; `RequiredData` tests that read a native `.data` section a wasm module has no equivalent of). **wasm now runs 732/0 by default** — the host's full runnable set minus those technical restrictions. **STILL BEYOND (later wasm slices): Arrays / generics (`Array with …`, P1.7 — the String methods `clone`/`slice`/`split`/`toByteArray` need it), `async` → asyncify (green threads), the narrow-integer cast widths (`i8/i16/i32/u16/u32/u64` load/store — gated language-wide on P1.9's `as`-cast, so the wasm width door panics loudly for now, mirroring native `accessWidthFor`), and a real relooper for codegen quality (the `br_table` dispatcher is correct but verbose). The HEAP-PREREQUISITE pointer-valtype divergence once flagged here is RESOLVED — every load/store address is now i32-coerced at the access site, so a loop-carried heap pointer wraps losslessly instead of failing validation.** ⭐ **arm64-linux AUTHORIZED 2026-07-19 (user ruling) — TWO RUNGS. ✅ RUNG 1 (compiler) LANDED 2026-07-19 (`c8ec215ef`..`1aa3b4323`): shv2 emits a running STATIC aarch64 ELF for `--target=arm64-linux`** — `min`⇒7, `hello`⇒prints+42, and a heap/String probe (mmap + append + refcounts + literal buffer bake + print) byte-identical to the arm64-macOS control, all under `orb run`; specs-shv2 **772/0** with an EMPTY fragment tree (macOS/x64 codegen byte-identical). An independent review then ran the **whole corpus** through the new target — **590 of 793 spec tests compiled and run on arm64-linux, 0 leaks, 0 faults** — and verified the leak gate actually FIRES there (a NOP'd `__str_decref` ⇒ exit 101; a gate that cannot fire is worthless). Scale-test: **+1 alloc / +48 bytes FLAT at every rung**, a once-per-compile constant (the `ElfIdent` byte literal), isolated by an A/B against the pre-rung parent — the raw delta column's +255k was **accumulated unlogged upstream work, not this rung**, which is why the A/B was necessary. ⚠ **RUNG 1 WAS BLOCKED BY A PRE-EXISTING BOOTSTRAP BUG, now fixed (`ea9a4d94a`):** `maxon-sharp`'s Mach-O writer built the GOT as ONE chained-fixup chain while telling dyld about ONE start page — but **a chain cannot cross a page boundary**, which is what `page_start[]` exists for. Once shv2's image grew ~20 KB the GOT straddled a 16 KB boundary and 16 of 56 imports were never bound, still holding their raw BIND words, so calling one jumped to `0x8000000000000000 | ordinal`; the unbound set was `getdirentries64`/`posix_spawn`/`waitpid`, i.e. `spec-test` itself. A pure size lottery — identical source built green until the image crossed a page. The comment directly above it records fixing the **segment**-level version of the same mistake. shv2's own `buildGot` has the same latent shape (safe only because its GOT is 3 slots and page-aligned) and now **asserts** that invariant rather than trusting it. **The port is unusually cheap, and the reason is that Linux PERMITS what macOS FORBIDS:** shv2 reaches the OS through exactly three GOT import slots (`_exit`/`_write`/`_mmap`) *only* because [macOS forbids raw syscalls](maxon-shv2/Compiler/Targets/Arm64/Arm64Runtime.maxon#L6). On Linux those become raw `svc #0`, so the whole dynamic-linking apparatus — PT_INTERP, GOT/PLT, chained fixups, LC_UUID, ad-hoc code signing — **does not exist**: v1's [ElfWriter.maxon](maxon-selfhosted/Compiler/Targets/Linux/ElfWriter.maxon) is **421 lines against Mach-O's 948**, and it **already patches arm64 relocations** (`patchElfRdataRelocationsArm64`), because v1 shipped x64-linux *and* arm64-linux. shv2's `Targets/` tree already mirrors v1's, so `Targets/Linux/` is a literally empty slot. **AUDITED — the ISA layer is OS-neutral:** `Arm64Backend.maxon` (the encoder) and `Arm64PrologueEpilogue.maxon` need **ZERO changes**, and the two divergences that normally make macOS→Linux arm64 painful **do not apply at all** — shv2 emits **no varargs anywhere**, and works exclusively in 64-bit values, so Apple's caller-extends-small-ints rule is satisfied vacuously. x18 is already out of the allocatable pool. **The one numerically-wrong constant is [`MmapAnonPrivate = 0x1002`](maxon-shv2/Compiler/Targets/Arm64/StdToArm64Conversion.maxon#L73)** (Darwin `MAP_ANON`); Linux `MAP_ANONYMOUS|MAP_PRIVATE` is **`0x22`**. **DESIGN PROVEN BEFORE ANY COMPILER CODE** (2026-07-19): a hand-built static aarch64 ELF — no libc, no PT_INTERP, raw `svc #0` for `write`+`exit` — runs, prints byte-exact stdout, returns exit 42. **TEST SUBSTRATE: OrbStack**, machine `maxon-linux` — native aarch64 (virtualization, **not** emulation), and **the Mac filesystem is mirrored at IDENTICAL paths**, so the runner passes the same `exePath` the compiler wrote with **no path translation**; **~21 ms/exec** against `docker run`'s 200–500 ms, which would have dominated a ~770-test suite. Exit codes + stdout/stderr separation verified. ⇒ **RUNG 1 (compiler) — DELIVERED, as planned except where marked:** port the ELF writer · add an `Arm64Op.syscall` (**shv2 has none** — v1's `Arm64Op.syscall(resultSlot, number:, argSlots:)` is the model) · Linux arms for the three lowering sites ([`:752`/`:776`/`:789`](maxon-shv2/Compiler/Targets/Arm64/StdToArm64Conversion.maxon#L752)) · a Linux entry stub (`_start` takes argc at `[sp]` with x30 undefined — **not** dyld's ABI) · hoist `MachoImportSlot` out of `Targets/Macos/` into a neutral `Targets/Shared/` slot (**a real design leak: two `Arm64/` files import a Mach-O enum today**) · the [`Main.maxon:70`](maxon-shv2/Main.maxon#L70) accept-list + [`BackendDispatch.maxon:249`](maxon-shv2/Compiler/Targets/BackendDispatch.maxon#L249). Acceptance: hello-world exits 42 under `orb run`. ⚠ **TWO ITEMS IN THIS PLAN WERE WRONG AT THE CODE, and both were caught by READING rather than by a gate.** (1) It said to port v1's `.covdata` third PT_LOAD and "mirror MachOWriter's `hasCovdata` branch" — **MachOWriter has no such branch, it PANICS on non-empty covdata**; the plan had been written off a grep hit read without its context. shv2 hard-codes the coverage image empty, so the segment could never execute or be tested, and it was removed for a guard matching Mach-O's. (2) It said to take v1's 64 KiB `.data` BSS tail; **the implementer refused, correctly** — that arena existed because *v1's bump allocator lived in `.data`*, and shv2 mmaps its arenas, so `p_memsz == p_filesz`. Also recorded: **v1's `stashEntryArgv` is a latent bug on Linux** (it stashes argc/argv from x0/x1, the dyld convention; Linux passes argc at `[sp]` with x0/x1 unspecified). shv2 needs no argv at the scalar core so this rung dodges it entirely, and the entry stub carries a landmine comment for whoever wires argv later. ⚠ **HOST TRAP, cost ~20 min:** a freshly rebuilt binary that exits **137 (SIGKILL) with no output** is macOS's code-signature cache poisoning that INODE after repeated rebuilds at one path — NOT a malformed image. `codesign -v` passes while the kernel still kills it; `rm` before rebuilding (new inode) clears it, and copying the same bytes to a fresh path proves it. ⇒ **RUNG 2 (harness) — ✅ LANDED 2026-07-20 (`5f5cd8fca`..`ef6d823c7`): `spec-test --target=arm64-linux` runs the suite through OrbStack at 780/0 — the FULL host-runnable set, the same count as the host.** ⭐ **THE DRIFT NUMBER IS THE RESULT:** across all 780 goldens, `fragments/arm64-linux` and `fragments/arm64-macos` differ in **FOUR files**, and every difference is the one substitution `arm64.importCall 0` ↔ `arm64.syscall 94` — same isel, same register allocator, same encoder, byte for byte. That MEASURES the claim rung 1 was built on, and makes the pair a permanent drift detector: a future arm64 change that diverges between the two OSes appears as a fifth differing file. The three `register-pressure` arm64 markers were WIDENED to name both arm64 targets (they restrict on register-pool grounds, which arm64-linux shares; their goldens came out byte-identical to the macOS twins) — leaving them narrow would have been a per-target opt-OUT with no technical reason, the shape the uniform rule bans. ⚠ **Three bugs fixed here had nothing to do with arm64-linux and would otherwise still be live:** (a) the mod-2⁶⁴ overflow in ALL THREE binary readers' truncation guard (`offset + size > count` wraps, so a header claiming `sh_offset=0xC000…`/`sh_size=0x4000…` sums to ZERO, passes, and panics inside the slice blaming a caller that did check) — now one shared subtraction; (b) `.data`'s `sh_size` was FLOORED along with the segment, so a 1-byte `bool` global advertised 8 bytes and `RequiredData`'s PREFIX comparison could not tell padding from globals — **the gate was WEAKER on arm64-linux than on its twins, on the very rung whose value is drift detection**; `p_filesz`/`p_memsz` are what the loader must MAP, `sh_size` is what was LAID DOWN, and they are different claims; (c) every spec spawn was unbounded (`timeoutMs = 0` = wait forever) — a cross run through `orb` was observed blocking **18m35s AFTER the guest had exited and been reaped**, with ZERO output because the runner's stdout is block-buffered when redirected. Now 120 s, ~1000× the ~0.12 s per-test average: the point is converting an unbounded SILENT hang into a named failure, not policing slow tests. ✅ **BOTH ITEMS ONCE LISTED OPEN HERE ARE NOW FIXED (2026-07-20, `8163f1056`/`847906427`), and the `--workers` one was NOT what this file said it was.** The ORPHAN golden was a stale P1.4b-Wave-2b artifact (`037c29f65` implemented managed returns and deleted the declaring test, but was authored on the x64/wasm host, so only the arm64-macOS golden was left behind) — deleted; the trees now match. ⚠⚠ **THE `--workers` ENTRY WAS WRONG IN EVERY PARTICULAR, AND IT HAD BEEN WRITTEN OFF AS "the host".** It said "≥4, pre-existing, under load". Measured on an IDLE host: **4 passes, 5 fails** — probabilistic, scaling with worker count, and **not environmental at all.** It is a REAL PUBLISH-BEFORE-PARK RACE in the emitted GT scheduler: `__io_submit_read` registers its kevent on the **SHARED** kqueue and only THEN parks (dequeue a successor, then switch — driving the scheduler INLINE on its own stack when nothing is runnable), so any other M polling that kqueue reaps the event in that window and enqueues a **still-running** GT; a third M switches in on its stale `gt.sp`, and because GT stacks start at 2 KB and RELOCATE when they grow, that pointer is into `munmap`ped memory. Caught under lldb with `P0->currentGt == P4->currentGt`. `MAXON_MAX_PROCS=1` at `--workers=8` was 4/4 green, which isolates it to multi-M rather than GT count. **Fixed by gating every wakeup on `ioYielded`** — the runtime's OWN existing "parked, off-stack" signal, which three other paths already stand on and which `__io_op_done`'s comment already claimed to mirror — plus the `DMB ISH` StoreStore/Dekker barriers the arm64 protocol needs. Now **786/0 exit 0 at `--workers=8`, six consecutive, ONE hash across all seven runs** (baseline: 6/6 crashed). ⚠ **THE SCARIEST PART WAS NOT THE CRASH:** at `--workers=6` it produced **33 SILENTLY WRONG results** — "codegen changed" failures whose actual IR was missing or started at the wrong function — so the suite REPORTED FALSE FAILURES rather than dying. A gate that lies under load is worse than one that stops. ⚠ **v1 has the IDENTICAL hole** (`Arm64MacosGreenThread.maxon:3359-3367`), so this is a divergence from BOTH references, not a port; and the **x86 twin is documented-but-unpatched on purpose** (`X86CodeEmitter.Runtime.cs`): an IOCP completion is CONSUMED when dequeued, so the arm64 trick of declining the wakeup would LOSE it and hang — a correct fix needs a hold-and-re-drive design, and x86 publishes "parked" LATER than arm64 (its `__gt_context_switch` does not set `ioYielded` itself), so a naive gate could read 0 for a genuinely parked GT. What rung 2 delivered, as planned: ⚠ **`isCrossTarget` CONFLATES THREE FACTS — this file's own signature disease, and arm64-linux is the first target to split them.** It simultaneously means *needs an external runner*, *emits no Target IR*, and *isn't the host*; those coincide for wasm alone. Two real bugs follow: [`SpecTestRunner.maxon:462-469`](maxon-shv2/Testing/SpecTestRunner.maxon#L462) makes cross ⇒ skip `--emit-ir` ⇒ **no golden**, but arm64-linux DOES emit Target IR and must be pinned; and [`:226`](maxon-shv2/Testing/SpecTestRunner.maxon#L226) keys the fragment dir to `detectHostTarget()` instead of `effectiveTarget`, so a cross-native run from this host would **write arm64-linux goldens into `fragments/arm64-macos/`, CLOBBERING them**. Fix by splitting the axes, **not** by adding a third special case. Plus an `ElfSectionReader` for the `RequiredData` gate, and `arm64-linux` appended to `static-variables.md`'s 8 markers. **arm64-linux shares ALL lowering with arm64-macos, so their `.test` fragments should be near-identical — which makes it a standing DRIFT DETECTOR on the arm64 path.** ⭐ **x64-linux LANDED 2026-07-21 — the x64 twin of arm64-linux, and the FIRST cross target runnable from THIS Windows host.** shv2 emits a running STATIC x86-64 `ET_EXEC` ELF for `--target=x64-linux`, cross-run under **WSL2** (`wsl --cd <dir> -e ./<exe>`; exit codes propagate; **NO install** — a static raw-syscall ELF runs under the WSL2 kernel with nothing in the distro). ONE new IR op **`x64Syscall(number)`** (encoder `mov eax,num; 0F 05`; args pre-moved to rdi/rsi/rdx/**r10**/r8/r9, result rax; `iatCall`'s clobber mask, guarded by `assertX64CallClobbers`), an **`x64OsCallKind`** door OS-branching `osExit`/`osAllocPages`/`osWriteStdout` to **`exit_group`(231) / `mmap`(9) / `write`(1)** — the deliberate 231-not-60 divergence from v1, mirroring arm64's 94-not-93 — a **FRAMELESS** Linux entry stub (no VEH panic runtime: the scalar core needs none, exactly as arm64-linux appends only its stub; System-V alignment via NO `push rbp` — the kernel enters `_start` at rsp≡0 so `call main` reaches 16-alignment without one), and the ELF writer relaxed to `EM_X86_64` + an x64 RIP-relative reloc patcher (`patchX64SectionRelocations`, the PeWriter twin over absolute VAs). The spec harness gained a **HOST-AWARE** runner: `externalRunnerFor`'s linux arm defers to `linuxRunnerForHost` (WSL from Windows, OrbStack from macOS) and rejects a foreign-CPU linux target upfront (WSL runs only the host CPU — no in-box cross-exec). **x64-linux runs the host's FULL x64-windows set** — originally minus the 2 divide/mod-by-zero tests (safety.md), which are now un-gated too: a **SIGFPE fault runtime landed 2026-07-21 (OPEN #82)** — the Linux twin of the Windows VEH handler, reusing its OS-neutral backtrace chunks over a `write` syscall, registered via rt_sigaction (⚠ x86-64 needs `SA_RESTORER` + an rt_sigreturn trampoline, or the kernel SIGSEGVs building the frame — found by strace). Goldens byte-identical `--workers=1` vs 12. 67 x64-generic markers (first-class-functions / closures / RequiredData / register-pressure / ternary) widened to name x64-linux. **`fragments/x64-linux` is a DRIFT DETECTOR vs `fragments/x64-windows`** — the two differ ONLY at the OS-call sites (syscall vs IAT), the x64 twin of the arm64-linux/arm64-macOS pair. ✅ BOTH FOLLOW-UPS DONE 2026-07-21: the SIGFPE handler (#82, above) and the shared `Targets/Shared/PosixMmap.maxon` home for the duplicated POSIX mmap constants (#83). |

### ⭐ Frontend parallelism — the pre-scan FANS OUT; the barrier is cheap and STAYS

The frontend is a whole-program `queryProgramSignatures` sweep (lex every file, extract its
DECLARATIONS off tokens, fold into one index) sitting UNDER the per-file `queryParseOps` parse. That
makes the sweep a **barrier**: no file's real parse can start until every file's declarations are
known. **This is intrinsic, not incidental.** *"Is `Foo` a type declared ANYWHERE?"* is a whole-program
predicate, and the parse needs the answer to shape control flow — a bool `and`/`or` is short-circuit
blocks + a phi, an int one is a bitwise op, and which it is must be decided BEFORE the right operand is
parsed. **The deferral was never the defect; not looking was** (see ARCHITECTURE.md → Query spine, and
`SignatureIndex.maxon`).

⇒ **Do not try to DELETE the barrier. Parallelize ACROSS it (BSP):**

```
parallel[ per file: lex → extract declarations into local arrays ]   ← the map
serial [ fold contributions into the index, in SOURCE-PATH order ]    ← the reduce (cheap)
parallel[ per file: parse, reusing the tokens the sweep already cached ]  ← the map
```

- **Token reuse is free** — `queryTokens` is memoized; the sweep populating it and the parse reading it
  back is today's behavior, just serial. No double-lex.
- **The extract is already the map half** — `foldDeclaredSignaturesInto` builds per-file LOCAL arrays and
  reads no shared state while walking (a sweep types no call, so it never reads the index it fills). Only
  the fold touches the shared index.
- **The serial reduce is small** — O(declarations) fold + the O(constants) `evaluateInitializers` DFS (the
  one genuinely-sequential piece: forward + cross-file constant refs, cycle-detected → `E2012`).
- ⚠ **TWO GUARDS, or a gate breaks.** (1) The fold must stay **LINEAR** — this exact whole-program-under-
  per-file spot has gone **O(files²) twice** (`clearDepsFor`, `compositeSourceHash`); before adding work
  to it, multiply by the file count. (2) Fold in **PATH ORDER, not completion order**, or the byte-identity
  bootstrap gate dies on a nondeterministic index.

This lands at **P2.6**, folded into the fan-out rung — it is the one upstream phase that rung otherwise
leaves serial.

**BATCH ≠ INCREMENTAL — two independent, composable wins.** Parallelizing the sweep is the **batch** win
(self-host throughput, the >90% CPU number). It does NOT shrink the **incremental** cost: the sweep memo is
keyed on the whole-program composite hash, so a one-file edit still re-sweeps every file (faster now, not
smaller). The orthogonal knob is **per-file sweep memoization** — cache each file's declaration contribution
on its own content hash, re-extract only the edited file, re-fold. Do both and a cold build saturates cores
while a warm rebuild does O(changed file) + O(declarations).

**Removing the barrier ENTIRELY is a MEASUREMENT-GATED alternative, not committed work.** Two designs would
dissolve the pre-scan: **(a) post-parse fixup** — parse with no cross-file info, record ops of unknown type
on a worklist, resolve after all files parse (needs *detached-block* deferral for `and`/`or`, the one
CFG-shape case); **(b) blocking demand-lookup** — each parse is a green-thread task that SUSPENDS on an
unknown type until a peer registers it (handles `and`/`or` inline, but adds a shared mutable registry,
wait-graph cycle/quiescence detection, and a determinism proof over interleavings — against the byte-identity
gate). **Neither is required** — the barrier does not block P2.6's fan-out or the ≤30 s goal. Promote one
only if the `signatures`-phase scale-test bends or a real incremental-latency target appears; prefer **(a)**
for shv2 (deterministic by construction). Until then this note is the record, so the analysis is not
re-derived.

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

### P1.5 residuals — closures, escape, and value-merge follow-ups

The closure + async halves of P1.5 landed their CORE (A1 · A2 · B1a · B2a · B2b), but **P1.5 is NOT
complete** — these front-end follow-ons remain (the runtime/async ones are under Workstream R; #64 the
borrowed-container throw and #65 implicit-self are noted with the P1.4b closed-box / the future-rungs list):

- **✅ #31 + #80 + #81 — the value-merge cluster — CLOSED 2026-07-22** (main `11a4bbce5`, 1079→1082/0, rebased
  onto P1.7). All three fired through the ONE `finalizeMatchMerge`. **#31**: a mixed int/float match/ternary now
  PROMOTES each integer arm to float (`promoteToFloat` in the arm's own exit block; a float-meets-non-numeric arm
  stays a give-type mismatch), matching the runnable bootstrap oracle. **#80**: the diagnostics are construct-aware
  via a `MergeConstruct` enum + ONE shared `valueMergeMismatchError` mapping — a ternary reports E2028 "ternary
  expression …", a match keeps E3005. **#81**: function-value arms must agree in signature (NOMINAL fn-type display
  equality, matching the oracle, which rejects `fn(Integer)` vs `fn(Whole)`); moved WHOLE-PROGRAM (recorded at parse,
  drained in `compileToCodeResult` before `resolveTypes`) so a same-file FORWARD-REF and a cross-file mismatch are
  both caught — a coordinator-probed silent miscompile the first parse-time-file-local pass had missed; an
  unverifiable branch (a function value from a param/field/call-result) is cleanly REJECTED, never silent-accepted.
- **#73 — capturing-closure escape checking is PER-SITE (enumerated sinks), not a derived invariant** —
  nothing forces a NEW value-sink to call `rejectEscapingClosure`. Complete for today's grammar (only the
  interprocedural summary got the DERIVE cure). Replace with ONE invariant: a capturing-closure value may
  appear only as an indirect-call callee OR a frame-local binding. Not a blocker.
- **✅ #78 — indirect calls carry the callee's float result/param types — CLOSED 2026-07-22** (main
  `a1515dfb7`, 1100→1106/0, cross-target). Was WORSE than framed: not a wasm-only trap but a COMPILER CRASH on
  x64 too (a float-returning/float-param function value called indirectly panicked "crosses register files").
  The `callIndirect` StdOp now carries `resultType` + a scalar `argFloatMask`; x64/arm64/wasm route the result
  + each arg by register class through ONE shared `emitArgMovesByFloatMask` (the reviewer collapsed a
  direct/indirect arg-move fork); the `__fnref_` thunk forwards float params in the FP file. Result AND param
  float cases closed, oracle-matched; args cap at `MaxRegisterParams=6` (mask fits u64).
- **#75 — `capturing-closure-name-not-leaked` stays disabled on a borrowed-struct-copy-return gap**
  (`var hh = h; hh.op = op; return hh` → E2015, no struct clone) — NOT a closure bug (its actual property is
  independently verified). Enable when struct-copy-return lands (P1.7-adjacent).
- **#79 — a closure that itself takes a function-typed param gets no companion env** (`parseClosureExpression`
  never calls `bindClosureEnvParams`); and the escape summary's transitive rule may over-reject an exotic
  pass-through-that-only-calls chain (sound). Both exotic, no corpus. Low priority.
- **#74 — payload-binding IMMUTABILITY is a deliberate divergence to CARRY** (shv2 declares union payload
  bindings immutable → E2013, where the bootstrap routes E3099 through a heap alias). Safer — shv2 never
  writes back through a payload binding. Author a spec pinning E2013 only if desired.

### PRINCIPLE — when may we rewrite shv2's own source?

> **Rewriting shv2's source to dodge a mechanism is a SCOPE CUT, not a simplification.**
> Use it *only* for mechanisms deliberately deferred as out-of-scope (async; and wasm's
> POST-SCALAR slices — the wasm **scalar core landed 2026-07-18**, see the Targets row).
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

> **A rung is COMPLETE only when it has NO open residuals.** A documented residual — a bug or gap the rung
> generated and did not close — means the rung is **NOT done**, however green its suite; its status says so
> (**◑**, not ✅). *"Core landed"* is not *"complete."* Residuals live with the rung (or its workstream) and
> are what keep it on this ladder until they close. Accepted debt measured linear-in-practice and a
> deliberate divergence are DECISIONS, not residuals — those do not hold a rung open.

| # | Mechanism | Note |
|---|---|---|
| **P1.0o** | **the compiler traces ITSELF — Workstream O1** ⭐ | **FIRST, because it is the instrument the rest of the ladder is debugged with.** shv2's stderr `Logger` dies the moment P1.0a interleaves N workers into one stream. A `__DebugStream` builtin in **`maxon.exe`** + 4 new event codes + a sink behind `Logger`'s existing API ⇒ binary events into the shared-memory ring, demuxed per-worker by `maxon monitor`. **Depends on NOTHING in Phase 1** — the bootstrap already carries the ring, the reserve, and the monitor *(see Workstream O)* |
| **P1.0a** | **grow the harness's parallel worker pool back** | **The acceptance target must exist before it can be a target.** Port `maxon-selfhosted`'s [`runAllSpecTestsParallel`](maxon-selfhosted/Testing/SpecTestRunner.maxon#L3401) worker pool into `maxon-shv2/Testing/`. Written in Maxon, compiled by **`maxon.exe`**, green under today's gates — so it lands *now*, and every later rung is measured against the real Phase-1 target instead of the serial stub. **Workstream S is what makes it pay:** the corpus takes the suite from 126 tests to thousands |
| **P1.0b** | **Workstream S — the `disabled-test:` marker, and ON-DEMAND porting** ⭐ | *(see Workstream S.)* The marker is SHIPPED (`362b07b72`). **The bulk port is NOT, and will not be** (user directive): spec files are copied from `/specs` **on demand, by the rung that needs them**, not as a corpus dump. A trial bulk sweep was run once, as a MEASUREMENT, and then discarded — see P1.0d, which is what it found |
| **P1.0d** | **complete the SCALAR CORE** ⭐⭐ | **NEW, and it exists because the sweep proved this plan's central claim false.** See "The scalar core is NOT done" below. ✅ **ALL SLICES DONE; P1.0d CLOSED 2026-07-15 — suite 126 → 355/0.** Slice 1 (parens · `true`/`false` · block scoping · void fns · top-level `typealias`) 126 → 159; then `.2` word/bitwise/chars, `.3` divide-by-zero, `.5a` top-level `let`, `.5b` globals, `.4` floats. |
| **P1.0d.2** | `not` / `and` / `or` · bitwise · character literals | ✅ **DONE.** Short-circuit `and`/`or` is control flow, so it landed as blocks + phis on the parser's on-the-fly SSA. Bitwise + chars are new `StdOp`s in the existing integer register class — **APPENDED at the END of a band** (a `match` range arm silently swallows anything inserted mid-band) |
| **P1.0d.3** | **`a / 0` ⇒ a clean panic** | ✅ **DONE 2026-07-15** — suite **279 → 281**. It escaped as a raw `0xC0000094` with **empty stderr**; it is now exit **1** + `panic: integer divide by zero` + a symbolized backtrace, and `specs/safety.md`'s `divide-by-zero` + `mod-by-zero` are ported and ENABLED. **Workstream R's first slice.** Three things this rung settled, each bigger than the divide: (1) **the fault is caught by a VEH thunk, not a divisor check** — shv2 is x64-only and the CPU raises `#DE` for free, so a `cmp`/`branch` before every `idiv` would be the scope cut the PRINCIPLE names; **NO gt redirect** (the reference's fault path rides green threads, which arrive at P1.5) — the thunk prints and exits in place, so the context travels as ordinary arguments and needs no fault globals at all. (2) ⭐ **FRAME POINTERS, on every function, leaves included** — see below. (3) the harness grew a ` ```stderr ` fence: `maxoncstderr` is the COMPILER's stderr, and a program's **RUNTIME** stderr was a thing no spec could pin |
| **P1.0d.5a** | **top-level `let`** ⚠ **the plan never listed this one either** | ✅ **DONE 2026-07-15** — suite **281 → 295**. **Found by probing, not by the ladder** — and it has **its own stable spec nobody had found: `specs/top-level-let.md`, 17 cases**, which the bootstrap passes. *(Missed twice because a grep for `global|static|module|init` does not match the filename — **look for a feature's own spec BY NAME.**)* **A module-scope constant is a NUMBER: it inlines as an ordinary `literal` at each use ⇒ no IR op, no `.data`, no relocation, NO BACKEND AT ALL.** Zero pre-existing goldens moved, which is the proof. Design: an arena of `(name, visibility, file, token range)` + a memoized DFS with cycle detection, driven inside `queryProgramSignatures` — that is what makes **forward references** and cross-file `export let` work. The evaluator **shares `TypeRules`' folds** (one opinion on constant arithmetic; only the climb loop forks, because `and`/`or` are *control flow* in a body and a *value* at file scope). ⚠ **Still `disabled-test:`: `basic-float-constant` (P1.0d.4), `file-private-same-name-cross-file` (P1.9 `as`), `from-literal-initializer` (P1.2 `String`)** |
| **P1.0d.5b** | **top-level `var` (GLOBALS)** ⚠ **another one the plan never listed** | ✅ **DONE 2026-07-15** — suite **295 → 317**. A module-scope global with a **real `.data` slot**. ⭐ **This rung created the Std MEMORY band** (`globalAddr` + `loadIndirect` + `storeIndirect`, v1's shape — NOT a fused `globalLoad`, because P1.1/P1.2 need the general pair) — **which begins retiring R1 @ P1.2's precondition** (see the R1 box: the Std-IR runtime route was blocked on the Std tier producing only `arith`/`call`). Plus `DataSectionEntry` in `GlobalDataTable`, a `.data` section in `PeWriter`, and the `dataSectionRipRelDisp32` arm. **NO `__module_init`** — initializers are constant-only, so they const-evaluate into `.data` **bytes**. ⚠ **`loadIndirect` MUST be `isPure: false`** or a global's read **would hoist out of a loop that writes it** — a silent wrong answer. ⚠ **CORRECTED 2026-07-15: this row said "pinned by a spec". IT IS NOT, AND NO SPEC CAN PIN IT — `StdOpMeta.isPure` has ZERO readers** (per-field sweep + a sabotage: flipped to `isPure: true`, the suite passes 371/0). The property holds because **nothing in the pipeline hoists**; the flag is a correct declaration awaiting DCE/CSE/LICM/the inliner. The spec pins the ANSWER, not the flag. **See OPEN.md #27.** ⭐ **shv2 does NOT inherit the bootstrap's aliasing bug (OPEN.md #17):** identity is per-FILE **by mechanism** (`fileScopedDeclKey(name, readerFilePath)`), label = bare name + `$1` **only on collision** (path-free, so goldens stay stable; `$` is structurally unwritable — `isAlphaNum` is `[A-Za-z0-9_]`). **The corpus had no `var` twin of `file-private-same-name-cross-file`, so we wrote one**: shv2 returns **118**, the bootstrap **212**. ✅ **`short-circuit-elision.md` RETIRED** — its 5 divide-by-zero workaround cases are superseded by the real corpus cases; its 9 genuine *lowering* tests moved to `short-circuit-lowering.md` (goldens are **R100 renames** — proof the codegen did not move). ✅ **`RequiredData` blocks now actually compare** (they were **silently ignored**) |
| **P1.0d.4** | **floats (f64)** | ✅ **DONE 2026-07-15 — suite 317/19 → 355/0, and P1.0d IS CLOSED.** Wave 1 (an all-caller-saved XMM pool; a float across a call force-spills). ⭐ **ONE `X64Register` enum, xmm0-15 at rawValue 16-31 — the low nibble IS the encoding number, so `and 0xF` encodes and bit 4 classifies.** The design that paid for itself: **CLASS-AGNOSTIC OPS, CLASS-DISPATCHED ENCODERS** — a move/spill/reload/swap is a class-agnostic *concept*, so there is no float spill op, no float move, no float `xchg`; the encoder picks `mov` vs `movsd` from `regClassOf`. ⇒ `SsaDestruction` needs **no float case**, the splitter no float spill op, coloring is class-blind, and a spill slot is 8 bytes = an f64. *(v1 mints a SECOND enum and pays a parallel op per emit, threading a bare `regClass` int through each — that tax is absent here.)* **The class travels FORWARD** (`ValueClassColumns`, per FUNCTION — ValueIds are function-local — produced by `StdToX64Conversion`, consumed by the allocator; the Target tier carries no types, so it *cannot* derive one). ⚠ **`operandType` is the SOURCE and the OPCODE names the result** — true only for `siToFp`/`fpToSi`, the dialect's only cross-class ops. `cmp` is ALWAYS `i1` (a float compare's answer is a GPR bool). Float `/` is `StdBinOpcode.div` on `binOp`, **not** `StdOp.div`: `idiv` faults and `divsd` does not, and purity is the band's membership rule. ⭐ **`<`/`<=` SWAP OPERANDS** into the already-NaN-correct `ja`/`jae` family (`ucomisd` sets CF on unordered) — one jump, four of six predicates shape-identical to an integer compare; only `==`/`!=` need parity, **as a second BLOCK**, so `IrBlock.CondBranch`'s one-branch-per-block invariant is never bent and v1's phi-copy miscompile is **unrepresentable, not fixed** (`float-compare-branch.md`, 11/11). **`E3009`, not a new code**, for narrowing — see the ⚠ row below |
| | ⚠ **What P1.0d.4 cost, and it is the ledger** | **The contract was WRONG TEN TIMES**, and every one shares a shape: **TYPE surfaces specified without OPERATIONS** — `operandType` with no ops consuming it, a register enum with no instructions, a float type with no conversions (**`trunc` had 0 hits in shv2**). **Nine were ONE FACT WRITTEN DOWN TWICE**, the file's own through-line: `classPoolSize` returned the FILE's width (16) where the POOL's (14) was meant *with the correct number in the comment directly above it*; `globalStdType` kept a second tag→StdType table that had diverged (no `float` arm); `notOperatorFor`'s range arm swallowed `float` **while its header boasted the `match` had FIXED that exact fall-through**; `floatCondCodeForPred` + `floatCmpSwapsOperands` had to agree with nothing making them; `valueConfinedTo` and `analyzePressure` gave opposite answers about a ∅-allowed value; the bootstrap **disagreed with itself** (`CoerceValueToExpectedKind` rejected float→int, `ConvertArgToParamType` truncated it) — **and the fix for THAT re-spelled the integer family 19 lines from its home**, which the review caught. ⭐⭐ **`Parser.mintPhi` hardcoded `argType: i64`, and its comment NAMED THIS RUNG**: *"the scaffold the float (XMM) register class will need at P1.0d.4, and that is when it acquires a consumer and a reason to be right."* **The rung gave it the consumer and not the reason** ⇒ `var f = 0.0` in a loop — the commonest float idiom there is — **crashed the emitter**, and the authored spec could not see it (its phis carry INTs). **Only extending the instrument found it.** ⇒ **READ THE COMMENT THAT NAMES YOUR RUNG** |
| | ⚠ **Deferred out of P1.0d.4, named not hidden** | ✅ **Float NAMEABLE (F1) LANDED 2026-07-19** (`fda8f73bb`, 756→761): `typealias F = float(low to high)` now parses + resolves to `MaxonType.float`, enabling the 5 `ranged-typealias.md` float-LOCAL tests (float-range, f32-range/arithmetic/comparison/to-int — float stays local via the no-op `as`-cast, `trunc`→int, no ABI). ✅✅ **THE FLOAT FEATURE IS COMPLETE 2026-07-19 ("do BOTH": F1 nameable → F2a return → F2b args).** F2a (float RETURN #50, `f8c516e33`): float-alias return types resolve to float + XMM0/d0 return codegen → `float-return-from-function` GREEN. F2b (float ARGS #57, `c428191ac`, 762→763): float param/arg in XMM/d-reg by a SEPARATE int/FP counter (v1's convention) + `buildCalleeParamClasses` + `emitCallArgMoves` → `f32-function-param-return` GREEN. ⭐ F2b review caught a real ARM64 compiler crash (shared liveness decoded float-arg regs through an x64-only door — the signature bug) — fixed x64-byte-identical. ✅ **The float NUMBER-TYPE story landed 2026-07-19**: F1 nameable → F2a return (#50) → F2b args (#57) → F3a int→float widening (`cc698eaf9`) → F3b throwing-float return (#60, `0b113576a`), suite 767/0 — nameable typealias, return, args, int→float coercion, and the throwing-return throw edge, on x64 + arm64 (cross-host golden-synced). ◑ **But floats are NOT COMPLETE — three follow-ups remain (below): `f64→f32` #19, `floor`/`ceil`/`round`, and float const-folding.** — **Wave 2 = the float ABI** (a float PARAM/RETURN needs the xmm arg slots; **measured** — `takeFloat(42.0)`, a literal with no coercion, panics in the callee's ABI reception). Win64 preserves all **128 bits** of xmm6-15 and `movsd` ZEROES 64-127, so callee-saved XMMs drag in 16-byte slots + leaf misalignment together — that is why Wave 1 is all-caller-saved. Also: **`floor`/`ceil`/`round`** (`roundsd` is SSE4.1, a three-byte escape) ⇒ their own rung; **float const-FOLDING** is rejected (E2015) — it needs software IEEE-754 in the compiler, and v1 does not do it either; **`0.0 - x` ≠ true negation on `-(+0.0)`** — unobservable until `print` (P1.2), and v1 has shipped it for its whole life; **`f64→f32` still coerces implicitly (#19)** — the same lossy-narrowing bug wearing different types, outside the float→int ruling, **unresolved** (its own lossy-narrowing rung — spec + both compilers); **`trunc` is reserved** wherever a call is parsed, silently shadowing a user function (v1 has the identical property for its whole intrinsic set) |
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
| **P1.0r** | **R1-core — the ALLOCATOR + refcounting runtime** ⭐⭐ | ✅ **DONE 2026-07-15** — suite **355 → 357/0**; see the closed box at the top of this file. *(Was "R1 @ P1.2". Promoted ahead of structs 2026-07-15 — see the RESTRUCTURED box: a struct is a heap value, so the heap cannot come second.)* The slab allocator · `__mm_alloc` · `__mm_incref`/`__mm_decref` · the `__destruct_*` cascade. **The allocator ALWAYS RETURNS ZEROED MEMORY, from commit 1** — a property of the allocator, not of each caller (it cost v1 three separately root-caused bugs; see ARCHITECTURE.md → "Allocator: the zeroing contract"). ⚠ **THIS RUNG MUST DECIDE THE RUNTIME'S *FORM*** — hand-assembled machine code vs. v1's `runtime.std` (6,049 lines of Std-IR text through the ordinary backend). **The excuse for deferring it has now EXPIRED: its precondition was the Std memory band, and P1.0d.5b shipped it.** See the R1 box in Workstream R — **the named risk is inertia**, 6 hand-assembled functions becoming 5,000 by default. **Write the reason down.** ⚠ Its acceptance test is **P1.1a**, not a bespoke spec: an allocator with no heap value to allocate is untestable, so the two are adjacent on purpose |
| **P1.1a** | **structs** — R1's DOGFOOD ⭐ | **✅ CLOSED 2026-07-16.** **WAVE 1** (with P1.0r: `type` · layout · `Self{}` · `static create` · field READS — 355→357). **WAVE 2** (field WRITES · mutability · defaults — 357→**371**). **WAVE 3** (INSTANCE METHODS · `self` · field VISIBILITY E3014 · E3011-at-fields — 371→**399**; see the closed box at the top). The receiver question resolved: it does **NOT** force P1.4's borrow-vs-consume ruling — both references AGREE the receiver borrows (the deferral's "the references disagree" reason does not hold for it), the corpus requires it, and the consume sink is unreachable at this rung (a struct-typed field is uninstantiable). *(was the struct half of `P1.1`.)* **trivial-ownership only** — scalar/float fields, destructor NULL, no field write increfs. Heap-boxed via `__mm_alloc`, **uniform 8-byte field slots** (`sizeof(Point)`=16, `sizeof(Outer{p Point, n Integer})`=**16** — a struct field is a POINTER). Field access = `loadIndirect`/`storeIndirect`. Methods = ordinary direct call, `__self` at param 0. `Self{…}` construction restricted to the type's own methods (**E3076**) |
| **P1.1b** | **enums + `match`** | **◑ CORE LANDED 2026-07-16 — NOT COMPLETE: match/enum residuals #33 (negative float-backed enum), #34 (union wording), #58 (bootstrap match null-ref) remain open** (#31 cross-class match-expr promotion ✅ CLOSED 2026-07-22 with the value-merge cluster — see P1.5 residuals) (sliced into two waves). **WAVE A** — `match` on SCALARS (stmt + expr, value/or/range/default, block-arg `gives` join; 399→462). **WAVE B** — payload-free `enum` + `union` + enum match + the OPEN #21 ordinal range arms (462→**508**); see the closed boxes at top. **NO new IR op** in either — a match is a `condBranch` chain with a block-arg `gives` join (NOT a result slot, the shv2 thesis); an enum is a parallel-column `EnumLayout` + tag constants, `Color.red` ⇒ its tag. **The OPEN #21 range rule** (OR of covered cases by declaration index, two-compare range only where no uncovered tag intrudes) is from the bootstrap `645a690cc` — v1's range code was the unfixed bug — and verified against the oracle. `union` = enum. *(was the enum/match half of `P1.1`.)* |
| **P1.2** | **`String` + ownership + drops** ⭐ | **THE CRUX — SLICED into 4 waves 2026-07-16..17** (survey: too big for one; seam = the heap-alloc / leak-gate boundary). **◑ CORE LANDED (Waves A–D), `specs-shv2` 508 → 611/0 — NOT COMPLETE: two bump-masked UAF residuals (#40, #43) must be fixed before the recycling free list turns them into wrong answers.** A: string LITERALS (immortal rdata, `capacity=-2`, NO alloc) + `print` (→`__print_string`, a `WriteFile` runtime fn via `iatCall`) + `==`/`!=` (→`__str_eq`); **508→523**. B: primitive INTERPOLATION → the first OWNED heap String (ONE `__mm_alloc`'d fused record, bytes INLINE at `record+48`, `parent=-3`) + int/bool/expr converters + STATEMENT-scoped temp-drop (per loop iteration) + a per-value `valueOwnsHeap` provenance column; **523→564**. Then the DROP MODEL was hardened (return-ownership + nested-block scope-exit drops + drop-on-reassign, `564→587`, user: "leaks are not ok"). C: static single-owner **MOVES + use-after-move** (`let u=t`/`s=t` MOVE, source poisoned, read = **E3102**, reassign REVIVES, conditional poison conservative), **587→597** — shv2's FIRST ownership program-rejection; fixes `s=t` double-free; then extended to field/method/store sites (OPEN #42, **597→602**). D: **`String.append` grows to an EXTERNAL buffer** (`__str_append` detach: `__mm_alloc` raw bytes, free old root, blit) + a String's OWN drop (`emitDecref` tag-routed → `__str_decref` frees the external buffer) + `byteLength()` + `E3019` on `.append` of a `let`; **602→611**. ⚠ **Wave D deviations, both ACCEPTED:** `__str_decref` STATIC dispatch (the dynamic `funcAddr`/`callIndirect` `__destruct_String` cascade is forced only by a struct/array FIELD holding a String ⇒ **P1.4**), and eager `var`-String heap promotion (a `var` on borrowed rdata is read-only). **⇒ REMAINING P1.2 owned-value follow-ups (both bump-allocator-MASKED UAFs a recycling `__mm_free` turns real, each its own rung): OPEN #40 (nested borrowed→owned depth-model) · OPEN #43 (`s.append(s)` self-append).** **A `String` IS its `__ManagedMemory`** — ONE fused 48B record (`buffer`@0, `length`@8, `capacity`@16, `element_size`@24, `parent_ptr`@32, `isAsciiFlag`@40), NOT a 16B envelope (⚠ **v1 NEVER fused it — v1 is the envelope form, TWO allocs; the BOOTSTRAP is the layout reference, v1 only for the `parent_ptr` sentinels**). Owned bytes INSIDE at `record+48`, `parent_ptr=-3`; growth DETACHES; `capacity=-2` rdata sentinel; synthesized `__destruct_String`. **Rides P1.0r's `__mm_alloc`.** mm-trace gates from Wave B. **⭐ OWNERSHIP MODEL = STATIC single-owner (drop at scope exit, moves transfer, no runtime refcount) — shv2's thesis, NOT the refcounting BOTH references use.** The full borrow-vs-consume ruling (String as a user-fn PARAM) stays **P1.4**, exactly as P1.1a deferred `struct-param`. `toByteArray()` is a MOVE (needs Array — P1.7); its COW-view specs become use-after-move errors, not ports. ⚠ v1's two-result-capture miscompile (`sequentializeCallResultCapture`) is a **P1.4** precondition (it bites `try`/`otherwise`), not P1.2 |
| **P1.3** | **owned payloads in enums/unions** | *moved into Phase 1* — `compilerError(text String)`, `fail(reason String)`. **SLICED: scalar first, managed next.** **✅ SLICE 1 CLOSED 2026-07-17 (611→617):** SCALAR-payload unions are heap-boxed (`8+maxArity*8`, tag@0, slot`i`@`8+i*8`, `emitEnumBox`, trivial drop) + E3066 (union `==`/`!=`/`<`… = box-address compare, refused) + boxed-union RETURN rejected (E2015, interim; leak else — OPEN #44). ⚠ **The corpus unlock was over-counted — owned payloads alone unlock ~1 case; the payload-union corpus is gated on COMPANION features** (`.unionCases` + `.name`/`.rawValue` accessors, `for-in`, union-PARAM P1.4), a separate rung. **✅ SLICE 2 CLOSED 2026-07-18 (617→641):** MANAGED (String+struct) payloads — move-in (E3102 on source reuse) / move-out (box slot nulled, scrutinee `partiallyMoved`) / tag-conditional static-cascade drop (`__destruct_<U>` → `__str_decref`/`__mm_decref`, null-guarded). A corrective rung (`fa685a7a1`) also fixed TWO pre-existing reachable match-flow bugs the review found: a temp-scrutinee leak (101) and an owned-`gives` double-free (0xC0000005). ⚠ Residual OPEN #46 (bootstrap fixpoint bypass); **#45 (give-type-consistency) ✅ CLOSED 2026-07-22** — the value-merge cluster's `checkGiveTypes` catches a String-vs-int give mismatch (E3005 match / E2028 ternary), pinned by existing match-expr-divergent-class + ternary-expression tests (recon-verified, no segfault). **⇒ P1.3 owned-local payloads DONE (scalar+managed); cross-call param/return = P1.4 (#44); the companion/accessor rung unlocks the bulk of the corpus.** Errors (P1.4) want it too: the harness calls `e.displayReason()` |
| **P1.4** | moves + borrows (NLL) · **errors** | first program-rejection point. **SLICED: P1.4a cross-call OWNERSHIP + P1.4b ERRORS (separate cleanly).** **RULING 2026-07-18 (user):** params BORROW-by-default, consume-by-use; returns ADOPT. **✅ P1.4a WAVE 1 CLOSED 2026-07-18 (641→647):** BORROW struct/union params + ADOPT struct/union returns (parse-time `named`→`structRef` re-tag; ownership unified onto the `valueOwnsHeap` BIT not the tag; `mintOwnedCallResult` adopts struct/boxed-union; **OPEN #44 closed**; closed a pre-existing managed-struct-field UAF via E2015). **✅ P1.4a WAVE 2 CLOSED 2026-07-18 (647→670):** CONSUME-into-field (struct-with-managed-field + `__destruct_<Struct>` cascade + a direct-sink param-consume analysis; transitive fixpoint E2015-deferred) **+ a PATH-SENSITIVE move model** — the review found a PRE-EXISTING conditional-move leak (`movedFrom` was monotonic → a value moved on one branch leaked on the not-moved path); user ruled the real fix, so drops are now reconciled at every join (drop on the LIVE edges, no runtime flags). Conditional moves accepted; outer-move-in-a-loop rejected (E2015, the back-edge double-free boundary). **⇒ P1.4a DONE; REMAINING P1.4 = P1.4b = errors**: `throws`/`try`/`otherwise` (v1's dual-register `(value, errorFlag)`, verbatim; needs `sequentializeCallResultCapture` for the 2-result capture) + drops on the error edge. **~201 corpus + 36 harness sites** (errors dominate). ⚠ OPEN #48 (`.spec-tmp` race), loop-exit-reconcile follow-up; **#47 (field-default reject leak) ✅ CLOSED 2026-07-22** — the P1.4b/P1.5 drop-on-error-edge work already freed those allocs (no live leak; the always-on `__mm_leak_check` proves the E2015-reject path exits 1 not 101), now pinned by a committed field-default-reject leak-guard test (`035eefdaa`). **#96 (out-of-range ranged field default compiles silently) → RE-SCOPED to P1.9** — a field-default range-check gap the BOOTSTRAP SHARES (it runtime-range-checks ARGS — `takeSmall(500)` panics "Range check failed" — but silently accepts a field default `= 500`); range checks are P1.9 (`InsertRangeChecks`/`ExpandCastRangeChecks`), so the field-default constant check lands there, NOT as a fix-now (recon-confirmed 2026-07-22). |
| **P1.5** | **closures + `async` + escape → `shared`** ⭐⭐ | **SLICED 5 ways (A1→A2→B1→B2→C, see the closed boxes at top) — P1.5 is IN PROGRESS and NOT COMPLETE (async residuals #87/#88/#89/#92/#93 → Workstream R; front-end residuals #73/#74/#75/#79 → "P1.5 residuals" — the #31/#80/#81 value-merge cluster + #78 (float indirect calls) ✅ CLOSED 2026-07-22 (main `a1515dfb7`); #64/#65 open, #68 statement-position calls ✅ CLOSED). ◑ A1 (non-capturing function VALUES) core landed 2026-07-20, 792→813/0 on x64+wasm** (NOT complete: arm64 un-restriction #67 remains; statement-position calls #68 + wasm/x64 non-i64 return #78 CLOSED). **THE THREE ARE ONE MECHANISM, AND THEY CO-LAND — this is the plan's "do the hard things early" in its purest form.** Capture-into-heap **IS** escape: a closure captures into an env block; a green thread captures into a task frame. Escape analysis is needed for heap correctness regardless — so build all three together and `EscapeAnalysis` gets **both** capture channels *from birth*. Land escape single-threaded and add `async` later and you bolt a **second capture channel** onto it: v1's `sys.dropTypeParam` split-brain mistake, exactly. Minimal closure = int capture, 0-arg, heap env, uniform `(args, env)` ABI (v1 lifts at parse time). Minimal `async` = `async`/`await` + Promise + the worker pool's needs. Escape is the **only** place refcounts appear. **Track `% values promoted to shared`** — if it's 40%, static ownership bought nothing. **Runtime slice R3 lands here** (the GT scheduler + async subprocess stdio) |
| **P1.6** ✅ **COMPLETE** | **generics + layout descriptors** ⭐ | declarations + instantiation. **SLICED A/B1/B2/C — ALL CLOSED.** **✅ A** (957→966/0) TYPES over trivial args, front-end. **✅ B1** (→989/0) `sizeof(T)` via a live descriptor READ + dictionary-passing. **✅ B2** (→998/0) MANAGED type args OWNED + dropped-once via STATIC per-instance destructors + a managed-only call-site consume (drop is STATIC-DISPATCH `decrefCalleeFor`, NOT a header ptr, so **NO `funcAbs64InRdata`/`destroyFunc@40`** — that reloc path is DEFERRED to opaque-`T` runtime drop → **P1.7 `Array`**). **✅ C** 2026-07-22 (→1011/0 host, 947/0 wasm) PER-INSTANCE ranged typealiases (nominal per instantiation, front-end only). ⏭ #94 (generic-instance union payload can't be match-bound — beyond-scope, P1.7-adjacent) is in "Future rungs". |
| **P1.7** ◑ | **`Array`** | = P1.6 ∘ P1.2 — the first real integration proof (managed elements → element-destroy through the descriptor). ⇒ unlocks **`b"…"` byte-string literals**. **SLICED A/B/C/D — ◑ IN PROGRESS.** ✅ **Slice 1** (scalar core, →1079/0, `94b4f667c`) + ✅ **Slice 2** (byte-string, →1100/0, `82e8f35a9`) + ✅ **Slice 3a** (CONCRETE managed elements — element-destroy via `funcAddr`, no reloc; →1140/0, `15ace5d01`) CLOSED — see the closed boxes at top. Remaining: **Slice 3b** opaque-`T` managed elements (THE reloc — `funcAbs64InRdata` + descriptor `destroyFunc@40` for `Array with T` in a generic body); **Slice 4** slice/append/clone. Residuals (named in the boxes): ⭐ borrow-on-get `try…otherwise <owned>` bump-masked UAF (a THESIS decision — incref-on-get vs move-only); module-level/top-level managed constants; nested-union/`.rawValue`-field-chain/enum-match/>6-param gaps; E3070 borrow-liveness; element_size 8-vs-1 unification |
| **P1.7a** | **interfaces + witness tables** ⭐ **(promoted from P2.1, 2026-07-13)** | Static conformance (`Hashable`, `Equatable`, `Stringable`). **No existentials** — shv2 stores nothing at interface type. **Forced into Phase 1 by MEASUREMENT (P1.0c):** `Set`'s element constraint needs `Character.hash`/`.equals` dispatched, and `Main.maxon:233-236` interpolates a bare `FilePath` **struct** through `Stringable`. Under dictionary-passing there is no route to `element.hash()` on a type parameter *except* a witness slot. ⇒ promotes the stdlib's interface decls DECLARE→EMIT, and unlocks `"{userStruct}"` |
| **P1.7b** | **`Set` + `Hashable`/`Equatable`** ⭐ **(promoted from P2.3, 2026-07-13)** | **Forced into Phase 1 by MEASUREMENT (P1.0c), and by nothing the harness wrote:** `String.trim()` (13 sites) → `CharacterSet.whitespacesAndNewlines()` → `typealias CharSet = Set with Character` ([CharacterSet.maxon:19](stdlib/CharacterSet.maxon#L19)). *"`Set` rides `Map`'s exact mechanism"* is **false as sequencing** — **`Set` is reached and `Map` is NOT.** The `stdlib-shv2/` fork could have cut the `trim()`→`CharacterSet` edge; **REJECTED — do the hard things early.** `Map` stays in Phase 2 (multi-param generics; genuinely unreached) |
| **P1.8** | `String` methods · `for-in` | real `String.equals` body (struct-`cmp` → `methodCall`); hardcoded `for-in` over Array/Range/String. ⚠ **`trim()` lands here and it is the thing that dragged `Set` in** — so P1.7a/P1.7b must precede it |
| **P1.9** | **ranged typealiases** | ◑ **CORE LANDED 2026-07-21 (`cb7107e8c`) — NOT COMPLETE: arm64-macos/arm64-linux goldens (#86) are owed (a capable host)** — x64-windows **887→917/0**, wasm **902/0**. Real `as` casts (retag + int↔float convert; `as` binds looser than unary `-`, so `-5 as Positive` is `(-5) as Positive`) + **`InsertRangeChecks`**: **ONE Std-tier guard emitter** for BOTH cast sites and ranged `return`s — v1's TWO-tier split (Maxon cast pass + Std return pass, which had already DRIFTED on unsigned handling) deliberately **not** ported; the bootstrap's single-emitter shape, as a resolved pass. Compile-time **E3005** for an out-of-range literal; a non-literal into a non-full range emits a runtime guard that **panics** (`mrt_panic` + symbolized backtrace on x64-windows; a bare `exit_group`/exit-1 on arm64/wasm/x64-linux, which have no panic runtime yet). Full-range aliases (`int(0 to u64.max)`, `Byte`, `i64.min..i64.max`) elide to nothing; signed-cascade compares (shv2's `StdCmpPred` has no unsigned compare) give the bootstrap's verdict for every enabled range. **30 disabled cases unlocked** across `ranged-typealias`/`contextual-literal-typing`/`short-circuit-evaluation`/`export-keyword`/`static-variables`/`top-level-let`; the two runtime-panic cases are `targets: x64-windows` (only Windows prints the message). ⚠ **The cast-guard is anchored at the cast's BLOCK, not the value's definition** — found by an integration bug when this rebased over the ternary feature: a cast in an *unselected* `if/else` arm was firing its guard unconditionally (`guard-actually-guards` panicked instead of returning 12). ✅ **FOLLOW-UPS #84/#85 FIXED (`8d6158c62`, 922/0):** the `int(N>0 to u64.max)` mis-reject (now unsigned-correct via a signed AND-cascade `value < 0 → in-range, else value < low → panic` — a **deliberate divergence from the bootstrap's documented signed-runtime limitation**, which panics on a bit-63-set in-range value while its own literal check reads unsigned; shv2 is self-consistent) and the top-level-`let` E3005 gap. ⚠ REMAINING (OPEN #86): arm64 + x64-linux goldens for this rung's cases await a capable host (WSL distro / Mac). Minor: the E3005 message shows `int(N to -1)` for a `u64.max` upper (pre-existing signed-display convention). |
| 🚩 | **PHASE 1 GATE** | below |

> ### ⭐ P1.2 — two decisions taken elsewhere that land HERE (2026-07-15)
>
> **1. `String.toByteArray()` is a MOVE, and the source is not mutable afterwards** (user decision).
> This is an OWNERSHIP statement, which is why it belongs to this rung and not to the bootstrap's
> refcounting. It **dissolves** a problem rather than managing it: with one owner there is no second
> observer to keep consistent, so there is no aliasing question, **no copy-on-write**, and no
> "independent value" contract to enforce.
>
> The bootstrap cannot do this — it has no ownership — so it takes the fallback: `toByteArray()` returns
> an **independent** ByteArray, implemented as a COW view (`185351d1f`). **That is a stopgap for a
> compiler without moves, not a design to port.** Two of its specs
> (`specs/string-type-2` / `tobytearray-is-independent-of-an-owned-source`,
> `tobytearray-survives-the-source-growing`) **use the string AFTER `toByteArray()` and must become
> use-after-move ERRORS here.** Delete them at this rung with that reason, do not make them pass.
>
> The bootstrap's COW is also **one-directional** — a write through the source's raw `managed` shows
> through a view that has not detached yet (measured). Moves make that unrepresentable. **Do not port
> the COW view, and do not port the hole.**
>
> **2. ⚠ v1's two-result-capture MISCOMPILE is waiting for THIS allocator.** shv2's register allocator is
> a deliberately different **linear SSA-chordal** design — i.e. v1's shape, where virtuals are colored
> independently and call-result **capture movs are real**. v1 paid for this bug already
> ([CopyResolution.maxon:377-395](maxon-selfhosted/Compiler/Targets/Shared/CopyResolution.maxon#L377-L395)):
> the colorer can legitimately assign two result dests into a **SWAP** or **CHAIN**, which naive
> sequential movs cannot realize — `mov x1, x0; mov x0, x1` collapsed value+flag into one register, so a
> bounds check read the element value instead of the OOB flag and **panicked on in-bounds data** (the
> array-sort `try get(j) otherwise` miscompile). The fix is `sequentializeCallResultCapture`: a
> **parallel-copy sequencer with per-class cycle-break scratch**, never sequential movs.
>
> The **bootstrap is immune and its code is therefore NOT the model** — its `Assign(reg, value)` merely
> pins a value into a register map and emits no capture mov at all, so it has nothing to sequence.
> **Port v1's lesson here, not the bootstrap's shortcut.**

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

### Future rungs — corpus-surfaced features that each need their own number

Distinct front-end / IR features the corpus surfaced that do NOT ride an existing rung; each gets a ladder
number when it is sequenced (each also has a `disabled-test:` or oracle-divergence pinning it):

- **#26 — function overloading.** `commitFuncSignatures` keys duplicate-function detection by NAME alone
  → E3006, so two same-name arities (`Counter.create` arity 0 + 2) collide; no `overload` resolution exists.
  Blocks `structs.md` `struct-field-default`, and the stdlib's overload sets (§487/§817) lean on it. Function
  resolution, orthogonal to structs.
- **#65 — implicit-self method resolution.** A bare `bump()` inside an instance method stays a free call →
  E3004 (bare self-FIELD access works). Needs `<EnclosingType>.<name>` resolution with static-vs-instance
  receiver handling. Keeps `propagate-throw-through-local-struct` disabled (with #64).
- **#77 — string-backed enum raw values** (`enum … implements Error { case = "text" }` → E2015). Needs
  String's `.rawValue`/`.name` accessors (P1.2); may unblock other corpus.
- **#33 — negative float-backed enum raw values** (`enum T { a = -1.0 }` → a MISLEADING E2010; positive
  floats parse, the bootstrap accepts). Support them, or emit a message naming the real gap.
- **✅ #68 — statement-position function-value calls — CLOSED 2026-07-22** (main `191e4175c`, 1106→1109/0).
  `callStmt` now shares `parsePrimary`'s `calleeIsFunctionTypedLocal` diversion (ONE predicate, both
  positions), so `let cb = doIt; cb()` and a discarded `f(21)` route to the indirect path (oracle-matched);
  an undefined name still E3004. The void case earned its keep: it exposed + FIXED two latent void panics
  (`appendCallIndirect`/`stdTypeOf` on a void result; `synthesizeFnRefThunk` on a void target — its comment
  cited "OPEN #68"), and the review caught a void-function-value-in-VALUE-position compiler panic (now a
  clean E2004 at the call, `resultUsed` threaded). **NEW #97: a function-typed FIELD or call-RESULT callee
  at statement position (`obj.handler()`, `getFn()()`) is E2015 "Unsupported"** — pre-existing, bare-local
  only; the statement dispatcher would need field-load-then-indirect-call. Its own rung.
- **#4b — the constant top-level-decl DFS is UNBOUNDED** (native-stack recursion → SIGSEGV at ~700 deep, no
  diagnostic, defeats E2012; inherited — the bootstrap overflows earlier). Fix = an explicit WORKLIST in BOTH
  compilers (NOT the dependency-graph pre-scan — that would be a third evaluator). *(Its other two sub-bullets
  are closed: duplicate top-level decl → E3006, `A=5` on a top-level `let` → E2013.)*
- **#27 — five `StdOpMeta` fields have ZERO readers** (`role`, `isMemory`, `isStore`, `isCmp`,
  `isUnsupportedInInlineBody`; `isPure` is kept as sole home, reader scheduled). They are held by REVIEW alone
  — a wrong one becomes a silent wrong answer the day a hoisting/scheduling pass lands. A survey with a
  decision per field (`isMemory`/`isStore` = scheduler barriers, `isCmp` = compare/branch pairing).
- **#94 — a union payload typed as a GENERIC-INSTANCE alias cannot be MATCH-BOUND** (P1.6-B2 found).
  `union Maybe some(b StrBox)` where `StrBox = Box with String` constructs and drops leak-free, but
  `some(b) then b.value` types `b` as its i64 storage → E2015 "field access on 'b', which is declared
  'int'". A CLEAN compile error, not a soundness hole, and beyond B2's surface (managed type ARGUMENTS, not
  unions carrying instances). Fix = a match-binding RETYPE of a generic-instance-aliased payload to its
  substituted concrete type — the same retype B2 added for field reads, applied at the match-bind site.
  Combines with P1.7 union-of-`Array` patterns.

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

**= Phase 1, plus exactly what shv2's own 44,671-line source adds.** Bounded by measurement
against that source:

| # | Mechanism | Forced by |
|---|---|---|
| ~~**P2.1**~~ | ~~interfaces + witness tables~~ | ⬆ **PROMOTED TO PHASE 1 (P1.7a)** — P1.0c measured `Stringable` and `Hashable` dispatch inside the harness's own cone |
| **P2.2** | **conditional conformance** | per-gid witness tables + synthesized thunks — one witness blob per `GenericInstanceId` (`Array<Byte>: Equatable`, …), each method slot pointing at a thunk with the interface-declared signature whose body forwards to the shared generic impl, materializing that instance's implicit layout/witness args (v1: `synthesizeWitnessThunks`). **The last big generics piece.** ⇒ **[GlobalDataTable.maxon:23](maxon-shv2/Compiler/Targets/Shared/GlobalDataTable.maxon#L23)** `Map with (ByteArray, String)` compiles — **its acceptance test, kept deliberately** |
| **P2.3** | **`Map`** *(`Set` ⬆ PROMOTED to P1.7b)* | multi-param generics. **`Map` is genuinely unreached by the harness — P1.0c proved it with the machine code** (its `EnvMap` arms compile to a bare `mm_decref`), so reachability-seeded lowering holds and it stays here. 12 `Map` typealiases in shv2's own source. ⚠ **`Set` does NOT wait for it** — it was reached in Phase 1 by `String.trim()`, which is why *"`Set` rides `Map`'s exact mechanism"* was false as sequencing |
| **P2.4** | **`extension`** | promotes the stdlib's extension blocks DECLARE→EMIT |
| **P2.5** | **closure dogfood** | shv2's `LazyMessage` sites ([Logger.maxon:35](maxon-shv2/Compiler/Logger.maxon#L35)) compile — the acceptance test for P1.5 |
| **P2.6** | **per-function fan-out + parallel frontend** | the one carry-over from the scalar core (M5's original scope, never built). Both seams exist (`PassPipeline.classifyPass`; the parser is already a pure function of its file) — and **the runtime under it now exists, because P1.5 brought R3 forward**. ⭐ **This rung ALSO parallelizes the frontend — the one upstream phase the fan-out otherwise leaves SERIAL.** The `queryProgramSignatures` sweep (lex + declaration extract) is today a serial `for path in sourcePaths` loop under the per-file parse; make it BSP — `parallel[lex + extract into local arrays]` → `serial[fold in SOURCE-PATH order]` → `parallel[parse, reusing cached tokens]`. The extract already builds per-file local arrays and reads no shared state, so only the fold is serial (O(declarations) + the cheap O(constants) `evaluateInitializers` DFS). **Guards:** the fold stays LINEAR (this spot has gone O(files²) twice) and folds in PATH order, not completion order, or byte-identity dies. **See ⭐ Frontend parallelism (Locked decisions).** Batch win only — the incremental re-sweep-all cost is a separate knob (per-file sweep memoization). Gate: **1-core-vs-N-core byte identity** |
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

> ### ✅ DECIDED 2026-07-15 @ P1.0r — **BUILDER-BUILT `StdModule` THROUGH THE ORDINARY BACKEND.** Hand-assembly is reserved for raw-ABI escapes.
>
> **The box below framed this as TWO choices. There are THREE, and the one v1 actually uses for THIS
> code is the one the box never names:**
>
> | | route | v1's use | size, allocator+refcount |
> |---|---|---|---|
> | **(a)** | **hand-assembled bytes** | the VEH thunk + ~93 x64 fns (GT/IOCP/subprocess) | **~3,900 lines of C#** (bootstrap) |
> | **(b)** | **Std-IR TEXT** → `StdParser` → ordinary backend | `runtime.std`, 6,049 lines — panic/backtrace/glue **only** | — |
> | **(c)** | **plain Maxon source** over a bounded intrinsic surface | ⭐ **`stdlib/Internals.maxon` — where the slab + refcount ACTUALLY live** | **~2,730 lines of Maxon** |
>
> ⭐⭐ **THE DECISIVE FACT: v1 MIGRATED the allocator and `__mm_incref`/`__mm_decref` OUT of (b) INTO (c),
> and left its reasons in the source.** `runtime.std`'s `mm_incref` is now a **3-op delegator** (`call
> __mm_incref`) whose comment reads *"single source of truth… lives in stdlib/Internals.maxon"*.
> - [Internals.maxon:19](stdlib/Internals.maxon#L19) — *"Migrated from runtime.std's mm_incref **so the
>   inliner can see this 2-op body at every call site**."*
> - [Internals.maxon:1-7](stdlib/Internals.maxon#L1) — they live in stdlib because they *"participate in
>   the full optimization pipeline (canonicalize, cse, licm, dce, Stage A const hoist, inliner) — **that's
>   why they live in stdlib rather than runtime.std (which only gets mem2reg)**."*
> - [runtime.std:4102](maxon-selfhosted/Compiler/Runtime/runtime.std#L4102) — *"an UNCONDITIONAL call
>   because **runtime.std has no conditional compilation**"* ⇒ `--rc-sanitize`/`--leak-report` pay a call
>   on the DISABLED path forever.
>
> ⇒ **(b) is a route v1 TRIED for exactly this code and ABANDONED.** Adopting it would re-walk a path whose
> exit is documented. **And (c) is already LOCKED OUT** by §Context's runtime-binding decision (*"exclude
> `Internals.maxon`, emit natively"*). So the choice is **(a) vs (b)** — with (b)'s one fatal flaw, no
> inlining, **applying EQUALLY to (a): hand-assembled bytes can never be inlined either.** The thing that
> killed `runtime.std` for v1 **cannot decide between the two candidates shv2 actually has.**
>
> **⇒ Decide on what DOES differ — and shv2 gets a fourth option v1 never had:**
>
> **(b′) BUILD THE `StdModule` DIRECTLY, in Maxon, with builder calls — no text, no parser.** v1 wrote IR
> *text* because it wanted to edit it as text; that cost it a **992-line `StdParser`**, a file-path walk
> that **panics if `runtime.std` is missing**, and a stdlib-checksum dependency. shv2 needs none of it:
> - **The pipeline is ALREADY REACHABLE, and `mrt_start` PROVES it** — it is hand-built from raw
>   `TargetOp`s and flows through the same `emitFunctionChunk` as every user function
>   ([BackendDispatch.maxon:149-175](maxon-shv2/Compiler/Targets/BackendDispatch.maxon#L149)).
>   `IrFunction.createFromStd` ([IrFunction.maxon:175](maxon-shv2/Compiler/IR/IrFunction.maxon#L175))
>   needs **no `FileParseArtifact`, no source position, no `MaxonType`**. ⇒ **The blocker was never the
>   architecture. It is the VOCABULARY.**
> - It is **type-checked by the compiler** rather than parsed at runtime, and it is **one spelling for
>   x64 and arm64** — where (a) duplicates the subsystem per target *by hand* (v1 did: `Arm64MacosGreenThread.maxon`
>   re-implements 3,853 lines of the same thing in AArch64).
> - **(a) buys NO CAPABILITY here.** Everything genuinely impossible in ordinary IR — the VEH thunk, GT
>   context switch, `__gt_morestack`, the OS worker-thread entry — belongs to the **fault handler (already
>   hand-assembled at P1.0d.3, correctly)** and the **GT scheduler (R3 @ P1.5)**. The allocator + refcount
>   core needs **only** atomics, OS-import calls, and ordinary internal calls.
> - ⚠ **(a) STRUCTURALLY INVITES THIS PROJECT'S SIGNATURE BUG, and it already did — in shv2, at P1.0d.3.**
>   The review (`651b4ea80`) caught the hand-assembled walker having **independently re-spelled the symbol
>   table's layout that the emitter writes** — *"widen the count and the walker still adds 4, striding the
>   table at the wrong pitch and naming the wrong function for every frame."* The fix had to be a **runtime
>   assertion** (`assertSymtabStride`) because **hand-assembly has no compiler-checked contract with the
>   tier that produced the data.** ONE FACT WRITTEN DOWN TWICE, structurally, forever.
>
> ### ⇒ P1.0r's REAL WORK IS THE VOCABULARY — and **"the precondition is satisfied" was FALSE**
> P1.0d.5b shipped the `memory` band, and the box below treats that as the whole gate. **It is not.** Four
> things are missing or wrong, and the first two are **the language's own semantics, not a tax the form
> chose** — `__destruct_*` dispatch reads a destructor pointer *out of the object header* and calls it, in
> **every** form:
> 1. ⛔ **NO REGISTER-INDIRECT CALL.** `TargetOp` has only `callDirect` + `iatCall`; `FF /2` with `mod=11`
>    (`call rax`) **has no encoder path at all**. **This is the hard blocker** — it is how a destructor is
>    reached.
> 2. ⛔ **NO `funcAddr`** — needed to *store* that pointer. Already named missing in-tree
>    ([X64Runtime.maxon:838](maxon-shv2/Compiler/Targets/X64/X64Runtime.maxon#L838)).
> 3. ⛔ **NO OS-import call from Std** — `VirtualAlloc` is not among `PeWriter`'s four kernel32 imports, and
>    `iatCall` is Target-tier only, unreachable from a Std op.
> 4. ⚠ **`StdTypeInfo` RECORDS NO SIGNEDNESS.** It carries `isFloat`/`castCategory`/`storageBytes`, so
>    `condCodeForPred` **cannot ask**, and every `u64` compare silently takes the SIGNED family
>    ([StdToX64Conversion.maxon:167](maxon-shv2/Compiler/Targets/X64/StdToX64Conversion.maxon#L167):
>    *"every Std integer is a signed i64"* — false; `StdType` has `u8`/`u16`/`u32`/`u64`). **Latent** (nothing
>    emits a `u64` cmp yet) and it does **not** bite Win64 pointers (user-mode addresses never set the high
>    bit, so both families agree) — **but it bites P1.2's `capacity = -2` rdata sentinel**, which is unsigned
>    and enormous. The fix is the file's own idiom: **put `isSigned` in the BACKING**, so *"a new case cannot
>    be added without stating"* it — exactly the argument `storageBytes` already makes at
>    [StdDialect.maxon:55-62](maxon-shv2/Compiler/IR/Std/StdDialect.maxon#L55).
>
> ### ⛔ THAT SLICE WAS WRONG, AND IT WAS WRONG THE `mintPhi` WAY — corrected within the hour
>
> ~~SLICE: `P1.0r.1` = the vocabulary (indirect call · `funcAddr` · OS-import call · signedness), each
> red-pinnable on its own; `P1.0r.2` = the allocator + refcount, built on it.~~
>
> **"Each red-pinnable on its own" was ASSERTED, not measured. It is false — measured, all four:**
>
> | item | its only consumer | red-pinnable alone? |
> |---|---|---|
> | `indirectCall` · `funcAddr` | `__destruct_*` dispatch | ❌ the allocator |
> | OS-import call | `VirtualAlloc` | ❌ the allocator |
> | **signedness** | `capacity = -2` | ❌ **P1.2's String — not this rung** |
> | **the allocator itself** | a heap value to allocate | ❌ ⇒ **structs** |
>
> ⭐ **The signedness row was measured and it is the sharpest**: a `u64` above `i64.max` **cannot be
> constructed at all** — `typealias Big = int(0 to u64.max)` + the literal `9223372036854775808` is
> **E2011** in BOTH compilers (*"outside the range of int (-9223372036854775808 to 9223372036854775807)"*),
> because the literal is range-checked against **i64**, never against the declared target range that
> contains it. So no program can reach the signed/unsigned divergence, and a "red spec" for it is
> unwritable. *(That the literal check ignores the target's declared range is arguably its own bug — but
> both compilers agree, so it is the language's de facto rule and it is not this rung's.)*
>
> ⇒ **A vocabulary-only rung is EXACTLY `mintPhi`, one step earlier.** That scaffold got *a consumer and
> not a reason to be right*, and `var f = 0.0` in a loop crashed the emitter. **Four ops with NO consumer
> is the same disease with nothing to catch it at all** — nothing would prove `indirectCall` calls the
> right address until the allocator dispatched a destructor through it, one rung later, on shipped code.
>
> ⇒ **CORRECTED SLICE — by CONSUMER, never by layer. `P1.0r` and `P1.1a` CO-LAND as ONE rung:**
> ### **"the heap and its first value"** — the allocator + refcount + **exactly the ops it consumes** +
> the minimal struct that is **the reason each of them has to be right**.
> **Acceptance is a REAL corpus case, not a bespoke one**: [`specs/structs.md`](specs/structs.md)'s
> `simple-type` — `let p = Point.create(3, y: 4)  return p.x + p.y` ⇒ **7**. It exercises the whole chain
> in one line: parse · layout · `__slab_alloc` · field store · field load · the struct-return ABI · and
> the **decref at scope end** that keeps the leak gate (exit **101**) green. **Then** `struct-field-access`
> (50), `sizeof.struct` (16), and the rest of the ~72 portable struct cases.
> ⚠ **This is what §"the acceptance test is P1.1a, not a bespoke spec" already said.** The layered slice
> contradicted it the same day it was written — *the plan disagreeing with itself, in the file whose own
> through-line is ONE FACT WRITTEN DOWN TWICE.*
> ⚠ **Atomics are NOT in P1.0r.** The refcount is atomic in the bootstrap (`LOCK INC`), but shv2 is
> single-threaded until R3 @ P1.5 brings green threads. **State the decision when P1.0r.2 lands** — do not
> let it be made by inertia, which is precisely what this box exists to prevent.
>
> ---
> *(The original box is kept below: it is the argument that forced the survey, and its instruction —
> "choose then, with the ops in hand, and write the reason down here" — is what the section above is.)*
>
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

### R3 residuals — P1.5's async slice is NOT complete until these close

The single-M R3 substrate (yield · netpoll · timer · subprocess-exit) shipped its CORE, but **P1.5's async
slice is not complete** — these reclamation and linearity residuals (the B-series holds them) remain. All
are `__slab`-invisible to the `__mm` leak gate:

- **#87 — the GT struct (~80 B) + its `__slab_alloc`'d arg buffer bump-leak per spawn.** B1a′ now handles
  the STACK (a relocating 2 KB `GtStackSize` seed, grown on demand, RELEASED on completion via
  `osFreePages`), but the `GreenThread` struct and its `GtOffArgBuf` are still `__slab`'d and never freed —
  invisible to the `__mm` leak gate. E3100 removed the re-await blocker; reclaiming the struct is a delicate
  scheduler change (co-recycle the arg buffer, an intrusive free-list, preserve nested-await LIFO, never
  recycle GT0). → **B1c.**
- **#92 — `__gt_process_run`'s per-call slab scratch (~150 B/call) bump-leaks** (STARTUPINFOA +
  PROCESS_INFORMATION + cmdline copy + exit-code slot, none freed). Same category as #87; full reclaim needs
  the recycling allocator. → **B1c.**
- **#88 — a never-awaited GT never runs, so its stack SEED + struct are never freed** — B1a′ SHRANK this
  from 1 MiB to a **2 KB seed** per spawn (the seed grows/frees only once a GT actually runs to completion),
  but `let p = async f()` with no `await` still leaks the 2 KB seed + the struct, and `while true { async
  f() }` is unbounded (bounded now by 2 KB, not 1 MiB). Needs linearity/drain — a "promise must be awaited"
  gate or an exit-drain of the ready queue. → **B2.**
- **#89 — `await` of a loop-carried / block-arg promise is over-rejected E2015**, because the promise mark
  and E3100 linearity both key on the defining `asyncCall` ValueId (not propagated across block args). SOUND,
  hits no corpus case; fix = flow promise identity + awaited-ness through block args. → **B2c.**
- **#93 — `runProcess` aborts via `osExit` on spawn-failure (1) and store-overflow (70)** — safe and
  spec-pinned, but silent and unrecoverable. A throwing / message-carrying path needs the `.rdata` interner
  (`installGtRuntime` takes `StdModule`, not `Project`) + the managed-error async path. → **subprocess-stdio.**

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

### Measured debt the trend log carries — revisit ONLY if the named trigger fires

`scale-test` and the per-type/per-scope tables have flagged these as **linear-in-practice**: real terms
that only bend under a specific future change. Each is filed with its measurement and its re-measure
trigger, not chased (a superlinearity you can *trigger* today is fixed, not filed):

- **#24(a) — `SplitLiveRanges` is O(blocks × K²)**, K = simultaneously-live owned values per block; measured
  K max 8, mean 0.16 over 2,445 fns ⇒ linear. (A separate quadratic — full liveness re-sweep per split —
  was already retired via `ReachCache`.) Re-measure trigger = an **INLINER** (shv2 has none).
- **#28 — the corpus hardcodes struct F=2**, hiding `StructLayout.indexOfField`'s O(F²) scan; measured F
  max 22 mean 4.14 over 166 real type decls ⇒ linear (programs add TYPES, not width). Re-measure needs a
  field-count corpus knob (a machine-generated wide type).
- **#32 — the E2027 duplicate-pattern check is O(P²)** in one match's hand-written arm count P; whole-program
  linear for bounded arms, quadratic only in a degenerate single-giant-match. Kept deliberately (a `Set` = a
  heap object per match to save a bounded scan).
- **#30 — the corpus is BLIND to instance methods AND `match`** (no knob emits either), so the optimizer
  measured ZERO allocation there; both paths are O(1)/O(fields) by inspection. Fold a method knob + a match
  knob in together.
- **#24(b) — post-pipeline runtime installs are billed to NO phase** (`installMmRuntime`/`installGtRuntime`/
  `installStringRuntime`/the destructor cascades run after `pipeline.run()`, outside every `PhaseProbe`), so
  phases don't sum to total; a constant ~316-alloc delta shows as `unattributed`. Constant, not superlinear.
- **#76 — the 14-register x64 pool refuses a 16-live-value interpolation-in-a-loop body (E5001)**, which
  restricts `capturing-closure-bound-outside-loop-called-inside` to wasm. Proven closure-INDEPENDENT (the
  same body with a plain `let r = i + 5` E5001s identically) — the "refuse the search" thesis firing, not a
  bug; x64 env-drop timing is covered by the `-rebound-in-loop`/`-called-from-nested-block` siblings.

---

## Beyond the two phases

**Broaden:** general `Iterable` + associated types · `List`/Json/… · ~~arm64~~ (macOS **scalar core DONE 2026-07-18** — see the Targets row; the arm64 **runtime floor** is its next rung) + wasm (**scalar+floats+globals+heap/String/print/structs/unions+ERROR-HANDLING DONE 2026-07-19** — WASI Preview2 component, **732/0** = the host's full runnable set minus technical restrictions (register/RequiredData); only Arrays/generics + async + a codegen relooper remain) · coverage ·
inliner. *(Two things LEFT this list on 2026-07-13. **`async`/green threads** are core, at P1.5.
**Porting the spec suite** is no longer an endgame chore — it is **Workstream S**, the driver of
every rung, starting at P1.0b.)*

**Cross-target goldens still owed** — all blocked on a CAPABLE HOST (this Windows box can *emit* for these
targets but cannot *run* their suites): **#91** the `does not throw'` stray-quote message typo
(`Queries.maxon`, pinned by all four fragment trees — fix + `--update-required` on a capable host); **#86**
the P1.9 arm64-macos/arm64-linux goldens (the x64-linux half is done); **#67** un-restrict the A1
function-value + A2a closure spec cases to arm64 (the ADRP+ADD codegen has landed and is disasm-verified —
this needs an arm64 RUNNER); **#20** the arm64/wasm compile-error fragments a Windows host structurally
cannot regenerate (it cross-compiles then tries to EXECUTE).

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

### Bootstrap (`maxon-sharp`) oracle bugs — silent wrong answers to fix when the subsystem is next touched

The bootstrap is the runnable `/specs` oracle, so a silent wrong answer in it erodes what shv2 is checked
against (user has ruled before: *fix the bootstrap so it stays a solid reference* — cf. the throw-ownership
work). **shv2 does NOT inherit these** (it diverges deliberately, usually stricter); each needs the full C#
suite as its gate:

- **#17 — two file-private `var`s of the same name alias into ONE `.data` slot** (`getA()+getB()` returns
  200 not 107): the name resolver is file-scoped but the global label is bare-name (`_globalLabels[name]`).
  Fix = file-qualify the global label in both emitters + the registry.
- **#90 — a duplicate top-level `let`/`var` is silently first-wins (exit 0)** while a duplicate FUNCTION is
  E3006 — inconsistent with itself (`ConstantDeclSet.From` uses `byName.TryAdd`). shv2 rejects E3006. Fix =
  raise E3006 at the later decl.
- **#46 — the whole-program mutating-parameter fixpoint does not converge** as the call graph grows,
  alternately demanding `let` (E3019) then `var` (E3077) on the SAME binding. Worked around in shv2 by a
  direct `branchEdges.push`. Fix = make `ParameterMutationAnalysisPass.SequentialFixpoint` converge.
- **#70 — a managed `Array` payload bound as a variant's TAIL field in a LARGE `match` arm corrupts the
  scrutinee's decref** (`mm_decref` on a sentinel; crashed every `try`/`throw` lowering when `indirectCall`
  joined `lowerOp`). Worked around in shv2 by a dedicated `lowerIndirectCall`.
- **#72 — a helper storing into ~11 aliased `self` array-fields double-frees at shv2 self-compile time**
  (`__destruct_FuncSignature`). Forces shv2 to keep the per-body column reset inline. Same class as #70.
- **#58 — a mixed-type `match … gives` internal-errors** (`E9001: Value cannot be null`, a C# null-ref, not
  a clean diagnostic). shv2's side is E3005 (via #54 Slice C); correct bootstrap behavior mirrors it.
- **#95 — a match/ternary EXPRESSION whose arms are function VALUES internal-errors** (`E9001: Cannot
  determine function type from MaxonVarRefOp`, a C# throw, not a clean diagnostic — DISTINCT from #58's
  null-ref). shv2 is strictly better: the P1.5 value-merge cluster rejects a mismatched function-value merge
  with E2028 (ternary) / E3005 (match), so the bootstrap cannot serve as shv2's oracle for that form. Found
  2026-07-22 by the value-merge reviewer. Fix = mirror shv2's whole-program merge signature check.
- **#4i — E3097 (enum-accessor comparison) is fully defeated by any NARROWING `as` cast** (`c.rawValue as
  Ordinal` interposes a range-check value that hides the accessor; a WIDE cast does not). Key the rule on the
  QUESTION, not the producing-op identity. (shv2 does not emit E3097 yet — a future shv2 rule too.)
- **#3 (residual only) — a double-await through STORAGE stays silent** (double-free): a boxed promise read
  from storage gets a FRESH `GreenThreadId`. shv2's side is closed (E3100 linear-await; stored/param promises
  are E2015), so shv2 does NOT hit this; maxon-sharp does.
- **#34 — union `E2026`/`E2046` say "enum"** (a hardcode oversight — the bootstrap's own E3034 IS
  `IsUnion`-conditional) where shv2 says "union" via `EnumLayout.kindWord()`. Nothing pins either; align the
  bootstrap's wording, or author an shv2 spec pinning "union".

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

**The scalar core is DONE — and this time MEASURED, not asserted: `specs-shv2` 371/0** (P1.0d closed
2026-07-15). `let`/`var` · full-Pratt arithmetic/comparison/unary · parens · `true`/`false` ·
`not`/`and`/`or` · bitwise · chars · **floats (f64, XMM class)** · real block scoping · void functions ·
top-level `typealias`/`let`/`var` · `if`/`else` · `while`/`break`/`continue` (on-the-fly SSA +
`EliminatePhis`) · functions with params + calls · integer `/` and `mod` with **`a / 0` a clean panic**.

**shv2 HAS A HEAP** (P1.0r, 2026-07-15): a VirtualAlloc bump allocator that always returns zeroed memory,
`__mm_alloc`/`__mm_decref`/`__mm_free` as a builder-built `StdModule`, and a leak gate that fires.
**And STRUCTS are real** (P1.1a waves 1–2): `type` · layout · `Self{}` · `static create` · field reads,
writes, mutability rules and defaults.

⚠ **The paragraph that used to live here said the scalar core was INCOMPLETE, and before that, that it was
DONE. Both were written with equal confidence, and the second was wrong by 48-of-2,746.** The difference
was never the prose — it was whether anyone had run the corpus. **Every claim above is a suite number that
`spec-test` will reproduce, or it is not in this section.**

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
| Testing | 7,699 | ✅ ~~704~~ → **6,982** (measured 2026-07-16) |
| **Total** | **191,487** → **192,971** (measured) | **~50–65k** |

**Current: ~~21,038~~ → 44,671** (measured 2026-07-16; `find maxon-shv2 -name '*.maxon' -exec cat {} + | wc -l`
— `Compiler/` is 37,096 of it, `Testing/` 6,982). Self-compile is **~5–20k lines away** on the estimate
above — and the hardest *single* piece of it (the allocator) is already behind us.

> ⚠ **THIS TABLE WAS STALE BY 2×, AND ITS `Testing` CELL BY 10× — and the drift is the file's own disease,
> aimed at its own schedule.** `21,038` and `704` were true when the plan was written and **nothing
> re-derives them**, so they aged into a claim ("~30–45k away") that was never re-measured. **The method is
> confirmed identical, not guessed:** the same command over v1 reads **192,971** against its recorded
> **191,487** — a 0.8% drift from ordinary edits, which is what a *maintained* number looks like next to an
> abandoned one. **The command is now written down beside the number**, because a figure whose derivation is
> unstated cannot be checked and therefore will not be. ⚠ **The `~50–65k` ESTIMATE is itself unvalidated** —
> it is a 2026-07-13 guess, and `Testing` alone overran its whole line by 6,278. Treat "5–20k away" as
> arithmetic on an untested premise, not as a schedule.

---

## Verification

- **Per rung:** `maxon-shv2 spec-test` stays green (**371/0** as of 2026-07-16, and growing with
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
