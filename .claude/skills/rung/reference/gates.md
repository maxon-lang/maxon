# The gates — what each one means, and what it deliberately does not

*Referenced from `SKILL.md` §7–§8. `scripts/rung-finish.sh` runs every gate below and refuses on each.
This file is what a red one MEANS.*

| Gate | |
|---|---|
| **Build** | exit 0, zero warnings. **ALWAYS BUILD** — both binaries are gitignored and nothing rebuilds them. A stale one lies in BOTH directions: measured 2026-07-27 on a clean `main`, the tree's `maxon-shv2.exe` read **71 FAILED** where a 13 s rebuild read **1922/0**, and `bin/maxon.exe` was stale against `maxon-sharp/` on that same tree. The build is 13 s. It is never the thing to save |
| **shv2 suite** | all green, **including every pre-existing test**. The gate is **`failed: 0`** — never a total. A total goes wrong when nothing is wrong (other agents land between and during rungs) and can come out RIGHT while something is (a spec that silently dropped two cases plus a suite that gained two elsewhere sums to the number you expected) |
| **Leak** | no run exits **101** — **and no reachable leak, including one found only by adversarial PROBING** (a `let m = f()` no committed test runs). A probed/latent leak is FIXED, or the leak-causing construct cleanly REJECTED, before merge — **never deferred as a live leak**. A green suite is not proof of no leak; it is proof no *committed test* leaks. ⇒ **and the probe that found it becomes a COMMITTED SPEC** |
| **Untracked goldens** | zero. A minted golden left untracked is invisible to `git status` noise and to the summary line alike — found three times in one session, every time by a LATER rung's baseline |
| **Golden DRIFT delta** | see below — the gate that did not exist |
| **`scale-test`** | ⚠ **NOT A GATE — an INSTRUMENT with no verdict.** See below |
| **C# lane** (if `maxon-sharp/` was touched) | C# suite green **AND codegen neutrality**: `git status --short specs/ specs-shv2/` EMPTY |
| **Cross-target** | every **locally runnable** target. Remote arm64 is NOT in this battery and is NOT required. Not run ⇒ SKIP (say which); ran-and-failed ⇒ **RED** |

---

## ⛔⛔ THE GOLDENS ARE REFERENCE, NOT A GATE — and `git status` cannot read them

**User ruling, 2026-08-02.** A fragment mismatch never carries a `FAIL` marker, never counts as a
failure and never touches the exit code. **Nothing here is red or green.**

⛔ **These reports used to FAIL the run, and that is exactly why they no longer do:** an x64-linux red
read as *"10 stale golden mismatches + 9 others"* — and the 9 were nine float programs exiting 1 (row
`X5`), unlooked-at for a day, because ten pieces of bookkeeping in the same list looked as red as they
did. **A red suite now means a program did the wrong thing. Read it; there is nothing in it to
regenerate away.**

### The instrument is the runner's trailer, and the answer is the DELTA

**A clean `git status` over `specs-shv2/fragments/` is NOT evidence that the emitted code is unchanged**,
and it was reported as exactly that in **five rungs** (`X1`, `N3`, `X5`, `X6`, `A3h`). `git status`
answers *"has anyone REGENERATED a golden"* — i.e. whether someone ran `--update-required`. **A tree can
have every golden mismatching and a perfectly clean `git status`.** The two questions are unrelated.

The runner prints, on stderr, and **silently when nothing drifted**:

```
note: N golden fragment(s) no longer match what the compiler emits. GOLDENS ARE REFERENCE, NOT A GATE
```

**And N alone is still not the answer**, because a rung inherits whatever drift `main` already carries.
⇒ **The answer is the DELTA against the merge base built the same way.** `A3h` read 34 at its tip
against 33 at its base, which is how its one genuinely moved fragment was found and explained.

**`rung-finish.sh` measures both ends**: it builds `origin/main` in a scratch worktree and runs its
suite purely to subtract. ~30 s, and it is the only honest way to say *"this rung did not move codegen."*

**A non-zero delta is NOT a failure.** It is a codegen change that must be EXPLAINED —
`--codegen-note-file`, and the explanation goes into the rung report. *"Is this change intended"* is not
a question a machine can answer; *"did one happen"* is, and now it does.

⚠ **A codegen change leaves `fragments-arm64-*` STALE, and those goldens can only be minted by the lane
that emits them** — so they are minted at the periodic remote sync, **not at the rung**. Do not hold a
merge for them, do not hand-edit them to look current, and **say in the rung report that the rung
changed codegen and the arm64 goldens are therefore owed a mint.**

⚠ **A FILTERED run's fragments are not authoritative — IN THE BOOTSTRAP.** Its runner batches a
spec's tests into a shared module and slices the IR per test, so literal-pool indices (`__str_N`,
`__static_lit_N`) depend on *which* tests are in the batch. **Regenerate `specs/` goldens only from an
unfiltered run.**

⛔ **THAT IS FALSE OF shv2, AND THIS FILE ASSERTED IT OF BOTH — MEASURED AT `BATCH44`, 2026-08-23.**
`maxon-shv2/Testing/SpecTestRunner.maxon:2904-2922` states the opposite invariant outright and names
its other end: `runOneSpec` walks its selected tests ONE AT A TIME, `stageTest` puts that test's source
on disk alone, and one `compileToProduct` compiles it — so **`--filter` cannot move a byte of any
fragment it does not exclude, and a filtered `--update-required` regenerates exactly what an unfiltered
one would.** `scripts/remote-mac.sh` RELIES on it to carry a filtered arm64 run's goldens home, and
excludes the C# suite for precisely the reason shv2 needs no exclusion. Twice measured: `BATCH44`'s 8
re-minted x64-windows goldens came back **byte-identical** after a full unfiltered re-mint, and `G20`
had already shown `--filter=<spec>` and `--filter=<spec>.<one test>` produce byte-identical output.

⭐ **The reason it is worth stating rather than deleting**: since `G20` that independence is a property
of the compiler's PER-COMPILE STATE, not of process isolation. Every test used to get its own `build`
subprocess, which made the claim free; now hundreds of compiles share one worker, and what carries it
is that each builds a fresh `Project` with every memo, interner and counter hanging off it. **If shv2
ever compiles several tests together, this paragraph and `remote-mac.sh`'s exclusion list are the two
ends of one invariant and must change together.**

---

## `scale-test` — an instrument, and there is no green one

**It collects data for TREND ANALYSIS. It has no verdict and there is nothing to pass.** It exits **0**
whatever the numbers say; a non-zero exit means the **RUN ITSELF BROKE** and produced no valid data.

- **The ladder DOUBLES, so the RATIO between consecutive rungs IS the growth.** Linear ⇒ allocations
  double. Quadratic ⇒ they quadruple. You read it straight off the raw numbers. **There are no exponent
  fits**, because a fit adds no information the doubling ladder does not already give you — it is
  interpretation dressed up as measurement, and it is what once dragged in a NOISY verdict that led an
  agent to **edit the instrument to stop it complaining**.
- **MEMORY columns are exact and bit-for-bit reproducible: ANY movement is real.**
- **The CPU column has a NOISE BAND of a few percent** (turbo, thermal, cache pressure) and a
  platform-defined unit. Against *"×2 is linear, ×4 is quadratic"* that band has a 100% margin. Against
  a claimed 3% constant-factor win it is worth nothing — use allocations for that.
- **There is no WALL time and there never will be.** It counts every *other* process on the box —
  measured, one run read `phase:parse` at ×5.03 then ×1.78 across a DOUBLING ladder, which is not a
  curve of any shape, it is preemption.

⚠ **DO NOT CHASE A GREEN SCALE-TEST. There isn't one.** A curve that looks wrong is **a reading to
explain**, not a light to turn green. **Never touch the instrument to make a number look better.**

**The artifact is the trend: `docs/optimization-log.md`.** Record what moved and WHY — **attribution is
only available now.** The instrument sees exactly WHAT moved and can never see why; ten rungs later,
neither can you. `rung-finish.sh` refuses to close a rung that wrote no row (or an explicit
`--no-ladder-row "<reason>"`).

⚠ **A Δ0 from a ladder that cannot express the feature is not evidence.** That is the CORPUS blind spot,
and the CPU column does not cure it — `regalloc:splitting`'s float-across-calls quadratic was hidden
because the corpus's `floatSpill` knob was **4**, few enough that every float fit a register.

---

## The CROSS-TARGET gate, and the arm64 trade

**The battery proves the rung on exactly ONE target: whichever one this host happens to be.** Everything
else the compiler emits stays unverified until somebody eventually runs it — and *"somebody eventually"*
is how **317 stale `specs/fragments-arm64-macos/` goldens** came to sit on `main` unnoticed, through a
run of rungs that were all green on x64-windows. **A green suite on one target is evidence about one
target.**

`--skip-build --skip-host` is the RUNG invocation **and it costs no coverage**: `--skip-build` **refuses
outright** if any source is newer than the binary it would have built (it checks — it does not take your
word for it), and `--skip-host` prints the host lane as **`PRIOR`**, not SKIP, naming what covered it.
*(Measured 2026-07-27: better than half the gate was re-derivation.)*

### ⛔ The REMOTE (arm64/Mac) lanes are NOT part of this gate — user, 2026-07-27

> **⭐ ARM64 VERIFICATION IS NEVER A CONDITION FOR COMPLETING A RUNG (user, 2026-07-28).** Not the
> suite, not the goldens, not "let me just try the Mac first." **A rung that is green on every LOCALLY
> runnable target is finished.** An unverified arm64 lane is a **reported SKIP, not a residual.**

**Everything expensive about them is the REMOTE part, not the arm64 part**: a bundle transport, a second
checkout's build, an OrbStack guest, and a machine that can be asleep, wedged, or behind flaky mDNS.
**They cost the rung more than they caught** — one wedged `orb run` preflight alone burned ~95 minutes
and produced *no verdict at all*.

- **The gate skips them by default and SAYS SO** — two SKIP rows with the reason, so a green run can
  never be read as full cross-target coverage.
- **The sync is a separate, periodic, manual run:** `scripts/cross-target-gate.sh --mac --require-mac`
  (or `bash scripts/remote-mac.sh --host=<user@mac> --shv2` for the native macOS lane alone).
  **Not your call to schedule as part of a rung.**
- ⚠ **This is a deliberate COVERAGE TRADE, not a claim arm64 is fine.** **Do not describe a rung as
  cross-target verified on arm64.** The trade is only honest if the SKIP is stated — an unreported skip
  converts *"we chose not to check"* into *"we checked"*, which is the one failure this section guards.

| | |
|---|---|
| **Not run ⇒ SKIP, and the gate still passes** | A missing runner, or a lane deliberately out of scope, must not block a rung. |
| **But a SKIP is REPORTED, never folded into the green** | It means **UNVERIFIED**, not *proven good*. **Name the skipped targets in the rung report** — the one thing worse than not testing arm64 is believing you did. |
| **A target that RUNS and FAILS is RED** | A rung-halting gate — HALT AND ASK. No flag softens it, and you never turn it green by dropping a target. **This holds on the manual sync too**: a red arm64 lane found by a periodic sync is a real defect, fixed, not filed as *"the sync was red."* |

⚙ **TWO COMMANDS IN ONE TREE ARE REFUSED BY THE BINARIES, not by a sentence.** `build`, `spec-test` and
`scale-test` take a **tree lock** (`maxon-shv2/Compiler/TreeLock.maxon`, `maxon-sharp/TreeLock.cs`); the
second exits **2** naming the holder's pid, argv and how long since it made progress. Worktrees hold
separate locks. **Know what it is when you see it**: the failure it replaces was expensive both ways —
two suites sharing `.spec-tmp` produced a **FALSE RED** in a lane that was actually green, and two
builds sharing `maxon-shv2/.maxon/` produced a **12-minute build and a silent exit 1** that a reviewer
read as a compile-time regression in the code under review. **If a lane goes red, re-run it ALONE before
reporting it.**

---

## There is NO worker-count invariance gate

It was deleted because it re-derived a known answer at full suite cost. **The reasoning, and the
`--workers=1` rule itself, are in `.claude/CLAUDE.md`, once** — this file used to carry a second copy,
and three agent briefs carried a third, fourth and fifth.
