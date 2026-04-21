# Refcount emit-site audit

Phase A of the [refcount optimization roadmap](refcount-optimization-roadmap.md)'s
emit-time pre-filter investigation. This document catalogs every
`EmitIncref`/`EmitIncrefValue`/`EmitIncrefValueIfNonnull`/
`EmitDecrefValueIfNonnull` call site in the Maxon→Standard conversion and
classifies each by whether the emitted refcount op is:

- **Always needed** — the op is load-bearing and cannot be skipped at emit
  time regardless of downstream analysis.
- **Local-context-skippable** — the emitter already has enough information
  (in `OwnershipFlags`, in the surrounding lowering decision, in the kind of
  ownership being established) to decide at the emission point that the op
  is unnecessary. These are candidates to skip at emit time.
- **Needs cross-statement analysis** — the op may be redundant, but proving
  so requires looking at subsequent uses, alias flow, or control flow. These
  belong to the optimizer's three existing sub-passes
  ([RefcountOptimizationPass.cs](../maxon-sharp/Compiler/MLIR/Passes/RefcountOptimizationPass.cs)).

The helper definitions live at
[MaxonToStandardConversion.Helpers.cs:384-434](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.Helpers.cs#L384-L434).
`OwnershipFlags` is defined at
[VarRegistry.cs:6-16](../maxon-sharp/Compiler/VarRegistry.cs#L6-L16):
`None, CallReturn, Borrowed, Orphan, SelfReturn, IsTemp, IsParam, OwnsRef`.

## Summary counts

Total distinct emission sites across the conversion: **48**.

| Classification | Count | Notes |
| --- | --- | --- |
| Always needed | 34 | Long-lived owning stores, destructors, scope cleanup, heap-container ownership acquisitions |
| Local-context-skippable | 2 | Enum-construct orphan pairs and struct-literal orphan pairs (both in .cs) |
| Needs cross-statement analysis | 12 | Assignment path, field access, container reads, global load, for-in/cursor results, `__forin_result` alias |

Two of the 48 sites are genuinely skippable at emit time. The rest are
either load-bearing (34) or need the optimizer's analyses to decide (12).
This refines the Phase A estimate from the plan: the emit-time wins are
real but narrow — the bulk of the elimination work legitimately belongs in
the optimizer.

## Classification table

Sites are grouped by file, in ascending line order. For each site the
**trigger** (what IR pattern causes the call), **purpose** (what the ref op
is protecting), and **class** (Always / Skippable / NeedsAnalysis) is
recorded.

### MaxonToStandardConversion.cs

| Line | Helper | Trigger | Purpose | Class |
| --- | --- | --- | --- | --- |
| 435 | IncrefValue | `MaxonEnumConstructOp` heap-pointer payload | Enum heap object holds a ref to each managed payload | **Always needed** — the enum is now a new owner of the payload. |
| 460 | IncrefValue | `MaxonEnumConstructOp` orphan (not inlined, not field value, not returned) | Balance a later scope-end decref on the orphan temp | **Skippable** — the orphan temp is literally invented here; both the incref and the scope-end decref are emitted by this conversion. Emitting neither is equivalent. Candidate for first skip. |
| 603 | DecrefValueIfNonnull | `MaxonEnumPayloadAssignOp` old heap-pointer slot | Release the old payload before overwriting | **Always needed** — standard field-overwrite protocol. |
| 607 | IncrefValue | `MaxonEnumPayloadAssignOp` new heap-pointer | Enum takes ownership of the new payload | **Always needed**. |
| 852 | IncrefValue | `MaxonStructLitOp` nested struct/enum field value | Parent struct takes ref on child | **Always needed** — the child value may have been aliased from elsewhere; the parent holds a distinct reference. |
| 860 | IncrefValue | `MaxonStructLitOp` heap field where value isn't tracked as `StdHeapPtr` (runtime call results) | Parent struct takes ref on runtime-allocated child | **Always needed**. |
| 989 | IncrefValue | Array-literal element, struct type | Array buffer holds refs to its element values | **Always needed**. |
| 1037 | IncrefValue | Struct literal orphan (not inlined/field/returned) | Balance scope-end decref on the orphan temp | **Skippable** — same shape as the enum orphan at 460: the incref and its matching scope-end decref are both injected here for a temp that exists only for cleanup bookkeeping. Candidate for second skip. |
| 1067 | DecrefValueIfNonnull | `MaxonAssignOp` stack-path old dst value | Release previous heap value before stack overwrite | **Always needed**. |
| 1087 | DecrefValueIfNonnull | `MaxonAssignOp` heap-path old dst value | Release previous value before overwrite | **Always needed**. |
| 1103 | Incref (slot) | `MaxonAssignOp` heap-path: the main alias-assignment incref | New reference for dst's slot | **Needs analysis** — this is the site the optimizer's intra-block, cross-block, and loop-invariant sub-passes all target (see RefcountOptimizationPass.cs roles described in the roadmap). The emitter *does* skip it when `srcName` has `CallReturn` or `OwnsRef` (lines 1095–1104), but cannot decide the general "src is alive across dst's lifetime" question locally. |
| 1354 | DecrefValueIfNonnull | `MaxonFieldAssignOp` old field value | Release old field before overwrite | **Always needed**. |
| 1359 | IncrefValue | `MaxonFieldAssignOp` new field value | Struct field holds a ref | **Always needed**. |
| 1470 | IncrefValueIfNonnull | `MaxonScopeEndOp` pre-incref Borrowed field temp being returned | Keep the field alive past parent's destructor on return | **Always needed** — this is a specific escape from the borrow convention for returned field aliases. |
| 1529 | DecrefValueIfNonnull | `MaxonScopeEndOp` per-slot scope cleanup | End-of-scope ownership release | **Needs analysis** — most of the optimizer's eliminations pair *this* site with an incref from line 1103 or from a call-return. Cannot be skipped at emit without knowing whether the slot's ownership is still load-bearing at scope end. |
| 1552 | DecrefValueIfNonnull | `MaxonScopeEndOp` orphan-temp cleanup | Release orphan lowering-temp | **Needs analysis** — pair with the orphan incref at 460/1037. If those are skipped, *this* scope-end decref must also be skipped for the same temps, so 460+1552 and 1037+1552 move together as a unit. The skip is local when the temp is genuinely "emitted and cleaned up by this lowering and nothing else touches it." |
| 1805 | Incref | `MaxonGlobalLoadOp` struct result | Caller gets a fresh owned reference on the loaded global | **Always needed** — the global slot continues to own its ref; the caller needs its own. |
| 1832 | DecrefValueIfNonnull | `MaxonGlobalStoreOp` old global value | Release old global before overwriting | **Always needed**. |
| 1836 | IncrefValue | `MaxonGlobalStoreOp` new global value | Global takes ref | **Always needed**. |
| 2184, 2190 | DecrefValueIfNonnull | `__maxon_global_cleanup` for static structs (lazy + eager) | Process-end release of global refs | **Always needed**. |
| 2361 | DecrefValueIfNonnull | Destructor for assoc-enum payload slot | Free managed payload on enum destruction | **Always needed**. |
| 2417 | DecrefValueIfNonnull | Destructor for `__ManagedMemory` slice parent | Release parent buffer on slice cleanup | **Always needed**. |
| 2460 | DecrefValueIfNonnull | Destructor for each managed struct field | Cascade cleanup | **Always needed**. |

### MaxonToStandardConversion.Calls.cs

| Line | Helper | Trigger | Purpose | Class |
| --- | --- | --- | --- | --- |
| 232 | IncrefValue | `try_call` assoc-enum dummy allocation | Establish rc=1 on dummy so scope cleanup can uniformly decref whichever of (real, dummy) was selected | **Always needed** — this is the non-null invariant enabling the `std.select` + single-decref pattern. Without it the dummy path would leak. |
| 243 | DecrefValueIfNonnull | `try_call` assoc-enum: decref the non-selected of (real, dummy) | Release whichever branch didn't win the select | **Always needed** — mirror of 232; can't be elided without reworking the select pattern. |
| 295 | Incref (slot) | Assoc-enum return incref on param or managed temp | Caller receives owned rc≥1 | **Needs analysis** — the optimizer's cross-block sub-pass targets this when the caller immediately stores-and-releases the return. Cannot be skipped at emit without per-callee retention summaries (roadmap #5). |
| 320 | Incref (slot) | Struct return incref on param or managed temp | Caller receives owned rc≥1 | **Needs analysis** — same shape as 295. |

### MaxonToStandardConversion.ArithmeticAndClosures.cs

| Line | Helper | Trigger | Purpose | Class |
| --- | --- | --- | --- | --- |
| 366 | IncrefValue | Closure env allocation | Closure holds the env; balances scope-end orphan cleanup | **Always needed** — the env is heap and escapes via the function pointer. |

### MaxonToStandardConversion.ManagedMemory.cs

| Line | Helper | Trigger | Purpose | Class |
| --- | --- | --- | --- | --- |
| 357 | IncrefValue | `mm_get` nonnull-slot path; struct element | Caller gets a fresh ref on the loaded element, balances scope-end decref on the `mmget` temp (which is marked `Orphan | OwnsRef`) | **Needs analysis** — roadmap item #2 (paired-pop elimination on container reads). The `OwnsRef` flag at [ManagedMemory.cs:267,337](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.ManagedMemory.cs#L267) correctly suppresses the later `Assign`-path incref at [MaxonToStandardConversion.cs:1103](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L1103), but the *first* incref (here) and its paired scope-end decref still fire. Eliminating them locally requires knowing the caller doesn't retain — same analysis as roadmap #2. |
| 647 | DecrefValueIfNonnull | `mm_set` old element slot (struct) | Release old before overwrite | **Always needed**. |
| 651 | IncrefValue | `mm_set` new element (struct) | Buffer takes ref on new element | **Always needed**. |
| 1153 | DecrefValueIfNonnull | COW-to-owned: slice's parent ptr after copy | Release the source slice once owned copy exists | **Always needed**. |
| 1783 | IncrefValueIfNonnull | Per-element incref during array-copy loop | Copied buffer holds new refs to each element | **Always needed**. |
| 2102 | IncrefValue | Cursor initialization: source_ptr incref | Cursor holds a ref on its backing buffer | **Always needed**. |
| 2167 | IncrefValue | `cursor.current()` struct-element load | Caller gets fresh ref on loaded element, balances scope-end on `ccur` temp (`Orphan | OwnsRef`) | **Needs analysis** — same shape as 357; this is the for-in-over-managed-elements pattern the roadmap called out as optimization #0/#2. |

### MaxonToStandardConversion.ManagedList.cs

| Line | Helper | Trigger | Purpose | Class |
| --- | --- | --- | --- | --- |
| 94, 152 | IncrefValue | Managed-list node creation: node takes ref on its stored struct value | Owner transfer into the list | **Always needed**. |
| 246 | DecrefValueIfNonnull | Managed-list detach: release list's counted ref | Runtime-directed release | **Always needed**. |
| 272, 292 | DecrefValueIfNonnull | Managed-list remove: release list's ref, then release node | Runtime-directed release | **Always needed**. |
| 283 | DecrefValueIfNonnull | Managed-list remove: release node's ref on extracted value | Transfer ownership to caller via an `Orphan | OwnsRef`-style pattern | **Always needed**. |
| 378 | DecrefValueIfNonnull | Managed-list node replace: old value | Field-replace protocol | **Always needed**. |
| 385 | IncrefValue | Managed-list node replace: new value | Node takes ref | **Always needed**. |
| 571 | IncrefValueIfNonnull | Managed-list navigation (next/prev node lookup) | Caller owns the returned node reference (temp is `Orphan | OwnsRef`) | **Needs analysis** — same "fresh ref on container read" shape as 357/2167. |

### MaxonToStandardConversion.Strings.cs

| Line | Helper | Trigger | Purpose | Class |
| --- | --- | --- | --- | --- |
| 55 | Incref (slot) | `_managed` field creation on new String/__ManagedMemory | String holds a ref on its managed backing | **Always needed**. |
| 267 | Incref (slot) | String interp result: `_managed` field | Same as 55 for interpolation | **Always needed**. |
| 950 | IncrefValue | Nested slice-of-slice: incref source's parent | Propagate parent ownership through a slice copy | **Always needed**. |
| 1069 | IncrefValue | Character constructor: incref the managed field | Character owns a ref | **Always needed**. |

## The two skippable sites

Both of the genuinely skippable sites share the same pattern: the emitter
creates a lowering-internal orphan temp, injects an `incref` on it at
creation, and relies on the scope-end cleanup to inject a matching
`decref`. The temp is never stored to a second slot, never aliased, never
passed to a call — it exists solely as a cleanup anchor.

- [MaxonToStandardConversion.cs:460](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L460)
  (enum construct orphan): paired with the scope-end orphan-cleanup loop
  at [MaxonToStandardConversion.cs:1548-1556](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L1548-L1556).
  The condition that triggers this incref (`!inlineTargets.Contains(...) &&
  !structLitFieldValueIds.Contains(...) && !structLitReturnIds.Contains(...)`)
  is the same condition that will cause the scope-end decref to fire for
  this temp.

- [MaxonToStandardConversion.cs:1037](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L1037)
  (struct literal orphan): identical shape. Same conditions at the same
  lines; the temp is only used for tagged scope-end cleanup.

**However — the allocation returns rc=0.** Per the roadmap's appendix
([refcount-optimization-roadmap.md:448](refcount-optimization-roadmap.md#L448)),
`mm_alloc` returns rc=0, and the incref immediately after is what
transitions the allocation to rc=1 so that the matching decref can drive it
back to rc=0 and free it. **Naively skipping the orphan incref would
leave the decref pointing at an rc=0 allocation, and the decref would
underflow (or with `_if_nonnull`, no-op) — leaking the allocation.**

The correct skip is a pair skip: remove **both** the orphan incref and its
matching scope-end decref **together**, and replace them with a direct
`mm_free` (or equivalent: mark the allocation so the scope-end cleanup
emits a `mm_decref` on an already-rc=1 allocation by some other route).
This is non-trivial because the scope-end cleanup at line 1552 is a
generic loop over `temps.OrphanTemps` — it has no per-temp knowledge that
a particular orphan needs `mm_free` instead of `mm_decref`.

There are three ways to make the skip land safely:

1. **Skip both sides explicitly.** Track a set of "orphan temps with
   skipped incref" on the `VarRegistry` or a sibling map; at scope-end,
   iterate that set first and emit `mm_free` directly, then iterate the
   remaining orphans with the existing `mm_decref` path. Most direct.
2. **Route through `mm_alloc_with_rc1`.** Add a variant of `mm_alloc` that
   returns at rc=1, use it for these two sites, and let the scope-end
   decref drive rc=1 → rc=0 → free uniformly. Cleaner but touches the
   runtime.
3. **Leave them.** The optimizer currently doesn't eliminate these pairs
   (the `mm_alloc`-trap guard explicitly refuses to eliminate when the
   source slot has no own decref, and these orphans *are* the source
   slot). The savings per site are small — two runtime calls per literal.
   Net traffic reduction depends on how many orphan struct/enum literals
   appear in hot paths.

**Recommendation.** Approach 1 is the minimum viable skip and is
confined to the conversion pass + VarRegistry. Landing it would shrink
emitted IR on roughly every struct/enum literal that appears as a borrowed
argument, but would not unlock new optimizer wins downstream because the
existing pass already treats these pairs as unreducible by design.

Given the user's goal is **runtime refcount traffic in the final binary**,
this is a net win of ~2 calls per hot orphan literal. Worth doing, but
smaller than the remaining roadmap items.

## What this audit rules out

The original Phase A hypothesis listed "Borrowed-marked field loads" and
"for-in iteration temporaries" as emit-time-skippable candidates. The
audit shows both are **not** local-context-skippable:

- **Borrowed field loads.** The `Borrowed` flag marks the temp to be
  skipped at scope-end cleanup (see
  [MaxonToStandardConversion.cs:1468-1472](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L1468-L1472)
  — Borrowed temps that are *returned* get a special pre-incref). The
  actual incref on a field load path (line 1103 during `Assign`, or the
  `OwnsRef`-marked `mmget` incref at line 357) is not suppressed by the
  `Borrowed` flag today. Suppressing it would be correct only when the
  field's source struct is alive across the borrow's lifetime — which is
  the optimizer's cross-block analysis, not a local decision.

- **For-in temporaries.** The `iter.current()` / `cursor.current()`
  results already use `OwnershipFlags.Orphan | OwnershipFlags.OwnsRef`
  (line 2170), which successfully suppresses the caller's assignment
  incref at line 1103. The remaining pair — the cursor-current incref at
  line 2167 and the scope-end decref for that temp — is the pair that
  roadmap item #4 (loop-invariant elimination) targets, and safety still
  requires the same "cursor buffer alive across the window" check the
  optimizer performs. Moving it to emit time would need the same CFG+
  liveness information; there is no local shortcut.

In short: **the `OwnershipFlags`-based filtering the emitter already
performs is the complete local-context filter**. Everything else needs
the optimizer.

## Conclusions and next steps

1. **The optimizer is not doing redundant work.** The 12 `Needs analysis`
   sites correspond 1:1 to patterns the optimizer's three sub-passes
   legitimately require cross-statement information to eliminate. The
   audit found no site where the optimizer is reconstructing information
   the emitter already threw away — the emitter acts on every piece of
   local info it has (via the `CallReturn`/`OwnsRef`/`SelfReturn` skips at
   [MaxonToStandardConversion.cs:1095-1104](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L1095-L1104)
   and the `Borrowed` skip at the scope-end cleanup).

2. **Two small emit-time wins are available**, both orphan-temp pair
   skips at lines 460 and 1037. Landing them is mechanical but requires
   a coordinated skip of the paired scope-end decref (per the `mm_alloc`
   trap) and confers a savings of ~2 runtime calls per hot orphan
   literal. Recommendation: prioritize only if profiling shows these
   literals in hot paths.

3. **The big wins for "runtime refcount traffic" live in the remaining
   roadmap items** in
   [refcount-optimization-roadmap.md](refcount-optimization-roadmap.md):
   - **#5 (interprocedural ownership / short-lived arg temporaries)** —
     now landed via
     [ParameterRetentionAnalysisPass](../maxon-sharp/Compiler/MLIR/Passes/ParameterRetentionAnalysisPass.cs)
     gating the relaxed aliasing view in the intra- and cross-block
     sub-passes. Scoreboard section 2 / section 8 wins.
   - **#7 (non-null downgrade)** — doesn't reduce counts but removes ~2
     insns per decref across ~263 sites in the scoreboard.
   - **#4 hoisting (deferred from item #4's elimination-only landing)** —
     preheader+loop-exit placement for loop-carried refs.

4. **Recommendation.** With #5 now landed, the next primary
   "emitted-code quality" win is roadmap #7 (non-null downgrade).
   Treat the two orphan-temp skips
   as a side project for when the optimizer work needs a palate
   cleanser; they will not change the scoreboard numbers materially on
   their own.
