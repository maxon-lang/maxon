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

## What one access costs (read off `out/slot/fannkuch-redux.ir`, 2026-09-08)

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
| 2 | Value-range analysis + redundant-check elimination | ranges for induction variables and loads along dominators; a check implied by a dominating check or the range is deleted | the `ElementIndex >= 0` guard (−2 per access), then the bound itself for loop-bounded indices | med | **closed, round 3** (the bound itself needs the array length modelled: round 4+) |
| 3 | Alias-aware loop-invariant code motion | memory disambiguation by object + field offset so an element store does not pin the record-header loads; known-effect calls admitted | `length@8` reloaded per access; today LICM refuses any loop with a store or a call | med | **folded into round 4** (the unswitching pass hoists the header loads under the same two facts) |
| 4 | Loop unswitching | loop-invariant conditions hoisted by versioning the loop | the remaining shape guards (ownership, buffer, sharing) leave the loop and the hot copy is call-free | high | **closed, round 4** |
| 5 | Loop rotation + block layout | one taken jump fewer per iteration; cold arms out of line | every loop's back edge; 54 slow arms inline with the hot code | med | open |
| 6 | General inlining with a cost model | beyond today's leaf-only single round | `flipCount`/`advancePermutation` into `main` | med | open |
| 7 | Escape analysis for managed arrays | `__managed_create` records promoted to the frame like struct records | allocation-bound programs only (not this one since the rewrite) | med | open |
| 8 | Target-tier peephole | there is none today | address-mode and compare/branch residue after the passes above | med | open |
| 9 | Program-side narrow element type | `Array with int(0 to 15)` mirroring `int8_t` | — | n/a | open |

Closed:

| Round | Change | n=11 A/B (control → change) | census | self-compile |
|---|---|---|---|---|
| 1 | static trivial-element stamp — the inlined get/set arms omit the `element_destroy@40` guard and the `__im_empty` block where the element type owes no drop (`specs/static-trivial-element.md`) | 7,926 → 7,698 ms (**−2.9%**) | ops 2308 → 2111, im-blocks 209 → 172, mov 266 → 236 | 40,491 → 40,103 ms (−1.0%) |
| 2 | `threadConstantBranches` — a branch on a block argument that a predecessor passes as a constant is decided on that edge and duplicated into the others; the join keeps its value phi so no SSA is rebuilt (`specs/thread-constant-branches.md`, 12 cases). 37 sites in this program: every inlined get/set's `try` flag test | 7,692 → 5,856 ms (**−23.9%**); n=12 109,655 → 81,949 ms, ratio **3.95** | ops 2111 → 2008, jmp 52 → 23, im-blocks 172 → 138, mov 236 → 199 | 41,606 → 39,575 ms (−4.9%) |
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
