# Optimization log

What the compiler's cost has actually done, change by change. **This is `scale-test`'s deliverable.**

**Read the tables downwards.** Each row is one recorded change, dated. A column *is* its history, so
the progression is the thing you see rather than something you have to reconstruct.

`scale-test` renders **no verdict**. There is nothing to pass, no golden to accept, and no light to
turn green — it is an instrument, and this document is what it is an instrument *for*. The last row
of each table below is the previous datapoint: every run reads it and reports the delta from it, so
you still see exactly what moved, without anything failing.

## How rows get here

Rows are **appended by the tool, not by hand**. Running

```
maxon-shv2 scale-test --note="<what changed, and why the numbers moved>"
```

records this run as a new dated row in both tables. **A run without `--note` records nothing** —
scale-test is run dozens of times a day against dirty trees and abandoned experiments, and a log of
all of them would be a log nobody reads.

**`--note` is an instruction, not a request: if you pass one, the row is written.** Even if the
numbers are identical to the row above it. The run will tell you plainly that nothing moved, and then
record it anyway, because you are the one who knows whether it is a datapoint and the instrument is
not.

The `--note` is not a formality. The instrument can see exactly *what* moved and can never see *why*,
and six months from now a number with no reason attached is worth almost nothing. So the reason is
demanded at the one moment it is still known: from you, now. See
[`maxon-shv2/Testing/ScaleHistory.maxon`](../maxon-shv2/Testing/ScaleHistory.maxon).

## Reading the numbers

The rungs are generated programs, each **double** the last, so rung 5 is 32× rung 0. Rung 0 shows
constant-factor wins; only rung 5 shows whether a change bent the curve.

**And because the ladder doubles, the RATIO between two rungs IS the growth.** Divide a rung by the
one before it: **2× is linear, 4× is quadratic**. That is the whole method — there is nothing fitted,
no exponent, no residual, and no threshold for anyone to argue about. `scale-test` prints that ratio
beside every phase; these tables carry the raw counts it is taken from.

| what | how much to trust it |
| --- | --- |
| **allocations, bytes** (per rung) | **Exact and bit-for-bit reproducible.** The same source through the same compiler makes exactly the same allocations, on an idle machine or a loaded one. A number here that moved, moved for a *reason*. |
| **the rung-over-rung ratio** | **Exact**, for the same reason: it is a division of two numbers that cannot move. |
| **time** | **Not measured, and never will be here.** It is machine-dependent, so a dated column of it would compare a loaded box in July against an idle one in August. For the milliseconds of one compile, use the compiler's own `--log=compiler:debug`. |

These are the compiler's own allocation counts and byte volumes while compiling that rung — **not**
peak resident memory, which nothing currently measures.

Frees are measured and reported but not tabulated here: they track allocations almost exactly, so a
third table would be a near-duplicate paid for in width. They are in `--result-json` for anyone who
wants them.

Dates are UTC. The columns assume a six-rung ladder; changing the ladder's length changes what the
numbers mean, so the tool refuses to append a row of a different width rather than make the tables
ragged.

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
| 2026-07-14 | scale-test stopped measuring its own residue: the runner wrote each rung's executable and metrics.tsv INTO the rung source directory, which loadProject enumerates before filtering to .maxon, so every run after the first compiled two extra directory entries per rung (+26 allocs, +945 bytes). Outputs now live in the corpus root and stale files are pruned, so a cold and a warm .scale-tmp now agree exactly. The COMPILER is unchanged; every earlier row in these tables was measured warm and is offset by that constant. | 225,909 | 458,567 | 969,325 | 2,173,347 | 5,311,569 | 14,509,295 |
| 2026-07-14 | elimTrivialBlockArgs re-swept the WHOLE FUNCTION to a fixpoint, and the sweep count was the length of the fold chain, so the pass was QUADRATIC: collectReadyArgs deferred any phi whose fold target was also folding that sweep, which peels exactly one link per O(function) sweep. A run of N sequential loops holding one idle var builds a chain of ~2N (loop L's header phi for the var is fed by loop L-1's EXIT phi for it), and the pass measured x3.95 allocs / x4.85 bytes / x4.23 time per DOUBLING -- folding a LINEAR 2N phis with quadratic work, which is the tell. It is now a WORKLIST: when a phi folds, only the phis it FEEDS can have changed, and a reverse (incoming -> arg) CSR names them in O(1); chains resolve through a path-compressed union-find instead of one sweep per link. On that shape at N of 128: 798,403 allocations fall to 6,090 (131x) and 107.5ms to 1.8ms (60x), growth x3.95 to x1.98 (linear), folding the identical 256 phis. (This note was TRUNCATED when written: CommandLine.optionValue cut every option value at its SECOND "=", so "N=128" ended the sentence. Repaired, and the stdlib bug fixed.) | 228,020 | 462,434 | 976,672 | 2,187,610 | 5,339,644 | 14,564,974 |
| 2026-07-14 | P1.0d.2: short-circuit and/or, word-form bitwise, char literals — plus the trivial-phi pass rewritten from an O(chain-length x IR) fixpoint (each whole-function sweep peeled exactly ONE link of the fold chain) to a worklist + path-compressed union-find whose trigger slots MIGRATE as folds re-point reads. On 128 sequential loops: 798,403 -> 6,090 allocations and x3.95 -> x1.98 growth, folding the identical 256 phis. elimTrivialBlockArgs is now x1.96 (linear). The rung itself adds a shrinking constant (+1.0% allocs at rung 0 falling to +0.4% at rung 5); short-circuit mints one phi and takes no mutable-var snapshot, so it does NOT multiply the parser's known O(V.B). | 228,016 | 462,430 | 976,668 | 2,187,606 | 5,339,640 | 14,564,970 |
| 2026-07-14 | P1.0d.3: bool/int type discipline — ONE agreement rule (TypeRules.typesAgree) now gates arithmetic, shifts, unary minus, comparisons, if/while conditions, REASSIGNMENT, return values and call arguments, where before only and/or/xor had one. So `4 + flag` compiled to 5, `4 * flag` to 4, `flag shl 4` to 16, `takeInt(flag)` put a bool in an int parameter, and `if 4` branched on nonzero — wrong ANSWERS, not crashes. Memory moved and is attributed EXACTLY, at all six rungs: bytes = 16 x functionCount + 56. The 16/function is IrFunction gaining ONE pointer field (`valueTags`, the parser's per-value type column, PUBLISHED rather than dropped so SemanticCheck can check a call argument against a callee that may be in another file), which rounds the object up one 16-byte slab size class. The flat +56 bytes / +2 allocs is the entry stub's empty ValueTypeTagArray (header + buffer) in createFromStd, once per program. The per-value TAG DATA COSTS NOTHING: the parser already allocated and filled exactly that array per function and threw it away at each function's end; it is now handed to IrFunction.create by reference. Allocations are FLAT (+2) across all six rungs — the type oracle does not scale with the program. | 228,018 | 462,432 | 976,670 | 2,187,608 | 5,339,642 | 14,564,972 |
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
| 2026-07-14 | scale-test stopped measuring its own residue: the runner wrote each rung's executable and metrics.tsv INTO the rung source directory, which loadProject enumerates before filtering to .maxon, so every run after the first compiled two extra directory entries per rung (+26 allocs, +945 bytes). Outputs now live in the corpus root and stale files are pruned, so a cold and a warm .scale-tmp now agree exactly. The COMPILER is unchanged; every earlier row in these tables was measured warm and is offset by that constant. | 10,035,504 | 21,193,752 | 48,085,196 | 123,186,970 | 382,007,625 | 1,522,047,456 |
| 2026-07-14 | elimTrivialBlockArgs re-swept the WHOLE FUNCTION to a fixpoint, and the sweep count was the length of the fold chain, so the pass was QUADRATIC: collectReadyArgs deferred any phi whose fold target was also folding that sweep, which peels exactly one link per O(function) sweep. A run of N sequential loops holding one idle var builds a chain of ~2N (loop L's header phi for the var is fed by loop L-1's EXIT phi for it), and the pass measured x3.95 allocs / x4.85 bytes / x4.23 time per DOUBLING -- folding a LINEAR 2N phis with quadratic work, which is the tell. It is now a WORKLIST: when a phi folds, only the phis it FEEDS can have changed, and a reverse (incoming -> arg) CSR names them in O(1); chains resolve through a path-compressed union-find instead of one sweep per link. On that shape at N of 128: 798,403 allocations fall to 6,090 (131x) and 107.5ms to 1.8ms (60x), growth x3.95 to x1.98 (linear), folding the identical 256 phis. (This note was TRUNCATED when written: CommandLine.optionValue cut every option value at its SECOND "=", so "N=128" ended the sentence. Repaired, and the stdlib bug fixed.) | 10,303,792 | 21,724,590 | 49,157,229 | 125,332,785 | 386,473,068 | 1,531,710,779 |
| 2026-07-14 | P1.0d.2: short-circuit and/or, word-form bitwise, char literals — plus the trivial-phi pass rewritten from an O(chain-length x IR) fixpoint (each whole-function sweep peeled exactly ONE link of the fold chain) to a worklist + path-compressed union-find whose trigger slots MIGRATE as folds re-point reads. On 128 sequential loops: 798,403 -> 6,090 allocations and x3.95 -> x1.98 growth, folding the identical 256 phis. elimTrivialBlockArgs is now x1.96 (linear). The rung itself adds a shrinking constant (+1.0% allocs at rung 0 falling to +0.4% at rung 5); short-circuit mints one phi and takes no mutable-var snapshot, so it does NOT multiply the parser's known O(V.B). | 10,302,131 | 21,718,747 | 49,144,218 | 125,302,038 | 386,393,240 | 1,531,478,357 |
| 2026-07-14 | P1.0d.3: bool/int type discipline — ONE agreement rule (TypeRules.typesAgree) now gates arithmetic, shifts, unary minus, comparisons, if/while conditions, REASSIGNMENT, return values and call arguments, where before only and/or/xor had one. So `4 + flag` compiled to 5, `4 * flag` to 4, `flag shl 4` to 16, `takeInt(flag)` put a bool in an int parameter, and `if 4` branched on nonzero — wrong ANSWERS, not crashes. Memory moved and is attributed EXACTLY, at all six rungs: bytes = 16 x functionCount + 56. The 16/function is IrFunction gaining ONE pointer field (`valueTags`, the parser's per-value type column, PUBLISHED rather than dropped so SemanticCheck can check a call argument against a callee that may be in another file), which rounds the object up one 16-byte slab size class. The flat +56 bytes / +2 allocs is the entry stub's empty ValueTypeTagArray (header + buffer) in createFromStd, once per program. The per-value TAG DATA COSTS NOTHING: the parser already allocated and filled exactly that array per function and threw it away at each function's end; it is now handed to IrFunction.create by reference. Allocations are FLAT (+2) across all six rungs — the type oracle does not scale with the program. | 10,302,667 | 21,719,667 | 49,145,906 | 125,305,262 | 386,399,536 | 1,531,490,797 |
<!-- scale-history:bytes -->

Since the suite was introduced, rung 5 has gone **36,897,948 → 14,509,321 allocations** (−61%) and
**2.86 GB → 1.52 GB** (−47%).

## Exponents — CLOSED 2026-07-14. Kept as history; no longer written.

**The tool no longer fits exponents, and no longer records time at all.** The row below is the only
one that was ever machine-written, and it is left exactly as it was measured. Deleting honestly-taken
numbers to tidy up a format change would be the worst of both worlds; they are simply not extended.

Two reasons it went, and they are the same reason twice:

**The ladder DOUBLES, so the ratio between two rungs already IS the growth.** x2.00 is linear, x4.00
is quadratic — read straight off the allocation counts in the tables above, no fit required. An
exponent could tell you nothing those numbers had not already told you, and it dragged in a residual,
which dragged in a NOISY verdict, which got an exemption written for it so it would stop complaining.
That is optimizing the gauge instead of the engine.

**And time can never be trended.** It is machine-dependent, so a dated column of it compares a loaded
box in July against an idle one in August. The `time /` half of every cell below is a fact about the
machine that ran it. (The compiler still times itself — `--log=compiler:debug` and `--metrics=<path>`
report per-phase milliseconds for one compile, which is where a clock is worth reading.)

What replaced it: `scale-test` prints per-phase **allocations and bytes at every rung**, with the
rung-over-rung ratio beside them. `regalloc:liveness` reads **x2.80** and `regalloc:splitting`
**x2.52** where every other phase sits on **x1.98** — the known-superlinear splitter, visible without
a single fitted number.

| date | change | phase:load | phase:lex | phase:parse | phase:merge | phase:resolveTypes | phase:semanticCheck | phase:lowerMaxonToStd | phase:pruneDeadBlockArgs | phase:elimTrivialBlockArgs | phase:foldConstOperands | phase:sugarGate | phase:isel | phase:regalloc | phase:prologueEpilogue | phase:runtimeAugment | phase:encode | phase:link | phase:writeExe | phase:emitIr | regalloc:criticalEdges | regalloc:blockOrder | regalloc:splitting | regalloc:liveness | regalloc:coloring | regalloc:ssaDestruction | regalloc:rewrite | aggregate |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-07-14 | scale-test stopped measuring its own residue: the runner wrote each rung's executable and metrics.tsv INTO the rung source directory, which loadProject enumerates before filtering to .maxon, so every run after the first compiled two extra directory entries per rung (+26 allocs, +945 bytes). Outputs now live in the corpus root and stale files are pruned, so a cold and a warm .scale-tmp now agree exactly. The COMPILER is unchanged; every earlier row in these tables was measured warm and is offset by that constant. | 0.478 / 0.597 | 0.844 / 0.983 | 0.819 / 0.987 | 0.866 / 0.984 | 0.747 / 0.928 | 0.872 / 0.980 | 0.865 / 0.985 | 1.026 / 0.980 | 0.870 / 0.884 | 1.079 / 0.982 | 0.807 / 0.984 | 0.803 / 0.982 | 1.554 / 1.386 | 0.795 / 0.983 | 0.433 / 0.000 | 0.856 / 0.969 | 0.790 / 0.768 | 0.674 / 0.681 | n/a / n/a | 0.752 / 0.971 | 0.817 / 0.951 | 1.782 / 1.493 | 1.598 / 1.617 | 0.826 / 0.984 | 0.896 / 0.988 | 0.922 / 0.986 | 1.342 / 1.182 |

## Notes on the changes

The four earliest rows predate the automated log; their numbers are reconstructed from the diffs in
git, so they are accurate but were not written by the tool. They also predate the exponent table,
which is why it starts empty.

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
that allocated it. **The scope column is the one that finds things** — a `String` row can never tell
you that 150 of them came from `emitFixedToken`.

| what | share | note |
| --- | ---: | --- |
| `OpIndexList` / `BlockRefList` | ~17% | `IrBlock.opRefs` and `IrFunction.blockRefs` are **linked lists of integers** — a heap node per op reference, plus a `ListIterator` per traversal. `Array` would kill all three (nodes, list boxes, iterators). Now the biggest single win available. |
| `stepBackwardOverOp` et al. | ~7% | A superlinear cluster inside the register allocator (fitted exponent ~1.47), not a constant factor. |

`ArrayIterator` (once the single biggest allocating scope, at 102,845) and `EnumDummy` (once ~20% of
all allocations) no longer appear.
