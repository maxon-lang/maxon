---
name: assign-task
description: The orchestrator's procedure for giving a free fleet worker its next task — whether it may take one (the account's budget), which issue it gets (priority, dependencies, areas, host), the planner's structural review, claiming the public issue, the fleet task issue's fields, and the ASSIGN message. Invoke as `/assign-task <worker id>` on a READY, after a DONE, or on a budget `start`.
---

# Assign a task to a worker

A task is one `/land` (or one `/plan-and-land`), never a slice of one. No two open tasks share an area,
and no issue is assigned twice.

## 1. May this worker take a task?

- Run `node .claude/fleet/fleet.mjs budget`. If the worker's account reads `pause`, start nothing on it:
  reply `PAUSE` and stop here. A worker already on a task finishes it. An account the user is burning
  down to a reset (a standing ruling on the fleet issue) is the exception: it takes work whatever its
  pacing says.
- `fleet.mjs send` enforces this: it refuses an `ASSIGN` to a worker whose account reads `pause`. Pass
  `--over-budget yes` only on the user's explicit word, such as the burn-down exception. A worker with no
  heartbeat yet needs `--account <label>`.
- Never reuse a long-running session the user has retired.

## 2. Which issue?

Take the highest priority first (`P0`, then `P1`, `P2`, `P3`; the Roadmap's `Priority` field). Within one
priority prefer:
1. work that unblocks other tasks;
2. the next milestone's open issues;
3. the oldest.

It must be:
- **on the Roadmap, `Ready` and unassigned.** Nothing on the Backlog is assigned; moving an issue from
  the Backlog to the Roadmap is the user's decision.
- **clear of every open task's area.** If the best candidate overlaps an active task, take the next one.
  If only overlapping work is left, park the worker, or the task, behind it (step 5's `Waits on:`).
- **runnable on this worker's host.** An OS-specific defect needs a worker on that OS. Otherwise the host
  only breaks ties when several workers are free: the heavier task goes to a desktop machine. Nothing a
  landing waits on goes to a host that may go offline. Never make work wait for a particular host.

## 3. Planner's structural review

Before you assign the issue, the `planner` examines it for its structural cause. Is it one instance of a
decision made in several places, or of a rule no single place owns? The planner designs the fix at that
one point, folds in the issue's siblings, and splits the work into tasks, each one `/land`, with each
task's area and dependencies.
- Close or retitle the sibling issues a plan absorbs.
- No issue skips this, however small its stated fix: the stated fix is often the per-instance patch.
- An issue the sweep already reviewed, and whose plan is recorded, needs no second review.

## 4. Claim it

- `gh issue edit <n> -R maxon-lang/maxon --add-assignee @me`. Never assign one someone else holds.
- `node .claude/roadmap/status.mjs <n> "In progress"` (it keeps the issue's priority and takes it off
  the Backlog).

## 5. Open the fleet task

Write the body in your scratchpad and run `gh issue create -R <fleet repository> --label task
--title "<worker>: <goal> (fixes maxon#<n>)" --body-file <path>`. The title's `<worker>: ` prefix is how
the panel and `fleet.mjs` find the owner. The body holds:

1. `Owner: <worker> (account)`;
2. the goal, one line;
3. the process: `/land <goal>`, or `/plan-and-land <problem>` when the approach still needs the worker's
   survey;
4. **the area**: the passes, directories or files it will change, as tightly as you can state them;
5. every Roadmap ruling it touches, quoted with its issue number;
6. what it must not touch, naming the tasks that own neighbouring areas;
7. `Waits on: #<task>, …` as the first line, if it is parked behind other tasks.

## 6. Send ASSIGN

Write the message to a file and send it with `fleet.mjs send --as orchestrator --to <worker> --file <path>`:

```
ASSIGN <task> /land maxon-lang/maxon#<n> — <one-line goal>

<the task body>

Split the /land into pieces and pick each piece's model (AGENTS.md). Heartbeat with --task <task>.
Test only what your change touches: `--filter`ed specs while you work, the §8 gates once at the end.
The full suite, the wasm lane and the tree corpora are CI's; a hook refuses an unfiltered run.
Push to a branch of your own if you cannot push main, then DONE <task> <sha> <branch>.
```

## Afterwards

- A `PROGRESS` that reports full-suite runs, repeated gate runs on an unchanged tree, or gates before the
  change is finished gets the testing line back.
- A task returned to open sets its issue back to `Ready` and unassigns it.
- A task moved to another worker is never retitled: close its fleet task issue (`not planned`, naming
  why) and open a fresh one for the new owner, so the panel's clock and the title's owner both start over.
- The fix's `Fixes #<n>` closes the issue, and `fleet.mjs land` closes the task and releases its waiters.
