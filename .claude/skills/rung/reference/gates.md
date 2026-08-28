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

### ⚖ A GOLDEN ON A LANE THAT CANNOT RUN ITS CASE: **DELETE THE GOLDEN, DO NOT WIDEN THE MARKER**

**User ruling, 2026-08-24, taken at `BATCH44` over 25 such files** (`<!-- targets: x64-windows -->`
async cases carrying goldens on three lanes that can never select them, plus one `mm-trace` case
whose capture is x64-windows-only *by derivation* — there is no marker on that one to widen).

**The `targets:` marker is the AUTHOR'S CLAIM about where a case runs. The golden is downstream of
it.** Widening a marker so an orphaned golden acquires something to compare against is *asserting a
case runs on a lane* in order to tidy bookkeeping — which is turning a gate green by changing what it
tests, one level removed. **The marker is right; the golden is the error.**

⚠ **A golden on a lane that can never compare it is worse than absent**: it is unmintable,
uncomparable, and it pads every count that a real absence would otherwise show up in. It is the
ORPHANED shape of the same family as ABSENT, STALE and UNCOMPARED — see the census in
`Testing/GoldenTracking.maxon`, which now reports all four.

⭐ **The converse still needs a reason, and stays available**: if a case genuinely *should* run on
more lanes, widen the marker **as a coverage decision, argued on its merits**, and let the goldens
follow. What is refused is widening it *because* a golden was sitting there.

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
