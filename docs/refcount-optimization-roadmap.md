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

The pass today is **intra-block only** and handles two patterns:

1. **Load-based alias.** `load %v = src; store %v → dst` with later
   `incref(dst) ... decref(dst)`, when `src` is provably alive across the
   window. Original pattern.
2. **Store-based alias (firstStoreOf).** Same SSA value stored into multiple
   slots without a reload in between — e.g. the for-in lowering that stores
   `iter.current()` into both `__forin_result` and the user's loop variable.
   Safety requires `src` to have its own decref at-or-after the candidate
   decref (otherwise eliding the second slot's pair leaks the allocation; see
   [the struct-backed-enum trap](#trap-mm_alloc-returns-rc0) below).

Across the current 2503-test spec corpus the pass fires on exactly two
programs (`rc-for-in-elem-decrefed`, and the new
`refcount-baseline-whole-program` scoreboard). Every other optimization
opportunity below requires infrastructure the pass does not yet have.

## Optimization proposals

### 1. Cross-block pairs via dominator-based matching

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

### 2. Paired-pop elimination on container reads

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

### 3. Call-site ownership-transfer elision

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

### 4. Loop-invariant decref/incref hoisting

**Pattern.** Inside a loop body, a slot holds the same value for the entire
loop lifetime but the current emitter produces one incref+decref pair per
iteration:

```
loop_0:
  %v = memref.load invariant_slot
  mm_incref %v                           ; every iteration
  ... use ...
  %v2 = memref.load invariant_slot
  mm_decref %v2                          ; every iteration
  cf.br loop_0.header
```

If `invariant_slot` is never reassigned in the loop and nothing can release
its contents, the pair should be hoisted out of the loop (or eliminated
entirely when another slot holds the value across the loop).

**Safety.** Classic LICM conditions on `invariant_slot`:
- No stores to `invariant_slot` inside the loop.
- No calls inside the loop whose callee could release the object (rule out
  via callee side-effect metadata, or conservatively assume all calls
  could).

**Expected impact.** Scoreboard section 3 (loop pushing strings into a
list) and section 4 (loop iterating a matrix). Estimated 10–20 pairs per
loop × multiple loops in real programs.

**Prerequisites.**

- Loop detection. The pass has no loop structure today — CFG + back-edge
  identification would be the minimum. A natural extension of the
  dominator infrastructure from #1.
- Side-effect metadata per callee (can this callee call mm_decref on an
  arbitrary object?). Conservative: if any callee in the loop body is
  unknown, don't hoist.

**Risks / traps.**

- Early-exit from the loop (break, throw). If the loop can exit after the
  hoisted incref but before the hoisted decref, the invariant is broken.
  Requires "every loop exit reaches the hoisted decref" as a side condition.

### 5. Short-lived argument temporaries

**Pattern.** A struct literal passed as an argument to a call gets allocated,
incref'd, passed, and then decref'd at the call-site scope-end — all within a
few ops:

```
%x = arith.constant ...
%y = arith.constant ...
%p = func.call @Point.create %x, %y          ; fresh alloc rc=1
memref.store %p, __call_tmp_3
%arg = memref.load __call_tmp_3
mm_incref %arg                                ; bump to rc=2
%ret = func.call @sum_point %arg
...
%arg2 = memref.load __call_tmp_3
mm_decref %arg2                               ; back to rc=1
... eventually ...
mm_decref __call_tmp_3                        ; rc=0, free
```

The first incref/decref pair on `__call_tmp_3` is a scope-local ownership
bump that the callee doesn't actually need — the callee only borrows the
reference.

**Safety.** Prove the callee does not retain the argument: it does not
store the parameter to a long-lived location, does not pass it to another
call that retains, and does not return it. This is interprocedural and
requires callee-side analysis.

**Expected impact.** Scoreboard section 2. Significant only if
interprocedural analysis is available — otherwise must conservatively
assume retention.

**Prerequisites.**

- Per-parameter "retained?" annotation on each function. Computed by
  summarizing each function's body once and caching per-call-graph SCC.
- Alternatively, a syntactic flag (`@borrow` parameter marker) that lets
  the user opt in. Lower compile-time cost, higher user-facing change.

**Risks / traps.**

- This is the biggest "whole-program optimization" win but also the
  highest-complexity addition. Recommend after #1 and #4 are in place.

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

### 8. Stack promotion for `@heap`-allocated values whose lifetime never escapes

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
