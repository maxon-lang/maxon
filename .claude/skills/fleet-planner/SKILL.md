---
name: fleet-planner
description: Where the maintainer's agent fleet records design rulings, and how a ruling made for it is read and worded — the `design` issues on the Maxon Roadmap, the Backlog, and each design's wiki page. Load it when the orchestrator dispatches the `planner` agent, and when a fleet worker needs the rulings that touch its task. Not for a session outside the fleet, whose rulings come from its plan file, the wiki and the user.
---

# Design rulings in the fleet

**A `design` issue on the Maxon Roadmap binds every fleet session, as a spec does.** The decided
direction, designs and release plan live on that GitHub project, and what is proposed but undecided on
the Maxon Backlog (`CONTRIBUTING.md`, "The roadmap"). A change that contradicts a ruling is a question on
its `design` issue, not an implementation choice.

## Reading

Read every `design` issue and every wiki `Design-*` page the question or the task touches before ruling,
planning or starting:

- `gh issue view <n> -R maxon-lang/maxon --comments`: the rulings are the issue's body and its comments;
- the wiki, cloned into your scratchpad (`git clone https://github.com/maxon-lang/maxon.wiki.git`): each
  design's page, whose `STATUS:` line says how much of it has landed.

Read as much as the answer needs; do not rule or plan on a summary.

## Wording a ruling

A ruling is worded to stand alone in a public `design` issue, for a contributor who never saw the
question: what is decided, the reason, what it rules out, and how it changes or extends any existing
ruling, by issue number. Name the issue it belongs in (an existing `design` issue, or the title of a new
one) and anything its wiki page must now say. A task in a plan names the issue it fixes.

A question goes to the user when the answer would set direction no `design` issue on the Roadmap
settles, or reverse one.

## Who writes

The planner changes nothing: the orchestrator records a ruling in its `design` issue and relays it. A
worker edits no board and no other design's wiki page (the `fleet-worker` skill). A plan page a worker
writes under `/plan-and-land` §4 is linked from its `design` issue, and the issue from the page, by the
orchestrator.

Nothing about the fleet goes on an issue or a wiki page: both are public.
