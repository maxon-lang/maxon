# Optimization log

What the compiler's memory traffic has actually done, change by change.

Every entry below the introduction is **appended by the tool, not by hand**. Running

```
maxon-shv2 scale-test --update-required --note="<why>"
```

accepts a new set of memory goldens, and that acceptance — a human deciding the compiler *should*
allocate differently now — is the only event recorded here. A scale-test run on its own proves
nothing and writes nothing; it happens dozens of times a day against dirty trees and abandoned
experiments. See [`maxon-shv2/Testing/ScaleHistory.maxon`](../maxon-shv2/Testing/ScaleHistory.maxon).

The `--note` is mandatory because the suite can see exactly *what* moved and can never see *why*.
Six months from now, "allocations fell 8% at rung 5" is worth almost nothing without the sentence
naming the change that did it.

## Reading the numbers

The rungs are a ladder of generated programs, each **double** the last, so rung 5 is 32× rung 0.
Rung 5 is the one to watch: constant-factor wins show up everywhere, but anything that bends the
*curve* only becomes visible at the top of the ladder.

Numbers are the compiler's own allocation traffic while compiling that rung — **not** peak
resident memory, which nothing currently measures. Allocations are counted, not sampled, and are
bit-for-bit reproducible: the same source through the same compiler makes exactly the same
allocations on an idle machine or a loaded one. That is what makes them a golden rather than a
budget.

Frees are gated (in `scale-baseline.tsv`) but deliberately not tabulated here — they track
allocations almost exactly, so a column of them would be a near-duplicate. What they are actually
*for* is the leak check: `allocs - frees` must not grow.

## Where it started

The scaling suite was introduced in `2e998a7ca`, which established the first baseline. For context,
that first measurement — before any of the work below:

| rung | allocations | bytes |
| ---: | ---: | ---: |
| 0 | 366,957 | 15,018,355 |
| 5 | 36,897,948 | 2,859,036,397 |

Rung 5 is now **19,261,978 allocations / 1,685,625,083 bytes** — 48% fewer allocations and 41% fewer
bytes than that starting point.

## History before this log existed

These three landed before the log was automated; their numbers are reconstructed from the
`scale-baseline.tsv` diffs in git, so they are accurate but were not written by the tool.

### `667ec9eee` — a name is a slice of the source, not a heap String

| rung | allocations | bytes |
| ---: | --- | --- |
| 0 | 366,957 -> 316,018 (-50,939, -13.8%) | 15,018,355 -> 13,125,747 (-1,892,608, -12.6%) |
| 5 | 36,897,948 -> 20,936,928 (-15,961,020, -43.2%) | 2,859,036,397 -> 1,735,970,667 (-1,123,065,730, -39.2%) |

By far the largest single win so far. Token text stopped being a heap `String` and became a
zero-copy `ByteArray` slice into the source bytes already in memory. Note the shape: rung 0 fell
14% but rung 5 fell 43% — this did not just shave a constant factor, it bent the curve.

### `4f81c7efe` — a byte string literal IS a ByteArray, and may be a global

| rung | allocations | bytes |
| ---: | --- | --- |
| 0 | 316,018 -> 315,808 (-210, -0.0%) | 13,125,747 -> 13,120,707 (-5,040, -0.0%) |
| 5 | 20,936,928 -> 20,930,518 (-6,410, -0.0%) | 1,735,970,667 -> 1,735,816,827 (-153,840, -0.0%) |

A bootstrap fix, not a compiler optimization: `b"..."` at module scope resolved to an auto-created
`__Array_i8` instead of the stdlib `ByteArray`, which forced byte constants to be materialized per
occurrence inside accessor functions. Hoisting them to real globals is the small win here; the
bigger payoff was making the *lexer's* keyword table a byte-literal map at all.

### `ac9e7db47` — for-in over an Array is an index counter, decided at lowering

| rung | allocations | bytes |
| ---: | --- | --- |
| 0 | 315,808 -> 281,775 (-34,033, -10.7%) | 13,120,707 -> 12,113,819 (-1,006,888, -7.6%) |
| 5 | 20,930,518 -> 19,261,978 (-1,668,540, -7.9%) | 1,735,816,827 -> 1,685,625,083 (-50,191,744, -2.8%) |

`for x in arr` no longer allocates an iterator object (a heap `ArrayIterator` wrapping a heap
cursor). It lowers to an integer counter over the backing buffer, in the parser, where the loop is
built — replacing a post-monomorphization pass that pattern-matched the CFG back into a loop and
bailed on exactly the loops a compiler runs (arrays of ops, blocks, tokens, strings).

## What the profile says is left

From `scale-test --per-type` (which attributes every allocation to the TYPE allocated *and* the
SCOPE that allocated it). As of `ac9e7db47`, out of ~2.23M traced allocations:

| what | allocations | share | note |
| --- | ---: | ---: | --- |
| `EnumDummy` | 435,879 | ~20% | Pure waste from the bootstrap's `try_call` assoc-enum lowering. Elidable. See `docs/refcount-optimization-roadmap.md` item 8. |
| `OpIndexList` / `BlockRefList` | ~390,000 | ~17% | `IrBlock.opRefs` and `IrFunction.blockRefs` are **linked lists of integers** — a heap node per op reference, plus a `ListIterator` per traversal. `Array` would kill all three (nodes, list boxes, iterators). |
| `stepBackwardOverOp` et al. | ~146,588 | ~7% | A superlinear cluster inside the register allocator (fitted exponent ~1.47), not a constant factor. |

`ArrayIterator`, previously the single biggest allocating scope at 102,845, no longer appears.
