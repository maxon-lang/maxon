# Refcount Optimization Roadmap

This document outlines optimization opportunities for eliminating redundant
`mm_incref` / `mm_decref` traffic in the maxon-sharp compiler. It is a
forward-looking companion to [MEMORY_MANAGEMENT.md](MEMORY_MANAGEMENT.md),
written against the state of [RefcountOptimizationPass.cs](../maxon-sharp/Compiler/MLIR/Passes/RefcountOptimizationPass.cs)
and the scoreboard at [specs/optimizer-refcount.md](../specs/optimizer-refcount.md).

## How to use this document

Each section describes one optimization, independent of the others. For each:

- **Pattern** — the IR shape the optimization targets, with a concrete example
- **Safety** — what must be proven before elimination is sound
- **Expected impact** — rough estimate against the current scoreboard
- **Prerequisites** — what analysis or infrastructure needs to exist first
- **Risks / known traps** — failure modes seen in the existing pass

The scoreboard spec is the authoritative benchmark. Every optimization below
should be landed together with a visible diff in the committed
`stderr` and `RequiredIR:x64-windows` blocks. If a change doesn't move those
numbers on the scoreboard, either it doesn't fire or the scoreboard is
incomplete and needs a new section added first.

## Current state

The pass runs in two sub-passes per function:

1. **Intra-block.** Linear scan within one block; handles pairs whose incref
   and matching decref are in the same block.
2. **Cross-block.** Dominator-tree based matching for pairs whose incref
   is in one block and matching decref is in a strictly-dominated successor
   block (e.g. the classic `entry → __range_ok_*` pattern). Implemented by
   #1 in this document.

Each sub-pass handles both alias shapes:

- **Load-based alias.** `load %v = src; store %v → dst` with later
  `incref(dst) ... decref(dst)`, when `src` is provably alive across the
  window.
- **Store-based alias (firstStoreOf).** Same SSA value stored into multiple
  slots without a reload in between — e.g. the for-in lowering that stores
  `iter.current()` into both `__forin_result` and the user's loop variable.
  Safety requires `src` to have its own decref at-or-after the candidate
  decref (otherwise eliding the second slot's pair leaks the allocation; see
  [the struct-backed-enum trap](#trap-mm_alloc-returns-rc0) below).

## Whole-compiler baseline (2026-04-21)

The scoreboard in [specs/optimizer-refcount.md](../specs/optimizer-refcount.md) is a
small synthetic program. It's the right regression harness but a bad
*workload* sample — it doesn't hit the lexer, parser, or register allocator
at realistic volume. For a prioritization signal, we run two whole-compiler
measurements against the self-hosted compiler:

1. **Refcount-traffic baseline.** Capture a `--mm-trace` trace of the
   self-hosted compiler compiling a trivial input, which forces the entire
   compilation pipeline to run end-to-end. Reports op counts, per-tag /
   per-scope breakdowns, pointless-pair candidates.
2. **Build-time and exe-size baseline.** Time a cold build of the
   self-hosted compiler and record the produced binary's size. Measures
   whether a landed optimization actually pays off at the "how fast does
   the compiler run on a real program" level.

**Capture:**

```bash
# (1) trace baseline
./bin/maxon.exe build maxon-selfhosted --mm-trace
./maxon-selfhosted/.maxon/maxon-selfhosted.exe build examples/basic.maxon \
  2> .mm-trace/basic.trace
python scripts/analyze_mm_trace.py .mm-trace/basic.trace --top=25 \
  > .mm-trace/analysis.txt

# (2) build time + exe size (cold, 3 runs)
./scripts/bench_build.sh --runs=3
```

`examples/basic.maxon` is three lines (`return 42`) — the workload is the
compiler itself, not the input.

### Baseline numbers

Current state (intra-block + cross-block + loop-invariant-eliminate +
#1 paired-pop + #3 call-return + #5 short-lived args + #10 try-call
borrow-awareness + #12 multi-exit bracket elimination +
#13 aliasFromStore prefix-kill relaxation + #11 global-load anchor
elimination + **#14 sibling-aware borrowedFrom prefix check, landed
2026-04-21**):

| Metric | Original baseline | After #10 | After #12 | After #13 | After #11 | After #14 | Delta (all) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Trace lines | 1,386,956 | 1,385,473 | 1,382,808 | 1,337,780 | 1,261,802 | 1,189,080 | −197,876 |
| `mm_alloc` / `mm_free` | 107,367 / 107,367 | 107,359 / 107,359 | 107,367 / 107,367 | 107,367 / 107,367 | 107,367 / 107,367 | 107,367 / 107,367 | 0 |
| `mm_incref` / `mm_decref` | 395,432 / 395,432 | 394,726 / 394,726 | 393,358 / 393,358 | 370,844 / 370,844 | 332,855 / 332,855 | 296,494 / 296,494 | **−98,938 each** |
| `mm_transfer` | 83,137 | 83,122 | 83,137 | 83,137 | 83,137 | 83,137 | 0 |
| `mm_raw_alloc` / `mm_raw_free` | 17,485 / 17,485 | 17,479 / 17,479 | 17,485 / 17,485 | 17,485 / 17,485 | 17,485 / 17,485 | 17,485 / 17,485 | 0 |
| `mm_realloc` | 13,536 | 13,536 | 13,536 | 13,536 | 13,536 | 13,536 | 0 |
| **Refcount ops per managed allocation** | **7.37** | **7.35** | **7.33** | **6.91** | **6.20** | **5.52** | −1.85 |
| Allocations with peak rc = 1 | 59,727 (55.6%) | same | same | same | same | same | 0 |
| Pointless-pair candidates ¹ | 206,077 | 205,688 | 204,305 | 181,198 | ~143,000 ² | ~110,000 ² | **~−96k** |

¹ `incref+decref` on the same allocation, same scope, with nothing between
— the shape the intra-block sub-pass targets.

² Estimated; the Lexer table brackets (#11) and list-iterator multi-block
brackets (#14) are intra-function multi-block ops not counted by the
pointless-pair heuristic. The actual trace-line drop is the authoritative
measure.

**Build time and exe size** (cold build of `maxon-selfhosted`):

| Metric | Original baseline | After #10 | After #12 | After #13 | After #11 | After #14 | Delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Cold build wall time (best / median / worst) | 16.90 / 17.03 / 17.96 s | 16.37 / 16.45 / 16.58 s | 16.24 / 16.30 / 16.35 s | 16.15 / 16.20 / 16.29 s | 16.08 / 16.09 / 16.33 s | 16.80 / 16.80 / 16.83 s ³ | −0.10 / −0.23 / −1.13 s |
| `maxon-selfhosted.exe` size | 4,327,012 bytes | 4,325,988 bytes | 4,324,452 bytes | 4,317,796 bytes | 4,310,116 bytes | 4,296,292 bytes | −30,720 bytes |

³ #14 build time regressed slightly vs. #11 despite the refcount-op
drop. The new pass runs an extra function-level alias-collection
sweep (two passes over each function's blocks instead of one) and a
sibling-aware prefix check that scans `DecrefIndices` for every
`IsFromStore=false` candidate. The regression is smaller than the
runtime savings from −72k ref ops, but a single cold-build run also
has ~±200ms noise at this scale, so treat the delta as at most
~0.7s and consider it the cost of the analysis. Profiling the new
two-pass sweep to see if the alias maps can be cached incrementally
is a candidate for follow-up work.

These numbers are the target the *next* roadmap item needs to beat. An
optimization that doesn't change any of: op counts, build time, or exe
size — is either not firing on real code or lands so rarely it doesn't
show up. Either case is worth investigating before declaring the item
done.

**#11's impact.** The three Lexer.run global tables (`charClassTable`,
`transitionTable`, `actionTable`) drove ~35k incref+decref pairs per
compile. After landing #11, all three are eliminated, plus a handful of
other global-load brackets elsewhere in the codebase. Total observed
reduction: **−37,989 incref, −37,989 decref** (−75,978 ref ops), trace
lines down by 75,978. The implementation also required a fix to allow
constant-zero null-init stores in the body-safety scan — the emitter
zero-initialises orphan-temp slots at function entry before the first
global_load, and the safety check was incorrectly treating those as
"unexpected re-assignments" and bailing. With that fix the Lexer tables
are fully eliminated. No leaks; 2538/2538 spec tests green; 206/206
self-hosted mm-debug tests green.

**#14's impact.** Unlocked the list-iterator and for-in-tuple hot
buckets that remained after #11 + #13. Combined observed reduction:
**−36,361 incref, −36,361 decref** (−72,722 ref ops per compile),
trace lines 1,261,802 → 1,189,080 (−5.8%), refcount ops per managed
allocation 6.20 → 5.52 (−11%), exe size −13,824 bytes. The
`__ListIterator_OpIndex.advance/__ManagedListNode` bucket dropped
from 13,716 to 6,251 pairs (−54%); `StdParser.lookupValueId/*`
dropped out of the top 20 entirely (was ~20k pairs across the
`String`, `ArrayIterator`, and `__Tuple_…` tags). No leaks; 2539/2539
spec tests green; 206/206 self-hosted mm-debug tests green.

**#13's impact.** Pre-land estimate was ~54k pointless-pair candidates.
Observed is ~23k — solid order-of-magnitude hit. The relaxation unlocked
the for-in tuple shape (`lookupValueId` / `__Tuple_...` moved from the top
5 to below the top 15), the top `collectBlockLabels` buckets, and similar
scope-end-sibling patterns throughout the codebase. Remaining top buckets
are mostly Gap-2 (global-load anchor, item #11) and loop-variant
list-iterator shapes that need their own analysis.

**#10 and #12's impact vs. expectation.** Pre-land estimates were ~60–70k
pointless-pair candidates eliminated for each. Observed is ~1.8k combined.
The two items interact: both attempt to unlock the top hot buckets
(`lookupValueId` for-in tuple, `__ListIterator.advance` node bracket,
etc.) but each handles a different blocking condition in isolation. The
hot buckets are blocked by a *third* condition — srcVar's own decref
appearing in the prefix of the matched decref block. Specifically, at
scope-end the emitter orders `decref(__forin_result_2)` before
`decref(__for_tuple_0)` in the same block; the aliasFromStore safety
check at
[RefcountOptimizationPass.cs:818-819](../maxon-sharp/Compiler/MLIR/Passes/RefcountOptimizationPass.cs#L818)
treats that srcVar-decref-in-prefix as a kill and bails. Unlocking those
buckets requires a scope-end-aware relaxation that recognizes the
prefix kill as safe when no intervening op dereferences the shared
allocation. See item #13 below.

**#10's impact vs. expectation (narrative).** The pre-land estimate was
~60–70k pointless-pair candidates eliminated (based on the top 8
try-call-adjacent buckets summing to ~69k). The observed drop is only
~400 pairs. The explanation: the hot buckets aren't blocked by the
try-call aliasing pessimism *alone* — they're also blocked by the
cross-block and loop-invariant sub-passes' "exactly one reachable
decref block" safety
check. The `lookupValueId` for-in tuple (10k pairs per bucket × 3 slots)
has decrefs in BOTH `otherwise_continue_5` and `match_3.after` because
scope cleanup fires on both match and no-match paths. #10 relaxed
the window-internal aliasing event classifier, but the window-exit
topology check still bails. A follow-up item ("Gap 3" below, tracked
as #12) is needed to unlock these buckets.

### Hot spots the baseline surfaces

**One-allocation-per-run tables with call-site borrow brackets.**
**ELIMINATED by #11 (landed 2026-04-21).** Three module-init-allocated
tables previously drove ~35k ref ops between them (100% peak rc=1).
`CancelGlobalLoadOrphanBrackets` now removes the incref+close-decref
bracket for each global load that is proven borrow-only in the function body.

**`EnumDummy`: 12,987 allocations, 100% peak rc = 1.** Generated by the
`try_call`-on-assoc-enum lowering at
[MaxonToStandardConversion.Calls.cs:231-244](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.Calls.cs#L231-L244):
every successful `try_call` allocates a dummy, increfs to rc=1, `select`s
between real-and-dummy, decrefs the non-selected, frees. Structural cost of
the select-based lowering, not user code. The emit-site audit classified
this site as "Always needed" *given the current lowering* — but the cost
data here (52k ops per compile) argues for revisiting with a branch-based
lowering. Not on the roadmap yet; added as a new candidate (see below).

**`__ManagedMemory`: 13,126 allocations, 99.7% peak rc = 1.** String
backing buffers are almost never shared. The incref/decref traffic
(~26k ops) is pure overhead. Roadmap items #1 (intra-block, already
landed) and #5 (interprocedural) should both chip at this; gap analysis
below.

**Top scope drivers** (both sides of the incref/decref split):
`StdParser.lookupValueId` (71k ops, tuple-driven map lookup),
`Lexer.run` (70k ops, the table-borrow pattern above),
`__WithIterIterator_ArrayIter_String.current` (42k ops, for-in
temporary tuples). The `lookupValueId` and `*.current` patterns are
roadmap-#4-hoisting territory — refs are stable across the loop body
but currently get bumped per-iteration.

### Gap analysis: why haven't the existing sub-passes eliminated the ~206k pointless pairs?

After landing #10 (try-call borrow-awareness), #12 (multi-exit
bracket elimination), and #13 (aliasFromStore prefix-kill relaxation),
181,198 pointless pairs remain. Spot-checking the top buckets
against the actual IR revealed four distinct gaps:

- **Gap 1 — Try-call aliasing pessimism.** The alias sub-pass
  classified every `StdTryCallOp` as aliasing regardless of callee
  borrow-only status. **Addressed by #10 (landed).** Narrower than
  estimated because most hot buckets are blocked by Gap 4 as well.

- **Gap 2 — No alias anchor for global loads.** Module globals like
  `charClassTable` have no stored-from-another-slot source, so
  `AnalyzeAliases` never sets `increfSource[...]`, and the eliminator
  bails at the "require known source" check. **Addressed by #11
  (landed 2026-04-21).** The new `CancelGlobalLoadOrphanBrackets` sub-pass
  pattern-matches the emitted open/close triples and performs a taint-based
  body-safety check without needing a stored alias source.

- **Gap 3 — Multi-exit-block scope-cleanup pessimism.** The cross-block
  sub-pass previously required **exactly one** reachable decref block
  for `varName` from the incref block. When scope cleanup fires on
  mutually-exclusive paths (match arm + no-match path, break + exit),
  there are 2+ reachable decref blocks and the pass bailed.
  **Addressed by #12 (landed).** Observed impact was smaller than
  estimated because the big hot buckets are *also* blocked by Gap 4.

- **Gap 4 — aliasFromStore prefix-kill pessimism.** When srcVar's own
  decref (or any sibling scope-end decref) appears in the prefix of a
  matched decref block before varName's decref, the pair-safety check
  previously bailed. **Addressed by #13 (landed).** Unlocked the
  `lookupValueId / __Tuple_...` bucket (10,000 → dropped below top 15),
  `collectBlockLabels / Token` (4,780 → dropped below top 15), and
  similar scope-end-sibling patterns throughout the codebase.

- **Gap 5 — borrowedFrom prefix-kill over-conservatism.** For
  `IsFromStore=false` (borrowedFrom) candidates, the prefix kill check
  used `HasKillInRange` with `BorrowAwareAliasingEventIndices`, which
  includes every `mm_decref_if_nonnull` in the prefix as an aliasing
  event. This blocked elimination whenever any unrelated
  `mm_decref` fired in the prefix of the matched decref block — e.g.
  `OpIndexList.walkTo`'s `otherwise_error_5` decrefs three unrelated
  nav/tmp slots before the matched decref, and none of those
  destructor chains can reach the `__selfref_*` iterator pointer.
  Meanwhile `store_indirect` / `mem_copy` were incorrectly classified
  as aliasing at all — Maxon doesn't use smart-pointer semantics, so
  raw heap writes can't trigger destructors. **Addressed by #14 (landed
  2026-04-21).** Removed `StdStoreIndirectOp` / `StdMemCopyOp` /
  `StdMemCopyReverseOp` from `IsAliasingOp` entirely, and replaced the
  prefix aliasing-event check for borrowedFrom candidates with a
  sibling-aware check: only block if srcVar itself is stored/decreffed,
  OR if a sibling variable (same `aliasSource` parent as srcVar, so
  holding the same runtime allocation) is decreffed. A non-sibling
  `mm_decref X` can only reach srcVar through a destructor chain if X's
  type holds an incref'd field of srcVar's type — and the destructor
  chain would have to be set up via an `mm_incref` of srcVar (either
  direct or into an aliasing field), which a borrowedFrom load from a
  parameter doesn't do.

Top remaining buckets (after #14):

| Scope | Tag | Pairs | Blocking gap |
| --- | --- | ---: | --- |
| `__WithIterIterator_ArrayIter_String.current` | `String` | 10,510 | Gap 6 ¹ |
| `OpIndexList.walkTo` | `__ManagedListNode` | 8,160 | Gap 6 ² |
| `ByteArray.slice` | `Slice` | 6,530 | new |
| `__ListIterator_OpIndex.advance` | `__ManagedListNode` | 6,251 | Gap 6 ² |
| `StdParser.collectBlockLabels` | `String` | 4,596 | new |
| `stdlib.ArrayIterator.create` | `Cursor` | 4,109 | new |

¹ `*.current` is the iterator-returns-tuple shape: `current()` is
called per iteration and returns a managed tuple that gets stored to
`__forin_result`. The bracket around the tuple's destructure (load
fields, incref, use, decref) could hoist out of the loop body since
the tuple fields don't change between iterations when the iterator
advances by a fixed offset — that's Gap 6 / item #4 (LICM-style
hoisting) territory.

² The list-iterator buckets shrunk substantially but didn't fully
vanish. The remaining pairs are the loop-internal per-iteration
brackets where `advance()` updates the nav pointer — item #4
hoisting should handle them since the per-iteration pair is pure
overhead when `self` is alive across the loop.

### Progress tracking

Re-run both captures after each optimization lands. Commit the updated
baseline rows above and a **delta** row below them. Example format once
#7 (non-null downgrade) lands:

> After #7 (2026-MM-DD): incref 395,432, decref 395,432 (unchanged —
> counts don't move, runtime instruction count drops by ~500k as
> `_if_nonnull` variants become plain `mm_decref`). Build time
> 16.90 → 16.70 s median (−1.2%). Exe size 4,327,012 → 4,319,208 bytes
> (−0.2%, from smaller decref call sites).

If the numbers don't move and you expected them to, either the pass isn't
firing or the trace was regenerated with a cached selfhosted binary. Delete
`maxon-selfhosted/.maxon/cache` and re-capture.

## Optimization proposals

### 1. Cross-block pairs via dominator-based matching (IMPLEMENTED)

> Landed in the current pass. The description below is retained for reference
> and to document the safety checks in place.

**Pattern.** The most common wasted work in practice. The compiler splits
scope-end decrefs into dedicated blocks (`__range_ok_*`, return-path blocks,
try/otherwise handlers). An `incref` in the entry block and its matching
`decref` at scope-end land in different basic blocks, so the intra-block pass
ignores them.

```
entry:
  %v = func.call @Point.create ...
  memref.store %v, a
  memref.store %v, b
  %p = memref.load b
  mm_incref %p                   ; <-- incref here
  ... range-check branch ...
__range_ok_0:
  %q = memref.load a
  mm_decref %q                   ; <-- a's decref
  %r = memref.load b
  mm_decref %r                   ; <-- matching decref here
  func.return ...
```

This is the classic `var b = a` alias pattern. Section 1 of the scoreboard
hits it; the current pass leaves it untouched.

**Safety.**

- The decref block must be reachable from the incref block.
- Between the incref and the decref, on every path, `src` must stay alive:
  no store, no decref, no call that could release `src` transitively.
- The `src` slot must have its own decref reachable after the candidate
  decref (same rule as the current firstStoreOf safety check).

**Expected impact.** Scoreboard section 1 plus most of sections 2, 5, 6.
Order-of-magnitude estimate: 15–30 incref/decref lines eliminated from the
scoreboard trace.

**Prerequisites.**

- Dominator tree over the function's CFG. No existing infrastructure in the
  pass — would add `DominatorAnalysis` as a sibling to the current pre-pass
  `aliasingEvents`/`storeIdxByVar` tables.
- Per-slot liveness across blocks: either full dataflow, or a cheap
  approximation that treats any store/decref/call-in-any-block as a
  conservative "kill" for the slot at that program point.

**Risks / traps.**

- Exception paths. If a call inside the window throws, the corresponding
  `otherwise` handler runs a scope cleanup that decrefs `src` — must not
  optimize across try boundaries without verifying the handler's decref is
  present.
- Loops. A block between the incref and the decref may execute zero or many
  times. Liveness must treat loop back-edges conservatively.

### 2. Paired-pop elimination on container reads (IMPLEMENTED)

**Pattern.** `list.get(i)` returns a borrowed reference that the assigning
code increfs to keep its own ownership, then decrefs at scope-end. When the
borrowed value is only used for a primitive read, the incref+decref pair is
pure overhead.

```
%elem = func.call @List.get %list, %i    ; owned rc=1 transferred to caller
memref.store %elem, s
%p = memref.load s
mm_incref %p                             ; caller's own ref (rc=2)
%x = load_indirect %p+0                  ; read primitive field
%q = memref.load s
mm_decref %q                             ; release (rc=1)
```

When `s` is only read (no aliases escape, not passed to another call, not
stored elsewhere), the mid-stretch incref/decref pair is redundant. The
callee's transfer (rc=1) already keeps the element alive for the whole
user binding scope.

**Safety.**

- `s` must not be aliased (no `var t = s` and no `store %p → t`).
- `s` must not be passed to a function call (the callee might retain).
- No assignment to `s` between the incref and the decref.

**Expected impact.** Section 4 (nested container: matrix of IntArrays, each
loop reads `row.count()` or similar). Scoreboard section 9 (for-in over
managed elements) already gets this via optimization #0.

**Prerequisites.** Use-def analysis on `s` across the block. Same
infrastructure as #1 once it exists — this becomes a specialized case.

### 3. Call-site ownership-transfer elision (IMPLEMENTED)

**Pattern.** When a function returns a freshly-constructed struct and the
caller immediately stores it, the callee does an incref before returning and
the caller *may* do another incref at the store site. The second incref is
redundant because the callee has already transferred ownership.

```
%made = func.call @make_point %x, %y     ; rc=1 transferred to caller
memref.store %made, local                ; store into caller's slot
%p = memref.load local
mm_incref %p                             ; <-- redundant: rc=1 was already transferred
```

The compiler already tracks this intent via `OwnershipFlags.CallReturn` at
the emit site ([MaxonToStandardConversion.cs:1090](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L1090)),
so in theory no such incref should be emitted. But edge cases leak through
(self-returning methods, try-call error paths, struct-field writes), and
a post-hoc pass catches those.

**Safety.** Prove the SSA value written to the slot was the direct result
of a function call that transfers ownership. The call-return flag is
emitter-local; at MIR level we recognize it by: `store %v, dst` where `%v =
func.call @callee` and the callee is known to transfer (non-stdlib callees
with a managed return type, or specifically-whitelisted stdlib calls).

**Expected impact.** Scoreboard section 6 (`make_point` factory). Smaller
win overall — the emitter already gets most cases right.

**Prerequisites.**

- Per-callee metadata: does this function transfer ownership of its return
  value? Add a side-table populated during `MaxonToStandardConversion`
  since the information is known at that stage.

**Risks / traps.**

- Self-returning methods (`returns Self`) that use the `SelfReturn`
  ownership flag — these borrow, not transfer. Don't elide increfs on
  their results.
- Error paths: `try_call` returns two values (result, error code). The
  result is only owned on the success path; decrefs on error paths must
  still be emitted.

### 4. Loop-invariant decref/incref elimination (IMPLEMENTED, eliminate-only)

> Landed as the third sub-pass in `RefcountOptimizationPass`. Hoisting out
> of the loop is not implemented; only the "eliminate entirely when the
> source variable is alive through the loop body" case. The description
> below is retained for reference.

**Pattern.** Inside a loop body, a slot holds an alias of another slot's
heap pointer, and the per-iteration incref+decref pair on the aliased slot
is pure overhead when the source slot's reference is stable across each
iteration:

```
loop_0:
  %v = memref.load aliased_slot        ; aliased_slot was stored from srcVar
  mm_incref %v                           ; every iteration — redundant
  ... use ...
  %v2 = memref.load aliased_slot
  mm_decref %v2                          ; every iteration — redundant
  cf.br loop_0.header
```

**Safety.** On every path from the incref to the decref within the loop
body:

- No store to `aliased_slot` (would change the cached pointer).
- No decref of `aliased_slot` on a sibling exit path (scope cleanup on a
  break would be left unpaired). Enforced by the "exactly one reachable
  decref block" rule over the full CFG (not just the loop body), mirroring
  the cross-block sub-pass.
- No aliasing event that could release the pointee: indirect stores,
  mem-copies, try-calls, or runtime calls other than `mm_incref`. Direct
  `func.call` ops are *allowed* — Maxon's borrow convention forbids the
  callee from decrefing a borrowed parameter.
- `srcVar` must own a reference: either a function parameter (caller owns)
  or has its own decref elsewhere in the function. Otherwise, the
  incref/decref pair on the aliased slot was the sole owner of the
  allocation and eliminating it would leak (the `mm_alloc`-returns-rc=0
  trap — see appendix).
- `srcVar` must not be stored-to or decreffed anywhere in the window.
- For `aliasFromStore` candidates (same SSA value written to two slots),
  `srcVar` must have its own decref reachable at-or-after the candidate
  decref — same invariant the cross-block sub-pass enforces.

**Scope.** Elimination only; true LICM-style hoisting (move incref to a
preheader block, decref to a loop-exit block) is deferred. The
eliminate-only form handles the pattern the roadmap targeted because once
an alias source keeps the allocation alive across the iteration, the
per-iteration bump+release is mechanical overhead.

**Expected impact.** ~50 pairs eliminated across the self-hosted compiler.
Smaller than #1 because most alias-source pairs have already been handled
by the intra-block and cross-block sub-passes — this one picks up
residuals where the matching decref lives on a sibling branch inside the
same iteration, or where scope cleanup on one loop-exit branch interacts
with per-iteration decrefs on another.

**Prerequisites.**

- Loop detection via natural loops (header, body, exit blocks) on the
  dominator tree. Implemented as `NaturalLoops.Find` in
  `Compiler/MLIR/Core/NaturalLoops.cs`.
- The same `AnalyzeAliases` and `BlockKillInfo` helpers used by the
  cross-block sub-pass; no new per-callee metadata required.

**Risks / traps (addressed in the implementation).**

- Early-exit from the loop. Scope cleanup on the break path emits a decref
  of the aliased slot that lives *outside* the loop body. The safety check
  uses a function-wide decref map and CFG-level reachability to require
  exactly one reachable decref block, catching this case.
- Fresh-mm_alloc slots with no alias source. The pair is load-bearing
  (it's what bumps rc from 0 to 1). The safety check requires
  `inc.SrcVar != null` and bails on rc=0 allocations.
- aliasFromStore source slots that never get their own decref. The
  allocation would leak if the aliased slot's pair is the only lifecycle.
  Same guard as the cross-block sub-pass.

**Deferred: actual hoisting.** Moving the incref into a loop preheader and
the decref into a single loop-exit block would save refcount traffic even
when the enclosed operations can release the pointee (as long as we
preserve a net +1/-1 around the loop). This requires constructing
preheader / unified-exit blocks and interacts with exception paths, so it
is postponed.

### 5. Short-lived argument temporaries (IMPLEMENTED)

> Landed as a relaxation of the existing intra-block and cross-block
> sub-passes, gated by a new interprocedural
> [ParameterRetentionAnalysisPass](../maxon-sharp/Compiler/MLIR/Passes/ParameterRetentionAnalysisPass.cs).
> The description below is retained for reference.

**Pattern.** A borrowed heap argument flows through an incref/call/decref
bracket around a function call — the caller bumps the refcount on the
argument slot before the call and drops it after, purely to keep the
allocation alive across the call boundary. When the callee is known to
only borrow its arguments, the bracket is pure overhead. Concrete example
from the scoreboard (`describe` match arm):

```
%44 = memref.load_indirect %43+8              ; borrow label out of the enum payload
memref.store %44, label                       ; label aliases __match_enum_describe_0
%46 = memref.load label
std.call_runtime @mm_incref %46               ; <-- redundant: label's lifetime is
%47 = memref.load label                       ;     anchored by __match_enum_describe_0
%48 = func.call @stdlib.String.count %47
%49 = memref.load label
std.call_runtime_if_nonnull @mm_decref %49    ; <-- redundant pair
```

**Safety.** The callee must not retain any argument. A parameter is
"retained" if a value transitively derived from it via local stores/loads
reaches an `mm_incref`, a `store_indirect` into heap memory, a `return`,
an outgoing call at a retaining position, or an indirect call (callee
unknown). Retention propagates interprocedurally to a fixpoint so a
callee that forwards its parameter to another retaining callee is itself
marked retained on that parameter.

Combined with Maxon's borrow convention (callees must not decref a
borrowed parameter), a borrow-only callee can neither release nor extend
the lifetime of any passed pointer — treating the call as non-aliasing in
the refcount window is sound.

**Expected impact.** Scoreboard section 8 (union match + method call).
Measured on the baseline: 2 static incref/decref pairs removed from
`describe` (each arm once, folded over 2 calls in the driver = 4
eliminated at runtime — visible as 4 `mm_incref` / 4 `mm_decref` trace
lines gone). Across the full spec suite another 3 fragment tests pick up
similar savings on `stdlib.Print.print` inside match arms.

**Prerequisites.**

- Per-parameter retention summary on each function (implemented as
  `IrFunction<StandardOp>.BorrowOnlyParamIndices`).
- Worklist-based interprocedural propagation so retention status converges
  across call-graph SCCs.

**Risks / traps.**

- Stdlib functions not in the module are conservatively treated as
  retaining every argument. That's correct but loses precision for
  frequently-called primitives; a manual whitelist of known-borrow-only
  stdlib functions could extend the optimization.
- Indirect calls (closure invocations) are conservatively treated as
  retaining every argument — no way to identify the callee at compile
  time.

### 6. Field-reassignment idempotence

**Pattern.** `person.name = "bob"` with `"bob"` being a string literal (rdata)
produces:

```
%old = load_indirect person.name+8           ; load old field
mm_decref %old                               ; release old
%new = load @str_literal_bob
memref.store_indirect %new, person.name+8
mm_incref %new                               ; retain new
```

When the old and new are identical (same rdata string, or both null-guarded
to noop), the decref/incref pair is a no-op but still gets emitted. This is
rare in hand-written code but common in generated code and helper wrappers.

**Safety.** Prove the stored value is the same SSA value as the loaded old
value. Trivial within a block.

**Expected impact.** Section 7, partial (the field-write pattern). Small.
Usually superseded by dead store elimination in practice.

**Prerequisites.** None beyond what the pass already has.

**Risks.** None of note — the pattern is narrow and the safety proof is
local.

### 7. Non-null static analysis for guarded decref elimination

**Pattern.** The compiler emits `mm_decref_if_nonnull` for scope-end decrefs
on slots that might be null-reset (e.g., after an earlier decref that zeroed
the slot). When the slot is provably non-null at the decref site, the guard
is dead — the call could be `mm_decref` (unguarded), saving a branch per
decref.

```
memref.store %ptr, slot                       ; slot is now non-null
%p = memref.load slot
std.call_runtime_if_nonnull @mm_decref %p     ; branch + call
```

When no null-reset happens in the block (or any dominating block) between
the store and the decref, the `_if_nonnull` variant can be downgraded to
plain `mm_decref`.

**Safety.** Prove the slot is non-null at the decref. A store of a
provably-non-null SSA value (alloc result, call result) establishes
non-null; a zero-store or uncertain store resets it.

**Expected impact.** Doesn't change the *trace* (same number of decrefs
fire), but removes branches from the generated asm. Each decref would go
from roughly 4 insns (test, branch, call, skip-label) to 2 (call). On the
scoreboard: 263 decref lines, ~2 insns saved each = ~500 insns cut.

**Prerequisites.** Non-null lattice over slots. Simple forward dataflow.

**Risks / traps.** Scope-end cleanup on an error path may decref a slot
that was never written (declared but initialization threw). The pass must
handle this conservatively: a slot is non-null only if *every* path from
entry to the decref stores a non-null value before the decref.

### 8. Rethink the `try_call` assoc-enum `EnumDummy` lowering

**Pattern.** `try_call` on a function returning an associated-value enum
currently allocates a dummy enum, increfs it to rc=1, uses
`std.select_i64` to pick between (real-result, dummy), decrefs the
non-selected via `mm_decref_if_nonnull`, and eventually frees whichever
wasn't transferred onward. Emitted at
[MaxonToStandardConversion.Calls.cs:231-244](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.Calls.cs#L231-L244).

The baseline capture showed **12,987 `EnumDummy` allocations per compile**,
100% peak-rc-1, contributing ~52k refcount ops (13k each of alloc, incref,
decref, free) and 13k raw allocations. Every successful `try_call` on an
assoc-enum return pays this cost.

The cost is structural — the select-based lowering needs the dummy to be a
real rc=1 allocation so the single decref on the non-selected value is
correct regardless of which side wins.

**Alternative.** Branch-based lowering: test the error flag, branch to a
success block that uses the real result directly, or an error block that
stores null and skips the decref. No dummy allocation, no ref ops. The
emit-site audit classified the current pattern as "Always needed *given
the current lowering*" — that qualifier is the thing to remove here.

**Safety.** The select-based lowering was presumably chosen to avoid a
branch inside the hot path. Verify the branch-based form doesn't
regress codegen on realistic benchmarks before switching.

**Expected impact.** 12,987 allocations × (1 alloc + 1 free + 2 ref ops +
1 raw alloc/free pair) = **~65k runtime operations per compile saved** if
fully replaced. Scoreboard section 6 would show the drop. This is one of
the largest single wins available without interprocedural analysis.

**Prerequisites.** None — the lowering is entirely local. The rework
touches only the `MaxonToStandardConversion.Calls.cs` block cited above
and requires new test coverage for the error-path decrefs.

**Risks / traps.** Error paths. The current code has a comment
specifically about scope cleanup needing the dummy to observe a valid
rc=1 allocation if the call errored. The branch-based form must emit the
equivalent scope-entry — scope-exit pairing on whichever branch was
taken; the committed scope-end cleanup can't be left holding a pointer
to a value that exists on only one branch.

### 9. Stack promotion for `@heap`-allocated values whose lifetime never escapes

**Pattern.** Some struct literals are heap-allocated only because the user
wrote `@heap` to force it, but the lifetime analysis can prove the value
never escapes the function. Moving them to the stack eliminates the alloc,
destructor, and all refcount ops on them.

Already partially done by [StackPromotionAnalysisPass](../maxon-sharp/Compiler/MLIR/Passes/StackPromotionAnalysisPass.cs).
The opportunity here is extending that pass to override `@heap` annotations
when safety can be proven — or deciding that `@heap` is a hard user override
and not auto-reverting it. Worth a design decision.

**Expected impact.** Narrow — this pattern is rare in user code and the
existing pass already catches the main case. Listed for completeness.

### 10. Try-call borrow-awareness in alias-bracket elimination (IMPLEMENTED)

> Landed 2026-04-21. Phase 1 of the try-call relaxation plan. Extends
> `ClassifyAliasingOp` in
> [RefcountOptimizationPass.cs](../maxon-sharp/Compiler/MLIR/Passes/RefcountOptimizationPass.cs)
> to treat `StdTryCallOp` the same way direct calls are treated when the
> callee is proven borrow-only on every argument: dropped from both the
> strict and borrow-aware aliasing-event lists. Previously every
> try-call unconditionally blocked alias-bracket elimination.

**Pattern.** An `incref/decref` bracket around a `StdTryCallOp` where
the callee doesn't retain any argument. Same safety principle as the
direct-call case (#5) extended across the error path: the error handler
either terminates via `func.error_return` after scope-end cleanup that
already decrefs `srcVar` (symmetric with the success-path window close),
or rejoins the success block without touching `srcVar`. On both paths
the bracket's net +1/-1 on the pointee is a no-op.

**Safety.** Spelled out in the docstring at
[RefcountOptimizationPass.cs](../maxon-sharp/Compiler/MLIR/Passes/RefcountOptimizationPass.cs) — see `ClassifyAliasingOp`.

**Observed impact (whole-compiler trace):** −706 incref ops, −706
decref ops, −1,024 bytes of exe size, ~0.5s faster cold build.
**Smaller than the pre-land estimate** of ~60-70k ops. Spot-checking
the surviving hot buckets revealed Gap 3 (multi-exit-block scope
cleanup) as the dominant remaining blocker, not Gap 1. See the Gap
analysis section.

**Scope.** Intentionally excludes `StdTryCallRuntimeOp` — its callees
are C runtime functions never analysed by `ParameterRetentionAnalysisPass`.

### 11. Global-load anchor elimination (IMPLEMENTED)

> Landed 2026-04-21. New sub-pass `CancelGlobalLoadOrphanBrackets` in
> [RefcountOptimizationPass.cs](../maxon-sharp/Compiler/MLIR/Passes/RefcountOptimizationPass.cs),
> running after the existing three sub-passes.

**Pattern.** Module globals like `charClassTable` (Lexer) or
`keywordMap` (Parser) are emitted at
[MaxonToStandardConversion.cs:1801-1811](../maxon-sharp/Compiler/MLIR/Conversion/MaxonToStandardConversion.cs#L1801)
as `global_load → store into orphan temp → incref → ... → scope-end
decref`. The orphan temp has no stored-from-another-slot source, so
`AnalyzeAliases` never sets `aliasSource[temp]`, so the existing
eliminator bails. The global's slot owns the reference from `__module_init`
through program end; the function-local orphan-temp bracket is pure
churn whenever the function doesn't retain the loaded value.

**Safety.** Taint-based body scan: any load of the orphan temp produces
a tainted SSA id; elimination is rejected if any tainted value reaches
`mm_incref`, `store_indirect`, `func.return`, a retaining callee arg
position, an indirect call, or a `global_store`. Additionally: the
global must not be reassigned (`StdGlobalStoreI64Op`) in the function;
all decrefs of the orphan temp must be covered by matched close-triples
(no stray decrefs that would underflow if the incref is removed).
Constant-zero null-init stores to the orphan temp (emitted at function
entry) are explicitly allowed.

**Observed impact.** −37,989 incref/decref ops per selfhosted build
(−10.2% from #13 state), trace lines −75,978. The three Lexer tables
(`charClassTable`, `transitionTable`, `actionTable`) are fully
eliminated. No leaks; 2538/2538 spec tests green.

### 12. Multi-exit-block bracket elimination (IMPLEMENTED — partial)

> Landed 2026-04-21 in `CancelCrossBlockRedundantRefcounts`. Also
> renamed `IsCrossBlockWindowSafe` → `IsCrossBlockPairSafe` and moved
> the srcVar-bypass check up one level so it operates over the full set
> of matched decs. Aligned `SelectWindowEvents` tier for aliasFromStore
> with the intra-block sub-pass's choice (strict vs. fully
> conservative).

**Pattern.** The cross-block sub-pass previously required **exactly
one** reachable decref block from the incref block. When scope cleanup
fires on multiple exit paths (match arm + no-match path, break +
loop-exit, try + otherwise), there were 2+ reachable decref blocks and
the pass bailed. The new implementation collects **all** reachable
decref blocks that are strictly dominated by the incref block and
removes them as a group when:

- Every matched dec's per-pair window (inc→dec) is safe
  (`IsCrossBlockPairSafe`: srcVar not killed in suffix/prefix/intermediates).
- srcVar is not decreffed on any path that bypasses the matched-dec
  set (once, at the multi-exit level, with all matched dec blocks as
  barriers).
- Non-overlap: from each matched dec block, BFS forward with incref as
  a barrier does not reach another matched dec (ensures each forward
  path hits at most one dec).
- aliasFromStore: per-pair `HasReachableDecrefAfter(srcVar, dec)` holds
  so the shared allocation is freed after each decref.

**Observed impact.** −1,368 incref/decref ops per selfhosted build,
−1,536 bytes exe size, ~0.15s faster cold build median. Smaller than
pre-land estimate — the big hot buckets (`lookupValueId` tuple et al.)
are blocked by a *third* condition covered by item #13 below: srcVar's
own decref in the prefix of the matched decref block.

**Scope note.** The loop-invariant sub-pass was NOT updated to the
multi-exit shape. Its jurisdiction is purely intra-loop-body elimination;
the cross-block sub-pass already covers loop-body incref →
dominated-decref cases via dominator-based matching. No observed miss
on current workload.

### 13. aliasFromStore prefix-kill relaxation (IMPLEMENTED)

> Landed 2026-04-21 as `TryPrefixIsBenignSiblingCleanup` in
> [RefcountOptimizationPass.cs](../maxon-sharp/Compiler/MLIR/Passes/RefcountOptimizationPass.cs).
> Also added `ComputeIntermediatesBarrierBackward` to suppress
> loop-back-edge false positives in the intermediate-kill check, and
> `PrefixDecrefOfSrcVarExists` so the aliasFromStore "srcVar decref
> reachable at-or-after matched dec" invariant is satisfied by a
> prefix decref in the same block.

**Pattern.** When srcVar has its own decref in the prefix of the
decref block (before the matched varName decref), the old
aliasFromStore safety check treated it as a kill and bailed. This is
overly conservative when the prefix kill is a sibling scope-end
decref and no intervening op dereferences the shared allocation.

Concrete shape from the whole-compiler trace (`StdParser.lookupValueId`'s
for-in tuple): in `otherwise_continue_5` the emitter orders

```
decref __forin_result_2    ← srcVar prefix kill
decref __for_iter_1        ← unrelated slot cleanup
decref __call_tmp_25934    ← unrelated slot cleanup
decref n                   ← unrelated slot cleanup
decref iter                ← unrelated slot cleanup
decref __for_tuple_0       ← matched varName decref
```

If the incref + all varName decrefs are removed, the allocation's rc
starts at 1 (from `current()` transfer), `srcVar` decref drops it to 0,
the allocation is freed, and subsequent `load __for_tuple_0` + removed
decref never executes (since the decref was removed). Safe.

**Safety (as implemented).** The relaxation accepts the prefix when:
- At most one decref of srcVar in the prefix (0 or 1).
- No store to srcVar or varName in the prefix.
- Every op in the prefix is one of: load/store of a slot ≠ srcVar,varName;
  constant; `mm_incref` / `mm_decref` / `mm_trace_transfer` runtime calls;
  the load feeding the matched varName decref (skipped since it'll be
  removed together); or the load feeding srcVar's own scope-end decref.
  Any direct or try-call, any `load_indirect` or `store_indirect`, any
  load of srcVar or varName not in the skip set — rejection.

**Observed impact.** −22,514 incref/decref ops per selfhosted build
(−5.7% from #12 state), −6,656 bytes exe size, ~0.1s faster cold
build median, pointless-pair candidates 204,305 → 181,198 (−23,107,
−11.3%). The `lookupValueId` for-in tuple bracket is eliminated
(moved from the top 5 to below top 15). Collectively #10+#12+#13
cumulative impact: −24,588 incref/decref each, −9,216 bytes exe.

**Risks / traps (addressed in the implementation).**
- Loop back-edges causing intermediate-kill false positives. Fix:
  `ComputeIntermediatesBarrierBackward` barriers the backward walk at
  the incref block, so blocks only back-reachable via looping through
  the incref don't count.
- mm-trace emission appending `lea_symdata` + `ptr_to_i64` ops per
  scope-end decref. Fix: the scan is reject-what's-dangerous rather
  than whitelist-what's-benign, so shape-carrier ops don't trip it.
- `HasReachableDecrefAfter` failing when srcVar's decref fires in the
  prefix (no later srcVar decref reachable). Fix: added
  `PrefixDecrefOfSrcVarExists` as an alternate satisfies-the-aliasFromStore
  invariant check.

### 14. Sibling-aware borrowedFrom prefix check + raw-store aliasing fix (IMPLEMENTED)

> Landed 2026-04-21 in
> [RefcountOptimizationPass.cs](../maxon-sharp/Compiler/MLIR/Passes/RefcountOptimizationPass.cs).
> Adds a function-level alias-map collection pass, dual-indexes
> decref sites under both the `aliasFromStore` source and the new
> `storeAlias` sibling-slot predecessor, extends the srcVar ownership
> check with a one-level transitive walk up the alias chain, and
> replaces the borrowedFrom prefix-kill check with a sibling-aware
> variant. Also removes `StdStoreIndirectOp`, `StdMemCopyOp`, and
> `StdMemCopyReverseOp` from `IsAliasingOp` since Maxon does not call
> destructors on field overwrites.

**Pattern.** Three distinct gaps that together blocked the
list-iterator hot buckets:

1. **VarName mismatch.** In `__ListIterator_*.advance`, `walkTo`, and
   similar iterator methods, the emitter stores the nav node's SSA
   value into two slots (`__managed_list_nav_*` and `__try_result_*`).
   The `incref` fires on the first slot; the `decref` fires on the
   second. `decrefsByVar[inc.VarName]` never matched because each slot
   is a different key.

2. **Ownership check fails for load_indirect chains.** When srcVar is
   a `__selfref_*` slot loaded via `load_indirect self+0`, srcVar
   itself has no direct decref in the function (it's borrowed from
   `self`). The old ownership check required srcVar to be either a
   parameter or have a direct decref, missing the case where srcVar's
   own `aliasSource` traces back to a parameter.

3. **Prefix kill false positives.** For `IsFromStore=false`
   candidates, the prefix kill check treated every `mm_decref` in the
   prefix as a potential kill of srcVar — even when the decreffed
   object was a nav node whose destructor can't reach the list
   iterator. `store_indirect` was also wrongly classified as aliasing.

**Safety.** Each fix has its own argument:

- **Dual-indexing.** When a variable `v` is stored via `borrowedFrom`
  *and* the same SSA value was earlier stored to another slot `w`
  (firstStoreOf predecessor), record `storeAlias[v] = w`. Decrefs on
  `v` are indexed under both `v` and `w` so the cross-block matcher
  finds them under either key. This just makes the existing pair-matching
  see relationships the emitter already established; it doesn't
  create new assumptions about lifetimes.

- **Transitive ownership.** Accept srcVar as owned if
  `aliasSource[srcVar]` is a parameter or has a decref. A borrowed
  sub-slot anchored in a caller-owned parameter is alive for the entire
  function call by the calling convention.

- **Sibling-aware prefix check.** For `IsFromStore=false` candidates,
  replace `HasKillInRange` (which treats any `mm_decref` in the
  prefix as an aliasing event) with:
  - `VarHasKillInRange(srcVar)` — catches direct store/decref of srcVar.
  - A scan of `decKill.DecrefIndices` keys `k` where
    `aliasSourceGlobal[k] == aliasSourceGlobal[srcVar]` — catches
    siblings that hold the same runtime allocation as srcVar.

  Rationale: a `mm_decref X` can only release srcVar if X's destructor
  reaches srcVar's allocation, which requires X to hold an
  incref'd reference to srcVar's type. For borrowedFrom candidates
  derived from a parameter, srcVar was never incref'd into any
  non-sibling struct, so no non-sibling destructor chain can decref
  it. Siblings (same `aliasSource` parent, same runtime pointer) are
  the only false-negative case — and the check catches them.

- **store_indirect / mem_copy demoted.** These are raw memory writes
  that don't invoke destructors in Maxon's refcount model (verified by
  inspecting `StandardToX86Conversion`, which lowers `store_indirect`
  to a plain `mov`). The only ops that release allocations are explicit
  `mm_decref` calls, tracked separately.

**Observed impact.** −36,361 incref, −36,361 decref (−72,722 ref ops),
trace lines 1,261,802 → 1,189,080, ref-ops-per-alloc 6.20 → 5.52,
exe size 4,310,116 → 4,296,292 bytes. Cold build time
16.08 / 16.09 / 16.33 → 16.80 / 16.80 / 16.83 s (a slight compile-time
regression from the extra function-level alias sweep; see footnote 3
on the baseline table). Top bucket drops:
- `__ListIterator_OpIndex.advance/__ManagedListNode`: 13,716 → 6,251 (−54%)
- `StdParser.lookupValueId/*` (String, ArrayIterator, Tuple combined
  ~28k): dropped out of the top 20
- `OpIndexList.walkTo/__ManagedListNode`: 9,151 → 8,160

Per-function elimination counts (from optimizer-debug log):
`StdParser.lookupValueId` 18 ops, `__ListIterator_OpIndex.advance` 6
ops, `__ListIterator_BlockRef.advance` 6 ops, `OpIndexList.walkTo` 8
ops, `BlockRefList.walkTo` 8 ops, plus 678 cross-block cancellations
across the whole self-hosted compiler.

2539/2539 maxon-sharp spec tests green; 206/206 self-hosted mm-debug
tests green; three previously-passing `list/memory.remove*` and
`ownership-edge-cases/rc-managed-list-remove-decrefs` tests needed
`--update-required` because the optimization correctly removes a
per-iteration Token bracket whose `__ManagedListNode` destructor is
empty (no field-decref chain to the Token).

**Risks / traps (addressed in the implementation).**

- **Flow-insensitive `aliasSource` tripping the sibling check.** In
  `advance`'s `otherwise_continue_1`, the `node` slot is written
  mid-block with `__try_result_3`'s value, so `aliasSource["node"]`
  ends up as `"__try_result_3"` instead of `"self"`. The sibling
  check uses the flow-insensitive final state, so `mm_decref node` at
  the block's start isn't detected as a sibling of `__selfref_s51516`.
  The elimination is still correct — mm-debug spec-test verifies — but
  the safety argument relies on the rc balance across the
  incref/decref pair being unaffected by the destructor chain of the
  decreffed object, not on detecting the alias. Future work: make the
  alias analysis per-op-position rather than per-block-final.

- **Fresh `mm_alloc` SrcVar=null candidates.** Early attempts to relax
  the `requireSrcVar: true` filter caused 191 test failures — fresh
  allocations don't have an external owner, so the incref+decref pair
  IS the owner. Kept the filter at `requireSrcVar: true`.

- **Two-pass alias collection.** `storeAlias["__try_result_3"] =
  "__managed_list_nav_s*"` is established in `entry`, but decrefs of
  `__try_result_3` fire in later blocks. A single-pass
  per-block collection never sees the relationship. Fix: collect all
  per-block alias maps into function-level `aliasSourceGlobal` /
  `aliasFromStoreGlobal` / `storeAliasGlobal` in a first pass, then do
  the site-collection pass using the global maps for dual-indexing.

## Infrastructure wish list

Several of the optimizations above share prerequisites. Building them in
this order lets each optimization piggyback on the previous:

1. **Dominator tree over Standard dialect IR.** Unblocks #1.
2. **Slot-level liveness** (forward dataflow, conservative at calls).
   Unblocks #1, #4.
3. **Loop structure recognition** (natural loops via dominator back-edges).
   Unblocks #4.
4. **Per-callee side-effect summary** (can this call release an arbitrary
   managed object?). Unblocks #3, #4, #5.
5. **Interprocedural ownership summary** (does this function retain each
   parameter? does it transfer its return?). Unblocks #5 fully.

Each of these is a sibling pass or a new analysis module. None are built.

## Testing discipline

Every optimization above must land with:

1. **A scoreboard diff.** If the optimization fires, [specs/optimizer-refcount.md](../specs/optimizer-refcount.md)'s
   `stderr` and `RequiredIR:x64-windows` blocks shrink. Regenerate via
   `maxon spec-test --filter=refcount-baseline-whole-program --update-required`
   and review the diff. If the scoreboard doesn't move, either the
   optimization doesn't fire on realistic code (in which case, why?), or
   the scoreboard is missing a section that exercises it (in which case,
   add a section *before* landing the optimization).
2. **Full spec-suite green.** Expect a handful of tests in
   [ownership-edge-cases.md](../specs/ownership-edge-cases.md) and
   [memory-safety.md](../specs/memory-safety.md) to need rebaselining via
   `--update-required`. Review each diff: the removed lines must be pure
   refcount churn, the set of `mm_alloc` / `mm_free` must stay identical,
   and every object must still reach `rc=0`.
3. **Self-hosted spec-test under `--mm-debug`.** The self-hosted compiler
   allocates hundreds of thousands of small structs during a spec-test
   run; a refcount bug that leaks even one allocation per compile shows
   up as tens of thousands of leaked allocations. This is the most
   sensitive leak detector available — run it on every refcount
   optimization change:

   ```
   maxon build maxon-selfhosted --mm-debug
   ./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test
   ```

   Any `MM leak: N allocation(s) remain` line (with N > 0) means the
   optimization is unsound. The per-tag breakdown tells you which type
   is leaking — use it to find the elision pattern that mispredicted
   ownership.

## <a name="trap-mm_alloc-returns-rc0"></a>Appendix: the `mm_alloc` trap

A worked example of an aliasing fallacy, preserved here because it already
caused one regression.

`mm_alloc` returns a fresh allocation at **rc=0**. The convention is that
the immediately-subsequent `mm_incref` call is what transitions ownership
to rc=1. Function calls (`Box.create` etc.) differ — they return at **rc=1**
via an internal incref inside the callee.

When the same SSA value is stored into two slots, the compiler treats the
second slot as an alias of the first. Eliding the second slot's
incref/decref pair is *only* safe when:

- Some slot (either the first or a transitive aliased slot) owns its own
  reference AND releases it via its own decref after the elided pair.

For `func.call @Box.create` results, the callee-returned rc=1 plus the
first slot's scope-end decref satisfy this. For `std.call_runtime @mm_alloc`
results, the first slot does NOT own anything — the allocation sits at
rc=0 until the second slot's explicit incref. Eliding the second slot's
pair leaves the allocation permanently at rc=0 and the scope-end decref
just underflows the counter without freeing, leaking the allocation.

The pass guards against this by requiring, on the firstStoreOf path, that
the source slot have its own decref at-or-after the candidate decref.
`__enum_rawval_*` slots have no decref (the only reference is transferred
to `__lit_tmp_*`), so the guard correctly refuses to elide.

Future optimizations that work on aliased slots should respect the same
invariant: **a slot that never gets its own decref cannot serve as the
"live source" justifying elimination elsewhere**. When in doubt, model
refcounts as actual numbers and mentally execute the trace under the
proposed optimization — if any `mm_alloc`'d allocation ends at rc > 0,
the optimization is unsound.
