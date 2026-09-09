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
| 2026-09-09 | b157bb5d | dirty | 11 | 5 | 1,525 / 1,528 | 7,679 / 7,692 | 5.03 | 43,251 | 2111 | ref@b157bb5d | round 2: threadConstantBranches — a branch on a block argument that a predecessor passes as a constant is decided on that edge and duplicated into the others; 37 sites in fannkuch (every inlined get/set's try flag test); self-compile 41,606 ms |
| 2026-09-09 | b157bb5d | dirty | 11 | 5 | 1,525 / 1,528 | 5,831 / 5,856 | 3.83 | 42,739 | 2008 | slot | round 2: threadConstantBranches — a branch on a block argument that a predecessor passes as a constant is decided on that edge and duplicated into the others; 37 sites in fannkuch (every inlined get/set's try flag test); self-compile 39,575 ms |
| 2026-09-09 | b157bb5d | dirty | 12 | 1 | 20,761 / 20,761 | 109,655 / 109,655 | 5.28 | 43,251 | 2111 | ref@b157bb5d | round 2 headline: threadConstantBranches |
| 2026-09-09 | b157bb5d | dirty | 12 | 1 | 20,761 / 20,761 | 81,949 / 81,949 | 3.95 | 42,739 | 2008 | slot | round 2 headline: threadConstantBranches |
| 2026-09-09 | b18dc1d8 | dirty | 11 | 5 | 1,530 / 1,535 | 5,843 / 5,860 | 3.82 | 42,739 | 2008 | ref@b18dc1d8 | round 3: refineValueRanges — interval analysis with branch refinement decides the __rc_ range checks on loop counters, checked-once values and while low<high; 22 compares in fannkuch; self-compile 43,305 ms |
| 2026-09-09 | b18dc1d8 | dirty | 11 | 5 | 1,530 / 1,535 | 4,432 / 4,442 | 2.89 | 41,715 | 1712 | slot | round 3: refineValueRanges — interval analysis with branch refinement decides the __rc_ range checks on loop counters, checked-once values and while low<high; 22 compares in fannkuch; self-compile 41,920 ms |
| 2026-09-09 | b18dc1d8 | dirty | 12 | 1 | 20,723 / 20,723 | 82,132 / 82,132 | 3.96 | 42,739 | 2008 | ref@b18dc1d8 | round 3 headline: refineValueRanges |
| 2026-09-09 | b18dc1d8 | dirty | 12 | 1 | 20,723 / 20,723 | 60,762 / 60,762 | 2.93 | 41,715 | 1712 | slot | round 3 headline: refineValueRanges |
| 2026-09-09 | f27344ef | dirty | 11 | 5 | 1,529 / 1,531 | 4,419 / 4,440 | 2.90 | 41,715 | 1712 | ref@f27344ef | round 4: unswitchInvariantGuards — loop unswitching on the managed shape guards with the header loads hoisted into the fast copy; 7 loops versioned in fannkuch; self-compile 43,256 ms |
| 2026-09-09 | f27344ef | dirty | 11 | 5 | 1,529 / 1,531 | 3,853 / 3,860 | 2.52 | 44,787 | 2244 | slot | round 4: unswitchInvariantGuards — loop unswitching on the managed shape guards with the header loads hoisted into the fast copy; 7 loops versioned in fannkuch; self-compile 43,521 ms |
| 2026-09-09 | f27344ef | dirty | 12 | 1 | 20,736 / 20,736 | 60,716 / 60,716 | 2.93 | 41,715 | 1712 | ref@f27344ef | round 4 headline: unswitchInvariantGuards |
| 2026-09-09 | f27344ef | dirty | 12 | 1 | 20,736 / 20,736 | 53,436 / 53,436 | 2.58 | 44,787 | 2244 | slot | round 4 headline: unswitchInvariantGuards |
| 2026-09-09 | 5cdf75b5 | dirty | 11 | 5 | 1,530 / 1,535 | 3,838 / 3,856 | 2.51 | 44,787 | 2244 | ref@5cdf75b5 | round 5: symbolic upper bounds in refineValueRanges (guarded-access join, composition, a second run after unswitching, preheader load reuse) — the remaining bound checks in fannkuch's versioned loops decided; self-compile 44,451 ms |
| 2026-09-09 | 5cdf75b5 | dirty | 11 | 5 | 1,530 / 1,535 | 2,860 / 2,874 | 1.87 | 44,275 | 2175 | slot | round 5: symbolic upper bounds in refineValueRanges (guarded-access join, composition, a second run after unswitching, preheader load reuse) — the remaining bound checks in fannkuch's versioned loops decided; self-compile 43,251 ms |
| 2026-09-09 | 5cdf75b5 | dirty | 12 | 1 | 20,736 / 20,736 | 53,393 / 53,393 | 2.57 | 44,787 | 2244 | ref@5cdf75b5 | round 5 headline: symbolic bounds |
| 2026-09-09 | 5cdf75b5 | dirty | 12 | 1 | 20,736 / 20,736 | 39,024 / 39,024 | 1.88 | 44,275 | 2175 | slot | round 5 headline: symbolic bounds |
