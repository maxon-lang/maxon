# Every fact about a value rides one record: written where the value is minted, merged or carried round a loop, and read by every ownership and region rule

Class issue: #309 · Priority: P0

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. A `deferred` stage is skipped. The plan lands once, after its
last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | V1 | A value's source rows, hold and live-binding index are one record written at one door and read by one resolver (in progress as #235) |  |  |  |
| [ ] | V2 | Every question of which live names bind a value asks the live-binding index, not a walk of the symbol table | V1 |  |  |
| [ ] | V3 | A value's birth is the record's second rider, carried by every merge, rename, hand-back, literal and call result | V1 | E1 |  |
| [ ] | E1 | One function settles a merge edge by the one move rule: a var arm moves, an outliving let is co-owned, a linear value moves | V1 | V3 |  |
| [ ] | E2 | The arms of a merge are recorded once, as the record's view of the unique arms, read by the root walk and variablesOperandMayBe | V3, E1 |  |  |
| [ ] | W1 | One predicate answers whether a value is the whole of its region, and every door asks it and adds only its own action | V1, V3 |  |  |
| [ ] | O1 | One predicate answers whether a position opens its value or moves it, so a moving position is never checked as a one-call open | W1 |  |  |
| [ ] | B1 | One binding door judges the source of every binding form: a plain binding, a destructuring binding and a tuple assignment alike | V1 |  |  |
| [ ] | LP1 | A loop header's hold and sole-or-co-owned bit are provisional until the loop closes, then sealed and re-judged by a reliance ledger | V1, V3, E1 |  |  |
| [ ] | LP2 | The existing loop settles for moves, freshness, capability and cell-occupant exclusivity are riders and reliances of one ledger | LP1 |  |  |
| [ ] | C1 | A call takes a held value only when the callee lets nothing of it leave, read from a per-parameter mask over the callee's body | V1 |  |  |
| [ ] | C2 | Whether a parameter is fed comes from a per-parameter mask over the callee's value flow, not from token scans of its spelling | C1 |  |  |

## Rulings

- Open question 4: `try f() otherwise v` moves a `var v`, one move rule for `try` and `cond`. The stage that makes the change carries its `## Unreleased` entry and `Fix:`.

## Problem

The parser builds SSA and decides ownership and region standing in the same pass. Every fact about a value
(moved, sole or co-owned, held by a lending match, born inside a window, which variables a merge names, which
record a field read came out of) is a mark keyed by `ValueId`, and every derivation mints a new `ValueId`. So
each construct that derives a value has to remember, by hand, to carry each fact, and each construct that joins
control flow has to remember to join each fact. Four seams follow, and every member below sits on one of them.

1. **Derivation.** What a new value inherits from the values it was made from is carried by separate tables with
   scattered writers: `heldResults` (written only by `takeStandingOfInputs`), `windowBirths` (the
   `carryWindowBirth*` and `recordWindowBirth` sites), `latentBirths`, `joinedVariables`, the field-origin chain.
   `carryValueFacts` is the one single-source carry door, and it carries none of them. `promoteBorrowedMergeEdge`
   hand-carries four facts and drops the rest. `collectSubjectRoots` finds a match's roots from the subject's
   tokens and gives up at a call. Standing in a window is `standingInWindowAt`: the value's id against the
   window's floor, so a value derived inside a window counts as born there unless some site filed a birth.
2. **Merge edges.** "Does this edge move its source or co-own it" is two decision trees: `settleArmGive` (ternary
   and `match` arms: co-owns only an outliving immutable binding, moves everything else) and
   `transferFallbackToPhi` (`try … otherwise`: co-owns everything that is not linear or an owned isolated local).
   A binding merge joins capability and the owned bit in `joinEdgeInto`, and no other fact.
3. **Loop headers.** A loop body is parsed before its back edges exist, so a header phi takes each fact from the
   entry edge (`mintPhiForBinding`). Each fact that can differ on a back edge has its own "assume the entry
   answer, record who relied on it, re-judge when the loop closes" routine: moves (`LoopMoveScan.headerReads`,
   `settleBackEdgesOf`), freshness (`HeaderFresh.reliance`, `settleHeaderFreshLocals`), capability
   (`carriedHolders`, `settleCarriedCapabilities`), a cell occupant's exclusivity
   (`settleCellOccupantAcrossTheLoop`). The sole-or-co-owned bit of a header phi has none (`joinOwnershipInto`
   downgrades the phi after the body was parsed against it), and neither has the hold.
4. **Doors.** A rule about a value is applied where one construct is parsed, so its siblings miss it: "is this
   value the whole of its region" is spelled at `requireCrossesWhole`, `isolateIfPossible`, `parseMerge`,
   `slotStoreVerdictGiven`, `requireAssignmentFitsVariable`, `requireFieldStoreAllowed`,
   `openIsolatedPositions` and `declarePayloadBindings`; "may a binding alias this field" (`requireNoUniqueAlias`)
   is asked at the plain `let` and not at a destructuring; "does this position open or move its value" is
   `positionOpensItsValue` answering "opens" for positions that move. "Which live names bind this value" is
   seven walks of `scope.allVars()`, each over every binding of the function, keyed by name, so a shadowed
   binding is never reached.

Unconfirmed, and each stage's first step: the repros of #274, #283, #297 and #271 are read, not run; #288's
mechanism is a hypothesis; #246 and #249's write form are suspected holes.

## Design

**One value-fact record, written at four doors, read by every rule.** This applies #30's ruling 45 ("each
decision gets one point in the compiler") and its readings H-1 to H-10 and R-a to R-g; it reverses none of them.

1. **The record.** Each value has one record. Its riders are: its source rows (single-source, written by
   `carryValueFacts`; multi-source, written by the one multi-source door, H-1, H-5), the hold (H-2), the birth
   (#232's S1), and the arms of a merge (one relation in place of `joinedVariables` beside the merge row).
   `heldResults`, `windowBirths`, `latentBirths` and `joinedVariables` fold into it. No function outside the
   four doors writes a rider; a gate in `tests/cli`, of the kind that pins `VarInfo`'s setters to the move
   ledger, holds that.
2. **The four doors.**
   - *Mint*: `carryValueFacts` for a copy, a cell load or store, a part read and a capture.
   - *Multi-source*: `takeStandingOfInputs`, reached by every call result whatever its declared capability, and
     by swap, pop and remove results and a store into a holder.
   - *Merge edge*: one edge-settle function for the three value merges, and `joinEdgeInto` for a binding merge;
     both join every rider, and both write the promoted edge's rows through the mint door.
   - *Loop close*: below.
3. **One resolver and one index.** One resolver walks source rows, the holder chain and merge rows from an
   operand to its roots, and serves both the roots a match pushes and the held lookup (H-1, H-10). The move
   ledger holds the index of live bindings by value, shadowed bindings included, and the move log (H-9). Every
   "which live names bind this value" question asks it.
4. **Loop headers are optimistic and sealed at the loop's close.** A header phi's riders start at the entry
   edge's answer. A rule that reads a rider of a value derived from an unsealed header phi files a *reliance*:
   the rider, the answer it assumed, the value, the site. When the loop closes, each back edge's riders are
   joined into the header phi; a changed rider is propagated forward over the source rows of the values minted
   in the body, by a worklist in which each (value, rider) pair changes at most once, so the cost is linear in
   the body. Then each reliance is re-judged:
   - a *check* reliance whose assumption no longer holds is refused with the diagnostic the rule gives, naming
     the later iteration;
   - an *emission* reliance (a destructive move-out decided under "sole") is re-decided at the op it emitted.
   A reliance of an inner loop on an outer header stays filed until the outermost loop closes. The four existing
   settle routines become riders of this one ledger.
5. **Doors ask predicates.** One whole-region predicate returns whole, or the reason it is not (plain part,
   held, lent, outside the innermost window) (#245's W1, ruling 49.2, R-b). One position predicate answers
   "opens" or "moves" (#250's O1). One binding door (`declareInitializedBinding`) judges the source of every
   binding form.
6. **A fact about a parameter comes from the callee's body, not its spelling.** The per-parameter masks beside
   `movedUnique`, seeded per function and propagated over call edges, carry "lets a held value leave" (ruling 50
   point 3) and "is fed" (the callee takes ownership). The token scans for feed parameters go.
7. **Merge edges follow the one move rule** (`specs/moves.md`: the source's mutability decides). A `var` named
   by an arm or a fallback moves; an outliving `let` is co-owned; a linear value moves in its arm (R-e). So
   `try f() otherwise v` moves a `var v`, and a later read of `v` is E3102. That changes a program the latest
   release compiles, so the stage owes its `## Unreleased` entry (*Fix:* read the merged value, or `.clone()`
   the fallback).

**Prior art.** rustc indexes moves and places once (`MoveData`) and its analyses ask the index (already cited on
#30, H-9). The sealing in point 4 is the incomplete-phi rule of SSA construction from a single pass (Braun et
al., "Simple and Efficient Construction of Static Single Assignment Form"): a loop header's phi is provisional
until every predecessor is known. The propagation is optimistic sparse propagation on SSA (Wegman and Zadeck).
Maxon departs from rustc and Swift, which run ownership diagnostics as a separate pass over a finished CFG,
because its ownership layer emits retains and drops as it decides (reference counting with elision), and #30
rules every check local.

### Open design questions

1. **Stay in the parser, or move the check-only rules to a pass over the finished CFG?**
   - *Stay (recommended):* the record, its four doors and loop-close sealing. It is what H-1 to H-10 already
     decide, it is linear, and it keeps one place deciding both the diagnostic and the emitted retain or drop.
   - *A CFG pass:* loops become an ordinary fixpoint and no reliance ledger is needed, but the hold, standing and
     move rules would be decided twice (once to emit, once to check) unless emission moves too, which is a
     rewrite of the ownership layer to "emit every copy, then elide" and reverses H-1's "decided when minted".
   Revisit only if LP1's survey finds an emission reliance that cannot be re-decided at its op.
2. **How is an emission reliance settled (#274)?**
   - *Re-decide the op when the loop closes (recommended):* the move-out records its op; a header that joins to
     co-owned rewrites it to the retaining form. No program slows down unless it needs to.
   - *Pessimistic header:* every loop-carried `var` is co-owned in its body. Simple, and silently slower on the
     list-walk shape (`match cur … cur = n.next`).
   - *Refuse:* a compile error for a fact the author cannot see or fix. Ruled out.
   LP1 is a `/plan-and-land` for this reason; if the op cannot be rewritten in place, that is a question, not a
   choice between the other two.
3. **Is this issue new, or #235 rewritten?** New (recommended): #235 and #245 each centralise one decision, and
   #235's first task is planned and may be in flight under its number. Its commit names this issue instead.

## Stages

Every stage is one commit through `/land` unless it says `/plan-and-land`. All of them edit the `Parser*.maxon`
files, so two stages run in parallel only where their areas, listed by function, share no file. Where a stage
names region-model corpus ids, its acceptance has two parts: a filtered classification
(`python scripts/model-specs/generate.py --model <capmodel> --no-write --hands <families> --findings <families>`)
shows those ids in an agreeing bucket, and the regenerated `specs/model-*.md` are green. No stage accepts a
program the model refuses, so none owes a model round; a stage that finds it would is a halt.

### V1. The record: source rows, the hold, the index (P0)
- **Changes:** design points 1 to 3 for the hold. Roots come from the subject's value, not its tokens; one
  resolver; the hold is a rider on the carry door; `takeStandingOfInputs` is the one multi-source door and every
  call result reaches it; `parseSwap` marks its result taken-out and owned and sheds every slot-read mark; the
  live-binding index and move log in the move ledger (H-9 decisions 1 to 5); the rider-writer gate.
- **Area:** `ParserControlFlow.maxon` (`subjectRootsIn`, `collectSubjectRoots`, `collectCondArmRoots`,
  `collectMatchArmRoots`, `lentArmRootsOf`, the `for` source roots, `matchHoldIndexOf`, `heldResultIndexOf`,
  `heldIndexOf`, `valueIsHeldByAMatch`, `takeStandingOfInputs`, `releaseMatchSubject`);
  `ParserExpressions.maxon` (`parseSwap`, `noteCallResultCapability`, `argumentIsACleanInput`);
  `ParserContainers.maxon` (`requireNotHeldByAMatch`); `ParserOwnership.maxon` (`carryValueFacts`, `carryFact`,
  `factCrossesPath`, `ValueFact`); `ParserStateTypes.maxon` (`matchHolds`, `heldResults`); `RegionWindows.maxon`
  (`RootStack`, `SubjectRoots`, `HeldResultMap`); `Project.maxon` and `MoveSlots.maxon` (the ledger).
- **Do not touch:** the callee door (C1), the births (V3), the binding-arm guard (`enterLentArm`,
  `stayingRoots`).
- **Depends on:** nothing.
- **Discharges:** #235 repros 1, 2 and 4; #282.
- **Shown by** (`specs/regions-match-lend.md`, red first):
  - #235's repros 1, 2 and 4 refused (1: the root `a` is a read-only holder in a binding arm; 2 and 4: the
    crossing refusal for a held value);
  - a held swap result, pop result and call result, each refused at a send, a spawn, a reply and an isolated
    position (H-6: a door is chosen by the parameter's or return's declaration);
  - a rename then a crossing in the arm; a store into a holder then the holder sent; H-10's cases
    (`error.a-root-whose-field-alias-stored-a-held-value-is-not-sent`, its backward twin `match inner.u` with
    `send a`, a chain of two hops, and the accepted control);
  - a closure that captures a held root (H-3);
  - ruling 45.1's controls still accepted (`swap` and `pop` in a non-binding arm);
  - a matched subject that is a `var`, a `mutable` parameter, a self field, a local inside `modify`, an acquired
    state inside `acquire`, and an isolated local inside its own `modify` (the window's shadow binding resolves
    to the binding it shadows before held-ness is decided);
  - #282: `let x = swap h.u with Slot.empty` and `return swap h.u with Slot.empty` accepted, as the argument
    form is;
  - hands AP, AQ, FA25 and `hole_ap1`: no verdict leaves an agreeing bucket;
  - a ladder of one function with K union locals and K lending matches parses at about x2 per doubling; the
    standard ladder's allocations and self-compile CPU do not move.

### V2. Every "which live names bind this value" asks the index (P0 for #297, else P2)
- **Changes:** the walks of `scope.allVars()` ask V1's index: `uniquePayloadSubjectsOf`, `poisonValueAliases`,
  `threadNameAliasedBy`, `requirePublishedValueNamedOnce`, `namesHoldingValueExcept`,
  `byReferenceParameterNameOf`, `enclosingLoopHolderNameOf`. `functionMintedValueAliases` goes. The index gains
  its second key, payload bindings by scrutinee box, for `bindingIsAUniquePayloadOf` and `bindingReadsValue`.
  `bindingReadsValue` and `VarInfo.isLiveNameOf` become one predicate, or the change says why they differ.
- **Area:** those functions in `ParserPromises`, `ParserOwnership`, `ParserDeclarations`, `ParserExpressions`,
  `ParserContainers`, `ParserControlFlow`, `ParserStaticCalls`; the ledger in `Project.maxon`, `MoveSlots.maxon`.
- **Depends on:** V1.
- **Discharges:** #295, #297.
- **Shown by:** first a profile on the ladder below, confirming these scans carry the cost. Then #297's shape as
  a spec (`let b = a`; a block that declares its own `b` and consumes `a`; a read of the outer `b` after the
  block is refused as a read of any moved alias is), red first, or a pin if it already fails for another reason.
  The K-union-locals ladder goes from about x5.7 per doubling to about x2.

### V3. The birth is the record's second rider (P0)
- **Changes:** `windowBirths` and `latentBirths` fold into the record; every merge, rename, hand-back, literal
  and call result carries the birth through the record's doors; a part read takes its holder's birth even from a
  plain holder (R-d), value types carry none; a payload load files its field origin;
  `openedRootIsOutsideTheOpenBlock` is the one reader of "may this isolated value be opened here" and judges a
  lent part as well as an owned region. `effectiveCapabilityOf` is unchanged.
- **Area:** `ParserPromises.maxon` (`rootBirthOf`, `carryWindowBirth`, `carryWindowBirthFrom`,
  `recordWindowBirth`); `ParserControlFlow.maxon` (`noteLatentBirth`, `latentBirthOf`, the payload carry in
  `declarePayloadBindings`, `emitUnionPayloadLoad`'s origin); `ParserContainers.maxon`
  (`openedRootIsOutsideTheOpenBlock`, the latent promotion in `recoverAsIsolated`, `joinMergeCapability`'s
  carry); the other birth call sites; `RegionWindows.maxon` (`windowBirths`, `birthOf`);
  `ParserStateTypes.maxon` (`latentBirths`).
- **Do not touch:** the crossing doors (W1).
- **Depends on:** V1.
- **Discharges:** #232, #249.
- **Shown by:** first probe #232's merge and hand-back forms and #249's write form (`u1(b) then b.n = 3` inside
  `modify bx`); a form already refused gets a pin only. Then: `ro-union-iso-arm-read-modify` to agree-refuse
  (E3199; the repro is `modify bx { match u { u0 then break; u1(b) then let _ = b.n } }` with `u` a plain `let`
  union declared outside the window and an `isolated Bx` payload); laundering through a ternary, a match
  expression, `try … otherwise`, a rename and a hand-back, each refused with E3199; controls: a union built
  inside the window, `match h.u` inside `modify h`, an outer isolated payload sent or merged; findings AE and
  hands AH and FA16 keep every agreeing verdict.

### E1. One function settles a merge edge (P0 for #290 and #271 once reproduced)
- **Changes:** the ternary, the `match` expression and `try … otherwise <value>` settle each edge through one
  function that applies design point 7 and asks `borrowedValueOwesAHandoffReference`, never `valueIsManagedHeap`,
  for a borrowed give. A promoted edge writes its rows and riders through the mint door as a copy, so the root
  walk sees every arm. `pushMergedEdgeValue` goes (its callers call `pushEdgeArgs` and `joinEdgeInto`);
  `joinOwnershipInto`'s panic names itself; `HoldRetainPlan` is named for what it is, a retain plan.
  `joinEdgeInto` joins every rider.
- **Area:** `ParserControlFlow.maxon` (`settleArmGive`, `mergeEdgeMovesItsOwner`, `transferGiveToPhi`,
  `finalizeMatchMerge`, `pushMergedEdgeValue`, `pushEdgeArgs`, `joinEdgeInto`, `joinOwnershipInto`);
  `ParserOwnership.maxon` (`finishValueTry`, `transferFallbackToPhi`, `promoteBorrowedMergeEdge`);
  `Parser.maxon` (`HoldRetainPlan`).
- **Depends on:** V1. Shares `ParserOwnership.maxon` with V3.
- **Discharges:** #287, #290, #271, #279.
- **Shown by:** `var v = <owned>; let r = try f() otherwise v`, then a read of `v`: E3102, as
  `let r = v if c else w` gives (#287), with its `## Unreleased` entry; `match (a if c else b)` with one arm
  promoted roots both arms, and a held value does not cross from an arm (#290); in a generic type's body,
  `try items.get(i) otherwise Self.create(v).stored` with `stored` of type `T` reads live memory under the leak
  checker on every target (#271; a read of freed memory before the fix makes it P0); the leak checker stays
  clean over `specs/moves.md` and the merge specs.

### E2. The arms of a merge are recorded once
- **Changes:** `joinedVariables` folds into the record as its view of the unique arms; `variablesOperandMayBe`
  and the root walk read the same rows (R-c: one walker). First trace #288: why a cond over a by-reference
  unique parameter reads as a field or element read.
- **Area:** `ParserOwnership.maxon` (`noteJoinedVariables`, `carryJoinedVariables`, `recordJoinedVariables`,
  `joinedVariablesOf`, `variablesOperandMayBe`, `isAJoinOfVariables`, `moveJoinedVariables`);
  `ParserStateTypes.maxon` (`joinedVariables`).
- **Depends on:** V3, E1.
- **Discharges:** #284, #288.
- **Shown by:** `function take(p mutable Tk, t bool)` with `let c = p if t else p` then `print("{p.n}")`: E3102
  on `p` (moved by the merge), not E3214 (#288); the merge specs of rulings 45 T1, 47 and 49.3 unchanged.

### W1. One predicate answers "is this value the whole of its region" (P0 for #246)
- **Changes:** design point 5's whole-region predicate. A value is whole when it is a value; an owned isolated
  region; a fresh value or a relied-on fresh local (I3, I4); an owned `unique` or linear value of a closed type
  from any owned source (a local, an owned parameter, a consumed union's payload, a message or factory
  parameter); or a merge whose every arm is whole. Every door named in Problem point 4 asks it and adds only its
  own action.
- **Area:** `ParserContainers.maxon` (`requireCrossesWhole`, `recoverIsolated`, `slotStoreVerdictGiven`,
  `requireFieldStoreAllowed`'s clean test, `requireAssignmentFitsVariable`, `valueIsCleanIntoARegion`,
  `valueIsAnOwnedClosedLinearParameter`); `ParserExpressions.maxon` (`isolateIfPossible`, `parseMerge`, the
  whole-region test in `openIsolatedPositions`); `ParserControlFlow.maxon` (`ownsItsRegion` in
  `declarePayloadBindings`).
- **Depends on:** V1 ("not held" reads its predicate), V3.
- **Discharges:** #245, #246, #251.
- **Shown by:** first probe whether an owned local declared before an `acquire` block and sent inside it is
  moved for code after the block; if not, #246 is a confirmed hole. Then:
  - to agree-accept: hand-FA25 `o-uq-{r1,r2,rl}-{isoparam,isodecl,slotnew,slotset,merge}`,
    `o-lin-{r1,r2,rl}-{isoparam,isodecl,slot,merge}`, `c-window-inner`, `a-if-alt-arms`,
    `a-cond-same-isoparam`, `a-cond-same-isodecl`, `a-cond-same-merge`, `a-cond-nested-else`,
    `a-cond-plain-fresh`, `a-cond-linear-same-sent` (`useI(cond(r, r))` with `r` an owned `isolated`
    parameter); hand-AP `m1-cond-in-merge`; hand-FA23 `p3-ok-closed-payload` (`match m { m0(x) then useI(x) }`
    in `behave take(m Mp)`); hand-AH `m-acq-item` (`acquire s { match u { m1(y) then s.item = y } }`, #251);
  - `p3-ok-factory-closed`: its first error (`let q: isolated = u`, E3200) gone;
  - `w1-acquire-outer` to agree-refuse (E3200, naming the fix: send before or after the block, or declare it
    `isolated`);
  - every refused control of ruling 49.2 still refused.

### O1. One predicate answers "does this position open its value or move it"
- **Changes:** `positionOpensItsValue` answers "moves" for a `consuming self` receiver that is an isolated
  variable (ruling 41 part 1), a spawn's factory argument, and an isolated temporary in an unmarked position
  (ruling 27). Ruling 42's "Confirmed" refusals inside a window stay; `checkWindowReceiversKeepNothing` stays the
  judge of a consuming receiver in a window.
- **Area:** `ParserExpressions.maxon` (`positionOpensItsValue`, `nextOpenedPosition`, the head of
  `openIsolatedPositions`, `argumentsWhoseBirthTheResultCarries`); the handed-back check in
  `CapabilityCallCheck.maxon`.
- **Depends on:** W1 (shares `openIsolatedPositions`).
- **Discharges:** #250, #252.
- **Shown by:** to agree-accept: `recv-wrap-no-window`, `recv-chain-wrap2`, `recv-param-store`,
  `recv-param-store-inner` (`var v isolated = new Rz(..)`, then `v.wrap()`, `v.rewrap(new Rz(1))`, `v.put(bx)`,
  each a `consuming self` method), and `p3-ok-factory-closed` (`spawn Sf4(mkUT())` with
  `mkUT() returns isolated UT` and an unmarked `create(u UT)`); ruling 42's refused shapes pinned.

### B1. One binding door judges the source of every binding form
- **Changes:** the unique-alias check moves into `declareInitializedBinding`, so a plain binding, a
  destructuring binding and the target of a tuple assignment judge their source alike. Destructured elements
  stay lends; what goes unjudged is the tuple source. The stage audits a `for` over a field and a match binding.
- **Area:** `ParserTypeDeclarations.maxon` (`declareInitializedBinding`, `parseDestructuringBinding`,
  `declareDestructuredElement`, `requireNoUniqueAlias` and its callers, the tuple-assignment target).
- **Depends on:** V1 (its holder-chain cases are written on this shape and are re-pointed here).
- **Discharges:** #302.
- **Shown by:** #302's program (`let (inner, _) = crate.pr` where `pr` is a `(Bag, Count)` field and `Bag` holds
  a unique value) refused with E3214 at the destructure, as `let inner = crate.bag` is.

### LP1. A loop header's facts are sealed when the loop closes (`/plan-and-land`)
- **Changes:** design point 4, for the two facts that have no settle today: the hold and the sole-or-co-owned
  bit. The reliance ledger, the join of back-edge riders into the header phi, the forward worklist, the
  re-judging. Open question 2 is settled by this stage's survey.
- **Area:** `ParserControlFlow.maxon` (`mintLoopHeaderPhis`, `mintPhiForBinding`, `openLoopScan`,
  `noteLoopBackEdge`, `finishLoopMoves`, `declarePayloadBindings`' move-out); `ParserStateTypes.maxon`
  (`LoopMoveScan`); the record's readers in `ParserOwnership.maxon`.
- **Depends on:** V1, V3, E1.
- **Discharges:** #283, #274.
- **Shown by:** probes first. #283: inside an arm of `match h.u`, `var x = Slot.empty`, then a `while c` loop
  whose body calls `s.take(x)` and then does `x = swap h.u with Slot.empty`: refused at the `take`, naming the
  later iteration. #274: `var t = makeTy()`, a loop whose body does `match t`, moves a payload out, then
  `t = h.ty`: after two trips `h.ty`'s slot is intact, under the leak checker on every target (a nulled slot or
  a read of freed memory before the fix makes it P0). A scale ladder over nested loops stays at about x2 per
  doubling.

### LP2. The existing loop settles ride the same ledger
- **Changes:** moves (`headerReads`), freshness (`HeaderFresh.reliance`), capability (`carriedHolders`,
  `carriedDerivations`) and cell-occupant exclusivity become riders and reliances of LP1's ledger. No verdict
  changes.
- **Area:** `ParserControlFlow.maxon` (`settleBackEdgesOf`, `settleCellBackEdges`,
  `settleCellOccupantAcrossTheLoop`, `loopExitEdgeMark`); `ParserContainers.maxon` (`noteHeaderFreshLocals`,
  `settleHeaderFreshLocals`, `noteLoopHeaderPhisCarried`, `settleCarriedCapabilities`); `ParserStateTypes.maxon`.
- **Depends on:** LP1.
- **Discharges:** none; it closes the class at the loop seam, so a fact added later cannot miss its settle.
- **Shown by:** the loop specs (moves in a loop, W2 refill, fresh locals through a header, carried
  capabilities) unchanged; the standard scale ladder unchanged.

### C1. A call takes a held value only when the callee lets nothing of it leave (P0, `/plan-and-land`)
- **Changes:** ruling 50 point 3. One more per-parameter mask beside `movedUnique`, seeded per function and
  propagated over the existing call edges. A parameter leaves at a crossing door or an isolated position, a
  store outside its own graph, a throw, a coroutine lend, or a pass to a parameter that lets it leave; returning
  is not leaving. A callee with no summary lets it leave unless the parameter is `immutable`. The parser files a
  site wherever V1's predicate says an argument is held; the door after inference refuses with E3214, naming the
  parameter and where the callee lets the value leave. A plain parameter, a `mutable` parameter and a receiver
  alike. The survey decides whether the IR-level reach in `ParamSources` covers take-outs (swap and pop from a
  parameter's slot); if not, that is a question, not a second tracker at parse time.
- **Area:** `ParamSources.maxon` (the seed row); `SemanticCheck.maxon` (`PropagatedNeed`, `edgeCarriesNeed`);
  `CapabilityInference.maxon` (the facts column); `KeptArguments.maxon` (the door); `ParserExpressions.maxon`
  (`noteKeptArgumentSites`, `fileCallNotes`); `RegionWindows.maxon` (`CapabilityNotes` and its generated cache
  codecs).
- **Do not touch:** `SignatureIndex*`, `ConformanceCheck`.
- **Depends on:** V1.
- **Discharges:** #235 repros 3 and 5, which completes #235.
- **Shown by:** repro 3 (`held then got = popSend(a, s: s)`) and 5 (`held then got = sendIt(u, s: s)`) refused
  at the call; each callee form (function, method, closure, conformer, function value, interface method); a
  closure that captures a held root and is called in the arm; `box.push(held)` in an arm refused at the call
  with the callee's store named (H-8); to agree-refuse: hand-AP `h10-after-arm`, `h10-after-arm-wrapped`,
  `h11-two-arms`, `h11-two-arms-rev`, hand-AQ `l1-callee`, hand-FA25 `c-arm-new-holder-after`, `c-stash-direct`,
  `c-stash-push`; controls: `empty then fill(u)`, and `Array.pop` and `Slot.take` by their bodies; a scale
  ladder, since this adds a propagated mask.

### C2. Whether a parameter is fed comes from the callee's value flow (`/plan-and-land`)
- **Changes:** the token scans that find a constructor's or method's feed parameters
  (`consumedParamAfterColon` for `Self{f: param}`, `.push(param)`, `self.f = param`) go; the fact is a
  per-parameter mask from the body, beside C1's, so a store through a local or a conditional reassignment
  counts. The survey decides how a caller that parses before the callee's summary exists defers its "drop the
  promise temporary" decision (a marker resolved after inference, as hold markers are), and how this meets #196.
- **Area:** `ParserDeclarations.maxon` (`consumedParamAfterColon` and the feed scans);
  `ParserTypeDeclarations.maxon` (`noteFeedStored`); `ParserPromises.maxon` (`noteDroppedPromise`);
  `ParamSources.maxon`, `CapabilityInference.maxon`.
- **Depends on:** C1.
- **Discharges:** #278.
- **Shown by:** a generic `Holder uses Item` whose `create(item Item)` does `let copy = item` (or `var`) then
  `return Self{held: copy}`, called as `PromiseHolder.create(async plain(7))` with
  `typealias PromiseHolder = Holder with IntPromise`: compiles, prints `true`, exits 0, where it is E3213 at the
  `let h = …` line (`specs/promise-must-use.md`).

## Members

- #235 — a lending match does not hold an element-read subject or a taken-out value, in the frame (repros 1, 2,
  4) or through a callee (3, 5): V1, then C1.
- #282 — a swap out of an isolated union field is refused when bound or returned (E3214), accepted as an
  argument: V1.
- #295 — seven scans of the symbol table per consume, parse about x5.7 per doubling: V2.
- #297 — a shadowed alias of a consumed value is not poisoned: V2.
- #232 — a value made inside a window does not take the standing of what it was made from (merge, hand-back):
  V3.
- #249 — a payload lent by an outer plain union reads as born inside the window: V3.
- #287 — a `try` with a `var` fallback co-owns the `var` where a cond arm moves it: E1.
- #290 — a borrowed cond arm promoted at the merge loses its root: E1.
- #271 — a `try … otherwise` fallback of type-parameter type may point into a temporary it frees: E1.
- #279 — the `pushMergedEdgeValue` wrapper, the panic naming it, `HoldRetainPlan`'s name: E1.
- #284 — the arms of a value merge are recorded twice: E2.
- #288 — a cond over a by-reference unique parameter is refused as a field read (E3214), not as a move (E3102):
  E2.
- #245 — whether a value is the whole of its region is decided door by door (29 wrong refusals, 9 more with
  ruling 49.3): W1.
- #246 — an owned local declared outside an `acquire` block can be sent inside it: W1.
- #251 — the lone plain payload of an owned isolated union is refused at an acquired state's isolated slot: W1.
- #250 — a call position that moves an isolated value is checked as a one-call open: O1.
- #252 — a spawn handing an isolated temporary to an unmarked factory parameter is checked as a one-call open:
  O1.
- #302 — a destructuring binding aliases a field that holds a unique value: B1.
- #283 — a hold a `var` takes late in a loop body is not seen by an earlier read on the next trip: LP1.
- #274 — a loop body is parsed against the header phi's entry-edge ownership bit: LP1.
- #278 — a parameter stored through a local is not seen as fed (false E3213): C2.

## Related, not folded

- #233 — a linear local consumed after a by-reference pass (E3224), and its leak: the leak's mechanism is not
  known, so it keeps its own `/plan-and-land`; it runs after V1 and E1, which hold its files.
- #196 — a promise's ownership answer is "holds no unique": a type-level answer in the signature index, not a
  flow fact. C2 and it meet at the promise predicates; whichever lands second re-reads the other.
- #171 — a `SharedValue` element judged outside the sendable predicate: a type-level rule.
- #219 — an element read with `get` and one read from a `for` are given different capabilities: the mechanism
  is not traced. Probe it after V3; if the record's carry gives both reads one answer it closes there with a
  spec, otherwise it keeps its own fix.
- #277 — three retain and drop decisions each spelled twice: emission tables, not flow facts. E1 renames
  `HoldRetainPlan` only.
- #73 — `instanceOwnershipWordsDiffer` in the lowering: generic-instance drop words, its own mechanism.
- #101 — `castEmitsAnOp` beside the cast door: its own door.
- #238 — the match last-mention scan's allocations: a token scan of its own.
- #126, #127, #184, #213 — Backlog proposals that amend #30's rulings (24, 10, 18 and 23, 29 and 45.1); each
  owes its own model round. #184 builds on E1 and E2.
- #257 — the region model's MERGE-ARMS-ALT lift (R-a, R-f): model tooling.

- #235 is in progress under its own number and stays open until its change lands.
- #282 is in progress under its own number and stays open until its change lands.
- #284 is on the Backlog, not yet accepted: it stays open and is not folded until it is.
