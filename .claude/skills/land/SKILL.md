---
name: land
description: Implement one self-contained change end to end and push it — decide the MINIMAL spec set and watch it go RED first, write the code, green the set on a filter, optimize, independent review, rebase on origin/main, then ONE gate battery (full shv2 suite + wasm lane + self-compile), commit and push. The lightweight sibling of /rung — main checkout, no worktree, no wave, no slice board, no contract, no PLAN.md bookkeeping. Use for a bug fix, a missing diagnostic, a small feature, a defect found by hand, or any change that does not need a rung. Invoke as `/land <what to do>`.
---

# Land one change

**One invocation = ONE change: specified, implemented, reviewed and pushed.** You work directly in the
main checkout, alone on `main`, and you run the expensive gates **exactly once, at the end**.

`/rung` is for a rung of `maxon-shv2/PLAN.md` — a named feature with a contract, a wave of agents and a
worktree. `/spec-port` is for one spec off v1's whitelist. **This is for everything else:** the bug you
just found, the diagnostic nobody wrote, the pass that answers wrong, the small feature that never
earned a row on the board.

## The shape — and the economy that makes it cheap

| | | red here means |
|---|---|---|
| **1** | Decide the **MINIMAL** spec set. **Watch every case FAIL.** | a case that is green *now* tests nothing about your change |
| **2** | Write **all** the code | |
| **3** | Green the set — **on the FILTER, never the full suite** | keep going |
| **4** | Optimization pass | a ladder you cannot explain |
| **5** | Independent review — an agent that did **not** write the code | fix before the commit |
| **6** | **Rebase** on `origin/main` | resolve by hand |
| **7** | THE BATTERY, once: full shv2 suite · wasm lane · **self-compile** | **stop** |
| **8** | Commit and push | a rejected push re-runs §7 |

**Everything before §7 runs on one `--filter`.** That is what makes this lightweight — and it is only
honest because §1's red was real. The filter is a *proxy* for the suite, and a proxy you never saw fail
is a proxy for nothing.

⚠ **Steps 4–7 do not reorder.** Optimize *before* review (an optimizer rewrites code and can introduce
the very duplication the review exists to catch). Review *before* commit (a review after the commit is a
bug report; before it, it is a gate). **Rebase before the battery** — a battery run on a tree you then
rebase has tested something you are not pushing.

---

## 0. Start

**Say in one or two sentences what the change is and what will prove it**, before touching anything, so
the user can redirect you cheaply. If the ask is ambiguous *in a way that changes the code*, this is the
cheapest moment to ask.

- ⚠ **`git status` must be CLEAN.** The suite MINTS goldens as it runs; on a dirty tree you cannot tell
  yours from the leftovers.
- **BUILD.** Both binaries are gitignored and nothing rebuilds them, so a stale one lies in *both*
  directions. `build target=csharp` first if `maxon-sharp/` is newer than `bin/` (shv2 is built BY the
  bootstrap), then `build target=shv2`. This is not a baseline — it is making the binary current, and
  every red you read in §1 is read off it.
- **No baseline suite run.** The §7 gate is `failed: 0`, not a delta from a remembered total, so there is
  nothing to measure yet. (When §7 comes back red you therefore may not assume the red is yours — §7 says
  how to attribute it.)

## 1. The MINIMAL spec set — and it goes RED before you write any code

**Minimal means the smallest set that FAILS today for the reason you are about to fix, and would fail
again if the fix were reverted.** Not "few": one case per distinct wrong answer, plus the error path if
the change adds or moves a diagnostic. A case that cannot fail for your reason is padding; a wrong
answer with no case is the thing this skill exists to prevent.

**Where the cases come from, in this order:**

1. **An existing `specs-shv2/` case.** Grep first — the behaviour may already be pinned, and then your
   set is "these three, which are red". A `<!-- disabled-test:` marker your change unblocks is a case you
   **enable**: those markers are DEBT, not precedent.
2. **A new case in the `specs-shv2/*.md` file that already owns the behaviour.** Format:
   `docs/SPECS.md`. Follow the neighbouring cases' shape.
3. **If the natural home is a `/specs` file that has NOT been ported**, port the WHOLE file — that is
   `/spec-port <name>` — rather than hand-copying one case out of it. A curated subset is a spec *you*
   wrote, and no count check can see what you left behind.

⛔ **Cases go in `specs-shv2/`.** shv2 is the product; the bootstrap is the means (user ruling,
2026-08-29). Write a `specs/` case only when the C# bootstrap itself is what you are changing.

**Then SEE IT RED.** A spec is data — no rebuild needed to run one.

```
run_spec_test compiler=shv2 filter=<pattern>
```

- **Read the failure of EVERY case in the set** and record the exact symptom — exit code, stderr, diff.
  That is your acceptance criterion *and* the most valuable input to the diagnosis.
  (`spec_test_outcome` with the same `filter` gives per-case PASS/FAIL detail when the summary is not
  enough.)
- **A case that is already GREEN is not in the set.** Either it does not test your change, or the change
  is already done. Find out which before writing a line.
- ⚠ **`--filter` is ONE substring**, matched against the `<spec>/<case>` label (`selectedByFilter`) — no
  lists. Name new cases so one distinctive substring selects the whole set; otherwise run one filter per
  file and read every one of them.

> ### ⛔ COUNT WHAT RAN. A spec can pass by running NOTHING — three ways, all of which read as green.
> ```bash
> grep -c '<!-- test:'          specs-shv2/<file>.md   # must EQUAL the runner's `total` for your filter
> grep -c '<!-- disabled-test:' specs-shv2/<file>.md   # cases you did not enable — you may write none
> grep -o '<!-- \(disabled-\)\?test: [^ ]*' specs-shv2/<file>.md | sed 's/.*test: //' | sort | uniq -d
> ```
> 1. **A shortfall against the runner's total is a defect in the spec, never a pass.** The two causes are
>    `status: draft` in the frontmatter (returns ZERO tests for the whole file) and a `## ` heading, which
>    **ENDS the active-test region** — every case below a stray `## Notes` silently disappears.
> 2. **The `uniq -d` must print nothing** — a name spelled both `test:` and `disabled-test:` reads as
>    disabled while it still runs.
> 3. ⛔ **You may not write a `disabled-test:`.** A case you cannot make pass is a HALT, not a marker.

## 2. Write the code

**All of it, in the main checkout.** Nobody else is here — no worktree, no lock dance. *(If the change
wants two agents on overlapping files, it is a rung: HALT and say so.)*

- **Run `maxon-coder` before writing any Maxon.**
- **Root causes, no workarounds** — and a defect is fixed whether or not it predates you (CLAUDE.md).
- **Cross-target consistency**: an x64 change needs its arm64 equivalent. The wasm lane runs in §7 and
  is not scalar-only — a float or `String` case failing there is a bug on that lane.
- **A new diagnostic goes through the registry**: an entry in `docs/error-codes.txt`, then
  `maxon error-codes generate`, then the code that emits it. Never hand-edit a generated enum, never
  grep one for a free number, never write a bare `"E3010"` in source.
- **Delegate when the diagnosis runs long** — `maxon-spec-implementer` works in the main checkout on
  exactly this shape of gap. Hand it the symptoms and **the diagnosis you have already done**; making it
  re-derive that is the one waste delegation is supposed to prevent. Brief it for DIAGNOSIS, never for
  COVERAGE: the gates below already prove coverage, better, one step later. Whoever writes the code,
  §5's reviewer must not be them.

## 3. Green the set

Iterate **edit → build → filtered run**. Rebuild every time; a stale binary reads green or red for the
wrong reason.

- ⛔ **Do not run the full suite in this loop.** It is §7's, once. A suite run per iteration is the
  single largest piece of duplicated work these processes have ever paid for.
- ⛔ **Never turn a case green by narrowing what it tests** — not the spec text, not the filter, not a
  marker. A green suite that tests nothing is the most expensive lie a test runner can tell.
- **A defect you find on the way is FIXED, not filed** — a wrong answer as much as a leak, whether or
  not the suite is green over it. **And the probe that found it becomes a case in the set.** "I verified
  it by hand" is discovery, never evidence: it is unrepeatable, unreviewable, and invisible to the wasm
  lane a spec case reaches for free.
- **When the set is green, re-run §1's count check**, then stop editing for correctness.

## 4. The optimization pass

**Three parts, and only the first is unconditional.**

- **(a) Read your own diff for unscalable structure — always, minutes.** An O(n) lookup inside an O(n)
  walk; a `findFirst` over a compiler-sized array in a loop; a fixpoint, liveness or dominator tree
  recomputed per element; a walk over a dense index space where the set bits were the point; an
  allocation in a hot path. The compiler must stay LINEAR in program size.
- **(b) The ladder — `run_scale_test` (~17 s, shv2 only)** when the change touched a pass, the IR, or a
  collection the compiler indexes by. ⚠ **It is an INSTRUMENT with no verdict — there is no green one, and
  you never touch it to make a number look better.** Read the doubling ladder straight: **×2 is linear,
  ×4 is quadratic.** Read it off the **ALLOCATION** columns, which are exact and bit-for-bit
  reproducible; the CPU column carries a few-percent noise band and a platform-defined unit. A Δ0 from a
  ladder whose corpus cannot express your feature is a blind spot, not a result.
- **(c) The `maxon-rung-optimizer` AGENT — only on a trigger:** the change adds a pass, an IR op, or an
  indexed collection; or (b)'s allocation ladder bends ≳2.4 per doubling and you cannot explain it. **If
  no trigger fired, say so in the report** — a superlinear hunt over a change that added no algorithm has
  nothing to find.

**If you ran the ladder, write the row in `docs/optimization-log.md` now** — attribution is only
available now; the instrument sees exactly WHAT moved and can never see WHY, and ten changes later
neither can you. ⚠ **Write no row you did not measure.**

## 5. The review

**`maxon-rung-reviewer` on the working diff — uncommitted, main checkout — after §4 and before the
commit.** It must not be the agent that wrote the code; that independence is the whole point.

- **Brief it for the "Code Quality" checklist in `.claude/CLAUDE.md`** — **duplication first**, including
  pre-existing duplication in the files touched, and especially logic copied across a boundary where
  nothing MAKES the copies agree — then latent bugs: resources released on every path, flags published
  before the data they guard, dropped or double-dispatched work.
- ⛔ **Do NOT ask it to run the full suite, prove coverage, or establish correctness.** Your battery runs
  minutes later on the identical tree, and the suite is a far better false-reject detector than anything
  it can probe by hand. Its `--filter`ed runs are its own. ⚠ A specific instruction in your brief
  OUTRANKS its standing stop rule — you cannot brief thoroughness in without briefing the stop out.
- **The bounds questions are YOURS**, and take seconds off `git status --short` and `git diff --stat`:
  did anything land outside this change? Is the diff as small as the fix warranted? **Did any spec
  expectation get weakened to pass?**
- **Findings are FIXED BEFORE the commit**, then the filtered set re-run. ⚠ Its report is a lead, not a
  verdict — this project's history is full of agent findings that measurement refuted. Confirm before
  acting, and say so if you disagree.

## 6. Rebase on `origin/main`

```bash
git stash push -u -m land && git pull --rebase && git stash pop
```

**Rebase FIRST, so §7 runs on the tree you will actually push.** Other agents land on `main` between and
during changes; a battery run before the rebase measured a tree that no longer exists.

- **A conflict is resolved BY HAND, hunk by hunk.** ⛔ Never `git checkout --ours/--theirs` a doc file to
  clear one: a whole-file resolution in `PLAN.md` has un-closed a landed rung, and it looks clean in
  `git diff`.
- **A stash must never outlive this step.** If the pop conflicts, resolve it now — work sitting in
  `git stash list` looks exactly like work that vanished.

## 7. THE BATTERY — once, in this order, on the rebased tree

| Gate | |
|---|---|
| **Build** exit 0 | `csharp` if it is stale, then `shv2`. Always — both are gitignored and the rebase may have moved their sources |
| **Full `run_spec_test compiler=shv2`** | **`failed: 0`**, and no exit **101**. The gate is zero failures *including every pre-existing test*, never a total |
| **`run_spec_test compiler=shv2 target=wasm32-wasi`** | `failed: 0`. Default battery, not an extra (user ruling, 2026-08-29) |
| **SELF-COMPILE** — `maxon-shv2/.maxon/maxon-shv2.exe build maxon-shv2 -o temp/land-selfcompile` | exit 0, ~3 min. Output discarded; only the exit code matters |
| **Minted goldens tracked** | `git status --short specs-shv2/fragments/` → `git add` every one. Untracked is invisible to `git status` noise and to every later count alike |
| **§1's count check** on the final tree | markers == ran, none disabled, no name spelled twice |
| **Bootstrap suite** | **ONLY if a bootstrap problem is suspected** — you touched `maxon-sharp/` or `stdlib/`, or the thing being verified is a cross-compiler answer. Otherwise skip it (user ruling, 2026-08-29). ⚠ *Building* the bootstrap is a different question: required whenever it is stale |

> ### ⭐ THE SELF-COMPILE IS THE GATE THE SUITE CANNOT SUBSTITUTE FOR
> `checkUnusedExports` (**E3092/E3093/E3094**) runs only when shv2 compiles a *program*, and the only
> program that names every `export` in `maxon-shv2/Compiler/` is the compiler itself — the bootstrap
> builds shv2 without that pass. **A three-line commit once broke stage-2 with the suite at 6673/0.**
> Run it after any change that adds or removes an `export` or a `module`, or adds a file declaring
> TYPES; run it anyway, it is three minutes. An E3092 here is a declaration more visible than its uses:
> narrow it, or land it *with* its first consumer.

**A red gate STOPS the change** — and a red you did not cause still gets fixed, in its own commit,
before yours (CLAUDE.md). To attribute it: do the failures touch what you changed? If that is not
obvious in a minute, **measure the control** — `git stash -u`, re-run, `git stash pop`. ⚠ **A lane that
did not RUN is not a red gate** (remote arm64 is outside this battery): that is a SKIP you report, never
folded into the green.

## 8. Commit and push

One commit, on `main` — this repo develops there; do not branch.

**The message carries what the diff cannot:** the wrong answer that motivated the change, the mechanism,
the cases that went red → green, and the gate figures you measured. ⚠ **Never a figure you did not
measure** — a number nobody took looks exactly like one that was.

```bash
git push origin main
```

⛔ **A REJECTED PUSH IS NOT A RETRY.** Someone landed while you were running, so the tree you tested is
not the tree you would push: **rebase and RE-RUN §7** before pushing again.

Then report in a few lines: the change, the cases that went red → green, each gate's number, and
anything skipped with the reason.

## ⛔ HALT AND ASK

Everything above runs unattended. Stop and report, without landing, when:

- **A gate is red and the fix is not yours to make.**
- **A DESIGN RULING is needed** — the specs and shv2 disagree about what is *correct*, or `/specs`
  contradicts itself. **Do not guess, and never edit a spec to match the compiler**: that is how a
  compiler bug becomes a specification.
- **A case would have to be disabled**, or **an existing `specs-shv2` expectation rewritten**. The first
  is the failure mode this whole process exists to prevent; the second has a blast radius beyond your
  change and belongs to whoever owns that behaviour.
- **It is really a rung** — it needs new IR ops with a contract, or two agents on overlapping files, or
  it closes a `PLAN.md` row. Say so and switch to `/rung`.

⚠ **"This is big" is NOT on that list**, and neither is "I could not find the gap" — that is diagnosis
you have not finished. The only honest stop is a question the user has to answer.

## The thing this process exists to catch

**A change that is green because nothing tested it.** Everything expensive here runs once, at the end,
and every cheap thing before it is a proxy — so the whole loop rests on §1's red being real. **Watch
every case fail before you write the fix, count what actually ran, and never make a case go away.**
