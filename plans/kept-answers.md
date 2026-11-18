# The compiler answers every by-name, by-type and per-compile question from one kept, keyed answer, and a ladder per program dimension shows it

Class issue: #308 · Priority: P1

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. A `deferred` stage is skipped. The plan lands once, after its
last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | A dimension table and one driver print per-phase rung ratios for every program dimension the compiler indexes by |  |  | Is the dimension sweep an instrument or a gate? (recommended: instrument, exit 0) |
| [ ] | 2 | `MemberRoster` is the only door to a declaration's member by name, and the raw name scan is no longer exported | 1 | 3, 4, 7 |  |
| [ ] | 3 | A record literal's labelled-field succession is answered from one per-literal count, never a layout walk |  | 2, 4, 7 |  |
| [ ] | 4 | One per-body mention index answers 'where is this name mentioned next' for every asker, allocating per distinct name |  | 2, 3, 7 |  |
| [ ] | 5 | `rowsOfBase` is the only door to the instances of a base; `RowSetQuery.dropDiverges` and its whole-registry scan are deleted |  |  |  |
| [ ] | 6 | The two recursive instance questions are kept per `GenericInstanceId`, so chains of nested instance aliases check in linear time (`/plan-and-land`) | 1 |  | Where do kept type answers live for a front-end worker? (recommended: in the signature settle, read-only afterwards; alternative: lazily, journaled as `deepCloneAnswers` is) |
| [ ] | 7 | One kept transfer verdict per (subject, kind) holds the promise reach, opaque-slot refusal, clone gate, blocker and strategy (`/plan-and-land`) | 6, [generics](generics.md) 7 | 2, 3, 4 |  |
| [ ] | 8 | An open generic chain of `sendable`-bounded instances is linear in every compile phase (`/plan-and-land`) | 7 |  |  |
| deferred | 9 | A std-pass worker builds each step's workspace once for its life and reuses it across functions |  |  |  |
| deferred | 10 | A backend worker owns its op variant facts and takes its ABI table `immutable`, with no per-worker clone |  |  |  |
| deferred | 11 | Whether a target provides every host facility is settled once per compile, not per function and spawn site |  |  |  |

## Problem

The compiler must stay linear in program size. It is not, in a family of places that share one shape: **a
question whose answer depends only on a key (a name, a value, a type, the target) is recomputed by every
asker, by walking a collection that grows with the program.** No one place owns the answer, so each new
asker writes its own walk, and each walk is found one at a time when a hand-built ladder happens to double
the dimension it scans. Three forms of the shape are in the tree:

**A. A member, a name or a mention is found by scanning.**
- `payloadSlotFor` scans a case's payloads per labelled argument; `witnessSlotsRequireMethod` scans an
  interface's transitive requirement list per ask; `declaredTypeNameOfField` scans a layout's fields per
  field-path step; `enumCaseDeclarationIn` scans case names. The generic `Array.positionOfName` extension is
  exported, so any caller can scan any name array (type parameters, associated types, generic bindings).
  `MemberRoster` already owns lookup by name for fields and cases.
- `Parser.literalSuccession` walks every field of the layout once per labelled field of a record literal.
- `Parser.restOfStatementNames` walks the tokens to the end of the statement per held cell read, while
  `nameIsReadAfter` answers the sibling question from a kept per-body map (`lastMentionsByBody`) that is
  keyed by the name's bytes, one upsert per identifier token.
- `dropDivergesOverTheRows` walks the whole generic-instance registry comparing base names, where
  `rowsOfBase` already indexes the registry by base.

**B. A transitive property of a type is re-walked from the root by every asker.** The share and clone gate
keeps its answer per aggregate name (`memoizedAggregateCascadeAnswer`), but only for the root of each walk,
and its neighbours keep nothing:
- `firstTypeParameterIn` / `firstTypeParameterInInstanceArgs` recurse through nested generic instances;
- `instanceConformerReduction` -> `arrayElementCarriesConformance` -> `arrayElementConstraintCheck` ->
  `conformerNameOfType` recurses through nested `Array` elements;
- `aggregateTransferWalk` asks `aggregateGraphHoldsAPromise` and `recordRestsAnOpaqueSlot` afresh on every
  call, and `Parser.transferWalkOfValue` is asked twice per `freeze` (`requireTransferGraphWalkable`, then
  `emitTransferGraphProof`);
- `aggregateNameSupportsDeepClone` reaches an interior type through `typeSupportsDeepClone`, past the memo,
  so a chain of N types asked once each walks N + (N-1) + ... fields;
- every instance-keyed entry (`instanceSupportsTransfer`, `instanceGraphHoldsAPromise`,
  `instanceTransferBlocker`) starts from a fresh `ClonerSet`.

**C. State that outlives one item is rebuilt per item.**
- `StdPassWorker.runSteps` builds every step's scratch (`workspaceFor`) per function, where the band driver
  `runStdPassesOverTheBand` builds one per step and reuses it across functions.
- The backend worker builds `OpVariantFacts.create()` per unit, and `BackendWorker.create` takes its ABI
  table `isolated`, so `BackendPool.begin` hands every worker `self.abi.clone()` of a table the pool and the
  worker both hold `immutable`.
- `targetProvidesEveryFacility` iterates `HostFacility.allCases` on every call; `blameTargetErrorsIn` calls
  it per function and per `async` spawn site. The answer depends on the target alone.

Form C is linear today (a constant per item); it is the same missing owner, so it is planned here.

### Measured on main (a compiler built at 08153166c)

| Ladder | Phase | Reading | Where a profile puts it |
|---|---|---|---|
| `typealias A1 = Array with A0` ... chain, each alias used (N = 200/400/800/1600) | `checkDeclarations` allocations | 298,853 / 859,381 / 3,060,422 / 11,782,461 | 55% under `arrayElementCarriesConformance` -> `instanceConformerReduction` (recursive), 23% under `firstTypeParameterIn` |
| `Box uses T`; `typealias B1 = Box with B0` ... chain (same N) | `checkDeclarations` allocations | 123,952 / 189,808 / 441,241 / 1,424,088 | 49% under `firstTypeParameterIn` / `firstTypeParameterInInstanceArgs` |
| N records `Ci` each holding `C(i+1)`, one `typealias Pi = Pool with immutable Ci`, `Pool` bounded `where T is sendable` (N = 100/200/400/800) | `checkDeclarations` allocations | 151,650 / 287,077 / 800,331 / 2,791,576 | 30% under `aggregateNameSupportsDeepClone` -> `typeSupportsDeepClone`, 25% under `aggregateReachesAPromise`, reached from `sendableBoundAnswer` -> `transferWalkOf` |
| `tests/ladders/genshare.sh <N> doors` (N = 200/400/800) | `parse` allocations | 2,487,674 / 3,331,026 / 5,978,339 | 29% under `parseFreeze` -> `transferWalkOfValue` -> `aggregateTransferWalk`: `recordRestsAnOpaqueSlot` 15%, `aggregateGraphHoldsAPromise` 14%; `emitTransferGraphProof` (the second ask) 15% |
| `tests/ladders/genmemberlookup.sh <n> fields` (n = 2048 ... 32768) | `parse` CPU | 0.385e9, 0.929e9, 2.863e9, 10.135e9, 42.812e9; allocations linear | `literalSuccession` 22.6% at n = 16384 (reading carried from #176) |

Linear is a difference between rungs that doubles per doubling; these grow about x4.

## Design

**The rule.** An answer that depends only on a key is computed once for the lifetime of its inputs, kept by
its owner, and reached through that owner. Per-item code never walks a collection that grows with the
program to find one thing.

1. **By name: `MemberRoster` is the only door.** Every roster of names a declaration owns (fields, cases, a
   case's payload labels, an interface's requirement slots, type parameters, associated types, generic
   bindings) is a `MemberRoster`, free at or below its scan ceiling. The raw `Array.positionOfName`
   extension stops being exported: it keeps two callers, `MemberRoster`'s scan arm and `positionOfArgLabel`
   (call arguments are bounded by `MaxAbiArgs`, the one named exception). A new hand-written scan through it
   then does not compile.
2. **By position in a literal: one count per literal.** A record literal counts the layout's helper-default
   fields once and keeps a running count of those it has filled; `literalSuccession` answers from the two.
   No layout field, so no library-cache codec moves.
3. **By mention: one per-body mention index.** One structure, built in one pass over the body, answers
   "where is this name mentioned next after token p" for both `nameIsReadAfter` and
   `restOfStatementNames`. It allocates per distinct name at most, never per identifier token.
4. **By base: `rowsOfBase` is the only door to "the instances of this base".** `RowSetQuery.dropDiverges`
   has no constructor outside the library-cache reader; the variant and `dropDivergesOverTheRows` are
   deleted and the generated codec regenerated in the same commit.
5. **By type: a kept answer per subject, keyed by the instance.** Each transitive type question has one
   entry that every asker, and the walk's own recursion, goes through, with the keeping rule
   `memoizedAggregateCascadeAnswer` already states: keep an answer that rests on declarations alone; keep
   nothing above an in-flight cut that belongs to an ancestor (`closeCascadeWalk`); keep nothing for a walk
   that read instance rows still being minted (`instanceWithTypeArgKind`). The key for a generic instance
   is its `GenericInstanceId`, never its base name. `transferWalkOf` returns one kept verdict per
   (subject, kind) that already holds the promise reach, the opaque-slot refusal, the gate, the blocker and
   the strategy, so a door that asks twice pays once.
6. **By lifetime: worker-lifetime and compile-lifetime state is built once.** A std-pass worker owns its
   step workspaces for its life; the per-unit tally is handed to the pass at the call instead of captured by
   the scratch, which is what lets the scratch outlive the unit whose reply carries the tally away. The
   backend worker owns one `OpVariantFacts` and takes its ABI table `immutable`. "This target provides
   every facility" is settled once per compile.
7. **A ladder per program dimension, in one table.** `tests/ladders/` gains a dimension table (one row per
   dimension: generator and mode, rungs, the phases to read) and one driver that runs every row and prints,
   per phase, the rung-to-rung ratios of allocations and of least-of-N CPU. A dimension with no row is a
   missing ladder, and adding a collection the compiler indexes by adds its row. This replaces finding a
   scan by the accident of which ladder someone built.

**What was read, and what it taught.**
- Go's `types2.Scope` (`vendor/go/src/cmd/compile/internal/types2/scope.go`): a scope is a lazily allocated
  map by name and nothing iterates it to answer a lookup. Adopted: the map is the only lookup door; a small
  roster stays an array (`MemberRoster`'s ceiling) because Maxon's rosters are ordered and mostly tiny.
- Go's `validType0` (`types2/validtype.go`): its memo of valid types was keyed by the uninstantiated generic
  and is disabled as unsound (go.dev/issue/65711), because validity depends on the type arguments. Adopted
  as point 5's key rule: a kept answer about a generic instance is keyed by the instance.
- rustc-perf's stress benchmarks are one program per dimension (wide structs, deep nesting, many
  associated items) read as a trend, with instruction counts as the stable column. Adopted as point 7's
  table. (Cited from knowledge of the project, not re-read for this plan.)

### Open questions

1. **Is the dimension sweep an instrument or a gate?** The `optimize` skill rules that `scale-test` has no
   verdict and no gate.
   - *Instrument (recommended):* the driver prints ratios and exits 0; `/land`'s optimization step reads the
     rows a change can touch. Costs nothing in flakiness; a bend is still caught only when someone reads it.
   - *Gate on the exact columns:* fail when an allocation ratio at the top rung exceeds a bound.
     Deterministic, but blind to a scan that allocates nothing (`literalSuccession` and the symbol-table
     scans are both allocation-linear).
   - *Gate on CPU:* sees everything, and fails on a loaded runner.
   A deterministic work column (instructions retired, on the Linux lane) would make a gate sound; that is a
   new runtime facility and its own design.
2. **Where do kept type answers live for a front-end worker?** A worker parses a file against an overlay and
   drops what it learned at the end of the parse, so an answer kept only in the overlay is paid once per
   file. Options: settle the answers in the signature settle, as `settledUniqueCause` and
   `settledLinearTypes` are (recommended: one settle, read-only afterwards, no journal traffic); or keep
   them lazily and journal them as `deepCloneAnswers` is.
3. **Who holds the live-bindings-by-value index (#295)?** Recommended: the ownership class. The index is the
   move ledger's (#30, reading H-9), it is built by #235's H1 change, and it closes #297 by construction.
   This issue asks only that #295's acceptance include deleting `Scope.allVars()`, so no later caller can
   walk the symbol table.

## Stages

Each stage is one `/land` and one commit. Stages 2, 3, 4 and 7 edit different functions of the `Parser*`
files; they run one at a time unless their areas are confirmed disjoint at assignment.

1. **The dimension table and its driver.**
   - Changes: `tests/ladders/` gains the table, the driver, and the generators for the dimensions no script
     covers and no later stage adds: nested generic instance aliases (`Array with` chain, user-generic
     chain) and the `sendable` chain. `README.md` documents reading the driver's output, and says a
     hand-run ladder pins `MAXON_HASH_SEED` and `MAXON_HASH_TIER`.
   - Area: `tests/ladders/`.
   - Depends on: nothing. Open question 1 decides its exit code.
   - Shown by: the driver's rows for the five ladders above reading about x4.
2. **`MemberRoster` owns every member-by-name lookup** (#188).
   - Changes: the sites of form A's first bullet; payload labels, requirement slots, type parameters,
     associated types and generic bindings become rosters; `enumCaseDeclarationIn` calls `hasCase`;
     `declaredTypeNameOfField` calls `indexOfField`; `Array.positionOfName` is no longer exported. The
     requirement scan reported in `ParserContainers` is located and routed. The comments #188 lists as
     false are corrected. A roster added to a cached layout regenerates the library-cache codec.
   - Area: `Project.maxon` (rosters, layouts), `IR/Maxon/IrInterface.maxon`, `ParserStaticCalls.maxon`,
     `ParserTypeDeclarations.maxon`, `ParserDeclarations.maxon`, `ParserOwnership.maxon` (the two
     `extensionAssociated` lookups), `Parser.maxon`, `ConformanceCheck.maxon`, `SemanticCheck.maxon`,
     `SignatureIndexSettle.maxon`, `Lsp/LspDeclaringFile.maxon`, `tests/ladders/genmemberlookup.sh`.
   - Depends on: stage 1 for its table rows only.
   - Shown by: new modes `genmemberlookup.sh payloads|requirements|typeparams` bending before and linear
     after; the raw `positionOfName` has exactly two callers; no diagnostic moves.
3. **A record literal's succession is one count** (#176).
   - Area: `ParserOwnership.maxon` (`literalSuccession`), `ParserExpressions.maxon` (the literal's field
     loop).
   - Depends on: nothing.
   - Shown by: `genmemberlookup.sh <n> fields` `parse` CPU linear at n = 2048 ... 32768; the hold specs and
     `ir-specs` pins unchanged.
4. **One per-body mention index** (#238).
   - Changes: design point 3. First attribute #238's allocations (by tag or profile) on a compiler with and
     without the lending-match retain rule, since the source was attributed by reading only.
   - Area: `Parser.maxon` (`LastMentionByName`, `lastMentionsByBody`), `ParserControlFlow.maxon`
     (`nameIsReadAfter`, `lastMentionsFrom`), `ParserOwnership.maxon` (`restOfStatementNames`).
   - Depends on: nothing.
   - Shown by: the standard ladder's `parse` allocations falling by the per-identifier term (about 80,000
     at rung 0 and 500,000 at rung 5 when the rule was added); a ladder of one statement with K held cell
     reads reading linear.
5. **`rowsOfBase` is the only door to a base's instances** (#152).
   - Area: `SignatureIndex.maxon` (`RowSetQuery`, its generated codec), `SignatureIndexGenericTypes.maxon`.
   - Depends on: nothing.
   - Shown by: the build; the codec check.
6. **Instance-keyed kept answers for the two recursive instance questions** (#236).
   - Changes: "is this instance built on a type parameter" and "does this `Array` instance carry its
     conditional conformance" are kept per `GenericInstanceId` under design point 5.
   - Area: `SignatureIndexInstances.maxon`, `TypeCycleCheck.maxon`; `SignatureIndex.maxon` for the kept
     columns and their codec.
   - Depends on: stage 1 (ladders); open question 2. `/plan-and-land`.
   - Shown by: both chain ladders' `checkDeclarations` allocations and CPU linear at N = 200 ... 1600;
     `array-conditional-conformance-withheld` and the type-cycle specs unchanged.
7. **One kept transfer verdict per subject** (#65, #268's chain).
   - Changes: design point 5 for `transferWalkOf`, the promise reach, the opaque-slot refusal and the clone
     gate's interior nodes, by aggregate name and by instance.
   - Area: `SignatureIndexGenericTypes.maxon` (the transfer, clone and promise walks), `SignatureIndex.maxon`
     (kept maps, codec), `ParserPromises.maxon` (`transferWalkOfValue`'s two askers).
   - Depends on: stage 6 (the keeping rule and where answers live are settled there); #276 lands first, so
     the kept answer is taken after one resolver decides which parameters a type mentions. `/plan-and-land`.
   - Shown by: `genshare.sh <N> doors` `parse` allocations linear at N = 100 ... 1600; the `sendable` chain
     ladder's `checkDeclarations` linear at N = 200 ... 1600; `sendable-bounds`, `regions-bounds` and the
     share-door specs unchanged.
8. **The open generic chain is linear in every phase** (#268's open chain).
   - Changes: the ladder is an open chain `Bi uses T`, each holding a `B(i+1) with T`, and N instances of
     `Pool with immutable (Bi with T)` written in one `Outer` bounded `where T is sendable`. Before stage 7
     it read 151,973 / 307,769 / 923,778 / 3,371,984 allocations in `checkDeclarations` at N = 25 / 50 / 100
     / 200, 46 s in `signatures`, `deriveRuntimeNeeds` and `synthesizeBodies` at N = 200, and over 6 minutes
     at N = 400. The stage profiles it after stage 7, fixes each term that is an unkept answer under design
     point 5, and owns the acceptance whatever the mechanism: a term that is per-instance expansion is fixed
     here or carried, with this ladder, into #106's plan before this stage closes.
   - Area: decided by the profile; expected `SignatureIndexInstances.maxon`, `Runtime/MmRuntime.maxon`.
   - Depends on: stage 7. `/plan-and-land`.
   - Shown by: every phase's allocations and CPU linear at N = 25 ... 400.
9. **A std-pass worker owns its workspaces** (#20).
   - Area: `IR/Std/StdPassPool.maxon` and the scratch types' constructors in the Std passes they belong to.
   - Depends on: nothing.
   - Shown by: `maxon scale-test --rungs=6 --repeat=3` against a compiler built from the parent: the
     std-pass phases' allocations fall at every rung, and the per-rung gap to the band driver's cost no
     longer grows with the rung.
10. **A backend worker owns its op facts and shares its ABI table** (#22).
    - Changes: `BackendWorker.create` takes `abi immutable CalleeAbiTable`; `OpVariantFacts` is a worker
      field. The same shape in `CodeResult.maxon` and `RegisterAllocator.maxon` follows.
    - Area: `Targets/Shared/BackendPool.maxon`, `Targets/Shared/TargetLiveness.maxon`,
      `Targets/Shared/CodeResult.maxon`, `Targets/Shared/RegisterAllocator.maxon`.
    - Depends on: nothing. The compiler's own spawn then shares an immutable graph, so the seed chain
      (seed, first stage, second stage) is proved before landing.
    - Shown by: `git grep "abi.clone()" -- maxon-bin` empty at the spawn; `regalloc` allocations fall by
      about 6 per function on the standard ladder.
11. **A target's facility coverage is settled once per compile** (#51).
    - Area: `TargetFacilities.maxon`, `SemanticCheck.maxon` (`blameTargetErrorsIn`,
      `requireTargetSupportsLiveWitnesses`), `StdlibSource.maxon`, and where the per-compile fact lives
      (`Project.maxon`).
    - Depends on: nothing.
    - Shown by: the target-refusal specs unchanged; `semanticCheck` allocations on the standard ladder not
      rising.

## Members

- #188 — hand-written scans for a declaration's member by name beside `MemberRoster`; stage 2. Its site
  table and acceptance list are carried in stage 2.
- #176 — `literalSuccession` walks every field per labelled field; stage 3.
- #238 — the per-body last-mention map costs an allocation per identifier token; stage 4.
- #152 — `RowSetQuery.dropDiverges` scans every instance per ask and has no live constructor; stage 5.
- #236 — `checkTypeReferenceCycles` is quadratic on a chain of nested generic instance aliases; stage 6.
  Beside the path #236 names, `instanceIsBuiltOnATypeParameter` carries the user-generic chain.
- #65 — `parse` grows as doors x fields at `freeze`; stage 7. The clone gate itself is kept; the two
  predicates beside it and the second ask are not.
- #268 — a `sendable` bound over a chain of records (stage 7) and over an open chain of generic records
  (stage 8).
- #20 — every std-pass function builds its own scratch; stage 9.
- #22 — every backend worker is handed a clone of the ABI table, and op facts are built per unit; stage 10.
- #51 — `targetProvidesEveryFacility` walks every facility per function; stage 11.

## Related, not folded

- #295 — every "which live names bind this value" question walks `scope.allVars()`. The same rule, but its
  index is the move ledger's (#30, H-9), is built by #235, and closes #297; it is planned with the ownership
  work (open question 3). Its sites on main are `uniquePayloadSubjectsOf`, `poisonValueAliases`,
  `threadNameAliasedBy`, `requirePublishedValueNamedOnce`, `namesHoldingValueExcept`,
  `byReferenceParameterNameOf` and `payloadSourceOf`.
- #297 — a soundness question about a shadowed alias; closed by #295's index, not by a lookup door.
- #276 — the share walk and the `sendable`-bound walk disagree on a tuple alias's parameters: two answers
  to one question, a soundness defect. Stage 7 lands after it.
- #154 — factory inference runs twice per call and the two answers can differ: a correctness defect of
  generic inference, not a repeated walk.
- #142 — allocation count grows with the working directory's depth: path identity needs its own owner, and
  must keep physical identity for a file that is a link.
- #189 — resolving a contested top-level alias allocates per reference in table order: the hash-seed
  ordering class.
- #56, #57 — library-cache eviction and worker residency: cache policy, measured in wall time per compile.
- #106 — per-instantiation compilation of generic types raises the instance count every stage-6-to-8
  answer is keyed by; its plan reads those ladders.
- #284 — `joinedVariables` and the value-fact merge row record one relation twice: the ownership record.

Found by reading while planning, not measured, and not yet anyone's: `noteLentUniqueMoved` scans
`lentUniqueParams` per holder; `noteReadOfKeptArgument` walks the open kept sites per read;
`OwnedBindingIndex.removeCell` scans the cell list per removal. Each gets a row in stage 1's table, and a
fix where its row bends.
