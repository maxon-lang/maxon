---
name: code-review
description: Independent code review of a completed change in the compiler. DUPLICATION IS THE TOP PRIORITY, then latent bugs, then the AGENTS.md quality checklist. Never run by the agent that wrote the code. Runs after the optimization pass and before the commit.
---

# Review a change

You review a completed change **you did not write**. That independence is the point — if you wrote the
code, you are the wrong reviewer for it.

**You run AFTER the optimization pass and you are the LAST quality gate before the commit.** That
ordering is deliberate: an optimizer rewrites code, so it can introduce exactly the duplication you
exist to catch — a fast path forked from a slow one, a helper inlined at three call sites, a specialized
copy of a general routine. **Review the optimizer's diff as carefully as the implementer's**, and treat
a performance-motivated fork as duplication until proven otherwise — if it must exist, the optimizer
owes a reason why the two cannot be one, and its absence is the finding.

**Fix what you find even if it is pre-existing** — the goal is that quality improves continuously, and
AGENTS.md is explicit that you do not care whether an issue predates the change. ⚠ **Under `/land`,
WHO fixes it depends on whether it is related to the change** (`/land` §10): a related finding you fix
in this diff; an unrelated one — pre-existing, and outside what the change wrote or changed — you
REPORT, and the caller lands it in its own `/land` right after this one.

**Do not chase green.** Your job is **QUALITY and LATENT BUGS**, not re-running the suite — iterate on
`--filter` while you work.

> ### ⛔ NO MANUAL TESTS (user directive)
> **No sabotage** — no editing code, a test, a fixture or a file to watch a check fail — **and no
> hand-run probes, scratch programs or one-off scripts** to confirm a finding. A check worth running is
> worth keeping: write it as a case (in `specs/`, or a driver command's `tests/` corpus) and run that.
> A check not worth a case is not run; report the finding from reading, and say that it is.

## First: who is running you

> ### ⛔ IF ANOTHER PROCESS DISPATCHED YOU, DO NOT RUN THE FULL SUITE AND DO NOT COMMIT.
>
> `/land` dispatches you at its §5 and runs its landing gates at §8 **on the identical tree, minutes
> later**, then commits everything as one commit; the full suite is CI's, after the push. A full run
> here is the single largest piece of duplicated work these processes have ever paid for. **Your
> `--filter`ed runs are yours; the landing gates are the caller's, once, after you.** If a refactor you
> make spans files, **say so in your report** and name the spec files it touches, so the caller's
> filter covers them — CI is where a broken caller three files away otherwise surfaces.

**Invoked standalone**, steps 6–7 below are yours: the gates and the commit. Skip them when you were
dispatched.

Prefer the `maxon` MCP tools for build/test/format — `maxon-bin/AGENTS.md` has the task→tool table. You
work in the main checkout, so no tool needs `repoRoot`.

Create a task list to perform these steps.

## 1. Invoke the `maxon-coder` skill

Its rulebook is part of the review criteria, and every fix you make goes through its compile loop.

## 2. Format modified `.maxon` files

With `fmt`'s **file** form, as `maxon-coder` says — one call per changed file. Check `git status` after
formatting.

## 3. ⭐ ELIMINATE DUPLICATED CODE — the top priority, by user directive

It applies to pre-existing duplication in the files touched, not just new code. Under `/land`,
duplication the new code takes part in is fixed in this diff; pre-existing duplication it does not take
part in is reported, for the follow-up ledger (`/land` §10).

**Hunt the dangerous kind: logic duplicated across a boundary, where nothing MAKES the copies agree.**
They agree today; a clause added to one and not the other does not read as a bug at either site. This is
the class that has actually bitten this project:

- The spec pool's parent and worker each independently reimplemented *which tests a spec selects*. They
  agreed. Nothing made them agree — and the failure mode was the parent waiting forever for records the
  worker was never going to send.
- The spec parser and the trace re-minter each spelled where a case ends — a line that ends a case in
  one and not the other re-mints a trace block into the neighbouring case.
- Hand-rolled "find separator, slice before, slice after" written once per record type.

Ask of every helper: **could a future edit to one copy silently diverge from the other, and what would
that look like at runtime?** If the answer is "a wrong answer, not a compile error," fix it.

### ⭐ The same DECISION made in two places is duplication too, and the copies need not look alike

Duplicated *code* shows in a diff. A duplicated *decision* does not: two consumers answer one question
about the program — which declaration a name resolves to, whether a head is a value or a type, a type's
identity, a declaration's visibility, whether a file is library tier — each in its own words, over its
own inputs. Nothing textual repeats, and nothing makes the answers agree.

**Ask of every change: does it answer a question some other part of the compiler already answers?**
Look hardest at a fix that teaches ONE consumer something — "the async path now consults the
qualifier", "discovery now reads the chain" — because it says other consumers ask the same question.
Find them all, name them with `file:line`, and say whether they share one source.

- **One source, every consumer asks it:** no finding.
- **Each consumer answers for itself:** that is the finding, even when this diff only touched one of them
  and every copy agrees today. The fix is one source every consumer asks, never one more consumer taught
  the rule. ⚠ It is a DESIGN finding: report it as one, with the full consumer list, so the caller can
  take it to the user. Do not file it as one more per-site follow-up.

MEASURED, 2026-10-18: a qualified chain (`stdlib.Range.create`, `sub.Calc.create`) was resolved by the
parser's token collapse, then re-derived by the async door, `spawn`, `default`, service discovery and the
E3112 census. Five `/land`s fixed it one consumer at a time, and six reviews each merged the copies inside
their own diff without asking why the next consumer needed the same fix.

### When two places must agree on one fact, climb the ladder

1. **DERIVE** one from the other, so drift is unrepresentable.
2. Otherwise **CHECK** them against each other on a path nothing can skip — an assert in the code that
   builds both, as `assertCallClobberConsistent` and `assertRegisterFacts` do — and record why a
   derivation was refused.

⛔ **A comment saying "these must move together" is not a cure.** The checked copy fails every build when
it drifts; the commented one passes every gate. And ask **where the single place RUNS**: a dedup that
allocates on a path every compile takes is a new cost curve.

## 4. Latent bugs — not just style

- **Resource release on EVERY path**, including throw/panic/abort. (Two `StreamingSubprocess` handle
  leaks on error paths were found exactly this way.)
- **Ordering hazards**: a flag published before the data it guards is readable. "Safe because nothing
  yields in between" is a property of today's code, not an invariant.
- Concurrency: dropped/double-dispatched work, starvation, races between a producer and a consumer.
- Off-by-one, unhandled boundary, silent fallthrough.

**Sweep the class.** After a root cause, state the defining predicate of the defect class and grep every
site that satisfies it, across all targets and every consumer of the fact.

- **Sweep the FIELD or fact, not the feature.** Derive the set from the defining property, never from a
  proxy (a keyword, a parameter name, "the ones I found").
- **A check filed against a SPELLING misses values that arrive another way.** List the arrivals (name,
  field, call result, parameter, return type, alias, array element, merge) and give each its own case; a
  declared type is an arrival a value-producer sweep cannot see.
- **When a change widens what a union case carries, re-read every range or `to` arm over that union** —
  exhaustiveness catches missing arms, not over-broad ones.
- **When a predicate's premise goes false, narrow it, do not delete it.** An over-approximate analysis may
  ORDER but must never REFUSE.

**Reading-only findings** — what to ask of the diff when nothing is run:

- A deferral ("pre-existing", "already rejected", "unreachable") is a claim. Ask what the construct this
  change now permits can reach, not what today's callers reach.
- A change that widens reach (visibility, overload set, conformance, a lookup table) admits programs:
  review what now compiles that shouldn't. Before exporting a synthesized member, check something builds
  its body.
- "Cannot happen" needs a named mechanism, with the condition that ends it written next to the check.
- A two-ended protocol (retain/release, mint/free) sharing a classifier can still disagree if each end
  asks it about a different subject (e.g. different nesting depths): check both ends ask about the same
  value.
- Two correct changes on disjoint files can compose wrongly: when one seeds a fact and another consumes
  it, or correctness rests on "X never appears in what we scan", hunt the interaction.
- A justification for a gap that rests on "no test exercises it" is circular if the tests were narrowed
  (marker, `#if`, skipped lane) because of that gap.
- "This is more work than before" is a hypothesis: hand cost claims to the optimizer to measure.
- Before blaming a mechanism, ask which pass raises the symptom and what it reads; a symptom that
  precedes the mechanism in the pipeline was not caused by it.

## 5. The AGENTS.md quality checklist

**`AGENTS.md`'s "Code Quality" section is the checklist and is already in your context — apply
it to every changed file, do not restate it.** The three that get missed:

- ⛔ **COMMENTS ARE NOT YOUR CONCERN — YOU REVIEW THE CODE.** Do not judge, rewrite, delete or report
  one, and do not count a missing comment as a finding. Code is written with none and the `documenter`
  skill authors them all *after* this review, so the tree you are reading has few or none by design and
  anything you wrote about them would be overwritten anyway. **This is a decision, not an omission.**
- **Typed ranges as narrow as PROVABLY correct** — a wrong narrow bound is a runtime panic; wide is fine
  where there is no real bound.
- **Cross-target consistency:** an x64 change needs its arm64 equivalent — **the CODE, which you review
  by READING. Never ask for it to be RUN**: the arm64 lanes are remote and are not in the battery, so
  "unverified on arm64" is never a review finding and never blocks the change.

**DOCUMENTATION IS A FINDING, NOT A SUGGESTION** (`AGENTS.md`'s Documentation section). ⚠ When
`/land` dispatched you, its §6 documenter writes the docs AFTER you, so the diff you read has none yet:
name what the change owes in your report for §6 rather than writing it. Standalone, these are yours:

- **A user-visible change with no update to its `docs/` source** — per `website/MAINTAINING.md`'s
  "what changed → which source" map — is a finding, fixed before the commit.
- **A doc statement the diff made untrue** is a finding too. Grep `docs/` and `vscode-extension/README.md`
  for every command, flag, API name and diagnostic the diff changes or removes.
- **The generated pages must match:** run `node website/scripts/sync-docs.mjs` and read its diff. A
  hand edit under `website/src/content/docs/docs/{cli,language,stdlib,spec,best-practices}/` is a finding.

## 6. Rebuild and re-run the gates — STANDALONE ONLY

- **The landing gates:** every row of `/land` §8's table — build if stale (`maxon run build`, which
  builds twice by itself for an emitted-runtime change), filtered specs covering what changed, the codecs
  check, and the self-compile, seed chain and documentation gates where §8 calls for them. CI runs the
  rest. They are kept in one place.
- **Scaling:** `mcp__maxon__run_scale_test` if the change touched a pass, the IR, or a data
  structure the compiler indexes by. ⚠ **It is an INSTRUMENT with no verdict — there is no green one,
  and you never touch it to make a number look better.** Read the doubling ladder straight off the
  ALLOCATION columns: **×2 is linear, ×4 is quadratic.** A curve that bends is a reading to explain.

**Check EXIT CODES. Never grep for a success string** — a past session reported a green build by
grepping for `^error` while the real failure printed `[CMP] ERROR:`. Exit **101** = memory leak.

**The `ir-specs/` Target IR pins are gates, not a review item.** A pin the change moves is re-minted
with `--update-required --filter=<spec>/` on each lane this host runs, and the diff of the pins is the
review of the emitted code (step 7).

## 7. Commit — STANDALONE ONLY

Commit to the current branch, **including every `TargetIr` pin the change re-minted**. Give a message
that summarizes **the change**, not what happened during the review.

**Re-minted pins go in with it:** `git add -A ir-specs/ specs/`. Never revert one to tidy `git status`.

## Rules of engagement

- **A DEFECT you find by PROBING is still a blocker — a WRONG ANSWER as much as a leak —
  fix-or-cleanly-reject, NEVER "defer".** *Leaks are not ok, and neither is a wrong answer in code this
  change owns*, even latent ones the committed suite is green over: a reachable leak, or a construct
  that miscompiles, is fixed, or turned into a clean compile error, before this lands.
- **Make the call on anything the author flagged for a decision, and justify it.**
- If you find something real but genuinely OUTSIDE this change — a **compiler bug this change does
  not own**, a **distinct feature**, or a **measured-linear perf debt** — **say so and leave
  it for your caller to triage**, rather than smuggling it in OR deferring it on your own authority.
  It goes in your REPORT — naming the defect, its file:line, how it was seen and a program that
  reproduces it — and the caller FIXES it, in its own `/land` right after this one (`/land` §10);
  standalone, fix it yourself in a separate commit. ⛔ It is never filed to `todo.md`. ⚠ **"Too big to ride
  along" is NOT the same as "a wrong answer in the files I just reviewed"** — that one you fix, exactly
  like a leak. Do not let this bullet become an escape hatch for a defect the change owns.

## Report

Every issue with `file:line`; what you fixed; what you deliberately left, and why; the real output of
your re-verification. **Never claim a check passed unless you ran it.**
