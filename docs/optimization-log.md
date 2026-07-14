# Optimization log

What the compiler's memory traffic has actually done, change by change.

**Read the tables downwards.** Each row is one accepted change, dated; each column is one rung of the
scaling ladder. A rung's column *is* its history, so the progression is the thing you see rather than
something you have to reconstruct.

## How rows get here

Rows are **appended by the tool, not by hand**. Running

```
maxon-shv2 scale-test --update-required --note="<why>"
```

accepts a new set of memory goldens, and that acceptance — a human deciding the compiler *should*
allocate differently now — is the only event recorded. A scale-test run on its own proves nothing and
writes nothing; it happens dozens of times a day against dirty trees and abandoned experiments. The
`--note` is mandatory: the suite can see exactly *what* moved and can never see *why*, and six months
from now a number with no reason attached is worth almost nothing. See
[`maxon-shv2/Testing/ScaleHistory.maxon`](../maxon-shv2/Testing/ScaleHistory.maxon).

## Reading the numbers

The rungs are generated programs, each **double** the last, so rung 5 is 32× rung 0. Rung 0 shows
constant-factor wins; only rung 5 shows whether a change bent the *curve*.

These are the compiler's own allocation counts and byte volumes while compiling that rung — **not**
peak resident memory, which nothing currently measures. They are counted rather than sampled and are
bit-for-bit reproducible: the same source through the same compiler makes exactly the same
allocations on an idle machine or a loaded one. That is what makes them a golden rather than a
budget.

Frees are gated in `scale-baseline.tsv` but not tabulated here — they track allocations almost
exactly, so a third table would be a near-duplicate. What they are *for* is the leak check:
`allocs - frees` must not grow.

Dates are UTC. The columns assume a six-rung ladder; changing the ladder's length changes what the
numbers mean, so that would want a new table rather than more columns.

## Allocations

| date | change | rung 0 | rung 1 | rung 2 | rung 3 | rung 4 | rung 5 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-07-13 | *(baseline)* the scaling suite is introduced | 366,957 | 773,790 | 1,742,398 | 4,296,568 | 11,872,878 | 36,897,948 |
| 2026-07-13 | a name is a slice of the source, not a heap String | 316,018 | 644,760 | 1,369,342 | 3,087,700 | 7,601,346 | 20,936,928 |
| 2026-07-13 | a byte string literal IS a ByteArray, and may be a global | 315,808 | 644,350 | 1,368,532 | 3,086,090 | 7,598,136 | 20,930,518 |
| 2026-07-13 | for-in over an Array is an index counter, decided at lowering | 281,775 | 575,590 | 1,226,720 | 2,783,774 | 6,917,212 | 19,261,978 |
| 2026-07-14 | Rebase onto the rewritten main: the ladder now sees the for-in index-counter lowering and the compiler-traces-itself work at the same time. Allocs and frees are exactly the for-in commit's; bytes are its numbers plus the constant +8/rung that origin already accepted, and the two compose with no interaction. | 281,775 | 575,590 | 1,226,720 | 2,783,774 | 6,917,212 | 19,261,978 |
| 2026-07-14 | try_call on an associated-value enum no longer allocates a placeholder EnumDummy: null already means absent, and scope cleanup was already null-guarded, so the dummy was allocated, increffed, decreffed and freed on every successful call without ever being read | 224,996 | 456,886 | 966,324 | 2,168,570 | 5,306,696 | 14,518,054 |
| 2026-07-14 | P1.0d block scoping: the parser now pushes and pops a real Scope per if/while body — pushScope/popScope existed, were correct, and were never called, so a let inside an if leaked to the function frame. Two Scope containers per function  | 225,935 | 458,593 | 969,351 | 2,173,373 | 5,311,595 | 14,509,321 |
<!-- scale-history:allocations -->

## Bytes

| date | change | rung 0 | rung 1 | rung 2 | rung 3 | rung 4 | rung 5 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-07-13 | *(baseline)* the scaling suite is introduced | 15,018,355 | 33,313,844 | 81,458,950 | 226,863,241 | 739,103,906 | 2,859,036,397 |
| 2026-07-13 | a name is a slice of the source, not a heap String | 13,125,747 | 27,565,923 | 61,724,631 | 154,176,421 | 458,959,060 | 1,735,970,667 |
| 2026-07-13 | a byte string literal IS a ByteArray, and may be a global | 13,120,707 | 27,556,083 | 61,705,191 | 154,137,781 | 458,882,020 | 1,735,816,827 |
| 2026-07-13 | for-in over an Array is an index counter, decided at lowering | 12,113,819 | 25,521,299 | 57,502,887 | 145,153,461 | 438,545,764 | 1,685,625,083 |
| 2026-07-14 | Rebase onto the rewritten main: the ladder now sees the for-in index-counter lowering and the compiler-traces-itself work at the same time. Allocs and frees are exactly the for-in commit's; bytes are its numbers plus the constant +8/rung that origin already accepted, and the two compose with no interaction. | 12,113,827 | 25,521,307 | 57,502,895 | 145,153,469 | 438,545,772 | 1,685,625,091 |
| 2026-07-14 | try_call on an associated-value enum no longer allocates a placeholder EnumDummy: null already means absent, and scope cleanup was already null-guarded, so the dummy was allocated, increffed, decreffed and freed on every successful call without ever being read | 10,092,819 | 21,316,475 | 48,359,471 | 123,847,293 | 383,771,820 | 1,527,346,499 |
| 2026-07-14 | P1.0d block scoping: the parser now pushes and pops a real Scope per if/while body — pushScope/popScope existed, were correct, and were never called, so a let inside an if leaked to the function frame. Two Scope containers per function  | 10,036,452 | 21,194,700 | 48,086,144 | 123,187,918 | 382,008,573 | 1,522,048,404 |
<!-- scale-history:bytes -->

Since the suite was introduced, rung 5 has gone **36,897,948 → 14,518,054 allocations** (−61%) and
**2.86 GB → 1.53 GB** (−47%).

## Notes on the changes

The four rows above predate the automated log; their numbers are reconstructed from the
`scale-baseline.tsv` diffs in git, so they are accurate but were not written by the tool.

**`667ec9eee` — a name is a slice of the source, not a heap String.** By far the largest win so far.
Token text stopped being a heap `String` and became a zero-copy `ByteArray` slice into the source
bytes already in memory. Note the *shape*: rung 0 fell 14% but rung 5 fell 43%. That did not shave a
constant factor, it bent the curve.

**`4f81c7efe` — a byte string literal IS a ByteArray, and may be a global.** A bootstrap fix, not a
compiler optimization. `b"..."` at module scope resolved to an auto-created `__Array_i8` rather than
the stdlib `ByteArray`, which forced byte constants to be rebuilt per occurrence inside accessor
functions. The measurable win is small; the point was making the lexer's keyword table a byte-literal
map at all.

**`ac9e7db47` — for-in over an Array is an index counter.** `for x in arr` no longer allocates an
iterator object (a heap `ArrayIterator` wrapping a heap cursor). It lowers to an integer counter over
the backing buffer, in the parser, where the loop is built — replacing a post-monomorphization pass
that pattern-matched the CFG back into a loop and bailed on exactly the loops a compiler actually
runs (arrays of ops, blocks, tokens, strings).

**`EnumDummy` — a `try_call` on an associated-value enum stops allocating a placeholder.** The
biggest single win in the table: −24.6% of *all* allocations at rung 5. The bootstrap lowered a
throwing call returning a union into a heap-allocated dummy enum, a `select` between it and the real
result, and a decref of whichever lost — because a `try_call` returns null on the error path and the
lowering believed scope cleanup needed a real rc=1 allocation to decref. It never did: scope-end
cleanup already emits a null-GUARDED decref, and every managed slot is zeroed on function entry, so a
null slot is already the well-defined "nothing to release" case. On the success path the dummy was
allocated, increffed, decreffed and freed **without ever being read**. Null is what absent means, and
storing it is now the whole lowering.

The dummy was also *leaking* on the error path (see below), so this is a correctness fix wearing an
optimization's clothes.

## Bugs this uncovered

Removing the dummy exposed a leak it had been half-hiding, and the leak in turn exposed a hole in the
test suite. Both are fixed; both are worth knowing about.

**A routed try-block call leaked one reference per call.** A bare throwing call inside a
`try 'blk' … end 'blk' otherwise (e)` block has its result hoisted into a `__try_block_result_N` temp
that receives the callee's *transferred* reference — so that temp owns it and must release it.
`VarRegistry.KeysSince` excluded exactly those temps from scope-end cleanup, on the theory that a
downstream `let x = <call>` aliased the same slot without increfing. That was true for a struct
return, which was separately handed a `CallReturn` `__call_tmp_` that turned the alias into a *move*,
and false for an associated-value union, which was handed nothing and so increfed like any other
alias. Both special cases are gone: the try-block form now works the way every single-statement `try`
form already did, and the temp that receives the reference is the temp that releases it.

**80% of the spec suite could not see a memory leak.** `mm_leak_check` overrides the process exit code
with 101 when an allocation is still live at exit. But 2311 of the 2886 tests are compiled together
into one batched binary whose per-test verdicts are parsed from stdout markers carrying each test's
own `main` return value — and `TestRunner` discarded the batched binary's process exit code entirely.
The leak checker ran, printed, and had its verdict thrown away. A leaking batched test still emitted a
full set of passing markers. The runner now reads that exit code; because the leak counter is
process-global and cannot say *which* test leaked, a non-zero batch exit invalidates the batch and
re-runs its tests individually, where each one's own leak check attributes it.

## What the profile says is left

From `scale-test --per-type`, which attributes every allocation to the TYPE allocated *and* the SCOPE
that allocated it.

| what | share | note |
| --- | ---: | --- |
| `OpIndexList` / `BlockRefList` | ~17% | `IrBlock.opRefs` and `IrFunction.blockRefs` are **linked lists of integers** — a heap node per op reference, plus a `ListIterator` per traversal. `Array` would kill all three (nodes, list boxes, iterators). Now the biggest single win available. |
| `stepBackwardOverOp` et al. | ~7% | A superlinear cluster inside the register allocator (fitted exponent ~1.47), not a constant factor. |

`ArrayIterator` (once the single biggest allocating scope, at 102,845) and `EnumDummy` (once ~20% of
all allocations) no longer appear.
