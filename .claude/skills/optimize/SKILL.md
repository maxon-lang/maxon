---
name: optimize
description: Optimization pass over a change. HUNTS UNSCALABLE (SUPERLINEAR) ALGORITHMS, read off scale-test's doubling ladder — the compiler must stay LINEAR in program size. Use when asked to optimize, profile, or check the scaling of compiler changes, when a scale-test ladder bends, or as the optimization step of a larger process. Runs before the code review, never after.
---

# Optimize a change

**Run BEFORE the code review, never after** — optimizing rewrites code and can introduce exactly the
duplication the review exists to catch. That ordering is not a licence to leave a mess: **do not fork a
fast path from a slow one, and do not inline a helper into three call sites.** If a specialized copy
genuinely must exist, **state in your REPORT why the two cannot be one** — otherwise the review will
(correctly) collapse it. ⛔ **Do not write that as a comment**: you write no comments, and the
`documenter` skill turns a reason like this into one (a deliberate asymmetry) after the review.

**Optimize code you did not write where you can.** When another process dispatched you here, that
independence is already true and is part of why the step exists.

**Invoke the `maxon-coder` skill before rewriting any Maxon**; a rewrite goes through its compile loop like
any other edit.

## First: who is running you

- **Dispatched as a step of another process** (`/land` §4, or any caller that owns the gates): **you do
  not run the full suite and you do not commit.** Your caller runs the landing gates and commits
  everything, minutes later. Your correctness proof is your `--filter`ed specs staying green while you iterate.
- **Invoked standalone**: `/land` §8's landing gates and the commit are yours, at the end, once. The
  full suite is CI's.

**Either way, a `TargetIr` pin your change moves is a gate in `ir-specs/`.** Re-mint it with
`--update-required --filter=<spec>/` and leave the re-minted file exactly as it lies, for whoever
commits.

## The mandate — UNSCALABLE ALGORITHMS (user directive)

**Hunt for anything superlinear.** The compiler must stay **LINEAR** in program size, and a change that
slows the self-compile by more than 5% is a HALT for the user's ruling. `docs/optimization-log.md` holds
the current figures. Look for:

- **Nested scans** over compiler-sized collections (`ops`, values, blocks, symbols) — an O(n) lookup
  inside an O(n) walk is the classic, and this project has shipped it more than once.
- **Linear lookups that want a hash/index** — `findFirst` over an array in a loop.
- **Repeated rebuilds** — recomputing a fixpoint, liveness, or a dominator tree per element instead of
  once. (The register allocator builds liveness once per function and repairs it incrementally after
  each split, `refreshAfterSplit`; a rebuild per split is the shape to catch.)
- **Iterating a dense index space when you should walk set BITS** — `0 upto valueCount` instead of the
  live-set's set bits. Making exactly this change is what turned the compiler's allocator linear.
- **Allocation in a hot path**, especially anything that allocates into the very `mm` stream being
  traced. (Hashing through a `toByteArray()` copy allocates; `String.hash` walks the bytes in place.)
  ⚠ **An allocation count is a proxy, not a time**: a slab fast-path allocation costs about 8 ns. For a
  speed goal rank by self time in `maxon profile` and gate on the ladder's CPU column; use
  `--allocations-by-tag` for questions about allocations themselves.

- **Copying a routine's arithmetic means copying what BOUNDS it.** The same `pos - shift` without the
  `firstSite upto endSite` bound is how a quadratic enters.
- **A hash-keyed structure that scales badly: read the key type's `hash()` for dropped bits first.** Primitive
  hashes mix every bit, so the cure is the hash, never one key site.

**Time a self-compile against the pre-change compiler whenever a pass is added or a hot pass changes**
(`python scripts/bench-fannkuch.py --ref <pre-sha> --self-compile`), whatever the change bought elsewhere.
When the change also grows the compiler's own source, the self-compile cannot separate a slower compiler from
a bigger input, so the 5% is judged on the same input for both compilers: the scale ladder's generated
programs. Inline-copying an accessor to dodge a cost is duplication, not an optimization; make the accessor
free in the optimizer.

**Scope the hunt to the change.** A new pass, a new IR op, or a new collection the compiler indexes by
earns the full hunt above. A change that added none of those gives the hunt structurally nothing to
find — confirm no new superlinear structure crept in, read one `scale-test`, and stop. **If nothing
triggered, say so**; a superlinear hunt over a change that added no algorithm has nothing to find, and
reporting that honestly is the correct outcome. Do not manufacture work where there is none; do not
micro-optimize to look busy.

## ⚠ `scale-test` IS AN INSTRUMENT. It collects data for TREND ANALYSIS. That is all it is.

**No verdict. No goldens. No gate. Nothing to pass.** This is easy to get backwards, and getting it
backwards makes you optimize the instrument instead of the compiler.

**The `compiler-workflow` skill carries the short form; this is the full reading guide.**

- **Do NOT chase a green scale-test. There isn't one.** A curve that looks wrong is a **reading to
  explain**, not a light to turn green.
- **NEVER touch the instrument to make a number look better.** The right response to a curve that bends
  is to say WHY it bends — a bucket that bills two call sites of different growth is a *sum of two
  exponents*, and bends on a perfectly idle machine. Write it down; do not launder it.
- **The per-rung MEMORY numbers are EXACT and bit-for-bit reproducible** — load cannot move them. A
  change in allocations/frees/bytes for the same input is **real, every time**, and is the single most
  informative thing in your report. It has caught a trace hook that *allocated* into the very `mm`
  stream it was added to trace, and a field store that boxed a union at +4 allocations per function.
  **Explain any movement. Attribute it.**
- ⚠ **An A/B of two binaries' CPU needs several samples per arm**, since a single sample's per-phase
  ratios wobble up to ±0.5 run to run — take them with `--repeat=N`, which reports the least CPU per
  phase and fails as a BROKEN RUN if a rung's repeats disagree on memory. And **an A/B must be
  INTERLEAVED** — a stable sign can come from the schedule alone.
- ⛔ **The "before" side of an A/B is a compiler built from a NAMED commit (a clean checkout), never
  whatever sits in the slot.** After a checkout, stash or branch switch that reverts compiler source,
  REBUILD before measuring: the harness's staleness refusal compares modification times, so a binary
  newer than the reverted sources passes it while emitting the old code. Read emitted-code claims from
  disassembled bytes.
- **`--repeat=N` bounds noise within a run, not drift between runs**; a baseline from another day,
  branch or session is not a measurement. Take the noise floor from the same binary run twice (control vs
  control); an effect inside it is no effect.
- **When the exact columns (allocations, bytes, code bytes) and the CPU column disagree on a change's
  sign, believe the exact ones and look for the confound.** If untouched phases move as much as touched
  ones, the comparison has a confound, not a result.
- **A speedup measured from an implementation that also fails cases is not a number.** Measure
  per-operation costs in the configuration the change runs in (a saturated worker pool, not a serial
  loop). Before attributing a regression to a file, `git diff --name-only <a>..<b> -- <path>`.
- **The CPU unit is platform-defined and the platforms do not agree** (TSC ticks on Windows,
  nanoseconds on macOS) and there is **no honest conversion** — `QueryPerformanceFrequency` is the
  *performance counter's* rate, not the TSC's. ⇒ **Compare RATIOS between rungs, which are unit-free;
  compare absolutes only within one platform.** ⚠ `DefaultRepeatCount` is **1**, so a logged CPU row is
  a single sample.
- **There is no WALL time, deliberately.** It counts every other process on the box, so a dated table of
  it would compare a loaded machine against an idle one. (Measured: allocation deltas read 0.000 on an
  unchanged compiler while time deltas read +0.09…+0.29, and one run read `phase:parse` at ×5.03 then
  ×1.78 across a DOUBLING ladder — that is not a curve of any shape, it is preemption.) CPU time comes
  from `__Builtins.threadCpuTicks()`, which advances only while the calling thread is scheduled.

- **The suite's `compile CPU:` stderr line is a trend indicator, not a gate.** Thread CPU time drops idle and
  preemption but not contention (SMT siblings, shared cache, memory bandwidth), so the sum rises with worker
  count. Compare it only at equal worker counts on one host. A failed compile reports nothing and is counted
  apart, never as zero.
- **A delta flat at all six rungs is a once-per-compile cost**, which excludes every per-function, per-token
  and per-block explanation and points at the load phase. `collectMaxonSources` walks `stdlib/` once per
  compile and pays per entry, so a stray or gitignored directory moves the count, and it exists only in the
  checkout that holds it. Measure a free plausible explanation before writing it down.
- **Correcting a logged row edits the last row of all three tables with the same note.** `ScaleHistory` holds
  allocations, bytes and CPU to one date, path and note, and treats a mismatch as a hand-edited log, which
  stops the next `scale-test`.
- ⚠ **`--repeat` can report NONDETERMINISTIC on a tree nothing is wrong with.** `__gt_free_get` takes a record
  from the current P's free list, then the global list, else allocates, and the process-wide count sees the
  allocation, so whether a pool worker lands on the P that retired the last record is timing. One allocation
  of a green-thread record's size, confined to `phase regalloc`, is this and not your diff: re-run, or compare
  allocation columns against a control compiler at `--repeat=1`.

### The two blind spots — and never credit one with the other's fix

✅ **The CPU column exists because a cost that ALLOCATES NOTHING was invisible**, and this project keeps
measuring them — the op-insertion quadratic inside `regalloc:splitting` (68% of the whole compile at
N=1024, allocation-free), `requireInterfaceForParse` (allocates identically to the digit across a
+24.15 ms parse delta), `getBlockByIdIn`'s per-guard-site scan, the cascade fixpoint duals.

⚠ **That cures ONE blind spot. The other is the CORPUS**, and a Δ0 from a ladder that cannot express the
feature is the instrument's blind spot, not the cost — in **every** column, CPU included. *(Clearest
case: `regalloc:splitting`'s float-across-calls quadratic was hidden because the corpus's `floatSpill`
knob was 4 — few enough that every float fit a register. The knob went 4 → 12; that is a corpus fix.)*

⚠ **A CORPUS HOLDING A CONSTRUCT IS NOT A CORPUS REACHING ITS CODE PATH.** Narrow aliases that appear only as
generic arguments are erased before any range check sees them; an interface corpus with no `uses` clause never
runs the arity lookup. Count executions of the path, not occurrences of the construct. A linear-scan quadratic
is also invisible unless the target sits LAST in the scanned array: position is the variable, so a hand-built
ladder (`tests/ladders/`) places it there.

**When the corpus cannot express the feature, build a ladder in `tests/ladders/`** that holds the program
constant and doubles only the shape the change indexes by (`genwitness.sh`: conformers × methods). Check which
phase the growth landed in before believing an exponent: a generator with many calls in one function measures
the allocator, not the change. A superlinearity can sit on a clean compile, so read structure even when the
curve is flat. A gate that is cheap while a feature is absent can be a cliff from its first use, so ladder the
first use. Attribute a delta by moving the knob it claims to scale with: a formula that only fits the numbers
is not attribution.

### When the question is the EMITTED code, not the compiler's logic

A green suite and `scale-test` both measure the compiler's LOGIC, which every stage of the self-host
chain shares byte for byte. **The QUALITY OF THE CODE the compiler EMITS is a different question**, and
it is answered by four instruments, none of which is a single command:

- **`scripts/fixpoint.sh`** — builds stage-2 (stage-1 compiling the compiler) and stage-3 (stage-2
  compiling the compiler) and `cmp`s them. A difference is a **MISCOMPILE**, and this is the gate that
  says so. ⚠ It compares one compiler against its OWN successor, never two compilers: its header states
  the limit — *a wrong answer both stages agree on is invisible here*.
- **`scripts/emitted-code-count.py`** (~10 s) — a per-program op census over a fixed corpus, the cheap
  standing read on emitted-code debris. ⚠ Its unit changed on 2026-08-29 when `--emit-ir` stopped
  emitting library bodies; they are now opt-in BY NAME, so a count taken before that date is not
  comparable with one taken after.
- **`--emit-ir --emit-ir-runtime=<name>,<name>`** — renders named withheld bodies as Target IR, the same
  text a spec's ```RequiredRuntime block pins. This is the instrument when the subject is a SPECIFIC
  body rather than the corpus: point two compilers at one program and diff the sidecars.
- **`scripts/sample_profile.py`** — a standalone sampling profiler for Maxon-compiled executables, for
  function-level attribution when you need to know WHERE the emitted code spends its time. It resolves
  samples against the `__symtable` every backend embeds, so it needs no `.mxdbg`, and it reads both the
  bootstrap's `.symtab` placement and this compiler's `.text`-closing one. ⚠ Windows x64 only. It takes
  the command to profile as its trailing arguments; it is not a flag of anything.

**"No cost to programs that do not use it" is proved by a control.** Build the base from a named commit the
same way as the change, compile the same programs with both, diff, and classify each difference by whether the
program uses the feature. A declaration inserted mid-file in `stdlib/` moves the line numbers baked into panic
strings, so a new declaration goes at the end of its file.

**A byte compare writes both images to one output path**: an image can carry its own path. Run the same-binary
control first and report a difference only after it reads zero.

⛔ **NOTHING IN THE TREE PRODUCES A TWO-COMPILER `scale-test` RATIO TABLE** — stage-1 and stage-2
`scale-test`ed interleaved, so any allocation ratio above 1.00 is a construct this tree's codegen
allocates for and the seed's does not. Build one from `scale-test --repeat=N` runs of each compiler,
interleaved; within a run, every repeat of a rung reports identical memory.

⚠ **The two-compiler harnesses are `scripts/bench-fannkuch.py`, `scripts/bench-binary-trees.py` and
`scripts/bench-concurrency.py`**, which share `--ref <sha>` through `scripts/bench_common.py`.
`bench-fannkuch.py`, which the `fannkuch-iterate` skill drives, reports per arm fannkuch's wall time, the
executable's bytes, the emitted-code census and (`--self-compile`) the self-compile time. Each ref is a
git worktree at the harness's `<out-dir>/ref-<sha>/tree/`, built by that tree's own
`scripts/fetch-seed.sh` and `scripts/build-from-seed.sh` — seed → `C1` → `C2`, as CI builds it, so the
control carries its own runtime.

⚠ **Measure on an IDLE machine, and measure the instrument before the subject.** This project has had a
dominant cost hide in the *wrong timing bucket* four separate times. Load can MASK a bug, not just
inflate numbers.

- **Instrument phases disjointly** (X's time excludes the Y it calls), fail if the parts exceed the
  total, and print an explicit `(unattributed)` row.
- **A share is a ratio**: a term measured small behind a dominant one is not disproved; re-profile after
  the dominant term is removed.
- **Benchmarks of EMITTED code can swing tens of percent on code alignment alone**; a control benchmark
  the change cannot affect that shows a delta is the tell. Sweep padding (0–5 dummy functions) and
  believe a delta only when the spreads are disjoint and the candidate wins at every padding.

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
  **measured** linear-in-practice across the real corpus is filed as debt — WITH the measurement that
  shows it linear today and the trigger that would make it bend (an inliner, a machine-generated wide
  type). A curve you have not measured is not yet a debt; it is a defect to run down. A debt goes in
  your REPORT and in `docs/optimization-log.md`, and your caller fixes it — never files it to `todo.md`.
- **Check exit codes; never grep for a success string.** Exit **101** = memory leak.
- ⚠ Redirecting suite runs by hand, `--workers=1` and `fmt`'s path argument are in `maxon-bin/AGENTS.md`,
  once — not repeated here.

## Report

Each hot spot found, with `file:line` and its **complexity before and after**, and the **real
`scale-test` per-phase tables** before and after — the raw per-rung numbers, since the ladder doubles
and the RATIO between rungs is the growth. There is no verdict and no exponent table to paste.

**Do NOT claim codegen neutrality from the `ir-specs/` pins alone.** They cover the corpus's emitted-code
cases, not every program. If you need to show that a pass changed how the compiler RUNS without
changing what it EMITS, disassemble or use `--emit-ir-runtime=<names>` and say what you read.

**Never claim a measurement you did not take** — and if you could not make something faster, say so
rather than shipping a change that only looks like an optimization.
