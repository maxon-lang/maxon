# fannkuch-redux benchmark log

Wall time of `examples/fannkuch-redux.maxon` against the C gcc #5 reference
(`bench/fannkuch/fannkuchredux.gcc-5.c`, built single-threaded with clang), both run on this
machine by `scripts/bench-fannkuch.py`. **Read the tables downwards**: each row is one measurement,
dated, and the ratio column is the number the loop drives at 1.0.

- **Rows are appended by the tool, never by hand.** A run without `--note` records nothing; a run
  with one records unconditionally. The note says WHY the number moved - the instrument cannot.
- **This is an instrument, not a gate.** There is nothing to pass; a row that did not move is a
  datapoint, and a row that moved the wrong way is a reading to explain.
- **Rows rot.** Re-measure before planning from one; an A/B is two arms in ONE session, interleaved,
  never a before/after across a source change.
- **Only rows from one host compare.** `-march=native` makes the C figure machine-specific.
- `docs/optimization-log.md` is the compile-time axis and is never touched by this tool.

Host: Intel Core i7-8700 (6C/12T, 3.2 GHz), Windows 11, x64. C reference: clang, single-threaded.

Columns: `C min/med ms` and `Maxon min/med ms` are the minimum and median of `runs` interleaved
timed runs after one warm-up each; `ratio` is Maxon median / C median; `ops` is the emitted-code
census of the Maxon binary (`scripts/emitted-code-count.py`); `arm` names the compiler
(`slot`, `ref@<sha>`, or a `--label`); `tree` is whether the harness's checkout was clean.

| date | head | tree | n | runs | C min/med ms | Maxon min/med ms | ratio (med) | exe bytes | ops | arm | note |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-09-08 | 70cd80b3 | clean | 11 | 5 | 1,526 / 1,529 | 7,927 / 7,943 | 5.20 | 44,275 | 2308 | slot | iteration 0 baseline: C #5 transliteration, arrays allocated once, checked get/set; slot compiler built from 9c5b8947 sources |
| 2026-09-08 | 70cd80b3 | dirty | 12 | 1 | 20,724 / 20,724 | 111,911 / 111,911 | 5.40 | 44,275 | 2308 | slot | iteration 0 headline baseline, single run (same binaries as the n=11 row) |
| 2026-09-08 | d22130d9 | dirty | 11 | 5 | 1,519 / 1,525 | 7,919 / 7,926 | 5.20 | 44,275 | 2308 | ref@d22130d9 | round 1: static trivial-element stamp — the inlined get/set arms omit the element_destroy@40 guard and the __im_empty block where the lowering knows the element owes no drop; predicted -3 instr per get and per set in every fannkuch loop; self-compile 40,491 ms |
| 2026-09-08 | d22130d9 | dirty | 11 | 5 | 1,519 / 1,525 | 7,685 / 7,698 | 5.05 | 43,251 | 2111 | slot | round 1: static trivial-element stamp — the inlined get/set arms omit the element_destroy@40 guard and the __im_empty block where the lowering knows the element owes no drop; predicted -3 instr per get and per set in every fannkuch loop; self-compile 40,103 ms |
