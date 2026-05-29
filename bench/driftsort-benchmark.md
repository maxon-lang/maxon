# Maxon driftsort vs Rust `slice::sort` — benchmark methods & findings

Date: 2026-05-28
Host: Windows 11, x64. Single machine, both binaries native.

## Purpose

Maxon's stable `Array.sort` is a faithful reproduction of Rust's driftsort
*algorithm* (powersort-ordered merging of runs manufactured by a stable
quicksort under a √n min-run). It does **not** yet have the low-level speed
primitives Rust's implementation relies on (branchless cmov small-sort,
`MaybeUninit` scratch, refcount-bypassing element moves). This benchmark
establishes the current baseline gap and, by comparing element types, decomposes
*where* the gap is — so the deferred-primitive work can be prioritized by data
rather than assumption.

Rust 1.90's stable `slice::sort` **is** driftsort (Peters & Bergdoll), so this is
a same-algorithm, same-machine comparison. The remaining gap is implementation
constant factor, not algorithm.

## Method

Two programs sort identical inputs and report the same CSV
(`lang,type,pattern,n,iters,total_ms`):

- Maxon: [`bench/sortbench.maxon`](sortbench.maxon) — `Array.sort` (driftsort),
  timed with `Clock.nowMs` / `Clock.elapsedMs`.
- Rust: [`bench/sortbench.rs`](sortbench.rs) — `slice::sort` (driftsort), timed
  with `std::time::Instant`, built `rustc -O`.

Controls for a fair comparison:

- **Identical input.** Both use the same LCG
  `state = (state*1103515245 + 12345) & 0x7FFFFFFF`, seed `2463534242`, values
  masked to 16 bits (`& 0xFFFF`) so duplicates occur — exercising the
  low-cardinality path. Patterns: `random`, `ascending` (`i`), `descending`
  (`n - i`).
- **Two element types.** `i64` (cheap value moves, no refcount) and `String`
  (heap, refcounted in Maxon). The i64-vs-String contrast isolates element-move
  cost from per-element loop cost.
- **Same comparator semantics.** i64 natural order; String lexicographic byte
  order (matches Rust's default `String: Ord`), so comparator shape is the same
  in both and is not the variable under test.
- **Clone cost subtracted.** Sorting consumes the input, so each timed iteration
  clones a master array then sorts. A clone-only loop is timed separately and
  subtracted, so the reported number is sort time alone. (Without this, the
  trivial ascending case is dominated by clone cost.)
- **Repetition.** N = 20000, iters = 100. N is bounded by Maxon's current speed
  (larger N times out); iters chosen so Rust's times clear millisecond
  resolution. Times are total over the 100 iterations.

### Reproduce

```
# Rust
cd bench && rustc -O sortbench.rs -o sortbench_rs.exe && ./sortbench_rs.exe
# Maxon (via the maxon-dev run_program tool, or build + run the emitted exe)
./bin/maxon.exe build bench/sortbench.maxon && ./bench/sortbench.exe
```

## Results

Sort-only time, total ms over 100 iterations, N = 20000 (clone subtracted):

| pattern    | Maxon i64 | Rust i64 | i64 ratio | Maxon String | Rust String | String ratio |
|------------|-----------|----------|-----------|--------------|-------------|--------------|
| random     | 3548      | 23       | **~154×** | 30454        | 207         | **~147×**    |
| ascending  | 16        | 1        | ~16×      | 812          | 27          | ~30×         |
| descending | 16        | <1       | —         | 1501         | 22          | ~68×         |

(Ascending/descending i64 Rust times are at/below ms resolution; treat those
ratios as indicative only. The random row is the load-bearing comparison.)

Within-language String-vs-i64 multiplier on `random`:

- Maxon: 30454 / 3548 = **8.6×**
- Rust:  207 / 23 = **9.0×**

## Findings

**1. The hot-path (random) gap is ~150× — on plain i64, where there is no
refcount traffic at all.** The gap exists in the most primitive case, so it is
not caused by heap-element handling. It is the per-element fundamentals: every
element access goes through a bounds-checked `managed.get`/`set` and the
comparator is an indirect closure call, none of which Rust pays (rustc
monomorphizes the comparator, inlines it, elides bounds checks, and uses
branchless inner loops).

**2. The element-move (refcount) primitive is NOT the dominant gap.** The
String-vs-i64 multiplier is ~8.6× in Maxon and ~9.0× in Rust — essentially the
same. If Maxon's refcount-on-move were the bottleneck, String would blow up
*relative to Rust*; instead the String ratio (147×) is slightly better than the
i64 ratio (154×). Both languages pay a similar premium for heap strings
(pointer-chasing comparator, heap allocation). So refcount-bypassing moves,
originally ranked the #1 lever, are in fact lower priority.

**3. The adaptive structure works.** Ascending/descending are ~100–200× faster
than random within Maxon (16 ms vs 3548 ms for i64), confirming powersort
run-detection: presorted input is dominated by the O(n) scan, exactly as
designed. Maxon is behaving like driftsort algorithmically; the gap is constant
factor per element operation.

## Reprioritized primitive roadmap

The data overrides the original plan's ordering:

1. **Inner-loop element access + comparator inlining (NEW #1).** A raw/unchecked
   element-access path for the sort hot loops (small-sort, merge, partition) and
   an inlinable comparator. This is what separates 23 ms from 3548 ms on i64 and
   is the single biggest lever.
2. **Branchless small-sort (cmov).** Matters once per-access overhead is removed.
3. **Refcount-bypassing element moves (DOWNGRADED).** Real but ~9× and paid
   similarly by both languages; not where Maxon is uniquely losing.
4. **`MaybeUninit` scratch.** Pairs with #3; same lower priority.

## Caveats

- Millisecond timer; N capped by Maxon's current speed. Numbers are baseline
  magnitude, not precision benchmarks — re-measure with finer timing / larger N
  once #1 lands and Maxon is fast enough.
- Single host, single run per cell (no best-of-N). Adequate for a ~150× gap;
  tighten once the gap closes to single/double digits.
- String comparator is lexicographic-on-decimal-text, so ascending/descending by
  numeric value are not perfectly ordered lexically — irrelevant to timing (we
  only measure wall-clock of a representative comparison workload).
