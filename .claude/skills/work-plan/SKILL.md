---
name: work-plan
description: Work one class plan under `plans/` from its first stage to its landing — check the plan against the code, make each stage as one local commit in the order the plan's table gives, then land the whole plan once as one commit and mark it done. Use when the maintainer hands a session a plan file or a queue. Invoke as `/work-plan <plan>`, e.g. `/work-plan ownership`, or `/work-plan <queue>` to take that queue's next plan.
---

# Work a class plan

A plan under `plans/` is one class of defects with one design and an ordered list of stages. One session
works it from start to finish. `plans/README.md` puts every plan in one of two queues; a session works only
its own queue, so no plan is claimed. Given a queue letter, take that queue's first plan, in turn order,
whose "Done" cell is empty. The stages are local commits; `main` receives the plan once, as one commit.

**A plan is the maintainer's, not the fleet's.** Send no `REQUEST` and run no `fleet.mjs` for it, whatever
`maxon.fleetRepo` says. Every ruling and approval comes from the user, through the ask tool.

| § | Step | Who |
|---|---|---|
| 1 | Read the plan against the code | you, with dispatched readers |
| 2 | Make each stage: one local commit | you coordinate; implementers write |
| 3 | Land the plan once, and mark it done | you |

## 1. Read the plan against the code

Read the whole plan file: its stage table, its rulings, its design, its members. Then check it against
the tree as it is now, one dispatched reader per stage area, asking only: does each function, type and
file the stage names still exist with the shape the plan describes?

- **The plan is not redesigned here.** A stage whose facts have moved is amended in the plan file, in
  place, in the stage commit that meets it.
- **Where the code contradicts the plan's design, stop and ask the user.** Write the answer into the
  plan's "Rulings" section; it lands with the plan.
- **Tick what is already true.** A stage the table marks "in progress as #n" is ticked once #n is closed
  and its change is on `main`.

## 2. Make each stage

Take the first unticked stage whose "Waits on" is satisfied: every stage it names in this plan is ticked,
every stage of another plan has landed on `main` with that plan, every issue is closed. If none is left
and stages remain, tell the user what they wait on.

For each stage, in order:

1. **A "Needs a ruling" entry is a question to the user first.** Record the answer under "Rulings".
2. **Fetch `main`.** If it moved, rebase the plan's commits onto its tip and re-run the filtered specs of
   the stages already made. A conflict is resolved by hand, never worked around.
3. **Run the stage as `/land` §1 to §6 and its independent review:** the scout, the stage's spec set
   watched red, the code through implementer agents on the model each piece needs, the filtered specs
   green, then `code-review` on the stage's diff by an agent that did not write it. The plan's "Shown by"
   line is the red set's floor.
4. **Write no comments and no documentation.** Both are written once, in §3, on the plan's final shape.
5. **Commit locally, on `main`, and push nothing.** The commit holds the stage's code and spec cases and
   ticks the stage's box. Its message carries what §3's message will need: the mechanism, the cases red
   to green, the figures measured, and any `RELEASE_NOTES.md` entry the stage owes, written out.

Carry on from stage to stage without checking in. Stop only for a ruling, a contradiction with the plan,
or a red on `main` that is yours.

**A defect that is not this plan's** is recorded as `/land` §10 says, and fixed after the plan lands.
**Giving the plan up:** tell the user which stages are committed and where.

## 3. Land the plan once

When every box is ticked:

1. **Squash the stage commits into one commit** on top of `origin/main` (`git reset --soft origin/main`
   after a final rebase, keeping the stage messages for step 5). `main` takes one commit per landing; a
   branch of several is read as a bootstrap chain.
2. **`optimize` over the whole diff**, then **`documenter`**: the comments, the `docs/` sources the
   plan's surfaces own, the site regeneration, and every `## Unreleased` entry the stages recorded.
3. **Mark the plan's "Done" cell in `plans/README.md`.** The plan file stays, with every box ticked.
4. **`/land` §8's gates once**, over the whole plan. A plan changes far more than one stage did: run the
   whole `ir-specs` suite, not a filter, when any stage touched code generation.
5. **`/land` §9:** one commit whose message carries each stage's paragraph in order and ends
   `Fixes #<class issue>`, landed through the door.

A bounce at the door is `/land` §9's bounce: rebase by hand, re-take the gates whose inputs changed,
submit again.
