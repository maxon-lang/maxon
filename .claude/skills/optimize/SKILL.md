---
name: optimize
description: Optimization pass over a change. HUNTS UNSCALABLE (SUPERLINEAR) ALGORITHMS, read off scale-test's doubling ladder — the compiler must stay LINEAR in program size. Use when asked to optimize, profile, or check the scaling of compiler changes, when a scale-test ladder bends, or as the optimization step of a larger process. Runs before the code review, never after.
---

# Optimize a change

**Run BEFORE the code review, never after** — optimizing rewrites code and can introduce exactly the
duplication the review exists to catch. That ordering is not a licence to leave a mess: **do not fork a
fast path from a slow one, and do not inline a helper into three call sites.** If a specialized copy
genuinely must exist, comment *why the two cannot be one* — otherwise the review will (correctly)
collapse it.

**Optimize code you did not write where you can.** When another process dispatched you here, that
independence is already true and is part of why the step exists.

## First: who is running you

- **Dispatched as a step of another process** (`/land` §4, or any caller that owns the gates): **you do
  not run the full suite and you do not commit.** Your caller runs the battery and commits everything,
  minutes later. Your correctness proof is your `--filter`ed specs staying green while you iterate.
- **Invoked standalone**: the full suite and the commit are yours, at the end, once.

**Either way, every golden your runs moved stays exactly where it lies.** A moved golden IS a codegen
change — `git checkout -- specs-shv2/fragments/` to tidy `git status` deletes the record of what your
change did to the emitted code. A fragment that moved for a reason you cannot state is a **finding**,
not churn to drop.

## The mandate — UNSCALABLE ALGORITHMS (user directive)

**Hunt for anything superlinear.** The compiler must stay **LINEAR** in program size; the whole budget
(≤30 s / ≤1.7 GB on self-compile) depends on it. Look for:

- **Nested scans** over compiler-sized collections (`ops`, values, blocks, symbols) — an O(n) lookup
  inside an O(n) walk is the classic, and this project has shipped it more than once.
- **Linear lookups that want a hash/index** — `findFirst` over an array in a loop.
- **Repeated rebuilds** — recomputing a fixpoint, liveness, or a dominator tree per element instead of
  once. (The register allocator's splitter recomputes liveness after every split.)
- **Iterating a dense index space when you should walk set BITS** — `0 upto valueCount` instead of the
  live-set's set bits. Making exactly this change is what turned shv2's allocator linear.
- **Allocation in a hot path**, especially anything that allocates into the very `mm` stream being
  traced. (`contentHash()` allocates. `String.hash` walks the bytes in place.)

**Scope the hunt to the change.** A new pass, a new IR op, or a new collection the compiler indexes by
earns the full hunt above. A change that added none of those gives the hunt structurally nothing to
find — confirm no new superlinear structure crept in, read one `scale-test`, and stop. **If nothing
triggered, say so**; a superlinear hunt over a change that added no algorithm has nothing to find, and
reporting that honestly is the correct outcome. Do not manufacture work where there is none; do not
micro-optimize to look busy.

## ⚠ `scale-test` IS AN INSTRUMENT. It collects data for TREND ANALYSIS. That is all it is.

**No verdict. No goldens. No gate. Nothing to pass.** This is easy to get backwards, and getting it
backwards makes you optimize the instrument instead of the compiler.

**`.claude/CLAUDE.md` carries the short form; this is the full reading guide.**

- **Do NOT chase a green scale-test. There isn't one.** A curve that looks wrong is a **reading to
  explain**, not a light to turn green.
- **NEVER touch the instrument to make a number look better.** The right response to a curve that bends
  is to say WHY it bends. (`regalloc:liveness` bills two call sites into one bucket: one per function,
  linear; one after every split, superlinear. It is a *sum of two exponents*, so it bends on a perfectly
  idle machine.) Write it down; do not launder it.
- **The per-rung MEMORY numbers are EXACT and bit-for-bit reproducible** — load cannot move them. A
  change in allocations/frees/bytes for the same input is **real, every time**, and is the single most
  informative thing in your report. It has already caught `traceUnitOf` calling `contentHash()` (which
  *allocates*, into the very `mm` stream it was added to trace), and a fix whose first cut cost +4
  allocations/function because a field store boxed a union. **Explain any movement. Attribute it.**
- ⚠ **A/B-ing two binaries' CPU needs `--repeat=3`** (the default is 1); a single sample's per-phase
  ratios wobble up to ±0.5 run to run. And **an A/B must be INTERLEAVED** — a stable sign can come from
  the schedule alone.
- **The CPU unit is platform-defined and the platforms do not agree** (TSC ticks on Windows,
  nanoseconds on macOS) and there is **no honest conversion** — `QueryPerformanceFrequency` is the
  *performance counter's* rate, not the TSC's. ⇒ **Compare RATIOS between rungs, which are unit-free;
  compare absolutes only within one platform.** ⚠ `DefaultRepeatCount` is **1**, so a logged CPU row is
  a single sample; rows logged before 2026-07-28 are minima instead (~+9–10% apart at rung 5) — do not
  read that step as a regression.
- **There is no WALL time, deliberately.** It counts every other process on the box, so a dated table of
  it would compare a loaded machine against an idle one. (Measured: allocation deltas read 0.000 on an
  unchanged compiler while time deltas read +0.09…+0.29, and one run read `phase:parse` at ×5.03 then
  ×1.78 across a DOUBLING ladder — that is not a curve of any shape, it is preemption.) CPU time comes
  from `__Builtins.threadCpuTicks()`, which advances only while the calling thread is scheduled.
- **`--per-type`** runs an untimed `--mm-trace` pass printing **two** ranked tables. Slow (minutes), off
  by default — and it is how you actually find things:
  - **by TYPE** — names the data structure. A `LiveIndexColumn` at exponent 2.17 **is** a quadratic.
  - **by SCOPE** — the *function* that made the allocation, which the type table structurally **cannot**
    tell you: a `String` row can never say that 150 of them came from `emitFixedToken`. A constant-factor
    hog hides inside its type and is a single named row here. **This is the column that finds things.**

### The two blind spots — and never credit one with the other's fix

✅ **The CPU column exists because a cost that ALLOCATES NOTHING was invisible**, and this project keeps
measuring them — the op-insertion quadratic inside `regalloc:splitting` (68% of the whole compile at
N=1024, allocation-free), `requireInterfaceForParse` (allocates identically to the digit across a
+24.15 ms parse delta), `getBlockByIdIn`'s per-guard-site scan, the cascade fixpoint duals.

⚠ **That cures ONE blind spot. The other is the CORPUS**, and a Δ0 from a ladder that cannot express the
feature is the instrument's blind spot, not the cost — in **every** column, CPU included. *(Clearest
case: `regalloc:splitting`'s float-across-calls quadratic was hidden because the corpus's `floatSpill`
knob was 4 — few enough that every float fit a register. The knob went 4 → 12; that is a corpus fix.)*

### `scripts/self-host-ab.sh` — when the question is the EMITTED code

A green suite and `scale-test` both measure the compiler's LOGIC, which every stage of the self-host
chain shares byte for byte. **The QUALITY OF THE CODE shv2 EMITS is a different question, and this is
the one command that answers it.** It builds stage-2 (stage-1 compiling shv2) and stage-3 (stage-2
compiling shv2), `cmp`s them (the fixpoint gate — a difference is a MISCOMPILE), times both
self-compiles, and runs `scale-test` on stage-1 and stage-2 INTERLEAVED, printing stage-2's per-phase
ratios over stage-1. Same logic in both ⇒ **any allocation ratio above 1.00 is a construct shv2's
codegen allocates for and the bootstrap's does not.** It reads the SEED as stage-1 (the tree binary is
already stage-2). `--profile` adds function-level attribution via `scripts/sample_profile.py`, which
reads shv2-emitted binaries (no `.mxdbg` needed — their `__symtable` closes `.text`). ~15 min; writes
only under `temp/selfhost/`.

⚠ **Measure on an IDLE machine, and measure the instrument before the subject.** This project has had a
dominant cost hide in the *wrong timing bucket* four separate times. Load can MASK a bug, not just
inflate numbers.

⚠ **The trend log is a deliverable, and it must say WHY.** The instrument can see exactly *what* moved
and can never see *why*. If your change moves the numbers, **record the reason in
`docs/optimization-log.md` at the one moment it is still known** — a row that says only "-8733
allocations" is worth a fraction of one that says which allocation stopped happening and what stopped
making it. **Write no row you did not measure.**

## Rules of engagement

- **Correctness first, always.** An optimization that changes behaviour is a bug.
- **Do not micro-optimize.** Constant factors are not the mandate; growth curves are. A tidy O(n) beats
  a clever O(n).
- **A superlinearity you can TRIGGER on a realistic input is FIXED, not filed.** Only a term you have
  **measured** linear-in-practice across the real corpus (like `SplitLiveRanges`' K², max K = 8) is filed
  as debt — reported to your caller, and to `docs/optimization-log.md`, WITH the measurement that shows
  it linear today and the trigger that would make it bend (an inliner, a machine-generated wide type). A
  curve you have not measured is not yet a debt; it is a defect to run down. **There is no backlog
  file**, so a debt you cannot fix goes in your REPORT and in the trend log, and the caller decides.
- **Check exit codes; never grep for a success string.** Exit **101** = memory leak.
- ⚠ Redirecting suite runs by hand, `--workers=1` and `fmt`'s path argument are in `.claude/CLAUDE.md`,
  once — not repeated here.

## Report

Each hot spot found, with `file:line` and its **complexity before and after**, and the **real
`scale-test` per-phase tables** before and after — the raw per-rung numbers, since the ladder doubles
and the RATIO between rungs is the growth. There is no verdict and no exponent table to paste.

**Do NOT claim codegen neutrality from the golden fragments.** They are REFERENCE MATERIAL and nothing
measures them (user ruling). If you need to show that a pass changed how the compiler RUNS without
changing what it EMITS, disassemble or use `--emit-ir-runtime=<names>` and say what you read.

**Never claim a measurement you did not take** — and if you could not make something faster, say so
rather than shipping a change that only looks like an optimization.
