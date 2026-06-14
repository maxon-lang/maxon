The findings are all confirmed against source. Line 1282 already uses `data.constantStringTable` (the template change), line 1271 still uses `writeMaxonType(project, ...)` (the blast radius), `CACHE_FORMAT_VERSION = 80` at line 262, and the re-serialize panic note is at line 2918. I have everything needed. Writing the design doc now.

---

# DESIGN: Library-owned id-space for the Maxon library cache (stdlib first)

Status: proposal for lead review. No code written. Targets `maxon-selfhosted/Compiler/StdlibCache.maxon`, `maxon-selfhosted/Compiler/IR/Maxon/MaxonDialect.maxon`, `maxon-selfhosted/Compiler/Project.maxon`.

---

## 1. PROBLEM RESTATEMENT

The library cache (today: stdlib only, `StdlibCacheData`) is supposed to let any consuming project read compiled stdlib bytecode, lazily extend it (compile a stdlib function the consumer first touches), and **persist** the grown cache back to disk so the next process reuses it. The first two work. The third is broken by **project-coupling in the codec write path**.

**The coupling.** Every id-bearing field is serialized by resolving its dense id through the *consuming* `project`'s interners, not through the cache's own snapshot. The reason this is wrong: each registry assigns ids by insertion order (`StringInterner.intern`: `let id = values.count()`, Project.maxon:1876; `TypeNameInterner.intern`: `names.count()`, Project.maxon:1795; `GenericInstanceRegistry.intern`: `baseIds.count()`, Project.maxon:1486). A cache produced by a dedicated cache-build project carries ids in *that* project's order. A worker that reads the cache, then lazily grows it, holds a `project` whose id-space diverges (it interned its own user types, and `readMaxonGenericInstance` interns generic args inner-first, MaxonDialect.maxon:1001-1014, so even the same generics land at different ids). When that worker tries to re-serialize, the write path dereferences foreign ids through the wrong registry → wrong value or out-of-range panic.

**The evidence in the code.** The `constantStringTable` field comment states it outright: a foreign `stringValue` id "panics in `writeConstantValue` (`project.strings.get(foreignId)`) if that consumer ever re-serializes its lazily-grown cache — the coupling that forced the persist gate off for workers" (StdlibCache.maxon:459-463). The restore path for top-level constants repeats it: inserting verbatim keeps a foreign id, "wrong string in this project, and a panic if this project later re-serializes the cache" (StdlibCache.maxon:2917-2918).

**What has been done so far (the template).** Exactly one write site has been de-coupled: `writeConstantValue` now takes a snapshot table, and its only caller passes `data.constantStringTable` rather than `project.strings` (StdlibCache.maxon:1282; `writeConstantValue(stringTable StringArray, ...)` Project.maxon:2618). This is the prototype for the whole fix — but it is one field. Every other id-bearing write still routes through `project` (finding #2 blast radius, enumerated in §3.iii below).

**Consequence.** Because re-serialize panics, the persist gate is off for any project that isn't the dedicated cache-build. The on-disk cache is stuck at **metadata-only** — it never accumulates compiled bytecode from spec-runner workers or ordinary user builds. Spec workers each recompile stdlib bytecode from scratch instead of sharing it, throwing away the measured **12.7x CLI warm speedup** that a populated, shared cache delivers. The principled fix is to give the library cache its **own stable id-space** so the codec resolves through the cache's snapshots, never the consumer — making re-serialize safe from any project.

---

## 2. THE TWO MODELS FOR "PER-LIBRARY IDS"

**Model (a) — separate per-library interner + compound `(library, id)` keys everywhere.** Each library keeps its own numbered id-space; a `MaxonType` arm becomes `(library, id)`; every registry becomes per-owner-partitioned; every read site disambiguates by owner.

**Model (b) — library interned first → its ids are a stable reserved prefix of the project's single id-space, identical across all consumers.** The library's registries are interned into a fresh consumer **in the library's own deterministic capture order, replayed identically by every consumer**. Because interning is order-deterministic and the library goes in first (before any user type), the resulting ids are a stable prefix — the same integer in every consumer — *without* compound keys.

### Verdict: Model (b). Model (a) is ruled out by the evidence.

Finding #3 establishes model (a) is incompatible with the codebase as written, on three independent grounds:

1. **The id carrier has no owner field.** `MaxonType` arms each carry a single bare integer — `named(id TypeNameId)`, `typeParameter(id)`, `genericInstance(id)`, `interface(id)`, `function(id)` (MaxonDialect.maxon:69-118). There is no `(library, id)` pair anywhere in the type representation. Model (a) requires widening `MaxonType` itself.

2. **The registries are dense position-indexed single-namespace arrays.** `GenericInstanceRegistry.get(id)` *is* `baseIds.get(id)` (Project.maxon:1493-1497) — the id is the array index, one global namespace, no per-owner partition. `TypeNameInterner.get(id)` *is* `names.get(id)` (Project.maxon:1814-1816). Model (a) requires re-architecting every registry.

3. **~225 read sites across 14 files index `project.*` by a bare id with no library context** (finding #3: LowerMaxonToStd 57, MaxonDialect 40, TypeResolution 40, BuildLayoutDescriptors 18, …). Representative: `let (baseId, _) = project.genericInstances.get(gid); return project.typeNames.get(baseId)` (TypeResolution.maxon:3252-3253). Model (a) requires threading an owner through all of them.

Model (a) is a pervasive, cross-cutting rewrite of the type representation. Rejected.

### Model (b) is what the code already does — and the determinism claim holds.

`restoreRegistries` already re-interns the stdlib's registries into the destination project, in cache order, before user code is parsed (typeParameters StdlibCache.maxon:3007-3015, interfaceRegistry :3028-3030, genericInstances via the writer-gid rebuild :3061-3065, conformances :3076-3080). Finding #3's conclusion is explicit: the read sites "force model (b)… intern the library into the consumer first so its ids form a stable prefix of the shared project-global id-space — which is precisely what `restoreRegistries` already does."

**Is the prefix actually identical across consumers?** Yes — *if and only if* the replay order is the library's own capture order and is byte-deterministic, AND the consumer interns the library before anything else. Both hold for stdlib:

- **The library goes in first.** The stdlib is restored into an otherwise-empty project at load, before any user type is interned. So its prefix `[0..N)` is never perturbed by user types appended at `[N..)`.
- **The replay order is the library's, not the consumer's.** The name/content-keyed registries (typeNames, typeParameters, interfaceRegistry, functionTypes, conformances) already round-trip by re-interning names/content in serialized order, so they reproduce the same ids in any empty consumer. Finding #1 confirms these are "UNSTABLE id but name-keyed/content-addressed → portable."

- **The one hole is generic-instance gid ordering.** `readMaxonGenericInstance` interns args inner-first (MaxonDialect.maxon:1006-1012), so the destination id ordering diverges from the writer's whenever args are themselves generic. This is the sole reason `writerGidToProjectGid` exists today (StdlibCache.maxon:561-571). Under model (b) this hole is **closed at its source**: if the codec writes the gid registry in the library's own capture order and the reader replays it in that exact order *as a flat list, interning each entry's args by the ids already established earlier in the same ordered replay* (rather than recursively re-deriving them), every consumer reproduces the identical gid prefix. The inner-first divergence only arises because the recursive reader re-interns args out of band; a flat in-order replay of the captured registry removes it.

**Conclusion.** Model (b) — deterministic in-order replay of the library's registries as a stable prefix — is correct, is already 90% implemented for the name-keyed registries, and makes the cache's ids **directly reusable** (the prefix is identical in every consumer) once the generic-instance replay is made order-faithful. Compound keys are unnecessary.

---

## 3. DESIGN (Model b)

The library cache owns an **ordered, self-describing id-space**: a set of registry snapshots whose array position *is* the library-local id. A consumer reconstructs that exact id-space as a prefix by replaying the snapshots in order. The codec reads and writes **entirely in the library id-space**, resolving through the cache's own snapshot tables — never the consuming project. This generalizes the `writeConstantValue(data.constantStringTable, …)` change (StdlibCache.maxon:1282) to every id-bearing field.

### (i) What the library cache stores for its id-space

The cache already carries most of it as in-memory snapshots; the design **promotes the snapshot tables to the authoritative, serialized id-space** and adds the missing ones:

- `typeNameTable` (StringArray, StdlibCache.maxon:452) — position = library `TypeNameId`. Already present; becomes authoritative and **serialized** (today it is rebuilt on decode — see §5).
- `constantStringTable` (StringArray, StdlibCache.maxon:464) — position = library `StringId` for constant strings. Already present; becomes authoritative and serialized.
- `typeParameters` (TypeParamRegistry, :491), `interfaceRegistry` (InterfaceTypeRegistry, :492), `functionTypes` (FunctionTypeRegistry, :493-499), `conformances` (:490) — already snapshotted; position/intern-order = library id.
- `genericInstances` (GenericInstanceRegistry, :502) — the load-bearing one. Stored as an **ordered flat list** in library-capture order; position = library `GenericInstanceId`. The `(baseId, args)` payload references **library** TypeNameIds and **library** gids of earlier entries (a strict DAG — args are always interned before their container in capture order).

The ordered list is the contract: **library id = index into the serialized registry, in capture order.**

### (ii) How a consumer restores so library ids are STABLE and identical to the cache's

Replace the "re-intern, then build a writer→project translation table" dance with **deterministic in-order replay into an empty prefix**:

1. The consumer's project registries are empty (or seeded only by libraries restored earlier — §3.iv, §4 multi-library). The library is restored before any user type.
2. For each registry, iterate the serialized snapshot **in order** and `intern` each entry. Because interning is order-deterministic and the destination starts empty at this library's base offset, entry *k* lands at library-local id `base + k` in **every** consumer.
3. For `genericInstances`, replay the **flat ordered list**, interning each entry's `(baseId, args)` using the ids already assigned earlier in the *same* replay (not via recursive re-derivation). This reproduces the writer's gid ordering exactly, so `gid` in the cache == `gid` in the consumer.

The result: the library occupies id range `[base, base+N)` identically in every consumer. The cache's serialized ids are directly meaningful after restore — no per-consumer translation table.

### (iii) How the codec writes/reads in the LIBRARY id-space

**Generalize the `writeConstantValue` template to every write site in finding #2's blast radius.** The fix is mechanical: each writer that today dereferences `project.<registry>.get(id)` instead dereferences the cache's own snapshot `data.<table>.get(id)`. Concretely, the sites to convert (all reached from `encodeStdlibCacheBody`, StdlibCache.maxon:974):

In `MaxonDialect.maxon` (the leaf every writer funnels through):
- `writeMaxonType` named arm: `project.typeNames.get(id)` → `data.typeNameTable.get(id)` (MaxonDialect.maxon:890)
- interface arm: `project.interfaceRegistry.getName(id)` → cache interface snapshot (MaxonDialect.maxon:909)
- typeParameter payload: `project.typeParameters.getDeclaringTypeName(id)` / `.getName(id)` → cache typeParameters snapshot (MaxonDialect.maxon:927-928)
- genericInstance payload: `project.genericInstances.get(id)` / `project.typeNames.get(baseId)` → cache genericInstances + typeNameTable (MaxonDialect.maxon:938-939)
- function-type payload: `project.functionTypes.get(id)` → cache functionTypes snapshot (MaxonDialect.maxon:954)

In `StdlibCache.maxon`:
- `writeGenericInstanceRegistry`: `project.typeNames.get(baseId)` → `data.typeNameTable.get(baseId)` (StdlibCache.maxon:3400)

Mechanically these writers must take the `data` snapshot (or the relevant table) as a parameter instead of `project` — exactly as `writeConstantValue` was changed to take `stringTable StringArray` (Project.maxon:2618). The call sites that thread `project` into them (StdlibCache.maxon:1148, 1162, 1220, 1241, 1271, 1305-1308, 1317) thread `data`/the tables instead.

**Read side.** Readers re-intern by name/content into the destination project in serialized order (already the case: `readMaxonTypeNamed` → `project.typeNames.intern(name)`, MaxonDialect.maxon:979). Under model (b) the in-order replay *is* the read side, so reads already produce the stable prefix. The key invariant becomes: **the order of entries in the serialized snapshot == the order in which `intern` is called on read == the library-local id.**

**Net effect:** the codec is now fully project-independent. A worker with any `project` id-space can re-serialize its lazily-grown `data` because every write resolves through `data`'s own tables.

### (iv) How a consumer that lazily compiles a NEW library function appends ids stably

When a worker first touches a stdlib function not yet compiled, it compiles the body and may intern **new** ids (new generic instances, new type names) that were not in the cache snapshot. To keep persist consistent:

1. **Append-only at the library's tail.** New ids are interned after the existing library prefix `[base, base+N)`, at `[base+N, …)`. They are still *within the library's id-space* (the function belongs to the library), so they extend the same ordered snapshot.
2. **Capture the delta into `data` in intern order.** When the worker grows `data`, it appends the new entries to `data.typeNameTable` / `data.genericInstances` / etc. **in the order they were interned**, preserving the position == id invariant.
3. **Re-serialize writes the extended ordered snapshot.** Because the codec now resolves through `data` (§3.iii), the appended entries serialize correctly with their library-local ids.
4. **Determinism across workers.** Two workers that independently compile the same new function may intern in slightly different orders, producing different tail orderings. This is acceptable because: (a) the cache file is content-addressed/versioned and the *last writer wins* (a worker that loses the race simply re-reads the winning file next run), and (b) the *prefix* (the originally-cached entries) is identical in both, so no already-serialized id ever shifts. The only non-determinism is in the freshly-appended tail, which is self-consistent within each written file. (If strict cross-worker byte-identity of the tail is later required, sort the appended delta by a content key before serialize — flagged as an open decision in §8.)

---

## 4. WHAT CHANGES vs THE PLAN'S re-intern / `writerGidToProjectGid` MODEL

The plan (`library plan.md`) commits to the **re-intern** model: each library's cache stores writer-side gids, and on restore the consumer re-interns every entry and builds a per-`StdlibCacheData` `writerGidToProjectGid` translation table (plan lines 961-963), with `writerGidToProjectGid` explicitly "in-memory only — recomputed on decode, never serialized" (plan lines 256-257).

**This design REPLACES the translation machinery for the library prefix; it does not sit alongside it.**

- **`writerGidToProjectGid` becomes unnecessary for the library prefix.** Its entire reason to exist is that `readMaxonGenericInstance` interns args inner-first, so destination gid ordering diverges from the writer's (StdlibCache.maxon:561-571). Model (b)'s order-faithful flat replay (§3.ii.3) makes destination gid == cache gid by construction, so there is nothing to translate. The `writerGidToDataGid` second table (StdlibCache.maxon:586) and the decode identity-mirror trick (which re-keys `data.genericInstances` into the first reader's space, StdlibCache.maxon:2019-2048) also fall away — they exist only to recover writer ordering that model (b) never loses.
- **`translateMaxonType` / `translateNamedTypeId` / `translateCaptured*` collapse to identity for the prefix.** They were the "real remap on the in-memory warmup→worker path, no-op on the disk path" (finding #4). Under model (b) the disk path already produces the stable prefix, and the in-memory capture path can capture in library order too, so both paths produce identical ids — the translate family is identity in both and can be removed for library-owned ids.
- **What stays:** the **append step** (§3.iv) still interns new ids into the project, and user types are still appended after all library prefixes. Re-interning as a *mechanism* (calling `intern`) remains — what's removed is the *translation table* that re-maps cache ids to different project ids. The cache ids and project ids now coincide.
- **Caveat the design must honor (from the plan, lines 3130-3142):** the plan warns that keeping a cache gid verbatim is wrong because "gid numbering is assignment-order-dependent and is NOT stable across compiles." Model (b) does not violate this — it makes gid numbering *deterministic and stable* by fixing the replay order, which is the precondition the plan's warning assumed absent. The verbatim-gid hazard disappears precisely because the ordering is now pinned.

---

## 5. FORMAT / VERSION IMPACT

**Yes — the on-disk format changes, and `CACHE_FORMAT_VERSION` must bump** (currently `80`, StdlibCache.maxon:262; mismatch triggers regeneration, :1403-1404).

Two format changes:

1. **`typeNameTable` and `constantStringTable` must be serialized.** Today they are in-memory-only, rebuilt on decode from the destination project's interner (the identity-mirror trick, StdlibCache.maxon:1991-2014). That trick works *only* because the disk codec re-interns names as it reads, so `table[id] == name` falls out for free. Under model (b) the table **is** the authoritative id-space and must be written explicitly so the reader replays it in a fixed order independent of any incidental interning. This is the core format addition.
2. **`genericInstances` must be serialized as an ordered flat list** with payloads referencing library-local TypeNameIds and earlier-entry gids (§3.i). The current registry write (StdlibCache.maxon:3400, writing `project.typeNames.get(baseId)`) changes to resolve through `data.typeNameTable` and to guarantee capture-order emission.

**`writerGidToProjectGid` / `writerGidToDataGid` stay unserialized** (they are deleted, not serialized — §4).

Because the version bump forces full regeneration on first run after the change, there is **no migration concern** — old caches are simply rebuilt. The bump is cheap and safe.

---

## 6. WHERE IT SLOTS IN THE PLAN

**A refinement of Phase 3 (Tier-A meta format) with consequences for Phase 4 (the restore loop) and Phase 1 (`Library` fields).** Phase 3 is where the Tier-A serialization format and the `writerGidToProjectGid` serialize/recompute boundary are defined (plan lines 248-257). This design changes exactly that boundary: it serializes the id-space snapshots and deletes the translation table. Phase 4's `restoreLibraryMetadata` loop changes from "re-intern + translate" to "ordered replay into a stable prefix" (plan lines 961-967).

**Bring it forward NOW — it is a prerequisite, not deferred.** Rationale:

- It is **the** fix that unblocks worker cache-sharing (the entire motivation: persist-gate-off, 12.7x speedup discarded). Everything downstream that wants a populated shared cache depends on it.
- The plan's stated escape hatch (lines 84-86) only contemplates *lazy* restore; it does **not** anticipate an owned id-space. So this is a genuine new design decision that must be made before the persist gate can be relaxed.
- It is **architecturally pre-positioned**: `writerGidToProjectGid` is already an isolated, recomputed-on-decode indirection with one choke point (`translateMaxonType`), and the linker is name-keyed (plan lines 44-52), so the id-space and link-space are already decoupled. The change touches Tier-A metadata restore only, not Tier-C linking.
- The first domino — `writeConstantValue(data.constantStringTable, …)` (StdlibCache.maxon:1282) — is already landed. This design is the principled completion of a change already in flight.

Scope it as **stdlib-only first** (the cache that exists today), with the same mechanism generalizing to all libraries when Phase 4's multi-library loop lands (each library gets its own base offset; prefixes stack deterministically).

---

## 7. INCREMENTAL LANDING PLAN

Each step builds (`mcp__maxon-dev__build target: both`) and is spec-green (`mcp__maxon-dev__run_self_hosted_test`) before the next. Starting state: `constantStringTable` exists and `writeConstantValue` is already de-coupled (StdlibCache.maxon:1282).

**Step 1 — De-couple the remaining write sites to resolve through `data` snapshots (no format change yet).** Convert each blast-radius writer (§3.iii: MaxonDialect.maxon:890, 909, 927-928, 938-939, 954; StdlibCache.maxon:3400) to take the relevant `data` table instead of `project`, mirroring `writeConstantValue`. At this step the snapshots are still rebuilt on decode (identity-mirror), so behavior is unchanged on the disk path; the win is that **re-serialize no longer dereferences `project`**. Verify: existing spec suite green; additionally re-serialize a cache from a non-cache-build project in a test and confirm no panic.

**Step 2 — Serialize `typeNameTable` + `constantStringTable`; bump `CACHE_FORMAT_VERSION` to 81.** Write the tables explicitly; reader loads them directly instead of rebuilding via the identity-mirror. Drop the identity-mirror rebuild (StdlibCache.maxon:1991-2014) for these two. Verify: full regeneration on first run, then green; cache file size grows by the two tables.

**Step 3 — Serialize `genericInstances` as an ordered flat list; make read an order-faithful replay (RISKIEST).** Change `writeGenericInstanceRegistry` to emit capture-order with library-local baseIds, and change the reader to replay the flat list in order, interning each entry's args from already-replayed entries rather than recursively re-deriving. Verify (acceptance for this step): a generic-heavy program (`Array with String`, nested `Dictionary with (String, Array with Int)`) restored from a populated cache resolves the **same gid** the cache holds; no `__layout_*` mislabeling; **no memory leak (exit code != 101)** — the original `writerGidToDataGid` failure was exactly an element-walk leak (StdlibCache.maxon:582-585), so the mm-leak check is the precise regression guard. Use `mcp__maxon-dev__run_self_hosted_test` with `--mm-trace` on the generic-collection tests.

**Step 4 — Delete `writerGidToProjectGid` / `writerGidToDataGid` and the `translate*` family for library ids.** With Step 3's order-faithful replay, these are dead. Remove them and the decode identity-mirror trick. Verify: green; confirm by grep that no remaining read path consults the deleted tables.

**Step 5 — Implement lazy-append (§3.iv): new library ids appended at the tail in intern order, captured into `data` for re-serialize.** Verify: a worker that compiles a previously-uncompiled stdlib function and re-serializes produces a cache whose prefix is byte-identical to the input and whose tail carries the new function's ids.

**Step 6 — Relax the persist gate for non-cache-build projects.** Now that re-serialize is project-independent, allow workers/user builds to persist their grown cache. Verify the **acceptance test**:
- **Cold spec run grows the on-disk cache with compiled bytecode** — inspect `maxon-selfhosted/.maxon/cache/stdlib-*.mxc` size before/after a cold `run_spec_test`; it must grow beyond metadata-only (now carries `functions`/`wasmFunctions` bodies).
- **A 2nd cold run reuses it** — second run is measurably faster (approaching the 12.7x warm figure) and does **not** recompile stdlib bytecode (assert via `--log=compiler` that the cache-hit path is taken, no stdlib codegen).
- **Cross-worker sharing observable** — run the spec suite with parallel workers; confirm one worker's persisted bytecode is read by a later worker (cache file populated by worker A, hit by worker B), not recompiled per-worker.

**Riskiest step: Step 3.** It changes the one registry whose ordering was historically unstable, and a subtle ordering bug manifests not as a compile error but as a **silent wrong-gid → wrong layout → memory leak** (the documented `Array with String`/`__layout_Array_SlotState` failure, StdlibCache.maxon:582-585). Verification must therefore include the mm-trace leak check, not just pass/fail.

---

## 8. OPEN DECISIONS FOR THE LEAD

1. **Format bump: confirm 80 → 81 (recommend YES).** The two-table serialization + ordered gid list cannot be expressed without a format change. Forces one-time regeneration; no migration. Cheap. (Decision: confirm.)

2. **Replace vs. augment the translate machinery (recommend REPLACE for library ids).** §4 argues `writerGidToProjectGid`/`writerGidToDataGid`/`translate*` become dead under order-faithful replay. The conservative alternative is to *keep* them as a fallback and only short-circuit when the replay matches — but that retains complexity the design exists to remove. Recommend full replacement, with Step 3's mm-leak acceptance as the safety net. Lead to confirm appetite for deleting the machinery outright vs. a phased keep-then-remove.

3. **Scope: stdlib-only now vs. all-libraries now (recommend stdlib-only now).** The cache that exists today is stdlib. The mechanism generalizes to N libraries via stacked base-offset prefixes (each library's prefix appended after earlier libraries, separation by namespace per plan lines 953-957). Doing stdlib first keeps Step 3's risk contained to one prefix. Lead to confirm we land stdlib-owned-ids now and fold multi-library prefix-stacking into Phase 4 rather than blocking on it.

4. **Cross-worker tail determinism (recommend defer; last-writer-wins).** §3.iv: independently-compiled appended deltas may differ in tail ordering between workers. Recommend accepting last-writer-wins (prefix always identical, tail self-consistent) and *not* sorting the delta now. If a future requirement needs byte-identical caches across workers (e.g. content-addressed cache dedup), add a content-key sort of the appended delta as a follow-up. Lead to confirm last-writer-wins is acceptable for the spec-runner use case.

5. **`captureStdlibMetadata` in-memory path: capture in library order to make both paths identity (recommend YES).** Finding #4 notes the warmup→worker in-memory path genuinely remaps today. For model (b) to make translate-as-identity hold on *both* paths, `captureStdlibMetadata` (StdlibCache.maxon:2495) should snapshot in the same library order the disk codec uses. Minor, but the lead should confirm we unify both paths rather than keeping the in-memory path on a separate remap.

Relevant files for implementation: `c:\Users\Eric\Dev\maxon-libraries\maxon-selfhosted\Compiler\StdlibCache.maxon` (codec, snapshots, restore, `CACHE_FORMAT_VERSION` :262, persist gate), `c:\Users\Eric\Dev\maxon-libraries\maxon-selfhosted\Compiler\IR\Maxon\MaxonDialect.maxon` (`writeMaxonType` and payload writers :876-1014), `c:\Users\Eric\Dev\maxon-libraries\maxon-selfhosted\Compiler\Project.maxon` (`writeConstantValue` :2618, the interners :1784/1463).