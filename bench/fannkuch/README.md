# fannkuch-redux: Maxon against the C reference

The compiler's runtime benchmark, measured against a fixed reference so a compiler change reads as
a ratio rather than a number that only compares with itself.

## The reference

`fannkuchredux.gcc-5.c` is the Computer Language Benchmarks Game program **C gcc #5** by Jeremy
Zerfas, vendored verbatim from
https://benchmarksgame-team.pages.debian.net/benchmarksgame/program/fannkuchredux-gcc-5.html
(the page's `<pre>` block, character for character). The Benchmarks Game programs are published under
the 3-Clause BSD licence: see
https://salsa.debian.org/benchmarksgame-team/benchmarksgame/-/blob/master/LICENSE.md before
redistributing it anywhere but here.

Why this program: it is the fastest non-SIMD entry on the game's results page, within 5% of Rust #4
(same algorithm lineage, both parallel): 7.28 s wall / 28.26 s CPU against 6.94 s / 27.12 s at n=12 on
the game's 4-core box.

**It is built single-threaded here.** The game builds it
`gcc -pipe -Wall -O3 -fomit-frame-pointer -march=ivybridge -fopenmp`; the harness builds it

```
clang -O3 -march=native -Wall -Wno-unknown-pragmas -o out/c/fannkuchredux.exe fannkuchredux.gcc-5.c
```

with the clang at `C:\Program Files\LLVM\bin` (clang 22 on the host that minted the log). Without
`-fopenmp` the `#pragma omp parallel for` is a comment and the 12 blocks run in sequence on one thread,
which is the fair opponent for a Maxon program that has no data-parallel primitive short of `spawn`
services. `-march=native` (Coffee Lake here) makes the C figure host-specific, so log rows compare only
within one host. Cygwin's gcc on this machine is broken; MSYS2 gcc is the alternative if gcc is wanted.

## The Maxon program

`examples/fannkuch-redux.maxon` is a transliteration of the C program's single block: the same
factorial table, the same block prologue (`initialPermutation`), the same flip loop (`flipCount`: copy
`current[1..n)` into a caller-owned scratch, restore `firstValue` at its flipped slot, reverse the
middle when `firstValue > 2` — with the simple `low < high` swap rather than C's unrolling trick), the
same pre-increment carry loop (`advancePermutation`), the same checksum by parity. All four working
arrays are allocated once in `main`. `n` is `argv[1]` with 11 as the default; the pancake count is the
exit code.

Two things the Maxon program does NOT do, on purpose:

- **It does not sidestep the bounds checks.** Every access is `try a.get(i) otherwise panic(...)` /
  `try a.set(i, value: v) otherwise panic(...)`. The stdlib exposes no unchecked indexed accessor (the
  only unchecked element path is `for v in a` iteration), and eliding checks the program cannot fail is
  the compiler's job — that is what the loop below optimizes.
- **It stores 8-byte elements** (`Array with int(i64.min to i64.max)`) where C stores `int8_t` pancakes
  and `intptr_t` counts. At n ≤ 12 every array sits in L1 either way. The narrow variant
  (`Array with int(0 to 15)`, 1-byte storage) trades set-side range checks for index-side ones and is a
  PROGRAM-side experiment to run on its own, never inside a compiler A/B.

## The harness

```
python scripts/bench-fannkuch.py --n 11 --runs 5 --profile           # measure the slot compiler
python scripts/bench-fannkuch.py --n 11 --runs 5 --ref <pre-sha> --note "..."   # A/B against a control
python scripts/bench-fannkuch.py --n 12 --runs 5 --note "..."         # the headline size
```

It builds the C reference (cached on source hash + clang version + flags), builds the example with
each arm's compiler into `out/<arm>/` (`--emit-ir` on, so `out/<arm>/fannkuch-redux.ir` is there to
read), runs every binary interleaved after one warm-up, verifies stdout and the exit code on every run
(exit 101 is reported as a LEAK, and no row is written on any failure), and prints min / median / max
per arm with the ratio against C, the emitted-code census of each Maxon binary, and with `--profile`
the `maxon profile run` hot-function table. `--note` appends a row per Maxon arm to
`docs/fannkuch-benchmark-log.md`. `--ref <sha>` builds a compiler from that commit in a cached
worktree under `out/ref-<sha>/`, seeded from `.bootstrap/`, which is how a control arm is made.

`out/` is gitignored. The `/fannkuch-iterate` skill is the procedure that drives one round.

## Census before and after the rewrite (same slot compiler, 2026-09-08)

`scripts/emitted-code-count.py` on the example, so the roadmap's earlier fannkuch figures can be
placed: the rewrite changed the corpus, and rows dated before it do not compare with rows after.

| program | ops | jmp | imul-imm | idiv | call-direct | mgd-call | im-blocks | mov |
|---|---|---|---|---|---|---|---|---|
| old example (per-permutation `IntArray.create()`) | 1909 | 64 | 1 | 2 | 134 | 48 | 180 | 212 |
| new example (C #5 transliteration) | 2308 | 70 | 1 | 2 | 166 | 54 | 209 | 266 |

## What one access costs (read off `out/slot/fannkuch-redux.ir`, 2026-09-08; after round 7 every guard falls through to its fast arm and the slow arms sit after the function's last hot block; after round 8 every loop's step block falls into its header and the back edge is the header's taken `jcc` into the body — the reverse loop is 8 instructions per iteration)

A `get` in `flipCount`'s reverse loop, fast path: `cmp idx,0 / setcc / jcc` (the `ElementIndex >= 0`
range check, its boolean kept in a callee-saved register for the matching `set`), `load [rec+8] / cmp /
jae` (the bound), `load [rec+0]`, `load [buf+idx*8]`, `mov 0`, `load [rec+40] / cmp / jcc` (managed
element?), then a critsplit copy pair and `cmp r10,0 / jcc` for the `try`. A `set` adds the
`capacity < 0` and `buffer == 0` guards and the buffer's refcount read. ~86 instructions per
reverse-loop iteration where clang emits ~8.

## Candidate ranking

**General, classical passes first** (user direction): each row is a compiler-wide optimization, and the
fannkuch shape it happens to cover is named beside it. Re-ranked after every round from the new
profile; a closed row keeps its measured delta.

| # | Candidate | What it is | The fannkuch shape it covers | Risk | State |
|---|---|---|---|---|---|
| 1 | Jump threading through constant phi inputs | a `condBranch` on a block argument that is a constant on an incoming edge is resolved on that edge | the `try` flag test after every inlined fast arm (`cmp r10,0 / jcc` + the phi copy), −4 per access | low-med | **closed, round 2** |
| 2 | Value-range analysis + redundant-check elimination | ranges for induction variables and loads along dominators; a check implied by a dominating check or the range is deleted | the `ElementIndex >= 0` guard (−2 per access), then the bound itself for loop-bounded indices | med | **closed, rounds 3 and 5** (the bound itself by symbolic bounds against the hoisted length) |
| 3 | Alias-aware loop-invariant code motion | memory disambiguation by object + field offset so an element store does not pin the record-header loads; known-effect calls admitted | `length@8` reloaded per access; today LICM refuses any loop with a store or a call | med | **folded into round 4** (the unswitching pass hoists the header loads under the same two facts) |
| 4 | Loop unswitching | loop-invariant conditions hoisted by versioning the loop | the remaining shape guards (ownership, buffer, sharing) leave the loop and the hot copy is call-free | high | **closed, round 4** |
| 5 | Loop rotation + block layout | one branch instruction fewer per iteration; cold arms out of line | every loop's back edge; 54 slow arms inline with the hot code | med | **closed, rounds 7 and 8** (cold-block sinking; rotation by layout) |
| 6 | General inlining with a cost model | beyond today's leaf-only single round | `flipCount`/`advancePermutation` into `main` | med | open |
| 7 | Escape analysis for managed arrays | `__managed_create` records promoted to the frame like struct records | allocation-bound programs only (not this one since the rewrite) | med | open |
| 8 | Target-tier peephole | there is none today | address-mode and compare/branch residue after the passes above | med | open |
| 9 | Program-side narrow element type | `Array with int(0 to 15)` mirroring `int8_t` | — | n/a | open |
| 10 | Cold-call spilling (register allocation) | a call in a cold block does not confine the hot path; save/reload around the cold run | every slow arm's call forced the loop's working set into five callee-saved registers and spilled the rest in the loop | high | **closed, round 6** |
| 11 | Redundant-load elimination under a dominating load | a load whose address was loaded on a dominating path with no store or call between is replaced by that value | `flipCount`'s second `temp.get(firstValue)` reloads `[buf + fv*8]` one block after the first, per flip | low | open, **re-ranked cold** in round 9: a per-instruction sample profile (2,376 samples at n=11) put 39 on the reload, most of them skid from the taken branch before it; the two loads issue together, so the reload is off the flip's serial chain |
| 12 | Established-guard folding (a forward dataflow over the managed shape and bound facts) | a shape guard, or a constant-index bound, already proved for its record by an earlier guard chain or a successful `set` is folded; every undescribed call or unplaceable store clears the facts | `advancePermutation`'s six fixed-index accesses re-ran their guards on a record the previous access proved, and its carry loop's unswitch chain re-tested the same predicates | low-med | **closed, round 9** |
| 13 | Spill weight by use count in the register allocator | a loop-carried value updated every iteration is not the one spilled across a call when a loop-invariant one would do; the unused callee-saved registers are used | `main`'s `index` and `checksum` live in frame slots across the two calls per permutation (3 loads + a store each per iteration) while `rsi`/`rdi` go unused; ~190 of 2,376 samples sit on those slot moves | med | open — **next**: round 9's profile is `flipCount` 76.0%, `main` 12.5%, `advancePermutation` 11.5%; `main`'s loop is slot traffic and the `mod 2` sequence |
| 14 | Bound-check hoisting by loop versioning | a bound `i < length` on an induction variable `i < hi` is replaced by one test `hi <= length` before the loop | `flipCount`'s copy loop pays two bound checks per iteration (137 samples of 2,376), its bound `n` being unrelated to either length | med | open |
| 15 | Test-chain threading through a versioned loop's fast exit | the second loop's `__us_test` chain is folded on the edge from the first loop's fast copy, which proved the same predicates | `flipCount`'s flip loop re-tests `temp`'s shape after the copy loop did (about 50 samples), and no fact survives the copy loop's join because its slow version may run zero iterations | med | open |
| 16 | `mod` by a power of two on a non-negative dividend | `x mod 2^k` is `x and (2^k − 1)` where the range analysis proves `x >= 0` | `main`'s `index mod 2` is a seven-instruction signed sequence (68 samples) on a counter the range analysis already knows non-negative | low | open |

Closed:

| Round | Change | n=11 A/B (control → change) | census | self-compile |
|---|---|---|---|---|
| 9 | `foldEstablishedGuards` — a Std pass after `unswitchInvariantGuards`: a forward dataflow per function, keyed by record value, carries the four shape predicates (each established on its guard's proceed arm, three of them also on `__managed_set`'s success edge, which the runtime states leaves the record owned, unshared and allocated) and a constant-index length floor; every call the runtime does not describe and every store that is not a bounded element store clears every record's facts, and a join keeps only what every edge proves; a guard whose predicate is established folds to its proceed arm and its unread operand chain is retired (`specs/established-guards.md`, 2 shape cases + 7 controls). The unswitch chain's destructor test, which could never fire, is gone with it. 47 guards fold in this program: `advancePermutation`'s six fixed-index accesses keep only their loads and stores, and every chain test but the slow copies' bound tests folds | 2,398 → 2,336 ms (**−2.6%**); n=12 31,818 → 30,952 ms, ratio **1.51** | ops 2517 → 2085, mgd-call 69 → 57, call-direct 159 → 142, im-blocks 182 → 135, jmp 41 → 34 (the folded guards, their loads, and the slow loop copies that lost their last way in) | 37,315 → 37,753 ms (+1.2%), control rebuilt with itself so both arms sit at their own fixed point |
| 8 | loop rotation by layout in `BranchCleanup` — transform 6: a natural loop's header chain (the header and its fall-through blocks up to the exit test) is laid down after the loop's physically last hot `jmp` latch, admitted only where the iteration loses one instruction (the header's `jcc` into the body becomes the taken back branch, or the exit block sits right after the latch so the inversion fires); the loop finder buckets its back edges per header once, for all five of its consumers (`specs/loop-rotation-layout.md`, 14 cases). All three `flipCount` loops rotate: the reverse loop is 8 instructions per iteration and its back edge is a taken `jcc` | 2,519 → 2,408 ms (**−4.4%**); n=12 32,993 → 31,883 ms, ratio **1.53** | ops 2518 → 2517, jmp 42 → 41: each elided back-edge `jmp` is offset by the entry `jmp` its unswitched sibling loop now needs, so the column cannot see the per-iteration change | 44,566 → 38,602 ms (**−13.4%**) |
| 7 | cold-block sinking in `BranchCleanup` — transform 5: every cold block (an inlined access's slow arm, a range check's panic block, an `otherwise panic` handler) is laid out after the function's last hot block, one stable partition with a no-terminator block glued to its physical successor, placed between the unreachable-block drop and the fall-through elision so every else-edge is still an op; the existing conditional inversion then turns each guard into a fall-through with the complemented branch aimed at the arm (`specs/cold-block-layout.md`, 5 cases). fannkuch's hot→cold layout boundaries 66 → 9 (one per function's cold region); `flipCount`'s copy loop 3 → 1 taken branches per 10-instruction iteration | 2,735 → 2,521 ms (**−7.8%**); n=12 36,834 → 32,928 ms, ratio **1.59** | byte-identical (ops 2518, jmp 42): the shape is layout, which no census column counts | 40,589 → 36,680 ms (**−9.6%**) |
| 1 | static trivial-element stamp — the inlined get/set arms omit the `element_destroy@40` guard and the `__im_empty` block where the element type owes no drop (`specs/static-trivial-element.md`) | 7,926 → 7,698 ms (**−2.9%**) | ops 2308 → 2111, im-blocks 209 → 172, mov 266 → 236 | 40,491 → 40,103 ms (−1.0%) |
| 2 | `threadConstantBranches` — a branch on a block argument that a predecessor passes as a constant is decided on that edge and duplicated into the others; the join keeps its value phi so no SSA is rebuilt (`specs/thread-constant-branches.md`, 12 cases). 37 sites in this program: every inlined get/set's `try` flag test | 7,692 → 5,856 ms (**−23.9%**); n=12 109,655 → 81,949 ms, ratio **3.95** | ops 2111 → 2008, jmp 52 → 23, im-blocks 172 → 138, mov 236 → 199 | 41,606 → 39,575 ms (−4.9%) |
| 6 | cold-call spilling in the register allocator — every block carries a heat (`normal`/`cold`) minted by the passes that create exceptional arms (an inlined access's slow arm when the fast arm serves the common case, a range check's panic block, the leaf inliner's panic redirect, a `try … otherwise panic` handler) and kept by every copy; a call in a cold block confines nothing, and the live caller-saved registers are saved before the cold RUN (pre-moves, call, captures) and reloaded after it, inside the cold block; loop unswitching became pressure-aware (a hoist plan is refused over the target's GPR budget); compiler-minted values carry derived origins so a refusal names a source span (`specs/cold-call-spilling.md`, 10 cases). Two reviews found a compiler panic and a wrong answer before landing; both are cases | 2,862 → 2,742 ms (**−4.2%**); n=12 38,997 → 36,863 ms, ratio **1.78** | ops 2175 → 2518 (brackets in every cold arm), mov 213 → 248; `advancePermutation`'s hot loop 55 → 0 spill ops | 44,871 → 40,313 ms (**−10.2%**); binary +11% (the brackets) |
| 5 | symbolic upper bounds in `refineValueRanges` — an interval carries `v ≤ base + k` for a non-negative base; the true edge of `a < b` attaches `b − 1`, composing through `b`'s own bound; a phi keeps a bound only under one base; a guarded-access JOIN is entered with the guard false (`index < length`) when the slow arm's callee succeeds only in bounds; a second run after unswitching, scoped to the functions it rewrote, sees the one hoisted length; the unswitching pass reuses a preheader load of the same field so a `for` bound and the accesses compare against one value (`specs/symbolic-bounds.md`, 8 cases). In fannkuch's versioned loops every bound check but the flip loop's first is decided: the reverse loop is 9 instructions per iteration | 3,856 → 2,874 ms (**−25.5%**); n=12 53,393 → 39,024 ms, ratio **1.88** | ops 2244 → 2175, call-direct 166 → 159, mgd-call 76 → 69 | 44,451 → 43,251 ms (−2.7%) |
| 4 | `unswitchInvariantGuards` — loop unswitching on the managed shape guards (ownership, buffer, sharing, destructor, constant-index bounds), resting on two runtime-stated facts (an element store cannot alias a record field; `set`-family callees write the header only through a detach whose precondition is a failed guard); the fast copy has the guards folded and `length`/`buffer` hoisted into callee-saved registers; a guard-less loop is hoisted in place (`specs/loop-unswitching.md`, 9 cases). 7 loops versioned here: the reverse loop's body is `cmp/jcc + load` per get and `cmp/jcc + store` per set | 4,440 → 3,860 ms (**−13.1%**); n=12 60,716 → 53,436 ms, ratio **2.58** | ops 1712 → 2244 (two versions of every loop), mgd-call 54 → 76 (the slow versions) | 43,256 → 43,521 ms (+0.6%) |
| 3 | `refineValueRanges` — signed-i64 interval analysis with branch refinement (relational through the other operand), widening, dead subtrees under an empty refinement; a decided compare becomes a constant and its branch folds (`specs/value-range-analysis.md`, 8 cases, 4 panic controls). 22 compares in this program: the `ElementIndex >= 0` check on every loop counter, on `firstValue` after its first check, and on both ends of the reverse loop — with them the CSE'd `setcc` registers and the in-loop spill | 5,860 → 4,442 ms (**−24.2%**); n=12 82,132 → 60,762 ms, ratio **2.93** | ops 2008 → 1712, call-direct 166 → 144 (the `__rc_panic` arms), `__rc_panic` blocks 31 → 9 | 43,305 → 41,920 ms (−3.2%) |

Declined for this program: EC18 (already took the `mod 2`), refcount inlining (no managed elements),
EC22 rel8 (size only), EC21/EC23 (measured empty by `docs/emitted-code-roadmap.md`).

## Traps this document owes the next reader

- Instruction count is not time: the roadmap's `EC14` deleted two loads and measured zero. A row
  claims a speedup only from the harness's interleaved A/B.
- Attribute the shape before predicting the win: one change was ×1.90 on fannkuch and −5.8% on nbody.
- A before/after across a source change is not an A/B; build the control from the pre-change commit
  with `--ref` and run both arms in one session.
- `scripts/sample_profile.py` is the other profiler (per-sample, call edges); `maxon profile run` is
  what the harness uses.
