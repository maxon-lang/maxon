---
name: work-plan
description: Work one class plan under `plans/` from its first stage to its landing — make each stage as one local commit in the order the plan's table gives, then land the whole plan once as one commit and mark it done. Built for the least time and tokens that still land a correct plan. Use when the maintainer hands a session a plan file or a queue. Invoke as `/work-plan <plan>`, e.g. `/work-plan ownership`, or `/work-plan <queue>` to take that queue's next plan.
---

# Work a class plan

A plan under `plans/` is one class of defects with one design and an ordered list of stages. One session
works it from start to finish. `plans/README.md` puts every plan in one of two queues; a session works only
its own queue, so no plan is claimed. Given a queue letter, take that queue's first plan, in turn order,
whose "Done" cell is empty. The stages are local commits; `main` receives the plan once, as one commit.

**A plan is worked in its own worktree, on its own branch, never on local `main`:**
`git worktree add temp/<plan>/tree -b plan/<plan>-stages origin/main`. The checkout is shared with other
sessions, whose resets and pushes of `main` would drop or publish a stage commit sitting there. The branch
is never pushed: a pushed branch starts the landing workflow.

**A plan is the maintainer's, not the fleet's.** Send no `REQUEST` and run no `fleet.mjs` for it, whatever
`maxon.fleetRepo` says.

**The procedure spends as little as a correct plan allows.** The plan is the survey and the design: no
step here re-derives it, and none asks the user what the plan already answers.

| § | Step | Who |
|---|---|---|
| 1 | Make each stage: one local commit | you coordinate; implementers write |
| 2 | Land the plan once, and mark it done | you |

## 1. Make each stage

Read the plan's stage table, its rulings and its design once. Do not survey the code against the plan:
each stage meets its own facts when it starts.

Take the first stage whose "Done" cell is empty and whose "Waits on" is satisfied: every stage it names
in this plan is ticked, every stage of another plan has landed on `main` with that plan, every issue is
closed. A stage marked `deferred` is skipped. A stage marked "in progress under #n" is being made
outside the plan: tick it once that commit is on `main`, whether or not #n has closed, and start no stage
that waits on it before then. If no stage can start and some remain, tell the user what they wait on.

For each stage, in order:

1. **An open question takes the plan's recommendation**, including one that breaks code the latest
   release compiles; the stage then owes its `## Unreleased` entry. Record the choice under the plan's
   "Rulings". Ask the user only where the plan gives no recommendation.
2. **Fetch `main`.** If it moved, rebase the plan's commits onto its tip; a conflict is resolved by hand,
   never worked around. Re-run only the cases of a stage whose files the rebase touched.
3. **Watch the stage's spec set red.** The plan's "Shown by" line is its floor. There is no scout: the
   plan names the functions and files.
4. **Write the code through implementer agents.** A brief carries the stage's own section of the plan
   and the design points it cites, never the whole plan file.
   **Every agent runs on the cheapest model that can do its piece** (`/land`'s `brief.md`, "The model for
   each stage"), chosen per piece and named in the dispatch:
   - `haiku` for a mechanical piece: a rename, a re-pin, a tick, a doc line;
   - `sonnet` for the spec author, the documenter, and a contained piece whose approach the plan settles,
     which is most stages;
   - `opus` for a piece that is cross-pass or soundness-sensitive, involves performance or concurrency, or
     leaves a design choice open; for a diagnosis; for `optimize`; and for a review.
   **An implementer changes only what its stage names.** Every brief says so, and says that anything
   else it meets (a defect, duplication, a wrong name, code the stage leaves awkward) is reported, not
   fixed. This overrides `AGENTS.md`'s "fix what you find" for the life of the plan: the implementer
   cannot see the later stages, so only you can tell what they make irrelevant. For each finding:
   - **a later stage of this plan, or a stage of another plan, owns it** (its "Rule it establishes", its
     section, or a plan's Members list covers it): do nothing. Add a line to that stage's section when
     the finding is a case its red set should include, in this plan only;
   - **the stage's own cases cannot go green without it:** fix the least that makes them green, and say
     so in the commit message;
   - **nothing in `plans/` owns it:** record it as `/land` §10 says, and fix it after the plan lands.

   The same holds for a review: a finding a later stage or another plan owns is not a blocker.
5. **The stage's own cases green, and nothing wider.** Until §2, a run is `--filter`ed to the cases the
   stage wrote or re-pinned and the cases of a spec file it edited: never a whole spec file's suite for
   safety, a corpus, `ir-specs` unfiltered, a second target, or a lane. What a stage breaks elsewhere is
   §2's gates' to find, over the whole plan, once. Where the code contradicts the plan, the implementer's finding amends the
   stage in the plan file, in this commit. A contradiction with the plan's design stops and asks the user.
6. **`code-review` per stage only in `ownership` and `generics`**, where a slip is a soundness hole: one
   round, by an agent that did not write the code, blockers only. Every other plan is reviewed in §2.
7. **Write no comments and no documentation.** Both are written once, in §2, on the plan's final shape.
8. **Commit on `plan/<plan>-stages`, and push nothing.** The commit holds the stage's code and spec cases and
   ticks the stage's box. Its message carries what §2's message will need: the mechanism, the cases red
   to green, the figures measured, and any `RELEASE_NOTES.md` entry the stage owes, written out.

Carry on from stage to stage without checking in. Stop only for a contradiction with the plan's design,
a question the plan does not answer, or a red on `main` that is yours.

**The plan file is the state.** A session whose context has grown heavy ends after a stage commit; a
fresh one resumes from the ticks.
**A defect that is not this plan's** is recorded as `/land` §10 says, and fixed after the plan lands.
**Giving the plan up:** tell the user which stages are committed and where.

## 2. Land the plan once

When every stage is ticked or `deferred`:

1. **Leave `plan/<plan>-stages` as it is:** it is what a red after the landing is bisected over.
2. **Squash the stage commits into one commit** on a second local branch cut from it after a final
   rebase (`git switch -c plan/<plan>`, then `git reset --soft origin/main`), and land that branch as
   `/land` §9 lands a session's own branch. `main` takes one commit per landing; a branch of several is read as a bootstrap
   chain.
3. **`code-review` over the whole diff**, one round, blockers only, for every plan §1 did not review
   per stage.
4. **`optimize` only for `ownership`, `generics`, `declaration-identity` and `kept-answers`**, the plans
   that change how the compiler's cost grows with the program.
5. **`documenter`**: the comments, the `docs/` sources the plan's surfaces own, the site regeneration,
   and every `## Unreleased` entry the stages recorded.
6. **Mark the plan's "Done" cell in `plans/README.md`.** The plan file stays.
7. **`/land` §8's gates once**, over the whole plan: the first run wider than a stage's own cases. A
   plan changes far more than one stage did: run the whole `ir-specs` suite, not a filter, when any stage
   touched code generation. A red here is fixed in the squashed tree and the gate re-taken.
8. **`/land` §9:** one commit whose message carries each stage's paragraph in order, landed through the
   door. It ends `Fixes #<class issue>` when no stage is `deferred`; with one deferred it names the
   issue without a closing word, and the issue stays open for those stages.

A bounce at the door is `/land` §9's bounce: rebase by hand, re-take the gates whose inputs changed,
submit again.
